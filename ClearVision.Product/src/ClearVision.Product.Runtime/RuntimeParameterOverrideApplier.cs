using System.Text.Json;
using ClearVision.Product.Application.DTOs;
using ClearVision.Product.Runtime.Abstractions;

namespace ClearVision.Product.Runtime;

public sealed class RuntimeParameterOverrideApplyResult
{
    public required OperatorFlowDto Flow { get; init; }

    public int AppliedOverrideCount { get; init; }
}

public static class RuntimeParameterOverrideApplier
{
    public static RuntimeParameterOverrideApplyResult CloneAndApply(
        RuntimePackage package,
        RuntimeSiteProfile? profile)
    {
        ArgumentNullException.ThrowIfNull(package);

        var activeProfile = profile ?? package.DefaultSiteProfile;
        RuntimeParameterValidator.ThrowIfInvalid(package.ParameterSchema, activeProfile);

        var flowClone = CloneFlow(package.Flow);
        if (activeProfile.Overrides.Count == 0)
        {
            return new RuntimeParameterOverrideApplyResult
            {
                Flow = flowClone,
                AppliedOverrideCount = 0
            };
        }

        var definitionsById = package.ParameterSchema.Parameters.ToDictionary(
            definition => definition.Id,
            StringComparer.Ordinal);

        var operatorsById = flowClone.Operators.ToDictionary(op => op.Id);
        var appliedCount = 0;

        foreach (var parameterOverride in activeProfile.Overrides)
        {
            var definition = definitionsById[parameterOverride.ParameterId];
            if (!RuntimeParameterValidator.TryGetNumber(parameterOverride.Value, out var number))
            {
                throw new RuntimePackageException($"Runtime parameter '{definition.Id}' value must be a JSON number.");
            }

            if (!operatorsById.TryGetValue(definition.OperatorId, out var op))
            {
                throw new RuntimePackageException($"Runtime parameter '{definition.Id}' references a missing operator.");
            }

            var parameter = op.Parameters.FirstOrDefault(item =>
                item.Name.Equals(definition.ParameterName, StringComparison.Ordinal));
            if (parameter == null)
            {
                throw new RuntimePackageException($"Runtime parameter '{definition.Id}' references a missing operator parameter.");
            }

            parameter.Value = number;
            appliedCount += 1;
        }

        return new RuntimeParameterOverrideApplyResult
        {
            Flow = flowClone,
            AppliedOverrideCount = appliedCount
        };
    }

    public static RuntimeSiteProfile CloneProfile(RuntimeSiteProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var bytes = JsonSerializer.SerializeToUtf8Bytes(profile, RuntimeJson.SerializerOptions);
        return JsonSerializer.Deserialize<RuntimeSiteProfile>(bytes, RuntimeJson.SerializerOptions)
            ?? throw new RuntimePackageException("Unable to clone runtime site profile.");
    }

    private static OperatorFlowDto CloneFlow(OperatorFlowDto flow)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(flow, RuntimeJson.SerializerOptions);
        return JsonSerializer.Deserialize<OperatorFlowDto>(bytes, RuntimeJson.SerializerOptions)
            ?? throw new RuntimePackageException("Unable to clone runtime flow.");
    }
}
