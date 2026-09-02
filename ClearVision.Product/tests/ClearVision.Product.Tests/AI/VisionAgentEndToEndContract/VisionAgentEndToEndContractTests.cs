using System.Text.Json;
using ClearVision.Product.Application.DTOs;
using ClearVision.Product.Application.Services;
using ClearVision.Product.Contracts.Messages;
using ClearVision.Product.Core.AI.Tools;
using ClearVision.Product.Core.DTOs;
using ClearVision.Product.Infrastructure.AI;
using ClearVision.Product.Infrastructure.AI.Agent;
using ClearVision.Product.Infrastructure.AI.AgentRun;
using ClearVision.Product.Infrastructure.AI.Tools;
using ClearVision.Product.Tests.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ClearVision.Product.Tests.AI.VisionAgentEndToEndContract;

[TestClassification(TestDomain.Ai, TestPurpose.Regression, TestLane.Nightly, TestEvidenceType.Contract, TestOracleType.Contract, TestResourceRequirement.None, TestExpectedDuration.Medium, TestFlakyPolicy.Blocking, "vision-agent")]
public sealed class VisionAgentEndToEndContractTests
{
    public static TheoryData<string> CanonicalTasks => new()
    {
        AiVisionTaskTypes.PresenceAbsence,
        AiVisionTaskTypes.AttributeClassification,
        AiVisionTaskTypes.ObjectDetection,
        AiVisionTaskTypes.TemplateLocation,
        AiVisionTaskTypes.SurfaceDefect,
        AiVisionTaskTypes.GeometryMeasurement,
        AiVisionTaskTypes.WireSequence,
        AiVisionTaskTypes.CodeRecognition
    };

    public static IEnumerable<object[]> TaskAliasMigrationCases() =>
        AiVisionTaskCatalog.PrimaryTasks.SelectMany(task =>
            task.AllValues.Select(value => new object[]
            {
                value,
                task.CanonicalValue,
                task.RouteContractKey
            }));

    public static TheoryData<string, string, string, string> ImageSourceMigrationCases => new()
    {
        { "file", "File", "File", string.Empty },
        { "file_sample", "File", "File", string.Empty },
        { "image_file", "File", "File", string.Empty },
        { "image_folder", "File", "File", string.Empty },
        { "offline_sample", "File", "File", string.Empty },
        { "sample_image", "File", "File", string.Empty },
        { "文件", "File", "File", string.Empty },
        { "图片目录", "File", "File", string.Empty },
        { "camera", "Camera", "Camera", string.Empty },
        { "station_camera", "Camera", "Camera", string.Empty },
        { "line_camera", "Camera", "Camera", string.Empty },
        { "industrial_camera", "Camera", "Camera", string.Empty },
        { "工站相机", "Camera", "Camera", string.Empty },
        { "camera_pending", "Pending", "<pending-image-source>", "image_source_pending" },
        { "source_pending", "Pending", "<pending-image-source>", "image_source_pending" },
        { "video", "Unsupported", "<unsupported-image-source>", "unsupported_image_source" },
        { "rtsp", "Unsupported", "<unsupported-image-source>", "unsupported_image_source" },
        { "unknown", "Unsupported", "<unsupported-image-source>", "unsupported_image_source" },
        { "legacy_unregistered_source", "Unsupported", "<unsupported-image-source>", "unsupported_image_source" }
    };

    [Theory(DisplayName = "Canonical task fixture should pass Build, route v2, admission and public projection")]
    [MemberData(nameof(CanonicalTasks))]
    public async Task CanonicalTaskFixture_ShouldPassEndToEndContract(string taskType)
    {
        var fixture = FixtureFor(taskType);
        var sink = new CapturingAgentRunEventSink();
        var application = CreateApplication(sink);
        var plan = BuildPlan(fixture);

        var result = (await application.BuildAsync(
            BuildCommand.FromGenerationRequest(
                BuildRequest(plan, fixture.ParameterSelections),
                transport: BuildCommandTransports.Internal,
                persistResult: false),
            CancellationToken.None)).Result;

        result.Success.Should().BeTrue(result.FailureSummary?.Message);
        result.Flow.Should().BeOfType<OperatorFlowDto>();
        result.AiExplanation.Should().NotBeNullOrWhiteSpace();
        result.BuildResult.Should().NotBeNull();
        var build = result.BuildResult!;
        var flow = result.Flow.Should().BeOfType<OperatorFlowDto>().Subject;
        build.TaskType.Should().Be(fixture.TaskType);
        build.OperatorPipeline.Select(item => item.OperatorType).Should()
            .Contain(fixture.Operators);
        build.RouteSemanticsSatisfied.Should().BeTrue(string.Join(",", build.ApplyGate.ApplyBlockers));
        build.ApplyGate.RouteSemanticsSatisfied.Should().BeTrue();
        build.ApplyGate.CanvasApplyReady.Should().BeTrue(string.Join(",", build.ApplyGate.ApplyBlockers));
        build.ApplyGate.Blocked.Should().BeFalse();
        Mapping(build, "ResultJudgment", "FieldName").ValueSummary.Should().Be(fixture.JudgmentField);
        Mapping(build, "ResultJudgment", "Condition").ValueSummary.Should().Be(fixture.JudgmentCondition);
        AssertJudgmentThresholds(build, fixture);
        HasConnection(
                flow,
                fixture.JudgmentSourceOperator,
                fixture.JudgmentSourcePort,
                "ResultJudgment",
                "Value")
            .Should().BeTrue($"{fixture.TaskType} business value must reach ResultJudgment.Value; {DescribeConnections(flow)}");
        HasConnection(
                flow,
                fixture.OutputSourceOperator,
                fixture.OutputSourcePort,
                "ResultOutput",
                fixture.OutputTargetPort)
            .Should().BeTrue($"{fixture.TaskType} business result must reach ResultOutput.{fixture.OutputTargetPort}; {DescribeConnections(flow)}");
        HasConnection(flow, "ResultJudgment", "JudgmentResult", "ResultOutput", "Result")
            .Should().BeTrue("the task judgment must reach the public result terminal");

        var admission = WorkflowArtifactAdmissionTestSupport.CreateGate().Inspect(
            flow,
            $"vision_agent.e2e.{fixture.TaskType}",
            context: new WorkflowArtifactAdmissionContext
            {
                TaskType = build.TaskType,
                RouteSemanticsSatisfied = build.RouteSemanticsSatisfied,
                ArtifactFingerprint = build.ArtifactFingerprint
            });
        admission.Disposition.Should().Be(WorkflowArtifactAdmissionDisposition.Canonical);
        admission.Flow.Should().NotBeNull();
        AssertPublicProjectionSafe(result, sink);
    }

