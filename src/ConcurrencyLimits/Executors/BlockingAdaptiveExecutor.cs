using ConcurrencyLimits.Internal;
using ConcurrencyLimits.Limit;
using ConcurrencyLimits.Limiter;

namespace ConcurrencyLimits.Executors;

/// <summary>Thrown when work cannot be scheduled because the concurrency limit has been reached.</summary>
public sealed class RejectedExecutionException : Exception
{
    public RejectedExecutionException() { }
    public RejectedExecutionException(string? message) : base(message) { }
}

/// <summary>
/// Executor which uses a <see cref="ILimiter{TContext}"/> to determine the size of the thread pool.
/// Any work executed once the limit has been reached will block the calling thread until the limit is released.
/// </summary>
/// <remarks>
/// Operations submitted to this executor should be homogeneous and have similar long-term latency
/// characteristics. RTT samples will only be taken from successful operations. The work should throw a
/// <see cref="UncheckedTimeoutException"/> if a request timed out or some external limit was reached.
/// All other exceptions will be ignored.
/// </remarks>
public sealed class BlockingAdaptiveExecutor
{
    public sealed class Builder
    {
        private static int _idCounter;

        internal IMetricRegistry MetricRegistry = EmptyMetricRegistry.Instance;
        internal Action<Action>? Executor;
        internal ILimiter<object?>? Limiter;
        internal string? Name;

        public Builder WithMetricRegistry(IMetricRegistry metricRegistry)
        {
            MetricRegistry = metricRegistry;
            return this;
        }

        public Builder WithExecutor(Action<Action> executor)
        {
            Executor = executor;
            return this;
        }

        public Builder WithLimiter(ILimiter<object?> limiter)
        {
            Limiter = limiter;
            return this;
        }

        public Builder WithName(string name)
        {
            Name = name;
            return this;
        }

        public BlockingAdaptiveExecutor Build()
        {
            Name ??= "unnamed-" + Interlocked.Increment(ref _idCounter);
            Executor ??= command => ThreadPool.QueueUserWorkItem(_ => command());
            Limiter ??= SimpleLimiter.NewBuilder()
                .MetricRegistry(MetricRegistry)
                .WithLimit(AIMDLimit.NewBuilder().Build())
                .Build<object?>();
            // Wrap so builder-built executors block like the constructor paths do, instead of fast-failing.
            Limiter = EnsureBlocking(Limiter);
            return new BlockingAdaptiveExecutor(this);
        }
    }

    public static Builder NewBuilder() => new();

    private readonly ILimiter<object?> _limiter;
    private readonly Action<Action> _executor;

    private BlockingAdaptiveExecutor(Builder builder)
    {
        _limiter = builder.Limiter!;
        _executor = builder.Executor!;
    }

    /// <summary>
    /// Wrap a non-blocking limiter so callers block until a permit is available, scheduling work on the thread pool.
    /// </summary>
    public BlockingAdaptiveExecutor(ILimiter<object?> limiter)
        : this(limiter, command => ThreadPool.QueueUserWorkItem(_ => command())) { }

    public BlockingAdaptiveExecutor(ILimiter<object?> limiter, Action<Action> executor)
    {
        _limiter = EnsureBlocking(limiter);
        _executor = executor;
    }

    private static ILimiter<object?> EnsureBlocking(ILimiter<object?> limiter)
        => limiter is BlockingLimiter<object?> ? limiter : BlockingLimiter<object?>.Wrap(limiter);

    public void Execute(Action command)
    {
        IListener listener = _limiter.Acquire(null) ?? throw new RejectedExecutionException();
        try
        {
            _executor(() =>
            {
                try
                {
                    command();
                    listener.OnSuccess();
                }
                catch (UncheckedTimeoutException)
                {
                    listener.OnDropped();
                }
                catch (RejectedExecutionException)
                {
                    listener.OnDropped();
                }
                catch (Exception)
                {
                    // Unknown cause; the only sane thing to do is ignore this request.
                    listener.OnIgnore();
                }
            });
        }
        catch (Exception)
        {
            listener.OnIgnore();
            throw;
        }
    }
}
