using System.Net.Http.Json;

namespace CorePoints.Resilience.Clients;

/// <summary>
/// Typed client interface for Ledger Core communication.
/// All methods propagate CancellationToken for cooperative cancellation.
/// </summary>
public interface ILedgerClient
{
    /// <summary>
    /// Posts a new transaction to the Ledger.
    /// </summary>
    Task<HttpResponseMessage> PostTransactionAsync(
        object request,
        string idempotencyKey,
        string correlationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the balance for a specified account.
    /// </summary>
    Task<HttpResponseMessage> GetBalanceAsync(
        string accountId,
        string correlationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the statement for a specified account.
    /// </summary>
    Task<HttpResponseMessage> GetStatementAsync(
        string accountId,
        string correlationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a specific transaction by ID.
    /// </summary>
    Task<HttpResponseMessage> GetTransactionAsync(
        string transactionId,
        string correlationId,
        CancellationToken cancellationToken = default);
}
