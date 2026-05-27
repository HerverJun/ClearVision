using Acme.Product.Core.DTOs;
using System.Text.RegularExpressions;

namespace Acme.Product.Infrastructure.AI;

public interface IAiTurnRouter
{
    AiTurnRoute Route(AiTurnRouteRequest request);
}

public sealed record AiTurnRouteRequest(
    string? Description,
    string? AdditionalContext,
    GenerateFlowMode RequestedMode,
    ConversationSession? Session,
    bool HasExistingFlow,
    IReadOnlyList<string>? Attachments);

public sealed record AiTurnRoute(
    string TurnIntent,
    string InteractionState,
    string Confidence,
    bool ShouldShortCircuit = false,
    bool ShouldBypassClarification = false,
    string? Reply = null)
{
    public static AiTurnRoute NewFlow(string confidence = AiRouterConfidence.Medium) =>
        new(AiTurnIntents.NewFlow, AiInteractionStates.Generating, confidence);
}

public sealed class AiTurnRouter : IAiTurnRouter
{
    private static readonly string[] BusinessSignals =
    [
        "检测", "测量", "识别", "流程", "算子", "参数", "阈值", "模型", "PLC", "plc", "数据库",
        "中文", "中文化", "修改", "调整", "改成", "增加", "新增", "删除", "移除", "解释",
        "缺陷", "外观", "线序", "端子", "ROI", "roi", "标定", "输出", "模板", "工程",
        "缺资源", "待确认", "DryRun", "dryrun", "校验",
        "inspect", "inspection", "detect", "detection", "measure", "measurement", "workflow", "flow",
        "operator", "parameter", "threshold", "database", "chinese", "defect", "sequence", "calibration",
        "output", "template", "validate", "validation"
    ];

    private static readonly string[] NewFlowSignals =
    [
        "生成", "创建", "新建", "新增", "构建", "搭建", "做一个", "帮我做", "搭一个", "设计", "配置", "从头", "重新", "重新做", "重做",
        "新流程", "新的流程", "另一个流程", "new flow", "new workflow", "create", "build", "generate", "start over", "from scratch"
    ];

    private static readonly string[] ModifySignals =
    [
        "改", "修改", "调整", "优化", "调优", "增加", "新增", "新建", "补充", "删除", "删掉", "移除",
        "替换", "改成", "变成", "中文", "中文化", "阈值", "参数", "算子名称", "displayName",
        "change", "update", "adjust", "add", "remove", "replace", "refine"
    ];

    private static readonly string[] NewFlowScopeSignals =
    [
        "流程", "工程", "检测", "测量", "识别", "方案", "workflow", "flow", "inspection", "detection", "measurement"
    ];

    private static readonly string[] ExistingFlowAnchors =
    [
        "当前流程", "当前工程", "当前方案", "现有流程", "现有工程", "已有流程", "已有工程", "这个流程", "这个工程",
        "原流程", "原工程", "现在的流程", "现在的工程", "current flow", "existing flow", "this flow"
    ];

    private static readonly string[] FlowEditTargets =
    [
        "算子", "节点", "参数", "阈值", "连线", "连接", "名称", "displayName", "operator", "node", "parameter", "threshold", "connection"
    ];

    private static readonly string[] ExplainSignals =
    [
        "解释", "为什么", "什么意思", "含义", "讲解", "说明", "原理", "思路",
        "explain", "why", "reason", "meaning"
    ];

    private static readonly string[] ReviewSignals =
    [
        "参数审核", "审核参数", "确认参数", "待确认参数", "补参数", "补录参数", "资源缺失", "缺资源",
        "review pending", "pending parameter"
    ];

    private static readonly string[] RepairSignals =
    [
        "继续修复", "继续纠错", "按草稿修复", "手动修复", "重试修复", "修正上一轮", "基于上一轮",
        "请基于上一轮需求继续修正工作流 JSON", "请只返回一个完整且可解析的 JSON 对象",
        "上一轮输出摘要", "上一轮模型原始输出", "优先修复：", "[format/invalid_json]", "[validation/"
    ];

    private static readonly HashSet<string> ChatPhrases = new(StringComparer.OrdinalIgnoreCase)
    {
        "hi", "hello", "hey", "你好", "您好", "在吗", "在不在", "有人吗", "你在吗",
        "早", "早上好", "下午好", "晚上好", "谢谢", "thanks", "thank you",
        "你能做什么", "能做什么", "你可以做什么", "你会什么", "帮我做什么", "help", "帮助"
    };

