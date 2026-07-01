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

public interface IVisionAgentSemanticExtractorService
{
    Task<VisionAgentSemanticExtractionResult> ExtractAsync(
        VisionAgentSemanticExtractionRequest request,
        CancellationToken cancellationToken);
}

public interface IVisionAgentSemanticExtractionCompletionSource
{
    Task<string> CompleteAsync(
        VisionAgentSemanticExtractionCompletionRequest request,
        CancellationToken cancellationToken);
}

public sealed record VisionAgentSemanticExtractionCompletionRequest(
    string SystemPrompt,
    List<ChatMessage> Messages,
    string ModelRole);

internal static class VisionAgentSemanticExtractionSafety
{
    private static readonly Regex UnsafeRegex = new(
        @"(?i)((?:rawPrompt|raw_prompt|systemPrompt|system_prompt|chain[-_ ]?of[-_ ]?thought|reasoning_content)(?:\s*[:=]\s*[^\s,;]+)?|[A-Za-z]:\\[^\s,;]+|\\\\[^\s,;]+|/(?:users|home|var|tmp|mnt|data)/[^\s,;]+|data:image/[^\s,;]+|base64[^\s,;]*|sk-[A-Za-z0-9_\-]{8,}|(?:api[_-]?key|x-api-key|token|secret|authorization|headers?|baseUrl|base_url|endpoint)\s*[:=]\s*[^\s,;]+|bearer\s+[A-Za-z0-9._\-]+|\b(?:\d{1,3}\.){3}\d{1,3}\b|\bDB\d+\.DB[XBWD]\d+(?:\.\d+)?\b|\bM\d+(?:\.\d+)?\b|\bD\d+\b|plc://[^\s,;]*)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static VisionAgentSemanticExtractionResult? Sanitize(
        VisionAgentSemanticExtractionResult? semantic)
    {
        if (semantic == null)
        {
            return null;
        }

        return semantic with
        {
            Intent = SafeToken(semantic.Intent),
            TaskType = SafeToken(semantic.TaskType),
            Confidence = Clamp(semantic.Confidence),
            TaskTypeConfidence = Clamp(semantic.TaskTypeConfidence),
            InspectionObject = SafeText(semantic.InspectionObject),
            TargetAttribute = SafeText(semantic.TargetAttribute),
            DefectType = SafeText(semantic.DefectType),
            MeasurementTarget = SafeText(semantic.MeasurementTarget),
            ImageSource = SafeText(semantic.ImageSource),
            OkCondition = SafeText(semantic.OkCondition),
            NgCondition = SafeText(semantic.NgCondition),
            OutputTarget = SafeText(semantic.OutputTarget),
            SuggestedRoute = SafeText(semantic.SuggestedRoute),
            ObjectSignals = NormalizeList(semantic.ObjectSignals),
            TaskSignals = NormalizeList(semantic.TaskSignals),
            MissingFields = NormalizeList(semantic.MissingFields, tokenOnly: true),
            ClarificationQuestions = NormalizeList(semantic.ClarificationQuestions),
            Source = NormalizeSource(semantic.Source),
            FailureCode = SafeToken(semantic.FailureCode),
            SanitizedErrorMessage = Truncate(SafeText(semantic.SanitizedErrorMessage), 200),
            MetadataOnly = true
        };
    }

    public static string SafeText(string? value)
    {
        var text = (value ?? string.Empty).Trim();
        return string.IsNullOrWhiteSpace(text) ? string.Empty : UnsafeRegex.Replace(text, "<redacted>");
    }

    public static string SafeToken(string? value)
    {
        var safe = SafeText(value);
        if (string.IsNullOrWhiteSpace(safe))
        {
            return string.Empty;
        }

        return Regex.Replace(safe, @"[^a-zA-Z0-9_\-\.]", "_").Trim('_').ToLowerInvariant();
    }

    private static IReadOnlyList<string> NormalizeList(
        IEnumerable<string>? values,
        bool tokenOnly = false)
    {
        if (values == null)
        {
            return [];
        }

        return values
            .Select(value => tokenOnly ? SafeToken(value) : SafeText(value))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(12)
            .ToList();
    }

