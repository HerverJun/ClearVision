using System.Collections;
using System.Diagnostics;
using System.Net.Sockets;
using System.Text.Json;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.ValueObjects;
using ClearVision.Product.Desktop.Middleware;
using ClearVision.Product.Infrastructure.Communication.Gr;
using ClearVision.Product.Infrastructure.Operators;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace ClearVision.Product.Desktop.Endpoints;

public static class CommunicationEndpoints
{
    public static IEndpointRouteBuilder MapCommunicationEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/communication/templates/gr", (GrRegisterMapCatalog catalog) =>
            Results.Ok(catalog.GetTemplate()))
            .RequireClearVisionPermission(ClearVisionPermissionPolicies.CanOperateHardware);

        app.MapGet("/api/communication/profiles", (JsonCommunicationProfileStore store) =>
            Results.Ok(new { Profiles = store.GetAll(), store.StoragePath }))
            .RequireClearVisionPermission(ClearVisionPermissionPolicies.CanOperateHardware);

        app.MapPut("/api/communication/profiles/{id}", (
            string id,
            SaveModbusProfileRequest request,
            JsonCommunicationProfileStore store,
            GrRegisterMapCatalog catalog) =>
        {
            try
            {
                var template = catalog.GetTemplate();
                var isGrTemplate = string.Equals(request.TemplateId, template.TemplateId, StringComparison.OrdinalIgnoreCase);
                var profile = store.Save(new ModbusDeviceProfile
                {
                    Id = id,
                    Name = request.Name,
                    Host = request.Host,
                    Port = request.Port,
                    UnitId = request.UnitId,
                    TemplateId = isGrTemplate ? template.TemplateId : request.TemplateId,
                    TemplateVersion = isGrTemplate ? template.Version : request.TemplateVersion,
                    TemplateHash = isGrTemplate ? template.Sha256 : request.TemplateHash,
                    ReadOnly = true
                });
                return Results.Ok(profile);
            }
            catch (Exception ex) when (ex is ArgumentException or IOException or JsonException)
            {
                return Results.BadRequest(new { Code = "COMMUNICATION_PROFILE_INVALID", Error = ex.Message });
            }
        })
        .RequireClearVisionPermission(ClearVisionPermissionPolicies.CanOperateHardware);

        app.MapDelete("/api/communication/profiles/{id}", (string id, JsonCommunicationProfileStore store) =>
            store.Delete(id)
                ? Results.NoContent()
                : Results.NotFound(new { Code = "COMMUNICATION_PROFILE_NOT_FOUND" }))
            .RequireClearVisionPermission(ClearVisionPermissionPolicies.CanOperateHardware);

        app.MapPost("/api/communication/diagnostics/execute", async (
            CommunicationDiagnosticRequest request,
            JsonCommunicationProfileStore store,
            GrRegisterMapCatalog catalog,
            GrStateDecoder decoder,
            ModbusCommunicationOperator modbusOperator,
            CancellationToken cancellationToken) =>
        {
            var profile = string.IsNullOrWhiteSpace(request.ProfileId) ? null : store.Get(request.ProfileId);
            if (!string.IsNullOrWhiteSpace(request.ProfileId) && profile == null)
            {
                return Results.NotFound(new { Code = "COMMUNICATION_PROFILE_NOT_FOUND", request.ProfileId });
            }

            var host = profile?.Host ?? request.Host;
            var port = profile?.Port ?? request.Port;
            var unitId = profile?.UnitId ?? request.UnitId;
            if (string.IsNullOrWhiteSpace(host) || port is < 1 or > 65535 || unitId is < 1 or > 255)
            {
                return Results.BadRequest(new { Code = "COMMUNICATION_ENDPOINT_INVALID", Error = "Host, Port and UnitId are required." });
            }

            var operation = request.Operation?.Trim() ?? string.Empty;
            if (operation.Equals("Connect", StringComparison.OrdinalIgnoreCase))
            {
                var stopwatch = Stopwatch.StartNew();
                try
                {
                    using var client = new TcpClient { NoDelay = true };
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    timeout.CancelAfter(Math.Clamp(request.TimeoutMs, 100, 60000));
                    await client.ConnectAsync(host, port, timeout.Token);
                    stopwatch.Stop();
                    return Results.Ok(new
                    {
                        Success = true,
                        Code = "COMMUNICATION_CONNECT_OK",
                        Endpoint = $"{host}:{port}",
                        UnitId = unitId,
                        LatencyMs = stopwatch.ElapsedMilliseconds,
                        ReadOnly = true
                    });
                }
                catch (Exception ex) when (ex is SocketException or OperationCanceledException or IOException)
                {
                    stopwatch.Stop();
                    return Results.Json(new
                    {
                        Success = false,
                        Code = ex is OperationCanceledException ? "COMMUNICATION_CONNECT_TIMEOUT" : "COMMUNICATION_CONNECT_FAILED",
                        Error = ex.Message,
                        Endpoint = $"{host}:{port}",
                        LatencyMs = stopwatch.ElapsedMilliseconds
                    }, statusCode: StatusCodes.Status502BadGateway);
                }
            }

            if (!operation.Equals("ReadOnce", StringComparison.OrdinalIgnoreCase))
            {
                return Results.BadRequest(new
                {
                    Code = "COMMUNICATION_DIAGNOSTIC_OPERATION_BLOCKED",
                    Error = "Only Connect and ReadOnce are allowed. Diagnostic writes are disabled."
                });
            }

            var requestedFunctionCode = request.FunctionCode?.Trim() ?? string.Empty;
            var functionCode = requestedFunctionCode.Equals("ReadHolding", StringComparison.OrdinalIgnoreCase)
                ? "ReadHolding"
                : requestedFunctionCode.Equals("ReadCoils", StringComparison.OrdinalIgnoreCase)
                    ? "ReadCoils"
                    : string.Empty;
            if (functionCode.Length == 0)
            {
                return Results.BadRequest(new
                {
                    Code = "COMMUNICATION_WRITE_BLOCKED",
                    Error = "Diagnostics only allow ReadHolding or ReadCoils."
                });
            }

            var op = new Operator("Communication diagnostic read", OperatorType.ModbusCommunication, 0, 0);
            AddParameter(op, "Protocol", "TCP", "string");
            AddParameter(op, "ProfileId", profile?.Id ?? string.Empty, "string");
            AddParameter(op, "IpAddress", host, "string");
            AddParameter(op, "Port", port, "int");
            AddParameter(op, "SlaveId", unitId, "int");
            AddParameter(op, "RegisterAddress", request.StartAddress, "int");
            AddParameter(op, "RegisterCount", Math.Clamp(request.Count, 1, 125), "int");
            AddParameter(op, "FunctionCode", functionCode, "string");
            AddParameter(op, "TimeoutMs", Math.Clamp(request.TimeoutMs, 100, 60000), "int");

            var result = await modbusOperator.ExecuteAsync(op, [], cancellationToken);
            if (!result.IsSuccess)
            {
                return Results.Json(new
                {
                    Success = false,
                    Code = "COMMUNICATION_READ_FAILED",
                    Error = result.ErrorMessage,
                    Endpoint = $"{host}:{port}"
                }, statusCode: StatusCodes.Status502BadGateway);
            }

            var values = ToUInt16Values(result.OutputData?.GetValueOrDefault("Values"));
            var template = catalog.GetTemplate();
            var decoded = profile?.TemplateId.Equals(template.TemplateId, StringComparison.OrdinalIgnoreCase) == true
                && functionCode == "ReadHolding"
                ? decoder.Decode(request.StartAddress, values)
                : [];
            return Results.Ok(new
            {
                Success = true,
                Code = "COMMUNICATION_READ_OK",
                Endpoint = $"{host}:{port}",
                UnitId = unitId,
                ReadOnly = true,
                Output = result.OutputData,
                Decoded = decoded
            });
        })
        .RequireClearVisionPermission(ClearVisionPermissionPolicies.CanOperateHardware);

        return app;
    }

    private static void AddParameter(Operator op, string name, object value, string dataType) =>
        op.AddParameter(new Parameter(Guid.NewGuid(), name, name, string.Empty, dataType, value, isRequired: true));

    private static ushort[] ToUInt16Values(object? value)
    {
        if (value is not IEnumerable values)
        {
            return [];
        }

        return values.Cast<object?>()
            .Select(item => Convert.ToUInt16(item))
            .ToArray();
    }
}

public sealed record SaveModbusProfileRequest
{
    public string Name { get; init; } = string.Empty;
    public string Host { get; init; } = string.Empty;
    public int Port { get; init; } = 502;
    public int UnitId { get; init; } = 1;
    public string TemplateId { get; init; } = string.Empty;
    public string TemplateVersion { get; init; } = string.Empty;
    public string TemplateHash { get; init; } = string.Empty;
}

public sealed record CommunicationDiagnosticRequest
{
    public string Operation { get; init; } = "Connect";
    public string ProfileId { get; init; } = string.Empty;
    public string Host { get; init; } = string.Empty;
    public int Port { get; init; } = 502;
    public int UnitId { get; init; } = 1;
    public string FunctionCode { get; init; } = "ReadHolding";
    public int StartAddress { get; init; } = 0;
    public int Count { get; init; } = 1;
    public int TimeoutMs { get; init; } = 5000;
}
