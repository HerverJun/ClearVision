using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ClearVision.Product.Contracts.Messages;
using ClearVision.Product.Core.DTOs;
using ClearVision.Product.Infrastructure.AI.Runtime;
using ClearVision.Product.Infrastructure.AI.Tools;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ClearVision.Product.Infrastructure.AI.Agent;

public interface IVisionAgentPlanPlannerService
{
    Task<VisionAgentPlanModeResult> CreatePlanAsync(
        VisionAgentPlanModeRequest request,
        VisionAgentPlanModeResult ruleBaseline,
        CancellationToken cancellationToken);
}

public interface IVisionAgentPlanCompletionSource
{
    Task<string> CompleteAsync(
        VisionAgentPlanCompletionRequest request,
        CancellationToken cancellationToken);
}

public sealed record VisionAgentPlanCompletionRequest(
    string SystemPrompt,
    List<ChatMessage> Messages,
    string ModelRole);

public sealed class LlmVisionAgentPlanCompletionSource : IVisionAgentPlanCompletionSource
{
    private readonly AiGenerationOrchestrator _orchestrator;

    public LlmVisionAgentPlanCompletionSource(AiGenerationOrchestrator orchestrator)
    {
        _orchestrator = orchestrator;
    }

    public async Task<string> CompleteAsync(
        VisionAgentPlanCompletionRequest request,
        CancellationToken cancellationToken)
    {
        var model = _orchestrator.ResolveModelForRole(request.ModelRole);
        var completion = await _orchestrator.CompleteAsync(
            request.SystemPrompt,
            request.Messages,
            model,
            cancellationToken);
        return completion.Content ?? string.Empty;
    }
}

