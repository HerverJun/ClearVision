using System.Threading.Channels;
using Acme.Product.Desktop.Station;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Acme.Product.Desktop.Endpoints;

public static class StationEndpoints
{
    public static IEndpointRouteBuilder MapStationEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/stations", (StationRegistryService registry) =>
        {
            return Results.Ok(registry.GetStations());
        });

        app.MapGet("/api/stations/summary", (StationRegistryService registry) =>
        {
            return Results.Ok(registry.GetSummary());
        });

        app.MapGet("/api/stations/{stationId}/results", (string stationId, int? take, StationRegistryService registry) =>
        {
            return Results.Ok(registry.GetRecentResults(stationId, Math.Clamp(take ?? 100, 1, 500)));
        });

        app.MapGet("/api/stations/{stationId}", (string stationId, StationRegistryService registry) =>
        {
            var station = registry.GetStation(stationId);
            return station == null ? Results.NotFound() : Results.Ok(station);
        });

        app.MapGet("/api/stations/events", HandleSseEventsAsync);
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
        var channel = Channel.CreateUnbounded<StoredStationRegistryEvent>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
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
}
