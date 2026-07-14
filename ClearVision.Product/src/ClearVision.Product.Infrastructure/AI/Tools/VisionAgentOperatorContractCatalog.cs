using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Services;
using ClearVision.Product.Core.ValueObjects;
using ClearVision.Product.Infrastructure.Services;

namespace ClearVision.Product.Infrastructure.AI.Tools;

internal interface IVisionAgentOperatorContractCatalog
{
    IReadOnlyCollection<string> OperatorTypes { get; }

    IReadOnlyCollection<VisionAgentOperatorContract> Operators { get; }

    bool TryGet(string operatorType, out VisionAgentOperatorContract contract);

    string CanonicalizeOperatorType(string operatorType);
}

internal sealed class VisionAgentOperatorContractCatalog : IVisionAgentOperatorContractCatalog
{
    private static readonly IReadOnlyDictionary<string, string> LegacyAgentAliases =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["MeasureDistance"] = nameof(OperatorType.Measurement),
            ["TemplateMatch"] = nameof(OperatorType.TemplateMatching)
        };

    private readonly IReadOnlyDictionary<string, VisionAgentOperatorContract> _contracts;

    public VisionAgentOperatorContractCatalog()
        : this(new OperatorFactory())
    {
    }

    public VisionAgentOperatorContractCatalog(IOperatorFactory operatorFactory)
    {
        var contracts = operatorFactory.GetAllMetadata()
            .Select(ToContract)
            .GroupBy(item => item.OperatorType, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        _contracts = contracts;
    }

    public IReadOnlyCollection<string> OperatorTypes => _contracts.Keys.ToList();

    public IReadOnlyCollection<VisionAgentOperatorContract> Operators => _contracts.Values.ToList();

    public bool TryGet(string operatorType, out VisionAgentOperatorContract contract)
    {
        var canonical = CanonicalizeOperatorType(operatorType);
        return _contracts.TryGetValue(canonical, out contract!);
    }

    public string CanonicalizeOperatorType(string operatorType)
    {
        var cleaned = string.IsNullOrWhiteSpace(operatorType) ? string.Empty : operatorType.Trim();
        if (string.IsNullOrWhiteSpace(cleaned))
        {
            return string.Empty;
        }

        if (LegacyAgentAliases.TryGetValue(cleaned, out var alias))
        {
            return alias;
        }

        if (Enum.TryParse<OperatorType>(cleaned, ignoreCase: true, out var parsed))
        {
            return OperatorTypeAliasResolver.Resolve(parsed).ToString();
        }

        return cleaned;
    }

    private static VisionAgentOperatorContract ToContract(OperatorMetadata metadata)
    {
        return new VisionAgentOperatorContract(
            metadata.Type.ToString(),
            metadata.DisplayName,
            metadata.CategoryId,
            OperatorCategoryCatalog.GetOrder(metadata.CategoryId),
            metadata.Category,
            metadata.Description,
            metadata.Lifecycle,
            metadata.LifecycleNote ?? string.Empty,
            metadata.DefaultHidden,
            OperatorLifecyclePolicy.IsDefaultAiRecommendation(metadata.Lifecycle),
            OperatorLifecyclePolicy.RequiresDisclosure(metadata.Lifecycle),
            metadata.InputPorts.Select(ToPort).ToList(),
            metadata.OutputPorts.Select(ToPort).ToList(),
            metadata.Parameters.Select(ToParameter).ToList(),
            metadata.ParameterConstraints.ToList(),
            metadata.OutputAvailabilityRules.ToList(),
            metadata.ImageInputContracts.ToList(),
            (metadata.Keywords ?? Array.Empty<string>())
                .Where(keyword => !string.IsNullOrWhiteSpace(keyword))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            (metadata.Tags ?? Array.Empty<string>())
                .Where(tag => !string.IsNullOrWhiteSpace(tag))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            metadata);
    }

    private static VisionAgentPortContract ToPort(PortDefinition port)
    {
        return new VisionAgentPortContract(
            port.Name,
            port.DisplayName,
            port.DataType,
            port.IsRequired,
            port.Description ?? string.Empty);
    }

    private static VisionAgentParameterContract ToParameter(ParameterDefinition parameter)
    {
        return new VisionAgentParameterContract(
            parameter.Name,
            parameter.DisplayName,
            parameter.DataType,
            parameter.IsRequired,
            parameter.DefaultValue,
            parameter.MinValue,
            parameter.MaxValue,
            parameter.Description ?? string.Empty,
            parameter.Options?
                .Select(option => new ParameterOption
                {
                    Label = option.Label,
                    Value = option.Value
                })
                .ToList());
    }

}

internal sealed record VisionAgentOperatorContract(
    string OperatorType,
    string DisplayName,
    OperatorCategoryId CategoryId,
    int CategoryOrder,
    string Category,
    string Description,
    OperatorLifecycle Lifecycle,
    string LifecycleNote,
    bool DefaultHidden,
    bool DefaultAiRecommendation,
    bool RequiresLifecycleDisclosure,
    IReadOnlyList<VisionAgentPortContract> InputPorts,
    IReadOnlyList<VisionAgentPortContract> OutputPorts,
    IReadOnlyList<VisionAgentParameterContract> Parameters,
    IReadOnlyList<OperatorParameterConstraint> ParameterConstraints,
    IReadOnlyList<OperatorOutputAvailabilityRule> OutputAvailabilityRules,
    IReadOnlyList<ImageInputContract> ImageInputContracts,
    IReadOnlyList<string> Keywords,
    IReadOnlyList<string> Tags,
    OperatorMetadata Metadata);

internal sealed record VisionAgentPortContract(
    string Name,
    string DisplayName,
    PortDataType DataType,
    bool IsRequired,
    string Description);

internal sealed record VisionAgentParameterContract(
    string Name,
    string DisplayName,
    string DataType,
    bool IsRequired,
    object? DefaultValue,
    object? MinValue,
    object? MaxValue,
    string Description,
    IReadOnlyList<ParameterOption>? Options);
