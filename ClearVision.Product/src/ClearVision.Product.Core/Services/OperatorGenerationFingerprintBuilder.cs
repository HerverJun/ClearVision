using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace ClearVision.Product.Core.Services;

/// <summary>
/// Builds the deterministic fingerprint used by generated operator artifacts.
/// The fingerprint covers the final runtime metadata, the operator source and
/// only the shared source files explicitly declared by the operator.
/// </summary>
public static class OperatorGenerationFingerprintBuilder
{
    public const string SchemeVersion = "operator-runtime-metadata-v2";

    private static readonly JsonSerializerOptions CanonicalJson = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public static string Compute(
        OperatorMetadata metadata,
        string operatorSource,
        IReadOnlyDictionary<string, string>? dependencySources = null)
    {
        ArgumentNullException.ThrowIfNull(metadata);

        var declaredDependencies = metadata.GenerationDependencies
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item.Trim())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(item => item, StringComparer.Ordinal)
            .ToArray();

        var resolvedDependencies = new List<object>(declaredDependencies.Length);
        foreach (var dependency in declaredDependencies)
        {
            if (dependencySources is null || !dependencySources.TryGetValue(dependency, out var source))
            {
                throw new InvalidOperationException(
                    $"Generation dependency '{dependency}' for operator '{metadata.Type}' was not resolved.");
            }

            resolvedDependencies.Add(new
            {
                Id = dependency,
                Source = NormalizeText(source)
            });
        }

        var snapshot = new
        {
            SchemeVersion,
            Metadata = new
            {
                Type = metadata.Type.ToString(),
                NumericType = (int)metadata.Type,
                metadata.DisplayName,
                metadata.Description,
                CategoryId = metadata.CategoryId.ToString(),
                metadata.Category,
                Lifecycle = metadata.Lifecycle.ToString(),
                metadata.LifecycleNote,
                metadata.IconName,
                Keywords = NormalizeSet(metadata.Keywords),
                Tags = NormalizeSet(metadata.Tags),
                metadata.Version,
                Inputs = metadata.InputPorts.Select(port => new
                {
                    port.Name,
                    port.DisplayName,
                    DataType = port.DataType.ToString(),
                    port.IsRequired,
                    port.Description
                }),
                Outputs = metadata.OutputPorts.Select(port => new
                {
                    port.Name,
                    port.DisplayName,
                    DataType = port.DataType.ToString(),
                    port.IsRequired,
                    port.Description
                }),
                Parameters = metadata.Parameters.Select(parameter => new
                {
                    parameter.Name,
                    parameter.DisplayName,
                    parameter.Description,
                    parameter.DataType,
                    DefaultValue = NormalizeValue(parameter.DefaultValue),
                    MinValue = NormalizeValue(parameter.MinValue),
                    MaxValue = NormalizeValue(parameter.MaxValue),
                    parameter.IsRequired,
                    Options = parameter.Options?.Select(option => new
                    {
                        option.Value,
                        option.Label
                    })
                }),
                ParameterConditions = metadata.ParameterConstraints
                    .OrderBy(item => item.Parameter, StringComparer.Ordinal)
                    .ThenBy(item => item.ReasonCode, StringComparer.Ordinal)
                    .Select(NormalizeConstraint),
                OutputConditions = metadata.OutputAvailabilityRules
                    .OrderBy(item => item.Output, StringComparer.Ordinal)
                    .ThenBy(item => item.ReasonCode, StringComparer.Ordinal)
                    .Select(rule => new
                    {
                        rule.Output,
                        AvailableWhen = NormalizeConditionSet(rule.AvailableWhen),
                        rule.ReasonCode
                    }),
                GenerationDependencies = declaredDependencies
            },
            OperatorSource = NormalizeText(operatorSource ?? string.Empty),
            DependencySources = resolvedDependencies
        };

        var canonical = JsonSerializer.Serialize(snapshot, CanonicalJson);
        var bytes = Encoding.UTF8.GetBytes(canonical);
        return Convert.ToHexString(SHA256.HashData(bytes));
    }

    private static object NormalizeConstraint(OperatorParameterConstraint constraint) => new
    {
        constraint.Parameter,
        constraint.RequiredPolicy,
        RequiredWhen = NormalizeConditionSet(constraint.RequiredWhen),
        EnabledWhen = NormalizeConditionSet(constraint.EnabledWhen),
        DisabledWhen = NormalizeConditionSet(constraint.DisabledWhen),
        VisibleWhen = NormalizeConditionSet(constraint.VisibleWhen),
        HiddenWhen = NormalizeConditionSet(constraint.HiddenWhen),
        IgnoredWhen = NormalizeConditionSet(constraint.IgnoredWhen),
        constraint.AtLeastOneGroup,
        constraint.MutuallyExclusiveGroup,
        constraint.AliasFor,
        constraint.Deprecated,
        constraint.ResourceKind,
        constraint.ReasonCode,
        SatisfiedByInputPorts = constraint.SatisfiedByInputPorts?
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
    };

    private static object? NormalizeConditionSet(OperatorParameterConditionSet? conditionSet)
    {
        if (conditionSet is null)
        {
            return null;
        }

        return new
        {
            All = conditionSet.All?.Select(NormalizeCondition),
            Any = conditionSet.Any?.Select(NormalizeCondition)
        };
    }

    private static object NormalizeCondition(OperatorParameterCondition condition) => new
    {
        condition.Parameter,
        condition.Comparison,
        Value = NormalizeValue(condition.Value)
    };

    private static string[] NormalizeSet(IEnumerable<string>? values) =>
        values?
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
            .ToArray() ?? [];

    private static object? NormalizeValue(object? value)
    {
        if (value is null)
        {
            return null;
        }

        var text = value switch
        {
            bool boolean => boolean ? "true" : "false",
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => Convert.ToString(value, CultureInfo.InvariantCulture)
        };

        return new
        {
            Type = value.GetType().FullName ?? value.GetType().Name,
            Value = text ?? string.Empty
        };
    }

    private static string NormalizeText(string value) =>
        value.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
}
