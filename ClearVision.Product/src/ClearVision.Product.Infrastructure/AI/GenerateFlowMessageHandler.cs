using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using ClearVision.Product.Contracts.Messages;
using ClearVision.Product.Core.DTOs;
using ClearVision.Product.Core.Services;
using ClearVision.Product.Infrastructure.AI.Agent;
using Microsoft.Extensions.Logging;

namespace ClearVision.Product.Infrastructure.AI;

/// <summary>
/// Handles GenerateFlow requests from the desktop bridge.
/// </summary>
public class GenerateFlowMessageHandler
{
    private readonly IAiFlowGenerationService _generationService;
    private readonly Microsoft.Extensions.Logging.ILogger<GenerateFlowMessageHandler> _logger;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    public GenerateFlowMessageHandler(
        IAiFlowGenerationService generationService,
        Microsoft.Extensions.Logging.ILogger<GenerateFlowMessageHandler> logger)
    {
        _generationService = generationService;
        _logger = logger;
    }

    public async Task<string> HandleAsync(
        string description,
        string? sessionId = null,
        string? existingFlowJson = null,
        string? hint = null,
        GenerateFlowMode mode = GenerateFlowMode.Auto,
        bool debugPrompt = false,
        string? requestId = null,
        IReadOnlyList<string>? attachments = null,
        string? requirementMode = null,
        AiTemplateSelectionInfo? templateSelection = null,
        VisionAgentBuildFromPlanRequest? buildFromPlan = null,
        bool useVisionAgentGenerateFlow = false,
        string? agentGenerateFlowMode = null,
        bool runtimePreviewConsent = false,
        Action<string, string>? onMessage = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Received AI generate-flow request. Description={Description}", description);

        try
        {
            onMessage?.Invoke(
                "GenerateFlowProgress",
                JsonSerializer.Serialize(new
                {
                    message = "正在连接 AI 服务...",
                    phase = "connecting",
                    requestId
                }, _jsonOptions));

            var maturityGate = TryBuildMaturityGateResponse(
                description,
                hint,
                sessionId,
                existingFlowJson,
                mode,
                requestId,
                requirementMode,
                templateSelection,
                buildFromPlan,
                useVisionAgentGenerateFlow);
            if (maturityGate is { } gate)
            {
                onMessage?.Invoke(
                    "GenerateFlowProgress",
                    JsonSerializer.Serialize(new
                    {
                        message = gate.Response.AiExplanation ?? "当前需求还需要澄清，已阻断直接构建。",
                        phase = "clarification",
                        requestId
                    }, _jsonOptions));

                return SerializeResponse(gate.Response, gate.FailureType);
            }

            var result = await _generationService.GenerateFlowAsync(
                new AiFlowGenerationRequest(
                    Description: description,
                    AdditionalContext: hint,
                    SessionId: sessionId,
                    ExistingFlowJson: existingFlowJson,
                    Attachments: attachments,
                    Mode: mode,
                    DebugPrompt: debugPrompt,
                    TemplateSelection: templateSelection)
                {
                    RequirementMode = requirementMode ?? AiRequirementModes.Strict,
                    UseVisionAgentGenerateFlow = useVisionAgentGenerateFlow,
                    AgentGenerateFlowMode = AiAgentGenerateFlowModes.Normalize(agentGenerateFlowMode),
                    RuntimePreviewConsent = runtimePreviewConsent,
                    BuildFromPlan = buildFromPlan
                },
                progressMsg => onMessage?.Invoke(
                    "GenerateFlowProgress",
                    JsonSerializer.Serialize(new
                    {
                        message = progressMsg,
                        phase = InferProgressPhase(progressMsg),
                        requestId
                    }, _jsonOptions)),
                chunk => onMessage?.Invoke(
                    "GenerateFlowStreamChunk",
                    JsonSerializer.Serialize(new GenerateFlowStreamChunk
                    {
                        ChunkType = chunk.ChunkType,
                        Content = chunk.Content,
                        RequestId = requestId
                    }, _jsonOptions)),
                cancellationToken,
                attachmentReport => onMessage?.Invoke(
                    "GenerateFlowAttachmentReport",
                    JsonSerializer.Serialize(attachmentReport with { RequestId = requestId }, _jsonOptions)));

            var response = new GenerateFlowResponse
            {
                Success = result.Success,
                Status = NormalizeStatus(result.CompletionStatus, result.Success),
                Flow = result.Flow,
                ErrorMessage = result.ErrorMessage,
                FailureSummary = BuildFailureSummaryText(result.FailureSummary, result.ErrorMessage),
                LastAttemptDiagnostics = result.LastAttemptDiagnostics,
                AiExplanation = result.AiExplanation,
                Reasoning = result.Reasoning,
                ParametersNeedingReview = result.ParametersNeedingReview,
                ClarificationRequired = result.ClarificationRequired,
                RequirementBrief = MapRequirementBrief(result.RequirementBrief),
                SessionId = result.SessionId ?? sessionId,
                RequestId = requestId,
                DetectedIntent = result.DetectedIntent,
                DryRunResult = result.DryRunResult,
                RecommendedTemplate = MapRecommendedTemplate(result.RecommendedTemplate),
                GenerationMode = result.GenerationMode,
                TemplateLockLevel = result.TemplateLockLevel,
                TemplateCandidates = MapTemplateCandidates(result.TemplateCandidates),
                PendingParameters = MapPendingParameters(result.PendingParameters),
                MissingResources = MapMissingResources(result.MissingResources),
                PendingActions = result.PendingActions,
                ValidationPreview = result.ValidationPreview,
                ToolTrace = result.ToolTrace,
                BuildResult = result.BuildResult,
                BuildReadiness = result.BuildReadiness,
                WorkflowDiff = result.BuildResult?.WorkflowDiff,
                ApplyGate = result.BuildResult?.ApplyGate,
                ToolEvidenceTimeline = result.BuildResult?.ToolEvidenceTimeline,
                FirstFixRecommendation = result.BuildResult?.FirstFixRecommendation,
                ManualRetry = MapManualRetry(result.ManualRetry),
                PromptTrace = MapPromptTrace(result.PromptTrace),
                StageTimeline = MapStageTimeline(result.StageTimeline),
                PerformanceBudget = BuildPerformanceBudget(result.StageTimeline, result.RetryCount, result.PromptTrace),
                CompletionStatus = result.CompletionStatus,
                RetryCount = result.RetryCount,
                KnowledgeDiagnostics = MapKnowledgeDiagnostics(result.KnowledgeDiagnostics),
                TurnIntent = result.TurnIntent,
                InteractionState = result.InteractionState,
                RouterConfidence = result.RouterConfidence,
                BlockingClarificationFields = result.BlockingClarificationFields.ToList(),
                NonBlockingMissingFields = result.NonBlockingMissingFields.ToList(),
                RequirementMaturity = result.RequirementMaturity,
                DecisionTrace = result.DecisionTrace,
                PromptVersionId = result.PromptTrace is AiPromptTrace pt ? pt.PromptVersionId : null,
                PromptVersionName = result.PromptTrace is AiPromptTrace pt2 ? pt2.PromptVersionName : null
            };

            return SerializeResponse(response, result.FailureType);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation("AI generation request was cancelled by the user. SessionId={SessionId}", sessionId);

            var cancelledResponse = new GenerateFlowResponse
            {
                Success = false,
                Status = AiFlowGenerationResult.CompletionStatusCancelled,
                ErrorMessage = "用户已取消本次生成。",
                FailureSummary = "用户已取消本次生成。",
                LastAttemptDiagnostics = Array.Empty<AiAttemptDiagnostic>(),
                SessionId = sessionId,
                RequestId = requestId
            };

            return SerializeResponse(cancelledResponse, AiFlowGenerationResult.FailureTypeUserCancelled);
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogWarning(
                "AI generation request timed out. SessionId={SessionId}. Error={Error}",
                sessionId ?? string.Empty,
                ex.Message);

            var timeoutResponse = new GenerateFlowResponse
            {
                Success = false,
                Status = AiFlowGenerationResult.CompletionStatusTimedOut,
                ErrorMessage = "AI 生成超时，请稍后重试。",
                FailureSummary = "AI 生成超时，请稍后重试。",
                LastAttemptDiagnostics = Array.Empty<AiAttemptDiagnostic>(),
                SessionId = sessionId,
                RequestId = requestId
            };

            return SerializeResponse(timeoutResponse, AiFlowGenerationResult.FailureTypeTimeout);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error while handling AI generate-flow request");

            var errorResponse = new GenerateFlowResponse
            {
                Success = false,
                Status = AiFlowGenerationResult.CompletionStatusFailed,
                ErrorMessage = $"服务内部错误：{ex.Message}",
                FailureSummary = $"服务内部错误：{ex.Message}",
                LastAttemptDiagnostics = Array.Empty<AiAttemptDiagnostic>(),
                SessionId = sessionId,
                RequestId = requestId
            };

            return SerializeResponse(errorResponse, AiFlowGenerationResult.FailureTypeSystemError);
        }
    }

