using ConcurrencyLimits.Limiter;
using Polly;

namespace ConcurrencyLimits.Polly;

/// <summary>
/// Builder for an <see cref="ILimiter{TContext}"/> keyed by <see cref="ResilienceContext"/>. Allows partitioning
/// and bypass to be driven by <see cref="ResilienceContext.OperationKey"/> or properties on the context.
/// </summary>
public sealed class ResilienceContextLimiterBuilder
    : AbstractPartitionedLimiter<ResilienceContext>.Builder<ResilienceContextLimiterBuilder>
{
    protected override ResilienceContextLimiterBuilder Self() => this;

    /// <summary>Partition by <see cref="ResilienceContext.OperationKey"/>.</summary>
    public ResilienceContextLimiterBuilder PartitionByOperationKey()
        => PartitionResolver(ctx => ctx.OperationKey);

    /// <summary>Partition by <see cref="ResilienceContext.OperationKey"/> mapped through <paramref name="keyToGroup"/>.</summary>
    public ResilienceContextLimiterBuilder PartitionByOperationKey(Func<string?, string?> keyToGroup)
        => PartitionResolver(ctx => keyToGroup(ctx.OperationKey));

    /// <summary>Partition by a property stored on the resilience context.</summary>
    public ResilienceContextLimiterBuilder PartitionByProperty(ResiliencePropertyKey<string?> key)
        => PartitionResolver(ctx => ctx.Properties.TryGetValue(key, out var v) ? v : null);

    /// <summary>Bypass the limit when the predicate evaluates to true for the context.</summary>
    public ResilienceContextLimiterBuilder BypassLimitResolver(Func<ResilienceContext, bool> shouldBypass)
        => BypassLimitResolver(ctx => ctx is ResilienceContext rc && shouldBypass(rc));

    /// <summary>Bypass the limit when the operation key matches <paramref name="operationKey"/>.</summary>
    public ResilienceContextLimiterBuilder BypassLimitByOperationKey(string operationKey)
        => BypassLimitResolver(ctx => ctx.OperationKey == operationKey);
}
