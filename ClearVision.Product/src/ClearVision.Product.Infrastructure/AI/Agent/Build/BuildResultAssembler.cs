using ClearVision.Product.Core.AI.Tools;
using ClearVision.Product.Core.DTOs;
using ClearVision.Product.Infrastructure.AI.AgentRun;
using ClearVision.Product.Infrastructure.AI.Tools;

namespace ClearVision.Product.Infrastructure.AI.Agent;

public sealed class BuildResultAssembler
{
    private readonly AgentRunEventRedactor _redactor;
    private readonly IAgentRunEventSink? _eventSink;
    private readonly IVisionAgentOperatorContractCatalog _contractCatalog;

    public BuildResultAssembler(
        AgentRunEventRedactor redactor,
        IAgentRunEventSink? eventSink = null)
        : this(redactor, eventSink, null)
    {
    }

    internal BuildResultAssembler(
        AgentRunEventRedactor redactor,
        IAgentRunEventSink? eventSink,
        IVisionAgentOperatorContractCatalog? contractCatalog)
    {
        _redactor = redactor;
        _eventSink = eventSink;
        _contractCatalog = contractCatalog ?? new VisionAgentOperatorContractCatalog();
    }

    internal AiFlowGenerationResult Assemble(BuildResultAssemblyInput input)
    {
        var pendingParameters = MergePendingParameters(
            input.ParameterMapping.PendingParameters,
            input.Validation,
            input.PackageReadiness);
        var missingResources = MergeMissingResources(
            input.Template,
            input.ParameterMapping.MissingResources,
            input.Validation,
            input.PackageReadiness);
        var globalVariableDrafts = BuildGlobalVariableDrafts(input);
        var globalVariableDiagnostics = BuildGlobalVariableDiagnostics(globalVariableDrafts, input);
        var result = input.CurrentDraft.GenerationResult;
        var artifact = input.CurrentDraft.Artifact;
        var returnedFlowFingerprint = WorkflowArtifactFingerprint.ComputeCanvasProjection(
            artifact.CanvasProjection,
            input.LoadPlan.PlanHash,
            artifact.CatalogVersion,
            input.Intent.BuildIntent,
            artifact.Graph,
            _contractCatalog);
        var applyGate = NormalizeApplyGate(input.ApplyGate, returnedFlowFingerprint);
        var firstFix = FirstFixRecommendation(
            applyGate,
            missingResources,
            pendingParameters);
        var planReadiness = input.LoadPlan.Plan?.BuildReadiness;
        var compilationSucceeded = VisionAgentBuildSupport.ReadCount(input.Validation.Data, "blockingIssues") == 0 &&
                                    string.Equals(
                                        artifact.ArtifactFingerprint,
                                        returnedFlowFingerprint,
                                        StringComparison.OrdinalIgnoreCase);
        result.Success = true;
        result.CompletionStatus = AiFlowGenerationResult.CompletionStatusCompleted;
        result.Flow = artifact.CanvasProjection;

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
        result.GlobalVariableDrafts = globalVariableDrafts;
        result.GlobalVariableSourceBindingDrafts = [];
        result.GlobalVariableTargetBindingDrafts = BuildGlobalVariableTargetBindingDrafts(globalVariableDrafts, input);
        result.GlobalVariableDiagnostics = globalVariableDiagnostics;
        result.GenerationMode = input.Template.GenerationMode;
        result.TemplateLockLevel = input.Template.TemplateLockLevel;
        result.DetectedIntent = input.Intent.BuildIntent;
        result.TurnIntent = ToTurnIntent(input.Intent.BuildIntent);
        result.InteractionState = AiInteractionStates.Completed;
        result.PlanId = input.LoadPlan.PlanId;
        result.PlanHash = input.LoadPlan.PlanHash;
        result.ContractVersion = input.Request.BuildFromPlan?.PlanSnapshot?.PlanContractVersion ?? VisionAgentPlanContractVersions.V2;
        result.AnswerSetFingerprint = input.LoadPlan.AnswerSetFingerprint;
        result.RequestedMode = AiAgentGenerateFlowModes.Normalize(input.Request.AgentGenerateFlowMode);
        result.EffectiveMode = result.RequestedMode;
        // Retained for wire compatibility; production BuildFromPlan has no Tool Loop branch.
        result.ToolLoopEntered = false;
        result.FallbackReason = string.Empty;
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
            ContractVersion = input.Request.BuildFromPlan?.PlanSnapshot?.PlanContractVersion ?? VisionAgentPlanContractVersions.V2,
            BuildIntent = input.Intent.BuildIntent,
            TaskType = input.LoadPlan.TaskType,
            AnswerSetFingerprint = input.LoadPlan.AnswerSetFingerprint,
            RequestedMode = AiAgentGenerateFlowModes.Normalize(input.Request.AgentGenerateFlowMode),
            EffectiveMode = AiAgentGenerateFlowModes.Normalize(input.Request.AgentGenerateFlowMode),
            ToolLoopEntered = false,
            FallbackReason = string.Empty,
            ResolvedFields = input.LoadPlan.ResolvedFields.ToList(),
            RemainingFields = input.LoadPlan.RemainingFields.ToList(),
            SelectionSource = input.Selection.SelectionSource,
            EffectiveRouteId = input.Selection.EffectiveRoute.RouteId,
            EffectiveOperators = input.Pipeline.Steps.Select(step => step.OperatorType).ToList(),
            StrategyConfirmed = input.Selection.StrategyConfirmed,
            StrategyConfirmationSource = input.Selection.StrategyConfirmationSource,
            UnresolvedStrategyBlockers = input.Selection.UnresolvedStrategyBlockers.ToList(),
            ParameterStrategy = string.IsNullOrWhiteSpace(input.ParameterMapping.ParameterStrategy)
                ? input.Selection.ParameterStrategy
                : input.ParameterMapping.ParameterStrategy,
            Flow = artifact.CanvasProjection,
            WorkflowDraft = artifact.WorkflowDraft,
            ArtifactFingerprint = artifact.ArtifactFingerprint,
            CompiledFingerprint = artifact.ArtifactFingerprint,
            ValidationFingerprint = applyGate.ValidationFingerprint,
            DryRunFingerprint = applyGate.DryRunFingerprint,
            PrecheckFingerprint = applyGate.PrecheckFingerprint,
            ReturnedFlowSemanticFingerprint = returnedFlowFingerprint,
            CatalogVersion = artifact.CatalogVersion,
            PlanSucceeded = input.LoadPlan.Plan != null &&
                            !input.LoadPlan.HashMismatch &&
                            !string.IsNullOrWhiteSpace(input.LoadPlan.PlanHash),
            CompilationSucceeded = compilationSucceeded,
            RouteSemanticsSatisfied = input.RouteAssessment.Supported && input.RouteAssessment.Satisfied,
            ArtifactDisposition = applyGate.ArtifactDisposition,
            OperatorPipeline = input.Pipeline.Steps,
            ParameterMapping = input.ParameterMapping.Mappings,
            PendingParameters = pendingParameters,
            MissingResources = missingResources,
            GlobalVariableDrafts = globalVariableDrafts,
            GlobalVariableSourceBindingDrafts = result.GlobalVariableSourceBindingDrafts,
            GlobalVariableTargetBindingDrafts = result.GlobalVariableTargetBindingDrafts,
            GlobalVariableDiagnostics = globalVariableDiagnostics,
            ValidationPreview = input.Validation.Data,
            DryRunResult = input.DryRun.Data,
            ReadinessReport = input.PackageReadiness.Data,
            StationCompatibilityReport = input.StationCompatibility.Report,
            OperatorContractReport = input.OperatorContract.Report,
            ReleaseReview = input.ReleaseReview.Report,
            WorkflowDiff = input.WorkflowDiff,
            ApplyGate = applyGate with
            {
                FirstFixRecommendation = firstFix
            },
            ToolEvidenceTimeline = input.Evidence.ToList(),
            AutoRepairs = input.AutoRepairs.ToList(),
            FirstFixRecommendation = firstFix,
            PublicWarnings = input.PublicWarnings.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            MetadataOnly = true
        };
        // BuildReadiness is owned by the formal Plan contract. Never infer it from
        // the compiled node count or from the returned canvas projection.
        result.BuildReadiness = planReadiness;
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
                routeAssessment = input.RouteAssessment,
                artifactFingerprint = result.BuildResult.ArtifactFingerprint,
                returnedFlowSemanticFingerprint = result.BuildResult.ReturnedFlowSemanticFingerprint,
                firstFixRecommendation = firstFix,
                globalVariableDraftCount = globalVariableDrafts.Count,
                globalVariableDiagnosticCount = globalVariableDiagnostics.Count,
                toolEvidenceCount = input.Evidence.Count,
                metadataOnly = true,
                redactionPass = true
            }
        });

        return result;
    }

    private static VisionAgentApplyGate NormalizeApplyGate(
        VisionAgentApplyGate gate,
        string returnedFlowFingerprint)
    {
        if (string.Equals(
                gate.ReturnedFlowSemanticFingerprint,
                returnedFlowFingerprint,
                StringComparison.OrdinalIgnoreCase))
        {
            return gate;
        }

        return gate with
        {
            CanvasApplyReady = false,
            RuntimeDraftReady = false,
            DeploymentReady = false,
            Blocked = true,
            Status = "blocked",
            ApplyBlockers = gate.ApplyBlockers
                .Concat(["returned_flow_fingerprint_recomputed_mismatch"])
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            ArtifactFingerprintConsistent = false,
            ReturnedFlowSemanticFingerprint = returnedFlowFingerprint,
            ArtifactDisposition = "blocked"
        };
    }

    internal AiFlowGenerationResult Failure(
        string buildId,
        IReadOnlyList<VisionAgentToolEvidence> evidence,
        IReadOnlyList<string> publicWarnings,
        string failureCode = "build_orchestrator_failed")
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
                Code = failureCode,
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
                    ApplyBlockers = [failureCode],
                    FirstFixRecommendation = "请查看公开工具证据，修复被阻断的元数据步骤后重试构建。"
                }
            }
        };
    }

    private static List<AiPendingParameterInfo> MergePendingParameters(
        IEnumerable<AiPendingParameterInfo> mapped,
        VisionAgentToolResult validation,
        VisionAgentToolResult packageReadiness)
    {
        // Plan defaults describe policy; only mapped or validated node parameters are actionable.
        return VisionAgentBuildSupport.DeduplicatePending(mapped
            .Concat(VisionAgentBuildSupport.ReadPendingParameters(validation.Data))
            .Concat(VisionAgentBuildSupport.ReadPendingParameters(packageReadiness.Data)));
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
                ResourceName = "模板资源",
                OperatorKey = "templatematching#1",
                OperatorType = "TemplateMatching",
                OperatorIndex = 0,
                ParameterName = "Template",
                Source = "template_strategy",
                ResolutionTarget = VisionAgentResourceResolutionTargets.TemplatePicker,
                DraftPolicy = VisionAgentResourceDraftPolicies.DraftAllowed,
                Description = "未找到用户明确选择的模板骨架，请绑定模板资源或改用算子链生成。"
            });
        }

        resources.AddRange(VisionAgentBuildSupport.ReadMissingResources(validation.Data));
        resources.AddRange(VisionAgentBuildSupport.ReadMissingResources(packageReadiness.Data));
        return VisionAgentBuildSupport.DeduplicateMissing(resources);
    }

    private static List<VisionAgentGlobalVariableDraft> BuildGlobalVariableDrafts(BuildResultAssemblyInput input)
    {
        var defaults = input.Request.BuildFromPlan?.PlanSnapshot?.RecommendedDefaults ?? [];
        var prompt = input.LoadPlan.OriginalUserPrompt ?? string.Empty;
        var acceptance = input.Request.BuildFromPlan?.PlanSnapshot?.AcceptanceCriteria ?? [];
        var candidates = defaults
            .Where(LooksLikeGlobalVariableHint)
            .Select(item => new VisionAgentGlobalVariableDraft
            {
                Name = NormalizeVariableName(item.Id),
                DisplayName = string.IsNullOrWhiteSpace(item.Label) ? item.Id : item.Label,
                ValueType = InferVariableType(item.Value),
                InitialValueSummary = SummarizeScalar(item.Value),
                Source = "plan_recommended_default",
                Rationale = string.IsNullOrWhiteSpace(item.Impact)
                    ? "Plan default indicates shared project state."
                    : item.Impact,
                ManualWriteAllowed = true,
                IncludeInResultMetadata = ShouldExposeInResultMetadata(item, prompt, acceptance),
                RequiresHumanConfirmation = true,
                MetadataOnly = true
            })
            .Where(item => !string.IsNullOrWhiteSpace(item.Name))
            .ToList();

        if (candidates.Count == 0 && MentionsGlobalVariable(prompt))
        {
            candidates.Add(new VisionAgentGlobalVariableDraft
            {
                Name = "project.shared_value",
                DisplayName = "project.shared_value",
                ValueType = "String",
                InitialValueSummary = string.Empty,
                Source = "user_request",
                Rationale = "User request mentions project/global/shared variables; exact binding still requires engineer confirmation.",
                RequiresHumanConfirmation = true,
                MetadataOnly = true
            });
        }

        return candidates
            .GroupBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
    }

    private static List<VisionAgentGlobalVariableTargetBindingDraft> BuildGlobalVariableTargetBindingDrafts(
        IReadOnlyList<VisionAgentGlobalVariableDraft> drafts,
        BuildResultAssemblyInput input)
    {
        if (drafts.Count == 0)
        {
            return [];
        }

        return input.ParameterMapping.Mappings
            .Where(mapping => mapping.Pending ||
                mapping.Source.Contains("plan", StringComparison.OrdinalIgnoreCase) ||
                mapping.Impact.Contains("shared", StringComparison.OrdinalIgnoreCase))
            .Take(12)
            .Select(mapping => new VisionAgentGlobalVariableTargetBindingDraft
            {
                VariableName = drafts[0].Name,
                OperatorHint = string.IsNullOrWhiteSpace(mapping.TempId) ? mapping.OperatorType : mapping.TempId,
                ParameterHint = mapping.ParameterName,
                Rationale = "Agent can only suggest this subscription; Studio must bind the exact operator parameter.",
                RequiresHumanConfirmation = true,
                MetadataOnly = true
            })
            .Where(item => !string.IsNullOrWhiteSpace(item.ParameterHint))
            .ToList();
    }

    private static List<VisionAgentGlobalVariableDiagnostic> BuildGlobalVariableDiagnostics(
        IReadOnlyList<VisionAgentGlobalVariableDraft> drafts,
        BuildResultAssemblyInput input)
    {
        var diagnostics = new List<VisionAgentGlobalVariableDiagnostic>();
        if (drafts.Count > 0)
        {
            diagnostics.Add(new VisionAgentGlobalVariableDiagnostic
            {
                Code = "GV_AGENT_DRAFT",
                Severity = "info",
                Message = "Global variable drafts are metadata-only suggestions and require manual confirmation before project changes.",
                VariableName = drafts[0].Name
            });
        }

        if (MentionsGlobalVariable(input.LoadPlan.OriginalUserPrompt) && drafts.Count == 0)
        {
            diagnostics.Add(new VisionAgentGlobalVariableDiagnostic
            {
                Code = "GV_AGENT_NEEDS_REVIEW",
                Severity = "warning",
                Message = "Request mentions shared project state, but no specific scalar variable draft could be inferred from the plan."
            });
        }

        return diagnostics;
    }

    private static bool LooksLikeGlobalVariableHint(VisionAgentDefaultAssumption item)
    {
        return MentionsGlobalVariable($"{item.Id} {item.Label} {item.Value} {item.Impact}");
    }

    private static bool MentionsGlobalVariable(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        return text.Contains("global", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("shared", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("variable", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("全局变量", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("共享变量", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("项目变量", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeVariableName(string value)
    {
        var normalized = new string((value ?? string.Empty)
            .Trim()
            .Select(ch => char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : '.')
            .ToArray());
        normalized = string.Join('.', normalized.Split('.', StringSplitOptions.RemoveEmptyEntries));
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return string.Empty;
        }

        return char.IsLetter(normalized[0]) ? normalized : "project." + normalized;
    }

    private static string InferVariableType(string value)
    {
        if (bool.TryParse(value, out _))
        {
            return "Boolean";
        }

        if (long.TryParse(value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out _))
        {
            return "Int64";
        }

        if (double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _))
        {
            return "Double";
        }

        return "String";
    }

    private static string SummarizeScalar(string value)
    {
        return string.IsNullOrEmpty(value)
            ? string.Empty
            : value.Length <= 80 ? value : value[..80] + "...";
    }

    private static bool ShouldExposeInResultMetadata(
        VisionAgentDefaultAssumption item,
        string prompt,
        IReadOnlyList<string> acceptance)
    {
        var text = $"{item.Id} {item.Label} {item.Value} {item.Impact} {prompt} {string.Join(' ', acceptance)}";
        return text.Contains("result", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("metadata", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("摘要", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("结果", StringComparison.OrdinalIgnoreCase);
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
