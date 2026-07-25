using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using ClearVision.Product.Application.DTOs;
using ClearVision.Product.Application.Services;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Events;
using ClearVision.Product.Core.Interfaces;
using ClearVision.Product.Core.Services;
using ClearVision.Product.Desktop.Endpoints;
using ClearVision.Product.Desktop.Handlers;
using ClearVision.Product.Infrastructure.Events;
using ClearVision.Product.Infrastructure.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace ClearVision.Product.Desktop.Tests;

public sealed class RealtimeInspectionEndpointsTests
{
    [Fact]
    public async Task RealtimeSurfaces_ShouldRejectAuthenticatedOperator()
    {
        await using var host = await RealtimeInspectionEndpointHost.CreateAsync(UserRole.Operator);

        var responses = await SendAllSurfacesAsync(host);

        responses.Should().OnlyContain(response => response.StatusCode == HttpStatusCode.Forbidden);
        foreach (var response in responses)
        {
            response.Dispose();
        }
    }

    [Theory]
    [InlineData(UserRole.Engineer)]
    [InlineData(UserRole.Admin)]
    public async Task RealtimeSurfaces_ShouldAllowEngineerAndAdmin(UserRole role)
    {
        await using var host = await RealtimeInspectionEndpointHost.CreateAsync(role);

        var responses = await SendAllSurfacesAsync(host);

        responses.Should().OnlyContain(response => response.StatusCode == HttpStatusCode.OK);
        foreach (var response in responses)
        {
            response.Dispose();
        }
    }

    private static async Task<IReadOnlyList<HttpResponseMessage>> SendAllSurfacesAsync(
        RealtimeInspectionEndpointHost host)
    {
        var projectId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        await host.Coordinator.TryStartAsync(projectId, sessionId, CancellationToken.None);
        host.Coordinator.UpdateSessionStatus(projectId, sessionId, RuntimeStatus.Running);
        var responses = new List<HttpResponseMessage>
        {
            await host.Client.PostAsJsonAsync("/api/inspection/realtime/start", new StartRealtimeInspectionRequest
            {
                ProjectId = projectId,
                RunMode = "camera"
            }),
            await host.Client.PostAsJsonAsync("/api/inspection/realtime/stop", new StopRealtimeInspectionRequest
            {
                ProjectId = projectId
            }),
            await host.Client.GetAsync($"/api/inspection/realtime/{projectId}/state"),
            await host.Client.GetAsync("/api/inspection/realtime/diagnostics")
        };

        using var eventsRequest = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/inspection/realtime/{projectId}/events");
        responses.Add(await host.Client.SendAsync(eventsRequest, HttpCompletionOption.ResponseHeadersRead));
        return responses;
    }

    private sealed class RealtimeInspectionEndpointHost : IAsyncDisposable
    {
        private readonly WebApplication app;

        private RealtimeInspectionEndpointHost(
            WebApplication app,
            IInspectionRuntimeCoordinator coordinator)
        {
            this.app = app;
            Coordinator = coordinator;
            Client = app.GetTestClient();
        }

        public HttpClient Client { get; }
        public IInspectionRuntimeCoordinator Coordinator { get; }

        public static async Task<RealtimeInspectionEndpointHost> CreateAsync(UserRole role)
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                EnvironmentName = Environments.Development
            });
            builder.WebHost.UseTestServer();

            var service = Substitute.For<IInspectionService>();
            service.StartRealtimeInspectionAsync(
                    Arg.Any<Guid>(),
                    Arg.Any<string?>(),
                    Arg.Any<CancellationToken>(),
                    Arg.Any<Action<ClearVision.Product.Core.Entities.InspectionResult>?>())
                .Returns(Task.CompletedTask);
            service.StopRealtimeInspectionAsync(Arg.Any<Guid>()).Returns(Task.CompletedTask);

            var eventStore = new InMemoryEventStore(NullLogger<InMemoryEventStore>.Instance);
            var eventBus = new InMemoryInspectionEventBus(
                NullLogger<InMemoryInspectionEventBus>.Instance,
                eventStore);
            var coordinator = new InspectionRuntimeCoordinator(NullLogger<InspectionRuntimeCoordinator>.Instance);

            builder.Services.AddSingleton(service);
            builder.Services.AddSingleton<IOperatorFactory>(Substitute.For<IOperatorFactory>());
            builder.Services.AddSingleton<IEventStore>(eventStore);
            builder.Services.AddSingleton<IInspectionEventBus>(eventBus);
            builder.Services.AddSingleton<IInspectionRuntimeCoordinator>(coordinator);
            builder.Services.AddSingleton<WebMessageHandler>();

            var app = builder.Build();
            app.Use(async (context, next) =>
            {
                context.Items["CurrentUser"] = new UserSession
                {
                    UserId = $"realtime-{role.ToString().ToLowerInvariant()}",
                    Username = $"realtime-{role.ToString().ToLowerInvariant()}",
                    Role = role.ToString()
                };
                await next();
            });
            MapInspectionEndpoints(app);
            app.MapInspectionEventEndpoints();
            await app.StartAsync();
            return new RealtimeInspectionEndpointHost(app, coordinator);
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    private static void MapInspectionEndpoints(IEndpointRouteBuilder app)
    {
        var method = typeof(ApiEndpoints).GetMethod(
            "MapInspectionEndpoints",
            BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();
        method!.Invoke(null, [app]);
    }
}
