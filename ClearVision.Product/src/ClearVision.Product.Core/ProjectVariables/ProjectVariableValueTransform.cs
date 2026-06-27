using System.Globalization;
using System.Text.Json;

namespace ClearVision.Product.Core.ProjectVariables;

public static class ProjectVariableValueTransform
{
    public static bool TryConvertToVariableValue(
        object? rawValue,
        ProjectGlobalVariableValueType targetType,
        ProjectVariableConversionMode conversionMode,
        string? expression,
        IReadOnlyDictionary<string, object?> variables,
        out JsonElement converted,
        out string? error)
    {
        converted = default;
        error = null;
        var value = ApplyExpression(rawValue, expression, variables, out error);
        if (error != null)
        {
            return false;
        }

        if (targetType == ProjectGlobalVariableValueType.Int64 &&
            !ProjectVariableValueConverter.TryConvertToVariableValue(value, targetType, out converted, out _) &&
            TryApplyIntegerConversion(value, conversionMode, out var convertedLong, out error))
        {
            converted = JsonSerializer.SerializeToElement(convertedLong);
            return true;
        }

        if (!ProjectVariableValueConverter.TryConvertToVariableValue(value, targetType, out converted, out error))
        {
            if (targetType == ProjectGlobalVariableValueType.Int64 &&
                conversionMode == ProjectVariableConversionMode.Exact)
            {
                error = $"{error} Use an explicit Round, Floor, Ceiling or Truncate conversion mode to store fractional numeric values in Int64 variables.";
            }

            return false;
        }

        return true;
    }

    public static bool TryConvertForParameter(
        JsonElement rawValue,
        ProjectGlobalVariableValueType variableType,
        string parameterType,
        ProjectVariableConversionMode conversionMode,
        string? expression,
        IReadOnlyDictionary<string, object?> variables,
        out object? converted,
        out string? error)
    {
        converted = null;
        error = null;
        var value = ApplyExpression(rawValue, expression, variables, out error);
        if (error != null)
        {
            return false;
        }

        var element = value is JsonElement jsonElement
            ? jsonElement
            : JsonSerializer.SerializeToElement(value);

        if (ProjectGlobalVariableTypeCompatibility.IsCompatibleWithParameter(variableType, parameterType))
        {
            if (ProjectVariableValueConverter.TryConvertForParameter(element, parameterType, out converted, out error))
            {
                return true;
            }

            if (IsIntegerParameterType(parameterType) &&
                IsExplicitIntegerConversion(conversionMode) &&
                TryApplyIntegerConversion(value, conversionMode, out var convertedLong, out error))
            {
                converted = convertedLong;
                return true;
            }

            return false;
        }

        if (IsIntegerParameterType(parameterType) &&
            variableType is ProjectGlobalVariableValueType.Int64 or ProjectGlobalVariableValueType.Double &&
            TryApplyIntegerConversion(value, conversionMode, out var convertedLongValue, out error))
        {
            converted = convertedLongValue;
            return true;
        }

        if (IsIntegerParameterType(parameterType) &&
            conversionMode == ProjectVariableConversionMode.Exact)
        {
            error = $"Project global variable type '{variableType}' is not compatible with integer parameter type '{parameterType}'. Use an explicit Round, Floor, Ceiling or Truncate conversion mode.";
            return false;
        }

        error = $"Project global variable type '{variableType}' is not compatible with parameter type '{parameterType}'.";
        return false;
    }

    public static bool IsCompatibleWithParameter(
        ProjectGlobalVariableValueType variableType,
        string? parameterType,
        ProjectVariableConversionMode conversionMode)
    {
        if (ProjectGlobalVariableTypeCompatibility.IsCompatibleWithParameter(variableType, parameterType))
        {
            return true;
        }

        return variableType == ProjectGlobalVariableValueType.Double &&
               IsIntegerParameterType(parameterType) &&
               IsExplicitIntegerConversion(conversionMode);
    }

    public static bool IsCompatibleWithVariableOperatorDataType(
        ProjectGlobalVariableValueType variableType,
        string? dataType,
        ProjectVariableConversionMode conversionMode)
    {
        if (ProjectGlobalVariableTypeCompatibility.IsCompatibleWithVariableOperatorDataType(variableType, dataType))
        {
            return true;
        }

        var normalized = string.IsNullOrWhiteSpace(dataType) ? string.Empty : dataType.Trim();
        return IsExplicitIntegerConversion(conversionMode) &&
               ((variableType == ProjectGlobalVariableValueType.Double && IsIntegerParameterType(normalized)) ||
                (variableType == ProjectGlobalVariableValueType.Int64 && IsNumberParameterType(normalized)));
    }

