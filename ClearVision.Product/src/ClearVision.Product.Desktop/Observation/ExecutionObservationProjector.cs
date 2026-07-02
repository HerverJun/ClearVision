using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using ClearVision.Product.Infrastructure.Operators;
using OpenCvSharp;

namespace ClearVision.Product.Desktop.Observation;

public static class ExecutionObservationProjector
{
    public const int MaxDepth = 4;
    public const int MaxObjectFields = 64;
    public const int MaxCollectionItems = 64;
    public const int MaxStringChars = 1024;
    public const int MaxNodes = 2048;
    public const int MaxDetailBytes = 256 * 1024;

    private const int MaxSummaryItems = 12;
    private const long MaxSafeIdentityValue = 9_007_199_254_740_991L;

    public static bool IsIdentityValueInSafeRange(long? value) =>
        !value.HasValue || value.Value is >= 0 and <= MaxSafeIdentityValue;

    public static ExecutionObservationEnvelopeV1 CreatePreviewObservation(ExecutionObservationPreviewInput input)
    {
        var context = new ProjectionContext();
        var rootValue = input.OutputData ?? new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        var detail = ProjectDetailNode(rootValue, "$", null, 0, addressableCandidate: false, context);
        var summary = BuildSummary(detail);

        return new ExecutionObservationEnvelopeV1
        {
            ObservedAtUtc = input.ObservedAtUtc ?? DateTimeOffset.UtcNow,
            Identity = new ExecutionObservationIdentityV1
            {
                ProjectId = input.ProjectId,
                TargetNodeId = input.TargetNodeId,
                DebugSessionId = input.DebugSessionId,
                ClientRequestSequence = input.ClientRequestSequence,
                FlowRevision = input.FlowRevision,
                RunId = null
            },
            Outcome = new ExecutionObservationOutcomeV1
            {
                Success = input.Success,
                ExecutionTimeMs = input.ExecutionTimeMs,
                ErrorMessage = ClipString(input.ErrorMessage, context, "$.outcome.errorMessage"),
                FailedOperatorId = input.FailedOperatorId,
                FailedOperatorName = ClipString(input.FailedOperatorName, context, "$.outcome.failedOperatorName"),
                FailedOperatorType = ClipString(input.FailedOperatorType, context, "$.outcome.failedOperatorType"),
                ExecutedOperatorCount = input.ExecutedOperatorCount
            },
            Summary = summary,
            Detail = detail,
            Diagnostics = context.Diagnostics,
            Limits = new ExecutionObservationLimitsV1(),
            Truncated = context.Truncated
        };
    }

    public static Dictionary<string, object> BuildLegacyOutputData(
        IReadOnlyDictionary<string, object> nodeOutput,
        Func<string, bool> shouldSkipKey)
    {
        var context = new LegacySanitizationContext();
        var response = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in nodeOutput.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (shouldSkipKey(pair.Key))
            {
                continue;
            }

            response[pair.Key] = SanitizeLegacyValue(pair.Value, 0, context)!;
        }

