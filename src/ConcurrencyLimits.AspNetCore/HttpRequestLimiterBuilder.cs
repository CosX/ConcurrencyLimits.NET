using ConcurrencyLimits.Limiter;
using Microsoft.AspNetCore.Http;

namespace ConcurrencyLimits.AspNetCore;

/// <summary>
/// Builder to simplify creating an <see cref="ILimiter{TContext}"/> for ASP.NET Core requests. By default the
/// same concurrency limit is shared by all requests. The limiter can be partitioned based on request attributes.
/// </summary>
public sealed class HttpRequestLimiterBuilder
    : AbstractPartitionedLimiter<HttpContext>.Builder<HttpRequestLimiterBuilder>
{
    protected override HttpRequestLimiterBuilder Self() => this;

    /// <summary>Partition the limit by a request header.</summary>
    public HttpRequestLimiterBuilder PartitionByHeader(string name)
        => PartitionResolver(ctx => ctx.Request.Headers.TryGetValue(name, out var v) ? v.ToString() : null);

    /// <summary>Partition the limit by a query string parameter.</summary>
    public HttpRequestLimiterBuilder PartitionByQuery(string name)
        => PartitionResolver(ctx => ctx.Request.Query.TryGetValue(name, out var v) ? v.ToString() : null);

    /// <summary>Partition the limit by a claim of the authenticated user, mapped to a named group.</summary>
    public HttpRequestLimiterBuilder PartitionByClaim(string claimType, Func<string, string?> claimToGroup)
        => PartitionResolver(ctx =>
        {
            string? value = ctx.User?.FindFirst(claimType)?.Value;
            return value == null ? null : claimToGroup(value);
        });

    /// <summary>Partition the limit by the request path, mapped to a named group.</summary>
    public HttpRequestLimiterBuilder PartitionByPath(Func<string, string?> pathToGroup)
        => PartitionResolver(ctx => ctx.Request.Path.HasValue ? pathToGroup(ctx.Request.Path.Value!) : null);

    /// <summary>Bypass the limit when the predicate evaluates to true for the request.</summary>
    public HttpRequestLimiterBuilder BypassLimitResolver(Func<HttpContext, bool> shouldBypass)
        => BypassLimitResolver(ctx => ctx is HttpContext http && shouldBypass(http));

    /// <summary>Bypass the limit if the named header matches the given value.</summary>
    public HttpRequestLimiterBuilder BypassLimitByHeader(string name, string value)
        => BypassLimitResolver(ctx => ctx.Request.Headers.TryGetValue(name, out var v) && v.ToString() == value);

    /// <summary>Bypass the limit if the request path matches the given value.</summary>
    public HttpRequestLimiterBuilder BypassLimitByPath(string path)
        => BypassLimitResolver(ctx => ctx.Request.Path == path);

    /// <summary>Bypass the limit if the request method matches the given HTTP method.</summary>
    public HttpRequestLimiterBuilder BypassLimitByMethod(string method)
        => BypassLimitResolver(ctx => string.Equals(ctx.Request.Method, method, StringComparison.OrdinalIgnoreCase));
}
