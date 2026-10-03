using System.Reflection;
using System.Text.Json;
using TakEngine.Crypto;
using TakEngine.Transport.Nostr;

namespace TakEngine.Transport.Tests;

/// <summary>F-032: every event received from a relay is verified (id + signature) before it is decrypted or parsed.</summary>
public class NostrTransportClientReceiveTests
{
    private static readonly SecretKey Alice = SecretKey.FromHex("b7e151628aed2a6abf7158809cf4f3c762e7160f38b4da56a784d9045190cfef");
    private static readonly SecretKey Bob = SecretKey.FromHex("c90fdaa22168c234c4c6628b80dc1cd129024e088a67cc74020bbea63b14e5c9");

    private static readonly TransportEnvelope Envelope = new(
        Guid.Parse("0b7e3d4c-1f2a-4b5c-8d9e-0f1a2b3c4d5e"), 1, Alice.PublicKey.ToHex(), new string('0', 64),
        new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), "move", new ActionData("a1"), "sig");

    /// <summary>Bob's client; no sockets are opened (the relay is never connected).</summary>
    private static (NostrTransportClient Client, List<TransportEnvelope> Envelopes, List<string> Profiles) BobsClient()
    {
        var client = new NostrTransportClient(Bob.PublicKey.ToHex(), Bob.ToHex(), ["wss://relay.invalid"]);
        var envelopes = new List<TransportEnvelope>();
        var profiles = new List<string>();
        client.OnMoveEnvelopeReceived += envelopes.Add;
        client.OnProfileReceived += (pubkey, _) => profiles.Add(pubkey);
        return (client, envelopes, profiles);
    }

    /// <summary>Delivers an event the way a relay connection does (the connection's event can only be raised from inside it).</summary>
    private static void Deliver(NostrTransportClient client, NostrEvent evt)
    {
        NostrRelayConnection relay = client.Relays[0];
        FieldInfo field = typeof(NostrRelayConnection).GetField(nameof(NostrRelayConnection.OnMessageReceived), BindingFlags.Instance | BindingFlags.NonPublic)!;
        var handler = (Action<NostrRelayConnection, NostrRelayMessage>?)field.GetValue(relay);
        handler!(relay, new EventRelayMessage("sub", evt));
    }

    private static NostrEvent SignedMove()
    {
        string content = Nip44.Encrypt(JsonSerializer.Serialize(Envelope), Nip44.ConversationKey(Alice, Bob.PublicKey), Enumerable.Range(1, Nip44.NonceLength).Select(i => (byte)i).ToArray());
        return NostrEvents.Sign(Alice, 1_700_000_000, 4, [["p", Bob.PublicKey.ToHex()], ["g", Envelope.GameId.ToString()]], content, new byte[32]);
    }

    private static NostrEvent SignedProfile() =>
        NostrEvents.Sign(Alice, 1_700_000_000, 0, [], """{"name":"alice"}""", new byte[32]);

    [Fact]
    public void ProperlySignedMove_IsSurfaced()
    {
        var (client, envelopes, _) = BobsClient();

        Deliver(client, SignedMove());

        Assert.Equal(Envelope.GameId, Assert.Single(envelopes).GameId);
    }

    [Fact]
    public void ProperlySignedProfile_IsSurfaced()
    {
        var (client, _, profiles) = BobsClient();

        Deliver(client, SignedProfile());

        Assert.Equal(Alice.PublicKey.ToHex(), Assert.Single(profiles));
    }

    [Theory]
    [InlineData("id")]
    [InlineData("sig")]
    [InlineData("unsigned")]
    [InlineData("other-author")]
    public void MoveFailingVerification_IsDropped(string tamper)
    {
        var (client, envelopes, _) = BobsClient();
        NostrEvent evt = SignedMove();
        switch (tamper)
        {
            case "id": evt.Id = (evt.Id[0] == '0' ? "1" : "0") + evt.Id[1..]; break;
            case "sig": evt.Sig = (evt.Sig[0] == '0' ? "1" : "0") + evt.Sig[1..]; break;
            case "unsigned": evt.Id = ""; evt.Sig = ""; break;
            case "other-author": evt.Pubkey = Bob.PublicKey.ToHex(); break;
        }

        Deliver(client, evt);

        Assert.Empty(envelopes);
    }

    [Fact]
    public void ProfileFailingVerification_IsDropped()
    {
        var (client, _, profiles) = BobsClient();
        NostrEvent evt = SignedProfile();
        evt.Sig = new string('0', 128);

        Deliver(client, evt);

        Assert.Empty(profiles);
    }

    [Fact]
    public void JunkEvent_IsDroppedWithoutAnExceptionEscaping()
    {
        var (client, envelopes, profiles) = BobsClient();
        NostrEvent junk = JsonSerializer.Deserialize<NostrEvent>("""{"id":null,"pubkey":null,"created_at":1,"kind":4,"tags":null,"content":null,"sig":null}""")!;

        Deliver(client, junk);

        Assert.Empty(envelopes);
        Assert.Empty(profiles);
    }

    [Fact]
    public void AnEventThatFailedVerification_DoesNotBlockTheGenuineEventWithTheSameId()
    {
        var (client, envelopes, _) = BobsClient();
        NostrEvent genuine = SignedMove();
        NostrEvent forged = JsonSerializer.Deserialize<NostrEvent>(JsonSerializer.Serialize(genuine))!;
        forged.Sig = new string('0', 128);

        Deliver(client, forged);
        Deliver(client, genuine);

        Assert.Single(envelopes);
    }
}
