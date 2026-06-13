using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ClearVision.Product.Core.AI.Tools;
using ClearVision.Product.Core.DTOs;
using ClearVision.Product.Core.Services;
using ClearVision.Product.Infrastructure.AI.AgentRun;
using Microsoft.Extensions.Options;

namespace ClearVision.Product.Infrastructure.AI.Agent;

public interface IVisionAgentOrchestrator
{
    Task<VisionAgentPlanModeResult> CreatePlanAsync(
        VisionAgentPlanModeRequest request,
        CancellationToken cancellationToken);

    Task<AiFlowGenerationResult> BuildFromPlanAsync(
        AiFlowGenerationRequest request,
        CancellationToken cancellationToken);
}

public sealed class VisionAgentOrchestrator : IVisionAgentOrchestrator
{
    private static readonly Regex UnsafeTemplateMetadataRegex = new(
        @"(?i)([A-Za-z]:\\|data:image\/|sk-[A-Za-z0-9_\-]{12,}|api[_-]?key\s*[:=]|token\s*[:=]|secret\s*[:=]|\b(?:\d{1,3}\.){3}\d{1,3}\b|\bDB\d+\.DB[XBWD]\d+\b|\bM\d+(?:\.\d+)?\b|\bD\d+\b|plc:\/\/)",
        RegexOptions.Compiled);

    private static readonly JsonSerializerOptions PlanHashJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private readonly IVisionAgentToolRegistry _toolRegistry;
    private readonly IAiFlowGenerationService _generationService;
    private readonly IVisionAgentPlanPlannerService? _planPlannerService;
    private readonly IVisionAgentSemanticExtractorService? _semanticExtractor;
    private readonly IVisionAgentBuildOrchestrator? _buildOrchestrator;
    private readonly IAgentRunEventSink? _eventSink;
    private readonly VisionAgentLoop? _toolLoop;
    private readonly IVisionAgentLoopCompletionSource? _toolLoopCompletionSource;
    private readonly AgentGenerateFlowOptions _agentOptions;
    private readonly VisionAgentLoopOptions _loopOptions;
    private readonly AgentRunEventRedactor _redactor;

    public VisionAgentOrchestrator(
        IVisionAgentToolRegistry toolRegistry,
        IAiFlowGenerationService generationService,
        IAgentRunEventSink? eventSink = null,
        IVisionAgentBuildOrchestrator? buildOrchestrator = null,
        IVisionAgentPlanPlannerService? planPlannerService = null,
        IVisionAgentSemanticExtractorService? semanticExtractor = null,
        VisionAgentLoop? toolLoop = null,
        IVisionAgentLoopCompletionSource? toolLoopCompletionSource = null,
        IOptions<AgentGenerateFlowOptions>? agentOptions = null,
        IOptions<VisionAgentLoopOptions>? loopOptions = null,
        AgentRunEventRedactor? redactor = null)
    {
        _toolRegistry = toolRegistry;
        _generationService = generationService;
        _planPlannerService = planPlannerService;
        _semanticExtractor = semanticExtractor;
        _buildOrchestrator = buildOrchestrator;
        _eventSink = eventSink;
        _toolLoop = toolLoop;
        _toolLoopCompletionSource = toolLoopCompletionSource;
        _agentOptions = agentOptions?.Value ?? new AgentGenerateFlowOptions();
        _agentOptions.Mode = AiAgentGenerateFlowModes.Normalize(_agentOptions.Mode);
        _loopOptions = loopOptions?.Value ?? new VisionAgentLoopOptions();
        _loopOptions.Normalize();
        _redactor = redactor ?? new AgentRunEventRedactor();
    }

    public async Task<VisionAgentPlanModeResult> CreatePlanAsync(
        VisionAgentPlanModeRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var semanticEvents = new List<VisionAgentPlanPublicEvent>();
        var semantic = VisionAgentSemanticExtractionSafety.Sanitize(request.SemanticExtraction);
        if (semantic == null && _semanticExtractor != null)
        {
            semanticEvents.Add(BuildSemanticStartedEvent());
            semantic = await _semanticExtractor.ExtractAsync(
                ToSemanticRequest(request),
                cancellationToken);
            semanticEvents.AddRange(BuildSemanticResultEvents(semantic));
        }

        var planRequest = semantic == null
            ? request
            : request with { SemanticExtraction = semantic };
        var ruleBaseline = BuildPlan(planRequest, semanticEvents);
        if (_planPlannerService == null)
        {
            return BuildRuleFallbackPlan(
                ruleBaseline,
                "planner_service_not_registered",
                "Planner 服务未注册，已使用规则兜底方案。");
        }

        return await _planPlannerService.CreatePlanAsync(
            planRequest,
            ruleBaseline,
            cancellationToken);
    }

    private static VisionAgentSemanticExtractionRequest ToSemanticRequest(VisionAgentPlanModeRequest request)
    {
        return new VisionAgentSemanticExtractionRequest
        {
            Description = request.Description,
            OriginalUserPrompt = request.OriginalUserPrompt,
            AdditionalContext = request.AdditionalContext,
            SessionId = request.SessionId,
            Mode = request.Mode,
            HasCurrentFlow = !string.IsNullOrWhiteSpace(request.CurrentFlowSnapshot),
            HasPendingPlan = false,
            TemplateSelection = request.TemplateSelection,
            AttachmentSummary = request.AttachmentSummary,
            HistorySummary = request.HistorySummary,
            CurrentFlowSummary = SummarizePublicJson(request.CurrentFlowSnapshot),
            MetadataOnly = true
        };
    }

