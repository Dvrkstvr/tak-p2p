using System;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TakEngine.Transport.Nostr;

public enum RelayConnectionState
{
    Disconnected,
    Connecting,
    Connected,
    Reconnecting,
    Failed
}

public sealed class NostrRelayConnection : IAsyncDisposable
{
    private readonly Uri _relayUri;
    private ClientWebSocket? _webSocket;
    private CancellationTokenSource? _cts;
    private Task? _receiveTask;

    public Uri RelayUri => _relayUri;
    public RelayConnectionState State { get; private set; } = RelayConnectionState.Disconnected;

    public event Action<NostrRelayConnection, NostrRelayMessage>? OnMessageReceived;
    public event Action<NostrRelayConnection, RelayConnectionState, string?>? OnStateChanged;

    public NostrRelayConnection(string relayUrl)
    {
        _relayUri = new Uri(relayUrl);
    }

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (State == RelayConnectionState.Connected)
            return;

        SetState(RelayConnectionState.Connecting, "Connecting to relay...");

        try
        {
            _cts = new CancellationTokenSource();
            _webSocket = new ClientWebSocket();
            await _webSocket.ConnectAsync(_relayUri, cancellationToken);

            SetState(RelayConnectionState.Connected, "Connected");
            _receiveTask = Task.Run(() => ReceiveLoopAsync(_cts.Token));
        }
        catch (Exception ex)
        {
            SetState(RelayConnectionState.Failed, ex.Message);
            throw;
        }
    }

    public async Task SendAsync(string message, CancellationToken cancellationToken = default)
    {
        if (_webSocket == null || _webSocket.State != WebSocketState.Open)
            throw new InvalidOperationException($"Relay {_relayUri} is not connected.");

        byte[] buffer = Encoding.UTF8.GetBytes(message);
        await _webSocket.SendAsync(
            new ArraySegment<byte>(buffer),
            WebSocketMessageType.Text,
            true,
            cancellationToken);
    }

    public async Task SendEventAsync(NostrEvent evt, CancellationToken cancellationToken = default)
    {
        string msg = NostrMessageParser.SerializeEvent(evt);
        await SendAsync(msg, cancellationToken);
    }

    public async Task SubscribeAsync(string subId, NostrFilter filter, CancellationToken cancellationToken = default)
    {
        string msg = NostrMessageParser.SerializeReq(subId, filter);
        await SendAsync(msg, cancellationToken);
    }

    public async Task UnsubscribeAsync(string subId, CancellationToken cancellationToken = default)
    {
        string msg = NostrMessageParser.SerializeClose(subId);
        await SendAsync(msg, cancellationToken);
    }

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        var buffer = new byte[8192];
        using var ms = new MemoryStream();

        try
        {
            while (!ct.IsCancellationRequested && _webSocket != null && _webSocket.State == WebSocketState.Open)
            {
                ms.SetLength(0);
                WebSocketReceiveResult result;

                do
                {
                    result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await DisconnectAsync();
                        return;
                    }

                    ms.Write(buffer, 0, result.Count);
                } while (!result.EndOfMessage);

                string text = Encoding.UTF8.GetString(ms.ToArray());
                var message = NostrMessageParser.Parse(text);
                if (message != null)
                {
                    OnMessageReceived?.Invoke(this, message);
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            SetState(RelayConnectionState.Failed, ex.Message);
        }
    }

    public async Task DisconnectAsync()
    {
        if (_cts != null)
        {
            _cts.Cancel();
            _cts.Dispose();
            _cts = null;
        }

        if (_webSocket != null)
        {
            if (_webSocket.State == WebSocketState.Open)
            {
                try
                {
                    await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Client disconnect", CancellationToken.None);
                }
                catch { }
            }
            _webSocket.Dispose();
            _webSocket = null;
        }

        SetState(RelayConnectionState.Disconnected, "Disconnected");
    }

    private void SetState(RelayConnectionState newState, string? detail = null)
    {
        State = newState;
        OnStateChanged?.Invoke(this, newState, detail);
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync();
    }
}
