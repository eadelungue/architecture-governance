# Design Document

## Overview

This design defines the implementation architecture for cross-cutting resilience patterns in CorePoints Product services. It uses Polly v8 via `Microsoft.Extensions.Http.Resilience`, typed/named HttpClients, ASP.NET Core health checks, and `System.Threading.RateLimiting`. All patterns are registered at DI startup and applied transparently to HTTP calls.

## Architecture

### Component Diagram

```
┌─────────────────────────────────────────────────────────────────┐
│ Product Service (ASP.NET Core / .NET 8)                         │
│                                                                 │
│  ┌──────────────┐    ┌────────────────────────────────────────┐ │
│  │ Rate Limiter │    │ ASP.NET Pipeline                       │ │
│  │ Middleware   │───▶│ Controller → UseCase → Client          │ │
│  └──────────────┘    └───────────────┬────────────────────────┘ │
│                                      │                          │
│  ┌───────────────────────────────────▼──────────────────────┐   │
│  │           IHttpClientFactory                              │   │
│  │  ┌─────────────────────┐  ┌────────────────────────────┐ │   │
│  │  │ "LedgerClient"      │  │ "ExternalClient"           │ │   │
│  │  │ ┌─────────────────┐ │  │ ┌────────────────────────┐ │ │   │
│  │  │ │ Bulkhead (50)   │ │  │ │ Bulkhead (20)          │ │ │   │
│  │  │ │ Timeout (10s)   │ │  │ │ Total Timeout (30s)    │ │ │   │
│  │  │ │ Retry (3x exp)  │ │  │ │ Timeout (15s)          │ │ │   │
│  │  │ │ CircuitBreaker  │ │  │ │ Retry (3x exp)         │ │ │   │
│  │  │ │ Fallback        │ │  │ │ CircuitBreaker          │ │ │   │
│  │  │ └─────────────────┘ │  │ │ Fallback               │ │ │   │
│  │  └─────────────────────┘  │ └────────────────────────┘ │ │   │
│  │                            └────────────────────────────┘ │   │
│  └───────────────────────────────────────────────────────────┘   │
│                                                                 │
│  ┌──────────────────────────────────────┐                       │
│  │ Health Checks                         │                       │
│  │  /health/live  → process alive        │                       │
│  │  /health/ready → DB + Redis + Ledger  │                       │
│  └──────────────────────────────────────┘                       │
└─────────────────────────────────────────────────────────────────┘
```

### Pipeline Ordering (Inner to Outer)

Polly v8 pipelines execute strategies from outer to inner. The order matters:

**Ledger Pipeline:** `Fallback → Bulkhead → TotalTimeout → Retry → CircuitBreaker → AttemptTimeout → HttpCall`

**External Pipeline:** `Fallback → Bulkhead → TotalTimeout → Retry → CircuitBreaker → AttemptTimeout → HttpCall`

### Key Design Decisions

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Pipeline registration | `AddStandardResilienceHandler` + custom overrides | Follows Microsoft.Extensions.Http.Resilience patterns |
| Typed vs Named clients | Named clients with typed wrappers | Allows DI flexibility while providing strong typing |
| Configuration source | `IOptions<T>` bound to `appsettings.json` | Hot-reload support, no redeployment needed |
| Health check framework | `Microsoft.Extensions.Diagnostics.HealthChecks` | Native ASP.NET Core, ECS-compatible |
| Rate limiting | `System.Threading.RateLimiting` middleware | Built into .NET 8, no external deps |
| Fallback pattern | Delegate-based via Polly `AddFallback<HttpResponseMessage>` | Typed, testable, composable |

## Components

### 1. Resilience Configuration Options

```csharp
// src/Infrastructure/Resilience/LedgerResilienceOptions.cs
public sealed class LedgerResilienceOptions
{
    public const string SectionName = "Resilience:Ledger";
    
    public int MaxRetryAttempts { get; set; } = 3;
    public TimeSpan MedianFirstRetryDelay { get; set; } = TimeSpan.FromSeconds(1);
    public TimeSpan AttemptTimeout { get; set; } = TimeSpan.FromSeconds(10);
    public TimeSpan CircuitBreakerDuration { get; set; } = TimeSpan.FromSeconds(30);
    public int CircuitBreakerThreshold { get; set; } = 5;
    public TimeSpan CircuitBreakerSamplingWindow { get; set; } = TimeSpan.FromSeconds(30);
    public int BulkheadMaxConcurrency { get; set; } = 50;
    public int BulkheadQueueLimit { get; set; } = 10;
}

// src/Infrastructure/Resilience/ExternalResilienceOptions.cs
public sealed class ExternalResilienceOptions
{
    public const string SectionName = "Resilience:External";
    
    public int MaxRetryAttempts { get; set; } = 3;
    public TimeSpan MedianFirstRetryDelay { get; set; } = TimeSpan.FromSeconds(1);
    public TimeSpan AttemptTimeout { get; set; } = TimeSpan.FromSeconds(15);
    public TimeSpan TotalTimeout { get; set; } = TimeSpan.FromSeconds(30);
    public TimeSpan CircuitBreakerDuration { get; set; } = TimeSpan.FromSeconds(60);
    public int CircuitBreakerThreshold { get; set; } = 10;
    public TimeSpan CircuitBreakerSamplingWindow { get; set; } = TimeSpan.FromSeconds(60);
    public int BulkheadMaxConcurrency { get; set; } = 20;
    public int BulkheadQueueLimit { get; set; } = 10;
}
```

