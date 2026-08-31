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
        app.MapGet("/api/stations", ([FromServices] StationRegistryService registry, HttpContext context) =>
        {
            var stations = registry.GetStations();
            return Results.Ok(IsStationAdmin(context)
                ? stations
                : stations.Select(StationMonitoringProjection.ToSafeStatus).ToList());
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
            [FromServices] StationRegistryService registry,
            HttpContext context) =>
        {
            try
            {
                var window = StationResultQueryBudget.Normalize(from, to, DateTimeOffset.UtcNow);
                var resolvedPageSize = take.HasValue
                    ? Math.Clamp(take.Value, 1, 500)
                    : Math.Clamp(pageSize.GetValueOrDefault(50), 1, 500);
                var resolvedPageIndex = Math.Max(0, pageIndex.GetValueOrDefault(0));
                var page = registry.GetResultsPage(
                    stationId,
                    window.FromUtc,
                    window.ToUtc,
                    status,
                    diagnosticCode,
                    resolvedPageIndex,
                    resolvedPageSize);
                return Results.Ok(IsStationAdmin(context)
                    ? page
                    : StationMonitoringProjection.ToSafeResultsPage(page));
            }
            catch (StationResultQueryBudgetException exception)
            {
                return StationBudgetViolation(exception);
            }
        });

        app.MapGet("/api/stations/{stationId}/results", (
            string stationId,
            int? take,
            [FromServices] StationRegistryService registry,
            HttpContext context) =>
        {
            var results = registry.GetRecentResults(stationId, Math.Clamp(take ?? 100, 1, 500));
            return Results.Ok(IsStationAdmin(context)
                ? results
                : results.Select(StationMonitoringProjection.ToSafeResult).ToList());
        });

        app.MapGet("/api/stations/{stationId}/health", (
            string stationId,
            int? take,
            [FromServices] StationRegistryService registry,
            HttpContext context) =>
        {
            var health = registry.GetRecentHealth(stationId, Math.Clamp(take ?? 100, 1, 500));
            return Results.Ok(IsStationAdmin(context)
                ? health
                : health.Select(item => StationMonitoringProjection.ToSafeHealth(item)).ToList());
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

            var command = store.CreateCommand(
                stationId,
                request.CommandType,
                payloadJson,
                issuedBy,
                TimeSpan.FromSeconds(Math.Clamp(request.ExpiresInSeconds ?? 300, 30, 86_400)));
            return Results.Ok(command);
        })
        .RequireClearVisionPermission(ClearVisionPermissionPolicies.RequireStationAdmin);

        app.MapPost("/api/stations/{stationId}/deploy-package", (
            string stationId,
            [FromBody] StationDeployPackageRequest request,
            [FromServices] StationCentralStore commandStore,
            [FromServices] StationPackageStore packageStore,
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

            var payload = System.Text.Json.JsonSerializer.Serialize(new
            {
                packageId = package.PackageId,
                packageName = package.PackageName,
                packageVersion = package.PackageVersion,
                sha256 = package.Sha256,
                downloadUrl = $"/api/station-packages/{Uri.EscapeDataString(package.PackageId)}/download"
            });
            var command = commandStore.CreateCommand(
                stationId,
                ClearVision.Product.Runtime.Abstractions.StationCommandType.DeployPackage,
                payload,
                issuedBy,
                TimeSpan.FromMinutes(30));
            return Results.Ok(command);
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
            try
            {
                var window = ResolveStatisticsWindow(range, from, to);
                return Results.Ok(registry.GetStatistics(window.FromUtc, window.ToUtc, stationId, status, diagnosticCode));
            }
            catch (StationResultQueryBudgetException exception)
            {
                return StationBudgetViolation(exception);
            }
        });

        app.MapGet("/api/stations/events", HandleSseEventsAsync);

        app.MapGet("/api/stations/{stationId}", (
            string stationId,
            [FromServices] StationRegistryService registry,
            HttpContext context) =>
        {
            var station = registry.GetStation(stationId);
            if (station is null)
            {
                return Results.NotFound();
            }

            return Results.Ok(IsStationAdmin(context)
                ? station
                : StationMonitoringProjection.ToSafeDetail(station));
        });

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

        var includeSensitive = IsStationAdmin(context);
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

        var snapshot = registry.GetSseSnapshot();
        await context.Response.WriteSseMessageAsync(
            new SseMessage(
                null,
                "initialState",
                includeSensitive
                    ? snapshot
                    : StationMonitoringProjection.ToSafeSnapshot(snapshot)),
            cancellationToken);

        if (lastSequenceId > 0)
        {
            foreach (var storedEvent in registry.GetEventsAfter(lastSequenceId))
            {
                replayWatermark = Math.Max(replayWatermark, storedEvent.SequenceId);
                if (!StationMonitoringProjection.TryProjectEvent(
                        storedEvent,
                        includeSensitive,
                        out var projectedEvent))
                {
                    continue;
                }

                await context.Response.WriteSseMessageAsync(
                    new SseMessage(projectedEvent.SequenceId, projectedEvent.EventType, projectedEvent.Data),
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

                if (!StationMonitoringProjection.TryProjectEvent(
                        evt,
                        includeSensitive,
                        out var projectedEvent))
                {
                    continue;
                }

                await context.Response.WriteSseMessageAsync(
                    new SseMessage(projectedEvent.SequenceId, projectedEvent.EventType, projectedEvent.Data),
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

    private static StationResultWindow ResolveStatisticsWindow(
        string? range,
        DateTimeOffset? from,
        DateTimeOffset? to)
    {
        if (from.HasValue || to.HasValue)
        {
            return StationResultQueryBudget.Normalize(from, to, DateTimeOffset.UtcNow);
        }

        var now = DateTimeOffset.UtcNow;
        var todayLocal = DateTime.Today;
        var todayUtc = new DateTimeOffset(todayLocal).ToUniversalTime();
        var requestedWindow = string.Equals(range, "week", StringComparison.OrdinalIgnoreCase) switch
        {
            true => (now.AddDays(-7), now),
            _ when string.Equals(range, "month", StringComparison.OrdinalIgnoreCase) => (now.AddMonths(-1), now),
            _ when string.Equals(range, "all", StringComparison.OrdinalIgnoreCase) => (now.AddDays(-StationResultQueryBudget.DefaultWindowDays), now),
            _ => (todayUtc, now)
        };
        return StationResultQueryBudget.Normalize(requestedWindow.Item1, requestedWindow.Item2, now);
    }

    private static IResult StationBudgetViolation(StationResultQueryBudgetException exception) =>
        Results.BadRequest(new
        {
            error = exception.ErrorCode,
            message = exception.Message,
            maximumWindowDays = StationResultQueryBudget.MaximumWindowDays
        });

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
}

public sealed class StationDeployPackageRequest
{
    public string PackageId { get; set; } = string.Empty;

    public string? IssuedBy { get; set; }
}
