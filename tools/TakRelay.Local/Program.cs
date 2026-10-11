using TakRelay.Local;

LocalRelayOptions? options = LocalRelayOptions.Parse(args, out string? error);
if (options is null)
{
    Console.Error.WriteLine(error);
    return 1;
}

await using LocalRelayHost host = await LocalRelayHost.StartAsync(options);
foreach (Uri url in host.Urls)
    Console.WriteLine($"relay {url}");
Console.WriteLine("Ctrl+C stops the relays (everything they hold is forgotten).");
await Task.Delay(Timeout.Infinite, host.Stopping).ContinueWith(_ => { }, TaskScheduler.Default);
return 0;
