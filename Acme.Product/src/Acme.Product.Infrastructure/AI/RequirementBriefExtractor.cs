// RequirementBriefExtractor.cs
// Extracts a structured AiRequirementBrief from user description, context, and scenario match.
// Integrates attachment metadata and model capability info.
using Acme.Product.Core.DTOs;
using Acme.Product.Core.Entities;

namespace Acme.Product.Infrastructure.AI;

public interface IRequirementBriefExtractor
{
    AiRequirementBrief Extract(
        string? description,
        string? additionalContext,
        ScenarioMatchResult? scenarioMatch,
        int attachmentCount = 0,
        bool modelSupportsVision = false,
        string? attachmentObservation = null);
}

public sealed class RequirementBriefExtractor : IRequirementBriefExtractor
{
    private const double HighConfidenceThreshold = 0.75;
    private const double LowConfidenceThreshold = 0.35;

    public AiRequirementBrief Extract(
        string? description,
        string? additionalContext,
        ScenarioMatchResult? scenarioMatch,
        int attachmentCount = 0,
        bool modelSupportsVision = false,
        string? attachmentObservation = null)
    {
        var text = NormalizeText($"{description} {additionalContext}");
        var scenario = scenarioMatch?.Scenario;
        var confidence = scenarioMatch?.Confidence ?? 0;

        var sceneType = ResolveSceneType(scenario, text);
        var industry = ResolveIndustry(scenario, text);
        var objectName = ResolveObjectName(scenario, text);
        var defectTypes = ResolveMatches(scenario?.DefectTypes, text);
        var measurementTargets = ResolveMatches(scenario?.MeasurementTargets, text);
        var imageSource = ResolveImageSource(scenario, text, attachmentCount);
        var triggerMode = ResolveTriggerMode(text);
        var outputTarget = ResolveOutputTarget(text);
        var aiModelRequired = ResolveAiModelRequired(text, defectTypes);
        var modelResource = ResolveModelResource(text);
        var roiRequirement = ResolveRoiRequirement(text);
        var calibrationRequirement = ResolveCalibrationRequirement(text);
        var decisionRule = ResolveDecisionRule(text, sceneType);
        var missingFields = BuildMissingFields(
            sceneType, objectName, defectTypes, measurementTargets,
            imageSource, modelResource, decisionRule, calibrationRequirement);

        var brief = new AiRequirementBrief
        {
            SceneType = sceneType,
            ScenarioKey = scenario?.ScenarioKey ?? string.Empty,
            ScenarioName = scenario?.ScenarioName ?? string.Empty,
            Industry = industry,
            IntentType = ResolveIntentType(scenario, text),
            ObjectName = objectName,
            ObjectTypes = ResolveMatches(scenario?.ObjectTypes, text),
            DefectTypes = defectTypes,
            MeasurementTargets = measurementTargets,
            ImageSource = imageSource,
            TriggerMode = triggerMode,
            OutputTarget = outputTarget,
            AiModelRequired = aiModelRequired,
            ModelResource = modelResource,
            LabelsPath = "missing",
            RoiRequirement = roiRequirement,
            CalibrationRequirement = calibrationRequirement,
            DecisionRule = decisionRule,
            Confidence = confidence > 0 ? confidence : EstimateConfidence(text, sceneType),
            MissingFields = missingFields,
            RequiredResources = scenario?.RequiredResources.ToList() ?? new List<string>(),
            AttachmentCount = attachmentCount,
            ModelSupportsVision = modelSupportsVision,
            AttachmentObservation = attachmentObservation ?? string.Empty
        };

        brief.ClarificationQuestions = BuildClarificationQuestions(brief, scenarioMatch).Take(3).ToList();
        return brief;
    }

    private static string NormalizeText(string? text)
    {
        return (text ?? string.Empty).Trim();
    }

    private static string ResolveSceneType(ScenarioDefinition? scenario, string text)
    {
        if (scenario?.IntentTypes.Count > 0)
            return scenario.IntentTypes[0];

        if (ContainsAny(text, ["线序", "排针", "排线", "端子顺序", "接线顺序", "wire sequence"]))
            return "wire_sequence";
        if (ContainsAny(text, ["漏装", "有无", "存在", "缺失", "missing part", "presence"]))
            return "missing_part";
        if (ContainsAny(text, ["测量", "距离", "间距", "孔距", "圆心距", "gap", "spacing", "diameter"]))
            return "measurement";
        if (ContainsAny(text, ["读码", "OCR", "条码", "二维码", "字符识别", "code reading"]))
            return "code_reading";
        if (ContainsAny(text, ["标定", "calibration", "手眼"]))
            return "calibration";
        if (ContainsAny(text, ["缺陷", "划伤", "破损", "外观", "脏污", "压痕", "defect", "scratch", "外观检测"]))
            return "appearance_defect";

        return string.Empty;
    }

