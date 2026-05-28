using System.Collections.Concurrent;
using ConcurrencyLimits;
using ConcurrencyLimits.Limit;
using ConcurrencyLimits.Limiter;
using Xunit;

namespace ConcurrencyLimits.Tests;

public class AbstractPartitionedLimiterTest
{
    private static AbstractPartitionedLimiter<string> NewPartitioned(Action<PartitionedLimiterBuilder<string>> configure)
    {
        var builder = PartitionedLimiter.NewBuilder<string>();
        configure(builder);
        return (AbstractPartitionedLimiter<string>)builder.Build();
    }

    [Fact]
    public void LimitAllocatedToBins()
    {
        var limiter = NewPartitioned(b => b
            .PartitionResolver(s => s)
            .AddPartition("batch", 0.3)
            .AddPartition("live", 0.7)
            .WithLimit(FixedLimit.Of(10)));

        Assert.Equal(3, limiter.GetPartition("batch")!.GetLimit());
        Assert.Equal(7, limiter.GetPartition("live")!.GetLimit());
    }

    [Fact]
    public void UseExcessCapacityUntilTotalLimit()
    {
        var limiter = NewPartitioned(b => b
            .PartitionResolver(s => s)
            .AddPartition("batch", 0.3)
            .AddPartition("live", 0.7)
            .WithLimit(FixedLimit.Of(10)));

        for (int i = 0; i < 10; i++)
        {
            Assert.NotNull(limiter.Acquire("batch"));
            Assert.Equal(i + 1, limiter.GetPartition("batch")!.GetInflight());
        }

        Assert.Null(limiter.Acquire("batch"));
    }

    [Fact]
    public void ExceedTotalLimitForUnusedBin()
    {
        var limiter = NewPartitioned(b => b
            .PartitionResolver(s => s)
            .AddPartition("batch", 0.3)
            .AddPartition("live", 0.7)
            .WithLimit(FixedLimit.Of(10)));

        for (int i = 0; i < 10; i++)
        {
            Assert.NotNull(limiter.Acquire("batch"));
            Assert.Equal(i + 1, limiter.GetPartition("batch")!.GetInflight());
        }

        Assert.Null(limiter.Acquire("batch"));

        for (int i = 0; i < 7; i++)
        {
            Assert.NotNull(limiter.Acquire("live"));
            Assert.Equal(i + 1, limiter.GetPartition("live")!.GetInflight());
        }

        Assert.Null(limiter.Acquire("live"));
    }

    [Fact]
    public void RejectOnceAllLimitsReached()
    {
        var limiter = NewPartitioned(b => b
            .PartitionResolver(s => s)
            .AddPartition("batch", 0.3)
            .AddPartition("live", 0.7)
            .WithLimit(FixedLimit.Of(10)));

        for (int i = 0; i < 3; i++)
        {
            Assert.NotNull(limiter.Acquire("batch"));
            Assert.Equal(i + 1, limiter.GetPartition("batch")!.GetInflight());
            Assert.Equal(i + 1, limiter.GetInflight());
        }

        for (int i = 0; i < 7; i++)
        {
            Assert.NotNull(limiter.Acquire("live"));
            Assert.Equal(i + 1, limiter.GetPartition("live")!.GetInflight());
            Assert.Equal(i + 4, limiter.GetInflight());
        }

        Assert.Null(limiter.Acquire("batch"));
        Assert.Null(limiter.Acquire("live"));
    }

    [Fact]
    public void ReleaseLimit()
    {
        var limiter = NewPartitioned(b => b
            .PartitionResolver(s => s)
            .AddPartition("batch", 0.3)
            .AddPartition("live", 0.7)
            .WithLimit(FixedLimit.Of(10)));

        IListener? completion = limiter.Acquire("batch");
        for (int i = 1; i < 10; i++)
        {
            Assert.NotNull(limiter.Acquire("batch"));
            Assert.Equal(i + 1, limiter.GetPartition("batch")!.GetInflight());
        }

        Assert.Equal(10, limiter.GetInflight());
        Assert.Null(limiter.Acquire("batch"));

        completion!.OnSuccess();
        Assert.Equal(9, limiter.GetPartition("batch")!.GetInflight());
        Assert.Equal(9, limiter.GetInflight());

        Assert.NotNull(limiter.Acquire("batch"));
        Assert.Equal(10, limiter.GetPartition("batch")!.GetInflight());
        Assert.Equal(10, limiter.GetInflight());
    }

    [Fact]
    public void SetLimitReservesBusy()
    {
        var limit = SettableLimit.StartingAt(10);

        var limiter = NewPartitioned(b => b
            .PartitionResolver(s => s)
            .AddPartition("batch", 0.3)
            .AddPartition("live", 0.7)
            .WithLimit(limit));

        limit.SetLimitValue(10);
        Assert.Equal(3, limiter.GetPartition("batch")!.GetLimit());
        Assert.NotNull(limiter.Acquire("batch"));
        Assert.Equal(1, limiter.GetPartition("batch")!.GetInflight());
        Assert.Equal(1, limiter.GetInflight());

        limit.SetLimitValue(20);
        Assert.Equal(6, limiter.GetPartition("batch")!.GetLimit());
        Assert.Equal(1, limiter.GetPartition("batch")!.GetInflight());
        Assert.Equal(1, limiter.GetInflight());
    }

