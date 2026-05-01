using System.Text.Json;
using Acme.Product.Core.DTOs;
using Acme.Product.Core.Entities;
using Acme.Product.Core.Services;

namespace Acme.Product.Infrastructure.AI;

public interface ITemplateConstraintValidator
{
    AiValidationResult Validate(
        AiGeneratedFlowJson generatedFlow,
        FlowTemplate? template,
        bool strict = true);
}

public sealed class TemplateConstraintValidator : ITemplateConstraintValidator
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters =
        {
            new FlexibleStringDictionaryJsonConverter()
        }
    };

    public AiValidationResult Validate(
        AiGeneratedFlowJson generatedFlow,
        FlowTemplate? template,
        bool strict = true)
    {
        var result = new AiValidationResult();
        if (template == null || string.IsNullOrWhiteSpace(template.FlowJson))
            return result;

        AiGeneratedFlowJson? templateFlow;
        try
        {
            templateFlow = JsonSerializer.Deserialize<AiGeneratedFlowJson>(template.FlowJson, _jsonOptions);
        }
        catch (JsonException ex)
        {
            result.AddWarning(
                $"Template {template.Name} cannot be parsed for template gate: {ex.Message}",
                code: "template_parse_failed",
                category: "template",
                relatedFields: ["template.flowJson"],
                repairHint: "Repair the stored template JSON before using strict template-first generation.");
            return result;
        }

        if (templateFlow == null)
            return result;

        ValidateRequiredOperators(generatedFlow, templateFlow, template, result);
        ValidateRequiredConnections(generatedFlow, templateFlow, template, result);
        ValidateRequiredResources(generatedFlow, template, result);

        if (strict)
            ValidateUnexpectedOperatorDrift(generatedFlow, templateFlow, template, result);

        return result;
    }

    private static void ValidateRequiredOperators(
        AiGeneratedFlowJson generatedFlow,
        AiGeneratedFlowJson templateFlow,
        FlowTemplate template,
        AiValidationResult result)
    {
        var generatedCounts = CountOperatorTypes(generatedFlow.Operators);
        var templateCounts = CountOperatorTypes(templateFlow.Operators);

        foreach (var pair in templateCounts)
        {
            generatedCounts.TryGetValue(pair.Key, out var actualCount);
            if (actualCount >= pair.Value)
                continue;

            result.AddError(
                $"Template gate failed for {template.Name}: required operator {pair.Key} is missing.",
                code: "template_required_operator_missing",
                category: "template",
                relatedFields: ["operators"],
                operatorId: pair.Key,
                repairHint: $"Restore the required template operator {pair.Key}.");
        }
    }

    private static void ValidateRequiredConnections(
        AiGeneratedFlowJson generatedFlow,
        AiGeneratedFlowJson templateFlow,
        FlowTemplate template,
        AiValidationResult result)
    {
        var generatedEdges = BuildOperatorTypeEdges(generatedFlow);
        foreach (var edge in BuildOperatorTypeEdges(templateFlow))
        {
            if (generatedEdges.Contains(edge))
                continue;

            result.AddError(
                $"Template gate failed for {template.Name}: required connection {edge} is missing.",
                code: "template_required_connection_missing",
                category: "template",
                relatedFields: ["connections"],
                repairHint: $"Restore the template connection pattern {edge}.");
        }
    }

    private static void ValidateRequiredResources(
        AiGeneratedFlowJson generatedFlow,
        FlowTemplate template,
        AiValidationResult result)
    {
        var resources = template.ScenarioPackage?.RequiredResources ?? new List<string>();
        foreach (var resource in resources.Where(item => !string.IsNullOrWhiteSpace(item)))
        {
            if (!IsResourceMissing(generatedFlow, resource))
                continue;

            result.AddWarning(
                $"Template {template.Name} requires resource {resource}.",
                code: "template_required_resource_missing",
                category: "template",
                relatedFields: ["missingResources", "operators.parameters"],
                parameterName: resource,
                repairHint: $"Prompt the user to provide {resource} before production use.");
        }
    }

    private static void ValidateUnexpectedOperatorDrift(
        AiGeneratedFlowJson generatedFlow,
        AiGeneratedFlowJson templateFlow,
        FlowTemplate template,
        AiValidationResult result)
    {
        var templateTypes = templateFlow.Operators
            .Select(op => op.OperatorType)
            .Where(type => !string.IsNullOrWhiteSpace(type))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var unexpected = generatedFlow.Operators
            .Select(op => op.OperatorType)
            .Where(type => !string.IsNullOrWhiteSpace(type) && !templateTypes.Contains(type))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (unexpected.Count == 0)
            return;

        result.AddWarning(
            $"Template {template.Name} generated additional operators outside the locked skeleton: {string.Join(", ", unexpected)}.",
            code: "template_unexpected_operator",
            category: "template",
            relatedFields: ["operators"],
            repairHint: "Keep template_fill changes to parameters, explanations, pending parameters, and missing resources unless the user explicitly allows topology adaptation.");
    }

    private static Dictionary<string, int> CountOperatorTypes(IEnumerable<AiGeneratedOperator>? operators)
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var type in (operators ?? Enumerable.Empty<AiGeneratedOperator>())
                     .Select(op => op.OperatorType)
                     .Where(type => !string.IsNullOrWhiteSpace(type)))
        {
            counts[type] = counts.TryGetValue(type, out var count) ? count + 1 : 1;
        }

        return counts;
    }

    private static HashSet<string> BuildOperatorTypeEdges(AiGeneratedFlowJson flow)
    {
        var typesById = (flow.Operators ?? new List<AiGeneratedOperator>())
            .Where(op => !string.IsNullOrWhiteSpace(op.TempId))
            .ToDictionary(op => op.TempId, op => op.OperatorType, StringComparer.OrdinalIgnoreCase);

        var edges = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var conn in flow.Connections ?? new List<AiGeneratedConnection>())
        {
            if (!typesById.TryGetValue(conn.SourceTempId, out var sourceType) ||
                !typesById.TryGetValue(conn.TargetTempId, out var targetType))
            {
                continue;
            }

            edges.Add($"{sourceType}.{conn.SourcePortName}->{targetType}.{conn.TargetPortName}");
        }

        return edges;
    }

    private static bool IsResourceMissing(AiGeneratedFlowJson generatedFlow, string resourceKey)
    {
        if (resourceKey.Equals("DeepLearning.ModelPath", StringComparison.OrdinalIgnoreCase))
        {
            return (generatedFlow.Operators ?? new List<AiGeneratedOperator>())
                .Where(op => op.OperatorType.Equals("DeepLearning", StringComparison.OrdinalIgnoreCase))
                .Any(op => IsMissingParameter(op.Parameters, "ModelPath", "ModelId"));
        }

        return generatedFlow.MissingResources.Any(item =>
            string.Equals(item.ResourceKey, resourceKey, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsMissingParameter(IReadOnlyDictionary<string, string>? parameters, params string[] keys)
    {
        if (parameters == null)
            return true;

        foreach (var key in keys)
        {
            if (!parameters.TryGetValue(key, out var value))
                continue;

            if (string.IsNullOrWhiteSpace(value))
                continue;

            var normalized = value.Trim();
            if (!normalized.Equals("todo", StringComparison.OrdinalIgnoreCase) &&
                !normalized.Equals("tbd", StringComparison.OrdinalIgnoreCase) &&
                !normalized.Contains("placeholder", StringComparison.OrdinalIgnoreCase) &&
                !normalized.Contains("your_", StringComparison.OrdinalIgnoreCase) &&
                !normalized.Contains("to_be_filled", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }
}
