# Requirements Document

## Introduction

This feature implements the distributed caching layer using Redis (ElastiCache) for the CorePoints platform. It provides Cache-Aside pattern for ledger balance data (synchronous invalidation) and product data (event-driven invalidation), with connection resilience and graceful degradation when Redis is unavailable.

## Glossary

- **Cache_Service**: The application-level caching abstraction that provides get, set, and invalidate operations against Redis.
- **Redis_Connection_Manager**: The component responsible for managing the StackExchange.Redis ConnectionMultiplexer lifecycle and health monitoring.
- **Ledger_Balance_Cache**: The cache subsystem dedicated to storing account balance values with synchronous invalidation semantics.
- **Product_Data_Cache**: The cache subsystem dedicated to storing product/catalog data with event-driven invalidation semantics.
- **Cache_Key**: A structured string identifier following the convention `{service}:{entity}:{id}` used to address cached items.
- **Circuit_Breaker**: A Polly resilience policy that opens after consecutive Redis failures, causing the system to bypass cache and fall through to the database.
- **TTL**: Time-To-Live — the duration after which a cached item expires automatically.
- **Cache_Miss**: The condition where a requested key does not exist in Redis, triggering a database fetch.
- **Synchronous_Invalidation**: The act of removing a cache entry within the same HTTP request that performed the write operation, after the ACID commit.

## Requirements

### Requirement 1: Cache-Aside Pattern

**User Story:** As a service developer, I want a Cache-Aside implementation, so that read operations check cache first and populate it on miss without coupling to a specific data source.

#### Acceptance Criteria

1. WHEN a read operation is requested, THE Cache_Service SHALL check Redis for the cached value before querying the database.
2. WHEN a Cache_Miss occurs, THE Cache_Service SHALL fetch the data from the database, store it in Redis with the configured TTL, and return the value to the caller.
3. WHEN a cached value exists (cache hit), THE Cache_Service SHALL return the cached value without querying the database.
4. THE Cache_Service SHALL accept a generic type parameter so that any serializable entity can be cached using the Cache-Aside pattern.

### Requirement 2: Ledger Balance Cache with Synchronous Invalidation

**User Story:** As a ledger service developer, I want balance values cached with synchronous invalidation after writes, so that read-heavy balance queries are fast while writes always reflect the latest state.

#### Acceptance Criteria

1. WHEN a balance query is requested, THE Ledger_Balance_Cache SHALL return the cached balance if present, or fetch from PostgreSQL and populate the cache on miss.
2. WHEN a ledger write transaction commits successfully, THE Ledger_Balance_Cache SHALL invalidate the cached balance for all affected account IDs synchronously within the same HTTP request.
3. THE Ledger_Balance_Cache SHALL set a TTL between 5 and 10 seconds on every cached balance entry as a safety net against failed synchronous invalidation.
4. IF the synchronous invalidation fails due to a transient Redis error, THEN THE Ledger_Balance_Cache SHALL log the failure at Warning level and allow the TTL safety net to expire the stale entry.

### Requirement 3: Product Data Cache with Event-Driven Invalidation

**User Story:** As a product service developer, I want product data cached with event-driven invalidation via SQS, so that frequently accessed catalog data is served from cache while remaining eventually consistent.

#### Acceptance Criteria

1. WHEN product data is requested, THE Product_Data_Cache SHALL return the cached value if present, or fetch from the database and populate the cache on miss.
2. THE Product_Data_Cache SHALL set a maximum TTL of 30 minutes on every cached product data entry.
3. WHEN an invalidation event is received from the SQS queue, THE Product_Data_Cache SHALL remove the corresponding cache entries identified by the event payload.
4. IF an invalidation event references a key that does not exist in cache, THEN THE Product_Data_Cache SHALL ignore the event without raising an error.

### Requirement 4: Cache Key Naming Convention

**User Story:** As a platform developer, I want a consistent cache key naming convention, so that keys are predictable, debuggable, and free of collisions across services.

#### Acceptance Criteria

