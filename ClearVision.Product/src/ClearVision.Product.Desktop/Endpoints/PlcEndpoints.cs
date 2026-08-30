using System.Net.Sockets;
using ClearVision.PlcComm;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Interfaces;
using ClearVision.Product.Desktop.Middleware;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace ClearVision.Product.Desktop.Endpoints;

public static class PlcEndpoints
{
    public static IEndpointRouteBuilder MapPlcEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/plc/settings", async (IConfigurationService configService) =>
        {
            var read = await configService.ReadAsync();
            if (!read.IsHealthy || read.Config == null)
            {
                return AppConfigEndpointResults.ReadFailure(read, config => new
                {
                    settings = config.Communication,
                    revision = config.Revision
                });
            }

            var config = read.Config;
            config.Normalize();
            return Results.Ok(new
            {
                success = true,
                settings = config.Communication,
                revision = config.Revision
            });
        })
        .RequireClearVisionPermission(ClearVisionPermissionPolicies.CanOperateHardware);

        app.MapPut("/api/plc/settings", async (
            PlcSettingsUpdateRequest? settings,
            IConfigurationService configService,
            HttpContext context) =>
        {
            if (!IsAdmin(context))
            {
                return Results.Json(new { error = "AdminRequired" }, statusCode: StatusCodes.Status403Forbidden);
            }

            settings ??= new PlcSettingsUpdateRequest();
            if (!settings.ExpectedRevision.HasValue)
            {
                return AppConfigEndpointResults.ExpectedRevisionFailure();
            }

            settings.Normalize();

            var validation = PlcSettingsValidator.Validate(settings);
            if (!validation.IsValid)
            {
                return Results.Json(new
                {
                    success = false,
                    errorCode = "APP_CONFIG_VALIDATION_FAILED",
                    message = "PLC 配置校验失败。",
                    settings,
                    errors = validation.Errors
                }, statusCode: StatusCodes.Status422UnprocessableEntity);
            }

            var communication = CloneCommunication(settings);
            var mutation = await configService.MutateAsync(
                settings.ExpectedRevision.Value,
                candidate => candidate.Communication = CloneCommunication(communication),
                candidate => PlcSettingsValidator.Validate(candidate.Communication).Errors
                    .Select(issue => new AppConfigValidationError(
                        $"communication.{issue.Protocol}.{issue.Section}.{issue.Field}",
                        issue.Message))
                    .ToArray(),
                context.RequestAborted);
            if (!mutation.IsSuccess)
            {
                return AppConfigEndpointResults.MutationFailure(mutation);
            }

            return Results.Ok(new
            {
                success = true,
                message = mutation.IsNoOp ? "PLC 配置未发生变化。" : "PLC 配置已保存。",
                settings = mutation.Config!.Communication,
                revision = mutation.ActualRevision,
                noOp = mutation.IsNoOp,
                errors = Array.Empty<PlcValidationIssue>()
            });
        })
        .RequireClearVisionPermission(ClearVisionPermissionPolicies.RequireAdmin);

        app.MapPost("/api/plc/test-connection", async (
            PlcTestConnectionRequest request,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            if (!TryNormalizeSupportedProtocol(request.Protocol, out var protocol))
            {
                return Results.BadRequest(new
                {
                    success = false,
                    message = "仅支持 S7、MC、FINS 连接测试。"
                });
            }

            var ipAddress = (request.IpAddress ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(ipAddress))
            {
                return Results.BadRequest(new
                {
                    success = false,
                    message = "PLC IP 地址不能为空。"
                });
            }

            if (request.Port <= 0 || request.Port > 65535)
            {
                return Results.BadRequest(new
                {
                    success = false,
                    message = "端口必须在 1-65535 之间。"
                });
            }

            if (!TryBuildPlcCommConnectionString(protocol, request, out var connectionString, out var badRequestMessage))
            {
                return Results.BadRequest(new
                {
                    success = false,
                    message = badRequestMessage
                });
            }

            try
            {
                var logger = loggerFactory.CreateLogger("PlcConnectionTest");
                using var client = PlcClientFactory.CreateFromConnectionString(connectionString, logger);
                var connected = await client.ConnectAsync(ct);
                var pingOk = connected && await client.PingAsync(ct);

                if (connected)
                {
                    await SafeDisconnectAsync(client);
                }

                return Results.Ok(new
                {
                    success = pingOk,
                    message = pingOk ? "连接成功。" : "连接失败。",
                    protocol
                });
            }
            catch (SocketException ex)
            {
                return Results.Ok(new
                {
                    success = false,
                    message = $"连接失败: {ex.Message}",
                    protocol
                });
            }
            catch (Exception ex)
            {
                return Results.Ok(new
                {
                    success = false,
                    message = $"连接测试失败: {ex.Message}",
                    protocol
                });
            }
        })
        .RequireClearVisionPermission(ClearVisionPermissionPolicies.CanOperateHardware);

        app.MapGet("/api/plc/mappings", async (IConfigurationService configService) =>
        {
            var read = await configService.ReadAsync();
            if (!read.IsHealthy || read.Config == null)
            {
                return AppConfigEndpointResults.ReadFailure(read);
            }

            var config = read.Config;
            config.Normalize();
            return Results.Ok(config.Communication.GetMappings(config.Communication.ActiveProtocol));
        })
        .RequireClearVisionPermission(ClearVisionPermissionPolicies.CanOperateHardware);

        app.MapPut("/api/plc/mappings", async (
            PlcMappingsUpdateRequest request,
            IConfigurationService configService,
            HttpContext context) =>
        {
            if (!IsAdmin(context))
            {
                return Results.Json(new { error = "AdminRequired" }, statusCode: StatusCodes.Status403Forbidden);
            }

            if (!request.ExpectedRevision.HasValue)
            {
                return AppConfigEndpointResults.ExpectedRevisionFailure();
            }

            var mutation = await configService.MutateAsync(
                request.ExpectedRevision.Value,
                candidate => candidate.Communication.SetMappings(
                    candidate.Communication.ActiveProtocol,
                    request.Mappings),
                candidate => PlcSettingsValidator.Validate(candidate.Communication).Errors
                    .Select(issue => new AppConfigValidationError(
                        $"communication.{issue.Protocol}.{issue.Section}.{issue.Field}",
                        issue.Message))
                    .ToArray(),
                context.RequestAborted);
            if (!mutation.IsSuccess)
            {
                return AppConfigEndpointResults.MutationFailure(mutation);
            }

            return Results.Ok(new
            {
                success = true,
                message = mutation.IsNoOp ? "PLC 映射未发生变化。" : "PLC 映射已保存。",
                mappings = mutation.Config!.Communication.GetMappings(mutation.Config.Communication.ActiveProtocol),
                revision = mutation.ActualRevision,
                noOp = mutation.IsNoOp,
                errors = Array.Empty<PlcValidationIssue>()
            });
        })
        .RequireClearVisionPermission(ClearVisionPermissionPolicies.RequireAdmin);

        return app;
    }

    private static async Task SafeDisconnectAsync(ClearVision.PlcComm.Interfaces.IPlcClient client)
    {
        try
        {
            await client.DisconnectAsync();
        }
        catch
        {
            // Ignore disconnect errors in test endpoint.
        }
    }

    private static bool IsAdmin(HttpContext context) => ClearVisionPermissionPolicies.IsAdmin(context);

    private static CommunicationConfig CloneCommunication(CommunicationConfig source)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(source);
        var clone = System.Text.Json.JsonSerializer.Deserialize<CommunicationConfig>(json) ?? new CommunicationConfig();
        clone.Normalize();
        return clone;
    }

    private static bool TryBuildPlcCommConnectionString(
        string protocol,
        PlcTestConnectionRequest request,
        out string connectionString,
        out string errorMessage)
    {
        connectionString = string.Empty;
        errorMessage = string.Empty;
        var ipAddress = (request.IpAddress ?? string.Empty).Trim();
        var port = request.Port;

        switch (protocol)
        {
            case CommunicationConfig.ProtocolS7:
            {
                var cpu = string.IsNullOrWhiteSpace(request.CpuType) ? "S7-1200" : request.CpuType!.Trim();
                var rack = request.Rack ?? 0;
                var slot = request.Slot ?? 1;
                if (rack < 0 || rack > 15)
                {
                    errorMessage = "Rack 必须在 0-15 之间。";
                    return false;
                }

                if (slot < 0 || slot > 15)
                {
                    errorMessage = "Slot 必须在 0-15 之间。";
                    return false;
                }

                connectionString = $"S7://{ipAddress}:{port}?cpu={Uri.EscapeDataString(cpu)}&rack={rack}&slot={slot}";
                return true;
            }
            case CommunicationConfig.ProtocolMc:
                connectionString = $"MC://{ipAddress}:{port}";
                return true;
            case CommunicationConfig.ProtocolFins:
                connectionString = $"FINS://{ipAddress}:{port}";
                return true;
            default:
                errorMessage = "仅支持 S7、MC、FINS 协议。";
                return false;
        }
    }

    private static bool TryNormalizeSupportedProtocol(string? protocol, out string normalizedProtocol)
    {
        var rawProtocol = (protocol ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(rawProtocol))
        {
            normalizedProtocol = CommunicationConfig.ProtocolS7;
            return true;
        }

        normalizedProtocol = CommunicationConfig.NormalizeProtocolKey(rawProtocol);
        return rawProtocol.Equals(normalizedProtocol, StringComparison.OrdinalIgnoreCase)
            || (normalizedProtocol == CommunicationConfig.ProtocolS7 && rawProtocol.Equals("SiemensS7", StringComparison.OrdinalIgnoreCase))
            || (normalizedProtocol == CommunicationConfig.ProtocolMc && rawProtocol.Equals("MitsubishiMc", StringComparison.OrdinalIgnoreCase))
            || (normalizedProtocol == CommunicationConfig.ProtocolFins && rawProtocol.Equals("OmronFins", StringComparison.OrdinalIgnoreCase));
    }
}

public class PlcTestConnectionRequest
{
    public string? Protocol { get; set; }
    public string? IpAddress { get; set; }
    public int Port { get; set; } = 102;
    public string? CpuType { get; set; }
    public int? Rack { get; set; }
    public int? Slot { get; set; }
}

public sealed class PlcSettingsUpdateRequest : CommunicationConfig
{
    public long? ExpectedRevision { get; set; }
}

public sealed class PlcMappingsUpdateRequest
{
    public long? ExpectedRevision { get; set; }
    public List<PlcAddressMapping>? Mappings { get; set; }
}
