using ConcurrencyLimits.Limiter;

namespace ConcurrencyLimits.Grpc.Client;

/// <summary>Builder for an <see cref="ILimiter{TContext}"/> that limits gRPC client calls.</summary>
public sealed class GrpcClientLimiterBuilder
    : AbstractPartitionedLimiter<IGrpcClientRequestContext>.Builder<GrpcClientLimiterBuilder>
{
    private bool _blockOnLimit;

    protected override GrpcClientLimiterBuilder Self() => this;

    /// <summary>Partition the limit by full method name.</summary>
    public GrpcClientLimiterBuilder PartitionByMethod()
        => PartitionResolver(ctx => ctx.Method);

    /// <summary>Bypass the limit when the predicate evaluates to true for the call.</summary>
    public GrpcClientLimiterBuilder BypassLimitResolver(Func<IGrpcClientRequestContext, bool> shouldBypass)
        => BypassLimitResolver(ctx => ctx is IGrpcClientRequestContext c && shouldBypass(c));

    /// <summary>Bypass the limit if the full method name matches.</summary>
    public GrpcClientLimiterBuilder BypassLimitByMethod(string fullMethodName)
        => BypassLimitResolver(ctx => fullMethodName == ctx.Method);

    /// <summary>
    /// When set to true new calls will block when the limit has been reached instead of failing fast
    /// with an Unavailable status.
    /// <para>WARNING: blocking is synchronous and also applies to async calls — the calling thread is
    /// parked (up to <see cref="BlockingLimiter{TContext}.MaxTimeout"/>) inside <c>AsyncUnaryCall</c>.
    /// Avoid in thread-pool-bound async applications.</para>
    /// </summary>
    public GrpcClientLimiterBuilder BlockOnLimit(bool blockOnLimit)
    {
        _blockOnLimit = blockOnLimit;
        return this;
    }

    public new ILimiter<IGrpcClientRequestContext> Build()
    {
        ILimiter<IGrpcClientRequestContext> limiter = base.Build();
        return _blockOnLimit ? BlockingLimiter<IGrpcClientRequestContext>.Wrap(limiter) : limiter;
    }
}
