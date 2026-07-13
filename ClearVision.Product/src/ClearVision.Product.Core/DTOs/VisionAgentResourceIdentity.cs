using System.Text.RegularExpressions;

namespace ClearVision.Product.Core.DTOs;

public static partial class VisionAgentResourceIdentity
{
    public static string NormalizeResourceType(string? value)
    {
        var normalized = NormalizeToken(value);
        if (normalized.Contains("camera", StringComparison.Ordinal)) return "camera_binding";
        if (normalized.Contains("model", StringComparison.Ordinal)) return "model_resource";
        if (normalized.Contains("template", StringComparison.Ordinal)) return "template_artifact";
        if (normalized.Contains("calibration", StringComparison.Ordinal) || normalized.Contains("measurement", StringComparison.Ordinal)) return "calibration_resource";
        if (normalized.Contains("plc", StringComparison.Ordinal)) return "plc_output";
        if (normalized.Contains("output", StringComparison.Ordinal)) return "output_channel";
        return string.IsNullOrWhiteSpace(normalized) ? "resource" : normalized;
    }

    public static string NormalizeParameter(string? value)
    {
        var normalized = NormalizeToken(value);
        return normalized is "cameraid" or "camerabindingid" or "camera_binding_id"
            ? "camera_binding_id"
            : normalized;
    }

    public static string OperatorKey(string? operatorType, int operatorIndex)
    {
        var normalizedType = NormalizeToken(operatorType);
        return string.IsNullOrWhiteSpace(normalizedType)
            ? string.Empty
            : $"{normalizedType}#{Math.Max(0, operatorIndex) + 1}";
    }

    public static string CreateCanonicalId(
        string? resourceType,
        string? operatorKey,
        string? parameterName,
        string? fallbackScope = null)
    {
        var type = NormalizeResourceType(resourceType);
        var op = NormalizeOperatorKey(operatorKey);
        var parameter = NormalizeParameter(parameterName);
        var scope = NormalizeToken(fallbackScope);
        return $"resource:v1|{type}|{FirstNonBlank(op, scope, "global")}|{FirstNonBlank(parameter, "resource")}";
    }

    public static IReadOnlyList<string> BuildAliases(params string?[] values)
    {
        return values
            .SelectMany(value => new[] { value?.Trim() ?? string.Empty, NormalizeToken(value) })
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static string NormalizeToken(string? value)
    {
        var text = (value ?? string.Empty).Trim().ToLowerInvariant();
        return UnsafeTokenRegex().Replace(text, string.Empty);
    }

    private static string NormalizeOperatorKey(string? value)
    {
        var text = (value ?? string.Empty).Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var hash = text.LastIndexOf('#');
        if (hash > 0 && int.TryParse(text[(hash + 1)..], out var ordinal))
        {
            return $"{NormalizeToken(text[..hash])}#{Math.Max(1, ordinal)}";
        }
        return NormalizeToken(text);
    }

    private static string FirstNonBlank(params string[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;

    [GeneratedRegex("[^a-z0-9_]+", RegexOptions.CultureInvariant)]
    private static partial Regex UnsafeTokenRegex();
}
