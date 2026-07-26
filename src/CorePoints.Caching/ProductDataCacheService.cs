using CorePoints.Caching.Abstractions;
using Microsoft.Extensions.Options;

namespace CorePoints.Caching;

/// <summary>
/// Wraps ICacheService with product-specific cache key building and TTL management.
/// Supports event-driven invalidation via SQS consumer integration.
/// </summary>
public sealed class ProductDataCacheService
{
    private readonly ICacheService _cacheService;
    private readonly CacheOptions _options;

    public ProductDataCacheService(ICacheService cacheService, IOptions<CacheOptions> options)
    {
        _cacheService = cacheService;
        _options = options.Value;
    }

    /// <summary>
    /// Gets product data from cache, or populates it via the factory on miss.
    /// Uses the product data TTL from configuration (default: 30 minutes).
    /// </summary>
    /// <param name="productId">The product identifier.</param>
    /// <param name="factory">Factory function to load the product from the database.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The product data.</returns>
    public async Task<T> GetProductAsync<T>(
        string productId,
        Func<CancellationToken, Task<T>> factory,
        CancellationToken ct = default)
    {
        var key = CacheKeyBuilder.ProductData(productId);
        var ttl = TimeSpan.FromSeconds(_options.ProductDataTtlSeconds);
        return await _cacheService.GetOrSetAsync(key, factory, ttl, ct);
    }

    /// <summary>
    /// Invalidates cached product data for the specified product ID.
    /// Intended for use by the SQS event consumer when ProductUpdated events are received.
    /// </summary>
    /// <remarks>
    /// Integration point for SQS consumer:
    /// <code>
    /// // In SQS consumer handler:
    /// var productId = eventPayload.ProductId;
    /// await _productCache.InvalidateProductAsync(productId);
    /// </code>
    /// </remarks>
    public async Task InvalidateProductAsync(string productId, CancellationToken ct = default)
    {
        var key = CacheKeyBuilder.ProductData(productId);
        await _cacheService.InvalidateAsync(key, ct);
    }
}
