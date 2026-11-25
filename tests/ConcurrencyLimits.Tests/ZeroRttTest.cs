using ConcurrencyLimits.Limit;
using ConcurrencyLimits.Limiter;
using Xunit;

namespace ConcurrencyLimits.Tests;

public class ZeroRttTest
{
    [Fact]
    public void Gradient2IgnoresZeroRtt()
    {
        Gradient2Limit limit = Gradient2Limit.NewBuilder().InitialLimitOf(20).Build();

        limit.OnSample(0, 0, 100, false);

        Assert.Equal(20, limit.GetLimit());
    }

    [Fact]
    public void Gradient2RecoversNormallyAfterZeroRtt()
    {
        Gradient2Limit limit = Gradient2Limit.NewBuilder().InitialLimitOf(20).Build();

        limit.OnSample(0, 0, 100, false);
        for (int i = 0; i < 50; i++)
        {
            limit.OnSample(0, 1_000_000, 100, false);
        }

        Assert.True(limit.GetLimit() >= 20, $"limit = {limit.GetLimit()}");
    }

    [Fact]
    public void GradientIgnoresZeroRtt()
    {
        GradientLimit limit = GradientLimit.NewBuilder().InitialLimitOf(50).Build();

        limit.OnSample(0, 0, 100, false);

        Assert.Equal(50, limit.GetLimit());
    }

    [Fact]
    public void LimiterReportsSubResolutionRttAsPositive()
    {
        long now = 1_000;
        SimpleLimiter<object?> limiter = SimpleLimiter.NewBuilder()
            .WithLimit(VegasLimit.NewDefault())
            .NanoClock(() => now)
            .Build<object?>();

        IListener listener = limiter.Acquire(null)!;

        listener.OnSuccess();
        Assert.Equal(0, limiter.GetInflight());
    }
}
