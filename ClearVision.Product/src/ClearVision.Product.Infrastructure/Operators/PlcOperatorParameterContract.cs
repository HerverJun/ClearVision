using System.Globalization;

namespace ClearVision.Product.Infrastructure.Operators;

internal static class PlcOperatorParameterContract
{
    internal static readonly string[] SupportedOperations = ["Read", "Write"];

    internal static readonly string[] SupportedPollingModes = ["None", "WaitForValue"];

    internal static readonly string[] SupportedPollingConditions =
    [
        "Equal",
        "NotEqual",
        "GreaterThan",
        "LessThan",
        "GreaterOrEqual",
        "LessOrEqual"
    ];

    internal static bool IsSupportedOperation(string? value) =>
        Contains(SupportedOperations, value);

    internal static bool IsRead(string? value) =>
        string.Equals(value, "Read", StringComparison.OrdinalIgnoreCase);

    internal static bool IsWrite(string? value) =>
        string.Equals(value, "Write", StringComparison.OrdinalIgnoreCase);

    internal static bool IsSupportedPollingMode(string? value) =>
        Contains(SupportedPollingModes, value);

    internal static bool IsWaitForValue(string? value) =>
        string.Equals(value, "WaitForValue", StringComparison.OrdinalIgnoreCase);

    internal static bool IsSupportedPollingCondition(string? value) =>
        Contains(SupportedPollingConditions, value);

    internal static bool EvaluatePollingCondition(
        object? currentValue,
        string condition,
        string targetValue)
    {
        var currentText = Convert.ToString(currentValue, CultureInfo.InvariantCulture) ?? string.Empty;
        var currentIsNumeric = double.TryParse(
            currentText,
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out var currentNumber);
        var targetIsNumeric = double.TryParse(
            targetValue,
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out var targetNumber);

        if (condition == "Equal")
        {
            return currentIsNumeric && targetIsNumeric
                ? Math.Abs(currentNumber - targetNumber) < 0.0001d
                : currentText.Equals(targetValue, StringComparison.OrdinalIgnoreCase);
        }

        if (condition == "NotEqual")
        {
            return currentIsNumeric && targetIsNumeric
                ? Math.Abs(currentNumber - targetNumber) >= 0.0001d
                : !currentText.Equals(targetValue, StringComparison.OrdinalIgnoreCase);
        }

        if (!currentIsNumeric || !targetIsNumeric)
        {
            return false;
        }

        return condition switch
        {
            "GreaterThan" => currentNumber > targetNumber,
            "LessThan" => currentNumber < targetNumber,
            "GreaterOrEqual" => currentNumber >= targetNumber,
            "LessOrEqual" => currentNumber <= targetNumber,
            _ => false
        };
    }

    private static bool Contains(IEnumerable<string> supportedValues, string? value) =>
        value is not null && supportedValues.Contains(value, StringComparer.Ordinal);
}
