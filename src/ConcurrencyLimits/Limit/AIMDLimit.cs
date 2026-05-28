using ConcurrencyLimits.Internal;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ConcurrencyLimits.Limit;

/// <summary>
/// Loss based dynamic <see cref="ILimit"/> that does an additive increment as long as there are no
/// errors and a multiplicative decrement when there is an error.
/// </summary>
public sealed class AIMDLimit : AbstractLimit
{
    private static readonly long DefaultTimeoutNanos = (long)TimeSpan.FromSeconds(5).TotalNanoseconds;

    public sealed class Builder
    {
        internal int MinLimitValue = 20;
        internal int InitialLimit = 20;
        internal int MaxLimitValue = 200;
        internal double BackoffRatioValue = 0.9;
        internal long TimeoutNanos = DefaultTimeoutNanos;
        internal ILogger Logger = NullLogger.Instance;

        public Builder InitialLimitOf(int initialLimit)
        {
            InitialLimit = initialLimit;
            return this;
        }

        public Builder MinLimit(int minLimit)
        {
            MinLimitValue = minLimit;
            return this;
        }

        public Builder MaxLimit(int maxLimit)
        {
            MaxLimitValue = maxLimit;
            return this;
        }

        public Builder BackoffRatio(double backoffRatio)
        {
            Preconditions.CheckArgument(backoffRatio < 1.0 && backoffRatio >= 0.5, "Backoff ratio must be in the range [0.5, 1.0)");
            BackoffRatioValue = backoffRatio;
            return this;
        }

        /// <summary>Timeout threshold that when exceeded equates to a drop.</summary>
        public Builder Timeout(TimeSpan timeout)
        {
            Preconditions.CheckArgument(timeout > TimeSpan.Zero, "Timeout must be positive");
            TimeoutNanos = (long)timeout.TotalNanoseconds;
            return this;
        }

        public Builder LoggerFactory(ILogger logger)
        {
            Logger = logger;
            return this;
        }

        public AIMDLimit Build()
        {
            if (InitialLimit > MaxLimitValue)
            {
                Logger.LogWarning("Initial limit {InitialLimit} exceeded maximum limit {MaxLimit}", InitialLimit, MaxLimitValue);
            }
            if (InitialLimit < MinLimitValue)
            {
                Logger.LogWarning("Initial limit {InitialLimit} is less than minimum limit {MinLimit}", InitialLimit, MinLimitValue);
            }
            return new AIMDLimit(this);
        }
    }

    public static Builder NewBuilder() => new();

    private readonly double _backoffRatio;
    private readonly long _timeout;
    private readonly int _minLimit;
    private readonly int _maxLimit;

    private AIMDLimit(Builder builder) : base(builder.InitialLimit)
    {
        _backoffRatio = builder.BackoffRatioValue;
        _timeout = builder.TimeoutNanos;
        _maxLimit = builder.MaxLimitValue;
        _minLimit = builder.MinLimitValue;
    }

    protected override int Update(long startTime, long rtt, int inflight, bool didDrop)
    {
        int currentLimit = GetLimit();

        if (didDrop || rtt > _timeout)
        {
            currentLimit = (int)(currentLimit * _backoffRatio);
        }
        else if (inflight * 2 >= currentLimit)
        {
            currentLimit += 1;
        }

        return Math.Min(_maxLimit, Math.Max(_minLimit, currentLimit));
    }

    public override string ToString() => $"AIMDLimit [limit={GetLimit()}]";
}
