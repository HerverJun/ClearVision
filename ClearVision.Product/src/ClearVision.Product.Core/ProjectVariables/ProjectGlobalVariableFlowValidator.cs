using System.Text.Json;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.ValueObjects;

namespace ClearVision.Product.Core.ProjectVariables;

public static class ProjectGlobalVariableFlowValidator
{
    public static IReadOnlyList<ProjectGlobalVariableDiagnostic> Validate(
        ProjectGlobalVariableSchema? schema,
        OperatorFlow? flow)
    {
        var diagnostics = new List<ProjectGlobalVariableDiagnostic>();
        if (schema == null || flow == null)
        {
            return diagnostics;
        }

        var variablesById = schema.Variables
            .Where(variable => variable.Id != Guid.Empty)
            .GroupBy(variable => variable.Id)
            .ToDictionary(group => group.Key, group => group.First());
        var variablesByName = schema.Variables
            .Where(variable => !string.IsNullOrWhiteSpace(variable.Name))
            .GroupBy(variable => variable.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        foreach (var reference in BuildReferenceIndex(flow).References)
        {
            ValidateOperatorReference(reference, variablesById, variablesByName, diagnostics);
        }

        if (!TryBuildExecutionOrder(flow, schema, flow.GetExecutionOrder().ToList(), out _, out var cycle))
        {
            diagnostics.Add(new ProjectGlobalVariableDiagnostic(
                "GV024",
                cycle ?? "Project global variable bindings create an implicit execution cycle."));
        }

        return diagnostics;
    }

    public static ProjectVariableReferenceIndex BuildReferenceIndex(OperatorFlow flow)
    {
        ArgumentNullException.ThrowIfNull(flow);
        var references = new List<ProjectVariableOperatorReference>();
        foreach (var op in flow.Operators)
        {
            if (op.Type is not (OperatorType.VariableRead or OperatorType.VariableWrite or OperatorType.VariableIncrement))
            {
                continue;
            }

            if (!string.Equals(GetParameterText(op, "Scope"), "Project", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            references.Add(new ProjectVariableOperatorReference(
                op.Id,
                op.Name,
                op.Type,
                TryGetParameterId(op, "VariableId"),
                GetParameterText(op, "VariableId"),
                TryGetParameterId(op, "VariableName"),
                GetParameterText(op, "VariableName"),
                TryGetParameterId(op, "DataType"),
                GetParameterText(op, "DataType"),
                TryGetParameterId(op, "ConversionMode"),
                GetParameterText(op, "ConversionMode"),
                TryGetParameterId(op, "Expression"),
                GetParameterText(op, "Expression")));
        }

        return new ProjectVariableReferenceIndex(references);
    }

    public static bool TryBuildExecutionOrder(
        OperatorFlow flow,
        ProjectGlobalVariableSchema? schema,
        IReadOnlyList<Operator> baseOrder,
        out List<Operator> ordered,
        out string? diagnosticChain)
    {
        ArgumentNullException.ThrowIfNull(flow);
        ArgumentNullException.ThrowIfNull(baseOrder);

        ordered = [];
        diagnosticChain = null;
        schema ??= new ProjectGlobalVariableSchema();

        var orderByOperatorId = baseOrder
            .Select((op, index) => (op.Id, Index: index))
            .ToDictionary(item => item.Id, item => item.Index);
        var operatorsById = baseOrder.ToDictionary(op => op.Id);
        var edgesBySource = baseOrder.ToDictionary(op => op.Id, _ => new List<ProjectVariableFlowEdge>());
        var indegree = baseOrder.ToDictionary(op => op.Id, _ => 0);

        void AddEdge(ProjectVariableFlowEdge edge)
        {
            if (!operatorsById.ContainsKey(edge.SourceOperatorId) ||
                !operatorsById.ContainsKey(edge.TargetOperatorId) ||
                edge.SourceOperatorId == edge.TargetOperatorId)
            {
                return;
            }

            var existing = edgesBySource[edge.SourceOperatorId].Any(item => item.TargetOperatorId == edge.TargetOperatorId);
            if (existing)
            {
                return;
            }

            edgesBySource[edge.SourceOperatorId].Add(edge);
            indegree[edge.TargetOperatorId]++;
        }

        foreach (var connection in flow.Connections)
        {
            AddEdge(new ProjectVariableFlowEdge(
                connection.SourceOperatorId,
                connection.TargetOperatorId,
                null,
                ProjectVariableFlowEdgeKind.Canvas));
        }

        var variablesById = schema.Variables.ToDictionary(variable => variable.Id);
        var sourceByVariableId = schema.SourceBindings
            .GroupBy(binding => binding.VariableId)
            .ToDictionary(group => group.Key, group => group.First());
        foreach (var target in schema.TargetBindings)
        {
            if (sourceByVariableId.TryGetValue(target.VariableId, out var source))
            {
                variablesById.TryGetValue(target.VariableId, out var variable);
                AddEdge(new ProjectVariableFlowEdge(
                    source.OperatorId,
                    target.OperatorId,
                    variable?.Name ?? target.VariableId.ToString("D"),
                    ProjectVariableFlowEdgeKind.GlobalVariable));
            }
        }

        var ready = new SortedSet<Guid>(Comparer<Guid>.Create((left, right) =>
        {
            var byIndex = orderByOperatorId[left].CompareTo(orderByOperatorId[right]);
            return byIndex != 0 ? byIndex : left.CompareTo(right);
        }));

        foreach (var (operatorId, count) in indegree)
        {
            if (count == 0)
            {
                ready.Add(operatorId);
            }
        }

        while (ready.Count > 0)
        {
            var operatorId = ready.Min;
            ready.Remove(operatorId);
            ordered.Add(operatorsById[operatorId]);

            foreach (var edge in edgesBySource[operatorId])
            {
                indegree[edge.TargetOperatorId]--;
                if (indegree[edge.TargetOperatorId] == 0)
                {
                    ready.Add(edge.TargetOperatorId);
                }
            }
        }

        if (ordered.Count == baseOrder.Count)
        {
            return true;
        }

        diagnosticChain = BuildCycleDiagnostic(operatorsById, edgesBySource);
        ordered = baseOrder.ToList();
        return false;
    }

    private static void ValidateOperatorReference(
        ProjectVariableOperatorReference reference,
        IReadOnlyDictionary<Guid, ProjectGlobalVariableDefinition> variablesById,
        IReadOnlyDictionary<string, ProjectGlobalVariableDefinition> variablesByName,
        List<ProjectGlobalVariableDiagnostic> diagnostics)
    {
        ProjectGlobalVariableDefinition? byId = null;
        ProjectGlobalVariableDefinition? byName = null;
        var hasId = Guid.TryParse(reference.VariableIdText, out var variableId);
        if (hasId)
        {
            variablesById.TryGetValue(variableId, out byId);
        }

        if (!string.IsNullOrWhiteSpace(reference.VariableName))
        {
            variablesByName.TryGetValue(reference.VariableName, out byName);
        }

        if (hasId && byId == null)
        {
            diagnostics.Add(new ProjectGlobalVariableDiagnostic(
                "GV008",
                $"{reference.OperatorType} references missing project global variable Id '{reference.VariableIdText}'.",
                variableId,
                reference.OperatorId,
                ParameterId: reference.VariableIdParameterId));
        }

        if (!string.IsNullOrWhiteSpace(reference.VariableName) && byName == null)
        {
            diagnostics.Add(new ProjectGlobalVariableDiagnostic(
                "GV008",
                $"{reference.OperatorType} references missing project global variable name '{reference.VariableName}'.",
                null,
                reference.OperatorId,
                ParameterId: reference.VariableNameParameterId));
        }

        if (byId != null && byName != null && byId.Id != byName.Id)
        {
            diagnostics.Add(new ProjectGlobalVariableDiagnostic(
                "GV026",
                $"{reference.OperatorType} VariableId points to '{byId.Name}' but VariableName points to '{byName.Name}'.",
                byId.Id,
                reference.OperatorId,
                ParameterId: reference.VariableIdParameterId));
            return;
        }

        var variable = byId ?? byName;
        if (variable == null)
        {
            return;
        }

        if (reference.OperatorType == OperatorType.VariableIncrement &&
            variable.ValueType != ProjectGlobalVariableValueType.Int64)
        {
            diagnostics.Add(new ProjectGlobalVariableDiagnostic(
                "GV027",
                $"VariableIncrement can only reference Int64 variables; '{variable.Name}' is {variable.ValueType}.",
                variable.Id,
                reference.OperatorId));
        }

        if (reference.OperatorType is OperatorType.VariableRead or OperatorType.VariableWrite &&
            !string.IsNullOrWhiteSpace(reference.DataType) &&
            !ProjectVariableValueTransform.IsCompatibleWithVariableOperatorDataType(variable.ValueType, reference.DataType, reference.ConversionMode))
        {
            diagnostics.Add(new ProjectGlobalVariableDiagnostic(
                "GV028",
                $"{reference.OperatorType} DataType '{reference.DataType}' is not compatible with project global variable '{variable.Name}' ({variable.ValueType}).",
                variable.Id,
                reference.OperatorId,
                ParameterId: reference.DataTypeParameterId));
        }

        if (!string.IsNullOrWhiteSpace(reference.Expression))
        {
            var sampleVariables = variablesByName.Values
                .Where(item => !string.IsNullOrWhiteSpace(item.Name))
                .ToDictionary(
                    item => item.Name,
                    item => (object?)(item.ValueType == ProjectGlobalVariableValueType.Boolean ? true : 1.25d),
                    StringComparer.OrdinalIgnoreCase);
            sampleVariables["value"] = 1.25d;
            if (!ProjectVariableExpressionEvaluator.TryEvaluate(reference.Expression, sampleVariables, out _, out var expressionError))
            {
                diagnostics.Add(new ProjectGlobalVariableDiagnostic(
                    "GV033",
                    $"{reference.OperatorType} expression is invalid: {expressionError}",
                    variable.Id,
                    reference.OperatorId,
                    ParameterId: reference.ExpressionParameterId));
            }
        }
    }

    private static string BuildCycleDiagnostic(
        IReadOnlyDictionary<Guid, Operator> operatorsById,
        IReadOnlyDictionary<Guid, List<ProjectVariableFlowEdge>> edgesBySource)
    {
        var visited = new HashSet<Guid>();
        var active = new HashSet<Guid>();
        var stack = new List<Guid>();
        var edgeStack = new List<ProjectVariableFlowEdge>();

        foreach (var operatorId in operatorsById.Keys)
        {
            if (Visit(operatorId, out var cycle))
            {
                return "Project global variable bindings create an execution cycle: " + cycle;
            }
        }

        return "Project global variable bindings create an execution cycle.";

        bool Visit(Guid operatorId, out string cycle)
        {
            cycle = string.Empty;
            if (active.Contains(operatorId))
            {
                var start = stack.IndexOf(operatorId);
                cycle = FormatCycle(stack.Skip(start).ToList(), edgeStack.Skip(start).ToList(), operatorsById);
                return true;
            }

            if (!visited.Add(operatorId))
            {
                return false;
            }

            active.Add(operatorId);
            stack.Add(operatorId);
            foreach (var edge in edgesBySource.TryGetValue(operatorId, out var edges) ? edges : [])
            {
                edgeStack.Add(edge);
                if (Visit(edge.TargetOperatorId, out cycle))
                {
                    return true;
                }

                edgeStack.RemoveAt(edgeStack.Count - 1);
            }

            stack.RemoveAt(stack.Count - 1);
            active.Remove(operatorId);
            return false;
        }
    }

    private static string FormatCycle(
        IReadOnlyList<Guid> operatorIds,
        IReadOnlyList<ProjectVariableFlowEdge> edges,
        IReadOnlyDictionary<Guid, Operator> operatorsById)
    {
        if (operatorIds.Count == 0)
        {
            return string.Empty;
        }

        var parts = new List<string> { OperatorName(operatorIds[0], operatorsById) };
        for (var index = 0; index < edges.Count; index++)
        {
            var edge = edges[index];
            if (edge.Kind == ProjectVariableFlowEdgeKind.GlobalVariable && !string.IsNullOrWhiteSpace(edge.VariableName))
            {
                parts.Add(edge.VariableName!);
            }

            parts.Add(OperatorName(edge.TargetOperatorId, operatorsById));
        }

        return string.Join(" -> ", parts);
    }

    private static string OperatorName(Guid operatorId, IReadOnlyDictionary<Guid, Operator> operatorsById)
    {
        return operatorsById.TryGetValue(operatorId, out var op) && !string.IsNullOrWhiteSpace(op.Name)
            ? op.Name
            : operatorId.ToString("D");
    }

    private static Guid? TryGetParameterId(Operator op, string parameterName)
    {
        return op.Parameters.FirstOrDefault(parameter =>
            parameter.Name.Equals(parameterName, StringComparison.OrdinalIgnoreCase))?.Id;
    }

    private static string? GetParameterText(Operator op, string parameterName)
    {
        var value = op.Parameters
            .FirstOrDefault(parameter => parameter.Name.Equals(parameterName, StringComparison.OrdinalIgnoreCase))
            ?.GetValue();
        return value switch
        {
            null => null,
            JsonElement { ValueKind: JsonValueKind.String } element => element.GetString(),
            JsonElement element => element.GetRawText(),
            _ => value.ToString()
        };
    }
}

public sealed record ProjectVariableReferenceIndex(
    IReadOnlyList<ProjectVariableOperatorReference> References);

public sealed record ProjectVariableOperatorReference(
    Guid OperatorId,
    string OperatorName,
    OperatorType OperatorType,
    Guid? VariableIdParameterId,
    string? VariableIdText,
    Guid? VariableNameParameterId,
    string? VariableName,
    Guid? DataTypeParameterId,
    string? DataType,
    Guid? ConversionModeParameterId,
    string? ConversionModeText,
    Guid? ExpressionParameterId,
    string? Expression)
{
    public ProjectVariableConversionMode ConversionMode =>
        Enum.TryParse<ProjectVariableConversionMode>(ConversionModeText, ignoreCase: true, out var mode)
            ? mode
            : ProjectVariableConversionMode.Exact;
}

public sealed record ProjectVariableFlowEdge(
    Guid SourceOperatorId,
    Guid TargetOperatorId,
    string? VariableName,
    ProjectVariableFlowEdgeKind Kind);

public enum ProjectVariableFlowEdgeKind
{
    Canvas = 0,
    GlobalVariable = 1
}
