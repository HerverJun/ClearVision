using System.Globalization;
using System.Text.Json;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Outcomes;
using ClearVision.Product.Core.Services;
using ClearVision.Product.Core.ValueObjects;

namespace ClearVision.Product.Core.Decisions;

public sealed record DecisionConfigurationIssue(
    string Code,
    string Message,
    Guid? OperatorId = null,
    string? OutputName = null);

public static class FinalDecisionResolver
{
    public static IReadOnlyList<DecisionConfigurationIssue> Validate(OperatorFlow? flow)
    {
        if (flow == null)
        {
            return [new("DECISION_FLOW_REQUIRED", "A flow is required to validate the final decision binding.")];
        }

        var configuration = flow.DecisionConfiguration;
        var binding = configuration?.FinalDecisionBinding;
        if (binding == null)
        {
            return [new("DECISION_BINDING_REQUIRED", "A final decision binding is required for official inspection.")];
        }

        var issues = new List<DecisionConfigurationIssue>();
        var sourceOperator = flow.Operators.FirstOrDefault(op => op.Id == binding.SourceOperatorId);
        if (binding.SourceOperatorId == Guid.Empty || sourceOperator == null)
        {
            issues.Add(new(
                "DECISION_SOURCE_OPERATOR_NOT_FOUND",
                $"Final decision source operator '{binding.SourceOperatorId}' does not exist in the flow.",
                binding.SourceOperatorId));
            return issues;
        }

        if (!sourceOperator.IsEnabled)
        {
            issues.Add(new(
                "DECISION_SOURCE_OPERATOR_DISABLED",
                $"Final decision source operator '{sourceOperator.Name}' is disabled.",
                sourceOperator.Id));
        }

        var outputPort = ResolveOutputPort(sourceOperator, binding);
        if (outputPort == null)
        {
            issues.Add(new(
                "DECISION_SOURCE_OUTPUT_NOT_FOUND",
                $"Final decision output cannot be resolved on operator '{sourceOperator.Name}'.",
                sourceOperator.Id,
                binding.SourceOutputName));
            return issues;
        }

        if (!string.IsNullOrWhiteSpace(binding.SourceOutputName) &&
            !outputPort.Name.Equals(binding.SourceOutputName.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            issues.Add(new(
                "DECISION_SOURCE_OUTPUT_MISMATCH",
                "SourceOutputPortId and SourceOutputName resolve to different outputs.",
                sourceOperator.Id,
                binding.SourceOutputName));
        }

        if (!binding.DataType.MatchesPortType(outputPort.DataType))
        {
            issues.Add(new(
                "DECISION_SOURCE_TYPE_MISMATCH",
                $"Binding data type '{binding.DataType}' is incompatible with output port type '{outputPort.DataType}'.",
                sourceOperator.Id,
                outputPort.Name));
        }

        ValidateRule(binding, sourceOperator.Id, outputPort.Name, issues);
        return issues;
    }

    public static InspectionDecisionEvaluation Resolve(
        OperatorFlow flow,
        FlowExecutionResult flowResult)
    {
        ArgumentNullException.ThrowIfNull(flow);
        ArgumentNullException.ThrowIfNull(flowResult);

        var issues = Validate(flow);
        if (issues.Count > 0)
        {
            return new InspectionDecisionEvaluation(
                DecisionOutcome.Invalid,
                "DecisionConfiguration",
                issues[0].Code,
                issues[0].Message,
                HasJudgmentSignal: false);
        }

        var configuration = flow.DecisionConfiguration!;
        var binding = configuration.FinalDecisionBinding!;
        var sourceOperator = flow.Operators.First(op => op.Id == binding.SourceOperatorId);
        var outputPort = ResolveOutputPort(sourceOperator, binding)!;
        var operatorResult = flowResult.OperatorResults.LastOrDefault(result => result.OperatorId == sourceOperator.Id);
        var outputData = operatorResult?.OutputData ??
                         (flowResult.OperatorResults.Count == 0 ? flowResult.OutputData : null);
        if (outputData == null || !TryGetValue(outputData, outputPort.Name, out var value) || value == null)
        {
            return ResolveMissing(configuration.MissingDecisionPolicy, sourceOperator.Id, outputPort.Name);
        }

        var source = $"FinalDecisionBinding:{sourceOperator.Id}:{outputPort.Name}";
        return binding.Rule switch
        {
            DecisionInterpretationRule.Boolean => ResolveBoolean(binding, value, source),
            DecisionInterpretationRule.StringMap => ResolveString(binding, value, source),
            DecisionInterpretationRule.NumericComparison => ResolveNumber(binding, value, source),
            _ => InvalidValue(source, "DECISION_RULE_UNSUPPORTED", $"Unsupported decision rule '{binding.Rule}'.")
        };
    }

    public static string? ResolveOutputName(OperatorFlow flow)
    {
        var binding = flow.DecisionConfiguration?.FinalDecisionBinding;
        var sourceOperator = binding == null
            ? null
            : flow.Operators.FirstOrDefault(op => op.Id == binding.SourceOperatorId);
        return sourceOperator == null ? null : ResolveOutputPort(sourceOperator, binding!)?.Name;
    }

    private static void ValidateRule(
        FinalDecisionBinding binding,
        Guid operatorId,
        string outputName,
        List<DecisionConfigurationIssue> issues)
    {
        switch (binding.Rule)
        {
            case DecisionInterpretationRule.Boolean when binding.DataType != DecisionValueType.Boolean:
                issues.Add(new("DECISION_RULE_TYPE_MISMATCH", "Boolean rule requires Boolean data type.", operatorId, outputName));
                break;
            case DecisionInterpretationRule.StringMap:
                if (binding.DataType != DecisionValueType.String)
                {
                    issues.Add(new("DECISION_RULE_TYPE_MISMATCH", "StringMap rule requires String data type.", operatorId, outputName));
                }
                if (string.IsNullOrWhiteSpace(binding.OkValue) || string.IsNullOrWhiteSpace(binding.NgValue))
                {
                    issues.Add(new("DECISION_STRING_MAP_VALUES_REQUIRED", "StringMap requires non-empty OkValue and NgValue.", operatorId, outputName));
                }
                else if (binding.OkValue.Trim().Equals(binding.NgValue.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    issues.Add(new("DECISION_STRING_MAP_VALUES_CONFLICT", "OkValue and NgValue must be different.", operatorId, outputName));
                }
                break;
            case DecisionInterpretationRule.NumericComparison:
                if (binding.DataType is not DecisionValueType.Integer and not DecisionValueType.Float)
                {
                    issues.Add(new("DECISION_RULE_TYPE_MISMATCH", "NumericComparison requires Integer or Float data type.", operatorId, outputName));
                }
                if (binding.Comparator == null || binding.Threshold == null ||
                    double.IsNaN(binding.Threshold.Value) || double.IsInfinity(binding.Threshold.Value))
                {
                    issues.Add(new("DECISION_NUMERIC_COMPARISON_REQUIRED", "NumericComparison requires a finite threshold and comparator.", operatorId, outputName));
                }
                break;
        }
    }

    private static InspectionDecisionEvaluation ResolveBoolean(FinalDecisionBinding binding, object value, string source)
    {
        if (!TryConvertBoolean(value, out var booleanValue))
        {
            return InvalidValue(source, "DECISION_VALUE_TYPE_INVALID", "Bound decision value is not Boolean.");
        }

        return Decided(binding.TrueMeansOk == booleanValue ? DecisionOutcome.Ok : DecisionOutcome.Ng, source);
    }

    private static InspectionDecisionEvaluation ResolveString(FinalDecisionBinding binding, object value, string source)
    {
        if (!TryConvertString(value, out var text))
        {
            return InvalidValue(source, "DECISION_VALUE_TYPE_INVALID", "Bound decision value is not String.");
        }

        var normalized = text!.Trim();
        if (normalized.Equals(binding.OkValue!.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return Decided(DecisionOutcome.Ok, source);
        }
        if (normalized.Equals(binding.NgValue!.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return Decided(DecisionOutcome.Ng, source);
        }

        return InvalidValue(source, "DECISION_STRING_VALUE_UNMAPPED", $"Bound value '{normalized}' is not mapped to OK or NG.");
    }

    private static InspectionDecisionEvaluation ResolveNumber(FinalDecisionBinding binding, object value, string source)
    {
        if (!TryConvertDouble(value, out var number))
        {
            return InvalidValue(source, "DECISION_VALUE_TYPE_INVALID", "Bound decision value is not numeric.");
        }
        if (binding.DataType == DecisionValueType.Integer && Math.Truncate(number) != number)
        {
            return InvalidValue(source, "DECISION_VALUE_TYPE_INVALID", "Bound decision value is not an integer.");
        }

        var threshold = binding.Threshold!.Value;
        var isOk = binding.Comparator!.Value switch
        {
            DecisionComparator.Equal => number == threshold,
            DecisionComparator.NotEqual => number != threshold,
            DecisionComparator.GreaterThan => number > threshold,
            DecisionComparator.GreaterThanOrEqual => number >= threshold,
            DecisionComparator.LessThan => number < threshold,
            DecisionComparator.LessThanOrEqual => number <= threshold,
            _ => false
        };
        return Decided(isOk ? DecisionOutcome.Ok : DecisionOutcome.Ng, source);
    }

    private static InspectionDecisionEvaluation ResolveMissing(
        MissingDecisionPolicy policy,
        Guid operatorId,
        string outputName)
    {
        var message = $"Bound decision output '{outputName}' was not produced by operator '{operatorId}'.";
        return policy switch
        {
            MissingDecisionPolicy.NotApplicable => new(DecisionOutcome.NotApplicable, "FinalDecisionBinding", "DECISION_SIGNAL_MISSING_NOT_APPLICABLE", message, false),
            MissingDecisionPolicy.Invalid => new(DecisionOutcome.Invalid, "FinalDecisionBinding", "DECISION_SIGNAL_MISSING_INVALID", message, false),
            _ => new(DecisionOutcome.Undetermined, "FinalDecisionBinding", "DECISION_SIGNAL_MISSING", message, false)
        };
    }

    private static InspectionDecisionEvaluation Decided(DecisionOutcome decision, string source) =>
        new(decision, source, "DECISION_BOUND_VALUE_RESOLVED", null, true);

    private static InspectionDecisionEvaluation InvalidValue(string source, string code, string message) =>
        new(DecisionOutcome.Invalid, source, code, message, true);

    private static Port? ResolveOutputPort(Operator sourceOperator, FinalDecisionBinding binding)
    {
        if (binding.SourceOutputPortId is { } portId && portId != Guid.Empty)
        {
            return sourceOperator.OutputPorts.FirstOrDefault(port => port.Id == portId);
        }

        if (!string.IsNullOrWhiteSpace(binding.SourceOutputName))
        {
            return sourceOperator.OutputPorts.FirstOrDefault(port =>
                port.Name.Equals(binding.SourceOutputName.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        return null;
    }

    private static bool TryGetValue(IReadOnlyDictionary<string, object> values, string name, out object? value)
    {
        if (values.TryGetValue(name, out var direct))
        {
            value = direct;
            return true;
        }

        foreach (var pair in values)
        {
            if (pair.Key.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                value = pair.Value;
                return true;
            }
        }

        value = null;
        return false;
    }

    private static bool TryConvertBoolean(object value, out bool result)
    {
        if (value is bool boolean)
        {
            result = boolean;
            return true;
        }
        if (value is JsonElement { ValueKind: JsonValueKind.True or JsonValueKind.False } element)
        {
            result = element.GetBoolean();
            return true;
        }
        result = false;
        return false;
    }

    private static bool TryConvertString(object value, out string? result)
    {
        if (value is string text)
        {
            result = text;
            return true;
        }
        if (value is JsonElement { ValueKind: JsonValueKind.String } element)
        {
            result = element.GetString();
            return true;
        }
        result = null;
        return false;
    }

    private static bool TryConvertDouble(object value, out double result)
    {
        if (value is JsonElement { ValueKind: JsonValueKind.Number } element && element.TryGetDouble(out result))
        {
            return true;
        }
        return double.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out result) &&
               !double.IsNaN(result) && !double.IsInfinity(result);
    }
}
