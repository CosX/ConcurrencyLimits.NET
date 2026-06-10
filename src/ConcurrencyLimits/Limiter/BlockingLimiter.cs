using ConcurrencyLimits.Internal;

namespace ConcurrencyLimits.Limiter;

/// <summary>
/// <see cref="ILimiter{TContext}"/> that blocks the caller when the limit has been reached. The caller is
/// blocked until the limiter has been released, or a timeout is reached. Commonly used in batch clients
/// that use the limiter as a back-pressure mechanism.
/// </summary>
public sealed class BlockingLimiter<TContext> : ILimiter<TContext>
{
    public static readonly TimeSpan MaxTimeout = TimeSpan.FromHours(1);

    private readonly ILimiter<TContext> _delegate;
    private readonly TimeSpan _timeout;
    private readonly object _lock = new();

    private BlockingLimiter(ILimiter<TContext> limiter, TimeSpan timeout)
    {
        _delegate = limiter;
        _timeout = timeout;
    }

    /// <summary>
    /// Wrap a limiter such that acquire will block up to <see cref="MaxTimeout"/> if the limit was reached
    /// instead of returning null immediately.
    /// </summary>
    public static BlockingLimiter<TContext> Wrap(ILimiter<TContext> delegateLimiter) => new(delegateLimiter, MaxTimeout);

    /// <summary>
    /// Wrap a limiter such that acquire will block up to a provided timeout if the limit was reached.
    /// </summary>
    public static BlockingLimiter<TContext> Wrap(ILimiter<TContext> delegateLimiter, TimeSpan timeout)
    {
        Preconditions.CheckArgument(timeout <= MaxTimeout, "Timeout cannot be greater than " + MaxTimeout);
        return new BlockingLimiter<TContext>(delegateLimiter, timeout);
    }

    public TimeSpan GetTimeout() => _timeout;

    private IListener? TryAcquire(TContext context)
    {
        DateTime deadline = DateTime.UtcNow + _timeout;
        lock (_lock)
        {
            while (true)
            {
                double timeoutMs = (deadline - DateTime.UtcNow).TotalMilliseconds;
                if (timeoutMs <= 0)
                {
                    return null;
                }

                IListener? listener = _delegate.Acquire(context);
                if (listener != null)
                {
                    return listener;
                }

                // We have reached the limit so block until a token is released
                Monitor.Wait(_lock, (int)timeoutMs);
            }
        }
    }

    private void Unblock()
    {
        lock (_lock)
        {
            // One token freed wakes one waiter; PulseAll causes a thundering herd that
            // all re-contend on _lock only for one to win.
            Monitor.Pulse(_lock);
        }
    }

    public IListener? Acquire(TContext context)
    {
        IListener? listener = TryAcquire(context);
        return listener == null ? null : new UnblockingListener(listener, Unblock);
    }

    public override string ToString() => $"BlockingLimiter [{_delegate}]";

    private sealed class UnblockingListener(IListener listener, Action unblock) : IListener
    {
        // Unblock in finally: a throwing delegate must not strand blocked waiters.
        public void OnSuccess()
        {
            try
            {
                listener.OnSuccess();
            }
            finally
            {
                unblock();
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
                unblock();
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
                unblock();
            }
        }
    }
}
