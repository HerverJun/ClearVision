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
        var text = BuildAnalysisText(description, additionalContext);
        var scenario = scenarioMatch?.Scenario;
        var brief = new AiRequirementBrief
        {
            ScenarioKey = scenario?.ScenarioKey ?? string.Empty,
            ScenarioName = scenario?.ScenarioName ?? string.Empty,
            IntentType = ResolveIntentType(scenario, text),
            Confidence = scenarioMatch?.Confidence ?? 0,
            ObjectTypes = ResolveMatches(
                scenario?.ObjectTypes,
                text,
                fallbackToFirst: (scenarioMatch?.Confidence ?? 0) >= 0.55),
            DefectTypes = ResolveDefectTypes(scenario, text),
            MeasurementTargets = ResolveMeasurementTargets(scenario, text),
            RequiredResources = scenario?.RequiredResources.ToList() ?? new List<string>()
        };

        brief.ObjectName = ResolveObjectName(brief, scenario, text);
        brief.OutputTarget = ResolveOutputTarget(text);
        brief.ImageSource = ResolveImageSource(text);
        brief.DecisionRule = ResolveDecisionRule(text);
        brief.RoiRequirement = ResolveRoiRequirement(brief, text);
        brief.CalibrationRequirement = ResolveCalibrationRequirement(brief, text);
        brief.RequiredFields = BuildRequiredFields(brief, scenarioMatch);
        brief.KnownFacts = BuildKnownFacts(brief);
        brief.MissingFacts = BuildMissingFacts(brief, scenarioMatch);
        brief.AttachmentFacts = BuildAttachmentFacts(text);
        brief.ClarificationQuestions = BuildClarificationQuestions(brief, scenario).Take(3).ToList();
        brief.HasOpenQuestions = brief.MissingFacts.Count > 0 || brief.ClarificationQuestions.Count > 0;
        brief.ClarificationRequired = brief.HasOpenQuestions;
        brief.CanGenerateDraftNow = CanGenerateDraftNow(brief);
        brief.DraftRiskLevel = DetermineDraftRiskLevel(brief);
        return brief;
    }

    private static string BuildAnalysisText(string? description, string? additionalContext)
    {
        return string.Join(' ', new[] { description, SanitizeAdditionalContext(additionalContext) }
                .Where(part => !string.IsNullOrWhiteSpace(part))
                .Select(part => part!.Trim()))
            .Trim();
    }

    private static string SanitizeAdditionalContext(string? additionalContext)
    {
        if (string.IsNullOrWhiteSpace(additionalContext))
            return string.Empty;

        var safeLines = new List<string>();
        var inQuestionBlock = false;
        foreach (var rawLine in additionalContext
                     .Replace("\r", string.Empty, StringComparison.Ordinal)
                     .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var line = rawLine.Trim();
            if (line.Length == 0)
                continue;

            if (ContainsAny(line, ["澄清问题", "待确认项", "仍需用户回答", "请先补充以下需求澄清项"]))
            {
                inQuestionBlock = true;
                continue;
            }

            if (ContainsAny(line, ["已知事实", "用户回答", "补充信息", "本轮回答"]))
            {
                inQuestionBlock = false;
                safeLines.Add(line);
                continue;
            }

            if (inQuestionBlock)
                continue;

            if ((line.StartsWith("-", StringComparison.Ordinal) || char.IsDigit(line[0])) &&
                ContainsAny(line, ["请", "是否", "例如", "可选"]))
            {
                continue;
            }

            if (ContainsAny(line, ["如果想先看草稿", "可选：", "例如"]))
                continue;

            safeLines.Add(line);
        }

        return string.Join(' ', safeLines);
    }

    private static string ResolveIntentType(ScenarioDefinition? scenario, string text)
    {
        if (scenario?.IntentTypes.Count > 0)
            return scenario.IntentTypes[0];

        if (ContainsAny(text, ["测量", "距离", "间距", "gap", "spacing", "孔距"]))
            return "measurement";
        if (ContainsAny(text, ["漏装", "有无", "存在", "missing", "缺件", "少装"]))
            return "presence_check";
        if (ContainsAny(text, ["线序", "端子", "顺序", "wire sequence", "terminal order"]))
            return "sequence_check";
        if (ContainsAny(text, ["缺陷", "划伤", "破损", "外观", "defect", "scratch", "damage", "dent"]))
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

    private static string ResolveObjectName(AiRequirementBrief brief, ScenarioDefinition? scenario, string text)
    {
        if (brief.ObjectTypes.Count > 0)
            return brief.ObjectTypes[0];

        if (scenario != null && !string.IsNullOrWhiteSpace(scenario.ScenarioName))
            return scenario.ScenarioName;

        if (ContainsAny(text, ["包装箱", "纸箱", "箱体"]))
            return "包装箱";
        if (ContainsAny(text, ["空调内机", "内机", "面板"]))
            return "空调内机";
        if (ContainsAny(text, ["遥控器", "remote"]))
            return "遥控器";
        if (ContainsAny(text, ["铜孔", "孔距", "间距"]))
            return "铜孔";

        return string.Empty;
    }

    private static string ResolveOutputTarget(string text)
    {
        if (ContainsAny(text, ["PLC", "plc", "发给PLC", "写入PLC", "联机输出"]))
            return "PLC";
        if (ContainsAny(text, ["数据库", "database", "db", "入库"]))
            return "Database";
        if (ContainsAny(text, ["界面", "页面", "UI", "web", "屏幕显示"]))
            return "UI";
        if (ContainsAny(text, ["CSV", "excel", "表格", "文件导出"]))
            return "File";

        return string.Empty;
    }

    private static string ResolveImageSource(string text)
    {
        if (ContainsAny(text, ["相机", "camera"]))
            return "camera";
        if (ContainsAny(text, ["图片", "附件", "文件", "图像", "照片"]))
            return "file";

        return "unknown";
    }

    private static string ResolveDecisionRule(string text)
    {
        if (ContainsAny(text, ["OK/NG", "OK NG", "合格/不合格", "良品/不良品", "发NG", "发OK"]))
            return "OK/NG";
        if (ContainsAny(text, ["数量", "有无", "存在", "漏装"]))
            return "presence";
        if (ContainsAny(text, ["测量", "尺寸", "间距", "距离", "圆心距离", "孔距"]))
            return "measurement";

        return string.Empty;
    }

    private static string ResolveRoiRequirement(AiRequirementBrief brief, string text)
    {
        if (ContainsAny(text, ["ROI", "区域", "范围", "局部"]))
            return "region";

        if (!string.IsNullOrWhiteSpace(brief.IntentType) &&
            (brief.IntentType.Contains("defect", StringComparison.OrdinalIgnoreCase) ||
             brief.IntentType.Contains("presence", StringComparison.OrdinalIgnoreCase) ||
             brief.IntentType.Contains("measurement", StringComparison.OrdinalIgnoreCase)))
        {
            return "region";
        }

        return "none";
    }

    private static string ResolveCalibrationRequirement(AiRequirementBrief brief, string text)
    {
        if (ContainsAny(text, ["标定", "pixel", "像素", "毫米", "mm", "world", "物理单位"]))
            return "pixel_to_world";

        if (brief.IntentType.Contains("measurement", StringComparison.OrdinalIgnoreCase))
            return "pixel_to_world";

        return "none";
    }

    private static List<string> BuildRequiredFields(AiRequirementBrief brief, ScenarioMatchResult? scenarioMatch)
    {
        var required = new List<string>();

        if (string.IsNullOrWhiteSpace(brief.ScenarioKey) || (scenarioMatch?.Confidence ?? 0) < 0.45)
            required.Add("scene");

        if (brief.ObjectTypes.Count == 0)
            required.Add("object_type");

        if (brief.IntentType.Contains("defect", StringComparison.OrdinalIgnoreCase) && brief.DefectTypes.Count == 0)
            required.Add("defect_type");

        if (brief.IntentType.Contains("measurement", StringComparison.OrdinalIgnoreCase) && brief.MeasurementTargets.Count == 0)
            required.Add("measurement_target");

        if (scenarioMatch?.MissingSignals != null)
        {
            foreach (var signal in scenarioMatch.MissingSignals)
            {
                if (IsBlockingMissingSignal(signal))
                    required.Add(signal);
            }
        }

        return required
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool IsBlockingMissingSignal(string? signal)
    {
        return signal is not null &&
               !string.IsNullOrWhiteSpace(signal) &&
               !signal.Equals("model_path", StringComparison.OrdinalIgnoreCase) &&
               !signal.Equals("roi", StringComparison.OrdinalIgnoreCase) &&
               !signal.Equals("calibration", StringComparison.OrdinalIgnoreCase) &&
               !signal.Equals("output_target", StringComparison.OrdinalIgnoreCase);
    }

    private static List<string> BuildKnownFacts(AiRequirementBrief brief)
    {
        var knownFacts = new List<string>();

        if (!string.IsNullOrWhiteSpace(brief.ScenarioName))
            knownFacts.Add($"已识别场景：{brief.ScenarioName}");
        if (!string.IsNullOrWhiteSpace(brief.ObjectName))
            knownFacts.Add($"对象：{brief.ObjectName}");
        if (brief.ObjectTypes.Count > 0)
            knownFacts.Add($"对象类型：{string.Join("、", brief.ObjectTypes)}");
        if (brief.DefectTypes.Count > 0)
            knownFacts.Add($"缺陷类型：{string.Join("、", brief.DefectTypes)}");
        if (brief.MeasurementTargets.Count > 0)
            knownFacts.Add($"测量目标：{string.Join("、", brief.MeasurementTargets)}");
        if (!string.IsNullOrWhiteSpace(brief.OutputTarget))
            knownFacts.Add($"输出目标：{brief.OutputTarget}");
        if (!string.IsNullOrWhiteSpace(brief.DecisionRule))
            knownFacts.Add($"判定逻辑：{brief.DecisionRule}");
        if (!string.IsNullOrWhiteSpace(brief.ImageSource) && !string.Equals(brief.ImageSource, "unknown", StringComparison.OrdinalIgnoreCase))
            knownFacts.Add($"图像来源：{brief.ImageSource}");
        if (!string.IsNullOrWhiteSpace(brief.RoiRequirement) && !string.Equals(brief.RoiRequirement, "none", StringComparison.OrdinalIgnoreCase))
            knownFacts.Add($"ROI：{brief.RoiRequirement}");

        return knownFacts;
    }

    private static List<string> BuildMissingFacts(AiRequirementBrief brief, ScenarioMatchResult? scenarioMatch)
    {
        var missing = new List<string>();

        foreach (var field in brief.RequiredFields)
        {
            missing.Add(field switch
            {
                "scene" => "需要确认具体场景",
                "object_type" => "需要确认检测对象",
                "defect_type" => "需要确认缺陷类别",
                "measurement_target" => "需要确认测量目标和单位",
                "output_target" => "需要确认输出目标（PLC/数据库/界面）",
                "model_path" => "需要确认模型文件或标签资源",
                "roi" => "需要确认ROI范围",
                "calibration" => "需要确认标定或像素转物理单位换算",
                "ambiguous_negative_signal" => "输入里存在歧义，需要补充更具体的对象描述",
                _ => field
            });
        }

        if (scenarioMatch?.Confidence is < 0.45)
            missing.Add("需要进一步澄清具体场景");

        return missing
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<string> BuildAttachmentFacts(string text)
    {
        var facts = new List<string>();

        if (ContainsAny(text, ["附件", "图片", "相机", "文件"]))
            facts.Add("已提供附件或图像元信息");

        if (ContainsAny(text, ["图像", "图片"]))
            facts.Add("涉及视觉输入");

        return facts;
    }

    private static IEnumerable<AiClarificationQuestion> BuildClarificationQuestions(
        AiRequirementBrief brief,
        ScenarioDefinition? scenario)
    {
        foreach (var field in brief.RequiredFields)
        {
            yield return BuildQuestionForField(field, brief, scenario);
        }

        if (string.IsNullOrWhiteSpace(brief.OutputTarget))
            yield return BuildQuestionForField("output_target", brief, scenario);

        if (brief.RequiredResources.Any(resource =>
                resource.Equals("DeepLearning.ModelPath", StringComparison.OrdinalIgnoreCase)))
        {
            yield return BuildQuestionForField("model_path", brief, scenario);
        }

        if (brief.RoiRequirement is "region")
            yield return BuildQuestionForField("roi", brief, scenario);

        if (brief.CalibrationRequirement is "pixel_to_world")
            yield return BuildQuestionForField("calibration", brief, scenario);
    }

    private static AiClarificationQuestion BuildQuestionForField(
        string field,
        AiRequirementBrief brief,
        ScenarioDefinition? scenario)
    {
        return field switch
        {
            "scene" => new AiClarificationQuestion
            {
                Field = field,
                Question = "请确认这是外观缺陷、漏装有无、线序判定还是尺寸测量场景。",
                Required = true,
                Reason = "场景未明确时无法安全生成流程。",
                Priority = "high",
                Options = ["外观缺陷", "漏装有无", "线序判定", "尺寸测量"]
            },
            "object_type" => new AiClarificationQuestion
            {
                Field = field,
                Question = "请补充检测对象是什么。",
                Required = true,
                Reason = "需要明确对象才能选择正确模板与算子。",
                Priority = "high",
                Options = BuildReferenceOptions(
                    (scenario?.ObjectTypes.AsEnumerable() ?? Enumerable.Empty<string>()).Concat(brief.ObjectTypes),
                    ["产品", "包装箱/纸箱", "金属件", "连接器/端子", "圆孔/孔位", "标签/二维码"])
            },
            "defect_type" => new AiClarificationQuestion
            {
                Field = field,
                Question = "请补充需要判定的缺陷类别，例如划伤、压痕、破损、标签异常。",
                Required = true,
                Reason = "缺陷类别缺失会影响模板与判定逻辑。",
                Priority = "high",
                Options = BuildReferenceOptions(
                    (scenario?.DefectTypes.AsEnumerable() ?? Enumerable.Empty<string>()).Concat(brief.DefectTypes),
                    ["划伤/划痕", "压痕/凹坑", "破损/裂纹", "脏污/污渍", "漏装/缺失", "标签异常"])
            },
            "measurement_target" => new AiClarificationQuestion
            {
                Field = field,
                Question = "请补充测量目标、单位和合格范围。",
                Required = true,
                Reason = "测量类场景需要明确目标与边界。",
                Priority = "high",
                Options = BuildReferenceOptions(
                    (scenario?.MeasurementTargets.AsEnumerable() ?? Enumerable.Empty<string>()).Concat(brief.MeasurementTargets),
                    ["孔距/圆心距离（mm）", "两边缘间距（mm）", "缝隙宽度（mm）", "直径/半径（mm）", "角度（deg）", "面积/长度阈值"])
            },
            "output_target" => new AiClarificationQuestion
            {
                Field = field,
                Question = "结果需要发给 PLC、数据库还是界面显示？",
                Required = false,
                Reason = "输出目标会影响后续算子编排。",
                Priority = "medium",
                Options = ["PLC", "数据库", "界面显示"]
            },
            "model_path" => new AiClarificationQuestion
            {
                Field = field,
                Question = "是否已有模型文件或标签资源？",
                Required = false,
                Reason = "深度学习模板需要模型资源才能落地。",
                Priority = "medium",
                Options = ["已有模型路径", "需要新训练模型", "先用传统视觉方案", "暂时未知"]
            },
            "roi" => new AiClarificationQuestion
            {
                Field = field,
                Question = "是否需要指定 ROI 范围？",
                Required = false,
                Reason = "ROI 能减少误检并提升稳定性。",
                Priority = "low",
                Options = ["整图检测", "固定ROI", "多ROI", "由模板/标定自动定位"]
            },
            "calibration" => new AiClarificationQuestion
            {
                Field = field,
                Question = "是否需要像素到物理单位换算或标定？",
                Required = false,
                Reason = "测量类场景通常需要标定。",
                Priority = "low",
                Options = ["像素到物理单位换算", "手眼标定", "不需要"]
            },
            "ambiguous_negative_signal" => new AiClarificationQuestion
            {
                Field = field,
                Question = "输入里存在歧义词，请再补充更具体的现场对象或步骤。",
                Required = true,
                Reason = "歧义会导致模板误匹配。",
                Priority = "high"
            },
            _ => new AiClarificationQuestion
            {
                Field = field,
                Question = $"请补充字段：{field}",
                Required = false,
                Reason = "需要补齐上下文。",
                Priority = "low"
            }
        };
    }

    private static List<string> BuildReferenceOptions(
        IEnumerable<string>? primaryOptions,
        IEnumerable<string> fallbackOptions)
    {
        return (primaryOptions ?? Enumerable.Empty<string>())
            .Concat(fallbackOptions)
            .Where(option => !string.IsNullOrWhiteSpace(option))
            .Select(option => ToDisplayOption(option.Trim()))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToList();
    }

    private static string ToDisplayOption(string option)
    {
        return option.Trim().ToLowerInvariant() switch
        {
            "copper_hole" => "铜孔/孔位",
            "heat_exchanger" => "换热器",
            "hole_spacing" => "孔距/圆心距离",
            "copper_hole_spacing" => "铜孔孔距",
            "metal_part" => "金属件",
            "carton" => "包装箱/纸箱",
            "connector" => "连接器",
            "terminal" => "端子",
            "label" => "标签",
            "scratch" => "划伤/划痕",
            "dent" => "压痕/凹坑",
            "broken" => "破损/裂纹",
            "damage" => "破损/裂纹",
            "stain" => "脏污/污渍",
            "gap_width" => "缝隙宽度",
            "diameter" => "直径",
            "angle" => "角度",
            _ => option
        };
    }

    private static bool CanGenerateDraftNow(AiRequirementBrief brief)
    {
        return brief.Confidence >= 0.35 &&
               (!string.IsNullOrWhiteSpace(brief.ScenarioKey) || brief.ObjectTypes.Count > 0 || brief.DefectTypes.Count > 0 || brief.MeasurementTargets.Count > 0) &&
               (brief.ObjectTypes.Count > 0 || brief.DefectTypes.Count > 0 || brief.MeasurementTargets.Count > 0 || !string.IsNullOrWhiteSpace(brief.OutputTarget) || !string.IsNullOrWhiteSpace(brief.ImageSource));
    }

    private static string DetermineDraftRiskLevel(AiRequirementBrief brief)
    {
        if (!brief.CanGenerateDraftNow)
            return "high";

        if (brief.Confidence >= 0.8 && brief.MissingFacts.Count == 0)
            return "low";

        if (brief.Confidence >= 0.55 && brief.MissingFacts.Count <= 1)
            return "medium";

        return "high";
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
