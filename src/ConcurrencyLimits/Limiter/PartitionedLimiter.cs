namespace ConcurrencyLimits.Limiter;

/// <summary>Entry point for building a partitioned limiter without a transport-specific subclass.</summary>
public static class PartitionedLimiter
{
    public static PartitionedLimiterBuilder<TContext> NewBuilder<TContext>() => new();
}

public sealed class PartitionedLimiterBuilder<TContext>
    : AbstractPartitionedLimiter<TContext>.Builder<PartitionedLimiterBuilder<TContext>>
{
    protected override PartitionedLimiterBuilder<TContext> Self() => this;
}
