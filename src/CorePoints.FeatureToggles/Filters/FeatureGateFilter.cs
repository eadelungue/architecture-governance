using CorePoints.FeatureToggles.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace CorePoints.FeatureToggles.Filters;

/// <summary>
/// Endpoint filter that evaluates a feature flag before allowing request processing.
/// If the flag is disabled for the current user context, returns 404 Not Found.
/// </summary>
public sealed class FeatureGateFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;

        // Read FlagName from endpoint metadata (FeatureGateAttribute)
        var endpoint = httpContext.GetEndpoint();
        var gateAttribute = endpoint?.Metadata.GetMetadata<FeatureGateAttribute>();

        if (gateAttribute is null)
        {
            // No gate attribute found, pass through
            return await next(context);
        }

        var flagName = gateAttribute.FlagName;

        // Extract userId from claims: "sub" or "user_id"
        var userId = httpContext.User?.FindFirst("sub")?.Value
                  ?? httpContext.User?.FindFirst("user_id")?.Value;

        // Extract group from claims: "group" or "cognito:groups"
        var group = httpContext.User?.FindFirst("group")?.Value
                 ?? httpContext.User?.FindFirst("cognito:groups")?.Value;

        // Resolve the service from DI
        var featureToggleService = httpContext.RequestServices.GetRequiredService<IFeatureToggleService>();

        var isEnabled = await featureToggleService.IsEnabledAsync(
            flagName,
            userId,
            group,
            httpContext.RequestAborted);

        if (!isEnabled)
        {
            return Results.NotFound();
        }

        return await next(context);
    }
}
