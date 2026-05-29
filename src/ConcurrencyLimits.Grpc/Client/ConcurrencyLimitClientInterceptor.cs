using ConcurrencyLimits.Internal;
using Grpc.Core;
using Grpc.Core.Interceptors;

namespace ConcurrencyLimits.Grpc.Client;

/// <summary>
/// <see cref="Interceptor"/> that enforces per service and/or per method concurrent request limits and returns
/// <see cref="StatusCode.Unavailable"/> when the limit has been reached. Only unary calls are limited.
/// </summary>
public sealed class ConcurrencyLimitClientInterceptor : Interceptor
{
    private static readonly Status LimitExceededStatus =
        new(StatusCode.Unavailable, "Client concurrency limit reached");

    private readonly ILimiter<IGrpcClientRequestContext> _limiter;

    public ConcurrencyLimitClientInterceptor(ILimiter<IGrpcClientRequestContext> limiter)
    {
        Preconditions.CheckArgument(limiter != null, "limiter cannot be null");
        _limiter = limiter!;
    }

    public override TResponse BlockingUnaryCall<TRequest, TResponse>(
        TRequest request,
        ClientInterceptorContext<TRequest, TResponse> context,
        BlockingUnaryCallContinuation<TRequest, TResponse> continuation)
    {
        IListener listener = Acquire(context.Method.FullName);

        try
        {
            TResponse response = continuation(request, context);
            listener.OnSuccess();
            return response;
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.Unavailable)
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

    public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
        TRequest request,
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncUnaryCallContinuation<TRequest, TResponse> continuation)
    {
        IListener listener = Acquire(context.Method.FullName);

        AsyncUnaryCall<TResponse> call;
        try
        {
            call = continuation(request, context);
        }
        catch
        {
            // continuation threw before producing a call; release the token we acquired.
            listener.OnIgnore();
            throw;
        }
        Task<TResponse> responseAsync = HandleResponse(call.ResponseAsync, listener);

        return new AsyncUnaryCall<TResponse>(
            responseAsync,
            call.ResponseHeadersAsync,
            call.GetStatus,
            call.GetTrailers,
            call.Dispose);
    }

    private IListener Acquire(string method)
        => _limiter.Acquire(new GrpcClientRequestContext(method)) ?? throw new RpcException(LimitExceededStatus);

    private static async Task<TResponse> HandleResponse<TResponse>(Task<TResponse> inner, IListener listener)
    {
        try
        {
            TResponse response = await inner;
            listener.OnSuccess();
            return response;
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.Unavailable)
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
