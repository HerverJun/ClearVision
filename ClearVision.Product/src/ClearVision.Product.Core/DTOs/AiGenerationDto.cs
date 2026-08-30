// AiGenerationDto.cs
// AI 生成 DTO 定义
// 定义 AI 流程生成请求与结果传输结构
// 作者：蘅芜君
using System.Text.Json.Serialization;
using ClearVision.Product.Core.Services;

namespace ClearVision.Product.Core.DTOs;

/// <summary>
/// AI 生成工作流的请求参数
/// </summary>
public record AiFlowGenerationRequest(
    string Description,
    string? AdditionalContext = null,
    string? SessionId = null,
    string? ExistingFlowJson = null,
    IReadOnlyList<string>? Attachments = null,
    GenerateFlowMode Mode = GenerateFlowMode.Auto,
    bool DebugPrompt = false,
    AiTemplateSelectionInfo? TemplateSelection = null
)
{
    /// <summary>
    /// Server-derived conversation/AgentRun owner authority. This value is
    /// deliberately excluded from transport JSON and must never be accepted
    /// from a browser request body.
    /// </summary>
    [JsonIgnore]
    public string OwnerHash { get; init; } = string.Empty;

    public string RequirementMode { get; init; } = AiRequirementModes.Strict;

    public bool UseVisionAgentGenerateFlow { get; init; }

    public string AgentGenerateFlowMode { get; init; } = AiAgentGenerateFlowModes.Scripted;

    public bool RuntimePreviewConsent { get; init; }

    public string? AgentRunId { get; init; }

    public VisionAgentBuildFromPlanRequest? BuildFromPlan { get; init; }
}

public sealed record RuntimePreviewConsent(
    bool Granted,
    string Scope = RuntimePreviewConsentScopes.SingleRequest);

public static class RuntimePreviewConsentScopes
{
    public const string SingleRequest = "single_request";
}

public static class AiRequirementModes
{
    public const string Draft = "draft";
    public const string Strict = "strict";
}

public static class AiAgentGenerateFlowModes
{
    public const string Scripted = "scripted";
    public const string Planner = "planner";
    public const string ToolLoop = "tool_loop";

    public static string Normalize(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            Planner => Planner,
            ToolLoop => ToolLoop,
            _ => Scripted
        };
    }
}

public enum GenerateFlowMode
{
    Auto,
    New,
    Modify,
    Explain,
    ReviewPendingParameters
}

public static class GenerateFlowModeExtensions
{
    public static GenerateFlowMode ParseOrAuto(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return GenerateFlowMode.Auto;

        return value.Trim().ToLowerInvariant() switch
        {
            "new" => GenerateFlowMode.New,
            "modify" => GenerateFlowMode.Modify,
            "explain" => GenerateFlowMode.Explain,
            "review_pending_parameters" => GenerateFlowMode.ReviewPendingParameters,
            _ => GenerateFlowMode.Auto
        };
    }

    public static string ToWireValue(this GenerateFlowMode mode)
    {
        return mode switch
        {
            GenerateFlowMode.New => "new",
            GenerateFlowMode.Modify => "modify",
            GenerateFlowMode.Explain => "explain",
            GenerateFlowMode.ReviewPendingParameters => "review_pending_parameters",
            _ => "auto"
        };
    }
}

/// <summary>
/// AI 生成工作流的响应结果
/// </summary>
public class AiFlowGenerationResult
{
    public const string CompletionStatusCompleted = "completed";
    public const string CompletionStatusCancelled = "cancelled";
    public const string CompletionStatusTimedOut = "timed_out";
    public const string CompletionStatusClarificationRequired = "clarification_required";
    public const string CompletionStatusFailed = "failed";

    public const string FailureTypeUserCancelled = "user_cancelled";
    public const string FailureTypeTimeout = "timeout";
    public const string FailureTypeClarificationRequired = "clarification_required";
    public const string FailureTypeSystemError = "system_error";
    public const string FailureTypeManualRetryRequired = "manual_retry_required";

    /// <summary>
    /// 是否生成成功
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// 生成的工作流 DTO（成功时不为 null）
    /// 实际类型为 ClearVision.Product.Application.DTOs.OperatorFlowDto，在此使用 object 以规避循环引用
    /// </summary>
    public object? Flow { get; set; }

    /// <summary>
    /// 错误消息（失败时不为 null）
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// 请求完成状态（completed / cancelled / timed_out / failed）
    /// </summary>
    public string CompletionStatus { get; set; } = CompletionStatusCompleted;

    /// <summary>
    /// 失败类型（如 user_cancelled / timeout / system_error）
    /// </summary>
    public string? FailureType { get; set; }

    /// <summary>
    /// AI 对本次生成的说明（解释为什么选择这些算子）
    /// </summary>
    public string? AiExplanation { get; set; }

