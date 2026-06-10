# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

.NET port of Netflix/concurrency-limits (Java → C#). Adaptive concurrency limiting via TCP-congestion-control-style algorithms. Faithful 1:1 algorithm parity with the Java source; .NET-idiomatic surface.

Target framework `net10.0` (set in `Directory.Build.props`, `Nullable` + `ImplicitUsings` on). Only the net10 runtime is installed locally.

## Commands

```bash
dotnet build                              # build whole solution
dotnet test                               # run all tests (xUnit)
dotnet test --filter "FullyQualifiedName~LimitTests"        # one test class
dotnet test --filter "FullyQualifiedName~LimitTests.IncreaseLimit"  # one test
```

Single-project build: `dotnet build src/ConcurrencyLimits/ConcurrencyLimits.csproj` (msbuild takes ONE project arg — don't pass two paths).

xUnit1031 "blocking task" warnings come from faithfully-ported timing tests (Blocking/Lifo limiters). Harmless — do not "fix" by rewriting test semantics.

## Architecture

Two-layer split mirroring the Java module layout:

**`src/ConcurrencyLimits` (core)** — the algorithms, no transport deps (only `Microsoft.Extensions.Logging.Abstractions`).
- `ILimit` = the limit *algorithm* (computes a concurrency number from RTT samples). `ILimiter<TContext>` = the *gate* (hands out `IListener` tokens). These are distinct: a limiter owns a limit algorithm and feeds it samples on token release.
- Flow: `Acquire(context)` → `IListener?` (null = rejected). Caller MUST call exactly one of `OnSuccess`/`OnIgnore`/`OnDropped`. `OnSuccess`/`OnDropped` feed an RTT sample into the limit algorithm via `ILimit.OnSample`; `OnIgnore` releases without sampling.
- `AbstractLimit` serializes `OnSample` under a lock and fans limit changes to `NotifyOnChange` listeners; `SimpleLimiter` listens and resizes its `AdjustableSemaphore` (custom — .NET `SemaphoreSlim` can't reduce permits).
- `Limit/` algorithms (`Vegas`, `Gradient`, `Gradient2`, `AIMD`, `Fixed`, `Settable`, `WindowedLimit`); `Limit/Measurement`, `Limit/Window`, `Limit/Functions` are their helpers.
- Partitioning: `AbstractPartitionedLimiter<TContext>` — partition limits are *soft* (only enforced once the global limit is hit, allowing burst). `PartitionedLimiter.NewBuilder<T>()` is the concrete entry point; `.Build()` returns a `SimpleLimiter` when no partitions/resolvers are configured, otherwise a partitioned limiter.

**`src/ConcurrencyLimits.AspNetCore`** — servlet-filter analog: `ConcurrencyLimitMiddleware` (429 on reject) + `HttpRequestLimiterBuilder` + `UseConcurrencyLimit`.

**`src/ConcurrencyLimits.Grpc`** — `Interceptor`-based server/client limiting (unary only, matching Java) on `Grpc.Core.Api`.

### Builder pattern (important, non-obvious)

Limiter builders use CRTP and are deliberately **context-agnostic at the base**, mirroring Java's static nested `Builder`:
- `AbstractLimiterBuilder` (non-generic) holds shared state (limit, clock, registry, bypass).
- `AbstractLimiterBuilder<TBuilder>` adds the fluent `Self()`-returning methods.
- A single builder can build a limiter for *any* `TContext` (`SimpleLimiterBuilder.Build<TContext>()`), because the context type isn't fixed until build time.
- Partitioned builders ARE generic over `TContext` (`AbstractPartitionedLimiter<TContext>.Builder<TBuilder>`) since partition/bypass resolvers are typed. They expose state to the limiter ctor via `IPartitionedBuilderState<TContext>` (C# can't pass `Builder<?>` like Java).

When adding a transport integration, subclass `AbstractPartitionedLimiter<TContext>.Builder<TYourBuilder>`, implement `Self()`, and add typed `PartitionBy*` / `BypassLimitBy*` helpers that delegate to `PartitionResolver` / `BypassLimitResolver`.

## Java→C# mapping conventions

Follow these when porting more code or fixing parity bugs:
- `Optional<Listener>` → nullable `IListener?` (null = empty/rejected).
- `long` nanos clock (`System.nanoTime`) → `Internal.SystemNanoTime.Now()` (Stopwatch-based); builders take `TimeSpan`, convert to nanos internally.
- `TimeUnit` args → `TimeSpan`.
- `IntUnaryOperator`/`DoubleUnaryOperator` → `Func<int,int>`/`Func<double,double>`; `Consumer<Integer>` → `Action<int>`.
- `Number`-typed measurements collapsed to `double`.
- slf4j → `ILogger` defaulting to `NullLogger.Instance` (optional `LoggerFactory(...)` on builders).
- `volatile double` (illegal in C#) → plain field guarded by the `AbstractLimit` lock.

Reference Java source is NOT in this repo; clone `Netflix/concurrency-limits` when verifying parity.
