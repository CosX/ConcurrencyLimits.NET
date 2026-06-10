using ConcurrencyLimits;
using ConcurrencyLimits.Limit;
using ConcurrencyLimits.Limiter;
using Xunit;

namespace ConcurrencyLimits.Tests;

public class SimpleLimiterTest
{
    [Fact]
    public void UseLimiterCapacityUntilTotalLimit()
    {
        var limiter = SimpleLimiter.NewBuilder().WithLimit(FixedLimit.Of(10)).Build<string>();

        for (int i = 0; i < 10; i++)
        {
            Assert.NotNull(limiter.Acquire("live"));
        }

        Assert.Null(limiter.Acquire("live"));
        Assert.Equal(10, limiter.GetInflight());
    }

    [Fact]
    public void TestReleaseLimit()
    {
        var limiter = SimpleLimiter.NewBuilder().WithLimit(FixedLimit.Of(10)).Build<string>();

        IListener? completion = limiter.Acquire("live");
        for (int i = 1; i < 10; i++)
        {
            Assert.NotNull(limiter.Acquire("live"));
        }

        Assert.Equal(10, limiter.GetInflight());
        Assert.Null(limiter.Acquire("live"));

        completion!.OnSuccess();
        Assert.Equal(9, limiter.GetInflight());

        Assert.NotNull(limiter.Acquire("live"));
        Assert.Equal(10, limiter.GetInflight());
    }

    [Fact]
    public void TestSimpleBypassLimiter()
    {
        var limiter = SimpleLimiter.NewBuilder()
            .WithLimit(FixedLimit.Of(10))
            .BypassLimitResolver(context => "admin".Equals(context))
            .Build<string>();

        for (int i = 0; i < 10; i++)
        {
            Assert.NotNull(limiter.Acquire("live"));
            Assert.Equal(i + 1, limiter.GetInflight());
        }

        for (int i = 0; i < 10; i++)
        {
            Assert.Null(limiter.Acquire("live"));
            Assert.NotNull(limiter.Acquire("admin"));
        }
    }

    [Fact]
    public void TestSimpleBypassLimiterDefault()
    {
        var limiter = SimpleLimiter.NewBuilder().WithLimit(FixedLimit.Of(10)).Build<string>();

        for (int i = 0; i < 10; i++)
        {
            Assert.NotNull(limiter.Acquire("live"));
            Assert.Equal(i + 1, limiter.GetInflight());
        }

        Assert.Null(limiter.Acquire("live"));
        Assert.Null(limiter.Acquire("admin"));
    }

    [Fact]
    public void ThrowingLimitAlgorithmDoesNotLeakPermit()
    {
        var limiter = SimpleLimiter.NewBuilder().WithLimit(new ThrowingLimit(1)).Build<string>();

        IListener listener = limiter.Acquire("live")!;
        Assert.Throws<InvalidOperationException>(listener.OnSuccess);

        // Permit must have been released despite the algorithm throwing.
        Assert.NotNull(limiter.Acquire("live"));
    }

    private sealed class ThrowingLimit(int limit) : ILimit
    {
        public int GetLimit() => limit;
        public void NotifyOnChange(Action<int> consumer) { }
        public void OnSample(long startTime, long rtt, int inflight, bool didDrop)
            => throw new InvalidOperationException("algorithm failure");
    }

    [Fact]
    public void TestConcurrentSimple()
    {
        const int threadCount = 100;
        const int iterations = 1000;
        const int limit = 10;

        var limiter = (SimpleLimiter<string>)PartitionedLimiter.NewBuilder<string>()
            .WithLimit(FixedLimit.Of(limit))
            .AddPartition("default", 1.0)
            .Build();

        var startLatch = new ManualResetEventSlim(false);
        int successCount = 0;
        int rejectionCount = 0;
        int maxConcurrent = 0;

        var threads = new List<Thread>();
        for (int i = 0; i < threadCount; i++)
        {
            var t = new Thread(() =>
            {
                startLatch.Wait();
                for (int j = 0; j < iterations; j++)
                {
                    IListener? listener = limiter.Acquire("default");
                    if (listener != null)
                    {
                        try
                        {
                            int current = limiter.GetInflight();
                            int prev;
                            do { prev = Volatile.Read(ref maxConcurrent); }
                            while (current > prev && Interlocked.CompareExchange(ref maxConcurrent, current, prev) != prev);
                            Interlocked.Increment(ref successCount);
                            Thread.Sleep(1);
                        }
                        finally
                        {
                            listener.OnSuccess();
                        }
                    }
                    else
                    {
                        Interlocked.Increment(ref rejectionCount);
                    }
                }
            });
            threads.Add(t);
            t.Start();
        }

        startLatch.Set();
        foreach (var t in threads) t.Join();

        Assert.True(maxConcurrent <= limit, $"Max concurrent {maxConcurrent} should not exceed limit");
        Assert.Equal(threadCount * iterations, successCount + rejectionCount);
    }
}
