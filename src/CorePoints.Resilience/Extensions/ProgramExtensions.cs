using CorePoints.Resilience.Clients;
using CorePoints.Resilience.Middleware;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CorePoints.Resilience.Extensions;

/// <summary>
/// Integration helper showing the correct middleware and service registration order.
/// Use these extension methods in Program.cs to wire all resilience components.
/// </summary>
public static class ProgramExtensions
{
    /// <summary>
    /// Registers all CorePoints resilience services including HTTP clients, health checks, and rate limiting.
    /// Call this in the service registration phase of Program.cs.
    /// </summary>
    public static IServiceCollection AddCorePointsServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // 1. Register resilience pipelines and named HTTP clients
        services.AddCorePointsResilience(configuration);

        // 2. Register rate limiting
        services.AddCorePointsRateLimiting(configuration);

        // 3. Register health checks
        services.AddCorePointsHealthChecks(configuration);

        // 4. Register typed clients
        services.AddScoped<ILedgerClient, LedgerHttpClient>();

        return services;
    }

    /// <summary>
    /// Configures the middleware pipeline in the correct order.
    /// Call this after building the app but before app.Run().
    /// 
    /// Correct middleware order:
    ///   UseRateLimiter → UseCancellationHandling → UseRouting → UseEndpoints (with health checks)
    /// </summary>
    public static WebApplication UseCorePointsPipeline(this WebApplication app)
    {
        // 1. Rate limiter first — reject abusive traffic before consuming resources
        app.UseRateLimiter();

        // 2. Cancellation handling — differentiate client disconnect vs timeout
        app.UseCancellationHandling();

        // 3. Routing
        app.UseRouting();

        // 4. Map health check endpoints
        app.MapCorePointsHealthChecks();

        return app;
    }
}
