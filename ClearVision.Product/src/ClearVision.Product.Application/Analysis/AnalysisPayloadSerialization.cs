using System.Collections;
using System.Text.Json;
using ClearVision.Product.Application.DTOs;
using ClearVision.Product.Core.Entities;
using Microsoft.Extensions.Logging;

namespace ClearVision.Product.Application.Analysis;

public static class AnalysisPayloadSerialization
{
    private const int MaxSerializableDictionaryEntries = 128;
    private const int MaxSerializableCollectionItems = 256;
    private const int MaxSerializableStringChars = 16 * 1024;
    private const string TruncatedTextMarker = "\n...<truncated>";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static Dictionary<string, object>? DeserializeJsonDictionary(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        return JsonSerializer.Deserialize<Dictionary<string, object>>(json, JsonOptions);
    }

    public static AnalysisDataDto? DeserializeAnalysisData(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        return JsonSerializer.Deserialize<AnalysisDataDto>(json, JsonOptions);
    }

    public static void TrySetOutputDataJson(InspectionResult result, Dictionary<string, object>? outputData, ILogger logger)
    {
        if (outputData == null || outputData.Count == 0)
        {
            return;
        }

        var serializableData = BuildSerializableOutputData(outputData);
        if (serializableData.Count == 0)
        {
            return;
        }

        try
        {
            result.SetOutputDataJson(JsonSerializer.Serialize(serializableData));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[AnalysisPayloadSerialization] 序列化 outputData 失败");
        }
    }

    public static void TrySetAnalysisDataJson(InspectionResult result, AnalysisDataDto? analysisData, ILogger logger)
    {
        if (analysisData == null || analysisData.Cards.Count == 0)
        {
            return;
        }

        var serializableData = BuildSerializableAnalysisData(analysisData);
        if (serializableData.Cards.Count == 0)
        {
            return;
        }

        try
        {
            result.SetAnalysisDataJson(JsonSerializer.Serialize(serializableData));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[AnalysisPayloadSerialization] 序列化 analysisData 失败");
        }
    }

    public static Dictionary<string, object?> BuildSerializableOutputData(Dictionary<string, object> outputData)
    {
        var serializable = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        var includedEntries = 0;

        foreach (var kvp in outputData)
        {
            if (includedEntries >= MaxSerializableDictionaryEntries)
            {
                AddTruncationMetadata(
                    serializable,
                    includedEntries,
                    MaxSerializableDictionaryEntries,
                    outputData.Count);
                break;
            }

            if (IsExcludedOutput(kvp.Key, kvp.Value))
            {
                continue;
            }

            if (TryConvertOutputValue(kvp.Value, out var converted))
            {
                serializable[kvp.Key] = converted;
                includedEntries++;
            }
        }

        return serializable;
    }

    public static AnalysisDataDto BuildSerializableAnalysisData(AnalysisDataDto analysisData)
    {
        ArgumentNullException.ThrowIfNull(analysisData);

        var cards = new List<AnalysisCardDto>();
        foreach (var card in analysisData.Cards)
        {
            var fields = new List<AnalysisFieldDto>();
            foreach (var field in card.Fields)
            {
                if (!TryConvertAnalysisValue(field.Key, field.Value, out var converted))
                {
                    continue;
                }

                fields.Add(new AnalysisFieldDto
                {
                    Key = field.Key,
                    Label = field.Label,
                    Value = converted,
                    Unit = field.Unit,
                    DisplayHint = field.DisplayHint,
                    Variant = field.Variant,
                    DataType = field.DataType,
                    Status = field.Status
                });
            }

            var meta = BuildSerializableNullableDictionary(card.Meta);
            var message = card.Message == null ? null : TruncateString(card.Message);
            if (fields.Count == 0 && (meta == null || meta.Count == 0) && string.IsNullOrWhiteSpace(message))
            {
                continue;
            }

            cards.Add(new AnalysisCardDto
            {
                Id = card.Id,
                Category = card.Category,
                SourceOperatorId = card.SourceOperatorId,
                SourceOperatorType = card.SourceOperatorType,
                Title = TruncateString(card.Title),
                Status = card.Status,
                Priority = card.Priority,
                Message = message,
                Fields = fields,
                Meta = meta
            });
        }

        return new AnalysisDataDto
        {
            Version = analysisData.Version,
            Cards = cards,
            Summary = analysisData.Summary == null
                ? null
                : new AnalysisSummaryDto
                {
                    CardCount = cards.Count,
                    Categories = cards
                        .Select(card => card.Category)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList()
                }
        };
    }

