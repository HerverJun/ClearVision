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

    [Fact(DisplayName = "Black-box metal scratch Build should create editable surface defect draft with missing model resource")]
    public async Task BuildAsync_MetalScratch_ShouldBuildSurfaceDefectDraftWithMissingModelResource()
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
            item.ResourceType == "model_resource" &&
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
            item.ResourceType == "model_resource" &&
            item.ResourceKey == "op_surface_defect.ModelId");
        result.BuildResult.FirstFixRecommendation.Should().Contain("模型资源");
        result.BuildResult.FirstFixRecommendation.Should().Contain("op_surface_defect.ModelId");
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
            .Contain(["ImageAcquisition", "DeepLearning", "ResultJudgment", "ResultOutput"]);
        var modelMapping = result.BuildResult.ParameterMapping.Should().ContainSingle(item =>
            item.OperatorType == "DeepLearning" &&
            item.ParameterName == "ModelPath").Subject;
        modelMapping.Pending.Should().BeTrue();
        modelMapping.ValueSummary.Should().StartWith("<pending");
        modelMapping.ValueSummary.Should().NotContain(":\\");
        modelMapping.ValueSummary.Should().NotContain("/");
        result.BuildResult.ParameterMapping.Should().Contain(item =>
            item.OperatorType == "ResultJudgment" &&
            item.ParameterName == "Rule" &&
            item.ValueSummary.Contains("线序", StringComparison.OrdinalIgnoreCase));
        result.BuildResult.MissingResources.Should().Contain(item =>
            item.ResourceType == "model_resource" &&
            item.ResourceKey == "op_detect.ModelPath");
        result.BuildResult.ApplyGate.DeploymentBlockers.Should().Contain("op_detect.ModelPath");
        AssertBuildQuality(result, sink, expectPreserved: true);
    }

    [Fact(DisplayName = "Black-box scenario: hole distance measurement Build quality")]
    public async Task BuildAsync_BlackBoxHoleDistance_ShouldProduceMeasurementDraftWithCalibrationBlockers()
    {
        var sink = new CapturingAgentRunEventSink();
        var orchestrator = CreateOrchestrator(sink);
        var plan = Plan(
            "measurement",
            ["ImageAcquisition", "CircleMeasurement", "CircleMeasurement", "MeasureDistance", "ResultJudgment", "ResultOutput"],
            "hole center distance measurement workflow");

        var result = await orchestrator.BuildAsync(
            Request(plan, buildIntent: "modify", currentFlowSnapshot: ExistingFlowSnapshot("existing-measurement-context")),
            CancellationToken.None);

        result.Success.Should().BeTrue();
        result.BuildResult!.OperatorPipeline.Select(item => item.OperatorType)
            .Should()
            .Contain(["ImageAcquisition", "CircleMeasurement", "MeasureDistance", "ResultJudgment", "ResultOutput"]);
        result.BuildResult.ParameterMapping.Should().Contain(item =>
            item.OperatorType == "MeasureDistance" &&
            item.ParameterName == "Unit" &&
            item.Pending &&
            item.ValueSummary.Contains("calibration", StringComparison.OrdinalIgnoreCase));
        result.BuildResult.ParameterMapping.Should().Contain(item =>
            item.OperatorType == "MeasureDistance" &&
            item.ParameterName == "Tolerance" &&
            item.Pending &&
            item.ValueSummary.Contains("measurement-threshold", StringComparison.OrdinalIgnoreCase));
        result.BuildResult.PendingParameters.Should().Contain(item =>
            item.OperatorId == "op_distance" &&
            item.ParameterNames.Contains("Unit"));
        result.BuildResult.MissingResources.Should().Contain(item =>
            item.ResourceType == "measurement_parameter" &&
            item.ResourceKey == "op_distance.Unit");
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
            item.ResourceKey == "op_match.TemplatePath");
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

    [Fact(DisplayName = "Build orchestrator parameter mapping should keep unknown resources pending")]
    public async Task BuildAsync_ShouldMapPendingParametersAndMissingResources()
    {
        var orchestrator = CreateOrchestrator(new CapturingAgentRunEventSink());
        var plan = Plan("surface_defect", ["ImageAcquisition", "SurfaceDefectDetection", "ResultOutput"]);

        var result = await orchestrator.BuildAsync(Request(plan), CancellationToken.None);

        result.Success.Should().BeTrue();
        result.BuildResult!.ParameterMapping.Should().Contain(item =>
            item.OperatorType == "SurfaceDefectDetection" &&
            item.ParameterName == "ModelId" &&
            item.Pending &&
            item.Source == "pending_metadata");
        result.BuildResult.PendingParameters.Should().Contain(item =>
            item.OperatorId == "op_surface_defect" &&
            item.ParameterNames.Contains("ModelId"));
        result.BuildResult.MissingResources.Should().Contain(item =>
            item.ResourceType == "model_resource" &&
            item.ResourceKey == "op_surface_defect.ModelId");
        result.BuildResult.WorkflowDiff.DeploymentBlockers.Should().Contain("op_surface_defect.ModelId");
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

    private static VisionAgentBuildOrchestrator CreateOrchestrator(CapturingAgentRunEventSink sink)
    {
        var redactor = new AgentRunEventRedactor();
        var toolRunner = new BuildToolRunner(CreateToolRegistry(), redactor, sink);
        return new VisionAgentBuildOrchestrator(
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
