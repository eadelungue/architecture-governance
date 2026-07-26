namespace CorePoints.Caching;

/// <summary>
/// Configuration options for the Redis caching layer.
/// </summary>
public sealed class CacheOptions
{
    /// <summary>
    /// Redis primary endpoint (read from SSM or configuration).
    /// </summary>
    public string RedisEndpoint { get; set; } = "";

    /// <summary>
    /// TTL in seconds for ledger balance cache entries. Default: 7 seconds.
    /// </summary>
    public int LedgerBalanceTtlSeconds { get; set; } = 7;

    /// <summary>
    /// TTL in seconds for product data cache entries. Default: 1800 seconds (30 minutes).
    /// </summary>
    public int ProductDataTtlSeconds { get; set; } = 1800;

    /// <summary>
    /// Number of consecutive failures before the circuit breaker opens. Default: 3.
    /// </summary>
    public int CircuitBreakerThreshold { get; set; } = 3;

    /// <summary>
    /// Duration in seconds the circuit breaker stays open before transitioning to half-open. Default: 15.
    /// </summary>
    public int CircuitBreakerBreakDurationSeconds { get; set; } = 15;

    /// <summary>
    /// Connection timeout in milliseconds for Redis. Default: 5000.
    /// </summary>
    public int ConnectTimeoutMs { get; set; } = 5000;

    /// <summary>
    /// Synchronous operation timeout in milliseconds for Redis. Default: 1000.
    /// </summary>
    public int SyncTimeoutMs { get; set; } = 1000;
}
