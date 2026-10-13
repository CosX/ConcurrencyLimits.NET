using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using ConcurrencyLimits.AspNetCore;
using ConcurrencyLimits.Polly;
using Microsoft.AspNetCore.Http;
using Polly;

namespace ConcurrencyLimits.Benchmarks;

/// <summary>
/// Overhead the transport integrations add to a request that does no work, using their default
/// (Vegas, unpartitioned) limiters. Each integration is compared with the same call made without it.
/// </summary>
[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class IntegrationBenchmarks
{
    private readonly DefaultHttpContext _httpContext = new();
    private readonly RequestDelegate _next = _ => Task.CompletedTask;
    private ConcurrencyLimitMiddleware _middleware = null!;
    private ResiliencePipeline _emptyPipeline = null!;
    private ResiliencePipeline _limitedPipeline = null!;

    [GlobalSetup]
    public void Setup()
    {
        _middleware = new ConcurrencyLimitMiddleware(_next, new HttpRequestLimiterBuilder().Build());
        _emptyPipeline = ResiliencePipeline.Empty;
        _limitedPipeline = new ResiliencePipelineBuilder()
            .AddConcurrencyLimit(new ResilienceContextLimiterBuilder().Build())
            .Build();
    }

    [Benchmark(Baseline = true), BenchmarkCategory("AspNetCore")]
    public Task NoMiddleware() => _next(_httpContext);

    [Benchmark, BenchmarkCategory("AspNetCore")]
    public Task WithMiddleware() => _middleware.InvokeAsync(_httpContext);

    [Benchmark(Baseline = true), BenchmarkCategory("Polly")]
    public int EmptyPipeline() => _emptyPipeline.Execute(static _ => 42);

    [Benchmark, BenchmarkCategory("Polly")]
    public int WithStrategy() => _limitedPipeline.Execute(static _ => 42);
}
