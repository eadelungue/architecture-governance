using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Polly.Timeout;

namespace CorePoints.Resilience.Middleware;

/// <summary>
/// Middleware that differentiates client disconnect from timeout cancellations.
/// Returns HTTP 499 for client disconnects and HTTP 504 for timeout policy triggers.
/// </summary>
public sealed class CancellationHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<CancellationHandlingMiddleware> _logger;

    public CancellationHandlingMiddleware(RequestDelegate next, ILogger<CancellationHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // Client disconnected - HTTP 499 (Client Closed Request)
            _logger.LogInformation(
                "Client disconnected during request processing. Path={Path}, Method={Method}",
                context.Request.Path, context.Request.Method);

            if (!context.Response.HasStarted)
            {
                context.Response.StatusCode = 499;
            }
        }
        catch (TimeoutRejectedException ex)
        {
            // Timeout policy triggered - HTTP 504 (Gateway Timeout)
            _logger.LogWarning(
                ex,
                "Request timed out. Path={Path}, Method={Method}",
                context.Request.Path, context.Request.Method);

            if (!context.Response.HasStarted)
            {
                context.Response.StatusCode = StatusCodes.Status504GatewayTimeout;
            }
        }
        catch (OperationCanceledException ex) when (!context.RequestAborted.IsCancellationRequested)
        {
            // Timeout from internal cancellation (not client disconnect) - HTTP 504
            _logger.LogWarning(
                ex,
                "Operation cancelled due to timeout. Path={Path}, Method={Method}",
                context.Request.Path, context.Request.Method);

            if (!context.Response.HasStarted)
            {
                context.Response.StatusCode = StatusCodes.Status504GatewayTimeout;
            }
        }
    }
}

/// <summary>
/// Extension method for registering the cancellation handling middleware.
/// </summary>
public static class CancellationHandlingMiddlewareExtensions
{
    public static IApplicationBuilder UseCancellationHandling(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<CancellationHandlingMiddleware>();
    }
}
