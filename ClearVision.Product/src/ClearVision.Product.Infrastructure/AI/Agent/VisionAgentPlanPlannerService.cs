using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Net;
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
        @"(?i)(sk-[A-Za-z0-9_\-]{12,}|api[_-]?key\s*[:=]\s*[^\s,;]+|token\s*[:=]\s*[^\s,;]+|secret\s*[:=]\s*[^\s,;]+)",
        RegexOptions.Compiled);
    private static readonly Regex IpAddressRegex = new(
        @"\b(?:\d{1,3}\.){3}\d{1,3}\b",
        RegexOptions.Compiled);
    private static readonly Regex PlcAddressRegex = new(
        @"(?i)\b(DB\d+\.DB[XBWD]\d+|M\d+(?:\.\d+)?|D\d+|plc://[^\s,;]+)\b",
        RegexOptions.Compiled);

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
        var events = new List<VisionAgentPlanPublicEvent>
        {
            Event("collecting_context", "completed", "上下文收集完成",
                "已收集公开需求、流程、模板、附件、算子和工站边界元数据。",
                new()
                {
                    ["hasCurrentFlow"] = ruleBaseline.ContextSummary.HasCurrentFlow.ToString().ToLowerInvariant(),
                    ["attachmentCount"] = ruleBaseline.ContextSummary.AttachmentCount.ToString(),
                    ["templateSelectionMode"] = ruleBaseline.ContextSummary.TemplateSelectionMode
                })
        };

        if (!_options.Enabled)
        {
            return BuildFallback(
                ruleBaseline,
                "planner_disabled",
                events,
                "Planner 生成未启用，已使用规则兜底方案。");
        }

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));
            var prompt = _promptComposer.Compose(request, ruleBaseline, _options);
            events.Add(Event("planning_with_model", "started", "模型规划已开始",
                "模型正在生成结构化 PlanModeResult 候选。",
                new()
                {
                    ["modelRole"] = _options.ModelRole,
                    ["metadataOnly"] = "true"
                }));
            var completion = await _completionSource.CompleteAsync(
                new VisionAgentPlanCompletionRequest(prompt.SystemPrompt, prompt.Messages, _options.ModelRole),
                timeout.Token);
            events.Add(Event("planning_with_model", "completed", "模型规划候选已返回",
                "模型已返回公开结构化候选，等待校验。"));

            events.Add(Event("validating_plan_contract", "started", "校验规划契约",
                "正在校验 JSON 结构、问题质量、算子目录和模板约束。"));
            var candidate = ParseCandidate(completion);
            var repaired = RepairCandidate(candidate, request, ruleBaseline, out var repairNotes, out var warnings);
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
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            events.Add(Event("planning_with_model", "failed", "模型规划超时",
                "模型规划超时，已使用规则兜底方案。",
                new() { ["fallbackReason"] = "planner_timeout" }));
            return BuildFallback(
                ruleBaseline,
                "planner_timeout",
                events,
                "模型规划超时，已使用规则兜底方案。");
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
        {
            events.Add(Event("planning_with_model", "failed", "模型规划鉴权失败",
                "模型规划鉴权失败，已使用规则兜底，请检查 Planner API Key/接口/模型名。",
                new() { ["fallbackReason"] = "planner_unauthorized" }));
            return BuildFallback(
                ruleBaseline,
                "planner_unauthorized",
                events,
                "模型规划鉴权失败，已使用规则兜底，请检查 Planner API Key/接口/模型名。");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                "Vision Agent Plan Planner failed; rule fallback will be used. Error={Error}",
                ex.Message);
            events.Add(Event("planning_with_model", "failed", "模型规划失败",
                "模型规划失败，已使用规则兜底方案。",
                new() { ["fallbackReason"] = "planner_failed" }));
            return BuildFallback(
                ruleBaseline,
                "planner_failed",
                events,
                "模型规划失败，已使用规则兜底方案。");
        }
    }

    private static VisionAgentPlanModeResult ParseCandidate(string completion)
    {
        var json = ExtractJsonObject(completion);
        return JsonSerializer.Deserialize<VisionAgentPlanModeResult>(json, JsonOptions)
            ?? throw new InvalidOperationException("Planner returned an empty plan object.");
    }

    private static VisionAgentPlanModeResult RepairCandidate(
        VisionAgentPlanModeResult candidate,
        VisionAgentPlanModeRequest request,
        VisionAgentPlanModeResult baseline,
        out List<string> repairNotes,
        out List<string> warnings)
    {
        repairNotes = [];
        warnings = [];

        var templateSelection = ConstrainTemplateSelection(
            candidate.TemplateSelection,
            baseline.TemplateSelection,
            repairNotes);
        var route = RepairRoute(candidate.RecommendedRoute, baseline.RecommendedRoute, repairNotes);
        var questions = RepairQuestions(candidate.ClarificationQuestions, baseline.ClarificationQuestions, repairNotes);
        var defaults = RepairDefaults(candidate.RecommendedDefaults, baseline.RecommendedDefaults, repairNotes);
        var understanding = NormalizeList(candidate.RequirementUnderstanding, baseline.RequirementUnderstanding);
        var risks = NormalizeList(candidate.Risks, baseline.Risks);
        var acceptance = NormalizeList(candidate.AcceptanceCriteria, baseline.AcceptanceCriteria);
        var executablePlan = NormalizeList(candidate.ExecutablePlan, baseline.ExecutablePlan);
        var blockingReasons = NormalizeList(candidate.BlockingReasons, []);

        var redactionNotes = new List<string>();
        var result = new VisionAgentPlanModeResult
        {
            PlanId = string.IsNullOrWhiteSpace(candidate.PlanId)
                ? baseline.PlanId
                : SafeToken(candidate.PlanId, redactionNotes),
            OriginalUserPrompt = SafeText(
                string.IsNullOrWhiteSpace(candidate.OriginalUserPrompt)
                    ? baseline.OriginalUserPrompt
                    : candidate.OriginalUserPrompt,
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
            CanBuild = candidate.CanBuild && !string.IsNullOrWhiteSpace(request.Description),
            BlockingReasons = SanitizeList(blockingReasons, redactionNotes),
            NextAction = SafeText(
                string.IsNullOrWhiteSpace(candidate.NextAction) ? baseline.NextAction : candidate.NextAction,
                redactionNotes),
            ContextSummary = baseline.ContextSummary,
            OperatorCatalogVersion = baseline.OperatorCatalogVersion,
            TemplateCatalogVersion = baseline.TemplateCatalogVersion,
            TemplateSelection = templateSelection,
            StationBoundarySummary = baseline.StationBoundarySummary,
            PlcOutputPolicy = NormalizePlcPolicy(candidate.PlcOutputPolicy, baseline.PlcOutputPolicy, redactionNotes),
            MetadataOnly = true
        };

        var maturityRequest = new VisionAgentRequirementMaturityRequest
        {
            Description = request.Description,
            AdditionalContext = request.AdditionalContext,
            Mode = request.Mode,
            HasCurrentFlow = !string.IsNullOrWhiteSpace(request.CurrentFlowSnapshot),
            TemplateSelection = baseline.TemplateSelection
        };
        var maturity = VisionAgentRequirementMaturityGate.Evaluate(maturityRequest);
        if (!maturity.CanBuild || baseline.CanBuild == false)
        {
            result = result with
            {
                Intent = maturity.Maturity,
                CanBuild = false,
                RecommendedRoute = baseline.RecommendedRoute,
                BlockingReasons = maturity.BlockingReasons.Count > 0
                    ? maturity.BlockingReasons.ToList()
                    : baseline.BlockingReasons.ToList(),
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
            result = result with
            {
                RequirementMaturity = maturity,
                DecisionTrace = VisionAgentRequirementMaturityGate.BuildDecisionTrace(
                    maturityRequest,
                    maturity,
                    "actionable_vision_plan",
                    "planning",
                    string.Empty)
            };
        }

        if (!result.CanBuild && result.BlockingReasons.Count == 0)
        {
            result.BlockingReasons.Add("inspection_goal_missing");
            repairNotes.Add("blocking_reason_added");
        }

        if (redactionNotes.Count > 0)
        {
            warnings.AddRange(redactionNotes.Distinct(StringComparer.OrdinalIgnoreCase));
            repairNotes.Add("unsafe_text_redacted");
        }

        return result;
    }

    private static VisionAgentPlanModeResult BuildFallback(
        VisionAgentPlanModeResult baseline,
        string reason,
        List<VisionAgentPlanPublicEvent> events,
        string summary)
    {
        var result = baseline with
        {
            PlanSource = "rule_fallback",
            FallbackReason = reason,
            PlanWarnings = [summary],
            ContractRepairNotes = [],
            PublicEvents =
            [
                .. events,
                Event("rule_fallback_used", "completed", "已启用规则兜底", summary,
                    new() { ["fallbackReason"] = reason }),
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

    private static VisionAgentRecommendedRoute RepairRoute(
        VisionAgentRecommendedRoute candidate,
        VisionAgentRecommendedRoute baseline,
        List<string> repairNotes)
    {
        var allowed = AllowedOperatorTypes();
        var candidateOperators = candidate.Operators
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
        List<VisionAgentClarificationQuestion> candidate,
        List<VisionAgentClarificationQuestion> baseline,
        List<string> repairNotes)
    {
        var questions = candidate
            .Where(question => !string.IsNullOrWhiteSpace(question.Id) &&
                               !string.IsNullOrWhiteSpace(question.Title))
            .Take(5)
            .Select(question => RepairQuestion(question))
            .Where(question => question.Options.Count is >= 2 and <= 5)
            .ToList();

        if (questions.Count < 2)
        {
            repairNotes.Add("clarification_questions_repaired_to_baseline");
            questions = baseline.Take(5).Select(RepairQuestion).ToList();
        }

        return questions.Take(5).ToList();
    }

    private static VisionAgentClarificationQuestion RepairQuestion(VisionAgentClarificationQuestion question)
    {
        var options = question.Options
            .Where(option => !string.IsNullOrWhiteSpace(option.Value) &&
                             !string.IsNullOrWhiteSpace(option.Label))
            .Take(5)
            .ToList();
        if (options.Count > 0 && options.All(option => !option.Recommended))
        {
            options[0] = options[0] with { Recommended = true };
        }

        var recommended = options.FirstOrDefault(option => option.Recommended)?.Value ??
                          options.FirstOrDefault()?.Value ??
                          question.DefaultValue;
        return question with
        {
            Id = SafeIdentifier(question.Id, "clarification"),
            Title = FallbackText(question.Title, "关键澄清问题"),
            Why = FallbackText(question.Why, "这会影响算子链、参数或发布就绪。"),
            DefaultValue = FallbackText(question.DefaultValue, recommended),
            DefaultAssumption = FallbackText(question.DefaultAssumption, "使用推荐选项作为仅元数据默认值。"),
            Impact = FallbackText(question.Impact, "修改该选项会改变构建假设。"),
            Options = options
        };
    }

    private static List<VisionAgentDefaultAssumption> RepairDefaults(
        List<VisionAgentDefaultAssumption> candidate,
        List<VisionAgentDefaultAssumption> baseline,
        List<string> repairNotes)
    {
        var defaults = candidate
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
        var mode = SafeIdentifier(selection?.Mode, string.Empty).ToLowerInvariant();
        var templateId = SafeIdentifier(selection?.TemplateId, "redacted_template");
        var scenarioKey = SafeIdentifier(selection?.ScenarioKey, string.Empty);
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
        return VisionAgentReadOnlyCatalog.Schemas.Keys
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

    private static bool LooksLikeUnsafe(string? value)
    {
        var text = value ?? string.Empty;
        return WindowsPathRegex.IsMatch(text) ||
               ImageBase64Regex.IsMatch(text) ||
               LongBase64Regex.IsMatch(text) ||
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
            "Return exactly one JSON object that matches VisionAgentPlanModeResult. No prose, markdown, comments, raw prompt, system prompt, reasoning, or chain-of-thought.",
            "You must plan from public metadata only. Do not include local paths, image bytes/base64, tokens, secrets, PLC addresses, Station IPs, camera resource paths, or hidden reasoning.",
            "Generate 2 to 5 high-value clarification questions. Each question needs id, title, why, defaultValue, defaultAssumption, impact, and 2 to 5 options. Exactly one or more options must have recommended=true.",
            "Use only operator types from the provided operator catalog. Missing camera/model/template/calibration/PLC resources must stay pending metadata.",
            "If a templateSelection is provided, respect it and do not replace it.",
            "Required top-level fields: goal, intent, confidence, requirementUnderstanding, recommendedRoute, clarificationQuestions, recommendedDefaults, risks, acceptanceCriteria, executablePlan, canBuild, blockingReasons, nextAction, templateSelection, stationBoundarySummary, plcOutputPolicy, metadataOnly.",
            "Do not set planHash; the backend computes it after validation."
        ]);
        var messages = new List<ChatMessage>
        {
            new("user", BuildContext(request, ruleBaseline, options.MaxContextChars))
        };
        return new VisionAgentPlanPrompt(systemPrompt, messages);
    }

    private static string BuildContext(
        VisionAgentPlanModeRequest request,
        VisionAgentPlanModeResult ruleBaseline,
        int maxChars)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Plan request context:");
        builder.AppendLine($"description={Truncate(request.Description, 2_000)}");
        builder.AppendLine($"originalUserPrompt={Truncate(request.OriginalUserPrompt, 2_000)}");
        builder.AppendLine($"additionalContext={Truncate(request.AdditionalContext, 2_000)}");
        builder.AppendLine($"mode={request.Mode}");
        builder.AppendLine($"historySummary={Truncate(request.HistorySummary, 2_000)}");
        builder.AppendLine($"hasCurrentFlow={!string.IsNullOrWhiteSpace(request.CurrentFlowSnapshot)}");
        builder.AppendLine($"currentFlowSummary={SummarizeJsonText(request.CurrentFlowSnapshot, 2_000)}");
        builder.AppendLine($"currentResultSummary={SummarizeJsonText(request.CurrentResultSnapshot, 2_000)}");
        builder.AppendLine($"attachmentCount={request.AttachmentSummary.Count}");
        builder.AppendLine($"attachmentKinds={string.Join(",", request.AttachmentSummary.ResourceKinds)}");
        builder.AppendLine($"attachmentPathsRedacted={request.AttachmentSummary.PathsRedacted.ToString().ToLowerInvariant()}");
        builder.AppendLine($"templateSelectionMode={ruleBaseline.TemplateSelection?.Mode}");
        builder.AppendLine($"templateSelectionId={ruleBaseline.TemplateSelection?.TemplateId}");
        builder.AppendLine($"stationBoundarySummary={ruleBaseline.StationBoundarySummary}");
        builder.AppendLine($"plcOutputPolicy={ruleBaseline.PlcOutputPolicy}");
        builder.AppendLine($"operatorCatalogVersion={ruleBaseline.OperatorCatalogVersion}");
        builder.AppendLine("operatorCatalog:");
        foreach (var item in VisionAgentReadOnlyCatalog.Operators)
        {
            builder.AppendLine($"- {item.OperatorType}: {item.Summary}");
        }

        builder.AppendLine("templateCatalog:");
        foreach (var item in VisionAgentReadOnlyCatalog.Templates)
        {
            builder.AppendLine($"- {item.TemplateId} scenario={item.ScenarioKey} operators={string.Join(",", item.OperatorTypes)}");
        }

        builder.AppendLine("ruleBaselineForFallback:");
        builder.AppendLine(JsonSerializer.Serialize(ruleBaseline, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        }));
        var text = builder.ToString();
        return Truncate(text, maxChars);
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
        if (string.IsNullOrEmpty(text))
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
