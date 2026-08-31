using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ClearVision.Product.Core.Decisions;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.ValueObjects;

namespace ClearVision.Product.Core.Services;

/// <summary>
/// Resolves embedded ForEach graphs once for admission, resource binding and
/// execution.  Sharing this parser prevents a permissive admission parser and
/// a more capable execution parser from observing different child graphs.
/// </summary>
public static class NestedExecutionFlowCatalog
{
    public const int MaximumNestingDepth = 16;
    public const int MaximumOperatorCount = 4096;

    public static bool TryResolveForEachSubGraph(
        Operator @operator,
        out OperatorFlow? subGraph,
        out string code,
        out string message)
    {
        ArgumentNullException.ThrowIfNull(@operator);
        subGraph = null;
        code = string.Empty;
        message = string.Empty;

        if (@operator.Type != OperatorType.ForEach)
        {
            code = "ADMISSION_NESTED_FLOW_OPERATOR_REQUIRED";
            message = "Only a ForEach operator can own an embedded execution graph.";
            return false;
        }

        var parameter = @operator.Parameters.FirstOrDefault(item =>
            string.Equals(item.Name, "SubGraph", StringComparison.OrdinalIgnoreCase));
        var value = parameter?.GetValue();
        if (value == null)
        {
            code = "ADMISSION_NESTED_FLOW_REQUIRED";
            message = "ForEach execution requires an embedded SubGraph parameter.";
            return false;
        }

        try
        {
            using var document = ParseValue(value);
            if (!TryParseFlow(document.RootElement, out subGraph, out message))
            {
                code = "ADMISSION_NESTED_FLOW_INVALID";
                return false;
            }

            return true;
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or ArgumentException or InvalidOperationException)
        {
            code = "ADMISSION_NESTED_FLOW_INVALID";
            message = $"ForEach SubGraph could not be parsed: {ex.Message}";
            subGraph = null;
            return false;
        }
    }

    /// <summary>Returns the root followed by every enabled nested graph.</summary>
    public static IReadOnlyList<OperatorFlow> EnumerateFlows(OperatorFlow root)
    {
        ArgumentNullException.ThrowIfNull(root);
        var flows = new List<OperatorFlow>();
        var operatorCount = 0;
        Visit(root, depth: 0, flows, ref operatorCount);
        return flows;
    }

    public static IReadOnlyList<Operator> EnumerateEnabledOperators(OperatorFlow root) =>
        EnumerateFlows(root)
            .SelectMany(flow => flow.Operators.Where(item => item.IsEnabled))
            .ToArray();

    private static void Visit(
        OperatorFlow flow,
        int depth,
        ICollection<OperatorFlow> flows,
        ref int operatorCount)
    {
        if (depth > MaximumNestingDepth)
        {
            throw new InvalidOperationException(
                $"ADMISSION_NESTED_FLOW_DEPTH_EXCEEDED: ForEach nesting cannot exceed {MaximumNestingDepth} levels.");
        }

        operatorCount = checked(operatorCount + flow.Operators.Count);
        if (operatorCount > MaximumOperatorCount)
        {
            throw new InvalidOperationException(
                $"ADMISSION_NESTED_FLOW_CAPACITY_EXCEEDED: An execution snapshot cannot contain more than {MaximumOperatorCount} operators including nested graphs.");
        }

        flows.Add(flow);
        foreach (var forEach in flow.Operators.Where(item => item.IsEnabled && item.Type == OperatorType.ForEach))
        {
            if (!TryResolveForEachSubGraph(forEach, out var child, out var code, out var message) || child == null)
            {
                throw new InvalidOperationException($"{code}: {message}");
            }

            Visit(child, depth + 1, flows, ref operatorCount);
        }
    }

    private static JsonDocument ParseValue(object value)
    {
        if (value is string text)
        {
            var first = JsonDocument.Parse(text);
            if (first.RootElement.ValueKind != JsonValueKind.String)
            {
                return first;
            }

            var nested = first.RootElement.GetString();
            first.Dispose();
            return JsonDocument.Parse(nested ?? string.Empty);
        }

        if (value is JsonElement element)
        {
            return JsonDocument.Parse(element.GetRawText());
        }

        return JsonDocument.Parse(JsonSerializer.Serialize(value));
    }

