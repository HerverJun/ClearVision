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

            try
            {
                return Results.Ok(store.GetSettings(runningIngressOptions.Value));
            }
            catch (StationCommunicationPersistenceException ex)
            {
                return BuildPersistenceError(ex);
            }
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

            var result = store.SaveSettings(request, runningIngressOptions.Value);
            if (!result.Success)
            {
                if (!string.IsNullOrWhiteSpace(result.ErrorCode))
                {
                    return Results.Json(result, statusCode: StatusCodes.Status503ServiceUnavailable);
                }

                return Results.BadRequest(result);
            }

            return Results.Ok(result.Settings);
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
            if (operation.Equals("reveal", StringComparison.OrdinalIgnoreCase))
            {
                var result = store.RevealToken(runningIngressOptions.Value);
                if (!result.Success)
                {
                    return Results.Json(result, statusCode: StatusCodes.Status503ServiceUnavailable);
                }

                return Results.Ok(result);
            }

            if (operation.Equals("regenerate", StringComparison.OrdinalIgnoreCase))
            {
                var result = store.RegenerateToken(runningIngressOptions.Value);
                if (!result.Success)
                {
                    if (!string.IsNullOrWhiteSpace(result.ErrorCode))
                    {
                        return Results.Json(result, statusCode: StatusCodes.Status503ServiceUnavailable);
                    }

                    return Results.BadRequest(result);
                }

                return Results.Ok(result);
            }

            return Results.BadRequest(new
            {
                success = false,
                message = "Token operation must be reveal or regenerate."
            });
        })
        .RequireClearVisionPermission(ClearVisionPermissionPolicies.RequireAdmin);

        return app;
    }

    private static IResult BuildPersistenceError(StationCommunicationPersistenceException ex) =>
        Results.Json(new
        {
            success = false,
            errorCode = ex.ErrorCode,
            publicMessage = ex.PublicMessage,
            retryable = ex.Retryable,
            stage = ex.Stage,
            metadataOnly = true
        }, statusCode: StatusCodes.Status503ServiceUnavailable);

    private static bool IsAdmin(HttpContext context) => ClearVisionPermissionPolicies.IsAdmin(context);
}