    private static VisionAgentPlanPublicEvent BuildSemanticStartedEvent()
    {
        return new VisionAgentPlanPublicEvent
        {
            Stage = "semantic_extraction",
            Status = AgentRunEventStatuses.Running,
            Title = "语义抽取已开始",
            Summary = "正在抽取视觉需求语义槽位。",
            Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["metadataOnly"] = "true"
            },
            MetadataOnly = true
        };
    }

    private static IReadOnlyList<VisionAgentPlanPublicEvent> BuildSemanticResultEvents(
        VisionAgentSemanticExtractionResult semantic)
    {
        if (string.Equals(semantic.Source, VisionAgentSemanticSources.Model, StringComparison.OrdinalIgnoreCase))
        {
            return
            [
                new VisionAgentPlanPublicEvent
                {
                    Stage = "semantic_extraction",
                    Status = AgentRunEventStatuses.Completed,
                    Title = "语义抽取完成",
                    Summary = "语义理解来自模型，已生成公开结构化摘要。",
                    Metadata = BuildSemanticMetadata(semantic),
                    MetadataOnly = true
                }
            ];
        }

        return
        [
            new VisionAgentPlanPublicEvent
            {
                Stage = "semantic_extraction",
                Status = AgentRunEventStatuses.Failed,
                Title = "语义抽取失败",
                Summary = "语义抽取模型不可用，当前为规则降级解析。",
                Metadata = BuildSemanticMetadata(semantic),
                MetadataOnly = true
            },
            new VisionAgentPlanPublicEvent
            {
                Stage = "semantic_fallback_used",
                Status = AgentRunEventStatuses.Warning,
                Title = "已启用语义规则降级",
                Summary = "语义抽取模型不可用，当前为规则降级解析。",
                Metadata = BuildSemanticMetadata(semantic),
                MetadataOnly = true
            }
        ];
    }

    private static Dictionary<string, string> BuildSemanticMetadata(
        VisionAgentSemanticExtractionResult semantic)
    {
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["semanticSource"] = Clean(semantic.Source),
            ["taskType"] = Clean(semantic.TaskType),
            ["inspectionObject"] = Clean(semantic.InspectionObject),
            ["targetAttribute"] = Clean(semantic.TargetAttribute),
            ["okCondition"] = Clean(semantic.OkCondition),
            ["ngCondition"] = Clean(semantic.NgCondition),
            ["imageSource"] = Clean(semantic.ImageSource),
            ["failureCode"] = Clean(semantic.FailureCode),
            ["sanitizedErrorMessage"] = Clean(semantic.SanitizedErrorMessage),
            ["missingFields"] = string.Join(",", semantic.MissingFields.Select(Clean).Where(value => !string.IsNullOrWhiteSpace(value)).Take(8)),
            ["metadataOnly"] = "true"
        };
        return metadata
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Value))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<string> BuildSemanticUnderstanding(
        VisionAgentSemanticExtractionResult? semantic)
    {
        if (semantic == null)
        {
            return [];
        }

        var lines = new List<string>
        {
            string.Equals(semantic.Source, VisionAgentSemanticSources.Model, StringComparison.OrdinalIgnoreCase)
                ? "semanticSource=model"
                : "semanticSource=rule_fallback"
        };
        AddSemanticLine(lines, "semantic.taskType", semantic.TaskType);
        AddSemanticLine(lines, "semantic.inspectionObject", semantic.InspectionObject);
        AddSemanticLine(lines, "semantic.targetAttribute", semantic.TargetAttribute);
        AddSemanticLine(lines, "semantic.okCondition", semantic.OkCondition);
        AddSemanticLine(lines, "semantic.ngCondition", semantic.NgCondition);
        AddSemanticLine(lines, "semantic.imageSource", semantic.ImageSource);
        AddSemanticLine(lines, "semantic.suggestedRoute", semantic.SuggestedRoute);
        if (!string.IsNullOrWhiteSpace(semantic.FailureCode))
        {
            AddSemanticLine(lines, "semantic.failureCode", semantic.FailureCode);
        }

        return lines;
    }

    private static void AddSemanticLine(List<string> lines, string key, string? value)
    {
        var text = Clean(value);
        if (!string.IsNullOrWhiteSpace(text))
        {
            lines.Add($"{key}={text}");
        }
    }

    private static string SummarizePublicJson(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        try
        {
            using var document = JsonDocument.Parse(text);
            return Truncate(document.RootElement.GetRawText(), 1_000);
        }
        catch (JsonException)
        {
            return Truncate(text, 1_000);
        }
    }

    public async Task<AiFlowGenerationResult> BuildFromPlanAsync(
        AiFlowGenerationRequest request,
        CancellationToken cancellationToken)
    {
        var maturityGate = EnforceBuildMaturityGate(request);
        if (maturityGate != null)
        {
            return maturityGate;
        }

        if (ShouldUseToolLoop(request))
        {
            return await BuildFromPlanWithToolLoopAsync(request, cancellationToken);
        }

        return await BuildFromPlanStableAsync(request, cancellationToken);
    }

    private AiFlowGenerationResult? EnforceBuildMaturityGate(AiFlowGenerationRequest request)
    {
        var build = request.BuildFromPlan;
        var plan = build?.PlanSnapshot;
        if (plan?.CanBuild == true &&
            plan.RequirementMaturity == null &&
            build?.RequirementMaturity == null)
        {
            return null;
        }

        var maturity = plan?.RequirementMaturity ??
                       build?.RequirementMaturity ??
                       VisionAgentRequirementMaturityGate.Evaluate(
                           new VisionAgentRequirementMaturityRequest
                           {
                               Description = build?.OriginalUserPrompt ?? request.Description,
                               AdditionalContext = request.AdditionalContext,
                               Mode = build?.BuildIntent ?? request.Mode.ToWireValue(),
                               HasCurrentFlow = !string.IsNullOrWhiteSpace(request.ExistingFlowJson) ||
                                                !string.IsNullOrWhiteSpace(build?.CurrentFlowSnapshot),
                               TemplateSelection = build?.TemplateSelection ?? request.TemplateSelection
                           },
                           plan?.SemanticExtraction);

        var blocked = plan?.CanBuild == false ||
                      build?.RequirementMaturity?.CanBuild == false ||
                      !maturity.CanBuild ||
                      maturity.Maturity is AiRequirementMaturity.AbstractGoal or AiRequirementMaturity.Ambiguous or AiRequirementMaturity.ChatOrHelp;
        if (!blocked)
        {
            return null;
        }

        var trace = VisionAgentRequirementMaturityGate.BuildDecisionTrace(
            new VisionAgentRequirementMaturityRequest
            {
                Description = build?.OriginalUserPrompt ?? request.Description,
                AdditionalContext = request.AdditionalContext,
                Mode = build?.BuildIntent ?? request.Mode.ToWireValue(),
                HasCurrentFlow = !string.IsNullOrWhiteSpace(request.ExistingFlowJson) ||
                                 !string.IsNullOrWhiteSpace(build?.CurrentFlowSnapshot),
                TemplateSelection = build?.TemplateSelection ?? request.TemplateSelection
            },
            maturity,
            "build_blocked",
            "clarifying",
            "maturity_gate_blocked");
        _eventSink?.StageCompleted(
            request.AgentRunId,
            "requirement_maturity_gate",
            "Build blocked by requirement maturity gate",
            maturity.PublicReason,
            new
            {
                maturity = maturity.Maturity,
                taskType = maturity.TaskType,
                canBuild = maturity.CanBuild,
                missingFields = maturity.MissingFields,
                blockingReasons = maturity.BlockingReasons,
                metadataOnly = true
            });
        return BuildMaturityGateFailure(request, maturity, trace);
    }

    private static AiFlowGenerationResult BuildMaturityGateFailure(
        AiFlowGenerationRequest request,
        AiRequirementMaturityResult maturity,
        AiDecisionTrace trace)
    {
        var fields = maturity.MissingFields.Count > 0
            ? maturity.MissingFields
            : ["inspection_object", "task_type", "image_source", "acceptance_criteria"];
        return new AiFlowGenerationResult
        {
            Success = false,
            CompletionStatus = AiFlowGenerationResult.CompletionStatusClarificationRequired,
            FailureType = AiFlowGenerationResult.FailureTypeClarificationRequired,
            ClarificationRequired = true,
            ErrorMessage = maturity.PublicReason,
            AiExplanation = maturity.PublicReason,
            FailureSummary = new AiFailureSummary
            {
                Category = "requirement_maturity",
                Code = "maturity_gate_blocked",
                Message = maturity.PublicReason,
                RepairTarget = "请补充检测对象、任务类型、图像来源、判定标准和输出目标后再构建。"
            },
            RequirementBrief = new AiRequirementBrief
            {
                IntentType = maturity.Maturity,
                RequirementMode = request.RequirementMode,
                Confidence = maturity.CanBuild ? 0.75 : 0.25,
                HasOpenQuestions = true,
                ClarificationRequired = true,
                CanGenerateDraftNow = false,
                DraftRiskLevel = "high",
                MissingFacts = fields.ToList(),
                RequiredFields = fields.ToList(),
                BlockingClarificationFields = fields.ToList(),
                NonBlockingMissingFields = maturity.MissingFields.Except(fields, StringComparer.OrdinalIgnoreCase).ToList(),
                ClarificationQuestions = fields.Select(field => new AiClarificationQuestion
                {
                    Field = field,
                    Question = field switch
                    {
                        "inspection_object" => "请说明要检测的产品或部件对象。",
                        "task_type" => "请说明任务类型：缺陷、测量、线序、OCR/读码、有无/漏装或分类。",
                        "image_source" => "请说明图像来源是相机、图片文件还是先只做元数据规划。",
                        "acceptance_criteria" => "请说明 OK/NG 判定标准或输出目标。",
                        _ => "请补充该字段后再构建。"
                    },
                    Required = true,
                    Reason = "Build 前硬门禁要求需求成熟度达到可构建。",
                    Priority = "high",
                    Options = []
                }).ToList()
            },
            BlockingClarificationFields = fields.ToList(),
            NonBlockingMissingFields = maturity.MissingFields.Except(fields, StringComparer.OrdinalIgnoreCase).ToList(),
            RequirementMaturity = maturity,
            DecisionTrace = trace,
            TurnIntent = AiTurnIntents.NewFlow,
            InteractionState = AiInteractionStates.Clarifying,
            RouterConfidence = AiRouterConfidence.High
        };
    }

    private async Task<AiFlowGenerationResult> BuildFromPlanStableAsync(
        AiFlowGenerationRequest request,
        CancellationToken cancellationToken)
    {
        if (_buildOrchestrator != null)
        {
            return await _buildOrchestrator.BuildAsync(request, cancellationToken);
        }

        EmitBuildPreparationEvents(request);
        return await _generationService.GenerateFlowAsync(request, cancellationToken: cancellationToken);
    }

    private async Task<AiFlowGenerationResult> BuildFromPlanWithToolLoopAsync(
        AiFlowGenerationRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_toolLoop == null || _toolLoopCompletionSource == null)
        {
            EmitToolLoopFallback(
                request.AgentRunId,
                "completion_source_missing",
                "Tool Loop completion source is not registered; using stable BuildOrchestrator.");
            var stable = await BuildFromPlanStableAsync(request, cancellationToken);
            MarkFixedEvidenceSource(stable, "fallback_build_orchestrator");
            AppendFallbackMarker(stable, "completion_source_missing");
            return stable;
        }

        var toolContext = BuildToolLoopContext(request);
        var loopResult = await _toolLoop.RunAsync(new VisionAgentLoopRequest
        {
            UserPrompt = BuildToolLoopUserPrompt(request),
            ToolContext = toolContext,
            EmitPublicEvents = true,
            RequireFinalDraft = true,
            CompleteAsync = (messages, ct) => _toolLoopCompletionSource.CompleteAsync(
                new VisionAgentLoopCompletionRequest
                {
                    GenerationRequest = request,
                    Messages = messages
                },
                ct)
        }, cancellationToken);

        var fallbackReason = ResolveToolLoopFallbackReason(loopResult);
        var draftValidationEmitted = false;
        if (string.IsNullOrWhiteSpace(fallbackReason))
        {
            var draftValidation = await ValidateToolLoopFinalDraftAsync(
                request,
                toolContext,
                loopResult,
                cancellationToken);
            draftValidationEmitted = true;
            if (!draftValidation.Accepted)
            {
                fallbackReason = draftValidation.FallbackReason;
            }
        }
        else if (ShouldEmitDraftRejected(loopResult, fallbackReason))
        {
            EmitToolLoopDraftRejected(
                request.AgentRunId,
                fallbackReason,
                loopResult.ErrorMessage ?? "Tool Loop final draft was rejected before stable fallback.");
            draftValidationEmitted = true;
        }

        if (!string.IsNullOrWhiteSpace(fallbackReason))
        {
            if (!draftValidationEmitted && loopResult.Success)
            {
                EmitToolLoopDraftRejected(
                    request.AgentRunId,
                    fallbackReason,
                    "Tool Loop final draft did not satisfy the public draft acceptance contract.");
            }
            EmitToolLoopFallback(
                request.AgentRunId,
                fallbackReason,
                "Experimental Tool Loop could not safely produce a complete Build payload; using stable BuildOrchestrator.");
            var fallback = await BuildFromPlanStableAsync(request, cancellationToken);
            MarkFixedEvidenceSource(fallback, "fallback_build_orchestrator");
            MergeToolLoopEvidence(fallback, loopResult, fallbackReason);
            return fallback;
        }

        var result = await BuildFromPlanStableAsync(request, cancellationToken);
        MarkFixedEvidenceSource(result, "fixed_build_orchestrator");
        MergeToolLoopEvidence(result, loopResult, fallbackReason: null);
        return result;
    }

    private bool ShouldUseToolLoop(AiFlowGenerationRequest request)
    {
        var requestMode = AiAgentGenerateFlowModes.Normalize(request.AgentGenerateFlowMode);
        if (string.Equals(requestMode, AiAgentGenerateFlowModes.ToolLoop, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return request.UseVisionAgentGenerateFlow &&
               _agentOptions.Enabled &&
               string.Equals(_agentOptions.Mode, AiAgentGenerateFlowModes.ToolLoop, StringComparison.OrdinalIgnoreCase);
    }

    private VisionAgentToolContext BuildToolLoopContext(AiFlowGenerationRequest request)
    {
        var permissions = new HashSet<VisionAgentToolPermission>
        {
            VisionAgentToolPermission.ReadOnly,
            VisionAgentToolPermission.Simulation
        };
        if (RuntimePreviewPermissionGate.HasConsent(request))
        {
            permissions.Add(VisionAgentToolPermission.RuntimePreview);
        }

        return new VisionAgentToolContext
        {
            UserDescription = request.BuildFromPlan?.OriginalUserPrompt ?? request.Description,
            AdditionalContext = request.AdditionalContext,
            SessionId = request.SessionId,
            AgentRunId = request.AgentRunId,
            ExistingFlowJson = VisionAgentBuildSupport.FirstNonEmpty(
                request.BuildFromPlan?.CurrentFlowSnapshot,
                request.ExistingFlowJson),
            DebugTrace = false,
            MaxToolResultChars = _loopOptions.MaxToolResultChars,
            RuntimePreviewConsent = RuntimePreviewPermissionGate.HasConsent(request),
            AllowedPermissions = permissions
        };
    }

    private static string BuildToolLoopUserPrompt(AiFlowGenerationRequest request)
    {
        var build = request.BuildFromPlan;
        var plan = build?.PlanSnapshot;
        var routeOperators = plan?.RecommendedRoute.Operators ?? [];
        return string.Join(Environment.NewLine,
        [
            "Build an experimental metadata-only workflow draft plan using JSON tool_call protocol.",
            $"userGoal={Clean(build?.OriginalUserPrompt ?? request.Description)}",
            $"buildIntent={Clean(build?.BuildIntent ?? request.Mode.ToWireValue())}",
            $"planIntent={Clean(plan?.Intent)}",
            $"recommendedOperators={string.Join(",", routeOperators.Select(Clean))}",
            $"templateSelectionMode={Clean(build?.TemplateSelection?.Mode ?? request.TemplateSelection?.Mode)}",
            $"templateId={Clean(build?.TemplateSelection?.TemplateId ?? request.TemplateSelection?.TemplateId)}",
            $"hasCurrentFlowSnapshot={(!string.IsNullOrWhiteSpace(build?.CurrentFlowSnapshot) || !string.IsNullOrWhiteSpace(request.ExistingFlowJson)).ToString().ToLowerInvariant()}",
            $"runtimePreviewConsent={RuntimePreviewPermissionGate.HasConsent(request).ToString().ToLowerInvariant()}",
            "Default permissions are ReadOnly and Simulation only. Do not call ConfigWrite or DeploymentPrepare.",
            "Return final JSON only after validating the operator and parameter metadata that is available."
        ]);
    }

    private static string? ResolveToolLoopFallbackReason(VisionAgentLoopResult result)
    {
        if (!result.Success)
        {
            return string.IsNullOrWhiteSpace(result.FailureType)
                ? "tool_loop_failed"
                : result.FailureType;
        }

        var denied = result.ToolTrace.FirstOrDefault(trace =>
            !trace.Success &&
            (string.Equals(trace.ErrorCode, "unknown_tool", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(trace.ErrorCode, "tool_permission_denied", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(trace.ErrorCode, RuntimePreviewPermissionGate.ConsentRequiredErrorCode, StringComparison.OrdinalIgnoreCase)));
        if (denied != null)
        {
            return string.IsNullOrWhiteSpace(denied.ErrorCode)
                ? "tool_permission_denied"
                : denied.ErrorCode;
        }

        return HasWorkflowDraftFinal(result.FinalContent)
            ? null
            : "partial_final_requires_stable_completion";
    }

    private static bool HasWorkflowDraftFinal(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(content);
            return doc.RootElement.ValueKind == JsonValueKind.Object &&
                   ((doc.RootElement.TryGetProperty("workflowDraft", out var draft) &&
                     draft.ValueKind == JsonValueKind.Object) ||
                    (doc.RootElement.TryGetProperty("draftEdits", out var edits) &&
                     edits.ValueKind == JsonValueKind.Array));
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private async Task<ToolLoopDraftValidationResult> ValidateToolLoopFinalDraftAsync(
        AiFlowGenerationRequest request,
        VisionAgentToolContext toolContext,
        VisionAgentLoopResult loopResult,
        CancellationToken cancellationToken)
    {
        if (!TryReadToolLoopFinalDraft(loopResult.FinalContent, out var flow, out var failureReason))
        {
            EmitToolLoopDraftRejected(
                request.AgentRunId,
                failureReason,
                "Tool Loop final 未包含可验收的 workflowDraft 或 draftEdits。");
            return ToolLoopDraftValidationResult.Rejected(failureReason);
        }

        if (!_redactor.IsRedactionSafe(flow))
        {
            EmitToolLoopDraftRejected(
                request.AgentRunId,
                "unsafe_final_payload",
                "Tool Loop final 草稿包含不允许公开的敏感元数据。");
            return ToolLoopDraftValidationResult.Rejected("unsafe_final_payload");
        }

        var validation = await RunToolLoopDraftCheckAsync(
            "validate_flow",
            toolContext,
            new
            {
                flow,
                entryOperatorTempId = string.Empty
            },
            cancellationToken);
        if (!validation.Success || VisionAgentBuildSupport.ReadCount(validation.Data, "blockingIssues") > 0)
        {
            EmitToolLoopDraftRejected(
                request.AgentRunId,
                "validate_flow_failed",
                "Tool Loop final 草稿未通过 validate_flow 结构验收。",
                validation);
            return ToolLoopDraftValidationResult.Rejected("validate_flow_failed");
        }

        var dryRun = await RunToolLoopDraftCheckAsync(
            "dryrun_flow",
            toolContext,
            new
            {
                flow,
                entryOperatorTempId = string.Empty
            },
            cancellationToken);
        if (!dryRun.Success || VisionAgentBuildSupport.ReadBool(dryRun.Data, "dryRunSucceeded") == false)
        {
            EmitToolLoopDraftRejected(
                request.AgentRunId,
                "dryrun_flow_failed",
                "Tool Loop final 草稿未通过 dryrun_flow 元数据预演。",
                dryRun);
            return ToolLoopDraftValidationResult.Rejected("dryrun_flow_failed");
        }

        var precheckContext = toolContext with
        {
            AllowedPermissions = toolContext.AllowedPermissions
                .Concat([VisionAgentToolPermission.DeploymentPrepare])
                .ToHashSet()
        };
        var precheck = await RunToolLoopDraftCheckAsync(
            "runtime_package_precheck",
            precheckContext,
            new
            {
                flow,
                validationSummary = validation.Data,
                dryRunSummary = dryRun.Data,
                requireReplay = false
            },
            cancellationToken);
        if (!precheck.Success)
        {
            EmitToolLoopDraftRejected(
                request.AgentRunId,
                "runtime_package_precheck_failed",
                "Tool Loop final 草稿未通过 runtime_package_precheck 元数据验收。",
                precheck);
            return ToolLoopDraftValidationResult.Rejected("runtime_package_precheck_failed");
        }

        var missingResourceCount =
            VisionAgentBuildSupport.ReadMissingResources(validation.Data).Count() +
            VisionAgentBuildSupport.ReadMissingResources(precheck.Data).Count();
        var pendingActionCount = precheck.PendingActions.Count;
        _eventSink?.Append(request.AgentRunId, new AgentRunEventDraft
        {
            EventType = AgentRunEventTypes.ToolLoopDraftAccepted,
            Stage = "tool_loop",
            Title = "Tool Loop draft accepted",
            Summary = "实验 Tool Loop final 草稿已通过公开元数据验收；后续继续由稳定构建链路补全可回放 BuildResult。",
            Status = AgentRunEventStatuses.Completed,
            Payload = new
            {
                validation = "validate_flow",
                dryRun = "dryrun_flow",
                precheck = "runtime_package_precheck",
                readyForDeployment = VisionAgentBuildSupport.ReadBool(precheck.Data, "readyForDeployment") == true,
                missingResourceCount,
                pendingActionCount,
                metadataOnly = true
            }
        });

        return ToolLoopDraftValidationResult.Accept();
    }

    private async Task<VisionAgentToolResult> RunToolLoopDraftCheckAsync(
        string toolName,
        VisionAgentToolContext context,
        object arguments,
        CancellationToken cancellationToken)
    {
        if (!_toolRegistry.TryGet(toolName, out _))
        {
            return VisionAgentToolResult.Fail(
                "tool_not_registered",
                $"Tool Loop draft validation tool '{toolName}' is not registered.");
        }

        try
        {
            return await _toolRegistry.ExecuteAsync(
                toolName,
                context,
                VisionAgentBuildSupport.ToJsonElement(arguments),
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return VisionAgentToolResult.Fail("tool_exception", ex.Message);
        }
    }

    private void EmitToolLoopDraftRejected(
        string? runId,
        string reason,
        string summary,
        VisionAgentToolResult? checkResult = null)
    {
        _eventSink?.Append(runId, new AgentRunEventDraft
        {
            EventType = AgentRunEventTypes.ToolLoopDraftRejected,
            Stage = "tool_loop",
            Title = "Tool Loop draft rejected",
            Summary = summary,
            Status = AgentRunEventStatuses.Failed,
            Payload = new
            {
                rejectionReason = reason,
                checkErrorCode = checkResult?.ErrorCode,
                checkErrorMessage = checkResult?.ErrorMessage,
                firstFixRecommendation = "已回退稳定构建链路；请查看公开工具轨迹和 BuildResult 后再调整请求。",
                metadataOnly = true
            }
        });
    }

    private static bool ShouldEmitDraftRejected(
        VisionAgentLoopResult loopResult,
        string fallbackReason)
    {
        if (loopResult.Success)
        {
            return true;
        }

        return string.Equals(fallbackReason, "invalid_json", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(fallbackReason, "partial_final_requires_stable_completion", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryReadToolLoopFinalDraft(
        string content,
        out object? flow,
        out string failureReason)
    {
        flow = null;
        failureReason = string.Empty;
        if (string.IsNullOrWhiteSpace(content))
        {
            failureReason = "empty_final";
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(content);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                failureReason = "invalid_json";
                return false;
            }

            if (doc.RootElement.TryGetProperty("workflowDraft", out var draft) &&
                draft.ValueKind == JsonValueKind.Object)
            {
                flow = JsonSerializer.Deserialize<object>(draft.GetRawText(), AgentRunEventJson.Options);
                return true;
            }

            if (doc.RootElement.TryGetProperty("draftEdits", out var edits) &&
                edits.ValueKind == JsonValueKind.Array)
            {
                failureReason = "draft_edits_require_stable_completion";
                return false;
            }

            failureReason = "final_draft_missing";
            return false;
        }
        catch (JsonException)
        {
            failureReason = "invalid_json";
            return false;
        }
    }

    private sealed record ToolLoopDraftValidationResult(bool Accepted, string FallbackReason)
    {
        public static ToolLoopDraftValidationResult Accept() => new(true, string.Empty);

        public static ToolLoopDraftValidationResult Rejected(string fallbackReason) => new(false, fallbackReason);
    }

    private void EmitToolLoopFallback(
        string? runId,
        string reason,
        string summary)
    {
        _eventSink?.Append(runId, new AgentRunEventDraft
        {
            EventType = AgentRunEventTypes.ToolLoopFallback,
            Stage = "tool_loop",
            Title = "Tool Loop fallback",
            Summary = summary,
            Status = AgentRunEventStatuses.Blocked,
            Payload = new
            {
                fallbackReason = reason,
                fallbackTarget = "VisionAgentBuildOrchestrator",
                userMessage = "实验 Tool Loop 已回退到稳定构建链路。",
                metadataOnly = true
            }
        });
    }

    private void MergeToolLoopEvidence(
        AiFlowGenerationResult result,
        VisionAgentLoopResult loopResult,
        string? fallbackReason)
    {
        if (result.BuildResult == null)
        {
            result.ToolTrace.AddRange(loopResult.ToolTrace.Cast<object>());
            return;
        }

        var loopEvidence = loopResult.ToolTrace
            .Select(ToLoopEvidence)
            .ToList();
        if (!string.IsNullOrWhiteSpace(fallbackReason))
        {
            loopEvidence.Add(new VisionAgentToolEvidence
            {
                Stage = "tool_loop",
                ToolName = "tool_loop_fallback",
                Source = "fallback_build_orchestrator",
                InputSummary = "Experimental Tool Loop fallback decision.",
                OutputSummary = $"Stable BuildOrchestrator completed after fallbackReason={_redactor.RedactText(fallbackReason)}.",
                Status = AgentRunEventStatuses.Completed,
                DurationMs = 0,
                EvidenceId = $"ev_loop_{Guid.NewGuid():N}",
                WarningCode = _redactor.RedactText(fallbackReason),
                ApplyImpact = "stable_build_completed",
                DeploymentImpact = "stable_build_completed",
                MetadataOnly = true,
                RedactionPass = true
            });
        }

        result.BuildResult.ToolEvidenceTimeline.InsertRange(0, loopEvidence);
        result.ToolTrace.InsertRange(0, loopEvidence.Cast<object>());
    }

    private VisionAgentToolEvidence ToLoopEvidence(VisionAgentToolTrace trace)
    {
        var denied = string.Equals(trace.ErrorCode, "unknown_tool", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(trace.ErrorCode, "tool_permission_denied", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(trace.ErrorCode, RuntimePreviewPermissionGate.ConsentRequiredErrorCode, StringComparison.OrdinalIgnoreCase);
        return new VisionAgentToolEvidence
        {
            Stage = "tool_loop",
            ToolName = _redactor.RedactText(trace.ToolName),
            Source = "llm_tool_loop",
            InputSummary = _redactor.RedactText($"LLM requested {trace.Permission} tool."),
            OutputSummary = trace.Success
                ? "LLM-requested tool completed with public metadata."
                : _redactor.RedactText($"LLM-requested tool did not complete: {trace.ErrorCode}."),
            Status = trace.Success
                ? AgentRunEventStatuses.Completed
                : denied ? AgentRunEventStatuses.Blocked : AgentRunEventStatuses.Failed,
            DurationMs = trace.DurationMs,
            EvidenceId = $"ev_loop_{Guid.NewGuid():N}",
            WarningCode = trace.Success ? string.Empty : _redactor.RedactText(trace.ErrorCode ?? "tool_failed"),
            ApplyImpact = trace.Success ? "no_canvas_change" : "fallback_to_stable_build",
            DeploymentImpact = trace.Success ? "no_deployment_action" : "fallback_to_stable_build",
            MetadataOnly = true,
            RedactionPass = true
        };
    }

    private static void MarkFixedEvidenceSource(
        AiFlowGenerationResult result,
        string source)
    {
        var evidence = result.BuildResult?.ToolEvidenceTimeline;
        if (evidence == null)
        {
            return;
        }

        for (var index = 0; index < evidence.Count; index++)
        {
            evidence[index] = evidence[index] with
            {
                Source = source
            };
        }
    }

    private static void AppendFallbackMarker(
        AiFlowGenerationResult result,
        string fallbackReason)
    {
        result.BuildResult?.ToolEvidenceTimeline.Insert(0, new VisionAgentToolEvidence
        {
            Stage = "tool_loop",
            ToolName = "tool_loop_fallback",
            Source = "fallback_build_orchestrator",
            InputSummary = "Tool Loop could not start.",
            OutputSummary = $"Stable BuildOrchestrator completed after fallbackReason={fallbackReason}.",
            Status = AgentRunEventStatuses.Completed,
            DurationMs = 0,
            EvidenceId = $"ev_loop_{Guid.NewGuid():N}",
            WarningCode = fallbackReason,
            ApplyImpact = "stable_build_completed",
            DeploymentImpact = "stable_build_completed",
            MetadataOnly = true,
            RedactionPass = true
        });
    }

    private VisionAgentPlanModeResult BuildPlan(
        VisionAgentPlanModeRequest request,
        IReadOnlyList<VisionAgentPlanPublicEvent>? semanticEvents = null)
    {
        var description = Clean(request.Description);
        var originalPrompt = string.IsNullOrWhiteSpace(request.OriginalUserPrompt)
            ? description
            : Clean(request.OriginalUserPrompt);
        var templateSelection = RedactTemplateSelection(request.TemplateSelection);
        var maturityRequest = new VisionAgentRequirementMaturityRequest
        {
            Description = request.Description,
            AdditionalContext = request.AdditionalContext,
            Mode = request.Mode,
            HasCurrentFlow = !string.IsNullOrWhiteSpace(request.CurrentFlowSnapshot),
            TemplateSelection = templateSelection
        };
        var semantic = request.SemanticExtraction;
        var maturity = VisionAgentRequirementMaturityGate.Evaluate(maturityRequest, semantic);
        var scenario = maturity.CanPlan
            ? VisionAgentRequirementMaturityGate.ToPlanIntent(maturity)
            : maturity.TaskType == AiVisionTaskTypes.AbstractGoal ? "abstract_goal" : "requirement_clarification";
        var route = maturity.CanPlan
            ? BuildRoute(scenario, templateSelection)
            : BuildClarificationRoute(maturity);
        var questions = maturity.CanBuild
            ? BuildQuestions(scenario)
            : BuildMaturityQuestions(maturity);
        var defaults = BuildDefaults(scenario, request);
        var toolNames = _toolRegistry.ListTools()
            .Select(tool => tool.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .Take(12)
            .ToList();
        var operatorCatalogVersion = BuildCatalogVersion(toolNames);
        var hasFlow = !string.IsNullOrWhiteSpace(request.CurrentFlowSnapshot);
        var hasResult = !string.IsNullOrWhiteSpace(request.CurrentResultSnapshot);
        var attachmentCount = Math.Max(request.AttachmentSummary.Count, 0);
        var canBuild = maturity.CanBuild;
        var blockingReasons = canBuild
            ? new List<string>()
            : maturity.BlockingReasons.Count > 0
                ? maturity.BlockingReasons.ToList()
                : ["inspection_goal_missing"];

        var result = new VisionAgentPlanModeResult
        {
            PlanId = $"plan_{Guid.NewGuid():N}",
            OriginalUserPrompt = originalPrompt,
            PlanSource = "rule_baseline",
            Goal = description.Length > 160 ? description[..160] : description,
            Intent = maturity.CanPlan ? scenario : maturity.Maturity,
            Confidence = canBuild && scenario != "general_inspection" ? "high" : maturity.CanPlan ? "low" : "medium",
            RequirementUnderstanding =
            [
                maturity.PublicReason,
                .. BuildSemanticUnderstanding(semantic),
                $"taskType={maturity.TaskType}",
                maturity.ObjectSignals.Count > 0 ? $"objectSignals={string.Join(",", maturity.ObjectSignals)}" : "objectSignals=missing",
                maturity.TaskSignals.Count > 0 ? $"taskSignals={string.Join(",", maturity.TaskSignals)}" : "taskSignals=missing",
                $"检测意图：{ToScenarioTitle(scenario)}。",
                hasFlow ? "当前画布摘要可用于构建。" : "可作为新的流程草稿开始构建。",
                templateSelection != null
                    ? "已提供模板选择，构建阶段会优先考虑。"
                    : "模板选择将在构建阶段根据元数据决定。",
                attachmentCount > 0
                    ? $"已有 {attachmentCount} 个附件作为脱敏元数据可用。"
                    : "未提供附件元数据。"
            ],
            RecommendedRoute = route,
            ClarificationQuestions = questions,
            RecommendedDefaults = defaults,
            Risks = BuildRisks(scenario),
            AcceptanceCriteria = BuildAcceptanceCriteria(scenario),
            ExecutablePlan =
            [
                "确认推荐假设，或只回答高影响问题。",
                "用计划快照、用户选择、当前流程、模板、附件和工站边界摘要准备构建输入。",
                "从仅元数据目录中选择模板策略和算子链。",
                "映射参数，并把未解决资源保留为待确认参数或缺失资源。",
                "运行结构校验、预演、运行包就绪、工站兼容、算子契约和发布复核检查。",
                "应用前返回可编辑流程草稿和首要修复建议。"
            ],
            CanBuild = canBuild,
            BlockingReasons = blockingReasons,
            SemanticExtraction = semantic,
            RequirementMaturity = maturity,
            DecisionTrace = VisionAgentRequirementMaturityGate.BuildDecisionTrace(
                maturityRequest,
                maturity,
                maturity.CanPlan ? "actionable_vision_plan" : "ambiguous_vision_requirement",
                maturity.CanPlan ? "planning" : "clarifying",
                string.Empty),
            NextAction = canBuild
                ? "可接受推荐默认值或回答关键问题，然后开始构建。"
                : maturity.CanPlan
                    ? "可先复核低置信规划草案；开始构建前需补齐缺失槽位。"
                    : "请先描述检测目标，再开始构建。",
            ContextSummary = new VisionAgentPlanContextSummary
            {
                HasCurrentFlow = hasFlow,
                HasCurrentResult = hasResult,
                AttachmentCount = attachmentCount,
                TemplateSelectionMode = templateSelection?.Mode ?? string.Empty,
                TemplateId = templateSelection?.TemplateId ?? string.Empty,
                ContextKinds =
                [
                    "user_requirement",
                    hasFlow ? "current_flow" : "new_flow",
                    hasResult ? "current_result" : "no_current_result",
                    templateSelection != null ? "template_selection" : "template_catalog",
                    "operator_catalog",
                    "station_boundary"
                ],
                OperatorCatalogTools = toolNames
            },
            OperatorCatalogVersion = operatorCatalogVersion,
            TemplateCatalogVersion = templateSelection?.TemplateId is { Length: > 0 } templateId
                ? $"selected-template:{templateId}"
                : "metadata-template-catalog.v1",
            TemplateSelection = templateSelection,
            StationBoundarySummary = "仅元数据工站边界；规划阶段不会触碰相机、PLC、文件系统或网络资源。",
            PlcOutputPolicy = scenario == "plc_output"
                ? "PLC 输出先作为待确认元数据规划，直到 OK/NG 地址、握手和失效保护策略确认。"
                : "优先本地结果输出；构建就绪复核前保持 PLC 写入禁用。",
            PublicEvents = semanticEvents?.ToList() ?? [],
            MetadataOnly = true
        };

        return result with
        {
            PlanHash = ComputePlanHash(result)
        };
    }

    private static VisionAgentPlanModeResult BuildRuleFallbackPlan(
        VisionAgentPlanModeResult baseline,
        string fallbackReason,
        string warning)
    {
        var result = baseline with
        {
            PlanSource = "rule_fallback",
            FallbackReason = fallbackReason,
            PlanWarnings = [warning],
            ContractRepairNotes = [],
            PublicEvents =
            [
                .. baseline.PublicEvents,
                new VisionAgentPlanPublicEvent
                {
                    Stage = "collecting_context",
                    Status = "completed",
                    Title = "上下文收集完成",
                    Summary = "已收集公开需求、流程、模板、附件、算子和工站边界元数据。",
                    MetadataOnly = true
                },
                new VisionAgentPlanPublicEvent
                {
                    Stage = "rule_fallback_used",
                    Status = "completed",
                    Title = "已启用规则兜底",
                    Summary = warning,
                    Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["fallbackReason"] = fallbackReason
                    },
                    MetadataOnly = true
                },
                new VisionAgentPlanPublicEvent
                {
                    Stage = "plan_ready",
                    Status = "completed",
                    Title = "兜底规划已就绪",
                    Summary = "规则兜底规划已就绪，等待用户确认。",
                    MetadataOnly = true
                }
            ],
            MetadataOnly = true
        };
        return result with
        {
            PlanHash = ComputePlanHash(result)
        };
    }

    private void EmitBuildPreparationEvents(AiFlowGenerationRequest request)
    {
        var runId = request.AgentRunId;
        var build = request.BuildFromPlan;
        var plan = build?.PlanSnapshot;
        var hasExistingFlow = !string.IsNullOrWhiteSpace(request.ExistingFlowJson) ||
                              !string.IsNullOrWhiteSpace(build?.CurrentFlowSnapshot);
        EmitPlanHashDiagnosticIfNeeded(runId, build);

        _eventSink?.StageStarted(
            runId,
            "understand_requirement",
            "正在理解需求",
            "正在读取用户目标和公开计划快照。",
            new
            {
                mode = request.Mode.ToWireValue(),
                buildIntent = build?.BuildIntent ?? request.Mode.ToWireValue(),
                hasExistingFlow,
                metadataOnly = true
            });
        _eventSink?.StageCompleted(
            runId,
            "understand_requirement",
            "需求理解完成",
            "公开需求和计划上下文已归一化。",
            new
            {
                goal = plan?.Goal ?? request.Description,
                intent = plan?.Intent ?? "build_without_plan_snapshot",
                confidence = plan?.Confidence ?? "unknown",
                metadataOnly = true
            });
        _eventSink?.StageStarted(
            runId,
            "context_collection",
            "正在收集工程上下文",
            "正在收集当前流程、模板、附件、算子目录和工站边界元数据。",
            new
            {
                metadataOnly = true
            });
        _eventSink?.StageCompleted(
            runId,
            "context_collection",
            "工程上下文已收集",
            "构建上下文已作为公开元数据收集。",
            new
            {
                contextKinds = plan?.ContextSummary.ContextKinds ?? ["user_requirement", "operator_catalog", "station_boundary"],
                hasExistingFlow,
                attachmentCount = build?.AttachmentSummary.Count ?? request.Attachments?.Count ?? 0,
                templateSelectionMode = build?.TemplateSelection?.Mode ?? request.TemplateSelection?.Mode ?? string.Empty,
                templateId = build?.TemplateSelection?.TemplateId ?? request.TemplateSelection?.TemplateId ?? string.Empty,
                operatorCatalogVersion = build?.OperatorCatalogVersion ?? plan?.OperatorCatalogVersion ?? string.Empty,
                metadataOnly = true
            });
        _eventSink?.StageCompleted(
            runId,
            "plan_generation",
            build == null ? "已为构建生成计划" : "已加载构建计划",
            build == null
                ? "构建在没有计划快照的情况下启动，已推断最小公开构建计划。"
                : "已加载规划模式快照和已选选项。",
            new
            {
                planId = build?.PlanId ?? plan?.PlanId ?? string.Empty,
                planHash = build?.PlanHash ?? plan?.PlanHash ?? string.Empty,
                planSnapshot = plan,
                userSelections = build?.UserSelections ?? new Dictionary<string, string>(),
                acceptedDefaults = build?.AcceptedDefaults ?? [],
                metadataOnly = true
            });
        _eventSink?.StageCompleted(
            runId,
            "assumption_confirmation",
            "假设已确认",
            "构建模式已收到结构化选择和已接受默认值。",
            new
            {
                acceptedRecommendedDefaults = build?.AcceptedRecommendedDefaults ?? false,
                defaultCount = build?.AcceptedDefaults.Count ?? plan?.RecommendedDefaults.Count ?? 0,
                plcOutputPolicy = build?.PlcOutputPolicy ?? plan?.PlcOutputPolicy ?? string.Empty,
                metadataOnly = true
            });
        _eventSink?.StageStarted(
            runId,
            "requirement_parsing",
            "正在解析需求",
            "正在归一化结构化 BuildFromPlan 请求，以便受控工具执行。",
            new
            {
                mode = request.Mode.ToWireValue(),
                hasExistingFlow,
                attachmentCount = build?.AttachmentSummary.Count ?? request.Attachments?.Count ?? 0,
                usePlanner = string.Equals(
                    AiAgentGenerateFlowModes.Normalize(request.AgentGenerateFlowMode),
                    AiAgentGenerateFlowModes.Planner,
                    StringComparison.OrdinalIgnoreCase),
                metadataOnly = true
            });
        _eventSink?.StageCompleted(
            runId,
            "requirement_parsing",
            "需求解析完成",
            "结构化计划、选择项和仅元数据上下文已准备好交给构建工具。",
            new
            {
                buildInputSummary = BuildInputSummary(request),
                metadataOnly = true
            });
    }

    private static object BuildInputSummary(AiFlowGenerationRequest request)
    {
        var build = request.BuildFromPlan;
        return new
        {
            planId = build?.PlanId ?? string.Empty,
            planHash = build?.PlanHash ?? build?.PlanSnapshot?.PlanHash ?? string.Empty,
            buildIntent = build?.BuildIntent ?? request.Mode.ToWireValue(),
            currentFlowSnapshotIncluded = !string.IsNullOrWhiteSpace(request.ExistingFlowJson) ||
                                          !string.IsNullOrWhiteSpace(build?.CurrentFlowSnapshot),
            templateSelectionMode = build?.TemplateSelection?.Mode ?? request.TemplateSelection?.Mode ?? string.Empty,
            templateId = build?.TemplateSelection?.TemplateId ?? request.TemplateSelection?.TemplateId ?? string.Empty,
            attachmentCount = build?.AttachmentSummary.Count ?? request.Attachments?.Count ?? 0,
            operatorCatalogVersion = build?.OperatorCatalogVersion ?? string.Empty,
            stationBoundarySummary = build?.StationBoundarySummary ?? string.Empty,
            plcOutputPolicy = build?.PlcOutputPolicy ?? string.Empty,
            metadataOnly = true
        };
    }

    public static string ComputePlanHash(VisionAgentPlanModeResult? plan)
    {
        if (plan == null)
        {
            return string.Empty;
        }

        var payload = new
        {
            goal = Clean(plan.Goal),
            intent = Clean(plan.Intent),
            confidence = Clean(plan.Confidence),
            requirementUnderstanding = NormalizeList(plan.RequirementUnderstanding),
            recommendedRoute = new
            {
                routeId = Clean(plan.RecommendedRoute.RouteId),
                title = Clean(plan.RecommendedRoute.Title),
                summary = Clean(plan.RecommendedRoute.Summary),
                operators = NormalizeList(plan.RecommendedRoute.Operators),
                templateDecision = Clean(plan.RecommendedRoute.TemplateDecision)
            },
            clarificationQuestions = plan.ClarificationQuestions
                .Select(question => new
                {
                    id = Clean(question.Id),
                    title = Clean(question.Title),
                    why = Clean(question.Why),
                    defaultValue = Clean(question.DefaultValue),
                    defaultAssumption = Clean(question.DefaultAssumption),
                    impact = Clean(question.Impact),
                    options = question.Options.Select(option => new
                    {
                        value = Clean(option.Value),
                        label = Clean(option.Label),
                        recommended = option.Recommended,
                        description = Clean(option.Description),
                        impact = Clean(option.Impact)
                    }).ToList()
                })
                .ToList(),
            recommendedDefaults = plan.RecommendedDefaults
                .Select(item => new
                {
                    id = Clean(item.Id),
                    label = Clean(item.Label),
                    value = Clean(item.Value),
                    impact = Clean(item.Impact)
                })
                .ToList(),
            risks = NormalizeList(plan.Risks),
            acceptanceCriteria = NormalizeList(plan.AcceptanceCriteria),
            executablePlan = NormalizeList(plan.ExecutablePlan),
            canBuild = plan.CanBuild,
            blockingReasons = NormalizeList(plan.BlockingReasons),
            requirementMaturity = plan.RequirementMaturity == null
                ? null
                : new
                {
                    maturity = Clean(plan.RequirementMaturity.Maturity),
                    taskType = Clean(plan.RequirementMaturity.TaskType),
                    canBuild = plan.RequirementMaturity.CanBuild,
                    missingFields = NormalizeList(plan.RequirementMaturity.MissingFields),
                    blockingReasons = NormalizeList(plan.RequirementMaturity.BlockingReasons)
                },
            nextAction = Clean(plan.NextAction),
            contextSummary = new
            {
                hasCurrentFlow = plan.ContextSummary.HasCurrentFlow,
                hasCurrentResult = plan.ContextSummary.HasCurrentResult,
                attachmentCount = Math.Max(plan.ContextSummary.AttachmentCount, 0),
                templateSelectionMode = Clean(plan.ContextSummary.TemplateSelectionMode),
                templateId = Clean(plan.ContextSummary.TemplateId),
                contextKinds = NormalizeList(plan.ContextSummary.ContextKinds),
                operatorCatalogTools = NormalizeList(plan.ContextSummary.OperatorCatalogTools)
            },
            operatorCatalogVersion = Clean(plan.OperatorCatalogVersion),
            templateCatalogVersion = Clean(plan.TemplateCatalogVersion),
            templateSelection = NormalizeTemplateSelectionForHash(plan.TemplateSelection),
            stationBoundarySummary = Clean(plan.StationBoundarySummary),
            plcOutputPolicy = Clean(plan.PlcOutputPolicy),
            metadataOnly = plan.MetadataOnly
        };

        var json = JsonSerializer.Serialize(payload, PlanHashJsonOptions);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return $"sha256:{Convert.ToHexString(bytes).ToLowerInvariant()}";
    }

    private void EmitPlanHashDiagnosticIfNeeded(
        string? runId,
        VisionAgentBuildFromPlanRequest? build)
    {
        if (build?.PlanSnapshot == null || string.IsNullOrWhiteSpace(build.PlanHash))
        {
            return;
        }

        var computed = ComputePlanHash(build.PlanSnapshot);
        if (string.IsNullOrWhiteSpace(computed) ||
            string.Equals(build.PlanHash, computed, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _eventSink?.StageCompleted(
            runId,
            "plan_hash_validation",
            "Plan hash mismatch detected",
            "Build is continuing with the public plan snapshot; review plan provenance before applying.",
            new
            {
                warningCode = "plan_hash_mismatch",
                planId = build.PlanId,
                providedPlanHash = build.PlanHash,
                computedPlanHash = computed,
                publicDiagnosticsOnly = true,
                metadataOnly = true
            });
    }

    private static string DetectScenario(string description, AiTemplateSelectionInfo? templateSelection)
    {
        var text = description.ToLowerInvariant();
        if (ContainsAny(text, "wire", "terminal", "harness", "sequence", "线序", "端子", "线束", "排线", "插线"))
        {
            return "wire_sequence";
        }

        if (ContainsAny(text, "plc", "plc输出", "输出信号", "握手", "地址"))
        {
            return "plc_output";
        }

        if (ContainsAny(text, "barcode", "qr", "datamatrix", "code", "二维码", "条码", "读码", "扫码"))
        {
            return "code_recognition";
        }

        if (ContainsAny(text, "measure", "distance", "diameter", "width", "hole", "calibration", "测量", "孔距", "直径", "宽度", "尺寸", "标定", "距离"))
        {
            return "measurement";
        }

        if (ContainsAny(text, "template", "align", "position", "locate", "matching", "模板", "定位", "匹配", "对位", "找正") ||
            string.Equals(templateSelection?.Mode, "template_fill", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(templateSelection?.Mode, "template_adapt", StringComparison.OrdinalIgnoreCase))
        {
            return "template_location";
        }

        if (ContainsAny(text, "remote", "button", "keypad", "key press", "遥控器", "按键", "按钮", "键盘"))
        {
            return "button_inspection";
        }

        if (ContainsAny(text, "scratch", "metal", "surface", "defect", "crack", "dent", "划痕", "刮伤", "金属", "表面", "缺陷", "裂纹", "凹坑"))
        {
            return "surface_defect";
        }

        return "general_inspection";
    }

    private static VisionAgentRecommendedRoute BuildClarificationRoute(AiRequirementMaturityResult maturity)
    {
        return new VisionAgentRecommendedRoute
        {
            RouteId = maturity.Maturity == AiRequirementMaturity.AbstractGoal
                ? "requirement_decomposition"
                : "requirement_clarification",
            Title = "需求拆解与澄清路线",
            Summary = string.IsNullOrWhiteSpace(maturity.PublicReason)
                ? "需求信息不足，先补充对象、任务类型、图像来源、判定标准和输出目标。"
                : maturity.PublicReason,
            Operators = [],
            TemplateDecision = "需求成熟度不足时不选择算子链或模板。"
        };
    }

    private static VisionAgentRecommendedRoute BuildRoute(
        string scenario,
        AiTemplateSelectionInfo? templateSelection)
    {
        var templateDecision = templateSelection == null
            ? "构建阶段会从元数据中匹配模板；没有模板时也可生成可编辑草稿。"
            : $"优先使用已选模板模式“{templateSelection.Mode}”，再适配参数。";

        return scenario switch
        {
            "wire_sequence" => new VisionAgentRecommendedRoute
            {
                RouteId = "wire_sequence_template_first",
                Title = "模板优先的线序检测路线",
                Summary = "使用线束/端子序列模板，绑定模型元数据后判定顺序并输出 OK/NG。",
                Operators = ["ImageAcquisition", "DeepLearning", "DetectionSequenceJudge", "ResultJudgment", "ResultOutput"],
                TemplateDecision = templateDecision
            },
            "code_recognition" => new VisionAgentRecommendedRoute
            {
                RouteId = "code_recognition",
                Title = "条码/二维码识别路线",
                Summary = "采集图像、分离码区 ROI、解码 QR/DataMatrix/条码，并发布结构化结果。",
                Operators = ["ImageAcquisition", "RoiManager", "CodeRecognition", "ResultJudgment", "ResultOutput"],
                TemplateDecision = templateDecision
            },
            "measurement" => new VisionAgentRecommendedRoute
            {
                RouteId = "measurement_with_calibration",
                Title = "带标定的尺寸测量路线",
                Summary = "加载标定信息、定位几何特征、测量尺寸并比较容差。",
                Operators = ["ImageAcquisition", "CircleMeasurement", "CircleMeasurement", "Measurement", "UnitConvert", "ResultJudgment", "ResultOutput"],
                TemplateDecision = templateDecision
            },
            "template_location" => new VisionAgentRecommendedRoute
            {
                RouteId = "template_location",
                Title = "模板定位路线",
                Summary = "匹配目标模板、归一化姿态，再把对齐后的 ROI 交给下游检测。",
                Operators = ["ImageAcquisition", "TemplateMatching", "ResultJudgment", "ResultOutput"],
                TemplateDecision = templateDecision
            },
            "button_inspection" => new VisionAgentRecommendedRoute
            {
                RouteId = "button_inspection",
                Title = "遥控器/按键检测路线",
                Summary = "定位面板、分割按键位置、分类有无/状态，并判定布局。",
                Operators = ["ImageAcquisition", "TemplateMatching", "RoiManager", "DeepLearning", "ResultJudgment", "ResultOutput"],
                TemplateDecision = templateDecision
            },
            "plc_output" => new VisionAgentRecommendedRoute
            {
                RouteId = "inspection_with_plc_pending",
                Title = "PLC 输出待确认的检测路线",
                Summary = "生成检测草稿，并在地址策略确认前把 PLC OK/NG 输出保留为元数据。",
                Operators = ["ImageAcquisition", "InspectionOperator", "ResultJudgment", "ResultOutput"],
                TemplateDecision = templateDecision
            },
            "presence_absence" => new VisionAgentRecommendedRoute
            {
                RouteId = "presence_absence_inspection",
                Title = "有无 / 漏装检测路线",
                Summary = "采集图像、定位目标区域、检查有无或漏装状态，并输出 OK/NG。",
                Operators = ["ImageAcquisition", "RoiManager", "DeepLearning", "ResultJudgment", "ResultOutput"],
                TemplateDecision = templateDecision
            },
            "classification" or "attribute_classification" => new VisionAgentRecommendedRoute
            {
                RouteId = "attribute_classification_ok_ng",
                Title = "属性分类 / OK-NG 判别路线",
                Summary = "图像采集后定位目标 ROI，抽取颜色、纹理、成熟度或类别特征，并按 OK/NG 条件判定。",
                Operators = ["ImageAcquisition", "RoiManager", "DeepLearning", "ResultJudgment", "ResultOutput"],
                TemplateDecision = templateDecision
            },
            _ => new VisionAgentRecommendedRoute
            {
                RouteId = "surface_defect_detection",
                Title = "表面缺陷检测路线",
                Summary = "归一化光照、增强缺陷、分割候选区域，并按面积/对比度判定。",
                Operators = ["ImageAcquisition", "SurfaceDefectDetection", "BlobAnalysis", "ResultJudgment", "ResultOutput"],
                TemplateDecision = templateDecision
            }
        };
    }

    private static List<VisionAgentClarificationQuestion> BuildQuestions(string scenario)
    {
        return scenario switch
        {
            "wire_sequence" =>
            [
                Question("sequence_rule", "优先检查哪种线序规则？", "线序规则会影响模型标签和判定逻辑。", "left_to_right", "按模型标签顺序从左到右判定端子。", "线序策略不同会改变 OK/NG 语义。",
                [
                    Option("left_to_right", "从左到右", true, "从左到右读取端子顺序。", "适合常见线束布局，配置最快。"),
                    Option("color_order", "颜色顺序", false, "按期望颜色/标签列表判定。", "需要明确期望线序。"),
                    Option("custom_rule", "自定义规则待确认", false, "把线序规则保留为待确认元数据。", "可生成草稿，但会阻断就绪检查。")
                ]),
                Question("model_binding", "构建阶段应假定哪类模型资源？", "线序检测通常需要训练好的标签模型。", "model_pending", "创建深度学习算子，并把模型资源保留为待绑定。", "缺少模型元数据时部署就绪会保持阻断。",
                [
                    Option("model_pending", "模型待绑定", true, "把模型资源暴露为待确认参数。", "不会猜测路径，同时保持草稿可编辑。"),
                    Option("existing_model", "已有模型", false, "绑定到已有模型元数据句柄。", "需要选择模型句柄。"),
                    Option("template_only", "先用模板", false, "模型绑定前先使用模板/ROI 逻辑。", "对颜色和端子变化的鲁棒性较弱。")
                ])
            ],
            "measurement" =>
            [
                Question("measurement_target", "主要测量哪一类尺寸？", "目标类型决定几何算子和容差字段。", "hole_distance", "按标定单位测量孔距或特征距离。", "目标选错会改变算子链。", [
                    Option("hole_distance", "孔距", true, "检测圆并测量圆心距离。", "最适合常见治具检查。"),
                    Option("diameter", "直径", false, "测量圆直径。", "需要稳定边缘质量和标定。"),
                    Option("width", "宽度/间隙", false, "测量零件宽度或间隙。", "需要线/边缘提取。")
                ]),
                Question("calibration_policy", "像素到毫米标定如何处理？", "测量场景不能凭空假定比例。", "calibration_pending", "把标定文件或比例保留为待确认元数据。", "草稿可编辑，但部署不会就绪。", [
                    Option("calibration_pending", "标定待确认", true, "把标定资源暴露为待确认参数。", "最稳妥的仅元数据路径。"),
                    Option("known_scale", "已知比例", false, "使用已提供的比例元数据。", "需要用户提供比例。"),
                    Option("pixel_only", "先按像素", false, "先输出像素单位结果。", "速度快，但不适合作为量产测量。")
                ])
            ],
            "template_location" =>
            [
                Question("template_asset", "应使用哪类模板资源？", "模板匹配没有模板句柄时不能部署。", "template_pending", "创建模板匹配算子，并把模板资源保留为待绑定。", "草稿可编辑；模板元数据补齐前就绪检查会阻断。", [
                    Option("template_pending", "模板待绑定", true, "把模板资源暴露为缺失资源。", "不猜测本地路径，最稳妥。"),
                    Option("selected_template", "使用已选模板", false, "使用 UI 中的模板选择元数据。", "已选模板时优先采用。"),
                    Option("auto_locate", "暂不指定模板", false, "使用检测器或 ROI 占位方案。", "更灵活，但确定性较弱。")
                ]),
                Question("pose_tolerance", "允许多大的姿态漂移？", "搜索范围会影响速度和误检率。", "moderate", "使用中等旋转/尺度搜索范围。", "过宽搜索会拖慢预演并降低置信度。", [
                    Option("moderate", "中等范围", true, "允许小幅旋转/平移漂移。", "均衡的默认值。"),
                    Option("fixed_pose", "固定姿态", false, "假设治具稳定。", "最快，但抗扰动较弱。"),
                    Option("wide_search", "宽范围搜索", false, "允许更大的姿态漂移。", "更鲁棒，但速度较慢。")
                ])
            ],
            "button_inspection" =>
            [
                Question("button_layout", "按键位置如何建模？", "按键检测需要稳定的布局参考。", "template_layout", "使用模板定位加命名按键 ROI。", "部署前需要模板或 ROI 元数据。", [
                    Option("template_layout", "模板布局", true, "先定位遥控器主体，再检测按键 ROI。", "更适合生产默认方案。"),
                    Option("fixed_grid", "固定网格", false, "使用固定 ROI 网格。", "治具稳定时速度快。"),
                    Option("detector", "检测模型", false, "使用模型检测按键。", "需要模型资源。")
                ]),
                Question("button_defect", "最关注哪类按键问题？", "问题类型不同会影响算子选择。", "presence_state", "检查缺失、按下或状态异常。", "磨损/印刷检查可能需要额外训练样本。", [
                    Option("presence_state", "有无/状态", true, "检查缺失、按下或按键状态错误。", "适合作为初始草稿。"),
                    Option("print_defect", "印刷缺陷", false, "检查标识或印刷质量。", "需要样品和阈值。"),
                    Option("color_mismatch", "颜色不一致", false, "检查颜色或键帽不匹配。", "需要光照约束。")
                ])
            ],
            "code_recognition" =>
            [
                Question("code_type", "需要解码哪种码制？", "解码器设置和分级逻辑取决于码制。", "auto_code", "先尝试 QR/DataMatrix/条码的元数据解码设置。", "自动模式灵活，但后续可能要收紧。", [
                    Option("auto_code", "自动码制", true, "保持解码码制灵活。", "适合作为第一版草稿。"),
                    Option("qr", "QR 码", false, "使用 QR 专用解码参数。", "更快且更严格。"),
                    Option("datamatrix", "DataMatrix", false, "使用 DataMatrix 专用解码参数。", "适合工业刻印码。")
                ]),
                Question("decode_policy", "码不可读时如何处理？", "失败处理会影响输出和工站策略。", "ng_on_unreadable", "解码失败时返回 NG。", "保守的生产默认值。", [
                    Option("ng_on_unreadable", "不可读判 NG", true, "不可读码直接判为 NG。", "安全默认值。"),
                    Option("retry", "重试待确认", false, "规划重试或二次曝光。", "需要确认工站节拍。"),
                    Option("manual_review", "人工复核", false, "标记复核而不是立即 NG。", "需要操作员流程。")
                ])
            ],
            "plc_output" =>
            [
                Question("plc_policy", "PLC OK/NG 输出如何表示？", "PLC 地址和网络细节在确认前必须保持脱敏。", "metadata_pending", "创建 ResultOutput，并把 PLC 策略保留为待确认。", "避免不安全的地址猜测。", [
                    Option("metadata_pending", "PLC 待确认", true, "把 PLC 地址和握手策略暴露为待确认项。", "最安全的路径。"),
                    Option("local_first", "先本地输出", false, "PLC 集成前先本地输出。", "适合实验室验证。"),
                    Option("station_profile", "工站配置", false, "使用已选工站配置元数据。", "需要确认配置。")
                ]),
                Question("failsafe", "输出失败时采用什么失效保护？", "失效保护策略属于发布就绪条件。", "ng_on_failure", "输出失败时按 NG 或待人工介入处理。", "保守默认值。", [
                    Option("ng_on_failure", "失败判 NG", true, "输出失败默认判 NG。", "更安全的生产行为。"),
                    Option("hold_last", "保持上一信号", false, "保持上一次信号。", "需要 PLC 握手复核。"),
                    Option("block_release", "阻断发布", false, "确认前阻断部署。", "最保守。")
                ])
            ],
            "presence_absence" =>
            [
                Question("presence_target", "主要检查哪类有无状态？", "有无/漏装目标决定 ROI 和分类标签。", "target_present", "检查目标存在、缺失或漏装。", "目标定义会影响 OK/NG 判定。", [
                    Option("target_present", "目标有无", true, "检测指定目标是否存在。", "适合漏装和缺件场景。"),
                    Option("assembly_complete", "装配完整", false, "检查多个部件是否齐全。", "需要补充部件清单。"),
                    Option("state_pending", "状态待确认", false, "先保留有无状态为待确认元数据。", "可生成草稿但就绪会阻断。")
                ]),
                Question("presence_judgment", "缺失时如何判定？", "判定策略决定 ResultJudgment 输出。", "missing_is_ng", "目标缺失时返回 NG。", "保守生产默认值。", [
                    Option("missing_is_ng", "缺失判 NG", true, "缺失或漏装直接判 NG。", "适合大多数量产检查。"),
                    Option("report_only", "仅报告", false, "只输出有无结果，不立即判 NG。", "适合探索阶段。"),
                    Option("manual_review", "人工复核", false, "缺失疑似时转人工复核。", "需要操作流程。")
                ])
            ],
            "classification" or "attribute_classification" =>
            [
                Question("attribute_target", "要判别哪个属性？", "属性会影响特征、模型标签和 OK/NG 语义。", "semantic_attribute", "按用户描述的属性进行分类或 OK/NG 判别。", "属性定义不清会导致误判。", [
                    Option("semantic_attribute", "语义属性", true, "例如成熟度、颜色、类别或状态。", "保留语义槽位并进入分类路线。"),
                    Option("color_texture", "颜色/纹理", false, "基于颜色、纹理或外观特征判别。", "需要稳定光照。"),
                    Option("model_label", "模型标签", false, "使用分类模型标签输出。", "需要模型资源。")
                ]),
                Question("ok_ng_rule", "OK/NG 条件如何落地？", "属性分类必须明确通过和不通过条件。", "use_extracted_conditions", "使用语义抽取出的 OK/NG 条件。", "后续可在构建前复核阈值或标签。", [
                    Option("use_extracted_conditions", "使用抽取条件", true, "沿用用户描述中的 OK/NG 条件。", "最快形成可审查草案。"),
                    Option("threshold_pending", "阈值待确认", false, "保留阈值或类别边界为待确认。", "不会猜测生产阈值。"),
                    Option("sample_review", "样本复核", false, "需要样本确认边界。", "更稳但需要数据。")
                ])
            ],
            _ =>
            [
                Question("defect_definition", "缺陷判定标准是什么？", "缺陷定义会影响阈值和判定逻辑。", "scratch_or_blob", "检测可见划痕/斑点，并按面积和对比度判定。", "阈值需要结合样品确认。", [
                    Option("scratch_or_blob", "划痕/斑点", true, "使用通用表面缺陷候选区域。", "适合作为初始草稿。"),
                    Option("crack", "裂纹", false, "重点检测细长明暗裂纹类缺陷。", "需要确认对比度假设。"),
                    Option("dent_or_stain", "凹痕/污渍", false, "关注凹痕、污渍或变色。", "需要确认光照和样品条件。")
                ]),
                Question("roi_strategy", "应使用哪种 ROI 策略？", "ROI 选择会影响误检率和参数完整性。", "main_surface", "检测主要可见表面。", "让草稿更聚焦。", [
                    Option("main_surface", "主要表面", true, "使用一个主要 ROI 占位。", "最佳默认值。"),
                    Option("full_frame", "整图检测", false, "检测完整画面。", "设置项更少，但噪声更多。"),
                    Option("auto_locate", "自动定位", false, "缺陷检测前先定位零件。", "更鲁棒，但更复杂。")
                ])
            ]
        };
    }

    private static List<VisionAgentClarificationQuestion> BuildMaturityQuestions(AiRequirementMaturityResult maturity)
    {
        var missing = maturity.MissingFields.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var questions = new List<VisionAgentClarificationQuestion>();

        if (missing.Count == 0 || missing.Contains("inspection_object"))
        {
            questions.Add(Question(
                "inspection_object",
                "检测对象是什么？",
                "对象决定相机视野、ROI、模板和算子链。",
                "object_pending",
                "先补充一个明确产品或部件对象。",
                "没有对象时不能生成可靠流程。",
                [
                    Option("package_box", "包装箱/标签", true, "适合外观、贴附、读码或缺失检查。", "会引导到包装类视觉路线。"),
                    Option("hole_part", "圆孔/孔位", false, "适合孔距、直径、圆心距测量。", "会引导到几何测量路线。"),
                    Option("terminal_harness", "端子/线束", false, "适合线序或插线检查。", "会引导到线序路线。")
                ]));
        }

        if (missing.Count == 0 || missing.Contains("task_type"))
        {
            questions.Add(Question(
                "task_type",
                "要解决哪一类视觉任务？",
                "任务类型决定是否走缺陷、测量、线序、OCR、漏装或分类路线。",
                "task_pending",
                "先选择一个可落地任务类型。",
                "任务未明确时不能开始构建。",
                [
                    Option("surface_or_pose_defect", "缺陷/贴附/姿态", true, "检查破损、脏污、贴正、偏斜等。", "会使用外观或定位相关路线。"),
                    Option("geometry_measurement", "尺寸测量", false, "测量孔距、直径、宽度、距离或角度。", "会使用几何测量路线。"),
                    Option("wire_sequence", "线序", false, "检查端子颜色或线束顺序是否正确。", "会使用线序路线。"),
                    Option("barcode_qr", "OCR/读码", false, "识别文字、二维码、条码或 DataMatrix。", "会使用读码识别路线。")
                ]));
        }

        if (missing.Contains("image_source"))
        {
            questions.Add(Question(
                "image_source",
                "图像来源是什么？",
                "图像来源影响采集节点、资源占位和部署就绪。",
                "camera_or_file_pending",
                "先按相机或文件源保留为待确认元数据。",
                "来源未确认时可以规划，但不能构建低成熟度需求。",
                [
                    Option("camera", "相机采集", true, "产线相机或工站相机输入。", "后续需要绑定相机元数据。"),
                    Option("file", "图片文件", false, "先用样张或文件目录验证。", "适合离线验证。"),
                    Option("metadata_only", "先不指定", false, "仅补充需求，不绑定真实资源。", "不会进入 Build。")
                ]));
        }

        if (missing.Contains("acceptance_criteria"))
        {
            questions.Add(Question(
                "acceptance_criteria",
                "OK/NG 判定标准是什么？",
                "判定标准决定阈值、公差、输出字段和验收方式。",
                "criteria_pending",
                "先补充一个可检查的判定规则。",
                "没有判定标准时不能可靠落地。",
                [
                    Option("ok_ng_rule", "OK/NG 规则", true, "说明通过和不通过的边界。", "会映射到结果判定节点。"),
                    Option("numeric_tolerance", "数值公差", false, "适合尺寸测量。", "会映射到测量阈值。"),
                    Option("report_only", "仅输出报告", false, "先输出结构化结果，不直接判定。", "适合探索阶段。")
                ]));
        }

        if (missing.Contains("model_or_rule_strategy"))
        {
            questions.Add(Question(
                "model_or_rule_strategy",
                "实现方式倾向是什么？",
                "未知对象或未知识别内容需要先确认使用规则、传统算法、模板还是模型策略。",
                "strategy_pending",
                "先按可替换策略占位，Build 前再确认实现方式。",
                "实现方式未确认时可以规划低置信草案，但不能直接构建。",
                [
                    Option("rule_first", "规则/传统算法优先", true, "先用阈值、几何、形态学或规则判断构成草案。", "适合可解释、低风险的初始方案。"),
                    Option("template_first", "模板/定位优先", false, "先定位目标区域，再按目标内容做检测。", "适合目标姿态稳定的场景。"),
                    Option("model_first", "模型识别优先", false, "保留模型资源为待确认占位。", "适合未知外观或语义识别，但需要后续模型资源。")
                ]));
        }

        return questions.Take(5).ToList();
    }

    private static List<VisionAgentDefaultAssumption> BuildDefaults(
        string scenario,
        VisionAgentPlanModeRequest request)
    {
        var defaults = new List<VisionAgentDefaultAssumption>
        {
            new()
            {
                Id = "metadata_only",
                Label = "仅公开诊断",
                Value = "redacted_metadata",
                Impact = "不会暴露原始本地路径、图像字节、工站网络细节、令牌或提示词。"
            },
            new()
            {
                Id = "draft_policy",
                Label = "可编辑流程草稿",
                Value = "allow_editable_draft_when_not_deploy_ready",
                Impact = "即使部署就绪被阻断，也可先应用到画布继续编辑。"
            },
            new()
            {
                Id = "resource_policy",
                Label = "缺失资源保持待确认",
                Value = "pending_parameters",
                Impact = "模型、模板、相机、标定和 PLC 元数据会显示为待补项，而不是被猜测。"
            }
        };

        if (request.TemplateSelection != null)
        {
            defaults.Add(new VisionAgentDefaultAssumption
            {
                Id = "template_selection",
                Label = "尊重已选模板元数据",
                Value = request.TemplateSelection.Mode ?? "selected",
                Impact = "构建会优先使用用户选择的模板，再回退到目录匹配。"
            });
        }

        if (scenario == "measurement")
        {
            defaults.Add(new VisionAgentDefaultAssumption
            {
                Id = "measurement_units",
                Label = "物理单位需要标定",
                Value = "calibration_pending",
                Impact = "比例或标定元数据确认前，测量输出不会达到发布就绪。"
            });
        }

        return defaults;
    }

    private static List<string> BuildRisks(string scenario)
    {
        var common = new List<string>
        {
            "量产前需要代表性图像确认现场阈值。",
            "相机、模型、模板、标定和 PLC 资源在确认前保持为仅元数据。",
            "工站兼容性可能阻断发布，但画布草稿仍可编辑。"
        };
        if (scenario == "plc_output")
        {
            common.Add("PLC OK/NG 输出在地址、握手和失效保护复核前不得启用。");
        }
        if (scenario == "measurement")
        {
            common.Add("测量精度依赖标定和镜头畸变控制。");
        }
        if (scenario is "classification" or "attribute_classification")
        {
            common.Add("属性分类边界需要样本、光照和 OK/NG 标签复核。");
        }
        return common;
    }

    private static List<string> BuildAcceptanceCriteria(string scenario)
    {
        var criteria = new List<string>
        {
            "流程草稿包含采集、检测、判定和输出阶段。",
            "计划快照、用户选择、默认值和构建输入摘要可从 AgentRun 回放。",
            "就绪、预演、运行包、工站、契约和发布复核事件可回放。",
            "应用或部署前可以看到待确认参数和缺失资源。"
        };
        if (scenario == "measurement")
        {
            criteria.Add("测量发布前，标定或比例元数据必须处于待确认或已确认状态。");
        }
        if (scenario == "code_recognition")
        {
            criteria.Add("解码失败策略必须体现在结果判定或待确认输出策略中。");
        }
        if (scenario is "classification" or "attribute_classification")
        {
            criteria.Add("OK/NG 条件必须保留在结果判定或待确认标签策略中。");
        }
        return criteria;
    }

    private static VisionAgentClarificationQuestion Question(
        string id,
        string title,
        string why,
        string defaultValue,
        string defaultAssumption,
        string impact,
        List<VisionAgentClarificationOption> options)
    {
        return new VisionAgentClarificationQuestion
        {
            Id = id,
            Title = title,
            Why = why,
            DefaultValue = defaultValue,
            DefaultAssumption = defaultAssumption,
            Impact = impact,
            Options = options
        };
    }

    private static VisionAgentClarificationOption Option(
        string value,
        string label,
        bool recommended,
        string description,
        string impact)
    {
        return new VisionAgentClarificationOption
        {
            Value = value,
            Label = label,
            Recommended = recommended,
            Description = description,
            Impact = impact
        };
    }

    private static bool ContainsAny(string text, params string[] values)
    {
        return values.Any(value => text.Contains(value, StringComparison.OrdinalIgnoreCase));
    }

    private static string ToScenarioTitle(string scenario)
    {
        return scenario switch
        {
            "wire_sequence" => "线序检测",
            "code_recognition" => "条码/二维码识别",
            "measurement" => "尺寸测量",
            "template_location" => "模板定位",
            "button_inspection" => "按键检测",
            "plc_output" => "带 PLC 输出的检测",
            "presence_absence" => "有无 / 漏装检测",
            "classification" => "分类识别",
            "attribute_classification" => "属性分类 / OK-NG 判别",
            "surface_defect" => "表面缺陷检测",
            _ => "通用视觉检测"
        };
    }

    private static string BuildCatalogVersion(IReadOnlyList<string> toolNames)
    {
        var joined = string.Join("|", toolNames);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(joined));
        return $"vision-agent-tools:{toolNames.Count}:{Convert.ToHexString(hash)[..12].ToLowerInvariant()}";
    }

    private static AiTemplateSelectionInfo? RedactTemplateSelection(AiTemplateSelectionInfo? selection)
    {
        var mode = SafeOptionalTemplateToken(selection?.Mode, string.Empty).ToLowerInvariant();
        var templateId = SafeOptionalTemplateToken(selection?.TemplateId, "redacted_template");
        var scenarioKey = SafeOptionalTemplateToken(selection?.ScenarioKey, string.Empty);

        if (string.IsNullOrWhiteSpace(mode) &&
            string.IsNullOrWhiteSpace(templateId) &&
            string.IsNullOrWhiteSpace(scenarioKey))
        {
            return null;
        }

        return new AiTemplateSelectionInfo
        {
            Mode = mode,
            TemplateId = string.IsNullOrWhiteSpace(templateId) ? null : templateId,
            ScenarioKey = string.IsNullOrWhiteSpace(scenarioKey) ? null : scenarioKey
        };
    }

    private static object? NormalizeTemplateSelectionForHash(AiTemplateSelectionInfo? selection)
    {
        var redacted = RedactTemplateSelection(selection);
        return redacted == null
            ? null
            : new
            {
                mode = Clean(redacted.Mode),
                templateId = Clean(redacted.TemplateId),
                scenarioKey = Clean(redacted.ScenarioKey)
            };
    }

    private static List<string> NormalizeList(IEnumerable<string>? values)
    {
        return values?
            .Select(Clean)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToList() ?? [];
    }

    private static string Clean(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
    }

    private static string Truncate(string? value, int maxChars)
    {
        var text = value ?? string.Empty;
        return text.Length <= maxChars ? text : text[..maxChars];
    }

    private static string SafeTemplateToken(string? value, string fallback)
    {
        var text = Clean(value);
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        if (UnsafeTemplateMetadataRegex.IsMatch(text) || text.Length > 160)
        {
            return fallback;
        }

        var safe = new string(text
            .Where(ch => char.IsLetterOrDigit(ch) || ch is '_' or '-' or '.')
            .ToArray());
        return string.IsNullOrWhiteSpace(safe) ? fallback : safe;
    }

    private static string SafeOptionalTemplateToken(string? value, string unsafeFallback)
    {
        return string.IsNullOrWhiteSpace(Clean(value))
            ? string.Empty
            : SafeTemplateToken(value, unsafeFallback);
    }
}
