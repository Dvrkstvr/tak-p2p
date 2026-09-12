using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace TakEngine.Core.Session;

public sealed class NtpTimeService : INtpTimeService
{
    private static readonly DateTime NtpEpoch = new(1900, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public static readonly string[] DefaultNtpServers =
    [
        "pool.ntp.org",
        "time.cloudflare.com",
        "time.google.com"
    ];

    private readonly string[] _servers;
    private readonly TimeSpan _syncInterval;
    private DateTime _lastSyncTime = DateTime.MinValue;

    public TimeSpan Offset { get; private set; } = TimeSpan.Zero;
    public bool IsSynchronized { get; private set; } = false;

    public NtpTimeService(string[]? servers = null, TimeSpan? syncInterval = null)
    {
        _servers = servers ?? DefaultNtpServers;
        _syncInterval = syncInterval ?? TimeSpan.FromHours(1);
    }

    public async Task<DateTime> GetUtcNowAsync(CancellationToken cancellationToken = default)
    {
        if (!IsSynchronized || DateTime.UtcNow - _lastSyncTime > _syncInterval)
        {
            await TrySynchronizeAsync(cancellationToken);
        }

        return DateTime.UtcNow + Offset;
    }

    public async Task<bool> TrySynchronizeAsync(CancellationToken cancellationToken = default)
    {
        foreach (string server in _servers)
        {
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(3000); // 3-second timeout per server

                DateTime networkUtc = await QueryServerAsync(server, cts.Token);
                Offset = networkUtc - DateTime.UtcNow;
                IsSynchronized = true;
                _lastSyncTime = DateTime.UtcNow;
                return true;
            }
            catch
            {
                // Try next server in pool
            }
        }

        // If all NTP queries fail, keep existing offset or fallback to zero
        return false;
    }

    private static async Task<DateTime> QueryServerAsync(string server, CancellationToken cancellationToken)
    {
        var ntpData = new byte[48];
        ntpData[0] = 0x1B; // LI = 0 (no warning), VN = 3 (IPv4 only), Mode = 3 (Client Mode)

        using var client = new UdpClient();
        var addresses = await Dns.GetHostAddressesAsync(server, cancellationToken);
        if (addresses.Length == 0)
            throw new SocketException((int)SocketError.HostNotFound);

        var ipEndPoint = new IPEndPoint(addresses[0], 123);
        await client.SendAsync(ntpData, ntpData.Length, ipEndPoint);

        var receiveResult = await client.ReceiveAsync(cancellationToken);
        return ParseNtpPacket(receiveResult.Buffer);
    }

    public static DateTime ParseNtpPacket(byte[] packet)
    {
        if (packet == null || packet.Length < 48)
            throw new ArgumentException("NTP packet must be at least 48 bytes.", nameof(packet));

        // Transmit Timestamp is at offset 40
        ulong intPart = ((ulong)packet[40] << 24) |
                        ((ulong)packet[41] << 16) |
                        ((ulong)packet[42] << 8) |
                        packet[43];

        ulong fractPart = ((ulong)packet[44] << 24) |
                          ((ulong)packet[45] << 16) |
                          ((ulong)packet[46] << 8) |
                          packet[47];

        double milliseconds = (intPart * 1000.0) + ((fractPart * 1000.0) / 0x100000000L);
        return NtpEpoch.AddMilliseconds(milliseconds);
    }
}