    private static Dictionary<string, object?>? BuildSerializableNullableDictionary(Dictionary<string, object?>? values)
    {
        if (values == null || values.Count == 0)
        {
            return null;
        }

        var serializable = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        var includedEntries = 0;

        foreach (var (key, value) in values)
        {
            if (includedEntries >= MaxSerializableDictionaryEntries)
            {
                AddTruncationMetadata(
                    serializable,
                    includedEntries,
                    MaxSerializableDictionaryEntries,
                    values.Count);
                break;
            }

            if (TryConvertAnalysisValue(key, value, out var converted))
            {
                serializable[key] = converted;
                includedEntries++;
            }
        }

        return serializable;
    }

    private static bool TryConvertAnalysisValue(string key, object? value, out object? converted)
    {
        if (value == null)
        {
            converted = null;
            return true;
        }

        if (IsExcludedOutput(key, value))
        {
            converted = null;
            return false;
        }

        return TryConvertOutputValue(value, out converted);
    }

    private static bool IsExcludedOutput(string key, object? value)
    {
        if (key.Equals("Image", StringComparison.OrdinalIgnoreCase) ||
            key.Equals("Defects", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (value is byte[])
        {
            return true;
        }

        if (value == null)
        {
            return false;
        }

        if (IsLikelyInlineImagePayload(key, value))
        {
            return true;
        }

        return IsKnownImageCarrierType(value.GetType());
    }

    private static bool TryConvertOutputValue(object? value, out object? converted, int depth = 0)
    {
        const int maxDepth = 8;
        if (depth > maxDepth)
        {
            converted = value?.ToString();
            return converted != null;
        }

        if (value == null)
        {
            converted = null;
            return true;
        }

        if (IsKnownImageCarrierType(value.GetType()) || value is byte[])
        {
            converted = null;
            return false;
        }

        if (value is JsonElement jsonElement)
        {
            TryConvertJsonElementWithBudgets(jsonElement, out converted, out _, depth);
            return true;
        }

        if (value is string text)
        {
            converted = TruncateString(text);
            return true;
        }

        if (IsSimpleValue(value))
        {
            converted = value;
            return true;
        }

        if (value is IDictionary<string, object> typedDict)
        {
            var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            var includedEntries = 0;
            foreach (var (key, nestedValue) in typedDict)
            {
                if (includedEntries >= MaxSerializableDictionaryEntries)
                {
                    AddTruncationMetadata(
                        dict,
                        includedEntries,
                        MaxSerializableDictionaryEntries,
                        typedDict.Count);
                    break;
                }

                if (IsExcludedOutput(key, nestedValue))
                {
                    continue;
                }

                if (TryConvertOutputValue(nestedValue, out var nested, depth + 1))
                {
                    dict[key] = nested;
                    includedEntries++;
                }
            }

            converted = dict;
            return true;
        }

        if (value is IDictionary dictionary)
        {
            var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            var includedEntries = 0;
            foreach (DictionaryEntry entry in dictionary)
            {
                if (includedEntries >= MaxSerializableDictionaryEntries)
                {
                    AddTruncationMetadata(
                        dict,
                        includedEntries,
                        MaxSerializableDictionaryEntries,
                        dictionary.Count);
                    break;
                }

                var key = entry.Key?.ToString();
                if (string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }

                if (IsExcludedOutput(key, entry.Value))
                {
                    continue;
                }

                if (TryConvertOutputValue(entry.Value, out var nested, depth + 1))
                {
                    dict[key] = nested;
                    includedEntries++;
                }
            }

            converted = dict;
            return true;
        }

        if (value is IEnumerable enumerable && value is not string)
        {
            var list = new List<object?>();
            var totalCount = TryGetCollectionCount(value);
            var rawIndex = 0;
            var truncated = false;
            foreach (var item in enumerable)
            {
                if (rawIndex >= MaxSerializableCollectionItems)
                {
                    truncated = true;
                    break;
                }

                if (TryConvertOutputValue(item, out var nested, depth + 1))
                {
                    list.Add(nested);
                }

                rawIndex++;
            }

            if (totalCount.HasValue && totalCount.Value > MaxSerializableCollectionItems)
            {
                truncated = true;
            }

            converted = truncated
                ? BuildTruncatedCollectionPayload(list, totalCount)
                : list;
            return true;
        }

        if (TrySerializeJsonValue(value, out var serializedJsonElement))
        {
            TryConvertJsonElementWithBudgets(serializedJsonElement, out converted, out _, depth + 1);
            return true;
        }

        var fallbackText = TruncateString(value.ToString() ?? string.Empty);
        if (string.IsNullOrWhiteSpace(fallbackText))
        {
            converted = null;
            return false;
        }

        converted = fallbackText;
        return true;
    }

    private static bool TryConvertJsonElementWithBudgets(
        JsonElement value,
        out object? converted,
        out bool changed,
        int depth)
    {
        const int maxDepth = 8;
        changed = false;

        if (depth > maxDepth)
        {
            converted = TruncateString(value.GetRawText());
            changed = true;
            return true;
        }

        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
            {
                var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                var includedEntries = 0;
                foreach (var property in value.EnumerateObject())
                {
                    if (includedEntries >= MaxSerializableDictionaryEntries)
                    {
                        AddTruncationMetadata(dict, includedEntries, MaxSerializableDictionaryEntries);
                        changed = true;
                        break;
                    }

                    if (IsExcludedOutput(property.Name, property.Value))
                    {
                        changed = true;
                        continue;
                    }

                    if (TryConvertJsonElementWithBudgets(property.Value, out var nested, out var nestedChanged, depth + 1))
                    {
                        dict[property.Name] = nested;
                        includedEntries++;
                        changed |= nestedChanged;
                    }
                }

                converted = changed ? dict : value;
                return true;
            }

            case JsonValueKind.Array:
            {
                var list = new List<object?>();
                var totalCount = value.GetArrayLength();
                var rawIndex = 0;
                var arrayTruncated = false;
                foreach (var item in value.EnumerateArray())
                {
                    if (rawIndex >= MaxSerializableCollectionItems)
                    {
                        arrayTruncated = true;
                        changed = true;
                        break;
                    }

                    if (TryConvertJsonElementWithBudgets(item, out var nested, out var nestedChanged, depth + 1))
                    {
                        list.Add(nested);
                        changed |= nestedChanged;
                    }

                    rawIndex++;
                }

                if (totalCount > MaxSerializableCollectionItems)
                {
                    arrayTruncated = true;
                    changed = true;
                }

                converted = arrayTruncated
                    ? BuildTruncatedCollectionPayload(list, totalCount)
                    : (changed ? list : value);
                return true;
            }

            case JsonValueKind.String:
            {
                var text = value.GetString() ?? string.Empty;
                var truncated = TruncateString(text);
                changed = text.Length != truncated.Length;
                converted = changed ? truncated : value;
                return true;
            }

            default:
                converted = value;
                return true;
        }
    }

    private static Dictionary<string, object?> BuildTruncatedCollectionPayload(
        List<object?> items,
        int? totalCount)
    {
        var payload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["items"] = items
        };

        AddTruncationMetadata(payload, items.Count, MaxSerializableCollectionItems, totalCount);
        return payload;
    }

