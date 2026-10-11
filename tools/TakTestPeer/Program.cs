using TakEngine.Transport.Relay;
using TakTestPeer;

return await TestPeer.RunAsync(args, new ClientWebSocketRelaySocketFactory(), TimeProvider.System, Console.Out, Console.Error);
