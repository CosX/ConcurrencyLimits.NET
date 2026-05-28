using ConcurrencyLimits.Limiter;

namespace ConcurrencyLimits.Grpc.Server;

/// <summary>Builder for an <see cref="ILimiter{TContext}"/> that limits gRPC server calls.</summary>
public sealed class GrpcServerLimiterBuilder
    : AbstractPartitionedLimiter<IGrpcServerRequestContext>.Builder<GrpcServerLimiterBuilder>
{
    protected override GrpcServerLimiterBuilder Self() => this;

    /// <summary>Partition the limit by full method name.</summary>
    public GrpcServerLimiterBuilder PartitionByMethod()
        => PartitionResolver(ctx => ctx.Method);

    /// <summary>Partition the limit by a request header.</summary>
    public GrpcServerLimiterBuilder PartitionByHeader(string headerKey)
        => PartitionResolver(ctx => ctx.Headers.GetValue(headerKey));

    /// <summary>Bypass the limit when the predicate evaluates to true for the call.</summary>
    public GrpcServerLimiterBuilder BypassLimitResolver(Func<IGrpcServerRequestContext, bool> shouldBypass)
        => BypassLimitResolver(ctx => ctx is IGrpcServerRequestContext c && shouldBypass(c));

    /// <summary>Bypass the limit if the full method name matches.</summary>
    public GrpcServerLimiterBuilder BypassLimitByMethod(string fullMethodName)
        => BypassLimitResolver(ctx => fullMethodName == ctx.Method);

    /// <summary>Bypass the limit if the named header matches the given value.</summary>
    public GrpcServerLimiterBuilder BypassLimitByHeader(string headerKey, string value)
        => BypassLimitResolver(ctx => value == ctx.Headers.GetValue(headerKey));
}