    private static bool TryParseFlow(
        JsonElement root,
        out OperatorFlow? flow,
        out string message)
    {
        flow = null;
        message = string.Empty;
        if (root.ValueKind != JsonValueKind.Object)
        {
            message = "ForEach SubGraph must be a JSON object.";
            return false;
        }

        var flowId = ReadGuid(root, "Id", "id") ?? DeterministicGuid(root.GetRawText());
        var name = ReadString(root, "Name", "name") ?? "SubGraph";
        var parsed = new OperatorFlow(flowId, name);
        if (!TryGet(root, out var operatorsElement, "Operators", "operators", "Nodes", "nodes") ||
            operatorsElement.ValueKind != JsonValueKind.Array)
        {
            message = "ForEach SubGraph must contain an operator array.";
            return false;
        }

        foreach (var operatorElement in operatorsElement.EnumerateArray())
        {
            if (!TryParseOperator(operatorElement, out var @operator, out message) || @operator == null)
            {
                return false;
            }

            parsed.AddOperator(@operator);
        }

        if (TryGet(root, out var connectionsElement, "Connections", "connections", "Edges", "edges"))
        {
            if (connectionsElement.ValueKind != JsonValueKind.Array)
            {
                message = "ForEach SubGraph connections must be an array.";
                return false;
            }

            foreach (var connectionElement in connectionsElement.EnumerateArray())
            {
                var sourceOperatorId = ReadGuid(
                    connectionElement,
                    "SourceOperatorId", "sourceOperatorId", "SourceNodeId", "sourceNodeId");
                var sourcePortId = ReadGuid(connectionElement, "SourcePortId", "sourcePortId");
                var targetOperatorId = ReadGuid(
                    connectionElement,
                    "TargetOperatorId", "targetOperatorId", "TargetNodeId", "targetNodeId");
                var targetPortId = ReadGuid(connectionElement, "TargetPortId", "targetPortId");
                if (sourceOperatorId is null || sourcePortId is null ||
                    targetOperatorId is null || targetPortId is null)
                {
                    message = "A ForEach SubGraph connection has an invalid operator or port id.";
                    return false;
                }

                try
                {
                    parsed.AddConnection(new OperatorConnection(
                        sourceOperatorId.Value,
                        sourcePortId.Value,
                        targetOperatorId.Value,
                        targetPortId.Value));
                }
                catch (InvalidOperationException ex)
                {
                    message = $"ForEach SubGraph connection is invalid: {ex.Message}";
                    return false;
                }
            }
        }

        if (TryGet(root, out var decisionElement, "DecisionConfiguration", "decisionConfiguration") &&
            decisionElement.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined))
        {
            try
            {
                parsed.DecisionConfiguration = JsonSerializer.Deserialize<DecisionConfiguration>(
                    decisionElement.GetRawText());
            }
            catch (JsonException ex)
            {
                message = $"ForEach SubGraph decision configuration is invalid: {ex.Message}";
                return false;
            }
        }

