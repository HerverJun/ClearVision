using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace ClearVision.Product.Infrastructure.Calibration;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SpatialFrameKindV1
{
    ImageFull = 0,
    RoiLocal = 1,
    Undistorted = 2,
    World2D = 3
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SpatialUnitV1
{
    Pixel = 0,
    Millimeter = 1,
    Unitless = 2,
    Meter = 3,
    Centimeter = 4,
    Micrometer = 5
}

/// <summary>
/// Stable spatial frame reference. Units are part of the identity to keep transforms explicit.
/// </summary>
public sealed record FrameRefV1
{
    [JsonConstructor]
    public FrameRefV1(
        string frameId,
        SpatialFrameKindV1 kind,
        SpatialUnitV1 unit,
        string? parentFrameId = null)
    {
        if (!TryValidate(frameId, kind, unit, parentFrameId, out var normalizedFrameId, out var normalizedParentFrameId, out var error))
        {
            throw new ArgumentException(error, nameof(frameId));
        }

        FrameId = normalizedFrameId;
        Kind = kind;
        Unit = unit;
        ParentFrameId = normalizedParentFrameId;
    }

    public string FrameId { get; }

    public SpatialFrameKindV1 Kind { get; }

    public SpatialUnitV1 Unit { get; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ParentFrameId { get; }

    public string UnitSymbol => Unit switch
    {
        SpatialUnitV1.Pixel => "px",
        SpatialUnitV1.Millimeter => "mm",
        SpatialUnitV1.Meter => "m",
        SpatialUnitV1.Centimeter => "cm",
        SpatialUnitV1.Micrometer => "um",
        SpatialUnitV1.Unitless => "unitless",
        _ => "unknown"
    };

    public static FrameRefV1 ImageFull(string frameId = "image.full") =>
        new(frameId, SpatialFrameKindV1.ImageFull, SpatialUnitV1.Pixel);

    public static FrameRefV1 RoiLocal(string frameId, string parentFrameId) =>
        new(frameId, SpatialFrameKindV1.RoiLocal, SpatialUnitV1.Pixel, parentFrameId);

    public static FrameRefV1 Undistorted(string frameId = "image.undistorted") =>
        new(frameId, SpatialFrameKindV1.Undistorted, SpatialUnitV1.Pixel);

    public static FrameRefV1 World2D(string frameId = "world.2d", SpatialUnitV1 unit = SpatialUnitV1.Millimeter) =>
        new(frameId, SpatialFrameKindV1.World2D, unit);

    public static bool TryCreate(
        string frameId,
        SpatialFrameKindV1 kind,
        SpatialUnitV1 unit,
        out FrameRefV1 frame,
        out string error,
        string? parentFrameId = null)
    {
        frame = ImageFull();
        if (!TryValidate(frameId, kind, unit, parentFrameId, out var normalizedFrameId, out var normalizedParentFrameId, out error))
        {
            return false;
        }

        frame = new FrameRefV1(normalizedFrameId, kind, unit, normalizedParentFrameId);
        return true;
    }

    internal static bool AreTransformUnitsAllowed(SpatialUnitV1 sourceUnit, SpatialUnitV1 targetUnit)
    {
        if (sourceUnit == targetUnit)
        {
            return true;
        }

        return (sourceUnit, targetUnit) is
            (SpatialUnitV1.Pixel, SpatialUnitV1.Millimeter) or
            (SpatialUnitV1.Millimeter, SpatialUnitV1.Pixel) or
            (SpatialUnitV1.Pixel, SpatialUnitV1.Meter) or
            (SpatialUnitV1.Meter, SpatialUnitV1.Pixel) or
            (SpatialUnitV1.Pixel, SpatialUnitV1.Centimeter) or
            (SpatialUnitV1.Centimeter, SpatialUnitV1.Pixel) or
            (SpatialUnitV1.Pixel, SpatialUnitV1.Micrometer) or
            (SpatialUnitV1.Micrometer, SpatialUnitV1.Pixel);
    }

    private static bool TryValidate(
        string frameId,
        SpatialFrameKindV1 kind,
        SpatialUnitV1 unit,
        string? parentFrameId,
        out string normalizedFrameId,
        out string? normalizedParentFrameId,
        out string error)
    {
        normalizedFrameId = NormalizeId(frameId);
        normalizedParentFrameId = string.IsNullOrWhiteSpace(parentFrameId) ? null : NormalizeId(parentFrameId);
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(normalizedFrameId))
        {
            error = "FrameId is required.";
            return false;
        }

        if (normalizedFrameId.Length > 128)
        {
            error = "FrameId must be 128 characters or less.";
            return false;
        }

        if (normalizedParentFrameId?.Length > 128)
        {
            error = "ParentFrameId must be 128 characters or less.";
            return false;
        }

        if (!Enum.IsDefined(kind))
        {
            error = $"Frame kind {kind} is not supported.";
            return false;
        }

        if (!Enum.IsDefined(unit))
        {
            error = $"Spatial unit {unit} is not supported.";
            return false;
        }

        if (!IsUnitAllowedForFrame(kind, unit))
        {
            error = $"{kind} does not support unit {unit}.";
            return false;
        }

        return true;
    }

    private static bool IsUnitAllowedForFrame(SpatialFrameKindV1 kind, SpatialUnitV1 unit) =>
        kind switch
        {
            SpatialFrameKindV1.ImageFull or SpatialFrameKindV1.RoiLocal or SpatialFrameKindV1.Undistorted =>
                unit is SpatialUnitV1.Pixel or SpatialUnitV1.Unitless,
            SpatialFrameKindV1.World2D =>
                unit is SpatialUnitV1.Millimeter or SpatialUnitV1.Meter or SpatialUnitV1.Centimeter or SpatialUnitV1.Micrometer,
            _ => false
        };

    private static string NormalizeId(string value) => value.Trim();
}

/// <summary>
/// 3x3 homogeneous 2D transform. Direction is targetPoint = T(sourcePoint).
/// </summary>
public sealed class SpatialTransform2DV1
{
    private const double Epsilon = 1e-12;
    private readonly double[][] _matrix3x3;

    [JsonConstructor]
    public SpatialTransform2DV1(
        FrameRefV1 sourceFrame,
        FrameRefV1 targetFrame,
        double[][] matrix3x3)
    {
        if (!TryValidate(sourceFrame, targetFrame, matrix3x3, out var normalizedMatrix, out var error))
        {
            throw new ArgumentException(error, nameof(matrix3x3));
        }

        SourceFrame = sourceFrame;
        TargetFrame = targetFrame;
        _matrix3x3 = normalizedMatrix;
    }

    public FrameRefV1 SourceFrame { get; }

    public FrameRefV1 TargetFrame { get; }

    public double[][] Matrix3x3 => CloneMatrix(_matrix3x3);

    public static SpatialTransform2DV1 Identity(FrameRefV1 frame) =>
        new(frame, frame, CreateIdentity3x3());

    public static bool TryCreate(
        FrameRefV1 sourceFrame,
        FrameRefV1 targetFrame,
        double[][] matrix3x3,
        out SpatialTransform2DV1 transform,
        out string error)
    {
        transform = Identity(sourceFrame);
        if (!TryValidate(sourceFrame, targetFrame, matrix3x3, out var normalizedMatrix, out error))
        {
            return false;
        }

        transform = new SpatialTransform2DV1(sourceFrame, targetFrame, normalizedMatrix);
        return true;
    }

    public bool TryApply(double x, double y, out double tx, out double ty, out string error)
    {
        return TryApplyMatrix(_matrix3x3, x, y, out tx, out ty, out error);
    }

    public bool TryInverse(out SpatialTransform2DV1 inverse, out string error)
    {
        inverse = Identity(TargetFrame);
        if (!TryInvert3x3(_matrix3x3, out var inverseMatrix, out error))
        {
            return false;
        }

        inverse = new SpatialTransform2DV1(TargetFrame, SourceFrame, inverseMatrix);
        return true;
    }

    public static bool TryCompose(
        SpatialTransform2DV1 first,
        SpatialTransform2DV1 second,
        out SpatialTransform2DV1 composed,
        out string error)
    {
        composed = Identity(first.SourceFrame);
        error = string.Empty;

        if (!Equals(first.TargetFrame, second.SourceFrame))
        {
            error = $"Frame mismatch: first target '{first.TargetFrame.FrameId}' does not equal second source '{second.SourceFrame.FrameId}'.";
            return false;
        }

        var matrix = Multiply3x3(second._matrix3x3, first._matrix3x3);
        composed = new SpatialTransform2DV1(first.SourceFrame, second.TargetFrame, matrix);
        return true;
    }

    internal double[][] CloneInternalMatrix() => CloneMatrix(_matrix3x3);

    private static bool TryValidate(
        FrameRefV1 sourceFrame,
        FrameRefV1 targetFrame,
        double[][] matrix3x3,
        out double[][] normalizedMatrix,
        out string error)
    {
        normalizedMatrix = CreateIdentity3x3();
        error = string.Empty;

        if (sourceFrame == null)
        {
            error = "Source frame is required.";
            return false;
        }

        if (targetFrame == null)
        {
            error = "Target frame is required.";
            return false;
        }

        if (!FrameRefV1.AreTransformUnitsAllowed(sourceFrame.Unit, targetFrame.Unit))
        {
            error = $"Transform unit combination {sourceFrame.UnitSymbol}->{targetFrame.UnitSymbol} is not allowed.";
            return false;
        }

        if (!HasMatrixShape(matrix3x3, 3, 3))
        {
            error = "Spatial transform requires a 3x3 matrix.";
            return false;
        }

        normalizedMatrix = CloneMatrix(matrix3x3);
        if (!IsFiniteMatrix(normalizedMatrix))
        {
            error = "Spatial transform matrix contains NaN or Infinity.";
            return false;
        }

        return true;
    }

    private static bool TryApplyMatrix(
        double[][] matrix,
        double x,
        double y,
        out double tx,
        out double ty,
        out string error)
    {
        tx = 0;
        ty = 0;
        error = string.Empty;

        if (!double.IsFinite(x) || !double.IsFinite(y))
        {
            error = "Input coordinate is not finite.";
            return false;
        }

        var w = matrix[2][0] * x + matrix[2][1] * y + matrix[2][2];
        if (Math.Abs(w) <= Epsilon)
        {
            error = "Homogeneous denominator is too close to zero.";
            return false;
        }

        tx = (matrix[0][0] * x + matrix[0][1] * y + matrix[0][2]) / w;
        ty = (matrix[1][0] * x + matrix[1][1] * y + matrix[1][2]) / w;

        if (!double.IsFinite(tx) || !double.IsFinite(ty))
        {
            error = "Transformed coordinate is not finite.";
            return false;
        }

        return true;
    }

    private static bool TryInvert3x3(double[][] matrix, out double[][] inverse, out string error)
    {
        inverse = CreateIdentity3x3();
        error = string.Empty;

        var a = matrix[0][0];
        var b = matrix[0][1];
        var c = matrix[0][2];
        var d = matrix[1][0];
        var e = matrix[1][1];
        var f = matrix[1][2];
        var g = matrix[2][0];
        var h = matrix[2][1];
        var i = matrix[2][2];

        var det = a * (e * i - f * h) - b * (d * i - f * g) + c * (d * h - e * g);
        if (Math.Abs(det) <= Epsilon)
        {
            error = "Spatial transform matrix is singular and cannot be inverted.";
            return false;
        }

        var invDet = 1.0 / det;
        inverse =
        [
            [(e * i - f * h) * invDet, (c * h - b * i) * invDet, (b * f - c * e) * invDet],
            [(f * g - d * i) * invDet, (a * i - c * g) * invDet, (c * d - a * f) * invDet],
            [(d * h - e * g) * invDet, (b * g - a * h) * invDet, (a * e - b * d) * invDet]
        ];

        if (!IsFiniteMatrix(inverse))
        {
            error = "Inverse spatial transform matrix contains NaN or Infinity.";
            return false;
        }

        return true;
    }

    private static double[][] Multiply3x3(double[][] left, double[][] right)
    {
        var result = CreateIdentity3x3();
        for (var row = 0; row < 3; row++)
        {
            for (var col = 0; col < 3; col++)
            {
                result[row][col] =
                    left[row][0] * right[0][col] +
                    left[row][1] * right[1][col] +
                    left[row][2] * right[2][col];
            }
        }

        return result;
    }

    private static bool HasMatrixShape(double[][]? matrix, int rows, int columns)
    {
        if (matrix == null || matrix.Length != rows)
        {
            return false;
        }

        return matrix.All(row => row is { Length: var length } && length == columns);
    }

    private static bool IsFiniteMatrix(double[][] matrix) =>
        matrix.All(row => row.All(double.IsFinite));

    private static double[][] CloneMatrix(double[][] source)
    {
        var clone = new double[source.Length][];
        for (var i = 0; i < source.Length; i++)
        {
            clone[i] = source[i].ToArray();
        }

        return clone;
    }

    public static double[][] CreateIdentity3x3() =>
    [
        [1, 0, 0],
        [0, 1, 0],
        [0, 0, 1]
    ];
}

public sealed class SpatialContextV1
{
    [JsonConstructor]
    public SpatialContextV1(
        FrameRefV1 currentFrame,
        IReadOnlyList<SpatialTransform2DV1>? transforms = null,
        SpatialContextBindingV1? binding = null)
    {
        CurrentFrame = currentFrame ?? throw new ArgumentNullException(nameof(currentFrame));
        Transforms = new ReadOnlyCollection<SpatialTransform2DV1>(
            (transforms ?? Enumerable.Empty<SpatialTransform2DV1>()).ToList());
        Binding = binding ?? SpatialContextBindingV1.Unbound;
    }

    public int SchemaVersion => 1;

    public FrameRefV1 CurrentFrame { get; }

    public IReadOnlyList<SpatialTransform2DV1> Transforms { get; }

    public SpatialContextBindingV1 Binding { get; }

    public static SpatialContextV1 DefaultImageFull(SpatialContextBindingV1? binding = null) =>
        new(FrameRefV1.ImageFull(), [SpatialTransform2DV1.Identity(FrameRefV1.ImageFull())], binding);

    public bool TryResolveTransform(
        FrameRefV1 sourceFrame,
        FrameRefV1 targetFrame,
        out SpatialTransform2DV1 transform,
        out string error)
    {
        transform = SpatialTransform2DV1.Identity(sourceFrame);
        error = string.Empty;

        if (Equals(sourceFrame, targetFrame))
        {
            return true;
        }

        var visited = new HashSet<FrameRefV1> { sourceFrame };
        var queue = new Queue<SpatialTransform2DV1>();
        queue.Enqueue(SpatialTransform2DV1.Identity(sourceFrame));

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            foreach (var candidate in Transforms.Where(item => Equals(item.SourceFrame, current.TargetFrame)))
            {
                if (!SpatialTransform2DV1.TryCompose(current, candidate, out var next, out error))
                {
                    return false;
                }

                if (Equals(next.TargetFrame, targetFrame))
                {
                    transform = next;
                    return true;
                }

                if (visited.Add(next.TargetFrame))
                {
                    queue.Enqueue(next);
                }
            }
        }

        error = $"No spatial transform path from '{sourceFrame.FrameId}' to '{targetFrame.FrameId}'.";
        return false;
    }
}

