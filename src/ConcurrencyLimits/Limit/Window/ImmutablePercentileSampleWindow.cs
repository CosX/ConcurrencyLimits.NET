using System.Threading;

namespace ConcurrencyLimits.Limit.Window;

public sealed class ImmutablePercentileSampleWindow : ISampleWindow
{
    private readonly long _minRtt;
    private readonly int _maxInFlight;
    private readonly bool _didDrop;
    private readonly long[] _observedRtts;
    private readonly int _sampleCount;
    private readonly double _percentile;

    public ImmutablePercentileSampleWindow(double percentile, int windowSize)
    {
        _minRtt = long.MaxValue;
        _maxInFlight = 0;
        _didDrop = false;
        _observedRtts = new long[windowSize];
        _sampleCount = 0;
        _percentile = percentile;
    }

    private ImmutablePercentileSampleWindow(long minRtt, int maxInFlight, bool didDrop, long[] observedRtts,
        int sampleCount, double percentile)
    {
        _minRtt = minRtt;
        _maxInFlight = maxInFlight;
        _didDrop = didDrop;
        _observedRtts = observedRtts;
        _sampleCount = sampleCount;
        _percentile = percentile;
    }

    public ISampleWindow AddSample(long rtt, int inflight, bool didDrop)
    {
        if (_sampleCount >= _observedRtts.Length)
        {
            return this;
        }
        Interlocked.Exchange(ref _observedRtts[_sampleCount], rtt);
        return new ImmutablePercentileSampleWindow(
            Math.Min(_minRtt, rtt),
            Math.Max(inflight, _maxInFlight),
            _didDrop || didDrop,
            _observedRtts,
            _sampleCount + 1,
            _percentile);
    }

    public long GetCandidateRttNanos() => _minRtt;

    public long GetTrackedRttNanos()
    {
        if (_sampleCount == 0)
        {
            return 0;
        }
        long[] copy = new long[_sampleCount];
        for (int i = 0; i < _sampleCount; i++)
        {
            copy[i] = Interlocked.Read(ref _observedRtts[i]);
        }
        Array.Sort(copy);

        int rttIndex = (int)Math.Floor(_sampleCount * _percentile + 0.5);
        int zeroBasedRttIndex = rttIndex - 1;
        return copy[zeroBasedRttIndex];
    }

    public int GetMaxInFlight() => _maxInFlight;

    public int GetSampleCount() => _sampleCount;

    public bool DidDrop() => _didDrop;

    public override string ToString()
        => $"ImmutablePercentileSampleWindow [minRtt={_minRtt / 1e6}, p{_percentile} rtt={GetTrackedRttNanos() / 1e6}, "
           + $"maxInFlight={_maxInFlight}, sampleCount={_sampleCount}, didDrop={_didDrop}]";
}
