namespace CorePoints.FeatureToggles.Models;

public sealed record UpdateFeatureFlagRequest
{
    public string? Description { get; init; }
    public bool? IsEnabled { get; init; }
    public List<string>? TargetGroups { get; init; }
}
