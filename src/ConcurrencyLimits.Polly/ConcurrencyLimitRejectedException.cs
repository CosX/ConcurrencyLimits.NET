namespace ConcurrencyLimits.Polly;

/// <summary>
/// Thrown by the concurrency limit resilience strategy when the limiter rejects a request.
/// </summary>
public sealed class ConcurrencyLimitRejectedException : Exception
{
    public ConcurrencyLimitRejectedException()
        : base("Concurrency limit exceeded") { }

    public ConcurrencyLimitRejectedException(string message) : base(message) { }

    public ConcurrencyLimitRejectedException(string message, Exception innerException)
        : base(message, innerException) { }
}
