using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CorePoints.Resilience.Extensions;

/// <summary>
/// Extension methods for configuring inbound rate limiting.
/// </summary>
public static class RateLimitingExtensions
{
    /// <summary>
    /// Registers per-client fixed-window and global concurrency rate limiters.
    /// Configuration is read from "RateLimiting" section.
    /// </summary>
    public static IServiceCollection AddCorePointsRateLimiting(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            // Per-client fixed-window limiter partitioned by client IP
            options.AddPolicy("PerClient", context =>
            {
                var clientIp = GetClientIp(context);

                var permitLimit = configuration.GetValue("RateLimiting:PerClient:PermitLimit", 100);
                var windowSeconds = configuration.GetValue("RateLimiting:PerClient:WindowSeconds", 60);
                var queueLimit = configuration.GetValue("RateLimiting:PerClient:QueueLimit", 0);

                return RateLimitPartition.GetFixedWindowLimiter(clientIp, _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = permitLimit,
                    Window = TimeSpan.FromSeconds(windowSeconds),
                    QueueLimit = queueLimit,
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst
                });
            });

            // Global concurrency limiter
            options.AddPolicy("Global", context =>
            {
                var permitLimit = configuration.GetValue("RateLimiting:Global:PermitLimit", 500);
                var queueLimit = configuration.GetValue("RateLimiting:Global:QueueLimit", 50);

                return RateLimitPartition.GetConcurrencyLimiter("global", _ => new ConcurrencyLimiterOptions
                {
                    PermitLimit = permitLimit,
                    QueueLimit = queueLimit,
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst
                });
            });

            // OnRejected callback: writes Retry-After header
            options.OnRejected = async (context, cancellationToken) =>
            {
                var retryAfter = "60"; // Default fallback
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfterValue))
                {
                    retryAfter = ((int)retryAfterValue.TotalSeconds).ToString();
                }

                context.HttpContext.Response.Headers.RetryAfter = retryAfter;

                var logger = context.HttpContext.RequestServices
                    .GetService<ILoggerFactory>()?
                    .CreateLogger("CorePoints.Resilience.RateLimiting");

                logger?.LogWarning(
                    "Rate limit exceeded for {ClientIp}. Path={Path}, RetryAfter={RetryAfter}s",
                    GetClientIp(context.HttpContext),
                    context.HttpContext.Request.Path,
                    retryAfter);

                context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                await context.HttpContext.Response.WriteAsJsonAsync(new
                {
                    error = "RATE_LIMIT_EXCEEDED",
                    message = "Too many requests. Please retry after the specified duration.",
                    retryAfterSeconds = int.Parse(retryAfter)
                }, cancellationToken);
            };
        });

        return services;
    }

    private static string GetClientIp(HttpContext context)
    {
        // Check X-Forwarded-For header first (behind load balancer)
        var forwardedFor = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrEmpty(forwardedFor))
        {
            // Take the first IP (original client)
            return forwardedFor.Split(',', StringSplitOptions.TrimEntries)[0];
        }

        return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }
}
