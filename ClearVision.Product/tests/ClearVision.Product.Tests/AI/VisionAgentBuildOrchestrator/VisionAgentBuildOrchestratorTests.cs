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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace ClearVision.Product.Tests.AI.VisionAgentBuildOrchestratorTests;

public sealed class VisionAgentBuildOrchestratorTests
{
    private static readonly string[] RequiredBuildStages =
    [
        "plan_generation",
        "template_strategy",
        "plan_selection",
        "operator_pipeline",
        "parameter_mapping",
        "workflow_draft",
        "validate_schema",
        "metadata_dry_run",
        "package_readiness",
        "workflow_diff",
        "apply_gate"
    ];

    [Fact(DisplayName = "Build orchestrator should resolve through injected Build execution services")]
    public async Task BuildAsync_ShouldResolveThroughInjectedBuildServices()
    {
        var sink = new CapturingAgentRunEventSink();
        using var provider = CreateServiceProvider(sink);
        var orchestrator = provider.GetRequiredService<IVisionAgentBuildOrchestrator>();
        var plan = Plan("surface_defect", ["ImageAcquisition", "SurfaceDefectDetection", "ResultOutput"]);

        var result = await orchestrator.BuildAsync(Request(plan), CancellationToken.None);

        result.Success.Should().BeTrue();
        result.BuildResult!.ToolEvidenceTimeline.Should().Contain(item => item.ToolName == "plan_snapshot_loader");
    }

    [Fact(DisplayName = "Black-box metal scratch Build should create editable surface defect draft without fake model resource")]
    public async Task BuildAsync_MetalScratch_ShouldBuildSurfaceDefectDraftWithoutFakeModelResource()
    {
        var orchestrator = CreateOrchestrator(new CapturingAgentRunEventSink());
        var plan = Plan(
            "surface_defect",
            ["ImageAcquisition", "SurfaceDefectDetection", "BlobAnalysis", "ResultJudgment", "ResultOutput"],
            "帮我做一个金属表面划痕检测流程");

        var result = await orchestrator.BuildAsync(Request(plan), CancellationToken.None);

        result.Success.Should().BeTrue();
        result.BuildResult!.OperatorPipeline.Select(item => item.OperatorType)
            .Should()
            .Contain("SurfaceDefectDetection");
        result.BuildResult.ApplyGate.CanvasApplyReady.Should().BeTrue();
        result.BuildResult.ApplyGate.DeploymentReady.Should().BeFalse();
        result.BuildResult.MissingResources.Should().Contain(item =>
            item.ResourceType == "camera_binding" &&
            item.ResourceKey == "op_cam.CameraId");
        result.BuildResult.MissingResources.Should().NotContain(item =>
            item.ResourceKey == "op_surface_defect.ModelId");
    }

    [Fact(DisplayName = "Black-box scenario: metal scratch Build quality")]
    public async Task BuildAsync_BlackBoxMetalScratch_ShouldProduceDeployBlockedSurfaceDefectDraft()
    {
        var sink = new CapturingAgentRunEventSink();
        var orchestrator = CreateOrchestrator(sink);
        var plan = Plan(
            "surface_defect",
            ["ImageAcquisition", "SurfaceDefectDetection", "BlobAnalysis", "ResultJudgment", "ResultOutput"],
            "metal surface scratch detection workflow");

        var result = await orchestrator.BuildAsync(
            Request(plan, buildIntent: "modify", currentFlowSnapshot: ExistingFlowSnapshot("existing-metal-context")),
            CancellationToken.None);

        result.Success.Should().BeTrue();
        result.BuildResult!.OperatorPipeline.Select(item => item.OperatorType)
            .Should()
            .Contain(["ImageAcquisition", "SurfaceDefectDetection", "ResultJudgment", "ResultOutput"]);
        result.BuildResult.MissingResources.Should().Contain(item =>
            item.ResourceType == "camera_binding" &&
            item.ResourceKey == "op_cam.CameraId");
        result.BuildResult.MissingResources.Should().NotContain(item =>
            item.ResourceKey == "op_surface_defect.ModelId");
        result.BuildResult.FirstFixRecommendation.Should().Contain("相机绑定");
        AssertBuildQuality(result, sink, expectPreserved: true);
    }

    [Fact(DisplayName = "Black-box scenario: terminal wire sequence Build quality")]
    public async Task BuildAsync_BlackBoxWireSequence_ShouldProduceDetectionDraftWithRuleAndModelBlockers()
    {
        var sink = new CapturingAgentRunEventSink();
        var orchestrator = CreateOrchestrator(sink);
        var plan = Plan(
            "wire_sequence",
            ["ImageAcquisition", "DeepLearning", "ResultJudgment", "ResultOutput"],
            "terminal wire sequence inspection workflow");

        var result = await orchestrator.BuildAsync(
            Request(plan, buildIntent: "modify", currentFlowSnapshot: ExistingFlowSnapshot("existing-wire-context")),
            CancellationToken.None);

        result.Success.Should().BeTrue();
        result.BuildResult!.OperatorPipeline.Select(item => item.OperatorType)
            .Should()
            .Contain(["ImageAcquisition", "DeepLearning", "DetectionSequenceJudge", "ResultJudgment", "ResultOutput"]);
        var modelMapping = result.BuildResult.ParameterMapping.Should().ContainSingle(item =>
            item.OperatorType == "DeepLearning" &&
            item.ParameterName == "ModelPath").Subject;
        modelMapping.Pending.Should().BeTrue();
        modelMapping.ValueSummary.Should().StartWith("<pending");
        modelMapping.ValueSummary.Should().NotContain(":\\");
        modelMapping.ValueSummary.Should().NotContain("/");
        result.BuildResult.ParameterMapping.Should().Contain(item =>
            item.OperatorType == "DetectionSequenceJudge" &&
            item.ParameterName == "ExpectedLabels" &&
            item.Pending);
        result.BuildResult.MissingResources.Should().Contain(item =>
            item.ResourceType == "model_resource" &&
            item.ResourceKey == "op_detect.ModelPath");
        result.BuildResult.ApplyGate.DeploymentBlockers.Should().Contain("op_detect.ModelPath");
        AssertBuildQuality(result, sink, expectPreserved: true);
    }

