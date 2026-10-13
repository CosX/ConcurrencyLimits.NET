using BenchmarkDotNet.Attributes;
using ConcurrencyLimits.Limit;
using ConcurrencyLimits.Limit.Window;

namespace ConcurrencyLimits.Benchmarks;

/// <summary>
/// Per-sample cost of each limit algorithm, fed a replayed trace of ~5-10ms RTTs (1% drops) at a 100µs
/// arrival spacing. Inflight is reported as the current limit, so every sample takes the full update path
/// instead of the "app limited" early return.
/// </summary>
[MemoryDiagnoser]
public class LimitAlgorithmBenchmarks
{
    private const int TraceLength = 4096; // power of two, indexed with a mask
    private const long ArrivalSpacingNanos = 100_000;

    private ILimit _limit = null!;
    private long[] _rtts = null!;
    private bool[] _drops = null!;
    private int _index;
    private long _now;

    [Params("Fixed", "AIMD", "Vegas", "Gradient", "Gradient2", "Windowed(Vegas)", "Windowed(Vegas,p90/100)")]
    public string Algorithm { get; set; } = null!;

    [GlobalSetup]
    public void Setup()
    {
        var random = new Random(42);
        _rtts = new long[TraceLength];
        _drops = new bool[TraceLength];
        for (int i = 0; i < TraceLength; i++)
        {
            _rtts[i] = 5_000_000 + (long)(random.NextDouble() * random.NextDouble() * 5_000_000);
            _drops[i] = random.NextDouble() < 0.01;
        }

        _limit = Algorithm switch
        {
            "Fixed" => FixedLimit.Of(100),
            "AIMD" => AIMDLimit.NewBuilder().Build(),
            "Vegas" => VegasLimit.NewDefault(),
            "Gradient" => GradientLimit.NewDefault(),
            "Gradient2" => Gradient2Limit.NewDefault(),
            "Windowed(Vegas)" => WindowedLimit.NewBuilder().Build(VegasLimit.NewDefault()),
            "Windowed(Vegas,p90/100)" => WindowedLimit.NewBuilder()
                .WithSampleWindowFactory(PercentileSampleWindowFactory.Of(0.9, 100))
                .Build(VegasLimit.NewDefault()),
            _ => throw new ArgumentOutOfRangeException(nameof(Algorithm), Algorithm, null),
        };
    }

    [Benchmark]
    public void OnSample()
    {
        int i = _index++ & (TraceLength - 1);
        _now += ArrivalSpacingNanos;
        _limit.OnSample(_now, _rtts[i], _limit.GetLimit(), _drops[i]);
    }
}
