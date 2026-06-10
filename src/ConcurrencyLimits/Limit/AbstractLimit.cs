namespace ConcurrencyLimits.Limit;

public abstract class AbstractLimit : ILimit
{
    private volatile int _limit;
    private readonly List<Action<int>> _listeners = new();
    private readonly object _sync = new();
    private readonly object _dispatchLock = new();
    private long _version;           // guarded by _sync
    private long _dispatchedVersion; // guarded by _dispatchLock

    /// <summary>Lock guarding algorithm state. Subclasses must hold this when reading mutable
    /// state outside <see cref="Update"/> (e.g. in accessors or ToString) to avoid torn reads.</summary>
    protected object SyncRoot => _sync;

    protected AbstractLimit(int initialLimit) => _limit = initialLimit;

    public void OnSample(long startTime, long rtt, int inflight, bool didDrop)
    {
        Action<int>[]? snapshot = null;
        long version = 0;
        int newLimit;
        lock (_sync)
        {
            newLimit = Update(startTime, rtt, inflight, didDrop);
            if (newLimit != _limit)
            {
                _limit = newLimit;
                version = ++_version;
                snapshot = _listeners.ToArray();
            }
        }
        Dispatch(snapshot, newLimit, version);
    }

    protected abstract int Update(long startTime, long rtt, int inflight, bool didDrop);

    public int GetLimit() => _limit;

    protected virtual void SetLimit(int newLimit)
    {
        Action<int>[]? snapshot = null;
        long version = 0;
        lock (_sync)
        {
            if (newLimit != _limit)
            {
                _limit = newLimit;
                version = ++_version;
                snapshot = _listeners.ToArray();
            }
        }
        Dispatch(snapshot, newLimit, version);
    }

    // Dispatch outside _sync so listener callbacks (e.g. semaphore resize) can't stall
    // algorithm state or invert lock order with downstream limiter locks. Changes are
    // version-stamped under _sync and stale dispatches dropped, so listeners never observe
    // limit changes out of order (Java delivers in order by notifying inside synchronized).
    private void Dispatch(Action<int>[]? snapshot, int newLimit, long version)
    {
        if (snapshot == null)
        {
            return;
        }
        lock (_dispatchLock)
        {
            if (version <= _dispatchedVersion)
            {
                return; // A newer limit was already delivered; dropping this one keeps listeners ordered.
            }
            _dispatchedVersion = version;
            foreach (var listener in snapshot)
            {
                listener(newLimit);
            }
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
