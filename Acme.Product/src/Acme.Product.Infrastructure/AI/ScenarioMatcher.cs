using Acme.Product.Core.Entities;

namespace Acme.Product.Infrastructure.AI;

public interface IScenarioMatcher
{
    Task<IReadOnlyList<ScenarioMatchResult>> MatchAsync(
        string? description,
        string? additionalContext = null,
        IReadOnlyList<string>? attachments = null,
        int topN = 3,
        CancellationToken cancellationToken = default);
}

public sealed class ScenarioMatcher : IScenarioMatcher
{
    private readonly IFlowTemplateService _templateService;

    public ScenarioMatcher(IFlowTemplateService templateService)
    {
        _templateService = templateService;
    }

    public async Task<IReadOnlyList<ScenarioMatchResult>> MatchAsync(
        string? description,
        string? additionalContext = null,
        IReadOnlyList<string>? attachments = null,
        int topN = 3,
        CancellationToken cancellationToken = default)
    {
        var text = BuildSearchText(description, additionalContext, attachments);
        if (string.IsNullOrWhiteSpace(text))
            return Array.Empty<ScenarioMatchResult>();

        IReadOnlyList<FlowTemplate> templates;
        try
        {
            templates = await _templateService.GetTemplatesAsync(cancellationToken: cancellationToken)
                ?? Array.Empty<FlowTemplate>();
        }
        catch
        {
            templates = Array.Empty<FlowTemplate>();
        }

        var definitions = BuildDefinitions(templates);
        return definitions
            .Select(definition =>
            {
                var match = Score(definition, text);
                match.Template = ResolveTemplate(templates, definition);
                return match;
            })
            .Where(match => match.Confidence > 0.08)
            .OrderByDescending(match => match.Confidence)
            .ThenBy(match => match.Scenario.ScenarioName, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Max(1, topN))
            .ToList();
    }

