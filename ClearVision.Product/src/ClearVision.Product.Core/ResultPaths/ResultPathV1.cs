using System.Collections;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace ClearVision.Product.Core.ResultPaths;

public static class ResultPathV1
{
    public const int Version = 1;
    public const string Root = "$";
    public const int MaxPathChars = 512;
    public const int MaxSegments = 32;
    public const int MaxKeyChars = 128;
    public const int MaxStableIdChars = 128;
    public const int MaxStableCollectionScanCount = 256;
}

public enum ResultPathSegmentKind
{
    ObjectKey = 0,
    StableId = 1
}

public sealed record ResultPathSegment(ResultPathSegmentKind Kind, string Value)
{
    public static ResultPathSegment ObjectKey(string key) => new(ResultPathSegmentKind.ObjectKey, key);

    public static ResultPathSegment StableId(string stableId) => new(ResultPathSegmentKind.StableId, stableId);
}

public sealed record ResultPath(int Version, IReadOnlyList<ResultPathSegment> Segments, string CanonicalPath);

public sealed record ResultPathDiagnostic(string Code, string Message, int? SegmentIndex = null)
{
    public override string ToString() =>
        SegmentIndex.HasValue
            ? $"{Code} at segment {SegmentIndex.Value}: {Message}"
            : $"{Code}: {Message}";
}

public sealed record ResultPathParseResult(ResultPath? Path, ResultPathDiagnostic? Diagnostic)
{
    public bool Succeeded => Path != null && Diagnostic == null;
}

public sealed record ResultPathResolutionResult(object? Value, ResultPath? Path, ResultPathDiagnostic? Diagnostic)
{
    public bool Succeeded => Diagnostic == null;
}

public readonly record struct ResultPathStableIdItem(string? StableId, object? Value);

public interface IResultPathStableIdCollectionAdapter
{
    bool CanRead(object? collection);

    IEnumerable<ResultPathStableIdItem> Enumerate(object collection);
}

public interface IResultPathResourceClassifier
{
    bool IsForbiddenResource(object? value);
}

public sealed class ResultPathResolverOptions
{
    public IReadOnlyList<IResultPathStableIdCollectionAdapter> StableIdAdapters { get; init; } =
        Array.Empty<IResultPathStableIdCollectionAdapter>();

    public IResultPathResourceClassifier ResourceClassifier { get; init; } =
        ResultPathDefaultResourceClassifier.Instance;
}

public sealed class ResultPathDefaultResourceClassifier : IResultPathResourceClassifier
{
    public static ResultPathDefaultResourceClassifier Instance { get; } = new();

    private ResultPathDefaultResourceClassifier()
    {
    }

    public bool IsForbiddenResource(object? value)
    {
        if (value == null || value is JsonElement)
        {
            return false;
        }

        if (value is byte[] or Stream or Delegate or Type)
        {
            return true;
        }

        var type = value.GetType();
        var fullName = type.FullName ?? type.Name;
        var name = type.Name;
        return string.Equals(fullName, "OpenCvSharp.Mat", StringComparison.Ordinal) ||
               string.Equals(name, "Mat", StringComparison.Ordinal) ||
               string.Equals(name, "ImageWrapper", StringComparison.Ordinal) ||
               string.Equals(name, "Image", StringComparison.Ordinal) ||
               string.Equals(name, "Mask", StringComparison.Ordinal) ||
               fullName.Contains(".Scripting.", StringComparison.OrdinalIgnoreCase) ||
               fullName.Contains("Script", StringComparison.OrdinalIgnoreCase);
    }
}

public static class ResultPathFormatter
{
    public static string Format(ResultPath path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return Format(path.Segments);
    }

    public static string Format(IReadOnlyList<ResultPathSegment> segments)
    {
        ArgumentNullException.ThrowIfNull(segments);
        var parts = new List<string>(segments.Count + 1) { ResultPathV1.Root };
        foreach (var segment in segments)
        {
            parts.Add(segment.Kind switch
            {
                ResultPathSegmentKind.ObjectKey => FormatObjectKeySegment(segment.Value),
                ResultPathSegmentKind.StableId => FormatStableIdSegment(segment.Value),
                _ => throw new ArgumentOutOfRangeException(nameof(segments), "Unknown ResultPath segment kind.")
            });
        }

        return string.Concat(parts);
    }