    /// <summary>
    /// AI 的推理/思维链内容（来自 DeepSeek reasoning_content 或 Anthropic thinking）
    /// </summary>
    public string? Reasoning { get; set; }

    /// <summary>
    /// 需要用户手动确认的参数列表（算子ID → 参数名列表）
    /// </summary>
    public Dictionary<string, List<string>> ParametersNeedingReview { get; set; } = new();

    /// <summary>
    /// 实际使用的 AI 重试次数
    /// </summary>
    public int RetryCount { get; set; }

    /// <summary>
    /// 会话 ID（用于多轮增量修改）
    /// </summary>
    public string? SessionId { get; set; }

    /// <summary>
    /// 自动识别的会话意图（NEW / MODIFY / EXPLAIN）
    /// </summary>
    public string? DetectedIntent { get; set; }

    /// <summary>
    /// 沙盒空跑验证的结果（覆盖率等信息）
    /// </summary>
    public object? DryRunResult { get; set; }

    /// <summary>
    /// 模板优先命中时的推荐模板信息
    /// </summary>
    public AiRecommendedTemplateInfo? RecommendedTemplate { get; set; }

    /// <summary>
    /// template_fill / template_adapt / free_generate.
    /// </summary>
    public string GenerationMode { get; set; } = string.Empty;

    /// <summary>
    /// strict / relaxed / none.
    /// </summary>
    public string TemplateLockLevel { get; set; } = string.Empty;

    /// <summary>
    /// 当前输入是否仍需要在生成前补齐关键需求。
    /// </summary>
    public bool ClarificationRequired { get; set; }

    /// <summary>
    /// 需求抽取与澄清结果的结构化摘要。
    /// </summary>
    public AiRequirementBrief? RequirementBrief { get; set; }

    /// <summary>
    /// 结构化待确认参数（用于前端更精准展示）
    /// </summary>
    public List<AiPendingParameterInfo> PendingParameters { get; set; } = new();

    /// <summary>
    /// 模板落地所缺资源（模型/地址/标定等）
    /// </summary>
    public List<AiMissingResourceInfo> MissingResources { get; set; } = new();

    public List<VisionAgentGlobalVariableDraft> GlobalVariableDrafts { get; set; } = new();

    public List<VisionAgentGlobalVariableSourceBindingDraft> GlobalVariableSourceBindingDrafts { get; set; } = new();

    public List<VisionAgentGlobalVariableTargetBindingDraft> GlobalVariableTargetBindingDrafts { get; set; } = new();

    public List<VisionAgentGlobalVariableDiagnostic> GlobalVariableDiagnostics { get; set; } = new();

    public List<object> PendingActions { get; set; } = new();

    public object? ValidationPreview { get; set; }

    public List<object> ToolTrace { get; set; } = new();

    public string PlanId { get; set; } = string.Empty;

    public string PlanHash { get; set; } = string.Empty;

    public string? AgentRunId { get; set; }

    public VisionAgentPlanModeResult? PlanSnapshot { get; set; }

    public string ContractVersion { get; set; } = string.Empty;

    public string AnswerSetFingerprint { get; set; } = string.Empty;

    public string RequestedMode { get; set; } = AiAgentGenerateFlowModes.Scripted;

    public string EffectiveMode { get; set; } = AiAgentGenerateFlowModes.Scripted;

    public bool ToolLoopEntered { get; set; }

    public string FallbackReason { get; set; } = string.Empty;

    public VisionAgentBuildResult? BuildResult { get; set; }

    public VisionAgentBuildReadinessSnapshot? BuildReadiness { get; set; }

    /// <summary>
    /// 本次失败的结构化摘要（成功时为空）
    /// </summary>
    public AiFailureSummary? FailureSummary { get; set; }

    /// <summary>
    /// 最近一次尝试的结构化诊断（可用于前端闭环提示）
    /// </summary>
    public List<AiAttemptDiagnostic> LastAttemptDiagnostics { get; set; } = new();

    /// <summary>
    /// 可选：本次发送给模型的调试追踪信息（开发态或显式开启时返回）
    /// </summary>
    public AiManualRetryInfo? ManualRetry { get; set; }
    public object? PromptTrace { get; set; }

    /// <summary>
    /// Template candidates produced by the deterministic scenario matcher.
    /// </summary>
    public List<AiTemplateCandidateInfo> TemplateCandidates { get; set; } = new();

    /// <summary>
    /// Structured generation timeline for workbench diagnostics.
    /// </summary>
    public List<AiGenerationStageDiagnostic> StageTimeline { get; set; } = new();

    /// <summary>
    /// Knowledge graph diagnostics (warnings about missing resources, anti-patterns, etc.)
    /// Filtered from validation diagnostics with category == "knowledge".
    /// </summary>
    public List<AiValidationDiagnostic>? KnowledgeDiagnostics { get; set; }

