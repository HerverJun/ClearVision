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
                negativeKeywords: ["空调", "内机", "外机", "遥控器", "铜孔", "端子"],
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
                negativeKeywords: ["外机", "室外机", "遥控器", "包装箱", "铜孔", "端子"],
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
                negativeKeywords: ["内机", "室内机", "遥控器", "包装箱", "铜孔", "端子"],
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
                negativeKeywords: ["包装箱破损", "铜孔", "线序", "端子"],
                intentTypes: ["presence_check", "object_detection"],
                objectTypes: ["remote_controller", "accessory"],
                defectTypes: ["missing_part", "漏装", "缺失"],
                measurementTargets: []),
            BuildDefinition(
                byScenario,
                key: "copper-hole-spacing-measurement",
                templateName: "两器铜孔间距检测",
                industry: "空调制造",
                keywords: ["两器", "铜孔", "孔距", "间距", "距离", "spacing", "gap", "pitch"],
                synonyms: ["换热器", "铜管孔", "孔之间", "距离是否合格", "测量"],
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
            "包装箱外观检测" => "carton-appearance-inspection",
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
