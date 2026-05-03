using System.Text.Json;
using Acme.Product.Runtime.Abstractions;

namespace Acme.Product.Runtime;

public sealed class RuntimeParameterValidationResult
{
    public List<string> Errors { get; } = [];

    public bool IsValid => Errors.Count == 0;
}

public static class RuntimeParameterValidator
{
    public static RuntimeParameterValidationResult Validate(RuntimeParameterSchema schema, RuntimeSiteProfile profile)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(profile);

        var result = new RuntimeParameterValidationResult();

        if (!string.Equals(profile.PackageId, schema.PackageId, StringComparison.Ordinal))
        {
            result.Errors.Add($"Profile packageId '{profile.PackageId}' does not match schema packageId '{schema.PackageId}'.");
        }

        if (!string.Equals(profile.FlowHash, schema.FlowHash, StringComparison.OrdinalIgnoreCase))
        {
            result.Errors.Add($"Profile flowHash '{profile.FlowHash}' does not match schema flowHash '{schema.FlowHash}'.");
        }

        var definitionsById = schema.Parameters.ToDictionary(
            definition => definition.Id,
            StringComparer.Ordinal);

        foreach (var parameterOverride in profile.Overrides)
        {
            if (!definitionsById.TryGetValue(parameterOverride.ParameterId, out var definition))
            {
                result.Errors.Add($"Unknown runtime parameter override '{parameterOverride.ParameterId}'.");
                continue;
            }

            ValidateOverrideValue(definition, parameterOverride.Value, result);
        }

        return result;
    }

    public static void ThrowIfInvalid(RuntimeParameterSchema schema, RuntimeSiteProfile profile)
    {
        var validation = Validate(schema, profile);
        if (!validation.IsValid)
        {
            throw new RuntimePackageException(
                "Runtime site profile validation failed: " + string.Join("; ", validation.Errors));
        }
    }

    public static bool TryGetNumber(JsonElement value, out double number)
    {
        number = 0;
        return value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out number);
    }

    private static void ValidateOverrideValue(
        RuntimeParameterDefinition definition,
        JsonElement value,
        RuntimeParameterValidationResult result)
    {
        if (!definition.SiteTunable)
        {
            result.Errors.Add($"Runtime parameter '{definition.Id}' is not site tunable.");
            return;
        }

        if (definition.ValueType != RuntimeParameterValueType.Number ||
            definition.UiKind != RuntimeParameterUiKind.NumericInput)
        {
            result.Errors.Add($"Runtime parameter '{definition.Id}' uses an unsupported V1 parameter type.");
            return;
        }

        if (!TryGetNumber(value, out var number))
        {
            result.Errors.Add($"Runtime parameter '{definition.Id}' value must be a JSON number.");
            return;
        }

        if (definition.Min.HasValue && number < definition.Min.Value)
        {
            result.Errors.Add($"Runtime parameter '{definition.Id}' value {number} is below min {definition.Min.Value}.");
        }

        if (definition.Max.HasValue && number > definition.Max.Value)
        {
            result.Errors.Add($"Runtime parameter '{definition.Id}' value {number} is above max {definition.Max.Value}.");
        }
    }
}
