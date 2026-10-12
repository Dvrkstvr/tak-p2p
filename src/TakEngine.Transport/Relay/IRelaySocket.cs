namespace TakEngine.Transport.Relay;

/// <summary>
/// One WebSocket to one relay, text frames only (pipeline/architecture.md "Seams"). Real: <see cref="ClientWebSocketRelaySocket"/>;
/// tests: the in-memory relay's fake socket. Implementations are used by one reader and one writer at a time.
/// </summary>
public interface IRelaySocket : IAsyncDisposable
{
    /// <summary>Opens the socket. Throws <see cref="RelayConnectException"/> when the relay can't be reached or refuses the upgrade.</summary>
    Task ConnectAsync(CancellationToken cancellationToken);

    /// <summary>Sends one text frame. False when the socket is already closed (the reader sees null too); never throws for that.</summary>
    Task<bool> SendAsync(string frame, CancellationToken cancellationToken);

    /// <summary>The next text frame, or null once the socket is closed (by either side or a drop).</summary>
    Task<string?> ReceiveAsync(CancellationToken cancellationToken);

    Task CloseAsync(CancellationToken cancellationToken);
}

public interface IRelaySocketFactory
{
    IRelaySocket Create(Uri relayUrl);
}

/// <summary>The relay could not be reached, or answered the upgrade with an HTTP status (503 counts as a retry, F-034).</summary>
public sealed class RelayConnectException(string message, int? httpStatus = null, Exception? inner = null) : Exception(message, inner)
{
    public int? HttpStatus { get; } = httpStatus;
}