    public static string FormatObjectPath(IEnumerable<string> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        var segments = keys.Select(ResultPathSegment.ObjectKey).ToList();
        return Format(segments);
    }

    public static string FormatObjectKeySegment(string key) => $"[{FormatJsonString(key)}]";

    public static string FormatStableIdSegment(string stableId) => $"[@id={FormatJsonString(stableId)}]";

    internal static string FormatJsonString(string value)
    {
        var builder = new StringBuilder(value.Length + 2);
        builder.Append('"');
        foreach (var ch in value)
        {
            switch (ch)
            {
                case '"':
                    builder.Append("\\\"");
                    break;
                case '\\':
                    builder.Append("\\\\");
                    break;
                case '\b':
                    builder.Append("\\b");
                    break;
                case '\f':
                    builder.Append("\\f");
                    break;
                case '\n':
                    builder.Append("\\n");
                    break;
                case '\r':
                    builder.Append("\\r");
                    break;
                case '\t':
                    builder.Append("\\t");
                    break;
                default:
                    if (char.IsControl(ch))
                    {
                        builder.Append("\\u");
                        builder.Append(((int)ch).ToString("X4", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        builder.Append(ch);
                    }
                    break;
            }
        }

        builder.Append('"');
        return builder.ToString();
    }
}

public static class ResultPathParser
{
    public static ResultPathParseResult Parse(int version, string? path)
    {
        if (version != ResultPathV1.Version)
        {
            return Failure("RP100", $"ResultPath version '{version.ToString(CultureInfo.InvariantCulture)}' is not supported.");
        }

        if (path == null)
        {
            return Failure("RP101", "ResultPath is required.");
        }

        if (path.Length == 0)
        {
            return Failure("RP101", "ResultPath cannot be empty.");
        }

        if (path.Length > ResultPathV1.MaxPathChars)
        {
            return Failure("RP102", $"ResultPath exceeds the {ResultPathV1.MaxPathChars.ToString(CultureInfo.InvariantCulture)} character limit.");
        }

        if (path[0] != '$')
        {
            return Failure("RP103", "ResultPath must start with the root token '$'.");
        }

        if (path.Length == 1)
        {
            return Success([]);
        }

        var segments = new List<ResultPathSegment>();
        var index = 1;
        while (index < path.Length)
        {
            if (segments.Count >= ResultPathV1.MaxSegments)
            {
                return Failure("RP108", $"ResultPath exceeds the {ResultPathV1.MaxSegments.ToString(CultureInfo.InvariantCulture)} segment limit.", segments.Count);
            }

            if (path[index] != '[')
            {
                return Failure("RP104", "Only bracket object-key and stable-id selector segments are supported.", segments.Count);
            }

            var result = ParseBracketSegment(path, index, segments.Count);
            if (!result.Succeeded)
            {
                return new ResultPathParseResult(null, result.Diagnostic);
            }

            segments.Add(result.Segment!);
            index = result.NextIndex;
        }

        var canonical = ResultPathFormatter.Format(segments);
        if (!string.Equals(canonical, path, StringComparison.Ordinal))
        {
            return Failure("RP107", "ResultPath is valid but not canonical.", null);
        }

        return new ResultPathParseResult(new ResultPath(ResultPathV1.Version, segments, canonical), null);
    }

    public static bool TryParse(int version, string? path, out ResultPath? result, out ResultPathDiagnostic? diagnostic)
    {
        var parsed = Parse(version, path);
        result = parsed.Path;
        diagnostic = parsed.Diagnostic;
        return parsed.Succeeded;
    }

    private static SegmentParseResult ParseBracketSegment(string path, int bracketIndex, int segmentIndex)
    {
        if (bracketIndex + 1 >= path.Length)
        {
            return SegmentFailure("RP105", "ResultPath bracket segment is incomplete.", segmentIndex);
        }

        if (path[bracketIndex + 1] == '"')
        {
            return ParseObjectKeySegment(path, bracketIndex, segmentIndex);
        }

        if (path[bracketIndex + 1] == '@')
        {
            return ParseStableIdSegment(path, bracketIndex, segmentIndex);
        }

        return SegmentFailure("RP104", "Unsupported ResultPath bracket segment syntax.", segmentIndex);
    }

    private static SegmentParseResult ParseObjectKeySegment(string path, int bracketIndex, int segmentIndex)
    {
        var stringStart = bracketIndex + 1;
        if (!TryFindJsonStringEnd(path, stringStart, out var stringEnd, out var diagnostic))
        {
            return SegmentFailure(diagnostic!.Code, diagnostic.Message, segmentIndex);
        }

        if (stringEnd + 1 >= path.Length || path[stringEnd + 1] != ']')
        {
            return SegmentFailure("RP105", "Object-key segment must end immediately after the JSON string.", segmentIndex);
        }

        if (!TryReadJsonString(path[stringStart..(stringEnd + 1)], out var key, out var readError))
        {
            return SegmentFailure("RP106", readError!, segmentIndex);
        }

        if (key.Length > ResultPathV1.MaxKeyChars)
        {
            return SegmentFailure("RP109", $"Object key exceeds the {ResultPathV1.MaxKeyChars.ToString(CultureInfo.InvariantCulture)} character limit.", segmentIndex);
        }

        var canonical = ResultPathFormatter.FormatObjectKeySegment(key);
        var actual = path[bracketIndex..(stringEnd + 2)];
        if (!string.Equals(canonical, actual, StringComparison.Ordinal))
        {
            return SegmentFailure("RP107", "Object-key segment is valid but not canonical.", segmentIndex);
        }

        return SegmentParseResult.Success(ResultPathSegment.ObjectKey(key), stringEnd + 2);
    }

    private static SegmentParseResult ParseStableIdSegment(string path, int bracketIndex, int segmentIndex)
    {
        const string prefix = "[@id=";
        if (!path.AsSpan(bracketIndex).StartsWith(prefix.AsSpan(), StringComparison.Ordinal))
        {
            return SegmentFailure("RP104", "Stable-ID selector must use exactly [@id=\"...\"] syntax.", segmentIndex);
        }

        var stringStart = bracketIndex + prefix.Length;
        if (stringStart >= path.Length || path[stringStart] != '"')
        {
            return SegmentFailure("RP104", "Stable-ID selector requires a JSON string ID.", segmentIndex);
        }

        if (!TryFindJsonStringEnd(path, stringStart, out var stringEnd, out var diagnostic))
        {
            return SegmentFailure(diagnostic!.Code, diagnostic.Message, segmentIndex);
        }

        if (stringEnd + 1 >= path.Length || path[stringEnd + 1] != ']')
        {
            return SegmentFailure("RP105", "Stable-ID selector must end immediately after the JSON string.", segmentIndex);
        }

        if (!TryReadJsonString(path[stringStart..(stringEnd + 1)], out var stableId, out var readError))
        {
            return SegmentFailure("RP106", readError!, segmentIndex);
        }

        if (stableId.Length == 0)
        {
            return SegmentFailure("RP109", "Stable-ID selector cannot be empty.", segmentIndex);
        }

        if (stableId.Length > ResultPathV1.MaxStableIdChars)
        {
            return SegmentFailure("RP109", $"Stable ID exceeds the {ResultPathV1.MaxStableIdChars.ToString(CultureInfo.InvariantCulture)} character limit.", segmentIndex);
        }

        var canonical = ResultPathFormatter.FormatStableIdSegment(stableId);
        var actual = path[bracketIndex..(stringEnd + 2)];
        if (!string.Equals(canonical, actual, StringComparison.Ordinal))
        {
            return SegmentFailure("RP107", "Stable-ID selector is valid but not canonical.", segmentIndex);
        }

        return SegmentParseResult.Success(ResultPathSegment.StableId(stableId), stringEnd + 2);
    }

    private static bool TryFindJsonStringEnd(
        string path,
        int quoteIndex,
        out int stringEnd,
        out ResultPathDiagnostic? diagnostic)
    {
        stringEnd = -1;
        diagnostic = null;
        if (quoteIndex >= path.Length || path[quoteIndex] != '"')
        {
            diagnostic = new ResultPathDiagnostic("RP105", "JSON string must start with a quote.");
            return false;
        }

        var escaped = false;
        for (var i = quoteIndex + 1; i < path.Length; i++)
        {
            var ch = path[i];
            if (escaped)
            {
                escaped = false;
                continue;
            }

            if (ch == '\\')
            {
                escaped = true;
                continue;
            }

            if (ch == '"')
            {
                stringEnd = i;
                return true;
            }
        }

        diagnostic = new ResultPathDiagnostic("RP105", "JSON string is unterminated.");
        return false;
    }

    private static bool TryReadJsonString(string literal, out string value, out string? error)
    {
        value = string.Empty;
        error = null;
        try
        {
            value = JsonSerializer.Deserialize<string>(literal) ?? string.Empty;
            return true;
        }
        catch (JsonException)
        {
            error = "JSON string escaping is invalid.";
            return false;
        }
        catch (NotSupportedException)
        {
            error = "JSON string escaping is invalid.";
            return false;
        }
    }

    private static ResultPathParseResult Success(IReadOnlyList<ResultPathSegment> segments)
    {
        var canonical = ResultPathFormatter.Format(segments);
        return new ResultPathParseResult(new ResultPath(ResultPathV1.Version, segments, canonical), null);
    }

    private static ResultPathParseResult Failure(string code, string message, int? segmentIndex = null) =>
        new(null, new ResultPathDiagnostic(code, message, segmentIndex));

    private static SegmentParseResult SegmentFailure(string code, string message, int? segmentIndex = null) =>
        new(null, 0, new ResultPathDiagnostic(code, message, segmentIndex));

    private sealed record SegmentParseResult(ResultPathSegment? Segment, int NextIndex, ResultPathDiagnostic? Diagnostic)
    {
        public bool Succeeded => Segment != null && Diagnostic == null;

        public static SegmentParseResult Success(ResultPathSegment segment, int nextIndex) => new(segment, nextIndex, null);
    }
}

public static class ResultPathResolver
{
    public static ResultPathResolutionResult Resolve(
        int version,
        string? path,
        object? root,
        ResultPathResolverOptions? options = null)
    {
        var parsed = ResultPathParser.Parse(version, path);
        if (!parsed.Succeeded)
        {
            return new ResultPathResolutionResult(null, null, parsed.Diagnostic);
        }

        return Resolve(parsed.Path!, root, options);
    }

    public static ResultPathResolutionResult Resolve(
        ResultPath path,
        object? root,
        ResultPathResolverOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(path);
        options ??= new ResultPathResolverOptions();

        object? current = root;
        for (var segmentIndex = 0; segmentIndex < path.Segments.Count; segmentIndex++)
        {
            var segment = path.Segments[segmentIndex];
            var boundary = ValidateIntermediateContainer(current, options, segmentIndex);
            if (boundary != null)
            {
                return Failure(path, boundary);
            }

            switch (segment.Kind)
            {
                case ResultPathSegmentKind.ObjectKey:
                    if (!TryResolveObjectKey(current, segment.Value, out current, out var diagnostic, segmentIndex))
                    {
                        return Failure(path, diagnostic!);
                    }
                    break;
                case ResultPathSegmentKind.StableId:
                    if (!TryResolveStableId(current, segment.Value, options, out current, out var stableDiagnostic, segmentIndex))
                    {
                        return Failure(path, stableDiagnostic!);
                    }
                    break;
                default:
                    return Failure(path, new ResultPathDiagnostic("RP104", "Unsupported ResultPath segment kind.", segmentIndex));
            }
        }

        var terminal = ValidateTerminalScalar(current, options, path.Segments.Count);
        return terminal == null
            ? new ResultPathResolutionResult(current, path, null)
            : Failure(path, terminal);
    }

    public static bool IsTerminalScalar(object? value, ResultPathResolverOptions? options = null)
    {
        options ??= new ResultPathResolverOptions();
        return ValidateTerminalScalar(value, options, null) == null;
    }

    private static ResultPathResolutionResult Failure(ResultPath? path, ResultPathDiagnostic diagnostic) =>
        new(null, path, diagnostic);

    private static ResultPathDiagnostic? ValidateIntermediateContainer(
        object? value,
        ResultPathResolverOptions options,
        int segmentIndex)
    {
        if (options.ResourceClassifier.IsForbiddenResource(value))
        {
            return new ResultPathDiagnostic("RP119", "ResultPath traversal encountered a forbidden resource value.", segmentIndex);
        }

        if (IsScalarValue(value, out var scalarDiagnostic))
        {
            return WithSegment(
                scalarDiagnostic ?? new ResultPathDiagnostic("RP117", "ResultPath traversal encountered a scalar before the path ended."),
                segmentIndex);
        }

        return null;
    }

    private static ResultPathDiagnostic? ValidateTerminalScalar(
        object? value,
        ResultPathResolverOptions options,
        int? segmentIndex)
    {
        if (options.ResourceClassifier.IsForbiddenResource(value))
        {
            return new ResultPathDiagnostic("RP119", "ResultPath resolved to a forbidden resource value.", segmentIndex);
        }

        if (IsScalarValue(value, out var scalarDiagnostic))
        {
            return scalarDiagnostic == null || scalarDiagnostic.SegmentIndex.HasValue || !segmentIndex.HasValue
                ? scalarDiagnostic
                : WithSegment(scalarDiagnostic, segmentIndex.Value);
        }

        return new ResultPathDiagnostic("RP118", "ResultPath terminal value is not a supported scalar.", segmentIndex);
    }

    private static ResultPathDiagnostic WithSegment(ResultPathDiagnostic diagnostic, int segmentIndex) =>
        diagnostic.SegmentIndex.HasValue
            ? diagnostic
            : diagnostic with { SegmentIndex = segmentIndex };

    private static bool TryResolveObjectKey(
        object? value,
        string key,
        out object? resolved,
        out ResultPathDiagnostic? diagnostic,
        int segmentIndex)
    {
        resolved = null;
        diagnostic = null;

        if (value is JsonElement element)
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                diagnostic = new ResultPathDiagnostic("RP110", "Object-key segment requires a JSON object container.", segmentIndex);
                return false;
            }

            if (!element.TryGetProperty(key, out var property))
            {
                diagnostic = new ResultPathDiagnostic("RP111", "ResultPath object key was not found.", segmentIndex);
                return false;
            }

            resolved = property.Clone();
            return true;
        }

        if (TryGetStringDictionaryValue(value, key, out resolved, out var dictionarySupported))
        {
            return true;
        }

        diagnostic = dictionarySupported
            ? new ResultPathDiagnostic("RP111", "ResultPath object key was not found.", segmentIndex)
            : new ResultPathDiagnostic("RP110", "Object-key segment requires an approved string-key dictionary or JSON object container.", segmentIndex);
        return false;
    }

