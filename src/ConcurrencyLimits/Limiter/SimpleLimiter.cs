namespace ConcurrencyLimits.Limiter;

public static class SimpleLimiter
{
    public static SimpleLimiterBuilder NewBuilder() => new();
}

public sealed class SimpleLimiterBuilder : AbstractLimiterBuilder<SimpleLimiterBuilder>
{
    public SimpleLimiter<TContext> Build<TContext>() => new(this);

    protected override SimpleLimiterBuilder Self() => this;
}

public class SimpleLimiter<TContext> : AbstractLimiter<TContext>
{
    private readonly IMetricRegistry.ISampleListener _inflightDistribution;
    private readonly AdjustableSemaphore _semaphore;
    private readonly object _limitChangeLock = new();

    public SimpleLimiter(AbstractLimiterBuilder builder) : base(builder)
    {
        _inflightDistribution = builder.Registry.Distribution(MetricIds.InflightName);
        _semaphore = new AdjustableSemaphore(GetLimit());
    }

    public override IListener? Acquire(TContext context)
    {
        IListener? listener;
        if (ShouldBypass(context))
        {
            listener = CreateBypassListener();
        }
        else if (!_semaphore.TryAcquire())
        {
            listener = CreateRejectedListener();
        }
        else
        {
            IListener delegateListener = CreateListener();
            listener = new ReleasingListener(this, delegateListener);
        }
        _inflightDistribution.AddLongSample(GetInflight());
        return listener;
    }

    protected override void OnNewLimit(int newLimit)
    {
        // Serialize the old/new read and the matching semaphore delta so concurrent
        // limit changes can't compute deltas against a stale baseline and drift permits.
        lock (_limitChangeLock)
        {
            int oldLimit = GetLimit();
            base.OnNewLimit(newLimit);

            if (newLimit > oldLimit)
            {
                _semaphore.Release(newLimit - oldLimit);
            }
            else
            {
                _semaphore.ReducePermits(oldLimit - newLimit);
            }
        }
    }

    /// <summary>Semaphore supporting non-blocking acquire, release, and dynamic permit reduction.</summary>
    private sealed class AdjustableSemaphore(int permits)
    {
        private readonly object _sync = new();
        private int _permits = permits;

        public bool TryAcquire()
        {
            lock (_sync)
            {
                if (_permits > 0)
                {
                    _permits--;
                    return true;
                }
                return false;
            }
        }

        public void Release(int n = 1)
        {
            lock (_sync)
            {
                _permits += n;
            }
        }

        public void ReducePermits(int reduction)
        {
            lock (_sync)
            {
                _permits -= reduction;
            }
        }
    }

    private sealed class ReleasingListener(SimpleLimiter<TContext> limiter, IListener delegateListener) : IListener
    {
        public void OnSuccess()
        {
            delegateListener.OnSuccess();
            limiter._semaphore.Release();
        }

        public void OnIgnore()
        {
            delegateListener.OnIgnore();
            limiter._semaphore.Release();
        }

        public void OnDropped()
        {
            delegateListener.OnDropped();
            limiter._semaphore.Release();
        }
    }
}
