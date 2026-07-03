using System.Collections;
using System.Globalization;
using System.Text.Json;
using ClearVision.Product.Core.ProjectVariables;
using ClearVision.Product.Core.ResultPaths;
using ClearVision.Product.Core.ValueObjects;
using ClearVision.Product.Desktop.PreviewArtifacts;
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
    public const int MaxLegacyOutputBytes = 256 * 1024;

    private const int MaxSummaryItems = 12;
    private const int MaxDiagnosticCount = 64;
    private const int MaxDiagnosticCodeChars = 64;
    private const int MaxDiagnosticMessageChars = 512;
    private const int MaxPathHintChars = 512;
    private const int MaxNameChars = 128;
    private const long MaxSafeIdentityValue = 9_007_199_254_740_991L;

    private static readonly JsonSerializerOptions ObservationBudgetJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static readonly JsonSerializerOptions LegacyBudgetJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static bool IsIdentityValueInSafeRange(long? value) =>
        !value.HasValue || value.Value is >= 0 and <= MaxSafeIdentityValue;

    public static ExecutionObservationEnvelopeV1 CreatePreviewObservation(ExecutionObservationPreviewInput input)
    {
        var context = new ProjectionContext(input.OutputPorts, input.OutputData);
        var rootValue = input.OutputData ?? new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        var detail = ProjectDetailNode(rootValue, PathInfo.Root, null, 0, addressableCandidate: false, context);
        detail = EnforceDetailByteBudget(detail, context);
        var summary = BuildSummary(detail);
        var visualScene = ReconcileVisualSceneWithDetail(CreateVisualScene(input), detail);

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
            Truncated = context.Truncated || detail.Truncated,
            VisualScene = visualScene
        };
    }

    private static ExecutionVisualSceneV1 CreateVisualScene(ExecutionObservationPreviewInput input)
    {
        try
        {
            return ExecutionVisualSceneProjector.Create(new ExecutionVisualSceneInput
            {
                TargetOperator = input.TargetOperator,
                OutputData = input.OutputData,
                OutputPorts = input.OutputPorts,
                FeatureFlags = input.FeatureFlags
            });
        }
        catch (Exception ex)
        {
            return new ExecutionVisualSceneV1
            {
                Diagnostics =
                [
                    new ExecutionVisualSceneDiagnosticV1
                    {
                        Code = "visual-scene-projector-error",
                        Message = $"Scene projection failed: {ClipForDisplay(ex.GetBaseException().Message)}"
                    }
                ]
            };
        }
    }

    private static ExecutionVisualSceneV1 ReconcileVisualSceneWithDetail(
        ExecutionVisualSceneV1 visualScene,
        ExecutionObservationDetailNodeV1 detail)
    {
        var detailLocators = visualScene.Primitives.Count == 0
            ? new Dictionary<DetailLocatorKey, int>()
            : CountDetailLocators(detail);
        var primitives = new List<ExecutionVisualScenePrimitiveV1>(visualScene.Primitives.Count);
        var reconcileDiagnostics = new List<ExecutionVisualSceneDiagnosticV1>();

        foreach (var primitive in visualScene.Primitives)
        {
            if (!primitive.Selectable)
            {
                primitives.Add(primitive);
                continue;
            }

            var key = TryCreateLocatorKey(primitive.OutputPortId, primitive.ResultPathVersion, primitive.ResultPath);
            var matches = 0;
            if (key != null)
            {
                detailLocators.TryGetValue(key.Value, out matches);
            }

            if (matches != 1)
            {
                primitives.Add(CloneScenePrimitive(primitive, selectable: false));
                reconcileDiagnostics.Add(new ExecutionVisualSceneDiagnosticV1
                {
                    Code = matches > 1
                        ? "visual-scene-detail-locator-ambiguous"
                        : "visual-scene-detail-locator-missing",
                    Message = matches > 1
                        ? "Selectable scene primitive locator matched multiple Observation Detail nodes; selection disabled."
                        : "Selectable scene primitive locator did not match exactly one Observation Detail node; selection disabled.",
                    PrimitiveId = primitive.PrimitiveId
                });
                continue;
            }

            primitives.Add(primitive);
        }

        var diagnostics = MergeVisualSceneDiagnostics(
            visualScene.Diagnostics,
            reconcileDiagnostics,
            out var diagnosticsTruncated);

        return new ExecutionVisualSceneV1
        {
            SchemaVersion = visualScene.SchemaVersion,
            CoordinateSpace = visualScene.CoordinateSpace,
            FrameId = visualScene.FrameId,
            FrameKind = visualScene.FrameKind,
            Unit = visualScene.Unit,
            WorldMinX = visualScene.WorldMinX,
            WorldMinY = visualScene.WorldMinY,
            WorldMaxX = visualScene.WorldMaxX,
            WorldMaxY = visualScene.WorldMaxY,
            WorldToSceneScale = visualScene.WorldToSceneScale,
            ImageWidth = visualScene.ImageWidth,
            ImageHeight = visualScene.ImageHeight,
            Primitives = primitives,
            Diagnostics = diagnostics,
            Truncated = visualScene.Truncated || diagnosticsTruncated
        };
    }

    private static List<ExecutionVisualSceneDiagnosticV1> MergeVisualSceneDiagnostics(
        IReadOnlyList<ExecutionVisualSceneDiagnosticV1> originalDiagnostics,
        IReadOnlyList<ExecutionVisualSceneDiagnosticV1> reconcileDiagnostics,
        out bool truncated)
    {
        truncated = false;
        var diagnostics = originalDiagnostics
            .Take(ExecutionVisualSceneProjector.MaxDiagnostics)
            .ToList();

        if (originalDiagnostics.Count > ExecutionVisualSceneProjector.MaxDiagnostics)
        {
            truncated = true;
        }

        if (reconcileDiagnostics.Count == 0)
        {
            return diagnostics;
        }

        var remaining = ExecutionVisualSceneProjector.MaxDiagnostics - diagnostics.Count;
        if (remaining <= 0)
        {
            truncated = true;
            return diagnostics;
        }

        if (reconcileDiagnostics.Count <= remaining)
        {
            diagnostics.AddRange(reconcileDiagnostics);
            return diagnostics;
        }

        var individualCount = Math.Max(0, remaining - 1);
        diagnostics.AddRange(reconcileDiagnostics.Take(individualCount));
        var omittedCount = reconcileDiagnostics.Count - individualCount;
        diagnostics.Add(new ExecutionVisualSceneDiagnosticV1
        {
            Code = "visual-scene-detail-locator-diagnostics-truncated",
            Message = $"{omittedCount.ToString(CultureInfo.InvariantCulture)} visual scene detail locator diagnostics were omitted because the diagnostics budget {ExecutionVisualSceneProjector.MaxDiagnostics.ToString(CultureInfo.InvariantCulture)} was reached."
        });
        truncated = true;
        return diagnostics;
    }

    private static Dictionary<DetailLocatorKey, int> CountDetailLocators(ExecutionObservationDetailNodeV1 detail)
    {
        var counts = new Dictionary<DetailLocatorKey, int>();
        var stack = new Stack<ExecutionObservationDetailNodeV1>();
        stack.Push(detail);
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (current.Locatable &&
                TryCreateLocatorKey(current.OutputPortId, current.ResultPathVersion, current.ResultPath) is { } key)
            {
                counts[key] = counts.TryGetValue(key, out var count) ? count + 1 : 1;
            }

            for (var index = current.Children.Count - 1; index >= 0; index--)
            {
                stack.Push(current.Children[index]);
            }
        }

        return counts;
    }

    private static DetailLocatorKey? TryCreateLocatorKey(Guid? outputPortId, int? resultPathVersion, string? resultPath)
    {
        if (!outputPortId.HasValue ||
            !resultPathVersion.HasValue ||
            string.IsNullOrWhiteSpace(resultPath))
        {
            return null;
        }

        return new DetailLocatorKey(outputPortId.Value, resultPathVersion.Value, resultPath);
    }

    private static ExecutionVisualScenePrimitiveV1 CloneScenePrimitive(
        ExecutionVisualScenePrimitiveV1 primitive,
        bool selectable) =>
        new()
        {
            PrimitiveId = primitive.PrimitiveId,
            Kind = primitive.Kind,
            Layer = primitive.Layer,
            ZOrder = primitive.ZOrder,
            Visible = primitive.Visible,
            Selectable = selectable,
            Label = primitive.Label,
            Geometry = primitive.Geometry,
            Style = primitive.Style,
            OutputPortId = primitive.OutputPortId,
            ResultPathVersion = primitive.ResultPathVersion,
            ResultPath = primitive.ResultPath,
            FrameId = primitive.FrameId,
            Unit = primitive.Unit
        };

    public static Dictionary<string, object> BuildLegacyOutputData(
        IReadOnlyDictionary<string, object> nodeOutput,
        Func<string, bool> shouldSkipKey)
    {
        var context = new LegacySanitizationContext();
        var response = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        var entries = nodeOutput
            .Where(pair => !shouldSkipKey(pair.Key))
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .ToList();

        foreach (var pair in entries.Take(MaxObjectFields))
        {
            AddLegacyResponseValue(response, ClipKey(pair.Key), SanitizeLegacyValue(pair.Value, 0, context));
        }

        if (entries.Count > MaxObjectFields)
        {
            AddLegacyResponseValue(response, "__truncated", $"field limit {MaxObjectFields} of {entries.Count}");
        }

        return EnforceLegacyOutputBudget(response);
    }

    private static ExecutionObservationDetailNodeV1 ProjectDetailNode(
        object? value,
        PathInfo path,
        string? name,
        int depth,
        bool addressableCandidate,
        ProjectionContext context)
    {
        var safeName = ClipName(name, context, path.Value, out var nameAddressable);
        var isAddressable = addressableCandidate && path.Addressable && nameAddressable && path.Value != "$";

        if (!context.TryReserveNode(path.Value))
        {
            return TruncatedNode("node-limit", path, safeName, value, context);
        }

        if (value == null)
        {
            return ScalarNode("null", "null", null, path, safeName, isAddressable, context, null);
        }

        if (TryProjectScalar(value, path, safeName, isAddressable, context, out var scalarNode))
        {
            return scalarNode;
        }

        if (IsResourceLike(value))
        {
            return ResourceNode(value, path, safeName, context);
        }

        if (!IsValueTypeLike(value) && !context.EnterReference(value, path.Value))
        {
            context.AddDiagnostic("circular-reference", "Circular reference omitted.", path.Value);
            return new ExecutionObservationDetailNodeV1
            {
                Kind = "circular",
                DisplayValue = "<circular reference>",
                OriginalType = GetTypeName(value),
                Truncated = true,
                PathHint = path.Value,
                Addressable = false,
                Name = safeName
            };
        }

        try
        {
            if (depth >= MaxDepth)
            {
                return TruncatedNode("depth-limit", path, safeName, value, context);
            }

            return value switch
            {
                JsonElement jsonElement => ProjectJsonElement(jsonElement, path, safeName, depth, isAddressable, context),
                DetectionResult detection => ProjectDetectionResult(detection, path, safeName, depth, context),
                DetectionList detectionList => ProjectDetectionList(detectionList, path, safeName, depth, context),
                IDictionary dictionary => ProjectDictionary(dictionary, path, safeName, depth, context),
                _ when TryProjectKnownFiniteCollection(value, path, safeName, depth, context, out var collectionNode) => collectionNode,
                IEnumerable and not string => UnsupportedEnumerableNode(value, path, safeName, context),
                _ => UnsupportedObjectNode(value, path, safeName, context)
            };
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
        PathInfo path,
        string? name,
        int depth,
        bool addressableCandidate,
        ProjectionContext context)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Null:
                return ScalarNode("null", "null", typeof(JsonElement).FullName, path, name, addressableCandidate, context, element);
            case JsonValueKind.True:
            case JsonValueKind.False:
                return ScalarNode("boolean", element.GetBoolean() ? "true" : "false", typeof(JsonElement).FullName, path, name, addressableCandidate, context, element);
            case JsonValueKind.Number:
                return ScalarNode("number", ClipForDisplay(element.GetRawText()), typeof(JsonElement).FullName, path, name, addressableCandidate, context, element);
            case JsonValueKind.String:
                return ScalarNode("string", ClipString(element.GetString(), context, path.Value), typeof(JsonElement).FullName, path, name, addressableCandidate, context, element);
            case JsonValueKind.Object:
                return ProjectJsonObject(element, path, name, depth, context);
            case JsonValueKind.Array:
                return ProjectJsonArray(element, path, name, depth, context);
            default:
                return ScalarNode("unknown", element.ValueKind.ToString(), typeof(JsonElement).FullName, path, name, false, context, element);
        }
    }

    private static ExecutionObservationDetailNodeV1 ProjectJsonObject(
        JsonElement element,
        PathInfo path,
        string? name,
        int depth,
        ProjectionContext context)
    {
        var properties = element.EnumerateObject()
            .OrderBy(property => property.Name, StringComparer.Ordinal)
            .ToList();
        var children = new List<ExecutionObservationDetailNodeV1>();

        foreach (var property in properties.Take(MaxObjectFields))
        {
            var childPath = AppendObjectKey(path, property.Name, context);
            children.Add(ProjectDetailNode(property.Value, childPath, property.Name, depth + 1, true, context));
        }

        var truncated = properties.Count > MaxObjectFields;
        if (truncated)
        {
            context.AddDiagnostic("field-limit", $"Object field count {properties.Count} exceeds limit {MaxObjectFields}.", path.Value);
        }

        return WithLocatableMetadata(new ExecutionObservationDetailNodeV1
        {
            Kind = "object",
            DisplayValue = $"{Math.Min(properties.Count, MaxObjectFields)}/{properties.Count} fields",
            OriginalType = typeof(JsonElement).FullName,
            Children = children,
            Truncated = truncated,
            PathHint = path.Value,
            Addressable = false,
            Name = name
        }, path, context);
    }

    private static ExecutionObservationDetailNodeV1 ProjectJsonArray(
        JsonElement element,
        PathInfo path,
        string? name,
        int depth,
        ProjectionContext context)
    {
        var total = element.GetArrayLength();
        var children = new List<ExecutionObservationDetailNodeV1>();
        var index = 0;
        foreach (var item in element.EnumerateArray())
        {
            if (index >= MaxCollectionItems)
            {
                break;
            }

            children.Add(ProjectDetailNode(item, AppendArrayIndex(path, index), index.ToString(CultureInfo.InvariantCulture), depth + 1, false, context));
            index++;
        }

        var truncated = total > MaxCollectionItems;
        if (truncated)
        {
            context.AddDiagnostic("collection-limit", $"Collection item count {total} exceeds limit {MaxCollectionItems}.", path.Value);
        }

        return WithLocatableMetadata(new ExecutionObservationDetailNodeV1
        {
            Kind = "array",
            DisplayValue = $"{Math.Min(total, MaxCollectionItems)}/{total} items",
            OriginalType = typeof(JsonElement).FullName,
            Children = children,
            Truncated = truncated,
            PathHint = path.Value,
            Addressable = false,
            Name = name
        }, path, context);
    }

    private static ExecutionObservationDetailNodeV1 ProjectDictionary(
        IDictionary dictionary,
        PathInfo path,
        string? name,
        int depth,
        ProjectionContext context)
    {
        var entries = new List<FieldEntry>();
        foreach (DictionaryEntry entry in dictionary)
        {
            if (!TryFormatDictionaryKey(entry.Key, out var key, out var isStringKey))
            {
                context.AddDiagnostic("dictionary-key-unsupported", "Dictionary entry with unsupported key type was omitted.", path.Value);
                continue;
            }

            entries.Add(new FieldEntry(key, ClipKey(key), path, entry.Value, isStringKey));
        }

        var collidingKeys = entries
            .GroupBy(entry => entry.SortKey, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.Ordinal);
        if (collidingKeys.Count > 0)
        {
            context.AddDiagnostic("dictionary-key-collision", "Dictionary contains display key collisions; affected entries are not addressable.", path.Value);
        }

        entries.Sort((left, right) => string.Compare(left.SortKey, right.SortKey, StringComparison.Ordinal));
        var children = new List<ExecutionObservationDetailNodeV1>();
        foreach (var entry in entries.Take(MaxObjectFields))
        {
            var childPath = entry.CanUseResultPath && !collidingKeys.Contains(entry.SortKey)
                ? AppendObjectKey(entry.ParentPath, entry.SortKey, context)
                : AppendDisplayObjectKey(entry.ParentPath, entry.SortKey);
            children.Add(ProjectDetailNode(entry.Value, childPath, entry.Name, depth + 1, true, context));
        }

        var truncated = entries.Count > MaxObjectFields;
        if (truncated)
        {
            context.AddDiagnostic("field-limit", $"Dictionary field count {entries.Count} exceeds limit {MaxObjectFields}.", path.Value);
        }

        return WithLocatableMetadata(new ExecutionObservationDetailNodeV1
        {
            Kind = "dictionary",
            DisplayValue = $"{Math.Min(entries.Count, MaxObjectFields)}/{entries.Count} fields",
            OriginalType = GetTypeName(dictionary),
            Children = children,
            Truncated = truncated,
            PathHint = path.Value,
            Addressable = false,
            Name = name
        }, path, context);
    }

    private static bool TryProjectKnownFiniteCollection(
        object value,
        PathInfo path,
        string? name,
        int depth,
        ProjectionContext context,
        out ExecutionObservationDetailNodeV1 node)
    {
        if (TryProjectArray(value, path, name, depth, context, out node))
        {
            return true;
        }

        if (TryProjectList(value, path, name, depth, context, out node))
        {
            return true;
        }

        node = new ExecutionObservationDetailNodeV1();
        return false;
    }

    private static bool TryProjectArray(
        object value,
        PathInfo path,
        string? name,
        int depth,
        ProjectionContext context,
        out ExecutionObservationDetailNodeV1 node)
    {
        if (value is not Array array || array.Rank != 1)
        {
            node = new ExecutionObservationDetailNodeV1();
            return false;
        }

        node = ProjectIndexedCollection(
            array.Length,
            index => array.GetValue(index),
            GetTypeName(value),
            path,
            name,
            depth,
            context);
        return true;
    }

    private static bool TryProjectList(
        object value,
        PathInfo path,
        string? name,
        int depth,
        ProjectionContext context,
        out ExecutionObservationDetailNodeV1 node)
    {
        if (!IsKnownGenericList(value.GetType()) || value is not IList list)
        {
            node = new ExecutionObservationDetailNodeV1();
            return false;
        }

        node = ProjectIndexedCollection(
            list.Count,
            index => list[index],
            GetTypeName(value),
            path,
            name,
            depth,
            context);
        return true;
    }

    private static ExecutionObservationDetailNodeV1 ProjectIndexedCollection(
        int total,
        Func<int, object?> readItem,
        string? originalType,
        PathInfo path,
        string? name,
        int depth,
        ProjectionContext context)
    {
        var children = new List<ExecutionObservationDetailNodeV1>();
        for (var index = 0; index < Math.Min(total, MaxCollectionItems); index++)
        {
            children.Add(ProjectDetailNode(readItem(index), AppendArrayIndex(path, index), index.ToString(CultureInfo.InvariantCulture), depth + 1, false, context));
        }

        var truncated = total > MaxCollectionItems;
        if (truncated)
        {
            context.AddDiagnostic("collection-limit", $"Collection item count {total} exceeds limit {MaxCollectionItems}.", path.Value);
        }

        return WithLocatableMetadata(new ExecutionObservationDetailNodeV1
        {
            Kind = "array",
            DisplayValue = $"{Math.Min(total, MaxCollectionItems)}/{total} items",
            OriginalType = originalType,
            Children = children,
            Truncated = truncated,
            PathHint = path.Value,
            Addressable = false,
            Name = name
        }, path, context);
    }

    private static ExecutionObservationDetailNodeV1 ProjectDetectionList(
        DetectionList detectionList,
        PathInfo path,
        string? name,
        int depth,
        ProjectionContext context)
    {
        var detections = detectionList.Detections ?? new List<DetectionResult>();
        var children = new List<ExecutionObservationDetailNodeV1>
        {
            ProjectDetailNode(detections.Count, AppendObjectKey(path, "Count", context), "Count", depth + 1, true, context)
        };
        var detectionPath = AppendObjectKey(path, "Detections", context);
        children.Add(ProjectIndexedCollection(
            detections.Count,
            index => detections[index],
            typeof(List<DetectionResult>).FullName,
            detectionPath,
            "Detections",
            depth + 1,
            context));

        var truncated = detections.Count > MaxCollectionItems;
        return WithLocatableMetadata(new ExecutionObservationDetailNodeV1
        {
            Kind = "detectionList",
            DisplayValue = $"{Math.Min(detections.Count, MaxCollectionItems)}/{detections.Count} detections",
            OriginalType = typeof(DetectionList).FullName,
            Children = children,
            Truncated = truncated,
            PathHint = path.Value,
            Addressable = false,
            Name = name
        }, path, context);
    }

    private static ExecutionObservationDetailNodeV1 ProjectDetectionResult(
        DetectionResult detection,
        PathInfo path,
        string? name,
        int depth,
        ProjectionContext context)
    {
        var fields = new (string Name, object? Value)[]
        {
            ("Label", detection.Label),
            ("Confidence", detection.Confidence),
            ("X", detection.X),
            ("Y", detection.Y),
            ("Width", detection.Width),
            ("Height", detection.Height),
            ("Area", detection.Area)
        };

        var children = fields
            .Select(field => ProjectDetailNode(field.Value, AppendObjectKey(path, field.Name, context), field.Name, depth + 1, true, context))
            .ToList();

        return WithLocatableMetadata(new ExecutionObservationDetailNodeV1
        {
            Kind = "detection",
            DisplayValue = $"Detection {ClipForDisplay(detection.Label)}",
            OriginalType = typeof(DetectionResult).FullName,
            Children = children,
            PathHint = path.Value,
            Addressable = false,
            Name = name
        }, path, context);
    }

    private static ExecutionObservationDetailNodeV1 UnsupportedEnumerableNode(
        object value,
        PathInfo path,
        string? name,
        ProjectionContext context)
    {
        context.AddDiagnostic("unsupported-enumerable", "Unknown enumerable was not enumerated.", path.Value);
        return new ExecutionObservationDetailNodeV1
        {
            Kind = "unsupportedEnumerable",
            DisplayValue = "Unknown enumerable; content omitted.",
            OriginalType = GetTypeName(value),
            Truncated = true,
            PathHint = path.Value,
            Addressable = false,
            Name = name
        };
    }

    private static ExecutionObservationDetailNodeV1 UnsupportedObjectNode(
        object value,
        PathInfo path,
        string? name,
        ProjectionContext context)
    {
        return WithLocatableMetadata(new ExecutionObservationDetailNodeV1
        {
            Kind = "objectDescriptor",
            DisplayValue = "Unsupported object; content omitted.",
            OriginalType = GetTypeName(value),
            Truncated = true,
            PathHint = path.Value,
            Addressable = false,
            Name = name
        }, path, context);
    }

    private static bool TryProjectScalar(
        object value,
        PathInfo path,
        string? name,
        bool addressable,
        ProjectionContext context,
        out ExecutionObservationDetailNodeV1 node)
    {
        switch (value)
        {
            case string text:
                node = ScalarNode("string", ClipString(text, context, path.Value), GetTypeName(value), path, name, addressable, context, value);
                return true;
            case bool boolean:
                node = ScalarNode("boolean", boolean ? "true" : "false", GetTypeName(value), path, name, addressable, context, value);
                return true;
            case char character:
                node = ScalarNode("string", character.ToString(), GetTypeName(value), path, name, addressable, context, value);
                return true;
            case Guid guid:
                node = ScalarNode("guid", guid.ToString("D"), GetTypeName(value), path, name, addressable, context, value);
                return true;
            case DateTime dateTime:
                node = ScalarNode("dateTime", dateTime.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture), GetTypeName(value), path, name, addressable, context, value);
                return true;
            case DateTimeOffset dateTimeOffset:
                node = ScalarNode("dateTime", dateTimeOffset.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture), GetTypeName(value), path, name, addressable, context, value);
                return true;
            case TimeSpan timeSpan:
                node = ScalarNode("duration", timeSpan.ToString("c", CultureInfo.InvariantCulture), GetTypeName(value), path, name, addressable, context, value);
                return true;
            case float floatValue when !float.IsFinite(floatValue):
                context.AddDiagnostic("non-finite-number", $"Non-finite float '{floatValue.ToString("R", CultureInfo.InvariantCulture)}' converted to display text.", path.Value);
                node = ScalarNode("nonFiniteNumber", floatValue.ToString("R", CultureInfo.InvariantCulture), GetTypeName(value), path, name, false, context, value);
                return true;
            case double doubleValue when !double.IsFinite(doubleValue):
                context.AddDiagnostic("non-finite-number", $"Non-finite double '{doubleValue.ToString("R", CultureInfo.InvariantCulture)}' converted to display text.", path.Value);
                node = ScalarNode("nonFiniteNumber", doubleValue.ToString("R", CultureInfo.InvariantCulture), GetTypeName(value), path, name, false, context, value);
                return true;
            case sbyte sbyteValue:
                node = NumberNode(sbyteValue, value, path, name, addressable, context);
                return true;
            case byte byteValue:
                node = NumberNode(byteValue, value, path, name, addressable, context);
                return true;
            case short shortValue:
                node = NumberNode(shortValue, value, path, name, addressable, context);
                return true;
            case ushort ushortValue:
                node = NumberNode(ushortValue, value, path, name, addressable, context);
                return true;
            case int intValue:
                node = NumberNode(intValue, value, path, name, addressable, context);
                return true;
            case uint uintValue:
                node = NumberNode(uintValue, value, path, name, addressable, context);
                return true;
            case long longValue:
                node = NumberNode(longValue, value, path, name, addressable, context);
                return true;
            case ulong ulongValue:
                node = NumberNode(ulongValue, value, path, name, addressable, context);
                return true;
            case float floatValue:
                node = ScalarNode("number", floatValue.ToString("R", CultureInfo.InvariantCulture), GetTypeName(value), path, name, addressable, context, value);
                return true;
            case double doubleValue:
                node = ScalarNode("number", doubleValue.ToString("R", CultureInfo.InvariantCulture), GetTypeName(value), path, name, addressable, context, value);
                return true;
            case decimal decimalValue:
                node = ScalarNode("number", decimalValue.ToString(CultureInfo.InvariantCulture), GetTypeName(value), path, name, addressable, context, value);
                return true;
        }

        var type = value.GetType();
        if (type.IsEnum)
        {
            node = ScalarNode("enum", value.ToString() ?? string.Empty, GetTypeName(value), path, name, addressable, context, value);
            return true;
        }

        node = new ExecutionObservationDetailNodeV1();
        return false;
    }

    private static ExecutionObservationDetailNodeV1 NumberNode<T>(
        T number,
        object original,
        PathInfo path,
        string? name,
        bool addressable,
        ProjectionContext context)
        where T : IFormattable =>
        ScalarNode("number", number.ToString(null, CultureInfo.InvariantCulture), GetTypeName(original), path, name, addressable, context, original);

    private static ExecutionObservationDetailNodeV1 ScalarNode(
        string kind,
        string? displayValue,
        string? originalType,
        PathInfo path,
        string? name,
        bool addressable,
        ProjectionContext context,
        object? scalarValue)
    {
        var locator = ValidateLocatableResultPath(path.ResultPathBinding, scalarValue, path.Value, context, compareScalarValue: true);
        var effectiveAddressable = addressable &&
            path.AllowsBinding &&
            path.Value != "$" &&
            path.ResultPathBinding != null &&
            !path.ResultPathBinding.ContainsIndex;
        var binding = effectiveAddressable
            ? ValidateBindableResultPath(path.ResultPathBinding!, scalarValue, path.Value, context)
            : null;
        effectiveAddressable = binding != null;
        var canonicalPath = binding?.ToCanonicalPath() ?? locator?.ResultPath;
        var bindableVariableTypes = binding == null
            ? null
            : GetBindableVariableTypes(scalarValue);
        return new ExecutionObservationDetailNodeV1
        {
            Kind = kind,
            DisplayValue = displayValue,
            OriginalType = originalType,
            PathHint = path.Value,
            Addressable = effectiveAddressable,
            Locatable = locator != null || binding != null,
            Name = name,
            OutputPortId = binding?.OutputPortId ?? locator?.OutputPortId,
            OutputPortName = binding?.OutputPortName ?? locator?.OutputPortName,
            ResultPathVersion = binding != null || locator != null ? ResultPathV1.Version : null,
            ResultPath = canonicalPath,
            BindableVariableTypes = bindableVariableTypes == null || bindableVariableTypes.Count == 0 ? null : bindableVariableTypes
        };
    }

    private static ExecutionObservationDetailNodeV1 WithLocatableMetadata(
        ExecutionObservationDetailNodeV1 node,
        PathInfo path,
        ProjectionContext context)
    {
        var locator = ValidateLocatableResultPath(path.ResultPathBinding, null, path.Value, context, compareScalarValue: false);
        if (locator == null)
        {
            return node;
        }

        node.Locatable = true;
        node.OutputPortId = locator.OutputPortId;
        node.OutputPortName = locator.OutputPortName;
        node.ResultPathVersion = ResultPathV1.Version;
        node.ResultPath = locator.ResultPath;
        return node;
    }

    private static LocatableMetadata? ValidateLocatableResultPath(
        ResultPathBindingInfo? binding,
        object? projectedValue,
        string pathHint,
        ProjectionContext context,
        bool compareScalarValue)
    {
        if (binding == null)
        {
            return null;
        }

        var canonicalPath = binding.ToCanonicalPath();
        var resolved = ResultPathResolver.Resolve(
            ResultPathV1.Version,
            canonicalPath,
            binding.OutputPortRoot,
            new ResultPathResolverOptions
            {
                AllowIndexSegments = true,
                RequireTerminalScalar = false
            });
        if (resolved.Succeeded &&
            (!compareScalarValue || AreEquivalentScalarValues(projectedValue, resolved.Value)))
        {
            return new LocatableMetadata(binding.OutputPortId, binding.OutputPortName, canonicalPath);
        }

        var message = resolved.Succeeded
            ? "Canonical locator resolved to a different value; metadata omitted."
            : $"Canonical locator is not resolvable by the read-only locator resolver: {resolved.Diagnostic!.Code}.";
        context.AddDiagnostic("resultpath-locator-unresolvable", message, pathHint);
        return null;
    }

    private static List<string> GetBindableVariableTypes(object? scalarValue)
    {
        var result = new List<string>();
        foreach (var valueType in Enum.GetValues<ProjectGlobalVariableValueType>())
        {
            if (ProjectVariableValueTransform.TryConvertToVariableValue(
                    scalarValue,
                    valueType,
                    ProjectVariableConversionMode.Exact,
                    expression: null,
                    variables: new Dictionary<string, object?>(),
                    out _,
                    out _))
            {
                result.Add(valueType.ToString());
            }
        }

        return result;
    }

    private static ResultPathBindingInfo? ValidateBindableResultPath(
        ResultPathBindingInfo binding,
        object? scalarValue,
        string pathHint,
        ProjectionContext context)
    {
        var canonicalPath = binding.ToCanonicalPath();
        var resolved = ResultPathResolver.Resolve(ResultPathV1.Version, canonicalPath, binding.OutputPortRoot);
        if (resolved.Succeeded && AreEquivalentScalarValues(scalarValue, resolved.Value))
        {
            return binding;
        }

        var message = resolved.Succeeded
            ? "Canonical ResultPath resolved to a different scalar; metadata omitted."
            : $"Canonical ResultPath is not resolvable by the production resolver: {resolved.Diagnostic!.Code}.";
        context.AddDiagnostic("resultpath-unresolvable", message, pathHint);
        return null;
    }

    private static bool AreEquivalentScalarValues(object? projectedValue, object? resolvedValue)
    {
        if (projectedValue is JsonElement projectedElement)
        {
            return resolvedValue is JsonElement resolvedElement &&
                   projectedElement.ValueKind == resolvedElement.ValueKind &&
                   string.Equals(projectedElement.GetRawText(), resolvedElement.GetRawText(), StringComparison.Ordinal);
        }

        if (resolvedValue is JsonElement)
        {
            return false;
        }

        return Equals(projectedValue, resolvedValue);
    }

    private static ExecutionObservationDetailNodeV1 ResourceNode(
        object value,
        PathInfo path,
        string? name,
        ProjectionContext context)
    {
        var descriptor = BuildResourceDescriptor(value);
        if (descriptor.Truncated)
        {
            context.AddDiagnostic("resource-descriptor", descriptor.DisplayValue, path.Value);
        }

        return new ExecutionObservationDetailNodeV1
        {
            Kind = descriptor.Kind,
            DisplayValue = descriptor.DisplayValue,
            OriginalType = GetTypeName(value),
            Children = descriptor.Metadata
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => ScalarNode("string", pair.Value, typeof(string).FullName, AppendObjectKey(path.WithoutResultPathBinding(), pair.Key, context), pair.Key, true, context, pair.Value))
                .ToList(),
            Truncated = true,
            PathHint = path.Value,
            Addressable = false,
            Name = name,
            Artifact = descriptor.Artifact
        };
    }

    private static ExecutionObservationDetailNodeV1 TruncatedNode(
        string code,
        PathInfo path,
        string? name,
        object? value,
        ProjectionContext context)
    {
        context.AddDiagnostic(code, $"Observation detail omitted because {code} was reached.", path.Value);
        return new ExecutionObservationDetailNodeV1
        {
            Kind = "truncated",
            DisplayValue = "<truncated>",
            OriginalType = GetTypeName(value),
            Truncated = true,
            PathHint = path.Value,
            Addressable = false,
            Name = name
        };
    }

    private static ExecutionObservationDetailNodeV1 EnforceDetailByteBudget(
        ExecutionObservationDetailNodeV1 detail,
        ProjectionContext context)
    {
        if (GetDetailByteCount(detail) <= MaxDetailBytes)
        {
            return detail;
        }

        context.AddDiagnostic("byte-budget", $"Serialized observation detail exceeded hard limit {MaxDetailBytes} bytes.", "$");
        context.MarkTruncated();

        while (GetDetailByteCount(detail) > MaxDetailBytes)
        {
            if (!TryPruneLastNode(detail))
            {
                detail = new ExecutionObservationDetailNodeV1
                {
                    Kind = "truncated",
                    DisplayValue = "<detail omitted by byte budget>",
                    Truncated = true,
                    PathHint = "$",
                    Addressable = false
                };
                break;
            }
        }

        return detail;
    }

    private static int GetDetailByteCount(ExecutionObservationDetailNodeV1 detail) =>
        JsonSerializer.SerializeToUtf8Bytes(detail, ObservationBudgetJsonOptions).Length;

    private static bool TryPruneLastNode(ExecutionObservationDetailNodeV1 node)
    {
        if (node.Children.Count == 0)
        {
            return false;
        }

        var index = node.Children.Count - 1;
        var child = node.Children[index];
        if (child.Children.Count > 0 && TryPruneLastNode(child))
        {
            node.Truncated = true;
            return true;
        }

        node.Children.RemoveAt(index);
        node.Truncated = true;
        return true;
    }

    private static List<ExecutionObservationSummaryItemV1> BuildSummary(ExecutionObservationDetailNodeV1 detail)
    {
        var result = new List<ExecutionObservationSummaryItemV1>();
        var stack = new Stack<ExecutionObservationDetailNodeV1>();
        for (var index = detail.Children.Count - 1; index >= 0; index--)
        {
            stack.Push(detail.Children[index]);
        }

        while (stack.Count > 0 && result.Count < MaxSummaryItems)
        {
            var current = stack.Pop();
            if (!string.IsNullOrWhiteSpace(current.Name) &&
                !string.IsNullOrWhiteSpace(current.DisplayValue) &&
                current.Children.Count == 0)
            {
                result.Add(new ExecutionObservationSummaryItemV1
                {
                    Key = current.Name!,
                    DisplayValue = current.DisplayValue!,
                    OriginalType = current.OriginalType,
                    PathHint = current.PathHint,
                    Addressable = current.Addressable,
                    Locatable = current.Locatable,
                    OutputPortId = current.OutputPortId,
                    OutputPortName = current.OutputPortName,
                    ResultPathVersion = current.ResultPathVersion,
                    ResultPath = current.ResultPath,
                    BindableVariableTypes = current.BindableVariableTypes
                });
            }

            for (var index = current.Children.Count - 1; index >= 0; index--)
            {
                stack.Push(current.Children[index]);
            }
        }

        return result;
    }

    private static object? SanitizeLegacyValue(object? value, int depth, LegacySanitizationContext context)
    {
        if (!context.TryReserve())
        {
            context.MarkTruncated();
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
            context.MarkTruncated();
            return BuildDescriptor("truncated", "depth limit reached", GetTypeName(value));
        }

        if (!IsValueTypeLike(value) && !context.EnterReference(value))
        {
            context.MarkTruncated();
            return BuildDescriptor("circular", "circular reference omitted", GetTypeName(value));
        }

        try
        {
            return value switch
            {
                JsonElement element => SanitizeJsonElement(element, depth, context),
                DetectionResult detection => SanitizeDetectionResult(detection, context),
                DetectionList detectionList => SanitizeDetectionList(detectionList, depth, context),
                IDictionary dictionary => SanitizeLegacyDictionary(dictionary, depth, context),
                _ when TrySanitizeKnownFiniteCollection(value, depth, context, out var collection) => collection,
                IEnumerable and not string => BuildDescriptor("unsupportedEnumerable", "unknown enumerable omitted", GetTypeName(value)),
                _ => BuildDescriptor("object", "unsupported object omitted", GetTypeName(value))
            };
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

                return ClipLegacyString(element.GetRawText());
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
                    result[ClipKey(property.Name)] = SanitizeLegacyValue(property.Value, depth + 1, context);
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
                var count = element.GetArrayLength();
                var index = 0;
                foreach (var item in element.EnumerateArray())
                {
                    if (index >= MaxCollectionItems)
                    {
                        break;
                    }

                    result.Add(SanitizeLegacyValue(item, depth + 1, context));
                    index++;
                }

                if (count > MaxCollectionItems)
                {
                    result.Add($"+ more items after {MaxCollectionItems}");
                }

                return result;
            }
            default:
                return BuildDescriptor("json", element.ValueKind.ToString(), typeof(JsonElement).FullName);
        }
    }

    private static object SanitizeLegacyDictionary(
        IDictionary dictionary,
        int depth,
        LegacySanitizationContext context)
    {
        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        var entries = new List<(string Key, object? Value)>();
        foreach (DictionaryEntry entry in dictionary)
        {
            if (TryFormatDictionaryKey(entry.Key, out var key, out _))
            {
                entries.Add((key, entry.Value));
            }
        }

        entries.Sort((left, right) => string.Compare(left.Key, right.Key, StringComparison.Ordinal));
        foreach (var entry in entries.Take(MaxObjectFields))
        {
            AddLegacyValue(result, ClipKey(entry.Key), SanitizeLegacyValue(entry.Value, depth + 1, context));
        }

        if (entries.Count > MaxObjectFields)
        {
            result["__truncated"] = $"field limit {MaxObjectFields} of {entries.Count}";
        }

        return result;
    }

    private static bool TrySanitizeKnownFiniteCollection(
        object value,
        int depth,
        LegacySanitizationContext context,
        out object collection)
    {
        if (value is Array array && array.Rank == 1)
        {
            collection = SanitizeIndexedCollection(array.Length, index => array.GetValue(index), depth, context);
            return true;
        }

        if (IsKnownGenericList(value.GetType()) && value is IList list)
        {
            collection = SanitizeIndexedCollection(list.Count, index => list[index], depth, context);
            return true;
        }

        collection = new List<object?>();
        return false;
    }

    private static List<object?> SanitizeIndexedCollection(
        int total,
        Func<int, object?> readItem,
        int depth,
        LegacySanitizationContext context)
    {
        var result = new List<object?>();
        for (var index = 0; index < Math.Min(total, MaxCollectionItems); index++)
        {
            result.Add(SanitizeLegacyValue(readItem(index), depth + 1, context));
        }

        if (total > MaxCollectionItems)
        {
            result.Add($"+ more items after {MaxCollectionItems}");
        }

        return result;
    }

    private static Dictionary<string, object?> SanitizeDetectionList(
        DetectionList detectionList,
        int depth,
        LegacySanitizationContext context)
    {
        var detections = detectionList.Detections ?? new List<DetectionResult>();
        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["Count"] = detections.Count,
            ["Detections"] = SanitizeIndexedCollection(detections.Count, index => detections[index], depth + 1, context)
        };

        return result;
    }

    private static Dictionary<string, object?> SanitizeDetectionResult(
        DetectionResult detection,
        LegacySanitizationContext context) =>
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["Label"] = ClipLegacyString(detection.Label),
            ["Confidence"] = SanitizeFiniteNumber(detection.Confidence, context),
            ["X"] = SanitizeFiniteNumber(detection.X, context),
            ["Y"] = SanitizeFiniteNumber(detection.Y, context),
            ["Width"] = SanitizeFiniteNumber(detection.Width, context),
            ["Height"] = SanitizeFiniteNumber(detection.Height, context),
            ["Area"] = SanitizeFiniteNumber(detection.Area, context)
        };

    private static object SanitizeFiniteNumber(float value, LegacySanitizationContext context)
    {
        if (float.IsFinite(value))
        {
            return value;
        }

        context.MarkTruncated();
        return BuildDescriptor("nonFiniteNumber", value.ToString("R", CultureInfo.InvariantCulture), typeof(float).FullName);
    }

    private static bool TrySanitizeLegacyScalar(object value, out object? scalar)
    {
        switch (value)
        {
            case string text:
                scalar = ClipLegacyString(text);
                return true;
            case bool boolean:
                scalar = boolean;
                return true;
            case char character:
                scalar = character.ToString();
                return true;
            case sbyte or byte or short or ushort or int or uint or long or ulong or decimal:
                scalar = value;
                return true;
            case float floatValue:
                scalar = float.IsFinite(floatValue)
                    ? floatValue
                    : BuildDescriptor("nonFiniteNumber", floatValue.ToString("R", CultureInfo.InvariantCulture), GetTypeName(value));
                return true;
            case double doubleValue:
                scalar = double.IsFinite(doubleValue)
                    ? doubleValue
                    : BuildDescriptor("nonFiniteNumber", doubleValue.ToString("R", CultureInfo.InvariantCulture), GetTypeName(value));
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
            ["displayValue"] = ClipForDisplay(displayValue ?? string.Empty)
        };
        if (!string.IsNullOrWhiteSpace(originalType))
        {
            descriptor["originalType"] = ClipForDisplay(originalType);
        }

        return descriptor;
    }

    private static Dictionary<string, object> EnforceLegacyOutputBudget(Dictionary<string, object> response)
    {
        if (GetLegacyByteCount(response) <= MaxLegacyOutputBytes)
        {
            return response;
        }

        response["__truncated"] = $"outputData byte limit {MaxLegacyOutputBytes}";
        var removableKeys = response.Keys
            .Where(key => !string.Equals(key, "__truncated", StringComparison.OrdinalIgnoreCase))
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToList();

        while (GetLegacyByteCount(response) > MaxLegacyOutputBytes && removableKeys.Count > 0)
        {
            var key = removableKeys[^1];
            removableKeys.RemoveAt(removableKeys.Count - 1);
            response.Remove(key);
        }

        if (GetLegacyByteCount(response) <= MaxLegacyOutputBytes)
        {
            return response;
        }

        return new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
        {
            ["__truncated"] = $"outputData byte limit {MaxLegacyOutputBytes}"
        };
    }

    private static int GetLegacyByteCount(Dictionary<string, object> response) =>
        JsonSerializer.SerializeToUtf8Bytes(response, LegacyBudgetJsonOptions).Length;

    private static ResourceDescriptor BuildResourceDescriptor(object value)
    {
        try
        {
            switch (value)
            {
                case PreviewArtifactValue artifactValue:
                    return new ResourceDescriptor(
                        artifactValue.Kind,
                        artifactValue.DisplayValue,
                        artifactValue.Metadata,
                        artifactValue.Truncated,
                        artifactValue.Artifact);
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

                    return new ResourceDescriptor("image", "ImageWrapper descriptor; content omitted.", metadata, true, null);
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

                    return new ResourceDescriptor("matrix", "Mat descriptor; content omitted.", metadata, true, null);
                }
                case byte[] bytes:
                    return new ResourceDescriptor("binary", "byte[] descriptor; content omitted.", new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["length"] = bytes.Length.ToString(CultureInfo.InvariantCulture)
                    }, true, null);
                case Stream stream:
                    return new ResourceDescriptor("stream", "Stream descriptor; content omitted.", new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["canRead"] = stream.CanRead ? "true" : "false",
                        ["canSeek"] = stream.CanSeek ? "true" : "false",
                        ["length"] = stream.CanSeek ? SafeStreamLength(stream) : "unknown"
                    }, true, null);
            }

            var type = value.GetType();
            if (LooksLikeMaskOrImagePayload(type))
            {
                return new ResourceDescriptor("resource", $"{ClipForDisplay(type.Name)} descriptor; content omitted.", new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["type"] = ClipForDisplay(type.FullName ?? type.Name)
                }, true, null);
            }
        }
        catch (Exception ex)
        {
            return new ResourceDescriptor("resource", $"Resource descriptor failed: {ClipForDisplay(ex.GetBaseException().Message)}", new Dictionary<string, string>(StringComparer.Ordinal), true, null);
        }

        return new ResourceDescriptor("resource", "Resource descriptor; content omitted.", new Dictionary<string, string>(StringComparer.Ordinal), true, null);
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
        if (value is PreviewArtifactValue or ImageWrapper or Mat or byte[] or Stream)
        {
            return true;
        }

        return LooksLikeMaskOrImagePayload(value.GetType());
    }

    private static bool LooksLikeMaskOrImagePayload(Type type)
    {
        var name = type.Name;
        return name.Contains("Mask", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("ImageWrapper", StringComparison.OrdinalIgnoreCase) ||
               name.EndsWith("Mat", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsKnownGenericList(Type type) =>
        type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>);

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

    private static string ClipForDisplay(string value) =>
        value.Length <= MaxStringChars ? value : value[..MaxStringChars] + "...";

    private static string ClipKey(string key) =>
        key.Length <= MaxNameChars ? key : key[..MaxNameChars] + "...";

    private static string? ClipName(string? name, ProjectionContext context, string pathHint, out bool addressable)
    {
        addressable = true;
        if (string.IsNullOrEmpty(name) || name.Length <= MaxNameChars)
        {
            return name;
        }

        addressable = false;
        context.AddDiagnostic("path-limit", $"Name length {name.Length} exceeds limit {MaxNameChars}.", pathHint);
        return name[..MaxNameChars] + "...";
    }

    private static PathInfo AppendObjectKey(PathInfo path, string key, ProjectionContext context)
    {
        var candidate = $"{path.Value}{ResultPathFormatter.FormatObjectKeySegment(key)}";
        var withinPathLimits = key.Length <= MaxNameChars && candidate.Length <= MaxPathHintChars;
        var addressable = path.Addressable && withinPathLimits;
        var allowsBinding = path.AllowsBinding && withinPathLimits;
        ResultPathBindingInfo? binding = null;
        if (addressable)
        {
            if (path.Value == "$" && path.ResultPathBinding == null && context.HasOutputPorts)
            {
                binding = context.CreateOutputPortBinding(key, path.Value);
                addressable = binding != null;
                allowsBinding = allowsBinding && binding != null;
            }
            else
            {
                binding = path.ResultPathBinding?.AppendKey(key);
                allowsBinding = allowsBinding && binding != null;
            }
        }

        if (path.Addressable && !withinPathLimits)
        {
            context.AddDiagnostic("path-limit", "Object key or path was clipped; node is not addressable.", path.Value);
        }

        return new PathInfo(
            candidate.Length <= MaxPathHintChars ? candidate : candidate[..MaxPathHintChars] + "...",
            addressable,
            allowsBinding,
            binding);
    }

    private static PathInfo AppendArrayIndex(PathInfo path, int index)
    {
        var candidate = $"{path.Value}[{index.ToString(CultureInfo.InvariantCulture)}]";
        var withinPathLimits = candidate.Length <= MaxPathHintChars;
        var binding = withinPathLimits
            ? path.ResultPathBinding?.AppendIndex(index)
            : null;
        return new PathInfo(
            candidate.Length <= MaxPathHintChars ? candidate : candidate[..MaxPathHintChars] + "...",
            path.Addressable && withinPathLimits && binding != null,
            false,
            binding);
    }

    private static PathInfo AppendDisplayObjectKey(PathInfo path, string key)
    {
        var candidate = $"{path.Value}{ResultPathFormatter.FormatObjectKeySegment(key)}";
        return new PathInfo(
            candidate.Length <= MaxPathHintChars ? candidate : candidate[..MaxPathHintChars] + "...",
            false,
            false,
            null);
    }

    private static bool TryFormatDictionaryKey(object? key, out string formatted, out bool isStringKey)
    {
        isStringKey = false;
        switch (key)
        {
            case string text:
                formatted = text;
                isStringKey = true;
                return !string.IsNullOrWhiteSpace(formatted);
            case char character:
                formatted = character.ToString();
                return true;
            case Guid guid:
                formatted = guid.ToString("D");
                return true;
            case bool boolean:
                formatted = boolean ? "true" : "false";
                return true;
            case sbyte sbyteValue:
                formatted = sbyteValue.ToString(CultureInfo.InvariantCulture);
                return true;
            case byte byteValue:
                formatted = byteValue.ToString(CultureInfo.InvariantCulture);
                return true;
            case short shortValue:
                formatted = shortValue.ToString(CultureInfo.InvariantCulture);
                return true;
            case ushort ushortValue:
                formatted = ushortValue.ToString(CultureInfo.InvariantCulture);
                return true;
            case int intValue:
                formatted = intValue.ToString(CultureInfo.InvariantCulture);
                return true;
            case uint uintValue:
                formatted = uintValue.ToString(CultureInfo.InvariantCulture);
                return true;
            case long longValue:
                formatted = longValue.ToString(CultureInfo.InvariantCulture);
                return true;
            case ulong ulongValue:
                formatted = ulongValue.ToString(CultureInfo.InvariantCulture);
                return true;
            case Enum enumValue:
                formatted = enumValue.ToString();
                return true;
            default:
                formatted = string.Empty;
                return false;
        }
    }

    private static void AddLegacyValue(IDictionary<string, object?> dictionary, string key, object? value)
    {
        var candidate = key;
        var suffix = 1;
        while (dictionary.ContainsKey(candidate))
        {
            candidate = $"{key}_{suffix.ToString(CultureInfo.InvariantCulture)}";
            suffix++;
        }

        dictionary[candidate] = value;
    }

    private static void AddLegacyResponseValue(IDictionary<string, object> dictionary, string key, object? value)
    {
        var candidate = key;
        var suffix = 1;
        while (dictionary.ContainsKey(candidate))
        {
            candidate = $"{key}_{suffix.ToString(CultureInfo.InvariantCulture)}";
            suffix++;
        }

        dictionary[candidate] = value!;
    }

    private static string? GetTypeName(object? value) =>
        value?.GetType().FullName;

    private readonly record struct PathInfo(string Value, bool Addressable, bool AllowsBinding, ResultPathBindingInfo? ResultPathBinding)
    {
        public static PathInfo Root { get; } = new("$", true, true, null);

        public PathInfo WithoutResultPathBinding() => this with { AllowsBinding = false, ResultPathBinding = null };
    }

    private sealed record FieldEntry(string SortKey, string Name, PathInfo ParentPath, object? Value, bool CanUseResultPath);

    private sealed record LocatableMetadata(Guid OutputPortId, string OutputPortName, string ResultPath);

    private readonly record struct DetailLocatorKey(Guid OutputPortId, int ResultPathVersion, string ResultPath);

    private sealed record ResultPathBindingInfo(
        Guid OutputPortId,
        string OutputPortName,
        object? OutputPortRoot,
        IReadOnlyList<ResultPathSegment> RelativeSegments)
    {
        public ResultPathBindingInfo AppendKey(string key)
        {
            var segments = RelativeSegments.Concat([ResultPathSegment.ObjectKey(key)]).ToArray();
            return this with { RelativeSegments = segments };
        }

        public ResultPathBindingInfo AppendIndex(int index)
        {
            var segments = RelativeSegments.Concat([ResultPathSegment.Index(index)]).ToArray();
            return this with { RelativeSegments = segments };
        }

        public bool ContainsIndex => RelativeSegments.Any(segment => segment.Kind == ResultPathSegmentKind.Index);

        public string ToCanonicalPath() => ResultPathFormatter.Format(RelativeSegments);
    }

    private sealed class ProjectionContext
    {
        private readonly HashSet<object> _activeReferences = new(ReferenceEqualityComparer.Instance);
        private readonly HashSet<string> _diagnosticKeys = new(StringComparer.Ordinal);
        private readonly Dictionary<string, List<ExecutionObservationOutputPortV1>> _outputPortsByName;
        private readonly Dictionary<string, object?> _outputPortRootsByName;
        private int _nodeCount;

        public ProjectionContext(
            IReadOnlyList<ExecutionObservationOutputPortV1>? outputPorts,
            IReadOnlyDictionary<string, object>? outputData)
        {
            _outputPortsByName = (outputPorts ?? [])
                .Where(port => port.Id != Guid.Empty && !string.IsNullOrWhiteSpace(port.Name))
                .GroupBy(port => port.Name, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);
            _outputPortRootsByName = (outputData ?? new Dictionary<string, object>())
                .GroupBy(pair => pair.Key, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => (object?)group.First().Value, StringComparer.Ordinal);
        }

        public List<ExecutionObservationDiagnosticV1> Diagnostics { get; } = new();
        public bool Truncated { get; private set; }

        public bool HasOutputPorts => _outputPortsByName.Count > 0;

        public ResultPathBindingInfo? CreateOutputPortBinding(string outputKey, string pathHint)
        {
            if (!_outputPortsByName.TryGetValue(outputKey, out var ports))
            {
                AddDiagnostic("resultpath-port-missing", "Observation output key does not match a declared output port; canonical ResultPath metadata omitted.", pathHint);
                return null;
            }

            if (ports.Count != 1)
            {
                AddDiagnostic("resultpath-port-ambiguous", "Observation output key matches multiple declared output ports; canonical ResultPath metadata omitted.", pathHint);
                return null;
            }

            if (!_outputPortRootsByName.TryGetValue(outputKey, out var outputPortRoot))
            {
                AddDiagnostic("resultpath-port-missing", "Observation output key has no runtime output value; canonical ResultPath metadata omitted.", pathHint);
                return null;
            }

            var port = ports[0];
            return new ResultPathBindingInfo(port.Id, port.Name, outputPortRoot, []);
        }

        public bool TryReserveNode(string pathHint)
        {
            _nodeCount++;
            if (_nodeCount <= MaxNodes)
            {
                return true;
            }

            AddDiagnostic("node-limit", $"Detail node count exceeded limit {MaxNodes}.", pathHint);
            return false;
        }

        public bool EnterReference(object value, string pathHint)
        {
            if (_activeReferences.Add(value))
            {
                return true;
            }

            AddDiagnostic("circular-reference", "Circular reference detected.", pathHint);
            return false;
        }

        public void LeaveReference(object value)
        {
            _activeReferences.Remove(value);
        }

        public void MarkTruncated()
        {
            Truncated = true;
        }

        public void AddDiagnostic(string code, string message, string pathHint)
        {
            var safeCode = code.Length <= MaxDiagnosticCodeChars ? code : code[..MaxDiagnosticCodeChars];
            var safeMessage = message.Length <= MaxDiagnosticMessageChars ? message : message[..MaxDiagnosticMessageChars] + "...";
            var safePath = pathHint.Length <= MaxPathHintChars ? pathHint : pathHint[..MaxPathHintChars] + "...";
            var key = $"{safeCode}|{safePath}";
            if (!_diagnosticKeys.Add(key))
            {
                return;
            }

            Truncated = Truncated ||
                        safeCode.Contains("limit", StringComparison.OrdinalIgnoreCase) ||
                        safeCode.Contains("budget", StringComparison.OrdinalIgnoreCase) ||
                        safeCode.Contains("circular", StringComparison.OrdinalIgnoreCase) ||
                        safeCode.Contains("unsupported", StringComparison.OrdinalIgnoreCase);

            if (Diagnostics.Count >= MaxDiagnosticCount)
            {
                if (IsPriorityDiagnostic(safeCode) && Diagnostics.All(item => item.Code != safeCode))
                {
                    Diagnostics.RemoveAt(Diagnostics.Count - 1);
                    Diagnostics.Add(new ExecutionObservationDiagnosticV1
                    {
                        Code = safeCode,
                        Message = safeMessage,
                        PathHint = safePath
                    });
                }

                return;
            }

            Diagnostics.Add(new ExecutionObservationDiagnosticV1
            {
                Code = safeCode,
                Message = safeMessage,
                PathHint = safePath
            });
        }

        private static bool IsPriorityDiagnostic(string code) =>
            string.Equals(code, "byte-budget", StringComparison.Ordinal) ||
            string.Equals(code, "node-limit", StringComparison.Ordinal);
    }

    private sealed class LegacySanitizationContext
    {
        private readonly HashSet<object> _activeReferences = new(ReferenceEqualityComparer.Instance);
        private int _nodeCount;

        public bool TryReserve()
        {
            _nodeCount++;
            return _nodeCount <= MaxNodes;
        }

        public bool EnterReference(object value) => _activeReferences.Add(value);

        public void LeaveReference(object value)
        {
            _activeReferences.Remove(value);
        }

        public void MarkTruncated()
        {
        }
    }

    private sealed record ResourceDescriptor(
        string Kind,
        string DisplayValue,
        Dictionary<string, string> Metadata,
        bool Truncated,
        PreviewArtifactReferenceV1? Artifact)
    {
        public Dictionary<string, object?> ToLegacyDictionary()
        {
            var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["kind"] = Kind,
                ["displayValue"] = DisplayValue,
                ["truncated"] = Truncated
            };
            if (Artifact != null)
            {
                result["artifact"] = Artifact;
            }

            foreach (var pair in Metadata.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                result[pair.Key] = pair.Value;
            }

            return result;
        }
    }
}
