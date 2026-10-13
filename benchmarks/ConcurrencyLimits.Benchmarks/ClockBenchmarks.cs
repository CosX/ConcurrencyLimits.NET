using System.Diagnostics;
using BenchmarkDotNet.Attributes;
using ConcurrencyLimits.Internal;

namespace ConcurrencyLimits.Benchmarks;

/// <summary>
/// Every acquire/release reads the nano clock twice, so its cost is a floor under every limiter.
/// It depends heavily on the host's clock source (TSC via vDSO is cheap; some VM clocks are not).
/// </summary>
public class ClockBenchmarks
{
    [Benchmark(Baseline = true)]
    public long StopwatchGetTimestamp() => Stopwatch.GetTimestamp();

    [Benchmark]
    public long SystemNanoTimeNow() => SystemNanoTime.Now();
}
