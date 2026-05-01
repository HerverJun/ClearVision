// ClarificationEngine.cs
// Dedicated clarification service that gates LLM generation when key requirement fields are missing.
// Defines minimum required fields per template and enforces the "ask first, generate later" pattern.
using Acme.Product.Core.DTOs;
using Acme.Product.Core.Entities;

namespace Acme.Product.Infrastructure.AI;

public interface IClarificationEngine
{
    /// <summary>
    /// Evaluates whether the requirement brief is sufficient for generation.
    /// Returns a ClarificationResult indicating whether to proceed or ask more questions.
    /// </summary>
    ClarificationResult Evaluate(
        AiRequirementBrief brief,
        ScenarioMatchResult? matchedScenario,
        string? templateScenarioKey = null);
}

public sealed class ClarificationResult
{
    /// <summary>Whether the system should gate (block) LLM generation.</summary>
    public bool GateGeneration { get; set; }

    /// <summary>Prioritized clarification questions (max 3).</summary>
    public List<AiClarificationQuestion> Questions { get; set; } = new();

    /// <summary>Fields that are missing and marked as required.</summary>
    public List<string> MissingRequiredFields { get; set; } = new();

    /// <summary>Fields that are missing but marked as recommended.</summary>
    public List<string> MissingRecommendedFields { get; set; } = new();
}

public sealed class ClarificationEngine : IClarificationEngine
{
    private const double GateConfidenceThreshold = 0.35;

    /// <summary>
    /// Per-template minimum required fields.
    /// If any of these are missing, the generation pipeline returns ClarificationRequired instead of calling the LLM.
    /// </summary>
    private static readonly Dictionary<string, RequiredFieldsSpec> TemplateRequiredFields = new(StringComparer.OrdinalIgnoreCase)
    {
        ["wire-sequence-terminal"] = new RequiredFieldsSpec(
            Required: ["defectTypes"],
            Recommended: ["modelResource", "roiRequirement"],
            Optional: ["decisionRule", "calibrationRequirement"]),
        ["carton-appearance-inspection"] = new RequiredFieldsSpec(
            Required: ["defectTypes"],
            Recommended: ["modelResource", "roiRequirement", "decisionRule"],
            Optional: ["calibrationRequirement"]),
        ["aircon-indoor-appearance-inspection"] = new RequiredFieldsSpec(
            Required: ["defectTypes", "objectName"],
            Recommended: ["modelResource", "roiRequirement"],
            Optional: ["calibrationRequirement", "decisionRule"]),
        ["aircon-outdoor-appearance-inspection"] = new RequiredFieldsSpec(
            Required: ["defectTypes", "objectName"],
            Recommended: ["modelResource", "roiRequirement"],
            Optional: ["calibrationRequirement", "decisionRule"]),
        ["remote-controller-missing-inspection"] = new RequiredFieldsSpec(
            Required: ["objectName"],
            Recommended: ["modelResource", "roiRequirement"],
            Optional: ["decisionRule", "calibrationRequirement"]),
        ["copper-hole-spacing-measurement"] = new RequiredFieldsSpec(
            Required: ["measurementTargets"],
            Recommended: ["decisionRule", "calibrationRequirement"],
            Optional: ["modelResource", "roiRequirement"])
    };

    private static readonly RequiredFieldsSpec DefaultSpec = new(
        Required: ["sceneType"],
        Recommended: ["defectTypes", "objectName"],
        Optional: ["modelResource", "roiRequirement", "decisionRule", "calibrationRequirement"]);

