// OperatorMetadataScanner.cs
// 算子元数据扫描器
// 通过反射扫描算子定义并构建元数据索引
// 作者：蘅芜君
using System.Reflection;
using System.Runtime.CompilerServices;
using ClearVision.Product.Core.Attributes;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Services;
using ClearVision.Product.Core.ValueObjects;
using ClearVision.Product.Infrastructure.Operators;
using Microsoft.Extensions.Logging;

namespace ClearVision.Product.Infrastructure.Services;

/// <summary>
/// Scans operator classes decorated by metadata attributes and converts them
/// to the runtime <see cref="OperatorMetadata"/> model.
/// </summary>
public class OperatorMetadataScanner
{
    private readonly ILogger<OperatorMetadataScanner>? _logger;

    public OperatorMetadataScanner(ILogger<OperatorMetadataScanner>? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// Scan the infrastructure operator assembly.
    /// </summary>
    public List<OperatorMetadata> Scan()
    {
        return Scan(new[] { typeof(OperatorBase).Assembly });
    }

    /// <summary>
    /// Scan one assembly for operator metadata.
    /// </summary>
    public List<OperatorMetadata> Scan(Assembly assembly)
    {
        if (assembly == null)
        {
            throw new ArgumentNullException(nameof(assembly));
        }

        return Scan(new[] { assembly });
    }

    /// <summary>
    /// Scan multiple assemblies for operator metadata.
    /// </summary>
    public List<OperatorMetadata> Scan(IEnumerable<Assembly> assemblies)
    {
        if (assemblies == null)
        {
            throw new ArgumentNullException(nameof(assemblies));
        }

        var metadataByType = new Dictionary<OperatorType, OperatorMetadata>();
        foreach (var assembly in assemblies.Where(a => a != null).Distinct())
        {
            foreach (var operatorClrType in GetCandidateOperatorTypes(assembly))
            {
                var metadata = TryBuildMetadata(operatorClrType);
                if (metadata == null)
                {
                    continue;
                }

                if (!metadataByType.TryAdd(metadata.Type, metadata))
                {
                    _logger?.LogWarning(
                        "Duplicate operator metadata scanned for {OperatorType}. Existing={ExistingType}, Ignored={IgnoredType}",
                        metadata.Type,
                        metadataByType[metadata.Type].DisplayName,
                        operatorClrType.FullName);
                }
            }
        }

        return metadataByType.Values
            .OrderBy(m => m.Type)
            .ToList();
    }

    private static IEnumerable<Type> GetCandidateOperatorTypes(Assembly assembly)
    {
        return GetLoadableTypes(assembly)
            .Where(t => t.IsClass && !t.IsAbstract)
            .Where(t => typeof(OperatorBase).IsAssignableFrom(t))
            .Where(t => t.GetCustomAttribute<OperatorMetaAttribute>(inherit: false) != null);
    }

    private static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(t => t != null)!;
        }
    }

    private OperatorMetadata? TryBuildMetadata(Type operatorClrType)
    {
        var operatorMeta = operatorClrType.GetCustomAttribute<OperatorMetaAttribute>(inherit: false);
        if (operatorMeta == null)
        {
            return null;
        }

        if (!TryResolveOperatorType(operatorClrType, out var operatorType))
        {
            _logger?.LogWarning(
                "Cannot resolve OperatorType from {OperatorClass}; skipped metadata scan.",
                operatorClrType.FullName);
            return null;
        }

        var metadata = new OperatorMetadata
        {
            Type = operatorType,
            DisplayName = operatorMeta.DisplayName,
            Description = operatorMeta.Description,
            CategoryId = operatorMeta.CategoryId,
            Category = OperatorCategoryCatalog.GetDisplayName(operatorMeta.CategoryId),
            Lifecycle = operatorMeta.Lifecycle,
            LifecycleNote = operatorMeta.LifecycleNote,
            IconName = operatorMeta.IconName,
            Keywords = operatorMeta.Keywords,
            Tags = operatorMeta.Tags,
            Version = string.IsNullOrWhiteSpace(operatorMeta.Version) ? "1.0.0" : operatorMeta.Version.Trim(),
            QualityState = OperatorQualityStateCatalog.Resolve(operatorType, operatorMeta.Lifecycle),
            InputPorts = operatorClrType
                .GetCustomAttributes<InputPortAttribute>(inherit: false)
                .Select(attr => new PortDefinition
                {
                    Name = attr.Name,
                    DisplayName = attr.DisplayName,
                    DataType = attr.DataType,
                    IsRequired = attr.IsRequired,
                    Description = attr.Description
                })
                .ToList(),
            OutputPorts = operatorClrType
                .GetCustomAttributes<OutputPortAttribute>(inherit: false)
                .Select(attr => new PortDefinition
                {
                    Name = attr.Name,
                    DisplayName = attr.DisplayName,
                    DataType = attr.DataType,
                    Description = attr.Description
                })
                .ToList(),
            Parameters = operatorClrType
                .GetCustomAttributes<OperatorParamAttribute>(inherit: false)
                .Select(attr => new ParameterDefinition
                {
                    Name = attr.Name,
                    DisplayName = attr.DisplayName,
                    Description = attr.Description,
                    DataType = attr.DataType,
                    DefaultValue = attr.DefaultValue,
                    MinValue = attr.Min,
                    MaxValue = attr.Max,
                    IsRequired = attr.IsRequired,
                    Options = BuildOptions(attr.Options)
                })
                .ToList(),
            ParameterConstraints = operatorClrType
                .GetCustomAttributes<OperatorParameterRuleAttribute>(inherit: false)
                .Select(BuildParameterConstraint)
                .ToList(),
            OutputAvailabilityRules = operatorClrType
                .GetCustomAttributes<OperatorOutputRuleAttribute>(inherit: false)
                .Select(BuildOutputRule)
                .ToList(),
            GenerationDependencies = operatorClrType
                .GetCustomAttributes<OperatorGenerationDependencyAttribute>(inherit: false)
                .Select(ResolveGenerationDependency)
                .Concat(operatorClrType
                    .GetCustomAttributes<OperatorImageContractProviderAttribute>(inherit: false)
                    .Select(attribute => $"type:{attribute.ProviderType.FullName}"))
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Distinct(StringComparer.Ordinal)
                .ToList(),
            ImageInputContracts = OperatorImageContractResolver
                .Resolve(operatorClrType, operatorType)
                .ToList()
        };

        return metadata;
    }

    private static bool TryResolveOperatorType(Type operatorClrType, out OperatorType operatorType)
    {
        operatorType = default;

        var property = operatorClrType.GetProperty(nameof(OperatorBase.OperatorType), BindingFlags.Public | BindingFlags.Instance);
        if (property?.PropertyType == typeof(OperatorType) && property.GetMethod != null)
        {
            try
            {
                var uninitialized = RuntimeHelpers.GetUninitializedObject(operatorClrType);
                var value = property.GetValue(uninitialized);
                if (value is OperatorType resolvedType)
                {
                    operatorType = resolvedType;
                    return true;
                }
            }
            catch
            {
                // Fall back to class-name parsing.
            }
        }

        const string suffix = "Operator";
        var className = operatorClrType.Name;
        if (className.EndsWith(suffix, StringComparison.Ordinal))
        {
            className = className[..^suffix.Length];
        }

        return Enum.TryParse(className, out operatorType);
    }

    private static List<ParameterOption>? BuildOptions(string[]? options)
    {
        if (options == null || options.Length == 0)
        {
            return null;
        }

        var result = new List<ParameterOption>(options.Length);
        foreach (var option in options)
        {
            if (string.IsNullOrWhiteSpace(option))
            {
                continue;
            }

            var parts = option.Split('|', 2, StringSplitOptions.TrimEntries);
            var value = parts[0];
            var label = parts.Length > 1 ? parts[1] : parts[0];

            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            result.Add(new ParameterOption
            {
                Value = value,
                Label = string.IsNullOrWhiteSpace(label) ? value : label
            });
        }

        return result.Count == 0 ? null : result;
    }

    private static OperatorParameterConstraint BuildParameterConstraint(OperatorParameterRuleAttribute attribute)
    {
        return new OperatorParameterConstraint(
            attribute.Parameter,
            attribute.RequiredPolicy switch
            {
                OperatorParameterRequiredPolicy.Required => OperatorParameterRequiredPolicies.Required,
                OperatorParameterRequiredPolicy.Optional => OperatorParameterRequiredPolicies.Optional,
                _ => OperatorParameterRequiredPolicies.Metadata
            },
            BuildConditionSet(attribute.RequiredWhenAll, attribute.RequiredWhenAny),
            BuildConditionSet(attribute.EnabledWhenAll, attribute.EnabledWhenAny),
            BuildConditionSet(attribute.DisabledWhenAll, attribute.DisabledWhenAny),
            attribute.AtLeastOneGroup,
            attribute.MutuallyExclusiveGroup,
            attribute.AliasFor,
            attribute.Deprecated,
            ResolveResourceKind(attribute.ResourceKind),
            attribute.ReasonCode,
            BuildConditionSet(attribute.VisibleWhenAll, attribute.VisibleWhenAny),
            BuildConditionSet(attribute.HiddenWhenAll, attribute.HiddenWhenAny),
            BuildConditionSet(attribute.IgnoredWhenAll, attribute.IgnoredWhenAny),
            attribute.SatisfiedByInputPorts?
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => name.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray());
    }

    private static OperatorOutputAvailabilityRule BuildOutputRule(OperatorOutputRuleAttribute attribute)
    {
        return new OperatorOutputAvailabilityRule(
            attribute.Output,
            BuildConditionSet(attribute.AvailableWhenAll, attribute.AvailableWhenAny),
            attribute.ReasonCode);
    }

    private static OperatorParameterConditionSet? BuildConditionSet(string[]? all, string[]? any)
    {
        var allConditions = ParseConditions(all);
        var anyConditions = ParseConditions(any);
        return allConditions.Count == 0 && anyConditions.Count == 0
            ? null
            : new OperatorParameterConditionSet(
                allConditions.Count == 0 ? null : allConditions,
                anyConditions.Count == 0 ? null : anyConditions);
    }

    private static IReadOnlyList<OperatorParameterCondition> ParseConditions(string[]? expressions)
    {
        if (expressions is null || expressions.Length == 0)
        {
            return [];
        }

        return expressions.Select(ParseCondition).ToArray();
    }

    private static OperatorParameterCondition ParseCondition(string expression)
    {
        var value = expression?.Trim() ?? string.Empty;
        if (value.EndsWith(":not-empty", StringComparison.OrdinalIgnoreCase))
        {
            return new OperatorParameterCondition(
                value[..^":not-empty".Length].Trim(),
                OperatorParameterConditionComparisons.NotEmpty);
        }

        if (value.EndsWith(":empty", StringComparison.OrdinalIgnoreCase))
        {
            return new OperatorParameterCondition(
                value[..^":empty".Length].Trim(),
                OperatorParameterConditionComparisons.Empty);
        }

        var comparisonIndex = value.IndexOf("!=", StringComparison.Ordinal);
        var comparison = OperatorParameterConditionComparisons.NotEquals;
        var separatorLength = 2;
        if (comparisonIndex < 0)
        {
            comparisonIndex = value.IndexOf("==", StringComparison.Ordinal);
            comparison = OperatorParameterConditionComparisons.Equal;
        }

        if (comparisonIndex <= 0)
        {
            throw new InvalidOperationException($"Invalid operator condition expression '{expression}'.");
        }

        var parameter = value[..comparisonIndex].Trim();
        var rawExpected = value[(comparisonIndex + separatorLength)..].Trim();
        object expected = bool.TryParse(rawExpected, out var boolean)
            ? boolean
            : rawExpected;
        return new OperatorParameterCondition(parameter, comparison, expected);
    }

    private static string? ResolveResourceKind(OperatorResourceKind resourceKind) => resourceKind switch
    {
        OperatorResourceKind.None => null,
        OperatorResourceKind.ImageFile => "image_file",
        OperatorResourceKind.CameraBinding => "camera_binding",
        OperatorResourceKind.TemplateResource => "template_resource",
        OperatorResourceKind.ModelResource => "model_resource",
        OperatorResourceKind.ModelCatalog => "model_catalog",
        OperatorResourceKind.ModelLabels => "model_labels",
        OperatorResourceKind.FeatureBank => "feature_bank",
        OperatorResourceKind.OutputFile => "output_file",
        OperatorResourceKind.PlcEndpoint => "plc_endpoint",
        OperatorResourceKind.PlcAddress => "plc_address",
        OperatorResourceKind.TcpProfile => "tcp_profile",
        OperatorResourceKind.NetworkEndpoint => "network_endpoint",
        _ => throw new ArgumentOutOfRangeException(nameof(resourceKind), resourceKind, null)
    };

    private static string ResolveGenerationDependency(OperatorGenerationDependencyAttribute attribute)
    {
        if (attribute.DependencyType is not null)
        {
            return $"type:{attribute.DependencyType.FullName}";
        }

        return string.IsNullOrWhiteSpace(attribute.SourcePath)
            ? string.Empty
            : $"source:{attribute.SourcePath.Trim().Replace('\\', '/')}";
    }
}