    [Theory(DisplayName = "Every canonical task should fail closed when its business processor is absent")]
    [MemberData(nameof(CanonicalTasks))]
    public async Task CanonicalTaskFixture_WithoutTaskProcessor_ShouldBeRejectedByAdmission(string taskType)
    {
        var fixture = FixtureFor(taskType) with
        {
            Operators = ["ImageAcquisition", "Thresholding", "ResultJudgment", "ResultOutput"]
        };
        var sink = new CapturingAgentRunEventSink();
        var application = CreateApplication(sink);
        var plan = BuildPlan(fixture);

        var result = (await application.BuildAsync(
            BuildCommand.FromGenerationRequest(
                BuildRequest(plan, fixture.ParameterSelections),
                transport: BuildCommandTransports.Internal,
                persistResult: false),
            CancellationToken.None)).Result;

        result.Success.Should().BeFalse();
        result.Flow.Should().BeNull();
        result.FailureSummary.Should().NotBeNull();
        result.FailureSummary!.Category.Should().Be("workflow_artifact_admission");
        result.FailureSummary.Code.Should().Be("route_missing_task_processor");
        result.FailureSummary.SecondaryDiagnosticCodes.Should().Contain("route_semantics_not_satisfied");
        result.BuildResult.Should().NotBeNull();
        var build = result.BuildResult!;
        build.RouteSemanticsSatisfied.Should().BeFalse();
        build.ApplyGate.CanvasApplyReady.Should().BeFalse();
        build.ApplyGate.ApplyBlockers.Should().Contain("route_missing_task_processor");
        AssertPublicProjectionSafe(result, sink);
    }