    public ClarificationResult Evaluate(
        AiRequirementBrief brief,
        ScenarioMatchResult? matchedScenario,
        string? templateScenarioKey = null)
    {
        var spec = ResolveSpec(templateScenarioKey ?? brief.ScenarioKey);

        var missingRequired = new List<string>();
        var missingRecommended = new List<string>();
        var questions = new List<AiClarificationQuestion>();

        // Assess required fields
        foreach (var field in spec.Required)
        {
            if (IsFieldMissing(brief, field, matchedScenario))
                missingRequired.Add(field);
        }

        // Assess recommended fields
        foreach (var field in spec.Recommended)
        {
            if (IsFieldMissing(brief, field, matchedScenario))
                missingRecommended.Add(field);
        }

        // Low confidence scenario match → always ask about scenario first
        var confidence = matchedScenario?.Confidence ?? brief.Confidence;
        if (string.IsNullOrWhiteSpace(brief.ScenarioKey) || confidence < GateConfidenceThreshold)
        {
            missingRequired.Insert(0, "scenario");
        }

        // Build targeted questions (max 3, required first)
        foreach (var field in missingRequired.Take(3))
        {
            var question = BuildQuestionForField(field, brief, level: "Required");
            if (question != null)
                questions.Add(question);
        }

        var remainingSlots = 3 - questions.Count;
        if (remainingSlots > 0)
        {
            foreach (var field in missingRecommended.Take(remainingSlots))
            {
                var question = BuildQuestionForField(field, brief, level: "Recommended");
                if (question != null)
                    questions.Add(question);
            }
        }

        // Gate generation if any required fields are missing
        var gateGeneration = missingRequired.Count > 0;

        return new ClarificationResult
        {
            GateGeneration = gateGeneration,
            Questions = questions,
            MissingRequiredFields = missingRequired,
            MissingRecommendedFields = missingRecommended
        };
    }

    private static RequiredFieldsSpec ResolveSpec(string? scenarioKey)
    {
        if (string.IsNullOrWhiteSpace(scenarioKey))
            return DefaultSpec;

        return TemplateRequiredFields.TryGetValue(scenarioKey, out var spec)
            ? spec
            : DefaultSpec;
    }

    private static bool IsFieldMissing(AiRequirementBrief brief, string field, ScenarioMatchResult? matchedScenario)
    {
        return field switch
        {
            "sceneType" => string.IsNullOrWhiteSpace(brief.SceneType),
            "scenario" => string.IsNullOrWhiteSpace(brief.ScenarioKey),
            "defectTypes" => brief.DefectTypes.Count == 0,
            "measurementTargets" => brief.MeasurementTargets.Count == 0,
            "objectName" => string.IsNullOrWhiteSpace(brief.ObjectName),
            "modelResource" => string.Equals(brief.ModelResource, "missing", StringComparison.OrdinalIgnoreCase),
            "roiRequirement" => string.Equals(brief.RoiRequirement, "unknown", StringComparison.OrdinalIgnoreCase),
            "decisionRule" => string.IsNullOrWhiteSpace(brief.DecisionRule),
            "calibrationRequirement" => string.Equals(brief.CalibrationRequirement, "unknown", StringComparison.OrdinalIgnoreCase),
            "imageSource" => string.Equals(brief.ImageSource, "unknown", StringComparison.OrdinalIgnoreCase),
            "triggerMode" => string.Equals(brief.TriggerMode, "unknown", StringComparison.OrdinalIgnoreCase),
            "outputTarget" => string.Equals(brief.OutputTarget, "unknown", StringComparison.OrdinalIgnoreCase),
            _ => false
        };
    }

