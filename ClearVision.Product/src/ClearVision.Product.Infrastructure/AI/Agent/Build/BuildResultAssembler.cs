using ClearVision.Product.Core.AI.Tools;
using ClearVision.Product.Core.DTOs;
using ClearVision.Product.Infrastructure.AI.AgentRun;

namespace ClearVision.Product.Infrastructure.AI.Agent;

public sealed class BuildResultAssembler
{
    private readonly AgentRunEventRedactor _redactor;
    private readonly IAgentRunEventSink? _eventSink;

    public BuildResultAssembler(
        AgentRunEventRedactor redactor,
        IAgentRunEventSink? eventSink = null)
    {
        _redactor = redactor;
        _eventSink = eventSink;
    }

    internal AiFlowGenerationResult Assemble(BuildResultAssemblyInput input)
    {
        var pendingParameters = MergePendingParameters(
            input.ParameterMapping.PendingParameters,
            input.Request);
        var missingResources = MergeMissingResources(
            input.ParameterMapping.MissingResources,
            input.Validation,
            input.PackageReadiness);
        var firstFix = FirstFixRecommendation(
            input.ApplyGate,
            missingResources,
            pendingParameters);
        var result = input.CurrentDraft.GenerationResult;
        result.Success = result.Success || input.CurrentDraft.CanvasFlow.Operators.Count > 0;
        result.CompletionStatus = result.Success
            ? AiFlowGenerationResult.CompletionStatusCompleted
            : AiFlowGenerationResult.CompletionStatusFailed;
        result.Flow ??= input.CurrentDraft.CanvasFlow;
        if (VisionAgentBuildSupport.FlowOperatorCount(result.Flow) == 0)
        {
            result.Flow = input.CurrentDraft.CanvasFlow;
        }

        result.ValidationPreview = input.Validation.Data ?? result.ValidationPreview;
        result.DryRunResult = input.DryRun.Data ?? result.DryRunResult;
        result.PendingParameters = pendingParameters;
        result.MissingResources = missingResources;
        result.GenerationMode = input.Template.GenerationMode;
        result.TemplateLockLevel = input.Template.TemplateLockLevel;
        result.DetectedIntent = input.Intent.BuildIntent;
        result.TurnIntent = ToTurnIntent(input.Intent.BuildIntent);
        result.InteractionState = AiInteractionStates.Completed;
        result.ToolTrace.AddRange(input.Evidence.Select(item => (object)item));
        result.StageTimeline.AddRange(input.Evidence.Select(item => new AiGenerationStageDiagnostic
        {
            Stage = item.Stage,
            Status = item.Status,
            Summary = item.OutputSummary,
            DurationMs = item.DurationMs,
            Metadata = new Dictionary<string, string>
            {
                ["toolName"] = item.ToolName,
                ["evidenceId"] = item.EvidenceId,
                ["warningCode"] = item.WarningCode,
                ["applyImpact"] = item.ApplyImpact,
                ["deploymentImpact"] = item.DeploymentImpact
            }
        }));
        result.BuildResult = new VisionAgentBuildResult
        {
            BuildId = input.BuildId,
            PlanId = input.LoadPlan.PlanId,
            PlanHash = input.LoadPlan.PlanHash,
            BuildIntent = input.Intent.BuildIntent,
            WorkflowDraft = input.CurrentDraft.WorkflowDraft,
            OperatorPipeline = input.Pipeline.Steps,
            ParameterMapping = input.ParameterMapping.Mappings,
            PendingParameters = pendingParameters,
            MissingResources = missingResources,
            ValidationPreview = input.Validation.Data,
            DryRunResult = input.DryRun.Data,
            ReadinessReport = input.PackageReadiness.Data,
            StationCompatibilityReport = input.StationCompatibility.Report,
            OperatorContractReport = input.OperatorContract.Report,
            ReleaseReview = input.ReleaseReview.Report,
            WorkflowDiff = input.WorkflowDiff,
            ApplyGate = input.ApplyGate with
            {
                FirstFixRecommendation = firstFix
            },
            ToolEvidenceTimeline = input.Evidence.ToList(),
            AutoRepairs = input.AutoRepairs.ToList(),
            FirstFixRecommendation = firstFix,
            PublicWarnings = input.PublicWarnings.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            MetadataOnly = true
        };
        result.AiExplanation = string.IsNullOrWhiteSpace(result.AiExplanation)
            ? "Build Mode executed a metadata-only tool loop under the confirmed Plan and produced an editable workflow draft."
            : _redactor.RedactText(result.AiExplanation);

        _eventSink?.Append(input.RunId, new AgentRunEventDraft
        {
            EventType = AgentRunEventTypes.ArtifactCreated,
            Stage = "artifact",
            Title = "Build artifact ready",
            Summary = "Replay-safe BuildResult, workflow diff, readiness gates, and editable draft are ready.",
            Status = AgentRunEventStatuses.Completed,
            Payload = new
            {
                buildId = input.BuildId,
                workflowDiff = result.BuildResult.WorkflowDiff,
                applyGate = result.BuildResult.ApplyGate,
                firstFixRecommendation = firstFix,
                toolEvidenceCount = input.Evidence.Count,
                metadataOnly = true,
                redactionPass = true
            }
        });

        return result;
    }

