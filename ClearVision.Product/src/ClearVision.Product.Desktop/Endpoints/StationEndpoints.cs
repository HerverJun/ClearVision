using System.Text.Json;
using System.Threading.Channels;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Desktop.Middleware;
using ClearVision.Product.Desktop.Station;
using ClearVision.Product.Runtime.Abstractions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace ClearVision.Product.Desktop.Endpoints;

public static class StationEndpoints
{
    private const int SseChannelCapacity = 1024;

    public static IEndpointRouteBuilder MapStationEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/stations", ([FromServices] StationRegistryService registry) =>
        {
            return Results.Ok(registry.GetStations());
        });

        app.MapGet("/api/stations/summary", ([FromServices] StationRegistryService registry) =>
        {
            return Results.Ok(registry.GetSummary());
        });

        app.MapGet("/api/stations/results", (
            string? stationId,
            DateTimeOffset? from,
            DateTimeOffset? to,
            string? status,
            string? diagnosticCode,
            int? take,
            int? pageIndex,
            int? pageSize,
            [FromServices] StationRegistryService registry) =>
        {
            var resolvedPageSize = take.HasValue
                ? Math.Clamp(take.Value, 1, 500)
                : Math.Clamp(pageSize.GetValueOrDefault(50), 1, 500);
            var resolvedPageIndex = Math.Max(0, pageIndex.GetValueOrDefault(0));
            return Results.Ok(registry.GetResultsPage(
                stationId,
                from,
                to,
                status,
                diagnosticCode,
                resolvedPageIndex,
                resolvedPageSize));
        });

        app.MapGet("/api/stations/{stationId}/results", (string stationId, int? take, [FromServices] StationRegistryService registry) =>
        {
            return Results.Ok(registry.GetRecentResults(stationId, Math.Clamp(take ?? 100, 1, 500)));
        });

        app.MapGet("/api/stations/{stationId}/health", (string stationId, int? take, [FromServices] StationRegistryService registry) =>
        {
            return Results.Ok(registry.GetRecentHealth(stationId, Math.Clamp(take ?? 100, 1, 500)));
        });

        app.MapGet("/api/stations/{stationId}/logs", (string stationId, int? take, [FromServices] StationRegistryService registry) =>
        {
            return Results.Ok(registry.GetRecentLogs(stationId, Math.Clamp(take ?? 100, 1, 500)));
        })
        .RequireClearVisionPermission(ClearVisionPermissionPolicies.RequireStationAdmin);

        app.MapGet("/api/stations/{stationId}/commands", (string stationId, int? take, [FromServices] StationRegistryService registry) =>
        {
            return Results.Ok(registry.GetRecentCommands(stationId, Math.Clamp(take ?? 100, 1, 500)));
        })
        .RequireClearVisionPermission(ClearVisionPermissionPolicies.RequireStationAdmin);

        app.MapGet("/api/stations/{stationId}/commands/by-client-request/{clientRequestId}", (
            string stationId,
            string clientRequestId,
            StationCommandType commandType,
            [FromServices] StationCentralStore store) =>
        {
            var command = store.GetCommandByClientRequestId(stationId, commandType, clientRequestId);
            return command == null
                ? Results.NotFound(new { error = "StationCommandNotFound" })
                : Results.Ok(command);
        })
        .RequireClearVisionPermission(ClearVisionPermissionPolicies.RequireStationAdmin);

        app.MapPatch("/api/stations/{stationId}/identity", (
            string stationId,
            [FromBody] StationIdentityUpdateRequest request,
            [FromServices] StationRegistryService registry,
            HttpContext context) =>
        {
            if (!IsStationAdmin(context))
            {
                return Results.Json(new { error = "StationAdminRequired" }, statusCode: StatusCodes.Status403Forbidden);
            }

            var issuedBy = GetAuthenticatedUserName(context);
            if (string.IsNullOrEmpty(issuedBy))
            {
                return Results.Json(new { error = "InvalidUserSession" }, statusCode: StatusCodes.Status401Unauthorized);
            }

            var updated = registry.UpdateIdentity(
                stationId,
                request,
                issuedBy,
                context.Connection.RemoteIpAddress?.ToString());
            return Results.Ok(updated);
        })
        .RequireClearVisionPermission(ClearVisionPermissionPolicies.RequireStationAdmin);

        app.MapGet("/api/stations/audit", (string? stationId, int? take, [FromServices] StationCentralStore store) =>
        {
            return Results.Ok(store.GetAudits(stationId, Math.Clamp(take ?? 100, 1, 500)));
        })
        .RequireClearVisionPermission(ClearVisionPermissionPolicies.RequireStationAdmin);

        app.MapPost("/api/stations/{stationId}/commands", (
            string stationId,
            [FromBody] StationCommandCreateRequest request,
            [FromServices] StationCentralStore store,
            HttpContext context) =>
        {
            if (!IsStationAdmin(context))
            {
                return Results.Json(new { error = "StationAdminRequired" }, statusCode: StatusCodes.Status403Forbidden);
            }

            var issuedBy = GetAuthenticatedUserName(context);
            if (string.IsNullOrEmpty(issuedBy))
            {
                return Results.Json(new { error = "InvalidUserSession" }, statusCode: StatusCodes.Status401Unauthorized);
            }

            if (string.IsNullOrWhiteSpace(stationId))
            {
                return Results.BadRequest(new { error = "StationIdRequired" });
            }

            if (!TryNormalizePayloadJson(request.PayloadJson, out var payloadJson, out var payloadError))
            {
                return Results.BadRequest(new { error = payloadError });
            }

            if (!TryNormalizeClientRequestId(request.ClientRequestId, out var clientRequestId, out var requestIdError))
            {
                return Results.BadRequest(new { error = requestIdError });
            }

            try
            {
                var command = store.CreateCommand(
                    stationId,
                    request.CommandType,
                    payloadJson,
                    issuedBy,
                    TimeSpan.FromSeconds(Math.Clamp(request.ExpiresInSeconds ?? 300, 30, 86_400)),
                    clientRequestId);
                return Results.Ok(command);
            }
            catch (StationCommandIdempotencyConflictException conflict)
            {
                return Results.Conflict(new
                {
                    error = "StationCommandIdempotencyConflict",
                    conflict.ExistingCommandId
                });
            }
        })
        .RequireClearVisionPermission(ClearVisionPermissionPolicies.RequireStationAdmin);

        app.MapPost("/api/stations/{stationId}/deploy-package", (
            string stationId,
            [FromBody] StationDeployPackageRequest request,
            [FromServices] StationCentralStore commandStore,
            [FromServices] StationPackageStore packageStore,
            [FromServices] StationRegistryService registry,
            HttpContext context) =>
        {
            if (!IsStationAdmin(context))
            {
                return Results.Json(new { error = "StationAdminRequired" }, statusCode: StatusCodes.Status403Forbidden);
            }

            var issuedBy = GetAuthenticatedUserName(context);
            if (string.IsNullOrEmpty(issuedBy))
            {
                return Results.Json(new { error = "InvalidUserSession" }, statusCode: StatusCodes.Status401Unauthorized);
            }

            if (string.IsNullOrWhiteSpace(stationId))
            {
                return Results.BadRequest(new { error = "StationIdRequired" });
            }

            if (string.IsNullOrWhiteSpace(request.PackageId))
            {
                return Results.BadRequest(new { error = "PackageIdRequired" });
            }
            if (!TryNormalizeClientRequestId(request.ClientRequestId, out var clientRequestId, out var requestIdError))
            {
                return Results.BadRequest(new { error = requestIdError });
            }

            var existing = commandStore.GetCommandByClientRequestId(
                stationId,
                StationCommandType.DeployPackage,
                clientRequestId);
            if (existing != null)
            {
                return DeployCommandTargetsPackage(existing, request.PackageId)
                    ? Results.Ok(existing)
                    : Results.Conflict(new
                    {
                        error = "StationCommandIdempotencyConflict",
                        existingCommandId = existing.CommandId
                    });
            }

            var package = packageStore.GetPackage(request.PackageId);
            if (package == null)
            {
                return Results.NotFound(new { error = "PackageNotFound" });
            }

            if (package.PackageKind != StationPackageKind.Production)
            {
                return Results.BadRequest(new
                {
                    error = "ProductionPackageRequired",
                    message = "测试包不能通过正式部署入口下发，请使用“下发测试包”。"
                });
            }

            if (!HasCompleteDeploymentIdentity(package))
            {
                return Results.Conflict(new
                {
                    error = "StationPackageIdentityIncomplete",
                    message = "运行包缺少版本、SHA、来源修订、流程或判定配置身份，不能创建正式部署命令。"
                });
            }

            var station = registry.GetStation(stationId);
            if (station == null)
            {
                return Results.NotFound(new { error = "StationNotFound" });
            }

            var admissionFailure = ValidateDeploymentAdmission(station, package);
            if (admissionFailure.HasValue)
            {
                return Results.Conflict(new
                {
                    error = admissionFailure.Value.Error,
                    message = admissionFailure.Value.Message
                });
            }

            var payload = System.Text.Json.JsonSerializer.Serialize(new
            {
                packageId = package.PackageId,
                packageName = package.PackageName,
                packageVersion = package.PackageVersion,
                packageKind = package.PackageKind,
                sha256 = package.Sha256,
                flowHash = package.FlowHash,
                sourceProjectId = package.SourceProjectId,
                sourceProjectRevision = package.SourceProjectRevision,
                decisionConfigurationHash = package.DecisionConfigurationHash,
                downloadUrl = $"/api/station-packages/{Uri.EscapeDataString(package.PackageId)}/download"
            });
            try
            {
                var command = commandStore.CreateCommand(
                    stationId,
                    ClearVision.Product.Runtime.Abstractions.StationCommandType.DeployPackage,
                    payload,
                    issuedBy,
                    TimeSpan.FromMinutes(30),
                    clientRequestId);
                return Results.Ok(command);
            }
            catch (StationCommandIdempotencyConflictException conflict)
            {
                return Results.Conflict(new
                {
                    error = "StationCommandIdempotencyConflict",
                    conflict.ExistingCommandId
                });
            }
        })
        .RequireClearVisionPermission(ClearVisionPermissionPolicies.RequireStationAdmin);

        app.MapGet("/api/station-packages", ([FromServices] StationPackageStore packageStore) =>
        {
            return Results.Ok(packageStore.GetPackages());
        })
        .RequireClearVisionPermission(ClearVisionPermissionPolicies.RequireStationAdmin);

        app.MapPost("/api/station-packages/test", async ([FromServices] StationPackageStore packageStore, HttpContext context, CancellationToken cancellationToken) =>
        {
            if (!IsStationAdmin(context))
            {
                return Results.Json(new { error = "StationAdminRequired" }, statusCode: StatusCodes.Status403Forbidden);
            }

            var issuedBy = GetAuthenticatedUserName(context);
            if (string.IsNullOrEmpty(issuedBy))
            {
                return Results.Json(new { error = "InvalidUserSession" }, statusCode: StatusCodes.Status401Unauthorized);
            }

            return Results.Ok(await packageStore.CreateTestPackageAsync(cancellationToken));
        })
        .RequireClearVisionPermission(ClearVisionPermissionPolicies.RequireStationAdmin);

        app.MapGet("/api/station-packages/{packageId}/download", (
            string packageId,
            [FromServices] StationPackageStore packageStore,
            HttpContext context) =>
        {
            if (!CanDownloadStationPackage(context))
            {
                return Results.Json(new { error = "StationPackageDownloadPermissionRequired" }, statusCode: StatusCodes.Status403Forbidden);
            }

            if (!packageStore.TryGetPackageFileForDownload(packageId, out var path))
            {
                return Results.NotFound(new { error = "PackageNotFound" });
            }

            return Results.File(path, "application/octet-stream", Path.GetFileName(path));
        });

        app.MapGet("/api/stations/statistics", (
            string? range,
            DateTimeOffset? from,
            DateTimeOffset? to,
            string? stationId,
            string? status,
            string? diagnosticCode,
            [FromServices] StationRegistryService registry) =>
        {
            var (fromUtc, toUtc) = ResolveStatisticsWindow(range, from, to);
            return Results.Ok(registry.GetStatistics(fromUtc, toUtc, stationId, status, diagnosticCode));
        });

        app.MapGet("/api/stations/events", HandleSseEventsAsync);

        app.MapGet("/api/stations/{stationId}", (string stationId, [FromServices] StationRegistryService registry) =>
        {
            var station = registry.GetStation(stationId);
            return station == null ? Results.NotFound() : Results.Ok(station);
        })
        .RequireClearVisionPermission(ClearVisionPermissionPolicies.RequireStationAdmin);

        return app;
    }

    private static async Task HandleSseEventsAsync(
        HttpContext context,
        StationRegistryService registry,
        CancellationToken cancellationToken)
    {
        context.Response.Headers.Append("Content-Type", "text/event-stream");
        context.Response.Headers.Append("Cache-Control", "no-cache");
        context.Response.Headers.Append("Connection", "keep-alive");
        context.Response.Headers.Append("X-Accel-Buffering", "no");
        await context.Response.StartAsync(cancellationToken);

        var lastSequenceId = ParseLastEventId(context.Request);
        var replayWatermark = lastSequenceId;
        var channel = Channel.CreateBounded<StoredStationRegistryEvent>(new BoundedChannelOptions(SseChannelCapacity)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropOldest
        });

        using var subscription = registry.Subscribe(evt => channel.Writer.TryWrite(evt));
        using var cancellationRegistration = cancellationToken.Register(() => channel.Writer.TryComplete());

        await context.Response.WriteSseMessageAsync(
            new SseMessage(
                null,
                "initialState",
                registry.GetSseSnapshot()),
            cancellationToken);

        if (lastSequenceId > 0)
        {
            foreach (var storedEvent in registry.GetEventsAfter(lastSequenceId))
            {
                replayWatermark = Math.Max(replayWatermark, storedEvent.SequenceId);
                await context.Response.WriteSseMessageAsync(
                    new SseMessage(storedEvent.SequenceId, storedEvent.EventType, storedEvent.Data),
                    cancellationToken);
            }
        }

        var heartbeatTask = SendHeartbeatsAsync(channel.Writer, cancellationToken);

        try
        {
            await foreach (var evt in channel.Reader.ReadAllAsync(cancellationToken))
            {
                if (evt.EventType != "heartbeat" && evt.SequenceId <= replayWatermark)
                {
                    continue;
                }

                await context.Response.WriteSseMessageAsync(
                    new SseMessage(evt.SequenceId, evt.EventType, evt.Data),
                    cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            channel.Writer.TryComplete();

            try
            {
                await heartbeatTask;
            }
            catch (OperationCanceledException)
            {
            }
        }
    }

    private static long ParseLastEventId(HttpRequest request)
    {
        if (request.Headers.TryGetValue("Last-Event-ID", out var lastEventIdHeader) &&
            long.TryParse(lastEventIdHeader.FirstOrDefault(), out var parsedId))
        {
            return parsedId;
        }

        if (long.TryParse(request.Query["lastEventId"].FirstOrDefault(), out var lastEventId))
        {
            return lastEventId;
        }

        return long.TryParse(request.Query["afterSequence"].FirstOrDefault(), out var afterSequence)
            ? afterSequence
            : 0;
    }

    private static async Task SendHeartbeatsAsync(ChannelWriter<StoredStationRegistryEvent> writer, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
                writer.TryWrite(new StoredStationRegistryEvent(0, "heartbeat", new { timestamp = DateTime.UtcNow }, DateTimeOffset.UtcNow));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private static (DateTimeOffset? FromUtc, DateTimeOffset? ToUtc) ResolveStatisticsWindow(
        string? range,
        DateTimeOffset? from,
        DateTimeOffset? to)
    {
        if (from.HasValue && to.HasValue && from < to)
        {
            return (from.Value.ToUniversalTime(), to.Value.ToUniversalTime());
        }

        if (string.Equals(range, "all", StringComparison.OrdinalIgnoreCase))
        {
            return (null, null);
        }

        var now = DateTimeOffset.UtcNow;
        var todayLocal = DateTime.Today;
        var todayUtc = new DateTimeOffset(todayLocal).ToUniversalTime();
        return string.Equals(range, "week", StringComparison.OrdinalIgnoreCase) switch
        {
            true => (now.AddDays(-7), now),
            _ when string.Equals(range, "month", StringComparison.OrdinalIgnoreCase) => (now.AddMonths(-1), now),
            _ => (todayUtc, now)
        };
    }

    private static bool TryNormalizePayloadJson(string? payloadJson, out string normalizedPayloadJson, out string? error)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            normalizedPayloadJson = "{}";
            error = null;
            return true;
        }

        try
        {
            using var document = JsonDocument.Parse(payloadJson);
            normalizedPayloadJson = document.RootElement.GetRawText();
            error = null;
            return true;
        }
        catch (JsonException)
        {
            normalizedPayloadJson = "{}";
            error = "PayloadJsonInvalid";
            return false;
        }
    }

    private static bool TryNormalizeClientRequestId(
        string? clientRequestId,
        out string normalizedClientRequestId,
        out string? error)
    {
        normalizedClientRequestId = clientRequestId?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedClientRequestId))
        {
            error = "ClientRequestIdRequired";
            return false;
        }

        if (normalizedClientRequestId.Length > 128)
        {
            error = "ClientRequestIdTooLong";
            return false;
        }

        error = null;
        return true;
    }

    private static bool DeployCommandTargetsPackage(StationCommandDto command, string packageId)
    {
        try
        {
            using var document = JsonDocument.Parse(command.PayloadJson);
            return document.RootElement.TryGetProperty("packageId", out var value) &&
                string.Equals(value.GetString(), packageId.Trim(), StringComparison.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool HasCompleteDeploymentIdentity(StationPackageManifestDto package)
    {
        var sha256 = package.Sha256.Trim().Replace("sha256:", string.Empty, StringComparison.OrdinalIgnoreCase);
        return !string.IsNullOrWhiteSpace(package.PackageVersion) &&
            !string.IsNullOrWhiteSpace(package.MinStationVersion) &&
            sha256.Length == 64 && sha256.All(Uri.IsHexDigit) &&
            !string.IsNullOrWhiteSpace(package.FlowHash) &&
            package.SourceProjectId is { } projectId && projectId != Guid.Empty &&
            package.SourceProjectRevision.HasValue &&
            !string.IsNullOrWhiteSpace(package.DecisionConfigurationHash);
    }

    private static (string Error, string Message)? ValidateDeploymentAdmission(
        StationStatusViewModel station,
        StationPackageManifestDto package)
    {
        if (!station.IsEnabled)
        {
            return ("StationDisabled", "目标工作站已禁用；请先恢复工作站准入状态。");
        }
        if (!station.IsOnline || station.OnlineState == StationOnlineState.Offline)
        {
            return ("StationOffline", "目标工作站离线或心跳已过期；恢复在线后再创建部署命令。");
        }
        if (!string.Equals(station.StationRole, "Inspection", StringComparison.OrdinalIgnoreCase))
        {
            return ("StationRoleNotDeployable", "正式运行包只能部署到 Inspection 角色工作站。");
        }
        if (station.RuntimeState != StationRuntimeState.Idle)
        {
            return ("StationRuntimeNotIdle", "目标工作站必须处于空闲状态才能创建部署命令。");
        }
        if (!TryParseVersion(station.ClientVersion, out var stationVersion))
        {
            return ("StationVersionUnknown", "目标工作站未上报可比较的版本号。");
        }
        if (!TryParseVersion(package.MinStationVersion, out var minimumVersion))
        {
            return ("PackageMinimumVersionInvalid", "运行包最小工作站版本格式无效。");
        }
        if (stationVersion < minimumVersion)
        {
            return ("StationVersionIncompatible", $"目标工作站版本 {station.ClientVersion} 低于运行包要求 {package.MinStationVersion}。");
        }

        return null;
    }

    private static bool TryParseVersion(string? value, out Version version)
    {
        var normalized = value?.Trim().Split('-', 2)[0].Split('+', 2)[0] ?? string.Empty;
        return Version.TryParse(normalized, out version!);
    }

    private static bool IsStationAdmin(HttpContext context)
    {
        return ClearVisionPermissionPolicies.Authorize(
            context,
            ClearVisionPermissionPolicies.RequireStationAdmin,
            out _);
    }

    private static bool CanDownloadStationPackage(HttpContext context)
    {
        if (IsStationAdmin(context))
        {
            return true;
        }

        var stationAuthService = context.RequestServices.GetService<StationIngressAuthService>();
        return stationAuthService?.TryAuthorize(context, out _) == true;
    }

    private static string GetAuthenticatedUserName(HttpContext context)
    {
        var resolvedUserName = EndpointPermissionGuards.GetAuthenticatedUserName(context);
        if (!string.IsNullOrWhiteSpace(resolvedUserName))
        {
            return resolvedUserName;
        }

        if (context.Items.TryGetValue("CurrentUser", out var userObj))
        {
            var userName = userObj switch
            {
                ClearVision.Product.Application.Services.UserSession user => user.Username,
                UserSession user => user.Username,
                _ => null
            };

            if (!string.IsNullOrWhiteSpace(userName))
            {
                return userName.Trim();
            }
        }

        var principalName = context.User?.Identity?.Name;
        if (!string.IsNullOrWhiteSpace(principalName))
        {
            return principalName.Trim();
        }

        // 拒绝回退到 "Studio"，防止特权端点进行隐秘操作或审计绕过
        return string.Empty;
    }
}

public sealed class StationCommandCreateRequest
{
    public ClearVision.Product.Runtime.Abstractions.StationCommandType CommandType { get; set; } = ClearVision.Product.Runtime.Abstractions.StationCommandType.Ping;

    public string? PayloadJson { get; set; }

    public string? IssuedBy { get; set; }

    public int? ExpiresInSeconds { get; set; }

    public string? ClientRequestId { get; set; }
}

public sealed class StationDeployPackageRequest
{
    public string PackageId { get; set; } = string.Empty;

    public string? IssuedBy { get; set; }

    public string? ClientRequestId { get; set; }
}