    private static string NormalizeSource(string? value)
    {
        var source = SafeToken(value);
        return source is VisionAgentSemanticSources.Model or VisionAgentSemanticSources.RuleFallback
            ? source
            : VisionAgentSemanticSources.RuleFallback;
    }

    private static double Clamp(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return 0;
        }

        return Math.Clamp(value, 0, 1);
    }

    private static string Truncate(string? value, int maxChars)
    {
        var text = value ?? string.Empty;
        return text.Length <= maxChars ? text : text[..maxChars];
    }
}

public sealed class LlmVisionAgentSemanticExtractionCompletionSource : IVisionAgentSemanticExtractionCompletionSource
{
    private readonly AiGenerationOrchestrator _orchestrator;

    public LlmVisionAgentSemanticExtractionCompletionSource(AiGenerationOrchestrator orchestrator)
    {
        _orchestrator = orchestrator;
    }

    public async Task<string> CompleteAsync(
        VisionAgentSemanticExtractionCompletionRequest request,
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

public sealed class VisionAgentSemanticExtractorOptions
{
    public const string SectionName = "AI:VisionAgent:SemanticExtractor";

    public bool Enabled { get; set; } = true;

    public string ModelRole { get; set; } = AiModelConfig.RolePlanner;

    public int TimeoutSeconds { get; set; } = 30;

    public int MaxContextChars { get; set; } = 6_000;

    public VisionAgentSemanticExtractorOptions Normalize()
    {
        ModelRole = AiModelConfig.NormalizeRoleName(ModelRole);
        TimeoutSeconds = Math.Clamp(TimeoutSeconds, 1, 90);
        MaxContextChars = Math.Clamp(MaxContextChars, 2_000, 16_000);
        return this;
    }
}

public sealed class VisionAgentSemanticExtractorService : IVisionAgentSemanticExtractorService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private static readonly Regex UnsafeRegex = new(
        @"(?i)((?:rawPrompt|raw_prompt|systemPrompt|system_prompt|chain[-_ ]?of[-_ ]?thought|reasoning_content)(?:\s*[:=]\s*[^\s,;]+)?|[A-Za-z]:\\[^\s,;]+|\\\\[^\s,;]+|/(?:users|home|var|tmp|mnt|data)/[^\s,;]+|data:image/[^\s,;]+|base64[^\s,;]*|sk-[A-Za-z0-9_\-]{8,}|(?:api[_-]?key|x-api-key|token|secret|authorization|headers?|baseUrl|base_url|endpoint)\s*[:=]\s*[^\s,;]+|bearer\s+[A-Za-z0-9._\-]+|\b(?:\d{1,3}\.){3}\d{1,3}\b|\bDB\d+\.DB[XBWD]\d+(?:\.\d+)?\b|\bM\d+(?:\.\d+)?\b|\bD\d+\b|plc://[^\s,;]*)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private const int MaxErrorChars = 200;

    private readonly IVisionAgentSemanticExtractionCompletionSource _completionSource;
    private readonly VisionAgentSemanticExtractorOptions _options;
    private readonly Microsoft.Extensions.Logging.ILogger<VisionAgentSemanticExtractorService> _logger;

    public VisionAgentSemanticExtractorService(
        IVisionAgentSemanticExtractionCompletionSource completionSource,
        IOptions<VisionAgentSemanticExtractorOptions>? options,
        Microsoft.Extensions.Logging.ILogger<VisionAgentSemanticExtractorService> logger)
    {
        _completionSource = completionSource;
        _options = (options?.Value ?? new VisionAgentSemanticExtractorOptions()).Normalize();
        _logger = logger;
    }

    public async Task<VisionAgentSemanticExtractionResult> ExtractAsync(
        VisionAgentSemanticExtractionRequest request,
        CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            return RuleFallback(
                request,
                VisionAgentSemanticFailureCodes.ModelRequestFailed,
                "Semantic extraction model is disabled; rule fallback is active.");
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));

        try
        {
            var prompt = BuildPrompt(request, _options.MaxContextChars);
            var completion = await _completionSource.CompleteAsync(
                new VisionAgentSemanticExtractionCompletionRequest(prompt.SystemPrompt, prompt.Messages, _options.ModelRole),
                timeout.Token);
            if (string.IsNullOrWhiteSpace(completion))
            {
                return RuleFallback(
                    request,
                    VisionAgentSemanticFailureCodes.ModelEmpty,
                    "Semantic extraction model returned empty content; rule fallback is active.");
            }

            return RepairModelResult(ParseResult(completion), request);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return RuleFallback(
                request,
                VisionAgentSemanticFailureCodes.Timeout,
                "Semantic extraction model timed out; rule fallback is active.");
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            _logger.LogWarning(
                "Vision Agent semantic extraction JSON parse failed; rule fallback will be used. Error={Error}",
                SafeText(ex.Message));
            return RuleFallback(
                request,
                VisionAgentSemanticFailureCodes.JsonParseFailed,
                "Semantic extraction JSON parse failed; rule fallback is active.",
                ex);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            var code = IsUnauthorized(ex)
                ? VisionAgentSemanticFailureCodes.Unauthorized
                : ex is HttpRequestException
                    ? VisionAgentSemanticFailureCodes.ModelRequestFailed
                    : VisionAgentSemanticFailureCodes.UnknownError;
            _logger.LogWarning(
                "Vision Agent semantic extraction failed; rule fallback will be used. Code={Code} Error={Error}",
                code,
                SafeText(ex.Message));
            return RuleFallback(
                request,
                code,
                code == VisionAgentSemanticFailureCodes.Unauthorized
                    ? "Semantic extraction model authorization failed; rule fallback is active."
                    : "Semantic extraction model request failed; rule fallback is active.",
                ex);
        }
    }

