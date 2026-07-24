using CorePoints.FeatureToggles.Models;

namespace CorePoints.FeatureToggles.Interfaces;

public interface IFeatureFlagRepository
{
    Task<FeatureFlag?> GetByNameAsync(string name, CancellationToken cancellationToken);
    Task<IReadOnlyList<FeatureFlag>> GetAllAsync(CancellationToken cancellationToken);
    Task<FeatureFlag> CreateAsync(CreateFeatureFlagRequest request, CancellationToken cancellationToken);
    Task<FeatureFlag?> UpdateAsync(string name, UpdateFeatureFlagRequest request, CancellationToken cancellationToken);
    Task<bool> DeleteAsync(string name, CancellationToken cancellationToken);
}
