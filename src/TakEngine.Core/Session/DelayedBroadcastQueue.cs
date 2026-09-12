using System;
using System.Collections.Generic;
using System.Linq;
using TakEngine.Abstractions;

namespace TakEngine.Core.Session;

public sealed record ScheduledBroadcastItem(
    BroadcastEnvelope Envelope,
    DateTime ReleaseTimeUtc);

public sealed class DelayedBroadcastQueue
{
    private readonly object _lock = new();
    private readonly List<ScheduledBroadcastItem> _items = [];

    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _items.Count;
            }
        }
    }

    public void Enqueue(BroadcastEnvelope envelope, TimeSpan delay, DateTime? nowUtc = null)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        DateTime baseTime = nowUtc ?? DateTime.UtcNow;
        DateTime releaseTime = delay <= TimeSpan.Zero
            ? baseTime
            : baseTime.Add(delay);

        lock (_lock)
        {
            _items.Add(new ScheduledBroadcastItem(envelope, releaseTime));
            // Keep items sorted by release time, then turn index
            _items.Sort((a, b) =>
            {
                int cmp = a.ReleaseTimeUtc.CompareTo(b.ReleaseTimeUtc);
                return cmp != 0 ? cmp : a.Envelope.TurnIndex.CompareTo(b.Envelope.TurnIndex);
            });
        }
    }

    public IReadOnlyList<BroadcastEnvelope> DequeueReady(DateTime? nowUtc = null)
    {
        DateTime current = nowUtc ?? DateTime.UtcNow;
        var ready = new List<BroadcastEnvelope>();

        lock (_lock)
        {
            int i = 0;
            while (i < _items.Count && _items[i].ReleaseTimeUtc <= current)
            {
                ready.Add(_items[i].Envelope);
                i++;
            }

            if (i > 0)
            {
                _items.RemoveRange(0, i);
            }
        }

        return ready;
    }

    public bool TryPeekNextRelease(out DateTime nextReleaseUtc)
    {
        lock (_lock)
        {
            if (_items.Count > 0)
            {
                nextReleaseUtc = _items[0].ReleaseTimeUtc;
                return true;
            }

            nextReleaseUtc = default;
            return false;
        }
    }

    public int Cancel(Guid gameId)
    {
        lock (_lock)
        {
            return _items.RemoveAll(item => item.Envelope.GameId == gameId);
        }
    }

    public IReadOnlyList<ScheduledBroadcastItem> GetPendingSnapshot()
    {
        lock (_lock)
        {
            return _items.ToList();
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _items.Clear();
        }
    }
}