    public AiTurnRoute Route(AiTurnRouteRequest request)
    {
        var text = NormalizeUserText(request.Description);
        var hasText = !string.IsNullOrWhiteSpace(text);
        var hasAdditionalContext = !string.IsNullOrWhiteSpace(request.AdditionalContext);
        var hasAttachments = request.Attachments is { Count: > 0 };
        var latestPendingClarificationPayload = FindLatestPendingClarificationPayload(request.Session);
        var latestManualRetryPayload = FindLatestManualRetryPayload(request.Session);
        var pendingClarification = latestPendingClarificationPayload?.ClarificationRequired == true;
        var pendingManualRetry = latestManualRetryPayload != null;
        var hasBusinessSignal = ContainsAny(text, BusinessSignals);
        var looksLikeExplicitNewFlow = LooksLikeExplicitNewFlowRequest(text);
        var looksLikeExistingFlowEdit = LooksLikeExistingFlowEditRequest(text);

        if (pendingManualRetry && (ContainsAny(text, RepairSignals) || IsManualRetryRepairDraft(text)))
        {
            return new AiTurnRoute(
                AiTurnIntents.ManualRetryRepair,
                AiInteractionStates.ManualRetry,
                AiRouterConfidence.High,
                ShouldBypassClarification: true);
        }

        if (pendingClarification)
        {
            var pendingQuestions = latestPendingClarificationPayload?.RequirementBrief?.ClarificationQuestions
                ?.Where(question => !string.IsNullOrWhiteSpace(question.Field))
                .ToList() ?? new List<AiClarificationQuestion>();

            if (pendingQuestions.Count > 0 &&
                pendingQuestions.Any(question => LooksLikeAnswerForField(text, question)) &&
                !LooksLikeSelfContainedNewRequirement(text))
            {
                return new AiTurnRoute(
                    AiTurnIntents.ClarificationAnswer,
                    AiInteractionStates.Clarifying,
                    AiRouterConfidence.High);
            }

            if (LooksLikeSelfContainedNewRequirement(text))
            {
                return AiTurnRoute.NewFlow(AiRouterConfidence.High);
            }
        }

        if (request.RequestedMode == GenerateFlowMode.ReviewPendingParameters ||
            ContainsAny(text, ReviewSignals))
        {
            if (!request.HasExistingFlow)
            {
                return BuildNoFlowRoute(
                    "当前没有可审核参数的工程。请先描述要新建的检测、测量或识别流程，或先应用一版工程后再审核参数。");
            }

            return new AiTurnRoute(
                AiTurnIntents.ReviewPendingParameters,
                AiInteractionStates.ReviewingParameters,
                AiRouterConfidence.High,
                ShouldBypassClarification: true);
        }

        if (request.RequestedMode == GenerateFlowMode.Explain ||
            (request.HasExistingFlow && ContainsAny(text, ExplainSignals)))
        {
            if (!request.HasExistingFlow)
            {
                return BuildNoFlowRoute(
                    "当前没有可解释的工程。请先描述要新建的视觉流程，或应用一版工程后再让我解释。");
            }

            return new AiTurnRoute(
                AiTurnIntents.ExplainFlow,
                AiInteractionStates.Generating,
                AiRouterConfidence.High,
                ShouldBypassClarification: true);
        }

        if (request.RequestedMode == GenerateFlowMode.New ||
            (request.HasExistingFlow &&
             looksLikeExplicitNewFlow &&
             !looksLikeExistingFlowEdit &&
             request.RequestedMode != GenerateFlowMode.Modify))
        {
            return AiTurnRoute.NewFlow(AiRouterConfidence.High);
        }

        if ((request.RequestedMode == GenerateFlowMode.Modify ||
             ContainsAny(text, ModifySignals) ||
             looksLikeExistingFlowEdit) &&
            request.HasExistingFlow)
        {
            return new AiTurnRoute(
                AiTurnIntents.ModifyFlow,
                AiInteractionStates.Modifying,
                AiRouterConfidence.High,
                ShouldBypassClarification: true);
        }

        if (!request.HasExistingFlow &&
            (request.RequestedMode == GenerateFlowMode.Modify ||
             ContainsAny(text, ModifySignals) ||
             looksLikeExistingFlowEdit) &&
            !looksLikeExplicitNewFlow)
        {
            return BuildNoFlowRoute(
                "当前没有可修改的工程。请先描述要新建的视觉流程，或应用一版工程后再提出微调。");
        }

        if ((ContainsAny(text, NewFlowSignals) && hasBusinessSignal) ||
            looksLikeExplicitNewFlow ||
            (!request.HasExistingFlow &&
             hasBusinessSignal &&
             !ContainsAny(text, ExplainSignals) &&
             !ContainsAny(text, ReviewSignals)) ||
            (request.HasExistingFlow && hasBusinessSignal && !ContainsAny(text, ModifySignals)))
        {
            return AiTurnRoute.NewFlow(hasBusinessSignal ? AiRouterConfidence.High : AiRouterConfidence.Medium);
        }

        if (IsHighConfidenceChatOrHelp(text, hasText, hasAttachments, hasBusinessSignal, request.RequestedMode))
        {
            return new AiTurnRoute(
                AiTurnIntents.ChatOrHelp,
                AiInteractionStates.Idle,
                AiRouterConfidence.High,
                ShouldShortCircuit: true,
                Reply: BuildChatReply(request.HasExistingFlow));
        }

        if (hasAdditionalContext)
        {
            return new AiTurnRoute(
                AiTurnIntents.Unknown,
                AiInteractionStates.Generating,
                AiRouterConfidence.Low);
        }

        return new AiTurnRoute(
            AiTurnIntents.Unknown,
            AiInteractionStates.Idle,
            AiRouterConfidence.Low,
            ShouldShortCircuit: true,
            Reply: BuildUnknownReply(request.HasExistingFlow));
    }

