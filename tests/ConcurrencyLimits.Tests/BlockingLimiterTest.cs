using System.Diagnostics;
using ConcurrencyLimits;
using ConcurrencyLimits.Limit;
using ConcurrencyLimits.Limiter;
using Xunit;

namespace ConcurrencyLimits.Tests;

public class BlockingLimiterTest
{
    [Fact]
    public void Test()
    {
        var limit = SettableLimit.StartingAt(10);
        var limiter = BlockingLimiter<object?>.Wrap(SimpleLimiter.NewBuilder().WithLimit(limit).Build<object?>());

        var listeners = new LinkedList<IListener>();
        for (int i = 0; i < 10; i++)
        {
            IListener? l = limiter.Acquire(null);
            if (l != null) listeners.AddLast(l);
        }

        limit.SetLimitValue(1);

        while (listeners.Count != 0)
        {
            IListener l = listeners.First!.Value;
            listeners.RemoveFirst();
            l.OnSuccess();
        }

        limiter.Acquire(null);
    }

    [Fact]
    public void TestMultipleBlockedThreads()
    {
        const int numThreads = 8;
        var limit = SettableLimit.StartingAt(1);
        var limiter = BlockingLimiter<object?>.Wrap(SimpleLimiter.NewBuilder().WithLimit(limit).Build<object?>());

        var tasks = new List<Task>();
        for (int i = 0; i < numThreads; i++)
        {
            tasks.Add(Task.Run(() => limiter.Acquire(null)!.OnSuccess()));
        }

        Assert.True(Task.WaitAll(tasks.ToArray(), TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public void TestTimeout()
    {
        var timeout = TimeSpan.FromMilliseconds(50);
        var limit = SettableLimit.StartingAt(1);
        var limiter = BlockingLimiter<object?>.Wrap(SimpleLimiter.NewBuilder().WithLimit(limit).Build<object?>(), timeout);

        limiter.Acquire(null);

        var sw = Stopwatch.StartNew();
        Assert.Null(limiter.Acquire(null));
        sw.Stop();

        Assert.True(sw.Elapsed >= timeout, $"Delay was {sw.ElapsedMilliseconds} millis");
    }

    [Fact]
    public void TestNoTimeout()
    {
        var limit = SettableLimit.StartingAt(1);
        var limiter = BlockingLimiter<object?>.Wrap(SimpleLimiter.NewBuilder().WithLimit(limit).Build<object?>());
        limiter.Acquire(null);

        var task = Task.Run(() => limiter.Acquire(null));
        // The second acquire blocks; it must not complete within the timeout.
        Assert.False(task.Wait(TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void FailOnHighTimeout()
    {
        var limit = SettableLimit.StartingAt(1);
        Assert.Throws<ArgumentException>(() =>
            BlockingLimiter<object?>.Wrap(SimpleLimiter.NewBuilder().WithLimit(limit).Build<object?>(), TimeSpan.FromDays(1)));
    }
}