    public string TurnIntent { get; set; } = AiTurnIntents.Unknown;
    public string InteractionState { get; set; } = AiInteractionStates.Idle;
    public string RouterConfidence { get; set; } = AiRouterConfidence.Low;
    public List<string> BlockingClarificationFields { get; set; } = new();
    public List<string> NonBlockingMissingFields { get; set; } = new();
    public AiRequirementMaturityResult? RequirementMaturity { get; set; }
    public AiDecisionTrace? DecisionTrace { get; set; }
    public AiPersistenceWarning? PersistenceWarning { get; set; }
}

public class AiPersistenceWarning
{
    public string Code { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public object? PersistenceStatus { get; set; }
}

public static class AiTurnIntents
{
    public const string ManualRetryRepair = "manual_retry_repair";
    public const string ClarificationAnswer = "clarification_answer";
    public const string ReviewPendingParameters = "review_pending_parameters";
    public const string ExplainFlow = "explain_flow";
    public const string ModifyFlow = "modify_flow";
    public const string NewFlow = "new_flow";
    public const string ChatOrHelp = "chat_or_help";
    public const string Unknown = "unknown";
}

public static class AiInteractionStates
{
    public const string Idle = "idle";
    public const string Clarifying = "clarifying";
    public const string Generating = "generating";
    public const string Modifying = "modifying";
    public const string ReviewingParameters = "reviewing_parameters";
    public const string ManualRetry = "manual_retry";
    public const string Completed = "completed";
    public const string Failed = "failed";
}

public static class AiRouterConfidence
{
    public const string High = "high";
    public const string Medium = "medium";
    public const string Low = "low";
}

public class AiFailureSummary
{
    public string Category { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string RepairTarget { get; set; } = string.Empty;
    public int RetryCount { get; set; }
    public string LastOutputSummary { get; set; } = string.Empty;
}

public class AiAttemptDiagnostic
{
    public int AttemptNumber { get; set; }
    public string Stage { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string OutputSummary { get; set; } = string.Empty;
    public List<AiValidationDiagnostic> Issues { get; set; } = new();
}

public class AiManualRetryInfo
{
    public bool Required { get; set; }
    public string Stage { get; set; } = string.Empty;
    public string Draft { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string RepairTarget { get; set; } = string.Empty;
    public string LastOutputSummary { get; set; } = string.Empty;
    public List<AiAttemptDiagnostic> Diagnostics { get; set; } = new();
}

/// <summary>
/// AI 原始输出的结构（AI 应严格按此格式输出 JSON）
/// </summary>
public class AiGeneratedFlowJson
{
    /// <summary>
    /// Schema version for the generated draft contract.
    /// </summary>
    public string SchemaVersion { get; set; } = "1.0";

    /// <summary>
    /// template_fill / template_adapt / free_generate.
    /// </summary>
    public string GenerationMode { get; set; } = string.Empty;

    /// <summary>
    /// strict / relaxed / none.
    /// </summary>
    public string TemplateLockLevel { get; set; } = string.Empty;

    /// <summary>
    /// AI 对生成结果的解释说明
    /// </summary>
    public string Explanation { get; set; } = string.Empty;

    /// <summary>
    /// 生成的算子列表
    /// </summary>
    public List<AiGeneratedOperator> Operators { get; set; } = new();

    /// <summary>
    /// 生成的连线列表
    /// </summary>
    public List<AiGeneratedConnection> Connections { get; set; } = new();

    /// <summary>
    /// 需要用户确认的参数（算子临时ID → 参数名列表）
    /// </summary>
    public Dictionary<string, List<string>> ParametersNeedingReview { get; set; } = new();

    /// <summary>
    /// AI 输出的推荐模板信息（可选）
    /// </summary>
    public AiRecommendedTemplateInfo? RecommendedTemplate { get; set; }

    /// <summary>
    /// AI 输出的待确认参数（可选）
    /// </summary>
    public List<AiPendingParameterInfo> PendingParameters { get; set; } = new();

