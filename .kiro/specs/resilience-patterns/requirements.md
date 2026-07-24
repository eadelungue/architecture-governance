# Requirements Document

## Introduction

This feature implements cross-cutting resilience patterns for the CorePoints platform. All outgoing HTTP calls from Product services (and any future service) must be wrapped with Polly v8 resilience pipelines including retry, circuit breaker, timeout, and bulkhead isolation. The Product→Ledger communication path receives the strictest resilience guarantees (circuit breaker mandatory). Health checks expose service liveness for ECS orchestration. Inbound rate limiting protects the platform from abuse.

## Glossary

- **Resilience_Pipeline**: A Polly v8 composed strategy combining retry, circuit breaker, timeout, and optionally bulkhead policies into a single reusable pipeline.
- **Product_Service**: Any CorePoints service in the Product layer that calls the Ledger Core or external APIs.
- **Ledger_Client**: A typed HttpClient used by Product services to communicate with the Ledger Core via AWS Cloud Map (HTTP, internal network).
- **External_Client**: A named HttpClient used for calls to third-party or non-critical downstream services.
- **Circuit_Breaker**: A Polly strategy that opens after consecutive failures, rejecting subsequent requests for a configured duration to protect the downstream.
- **Bulkhead**: A concurrency limiter that isolates resource consumption between critical and non-critical call paths.
- **Health_Check_Endpoint**: An ASP.NET Core health check route that ECS uses for task liveness and readiness probes.
- **Rate_Limiter**: Middleware that constrains inbound request throughput per client or globally using System.Threading.RateLimiting.
- **CancellationToken**: A .NET token propagated through the async call chain enabling cooperative cancellation on timeout or client disconnect.

## Requirements

### Requirement 1: Ledger Resilience Pipeline

**User Story:** As a Product service developer, I want a pre-configured resilience pipeline for Ledger HTTP calls, so that transient failures are retried and the Ledger is protected from overload via circuit breaker.

#### Acceptance Criteria

1. WHEN the Ledger_Client sends an HTTP request and receives a transient error (HTTP 408, 429, 500, 502, 503, 504), THE Resilience_Pipeline SHALL retry the request using exponential backoff with jitter, up to a maximum of 3 retry attempts.
2. WHEN the Ledger_Client experiences 5 consecutive failed requests within a 30-second sampling window, THE Circuit_Breaker SHALL open and reject subsequent requests for 30 seconds, returning a failure immediately.
3. WHILE the Circuit_Breaker is in the open state, THE Ledger_Client SHALL not send requests to the Ledger Core and SHALL return a predefined failure response to the caller.
4. WHEN the Circuit_Breaker transitions from open to half-open state, THE Ledger_Client SHALL permit a single probe request to determine if the Ledger Core has recovered.
5. THE Resilience_Pipeline SHALL apply a per-request timeout of 10 seconds; IF the Ledger Core does not respond within 10 seconds, THEN THE Ledger_Client SHALL cancel the request and treat it as a transient failure.

### Requirement 2: External Client Resilience Pipeline

**User Story:** As a Product service developer, I want a standard resilience pipeline for external/third-party HTTP calls, so that all outgoing calls have baseline protection without manual configuration.

#### Acceptance Criteria

1. WHEN the External_Client sends an HTTP request and receives a transient error, THE Resilience_Pipeline SHALL retry the request using exponential backoff with jitter, up to a maximum of 3 retry attempts.
2. WHEN the External_Client experiences 10 consecutive failed requests within a 60-second sampling window, THE Circuit_Breaker SHALL open for 60 seconds.
3. THE Resilience_Pipeline SHALL apply a per-request timeout of 15 seconds; IF the downstream does not respond within 15 seconds, THEN THE External_Client SHALL cancel the request.
4. THE Resilience_Pipeline SHALL apply a total request timeout of 30 seconds across all retry attempts; IF retries exceed 30 seconds total, THEN THE External_Client SHALL abandon the operation.

### Requirement 3: Named HttpClient Registration

**User Story:** As a Platform engineer, I want resilience pipelines registered per named HttpClient at DI startup, so that developers get resilience automatically by injecting the correct client.

#### Acceptance Criteria

1. WHEN the application starts, THE Product_Service SHALL register a named HttpClient "LedgerClient" with the Ledger Resilience Pipeline attached.
2. WHEN the application starts, THE Product_Service SHALL register a named HttpClient "ExternalClient" with the External Resilience Pipeline attached.
3. THE Product_Service SHALL configure each named HttpClient with a base address resolvable via AWS Cloud Map or environment configuration.
4. WHEN a developer injects IHttpClientFactory and requests a named client, THE IHttpClientFactory SHALL return an HttpClient instance with the corresponding resilience pipeline pre-configured.

