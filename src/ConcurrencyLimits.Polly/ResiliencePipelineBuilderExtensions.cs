using Polly;

namespace ConcurrencyLimits.Polly;

/// <summary>
/// Extensions for adding the ConcurrencyLimits resilience strategy to a Polly pipeline.
/// </summary>
public static class ResiliencePipelineBuilderExtensions
{
    /// <summary>
    /// Add a concurrency-limit strategy gated by the supplied limiter.
    /// </summary>
    public static TBuilder AddConcurrencyLimit<TBuilder>(this TBuilder builder, ILimiter<ResilienceContext> limiter)
        where TBuilder : ResiliencePipelineBuilderBase
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(limiter);
        return builder.AddConcurrencyLimit(new ConcurrencyLimitStrategyOptions { Limiter = limiter });
    }

    /// <summary>
    /// Add a concurrency-limit strategy configured by <paramref name="options"/>.
    /// </summary>
    public static TBuilder AddConcurrencyLimit<TBuilder>(this TBuilder builder, ConcurrencyLimitStrategyOptions options)
        where TBuilder : ResiliencePipelineBuilderBase
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(options);

        builder.AddStrategy(
            _ => new ConcurrencyLimitStrategy(options.Limiter, options.RejectionExceptionFactory),
            options);
        return builder;
    }
}
