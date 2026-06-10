using ConcurrencyLimits.Internal;
using ConcurrencyLimits.Limit.Functions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ConcurrencyLimits.Limit;

/// <summary>
/// Limiter based on TCP Vegas where the limit increases by alpha if the queue_use is small (&lt; alpha)
/// and decreases by alpha if the queue_use is large (&gt; beta).
/// </summary>
public class VegasLimit : AbstractLimit
{
    private static readonly Func<int, int> Log10 = Log10RootIntFunction.Create(0);

    public sealed class Builder
    {
        internal int InitialLimit = 20;
        internal int MaxConcurrencyValue = 1000;
        internal IMetricRegistry Registry = EmptyMetricRegistry.Instance;
        internal double SmoothingValue = 1.0;

        internal Func<int, int> AlphaFunc = limit => 3 * Log10(limit);
        internal Func<int, int> BetaFunc = limit => 6 * Log10(limit);
        internal Func<int, int> ThresholdFunc = Log10;
        internal Func<double, double> IncreaseFunc = limit => limit + Log10((int)limit);
        internal Func<double, double> DecreaseFunc = limit => limit - Log10((int)limit);
        internal int ProbeMultiplierValue = 30;
        internal ILogger Logger = NullLogger.Instance;

        internal Builder() { }

        /// <summary>
        /// The limiter will probe for a new noload RTT every probeMultiplier * current limit iterations.
        /// </summary>
        public Builder ProbeMultiplier(int probeMultiplier)
        {
            Preconditions.CheckArgument(probeMultiplier > 0, "Probe multiplier must be > 0");
            ProbeMultiplierValue = probeMultiplier;
            return this;
        }

        public Builder Alpha(int alpha)
        {
            AlphaFunc = _ => alpha;
            return this;
        }

        public Builder ThresholdFunction(Func<int, int> threshold)
        {
            ThresholdFunc = threshold;
            return this;
        }

        public Builder AlphaFunction(Func<int, int> alpha)
        {
            AlphaFunc = alpha;
            return this;
        }

        public Builder Beta(int beta)
        {
            BetaFunc = _ => beta;
            return this;
        }

        public Builder BetaFunction(Func<int, int> beta)
        {
            BetaFunc = beta;
            return this;
        }

        public Builder IncreaseFunction(Func<double, double> increase)
        {
            IncreaseFunc = increase;
            return this;
        }

        public Builder DecreaseFunction(Func<double, double> decrease)
        {
            DecreaseFunc = decrease;
            return this;
        }

        public Builder Smoothing(double smoothing)
        {
            Preconditions.CheckArgument(smoothing > 0.0 && smoothing <= 1.0, "Smoothing must be in the range (0.0, 1.0]");
            SmoothingValue = smoothing;
            return this;
        }

        public Builder InitialLimitOf(int initialLimit)
        {
            InitialLimit = initialLimit;
            return this;
        }

        public Builder MaxConcurrency(int maxConcurrency)
        {
            MaxConcurrencyValue = maxConcurrency;
            return this;
        }

        public Builder MetricRegistry(IMetricRegistry registry)
        {
            Registry = registry;
            return this;
        }

        public Builder LoggerFactory(ILogger logger)
        {
            Logger = logger;
            return this;
        }

        public VegasLimit Build()
        {
            if (InitialLimit > MaxConcurrencyValue)
            {
                Logger.LogWarning("Initial limit {InitialLimit} exceeded maximum limit {MaxLimit}", InitialLimit, MaxConcurrencyValue);
            }
            return new VegasLimit(this);
        }
    }

    public static Builder NewBuilder() => new();

    public static VegasLimit NewDefault() => NewBuilder().Build();

