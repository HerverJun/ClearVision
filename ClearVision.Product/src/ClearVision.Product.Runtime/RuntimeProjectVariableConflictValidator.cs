using ClearVision.Product.Application.DTOs;
using ClearVision.Product.Core.ProjectVariables;
using ClearVision.Product.Runtime.Abstractions;

namespace ClearVision.Product.Runtime;

internal static class RuntimeProjectVariableConflictValidator
{
    public static IReadOnlyList<string> FindSiteProfileConflicts(
        ProjectGlobalVariableSchema? globalVariables,
        RuntimeParameterSchema? parameterSchema,
        OperatorFlowDto? flow)
    {
        if (globalVariables == null ||
            parameterSchema == null ||
            flow == null ||
            globalVariables.TargetBindings.Count == 0 ||
            parameterSchema.Parameters.Count == 0)
        {
            return [];
        }

        var siteParameters = parameterSchema.Parameters
            .Where(parameter => parameter.SiteTunable)
            .GroupBy(parameter => (parameter.OperatorId, ParameterName: Normalize(parameter.ParameterName)))
            .ToDictionary(group => group.Key, group => group.First());

        if (siteParameters.Count == 0)
        {
            return [];
        }

        var operatorsById = flow.Operators.ToDictionary(op => op.Id);
        var variablesById = globalVariables.Variables.ToDictionary(variable => variable.Id);
        var conflicts = new List<string>();

        foreach (var binding in globalVariables.TargetBindings)
        {
            if (!operatorsById.TryGetValue(binding.OperatorId, out var op))
            {
                continue;
            }

            var parameter = op.Parameters.FirstOrDefault(item => item.Id == binding.ParameterId);
            var parameterName = parameter?.Name ?? binding.ParameterName;
            if (string.IsNullOrWhiteSpace(parameterName))
            {
                continue;
            }

            if (!siteParameters.TryGetValue((binding.OperatorId, Normalize(parameterName)), out var siteParameter))
            {
                continue;
            }

            var variableName = variablesById.TryGetValue(binding.VariableId, out var variable)
                ? variable.Name
                : binding.VariableId.ToString("D");
            conflicts.Add(
                $"GV016: parameter '{op.Name}.{parameterName}' is both SiteProfile-tunable ({siteParameter.Id}) and bound to project global variable '{variableName}'.");
        }

        return conflicts;
    }

    public static void ThrowIfAnySiteProfileConflicts(
        ProjectGlobalVariableSchema? globalVariables,
        RuntimeParameterSchema? parameterSchema,
        OperatorFlowDto? flow)
    {
        var conflicts = FindSiteProfileConflicts(globalVariables, parameterSchema, flow);
        if (conflicts.Count > 0)
        {
            throw new RuntimePackageException(string.Join(Environment.NewLine, conflicts));
        }
    }

    private static string Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToLowerInvariant();
    }
}
