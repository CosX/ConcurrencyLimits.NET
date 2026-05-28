namespace ConcurrencyLimits.Limit.Window;

public sealed class ImmutableAverageSampleWindow : ISampleWindow
{
    private readonly long _minRtt;
    private readonly long _sum;
    private readonly int _maxInFlight;
    private readonly int _sampleCount;
    private readonly bool _didDrop;

    public ImmutableAverageSampleWindow()
    {
        _minRtt = long.MaxValue;
        _sum = 0;
        _maxInFlight = 0;
        _sampleCount = 0;
        _didDrop = false;
    }

    private ImmutableAverageSampleWindow(long minRtt, long sum, int maxInFlight, int sampleCount, bool didDrop)
    {
        _minRtt = minRtt;
        _sum = sum;
        _maxInFlight = maxInFlight;
        _sampleCount = sampleCount;
        _didDrop = didDrop;
    }

    public ISampleWindow AddSample(long rtt, int inflight, bool didDrop)
        => new ImmutableAverageSampleWindow(
            Math.Min(rtt, _minRtt),
            _sum + rtt,
            Math.Max(inflight, _maxInFlight),
            _sampleCount + 1,
            _didDrop || didDrop);

    public long GetCandidateRttNanos() => _minRtt;

    public long GetTrackedRttNanos() => _sampleCount == 0 ? 0 : _sum / _sampleCount;

    public int GetMaxInFlight() => _maxInFlight;

    public int GetSampleCount() => _sampleCount;

    public bool DidDrop() => _didDrop;

    public override string ToString()
        => $"ImmutableAverageSampleWindow [minRtt={_minRtt / 1e6}, avgRtt={GetTrackedRttNanos() / 1e6}, "
           + $"maxInFlight={_maxInFlight}, sampleCount={_sampleCount}, didDrop={_didDrop}]";
}
