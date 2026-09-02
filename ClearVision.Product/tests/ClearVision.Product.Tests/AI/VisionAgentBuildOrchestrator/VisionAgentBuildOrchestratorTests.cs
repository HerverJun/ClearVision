using System.Text.Json;
using ClearVision.Product.Application.DTOs;
using ClearVision.Product.Contracts.Messages;
using ClearVision.Product.Core.AI.Tools;
using ClearVision.Product.Core.DTOs;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Services;
using ClearVision.Product.Infrastructure.AI;
using ClearVision.Product.Infrastructure.AI.Agent;
using ClearVision.Product.Infrastructure.AI.AgentRun;
using ClearVision.Product.Infrastructure.AI.Runtime;
using ClearVision.Product.Infrastructure.AI.Tools;
using ClearVision.Product.Tests.Services;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ClearVision.Product.Tests.AI.VisionAgentBuildOrchestratorTests;

[TestClassification(TestDomain.Ai, TestPurpose.Regression, TestLane.Nightly, TestEvidenceType.Contract, TestOracleType.Contract, TestResourceRequirement.None, TestExpectedDuration.Medium, TestFlakyPolicy.Blocking, "vision-agent")]
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

    [Fact(DisplayName = "BuildFromPlan should allow editable draft when image source and acceptance stay pending but acquisition route exists")]
    public async Task BuildAsync_DraftPendingImageAndAcceptanceWithAcquisitionRoute_ShouldBuildEditableDraft()
    {
        var orchestrator = CreateOrchestrator(new CapturingAgentRunEventSink());
        var baseline = Plan(
            "surface_defect",
            ["ImageAcquisition", "SurfaceDefectDetection", "ResultJudgment", "ResultOutput"],
            "steering wheel logo appearance defect workflow");
        var plan = baseline with
        {
            CanBuild = false,
            BlockingReasons = ["hard_requirement:image_source_missing", "hard_requirement:acceptance_criteria_missing"],
            SemanticExtraction = baseline.SemanticExtraction! with
            {
                ImageSource = string.Empty
            },
            RequirementMaturity = baseline.RequirementMaturity! with
            {
                CanBuild = false,
                MissingFields = ["image_source", "acceptance_criteria"],
                BlockingReasons = ["image_source_missing", "acceptance_criteria_missing"],
                PublicReason = "图像来源需要在部署前绑定。"
            }
        };
        plan = plan with
        {
            PlanHash = VisionAgentOrchestrator.ComputePlanHash(plan)
        };

        var result = await orchestrator.BuildAsync(
            Request(plan, acceptedRecommendedDefaults: false, requirementMode: AiRequirementModes.Draft),
            CancellationToken.None);

        result.Success.Should().BeTrue();
        var build = result.BuildResult!;
        build.OperatorPipeline.Select(item => item.OperatorType)
            .Should()
            .Contain(["ImageAcquisition", "SurfaceDefectDetection", "ResultJudgment", "ResultOutput"]);
        build.MissingResources.Should().Contain(item =>
            item.ResourceType == "image_source" &&
            item.ResourceKey == "op_cam.SourceType");
        build.MissingResources.Should().NotContain(item =>
            item.ResourceType == "camera_binding" || item.ResourceType == "image_file");
        build.ApplyGate.CanvasApplyReady.Should().BeTrue();
        build.ApplyGate.DeploymentReady.Should().BeFalse();
    }

    [Fact(DisplayName = "BuildFromPlan strict missing image source should block at final evaluator gate")]
    public async Task BuildAsync_StrictMissingImageSource_ShouldBlock()
    {
        var sink = new CapturingAgentRunEventSink();
        var applicationService = CreateBuildApplicationService(sink);
        var baseline = Plan(
            "surface_defect",
            ["ImageAcquisition", "SurfaceDefectDetection", "ResultJudgment", "ResultOutput"],
            "metal scratch inspection workflow");
        var plan = baseline with
        {
            CanBuild = false,
            BlockingReasons = ["hard_requirement:image_source_missing"],
            SemanticExtraction = baseline.SemanticExtraction! with
            {
                ImageSource = string.Empty
            },
            RequirementMaturity = baseline.RequirementMaturity! with
            {
                CanBuild = false,
                MissingFields = [VisionAgentPlanAnswerFields.ImageSource],
                BlockingReasons = ["image_source_missing"],
                PublicReason = "Image source is required before strict Build."
            }
        };
        plan = plan with
        {
            PlanHash = VisionAgentOrchestrator.ComputePlanHash(plan)
        };

        var result = (await applicationService.BuildAsync(
            BuildCommand.FromGenerationRequest(
                Request(plan, acceptedRecommendedDefaults: false, requirementMode: AiRequirementModes.Strict),
                transport: BuildCommandTransports.Internal,
                persistResult: false),
            CancellationToken.None)).Result;

        result.Success.Should().BeFalse();
        result.ClarificationRequired.Should().BeTrue();
        result.RequirementBrief!.BlockingClarificationFields.Should()
            .Contain(VisionAgentPlanAnswerFields.ImageSource);
    }

    [Fact(DisplayName = "BuildReadiness preview should allow draft defer with a repairable planner route")]
    public async Task PreviewBuildReadiness_StrictAndDraftDefer_ShouldUseCanonicalEvaluator()
    {
        var sink = new CapturingAgentRunEventSink();
        var applicationService = CreateBuildApplicationService(sink);
        var baseline = Plan(
            "surface_defect",
            ["ImageAcquisition", "SurfaceDefectDetection", "ResultJudgment"],
            "logo appearance defect workflow");
        var plan = baseline with
        {
            CanBuild = false,
            BlockingReasons = ["hard_requirement:image_source_missing"],
            ClarificationQuestions =
            [
                new VisionAgentClarificationQuestion
                {
                    Id = "image_source",
                    Field = VisionAgentPlanAnswerFields.ImageSource,
                    Title = "Image source",
                    Options =
                    [
                        new VisionAgentClarificationOption
                        {
                            Value = "camera_pending",
                            Label = "Keep pending",
                            Recommended = true,
                            AnswerEffect = VisionAgentClarificationAnswerEffects.Defer
                        }
                    ]
                },
                new VisionAgentClarificationQuestion
                {
                    Id = "ok_ng_rule",
                    Field = VisionAgentPlanAnswerFields.AcceptanceCriteria,
                    Title = "OK/NG rule",
                    Options =
                    [
                        new VisionAgentClarificationOption
                        {
                            Value = "threshold_pending",
                            Label = "Keep pending",
                            Recommended = true,
                            AnswerEffect = VisionAgentClarificationAnswerEffects.Defer
                        }
                    ]
                }
            ],
            SemanticExtraction = baseline.SemanticExtraction! with
            {
                ImageSource = string.Empty
            },
            RequirementMaturity = baseline.RequirementMaturity! with
            {
                CanPlan = true,
                CanBuild = false,
                MissingFields = [VisionAgentPlanAnswerFields.ImageSource],
                BlockingReasons = ["image_source_missing"]
            }
        };
        plan = plan with { PlanHash = VisionAgentOrchestrator.ComputePlanHash(plan) };
        var selections = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["image_source"] = "camera_pending",
            ["ok_ng_rule"] = "threshold_pending"
        };

        var strict = await applicationService.PreviewBuildReadinessAsync(
            PreviewRequest(plan, selections, requirementMode: AiRequirementModes.Strict, answerRevision: 7),
            CancellationToken.None);
        var draft = await applicationService.PreviewBuildReadinessAsync(
            PreviewRequest(plan, selections, requirementMode: AiRequirementModes.Draft, answerRevision: 8),
            CancellationToken.None);

        strict.BuildReadiness.CanBuild.Should().BeFalse();
        strict.DeferredQuestionIds.Should().BeEquivalentTo(["image_source", "ok_ng_rule"]);
        strict.AcceptedAnswers.Should().BeEmpty();
        draft.BuildReadiness.CanBuild.Should().BeTrue();
        draft.BuildReadiness.ResolvedFields.Should().NotContain(VisionAgentPlanAnswerFields.AcceptanceCriteria);
        draft.BuildReadiness.RemainingFields.Should().Contain(VisionAgentPlanAnswerFields.AcceptanceCriteria);
        draft.BuildReadiness.Blockers.Should().ContainSingle(blocker =>
            blocker.Resource != null &&
            blocker.Resource.ResourceType == "image_source" &&
            blocker.Resource.ResourceKey == "imageacquisition#1.SourceType" &&
            blocker.Category == VisionAgentBuildBlockerCategories.ResourcePending &&
            blocker.BlocksBuild == false);
        draft.BuildBlockingConfirmationCount.Should().Be(0);
        draft.BuildRequiredResourceCount.Should().Be(0);
        draft.DeferredFieldCount.Should().Be(1);
        draft.DraftAllowedResourceCount.Should().Be(1);
        draft.MustConfirmBeforeBuildCount.Should().Be(0);
        draft.FillLaterCount.Should().Be(2);
        draft.TotalIncompleteCount.Should().Be(2);
        draft.AnswerRevision.Should().Be(8);
        sink.Events.Should().BeEmpty();
    }

    [Fact(DisplayName = "Build application should retain a safe scaffold for preview while blocking apply")]
    public async Task BuildAsync_SafeScaffold_ShouldRemainPreviewOnly()
    {
        var applicationService = CreateBuildApplicationService(new CapturingAgentRunEventSink());
        var plan = Plan(
            "surface_defect",
            ["ImageAcquisition", "ResultJudgment", "ResultOutput"],
            "surface defect scaffold requiring manual completion");

        var outcome = await applicationService.BuildAsync(
            BuildCommand.FromGenerationRequest(
                Request(plan, requirementMode: AiRequirementModes.Draft),
                transport: BuildCommandTransports.Internal,
                persistResult: false),
            CancellationToken.None);

        outcome.Result.Success.Should().BeTrue();
        outcome.Result.Flow.Should().NotBeNull();
        outcome.Result.BuildResult.Should().NotBeNull();
        outcome.Result.BuildResult!.ApplyGate.CanvasApplyReady.Should().BeFalse();
        outcome.Result.BuildResult.ApplyGate.RuntimeDraftReady.Should().BeFalse();
        outcome.Result.BuildResult.ApplyGate.DeploymentReady.Should().BeFalse();
        outcome.Result.BuildResult.RouteSemanticsSatisfied.Should().BeFalse();
        outcome.Result.BuildResult.PublicWarnings.Should().Contain("safe_scaffold_requires_user_review");
    }

    [Fact(DisplayName = "Build application should reject a Blob-only template guidance route under route v2")]
    public async Task BuildAsync_BlobRobotGuidance_ShouldFailClosedUnderRouteV2()
    {
        var applicationService = CreateBuildApplicationService(new CapturingAgentRunEventSink());
        var plan = Plan(
            "template_location",
            ["ImageAcquisition", "RoiManager", "Thresholding", "BlobAnalysis", "ResultJudgment", "ResultOutput"],
            "Build a blob-analysis robot screw-driving guidance workflow.");

        var outcome = await applicationService.BuildAsync(
            BuildCommand.FromGenerationRequest(
                Request(plan, requirementMode: AiRequirementModes.Draft),
                transport: BuildCommandTransports.Internal,
                persistResult: false),
            CancellationToken.None);

        outcome.Result.Success.Should().BeFalse();
        outcome.Result.Flow.Should().BeNull();
        outcome.Result.BuildResult.Should().NotBeNull();
        outcome.Result.BuildResult!.RouteSemanticsSatisfied.Should().BeFalse();
        outcome.Result.BuildResult.ApplyGate.ApplyBlockers.Should().Contain("route_missing_task_processor");
    }

    [Fact(DisplayName = "BuildFromPlan strawberry draft should append a safe terminal output and round-trip the canvas flow")]
    public async Task BuildAsync_StrawberryDraftWithoutTerminalOutput_ShouldProduceEditableRoundTripFlow()
    {
        var applicationService = CreateBuildApplicationService(new CapturingAgentRunEventSink());
        var plan = StrawberryDraftBuildPlan();
        var selections = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["classification_strategy"] = "strategy_pending",
            ["ok_ng_rule"] = "threshold_pending",
            ["q_fallback_image_source"] = "camera_pending"
        };
        var confirmed = plan.ConfirmedPlanAnswers.ToList();

        var preview = await applicationService.PreviewBuildReadinessAsync(
            PreviewRequest(
                plan,
                selections,
                confirmed,
                requirementMode: AiRequirementModes.Draft),
            CancellationToken.None);
        var outcome = await applicationService.BuildAsync(
            BuildCommand.FromGenerationRequest(
                Request(
                    plan,
                    userSelections: selections,
                    confirmedAnswers: confirmed,
                    acceptedRecommendedDefaults: false,
                    requirementMode: AiRequirementModes.Draft),
                transport: BuildCommandTransports.Internal,
                persistResult: false),
            CancellationToken.None);

        preview.BuildReadiness.CanBuild.Should().BeTrue();
        outcome.Result.Success.Should().BeTrue();
        outcome.CompletionStatus.Should().Be(AiFlowGenerationResult.CompletionStatusCompleted);
        outcome.FailureCode.Should().BeEmpty();
        outcome.FailureType.Should().BeEmpty();
        outcome.Result.BuildResult.Should().NotBeNull();
        var build = outcome.Result.BuildResult!;
        var finalOperators = build.OperatorPipeline.Select(item => item.OperatorType).ToList();
        finalOperators.Should().Equal([
            "ImageAcquisition",
            "ColorConversion",
            "RoiManager",
            "DeepLearning",
            "ResultJudgment",
            "ResultOutput"
        ]);
        build.EffectiveOperators.Should().Equal(finalOperators);
        build.OperatorPipeline.Where(step => step.Source == "repair")
            .Should().Contain(step => step.OperatorType == "ResultJudgment" && step.RepairNote == "draft_result_chain_added")
            .And.Contain(step => step.OperatorType == "ResultOutput" && step.RepairNote == "draft_result_chain_added");
        build.PublicWarnings.Should().Contain("draft_terminal_output_added");
        build.PublicWarnings.Should().Contain("draft_result_judgment_added");

        var normalized = VisionAgentFlowDraftNormalizer.Normalize(
            JsonSerializer.SerializeToElement(new { flow = build.WorkflowDraft }),
            new VisionAgentToolContext());
        normalized.Success.Should().BeTrue();
        var validation = VisionAgentFlowDraftValidator.Validate(normalized.Flow);
        validation.BlockingIssues.Should().BeEmpty();
        validation.BlockingIssues.Should().NotContain(issue =>
            issue.Code.Equals("missing_required_input", StringComparison.OrdinalIgnoreCase));

        var flow = Flow(outcome.Result);
        var resultOutput = flow.Operators.Should()
            .ContainSingle(op => op.Type == OperatorType.ResultOutput)
            .Which;
        resultOutput.Parameters.Should().ContainSingle(parameter =>
            parameter.Name == "SaveToFile" &&
            parameter.Value != null &&
            string.Equals(parameter.Value.ToString(), "false", StringComparison.OrdinalIgnoreCase));
        var flowJson = JsonSerializer.Serialize(flow);
        var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var canvasFlow = JsonSerializer.Deserialize<OperatorFlowDto>(flowJson, jsonOptions);
        canvasFlow.Should().NotBeNull();
        canvasFlow!.ToEntity().Operators.Should().HaveCount(flow.Operators.Count);

        var storagePath = Path.Combine(Path.GetTempPath(), $"ClearVision-strawberry-build-{Guid.NewGuid():N}");
        try
        {
            var storage = new ClearVision.Product.Infrastructure.Services.JsonFileProjectFlowStorage(storagePath);
            await storage.SaveFlowJsonAsync(flow.Id, flowJson, 1);
            var restoredJson = await storage.LoadFlowJsonAsync(flow.Id);
            restoredJson.Should().NotBeNullOrWhiteSpace();
            var restoredFlow = JsonSerializer.Deserialize<OperatorFlowDto>(restoredJson!, jsonOptions);
            restoredFlow.Should().NotBeNull();
            restoredFlow!.ToEntity().Operators.Should().ContainSingle(op => op.Type == OperatorType.ResultOutput);
        }
        finally
        {
            if (Directory.Exists(storagePath)) Directory.Delete(storagePath, recursive: true);
        }

        build.ApplyGate.CanvasApplyReady.Should().BeTrue();
        build.ApplyGate.DeploymentReady.Should().BeFalse();
        build.MissingResources.Select(resource => resource.ResourceType).Should().BeEquivalentTo([
            "image_source",
            "model_resource"
        ]);
        var runAllowed = build.ApplyGate.RuntimeDraftReady && build.MissingResources.Count == 0;
        runAllowed.Should().BeFalse();
    }

    [Fact(DisplayName = "BuildReadiness preview should keep station camera resource pending in strict and draft")]
    public async Task PreviewBuildReadiness_StationCamera_ShouldKeepCameraBindingResourcePending()
    {
        var applicationService = CreateBuildApplicationService(new CapturingAgentRunEventSink());
        var baseline = Plan(
            "surface_defect",
            ["ImageAcquisition", "SurfaceDefectDetection", "ResultJudgment", "ResultOutput"],
            "station camera scratch inspection workflow");
        var plan = baseline with
        {
            CanBuild = false,
            BlockingReasons = ["hard_requirement:image_source_missing"],
            ClarificationQuestions =
            [
                new VisionAgentClarificationQuestion
                {
                    Id = "image_source",
                    Field = VisionAgentPlanAnswerFields.ImageSource,
                    Title = "Image source",
                    Options =
                    [
                        new VisionAgentClarificationOption
                        {
                            Value = "station_camera",
                            Label = "Station camera",
                            Recommended = true,
                            AnswerEffect = VisionAgentClarificationAnswerEffects.ResolveField
                        }
                    ]
                }
            ],
            SemanticExtraction = baseline.SemanticExtraction! with
            {
                ImageSource = string.Empty
            },
            RequirementMaturity = baseline.RequirementMaturity! with
            {
                CanPlan = true,
                CanBuild = false,
                MissingFields = [VisionAgentPlanAnswerFields.ImageSource],
                BlockingReasons = ["image_source_missing"]
            }
        };
        plan = plan with { PlanHash = VisionAgentOrchestrator.ComputePlanHash(plan) };
        var answer = PlanAnswer("image_source", VisionAgentPlanAnswerFields.ImageSource, "station_camera");

        var strict = await applicationService.PreviewBuildReadinessAsync(
            PreviewRequest(plan, confirmedAnswers: [answer], requirementMode: AiRequirementModes.Strict),
            CancellationToken.None);
        var draft = await applicationService.PreviewBuildReadinessAsync(
            PreviewRequest(plan, confirmedAnswers: [answer], requirementMode: AiRequirementModes.Draft),
            CancellationToken.None);

        strict.BuildReadiness.CanBuild.Should().BeFalse();
        strict.BuildReadiness.Blockers.Should().ContainSingle(blocker =>
            blocker.Resource != null &&
            blocker.Resource.ResourceType == "camera_binding" &&
            blocker.Resource.ResourceKey == "imageacquisition#1.CameraBindingId" &&
            blocker.BlocksBuild);
        strict.DraftAllowedResourceCount.Should().Be(1);
        strict.MustConfirmBeforeBuildCount.Should().Be(1);
        strict.FillLaterCount.Should().Be(0);
        strict.TotalIncompleteCount.Should().Be(1);
        draft.BuildReadiness.CanBuild.Should().BeTrue();
        draft.BuildReadiness.Blockers.Should().ContainSingle(blocker =>
            blocker.Resource != null &&
            blocker.Resource.ResourceType == "camera_binding" &&
            blocker.Resource.ResourceKey == "imageacquisition#1.CameraBindingId" &&
            blocker.BlocksBuild == false);
        draft.AcceptedAnswers.Should().ContainSingle(accepted =>
            accepted.Field == VisionAgentPlanAnswerFields.ImageSource &&
            accepted.Value == "station_camera");

        var partition = JsonSerializer.SerializeToElement(draft);
        partition.GetProperty("buildBlockingConfirmationCount").GetInt32().Should().Be(0);
        partition.GetProperty("buildRequiredResourceCount").GetInt32().Should().Be(0);
        partition.GetProperty("deferredFieldCount").GetInt32().Should().Be(0);
        partition.GetProperty("draftAllowedResourceCount").GetInt32().Should().Be(1);
        partition.GetProperty("mustConfirmBeforeBuildCount").GetInt32().Should().Be(0);
        partition.GetProperty("fillLaterCount").GetInt32().Should().Be(1);
        partition.GetProperty("totalIncompleteCount").GetInt32().Should().Be(1);

        var bound = await applicationService.PreviewBuildReadinessAsync(
            PreviewRequest(
                plan,
                confirmedAnswers: [answer],
                requirementMode: AiRequirementModes.Strict,
                resourceDecisions:
                [
                    new VisionAgentResourceDecision
                    {
                        CanonicalId = draft.BuildReadiness.MissingResources.Single().CanonicalId,
                        ResourceType = "camera_binding",
                        OperatorKey = "imageacquisition#1",
                        ParameterName = "CameraBindingId",
                        Status = VisionAgentResourceStatuses.Bound
                    }
                ]),
            CancellationToken.None);
        bound.BuildReadiness.CanBuild.Should().BeTrue();
        bound.ResourcePendingCount.Should().Be(0);
        bound.MustConfirmBeforeBuildCount.Should().Be(0);
        bound.FillLaterCount.Should().Be(0);
        bound.TotalIncompleteCount.Should().Be(0);
    }

    [Fact(DisplayName = "BuildReadiness count partition should be disjoint for fields and build-required resources")]
    public async Task PreviewBuildReadiness_CountPartition_ShouldCoverStrictDraftAndBoundResources()
    {
        var applicationService = CreateBuildApplicationService(new CapturingAgentRunEventSink());
        var baseline = Plan(
            "surface_defect",
            ["ImageAcquisition", "SurfaceDefectDetection", "ResultJudgment", "ResultOutput"]);
        var fieldPlan = baseline with
        {
            CanBuild = false,
            BlockingReasons = ["hard_requirement:acceptance_criteria_missing"],
            RemainingPlanFields = [VisionAgentPlanAnswerFields.AcceptanceCriteria],
            SemanticExtraction = baseline.SemanticExtraction! with { OkCondition = string.Empty, NgCondition = string.Empty },
            RequirementMaturity = baseline.RequirementMaturity! with
            {
                CanPlan = true,
                CanBuild = false,
                MissingFields = [VisionAgentPlanAnswerFields.AcceptanceCriteria],
                BlockingReasons = ["acceptance_criteria_missing"]
            }
        };
        fieldPlan = fieldPlan with { PlanHash = VisionAgentOrchestrator.ComputePlanHash(fieldPlan) };

        var strictField = await applicationService.PreviewBuildReadinessAsync(
            PreviewRequest(fieldPlan, requirementMode: AiRequirementModes.Strict),
            CancellationToken.None);
        var draftField = await applicationService.PreviewBuildReadinessAsync(
            PreviewRequest(fieldPlan, requirementMode: AiRequirementModes.Draft),
            CancellationToken.None);

        strictField.BuildBlockingConfirmationCount.Should().Be(1);
        strictField.DeferredFieldCount.Should().Be(0);
        strictField.MustConfirmBeforeBuildCount.Should().Be(1);
        strictField.FillLaterCount.Should().Be(0);
        strictField.TotalIncompleteCount.Should().Be(1);
        draftField.BuildBlockingConfirmationCount.Should().Be(0);
        draftField.DeferredFieldCount.Should().Be(1);
        draftField.MustConfirmBeforeBuildCount.Should().Be(0);
        draftField.FillLaterCount.Should().Be(1);
        draftField.TotalIncompleteCount.Should().Be(1);

        var resourcePlan = baseline with
        {
            CanBuild = false,
            BlockingReasons =
            [
                "resource_pending:plc_output_missing",
                "resource_pending:resource:v1|plc_output|resultoutput#1|outputchannel"
            ]
        };
        resourcePlan = resourcePlan with { PlanHash = VisionAgentOrchestrator.ComputePlanHash(resourcePlan) };
        var pendingResource = await applicationService.PreviewBuildReadinessAsync(
            PreviewRequest(resourcePlan, requirementMode: AiRequirementModes.Draft),
            CancellationToken.None);

        pendingResource.BuildRequiredResourceCount.Should().Be(1);
        pendingResource.DraftAllowedResourceCount.Should().Be(0);
        pendingResource.MustConfirmBeforeBuildCount.Should().Be(1);
        pendingResource.TotalIncompleteCount.Should().Be(1);
        pendingResource.BuildReadiness.MissingResources.Should().ContainSingle();

        var boundResource = await applicationService.PreviewBuildReadinessAsync(
            PreviewRequest(
                resourcePlan,
                requirementMode: AiRequirementModes.Draft,
                resourceDecisions:
                [
                    new VisionAgentResourceDecision
                    {
                        CanonicalId = pendingResource.BuildReadiness.MissingResources.Single().CanonicalId,
                        ResourceType = "plc_output",
                        OperatorKey = "resultoutput#1",
                        ParameterName = "OutputChannel",
                        Status = VisionAgentResourceStatuses.Bound
                    }
                ]),
            CancellationToken.None);
        boundResource.ResourcePendingCount.Should().Be(0);
        boundResource.MustConfirmBeforeBuildCount.Should().Be(0);
        boundResource.FillLaterCount.Should().Be(0);
        boundResource.TotalIncompleteCount.Should().Be(0);
    }

    [Fact(DisplayName = "BuildReadiness preview should block external output in strict and draft")]
    public async Task PreviewBuildReadiness_ExternalOutput_ShouldBlockBothModes()
    {
        var applicationService = CreateBuildApplicationService(new CapturingAgentRunEventSink());
        var plan = ExternalMesOutputPlan();

        var strict = await applicationService.PreviewBuildReadinessAsync(
            PreviewRequest(plan, requirementMode: AiRequirementModes.Strict),
            CancellationToken.None);
        var draft = await applicationService.PreviewBuildReadinessAsync(
            PreviewRequest(plan, requirementMode: AiRequirementModes.Draft),
            CancellationToken.None);

        strict.BuildReadiness.CanBuild.Should().BeFalse();
        draft.BuildReadiness.CanBuild.Should().BeFalse();
        strict.BuildReadiness.Blockers.Should().Contain(blocker =>
            blocker.Field == VisionAgentPlanAnswerFields.OutputTarget &&
            blocker.BlocksBuild);
        draft.BuildReadiness.Blockers.Should().Contain(blocker =>
            blocker.Field == VisionAgentPlanAnswerFields.OutputTarget &&
            blocker.BlocksBuild);
    }

    [Fact(DisplayName = "BuildReadiness preview should match BuildFromPlan readiness and fingerprint")]
    public async Task PreviewBuildReadiness_ShouldMatchBuildFromPlanFinalGate()
    {
        var sink = new CapturingAgentRunEventSink();
        var applicationService = CreateBuildApplicationService(sink);
        var plan = PlanWithStrategyConfirmationBlocker();
        var request = Request(
            plan,
            userSelections: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            acceptedRecommendedDefaults: false);

        var preview = await applicationService.PreviewBuildReadinessAsync(
            PreviewRequest(
                plan,
                userSelections: request.BuildFromPlan!.UserSelections,
                confirmedAnswers: request.BuildFromPlan.ConfirmedAnswers,
                acceptedRecommendedDefaults: request.BuildFromPlan.AcceptedRecommendedDefaults,
                requirementMode: request.RequirementMode,
                answerRevision: 42),
            CancellationToken.None);
        var build = await applicationService.BuildAsync(
            BuildCommand.FromGenerationRequest(request, transport: BuildCommandTransports.Internal, persistResult: false),
            CancellationToken.None);

        preview.BuildReadiness.Should().BeEquivalentTo(build.Result.BuildReadiness);
        preview.AnswerSetFingerprint.Should().Be(build.Result.AnswerSetFingerprint);
        preview.AcceptedAnswers.Should().BeEquivalentTo(
            new VisionAgentPlanAnswerValidator().Validate(
                plan,
                request.BuildFromPlan.ConfirmedAnswers,
                request.BuildFromPlan.UserSelections,
                request.BuildFromPlan.AcceptedRecommendedDefaults).AcceptedAnswers);
    }

    [Fact(DisplayName = "BuildReadiness preview should fail closed on stale plan hash without changing answer revision")]
    public async Task PreviewBuildReadiness_StalePlanHash_ShouldReturnControlledFailure()
    {
        var applicationService = CreateBuildApplicationService(new CapturingAgentRunEventSink());
        var plan = PlanWithStrategyConfirmationBlocker();

        var preview = await applicationService.PreviewBuildReadinessAsync(
            PreviewRequest(plan, planHashOverride: "sha256:stale", answerRevision: 99),
            CancellationToken.None);

        preview.ContractValid.Should().BeFalse();
        preview.FailureCode.Should().Be(VisionAgentBuildFailureCodes.StalePlan);
        preview.AnswerRevision.Should().Be(99);
        preview.BuildReadiness.CanBuild.Should().BeFalse();
        preview.BuildReadiness.Blockers.Should().ContainSingle(blocker =>
            blocker.Id == VisionAgentBuildFailureCodes.StalePlan &&
            blocker.BlocksBuild);
    }

    [Fact(DisplayName = "Requirement mode and answer revision should not participate in PlanHash")]
    public void PreviewInputs_ShouldNotParticipateInPlanHash()
    {
        var plan = PlanWithStrategyConfirmationBlocker();

        var strict = PreviewRequest(plan, requirementMode: AiRequirementModes.Strict, answerRevision: 1);
        var draft = PreviewRequest(plan, requirementMode: AiRequirementModes.Draft, answerRevision: 2);

        VisionAgentOrchestrator.ComputePlanHash(strict.PlanSnapshot).Should().Be(plan.PlanHash);
        VisionAgentOrchestrator.ComputePlanHash(draft.PlanSnapshot).Should().Be(plan.PlanHash);
    }

    [Fact(DisplayName = "BuildFromPlan strict answered image source should build")]
    public async Task BuildAsync_StrictImageSourceAnswered_ShouldBuild()
    {
        var orchestrator = CreateOrchestrator(new CapturingAgentRunEventSink());
        var baseline = Plan(
            "surface_defect",
            ["ImageAcquisition", "SurfaceDefectDetection", "ResultJudgment", "ResultOutput"],
            "metal scratch inspection workflow");
        var plan = baseline with
        {
            CanBuild = false,
            BlockingReasons = ["hard_requirement:image_source_missing"],
            ClarificationQuestions =
            [
                new VisionAgentClarificationQuestion
                {
                    Id = "image_source",
                    Field = VisionAgentPlanAnswerFields.ImageSource,
                    Title = "Image source",
                    DefaultValue = "camera",
                    Options =
                    [
                        new VisionAgentClarificationOption
                        {
                            Value = "camera",
                            Label = "Camera",
                            Recommended = true
                        }
                    ]
                }
            ],
            SemanticExtraction = baseline.SemanticExtraction! with
            {
                ImageSource = string.Empty
            },
            RequirementMaturity = baseline.RequirementMaturity! with
            {
                CanBuild = false,
                MissingFields = [VisionAgentPlanAnswerFields.ImageSource],
                BlockingReasons = ["image_source_missing"]
            }
        };
        plan = plan with
        {
            PlanHash = VisionAgentOrchestrator.ComputePlanHash(plan)
        };

        var result = await orchestrator.BuildAsync(
            Request(
                plan,
                confirmedAnswers:
                [
                    PlanAnswer("image_source", VisionAgentPlanAnswerFields.ImageSource, "camera")
                ],
                userSelections: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                acceptedRecommendedDefaults: false,
                requirementMode: AiRequirementModes.Strict),
            CancellationToken.None);

        result.Success.Should().BeTrue();
        result.BuildResult!.ResolvedFields.Should().Contain(VisionAgentPlanAnswerFields.ImageSource);
        result.BuildResult.ApplyGate.CanvasApplyReady.Should().BeTrue();
    }

    [Fact(DisplayName = "BuildFromPlan local rapid inspection should not require output target confirmation")]
    public async Task BuildAsync_LocalRapidInspection_ShouldNotRequireOutputTarget()
    {
        var orchestrator = CreateOrchestrator(new CapturingAgentRunEventSink());
        var baseline = Plan(
            "classification",
            ["ImageAcquisition", "DeepLearning", "ResultJudgment", "ResultOutput"],
            "rapid inspection of capital letters");
        var plan = baseline with
        {
            CanBuild = false,
            BlockingReasons = ["strategy_confirmation:output_target_missing"],
            ClarificationQuestions =
            [
                new VisionAgentClarificationQuestion
                {
                    Id = "output_target",
                    Field = VisionAgentPlanAnswerFields.OutputTarget,
                    Title = "Output target",
                    DefaultValue = "local_result_payload",
                    Options =
                    [
                        new VisionAgentClarificationOption
                        {
                            Value = "local_result_payload",
                            Label = "Local structured output",
                            Recommended = true
                        }
                    ]
                }
            ],
            SemanticExtraction = baseline.SemanticExtraction! with
            {
                OutputTarget = string.Empty
            }
        };
        plan = plan with
        {
            PlanHash = VisionAgentOrchestrator.ComputePlanHash(plan)
        };

        var result = await orchestrator.BuildAsync(
            Request(plan, acceptedRecommendedDefaults: false, requirementMode: AiRequirementModes.Strict),
            CancellationToken.None);

        result.Success.Should().BeTrue();
        result.BuildResult!.ApplyGate.CanvasApplyReady.Should().BeTrue();
    }

    [Fact(DisplayName = "BuildFromPlan old planner candidate marker should not be reblocked by PlanSelection")]
    public async Task BuildAsync_LegacyPlannerCandidateNotBuildableWarning_ShouldBuild()
    {
        var orchestrator = CreateOrchestrator(new CapturingAgentRunEventSink());
        var baseline = Plan(
            "surface_defect",
            ["ImageAcquisition", "SurfaceDefectDetection", "ResultJudgment", "ResultOutput"],
            "metal scratch inspection workflow");
        var plan = baseline with
        {
            CanBuild = false,
            BlockingReasons = ["strategy_confirmation:planner_candidate_not_buildable"],
            ClarificationQuestions = [],
            BuildReadiness = new VisionAgentBuildReadinessSnapshot
            {
                CanBuild = true,
                Blockers =
                [
                    new VisionAgentBuildBlocker
                    {
                        Id = "contract_warning:planner_candidate_not_buildable",
                        Category = VisionAgentBuildBlockerCategories.ContractWarning,
                        BlocksBuild = false,
                        ResolutionMode = VisionAgentBuildBlockerResolutionModes.NonBlocking,
                        PublicLabel = "Planner candidate warning retained."
                    }
                ],
                ResolvedFields =
                [
                    VisionAgentPlanAnswerFields.InspectionObject,
                    VisionAgentPlanAnswerFields.TaskType,
                    VisionAgentPlanAnswerFields.ImageSource,
                    VisionAgentPlanAnswerFields.AcceptanceCriteria
                ],
                RemainingFields = [],
                PrimaryMessage = "Plan ready.",
                ContractVersion = VisionAgentPlanContractVersions.V2
            },
            RequirementMaturity = baseline.RequirementMaturity! with
            {
                Maturity = AiRequirementMaturity.Actionable,
                CanPlan = true,
                CanBuild = true,
                MissingFields = [],
                BlockingReasons = [],
                PublicReason = "Requirement is actionable."
            }
        };
        plan = plan with
        {
            PlanHash = VisionAgentOrchestrator.ComputePlanHash(plan)
        };

        var result = await orchestrator.BuildAsync(
            Request(plan, acceptedRecommendedDefaults: false, requirementMode: AiRequirementModes.Strict),
            CancellationToken.None);

        result.Success.Should().BeTrue();
        result.BuildResult!.UnresolvedStrategyBlockers.Should().BeEmpty();
        result.BuildResult.OperatorPipeline.Select(item => item.OperatorType)
            .Should()
            .Contain(["ImageAcquisition", "SurfaceDefectDetection", "ResultJudgment", "ResultOutput"]);
        result.BuildResult.ApplyGate.CanvasApplyReady.Should().BeTrue();
    }

    [Fact(DisplayName = "BuildFromPlan local connector mating wording should not require output target")]
    public async Task BuildAsync_LocalConnectorMatingInspection_ShouldNotRequireOutputTarget()
    {
        var orchestrator = CreateOrchestrator(new CapturingAgentRunEventSink());
        var baseline = Plan(
            "surface_defect",
            ["ImageAcquisition", "SurfaceDefectDetection", "ResultJudgment", "ResultOutput"],
            "检测连接器对接到位和外部标签缺失");
        var plan = baseline with
        {
            CanBuild = false,
            BlockingReasons = ["strategy_confirmation:output_target_missing"],
            ClarificationQuestions =
            [
                new VisionAgentClarificationQuestion
                {
                    Id = "output_target",
                    Field = VisionAgentPlanAnswerFields.OutputTarget,
                    Title = "Output target",
                    DefaultValue = "local_result_payload",
                    Options =
                    [
                        new VisionAgentClarificationOption
                        {
                            Value = "local_result_payload",
                            Label = "Local structured output",
                            Recommended = true
                        }
                    ]
                }
            ],
            SemanticExtraction = baseline.SemanticExtraction! with
            {
                OutputTarget = string.Empty
            }
        };
        plan = plan with
        {
            PlanHash = VisionAgentOrchestrator.ComputePlanHash(plan)
        };

        var result = await orchestrator.BuildAsync(
            Request(plan, acceptedRecommendedDefaults: false, requirementMode: AiRequirementModes.Strict),
            CancellationToken.None);

        result.Success.Should().BeTrue();
        result.BuildResult!.ApplyGate.CanvasApplyReady.Should().BeTrue();
    }

    [Fact(DisplayName = "BuildFromPlan explicit MES output should block until answered")]
    public async Task BuildAsync_ExplicitMesOutputWithoutAnswer_ShouldBlock()
    {
        var sink = new CapturingAgentRunEventSink();
        var applicationService = CreateBuildApplicationService(sink);
        var plan = ExternalMesOutputPlan();

        var result = (await applicationService.BuildAsync(
            BuildCommand.FromGenerationRequest(
                Request(plan, acceptedRecommendedDefaults: false, requirementMode: AiRequirementModes.Strict),
                transport: BuildCommandTransports.Internal,
                persistResult: false),
            CancellationToken.None)).Result;

        result.Success.Should().BeFalse();
        result.ClarificationRequired.Should().BeTrue();
        result.FailureSummary!.Code.Should().Be(VisionAgentBuildFailureCodes.ReadinessBlocked);
        result.BuildReadiness!.Blockers.Should()
            .Contain(blocker => blocker.Field == VisionAgentPlanAnswerFields.OutputTarget);
    }

    [Fact(DisplayName = "BuildFromPlan explicit MES output should build after answer")]
    public async Task BuildAsync_ExplicitMesOutputAnswered_ShouldBuild()
    {
        var orchestrator = CreateOrchestrator(new CapturingAgentRunEventSink());
        var plan = ExternalMesOutputPlan();

        var result = await orchestrator.BuildAsync(
            Request(
                plan,
                confirmedAnswers:
                [
                    PlanAnswer("output_target", VisionAgentPlanAnswerFields.OutputTarget, "business_system_output")
                ],
                userSelections: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                acceptedRecommendedDefaults: false,
                requirementMode: AiRequirementModes.Strict),
            CancellationToken.None);

        result.Success.Should().BeTrue();
        result.BuildResult!.ResolvedFields.Should().Contain(VisionAgentPlanAnswerFields.OutputTarget);
        result.BuildResult.ApplyGate.CanvasApplyReady.Should().BeTrue();
    }

    [Fact(DisplayName = "BuildFromPlan should synthesize EffectiveRequirement from rule fallback semantic and confirmed answers")]
    public async Task BuildAsync_RuleFallbackSemanticWithConfirmedAnswers_ShouldBuildFromEffectiveRequirement()
    {
        var orchestrator = CreateOrchestrator(new CapturingAgentRunEventSink());
        var baseline = Plan(
            "surface_defect",
            ["ImageAcquisition", "SurfaceDefectDetection", "ResultJudgment", "ResultOutput"],
            "start build from confirmed plan");
        var plan = baseline with
        {
            CanBuild = false,
            BlockingReasons =
            [
                "hard_requirement:inspection_object_missing",
                "hard_requirement:task_type_missing",
                "hard_requirement:image_source_missing",
                "hard_requirement:acceptance_criteria_missing"
            ],
            SemanticExtraction = new VisionAgentSemanticExtractionResult
            {
                IsVisionRequest = true,
                Intent = "new_flow",
                TaskType = AiVisionTaskTypes.Unknown,
                Source = VisionAgentSemanticSources.RuleFallback,
                MetadataOnly = true
            },
            RequirementMaturity = new AiRequirementMaturityResult
            {
                Maturity = AiRequirementMaturity.Ambiguous,
                TaskType = AiVisionTaskTypes.Unknown,
                CanPlan = false,
                CanBuild = false,
                MissingFields = ["inspection_object", "task_type", "image_source", "acceptance_criteria"],
                BlockingReasons = ["inspection_object_missing", "task_type_missing", "image_source_missing", "acceptance_criteria_missing"],
                PublicReason = "Legacy plan snapshot was not buildable before answers were applied."
            }
        };
        plan = plan with
        {
            PlanHash = VisionAgentOrchestrator.ComputePlanHash(plan)
        };

        var result = await orchestrator.BuildAsync(
            Request(
                plan,
                confirmedAnswers:
                [
                    PlanAnswer(string.Empty, VisionAgentPlanAnswerFields.InspectionObject, "logo area", VisionAgentPlanAnswerOrigins.ExplicitUserText),
                    PlanAnswer(string.Empty, VisionAgentPlanAnswerFields.TaskType, AiVisionTaskTypes.SurfaceDefect, VisionAgentPlanAnswerOrigins.ExplicitUserSelection),
                    PlanAnswer(string.Empty, VisionAgentPlanAnswerFields.ImageSource, "camera", VisionAgentPlanAnswerOrigins.ExplicitUserText),
                    PlanAnswer(string.Empty, VisionAgentPlanAnswerFields.AcceptanceCriteria, "scratch is NG", VisionAgentPlanAnswerOrigins.ExplicitUserText)
                ],
                userSelections: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                acceptedRecommendedDefaults: false,
                requirementMode: AiRequirementModes.Strict),
            CancellationToken.None);

        result.Success.Should().BeTrue();
        var build = result.BuildResult!;
        build.ResolvedFields.Should().Contain([
            VisionAgentPlanAnswerFields.InspectionObject,
            VisionAgentPlanAnswerFields.TaskType,
            VisionAgentPlanAnswerFields.ImageSource,
            VisionAgentPlanAnswerFields.AcceptanceCriteria]);
        build.RemainingFields.Should().NotContain(VisionAgentPlanAnswerFields.InspectionObject);
        build.ApplyGate.CanvasApplyReady.Should().BeTrue();
        build.ToolEvidenceTimeline.Should().Contain(item =>
            item.Stage == "plan_generation" &&
            item.ToolName == "plan_snapshot_loader");
    }

    [Fact(DisplayName = "BuildFromPlan should clear aliased strategy blocker with confirmed requirement answer")]
    public async Task BuildAsync_AliasedStrategyBlockerWithRequirementAnswer_ShouldBuild()
    {
        var orchestrator = CreateOrchestrator(new CapturingAgentRunEventSink());
        var baseline = Plan(
            "surface_defect",
            ["ImageAcquisition", "SurfaceDefectDetection", "ResultJudgment", "ResultOutput"],
            "medical lesion inspection workflow");
        var plan = baseline with
        {
            CanBuild = false,
            BlockingReasons = ["strategy_confirmation:medical_modality_and_lesion_type_missing"],
            ClarificationQuestions =
            [
                new VisionAgentClarificationQuestion
                {
                    Id = "medical_modality_and_lesion_type",
                    Field = "medical_modality_and_lesion_type",
                    Title = "Medical modality and lesion type",
                    Why = "This determines the executable task type.",
                    DefaultValue = AiVisionTaskTypes.SurfaceDefect,
                    DefaultAssumption = "Treat suspicious lesions as a surface defect style inspection draft.",
                    Impact = "Model resources remain pending metadata.",
                    Options =
                    [
                        new VisionAgentClarificationOption
                        {
                            Value = AiVisionTaskTypes.SurfaceDefect,
                            Label = "Lesion defect detection",
                            Recommended = true,
                            Description = "Build a suspected lesion detection draft.",
                            Impact = "Editable draft can continue."
                        },
                        new VisionAgentClarificationOption
                        {
                            Value = AiVisionTaskTypes.AttributeClassification,
                            Label = "Attribute classification",
                            Recommended = false,
                            Description = "Classify image-level lesion attributes.",
                            Impact = "Operator strategy changes."
                        }
                    ]
                }
            ]
        };
        plan = plan with
        {
            PlanHash = VisionAgentOrchestrator.ComputePlanHash(plan)
        };

        var result = await orchestrator.BuildAsync(
            Request(
                plan,
                confirmedAnswers:
                [
                    PlanAnswer(
                        "medical_modality_and_lesion_type",
                        VisionAgentPlanAnswerFields.TaskType,
                        AiVisionTaskTypes.SurfaceDefect)
                ],
                userSelections: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                acceptedRecommendedDefaults: false,
                requirementMode: AiRequirementModes.Strict),
            CancellationToken.None);

        result.Success.Should().BeTrue();
        var build = result.BuildResult!;
        build.UnresolvedStrategyBlockers.Should().BeEmpty();
        build.ResolvedFields.Should().Contain(VisionAgentPlanAnswerFields.TaskType);
        build.ApplyGate.CanvasApplyReady.Should().BeTrue();
    }

    [Fact(DisplayName = "BuildFromPlan should clear unknown strategy blocker when matching question is answered")]
    public async Task BuildAsync_UnknownStrategyBlockerWithMatchingQuestionAnswer_ShouldBuild()
    {
        var orchestrator = CreateOrchestrator(new CapturingAgentRunEventSink());
        var baseline = Plan(
            "surface_defect",
            ["ImageAcquisition", "SurfaceDefectDetection", "ResultJudgment", "ResultOutput"],
            "custom line guidance inspection workflow");
        var plan = baseline with
        {
            CanBuild = false,
            BlockingReasons = ["strategy_confirmation:line_guidance_profile_missing"],
            ClarificationQuestions =
            [
                new VisionAgentClarificationQuestion
                {
                    Id = "line_guidance_profile",
                    Field = "line_guidance_profile",
                    Title = "Line guidance profile",
                    Why = "A new industry-specific profile gates the planner route.",
                    DefaultValue = "profile_a",
                    DefaultAssumption = "Use profile A for the first editable draft.",
                    Impact = "Parameters remain editable.",
                    Options =
                    [
                        new VisionAgentClarificationOption
                        {
                            Value = "profile_a",
                            Label = "Profile A",
                            Recommended = true,
                            Description = "Use profile A.",
                            Impact = "Editable draft can continue."
                        },
                        new VisionAgentClarificationOption
                        {
                            Value = "profile_b",
                            Label = "Profile B",
                            Recommended = false,
                            Description = "Use profile B.",
                            Impact = "Different parameters are selected."
                        }
                    ]
                }
            ]
        };
        plan = plan with
        {
            PlanHash = VisionAgentOrchestrator.ComputePlanHash(plan)
        };

        var result = await orchestrator.BuildAsync(
            Request(
                plan,
                confirmedAnswers:
                [
                    PlanAnswer(
                        "line_guidance_profile",
                        VisionAgentPlanAnswerFields.AlgorithmStrategy,
                        "profile_a")
                ],
                userSelections: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                acceptedRecommendedDefaults: false,
                requirementMode: AiRequirementModes.Strict),
            CancellationToken.None);

        result.Success.Should().BeTrue();
        var build = result.BuildResult!;
        build.UnresolvedStrategyBlockers.Should().BeEmpty();
        build.StrategyConfirmed.Should().BeTrue();
        build.StrategyConfirmationSource.Should().Be("user_selection");
        build.ApplyGate.CanvasApplyReady.Should().BeTrue();
    }

    [Fact(DisplayName = "BuildFromPlan draft should block when object and task are both empty")]
    public async Task BuildAsync_DraftWithEmptyObjectAndTask_ShouldBlock()
    {
        var sink = new CapturingAgentRunEventSink();
        var applicationService = CreateBuildApplicationService(sink);
        var baseline = Plan(
            "surface_defect",
            ["ImageAcquisition", "SurfaceDefectDetection", "ResultJudgment", "ResultOutput"],
            "safe editable draft");
        var plan = baseline with
        {
            CanBuild = false,
            SemanticExtraction = baseline.SemanticExtraction! with
            {
                TaskType = AiVisionTaskTypes.Unknown,
                InspectionObject = string.Empty,
                TargetAttribute = string.Empty,
                DefectType = string.Empty,
                MeasurementTarget = string.Empty,
                ObjectSignals = [],
                TaskSignals = [],
                CanPlanCandidate = true,
                CanBuildCandidate = false
            },
            RequirementMaturity = baseline.RequirementMaturity! with
            {
                CanPlan = true,
                CanBuild = false,
                ObjectSignals = [],
                TaskSignals = [],
                MissingFields = ["inspection_object", "task_type"],
                BlockingReasons = ["inspection_object_missing", "task_type_missing"]
            }
        };
        plan = plan with
        {
            PlanHash = VisionAgentOrchestrator.ComputePlanHash(plan)
        };

        var result = (await applicationService.BuildAsync(
            BuildCommand.FromGenerationRequest(
                Request(plan, acceptedRecommendedDefaults: false, requirementMode: AiRequirementModes.Draft),
                transport: BuildCommandTransports.Internal,
                persistResult: false),
            CancellationToken.None)).Result;

        result.Success.Should().BeFalse();
        result.ClarificationRequired.Should().BeTrue();
        result.FailureSummary!.Code.Should().Be(VisionAgentBuildFailureCodes.ReadinessBlocked);
        result.BuildReadiness!.Blockers.Select(blocker => blocker.Field).Should()
            .Contain(VisionAgentPlanAnswerFields.InspectionObject)
            .And
            .Contain(VisionAgentPlanAnswerFields.TaskType);
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

    [Fact(DisplayName = "Plan should keep source blocking reasons separate from derived readiness blockers")]
    public async Task CreatePlanAsync_MissingCamera_ShouldNotWriteReadinessIdsBackToBlockingReasons()
    {
        const string prompt = "检测金属表面划痕，划痕为 NG，输入源使用现场相机，结果使用本地结构化输出。";
        var semantic = SemanticForPlan("surface_defect", prompt) with { ImageSource = "station_camera" };
        var orchestrator = new VisionAgentOrchestrator(
            CreateToolRegistry(),
            semanticExtractor: new FakeSemanticExtractor(semantic));

        var plan = await orchestrator.CreatePlanAsync(
            new VisionAgentPlanModeRequest
            {
                Description = prompt,
                OriginalUserPrompt = prompt,
                RequirementMode = AiRequirementModes.Strict,
                ConfirmedPlanAnswers =
                [
                    new VisionAgentPlanAnswer
                    {
                        Field = VisionAgentPlanAnswerFields.ImageSource,
                        Value = "station_camera",
                        Origin = VisionAgentPlanAnswerOrigins.ExplicitUserSelection
                    }
                ]
            },
            CancellationToken.None);

        plan.BuildReadiness.MissingResources.Should().ContainSingle(resource =>
            resource.ResourceType == "camera_binding");
        plan.BlockingReasons.Should().Equal("inspection_goal_missing");
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
            sink,
            planPlannerService: planner,
            semanticExtractor: new FakeSemanticExtractor(semantic));

        var plan = await planOrchestrator.CreatePlanAsync(
            new VisionAgentPlanModeRequest
            {
                Description = prompt,
                OriginalUserPrompt = prompt,
                RequirementMode = AiRequirementModes.Draft
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
                },
                requirementMode: AiRequirementModes.Draft),
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

    [Fact(DisplayName = "BuildFromPlan should block strategy confirmation until user explicitly chooses or accepts recommended")]
    public async Task BuildAsync_StrategyConfirmationMissing_ShouldBlockDespiteDefaultValue()
    {
        var sink = new CapturingAgentRunEventSink();
        var applicationService = CreateBuildApplicationService(sink);
        var plan = PlanWithStrategyConfirmationBlocker();

        var result = (await applicationService.BuildAsync(
            BuildCommand.FromGenerationRequest(
                Request(
                    plan,
                    userSelections: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                    acceptedRecommendedDefaults: false),
                transport: BuildCommandTransports.Internal,
                persistResult: false),
            CancellationToken.None)).Result;

        result.Success.Should().BeFalse();
        result.ClarificationRequired.Should().BeTrue();
        result.FailureSummary!.Code.Should().Be(VisionAgentBuildFailureCodes.ReadinessBlocked);
        result.BuildReadiness!.Blockers.Select(blocker => blocker.Id)
            .Should()
            .Contain("strategy_confirmation:model_or_rule_strategy_missing");
    }

    [Fact(DisplayName = "BuildFromPlan draft should allow unconfirmed strategy when Planner route is buildable")]
    public async Task BuildAsync_DraftStrategyConfirmationMissingWithPlannerRoute_ShouldBuildEditableDraft()
    {
        var sink = new CapturingAgentRunEventSink();
        var orchestrator = CreateOrchestrator(sink);
        var plan = PlanWithStrategyConfirmationBlocker();

        var result = await orchestrator.BuildAsync(
            Request(
                plan,
                userSelections: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                acceptedRecommendedDefaults: false,
                requirementMode: AiRequirementModes.Draft),
            CancellationToken.None);

        result.Success.Should().BeTrue();
        var build = result.BuildResult!;
        build.StrategyConfirmed.Should().BeFalse();
        build.UnresolvedStrategyBlockers.Should()
            .Contain("strategy_confirmation:model_or_rule_strategy_missing");
        build.SelectionSource.Should().Be("planner_route");
        build.EffectiveRouteId.Should().Be("attribute_classification_route");
        build.ApplyGate.CanvasApplyReady.Should().BeTrue();
        build.ToolEvidenceTimeline.Should().Contain(item =>
            item.Stage == "plan_selection" &&
            item.OutputSummary.Contains("Build route selection resolved", StringComparison.OrdinalIgnoreCase));
    }

    [Fact(DisplayName = "BuildFromPlan should block invalid confirmed answer values")]
    public async Task BuildAsync_InvalidConfirmedAnswer_ShouldBlock()
    {
        var sink = new CapturingAgentRunEventSink();
        var applicationService = CreateBuildApplicationService(sink);
        var plan = PlanWithStrategyConfirmationBlocker();

        var result = (await applicationService.BuildAsync(
            BuildCommand.FromGenerationRequest(
                Request(
                    plan,
                    confirmedAnswers:
                    [
                        PlanAnswer(
                            "model_or_rule_strategy",
                            VisionAgentPlanAnswerFields.AlgorithmStrategy,
                            "unsupported_strategy")
                    ],
                    acceptedRecommendedDefaults: false),
                transport: BuildCommandTransports.Internal,
                persistResult: false),
            CancellationToken.None)).Result;

        result.Success.Should().BeFalse();
        result.ClarificationRequired.Should().BeTrue();
        result.FailureSummary!.Code.Should().Be(VisionAgentBuildFailureCodes.ReadinessBlocked);
        result.BuildReadiness!.Blockers.Select(blocker => blocker.Id)
            .Should()
            .Contain("hard_requirement:invalid_plan_answer_value");
    }

    [Fact(DisplayName = "Plan answers should change fingerprint without changing PlanHash")]
    public void PlanAnswerValidator_ShouldFingerprintAnswersOutsidePlanHash()
    {
        var plan = PlanWithStrategyConfirmationBlocker();
        var validator = new VisionAgentPlanAnswerValidator();

        var deepLearning = validator.Validate(
            plan,
            [PlanAnswer("model_or_rule_strategy", VisionAgentPlanAnswerFields.AlgorithmStrategy, "deep_learning")],
            null,
            acceptedRecommendedDefaults: false);
        var traditionalRule = validator.Validate(
            plan,
            [PlanAnswer("model_or_rule_strategy", VisionAgentPlanAnswerFields.AlgorithmStrategy, "traditional_rule")],
            null,
            acceptedRecommendedDefaults: false);

        deepLearning.AnswerSetFingerprint.Should().StartWith("sha256:");
        traditionalRule.AnswerSetFingerprint.Should().StartWith("sha256:");
        deepLearning.AnswerSetFingerprint.Should().NotBe(traditionalRule.AnswerSetFingerprint);
        VisionAgentOrchestrator.ComputePlanHash(plan).Should().Be(plan.PlanHash);
    }

    [Fact(DisplayName = "PlanHash v1 should preserve the c9b2e871 payload exactly")]
    public void ComputePlanHash_V1_ShouldMatchLegacyGolden()
    {
        var plan = PlanWithStrategyConfirmationBlocker() with
        {
            PlanContractVersion = VisionAgentPlanContractVersions.V1
        };

        VisionAgentOrchestrator.ComputePlanHash(plan)
            .Should()
            .Be("sha256:eb244c0ca53982c4943e791adc9ae2cf725ade4a030a97e22465a9632ced335b");
    }

    [Fact(DisplayName = "BuildFromPlan should accept recommended strategy and emit confirmation metadata")]
    public async Task BuildAsync_AcceptedRecommendedStrategy_ShouldBuildDeepLearningDraft()
    {
        var sink = new CapturingAgentRunEventSink();
        var orchestrator = CreateOrchestrator(sink);
        var plan = PlanWithStrategyConfirmationBlocker();

        var result = await orchestrator.BuildAsync(
            Request(
                plan,
                userSelections: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                acceptedRecommendedDefaults: true),
            CancellationToken.None);

        result.Success.Should().BeTrue();
        var build = result.BuildResult!;
        build.StrategyConfirmed.Should().BeTrue();
        build.StrategyConfirmationSource.Should().Be("accepted_recommended");
        build.AnswerSetFingerprint.Should().StartWith("sha256:");
        build.ResolvedFields.Should().Contain(VisionAgentPlanAnswerFields.AlgorithmStrategy);
        build.UnresolvedStrategyBlockers.Should().BeEmpty();
        build.ParameterStrategy.Should().Be("deep_learning_classification");
        build.EffectiveRouteId.Should().Be("attribute_classification_deep_learning");
        build.OperatorPipeline.Select(item => item.OperatorType)
            .Should()
            .Contain(["ImageAcquisition", "DeepLearning", "ResultJudgment", "ResultOutput"])
            .And
            .NotContain(["Thresholding", "BlobAnalysis"]);
    }

    [Fact(DisplayName = "BuildFromPlan should use explicit stable strategy value for deep learning classification")]
    public async Task BuildAsync_UserDeepLearningSelection_ShouldUseDeepLearningClassificationRoute()
    {
        var sink = new CapturingAgentRunEventSink();
        var orchestrator = CreateOrchestrator(sink);
        var plan = PlanWithStrategyConfirmationBlocker();

        var result = await orchestrator.BuildAsync(
            Request(
                plan,
                userSelections: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["classification_ok_label"] = "expected class"
                },
                confirmedAnswers:
                [
                    PlanAnswer(
                        "model_or_rule_strategy",
                        VisionAgentPlanAnswerFields.AlgorithmStrategy,
                        "deep_learning")
                ],
                acceptedRecommendedDefaults: false),
            CancellationToken.None);

        result.Success.Should().BeTrue();
        var build = result.BuildResult!;
        build.SelectionSource.Should().Be("user_strategy");
        build.StrategyConfirmed.Should().BeTrue();
        build.StrategyConfirmationSource.Should().Be("user_selection");
        build.AnswerSetFingerprint.Should().StartWith("sha256:");
        build.ResolvedFields.Should().Contain(VisionAgentPlanAnswerFields.AlgorithmStrategy);
        build.EffectiveRouteId.Should().Be("attribute_classification_deep_learning");
        build.ParameterStrategy.Should().Be("deep_learning_classification");
        build.ParameterMapping.Should().Contain(item =>
            item.OperatorType == "ResultJudgment" &&
            item.ParameterName == "FieldName" &&
            item.ValueSummary == "TopClassLabel");
        build.ParameterMapping.Should().Contain(item =>
            item.OperatorType == "ResultJudgment" &&
            item.ParameterName == "ExpectValue" &&
            item.ValueSummary == "expected class");
        build.ParameterMapping.Should().Contain(item =>
            item.OperatorType == "DeepLearning" &&
            item.ParameterName == "TaskType" &&
            item.ValueSummary == "ImageClassification");
        build.ParameterMapping.Should().NotContain(item =>
            item.OperatorType == "DeepLearning" &&
            item.ParameterName == "Confidence");
        build.MissingResources.Should().Contain(item =>
            item.ResourceType == "model_resource" &&
            item.ResourceKey == "op_detect.ModelPath");
    }

    [Fact(DisplayName = "Build orchestrator should preserve user traditional strategy but fail closed when it lacks classification semantics")]
    public async Task BuildAsync_UserTraditionalRuleSelection_ShouldOverridePlannerDeepLearningRoute()
    {
        var sink = new CapturingAgentRunEventSink();
        var orchestrator = CreateOrchestrator(sink);
        var plan = PlanWithStrategyConfirmationBlocker();

        var result = await orchestrator.BuildAsync(
            Request(
                plan,
                userSelections: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["classification_ok_label"] = "expected class"
                },
                confirmedAnswers:
                [
                    PlanAnswer(
                        "model_or_rule_strategy",
                        VisionAgentPlanAnswerFields.AlgorithmStrategy,
                        "traditional_rule")
                ],
                acceptedRecommendedDefaults: false),
            CancellationToken.None);

        result.Success.Should().BeTrue();
        result.BuildResult.Should().NotBeNull();
        var build = result.BuildResult!;
        build.SelectionSource.Should().Be("user_strategy");
        build.StrategyConfirmed.Should().BeTrue();
        build.StrategyConfirmationSource.Should().Be("user_selection");
        build.EffectiveRouteId.Should().Be("attribute_classification_traditional_rule");
        build.ParameterStrategy.Should().Be("traditional_numeric_rule");
        build.EffectiveOperators.Should().Contain(["ImageAcquisition", "Thresholding", "BlobAnalysis", "ResultJudgment", "ResultOutput"]);
        build.OperatorPipeline.Select(item => item.OperatorType)
            .Should()
            .Contain(["ImageAcquisition", "Thresholding", "BlobAnalysis", "ResultJudgment", "ResultOutput"])
            .And
            .NotContain("DeepLearning");
        build.ParameterMapping.Should().NotContain(item => item.ValueSummary.Contains("TopClassLabel", StringComparison.OrdinalIgnoreCase));
        build.ParameterMapping.Should().NotContain(item =>
            item.OperatorType == "ResultJudgment" &&
            item.ParameterName == "ExpectValue" &&
            item.ValueSummary == "expected class");
        build.ParameterMapping.Should().Contain(item =>
            item.OperatorType == "ResultJudgment" &&
            item.ParameterName == "FieldName" &&
            item.ValueSummary == "BlobCount");
        build.ParameterMapping.Should().Contain(item =>
            item.OperatorType == "ResultJudgment" &&
            item.ParameterName == "Condition" &&
            item.ValueSummary == "GreaterOrEqual");
        build.ParameterMapping.Should().Contain(item =>
            item.OperatorType == "Thresholding" &&
            item.ParameterName == "Threshold" &&
            item.Pending);
        build.MissingResources.Should().Contain(item =>
            item.ResourceType == "threshold_parameter" &&
            item.ResourceKey == "op_threshold.Threshold");
        build.MissingResources.Should().Contain(item =>
            item.ResourceType == "area_range_parameter" &&
            item.ResourceKey == "op_blob.MinArea");
        build.ApplyGate.CanvasApplyReady.Should().BeFalse();
        build.ApplyGate.ApplyBlockers.Should().Contain("route_missing_task_processor");
        build.ApplyGate.DeploymentReady.Should().BeFalse();
        build.ToolEvidenceTimeline.Should().Contain(item =>
            item.Stage == "plan_selection" &&
            item.ToolName == "plan_selection_resolver");
    }

    [Fact(DisplayName = "Build application should publish the specific admission reason before the generic route summary")]
    public async Task BuildApplication_AdmissionFailure_ShouldUsePrimaryAndPreserveSecondaryDiagnostics()
    {
        var sink = new CapturingAgentRunEventSink();
        var applicationService = CreateBuildApplicationService(sink);
        var plan = PlanWithStrategyConfirmationBlocker();

        var result = (await applicationService.BuildAsync(
            BuildCommand.FromGenerationRequest(
                Request(
                    plan,
                    userSelections: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["classification_ok_label"] = "expected class"
                    },
                    confirmedAnswers:
                    [
                        PlanAnswer(
                            "model_or_rule_strategy",
                            VisionAgentPlanAnswerFields.AlgorithmStrategy,
                            "traditional_rule")
                    ],
                    acceptedRecommendedDefaults: false,
                    requirementMode: AiRequirementModes.Draft),
                transport: BuildCommandTransports.Internal,
                persistResult: false),
            CancellationToken.None)).Result;

        result.Success.Should().BeFalse();
        result.FailureSummary.Should().NotBeNull();
        result.FailureSummary!.Category.Should().Be("workflow_artifact_admission");
        result.FailureSummary.Code.Should().Be("route_missing_task_processor");
        result.FailureSummary.Message.Should().Contain("route_missing_task_processor");
        result.FailureSummary.SecondaryDiagnosticCodes.Should().Contain("route_semantics_not_satisfied");
        result.BuildResult!.ApplyGate.ApplyBlockers.Should().Contain("route_missing_task_processor");
    }

    [Theory(DisplayName = "Confirmed task_type should drive attribute classification strategy routes")]
    [InlineData("deep_learning", "attribute_classification_deep_learning", "deep_learning_classification", "DeepLearning")]
    [InlineData("traditional_rule", "attribute_classification_traditional_rule", "traditional_numeric_rule", "Thresholding")]
    public async Task BuildAsync_ConfirmedTaskTypeAttributeClassification_ShouldDriveStrategyRoutes(
        string strategy,
        string expectedRouteId,
        string expectedParameterStrategy,
        string expectedOperator)
    {
        var orchestrator = CreateOrchestrator(new CapturingAgentRunEventSink());
        var plan = PlanWithStrategyConfirmationBlocker(
            intent: "surface_defect",
            originalUserPrompt: "inspect fruit",
            routeId: "planner_route",
            taskType: AiVisionTaskTypes.SurfaceDefect,
            targetAttribute: string.Empty,
            okCondition: string.Empty);

        var result = await orchestrator.BuildAsync(
            Request(
                plan,
                userSelections: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                confirmedAnswers:
                [
                    PlanAnswer(
                        string.Empty,
                        VisionAgentPlanAnswerFields.TaskType,
                        AiVisionTaskTypes.AttributeClassification,
                        VisionAgentPlanAnswerOrigins.ExplicitUserSelection),
                    PlanAnswer(
                        string.Empty,
                        VisionAgentPlanAnswerFields.TargetAttribute,
                        "ripe",
                        VisionAgentPlanAnswerOrigins.ExplicitUserText),
                    PlanAnswer(
                        string.Empty,
                        VisionAgentPlanAnswerFields.AcceptanceCriteria,
                        "ripe is OK",
                        VisionAgentPlanAnswerOrigins.ExplicitUserText),
                    PlanAnswer(
                        "model_or_rule_strategy",
                        VisionAgentPlanAnswerFields.AlgorithmStrategy,
                        strategy)
                ],
                acceptedRecommendedDefaults: false),
            CancellationToken.None);

        result.Success.Should().BeTrue();
        var build = result.BuildResult!;
        build.EffectiveRouteId.Should().Be(expectedRouteId);
        build.ParameterStrategy.Should().Be(expectedParameterStrategy);
        build.OperatorPipeline.Select(item => item.OperatorType).Should().Contain(expectedOperator);
        build.ResolvedFields.Should().Contain(VisionAgentPlanAnswerFields.TaskType);
        if (strategy == "deep_learning")
        {
            build.ParameterMapping.Should().Contain(item =>
                item.OperatorType == "ResultJudgment" &&
                item.ParameterName == "ExpectValue" &&
                item.ValueSummary == "ripe");
        }
        else
        {
            build.ParameterMapping.Should().Contain(item =>
                item.OperatorType == "ResultJudgment" &&
                item.ParameterName == "FieldName" &&
                item.ValueSummary == "BlobCount");
        }
    }

    [Fact(DisplayName = "Non-attribute tasks should not switch routes from incidental model or rule values")]
    public async Task BuildAsync_NonAttributeStrategyText_ShouldNotSwitchToClassificationRoute()
    {
        var sink = new CapturingAgentRunEventSink();
        var orchestrator = CreateOrchestrator(sink);
        var plan = Plan(
            "measurement",
            ["ImageAcquisition", "CircleMeasurement", "MeasureDistance", "UnitConvert", "Aggregator", "ResultJudgment", "ResultOutput"],
            "hole distance measurement workflow");

        var result = await orchestrator.BuildAsync(
            Request(
                plan,
                userSelections: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["model_or_rule_strategy"] = "traditional_rule",
                    ["notes"] = "operator model/rule wording is just a parameter note"
                }),
            CancellationToken.None);

        result.Success.Should().BeTrue();
        var build = result.BuildResult!;
        build.EffectiveRouteId.Should().NotBe("attribute_classification_traditional_rule");
        build.ParameterStrategy.Should().BeEmpty();
        build.OperatorPipeline.Select(item => item.OperatorType)
            .Should()
            .Contain(["Measurement", "UnitConvert", "Aggregator", "ResultJudgment", "ResultOutput"])
            .And
            .NotContain(["Thresholding", "BlobAnalysis", "DeepLearning"]);
    }

    [Fact(DisplayName = "Black-box scenario: hole distance measurement Build quality")]
    public async Task BuildAsync_BlackBoxHoleDistance_ShouldProduceMeasurementDraftWithCalibrationBlockers()
    {
        var sink = new CapturingAgentRunEventSink();
        var orchestrator = CreateOrchestrator(sink);
        var plan = Plan(
            "measurement",
            ["ImageAcquisition", "CircleMeasurement", "CircleMeasurement", "MeasureDistance", "UnitConvert", "Aggregator", "ResultJudgment", "ResultOutput"],
            "hole center distance measurement workflow");

        var result = await orchestrator.BuildAsync(
            Request(plan, buildIntent: "modify", currentFlowSnapshot: ExistingFlowSnapshot("existing-measurement-context")),
            CancellationToken.None);

        result.Success.Should().BeTrue();
        result.BuildResult!.OperatorPipeline.Select(item => item.OperatorType)
            .Should()
            .Contain(["ImageAcquisition", "CircleMeasurement", "Measurement", "UnitConvert", "Aggregator", "ResultJudgment", "ResultOutput"]);
        result.BuildResult.ParameterMapping.Should().Contain(item =>
            item.OperatorType == "UnitConvert" &&
            item.ParameterName == "Scale" &&
            item.Pending &&
            item.ValueSummary.Contains("pixel-to-world-scale", StringComparison.OrdinalIgnoreCase));
        result.BuildResult.PendingParameters.Should().Contain(item =>
            item.OperatorId == "op_calibration" &&
            item.ParameterNames.Contains("Scale"));
        result.BuildResult.MissingResources.Should().Contain(item =>
            item.ResourceType == "calibration_resource" &&
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
        result.BuildResult.ApplyGate.CanvasApplyReady.Should().BeTrue();
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
        result.BuildResult.Flow.Should().NotBeNull();
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

        result.Success.Should().BeFalse();
        result.CompletionStatus.Should().Be(AiFlowGenerationResult.CompletionStatusFailed);
        result.FailureSummary.Should().NotBeNull();
        result.FailureSummary!.Code.Should().Be("plan_hash_mismatch");
        result.BuildResult!.PublicWarnings.Should().Contain("plan_hash_mismatch");
        result.BuildResult.ApplyGate.ApplyBlockers.Should().Contain("plan_hash_mismatch");
        result.BuildResult.ToolEvidenceTimeline.Should().Contain(item =>
            item.Stage == "plan_generation" &&
            item.WarningCode == "plan_hash_mismatch");
        sink.Events.Should().Contain(evt =>
            evt.Stage == "plan_hash_validation" &&
            evt.Summary.Contains("复核计划来源", StringComparison.OrdinalIgnoreCase));
        sink.Events.Should().NotContain(evt =>
            evt.Stage == "plan_hash_validation" &&
            evt.Summary.Contains("构建会继续", StringComparison.OrdinalIgnoreCase));
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
            ["ImageAcquisition", "SurfaceDefectDetection", "ResultJudgment", "ResultOutput"],
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

    [Fact(DisplayName = "Build orchestrator should emit global variable drafts as metadata-only suggestions")]
    public async Task BuildAsync_ShouldEmitGlobalVariableDrafts()
    {
        var sink = new CapturingAgentRunEventSink();
        var orchestrator = CreateOrchestrator(sink);
        var baseline = Plan(
            "surface_defect",
            ["ImageAcquisition", "SurfaceDefectDetection", "ResultOutput"],
            "use a global variable for shared defect threshold and expose it in result metadata");
        var plan = baseline with
        {
            RecommendedDefaults =
            [
                ..baseline.RecommendedDefaults,
                new VisionAgentDefaultAssumption
                {
                    Id = "Shared Defect Threshold",
                    Label = "Shared defect threshold",
                    Value = "0.72",
                    Impact = "Shared project variable used by multiple threshold checks."
                }
            ],
            AcceptanceCriteria =
            [
                ..baseline.AcceptanceCriteria,
                "Expose the confirmed shared threshold in result metadata."
            ]
        };
        plan = plan with
        {
            PlanHash = VisionAgentOrchestrator.ComputePlanHash(plan)
        };

        var result = await orchestrator.BuildAsync(Request(plan), CancellationToken.None);

        result.Success.Should().BeTrue();
        result.GlobalVariableDrafts.Should().ContainSingle(item =>
            item.Name == "shared.defect.threshold" &&
            item.ValueType == "Double" &&
            item.RequiresHumanConfirmation &&
            item.MetadataOnly &&
            item.IncludeInResultMetadata);
        result.GlobalVariableDiagnostics.Should().Contain(item =>
            item.Code == "GV_AGENT_DRAFT" &&
            item.Severity == "info" &&
            item.MetadataOnly);
        result.BuildResult!.GlobalVariableDrafts.Should().BeEquivalentTo(result.GlobalVariableDrafts);
        result.BuildResult.GlobalVariableTargetBindingDrafts.Should().OnlyContain(item =>
            item.VariableName == "shared.defect.threshold" &&
            item.RequiresHumanConfirmation &&
            item.MetadataOnly);
        sink.Events.Should().Contain(item =>
            item.EventType == AgentRunEventTypes.ArtifactCreated &&
            JsonSerializer.Serialize(item.Payload, AgentRunEventJson.Options).Contains("globalVariableDraftCount"));
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

    [Fact(DisplayName = "Build orchestrator failure should return controlled system error contract")]
    public async Task BuildAsync_Failure_ShouldReturnControlledSystemErrorContract()
    {
        var sink = new CapturingAgentRunEventSink();
        var redactor = new AgentRunEventRedactor();
        var toolRunner = new BuildToolRunner(CreateToolRegistry(), redactor, sink);
        var orchestrator = new VisionAgentBuildOrchestrator(
            new BuildPlanContextLoader(sink),
            null!,
            new TemplateStrategyResolver(toolRunner),
            new PlanSelectionResolver(),
            new OperatorPipelineSelector(),
            new ParameterMappingService(),
            new WorkflowDraftBuilder(),
            toolRunner,
            new BuildReadinessReviewService(),
            new WorkflowDiffService(),
            new ApplyGateResolver(),
            new BuildResultAssembler(redactor, sink),
            NullLogger<VisionAgentBuildOrchestrator>.Instance,
            sink);
        var plan = Plan("surface_defect", ["ImageAcquisition", "SurfaceDefectDetection", "ResultOutput"]);

        var result = await orchestrator.BuildAsync(
            Request(plan, planHashOverride: "stale_plan_hash"),
            CancellationToken.None);

        result.Success.Should().BeFalse();
        result.CompletionStatus.Should().Be(AiFlowGenerationResult.CompletionStatusFailed);
        result.FailureType.Should().Be(AiFlowGenerationResult.FailureTypeSystemError);
        result.ClarificationRequired.Should().BeFalse();
        result.RequirementBrief.Should().BeNull();
        result.BuildReadiness.Should().BeNull();
        result.InteractionState.Should().Be(AiInteractionStates.Failed);
        result.FailureSummary.Should().NotBeNull();
        result.FailureSummary!.Category.Should().Be("vision_agent_build_from_plan");
        result.FailureSummary.Code.Should().Be("plan_hash_mismatch");
        result.FailureSummary.Message.Should().NotBeNullOrWhiteSpace();
        result.FailureSummary.RepairTarget.Should().Contain("公开工具证据");
        result.BuildResult.Should().NotBeNull();
        result.BuildResult!.ToolEvidenceTimeline.Should().Contain(item => item.ToolName == "plan_snapshot_loader");
        result.BuildResult.ToolEvidenceTimeline.Should().OnlyContain(item => item.MetadataOnly);
        result.BuildResult.PublicWarnings.Should().Contain("plan_hash_mismatch");
        result.BuildResult.ApplyGate.Blocked.Should().BeTrue();
        result.BuildResult.ApplyGate.ApplyBlockers.Should().Contain("plan_hash_mismatch");
        var publicJson = JsonSerializer.Serialize(new { result, sink.Events }, AgentRunEventJson.Options);
        publicJson.Should().NotContain("C:\\factory");
        publicJson.Should().NotContain("sk-secret");
        publicJson.Should().NotContain("192.168.1.10");
        publicJson.Should().NotContain("systemPrompt");
        publicJson.Should().NotContain("rawPrompt");
        publicJson.Should().NotContain("rawModelResponse");
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
            new WorkflowDraftBuilder(),
            toolRunner,
            new BuildReadinessReviewService(),
            new WorkflowDiffService(),
            new ApplyGateResolver(),
            new BuildResultAssembler(redactor, sink),
            NullLogger<VisionAgentBuildOrchestrator>.Instance,
            sink);
    }

    private static VisionAgentBuildApplicationService CreateBuildApplicationService(
        CapturingAgentRunEventSink sink,
        IVisionAgentToolRegistry? registry = null)
    {
        return new VisionAgentBuildApplicationService(
            new BuildExecutionAdapter(CreateOrchestrator(sink, registry)),
            new VisionAgentPlanAnswerValidator(),
            new VisionAgentPlanRequirementOverlay(),
            NullLogger<VisionAgentBuildApplicationService>.Instance,
            Options.Create(new AgentGenerateFlowOptions
            {
                Enabled = true
            }),
            sink,
            workflowArtifactAdmissionGate: WorkflowArtifactAdmissionTestSupport.CreateGate());
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
                (_, arguments) =>
                {
                    var fingerprint = ReadArtifactFingerprint(arguments);
                    return VisionAgentToolResult.Ok(new
                    {
                        blockingIssues = Array.Empty<object>(),
                        warnings = Array.Empty<object>(),
                        missingResources = Array.Empty<object>(),
                        pendingParameters = Array.Empty<object>(),
                        artifactFingerprint = fingerprint,
                        validationFingerprint = fingerprint,
                        compiledFingerprint = fingerprint,
                        fingerprintConsistent = !string.IsNullOrWhiteSpace(fingerprint),
                        metadataOnly = true
                    });
                }),
            new FakeVisionAgentTool(
                "dryrun_flow",
                VisionAgentToolPermission.Simulation,
                (_, arguments) =>
                {
                    var fingerprint = ReadArtifactFingerprint(arguments);
                    return VisionAgentToolResult.Ok(new
                    {
                        dryRunSucceeded = true,
                        blockingIssues = Array.Empty<object>(),
                        missingResources = Array.Empty<object>(),
                        artifactFingerprint = fingerprint,
                        dryRunFingerprint = fingerprint,
                        compiledFingerprint = fingerprint,
                        fingerprintConsistent = !string.IsNullOrWhiteSpace(fingerprint),
                        metadataOnly = true
                    });
                }),
            new FakeVisionAgentTool(
                "runtime_package_precheck",
                VisionAgentToolPermission.DeploymentPrepare,
                (_, arguments) =>
                {
                    var fingerprint = ReadArtifactFingerprint(arguments);
                    return VisionAgentToolResult.Ok(new
                    {
                        readyForDeployment = false,
                        blockingIssues = Array.Empty<object>(),
                        missingResources = Array.Empty<object>(),
                        pendingActions = Array.Empty<object>(),
                        artifactFingerprint = fingerprint,
                        precheckFingerprint = fingerprint,
                        compiledFingerprint = fingerprint,
                        fingerprintConsistent = !string.IsNullOrWhiteSpace(fingerprint),
                        metadataOnly = true
                    });
                })
        ]);
    }

    private static string ReadArtifactFingerprint(JsonElement arguments)
    {
        return arguments.TryGetProperty("artifactFingerprint", out var value) &&
               value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;
    }

    private static AiFlowGenerationRequest Request(
        VisionAgentPlanModeResult plan,
        string buildIntent = "new",
        string? currentFlowSnapshot = null,
        string? planHashOverride = null,
        AiTemplateSelectionInfo? templateSelection = null,
        Dictionary<string, string>? userSelections = null,
        List<VisionAgentPlanAnswer>? confirmedAnswers = null,
        bool acceptedRecommendedDefaults = true,
        string requirementMode = AiRequirementModes.Strict)
    {
        return new AiFlowGenerationRequest(plan.OriginalUserPrompt, Mode: GenerateFlowModeExtensions.ParseOrAuto(buildIntent))
        {
            AgentRunId = "ar_build_test",
            UseVisionAgentGenerateFlow = true,
            RequirementMode = requirementMode,
            BuildFromPlan = new VisionAgentBuildFromPlanRequest
            {
                PlanId = plan.PlanId,
                PlanHash = planHashOverride ?? plan.PlanHash,
                PlanSnapshot = plan,
                ConfirmedAnswers = confirmedAnswers ?? [],
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
                AcceptedRecommendedDefaults = acceptedRecommendedDefaults,
                MetadataOnly = true
            }
        };
    }

    private static VisionAgentBuildReadinessPreviewRequest PreviewRequest(
        VisionAgentPlanModeResult plan,
        Dictionary<string, string>? userSelections = null,
        List<VisionAgentPlanAnswer>? confirmedAnswers = null,
        bool acceptedRecommendedDefaults = false,
        string requirementMode = AiRequirementModes.Strict,
        int answerRevision = 1,
        string? planHashOverride = null,
        List<VisionAgentResourceDecision>? resourceDecisions = null)
    {
        var request = Request(
            plan,
            planHashOverride: planHashOverride,
            userSelections: userSelections ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            confirmedAnswers: confirmedAnswers ?? [],
            acceptedRecommendedDefaults: acceptedRecommendedDefaults,
            requirementMode: requirementMode);
        var build = request.BuildFromPlan!;
        return new VisionAgentBuildReadinessPreviewRequest
        {
            PlanId = build.PlanId,
            PlanHash = build.PlanHash,
            PlanSnapshot = build.PlanSnapshot,
            RequirementMode = request.RequirementMode,
            ConfirmedAnswers = build.ConfirmedAnswers,
            UserSelections = build.UserSelections,
            AcceptedDefaults = build.AcceptedDefaults,
            AcceptedRecommendedDefaults = build.AcceptedRecommendedDefaults,
            AnswerRevision = answerRevision,
            ResourceDecisions = resourceDecisions ?? [],
            AdditionalContext = request.AdditionalContext,
            CurrentFlowSnapshot = build.CurrentFlowSnapshot,
            TemplateSelection = build.TemplateSelection,
            AttachmentSummary = build.AttachmentSummary,
            OperatorCatalogVersion = build.OperatorCatalogVersion,
            StationBoundarySummary = build.StationBoundarySummary,
            PlcOutputPolicy = build.PlcOutputPolicy,
            BuildIntent = build.BuildIntent,
            OriginalUserPrompt = build.OriginalUserPrompt,
            RequirementMaturity = build.RequirementMaturity,
            DecisionTrace = build.DecisionTrace,
            MetadataOnly = true
        };
    }

    private static VisionAgentPlanAnswer PlanAnswer(
        string questionId,
        string field,
        string value,
        string origin = VisionAgentPlanAnswerOrigins.ExplicitUserSelection)
    {
        return new VisionAgentPlanAnswer
        {
            QuestionId = questionId,
            Field = field,
            Value = value,
            Origin = origin
        };
    }

    private sealed class BuildExecutionAdapter : IVisionAgentOrchestrator
    {
        private readonly IVisionAgentBuildOrchestrator _buildOrchestrator;

        public BuildExecutionAdapter(IVisionAgentBuildOrchestrator buildOrchestrator)
        {
            _buildOrchestrator = buildOrchestrator;
        }

        public Task<VisionAgentPlanModeResult> CreatePlanAsync(
            VisionAgentPlanModeRequest request,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<AiFlowGenerationResult> BuildFromPlanAsync(
            AiFlowGenerationRequest request,
            CancellationToken cancellationToken)
        {
            return _buildOrchestrator.BuildAsync(request, cancellationToken);
        }
    }

    private static VisionAgentPlanModeResult ExternalMesOutputPlan()
    {
        var baseline = Plan(
            "classification",
            ["ImageAcquisition", "DeepLearning", "ResultJudgment", "ResultOutput"],
            "classify apples and send result to MES");
        var plan = baseline with
        {
            CanBuild = false,
            BlockingReasons = ["strategy_confirmation:output_target_missing"],
            ClarificationQuestions =
            [
                new VisionAgentClarificationQuestion
                {
                    Id = "output_target",
                    Field = VisionAgentPlanAnswerFields.OutputTarget,
                    Title = "Output target",
                    DefaultValue = "business_system_output",
                    Options =
                    [
                        new VisionAgentClarificationOption
                        {
                            Value = "business_system_output",
                            Label = "MES output",
                            Recommended = true
                        },
                        new VisionAgentClarificationOption
                        {
                            Value = "local_result_payload",
                            Label = "Local structured output",
                            Recommended = false
                        }
                    ]
                }
            ],
            SemanticExtraction = baseline.SemanticExtraction! with
            {
                OutputTarget = string.Empty
            },
            RequirementMaturity = baseline.RequirementMaturity! with
            {
                CanPlan = true,
                CanBuild = true,
                MissingFields = [],
                BlockingReasons = [],
                PublicReason = "Hard facts are ready; output target remains."
            }
        };

        return plan with
        {
            PlanHash = VisionAgentOrchestrator.ComputePlanHash(plan)
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

    private static VisionAgentPlanModeResult StrawberryDraftBuildPlan()
    {
        var baseline = Plan(
            AiVisionTaskTypes.AttributeClassification,
            ["ImageAcquisition", "ColorConversion", "RoiManager", "DeepLearning"],
            "构建一个检测果园中草莓成熟度的视觉检测应用。");
        var plan = baseline with
        {
            PlanId = "plan_strawberry_draft_build_contract",
            Goal = "构建一个检测果园中草莓成熟度的视觉检测应用。",
            Intent = AiVisionTaskTypes.AttributeClassification,
            Confidence = "medium",
            CanBuild = false,
            BlockingReasons = ["resource_pending:image_source_missing"],
            RecommendedRoute = baseline.RecommendedRoute with
            {
                RouteId = "strawberry_maturity_attribute_classification",
                Operators = ["imageacquisition", "colorconversion", "roimanager", "deeplearning"],
                TemplateDecision = "planner_route"
            },
            ClarificationQuestions =
            [
                DeferredQuestion("classification_strategy", VisionAgentPlanAnswerFields.AlgorithmStrategy, "model_strategy", "strategy_pending"),
                DeferredQuestion("ok_ng_rule", VisionAgentPlanAnswerFields.AcceptanceCriteria, "use_extracted_conditions", "threshold_pending"),
                DeferredQuestion("q_fallback_image_source", VisionAgentPlanAnswerFields.ImageSource, "station_camera", "camera_pending")
            ],
            ConfirmedPlanAnswers =
            [
                PlanAnswer(string.Empty, VisionAgentPlanAnswerFields.InspectionObject, "草莓", VisionAgentPlanAnswerOrigins.ExplicitUserText),
                PlanAnswer("classification_strategy", VisionAgentPlanAnswerFields.AlgorithmStrategy, "model_strategy")
            ],
            ResolvedPlanFields = [VisionAgentPlanAnswerFields.InspectionObject],
            RemainingPlanFields =
            [
                VisionAgentPlanAnswerFields.ImageSource,
                VisionAgentPlanAnswerFields.TaskType,
                VisionAgentPlanAnswerFields.AcceptanceCriteria,
                VisionAgentPlanAnswerFields.OutputTarget,
                VisionAgentPlanAnswerFields.AlgorithmStrategy
            ],
            SemanticExtraction = new VisionAgentSemanticExtractionResult
            {
                IsVisionRequest = true,
                TaskType = AiVisionTaskTypes.AttributeClassification,
                InspectionObject = "草莓",
                TargetAttribute = "成熟度",
                OkCondition = "草莓已成熟",
                NgCondition = "草莓未成熟",
                ObjectSignals = ["果园环境", "草莓果实", "草莓"],
                TaskSignals = ["成熟度判断", "视觉检测", "OK/NG分类", "成熟度", "草莓已成熟", "草莓未成熟"],
                Source = VisionAgentSemanticSources.Model,
                MetadataOnly = true
            },
            RequirementMaturity = new AiRequirementMaturityResult
            {
                Maturity = AiRequirementMaturity.Ambiguous,
                TaskType = AiVisionTaskTypes.AttributeClassification,
                CanPlan = true,
                CanBuild = false,
                ObjectSignals = ["果园环境", "草莓果实", "草莓"],
                TaskSignals = ["成熟度判断", "视觉检测", "OK/NG分类", "成熟度", "草莓已成熟", "草莓未成熟"],
                MissingFields =
                [
                    VisionAgentPlanAnswerFields.ImageSource,
                    VisionAgentPlanAnswerFields.TaskType,
                    VisionAgentPlanAnswerFields.AcceptanceCriteria,
                    VisionAgentPlanAnswerFields.OutputTarget,
                    VisionAgentPlanAnswerFields.AlgorithmStrategy
                ],
                MetadataOnly = true
            }
        };
        return plan with { PlanHash = VisionAgentOrchestrator.ComputePlanHash(plan) };
    }

    private static VisionAgentClarificationQuestion DeferredQuestion(
        string id,
        string field,
        string resolvedValue,
        string deferredValue)
    {
        return new VisionAgentClarificationQuestion
        {
            Id = id,
            Field = field,
            Title = field,
            Options =
            [
                new VisionAgentClarificationOption
                {
                    Value = resolvedValue,
                    Label = resolvedValue,
                    AnswerEffect = VisionAgentClarificationAnswerEffects.ResolveField
                },
                new VisionAgentClarificationOption
                {
                    Value = deferredValue,
                    Label = deferredValue,
                    AnswerEffect = VisionAgentClarificationAnswerEffects.Defer
                }
            ]
        };
    }

    private static VisionAgentPlanModeResult PlanWithStrategyConfirmationBlocker(
        string intent = "attribute_classification",
        string originalUserPrompt = "classify object maturity from camera",
        string? routeId = null,
        string taskType = AiVisionTaskTypes.AttributeClassification,
        string targetAttribute = "attribute",
        string okCondition = "expected class is OK")
    {
        var plan = Plan(
            intent,
            ["ImageAcquisition", "DeepLearning", "ResultJudgment", "ResultOutput"],
            originalUserPrompt);
        var semantic = plan.SemanticExtraction! with
        {
            TaskType = taskType,
            TargetAttribute = targetAttribute,
            OkCondition = okCondition,
            TaskSignals = string.IsNullOrWhiteSpace(targetAttribute)
                ? [taskType]
                : [taskType, targetAttribute]
        };
        var maturity = VisionAgentRequirementMaturityGate.Evaluate(
            new VisionAgentRequirementMaturityRequest
            {
                Description = originalUserPrompt
            },
            semantic);
        var blocked = plan with
        {
            CanBuild = false,
            Intent = intent,
            SemanticExtraction = semantic,
            RequirementMaturity = maturity,
            RecommendedRoute = plan.RecommendedRoute with
            {
                RouteId = routeId ?? $"{intent}_route"
            },
            BlockingReasons = ["strategy_confirmation:model_or_rule_strategy_missing"],
            ClarificationQuestions =
            [
                new VisionAgentClarificationQuestion
                {
                    Id = "model_or_rule_strategy",
                    Field = VisionAgentPlanAnswerFields.AlgorithmStrategy,
                    Title = "Classification strategy",
                    Why = "The operator route changes between model classification and calibrated numeric rules.",
                    DefaultValue = "deep_learning",
                    DefaultAssumption = "Use deep learning classification unless the user chooses traditional rules.",
                    Impact = "Deep learning keeps model resources pending; traditional rules keep calibration parameters pending.",
                    Options =
                    [
                        new VisionAgentClarificationOption
                        {
                            Value = "deep_learning",
                            Label = "Deep learning",
                            Recommended = true,
                            Description = "Use TopClassLabel and TopClassConfidence from DeepLearning.",
                            Impact = "Editable draft is allowed; deployment waits for model binding."
                        },
                        new VisionAgentClarificationOption
                        {
                            Value = "traditional_rule",
                            Label = "Traditional rule",
                            Recommended = false,
                            Description = "Use thresholding and blob analysis with numeric judgment.",
                            Impact = "Editable draft is allowed; deployment waits for calibration."
                        }
                    ]
                }
            ]
        };

        return blocked with
        {
            PlanHash = VisionAgentOrchestrator.ComputePlanHash(blocked)
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
            CanBuildCandidate = true,
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
