using ConcurrencyLimits.Limit;

namespace ConcurrencyLimits.Limiter;

public abstract class AbstractLimiter<TContext> : ILimiter<TContext>
{
    public const string IdTag = "id";
    public const string StatusTag = "status";

    internal sealed class NoopListener : IListener
    {
        public static readonly NoopListener Instance = new();
        public void OnSuccess() { }
        public void OnIgnore() { }
        public void OnDropped() { }
        public override string ToString() => "{NoopListener}";
    }

    private int _inFlight;
    private readonly Func<long> _clock;
    private readonly ILimit _limitAlgorithm;
    private readonly IMetricRegistry.ICounter _successCounter;
    private readonly IMetricRegistry.ICounter _droppedCounter;
    private readonly IMetricRegistry.ICounter _ignoredCounter;
    private readonly IMetricRegistry.ICounter _rejectedCounter;
    private readonly IMetricRegistry.ICounter _bypassCounter;
    private readonly Func<object?, bool> _bypassResolver;

    private volatile int _limit;

    protected AbstractLimiter(AbstractLimiterBuilder builder)
    {
        _clock = builder.Clock;
        _limitAlgorithm = builder.Limit;
        _limit = _limitAlgorithm.GetLimit();
        _limitAlgorithm.NotifyOnChange(OnNewLimit);
        _bypassResolver = builder.BypassResolver;

        string name = builder.ResolveName();
        builder.Registry.Gauge(MetricIds.LimitName, () => GetLimit());
        _successCounter = builder.Registry.Counter(MetricIds.CallName, IdTag, name, StatusTag, "success");
        _droppedCounter = builder.Registry.Counter(MetricIds.CallName, IdTag, name, StatusTag, "dropped");
        _ignoredCounter = builder.Registry.Counter(MetricIds.CallName, IdTag, name, StatusTag, "ignored");
        _rejectedCounter = builder.Registry.Counter(MetricIds.CallName, IdTag, name, StatusTag, "rejected");
        _bypassCounter = builder.Registry.Counter(MetricIds.CallName, IdTag, name, StatusTag, "bypassed");
    }

    public abstract IListener? Acquire(TContext context);

    protected bool ShouldBypass(TContext context) => _bypassResolver(context);

    protected IListener? CreateRejectedListener()
    {
        _rejectedCounter.Increment();
        return null;
    }

    protected IListener CreateBypassListener()
    {
        _bypassCounter.Increment();
        return NoopListener.Instance;
    }

    protected IListener CreateListener()
    {
        long startTime = _clock();
        int currentInflight = Interlocked.Increment(ref _inFlight);
        return new ListenerImpl(this, startTime, currentInflight);
    }

    private sealed class ListenerImpl(AbstractLimiter<TContext> limiter, long startTime, int currentInflight) : IListener
    {
        public void OnSuccess()
        {
            Interlocked.Decrement(ref limiter._inFlight);
            limiter._successCounter.Increment();
            limiter._limitAlgorithm.OnSample(startTime, limiter._clock() - startTime, currentInflight, false);
        }

        public void OnIgnore()
        {
            Interlocked.Decrement(ref limiter._inFlight);
            limiter._ignoredCounter.Increment();
        }

        public void OnDropped()
        {
            Interlocked.Decrement(ref limiter._inFlight);
            limiter._droppedCounter.Increment();
            limiter._limitAlgorithm.OnSample(startTime, limiter._clock() - startTime, currentInflight, true);
        }
    }

    public int GetLimit() => _limit;

    public int GetInflight() => Volatile.Read(ref _inFlight);

    protected virtual void OnNewLimit(int newLimit) => _limit = newLimit;
}