1. THE Cache_Service SHALL construct cache keys using the format `{service}:{entity}:{id}` where service, entity, and id are non-empty lowercase strings.
2. IF a cache key component contains invalid characters (whitespace, colons, or empty values), THEN THE Cache_Service SHALL throw an ArgumentException before executing the Redis operation.
3. THE Cache_Service SHALL provide a static key-builder method that enforces the naming convention at compile time or runtime.

### Requirement 5: Redis Connection Management

**User Story:** As a platform engineer, I want Redis connections managed via a shared ConnectionMultiplexer with health monitoring, so that the application uses connections efficiently and detects failures early.

#### Acceptance Criteria

1. THE Redis_Connection_Manager SHALL create a single shared ConnectionMultiplexer instance registered as a singleton in the DI container.
2. WHEN the application starts, THE Redis_Connection_Manager SHALL read the Redis endpoint from AWS SSM Parameter Store via the configured environment path.
3. THE Redis_Connection_Manager SHALL configure the ConnectionMultiplexer with abortConnect=false so that the application starts even if Redis is temporarily unavailable.
4. THE Redis_Connection_Manager SHALL configure a connect timeout of 5 seconds and a sync timeout of 1 second for Redis operations.
5. WHEN the ConnectionMultiplexer raises a ConnectionFailed event, THE Redis_Connection_Manager SHALL log the failure at Error level including the endpoint and failure type.

### Requirement 6: Serialization Strategy

**User Story:** As a developer, I want cached values serialized with System.Text.Json using consistent settings, so that serialization is fast, deterministic, and compatible across service versions.

#### Acceptance Criteria

1. THE Cache_Service SHALL serialize values to JSON using System.Text.Json with camelCase property naming and no indentation.
2. THE Cache_Service SHALL deserialize cached JSON back to the requested type, returning a cache miss result if deserialization fails.
3. IF deserialization of a cached value fails, THEN THE Cache_Service SHALL log the failure at Warning level, remove the corrupted entry from Redis, and treat the operation as a Cache_Miss.

### Requirement 7: Resilience and Graceful Degradation

**User Story:** As a platform engineer, I want the application to degrade gracefully when Redis is unavailable, so that users are never blocked by cache infrastructure failures.

#### Acceptance Criteria

1. THE Circuit_Breaker SHALL open after 3 consecutive Redis operation failures within a 30-second window.
2. WHILE the Circuit_Breaker is open, THE Cache_Service SHALL bypass Redis entirely and route all read operations directly to the database without attempting cache access.
3. THE Circuit_Breaker SHALL transition to half-open state after 15 seconds, allowing a single probe request to Redis.
4. WHEN a probe request succeeds in half-open state, THE Circuit_Breaker SHALL close and resume normal cache operations.
5. IF a Redis operation times out beyond the configured sync timeout, THEN THE Cache_Service SHALL treat the operation as a failure and return a cache miss result to the caller.
6. THE Cache_Service SHALL record cache hit, miss, and error counts as application metrics for observability.

### Requirement 8: Configuration Management

**User Story:** As a DevOps engineer, I want cache configuration externalized to environment variables and SSM, so that TTL values and endpoints can be tuned without redeployment.

#### Acceptance Criteria

1. THE Redis_Connection_Manager SHALL read the Redis primary endpoint from the SSM parameter at path `/{environment}/redis/primary_endpoint`.
2. THE Cache_Service SHALL read ledger balance TTL from the environment variable `CACHE_LEDGER_BALANCE_TTL_SECONDS` with a default of 7 seconds.
3. THE Cache_Service SHALL read product data TTL from the environment variable `CACHE_PRODUCT_DATA_TTL_SECONDS` with a default of 1800 seconds (30 minutes).
4. THE Circuit_Breaker SHALL read its failure threshold from the environment variable `CACHE_CIRCUIT_BREAKER_THRESHOLD` with a default of 3.
5. IF a required configuration value is missing or unparseable, THEN THE Cache_Service SHALL use the documented default value and log a Warning at startup.