        return response;
    }

    private static ExecutionObservationDetailNodeV1 ProjectDetailNode(
        object? value,
        string pathHint,
        string? name,
        int depth,
        bool addressableCandidate,
        ProjectionContext context)
    {
        if (!context.TryReserveNode(pathHint, value))
        {
            return TruncatedNode("node-limit", pathHint, name, value, context);
        }

        if (context.ExceedsBudget(pathHint, value))
        {
            return TruncatedNode("byte-budget", pathHint, name, value, context);
        }

        if (value == null)
        {
            return ScalarNode("null", "null", null, pathHint, name, addressableCandidate);
        }

        if (TryProjectScalar(value, pathHint, name, addressableCandidate, context, out var scalarNode))
        {
            return scalarNode;
        }

        if (IsResourceLike(value))
        {
            return ResourceNode(value, pathHint, name, context);
        }

        if (!IsValueTypeLike(value) && !context.EnterReference(value, pathHint))
        {
            context.AddDiagnostic("circular-reference", "Circular reference omitted.", pathHint);
            return new ExecutionObservationDetailNodeV1
            {
                Kind = "circular",
                DisplayValue = "<circular reference>",
                OriginalType = value.GetType().FullName,
                Truncated = true,
                PathHint = pathHint,
                Addressable = false,
                Name = name
            };
        }

        try
        {
            if (depth >= MaxDepth)
            {
                return TruncatedNode("depth-limit", pathHint, name, value, context);
            }

            if (value is JsonElement jsonElement)
            {
                return ProjectJsonElement(jsonElement, pathHint, name, depth, addressableCandidate, context);
            }

            if (value is IDictionary dictionary)
            {
                return ProjectDictionary(dictionary, pathHint, name, depth, context);
            }

            if (value is IEnumerable enumerable and not string)
            {
                return ProjectEnumerable(enumerable, pathHint, name, depth, context);
            }

            return ProjectObject(value, pathHint, name, depth, context);
        }
        finally
        {
            if (!IsValueTypeLike(value))
            {
                context.LeaveReference(value);
            }
        }
    }

    private static ExecutionObservationDetailNodeV1 ProjectJsonElement(
        JsonElement element,
        string pathHint,
        string? name,
        int depth,
        bool addressableCandidate,
        ProjectionContext context)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Null:
                return ScalarNode("null", "null", typeof(JsonElement).FullName, pathHint, name, addressableCandidate);
            case JsonValueKind.True:
            case JsonValueKind.False:
                return ScalarNode("boolean", element.GetBoolean() ? "true" : "false", typeof(JsonElement).FullName, pathHint, name, addressableCandidate);
            case JsonValueKind.Number:
                return ScalarNode("number", element.GetRawText(), typeof(JsonElement).FullName, pathHint, name, addressableCandidate);
            case JsonValueKind.String:
                return ScalarNode("string", ClipString(element.GetString(), context, pathHint), typeof(JsonElement).FullName, pathHint, name, addressableCandidate);
            case JsonValueKind.Object:
            {
                var children = new List<ExecutionObservationDetailNodeV1>();
                var properties = element.EnumerateObject()
                    .OrderBy(property => property.Name, StringComparer.Ordinal)
                    .ToList();
                var truncated = properties.Count > MaxObjectFields;
                foreach (var property in properties.Take(MaxObjectFields))
                {
                    children.Add(ProjectDetailNode(
                        property.Value,
                        AppendObjectKey(pathHint, property.Name),
                        property.Name,
                        depth + 1,
                        addressableCandidate: true,
                        context));
                }

                if (truncated)
                {
                    context.AddDiagnostic("field-limit", $"Object field count {properties.Count} exceeds limit {MaxObjectFields}.", pathHint);
                }

                return new ExecutionObservationDetailNodeV1
                {
                    Kind = "object",
                    DisplayValue = $"{Math.Min(properties.Count, MaxObjectFields)}/{properties.Count} fields",
                    OriginalType = typeof(JsonElement).FullName,
                    Children = children,
                    Truncated = truncated,
                    PathHint = pathHint,
                    Addressable = false,
                    Name = name
                };
            }
            case JsonValueKind.Array:
            {
                var children = new List<ExecutionObservationDetailNodeV1>();
                var index = 0;
                var total = 0;
                foreach (var item in element.EnumerateArray())
                {
                    total++;
                    if (index < MaxCollectionItems)
                    {
                        children.Add(ProjectDetailNode(item, AppendArrayIndex(pathHint, index), index.ToString(CultureInfo.InvariantCulture), depth + 1, false, context));
                        index++;
                    }
                }

                var truncated = total > MaxCollectionItems;
                if (truncated)
                {
                    context.AddDiagnostic("collection-limit", $"Collection item count {total} exceeds limit {MaxCollectionItems}.", pathHint);
                }

                return new ExecutionObservationDetailNodeV1
                {
                    Kind = "array",
                    DisplayValue = $"{Math.Min(total, MaxCollectionItems)}/{total} items",
                    OriginalType = typeof(JsonElement).FullName,
                    Children = children,
                    Truncated = truncated,
                    PathHint = pathHint,
                    Addressable = false,
                    Name = name
                };
            }
            default:
                return ScalarNode("unknown", element.ValueKind.ToString(), typeof(JsonElement).FullName, pathHint, name, false);
        }
    }

    private static ExecutionObservationDetailNodeV1 ProjectDictionary(
        IDictionary dictionary,
        string pathHint,
        string? name,
        int depth,
        ProjectionContext context)
    {
        var entries = new List<(string Key, object? Value)>();
        foreach (DictionaryEntry entry in dictionary)
        {
            var key = entry.Key?.ToString();
            if (!string.IsNullOrWhiteSpace(key))
            {
                entries.Add((key!, entry.Value));
            }
        }

        entries.Sort((left, right) => string.Compare(left.Key, right.Key, StringComparison.Ordinal));
        var children = new List<ExecutionObservationDetailNodeV1>();
        foreach (var entry in entries.Take(MaxObjectFields))
        {
            children.Add(ProjectDetailNode(entry.Value, AppendObjectKey(pathHint, entry.Key), entry.Key, depth + 1, true, context));
        }

        var truncated = entries.Count > MaxObjectFields;
        if (truncated)
        {
            context.AddDiagnostic("field-limit", $"Dictionary field count {entries.Count} exceeds limit {MaxObjectFields}.", pathHint);
        }

        return new ExecutionObservationDetailNodeV1
        {
            Kind = "dictionary",
            DisplayValue = $"{Math.Min(entries.Count, MaxObjectFields)}/{entries.Count} fields",
            OriginalType = dictionary.GetType().FullName,
            Children = children,
            Truncated = truncated,
            PathHint = pathHint,
            Addressable = false,
            Name = name
        };
    }

    private static ExecutionObservationDetailNodeV1 ProjectEnumerable(
        IEnumerable enumerable,
        string pathHint,
        string? name,
        int depth,
        ProjectionContext context)
    {
        var children = new List<ExecutionObservationDetailNodeV1>();
        var total = 0;
        var truncated = false;
        try
        {
            foreach (var item in enumerable)
            {
                total++;
                if (children.Count < MaxCollectionItems)
                {
                    children.Add(ProjectDetailNode(item, AppendArrayIndex(pathHint, children.Count), children.Count.ToString(CultureInfo.InvariantCulture), depth + 1, false, context));
                    continue;
                }

                truncated = true;
                break;
            }
        }
        catch (Exception ex)
        {
            truncated = true;
            context.AddDiagnostic("enumeration-error", $"Enumeration failed: {ex.Message}", pathHint);
        }

        if (truncated)
        {
            context.AddDiagnostic("collection-limit", $"Collection exceeds limit {MaxCollectionItems}.", pathHint);
        }

        var declaredCount = enumerable is ICollection collection
            ? collection.Count
            : total;

        return new ExecutionObservationDetailNodeV1
        {
            Kind = "array",
            DisplayValue = $"{Math.Min(declaredCount, MaxCollectionItems)}/{declaredCount} items",
            OriginalType = enumerable.GetType().FullName,
            Children = children,
            Truncated = truncated || declaredCount > MaxCollectionItems,
            PathHint = pathHint,
            Addressable = false,
            Name = name
        };
    }

    private static ExecutionObservationDetailNodeV1 ProjectObject(
        object value,
        string pathHint,
        string? name,
        int depth,
        ProjectionContext context)
    {
        var type = value.GetType();
        var properties = type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(property => property.GetIndexParameters().Length == 0)
            .OrderBy(property => property.Name, StringComparer.Ordinal)
            .Take(MaxObjectFields + 1)
            .ToList();

        if (properties.Count == 0)
        {
            return new ExecutionObservationDetailNodeV1
            {
                Kind = "object",
                DisplayValue = SafeObjectDisplay(value),
                OriginalType = type.FullName,
                PathHint = pathHint,
                Addressable = false,
                Name = name
            };
        }

        var children = new List<ExecutionObservationDetailNodeV1>();
        foreach (var property in properties.Take(MaxObjectFields))
        {
            var propertyPath = AppendObjectKey(pathHint, property.Name);
            try
            {
                children.Add(ProjectDetailNode(property.GetValue(value), propertyPath, property.Name, depth + 1, true, context));
            }
            catch (Exception ex)
            {
                context.AddDiagnostic("getter-error", $"Getter '{property.Name}' failed: {ex.GetBaseException().Message}", propertyPath);
                children.Add(new ExecutionObservationDetailNodeV1
                {
                    Kind = "propertyError",
                    DisplayValue = "<getter failed>",
                    OriginalType = property.PropertyType.FullName,
                    Truncated = true,
                    PathHint = propertyPath,
                    Addressable = false,
                    Name = property.Name
                });
            }
        }

        var truncated = properties.Count > MaxObjectFields;
        if (truncated)
        {
            context.AddDiagnostic("field-limit", $"Object field count exceeds limit {MaxObjectFields}.", pathHint);
        }

        return new ExecutionObservationDetailNodeV1
        {
            Kind = "object",
            DisplayValue = $"{Math.Min(properties.Count, MaxObjectFields)}/{properties.Count} fields",
            OriginalType = type.FullName,
            Children = children,
            Truncated = truncated,
            PathHint = pathHint,
            Addressable = false,
            Name = name
        };
    }

    private static bool TryProjectScalar(
        object value,
        string pathHint,
        string? name,
        bool addressableCandidate,
        ProjectionContext context,
        out ExecutionObservationDetailNodeV1 node)
    {
        switch (value)
        {
            case string text:
                node = ScalarNode("string", ClipString(text, context, pathHint), value.GetType().FullName, pathHint, name, addressableCandidate);
                return true;
            case bool boolean:
                node = ScalarNode("boolean", boolean ? "true" : "false", value.GetType().FullName, pathHint, name, addressableCandidate);
                return true;
            case char character:
                node = ScalarNode("string", character.ToString(), value.GetType().FullName, pathHint, name, addressableCandidate);
                return true;
            case Guid guid:
                node = ScalarNode("guid", guid.ToString("D"), value.GetType().FullName, pathHint, name, addressableCandidate);
                return true;
            case DateTime dateTime:
                node = ScalarNode("dateTime", dateTime.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture), value.GetType().FullName, pathHint, name, addressableCandidate);
                return true;
            case DateTimeOffset dateTimeOffset:
                node = ScalarNode("dateTime", dateTimeOffset.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture), value.GetType().FullName, pathHint, name, addressableCandidate);
                return true;
            case TimeSpan timeSpan:
                node = ScalarNode("duration", timeSpan.ToString("c", CultureInfo.InvariantCulture), value.GetType().FullName, pathHint, name, addressableCandidate);
                return true;
            case float floatValue when !float.IsFinite(floatValue):
                context.AddDiagnostic("non-finite-number", $"Non-finite float '{floatValue}' converted to display text.", pathHint);
                node = ScalarNode("nonFiniteNumber", floatValue.ToString("R", CultureInfo.InvariantCulture), value.GetType().FullName, pathHint, name, false);
                return true;
            case double doubleValue when !double.IsFinite(doubleValue):
                context.AddDiagnostic("non-finite-number", $"Non-finite double '{doubleValue}' converted to display text.", pathHint);
                node = ScalarNode("nonFiniteNumber", doubleValue.ToString("R", CultureInfo.InvariantCulture), value.GetType().FullName, pathHint, name, false);
                return true;
            case sbyte or byte or short or ushort or int or uint or long or ulong or float or double or decimal:
                node = ScalarNode("number", Convert.ToString(value, CultureInfo.InvariantCulture), value.GetType().FullName, pathHint, name, addressableCandidate);
                return true;
        }

        var type = value.GetType();
        if (type.IsEnum)
        {
            node = ScalarNode("enum", value.ToString() ?? string.Empty, type.FullName, pathHint, name, addressableCandidate);
            return true;
        }

        node = new ExecutionObservationDetailNodeV1();
        return false;
    }

    private static ExecutionObservationDetailNodeV1 ScalarNode(
        string kind,
        string? displayValue,
        string? originalType,
        string pathHint,
        string? name,
        bool addressableCandidate)
    {
        return new ExecutionObservationDetailNodeV1
        {
            Kind = kind,
            DisplayValue = displayValue,
            OriginalType = originalType,
            PathHint = pathHint,
            Addressable = addressableCandidate && pathHint != "$",
            Name = name
        };
    }

    private static ExecutionObservationDetailNodeV1 ResourceNode(
        object value,
        string pathHint,
        string? name,
        ProjectionContext context)
    {
        var descriptor = BuildResourceDescriptor(value);
        if (descriptor.Truncated)
        {
            context.AddDiagnostic("resource-descriptor", descriptor.DisplayValue ?? "Resource content omitted.", pathHint);
        }

        return new ExecutionObservationDetailNodeV1
        {
            Kind = descriptor.Kind,
            DisplayValue = descriptor.DisplayValue,
            OriginalType = value.GetType().FullName,
            Children = descriptor.Metadata
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => ScalarNode("string", pair.Value, typeof(string).FullName, AppendObjectKey(pathHint, pair.Key), pair.Key, false))
                .ToList(),
            Truncated = true,
            PathHint = pathHint,
            Addressable = false,
            Name = name
        };
    }

    private static ExecutionObservationDetailNodeV1 TruncatedNode(
        string reason,
        string pathHint,
        string? name,
        object? value,
        ProjectionContext context)
    {
        context.AddDiagnostic(reason, $"Detail node truncated by {reason}.", pathHint);
        return new ExecutionObservationDetailNodeV1
        {
            Kind = "truncated",
            DisplayValue = $"<{reason}>",
            OriginalType = value?.GetType().FullName,
            Truncated = true,
            PathHint = pathHint,
            Addressable = false,
            Name = name
        };
    }

    private static List<ExecutionObservationSummaryItemV1> BuildSummary(ExecutionObservationDetailNodeV1 detail)
    {
        var result = new List<ExecutionObservationSummaryItemV1>();
        var stack = new Stack<ExecutionObservationDetailNodeV1>();
        stack.Push(detail);
        while (stack.Count > 0 && result.Count < MaxSummaryItems)
        {
            var current = stack.Pop();
            if (current.Addressable &&
                current.Children.Count == 0 &&
                !string.IsNullOrWhiteSpace(current.Name) &&
                !string.IsNullOrWhiteSpace(current.DisplayValue))
            {
                result.Add(new ExecutionObservationSummaryItemV1
                {
                    Key = current.Name!,
                    DisplayValue = current.DisplayValue!,
                    OriginalType = current.OriginalType,
                    PathHint = current.PathHint,
                    Addressable = current.Addressable
                });
            }

            for (var i = current.Children.Count - 1; i >= 0; i--)
            {
                stack.Push(current.Children[i]);
            }
        }

        return result;
    }

    private static object? SanitizeLegacyValue(object? value, int depth, LegacySanitizationContext context)
    {
        if (!context.TryReserve(value))
        {
            return BuildDescriptor("truncated", "node limit reached");
        }

        if (value == null)
        {
            return null;
        }

        if (TrySanitizeLegacyScalar(value, out var scalar))
        {
            return scalar;
        }

        if (IsResourceLike(value))
        {
            return BuildResourceDescriptor(value).ToLegacyDictionary();
        }

        if (depth >= MaxDepth)
        {
            return BuildDescriptor("truncated", "depth limit reached", value.GetType().FullName);
        }

        if (!IsValueTypeLike(value) && !context.EnterReference(value))
        {
            return BuildDescriptor("circular", "circular reference omitted", value.GetType().FullName);
        }

        try
        {
            if (value is JsonElement element)
            {
                return SanitizeJsonElement(element, depth, context);
            }

            if (value is IDictionary dictionary)
            {
                var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                var entries = new List<(string Key, object? Value)>();
                foreach (DictionaryEntry entry in dictionary)
                {
                    var key = entry.Key?.ToString();
                    if (!string.IsNullOrWhiteSpace(key))
                    {
                        entries.Add((key!, entry.Value));
                    }
                }

                entries.Sort((left, right) => string.Compare(left.Key, right.Key, StringComparison.Ordinal));
                foreach (var entry in entries.Take(MaxObjectFields))
                {
                    result[entry.Key] = SanitizeLegacyValue(entry.Value, depth + 1, context);
                }

                if (entries.Count > MaxObjectFields)
                {
                    result["__truncated"] = $"field limit {MaxObjectFields} of {entries.Count}";
                }

                return result;
            }

            if (value is IEnumerable enumerable and not string)
            {
                var result = new List<object?>();
                var count = 0;
                foreach (var item in enumerable)
                {
                    if (count >= MaxCollectionItems)
                    {
                        result.Add($"+ more items after {MaxCollectionItems}");
                        break;
                    }

                    result.Add(SanitizeLegacyValue(item, depth + 1, context));
                    count++;
                }

                return result;
            }

            return SanitizeObject(value, depth, context);
        }
        finally
        {
            if (!IsValueTypeLike(value))
            {
                context.LeaveReference(value);
            }
        }
    }

    private static object? SanitizeJsonElement(JsonElement element, int depth, LegacySanitizationContext context)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Null:
                return null;
            case JsonValueKind.True:
            case JsonValueKind.False:
                return element.GetBoolean();
            case JsonValueKind.Number:
                if (element.TryGetInt64(out var longValue))
                {
                    return longValue;
                }
                if (element.TryGetDouble(out var doubleValue) && double.IsFinite(doubleValue))
                {
                    return doubleValue;
                }
                return element.GetRawText();
            case JsonValueKind.String:
                return ClipLegacyString(element.GetString());
            case JsonValueKind.Object:
            {
                var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                var properties = element.EnumerateObject()
                    .OrderBy(property => property.Name, StringComparer.Ordinal)
                    .ToList();
                foreach (var property in properties.Take(MaxObjectFields))
                {
                    result[property.Name] = SanitizeLegacyValue(property.Value, depth + 1, context);
                }

                if (properties.Count > MaxObjectFields)
                {
                    result["__truncated"] = $"field limit {MaxObjectFields} of {properties.Count}";
                }

                return result;
            }
            case JsonValueKind.Array:
            {
                var result = new List<object?>();
                var index = 0;
                foreach (var item in element.EnumerateArray())
                {
                    if (index >= MaxCollectionItems)
                    {
                        result.Add($"+ more items after {MaxCollectionItems}");
                        break;
                    }

                    result.Add(SanitizeLegacyValue(item, depth + 1, context));
                    index++;
                }

                return result;
            }
            default:
                return element.ToString();
        }
    }

    private static object SanitizeObject(object value, int depth, LegacySanitizationContext context)
    {
        var type = value.GetType();
        var properties = type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(property => property.GetIndexParameters().Length == 0)
            .OrderBy(property => property.Name, StringComparer.Ordinal)
            .Take(MaxObjectFields + 1)
            .ToList();

        if (properties.Count == 0)
        {
            return BuildDescriptor("object", SafeObjectDisplay(value), type.FullName);
        }

        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in properties.Take(MaxObjectFields))
        {
            try
            {
                result[property.Name] = SanitizeLegacyValue(property.GetValue(value), depth + 1, context);
            }
            catch (Exception ex)
            {
                result[property.Name] = BuildDescriptor("propertyError", ex.GetBaseException().Message, property.PropertyType.FullName);
            }
        }

        if (properties.Count > MaxObjectFields)
        {
            result["__truncated"] = $"field limit {MaxObjectFields}";
        }

        return result;
    }

    private static bool TrySanitizeLegacyScalar(object value, out object? scalar)
    {
        switch (value)
        {
            case string text:
                scalar = ClipLegacyString(text);
                return true;
            case bool or char or sbyte or byte or short or ushort or int or uint or long or ulong or decimal:
                scalar = value;
                return true;
            case float floatValue:
                scalar = float.IsFinite(floatValue)
                    ? floatValue
                    : BuildDescriptor("nonFiniteNumber", floatValue.ToString("R", CultureInfo.InvariantCulture), value.GetType().FullName);
                return true;
            case double doubleValue:
                scalar = double.IsFinite(doubleValue)
                    ? doubleValue
                    : BuildDescriptor("nonFiniteNumber", doubleValue.ToString("R", CultureInfo.InvariantCulture), value.GetType().FullName);
                return true;
            case Guid guid:
                scalar = guid.ToString("D");
                return true;
            case DateTime dateTime:
                scalar = dateTime.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
                return true;
            case DateTimeOffset dateTimeOffset:
                scalar = dateTimeOffset.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
                return true;
            case TimeSpan timeSpan:
                scalar = timeSpan.ToString("c", CultureInfo.InvariantCulture);
                return true;
        }

        var type = value.GetType();
        if (type.IsEnum)
        {
            scalar = value.ToString();
            return true;
        }

        scalar = null;
        return false;
    }

    private static Dictionary<string, object?> BuildDescriptor(string kind, string? displayValue, string? originalType = null)
    {
        var descriptor = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["kind"] = kind,
            ["displayValue"] = displayValue
        };
        if (!string.IsNullOrWhiteSpace(originalType))
        {
            descriptor["originalType"] = originalType;
        }

        return descriptor;
    }

    private static ResourceDescriptor BuildResourceDescriptor(object value)
    {
        try
        {
            switch (value)
            {
                case ImageWrapper wrapper:
                {
                    var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["isDecoded"] = wrapper.IsDecoded ? "true" : "false",
                        ["refCount"] = wrapper.RefCount.ToString(CultureInfo.InvariantCulture)
                    };
                    if (wrapper.IsDecoded)
                    {
                        metadata["width"] = wrapper.Width.ToString(CultureInfo.InvariantCulture);
                        metadata["height"] = wrapper.Height.ToString(CultureInfo.InvariantCulture);
                        metadata["channels"] = wrapper.Channels.ToString(CultureInfo.InvariantCulture);
                    }

                    return new ResourceDescriptor("image", "ImageWrapper descriptor; content omitted.", metadata, true);
                }
                case Mat mat:
                {
                    var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["empty"] = mat.Empty() ? "true" : "false"
                    };
                    if (!mat.Empty())
                    {
                        metadata["width"] = mat.Width.ToString(CultureInfo.InvariantCulture);
                        metadata["height"] = mat.Height.ToString(CultureInfo.InvariantCulture);
                        metadata["channels"] = mat.Channels().ToString(CultureInfo.InvariantCulture);
                        metadata["type"] = mat.Type().ToString();
                    }

                    return new ResourceDescriptor("matrix", "Mat descriptor; content omitted.", metadata, true);
                }
                case byte[] bytes:
                    return new ResourceDescriptor("binary", "byte[] descriptor; content omitted.", new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["length"] = bytes.Length.ToString(CultureInfo.InvariantCulture)
                    }, true);
                case Stream stream:
                    return new ResourceDescriptor("stream", "Stream descriptor; content omitted.", new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["canRead"] = stream.CanRead ? "true" : "false",
                        ["canSeek"] = stream.CanSeek ? "true" : "false",
                        ["length"] = stream.CanSeek ? SafeStreamLength(stream) : "unknown"
                    }, true);
            }

            var type = value.GetType();
            if (LooksLikeMaskOrImagePayload(type))
            {
                return new ResourceDescriptor("resource", $"{type.Name} descriptor; content omitted.", new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["type"] = type.FullName ?? type.Name
                }, true);
            }
        }
        catch (Exception ex)
        {
            return new ResourceDescriptor("resource", $"Resource descriptor failed: {ex.GetBaseException().Message}", new Dictionary<string, string>(StringComparer.Ordinal), true);
        }

        return new ResourceDescriptor("resource", $"{value.GetType().Name} descriptor; content omitted.", new Dictionary<string, string>(StringComparer.Ordinal), true);
    }

    private static string SafeStreamLength(Stream stream)
    {
        try
        {
            return stream.Length.ToString(CultureInfo.InvariantCulture);
        }
        catch
        {
            return "unknown";
        }
    }

    private static bool IsResourceLike(object value)
    {
        if (value is ImageWrapper or Mat or byte[] or Stream)
        {
            return true;
        }

        return LooksLikeMaskOrImagePayload(value.GetType());
    }

    private static bool LooksLikeMaskOrImagePayload(Type type)
    {
        var name = type.Name;
        var fullName = type.FullName ?? name;
        return name.Contains("Mask", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("ImageWrapper", StringComparison.OrdinalIgnoreCase) ||
               fullName.Contains("OpenCvSharp", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsValueTypeLike(object value)
    {
        var type = value.GetType();
        return type.IsValueType || value is string;
    }

    private static string? ClipString(string? value, ProjectionContext context, string pathHint)
    {
        if (value == null || value.Length <= MaxStringChars)
        {
            return value;
        }

        context.AddDiagnostic("string-limit", $"String length {value.Length} exceeds limit {MaxStringChars}.", pathHint);
        return value[..MaxStringChars] + "...";
    }

    private static string? ClipLegacyString(string? value)
    {
        if (value == null || value.Length <= MaxStringChars)
        {
            return value;
        }

        return value[..MaxStringChars] + "...";
    }

    private static string SafeObjectDisplay(object value)
    {
        try
        {
            return ClipForDisplay(value.ToString() ?? value.GetType().Name);
        }
        catch
        {
            return value.GetType().Name;
        }
    }

    private static string ClipForDisplay(string value) =>
        value.Length <= MaxStringChars ? value : value[..MaxStringChars] + "...";

    private static string AppendObjectKey(string pathHint, string key) =>
        $"{pathHint}[\"{EscapePathKey(key)}\"]";

    private static string AppendArrayIndex(string pathHint, int index) =>
        $"{pathHint}[{index.ToString(CultureInfo.InvariantCulture)}]";

    private static string EscapePathKey(string key) =>
        key.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);

    private sealed class ProjectionContext
    {
        private readonly HashSet<object> _activeReferences = new(ReferenceEqualityComparer.Instance);
        private int _nodeCount;
        private int _estimatedBytes;

        public List<ExecutionObservationDiagnosticV1> Diagnostics { get; } = new();
        public bool Truncated { get; private set; }

        public bool TryReserveNode(string pathHint, object? value)
        {
            _nodeCount++;
            if (_nodeCount <= MaxNodes)
            {
                _estimatedBytes += EstimateNodeBytes(pathHint, value);
                return true;
            }

            Truncated = true;
            AddDiagnostic("node-limit", $"Detail node count exceeded limit {MaxNodes}.", pathHint);
            return false;
        }

        public bool ExceedsBudget(string pathHint, object? value)
        {
            if (_estimatedBytes <= MaxDetailBytes)
            {
                return false;
            }

            Truncated = true;
            AddDiagnostic("byte-budget", $"Estimated detail JSON exceeded budget {MaxDetailBytes} bytes.", pathHint);
            return true;
        }

        public bool EnterReference(object value, string pathHint)
        {
            if (_activeReferences.Add(value))
            {
                return true;
            }

            Truncated = true;
            AddDiagnostic("circular-reference", "Circular reference detected.", pathHint);
            return false;
        }

        public void LeaveReference(object value)
        {
            _activeReferences.Remove(value);
        }

        public void AddDiagnostic(string code, string message, string pathHint)
        {
            Truncated = Truncated || code.Contains("limit", StringComparison.OrdinalIgnoreCase) ||
                        code.Contains("budget", StringComparison.OrdinalIgnoreCase) ||
                        code.Contains("circular", StringComparison.OrdinalIgnoreCase);
            if (Diagnostics.Count >= 256)
            {
                return;
            }

            Diagnostics.Add(new ExecutionObservationDiagnosticV1
            {
                Code = code,
                Message = message,
                PathHint = pathHint
            });
        }

        private static int EstimateNodeBytes(string pathHint, object? value)
        {
            var typeName = value?.GetType().FullName ?? string.Empty;
            var display = value switch
            {
                null => "null",
                string text => text.Length > MaxStringChars ? text[..MaxStringChars] : text,
                _ => value.ToString() ?? string.Empty
            };
            return 160 +
                   Encoding.UTF8.GetByteCount(pathHint) +
                   Encoding.UTF8.GetByteCount(typeName) +
                   Encoding.UTF8.GetByteCount(display);
        }
    }

    private sealed class LegacySanitizationContext
    {
        private readonly HashSet<object> _activeReferences = new(ReferenceEqualityComparer.Instance);
        private int _nodeCount;
        private int _estimatedBytes;

        public bool TryReserve(object? value)
        {
            _nodeCount++;
            _estimatedBytes += EstimateLegacyBytes(value);
            return _nodeCount <= MaxNodes && _estimatedBytes <= MaxDetailBytes;
        }

        public bool EnterReference(object value) => _activeReferences.Add(value);

        public void LeaveReference(object value)
        {
            _activeReferences.Remove(value);
        }

        private static int EstimateLegacyBytes(object? value)
        {
            var typeName = value?.GetType().FullName ?? string.Empty;
            var display = value switch
            {
                null => "null",
                string text => text.Length > MaxStringChars ? text[..MaxStringChars] : text,
                _ => value.ToString() ?? string.Empty
            };
            return 160 + Encoding.UTF8.GetByteCount(typeName) + Encoding.UTF8.GetByteCount(display);
        }
    }

    private sealed record ResourceDescriptor(
        string Kind,
        string DisplayValue,
        Dictionary<string, string> Metadata,
        bool Truncated)
    {
        public Dictionary<string, object?> ToLegacyDictionary()
        {
            var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["kind"] = Kind,
                ["displayValue"] = DisplayValue,
                ["truncated"] = Truncated
            };
            foreach (var pair in Metadata.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                result[pair.Key] = pair.Value;
            }

            return result;
        }
    }
}
