namespace ConcurrencyLimits.Limit;

public abstract class AbstractLimit : ILimit
{
    private volatile int _limit;
    private readonly List<Action<int>> _listeners = new();
    private readonly object _sync = new();

    protected AbstractLimit(int initialLimit) => _limit = initialLimit;

    public void OnSample(long startTime, long rtt, int inflight, bool didDrop)
    {
        lock (_sync)
        {
            SetLimit(Update(startTime, rtt, inflight, didDrop));
        }
    }

    protected abstract int Update(long startTime, long rtt, int inflight, bool didDrop);

    public int GetLimit() => _limit;

    protected virtual void SetLimit(int newLimit)
    {
        lock (_sync)
        {
            if (newLimit != _limit)
            {
                _limit = newLimit;
                Action<int>[] snapshot;
                lock (_listeners)
                {
                    snapshot = _listeners.ToArray();
                }
                foreach (var listener in snapshot)
                {
                    listener(newLimit);
                }
            }
        }
    }

    public void NotifyOnChange(Action<int> consumer)
    {
        lock (_listeners)
        {
            _listeners.Add(consumer);
        }
    }
}