    private static (GenerateFlowResponse Response, string FailureType)? TryBuildMaturityGateResponse(
        string description,
        string? hint,
        string? sessionId,
        string? existingFlowJson,
        GenerateFlowMode mode,
        string? requestId,
        string? requirementMode,
        AiTemplateSelectionInfo? templateSelection,
        VisionAgentBuildFromPlanRequest? buildFromPlan,
        bool useVisionAgentGenerateFlow)
    {
        if (!useVisionAgentGenerateFlow ||
            buildFromPlan != null ||
            mode is GenerateFlowMode.Explain or GenerateFlowMode.ReviewPendingParameters)
        {
            return null;
        }

        var maturityRequest = new VisionAgentRequirementMaturityRequest
        {
            Description = buildFromPlan?.OriginalUserPrompt ?? description,
            AdditionalContext = hint,
            Mode = buildFromPlan?.BuildIntent ?? mode.ToWireValue(),
            HasCurrentFlow = !string.IsNullOrWhiteSpace(existingFlowJson) ||
                             !string.IsNullOrWhiteSpace(buildFromPlan?.CurrentFlowSnapshot),
            HasPendingPlan = buildFromPlan?.PlanSnapshot != null,
            TemplateSelection = buildFromPlan?.TemplateSelection ?? templateSelection
        };
        if (buildFromPlan?.PlanSnapshot?.CanBuild == true &&
            buildFromPlan.PlanSnapshot.RequirementMaturity == null &&
            buildFromPlan.RequirementMaturity == null)
        {
            return null;
        }

        var maturity = buildFromPlan?.PlanSnapshot?.RequirementMaturity ??
                       buildFromPlan?.RequirementMaturity ??
                       VisionAgentRequirementMaturityGate.Evaluate(
                           maturityRequest,
                           buildFromPlan?.PlanSnapshot?.SemanticExtraction);
        var blocked = buildFromPlan?.PlanSnapshot?.CanBuild == false ||
                      buildFromPlan?.RequirementMaturity?.CanBuild == false ||
                      !maturity.CanBuild ||
                      maturity.Maturity is AiRequirementMaturity.AbstractGoal or AiRequirementMaturity.Ambiguous or AiRequirementMaturity.ChatOrHelp;
        if (!blocked)
        {
            return null;
        }

        var fields = maturity.MissingFields.Count > 0
            ? maturity.MissingFields
            : ["inspection_object", "task_type", "image_source", "acceptance_criteria"];
        var trace = VisionAgentRequirementMaturityGate.BuildDecisionTrace(
            maturityRequest,
            maturity,
            "build_blocked",
            "clarifying",
            "maturity_gate_blocked");
        var brief = new AiRequirementBrief
        {
            IntentType = maturity.Maturity,
            RequirementMode = requirementMode ?? AiRequirementModes.Strict,
            Confidence = maturity.CanBuild ? 0.75 : 0.25,
            HasOpenQuestions = true,
            ClarificationRequired = true,
            CanGenerateDraftNow = false,
            DraftRiskLevel = "high",
            KnownFacts = maturity.ObjectSignals
                .Concat(maturity.TaskSignals)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(8)
                .ToList(),
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
                    "image_source" => "请说明图像来源是相机、图片文件，还是先只做元数据规划。",
                    "acceptance_criteria" => "请说明 OK/NG 判定标准、数值公差或输出目标。",
                    "output_target" => "请说明结果要输出到画布、报表、PLC，还是仅做人工复核。",
                    _ => "请补充该字段后再构建。"
                },
                Required = true,
                Reason = "直接构建前要求需求成熟度达到可构建。",
                Priority = "high",
                Options = []
            }).ToList()
        };

        var response = new GenerateFlowResponse
        {
            Success = false,
            Status = AiFlowGenerationResult.CompletionStatusClarificationRequired,
            ErrorMessage = maturity.PublicReason,
            FailureSummary = maturity.PublicReason,
            AiExplanation = maturity.PublicReason,
            ClarificationRequired = true,
            RequirementBrief = MapRequirementBrief(brief),
            SessionId = sessionId,
            RequestId = requestId,
            CompletionStatus = AiFlowGenerationResult.CompletionStatusClarificationRequired,
            TurnIntent = AiTurnIntents.NewFlow,
            InteractionState = AiInteractionStates.Clarifying,
            RouterConfidence = AiRouterConfidence.High,
            BlockingClarificationFields = fields.ToList(),
            NonBlockingMissingFields = maturity.MissingFields.Except(fields, StringComparer.OrdinalIgnoreCase).ToList(),
            RequirementMaturity = maturity,
            DecisionTrace = trace,
            StageTimeline =
            [
                new GenerateFlowStageDiagnostic
                {
                    Stage = "requirement_maturity_gate",
                    Status = "blocked",
                    Summary = maturity.PublicReason,
                    Metadata = new Dictionary<string, string>
                    {
                        ["maturity"] = maturity.Maturity,
                        ["taskType"] = maturity.TaskType,
                        ["canBuild"] = maturity.CanBuild ? "true" : "false"
                    }
                }
            ]
        };

        return (response, AiFlowGenerationResult.FailureTypeClarificationRequired);
    }

    private static string SerializeResponse(GenerateFlowResponse response, string? failureType)
    {
        return JsonSerializer.Serialize(new
        {
            response.Type,
            response.Success,
            response.Status,
            response.Flow,
            response.ErrorMessage,
            response.FailureSummary,
            response.LastAttemptDiagnostics,
            response.AiExplanation,
            response.Reasoning,
            response.ParametersNeedingReview,
            response.ClarificationRequired,
            response.RequirementBrief,
            response.SessionId,
            response.RequestId,
            response.DetectedIntent,
            response.DryRunResult,
            response.RecommendedTemplate,
            response.GenerationMode,
            response.TemplateLockLevel,
            response.TemplateCandidates,
            response.PendingParameters,
            response.MissingResources,
            response.PendingActions,
            response.ValidationPreview,
            response.ToolTrace,
            response.BuildResult,
            response.BuildReadiness,
            response.WorkflowDiff,
            response.ApplyGate,
            response.ToolEvidenceTimeline,
            response.FirstFixRecommendation,
            response.ManualRetry,
            response.PromptTrace,
            response.StageTimeline,
            response.PerformanceBudget,
            response.CompletionStatus,
            response.RetryCount,
            response.KnowledgeDiagnostics,
            response.TurnIntent,
            response.InteractionState,
            response.RouterConfidence,
            response.BlockingClarificationFields,
            response.NonBlockingMissingFields,
            response.RequirementMaturity,
            response.DecisionTrace,
            response.PromptVersionId,
            response.PromptVersionName,
            FailureType = failureType
        }, _jsonOptions);
    }

    private static string NormalizeStatus(string? completionStatus, bool success)
    {
        if (!string.IsNullOrWhiteSpace(completionStatus))
        {
            return completionStatus;
        }

        return success
            ? AiFlowGenerationResult.CompletionStatusCompleted
            : AiFlowGenerationResult.CompletionStatusFailed;
    }

    private static string? BuildFailureSummaryText(AiFailureSummary? failureSummary, string? fallbackMessage)
    {
        if (!string.IsNullOrWhiteSpace(failureSummary?.Message))
        {
            return failureSummary.Message;
        }

        return string.IsNullOrWhiteSpace(fallbackMessage)
            ? null
            : fallbackMessage;
    }

    private static string InferProgressPhase(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return string.Empty;

        if (ContainsAny(message, ["澄清", "缺少关键字段"]))
            return "clarification";
        if (ContainsAny(message, ["场景", "模板"]))
            return "matching_template";
        if (ContainsAny(message, ["提示词", "需求", "Prompt", "prompt"]))
            return "prompt_context";
        if (ContainsAny(message, ["请求 AI", "AI 模型", "模型生成", "备用模型", "调用失败"]))
            return "calling_ai";
        if (ContainsAny(message, ["解析", "JSON 数据"]))
            return "parsing";
        if (ContainsAny(message, ["校验", "算子和参数", "结构校验"]))
            return "validating";
        if (ContainsAny(message, ["Dry-Run", "DryRun", "沙箱"]))
            return "dryrun";
        if (ContainsAny(message, ["布局", "layout"]))
            return "layouting";

        return string.Empty;
    }

    private static bool ContainsAny(string text, IEnumerable<string> terms)
    {
        return terms.Any(term => text.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    private static GenerateFlowTemplateRecommendation? MapRecommendedTemplate(AiRecommendedTemplateInfo? template)
    {
        if (template == null)
        {
            return null;
        }

        return new GenerateFlowTemplateRecommendation
        {
            TemplateId = template.TemplateId,
            TemplateName = template.TemplateName,
            TemplateVersion = template.TemplateVersion,
            ScenarioKey = template.ScenarioKey,
            Industry = template.Industry,
            MatchReason = template.MatchReason,
            MatchMode = template.MatchMode,
            Confidence = template.Confidence,
            MatchedFields = template.MatchedFields?.Distinct(StringComparer.OrdinalIgnoreCase).ToList() ?? new List<string>(),
            MissingSignals = template.MissingSignals?.Distinct(StringComparer.OrdinalIgnoreCase).ToList() ?? new List<string>()
        };
    }

    private static List<GenerateFlowTemplateCandidate> MapTemplateCandidates(
        IReadOnlyCollection<AiTemplateCandidateInfo>? candidates)
    {
        if (candidates == null || candidates.Count == 0)
        {
            return new List<GenerateFlowTemplateCandidate>();
        }

        return candidates
            .OrderByDescending(item => item.Confidence)
            .Select(item => new GenerateFlowTemplateCandidate
            {
                TemplateId = item.TemplateId,
                TemplateName = item.TemplateName,
                TemplateVersion = item.TemplateVersion,
                ScenarioKey = item.ScenarioKey,
                Industry = item.Industry,
                Confidence = item.Confidence,
                MatchReason = item.MatchReason,
                MatchedFields = item.MatchedFields?.Distinct(StringComparer.OrdinalIgnoreCase).ToList() ?? new List<string>(),
                MissingSignals = item.MissingSignals?.Distinct(StringComparer.OrdinalIgnoreCase).ToList() ?? new List<string>()
            })
            .ToList();
    }

    private static List<GenerateFlowPendingParameter> MapPendingParameters(IReadOnlyCollection<AiPendingParameterInfo>? parameters)
    {
        if (parameters == null || parameters.Count == 0)
        {
            return new List<GenerateFlowPendingParameter>();
        }

        return parameters.Select(item => new GenerateFlowPendingParameter
        {
            OperatorId = item.OperatorId,
            ActualOperatorId = item.ActualOperatorId,
            ParameterNames = item.ParameterNames?.Distinct(StringComparer.OrdinalIgnoreCase).ToList() ?? new List<string>()
        }).ToList();
    }

    private static List<GenerateFlowMissingResource> MapMissingResources(IReadOnlyCollection<AiMissingResourceInfo>? resources)
    {
        if (resources == null || resources.Count == 0)
        {
            return new List<GenerateFlowMissingResource>();
        }

        return resources.Select(item => new GenerateFlowMissingResource
        {
            ResourceType = item.ResourceType,
            ResourceKey = item.ResourceKey,
            Description = item.Description
        }).ToList();
    }

    private static GenerateFlowManualRetry? MapManualRetry(AiManualRetryInfo? manualRetry)
    {
        if (manualRetry == null)
        {
            return null;
        }

        return new GenerateFlowManualRetry
        {
            Required = manualRetry.Required,
            Stage = manualRetry.Stage,
            Draft = manualRetry.Draft,
            Summary = manualRetry.Summary,
            RepairTarget = manualRetry.RepairTarget,
            LastOutputSummary = manualRetry.LastOutputSummary,
            Diagnostics = manualRetry.Diagnostics.Cast<object>().ToList()
        };
    }

    private static object? MapPromptTrace(object? promptTrace)
    {
        return promptTrace switch
        {
            null => null,
            AiPromptTrace trace => trace.Desensitize(),
            _ => new
            {
                redactionPass = true,
                hidden = true
            }
        };
    }

    private static List<GenerateFlowStageDiagnostic>? MapStageTimeline(IReadOnlyList<AiGenerationStageDiagnostic>? timeline)
    {
        if (timeline == null || timeline.Count == 0)
        {
            return null;
        }

        return timeline.Select(stage => new GenerateFlowStageDiagnostic
        {
            Stage = stage.Stage ?? string.Empty,
            Status = stage.Status ?? "completed",
            Summary = stage.Summary ?? string.Empty,
            DurationMs = stage.DurationMs,
            Metadata = stage.Metadata ?? new Dictionary<string, string>()
        }).ToList();
    }

    private static GenerateFlowPerformanceBudget? BuildPerformanceBudget(
        IReadOnlyList<AiGenerationStageDiagnostic>? timeline,
        int retryCount,
        object? promptTrace)
    {
        if ((timeline == null || timeline.Count == 0) && promptTrace is not AiPromptTrace)
        {
            return null;
        }

        var stages = timeline ?? Array.Empty<AiGenerationStageDiagnostic>();
        var totalDurationMs = stages.Sum(stage => Math.Max(0, stage.DurationMs));
        var slowestStage = stages
            .OrderByDescending(stage => Math.Max(0, stage.DurationMs))
            .FirstOrDefault();
        var (inputTokens, outputTokens) = ResolveTokenEstimates(stages, promptTrace);

        var warnings = new List<string>();
        if (totalDurationMs > 45_000)
        {
            warnings.Add("total_latency_over_45s");
        }

        if ((slowestStage?.DurationMs ?? 0) > 30_000)
        {
            warnings.Add($"slow_stage:{slowestStage!.Stage}");
        }

        if (retryCount > 0)
        {
            warnings.Add("auto_retry_used");
        }

        if (inputTokens + outputTokens > 24_000)
        {
            warnings.Add("token_estimate_over_24k");
        }

        return new GenerateFlowPerformanceBudget
        {
            TotalDurationMs = totalDurationMs,
            StageCount = stages.Count,
            RetryCount = retryCount,
            EstimatedInputTokens = inputTokens,
            EstimatedOutputTokens = outputTokens,
            BudgetStatus = warnings.Count == 0 ? "ok" : "warning",
            SlowestStage = slowestStage?.Stage ?? string.Empty,
            SlowestStageDurationMs = Math.Max(0, slowestStage?.DurationMs ?? 0),
            Warnings = warnings
        };
    }

    private static (int InputTokens, int OutputTokens) ResolveTokenEstimates(
        IReadOnlyList<AiGenerationStageDiagnostic> stages,
        object? promptTrace)
    {
        if (promptTrace is AiPromptTrace trace)
        {
            return (trace.EstimatedInputTokens ?? 0, trace.EstimatedOutputTokens ?? 0);
        }

        var llmStage = stages
            .LastOrDefault(stage => string.Equals(stage.Stage, "llm", StringComparison.OrdinalIgnoreCase));
        if (llmStage?.Metadata == null)
        {
            return (0, 0);
        }

        return (
            TryReadInt(llmStage.Metadata, "estimatedInputTokens"),
            TryReadInt(llmStage.Metadata, "estimatedOutputTokens"));
    }

    private static int TryReadInt(IReadOnlyDictionary<string, string> metadata, string key)
    {
        return metadata.TryGetValue(key, out var raw) &&
               int.TryParse(raw, out var value) &&
               value > 0
            ? value
            : 0;
    }

    private static List<GenerateFlowKnowledgeDiagnostic>? MapKnowledgeDiagnostics(IReadOnlyList<AiValidationDiagnostic>? diagnostics)
    {
        if (diagnostics == null || diagnostics.Count == 0)
        {
            return null;
        }

        return diagnostics.Select(d => new GenerateFlowKnowledgeDiagnostic
        {
            Severity = d.Severity ?? string.Empty,
            Code = d.Code ?? string.Empty,
            Category = d.Category ?? string.Empty,
            Message = d.Message ?? string.Empty,
            RelatedFields = d.RelatedFields?.ToList() ?? new List<string>(),
            OperatorId = d.OperatorId,
            RepairHint = d.RepairHint
        }).ToList();
    }

    private static GenerateFlowRequirementBrief? MapRequirementBrief(AiRequirementBrief? brief)
    {
        if (brief == null)
        {
            return null;
        }

        return new GenerateFlowRequirementBrief
        {
            ScenarioKey = brief.ScenarioKey,
            ScenarioName = brief.ScenarioName,
            IntentType = brief.IntentType,
            RequirementMode = brief.RequirementMode,
            Confidence = brief.Confidence,
            HasOpenQuestions = brief.HasOpenQuestions,
            ClarificationRequired = brief.ClarificationRequired,
            CanGenerateDraftNow = brief.CanGenerateDraftNow,
            DraftRiskLevel = brief.DraftRiskLevel,
            ObjectTypes = brief.ObjectTypes?.ToList() ?? new List<string>(),
            DefectTypes = brief.DefectTypes?.ToList() ?? new List<string>(),
            MeasurementTargets = brief.MeasurementTargets?.ToList() ?? new List<string>(),
            RequiredResources = brief.RequiredResources?.ToList() ?? new List<string>(),
            RequiredFields = brief.RequiredFields?.ToList() ?? new List<string>(),
            KnownFacts = brief.KnownFacts?.ToList() ?? new List<string>(),
            MissingFacts = brief.MissingFacts?.ToList() ?? new List<string>(),
            AttachmentFacts = brief.AttachmentFacts?.ToList() ?? new List<string>(),
            ObjectName = brief.ObjectName,
            ImageSource = brief.ImageSource,
            OutputTarget = brief.OutputTarget,
            DecisionRule = brief.DecisionRule,
            RoiRequirement = brief.RoiRequirement,
            CalibrationRequirement = brief.CalibrationRequirement,
            BlockingClarificationFields = brief.BlockingClarificationFields?.ToList() ?? new List<string>(),
            NonBlockingMissingFields = brief.NonBlockingMissingFields?.ToList() ?? new List<string>(),
            ClarificationQuestions = brief.ClarificationQuestions?
                .Select(question => new GenerateFlowClarificationQuestion
                {
                    Field = question.Field,
                    Question = question.Question,
                    Required = question.Required,
                    Reason = question.Reason,
                    Priority = question.Priority,
                    Options = question.Options?.ToList() ?? new List<string>()
                })
                .ToList() ?? new List<GenerateFlowClarificationQuestion>()
        };
    }
}
