using System.Globalization;
using System.Text.Json;
using ClearVision.Product.Core.Enums;

namespace ClearVision.Product.Core.ProjectVariables;

public static class ProjectVariableValueConverter
{
    public const int MaxStringLength = 4096;

    public static bool TryConvertToVariableValue(
        object? value,
        ProjectGlobalVariableValueType valueType,
        out JsonElement converted,
        out string? error)
    {
        converted = default;
        error = null;

        if (value is JsonElement element)
        {
            return TryConvertJsonElement(element, valueType, out converted, out error);
        }

        try
        {
            converted = valueType switch
            {
                ProjectGlobalVariableValueType.String => JsonSerializer.SerializeToElement(ConvertToString(value)),
                ProjectGlobalVariableValueType.Int64 => JsonSerializer.SerializeToElement(ConvertToInt64(value)),
                ProjectGlobalVariableValueType.Double => JsonSerializer.SerializeToElement(ConvertToDouble(value)),
                ProjectGlobalVariableValueType.Boolean => JsonSerializer.SerializeToElement(ConvertToBoolean(value)),
                _ => throw new InvalidOperationException($"Unsupported project variable type '{valueType}'.")
            };
            return true;
        }
        catch (Exception ex) when (ex is InvalidCastException or FormatException or OverflowException or InvalidOperationException)
        {
            error = ex.Message;
            return false;
        }
    }

    public static bool TryConvertForParameter(JsonElement value, string parameterType, out object? converted, out string? error)
    {
        converted = null;
        error = null;
        var normalizedType = parameterType.Trim().ToLowerInvariant();

        try
        {
            converted = normalizedType switch
            {
                "int" or "integer" => ReadInt64(value),
                "long" or "int64" => ReadInt64(value),
                "double" or "float" or "number" or "decimal" => ReadDouble(value),
                "bool" or "boolean" => ReadBoolean(value),
                "string" or "enum" or "select" => ReadString(value),
                _ => ReadBestScalar(value)
            };
            return true;
        }
        catch (Exception ex) when (ex is InvalidCastException or FormatException or OverflowException or InvalidOperationException)
        {
            error = ex.Message;
            return false;
        }
    }

    public static bool TryConvertForParameter(
        JsonElement value,
        ProjectGlobalVariableValueType variableType,
        string parameterType,
        out object? converted,
        out string? error)
    {
        converted = null;
        error = null;

        if (!ProjectGlobalVariableTypeCompatibility.IsCompatibleWithParameter(variableType, parameterType))
        {
            error = $"Project global variable type '{variableType}' is not compatible with parameter type '{parameterType}'.";
            return false;
        }

        return TryConvertForParameter(value, parameterType, out converted, out error);
    }