    private static string NormalizeUserText(string? description)
    {
        return string.IsNullOrWhiteSpace(description)
            ? string.Empty
            : description.Trim();
    }

    private static ConversationTurnPayload? FindLatestPendingClarificationPayload(ConversationSession? session)
    {
        if (session?.History == null)
            return null;

        foreach (var turn in session.History
                     .Where(turn => turn.Role.Equals("assistant", StringComparison.OrdinalIgnoreCase))
                     .OrderByDescending(turn => turn.TimestampUtc))
        {
            if (turn.Payload?.ClarificationRequired == true)
                return turn.Payload;

            if (IsIgnorableInteractionPayload(turn.Payload))
                continue;

            return null;
        }

        return null;
    }

    private static ConversationTurnPayload? FindLatestManualRetryPayload(ConversationSession? session)
    {
        if (session?.History == null)
            return null;

        foreach (var turn in session.History
                     .Where(turn => turn.Role.Equals("assistant", StringComparison.OrdinalIgnoreCase))
                     .OrderByDescending(turn => turn.TimestampUtc))
        {
            var payload = turn.Payload;
            if (payload?.ManualRetry?.Required == true ||
                string.Equals(payload?.Status, AiFlowGenerationResult.FailureTypeManualRetryRequired, StringComparison.OrdinalIgnoreCase))
            {
                return payload;
            }

            if (IsIgnorableInteractionPayload(payload))
                continue;

            return null;
        }

        return null;
    }

