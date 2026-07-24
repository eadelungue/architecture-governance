namespace CorePoints.FeatureToggles.Interfaces;

public interface IFeatureToggleService
{
    Task<bool> IsEnabledAsync(string flagName, string? userId = null, string? group = null, CancellationToken cancellationToken = default);
}
