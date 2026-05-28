using ConcurrencyLimits.AspNetCore;
using ConcurrencyLimits.Limit;
using ConcurrencyLimits.Limiter;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace ConcurrencyLimits.Tests;

public class ConcurrencyLimitMiddlewareTest
{
    private static ConcurrencyLimitMiddleware Build(RequestDelegate next, int limit)
    {
        ILimiter<HttpContext> limiter = new HttpRequestLimiterBuilder()
            .WithLimit(FixedLimit.Of(limit))
            .AddPartition("all", 1.0)
            .PartitionResolver(_ => "all")
            .Build();
        return new ConcurrencyLimitMiddleware(next, limiter);
    }

    [Fact]
    public async Task AllowsRequestUnderLimit()
    {
        bool called = false;
        var middleware = Build(_ => { called = true; return Task.CompletedTask; }, limit: 1);

        var ctx = new DefaultHttpContext();
        await middleware.InvokeAsync(ctx);

        Assert.True(called);
        Assert.Equal(StatusCodes.Status200OK, ctx.Response.StatusCode);
    }

    [Fact]
    public async Task Returns429WhenLimitExceeded()
    {
        var gate = new TaskCompletionSource();
        var release = new TaskCompletionSource();

        // A handler that blocks so the single permit stays held.
        var middleware = Build(async _ =>
        {
            gate.TrySetResult();
            await release.Task;
        }, limit: 1);

        var first = Task.Run(async () => await middleware.InvokeAsync(new DefaultHttpContext()));
        await gate.Task; // ensure the permit is held

        var ctx = new DefaultHttpContext();
        await middleware.InvokeAsync(ctx);
        Assert.Equal(StatusCodes.Status429TooManyRequests, ctx.Response.StatusCode);

        release.TrySetResult();
        await first;
    }
}
