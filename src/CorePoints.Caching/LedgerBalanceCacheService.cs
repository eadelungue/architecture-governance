using CorePoints.Caching.Abstractions;
using Microsoft.Extensions.Options;

namespace CorePoints.Caching;

/// <summary>
/// Wraps ICacheService with ledger-specific cache key building and TTL management.
/// Provides synchronous invalidation helpers for use after PostgreSQL commits.
/// </summary>
public sealed class LedgerBalanceCacheService
{
    private readonly ICacheService _cacheService;
    private readonly CacheOptions _options;

    public LedgerBalanceCacheService(ICacheService cacheService, IOptions<CacheOptions> options)
    {
        _cacheService = cacheService;
        _options = options.Value;
    }

    /// <summary>
    /// Gets a balance from cache, or populates it via the factory on miss.
    /// Uses the ledger balance TTL from configuration.
    /// </summary>
    /// <param name="accountId">The account identifier.</param>
    /// <param name="factory">Factory function to load the balance from the database.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The balance value.</returns>
    public async Task<T> GetBalanceAsync<T>(
        string accountId,
        Func<CancellationToken, Task<T>> factory,
        CancellationToken ct = default)
    {
        var key = CacheKeyBuilder.LedgerBalance(accountId);
        var ttl = TimeSpan.FromSeconds(_options.LedgerBalanceTtlSeconds);
        return await _cacheService.GetOrSetAsync(key, factory, ttl, ct);
    }

    /// <summary>
    /// Invalidates cached balances for the specified account IDs.
    /// Should be called synchronously after PostgreSQL ACID commit.
    /// </summary>
    /// <remarks>
    /// Usage pattern:
    /// <code>
    /// await _dbTransaction.CommitAsync();
    /// await _balanceCache.InvalidateBalanceAsync(debitAccountId, creditAccountId);
    /// </code>
    /// </remarks>
    public async Task InvalidateBalanceAsync(params string[] accountIds)
    {
        var keys = accountIds.Select(CacheKeyBuilder.LedgerBalance);
        await _cacheService.InvalidateAsync(keys);
    }
}
