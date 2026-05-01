using Acme.Product.Core.DTOs;
using Acme.Product.Core.Entities;

namespace Acme.Product.Infrastructure.AI;

public interface IRequirementBriefExtractor
{
    AiRequirementBrief Extract(
        string? description,
        string? additionalContext,
        ScenarioMatchResult? scenarioMatch,
        IReadOnlyList<string>? attachments = null);
}

public sealed class RequirementBriefExtractor : IRequirementBriefExtractor
{
    public AiRequirementBrief Extract(
        string? description,
        string? additionalContext,
        ScenarioMatchResult? scenarioMatch,
        IReadOnlyList<string>? attachments = null)
    {
        var text = $"{description} {additionalContext}".Trim();
        var scenario = scenarioMatch?.Scenario;
        var intentType = ResolveIntentType(scenario, text);
        var objectTypes = ResolveMatches(scenario?.ObjectTypes, text, fallbackToFirst: true);
        var defectTypes = ResolveDefectTypes(scenario, text);
        var measurementTargets = ResolveMeasurementTargets(scenario, text);
        var requiredResources = scenario?.RequiredResources.ToList() ?? new List<string>();
        var aiModelRequired = ResolveAiModelRequired(intentType, requiredResources, text);
        if (aiModelRequired == true &&
            !requiredResources.Contains("DeepLearning.ModelPath", StringComparer.OrdinalIgnoreCase))
        {
            requiredResources.Add("DeepLearning.ModelPath");
        }

        var brief = new AiRequirementBrief
        {
            ScenarioKey = scenario?.ScenarioKey ?? string.Empty,
            ScenarioName = scenario?.ScenarioName ?? string.Empty,
            Industry = scenario?.Industry ?? string.Empty,
            IntentType = intentType,
            ObjectName = ResolveObjectName(objectTypes, text),
            ObjectTypes = objectTypes,
            DefectTypes = defectTypes,
            MeasurementTargets = measurementTargets,
            ImageSource = ResolveImageSource(text, attachments),
            TriggerMode = ResolveTriggerMode(text),
            OutputTarget = ResolveOutputTarget(text),
            AiModelRequired = aiModelRequired,
            ModelResource = ResolveModelResource(text, requiredResources, aiModelRequired),
            RoiRequirement = ResolveRoiRequirement(text),
            CalibrationRequirement = ResolveCalibrationRequirement(intentType, text),
            DecisionRule = ResolveDecisionRule(text),
            Confidence = scenarioMatch?.Confidence ?? 0,
            RequiredResources = requiredResources.Distinct(StringComparer.OrdinalIgnoreCase).ToList()
        };

        brief.KnownFacts = BuildKnownFacts(brief);
        brief.ClarificationQuestions = BuildClarificationQuestions(brief, scenarioMatch, text).Take(3).ToList();
        brief.MissingFields = brief.ClarificationQuestions
            .Where(question => question.Required)
            .Select(question => question.Field)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        brief.CanGenerateDraftNow = brief.ClarificationQuestions.All(question => !question.Required);
        brief.DraftRiskLevel = brief.ClarificationQuestions.Any(question => question.Required)
            ? "high"
            : brief.ClarificationQuestions.Count > 0 ? "medium" : "low";
        return brief;
    }

    private static string ResolveIntentType(ScenarioDefinition? scenario, string text)
    {
        if (scenario?.IntentTypes.Count > 0)
            return scenario.IntentTypes[0];

        if (ContainsAny(text, ["测量", "距离", "间距", "gap", "spacing"]))
            return "measurement";
        if (ContainsAny(text, ["漏装", "有无", "存在", "missing"]))
            return "presence_check";
        if (ContainsAny(text, ["缺陷", "划伤", "破损", "外观", "defect", "scratch"]))
            return "defect_detection";

        return string.Empty;
    }

