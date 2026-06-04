using System.Text.Json;
using ClearVision.Product.Core.AI.Tools;

namespace ClearVision.Product.Infrastructure.AI.Tools;

public sealed class FlowTemplateMatchTool : VisionAgentToolBase
{
    public override string Name => "match_flow_template";
    public override string DisplayName => "Match flow template";
    public override string Description => "Matches a user request against static read-only flow template candidates.";
    public override string Category => "template";
    public override JsonElement ParametersSchema { get; } = Schema("""
        {
          "type": "object",
          "properties": {
            "request": { "type": "string" },
            "topN": { "type": "integer", "minimum": 1, "maximum": 10 }
          }
        }
        """);

    public override Task<VisionAgentToolResult> ExecuteAsync(
        VisionAgentToolContext context,
        JsonElement arguments,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var request = ReadString(arguments, "request") ?? context.UserDescription;
        var topN = Math.Clamp(ReadInt(arguments, "topN") ?? 3, 1, 10);
        var candidates = VisionAgentReadOnlyCatalog.Templates
            .Select(template => new
            {
                templateId = template.TemplateId,
                scenarioKey = template.ScenarioKey,
                name = template.Name,
                score = Score(template, request),
                matchedKeywords = template.Keywords
                    .Where(keyword => request.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                    .ToList(),
                operatorTypes = template.OperatorTypes
            })
            .OrderByDescending(item => item.score)
            .ThenBy(item => item.templateId, StringComparer.OrdinalIgnoreCase)
            .Take(topN)
            .ToList();

        return Task.FromResult(VisionAgentToolResult.Ok(new
        {
            source = "readonly_static_templates",
            request,
            candidates
        }));
    }

    private static double Score(TemplateItem template, string request)
    {
        if (string.IsNullOrWhiteSpace(request))
        {
            return 0.1;
        }

        var score = 0.1;
        foreach (var keyword in template.Keywords)
        {
            if (request.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                score += 0.3;
            }
        }

        if (request.Contains(template.ScenarioKey, StringComparison.OrdinalIgnoreCase) ||
            request.Contains(template.TemplateId, StringComparison.OrdinalIgnoreCase))
        {
            score += 0.5;
        }

        return Math.Min(score, 1.0);
    }
}

public sealed class FlowTemplateSkeletonTool : VisionAgentToolBase
{
    public override string Name => "get_flow_template_skeleton";
    public override string DisplayName => "Get flow template skeleton";
    public override string Description => "Returns a read-only operator/connection skeleton for a matched template.";
    public override string Category => "template";
    public override JsonElement ParametersSchema { get; } = Schema("""
        {
          "type": "object",
          "properties": {
            "templateId": { "type": "string" },
            "scenarioKey": { "type": "string" }
          }
        }
        """);

    public override Task<VisionAgentToolResult> ExecuteAsync(
        VisionAgentToolContext context,
        JsonElement arguments,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var templateId = ReadString(arguments, "templateId");
        var scenarioKey = ReadString(arguments, "scenarioKey");
        var template = VisionAgentReadOnlyCatalog.Templates.FirstOrDefault(item =>
            string.Equals(item.TemplateId, templateId, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(item.ScenarioKey, scenarioKey, StringComparison.OrdinalIgnoreCase));
        if (template == null)
        {
            return Task.FromResult(VisionAgentToolResult.Fail(
                "template_not_found",
                "templateId or scenarioKey does not match a read-only template.",
                new { templateId, scenarioKey }));
        }

        var operators = BuildOperators(template);
        return Task.FromResult(VisionAgentToolResult.Ok(new
        {
            source = "readonly_static_template_skeleton",
            templateId = template.TemplateId,
            scenarioKey = template.ScenarioKey,
            name = template.Name,
            operators,
            connections = template.Connections.Select(connection => new
            {
                sourceTempId = connection.SourceTempId,
                sourcePortName = connection.SourcePortName,
                targetTempId = connection.TargetTempId,
                targetPortName = connection.TargetPortName
            }).ToList(),
            resourcePolicy = "No camera capture, frame replay, model loading, template file loading, or Station access is performed."
        }));
    }

    private static IReadOnlyList<object> BuildOperators(TemplateItem template)
    {
        var counters = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        return template.OperatorTypes.Select(operatorType =>
        {
            counters.TryGetValue(operatorType, out var count);
            counters[operatorType] = ++count;
            var suffix = count == 1 ? string.Empty : $"_{count}";
            return new
            {
                tempId = operatorType switch
                {
                    "ImageAcquisition" => "op_cam" + suffix,
                    "RoiManager" => "op_roi" + suffix,
                    "DeepLearning" => "op_detect" + suffix,
                    "TemplateMatching" => "op_match" + suffix,
                    "CircleMeasurement" => count == 1 ? "op_circle_a" : "op_circle_b",
                    "MeasureDistance" => "op_distance" + suffix,
                    "ResultJudgment" => "op_judge" + suffix,
                    "ResultOutput" => "op_out" + suffix,
                    _ => $"op_{operatorType.ToLowerInvariant()}{suffix}"
                },
                operatorType,
                displayName = operatorType,
                parameters = BuildPlaceholderParameters(operatorType)
            };
        }).ToList<object>();
    }

    private static IReadOnlyDictionary<string, string> BuildPlaceholderParameters(string operatorType)
    {
        return operatorType switch
        {
            "ImageAcquisition" => new Dictionary<string, string> { ["CameraBindingId"] = "<pending-camera-binding>" },
            "TemplateMatching" => new Dictionary<string, string> { ["TemplatePath"] = "<pending-template-path>" },
            "DeepLearning" => new Dictionary<string, string> { ["ModelPath"] = "<pending-model-path>" },
            "MeasureDistance" => new Dictionary<string, string> { ["Unit"] = "mm", ["Tolerance"] = "<pending-tolerance>" },
            "ResultOutput" => new Dictionary<string, string> { ["Channel"] = "<pending-output-channel>" },
            _ => new Dictionary<string, string>()
        };
    }
}
