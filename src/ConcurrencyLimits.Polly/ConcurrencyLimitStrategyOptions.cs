using System.ComponentModel.DataAnnotations;
using Polly;

namespace ConcurrencyLimits.Polly;

/// <summary>
/// Options for the ConcurrencyLimits resilience strategy.
/// </summary>
public sealed class ConcurrencyLimitStrategyOptions : ResilienceStrategyOptions
{
    public ConcurrencyLimitStrategyOptions()
    {
        Name = "ConcurrencyLimit";
    }

    /// <summary>
    /// Limiter used to gate executions. Required.
    /// </summary>
    [Required]
    public ILimiter<ResilienceContext> Limiter { get; set; } = null!;

    /// <summary>
    /// Factory invoked when the limiter rejects a request. The returned exception is propagated
    /// as the execution outcome. Defaults to <see cref="ConcurrencyLimitRejectedException"/>.
    /// </summary>
    public Func<ResilienceContext, Exception> RejectionExceptionFactory { get; set; }
        = _ => new ConcurrencyLimitRejectedException();
}
