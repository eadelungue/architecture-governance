using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;

namespace CorePoints.Resilience.HealthChecks;

/// <summary>
/// Health check that verifies the Ledger Core is reachable by calling its /health/live endpoint.
/// Times out after 5 seconds.
/// </summary>
public sealed class LedgerHealthCheck : IHealthCheck
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<LedgerHealthCheck> _logger;
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    public LedgerHealthCheck(IHttpClientFactory httpClientFactory, ILogger<LedgerHealthCheck> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(Timeout);

            var httpClient = _httpClientFactory.CreateClient("LedgerClient");
            var response = await httpClient.GetAsync("/health/live", cts.Token);

            if (response.IsSuccessStatusCode)
            {
                return HealthCheckResult.Healthy("Ledger Core is reachable.");
            }

            _logger.LogWarning(
                "Ledger health check returned non-success status: {StatusCode}",
                response.StatusCode);

            return HealthCheckResult.Unhealthy(
                $"Ledger Core returned HTTP {(int)response.StatusCode}.");
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Ledger health check timed out after {Timeout}s.", Timeout.TotalSeconds);
            return HealthCheckResult.Unhealthy("Ledger Core health check timed out.");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Ledger health check failed.");
            return HealthCheckResult.Unhealthy("Ledger Core is unreachable.", ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during Ledger health check.");
            return HealthCheckResult.Unhealthy("Unexpected error checking Ledger Core.", ex);
        }
    }
}