    public static IReadOnlyList<ScenarioDefinition> BuildDefinitions(IReadOnlyList<FlowTemplate> templates)
    {
        var byScenario = templates
            .Where(template => !string.IsNullOrWhiteSpace(template.Name))
            .GroupBy(template => ResolveScenarioKey(template), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        var definitions = new List<ScenarioDefinition>
        {
            BuildDefinition(
                byScenario,
                key: "classic-template-matching-inspection",
                templateName: "传统模板匹配检测",
                industry: "通用制造",
                keywords: ["传统视觉", "传统模板匹配", "模板匹配", "标准模板", "标准图", "参考图", "基准图", "template matching", "reference image", "golden sample"],
                synonyms: ["上传模板", "模板图", "标准样", "样板", "对比", "比对", "合格与否", "OK/NG"],
                negativeKeywords: [],
                intentTypes: ["template_matching_inspection", "appearance_inspection"],
                objectTypes: ["产品", "standard_template", "reference_image"],
                defectTypes: [],
                measurementTargets: []),
            BuildDefinition(
                byScenario,
                key: "gradient-shape-match-positioning",
                templateName: "梯度形状匹配定位检测",
                industry: "通用制造",
                keywords: ["梯度形状", "形状匹配", "轮廓匹配", "旋转定位", "方向变化", "gradient shape", "shape match"],
                synonyms: ["边缘轮廓", "工件定位", "治具定位", "旋转角度", "姿态变化", "有无检测"],
                negativeKeywords: ["深度学习", "AI模型", "包装箱", "线序", "二维码", "条码", "颜色"],
                intentTypes: ["positioning", "presence_check", "shape_matching"],
                objectTypes: ["工件", "轮廓", "fixture", "part"],
                defectTypes: ["missing_part", "错位"],
                measurementTargets: []),
            BuildDefinition(
                byScenario,
                key: "planar-feature-label-positioning",
                templateName: "平面特征匹配定位检测",
                industry: "包装追溯",
                keywords: ["平面匹配", "特征匹配", "单应性", "透视", "铭牌", "标签定位", "homography", "planar matching"],
                synonyms: ["ORB", "AKAZE", "BRISK", "透视变化", "贴标定位", "印刷件定位", "图案定位"],
                negativeKeywords: ["线序", "孔径", "圆孔", "Blob", "连通域", "清晰度"],
                intentTypes: ["positioning", "feature_matching", "label_inspection"],
                objectTypes: ["label", "nameplate", "planar_target", "printed_part"],
                defectTypes: ["贴歪", "错位", "missing_label"],
                measurementTargets: []),
            BuildDefinition(
                byScenario,
                key: "blob-defect-region-analysis",
                templateName: "Blob缺陷区域分析",
                industry: "通用制造",
                keywords: ["Blob", "连通域", "黑点", "白点", "脏污", "缺料", "毛刺", "异物", "二值化", "阈值分割"],
                synonyms: ["污点", "颗粒", "斑点", "区域缺陷", "面积过滤", "形态学", "开运算", "blob analysis"],
                negativeKeywords: ["深度学习", "YOLO", "线序", "条码", "二维码", "圆心距离"],
                intentTypes: ["defect_detection", "region_analysis", "blob_analysis"],
                objectTypes: ["surface", "plastic_part", "metal_part", "region"],
                defectTypes: ["stain", "burr", "foreign_object", "missing_material", "black_spot", "white_spot"],
                measurementTargets: ["defect_area", "blob_count"]),
            BuildDefinition(
                byScenario,
                key: "caliper-width-measurement",
                templateName: "卡尺宽度测量",
                industry: "精密装配",
                keywords: ["卡尺", "宽度", "槽宽", "胶宽", "焊缝宽度", "边缘对", "间隙宽度", "caliper"],
                synonyms: ["两条边", "边缘距离", "边到边", "尺寸超差", "宽度超差", "在线尺寸"],
                negativeKeywords: ["圆孔", "圆心距", "孔距", "颜色", "条码", "线序"],
                intentTypes: ["measurement", "width_measurement", "gap_measurement"],
                objectTypes: ["edge_pair", "slot", "sealant", "weld", "gap"],
                defectTypes: ["out_of_range"],
                measurementTargets: ["宽度", "胶条宽度", "槽宽", "间隙宽度", "width", "edge_pair_width", "gap_width"]),
            BuildDefinition(
                byScenario,
                key: "circular-hole-radius-measurement",
                templateName: "圆孔孔径与圆度检测",
                industry: "精密装配",
                keywords: ["圆孔", "孔径", "孔半径", "圆度", "螺丝孔", "冲孔", "孔数量", "circle measurement"],
                synonyms: ["圆形孔", "孔洞", "半径", "直径", "孔位检测", "圆测量"],
                negativeKeywords: ["圆心距", "孔距", "两个孔", "两器", "线序", "条码", "颜色"],
                intentTypes: ["measurement", "circle_measurement", "hole_inspection"],
                objectTypes: ["hole", "circle", "screw_hole", "punched_hole"],
                defectTypes: ["out_of_range", "missing_hole", "deformed_hole"],
                measurementTargets: ["radius", "diameter", "circularity", "hole_count"]),
            BuildDefinition(
                byScenario,
                key: "color-deltae-inspection",
                templateName: "Lab色差检测",
                industry: "通用制造",
                keywords: ["颜色", "色差", "色偏", "DeltaE", "Lab", "喷涂颜色", "注塑颜色", "颜色偏差"],
                synonyms: ["颜色一致性", "色值", "CIEDE2000", "色差仪", "标签颜色", "指示灯颜色"],
                negativeKeywords: ["条码", "二维码", "线序", "圆孔", "宽度", "Blob"],
                intentTypes: ["color_inspection", "measurement"],
                objectTypes: ["coating", "plastic_part", "label", "indicator"],
                defectTypes: ["color_shift", "wrong_color"],
                measurementTargets: ["delta_e", "lab_deltae", "hue"]),
            BuildDefinition(
                byScenario,
                key: "code-traceability-inspection",
                templateName: "条码二维码追溯检测",
                industry: "包装追溯",
                keywords: ["条码", "二维码", "追溯", "扫码", "读码", "DataMatrix", "Code128", "QR"],
                synonyms: ["SN码", "序列号", "产品码", "包装码", "铭牌码", "码制", "traceability"],
                negativeKeywords: ["颜色", "色差", "圆孔", "卡尺", "线序", "Blob"],
                intentTypes: ["code_recognition", "traceability", "presence_check"],
                objectTypes: ["barcode", "qr_code", "datamatrix", "label_code"],
                defectTypes: ["unreadable_code", "missing_code"],
                measurementTargets: ["code_count"]),
            BuildDefinition(
                byScenario,
                key: "surface-reference-defect-inspection",
                templateName: "参考图表面缺陷检测",
                industry: "表面检测",
                keywords: ["表面缺陷", "参考图差分", "良品参考", "划伤", "刮痕", "膜面", "金属表面", "低对比缺陷"],
                synonyms: ["reference diff", "表面划伤", "脏污检测", "局部对比", "对齐差分", "相位相关", "不用深度学习"],
                negativeKeywords: ["线序", "条码", "二维码", "圆孔", "宽度", "颜色"],
                intentTypes: ["defect_detection", "surface_inspection", "reference_comparison"],
                objectTypes: ["surface", "metal_surface", "film", "coating"],
                defectTypes: ["scratch", "stain", "dent", "foreign_object"],
                measurementTargets: ["defect_count", "defect_area"]),
            BuildDefinition(
                byScenario,
                key: "sharpness-focus-gate",
                templateName: "清晰度对焦质量门",
                industry: "通用制造",
                keywords: ["清晰度", "对焦", "失焦", "模糊", "运动模糊", "焦点", "focus", "sharpness"],
                synonyms: ["图像质量", "质量门", "脏污镜头", "虚焦", "清晰度评分", "Tenengrad", "Laplacian"],
                negativeKeywords: ["线序", "条码", "二维码", "颜色", "孔径", "宽度"],
                intentTypes: ["image_quality_gate", "focus_check"],
                objectTypes: ["camera", "image", "lens"],
                defectTypes: ["blur", "defocus"],
                measurementTargets: ["sharpness_score"]),
            BuildDefinition(
                byScenario,
                key: "wire-sequence-terminal",
                templateName: "端子线序检测",
                industry: "线束装配",
                keywords: ["线序", "端子", "接线顺序", "排针顺序", "黑蓝", "黑 蓝", "wire sequence", "terminal order", "connector order", "wiring order"],
                synonyms: ["线束", "插针", "端子排", "线根", "颜色顺序", "pin order"],
                negativeKeywords: ["包装箱", "空调", "遥控器", "铜孔", "间距"],
                intentTypes: ["sequence_check", "object_detection"],
                objectTypes: ["terminal", "wire", "connector"],
                defectTypes: ["wrong_sequence", "missing_wire"],
                measurementTargets: []),
            BuildDefinition(
                byScenario,
                key: "carton-appearance-inspection",
                templateName: "包装箱外观检测",
                industry: "包装终检",
                keywords: ["包装箱", "纸箱", "箱体", "外观", "carton", "box", "package"],
                synonyms: ["包装", "箱面", "封箱", "标签", "终检"],
                negativeKeywords: ["空调", "内机", "外机", "遥控器", "铜孔", "端子", "传统视觉", "传统模板匹配", "模板匹配", "标准模板", "标准图", "参考图", "基准图", "上传模板", "reference image", "golden sample"],
                intentTypes: ["defect_detection", "appearance_inspection"],
                objectTypes: ["carton", "package", "label"],
                defectTypes: ["破损", "压痕", "脏污", "封箱异常", "标签异常", "damage", "dent", "stain"],
                measurementTargets: []),
            BuildDefinition(
                byScenario,
                key: "aircon-indoor-appearance-inspection",
                templateName: "空调内机外观检测",
                industry: "空调制造",
                keywords: ["空调内机", "内机", "面板", "室内机", "indoor unit", "panel"],
                synonyms: ["空调面板", "内机面板", "总装", "终检"],
                negativeKeywords: ["外机", "室外机", "遥控器", "包装箱", "铜孔", "端子", "传统视觉", "传统模板匹配", "模板匹配", "标准模板", "标准图", "参考图", "基准图", "上传模板", "reference image", "golden sample"],
                intentTypes: ["defect_detection", "appearance_inspection"],
                objectTypes: ["aircon_indoor", "panel"],
                defectTypes: ["划伤", "缝隙", "污渍", "磕碰", "scratch", "gap", "stain"],
                measurementTargets: ["panel_gap"]),
            BuildDefinition(
                byScenario,
                key: "aircon-outdoor-appearance-inspection",
                templateName: "空调外机外观检测",
                industry: "空调制造",
                keywords: ["空调外机", "外机", "室外机", "翅片", "护网", "outdoor unit", "condenser"],
                synonyms: ["外机外观", "冷凝器", "总装", "终检"],
                negativeKeywords: ["内机", "室内机", "遥控器", "包装箱", "铜孔", "端子", "传统视觉", "传统模板匹配", "模板匹配", "标准模板", "标准图", "参考图", "基准图", "上传模板", "reference image", "golden sample"],
                intentTypes: ["defect_detection", "appearance_inspection"],
                objectTypes: ["aircon_outdoor", "fin", "guard"],
                defectTypes: ["变形", "破损", "凹陷", "缺件", "deform", "dent", "missing"],
                measurementTargets: []),
            BuildDefinition(
                byScenario,
                key: "remote-controller-missing-inspection",
                templateName: "遥控器漏装检测",
                industry: "空调制造",
                keywords: ["遥控器", "漏装", "附件", "有无", "remote", "missing", "accessory"],
                synonyms: ["附件区", "配件", "是否存在", "少装"],
                negativeKeywords: ["包装箱破损", "铜孔", "线序", "端子", "传统视觉", "传统模板匹配", "模板匹配", "标准模板", "标准图", "参考图", "基准图", "上传模板", "reference image", "golden sample"],
                intentTypes: ["presence_check", "object_detection"],
                objectTypes: ["remote_controller", "accessory"],
                defectTypes: ["missing_part", "漏装", "缺失"],
                measurementTargets: []),
            BuildDefinition(
                byScenario,
                key: "copper-hole-spacing-measurement",
                templateName: "两器铜孔间距检测",
                industry: "空调制造",
                keywords: ["两器", "铜孔", "圆孔", "孔位", "圆形孔", "孔距", "圆心距", "圆心距离", "间距", "距离", "spacing", "gap", "pitch"],
                synonyms: ["换热器", "铜管孔", "孔之间", "两个孔", "两个圆形孔", "距离是否合格", "测量"],
                negativeKeywords: ["遥控器", "包装箱", "线序", "端子", "外观划伤"],
                intentTypes: ["measurement", "gap_measurement"],
                objectTypes: ["copper_hole", "heat_exchanger"],
                defectTypes: ["out_of_range"],
                measurementTargets: ["hole_spacing", "copper_hole_spacing"])
        };

        return definitions;
    }

    private static ScenarioDefinition BuildDefinition(
        IReadOnlyDictionary<string, FlowTemplate> templates,
        string key,
        string templateName,
        string industry,
        IReadOnlyList<string> keywords,
        IReadOnlyList<string> synonyms,
        IReadOnlyList<string> negativeKeywords,
        IReadOnlyList<string> intentTypes,
        IReadOnlyList<string> objectTypes,
        IReadOnlyList<string> defectTypes,
        IReadOnlyList<string> measurementTargets)
    {
        templates.TryGetValue(key, out var template);
        template ??= templates.Values.FirstOrDefault(item =>
            string.Equals(item.Name, templateName, StringComparison.OrdinalIgnoreCase));

        return new ScenarioDefinition
        {
            ScenarioKey = key,
            ScenarioName = templateName,
            Industry = template?.Industry ?? industry,
            Keywords = keywords.ToList(),
            Synonyms = synonyms.ToList(),
            NegativeKeywords = negativeKeywords.ToList(),
            IntentTypes = intentTypes.ToList(),
            ObjectTypes = objectTypes.ToList(),
            DefectTypes = defectTypes.ToList(),
            MeasurementTargets = measurementTargets.ToList(),
            RequiredResources = template?.ScenarioPackage?.RequiredResources.ToList() ?? InferRequiredResources(templateName),
            TemplateId = template?.Id == Guid.Empty ? null : template?.Id.ToString(),
            TemplateName = template?.Name ?? templateName,
            TemplateVersion = template?.TemplateVersion ?? "1.0.0"
        };
    }

    private static List<string> InferRequiredResources(string templateName)
    {
        if (templateName.Contains("外观", StringComparison.OrdinalIgnoreCase) ||
            templateName.Contains("漏装", StringComparison.OrdinalIgnoreCase) ||
            templateName.Contains("线序", StringComparison.OrdinalIgnoreCase))
        {
            return ["DeepLearning.ModelPath"];
        }

        return new List<string>();
    }

    private static ScenarioMatchResult Score(ScenarioDefinition definition, string text)
    {
        var matchedFields = new List<string>();
        var matchedTerms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var score = 0.0;

        AddMatches(definition.Keywords, "keywords", 4.0);
        AddMatches(definition.Synonyms, "synonyms", 2.0);
        AddMatches(definition.ObjectTypes, "objectTypes", 2.4);
        AddMatches(definition.DefectTypes, "defectTypes", 2.6);
        AddMatches(definition.MeasurementTargets, "measurementTargets", 3.2);
        AddMatches(definition.IntentTypes, "intentTypes", 1.2);

        var negativeHits = definition.NegativeKeywords
            .Where(term => ContainsTerm(text, term))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        score -= negativeHits.Count * 3.5;

        var hasIndustrySignal = ContainsTerm(text, definition.Industry);
        if (hasIndustrySignal)
        {
            score += 1.0;
            AddField(matchedFields, "industry");
        }

        var missingSignals = BuildMissingSignals(definition, matchedFields, negativeHits);
        var confidence = Math.Clamp(score / 12.0, 0, 0.99);
        if (matchedFields.Count >= 3)
            confidence = Math.Min(0.99, confidence + 0.12);
        if (negativeHits.Count > 0)
            confidence = Math.Max(0, confidence - 0.12);

        var reason = matchedTerms.Count > 0
            ? $"Matched {string.Join(", ", matchedTerms.Take(6))}"
            : "Weak lexical match";

        return new ScenarioMatchResult
        {
            Scenario = definition,
            Confidence = Math.Round(confidence, 4),
            MatchReason = reason,
            MatchedFields = matchedFields,
            MissingSignals = missingSignals
        };

        void AddMatches(IEnumerable<string> terms, string field, double weight)
        {
            var hits = terms
                .Where(term => ContainsTerm(text, term))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (hits.Count == 0)
                return;

            score += hits.Count * weight;
            AddField(matchedFields, field);
            foreach (var hit in hits)
                matchedTerms.Add(hit);
        }
    }

    private static List<string> BuildMissingSignals(
        ScenarioDefinition definition,
        IReadOnlyCollection<string> matchedFields,
        IReadOnlyCollection<string> negativeHits)
    {
        var missing = new List<string>();
        if (!matchedFields.Contains("objectTypes", StringComparer.OrdinalIgnoreCase))
            missing.Add("object_type");
        if (!matchedFields.Contains("defectTypes", StringComparer.OrdinalIgnoreCase) &&
            definition.IntentTypes.Any(item => item.Contains("defect", StringComparison.OrdinalIgnoreCase)))
        {
            missing.Add("defect_type");
        }
        if (!matchedFields.Contains("measurementTargets", StringComparer.OrdinalIgnoreCase) &&
            definition.IntentTypes.Any(item => item.Contains("measurement", StringComparison.OrdinalIgnoreCase)))
        {
            missing.Add("measurement_target");
        }
        if (negativeHits.Count > 0)
            missing.Add("ambiguous_negative_signal");

        return missing.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string ResolveScenarioKey(FlowTemplate template)
    {
        if (!string.IsNullOrWhiteSpace(template.ScenarioKey))
            return template.ScenarioKey.Trim();

        return template.Name switch
        {
            "传统模板匹配检测" => "classic-template-matching-inspection",
            "包装箱外观检测" => "carton-appearance-inspection",
            "梯度形状匹配定位检测" => "gradient-shape-match-positioning",
            "平面特征匹配定位检测" => "planar-feature-label-positioning",
            "Blob缺陷区域分析" => "blob-defect-region-analysis",
            "卡尺宽度测量" => "caliper-width-measurement",
            "圆孔孔径与圆度检测" => "circular-hole-radius-measurement",
            "Lab色差检测" => "color-deltae-inspection",
            "条码二维码追溯检测" => "code-traceability-inspection",
            "参考图表面缺陷检测" => "surface-reference-defect-inspection",
            "清晰度对焦质量门" => "sharpness-focus-gate",
            "空调内机外观检测" => "aircon-indoor-appearance-inspection",
            "空调外机外观检测" => "aircon-outdoor-appearance-inspection",
            "遥控器漏装检测" => "remote-controller-missing-inspection",
            "两器铜孔间距检测" => "copper-hole-spacing-measurement",
            "端子线序检测" => "wire-sequence-terminal",
            _ => template.Name.Trim()
        };
    }

    private static FlowTemplate? ResolveTemplate(IReadOnlyList<FlowTemplate> templates, ScenarioDefinition definition)
    {
        if (!string.IsNullOrWhiteSpace(definition.TemplateId) &&
            Guid.TryParse(definition.TemplateId, out var templateId))
        {
            var byId = templates.FirstOrDefault(item => item.Id == templateId);
            if (byId != null)
                return byId;
        }

        return templates.FirstOrDefault(item =>
                   string.Equals(ResolveScenarioKey(item), definition.ScenarioKey, StringComparison.OrdinalIgnoreCase))
               ?? templates.FirstOrDefault(item =>
                   string.Equals(item.Name, definition.TemplateName, StringComparison.OrdinalIgnoreCase));
    }

    private static string BuildSearchText(
        string? description,
        string? additionalContext,
        IReadOnlyList<string>? attachments)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(description))
            parts.Add(description);
        if (!string.IsNullOrWhiteSpace(additionalContext))
            parts.Add(additionalContext);
        if (attachments is { Count: > 0 })
            parts.Add(string.Join(" ", attachments.Select(Path.GetFileName)));

        return string.Join(" ", parts).Trim().ToLowerInvariant();
    }

    private static bool ContainsTerm(string text, string? term)
    {
        if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(term))
            return false;

        return text.Contains(term.Trim().ToLowerInvariant(), StringComparison.OrdinalIgnoreCase);
    }

    private static void AddField(List<string> fields, string field)
    {
        if (!fields.Contains(field, StringComparer.OrdinalIgnoreCase))
            fields.Add(field);
    }
}
