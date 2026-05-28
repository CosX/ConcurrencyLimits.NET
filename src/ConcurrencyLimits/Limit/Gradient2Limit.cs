using ConcurrencyLimits.Internal;
using ConcurrencyLimits.Limit.Measurement;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ConcurrencyLimits.Limit;

/// <summary>
/// Concurrency limit algorithm that adjusts the limit based on the gradient of change of the current
/// average RTT and a long term exponentially smoothed average RTT.
/// </summary>
public sealed class Gradient2Limit : AbstractLimit
{
    public sealed class Builder
    {
        internal int InitialLimit = 20;
        internal int MinLimitValue = 20;
        internal int MaxConcurrencyValue = 200;
        internal double SmoothingValue = 0.2;
        internal Func<int, int> QueueSizeFunc = _ => 4;
        internal IMetricRegistry Registry = EmptyMetricRegistry.Instance;
        internal int LongWindowValue = 600;
        internal double RttToleranceValue = 1.5;
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

        public Builder QueueSizeFunction(Func<int, int> queueSize)
        {
            QueueSizeFunc = queueSize;
            return this;
        }

        public Builder RttTolerance(double rttTolerance)
        {
            Preconditions.CheckArgument(rttTolerance >= 1.0, "Tolerance must be >= 1.0");
            RttToleranceValue = rttTolerance;
            return this;
        }

        public Builder Smoothing(double smoothing)
        {
            SmoothingValue = smoothing;
            return this;
        }

        public Builder MetricRegistry(IMetricRegistry registry)
        {
            Registry = registry;
            return this;
        }

        public Builder LongWindow(int n)
        {
            LongWindowValue = n;
            return this;
        }

        public Builder LoggerFactory(ILogger logger)
        {
            Logger = logger;
            return this;
        }

        public Gradient2Limit Build()
        {
            if (InitialLimit > MaxConcurrencyValue)
            {
                Logger.LogWarning("Initial limit {InitialLimit} exceeded maximum limit {MaxLimit}", InitialLimit, MaxConcurrencyValue);
            }
            if (InitialLimit < MinLimitValue)
            {
                Logger.LogWarning("Initial limit {InitialLimit} is less than minimum limit {MinLimit}", InitialLimit, MinLimitValue);
            }
            return new Gradient2Limit(this);
        }
    }

    public static Builder NewBuilder() => new();

    public static Gradient2Limit NewDefault() => NewBuilder().Build();

    private double _estimatedLimit;
    private long _lastRtt;
    private readonly IMeasurement _longRtt;
    private readonly int _maxLimit;
    private readonly int _minLimit;
    private readonly Func<int, int> _queueSize;
    private readonly double _smoothing;
    private readonly IMetricRegistry.ISampleListener _longRttSampleListener;
    private readonly IMetricRegistry.ISampleListener _shortRttSampleListener;
    private readonly IMetricRegistry.ISampleListener _queueSizeSampleListener;
    private readonly double _tolerance;

    private Gradient2Limit(Builder builder) : base(builder.InitialLimit)
    {
        _estimatedLimit = builder.InitialLimit;
        _maxLimit = builder.MaxConcurrencyValue;
        _minLimit = builder.MinLimitValue;
        _queueSize = builder.QueueSizeFunc;
        _smoothing = builder.SmoothingValue;
        _tolerance = builder.RttToleranceValue;
        _lastRtt = 0;
        _longRtt = new ExpAvgMeasurement(builder.LongWindowValue, 10);

        _longRttSampleListener = builder.Registry.Distribution(MetricIds.MinRttName);
        _shortRttSampleListener = builder.Registry.Distribution(MetricIds.WindowMinRttName);
        _queueSizeSampleListener = builder.Registry.Distribution(MetricIds.WindowQueueSizeName);
    }

    protected override int Update(long startTime, long rtt, int inflight, bool didDrop)
    {
        double estimatedLimit = _estimatedLimit;
        double queueSize = _queueSize((int)estimatedLimit);

        _lastRtt = rtt;
        double shortRtt = rtt;
        double longRtt = _longRtt.Add(rtt);

        _shortRttSampleListener.AddDoubleSample(shortRtt);
        _longRttSampleListener.AddDoubleSample(longRtt);
        _queueSizeSampleListener.AddDoubleSample(queueSize);

        // If the long RTT is substantially larger than the short RTT then reduce the long RTT measurement.
        if (longRtt / shortRtt > 2)
        {
            _longRtt.Update(current => current * 0.95);
        }

        // Don't grow the limit if we are app limited
        if (inflight < estimatedLimit / 2)
        {
            return (int)estimatedLimit;
        }

        double gradient = Math.Max(0.5, Math.Min(1.0, _tolerance * longRtt / shortRtt));
        double newLimit = estimatedLimit * gradient + queueSize;
        newLimit = estimatedLimit * (1 - _smoothing) + newLimit * _smoothing;
        newLimit = Math.Max(_minLimit, Math.Min(_maxLimit, newLimit));

        _estimatedLimit = newLimit;
        return (int)newLimit;
    }

    public long GetLastRttNanos() => _lastRtt;

    public long GetRttNoLoadNanos() => (long)_longRtt.Get();

    public override string ToString() => $"Gradient2Limit [limit={(int)_estimatedLimit}]";
}
