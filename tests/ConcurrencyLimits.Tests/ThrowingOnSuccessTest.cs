using ConcurrencyLimits.AspNetCore;
using ConcurrencyLimits.Executors;
using ConcurrencyLimits.Grpc.Client;
using ConcurrencyLimits.Grpc.Server;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace ConcurrencyLimits.Tests;

public class ThrowingOnSuccessTest
{
    private sealed class ThrowingOnSuccessListener : IListener
    {
        public int SuccessCount;
        public int IgnoreCount;
        public int DroppedCount;

        public void OnSuccess()
        {
            SuccessCount++;
            throw new InvalidOperationException("OnSuccess failed");
        }

        public void OnIgnore() => IgnoreCount++;
        public void OnDropped() => DroppedCount++;
    }

    private sealed class FixedListenerLimiter<TContext>(IListener listener) : ILimiter<TContext>
    {
        public IListener? Acquire(TContext context) => listener;
    }

    private static void AssertReleasedOnce(ThrowingOnSuccessListener listener)
    {
        Assert.Equal(1, listener.SuccessCount);
        Assert.Equal(0, listener.IgnoreCount);
        Assert.Equal(0, listener.DroppedCount);
    }

    [Fact]
    public async Task Middleware()
    {
        var listener = new ThrowingOnSuccessListener();
        var middleware = new ConcurrencyLimitMiddleware(_ => Task.CompletedTask, new FixedListenerLimiter<HttpContext>(listener));

        await Assert.ThrowsAsync<InvalidOperationException>(() => middleware.InvokeAsync(new DefaultHttpContext()));

        AssertReleasedOnce(listener);
    }

    private sealed class TestServerCallContext : ServerCallContext
    {
        protected override string MethodCore => "/test.Service/Method";
        protected override string HostCore => "localhost";
        protected override string PeerCore => "ipv4:127.0.0.1";
        protected override DateTime DeadlineCore => DateTime.MaxValue;
        protected override Metadata RequestHeadersCore { get; } = new();
        protected override CancellationToken CancellationTokenCore => CancellationToken.None;
        protected override Metadata ResponseTrailersCore { get; } = new();
        protected override Status StatusCore { get; set; }
        protected override WriteOptions? WriteOptionsCore { get; set; }
        protected override AuthContext AuthContextCore => throw new NotSupportedException();

        protected override ContextPropagationToken CreatePropagationTokenCore(ContextPropagationOptions? options)
            => throw new NotSupportedException();

        protected override Task WriteResponseHeadersAsyncCore(Metadata responseHeaders) => Task.CompletedTask;
    }

    [Fact]
    public async Task GrpcServer()
    {
        var listener = new ThrowingOnSuccessListener();
        var interceptor = new ConcurrencyLimitServerInterceptor(new FixedListenerLimiter<IGrpcServerRequestContext>(listener));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            interceptor.UnaryServerHandler<string, string>("req", new TestServerCallContext(), (_, _) => Task.FromResult("resp")));

        AssertReleasedOnce(listener);
    }

    private static readonly Marshaller<string> StringMarshaller = Marshallers.Create(
        s => System.Text.Encoding.UTF8.GetBytes(s),
        b => System.Text.Encoding.UTF8.GetString(b));

    private static readonly Method<string, string> TestMethod =
        new(MethodType.Unary, "test.Service", "Method", StringMarshaller, StringMarshaller);

    [Fact]
    public void GrpcClientBlocking()
    {
        var listener = new ThrowingOnSuccessListener();
        var interceptor = new ConcurrencyLimitClientInterceptor(new FixedListenerLimiter<IGrpcClientRequestContext>(listener));

        Assert.Throws<InvalidOperationException>(() =>
            interceptor.BlockingUnaryCall("req", new ClientInterceptorContext<string, string>(TestMethod, null, default), (_, _) => "resp"));

        AssertReleasedOnce(listener);
    }

    [Fact]
    public async Task GrpcClientAsync()
    {
        var listener = new ThrowingOnSuccessListener();
        var interceptor = new ConcurrencyLimitClientInterceptor(new FixedListenerLimiter<IGrpcClientRequestContext>(listener));

        AsyncUnaryCall<string> call = interceptor.AsyncUnaryCall("req",
            new ClientInterceptorContext<string, string>(TestMethod, null, default),
            (_, _) => new AsyncUnaryCall<string>(Task.FromResult("resp"), Task.FromResult(new Metadata()),
                () => Status.DefaultSuccess, () => new Metadata(), () => { }));

        await Assert.ThrowsAsync<InvalidOperationException>(() => call.ResponseAsync);

        AssertReleasedOnce(listener);
    }

    [Fact]
    public void ExecutorWithInlineScheduler()
    {
        var listener = new ThrowingOnSuccessListener();
        var executor = new BlockingAdaptiveExecutor(new FixedListenerLimiter<object?>(listener), command => command());

        Assert.Throws<InvalidOperationException>(() => executor.Execute(() => { }));

        AssertReleasedOnce(listener);
    }
}
