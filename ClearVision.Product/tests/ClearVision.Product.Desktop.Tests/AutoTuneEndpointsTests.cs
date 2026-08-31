using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using ClearVision.Product.Application.Services;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Interfaces;
using ClearVision.Product.Core.ProjectVariables;
using ClearVision.Product.Core.Services;
using ClearVision.Product.Desktop.Endpoints;
using ClearVision.Product.Desktop.Middleware;
using ClearVision.Product.Infrastructure.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace ClearVision.Product.Desktop.Tests;

[TestClassification(TestDomain.Desktop, TestPurpose.Regression, TestLane.Pr, TestEvidenceType.Contract, TestOracleType.Contract, TestResourceRequirement.None, TestExpectedDuration.Fast, TestFlakyPolicy.Blocking, "desktop", Suites = "DesktopEndpoints")]

public class AutoTuneEndpointsTests
{
    [Fact]
    public async Task StrategiesEndpoint_ShouldWorkWithDesktopAuthMiddleware()
    {
        var previewService = Substitute.For<IFlowNodePreviewService>();
        var autoTuneService = Substitute.For<IAutoTuneService>();
        var authService = Substitute.For<IAuthService>();

        authService.GetSessionAsync("desktop-token").Returns(Task.FromResult<ClearVision.Product.Application.Services.UserSession?>(new ClearVision.Product.Application.Services.UserSession
        {
            UserId = Guid.NewGuid().ToString(),
            Username = "tester",
            Role = "Engineer",
            ExpiresAt = DateTime.UtcNow.AddMinutes(30)
        }));

        await using var host = await AutoTuneEndpointTestHost.CreateWithDesktopAuthAsync(
            previewService,
            autoTuneService,
            authService);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/autotune/strategies");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "desktop-token");

        using var response = await host.Client.SendAsync(request);

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        document.RootElement.ValueKind.Should().Be(JsonValueKind.Array);
        document.RootElement.GetArrayLength().Should().BeGreaterThan(0);
    }