### Requirement 4: CancellationToken Propagation

**User Story:** As a platform engineer, I want CancellationToken propagated through the entire async call chain, so that cancelled or timed-out requests release resources immediately.

#### Acceptance Criteria

1. THE Product_Service SHALL pass the CancellationToken from the ASP.NET Core request context to every async method in the call chain (controller → use case → repository/client).
2. WHEN the client disconnects or the request timeout expires, THE Product_Service SHALL trigger cancellation via the CancellationToken, causing in-flight async operations to terminate cooperatively.
3. IF an OperationCanceledException is thrown due to client disconnect, THEN THE Product_Service SHALL return HTTP 499 (Client Closed Request) and log the cancellation at Information level.
4. IF an OperationCanceledException is thrown due to a timeout policy, THEN THE Product_Service SHALL return HTTP 504 (Gateway Timeout) and log the timeout at Warning level.

### Requirement 5: Health Check Endpoints

**User Story:** As an infrastructure engineer, I want health check endpoints reporting service readiness, so that ECS can route traffic only to healthy instances.

#### Acceptance Criteria

1. THE Health_Check_Endpoint SHALL expose a liveness probe at `/health/live` that returns HTTP 200 when the process is running.
2. THE Health_Check_Endpoint SHALL expose a readiness probe at `/health/ready` that returns HTTP 200 only when all critical dependencies (PostgreSQL, Redis, Ledger Core) are reachable.
3. WHEN any critical dependency is unreachable, THE Health_Check_Endpoint SHALL return HTTP 503 on the readiness probe with a JSON body indicating which dependency failed.
4. THE Health_Check_Endpoint SHALL complete within 5 seconds; IF a dependency check exceeds 5 seconds, THEN THE Health_Check_Endpoint SHALL report that dependency as unhealthy.

### Requirement 6: Rate Limiting for Inbound Requests

**User Story:** As a platform engineer, I want inbound rate limiting on Product service endpoints, so that abusive traffic is rejected before consuming compute resources.

#### Acceptance Criteria

1. THE Rate_Limiter SHALL enforce a fixed-window rate limit of configurable requests-per-second per client IP.
2. WHEN a client exceeds the configured rate limit, THE Rate_Limiter SHALL return HTTP 429 (Too Many Requests) with a `Retry-After` header indicating when the client may retry.
3. THE Rate_Limiter SHALL apply a global concurrency limit to protect total system capacity independently of per-client limits.
4. THE Rate_Limiter SHALL read configuration values (window size, permit limit, queue limit) from `appsettings.json` or environment variables without requiring redeployment.

### Requirement 7: Bulkhead Isolation

**User Story:** As a platform architect, I want separate concurrency pools for critical and non-critical flows, so that slow third-party calls cannot exhaust resources needed for Ledger transactions.

#### Acceptance Criteria

1. THE Bulkhead SHALL limit concurrent outgoing requests to the Ledger Core to a maximum of 50 concurrent calls.
2. THE Bulkhead SHALL limit concurrent outgoing requests to external/non-critical services to a maximum of 20 concurrent calls.
3. WHEN the Bulkhead concurrency limit is reached, THE Resilience_Pipeline SHALL queue up to 10 additional requests; IF the queue is also full, THEN THE Resilience_Pipeline SHALL reject the request immediately with a BulkheadRejectedException.
4. THE Bulkhead limits SHALL be configurable via `appsettings.json` without code changes.

### Requirement 8: Fallback Strategies

**User Story:** As a Product service developer, I want fallback responses when downstream services fail, so that the user experience degrades gracefully instead of returning raw 500 errors.

#### Acceptance Criteria

1. WHEN the Ledger_Client circuit breaker is open or a request fails after all retries, THE Resilience_Pipeline SHALL execute a fallback delegate that returns a typed error response indicating temporary unavailability.
2. WHEN the External_Client circuit breaker is open or a request fails after all retries, THE Resilience_Pipeline SHALL execute a fallback delegate that returns a cached or default response where applicable.
3. THE fallback response SHALL include a correlation ID and a machine-readable error code so upstream callers can distinguish transient failures from permanent errors.
4. THE Product_Service SHALL log every fallback activation at Warning level including the downstream service name, failure reason, and correlation ID.