public sealed record SpatialContextBindingV1
{
    public static SpatialContextBindingV1 Unbound { get; } = new();

    public Guid? ProjectId { get; init; }

    public Guid? SourceOperatorId { get; init; }

    public Guid? OutputPortId { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? OutputName { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Guid? DebugSessionId { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? ClientRequestSequence { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? FlowRevision { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ArtifactId { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RunId { get; init; }

    public bool HasFlowOutputBinding =>
        SourceOperatorId.HasValue || OutputPortId.HasValue || !string.IsNullOrWhiteSpace(OutputName);

    public bool HasPreviewArtifactBinding => !string.IsNullOrWhiteSpace(ArtifactId);

    public static SpatialContextBindingV1 ForFlowOutput(
        Guid sourceOperatorId,
        Guid outputPortId,
        string outputName,
        Guid? projectId = null,
        string? runId = null)
    {
        return new SpatialContextBindingV1
        {
            ProjectId = projectId,
            SourceOperatorId = sourceOperatorId,
            OutputPortId = outputPortId,
            OutputName = NormalizeOptional(outputName),
            RunId = NormalizeOptional(runId)
        };
    }

    public static SpatialContextBindingV1 ForPreviewArtifact(
        Guid projectId,
        Guid sourceOperatorId,
        Guid debugSessionId,
        long? clientRequestSequence,
        long? flowRevision,
        string artifactId,
        Guid? outputPortId = null,
        string? outputName = null)
    {
        return new SpatialContextBindingV1
        {
            ProjectId = projectId,
            SourceOperatorId = sourceOperatorId,
            OutputPortId = outputPortId,
            OutputName = NormalizeOptional(outputName),
            DebugSessionId = debugSessionId,
            ClientRequestSequence = clientRequestSequence,
            FlowRevision = flowRevision,
            ArtifactId = NormalizeOptional(artifactId)
        };
    }

    public bool TryValidate(out string error)
    {
        error = string.Empty;

        if (!IsValidGuid(ProjectId, "ProjectId", out error) ||
            !IsValidGuid(SourceOperatorId, "SourceOperatorId", out error) ||
            !IsValidGuid(OutputPortId, "OutputPortId", out error) ||
            !IsValidGuid(DebugSessionId, "DebugSessionId", out error))
        {
            return false;
        }

        if (HasFlowOutputBinding)
        {
            if (!SourceOperatorId.HasValue)
            {
                error = "Flow output binding requires SourceOperatorId.";
                return false;
            }

            if (!OutputPortId.HasValue && string.IsNullOrWhiteSpace(OutputName))
            {
                error = "Flow output binding requires OutputPortId or OutputName.";
                return false;
            }
        }

        if (HasPreviewArtifactBinding)
        {
            if (!ProjectId.HasValue || !SourceOperatorId.HasValue || !DebugSessionId.HasValue)
            {
                error = "Preview artifact binding requires ProjectId, SourceOperatorId, and DebugSessionId.";
                return false;
            }

            if (!IsSafeArtifactId(ArtifactId!))
            {
                error = "ArtifactId contains unsupported characters.";
                return false;
            }
        }

        if (ClientRequestSequence is <= 0)
        {
            error = "ClientRequestSequence must be positive when present.";
            return false;
        }

        if (FlowRevision is <= 0)
        {
            error = "FlowRevision must be positive when present.";
            return false;
        }

        return true;
    }

    private static bool IsValidGuid(Guid? value, string name, out string error)
    {
        error = string.Empty;
        if (value == Guid.Empty)
        {
            error = $"{name} must not be empty.";
            return false;
        }

        return true;
    }

    private static bool IsSafeArtifactId(string artifactId)
    {
        if (string.IsNullOrWhiteSpace(artifactId) || artifactId.Length > 128)
        {
            return false;
        }

        return artifactId.All(ch =>
            char.IsAsciiLetterOrDigit(ch) ||
            ch is '_' or '-');
    }

    private static string? NormalizeOptional(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }
}
