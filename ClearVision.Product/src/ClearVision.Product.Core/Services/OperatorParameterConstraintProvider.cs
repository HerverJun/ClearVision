using System.Collections.ObjectModel;
using ClearVision.Product.Core.Enums;

namespace ClearVision.Product.Core.Services;

public static class OperatorParameterRequiredPolicies
{
    public const string Metadata = "metadata";
    public const string Required = "required";
    public const string Optional = "optional";
}
public static class OperatorParameterConditionComparisons
{
    public const string Equal = "equals";
    public const string NotEquals = "not-equals";
    public const string Empty = "empty";
    public const string NotEmpty = "not-empty";
}

public sealed record OperatorParameterCondition(
    string Parameter,
    string Comparison,
    object? Value = null);

public sealed record OperatorParameterConditionSet(
    IReadOnlyList<OperatorParameterCondition>? All = null,
    IReadOnlyList<OperatorParameterCondition>? Any = null);

public sealed record OperatorParameterConstraint(
    string Parameter,
    string RequiredPolicy,
    OperatorParameterConditionSet? RequiredWhen,
    OperatorParameterConditionSet? EnabledWhen,
    OperatorParameterConditionSet? DisabledWhen,
    string? AtLeastOneGroup,
    string? MutuallyExclusiveGroup,
    string? AliasFor,
    bool Deprecated,
    string? ResourceKind,
    string ReasonCode,
    OperatorParameterConditionSet? VisibleWhen = null,
    OperatorParameterConditionSet? HiddenWhen = null,
    OperatorParameterConditionSet? IgnoredWhen = null,
    IReadOnlyList<string>? SatisfiedByInputPorts = null);

public sealed record OperatorOutputAvailabilityRule(
    string Output,
    OperatorParameterConditionSet? AvailableWhen,
    string ReasonCode);

public sealed record OperatorParameterConstraintState(
    OperatorParameterConstraint Constraint,
    bool EffectiveRequired,
    bool EffectiveDisabled,
    bool EffectiveVisible = true,
    bool EffectiveIgnored = false);

public sealed record OperatorOutputAvailabilityState(
    OperatorOutputAvailabilityRule? Rule,
    string Output,
    bool IsAvailable,
    bool IsGuaranteed,
    string ReasonCode);

public sealed record OperatorParameterConstraintViolation(
    string Code,
    IReadOnlyList<string> ParameterNames,
    string? ResourceKind,
    string ReasonCode);

public sealed record OperatorParameterAliasDiagnostic(
    string Code,
    string CanonicalParameter,
    string AliasParameter,
    string Message);

public sealed record OperatorParameterCanonicalizationResult(
    IReadOnlyDictionary<string, object?> EffectiveValues,
    IReadOnlyDictionary<string, object?> ExplicitValues,
    IReadOnlyList<OperatorParameterAliasDiagnostic> Diagnostics);

public static class OperatorParameterValueSemantics
{
    private const string PendingPrefix = "<pending-";

    public static bool IsPendingSentinel(object? value)
    {
        var text = value?.ToString()?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        if (text.Equals("<pending>", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!text.StartsWith(PendingPrefix, StringComparison.OrdinalIgnoreCase) ||
            !text.EndsWith('>'))
        {
            return false;
        }

        var payloadLength = text.Length - PendingPrefix.Length - 1;
        if (payloadLength <= 0)
        {
            return false;
        }

        for (var index = PendingPrefix.Length; index < text.Length - 1; index++)
        {
            if (char.IsWhiteSpace(text[index]) || text[index] is '<' or '>')
            {
                return false;
            }
        }

        return true;
    }

    public static bool IsMissing(object? value)
    {
        if (value is null)
        {
            return true;
        }

        var text = value.ToString();
        return string.IsNullOrWhiteSpace(text) || IsPendingSentinel(text);
    }
}

public static class OperatorParameterConstraintEvaluator
{
    public static IReadOnlyList<OperatorParameterConstraintState> ResolveStates(
        OperatorMetadata metadata,
        IReadOnlyDictionary<string, object?> values,
        IReadOnlySet<string>? explicitParameterNames = null)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        var normalizedValues = Canonicalize(metadata, values, explicitParameterNames).EffectiveValues;
        return ResolveStatesCore(metadata, normalizedValues);
    }