    public static bool IsCompatibleWithOutputPort(
        ProjectGlobalVariableValueType variableType,
        ClearVision.Product.Core.Enums.PortDataType portType,
        ProjectVariableConversionMode conversionMode)
    {
        if (ProjectGlobalVariableTypeCompatibility.IsCompatibleWithOutputPort(variableType, portType))
        {
            return true;
        }

        return variableType == ProjectGlobalVariableValueType.Int64 &&
               portType == ClearVision.Product.Core.Enums.PortDataType.Float &&
               IsExplicitIntegerConversion(conversionMode);
    }

    public static IReadOnlyDictionary<string, object?> BuildExpressionVariables(
        IProjectVariableSession session,
        object? value)
    {
        var variables = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["value"] = value
        };

        foreach (var snapshot in session.GetSnapshots())
        {
            if (!session.TryGetDefinition(snapshot.VariableId, out var definition) ||
                string.IsNullOrWhiteSpace(definition.Name))
            {
                continue;
            }

            variables[definition.Name] = ProjectVariableValueConverter.ToObject(snapshot.Value);
        }

        return variables;
    }

    private static object? ApplyExpression(
        object? rawValue,
        string? expression,
        IReadOnlyDictionary<string, object?> variables,
        out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(expression))
        {
            return rawValue;
        }

        var context = new Dictionary<string, object?>(variables, StringComparer.OrdinalIgnoreCase)
        {
            ["value"] = rawValue is JsonElement element ? ProjectVariableValueConverter.ToObject(element) : rawValue
        };

        if (!ProjectVariableExpressionEvaluator.TryEvaluate(expression, context, out var result, out error))
        {
            error = $"Expression '{expression}' is invalid: {error}";
            return null;
        }

        return result;
    }

    private static bool TryApplyIntegerConversion(
        object? value,
        ProjectVariableConversionMode conversionMode,
        out long converted,
        out string? error)
    {
        converted = 0;
        error = null;

        if (!TryReadDecimal(value, out var number))
        {
            error = $"Value type '{value?.GetType().Name ?? "null"}' is not numeric.";
            return false;
        }

        if (!IsExplicitIntegerConversion(conversionMode))
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
        number = 0;
        if (value is JsonElement element)
        {
            value = ProjectVariableValueConverter.ToObject(element);
        }

        var ok = value switch
        {
            double doubleValue when double.IsFinite(doubleValue) => TryConvertFiniteDouble(doubleValue, out number),
            float floatValue when float.IsFinite(floatValue) => TryConvertFiniteDouble(floatValue, out number),
            decimal decimalValue => (number = decimalValue) == decimalValue,
            long longValue => (number = longValue) == longValue,
            int intValue => (number = intValue) == intValue,
            short shortValue => (number = shortValue) == shortValue,
            byte byteValue => (number = byteValue) == byteValue,
            string text => decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out number),
            _ => false
        };

        return ok;
    }

    private static bool TryConvertFiniteDouble(double value, out decimal number)
    {
        try
        {
            number = Convert.ToDecimal(value, CultureInfo.InvariantCulture);
            return true;
        }
        catch (OverflowException)
        {
            number = 0;
            return false;
        }
    }

    private static bool IsExplicitIntegerConversion(ProjectVariableConversionMode conversionMode) =>
        conversionMode is ProjectVariableConversionMode.Round
            or ProjectVariableConversionMode.Floor
            or ProjectVariableConversionMode.Ceiling
            or ProjectVariableConversionMode.Truncate;

    private static bool IsIntegerParameterType(string? dataType)
    {
        var normalized = string.IsNullOrWhiteSpace(dataType) ? string.Empty : dataType.Trim();
        return normalized.Equals("int", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("integer", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("long", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("int64", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsNumberParameterType(string? dataType)
    {
        var normalized = string.IsNullOrWhiteSpace(dataType) ? string.Empty : dataType.Trim();
        return normalized.Equals("double", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("float", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("number", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("decimal", StringComparison.OrdinalIgnoreCase);
    }
}
