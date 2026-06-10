using ConcurrencyLimits.Internal;

namespace ConcurrencyLimits.Limiter;

/// <summary>Type-erased view of a partitioned builder's state, consumed by the limiter constructor.</summary>
public interface IPartitionedBuilderState<TContext>
{
    IReadOnlyList<Func<TContext, string?>> PartitionResolvers { get; }
    IReadOnlyDictionary<string, Partition> Partitions { get; }
    int MaxDelayedThreads { get; }
}

public abstract class AbstractPartitionedLimiter<TContext> : AbstractLimiter<TContext>
{
    public abstract class Builder<TBuilder> : AbstractLimiterBuilder<TBuilder>, IPartitionedBuilderState<TContext>
        where TBuilder : Builder<TBuilder>
    {
        private readonly List<Func<TContext, string?>> _partitionResolvers = new();
        private readonly Dictionary<string, Partition> _partitions = new();
        private int _maxDelayedThreads = 100;

        IReadOnlyList<Func<TContext, string?>> IPartitionedBuilderState<TContext>.PartitionResolvers => _partitionResolvers;
        IReadOnlyDictionary<string, Partition> IPartitionedBuilderState<TContext>.Partitions => _partitions;
        int IPartitionedBuilderState<TContext>.MaxDelayedThreads => _maxDelayedThreads;

        public TBuilder PartitionResolver(Func<TContext, string?> contextToPartition)
        {
            _partitionResolvers.Add(contextToPartition);
            return Self();
        }

        public TBuilder AddPartition(string name, double percent)
        {
            Preconditions.CheckArgument(name != null, "Partition name may not be null");
            Preconditions.CheckArgument(percent >= 0.0 && percent <= 1.0, "Partition percentage must be in the range [0.0, 1.0]");
            GetOrCreate(name!).SetPercent(percent);
            return Self();
        }

        /// <summary>
        /// Delay rejections for the partition by the given duration, smoothing out retry storms.
        /// <para>WARNING: the delay blocks the calling thread (<see cref="Thread.Sleep(int)"/>).
        /// In async hosts such as ASP.NET Core this parks thread-pool threads precisely when the
        /// system is overloaded; prefer leaving this unset and delaying asynchronously in the caller.</para>
        /// </summary>
        public TBuilder PartitionRejectDelay(string name, TimeSpan duration)
        {
            GetOrCreate(name).SetBackoffMillis((long)duration.TotalMilliseconds);
            return Self();
        }

        public TBuilder MaxDelayedThreads(int maxDelayedThreads)
        {
            Preconditions.CheckArgument(maxDelayedThreads >= 0, "Max delayed threads must be >= 0");
            _maxDelayedThreads = maxDelayedThreads;
            return Self();
        }

        private Partition GetOrCreate(string name)
        {
            if (!_partitions.TryGetValue(name, out Partition? partition))
            {
                partition = new Partition(name);
                _partitions[name] = partition;
            }
            return partition;
        }

        protected bool HasPartitions() => _partitions.Count != 0;

        public ILimiter<TContext> Build()
        {
            // Half-configured partitioning silently degraded to a SimpleLimiter before; fail loudly instead.
            Preconditions.CheckState(HasPartitions() == (_partitionResolvers.Count != 0),
                HasPartitions()
                    ? "Partitions are configured but no partition resolver was provided"
                    : "A partition resolver is configured but no partitions were added");

            return HasPartitions()
                ? new DefaultPartitionedLimiter<TContext>(this, this)
                : new SimpleLimiter<TContext>(this);
        }
    }

    private readonly Dictionary<string, Partition> _partitions;
    private readonly Partition _unknownPartition;
    private readonly IReadOnlyList<Func<TContext, string?>> _partitionResolvers;
    private int _delayedThreads;
    private readonly int _maxDelayedThreads;
    private readonly object _acquireLock = new();

    protected AbstractPartitionedLimiter(AbstractLimiterBuilder builder, IPartitionedBuilderState<TContext> state)
        : base(builder)
    {
        Preconditions.CheckArgument(state.Partitions.Count != 0, "No partitions specified");
        Preconditions.CheckArgument(state.Partitions.Values.Sum(p => p.GetPercent()) <= 1.0 + 1e-9,
            "Sum of percentages must be <= 1.0");

        _partitions = new Dictionary<string, Partition>(state.Partitions);
        foreach (Partition partition in _partitions.Values)
        {
            partition.CreateMetrics(builder.Registry);
        }

        _unknownPartition = new Partition("unknown");
        _unknownPartition.CreateMetrics(builder.Registry);

        _partitionResolvers = state.PartitionResolvers;
        _maxDelayedThreads = state.MaxDelayedThreads;

        OnNewLimit(GetLimit());
    }

    private Partition ResolvePartition(TContext context)
    {
        foreach (Func<TContext, string?> resolver in _partitionResolvers)
        {
            string? name = resolver(context);
            if (name != null && _partitions.TryGetValue(name, out Partition? partition))
            {
                return partition;
            }
        }
        return _unknownPartition;
    }

    public override IListener? Acquire(TContext context)
    {
        if (ShouldBypass(context))
        {
            return CreateBypassListener();
        }

        Partition partition = ResolvePartition(context);

        // The partition is not a hard limit. It is only applied if the global limit is exceeded.
        // This allows for excess capacity in each partition to allow bursting over the limit, but
        // only if there is spare global capacity. The check, the partition increment and the
        // inflight increment must be atomic, otherwise concurrent acquires transiently breach
        // both the global and partition limits.
        lock (_acquireLock)
        {
            if (GetInflight() < GetLimit() || !partition.IsLimitExceeded())
            {
                partition.Acquire();
                IListener acquired = CreateListener();
                return new PartitionReleasingListener(acquired, partition);
            }
        }

        if (partition.BackoffMillis > 0 && Volatile.Read(ref _delayedThreads) < _maxDelayedThreads)
        {
            try
            {
                Interlocked.Increment(ref _delayedThreads);
                Thread.Sleep((int)Math.Clamp(partition.BackoffMillis, 0, int.MaxValue));
            }
            finally
            {
                Interlocked.Decrement(ref _delayedThreads);
            }
        }

        return CreateRejectedListener();
    }

    protected override void OnNewLimit(int newLimit)
    {
        base.OnNewLimit(newLimit);
        foreach (Partition partition in _partitions.Values)
        {
            partition.UpdateLimit(newLimit);
        }
    }

    public Partition? GetPartition(string name) => _partitions.GetValueOrDefault(name);

    private sealed class PartitionReleasingListener(IListener listener, Partition partition) : IListener
    {
        // Release in finally: a throwing limit algorithm must not leak the partition slot.
        public void OnSuccess()
        {
            try
            {
                listener.OnSuccess();
            }
            finally
            {
                partition.Release();
            }
        }

        public void OnIgnore()
        {
            try
            {
                listener.OnIgnore();
            }
            finally
            {
                partition.Release();
            }
        }

        public void OnDropped()
        {
            try
            {
                listener.OnDropped();
            }
            finally
            {
                partition.Release();
            }
        }
    }
}

internal sealed class DefaultPartitionedLimiter<TContext> : AbstractPartitionedLimiter<TContext>
{
    public DefaultPartitionedLimiter(AbstractLimiterBuilder builder, IPartitionedBuilderState<TContext> state)
        : base(builder, state) { }
}