    private static bool TryResolveStableId(
        object? value,
        string stableId,
        ResultPathResolverOptions options,
        out object? resolved,
        out ResultPathDiagnostic? diagnostic,
        int segmentIndex)
    {
        resolved = null;
        diagnostic = null;

        foreach (var adapter in options.StableIdAdapters)
        {
            if (!adapter.CanRead(value))
            {
                continue;
            }

            object? match = null;
            var matched = false;
            var scanned = 0;
            var seenIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in adapter.Enumerate(value!))
            {
                if (scanned >= ResultPathV1.MaxStableCollectionScanCount)
                {
                    diagnostic = new ResultPathDiagnostic("RP116", "Stable-ID collection scan exceeded the configured limit.", segmentIndex);
                    return false;
                }

                scanned++;
                if (string.IsNullOrEmpty(item.StableId))
                {
                    diagnostic = new ResultPathDiagnostic("RP113", "Stable-ID collection contains an item without a usable stable ID.", segmentIndex);
                    return false;
                }

                if (item.StableId.Length > ResultPathV1.MaxStableIdChars)
                {
                    diagnostic = new ResultPathDiagnostic("RP109", $"Stable ID exceeds the {ResultPathV1.MaxStableIdChars.ToString(CultureInfo.InvariantCulture)} character limit.", segmentIndex);
                    return false;
                }

                if (!seenIds.Add(item.StableId))
                {
                    diagnostic = new ResultPathDiagnostic("RP114", "Stable-ID collection contains duplicate IDs.", segmentIndex);
                    return false;
                }

                if (string.Equals(item.StableId, stableId, StringComparison.Ordinal))
                {
                    matched = true;
                    match = item.Value;
                }
            }

            if (!matched)
            {
                diagnostic = new ResultPathDiagnostic("RP115", "Stable ID was not found.", segmentIndex);
                return false;
            }

            resolved = match;
            return true;
        }

