using ConcurrencyLimits.AspNetCore;
using ConcurrencyLimits.Grpc.Server;
using ConcurrencyLimits.Limit;
using ConcurrencyLimits.Polly;
using Grpc.Core;
using Microsoft.AspNetCore.Http;
using Polly;
using Xunit;

namespace ConcurrencyLimits.Tests;

public class BypassHelperTest
{
    [Fact]
    public void HttpBypassHelpersBypassMatchingRequests()
    {
        ILimiter<HttpContext> limiter = new HttpRequestLimiterBuilder()
            .WithLimit(FixedLimit.Of(1))
            .BypassLimitByPath("/health")
            .BypassLimitByHeader("x-bypass", "1")
            .BypassLimitByMethod("OPTIONS")
            .BypassLimitResolver(ctx => ctx.Request.Query.ContainsKey("skip"))
            .Build();

        Assert.NotNull(limiter.Acquire(new DefaultHttpContext()));

        var byPath = new DefaultHttpContext();
        byPath.Request.Path = "/health";
        var byHeader = new DefaultHttpContext();
        byHeader.Request.Headers["x-bypass"] = "1";
        var byMethod = new DefaultHttpContext();
        byMethod.Request.Method = "OPTIONS";
        var byPredicate = new DefaultHttpContext();
        byPredicate.Request.QueryString = new QueryString("?skip=1");

        Assert.NotNull(limiter.Acquire(byPath));
        Assert.NotNull(limiter.Acquire(byHeader));
        Assert.NotNull(limiter.Acquire(byMethod));
        Assert.NotNull(limiter.Acquire(byPredicate));
        Assert.Null(limiter.Acquire(new DefaultHttpContext()));
    }

    private sealed class GrpcContext(string method, Metadata? headers = null) : IGrpcServerRequestContext
    {
        public string Method { get; } = method;
        public Metadata Headers { get; } = headers ?? new Metadata();
    }

    [Fact]
    public void GrpcServerBypassHelpersBypassMatchingCalls()
    {
        ILimiter<IGrpcServerRequestContext> limiter = new GrpcServerLimiterBuilder()
            .WithLimit(FixedLimit.Of(1))
            .BypassLimitByMethod("/svc/Health")
            .BypassLimitByHeader("x-bypass", "1")
            .BypassLimitResolver(ctx => ctx.Method == "/svc/Skip")
            .Build();

        Assert.NotNull(limiter.Acquire(new GrpcContext("/svc/Work")));

        Assert.NotNull(limiter.Acquire(new GrpcContext("/svc/Health")));
        Assert.NotNull(limiter.Acquire(new GrpcContext("/svc/Work", new Metadata { { "x-bypass", "1" } })));
        Assert.NotNull(limiter.Acquire(new GrpcContext("/svc/Skip")));
        Assert.Null(limiter.Acquire(new GrpcContext("/svc/Work")));
    }

    [Fact]
    public void PollyBypassHelpersBypassMatchingContexts()
    {
        ILimiter<ResilienceContext> limiter = new ResilienceContextLimiterBuilder()
            .WithLimit(FixedLimit.Of(1))
            .BypassLimitByOperationKey("health")
            .BypassLimitResolver(ctx => ctx.OperationKey == "skip")
            .Build();

        Assert.NotNull(limiter.Acquire(ResilienceContextPool.Shared.Get("work")));

        Assert.NotNull(limiter.Acquire(ResilienceContextPool.Shared.Get("health")));
        Assert.NotNull(limiter.Acquire(ResilienceContextPool.Shared.Get("skip")));
        Assert.Null(limiter.Acquire(ResilienceContextPool.Shared.Get("work")));
    }
}
