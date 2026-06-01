# ConcurrencyLimits

A .NET port of [Netflix/concurrency-limits](https://github.com/Netflix/concurrency-limits). Adaptive concurrency limiting based on TCP congestion-control-style algorithms: instead of configuring a fixed limit, the limiter continuously measures round-trip latency and adjusts the allowed concurrency up or down to find the largest limit that keeps latency low.

The port is a faithful 1:1 of the Java core algorithms with a .NET-idiomatic surface (nullable returns instead of `Optional`, `TimeSpan` instead of `TimeUnit`, fluent builders).

Targets `net10.0`.

## Projects

| Project | Description |
| --- | --- |
| `ConcurrencyLimits` | Core algorithms and limiters. No transport dependencies. |
| `ConcurrencyLimits.AspNetCore` | Request-pipeline middleware that returns 429 when the limit is reached. |
| `ConcurrencyLimits.Grpc` | gRPC server and client interceptors (unary calls). |
| `ConcurrencyLimits.Polly` | Polly v8 resilience strategy that gates executions through a limiter. |

## Install

```bash
dotnet add package ConcurrencyLimits              # core algorithms and limiters
dotnet add package ConcurrencyLimits.AspNetCore   # ASP.NET Core middleware
dotnet add package ConcurrencyLimits.Grpc         # gRPC interceptors
dotnet add package ConcurrencyLimits.Polly        # Polly v8 resilience strategy
```

Or via `PackageReference`:

```xml
<PackageReference Include="ConcurrencyLimits" Version="1.0.0" />
```

The `AspNetCore` and `Grpc` packages depend on the core package, so installing either pulls it in transitively.

## Build and test

```bash
dotnet build
dotnet test
```

## Core concepts

- **`ILimit`** — the algorithm that computes a concurrency number from RTT samples (`Vegas`, `Gradient`, `Gradient2`, `AIMD`, `Fixed`, `Settable`, `WindowedLimit`).
- **`ILimiter<TContext>`** — the gate. `Acquire(context)` returns an `IListener?`; `null` means the limit was exceeded.
- **`IListener`** — returned on a successful acquire. Call exactly one of:
  - `OnSuccess()` — operation completed; its latency is fed back as an RTT sample.
  - `OnDropped()` — rejected/timed out; loss-based algorithms reduce the limit aggressively.
  - `OnIgnore()` — release the token without recording a sample.

### Basic usage

```csharp
using ConcurrencyLimits.Limit;
using ConcurrencyLimits.Limiter;

ILimiter<string> limiter = SimpleLimiter.NewBuilder()
    .WithLimit(VegasLimit.NewDefault())
    .Build<string>();

IListener? listener = limiter.Acquire("my-request");
if (listener is null)
{
    // limit exceeded — shed the request
    return;
}

try
{
    DoWork();
    listener.OnSuccess();
}
catch (TimeoutException)
{
    listener.OnDropped();
}
catch
{
    listener.OnIgnore();
    throw;
}
```

### Partitioned limits

Partitions reserve a percentage of the total limit per caller class. They are **soft**: a partition limit is only enforced once the global limit is reached, so spare global capacity allows bursting beyond a partition's share.

```csharp
ILimiter<string> limiter = PartitionedLimiter.NewBuilder<string>()
    .WithLimit(FixedLimit.Of(100))
    .PartitionResolver(ctx => ctx)          // map context -> partition name
    .AddPartition("live", 0.7)              // 70% reserved for "live"
    .AddPartition("batch", 0.3)             // 30% reserved for "batch"
    .Build();
```

### Blocking limiters

Wrap any limiter so callers wait for a permit instead of being rejected immediately:

```csharp
var blocking = BlockingLimiter<string>.Wrap(limiter, TimeSpan.FromSeconds(5));
var lifo = LifoBlockingLimiter<string>.NewBuilder(limiter)
    .BacklogSize(100)
    .BacklogTimeout(TimeSpan.FromSeconds(1))
    .Build();
```

## ASP.NET Core

```csharp
using ConcurrencyLimits.AspNetCore;
using ConcurrencyLimits.Limit;

ILimiter<HttpContext> limiter = new HttpRequestLimiterBuilder()
    .WithLimit(Gradient2Limit.NewDefault())
    .PartitionByHeader("x-tenant")
    .AddPartition("a", 0.5)
    .AddPartition("b", 0.5)
    .Build();

app.UseConcurrencyLimit(limiter);
```

Requests that exceed the limit get `429 Too Many Requests`. Builder helpers cover `PartitionBy{Header,Query,Claim,Path}` and `BypassLimitBy{Header,Path,Method}`.

## gRPC

```csharp
using ConcurrencyLimits.Grpc.Server;
using ConcurrencyLimits.Grpc.Client;

// Server
var serverLimiter = new GrpcServerLimiterBuilder()
    .WithLimit(VegasLimit.NewDefault())
    .PartitionByMethod()
    .AddPartition("/svc.Foo/Bar", 1.0)
    .Build();
var serverInterceptor = ConcurrencyLimitServerInterceptor.NewBuilder(serverLimiter).Build();

// Client
var clientLimiter = new GrpcClientLimiterBuilder()
    .PartitionByMethod()
    .BlockOnLimit(false)   // true => block instead of returning UNAVAILABLE
    .Build();
var clientInterceptor = new ConcurrencyLimitClientInterceptor(clientLimiter);
```

Only unary calls are limited (matching the Java implementation); streaming calls pass through. Rejected calls return `StatusCode.Unavailable`.

## Polly

Add an adaptive concurrency gate to any Polly v8 `ResiliencePipeline`. Acquire/release happens around the inner callback; successful runs feed RTT samples back into the limit algorithm.

```csharp
using ConcurrencyLimits.Limit;
using ConcurrencyLimits.Polly;
using Polly;

ILimiter<ResilienceContext> limiter = new ResilienceContextLimiterBuilder()
    .WithLimit(Gradient2Limit.NewDefault())
    .PartitionByOperationKey()
    .AddPartition("checkout", 0.7)
    .AddPartition("search", 0.3)
    .Build();

ResiliencePipeline pipeline = new ResiliencePipelineBuilder()
    .AddConcurrencyLimit(limiter)
    .AddRetry(new() { MaxRetryAttempts = 2 })
    .Build();

var ctx = ResilienceContextPool.Shared.Get("checkout");
try
{
    var result = await pipeline.ExecuteAsync(static async c => await CallDownstreamAsync(c.CancellationToken), ctx);
}
catch (ConcurrencyLimitRejectedException)
{
    // limiter shed the request — fall back / return 503 / etc.
}
finally
{
    ResilienceContextPool.Shared.Return(ctx);
}
```

Customize the rejection exception via `ConcurrencyLimitStrategyOptions.RejectionExceptionFactory` (e.g. throw `BrokenCircuitException` so a downstream `Retry` or `Fallback` strategy reacts to it).

## Metrics

Limiters report through the `IMetricRegistry` abstraction. The default is `EmptyMetricRegistry`; implement `IMetricRegistry` to bridge to your metrics system (e.g. `System.Diagnostics.Metrics`).

## License

Apache 2.0, matching the original Netflix project.
