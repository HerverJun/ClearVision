using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Channels;
using ClearVision.Product.Core.Events;
using ClearVision.Product.Core.Services;
using ClearVision.Product.Desktop.Inspection;
using ClearVision.Product.Infrastructure.Events;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace ClearVision.Product.Desktop.Endpoints;

public static class InspectionEventEndpoints
{
    private const int DefaultSseChannelCapacity = 512;

    private static long _sseDroppedMessageCount;
    private static long _sseReplayedMessageCount;

    private static readonly JsonSerializerOptions SseJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters =
        {
            new JsonStringEnumConverter(allowIntegerValues: true)
        }
    };

    public static IEndpointRouteBuilder MapInspectionEventEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/inspection/realtime/{projectId:guid}/events", HandleSseEventsAsync);
        app.MapGet("/api/inspection/realtime/diagnostics", () => Results.Ok(new
        {
            droppedMessages = Volatile.Read(ref _sseDroppedMessageCount),
            replayedMessages = Volatile.Read(ref _sseReplayedMessageCount),
            channelCapacity = ResolveSseChannelCapacity()
        }));
        return app;
    }

    private static async Task HandleSseEventsAsync(
        Guid projectId,
        HttpContext context,
        IInspectionEventBus eventBus,
        IEventStore eventStore,
        IInspectionRuntimeCoordinator coordinator,
        CancellationToken ct)
    {
        context.Response.Headers.Append("Content-Type", "text/event-stream");
        context.Response.Headers.Append("Cache-Control", "no-cache");
        context.Response.Headers.Append("Connection", "keep-alive");
        context.Response.Headers.Append("X-Accel-Buffering", "no");
        await context.Response.StartAsync(ct);

        var lastSequenceId = ParseLastEventId(context.Request);
        var currentState = coordinator.GetState(projectId);
        var replayWatermark = lastSequenceId;

        var channel = Channel.CreateBounded<SseMessage>(new BoundedChannelOptions(ResolveSseChannelCapacity())
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropWrite
        });

        using var subscription = eventBus.SubscribeInterface<IInspectionEvent>((evt, _) =>
        {
            if (evt.ProjectId != projectId)
            {
                return Task.CompletedTask;
            }

            var sequenceId = eventStore.Append(projectId, evt);
            foreach (var mappedEvent in InspectionRealtimeEventMapper.Map(evt))
            {
                if (!channel.Writer.TryWrite(new SseMessage(sequenceId, mappedEvent.EventType, mappedEvent.Payload)))
                {
                    Interlocked.Increment(ref _sseDroppedMessageCount);
                }
            }
            return Task.CompletedTask;
        });

        if (currentState is not null)
        {
            foreach (var snapshot in InspectionRealtimeEventMapper.CreateSnapshot(currentState))
            {
                await context.Response.WriteSseMessageAsync(
                    new SseMessage(
                        null,
                        snapshot.EventType,
                        snapshot.Payload),
                    ct);
            }
        }

        if (lastSequenceId > 0)
        {
            foreach (var storedEvent in eventStore.GetEventsAfter(projectId, lastSequenceId))
            {
                replayWatermark = Math.Max(replayWatermark, storedEvent.SequenceId);
                foreach (var mappedEvent in InspectionRealtimeEventMapper.Map(storedEvent.Event))
                {
                    Interlocked.Increment(ref _sseReplayedMessageCount);
                    await context.Response.WriteSseMessageAsync(
                        new SseMessage(
                            storedEvent.SequenceId,
                            mappedEvent.EventType,
                            mappedEvent.Payload),
                        ct);
                }
            }
        }

        using var channelRegistration = ct.Register(() => channel.Writer.TryComplete());
        var heartbeatTask = SendHeartbeatsAsync(channel.Writer, ct);

        try
        {
            await foreach (var message in channel.Reader.ReadAllAsync(ct))
            {
                if (message.SequenceId.HasValue && message.SequenceId.Value <= replayWatermark)
                {
                    continue;
                }

                await context.Response.WriteSseMessageAsync(message, ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Client disconnected.
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
                // Ignore cancellation during shutdown.
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

    private static int ResolveSseChannelCapacity()
    {
        return int.TryParse(Environment.GetEnvironmentVariable("CV_INSPECTION_SSE_CHANNEL_CAPACITY"), out var configured)
            ? Math.Clamp(configured, 1, 10_000)
            : DefaultSseChannelCapacity;
    }

    private static async Task SendHeartbeatsAsync(ChannelWriter<SseMessage> writer, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(30), ct);
                writer.TryWrite(new SseMessage(null, "heartbeat", new { timestamp = DateTime.UtcNow }));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
        }
    }

    public static async Task WriteSseMessageAsync(this HttpResponse response, SseMessage message, CancellationToken ct)
    {
        if (message.EventType == "heartbeat")
        {
            await response.WriteAsync(":keepalive\n\n", ct);
            await response.Body.FlushAsync(ct);
            return;
        }

        var json = JsonSerializer.Serialize(
            message.Data,
            SseJsonOptions);

        if (message.SequenceId.HasValue)
        {
            await response.WriteAsync($"id: {message.SequenceId.Value}\n", ct);
        }

        await response.WriteAsync($"event: {message.EventType}\n", ct);
        await response.WriteAsync($"data: {json}\n\n", ct);
        await response.Body.FlushAsync(ct);
    }
}

public sealed record SseMessage(long? SequenceId, string EventType, object Data);