    internal AiFlowGenerationResult Failure(
        string buildId,
        IReadOnlyList<VisionAgentToolEvidence> evidence,
        IReadOnlyList<string> publicWarnings)
    {
        return new AiFlowGenerationResult
        {
            Success = false,
            CompletionStatus = AiFlowGenerationResult.CompletionStatusFailed,
            FailureType = AiFlowGenerationResult.FailureTypeSystemError,
            ErrorMessage = "Vision Agent Build Mode failed while executing the metadata-only tool loop.",
            BuildResult = new VisionAgentBuildResult
            {
                BuildId = buildId,
                ToolEvidenceTimeline = evidence.ToList(),
                FirstFixRecommendation = "Review public tool evidence and retry Build after fixing the blocked metadata step.",
                PublicWarnings = publicWarnings.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                ApplyGate = new VisionAgentApplyGate
                {
                    Blocked = true,
                    Status = "blocked",
                    ApplyBlockers = ["build_orchestrator_failed"],
                    FirstFixRecommendation = "Review public tool evidence and retry Build after fixing the blocked metadata step."
                }
            }
        };
    }

    private static List<AiPendingParameterInfo> MergePendingParameters(
        IEnumerable<AiPendingParameterInfo> mapped,
        AiFlowGenerationRequest request)
    {
        return VisionAgentBuildSupport.DeduplicatePending(mapped
            .Concat(request.BuildFromPlan?.PlanSnapshot?.RecommendedDefaults
                .Where(item => item.Value.Contains("pending", StringComparison.OrdinalIgnoreCase))
                .Select(item => new AiPendingParameterInfo
                {
                    OperatorId = "plan_default",
                    ActualOperatorId = "plan_default",
                    ParameterNames = [item.Id]
                }) ?? []));
    }

    private static List<AiMissingResourceInfo> MergeMissingResources(
        IEnumerable<AiMissingResourceInfo> mapped,
        VisionAgentToolResult validation,
        VisionAgentToolResult packageReadiness)
    {
        var resources = mapped.ToList();
        resources.AddRange(VisionAgentBuildSupport.ReadMissingResources(validation.Data));
        resources.AddRange(VisionAgentBuildSupport.ReadMissingResources(packageReadiness.Data));
        return VisionAgentBuildSupport.DeduplicateMissing(resources);
    }

    private static string FirstFixRecommendation(
        VisionAgentApplyGate gate,
        IReadOnlyList<AiMissingResourceInfo> missingResources,
        IReadOnlyList<AiPendingParameterInfo> pendingParameters)
    {
        if (gate.Blocked)
        {
            return "Fix workflow structure blockers before applying the draft to the canvas.";
        }

        var firstMissing = PreferredMissingResource(missingResources);
        if (firstMissing != null)
        {
            return $"Bind missing {firstMissing.ResourceType} metadata for {firstMissing.ResourceKey} before deployment.";
        }

        var firstPending = pendingParameters.FirstOrDefault();
        if (firstPending != null)
        {
            return $"Confirm pending parameter metadata on {firstPending.OperatorId} before release.";
        }

        return gate.DeploymentReady
            ? "Review the draft on canvas, then proceed to runtime packaging when ready."
            : "Review readiness gates and resolve deployment blockers before Station deployment.";
    }

    private static AiMissingResourceInfo? PreferredMissingResource(IReadOnlyList<AiMissingResourceInfo> missingResources)
    {
        if (missingResources.Count == 0)
        {
            return null;
        }

        foreach (var preferredKind in new[]
                 {
                     "model_resource",
                     "template_artifact",
                     "measurement_parameter",
                     "camera_binding",
                     "output_channel"
                 })
        {
            var match = missingResources.FirstOrDefault(item =>
                string.Equals(item.ResourceType, preferredKind, StringComparison.OrdinalIgnoreCase));
            if (match != null)
            {
                return match;
            }
        }

        return missingResources.FirstOrDefault();
    }

    private static string ToTurnIntent(string buildIntent)
    {
        return buildIntent switch
        {
            "modify" or "refactor" => AiTurnIntents.ModifyFlow,
            "explain" => AiTurnIntents.ExplainFlow,
            "review_pending_parameters" => AiTurnIntents.ReviewPendingParameters,
            _ => AiTurnIntents.NewFlow
        };
    }
}