    private static string ResolveIndustry(ScenarioDefinition? scenario, string text)
    {
        if (!string.IsNullOrWhiteSpace(scenario?.Industry))
            return scenario!.Industry;

        if (ContainsAny(text, ["线束", "端子", "排针", "排线"]))
            return "线束装配";
        if (ContainsAny(text, ["空调", "aircon", "air condition"]))
            return "空调制造";
        if (ContainsAny(text, ["包装", "纸箱", "carton", "包装箱"]))
            return "包装终检";
        if (ContainsAny(text, ["PCB", "电路板", "线路板"]))
            return "电子制造";

        return "通用制造";
    }

    private static string ResolveObjectName(ScenarioDefinition? scenario, string text)
    {
        if (!string.IsNullOrWhiteSpace(scenario?.ObjectTypes.FirstOrDefault()))
            return scenario!.ObjectTypes[0];

        if (ContainsAny(text, ["包装箱", "纸箱", "carton"]))
            return "包装箱";
        if (ContainsAny(text, ["内机", "空调内机", "indoor"]))
            return "空调内机";
        if (ContainsAny(text, ["外机", "空调外机", "outdoor"]))
            return "空调外机";
        if (ContainsAny(text, ["遥控器", "remote"]))
            return "遥控器";
        if (ContainsAny(text, ["端子", "排针"]))
            return "端子排";
        if (ContainsAny(text, ["两器", "铜孔", "铜管"]))
            return "两器";

        return string.Empty;
    }

    private static string ResolveImageSource(ScenarioDefinition? scenario, string text, int attachmentCount)
    {
        if (ContainsAny(text, ["相机", "camera", "摄像头", "实时"]))
            return "camera";
        if (ContainsAny(text, ["文件", "图片", "file", "image", "本地"]))
            return "file";
        if (attachmentCount > 0)
            return "file";

        return "unknown";
    }

    private static string ResolveTriggerMode(string text)
    {
        if (ContainsAny(text, ["硬件触发", "hardware trigger", "光电", "传感器触发"]))
            return "hardware";
        if (ContainsAny(text, ["软件触发", "software trigger", "命令触发"]))
            return "software";
        if (ContainsAny(text, ["连续", "continuous", "实时"]))
            return "continuous";

        return "unknown";
    }

    private static string ResolveOutputTarget(string text)
    {
        if (ContainsAny(text, ["PLC", "plc", "控制信号", "输出信号", "DO信号"]))
            return "PLC";
        if (ContainsAny(text, ["数据库", "database", "MES", "SQL"]))
            return "Database";
        if (ContainsAny(text, ["显示", "ResultOutput", "看板", "界面"]))
            return "ResultOutput";

        return "unknown";
    }

    private static string ResolveAiModelRequired(string text, List<string> defectTypes)
    {
        if (ContainsAny(text, ["深度学习", "YOLO", "DeepLearning", "目标检测", "语义分割", "AI检测", "模型"]))
            return "true";
        if (ContainsAny(text, ["传统", "阈值", "Blob", "边缘", "模板匹配", "不用AI", "不用模型"]))
            return "false";
        if (defectTypes.Count > 0)
            return "true"; // defect detection defaults to AI

        return "unknown";
    }

    private static string ResolveModelResource(string text)
    {
        if (ContainsAny(text, [".onnx", ".pt", ".pth", ".weights", ".pb", ".model", "ModelPath"]))
            return "provided";

        return "missing";
    }

    private static string ResolveRoiRequirement(string text)
    {
        if (ContainsAny(text, ["ROI", "roi", "区域", "指定位置", "局部", "region"]))
            return "region";
        if (ContainsAny(text, ["全图", "整张", "全局"]))
            return "none";

        return "unknown";
    }

    private static string ResolveCalibrationRequirement(string text)
    {
        if (ContainsAny(text, ["手眼标定", "hand eye", "hand-eye"]))
            return "hand_eye";
        if (ContainsAny(text, ["像素到物理", "标定板", "calibration", "mm", "毫米", "物理单位", "pixel to world"]))
            return "pixel_to_world";
        if (ContainsAny(text, ["不需要标定", "无标定", "像素级别"]))
            return "none";

        return "unknown";
    }

