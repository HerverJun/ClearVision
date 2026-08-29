// UserEndpoints.cs
// 重置密码请求
// 作者：蘅芜君

using ClearVision.Product.Application.DTOs;
using ClearVision.Product.Application.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace ClearVision.Product.Desktop.Endpoints;

/// <summary>
/// 用户管理 API 端点 - 仅 Admin 可访问
/// </summary>
public static class UserEndpoints
{
    public static IEndpointRouteBuilder MapUserEndpoints(this IEndpointRouteBuilder app)
    {
        // 获取所有用户 - Admin
        app.MapGet("/api/users", async (UserManagementService userService) =>
        {
            var users = await userService.GetAllUsersAsync();
            return Results.Ok(users);
        })
        .RequireClearVisionPermission(ClearVisionPermissionPolicies.RequireAdmin);

        // 获取单个用户 - Admin
        app.MapGet("/api/users/{id}", async (string id, UserManagementService userService) =>
        {
            var user = await userService.GetUserByIdAsync(id);
            if (user == null)
            {
                return Results.NotFound(new
                {
                    code = UserManagementErrorCodes.UserNotFound,
                    error = $"用户 {id} 不存在"
                });
            }

            return Results.Ok(user);
        })
        .RequireClearVisionPermission(ClearVisionPermissionPolicies.RequireAdmin);

        // 创建用户 - Admin
        app.MapPost("/api/users", async (CreateUserRequest request, UserManagementService userService) =>
        {
            var result = await userService.CreateUserAsync(request);

            if (!result.Success)
            {
                return ToErrorResult(result);
            }

            return Results.Created($"/api/users/{result.User!.Id}", result.User);
        })
        .RequireClearVisionPermission(ClearVisionPermissionPolicies.RequireAdmin);

        // 更新用户 - Admin
        app.MapPut("/api/users/{id}", async (
            string id,
            UpdateUserRequest request,
            UserManagementService userService) =>
        {
            var result = await userService.UpdateUserAsync(id, request);

            if (!result.Success)
            {
                return ToErrorResult(result);
            }

            return Results.Ok(result.User);
        })
        .RequireClearVisionPermission(ClearVisionPermissionPolicies.RequireAdmin);

        // 删除用户 - Admin
        app.MapDelete("/api/users/{id}", async (string id, UserManagementService userService) =>
        {
            var result = await userService.DeleteUserAsync(id);

            if (!result.Success)
            {
                return ToErrorResult(result);
            }

            return Results.NoContent();
        })
        .RequireClearVisionPermission(ClearVisionPermissionPolicies.RequireAdmin);

        // 重置密码 - Admin
        app.MapPost("/api/users/{id}/reset-password", async (
            string id,
            ResetPasswordRequest request,
            UserManagementService userService) =>
        {
            var result = await userService.ResetPasswordAsync(id, request.NewPassword);

            if (!result.Success)
            {
                return ToErrorResult(result);
            }

            return Results.Ok(new { Message = "密码重置成功" });
        })
        .RequireClearVisionPermission(ClearVisionPermissionPolicies.RequireAdmin);

        return app;
    }

    private static IResult ToErrorResult(UserResult result)
    {
        var payload = new
        {
            code = result.ErrorCode ?? UserManagementErrorCodes.ValidationError,
            error = result.ErrorMessage ?? "用户操作失败"
        };

        return result.ErrorCode switch
        {
            UserManagementErrorCodes.UserNotFound => Results.NotFound(payload),
            UserManagementErrorCodes.LastActiveAdmin or
                UserManagementErrorCodes.RevisionConflict or
                UserManagementErrorCodes.UsernameConflict => Results.Conflict(payload),
            _ => Results.UnprocessableEntity(payload)
        };
    }
}

/// <summary>
/// 重置密码请求
/// </summary>
public class ResetPasswordRequest
{
    public string NewPassword { get; set; } = string.Empty;
}