    private static bool IsIgnorableInteractionPayload(ConversationTurnPayload? payload)
    {
        return string.Equals(payload?.Kind, "assistant_interaction", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(payload?.TurnIntent, AiTurnIntents.ChatOrHelp, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(payload?.TurnIntent, AiTurnIntents.Unknown, StringComparison.OrdinalIgnoreCase);
    }

    private static AiTurnRoute BuildNoFlowRoute(string reply)
    {
        return new AiTurnRoute(
            AiTurnIntents.Unknown,
            AiInteractionStates.Idle,
            AiRouterConfidence.High,
            ShouldShortCircuit: true,
            Reply: reply);
    }

    private static bool IsHighConfidenceChatOrHelp(
        string text,
        bool hasText,
        bool hasAttachments,
        bool hasBusinessSignal,
        GenerateFlowMode requestedMode)
    {
        if (!hasText || hasAttachments || hasBusinessSignal || requestedMode != GenerateFlowMode.Auto)
            return false;

        var normalized = NormalizeChatText(text);
        if (normalized.Length > 24)
            return false;

        if (ChatPhrases.Contains(normalized))
            return true;

        return normalized is "?" or "？";
    }

    private static string NormalizeChatText(string text)
    {
        var chars = text.Trim()
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

    private static string BuildChatReply(bool hasExistingFlow)
    {
        return hasExistingFlow
            ? "我在。你可以继续说明要修改、解释或补齐当前工程的哪一部分。"
            : "我在。你可以直接描述要做的视觉检测、测量、识别或输出流程。";
    }

    private static string BuildUnknownReply(bool hasExistingFlow)
    {
        return hasExistingFlow
            ? "我还不能确定这一轮要做什么。你可以说明要继续修改当前工程，或描述一个新的检测、测量、识别需求。"
            : "我还不能确定具体业务目标。请描述要检测、测量或识别的对象，以及希望输出到哪里。";
    }

    private static bool IsManualRetryRepairDraft(string text)
    {
        return ContainsAny(text, RepairSignals);
    }

    private static bool LooksLikeSelfContainedNewRequirement(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        return ContainsAny(text, NewFlowSignals) &&
               ContainsAny(text, BusinessSignals);
    }

    private static bool LooksLikeExplicitNewFlowRequest(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        if (LooksLikeExistingFlowEditRequest(text))
            return false;

        if (ContainsAny(text, ["从头", "重新", "重做", "重新做", "另一个流程", "新流程", "新的流程", "start over", "from scratch", "new flow", "new workflow"]))
            return true;

        if (!ContainsAny(text, NewFlowSignals))
            return false;

        if (ContainsAny(text, NewFlowScopeSignals))
            return true;

        return Regex.IsMatch(text, "(新增|新建|创建|生成|构建|搭建|设计).{0,12}(流程|工程|检测|测量|识别|方案)", RegexOptions.IgnoreCase);
    }

    private static bool LooksLikeExistingFlowEditRequest(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var anchoredEdit = ContainsAny(text, ExistingFlowAnchors) && ContainsAny(text, FlowEditTargets);
        var directEditTarget = Regex.IsMatch(text, "(新增|新建|增加|添加|删除|移除|修改|调整|改).{0,12}(算子|节点|参数|阈值|连线|连接|名称|displayName)", RegexOptions.IgnoreCase);
        return anchoredEdit || directEditTarget;
    }

    private static bool LooksLikeAnswerForField(string message, AiClarificationQuestion question)
    {
        var text = message.Trim();
        if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(question.Field))
            return false;

        if (ContainsAny(text, FieldLabels(question.Field)))
            return true;

        if (question.Options.Any(option =>
                !string.IsNullOrWhiteSpace(option) &&
                text.Contains(option, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return question.Field switch
        {
            "scene" => ContainsAny(text, ["外观", "缺陷", "漏装", "有无", "线序", "尺寸", "测量", "分类"]),
            "object_type" => ContainsAny(text, ["包装箱", "纸箱", "箱体", "产品", "金属件", "连接器", "端子", "空调", "内机", "外机", "遥控器", "铜孔", "孔位", "标签"]),
            "defect_type" => ContainsAny(text, ["破损", "裂纹", "划伤", "划痕", "压痕", "凹坑", "脏污", "污渍", "标签异常", "封箱异常", "scratch", "dent", "damage", "broken", "stain"]),
            "measurement_target" => ContainsAny(text, ["孔距", "圆心距", "间距", "距离", "缝隙", "直径", "半径", "角度", "mm", "毫米"]),
            "output_target" => ContainsAny(text, ["PLC", "数据库", "界面", "UI", "屏幕", "文件", "CSV", "Excel"]),
            "model_path" => ContainsAny(text, ["模型", "训练", "YOLO", "onnx", "pt", "路径", "已有模型", "传统视觉"]),
            "roi" => ContainsAny(text, ["ROI", "整图", "固定", "区域", "多ROI", "范围"]),
            "calibration" => ContainsAny(text, ["标定", "像素", "物理", "毫米", "mm", "手眼"]),
            "ambiguous_negative_signal" => text.Length >= 4,
            _ => text.Length >= 2
        };
    }

    private static IReadOnlyList<string> FieldLabels(string field)
    {
        return field switch
        {
            "scene" => ["场景", "场景类型"],
            "object_type" => ["检测对象", "对象", "产品"],
            "defect_type" => ["缺陷类别", "缺陷类型", "缺陷", "瑕疵"],
            "measurement_target" => ["测量目标", "测量项", "尺寸"],
            "output_target" => ["输出目标", "输出", "结果"],
            "model_path" => ["模型资源", "模型", "标签资源"],
            "roi" => ["ROI", "区域", "范围"],
            "calibration" => ["标定", "换算"],
            "ambiguous_negative_signal" => ["歧义", "补充信息"],
            _ => [field]
        };
    }

    private static bool ContainsAny(string text, IEnumerable<string> terms)
    {
        return !string.IsNullOrWhiteSpace(text) &&
               terms.Any(term => !string.IsNullOrWhiteSpace(term) &&
                                 text.Contains(term, StringComparison.OrdinalIgnoreCase));
    }
}
