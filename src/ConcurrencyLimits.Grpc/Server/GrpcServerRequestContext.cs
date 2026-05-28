using Grpc.Core;

namespace ConcurrencyLimits.Grpc.Server;

/// <summary>Context describing an inbound gRPC server call, used by partition and bypass resolvers.</summary>
public interface IGrpcServerRequestContext
{
    string Method { get; }
    Metadata Headers { get; }
}

internal sealed class GrpcServerRequestContext(ServerCallContext context) : IGrpcServerRequestContext
{
    public string Method => context.Method;
    public Metadata Headers => context.RequestHeaders;
}
