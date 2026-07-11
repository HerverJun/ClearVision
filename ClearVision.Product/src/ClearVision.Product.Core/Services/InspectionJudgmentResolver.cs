using System.Collections;
using System.Text.Json;
using ClearVision.Product.Core.Outcomes;

namespace ClearVision.Product.Core.Services;

public readonly record struct InspectionDecisionEvaluation(
    DecisionOutcome Decision,
    string DecisionSource,
    string ReasonCode,
    string? Message,
    bool HasJudgmentSignal = true)
{
    public bool MissingJudgmentSignal => ReasonCode == "MissingJudgmentSignal";
}

public static class InspectionJudgmentResolver
{
    private static readonly JudgmentBooleanSignal[] DirectBooleanSignals =
    [
        new("IsOk", PositiveMeansOk: true, "DerivedFromIsOk"),
        new("ConditionResult", PositiveMeansOk: true, "DerivedFromConditionResult"),
        new("Accepted", PositiveMeansOk: true, "DerivedFromAccepted"),
        new("VerificationPassed", PositiveMeansOk: true, "DerivedFromVerificationPassed"),
        new("IsMatch", PositiveMeansOk: true, "DerivedFromIsMatch"),
        new("IsMatched", PositiveMeansOk: true, "DerivedFromIsMatched"),
        new("HueValid", PositiveMeansOk: true, "DerivedFromHueValid"),
        new("IsSharp", PositiveMeansOk: true, "DerivedFromIsSharp"),
        new("IsAnomaly", PositiveMeansOk: false, "DerivedFromIsAnomaly")
    ];

    public static InspectionDecisionEvaluation DetermineDecisionFromLegacyHeuristic(Dictionary<string, object>? outputData)
    {
        if (outputData == null)
        {
            return DetermineDecisionFromFlowOutput(null, sourcePrefix: null, depth: 0);
        }

        return DetermineDecisionFromFlowOutput(
            new Dictionary<string, object>(outputData, StringComparer.OrdinalIgnoreCase),
            sourcePrefix: null,
            depth: 0);
    }

    [Obsolete("Use FinalDecisionResolver for official execution. This method is legacy preview compatibility only.")]
    public static InspectionDecisionEvaluation DetermineDecisionFromFlowOutput(Dictionary<string, object>? outputData) =>
        DetermineDecisionFromLegacyHeuristic(outputData);

    private static InspectionDecisionEvaluation DetermineDecisionFromFlowOutput(
        IReadOnlyDictionary<string, object>? outputData,
        string? sourcePrefix,
        int depth)
    {
        if (outputData == null || outputData.Count == 0)
        {
            return MissingJudgmentSignal();
        }

        if (depth > 8)
        {
            return Invalid(
                ComposeDecisionSource(sourcePrefix, "Depth"),
                "JudgmentTraversalDepthExceeded",
                "Judgment payload traversal exceeded the supported depth.");
        }

        if (outputData.TryGetValue("JudgmentResult", out var judgmentResult))
        {
            var source = ComposeDecisionSource(sourcePrefix, "JudgmentResult");
            if (!TryGetStringValue(judgmentResult, out var judgmentText))
            {
                return BuildInvalidTypeResult(source, "string", judgmentResult);
            }

            return ParseExplicitJudgment(judgmentText!, source, "DerivedFromJudgmentResult");
        }

        foreach (var signal in DirectBooleanSignals)
        {
            if (TryEvaluateBooleanSignal(outputData, signal, sourcePrefix, out var evaluation))
            {
                return evaluation;
            }
        }

        if (outputData.TryGetValue("Result", out var resultValue))
        {
            var source = ComposeDecisionSource(sourcePrefix, "Result");
            if (TryGetBoolValue(resultValue, out var resultBool))
            {
                return Decided(
                    resultBool ? DecisionOutcome.Ok : DecisionOutcome.Ng,
                    source,
                    "DerivedFromResult");
            }

            if (TryGetStringValue(resultValue, out var resultText))
            {
                return ParseExplicitJudgment(resultText!, source, "DerivedFromResultText");
            }

            if (!TryExtractNestedJudgmentPayload(resultValue, out _))
            {
                return BuildInvalidTypeResult(source, "bool or string", resultValue);
            }
        }

        if (outputData.TryGetValue("DefectCount", out var defectCountValue))
        {
            var source = ComposeDecisionSource(sourcePrefix, "DefectCount");
            if (!TryGetIntValue(defectCountValue, out var defectCount))
            {
                return BuildInvalidTypeResult(source, "int", defectCountValue);
            }

            return Decided(
                defectCount > 0 ? DecisionOutcome.Ng : DecisionOutcome.Ok,
                source,
                "DerivedFromDefectCount");
        }

        // Prompt 1 deliberately keeps the legacy recursive scan. Explicit binding replaces it in Prompt 2.
        foreach (var (key, value) in outputData)
        {
            if (value == null || !TryExtractNestedJudgmentPayload(value, out var nestedPayload))
            {
                continue;
            }

            var nestedEvaluation = DetermineDecisionFromFlowOutput(
                nestedPayload,
                ComposeDecisionSource(sourcePrefix, key),
                depth + 1);
            if (!nestedEvaluation.MissingJudgmentSignal)
            {
                return nestedEvaluation;
            }
        }

        return MissingJudgmentSignal();
    }

