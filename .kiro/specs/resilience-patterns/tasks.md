# Tasks

## Task 1: Resilience Options and Configuration

Create the configuration options classes and appsettings schema for all resilience parameters.

- [ ] 1.1 Create `LedgerResilienceOptions.cs` with properties: MaxRetryAttempts, MedianFirstRetryDelay, AttemptTimeout, CircuitBreakerDuration, CircuitBreakerThreshold, CircuitBreakerSamplingWindow, BulkheadMaxConcurrency, BulkheadQueueLimit. Bind to section `Resilience:Ledger`.
- [ ] 1.2 Create `ExternalResilienceOptions.cs` with properties: MaxRetryAttempts, MedianFirstRetryDelay, AttemptTimeout, TotalTimeout, CircuitBreakerDuration, CircuitBreakerThreshold, CircuitBreakerSamplingWindow, BulkheadMaxConcurrency, BulkheadQueueLimit. Bind to section `Resilience:External`.
- [ ] 1.3 Add `Resilience`, `RateLimiting`, and `Services` sections to `appsettings.json` with default values as specified in the design.
- [ ] 1.4 Register options via `services.Configure<T>()` in the DI extension method.

## Task 2: Ledger Resilience Pipeline and Named Client

Register the "LedgerClient" named HttpClient with the full Polly v8 pipeline.

- [ ] 2.1 Create `ResilienceServiceCollectionExtensions.cs` with `AddCorePointsResilience` extension method.
- [ ] 2.2 Register "LedgerClient" named HttpClient with base address from configuration (`Services:Ledger:BaseUrl`).
- [ ] 2.3 Add `.AddResilienceHandler("ledger-pipeline")` with strategies in order: Fallback → ConcurrencyLimiter (50) → TotalTimeout → Retry (3x, exponential + jitter) → CircuitBreaker (5 failures / 30s window / 30s break) → AttemptTimeout (10s).
- [ ] 2.4 Implement fallback delegate returning a typed error response with correlation ID and error code `LEDGER_UNAVAILABLE`.

## Task 3: External Resilience Pipeline and Named Client

Register the "ExternalClient" named HttpClient with its resilience pipeline.

- [ ] 3.1 Register "ExternalClient" named HttpClient with configurable base address.
- [ ] 3.2 Add `.AddResilienceHandler("external-pipeline")` with strategies: Fallback → ConcurrencyLimiter (20) → TotalTimeout (30s) → Retry (3x, exponential + jitter) → CircuitBreaker (10 failures / 60s window / 60s break) → AttemptTimeout (15s).
- [ ] 3.3 Implement fallback delegate returning a typed error response with correlation ID and error code `EXTERNAL_SERVICE_UNAVAILABLE`.

## Task 4: Typed Ledger Client

Create the typed client wrapper that uses the named "LedgerClient" with CancellationToken and correlation headers.

- [ ] 4.1 Create `ILedgerClient` interface with methods: `PostTransactionAsync`, `GetBalanceAsync`, `GetStatementAsync`, `GetTransactionAsync` — all accepting `CancellationToken`.
- [ ] 4.2 Create `LedgerHttpClient` implementation that resolves "LedgerClient" from IHttpClientFactory, attaches `Idempotency-Key` and `X-Correlation-ID` headers, and propagates CancellationToken on every call.
- [ ] 4.3 Register `ILedgerClient` → `LedgerHttpClient` as scoped in DI.

## Task 5: CancellationToken Propagation Middleware

Implement middleware that differentiates client disconnect from timeout cancellations.

- [ ] 5.1 Create `CancellationHandlingMiddleware` that catches `OperationCanceledException` and `TimeoutRejectedException`.
- [ ] 5.2 Return HTTP 499 and log at Information level when `context.RequestAborted.IsCancellationRequested` is true (client disconnect).
- [ ] 5.3 Return HTTP 504 and log at Warning level when a `TimeoutRejectedException` is caught (timeout policy triggered).
- [ ] 5.4 Register middleware early in the pipeline (before controllers, after rate limiting).

## Task 6: Health Check Endpoints

Implement liveness and readiness health check endpoints for ECS.

- [ ] 6.1 Create `LedgerHealthCheck : IHealthCheck` that calls Ledger `/health/live` with a 5-second timeout via CancellationTokenSource.
- [ ] 6.2 Register health checks: `AddNpgSql` (tag: ready), `AddRedis` (tag: ready), `AddCheck<LedgerHealthCheck>` (tag: ready).
- [ ] 6.3 Map `/health/live` endpoint with predicate `_ => false` (always 200, no dependency checks).
- [ ] 6.4 Map `/health/ready` endpoint with predicate filtering on `"ready"` tag, returning JSON response with per-dependency status.
- [ ] 6.5 Configure health check timeout to 5 seconds via `HealthCheckOptions.Timeout`.

## Task 7: Rate Limiting Middleware

Implement inbound rate limiting using System.Threading.RateLimiting.

- [ ] 7.1 Create `RateLimitingExtensions.cs` with `AddCorePointsRateLimiting` extension method.
- [ ] 7.2 Add fixed-window limiter "PerClient" partitioned by client IP (from `X-Forwarded-For` or `RemoteIpAddress`), reading PermitLimit and WindowSeconds from config.
- [ ] 7.3 Add global concurrency limiter "Global" with configurable PermitLimit and QueueLimit.
- [ ] 7.4 Set `OnRejected` callback to write `Retry-After` header from lease metadata.
- [ ] 7.5 Apply `[EnableRateLimiting("PerClient")]` attribute pattern or global policy, and register `app.UseRateLimiter()` in the pipeline.

## Task 8: Integration Wiring in Program.cs

Wire all components together in the application startup.

- [ ] 8.1 Call `builder.Services.AddCorePointsResilience(builder.Configuration)` in Program.cs.
- [ ] 8.2 Call `builder.Services.AddCorePointsRateLimiting(builder.Configuration)` in Program.cs.
- [ ] 8.3 Register health checks with proper tags and timeout.
- [ ] 8.4 Add middleware in correct order: `UseRateLimiter()` → `UseCancellationHandling()` → `UseRouting()` → `UseEndpoints()`.
- [ ] 8.5 Map health check endpoints at `/health/live` and `/health/ready`.

## Task 9: Unit Tests for Resilience Pipelines

Write tests verifying the resilience behavior.

- [ ] 9.1 Test that transient HTTP errors (502, 503, 504) trigger retry up to MaxRetryAttempts with exponential delays.
- [ ] 9.2 Test that circuit breaker opens after configured consecutive failures and rejects subsequent requests immediately.
- [ ] 9.3 Test that requests exceeding AttemptTimeout are cancelled and treated as transient failures.
- [ ] 9.4 Test that bulkhead rejects requests when concurrency limit + queue are exhausted.
- [ ] 9.5 Test that fallback delegate is invoked when circuit is open or retries exhausted.
- [ ] 9.6 Test that CancellationToken propagation cancels in-flight HTTP calls when the token fires.
- [ ] 9.7 Test health check returns Unhealthy when dependency is unreachable within 5s timeout.
- [ ] 9.8 Test rate limiter returns 429 with Retry-After header when limit exceeded.
