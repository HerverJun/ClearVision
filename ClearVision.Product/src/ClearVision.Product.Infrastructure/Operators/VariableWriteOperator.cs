using System.Globalization;
using System.Reflection;
using System.Text.Json;
using ClearVision.Product.Core.Attributes;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Operators;
using ClearVision.Product.Core.ProjectVariables;
using ClearVision.Product.Core.Services;
using Microsoft.Extensions.Logging;

namespace ClearVision.Product.Infrastructure.Operators;

[OperatorMeta(
    DisplayName = "变量写入",
    Description = "写入单次运行变量或项目全局变量",
    Category = "变量",
    IconName = "variable-write")]
[InputPort("Value", "值", PortDataType.Any, IsRequired = false)]
[OutputPort("VariableName", "变量名", PortDataType.String)]
[OutputPort("Value", "写入的值", PortDataType.Any)]
[OutputPort("CycleCount", "循环计数", PortDataType.Integer)]
[OperatorParam("Scope", "作用域", "enum", DefaultValue = "Run", Options = new[] { "Run|单次运行", "Project|项目全局" })]
[OperatorParam("VariableId", "变量ID", "string", Description = "Project 作用域变量的稳定 ID", DefaultValue = "")]
[OperatorParam("VariableName", "变量名", "string", Description = "要写入的变量名称", DefaultValue = "")]
[OperatorParam("DataType", "数据类型", "enum", DefaultValue = "String", Options = new[] { "String|字符串", "Int|整数", "Double|浮点数", "Bool|布尔值", "Object|对象" })]
[OperatorParam("UseInputValue", "使用输入值", "bool", Description = "优先使用上游输入值，否则使用静态值", DefaultValue = true)]
[OperatorParam("StaticValue", "静态值", "string", Description = "没有上游输入时使用的值", DefaultValue = "0")]
[OutputPort("VariableId", "Variable Id", PortDataType.String)]
[OutputPort("ValueType", "Value Type", PortDataType.String)]
[OutputPort("Version", "Version", PortDataType.Integer)]
[OutputPort("UpdatedAtUtc", "Updated At UTC", PortDataType.String)]
[OutputPort("UpdatedBy", "Updated By", PortDataType.String)]
[OutputPort("WriteSkipped", "Write Skipped", PortDataType.Boolean)]
[OutputPort("SkipReason", "Skip Reason", PortDataType.String)]
[OutputPort("InputStatusValue", "Input Status Value", PortDataType.String)]
[OperatorParam("ConversionMode", "Conversion Mode", "enum", DefaultValue = "Exact", Options = new[] { "Exact|Exact", "Round|Round", "Floor|Floor", "Ceiling|Ceiling", "Truncate|Truncate" })]
[OperatorParam("Expression", "Expression", "string", Description = "Optional controlled expression evaluated before Project variable write. Use value for the raw input.", DefaultValue = "")]
[OperatorParam("InputFieldName", "Input Field Name", "string", Description = "Optional upstream field path such as ParsedFields.score.", DefaultValue = "")]
[OperatorParam("RequireInputStatus", "Require Input Status", "bool", Description = "When enabled, write only if the configured upstream status field is true/OK/PASS/1.", DefaultValue = false)]
[OperatorParam("InputStatusFieldName", "Input Status Field Name", "string", Description = "Optional upstream status field path such as Status or ResponseAccepted.", DefaultValue = "Status")]
[OperatorParam("FailOnInputStatusFalse", "Fail On Input Status False", "bool", Description = "Return failure instead of a skipped write when the upstream status is false or missing.", DefaultValue = false)]
public class VariableWriteOperator : OperatorBase
{
    private readonly IVariableContext _variableContext;
    private readonly IProjectVariableExecutionContextAccessor _projectVariableContextAccessor;

    public override OperatorType OperatorType => OperatorType.VariableWrite;

    public VariableWriteOperator(
        ILogger<VariableWriteOperator> logger,
        IVariableContext variableContext,
        IProjectVariableExecutionContextAccessor? projectVariableContextAccessor = null) : base(logger)
    {
        _variableContext = variableContext;
        _projectVariableContextAccessor = projectVariableContextAccessor ?? new ProjectVariableExecutionContextAccessor();
    }

