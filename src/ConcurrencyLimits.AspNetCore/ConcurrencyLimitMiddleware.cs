using Microsoft.AspNetCore.Http;

namespace ConcurrencyLimits.AspNetCore;

/// <summary>
/// Middleware that enforces a concurrency limit on incoming requests and returns 429 (Too Many Requests)
/// when the limit has been reached.
/// </summary>
public sealed class ConcurrencyLimitMiddleware
{
    private const int StatusTooManyRequests = StatusCodes.Status429TooManyRequests;

    private readonly RequestDelegate _next;
    private readonly ILimiter<HttpContext> _limiter;
    private readonly int _throttleStatus;

    public ConcurrencyLimitMiddleware(RequestDelegate next, ILimiter<HttpContext> limiter, int throttleStatus = StatusTooManyRequests)
    {
        _next = next;
        _limiter = limiter;
        _throttleStatus = throttleStatus;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        IListener? listener = _limiter.Acquire(context);
        if (listener != null)
        {
            try
            {
                await _next(context);
                listener.OnSuccess();
            }
            catch
            {
                listener.OnIgnore();
                throw;
            }
        }
        else
        {
            context.Response.StatusCode = _throttleStatus;
            await context.Response.WriteAsync("Concurrency limit exceeded");
        }
    }
}