    [Fact(DisplayName = "Original scratch Canny area file-sample failure should preserve every explicit contract")]
    public async Task OriginalFailureFixture_ShouldBuildCannyAreaRouteWithoutBlobSubstitution()
    {
        var fixture = FixtureFor(AiVisionTaskTypes.SurfaceDefect) with
        {
            Prompt = "检测金属表面划痕，必须使用 Canny，计算并输出划痕面积，输入使用文件样张。",
            AcceptanceCriteria = "OK: defect area <= 2.5 mm2; NG: defect area exceeds 2.5 mm2",
            MeasurementTarget = "defect_area",
            OutputTarget = "defect_area",
            Operators = ["ImageAcquisition", "SurfaceDefectDetection", "ResultJudgment", "ResultOutput"],
            JudgmentField = "DefectArea",
            JudgmentCondition = "LessOrEqual",
            ExpectedValue = "2.5",
            JudgmentSourcePort = "DefectArea",
            OutputSourcePort = "DefectArea"
        };
        var initialPlan = BuildPlan(fixture, includeHash: false);
        var fidelity = new VisionAgentPlanFidelityValidator().Validate(
            new VisionAgentPlanModeRequest
            {
                Description = fixture.Prompt,
                OriginalUserPrompt = fixture.Prompt,
                RequirementMode = AiRequirementModes.Draft,
                ConfirmedPlanAnswers = initialPlan.ConfirmedPlanAnswers
            },
            initialPlan);
        var plan = initialPlan with
        {
            RecommendedRoute = fidelity.Route,
            PlanFidelity = fidelity.Assessment,
            Risks = fidelity.Risks.ToList(),
            ContractRepairNotes = fidelity.RepairNotes.ToList(),
            PlanWarnings = fidelity.Warnings.ToList()
        };
        plan = plan with { PlanHash = VisionAgentOrchestrator.ComputePlanHash(plan) };
        var sink = new CapturingAgentRunEventSink();
        var application = CreateApplication(sink);

        var result = (await application.BuildAsync(
            BuildCommand.FromGenerationRequest(
                BuildRequest(plan, fixture.ParameterSelections),
                transport: BuildCommandTransports.Internal,
                persistResult: false),
            CancellationToken.None)).Result;

        fidelity.Assessment.Satisfied.Should().BeTrue();
        fidelity.Assessment.RequiredCapabilities.Should().ContainSingle(item =>
            item.OperatorType == "EdgeDetection" &&
            item.ParameterName == "Method" &&
            item.RequiredValue == "Canny");
        fidelity.Assessment.RequiredOutputSemantics.Should().Contain("defect_area");
        result.Success.Should().BeTrue(result.FailureSummary?.Message);
        result.BuildResult.Should().NotBeNull();
        var build = result.BuildResult!;
        var flow = result.Flow.Should().BeOfType<OperatorFlowDto>().Subject;
        build.TaskType.Should().Be(AiVisionTaskTypes.SurfaceDefect);
        build.OperatorPipeline.Select(item => item.OperatorType).Should()
            .Contain(["EdgeDetection", "SurfaceDefectDetection", "ResultJudgment", "ResultOutput"])
            .And.NotContain("BlobAnalysis");
        Mapping(build, "ImageAcquisition", "SourceType").ValueSummary.Should().Be("File");
        Mapping(build, "EdgeDetection", "Method").ValueSummary.Should().Be("Canny");
        Mapping(build, "ResultJudgment", "FieldName").ValueSummary.Should().Be("DefectArea");
        Mapping(build, "ResultJudgment", "Condition").ValueSummary.Should().Be("LessOrEqual");
        Mapping(build, "ResultJudgment", "ExpectValue").ValueSummary.Should().Be("2.5");
        HasConnection(flow, "EdgeDetection", "Edges", "SurfaceDefectDetection", "Image").Should().BeTrue();
        HasConnection(flow, "SurfaceDefectDetection", "DefectArea", "ResultJudgment", "Value").Should().BeTrue();
        HasConnection(flow, "SurfaceDefectDetection", "DefectArea", "ResultOutput", "Data").Should().BeTrue();
        HasConnection(flow, "BlobAnalysis", "BlobCount", "ResultJudgment", "Value").Should().BeFalse();
        HasConnection(flow, "BlobAnalysis", "BlobCount", "ResultOutput", "Data").Should().BeFalse();
        build.RouteSemanticsSatisfied.Should().BeTrue();
        build.ApplyGate.CanvasApplyReady.Should().BeTrue(string.Join(",", build.ApplyGate.ApplyBlockers));
        AssertPublicProjectionSafe(result, sink);
    }

    [Fact(DisplayName = "A safe v1 alias Plan should retain its original hash, normalize in memory and pass route v2")]
    public async Task SafeLegacyV1AliasPlan_ShouldBuildWithNormalizationAuditAndRouteV2Evidence()
    {
        var fixture = FixtureFor(AiVisionTaskTypes.SurfaceDefect) with
        {
            ImageSource = "image_folder"
        };
        var baseline = BuildPlan(fixture, VisionAgentPlanContractVersions.V1, includeHash: false);
        const string legacyTask = AiVisionTaskTypes.SurfaceOrPoseDefect;
        var legacy = baseline with
        {
            Intent = legacyTask,
            ConfirmedPlanAnswers = baseline.ConfirmedPlanAnswers
                .Select(answer => answer.Field switch
                {
                    VisionAgentPlanAnswerFields.TaskType => answer with { Value = legacyTask },
                    VisionAgentPlanAnswerFields.ImageSource => answer with { Value = "image_folder" },
                    _ => answer
                })
                .ToList(),
            SemanticExtraction = baseline.SemanticExtraction! with
            {
                TaskType = legacyTask,
                ImageSource = "image_folder"
            },
            RequirementMaturity = baseline.RequirementMaturity! with
            {
                TaskType = legacyTask
            }
        };
        legacy = legacy with { PlanHash = VisionAgentOrchestrator.ComputePlanHash(legacy) };
        var originalHash = legacy.PlanHash;
        var sink = new CapturingAgentRunEventSink();
        var application = CreateApplication(sink);

        var result = (await application.BuildAsync(
            BuildCommand.FromGenerationRequest(
                BuildRequest(legacy, fixture.ParameterSelections),
                transport: BuildCommandTransports.Internal,
                persistResult: false),
            CancellationToken.None)).Result;

        result.Success.Should().BeTrue(result.FailureSummary?.Message);
        result.PlanHash.Should().Be(originalHash);
        result.ContractVersion.Should().Be(VisionAgentPlanContractVersions.V1);
        result.BuildResult.Should().NotBeNull();
        var build = result.BuildResult!;
        var flow = result.Flow.Should().BeOfType<OperatorFlowDto>().Subject;
        build.TaskType.Should().Be(AiVisionTaskTypes.SurfaceDefect);
        build.RouteSemanticsSatisfied.Should().BeTrue();
        Mapping(build, "ImageAcquisition", "SourceType").ValueSummary.Should().Be("File");
        flow.Operators.Should().OnlyContain(op =>
            ReadMetadata(op, "agentTaskType") == AiVisionTaskTypes.SurfaceDefect &&
            ReadMetadata(op, "agentRouteContractVersion") == VisionAgentPlanContractVersions.V2);
        var planLoadAudit = JsonSerializer.Serialize(
            sink.Events.Where(evt =>
                evt.Stage == "plan_generation" &&
                evt.Title == "plan_snapshot_loader" &&
                evt.EventType == AgentRunEventTypes.ToolCallCompleted),
            AgentRunEventJson.Options);
        planLoadAudit.Should().Contain("\"rawValue\":\"surface_or_pose_defect\"");
        planLoadAudit.Should().Contain("\"canonicalValue\":\"surface_defect\"");
        AssertPublicProjectionSafe(result, sink);
    }

