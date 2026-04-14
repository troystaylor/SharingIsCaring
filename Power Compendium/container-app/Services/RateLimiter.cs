using System.Collections.Concurrent;
using Microsoft.AspNetCore.Http;

namespace LLMbook.Api.Services;

/// <summary>
/// In-memory per-user rate limiter for LLM-bound operations.
/// Limits are per Function App instance. For multi-instance deployments,
/// use Azure API Management or Redis-backed rate limiting.
/// </summary>
public class RateLimiter
{
    private readonly ConcurrentDictionary<string, SlidingWindow> _windows = new();

    public int MaxRequestsPerWindow { get; init; } = 30;
    public TimeSpan WindowDuration { get; init; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Check if a request is allowed for the given user.
    /// Returns true if allowed, false if rate limited.
    /// </summary>
    public bool TryAcquire(string key, out int remaining, out TimeSpan retryAfter)
    {
        var window = _windows.GetOrAdd(key, _ => new SlidingWindow());
        var now = DateTimeOffset.UtcNow;

        lock (window)
        {
            window.Timestamps.RemoveAll(t => now - t > WindowDuration);

            remaining = MaxRequestsPerWindow - window.Timestamps.Count;

            if (window.Timestamps.Count >= MaxRequestsPerWindow)
            {
                retryAfter = WindowDuration - (now - window.Timestamps[0]);
                if (retryAfter < TimeSpan.Zero) retryAfter = TimeSpan.FromSeconds(1);
                remaining = 0;
                return false;
            }

            window.Timestamps.Add(now);
            remaining = MaxRequestsPerWindow - window.Timestamps.Count;
            retryAfter = TimeSpan.Zero;
            return true;
        }
    }

    private class SlidingWindow
    {
        public List<DateTimeOffset> Timestamps { get; } = [];
    }
}

/// <summary>
/// ASP.NET Core middleware for per-user rate limiting on LLM-bound endpoints.
/// </summary>
public class RateLimitingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly RateLimiter _rateLimiter;

    private static readonly HashSet<string> RateLimitedPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        "/api/book/ingest",
        "/api/book/query",
        "/api/book/lint",
        "/api/mcp"
    };

    public RateLimitingMiddleware(RequestDelegate next, RateLimiter rateLimiter)
    {
        _next = next;
        _rateLimiter = rateLimiter;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? "";
        if (!RateLimitedPaths.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
        {
            await _next(context);
            return;
        }

        var userId = context.Request.Headers["X-MS-CLIENT-PRINCIPAL-ID"].FirstOrDefault();
        var ip = context.Request.Headers["X-Forwarded-For"].FirstOrDefault()
            ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var key = !string.IsNullOrEmpty(userId) ? $"user:{userId}" : $"ip:{ip}";

        if (!_rateLimiter.TryAcquire(key, out var remaining, out var retryAfter))
        {
            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            context.Response.Headers["Retry-After"] = ((int)retryAfter.TotalSeconds).ToString();
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(
                $"{{\"error\":\"Rate limit exceeded. Try again in {(int)retryAfter.TotalSeconds} seconds.\"}}");
            return;
        }

        context.Response.Headers["X-RateLimit-Remaining"] = remaining.ToString();
        await _next(context);
    }
}
