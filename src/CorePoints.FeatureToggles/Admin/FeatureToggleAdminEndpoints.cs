using CorePoints.FeatureToggles.Interfaces;
using CorePoints.FeatureToggles.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;

namespace CorePoints.FeatureToggles.Admin;

/// <summary>
/// Defines the Admin API endpoints for feature flag management.
/// Uses minimal API typed results for RFC 7807 Problem Details compliance.
/// </summary>
public static class FeatureToggleAdminEndpoints
{
    private const string CacheKeyPrefix = "feature_flag:";

    /// <summary>
    /// GET /admin/flags → Returns all feature flags (HTTP 200).
    /// </summary>
    public static async Task<Ok<IReadOnlyList<FeatureFlag>>> GetAllFlags(
        IFeatureFlagRepository repository,
        CancellationToken cancellationToken)
    {
        var flags = await repository.GetAllAsync(cancellationToken);
        return TypedResults.Ok(flags);
    }

    /// <summary>
    /// POST /admin/flags → Creates a new feature flag (HTTP 201 or 409 on duplicate).
    /// </summary>
    public static async Task<Results<Created<FeatureFlag>, Conflict<ProblemDetails>, ValidationProblem>> CreateFlag(
        CreateFeatureFlagRequest request,
        IFeatureFlagRepository repository,
        IMemoryCache cache,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            var errors = new Dictionary<string, string[]>
            {
                { "name", new[] { "The 'name' field is required." } }
            };
            return TypedResults.ValidationProblem(errors);
        }

        try
        {
            var flag = await repository.CreateAsync(request, cancellationToken);

            // Invalidate cache for the new flag
            cache.Remove($"{CacheKeyPrefix}{flag.Name}");

            return TypedResults.Created($"/admin/flags/{flag.Name}", flag);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("already exists"))
        {
            var problem = new ProblemDetails
            {
                Type = "https://tools.ietf.org/html/rfc7807",
                Title = "Conflict",
                Status = StatusCodes.Status409Conflict,
                Detail = ex.Message
            };
            return TypedResults.Conflict(problem);
        }
    }

    /// <summary>
    /// PUT /admin/flags/{name} → Updates an existing feature flag (HTTP 200 or 404).
    /// </summary>
    public static async Task<Results<Ok<FeatureFlag>, NotFound<ProblemDetails>>> UpdateFlag(
        string name,
        UpdateFeatureFlagRequest request,
        IFeatureFlagRepository repository,
        IMemoryCache cache,
        CancellationToken cancellationToken)
    {
        var updated = await repository.UpdateAsync(name, request, cancellationToken);

        if (updated is null)
        {
            var problem = new ProblemDetails
            {
                Type = "https://tools.ietf.org/html/rfc7807",
                Title = "Not Found",
                Status = StatusCodes.Status404NotFound,
                Detail = $"Feature flag '{name}' not found."
            };
            return TypedResults.NotFound(problem);
        }

        // Invalidate cache for the updated flag
        cache.Remove($"{CacheKeyPrefix}{name}");

        return TypedResults.Ok(updated);
    }

    /// <summary>
    /// DELETE /admin/flags/{name} → Deletes a feature flag (HTTP 204 or 404).
    /// </summary>
    public static async Task<Results<NoContent, NotFound<ProblemDetails>>> DeleteFlag(
        string name,
        IFeatureFlagRepository repository,
        IMemoryCache cache,
        CancellationToken cancellationToken)
    {
        var deleted = await repository.DeleteAsync(name, cancellationToken);

        if (!deleted)
        {
            var problem = new ProblemDetails
            {
                Type = "https://tools.ietf.org/html/rfc7807",
                Title = "Not Found",
                Status = StatusCodes.Status404NotFound,
                Detail = $"Feature flag '{name}' not found."
            };
            return TypedResults.NotFound(problem);
        }

        // Invalidate cache for the deleted flag
        cache.Remove($"{CacheKeyPrefix}{name}");

        return TypedResults.NoContent();
    }
}
