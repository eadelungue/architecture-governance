namespace CorePoints.Resilience.Options;

/// <summary>
/// Configuration options for the Ledger resilience pipeline.
/// Bound to the "Resilience:Ledger" configuration section.
/// </summary>
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
