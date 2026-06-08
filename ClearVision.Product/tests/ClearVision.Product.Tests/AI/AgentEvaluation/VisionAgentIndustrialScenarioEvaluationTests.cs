using System.Text.Json;
using ClearVision.Product.Application.DTOs;
using ClearVision.Product.Contracts.Messages;
using ClearVision.Product.Core.AI.Tools;
using ClearVision.Product.Core.DTOs;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Services;
using ClearVision.Product.Infrastructure.AI.Agent;
using ClearVision.Product.Infrastructure.AI.AgentRun;
using ClearVision.Product.Infrastructure.AI.Runtime;
using ClearVision.Product.Infrastructure.AI.Tools;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace ClearVision.Product.Tests.AI.AgentEvaluation;

public sealed class VisionAgentIndustrialScenarioEvaluationTests
{
    [Fact(DisplayName = "Scenario eval: metal scratch detection should use real surface defect schema")]
    public async Task MetalScratchScenario_ShouldBuildRealSurfaceDefectDraft()
    {
        var (plan, request, result, sink) = await RunScenarioAsync(
            "金属表面划痕检测，先生成可编辑流程草稿，资源稍后补齐。",
            buildIntent: "new");

        AssertPlanQuality(plan);
        AssertBuildRequestPreservesPlanContext(request);
        AssertCommonBuildQuality(result, sink);
        result.BuildResult!.OperatorPipeline.Select(item => item.OperatorType)
            .Should().Contain(["ImageAcquisition", "SurfaceDefectDetection", "BlobAnalysis", "ResultJudgment", "ResultOutput"]);
        AssertWorkflowUsesOnlyRealOperatorTypes(result);
        AssertWorkflowDoesNotContainParameter(result, "ModelId", "TemplatePath", "Rule", "Channel");
        result.BuildResult.MissingResources.Should().Contain(item => item.ResourceType == "camera_binding");
        result.BuildResult.MissingResources.Should().Contain(item => item.ResourceType == "output_channel");
    }

    [Fact(DisplayName = "Scenario eval: terminal wire sequence should keep model and sequence labels pending")]
    public async Task WireSequenceScenario_ShouldBuildDetectionSequenceDraft()
    {
        var (plan, request, result, sink) = await RunScenarioAsync(
            "端子线序检测，按从左到右的端子标签顺序判定 OK/NG。",
            buildIntent: "new",
            userSelections: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["sequence_rule"] = "left_to_right"
            });

