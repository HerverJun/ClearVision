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

namespace ClearVision.Product.Tests.AI.VisionAgentBuildOrchestratorTests;

public sealed class VisionAgentBuildOrchestratorTests
{
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
        result.BuildResult.ApplyGate.DeploymentReady.Should().BeFalse();
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
        return new VisionAgentBuildOrchestrator(
            new VisionAgentToolRegistry(
            [
                new FlowTemplateMatchTool(),
                new FlowTemplateSkeletonTool(),
                new FlowValidationTool(),
                new DryRunFlowTool(),
                new RuntimePackagePrecheckTool()
            ]),
            new FakeAiFlowGenerationService(),
            new AgentRunEventRedactor(),
            NullLogger<VisionAgentBuildOrchestrator>.Instance,
            sink);
    }

    private static AiFlowGenerationRequest Request(
        VisionAgentPlanModeResult plan,
        string buildIntent = "new",
        string? currentFlowSnapshot = null)
    {
        return new AiFlowGenerationRequest(plan.OriginalUserPrompt, Mode: GenerateFlowModeExtensions.ParseOrAuto(buildIntent))
        {
            AgentRunId = "ar_build_test",
            UseVisionAgentGenerateFlow = true,
            BuildFromPlan = new VisionAgentBuildFromPlanRequest
            {
                PlanId = plan.PlanId,
                PlanHash = plan.PlanHash,
                PlanSnapshot = plan,
                UserSelections = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["defect_morphology"] = "scratch"
                },
                AcceptedDefaults = ["metadata_only"],
                CurrentFlowSnapshot = currentFlowSnapshot,
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

    private static VisionAgentPlanModeResult Plan(string intent, List<string> operators)
    {
        var result = new VisionAgentPlanModeResult
        {
            PlanId = "plan_build_test",
            OriginalUserPrompt = "metal surface scratch detection",
            Goal = "metal surface scratch detection",
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
