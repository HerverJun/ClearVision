using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ClearVision.Product.Contracts.Messages;
using ClearVision.Product.Core.DTOs;
using ClearVision.Product.Infrastructure.AI.Runtime;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ClearVision.Product.Infrastructure.AI.Agent;

public interface IVisionAgentIntentRouterService
{
    Task<VisionAgentIntentRouterResult> RouteAsync(
        VisionAgentIntentRouterRequest request,
        CancellationToken cancellationToken);
}

public interface IVisionAgentIntentRouterCompletionSource
{
    Task<string> CompleteAsync(
        VisionAgentIntentRouterCompletionRequest request,
        CancellationToken cancellationToken);
}

public sealed record VisionAgentIntentRouterCompletionRequest(
    string SystemPrompt,
    List<ChatMessage> Messages,
    string ModelRole);

public sealed class LlmVisionAgentIntentRouterCompletionSource : IVisionAgentIntentRouterCompletionSource
{
    private readonly AiGenerationOrchestrator _orchestrator;

    public LlmVisionAgentIntentRouterCompletionSource(AiGenerationOrchestrator orchestrator)
    {
        _orchestrator = orchestrator;
    }

    public async Task<string> CompleteAsync(
        VisionAgentIntentRouterCompletionRequest request,
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

public sealed class VisionAgentIntentRouterOptions
{
    public const string SectionName = "AI:VisionAgent:IntentRouter";

    public bool Enabled { get; set; } = true;

    public string ModelRole { get; set; } = AiModelConfig.RolePlanner;

    public bool AllowRuleFallback { get; set; } = true;

    public int TimeoutSeconds { get; set; } = 30;

    public int MaxContextChars { get; set; } = 8_000;

    public VisionAgentIntentRouterOptions Normalize()
    {
        ModelRole = AiModelConfig.NormalizeRoleName(ModelRole);
        TimeoutSeconds = Math.Clamp(TimeoutSeconds, 1, 90);
        MaxContextChars = Math.Clamp(MaxContextChars, 2_000, 24_000);
        return this;
    }
}

public sealed class VisionAgentIntentRouterService : IVisionAgentIntentRouterService
{
    public const string IntentCasualChat = "casual_chat";
    public const string IntentHelp = "help";
    public const string IntentAmbiguousVisionRequirement = "ambiguous_vision_requirement";
    public const string IntentActionableVisionPlan = "actionable_vision_plan";
    public const string IntentModifyExistingFlow = "modify_existing_flow";
    public const string IntentBuildFromConfirmedPlan = "build_from_confirmed_plan";
    public const string IntentDirectBuildDebug = "direct_build_debug";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private static readonly Regex UnsafeRegex = new(
        @"(?i)(rawPrompt|raw_prompt|systemPrompt|system_prompt|chain[-_ ]?of[-_ ]?thought|reasoning_content|[A-Za-z]:\\|\\\\|/users/|/home/|data:image/|base64|sk-[A-Za-z0-9_\-]{8,}|api[_-]?key\s*[:=]|token\s*[:=]|secret\s*[:=]|authorization\s*[:=]|headers?\s*[:=]|baseUrl\s*[:=]|base_url\s*[:=]|bearer\s+[A-Za-z0-9._\-]+|\b(?:\d{1,3}\.){3}\d{1,3}\b|\bDB\d+\.DB[XBWD]\d+(?:\.\d+)?\b|\bM\d+(?:\.\d+)?\b|\bD\d+\b|plc://)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly IVisionAgentIntentRouterCompletionSource _completionSource;
    private readonly VisionAgentIntentRouterOptions _options;
    private readonly Microsoft.Extensions.Logging.ILogger<VisionAgentIntentRouterService> _logger;

    public VisionAgentIntentRouterService(
        IVisionAgentIntentRouterCompletionSource completionSource,
        IOptions<VisionAgentIntentRouterOptions>? options,
        Microsoft.Extensions.Logging.ILogger<VisionAgentIntentRouterService> logger)
    {
        _completionSource = completionSource;
        _options = (options?.Value ?? new VisionAgentIntentRouterOptions()).Normalize();
        _logger = logger;
    }

    public async Task<VisionAgentIntentRouterResult> RouteAsync(
        VisionAgentIntentRouterRequest request,
        CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            return RuleFallback(request, "router_disabled");
        }

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));
            var prompt = BuildPrompt(request, _options);
            var completion = await _completionSource.CompleteAsync(
                new VisionAgentIntentRouterCompletionRequest(prompt.SystemPrompt, prompt.Messages, _options.ModelRole),
                timeout.Token);
            return RepairResult(ParseResult(completion), request, "model_router", string.Empty);
        }
        catch (Exception firstError) when (!cancellationToken.IsCancellationRequested &&
                                         !IsUnauthorized(firstError) &&
                                         IsRepairable(firstError))
        {
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));
                var repairPrompt = BuildRepairPrompt(request, firstError.Message, _options);
                var repaired = await _completionSource.CompleteAsync(
                    new VisionAgentIntentRouterCompletionRequest(repairPrompt.SystemPrompt, repairPrompt.Messages, _options.ModelRole),
                    timeout.Token);
                return RepairResult(ParseResult(repaired), request, "model_router_repaired", "router_json_repaired");
            }
            catch (Exception repairError) when (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning(
                    "Vision Agent Intent Router repair failed; rule fallback will be used. Error={Error}",
                    repairError.Message);
                return RuleFallback(request, IsUnauthorized(repairError) ? "router_unauthorized" : "router_repair_failed");
            }
        }
        catch (Exception error) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(
                "Vision Agent Intent Router failed; rule fallback will be used. Error={Error}",
                error.Message);
            return RuleFallback(request, IsUnauthorized(error) ? "router_unauthorized" : "router_failed");
        }
    }

    private static VisionAgentIntentRouterResult ParseResult(string completion)
    {
        var json = ExtractJsonObject(completion);
        return JsonSerializer.Deserialize<VisionAgentIntentRouterResult>(json, JsonOptions)
            ?? throw new InvalidOperationException("Intent Router returned an empty JSON object.");
    }

    private static VisionAgentIntentRouterResult RepairResult(
        VisionAgentIntentRouterResult candidate,
        VisionAgentIntentRouterRequest request,
        string source,
        string fallbackReason)
    {
        var intent = NormalizeIntent(candidate.Intent);
        var confidence = NormalizeConfidence(candidate.Confidence);
        var canBuild = intent is IntentActionableVisionPlan or IntentModifyExistingFlow or IntentBuildFromConfirmedPlan or IntentDirectBuildDebug;
        var shouldOpenPlan = intent == IntentActionableVisionPlan;
        var shouldBuildDirectly = intent is IntentModifyExistingFlow or IntentBuildFromConfirmedPlan or IntentDirectBuildDebug;
        var needsClarification = intent == IntentAmbiguousVisionRequirement;
        var questions = SanitizeQuestions(candidate.ClarificationQuestions);

        if (intent == IntentBuildFromConfirmedPlan && !request.HasPendingPlan)
        {
            intent = IntentAmbiguousVisionRequirement;
            confidence = "medium";
            canBuild = false;
            shouldBuildDirectly = false;
            needsClarification = true;
            questions = DefaultClarificationQuestions();
            fallbackReason = "confirmed_plan_missing";
        }

        if (intent == IntentDirectBuildDebug && !request.DeveloperDirectBuildDebug)
        {
            intent = IntentActionableVisionPlan;
            confidence = "medium";
            shouldOpenPlan = true;
            shouldBuildDirectly = false;
            needsClarification = false;
            fallbackReason = "direct_build_debug_not_enabled";
        }

        if (intent == IntentAmbiguousVisionRequirement && questions.Count == 0)
        {
            questions = DefaultClarificationQuestions();
        }

        return candidate with
        {
            Intent = intent,
            Confidence = confidence,
            ShouldOpenPlan = shouldOpenPlan,
            ShouldBuildDirectly = shouldBuildDirectly,
            CanBuild = canBuild,
            NeedsClarification = needsClarification,
            PublicReason = SafeText(string.IsNullOrWhiteSpace(candidate.PublicReason)
                ? DefaultReason(intent)
                : candidate.PublicReason),
            AssistantReply = SafeText(string.IsNullOrWhiteSpace(candidate.AssistantReply)
                ? DefaultReply(intent)
                : candidate.AssistantReply),
            ClarificationQuestions = questions,
            FallbackAllowed = candidate.FallbackAllowed,
            RouterSource = source,
            FallbackReason = SafeToken(fallbackReason),
            MetadataOnly = true
        };
    }

    private static VisionAgentIntentRouterResult RuleFallback(
        VisionAgentIntentRouterRequest request,
        string reason)
    {
        var text = Clean(request.Description);
        var intent = ResolveRuleFallbackIntent(text, request);
        var isCasual = intent == IntentCasualChat;
        var isHelp = intent == IntentHelp;
        var isActionable = intent == IntentActionableVisionPlan;
        var isModify = intent == IntentModifyExistingFlow;
        var isConfirmed = intent == IntentBuildFromConfirmedPlan;
        var isDirectDebug = intent == IntentDirectBuildDebug;
        var isAmbiguous = intent == IntentAmbiguousVisionRequirement;
        var questions = isAmbiguous ? DefaultClarificationQuestions() : [];

        return new VisionAgentIntentRouterResult
        {
            Intent = intent,
            Confidence = reason == "router_unauthorized" && (isCasual || isHelp) ? "high" : "medium",
            ShouldOpenPlan = isActionable,
            ShouldBuildDirectly = isModify || isConfirmed || isDirectDebug,
            CanBuild = isActionable || isModify || isConfirmed || isDirectDebug,
            NeedsClarification = isAmbiguous,
            PublicReason = reason == "router_unauthorized"
                ? UnauthorizedReason(intent)
                : DefaultReason(intent),
            AssistantReply = DefaultReply(intent),
            ClarificationQuestions = questions,
            FallbackAllowed = true,
            RouterSource = "rule_fallback",
            FallbackReason = SafeToken(reason),
            MetadataOnly = true
        };
    }

    private static string ResolveRuleFallbackIntent(string text, VisionAgentIntentRouterRequest request)
    {
        if (request.DeveloperDirectBuildDebug)
        {
            return IntentDirectBuildDebug;
        }

        if (LooksLikeHelp(text))
        {
            return IntentHelp;
        }

        if (LooksLikeCasual(text))
        {
            return IntentCasualChat;
        }

        if (request.HasPendingPlan && LooksLikeConfirmedPlanBuild(text))
        {
            return IntentBuildFromConfirmedPlan;
        }

        if (!string.IsNullOrWhiteSpace(request.CurrentFlowSnapshot) && LooksLikeModifyExistingFlow(text))
        {
            return IntentModifyExistingFlow;
        }

        if (LooksLikeActionableVisionNeed(text))
        {
            return IntentActionableVisionPlan;
        }

        return IntentAmbiguousVisionRequirement;
    }

    private static VisionAgentIntentRouterPrompt BuildPrompt(
        VisionAgentIntentRouterRequest request,
        VisionAgentIntentRouterOptions options)
    {
        var systemPrompt = string.Join(Environment.NewLine,
        [
            "You are ClearVision Intent Router for an industrial vision assistant.",
            "Return exactly one JSON object. No prose, markdown, comments, raw prompt, system prompt, reasoning, or chain-of-thought.",
            "Classify the user's latest input into exactly one intent: casual_chat, help, ambiguous_vision_requirement, actionable_vision_plan, modify_existing_flow, build_from_confirmed_plan, direct_build_debug.",
            "Rules: casual/help must not open Plan or Build. ambiguous_vision_requirement must ask concise public clarification questions and canBuild=false. actionable_vision_plan opens Plan only. modify_existing_flow may build directly only when current flow metadata exists. build_from_confirmed_plan may build directly only when a pending confirmed plan exists. direct_build_debug is only valid when developerDirectBuildDebug=true.",
            "Use the model for semantic judgment. Do not rely on keyword matching. Do not include chain-of-thought; publicReason must be short and safe.",
            "Safety boundary: never request or expose camera paths, PLC addresses, Station IPs, external URLs, API keys, headers, tokens, baseUrl, rawPrompt, systemPrompt, image bytes/base64, model/template/image/PLC filesystem paths, or deployment/config writes.",
            "Required JSON fields: intent, confidence, shouldOpenPlan, shouldBuildDirectly, canBuild, needsClarification, publicReason, assistantReply, clarificationQuestions, fallbackAllowed."
        ]);
        return new VisionAgentIntentRouterPrompt(systemPrompt, [new ChatMessage("user", BuildContext(request, options.MaxContextChars))]);
    }

    private static VisionAgentIntentRouterPrompt BuildRepairPrompt(
        VisionAgentIntentRouterRequest request,
        string error,
        VisionAgentIntentRouterOptions options)
    {
        var systemPrompt = string.Join(Environment.NewLine,
        [
            "Repair the Intent Router output. Return exactly one valid JSON object and no other text.",
            "The object must use only public fields: intent, confidence, shouldOpenPlan, shouldBuildDirectly, canBuild, needsClarification, publicReason, assistantReply, clarificationQuestions, fallbackAllowed.",
            "Do not include rawPrompt, systemPrompt, reasoning, chain-of-thought, keys, tokens, baseUrl, paths, IPs, PLC addresses, or base64."
        ]);
        var context = BuildContext(request, options.MaxContextChars);
        return new VisionAgentIntentRouterPrompt(systemPrompt, [new ChatMessage("user", $"Previous output error={SafeText(error)}\n{context}")]);
    }

    private static string BuildContext(VisionAgentIntentRouterRequest request, int maxChars)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Intent Router request context:");
        builder.AppendLine($"description={Truncate(SafeText(request.Description), 2_000)}");
        builder.AppendLine($"originalUserPrompt={Truncate(SafeText(request.OriginalUserPrompt), 2_000)}");
        builder.AppendLine($"additionalContext={Truncate(SafeText(request.AdditionalContext), 1_500)}");
        builder.AppendLine($"mode={SafeToken(request.Mode)}");
        builder.AppendLine($"historySummary={Truncate(SafeText(request.HistorySummary), 1_500)}");
        builder.AppendLine($"hasCurrentFlow={!string.IsNullOrWhiteSpace(request.CurrentFlowSnapshot)}");
        builder.AppendLine($"currentResultSummary={SummarizeJsonText(request.CurrentResultSnapshot, 1_000)}");
        builder.AppendLine($"attachmentCount={request.AttachmentSummary.Count}");
        builder.AppendLine($"attachmentKinds={string.Join(",", request.AttachmentSummary.ResourceKinds.Select(SafeToken))}");
        builder.AppendLine($"attachmentPathsRedacted={request.AttachmentSummary.PathsRedacted.ToString().ToLowerInvariant()}");
        builder.AppendLine($"templateSelectionMode={SafeToken(request.TemplateSelection?.Mode)}");
        builder.AppendLine($"templateSelectionId={SafeToken(request.TemplateSelection?.TemplateId)}");
        builder.AppendLine($"hasPendingPlan={request.HasPendingPlan.ToString().ToLowerInvariant()}");
        builder.AppendLine($"pendingPlanSummary={Truncate(SafeText(request.PendingPlanSummary), 1_500)}");
        builder.AppendLine($"developerDirectBuildDebug={request.DeveloperDirectBuildDebug.ToString().ToLowerInvariant()}");
        return Truncate(builder.ToString(), maxChars);
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
            throw new InvalidOperationException("Intent Router did not return a JSON object.");
        }

        return text[start..(end + 1)];
    }

    private static bool IsRepairable(Exception error)
    {
        return error is JsonException or InvalidOperationException;
    }

    private static bool IsUnauthorized(Exception error)
    {
        return error is HttpRequestException { StatusCode: HttpStatusCode.Unauthorized } ||
               error.Message.Contains("401", StringComparison.OrdinalIgnoreCase) ||
               error.Message.Contains("unauthorized", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeIntent(string? intent)
    {
        return Clean(intent).ToLowerInvariant() switch
        {
            IntentCasualChat => IntentCasualChat,
            IntentHelp => IntentHelp,
            IntentAmbiguousVisionRequirement => IntentAmbiguousVisionRequirement,
            IntentActionableVisionPlan => IntentActionableVisionPlan,
            IntentModifyExistingFlow => IntentModifyExistingFlow,
            IntentBuildFromConfirmedPlan => IntentBuildFromConfirmedPlan,
            IntentDirectBuildDebug => IntentDirectBuildDebug,
            _ => IntentAmbiguousVisionRequirement
        };
    }

    private static string NormalizeConfidence(string? confidence)
    {
        return Clean(confidence).ToLowerInvariant() switch
        {
            "high" => "high",
            "medium" => "medium",
            "low" => "low",
            _ => "low"
        };
    }

    private static List<string> SanitizeQuestions(IEnumerable<string>? questions)
    {
        return (questions ?? [])
            .Select(SafeText)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(5)
            .ToList();
    }

    private static List<string> DefaultClarificationQuestions()
    {
        return
        [
            "请补充检测目标或产品对象。",
            "请说明要判断的缺陷、测量项或识别内容。",
            "请说明输入来源是相机、文件还是仅先做元数据草稿。",
            "请说明 OK/NG 判定规则或输出目标。"
        ];
    }

    private static string DefaultReason(string intent)
    {
        return intent switch
        {
            IntentCasualChat => "这是普通寒暄，不需要进入规划。",
            IntentHelp => "这是能力咨询，不需要进入规划。",
            IntentActionableVisionPlan => "输入包含可规划的视觉检测、测量或识别目标。",
            IntentModifyExistingFlow => "输入是在已有流程上做修改。",
            IntentBuildFromConfirmedPlan => "用户已确认当前规划，可以进入构建。",
            IntentDirectBuildDebug => "开发者已显式启用直接 Build 调试。",
            _ => "需求信息不足，暂不可构建。"
        };
    }

    private static string UnauthorizedReason(string intent)
    {
        return intent switch
        {
            IntentCasualChat or IntentHelp => "模型路由鉴权失败，已使用安全规则回复。",
            IntentActionableVisionPlan => "模型路由鉴权失败，明显视觉需求将进入规则兜底规划。",
            _ => "模型路由鉴权失败，需求信息不足，暂不可构建。"
        };
    }

    private static string DefaultReply(string intent)
    {
        return intent switch
        {
            IntentCasualChat => "我在。你可以描述要做的视觉检测、测量、识别或输出流程。",
            IntentHelp => "我可以先帮你梳理视觉检测需求，形成规划；确认推荐方案后再构建可编辑的算子链。",
            IntentActionableVisionPlan => "已识别为可规划的视觉需求，将先进入 Plan 规划。",
            IntentModifyExistingFlow => "已识别为当前流程修改请求，将进入构建审计。",
            IntentBuildFromConfirmedPlan => "已识别为确认规划后的构建请求，将进入 Build。",
            IntentDirectBuildDebug => "已启用直接 Build 调试，本轮会跳过 Plan。",
            _ => "需求不足，暂不可构建。请补充检测目标、缺陷或识别内容、输入来源、OK/NG 规则。"
        };
    }

    private static bool LooksLikeCasual(string text)
    {
        var normalized = NormalizeChatText(text);
        return normalized is "hi" or "hello" or "hey" or "你好" or "您好" or "在吗" or "在不在" or "你在吗" or "早" or "早上好" or "下午好" or "晚上好" or "谢谢" or "thanks" or "thankyou";
    }

    private static bool LooksLikeHelp(string text)
    {
        var normalized = NormalizeChatText(text);
        return normalized.Contains("你能做什么", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("能做什么", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("你可以做什么", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("你会什么", StringComparison.OrdinalIgnoreCase) ||
               normalized is "help" or "帮助";
    }

    private static bool LooksLikeConfirmedPlanBuild(string text)
    {
        return ContainsAny(text, ["开始构建", "按推荐方案", "确认计划", "确认规划", "就按这个", "开始 build", "build now"]);
    }

    private static bool LooksLikeModifyExistingFlow(string text)
    {
        return ContainsAny(text, ["当前流程", "已有流程", "现有流程", "这个流程", "算子", "参数", "阈值", "连线", "改成", "修改", "调整", "删除", "新增"]);
    }

    private static bool LooksLikeActionableVisionNeed(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        if (ContainsAny(text, ["构建", "生成", "创建", "新建", "搭建", "设计", "做一个"]) &&
            ContainsAny(text, ["检测", "测量", "识别", "流程", "视觉", "外观", "缺陷", "条码", "二维码", "OCR", "尺寸"]))
        {
            return true;
        }

        var detailSignals = new[]
        {
            "贴歪", "条码", "可读", "Logo", "缺失", "箱角", "破损", "划痕", "裂纹", "孔距", "线序", "缺陷", "OK", "NG", "检测", "测量", "识别"
        };
        return detailSignals.Count(signal => text.Contains(signal, StringComparison.OrdinalIgnoreCase)) >= 2;
    }

    private static string NormalizeChatText(string text)
    {
        var chars = Clean(text)
            .Where(ch => !char.IsWhiteSpace(ch) &&
                         ch != '。' &&
                         ch != '！' &&
                         ch != '!' &&
                         ch != '？' &&
                         ch != '?' &&
                         ch != ',' &&
                         ch != '，' &&
                         ch != '.')
            .ToArray();
        return new string(chars).Trim();
    }

    private static string SummarizeJsonText(string? text, int maxChars)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var value = SafeText(text);
        return Truncate(value, maxChars);
    }

    private static string SafeText(string? value)
    {
        var text = Clean(value);
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        return UnsafeRegex.Replace(text, "<redacted>");
    }

    private static string SafeToken(string? value)
    {
        var text = SafeText(value);
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        return Regex.Replace(text, @"[^A-Za-z0-9_\-.:]", "_");
    }

    private static string Truncate(string? value, int maxChars)
    {
        var text = value ?? string.Empty;
        return text.Length <= maxChars ? text : text[..maxChars];
    }

    private static bool ContainsAny(string text, IEnumerable<string> terms)
    {
        return !string.IsNullOrWhiteSpace(text) &&
               terms.Any(term => !string.IsNullOrWhiteSpace(term) &&
                                 text.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    private static string Clean(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
    }
}

public sealed record VisionAgentIntentRouterPrompt(
    string SystemPrompt,
    List<ChatMessage> Messages);
