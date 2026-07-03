using System.Collections;
using System.Globalization;
using System.Text.Json;
using ClearVision.Product.Core.ValueObjects;
using ClearVision.Product.Infrastructure.Operators;
using Microsoft.Extensions.Logging;
using OpenCvSharp;

namespace ClearVision.Product.Desktop.PreviewArtifacts;

public sealed class PreviewArtifactMaterializer
{
    private const int MaxArtifactsPerPreview = 32;
    private const int InlineCollectionThreshold = 64;
    private const int MaxCollectionItemsToArtifact = 4096;
    private const int MaxMaterializationDepth = 4;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly PreviewArtifactStore _store;
    private readonly ILogger<PreviewArtifactMaterializer> _logger;

    public PreviewArtifactMaterializer(
        PreviewArtifactStore store,
        ILogger<PreviewArtifactMaterializer> logger)
    {
        _store = store;
        _logger = logger;
    }

    public PreviewArtifactMaterializationResult MaterializePreview(
        PreviewArtifactOwnerScope owner,
        IReadOnlyDictionary<string, object>? nodeOutput,
        byte[]? inputImageBytes,
        byte[]? outputImageBytes,
        CancellationToken cancellationToken)
    {
        var batch = _store.CreateBatch(owner);
        var artifacts = new List<PreviewArtifactReferenceV1>();
        var diagnostics = new List<string>();

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (inputImageBytes is { Length: > 0 })
            {
                TryAddBytesArtifact(
                    batch,
                    artifacts,
                    diagnostics,
                    "image",
                    "inputImage",
                    "$.inputImageBase64",
                    inputImageBytes,
                    cancellationToken,
                    out _);
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (outputImageBytes is { Length: > 0 })
            {
                TryAddBytesArtifact(
                    batch,
                    artifacts,
                    diagnostics,
                    "image",
                    "outputImage",
                    "$.outputImageBase64",
                    outputImageBytes,
                    cancellationToken,
                    out _);
            }

            var outputData = MaterializeDictionary(
                batch,
                artifacts,
                diagnostics,
                nodeOutput,
                "$",
                0,
                cancellationToken);

            return new PreviewArtifactMaterializationResult(batch, outputData, artifacts, diagnostics);
        }
        catch
        {
            batch.Rollback();
            batch.Dispose();
            throw;
        }
    }

    private Dictionary<string, object> MaterializeDictionary(
        PreviewArtifactBatch batch,
        List<PreviewArtifactReferenceV1> artifacts,
        List<string> diagnostics,
        IReadOnlyDictionary<string, object>? source,
        string pathHint,
        int depth,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        if (source == null)
        {
            return result;
        }

        foreach (var pair in source.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var childPath = AppendObjectPath(pathHint, pair.Key);
            result[pair.Key] = MaterializeValue(
                batch,
                artifacts,
                diagnostics,
                pair.Value,
                childPath,
                pair.Key,
                depth + 1,
                cancellationToken)!;
        }

        return result;
    }