    public static object? ToObject(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.TryGetInt64(out var longValue)
                ? longValue
                : value.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            _ => value.GetRawText()
        };
    }

    public static string ToStableJson(ProjectGlobalVariableSchema schema)
    {
        return JsonSerializer.Serialize(schema, ProjectVariableJson.Options);
    }

    private static bool TryConvertJsonElement(
        JsonElement element,
        ProjectGlobalVariableValueType valueType,
        out JsonElement converted,
        out string? error)
    {
        converted = default;
        error = null;

        try
        {
            converted = valueType switch
            {
                ProjectGlobalVariableValueType.String => JsonSerializer.SerializeToElement(ReadString(element)),
                ProjectGlobalVariableValueType.Int64 => JsonSerializer.SerializeToElement(ReadInt64(element)),
                ProjectGlobalVariableValueType.Double => JsonSerializer.SerializeToElement(ReadDouble(element)),
                ProjectGlobalVariableValueType.Boolean => JsonSerializer.SerializeToElement(ReadBoolean(element)),
                _ => throw new InvalidOperationException($"Unsupported project variable type '{valueType}'.")
            };
            return true;
        }
        catch (Exception ex) when (ex is InvalidCastException or FormatException or OverflowException or InvalidOperationException)
        {
            error = ex.Message;
            return false;
        }
    }

    private static string ConvertToString(object? value)
    {
        if (value == null)
        {
            return string.Empty;
        }

        if (IsComplexObject(value))
        {
            throw new InvalidCastException($"Complex value type '{value.GetType().Name}' cannot be stored in a project global variable.");
        }

        var text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
        if (text.Length > MaxStringLength)
        {
            throw new InvalidCastException($"String value exceeds {MaxStringLength} characters.");
        }

        return text;
    }

    private static long ConvertToInt64(object? value)
    {
        return value switch
        {
            null => 0L,
            long longValue => longValue,
            int intValue => intValue,
            short shortValue => shortValue,
            byte byteValue => byteValue,
            double doubleValue when IsWhole(doubleValue) => Convert.ToInt64(doubleValue),
            decimal decimalValue when decimal.Truncate(decimalValue) == decimalValue => Convert.ToInt64(decimalValue),
            string stringValue when long.TryParse(stringValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => throw new InvalidCastException($"Value type '{value.GetType().Name}' is not compatible with Int64.")
        };
    }

    private static double ConvertToDouble(object? value)
    {
        return value switch
        {
            null => 0d,
            double doubleValue => doubleValue,
            float floatValue => floatValue,
            decimal decimalValue => Convert.ToDouble(decimalValue),
            long longValue => longValue,
            int intValue => intValue,
            string stringValue when double.TryParse(stringValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => throw new InvalidCastException($"Value type '{value.GetType().Name}' is not compatible with Double.")
        };
    }

    private static bool ConvertToBoolean(object? value)
    {
        return value switch
        {
            bool boolValue => boolValue,
            string stringValue when bool.TryParse(stringValue, out var parsed) => parsed,
            _ => throw new InvalidCastException($"Value type '{value?.GetType().Name ?? "null"}' is not compatible with Boolean.")
        };
    }

    private static string ReadString(JsonElement value)
    {
        var text = value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? string.Empty,
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Null or JsonValueKind.Undefined => string.Empty,
            _ => throw new InvalidCastException($"JSON {value.ValueKind} cannot be stored in a project global variable.")
        };

        if (text.Length > MaxStringLength)
        {
            throw new InvalidCastException($"String value exceeds {MaxStringLength} characters.");
        }

        return text;
    }

    private static long ReadInt64(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt64(out var longValue) => longValue,
            JsonValueKind.Number => throw new InvalidCastException("JSON number is not a valid Int64."),
            JsonValueKind.String when long.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => throw new InvalidCastException($"JSON {value.ValueKind} is not compatible with Int64.")
        };
    }

    private static double ReadDouble(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.Number => value.GetDouble(),
            JsonValueKind.String when double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => throw new InvalidCastException($"JSON {value.ValueKind} is not compatible with Double.")
        };
    }

    private static bool ReadBoolean(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String when bool.TryParse(value.GetString(), out var parsed) => parsed,
            _ => throw new InvalidCastException($"JSON {value.ValueKind} is not compatible with Boolean.")
        };
    }

    private static object? ReadBestScalar(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number when value.TryGetInt64(out var longValue) => longValue,
            JsonValueKind.Number => value.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            _ => throw new InvalidCastException($"JSON {value.ValueKind} cannot be applied to a scalar parameter.")
        };
    }

    private static bool IsWhole(double value) => Math.Abs(value % 1) < double.Epsilon;

    private static bool IsComplexObject(object value)
    {
        var type = value.GetType();
        return value is not string &&
            value is not bool &&
            value is not byte &&
            value is not short &&
            value is not int &&
            value is not long &&
            value is not float &&
            value is not double &&
            value is not decimal &&
            value is not JsonElement &&
            !type.IsEnum;
    }
}

public static class ProjectVariableJson
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };
}

public static class ProjectGlobalVariableTypeCompatibility
{
    private static readonly HashSet<string> StringParameterTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "string",
        "text",
        "enum",
        "select"
    };

    private static readonly HashSet<string> IntegerParameterTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "int",
        "integer",
        "long",
        "int64"
    };

    private static readonly HashSet<string> NumberParameterTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "double",
        "float",
        "number",
        "decimal"
    };

    private static readonly HashSet<string> BooleanParameterTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "bool",
        "boolean"
    };

    public static bool IsCompatibleWithParameter(ProjectGlobalVariableValueType variableType, string? parameterType)
    {
        var normalized = string.IsNullOrWhiteSpace(parameterType) ? string.Empty : parameterType.Trim();
        if (string.IsNullOrEmpty(normalized))
        {
            return true;
        }

        return variableType switch
        {
            ProjectGlobalVariableValueType.String => StringParameterTypes.Contains(normalized),
            ProjectGlobalVariableValueType.Int64 => IntegerParameterTypes.Contains(normalized) || NumberParameterTypes.Contains(normalized),
            ProjectGlobalVariableValueType.Double => NumberParameterTypes.Contains(normalized),
            ProjectGlobalVariableValueType.Boolean => BooleanParameterTypes.Contains(normalized),
            _ => false
        };
    }

    public static bool IsCompatibleWithOutputPort(ProjectGlobalVariableValueType variableType, PortDataType portType)
    {
        return variableType switch
        {
            ProjectGlobalVariableValueType.String => portType is PortDataType.String or PortDataType.Any,
            ProjectGlobalVariableValueType.Int64 => portType is PortDataType.Integer or PortDataType.Any,
            ProjectGlobalVariableValueType.Double => portType is PortDataType.Integer or PortDataType.Float or PortDataType.Any,
            ProjectGlobalVariableValueType.Boolean => portType is PortDataType.Boolean or PortDataType.Any,
            _ => false
        };
    }
}