public sealed class VisionAgentPlanPlannerService : IVisionAgentPlanPlannerService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private static readonly Regex WindowsPathRegex = new(
        @"[A-Za-z]:\\[^\s""'`>,;|]+",
        RegexOptions.Compiled);
    private static readonly Regex ImageBase64Regex = new(
        @"data:image\/[a-zA-Z0-9.+-]+;base64,[A-Za-z0-9+/=\r\n]+",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex LongBase64Regex = new(
        @"(?<![A-Za-z0-9+/])[A-Za-z0-9+/]{96,}={0,2}(?![A-Za-z0-9+/])",
        RegexOptions.Compiled);
    private static readonly Regex SecretRegex = new(
        @"(?i)(sk-[A-Za-z0-9_\-]{12,}|bearer\s+[A-Za-z0-9._~+/=-]{8,}|x-api-key\s*[:=]\s*[^\s,;]+|api[_-]?key\s*[:=]\s*[^\s,;]+|token\s*[:=]\s*[^\s,;]+|secret\s*[:=]\s*[^\s,;]+)",
        RegexOptions.Compiled);
    private static readonly Regex EndpointValueRegex = new(
        @"(?i)\b(baseUrl|base_url|url|endpoint|host)\s*[:=]\s*[""']?[^\s,;""'}]+",
        RegexOptions.Compiled);
    private static readonly Regex UrlRegex = new(
        @"(?i)\bhttps?:\/\/[^\s""'<>|]+",
        RegexOptions.Compiled);
    private static readonly Regex IpAddressRegex = new(
        @"\b(?:\d{1,3}\.){3}\d{1,3}\b",
        RegexOptions.Compiled);
    private static readonly Regex PlcAddressRegex = new(
        @"(?i)\b(DB\d+\.DB[XBWD]\d+|M\d+(?:\.\d+)?|D\d+|plc://[^\s,;]+)\b",
        RegexOptions.Compiled);
    private const int MaxSanitizedErrorMessageChars = 200;
    private const string PlannerFailureStageRequest = "completion_request";
    private const string PlannerFailureStageResponse = "completion_response";
    private const string PlannerFailureStageJsonParse = "json_parse";
    private const string PlannerFailureStageContractRepair = "contract_repair";
    private const string PlannerFailureStageUnknown = "unknown";
    private const string CompletionRequestFailed = "completion_request_failed";
    private const string CompletionEmpty = "completion_empty";
    private const string PlannerJsonParseFailed = "planner_json_parse_failed";
    private const string PlannerJsonRepairFailed = "planner_json_repair_failed";
    private const string PlannerJsonRepairTimeout = "planner_json_repair_timeout";
    private const string PlannerContractRepairFailed = "planner_contract_repair_failed";
    private const string PlannerTimeout = "planner_timeout";
    private const string PlannerUnauthorized = "planner_unauthorized";
    private const string PlannerUnknownError = "planner_unknown_error";

    private readonly IVisionAgentPlanCompletionSource _completionSource;
    private readonly VisionAgentPlanPromptComposer _promptComposer;
    private readonly VisionAgentPlanPlannerOptions _options;
    private readonly Microsoft.Extensions.Logging.ILogger<VisionAgentPlanPlannerService> _logger;

    public VisionAgentPlanPlannerService(
        IVisionAgentPlanCompletionSource completionSource,
        VisionAgentPlanPromptComposer promptComposer,
        IOptions<VisionAgentPlanPlannerOptions>? options,
        Microsoft.Extensions.Logging.ILogger<VisionAgentPlanPlannerService> logger)
    {
        _completionSource = completionSource;
        _promptComposer = promptComposer;
        _options = (options?.Value ?? new VisionAgentPlanPlannerOptions()).Normalize();
        _logger = logger;
    }

    public async Task<VisionAgentPlanModeResult> CreatePlanAsync(
        VisionAgentPlanModeRequest request,
        VisionAgentPlanModeResult ruleBaseline,
        CancellationToken cancellationToken)
    {
        var events = new List<VisionAgentPlanPublicEvent>();
        events.AddRange(ruleBaseline.PublicEvents);
        events.Add(
            Event("collecting_context", "completed", "上下文收集完成",
                "已收集公开需求、流程、模板、附件、算子和工站边界元数据。",
                new()
                {
                    ["hasCurrentFlow"] = ruleBaseline.ContextSummary.HasCurrentFlow.ToString().ToLowerInvariant(),
                    ["attachmentCount"] = ruleBaseline.ContextSummary.AttachmentCount.ToString(),
                    ["templateSelectionMode"] = ruleBaseline.ContextSummary.TemplateSelectionMode
                }));

        if (!_options.Enabled)
        {
            return BuildFallback(
                ruleBaseline,
                "planner_disabled",
                events,
                "Planner 生成未启用，已使用规则兜底方案。");
        }

        VisionAgentPlanPrompt prompt;
        try
        {
            prompt = _promptComposer.Compose(request, ruleBaseline, _options);
        }
        catch (Exception ex)
        {
            var diagnostic = BuildDiagnostic(
                PlannerFailureStageUnknown,
                PlannerUnknownError,
                "planner_prompt_compose_failed",
                "Planner 请求准备失败，已使用规则兜底方案。",
                ex);
            LogPlannerFailure(diagnostic);
            events.Add(Event("planning_with_model", "failed", "模型规划失败",
                "模型规划失败，已使用规则兜底方案。",
                BuildDiagnosticMetadata("planner_failed", diagnostic)));
            return BuildFallback(
                ruleBaseline,
                "planner_failed",
                events,
                "模型规划失败，已使用规则兜底方案。",
                diagnostic);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));
        events.Add(Event("planning_with_model", "started", "模型规划已开始",
            "模型正在生成结构化 PlanModeResult 候选。",
            new()
            {
                ["modelRole"] = _options.ModelRole,
                ["metadataOnly"] = "true"
            }));

        string completion;
        try
        {
            completion = await _completionSource.CompleteAsync(
                new VisionAgentPlanCompletionRequest(prompt.SystemPrompt, prompt.Messages, _options.ModelRole),
                timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            var diagnostic = BuildDiagnostic(
                PlannerFailureStageRequest,
                PlannerTimeout,
                PlannerTimeout,
                "模型规划超时，已使用规则兜底方案。",
                null);
            LogPlannerFailure(diagnostic);
            events.Add(Event("planning_with_model", "failed", "模型规划超时",
                "模型规划超时，已使用规则兜底方案。",
                BuildDiagnosticMetadata("planner_timeout", diagnostic)));
            return BuildFallback(
                ruleBaseline,
                "planner_timeout",
                events,
                "模型规划超时，已使用规则兜底方案。",
                diagnostic);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
        {
            var diagnostic = BuildDiagnostic(
                PlannerFailureStageRequest,
                PlannerUnauthorized,
                PlannerUnauthorized,
                "模型规划鉴权失败，已使用规则兜底，请检查 Planner API Key、模型名和接口配置。",
                ex);
            LogPlannerFailure(diagnostic);
            events.Add(Event("planning_with_model", "failed", "模型规划鉴权失败",
                "模型规划鉴权失败，已使用规则兜底，请检查 Planner API Key/接口/模型名。",
                BuildDiagnosticMetadata("planner_unauthorized", diagnostic)));
            return BuildFallback(
                ruleBaseline,
                "planner_unauthorized",
                events,
                "模型规划鉴权失败，已使用规则兜底，请检查 Planner API Key/接口/模型名。",
                diagnostic);
        }
        catch (Exception ex)
        {
            var diagnostic = BuildDiagnostic(
                PlannerFailureStageRequest,
                CompletionRequestFailed,
                CompletionRequestFailed,
                "Planner 模型请求失败，请检查网络、Planner 接口地址配置、模型服务和中转站状态。",
                ex);
            LogPlannerFailure(diagnostic);
            events.Add(Event("planning_with_model", "failed", "模型请求失败",
                "Planner 模型请求失败，已使用规则兜底方案。",
                BuildDiagnosticMetadata("planner_failed", diagnostic)));
            return BuildFallback(
                ruleBaseline,
                "planner_failed",
                events,
                "模型规划失败，已使用规则兜底方案。",
                diagnostic);
        }

        if (string.IsNullOrWhiteSpace(completion))
        {
            var diagnostic = BuildDiagnostic(
                PlannerFailureStageResponse,
                CompletionEmpty,
                CompletionEmpty,
                "Planner 模型返回空内容，已使用规则兜底方案。",
                null);
            LogPlannerFailure(diagnostic);
            events.Add(Event("planning_with_model", "failed", "模型返回为空",
                "Planner 模型返回空内容，已使用规则兜底方案。",
                BuildDiagnosticMetadata("planner_failed", diagnostic)));
            return BuildFallback(
                ruleBaseline,
                "planner_failed",
                events,
                "模型规划失败，已使用规则兜底方案。",
                diagnostic);
        }

        events.Add(Event("planning_with_model", "completed", "模型规划候选已返回",
            "模型已返回公开结构化候选，等待校验。"));

        events.Add(Event("validating_plan_contract", "started", "校验规划契约",
            "正在校验 JSON 结构、问题质量、算子目录和模板约束。"));
        var completionTooLarge = completion.Length > _options.MaxCompletionChars;
        if (completionTooLarge)
        {
            completion = BoundCompletion(completion, _options.MaxCompletionChars);
            events.Add(Event("completion_too_large", "completed", "Planner completion truncated",
                "Planner completion exceeded MaxCompletionChars and was safely truncated before validation.",
                new()
                {
                    ["maxCompletionChars"] = _options.MaxCompletionChars.ToString(),
                    ["metadataOnly"] = "true"
                }));
        }

        VisionAgentPlannerCandidate candidate;
        try
        {
            candidate = ParseCandidate(completion);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            var parseDiagnostic = BuildDiagnostic(
                PlannerFailureStageJsonParse,
                PlannerJsonParseFailed,
                PlannerJsonParseFailed,
                "Planner returned content that could not be parsed as PlannerCandidate JSON.",
                ex);
            var repairAttempt = await TryRepairJsonAsync(
                request,
                ruleBaseline,
                completion,
                parseDiagnostic,
                events,
                timeout.Token,
                cancellationToken);
            if (!string.IsNullOrWhiteSpace(repairAttempt.Completion))
            {
                try
                {
                    candidate = ParseCandidate(repairAttempt.Completion);
                    goto PlannerCandidateParsed;
                }
                catch (Exception repairEx) when (repairEx is JsonException or InvalidOperationException or FormatException)
                {
                    events.Add(Event("planner_json_repair_failed", "failed", "Planner JSON repair failed",
                        "Planner JSON repair returned content that still could not be parsed.",
                        BuildDiagnosticMetadata("planner_failed", BuildDiagnostic(
                            PlannerFailureStageJsonParse,
                            PlannerJsonRepairFailed,
                            PlannerJsonRepairFailed,
                            "Planner JSON repair failed.",
                            repairEx))));
                }
            }

            var diagnostic = repairAttempt.Diagnostic ?? BuildDiagnostic(
                PlannerFailureStageJsonParse,
                PlannerJsonParseFailed,
                PlannerJsonParseFailed,
                "Planner 返回内容无法解析为 PlanModeResult JSON，请检查 Planner 模型是否按 PlanModeResult JSON 契约输出。",
                ex);
            LogPlannerFailure(diagnostic);
            events.Add(Event("validating_plan_contract", "failed", "JSON 解析失败",
                "Planner 返回内容无法解析为合法 JSON，已使用规则兜底方案。",
                BuildDiagnosticMetadata(repairAttempt.FallbackReason ?? "planner_failed", diagnostic)));
            return BuildFallback(
                ruleBaseline,
                repairAttempt.FallbackReason ?? "planner_failed",
                events,
                "模型规划失败，已使用规则兜底方案。",
                diagnostic);
        }

PlannerCandidateParsed:
        VisionAgentPlanModeResult repaired;
        List<string> repairNotes;
        List<string> warnings;
        try
        {
            repaired = RepairCandidate(candidate, request, ruleBaseline, out repairNotes, out warnings);
            if (completionTooLarge)
            {
                warnings.Add("completion_too_large");
                repairNotes.Add("completion_truncated_to_max_completion_chars");
            }
        }
        catch (Exception ex)
        {
            var diagnostic = BuildDiagnostic(
                PlannerFailureStageContractRepair,
                PlannerContractRepairFailed,
                PlannerContractRepairFailed,
                "Planner JSON 可解析但未通过 PlanModeResult 契约修复，请检查输出字段完整性。",
                ex);
            LogPlannerFailure(diagnostic);
            events.Add(Event("validating_plan_contract", "failed", "契约修复失败",
                "Planner JSON 可解析但未通过契约修复，已使用规则兜底方案。",
                BuildDiagnosticMetadata("planner_failed", diagnostic)));
            return BuildFallback(
                ruleBaseline,
                "planner_failed",
                events,
                "模型规划失败，已使用规则兜底方案。",
                diagnostic);
        }

        events.Add(Event("validating_plan_contract", "completed", "规划契约已校验",
            "模型规划已归一到公开 PlanModeResult 契约。",
            new()
            {
                ["repairCount"] = repairNotes.Count.ToString(),
                ["warningCount"] = warnings.Count.ToString()
            }));
        events.Add(Event("applying_safety_constraints", "completed", "安全约束已应用",
            "已应用脱敏、元数据边界、资源占位和 PLC 安全策略。"));

        var result = repaired with
        {
            PlanSource = "model_planner",
            FallbackReason = string.Empty,
            PlannerFailureStage = string.Empty,
            PlannerFailureCode = string.Empty,
            SanitizedErrorKind = string.Empty,
            SanitizedErrorMessage = string.Empty,
            PlanWarnings = warnings,
            ContractRepairNotes = repairNotes,
            PublicEvents = [.. events, Event("plan_ready", "completed", "规划已就绪",
                "Planner 规划已就绪，等待用户确认。")],
            MetadataOnly = true
        };
        return result with
        {
            PlanHash = VisionAgentOrchestrator.ComputePlanHash(result)
        };
    }

    private static VisionAgentPlannerCandidate ParseCandidate(string completion)
    {
        var json = ExtractJsonObject(completion);
        return JsonSerializer.Deserialize<VisionAgentPlannerCandidate>(json, JsonOptions)
            ?? throw new InvalidOperationException("Planner returned an empty plan object.");
    }

    private static VisionAgentPlanModeResult RepairCandidate(
        VisionAgentPlannerCandidate candidate,
        VisionAgentPlanModeRequest request,
        VisionAgentPlanModeResult baseline,
        out List<string> repairNotes,
        out List<string> warnings)
    {
        repairNotes = [];
        warnings = [];

        var templateSelection = ConstrainTemplateSelection(
            null,
            baseline.TemplateSelection,
            repairNotes);
        var route = RepairRoute(candidate.RecommendedRoute, baseline.RecommendedRoute, repairNotes);
        var questions = RepairQuestions(candidate.ClarificationQuestions, baseline, request, repairNotes);
        var defaults = RepairDefaults(candidate.RecommendedDefaults, baseline.RecommendedDefaults, repairNotes);
        var understanding = NormalizeList(candidate.RequirementUnderstanding, baseline.RequirementUnderstanding);
        var risks = NormalizeList(candidate.Risks, baseline.Risks);
        var acceptance = NormalizeList(candidate.AcceptanceCriteria, baseline.AcceptanceCriteria);
        var executablePlan = NormalizeList(candidate.ExecutablePlan, baseline.ExecutablePlan);
        var blockingReasons = NormalizeList(candidate.BlockingReasons, []);
        var classifiedPlannerBlocking = ClassifyPlannerBlockingReasons(
            blockingReasons,
            candidate.CanBuildCandidate,
            questions);
        var semantic = VisionAgentSemanticExtractionSafety.Sanitize(baseline.SemanticExtraction);

        var redactionNotes = new List<string>();
        var result = new VisionAgentPlanModeResult
        {
            PlanContractVersion = VisionAgentPlanContractVersions.V2,
            PlanId = baseline.PlanId,
            CurrentPhase = baseline.CurrentPhase,
            OriginalUserPrompt = SafeText(
                string.IsNullOrWhiteSpace(baseline.OriginalUserPrompt)
                    ? request.OriginalUserPrompt ?? request.Description
                    : baseline.OriginalUserPrompt,
                redactionNotes),
            Goal = SafeText(
                string.IsNullOrWhiteSpace(candidate.Goal) ? baseline.Goal : candidate.Goal,
                redactionNotes),
            Intent = SafeToken(
                string.IsNullOrWhiteSpace(candidate.Intent) ? baseline.Intent : candidate.Intent,
                redactionNotes),
            Confidence = NormalizeConfidence(candidate.Confidence, baseline.Confidence),
            RequirementUnderstanding = SanitizeList(understanding, redactionNotes),
            RecommendedRoute = SanitizeRoute(route, redactionNotes),
            ClarificationQuestions = questions.Select(question => SanitizeQuestion(question, redactionNotes)).ToList(),
            RecommendedDefaults = defaults.Select(item => SanitizeDefault(item, redactionNotes)).ToList(),
            Risks = SanitizeList(risks, redactionNotes),
            AcceptanceCriteria = SanitizeList(acceptance, redactionNotes),
            ExecutablePlan = SanitizeList(executablePlan, redactionNotes),
            CanBuild = false,
            BlockingReasons = SanitizeList(classifiedPlannerBlocking, redactionNotes),
            SemanticExtraction = semantic,
            ConfirmedPlanAnswers = baseline.ConfirmedPlanAnswers ?? [],
            ResolvedPlanFields = baseline.ResolvedPlanFields ?? [],
            RemainingPlanFields = baseline.RemainingPlanFields ?? [],
            NextAction = SafeText(
                string.IsNullOrWhiteSpace(candidate.NextAction) ? baseline.NextAction : candidate.NextAction,
                redactionNotes),
            ContextSummary = baseline.ContextSummary,
            OperatorCatalogVersion = baseline.OperatorCatalogVersion,
            TemplateCatalogVersion = baseline.TemplateCatalogVersion,
            TemplateSelection = templateSelection,
            StationBoundarySummary = baseline.StationBoundarySummary,
            PlcOutputPolicy = NormalizePlcPolicy(string.Empty, baseline.PlcOutputPolicy, redactionNotes),
            MetadataOnly = true
        };

        var alignedRemaining = (result.RemainingPlanFields ?? [])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        result = result with
        {
            RemainingPlanFields = alignedRemaining
        };

        var maturityRequest = new VisionAgentRequirementMaturityRequest
        {
            Description = request.Description,
            AdditionalContext = request.AdditionalContext,
            Mode = request.Mode,
            HasCurrentFlow = !string.IsNullOrWhiteSpace(request.CurrentFlowSnapshot),
            TemplateSelection = baseline.TemplateSelection
        };
        var updatedMaturity = VisionAgentRequirementMaturityGate.Evaluate(maturityRequest, semantic) with
        {
            MissingFields = alignedRemaining
        };
        var maturity = updatedMaturity;
        if (!maturity.CanPlan)
        {
            var blockedReadiness = VisionAgentPlanReadinessEvaluator.Evaluate(result with
            {
                RequirementMaturity = maturity,
                BlockingReasons = maturity.BlockingReasons.Count > 0
                    ? maturity.BlockingReasons.ToList()
                    : baseline.BlockingReasons.ToList()
            });
            result = result with
            {
                Intent = maturity.Maturity,
                CanBuild = false,
                RecommendedRoute = baseline.RecommendedRoute,
                BlockingReasons = blockedReadiness.Blockers
                    .Where(blocker => blocker.BlocksBuild)
                    .Select(blocker => blocker.Id)
                    .DefaultIfEmpty("hard_requirement:inspection_goal_missing")
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(12)
                    .ToList(),
                BuildReadiness = blockedReadiness,
                RequirementMaturity = maturity,
                DecisionTrace = VisionAgentRequirementMaturityGate.BuildDecisionTrace(
                    maturityRequest,
                    maturity,
                    "ambiguous_vision_requirement",
                    "clarifying",
                    string.Empty)
            };
            repairNotes.Add("maturity_gate_applied");
        }
        else
        {
            var readiness = VisionAgentPlanReadinessEvaluator.Evaluate(result with { RequirementMaturity = maturity });
            var buildBlockingReasons = readiness.Blockers
                .Where(blocker => blocker.BlocksBuild)
                .Select(blocker => blocker.Id)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(12)
                .ToList();
            var canBuild = baseline.CanBuild && readiness.CanBuild;
            result = result with
            {
                CanBuild = canBuild,
                BlockingReasons = buildBlockingReasons.Count > 0
                        ? buildBlockingReasons
                        : readiness.Blockers
                            .Where(blocker => !blocker.BlocksBuild)
                            .Select(blocker => blocker.Id)
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .Take(12)
                            .ToList(),
                BuildReadiness = readiness,
                RequirementMaturity = maturity,
                DecisionTrace = VisionAgentRequirementMaturityGate.BuildDecisionTrace(
                    maturityRequest,
                    maturity,
                    "actionable_vision_plan",
                    "planning",
                    string.Empty)
            };
            if (!canBuild)
            {
                repairNotes.Add("plan_build_readiness_blocked");
            }
        }

        if (!result.CanBuild && result.BlockingReasons.Count == 0)
        {
            result.BlockingReasons.Add(maturity.CanPlan ? "contract_warning:build_requirement_missing" : "hard_requirement:inspection_goal_missing");
            repairNotes.Add("blocking_reason_added");
        }

        if (redactionNotes.Count > 0)
        {
            warnings.AddRange(redactionNotes.Distinct(StringComparer.OrdinalIgnoreCase));
            repairNotes.Add("unsafe_text_redacted");
        }

        return result;
    }

    private async Task<JsonRepairAttempt> TryRepairJsonAsync(
        VisionAgentPlanModeRequest request,
        VisionAgentPlanModeResult ruleBaseline,
        string invalidCompletion,
        PlannerFailureDiagnostic parseDiagnostic,
        List<VisionAgentPlanPublicEvent> events,
        CancellationToken repairCancellationToken,
        CancellationToken callerCancellationToken)
    {
        events.Add(Event("planner_json_repair_started", "started", "Planner JSON repair started",
            "Planner JSON parse failed; requesting one sanitized repair completion.",
            BuildDiagnosticMetadata("planner_json_repair", parseDiagnostic)));

        try
        {
            var repairPrompt = _promptComposer.ComposeRepair(
                SanitizeCompletionSummary(invalidCompletion),
                parseDiagnostic,
                request,
                ruleBaseline);
            var repaired = await _completionSource.CompleteAsync(
                new VisionAgentPlanCompletionRequest(
                    repairPrompt.SystemPrompt,
                    repairPrompt.Messages,
                    _options.ModelRole),
                repairCancellationToken);
            if (string.IsNullOrWhiteSpace(repaired))
            {
                events.Add(Event("planner_json_repair_failed", "failed", "Planner JSON repair empty",
                    "Planner JSON repair returned empty content.",
                    BuildDiagnosticMetadata("planner_json_repair_empty", parseDiagnostic)));
                return new JsonRepairAttempt(
                    string.Empty,
                    parseDiagnostic,
                    "planner_failed");
            }

            var tooLarge = repaired.Length > _options.MaxCompletionChars;
            var bounded = BoundCompletion(repaired, _options.MaxCompletionChars);
            events.Add(Event("planner_json_repair_completed", "completed", "Planner JSON repair completed",
                "Planner JSON repair returned a bounded candidate for contract validation.",
                new()
                {
                    ["completionTooLarge"] = tooLarge.ToString().ToLowerInvariant(),
                    ["metadataOnly"] = "true"
                }));
            return new JsonRepairAttempt(bounded, null, null);
        }
        catch (OperationCanceledException) when (!callerCancellationToken.IsCancellationRequested)
        {
            var diagnostic = BuildDiagnostic(
                PlannerFailureStageJsonParse,
                PlannerJsonRepairTimeout,
                PlannerJsonRepairTimeout,
                "Planner JSON repair request timed out.",
                null);
            LogPlannerFailure(diagnostic);
            events.Add(Event("planner_json_repair_timeout", "failed", "Planner JSON repair timeout",
                "Planner JSON repair timed out; rule fallback will be used.",
                BuildDiagnosticMetadata(PlannerJsonRepairTimeout, diagnostic)));
            return new JsonRepairAttempt(
                string.Empty,
                diagnostic,
                PlannerJsonRepairTimeout);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            var diagnostic = BuildDiagnostic(
                PlannerFailureStageJsonParse,
                PlannerJsonRepairFailed,
                PlannerJsonRepairFailed,
                "Planner JSON repair request failed.",
                ex);
            LogPlannerFailure(diagnostic);
            events.Add(Event("planner_json_repair_failed", "failed", "Planner JSON repair failed",
                "Planner JSON repair request failed; rule fallback will be used.",
                BuildDiagnosticMetadata("planner_failed", diagnostic)));
            return new JsonRepairAttempt(
                string.Empty,
                diagnostic,
                "planner_failed");
        }
    }

    private sealed record JsonRepairAttempt(
        string Completion,
        PlannerFailureDiagnostic? Diagnostic,
        string? FallbackReason);

    internal sealed record PlannerFailureDiagnostic(
        string Stage,
        string Code,
        string SanitizedErrorKind,
        string SanitizedErrorMessage);

    private static VisionAgentPlanModeResult BuildFallback(
        VisionAgentPlanModeResult baseline,
        string reason,
        List<VisionAgentPlanPublicEvent> events,
        string summary,
        PlannerFailureDiagnostic? diagnostic = null)
    {
        var planWarnings = new List<string> { summary };
        var contractRepairNotes = new List<string>();
        if (diagnostic != null)
        {
            planWarnings.Add(diagnostic.SanitizedErrorMessage);
            contractRepairNotes.Add($"planner_failure_stage:{diagnostic.Stage}");
            contractRepairNotes.Add($"planner_failure_code:{diagnostic.Code}");
            contractRepairNotes.Add($"sanitized_error_kind:{diagnostic.SanitizedErrorKind}");
        }

        var result = baseline with
        {
            PlanSource = "rule_fallback",
            FallbackReason = reason,
            PlannerFailureStage = diagnostic?.Stage ?? string.Empty,
            PlannerFailureCode = diagnostic?.Code ?? string.Empty,
            SanitizedErrorKind = diagnostic?.SanitizedErrorKind ?? string.Empty,
            SanitizedErrorMessage = diagnostic?.SanitizedErrorMessage ?? string.Empty,
            PlanWarnings = planWarnings
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            ContractRepairNotes = contractRepairNotes,
            PublicEvents =
            [
                .. events,
                Event("rule_fallback_used", "completed", "已启用规则兜底", summary,
                    BuildDiagnosticMetadata(reason, diagnostic)),
                Event("plan_ready", "completed", "兜底规划已就绪",
                    "规则兜底规划已就绪，等待用户确认。")
            ],
            MetadataOnly = true
        };
        return result with
        {
            PlanHash = VisionAgentOrchestrator.ComputePlanHash(result)
        };
    }

    private static PlannerFailureDiagnostic BuildDiagnostic(
        string stage,
        string code,
        string kind,
        string publicSummary,
        Exception? exception)
    {
        var sanitized = SafeErrorSummary(publicSummary, exception);
        return new PlannerFailureDiagnostic(
            SafeIdentifier(stage, PlannerFailureStageUnknown),
            SafeIdentifier(code, PlannerUnknownError),
            SafeIdentifier(kind, PlannerUnknownError),
            sanitized);
    }

    private static Dictionary<string, string> BuildDiagnosticMetadata(
        string fallbackReason,
        PlannerFailureDiagnostic? diagnostic)
    {
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["fallbackReason"] = SafeIdentifier(fallbackReason, "planner_failed")
        };
        if (diagnostic == null)
        {
            return metadata;
        }

        metadata["plannerFailureStage"] = diagnostic.Stage;
        metadata["plannerFailureCode"] = diagnostic.Code;
        metadata["sanitizedErrorKind"] = diagnostic.SanitizedErrorKind;
        metadata["sanitizedErrorMessage"] = diagnostic.SanitizedErrorMessage;
        return metadata;
    }

    private void LogPlannerFailure(PlannerFailureDiagnostic diagnostic)
    {
        _logger.LogWarning(
            "Vision Agent Plan Planner failed; rule fallback will be used. Stage={Stage} Code={Code} Kind={Kind} Summary={Summary}",
            diagnostic.Stage,
            diagnostic.Code,
            diagnostic.SanitizedErrorKind,
            diagnostic.SanitizedErrorMessage);
    }

    private static VisionAgentRecommendedRoute RepairRoute(
        VisionAgentRecommendedRoute? candidate,
        VisionAgentRecommendedRoute baseline,
        List<string> repairNotes)
    {
        if (candidate == null)
        {
            repairNotes.Add("recommended_route_repaired_to_baseline");
            candidate = baseline;
        }

        var allowed = AllowedOperatorTypes();
        var candidateOperators = (candidate.Operators ?? [])
            .Select(Clean)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToList();
        var repairedOperators = candidateOperators
            .Where(op => allowed.Contains(op))
            .ToList();
        if (candidateOperators.Count == 0 || repairedOperators.Count != candidateOperators.Count)
        {
            repairedOperators = baseline.Operators;
            repairNotes.Add("operator_pipeline_repaired_to_catalog");
        }

        if (repairedOperators.Count == 0)
        {
            repairedOperators = ["ImageAcquisition", "ResultJudgment", "ResultOutput"];
            repairNotes.Add("operator_pipeline_minimum_added");
        }

        return new VisionAgentRecommendedRoute
        {
            RouteId = SafeIdentifier(candidate.RouteId, baseline.RouteId),
            Title = FallbackText(candidate.Title, baseline.Title),
            Summary = FallbackText(candidate.Summary, baseline.Summary),
            Operators = repairedOperators,
            TemplateDecision = FallbackText(candidate.TemplateDecision, baseline.TemplateDecision)
        };
    }

    private static List<VisionAgentClarificationQuestion> RepairQuestions(
        List<VisionAgentClarificationQuestion>? candidate,
        VisionAgentPlanModeResult baselinePlan,
        VisionAgentPlanModeRequest request,
        List<string> repairNotes)
    {
        var confirmedAnswers = baselinePlan.ConfirmedPlanAnswers ?? [];
        var resolvedFields = baselinePlan.ResolvedPlanFields ?? [];
        var remainingFields = baselinePlan.RemainingPlanFields ?? [];
        var rawCandidate = candidate ?? [];
        if (rawCandidate.Any(question => (question.Options ?? []).Count(option =>
                !string.IsNullOrWhiteSpace(option.Value) &&
                !string.IsNullOrWhiteSpace(option.Label)) < 2))
        {
            repairNotes.Add("clarification_question_options_repaired");
        }
        var repairedPlannerQuestions = rawCandidate
            .Select(RepairQuestion)
            .Where(question => !string.IsNullOrWhiteSpace(question.Id) &&
                               !string.IsNullOrWhiteSpace(question.Title) &&
                               question.Options.Count > 0)
            .Take(5)
            .ToList();
        var filteredPlannerQuestions = VisionAgentPlanFieldPolicy.NormalizeQuestions(
            repairedPlannerQuestions,
            remainingFields,
            resolvedFields,
            confirmedAnswers);
        if (filteredPlannerQuestions.Count > 0)
        {
            return filteredPlannerQuestions.Take(3).ToList();
        }
        if (remainingFields.Count == 0 &&
            baselinePlan.ClarificationQuestions.Count > 0 &&
            repairedPlannerQuestions.Count > 0)
        {
            // Legacy standalone baselines can carry questions without v2 remaining fields.
            // Real v2 plans use RemainingPlanFields as authority and take the filtered path above.
            return repairedPlannerQuestions.Take(3).ToList();
        }

        if (baselinePlan.ClarificationQuestions.Count > 0)
        {
            repairNotes.Add("clarification_questions_repaired_to_baseline");
            return baselinePlan.ClarificationQuestions
                .Select(RepairQuestion)
                .Where(question => !string.IsNullOrWhiteSpace(question.Id) &&
                                   !string.IsNullOrWhiteSpace(question.Title))
                .Take(3)
                .ToList();
        }

        var filteredQuestions = VisionAgentPlanFieldPolicy.NormalizeQuestions(
            candidate,
            remainingFields,
            resolvedFields,
            confirmedAnswers);

        var confirmedSet = confirmedAnswers
            .Select(a => VisionAgentPlanFieldPolicy.NormalizeField(a.Field))
            .Where(f => !string.IsNullOrWhiteSpace(f))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var resolvedSet = resolvedFields
            .Select(VisionAgentPlanFieldPolicy.NormalizeField)
            .Where(f => !string.IsNullOrWhiteSpace(f))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var remainingSet = remainingFields
            .Select(VisionAgentPlanFieldPolicy.NormalizeField)
            .Where(f => !string.IsNullOrWhiteSpace(f))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var allowedSet = remainingSet
            .Except(resolvedSet)
            .Except(confirmedSet)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (filteredQuestions.Count == 0 && allowedSet.Count > 0)
        {
            repairNotes.Add("clarification_questions_repaired_to_fallback_inputs");
            filteredQuestions = VisionAgentPlanFieldPolicy.BuildFallbackQuestionsForRemaining(allowedSet);
        }

        return filteredQuestions.Take(3).ToList();
    }

    private static VisionAgentClarificationQuestion RepairQuestion(VisionAgentClarificationQuestion question)
    {
        var field = VisionAgentPlanFieldPolicy.ResolveQuestionField(question);
        var options = VisionAgentPlanFieldPolicy.NormalizeQuestionOptions(field, question.Options);
        var recommended = options.FirstOrDefault(option =>
                              option.Recommended &&
                              VisionAgentPlanFieldPolicy.IsResolveFieldOption(option))?.Value ??
                          options.FirstOrDefault(option =>
                              VisionAgentPlanFieldPolicy.IsResolveFieldOption(option))?.Value ??
                          options.FirstOrDefault()?.Value ??
                          question.DefaultValue;
        return question with
        {
            Id = SafeIdentifier(question.Id, "clarification"),
            Field = field,
            Title = FallbackText(question.Title, "关键澄清问题"),
            Why = FallbackText(question.Why, "这会影响算子链、参数或发布就绪。"),
            DefaultValue = FallbackText(question.DefaultValue, recommended),
            DefaultAssumption = FallbackText(question.DefaultAssumption, "使用推荐选项作为仅元数据默认值。"),
            Impact = FallbackText(question.Impact, "修改该选项会改变构建假设。"),
            Options = options
        };
    }

    private static List<VisionAgentDefaultAssumption> RepairDefaults(
        List<VisionAgentDefaultAssumption>? candidate,
        List<VisionAgentDefaultAssumption> baseline,
        List<string> repairNotes)
    {
        var defaults = (candidate ?? [])
            .Where(item => !string.IsNullOrWhiteSpace(item.Id) &&
                           !string.IsNullOrWhiteSpace(item.Label))
            .Take(8)
            .ToList();
        if (defaults.Count == 0)
        {
            defaults = baseline;
            repairNotes.Add("recommended_defaults_repaired_to_baseline");
        }

        if (defaults.All(item => !string.Equals(item.Id, "metadata_only", StringComparison.OrdinalIgnoreCase)))
        {
            defaults.Insert(0, new VisionAgentDefaultAssumption
            {
                Id = "metadata_only",
                Label = "仅公开诊断",
                Value = "redacted_metadata",
                Impact = "不会暴露原始路径、图像字节、令牌、提示词或工站网络细节。"
            });
            repairNotes.Add("metadata_only_default_added");
        }

        return defaults.Take(8).ToList();
    }

    private static AiTemplateSelectionInfo? ConstrainTemplateSelection(
        AiTemplateSelectionInfo? candidate,
        AiTemplateSelectionInfo? baseline,
        List<string> repairNotes)
    {
        if (baseline != null)
        {
            if (candidate != null &&
                (!string.Equals(Clean(candidate.TemplateId), Clean(baseline.TemplateId), StringComparison.OrdinalIgnoreCase) ||
                 !string.Equals(Clean(candidate.Mode), Clean(baseline.Mode), StringComparison.OrdinalIgnoreCase)))
            {
                repairNotes.Add("template_selection_repaired_to_user_selection");
            }

            return RedactTemplateSelection(baseline);
        }

        return RedactTemplateSelection(candidate);
    }

    private static AiTemplateSelectionInfo? RedactTemplateSelection(AiTemplateSelectionInfo? selection)
    {
        var mode = SafeOptionalIdentifier(selection?.Mode, string.Empty).ToLowerInvariant();
        var templateId = SafeOptionalIdentifier(selection?.TemplateId, "redacted_template");
        var scenarioKey = SafeOptionalIdentifier(selection?.ScenarioKey, string.Empty);
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

    private static string NormalizePlcPolicy(string candidate, string baseline, List<string> notes)
    {
        var value = SafeText(FallbackText(candidate, baseline), notes);
        if (LooksLikeUnsafe(value))
        {
            notes.Add("plc_or_station_detail_redacted");
            return baseline;
        }

        return value;
    }

    private static string ExtractJsonObject(string value)
    {
        var text = (value ?? string.Empty).Trim();
        if (text.StartsWith("```", StringComparison.Ordinal))
        {
            var firstNewLine = text.IndexOf('\n');
            var lastFence = text.LastIndexOf("```", StringComparison.Ordinal);
            if (firstNewLine >= 0 && lastFence > firstNewLine)
            {
                text = text[(firstNewLine + 1)..lastFence].Trim();
            }
        }

        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start < 0 || end <= start)
        {
            throw new InvalidOperationException("Planner did not return a JSON object.");
        }

        return text[start..(end + 1)];
    }

    private static VisionAgentPlanPublicEvent Event(
        string stage,
        string status,
        string title,
        string summary,
        Dictionary<string, string>? metadata = null)
    {
        return new VisionAgentPlanPublicEvent
        {
            Stage = stage,
            Status = status,
            Title = title,
            Summary = summary,
            Metadata = metadata ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            MetadataOnly = true
        };
    }

    private static HashSet<string> AllowedOperatorTypes()
    {
        return new VisionAgentOperatorContractCatalog().OperatorTypes
            .Where(type => !string.Equals(type, "ModbusCommunication", StringComparison.OrdinalIgnoreCase) &&
                           !string.Equals(type, "HttpRequest", StringComparison.OrdinalIgnoreCase) &&
                           !string.Equals(type, "ScriptOperator", StringComparison.OrdinalIgnoreCase))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static List<string> NormalizeList(List<string>? candidate, List<string> baseline)
    {
        var values = candidate?
            .Select(Clean)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Take(12)
            .ToList() ?? [];
        return values.Count > 0 ? values : baseline;
    }

    private static List<string> ClassifyPlannerBlockingReasons(
        IEnumerable<string> reasons,
        bool canBuildCandidate,
        IEnumerable<VisionAgentClarificationQuestion>? questions)
    {
        var classified = reasons
            .Select(ClassifyPlannerBlockingReason)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(12)
            .ToList();
        if (!canBuildCandidate &&
            classified.Count == 0)
        {
            var strategyQuestionId = questions?
                .FirstOrDefault(question =>
                    VisionAgentStrategyConfirmationSupport.IsStrategyQuestionId(question.Id) ||
                    VisionAgentStrategyConfirmationSupport.IsStrategyQuestionId(question.Field))
                ?.Id;
            classified.Add(string.IsNullOrWhiteSpace(strategyQuestionId)
                ? "contract_warning:planner_candidate_not_buildable"
                : $"strategy_confirmation:{strategyQuestionId}_missing");
        }

        return classified;
    }

    private static string ClassifyPlannerBlockingReason(string reason)
    {
        var raw = Clean(reason);
        foreach (var prefix in new[] { "hard_requirement:", "strategy_confirmation:", "resource_pending:", "contract_warning:" })
        {
            if (raw.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                var tail = SafeIdentifier(raw[prefix.Length..], "planner_blocker");
                return $"{prefix}{tail}";
            }
        }

        var clean = SafeIdentifier(raw, "planner_blocker");
        if (string.IsNullOrWhiteSpace(clean))
        {
            return string.Empty;
        }

        if (ContainsAny(clean, "model_or_rule_strategy", "strategy", "choose", "confirm", "selection"))
        {
            return $"strategy_confirmation:{clean}";
        }

        if (ContainsAny(clean, "resource", "pending", "model", "camera", "template", "calibration"))
        {
            return $"resource_pending:{clean}";
        }

        if (ContainsAny(clean, "inspection_object", "task_type", "image_source", "acceptance_criteria", "output_target", "condition"))
        {
            return $"hard_requirement:{clean}";
        }

        return $"contract_warning:{clean}";
    }

    private static bool IsBuildBlockingReason(string reason)
    {
        return reason.StartsWith("hard_requirement:", StringComparison.OrdinalIgnoreCase) ||
               reason.StartsWith("strategy_confirmation:", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDraftableImageSourceBlocker(
        VisionAgentPlanModeResult result,
        string reason)
    {
        return reason.Contains("image_source", StringComparison.OrdinalIgnoreCase) &&
               (result.RecommendedRoute?.Operators ?? [])
               .Any(op => op.Equals("ImageAcquisition", StringComparison.OrdinalIgnoreCase));
    }

    private static bool ContainsAny(string text, params string[] terms)
    {
        return terms.Any(term => text.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    private static List<string> SanitizeList(IEnumerable<string> values, List<string> notes)
    {
        return values.Select(value => SafeText(value, notes))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToList();
    }

    private static VisionAgentRecommendedRoute SanitizeRoute(
        VisionAgentRecommendedRoute route,
        List<string> notes)
    {
        return route with
        {
            RouteId = SafeIdentifier(route.RouteId, "vision_route"),
            Title = SafeText(route.Title, notes),
            Summary = SafeText(route.Summary, notes),
            Operators = route.Operators.Select(op => SafeIdentifier(op, "Operator")).ToList(),
            TemplateDecision = SafeText(route.TemplateDecision, notes)
        };
    }

    private static VisionAgentClarificationQuestion SanitizeQuestion(
        VisionAgentClarificationQuestion question,
        List<string> notes)
    {
        return question with
        {
            Id = SafeIdentifier(question.Id, "clarification"),
            Field = VisionAgentPlanFieldPolicy.ResolveQuestionField(question),
            Title = SafeText(question.Title, notes),
            Why = SafeText(question.Why, notes),
            DefaultValue = SafeIdentifier(question.DefaultValue, "default"),
            DefaultAssumption = SafeText(question.DefaultAssumption, notes),
            Impact = SafeText(question.Impact, notes),
            Options = question.Options.Select(option => option with
            {
                Value = SafeIdentifier(option.Value, "option"),
                Label = SafeText(option.Label, notes),
                Description = SafeText(option.Description, notes),
                Impact = SafeText(option.Impact, notes)
            }).ToList()
        };
    }

    private static VisionAgentDefaultAssumption SanitizeDefault(
        VisionAgentDefaultAssumption item,
        List<string> notes)
    {
        return item with
        {
            Id = SafeIdentifier(item.Id, "default"),
            Label = SafeText(item.Label, notes),
            Value = SafeText(item.Value, notes),
            Impact = SafeText(item.Impact, notes)
        };
    }

    private static string SafeText(string? value, List<string> notes)
    {
        var text = Clean(value);
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var original = text;
        text = ImageBase64Regex.Replace(text, "<redacted-image-base64>");
        text = EndpointValueRegex.Replace(text, "<redacted-endpoint>");
        text = UrlRegex.Replace(text, "<redacted-url>");
        text = WindowsPathRegex.Replace(text, "<redacted-local-path>");
        text = SecretRegex.Replace(text, "<redacted-secret>");
        text = PlcAddressRegex.Replace(text, "<redacted-plc-address>");
        text = IpAddressRegex.Replace(text, "<redacted-network-address>");
        text = LongBase64Regex.Replace(text, "<redacted-base64>");
        if (!string.Equals(original, text, StringComparison.Ordinal))
        {
            notes.Add("unsafe_public_text_redacted");
        }

        return text;
    }

    private static string SafeErrorSummary(string publicSummary, Exception? exception)
    {
        var text = publicSummary;
        if (exception != null && !string.IsNullOrWhiteSpace(exception.Message))
        {
            text = $"{publicSummary} {exception.GetType().Name}: {exception.Message}";
        }

        var notes = new List<string>();
        var safe = SafeText(text, notes);
        return Truncate(safe, MaxSanitizedErrorMessageChars);
    }

    private static string SanitizeCompletionSummary(string completion)
    {
        var notes = new List<string>();
        var safe = SafeText(completion, notes);
        safe = safe.Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal);
        return Truncate(safe, 600);
    }

    private static string BoundCompletion(string completion, int maxChars)
    {
        var text = completion ?? string.Empty;
        if (text.Length <= maxChars)
        {
            return text;
        }

        return text[..maxChars];
    }

    private static bool LooksLikeUnsafe(string? value)
    {
        var text = value ?? string.Empty;
        return WindowsPathRegex.IsMatch(text) ||
               ImageBase64Regex.IsMatch(text) ||
               LongBase64Regex.IsMatch(text) ||
               EndpointValueRegex.IsMatch(text) ||
               UrlRegex.IsMatch(text) ||
               SecretRegex.IsMatch(text) ||
               IpAddressRegex.IsMatch(text) ||
               PlcAddressRegex.IsMatch(text);
    }

    private static string SafeIdentifier(string? value, string fallback)
    {
        var text = Clean(value);
        if (string.IsNullOrWhiteSpace(text) || LooksLikeUnsafe(text))
        {
            return fallback;
        }

        var safe = new string(text
            .Where(ch => char.IsLetterOrDigit(ch) || ch is '_' or '-' or '.')
            .ToArray());
        return string.IsNullOrWhiteSpace(safe) ? fallback : safe;
    }

    private static string SafeOptionalIdentifier(string? value, string unsafeFallback)
    {
        return string.IsNullOrWhiteSpace(Clean(value))
            ? string.Empty
            : SafeIdentifier(value, unsafeFallback);
    }

    private static string SafeToken(string? value, List<string> notes)
    {
        var safe = SafeText(value, notes);
        return SafeIdentifier(safe, string.Empty);
    }

    private static string NormalizeConfidence(string? candidate, string fallback)
    {
        var value = Clean(candidate).ToLowerInvariant();
        return value is "low" or "medium" or "high" or "medium-high"
            ? value
            : FallbackText(fallback, "medium");
    }

    private static string FallbackText(string? value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }

    private static string Clean(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
    }

    private static string Truncate(string text, int maxChars)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxChars)
        {
            return text;
        }

        return text[..maxChars];
    }
}

public sealed class VisionAgentPlanPromptComposer
{
    public VisionAgentPlanPrompt Compose(
        VisionAgentPlanModeRequest request,
        VisionAgentPlanModeResult ruleBaseline,
        VisionAgentPlanPlannerOptions options)
    {
        options.Normalize();
        var systemPrompt = string.Join(Environment.NewLine,
        [
            "You are ClearVision Plan Mode, an industrial vision engineering planner.",
            "Return exactly one JSON object that matches PlannerCandidate. No prose, markdown, comments, raw prompt, system prompt, reasoning, or chain-of-thought.",
            "You must plan from public metadata only. Do not include local paths, image bytes/base64, tokens, secrets, PLC addresses, Station IPs, camera resource paths, or hidden reasoning.",
            "Generate 0 to 3 high-value clarification questions targeting ONLY fields listed in [remaining_fields] and NEVER fields in [resolved_fields] or [confirmed_plan_answers]. Each question needs id, field, title, why, defaultValue, defaultAssumption, impact, and 2 to 5 business options. Exactly one option per question must have recommended=true. The field must be one of inspection_object, task_type, image_source, acceptance_criteria, output_target, target_attribute, defect_type, measurement_target, algorithm_strategy, roi_strategy, template_strategy. Options must include answerEffect: resolve_field, defer, or informational. recommended is orthogonal to answerEffect.",
            ruleBaseline.CurrentPhase == VisionAgentPlanPhases.ClarificationOnly
                ? "This is clarification-only mode. Ask the minimum questions needed to mature the requirement; do not invent a buildable route and keep canBuildCandidate=false."
                : "This is planning mode. Produce the public plan and only ask questions for unresolved fields.",
            "Use answerEffect=resolve_field only for concrete choices such as file_sample, station_camera, traditional_rule, or a confirmed output target. Use answerEffect=defer for camera_pending, ok_ng_pending, strategy_pending, placeholder, pending, or *_pending. Use answerEffect=informational only for read-only explanatory items. Do not mark informational as a recommended answer.",
            "Use only operator types from the provided operator catalog. Missing camera/model/template/calibration/PLC resources must be expressed as resource_pending blockers; do not degrade concrete choices into invalid answers.",
            "If a templateSelection is provided, respect it and do not replace it.",
            "Required top-level fields: goal, intent, confidence, requirementUnderstanding, recommendedRoute, clarificationQuestions, recommendedDefaults, risks, acceptanceCriteria, executablePlan, canBuildCandidate, blockingReasons, nextAction.",
            "Do not output planId, planHash, semanticExtraction, requirementMaturity, decisionTrace, contextSummary, catalog versions, stationBoundarySummary, plcOutputPolicy, publicEvents, or metadataOnly. The backend fills those fields."
        ]);
        var messages = new List<ChatMessage>
        {
            new("user", BuildContext(request, ruleBaseline, options.MaxContextChars))
        };
        return new VisionAgentPlanPrompt(systemPrompt, messages);
    }

    internal VisionAgentPlanPrompt ComposeRepair(
        string invalidOutputSummary,
        VisionAgentPlanPlannerService.PlannerFailureDiagnostic diagnostic,
        VisionAgentPlanModeRequest request,
        VisionAgentPlanModeResult ruleBaseline)
    {
        var systemPrompt = string.Join(Environment.NewLine,
        [
            "You repair invalid JSON for ClearVision Plan Mode.",
            "Return exactly one valid JSON object matching PlannerCandidate. No markdown, no prose, no comments, no reasoning.",
            "Use only the compact contract below. Do not include planId, planHash, semanticExtraction, requirementMaturity, decisionTrace, contextSummary, catalog versions, safety policy, publicEvents, secrets, paths, endpoints, image bytes, or PLC addresses."
        ]);
        var user = string.Join(Environment.NewLine,
        [
            "Repair context:",
            $"parseStage={diagnostic.Stage}",
            $"parseCode={diagnostic.Code}",
            $"sanitizedInvalidOutputSummary={Truncate(invalidOutputSummary, 600)}",
            "[compact_business_context]",
            BuildRepairBusinessContext(request, ruleBaseline, 1_800),
            "PlannerCandidate compact contract:",
            PlannerCandidateContract()
        ]);
        return new VisionAgentPlanPrompt(systemPrompt, [new ChatMessage("user", user)]);
    }

    private static string BuildRepairBusinessContext(
        VisionAgentPlanModeRequest request,
        VisionAgentPlanModeResult ruleBaseline,
        int maxChars)
    {
        var semantic = VisionAgentSemanticExtractionSafety.Sanitize(request.SemanticExtraction ?? ruleBaseline.SemanticExtraction);
        var builder = new StringBuilder();
        builder.AppendLine($"description={Truncate(VisionAgentSemanticExtractionSafety.SafeText(request.Description), 360)}");
        builder.AppendLine($"taskType={VisionAgentSemanticExtractionSafety.SafeToken(semantic?.TaskType)}");
        builder.AppendLine($"inspectionObject={Truncate(VisionAgentSemanticExtractionSafety.SafeText(semantic?.InspectionObject), 160)}");
        builder.AppendLine($"targetAttribute={Truncate(VisionAgentSemanticExtractionSafety.SafeText(semantic?.TargetAttribute), 160)}");
        builder.AppendLine($"defectType={Truncate(VisionAgentSemanticExtractionSafety.SafeText(semantic?.DefectType), 160)}");
        builder.AppendLine($"measurementTarget={Truncate(VisionAgentSemanticExtractionSafety.SafeText(semantic?.MeasurementTarget), 160)}");
        builder.AppendLine($"imageSource={Truncate(VisionAgentSemanticExtractionSafety.SafeText(semantic?.ImageSource), 120)}");
        builder.AppendLine($"okCondition={Truncate(VisionAgentSemanticExtractionSafety.SafeText(semantic?.OkCondition), 220)}");
        builder.AppendLine($"ngCondition={Truncate(VisionAgentSemanticExtractionSafety.SafeText(semantic?.NgCondition), 220)}");
        builder.AppendLine($"maturityCanPlan={ruleBaseline.RequirementMaturity?.CanPlan.ToString().ToLowerInvariant() ?? "unknown"}");
        builder.AppendLine($"maturityTaskType={VisionAgentSemanticExtractionSafety.SafeToken(ruleBaseline.RequirementMaturity?.TaskType)}");
        builder.AppendLine($"baselineRouteId={VisionAgentSemanticExtractionSafety.SafeToken(ruleBaseline.RecommendedRoute.RouteId)}");
        builder.AppendLine($"baselineOperators={string.Join(",", ruleBaseline.RecommendedRoute.Operators.Select(VisionAgentSemanticExtractionSafety.SafeToken))}");
        builder.AppendLine($"templateSelectionMode={VisionAgentSemanticExtractionSafety.SafeToken(ruleBaseline.TemplateSelection?.Mode)}");
        builder.AppendLine($"templateSelectionId={VisionAgentSemanticExtractionSafety.SafeToken(ruleBaseline.TemplateSelection?.TemplateId)}");
        builder.AppendLine("allowedOperators=");
        foreach (var item in VisionAgentReadOnlyCatalog.Operators.Take(32))
        {
            VisionAgentReadOnlyCatalog.Schemas.TryGetValue(item.OperatorType, out var schema);
            builder.AppendLine($"- {VisionAgentSemanticExtractionSafety.SafeToken(item.OperatorType)} inputs={string.Join(",", schema?.InputPorts ?? Array.Empty<string>())} outputs={string.Join(",", schema?.OutputPorts ?? Array.Empty<string>())}");
        }

        return Truncate(builder.ToString(), maxChars);
    }

    private static string BuildContext(
        VisionAgentPlanModeRequest request,
        VisionAgentPlanModeResult ruleBaseline,
        int maxChars)
    {
        maxChars = Math.Max(0, maxChars);
        var semantic = VisionAgentSemanticExtractionSafety.Sanitize(request.SemanticExtraction ?? ruleBaseline.SemanticExtraction);
        var semanticContext = new StringBuilder();
        semanticContext.AppendLine("[semantic_extraction]");
        AppendSemanticContext(semanticContext, semantic);
        semanticContext.AppendLine("[maturity_summary]");
        AppendMaturityContext(semanticContext, ruleBaseline.RequirementMaturity);

        semanticContext.AppendLine("[confirmed_plan_answers]");
        foreach (var answer in request.ConfirmedPlanAnswers ?? [])
        {
            semanticContext.AppendLine($"- field={answer.Field} value={answer.Value} origin={answer.Origin}");
        }
        semanticContext.AppendLine("[resolved_fields]");
        semanticContext.AppendLine(string.Join(",", request.ResolvedPlanFields ?? []));
        semanticContext.AppendLine("[remaining_fields]");
        semanticContext.AppendLine(string.Join(",", request.RemainingPlanFields ?? []));
        var safetyAndContract = new StringBuilder();
        safetyAndContract.AppendLine("[safety_boundary]");
        safetyAndContract.AppendLine($"stationBoundarySummary={Truncate(VisionAgentSemanticExtractionSafety.SafeText(ruleBaseline.StationBoundarySummary), 300)}");
        safetyAndContract.AppendLine($"plcOutputPolicy={Truncate(VisionAgentSemanticExtractionSafety.SafeText(ruleBaseline.PlcOutputPolicy), 300)}");
        safetyAndContract.AppendLine("No camera capture, file read, model load, PLC write, network request, secret/path echo, or deployment approval can be performed in Plan Mode.");
        safetyAndContract.AppendLine("[planner_candidate_contract]");
        safetyAndContract.AppendLine(PlannerCandidateContract());

        var remaining = Math.Max(0, maxChars - semanticContext.Length - safetyAndContract.Length - "Plan request context:".Length - 4);
        var userBudget = Math.Min(2_800, remaining * 35 / 100);
        var flowBudget = Math.Min(1_400, remaining * 15 / 100);
        var templateBudget = Math.Min(1_800, remaining * 20 / 100);
        var operatorBudget = Math.Max(0, remaining - userBudget - flowBudget - templateBudget);

        var builder = new StringBuilder();
        builder.AppendLine("Plan request context:");
        AppendBudgetedSection(builder, BuildUserRequirementSection(request), userBudget);
        AppendBudgetedSection(builder, BuildCurrentFlowSection(request), flowBudget);
        builder.Append(semanticContext);
        AppendBudgetedSection(builder, BuildTemplateSection(ruleBaseline), templateBudget);
        AppendBudgetedSection(builder, BuildOperatorCatalogSection(), operatorBudget);
        builder.Append(safetyAndContract);
        return builder.ToString();
    }

    private static string BuildUserRequirementSection(VisionAgentPlanModeRequest request)
    {
        var builder = new StringBuilder();
        builder.AppendLine("[user_requirement]");
        builder.AppendLine($"description={Truncate(VisionAgentSemanticExtractionSafety.SafeText(request.Description), 1_000)}");
        builder.AppendLine($"originalUserPrompt={Truncate(VisionAgentSemanticExtractionSafety.SafeText(request.OriginalUserPrompt), 1_000)}");
        builder.AppendLine($"additionalContext={Truncate(VisionAgentSemanticExtractionSafety.SafeText(request.AdditionalContext), 700)}");
        builder.AppendLine($"mode={VisionAgentSemanticExtractionSafety.SafeToken(request.Mode)}");
        builder.AppendLine($"historySummary={Truncate(VisionAgentSemanticExtractionSafety.SafeText(request.HistorySummary), 600)}");
        return builder.ToString();
    }

    private static string BuildCurrentFlowSection(VisionAgentPlanModeRequest request)
    {
        var builder = new StringBuilder();
        builder.AppendLine("[current_flow]");
        builder.AppendLine($"hasCurrentFlow={!string.IsNullOrWhiteSpace(request.CurrentFlowSnapshot)}");
        builder.AppendLine($"currentFlowSummary={Truncate(VisionAgentSemanticExtractionSafety.SafeText(SummarizeJsonText(request.CurrentFlowSnapshot, 700)), 700)}");
        builder.AppendLine($"currentResultSummary={Truncate(VisionAgentSemanticExtractionSafety.SafeText(SummarizeJsonText(request.CurrentResultSnapshot, 500)), 500)}");
        builder.AppendLine($"attachmentCount={request.AttachmentSummary.Count}");
        builder.AppendLine($"attachmentKinds={string.Join(",", request.AttachmentSummary.ResourceKinds.Select(VisionAgentSemanticExtractionSafety.SafeToken))}");
        builder.AppendLine($"attachmentPathsRedacted={request.AttachmentSummary.PathsRedacted.ToString().ToLowerInvariant()}");
        return builder.ToString();
    }

    private static string BuildTemplateSection(VisionAgentPlanModeResult ruleBaseline)
    {
        var builder = new StringBuilder();
        builder.AppendLine("[template_candidates]");
        builder.AppendLine($"templateSelectionMode={VisionAgentSemanticExtractionSafety.SafeToken(ruleBaseline.TemplateSelection?.Mode)}");
        builder.AppendLine($"templateSelectionId={VisionAgentSemanticExtractionSafety.SafeToken(ruleBaseline.TemplateSelection?.TemplateId)}");
        foreach (var item in VisionAgentReadOnlyCatalog.Templates)
        {
            builder.AppendLine($"- {VisionAgentSemanticExtractionSafety.SafeToken(item.TemplateId)} scenario={VisionAgentSemanticExtractionSafety.SafeToken(item.ScenarioKey)} operators={string.Join(",", item.OperatorTypes.Select(VisionAgentSemanticExtractionSafety.SafeToken))}");
        }

        return builder.ToString();
    }

    private static string BuildOperatorCatalogSection()
    {
        var builder = new StringBuilder();
        builder.AppendLine("[operator_catalog_key_io]");
        foreach (var item in VisionAgentReadOnlyCatalog.Operators)
        {
            VisionAgentReadOnlyCatalog.Schemas.TryGetValue(item.OperatorType, out var schema);
            builder.AppendLine($"- {VisionAgentSemanticExtractionSafety.SafeToken(item.OperatorType)}: {Truncate(VisionAgentSemanticExtractionSafety.SafeText(item.Summary), 160)}; inputs={string.Join(",", schema?.InputPorts ?? Array.Empty<string>())}; outputs={string.Join(",", schema?.OutputPorts ?? Array.Empty<string>())}");
        }

        return builder.ToString();
    }

    private static void AppendBudgetedSection(
        StringBuilder target,
        string section,
        int maxChars)
    {
        if (maxChars <= 0 ||
            string.IsNullOrWhiteSpace(section))
        {
            return;
        }

        var used = 0;
        var lines = section.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n');
        foreach (var line in lines)
        {
            var projected = line.Length + Environment.NewLine.Length;
            if (used + projected > maxChars)
            {
                if (used == 0)
                {
                    target.AppendLine(Truncate(line, Math.Max(0, maxChars - Environment.NewLine.Length)));
                }
                else if (used + "...[section_truncated]".Length + Environment.NewLine.Length <= maxChars)
                {
                    target.AppendLine("...[section_truncated]");
                }

                break;
            }

            target.AppendLine(line);
            used += projected;
        }
    }

    private static void AppendSemanticContext(
        StringBuilder builder,
        VisionAgentSemanticExtractionResult? semantic)
    {
        if (semantic == null)
        {
            builder.AppendLine("semanticExtraction=unavailable");
            return;
        }

        builder.AppendLine("semanticExtraction:");
        builder.AppendLine($"- source={VisionAgentSemanticExtractionSafety.SafeToken(semantic.Source)}");
        builder.AppendLine($"- isVisionRequest={semantic.IsVisionRequest.ToString().ToLowerInvariant()}");
        builder.AppendLine($"- intent={VisionAgentSemanticExtractionSafety.SafeToken(semantic.Intent)}");
        builder.AppendLine($"- taskType={VisionAgentSemanticExtractionSafety.SafeToken(semantic.TaskType)}");
        builder.AppendLine($"- confidence={semantic.Confidence:0.###}");
        builder.AppendLine($"- taskTypeConfidence={semantic.TaskTypeConfidence:0.###}");
        builder.AppendLine($"- inspectionObject={Truncate(VisionAgentSemanticExtractionSafety.SafeText(semantic.InspectionObject), 200)}");
        builder.AppendLine($"- targetAttribute={Truncate(VisionAgentSemanticExtractionSafety.SafeText(semantic.TargetAttribute), 200)}");
        builder.AppendLine($"- defectType={Truncate(VisionAgentSemanticExtractionSafety.SafeText(semantic.DefectType), 200)}");
        builder.AppendLine($"- measurementTarget={Truncate(VisionAgentSemanticExtractionSafety.SafeText(semantic.MeasurementTarget), 200)}");
        builder.AppendLine($"- imageSource={Truncate(VisionAgentSemanticExtractionSafety.SafeText(semantic.ImageSource), 200)}");
        builder.AppendLine($"- okCondition={Truncate(VisionAgentSemanticExtractionSafety.SafeText(semantic.OkCondition), 300)}");
        builder.AppendLine($"- ngCondition={Truncate(VisionAgentSemanticExtractionSafety.SafeText(semantic.NgCondition), 300)}");
        builder.AppendLine($"- outputTarget={Truncate(VisionAgentSemanticExtractionSafety.SafeText(semantic.OutputTarget), 200)}");
        builder.AppendLine($"- suggestedRoute={Truncate(VisionAgentSemanticExtractionSafety.SafeText(semantic.SuggestedRoute), 300)}");
        builder.AppendLine($"- missingFields={string.Join(",", semantic.MissingFields.Select(VisionAgentSemanticExtractionSafety.SafeToken))}");
        builder.AppendLine($"- failureCode={VisionAgentSemanticExtractionSafety.SafeToken(semantic.FailureCode)}");
        builder.AppendLine("- safety=semantic extraction is read-only; do not treat canBuildCandidate as final Build permission.");
    }

    private static void AppendMaturityContext(
        StringBuilder builder,
        AiRequirementMaturityResult? maturity)
    {
        if (maturity == null)
        {
            builder.AppendLine("maturity=unavailable");
            return;
        }

        builder.AppendLine($"- maturity={VisionAgentSemanticExtractionSafety.SafeToken(maturity.Maturity)}");
        builder.AppendLine($"- taskType={VisionAgentSemanticExtractionSafety.SafeToken(maturity.TaskType)}");
        builder.AppendLine($"- canPlan={maturity.CanPlan.ToString().ToLowerInvariant()}");
        builder.AppendLine($"- canBuildHardFacts={maturity.CanBuild.ToString().ToLowerInvariant()}");
        builder.AppendLine($"- missingFields={string.Join(",", maturity.MissingFields.Select(VisionAgentSemanticExtractionSafety.SafeToken))}");
        builder.AppendLine($"- blockingReasons={string.Join(",", maturity.BlockingReasons.Select(VisionAgentSemanticExtractionSafety.SafeToken))}");
        builder.AppendLine($"- publicReason={Truncate(VisionAgentSemanticExtractionSafety.SafeText(maturity.PublicReason), 240)}");
    }

    private static string PlannerCandidateContract()
    {
        return """
{
  "goal": "short public goal",
  "intent": "surface_defect|measurement|wire_sequence|code_recognition|presence_absence|classification|attribute_classification|template_location|general_inspection",
  "confidence": "low|medium|high",
  "requirementUnderstanding": ["public facts from the user requirement"],
  "recommendedRoute": {
    "routeId": "stable_route_id",
    "title": "public route title",
    "summary": "why this route fits",
    "operators": ["ImageAcquisition", "DeepLearning", "ResultJudgment", "ResultOutput"],
    "templateDecision": "use_selected_template|catalog_match|planner_route"
  },
  "clarificationQuestions": [
    {
      "id": "stable_question_id",
      "field": "algorithm_strategy",
      "title": "question",
      "why": "impact",
      "defaultValue": "recommended option value",
      "defaultAssumption": "assumption",
      "impact": "build impact",
      "options": [
        { "value": "recommended", "label": "Recommended", "recommended": true, "answerEffect": "resolve_field", "recommendationReason": "public sanitized reason", "description": "option", "impact": "impact" },
        { "value": "pending", "label": "Keep pending", "recommended": false, "answerEffect": "defer", "recommendationReason": "", "description": "option", "impact": "impact" }
      ]
    }
  ],
  "recommendedDefaults": [{ "id": "metadata_only", "label": "Public diagnostics only", "value": "redacted_metadata", "impact": "no raw resources are exposed" }],
  "risks": ["public risk"],
  "acceptanceCriteria": ["public acceptance criterion"],
  "executablePlan": ["confirm choices", "build editable draft", "run readiness checks"],
  "canBuildCandidate": true,
  "blockingReasons": [],
  "nextAction": "Accept defaults and build editable draft"
}
""";
    }

    private static string SummarizeJsonText(string? text, int maxChars)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        try
        {
            using var doc = JsonDocument.Parse(text);
            return Truncate(doc.RootElement.GetRawText(), maxChars);
        }
        catch (JsonException)
        {
            return Truncate(text, maxChars);
        }
    }

    private static string Truncate(string? text, int maxChars)
    {
        if (string.IsNullOrEmpty(text) ||
            maxChars <= 0)
        {
            return string.Empty;
        }

        return text.Length <= maxChars
            ? text
            : text[..maxChars] + "...[truncated]";
    }
}

public sealed record VisionAgentPlanPrompt(
    string SystemPrompt,
    List<ChatMessage> Messages);