    private static InspectionDecisionEvaluation ParseExplicitJudgment(
        string judgmentText,
        string source,
        string decidedReasonCode)
    {
        var normalized = judgmentText.Trim();
        if (normalized.Equals("OK", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("Pass", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("Passed", StringComparison.OrdinalIgnoreCase))
        {
            return Decided(DecisionOutcome.Ok, source, decidedReasonCode);
        }

        if (normalized.Equals("NG", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("Fail", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("Failed", StringComparison.OrdinalIgnoreCase))
        {
            return Decided(DecisionOutcome.Ng, source, decidedReasonCode);
        }

        if (normalized.Equals("Unknown", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("Pending", StringComparison.OrdinalIgnoreCase))
        {
            return Decided(DecisionOutcome.Undetermined, source, "JudgmentUndetermined");
        }

        if (normalized.Equals("Skipped", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("NotApplicable", StringComparison.OrdinalIgnoreCase))
        {
            return Decided(DecisionOutcome.NotApplicable, source, "JudgmentNotApplicable");
        }

        if (normalized.Equals("Error", StringComparison.OrdinalIgnoreCase))
        {
            return Invalid(source, "ExplicitJudgmentError", "The explicit judgment value is Error.");
        }

        return Invalid(
            source,
            "UnrecognizedJudgmentValue",
            $"Unrecognized explicit judgment value '{normalized}'.");
    }

    private static bool TryEvaluateBooleanSignal(
        IReadOnlyDictionary<string, object> outputData,
        JudgmentBooleanSignal signal,
        string? sourcePrefix,
        out InspectionDecisionEvaluation evaluation)
    {
        evaluation = default;
        if (!outputData.TryGetValue(signal.FieldName, out var rawValue))
        {
            return false;
        }

        var source = ComposeDecisionSource(sourcePrefix, signal.FieldName);
        if (!TryGetBoolValue(rawValue, out var boolValue))
        {
            evaluation = BuildInvalidTypeResult(source, "bool", rawValue);
            return true;
        }

        evaluation = Decided(
            signal.PositiveMeansOk == boolValue ? DecisionOutcome.Ok : DecisionOutcome.Ng,
            source,
            signal.ReasonCode);
        return true;
    }

    private static bool TryExtractNestedJudgmentPayload(object value, out Dictionary<string, object> payload)
    {
        switch (value)
        {
            case Dictionary<string, object> dictionary:
                payload = new Dictionary<string, object>(dictionary, StringComparer.OrdinalIgnoreCase);
                return true;
            case IReadOnlyDictionary<string, object> readOnlyDictionary:
                payload = new Dictionary<string, object>(readOnlyDictionary, StringComparer.OrdinalIgnoreCase);
                return true;
            case IDictionary legacyDictionary:
                payload = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                foreach (DictionaryEntry entry in legacyDictionary)
                {
                    if (entry.Key is string key && entry.Value != null)
                    {
                        payload[key] = entry.Value;
                    }
                }

                return payload.Count > 0;
            case JsonElement { ValueKind: JsonValueKind.Object } element:
                payload = JsonSerializer.Deserialize<Dictionary<string, object>>(element.GetRawText()) ??
                          new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                return payload.Count > 0;
            default:
                payload = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                return false;
        }
    }

    private static bool TryGetStringValue(object? value, out string? text)
    {
        switch (value)
        {
            case string s:
                text = s;
                return true;
            case JsonElement { ValueKind: JsonValueKind.String } element:
                text = element.GetString();
                return true;
            default:
                text = null;
                return false;
        }
    }

    private static bool TryGetBoolValue(object? value, out bool boolValue)
    {
        switch (value)
        {
            case bool b:
                boolValue = b;
                return true;
            case JsonElement { ValueKind: JsonValueKind.True }:
                boolValue = true;
                return true;
            case JsonElement { ValueKind: JsonValueKind.False }:
                boolValue = false;
                return true;
            default:
                boolValue = false;
                return false;
        }
    }

    private static bool TryGetIntValue(object? value, out int intValue)
    {
        switch (value)
        {
            case int i:
                intValue = i;
                return true;
            case long l when l >= int.MinValue && l <= int.MaxValue:
                intValue = (int)l;
                return true;
            case double d when IsWholeNumberInIntRange(d):
                intValue = (int)d;
                return true;
            case float f when IsWholeNumberInIntRange(f):
                intValue = (int)f;
                return true;
            case decimal m when m >= int.MinValue && m <= int.MaxValue && decimal.Truncate(m) == m:
                intValue = (int)m;
                return true;
            case string s when int.TryParse(s, out var parsedString):
                intValue = parsedString;
                return true;
            case JsonElement { ValueKind: JsonValueKind.Number } element when element.TryGetInt32(out var parsed):
                intValue = parsed;
                return true;
            case JsonElement { ValueKind: JsonValueKind.Number } element when element.TryGetDouble(out var parsedDouble) && IsWholeNumberInIntRange(parsedDouble):
                intValue = (int)parsedDouble;
                return true;
            case JsonElement { ValueKind: JsonValueKind.String } element when int.TryParse(element.GetString(), out var parsedStringElement):
                intValue = parsedStringElement;
                return true;
            default:
                intValue = 0;
                return false;
        }
    }

    private static bool IsWholeNumberInIntRange(double value) =>
        !double.IsNaN(value) &&
        !double.IsInfinity(value) &&
        value >= int.MinValue &&
        value <= int.MaxValue &&
        Math.Truncate(value) == value;

    private static InspectionDecisionEvaluation MissingJudgmentSignal() =>
        new(DecisionOutcome.Undetermined, "LegacyHeuristic:None", "MissingJudgmentSignal", null, false);

    private static InspectionDecisionEvaluation BuildInvalidTypeResult(
        string fieldName,
        string expectedType,
        object? actualValue)
    {
        var actualType = DescribeType(actualValue);
        return Invalid(
            fieldName,
            "InvalidJudgmentType",
            $"Invalid judgment type at {fieldName}. Expected {expectedType}, actual {actualType}.");
    }

    private static InspectionDecisionEvaluation Decided(
        DecisionOutcome decision,
        string source,
        string reasonCode) =>
        new(decision, $"LegacyHeuristic:{source}", reasonCode, null, true);

    private static InspectionDecisionEvaluation Invalid(
        string source,
        string reasonCode,
        string message) =>
        new(DecisionOutcome.Invalid, $"LegacyHeuristic:{source}", reasonCode, message, true);

    private static string ComposeDecisionSource(string? prefix, string fieldName) =>
        string.IsNullOrWhiteSpace(prefix) ? fieldName : $"{prefix}.{fieldName}";

    private static string DescribeType(object? value) => value?.GetType().Name ?? "null";

    private readonly record struct JudgmentBooleanSignal(
        string FieldName,
        bool PositiveMeansOk,
        string ReasonCode);
}
