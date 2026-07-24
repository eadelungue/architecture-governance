# Design Document

## Overview

This design implements the Redis distributed caching layer for CorePoints using C# / .NET 8, StackExchange.Redis, System.Text.Json, and Polly. The architecture follows Cache-Aside with two distinct invalidation strategies: synchronous for ledger balances and event-driven for product data.

## Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                      Application Layer                        │
│                                                              │
│  ┌──────────────┐    ┌──────────────────┐                   │
│  │ LedgerService│    │  ProductService   │                   │
│  └──────┬───────┘    └────────┬─────────┘                   │
│         │                     │                              │
│         ▼                     ▼                              │
│  ┌─────────────────────────────────────────┐                │
│  │         ICacheService                    │                │
│  │  - GetOrSetAsync<T>(key, factory, ttl)  │                │
│  │  - InvalidateAsync(key)                  │                │
│  │  - SetAsync<T>(key, value, ttl)         │                │
│  └──────────────────┬──────────────────────┘                │
│                     │                                        │
│         ┌───────────┼───────────┐                           │
│         ▼           ▼           ▼                           │
│  ┌────────────┐ ┌────────┐ ┌────────────────┐              │
│  │CacheKeyBldr│ │Serialzr│ │CircuitBreaker  │              │
│  └────────────┘ └────────┘ │(Polly)         │              │
│                             └───────┬────────┘              │
│                                     │                        │
│         ┌───────────────────────────┼──────┐                │
│         ▼                           ▼      │                │
│  ┌─────────────────────────────────────┐   │                │
│  │     RedisConnectionManager          │   │                │
│  │  (Singleton ConnectionMultiplexer)  │   │                │
│  └──────────────────┬──────────────────┘   │                │
│                     │                       │                │
└─────────────────────┼───────────────────────┘                
                      ▼                                        
          ┌───────────────────┐                                
          │  ElastiCache Redis │                                
          │  (cache.t3.micro)  │                                
          └───────────────────┘                                
```

## Component Design

### 1. ICacheService Interface

```csharp
namespace CorePoints.Caching.Abstractions;

public interface ICacheService
{
    Task<T?> GetAsync<T>(string key, CancellationToken ct = default);
    Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken ct = default);
    Task<T> GetOrSetAsync<T>(string key, Func<CancellationToken, Task<T>> factory, TimeSpan ttl, CancellationToken ct = default);
    Task InvalidateAsync(string key, CancellationToken ct = default);
    Task InvalidateAsync(IEnumerable<string> keys, CancellationToken ct = default);
}
```

### 2. RedisCacheService Implementation

```csharp
namespace CorePoints.Caching;

public sealed class RedisCacheService : ICacheService
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ICacheSerializer _serializer;
    private readonly ResiliencePipeline _resiliencePipeline;
    private readonly ILogger<RedisCacheService> _logger;

    // GetOrSetAsync implements the full Cache-Aside pattern:
    // 1. Try Redis GET through circuit breaker
    // 2. On miss or circuit open → call factory (DB)
    // 3. On successful DB fetch → SET in Redis with TTL (fire-and-forget if circuit half-open)
    // 4. Return value to caller
}
```

### 3. CacheKeyBuilder

```csharp
namespace CorePoints.Caching;

public static class CacheKeyBuilder
{
    /// <summary>
    /// Builds a cache key in format {service}:{entity}:{id}.
    /// Throws ArgumentException if any component is null, empty, or contains invalid characters.
    /// </summary>
    public static string Build(string service, string entity, string id);

    // Predefined builders for known domains
    public static string LedgerBalance(string accountId) => Build("ledger", "balance", accountId);
    public static string ProductData(string productId) => Build("product", "data", productId);
}
```

### 4. RedisConnectionManager

```csharp
namespace CorePoints.Caching.Infrastructure;

public sealed class RedisConnectionManager : IDisposable
{
    // Reads endpoint from SSM at startup (/{env}/redis/primary_endpoint)
    // Creates a singleton ConnectionMultiplexer with:
    //   - abortConnect = false
    //   - connectTimeout = 5000ms
    //   - syncTimeout = 1000ms
    // Subscribes to ConnectionFailed/ConnectionRestored events for logging
}
```

### 5. CacheSerializer

```csharp
namespace CorePoints.Caching;

public interface ICacheSerializer
{
    byte[] Serialize<T>(T value);
    T? Deserialize<T>(byte[] data);
}

public sealed class JsonCacheSerializer : ICacheSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };
}
```

### 6. Resilience Pipeline (Polly v8)

```csharp
// Registered in DI as a named ResiliencePipeline
services.AddResiliencePipeline("redis-cache", builder =>
{
    builder.AddCircuitBreaker(new CircuitBreakerStrategyOptions
    {
        FailureRatio = 1.0,  // All failures count
        SamplingDuration = TimeSpan.FromSeconds(30),
        MinimumThroughput = 3,  // 3 failures to open
        BreakDuration = TimeSpan.FromSeconds(15),
        ShouldHandle = new PredicateBuilder().Handle<RedisException>().Handle<TimeoutException>()
    });
    builder.AddTimeout(TimeSpan.FromSeconds(1));
});
```

## Data Flow

### Cache-Aside Read (Ledger Balance)

```
1. LedgerService.GetBalance(accountId)
2. → ICacheService.GetOrSetAsync(
       key: CacheKeyBuilder.LedgerBalance(accountId),
       factory: () => dapper.QueryBalance(accountId),
       ttl: TimeSpan.FromSeconds(7)
   )