    [Fact(DisplayName = "End-to-end mature strawberry Plan repair and Build should create editable classification draft")]
    public async Task SemanticPlannerConfirmationBuild_MatureStrawberry_ShouldCreateEditableClassificationDraft()
    {
        const string prompt = "检测果园里的草莓，熟透为 OK，否则 NG，输入源是相机。";
        var sink = new CapturingAgentRunEventSink();
        var semantic = StrawberrySemantic();
        var plannerCalls = 0;
        var planner = new VisionAgentPlanPlannerService(
            new DelegatePlanCompletionSource((_, _) =>
            {
                plannerCalls++;
                return Task.FromResult(plannerCalls == 1
                    ? "{\"goal\":\"truncated\""
                    : PlannerCandidateJson(
                        "attribute_classification",
                        "classification_strategy",
                        ["ImageAcquisition", "DeepLearning", "ResultJudgment", "ResultOutput"]));
            }),
            new VisionAgentPlanPromptComposer(),
            Microsoft.Extensions.Options.Options.Create(new VisionAgentPlanPlannerOptions { Enabled = true }),
            NullLogger<VisionAgentPlanPlannerService>.Instance);
        var planOrchestrator = new VisionAgentOrchestrator(
            CreateToolRegistry(),
            new FakeAiFlowGenerationService(),
            sink,
            planPlannerService: planner,
            semanticExtractor: new FakeSemanticExtractor(semantic));

        var plan = await planOrchestrator.CreatePlanAsync(
            new VisionAgentPlanModeRequest
            {
                Description = prompt,
                OriginalUserPrompt = prompt
            },
            CancellationToken.None);

        plannerCalls.Should().Be(2);
        plan.SemanticExtraction.Should().NotBeNull();
        plan.SemanticExtraction!.Source.Should().Be(VisionAgentSemanticSources.Model);
        plan.SemanticExtraction.TaskType.Should().Be(AiVisionTaskTypes.AttributeClassification);
        plan.PlanSource.Should().Be("model_planner");
        plan.FallbackReason.Should().BeEmpty();
        plan.CanBuild.Should().BeTrue();
        plan.RecommendedRoute.Operators.Should().Contain(["ImageAcquisition", "DeepLearning", "ResultJudgment", "ResultOutput"]);
        plan.RecommendedRoute.Operators.Should().NotContain("SurfaceDefectDetection");
        plan.PublicEvents.Should().Contain(evt => evt.Stage == "planner_json_repair_started");
        plan.PublicEvents.Should().Contain(evt => evt.Stage == "planner_json_repair_completed");

        var buildOrchestrator = CreateOrchestrator(sink);
        var result = await buildOrchestrator.BuildAsync(
            Request(
                plan,
                userSelections: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["attribute_target"] = "semantic_attribute",
                    ["ok_ng_rule"] = "use_extracted_conditions",
                    ["classification_ok_label"] = "熟透"
                }),
            CancellationToken.None);

