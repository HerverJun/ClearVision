using Acme.Product.Core.DTOs;
using Acme.Product.Core.Entities;

namespace Acme.Product.Infrastructure.AI;

public interface IRequirementBriefExtractor
{
    AiRequirementBrief Extract(
        string? description,
        string? additionalContext,
        ScenarioMatchResult? scenarioMatch);
}

public sealed class RequirementBriefExtractor : IRequirementBriefExtractor
{
    public AiRequirementBrief Extract(
        string? description,
        string? additionalContext,
        ScenarioMatchResult? scenarioMatch)
    {
        var text = $"{description} {additionalContext}".Trim();
        var scenario = scenarioMatch?.Scenario;
        var brief = new AiRequirementBrief
        {
            ScenarioKey = scenario?.ScenarioKey ?? string.Empty,
            ScenarioName = scenario?.ScenarioName ?? string.Empty,
            IntentType = ResolveIntentType(scenario, text),
            ObjectTypes = ResolveMatches(scenario?.ObjectTypes, text),
            DefectTypes = ResolveMatches(scenario?.DefectTypes, text),
            MeasurementTargets = ResolveMatches(scenario?.MeasurementTargets, text),
            RequiredResources = scenario?.RequiredResources.ToList() ?? new List<string>(),
            Industry = scenario?.Industry ?? string.Empty,
            ObjectName = ResolveObjectName(scenario, text),
            ImageSource = ResolveImageSource(text),
            TriggerMode = ResolveTriggerMode(text),
            OutputTarget = ResolveOutputTarget(text),
            AiModelRequired = scenario?.RequiredResources.Any(r => r.Contains("Model", StringComparison.OrdinalIgnoreCase)) == true,
            ModelResource = ResolveModelResource(scenario),
            RoiRequirement = string.Empty,
            CalibrationRequirement = ResolveCalibrationRequirement(text, scenario),
            DecisionRule = ResolveDecisionRule(scenario, text),
            Confidence = scenarioMatch?.Confidence ?? 0
        };

        brief.MissingFields = ComputeMissingFields(brief);
        brief.ClarificationQuestions = BuildClarificationQuestions(brief, scenarioMatch).Take(3).ToList();
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

    private static List<string> ResolveMatches(IReadOnlyList<string>? terms, string text)
    {
        if (terms == null || terms.Count == 0)
            return new List<string>();

        var matches = terms
            .Where(term => !string.IsNullOrWhiteSpace(term) &&
                           text.Contains(term, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return matches.Count > 0 ? matches : terms.Take(1).ToList();
    }

    private static string ResolveObjectName(ScenarioDefinition? scenario, string text)
    {
        if (scenario?.ObjectTypes.Count > 0)
        {
            var matched = scenario.ObjectTypes.FirstOrDefault(t =>
                text.Contains(t, StringComparison.OrdinalIgnoreCase));
            return matched ?? scenario.ObjectTypes[0];
        }
        return string.Empty;
    }

    private static string ResolveImageSource(string text)
    {
        if (ContainsAny(text, ["相机", "摄像头", "camera", "cam"]))
            return "camera";
        if (ContainsAny(text, ["文件", "图片", "file", "image", "照片"]))
            return "file";
        return "unknown";
    }

    private static string ResolveTriggerMode(string text)
    {
        if (ContainsAny(text, ["外部触发", "PLC触发", "硬件触发", "external", "hardware"]))
            return "external";
        if (ContainsAny(text, ["连续", "continuous", "实时"]))
            return "continuous";
        if (ContainsAny(text, ["手动", "manual", "点击"]))
            return "manual";
        return "unknown";
    }

    private static string ResolveOutputTarget(string text)
    {
        if (ContainsAny(text, ["PLC", "plc", "Modbus", "modbus"]))
            return "plc";
        if (ContainsAny(text, ["数据库", "database", "DB", "db"]))
            return "database";
        if (ContainsAny(text, ["屏幕", "显示", "screen", "display"]))
            return "screen";
        if (ContainsAny(text, ["文件", "导出", "file", "export"]))
            return "file";
        return "unknown";
    }

    private static string ResolveModelResource(ScenarioDefinition? scenario)
    {
        if (scenario?.RequiredResources == null)
            return string.Empty;

        var modelRes = scenario.RequiredResources.FirstOrDefault(r =>
            r.Contains("Model", StringComparison.OrdinalIgnoreCase));
        return modelRes ?? string.Empty;
    }

    private static string ResolveCalibrationRequirement(string text, ScenarioDefinition? scenario)
    {
        if (ContainsAny(text, ["标定", "像素转毫米", "pixel to mm", "calibration", "坐标转换"]))
            return "pixel_to_world";
        if (ContainsAny(text, ["手眼标定", "hand-eye", "eye_in_hand"]))
            return "hand_eye";
        if (scenario?.ScenarioKey == "copper-hole-spacing-measurement")
            return "pixel_to_world";
        return "none";
    }

    private static string ResolveDecisionRule(ScenarioDefinition? scenario, string text)
    {
        var intent = scenario?.IntentTypes?.FirstOrDefault() ?? string.Empty;
        if (intent.Contains("measurement", StringComparison.OrdinalIgnoreCase) ||
            ContainsAny(text, ["测量", "距离", "间距"]))
            return "range";
        if (intent.Contains("presence", StringComparison.OrdinalIgnoreCase) ||
            ContainsAny(text, ["漏装", "有无", "存在"]))
            return "count";
        return "pass_fail";
    }

    private static List<string> ComputeMissingFields(AiRequirementBrief brief)
    {
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(brief.IntentType)) missing.Add("intentType");
        if (string.IsNullOrWhiteSpace(brief.Industry)) missing.Add("industry");
        if (brief.ObjectTypes.Count == 0) missing.Add("objectTypes");
        if (brief.DefectTypes.Count == 0 && brief.IntentType.Contains("defect", StringComparison.OrdinalIgnoreCase))
            missing.Add("defectTypes");
        if (brief.MeasurementTargets.Count == 0 && brief.IntentType.Contains("measurement", StringComparison.OrdinalIgnoreCase))
            missing.Add("measurementTargets");
        if (string.IsNullOrWhiteSpace(brief.OutputTarget) || brief.OutputTarget == "unknown")
            missing.Add("outputTarget");
        return missing;
    }

    private static IEnumerable<AiClarificationQuestion> BuildClarificationQuestions(
        AiRequirementBrief brief,
        ScenarioMatchResult? scenarioMatch)
    {
        if (string.IsNullOrWhiteSpace(brief.ScenarioKey) || (scenarioMatch?.Confidence ?? 0) < 0.45)
        {
            yield return new AiClarificationQuestion
            {
                Field = "scenario",
                Question = "请确认这是外观缺陷、漏装有无、线序判定还是尺寸测量场景。",
                Required = true,
                Options = ["外观缺陷检测", "漏装/有无检测", "线序判定", "尺寸测量"],
                Level = "required",
                Reason = "场景类型不明确，需要确认后才能选择合适的检测方案。"
            };
        }

        if (brief.DefectTypes.Count == 0 && brief.IntentType.Contains("defect", StringComparison.OrdinalIgnoreCase))
        {
            yield return new AiClarificationQuestion
            {
                Field = "defectTypes",
                Question = "请补充需要判定的缺陷类别，例如划伤、压痕、破损、标签异常。",
                Required = true,
                Options = ["划伤", "压痕", "破损", "脏污", "标签异常", "变形", "色差"],
                Level = "required",
                Reason = "缺陷类型直接影响模型选择和判定逻辑。"
            };
        }

        if (brief.MeasurementTargets.Count == 0 && brief.IntentType.Contains("measurement", StringComparison.OrdinalIgnoreCase))
        {
            yield return new AiClarificationQuestion
            {
                Field = "measurementTarget",
                Question = "请补充测量对象、单位和合格范围。",
                Required = true,
                Level = "required",
                Reason = "测量目标和合格范围是判定OK/NG的核心依据。"
            };
        }

        if (string.IsNullOrWhiteSpace(brief.OutputTarget) || brief.OutputTarget == "unknown")
        {
            yield return new AiClarificationQuestion
            {
                Field = "outputTarget",
                Question = "检测结果需要输出到哪里？",
                Required = false,
                Options = ["PLC（Modbus）", "数据库", "屏幕显示", "文件导出"],
                DefaultValue = "屏幕显示",
                Level = "recommended",
                Reason = "输出目标决定了结果输出算子的配置方式。"
            };
        }

        if (brief.RequiredResources.Count > 0)
        {
            yield return new AiClarificationQuestion
            {
                Field = "resources",
                Question = $"请确认资源是否已准备：{string.Join(", ", brief.RequiredResources)}。",
                Required = false,
                Level = "optional",
                Reason = "确认资源可用性可以避免生成后缺少关键文件。"
            };
        }
    }

    private static bool ContainsAny(string text, IEnumerable<string> terms)
    {
        return terms.Any(term => text.Contains(term, StringComparison.OrdinalIgnoreCase));
    }
}
