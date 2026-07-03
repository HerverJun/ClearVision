using System.Text.Json;
using OpenCvSharp;

namespace ClearVision.Product.Infrastructure.Calibration;

public enum SpatialCalibrationTransformMode
{
    PixelToWorld = 0,
    WorldToPixel = 1
}

public sealed record SpatialCalibrationTransformRequest(
    IReadOnlyList<Point3d> Points,
    SpatialContextV1? SpatialContext,
    CalibrationBundleV2 Bundle,
    CalibrationPlanarTransformRuntime Runtime,
    SpatialCalibrationTransformMode Mode,
    double WorldPlaneZ,
    WorldUnitContract WorldUnit,
    bool UseSpatialContextAsWorldInput,
    string? RequestedInputFrame = null,
    string? RequestedOutputFrame = null)
{
    public double UnitScale => WorldUnit.MillimetersPerUnit;
}

public sealed record WorldUnitContract(
    SpatialUnitV1 SpatialUnit,
    string UnitSymbol,
    double MillimetersPerUnit,
    IReadOnlyList<string> Diagnostics);

public sealed record SpatialCalibrationTransformResult(
    IReadOnlyList<Point3d> OutputPoints,
    FrameRefV1 InputFrame,
    FrameRefV1 CalibrationSourceFrame,
    FrameRefV1 CalibrationTargetFrame,
    FrameRefV1 OutputFrame,
    string InputUnit,
    string OutputUnit,
    int AppliedSpatialTransformCount,
    IReadOnlyList<string> TransformChain,
    bool CompatibilityMode,
    IReadOnlyList<string> Diagnostics);

public static class SpatialCalibrationTransformService
{
    private const double Epsilon = 1e-12;

