using ConcurrencyLimits.Internal;

namespace ConcurrencyLimits.Limiter;

/// <summary>
/// <see cref="ILimiter{TContext}"/> decorator that blocks the caller when the limit has been reached. This
/// strategy favors availability over latency by not fast-failing requests when the limit has been reached.
/// Blocked requests are processed in last-in/first-out order to keep success latencies low.
/// </summary>
public sealed class LifoBlockingLimiter<TContext> : ILimiter<TContext>
{
    public sealed class Builder
    {
        internal readonly ILimiter<TContext> Delegate;
        internal int MaxBacklogSizeValue = 100;
        internal long? FixedBacklogTimeoutMillisValue = 1_000L;
        internal Func<TContext, long> MaxBacklogTimeoutMillisFunc;

        internal Builder(ILimiter<TContext> delegateLimiter)
        {
            Delegate = delegateLimiter;
            MaxBacklogTimeoutMillisFunc = _ => FixedBacklogTimeoutMillisValue ?? 1_000L;
        }

        /// <summary>Set maximum number of blocked threads. Default is 100.</summary>
        public Builder BacklogSize(int size)
        {
            Preconditions.CheckArgument(size > 0, "Backlog size must be > 0");
            MaxBacklogSizeValue = size;
            return this;
        }

        /// <summary>Set maximum timeout for threads blocked on the limiter. Default is 1 second.</summary>
        public Builder BacklogTimeout(TimeSpan timeout) => BacklogTimeoutMillis((long)timeout.TotalMilliseconds);

        public Builder BacklogTimeoutMillis(long timeout)
        {
            MaxBacklogTimeoutMillisFunc = _ => timeout;
            FixedBacklogTimeoutMillisValue = timeout;
            return this;
        }

        /// <summary>
        /// Derive the backlog timeout from the request context, allowing timeouts to be set dynamically
        /// based on things like request deadlines.
        /// </summary>
        public Builder BacklogTimeout(Func<TContext, TimeSpan> mapper)
        {
            MaxBacklogTimeoutMillisFunc = ctx => (long)mapper(ctx).TotalMilliseconds;
            FixedBacklogTimeoutMillisValue = null;
            return this;
        }

        public LifoBlockingLimiter<TContext> Build() => new(this);
    }

    public static Builder NewBuilder(ILimiter<TContext> delegateLimiter) => new(delegateLimiter);

    private readonly ILimiter<TContext> _delegate;

    private sealed class ListenerHolder(TContext context)
    {
        private readonly ManualResetEventSlim _latch = new(false);
        public IListener? Listener;
        public readonly TContext Context = context;

        // Clamp: context-derived timeouts (e.g. deadline - now) can be negative or exceed
        // int.MaxValue; ManualResetEventSlim.Wait throws on values below -1 and the raw
        // cast would overflow. 0 polls the latch without blocking.
        public bool Await(long timeoutMillis) => _latch.Wait((int)Math.Clamp(timeoutMillis, 0, int.MaxValue));

        public void Set(IListener? listener)
        {
            Listener = listener;
            _latch.Set();
        }

        /// <summary>Only call once the holder is unreachable from the backlog — no Set can race the dispose.</summary>
        public void DisposeLatch() => _latch.Dispose();
    }

    private readonly LinkedList<ListenerHolder> _backlog = new();
    private readonly int _backlogSize;
    private readonly Func<TContext, long> _backlogTimeoutMillis;
    private readonly long? _fixedBacklogTimeoutMillis;
    private readonly object _lock = new();

    private LifoBlockingLimiter(Builder builder)
    {
        _delegate = builder.Delegate;
        _backlogSize = builder.MaxBacklogSizeValue;
        _backlogTimeoutMillis = builder.MaxBacklogTimeoutMillisFunc;
        _fixedBacklogTimeoutMillis = builder.FixedBacklogTimeoutMillisValue;
    }

    /// <summary>Fixed backlog timeout in milliseconds, or null if the timeout is derived from the request context.</summary>
    public long? GetFixedBacklogTimeoutMillis() => _fixedBacklogTimeoutMillis;

    private IListener? TryAcquire(TContext context)
    {
        IListener? listener = _delegate.Acquire(context);
        if (listener != null)
        {
            return listener;
        }

        ListenerHolder holder = new(context);

        // Restrict backlog size so the queue doesn't grow unbounded during an outage.
        // Check and enqueue atomically to avoid overshooting _backlogSize under concurrency.
        lock (_lock)
        {
            if (_backlog.Count >= _backlogSize)
            {
                return null;
            }
            _backlog.AddFirst(holder);
        }

        if (!holder.Await(_backlogTimeoutMillis(context)))
        {
            lock (_lock)
            {
                RemoveLastOccurrence(holder);
            }
            holder.DisposeLatch();
            // if we acquired a token just as we were timing out then return it
            return holder.Listener;
        }
        holder.DisposeLatch();
        return holder.Listener;
    }

    private void RemoveLastOccurrence(ListenerHolder holder)
    {
        for (LinkedListNode<ListenerHolder>? node = _backlog.Last; node != null; node = node.Previous)
        {
            if (ReferenceEquals(node.Value, holder))
            {
                _backlog.Remove(node);
                return;
            }
        }
    }

    private void Unblock()
    {
        lock (_lock)
        {
            if (_backlog.Count != 0)
            {
                ListenerHolder holder = _backlog.First!.Value;
                IListener? listener = _delegate.Acquire(holder.Context);
                if (listener != null)
                {
                    _backlog.RemoveFirst();
                    holder.Set(listener);
                }
                // else: still can't acquire; unblock will be called again on next release.
            }
        }
    }

    public IListener? Acquire(TContext context)
    {
        IListener? listener = TryAcquire(context);
        return listener == null ? null : new UnblockingListener(listener, Unblock);
    }

    public override string ToString() => $"LifoBlockingLimiter [{_delegate}]";

    private sealed class UnblockingListener(IListener listener, Action unblock) : IListener
    {
        // Unblock in finally: a throwing delegate must not strand backlogged waiters.
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
