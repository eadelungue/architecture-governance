using Amazon.SimpleSystemsManagement;
using CorePoints.Caching.Abstractions;
using CorePoints.Caching.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.CircuitBreaker;
using Polly.Registry;
using StackExchange.Redis;

namespace CorePoints.Caching.Extensions;

/// <summary>
/// Extension methods for registering CorePoints caching services in DI.
/// </summary>
public static class CachingServiceCollectionExtensions
{
    /// <summary>
    /// Adds CorePoints caching services to the service collection.
    /// Registers Redis connection, serializer, cache service, and resilience pipeline.
    /// </summary>
    public static IServiceCollection AddCorePointsCaching(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Bind configuration
        services.Configure<CacheOptions>(configuration.GetSection("Caching"));

        // Register RedisConnectionManager as singleton
        services.AddSingleton<RedisConnectionManager>();

        // Register IConnectionMultiplexer from the manager
        services.AddSingleton<IConnectionMultiplexer>(sp =>
        {
            var manager = sp.GetRequiredService<RedisConnectionManager>();
            return manager.GetConnectionAsync().GetAwaiter().GetResult();
        });

        // Register serializer
        services.AddSingleton<ICacheSerializer, JsonCacheSerializer>();

        // Register resilience pipeline
        services.AddResiliencePipeline("redis-cache", (builder, context) =>
        {
            var options = context.ServiceProvider.GetRequiredService<IOptions<CacheOptions>>().Value;
            var logger = context.ServiceProvider.GetRequiredService<ILogger<RedisCacheService>>();

            builder.AddCircuitBreaker(new CircuitBreakerStrategyOptions
            {
                FailureRatio = 1.0,
                SamplingDuration = TimeSpan.FromSeconds(30),
                MinimumThroughput = options.CircuitBreakerThreshold,
                BreakDuration = TimeSpan.FromSeconds(options.CircuitBreakerBreakDurationSeconds),
                ShouldHandle = new PredicateBuilder()
                    .Handle<RedisException>()
                    .Handle<TimeoutException>(),
                OnOpened = args =>
                {
                    logger.LogInformation("Circuit breaker OPENED. Redis operations will be bypassed for {BreakDuration}s.",
                        args.BreakDuration.TotalSeconds);
                    return ValueTask.CompletedTask;
                },
                OnClosed = args =>
                {
                    logger.LogInformation("Circuit breaker CLOSED. Resuming normal Redis operations.");
                    return ValueTask.CompletedTask;
                },
                OnHalfOpened = args =>
                {
                    logger.LogInformation("Circuit breaker HALF-OPEN. Sending probe request to Redis.");
                    return ValueTask.CompletedTask;
                }
            });

            builder.AddTimeout(TimeSpan.FromMilliseconds(options.SyncTimeoutMs));
        });

        // Register ICacheService
        services.AddSingleton<ICacheService>(sp =>
        {
            var redis = sp.GetRequiredService<IConnectionMultiplexer>();
            var serializer = sp.GetRequiredService<ICacheSerializer>();
            var pipelineProvider = sp.GetRequiredService<ResiliencePipelineProvider<string>>();
            var pipeline = pipelineProvider.GetPipeline("redis-cache");
            var cacheLogger = sp.GetRequiredService<ILogger<RedisCacheService>>();

            return new RedisCacheService(redis, serializer, pipeline, cacheLogger);
        });

        // Register domain-specific cache services
        services.AddSingleton<LedgerBalanceCacheService>();
        services.AddSingleton<ProductDataCacheService>();

        return services;
    }
}
