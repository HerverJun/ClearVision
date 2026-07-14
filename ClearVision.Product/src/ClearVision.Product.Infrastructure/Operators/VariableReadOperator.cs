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
    DisplayName = "变量读取",
    Description = "从单次运行变量或项目全局变量读取值",
    CategoryId = OperatorCategoryId.DataProcessing,
    IconName = "variable-read")]
[OutputPort("Value", "值", PortDataType.Any)]
[OutputPort("RawValue", "Raw Value", PortDataType.Any)]
[OutputPort("Exists", "是否存在", PortDataType.Boolean)]
[OutputPort("CycleCount", "循环计数", PortDataType.Integer)]
[OperatorParam("Scope", "作用域", "enum", DefaultValue = "Run", Options = new[] { "Run|单次运行", "Project|项目全局" })]
[OperatorParam("VariableId", "变量ID", "string", Description = "Project 作用域变量的稳定 ID", DefaultValue = "")]
[OperatorParam("VariableName", "变量名", "string", Description = "要读取的变量名称", DefaultValue = "")]
[OperatorParam("DefaultValue", "默认值", "string", Description = "变量不存在时的默认值", DefaultValue = "0")]
[OperatorParam("DataType", "数据类型", "enum", DefaultValue = "String", Options = new[] { "String|字符串", "Int|整数", "Double|浮点数", "Bool|布尔值", "Object|对象" })]
[OperatorParam("OutputFieldName", "Output Field Name", "string", Description = "Optional field path read from the raw variable value, for example ParsedFields.Score.", DefaultValue = "")]
[OperatorParam("FailOnMissingOutputField", "Fail On Missing Output Field", "bool", Description = "When enabled, a missing OutputFieldName path fails instead of falling back to the full variable value.", DefaultValue = true)]
[OperatorParam("ConversionMode", "Conversion Mode", "enum", DefaultValue = "Exact", Options = new[] { "Exact|Exact", "Round|Round", "Floor|Floor", "Ceiling|Ceiling", "Truncate|Truncate" })]
[OperatorParam("Expression", "Expression", "string", Description = "Optional controlled expression evaluated after Project variable read. Use value for the current variable value.", DefaultValue = "")]
[OutputPort("VariableId", "Variable Id", PortDataType.String)]
[OutputPort("ValueType", "Value Type", PortDataType.String)]
[OutputPort("Version", "Version", PortDataType.Integer)]
[OutputPort("UpdatedAtUtc", "Updated At UTC", PortDataType.String)]
[OutputPort("UpdatedBy", "Updated By", PortDataType.String)]
[OutputPort("ReadSource", "Read Source", PortDataType.String)]
[OutputPort("OutputFieldName", "Output Field Name", PortDataType.String)]
[OutputPort("OutputFieldFound", "Output Field Found", PortDataType.Boolean)]
[OperatorParam("FailOnMissingVariable", "Fail On Missing Variable", "bool", Description = "When enabled, missing run/project variable values fail instead of returning DefaultValue.", DefaultValue = false)]
public class VariableReadOperator : OperatorBase
{
    private readonly IVariableContext _variableContext;
    private readonly IProjectVariableExecutionContextAccessor _projectVariableContextAccessor;

    public override OperatorType OperatorType => OperatorType.VariableRead;