    [Fact(DisplayName = "A readable Blob-only v1 template Plan should be blocked by route v2 admission")]
    public async Task UnsafeLegacyV1BlobTemplatePlan_ShouldFailClosedWithSpecificRouteDiagnostic()
    {
        var fixture = FixtureFor(AiVisionTaskTypes.TemplateLocation) with
        {
            Operators = ["ImageAcquisition", "BlobAnalysis", "ResultJudgment", "ResultOutput"]
        };
        var baseline = BuildPlan(fixture, VisionAgentPlanContractVersions.V1, includeHash: false);
        const string legacyTask = "template_matching";
        var legacy = baseline with
        {
            Intent = legacyTask,
            ConfirmedPlanAnswers = baseline.ConfirmedPlanAnswers
                .Select(answer => answer.Field == VisionAgentPlanAnswerFields.TaskType
                    ? answer with { Value = legacyTask }
                    : answer)
                .ToList(),
            SemanticExtraction = baseline.SemanticExtraction! with { TaskType = legacyTask },
            RequirementMaturity = baseline.RequirementMaturity! with { TaskType = legacyTask }
        };
        legacy = legacy with { PlanHash = VisionAgentOrchestrator.ComputePlanHash(legacy) };
        var application = CreateApplication(new CapturingAgentRunEventSink());

        var result = (await application.BuildAsync(
            BuildCommand.FromGenerationRequest(
                BuildRequest(legacy, fixture.ParameterSelections),
                transport: BuildCommandTransports.Internal,
                persistResult: false),
            CancellationToken.None)).Result;

        result.Success.Should().BeFalse();
        result.Flow.Should().BeNull();
        result.ContractVersion.Should().Be(VisionAgentPlanContractVersions.V1);
        result.FailureSummary.Should().NotBeNull();
        result.FailureSummary!.Code.Should().Be("route_missing_task_processor");
        result.BuildResult!.TaskType.Should().Be(AiVisionTaskTypes.TemplateLocation);
        result.BuildResult.RouteSemanticsSatisfied.Should().BeFalse();
        result.BuildResult.ApplyGate.ApplyBlockers.Should().Contain("route_missing_task_processor");
    }

    [Theory(DisplayName = "Every task legacy/UI/route alias should retain raw evidence and resolve one v2 route key")]
    [MemberData(nameof(TaskAliasMigrationCases))]
    public void TaskAliasMigrationMatrix_ShouldPreserveRawCanonicalAndRouteV2Key(
        string rawValue,
        string canonicalValue,
        string routeContractKey)
    {
        var answer = PlanAnswer(VisionAgentPlanAnswerFields.TaskType, rawValue);
        var plan = new VisionAgentPlanModeResult
        {
            Intent = rawValue,
            ConfirmedPlanAnswers = [answer],
            SemanticExtraction = new VisionAgentSemanticExtractionResult { TaskType = rawValue },
            RequirementMaturity = new AiRequirementMaturityResult { TaskType = rawValue }
        };

        var validation = new VisionAgentPlanAnswerValidator().Validate(
            plan,
            [answer],
            null,
            acceptedRecommendedDefaults: false);

        validation.RequirementAnswers[VisionAgentPlanAnswerFields.TaskType].Should().Be(canonicalValue);
        validation.TaskTypeNormalizationAudit.Should().Contain(item =>
            item.RawValue == rawValue &&
            item.CanonicalValue == canonicalValue);
        VisionTaskRouteContractRegistry.NormalizeTaskType(rawValue).Should().Be(routeContractKey);
        plan.ConfirmedPlanAnswers.Single().Value.Should().Be(rawValue, "normalization must not rewrite the historical Plan snapshot");
    }

    [Theory(DisplayName = "Image-source migration matrix should preserve original values and fail closed for unsupported sources")]
    [MemberData(nameof(ImageSourceMigrationCases))]
    public void ImageSourceMigrationMatrix_ShouldUseOneFailClosedAliasTable(
        string rawValue,
        string expectedKind,
        string expectedSourceType,
        string expectedDiagnostic)
    {
        var resolution = VisionAgentImageSourceResolver.Resolve(rawValue);

        resolution.OriginalValue.Should().Be(rawValue);
        resolution.Kind.ToString().Should().Be(expectedKind);
        resolution.SourceType.Should().Be(expectedSourceType);
        resolution.DiagnosticCode.Should().Be(expectedDiagnostic);
        resolution.Supported.Should().Be(expectedKind is "File" or "Camera");
    }