        diagnostic = new ResultPathDiagnostic("RP112", "Stable-ID selector requires a registered finite collection adapter.", segmentIndex);
        return false;
    }

    private static bool TryGetStringDictionaryValue(
        object? value,
        string key,
        out object? resolved,
        out bool dictionarySupported)
    {
        resolved = null;
        dictionarySupported = false;

        switch (value)
        {
            case IReadOnlyDictionary<string, object?> nullableReadOnly:
                dictionarySupported = true;
                foreach (var pair in nullableReadOnly)
                {
                    if (string.Equals(pair.Key, key, StringComparison.Ordinal))
                    {
                        resolved = pair.Value;
                        return true;
                    }
                }
                return false;
            case IDictionary<string, object?> nullableDictionary:
                dictionarySupported = true;
                foreach (var pair in nullableDictionary)
                {
                    if (string.Equals(pair.Key, key, StringComparison.Ordinal))
                    {
                        resolved = pair.Value;
                        return true;
                    }
                }
                return false;
            case IDictionary untypedDictionary:
                dictionarySupported = true;
                foreach (DictionaryEntry entry in untypedDictionary)
                {
                    if (entry.Key is string entryKey && string.Equals(entryKey, key, StringComparison.Ordinal))
                    {
                        resolved = entry.Value;
                        return true;
                    }
                }
                return false;
            default:
                return false;
        }
    }

    private static bool IsScalarValue(object? value, out ResultPathDiagnostic? diagnostic)
    {
        diagnostic = null;
        switch (value)
        {
            case null:
            case string:
            case char:
            case bool:
            case sbyte:
            case byte:
            case short:
            case ushort:
            case int:
            case uint:
            case long:
            case ulong:
            case decimal:
            case Guid:
            case DateTime:
            case DateTimeOffset:
            case TimeSpan:
                return true;
            case float floatValue:
                if (float.IsFinite(floatValue))
                {
                    return true;
                }

                diagnostic = new ResultPathDiagnostic("RP120", "ResultPath resolved to a non-finite floating point value.");
                return true;
            case double doubleValue:
                if (double.IsFinite(doubleValue))
                {
                    return true;
                }

                diagnostic = new ResultPathDiagnostic("RP120", "ResultPath resolved to a non-finite floating point value.");
                return true;
            case JsonElement element:
                return IsScalarJsonElement(element, out diagnostic);
            default:
                if (value.GetType().IsEnum)
                {
                    return true;
                }

                return false;
        }
    }

    private static bool IsScalarJsonElement(JsonElement element, out ResultPathDiagnostic? diagnostic)
    {
        diagnostic = null;
        switch (element.ValueKind)
        {
            case JsonValueKind.Null:
            case JsonValueKind.String:
            case JsonValueKind.True:
            case JsonValueKind.False:
                return true;
            case JsonValueKind.Number:
                try
                {
                    var number = element.GetDouble();
                    if (!double.IsFinite(number))
                    {
                        diagnostic = new ResultPathDiagnostic("RP120", "ResultPath resolved to a non-finite JSON number.");
                    }

                    return true;
                }
                catch (FormatException)
                {
                    diagnostic = new ResultPathDiagnostic("RP120", "ResultPath resolved to an unsupported JSON number.");
                    return true;
                }
            default:
                return false;
        }
    }
}