    private static IReadOnlyList<OperatorParameterConstraintState> ResolveStatesCore(
        OperatorMetadata metadata,
        IReadOnlyDictionary<string, object?> normalizedValues)
    {
        var metadataByName = metadata.Parameters.ToDictionary(item => item.Name, StringComparer.OrdinalIgnoreCase);

        var states = metadata.ParameterConstraints
            .Select(constraint =>
            {
                var rawRequired = metadataByName.TryGetValue(constraint.Parameter, out var parameter) && parameter.IsRequired;
                var required = constraint.RequiredPolicy switch
                {
                    OperatorParameterRequiredPolicies.Required => true,
                    OperatorParameterRequiredPolicies.Optional => false,
                    _ => rawRequired
                };

                if (constraint.RequiredWhen is not null)
                {
                    required = EvaluateConditionSet(constraint.RequiredWhen, normalizedValues);
                }

                var visible = constraint.VisibleWhen is null || EvaluateConditionSet(constraint.VisibleWhen, normalizedValues);
                if (constraint.HiddenWhen is not null && EvaluateConditionSet(constraint.HiddenWhen, normalizedValues))
                {
                    visible = false;
                }

                var ignored = constraint.IgnoredWhen is not null &&
                              EvaluateConditionSet(constraint.IgnoredWhen, normalizedValues);
                var enabled = constraint.EnabledWhen is null || EvaluateConditionSet(constraint.EnabledWhen, normalizedValues);
                var disabled = !enabled ||
                               ignored ||
                               (constraint.DisabledWhen is not null && EvaluateConditionSet(constraint.DisabledWhen, normalizedValues));
                return new OperatorParameterConstraintState(
                    constraint,
                    EffectiveRequired: required && !disabled,
                    EffectiveDisabled: disabled,
                    EffectiveVisible: visible,
                    EffectiveIgnored: ignored);
            })
            .ToArray();

        return states
            .Select(state =>
            {
                if (state.EffectiveDisabled ||
                    string.IsNullOrWhiteSpace(state.Constraint.MutuallyExclusiveGroup) ||
                    !IsMissing(GetValue(normalizedValues, state.Constraint.Parameter)))
                {
                    return state;
                }

                var hasConfiguredPeer = states.Any(peer =>
                    !ReferenceEquals(peer, state) &&
                    string.Equals(
                        peer.Constraint.MutuallyExclusiveGroup,
                        state.Constraint.MutuallyExclusiveGroup,
                        StringComparison.OrdinalIgnoreCase) &&
                    !peer.EffectiveDisabled &&
                    !peer.EffectiveIgnored &&
                    !IsMissing(GetValue(normalizedValues, peer.Constraint.Parameter)));

                return hasConfiguredPeer
                    ? state with { EffectiveRequired = false, EffectiveDisabled = true }
                    : state;
            })
            .ToArray();
    }

