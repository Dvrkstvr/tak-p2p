using System;
using System.Threading;
using System.Threading.Tasks;
using TakEngine.Core.Session;
using Xunit;

namespace TakEngine.Core.Tests;

public class MockNtpTimeService : INtpTimeService
{
    public DateTime CurrentTimeUtc { get; set; }
    public TimeSpan Offset => CurrentTimeUtc - DateTime.UtcNow;
    public bool IsSynchronized => true;

    public MockNtpTimeService(DateTime initialUtc)
    {
        CurrentTimeUtc = initialUtc;
    }

    public Task<DateTime> GetUtcNowAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(CurrentTimeUtc);
    }
}

public class NtpTimeTests
{
    [Fact]
    public void ParseNtpPacket_ParsesKnownTimestampCorrectly()
    {
        // Construct a synthetic 48-byte NTP packet
        var packet = new byte[48];
        packet[0] = 0x1B;

        // Target: 2026-09-12 00:00:00 UTC
        var targetUtc = new DateTime(2026, 9, 12, 0, 0, 0, DateTimeKind.Utc);
        var epoch = new DateTime(1900, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        ulong totalSeconds = (ulong)(targetUtc - epoch).TotalSeconds;

        // Write big-endian seconds into bytes 40-43
        packet[40] = (byte)((totalSeconds >> 24) & 0xFF);
        packet[41] = (byte)((totalSeconds >> 16) & 0xFF);
        packet[42] = (byte)((totalSeconds >> 8) & 0xFF);
        packet[43] = (byte)(totalSeconds & 0xFF);

        DateTime parsedUtc = NtpTimeService.ParseNtpPacket(packet);

        Assert.Equal(targetUtc, parsedUtc);
    }

    [Fact]
    public void ParseNtpPacket_InvalidLength_ThrowsArgumentException()
    {
        byte[] shortPacket = new byte[32];
        Assert.Throws<ArgumentException>(() => NtpTimeService.ParseNtpPacket(shortPacket));
    }

    [Fact]
    public async Task MockNtpTimeService_ReturnsConfiguredTime()
    {
        var fixedTime = new DateTime(2026, 9, 12, 12, 34, 56, DateTimeKind.Utc);
        var mockService = new MockNtpTimeService(fixedTime);

        DateTime retrieved = await mockService.GetUtcNowAsync();
        Assert.Equal(fixedTime, retrieved);
        Assert.True(mockService.IsSynchronized);
    }
}
