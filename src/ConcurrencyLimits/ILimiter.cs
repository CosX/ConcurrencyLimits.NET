namespace ConcurrencyLimits;

/// <summary>
/// Contract for a concurrency limiter. The caller is expected to call <see cref="Acquire"/> for each request
/// and must also release the returned listener when the operation completes. Releasing the listener
/// may trigger an update to the concurrency limit based on error rate or latency measurement.
/// </summary>
/// <typeparam name="TContext">Some limiters take a context to perform more fine grained limits.</typeparam>
public interface ILimiter<in TContext>
{
    /// <summary>
    /// Acquire a token from the limiter. Returns <c>null</c> if the limit has been exceeded.
    /// If acquired the caller must call one of the <see cref="IListener"/> methods when the operation
    /// has completed to release the count.
    /// </summary>
    /// <param name="context">Context for the request.</param>
    /// <returns><c>null</c> if limit exceeded.</returns>
    IListener? Acquire(TContext context);
}

/// <summary>
/// Listener returned from <see cref="ILimiter{TContext}.Acquire"/> used to notify the limiter of
/// the outcome of the operation.
/// </summary>
public interface IListener
{
    /// <summary>
    /// Notification that the operation succeeded and internally measured latency should be used as an RTT sample.
    /// </summary>
    void OnSuccess();

    /// <summary>
    /// The operation failed before any meaningful RTT measurement could be made and should be ignored
    /// to not introduce an artificially low RTT.
    /// </summary>
    void OnIgnore();

    /// <summary>
    /// The request failed and was dropped due to being rejected by an external limit or hitting a timeout.
    /// Loss based <see cref="ILimit"/> implementations will likely do an aggressive reduction in limit when this happens.
    /// </summary>
    void OnDropped();
}