    public static IReadOnlyList<OperatorParameterConstraintViolation> Validate(
        OperatorMetadata metadata,
        IReadOnlyDictionary<string, object?> values,
        IReadOnlySet<string>? explicitParameterNames = null,
        bool requireExplicitResourceConfiguration = false,
        IReadOnlySet<string>? satisfiedInputPorts = null)
    {
        var canonicalization = Canonicalize(metadata, values, explicitParameterNames);
        var normalizedValues = canonicalization.EffectiveValues;
        var states = ResolveStatesCore(metadata, normalizedValues);
        var violations = new List<OperatorParameterConstraintViolation>();

        bool IsConfigured(OperatorParameterConstraintState state)
        {
            if (IsSatisfiedByInputPort(state.Constraint, satisfiedInputPorts))
            {
                return true;
            }

            var effectiveValue = GetValue(normalizedValues, state.Constraint.Parameter);
            if (IsMissing(effectiveValue))
            {
                return false;
            }

            if (IsInactiveResourceSwitch(effectiveValue))
            {
                return true;
            }

            if (!requireExplicitResourceConfiguration ||
                string.IsNullOrWhiteSpace(state.Constraint.ResourceKind))
            {
                return true;
            }

            return canonicalization.ExplicitValues.TryGetValue(state.Constraint.Parameter, out var explicitValue) &&
                   !IsMissing(explicitValue);
        }

        foreach (var group in states
                     .Where(item => !string.IsNullOrWhiteSpace(item.Constraint.AtLeastOneGroup))
                     .GroupBy(item => item.Constraint.AtLeastOneGroup!, StringComparer.OrdinalIgnoreCase))
        {
            var active = group.Where(item => item.EffectiveRequired && !item.EffectiveDisabled).ToArray();
            if (active.Length == 0)
            {
                continue;
            }

            var names = active
                .Select(item => item.Constraint.Parameter)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (active.Any(IsConfigured))
            {
                continue;
            }

            var primary = active[0].Constraint;
            violations.Add(new OperatorParameterConstraintViolation(
                "at-least-one",
                names,
                primary.ResourceKind,
                primary.ReasonCode));
        }

        foreach (var group in states
                     .Where(item => !string.IsNullOrWhiteSpace(item.Constraint.MutuallyExclusiveGroup))
                     .GroupBy(item => item.Constraint.MutuallyExclusiveGroup!, StringComparer.OrdinalIgnoreCase))
        {
            var active = group
                .Where(item => !item.EffectiveDisabled && !item.EffectiveIgnored)
                .ToArray();
            if (active.Length == 0)
            {
                continue;
            }

            var configured = active
                .Select(item => item.Constraint.Parameter)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(name => !IsMissing(GetValue(normalizedValues, name)))
                .ToArray();
            if (configured.Length < 2)
            {
                continue;
            }

            var primary = active[0].Constraint;
            violations.Add(new OperatorParameterConstraintViolation(
                "mutually-exclusive",
                configured,
                primary.ResourceKind,
                primary.ReasonCode));
        }

        foreach (var state in states.Where(item =>
                     item.EffectiveRequired &&
                     string.IsNullOrWhiteSpace(item.Constraint.AtLeastOneGroup) &&
                     !IsConfigured(item)))
        {
            violations.Add(new OperatorParameterConstraintViolation(
                "required",
                [state.Constraint.Parameter],
                state.Constraint.ResourceKind,
                state.Constraint.ReasonCode));
        }

        return violations
            .GroupBy(item => $"{item.Code}|{string.Join('|', item.ParameterNames.OrderBy(name => name, StringComparer.OrdinalIgnoreCase))}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();
    }

    public static bool IsSatisfiedByInputPort(
        OperatorParameterConstraint constraint,
        IReadOnlySet<string>? satisfiedInputPorts)
    {
        return satisfiedInputPorts is { Count: > 0 } &&
               constraint.SatisfiedByInputPorts is { Count: > 0 } &&
               constraint.SatisfiedByInputPorts.Any(satisfiedInputPorts.Contains);
    }

    public static bool IsMissing(object? value)
    {
        return OperatorParameterValueSemantics.IsMissing(value);
    }

    public static OperatorParameterCanonicalizationResult Canonicalize(
        OperatorMetadata metadata,
        IReadOnlyDictionary<string, object?> values,
        IReadOnlySet<string>? explicitParameterNames = null)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(values);

        var metadataByExactName = metadata.Parameters
            .GroupBy(parameter => parameter.Name, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var metadataByName = metadata.Parameters
            .GroupBy(parameter => parameter.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var constraintsByExactName = metadata.ParameterConstraints
            .GroupBy(constraint => constraint.Parameter, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var constraintsByName = metadata.ParameterConstraints
            .GroupBy(constraint => constraint.Parameter, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var aliasConstraints = metadata.ParameterConstraints
            .Where(item => !string.IsNullOrWhiteSpace(item.AliasFor))
            .ToArray();
        var aliasNames = aliasConstraints
            .Select(item => item.Parameter)
            .ToHashSet(StringComparer.Ordinal);

        string NormalizeName(string name)
        {
            if (metadataByExactName.TryGetValue(name, out var exactParameter))
            {
                return exactParameter.Name;
            }

            if (constraintsByExactName.TryGetValue(name, out var exactConstraint))
            {
                return exactConstraint.Parameter;
            }

            if (metadataByName.TryGetValue(name, out var parameter))
            {
                return parameter.Name;
            }

            if (constraintsByName.TryGetValue(name, out var constraint))
            {
                return constraint.Parameter;
            }

            return name;
        }

        var explicitNames = explicitParameterNames is null
            ? null
            : explicitParameterNames.ToHashSet(StringComparer.Ordinal);
        var rawExplicit = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var pair in values)
        {
            if (explicitNames is not null &&
                !explicitNames.Contains(pair.Key) &&
                !explicitNames.Contains(NormalizeName(pair.Key)))
            {
                continue;
            }

            rawExplicit[NormalizeName(pair.Key)] = pair.Value;
        }

        var explicitValues = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in rawExplicit.Where(pair => !aliasNames.Contains(pair.Key)))
        {
            explicitValues[pair.Key] = pair.Value;
        }
        var diagnostics = new List<OperatorParameterAliasDiagnostic>();

        foreach (var aliasGroup in aliasConstraints
                     .GroupBy(item => NormalizeName(item.AliasFor!), StringComparer.OrdinalIgnoreCase))
        {
            var canonicalName = aliasGroup.Key;
            var hasCanonical = rawExplicit.TryGetValue(canonicalName, out var canonicalValue);
            var configuredAliases = aliasGroup
                .Where(item => rawExplicit.ContainsKey(item.Parameter))
                .Select(item => (Constraint: item, Value: rawExplicit[item.Parameter]))
                .ToArray();

            if (hasCanonical)
            {
                explicitValues[canonicalName] = canonicalValue;
                foreach (var alias in configuredAliases.Where(alias => !ValuesEqual(canonicalValue, alias.Value)))
                {
                    diagnostics.Add(new OperatorParameterAliasDiagnostic(
                        "canonical-overrides-alias",
                        canonicalName,
                        alias.Constraint.Parameter,
                        $"{canonicalName} overrides conflicting alias {alias.Constraint.Parameter}."));
                }

                continue;
            }

            if (configuredAliases.Length == 0)
            {
                continue;
            }

            var selected = configuredAliases[0];
            explicitValues[canonicalName] = selected.Value;
            foreach (var alias in configuredAliases.Skip(1).Where(alias => !ValuesEqual(selected.Value, alias.Value)))
            {
                diagnostics.Add(new OperatorParameterAliasDiagnostic(
                    "alias-conflict",
                    canonicalName,
                    alias.Constraint.Parameter,
                    $"Alias {selected.Constraint.Parameter} overrides conflicting alias {alias.Constraint.Parameter} for {canonicalName}."));
            }
        }

        var effectiveValues = metadata.Parameters
            .Where(parameter => parameter.DefaultValue is not null)
            .ToDictionary(
                parameter => parameter.Name,
                parameter => parameter.DefaultValue,
                StringComparer.OrdinalIgnoreCase);
        foreach (var pair in explicitValues)
        {
            effectiveValues[pair.Key] = pair.Value;
        }

        foreach (var alias in aliasConstraints)
        {
            var canonicalName = NormalizeName(alias.AliasFor!);
            if (effectiveValues.TryGetValue(canonicalName, out var canonicalValue))
            {
                effectiveValues[alias.Parameter] = canonicalValue;
            }
        }

        return new OperatorParameterCanonicalizationResult(
            new ReadOnlyDictionary<string, object?>(effectiveValues),
            new ReadOnlyDictionary<string, object?>(explicitValues),
            diagnostics);
    }

    public static bool EvaluateConditionSet(
        OperatorParameterConditionSet set,
        IReadOnlyDictionary<string, object?> values)
    {
        var all = set.All;
        var any = set.Any;
        var allMatches = all is null || all.Count == 0 || all.All(condition => EvaluateCondition(condition, values));
        var anyMatches = any is null || any.Count == 0 || any.Any(condition => EvaluateCondition(condition, values));
        return allMatches && anyMatches;
    }

    private static bool EvaluateCondition(
        OperatorParameterCondition condition,
        IReadOnlyDictionary<string, object?> values)
    {
        var value = GetValue(values, condition.Parameter);
        return condition.Comparison switch
        {
            OperatorParameterConditionComparisons.Equal => ValuesEqual(value, condition.Value),
            OperatorParameterConditionComparisons.NotEquals => !ValuesEqual(value, condition.Value),
            OperatorParameterConditionComparisons.Empty => IsMissing(value),
            OperatorParameterConditionComparisons.NotEmpty => !IsMissing(value),
            _ => false
        };
    }

    private static object? GetValue(IReadOnlyDictionary<string, object?> values, string name) =>
        values.TryGetValue(name, out var value) ? value : null;

    private static bool IsInactiveResourceSwitch(object? value)
    {
        return value is bool boolean
            ? !boolean
            : bool.TryParse(value?.ToString(), out var parsed) && !parsed;
    }

    private static bool ValuesEqual(object? left, object? right)
    {
        if (left is bool leftBool && right is bool rightBool)
        {
            return leftBool == rightBool;
        }

        if (bool.TryParse(left?.ToString(), out var parsedLeft) &&
            bool.TryParse(right?.ToString(), out var parsedRight))
        {
            return parsedLeft == parsedRight;
        }

        return string.Equals(left?.ToString()?.Trim(), right?.ToString()?.Trim(), StringComparison.OrdinalIgnoreCase);
    }
}

public static class OperatorOutputAvailabilityEvaluator
{
    public static IReadOnlyList<OperatorOutputAvailabilityState> ResolveStates(
        OperatorMetadata metadata,
        IReadOnlyDictionary<string, object?> values,
        IReadOnlySet<string>? explicitParameterNames = null)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(values);

        var normalized = OperatorParameterConstraintEvaluator
            .Canonicalize(metadata, values, explicitParameterNames)
            .EffectiveValues;
        var rulesByOutput = metadata.OutputAvailabilityRules
            .GroupBy(rule => rule.Output, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        return metadata.OutputPorts.Select(port =>
        {
            if (!rulesByOutput.TryGetValue(port.Name, out var rule))
            {
                return new OperatorOutputAvailabilityState(
                    null,
                    port.Name,
                    IsAvailable: true,
                    IsGuaranteed: true,
                    ReasonCode: "OUTPUT_ALWAYS_AVAILABLE");
            }

            var available = rule.AvailableWhen is null ||
                            OperatorParameterConstraintEvaluator.EvaluateConditionSet(rule.AvailableWhen, normalized);
            return new OperatorOutputAvailabilityState(
                rule,
                port.Name,
                IsAvailable: available,
                IsGuaranteed: available,
                ReasonCode: rule.ReasonCode);
        }).ToArray();
    }

    public static bool IsAvailable(
        OperatorMetadata metadata,
        string output,
        IReadOnlyDictionary<string, object?> values)
    {
        return ResolveStates(metadata, values)
            .FirstOrDefault(state => state.Output.Equals(output, StringComparison.OrdinalIgnoreCase))?
            .IsAvailable ?? false;
    }
}
