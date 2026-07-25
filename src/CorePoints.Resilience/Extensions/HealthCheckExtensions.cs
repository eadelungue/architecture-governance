using System.Text.Json;
using CorePoints.Resilience.HealthChecks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace CorePoints.Resilience.Extensions;

/// <summary>
/// Extension methods for registering health check services and endpoints.
/// </summary>
public static class HealthCheckExtensions
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    /// <summary>
    /// Registers health checks for PostgreSQL, Redis, and Ledger Core.
    /// </summary>
    public static IServiceCollection AddCorePointsHealthChecks(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var healthChecksBuilder = services.AddHealthChecks();

        // PostgreSQL health check
        var pgConnectionString = configuration.GetConnectionString("PostgreSQL");
        if (!string.IsNullOrEmpty(pgConnectionString))
        {
            healthChecksBuilder.AddNpgSql(
                pgConnectionString,
                name: "postgresql",
                tags: new[] { "ready" },
                timeout: TimeSpan.FromSeconds(5));
        }

        // Redis health check
        var redisConnection = configuration.GetConnectionString("Redis");
        if (!string.IsNullOrEmpty(redisConnection))
        {
            healthChecksBuilder.AddRedis(
                redisConnection,
                name: "redis",
                tags: new[] { "ready" },
                timeout: TimeSpan.FromSeconds(5));
        }

        // Ledger Core health check
        healthChecksBuilder.AddCheck<LedgerHealthCheck>(
            "ledger",
            failureStatus: HealthStatus.Unhealthy,
            tags: new[] { "ready" },
            timeout: TimeSpan.FromSeconds(5));

        return services;
    }

    /// <summary>
    /// Maps health check endpoints for liveness and readiness probes.
    /// </summary>
    public static IEndpointRouteBuilder MapCorePointsHealthChecks(this IEndpointRouteBuilder endpoints)
    {
        // Liveness probe: always returns 200 if process is running
        endpoints.MapHealthChecks("/health/live", new HealthCheckOptions
        {
            Predicate = _ => false // No dependency checks, always healthy
        });

        // Readiness probe: checks all dependencies tagged "ready"
        endpoints.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains("ready"),
            ResponseWriter = WriteHealthCheckResponse
        });

        return endpoints;
    }

    private static async Task WriteHealthCheckResponse(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json";

        var response = new
        {
            status = report.Status.ToString(),
            totalDuration = report.TotalDuration.TotalMilliseconds,
            entries = report.Entries.Select(e => new
            {
                name = e.Key,
                status = e.Value.Status.ToString(),
                duration = e.Value.Duration.TotalMilliseconds,
                description = e.Value.Description,
                exception = e.Value.Exception?.Message
            })
        };

        await context.Response.WriteAsJsonAsync(response, JsonOptions);
    }
}
