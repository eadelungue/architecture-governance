# Implementation Tasks

## Task 1: Project Setup and Configuration Model

- [ ] 1.1 Create `src/CorePoints.Caching/CorePoints.Caching.csproj` with dependencies: StackExchange.Redis 2.7.*, Microsoft.Extensions.Resilience 8.0.*, AWSSDK.SimpleSystemsManagement 3.*, System.Text.Json 8.0.*
- [ ] 1.2 Create `src/CorePoints.Caching/CacheOptions.cs` with properties: RedisEndpoint, LedgerBalanceTtlSeconds (default 7), ProductDataTtlSeconds (default 1800), CircuitBreakerThreshold (default 3), CircuitBreakerBreakDurationSeconds (default 15), ConnectTimeoutMs (default 5000), SyncTimeoutMs (default 1000)
- [ ] 1.3 Create `src/CorePoints.Caching.Tests/CorePoints.Caching.Tests.csproj` with references to xUnit, Moq, FluentAssertions, and the main project

## Task 2: Cache Key Builder

- [ ] 2.1 Create `src/CorePoints.Caching/CacheKeyBuilder.cs` as a static class with `Build(string service, string entity, string id)` method that validates inputs (non-empty, no whitespace/colons, lowercase) and returns `{service}:{entity}:{id}` format. Throw ArgumentException on invalid input.
- [ ] 2.2 Add convenience methods: `LedgerBalance(string accountId)` → `"ledger:balance:{accountId}"` and `ProductData(string productId)` → `"product:data:{productId}"`
- [ ] 2.3 Create `src/CorePoints.Caching.Tests/CacheKeyBuilderTests.cs` with tests: valid key format, throws on empty/null components, throws on whitespace, throws on colon in component, convenience methods produce correct format

## Task 3: Serialization Layer

- [ ] 3.1 Create `src/CorePoints.Caching/Abstractions/ICacheSerializer.cs` with methods `byte[] Serialize<T>(T value)` and `T? Deserialize<T>(byte[] data)`
- [ ] 3.2 Create `src/CorePoints.Caching/Infrastructure/JsonCacheSerializer.cs` implementing ICacheSerializer using System.Text.Json with JsonSerializerOptions (camelCase, no indentation). Return null from Deserialize on failure rather than throwing.
- [ ] 3.3 Create `src/CorePoints.Caching.Tests/JsonCacheSerializerTests.cs` with tests: round-trip serialization of POCOs, camelCase output verification, deserialization of corrupted data returns null

## Task 4: Redis Connection Manager

- [ ] 4.1 Create `src/CorePoints.Caching/Infrastructure/RedisConnectionManager.cs` as a singleton that reads the Redis endpoint from SSM parameter `/{environment}/redis/primary_endpoint` on initialization. Use AWSSDK.SimpleSystemsManagement to fetch the parameter.
- [ ] 4.2 Configure ConnectionMultiplexer with ConfigurationOptions: abortConnect=false, connectTimeout=5000, syncTimeout=1000, endpoint from SSM. Expose the multiplexer via `IConnectionMultiplexer` property.
- [ ] 4.3 Subscribe to `ConnectionFailed` and `ConnectionRestored` events on the multiplexer, logging at Error and Information levels respectively. Include endpoint and failure type in log messages.
- [ ] 4.4 Implement IDisposable to properly dispose the ConnectionMultiplexer on shutdown.

## Task 5: ICacheService Interface and Redis Implementation

- [ ] 5.1 Create `src/CorePoints.Caching/Abstractions/ICacheService.cs` with methods: `GetAsync<T>`, `SetAsync<T>`, `GetOrSetAsync<T>` (with factory delegate and TTL), `InvalidateAsync(string key)`, `InvalidateAsync(IEnumerable<string> keys)`
- [ ] 5.2 Create `src/CorePoints.Caching/RedisCacheService.cs` implementing ICacheService. Inject IConnectionMultiplexer, ICacheSerializer, ResiliencePipeline (named "redis-cache"), and ILogger.
- [ ] 5.3 Implement `GetAsync<T>`: execute Redis GET through the resilience pipeline. On success, deserialize and return. On deserialization failure, log warning, DEL the key, return null.
- [ ] 5.4 Implement `SetAsync<T>`: serialize value, execute Redis SET with EX (TTL in seconds) through the resilience pipeline. On failure, log warning and return (no throw).
- [ ] 5.5 Implement `GetOrSetAsync<T>`: call GetAsync → if hit return value → if miss call factory → call SetAsync with result → return result. Ensure factory is called exactly once on miss.
- [ ] 5.6 Implement `InvalidateAsync(string key)` and `InvalidateAsync(IEnumerable<string> keys)`: execute Redis DEL through the resilience pipeline. On failure, log warning (do not throw) — rely on TTL safety net.
- [ ] 5.7 Create `src/CorePoints.Caching.Tests/RedisCacheServiceTests.cs` with tests using mocked IConnectionMultiplexer: cache hit returns value, cache miss calls factory, invalidation calls DEL, circuit open falls through to factory, deserialization failure triggers cleanup