        AssertPlanQuality(plan);
        AssertBuildRequestPreservesPlanContext(request);
        AssertCommonBuildQuality(result, sink);
        result.BuildResult!.OperatorPipeline.Select(item => item.OperatorType)
            .Should().Contain(["ImageAcquisition", "DeepLearning", "DetectionSequenceJudge", "ResultJudgment", "ResultOutput"]);
        result.BuildResult.MissingResources.Should().Contain(item =>
            item.ResourceType == "model_resource" &&
            item.ResourceKey == "op_detect.ModelPath");
        result.BuildResult.ParameterMapping.Should().Contain(item =>
            item.OperatorType == "DetectionSequenceJudge" &&
            item.ParameterName == "ExpectedLabels" &&
            item.Pending);
        AssertWorkflowDoesNotContainParameter(result, "Rule", "Channel");
    }

    [Fact(DisplayName = "Scenario eval: hole distance measurement should keep calibration scale pending")]
    public async Task HoleDistanceScenario_ShouldBuildCalibratedMeasurementDraft()
    {
        var (plan, request, result, sink) = await RunScenarioAsync(
            "孔距/圆心距测量，需要输出毫米单位，但标定比例稍后填写。",
            buildIntent: "new",
            userSelections: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["measurement_target"] = "hole_distance",
                ["calibration_policy"] = "calibration_pending"
            });

        AssertPlanQuality(plan);
        AssertBuildRequestPreservesPlanContext(request);
        AssertCommonBuildQuality(result, sink);
        result.BuildResult!.OperatorPipeline.Select(item => item.OperatorType)
            .Should().Contain(["ImageAcquisition", "CircleMeasurement", "Measurement", "UnitConvert", "ResultJudgment", "ResultOutput"]);
        result.BuildResult.MissingResources.Should().Contain(item =>
            item.ResourceType == "measurement_parameter" &&
            item.ResourceKey == "op_calibration.Scale");
        result.BuildResult.PendingParameters.Should().Contain(item =>
            item.OperatorId == "op_calibration" &&
            item.ParameterNames.Contains("Scale"));
    }

    [Fact(DisplayName = "Scenario eval: template matching should bind template artifact through real Template input")]
    public async Task TemplateMatchingScenario_ShouldBuildTemplateResourceTask()
    {
        var selection = new AiTemplateSelectionInfo
        {
            Mode = "use_selected_template",
            TemplateId = "template_matching_alignment",
            ScenarioKey = "template_matching"
        };
        var (plan, request, result, sink) = await RunScenarioAsync(
            "模板定位/模板匹配，对位后输出匹配分数。",
            buildIntent: "new",
            templateSelection: selection);

        AssertPlanQuality(plan);
        AssertBuildRequestPreservesPlanContext(request, expectTemplateSelection: true);
        AssertCommonBuildQuality(result, sink);
        result.GenerationMode.Should().Be("template_fill");
        result.BuildResult!.OperatorPipeline.Select(item => item.OperatorType)
            .Should().Contain(["ImageAcquisition", "TemplateMatching", "ResultJudgment", "ResultOutput"]);
        result.BuildResult.MissingResources.Should().Contain(item =>
            item.ResourceType == "template_artifact" &&
            item.ResourceKey == "op_match.Template");
        AssertWorkflowDoesNotContainParameter(result, "TemplatePath");
    }

    [Fact(DisplayName = "Scenario eval: remote control appearance should expose template and model resource tasks")]
    public async Task RemoteControlScenario_ShouldBuildAppearanceInspectionDraft()
    {
        var (plan, request, result, sink) = await RunScenarioAsync(
            "遥控器外观检测，检查按键缺失、按下状态和面板外观。",
            buildIntent: "new");

        AssertPlanQuality(plan);
        AssertBuildRequestPreservesPlanContext(request);
        AssertCommonBuildQuality(result, sink);
        result.BuildResult!.OperatorPipeline.Select(item => item.OperatorType)
            .Should().Contain(["ImageAcquisition", "TemplateMatching", "RoiManager", "DeepLearning", "ResultJudgment", "ResultOutput"]);
        result.BuildResult.MissingResources.Should().Contain(item => item.ResourceType == "template_artifact");
        result.BuildResult.MissingResources.Should().Contain(item => item.ResourceType == "model_resource");
    }

    [Fact(DisplayName = "Scenario eval: modify existing flow should append defect detection and preserve old nodes")]
    public async Task ModifyExistingFlowScenario_ShouldPreserveExistingNodes()
    {
        var existingFlow = ExistingAcquisitionJudgmentFlow();
        var (plan, request, result, sink) = await RunScenarioAsync(
            "修改已有流程：在现有采集+判定流程中追加缺陷检测，不得清空旧节点。",
            buildIntent: "modify",
            currentFlowSnapshot: JsonSerializer.Serialize(existingFlow));

        AssertPlanQuality(plan);
        AssertBuildRequestPreservesPlanContext(request, expectCurrentFlow: true);
        AssertCommonBuildQuality(result, sink);
        result.BuildResult!.WorkflowDiff.PreservedNodes.Should().HaveCountGreaterThanOrEqualTo(existingFlow.Operators.Count);
        Flow(result).Operators.Select(item => item.Name).Should().Contain("existing-camera");
        Flow(result).Operators.Count.Should().BeGreaterThan(existingFlow.Operators.Count);
        result.BuildResult.OperatorPipeline.Select(item => item.OperatorType)
            .Should().Contain("SurfaceDefectDetection");
    }

    private static async Task<(VisionAgentPlanModeResult Plan, AiFlowGenerationRequest Request, AiFlowGenerationResult Result, CapturingAgentRunEventSink Sink)> RunScenarioAsync(
        string prompt,
        string buildIntent,
        AiTemplateSelectionInfo? templateSelection = null,
        string? currentFlowSnapshot = null,
        Dictionary<string, string>? userSelections = null)
    {
        var sink = new CapturingAgentRunEventSink();
        var orchestrator = CreateOrchestrator(sink);
        var plan = await orchestrator.CreatePlanAsync(
            new VisionAgentPlanModeRequest
            {
                Description = prompt,
                OriginalUserPrompt = prompt,
                CurrentFlowSnapshot = currentFlowSnapshot,
                TemplateSelection = templateSelection,
                AttachmentSummary = new VisionAgentAttachmentSummary
                {
                    Count = 1,
                    ResourceKinds = ["sample_image_metadata"],
                    PathsRedacted = true
                }
            },
            CancellationToken.None);
        var request = new AiFlowGenerationRequest(prompt, Mode: GenerateFlowModeExtensions.ParseOrAuto(buildIntent))
        {
            AgentRunId = $"ar_eval_{Guid.NewGuid():N}",
            UseVisionAgentGenerateFlow = true,
            BuildFromPlan = new VisionAgentBuildFromPlanRequest
            {
                PlanId = plan.PlanId,
                PlanHash = plan.PlanHash,
                PlanSnapshot = plan,
                UserSelections = userSelections ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                AcceptedDefaults = plan.RecommendedDefaults.Select(item => item.Id).ToList(),
                CurrentFlowSnapshot = currentFlowSnapshot,
                TemplateSelection = templateSelection,
                AttachmentSummary = new VisionAgentAttachmentSummary
                {
                    Count = 1,
                    ResourceKinds = ["sample_image_metadata"],
                    PathsRedacted = true
                },
                OperatorCatalogVersion = plan.OperatorCatalogVersion,
                StationBoundarySummary = plan.StationBoundarySummary,
                PlcOutputPolicy = plan.PlcOutputPolicy,
                BuildIntent = buildIntent,
                OriginalUserPrompt = prompt,
                AcceptedRecommendedDefaults = true,
                MetadataOnly = true
            }
        };

        var result = await orchestrator.BuildFromPlanAsync(request, CancellationToken.None);
        return (plan, request, result, sink);
    }

    private static VisionAgentOrchestrator CreateOrchestrator(CapturingAgentRunEventSink sink)
    {
        var redactor = new AgentRunEventRedactor();
        var toolRunner = new BuildToolRunner(CreateToolRegistry(), redactor, sink);
        var buildOrchestrator = new VisionAgentBuildOrchestrator(
            new BuildPlanContextLoader(sink),
            new BuildIntentResolver(),
            new TemplateStrategyResolver(toolRunner),
            new OperatorPipelineSelector(),
            new ParameterMappingService(),
            new WorkflowDraftBuilder(new FakeAiFlowGenerationService()),
            toolRunner,
            new BuildReadinessReviewService(),
            new WorkflowDiffService(),
            new ApplyGateResolver(),
            new BuildResultAssembler(redactor, sink),
            NullLogger<VisionAgentBuildOrchestrator>.Instance,
            sink);
        return new VisionAgentOrchestrator(
            CreateToolRegistry(),
            new FakeAiFlowGenerationService(),
            sink,
            buildOrchestrator);
    }

    private static VisionAgentToolRegistry CreateToolRegistry()
    {
        return new VisionAgentToolRegistry(
        [
            new OperatorCatalogTool(),
            new OperatorSchemaTool(),
            new FlowTemplateMatchTool(),
            new FlowTemplateSkeletonTool(),
            new CurrentFlowInspectTool(),
            new FlowValidationTool(),
            new DryRunFlowTool(),
            new RuntimePackagePrecheckTool()
        ]);
    }

    private static void AssertPlanQuality(VisionAgentPlanModeResult plan)
    {
        plan.PlanHash.Should().NotBeNullOrWhiteSpace();
        plan.ClarificationQuestions.Should().NotBeEmpty();
        plan.RecommendedDefaults.Should().NotBeEmpty();
        plan.RecommendedRoute.Operators.Should().NotBeEmpty();
        plan.NextAction.Should().Contain("构建");
        plan.MetadataOnly.Should().BeTrue();
    }

    private static void AssertBuildRequestPreservesPlanContext(
        AiFlowGenerationRequest request,
        bool expectTemplateSelection = false,
        bool expectCurrentFlow = false)
    {
        request.BuildFromPlan.Should().NotBeNull();
        request.BuildFromPlan!.PlanHash.Should().Be(request.BuildFromPlan.PlanSnapshot!.PlanHash);
        request.BuildFromPlan.UserSelections.Should().NotBeNull();
        request.BuildFromPlan.AcceptedDefaults.Should().NotBeEmpty();
        if (expectTemplateSelection)
        {
            request.BuildFromPlan.TemplateSelection.Should().NotBeNull();
        }

        if (expectCurrentFlow)
        {
            request.BuildFromPlan.CurrentFlowSnapshot.Should().NotBeNullOrWhiteSpace();
        }
    }

    private static void AssertCommonBuildQuality(
        AiFlowGenerationResult result,
        CapturingAgentRunEventSink sink)
    {
        result.Success.Should().BeTrue();
        result.BuildResult.Should().NotBeNull();
        result.BuildResult!.ValidationPreview.Should().NotBeNull();
        result.BuildResult.WorkflowDiff.ValidationFailures.Should().BeEmpty();
        result.BuildResult.ApplyGate.CanvasApplyReady.Should().BeTrue();
        result.BuildResult.ApplyGate.RuntimeDraftReady.Should().BeTrue();
        result.BuildResult.ApplyGate.DeploymentReady.Should().BeFalse();
        result.BuildResult.ApplyGate.DeploymentBlockers.Should().NotBeEmpty();
        result.BuildResult.FirstFixRecommendation.Should().NotBeNullOrWhiteSpace();
        result.BuildResult.FirstFixRecommendation.Should().MatchRegex("[\u4e00-\u9fa5]");
        result.BuildResult.ToolEvidenceTimeline.Select(item => item.Stage)
            .Should().Contain(["validate_schema", "metadata_dry_run", "package_readiness", "workflow_diff", "apply_gate"]);
        var flow = Flow(result);
        flow.Operators.Should().NotBeEmpty();
        flow.Connections.Should().NotBeEmpty();
        var act = () => flow.ToEntity();
        act.Should().NotThrow("generated canvas flow should deserialize into runtime entities");
        AssertWorkflowUsesOnlyRealOperatorTypes(result);
        AssertNoSensitiveLeak(result, sink);
    }

    private static void AssertWorkflowUsesOnlyRealOperatorTypes(AiFlowGenerationResult result)
    {
        Flow(result).Operators.Select(item => item.Type)
            .Should().OnlyContain(type => Enum.IsDefined(typeof(OperatorType), type));
        result.BuildResult!.OperatorPipeline.Select(item => item.OperatorType)
            .Should().NotContain(["MeasureDistance", "TemplateMatch", "ImageCompose"]);
    }

    private static void AssertWorkflowDoesNotContainParameter(
        AiFlowGenerationResult result,
        params string[] parameterNames)
    {
        var names = Flow(result).Operators
            .SelectMany(item => item.Parameters)
            .Select(item => item.Name)
            .ToList();
        names.Should().NotContain(parameterNames);
    }

    private static void AssertNoSensitiveLeak(
        AiFlowGenerationResult result,
        CapturingAgentRunEventSink sink)
    {
        var publicJson = JsonSerializer.Serialize(new { result.BuildResult, sink.Events }, AgentRunEventJson.Options);
        publicJson.Should().NotContain("systemPrompt");
        publicJson.Should().NotContain("rawPrompt");
        publicJson.Should().NotContain("chainOfThought");
        publicJson.Should().NotContain("C:\\");
        publicJson.Should().NotContain("D:\\");
        publicJson.Should().NotContain("192.168.");
        publicJson.Should().NotContain("DB1.DB");
        publicJson.Should().NotContain("sk-");
        publicJson.Should().NotContain("data:image");
        publicJson.Should().NotContain(";base64");
    }

    private static OperatorFlowDto Flow(AiFlowGenerationResult result)
    {
        return result.Flow.Should().BeOfType<OperatorFlowDto>().Subject;
    }

    private static OperatorFlowDto ExistingAcquisitionJudgmentFlow()
    {
        var cameraId = Guid.NewGuid();
        var cameraOutput = Guid.NewGuid();
        var judgeId = Guid.NewGuid();
        var judgeInput = Guid.NewGuid();
        return new OperatorFlowDto
        {
            Id = Guid.NewGuid(),
            Name = "Existing acquisition and judgment flow",
            Operators =
            [
                new OperatorDto
                {
                    Id = cameraId,
                    Name = "existing-camera",
                    Type = OperatorType.ImageAcquisition,
                    OutputPorts =
                    [
                        new PortDto
                        {
                            Id = cameraOutput,
                            Name = "Image",
                            Direction = PortDirection.Output,
                            DataType = PortDataType.Image
                        }
                    ]
                },
                new OperatorDto
                {
                    Id = judgeId,
                    Name = "existing-judge",
                    Type = OperatorType.ResultJudgment,
                    InputPorts =
                    [
                        new PortDto
                        {
                            Id = judgeInput,
                            Name = "Value",
                            Direction = PortDirection.Input,
                            DataType = PortDataType.Any
                        }
                    ]
                }
            ],
            Connections =
            [
                new OperatorConnectionDto
                {
                    Id = Guid.NewGuid(),
                    SourceOperatorId = cameraId,
                    SourcePortId = cameraOutput,
                    TargetOperatorId = judgeId,
                    TargetPortId = judgeInput
                }
            ]
        };
    }

    private sealed class FakeAiFlowGenerationService : IAiFlowGenerationService
    {
        public Task<AiFlowGenerationResult> GenerateFlowAsync(
            AiFlowGenerationRequest request,
            Action<string>? onProgress = null,
            Action<AiStreamChunk>? onStreamChunk = null,
            CancellationToken cancellationToken = default,
            Action<GenerateFlowAttachmentReport>? onAttachmentReport = null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new AiFlowGenerationResult
            {
                Success = true,
                CompletionStatus = AiFlowGenerationResult.CompletionStatusCompleted,
                Flow = new OperatorFlowDto(),
                AiExplanation = "Build eval placeholder; canonical draft is produced by metadata-only builder."
            });
        }
    }

    private sealed class CapturingAgentRunEventSink : IAgentRunEventSink
    {
        public List<AgentRunEventDraft> Events { get; } = [];

        public void Append(string? runId, AgentRunEventDraft draft)
        {
            Events.Add(draft);
        }

        public void StageStarted(string? runId, string stage, string title, string summary, object? payload = null)
        {
            Append(runId, new AgentRunEventDraft
            {
                EventType = AgentRunEventTypes.StageStarted,
                Stage = stage,
                Title = title,
                Summary = summary,
                Status = AgentRunEventStatuses.Running,
                Payload = payload
            });
        }

        public void StageCompleted(string? runId, string stage, string title, string summary, object? payload = null)
        {
            Append(runId, new AgentRunEventDraft
            {
                EventType = AgentRunEventTypes.StageCompleted,
                Stage = stage,
                Title = title,
                Summary = summary,
                Status = AgentRunEventStatuses.Completed,
                Payload = payload
            });
        }

        public void ToolStarted(string? runId, string stage, string toolName, object? payload = null)
        {
            Append(runId, new AgentRunEventDraft
            {
                EventType = AgentRunEventTypes.ToolCallStarted,
                Stage = stage,
                Title = toolName,
                Summary = "started",
                Status = AgentRunEventStatuses.Running,
                Payload = payload
            });
        }

        public void ToolCompleted(string? runId, string stage, string toolName, long durationMs, object? payload = null)
        {
            Append(runId, new AgentRunEventDraft
            {
                EventType = AgentRunEventTypes.ToolCallCompleted,
                Stage = stage,
                Title = toolName,
                Summary = "completed",
                Status = AgentRunEventStatuses.Completed,
                Payload = payload
            });
        }

        public void ToolFailed(string? runId, string stage, string toolName, long durationMs, string summary, object? payload = null)
        {
            Append(runId, new AgentRunEventDraft
            {
                EventType = AgentRunEventTypes.ToolCallFailed,
                Stage = stage,
                Title = toolName,
                Summary = summary,
                Status = AgentRunEventStatuses.Failed,
                Payload = payload
            });
        }
    }
}