    private object? MaterializeValue(
        PreviewArtifactBatch batch,
        List<PreviewArtifactReferenceV1> artifacts,
        List<string> diagnostics,
        object? value,
        string pathHint,
        string? sourceKey,
        int depth,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (value == null)
        {
            return null;
        }

        if (depth > MaxMaterializationDepth)
        {
            return value;
        }

        try
        {
            switch (value)
            {
                case PreviewArtifactValue:
                    return value;
                case ImageWrapper wrapper:
                    return MaterializeImageWrapper(batch, artifacts, diagnostics, wrapper, pathHint, sourceKey, cancellationToken);
                case Mat mat:
                    return MaterializeMat(batch, artifacts, diagnostics, mat, pathHint, sourceKey, cancellationToken);
                case byte[] bytes:
                    return MaterializeBytes(batch, artifacts, diagnostics, bytes, pathHint, sourceKey, cancellationToken);
                case Stream stream:
                    return Descriptor(
                        "stream",
                        "Stream descriptor; content omitted.",
                        pathHint,
                        value,
                        new Dictionary<string, string>(StringComparer.Ordinal)
                        {
                            ["canRead"] = stream.CanRead ? "true" : "false",
                            ["canSeek"] = stream.CanSeek ? "true" : "false"
                        });
                case IReadOnlyDictionary<string, object> typedReadOnlyDictionary:
                    return MaterializeDictionary(
                        batch,
                        artifacts,
                        diagnostics,
                        typedReadOnlyDictionary,
                        pathHint,
                        depth,
                        cancellationToken);
                case IDictionary<string, object> typedDictionary:
                    return MaterializeDictionary(
                        batch,
                        artifacts,
                        diagnostics,
                        typedDictionary.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase),
                        pathHint,
                        depth,
                        cancellationToken);
                case IDictionary:
                    return Descriptor(
                        "objectDescriptor",
                        "Unknown dictionary descriptor; content omitted by artifact materializer.",
                        pathHint,
                        value);
                case IEnumerable and not string when TryMaterializeKnownFiniteCollection(
                    batch,
                    artifacts,
                    diagnostics,
                    value,
                    pathHint,
                    sourceKey,
                    cancellationToken,
                    out var collectionValue):
                    return collectionValue;
                case IEnumerable and not string:
                    return Descriptor(
                        "unsupportedEnumerable",
                        "Unknown enumerable; content omitted by artifact materializer.",
                        pathHint,
                        value);
                default:
                    return value;
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            var message = $"PreviewArtifactMaterializationFailed at {pathHint}: {ex.GetBaseException().Message}";
            diagnostics.Add(message);
            _logger.LogWarning(ex, "[PreviewArtifact] Failed to materialize {PathHint}", pathHint);
            return Descriptor("resource", message, pathHint, value);
        }
    }

    private PreviewArtifactValue MaterializeImageWrapper(
        PreviewArtifactBatch batch,
        List<PreviewArtifactReferenceV1> artifacts,
        List<string> diagnostics,
        ImageWrapper wrapper,
        string pathHint,
        string? sourceKey,
        CancellationToken cancellationToken)
    {
        var bytes = wrapper.GetBytes();
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

        return TryAddBytesArtifact(
            batch,
            artifacts,
            diagnostics,
            "image",
            ResolveRole(sourceKey, "image"),
            pathHint,
            bytes,
            cancellationToken,
            out var artifact)
            ? Descriptor("image", "ImageWrapper artifact; content omitted.", pathHint, wrapper, metadata, artifact)
            : Descriptor("image", "ImageWrapper descriptor; artifact omitted.", pathHint, wrapper, metadata);
    }

    private PreviewArtifactValue MaterializeMat(
        PreviewArtifactBatch batch,
        List<PreviewArtifactReferenceV1> artifacts,
        List<string> diagnostics,
        Mat mat,
        string pathHint,
        string? sourceKey,
        CancellationToken cancellationToken)
    {
        if (mat.Empty())
        {
            return Descriptor("matrix", "Empty Mat descriptor; content omitted.", pathHint, mat);
        }

        var bytes = mat.ToBytes(".png");
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["width"] = mat.Width.ToString(CultureInfo.InvariantCulture),
            ["height"] = mat.Height.ToString(CultureInfo.InvariantCulture),
            ["channels"] = mat.Channels().ToString(CultureInfo.InvariantCulture),
            ["type"] = mat.Type().ToString()
        };