## Task 6: Polly Resilience Pipeline

- [ ] 6.1 Register a named resilience pipeline "redis-cache" in DI using `AddResiliencePipeline`. Configure CircuitBreaker: MinimumThroughput from CacheOptions.CircuitBreakerThreshold (default 3), SamplingDuration 30s, BreakDuration from CacheOptions.CircuitBreakerBreakDurationSeconds (default 15), handle RedisException and TimeoutException.
- [ ] 6.2 Add a Timeout strategy of 1 second (from CacheOptions.SyncTimeoutMs) to the pipeline, nested inside the circuit breaker.
- [ ] 6.3 Verify that when the circuit breaker opens, GetOrSetAsync bypasses Redis and calls the factory directly without delay.

## Task 7: DI Registration Extension

- [ ] 7.1 Create `src/CorePoints.Caching/Extensions/CachingServiceCollectionExtensions.cs` with `AddCorePointsCaching(this IServiceCollection services, IConfiguration configuration)` extension method.
- [ ] 7.2 Register services: RedisConnectionManager (singleton), IConnectionMultiplexer (singleton factory from manager), ICacheSerializer as JsonCacheSerializer (singleton), ICacheService as RedisCacheService (singleton), CacheOptions bound from configuration section "Caching".
- [ ] 7.3 Register the Polly resilience pipeline "redis-cache" reading thresholds from CacheOptions.
- [ ] 7.4 Add startup validation: log Warning if Redis endpoint is empty or SSM fetch fails, but allow application to start (abortConnect=false ensures graceful degradation).

## Task 8: Ledger Balance Cache Integration

- [ ] 8.1 Create a `LedgerBalanceCacheService` helper class (or extension methods) that wraps ICacheService with ledger-specific logic: `GetBalanceAsync(string accountId)` calls `GetOrSetAsync` with key from `CacheKeyBuilder.LedgerBalance(accountId)` and TTL from `CacheOptions.LedgerBalanceTtlSeconds`.
- [ ] 8.2 Implement `InvalidateBalanceAsync(params string[] accountIds)` that builds keys via CacheKeyBuilder and calls `ICacheService.InvalidateAsync`. This is called synchronously after PostgreSQL commit in the ledger transaction flow.
- [ ] 8.3 Document usage pattern: after `await _dbTransaction.CommitAsync()`, immediately call `await _balanceCache.InvalidateBalanceAsync(debitAccountId, creditAccountId)`.

## Task 9: Product Data Cache Integration

- [ ] 9.1 Create a `ProductDataCacheService` helper class that wraps ICacheService with product-specific logic: `GetProductAsync<T>(string productId)` calls `GetOrSetAsync` with key from `CacheKeyBuilder.ProductData(productId)` and TTL from `CacheOptions.ProductDataTtlSeconds`.
- [ ] 9.2 Implement `InvalidateProductAsync(string productId)` for use by the SQS event consumer when a ProductUpdated event is received.
- [ ] 9.3 Document the SQS consumer integration point: the existing SQS consumer should call `InvalidateProductAsync` when receiving product change events.

## Task 10: Observability and Metrics

- [ ] 10.1 Add structured logging to RedisCacheService: log cache hit/miss at Debug level, log errors at Warning/Error level. Include cache key (without sensitive data) and operation duration.
- [ ] 10.2 Add metric counters: `cache_hits_total`, `cache_misses_total`, `cache_errors_total` with labels for `service` and `entity` (extracted from cache key). Use .NET Meters API.
- [ ] 10.3 Log circuit breaker state transitions (Closed→Open, Open→HalfOpen, HalfOpen→Closed) at Information level.
