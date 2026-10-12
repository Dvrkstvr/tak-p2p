// R-001 spike peer: one OS process = one player with its own secp256k1 key.
// Plays alternating "move" messages (TransportEnvelope-like JSON, NIP-44 v2 encrypted) through one or more Nostr relays.
// Throwaway spike code. Uses Spike.Peer.NostrCrypto (copy of the R-002/R-003 spike's NostrCrypto.cs; NBitcoin.Secp256k1 + NIP-44 v2).
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Spike.Peer;

public static class Program
{
    static readonly Dictionary<string, string> A = new();
    static string Arg(string k, string d = "") => A.TryGetValue(k, out var v) ? v : d;
    static long NowMs() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    static StreamWriter? logFile;
    static readonly object logLock = new();
    static void Log(string ev, object? data = null)
    {
        var o = new JsonObject { ["t_ms"] = NowMs(), ["who"] = Arg("name", "?"), ["ev"] = ev };
        if (data is not null) o["d"] = JsonSerializer.SerializeToNode(data);
        var line = o.ToJsonString();
        lock (logLock) { Console.WriteLine(line); logFile?.WriteLine(line); logFile?.Flush(); }
    }

    // ---- state persisted between restarts ----
    sealed class State { public int Applied { get; set; } public string Hash { get; set; } = ""; public long MaxCreatedAt { get; set; } public int Published { get; set; } }
    static State st = new();
    static string statePath = "";
    static void SaveState() { if (statePath != "") File.WriteAllText(statePath, JsonSerializer.Serialize(st)); }

    static NostrKey key = null!; static string peerPub = "", game = "", myPub = ""; static byte[] conv = null!;
    static int totalTurns, kind, delayMs, dieAfterPublish; static bool first, replay;
    static readonly Dictionary<int, (string ptn, string prevHash, long sentMs, string evId, string author)> pending = new();
    static readonly HashSet<string> seenIds = new();
    static readonly object gate = new();
    static readonly List<Relay> relays = new();
    static int arrivalOutOfOrder, dropped, dupes;
    static readonly TaskCompletionSource done = new();
    static int eoseCount, failedPublishes;

    public static async Task<int> Main(string[] args)
    {
        for (int i = 0; i < args.Length; i++) if (args[i].StartsWith("--")) A[args[i][2..]] = i + 1 < args.Length && !args[i + 1].StartsWith("--") ? args[++i] : "true";
        if (A.ContainsKey("gen-key")) { var k0 = NostrKey.Generate(); File.WriteAllText(Arg("gen-key"), Convert.ToHexStringLower(k0.Secret)); Console.WriteLine(k0.PubHex); return 0; }
        var keyPath = Arg("key"); key = new NostrKey(Convert.FromHexString(File.ReadAllText(keyPath).Trim())); myPub = key.PubHex;
        peerPub = Arg("peer"); game = Arg("game"); totalTurns = int.Parse(Arg("turns", "12")); kind = int.Parse(Arg("kind", "9999"));
        delayMs = int.Parse(Arg("delay-ms", "300")); dieAfterPublish = int.Parse(Arg("die-after-publish", "0")); first = Arg("role") == "first"; replay = A.ContainsKey("replay");
        var since0 = long.Parse(Arg("since", "0"));
        statePath = Arg("state"); if (statePath != "" && File.Exists(statePath)) st = JsonSerializer.Deserialize<State>(File.ReadAllText(statePath)) ?? new();
        var lf = Arg("log"); if (lf != "") logFile = new StreamWriter(lf, append: true);
        conv = key.ConversationKey(Convert.FromHexString(peerPub));
        Log("start", new { myPub, peerPub, game, role = first ? "first" : "second", kind, replay, resumedFromState = st.Applied > 0 || st.Published > 0, st.Applied, st.Published, st.MaxCreatedAt });
        long since = st.MaxCreatedAt > 0 ? st.MaxCreatedAt - 30 : since0;   // -30 s slack for created_at granularity/skew; dedupe by event id
        foreach (var url in Arg("relay", "ws://127.0.0.1:7777").Split(',')) { var r = new Relay(url); relays.Add(r); _ = r.RunAsync(since); }
        if (!replay) _ = Task.Run(() => MaybePlayAsync());
        var timeout = Task.Delay(int.Parse(Arg("timeout-s", "120")) * 1000);
        var fin = await Task.WhenAny(done.Task, timeout);
        Log(fin == done.Task ? "DONE" : "TIMEOUT", new { st.Applied, st.Published, finalHash = st.Hash, arrivalOutOfOrder, dupes, dropped });
        foreach (var r in relays) r.Close();
        return fin == done.Task ? 0 : 2;
    }

