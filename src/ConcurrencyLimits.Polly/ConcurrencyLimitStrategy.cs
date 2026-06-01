using Polly;

namespace ConcurrencyLimits.Polly;

/// <summary>
/// Resilience strategy that gates execution through an <see cref="ILimiter{TContext}"/>. When the limiter
/// rejects, the strategy returns an outcome carrying the configured rejection exception. Successful
/// completions feed an RTT sample; cancellations from the resilience context token are reported as drops;
/// other faults are ignored.
/// </summary>
internal sealed class ConcurrencyLimitStrategy : ResilienceStrategy
{
    private readonly ILimiter<ResilienceContext> _limiter;
    private readonly Func<ResilienceContext, Exception> _rejectionExceptionFactory;

    public ConcurrencyLimitStrategy(
        ILimiter<ResilienceContext> limiter,
        Func<ResilienceContext, Exception> rejectionExceptionFactory)
    {
        _limiter = limiter;
        _rejectionExceptionFactory = rejectionExceptionFactory;
    }

    protected override async ValueTask<Outcome<TResult>> ExecuteCore<TResult, TState>(
        Func<ResilienceContext, TState, ValueTask<Outcome<TResult>>> callback,
        ResilienceContext context,
        TState state)
    {
        IListener? listener = _limiter.Acquire(context);
        if (listener is null)
        {
            return Outcome.FromException<TResult>(_rejectionExceptionFactory(context));
        }

        Outcome<TResult> outcome;
        try
        {
            outcome = await callback(context, state).ConfigureAwait(context.ContinueOnCapturedContext);
        }
        catch (Exception ex)
        {
            ReportOutcome(listener, ex, context);
            throw;
        }

        ReportOutcome(listener, outcome.Exception, context);
        return outcome;
    }

    private static void ReportOutcome(IListener listener, Exception? exception, ResilienceContext context)
    {
        if (exception is null)
        {
            listener.OnSuccess();
            return;
        }

        // Cancellation tied to the resilience context's token is a real drop (client gave up / timeout).
        if (exception is OperationCanceledException && context.CancellationToken.IsCancellationRequested)
        {
            listener.OnDropped();
            return;
        }

        listener.OnIgnore();
    }
}