    private static VisionAgentSemanticExtractionPrompt BuildPrompt(
        VisionAgentSemanticExtractionRequest request,
        int maxContextChars)
    {
        var systemPrompt = string.Join(Environment.NewLine,
        [
            "You are ClearVision SemanticExtractor for industrial vision requirements.",
            "Return exactly one JSON object. No markdown, prose, comments, raw prompt, system prompt, reasoning, or chain-of-thought.",
            "Only extract semantic slots. Do not generate a workflow, call tools, read files/images/base64, bind cameras/models, write PLC, or make Build decisions.",
            "Allowed taskType values: surface_defect, geometry_measurement, wire_sequence, code_recognition, presence_absence, classification, attribute_classification, template_location, plc_output, abstract_goal, unknown.",
            "Use attribute_classification when the user describes an object attribute and OK/NG condition, for example maturity, color, grade, state, or category.",
            "Use surface_defect or template_location for skew, pose, tape placement, scratches, stains, cracks, dents, and visible defects.",
            "Required JSON fields: isVisionRequest, intent, taskType, confidence, taskTypeConfidence, inspectionObject, targetAttribute, defectType, measurementTarget, imageSource, okCondition, ngCondition, outputTarget, suggestedRoute, canPlanCandidate, canBuildCandidate, objectSignals, taskSignals, missingFields, clarificationQuestions."
        ]);

        var builder = new StringBuilder();
        builder.AppendLine("Semantic extraction context:");
        builder.AppendLine($"description={Truncate(SafeText(request.Description), 2_000)}");
        builder.AppendLine($"originalUserPrompt={Truncate(SafeText(request.OriginalUserPrompt), 2_000)}");
        builder.AppendLine($"additionalContext={Truncate(SafeText(request.AdditionalContext), 1_500)}");
        builder.AppendLine($"mode={SafeToken(request.Mode)}");
        builder.AppendLine($"historySummary={Truncate(SafeText(request.HistorySummary), 1_000)}");
        builder.AppendLine($"hasCurrentFlow={request.HasCurrentFlow.ToString().ToLowerInvariant()}");
        builder.AppendLine($"hasPendingPlan={request.HasPendingPlan.ToString().ToLowerInvariant()}");
        builder.AppendLine($"currentFlowSummary={Truncate(SafeText(request.CurrentFlowSummary), 1_000)}");
        builder.AppendLine($"templateSelectionMode={SafeToken(request.TemplateSelection?.Mode)}");
        builder.AppendLine($"templateSelectionId={SafeToken(request.TemplateSelection?.TemplateId)}");
        builder.AppendLine($"attachmentCount={request.AttachmentSummary.Count}");
        builder.AppendLine($"attachmentKinds={string.Join(",", request.AttachmentSummary.ResourceKinds.Select(SafeToken))}");
        builder.AppendLine($"attachmentPathsRedacted={request.AttachmentSummary.PathsRedacted.ToString().ToLowerInvariant()}");

        return new VisionAgentSemanticExtractionPrompt(
            systemPrompt,
            [new ChatMessage("user", Truncate(builder.ToString(), maxContextChars))]);
    }

