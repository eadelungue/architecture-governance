namespace CorePoints.Caching.Abstractions;

/// <summary>
/// Abstraction for cache operations using the Cache-Aside pattern.
/// </summary>
public interface ICacheService
{
    /// <summary>
    /// Gets a value from the cache by key.
    /// Returns null if not found or on error.
    /// </summary>
    Task<T?> GetAsync<T>(string key, CancellationToken ct = default);

    /// <summary>
    /// Sets a value in the cache with the specified TTL.
    /// </summary>
    Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken ct = default);

    /// <summary>
    /// Gets a value from cache, or populates it using the factory on miss.
    /// Implements the full Cache-Aside pattern.
    /// </summary>
    Task<T> GetOrSetAsync<T>(string key, Func<CancellationToken, Task<T>> factory, TimeSpan ttl, CancellationToken ct = default);

    /// <summary>
    /// Invalidates (removes) a single cache entry.
    /// </summary>
    Task InvalidateAsync(string key, CancellationToken ct = default);

    /// <summary>
    /// Invalidates (removes) multiple cache entries.
    /// </summary>
    Task InvalidateAsync(IEnumerable<string> keys, CancellationToken ct = default);
}
