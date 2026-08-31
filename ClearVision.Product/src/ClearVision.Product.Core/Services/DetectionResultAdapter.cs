using System.Collections;
using System.Globalization;
using System.Text.Json;
using DetectionBox = ClearVision.Product.Core.ValueObjects.DetectionResult;
using DetectionList = ClearVision.Product.Core.ValueObjects.DetectionList;

namespace ClearVision.Product.Core.Services;

/// <summary>
/// Converts the public detection payload shapes emitted by operators into one canonical form.
/// This lives in Core because stored execution, real-time execution, and background execution
/// must make exactly the same decision about a detection payload.
/// </summary>
public static class DetectionResultAdapter
{
    private static readonly string[] DetectionOutputKeys =
    [
        "Defects",
        "DetectionList",
        "Detections",
        "Objects",
        "SortedDetections",
        "Blobs"
    ];

    public static bool TryExtractFromOutput(
        IReadOnlyDictionary<string, object>? outputData,
        out IReadOnlyList<DetectionBox> detections,
        out bool hasDetectionPayload)
    {
        detections = Array.Empty<DetectionBox>();
        hasDetectionPayload = false;
        if (outputData == null)
        {
            return false;
        }

        foreach (var key in DetectionOutputKeys)
        {
            if (!outputData.TryGetValue(key, out var payload))
            {
                continue;
            }

            hasDetectionPayload = true;
            return TryExtract(payload, out detections);
        }

        return false;
    }

    public static bool TryExtract(object? payload, out IReadOnlyList<DetectionBox> detections)
    {
        var converted = new List<DetectionBox>();
        if (!TryExtractCore(payload, converted, out var recognized))
        {
            detections = Array.Empty<DetectionBox>();
            return false;
        }

        detections = converted;
        return recognized;
    }

    public static bool TryCreateDecision(object? payload, out CanonicalDetectionDecision decision)
    {
        switch (payload)
        {
            case DetectionResult result when result.IsSuccess:
                decision = new CanonicalDetectionDecision(result.IsOk, ClampConfidence(result.Confidence));
                return true;
            case DetectionList detectionList:
                return TryCreateDecisionFromDetections(detectionList.Detections, out decision);
            case IDictionary dictionary:
                return TryCreateDecisionFromDictionary(dictionary, out decision);
            case JsonElement element:
                return TryCreateDecisionFromJson(element, out decision);
            default:
                if (TryExtract(payload, out var detections))
                {
                    return TryCreateDecisionFromDetections(detections, out decision);
                }

                decision = default;
                return false;
        }
    }

    private static bool TryCreateDecisionFromDictionary(IDictionary dictionary, out CanonicalDetectionDecision decision)
    {
        var values = ToDictionary(dictionary);
        if (TryGetValue(values, "IsOk", out var isOkValue) &&
            TryGetValue(values, "Confidence", out var confidenceValue) &&
            TryReadBoolean(isOkValue, out var isOk) &&
            TryReadDouble(confidenceValue, out var confidence))
        {
            decision = new CanonicalDetectionDecision(isOk, ClampConfidence(confidence));
            return true;
        }

        if (TryGetValue(values, "DefectCount", out var defectCountValue) &&
            TryReadNonNegativeInt(defectCountValue, out var defectCount))
        {
            if (TryGetNestedDetectionPayload(values, out var nestedPayload))
            {
                if (TryExtract(nestedPayload, out var detections))
                {
                    if (detections.Count == defectCount)
                    {
                        return TryCreateDecisionFromDetections(detections, out decision);
                    }
                }

                // Legacy decision dictionaries may carry only DefectCount and a
                // confidence-only Defects array.  They remain valid for voting,
                // but are intentionally not accepted by TryExtract for result
                // persistence because they do not contain a bounding box.
                if (TryReadLegacyDefectConfidence(nestedPayload, out var maxConfidence, out var hasConfidence))
                {
                    decision = new CanonicalDetectionDecision(
                        defectCount == 0,
                        defectCount == 0
                            ? 1.0
                            : hasConfidence ? ClampConfidence(maxConfidence) : 1.0);
                    return true;
                }

                decision = default;
                return false;
            }

            decision = new CanonicalDetectionDecision(defectCount == 0, 1.0);
            return true;
        }

        if (TryGetNestedDetectionPayload(values, out var payload))
        {
            return TryCreateDecision(payload, out decision);
        }

        decision = default;
        return false;
    }

