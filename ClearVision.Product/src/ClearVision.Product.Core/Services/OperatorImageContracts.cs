using System.Reflection;
using ClearVision.Product.Core.Attributes;
using ClearVision.Product.Core.Enums;

namespace ClearVision.Product.Core.Services;

public enum ImageContractStatus
{
    Native,
    Converted,
    Restricted,
    Rejected,
    Unknown
}

public sealed record ImageModeRestriction(
    string Mode,
    ImageContractStatus Status,
    IReadOnlyList<string> SupportedDepths,
    IReadOnlyList<int> SupportedChannels,
    string ConversionPolicy,
    string OutputDepthPolicy,
    string DynamicRangePolicy,
    string FailureCode,
    string EvidenceLevel,
    string? Condition = null);

public sealed record ImageInputContract(
    string InputPort,
    IReadOnlyList<string> SupportedDepths,
    IReadOnlyList<int> SupportedChannels,
    IReadOnlyList<string> NativeDepths,
    string InputDepthPolicy,
    string ImplicitConversionPolicy,
    string OutputDepthPolicy,
    string DynamicRangePolicy,
    IReadOnlyList<ImageModeRestriction> ModeRestrictions,
    string NonFinitePolicy,
    string FailureCode,
    string ContractVersion,
    ImageContractStatus Status,
    string EvidenceLevel);

public interface IOperatorImageContractProvider
{
    IReadOnlyList<ImageInputContract> GetContracts(
        OperatorType operatorType,
        IReadOnlyList<string> imageInputPorts,
        OperatorLifecycle lifecycle);
}

/// <summary>
/// Resolves image contracts from the same provider for scanner and runtime use.
/// Operators without a dedicated provider receive a conservative Stage 2 contract:
/// legacy 8U execution remains available, while higher depths are rejected until
/// operator-specific evidence promotes them.
/// </summary>
public static class OperatorImageContractResolver
{
    public const string ContractVersion = "2.0";

    public static IReadOnlyList<ImageInputContract> Resolve(
        Type operatorClrType,
        OperatorType operatorType)
    {
        ArgumentNullException.ThrowIfNull(operatorClrType);

        var imagePorts = operatorClrType
            .GetCustomAttributes<InputPortAttribute>(inherit: false)
            .Where(attribute => attribute.DataType == PortDataType.Image)
            .Select(attribute => attribute.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (imagePorts.Length == 0)
        {
            return [];
        }

        var lifecycle = operatorClrType
            .GetCustomAttribute<OperatorMetaAttribute>(inherit: false)?
            .Lifecycle ?? OperatorLifecycle.Stable;
        var providerAttribute = operatorClrType
            .GetCustomAttribute<OperatorImageContractProviderAttribute>(inherit: false);
        if (providerAttribute is not null)
        {
            if (!typeof(IOperatorImageContractProvider).IsAssignableFrom(providerAttribute.ProviderType))
            {
                throw new InvalidOperationException(
                    $"Image contract provider '{providerAttribute.ProviderType.FullName}' for '{operatorClrType.FullName}' " +
                    $"must implement {nameof(IOperatorImageContractProvider)}.");
            }

            if (Activator.CreateInstance(providerAttribute.ProviderType) is not IOperatorImageContractProvider provider)
            {
                throw new InvalidOperationException(
                    $"Image contract provider '{providerAttribute.ProviderType.FullName}' could not be created.");
            }

            var resolved = provider.GetContracts(operatorType, imagePorts, lifecycle)
                .Where(contract => contract is not null)
                .ToArray();
            ValidateResolvedContracts(operatorClrType, imagePorts, resolved);
            return resolved;
        }

        return imagePorts
            .Select(port => CreateConservativeDefault(port, lifecycle))
            .ToArray();
    }

    public static ImageInputContract CreateConservativeDefault(
        string inputPort,
        OperatorLifecycle lifecycle)
    {
        var isExperimental = lifecycle == OperatorLifecycle.Experimental;
        return new ImageInputContract(
            inputPort,
            isExperimental ? [] : ["CV_8U"],
            [1, 3, 4],
            isExperimental ? [] : ["CV_8U"],
            isExperimental
                ? "Unverified image depth domain; Unknown is not support."
                : "Stage 2 conservative baseline: retain evidenced legacy 8U paths; reject higher depths until operator-specific evidence is added.",
            "None",
            "Operator-specific legacy output policy; no Stage 2 depth widening.",
            isExperimental
                ? "Undefined until verified."
                : "8-bit native numeric domain; no implicit MinMax conversion.",
            [],
            isExperimental ? "Unknown" : "NotApplicableFor8U",
            "IMAGE_DEPTH_UNSUPPORTED",
            ContractVersion,
            isExperimental ? ImageContractStatus.Unknown : ImageContractStatus.Restricted,
            isExperimental ? "Unknown" : "E0_SOURCE_AUDIT");
    }

    private static void ValidateResolvedContracts(
        Type operatorClrType,
        IReadOnlyList<string> imagePorts,
        IReadOnlyList<ImageInputContract> contracts)
    {
        var declaredPorts = imagePorts.ToHashSet(StringComparer.Ordinal);
        var contractPorts = contracts.Select(contract => contract.InputPort).ToList();
        contractPorts.ShouldContainEachDeclaredPort(operatorClrType, declaredPorts);

        foreach (var contract in contracts)
        {
            if (!declaredPorts.Contains(contract.InputPort))
            {
                throw new InvalidOperationException(
                    $"Image contract for '{operatorClrType.FullName}' references undeclared input port '{contract.InputPort}'.");
            }

            if (string.IsNullOrWhiteSpace(contract.ContractVersion) ||
                string.IsNullOrWhiteSpace(contract.FailureCode) ||
                string.IsNullOrWhiteSpace(contract.EvidenceLevel))
            {
                throw new InvalidOperationException(
                    $"Image contract for '{operatorClrType.FullName}:{contract.InputPort}' is incomplete.");
            }
        }
    }

    private static void ShouldContainEachDeclaredPort(
        this IReadOnlyCollection<string> contractPorts,
        Type operatorClrType,
        IReadOnlySet<string> declaredPorts)
    {
        foreach (var port in declaredPorts)
        {
            if (!contractPorts.Contains(port, StringComparer.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Image contract provider for '{operatorClrType.FullName}' did not describe input port '{port}'.");
            }
        }
    }
}
