namespace CorePoints.FeatureToggles.Models;

public sealed record CreateFeatureFlagRequest
{
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public bool IsEnabled { get; init; } = false;
    public List<string>? TargetGroups { get; init; }
}