    // ---- game logic ----
    static string NextHash(string prev, int turn, string pub, string ptn) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(prev + "|" + turn + "|" + pub + "|" + ptn)));
    static string PtnFor(int turn) => "m" + turn;   // fake but deterministic move text
    static bool MyTurn(int turn) => first ? turn % 2 == 1 : turn % 2 == 0;

    static void OnEvent(string relayUrl, JsonElement ev, long recvMs)
    {
        string id = ev.GetProperty("id").GetString()!;
        var e = new NostrEventLite { Id = id, Pubkey = ev.GetProperty("pubkey").GetString()!, CreatedAt = ev.GetProperty("created_at").GetInt64(), Kind = ev.GetProperty("kind").GetInt32(), Content = ev.GetProperty("content").GetString()!, Sig = ev.GetProperty("sig").GetString()! };
        foreach (var t in ev.GetProperty("tags").EnumerateArray()) e.Tags.Add(t.EnumerateArray().Select(x => x.GetString()!).ToList());
        lock (gate)
        {
            if (!seenIds.Add(id)) { dupes++; return; }
            if (!e.Verify()) { dropped++; Log("DROP bad-sig-or-id", new { id }); return; }
            if (e.Pubkey != peerPub && e.Pubkey != myPub) { dropped++; Log("DROP wrong-author", new { id, author = e.Pubkey }); return; }
            JsonNode? p; try { p = JsonNode.Parse(Nip44.Decrypt(e.Content, conv)); } catch (Exception ex) { dropped++; Log("DROP decrypt-fail", new { id, ex.Message }); return; }
            int turn = p!["turn"]!.GetValue<int>(); long sentMs = p["sent_ms"]!.GetValue<long>();
            bool mine = e.Pubkey == myPub;
            if (mine && turn <= st.Applied) return;   // echo of my own published move
            Log(mine ? "RECV-OWN (rehydrate)" : "RECV", new { relay = relayUrl, turn, id = id[..12], latency_ms = recvMs - sentMs, created_at = e.CreatedAt, expected_next = st.Applied + 1 });
            if (turn != st.Applied + 1) arrivalOutOfOrder++;
            if (turn <= st.Applied) { dupes++; return; }
            if (e.CreatedAt > st.MaxCreatedAt) st.MaxCreatedAt = e.CreatedAt;
            pending[turn] = (p["action_data"]!["ptn"]!.GetValue<string>(), p["prev_state_hash"]!.GetValue<string>(), sentMs, id, e.Pubkey);
            // apply in turn order, verifying hash chain + turn ownership
            while (pending.TryGetValue(st.Applied + 1, out var m))
            {
                int t = st.Applied + 1;
                var owner = MyTurn(t) ? myPub : peerPub;
                if (m.author != owner) { dropped++; Log("DROP wrong-turn-owner", new { t, author = m.author }); pending.Remove(t); break; }
                if (m.prevHash != st.Hash) { dropped++; Log("DROP hash-chain-mismatch", new { t, expected = st.Hash, got = m.prevHash }); pending.Remove(t); break; }
                st.Hash = NextHash(st.Hash, t, owner, m.ptn); st.Applied = t; pending.Remove(t); SaveState();
                Log("APPLIED", new { turn = t, hash = st.Hash[..12], by = owner == myPub ? "me(from relay)" : "peer" });
            }
        }
        _ = MaybePlayAsync();
    }

    static readonly SemaphoreSlim playLock = new(1, 1);
    static async Task MaybePlayAsync()
    {
        if (replay) return;   // BUG FIX: replay mode must never publish (first runs published one stray duplicate 'turn 2' each)
        await playLock.WaitAsync();
        try
        {
            while (true)
            {
                int n; string prev;
                lock (gate)
                {
                    if (st.Applied >= totalTurns) { done.TrySetResult(); return; }
                    n = st.Applied + 1; prev = st.Hash;
                    if (!MyTurn(n)) return;
                    if (st.Published >= n) { if (st.Applied + 1 == n) { } return; }   // already published this turn (waiting for peer)
                }
                await Task.Delay(delayMs);
                var ptn = PtnFor(n);
                var env = new JsonObject
                {
                    ["game_id"] = game, ["turn"] = n, ["player_pubkey"] = myPub, ["prev_state_hash"] = prev,
                    ["timestamp_utc"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"), ["action_type"] = "MOVE",
                    ["action_data"] = new JsonObject { ["ptn"] = ptn }, ["sent_ms"] = NowMs()
                };
                var ev = new NostrEventLite { Kind = kind, CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds(), Content = Nip44.Encrypt(env.ToJsonString(), conv) };
                ev.Tags.Add(["p", peerPub]); ev.Tags.Add(["g", game]); ev.Tags.Add(["d", game + ":" + n]);
                ev.Sign(key);
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var oks = await Task.WhenAll(relays.Select(r => r.PublishAsync(ev)));
                Log("PUBLISHED", new { turn = n, id = ev.Id, kind, relaysTried = relays.Count, bytes = ev.ToJson().Length, ok_ms = sw.ElapsedMilliseconds, results = oks });
                if (oks.All(o => o.StartsWith("ok:false") || o.StartsWith("timeout"))) { Log("PUBLISH-FAILED-EVERYWHERE", new { n }); if (++failedPublishes >= 2) { Log("ABORT after 2 failed publishes (event budget)"); done.TrySetResult(); return; } await Task.Delay(1000); continue; }
                lock (gate)
                {
                    st.Published = n;
                    if (st.Applied < n) { st.Hash = NextHash(prev, n, myPub, ptn); st.Applied = n; Log("APPLIED", new { turn = n, hash = st.Hash[..12], by = "me" }); }   // relay echo may already have applied it (and the peer may already have replied)
                    SaveState();
                }
                if (dieAfterPublish == n) { Log("SIMULATED-KILL (self kill -9 after publishing turn " + n + ")"); Environment.FailFast("die-after-publish"); }
            }
        }
        finally { playLock.Release(); }
    }

    // ---- relay connection ----
    sealed class Relay
    {
        readonly string url; ClientWebSocket? ws; readonly SemaphoreSlim sendLock = new(1, 1);
        readonly Dictionary<string, TaskCompletionSource<string>> oks = new(); volatile bool stop;
        public Relay(string u) { url = u; }
        public void Close() { stop = true; try { ws?.Abort(); } catch { } }
        async Task SendAsync(string s) { await sendLock.WaitAsync(); try { await ws!.SendAsync(Encoding.UTF8.GetBytes(s), WebSocketMessageType.Text, true, CancellationToken.None); } finally { sendLock.Release(); } }
        public async Task<string> PublishAsync(NostrEventLite ev)
        {
            for (int i = 0; i < 40 && (ws is null || ws.State != WebSocketState.Open); i++) await Task.Delay(250);
            if (ws is null || ws.State != WebSocketState.Open) return "timeout:not-connected";
            var tcs = new TaskCompletionSource<string>(); lock (oks) oks[ev.Id] = tcs;
            await SendAsync("[\"EVENT\"," + ev.ToJson() + "]");
            var w = await Task.WhenAny(tcs.Task, Task.Delay(10000));
            return w == tcs.Task ? tcs.Task.Result : "timeout:no-OK";
        }
        public async Task RunAsync(long since)
        {
            int attempt = 0;
            while (!stop)
            {
                try
                {
                    ws = new ClientWebSocket(); var t0 = NowMs();
                    await ws.ConnectAsync(new Uri(url), CancellationToken.None);
                    Log("relay-connected", new { url, ms = NowMs() - t0, attempt }); attempt = 0;
                    long s; lock (gate) s = st.MaxCreatedAt > 0 ? st.MaxCreatedAt - 30 : since;
                    // two filters: peer's moves addressed to me, and my own moves addressed to the peer (needed to rehydrate the full hash chain after state loss)
                    var f1 = new JsonObject { ["kinds"] = new JsonArray(kind), ["authors"] = new JsonArray(peerPub), ["#p"] = new JsonArray(myPub), ["#g"] = new JsonArray(game), ["since"] = s, ["limit"] = 500 };
                    var f2 = new JsonObject { ["kinds"] = new JsonArray(kind), ["authors"] = new JsonArray(myPub), ["#p"] = new JsonArray(peerPub), ["#g"] = new JsonArray(game), ["since"] = s, ["limit"] = 500 };
                    await SendAsync("[\"REQ\",\"game\"," + f1.ToJsonString() + "," + f2.ToJsonString() + "]");
                    Log("REQ-sent", new { url, since = s });
                    var buf = new byte[1 << 16];
                    while (!stop && ws.State == WebSocketState.Open)
                    {
                        var sb = new StringBuilder(); WebSocketReceiveResult r;
                        do { r = await ws.ReceiveAsync(buf, CancellationToken.None); if (r.MessageType == WebSocketMessageType.Close) throw new Exception("server closed"); sb.Append(Encoding.UTF8.GetString(buf, 0, r.Count)); } while (!r.EndOfMessage);
                        var recv = NowMs();
                        using var doc = JsonDocument.Parse(sb.ToString()); var root = doc.RootElement; var typ = root[0].GetString();
                        if (typ == "EVENT") OnEvent(url, root[2], recv);
                        else if (typ == "EOSE") { Log("EOSE", new { url }); if (replay) { Interlocked.Increment(ref eoseCount); await Task.Delay(500); lock (gate) { done.TrySetResult(); } } }
                        else if (typ == "OK") { lock (oks) if (oks.Remove(root[1].GetString()!, out var t)) t.TrySetResult((root[2].GetBoolean() ? "ok:true" : "ok:false") + ":" + root[3].GetString()); }
                        else Log("relay-msg-" + typ, new { url, raw = sb.ToString().Length > 300 ? sb.ToString()[..300] : sb.ToString() });
                    }
                }
                catch (Exception ex) { if (stop) return; Log("relay-error", new { url, ex.Message }); }
                if (stop) return; attempt++; await Task.Delay(Math.Min(15000, 500 * attempt * attempt));
            }
        }
    }
}
