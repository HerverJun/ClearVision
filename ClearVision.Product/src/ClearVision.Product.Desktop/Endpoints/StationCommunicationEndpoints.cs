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
        });

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
                return Results.BadRequest(result);
            }

            return Results.Ok(result.Settings);
        });

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
                return Results.Ok(store.RevealToken(runningIngressOptions.Value));
            }

            if (operation.Equals("regenerate", StringComparison.OrdinalIgnoreCase))
            {
                var result = store.RegenerateToken(runningIngressOptions.Value);
                if (!result.Success)
                {
                    return Results.BadRequest(result);
                }

                return Results.Ok(result);
            }

            return Results.BadRequest(new
            {
                success = false,
                message = "Token operation must be reveal or regenerate."
            });
        });

        return app;
    }

    private static bool IsAdmin(HttpContext context)
    {
        if (!context.Items.TryGetValue("CurrentUser", out var userObj))
        {
            return false;
        }

        var role = userObj switch
        {
            ClearVision.Product.Application.Services.UserSession user => user.Role,
            UserSession user => user.Role,
            _ => null
        };

        return string.Equals(role, UserRole.Admin.ToString(), StringComparison.Ordinal);
    }
}
