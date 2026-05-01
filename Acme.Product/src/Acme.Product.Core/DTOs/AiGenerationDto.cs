// AiGenerationDto.cs
// AI 生成 DTO 定义
// 定义 AI 流程生成请求与结果传输结构
// 作者：蘅芜君
using Acme.Product.Core.Services;

namespace Acme.Product.Core.DTOs;

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
);

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
    public const string CompletionStatusFailed = "failed";

    public const string FailureTypeUserCancelled = "user_cancelled";
    public const string FailureTypeTimeout = "timeout";
    public const string FailureTypeSystemError = "system_error";
    public const string FailureTypeManualRetryRequired = "manual_retry_required";

    /// <summary>
    /// 是否生成成功
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// 生成的工作流 DTO（成功时不为 null）
    /// 实际类型为 Acme.Product.Application.DTOs.OperatorFlowDto，在此使用 object 以规避循环引用
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
    /// 结构化待确认参数（用于前端更精准展示）
    /// </summary>
    public List<AiPendingParameterInfo> PendingParameters { get; set; } = new();

    /// <summary>
    /// 模板落地所缺资源（模型/地址/标定等）
    /// </summary>
    public List<AiMissingResourceInfo> MissingResources { get; set; } = new();

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
    /// When true, the system needs more information before calling the LLM.
    /// The UI should display ClarificationQuestions and wait for user input.
    /// </summary>
    public bool ClarificationRequired { get; set; }

    /// <summary>
    /// Parsed requirement summary used by the AI workbench before and after generation.
    /// </summary>
    public AiRequirementBrief? RequirementBrief { get; set; }

    /// <summary>
    /// Template candidates produced by the deterministic scenario matcher.
    /// </summary>
    public List<AiTemplateCandidateInfo> TemplateCandidates { get; set; } = new();

    /// <summary>
    /// Structured generation timeline for workbench diagnostics.
    /// </summary>
    public List<AiGenerationStageDiagnostic> StageTimeline { get; set; } = new();
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
    public string ResourceType { get; set; } = string.Empty;
    public string ResourceKey { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}

public enum ClarificationLevel
{
    Required,
    Recommended,
    Optional
}

public class AiRequirementBrief
{
    /// <summary>appearance_defect / measurement / code_reading / wire_sequence / missing_part / calibration / other</summary>
    public string SceneType { get; set; } = string.Empty;

    public string ScenarioKey { get; set; } = string.Empty;
    public string ScenarioName { get; set; } = string.Empty;

    /// <summary>线束装配 / 空调制造 / 包装终检 / 通用制造 ...</summary>
    public string Industry { get; set; } = string.Empty;

    public string IntentType { get; set; } = string.Empty;

    /// <summary>产品/部件名</summary>
    public string ObjectName { get; set; } = string.Empty;

    public List<string> ObjectTypes { get; set; } = new();

    /// <summary>划伤、破损、脏污、漏装、错序 ...</summary>
    public List<string> DefectTypes { get; set; } = new();

    /// <summary>孔距、间距、圆心距离 ...</summary>
    public List<string> MeasurementTargets { get; set; } = new();

    /// <summary>camera / file / unknown</summary>
    public string ImageSource { get; set; } = "unknown";

    /// <summary>software / hardware / continuous / unknown</summary>
    public string TriggerMode { get; set; } = "unknown";

    /// <summary>ResultOutput / PLC / Database / unknown</summary>
    public string OutputTarget { get; set; } = "unknown";

    /// <summary>true / false / unknown</summary>
    public string AiModelRequired { get; set; } = "unknown";

    public string ModelResource { get; set; } = "missing";
    public string LabelsPath { get; set; } = "missing";

    /// <summary>none / region / unknown</summary>
    public string RoiRequirement { get; set; } = "unknown";

    /// <summary>none / pixel_to_world / hand_eye / unknown</summary>
    public string CalibrationRequirement { get; set; } = "unknown";

    /// <summary>OK/NG 判定逻辑</summary>
    public string DecisionRule { get; set; } = string.Empty;

    /// <summary>需求理解置信度 (0-1)</summary>
    public double Confidence { get; set; }

    /// <summary>缺失的关键字段</summary>
    public List<string> MissingFields { get; set; } = new();

    public List<string> RequiredResources { get; set; } = new();
    public List<AiClarificationQuestion> ClarificationQuestions { get; set; } = new();

    /// <summary>Attachment count and summary metadata</summary>
    public int AttachmentCount { get; set; }

    /// <summary>Whether the active model supports visual input for attachments</summary>
    public bool ModelSupportsVision { get; set; }

    /// <summary>Attachment observation summary when vision is available</summary>
    public string AttachmentObservation { get; set; } = string.Empty;
}

public class AiClarificationQuestion
{
    public string Field { get; set; } = string.Empty;
    public string Question { get; set; } = string.Empty;
    public bool Required { get; set; }

    /// <summary>Required / Recommended / Optional</summary>
    public string Level { get; set; } = "Required";

    /// <summary>Predefined options the user can select from</summary>
    public List<string> Options { get; set; } = new();

    /// <summary>Why this question matters for the workflow</summary>
    public string Reason { get; set; } = string.Empty;
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
