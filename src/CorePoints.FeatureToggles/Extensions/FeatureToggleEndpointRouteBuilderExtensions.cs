using CorePoints.FeatureToggles.Admin;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace CorePoints.FeatureToggles.Extensions;

/// <summary>
/// Extension methods for mapping Feature Toggle Admin API routes.
/// </summary>
public static class FeatureToggleEndpointRouteBuilderExtensions
{
    /// <summary>
    /// Maps the Feature Toggle Admin API endpoints under /admin/flags.
    /// </summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <returns>A RouteGroupBuilder for further configuration.</returns>
    public static RouteGroupBuilder MapFeatureToggleAdmin(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/admin/flags")
            .WithTags("FeatureToggles");

        group.MapGet("/", FeatureToggleAdminEndpoints.GetAllFlags)
            .WithName("GetAllFeatureFlags")
            .Produces(StatusCodes.Status200OK);

        group.MapPost("/", FeatureToggleAdminEndpoints.CreateFlag)
            .WithName("CreateFeatureFlag")
            .Produces(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status400BadRequest);

        group.MapPut("/{name}", FeatureToggleAdminEndpoints.UpdateFlag)
            .WithName("UpdateFeatureFlag")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        group.MapDelete("/{name}", FeatureToggleAdminEndpoints.DeleteFlag)
            .WithName("DeleteFeatureFlag")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound);

        return group;
    }
}