        flow = parsed;
        return true;
    }

    private static bool TryParseOperator(
        JsonElement element,
        out Operator? @operator,
        out string message)
    {
        @operator = null;
        message = string.Empty;
        if (element.ValueKind != JsonValueKind.Object)
        {
            message = "A ForEach SubGraph operator must be a JSON object.";
            return false;
        }

        var id = ReadGuid(element, "Id", "id");
        var name = ReadString(element, "Name", "name", "Title", "title");
        if (id is null || id == Guid.Empty || string.IsNullOrWhiteSpace(name) ||
            !TryReadEnum(element, out OperatorType type, "Type", "type", "OperatorType", "operatorType"))
        {
            message = "A ForEach SubGraph operator has an invalid id, name, or type.";
            return false;
        }

        var (x, y) = ReadPosition(element);
        var parsed = new Operator(id.Value, name, OperatorTypeAliasResolver.Resolve(type), x, y);
        if (!ReadBoolean(element, defaultValue: true, "IsEnabled", "isEnabled", "Enabled", "enabled"))
        {
            parsed.Disable();
        }

        if (!TryParsePorts(element, parsed, input: true, out message) ||
            !TryParsePorts(element, parsed, input: false, out message) ||
            !TryParseParameters(element, parsed, out message))
        {
            return false;
        }

        @operator = parsed;
        return true;
    }

    private static bool TryParsePorts(
        JsonElement element,
        Operator @operator,
        bool input,
        out string message)
    {
        message = string.Empty;
        var names = input
            ? new[] { "InputPorts", "inputPorts", "Inputs", "inputs" }
            : new[] { "OutputPorts", "outputPorts", "Outputs", "outputs" };
        if (!TryGet(element, out var portsElement, names))
        {
            return true;
        }

        if (portsElement.ValueKind != JsonValueKind.Array)
        {
            message = "ForEach SubGraph ports must be arrays.";
            return false;
        }

        foreach (var portElement in portsElement.EnumerateArray())
        {
            var id = ReadGuid(portElement, "Id", "id");
            var name = ReadString(portElement, "Name", "name");
            if (id is null || id == Guid.Empty || string.IsNullOrWhiteSpace(name) ||
                !TryReadEnum(portElement, out PortDataType dataType, "DataType", "dataType", "Type", "type"))
            {
                message = "A ForEach SubGraph port has an invalid id, name, or data type.";
                return false;
            }

            var isRequired = input && ReadBoolean(portElement, defaultValue: true, "IsRequired", "isRequired");
            if (input)
            {
                @operator.LoadInputPort(id.Value, name, dataType, isRequired);
            }
            else
            {
                @operator.LoadOutputPort(id.Value, name, dataType);
            }
        }

        return true;
    }

    private static bool TryParseParameters(
        JsonElement element,
        Operator @operator,
        out string message)
    {
        message = string.Empty;
        if (!TryGet(element, out var parametersElement, "Parameters", "parameters"))
        {
            return true;
        }

        if (parametersElement.ValueKind != JsonValueKind.Array)
        {
            message = "ForEach SubGraph parameters must be an array.";
            return false;
        }

        foreach (var parameterElement in parametersElement.EnumerateArray())
        {
            var name = ReadString(parameterElement, "Name", "name");
            if (string.IsNullOrWhiteSpace(name))
            {
                message = "A ForEach SubGraph parameter is missing its name.";
                return false;
            }

            var id = ReadGuid(parameterElement, "Id", "id") ??
                DeterministicGuid($"{@operator.Id:N}:{name}");
            var displayName = ReadString(parameterElement, "DisplayName", "displayName") ?? name;
            var description = ReadString(parameterElement, "Description", "description") ?? string.Empty;
            var dataType = ReadString(parameterElement, "DataType", "dataType") ?? "object";
            var isRequired = ReadBoolean(parameterElement, defaultValue: false, "IsRequired", "isRequired");
            var defaultValue = ReadParameterValue(
                parameterElement,
                "DefaultValue", "defaultValue", "DefaultValueJson", "defaultValueJson");
            var currentValue = ReadParameterValue(
                parameterElement,
                "Value", "value", "ValueJson", "valueJson");
            var minimum = ReadParameterValue(
                parameterElement,
                "MinValue", "minValue", "MinValueJson", "minValueJson", "Minimum", "minimum");
            var maximum = ReadParameterValue(
                parameterElement,
                "MaxValue", "maxValue", "MaxValueJson", "maxValueJson", "Maximum", "maximum");
            var options = ReadOptions(parameterElement);

            var parameter = new Parameter(
                id,
                name,
                displayName,
                description,
                dataType,
                defaultValue.Value,
                minimum.Value,
                maximum.Value,
                isRequired,
                options);
            if (currentValue.Found)
            {
                parameter.SetValue(currentValue.Value);
            }

            @operator.AddParameter(parameter);
        }

        return true;
    }

    private static List<ParameterOption>? ReadOptions(JsonElement element)
    {
        var optionsValue = ReadParameterValue(element, "Options", "options", "OptionsJson", "optionsJson");
        if (!optionsValue.Found || optionsValue.Value is not object[] values)
        {
            return null;
        }

        var result = new List<ParameterOption>();
        foreach (var value in values.OfType<IReadOnlyDictionary<string, object?>>())
        {
            var label = value.FirstOrDefault(pair =>
                string.Equals(pair.Key, "Label", StringComparison.OrdinalIgnoreCase)).Value?.ToString();
            var optionValue = value.FirstOrDefault(pair =>
                string.Equals(pair.Key, "Value", StringComparison.OrdinalIgnoreCase)).Value?.ToString();
            if (!string.IsNullOrWhiteSpace(label) && optionValue != null)
            {
                result.Add(new ParameterOption { Label = label, Value = optionValue });
            }
        }

        return result.Count == 0 ? null : result;
    }

    private static (bool Found, object? Value) ReadParameterValue(
        JsonElement element,
        params string[] names)
    {
        foreach (var name in names)
        {
            if (!TryGet(element, out var valueElement, name))
            {
                continue;
            }

            if (name.EndsWith("Json", StringComparison.OrdinalIgnoreCase) &&
                valueElement.ValueKind == JsonValueKind.String)
            {
                var json = valueElement.GetString();
                if (string.IsNullOrWhiteSpace(json))
                {
                    return (true, null);
                }

                using var document = JsonDocument.Parse(json);
                return (true, ConvertElement(document.RootElement));
            }

            return (true, ConvertElement(valueElement));
        }

        return (false, null);
    }

    private static object? ConvertElement(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString(),
        JsonValueKind.Number when element.TryGetInt64(out var integer) => integer,
        JsonValueKind.Number when element.TryGetDouble(out var number) => number,
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null or JsonValueKind.Undefined => null,
        JsonValueKind.Array => element.EnumerateArray().Select(ConvertElement).ToArray(),
        JsonValueKind.Object => element.EnumerateObject()
            .ToDictionary(property => property.Name, property => ConvertElement(property.Value), StringComparer.Ordinal),
        _ => element.GetRawText()
    };

    private static (double X, double Y) ReadPosition(JsonElement element)
    {
        if (TryGet(element, out var position, "Position", "position") &&
            position.ValueKind == JsonValueKind.Object)
        {
            return (ReadDouble(position, "X", "x"), ReadDouble(position, "Y", "y"));
        }

        return (ReadDouble(element, "X", "x"), ReadDouble(element, "Y", "y"));
    }

    private static bool TryReadEnum<TEnum>(
        JsonElement element,
        out TEnum value,
        params string[] names)
        where TEnum : struct, Enum
    {
        value = default;
        if (!TryGet(element, out var enumElement, names))
        {
            return false;
        }

        if (enumElement.ValueKind == JsonValueKind.Number && enumElement.TryGetInt32(out var number))
        {
            value = (TEnum)Enum.ToObject(typeof(TEnum), number);
            return Enum.IsDefined(value);
        }

        var text = enumElement.ValueKind == JsonValueKind.String ? enumElement.GetString() : null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        if (Enum.TryParse(text, ignoreCase: true, out value) && Enum.IsDefined(value))
        {
            return true;
        }

        return int.TryParse(text, out number) &&
               Enum.IsDefined(value = (TEnum)Enum.ToObject(typeof(TEnum), number));
    }

    private static Guid? ReadGuid(JsonElement element, params string[] names)
    {
        if (!TryGet(element, out var value, names))
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.String && Guid.TryParse(value.GetString(), out var parsed)
            ? parsed
            : value.ValueKind == JsonValueKind.String && Guid.TryParseExact(value.GetString(), "N", out parsed)
                ? parsed
                : null;
    }

    private static string? ReadString(JsonElement element, params string[] names)
    {
        if (!TryGet(element, out var value, names))
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
    }

    private static bool ReadBoolean(JsonElement element, bool defaultValue, params string[] names)
    {
        if (!TryGet(element, out var value, names))
        {
            return defaultValue;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String when bool.TryParse(value.GetString(), out var parsed) => parsed,
            _ => defaultValue
        };
    }

    private static double ReadDouble(JsonElement element, params string[] names)
    {
        if (!TryGet(element, out var value, names))
        {
            return 0;
        }

        return value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var parsed)
            ? parsed
            : value.ValueKind == JsonValueKind.String && double.TryParse(
                value.GetString(),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out parsed)
                ? parsed
                : 0;
    }

    private static bool TryGet(JsonElement element, out JsonElement value, params string[] names)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (names.Any(name => string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static Guid DeterministicGuid(string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return new Guid(hash.AsSpan(0, 16));
    }
}
