using System.Security.Claims;
using ClearVision.Product.Application.Security;
using ClearVision.Product.Core.Enums;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace ClearVision.Product.Desktop.Endpoints;

public static class ClearVisionPermissionPolicies
{
    public const string RequireAuthenticated = ClearVisionAuthorizationPolicies.RequireAuthenticated;
    public const string RequireAdmin = ClearVisionAuthorizationPolicies.RequireAdmin;
    public const string RequireEngineerOrAdmin = ClearVisionAuthorizationPolicies.RequireEngineerOrAdmin;
    public const string RequireStationAdmin = ClearVisionAuthorizationPolicies.RequireStationAdmin;
    public const string CanEditProject = ClearVisionAuthorizationPolicies.CanEditProject;
    public const string CanOperateHardware = ClearVisionAuthorizationPolicies.CanOperateHardware;
    public const string CanReadSensitiveConfig = ClearVisionAuthorizationPolicies.CanReadSensitiveConfig;
    public const string CanReadInspectionResults = ClearVisionAuthorizationPolicies.CanReadInspectionResults;

    public static bool Authorize(HttpContext context, string policy, out ClearVisionPermissionDenial denial)
    {
        denial = default;
        var role = EndpointPermissionGuards.GetCurrentRole(context);
        if (string.IsNullOrWhiteSpace(role))
        {
            denial = Deny(policy, "AuthenticatedSessionRequired");
            return false;
        }

        var allowed = ClearVisionAuthorizationPolicies.IsAllowed(role, policy);

        if (allowed)
        {
            return true;
        }

        denial = Deny(policy, ErrorCodeFor(policy));
        return false;
    }

    public static string ErrorCodeFor(string policy) => policy switch
    {
        RequireAdmin => "AdminRequired",
        RequireEngineerOrAdmin => "EngineerOrAdminRequired",
        RequireStationAdmin => "StationAdminRequired",
        CanEditProject => "ProjectEditPermissionRequired",
        CanOperateHardware => "HardwareOperationPermissionRequired",
        CanReadSensitiveConfig => "SensitiveConfigPermissionRequired",
        CanReadInspectionResults => "InspectionResultReadPermissionRequired",
        _ => "PermissionRequired"
    };

    public static bool IsAdmin(HttpContext context) => IsAdmin(EndpointPermissionGuards.GetCurrentRole(context));

    public static bool IsEngineerOrAdmin(HttpContext context) =>
        IsEngineerOrAdmin(EndpointPermissionGuards.GetCurrentRole(context));

    private static bool IsAdmin(string? role) =>
        string.Equals(role, UserRole.Admin.ToString(), StringComparison.Ordinal);

    private static bool IsEngineerOrAdmin(string? role) =>
        IsAdmin(role) ||
        string.Equals(role, UserRole.Engineer.ToString(), StringComparison.Ordinal);

    private static ClearVisionPermissionDenial Deny(string policy, string code) =>
        new(policy, code);
}

public readonly record struct ClearVisionPermissionDenial(string Policy, string Code);

public static class EndpointPermissionGuards
{
    public static RouteHandlerBuilder RequireClearVisionPermission(
        this RouteHandlerBuilder builder,
        string policy)
    {
        return builder.AddEndpointFilter(async (context, next) =>
        {
            if (!ClearVisionPermissionPolicies.Authorize(context.HttpContext, policy, out var denial))
            {
                return Results.Json(
                    new
                    {
                        error = denial.Code,
                        code = denial.Code,
                        policy = denial.Policy
                    },
                    statusCode: StatusCodes.Status403Forbidden);
            }

            return await next(context);
        });
    }

    public static string? GetCurrentRole(HttpContext context)
    {
        if (context.Items.TryGetValue("CurrentUser", out var userObj))
        {
            var role = userObj switch
            {
                ClearVision.Product.Application.Services.UserSession user => user.Role,
                ClearVision.Product.Desktop.Middleware.UserSession user => user.Role,
                _ => null
            };

            if (!string.IsNullOrWhiteSpace(role))
            {
                return role.Trim();
            }
        }

        var claimRole = context.User?.FindFirstValue(ClaimTypes.Role);
        return string.IsNullOrWhiteSpace(claimRole) ? null : claimRole.Trim();
    }

    public static string GetAuthenticatedUserName(HttpContext context)
    {
        if (context.Items.TryGetValue("CurrentUser", out var userObj))
        {
            var userName = userObj switch
            {
                ClearVision.Product.Application.Services.UserSession user => user.Username,
                ClearVision.Product.Desktop.Middleware.UserSession user => user.Username,
                _ => null
            };

            if (!string.IsNullOrWhiteSpace(userName))
            {
                return userName.Trim();
            }
        }

        var principalName = context.User?.Identity?.Name;
        return string.IsNullOrWhiteSpace(principalName) ? string.Empty : principalName.Trim();
    }
}
