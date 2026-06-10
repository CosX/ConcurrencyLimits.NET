using ConcurrencyLimits.Executors;
using ConcurrencyLimits.Limit;
using ConcurrencyLimits.Limiter;
using Xunit;

namespace ConcurrencyLimits.Tests;

public class BuilderValidationTest
{
    [Fact]
    public void PartitionsWithoutResolverThrows()
    {
        var builder = PartitionedLimiter.NewBuilder<string>().AddPartition("batch", 1.0);

        Assert.Throws<InvalidOperationException>(() => builder.Build());
    }

    [Fact]
    public void ResolverWithoutPartitionsThrows()
    {
        var builder = PartitionedLimiter.NewBuilder<string>().PartitionResolver(s => s);

        Assert.Throws<InvalidOperationException>(() => builder.Build());
    }

    [Fact]
    public void NoPartitionsAndNoResolverBuildsSimpleLimiter()
    {
        ILimiter<string> limiter = PartitionedLimiter.NewBuilder<string>().Build();

        Assert.IsType<SimpleLimiter<string>>(limiter);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-0.5)]
    [InlineData(1.5)]
    public void VegasRejectsInvalidSmoothing(double smoothing)
        => Assert.Throws<ArgumentException>(() => VegasLimit.NewBuilder().Smoothing(smoothing));

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void VegasRejectsInvalidProbeMultiplier(int probeMultiplier)
        => Assert.Throws<ArgumentException>(() => VegasLimit.NewBuilder().ProbeMultiplier(probeMultiplier));

    [Fact]
    public void GradientRejectsInvalidSmoothing()
        => Assert.Throws<ArgumentException>(() => GradientLimit.NewBuilder().Smoothing(0.0));

    [Fact]
    public void Gradient2RejectsInvalidSmoothing()
        => Assert.Throws<ArgumentException>(() => Gradient2Limit.NewBuilder().Smoothing(1.5));

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void LifoBacklogSizeMustBePositive(int size)
    {
        var limiter = SimpleLimiter.NewBuilder().WithLimit(FixedLimit.Of(1)).Build<string>();

        Assert.Throws<ArgumentException>(() => LifoBlockingLimiter<string>.NewBuilder(limiter).BacklogSize(size));
    }

    [Fact]
    public void MaxDelayedThreadsMustBeNonNegative()
        => Assert.Throws<ArgumentException>(() => PartitionedLimiter.NewBuilder<string>().MaxDelayedThreads(-1));

    [Fact]
    public async Task BuilderBuiltExecutorBlocksInsteadOfRejecting()
    {
        BlockingAdaptiveExecutor executor = BlockingAdaptiveExecutor.NewBuilder()
            .WithLimiter(SimpleLimiter.NewBuilder().WithLimit(FixedLimit.Of(1)).Build<object?>())
            .Build();

        var firstRunning = new TaskCompletionSource();
        var releaseFirst = new TaskCompletionSource();
        var secondDone = new TaskCompletionSource();

        executor.Execute(() =>
        {
            firstRunning.SetResult();
            releaseFirst.Task.Wait();
        });
        await firstRunning.Task;

        // Pre-fix this threw RejectedExecutionException immediately; now it must block until released.
        Task second = Task.Run(() => executor.Execute(() => secondDone.SetResult()));
        await Task.Delay(100);
        Assert.False(second.IsCompleted);

        releaseFirst.SetResult();
        await second;
        await secondDone.Task;
    }
}
