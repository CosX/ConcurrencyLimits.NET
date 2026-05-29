namespace ConcurrencyLimits.Limit;

public abstract class AbstractLimit : ILimit
{
    private volatile int _limit;
    private readonly List<Action<int>> _listeners = new();
    private readonly object _sync = new();

    protected AbstractLimit(int initialLimit) => _limit = initialLimit;

    public void OnSample(long startTime, long rtt, int inflight, bool didDrop)
    {
        Action<int>[]? snapshot = null;
        int newLimit;
        lock (_sync)
        {
            newLimit = Update(startTime, rtt, inflight, didDrop);
            if (newLimit != _limit)
            {
                _limit = newLimit;
                snapshot = _listeners.ToArray();
            }
        }
        Dispatch(snapshot, newLimit);
    }

    protected abstract int Update(long startTime, long rtt, int inflight, bool didDrop);

    public int GetLimit() => _limit;

    protected virtual void SetLimit(int newLimit)
    {
        Action<int>[]? snapshot = null;
        lock (_sync)
        {
            if (newLimit != _limit)
            {
                _limit = newLimit;
                snapshot = _listeners.ToArray();
            }
        }
        Dispatch(snapshot, newLimit);
    }

    // Dispatch outside _sync so listener callbacks (e.g. semaphore resize) can't stall
    // algorithm state or invert lock order with downstream limiter locks.
    private static void Dispatch(Action<int>[]? snapshot, int newLimit)
    {
        if (snapshot == null)
        {
            return;
        }
        foreach (var listener in snapshot)
        {
            listener(newLimit);
        }
    }

    public void NotifyOnChange(Action<int> consumer)
    {
        lock (_sync)
        {
            _listeners.Add(consumer);
        }
    }
}
