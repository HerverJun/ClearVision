using System.Text;
using System.Text.Json;
using ClearVision.Product.Application.Services;
using ClearVision.Product.Core.Events;
using ClearVision.Product.Core.Services;
using ClearVision.Product.Desktop.Endpoints;
using ClearVision.Product.Desktop.Middleware;
using ClearVision.Product.Infrastructure.Events;
using ClearVision.Product.Infrastructure.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace ClearVision.Product.Desktop.Tests;

public class InspectionEventEndpointsTests
{
    [Fact]
    public async Task StateEndpoint_ReturnsIdle_WhenNoSessionExists()
    {
        await using var host = await InspectionEventTestHost.CreateAsync();
        var projectId = Guid.NewGuid();

        using var response = await host.Client.GetAsync($"/api/inspection/realtime/{projectId}/state");
        var state = await ReadJsonObjectAsync(response);

        response.EnsureSuccessStatusCode();
        state.GetProperty("projectId").GetGuid().Should().Be(projectId);
        state.GetProperty("status").GetString().Should().Be("Idle");
        state.GetProperty("isBusy").GetBoolean().Should().BeFalse();
        state.GetProperty("sessionId").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Theory]
    [InlineData(RuntimeStatus.Starting)]
    [InlineData(RuntimeStatus.Running)]
    [InlineData(RuntimeStatus.Stopping)]
    public async Task StateEndpoint_ReturnsBusy_ForActiveRuntimeStates(RuntimeStatus status)
    {
        await using var host = await InspectionEventTestHost.CreateAsync();
        var projectId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();

        await host.Coordinator.TryStartAsync(projectId, sessionId, CancellationToken.None);
        if (status is RuntimeStatus.Running or RuntimeStatus.Stopping)
        {
            host.Coordinator.UpdateSessionStatus(projectId, sessionId, status);
        }

        using var response = await host.Client.GetAsync($"/api/inspection/realtime/{projectId}/state");
        var state = await ReadJsonObjectAsync(response);

        response.EnsureSuccessStatusCode();
        state.GetProperty("projectId").GetGuid().Should().Be(projectId);
        state.GetProperty("status").GetString().Should().Be(status.ToString());
        state.GetProperty("isBusy").GetBoolean().Should().BeTrue();
        state.GetProperty("sessionId").GetGuid().Should().Be(sessionId);
    }

    [Theory]
    [InlineData(RuntimeStatus.Stopped)]
    [InlineData(RuntimeStatus.Faulted)]
    public async Task StateEndpoint_ReturnsNotBusy_ForTerminalRuntimeStates(RuntimeStatus status)
    {
        await using var host = await InspectionEventTestHost.CreateAsync();
        var projectId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();

        await host.Coordinator.TryStartAsync(projectId, sessionId, CancellationToken.None);
        host.Coordinator.UpdateSessionStatus(projectId, sessionId, status);

        using var response = await host.Client.GetAsync($"/api/inspection/realtime/{projectId}/state");
        var state = await ReadJsonObjectAsync(response);

        response.EnsureSuccessStatusCode();
        state.GetProperty("status").GetString().Should().Be(status.ToString());
        state.GetProperty("isBusy").GetBoolean().Should().BeFalse();
        state.GetProperty("sessionId").GetGuid().Should().Be(sessionId);
    }

    [Fact]
    public async Task StateEndpoint_ReturnsIdle_ForUnknownProject()
    {
        await using var host = await InspectionEventTestHost.CreateAsync();
        var knownProjectId = Guid.NewGuid();
        var unknownProjectId = Guid.NewGuid();

        await host.Coordinator.TryStartAsync(knownProjectId, Guid.NewGuid(), CancellationToken.None);

        using var response = await host.Client.GetAsync($"/api/inspection/realtime/{unknownProjectId}/state");
        var state = await ReadJsonObjectAsync(response);

        response.EnsureSuccessStatusCode();
        state.GetProperty("projectId").GetGuid().Should().Be(unknownProjectId);
        state.GetProperty("status").GetString().Should().Be("Idle");
        state.GetProperty("isBusy").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task EventsEndpoint_StreamsLiveEvents_AsSseFrames()
    {
        await using var host = await InspectionEventTestHost.CreateAsync();
        var projectId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();

        await host.Coordinator.TryStartAsync(projectId, sessionId, CancellationToken.None);
        host.Coordinator.UpdateSessionStatus(projectId, sessionId, RuntimeStatus.Running);

        using var response = await host.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, $"/api/inspection/realtime/{projectId}/events"),
            HttpCompletionOption.ResponseHeadersRead);
        await using var stream = await response.Content.ReadAsStreamAsync();