    private static VisionAgentBuildApplicationService CreateApplication(CapturingAgentRunEventSink sink)
    {
        return new VisionAgentBuildApplicationService(
            new BuildExecutionAdapter(CreateBuildOrchestrator(sink)),
            new VisionAgentPlanAnswerValidator(),
            new VisionAgentPlanRequirementOverlay(),
            NullLogger<VisionAgentBuildApplicationService>.Instance,
            Options.Create(new AgentGenerateFlowOptions { Enabled = true }),
            sink,
            WorkflowArtifactAdmissionTestSupport.CreateGate());
    }

    private static VisionAgentBuildOrchestrator CreateBuildOrchestrator(CapturingAgentRunEventSink sink)
    {
        var redactor = new AgentRunEventRedactor();
        var runner = new BuildToolRunner(
            new VisionAgentToolRegistry(
            [
                new FlowTemplateMatchTool(),
                new FlowTemplateSkeletonTool(),
                new FlowValidationTool(),
                new DryRunFlowTool(),
                new RuntimePackagePrecheckTool()
            ]),
            redactor,
            sink);
        return new VisionAgentBuildOrchestrator(
            new BuildPlanContextLoader(sink),
            new BuildIntentResolver(),
            new TemplateStrategyResolver(runner),
            new PlanSelectionResolver(),
            new OperatorPipelineSelector(),
            new ParameterMappingService(),
            new WorkflowDraftBuilder(),
            runner,
            new BuildReadinessReviewService(),
            new WorkflowDiffService(),
            new ApplyGateResolver(),
            new BuildResultAssembler(redactor, sink),
            NullLogger<VisionAgentBuildOrchestrator>.Instance,
            sink);
    }

    private static VisionAgentPlanModeResult BuildPlan(
        TaskFixture fixture,
        string contractVersion = VisionAgentPlanContractVersions.V2,
        bool includeHash = true)
    {
        var answers = BuildAnswers(fixture);
        var semantic = new VisionAgentSemanticExtractionResult
        {
            IsVisionRequest = true,
            Intent = "new_flow",
            TaskType = fixture.TaskType,
            Confidence = 0.96,
            TaskTypeConfidence = 0.95,
            InspectionObject = fixture.InspectionObject,
            TargetAttribute = fixture.TargetAttribute,
            MeasurementTarget = fixture.MeasurementTarget,
            DefectType = fixture.DefectType,
            ImageSource = fixture.ImageSource,
            OkCondition = fixture.AcceptanceCriteria,
            NgCondition = "otherwise NG",
            OutputTarget = fixture.OutputTarget,
            CanPlanCandidate = true,
            CanBuildCandidate = true,
            ObjectSignals = [fixture.InspectionObject],
            TaskSignals = [fixture.TaskType],
            Source = VisionAgentSemanticSources.Model,
            MetadataOnly = true
        };
        var maturity = VisionAgentRequirementMaturityGate.Evaluate(
            new VisionAgentRequirementMaturityRequest
            {
                Description = fixture.Prompt,
                Mode = "new",
                HasPendingPlan = true,
                RequirementMode = AiRequirementModes.Draft
            },
            semantic);
        var plan = new VisionAgentPlanModeResult
        {
            PlanContractVersion = contractVersion,
            PlanId = $"plan_e2e_{fixture.TaskType}",
            PlanSource = "contract_fixture",
            CurrentPhase = VisionAgentPlanPhases.ReadyToBuild,
            OriginalUserPrompt = fixture.Prompt,
            Goal = fixture.Prompt,
            Intent = fixture.TaskType,
            Confidence = "high",
            RequirementUnderstanding =
            [
                fixture.InspectionObject,
                fixture.AcceptanceCriteria,
                fixture.OutputTarget
            ],
            ConfirmedPlanAnswers = answers,
            ResolvedPlanFields = answers.Select(answer => answer.Field)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            RemainingPlanFields = [],
            RecommendedRoute = new VisionAgentRecommendedRoute
            {
                RouteId = $"{fixture.TaskType}_e2e_route",
                Title = $"{fixture.TaskType} end-to-end route",
                Summary = "Contract fixture route with a task processor, judgment and public output.",
                Operators = fixture.Operators,
                TemplateDecision = "free_generate"
            },
            ClarificationQuestions = [],
            RecommendedDefaults = [],
            Risks = ["External resources remain deployment-time bindings."],
            AcceptanceCriteria = [fixture.AcceptanceCriteria],
            ExecutablePlan = ["Build canonical graph", "Validate route v2", "Run admission"],
            CanBuild = true,
            BlockingReasons = [],
            BuildReadiness = new VisionAgentBuildReadinessSnapshot
            {
                CanBuild = true,
                ResolvedFields = answers.Select(answer => answer.Field).ToList(),
                RemainingFields = [],
                ContractVersion = contractVersion
            },
            SemanticExtraction = semantic,
            RequirementMaturity = maturity with
            {
                TaskType = fixture.TaskType,
                CanPlan = true,
                CanBuild = true,
                MissingFields = [],
                BlockingReasons = []
            },
            NextAction = "Build",
            OperatorCatalogVersion = "catalog.e2e.v2",
            TemplateCatalogVersion = "templates.e2e.v1",
            StationBoundarySummary = "metadata-only test boundary",
            PlcOutputPolicy = "local_result_only",
            PlanFidelity = new VisionAgentPlanFidelityAssessment
            {
                ContractVersion = VisionAgentPlanContractVersions.V2,
                TaskType = fixture.TaskType,
                Satisfied = true
            },
            MetadataOnly = true
        };

        return includeHash
            ? plan with { PlanHash = VisionAgentOrchestrator.ComputePlanHash(plan) }
            : plan;
    }