### 2. HttpClient Registration (DI Extension)

```csharp
// src/Infrastructure/Resilience/ResilienceServiceCollectionExtensions.cs
public static class ResilienceServiceCollectionExtensions
{
    public static IServiceCollection AddCorePointsResilience(
        this IServiceCollection services, IConfiguration configuration)
    {
        // Bind options
        services.Configure<LedgerResilienceOptions>(
            configuration.GetSection(LedgerResilienceOptions.SectionName));
        services.Configure<ExternalResilienceOptions>(
            configuration.GetSection(ExternalResilienceOptions.SectionName));

        // Ledger Client
        services.AddHttpClient("LedgerClient", (sp, client) =>
        {
            client.BaseAddress = new Uri(configuration["Services:Ledger:BaseUrl"]!);
            client.DefaultRequestHeaders.Add("Accept", "application/json");
        })
        .AddResilienceHandler("ledger-pipeline", (builder, context) =>
        {
            var options = context.ServiceProvider
                .GetRequiredService<IOptions<LedgerResilienceOptions>>().Value;
            
            builder
                .AddFallback(new FallbackStrategyOptions<HttpResponseMessage> { /* ... */ })
                .AddConcurrencyLimiter(new ConcurrencyLimiterOptions
                {
                    PermitLimit = options.BulkheadMaxConcurrency,
                    QueueLimit = options.BulkheadQueueLimit
                })
                .AddTimeout(new TimeoutStrategyOptions
                {
                    Timeout = options.AttemptTimeout * (options.MaxRetryAttempts + 1)
                })
                .AddRetry(new HttpRetryStrategyOptions
                {
                    MaxRetryAttempts = options.MaxRetryAttempts,
                    BackoffType = DelayBackoffType.Exponential,
                    UseJitter = true,
                    Delay = options.MedianFirstRetryDelay
                })
                .AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
                {
                    FailureRatio = 0.9,
                    MinimumThroughput = options.CircuitBreakerThreshold,
                    SamplingDuration = options.CircuitBreakerSamplingWindow,
                    BreakDuration = options.CircuitBreakerDuration
                })
                .AddTimeout(options.AttemptTimeout);
        });

        // External Client (similar pattern with ExternalResilienceOptions)
        services.AddHttpClient("ExternalClient", /* ... */)
            .AddResilienceHandler("external-pipeline", /* ... */);

        return services;
    }
}
```

### 3. Typed Ledger Client

```csharp
// src/Infrastructure/Clients/LedgerHttpClient.cs
public sealed class LedgerHttpClient : ILedgerClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<LedgerHttpClient> _logger;

    public LedgerHttpClient(IHttpClientFactory factory, ILogger<LedgerHttpClient> logger)
    {
        _httpClient = factory.CreateClient("LedgerClient");
        _logger = logger;
    }

    public async Task<TransactionResult> PostTransactionAsync(
        CreateTransactionRequest request,
        string idempotencyKey,
        string correlationId,
        CancellationToken cancellationToken)
    {
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "/transactions");
        httpRequest.Headers.Add("Idempotency-Key", idempotencyKey);
        httpRequest.Headers.Add("X-Correlation-ID", correlationId);
        httpRequest.Content = JsonContent.Create(request);

        var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<TransactionResult>(cancellationToken: cancellationToken)!;
    }

    // Additional methods: GetBalanceAsync, GetStatementAsync, etc.
}
```

### 4. CancellationToken Propagation Pattern

```csharp
// Controllers receive CancellationToken from framework
[HttpPost("transactions")]
public async Task<IActionResult> CreateTransaction(
    [FromBody] CreateTransactionDto dto,
    CancellationToken cancellationToken)
{
    var result = await _useCase.ExecuteAsync(dto, cancellationToken);
    return Ok(result);
}

// Middleware to differentiate client disconnect vs timeout
// src/Infrastructure/Middleware/CancellationHandlingMiddleware.cs
public class CancellationHandlingMiddleware
{
    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        try
        {
            await next(context);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            context.Response.StatusCode = 499; // Client closed
            _logger.LogInformation("Client disconnected: {Path}", context.Request.Path);
        }
        catch (TimeoutRejectedException)
        {
            context.Response.StatusCode = 504;
            _logger.LogWarning("Timeout on: {Path}", context.Request.Path);
        }
    }
}
```

### 5. Health Check Implementation