    private static bool TryCreateDecisionFromJson(JsonElement element, out CanonicalDetectionDecision decision)
    {
        if (element.ValueKind == JsonValueKind.Array)
        {
            return TryCreateDecisionFromPayload(element, out decision);
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            decision = default;
            return false;
        }

        var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in element.EnumerateObject())
        {
            values[property.Name] = property.Value;
        }

        return TryCreateDecisionFromDictionary(new DictionaryAdapter(values), out decision);
    }

    private static bool TryCreateDecisionFromPayload(object? payload, out CanonicalDetectionDecision decision)
    {
        if (!TryExtract(payload, out var detections))
        {
            decision = default;
            return false;
        }

        return TryCreateDecisionFromDetections(detections, out decision);
    }

    private static bool TryCreateDecisionFromDetections(IEnumerable<DetectionBox>? detections, out CanonicalDetectionDecision decision)
    {
        if (detections == null)
        {
            decision = default;
            return false;
        }

        var list = detections.ToList();
        if (list.Any(detection => !IsValid(detection)))
        {
            decision = default;
            return false;
        }

        decision = list.Count == 0
            ? new CanonicalDetectionDecision(true, 1.0)
            : new CanonicalDetectionDecision(false, ClampConfidence(list.Max(detection => detection.Confidence)));
        return true;
    }

    private static bool TryExtractCore(object? payload, List<DetectionBox> target, out bool recognized)
    {
        recognized = true;
        switch (payload)
        {
            case DetectionList detectionList:
                return TryAppendTyped(detectionList.Detections, target);
            case IEnumerable<DetectionBox> typed:
                return TryAppendTyped(typed, target);
            case JsonElement element:
                return TryAppendJson(element, target, out recognized);
            case IDictionary dictionary:
                return TryAppendDictionary(dictionary, target, out recognized);
            case string json:
                return TryAppendJsonString(json, target, out recognized);
            case null:
                recognized = false;
                return false;
            case IEnumerable enumerable:
                return TryAppendEnumerable(enumerable, target);
            default:
                recognized = false;
                return false;
        }
    }

    private static bool TryAppendTyped(IEnumerable<DetectionBox>? source, List<DetectionBox> target)
    {
        if (source == null)
        {
            return false;
        }

        foreach (var detection in source)
        {
            if (!IsValid(detection))
            {
                return false;
            }

            target.Add(Clone(detection));
        }

        return true;
    }

    private static bool TryAppendEnumerable(IEnumerable source, List<DetectionBox> target)
    {
        foreach (var item in source)
        {
            if (!TryAppendOne(item, target))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryAppendJson(JsonElement element, List<DetectionBox> target, out bool recognized)
    {
        recognized = true;
        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                if (!TryAppendOne(item, target))
                {
                    return false;
                }
            }

            return true;
        }

        if (element.ValueKind == JsonValueKind.Object)
        {
            var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in element.EnumerateObject())
            {
                values[property.Name] = property.Value.Clone();
            }

            return TryAppendDictionary(new DictionaryAdapter(values), target, out recognized);
        }

        recognized = false;
        return false;
    }

    private static bool TryAppendJsonString(
        string json,
        List<DetectionBox> target,
        out bool recognized)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return TryAppendJson(document.RootElement, target, out recognized);
        }
        catch (JsonException)
        {
            recognized = false;
            return false;
        }
    }

    private static bool TryAppendDictionary(IDictionary dictionary, List<DetectionBox> target, out bool recognized)
    {
        var values = ToDictionary(dictionary);
        if (TryGetNestedDetectionPayload(values, out var nestedPayload))
        {
            recognized = true;
            return TryExtractCore(nestedPayload, target, out _);
        }

        recognized = HasDetectionFields(values);
        return recognized && TryBuildDetection(values, out var detection) && Add(detection, target);
    }

    private static bool TryAppendOne(object? item, List<DetectionBox> target)
    {
        switch (item)
        {
            case DetectionBox detection:
                return IsValid(detection) && Add(detection, target);
            case JsonElement json:
                return TryAppendJson(json, target, out _);
            case IDictionary dictionary:
                return TryAppendDictionary(dictionary, target, out _);
            default:
                return false;
        }
    }

    private static bool Add(DetectionBox detection, List<DetectionBox> target)
    {
        target.Add(Clone(detection));
        return true;
    }

    private static bool TryBuildDetection(IReadOnlyDictionary<string, object?> values, out DetectionBox detection)
    {
        var label = ReadString(values, "Label") ?? ReadString(values, "ClassName");
        if (!TryReadValue(values, "Confidence", "Score", out var confidence) ||
            !TryReadValue(values, "X", "Left", out var x) ||
            !TryReadValue(values, "Y", "Top", out var y) ||
            !TryReadValue(values, "Width", null, out var width) ||
            !TryReadValue(values, "Height", null, out var height))
        {
            detection = new DetectionBox();
            return false;
        }

        detection = new DetectionBox(label ?? string.Empty, (float)confidence, (float)x, (float)y, (float)width, (float)height);
        return IsValid(detection);
    }

    private static bool TryReadValue(IReadOnlyDictionary<string, object?> values, string primary, string? alternate, out double value)
    {
        if (TryGetValue(values, primary, out var candidate) && TryReadDouble(candidate, out value))
        {
            return true;
        }

        if (alternate != null && TryGetValue(values, alternate, out candidate) && TryReadDouble(candidate, out value))
        {
            return true;
        }

        value = 0;
        return false;
    }

    private static bool TryGetNestedDetectionPayload(IReadOnlyDictionary<string, object?> values, out object? payload)
    {
        foreach (var key in DetectionOutputKeys)
        {
            if (TryGetValue(values, key, out payload))
            {
                return true;
            }
        }

        payload = null;
        return false;
    }

    private static bool TryReadLegacyDefectConfidence(
        object? payload,
        out double maxConfidence,
        out bool hasConfidence)
    {
        maxConfidence = 0;
        hasConfidence = false;
        if (payload is JsonElement json)
        {
            if (json.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            foreach (var item in json.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                {
                    return false;
                }

                if (item.TryGetProperty("Confidence", out var confidence) ||
                    item.TryGetProperty("confidence", out confidence) ||
                    item.TryGetProperty("Score", out confidence) ||
                    item.TryGetProperty("score", out confidence))
                {
                    if (!TryReadDouble(confidence, out var parsed))
                    {
                        return false;
                    }

                    hasConfidence = true;
                    maxConfidence = Math.Max(maxConfidence, parsed);
                }
            }

            return true;
        }

        if (payload is not IEnumerable enumerable || payload is string)
        {
            return false;
        }

        foreach (var item in enumerable)
        {
            if (item is not IDictionary dictionary)
            {
                return false;
            }

            var values = ToDictionary(dictionary);
            if (TryGetValue(values, "Confidence", out var confidence) ||
                TryGetValue(values, "Score", out confidence))
            {
                if (!TryReadDouble(confidence, out var parsed))
                {
                    return false;
                }

                hasConfidence = true;
                maxConfidence = Math.Max(maxConfidence, parsed);
            }
        }

        return true;
    }

    private static bool HasDetectionFields(IReadOnlyDictionary<string, object?> values) =>
        TryGetValue(values, "X", out _) ||
        TryGetValue(values, "Left", out _) ||
        TryGetValue(values, "Y", out _) ||
        TryGetValue(values, "Top", out _) ||
        TryGetValue(values, "Width", out _) ||
        TryGetValue(values, "Height", out _);

    private static Dictionary<string, object?> ToDictionary(IDictionary dictionary)
    {
        var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (DictionaryEntry entry in dictionary)
        {
            if (entry.Key is string key)
            {
                values[key] = entry.Value;
            }
        }

        return values;
    }

    private static bool TryGetValue(IReadOnlyDictionary<string, object?> values, string key, out object? value)
    {
        if (values.TryGetValue(key, out value))
        {
            return true;
        }

        foreach (var pair in values)
        {
            if (string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                value = pair.Value;
                return true;
            }
        }

        value = null;
        return false;
    }

    private static string? ReadString(IReadOnlyDictionary<string, object?> values, string key) =>
        TryGetValue(values, key, out var value) ? value?.ToString() : null;

    private static bool TryReadBoolean(object? value, out bool parsed)
    {
        switch (value)
        {
            case bool boolean:
                parsed = boolean;
                return true;
            case JsonElement json when json.ValueKind is JsonValueKind.True or JsonValueKind.False:
                parsed = json.GetBoolean();
                return true;
            default:
                return bool.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), out parsed);
        }
    }

    private static bool TryReadNonNegativeInt(object? value, out int parsed)
    {
        if (!TryReadDouble(value, out var numeric) ||
            numeric < 0 ||
            numeric > int.MaxValue ||
            Math.Abs(numeric - Math.Round(numeric)) > double.Epsilon)
        {
            parsed = 0;
            return false;
        }

        parsed = (int)numeric;
        return true;
    }

    private static bool TryReadDouble(object? value, out double parsed)
    {
        switch (value)
        {
            case null:
                parsed = 0;
                return false;
            case JsonElement json when json.ValueKind == JsonValueKind.Number:
                return json.TryGetDouble(out parsed) && double.IsFinite(parsed);
            case byte number:
                parsed = number;
                return true;
            case short number:
                parsed = number;
                return true;
            case int number:
                parsed = number;
                return true;
            case long number:
                parsed = number;
                return true;
            case float number when float.IsFinite(number):
                parsed = number;
                return true;
            case double number when double.IsFinite(number):
                parsed = number;
                return true;
            case decimal number:
                parsed = (double)number;
                return double.IsFinite(parsed);
            default:
                return double.TryParse(
                    Convert.ToString(value, CultureInfo.InvariantCulture),
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out parsed) && double.IsFinite(parsed);
        }
    }

    private static bool IsValid(DetectionBox? detection) =>
        detection != null &&
        float.IsFinite(detection.Confidence) &&
        float.IsFinite(detection.X) &&
        float.IsFinite(detection.Y) &&
        float.IsFinite(detection.Width) &&
        float.IsFinite(detection.Height) &&
        detection.Width >= 0 &&
        detection.Height >= 0;

    private static double ClampConfidence(double confidence) => Math.Clamp(confidence, 0.0, 1.0);

    private static DetectionBox Clone(DetectionBox source) => new(
        source.Label,
        source.Confidence,
        source.X,
        source.Y,
        source.Width,
        source.Height);

    private sealed class DictionaryAdapter : IDictionary
    {
        private readonly IReadOnlyDictionary<string, object?> _values;

        public DictionaryAdapter(IReadOnlyDictionary<string, object?> values) => _values = values;
        public object? this[object key] { get => _values.TryGetValue(key.ToString() ?? string.Empty, out var value) ? value : null; set => throw new NotSupportedException(); }
        public ICollection Keys => _values.Keys.ToArray();
        public ICollection Values => _values.Values.ToArray();
        public bool IsReadOnly => true;
        public bool IsFixedSize => true;
        public int Count => _values.Count;
        public object SyncRoot => this;
        public bool IsSynchronized => false;
        public void Add(object key, object? value) => throw new NotSupportedException();
        public void Clear() => throw new NotSupportedException();
        public bool Contains(object key) => key is string name && _values.ContainsKey(name);
        public IDictionaryEnumerator GetEnumerator() => new DictionaryEnumerator(_values.GetEnumerator());
        public void Remove(object key) => throw new NotSupportedException();
        public void CopyTo(Array array, int index) => ((ICollection)_values.ToArray()).CopyTo(array, index);
        IEnumerator IEnumerable.GetEnumerator() => _values.GetEnumerator();
    }

    private sealed class DictionaryEnumerator : IDictionaryEnumerator
    {
        private readonly IEnumerator<KeyValuePair<string, object?>> _inner;
        public DictionaryEnumerator(IEnumerator<KeyValuePair<string, object?>> inner) => _inner = inner;
        public DictionaryEntry Entry => new(Key, Value);
        public object Key => _inner.Current.Key;
        public object? Value => _inner.Current.Value;
        public object Current => Entry;
        public bool MoveNext() => _inner.MoveNext();
        public void Reset() => _inner.Reset();
    }
}

public readonly record struct CanonicalDetectionDecision(bool IsOk, double Confidence);
