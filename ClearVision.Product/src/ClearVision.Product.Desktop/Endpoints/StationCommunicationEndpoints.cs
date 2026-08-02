using System;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Desktop.Middleware;
using ClearVision.Product.Desktop.Station;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;

namespace ClearVision.Product.Desktop.Endpoints;

public static class StationCommunicationEndpoints
{
    public static IEndpointRouteBuilder MapStationCommunicationEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/station-communication/settings", (
            StationCommunicationSettingsStore store,
            IOptions<StationIngressOptions> runningIngressOptions,
            HttpContext context) =>
        {
            if (!IsAdmin(context))
            {
                return Results.Json(new { error = "AdminRequired" }, statusCode: StatusCodes.Status403Forbidden);
            }

            return Results.Ok(store.GetSettings(runningIngressOptions.Value));
        })
        .RequireClearVisionPermission(ClearVisionPermissionPolicies.RequireAdmin);

        app.MapPut("/api/station-communication/settings", (
            StationCommunicationSettingsUpdateRequest request,
            StationCommunicationSettingsStore store,
            IOptions<StationIngressOptions> runningIngressOptions,
            HttpContext context) =>
        {
            if (!IsAdmin(context))
            {
                return Results.Json(new { error = "AdminRequired" }, statusCode: StatusCodes.Status403Forbidden);
            }

            try
            {
                var result = store.SaveSettings(request, runningIngressOptions.Value);
                if (!result.Success)
                {
                    return Results.BadRequest(result);
                }

                return Results.Ok(result.Settings);
            }
            catch (StationCommunicationPersistenceException ex)
            {
                return BuildPersistenceFailureResult(ex);
            }
        })
        .RequireClearVisionPermission(ClearVisionPermissionPolicies.RequireAdmin);

        app.MapPost("/api/station-communication/token", (
            StationCommunicationTokenRequest request,
            StationCommunicationSettingsStore store,
            IOptions<StationIngressOptions> runningIngressOptions,
            HttpContext context) =>
        {
            if (!IsAdmin(context))
            {
                return Results.Json(new { error = "AdminRequired" }, statusCode: StatusCodes.Status403Forbidden);
            }

            var operation = (request.Operation ?? request.Action ?? string.Empty).Trim();
            if (operation.Equals("regenerate", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var result = store.RegenerateToken(runningIngressOptions.Value);
                    if (!result.Success)
                    {
                        return Results.BadRequest(result);
                    }

                    return Results.Ok(result);
                }
                catch (StationCommunicationPersistenceException ex)
                {
                    return BuildPersistenceFailureResult(ex);
                }
            }

            return Results.BadRequest(new
            {
                success = false,
                message = "Token reveal is excluded; the only supported token operation is regenerate."
            });
        })
        .RequireClearVisionPermission(ClearVisionPermissionPolicies.RequireAdmin);

        return app;
    }

    private static bool IsAdmin(HttpContext context) => ClearVisionPermissionPolicies.IsAdmin(context);

    private static IResult BuildPersistenceFailureResult(StationCommunicationPersistenceException exception)
    {
        return Results.Json(new
        {
            errorCode = exception.DiagnosticCode,
            code = exception.DiagnosticCode,
            outcome = exception.OutcomeUnknown ? "unknown" : "unchanged",
            policy = exception.OutcomeUnknown ? "reload-before-retry" : "retry-allowed",
            publicMessage = exception.Message
        }, statusCode: exception.OutcomeUnknown
            ? StatusCodes.Status503ServiceUnavailable
            : StatusCodes.Status500InternalServerError);
    }
}
