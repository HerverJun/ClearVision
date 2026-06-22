using System.Text.Json;
using ClearVision.Product.Application.DTOs;
using ClearVision.Product.Core.DTOs;
using ClearVision.Product.Infrastructure.AI;
using ClearVision.Product.Infrastructure.AI.Agent;
using FluentAssertions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace ClearVision.Product.Tests.AI;

public sealed class BuildFromPlanEntryParityTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(
        Path.GetTempPath(),
        "clearvision-build-entry-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task AgentRunAndWebMessage_ShouldReturnEquivalentBusinessOutcome()
    {
        var plan = BuildPlan();
        var request = BuildRequest(plan);
        var agentRun = await CreateService().BuildAsync(
            BuildCommand.FromGenerationRequest(
                request,
                "run-agent",
                "req-agent",
                BuildCommandTransports.AgentRun,
                persistResult: false),
            CancellationToken.None);
        var webMessage = await CreateService().BuildAsync(
            BuildCommand.FromGenerationRequest(
                request,
                "run-web",
                "req-web",
                BuildCommandTransports.WebMessage,
                persistResult: false),
            CancellationToken.None);

        BusinessProjection(agentRun).Should().BeEquivalentTo(BusinessProjection(webMessage));
    }

    [Fact]
    public async Task DisabledBuild_ShouldFailWithCanonicalCode()
    {
        var outcome = await CreateService(enabled: false).BuildAsync(
            BuildCommand.FromGenerationRequest(BuildRequest(BuildPlan()), "run-disabled", persistResult: false),
            CancellationToken.None);

        outcome.CompletionStatus.Should().Be(AiFlowGenerationResult.CompletionStatusFailed);
        outcome.FailureType.Should().Be(AiFlowGenerationResult.FailureTypeSystemError);
        outcome.FailureCode.Should().Be(VisionAgentBuildFailureCodes.Disabled);
    }

    [Fact]
    public async Task MissingContract_ShouldFailClosed()
    {
        var request = new AiFlowGenerationRequest("detect scratches")
        {
            UseVisionAgentGenerateFlow = true
        };

        var outcome = await CreateService().BuildAsync(
            BuildCommand.FromGenerationRequest(request, "run-missing", persistResult: false),
            CancellationToken.None);

        outcome.FailureCode.Should().Be(VisionAgentBuildFailureCodes.ContractInvalid);
        outcome.Result.BuildResult!.ApplyGate.Blocked.Should().BeTrue();
    }

    [Fact]
    public async Task PlanIdMismatch_ShouldFailClosedWithFixedCode()
    {
        var plan = BuildPlan();
        var request = BuildRequest(plan, build => build with { PlanId = "different-plan" });

        var outcome = await CreateService().BuildAsync(
            BuildCommand.FromGenerationRequest(request, "run-plan-mismatch", persistResult: false),
            CancellationToken.None);

        outcome.FailureCode.Should().Be(VisionAgentBuildFailureCodes.PlanIdMismatch);
        outcome.CompletionStatus.Should().Be(AiFlowGenerationResult.CompletionStatusFailed);
    }

    [Fact]
    public async Task PlanHashMismatch_ShouldRejectAsStalePlan()
    {
        var plan = BuildPlan();
        var request = BuildRequest(plan, build => build with { PlanHash = "sha256:stale" });

        var outcome = await CreateService().BuildAsync(
            BuildCommand.FromGenerationRequest(request, "run-stale", persistResult: false),
            CancellationToken.None);

        outcome.FailureCode.Should().Be(VisionAgentBuildFailureCodes.StalePlan);
        outcome.Result.BuildResult!.ApplyGate.ApplyBlockers.Should().Contain(VisionAgentBuildFailureCodes.StalePlan);
    }

    [Fact]
    public async Task LegacyContractWithoutHash_ShouldSucceedWithExplicitWarning()
    {
        var legacy = BuildPlan(VisionAgentPlanContractVersions.V1, includeHash: false);
        var request = BuildRequest(legacy, build => build with { PlanHash = string.Empty });

        var outcome = await CreateService().BuildAsync(
            BuildCommand.FromGenerationRequest(request, "run-legacy", persistResult: false),
            CancellationToken.None);

        outcome.CompletionStatus.Should().Be(AiFlowGenerationResult.CompletionStatusCompleted);
        outcome.Result.BuildResult!.PublicWarnings.Should().Contain("legacy_plan_hash_missing");
        outcome.PlanHash.Should().StartWith("sha256:");
    }

    [Fact]
    public async Task ToolLoopMode_ShouldNormalizeRequestedAndEffectiveMode()
    {
        var request = BuildRequest(BuildPlan()) with
        {
            AgentGenerateFlowMode = AiAgentGenerateFlowModes.ToolLoop
        };

        var outcome = await CreateService().BuildAsync(
            BuildCommand.FromGenerationRequest(request, "run-tool-loop", persistResult: false),
            CancellationToken.None);

        outcome.RequestedMode.Should().Be(AiAgentGenerateFlowModes.ToolLoop);
        outcome.EffectiveMode.Should().Be(AiAgentGenerateFlowModes.ToolLoop);
        outcome.ToolLoopEntered.Should().BeTrue();
        outcome.Result.BuildResult!.RequestedMode.Should().Be(AiAgentGenerateFlowModes.ToolLoop);
    }

    [Fact]
    public async Task SameRunTerminalProjection_ShouldPersistSessionOnce()
    {
        var conversation = new ConversationalFlowService(_tempRoot);
        var service = CreateService(conversation: conversation);
        var request = BuildRequest(BuildPlan()) with { SessionId = "session-idempotent" };
        var command = BuildCommand.FromGenerationRequest(
            request,
            "run-idempotent",
            "req-idempotent",
            BuildCommandTransports.AgentRun);

        var first = await service.BuildAsync(command, CancellationToken.None);
        var second = await service.BuildAsync(command, CancellationToken.None);

        first.Persisted.Should().BeTrue();
        second.Persisted.Should().BeFalse();
        conversation.GetSession("session-idempotent")!.History.Should().HaveCount(1);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }

    private VisionAgentBuildApplicationService CreateService(
        bool enabled = true,
        string mode = AiAgentGenerateFlowModes.Scripted,
        IConversationalFlowService? conversation = null)
    {
        return new VisionAgentBuildApplicationService(
            new FakeBuildExecution(),
            new VisionAgentPlanAnswerValidator(),
            new VisionAgentPlanRequirementOverlay(),
            conversation ?? new ConversationalFlowService(_tempRoot),
            Substitute.For<Microsoft.Extensions.Logging.ILogger<VisionAgentBuildApplicationService>>(),
            Options.Create(new AgentGenerateFlowOptions
            {
                Enabled = enabled,
                Mode = mode
            }));
    }

    private static object BusinessProjection(CanonicalBuildOutcome outcome)
    {
        return new
        {
            outcome.CompletionStatus,
            outcome.FailureType,
            outcome.FailureCode,
            outcome.PlanId,
            outcome.PlanHash,
            outcome.ContractVersion,
            outcome.AnswerSetFingerprint,
            outcome.RequestedMode,
            outcome.EffectiveMode,
            outcome.ToolLoopEntered,
            outcome.FallbackReason,
            Readiness = JsonSerializer.Serialize(outcome.BuildReadiness),
            Flow = JsonSerializer.Serialize(outcome.Result.Flow),
            WorkflowDiff = JsonSerializer.Serialize(outcome.WorkflowDiff),
            ApplyGate = JsonSerializer.Serialize(outcome.ApplyGate)
        };
    }

    private static AiFlowGenerationRequest BuildRequest(
        VisionAgentPlanModeResult plan,
        Func<VisionAgentBuildFromPlanRequest, VisionAgentBuildFromPlanRequest>? mutate = null)
    {
        var build = new VisionAgentBuildFromPlanRequest
        {
            PlanId = plan.PlanId,
            PlanHash = plan.PlanHash,
            PlanSnapshot = plan,
            ConfirmedAnswers = plan.ConfirmedPlanAnswers,
            AcceptedRecommendedDefaults = true,
            OperatorCatalogVersion = plan.OperatorCatalogVersion,
            StationBoundarySummary = plan.StationBoundarySummary,
            PlcOutputPolicy = plan.PlcOutputPolicy,
            BuildIntent = "new",
            OriginalUserPrompt = plan.OriginalUserPrompt
        };
        build = mutate?.Invoke(build) ?? build;
        return new AiFlowGenerationRequest("detect scratches", Mode: GenerateFlowMode.New)
        {
            UseVisionAgentGenerateFlow = true,
            AgentGenerateFlowMode = AiAgentGenerateFlowModes.Scripted,
            BuildFromPlan = build
        };
    }

    private static VisionAgentPlanModeResult BuildPlan(
        string version = VisionAgentPlanContractVersions.V2,
        bool includeHash = true)
    {
        var answers = new List<VisionAgentPlanAnswer>
        {
            Answer(VisionAgentPlanAnswerFields.InspectionObject, "metal panel"),
            Answer(VisionAgentPlanAnswerFields.TaskType, AiVisionTaskTypes.SurfaceDefect),
            Answer(VisionAgentPlanAnswerFields.ImageSource, "camera"),
            Answer(VisionAgentPlanAnswerFields.AcceptanceCriteria, "no visible scratch"),
            Answer(VisionAgentPlanAnswerFields.OutputTarget, "ok_ng")
        };
        var plan = new VisionAgentPlanModeResult
        {
            PlanContractVersion = version,
            PlanId = "plan-entry-parity",
            OriginalUserPrompt = "detect scratches on metal panel",
            Goal = "detect scratches on metal panel",
            Intent = AiVisionTaskTypes.SurfaceDefect,
            Confidence = "high",
            RequirementUnderstanding = ["metal panel", "scratch", "ok/ng"],
            ConfirmedPlanAnswers = answers,
            ResolvedPlanFields = answers.Select(answer => answer.Field).ToList(),
            RemainingPlanFields = [],
            RecommendedRoute = new VisionAgentRecommendedRoute
            {
                RouteId = "surface_defect_detection",
                Title = "Surface defect detection",
                Summary = "Detect scratches and output OK/NG.",
                Operators = ["ImageAcquisition", "SurfaceDefectDetection", "ResultOutput"],
                TemplateDecision = "free_generate"
            },
            CanBuild = true,
            BuildReadiness = new VisionAgentBuildReadinessSnapshot
            {
                CanBuild = true,
                ResolvedFields = answers.Select(answer => answer.Field).ToList(),
                ContractVersion = version
            },
            RequirementMaturity = new AiRequirementMaturityResult
            {
                Maturity = AiRequirementMaturity.Actionable,
                TaskType = AiVisionTaskTypes.SurfaceDefect,
                CanPlan = true,
                CanBuild = true,
                ObjectSignals = ["metal panel"],
                TaskSignals = ["scratch"],
                PublicReason = "ready"
            },
            OperatorCatalogVersion = "catalog-test",
            StationBoundarySummary = "metadata only",
            PlcOutputPolicy = "ok_ng",
            MetadataOnly = true
        };

        return includeHash
            ? plan with { PlanHash = VisionAgentOrchestrator.ComputePlanHash(plan) }
            : plan;
    }

    private static VisionAgentPlanAnswer Answer(string field, string value)
    {
        return new VisionAgentPlanAnswer
        {
            QuestionId = $"q_{field}",
            Field = field,
            Value = value,
            Origin = VisionAgentPlanAnswerOrigins.ExplicitUserSelection,
            Resolved = true
        };
    }

    private sealed class FakeBuildExecution : IVisionAgentOrchestrator
    {
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
            var build = request.BuildFromPlan!;
            var plan = build.PlanSnapshot!;
            var gate = new VisionAgentApplyGate
            {
                CanvasApplyReady = true,
                RuntimeDraftReady = true,
                DeploymentReady = false,
                Status = "ready",
                MetadataOnly = true
            };
            var diff = new VisionAgentWorkflowDiff
            {
                AddedNodes = ["op_camera", "op_defect", "op_output"],
                MetadataOnly = true
            };
            var buildResult = new VisionAgentBuildResult
            {
                BuildId = "build-fake",
                PlanId = build.PlanId,
                PlanHash = build.PlanHash,
                ContractVersion = plan.PlanContractVersion,
                ApplyGate = gate,
                WorkflowDiff = diff,
                ToolEvidenceTimeline =
                [
                    new VisionAgentToolEvidence
                    {
                        Stage = "fake_build",
                        ToolName = "fake_build",
                        Source = string.Equals(
                            request.AgentGenerateFlowMode,
                            AiAgentGenerateFlowModes.ToolLoop,
                            StringComparison.OrdinalIgnoreCase)
                            ? "tool_loop"
                            : "fixed_build_orchestrator",
                        Status = "completed"
                    }
                ]
            };
            return Task.FromResult(new AiFlowGenerationResult
            {
                Success = true,
                CompletionStatus = AiFlowGenerationResult.CompletionStatusCompleted,
                Flow = new OperatorFlowDto(),
                AiExplanation = "fake build completed",
                BuildResult = buildResult,
                BuildReadiness = new VisionAgentBuildReadinessSnapshot
                {
                    CanBuild = true,
                    ContractVersion = plan.PlanContractVersion,
                    ResolvedFields = plan.ResolvedPlanFields
                },
                InteractionState = AiInteractionStates.Completed,
                TurnIntent = AiTurnIntents.NewFlow,
                RouterConfidence = AiRouterConfidence.High
            });
        }
    }
}