    public VariableReadOperator(
        ILogger<VariableReadOperator> logger,
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
        var defaultValue = GetStringParam(@operator, "DefaultValue", "0");
        var dataType = GetStringParam(@operator, "DataType", "String");
        var outputFieldName = GetStringParam(@operator, "OutputFieldName", "");
        var failOnMissingOutputField = GetBoolParam(@operator, "FailOnMissingOutputField", true);
        var conversionMode = GetConversionMode(@operator);
        var expression = GetStringParam(@operator, "Expression", "");
        var failOnMissingVariable = GetBoolParam(@operator, "FailOnMissingVariable", false);

        if (scope.Equals("Project", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(ReadProjectVariable(
                variableIdText,
                variableName,
                defaultValue,
                dataType,
                outputFieldName,
                failOnMissingOutputField,
                conversionMode,
                expression,
                failOnMissingVariable));
        }

        if (string.IsNullOrWhiteSpace(variableName))
        {
            return Task.FromResult(OperatorExecutionOutput.Failure("变量名不能为空"));
        }

        var exists = _variableContext.Contains(variableName);
        if (!exists && failOnMissingVariable)
        {
            return Task.FromResult(OperatorExecutionOutput.Failure($"Run variable '{variableName}' does not exist."));
        }

        var rawValue = exists
            ? _variableContext.GetValue<object?>(variableName, null) ?? string.Empty
            : defaultValue;
        if (!TryResolveReadValue(rawValue, variableName, defaultValue, dataType, outputFieldName, failOnMissingOutputField, conversionMode, out var value, out var outputFieldFound, out var readSourceSuffix, out var readError))
        {
            return Task.FromResult(OperatorExecutionOutput.Failure(readError));
        }

        Logger.LogDebug("[VariableRead] Read {VariableName} = {Value} (exists: {Exists})",
            variableName, value, exists);

        return Task.FromResult(OperatorExecutionOutput.Success(new Dictionary<string, object>
        {
            ["Value"] = value,
            ["RawValue"] = rawValue,
            ["VariableName"] = variableName,
            ["VariableId"] = string.Empty,
            ["ValueType"] = dataType,
            ["Version"] = 0L,
            ["UpdatedAtUtc"] = string.Empty,
            ["UpdatedBy"] = "Run",
            ["ReadSource"] = exists ? $"RunVariable{readSourceSuffix}" : "DefaultValue",
            ["OutputFieldName"] = outputFieldName,
            ["OutputFieldFound"] = outputFieldFound,
            ["Exists"] = exists,
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

    private static bool TryResolveReadValue(
        object? rawValue,
        string variableName,
        string defaultValue,
        string dataType,
        string outputFieldName,
        bool failOnMissingOutputField,
        ProjectVariableConversionMode? integerConversionMode,
        out object value,
        out bool outputFieldFound,
        out string readSourceSuffix,
        out string error)
    {
        value = string.Empty;
        outputFieldFound = false;
        readSourceSuffix = string.Empty;
        error = string.Empty;

        var selectedValue = rawValue ?? defaultValue;
        if (!string.IsNullOrWhiteSpace(outputFieldName))
        {
            if (TryResolveValuePath(selectedValue, outputFieldName, out var fieldValue))
            {
                selectedValue = fieldValue ?? string.Empty;
                outputFieldFound = true;
                readSourceSuffix = "Field";
            }
            else if (failOnMissingOutputField)
            {
                error = $"Variable '{variableName}' field '{outputFieldName}' was not found.";
                return false;
            }
        }

        var conversionDefaultValue = outputFieldFound ? string.Empty : defaultValue;
        if (!TryConvertReadValue(selectedValue, dataType, conversionDefaultValue, integerConversionMode, out value, out var convertError))
        {
            error = string.IsNullOrWhiteSpace(outputFieldName)
                ? $"Variable '{variableName}' cannot be read as {dataType}: {convertError}"
                : $"Variable '{variableName}' field '{outputFieldName}' cannot be read as {dataType}: {convertError}";
            return false;
        }

        return true;
    }

    private static bool TryConvertReadValue(
        object? raw,
        string dataType,
        string defaultValue,
        ProjectVariableConversionMode? integerConversionMode,
        out object value,
        out string error)
    {
        value = string.Empty;
        error = string.Empty;
        var normalizedType = (dataType ?? string.Empty).Trim().ToLowerInvariant();
        try
        {
            switch (normalizedType)
            {
                case "int":
                case "integer":
                    if (integerConversionMode.HasValue)
                    {
                        if (TryConvertToInt64(raw, integerConversionMode, out var convertedValue, out var convertedError))
                        {
                            value = convertedValue;
                            return true;
                        }

                        error = string.IsNullOrWhiteSpace(convertedError)
                            ? $"'{FormatScalar(raw)}' is not an integer."
                            : convertedError;
                        return false;
                    }

                    if (TryConvertToInt64(raw, null, out var intValue, out var intError) ||
                        TryConvertToInt64(defaultValue, null, out intValue, out _))
                    {
                        value = intValue;
                        return true;
                    }

                    error = string.IsNullOrWhiteSpace(intError)
                        ? $"'{FormatScalar(raw)}' is not an integer."
                        : intError;
                    return false;
                case "double":
                case "float":
                    if (TryConvertToDouble(raw, out var doubleValue) ||
                        TryConvertToDouble(defaultValue, out doubleValue))
                    {
                        value = doubleValue;
                        return true;
                    }

                    error = $"'{FormatScalar(raw)}' is not a number.";
                    return false;
                case "bool":
                case "boolean":
                    if (TryConvertToBool(raw, out var boolValue) ||
                        TryConvertToBool(defaultValue, out boolValue))
                    {
                        value = boolValue;
                        return true;
                    }

                    error = $"'{FormatScalar(raw)}' is not a boolean/OK/NG value.";
                    return false;
                case "object":
                case "any":
                    value = NormalizeObjectValue(raw ?? defaultValue);
                    return true;
                default:
                    value = FormatScalar(raw ?? defaultValue);
                    return true;
            }
        }
        catch (Exception ex) when (ex is InvalidCastException or FormatException or OverflowException)
        {
            error = ex.Message;
            return false;
        }
    }

    private static bool TryConvertToInt64(
        object? raw,
        ProjectVariableConversionMode? conversionMode,
        out long value,
        out string error)
    {
        value = 0L;
        error = string.Empty;
        if (!conversionMode.HasValue)
        {
            return TryConvertToInt64(raw, out value);
        }

        if (!TryReadDecimal(raw, out var number))
        {
            error = $"'{FormatScalar(raw)}' is not an integer.";
            return false;
        }

        var hasFraction = decimal.Truncate(number) != number;
        if (hasFraction && conversionMode.Value == ProjectVariableConversionMode.Exact)
        {
            error = "Fractional numeric values require an explicit Round, Floor, Ceiling or Truncate conversion mode.";
            return false;
        }

        var converted = conversionMode.Value switch
        {
            ProjectVariableConversionMode.Round => decimal.Round(number, 0, MidpointRounding.AwayFromZero),
            ProjectVariableConversionMode.Floor => decimal.Floor(number),
            ProjectVariableConversionMode.Ceiling => decimal.Ceiling(number),
            ProjectVariableConversionMode.Truncate => decimal.Truncate(number),
            _ => number
        };

        if (converted < long.MinValue || converted > long.MaxValue)
        {
            error = "Converted integer value is outside Int64 range.";
            return false;
        }

        value = decimal.ToInt64(converted);
        return true;
    }

    private static bool TryConvertToInt64(object? raw, out long value)
    {
        value = 0L;
        return raw switch
        {
            null => false,
            long longValue => Set(out value, longValue),
            int intValue => Set(out value, intValue),
            short shortValue => Set(out value, shortValue),
            byte byteValue => Set(out value, byteValue),
            double doubleValue when double.IsFinite(doubleValue) => Set(out value, Convert.ToInt64(doubleValue)),
            float floatValue when float.IsFinite(floatValue) => Set(out value, Convert.ToInt64(floatValue)),
            decimal decimalValue => Set(out value, Convert.ToInt64(decimalValue, CultureInfo.InvariantCulture)),
            JsonElement jsonElement => TryConvertToInt64(ConvertJsonElement(jsonElement), out value),
            _ => long.TryParse(FormatScalar(raw), NumberStyles.Integer, CultureInfo.InvariantCulture, out value)
        };
    }

    private static bool TryReadDecimal(object? raw, out decimal number)
    {
        number = 0m;
        try
        {
            switch (raw)
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
            FormatScalar(raw),
            NumberStyles.Float | NumberStyles.AllowThousands,
            CultureInfo.InvariantCulture,
            out number);
    }

    private static bool TryConvertToDouble(object? raw, out double value)
    {
        value = 0.0;
        return raw switch
        {
            null => false,
            double doubleValue when double.IsFinite(doubleValue) => Set(out value, doubleValue),
            float floatValue when float.IsFinite(floatValue) => Set(out value, floatValue),
            decimal decimalValue => Set(out value, (double)decimalValue),
            long longValue => Set(out value, longValue),
            int intValue => Set(out value, intValue),
            short shortValue => Set(out value, shortValue),
            byte byteValue => Set(out value, byteValue),
            JsonElement jsonElement => TryConvertToDouble(ConvertJsonElement(jsonElement), out value),
            _ => double.TryParse(FormatScalar(raw), NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out value) &&
                 double.IsFinite(value)
        };
    }

    private static bool TryConvertToBool(object? raw, out bool value)
    {
        value = false;
        switch (raw)
        {
            case bool boolean:
                value = boolean;
                return true;
            case JsonElement jsonElement:
                return TryConvertToBool(ConvertJsonElement(jsonElement), out value);
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

    private static object NormalizeObjectValue(object? value)
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

    private static bool Set<T>(out T target, T value)
    {
        target = value;
        return true;
    }

    private static bool TryResolveValuePath(object? source, string path, out object? value)
    {
        value = source;
        var segments = path.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length == 0)
        {
            return false;
        }

        foreach (var segment in segments)
        {
            if (!TryGetMemberValue(value, segment, out value))
            {
                return false;
            }
        }

        return true;
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

    private static bool TryGetIndexedValue(object source, int index, out object? value)
    {
        value = null;
        if (index < 0)
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

        if (source is Array array)
        {
            if (index >= array.Length)
            {
                return false;
            }

            value = array.GetValue(index);
            return true;
        }

        return false;
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

        if (value is System.Collections.IEnumerable and not string)
        {
            try
            {
                return JsonSerializer.Serialize(value);
            }
            catch
            {
                return value.ToString() ?? string.Empty;
            }
        }

        return value.ToString() ?? string.Empty;
    }

    private OperatorExecutionOutput ReadProjectVariable(
        string variableIdText,
        string variableName,
        string defaultValue,
        string dataType,
        string outputFieldName,
        bool failOnMissingOutputField,
        ProjectVariableConversionMode conversionMode,
        string? expression,
        bool failOnMissingVariable)
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

        var exists = context.Session.TryGetSnapshot(definition.Id, out var snapshot);
        if (!exists && failOnMissingVariable)
        {
            return OperatorExecutionOutput.Failure($"Project variable '{definition.Name}' has no value snapshot.");
        }

        object rawValue = defaultValue;
        object value = defaultValue;
        var outputFieldFound = false;
        var readSourceSuffix = string.Empty;
        if (exists)
        {
            rawValue = ProjectVariableValueConverter.ToObject(snapshot.Value) ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(outputFieldName))
            {
                if (!TryResolveReadValue(rawValue, definition.Name, defaultValue, dataType, outputFieldName, failOnMissingOutputField, null, out value, out outputFieldFound, out readSourceSuffix, out var readError))
                {
                    return OperatorExecutionOutput.Failure(readError);
                }
            }
            else
            {
                var expressionVariables = ProjectVariableValueTransform.BuildExpressionVariables(context.Session, rawValue);
                if (!ProjectVariableValueTransform.TryConvertForParameter(
                        snapshot.Value,
                        definition.ValueType,
                        ToParameterType(dataType),
                        conversionMode,
                        expression,
                        expressionVariables,
                        out var converted,
                        out var error))
                {
                    return OperatorExecutionOutput.Failure($"Project variable '{definition.Name}' cannot be read as {dataType}: {error}");
                }

                value = converted ?? "";
            }
        }

        return OperatorExecutionOutput.Success(new Dictionary<string, object>
        {
            ["Value"] = value,
            ["RawValue"] = rawValue,
            ["VariableName"] = definition.Name,
            ["VariableId"] = definition.Id.ToString("D"),
            ["ValueType"] = definition.ValueType.ToString(),
            ["Version"] = exists ? snapshot.Version : 0L,
            ["UpdatedAtUtc"] = exists ? snapshot.UpdatedAtUtc.ToString("O") : string.Empty,
            ["UpdatedBy"] = exists ? snapshot.UpdatedBy.ToString() : string.Empty,
            ["ReadSource"] = exists ? $"ProjectVariable{readSourceSuffix}" : "DefaultValue",
            ["OutputFieldName"] = outputFieldName,
            ["OutputFieldFound"] = outputFieldFound,
            ["Exists"] = exists,
            ["CycleCount"] = _variableContext.CycleCount
        });
    }

    private static string ToParameterType(string dataType)
    {
        return dataType.ToLowerInvariant() switch
        {
            "int" or "integer" => "long",
            "bool" or "boolean" => "bool",
            "double" or "float" => "double",
            _ => "string"
        };
    }

    private ProjectVariableConversionMode GetConversionMode(Operator @operator)
    {
        var text = GetStringParam(@operator, "ConversionMode", "Exact");
        return Enum.TryParse<ProjectVariableConversionMode>(text, ignoreCase: true, out var mode)
            ? mode
            : ProjectVariableConversionMode.Exact;
    }
}
