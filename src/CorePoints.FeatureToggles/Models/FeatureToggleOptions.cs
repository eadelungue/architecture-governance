namespace CorePoints.FeatureToggles.Models;

public sealed class FeatureToggleOptions
{
    public TimeSpan CacheTtl { get; init; } = TimeSpan.FromSeconds(60);
    public string ConnectionString { get; init; } = string.Empty;
}
