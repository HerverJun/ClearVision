using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.ResultPaths;

namespace ClearVision.Product.Core.ProjectVariables;

public static partial class ProjectGlobalVariableSchemaValidator
{
    public static IReadOnlyList<ProjectGlobalVariableDiagnostic> Validate(
        ProjectGlobalVariableSchema? schema,
        OperatorFlow? flow = null)
    {
        var diagnostics = new List<ProjectGlobalVariableDiagnostic>();
        schema ??= new ProjectGlobalVariableSchema();

        if (!string.Equals(schema.SchemaVersion, "1.0", StringComparison.Ordinal))
        {
            diagnostics.Add(new ProjectGlobalVariableDiagnostic("GV001", $"Unsupported global variable schema version '{schema.SchemaVersion}'."));
        }

        var variableIds = new HashSet<Guid>();
        var variableNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var variable in schema.Variables)
        {
            ValidateVariable(variable, variableIds, variableNames, diagnostics);
        }
        var variablesById = schema.Variables
            .Where(variable => variable.Id != Guid.Empty)
            .GroupBy(variable => variable.Id)
            .ToDictionary(group => group.Key, group => group.First());

        foreach (var group in schema.SourceBindings.GroupBy(binding => binding.VariableId))
        {
            if (group.Count() > 1)
            {
                diagnostics.Add(new ProjectGlobalVariableDiagnostic("GV006", "A project global variable can have at most one automatic source binding.", group.Key));
            }
        }

        foreach (var group in schema.TargetBindings.GroupBy(binding => (binding.OperatorId, binding.ParameterId)))
        {
            if (group.Count() > 1)
            {
                diagnostics.Add(new ProjectGlobalVariableDiagnostic("GV007", "An operator parameter can subscribe to at most one project global variable.", null, group.Key.OperatorId, ParameterId: group.Key.ParameterId));
            }
        }

        var operatorsById = flow?.Operators
            .Where(op => op.Id != Guid.Empty)
            .GroupBy(op => op.Id)
            .ToDictionary(group => group.Key, group => group.First()) ?? [];
        foreach (var binding in schema.SourceBindings)
        {
            ValidateSourceBinding(binding, variablesById, operatorsById, diagnostics);
        }

        foreach (var binding in schema.TargetBindings)
        {
            ValidateTargetBinding(binding, variablesById, operatorsById, diagnostics);
        }

        diagnostics.AddRange(ProjectGlobalVariableFlowValidator.Validate(schema, flow));