    private static void AddTruncationMetadata(
        Dictionary<string, object?> target,
        int shownCount,
        int limit,
        int? totalCount = null)
    {
        target["__truncated"] = true;
        target["__shownCount"] = shownCount;
        target["__limit"] = limit;
        if (totalCount.HasValue)
        {
            target["__totalCount"] = totalCount.Value;
        }
    }

    private static int? TryGetCollectionCount(object value)
    {
        return value is ICollection collection
            ? collection.Count
            : null;
    }

    private static string TruncateString(string value)
    {
        return value.Length > MaxSerializableStringChars
            ? value[..MaxSerializableStringChars] + TruncatedTextMarker
            : value;
    }

    private static bool TrySerializeJsonValue(object value, out JsonElement jsonElement)
    {
        try
        {
            jsonElement = JsonSerializer.SerializeToElement(value, value.GetType());
            return true;
        }
        catch
        {
            jsonElement = default;
            return false;
        }
    }

    private static bool IsSimpleValue(object value)
    {
        var type = value.GetType();
        return type.IsPrimitive ||
               type.IsEnum ||
               value is string ||
               value is decimal ||
               value is DateTime ||
               value is DateTimeOffset ||
               value is Guid ||
               value is TimeSpan;
    }

    private static bool IsKnownImageCarrierType(Type type)
    {
        var fullName = type.FullName;
        return string.Equals(fullName, "OpenCvSharp.Mat", StringComparison.Ordinal) ||
               string.Equals(fullName, "ClearVision.Product.Infrastructure.Operators.ImageWrapper", StringComparison.Ordinal);
    }

    private static bool IsLikelyInlineImagePayload(string key, object value)
    {
        if (!IsImagePayloadKey(key))
        {
            return false;
        }

        return value switch
        {
            string text => text.Length > 128,
            JsonElement jsonElement when jsonElement.ValueKind == JsonValueKind.String =>
                (jsonElement.GetString()?.Length ?? 0) > 128,
            _ => IsKnownImageCarrierType(value.GetType())
        };
    }

    private static bool IsImagePayloadKey(string key)
    {
        return key.Equals("Image", StringComparison.OrdinalIgnoreCase) ||
               key.Contains("ImageData", StringComparison.OrdinalIgnoreCase) ||
               key.Contains("ImageBase64", StringComparison.OrdinalIgnoreCase) ||
               key.Contains("OutputImage", StringComparison.OrdinalIgnoreCase) ||
               key.Contains("OriginalImage", StringComparison.OrdinalIgnoreCase) ||
               key.Contains("Bitmap", StringComparison.OrdinalIgnoreCase) ||
               key.Contains("Base64", StringComparison.OrdinalIgnoreCase);
    }
}