    private static List<string> ResolveMatches(
        IReadOnlyList<string>? terms,
        string text,
        bool fallbackToFirst = false)
    {
        if (terms == null || terms.Count == 0)
            return new List<string>();

        var matches = terms
            .Where(term => !string.IsNullOrWhiteSpace(term) &&
                           text.Contains(term, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return matches.Count > 0
            ? matches
            : fallbackToFirst ? terms.Take(1).ToList() : new List<string>();
    }

    private static List<string> ResolveDefectTypes(ScenarioDefinition? scenario, string text)
    {
        var matches = ResolveMatches(scenario?.DefectTypes, text);
        foreach (var defect in KnownDefectTerms)
        {
            if (ContainsAny(text, defect.Value))
                matches.Add(defect.Key);
        }

        return matches
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<string> ResolveMeasurementTargets(ScenarioDefinition? scenario, string text)
    {
        var matches = ResolveMatches(scenario?.MeasurementTargets, text);
        if (ContainsAny(text, ["孔距", "孔间距", "圆心距", "圆心距离"]))
            matches.Add("孔距/圆心距离");
        if (ContainsAny(text, ["间距", "距离", "gap", "spacing", "pitch"]))
            matches.Add("间距");
        if (ContainsAny(text, ["缝隙", "缝宽"]))
            matches.Add("缝隙宽度");

        return matches
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string ResolveObjectName(IReadOnlyList<string> objectTypes, string text)
    {
        if (ContainsAny(text, ["包装箱", "纸箱", "箱体"]))
            return "包装箱";
        if (ContainsAny(text, ["空调内机", "内机", "面板"]))
            return "空调内机面板";
        if (ContainsAny(text, ["空调外机", "外机", "室外机"]))
            return "空调外机";
        if (ContainsAny(text, ["遥控器", "附件"]))
            return "遥控器/附件";
        if (ContainsAny(text, ["铜孔", "两器", "孔"]))
            return "铜孔";
        if (ContainsAny(text, ["端子", "线序", "线束"]))
            return "端子线束";

        return objectTypes.FirstOrDefault() ?? string.Empty;
    }

    private static string ResolveImageSource(string text, IReadOnlyList<string>? attachments)
    {
        if (attachments is { Count: > 0 })
            return "file";
        if (ContainsAny(text, ["相机", "camera", "海康", "华睿", "采集"]))
            return "camera";
        if (ContainsAny(text, ["图片", "图像", "文件", "照片", "image", "file"]))
            return "file";

        return "unknown";
    }

    private static string ResolveTriggerMode(string text)
    {
        if (ContainsAny(text, ["硬触发", "外触发", "硬件触发", "hardware trigger"]))
            return "hardware";
        if (ContainsAny(text, ["软触发", "软件触发", "按钮触发", "software trigger"]))
            return "software";
        if (ContainsAny(text, ["连续", "轮询", "continuous"]))
            return "continuous";

        return "unknown";
    }

    private static string ResolveOutputTarget(string text)
    {
        if (ContainsAny(text, ["PLC", "modbus", "tcp", "寄存器"]))
            return "PLC";
        if (ContainsAny(text, ["数据库", "database", "db"]))
            return "Database";
        if (ContainsAny(text, ["CSV", "文件输出", "报表"]))
            return "File";
        if (ContainsAny(text, ["OK", "NG", "结果输出", "ResultOutput"]))
            return "ResultOutput";

        return "unknown";
    }

    private static bool? ResolveAiModelRequired(
        string intentType,
        IReadOnlyCollection<string> requiredResources,
        string text)
    {
        if (ContainsAny(text, ["yolo", "onnx", "模型", "深度学习", "AI", "deep learning", "model"]))
            return true;

        if (requiredResources.Any(item => item.Contains("ModelPath", StringComparison.OrdinalIgnoreCase)))
            return true;

        if (intentType.Contains("measurement", StringComparison.OrdinalIgnoreCase))
            return false;
        if (intentType.Contains("defect", StringComparison.OrdinalIgnoreCase) ||
            intentType.Contains("presence", StringComparison.OrdinalIgnoreCase) ||
            intentType.Contains("sequence", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return null;
    }

    private static string ResolveModelResource(
        string text,
        IReadOnlyCollection<string> requiredResources,
        bool? aiModelRequired)
    {
        if (ContainsAny(text, [".onnx", ".pt", ".pth", "modelpath", "模型路径", "已训练模型", "已有模型"]))
            return "provided";
        if (aiModelRequired == true ||
            requiredResources.Any(item => item.Contains("ModelPath", StringComparison.OrdinalIgnoreCase)))
        {
            return "missing";
        }

        return "not_required";
    }

    private static string ResolveRoiRequirement(string text)
    {
        if (ContainsAny(text, ["ROI", "区域", "检测区", "局部", "region"]))
            return "region";
        if (ContainsAny(text, ["全图", "整图", "whole image"]))
            return "none";

        return "unknown";
    }

    private static string ResolveCalibrationRequirement(string intentType, string text)
    {
        if (ContainsAny(text, ["手眼", "hand-eye", "hand eye"]))
            return "hand_eye";
        if (ContainsAny(text, ["mm", "毫米", "物理", "标定", "pixel_to_world"]))
            return "pixel_to_world";
        if (intentType.Contains("measurement", StringComparison.OrdinalIgnoreCase))
            return "unknown";

        return "none";
    }

    private static string ResolveDecisionRule(string text)
    {
        if (ContainsAny(text, ["OK", "NG", "合格", "不合格"]))
            return "OK/NG";
        if (HasMeasurementRange(text))
            return "按测量上下限判定";

        return string.Empty;
    }

    private static List<string> BuildKnownFacts(AiRequirementBrief brief)
    {
        var facts = new List<string>();
        if (!string.IsNullOrWhiteSpace(brief.ScenarioName))
            facts.Add($"场景：{brief.ScenarioName}");
        if (!string.IsNullOrWhiteSpace(brief.Industry))
            facts.Add($"行业：{brief.Industry}");
        if (!string.IsNullOrWhiteSpace(brief.ObjectName))
            facts.Add($"对象：{brief.ObjectName}");
        if (brief.DefectTypes.Count > 0)
            facts.Add($"缺陷：{string.Join("、", brief.DefectTypes)}");
        if (brief.MeasurementTargets.Count > 0)
            facts.Add($"测量：{string.Join("、", brief.MeasurementTargets)}");
        if (!string.Equals(brief.OutputTarget, "unknown", StringComparison.OrdinalIgnoreCase))
            facts.Add($"输出：{brief.OutputTarget}");
        if (!string.Equals(brief.ImageSource, "unknown", StringComparison.OrdinalIgnoreCase))
            facts.Add($"图像来源：{brief.ImageSource}");

        return facts;
    }

    private static IEnumerable<AiClarificationQuestion> BuildClarificationQuestions(
        AiRequirementBrief brief,
        ScenarioMatchResult? scenarioMatch,
        string text)
    {
        if (string.IsNullOrWhiteSpace(brief.ScenarioKey) || (scenarioMatch?.Confidence ?? 0) < 0.45)
        {
            yield return new AiClarificationQuestion
            {
                Field = "scenario",
                Question = "请确认这是外观缺陷、漏装有无、线序判定还是尺寸测量场景。",
                Level = "required",
                Reason = "当前描述不足以稳定选择模板和算子主链。",
                Options = ["外观缺陷", "漏装有无", "线序判定", "尺寸测量"],
                Required = true
            };
        }

        if (brief.DefectTypes.Count == 0 && brief.IntentType.Contains("defect", StringComparison.OrdinalIgnoreCase))
        {
            yield return new AiClarificationQuestion
            {
                Field = "defectTypes",
                Question = "请补充需要判定的缺陷类别，例如划伤、压痕、破损、标签异常。",
                Level = "required",
                Reason = "缺陷类别决定模型标签、过滤阈值和 OK/NG 判定逻辑。",
                Options = ["划伤", "压痕", "破损", "脏污", "标签异常"],
                Required = true
            };
        }

        if (brief.MeasurementTargets.Count == 0 && brief.IntentType.Contains("measurement", StringComparison.OrdinalIgnoreCase))
        {
            yield return new AiClarificationQuestion
            {
                Field = "measurementTarget",
                Question = "请补充测量对象、单位和合格范围。",
                Level = "required",
                Reason = "测量类流程必须知道测量对象，否则无法选择边缘/卡尺/间距主链。",
                Required = true
            };
        }

        if (brief.IntentType.Contains("measurement", StringComparison.OrdinalIgnoreCase) && !HasMeasurementRange(text))
        {
            yield return new AiClarificationQuestion
            {
                Field = "measurementRule",
                Question = "请补充测量单位、合格范围或是否需要像素到物理单位换算。",
                Level = "required",
                Reason = "没有上下限或单位时，只能测量，不能完成 OK/NG 判定。",
                Options = ["像素单位即可", "需要毫米换算", "补充上/下限"],
                Required = true
            };
        }

        if (brief.RequiredResources.Count > 0)
        {
            yield return new AiClarificationQuestion
            {
                Field = "resources",
                Question = $"请确认资源是否已准备：{string.Join(", ", brief.RequiredResources)}。",
                Level = "recommended",
                Reason = "缺少模型、标签或标定文件时可先生成草案，但后续必须补齐才能执行。",
                Options = ["已有资源", "稍后选择文件", "需要先生成占位流程"],
                Required = false
            };
        }

        if (string.Equals(brief.OutputTarget, "unknown", StringComparison.OrdinalIgnoreCase))
        {
            yield return new AiClarificationQuestion
            {
                Field = "outputTarget",
                Question = "结果需要只在界面显示，还是要输出到 PLC、数据库或文件？",
                Level = "recommended",
                Reason = "输出目标会影响末端算子和通信参数。",
                Options = ["界面显示", "PLC", "数据库", "文件"],
                Required = false
            };
        }
    }

    private static bool HasMeasurementRange(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        if (System.Text.RegularExpressions.Regex.IsMatch(
                text,
                @"\d+(?:\.\d+)?\s*(mm|毫米|cm|厘米|px|像素)",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase))
        {
            return true;
        }

        return ContainsAny(text, ["合格范围", "上下限", "公差", "阈值", "最小", "最大", "小于", "大于", "between"]);
    }

    private static bool ContainsAny(string text, IEnumerable<string> terms)
    {
        return terms.Any(term => text.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    private static readonly IReadOnlyDictionary<string, string[]> KnownDefectTerms =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["划伤"] = ["划伤", "划痕", "scratch"],
            ["破损"] = ["破损", "破裂", "损坏", "damage", "broken"],
            ["压痕"] = ["压痕", "凹坑", "dent"],
            ["脏污"] = ["脏污", "污渍", "污点", "stain"],
            ["漏装"] = ["漏装", "缺失", "少装", "missing"],
            ["错序"] = ["错序", "顺序错误", "wrong sequence"],
            ["变形"] = ["变形", "deform"],
            ["标签异常"] = ["标签异常", "标签", "label"],
            ["缝隙"] = ["缝隙", "gap"]
        };
}