        return TryAddBytesArtifact(
            batch,
            artifacts,
            diagnostics,
            "image",
            ResolveRole(sourceKey, "image"),
            pathHint,
            bytes,
            cancellationToken,
            out var artifact)
            ? Descriptor("image", "Mat artifact; content omitted.", pathHint, mat, metadata, artifact)
            : Descriptor("matrix", "Mat descriptor; artifact omitted.", pathHint, mat, metadata);
    }

    private PreviewArtifactValue MaterializeBytes(
        PreviewArtifactBatch batch,
        List<PreviewArtifactReferenceV1> artifacts,
        List<string> diagnostics,
        byte[] bytes,
        string pathHint,
        string? sourceKey,
        CancellationToken cancellationToken)
    {
        var contentType = DetectContentType(bytes);
        var kind = contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
            ? "image"
            : ResolveKind(sourceKey, bytes);
        var role = ResolveRole(sourceKey, kind);

        return TryAddBytesArtifact(
            batch,
            artifacts,
            diagnostics,
            kind,
            role,
            pathHint,
            bytes,
            cancellationToken,
            out var artifact)
            ? Descriptor(kind, $"{kind} artifact; content omitted.", pathHint, bytes, artifact: artifact)
            : Descriptor(kind, $"{kind} descriptor; artifact omitted.", pathHint, bytes);
    }

    private bool TryMaterializeKnownFiniteCollection(
        PreviewArtifactBatch batch,
        List<PreviewArtifactReferenceV1> artifacts,
        List<string> diagnostics,
        object value,
        string pathHint,
        string? sourceKey,
        CancellationToken cancellationToken,
        out object? materialized)
    {
        materialized = null;
        if (!TryReadKnownFiniteCollection(value, out var count, out var readItem, out var itemKind, out var itemType))
        {
            return false;
        }

        var forceArtifact = itemType == typeof(CircleCaliperFitV2ProfileEvidence) && count > 0;
        if (!forceArtifact && count <= InlineCollectionThreshold)
        {
            materialized = value;
            return true;
        }

        if (count > MaxCollectionItemsToArtifact)
        {
            diagnostics.Add($"PreviewArtifactCollectionTooLarge at {pathHint}: {count} items.");
            materialized = Descriptor(
                "collection",
                $"Known finite collection has {count.ToString(CultureInfo.InvariantCulture)} items; artifact omitted.",
                pathHint,
                value,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["itemKind"] = itemKind,
                    ["count"] = count.ToString(CultureInfo.InvariantCulture)
                });
            return true;
        }

        cancellationToken.ThrowIfCancellationRequested();
        var jsonSafeItems = new List<object?>(count);
        for (var index = 0; index < count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            jsonSafeItems.Add(ToJsonSafeCollectionItem(readItem(index)));
        }

        var bytes = JsonSerializer.SerializeToUtf8Bytes(jsonSafeItems, JsonOptions);
        if (forceArtifact && bytes.Length > CircleCaliperFitV2Request.MaxProfileEvidenceArtifactBytes)
        {
            diagnostics.Add($"PreviewArtifactProfileEvidenceTooLarge at {pathHint}: {bytes.Length.ToString(CultureInfo.InvariantCulture)} bytes.");
            materialized = Descriptor(
                "profile",
                "CaliperFitV2 profile evidence exceeded the bounded artifact byte budget; artifact omitted.",
                pathHint,
                value,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["itemKind"] = itemKind,
                    ["count"] = count.ToString(CultureInfo.InvariantCulture),
                    ["bytes"] = bytes.Length.ToString(CultureInfo.InvariantCulture),
                    ["maxBytes"] = CircleCaliperFitV2Request.MaxProfileEvidenceArtifactBytes.ToString(CultureInfo.InvariantCulture)
                });
            return true;
        }

        var role = ResolveRole(sourceKey, itemKind == "point" ? "pointSet" : "profile");
        if (!TryAddBytesArtifact(
                batch,
                artifacts,
                diagnostics,
                itemKind == "point" ? "pointSet" : "profile",
                role,
                pathHint,
                bytes,
                cancellationToken,
                out var artifact,
                contentTypeOverride: "application/json"))
        {
            materialized = Descriptor(
                itemKind == "point" ? "pointSet" : "profile",
                "Known finite collection descriptor; artifact omitted.",
                pathHint,
                value,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["itemKind"] = itemKind,
                    ["count"] = count.ToString(CultureInfo.InvariantCulture)
                });
            return true;
        }

        materialized = Descriptor(
            itemKind == "point" ? "pointSet" : "profile",
            "Known finite collection artifact; content omitted.",
            pathHint,
            value,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["itemKind"] = itemKind,
                ["count"] = count.ToString(CultureInfo.InvariantCulture),
                ["bytes"] = bytes.Length.ToString(CultureInfo.InvariantCulture)
            },
            artifact);
        return true;
    }

    private bool TryAddBytesArtifact(
        PreviewArtifactBatch batch,
        List<PreviewArtifactReferenceV1> artifacts,
        List<string> diagnostics,
        string kind,
        string role,
        string pathHint,
        byte[] bytes,
        CancellationToken cancellationToken,
        out PreviewArtifactReferenceV1? artifact,
        string? contentTypeOverride = null)
    {
        artifact = null;
        cancellationToken.ThrowIfCancellationRequested();
        if (artifacts.Count >= MaxArtifactsPerPreview)
        {
            diagnostics.Add($"PreviewArtifactCountLimit at {pathHint}: max {MaxArtifactsPerPreview} artifacts.");
            return false;
        }

        try
        {
            var contentType = contentTypeOverride ?? DetectContentType(bytes);
            var dimensions = contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
                ? TryReadImageDimensions(bytes)
                : null;
            artifact = batch.Add(
                kind,
                role,
                pathHint,
                contentType,
                bytes,
                dimensions?.Width,
                dimensions?.Height,
                dimensions?.Channels);
            artifacts.Add(artifact);
            return true;
        }
        catch (PreviewArtifactStoreRejectedException ex)
        {
            diagnostics.Add($"PreviewArtifactRejected at {pathHint}: {ex.Message}");
            return false;
        }
    }

    private static PreviewArtifactValue Descriptor(
        string kind,
        string displayValue,
        string pathHint,
        object value,
        Dictionary<string, string>? metadata = null,
        PreviewArtifactReferenceV1? artifact = null)
    {
        var resultMetadata = metadata == null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(metadata, StringComparer.Ordinal);
        if (value is byte[] bytes)
        {
            resultMetadata["length"] = bytes.LongLength.ToString(CultureInfo.InvariantCulture);
        }

        return new PreviewArtifactValue
        {
            Kind = kind,
            DisplayValue = displayValue,
            OriginalType = value.GetType().FullName,
            PathHint = pathHint,
            Truncated = true,
            Artifact = artifact,
            Metadata = resultMetadata
        };
    }

    private static bool TryReadKnownFiniteCollection(
        object value,
        out int count,
        out Func<int, object?> readItem,
        out string itemKind,
        out Type? itemType)
    {
        count = 0;
        readItem = _ => null;
        itemKind = string.Empty;
        itemType = null;
        var type = value.GetType();
        itemType = ResolveFiniteCollectionItemType(type);
        if (itemType == null || !IsSupportedFiniteArtifactItemType(itemType, out itemKind))
        {
            return false;
        }

        if (value is Array array && array.Rank == 1)
        {
            count = array.Length;
            readItem = index => array.GetValue(index);
            return true;
        }

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>) && value is IList list)
        {
            count = list.Count;
            readItem = index => list[index];
            return true;
        }

        return false;
    }

    private static Type? ResolveFiniteCollectionItemType(Type type)
    {
        if (type.IsArray)
        {
            return type.GetElementType();
        }

        return type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>)
            ? type.GetGenericArguments()[0]
            : null;
    }

    private static bool IsSupportedFiniteArtifactItemType(Type type, out string kind)
    {
        if (type == typeof(float) ||
            type == typeof(double) ||
            type == typeof(int) ||
            type == typeof(CircleCaliperFitV2ProfileEvidence))
        {
            kind = "profile";
            return true;
        }

        if (type == typeof(OpenCvSharp.Point) ||
            type == typeof(Point2f) ||
            type == typeof(Point2d) ||
            type == typeof(Position) ||
            type == typeof(CircleCaliperFitV2Point))
        {
            kind = "point";
            return true;
        }

        kind = string.Empty;
        return false;
    }

    private static object? ToJsonSafeCollectionItem(object? item) =>
        item switch
        {
            OpenCvSharp.Point point => new { x = point.X, y = point.Y },
            Point2f point => new { x = point.X, y = point.Y },
            Point2d point => new { x = point.X, y = point.Y },
            Position point => new { x = point.X, y = point.Y },
            CircleCaliperFitV2Point point => new
            {
                x = point.X,
                y = point.Y,
                caliperIndex = point.CaliperIndex,
                angleDegrees = point.AngleDegrees,
                radius = point.Radius,
                strength = point.Strength,
                polarity = point.Polarity
            },
            CircleCaliperFitV2ProfileEvidence evidence => new
            {
                contractVersion = evidence.ContractVersion,
                caliperIndex = evidence.CaliperIndex,
                angleDegrees = evidence.AngleDegrees,
                startX = evidence.StartX,
                startY = evidence.StartY,
                endX = evidence.EndX,
                endY = evidence.EndY,
                originalSampleCount = evidence.OriginalSampleCount,
                sampleStride = evidence.SampleStride,
                threshold = evidence.Threshold,
                selectedPosition = evidence.SelectedPosition,
                selectedStrength = evidence.SelectedStrength,
                selectedPolarity = evidence.SelectedPolarity,
                samples = evidence.Samples
            },
            float number when float.IsFinite(number) => number,
            double number when double.IsFinite(number) => number,
            int number => number,
            null => null,
            _ => null
        };

    private static string ResolveKind(string? sourceKey, byte[] bytes)
    {
        var key = sourceKey ?? string.Empty;
        if (key.Contains("mask", StringComparison.OrdinalIgnoreCase))
        {
            return "mask";
        }

        if (key.Contains("profile", StringComparison.OrdinalIgnoreCase))
        {
            return "profile";
        }

        if (key.Contains("point", StringComparison.OrdinalIgnoreCase))
        {
            return "pointSet";
        }

        return bytes.Length == 0 ? "binary" : "binary";
    }

    private static string ResolveRole(string? sourceKey, string fallback)
    {
        var key = sourceKey ?? string.Empty;
        if (key.Contains("input", StringComparison.OrdinalIgnoreCase))
        {
            return "inputImage";
        }

        if (key.Contains("output", StringComparison.OrdinalIgnoreCase))
        {
            return "outputImage";
        }

        if (key.Contains("mask", StringComparison.OrdinalIgnoreCase))
        {
            return "mask";
        }

        if (key.Contains("profile", StringComparison.OrdinalIgnoreCase))
        {
            return "profile";
        }

        if (key.Contains("point", StringComparison.OrdinalIgnoreCase))
        {
            return "pointSet";
        }

        return fallback;
    }

    private static string DetectContentType(byte[] bytes)
    {
        if (bytes.Length >= 8 &&
            bytes[0] == 0x89 &&
            bytes[1] == 0x50 &&
            bytes[2] == 0x4E &&
            bytes[3] == 0x47 &&
            bytes[4] == 0x0D &&
            bytes[5] == 0x0A &&
            bytes[6] == 0x1A &&
            bytes[7] == 0x0A)
        {
            return "image/png";
        }

        if (bytes.Length >= 3 &&
            bytes[0] == 0xFF &&
            bytes[1] == 0xD8 &&
            bytes[2] == 0xFF)
        {
            return "image/jpeg";
        }

        if (bytes.Length >= 6 &&
            bytes[0] == 0x47 &&
            bytes[1] == 0x49 &&
            bytes[2] == 0x46 &&
            bytes[3] == 0x38)
        {
            return "image/gif";
        }

        if (bytes.Length >= 2 &&
            bytes[0] == 0x42 &&
            bytes[1] == 0x4D)
        {
            return "image/bmp";
        }

        return "application/octet-stream";
    }

    private static (int Width, int Height, int Channels)? TryReadImageDimensions(byte[] bytes)
    {
        try
        {
            using var image = Cv2.ImDecode(bytes, ImreadModes.Unchanged);
            if (image.Empty())
            {
                return null;
            }

            return (image.Width, image.Height, image.Channels());
        }
        catch
        {
            return null;
        }
    }

    private static string AppendObjectPath(string path, string key)
    {
        var escaped = key.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);
        return path == "$"
            ? $"$.\"{escaped}\""
            : $"{path}.\"{escaped}\"";
    }
}