    public static bool TryTransformPlanar(
        SpatialCalibrationTransformRequest request,
        out SpatialCalibrationTransformResult result,
        out string error)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Points);
        ArgumentNullException.ThrowIfNull(request.Bundle);
        ArgumentNullException.ThrowIfNull(request.Runtime);

        result = EmptyResult();
        error = string.Empty;

        if (request.WorldUnit == null || request.UnitScale <= 0 || !double.IsFinite(request.UnitScale))
        {
            error = "WorldUnitContract must resolve to a positive finite millimeters-per-unit value.";
            return false;
        }

        if (!double.IsFinite(request.WorldPlaneZ))
        {
            error = "WorldPlaneZ must be finite.";
            return false;
        }

        if (!TryNormalizeBundleFrame(request.Bundle.SourceFrame, FrameRefV1.ImageFull(), out var sourceFrame, out var sourceDiagnostics, out error) ||
            !TryNormalizeBundleFrame(request.Bundle.TargetFrame, FrameRefV1.World2D(unit: request.WorldUnit.SpatialUnit), out var targetFrame, out var targetDiagnostics, out error))
        {
            return false;
        }

        var diagnostics = new List<string>();
        diagnostics.AddRange(request.WorldUnit.Diagnostics);
        diagnostics.AddRange(sourceDiagnostics);
        diagnostics.AddRange(targetDiagnostics);

        if (sourceFrame.Kind is not (SpatialFrameKindV1.ImageFull or SpatialFrameKindV1.Undistorted))
        {
            error = $"Calibration SourceFrame '{request.Bundle.SourceFrame}' must normalize to ImageFull or Undistorted for planar PixelToWorld.";
            return false;
        }

        if (targetFrame.Kind != SpatialFrameKindV1.World2D)
        {
            error = $"Calibration TargetFrame '{request.Bundle.TargetFrame}' must normalize to World2D for planar PixelToWorld.";
            return false;
        }

        return request.Mode switch
        {
            SpatialCalibrationTransformMode.PixelToWorld => TryPlanarPixelToWorld(
                request,
                sourceFrame,
                targetFrame,
                diagnostics,
                out result,
                out error),
            SpatialCalibrationTransformMode.WorldToPixel => TryPlanarWorldToPixel(
                request,
                sourceFrame,
                targetFrame,
                diagnostics,
                out result,
                out error),
            _ => FailUnsupportedMode(out result, out error)
        };
    }

    public static bool TryReadSpatialContext(object? raw, out SpatialContextV1 context, out string error)
    {
        context = SpatialContextV1.DefaultImageFull();
        error = string.Empty;

        switch (raw)
        {
            case SpatialContextV1 typed:
                context = typed;
                return ValidateContextBinding(context, out error);
            case JsonElement element:
                try
                {
                    var parsed = element.Deserialize<SpatialContextV1>(SpatialJson.Options);
                    if (parsed == null)
                    {
                        error = "SpatialContext JSON deserialized to null.";
                        return false;
                    }

                    context = parsed;
                    return ValidateContextBinding(context, out error);
                }
                catch (Exception ex)
                {
                    error = $"SpatialContext JSON is malformed: {ex.GetBaseException().Message}";
                    return false;
                }
            case string text when !string.IsNullOrWhiteSpace(text):
                try
                {
                    var parsed = JsonSerializer.Deserialize<SpatialContextV1>(text, SpatialJson.Options);
                    if (parsed == null)
                    {
                        error = "SpatialContext JSON deserialized to null.";
                        return false;
                    }

                    context = parsed;
                    return ValidateContextBinding(context, out error);
                }
                catch (Exception ex)
                {
                    error = $"SpatialContext JSON is malformed: {ex.GetBaseException().Message}";
                    return false;
                }
            default:
                error = raw == null
                    ? "SpatialContext value is null."
                    : $"Unsupported SpatialContext value type '{raw.GetType().Name}'.";
                return false;
        }
    }

    public static bool TryResolveSpatialTransform(
        SpatialContextV1 context,
        FrameRefV1 sourceFrame,
        FrameRefV1 targetFrame,
        bool allowInverse,
        out SpatialTransform2DV1 transform,
        out IReadOnlyList<SpatialTransform2DV1> path,
        out string error)
    {
        transform = SpatialTransform2DV1.Identity(sourceFrame);
        path = Array.Empty<SpatialTransform2DV1>();
        error = string.Empty;

        if (Equals(sourceFrame, targetFrame))
        {
            return true;
        }

        var visited = new HashSet<FrameRefV1> { sourceFrame };
        var queue = new Queue<TransformSearchState>();
        queue.Enqueue(new TransformSearchState(SpatialTransform2DV1.Identity(sourceFrame), []));

        while (queue.Count > 0)
        {
            var state = queue.Dequeue();
            if (!TryEnumerateTransformEdges(context, state.Transform.TargetFrame, allowInverse, out var edges, out error))
            {
                return false;
            }

            foreach (var edge in edges)
            {
                if (!SpatialTransform2DV1.TryCompose(state.Transform, edge, out var next, out error))
                {
                    return false;
                }

                var nextPath = state.Path.Concat([edge]).ToList();
                if (Equals(next.TargetFrame, targetFrame))
                {
                    transform = next;
                    path = nextPath;
                    return true;
                }

                if (visited.Add(next.TargetFrame))
                {
                    queue.Enqueue(new TransformSearchState(next, nextPath));
                }
            }
        }

        error = $"No spatial transform path from '{sourceFrame.FrameId}' to '{targetFrame.FrameId}'.";
        return false;
    }

    public static bool TryNormalizeBundleFrame(
        string? rawFrame,
        FrameRefV1 defaultFrame,
        out FrameRefV1 frame,
        out IReadOnlyList<string> diagnostics,
        out string error)
    {
        frame = defaultFrame;
        diagnostics = Array.Empty<string>();
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(rawFrame))
        {
            error = "Calibration frame is required.";
            return false;
        }

        var raw = rawFrame.Trim();
        var token = NormalizeFrameToken(raw);
        var compatibility = false;
        switch (token)
        {
            case "imagefull":
            case "imagepixel":
            case "imagepixels":
            case "fullimage":
            case "image":
                frame = FrameRefV1.ImageFull();
                compatibility = !raw.Equals("image.full", StringComparison.OrdinalIgnoreCase);
                break;
            case "imageundistorted":
            case "undistorted":
            case "undistortedimage":
                frame = FrameRefV1.Undistorted();
                compatibility = !raw.Equals("image.undistorted", StringComparison.OrdinalIgnoreCase);
                break;
            case "world2d":
            case "world":
            case "worldframe":
                frame = FrameRefV1.World2D(unit: defaultFrame.Unit);
                compatibility = !raw.Equals("world.2d", StringComparison.OrdinalIgnoreCase);
                break;
            case "roilocal":
                frame = FrameRefV1.RoiLocal("roi.local", "image.full");
                compatibility = true;
                break;
            default:
                error = $"Unsupported calibration frame '{rawFrame}'.";
                return false;
        }

        diagnostics = compatibility
            ? [$"Compatibility frame mapping: '{raw}' -> '{frame.FrameId}' ({frame.Kind}, {frame.UnitSymbol})."]
            : Array.Empty<string>();
        return true;
    }

    public static string NormalizeFrameToken(string? frame)
    {
        if (string.IsNullOrWhiteSpace(frame))
        {
            return string.Empty;
        }

        return new string(frame.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
    }

    public static bool TryResolveWorldUnitContract(
        string? bundleUnit,
        double configuredUnitScale,
        bool unitScaleExplicitlyConfigured,
        out WorldUnitContract contract,
        out string error)
    {
        contract = new WorldUnitContract(SpatialUnitV1.Millimeter, "mm", 1.0, Array.Empty<string>());
        error = string.Empty;

        if (configuredUnitScale <= 0 || !double.IsFinite(configuredUnitScale))
        {
            error = "SPATIAL_UNIT_INCOMPATIBLE: UnitScale must be a positive finite number.";
            return false;
        }

        if (!TryResolveKnownWorldUnit(bundleUnit, out var spatialUnit, out var symbol, out var fixedMillimetersPerUnit))
        {
            error = $"SPATIAL_UNIT_INCOMPATIBLE: unsupported world unit '{bundleUnit}'.";
            return false;
        }

        if (unitScaleExplicitlyConfigured &&
            Math.Abs(configuredUnitScale - fixedMillimetersPerUnit) > Epsilon)
        {
            error = $"SPATIAL_UNIT_INCOMPATIBLE: UnitScale {configuredUnitScale.ToString("R", System.Globalization.CultureInfo.InvariantCulture)} mm/unit conflicts with bundle unit '{symbol}' fixed ratio {fixedMillimetersPerUnit.ToString("R", System.Globalization.CultureInfo.InvariantCulture)} mm/unit.";
            return false;
        }

        var diagnostics = new List<string>
        {
            $"WorldUnitContract: bundle unit '{symbol}' => {fixedMillimetersPerUnit.ToString("R", System.Globalization.CultureInfo.InvariantCulture)} mm/unit."
        };
        if (unitScaleExplicitlyConfigured)
        {
            diagnostics.Add("WorldUnitContract: explicit UnitScale matches known physical unit ratio.");
        }

        contract = new WorldUnitContract(spatialUnit, symbol, fixedMillimetersPerUnit, diagnostics);
        return true;
    }

    public static string NormalizeWorldUnit(string? rawUnit, double unitScale)
    {
        return TryResolveKnownWorldUnit(rawUnit, out _, out var symbol, out _)
            ? symbol
            : throw new InvalidOperationException($"Unsupported world unit '{rawUnit}'.");
    }

    public static bool TryGetMillimetersPerUnit(
        SpatialUnitV1 unit,
        out double millimetersPerUnit,
        out string unitSymbol,
        out string error)
    {
        error = string.Empty;
        (millimetersPerUnit, unitSymbol) = unit switch
        {
            SpatialUnitV1.Millimeter => (1.0, "mm"),
            SpatialUnitV1.Centimeter => (10.0, "cm"),
            SpatialUnitV1.Meter => (1000.0, "m"),
            SpatialUnitV1.Micrometer => (0.001, "um"),
            _ => (double.NaN, string.Empty)
        };

        if (double.IsFinite(millimetersPerUnit))
        {
            return true;
        }

        error = $"SPATIAL_UNIT_INCOMPATIBLE: World2D unit '{unit}' is not a supported physical unit.";
        return false;
    }

    private static bool TryResolveKnownWorldUnit(
        string? rawUnit,
        out SpatialUnitV1 spatialUnit,
        out string unitSymbol,
        out double millimetersPerUnit)
    {
        var token = string.IsNullOrWhiteSpace(rawUnit)
            ? string.Empty
            : rawUnit.Trim().ToLowerInvariant();

        (spatialUnit, unitSymbol, millimetersPerUnit) = token switch
        {
            "mm" or "millimeter" or "millimeters" => (SpatialUnitV1.Millimeter, "mm", 1.0),
            "cm" or "centimeter" or "centimeters" => (SpatialUnitV1.Centimeter, "cm", 10.0),
            "m" or "meter" or "meters" => (SpatialUnitV1.Meter, "m", 1000.0),
            "um" or "µm" or "μm" or "micrometer" or "micrometers" => (SpatialUnitV1.Micrometer, "um", 0.001),
            _ => (SpatialUnitV1.Millimeter, string.Empty, double.NaN)
        };

        return double.IsFinite(millimetersPerUnit);
    }

    public static IReadOnlyList<string> DescribeTransformChain(
        IReadOnlyList<SpatialTransform2DV1> spatialPath,
        FrameRefV1 calibrationSourceFrame,
        FrameRefV1 calibrationTargetFrame,
        SpatialCalibrationTransformMode mode)
    {
        var chain = new List<string>();
        if (mode == SpatialCalibrationTransformMode.PixelToWorld)
        {
            chain.AddRange(spatialPath.Select(transform =>
                $"{transform.SourceFrame.FrameId}->{transform.TargetFrame.FrameId}"));
            chain.Add($"{calibrationSourceFrame.FrameId}->{calibrationTargetFrame.FrameId}");
        }
        else
        {
            chain.Add($"{calibrationTargetFrame.FrameId}->{calibrationSourceFrame.FrameId}");
            chain.AddRange(spatialPath.Select(transform =>
                $"{transform.SourceFrame.FrameId}->{transform.TargetFrame.FrameId}"));
        }

        return chain;
    }

    private static bool TryPlanarPixelToWorld(
        SpatialCalibrationTransformRequest request,
        FrameRefV1 sourceFrame,
        FrameRefV1 targetFrame,
        List<string> diagnostics,
        out SpatialCalibrationTransformResult result,
        out string error)
    {
        result = EmptyResult();
        error = string.Empty;

        if (!TryResolveInputFrame(
                request.RequestedInputFrame,
                request.SpatialContext,
                sourceFrame,
                out var inputFrame,
                out var inputDiagnostics,
                out error))
        {
            return false;
        }

        diagnostics.AddRange(inputDiagnostics);
        if (inputFrame.Kind == SpatialFrameKindV1.World2D)
        {
            error = "SPATIAL_FRAME_DIRECTION_INVALID: PixelToWorld input frame cannot be World2D.";
            return false;
        }

        if (!IsAutoFrame(request.RequestedOutputFrame))
        {
            if (!TryNormalizeRequestedFrame(
                    request.RequestedOutputFrame,
                    targetFrame,
                    out var requestedOutputFrame,
                    out var outputDiagnostics,
                    out error))
            {
                return false;
            }

            diagnostics.AddRange(outputDiagnostics);
            if (requestedOutputFrame.Kind != SpatialFrameKindV1.World2D)
            {
                error = $"SPATIAL_FRAME_DIRECTION_INVALID: PixelToWorld output frame must be World2D, got {requestedOutputFrame.Kind}.";
                return false;
            }
        }

        if (inputFrame.Kind == SpatialFrameKindV1.RoiLocal && request.SpatialContext == null)
        {
            error = "RoiLocal PixelToWorld input requires SpatialContext; missing context is not treated as zero crop offset.";
            return false;
        }

        if (!TryResolveSpatialPathForCalibration(
                request.SpatialContext,
                inputFrame,
                sourceFrame,
                allowInverse: false,
                out var inputToCalibration,
                out var spatialPath,
                out error))
        {
            return false;
        }

        var outputPoints = new List<Point3d>(request.Points.Count);
        foreach (var point in request.Points)
        {
            if (!inputToCalibration.TryApply(point.X, point.Y, out var sourceX, out var sourceY, out error))
            {
                error = $"Spatial input transform failed: {error}";
                return false;
            }

            if (!request.Runtime.TryApplyForward(sourceX, sourceY, out var worldXmm, out var worldYmm, out error))
            {
                error = $"Planar forward transform failed: {error}";
                return false;
            }

            var x = worldXmm / request.UnitScale;
            var y = worldYmm / request.UnitScale;
            var z = request.WorldPlaneZ / request.UnitScale;
            if (!AreFinite(x, y, z))
            {
                error = "Planar PixelToWorld produced non-finite output.";
                return false;
            }

            outputPoints.Add(new Point3d(x, y, z));
        }

        result = new SpatialCalibrationTransformResult(
            outputPoints,
            inputFrame,
            sourceFrame,
            targetFrame,
            targetFrame,
            inputFrame.UnitSymbol,
            request.WorldUnit.UnitSymbol,
            spatialPath.Count,
            DescribeTransformChain(spatialPath, sourceFrame, targetFrame, SpatialCalibrationTransformMode.PixelToWorld),
            diagnostics.Count > 0,
            diagnostics);
        return true;
    }

    private static bool TryPlanarWorldToPixel(
        SpatialCalibrationTransformRequest request,
        FrameRefV1 sourceFrame,
        FrameRefV1 targetFrame,
        List<string> diagnostics,
        out SpatialCalibrationTransformResult result,
        out string error)
    {
        result = EmptyResult();
        error = string.Empty;

        if (!TryResolveWorldToPixelInputFrame(
                request.RequestedInputFrame,
                request.SpatialContext,
                request.UseSpatialContextAsWorldInput,
                targetFrame,
                out var inputFrame,
                out var inputDiagnostics,
                out error))
        {
            return false;
        }

        diagnostics.AddRange(inputDiagnostics);
        if (inputFrame.Kind != SpatialFrameKindV1.World2D)
        {
            error = $"SPATIAL_FRAME_DIRECTION_INVALID: WorldToPixel input frame must be World2D, got {inputFrame.Kind}.";
            return false;
        }

        if (!TryResolveOutputFrame(
                request.RequestedOutputFrame,
                request.SpatialContext,
                sourceFrame,
                out var outputFrame,
                out var outputDiagnostics,
                out error))
        {
            return false;
        }

        diagnostics.AddRange(outputDiagnostics);
        if (outputFrame.Kind == SpatialFrameKindV1.RoiLocal && request.SpatialContext == null)
        {
            error = "RoiLocal WorldToPixel output requires SpatialContext.";
            return false;
        }

        if (!TryResolveSpatialPathForCalibration(
                request.SpatialContext,
                sourceFrame,
                outputFrame,
                allowInverse: true,
                out var calibrationToOutput,
                out var spatialPath,
                out error))
        {
            return false;
        }

        if (!TryGetMillimetersPerUnit(inputFrame.Unit, out var inputMillimetersPerUnit, out var inputUnitSymbol, out error))
        {
            return false;
        }

        if (!Equals(inputFrame, targetFrame))
        {
            diagnostics.Add(
                $"WorldToPixel input unit authority: PointsSpatialContext frame '{inputFrame.FrameId}' ({inputUnitSymbol}) => {inputMillimetersPerUnit.ToString("R", System.Globalization.CultureInfo.InvariantCulture)} mm/unit.");
        }

        var outputPoints = new List<Point3d>(request.Points.Count);
        foreach (var point in request.Points)
        {
            var worldXmm = point.X * inputMillimetersPerUnit;
            var worldYmm = point.Y * inputMillimetersPerUnit;
            if (!request.Runtime.TryApplyInverse(worldXmm, worldYmm, out var sourceX, out var sourceY, out error))
            {
                error = $"Planar inverse transform failed: {error}";
                return false;
            }

            if (!calibrationToOutput.TryApply(sourceX, sourceY, out var outputX, out var outputY, out error))
            {
                error = $"Spatial output transform failed: {error}";
                return false;
            }

            if (!AreFinite(outputX, outputY))
            {
                error = "Planar WorldToPixel produced non-finite output.";
                return false;
            }

            outputPoints.Add(new Point3d(outputX, outputY, 0));
        }

        result = new SpatialCalibrationTransformResult(
            outputPoints,
            inputFrame,
            sourceFrame,
            targetFrame,
            outputFrame,
            inputUnitSymbol,
            outputFrame.UnitSymbol,
            spatialPath.Count,
            DescribeTransformChain(spatialPath, sourceFrame, targetFrame, SpatialCalibrationTransformMode.WorldToPixel),
            diagnostics.Count > 0,
            diagnostics);
        return true;
    }

    private static bool TryResolveWorldToPixelInputFrame(
        string? requestedFrame,
        SpatialContextV1? spatialContext,
        bool useSpatialContextAsWorldInput,
        FrameRefV1 defaultFrame,
        out FrameRefV1 inputFrame,
        out IReadOnlyList<string> diagnostics,
        out string error)
    {
        if (spatialContext != null && useSpatialContextAsWorldInput)
        {
            inputFrame = spatialContext.CurrentFrame;
            error = string.Empty;
            var resolvedDiagnostics = new List<string>
            {
                $"WorldToPixel input frame resolved from PointsSpatialContext '{inputFrame.FrameId}' ({inputFrame.Kind}, {inputFrame.UnitSymbol})."
            };
            if (!IsAutoFrame(requestedFrame))
            {
                if (!TryNormalizeRequestedFrame(requestedFrame, defaultFrame, out var requested, out var requestedDiagnostics, out error))
                {
                    diagnostics = resolvedDiagnostics;
                    return false;
                }

                resolvedDiagnostics.AddRange(requestedDiagnostics);
                if (requested.Kind != SpatialFrameKindV1.World2D)
                {
                    diagnostics = resolvedDiagnostics;
                    error = $"SPATIAL_FRAME_DIRECTION_INVALID: WorldToPixel input frame must be World2D, got {requested.Kind}.";
                    return false;
                }
            }

            diagnostics = resolvedDiagnostics;
            return true;
        }

        return TryNormalizeRequestedFrame(requestedFrame, defaultFrame, out inputFrame, out diagnostics, out error);
    }

    public static bool TryResolveInputFrame(
        string? requestedFrame,
        SpatialContextV1? spatialContext,
        FrameRefV1 defaultFrame,
        out FrameRefV1 inputFrame,
        out IReadOnlyList<string> diagnostics,
        out string error)
    {
        if (IsRequestedRoiLocal(requestedFrame))
        {
            return TryResolveRequestedRoiLocalFrame(
                requestedFrame,
                spatialContext,
                role: "input",
                out inputFrame,
                out diagnostics,
                out error);
        }

        if (IsAutoFrame(requestedFrame))
        {
            inputFrame = spatialContext?.CurrentFrame ?? defaultFrame;
            diagnostics = Array.Empty<string>();
            error = string.Empty;
            return true;
        }

        return TryNormalizeRequestedFrame(requestedFrame, defaultFrame, out inputFrame, out diagnostics, out error);
    }

    public static bool TryResolveOutputFrame(
        string? requestedFrame,
        SpatialContextV1? spatialContext,
        FrameRefV1 defaultFrame,
        out FrameRefV1 outputFrame,
        out IReadOnlyList<string> diagnostics,
        out string error)
    {
        if (IsRequestedRoiLocal(requestedFrame))
        {
            return TryResolveRequestedRoiLocalFrame(
                requestedFrame,
                spatialContext,
                role: "output",
                out outputFrame,
                out diagnostics,
                out error);
        }

        if (IsAutoFrame(requestedFrame))
        {
            outputFrame = spatialContext?.CurrentFrame.Kind == SpatialFrameKindV1.RoiLocal
                ? spatialContext.CurrentFrame
                : defaultFrame;
            diagnostics = Array.Empty<string>();
            error = string.Empty;
            return true;
        }

        return TryNormalizeRequestedFrame(requestedFrame, defaultFrame, out outputFrame, out diagnostics, out error);
    }

    public static bool TryNormalizeRequestedFrame(
        string? requestedFrame,
        FrameRefV1 defaultFrame,
        out FrameRefV1 frame,
        out IReadOnlyList<string> diagnostics,
        out string error)
    {
        frame = defaultFrame;
        diagnostics = Array.Empty<string>();
        error = string.Empty;

        if (IsAutoFrame(requestedFrame))
        {
            return true;
        }

        var token = NormalizeFrameToken(requestedFrame);
        switch (token)
        {
            case "imagefull":
            case "image":
                frame = FrameRefV1.ImageFull();
                diagnostics = requestedFrame!.Trim().Equals("image.full", StringComparison.OrdinalIgnoreCase)
                    ? Array.Empty<string>()
                    : [$"Compatibility frame mapping: '{requestedFrame.Trim()}' -> 'image.full' (ImageFull, px)."];
                return true;
            case "imageundistorted":
            case "undistorted":
                frame = FrameRefV1.Undistorted();
                diagnostics = requestedFrame!.Trim().Equals("image.undistorted", StringComparison.OrdinalIgnoreCase)
                    ? Array.Empty<string>()
                    : [$"Compatibility frame mapping: '{requestedFrame.Trim()}' -> 'image.undistorted' (Undistorted, px)."];
                return true;
            case "world2d":
            case "world":
                frame = FrameRefV1.World2D(unit: defaultFrame.Unit);
                diagnostics = requestedFrame!.Trim().Equals("world.2d", StringComparison.OrdinalIgnoreCase)
                    ? Array.Empty<string>()
                    : [$"Compatibility frame mapping: '{requestedFrame.Trim()}' -> 'world.2d' (World2D, {frame.UnitSymbol})."];
                return true;
            case "roilocal":
                frame = FrameRefV1.RoiLocal("roi.local.requested", "image.full");
                diagnostics = [$"Compatibility frame mapping: '{requestedFrame!.Trim()}' -> 'roi.local.requested' (RoiLocal, px)."];
                return true;
            default:
                error = $"Unsupported requested frame '{requestedFrame}'.";
                return false;
        }
    }

    public static bool TryResolveSpatialPathForCalibration(
        SpatialContextV1? spatialContext,
        FrameRefV1 sourceFrame,
        FrameRefV1 targetFrame,
        bool allowInverse,
        out SpatialTransform2DV1 transform,
        out IReadOnlyList<SpatialTransform2DV1> path,
        out string error)
    {
        transform = SpatialTransform2DV1.Identity(sourceFrame);
        path = Array.Empty<SpatialTransform2DV1>();
        error = string.Empty;

        if (Equals(sourceFrame, targetFrame))
        {
            return true;
        }

        if (spatialContext == null)
        {
            error = $"SpatialContext is required to transform from '{sourceFrame.FrameId}' to '{targetFrame.FrameId}'.";
            return false;
        }

        return TryResolveSpatialTransform(spatialContext, sourceFrame, targetFrame, allowInverse, out transform, out path, out error);
    }

    private static bool TryResolveRequestedRoiLocalFrame(
        string? requestedFrame,
        SpatialContextV1? spatialContext,
        string role,
        out FrameRefV1 frame,
        out IReadOnlyList<string> diagnostics,
        out string error)
    {
        frame = FrameRefV1.RoiLocal("roi.local.requested", "image.full");
        diagnostics = Array.Empty<string>();
        error = string.Empty;

        var raw = string.IsNullOrWhiteSpace(requestedFrame)
            ? "RoiLocal"
            : requestedFrame.Trim();
        if (spatialContext == null)
        {
            error = $"RoiLocal {role} frame requires SpatialContext; missing context is not treated as zero crop offset.";
            return false;
        }

        if (spatialContext.CurrentFrame.Kind != SpatialFrameKindV1.RoiLocal)
        {
            error = $"RoiLocal {role} frame requires SpatialContext.CurrentFrame to be RoiLocal, got {spatialContext.CurrentFrame.Kind}.";
            return false;
        }

        frame = spatialContext.CurrentFrame;
        diagnostics =
        [
            $"Requested RoiLocal {role} frame '{raw}' resolved to SpatialContext current frame '{frame.FrameId}' ({frame.Kind}, {frame.UnitSymbol})."
        ];
        return true;
    }

    private static bool TryEnumerateTransformEdges(
        SpatialContextV1 context,
        FrameRefV1 frame,
        bool allowInverse,
        out IReadOnlyList<SpatialTransform2DV1> edges,
        out string error)
    {
        var result = new List<SpatialTransform2DV1>();
        edges = result;
        error = string.Empty;

        foreach (var transform in context.Transforms)
        {
            if (Equals(transform.SourceFrame, frame))
            {
                result.Add(transform);
            }

            if (!allowInverse || !Equals(transform.TargetFrame, frame))
            {
                continue;
            }

            if (!transform.TryInverse(out var inverse, out var inverseError))
            {
                error = inverseError;
                return false;
            }

            result.Add(inverse);
        }

        return true;
    }

    private static bool ValidateContextBinding(SpatialContextV1 context, out string error)
    {
        error = string.Empty;
        return context.Binding.TryValidate(out error);
    }

    private static bool IsAutoFrame(string? value) =>
        string.IsNullOrWhiteSpace(value) ||
        value.Trim().Equals("Auto", StringComparison.OrdinalIgnoreCase);

    private static bool IsRequestedRoiLocal(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        NormalizeFrameToken(value) == "roilocal";

    private static bool FailUnsupportedMode(out SpatialCalibrationTransformResult result, out string error)
    {
        result = EmptyResult();
        error = "Unsupported spatial calibration transform mode.";
        return false;
    }

    private static bool AreFinite(params double[] values) => values.All(double.IsFinite);

    private static SpatialCalibrationTransformResult EmptyResult() =>
        new(
            Array.Empty<Point3d>(),
            FrameRefV1.ImageFull(),
            FrameRefV1.ImageFull(),
            FrameRefV1.World2D(),
            FrameRefV1.ImageFull(),
            "px",
            "px",
            0,
            Array.Empty<string>(),
            false,
            Array.Empty<string>());

    private sealed record TransformSearchState(
        SpatialTransform2DV1 Transform,
        IReadOnlyList<SpatialTransform2DV1> Path);

    private static class SpatialJson
    {
        public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
    }
}