    protected override Task<OperatorExecutionOutput> ExecuteCoreAsync(
        Operator @operator,
        Dictionary<string, object>? inputs,
        CancellationToken cancellationToken)
    {
        var scope = GetStringParam(@operator, "Scope", "Run");
        var variableIdText = GetStringParam(@operator, "VariableId", "");
        var variableName = GetStringParam(@operator, "VariableName", "");
        var dataType = GetStringParam(@operator, "DataType", "String");
        var conversionMode = GetConversionMode(@operator);
        var expression = GetStringParam(@operator, "Expression", "");
        var statusGate = EvaluateInputStatusGate(@operator, inputs);
        if (!statusGate.Allowed)
        {
            if (statusGate.Fail)
            {
                return Task.FromResult(OperatorExecutionOutput.Failure(statusGate.Reason));
            }

            return Task.FromResult(CreateSkippedOutput(variableIdText, variableName, dataType, statusGate));
        }

        if (!TryResolveWriteValue(@operator, inputs, variableName, dataType, conversionMode, out var value, out var valueError))
        {
            return Task.FromResult(OperatorExecutionOutput.Failure(valueError));
        }

        if (scope.Equals("Project", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(WriteProjectVariable(@operator.Id, variableIdText, variableName, value, conversionMode, expression, statusGate));
        }

        if (string.IsNullOrWhiteSpace(variableName))
        {
            return Task.FromResult(OperatorExecutionOutput.Failure("变量名不能为空"));
        }

        if (!TryConvertRunValue(value, dataType, conversionMode, out var converted, out var convertError))
        {
            return Task.FromResult(OperatorExecutionOutput.Failure($"Variable '{variableName}' cannot be written as {dataType}: {convertError}"));
        }

        _variableContext.SetValue(variableName, converted);

        Logger.LogDebug("[VariableWrite] Write {VariableName} = {Value}", variableName, converted);

        return Task.FromResult(OperatorExecutionOutput.Success(new Dictionary<string, object>
        {
            ["VariableName"] = variableName,
            ["VariableId"] = string.Empty,
            ["ValueType"] = dataType,
            ["Value"] = converted,
            ["Version"] = 0L,
            ["UpdatedAtUtc"] = string.Empty,
            ["UpdatedBy"] = "Run",
            ["WriteSkipped"] = false,
            ["SkipReason"] = string.Empty,
            ["InputStatusValue"] = statusGate.StatusValue,
            ["CycleCount"] = _variableContext.CycleCount
        }));
    }

    public override ValidationResult ValidateParameters(Operator @operator)
    {
        var scope = GetStringParam(@operator, "Scope", "Run");
        var variableName = GetStringParam(@operator, "VariableName", "");
        var variableIdText = GetStringParam(@operator, "VariableId", "");

        if (scope.Equals("Project", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(variableIdText) && string.IsNullOrWhiteSpace(variableName))
            {
                return ValidationResult.Invalid("Project 作用域必须配置 VariableId 或 VariableName");
            }
        }
        else if (string.IsNullOrWhiteSpace(variableName))
        {
            return ValidationResult.Invalid("变量名不能为空");
        }

        var validTypes = new[] { "string", "int", "integer", "double", "float", "bool", "boolean", "object", "any" };
        var dataType = GetStringParam(@operator, "DataType", "String").ToLowerInvariant();
        if (!validTypes.Contains(dataType))
        {
            return ValidationResult.Invalid($"不支持的数据类型: {dataType}");
        }

        if (scope.Equals("Project", StringComparison.OrdinalIgnoreCase) &&
            dataType is "object" or "any")
        {
            return ValidationResult.Invalid("Object/Any DataType is only supported for Run scope variables.");
        }

        return ValidationResult.Valid();
    }

    private InputStatusGateResult EvaluateInputStatusGate(
        Operator @operator,
        Dictionary<string, object>? inputs)
    {
        var requireInputStatus = GetBoolParam(@operator, "RequireInputStatus", false);
        if (!requireInputStatus)
        {
            return InputStatusGateResult.Allow(string.Empty);
        }

        var statusFieldName = GetStringParam(@operator, "InputStatusFieldName", "Status");
        var failOnInputStatusFalse = GetBoolParam(@operator, "FailOnInputStatusFalse", false);
        if (inputs == null || !TryResolveInputPath(inputs, statusFieldName, out var statusValue))
        {
            return InputStatusGateResult.Block(
                $"Input status field '{statusFieldName}' was not found.",
                string.Empty,
                failOnInputStatusFalse);
        }

        var formattedStatus = FormatScalar(statusValue);
        if (!TryConvertInputStatus(statusValue, out var isAllowed))
        {
            return InputStatusGateResult.Block(
                $"Input status field '{statusFieldName}' is not a boolean/OK/NG value.",
                formattedStatus,
                failOnInputStatusFalse);
        }

        return isAllowed
            ? InputStatusGateResult.Allow(formattedStatus)
            : InputStatusGateResult.Block(
                $"Input status field '{statusFieldName}' is false.",
                formattedStatus,
                failOnInputStatusFalse);
    }

    private OperatorExecutionOutput CreateSkippedOutput(
        string variableIdText,
        string variableName,
        string dataType,
        InputStatusGateResult statusGate)
    {
        return OperatorExecutionOutput.Success(new Dictionary<string, object>
        {
            ["VariableName"] = variableName,
            ["VariableId"] = variableIdText,
            ["ValueType"] = dataType,
            ["Value"] = string.Empty,
            ["Version"] = 0L,
            ["UpdatedAtUtc"] = string.Empty,
            ["UpdatedBy"] = string.Empty,
            ["WriteSkipped"] = true,
            ["SkipReason"] = statusGate.Reason,
            ["InputStatusValue"] = statusGate.StatusValue,
            ["CycleCount"] = _variableContext.CycleCount
        });
    }

    private bool TryResolveWriteValue(
        Operator @operator,
        Dictionary<string, object>? inputs,
        string variableName,
        string dataType,
        ProjectVariableConversionMode conversionMode,
        out object value,
        out string error)
    {
        value = string.Empty;
        error = string.Empty;

        var useInputValue = GetBoolParam(@operator, "UseInputValue", true);
        if (useInputValue && inputs != null)
        {
            var inputFieldName = GetStringParam(@operator, "InputFieldName", string.Empty);
            if (!string.IsNullOrWhiteSpace(inputFieldName))
            {
                if (TryResolveInputPath(inputs, inputFieldName, out var selectedValue))
                {
                    value = selectedValue ?? string.Empty;
                    return true;
                }

                error = $"Input field '{inputFieldName}' was not found.";
                return false;
            }

            if (inputs.TryGetValue("Value", out var inputValue))
            {
                value = inputValue;
                return true;
            }

            if (!string.IsNullOrWhiteSpace(variableName) && inputs.TryGetValue(variableName, out var namedValue))
            {
                value = namedValue;
                return true;
            }
        }

        return TryGetStaticValue(@operator, dataType, conversionMode, out value, out error);
    }

    private static bool TryConvertInputStatus(object? raw, out bool value)
    {
        value = false;
        switch (raw)
        {
            case bool boolean:
                value = boolean;
                return true;
            case JsonElement { ValueKind: JsonValueKind.True }:
                value = true;
                return true;
            case JsonElement { ValueKind: JsonValueKind.False }:
                value = false;
                return true;
            case int i:
                value = i != 0;
                return true;
            case long l:
                value = l != 0;
                return true;
            case double d when double.IsFinite(d):
                value = Math.Abs(d) > double.Epsilon;
                return true;
            case float f when float.IsFinite(f):
                value = Math.Abs(f) > float.Epsilon;
                return true;
            case decimal m:
                value = m != 0;
                return true;
        }

        var text = FormatScalar(raw).Trim();
        if (bool.TryParse(text, out value))
        {
            return true;
        }

        if (double.TryParse(text, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var number) &&
            double.IsFinite(number))
        {
            value = Math.Abs(number) > double.Epsilon;
            return true;
        }

        switch (text.ToUpperInvariant())
        {
            case "OK":
            case "PASS":
            case "TRUE":
                value = true;
                return true;
            case "NG":
            case "FAIL":
            case "FALSE":
                value = false;
                return true;
            default:
                return false;
        }
    }

    private static string FormatScalar(object? value)
    {
        if (value == null)
        {
            return string.Empty;
        }

        if (value is JsonElement jsonElement)
        {
            return jsonElement.ValueKind switch
            {
                JsonValueKind.String => jsonElement.GetString() ?? string.Empty,
                JsonValueKind.Null => string.Empty,
                JsonValueKind.Undefined => string.Empty,
                _ => jsonElement.GetRawText()
            };
        }

        if (value is IFormattable formattable && value is not string)
        {
            return formattable.ToString(null, CultureInfo.InvariantCulture) ?? string.Empty;
        }

        return value.ToString() ?? string.Empty;
    }

    private static bool TryResolveInputPath(
        IReadOnlyDictionary<string, object> inputs,
        string path,
        out object? value)
    {
        value = null;
        var segments = path.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length == 0 || !TryGetDictionaryValue(inputs, segments[0], out value))
        {
            return false;
        }

        for (var i = 1; i < segments.Length; i++)
        {
            if (!TryGetMemberValue(value, segments[i], out value))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryGetDictionaryValue(
        IReadOnlyDictionary<string, object> values,
        string key,
        out object? value)
    {
        if (values.TryGetValue(key, out value))
        {
            return true;
        }

        foreach (var item in values)
        {
            if (item.Key.Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                value = item.Value;
                return true;
            }
        }

        value = null;
        return false;
    }

    private static bool TryGetMemberValue(object? source, string memberName, out object? value)
    {
        value = null;
        if (source == null || string.IsNullOrWhiteSpace(memberName))
        {
            return false;
        }

        if (source is string text && TryParseJsonValue(text, out var parsedJson))
        {
            return TryGetMemberValue(parsedJson, memberName, out value);
        }

        if (source is IReadOnlyDictionary<string, object> readOnlyDictionary)
        {
            return TryGetDictionaryValue(readOnlyDictionary, memberName, out value);
        }

        if (source is IDictionary<string, object> dictionary)
        {
            foreach (var item in dictionary)
            {
                if (item.Key.Equals(memberName, StringComparison.OrdinalIgnoreCase))
                {
                    value = item.Value;
                    return true;
                }
            }

            return false;
        }

        if (source is System.Collections.IDictionary nonGenericDictionary)
        {
            foreach (System.Collections.DictionaryEntry entry in nonGenericDictionary)
            {
                if (entry.Key?.ToString()?.Equals(memberName, StringComparison.OrdinalIgnoreCase) == true)
                {
                    value = entry.Value;
                    return true;
                }
            }

            return false;
        }

        if (source is JsonElement jsonElement)
        {
            return TryGetMemberValue(ConvertJsonElement(jsonElement), memberName, out value);
        }

        if (int.TryParse(memberName, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index) &&
            TryGetIndexedValue(source, index, out value))
        {
            return true;
        }

        var property = source.GetType().GetProperty(
            memberName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);
        if (property == null)
        {
            return false;
        }

        value = property.GetValue(source);
        return true;
    }

    private static bool TryGetIndexedValue(object? source, int index, out object? value)
    {
        value = null;
        if (source == null || index < 0)
        {
            return false;
        }

        if (source is System.Collections.IList list)
        {
            if (index >= list.Count)
            {
                return false;
            }

            value = list[index];
            return true;
        }

        if (source is System.Collections.IEnumerable enumerable &&
            source is not string &&
            source is not System.Collections.IDictionary)
        {
            var currentIndex = 0;
            foreach (var item in enumerable)
            {
                if (currentIndex == index)
                {
                    value = item;
                    return true;
                }

                currentIndex++;
            }
        }

        return false;
    }

    private static bool TryGetJsonElementMember(JsonElement element, string memberName, out object? value)
    {
        value = null;
        if (element.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        foreach (var property in element.EnumerateObject())
        {
            if (!property.Name.Equals(memberName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            value = property.Value.ValueKind switch
            {
                JsonValueKind.String => property.Value.GetString(),
                JsonValueKind.Number when property.Value.TryGetInt64(out var longValue) => longValue,
                JsonValueKind.Number when property.Value.TryGetDouble(out var doubleValue) => doubleValue,
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Null => null,
                _ => property.Value
            };
            return true;
        }

        return false;
    }

    private OperatorExecutionOutput WriteProjectVariable(
        Guid operatorId,
        string variableIdText,
        string variableName,
        object value,
        ProjectVariableConversionMode conversionMode,
        string? expression,
        InputStatusGateResult statusGate)
    {
        var context = _projectVariableContextAccessor.Current;
        if (context == null)
        {
            return OperatorExecutionOutput.Failure("Project variable session is not available.");
        }

        ProjectGlobalVariableDefinition definition;
        if (Guid.TryParse(variableIdText, out var variableId) &&
            context.Session.TryGetDefinition(variableId, out definition!))
        {
        }
        else if (!string.IsNullOrWhiteSpace(variableName) &&
            context.Session.TryGetDefinitionByName(variableName, out definition!))
        {
        }
        else
        {
            return OperatorExecutionOutput.Failure($"Project variable '{variableIdText}'/'{variableName}' does not exist.");
        }

        try
        {
            var writeValue = NormalizeProjectWriteValue(value, definition.ValueType, expression);
            var expressionVariables = ProjectVariableValueTransform.BuildExpressionVariables(context.Session, writeValue);
            if (!ProjectVariableValueTransform.TryConvertToVariableValue(
                    writeValue,
                    definition.ValueType,
                    conversionMode,
                    expression,
                    expressionVariables,
                    out var converted,
                    out var convertError))
            {
                return OperatorExecutionOutput.Failure(convertError ?? "Project variable value conversion failed.");
            }

            var snapshot = context.Session.SetValue(
                definition.Id,
                converted,
                ProjectVariableUpdatedBy.VariableWrite,
                context.RunId,
                operatorId);
            return OperatorExecutionOutput.Success(new Dictionary<string, object>
            {
                ["VariableName"] = definition.Name,
                ["VariableId"] = definition.Id.ToString("D"),
                ["ValueType"] = definition.ValueType.ToString(),
                ["Value"] = ProjectVariableValueConverter.ToObject(snapshot.Value)!,
                ["Version"] = snapshot.Version,
                ["UpdatedAtUtc"] = snapshot.UpdatedAtUtc.ToString("O"),
                ["UpdatedBy"] = snapshot.UpdatedBy.ToString(),
                ["WriteSkipped"] = false,
                ["SkipReason"] = string.Empty,
                ["InputStatusValue"] = statusGate.StatusValue,
                ["CycleCount"] = _variableContext.CycleCount
            });
        }
        catch (Exception ex)
        {
            return OperatorExecutionOutput.Failure(ex.Message);
        }
    }

    private bool TryGetStaticValue(
        Operator @operator,
        string dataType,
        ProjectVariableConversionMode conversionMode,
        out object value,
        out string error)
    {
        value = string.Empty;
        error = string.Empty;
        var staticValue = GetStringParam(@operator, "StaticValue", "0");
        if (TryConvertRunValue(staticValue, dataType, conversionMode, out value, out error))
        {
            return true;
        }

        error = $"StaticValue cannot be converted to {dataType}: {error}";
        return false;
    }

    private static bool TryConvertRunValue(
        object? value,
        string dataType,
        ProjectVariableConversionMode conversionMode,
        out object converted,
        out string error)
    {
        converted = string.Empty;
        error = string.Empty;
        try
        {
            converted = dataType.ToLowerInvariant() switch
            {
                "int" or "integer" => TryConvertRunIntegerValue(value, conversionMode, out var integerValue, out var integerError)
                    ? integerValue
                    : throw new FormatException(integerError),
                "double" or "float" => Convert.ToDouble(value),
                "bool" or "boolean" => TryConvertInputStatus(value, out var boolValue)
                    ? boolValue
                    : Convert.ToBoolean(value),
                "object" or "any" => NormalizeRunObjectValue(value),
                _ => value?.ToString() ?? string.Empty
            };
            return true;
        }
        catch (Exception ex) when (ex is InvalidCastException or FormatException or OverflowException)
        {
            error = ex.Message;
            return false;
        }
    }

    private static bool TryConvertRunIntegerValue(
        object? value,
        ProjectVariableConversionMode conversionMode,
        out long converted,
        out string error)
    {
        converted = 0L;
        error = string.Empty;
        if (!TryReadDecimal(value, out var number))
        {
            error = $"Value type '{value?.GetType().Name ?? "null"}' is not numeric.";
            return false;
        }

        var hasFraction = decimal.Truncate(number) != number;
        if (hasFraction && conversionMode == ProjectVariableConversionMode.Exact)
        {
            error = "Fractional numeric values require an explicit Round, Floor, Ceiling or Truncate conversion mode.";
            return false;
        }

        var rounded = conversionMode switch
        {
            ProjectVariableConversionMode.Round => decimal.Round(number, 0, MidpointRounding.AwayFromZero),
            ProjectVariableConversionMode.Floor => decimal.Floor(number),
            ProjectVariableConversionMode.Ceiling => decimal.Ceiling(number),
            ProjectVariableConversionMode.Truncate => decimal.Truncate(number),
            _ => number
        };

        if (rounded < long.MinValue || rounded > long.MaxValue)
        {
            error = "Converted integer value is outside Int64 range.";
            return false;
        }

        converted = decimal.ToInt64(rounded);
        return true;
    }

    private static bool TryReadDecimal(object? value, out decimal number)
    {
        number = 0m;
        try
        {
            switch (value)
            {
                case null:
                    return false;
                case decimal decimalValue:
                    number = decimalValue;
                    return true;
                case JsonElement jsonElement:
                    return TryReadDecimal(ConvertJsonElement(jsonElement), out number);
                case byte byteValue:
                    number = byteValue;
                    return true;
                case short shortValue:
                    number = shortValue;
                    return true;
                case int intValue:
                    number = intValue;
                    return true;
                case long longValue:
                    number = longValue;
                    return true;
                case float floatValue when float.IsFinite(floatValue):
                    number = Convert.ToDecimal(floatValue);
                    return true;
                case double doubleValue when double.IsFinite(doubleValue):
                    number = Convert.ToDecimal(doubleValue);
                    return true;
            }
        }
        catch (OverflowException)
        {
            return false;
        }

        return decimal.TryParse(
            FormatScalar(value),
            NumberStyles.Float | NumberStyles.AllowThousands,
            CultureInfo.InvariantCulture,
            out number);
    }

    private static object NormalizeProjectWriteValue(
        object value,
        ProjectGlobalVariableValueType targetType,
        string? expression)
    {
        if (targetType == ProjectGlobalVariableValueType.Boolean &&
            string.IsNullOrWhiteSpace(expression) &&
            TryConvertInputStatus(value, out var boolValue))
        {
            return boolValue;
        }

        return value;
    }

    private static object NormalizeRunObjectValue(object? value)
    {
        if (value == null)
        {
            return string.Empty;
        }

        if (value is JsonElement jsonElement)
        {
            return ConvertJsonElement(jsonElement) ?? string.Empty;
        }

        if (value is string text && TryParseJsonValue(text, out var parsed))
        {
            return parsed ?? string.Empty;
        }

        return value;
    }

    private static bool TryParseJsonValue(string text, out object? value)
    {
        value = null;
        var trimmed = text.Trim();
        if (trimmed.Length < 2 ||
            (trimmed[0] != '{' && trimmed[0] != '['))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(trimmed);
            value = ConvertJsonElement(document.RootElement);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static object? ConvertJsonElement(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Object => element
                .EnumerateObject()
                .ToDictionary(
                    property => property.Name,
                    property => ConvertJsonElement(property.Value) ?? string.Empty,
                    StringComparer.OrdinalIgnoreCase),
            JsonValueKind.Array => element.EnumerateArray().Select(ConvertJsonElement).ToArray(),
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number when element.TryGetInt64(out var longValue) => longValue,
            JsonValueKind.Number when element.TryGetDouble(out var doubleValue) => doubleValue,
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            _ => element.GetRawText()
        };
    }

    private ProjectVariableConversionMode GetConversionMode(Operator @operator)
    {
        var text = GetStringParam(@operator, "ConversionMode", "Exact");
        return Enum.TryParse<ProjectVariableConversionMode>(text, ignoreCase: true, out var mode)
            ? mode
            : ProjectVariableConversionMode.Exact;
    }

    private sealed record InputStatusGateResult(
        bool Allowed,
        bool Fail,
        string StatusValue,
        string Reason)
    {
        public static InputStatusGateResult Allow(string statusValue)
        {
            return new InputStatusGateResult(true, false, statusValue, string.Empty);
        }

        public static InputStatusGateResult Block(string reason, string statusValue, bool fail)
        {
            return new InputStatusGateResult(false, fail, statusValue, reason);
        }
    }
}