    [Fact]
    public void TestBypassPartitionedLimiter()
    {
        var limiter = NewPartitioned(b => b
            .PartitionResolver(s => s)
            .AddPartition("batch", 0.1)
            .AddPartition("live", 0.9)
            .WithLimit(FixedLimit.Of(10))
            .BypassLimitResolver(ctx => ctx is string s && s.Contains("admin")));

        Assert.NotNull(limiter.Acquire("batch"));
        Assert.Equal(1, limiter.GetPartition("batch")!.GetInflight());
        Assert.NotNull(limiter.Acquire("admin"));

        for (int i = 0; i < 9; i++)
        {
            Assert.NotNull(limiter.Acquire("live"));
            Assert.Equal(i + 1, limiter.GetPartition("live")!.GetInflight());
            Assert.NotNull(limiter.Acquire("admin"));
        }

        Assert.Null(limiter.Acquire("batch"));
        Assert.Equal(1, limiter.GetPartition("batch")!.GetInflight());
        Assert.Null(limiter.Acquire("live"));
        Assert.Equal(9, limiter.GetPartition("live")!.GetInflight());
        Assert.Equal(10, limiter.GetInflight());
        Assert.NotNull(limiter.Acquire("admin"));
    }

    [Fact]
    public void TestBypassSimpleLimiter()
    {
        var limiter = (SimpleLimiter<string>)PartitionedLimiter.NewBuilder<string>()
            .WithLimit(FixedLimit.Of(10))
            .BypassLimitResolver(ctx => ctx is string s && s.Contains("admin"))
            .Build();

        int inflightCount = 0;
        for (int i = 0; i < 5; i++)
        {
            Assert.NotNull(limiter.Acquire("request"));
            Assert.Equal(i + 1, limiter.GetInflight());
            inflightCount++;
        }

        for (int i = 0; i < 15; i++)
        {
            Assert.NotNull(limiter.Acquire("admin"));
            Assert.Equal(inflightCount, limiter.GetInflight());
        }

        for (int i = 0; i < 5; i++)
        {
            Assert.NotNull(limiter.Acquire("request"));
            Assert.Equal(inflightCount + i + 1, limiter.GetInflight());
        }

        for (int i = 0; i < 10; i++)
        {
            Assert.Null(limiter.Acquire("request"));
            Assert.NotNull(limiter.Acquire("admin"));
        }
    }

    [Fact]
    public void TestConcurrentPartitions()
    {
        const int threadCount = 5;
        const int iterations = 500;
        const int limit = 20;

        var limiter = NewPartitioned(b => b
            .WithLimit(FixedLimit.Of(limit))
            .PartitionResolver(s => s)
            .AddPartition("A", 0.5)
            .AddPartition("B", 0.3)
            .AddPartition("C", 0.2));

        var startLatch = new ManualResetEventSlim(false);
        var successCounts = new ConcurrentDictionary<string, int>();
        var rejectionCounts = new ConcurrentDictionary<string, int>();
        var maxConcurrents = new ConcurrentDictionary<string, int>();
        int globalMaxInflight = 0;

        var threads = new List<Thread>();
        foreach (string partition in new[] { "A", "B", "C" })
        {
            successCounts[partition] = 0;
            rejectionCounts[partition] = 0;
            maxConcurrents[partition] = 0;

            for (int i = 0; i < threadCount; i++)
            {
                string p = partition;
                var t = new Thread(() =>
                {
                    startLatch.Wait();
                    for (int j = 0; j < iterations; j++)
                    {
                        IListener? listener = limiter.Acquire(p);
                        if (listener != null)
                        {
                            try
                            {
                                int current = limiter.GetPartition(p)!.GetInflight();
                                UpdateMax(maxConcurrents, p, current);
                                successCounts.AddOrUpdate(p, 1, (_, v) => v + 1);
                                UpdateGlobalMax(ref globalMaxInflight, limiter.GetInflight());
                                Thread.Sleep(1);
                            }
                            finally
                            {
                                listener.OnSuccess();
                            }
                        }
                        else
                        {
                            rejectionCounts.AddOrUpdate(p, 1, (_, v) => v + 1);
                        }
                    }
                });
                threads.Add(t);
                t.Start();
            }
        }

        startLatch.Set();
        foreach (var t in threads) t.Join();

        foreach (string partition in new[] { "A", "B", "C" })
        {
            Assert.True(maxConcurrents[partition] <= limit);
            Assert.Equal(threadCount * iterations, successCounts[partition] + rejectionCounts[partition]);
        }

        Assert.True(globalMaxInflight <= limit + threadCount);
    }

    private static void UpdateMax(ConcurrentDictionary<string, int> map, string key, int value)
        => map.AddOrUpdate(key, value, (_, existing) => Math.Max(existing, value));

    private static void UpdateGlobalMax(ref int target, int value)
    {
        int prev;
        do { prev = Volatile.Read(ref target); }
        while (value > prev && Interlocked.CompareExchange(ref target, value, prev) != prev);
    }
}
