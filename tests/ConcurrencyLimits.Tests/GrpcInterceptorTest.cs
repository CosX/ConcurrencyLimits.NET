using ConcurrencyLimits.Grpc.Client;
using ConcurrencyLimits.Grpc.Server;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Xunit;

namespace ConcurrencyLimits.Tests;

public class GrpcInterceptorTest
{
    private sealed class RecordingListener : IListener
    {
        public int SuccessCount;
        public int IgnoreCount;
        public int DroppedCount;

        public void OnSuccess() => SuccessCount++;
        public void OnIgnore() => IgnoreCount++;
        public void OnDropped() => DroppedCount++;
    }

    private sealed class FakeLimiter<TContext>(IListener? listener) : ILimiter<TContext>
    {
        public TContext? LastContext;

        public IListener? Acquire(TContext context)
        {
            LastContext = context;
            return listener;
        }
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

    private static readonly Marshaller<string> StringMarshaller = Marshallers.Create(
        s => System.Text.Encoding.UTF8.GetBytes(s),
        b => System.Text.Encoding.UTF8.GetString(b));

    private static readonly Method<string, string> TestMethod =
        new(MethodType.Unary, "test.Service", "Method", StringMarshaller, StringMarshaller);

    private static ClientInterceptorContext<string, string> ClientContext()
        => new(TestMethod, null, default);

    private static AsyncUnaryCall<string> Call(Task<string> response)
        => new(response, Task.FromResult(new Metadata()), () => Status.DefaultSuccess, () => new Metadata(), () => { });

    // ---------- Server interceptor ----------

    [Fact]
    public async Task ServerRejectsWithUnavailableWhenLimitExceeded()
    {
        var interceptor = new ConcurrencyLimitServerInterceptor(new FakeLimiter<IGrpcServerRequestContext>(null));
        bool continuationCalled = false;

        RpcException ex = await Assert.ThrowsAsync<RpcException>(() =>
            interceptor.UnaryServerHandler<string, string>("req", new TestServerCallContext(), (_, _) =>
            {
                continuationCalled = true;
                return Task.FromResult("resp");
            }));

        Assert.Equal(StatusCode.Unavailable, ex.StatusCode);
        Assert.False(continuationCalled);
    }

    [Fact]
    public async Task ServerUsesCustomStatusSupplierOnReject()
    {
        ConcurrencyLimitServerInterceptor interceptor = ConcurrencyLimitServerInterceptor
            .NewBuilder(new FakeLimiter<IGrpcServerRequestContext>(null))
            .StatusSupplier(() => new Status(StatusCode.ResourceExhausted, "too busy"))
            .Build();

        RpcException ex = await Assert.ThrowsAsync<RpcException>(() =>
            interceptor.UnaryServerHandler<string, string>("req", new TestServerCallContext(),
                (_, _) => Task.FromResult("resp")));

        Assert.Equal(StatusCode.ResourceExhausted, ex.StatusCode);
    }

    [Fact]
    public async Task ServerReportsSuccess()
    {
        var listener = new RecordingListener();
        var limiter = new FakeLimiter<IGrpcServerRequestContext>(listener);
        var interceptor = new ConcurrencyLimitServerInterceptor(limiter);

        string response = await interceptor.UnaryServerHandler<string, string>("req", new TestServerCallContext(),
            (_, _) => Task.FromResult("resp"));

        Assert.Equal("resp", response);
        Assert.Equal(1, listener.SuccessCount);
        Assert.Equal(0, listener.IgnoreCount);
        Assert.Equal(0, listener.DroppedCount);
        Assert.Equal("/test.Service/Method", limiter.LastContext!.Method);
    }

    [Theory]
    [InlineData(StatusCode.DeadlineExceeded)]
    [InlineData(StatusCode.Cancelled)]
    public async Task ServerReportsDropOnDeadlineOrCancel(StatusCode statusCode)
    {
        var listener = new RecordingListener();
        var interceptor = new ConcurrencyLimitServerInterceptor(new FakeLimiter<IGrpcServerRequestContext>(listener));

        await Assert.ThrowsAsync<RpcException>(() =>
            interceptor.UnaryServerHandler<string, string>("req", new TestServerCallContext(),
                (_, _) => Task.FromException<string>(new RpcException(new Status(statusCode, "boom")))));

        Assert.Equal(1, listener.DroppedCount);
        Assert.Equal(0, listener.SuccessCount);
        Assert.Equal(0, listener.IgnoreCount);
    }

    [Fact]
    public async Task ServerReportsIgnoreOnUnknownFailure()
    {
        var listener = new RecordingListener();
        var interceptor = new ConcurrencyLimitServerInterceptor(new FakeLimiter<IGrpcServerRequestContext>(listener));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            interceptor.UnaryServerHandler<string, string>("req", new TestServerCallContext(),
                (_, _) => Task.FromException<string>(new InvalidOperationException("boom"))));

        Assert.Equal(1, listener.IgnoreCount);
        Assert.Equal(0, listener.SuccessCount);
        Assert.Equal(0, listener.DroppedCount);
    }

    // ---------- Client interceptor: blocking ----------

    [Fact]
    public void ClientBlockingRejectsWithUnavailableWhenLimitExceeded()
    {
        var interceptor = new ConcurrencyLimitClientInterceptor(new FakeLimiter<IGrpcClientRequestContext>(null));

        RpcException ex = Assert.Throws<RpcException>(() =>
            interceptor.BlockingUnaryCall("req", ClientContext(), (_, _) => "resp"));

        Assert.Equal(StatusCode.Unavailable, ex.StatusCode);
    }