    private static AiClarificationQuestion? BuildQuestionForField(string field, AiRequirementBrief brief, string level)
    {
        return field switch
        {
            "scenario" => new AiClarificationQuestion
            {
                Field = "scenario",
                Question = "请确认这是外观缺陷检测、漏装有无检测、线序判定还是尺寸测量场景。",
                Required = true,
                Level = level,
                Options = ["外观缺陷检测", "漏装有无检测", "线序判定", "尺寸测量", "其他"],
                Reason = "不同场景对应不同的模板、算子链和必需资源，明确场景后才能准确生成流程。"
            },
            "sceneType" => new AiClarificationQuestion
            {
                Field = "sceneType",
                Question = "请问需要检测什么类型的缺陷或做什么类型的测量？",
                Required = true,
                Level = level,
                Options = ["外观缺陷检测", "尺寸测量", "读码/OCR", "线序检测", "漏装/有无检测", "标定", "其他"],
                Reason = "检测类型决定核心算子链和判定逻辑。"
            },
            "defectTypes" => new AiClarificationQuestion
            {
                Field = "defectTypes",
                Question = "请补充需要判定的缺陷类别，例如划伤、压痕、破损、脏污、标签异常。",
                Required = true,
                Level = level,
                Options = ["划伤", "压痕", "破损", "脏污", "标签异常", "毛刺", "变形", "色差"],
                Reason = "缺陷类型影响模型选择和判定阈值。"
            },
            "measurementTargets" => new AiClarificationQuestion
            {
                Field = "measurementTargets",
                Question = "请补充测量对象、单位和合格范围。例如：孔间距 10±0.5mm。",
                Required = true,
                Level = level,
                Options = ["孔距", "圆心距离", "边缘间距", "直径", "角度", "面积"],
                Reason = "测量目标和合格范围是生成测量流程的核心依据。"
            },
            "objectName" => new AiClarificationQuestion
            {
                Field = "objectName",
                Question = "请确认检测对象（产品/部件名称），例如空调内机面板、包装箱、遥控器。",
                Required = true,
                Level = level,
                Options = ["包装箱", "空调内机", "空调外机", "遥控器", "端子排", "PCB板", "其他"],
                Reason = "检测对象决定模板匹配和 ROI 配置。"
            },
            "modelResource" => new AiClarificationQuestion
            {
                Field = "modelResource",
                Question = "是否有已训练的检测模型？请提供 ModelPath。若暂无模型，系统将标记为缺资源。",
                Required = false,
                Level = level,
                Options = ["已有模型，稍后提供路径", "暂无模型，先创建流程骨架", "使用传统视觉方法"],
                Reason = "DeepLearning 等 AI 算子依赖模型文件，缺模型时可先生成流程骨架。"
            },
            "roiRequirement" => new AiClarificationQuestion
            {
                Field = "roiRequirement",
                Question = "是否需要限定检测区域（ROI）？",
                Required = false,
                Level = level,
                Options = ["需要，全图检测", "需要，指定区域", "不需要 ROI"],
                Reason = "ROI 限定可减少误检并提升效率。"
            },
            "decisionRule" => new AiClarificationQuestion
            {
                Field = "decisionRule",
                Question = "OK/NG 判定逻辑是什么？例如：任一缺陷存在即 NG，或所有测量值在公差内才 OK。",
                Required = false,
                Level = level,
                Options = ["任一缺陷存在即 NG", "所有测量在公差内才 OK", "自定义判定逻辑"],
                Reason = "判定逻辑决定 ResultJudgment 算子的配置。"
            },
            "calibrationRequirement" => new AiClarificationQuestion
            {
                Field = "calibrationRequirement",
                Question = "是否需要标定（像素→物理单位换算）？",
                Required = false,
                Level = level,
                Options = ["不需要", "需要像素→毫米", "需要手眼标定"],
                Reason = "标定影响测量精度和坐标系转换。"
            },
            "imageSource" => new AiClarificationQuestion
            {
                Field = "imageSource",
                Question = "图像来源是什么？",
                Required = false,
                Level = level,
                Options = ["工业相机实时采集", "本地图片文件", "未知/待定"],
                Reason = "图像来源决定使用 ImageAcquisition 还是 ImageFileReader 算子。"
            },
            "triggerMode" => new AiClarificationQuestion
            {
                Field = "triggerMode",
                Question = "触发方式是什么？",
                Required = false,
                Level = level,
                Options = ["软件触发", "硬件触发", "连续采集", "未知/待定"],
                Reason = "触发方式影响采集算子和 PLC 通信配置。"
            },
            "outputTarget" => new AiClarificationQuestion
            {
                Field = "outputTarget",
                Question = "检测结果输出到哪里？",
                Required = false,
                Level = level,
                Options = ["ResultOutput（仅显示）", "PLC（控制信号）", "数据库", "未知/待定"],
                Reason = "输出目标决定结果算子和通信配置。"
            },
            _ => null
        };
    }

    private sealed record RequiredFieldsSpec(
        List<string> Required,
        List<string> Recommended,
        List<string> Optional);
}
