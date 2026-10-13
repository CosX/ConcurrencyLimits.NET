using BenchmarkDotNet.Attributes;

namespace ConcurrencyLimits.Benchmarks;

/// <summary>
/// Cost of one acquire + OnSuccess round trip per limiter configuration. A fixed amount of work is split
/// across <see cref="Threads"/> workers, so the reported time is wall-clock per operation: perfect scaling
/// would halve it each time the thread count doubles, while lock contention makes it flat or worse.
/// </summary>
[MemoryDiagnoser]
public class GateThroughputBenchmarks
{
    private const int TotalOps = 120_000;

    private IGate _gate = null!;

    public static IEnumerable<string> Kinds => Gates.Kinds;

    [ParamsSource(nameof(Kinds))]
    public string Gate { get; set; } = null!;

    [Params(1, 2, 4)]
    public int Threads { get; set; }

    [GlobalSetup]
    public void Setup() => _gate = Gates.Create(Gate);

    [Benchmark(OperationsPerInvoke = TotalOps)]
    public void AcquireRelease()
    {
        int opsPerThread = TotalOps / Threads;
        IGate gate = _gate;
        if (Threads == 1)
        {
            Loop(gate, opsPerThread);
            return;
        }
        Parallel.For(0, Threads, new ParallelOptions { MaxDegreeOfParallelism = Threads }, _ => Loop(gate, opsPerThread));
    }

    private static void Loop(IGate gate, int ops)
    {
        for (int i = 0; i < ops; i++)
        {
            gate.Run();
        }
    }
}
