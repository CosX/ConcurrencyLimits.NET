using ConcurrencyLimits.Internal;
using ConcurrencyLimits.Limit;

namespace ConcurrencyLimits.Limiter;

/// <summary>
/// Context-agnostic shared state for limiter builders. Mirrors the raw <c>AbstractLimiter.Builder</c>
/// of the Java library so a single builder can construct limiters for any context type.
/// </summary>
public abstract class AbstractLimiterBuilder
{
    internal ILimit Limit = VegasLimit.NewDefault();
    internal Func<long> Clock = SystemNanoTime.Now;
    internal string? Name;
    internal IMetricRegistry Registry = EmptyMetricRegistry.Instance;

    internal static readonly Func<object?, bool> AlwaysFalse = _ => false;
    internal Func<object?, bool> BypassResolver = AlwaysFalse;

    private static int _idCounter;

    /// <summary>Resolve the limiter name, allocating an "unnamed-N" id lazily at build time so
    /// ids reflect actual limiters built, not every builder instantiated.</summary>
    internal string ResolveName() => Name ??= "unnamed-" + Interlocked.Increment(ref _idCounter);
}

public abstract class AbstractLimiterBuilder<TBuilder> : AbstractLimiterBuilder
    where TBuilder : AbstractLimiterBuilder<TBuilder>
{
    public TBuilder Named(string name)
    {
        Name = name;
        return Self();
    }

    public TBuilder WithLimit(ILimit limit)
    {
        Limit = limit;
        return Self();
    }

    public TBuilder NanoClock(Func<long> clock)
    {
        Clock = clock;
        return Self();
    }

    public TBuilder MetricRegistry(IMetricRegistry registry)
    {
        Registry = registry;
        return Self();
    }

    protected abstract TBuilder Self();

    /// <summary>
    /// Add a chainable bypass resolver predicate from context. Multiple resolvers may be added and if any
    /// returns true the call is bypassed without increasing the limiter inflight count or affecting the algorithm.
    /// </summary>
    public TBuilder BypassLimitResolver(Func<object?, bool> shouldBypass)
    {
        if (ReferenceEquals(BypassResolver, AlwaysFalse))
        {
            BypassResolver = shouldBypass;
        }
        else
        {
            Func<object?, bool> existing = BypassResolver;
            BypassResolver = ctx => existing(ctx) || shouldBypass(ctx);
        }
        return Self();
    }
}