    /// <summary>
    /// AI 输出的缺失资源（可选）
    /// </summary>
    public List<AiMissingResourceInfo> MissingResources { get; set; } = new();
}

public class AiRecommendedTemplateInfo
{
    public string? TemplateId { get; set; }
    public string TemplateName { get; set; } = string.Empty;
    public string? TemplateVersion { get; set; }
    public string? ScenarioKey { get; set; }
    public string? Industry { get; set; }
    public string MatchReason { get; set; } = string.Empty;
    public string MatchMode { get; set; } = string.Empty;
    public double Confidence { get; set; }
    public List<string> MatchedFields { get; set; } = new();
    public List<string> MissingSignals { get; set; } = new();
}

public class AiPendingParameterInfo
{
    public string OperatorId { get; set; } = string.Empty;
    public string ActualOperatorId { get; set; } = string.Empty;
    public List<string> ParameterNames { get; set; } = new();
}

public class AiMissingResourceInfo
{
    public string CanonicalId { get; set; } = string.Empty;
    public string ResourceType { get; set; } = string.Empty;
    public string ResourceName { get; set; } = string.Empty;
    public string ResourceKey { get; set; } = string.Empty;
    public string OperatorKey { get; set; } = string.Empty;
    public string OperatorId { get; set; } = string.Empty;
    public string OperatorType { get; set; } = string.Empty;
    public int OperatorIndex { get; set; } = -1;
    public string ParameterName { get; set; } = string.Empty;
    public string Status { get; set; } = VisionAgentResourceStatuses.Pending;
    public string BlockingScope { get; set; } = VisionAgentResourceBlockingScopes.DeployRun;
    public string Source { get; set; } = string.Empty;
    public string ResolutionTarget { get; set; } = VisionAgentResourceResolutionTargets.PlanWorkbench;
    public string DraftPolicy { get; set; } = VisionAgentResourceDraftPolicies.DraftAllowed;
    public string Description { get; set; } = string.Empty;
    public List<string> Aliases { get; set; } = new();
}

public class AiRequirementBrief
{
    public string ScenarioKey { get; set; } = string.Empty;
    public string ScenarioName { get; set; } = string.Empty;
    public string IntentType { get; set; } = string.Empty;
    public string RequirementMode { get; set; } = AiRequirementModes.Strict;
    public double Confidence { get; set; }
    public bool HasOpenQuestions { get; set; }
    public bool ClarificationRequired { get; set; }
    public bool CanGenerateDraftNow { get; set; }
    public string DraftRiskLevel { get; set; } = "medium";
    public List<string> ObjectTypes { get; set; } = new();
    public List<string> DefectTypes { get; set; } = new();
    public List<string> MeasurementTargets { get; set; } = new();
    public List<string> RequiredResources { get; set; } = new();
    public List<string> RequiredFields { get; set; } = new();
    public List<string> KnownFacts { get; set; } = new();
    public List<string> MissingFacts { get; set; } = new();
    public List<string> AttachmentFacts { get; set; } = new();
    public string? ObjectName { get; set; }
    public string? ImageSource { get; set; }
    public string? OutputTarget { get; set; }
    public string? DecisionRule { get; set; }
    public string? RoiRequirement { get; set; }
    public string? CalibrationRequirement { get; set; }
    public List<string> BlockingClarificationFields { get; set; } = new();
    public List<string> NonBlockingMissingFields { get; set; } = new();
    public List<AiClarificationQuestion> ClarificationQuestions { get; set; } = new();
}

public class AiClarificationQuestion
{
    public string Field { get; set; } = string.Empty;
    public string Question { get; set; } = string.Empty;
    public bool Required { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string Priority { get; set; } = string.Empty;
    public List<string> Options { get; set; } = new();
}

public class AiTemplateCandidateInfo
{
    public string? TemplateId { get; set; }
    public string TemplateName { get; set; } = string.Empty;
    public string? TemplateVersion { get; set; }
    public string? ScenarioKey { get; set; }
    public string? Industry { get; set; }
    public double Confidence { get; set; }
    public string MatchReason { get; set; } = string.Empty;
    public List<string> MatchedFields { get; set; } = new();
    public List<string> MissingSignals { get; set; } = new();
}

public class AiTemplateSelectionInfo
{
    public string Mode { get; set; } = string.Empty;
    public string? TemplateId { get; set; }
    public string? ScenarioKey { get; set; }
}

public class AiGenerationStageDiagnostic
{
    public string Stage { get; set; } = string.Empty;
    public string Status { get; set; } = "completed";
    public string Summary { get; set; } = string.Empty;
    public long DurationMs { get; set; }
    public Dictionary<string, string> Metadata { get; set; } = new();
}

public class AiGeneratedOperator
{
    /// <summary>
    /// AI 分配的临时 ID，用于在 connections 中引用（格式：op_1, op_2...）
    /// </summary>
    public string TempId { get; set; } = string.Empty;

    /// <summary>
    /// 算子类型，必须与 OperatorType 枚举名完全一致
    /// </summary>
    public string OperatorType { get; set; } = string.Empty;

    /// <summary>
    /// 用户友好的显示名称（可自定义，如"圆测量#1"）
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// 算子参数键值对（参数名 → 参数值字符串）
    /// </summary>
    public Dictionary<string, string> Parameters { get; set; } = new();
}

public class AiGeneratedConnection
{
    public string SourceTempId { get; set; } = string.Empty;
    public string SourcePortName { get; set; } = string.Empty;
    public string TargetTempId { get; set; } = string.Empty;
    public string TargetPortName { get; set; } = string.Empty;
}
