using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using ClearVision.Product.Application.DTOs;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Events;
using ClearVision.Product.Core.Services;
using ClearVision.Product.Desktop.Endpoints;
using ClearVision.Product.Desktop.Handlers;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;

namespace ClearVision.Product.Desktop.Tests;

public sealed class InspectionRunEndpointsTests
{
    [Fact]
    public async Task PersistedWorkspaceRun_AdmissionAndExecute_EnforceIdentityAndRejectRawFlow()
    {
        var projectId = Guid.NewGuid();
        var clientSnapshotId = Guid.NewGuid();
        var service = Substitute.For<IInspectionService>();
        service.AdmitPersistedStudioRunAsync(projectId, 7, clientSnapshotId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new StudioInspectionRunAdmission(
                projectId,
                clientSnapshotId,
                7,
                "persisted-flow-hash",
                "persisted-decision-hash")));
        var result = new InspectionResult(projectId);
        result.SetResult(InspectionStatus.OK, 12);
        service.ExecutePersistedStudioRunAsync(
                projectId,
                7,
                clientSnapshotId,
                "persisted-flow-hash",
                "persisted-decision-hash",
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(result));

        await using var host = await InspectionRunEndpointHost.CreateAsync(service);

        using var admissionResponse = await host.Client.PostAsJsonAsync("/api/inspection/admission",
            new StudioInspectionRunAdmissionRequest
            {
                ProjectId = projectId,
                ClientSnapshotId = clientSnapshotId,
                ExpectedPersistenceRevision = 7
            });
        var admissionContent = await admissionResponse.Content.ReadAsStringAsync();
        admissionResponse.StatusCode.Should().Be(HttpStatusCode.OK, "response={0}", admissionContent);
        using (var admission = JsonDocument.Parse(admissionContent))
        {
            admission.RootElement.GetProperty("allowed").GetBoolean().Should().BeTrue();
            admission.RootElement.GetProperty("projectId").GetGuid().Should().Be(projectId);
            admission.RootElement.GetProperty("clientSnapshotId").GetGuid().Should().Be(clientSnapshotId);
            admission.RootElement.GetProperty("projectPersistenceRevision").GetInt64().Should().Be(7);
            admission.RootElement.GetProperty("canonicalFlowHash").GetString().Should().Be("persisted-flow-hash");
            admission.RootElement.GetProperty("decisionConfigurationHash").GetString().Should().Be("persisted-decision-hash");
        }
        await service.Received(1).AdmitPersistedStudioRunAsync(
            projectId,
            7,
            clientSnapshotId,
            Arg.Any<CancellationToken>());

        using var rawFlowResponse = await host.Client.PostAsJsonAsync("/api/inspection/execute",
            new ExecuteInspectionRequest
            {
                ProjectId = projectId,
                ClientSnapshotId = clientSnapshotId,
                ExpectedPersistenceRevision = 7,
                ExpectedCanonicalFlowHash = "persisted-flow-hash",
                ExpectedDecisionConfigurationHash = "persisted-decision-hash",
                FlowData = new OperatorFlowDto()
            });
        rawFlowResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await rawFlowResponse.Content.ReadAsStringAsync()).Should().Contain("ADMISSION_PERSISTED_SNAPSHOT_REQUIRED");
        await service.DidNotReceive().ExecutePersistedStudioRunAsync(
            Arg.Any<Guid>(),
            Arg.Any<long>(),
            Arg.Any<Guid>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());

        using var executeResponse = await host.Client.PostAsJsonAsync("/api/inspection/execute",
            new ExecuteInspectionRequest
            {
                ProjectId = projectId,
                ClientSnapshotId = clientSnapshotId,
                ExpectedPersistenceRevision = 7,
                ExpectedCanonicalFlowHash = "persisted-flow-hash",
                ExpectedDecisionConfigurationHash = "persisted-decision-hash"
            });
        executeResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        using (var execution = JsonDocument.Parse(await executeResponse.Content.ReadAsStringAsync()))
        {
            execution.RootElement.GetProperty("projectId").GetGuid().Should().Be(projectId);
            execution.RootElement.GetProperty("executionOutcome").GetString().Should().Be("Succeeded");
            execution.RootElement.GetProperty("decisionOutcome").GetString().Should().Be("Ok");
        }
        await service.Received(1).ExecutePersistedStudioRunAsync(
            projectId,
            7,
            clientSnapshotId,
            "persisted-flow-hash",
            "persisted-decision-hash",
            Arg.Any<CancellationToken>());
    }

    private sealed class InspectionRunEndpointHost : IAsyncDisposable
    {
        private readonly WebApplication app;

        private InspectionRunEndpointHost(WebApplication app)
        {
            this.app = app;
            Client = app.GetTestClient();
        }

        public HttpClient Client { get; }

        public static async Task<InspectionRunEndpointHost> CreateAsync(IInspectionService service)
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                EnvironmentName = Environments.Development
            });
            builder.WebHost.UseTestServer();
            builder.Services.AddSingleton(service);
            builder.Services.AddSingleton(Substitute.For<IOperatorFactory>());
            builder.Services.AddSingleton(Substitute.For<IInspectionEventBus>());
            builder.Services.AddSingleton<WebMessageHandler>();

            var app = builder.Build();
            app.UseDeveloperExceptionPage();
            MapInspectionEndpoints(app);
            await app.StartAsync();
            return new InspectionRunEndpointHost(app);
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
