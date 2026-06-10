using ConcurrencyLimits.Internal;
using ConcurrencyLimits.Limit.Functions;
using ConcurrencyLimits.Limit.Measurement;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ConcurrencyLimits.Limit;

/// <summary>
/// Concurrency limit algorithm that adjusts the limits based on the gradient of change in the
/// samples minimum RTT and absolute minimum RTT allowing for a queue of square root of the
/// current limit.
/// </summary>
public sealed class GradientLimit : AbstractLimit
{
    private const int Disabled = -1;

    public sealed class Builder
    {
        internal int InitialLimit = 50;
        internal int MinLimitValue = 1;
        internal int MaxConcurrencyValue = 1000;
        internal double SmoothingValue = 0.2;
        internal Func<int, int> QueueSizeFunc = SquareRootFunction.Create(4);
        internal IMetricRegistry Registry = EmptyMetricRegistry.Instance;
        internal double RttToleranceValue = 2.0;
        internal int ProbeIntervalValue = 1000;
        internal double BackoffRatioValue = 0.9;
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

        public Builder RttTolerance(double rttTolerance)
        {
            Preconditions.CheckArgument(rttTolerance >= 1.0, "Tolerance must be >= 1.0");
            RttToleranceValue = rttTolerance;
            return this;
        }

        public Builder MaxConcurrency(int maxConcurrency)
        {
            MaxConcurrencyValue = maxConcurrency;
            return this;
        }

        public Builder QueueSize(int queueSize)
        {
            QueueSizeFunc = _ => queueSize;
            return this;
        }

        public Builder QueueSize(Func<int, int> queueSize)
        {
            QueueSizeFunc = queueSize;
            return this;
        }

        public Builder Smoothing(double smoothing)
        {
            Preconditions.CheckArgument(smoothing > 0.0 && smoothing <= 1.0, "Smoothing must be in the range (0.0, 1.0]");
            SmoothingValue = smoothing;
            return this;
        }

        public Builder MetricRegistry(IMetricRegistry registry)
        {
            Registry = registry;
            return this;
        }

        public Builder BackoffRatio(double backoffRatio)
        {
            Preconditions.CheckArgument(backoffRatio >= 0.5 && backoffRatio <= 1.0, "backoffRatio must be in the range [0.5, 1.0]");
            BackoffRatioValue = backoffRatio;
            return this;
        }

        /// <summary>
        /// The limiter will probe for a new noload RTT every probeInterval updates. Default 1000. Set to -1 to disable.
        /// </summary>
        public Builder ProbeInterval(int probeInterval)
        {
            ProbeIntervalValue = probeInterval;
            return this;
        }

        public Builder LoggerFactory(ILogger logger)
        {
            Logger = logger;
            return this;
        }

        public GradientLimit Build()
        {
            if (InitialLimit > MaxConcurrencyValue)
            {
                Logger.LogWarning("Initial limit {InitialLimit} exceeded maximum limit {MaxLimit}", InitialLimit, MaxConcurrencyValue);
            }
            if (InitialLimit < MinLimitValue)
            {
                Logger.LogWarning("Initial limit {InitialLimit} is less than minimum limit {MinLimit}", InitialLimit, MinLimitValue);
            }
            return new GradientLimit(this);
        }
    }

    public static Builder NewBuilder() => new();

    public static GradientLimit NewDefault() => NewBuilder().Build();

    private double _estimatedLimit;
    private long _lastRtt;
    private readonly IMeasurement _rttNoLoadMeasurement;
    private readonly int _maxLimit;
    private readonly int _minLimit;
    private readonly Func<int, int> _queueSize;
    private readonly double _smoothing;
    private readonly double _rttTolerance;
    private readonly double _backoffRatio;
    private readonly IMetricRegistry.ISampleListener _minRttSampleListener;
    private readonly IMetricRegistry.ISampleListener _minWindowRttSampleListener;
    private readonly IMetricRegistry.ISampleListener _queueSizeSampleListener;
    private readonly int _probeInterval;
    private readonly ILogger _log;
    private int _resetRttCounter;

    private GradientLimit(Builder builder) : base(builder.InitialLimit)
    {
        _estimatedLimit = builder.InitialLimit;
        _maxLimit = builder.MaxConcurrencyValue;
        _minLimit = builder.MinLimitValue;
        _queueSize = builder.QueueSizeFunc;
        _smoothing = builder.SmoothingValue;
        _rttTolerance = builder.RttToleranceValue;
        _backoffRatio = builder.BackoffRatioValue;
        _probeInterval = builder.ProbeIntervalValue;
        _log = builder.Logger;
        _resetRttCounter = NextProbeCountdown();
        _rttNoLoadMeasurement = new MinimumMeasurement();

        _minRttSampleListener = builder.Registry.Distribution(MetricIds.MinRttName);
        _minWindowRttSampleListener = builder.Registry.Distribution(MetricIds.WindowMinRttName);
        _queueSizeSampleListener = builder.Registry.Distribution(MetricIds.WindowQueueSizeName);
    }

    private int NextProbeCountdown()
    {
        if (_probeInterval == Disabled)
        {
            return Disabled;
        }
        return _probeInterval + Random.Shared.Next(_probeInterval);
    }

    protected override int Update(long startTime, long rtt, int inflight, bool didDrop)
    {
        _lastRtt = rtt;
        _minWindowRttSampleListener.AddLongSample(rtt);

        double queueSize = _queueSize((int)_estimatedLimit);
        _queueSizeSampleListener.AddDoubleSample(queueSize);

        if (_probeInterval != Disabled && _resetRttCounter-- <= 0)
        {
            _resetRttCounter = NextProbeCountdown();

            _estimatedLimit = Math.Max(_minLimit, queueSize);
            _rttNoLoadMeasurement.Reset();
            _lastRtt = 0;
            return (int)_estimatedLimit;
        }

        long rttNoLoad = (long)_rttNoLoadMeasurement.Add(rtt);
        _minRttSampleListener.AddLongSample(rttNoLoad);

        double gradient = Math.Max(0.5, Math.Min(1.0, _rttTolerance * rttNoLoad / rtt));

        double newLimit;
        if (didDrop)
        {
            newLimit = _estimatedLimit * _backoffRatio;
        }
        else if (inflight < _estimatedLimit / 2)
        {
            return (int)_estimatedLimit;
        }
        else
        {
            newLimit = _estimatedLimit * gradient + queueSize;
        }

        if (newLimit < _estimatedLimit)
        {
            newLimit = Math.Max(_minLimit, _estimatedLimit * (1 - _smoothing) + _smoothing * newLimit);
        }
        newLimit = Math.Max(queueSize, Math.Min(_maxLimit, newLimit));

        _estimatedLimit = newLimit;
        return (int)_estimatedLimit;
    }

    public long GetLastRttNanos()
    {
        lock (SyncRoot)
        {
            return _lastRtt;
        }
    }

    public long GetRttNoLoadNanos()
    {
        lock (SyncRoot)
        {
            return (long)_rttNoLoadMeasurement.Get();
        }
    }

    public override string ToString()
    {
        lock (SyncRoot)
        {
            return $"GradientLimit [limit={(int)_estimatedLimit}, rtt_noload={(long)_rttNoLoadMeasurement.Get() / 1e6} ms]";
        }
    }
}