    private double _estimatedLimit;
    private long _rttNoLoad;
    private readonly int _maxLimit;
    private readonly double _smoothing;
    private readonly Func<int, int> _alphaFunc;
    private readonly Func<int, int> _betaFunc;
    private readonly Func<int, int> _thresholdFunc;
    private readonly Func<double, double> _increaseFunc;
    private readonly Func<double, double> _decreaseFunc;
    private readonly IMetricRegistry.ISampleListener _rttSampleListener;
    private readonly int _probeMultiplier;
    private readonly ILogger _log;
    private int _probeCount;
    private double _probeJitter;

    private VegasLimit(Builder builder) : base(builder.InitialLimit)
    {
        _estimatedLimit = builder.InitialLimit;
        _maxLimit = builder.MaxConcurrencyValue;
        _alphaFunc = builder.AlphaFunc;
        _betaFunc = builder.BetaFunc;
        _increaseFunc = builder.IncreaseFunc;
        _decreaseFunc = builder.DecreaseFunc;
        _thresholdFunc = builder.ThresholdFunc;
        _smoothing = builder.SmoothingValue;
        _probeMultiplier = builder.ProbeMultiplierValue;
        _log = builder.Logger;

        ResetProbeJitter();

        _rttSampleListener = builder.Registry.Distribution(MetricIds.MinRttName);
    }

    private void ResetProbeJitter() => _probeJitter = Random.Shared.NextDouble() * 0.5 + 0.5;

    private bool ShouldProbe() => _probeJitter * _probeMultiplier * _estimatedLimit <= _probeCount;

    protected override int Update(long startTime, long rtt, int inflight, bool didDrop)
    {
        if (rtt <= 0)
        {
            throw new ArgumentException("rtt must be >0 but got " + rtt);
        }

        _probeCount++;
        if (ShouldProbe())
        {
            ResetProbeJitter();
            _probeCount = 0;
            _rttNoLoad = rtt;
            return (int)_estimatedLimit;
        }

        long rttNoLoad = _rttNoLoad;
        if (rttNoLoad == 0 || rtt < rttNoLoad)
        {
            _rttNoLoad = rtt;
            return (int)_estimatedLimit;
        }

        _rttSampleListener.AddLongSample(rttNoLoad);

        return UpdateEstimatedLimit(rtt, rttNoLoad, inflight, didDrop);
    }

    private int UpdateEstimatedLimit(long rtt, long rttNoLoad, int inflight, bool didDrop)
    {
        double estimatedLimit = _estimatedLimit;
        int queueSize = (int)Math.Ceiling(estimatedLimit * (1 - (double)rttNoLoad / rtt));

        double newLimit;
        // Treat any drop (i.e timeout) as needing to reduce the limit
        if (didDrop)
        {
            newLimit = _decreaseFunc(estimatedLimit);
        }
        // Prevent upward drift if not close to the limit
        else if (inflight * 2 < estimatedLimit)
        {
            return (int)estimatedLimit;
        }
        else
        {
            int alpha = _alphaFunc((int)estimatedLimit);
            int beta = _betaFunc((int)estimatedLimit);
            int threshold = _thresholdFunc((int)estimatedLimit);

            // Aggressive increase when no queuing
            if (queueSize <= threshold)
            {
                newLimit = estimatedLimit + beta;
            }
            // Increase the limit if queue is still manageable
            else if (queueSize < alpha)
            {
                newLimit = _increaseFunc(estimatedLimit);
            }
            // Detecting latency so decrease
            else if (queueSize > beta)
            {
                newLimit = _decreaseFunc(estimatedLimit);
            }
            // We're within the sweet spot so nothing to do
            else
            {
                return (int)estimatedLimit;
            }
        }

        newLimit = Math.Max(1, Math.Min(_maxLimit, newLimit));
        newLimit = (1 - _smoothing) * estimatedLimit + _smoothing * newLimit;
        _estimatedLimit = newLimit;
        return (int)newLimit;
    }

    public override string ToString()
    {
        lock (SyncRoot)
        {
            return $"VegasLimit [limit={GetLimit()}, rtt_noload={_rttNoLoad / 1e6} ms]";
        }
    }
}
