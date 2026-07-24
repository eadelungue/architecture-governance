using CorePoints.FeatureToggles.Interfaces;
using CorePoints.FeatureToggles.Models;
using CorePoints.FeatureToggles.Repositories;
using CorePoints.FeatureToggles.Services;
using Dapper;
using Microsoft.Extensions.DependencyInjection;

namespace CorePoints.FeatureToggles.Extensions;

/// <summary>
/// Extension methods for registering Feature Toggle services in the DI container.
/// </summary>
public static class FeatureToggleServiceCollectionExtensions
{
    /// <summary>
    /// Registers all Feature Toggle services: repository, service, cache, options, and type handlers.
    /// </summary>
    /// <param name="services">The service collection to add to.</param>
    /// <param name="configure">Action to configure FeatureToggleOptions (ConnectionString, CacheTtl).</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddFeatureToggles(
        this IServiceCollection services,
        Action<FeatureToggleOptions> configure)
    {
        // Register options
        var options = new FeatureToggleOptions();
        configure(options);
        services.Configure(configure);

        // Register IMemoryCache
        services.AddMemoryCache();

        // Register repository (scoped)
        services.AddScoped<IFeatureFlagRepository, FeatureFlagRepository>();

        // Register service (scoped)
        services.AddScoped<IFeatureToggleService, FeatureToggleService>();

        // Register Dapper JSONB type handler for List<string>
        SqlMapper.AddTypeHandler(new JsonbTypeHandler());

        return services;
    }
}
