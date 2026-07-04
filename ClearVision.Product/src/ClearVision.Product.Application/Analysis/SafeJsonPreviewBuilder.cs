using System.Text.Json;
using System.Text.RegularExpressions;

namespace ClearVision.Product.Application.Analysis;

public sealed class SafeJsonPreview
{
    public bool IsPresent { get; init; }

    public bool IsJson { get; init; }

    public object? Value { get; init; }

    public bool WasTruncated { get; init; }

    public bool WasRedacted { get; init; }

    public string? Message { get; init; }

    public string? Error { get; init; }
}

public static class SafeJsonPreviewBuilder
{
    private const int MaxDepth = 6;
    private const int MaxObjectProperties = 64;
    private const int MaxArrayItems = 64;
    private const int MaxStringChars = 512;
    private const string RedactedValue = "[REDACTED]";
    private const string RedactedPathValue = "[REDACTED_PATH]";
    private const string OmittedPayloadValue = "[OMITTED_LARGE_PAYLOAD]";
    private const string TruncatedMarker = "...<truncated>";

    private static readonly Regex WindowsAbsolutePathPattern = new(
        @"^[A-Za-z]:[\\/].+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex UncPathPattern = new(
        @"^\\\\[^\\]+\\[^\\]+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static SafeJsonPreview Build(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new SafeJsonPreview
            {
                IsPresent = false,
                IsJson = false,
                Value = null,
                Message = "No stored JSON payload."
            };
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var state = new PreviewState();
            var value = ConvertElement(document.RootElement, propertyName: null, depth: 0, state);
            return new SafeJsonPreview
            {
                IsPresent = true,
                IsJson = true,
                Value = value,
                WasTruncated = state.WasTruncated,
                WasRedacted = state.WasRedacted,
                Message = state.WasTruncated
                    ? "JSON preview truncated. Open a narrower detail view or export path for full raw data."
                    : null
            };
        }
        catch (JsonException)
        {
            return MalformedJson();
        }
        catch (NotSupportedException)
        {
            return MalformedJson();
        }
    }

    private static SafeJsonPreview MalformedJson()
    {
        return new SafeJsonPreview
        {
            IsPresent = true,
            IsJson = false,
            Value = null,
            WasTruncated = true,
            Message = "Stored JSON payload could not be parsed and raw content was hidden.",
            Error = "MalformedJson"
        };
    }

    private static object? ConvertElement(JsonElement element, string? propertyName, int depth, PreviewState state)
    {
        if (!string.IsNullOrWhiteSpace(propertyName))
        {
            if (IsSecretLikeKey(propertyName))
            {
                state.WasRedacted = true;
                return RedactedValue;
            }

            if (IsLargePayloadKey(propertyName))
            {
                state.WasRedacted = true;
                state.WasTruncated = true;
                return OmittedPayloadValue;
            }
        }

        if (depth > MaxDepth)
        {
            state.WasTruncated = true;
            return TruncateString(element.GetRawText(), state);
        }

        return element.ValueKind switch
        {
            JsonValueKind.Object => ConvertObject(element, depth, state),
            JsonValueKind.Array => ConvertArray(element, depth, state),
            JsonValueKind.String => ConvertString(element.GetString() ?? string.Empty, state),
            JsonValueKind.Number => ConvertNumber(element),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            _ => TruncateString(element.GetRawText(), state)
        };
    }

    private static object ConvertObject(JsonElement element, int depth, PreviewState state)
    {
        var output = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        var shouldRedactValueProperty = HasSecretLikeFieldKey(element);
        var index = 0;
        var total = 0;
        foreach (var property in element.EnumerateObject())
        {
            total++;
            if (index >= MaxObjectProperties)
            {
                state.WasTruncated = true;
                continue;
            }

            if (shouldRedactValueProperty && property.Name.Equals("value", StringComparison.OrdinalIgnoreCase))
            {
                state.WasRedacted = true;
                output[property.Name] = RedactedValue;
            }
            else
            {
                output[property.Name] = ConvertElement(property.Value, property.Name, depth + 1, state);
            }

            index++;
        }

        if (total > index)
        {
            output["__truncated"] = true;
            output["__shownCount"] = index;
            output["__totalCount"] = total;
        }

        return output;
    }

    private static bool HasSecretLikeFieldKey(JsonElement element)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (!property.Name.Equals("key", StringComparison.OrdinalIgnoreCase) &&
                !property.Name.Equals("name", StringComparison.OrdinalIgnoreCase) &&
                !property.Name.Equals("label", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (property.Value.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var key = property.Value.GetString();
            if (!string.IsNullOrWhiteSpace(key) && IsSecretLikeKey(key))
            {
                return true;
            }
        }

        return false;
    }

    private static object ConvertArray(JsonElement element, int depth, PreviewState state)
    {
        var output = new List<object?>();
        var total = element.GetArrayLength();
        var index = 0;
        foreach (var item in element.EnumerateArray())
        {
            if (index >= MaxArrayItems)
            {
                state.WasTruncated = true;
                break;
            }

            output.Add(ConvertElement(item, propertyName: null, depth + 1, state));
            index++;
        }

        if (total <= MaxArrayItems)
        {
            return output;
        }

        return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["items"] = output,
            ["__truncated"] = true,
            ["__shownCount"] = output.Count,
            ["__totalCount"] = total
        };
    }

    private static object ConvertString(string value, PreviewState state)
    {
        if (IsLocalAbsolutePath(value))
        {
            state.WasRedacted = true;
            return RedactedPathValue;
        }

        if (LooksLikeBearerSecret(value))
        {
            state.WasRedacted = true;
            return RedactedValue;
        }

        return TruncateString(value, state);
    }

    private static object ConvertNumber(JsonElement element)
    {
        if (element.TryGetInt64(out var longValue))
        {
            return longValue;
        }

        return element.TryGetDouble(out var doubleValue)
            ? doubleValue
            : element.GetRawText();
    }

    private static string TruncateString(string value, PreviewState state)
    {
        if (value.Length <= MaxStringChars)
        {
            return value;
        }

        state.WasTruncated = true;
        return value[..MaxStringChars] + TruncatedMarker;
    }

    private static bool IsSecretLikeKey(string key)
    {
        return key.Contains("password", StringComparison.OrdinalIgnoreCase) ||
               key.Contains("passwd", StringComparison.OrdinalIgnoreCase) ||
               key.Contains("token", StringComparison.OrdinalIgnoreCase) ||
               key.Contains("secret", StringComparison.OrdinalIgnoreCase) ||
               key.Contains("apiKey", StringComparison.OrdinalIgnoreCase) ||
               key.Contains("api_key", StringComparison.OrdinalIgnoreCase) ||
               key.Contains("authorization", StringComparison.OrdinalIgnoreCase) ||
               key.Contains("credential", StringComparison.OrdinalIgnoreCase) ||
               key.Contains("connectionString", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsLargePayloadKey(string key)
    {
        return key.Contains("outputImage", StringComparison.OrdinalIgnoreCase) ||
               key.Contains("originalImage", StringComparison.OrdinalIgnoreCase) ||
               key.Contains("imageBase64", StringComparison.OrdinalIgnoreCase) ||
               key.Contains("imageData", StringComparison.OrdinalIgnoreCase) ||
               key.Contains("bitmap", StringComparison.OrdinalIgnoreCase) ||
               key.Contains("scene", StringComparison.OrdinalIgnoreCase) ||
               key.Contains("artifactPayload", StringComparison.OrdinalIgnoreCase) ||
               key.Contains("previewArtifact", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsLocalAbsolutePath(string value)
    {
        var text = value.Trim();
        return WindowsAbsolutePathPattern.IsMatch(text) ||
               UncPathPattern.IsMatch(text) ||
               text.StartsWith("/Users/", StringComparison.Ordinal) ||
               text.StartsWith("/home/", StringComparison.Ordinal) ||
               text.StartsWith("/var/", StringComparison.Ordinal) ||
               text.StartsWith("/tmp/", StringComparison.Ordinal);
    }

    private static bool LooksLikeBearerSecret(string value)
    {
        var text = value.Trim();
        return text.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ||
               text.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase);
    }

    private sealed class PreviewState
    {
        public bool WasTruncated { get; set; }

        public bool WasRedacted { get; set; }
    }
}
