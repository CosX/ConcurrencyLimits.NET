using BenchmarkDotNet.Attributes;
using ConcurrencyLimits.Limit.Window;

namespace ConcurrencyLimits.Benchmarks;

/// <summary>
/// Cost of filling one <see cref="ISampleWindow"/> with <see cref="WindowSize"/> samples and reading the
/// tracked RTT, as <see cref="ConcurrencyLimits.Limit.WindowedLimit"/> does once per window. The percentile
/// window copies its whole buffer on every add, so its cost should grow quadratically with the window size.
/// </summary>
[MemoryDiagnoser]
public class SampleWindowBenchmarks
{
    private long[] _rtts = null!;
    private PercentileSampleWindowFactory _percentile = null!;

    [Params(10, 100, 1000)]
    public int WindowSize { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var random = new Random(42);
        _rtts = new long[WindowSize];
        for (int i = 0; i < WindowSize; i++)
        {
            _rtts[i] = 5_000_000 + random.Next(5_000_000);
        }
        _percentile = PercentileSampleWindowFactory.Of(0.9, WindowSize);
    }

    [Benchmark(Baseline = true)]
    public long Average() => Fill(AverageSampleWindowFactory.Create().NewInstance());

    [Benchmark]
    public long Percentile() => Fill(_percentile.NewInstance());

    private long Fill(ISampleWindow window)
    {
        foreach (long rtt in _rtts)
        {
            window = window.AddSample(rtt, 10, false);
        }
        return window.GetTrackedRttNanos();
    }
}