    private static VisionAgentSemanticExtractionResult ParseResult(string completion)
    {
        var json = ExtractJsonObject(completion);
        return JsonSerializer.Deserialize<VisionAgentSemanticExtractionResult>(json, JsonOptions)
            ?? throw new InvalidOperationException("Semantic extractor returned an empty JSON object.");
    }

    private static VisionAgentSemanticExtractionResult RepairModelResult(
        VisionAgentSemanticExtractionResult candidate,
        VisionAgentSemanticExtractionRequest request)
    {
        var taskType = NormalizeTaskType(candidate.TaskType);
        var objectSignals = NormalizeList(candidate.ObjectSignals)
            .Concat(Single(candidate.InspectionObject))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(12)
            .ToList();
        var taskSignals = NormalizeList(candidate.TaskSignals)
            .Concat(Single(candidate.TargetAttribute))
            .Concat(Single(candidate.DefectType))
            .Concat(Single(candidate.MeasurementTarget))
            .Concat(Single(candidate.OkCondition))
            .Concat(Single(candidate.NgCondition))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(12)
            .ToList();
        var missingFields = NormalizeList(candidate.MissingFields);

        return candidate with
        {
            Intent = NormalizeIntent(candidate.Intent, request),
            TaskType = taskType,
            Confidence = Clamp(candidate.Confidence),
            TaskTypeConfidence = Clamp(candidate.TaskTypeConfidence),
            InspectionObject = SafeText(candidate.InspectionObject),
            TargetAttribute = SafeText(candidate.TargetAttribute),
            DefectType = SafeText(candidate.DefectType),
            MeasurementTarget = SafeText(candidate.MeasurementTarget),
            ImageSource = SafeText(candidate.ImageSource),
            OkCondition = SafeText(candidate.OkCondition),
            NgCondition = SafeText(candidate.NgCondition),
            OutputTarget = SafeText(candidate.OutputTarget),
            SuggestedRoute = SafeText(candidate.SuggestedRoute),
            ObjectSignals = objectSignals,
            TaskSignals = taskSignals,
            MissingFields = missingFields,
            ClarificationQuestions = NormalizeList(candidate.ClarificationQuestions),
            Source = VisionAgentSemanticSources.Model,
            FailureCode = string.Empty,
            SanitizedErrorMessage = string.Empty,
            MetadataOnly = true
        };
    }

