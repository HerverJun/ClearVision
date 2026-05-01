using Acme.Product.Core.DTOs;
using Acme.Product.Core.Entities;

namespace Acme.Product.Infrastructure.AI;

public interface IClarificationEngine
{
    ClarificationEvaluationResult Evaluate(
        AiRequirementBrief brief,
        ScenarioMatchResult? scenarioMatch,
        IReadOnlyList<string>? userProvidedAnswers = null);
}

public class ClarificationEvaluationResult
{
    public bool ClarificationRequired { get; set; }
    public string Level { get; set; } = "none";
    public List<AiClarificationQuestion> Questions { get; set; } = new();
    public List<string> ResolvedFields { get; set; } = new();
    public List<string> StillMissingFields { get; set; } = new();
    public string Reason { get; set; } = string.Empty;
}

public sealed class ClarificationEngine : IClarificationEngine
{
    private static readonly Dictionary<string, IReadOnlyList<RequiredFieldSpec>> ScenarioRequiredFields = new(StringComparer.OrdinalIgnoreCase)
    {
        ["wire-sequence-terminal"] = new List<RequiredFieldSpec>
        {
            new("defectTypes", "required", "请补充线序检测的期望顺序，例如：黑-蓝-棕。", new[] { "黑-蓝-棕", "蓝-黑-棕", "棕-蓝-黑", "自定义" }),
            new("modelPath", "required", "线序检测需要深度学习模型文件。"),
        },
        ["carton-appearance-inspection"] = new List<RequiredFieldSpec>
        {
            new("defectTypes", "required", "请补充需要检测的包装箱缺陷类型。", new[] { "破损", "压痕", "脏污", "标签异常", "变形" }),
            new("modelPath", "required", "外观检测需要深度学习模型文件。"),
        },
        ["aircon-indoor-appearance-inspection"] = new List<RequiredFieldSpec>
        {
            new("defectTypes", "required", "请补充空调内机的缺陷类型。", new[] { "划伤", "压痕", "脏污", "变形", "色差" }),
            new("modelPath", "required", "外观检测需要深度学习模型文件。"),
        },
        ["aircon-outdoor-appearance-inspection"] = new List<RequiredFieldSpec>
        {
            new("defectTypes", "required", "请补充空调外机的缺陷类型。", new[] { "划伤", "压痕", "脏污", "变形", "色差" }),
            new("modelPath", "required", "外观检测需要深度学习模型文件。"),
        },
        ["remote-controller-missing-inspection"] = new List<RequiredFieldSpec>
        {
            new("objectTypes", "required", "请补充需要检测是否漏装的目标对象。", new[] { "遥控器", "说明书", "配件", "合格证" }),
            new("modelPath", "required", "漏装检测需要深度学习模型文件。"),
        },
        ["copper-hole-spacing-measurement"] = new List<RequiredFieldSpec>
        {
            new("measurementTargets", "required", "请补充铜孔间距的测量目标和合格范围。"),
            new("calibrationRequirement", "recommended", "像素到物理单位的换算关系。", new[] { "已标定（像素/毫米）", "未标定（仅像素）" }),
        },
    };

    private static readonly IReadOnlyList<RequiredFieldSpec> DefaultRequiredFields = new List<RequiredFieldSpec>
    {
        new("intentType", "required", "请确认检测意图（缺陷检测、测量、漏装检测等）。", new[] { "缺陷检测", "尺寸测量", "漏装检测", "线序判定" }),
    };

    public ClarificationEvaluationResult Evaluate(
        AiRequirementBrief brief,
        ScenarioMatchResult? scenarioMatch,
        IReadOnlyList<string>? userProvidedAnswers = null)
    {
        var scenarioKey = brief.ScenarioKey;
        if (string.IsNullOrWhiteSpace(scenarioKey) && scenarioMatch?.Scenario != null)
            scenarioKey = scenarioMatch.Scenario.ScenarioKey;

        var requiredFields = ScenarioRequiredFields.TryGetValue(scenarioKey, out var specs)
            ? specs
            : DefaultRequiredFields;

        var resolvedFields = new List<string>();
        var missingRequired = new List<string>();
        var missingRecommended = new List<string>();
        var questions = new List<AiClarificationQuestion>();

        foreach (var spec in requiredFields)
        {
            var isFilled = IsFieldFilled(brief, spec.Field, userProvidedAnswers);
            if (isFilled)
            {
                resolvedFields.Add(spec.Field);
                continue;
            }

            var question = new AiClarificationQuestion
            {
                Field = spec.Field,
                Question = spec.Question,
                Required = spec.Level == "required",
                Level = spec.Level,
                Options = spec.Options?.ToList() ?? new List<string>(),
                Reason = spec.Level == "required" ? "此字段为必填项，缺少后无法生成准确方案。" : "补充此信息可提高生成质量。",
            };

            questions.Add(question);

            if (spec.Level == "required")
                missingRequired.Add(spec.Field);
            else
                missingRecommended.Add(spec.Field);
        }

        // Low-confidence scenario confirmation
        if ((scenarioMatch?.Confidence ?? 0) < 0.35 && !string.IsNullOrWhiteSpace(scenarioKey))
        {
            questions.Insert(0, new AiClarificationQuestion
            {
                Field = "scenario",
                Question = $"系统识别到的场景是「{brief.ScenarioName}」，请确认是否正确。",
                Required = true,
                Level = "required",
                Options = new List<string> { brief.ScenarioName, "其他场景" },
                Reason = "场景匹配置信度较低，需要人工确认。"
            });
            if (!missingRequired.Contains("scenario"))
                missingRequired.Add("scenario");
        }

        var clarificationRequired = missingRequired.Count > 0;
        var level = clarificationRequired ? "required"
            : missingRecommended.Count > 0 ? "recommended"
            : "none";

        return new ClarificationEvaluationResult
        {
            ClarificationRequired = clarificationRequired,
            Level = level,
            Questions = questions,
            ResolvedFields = resolvedFields,
            StillMissingFields = missingRequired.Concat(missingRecommended).Distinct().ToList(),
            Reason = clarificationRequired
                ? $"缺少 {missingRequired.Count} 项必填信息：{string.Join("、", missingRequired)}"
                : missingRecommended.Count > 0
                    ? $"有 {missingRecommended.Count} 项推荐信息可补充"
                    : "信息充分，可以生成"
        };
    }

    private static bool IsFieldFilled(AiRequirementBrief brief, string field, IReadOnlyList<string>? userAnswers)
    {
        if (userAnswers != null && userAnswers.Count > 0)
        {
            // If user provided answers, treat as filled
            return true;
        }

        return field switch
        {
            "intentType" => !string.IsNullOrWhiteSpace(brief.IntentType),
            "defectTypes" => brief.DefectTypes.Count > 0,
            "measurementTargets" => brief.MeasurementTargets.Count > 0,
            "objectTypes" => brief.ObjectTypes.Count > 0,
            "modelPath" => !string.IsNullOrWhiteSpace(brief.ModelResource),
            "outputTarget" => !string.IsNullOrWhiteSpace(brief.OutputTarget) && brief.OutputTarget != "unknown",
            "calibrationRequirement" => !string.IsNullOrWhiteSpace(brief.CalibrationRequirement) && brief.CalibrationRequirement != "none",
            "scenario" => true, // handled separately via confidence check
            "resources" => true, // optional, not blocking
            _ => false
        };
    }

    private sealed record RequiredFieldSpec(
        string Field,
        string Level,
        string Question,
        string[]? Options = null);
}
