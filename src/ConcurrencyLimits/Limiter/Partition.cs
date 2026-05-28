namespace ConcurrencyLimits.Limiter;

public sealed class Partition(string name)
{
    private const string PartitionTagName = "partition";

    private int _busy;
    private double _percent;
    private volatile int _limit;
    private long _backoffMillis;
    private IMetricRegistry.ISampleListener _inflightDistribution = null!;

    public string Name { get; } = name;

    internal long BackoffMillis => _backoffMillis;

    internal Partition SetPercent(double percent)
    {
        _percent = percent;
        return this;
    }

    internal Partition SetBackoffMillis(long backoffMillis)
    {
        _backoffMillis = backoffMillis;
        return this;
    }

    internal void UpdateLimit(int totalLimit)
    {
        // Round up and ensure at least 1. With this technique the sum of bin limits may end up
        // being higher than the concurrency limit.
        _limit = (int)Math.Max(1, Math.Ceiling(totalLimit * _percent));
    }

    public bool IsLimitExceeded() => Volatile.Read(ref _busy) >= _limit;

    internal void Acquire()
    {
        int nowBusy = Interlocked.Increment(ref _busy);
        _inflightDistribution.AddLongSample(nowBusy);
    }

    internal bool TryAcquire()
    {
        int current = Volatile.Read(ref _busy);
        while (current < _limit)
        {
            if (Interlocked.CompareExchange(ref _busy, current + 1, current) == current)
            {
                _inflightDistribution.AddLongSample(current + 1);
                return true;
            }
            current = Volatile.Read(ref _busy);
        }
        return false;
    }

    internal void Release() => Interlocked.Decrement(ref _busy);

    public int GetLimit() => _limit;

    public int GetInflight() => Volatile.Read(ref _busy);

    internal double GetPercent() => _percent;

    internal void CreateMetrics(IMetricRegistry registry)
    {
        _inflightDistribution = registry.Distribution(MetricIds.InflightName, PartitionTagName, Name);
        registry.Gauge(MetricIds.PartitionLimitName, () => GetLimit(), PartitionTagName, Name);
    }

    public override string ToString() => $"Partition [pct={_percent}, limit={_limit}, busy={Volatile.Read(ref _busy)}]";
}
