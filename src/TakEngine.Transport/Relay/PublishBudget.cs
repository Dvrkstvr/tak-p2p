namespace TakEngine.Transport.Relay;

/// <summary>
/// At most <see cref="MaxEvents"/> published events in any sliding <see cref="Window"/> per client, retries included
/// (damus refuses the ~6th event in a burst; CLAUDE.md "Gotchas"). Pure: time is passed in.
/// </summary>
public sealed class PublishBudget
{
    public const int MaxEvents = 4;
    public static readonly TimeSpan Window = TimeSpan.FromSeconds(10);

    private readonly Queue<DateTimeOffset> _taken = new();

    public bool TryTake(DateTimeOffset now)
    {
        Expire(now);
        if (_taken.Count >= MaxEvents)
            return false;
        _taken.Enqueue(now);
        return true;
    }

    /// <summary>When the next <see cref="TryTake"/> can succeed (now if it already can).</summary>
    public DateTimeOffset NextAvailable(DateTimeOffset now)
    {
        Expire(now);
        return _taken.Count < MaxEvents ? now : _taken.Peek() + Window;
    }

    private void Expire(DateTimeOffset now)
    {
        while (_taken.Count > 0 && now - _taken.Peek() >= Window)
            _taken.Dequeue();
    }
}
