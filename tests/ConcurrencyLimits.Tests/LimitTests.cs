using ConcurrencyLimits.Limit;
using Xunit;

namespace ConcurrencyLimits.Tests;

internal static class Nanos
{
    public static long Ms(long ms) => ms * 1_000_000L;
    public static long Sec(long s) => s * 1_000_000_000L;
}

public class AIMDLimitTest
{
    [Fact]
    public void TestDefault()
    {
        var limiter = AIMDLimit.NewBuilder().InitialLimitOf(10).Build();
        Assert.Equal(10, limiter.GetLimit());
    }

    [Fact]
    public void IncreaseOnSuccess()
    {
        var limiter = AIMDLimit.NewBuilder().InitialLimitOf(20).Build();
        limiter.OnSample(0, Nanos.Ms(1), 10, false);
        Assert.Equal(21, limiter.GetLimit());
    }

    [Fact]
    public void DecreaseOnDrops()
    {
        var limiter = AIMDLimit.NewBuilder().InitialLimitOf(30).Build();
        limiter.OnSample(0, 0, 0, true);
        Assert.Equal(27, limiter.GetLimit());
    }

    [Fact]
    public void SuccessOverflow()
    {
        var limiter = AIMDLimit.NewBuilder().InitialLimitOf(21).MaxLimit(21).MinLimit(0).Build();
        limiter.OnSample(0, Nanos.Ms(1), 10, false);
        Assert.Equal(21, limiter.GetLimit());
    }
}

public class VegasLimitTest
{
    private static VegasLimit Create() => VegasLimit.NewBuilder()
        .Alpha(3)
        .Beta(6)
        .Smoothing(1.0)
        .InitialLimitOf(10)
        .MaxConcurrency(20)
        .Build();

    [Fact]
    public void LargeLimitIncrease()
    {
        var limit = VegasLimit.NewBuilder().InitialLimitOf(10000).MaxConcurrency(20000).Build();
        limit.OnSample(0, Nanos.Sec(10), 5000, false);
        Assert.Equal(10000, limit.GetLimit());
        limit.OnSample(0, Nanos.Sec(10), 6000, false);
        Assert.Equal(10024, limit.GetLimit());
    }

    [Fact]
    public void IncreaseLimit()
    {
        var limit = Create();
        limit.OnSample(0, Nanos.Ms(10), 10, false);
        Assert.Equal(10, limit.GetLimit());
        limit.OnSample(0, Nanos.Ms(10), 11, false);
        Assert.Equal(16, limit.GetLimit());
    }

    [Fact]
    public void DecreaseLimit()
    {
        var limit = Create();
        limit.OnSample(0, Nanos.Ms(10), 10, false);
        Assert.Equal(10, limit.GetLimit());
        limit.OnSample(0, Nanos.Ms(50), 11, false);
        Assert.Equal(9, limit.GetLimit());
    }

    [Fact]
    public void NoChangeIfWithinThresholds()
    {
        var limit = Create();
        limit.OnSample(0, Nanos.Ms(10), 10, false);
        Assert.Equal(10, limit.GetLimit());
        limit.OnSample(0, Nanos.Ms(14), 14, false);
        Assert.Equal(10, limit.GetLimit());
    }

    [Fact]
    public void DecreaseSmoothing()
    {
        var limit = VegasLimit.NewBuilder()
            .DecreaseFunction(current => current / 2)
            .Smoothing(0.5)
            .InitialLimitOf(100)
            .MaxConcurrency(200)
            .Build();

        limit.OnSample(0, Nanos.Ms(10), 100, false);
        Assert.Equal(100, limit.GetLimit());

        limit.OnSample(0, Nanos.Ms(20), 100, false);
        Assert.Equal(75, limit.GetLimit());

        limit.OnSample(0, Nanos.Ms(20), 100, false);
        Assert.Equal(56, limit.GetLimit());
    }

    [Fact]
    public void DecreaseWithoutSmoothing()
    {
        var limit = VegasLimit.NewBuilder()
            .DecreaseFunction(current => current / 2)
            .InitialLimitOf(100)
            .MaxConcurrency(200)
            .Build();

        limit.OnSample(0, Nanos.Ms(10), 100, false);
        Assert.Equal(100, limit.GetLimit());

        limit.OnSample(0, Nanos.Ms(20), 100, false);
        Assert.Equal(50, limit.GetLimit());

        limit.OnSample(0, Nanos.Ms(20), 100, false);
        Assert.Equal(25, limit.GetLimit());
    }
}
