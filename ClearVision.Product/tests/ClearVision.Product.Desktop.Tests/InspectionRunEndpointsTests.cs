using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ClearVision.Product.Application.Analysis;
using ClearVision.Product.Application.DTOs;
using ClearVision.Product.Application.Services;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Events;
using ClearVision.Product.Core.Interfaces;
using ClearVision.Product.Core.Services;
using ClearVision.Product.Desktop.Endpoints;
using ClearVision.Product.Desktop.Handlers;
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

    [Fact]
    public async Task PersistedWorkspaceRun_StopAndReconcile_ReturnAuthoritativeStatusAndIdentity()
    {
        var identity = new StudioInspectionRunIdentity(
            Guid.NewGuid(),
            Guid.NewGuid(),
            9,
            "flow-hash",
            "decision-hash");
        var service = Substitute.For<IInspectionService>();
        service.StopPersistedStudioRunAsync(identity, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new StudioInspectionRunReconciliation(
                identity.ProjectId,
                identity.ClientSnapshotId,
                identity.PersistenceRevision,
                identity.CanonicalFlowHash,
                identity.DecisionConfigurationHash,
                StudioInspectionRunReconciliationStatus.Cancelled,
                "RUN_CANCELLED",
                "cancelled",
                null)));
        service.ReconcilePersistedStudioRunAsync(identity, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new StudioInspectionRunReconciliation(
                identity.ProjectId,
                identity.ClientSnapshotId,
                identity.PersistenceRevision,
                identity.CanonicalFlowHash,
                identity.DecisionConfigurationHash,
                StudioInspectionRunReconciliationStatus.IdentityMismatch,
                "RUN_IDENTITY_MISMATCH",
                "mismatch",
                null)));

        await using var host = await InspectionRunEndpointHost.CreateAsync(service);
        var request = new StudioInspectionRunIdentityRequest
        {
            ProjectId = identity.ProjectId,
            ClientSnapshotId = identity.ClientSnapshotId,
            ExpectedPersistenceRevision = identity.PersistenceRevision,
            ExpectedCanonicalFlowHash = identity.CanonicalFlowHash,
            ExpectedDecisionConfigurationHash = identity.DecisionConfigurationHash
        };

        using var stopResponse = await host.Client.PostAsJsonAsync("/api/inspection/stop", request);
        stopResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        using (var stop = JsonDocument.Parse(await stopResponse.Content.ReadAsStringAsync()))
        {
            stop.RootElement.GetProperty("status").GetString().Should().Be("cancelled");
            stop.RootElement.GetProperty("clientSnapshotId").GetGuid().Should().Be(identity.ClientSnapshotId);
            stop.RootElement.GetProperty("projectPersistenceRevision").GetInt64().Should().Be(identity.PersistenceRevision);
        }

        using var reconcileResponse = await host.Client.PostAsJsonAsync("/api/inspection/reconcile", request);
        reconcileResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        using (var reconcile = JsonDocument.Parse(await reconcileResponse.Content.ReadAsStringAsync()))
        {
            reconcile.RootElement.GetProperty("status").GetString().Should().Be("identity-mismatch");
            reconcile.RootElement.GetProperty("code").GetString().Should().Be("RUN_IDENTITY_MISMATCH");
        }

        await service.Received(1).StopPersistedStudioRunAsync(identity, Arg.Any<CancellationToken>());
        await service.Received(1).ReconcilePersistedStudioRunAsync(identity, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PersistedWorkspaceRun_StopAndReconcile_RejectIncompleteIdentity()
    {
        var service = Substitute.For<IInspectionService>();
        await using var host = await InspectionRunEndpointHost.CreateAsync(service);

        using var response = await host.Client.PostAsJsonAsync("/api/inspection/reconcile", new
        {
            projectId = Guid.NewGuid(),
            clientSnapshotId = Guid.NewGuid(),
            expectedPersistenceRevision = 1,
            expectedCanonicalFlowHash = "",
            expectedDecisionConfigurationHash = "decision-hash"
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("RUN_IDENTITY_INVALID");
        await service.DidNotReceiveWithAnyArgs().ReconcilePersistedStudioRunAsync(default!, default);
    }

    [Fact]
    public async Task PersistedWorkspaceRun_RunningStop_CancelsCoordinatorAndPersistsCancelledIdentity()
    {
        var project = new Project("concurrent-running-stop");
        project.SetPersistenceRevision(7);
        var projectId = project.Id;
        var clientSnapshotId = Guid.NewGuid();
        var flowJson = SerializeFormalFlow("deterministic-slow-execution");
        var resultRepository = Substitute.For<IInspectionResultRepository>();
        var projectRepository = Substitute.For<IProjectRepository>();
        var flowExecution = Substitute.For<IFlowExecutionService>();
        var flowStorage = Substitute.For<IProjectFlowStorage>();
        var admissionService = Substitute.For<IExecutionAdmissionService>();
        using var coordinator = new InspectionRuntimeCoordinator(NullLogger<InspectionRuntimeCoordinator>.Instance);
        var enteredExecution = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancellationObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        InspectionResult? persistedResult = null;

        async Task<FlowExecutionResult> ExecuteUntilCancelledAsync(CancellationToken cancellationToken)
        {
            enteredExecution.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                throw new InvalidOperationException("The deterministic execution fixture was released without cancellation.");
            }
            catch (OperationCanceledException)
            {
                cancellationObserved.TrySetResult();
                throw;
            }
        }

        projectRepository.GetWithFlowAsync(projectId).Returns(project);
        flowStorage.LoadFlowJsonAsync(projectId).Returns(flowJson);
        flowStorage.LoadMetadataAsync(projectId).Returns(new ProjectFlowStorageMetadata(
            1,
            projectId,
            7,
            ComputeStoredFlowArtifactHash(flowJson),
            DateTimeOffset.UtcNow));
        admissionService.ValidateSnapshot(Arg.Any<ExecutionSnapshot>(), ExecutionAdmissionSurface.StudioInspectionRun)
            .Returns(ExecutionAdmissionResult.Allow());
        flowExecution.ExecuteWithSnapshotAsync(
                Arg.Any<ExecutionSnapshot>(),
                Arg.Any<Dictionary<string, object>?>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo => ExecuteUntilCancelledAsync(callInfo.ArgAt<CancellationToken>(3)));
        resultRepository.AddAsync(Arg.Any<InspectionResult>()).Returns(callInfo =>
        {
            persistedResult = callInfo.Arg<InspectionResult>();
            return Task.FromResult(persistedResult);
        });
        resultRepository.FindByExecutionSnapshotIdAsync(projectId, clientSnapshotId)
            .Returns(_ => Task.FromResult(persistedResult));

        var service = new InspectionService(
            resultRepository,
            projectRepository,
            flowExecution,
            Substitute.For<IImageAcquisitionService>(),
            Substitute.For<IConfigurationService>(),
            coordinator,
            Substitute.For<IInspectionWorker>(),
            Substitute.For<IImageCacheRepository>(),
            new AnalysisDataBuilder(),
            flowStorage,
            NullLogger<InspectionService>.Instance,
            executionAdmissionService: admissionService);
        await using var host = await InspectionRunEndpointHost.CreateAsync(service);

        using var admissionResponse = await host.Client.PostAsJsonAsync("/api/inspection/admission",
            new StudioInspectionRunAdmissionRequest
            {
                ProjectId = projectId,
                ClientSnapshotId = clientSnapshotId,
                ExpectedPersistenceRevision = 7
            });
        admissionResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        using var admission = JsonDocument.Parse(await admissionResponse.Content.ReadAsStringAsync());
        var flowHash = admission.RootElement.GetProperty("canonicalFlowHash").GetString()!;
        var decisionHash = admission.RootElement.GetProperty("decisionConfigurationHash").GetString()!;
        var identityRequest = new StudioInspectionRunIdentityRequest
        {
            ProjectId = projectId,
            ClientSnapshotId = clientSnapshotId,
            ExpectedPersistenceRevision = 7,
            ExpectedCanonicalFlowHash = flowHash,
            ExpectedDecisionConfigurationHash = decisionHash
        };

        var executeTask = host.Client.PostAsJsonAsync("/api/inspection/execute", new ExecuteInspectionRequest
        {
            ProjectId = projectId,
            ClientSnapshotId = clientSnapshotId,
            ExpectedPersistenceRevision = 7,
            ExpectedCanonicalFlowHash = flowHash,
            ExpectedDecisionConfigurationHash = decisionHash
        });
        await enteredExecution.Task.WaitAsync(TimeSpan.FromSeconds(5));

        executeTask.IsCompleted.Should().BeFalse("Stop must occur before the execute response completes");
        coordinator.GetState(projectId).Should().Match<RuntimeState>(state =>
            state.Status == RuntimeStatus.Running &&
            state.ExecutionSnapshotId == clientSnapshotId &&
            state.ProjectRevision == 7 &&
            state.FlowHash == flowHash &&
            state.DecisionConfigurationHash == decisionHash);

        using var stopResponse = await host.Client.PostAsJsonAsync("/api/inspection/stop", identityRequest);
        stopResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        await cancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));

        using var executeResponse = await executeTask.WaitAsync(TimeSpan.FromSeconds(5));
        executeResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var executeContent = await executeResponse.Content.ReadAsStringAsync();
        using (var execute = JsonDocument.Parse(executeContent))
        {
            execute.RootElement.GetProperty("executionOutcome").GetString().Should().Be("Cancelled", "response={0}", executeContent);
            execute.RootElement.GetProperty("executionSnapshotId").GetGuid().Should().Be(clientSnapshotId);
            execute.RootElement.GetProperty("projectPersistenceRevision").GetInt64().Should().Be(7);
            execute.RootElement.GetProperty("flowVersionHash").GetString().Should().Be(flowHash);
            execute.RootElement.GetProperty("decisionConfigurationHash").GetString().Should().Be(decisionHash);
        }

        using var reconcileResponse = await host.Client.PostAsJsonAsync("/api/inspection/reconcile", identityRequest);
        reconcileResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        using (var reconciliation = JsonDocument.Parse(await reconcileResponse.Content.ReadAsStringAsync()))
        {
            reconciliation.RootElement.GetProperty("status").GetString().Should().Be("cancelled");
            var result = reconciliation.RootElement.GetProperty("result");
            result.GetProperty("executionOutcome").GetString().Should().Be("Cancelled");
            result.GetProperty("executionSnapshotId").GetGuid().Should().Be(clientSnapshotId);
            result.GetProperty("projectPersistenceRevision").GetInt64().Should().Be(7);
            result.GetProperty("flowVersionHash").GetString().Should().Be(flowHash);
            result.GetProperty("decisionConfigurationHash").GetString().Should().Be(decisionHash);
        }

        persistedResult.Should().NotBeNull();
        persistedResult!.GetOutcome().Execution.Should().Be(ClearVision.Product.Core.Outcomes.ExecutionOutcome.Cancelled);
        persistedResult.ExecutionSnapshotId.Should().Be(clientSnapshotId);
        persistedResult.ProjectPersistenceRevision.Should().Be(7);
        persistedResult.FlowVersionHash.Should().Be(flowHash);
        persistedResult.DecisionConfigurationHash.Should().Be(decisionHash);
        await resultRepository.Received(1).AddAsync(Arg.Is<InspectionResult>(result =>
            result.ExecutionSnapshotId == clientSnapshotId &&
            result.GetOutcome().Execution == ClearVision.Product.Core.Outcomes.ExecutionOutcome.Cancelled));
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

    private static string SerializeFormalFlow(string operatorName)
    {
        var operatorId = Guid.NewGuid();
        var outputPortId = Guid.NewGuid();
        var dto = new OperatorFlowDto
        {
            Name = "concurrent-stop-flow",
            DecisionConfiguration = new ClearVision.Product.Core.Decisions.DecisionConfiguration
            {
                FinalDecisionBinding = new ClearVision.Product.Core.Decisions.FinalDecisionBinding
                {
                    SourceOperatorId = operatorId,
                    SourceOutputPortId = outputPortId,
                    SourceOutputName = "JudgmentResult",
                    DataType = ClearVision.Product.Core.Decisions.DecisionValueType.String,
                    Rule = ClearVision.Product.Core.Decisions.DecisionInterpretationRule.StringMap,
                    OkValue = "OK",
                    NgValue = "NG"
                }
            },
            Operators =
            [
                new OperatorDto
                {
                    Id = operatorId,
                    Name = operatorName,
                    Type = OperatorType.ResultJudgment,
                    X = 0,
                    Y = 0,
                    OutputPorts =
                    [
                        new PortDto
                        {
                            Id = outputPortId,
                            Name = "JudgmentResult",
                            Direction = PortDirection.Output,
                            DataType = PortDataType.String
                        }
                    ]
                }
            ]
        };

        return JsonSerializer.Serialize(dto, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
        });
    }

    private static string ComputeStoredFlowArtifactHash(string flowJson)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(flowJson));
        return "sha256:" + Convert.ToHexString(hash).ToLowerInvariant();
    }
}