    private static VisionAgentSemanticExtractionResult RuleFallback(
        VisionAgentSemanticExtractionRequest request,
        string failureCode,
        string publicSummary,
        Exception? exception = null)
    {
        var text = NormalizeText(request.Description, request.AdditionalContext);
        var maturity = VisionAgentRequirementMaturityGate.Evaluate(new VisionAgentRequirementMaturityRequest
        {
            Description = request.Description,
            AdditionalContext = request.AdditionalContext,
            Mode = request.Mode,
            HasCurrentFlow = request.HasCurrentFlow,
            HasPendingPlan = request.HasPendingPlan,
            TemplateSelection = request.TemplateSelection
        });
        var slots = ExtractRuleSlots(text, maturity);
        var taskType = slots.TaskType == AiVisionTaskTypes.Unknown
            ? NormalizeTaskType(maturity.TaskType)
            : slots.TaskType;
        var missing = maturity.MissingFields
            .Concat(BuildSemanticMissingFields(slots, taskType))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(12)
            .ToList();
        var objectSignals = maturity.ObjectSignals
            .Concat(Single(slots.InspectionObject))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(12)
            .ToList();
        var taskSignals = maturity.TaskSignals
            .Concat(Single(slots.TargetAttribute))
            .Concat(Single(slots.OkCondition))
            .Concat(Single(slots.NgCondition))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(12)
            .ToList();

        return new VisionAgentSemanticExtractionResult
        {
            IsVisionRequest = maturity.Maturity != AiRequirementMaturity.ChatOrHelp,
            Intent = maturity.Maturity switch
            {
                AiRequirementMaturity.ChatOrHelp => "help",
                AiRequirementMaturity.ModifyExistingFlow => "modify_flow",
                _ when maturity.CanPlan => "new_flow",
                _ => "unknown"
            },
            TaskType = taskType,
            Confidence = 0.35,
            TaskTypeConfidence = taskType == AiVisionTaskTypes.Unknown ? 0.1 : 0.35,
            InspectionObject = slots.InspectionObject,
            TargetAttribute = slots.TargetAttribute,
            DefectType = slots.DefectType,
            MeasurementTarget = slots.MeasurementTarget,
            ImageSource = slots.ImageSource,
            OkCondition = slots.OkCondition,
            NgCondition = slots.NgCondition,
            OutputTarget = slots.OutputTarget,
            SuggestedRoute = slots.SuggestedRoute,
            CanPlanCandidate = maturity.CanPlan,
            CanBuildCandidate = false,
            ObjectSignals = objectSignals,
            TaskSignals = taskSignals,
            MissingFields = missing,
            ClarificationQuestions = BuildClarificationQuestions(missing),
            Source = VisionAgentSemanticSources.RuleFallback,
            FailureCode = SafeToken(failureCode),
            SanitizedErrorMessage = BuildSafeError(publicSummary, exception),
            MetadataOnly = true
        };
    }

    private static RuleSemanticSlots ExtractRuleSlots(
        string text,
        AiRequirementMaturityResult maturity)
    {
        var taskType = NormalizeTaskType(maturity.TaskType);
        var inspectionObject = FirstRegex(text, @"(?:检测目标|检测对象)\s*(?:是|为|:|：)\s*(?<value>[^，。；;,.!?！？]+)");
        var okCondition = FirstRegex(text, @"如果(?<value>[^，。；;,.!?！？]+?)\s*(?:则|就)?\s*(?:为|是)?\s*OK");
        var ngCondition = FirstRegex(text, @"(?:否则|不满足|否則)\s*(?:为|是)?\s*NG");
        var imageSource = ContainsAny(text, ["相机", "camera"]) ? "相机" :
            ContainsAny(text, ["图片", "图像", "照片", "image", "photo"]) ? "图片" : string.Empty;
        var targetAttribute = string.Empty;
        var suggestedRoute = string.Empty;
        var attributeObject = FirstRegex(text, @"判断\s*(?<object>[^，。；;,.!?！？\s]+?)\s*是否\s*(?<value>[^，。；;,.!?！？\s]+)", "object");
        var attributeTarget = FirstRegex(text, @"判断\s*(?<object>[^，。；;,.!?！？\s]+?)\s*是否\s*(?<value>[^，。；;,.!?！？\s]+)");
        if (ContainsAny(text, ["成熟", "熟透", "成熟度"]))
        {
            taskType = AiVisionTaskTypes.AttributeClassification;
            targetAttribute = ContainsAny(text, ["熟透"]) ? "成熟度/熟透" : "成熟度";
            suggestedRoute = "属性分类 / OK-NG 判别路线";
        }
        else if (!string.IsNullOrWhiteSpace(attributeTarget))
        {
            taskType = AiVisionTaskTypes.AttributeClassification;
            targetAttribute = attributeTarget;
            if (string.IsNullOrWhiteSpace(inspectionObject))
            {
                inspectionObject = attributeObject;
            }
            suggestedRoute = "属性分类 / OK-NG 判别路线";
        }
        else if (taskType == AiVisionTaskTypes.Classification)
        {
            suggestedRoute = "属性分类 / OK-NG 判别路线";
        }
        else if (taskType is AiVisionTaskTypes.SurfaceDefect or AiVisionTaskTypes.SurfaceOrPoseDefect)
        {
            suggestedRoute = "表面缺陷检测路线";
        }
        else if (taskType == AiVisionTaskTypes.GeometryMeasurement)
        {
            suggestedRoute = "几何测量路线";
        }
        else if (taskType == AiVisionTaskTypes.WireSequence)
        {
            suggestedRoute = "线序检测路线";
        }
        else if (taskType is AiVisionTaskTypes.BarcodeQr or AiVisionTaskTypes.CodeRecognition)
        {
            suggestedRoute = "OCR / 条码识别路线";
        }
        else if (taskType == AiVisionTaskTypes.PresenceAbsence)
        {
            suggestedRoute = "有无 / 漏装检测路线";
        }
        else if (taskType == AiVisionTaskTypes.TemplateLocation)
        {
            suggestedRoute = "模板定位 / 位姿路线";
        }

        if (string.IsNullOrWhiteSpace(inspectionObject))
        {
            inspectionObject = maturity.ObjectSignals.FirstOrDefault() ?? string.Empty;
        }

        return new RuleSemanticSlots(
            SafeText(inspectionObject),
            SafeText(targetAttribute),
            SafeText(taskType is AiVisionTaskTypes.SurfaceDefect or AiVisionTaskTypes.SurfaceOrPoseDefect ? maturity.TaskSignals.FirstOrDefault() ?? string.Empty : string.Empty),
            SafeText(taskType == AiVisionTaskTypes.GeometryMeasurement ? maturity.TaskSignals.FirstOrDefault() ?? string.Empty : string.Empty),
            SafeText(imageSource),
            SafeText(okCondition),
            string.IsNullOrWhiteSpace(ngCondition) && !string.IsNullOrWhiteSpace(okCondition) ? "否则为NG" : SafeText(ngCondition),
            ContainsAny(text, ["输出", "report", "结果"]) ? "结果输出" : string.Empty,
            NormalizeTaskType(taskType),
            SafeText(suggestedRoute));
    }

