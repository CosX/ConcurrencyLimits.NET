using ConcurrencyLimits.Limit;
using ConcurrencyLimits.Limiter;
using ConcurrencyLimits.Polly;
using Polly;
using Xunit;

namespace ConcurrencyLimits.Tests;

public class ConcurrencyLimitPollyStrategyTest
{
    private static ResiliencePipeline BuildPipeline(int limit, out ILimiter<ResilienceContext> limiter)
    {
        limiter = new ResilienceContextLimiterBuilder()
            .WithLimit(FixedLimit.Of(limit))
            .AddPartition("all", 1.0)
            .PartitionResolver(_ => "all")
            .Build();

        return new ResiliencePipelineBuilder()
            .AddConcurrencyLimit(limiter)
            .Build();
    }

    [Fact]
    public async Task AllowsExecutionUnderLimit()
    {
        ResiliencePipeline pipeline = BuildPipeline(limit: 1, out _);

        int result = await pipeline.ExecuteAsync(_ => ValueTask.FromResult(42));

        Assert.Equal(42, result);
    }

    [Fact]
    public async Task RejectsWithExceptionWhenLimitExceeded()
    {
        var gate = new TaskCompletionSource();
        var release = new TaskCompletionSource();

        ResiliencePipeline pipeline = BuildPipeline(limit: 1, out _);

        Task holder = Task.Run(async () => await pipeline.ExecuteAsync(async _ =>
        {
            gate.TrySetResult();
            await release.Task;
        }));

        await gate.Task;

        await Assert.ThrowsAsync<ConcurrencyLimitRejectedException>(async () =>
            await pipeline.ExecuteAsync(_ => ValueTask.FromResult(0)));

        release.TrySetResult();
        await holder;
    }

    [Fact]
    public async Task ReleasesPermitAfterSuccess()
    {
        ResiliencePipeline pipeline = BuildPipeline(limit: 1, out _);

        for (int i = 0; i < 5; i++)
        {
            int value = await pipeline.ExecuteAsync(_ => ValueTask.FromResult(i));
            Assert.Equal(i, value);
        }
    }

    [Fact]
    public async Task ReleasesPermitAfterFault()
    {
        ResiliencePipeline pipeline = BuildPipeline(limit: 1, out _);

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await pipeline.ExecuteAsync<int>(_ => throw new InvalidOperationException("boom")));

        // Pipeline should still admit further work — permit was released via OnIgnore.
        int result = await pipeline.ExecuteAsync(_ => ValueTask.FromResult(7));
        Assert.Equal(7, result);
    }

    [Fact]
    public async Task CustomRejectionExceptionFactoryUsed()
    {
        var gate = new TaskCompletionSource();
        var release = new TaskCompletionSource();

        ILimiter<ResilienceContext> limiter = new ResilienceContextLimiterBuilder()
            .WithLimit(FixedLimit.Of(1))
            .AddPartition("all", 1.0)
            .PartitionResolver(_ => "all")
            .Build();

        ResiliencePipeline pipeline = new ResiliencePipelineBuilder()
            .AddConcurrencyLimit(new ConcurrencyLimitStrategyOptions
            {
                Limiter = limiter,
                RejectionExceptionFactory = _ => new InvalidOperationException("custom-reject"),
            })
            .Build();

        Task holder = Task.Run(async () => await pipeline.ExecuteAsync(async _ =>
        {
            gate.TrySetResult();
            await release.Task;
        }));
        await gate.Task;

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await pipeline.ExecuteAsync(_ => ValueTask.FromResult(0)));
        Assert.Equal("custom-reject", ex.Message);

        release.TrySetResult();
        await holder;
    }
}