        result.Success.Should().BeTrue();
        result.BuildResult.Should().NotBeNull();
        var build = result.BuildResult!;
        build.OperatorPipeline.Select(item => item.OperatorType)
            .Should()
            .Contain(["ImageAcquisition", "DeepLearning", "ResultJudgment", "ResultOutput"])
            .And
            .NotContain("SurfaceDefectDetection");
        Flow(result).Operators.Should().NotBeEmpty();
        build.MissingResources.Should().Contain(item =>
            item.ResourceType == "model_resource" &&
            item.ResourceKey == "op_detect.ModelPath");
        build.ApplyGate.CanvasApplyReady.Should().BeTrue();
        build.ApplyGate.DeploymentReady.Should().BeFalse();
        build.ParameterMapping.Should().Contain(item =>
            item.OperatorType == "ResultJudgment" &&
            item.ParameterName == "FieldName" &&
            item.ValueSummary == "TopClassLabel");
        build.ParameterMapping.Should().Contain(item =>
            item.OperatorType == "ResultJudgment" &&
            item.ParameterName == "Condition" &&
            item.ValueSummary == "Equal");
        build.ParameterMapping.Should().Contain(item =>
            item.OperatorType == "ResultJudgment" &&
            item.ParameterName == "ExpectValue" &&
            item.ValueSummary.Contains("熟透", StringComparison.Ordinal));
        build.ParameterMapping.Should().NotContain(item =>
            item.OperatorType == "ResultJudgment" &&
            item.ParameterName == "ExpectValue" &&
            item.ValueSummary == "1");
    }

    [Fact(DisplayName = "Build orchestrator should let user strategy override Planner attribute classification route")]
    public async Task BuildAsync_UserTraditionalRuleSelection_ShouldOverridePlannerDeepLearningRoute()
    {
        var sink = new CapturingAgentRunEventSink();
        var orchestrator = CreateOrchestrator(sink);
        var plan = Plan(
            "attribute_classification",
            ["ImageAcquisition", "DeepLearning", "ResultJudgment", "ResultOutput"],
            "classify object maturity from camera");

        var result = await orchestrator.BuildAsync(
            Request(
                plan,
                userSelections: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["model_or_rule_strategy"] = "traditional_rule",
                    ["classification_ok_label"] = "expected class"
                }),
            CancellationToken.None);

        result.Success.Should().BeTrue();
        result.BuildResult.Should().NotBeNull();
        var build = result.BuildResult!;
        build.SelectionSource.Should().Be("user_strategy");
        build.EffectiveRouteId.Should().Be("attribute_classification_traditional_rule");
        build.EffectiveOperators.Should().Contain(["ImageAcquisition", "Thresholding", "BlobAnalysis", "ResultJudgment", "ResultOutput"]);
        build.OperatorPipeline.Select(item => item.OperatorType)
            .Should()
            .Contain(["ImageAcquisition", "Thresholding", "BlobAnalysis", "ResultJudgment", "ResultOutput"])
            .And
            .NotContain("DeepLearning");
        build.ToolEvidenceTimeline.Should().Contain(item =>
            item.Stage == "plan_selection" &&
            item.ToolName == "plan_selection_resolver");
    }

    [Fact(DisplayName = "Black-box scenario: hole distance measurement Build quality")]
    public async Task BuildAsync_BlackBoxHoleDistance_ShouldProduceMeasurementDraftWithCalibrationBlockers()
    {
        var sink = new CapturingAgentRunEventSink();
        var orchestrator = CreateOrchestrator(sink);
        var plan = Plan(
            "measurement",
            ["ImageAcquisition", "CircleMeasurement", "CircleMeasurement", "MeasureDistance", "UnitConvert", "ResultJudgment", "ResultOutput"],
            "hole center distance measurement workflow");

        var result = await orchestrator.BuildAsync(
            Request(plan, buildIntent: "modify", currentFlowSnapshot: ExistingFlowSnapshot("existing-measurement-context")),
            CancellationToken.None);

        result.Success.Should().BeTrue();
        result.BuildResult!.OperatorPipeline.Select(item => item.OperatorType)
            .Should()
            .Contain(["ImageAcquisition", "CircleMeasurement", "Measurement", "UnitConvert", "ResultJudgment", "ResultOutput"]);
        result.BuildResult.ParameterMapping.Should().Contain(item =>
            item.OperatorType == "UnitConvert" &&
            item.ParameterName == "Scale" &&
            item.Pending &&
            item.ValueSummary.Contains("pixel-to-world-scale", StringComparison.OrdinalIgnoreCase));
        result.BuildResult.PendingParameters.Should().Contain(item =>
            item.OperatorId == "op_calibration" &&
            item.ParameterNames.Contains("Scale"));
        result.BuildResult.MissingResources.Should().Contain(item =>
            item.ResourceType == "measurement_parameter" &&
            item.ResourceKey == "op_calibration.Scale");
        AssertBuildQuality(result, sink, expectPreserved: true);
    }

    [Fact(DisplayName = "Black-box scenario: template positioning Build quality")]
    public async Task BuildAsync_BlackBoxTemplatePositioning_ShouldUseTemplateSkeletonWithTemplateBlockers()
    {
        var sink = new CapturingAgentRunEventSink();
        var orchestrator = CreateOrchestrator(sink);
        var plan = Plan(
            "template_positioning",
            ["ImageAcquisition", "SurfaceDefectDetection", "ResultOutput"],
            "template positioning and matching workflow");
        var templateSelection = new AiTemplateSelectionInfo
        {
            Mode = "use_selected_template",
            TemplateId = "template_matching_alignment",
            ScenarioKey = "template_matching"
        };

        var result = await orchestrator.BuildAsync(
            Request(
                plan,
                buildIntent: "modify",
                currentFlowSnapshot: ExistingFlowSnapshot("existing-template-context"),
                templateSelection: templateSelection),
            CancellationToken.None);

        result.Success.Should().BeTrue();
        result.GenerationMode.Should().Be("template_fill");
        result.TemplateLockLevel.Should().Be("strict");
        result.BuildResult!.OperatorPipeline.Select(item => item.OperatorType)
            .Should()
            .Contain(["ImageAcquisition", "TemplateMatching", "ResultJudgment", "ResultOutput"])
            .And
            .NotContain("SurfaceDefectDetection");
        result.BuildResult.MissingResources.Should().Contain(item =>
            item.ResourceType == "template_artifact" &&
            item.ResourceKey == "op_match.Template");
        result.BuildResult.FirstFixRecommendation.Should().Contain("模板资源");
        result.BuildResult.ToolEvidenceTimeline.Should().Contain(item =>
            item.Stage == "template_strategy" &&
            item.ToolName == "get_flow_template_skeleton");
        AssertBuildQuality(result, sink, expectPreserved: true);
    }

    [Fact(DisplayName = "Build orchestrator should produce tool evidence, workflow diff, and apply gates")]
    public async Task BuildAsync_ShouldProduceToolEvidenceDiffAndApplyGates()
    {
        var sink = new CapturingAgentRunEventSink();
        var orchestrator = CreateOrchestrator(sink);
        var plan = Plan("surface_defect", ["ImageAcquisition", "SurfaceDefectDetection", "BlobAnalysis", "ResultJudgment", "ResultOutput"]);

        var result = await orchestrator.BuildAsync(Request(plan), CancellationToken.None);

        result.Success.Should().BeTrue();
        result.BuildResult.Should().NotBeNull();
        result.BuildResult!.ToolEvidenceTimeline.Select(item => item.Stage).Should().Contain([
            "plan_generation",
            "template_strategy",
            "plan_selection",
            "operator_pipeline",
            "parameter_mapping",
            "workflow_draft",
            "validate_schema",
            "metadata_dry_run",
            "package_readiness",
            "workflow_diff",
            "apply_gate"
        ]);
        result.BuildResult.OperatorPipeline.Select(item => item.OperatorType)
            .Should()
            .Contain("SurfaceDefectDetection");
        result.BuildResult.WorkflowDiff.AddedNodes.Should().NotBeEmpty();
        result.BuildResult.ApplyGate.CanvasApplyReady.Should().BeTrue();
        result.BuildResult.ApplyGate.RuntimeDraftReady.Should().BeTrue();
        result.BuildResult.ApplyGate.DeploymentReady.Should().BeFalse();
        result.BuildResult.ApplyGate.Blocked.Should().BeFalse();
        result.BuildResult.ApplyGate.DeploymentBlockers.Should().NotBeEmpty();
        result.BuildResult.FirstFixRecommendation.Should().NotBeNullOrWhiteSpace();
        Flow(result).Operators.Should().NotBeEmpty();
        sink.Events.Should().Contain(evt => evt.EventType == AgentRunEventTypes.WorkflowDraftUpdated);
        sink.Events.Should().Contain(evt => evt.EventType == AgentRunEventTypes.PackageReadinessChecked);
    }

    [Fact(DisplayName = "Build orchestrator should repair invalid plan operators before drafting")]
    public async Task BuildAsync_ShouldRepairInvalidOperators()
    {
        var orchestrator = CreateOrchestrator(new CapturingAgentRunEventSink());
        var plan = Plan("surface_defect", ["ImageAcquisition", "QuantumScratchMagic", "ResultJudgment", "ResultOutput"]);

        var result = await orchestrator.BuildAsync(Request(plan), CancellationToken.None);

        result.Success.Should().BeTrue();
        result.BuildResult!.OperatorPipeline.Select(item => item.OperatorType)
            .Should()
            .NotContain("QuantumScratchMagic");
        result.BuildResult.PublicWarnings.Should().Contain("invalid_operator_removed");
        result.BuildResult.ToolEvidenceTimeline.Should().Contain(item =>
            item.WarningCode == "invalid_operator_removed" &&
            item.RepairAction == "removed_invalid_operators");
    }

    [Fact(DisplayName = "Build orchestrator should publish a public warning when plan hash mismatches")]
    public async Task BuildAsync_ShouldWarnWhenPlanHashMismatches()
    {
        var sink = new CapturingAgentRunEventSink();
        var orchestrator = CreateOrchestrator(sink);
        var plan = Plan("surface_defect", ["ImageAcquisition", "SurfaceDefectDetection", "ResultOutput"]);

        var result = await orchestrator.BuildAsync(Request(plan, planHashOverride: "stale_plan_hash"), CancellationToken.None);

        result.Success.Should().BeTrue();
        result.BuildResult!.PublicWarnings.Should().Contain("plan_hash_mismatch");
        result.BuildResult.ToolEvidenceTimeline.Should().Contain(item =>
            item.Stage == "plan_generation" &&
            item.WarningCode == "plan_hash_mismatch");
        sink.Events.Should().Contain(evt =>
            evt.Stage == "plan_hash_validation" &&
            evt.Summary.Contains("复核计划来源", StringComparison.OrdinalIgnoreCase));
    }

    [Fact(DisplayName = "Black-box template positioning Build should use template skeleton instead of surface defect route")]
    public async Task BuildAsync_ShouldUseTemplateSelectionForTemplateStrategy()
    {
        var orchestrator = CreateOrchestrator(new CapturingAgentRunEventSink());
        var plan = Plan(
            "template_positioning",
            ["ImageAcquisition", "SurfaceDefectDetection", "ResultOutput"],
            "帮我做一个模板定位流程");
        var templateSelection = new AiTemplateSelectionInfo
        {
            Mode = "use_selected_template",
            TemplateId = "template_matching_alignment",
            ScenarioKey = "template_matching"
        };

        var result = await orchestrator.BuildAsync(Request(plan, templateSelection: templateSelection), CancellationToken.None);

        result.Success.Should().BeTrue();
        result.GenerationMode.Should().Be("template_fill");
        result.TemplateLockLevel.Should().Be("strict");
        result.BuildResult!.OperatorPipeline.Select(item => item.OperatorType)
            .Should()
            .Contain("TemplateMatching")
            .And
            .NotContain("SurfaceDefectDetection");
        result.BuildResult.ToolEvidenceTimeline.Should().Contain(item =>
            item.Stage == "template_strategy" &&
            item.ToolName == "get_flow_template_skeleton");
    }

    [Fact(DisplayName = "Build orchestrator should warn and continue when optional template skeleton is missing")]
    public async Task BuildAsync_ShouldWarnAndContinueWhenOptionalTemplateSkeletonMissing()
    {
        var sink = new CapturingAgentRunEventSink();
        var orchestrator = CreateOrchestrator(
            sink,
            CreateTemplateNotFoundRegistry("missing_optional_template"));
        var plan = Plan(
            "surface_defect",
            ["ImageAcquisition", "SurfaceDefectDetection", "ResultOutput"],
            "metal scratch inspection with optional catalog template");

        var result = await orchestrator.BuildAsync(Request(plan), CancellationToken.None);

        result.Success.Should().BeTrue();
        result.BuildResult!.ApplyGate.CanvasApplyReady.Should().BeTrue();
        result.BuildResult.MissingResources.Should().NotContain(item => item.ResourceType == "template_artifact");
        result.BuildResult.ToolEvidenceTimeline.Should().Contain(item =>
            item.Stage == "template_strategy" &&
            item.ToolName == "get_flow_template_skeleton" &&
            item.Status == AgentRunEventStatuses.Warning &&
            item.WarningCode == "template_not_found" &&
            item.OutputSummary.Contains("未找到匹配模板骨架，已改用算子链生成", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "Build orchestrator should keep explicitly required missing template as missing resource")]
    public async Task BuildAsync_ShouldMarkRequiredMissingTemplateAsResourcePending()
    {
        var sink = new CapturingAgentRunEventSink();
        var orchestrator = CreateOrchestrator(
            sink,
            CreateTemplateNotFoundRegistry("missing_required_template"));
        var plan = Plan(
            "template_positioning",
            ["ImageAcquisition", "TemplateMatching", "ResultOutput"],
            "template positioning with selected missing skeleton");
        var templateSelection = new AiTemplateSelectionInfo
        {
            Mode = "use_selected_template",
            TemplateId = "missing_required_template",
            ScenarioKey = "template_matching"
        };

        var result = await orchestrator.BuildAsync(
            Request(plan, templateSelection: templateSelection),
            CancellationToken.None);

        result.Success.Should().BeTrue();
        result.BuildResult!.MissingResources.Should().Contain(item =>
            item.ResourceType == "template_artifact" &&
            item.ResourceKey == "missing_required_template");
        result.BuildResult.ToolEvidenceTimeline.Should().Contain(item =>
            item.Stage == "template_strategy" &&
            item.ToolName == "get_flow_template_skeleton" &&
            item.Status == AgentRunEventStatuses.Failed &&
            item.WarningCode == "template_not_found");
    }

    [Fact(DisplayName = "Build orchestrator parameter mapping should keep unknown resources pending")]
    public async Task BuildAsync_ShouldMapPendingParametersAndMissingResources()
    {
        var orchestrator = CreateOrchestrator(new CapturingAgentRunEventSink());
        var plan = Plan("surface_defect", ["ImageAcquisition", "SurfaceDefectDetection", "ResultOutput"]);

        var result = await orchestrator.BuildAsync(Request(plan), CancellationToken.None);

        result.Success.Should().BeTrue();
        result.BuildResult!.ParameterMapping.Should().Contain(item =>
            item.OperatorType == "ImageAcquisition" &&
            item.ParameterName == "CameraId" &&
            item.Pending &&
            item.Source == "pending_metadata");
        result.BuildResult.PendingParameters.Should().Contain(item =>
            item.OperatorId == "op_cam" &&
            item.ParameterNames.Contains("CameraId"));
        result.BuildResult.MissingResources.Should().Contain(item =>
            item.ResourceType == "camera_binding" &&
            item.ResourceKey == "op_cam.CameraId");
        result.BuildResult.WorkflowDiff.DeploymentBlockers.Should().Contain("op_cam.CameraId");
    }

    [Fact(DisplayName = "Build orchestrator modify intent should preserve existing canvas nodes")]
    public async Task BuildAsync_ShouldPreserveExistingFlowForModifyIntent()
    {
        var orchestrator = CreateOrchestrator(new CapturingAgentRunEventSink());
        var existing = new OperatorFlowDto
        {
            Id = Guid.NewGuid(),
            Name = "Existing flow",
            Operators =
            [
                new OperatorDto
                {
                    Id = Guid.NewGuid(),
                    Name = "existing-camera",
                    Type = OperatorType.ImageAcquisition,
                    OutputPorts =
                    [
                        new PortDto
                        {
                            Id = Guid.NewGuid(),
                            Name = "Image",
                            Direction = PortDirection.Output,
                            DataType = PortDataType.Image
                        }
                    ]
                }
            ]
        };
        var plan = Plan("surface_defect", ["ImageAcquisition", "SurfaceDefectDetection", "ResultOutput"]);
        var request = Request(plan, buildIntent: "modify", currentFlowSnapshot: JsonSerializer.Serialize(existing));

        var result = await orchestrator.BuildAsync(request, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.BuildResult!.WorkflowDiff.PreservedNodes.Should().NotBeEmpty();
        Flow(result).Operators.Select(item => item.Name).Should().Contain("existing-camera");
        Flow(result).Operators.Count.Should().BeGreaterThan(1);
        Flow(result).Operators.Count.Should().BeGreaterThanOrEqualTo(existing.Operators.Count + 1);
    }

    [Fact(DisplayName = "Build orchestrator replay payload should redact unsafe metadata")]
    public async Task BuildAsync_ShouldRedactUnsafeEvidenceAndBuildResult()
    {
        var sink = new CapturingAgentRunEventSink();
        var orchestrator = CreateOrchestrator(sink);
        var plan = Plan("plc_output", ["ImageAcquisition", "ResultJudgment", "ResultOutput"]) with
        {
            OriginalUserPrompt = "use C:\\factory\\scratch.png sk-secret-token DB1.DBX0.0 192.168.1.10"
        };

        var result = await orchestrator.BuildAsync(Request(plan), CancellationToken.None);
        var publicJson = JsonSerializer.Serialize(new { result.BuildResult, sink.Events }, AgentRunEventJson.Options);

        publicJson.Should().NotContain("C:\\factory");
        publicJson.Should().NotContain("sk-secret-token");
        publicJson.Should().NotContain("DB1.DBX0.0");
        publicJson.Should().NotContain("192.168.1.10");
        publicJson.Should().NotContain("systemPrompt");
        publicJson.Should().NotContain("rawPrompt");
        publicJson.Should().NotContain("chainOfThought");
    }

    private static VisionAgentBuildOrchestrator CreateOrchestrator(
        CapturingAgentRunEventSink sink,
        IVisionAgentToolRegistry? registry = null)
    {
        var redactor = new AgentRunEventRedactor();
        var toolRunner = new BuildToolRunner(registry ?? CreateToolRegistry(), redactor, sink);
        return new VisionAgentBuildOrchestrator(
            new BuildPlanContextLoader(sink),
            new BuildIntentResolver(),
            new TemplateStrategyResolver(toolRunner),
            new PlanSelectionResolver(),
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
    }

    private static void AssertBuildQuality(
        AiFlowGenerationResult result,
        CapturingAgentRunEventSink sink,
        bool expectPreserved)
    {
        result.BuildResult.Should().NotBeNull();
        var build = result.BuildResult!;
        build.ToolEvidenceTimeline.Select(item => item.Stage)
            .Should()
            .Contain(RequiredBuildStages);
        build.WorkflowDiff.AddedNodes.Should().NotBeEmpty();
        if (expectPreserved)
        {
            build.WorkflowDiff.PreservedNodes.Should().NotBeEmpty();
        }

        build.WorkflowDiff.PendingParameters.Should().NotBeEmpty();
        build.WorkflowDiff.DeploymentBlockers.Should().NotBeEmpty();
        build.ApplyGate.CanvasApplyReady.Should().BeTrue();
        build.ApplyGate.RuntimeDraftReady.Should().BeTrue();
        build.ApplyGate.DeploymentReady.Should().BeFalse();
        build.ApplyGate.Blocked.Should().BeFalse();
        build.ApplyGate.DeploymentBlockers.Should().NotBeEmpty();
        if (build.MissingResources.Count > 0 || build.PendingParameters.Count > 0)
        {
            build.FirstFixRecommendation.Should().NotBeNullOrWhiteSpace();
            build.FirstFixRecommendation.Should().NotBe("<pending-parameter>");
        }

        AssertNoSensitiveLeak(result, sink);
    }

    private static void AssertNoSensitiveLeak(
        AiFlowGenerationResult result,
        CapturingAgentRunEventSink sink)
    {
        var publicJson = JsonSerializer.Serialize(new { result.BuildResult, sink.Events }, AgentRunEventJson.Options);
        publicJson.Should().NotContain("systemPrompt");
        publicJson.Should().NotContain("rawPrompt");
        publicJson.Should().NotContain("chainOfThought");
        publicJson.Should().NotContain("reasoning_content");
        publicJson.Should().NotContain("C:\\");
        publicJson.Should().NotContain("D:\\");
        publicJson.Should().NotContain("sk-");
        publicJson.Should().NotContain("DB1.DBX");
        publicJson.Should().NotContain("192.168.");
        publicJson.Should().NotContain("data:image");
        publicJson.Should().NotContain(";base64");
    }

    private static string ExistingFlowSnapshot(string existingNodeName)
    {
        return JsonSerializer.Serialize(new OperatorFlowDto
        {
            Id = Guid.NewGuid(),
            Name = "Existing scenario context",
            Operators =
            [
                new OperatorDto
                {
                    Id = Guid.NewGuid(),
                    Name = existingNodeName,
                    Type = OperatorType.ImageAcquisition,
                    OutputPorts =
                    [
                        new PortDto
                        {
                            Id = Guid.NewGuid(),
                            Name = "Image",
                            Direction = PortDirection.Output,
                            DataType = PortDataType.Image
                        }
                    ]
                }
            ]
        });
    }

    private static ServiceProvider CreateServiceProvider(CapturingAgentRunEventSink sink)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IAgentRunEventSink>(sink);
        services.AddSingleton<AgentRunEventRedactor>();
        services.AddSingleton<IAiFlowGenerationService, FakeAiFlowGenerationService>();
        services.AddSingleton<IVisionAgentToolRegistry>(_ => CreateToolRegistry());
        services.AddSingleton<BuildToolRunner>();
        services.AddSingleton<BuildPlanContextLoader>();
        services.AddSingleton<BuildIntentResolver>();
        services.AddSingleton<TemplateStrategyResolver>();
        services.AddSingleton<PlanSelectionResolver>();
        services.AddSingleton<OperatorPipelineSelector>();
        services.AddSingleton<ParameterMappingService>();
        services.AddSingleton<WorkflowDraftBuilder>();
        services.AddSingleton<BuildReadinessReviewService>();
        services.AddSingleton<WorkflowDiffService>();
        services.AddSingleton<ApplyGateResolver>();
        services.AddSingleton<BuildResultAssembler>();
        services.AddSingleton<Microsoft.Extensions.Logging.ILogger<VisionAgentBuildOrchestrator>>(
            NullLogger<VisionAgentBuildOrchestrator>.Instance);
        services.AddSingleton<IVisionAgentBuildOrchestrator, VisionAgentBuildOrchestrator>();
        return services.BuildServiceProvider();
    }

    private static VisionAgentToolRegistry CreateToolRegistry()
    {
        return new VisionAgentToolRegistry(
        [
            new FlowTemplateMatchTool(),
            new FlowTemplateSkeletonTool(),
            new FlowValidationTool(),
            new DryRunFlowTool(),
            new RuntimePackagePrecheckTool()
        ]);
    }

    private static VisionAgentToolRegistry CreateTemplateNotFoundRegistry(string templateId)
    {
        return new VisionAgentToolRegistry(
        [
            new FakeVisionAgentTool(
                "match_flow_template",
                VisionAgentToolPermission.ReadOnly,
                (_, _) => VisionAgentToolResult.Ok(new
                {
                    candidates = new[]
                    {
                        new
                        {
                            templateId,
                            scenarioKey = "surface_defect",
                            score = 0.92,
                            metadataOnly = true
                        }
                    },
                    metadataOnly = true
                })),
            new FakeVisionAgentTool(
                "get_flow_template_skeleton",
                VisionAgentToolPermission.ReadOnly,
                (_, _) => VisionAgentToolResult.Fail(
                    "template_not_found",
                    "Template skeleton was not found in the metadata catalog.")),
            new FakeVisionAgentTool(
                "validate_flow",
                VisionAgentToolPermission.Simulation,
                (_, _) => VisionAgentToolResult.Ok(new
                {
                    blockingIssues = Array.Empty<object>(),
                    warnings = Array.Empty<object>(),
                    missingResources = Array.Empty<object>(),
                    pendingParameters = Array.Empty<object>(),
                    metadataOnly = true
                })),
            new FakeVisionAgentTool(
                "dryrun_flow",
                VisionAgentToolPermission.Simulation,
                (_, _) => VisionAgentToolResult.Ok(new
                {
                    dryRunSucceeded = true,
                    blockingIssues = Array.Empty<object>(),
                    missingResources = Array.Empty<object>(),
                    metadataOnly = true
                })),
            new FakeVisionAgentTool(
                "runtime_package_precheck",
                VisionAgentToolPermission.DeploymentPrepare,
                (_, _) => VisionAgentToolResult.Ok(new
                {
                    readyForDeployment = false,
                    blockingIssues = Array.Empty<object>(),
                    missingResources = Array.Empty<object>(),
                    pendingActions = Array.Empty<object>(),
                    metadataOnly = true
                }))
        ]);
    }

    private static AiFlowGenerationRequest Request(
        VisionAgentPlanModeResult plan,
        string buildIntent = "new",
        string? currentFlowSnapshot = null,
        string? planHashOverride = null,
        AiTemplateSelectionInfo? templateSelection = null,
        Dictionary<string, string>? userSelections = null)
    {
        return new AiFlowGenerationRequest(plan.OriginalUserPrompt, Mode: GenerateFlowModeExtensions.ParseOrAuto(buildIntent))
        {
            AgentRunId = "ar_build_test",
            UseVisionAgentGenerateFlow = true,
            BuildFromPlan = new VisionAgentBuildFromPlanRequest
            {
                PlanId = plan.PlanId,
                PlanHash = planHashOverride ?? plan.PlanHash,
                PlanSnapshot = plan,
                UserSelections = userSelections ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["defect_morphology"] = "scratch"
                },
                AcceptedDefaults = ["metadata_only"],
                CurrentFlowSnapshot = currentFlowSnapshot,
                TemplateSelection = templateSelection,
                OperatorCatalogVersion = plan.OperatorCatalogVersion,
                StationBoundarySummary = plan.StationBoundarySummary,
                PlcOutputPolicy = plan.PlcOutputPolicy,
                BuildIntent = buildIntent,
                OriginalUserPrompt = plan.OriginalUserPrompt,
                AcceptedRecommendedDefaults = true,
                MetadataOnly = true
            }
        };
    }

    private static VisionAgentPlanModeResult Plan(
        string intent,
        List<string> operators,
        string originalUserPrompt = "metal surface scratch detection")
    {
        var semantic = SemanticForPlan(intent, originalUserPrompt);
        var maturity = VisionAgentRequirementMaturityGate.Evaluate(
            new VisionAgentRequirementMaturityRequest
            {
                Description = originalUserPrompt
            },
            semantic);
        var result = new VisionAgentPlanModeResult
        {
            PlanId = "plan_build_test",
            OriginalUserPrompt = originalUserPrompt,
            Goal = originalUserPrompt,
            Intent = intent,
            Confidence = "high",
            RequirementUnderstanding = ["Inspect surface defects with metadata-only Build."],
            RecommendedRoute = new VisionAgentRecommendedRoute
            {
                RouteId = $"{intent}_route",
                Title = "Inspection route",
                Summary = "Plan-constrained operator route.",
                Operators = operators,
                TemplateDecision = "catalog_match"
            },
            ClarificationQuestions = [],
            RecommendedDefaults =
            [
                new VisionAgentDefaultAssumption
                {
                    Id = "metadata_only",
                    Label = "Metadata only",
                    Value = "pending_resources",
                    Impact = "No raw resources are guessed."
                }
            ],
            Risks = ["Resources must be bound before deployment."],
            AcceptanceCriteria = ["Editable workflow draft can be applied to canvas."],
            ExecutablePlan = ["Build draft", "Validate", "Dry-run", "Review gates"],
            CanBuild = true,
            SemanticExtraction = semantic,
            RequirementMaturity = maturity,
            NextAction = "Build",
            OperatorCatalogVersion = "catalog.v1",
            TemplateCatalogVersion = "template.v1",
            StationBoundarySummary = "metadata-only Station boundary",
            PlcOutputPolicy = "local ResultOutput first; PLC writes disabled",
            MetadataOnly = true
        };

        return result with
        {
            PlanHash = VisionAgentOrchestrator.ComputePlanHash(result)
        };
    }

    private static OperatorFlowDto Flow(AiFlowGenerationResult result)
    {
        return result.Flow.Should().BeOfType<OperatorFlowDto>().Subject;
    }

    private static VisionAgentSemanticExtractionResult SemanticForPlan(
        string intent,
        string originalUserPrompt)
    {
        var taskType = intent switch
        {
            "wire_sequence" => AiVisionTaskTypes.WireSequence,
            "measurement" => AiVisionTaskTypes.GeometryMeasurement,
            "template_positioning" or "template_location" => AiVisionTaskTypes.TemplateLocation,
            "presence_absence" => AiVisionTaskTypes.PresenceAbsence,
            "classification" or "attribute_classification" => AiVisionTaskTypes.AttributeClassification,
            "code_recognition" => AiVisionTaskTypes.CodeRecognition,
            _ => AiVisionTaskTypes.SurfaceDefect
        };
        var inspectionObject = taskType switch
        {
            AiVisionTaskTypes.WireSequence => "terminal wire",
            AiVisionTaskTypes.GeometryMeasurement => "hole distance",
            AiVisionTaskTypes.TemplateLocation => "template target",
            AiVisionTaskTypes.PresenceAbsence => "assembly part",
            AiVisionTaskTypes.AttributeClassification => "classified object",
            AiVisionTaskTypes.CodeRecognition => "code",
            _ => "metal surface"
        };

        return new VisionAgentSemanticExtractionResult
        {
            IsVisionRequest = true,
            Intent = "new_flow",
            TaskType = taskType,
            Confidence = 0.9,
            TaskTypeConfidence = 0.9,
            InspectionObject = inspectionObject,
            TargetAttribute = taskType == AiVisionTaskTypes.AttributeClassification ? "attribute" : string.Empty,
            MeasurementTarget = taskType == AiVisionTaskTypes.GeometryMeasurement ? "hole distance" : string.Empty,
            DefectType = taskType == AiVisionTaskTypes.SurfaceDefect ? "scratch" : string.Empty,
            ImageSource = "camera",
            OkCondition = taskType == AiVisionTaskTypes.AttributeClassification ? "expected class is OK" : "meets requirement is OK",
            NgCondition = "otherwise NG",
            OutputTarget = "OK/NG result",
            CanPlanCandidate = true,
            CanBuildCandidate = true,
            ObjectSignals = [inspectionObject],
            TaskSignals = [taskType],
            Source = VisionAgentSemanticSources.Model,
            MetadataOnly = true
        };
    }

    private static VisionAgentSemanticExtractionResult StrawberrySemantic()
    {
        return new VisionAgentSemanticExtractionResult
        {
            IsVisionRequest = true,
            Intent = "new_flow",
            TaskType = AiVisionTaskTypes.AttributeClassification,
            Confidence = 0.94,
            TaskTypeConfidence = 0.91,
            InspectionObject = "草莓",
            TargetAttribute = "成熟度/熟透",
            ImageSource = "相机",
            OkCondition = "熟透为 OK",
            NgCondition = "否则 NG",
            CanPlanCandidate = true,
            CanBuildCandidate = false,
            ObjectSignals = ["草莓"],
            TaskSignals = ["成熟度", "熟透"],
            Source = VisionAgentSemanticSources.Model,
            MetadataOnly = true
        };
    }

    private static string PlannerCandidateJson(
        string intent,
        string questionId,
        IReadOnlyList<string> operators)
    {
        var payload = new
        {
            goal = $"{intent} planner goal",
            intent,
            confidence = "high",
            requirementUnderstanding = new[]
            {
                "Planner understood the object attribute classification requirement.",
                "Use public metadata and keep camera/model resources pending."
            },
            recommendedRoute = new
            {
                routeId = $"{intent}_planner_route",
                title = $"{intent} planner route",
                summary = "Planner selected a model-backed attribute classification route.",
                operators,
                templateDecision = "planner_route"
            },
            clarificationQuestions = new[]
            {
                QuestionPayload(questionId, "Classification strategy"),
                QuestionPayload("ok_ng_rule", "OK/NG judgment")
            },
            recommendedDefaults = new[]
            {
                new
                {
                    id = "metadata_only",
                    label = "Metadata only",
                    value = "pending_resources",
                    impact = "Camera and model resources remain pending."
                }
            },
            risks = new[] { "Classification boundary requires sample review before deployment." },
            acceptanceCriteria = new[] { "Workflow draft contains acquisition, model classification, judgment, and output stages." },
            executablePlan = new[] { "Confirm recommended strategy.", "Build editable draft.", "Review pending model resource." },
            canBuildCandidate = true,
            blockingReasons = Array.Empty<string>(),
            nextAction = "Accept recommended defaults and Build."
        };

        return JsonSerializer.Serialize(payload);
    }

    private static object QuestionPayload(string id, string title)
    {
        return new
        {
            id,
            title,
            why = "This affects operator parameters and release readiness.",
            defaultValue = "recommended",
            defaultAssumption = "Use the planner recommended metadata-only default.",
            impact = "Build can continue with pending resources.",
            options = new[]
            {
                new
                {
                    value = "recommended",
                    label = "Recommended",
                    recommended = true,
                    description = "Use the recommended default.",
                    impact = "Fastest path to editable draft."
                },
                new
                {
                    value = "pending",
                    label = "Keep pending",
                    recommended = false,
                    description = "Keep this choice pending.",
                    impact = "Draft remains editable; deployment remains blocked."
                }
            }
        };
    }

    private sealed class FakeSemanticExtractor : IVisionAgentSemanticExtractorService
    {
        private readonly VisionAgentSemanticExtractionResult _result;

        public FakeSemanticExtractor(VisionAgentSemanticExtractionResult result)
        {
            _result = result;
        }

        public Task<VisionAgentSemanticExtractionResult> ExtractAsync(
            VisionAgentSemanticExtractionRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_result);
        }
    }

    private sealed class DelegatePlanCompletionSource : IVisionAgentPlanCompletionSource
    {
        private readonly Func<VisionAgentPlanCompletionRequest, CancellationToken, Task<string>> _completion;

        public DelegatePlanCompletionSource(
            Func<VisionAgentPlanCompletionRequest, CancellationToken, Task<string>> completion)
        {
            _completion = completion;
        }

        public Task<string> CompleteAsync(
            VisionAgentPlanCompletionRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return _completion(request, cancellationToken);
        }
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
                AiExplanation = "Generated empty draft placeholder for BuildOrchestrator fallback."
            });
        }
    }

    private sealed class FakeVisionAgentTool : IVisionAgentTool
    {
        private readonly Func<VisionAgentToolContext, JsonElement, VisionAgentToolResult> _execute;

        public FakeVisionAgentTool(
            string name,
            VisionAgentToolPermission permission,
            Func<VisionAgentToolContext, JsonElement, VisionAgentToolResult> execute)
        {
            Name = name;
            Permission = permission;
            _execute = execute;
        }

        public string Name { get; }
        public string DisplayName => Name;
        public string Description => "Fake BuildOrchestrator test tool.";
        public string Category => "test";
        public VisionAgentToolPermission Permission { get; }
        public JsonElement ParametersSchema { get; } = Schema();

        public Task<VisionAgentToolResult> ExecuteAsync(
            VisionAgentToolContext context,
            JsonElement arguments,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_execute(context, arguments));
        }

        private static JsonElement Schema()
        {
            using var doc = JsonDocument.Parse("""{"type":"object","properties":{}}""");
            return doc.RootElement.Clone();
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
