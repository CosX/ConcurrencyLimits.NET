using System.Threading.RateLimiting;
using ConcurrencyLimits.Limit;
using ConcurrencyLimits.Limiter;

namespace ConcurrencyLimits.Benchmarks;

/// <summary>One admission round trip: acquire a permit and release it as a success.</summary>
internal interface IGate
{
    /// <returns>false if the permit was rejected.</returns>
    bool Run();
}

internal sealed class LimiterGate(ILimiter<string> limiter) : IGate
{
    public bool Run()
    {
        IListener? listener = limiter.Acquire("live");
        if (listener == null)
        {
            return false;
        }
        listener.OnSuccess();
        return true;
    }
}

internal sealed class SemaphoreSlimGate(int permits) : IGate
{
    private readonly SemaphoreSlim _semaphore = new(permits, permits);

    public bool Run()
    {
        if (!_semaphore.Wait(0))
        {
            return false;
        }
        _semaphore.Release();
        return true;
    }
}

internal sealed class RateLimitingGate(int permits) : IGate
{
    private readonly ConcurrencyLimiter _limiter = new(new ConcurrencyLimiterOptions
    {
        PermitLimit = permits,
        QueueLimit = 0,
    });

    public bool Run()
    {
        using RateLimitLease lease = _limiter.AttemptAcquire();
        return lease.IsAcquired;
    }
}

internal static class Gates
{
    // Comfortably above the benchmark's thread count, so only the "Rejecting" gate ever rejects.
    private const int Permits = 1000;

    public static readonly string[] Kinds =
    [
        "SemaphoreSlim",
        "RateLimiting.ConcurrencyLimiter",
        "Simple+Fixed",
        "Simple+AIMD",
        "Simple+Vegas",
        "Simple+Gradient2",
        "Simple+Windowed(Vegas)",
        "Partitioned+Fixed",
        "Simple+Bypass",
        "Simple+Rejecting",
        "Blocking(Simple+Fixed)",
        "Lifo(Simple+Fixed)",
    ];

    public static IGate Create(string kind) => kind switch
    {
        "SemaphoreSlim" => new SemaphoreSlimGate(Permits),
        "RateLimiting.ConcurrencyLimiter" => new RateLimitingGate(Permits),
        "Simple+Fixed" => new LimiterGate(Simple(FixedLimit.Of(Permits))),
        "Simple+AIMD" => new LimiterGate(Simple(AIMDLimit.NewBuilder().Build())),
        "Simple+Vegas" => new LimiterGate(Simple(VegasLimit.NewDefault())),
        "Simple+Gradient2" => new LimiterGate(Simple(Gradient2Limit.NewDefault())),
        // Benchmark RTTs are nanoseconds; drop the default 100µs floor so samples reach the window.
        "Simple+Windowed(Vegas)" => new LimiterGate(Simple(WindowedLimit.NewBuilder()
            .MinRttThresholdOf(TimeSpan.Zero)
            .Build(VegasLimit.NewDefault()))),
        "Partitioned+Fixed" => new LimiterGate(PartitionedLimiter.NewBuilder<string>()
            .WithLimit(FixedLimit.Of(Permits))
            .PartitionResolver(ctx => ctx)
            .AddPartition("live", 0.8)
            .AddPartition("batch", 0.2)
            .Build()),
        "Simple+Bypass" => new LimiterGate(SimpleLimiter.NewBuilder()
            .WithLimit(FixedLimit.Of(Permits))
            .BypassLimitResolver(_ => true)
            .Build<string>()),
        "Simple+Rejecting" => new LimiterGate(Simple(FixedLimit.Of(0))),
        "Blocking(Simple+Fixed)" => new LimiterGate(BlockingLimiter<string>.Wrap(Simple(FixedLimit.Of(Permits)))),
        "Lifo(Simple+Fixed)" => new LimiterGate(LifoBlockingLimiter<string>.NewBuilder(Simple(FixedLimit.Of(Permits))).Build()),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    private static ILimiter<string> Simple(ILimit limit) => SimpleLimiter.NewBuilder().WithLimit(limit).Build<string>();
}
