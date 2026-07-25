using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.RateLimiting;
using CorePoints.Resilience.Clients;
using CorePoints.Resilience.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Polly;
using Polly.CircuitBreaker;
using Polly.Fallback;
using Polly.Retry;
using Polly.Timeout;

namespace CorePoints.Resilience.Extensions;

/// <summary>
/// Extension methods for registering CorePoints resilience pipelines.
/// </summary>
public static class ResilienceServiceCollectionExtensions
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>
    /// Registers resilience options, named HttpClients, and resilience pipelines for Ledger and External services.
    /// </summary>
    public static IServiceCollection AddCorePointsResilience(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Bind options from configuration
        services.Configure<LedgerResilienceOptions>(
            configuration.GetSection(LedgerResilienceOptions.SectionName));
        services.Configure<ExternalResilienceOptions>(
            configuration.GetSection(ExternalResilienceOptions.SectionName));

        // Register Ledger named HttpClient with resilience pipeline
        services.AddHttpClient("LedgerClient", (sp, client) =>
        {
            var baseUrl = configuration["Services:Ledger:BaseUrl"];
            if (!string.IsNullOrEmpty(baseUrl))
            {
                client.BaseAddress = new Uri(baseUrl);
            }
            client.DefaultRequestHeaders.Add("Accept", "application/json");
        })
        .AddResilienceHandler("ledger-pipeline", (builder, context) =>
        {
            var options = context.ServiceProvider
                .GetRequiredService<IOptions<LedgerResilienceOptions>>().Value;

            // Fallback (outermost - catches all failures)
            builder.AddFallback(new FallbackStrategyOptions<HttpResponseMessage>
            {
                FallbackAction = args =>
                {
                    var correlationId = Guid.NewGuid().ToString("N");
                    var errorResponse = new
                    {
                        error = "LEDGER_UNAVAILABLE",
                        message = "Ledger service is temporarily unavailable. Please retry later.",
                        correlationId,
                        timestamp = DateTimeOffset.UtcNow
                    };

                    var response = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                    {
                        Content = JsonContent.Create(errorResponse, options: JsonOptions)
                    };
                    response.Headers.Add("X-Correlation-ID", correlationId);

                    return Outcome.FromResultAsValueTask(response);
                },
                ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
                    .Handle<BrokenCircuitException>()
                    .Handle<TimeoutRejectedException>()
                    .Handle<HttpRequestException>()
                    .HandleResult(r => r.StatusCode >= HttpStatusCode.InternalServerError)
            });

            // Concurrency Limiter (bulkhead)
            builder.AddConcurrencyLimiter(new ConcurrencyLimiterOptions
            {
                PermitLimit = options.BulkheadMaxConcurrency,
                QueueLimit = options.BulkheadQueueLimit
            });

            // Retry with exponential backoff + jitter
            builder.AddRetry(new HttpRetryStrategyOptions
            {
                MaxRetryAttempts = options.MaxRetryAttempts,
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                Delay = options.MedianFirstRetryDelay,
                ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
                    .Handle<HttpRequestException>()
                    .Handle<TimeoutRejectedException>()
                    .HandleResult(r => r.StatusCode is
                        HttpStatusCode.RequestTimeout or
                        HttpStatusCode.TooManyRequests or
                        HttpStatusCode.InternalServerError or
                        HttpStatusCode.BadGateway or
                        HttpStatusCode.ServiceUnavailable or
                        HttpStatusCode.GatewayTimeout)
            });

            // Circuit Breaker
            builder.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
            {
                FailureRatio = 0.9,
                MinimumThroughput = options.CircuitBreakerThreshold,
                SamplingDuration = options.CircuitBreakerSamplingWindow,
                BreakDuration = options.CircuitBreakerDuration,
                ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
                    .Handle<HttpRequestException>()
                    .Handle<TimeoutRejectedException>()
                    .HandleResult(r => r.StatusCode >= HttpStatusCode.InternalServerError)
            });

            // Attempt Timeout (innermost)
            builder.AddTimeout(options.AttemptTimeout);
        });

        // Register External named HttpClient with resilience pipeline
        services.AddHttpClient("ExternalClient", (sp, client) =>
        {
            var baseUrl = configuration["Services:External:BaseUrl"];
            if (!string.IsNullOrEmpty(baseUrl))
            {
                client.BaseAddress = new Uri(baseUrl);
            }
            client.DefaultRequestHeaders.Add("Accept", "application/json");
        })
        .AddResilienceHandler("external-pipeline", (builder, context) =>
        {
            var options = context.ServiceProvider
                .GetRequiredService<IOptions<ExternalResilienceOptions>>().Value;

            // Fallback (outermost)
            builder.AddFallback(new FallbackStrategyOptions<HttpResponseMessage>
            {
                FallbackAction = args =>
                {
                    var correlationId = Guid.NewGuid().ToString("N");
                    var errorResponse = new
                    {
                        error = "EXTERNAL_SERVICE_UNAVAILABLE",
                        message = "External service is temporarily unavailable. Please retry later.",
                        correlationId,
                        timestamp = DateTimeOffset.UtcNow
                    };

                    var response = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                    {
                        Content = JsonContent.Create(errorResponse, options: JsonOptions)
                    };
                    response.Headers.Add("X-Correlation-ID", correlationId);

                    return Outcome.FromResultAsValueTask(response);
                },
                ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
                    .Handle<BrokenCircuitException>()
                    .Handle<TimeoutRejectedException>()
                    .Handle<HttpRequestException>()
                    .HandleResult(r => r.StatusCode >= HttpStatusCode.InternalServerError)
            });

            // Concurrency Limiter (bulkhead)
            builder.AddConcurrencyLimiter(new ConcurrencyLimiterOptions
            {
                PermitLimit = options.BulkheadMaxConcurrency,
                QueueLimit = options.BulkheadQueueLimit
            });

            // Total Timeout (across all retries)
            builder.AddTimeout(options.TotalTimeout);

            // Retry with exponential backoff + jitter
            builder.AddRetry(new HttpRetryStrategyOptions
            {
                MaxRetryAttempts = options.MaxRetryAttempts,
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                Delay = options.MedianFirstRetryDelay,
                ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
                    .Handle<HttpRequestException>()
                    .Handle<TimeoutRejectedException>()
                    .HandleResult(r => r.StatusCode is
                        HttpStatusCode.RequestTimeout or
                        HttpStatusCode.TooManyRequests or
                        HttpStatusCode.InternalServerError or
                        HttpStatusCode.BadGateway or
                        HttpStatusCode.ServiceUnavailable or
                        HttpStatusCode.GatewayTimeout)
            });

            // Circuit Breaker
            builder.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
            {
                FailureRatio = 0.9,
                MinimumThroughput = options.CircuitBreakerThreshold,
                SamplingDuration = options.CircuitBreakerSamplingWindow,
                BreakDuration = options.CircuitBreakerDuration,
                ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
                    .Handle<HttpRequestException>()
                    .Handle<TimeoutRejectedException>()
                    .HandleResult(r => r.StatusCode >= HttpStatusCode.InternalServerError)
            });

            // Attempt Timeout (innermost)
            builder.AddTimeout(options.AttemptTimeout);
        });

        // Register typed Ledger client
        services.AddScoped<ILedgerClient, LedgerHttpClient>();

        return services;
    }
}
