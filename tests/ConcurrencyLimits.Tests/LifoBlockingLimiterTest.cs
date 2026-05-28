using System.Collections.Concurrent;
using System.Diagnostics;
using ConcurrencyLimits;
using ConcurrencyLimits.Limit;
using ConcurrencyLimits.Limiter;
using Xunit;

namespace ConcurrencyLimits.Tests;

public class LifoBlockingLimiterTest
{
    private readonly SettableLimit _limit = SettableLimit.StartingAt(4);
    private readonly SimpleLimiter<object?> _simpleLimiter;
    private readonly LifoBlockingLimiter<object?> _blockingLimiter;

    public LifoBlockingLimiterTest()
    {
        _simpleLimiter = SimpleLimiter.NewBuilder().WithLimit(_limit).Build<object?>();
        _blockingLimiter = LifoBlockingLimiter<object?>.NewBuilder(_simpleLimiter)
            .BacklogSize(10)
            .BacklogTimeout(TimeSpan.FromSeconds(1))
            .Build();
    }

    [Fact]
    public void BlockWhenFullAndTimeout()
    {
        for (int i = 0; i < 4; i++)
        {
            Assert.NotNull(_blockingLimiter.Acquire(null));
        }

        var sw = Stopwatch.StartNew();
        IListener? listener = _blockingLimiter.Acquire(null);
        sw.Stop();
        Assert.True(sw.Elapsed.TotalSeconds >= 1);
        Assert.Null(listener);
    }

    [Fact]
    public void UnblockWhenFullBeforeTimeout()
    {
        var listeners = AcquireN(_blockingLimiter, 4);

        _ = Task.Run(async () =>
        {
            await Task.Delay(250);
            listeners[0]!.OnSuccess();
        });

        var sw = Stopwatch.StartNew();
        IListener? listener = _blockingLimiter.Acquire(null);
        sw.Stop();
        Assert.True(sw.ElapsedMilliseconds >= 200, $"Duration = {sw.ElapsedMilliseconds}");
        Assert.NotNull(listener);
    }

    [Fact]
    [Trait("Category", "Timing")]
    public void RejectWhenBacklogSizeReached()
    {
        AcquireNAsync(_blockingLimiter, 14);

        Thread.Sleep(250);

        var sw = Stopwatch.StartNew();
        IListener? listener = _blockingLimiter.Acquire(null);
        sw.Stop();
        Assert.True(sw.ElapsedMilliseconds < 100, $"Duration = {sw.ElapsedMilliseconds}");
        Assert.Null(listener);
    }

    [Fact]
    [Trait("Category", "Timing")]
    public void AdaptWhenLimitIncreases()
    {
        AcquireN(_blockingLimiter, 4);

        _limit.SetLimitValue(5);

        var sw = Stopwatch.StartNew();
        IListener? listener = _blockingLimiter.Acquire(null);
        sw.Stop();
        Assert.True(sw.ElapsedMilliseconds < 100, $"Duration = {sw.ElapsedMilliseconds}");
        Assert.NotNull(listener);
    }

    [Fact]
    public void AdaptWhenLimitDecreases()
    {
        var listeners = AcquireN(_blockingLimiter, 4);

        _limit.SetLimitValue(3);

        listeners[0]!.OnSuccess();

        var sw = Stopwatch.StartNew();
        IListener? listener = _blockingLimiter.Acquire(null);
        sw.Stop();
        Assert.True(sw.Elapsed.TotalSeconds >= 1, $"Duration = {sw.ElapsedMilliseconds}");
        Assert.Null(listener);
    }

    [Fact]
    [Trait("Category", "Timing")]
    public void VerifyLifoOrder()
    {
        var firstBatch = AcquireN(_blockingLimiter, 4);

        var values = new ConcurrentQueue<int>();
        var tasks = new List<Task>();
        for (int i = 0; i < 5; i++)
        {
            Thread.Sleep(50);
            int captured = i;
            tasks.Add(Task.Run(() =>
            {
                IListener? listener = _blockingLimiter.Acquire(null);
                if (listener == null)
                {
                    values.Enqueue(-1);
                    return;
                }
                try
                {
                    values.Enqueue(captured);
                }
                finally
                {
                    listener.OnSuccess();
                }
            }));
        }

        foreach (IListener? listener in firstBatch)
        {
            Thread.Sleep(100);
            listener!.OnSuccess();
        }

        Task.WaitAll(tasks.ToArray(), TimeSpan.FromSeconds(10));

        Assert.Equal(new[] { 4, 3, 2, 1, 0 }, values.ToArray());
    }

    [Fact]
    public void TimeoutAcquireRaceCondition()
    {
        var limiter = LifoBlockingLimiter<object?>.NewBuilder(_simpleLimiter)
            .BacklogSize(1000)
            .BacklogTimeout(TimeSpan.FromMilliseconds(10))
            .Build();

        AcquireN(limiter, 3);

        for (int round = 0; round < 10; round++)
        {
            var firstTimeout = 0;
            IListener one = limiter.Acquire(null)!;
            var tasks = new List<Task>();
            for (int i = 0; i < 10; i++)
            {
                tasks.Add(Task.Run(() =>
                {
                    IListener? listener = limiter.Acquire(null);
                    if (listener != null)
                    {
                        listener.OnSuccess();
                    }
                    else if (Interlocked.CompareExchange(ref firstTimeout, 1, 0) == 0)
                    {
                        one.OnSuccess();
                    }
                }));
            }
            Task.WaitAll(tasks.ToArray());
            Assert.Equal(3, _simpleLimiter.GetInflight());
        }
    }

    [Fact]
    public void FixedTimeoutExposedWhenConfigured()
    {
        var limiter = LifoBlockingLimiter<object?>.NewBuilder(_simpleLimiter)
            .BacklogSize(10)
            .BacklogTimeoutMillis(250)
            .Build();

        Assert.Equal(250L, limiter.GetFixedBacklogTimeoutMillis());
    }

    [Fact]
    public void FixedTimeoutIsNullForDynamicBacklogTimeout()
    {
        var limiter = LifoBlockingLimiter<object?>.NewBuilder(_simpleLimiter)
            .BacklogSize(10)
            .BacklogTimeout(_ => TimeSpan.FromMilliseconds(50))
            .Build();

        Assert.Null(limiter.GetFixedBacklogTimeoutMillis());
    }

    private static List<IListener?> AcquireN(ILimiter<object?> limiter, int n)
    {
        var listeners = new List<IListener?>();
        for (int i = 0; i < n; i++)
        {
            IListener? listener = limiter.Acquire(null);
            Assert.NotNull(listener);
            listeners.Add(listener);
        }
        return listeners;
    }

    private void AcquireNAsync(ILimiter<object?> limiter, int n)
    {
        for (int i = 0; i < n; i++)
        {
            _ = Task.Run(() => limiter.Acquire(null));
        }
    }
}