```csharp
// src/Infrastructure/HealthChecks/LedgerHealthCheck.cs
public class LedgerHealthCheck : IHealthCheck
{
    private readonly HttpClient _httpClient;

    public LedgerHealthCheck(IHttpClientFactory factory)
    {
        _httpClient = factory.CreateClient("LedgerClient");
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _httpClient.GetAsync("/health/live", cancellationToken);
            return response.IsSuccessStatusCode
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy($"Ledger returned {response.StatusCode}");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Ledger unreachable", ex);
        }
    }
}

// Registration in Program.cs
builder.Services.AddHealthChecks()
    .AddNpgSql(connectionString, name: "postgresql", tags: new[] { "ready" })
    .AddRedis(redisConnection, name: "redis", tags: new[] { "ready" })
    .AddCheck<LedgerHealthCheck>("ledger", tags: new[] { "ready" });

app.MapHealthChecks("/health/live", new() { Predicate = _ => false }); // always 200
app.MapHealthChecks("/health/ready", new() { Predicate = check => check.Tags.Contains("ready") });
```

### 6. Rate Limiting Middleware

```csharp
// src/Infrastructure/RateLimiting/RateLimitingExtensions.cs
public static class RateLimitingExtensions
{
    public static IServiceCollection AddCorePointsRateLimiting(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            options.AddFixedWindowLimiter("PerClient", opt =>
            {
                opt.PermitLimit = configuration.GetValue("RateLimiting:PerClient:PermitLimit", 100);
                opt.Window = TimeSpan.FromSeconds(
                    configuration.GetValue("RateLimiting:PerClient:WindowSeconds", 60));
                opt.QueueLimit = configuration.GetValue("RateLimiting:PerClient:QueueLimit", 0);
            });

            options.AddConcurrencyLimiter("Global", opt =>
            {
                opt.PermitLimit = configuration.GetValue("RateLimiting:Global:PermitLimit", 500);
                opt.QueueLimit = configuration.GetValue("RateLimiting:Global:QueueLimit", 50);
            });

            options.OnRejected = async (context, ct) =>
            {
                context.HttpContext.Response.Headers.RetryAfter =
                    context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter)
                        ? retryAfter.TotalSeconds.ToString("0")
                        : "60";
            };
        });

        return services;
    }
}
```

### 7. Configuration Schema (appsettings.json)

```json
{
  "Resilience": {
    "Ledger": {
      "MaxRetryAttempts": 3,
      "MedianFirstRetryDelay": "00:00:01",
      "AttemptTimeout": "00:00:10",
      "CircuitBreakerDuration": "00:00:30",
      "CircuitBreakerThreshold": 5,
      "CircuitBreakerSamplingWindow": "00:00:30",
      "BulkheadMaxConcurrency": 50,
      "BulkheadQueueLimit": 10
    },
    "External": {
      "MaxRetryAttempts": 3,
      "MedianFirstRetryDelay": "00:00:01",
      "AttemptTimeout": "00:00:15",
      "TotalTimeout": "00:00:30",
      "CircuitBreakerDuration": "00:01:00",
      "CircuitBreakerThreshold": 10,
      "CircuitBreakerSamplingWindow": "00:01:00",
      "BulkheadMaxConcurrency": 20,
      "BulkheadQueueLimit": 10
    }
  },
  "RateLimiting": {
    "PerClient": {
      "PermitLimit": 100,
      "WindowSeconds": 60,
      "QueueLimit": 0
    },
    "Global": {
      "PermitLimit": 500,
      "QueueLimit": 50
    }
  },
  "Services": {
    "Ledger": {
      "BaseUrl": "http://ledger-core.corepoints.local"
    }
  }
}
```

## File Structure

```
src/
├── Infrastructure/
│   ├── Resilience/
│   │   ├── LedgerResilienceOptions.cs
│   │   ├── ExternalResilienceOptions.cs
│   │   ├── ResilienceServiceCollectionExtensions.cs
│   │   └── FallbackDelegates.cs
│   ├── Clients/
│   │   ├── ILedgerClient.cs
│   │   └── LedgerHttpClient.cs
│   ├── HealthChecks/
│   │   ├── LedgerHealthCheck.cs
│   │   └── HealthCheckExtensions.cs
│   ├── Middleware/
│   │   └── CancellationHandlingMiddleware.cs
│   └── RateLimiting/
│       └── RateLimitingExtensions.cs
├── Program.cs (registration)
└── appsettings.json
```

## Correctness Properties

1. **Retry idempotence**: Retrying a request N times with the same Idempotency-Key must produce the same final result as a single successful request (no duplicate side effects).
2. **Circuit breaker state machine**: The circuit breaker transitions follow the state machine (Closed → Open → HalfOpen → Closed/Open) and never skip states.
3. **Timeout guarantee**: No HTTP call can exceed its configured timeout; the CancellationToken fires at or before the timeout boundary.
4. **Bulkhead invariant**: The number of concurrent in-flight requests to a given client never exceeds `PermitLimit + QueueLimit`.
5. **Rate limiter fairness**: Requests within the configured rate are always permitted; requests exceeding the rate always receive HTTP 429.
6. **CancellationToken propagation**: Every async method in the call chain receives the same logical CancellationToken (or a linked derivative) — cancellation at any level propagates downward.
7. **Health check timeout**: Health check endpoint always responds within 5 seconds, even when dependencies are completely unresponsive.
8. **Fallback activation**: When the circuit breaker is open OR max retries exhausted, the fallback delegate is always invoked (never throws a raw exception to the caller).
