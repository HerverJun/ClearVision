using ClearVision.Product.Infrastructure.AI;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace ClearVision.Product.Desktop.Endpoints;

/// <summary>
/// Explicit local maintenance operations for the authoritative flow-template store.
/// </summary>
public static class TemplateMaintenanceEndpoints
{
    public static IEndpointRouteBuilder MapTemplateMaintenanceEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/settings/maintenance/templates/repair", async (
            IFlowTemplateService templateService,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (!ClearVisionPermissionPolicies.IsAdmin(context))
            {
                return Results.Forbid();
            }

            try
            {
                return Results.Ok(await templateService.RepairAsync(cancellationToken));
            }
            catch (FlowTemplateStoreException ex)
            {
                return ApiEndpoints.BuildFlowTemplateStoreError(ex);
            }
        })
        .RequireClearVisionPermission(ClearVisionPermissionPolicies.RequireAdmin);

        return app;
    }
}
