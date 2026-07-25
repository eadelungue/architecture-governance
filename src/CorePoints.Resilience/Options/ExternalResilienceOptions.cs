namespace CorePoints.Resilience.Options;

/// <summary>
/// Configuration options for the External client resilience pipeline.
/// Bound to the "Resilience:External" configuration section.
/// </summary>
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
