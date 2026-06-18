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
            input.Validation,
            input.PackageReadiness,
            input.Request);
        var missingResources = MergeMissingResources(
            input.Template,
            input.ParameterMapping.MissingResources,
            input.Validation,
            input.PackageReadiness);
        var firstFix = FirstFixRecommendation(
            input.ApplyGate,
            missingResources,
            pendingParameters);
        var result = input.CurrentDraft.GenerationResult;
        result.Success = input.CurrentDraft.CanvasFlow.Operators.Count > 0;
        result.CompletionStatus = result.Success
            ? AiFlowGenerationResult.CompletionStatusCompleted
            : AiFlowGenerationResult.CompletionStatusFailed;
        result.Flow ??= input.CurrentDraft.CanvasFlow;
        if (VisionAgentBuildSupport.FlowOperatorCount(result.Flow) == 0)
        {
            result.Flow = input.CurrentDraft.CanvasFlow;
        }

        result.ClarificationRequired = false;
        result.RequirementBrief = null;
        result.FailureType = null;
        result.FailureSummary = null;
        result.ErrorMessage = null;
        result.BlockingClarificationFields.Clear();
        result.NonBlockingMissingFields.Clear();
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
            AnswerSetFingerprint = input.LoadPlan.AnswerSetFingerprint,
            ResolvedFields = input.LoadPlan.ResolvedFields.ToList(),
            RemainingFields = input.LoadPlan.RemainingFields.ToList(),
            SelectionSource = input.Selection.SelectionSource,
            EffectiveRouteId = input.Selection.EffectiveRoute.RouteId,
            EffectiveOperators = input.Selection.EffectiveRoute.Operators.ToList(),
            StrategyConfirmed = input.Selection.StrategyConfirmed,
            StrategyConfirmationSource = input.Selection.StrategyConfirmationSource,
            UnresolvedStrategyBlockers = input.Selection.UnresolvedStrategyBlockers.ToList(),
            ParameterStrategy = string.IsNullOrWhiteSpace(input.ParameterMapping.ParameterStrategy)
                ? input.Selection.ParameterStrategy
                : input.ParameterMapping.ParameterStrategy,
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
        result.BuildReadiness = result.Success
            ? new VisionAgentBuildReadinessSnapshot
            {
                CanBuild = true,
                Blockers = [],
                ResolvedFields = input.LoadPlan.ResolvedFields.ToList(),
                RemainingFields = [],
                PrimaryMessage = "已基于确认计划生成可编辑草稿",
                ContractVersion = VisionAgentPlanContractVersions.V2
            }
            : null;
        result.AiExplanation = string.IsNullOrWhiteSpace(result.AiExplanation)
            ? "构建模式已基于确认计划执行仅元数据工具链，并生成可编辑流程草稿。"
            : _redactor.RedactText(result.AiExplanation);

        _eventSink?.Append(input.RunId, new AgentRunEventDraft
        {
            EventType = AgentRunEventTypes.ArtifactCreated,
            Stage = "artifact",
            Title = "构建产物已就绪",
            Summary = "可回放 BuildResult、流程差异、就绪门禁和可编辑草稿已就绪。",
            Status = AgentRunEventStatuses.Completed,
            Payload = new
            {
                buildId = input.BuildId,
                selectionSource = result.BuildResult.SelectionSource,
                effectiveRouteId = result.BuildResult.EffectiveRouteId,
                effectiveOperators = result.BuildResult.EffectiveOperators,
                strategyConfirmed = result.BuildResult.StrategyConfirmed,
                strategyConfirmationSource = result.BuildResult.StrategyConfirmationSource,
                unresolvedStrategyBlockers = result.BuildResult.UnresolvedStrategyBlockers,
                parameterStrategy = result.BuildResult.ParameterStrategy,
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
            ErrorMessage = "Vision Agent Build failed before completion.",
            FailureSummary = new AiFailureSummary
            {
                Category = "vision_agent_build_from_plan",
                Code = "build_orchestrator_failed",
                Message = "Vision Agent Build failed before completion.",
                RepairTarget = "请查看公开工具证据和后端日志，修复 BuildFromPlan 构建异常后重试。"
            },
            ClarificationRequired = false,
            RequirementBrief = null,
            BuildReadiness = null,
            InteractionState = AiInteractionStates.Failed,
            BuildResult = new VisionAgentBuildResult
            {
                BuildId = buildId,
                ToolEvidenceTimeline = evidence.ToList(),
                FirstFixRecommendation = "请查看公开工具证据，修复被阻断的元数据步骤后重试构建。",
                PublicWarnings = publicWarnings.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                ApplyGate = new VisionAgentApplyGate
                {
                    Blocked = true,
                    Status = "blocked",
                    ApplyBlockers = ["build_orchestrator_failed"],
                    FirstFixRecommendation = "请查看公开工具证据，修复被阻断的元数据步骤后重试构建。"
                }
            }
        };
    }

    private static List<AiPendingParameterInfo> MergePendingParameters(
        IEnumerable<AiPendingParameterInfo> mapped,
        VisionAgentToolResult validation,
        VisionAgentToolResult packageReadiness,
        AiFlowGenerationRequest request)
    {
        return VisionAgentBuildSupport.DeduplicatePending(mapped
            .Concat(VisionAgentBuildSupport.ReadPendingParameters(validation.Data))
            .Concat(VisionAgentBuildSupport.ReadPendingParameters(packageReadiness.Data))
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
        TemplateStrategyResolution template,
        IEnumerable<AiMissingResourceInfo> mapped,
        VisionAgentToolResult validation,
        VisionAgentToolResult packageReadiness)
    {
        var resources = mapped.ToList();
        if (template.RequiredTemplateMissing)
        {
            resources.Add(new AiMissingResourceInfo
            {
                ResourceType = "template_artifact",
                ResourceKey = string.IsNullOrWhiteSpace(template.MissingTemplateResourceKey)
                    ? "template_artifact"
                    : template.MissingTemplateResourceKey,
                Description = "未找到用户明确选择的模板骨架，请绑定模板资源或改用算子链生成。"
            });
        }

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
            return "应用草稿到画布前，请先修复流程结构阻断项。";
        }

        var firstMissing = PreferredMissingResource(missingResources);
        if (firstMissing != null)
        {
            return $"部署前请绑定缺失的{DisplayResourceType(firstMissing.ResourceType)}元数据：{firstMissing.ResourceKey}。";
        }

        var firstPending = pendingParameters.FirstOrDefault();
        if (firstPending != null)
        {
            return $"发布前请确认 {firstPending.OperatorId} 的待确认参数元数据。";
        }

        return gate.DeploymentReady
            ? "请在画布上复核草稿，准备好后再进入运行包流程。"
            : "工站部署前请复核就绪门禁并解决部署阻断项。";
    }

    private static string DisplayResourceType(string resourceType)
    {
        return resourceType switch
        {
            "model_resource" => "模型资源",
            "template_artifact" => "模板资源",
            "measurement_parameter" => "测量参数",
            "camera_binding" => "相机绑定",
            "output_channel" => "输出通道",
            "plc_address" => "PLC 地址",
            _ => string.IsNullOrWhiteSpace(resourceType) ? "资源" : resourceType
        };
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
                     "output_channel",
                     "plc_address"
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