    private static AiFlowGenerationRequest BuildRequest(
        VisionAgentPlanModeResult plan,
        IReadOnlyDictionary<string, string> parameterSelections)
    {
        return new AiFlowGenerationRequest(plan.OriginalUserPrompt, Mode: GenerateFlowMode.New)
        {
            AgentRunId = $"ar_e2e_{Guid.NewGuid():N}",
            UseVisionAgentGenerateFlow = true,
            AgentGenerateFlowMode = AiAgentGenerateFlowModes.Scripted,
            RequirementMode = AiRequirementModes.Draft,
            BuildFromPlan = new VisionAgentBuildFromPlanRequest
            {
                PlanId = plan.PlanId,
                PlanHash = plan.PlanHash,
                PlanSnapshot = plan,
                ConfirmedAnswers = plan.ConfirmedPlanAnswers,
                UserSelections = new Dictionary<string, string>(parameterSelections, StringComparer.OrdinalIgnoreCase),
                AcceptedRecommendedDefaults = false,
                AcceptedDefaults = [],
                ResourceDecisions = [],
                OperatorCatalogVersion = plan.OperatorCatalogVersion,
                StationBoundarySummary = plan.StationBoundarySummary,
                PlcOutputPolicy = plan.PlcOutputPolicy,
                BuildIntent = "new",
                OriginalUserPrompt = plan.OriginalUserPrompt,
                MetadataOnly = true
            }
        };
    }

    private static List<VisionAgentPlanAnswer> BuildAnswers(TaskFixture fixture)
    {
        var answers = new List<VisionAgentPlanAnswer>
        {
            PlanAnswer(VisionAgentPlanAnswerFields.InspectionObject, fixture.InspectionObject),
            PlanAnswer(VisionAgentPlanAnswerFields.TaskType, fixture.TaskType),
            PlanAnswer(VisionAgentPlanAnswerFields.ImageSource, fixture.ImageSource),
            PlanAnswer(VisionAgentPlanAnswerFields.AcceptanceCriteria, fixture.AcceptanceCriteria),
            PlanAnswer(VisionAgentPlanAnswerFields.OutputTarget, fixture.OutputTarget)
        };
        AddOptionalAnswer(answers, VisionAgentPlanAnswerFields.TargetAttribute, fixture.TargetAttribute);
        AddOptionalAnswer(answers, VisionAgentPlanAnswerFields.DefectType, fixture.DefectType);
        AddOptionalAnswer(answers, VisionAgentPlanAnswerFields.MeasurementTarget, fixture.MeasurementTarget);
        return answers;
    }

    private static VisionAgentPlanAnswer PlanAnswer(string field, string value) => new()
    {
        QuestionId = $"q_{field}",
        Field = field,
        Value = value,
        Origin = VisionAgentPlanAnswerOrigins.ExplicitUserSelection,
        Resolved = true
    };

