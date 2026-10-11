using System.Net.WebSockets;
using System.Text;

namespace TakEngine.Transport.Relay;

/// <summary>
/// <see cref="IRelaySocket"/> over <see cref="ClientWebSocket"/>. Sets no <c>Options</c>: several are unsupported in browser-wasm
/// (pipeline/architecture.md "Principles").
/// </summary>
public sealed class ClientWebSocketRelaySocket(Uri relayUrl) : IRelaySocket
{
    private const int MaxFrameBytes = 1 << 20;

    private readonly ClientWebSocket _socket = new();

    public async Task ConnectAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _socket.ConnectAsync(relayUrl, cancellationToken).ConfigureAwait(false);
        }
        catch (WebSocketException ex)
        {
            int? status = _socket.HttpStatusCode == 0 ? null : (int)_socket.HttpStatusCode;
            throw new RelayConnectException($"Could not connect to {relayUrl}: {ex.Message}", status, ex);
        }
    }

    public async Task<bool> SendAsync(string frame, CancellationToken cancellationToken)
    {
        if (_socket.State != WebSocketState.Open)
            return false;
        try
        {
            await _socket.SendAsync(Encoding.UTF8.GetBytes(frame), WebSocketMessageType.Text, endOfMessage: true, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (WebSocketException)
        {
            return false;
        }
    }

    public async Task<string?> ReceiveAsync(CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[16 * 1024];
        using var message = new MemoryStream();
        try
        {
            while (true)
            {
                WebSocketReceiveResult result = await _socket.ReceiveAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (result.MessageType == WebSocketMessageType.Close)
                    return null;
                message.Write(buffer, 0, result.Count);
                if (message.Length > MaxFrameBytes)
                    return null; // a relay that floods one frame is dropped like a closed socket
                if (result.EndOfMessage)
                    return Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length);
            }
        }
        catch (WebSocketException)
        {
            return null;
        }
    }

    public async Task CloseAsync(CancellationToken cancellationToken)
    {
        if (_socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
        {
            try
            {
                await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", cancellationToken).ConfigureAwait(false);
            }
            catch (WebSocketException)
            {
                // already gone
            }
        }
    }

    public ValueTask DisposeAsync()
    {
        _socket.Dispose();
        return ValueTask.CompletedTask;
    }
}

public sealed class ClientWebSocketRelaySocketFactory : IRelaySocketFactory
{
    public IRelaySocket Create(Uri relayUrl) => new ClientWebSocketRelaySocket(relayUrl);
}
