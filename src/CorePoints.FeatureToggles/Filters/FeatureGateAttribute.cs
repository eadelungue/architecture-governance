namespace CorePoints.FeatureToggles.Filters;

/// <summary>
/// Marks an endpoint as gated behind a specific feature flag.
/// The FeatureGateFilter reads this attribute to determine which flag to evaluate.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false)]
public sealed class FeatureGateAttribute : Attribute
{
    public string FlagName { get; }

    public FeatureGateAttribute(string flagName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(flagName);
        FlagName = flagName;
    }
}