3. → Circuit Breaker check → if OPEN → skip to step 5
4. → Redis GET "ledger:balance:{accountId}"
      HIT  → deserialize → return
      MISS → continue
5. → factory() → PostgreSQL query → balance value
6. → Redis SET "ledger:balance:{accountId}" value EX 7
7. → return balance
```

### Synchronous Invalidation (Ledger Write)

```
1. LedgerService.RecordTransaction(debitAccountId, creditAccountId)
2. → PostgreSQL BEGIN → INSERT → COMMIT (ACID)
3. → ICacheService.InvalidateAsync([
       CacheKeyBuilder.LedgerBalance(debitAccountId),
       CacheKeyBuilder.LedgerBalance(creditAccountId)
   ])
4. → Redis DEL "ledger:balance:{debitId}" "ledger:balance:{creditId}"
5. → if Redis error → log Warning, rely on TTL expiry
6. → return transaction result to caller
```

### Event-Driven Invalidation (Product Data)

```
1. SQS Consumer receives ProductUpdated event
2. → Extract productId from event payload
3. → ICacheService.InvalidateAsync(CacheKeyBuilder.ProductData(productId))
4. → Redis DEL "product:data:{productId}"
5. → if key not found → no-op (success)
```

## DI Registration

```csharp
namespace CorePoints.Caching.Extensions;

public static class CachingServiceCollectionExtensions
{
    public static IServiceCollection AddCorePontsCaching(
        this IServiceCollection services, 
        IConfiguration configuration)
    {
        // 1. Register RedisConnectionManager as singleton
        // 2. Register IConnectionMultiplexer (from manager) as singleton
        // 3. Register ICacheSerializer as singleton
        // 4. Register ICacheService as singleton
        // 5. Register Polly resilience pipeline "redis-cache"
        // 6. Bind CacheOptions from configuration/env vars
    }
}
```

## Configuration Model

```csharp
public sealed class CacheOptions
{
    public string RedisEndpoint { get; set; } = "";  // From SSM
    public int LedgerBalanceTtlSeconds { get; set; } = 7;
    public int ProductDataTtlSeconds { get; set; } = 1800;
    public int CircuitBreakerThreshold { get; set; } = 3;
    public int CircuitBreakerBreakDurationSeconds { get; set; } = 15;
    public int ConnectTimeoutMs { get; set; } = 5000;
    public int SyncTimeoutMs { get; set; } = 1000;
}
```

## Error Handling Strategy

| Scenario | Behavior |
|----------|----------|
| Redis timeout on GET | Return cache miss, call DB factory |
| Redis timeout on SET | Log warning, return value from DB (data still served) |
| Redis down (circuit open) | Bypass cache entirely, all reads go to DB |
| Deserialization failure | Log warning, DEL corrupted key, treat as miss |
| Invalidation failure | Log warning, rely on TTL safety net |
| SSM parameter missing | Use empty endpoint → connection fails → circuit opens → DB fallback |

## File Structure

```
src/CorePoints.Caching/
├── Abstractions/
│   ├── ICacheService.cs
│   └── ICacheSerializer.cs
├── Infrastructure/
│   ├── RedisConnectionManager.cs
│   └── JsonCacheSerializer.cs
├── CacheKeyBuilder.cs
├── RedisCacheService.cs
├── CacheOptions.cs
├── Extensions/
│   └── CachingServiceCollectionExtensions.cs
└── CorePoints.Caching.csproj

src/CorePoints.Caching.Tests/
├── CacheKeyBuilderTests.cs
├── RedisCacheServiceTests.cs
├── JsonCacheSerializerTests.cs
└── CorePoints.Caching.Tests.csproj
```

## Dependencies

```xml
<PackageReference Include="StackExchange.Redis" Version="2.7.*" />
<PackageReference Include="Microsoft.Extensions.Caching.StackExchangeRedis" Version="8.0.*" />
<PackageReference Include="Microsoft.Extensions.Resilience" Version="8.0.*" />
<PackageReference Include="AWSSDK.SimpleSystemsManagement" Version="3.*" />
<PackageReference Include="System.Text.Json" Version="8.0.*" />
```

## Correctness Properties

1. **Round-trip serialization**: For any serializable value T, `Deserialize<T>(Serialize(value))` produces an equivalent object.
2. **Cache-Aside invariant**: After GetOrSetAsync returns, either the value was served from cache (hit) or the factory was called exactly once and the result was stored.
3. **Key format invariant**: Every key produced by CacheKeyBuilder matches the regex `^[a-z0-9-]+:[a-z0-9-]+:[a-z0-9-]+$`.
4. **Synchronous invalidation ordering**: Invalidation of ledger balance keys always occurs AFTER the PostgreSQL ACID commit, never before.
5. **Circuit breaker idempotence**: Multiple consecutive failures beyond threshold do not cause additional side effects — the circuit remains open.
6. **Graceful degradation**: When circuit is open, GetOrSetAsync still returns valid data (from DB) — it never throws due to Redis unavailability.
7. **TTL safety net**: Every ledger balance cache entry has TTL in range [5, 10] seconds regardless of invalidation success.