    private static string ResolveDecisionRule(string text, string sceneType)
    {
        if (ContainsAny(text, ["任一", "任意", "任一缺陷", "任意一个", "只要有一个"]))
            return "任一缺陷存在即 NG";
        if (ContainsAny(text, ["全部", "所有", "都合格", "全部通过"]))
            return "所有测量在公差内才 OK";
        if (sceneType == "measurement" && ContainsAny(text, ["±", "公差", "范围"]))
            return "测量值超出公差即 NG";

        return string.Empty;
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
        if (ContainsAny(text, ["线序", "排线顺序"]))
            return "wire_sequence_check";

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

    private static List<string> BuildMissingFields(
        string sceneType,
        string objectName,
        List<string> defectTypes,
        List<string> measurementTargets,
        string imageSource,
        string modelResource,
        string decisionRule,
        string calibrationRequirement)
    {
        var missing = new List<string>();

        if (string.IsNullOrWhiteSpace(sceneType))
            missing.Add("sceneType");

        if (string.IsNullOrWhiteSpace(objectName))
            missing.Add("objectName");

        if (sceneType is "appearance_defect" or "wire_sequence" or "missing_part")
        {
            if (defectTypes.Count == 0)
                missing.Add("defectTypes");
        }

        if (sceneType == "measurement")
        {
            if (measurementTargets.Count == 0)
                missing.Add("measurementTargets");
            if (string.IsNullOrWhiteSpace(decisionRule))
                missing.Add("decisionRule");
        }

        if (string.Equals(modelResource, "missing", StringComparison.OrdinalIgnoreCase))
            missing.Add("modelResource");

        if (string.Equals(imageSource, "unknown", StringComparison.OrdinalIgnoreCase))
            missing.Add("imageSource");

        if (string.Equals(calibrationRequirement, "unknown", StringComparison.OrdinalIgnoreCase) &&
            sceneType == "measurement")
            missing.Add("calibrationRequirement");

        return missing;
    }

    private static double EstimateConfidence(string text, string sceneType)
    {
        if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(sceneType))
            return 0.0;

        var signals = 0;
        if (ContainsAny(text, ["检测", "判定", "识别", "测量"]))
            signals++;
        if (ContainsAny(text, ["划伤", "破损", "脏污", "漏装", "错序", "孔距", "间距"]))
            signals++;
        if (ContainsAny(text, ["OK", "NG", "合格", "不合格", "通过"]))
            signals++;

        return Math.Clamp(signals * 0.25, 0.0, 0.75);
    }

    private static IEnumerable<AiClarificationQuestion> BuildClarificationQuestions(
        AiRequirementBrief brief,
        ScenarioMatchResult? scenarioMatch)
    {
        var confidence = scenarioMatch?.Confidence ?? brief.Confidence;

        if (string.IsNullOrWhiteSpace(brief.ScenarioKey) || confidence < LowConfidenceThreshold)
        {
            yield return new AiClarificationQuestion
            {
                Field = "scenario",
                Question = "请确认这是外观缺陷检测、漏装有无检测、线序判定还是尺寸测量场景。",
                Required = true,
                Level = "Required",
                Options = ["外观缺陷检测", "漏装有无检测", "线序判定", "尺寸测量", "读码/OCR", "标定", "其他"],
                Reason = "不同场景对应不同的模板、算子链和必需资源，明确场景后才能准确生成流程。"
            };
        }

        if (brief.DefectTypes.Count == 0 && brief.IntentType.Contains("defect", StringComparison.OrdinalIgnoreCase))
        {
            yield return new AiClarificationQuestion
            {
                Field = "defectTypes",
                Question = "请补充需要判定的缺陷类别，例如划伤、压痕、破损、标签异常。",
                Required = true,
                Level = "Required",
                Options = ["划伤", "压痕", "破损", "脏污", "标签异常", "毛刺", "变形", "色差"],
                Reason = "缺陷类型影响算子选择和判定阈值。"
            };
        }

        if (brief.MeasurementTargets.Count == 0 && brief.IntentType.Contains("measurement", StringComparison.OrdinalIgnoreCase))
        {
            yield return new AiClarificationQuestion
            {
                Field = "measurementTargets",
                Question = "请补充测量对象、单位和合格范围。例如：孔间距 10±0.5mm。",
                Required = true,
                Level = "Required",
                Options = ["孔距", "圆心距离", "边缘间距", "直径", "角度", "面积"],
                Reason = "测量目标和合格范围是生成测量流程的核心依据。"
            };
        }

        if (brief.RequiredResources.Count > 0 && string.Equals(brief.ModelResource, "missing", StringComparison.OrdinalIgnoreCase))
        {
            yield return new AiClarificationQuestion
            {
                Field = "modelResource",
                Question = $"请确认是否已准备好所需资源：{string.Join(", ", brief.RequiredResources)}。",
                Required = false,
                Level = "Recommended",
                Options = ["已有模型，稍后提供路径", "暂无模型，先创建流程骨架", "使用传统视觉方法"],
                Reason = "AI 检测算子依赖模型文件，缺模型时可先生成流程骨架。"
            };
        }

        if (brief.AttachmentCount > 0 && !brief.ModelSupportsVision)
        {
            yield return new AiClarificationQuestion
            {
                Field = "attachments",
                Question = $"已收到 {brief.AttachmentCount} 个附件，但当前模型不支持视觉输入，附件仅用于元信息。是否继续文本模式生成？",
                Required = false,
                Level = "Recommended",
                Options = ["继续文本模式生成", "换一个支持视觉的模型"],
                Reason = "模型不支持视觉输入时，附件中的图像内容不会被发送给模型。"
            };
        }
    }

    private static bool ContainsAny(string text, IEnumerable<string> terms)
    {
        return terms.Any(term => !string.IsNullOrWhiteSpace(term) &&
                                 text.Contains(term, StringComparison.OrdinalIgnoreCase));
    }
}