    [Theory]
    [InlineData("/api/autotune/operator")]
    [InlineData("/api/autotune/flow-node")]
    public async Task RemovedAutoTuneExecutionRoutes_ShouldReturnNotFoundWithoutDispatch(string route)
    {
        var previewService = Substitute.For<IFlowNodePreviewService>();
        var autoTuneService = Substitute.For<IAutoTuneService>();
        await using var host = await AutoTuneEndpointTestHost.CreateAsync(previewService, autoTuneService);

        using var response = await host.Client.PostAsJsonAsync(route, new { });

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.NotFound);
        await previewService.DidNotReceiveWithAnyArgs().PreviewWithMetricsAsync(
            Arg.Any<OperatorFlow>(),
            Arg.Any<Guid>(),
            Arg.Any<byte[]?>(),
            Arg.Any<Guid>(),
            Arg.Any<long>(),
            Arg.Any<ExecutionRequestAuthority>(),
            Arg.Any<ProjectVariableExecutionContext?>(),
            Arg.Any<CancellationToken>());
        await autoTuneService.DidNotReceiveWithAnyArgs().AutoTuneScenarioAsync(
            Arg.Any<string>(),
            Arg.Any<OperatorFlow>(),
            Arg.Any<byte[]>(),
            Arg.Any<AutoTuneGoal>(),
            Arg.Any<Guid>(),
            Arg.Any<long>(),
            Arg.Any<ExecutionRequestAuthority>(),
            Arg.Any<int>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OperatorRole_ShouldNotExecuteRetainedAutoTuneSurfaces()
    {
        var nodeId = Guid.NewGuid();
        var previewService = Substitute.For<IFlowNodePreviewService>();
        var autoTuneService = Substitute.For<IAutoTuneService>();
        await using var host = await AutoTuneEndpointTestHost.CreateAsync(
            previewService,
            autoTuneService,
            role: "Operator");

        using var previewResponse = await host.Client.PostAsJsonAsync(
            "/api/autotune/flow-node/preview",
            CreatePreviewRequest(host.Project, nodeId, OperatorType.DetectionSequenceJudge));
        using var scenarioResponse = await host.Client.PostAsJsonAsync(
            "/api/autotune/scenario",
            CreateScenarioRequest(host.Project, nodeId));

        previewResponse.StatusCode.Should().Be(System.Net.HttpStatusCode.Forbidden);
        scenarioResponse.StatusCode.Should().Be(System.Net.HttpStatusCode.Forbidden);
        await previewService.DidNotReceiveWithAnyArgs().PreviewWithMetricsAsync(
            Arg.Any<OperatorFlow>(),
            Arg.Any<Guid>(),
            Arg.Any<byte[]?>(),
            Arg.Any<Guid>(),
            Arg.Any<long>(),
            Arg.Any<ExecutionRequestAuthority>(),
            Arg.Any<ProjectVariableExecutionContext?>(),
            Arg.Any<CancellationToken>());
        await autoTuneService.DidNotReceiveWithAnyArgs().AutoTuneScenarioAsync(
            Arg.Any<string>(),
            Arg.Any<OperatorFlow>(),
            Arg.Any<byte[]>(),
            Arg.Any<AutoTuneGoal>(),
            Arg.Any<Guid>(),
            Arg.Any<long>(),
            Arg.Any<ExecutionRequestAuthority>(),
            Arg.Any<int>(),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("missing-project", "ADMISSION_DRAFT_REVISION_REQUIRED")]
    [InlineData("missing-revision", "ADMISSION_DRAFT_REVISION_REQUIRED")]
    [InlineData("missing-confirmation", "ADMISSION_DRAFT_CONFIRMATION_REQUIRED")]
    [InlineData("invalid-audit", "ADMISSION_DRAFT_CONFIRMATION_REQUIRED")]
    [InlineData("same-authority-ids", "ADMISSION_DRAFT_CONFIRMATION_REQUIRED")]
    [InlineData("missing-capabilities", "ADMISSION_DRAFT_CAPABILITY_CONFIRMATION_REQUIRED")]
    [InlineData("forged-capabilities", "ADMISSION_CAPABILITY_MANIFEST_MISMATCH")]
    [InlineData("unknown-project", "ADMISSION_PROJECT_NOT_ACTIVE")]
    public async Task FlowNodePreview_ShouldRejectInvalidDraftAuthorityBeforeDispatch(
        string mutation,
        string expectedCode)
    {
        var nodeId = Guid.NewGuid();
        var previewService = Substitute.For<IFlowNodePreviewService>();
        var autoTuneService = Substitute.For<IAutoTuneService>();
        await using var host = await AutoTuneEndpointTestHost.CreateAsync(previewService, autoTuneService);
        var request = CreatePreviewRequest(host.Project, nodeId, OperatorType.DetectionSequenceJudge);

        switch (mutation)
        {
            case "missing-project":
                request.ProjectId = Guid.Empty;
                break;
            case "missing-revision":
                request.ExpectedProjectRevision = null;
                break;
            case "missing-confirmation":
                request.ConfirmationId = null;
                break;
            case "invalid-audit":
                request.AuditId = "not-a-uuid";
                break;
            case "same-authority-ids":
                request.AuditId = request.ConfirmationId;
                break;
            case "missing-capabilities":
                request.DeclaredCapabilities = null;
                break;
            case "forged-capabilities":
                request.DeclaredCapabilities = ExecutionSideEffect.FileRead;
                break;
            case "unknown-project":
                request.ProjectId = Guid.NewGuid();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(mutation), mutation, null);
        }

        using var response = await host.Client.PostAsJsonAsync("/api/autotune/flow-node/preview", request);
        var payload = await response.Content.ReadAsStringAsync();

        response.IsSuccessStatusCode.Should().BeFalse(payload);
        payload.Should().Contain(expectedCode);
        await previewService.DidNotReceiveWithAnyArgs().PreviewWithMetricsAsync(
            Arg.Any<OperatorFlow>(),
            Arg.Any<Guid>(),
            Arg.Any<byte[]?>(),
            Arg.Any<Guid>(),
            Arg.Any<long>(),
            Arg.Any<ExecutionRequestAuthority>(),
            Arg.Any<ProjectVariableExecutionContext?>(),
            Arg.Any<CancellationToken>());
        await autoTuneService.DidNotReceiveWithAnyArgs().AutoTuneScenarioAsync(
            Arg.Any<string>(),
            Arg.Any<OperatorFlow>(),
            Arg.Any<byte[]>(),
            Arg.Any<AutoTuneGoal>(),
            Arg.Any<Guid>(),
            Arg.Any<long>(),
            Arg.Any<ExecutionRequestAuthority>(),
            Arg.Any<int>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RetainedAutoTuneSurfaces_ShouldRejectDeletedOrStaleProjectBeforeDispatch()
    {
        var nodeId = Guid.NewGuid();
        var previewService = Substitute.For<IFlowNodePreviewService>();
        var autoTuneService = Substitute.For<IAutoTuneService>();
        await using var host = await AutoTuneEndpointTestHost.CreateAsync(previewService, autoTuneService);

        var staleScenario = CreateScenarioRequest(host.Project, nodeId);
        staleScenario.ExpectedProjectRevision = host.Project.PersistenceRevision + 1;
        using var staleResponse = await host.Client.PostAsJsonAsync("/api/autotune/scenario", staleScenario);
        var stalePayload = await staleResponse.Content.ReadAsStringAsync();
        staleResponse.StatusCode.Should().Be(System.Net.HttpStatusCode.Conflict, stalePayload);
        stalePayload.Should().Contain("ADMISSION_DRAFT_REVISION_STALE");

        host.Project.MarkAsDeleted();
        var deletedPreview = CreatePreviewRequest(host.Project, nodeId, OperatorType.DetectionSequenceJudge);
        using var deletedResponse = await host.Client.PostAsJsonAsync("/api/autotune/flow-node/preview", deletedPreview);
        var deletedPayload = await deletedResponse.Content.ReadAsStringAsync();
        deletedResponse.StatusCode.Should().Be(System.Net.HttpStatusCode.BadRequest, deletedPayload);
        deletedPayload.Should().Contain("ADMISSION_PROJECT_NOT_ACTIVE");

        await previewService.DidNotReceiveWithAnyArgs().PreviewWithMetricsAsync(
            Arg.Any<OperatorFlow>(),
            Arg.Any<Guid>(),
            Arg.Any<byte[]?>(),
            Arg.Any<Guid>(),
            Arg.Any<long>(),
            Arg.Any<ExecutionRequestAuthority>(),
            Arg.Any<ProjectVariableExecutionContext?>(),
            Arg.Any<CancellationToken>());
        await autoTuneService.DidNotReceiveWithAnyArgs().AutoTuneScenarioAsync(
            Arg.Any<string>(),
            Arg.Any<OperatorFlow>(),
            Arg.Any<byte[]>(),
            Arg.Any<AutoTuneGoal>(),
            Arg.Any<Guid>(),
            Arg.Any<long>(),
            Arg.Any<ExecutionRequestAuthority>(),
            Arg.Any<int>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FlowNodePreview_ShouldReturnMetricsDiagnosticCodesAndSuggestions()
    {
        var targetNodeId = Guid.NewGuid();
        var previewService = Substitute.For<IFlowNodePreviewService>();
        var autoTuneService = Substitute.For<IAutoTuneService>();

        previewService.PreviewWithMetricsAsync(
                Arg.Any<OperatorFlow>(),
                Arg.Any<Guid>(),
                Arg.Any<byte[]?>(),
                Arg.Any<Guid>(),
                Arg.Any<long>(),
                Arg.Any<ExecutionRequestAuthority>(),
                Arg.Any<ProjectVariableExecutionContext?>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new FlowNodePreviewWithMetricsResult
            {
                Success = true,
                TargetNodeId = targetNodeId,
                PreviewImage = new byte[] { 1, 2, 3 },
                Outputs = new Dictionary<string, object>
                {
                    ["IsMatch"] = false,
                    ["ExpectedLabels"] = new[] { "Wire_Brown", "Wire_Black", "Wire_Blue" },
                    ["ActualOrder"] = new[] { "Wire_Brown", "Wire_Blue" }
                },
                Metrics = new PreviewMetrics
                {
                    OverallScore = 0.72,
                    Diagnostics = new List<string> { PreviewDiagnosticTags.MissingExpectedClass }
                },
                DiagnosticCodes = new List<string> { "missing_expected_class" },
                Suggestions = new List<ParameterSuggestion>
                {
                    new()
                    {
                        ParameterName = "BoxNms.ScoreThreshold",
                        SuggestedValue = "decrease",
                        Reason = "当前数量低于预期",
                        ExpectedImprovement = "保留更多候选框"
                    }
                }
            }));

        await using var host = await AutoTuneEndpointTestHost.CreateAsync(previewService, autoTuneService);

        var request = CreatePreviewRequest(host.Project, targetNodeId, OperatorType.DetectionSequenceJudge);
        request.InputImageBase64 = Convert.ToBase64String(new byte[] { 9, 9, 9 });
        using var response = await host.Client.PostAsJsonAsync("/api/autotune/flow-node/preview", request);

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        document.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
        document.RootElement.GetProperty("targetNodeId").GetGuid().Should().Be(targetNodeId);
        document.RootElement.GetProperty("previewImageBase64").GetString().Should().Be(Convert.ToBase64String(new byte[] { 1, 2, 3 }));
        document.RootElement.GetProperty("diagnosticCodes")[0].GetString().Should().Be("missing_expected_class");
        document.RootElement.GetProperty("suggestions")[0].GetProperty("parameterName").GetString().Should().Be("BoxNms.ScoreThreshold");
        document.RootElement.GetProperty("metrics").GetProperty("overallScore").GetDouble().Should().BeApproximately(0.72d, 0.001d);
    }

    [Fact]
    public async Task FlowNodePreview_ShouldReturnMissingResources()
    {
        var targetNodeId = Guid.NewGuid();
        var previewService = Substitute.For<IFlowNodePreviewService>();
        var autoTuneService = Substitute.For<IAutoTuneService>();

        previewService.PreviewWithMetricsAsync(
                Arg.Any<OperatorFlow>(),
                Arg.Any<Guid>(),
                Arg.Any<byte[]?>(),
                Arg.Any<Guid>(),
                Arg.Any<long>(),
                Arg.Any<ExecutionRequestAuthority>(),
                Arg.Any<ProjectVariableExecutionContext?>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new FlowNodePreviewWithMetricsResult
            {
                Success = false,
                TargetNodeId = targetNodeId,
                ErrorMessage = "线序预览缺少必要资源",
                DiagnosticCodes = new List<string> { "missing_model", "missing_labels" },
                MissingResources = new List<PreviewMissingResource>
                {
                    new()
                    {
                        ResourceType = "Model",
                        ResourceKey = "DeepLearning.ModelPath",
                        Description = "缺少模型文件路径",
                        DiagnosticCode = "missing_model"
                    },
                    new()
                    {
                        ResourceType = "Label",
                        ResourceKey = "DeepLearning.LabelsPath",
                        Description = "缺少标签文件路径",
                        DiagnosticCode = "missing_labels"
                    }
                }
            }));

        await using var host = await AutoTuneEndpointTestHost.CreateAsync(previewService, autoTuneService);

        using var response = await host.Client.PostAsJsonAsync(
            "/api/autotune/flow-node/preview",
            CreatePreviewRequest(host.Project, targetNodeId, OperatorType.DetectionSequenceJudge));

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        document.RootElement.GetProperty("success").GetBoolean().Should().BeFalse();
        document.RootElement.GetProperty("missingResources").GetArrayLength().Should().Be(2);
        document.RootElement.GetProperty("diagnosticCodes").EnumerateArray().Select(item => item.GetString()).Should()
            .Contain(new[] { "missing_model", "missing_labels" });
    }

    [Fact]
    public async Task FlowNodePreview_ShouldRejectSideEffectTargetBeforePreviewService()
    {
        var targetNodeId = Guid.NewGuid();
        var previewService = Substitute.For<IFlowNodePreviewService>();
        var autoTuneService = Substitute.For<IAutoTuneService>();

        await using var host = await AutoTuneEndpointTestHost.CreateAsync(previewService, autoTuneService);

        using var response = await host.Client.PostAsJsonAsync(
            "/api/autotune/flow-node/preview",
            CreatePreviewRequest(host.Project, targetNodeId, OperatorType.HttpRequest));

        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.BadRequest, payload);
        payload.Should().Contain("ADMISSION_AUTOTUNE_PREVIEW_SIDE_EFFECT_BLOCKED");
        payload.Should().Contain("NetworkWrite");
        await previewService.DidNotReceiveWithAnyArgs().PreviewWithMetricsAsync(
            Arg.Any<OperatorFlow>(),
            Arg.Any<Guid>(),
            Arg.Any<byte[]?>(),
            Arg.Any<Guid>(),
            Arg.Any<long>(),
            Arg.Any<ExecutionRequestAuthority>(),
            Arg.Any<ProjectVariableExecutionContext?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FlowNodePreview_ShouldRejectUpstreamSideEffectBeforePreviewService()
    {
        var sideEffectNodeId = Guid.NewGuid();
        var targetNodeId = Guid.NewGuid();
        var sourcePortId = Guid.NewGuid();
        var targetPortId = Guid.NewGuid();
        var previewService = Substitute.For<IFlowNodePreviewService>();
        var autoTuneService = Substitute.For<IAutoTuneService>();

        await using var host = await AutoTuneEndpointTestHost.CreateAsync(previewService, autoTuneService);

        using var response = await host.Client.PostAsJsonAsync(
            "/api/autotune/flow-node/preview",
            WithAuthority(new FlowNodePreviewRequest
        {
            FlowId = Guid.NewGuid(),
            TargetNodeId = targetNodeId,
            FlowData = new FlowDataDto
            {
                Id = Guid.NewGuid(),
                Name = "AutoTuneAdmissionFlow",
                Operators =
                [
                    new CanvasOperatorDataDto
                    {
                        Id = sideEffectNodeId,
                        Name = "TextSave",
                        Type = "TextSave",
                        OutputPorts =
                        [
                            new CanvasPortDataDto
                            {
                                Id = sourcePortId,
                                Name = "FilePath",
                                DataType = "String"
                            }
                        ],
                        Parameters = new Dictionary<string, object>
                        {
                            ["FilePath"] = "should-not-write.txt"
                        }
                    },
                    new CanvasOperatorDataDto
                    {
                        Id = targetNodeId,
                        Name = "DetectionSequenceJudge",
                        Type = "DetectionSequenceJudge",
                        InputPorts =
                        [
                            new CanvasPortDataDto
                            {
                                Id = targetPortId,
                                Name = "Data",
                                DataType = "Any",
                                IsRequired = false
                            }
                        ]
                    }
                ],
                Connections =
                [
                    new FlowConnectionDto
                    {
                        Id = Guid.NewGuid(),
                        SourceOperatorId = sideEffectNodeId,
                        SourcePortId = sourcePortId,
                        TargetOperatorId = targetNodeId,
                        TargetPortId = targetPortId
                    }
                ]
            }
        }, host.Project));

        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.BadRequest, payload);
        payload.Should().Contain("ADMISSION_AUTOTUNE_PREVIEW_SIDE_EFFECT_BLOCKED");
        payload.Should().Contain("TextSave");
        await previewService.DidNotReceiveWithAnyArgs().PreviewWithMetricsAsync(
            Arg.Any<OperatorFlow>(),
            Arg.Any<Guid>(),
            Arg.Any<byte[]?>(),
            Arg.Any<Guid>(),
            Arg.Any<long>(),
            Arg.Any<ExecutionRequestAuthority>(),
            Arg.Any<ProjectVariableExecutionContext?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FlowNodePreview_ShouldRejectCanvasSerializedFileAcquisitionBeforePreviewService()
    {
        var acquisitionId = Guid.NewGuid();
        var targetNodeId = Guid.NewGuid();
        OperatorFlow? capturedFlow = null;
        var previewService = Substitute.For<IFlowNodePreviewService>();
        var autoTuneService = Substitute.For<IAutoTuneService>();
        using var acquisitionParametersJson = JsonDocument.Parse("""
            [
              { "name": "SourceType", "value": "File", "dataType": "enum" },
              { "name": "FilePath", "value": "demo.png", "dataType": "file" }
            ]
            """);

        var acquisitionOutputId = Guid.NewGuid();
        var targetInputId = Guid.NewGuid();

        previewService.PreviewWithMetricsAsync(
                Arg.Any<OperatorFlow>(),
                Arg.Any<Guid>(),
                Arg.Any<byte[]?>(),
                Arg.Any<Guid>(),
                Arg.Any<long>(),
                Arg.Any<ExecutionRequestAuthority>(),
                Arg.Any<ProjectVariableExecutionContext?>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                capturedFlow = callInfo.ArgAt<OperatorFlow>(0);
                return Task.FromResult(new FlowNodePreviewWithMetricsResult
                {
                    Success = true,
                    TargetNodeId = targetNodeId
                });
            });

        await using var host = await AutoTuneEndpointTestHost.CreateAsync(previewService, autoTuneService);

        using var response = await host.Client.PostAsJsonAsync("/api/autotune/flow-node/preview", new
        {
            projectId = host.Project.Id,
            expectedProjectRevision = host.Project.PersistenceRevision,
            declaredCapabilities = ExecutionSideEffect.FileRead,
            confirmationId = Guid.NewGuid().ToString(),
            auditId = Guid.NewGuid().ToString(),
            flowId = Guid.NewGuid(),
            targetNodeId = targetNodeId,
            flowData = new
            {
                id = Guid.NewGuid(),
                name = "CanvasPreviewFlow",
                operators = new object[]
                {
                    new
                    {
                        id = acquisitionId,
                        name = "Acquire",
                        type = "ImageAcquisition",
                        x = 0,
                        y = 0,
                        parameters = acquisitionParametersJson.RootElement.Clone(),
                        outputPorts = new[]
                        {
                            new
                            {
                                id = acquisitionOutputId,
                                name = "Image",
                                dataType = "Image",
                                isRequired = false
                            }
                        }
                    },
                    new
                    {
                        id = targetNodeId,
                        name = "Resize",
                        type = "ImageResize",
                        x = 10,
                        y = 10,
                        inputPorts = new[]
                        {
                            new
                            {
                                id = targetInputId,
                                name = "Image",
                                dataType = "Image",
                                isRequired = true
                            }
                        }
                    }
                },
                connections = new object[]
                {
                    new
                    {
                        sourceOperatorId = acquisitionId,
                        sourcePortId = acquisitionOutputId,
                        targetOperatorId = targetNodeId,
                        targetPortId = targetInputId
                    }
                }
            }
        });

        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.BadRequest, payload);
        payload.Should().Contain("ADMISSION_AUTOTUNE_PREVIEW_SIDE_EFFECT_BLOCKED");
        payload.Should().Contain("FilePath");
        capturedFlow.Should().BeNull();
        await previewService.DidNotReceiveWithAnyArgs().PreviewWithMetricsAsync(
            Arg.Any<OperatorFlow>(),
            Arg.Any<Guid>(),
            Arg.Any<byte[]?>(),
            Arg.Any<Guid>(),
            Arg.Any<long>(),
            Arg.Any<ExecutionRequestAuthority>(),
            Arg.Any<ProjectVariableExecutionContext?>(),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(OperatorType.HttpRequest)]
    [InlineData(OperatorType.DatabaseWrite)]
    [InlineData(OperatorType.TcpCommunication)]
    public async Task ScenarioAutoTune_ShouldRejectExternalIoBeforeService(OperatorType operatorType)
    {
        var nodeId = Guid.NewGuid();
        var previewService = Substitute.For<IFlowNodePreviewService>();
        var autoTuneService = Substitute.For<IAutoTuneService>();
        await using var host = await AutoTuneEndpointTestHost.CreateAsync(previewService, autoTuneService);
        var request = CreateScenarioRequest(host.Project, nodeId);
        request.FlowData = CreateFlowData(nodeId, operatorType);

        using var response = await host.Client.PostAsJsonAsync("/api/autotune/scenario", request);

        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.BadRequest, payload);
        payload.Should().Contain("ADMISSION_AUTOTUNE_PREVIEW_SIDE_EFFECT_BLOCKED");
        await autoTuneService.DidNotReceiveWithAnyArgs().AutoTuneScenarioAsync(
            Arg.Any<string>(),
            Arg.Any<OperatorFlow>(),
            Arg.Any<byte[]>(),
            Arg.Any<AutoTuneGoal>(),
            Arg.Any<Guid>(),
            Arg.Any<long>(),
            Arg.Any<ExecutionRequestAuthority>(),
            Arg.Any<int>(),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("Camera", null)]
    [InlineData("File", "model-input.png")]
    public async Task ScenarioAutoTune_ShouldRejectRawAcquisitionAuthorityBeforeService(
        string sourceType,
        string? filePath)
    {
        var nodeId = Guid.NewGuid();
        var previewService = Substitute.For<IFlowNodePreviewService>();
        var autoTuneService = Substitute.For<IAutoTuneService>();
        await using var host = await AutoTuneEndpointTestHost.CreateAsync(previewService, autoTuneService);
        var request = CreateScenarioRequest(host.Project, nodeId);
        request.FlowData = CreateImageAcquisitionFlowData(nodeId, sourceType, filePath);

        using var response = await host.Client.PostAsJsonAsync("/api/autotune/scenario", request);

        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.BadRequest, payload);
        payload.Should().Contain("ADMISSION_AUTOTUNE_PREVIEW_SIDE_EFFECT_BLOCKED");
        await autoTuneService.DidNotReceiveWithAnyArgs().AutoTuneScenarioAsync(
            Arg.Any<string>(),
            Arg.Any<OperatorFlow>(),
            Arg.Any<byte[]>(),
            Arg.Any<AutoTuneGoal>(),
            Arg.Any<Guid>(),
            Arg.Any<long>(),
            Arg.Any<ExecutionRequestAuthority>(),
            Arg.Any<int>(),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(6)]
    [InlineData(1000)]
    public async Task ScenarioAutoTune_ShouldRejectIterationLimitBeforeService(int maxIterations)
    {
        var nodeId = Guid.NewGuid();
        var previewService = Substitute.For<IFlowNodePreviewService>();
        var autoTuneService = Substitute.For<IAutoTuneService>();
        await using var host = await AutoTuneEndpointTestHost.CreateAsync(previewService, autoTuneService);
        var request = CreateScenarioRequest(host.Project, nodeId);
        request.MaxIterations = maxIterations;

        using var response = await host.Client.PostAsJsonAsync("/api/autotune/scenario", request);

        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.BadRequest, payload);
        payload.Should().Contain("AUTOTUNE_ITERATION_LIMIT_EXCEEDED");
        await autoTuneService.DidNotReceiveWithAnyArgs().AutoTuneScenarioAsync(
            Arg.Any<string>(),
            Arg.Any<OperatorFlow>(),
            Arg.Any<byte[]>(),
            Arg.Any<AutoTuneGoal>(),
            Arg.Any<Guid>(),
            Arg.Any<long>(),
            Arg.Any<ExecutionRequestAuthority>(),
            Arg.Any<int>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ScenarioAutoTune_ShouldRejectConcurrentRequestWithoutDispatchAndReleaseLease()
    {
        var nodeId = Guid.NewGuid();
        var previewService = Substitute.For<IFlowNodePreviewService>();
        var autoTuneService = Substitute.For<IAutoTuneService>();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var gate = new AutoTuneExecutionGate(1, 1, TimeSpan.FromSeconds(5));

        autoTuneService.AutoTuneScenarioAsync(
                Arg.Any<string>(),
                Arg.Any<OperatorFlow>(),
                Arg.Any<byte[]>(),
                Arg.Any<AutoTuneGoal>(),
                Arg.Any<Guid>(),
                Arg.Any<long>(),
                Arg.Any<ExecutionRequestAuthority>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns(async callInfo =>
            {
                entered.TrySetResult();
                await release.Task.WaitAsync(callInfo.Arg<CancellationToken>());
                return new ScenarioAutoTuneResult
                {
                    Success = true,
                    ScenarioKey = "wire-sequence-terminal"
                };
            });

        await using var host = await AutoTuneEndpointTestHost.CreateAsync(
            previewService,
            autoTuneService,
            executionGate: gate);

        var firstRequest = host.Client.PostAsJsonAsync("/api/autotune/scenario", CreateScenarioRequest(host.Project, nodeId));
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        using var secondResponse = await host.Client.PostAsJsonAsync("/api/autotune/scenario", CreateScenarioRequest(host.Project, nodeId));

        var secondPayload = await secondResponse.Content.ReadAsStringAsync();
        secondResponse.StatusCode.Should().Be(System.Net.HttpStatusCode.TooManyRequests, secondPayload);
        secondPayload.Should().Contain("AUTOTUNE_CONCURRENCY_LIMIT_EXCEEDED");
        await autoTuneService.Received(1).AutoTuneScenarioAsync(
            Arg.Any<string>(),
            Arg.Any<OperatorFlow>(),
            Arg.Any<byte[]>(),
            Arg.Any<AutoTuneGoal>(),
            Arg.Any<Guid>(),
            Arg.Any<long>(),
            Arg.Any<ExecutionRequestAuthority>(),
            Arg.Any<int>(),
            Arg.Any<CancellationToken>());

        release.TrySetResult();
        using var firstResponse = await firstRequest.WaitAsync(TimeSpan.FromSeconds(5));
        firstResponse.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        gate.ActiveCount.Should().Be(0);
    }

    [Fact]
    public async Task FlowNodePreview_ShouldEnforceServerDeadlineAndPassCancellationToken()
    {
        var nodeId = Guid.NewGuid();
        var previewService = Substitute.For<IFlowNodePreviewService>();
        var autoTuneService = Substitute.For<IAutoTuneService>();
        var observedToken = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        var gate = new AutoTuneExecutionGate(1, 1, TimeSpan.FromMilliseconds(50));

        previewService.PreviewWithMetricsAsync(
                Arg.Any<OperatorFlow>(),
                Arg.Any<Guid>(),
                Arg.Any<byte[]?>(),
                Arg.Any<Guid>(),
                Arg.Any<long>(),
                Arg.Any<ExecutionRequestAuthority>(),
                Arg.Any<ProjectVariableExecutionContext?>(),
                Arg.Any<CancellationToken>())
            .Returns(async callInfo =>
            {
                var token = callInfo.Arg<CancellationToken>();
                observedToken.TrySetResult(token);
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return new FlowNodePreviewWithMetricsResult();
            });

        await using var host = await AutoTuneEndpointTestHost.CreateAsync(
            previewService,
            autoTuneService,
            executionGate: gate);

        using var response = await host.Client.PostAsJsonAsync(
            "/api/autotune/flow-node/preview",
            CreatePreviewRequest(host.Project, nodeId, OperatorType.DetectionSequenceJudge));

        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.RequestTimeout, payload);
        payload.Should().Contain("AUTOTUNE_DEADLINE_EXCEEDED");
        (await observedToken.Task.WaitAsync(TimeSpan.FromSeconds(5))).IsCancellationRequested.Should().BeTrue();
        gate.ActiveCount.Should().Be(0);
    }

    [Fact]
    public async Task ScenarioAutoTune_ShouldPropagateRequestAbortedToService()
    {
        var nodeId = Guid.NewGuid();
        var previewService = Substitute.For<IFlowNodePreviewService>();
        var autoTuneService = Substitute.For<IAutoTuneService>();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancellationObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var gate = new AutoTuneExecutionGate(1, 1, TimeSpan.FromSeconds(10));

        autoTuneService.AutoTuneScenarioAsync(
                Arg.Any<string>(),
                Arg.Any<OperatorFlow>(),
                Arg.Any<byte[]>(),
                Arg.Any<AutoTuneGoal>(),
                Arg.Any<Guid>(),
                Arg.Any<long>(),
                Arg.Any<ExecutionRequestAuthority>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns(async callInfo =>
            {
                var token = callInfo.Arg<CancellationToken>();
                using var registration = token.Register(() => cancellationObserved.TrySetResult());
                entered.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return new ScenarioAutoTuneResult();
            });

        await using var host = await AutoTuneEndpointTestHost.CreateAsync(
            previewService,
            autoTuneService,
            executionGate: gate);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/autotune/scenario")
        {
            Content = JsonContent.Create(CreateScenarioRequest(host.Project, nodeId))
        };
        using var requestCancellation = new CancellationTokenSource();

        var sendTask = host.Client.SendAsync(request, requestCancellation.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        requestCancellation.Cancel();

        await cancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            using var response = await sendTask;
            response.StatusCode.Should().Be((System.Net.HttpStatusCode)499);
        }
        catch (OperationCanceledException)
        {
            // TestServer may surface the client cancellation instead of the endpoint's 499 response.
        }
    }

    [Fact]
    public async Task ScenarioAutoTune_ShouldReturnFinalPreviewAndParameters()
    {
        var targetNodeId = Guid.NewGuid();
        var previewService = Substitute.For<IFlowNodePreviewService>();
        var autoTuneService = Substitute.For<IAutoTuneService>();

        autoTuneService.AutoTuneScenarioAsync(
                Arg.Any<string>(),
                Arg.Any<OperatorFlow>(),
                Arg.Any<byte[]>(),
                Arg.Any<AutoTuneGoal>(),
                Arg.Any<Guid>(),
                Arg.Any<long>(),
                Arg.Any<ExecutionRequestAuthority>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ScenarioAutoTuneResult
            {
                Success = true,
                ScenarioKey = "wire-sequence-terminal",
                FinalParameters = new Dictionary<string, object>
                {
                    ["BoxNms.ScoreThreshold"] = 0.2d,
                    ["BoxNms.IouThreshold"] = 0.4d
                },
                TotalIterations = 2,
                TotalExecutionTimeMs = 30,
                IsGoalAchieved = true,
                DiagnosticCodes = new List<string>(),
                FinalPreview = new FlowNodePreviewWithMetricsResult
                {
                    Success = true,
                    TargetNodeId = targetNodeId,
                    Outputs = new Dictionary<string, object>
                    {
                        ["IsMatch"] = true
                    },
                    Suggestions = new List<ParameterSuggestion>
                    {
                        new()
                        {
                            ParameterName = "BoxNms.IouThreshold",
                            SuggestedValue = "decrease",
                            Reason = "收紧重复框",
                            ExpectedImprovement = "减少同类重复框"
                        }
                    }
                }
            }));

        await using var host = await AutoTuneEndpointTestHost.CreateAsync(previewService, autoTuneService);

        using var response = await host.Client.PostAsJsonAsync(
            "/api/autotune/scenario",
            CreateScenarioRequest(host.Project, targetNodeId));

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        document.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
        document.RootElement.GetProperty("scenarioKey").GetString().Should().Be("wire-sequence-terminal");
        document.RootElement.GetProperty("finalParameters").GetProperty("BoxNms.ScoreThreshold").GetDouble().Should().BeApproximately(0.2d, 0.001d);
        document.RootElement.GetProperty("finalParameters").GetProperty("BoxNms.IouThreshold").GetDouble().Should().BeApproximately(0.4d, 0.001d);
        document.RootElement.GetProperty("finalPreview").GetProperty("success").GetBoolean().Should().BeTrue();
    }

    private static ScenarioAutoTuneRequest CreateScenarioRequest(Project project, Guid nodeId) =>
        WithAuthority(new ScenarioAutoTuneRequest
        {
            ScenarioKey = "wire-sequence-terminal",
            InputImageBase64 = Convert.ToBase64String([7, 8, 9]),
            MaxIterations = 5,
            FlowData = CreateFlowData(nodeId, OperatorType.DetectionSequenceJudge)
        }, project);

    private static FlowNodePreviewRequest CreatePreviewRequest(
        Project project,
        Guid nodeId,
        OperatorType type) =>
        WithAuthority(new FlowNodePreviewRequest
        {
            FlowId = Guid.NewGuid(),
            TargetNodeId = nodeId,
            FlowData = CreateFlowData(nodeId, type)
        }, project);

    private static T WithAuthority<T>(
        T request,
        Project project,
        ExecutionSideEffect capabilities = ExecutionSideEffect.None)
        where T : AutoTuneDraftAuthorityRequest
    {
        request.ProjectId = project.Id;
        request.ExpectedProjectRevision = project.PersistenceRevision;
        request.DeclaredCapabilities = capabilities;
        request.ConfirmationId = Guid.NewGuid().ToString();
        request.AuditId = Guid.NewGuid().ToString();
        return request;
    }

    private static FlowDataDto CreateImageAcquisitionFlowData(
        Guid nodeId,
        string sourceType,
        string? filePath)
    {
        var parameters = new Dictionary<string, object>
        {
            ["SourceType"] = sourceType
        };
        if (!string.IsNullOrWhiteSpace(filePath))
        {
            parameters["FilePath"] = filePath;
        }

        return new FlowDataDto
        {
            Id = Guid.NewGuid(),
            Name = "RawAcquisitionAuthority",
            Operators =
            [
                new CanvasOperatorDataDto
                {
                    Id = nodeId,
                    Name = "Acquire",
                    Type = "ImageAcquisition",
                    Parameters = parameters
                }
            ]
        };
    }

    private static FlowDataDto CreateFlowData(Guid nodeId, OperatorType type)
    {
        return new FlowDataDto
        {
            Id = Guid.NewGuid(),
            Name = "WireSequenceFlow",
            Nodes =
            [
                new FlowNodeDto
                {
                    Id = nodeId,
                    Name = type.ToString(),
                    Type = type,
                    Position = new PositionDto()
                }
            ]
        };
    }

    private sealed class AutoTuneEndpointTestHost : IAsyncDisposable
    {
        private readonly WebApplication _app;

        private AutoTuneEndpointTestHost(WebApplication app, HttpClient client, Project project)
        {
            _app = app;
            Client = client;
            Project = project;
        }

        public HttpClient Client { get; }
        public Project Project { get; }

        public static async Task<AutoTuneEndpointTestHost> CreateAsync(
            IFlowNodePreviewService previewService,
            IAutoTuneService autoTuneService,
            string role = "Engineer",
            AutoTuneExecutionGate? executionGate = null,
            Project? project = null)
        {
            project ??= new Project("AutoTune endpoint project");
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                EnvironmentName = Environments.Development
            });

            builder.WebHost.UseTestServer();
            builder.Services.AddLogging();
            builder.Services.AddSingleton(previewService);
            builder.Services.AddSingleton(autoTuneService);
            builder.Services.AddSingleton(executionGate ?? new AutoTuneExecutionGate());
            builder.Services.AddSingleton(Substitute.For<IPreviewMetricsAnalyzer>());
            builder.Services.AddSingleton(new TestPrincipal(role));
            var projectRepository = Substitute.For<IProjectRepository>();
            projectRepository.GetByIdFreshAsync(project.Id).Returns(project);
            builder.Services.AddSingleton(projectRepository);
            builder.Services.AddSingleton<IExecutionAdmissionService>(new ExecutionAdmissionService(projectRepository));
            builder.Services
                .AddAuthentication(TestAuthHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });
            builder.Services.AddAuthorization();

            var app = builder.Build();
            app.UseAuthentication();
            app.UseAuthorization();
            app.MapAutoTuneEndpoints();
            await app.StartAsync();

            return new AutoTuneEndpointTestHost(app, app.GetTestClient(), project);
        }

        public static async Task<AutoTuneEndpointTestHost> CreateWithDesktopAuthAsync(
            IFlowNodePreviewService previewService,
            IAutoTuneService autoTuneService,
            IAuthService authService)
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                EnvironmentName = Environments.Development
            });

            builder.WebHost.UseTestServer();
            builder.Services.AddLogging();
            builder.Services.AddSingleton(previewService);
            builder.Services.AddSingleton(autoTuneService);
            builder.Services.AddSingleton(new AutoTuneExecutionGate());
            builder.Services.AddSingleton(Substitute.For<IPreviewMetricsAnalyzer>());
            var projectRepository = Substitute.For<IProjectRepository>();
            var project = new Project("AutoTune desktop auth project");
            projectRepository.GetByIdFreshAsync(project.Id).Returns(project);
            builder.Services.AddSingleton(projectRepository);
            builder.Services.AddSingleton<IExecutionAdmissionService>(new ExecutionAdmissionService(projectRepository));
            builder.Services.AddSingleton(authService);

            var app = builder.Build();
            app.UseMiddleware<AuthMiddleware>();
            app.MapAutoTuneEndpoints();
            await app.StartAsync();

            return new AutoTuneEndpointTestHost(app, app.GetTestClient(), project);
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }

    private sealed class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public const string SchemeName = "Test";

        public TestAuthHandler(
            IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder,
            TestPrincipal principal)
            : base(options, logger, encoder)
        {
            _principal = principal;
        }

        private readonly TestPrincipal _principal;

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, "test-user"),
                new Claim(ClaimTypes.Name, "test-user"),
                new Claim(ClaimTypes.Role, _principal.Role)
            ], SchemeName);
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, SchemeName);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }

    private sealed record TestPrincipal(string Role);
}