    private static List<string> BuildSemanticMissingFields(RuleSemanticSlots slots, string taskType)
    {
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(slots.InspectionObject))
        {
            missing.Add("inspection_object");
        }

        if (string.IsNullOrWhiteSpace(taskType) ||
            string.Equals(taskType, AiVisionTaskTypes.Unknown, StringComparison.OrdinalIgnoreCase))
        {
            missing.Add("task_type");
        }

        if (string.IsNullOrWhiteSpace(slots.ImageSource))
        {
            missing.Add("image_source");
        }

        if (string.IsNullOrWhiteSpace(slots.OkCondition) &&
            string.IsNullOrWhiteSpace(slots.NgCondition))
        {
            missing.Add("acceptance_criteria");
        }

        return missing;
    }

    private static List<string> BuildClarificationQuestions(IEnumerable<string> missingFields)
    {
        return missingFields
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(field => field switch
            {
                "inspection_object" => "请补充检测目标或产品对象。",
                "task_type" => "请说明视觉任务类型或判断内容。",
                "image_source" => "请说明输入来源是相机、文件还是仅做元数据草稿。",
                "acceptance_criteria" => "请说明 OK/NG 判定规则。",
                "model_or_rule_strategy" => "请说明倾向使用规则、模板、传统算法还是模型策略。",
                _ => $"请补充 {field}。"
            })
            .Take(5)
            .ToList();
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
            throw new InvalidOperationException("Semantic extractor did not return a JSON object.");
        }

        return text[start..(end + 1)];
    }

    private static string NormalizeTaskType(string? taskType)
    {
        return SafeToken(taskType).ToLowerInvariant() switch
        {
            AiVisionTaskTypes.SurfaceDefect => AiVisionTaskTypes.SurfaceDefect,
            AiVisionTaskTypes.SurfaceOrPoseDefect => AiVisionTaskTypes.SurfaceDefect,
            AiVisionTaskTypes.GeometryMeasurement => AiVisionTaskTypes.GeometryMeasurement,
            "measurement" => AiVisionTaskTypes.GeometryMeasurement,
            AiVisionTaskTypes.WireSequence => AiVisionTaskTypes.WireSequence,
            AiVisionTaskTypes.CodeRecognition => AiVisionTaskTypes.CodeRecognition,
            AiVisionTaskTypes.BarcodeQr => AiVisionTaskTypes.CodeRecognition,
            "ocr" => AiVisionTaskTypes.CodeRecognition,
            AiVisionTaskTypes.PresenceAbsence => AiVisionTaskTypes.PresenceAbsence,
            AiVisionTaskTypes.Classification => AiVisionTaskTypes.Classification,
            AiVisionTaskTypes.AttributeClassification => AiVisionTaskTypes.AttributeClassification,
            AiVisionTaskTypes.TemplateLocation => AiVisionTaskTypes.TemplateLocation,
            AiVisionTaskTypes.PlcOutput => AiVisionTaskTypes.PlcOutput,
            AiVisionTaskTypes.AbstractGoal => AiVisionTaskTypes.AbstractGoal,
            _ => AiVisionTaskTypes.Unknown
        };
    }

    private static string NormalizeIntent(
        string? intent,
        VisionAgentSemanticExtractionRequest request)
    {
        var value = SafeToken(intent).ToLowerInvariant();
        return value switch
        {
            "new_flow" or "modify_flow" or "help" or "chat" or "unknown" => value,
            "actionable_vision_plan" => "new_flow",
            "modify_existing_flow" => "modify_flow",
            "casual_chat" => "chat",
            _ when request.HasCurrentFlow && ContainsAny(NormalizeText(request.Description, request.AdditionalContext), ["当前流程", "已有流程", "修改", "调整"]) => "modify_flow",
            _ => "unknown"
        };
    }

    private static bool IsUnauthorized(Exception error)
    {
        return error is HttpRequestException { StatusCode: HttpStatusCode.Unauthorized } ||
               error.Message.Contains("401", StringComparison.OrdinalIgnoreCase) ||
               error.Message.Contains("unauthorized", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildSafeError(string publicSummary, Exception? exception)
    {
        var text = publicSummary;
        if (exception != null && !string.IsNullOrWhiteSpace(exception.Message))
        {
            text = $"{publicSummary} {exception.GetType().Name}: {exception.Message}";
        }

        return Truncate(SafeText(text), MaxErrorChars);
    }

    private static string FirstRegex(string text, string pattern)
    {
        return FirstRegex(text, pattern, "value");
    }

    private static string FirstRegex(string text, string pattern, string groupName)
    {
        var match = Regex.Match(text, pattern, RegexOptions.CultureInvariant);
        return match.Success ? SafeText(match.Groups[groupName].Value) : string.Empty;
    }

    private static string NormalizeText(string? description, string? additionalContext)
    {
        return string.Join(' ', new[] { description, additionalContext }
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value!.Trim()))
            .Trim();
    }

    private static List<string> NormalizeList(IEnumerable<string>? values)
    {
        return (values ?? [])
            .Select(SafeText)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(12)
            .ToList();
    }

    private static IEnumerable<string> Single(string? value)
    {
        var text = SafeText(value);
        return string.IsNullOrWhiteSpace(text) ? [] : [text];
    }

    private static bool ContainsAny(string text, IEnumerable<string> terms)
    {
        return !string.IsNullOrWhiteSpace(text) &&
               terms.Any(term => !string.IsNullOrWhiteSpace(term) &&
                                 text.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    private static double Clamp(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return 0;
        }

        return Math.Clamp(value, 0, 1);
    }

    private static string SafeText(string? value)
    {
        var text = string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        return string.IsNullOrWhiteSpace(text) ? string.Empty : UnsafeRegex.Replace(text, "<redacted>");
    }

    private static string SafeToken(string? value)
    {
        var text = SafeText(value);
        return string.IsNullOrWhiteSpace(text)
            ? string.Empty
            : Regex.Replace(text, @"[^A-Za-z0-9_\-.:]", "_");
    }

    private static string Truncate(string? value, int maxChars)
    {
        var text = value ?? string.Empty;
        return text.Length <= maxChars ? text : text[..maxChars];
    }

    private sealed record VisionAgentSemanticExtractionPrompt(
        string SystemPrompt,
        List<ChatMessage> Messages);

    private sealed record RuleSemanticSlots(
        string InspectionObject,
        string TargetAttribute,
        string DefectType,
        string MeasurementTarget,
        string ImageSource,
        string OkCondition,
        string NgCondition,
        string OutputTarget,
        string TaskType,
        string SuggestedRoute);
}
