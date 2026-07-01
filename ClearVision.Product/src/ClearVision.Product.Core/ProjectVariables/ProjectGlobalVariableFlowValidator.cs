using System.Text.Json;
using System.Text.RegularExpressions;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.ValueObjects;

namespace ClearVision.Product.Core.ProjectVariables;

public static class ProjectGlobalVariableFlowValidator
{
    private static readonly Regex ExpressionIdentifierRegex = new(
        @"[A-Za-z_][A-Za-z0-9_.]*",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

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

    public static bool HasProjectVariableSemantics(OperatorFlow flow, ProjectGlobalVariableSchema? schema)
    {
        ArgumentNullException.ThrowIfNull(flow);
        schema ??= new ProjectGlobalVariableSchema();

        var operatorIds = flow.Operators.Select(op => op.Id).ToHashSet();
        if (schema.SourceBindings.Any(binding => operatorIds.Contains(binding.OperatorId)) ||
            schema.TargetBindings.Any(binding => operatorIds.Contains(binding.OperatorId)))
        {
            return true;
        }

        return BuildReferenceIndex(flow).References.Count > 0;
    }

    public static bool HasProjectVariableWriteCapability(OperatorFlow flow, ProjectGlobalVariableSchema? schema)
    {
        ArgumentNullException.ThrowIfNull(flow);
        schema ??= new ProjectGlobalVariableSchema();

        var operatorIds = flow.Operators.Select(op => op.Id).ToHashSet();
        if (schema.SourceBindings.Any(binding => operatorIds.Contains(binding.OperatorId)))
        {
            return true;
        }

        return BuildReferenceIndex(flow).References.Any(reference =>
            reference.OperatorType is OperatorType.VariableWrite or OperatorType.VariableIncrement);
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
            .GroupBy(item => item.Id)
            .ToDictionary(group => group.Key, group => group.First().Index);
        var operatorsById = baseOrder
            .GroupBy(op => op.Id)
            .ToDictionary(group => group.Key, group => group.First());
        var edgesBySource = operatorsById.ToDictionary(item => item.Key, _ => new List<ProjectVariableFlowEdge>());
        var indegree = operatorsById.ToDictionary(item => item.Key, _ => 0);

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

        foreach (var edge in BuildDependencyEdges(flow, schema, baseOrder))
        {
            AddEdge(edge);
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

    public static IReadOnlyList<ProjectVariableFlowEdge> BuildDependencyEdges(
        OperatorFlow flow,
        ProjectGlobalVariableSchema? schema,
        IReadOnlyList<Operator> baseOrder)
    {
        ArgumentNullException.ThrowIfNull(flow);
        ArgumentNullException.ThrowIfNull(baseOrder);
        schema ??= new ProjectGlobalVariableSchema();

        var orderByOperatorId = baseOrder
            .Select((op, index) => (op.Id, Index: index))
            .GroupBy(item => item.Id)
            .ToDictionary(group => group.Key, group => group.First().Index);
        var operatorIds = orderByOperatorId.Keys.ToHashSet();
        var variablesById = schema.Variables
            .Where(variable => variable.Id != Guid.Empty)
            .GroupBy(variable => variable.Id)
            .ToDictionary(group => group.Key, group => group.First());
        var variablesByName = schema.Variables
            .Where(variable => !string.IsNullOrWhiteSpace(variable.Name))
            .GroupBy(variable => variable.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var readersByVariableId = new Dictionary<Guid, HashSet<Guid>>();
        var writersByVariableId = new Dictionary<Guid, HashSet<Guid>>();
        var edges = new List<ProjectVariableFlowEdge>();
        var seenEdges = new HashSet<(Guid Source, Guid Target, ProjectVariableFlowEdgeKind Kind, string? VariableName)>();

        void AddEdge(Guid sourceOperatorId, Guid targetOperatorId, Guid? variableId, ProjectVariableFlowEdgeKind kind)
        {
            if (sourceOperatorId == targetOperatorId ||
                !operatorIds.Contains(sourceOperatorId) ||
                !operatorIds.Contains(targetOperatorId))
            {
                return;
            }

            var variableName = variableId.HasValue && variablesById.TryGetValue(variableId.Value, out var variable)
                ? variable.Name
                : variableId?.ToString("D");
            if (!seenEdges.Add((sourceOperatorId, targetOperatorId, kind, variableName)))
            {
                return;
            }

            edges.Add(new ProjectVariableFlowEdge(sourceOperatorId, targetOperatorId, variableName, kind));
        }

        void AddAccess(Dictionary<Guid, HashSet<Guid>> accessByVariableId, Guid variableId, Guid operatorId)
        {
            if (!operatorIds.Contains(operatorId))
            {
                return;
            }

            if (!accessByVariableId.TryGetValue(variableId, out var operators))
            {
                operators = [];
                accessByVariableId[variableId] = operators;
            }

            operators.Add(operatorId);
        }

        foreach (var connection in flow.Connections)
        {
            AddEdge(connection.SourceOperatorId, connection.TargetOperatorId, null, ProjectVariableFlowEdgeKind.Canvas);
        }

        foreach (var source in schema.SourceBindings)
        {
            AddAccess(writersByVariableId, source.VariableId, source.OperatorId);
            foreach (var expressionVariableId in ResolveExpressionVariableIds(source.Expression, variablesByName))
            {
                AddAccess(readersByVariableId, expressionVariableId, source.OperatorId);
            }
        }

        foreach (var target in schema.TargetBindings)
        {
            AddAccess(readersByVariableId, target.VariableId, target.OperatorId);
            foreach (var expressionVariableId in ResolveExpressionVariableIds(target.Expression, variablesByName))
            {
                AddAccess(readersByVariableId, expressionVariableId, target.OperatorId);
            }
        }

        foreach (var reference in BuildReferenceIndex(flow).References)
        {
            foreach (var expressionVariableId in ResolveExpressionVariableIds(reference.Expression, variablesByName))
            {
                AddAccess(readersByVariableId, expressionVariableId, reference.OperatorId);
            }

            if (!TryResolveVariableId(reference, variablesById, variablesByName, out var variableId))
            {
                continue;
            }

            switch (reference.OperatorType)
            {
                case OperatorType.VariableRead:
                    AddAccess(readersByVariableId, variableId, reference.OperatorId);
                    break;
                case OperatorType.VariableWrite:
                    AddAccess(writersByVariableId, variableId, reference.OperatorId);
                    break;
                case OperatorType.VariableIncrement:
                    AddAccess(readersByVariableId, variableId, reference.OperatorId);
                    AddAccess(writersByVariableId, variableId, reference.OperatorId);
                    break;
            }
        }

        foreach (var (variableId, writers) in writersByVariableId)
        {
            var orderedWriters = writers
                .OrderBy(operatorId => orderByOperatorId.GetValueOrDefault(operatorId, int.MaxValue))
                .ThenBy(operatorId => operatorId)
                .ToList();
            for (var index = 0; index < orderedWriters.Count - 1; index++)
            {
                AddEdge(orderedWriters[index], orderedWriters[index + 1], variableId, ProjectVariableFlowEdgeKind.GlobalVariable);
            }

            if (!readersByVariableId.TryGetValue(variableId, out var readers))
            {
                continue;
            }

            foreach (var writer in orderedWriters)
            {
                foreach (var reader in readers)
                {
                    AddEdge(writer, reader, variableId, ProjectVariableFlowEdgeKind.GlobalVariable);
                }
            }
        }

        return edges;
    }

    private static IEnumerable<Guid> ResolveExpressionVariableIds(
        string? expression,
        IReadOnlyDictionary<string, ProjectGlobalVariableDefinition> variablesByName)
    {
        if (string.IsNullOrWhiteSpace(expression) || variablesByName.Count == 0)
        {
            yield break;
        }

        var seen = new HashSet<Guid>();
        foreach (Match match in ExpressionIdentifierRegex.Matches(expression))
        {
            if (variablesByName.TryGetValue(match.Value, out var variable) &&
                variable.Id != Guid.Empty &&
                seen.Add(variable.Id))
            {
                yield return variable.Id;
            }
        }
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

        if (!hasId && !string.IsNullOrWhiteSpace(reference.VariableName) && byName == null)
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
            var knownVariables = variablesByName.Values
                .Select(item => item.Name)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Append("value");
            if (!ProjectVariableExpressionEvaluator.TryCompile(reference.Expression, knownVariables!, out var expressionError))
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

    private static bool TryResolveVariableId(
        ProjectVariableOperatorReference reference,
        IReadOnlyDictionary<Guid, ProjectGlobalVariableDefinition> variablesById,
        IReadOnlyDictionary<string, ProjectGlobalVariableDefinition> variablesByName,
        out Guid variableId)
    {
        ProjectGlobalVariableDefinition? byId = null;
        ProjectGlobalVariableDefinition? byName = null;
        var hasId = Guid.TryParse(reference.VariableIdText, out var parsedId);
        if (hasId)
        {
            variablesById.TryGetValue(parsedId, out byId);
        }

        if (!string.IsNullOrWhiteSpace(reference.VariableName))
        {
            variablesByName.TryGetValue(reference.VariableName, out byName);
        }

        var variable = byId ?? byName;
        if (variable == null || (byId != null && byName != null && byId.Id != byName.Id))
        {
            variableId = default;
            return false;
        }

        variableId = variable.Id;
        return true;
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
