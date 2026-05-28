using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace ConcurrencyLimits.AspNetCore;

public static class ApplicationBuilderExtensions
{
    /// <summary>
    /// Add the concurrency limit middleware to the request pipeline using the supplied limiter.
    /// </summary>
    public static IApplicationBuilder UseConcurrencyLimit(this IApplicationBuilder app, ILimiter<HttpContext> limiter,
        int throttleStatus = StatusCodes.Status429TooManyRequests)
        => app.UseMiddleware<ConcurrencyLimitMiddleware>(limiter, throttleStatus);
}
