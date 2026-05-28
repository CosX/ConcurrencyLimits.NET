namespace ConcurrencyLimits.Grpc.Client;

/// <summary>Context describing an outbound gRPC client call, used by partition and bypass resolvers.</summary>
public interface IGrpcClientRequestContext
{
    string Method { get; }
}

internal sealed class GrpcClientRequestContext(string method) : IGrpcClientRequestContext
{
    public string Method => method;
}