        return diagnostics;
    }

    public static void ThrowIfInvalid(ProjectGlobalVariableSchema? schema, OperatorFlow? flow = null)
    {
        var diagnostics = Validate(schema, flow)
            .Where(d => d.Severity == ProjectGlobalVariableDiagnosticSeverity.Error)
            .ToList();
        if (diagnostics.Count > 0)
        {
            throw new ProjectGlobalVariableSchemaValidationException(diagnostics);
        }
    }

    public static string ComputeSchemaHash(ProjectGlobalVariableSchema? schema)
    {
        schema ??= new ProjectGlobalVariableSchema();
        var json = ProjectVariableValueConverter.ToStableJson(schema);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return "sha256:" + Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static void ValidateVariable(
        ProjectGlobalVariableDefinition variable,
        HashSet<Guid> variableIds,
        HashSet<string> variableNames,
        List<ProjectGlobalVariableDiagnostic> diagnostics)
    {
        if (variable.Id == Guid.Empty)
        {
            diagnostics.Add(new ProjectGlobalVariableDiagnostic("GV002", "Project global variable Id is required."));
        }
        else if (!variableIds.Add(variable.Id))
        {
            diagnostics.Add(new ProjectGlobalVariableDiagnostic("GV003", "Project global variable Id must be unique.", variable.Id));
        }

        if (string.IsNullOrWhiteSpace(variable.Name) || !VariableNamePattern().IsMatch(variable.Name))
        {
            diagnostics.Add(new ProjectGlobalVariableDiagnostic("GV013", $"Project global variable name '{variable.Name}' is invalid.", variable.Id));
        }
        else if (!variableNames.Add(variable.Name))
        {
            diagnostics.Add(new ProjectGlobalVariableDiagnostic("GV004", $"Project global variable name '{variable.Name}' is duplicated.", variable.Id));
        }

        if (!ProjectVariableValueConverter.TryConvertToVariableValue(variable.InitialValue, variable.ValueType, out var converted, out var error))
        {
            diagnostics.Add(new ProjectGlobalVariableDiagnostic("GV005", $"Initial value is not compatible with {variable.ValueType}: {error}", variable.Id));
            return;
        }

        if (variable.ValueType == ProjectGlobalVariableValueType.Int64)
        {
            long? min = null;
            long? max = null;
            if (variable.MinBound.HasValue)
            {
                if (variable.MinBound.Value.TryGetInt64(out var parsed))
                {
                    min = parsed;
                }
                else
                {
                    diagnostics.Add(new ProjectGlobalVariableDiagnostic("GV018", "Numeric minimum must be a valid Int64.", variable.Id));
                }
            }

            if (variable.MaxBound.HasValue)
            {
                if (variable.MaxBound.Value.TryGetInt64(out var parsed))
                {
                    max = parsed;
                }
                else
                {
                    diagnostics.Add(new ProjectGlobalVariableDiagnostic("GV019", "Numeric maximum must be a valid Int64.", variable.Id));
                }
            }

            if (min.HasValue && max.HasValue && min.Value > max.Value)
            {
                diagnostics.Add(new ProjectGlobalVariableDiagnostic("GV021", "Numeric minimum cannot be greater than maximum.", variable.Id));
            }

            var numeric = converted.GetInt64();

            if ((min.HasValue && numeric < min.Value) ||
                (max.HasValue && numeric > max.Value))
            {
                diagnostics.Add(new ProjectGlobalVariableDiagnostic("GV014", "Initial value is outside the configured numeric range.", variable.Id));
            }
        }
        else if (variable.ValueType == ProjectGlobalVariableValueType.Double)
        {
            double? min = null;
            double? max = null;
            if (variable.MinBound.HasValue)
            {
                if (variable.MinBound.Value.TryGetDouble(out var parsed))
                {
                    min = parsed;
                }
                else
                {
                    diagnostics.Add(new ProjectGlobalVariableDiagnostic("GV018", "Numeric minimum must be finite.", variable.Id));
                }
            }

            if (variable.MaxBound.HasValue)
            {
                if (variable.MaxBound.Value.TryGetDouble(out var parsed))
                {
                    max = parsed;
                }
                else
                {
                    diagnostics.Add(new ProjectGlobalVariableDiagnostic("GV019", "Numeric maximum must be finite.", variable.Id));
                }
            }

            if (min.HasValue && max.HasValue && min.Value > max.Value)
            {
                diagnostics.Add(new ProjectGlobalVariableDiagnostic("GV021", "Numeric minimum cannot be greater than maximum.", variable.Id));
            }

            var numeric = converted.GetDouble();

            if ((min.HasValue && numeric < min.Value) ||
                (max.HasValue && numeric > max.Value))
            {
                diagnostics.Add(new ProjectGlobalVariableDiagnostic("GV014", "Initial value is outside the configured numeric range.", variable.Id));
            }
        }

        if (variable.ValueType == ProjectGlobalVariableValueType.String &&
            converted.ValueKind == System.Text.Json.JsonValueKind.String &&
            (converted.GetString()?.Length ?? 0) > ProjectVariableValueConverter.MaxStringLength)
        {
            diagnostics.Add(new ProjectGlobalVariableDiagnostic("GV015", "String initial value is too long.", variable.Id));
        }
    }

    private static void ValidateSourceBinding(
        ProjectGlobalVariableSourceBinding binding,
        IReadOnlyDictionary<Guid, ProjectGlobalVariableDefinition> variablesById,
        IReadOnlyDictionary<Guid, Operator> operatorsById,
        List<ProjectGlobalVariableDiagnostic> diagnostics)
    {
        if (!variablesById.TryGetValue(binding.VariableId, out var variable))
        {
            diagnostics.Add(new ProjectGlobalVariableDiagnostic("GV008", "Source binding references a missing project global variable.", binding.VariableId, binding.OperatorId, binding.OutputPortId));
        }

        ValidateSourceBindingResultPath(binding, diagnostics);

        if (operatorsById.Count == 0)
        {
            return;
        }

        if (!operatorsById.TryGetValue(binding.OperatorId, out var op))
        {
            diagnostics.Add(new ProjectGlobalVariableDiagnostic("GV009", "Source binding references a missing operator.", binding.VariableId, binding.OperatorId, binding.OutputPortId));
            return;
        }

        if (!op.IsEnabled)
        {
            diagnostics.Add(new ProjectGlobalVariableDiagnostic(
                "GV020",
                "Source binding references a disabled operator.",
                binding.VariableId,
                binding.OperatorId,
                binding.OutputPortId,
                Severity: ProjectGlobalVariableDiagnosticSeverity.Warning));
        }

        var outputPort = op.OutputPorts.FirstOrDefault(port => port.Id == binding.OutputPortId);
        if (outputPort == null)
        {
            diagnostics.Add(new ProjectGlobalVariableDiagnostic("GV010", "Source binding references a missing output port.", binding.VariableId, binding.OperatorId, binding.OutputPortId));
            return;
        }

        if (variable != null &&
            ShouldValidateSourceBindingRootType(binding, outputPort.DataType) &&
            !ProjectVariableValueTransform.IsCompatibleWithOutputPort(variable.ValueType, outputPort.DataType, binding.ConversionMode))
        {
            diagnostics.Add(new ProjectGlobalVariableDiagnostic(
                "GV017",
                $"Source output type '{outputPort.DataType}' cannot be stored in project global variable '{variable.Name}' ({variable.ValueType}).",
                binding.VariableId,
                binding.OperatorId,
                binding.OutputPortId));
        }

        ValidateExpression(binding.Expression, variablesById.Values, binding.VariableId, binding.OperatorId, binding.OutputPortId, null, diagnostics);
    }

    private static bool ShouldValidateSourceBindingRootType(ProjectGlobalVariableSourceBinding binding, PortDataType outputPortType)
    {
        if (IsResourceRootPort(outputPortType))
        {
            return true;
        }

        if (!binding.ResultPathVersion.HasValue && binding.ResultPath == null)
        {
            return true;
        }

        return string.Equals(binding.ResultPath, ResultPathV1.Root, StringComparison.Ordinal);
    }

    private static bool IsResourceRootPort(PortDataType outputPortType) =>
        outputPortType is PortDataType.Image;

    private static void ValidateSourceBindingResultPath(
        ProjectGlobalVariableSourceBinding binding,
        List<ProjectGlobalVariableDiagnostic> diagnostics)
    {
        var hasVersion = binding.ResultPathVersion.HasValue;
        var hasPath = binding.ResultPath != null;
        if (!hasVersion && !hasPath)
        {
            return;
        }

        if (!hasVersion || !hasPath)
        {
            diagnostics.Add(new ProjectGlobalVariableDiagnostic(
                "RP101",
                "Source binding ResultPathVersion and ResultPath must be provided together.",
                binding.VariableId,
                binding.OperatorId,
                binding.OutputPortId));
            return;
        }

        var parsed = ResultPathParser.Parse(binding.ResultPathVersion!.Value, binding.ResultPath);
        if (!parsed.Succeeded)
        {
            var diagnostic = parsed.Diagnostic!;
            diagnostics.Add(new ProjectGlobalVariableDiagnostic(
                diagnostic.Code,
                $"Source binding ResultPath is invalid: {diagnostic.Message}",
                binding.VariableId,
                binding.OperatorId,
                binding.OutputPortId));
            return;
        }

        if (parsed.Path!.Segments.Any(segment => segment.Kind == ResultPathSegmentKind.Index))
        {
            diagnostics.Add(new ProjectGlobalVariableDiagnostic(
                "RP122",
                "Source binding ResultPath index segments are not supported for project global variables.",
                binding.VariableId,
                binding.OperatorId,
                binding.OutputPortId));
            return;
        }

        if (!string.Equals(parsed.Path.CanonicalPath, binding.ResultPath, StringComparison.Ordinal))
        {
            diagnostics.Add(new ProjectGlobalVariableDiagnostic(
                "RP107",
                "Source binding ResultPath must be canonical.",
                binding.VariableId,
                binding.OperatorId,
                binding.OutputPortId));
        }
    }

    private static void ValidateTargetBinding(
        ProjectGlobalVariableTargetBinding binding,
        IReadOnlyDictionary<Guid, ProjectGlobalVariableDefinition> variablesById,
        IReadOnlyDictionary<Guid, Operator> operatorsById,
        List<ProjectGlobalVariableDiagnostic> diagnostics)
    {
        if (!variablesById.TryGetValue(binding.VariableId, out var variable))
        {
            diagnostics.Add(new ProjectGlobalVariableDiagnostic("GV008", "Target binding references a missing project global variable.", binding.VariableId, binding.OperatorId, ParameterId: binding.ParameterId));
        }

        if (operatorsById.Count == 0)
        {
            return;
        }

        if (!operatorsById.TryGetValue(binding.OperatorId, out var op))
        {
            diagnostics.Add(new ProjectGlobalVariableDiagnostic("GV009", "Target binding references a missing operator.", binding.VariableId, binding.OperatorId, ParameterId: binding.ParameterId));
            return;
        }

        if (!op.IsEnabled)
        {
            diagnostics.Add(new ProjectGlobalVariableDiagnostic(
                "GV020",
                "Target binding references a disabled operator.",
                binding.VariableId,
                binding.OperatorId,
                ParameterId: binding.ParameterId,
                Severity: ProjectGlobalVariableDiagnosticSeverity.Warning));
        }

        var parameter = op.Parameters.FirstOrDefault(parameter => parameter.Id == binding.ParameterId);
        if (parameter == null)
        {
            diagnostics.Add(new ProjectGlobalVariableDiagnostic("GV011", "Target binding references a missing parameter.", binding.VariableId, binding.OperatorId, ParameterId: binding.ParameterId));
            return;
        }

        if (variable != null &&
            !ProjectVariableValueTransform.IsCompatibleWithParameter(variable.ValueType, parameter.DataType, binding.ConversionMode))
        {
            diagnostics.Add(new ProjectGlobalVariableDiagnostic(
                "GV022",
                $"Project global variable '{variable.Name}' ({variable.ValueType}) is not compatible with target parameter '{parameter.Name}' ({parameter.DataType}).",
                binding.VariableId,
                binding.OperatorId,
                ParameterId: binding.ParameterId));
        }

        ValidateExpression(binding.Expression, variablesById.Values, binding.VariableId, binding.OperatorId, null, binding.ParameterId, diagnostics);
    }

    private static void ValidateExpression(
        string? expression,
        IEnumerable<ProjectGlobalVariableDefinition> variables,
        Guid? variableId,
        Guid? operatorId,
        Guid? portId,
        Guid? parameterId,
        List<ProjectGlobalVariableDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            return;
        }

        var knownVariables = variables
            .Select(variable => variable.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Append("value");
        if (!ProjectVariableExpressionEvaluator.TryCompile(expression, knownVariables!, out var error))
        {
            diagnostics.Add(new ProjectGlobalVariableDiagnostic(
                "GV033",
                $"Project global variable expression is invalid: {error}",
                variableId,
                operatorId,
                portId,
                parameterId));
        }
    }

    [GeneratedRegex("^[a-zA-Z][a-zA-Z0-9_]*(\\.[a-zA-Z][a-zA-Z0-9_]*)*$", RegexOptions.CultureInvariant)]
    private static partial Regex VariableNamePattern();
}

