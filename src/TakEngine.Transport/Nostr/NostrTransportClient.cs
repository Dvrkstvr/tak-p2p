using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace TakEngine.Transport.Nostr;

public sealed class NostrTransportClient : IAsyncDisposable
{
    private readonly List<NostrRelayConnection> _relays = new();
    private readonly ConcurrentDictionary<string, byte> _processedEventIds = new();
    private readonly string _localPubKey;
    private readonly string _localPrivKey;

    public static readonly string[] DefaultRelays =
    [
        "wss://relay.damus.io",
        "wss://nos.lol",
        "wss://relay.primal.net"
    ];

    public event Action<TransportEnvelope>? OnMoveEnvelopeReceived;
    public event Action<string, NostrProfile>? OnProfileReceived;
    public event Action<string>? OnStatusMessage;

    public IReadOnlyList<NostrRelayConnection> Relays => _relays.AsReadOnly();

    public NostrTransportClient(
        string localPubKey,
        string localPrivKey,
        IEnumerable<string>? relayUrls = null)
    {
        _localPubKey = localPubKey;
        _localPrivKey = localPrivKey;

        var urls = relayUrls ?? DefaultRelays;
        foreach (string url in urls)
        {
            var relay = new NostrRelayConnection(url);
            relay.OnMessageReceived += HandleRelayMessage;
            relay.OnStateChanged += (conn, state, detail) =>
            {
                OnStatusMessage?.Invoke($"[{conn.RelayUri.Host}] {state}: {detail}");
            };
            _relays.Add(relay);
        }
    }

    public async Task ConnectAllAsync(CancellationToken cancellationToken = default)
    {
        var tasks = new List<Task>();
        foreach (var relay in _relays)
        {
            tasks.Add(Task.Run(async () =>
            {
                try
                {
                    await relay.ConnectAsync(cancellationToken);
                }
                catch (Exception ex)
                {
                    OnStatusMessage?.Invoke($"Failed to connect to {relay.RelayUri.Host}: {ex.Message}");
                }
            }, cancellationToken));
        }

        await Task.WhenAll(tasks);
    }

    public async Task SubscribeIncomingMovesAsync(string subId = "tak_moves", CancellationToken cancellationToken = default)
    {
        var filter = new NostrFilter
        {
            Kinds = [4],
            TagP = [_localPubKey],
            Limit = 50
        };

        foreach (var relay in _relays)
        {
            if (relay.State == RelayConnectionState.Connected)
            {
                try
                {
                    await relay.SubscribeAsync(subId, filter, cancellationToken);
                }
                catch (Exception ex)
                {
                    OnStatusMessage?.Invoke($"Subscription failed on {relay.RelayUri.Host}: {ex.Message}");
                }
            }
        }
    }

    public async Task<int> SendMoveEnvelopeAsync(
        string recipientPubKey,
        TransportEnvelope envelope,
        CancellationToken cancellationToken = default)
    {
        string json = JsonSerializer.Serialize(envelope);
        byte[] sharedSecret = Nip44Encryption.DeriveSharedSecret(_localPrivKey, recipientPubKey);
        string encryptedContent = Nip44Encryption.Encrypt(json, sharedSecret);

        var evt = new NostrEvent
        {
            Pubkey = _localPubKey,
            CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            Kind = 4,
            Tags =
            [
                ["p", recipientPubKey],
                ["g", envelope.GameId.ToString()]
            ],
            Content = encryptedContent
        };
        evt.Id = evt.ComputeId();

        int publishedCount = 0;
        foreach (var relay in _relays)
        {
            if (relay.State == RelayConnectionState.Connected)
            {
                try
                {
                    await relay.SendEventAsync(evt, cancellationToken);
                    publishedCount++;
                }
                catch (Exception ex)
                {
                    OnStatusMessage?.Invoke($"Failed to publish move to {relay.RelayUri.Host}: {ex.Message}");
                }
            }
        }

        return publishedCount;
    }

    public async Task<int> PublishProfileAsync(
        NostrProfile profile,
        CancellationToken cancellationToken = default)
    {
        var evt = NostrProfile.CreateMetadataEvent(_localPubKey, profile);
        int publishedCount = 0;
        foreach (var relay in _relays)
        {
            if (relay.State == RelayConnectionState.Connected)
            {
                try
                {
                    await relay.SendEventAsync(evt, cancellationToken);
                    publishedCount++;
                }
                catch (Exception ex)
                {
                    OnStatusMessage?.Invoke($"Failed to publish profile to {relay.RelayUri.Host}: {ex.Message}");
                }
            }
        }

        return publishedCount;
    }

    public async Task SubscribeProfileAsync(
        string targetPubKey,
        string subId = "tak_profile",
        CancellationToken cancellationToken = default)
    {
        var filter = new NostrFilter
        {
            Kinds = [0],
            Authors = [targetPubKey],
            Limit = 1
        };

        foreach (var relay in _relays)
        {
            if (relay.State == RelayConnectionState.Connected)
            {
                try
                {
                    await relay.SubscribeAsync(subId, filter, cancellationToken);
                }
                catch (Exception ex)
                {
                    OnStatusMessage?.Invoke($"Profile subscription failed on {relay.RelayUri.Host}: {ex.Message}");
                }
            }
        }
    }

    private void HandleRelayMessage(NostrRelayConnection conn, NostrRelayMessage message)
    {
        if (message is EventRelayMessage eventMsg)
        {
            var evt = eventMsg.Event;

            // Handle Kind 0 Metadata (Profile / Nickname)
            if (evt.Kind == 0)
            {
                var profile = NostrProfile.Parse(evt.Content);
                OnProfileReceived?.Invoke(evt.Pubkey, profile);
                return;
            }

            if (evt.Kind != 4)
                return;

            if (!_processedEventIds.TryAdd(evt.Id, 0))
                return; // Already processed

            try
            {
                byte[] sharedSecret = Nip44Encryption.DeriveSharedSecret(_localPrivKey, evt.Pubkey);
                string decryptedJson = Nip44Encryption.Decrypt(evt.Content, sharedSecret);

                var envelope = JsonSerializer.Deserialize<TransportEnvelope>(decryptedJson);
                if (envelope != null)
                {
                    OnMoveEnvelopeReceived?.Invoke(envelope);
                }
            }
            catch (Exception ex)
            {
                OnStatusMessage?.Invoke($"Failed to decrypt or parse envelope from {evt.Pubkey[..8]}: {ex.Message}");
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var relay in _relays)
        {
            await relay.DisposeAsync();
        }
        _relays.Clear();
    }
}