        var initialChunk = await ReadUntilContainsAsync(stream, "event: stateChanged", TimeSpan.FromSeconds(2));
        initialChunk.Should().Contain("event: stateChanged");
        initialChunk.Should().NotContain("id:");
        initialChunk.Should().Contain("\"newState\":\"Running\"");
        initialChunk.Should().Contain("\"isSnapshot\":true");

        await host.EventBus.PublishAsync(new InspectionProgressEvent
        {
            ProjectId = projectId,
            SessionId = sessionId,
            ProcessedCount = 1
        });

        var eventChunk = await ReadUntilContainsAllAsync(
            stream,
            TimeSpan.FromSeconds(2),
            "id: 1",
            "event: progressChanged",
            "\"processedCount\":1");
        eventChunk.Should().Contain("id: 1");
        eventChunk.Should().Contain("event: progressChanged");
        eventChunk.Should().Contain("\"processedCount\":1");
    }

    [Fact]
    public async Task EventsEndpoint_ReplaysStoredEvents_UsingStableSequenceIds()
    {
        await using var host = await InspectionEventTestHost.CreateAsync();
        var projectId = Guid.NewGuid();

        var firstSequence = host.EventStore.Append(projectId, new InspectionProgressEvent
        {
            ProjectId = projectId,
            SessionId = Guid.NewGuid(),
            ProcessedCount = 1
        });
        var secondSequence = host.EventStore.Append(projectId, new InspectionProgressEvent
        {
            ProjectId = projectId,
            SessionId = Guid.NewGuid(),
            ProcessedCount = 2
        });

        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/inspection/realtime/{projectId}/events");
        request.Headers.Add("Last-Event-ID", firstSequence.ToString());

        using var response = await host.Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        await using var stream = await response.Content.ReadAsStreamAsync();

        var replayChunk = await ReadUntilContainsAllAsync(
            stream,
            TimeSpan.FromSeconds(2),
            $"id: {secondSequence}",
            "event: progressChanged",
            "\"processedCount\":2");
        replayChunk.Should().Contain($"id: {secondSequence}");
        replayChunk.Should().NotContain($"id: {firstSequence}\n");
        replayChunk.Should().Contain("event: progressChanged");
        replayChunk.Should().Contain("\"processedCount\":2");
    }

    [Theory]
    [InlineData("lastEventId")]
    [InlineData("afterSequence")]
    public async Task EventsEndpoint_ReplaysStoredEvents_UsingQueryCursor(string cursorName)
    {
        await using var host = await InspectionEventTestHost.CreateAsync();
        var projectId = Guid.NewGuid();

        var firstSequence = host.EventStore.Append(projectId, new InspectionProgressEvent
        {
            ProjectId = projectId,
            SessionId = Guid.NewGuid(),
            ProcessedCount = 1
        });
        var secondSequence = host.EventStore.Append(projectId, new InspectionProgressEvent
        {
            ProjectId = projectId,
            SessionId = Guid.NewGuid(),
            ProcessedCount = 2
        });

        using var response = await host.Client.SendAsync(
            new HttpRequestMessage(
                HttpMethod.Get,
                $"/api/inspection/realtime/{projectId}/events?{cursorName}={firstSequence}"),
            HttpCompletionOption.ResponseHeadersRead);
        await using var stream = await response.Content.ReadAsStreamAsync();

        var replayChunk = await ReadUntilContainsAllAsync(
            stream,
            TimeSpan.FromSeconds(2),
            $"id: {secondSequence}",
            "event: progressChanged",
            "\"processedCount\":2");
        replayChunk.Should().Contain($"id: {secondSequence}");
        replayChunk.Should().NotContain($"id: {firstSequence}\n");
    }

    [Fact]
    public async Task EventsEndpoint_DoesNotDropLiveEventsPublishedWhileReplayCompletes()
    {
        var projectId = Guid.NewGuid();
        var eventStore = new PublishOnReplayCompletedEventStore();
        var firstSequence = eventStore.Append(projectId, new InspectionProgressEvent
        {
            ProjectId = projectId,
            SessionId = Guid.NewGuid(),
            ProcessedCount = 1
        });

        await using var host = await InspectionEventTestHost.CreateAsync(eventStore: eventStore);
        eventStore.PublishWhenReplayEnumerationCompletes = () => host.EventBus.PublishAsync(new InspectionProgressEvent
        {
            ProjectId = projectId,
            SessionId = Guid.NewGuid(),
            ProcessedCount = 2
        });

        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/inspection/realtime/{projectId}/events");
        request.Headers.Add("Last-Event-ID", firstSequence.ToString());

        using var response = await host.Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        await using var stream = await response.Content.ReadAsStreamAsync();

        var liveChunk = await ReadUntilContainsAsync(stream, "event: progressChanged", TimeSpan.FromSeconds(2));
        liveChunk.Should().Contain("\"processedCount\":2");
    }

    [Fact]
    public async Task EventsEndpoint_AllowsAuthenticatedSseRequests_UsingAuthorizationHeader()
    {
        await using var host = await InspectionEventTestHost.CreateAsync(requireAuth: true);
        var projectId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();

        await host.Coordinator.TryStartAsync(projectId, sessionId, CancellationToken.None);
        host.Coordinator.UpdateSessionStatus(projectId, sessionId, RuntimeStatus.Running);

        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/inspection/realtime/{projectId}/events");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "test-token");

        using var response = await host.Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync();
        var chunk = await ReadUntilContainsAllAsync(
            stream,
            TimeSpan.FromSeconds(2),
            "event: stateChanged",
            "\"newState\":\"Running\"");
        chunk.Should().Contain("\"newState\":\"Running\"");
    }

    [Fact]
    public async Task EventsEndpoint_RejectsQueryToken_WhenAuthIsRequired()
    {
        await using var host = await InspectionEventTestHost.CreateAsync(requireAuth: true);
        var projectId = Guid.NewGuid();

        using var response = await host.Client.SendAsync(
            new HttpRequestMessage(
                HttpMethod.Get,
                $"/api/inspection/realtime/{projectId}/events?token=test-token"));

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.Unauthorized);
    }

    private static async Task<string> ReadUntilContainsAsync(Stream stream, string marker, TimeSpan timeout)
    {
        var buffer = new byte[512];
        var builder = new StringBuilder();
        using var cts = new CancellationTokenSource(timeout);

        while (!builder.ToString().Contains(marker, StringComparison.Ordinal))
        {
            var bytesRead = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cts.Token);
            bytesRead.Should().BeGreaterThan(0);
            builder.Append(Encoding.UTF8.GetString(buffer, 0, bytesRead));
        }

        return builder.ToString();
    }

    private static async Task<JsonElement> ReadJsonObjectAsync(HttpResponseMessage response)
    {
        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static async Task<string> ReadUntilContainsAllAsync(Stream stream, TimeSpan timeout, params string[] markers)
    {
        var buffer = new byte[512];
        var builder = new StringBuilder();
        using var cts = new CancellationTokenSource(timeout);

        while (markers.Any(marker => !builder.ToString().Contains(marker, StringComparison.Ordinal)))
        {
            var bytesRead = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cts.Token);
            bytesRead.Should().BeGreaterThan(0);
            builder.Append(Encoding.UTF8.GetString(buffer, 0, bytesRead));
        }

        return builder.ToString();
    }

    private sealed class InspectionEventTestHost : IAsyncDisposable
    {
        private readonly WebApplication _app;

        private InspectionEventTestHost(
            WebApplication app,
            IInspectionEventBus eventBus,
            IEventStore eventStore,
            IInspectionRuntimeCoordinator coordinator)
        {
            _app = app;
            EventBus = eventBus;
            EventStore = eventStore;
            Coordinator = coordinator;
            Client = app.GetTestClient();
        }

        public HttpClient Client { get; }
        public IInspectionEventBus EventBus { get; }
        public IEventStore EventStore { get; }
        public IInspectionRuntimeCoordinator Coordinator { get; }

        public static async Task<InspectionEventTestHost> CreateAsync(
            bool requireAuth = false,
            IEventStore? eventStore = null)
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                EnvironmentName = Environments.Development
            });

            builder.WebHost.UseTestServer();

            eventStore ??= new InMemoryEventStore(NullLogger<InMemoryEventStore>.Instance);
            var eventBus = new InMemoryInspectionEventBus(
                NullLogger<InMemoryInspectionEventBus>.Instance,
                eventStore);
            var coordinator = new InspectionRuntimeCoordinator(NullLogger<InspectionRuntimeCoordinator>.Instance);

            builder.Services.AddSingleton<IEventStore>(eventStore);
            builder.Services.AddSingleton<IInspectionEventBus>(eventBus);
            builder.Services.AddSingleton<IInspectionRuntimeCoordinator>(coordinator);

            if (requireAuth)
            {
                var authService = Substitute.For<IAuthService>();
                authService.GetSessionAsync("test-token").Returns(
                    Task.FromResult<ClearVision.Product.Application.Services.UserSession?>(new ClearVision.Product.Application.Services.UserSession
                    {
                        UserId = "user-1",
                        Username = "tester",
                        Role = "Admin",
                        ExpiresAt = DateTime.UtcNow.AddMinutes(30)
                    }));
                builder.Services.AddSingleton(authService);
            }

            var app = builder.Build();
            if (requireAuth)
            {
                app.UseMiddleware<AuthMiddleware>();
            }
            app.MapInspectionEventEndpoints();
            await app.StartAsync();

            return new InspectionEventTestHost(app, eventBus, eventStore, coordinator);
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }

    private sealed class PublishOnReplayCompletedEventStore : IEventStore
    {
        private readonly List<StoredInspectionEvent> _events = new();
        private long _nextSequenceId;
        private bool _published;

        public Func<Task>? PublishWhenReplayEnumerationCompletes { get; set; }

        public long Append(Guid projectId, IInspectionEvent evt)
        {
            var sequenceId = Interlocked.Increment(ref _nextSequenceId);
            _events.Add(new StoredInspectionEvent(sequenceId, evt, DateTime.UtcNow));
            return sequenceId;
        }

        public IReadOnlyList<StoredInspectionEvent> GetEventsAfter(Guid projectId, long sequenceId)
        {
            var replayEvents = _events
                .Where(item => item.Event.ProjectId == projectId && item.SequenceId > sequenceId)
                .OrderBy(item => item.SequenceId)
                .ToList();

            return new ReplayList(replayEvents, () =>
            {
                if (_published || PublishWhenReplayEnumerationCompletes == null)
                {
                    return;
                }

                _published = true;
                PublishWhenReplayEnumerationCompletes().GetAwaiter().GetResult();
            });
        }

        public void Cleanup(Guid projectId)
        {
            _events.RemoveAll(item => item.Event.ProjectId == projectId);
        }

        private sealed class ReplayList : IReadOnlyList<StoredInspectionEvent>
        {
            private readonly IReadOnlyList<StoredInspectionEvent> _inner;
            private readonly Action _onEnumerationCompleted;

            public ReplayList(IReadOnlyList<StoredInspectionEvent> inner, Action onEnumerationCompleted)
            {
                _inner = inner;
                _onEnumerationCompleted = onEnumerationCompleted;
            }

            public int Count => _inner.Count;

            public StoredInspectionEvent this[int index] => _inner[index];

            public IEnumerator<StoredInspectionEvent> GetEnumerator()
            {
                foreach (var item in _inner)
                {
                    yield return item;
                }

                _onEnumerationCompleted();
            }

            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
        }
    }
}
