using System.Text.Json;
using System.Threading.Channels;
using Acme.Product.Core.Enums;
using Acme.Product.Desktop.Middleware;
using Acme.Product.Desktop.Station;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Acme.Product.Desktop.Endpoints;

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
        });

        app.MapGet("/api/stations/{stationId}/commands", (string stationId, int? take, [FromServices] StationRegistryService registry) =>
        {
            return Results.Ok(registry.GetRecentCommands(stationId, Math.Clamp(take ?? 100, 1, 500)));
        });

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

            var userName = context.User?.Identity?.Name;
            var updated = registry.UpdateIdentity(
                stationId,
                request,
                string.IsNullOrWhiteSpace(userName) ? request.UpdatedBy ?? "Studio" : userName,
                context.Connection.RemoteIpAddress?.ToString());
            return Results.Ok(updated);
        });

        app.MapGet("/api/stations/audit", (string? stationId, int? take, [FromServices] StationCentralStore store) =>
        {
            return Results.Ok(store.GetAudits(stationId, Math.Clamp(take ?? 100, 1, 500)));
        });

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

            if (string.IsNullOrWhiteSpace(stationId))
            {
                return Results.BadRequest(new { error = "StationIdRequired" });
            }

            if (!TryNormalizePayloadJson(request.PayloadJson, out var payloadJson, out var payloadError))
            {
                return Results.BadRequest(new { error = payloadError });
            }

            var issuedBy = context.User?.Identity?.Name;
            var command = store.CreateCommand(
                stationId,
                request.CommandType,
                payloadJson,
                string.IsNullOrWhiteSpace(issuedBy) ? request.IssuedBy ?? "Studio" : issuedBy,
                TimeSpan.FromSeconds(Math.Clamp(request.ExpiresInSeconds ?? 300, 30, 86_400)));
            return Results.Ok(command);
        });

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
                Acme.Product.Runtime.Abstractions.StationCommandType.DeployPackage,
                payload,
                context.User?.Identity?.Name ?? request.IssuedBy ?? "Studio",
                TimeSpan.FromMinutes(30));
            return Results.Ok(command);
        });

        app.MapGet("/api/station-packages", ([FromServices] StationPackageStore packageStore) =>
        {
            return Results.Ok(packageStore.GetPackages());
        });

        app.MapPost("/api/station-packages/test", async ([FromServices] StationPackageStore packageStore, HttpContext context, CancellationToken cancellationToken) =>
        {
            if (!IsStationAdmin(context))
            {
                return Results.Json(new { error = "StationAdminRequired" }, statusCode: StatusCodes.Status403Forbidden);
            }

            return Results.Ok(await packageStore.CreateTestPackageAsync(cancellationToken));
        });

        app.MapGet("/api/station-packages/{packageId}/download", (string packageId, [FromServices] StationPackageStore packageStore) =>
        {
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
            [FromServices] StationCentralStore store) =>
        {
            var (fromUtc, toUtc) = ResolveStatisticsWindow(range, from, to);
            return Results.Ok(store.GetStatistics(fromUtc, toUtc));
        });

        app.MapGet("/api/stations/events", HandleSseEventsAsync);

        app.MapGet("/api/stations/{stationId}", (string stationId, [FromServices] StationRegistryService registry) =>
        {
            var station = registry.GetStation(stationId);
            return station == null ? Results.NotFound() : Results.Ok(station);
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

        return 0;
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

    private static (DateTimeOffset FromUtc, DateTimeOffset ToUtc) ResolveStatisticsWindow(
        string? range,
        DateTimeOffset? from,
        DateTimeOffset? to)
    {
        if (from.HasValue && to.HasValue && from < to)
        {
            return (from.Value.ToUniversalTime(), to.Value.ToUniversalTime());
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

    private static bool IsStationAdmin(HttpContext context)
    {
        if (!context.Items.TryGetValue("CurrentUser", out var userObj))
        {
            return false;
        }

        var role = userObj switch
        {
            Acme.Product.Application.Services.UserSession user => user.Role,
            UserSession user => user.Role,
            _ => null
        };

        return string.Equals(role, UserRole.Admin.ToString(), StringComparison.Ordinal);
    }
}

public sealed class StationCommandCreateRequest
{
    public Acme.Product.Runtime.Abstractions.StationCommandType CommandType { get; set; } = Acme.Product.Runtime.Abstractions.StationCommandType.Ping;

    public string? PayloadJson { get; set; }

    public string? IssuedBy { get; set; }

    public int? ExpiresInSeconds { get; set; }
}

public sealed class StationDeployPackageRequest
{
    public string PackageId { get; set; } = string.Empty;

    public string? IssuedBy { get; set; }
}
