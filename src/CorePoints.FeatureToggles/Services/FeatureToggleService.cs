using CorePoints.FeatureToggles.Interfaces;
using CorePoints.FeatureToggles.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CorePoints.FeatureToggles.Services;

public sealed class FeatureToggleService : IFeatureToggleService
{
    private readonly IFeatureFlagRepository _repository;
    private readonly IMemoryCache _cache;
    private readonly ILogger<FeatureToggleService> _logger;
    private readonly FeatureToggleOptions _options;

    private const string CacheKeyPrefix = "feature_flag:";

    public FeatureToggleService(
        IFeatureFlagRepository repository,
        IMemoryCache cache,
        ILogger<FeatureToggleService> logger,
        IOptions<FeatureToggleOptions> options)
    {
        _repository = repository;
        _cache = cache;
        _logger = logger;
        _options = options.Value;
    }

    public async Task<bool> IsEnabledAsync(
        string flagName,
        string? userId = null,
        string? group = null,
        CancellationToken cancellationToken = default)
    {
        var cacheKey = $"{CacheKeyPrefix}{flagName}";
        FeatureFlag? flag;

        // 1. Try cache (on cache error, fall through to repository)
        try
        {
            if (_cache.TryGetValue(cacheKey, out FeatureFlag? cachedFlag))
            {
                flag = cachedFlag;
                return Evaluate(flag, group);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cache error while reading flag '{FlagName}'. Falling through to repository.", flagName);
        }

        // 2. On cache miss (or cache error), query repository
        try
        {
            flag = await _repository.GetByNameAsync(flagName, cancellationToken);
        }
        catch (Exception ex)
        {
            // Fail-closed: on any repository error, return false and log
            _logger.LogError(ex, "Repository error while reading flag '{FlagName}'. Returning false (fail-closed).", flagName);
            return false;
        }

        // 3. Store in cache with TTL (best effort, ignore cache write errors)
        if (flag is not null)
        {
            try
            {
                var cacheOptions = new MemoryCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = _options.CacheTtl
                };
                _cache.Set(cacheKey, flag, cacheOptions);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to store flag '{FlagName}' in cache.", flagName);
            }
        }

        // 4. Evaluate
        return Evaluate(flag, group);
    }

    private static bool Evaluate(FeatureFlag? flag, string? group)
    {
        // Flag not found → false
        if (flag is null)
            return false;

        // Flag disabled → false
        if (!flag.IsEnabled)
            return false;

        // Flag enabled + no target groups → true (global enable)
        if (flag.TargetGroups is null || flag.TargetGroups.Count == 0)
            return true;

        // Flag enabled + target groups → match group (case-insensitive)
        if (string.IsNullOrEmpty(group))
            return false;

        return flag.TargetGroups.Any(tg =>
            string.Equals(tg, group, StringComparison.OrdinalIgnoreCase));
    }
}
