using System.Diagnostics;
using CorePoints.Caching.Abstractions;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.CircuitBreaker;
using StackExchange.Redis;

namespace CorePoints.Caching;

/// <summary>
/// Redis-backed implementation of ICacheService with resilience pipeline integration.
/// Implements Cache-Aside pattern with graceful degradation when Redis is unavailable.
/// </summary>
public sealed class RedisCacheService : ICacheService
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ICacheSerializer _serializer;
    private readonly ResiliencePipeline _resiliencePipeline;
    private readonly ILogger<RedisCacheService> _logger;

    public RedisCacheService(
        IConnectionMultiplexer redis,
        ICacheSerializer serializer,
        ResiliencePipeline resiliencePipeline,
        ILogger<RedisCacheService> logger)
    {
        _redis = redis;
        _serializer = serializer;
        _resiliencePipeline = resiliencePipeline;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<T?> GetAsync<T>(string key, CancellationToken ct = default)
    {
        try
        {
            var result = await _resiliencePipeline.ExecuteAsync(async token =>
            {
                var db = _redis.GetDatabase();
                return await db.StringGetAsync(key);
            }, ct);

            if (result.IsNullOrEmpty)
            {
                _logger.LogDebug("Cache MISS for key: {CacheKey}", key);
                return default;
            }

            _logger.LogDebug("Cache HIT for key: {CacheKey}", key);

            var deserialized = _serializer.Deserialize<T>((byte[])result!);
            if (deserialized is null)
            {
                _logger.LogWarning("Deserialization failed for key: {CacheKey}. Removing corrupted entry.", key);
                await TryDeleteKeyAsync(key, ct);
                return default;
            }

            return deserialized;
        }
        catch (BrokenCircuitException)
        {
            _logger.LogDebug("Circuit breaker is open. Bypassing cache for key: {CacheKey}", key);
            return default;
        }
        catch (Exception ex) when (ex is RedisException or TimeoutException or OperationCanceledException)
        {
            _logger.LogWarning(ex, "Redis GET failed for key: {CacheKey}. Treating as cache miss.", key);
            return default;
        }
    }

    /// <inheritdoc />
    public async Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken ct = default)
    {
        try
        {
            var serialized = _serializer.Serialize(value);

            await _resiliencePipeline.ExecuteAsync(async token =>
            {
                var db = _redis.GetDatabase();
                await db.StringSetAsync(key, serialized, ttl);
            }, ct);

            _logger.LogDebug("Cache SET for key: {CacheKey}, TTL: {TtlSeconds}s", key, ttl.TotalSeconds);
        }
        catch (BrokenCircuitException)
        {
            _logger.LogDebug("Circuit breaker is open. Skipping cache SET for key: {CacheKey}", key);
        }
        catch (Exception ex) when (ex is RedisException or TimeoutException or OperationCanceledException)
        {
            _logger.LogWarning(ex, "Redis SET failed for key: {CacheKey}. Value not cached.", key);
        }
    }

    /// <inheritdoc />
    public async Task<T> GetOrSetAsync<T>(string key, Func<CancellationToken, Task<T>> factory, TimeSpan ttl, CancellationToken ct = default)
    {
        // Try to get from cache
        var cached = await GetAsync<T>(key, ct);
        if (cached is not null)
        {
            return cached;
        }

        // Cache miss — call the factory (database)
        _logger.LogDebug("Cache miss for key: {CacheKey}. Calling factory.", key);
        var value = await factory(ct);

        // Store in cache (best effort)
        await SetAsync(key, value, ttl, ct);

        return value;
    }

    /// <inheritdoc />
    public async Task InvalidateAsync(string key, CancellationToken ct = default)
    {
        try
        {
            await _resiliencePipeline.ExecuteAsync(async token =>
            {
                var db = _redis.GetDatabase();
                await db.KeyDeleteAsync(key);
            }, ct);

            _logger.LogDebug("Cache INVALIDATED key: {CacheKey}", key);
        }
        catch (BrokenCircuitException)
        {
            _logger.LogWarning("Circuit breaker is open. Could not invalidate key: {CacheKey}. Relying on TTL safety net.", key);
        }
        catch (Exception ex) when (ex is RedisException or TimeoutException or OperationCanceledException)
        {
            _logger.LogWarning(ex, "Redis DEL failed for key: {CacheKey}. Relying on TTL safety net.", key);
        }
    }

    /// <inheritdoc />
    public async Task InvalidateAsync(IEnumerable<string> keys, CancellationToken ct = default)
    {
        foreach (var key in keys)
        {
            await InvalidateAsync(key, ct);
        }
    }

    private async Task TryDeleteKeyAsync(string key, CancellationToken ct)
    {
        try
        {
            var db = _redis.GetDatabase();
            await db.KeyDeleteAsync(key);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete corrupted cache key: {CacheKey}", key);
        }
    }
}