public sealed class ProjectVariableBindingIndex
{
    private readonly Dictionary<Guid, List<ProjectGlobalVariableTargetBinding>> _targetBindingsByOperatorId;
    private readonly Dictionary<Guid, List<ProjectGlobalVariableSourceBinding>> _sourceBindingsByOperatorId;

    private ProjectVariableBindingIndex(
        Dictionary<Guid, List<ProjectGlobalVariableTargetBinding>> targetBindingsByOperatorId,
        Dictionary<Guid, List<ProjectGlobalVariableSourceBinding>> sourceBindingsByOperatorId)
    {
        _targetBindingsByOperatorId = targetBindingsByOperatorId;
        _sourceBindingsByOperatorId = sourceBindingsByOperatorId;
    }

    public bool HasBindings => _targetBindingsByOperatorId.Count > 0 || _sourceBindingsByOperatorId.Count > 0;

    public static ProjectVariableBindingIndex Build(ProjectGlobalVariableSchema? schema)
    {
        schema ??= new ProjectGlobalVariableSchema();
        return new ProjectVariableBindingIndex(
            schema.TargetBindings.GroupBy(binding => binding.OperatorId).ToDictionary(group => group.Key, group => group.ToList()),
            schema.SourceBindings.GroupBy(binding => binding.OperatorId).ToDictionary(group => group.Key, group => group.ToList()));
    }

    public IReadOnlyList<ProjectGlobalVariableTargetBinding> GetTargets(Guid operatorId)
    {
        return _targetBindingsByOperatorId.TryGetValue(operatorId, out var bindings) ? bindings : [];
    }

    public IReadOnlyList<ProjectGlobalVariableSourceBinding> GetSources(Guid operatorId)
    {
        return _sourceBindingsByOperatorId.TryGetValue(operatorId, out var bindings) ? bindings : [];
    }

    public IEnumerable<(Guid SourceOperatorId, Guid TargetOperatorId, Guid VariableId)> GetImplicitEdges(ProjectGlobalVariableSchema schema)
    {
        var sourceByVariableId = schema.SourceBindings
            .Where(binding => binding.VariableId != Guid.Empty)
            .GroupBy(binding => binding.VariableId)
            .ToDictionary(group => group.Key, group => group.First());
        foreach (var target in schema.TargetBindings)
        {
            if (sourceByVariableId.TryGetValue(target.VariableId, out var source) &&
                source.OperatorId != target.OperatorId)
            {
                yield return (source.OperatorId, target.OperatorId, target.VariableId);
            }
        }
    }
}
