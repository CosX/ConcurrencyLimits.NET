using ConcurrencyLimits.Internal;
using Grpc.Core;
using Grpc.Core.Interceptors;

namespace ConcurrencyLimits.Grpc.Server;

/// <summary>
/// <see cref="Interceptor"/> that enforces per service and/or per method concurrent request limits and returns
/// <see cref="StatusCode.Unavailable"/> when the limit has been reached. Only unary calls are limited.
/// </summary>
/// <remarks>
/// Outcome mapping: success feeds an RTT sample; <see cref="StatusCode.DeadlineExceeded"/> and
/// <see cref="StatusCode.Cancelled"/> are reported as drops (deliberate deviation from the Java library,
/// which ignores cancellations — a client giving up is treated here as a latency signal the algorithm
/// must see); all other failures are ignored.
/// </remarks>
public sealed class ConcurrencyLimitServerInterceptor : Interceptor
{
    private static readonly Status LimitExceededStatus =
        new(StatusCode.Unavailable, "Server concurrency limit reached");

    private readonly ILimiter<IGrpcServerRequestContext> _limiter;
    private readonly Func<Status> _statusSupplier;

    public sealed class Builder
    {
        private readonly ILimiter<IGrpcServerRequestContext> _limiter;
        private Func<Status> _statusSupplier = () => LimitExceededStatus;

        public Builder(ILimiter<IGrpcServerRequestContext> limiter)
        {
            Preconditions.CheckArgument(limiter != null, "limiter cannot be null");
            _limiter = limiter!;
        }

        public Builder StatusSupplier(Func<Status> supplier)
        {
            Preconditions.CheckArgument(supplier != null, "statusSupplier cannot be null");
            _statusSupplier = supplier!;
            return this;
        }

        public ConcurrencyLimitServerInterceptor Build() => new(_limiter, _statusSupplier);
    }

    public static Builder NewBuilder(ILimiter<IGrpcServerRequestContext> limiter) => new(limiter);

    private ConcurrencyLimitServerInterceptor(ILimiter<IGrpcServerRequestContext> limiter, Func<Status> statusSupplier)
    {
        _limiter = limiter;
        _statusSupplier = statusSupplier;
    }

    public ConcurrencyLimitServerInterceptor(ILimiter<IGrpcServerRequestContext> limiter)
        : this(limiter, () => LimitExceededStatus) { }

    public override async Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
        TRequest request,
        ServerCallContext context,
        UnaryServerMethod<TRequest, TResponse> continuation)
    {
        IListener? listener = _limiter.Acquire(new GrpcServerRequestContext(context));
        if (listener == null)
        {
            throw new RpcException(_statusSupplier());
        }

        try
        {
            TResponse response = await continuation(request, context);
            listener.OnSuccess();
            return response;
        }
        catch (RpcException ex) when (ex.StatusCode is StatusCode.DeadlineExceeded or StatusCode.Cancelled)
        {
            listener.OnDropped();
            throw;
        }
        catch
        {
            listener.OnIgnore();
            throw;
        }
    }
}
