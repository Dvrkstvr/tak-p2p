using System;
using System.Threading;
using System.Threading.Tasks;

namespace TakEngine.Core.Session;

public interface INtpTimeService
{
    TimeSpan Offset { get; }
    bool IsSynchronized { get; }
    Task<DateTime> GetUtcNowAsync(CancellationToken cancellationToken = default);
}
