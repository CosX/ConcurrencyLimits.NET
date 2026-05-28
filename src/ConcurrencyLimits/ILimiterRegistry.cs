namespace ConcurrencyLimits;

/// <summary>
/// <see cref="ILimiter{TContext}"/> lookup for integrations that support multiple limiters,
/// i.e. one per RPC method.
/// </summary>
public interface ILimiterRegistry<TContext>
{
    ILimiter<TContext> Get(string key);

    static ILimiterRegistry<TContext> Single(ILimiter<TContext> limiter) => new SingleLimiterRegistry<TContext>(limiter);
}

internal sealed class SingleLimiterRegistry<TContext>(ILimiter<TContext> limiter) : ILimiterRegistry<TContext>
{
    public ILimiter<TContext> Get(string key) => limiter;
}
