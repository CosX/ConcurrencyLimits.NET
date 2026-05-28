using ConcurrencyLimits.Internal;
using ConcurrencyLimits.Limit.Window;

namespace ConcurrencyLimits.Limit;

public sealed class WindowedLimit : ILimit
{
    private static readonly long DefaultMinWindowTime = (long)TimeSpan.FromSeconds(1).TotalNanoseconds;
    private static readonly long DefaultMaxWindowTime = (long)TimeSpan.FromSeconds(1).TotalNanoseconds;
    private static readonly long DefaultMinRttThreshold = (long)TimeSpan.FromMicroseconds(100).TotalNanoseconds;

    /// <summary>Minimum observed samples to filter out sample windows with not enough significant samples.</summary>
    private const int DefaultWindowSize = 10;

    public static Builder NewBuilder() => new();

    public sealed class Builder
    {
        internal long MaxWindowTime = DefaultMaxWindowTime;
        internal long MinWindowTime = DefaultMinWindowTime;
        internal int WindowSizeValue = DefaultWindowSize;
        internal long MinRttThreshold = DefaultMinRttThreshold;
        internal ISampleWindowFactory SampleWindowFactory = AverageSampleWindowFactory.Create();

        public Builder MinWindowTimeOf(TimeSpan minWindowTime)
        {
            Preconditions.CheckArgument(minWindowTime.TotalMilliseconds >= 100, "minWindowTime must be >= 100 ms");
            MinWindowTime = (long)minWindowTime.TotalNanoseconds;
            return this;
        }

        public Builder MaxWindowTimeOf(TimeSpan maxWindowTime)
        {
            Preconditions.CheckArgument(maxWindowTime.TotalMilliseconds >= 100, "maxWindowTime must be >= 100 ms");
            MaxWindowTime = (long)maxWindowTime.TotalNanoseconds;
            return this;
        }

        public Builder WindowSize(int windowSize)
        {
            Preconditions.CheckArgument(windowSize >= 10, "Window size must be >= 10");
            WindowSizeValue = windowSize;
            return this;
        }

        public Builder MinRttThresholdOf(TimeSpan threshold)
        {
            MinRttThreshold = (long)threshold.TotalNanoseconds;
            return this;
        }

        public Builder WithSampleWindowFactory(ISampleWindowFactory sampleWindowFactory)
        {
            SampleWindowFactory = sampleWindowFactory;
            return this;
        }

        public WindowedLimit Build(ILimit delegateLimit) => new(this, delegateLimit);
    }

    private readonly ILimit _delegate;
    private long _nextUpdateTime;
    private readonly long _minWindowTime;
    private readonly long _maxWindowTime;
    private readonly int _windowSize;
    private readonly long _minRttThreshold;
    private readonly object _lock = new();
    private readonly ISampleWindowFactory _sampleWindowFactory;
    private ISampleWindow _sample;

    private WindowedLimit(Builder builder, ILimit delegateLimit)
    {
        _delegate = delegateLimit;
        _minWindowTime = builder.MinWindowTime;
        _maxWindowTime = builder.MaxWindowTime;
        _windowSize = builder.WindowSizeValue;
        _minRttThreshold = builder.MinRttThreshold;
        _sampleWindowFactory = builder.SampleWindowFactory;
        _sample = _sampleWindowFactory.NewInstance();
    }

    public void NotifyOnChange(Action<int> consumer) => _delegate.NotifyOnChange(consumer);

    public void OnSample(long startTime, long rtt, int inflight, bool didDrop)
    {
        long endTime = startTime + rtt;

        if (rtt < _minRttThreshold)
        {
            return;
        }

        UpdateSample(current => current.AddSample(rtt, inflight, didDrop));

        if (endTime > Volatile.Read(ref _nextUpdateTime))
        {
            // Only allow one thread to propagate the sample to the delegate.
            if (Monitor.TryEnter(_lock))
            {
                try
                {
                    if (endTime > _nextUpdateTime)
                    {
                        ISampleWindow current = Interlocked.Exchange(ref _sample, _sampleWindowFactory.NewInstance());
                        Volatile.Write(ref _nextUpdateTime,
                            endTime + Math.Min(Math.Max(current.GetCandidateRttNanos() * 2, _minWindowTime), _maxWindowTime));

                        if (IsWindowReady(current))
                        {
                            _delegate.OnSample(startTime, current.GetTrackedRttNanos(), current.GetMaxInFlight(), current.DidDrop());
                        }
                    }
                }
                finally
                {
                    Monitor.Exit(_lock);
                }
            }
        }
    }

    private void UpdateSample(Func<ISampleWindow, ISampleWindow> updater)
    {
        while (true)
        {
            ISampleWindow current = Volatile.Read(ref _sample);
            ISampleWindow updated = updater(current);
            if (ReferenceEquals(Interlocked.CompareExchange(ref _sample, updated, current), current))
            {
                return;
            }
        }
    }

    private bool IsWindowReady(ISampleWindow sample)
        => sample.GetCandidateRttNanos() < long.MaxValue && sample.GetSampleCount() >= _windowSize;

    public int GetLimit() => _delegate.GetLimit();
}