    [Fact]
    public void ClientBlockingReportsSuccess()
    {
        var listener = new RecordingListener();
        var limiter = new FakeLimiter<IGrpcClientRequestContext>(listener);
        var interceptor = new ConcurrencyLimitClientInterceptor(limiter);

        string response = interceptor.BlockingUnaryCall("req", ClientContext(), (_, _) => "resp");

        Assert.Equal("resp", response);
        Assert.Equal(1, listener.SuccessCount);
        Assert.Equal("/test.Service/Method", limiter.LastContext!.Method);
    }

    [Theory]
    [InlineData(StatusCode.Unavailable)]
    [InlineData(StatusCode.DeadlineExceeded)]
    [InlineData(StatusCode.Cancelled)]
    public void ClientBlockingReportsDropOnThrottlingStatus(StatusCode statusCode)
    {
        var listener = new RecordingListener();
        var interceptor = new ConcurrencyLimitClientInterceptor(new FakeLimiter<IGrpcClientRequestContext>(listener));

        Assert.Throws<RpcException>(() => interceptor.BlockingUnaryCall<string, string>("req", ClientContext(),
            (_, _) => throw new RpcException(new Status(statusCode, "boom"))));

        Assert.Equal(1, listener.DroppedCount);
        Assert.Equal(0, listener.SuccessCount);
        Assert.Equal(0, listener.IgnoreCount);
    }

    [Fact]
    public void ClientBlockingReportsIgnoreOnUnknownFailure()
    {
        var listener = new RecordingListener();
        var interceptor = new ConcurrencyLimitClientInterceptor(new FakeLimiter<IGrpcClientRequestContext>(listener));

        Assert.Throws<InvalidOperationException>(() => interceptor.BlockingUnaryCall<string, string>(
            "req", ClientContext(), (_, _) => throw new InvalidOperationException("boom")));

        Assert.Equal(1, listener.IgnoreCount);
        Assert.Equal(0, listener.DroppedCount);
    }

    // ---------- Client interceptor: async ----------

    [Fact]
    public void ClientAsyncRejectsWithUnavailableWhenLimitExceeded()
    {
        var interceptor = new ConcurrencyLimitClientInterceptor(new FakeLimiter<IGrpcClientRequestContext>(null));

        RpcException ex = Assert.Throws<RpcException>(() =>
            interceptor.AsyncUnaryCall("req", ClientContext(), (_, _) => Call(Task.FromResult("resp"))));

        Assert.Equal(StatusCode.Unavailable, ex.StatusCode);
    }

    [Fact]
    public async Task ClientAsyncReportsSuccessAfterResponse()
    {
        var listener = new RecordingListener();
        var interceptor = new ConcurrencyLimitClientInterceptor(new FakeLimiter<IGrpcClientRequestContext>(listener));

        AsyncUnaryCall<string> call = interceptor.AsyncUnaryCall("req", ClientContext(),
            (_, _) => Call(Task.FromResult("resp")));

        Assert.Equal("resp", await call.ResponseAsync);
        Assert.Equal(1, listener.SuccessCount);
    }

    [Theory]
    [InlineData(StatusCode.Unavailable)]
    [InlineData(StatusCode.DeadlineExceeded)]
    [InlineData(StatusCode.Cancelled)]
    public async Task ClientAsyncReportsDropOnThrottlingStatus(StatusCode statusCode)
    {
        var listener = new RecordingListener();
        var interceptor = new ConcurrencyLimitClientInterceptor(new FakeLimiter<IGrpcClientRequestContext>(listener));

        AsyncUnaryCall<string> call = interceptor.AsyncUnaryCall("req", ClientContext(),
            (_, _) => Call(Task.FromException<string>(new RpcException(new Status(statusCode, "boom")))));

        await Assert.ThrowsAsync<RpcException>(() => call.ResponseAsync);
        Assert.Equal(1, listener.DroppedCount);
        Assert.Equal(0, listener.SuccessCount);
    }

    [Fact]
    public async Task ClientAsyncReportsIgnoreOnUnknownFailure()
    {
        var listener = new RecordingListener();
        var interceptor = new ConcurrencyLimitClientInterceptor(new FakeLimiter<IGrpcClientRequestContext>(listener));

        AsyncUnaryCall<string> call = interceptor.AsyncUnaryCall("req", ClientContext(),
            (_, _) => Call(Task.FromException<string>(new InvalidOperationException("boom"))));

        await Assert.ThrowsAsync<InvalidOperationException>(() => call.ResponseAsync);
        Assert.Equal(1, listener.IgnoreCount);
    }

    [Fact]
    public void ClientAsyncReleasesTokenWhenContinuationThrows()
    {
        var listener = new RecordingListener();
        var interceptor = new ConcurrencyLimitClientInterceptor(new FakeLimiter<IGrpcClientRequestContext>(listener));

        Assert.Throws<InvalidOperationException>(() => interceptor.AsyncUnaryCall<string, string>(
            "req", ClientContext(), (_, _) => throw new InvalidOperationException("boom")));

        Assert.Equal(1, listener.IgnoreCount);
        Assert.Equal(0, listener.SuccessCount);
        Assert.Equal(0, listener.DroppedCount);
    }
}