    private static void AddOptionalAnswer(
        ICollection<VisionAgentPlanAnswer> answers,
        string field,
        string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            answers.Add(PlanAnswer(field, value));
        }
    }

    private static VisionAgentParameterMapping Mapping(
        VisionAgentBuildResult build,
        string operatorType,
        string parameterName)
    {
        return build.ParameterMapping.Should().ContainSingle(item =>
            item.OperatorType == operatorType &&
            item.ParameterName == parameterName).Subject;
    }

    private static void AssertJudgmentThresholds(VisionAgentBuildResult build, TaskFixture fixture)
    {
        if (!string.IsNullOrWhiteSpace(fixture.ExpectedValue))
        {
            var mapping = Mapping(build, "ResultJudgment", "ExpectValue");
            mapping.ValueSummary.Should().Be(fixture.ExpectedValue);
            mapping.Pending.Should().BeFalse();
        }

        if (!string.IsNullOrWhiteSpace(fixture.ExpectedMinimum))
        {
            var minimum = Mapping(build, "ResultJudgment", "ExpectValueMin");
            minimum.ValueSummary.Should().Be(fixture.ExpectedMinimum);
            minimum.Pending.Should().BeFalse();
        }

        if (!string.IsNullOrWhiteSpace(fixture.ExpectedMaximum))
        {
            var maximum = Mapping(build, "ResultJudgment", "ExpectValueMax");
            maximum.ValueSummary.Should().Be(fixture.ExpectedMaximum);
            maximum.Pending.Should().BeFalse();
        }
    }

    private static bool HasConnection(
        OperatorFlowDto flow,
        string sourceOperatorType,
        string sourcePortName,
        string targetOperatorType,
        string targetPortName)
    {
        foreach (var connection in flow.Connections)
        {
            var source = flow.Operators.FirstOrDefault(op => op.Id == connection.SourceOperatorId);
            var target = flow.Operators.FirstOrDefault(op => op.Id == connection.TargetOperatorId);
            if (source == null || target == null ||
                !source.Type.ToString().Equals(sourceOperatorType, StringComparison.OrdinalIgnoreCase) ||
                !target.Type.ToString().Equals(targetOperatorType, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var sourcePort = source.OutputPorts.FirstOrDefault(port => port.Id == connection.SourcePortId);
            var targetPort = target.InputPorts.FirstOrDefault(port => port.Id == connection.TargetPortId);
            if (sourcePort?.Name.Equals(sourcePortName, StringComparison.OrdinalIgnoreCase) == true &&
                targetPort?.Name.Equals(targetPortName, StringComparison.OrdinalIgnoreCase) == true)
            {
                return true;
            }
        }

        return false;
    }

    private static string DescribeConnections(OperatorFlowDto flow)
    {
        return string.Join(
            "; ",
            flow.Connections.Select(connection =>
            {
                var source = flow.Operators.FirstOrDefault(op => op.Id == connection.SourceOperatorId);
                var target = flow.Operators.FirstOrDefault(op => op.Id == connection.TargetOperatorId);
                var sourcePort = source?.OutputPorts.FirstOrDefault(port => port.Id == connection.SourcePortId);
                var targetPort = target?.InputPorts.FirstOrDefault(port => port.Id == connection.TargetPortId);
                return $"{source?.Type}.{sourcePort?.Name}->{target?.Type}.{targetPort?.Name}";
            }));
    }

    private static string ReadMetadata(OperatorDto op, string key)
    {
        if (op.Metadata?.TryGetValue(key, out var value) != true || value == null)
        {
            return string.Empty;
        }

        return value is JsonElement element
            ? element.ValueKind == JsonValueKind.String ? element.GetString() ?? string.Empty : element.ToString()
            : value.ToString() ?? string.Empty;
    }

    private static void AssertPublicProjectionSafe(
        AiFlowGenerationResult result,
        CapturingAgentRunEventSink sink)
    {
        var publicJson = JsonSerializer.Serialize(new
        {
            result.AiExplanation,
            result.FailureSummary,
            result.BuildResult,
            sink.Events
        }, AgentRunEventJson.Options);
        publicJson.Should().NotBeNullOrWhiteSpace();
        publicJson.Should().NotContain("systemPrompt");
        publicJson.Should().NotContain("rawPrompt");
        publicJson.Should().NotContain("chainOfThought");
        publicJson.Should().NotContain("reasoning_content");
        publicJson.Should().NotContain("C:\\factory");
        publicJson.Should().NotContain("sk-secret");
        publicJson.Should().NotContain("192.168.");
        publicJson.Should().NotContain("data:image");
        publicJson.Should().NotContain(";base64");
    }

    private static TaskFixture FixtureFor(string taskType)
    {
        return taskType switch
        {
            AiVisionTaskTypes.PresenceAbsence => new(
                taskType,
                "检查装配件是否存在并输出结果。",
                "assembly part",
                "OK: part is present; NG: part is missing",
                "local_result_payload",
                ["ImageAcquisition", "BlobAnalysis", "ResultJudgment", "ResultOutput"],
                "BlobAnalysis",
                "PresenceCount",
                "GreaterOrEqual",
                "1",
                string.Empty,
                string.Empty,
                "BlobAnalysis",
                "BlobCount",
                "BlobAnalysis",
                "BlobCount",
                "Data"),
            AiVisionTaskTypes.AttributeClassification => new(
                taskType,
                "判断水果成熟属性并输出分类结果。",
                "fruit",
                "OK when class equals ripe and confidence >= 0.85",
                "local_result_payload",
                ["ImageAcquisition", "DeepLearning", "ResultJudgment", "ResultOutput"],
                "DeepLearning",
                "TopClassLabel",
                "Equal",
                "ripe",
                string.Empty,
                string.Empty,
                "DeepLearning",
                "TopClassLabel",
                "DeepLearning",
                "ClassificationTopK",
                "Data",
                TargetAttribute: "ripeness",
                Selections: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["classification_ok_label"] = "ripe"
                }),
            AiVisionTaskTypes.ObjectDetection => new(
                taskType,
                "检测包装中的目标并输出目标明细。",
                "package object",
                "OK when at least 2 objects are detected",
                "local_result_payload",
                ["ImageAcquisition", "DeepLearning", "ResultJudgment", "ResultOutput"],
                "DeepLearning",
                "ObjectCount",
                "GreaterOrEqual",
                "2",
                string.Empty,
                string.Empty,
                "DeepLearning",
                "ObjectCount",
                "DeepLearning",
                "DetectionList",
                "Data"),
            AiVisionTaskTypes.TemplateLocation => new(
                taskType,
                "定位零件基准并输出位姿匹配结果。",
                "reference part",
                "OK when the task-specific match succeeds",
                "local_result_payload",
                ["ImageAcquisition", "TemplateMatching", "ResultJudgment", "ResultOutput"],
                "TemplateMatching",
                "IsMatch",
                "Equal",
                "true",
                string.Empty,
                string.Empty,
                "TemplateMatching",
                "IsMatch",
                "TemplateMatching",
                "Matches",
                "Data"),
            AiVisionTaskTypes.SurfaceDefect => new(
                taskType,
                "检查金属表面缺陷并输出缺陷证据。",
                "metal surface",
                "OK: no defects; NG: defect found",
                "local_result_payload",
                ["ImageAcquisition", "SurfaceDefectDetection", "ResultJudgment", "ResultOutput"],
                "SurfaceDefectDetection",
                "DefectCount",
                "LessOrEqual",
                "0",
                string.Empty,
                string.Empty,
                "SurfaceDefectDetection",
                "DefectCount",
                "SurfaceDefectDetection",
                "Diagnostics",
                "Data",
                DefectType: "scratch"),
            AiVisionTaskTypes.GeometryMeasurement => new(
                taskType,
                "测量圆孔尺寸并输出带单位的结果。",
                "circular hole",
                "OK: distance 10 to 12 mm; NG: outside range",
                "local_result_payload",
                ["ImageAcquisition", "CircleMeasurement", "UnitConvert", "Aggregator", "ResultJudgment", "ResultOutput"],
                "CircleMeasurement",
                "Value",
                "Range",
                string.Empty,
                "10",
                "12",
                "UnitConvert",
                "Result",
                "Aggregator",
                "Result",
                "Data",
                MeasurementTarget: "distance"),
            AiVisionTaskTypes.WireSequence => new(
                taskType,
                "检查端子线序并输出实际排列。",
                "terminal harness",
                "OK when the task-specific match succeeds",
                "local_result_payload",
                ["ImageAcquisition", "DeepLearning", "DetectionSequenceJudge", "ResultJudgment", "ResultOutput"],
                "DetectionSequenceJudge",
                "IsMatch",
                "Equal",
                "true",
                string.Empty,
                string.Empty,
                "DetectionSequenceJudge",
                "IsMatch",
                "DetectionSequenceJudge",
                "ActualOrder",
                "Data"),
            AiVisionTaskTypes.CodeRecognition => new(
                taskType,
                "读取产品码并输出解码文本和码制。",
                "product code",
                "OK: code = \"ABC-123\"; NG: code differs",
                "local_result_payload",
                ["ImageAcquisition", "CodeRecognition", "ResultJudgment", "ResultOutput"],
                "CodeRecognition",
                "Text",
                "Equal",
                "ABC-123",
                string.Empty,
                string.Empty,
                "CodeRecognition",
                "Text",
                "CodeRecognition",
                "Text",
                "Text"),
            _ => throw new ArgumentOutOfRangeException(nameof(taskType), taskType, null)
        };
    }

    private sealed record TaskFixture(
        string TaskType,
        string Prompt,
        string InspectionObject,
        string AcceptanceCriteria,
        string OutputTarget,
        List<string> Operators,
        string TaskProcessor,
        string JudgmentField,
        string JudgmentCondition,
        string ExpectedValue,
        string ExpectedMinimum,
        string ExpectedMaximum,
        string JudgmentSourceOperator,
        string JudgmentSourcePort,
        string OutputSourceOperator,
        string OutputSourcePort,
        string OutputTargetPort,
        string ImageSource = "file_sample",
        string TargetAttribute = "",
        string DefectType = "",
        string MeasurementTarget = "",
        IReadOnlyDictionary<string, string>? Selections = null)
    {
        public IReadOnlyDictionary<string, string> ParameterSelections { get; init; } =
            Selections ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
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
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<AiFlowGenerationResult> BuildFromPlanAsync(
            AiFlowGenerationRequest request,
            CancellationToken cancellationToken) =>
            _buildOrchestrator.BuildAsync(request, cancellationToken);
    }

    private sealed class CapturingAgentRunEventSink : IAgentRunEventSink
    {
        public List<AgentRunEventDraft> Events { get; } = [];

        public void Append(string? runId, AgentRunEventDraft draft) => Events.Add(draft);

        public void StageStarted(string? runId, string stage, string title, string summary, object? payload = null) =>
            Append(runId, Draft(AgentRunEventTypes.StageStarted, stage, title, summary, AgentRunEventStatuses.Running, payload));

        public void StageCompleted(string? runId, string stage, string title, string summary, object? payload = null) =>
            Append(runId, Draft(AgentRunEventTypes.StageCompleted, stage, title, summary, AgentRunEventStatuses.Completed, payload));

        public void ToolStarted(string? runId, string stage, string toolName, object? payload = null) =>
            Append(runId, Draft(AgentRunEventTypes.ToolCallStarted, stage, toolName, "started", AgentRunEventStatuses.Running, payload));

        public void ToolCompleted(string? runId, string stage, string toolName, long durationMs, object? payload = null) =>
            Append(runId, Draft(AgentRunEventTypes.ToolCallCompleted, stage, toolName, "completed", AgentRunEventStatuses.Completed, payload));

        public void ToolFailed(string? runId, string stage, string toolName, long durationMs, string summary, object? payload = null) =>
            Append(runId, Draft(AgentRunEventTypes.ToolCallFailed, stage, toolName, summary, AgentRunEventStatuses.Failed, payload));

        private static AgentRunEventDraft Draft(
            string eventType,
            string stage,
            string title,
            string summary,
            string status,
            object? payload) => new()
        {
            EventType = eventType,
            Stage = stage,
            Title = title,
            Summary = summary,
            Status = status,
            Payload = payload
        };
    }
}
