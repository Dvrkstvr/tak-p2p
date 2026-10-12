using TakEngine.Transport.Nostr;
using TakEngine.Transport.Relay;

namespace TakEngine.Testing;

/// <summary>Hands out sockets connected to <see cref="InMemoryRelay"/>s by URL. An unknown URL behaves like an unreachable host.</summary>
public sealed class FakeRelaySocketFactory(params InMemoryRelay[] relays) : IRelaySocketFactory
{
    private readonly Dictionary<Uri, InMemoryRelay> _relays = relays.ToDictionary(r => r.Url);

    public List<FakeRelaySocket> Created { get; } = [];

    public IRelaySocket Create(Uri relayUrl)
    {
        _relays.TryGetValue(relayUrl, out InMemoryRelay? relay);
        var socket = new FakeRelaySocket(relayUrl, relay);
        lock (Created) Created.Add(socket);
        return socket;
    }
}

public sealed class FakeRelaySocket(Uri url, InMemoryRelay? relay) : IRelaySocket
{
    private InMemoryRelay.Link? _link;

    public Uri Url { get; } = url;

    public Task ConnectAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (relay is null)
            throw new RelayConnectException($"{Url}: name not resolved.");
        _link = relay.Accept();
        return Task.CompletedTask;
    }

    public Task<bool> SendAsync(string frame, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_link is not null && _link.Receive(frame));
    }

    public async Task<string?> ReceiveAsync(CancellationToken cancellationToken)
    {
        if (_link is null)
            return null;
        try
        {
            return await _link.Outbound.ReadAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (System.Threading.Channels.ChannelClosedException)
        {
            return null;
        }
    }

    /// <summary>Every frame already waiting, without blocking (tests that drive the relay directly).</summary>
    public List<RelayMessage> Drain()
    {
        var frames = new List<RelayMessage>();
        while (_link is not null && _link.Outbound.TryRead(out string? frame))
            frames.Add(RelayMessage.Parse(frame));
        return frames;
    }

    public Task CloseAsync(CancellationToken cancellationToken)
    {
        _link?.Close();
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        _link?.Close();
        return ValueTask.CompletedTask;
    }
}
