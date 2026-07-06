using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Interfaces;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace ClearVision.Product.Desktop.Endpoints;

public static class TcpEndpoints
{
    public static IEndpointRouteBuilder MapTcpEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/tcp/profiles", async (
            ITcpDeviceManager tcpDeviceManager,
            CancellationToken cancellationToken) =>
        {
            var config = await tcpDeviceManager.GetConfigAsync(cancellationToken);
            return Results.Ok(new
            {
                success = true,
                profiles = config.Profiles
            });
        })
        .RequireClearVisionPermission(ClearVisionPermissionPolicies.CanOperateHardware);

        app.MapPut("/api/tcp/profiles", async (
            [FromBody] TcpCommunicationProfile[]? profiles,
            ITcpDeviceManager tcpDeviceManager,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (!ClearVisionPermissionPolicies.IsAdmin(context))
            {
                return Results.Json(new { error = "AdminRequired" }, statusCode: StatusCodes.Status403Forbidden);
            }

            var config = new TcpCommunicationConfig
            {
                Profiles = profiles?.ToList() ?? new List<TcpCommunicationProfile>()
            };
            config.Normalize();

            var validation = TcpCommunicationConfigValidator.Validate(config);
            if (!validation.IsValid)
            {
                return Results.Ok(new
                {
                    success = false,
                    message = "TCP Profile 校验失败。",
                    profiles = config.Profiles,
                    errors = validation.Errors
                });
            }

            var saved = await tcpDeviceManager.SaveConfigAsync(config, cancellationToken);
            return Results.Ok(new
            {
                success = true,
                message = "TCP Profile 已保存。",
                profiles = saved.Profiles,
                errors = Array.Empty<TcpCommunicationValidationIssue>()
            });
        })
        .RequireClearVisionPermission(ClearVisionPermissionPolicies.RequireAdmin);

        app.MapPost("/api/tcp/profiles/{id}/connect", async (
            string id,
            ITcpDeviceManager tcpDeviceManager,
            CancellationToken cancellationToken) =>
        {
            return ToResult(await tcpDeviceManager.ConnectAsync(id, cancellationToken));
        })
        .RequireClearVisionPermission(ClearVisionPermissionPolicies.CanOperateHardware);

        app.MapPost("/api/tcp/profiles/{id}/disconnect", async (
            string id,
            ITcpDeviceManager tcpDeviceManager,
            CancellationToken cancellationToken) =>
        {
            return ToResult(await tcpDeviceManager.DisconnectAsync(id, cancellationToken));
        })
        .RequireClearVisionPermission(ClearVisionPermissionPolicies.CanOperateHardware);

        app.MapPost("/api/tcp/profiles/{id}/send", async (
            string id,
            [FromBody] TcpSendEndpointRequest? request,
            ITcpDeviceManager tcpDeviceManager,
            CancellationToken cancellationToken) =>
        {
            var sendRequest = new TcpDeviceSendRequest(
                request?.Payload ?? string.Empty,
                request?.IsHex == true,
                request?.WaitResponse ?? true,
                request?.ResponseTimeoutMs);
            return ToResult(await tcpDeviceManager.SendAsync(id, sendRequest, cancellationToken));
        })
        .RequireClearVisionPermission(ClearVisionPermissionPolicies.CanOperateHardware);

        app.MapPost("/api/tcp/profiles/{id}/server/start", async (
            string id,
            ITcpDeviceManager tcpDeviceManager,
            CancellationToken cancellationToken) =>
        {
            return ToResult(await tcpDeviceManager.StartServerAsync(id, cancellationToken));
        })
        .RequireClearVisionPermission(ClearVisionPermissionPolicies.CanOperateHardware);

        app.MapPost("/api/tcp/profiles/{id}/server/stop", async (
            string id,
            ITcpDeviceManager tcpDeviceManager,
            CancellationToken cancellationToken) =>
        {
            return ToResult(await tcpDeviceManager.StopServerAsync(id, cancellationToken));
        })
        .RequireClearVisionPermission(ClearVisionPermissionPolicies.CanOperateHardware);

        app.MapGet("/api/tcp/profiles/{id}/status", async (
            string id,
            ITcpDeviceManager tcpDeviceManager,
            CancellationToken cancellationToken) =>
        {
            return Results.Ok(new
            {
                success = true,
                status = await tcpDeviceManager.GetStatusAsync(id, cancellationToken)
            });
        })
        .RequireClearVisionPermission(ClearVisionPermissionPolicies.CanOperateHardware);

        app.MapGet("/api/tcp/profiles/{id}/frames", async (
            string id,
            ITcpDeviceManager tcpDeviceManager,
            CancellationToken cancellationToken) =>
        {
            return Results.Ok(new
            {
                success = true,
                frames = await tcpDeviceManager.GetFramesAsync(id, cancellationToken)
            });
        })
        .RequireClearVisionPermission(ClearVisionPermissionPolicies.CanOperateHardware);

        app.MapPost("/api/tcp/profiles/{id}/frames/clear", async (
            string id,
            ITcpDeviceManager tcpDeviceManager,
            CancellationToken cancellationToken) =>
        {
            await tcpDeviceManager.ClearFramesAsync(id, cancellationToken);
            return Results.Ok(new
            {
                success = true,
                message = "TCP 收发日志已清空。"
            });
        })
        .RequireClearVisionPermission(ClearVisionPermissionPolicies.CanOperateHardware);

        return app;
    }

    private static IResult ToResult(TcpDeviceOperationResult result)
    {
        var payload = new
        {
            success = result.Success,
            message = result.Message,
            status = result.Status,
            errors = result.Errors ?? Array.Empty<TcpCommunicationValidationIssue>()
        };

        return result.Success
            ? Results.Ok(payload)
            : Results.BadRequest(payload);
    }

    private static IResult ToResult(TcpDeviceSendResult result)
    {
        var payload = new
        {
            success = result.Success,
            message = result.Message,
            response = result.Response,
            status = result.Status,
            errors = result.Errors ?? Array.Empty<TcpCommunicationValidationIssue>()
        };

        return result.Success
            ? Results.Ok(payload)
            : Results.BadRequest(payload);
    }
}

public sealed class TcpSendEndpointRequest
{
    public string? Payload { get; set; }

    public bool IsHex { get; set; }

    public bool WaitResponse { get; set; } = true;

    public int? ResponseTimeoutMs { get; set; }
}
