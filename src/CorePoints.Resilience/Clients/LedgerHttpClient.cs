using System.Net.Http.Json;
using Microsoft.Extensions.Logging;

namespace CorePoints.Resilience.Clients;

/// <summary>
/// Typed Ledger client that resolves the "LedgerClient" named HttpClient from IHttpClientFactory.
/// Attaches Idempotency-Key and X-Correlation-ID headers on every request.
/// Propagates CancellationToken through all async calls.
/// </summary>
public sealed class LedgerHttpClient : ILedgerClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<LedgerHttpClient> _logger;

    public LedgerHttpClient(IHttpClientFactory httpClientFactory, ILogger<LedgerHttpClient> logger)
    {
        _httpClient = httpClientFactory.CreateClient("LedgerClient");
        _logger = logger;
    }

    public async Task<HttpResponseMessage> PostTransactionAsync(
        object request,
        string idempotencyKey,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "/transactions")
        {
            Content = JsonContent.Create(request)
        };
        AttachHeaders(httpRequest, idempotencyKey, correlationId);

        _logger.LogDebug(
            "Posting transaction to Ledger. IdempotencyKey={IdempotencyKey}, CorrelationId={CorrelationId}",
            idempotencyKey, correlationId);

        return await _httpClient.SendAsync(httpRequest, cancellationToken);
    }

    public async Task<HttpResponseMessage> GetBalanceAsync(
        string accountId,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        using var httpRequest = new HttpRequestMessage(HttpMethod.Get, $"/accounts/{accountId}/balance");
        AttachHeaders(httpRequest, idempotencyKey: null, correlationId);

        _logger.LogDebug(
            "Getting balance from Ledger. AccountId={AccountId}, CorrelationId={CorrelationId}",
            accountId, correlationId);

        return await _httpClient.SendAsync(httpRequest, cancellationToken);
    }

    public async Task<HttpResponseMessage> GetStatementAsync(
        string accountId,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        using var httpRequest = new HttpRequestMessage(HttpMethod.Get, $"/accounts/{accountId}/statement");
        AttachHeaders(httpRequest, idempotencyKey: null, correlationId);

        _logger.LogDebug(
            "Getting statement from Ledger. AccountId={AccountId}, CorrelationId={CorrelationId}",
            accountId, correlationId);

        return await _httpClient.SendAsync(httpRequest, cancellationToken);
    }

    public async Task<HttpResponseMessage> GetTransactionAsync(
        string transactionId,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        using var httpRequest = new HttpRequestMessage(HttpMethod.Get, $"/transactions/{transactionId}");
        AttachHeaders(httpRequest, idempotencyKey: null, correlationId);

        _logger.LogDebug(
            "Getting transaction from Ledger. TransactionId={TransactionId}, CorrelationId={CorrelationId}",
            transactionId, correlationId);

        return await _httpClient.SendAsync(httpRequest, cancellationToken);
    }

    private static void AttachHeaders(HttpRequestMessage request, string? idempotencyKey, string correlationId)
    {
        if (!string.IsNullOrEmpty(idempotencyKey))
        {
            request.Headers.Add("Idempotency-Key", idempotencyKey);
        }
        request.Headers.Add("X-Correlation-ID", correlationId);
    }
}
