using System.Collections;
using System.Globalization;
using System.Text.Json;
using ClearVision.Product.Core.Attributes;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Operators;
using ClearVision.Product.Core.RuntimeAssets;
using ClearVision.Product.Core.ValueObjects;
using ClearVision.Product.Infrastructure.Calibration;
using Microsoft.Extensions.Logging;
using OpenCvSharp;

namespace ClearVision.Product.Infrastructure.Operators;

[OperatorMeta(
    DisplayName = "像素世界映射",
    Description = "通过 CalibrationBundleV2 执行坐标转换，可使用 Transform2D 或相机射线与平面求交。",
    CategoryId = OperatorCategoryId.CalibrationAndCoordinates,
    IconName = "coordinate-transform",
    Keywords = new[] { "pixel", "world", "coordinate", "transform", "calibration", "ray-plane" },
    Version = "1.0.1"
)]
[InputPort("Image", "Input Image (Optional)", PortDataType.Image, IsRequired = false)]
[InputPort("Points", "Input Points", PortDataType.PointList, IsRequired = false)]
[InputPort("CalibrationData", "Calibration Bundle V2 JSON", PortDataType.String, IsRequired = false)]
[OutputPort("Image", "Visualization Image", PortDataType.Image)]
[OutputPort("TransformedPoints", "Transformed Points", PortDataType.PointList)]
[OutputPort("TransformResult", "Transform Result Details", PortDataType.Any)]
[OperatorParam("TransformMode", "Transform Mode", "enum", DefaultValue = "PixelToWorld", Options = new[] { "PixelToWorld|Pixel to World", "WorldToPixel|World to Pixel" })]
[OperatorParam("InputFrame", "Input Frame", "enum", DefaultValue = "Auto", Options = new[] { "Auto|Auto", "ImageFull|Image Full", "RoiLocal|ROI Local", "Undistorted|Undistorted", "World2D|World 2D" })]
[OperatorParam("OutputFrame", "Output Frame", "enum", DefaultValue = "Auto", Options = new[] { "Auto|Auto", "ImageFull|Image Full", "RoiLocal|ROI Local", "Undistorted|Undistorted", "World2D|World 2D" })]
[OperatorParam("WorldPlaneZ", "World Plane Z (mm)", "double", DefaultValue = 0.0)]
[OperatorParam("UnitScale", "Unit Scale (mm per unit)", "double", DefaultValue = 1.0, Min = 0.0001, Max = 10000.0)]
[OperatorParam("InputPointX", "Input Point X (Single Point Mode)", "double", DefaultValue = 0.0)]
[OperatorParam("InputPointY", "Input Point Y (Single Point Mode)", "double", DefaultValue = 0.0)]
[OperatorParam("CalibrationAssetId", "Runtime Calibration Asset Id", "string", DefaultValue = "")]
[OperatorParam("CalibrationBundleId", "Runtime Calibration Bundle Id", "string", DefaultValue = "")]
[OperatorParam("UseDistortion", "Use Distortion Model", "bool", DefaultValue = true)]
[OperatorParam("GenerateReport", "Generate Accuracy Report", "bool", DefaultValue = true)]
public class PixelToWorldTransformOperator : OperatorBase
{
    private const double Epsilon = 1e-12;
    private static readonly HashSet<int> BrownConradyCoefficientLengths = new() { 4, 5, 8, 12, 14 };

    public override OperatorType OperatorType => OperatorType.PixelToWorldTransform;

    public PixelToWorldTransformOperator(ILogger<PixelToWorldTransformOperator> logger) : base(logger)
    {
    }

    protected override Task<OperatorExecutionOutput> ExecuteCoreAsync(
        Operator @operator,
        Dictionary<string, object>? inputs,
        CancellationToken cancellationToken)
    {
        var transformMode = GetStringParam(@operator, "TransformMode", "PixelToWorld");
        var isPixelToWorld = transformMode.Equals("PixelToWorld", StringComparison.OrdinalIgnoreCase);
        var isWorldToPixel = transformMode.Equals("WorldToPixel", StringComparison.OrdinalIgnoreCase);
        if (!isPixelToWorld && !isWorldToPixel)
        {
            return Task.FromResult(OperatorExecutionOutput.Failure("TransformMode must be PixelToWorld or WorldToPixel."));
        }

        var worldPlaneZ = GetDoubleParam(@operator, "WorldPlaneZ", 0.0);
        var configuredUnitScale = GetDoubleParam(@operator, "UnitScale", 1.0);
        var requestedInputFrame = GetStringParam(@operator, "InputFrame", "Auto");
        var requestedOutputFrame = GetStringParam(@operator, "OutputFrame", "Auto");
        var useDistortion = GetBoolParam(@operator, "UseDistortion", true);
        var generateReport = GetBoolParam(@operator, "GenerateReport", true);

        if (!TryResolveCalibrationData(@operator, inputs, out var calibrationJson, out var runtimeCalibrationAsset, out var calibrationResolveError))
        {
            return Task.FromResult(OperatorExecutionOutput.Failure(calibrationResolveError ?? "CalibrationBundleV2 data is required."));
        }

        if (!CalibrationBundleV2Json.TryDeserialize(calibrationJson!, out var bundle, out var parseError))
        {
            return Task.FromResult(OperatorExecutionOutput.Failure($"Invalid CalibrationBundleV2: {parseError}"));
        }

        if (!CalibrationBundleV2Json.TryRequireAccepted(bundle, out var acceptedError))
        {
            return Task.FromResult(OperatorExecutionOutput.Failure(acceptedError));
        }

        if (!SpatialCalibrationTransformService.TryResolveWorldUnitContract(
                bundle.Unit,
                configuredUnitScale,
                IsParameterExplicitlyConfigured(@operator, "UnitScale"),
                out var worldUnit,
                out var unitError))
        {
            return Task.FromResult(OperatorExecutionOutput.Failure(unitError));
        }

        var unitScale = worldUnit.MillimetersPerUnit;

        if (!TryGetInputPoints(@operator, inputs, out var inputPoints, out var pointError))
        {
            return Task.FromResult(OperatorExecutionOutput.Failure(pointError));
        }

        if (!TryResolveInputSpatialContexts(
                inputs,
                out var pointsSpatialContext,
                out var imageSpatialContext,
                out var spatialContextDiagnostics,
                out var omitImageSpatialContextSidecar,
                out var pointsSpatialContextProvided,
                out var spatialContextError))
        {
            return Task.FromResult(OperatorExecutionOutput.Failure(spatialContextError));
        }

        if (bundle.Transform2D != null)
        {
            return Task.FromResult(ExecutePlanarPath(
                @operator,
                inputs,
                bundle,
                runtimeCalibrationAsset,
                inputPoints,
                pointsSpatialContext,
                imageSpatialContext,
                omitImageSpatialContextSidecar,
                pointsSpatialContextProvided,
                spatialContextDiagnostics,
                isPixelToWorld,
                requestedInputFrame,
                requestedOutputFrame,
                worldPlaneZ,
                worldUnit,
                generateReport));
        }

        return Task.FromResult(ExecuteRayPlanePath(
            @operator,
            inputs,
            bundle,
            runtimeCalibrationAsset,
            inputPoints,
            pointsSpatialContext,
            imageSpatialContext,
            omitImageSpatialContextSidecar,
            pointsSpatialContextProvided,
            spatialContextDiagnostics,
            isPixelToWorld,
            requestedInputFrame,
            requestedOutputFrame,
            worldPlaneZ,
            worldUnit,
            useDistortion,
            generateReport));
    }

    public override ValidationResult ValidateParameters(Operator @operator)
    {
        var unitScale = GetDoubleParam(@operator, "UnitScale", 1.0);
        if (unitScale <= 0 || !double.IsFinite(unitScale))
        {
            return ValidationResult.Invalid("UnitScale must be a positive finite number.");
        }

        var worldPlaneZ = GetDoubleParam(@operator, "WorldPlaneZ", 0.0);
        if (!double.IsFinite(worldPlaneZ))
        {
            return ValidationResult.Invalid("WorldPlaneZ must be finite.");
        }

        return ValidationResult.Valid();
    }

    private static bool IsParameterExplicitlyConfigured(Operator @operator, string parameterName)
    {
        return @operator.Parameters.Any(parameter =>
            string.Equals(parameter.Name, parameterName, StringComparison.OrdinalIgnoreCase));
    }

    private OperatorExecutionOutput ExecutePlanarPath(
        Operator @operator,
        Dictionary<string, object>? inputs,
        CalibrationBundleV2 bundle,
        RuntimeCalibrationBundleAsset? runtimeCalibrationAsset,
        IReadOnlyList<Point3d> inputPoints,
        SpatialContextV1? spatialContext,
        SpatialContextV1? imageSpatialContext,
        bool omitImageSpatialContextSidecar,
        bool pointsSpatialContextProvided,
        IReadOnlyList<string> spatialContextDiagnostics,
        bool isPixelToWorld,
        string? requestedInputFrame,
        string? requestedOutputFrame,
        double worldPlaneZ,
        WorldUnitContract worldUnit,
        bool generateReport)
    {
        if (!IsSupportedPlanarKind(bundle.CalibrationKind))
        {
            return OperatorExecutionOutput.Failure(
                $"Planar path requires CalibrationKind PlanarTransform2D/RigidTransform2D, got {bundle.CalibrationKind}.");
        }

        if (!CalibrationPlanarTransformRuntime.TryCreate(
                bundle,
                new[] { TransformModelV2.ScaleOffset, TransformModelV2.Similarity, TransformModelV2.Affine, TransformModelV2.Homography },
                out var runtime,
                out var runtimeError))
        {
            return OperatorExecutionOutput.Failure(runtimeError);
        }

        if (!SpatialCalibrationTransformService.TryTransformPlanar(
                new SpatialCalibrationTransformRequest(
                    inputPoints,
                    spatialContext,
                    bundle,
                    runtime,
                    isPixelToWorld
                        ? SpatialCalibrationTransformMode.PixelToWorld
                        : SpatialCalibrationTransformMode.WorldToPixel,
                    worldPlaneZ,
                    worldUnit,
                    pointsSpatialContextProvided,
                    requestedInputFrame,
                    requestedOutputFrame),
                out var transformResult,
                out var transformError))
        {
            return OperatorExecutionOutput.Failure(transformError);
        }

        Dictionary<string, object>? accuracyReport = null;
        if (generateReport &&
            !TryBuildPlanarAccuracyReport(
                runtime,
                spatialContext,
                inputPoints,
                transformResult.OutputPoints,
                isPixelToWorld,
                transformResult,
                transformResult.Diagnostics,
                out accuracyReport,
                out var reportError))
        {
            return OperatorExecutionOutput.Failure(reportError);
        }
        var outputDiagnostics = transformResult.Diagnostics.Concat(spatialContextDiagnostics).ToList();

        return BuildSuccessOutput(
            @operator,
            inputs,
            inputPoints,
            transformResult.OutputPoints,
            isPixelToWorld ? "PixelToWorld" : "WorldToPixel",
            "PlanarTransform2D",
            runtime.Model.ToString(),
            bundle,
            runtimeCalibrationAsset,
            transformResult,
            imageSpatialContext,
            omitImageSpatialContextSidecar,
            worldPlaneZ,
            worldUnit.MillimetersPerUnit,
            generateReport,
            accuracyReport,
            outputDiagnostics);
    }

    private OperatorExecutionOutput ExecuteRayPlanePath(
        Operator @operator,
        Dictionary<string, object>? inputs,
        CalibrationBundleV2 bundle,
        RuntimeCalibrationBundleAsset? runtimeCalibrationAsset,
        IReadOnlyList<Point3d> inputPoints,
        SpatialContextV1? spatialContext,
        SpatialContextV1? imageSpatialContext,
        bool omitImageSpatialContextSidecar,
        bool pointsSpatialContextProvided,
        IReadOnlyList<string> spatialContextDiagnostics,
        bool isPixelToWorld,
        string? requestedInputFrame,
        string? requestedOutputFrame,
        double worldPlaneZ,
        WorldUnitContract worldUnit,
        bool useDistortion,
        bool generateReport)
    {
        if (!TryCreateRayPlaneContext(bundle, out var context, out var contextError))
        {
            return OperatorExecutionOutput.Failure(contextError);
        }

        if (!TryCreateDistortionContext(bundle, useDistortion, out var distortion, out var distortionError))
        {
            return OperatorExecutionOutput.Failure(distortionError);
        }

        var diagnostics = new List<string>();
        if (!TryResolveRayPlaneCalibrationFrames(
                bundle,
                out var calibrationSourceFrame,
                out var calibrationTargetFrame,
                out var frameDiagnostics,
                out var frameError))
        {
            return OperatorExecutionOutput.Failure(frameError);
        }

        diagnostics.AddRange(frameDiagnostics);
        if (distortion.Enabled)
        {
            diagnostics.Add($"Distortion model applied in ray-plane PixelToWorld path: {distortion.Model}.");
        }

        if (!TryExecuteRayPlaneTransform(
                inputPoints,
                spatialContext,
                context,
                distortion,
                calibrationSourceFrame,
                calibrationTargetFrame,
                isPixelToWorld,
                requestedInputFrame,
                requestedOutputFrame,
                worldPlaneZ,
                worldUnit,
                pointsSpatialContextProvided,
                diagnostics,
                out var outputPoints,
                out var frameResult,
                out var transformError))
        {
            return OperatorExecutionOutput.Failure(transformError);
        }

        Dictionary<string, object>? accuracyReport = null;
        if (generateReport &&
            !TryBuildRayPlaneAccuracyReport(
                context,
                distortion,
                spatialContext,
                inputPoints,
                outputPoints,
                isPixelToWorld,
                worldPlaneZ,
                frameResult,
                frameResult.Diagnostics,
                out accuracyReport,
                out var reportError))
        {
            return OperatorExecutionOutput.Failure(reportError);
        }
        var outputDiagnostics = frameResult.Diagnostics.Concat(spatialContextDiagnostics).ToList();

        return BuildSuccessOutput(
            @operator,
            inputs,
            inputPoints,
            outputPoints,
            isPixelToWorld ? "PixelToWorld" : "WorldToPixel",
            "RayPlaneIntersection",
            "Projection",
            bundle,
            runtimeCalibrationAsset,
            frameResult,
            imageSpatialContext,
            omitImageSpatialContextSidecar,
            worldPlaneZ,
            worldUnit.MillimetersPerUnit,
            generateReport,
            accuracyReport,
            outputDiagnostics);
    }

    private static bool TryExecuteRayPlaneTransform(
        IReadOnlyList<Point3d> inputPoints,
        SpatialContextV1? spatialContext,
        RayPlaneContext context,
        DistortionContext distortion,
        FrameRefV1 calibrationSourceFrame,
        FrameRefV1 calibrationTargetFrame,
        bool isPixelToWorld,
        string? requestedInputFrame,
        string? requestedOutputFrame,
        double worldPlaneZ,
        WorldUnitContract worldUnit,
        bool useSpatialContextAsWorldInput,
        IReadOnlyList<string> baseDiagnostics,
        out IReadOnlyList<Point3d> outputPoints,
        out SpatialCalibrationTransformResult frameResult,
        out string error)
    {
        outputPoints = Array.Empty<Point3d>();
        frameResult = new SpatialCalibrationTransformResult(
            Array.Empty<Point3d>(),
            calibrationSourceFrame,
            calibrationSourceFrame,
            calibrationTargetFrame,
            calibrationTargetFrame,
            calibrationSourceFrame.UnitSymbol,
            calibrationTargetFrame.UnitSymbol,
            0,
            Array.Empty<string>(),
            false,
            baseDiagnostics);
        error = string.Empty;

        return isPixelToWorld
            ? TryExecuteRayPlanePixelToWorld(
                inputPoints,
                spatialContext,
                context,
                distortion,
                calibrationSourceFrame,
                calibrationTargetFrame,
                requestedInputFrame,
                requestedOutputFrame,
                worldPlaneZ,
                worldUnit,
                useSpatialContextAsWorldInput,
                baseDiagnostics,
                out outputPoints,
                out frameResult,
                out error)
            : TryExecuteRayPlaneWorldToPixel(
                inputPoints,
                spatialContext,
                context,
                distortion,
                calibrationSourceFrame,
                calibrationTargetFrame,
                requestedInputFrame,
                requestedOutputFrame,
                worldPlaneZ,
                worldUnit,
                useSpatialContextAsWorldInput,
                baseDiagnostics,
                out outputPoints,
                out frameResult,
                out error);
    }

    private static bool TryExecuteRayPlanePixelToWorld(
        IReadOnlyList<Point3d> inputPoints,
        SpatialContextV1? spatialContext,
        RayPlaneContext context,
        DistortionContext distortion,
        FrameRefV1 calibrationSourceFrame,
        FrameRefV1 calibrationTargetFrame,
        string? requestedInputFrame,
        string? requestedOutputFrame,
        double worldPlaneZ,
        WorldUnitContract worldUnit,
        bool useSpatialContextAsWorldInput,
        IReadOnlyList<string> baseDiagnostics,
        out IReadOnlyList<Point3d> outputPoints,
        out SpatialCalibrationTransformResult frameResult,
        out string error)
    {
        outputPoints = Array.Empty<Point3d>();
        frameResult = EmptyRayPlaneResult(baseDiagnostics, calibrationSourceFrame, calibrationTargetFrame);
        error = string.Empty;

        if (!SpatialCalibrationTransformService.TryResolveInputFrame(
                requestedInputFrame,
                spatialContext,
                calibrationSourceFrame,
                out var inputFrame,
                out var inputDiagnostics,
                out error))
        {
            return false;
        }

        var diagnostics = baseDiagnostics.Concat(worldUnit.Diagnostics).Concat(inputDiagnostics).ToList();
        if (inputFrame.Kind == SpatialFrameKindV1.World2D)
        {
            error = "SPATIAL_FRAME_DIRECTION_INVALID: PixelToWorld input frame cannot be World2D.";
            return false;
        }

        if (!SpatialCalibrationTransformService.TryNormalizeRequestedFrame(
                requestedOutputFrame,
                calibrationTargetFrame,
                out var outputFrame,
                out var outputDiagnostics,
                out error))
        {
            return false;
        }

        diagnostics.AddRange(outputDiagnostics);
        if (outputFrame.Kind != SpatialFrameKindV1.World2D)
        {
            error = $"SPATIAL_FRAME_DIRECTION_INVALID: PixelToWorld output frame must be World2D, got {outputFrame.Kind}.";
            return false;
        }

        if (!SpatialCalibrationTransformService.TryResolveSpatialPathForCalibration(
                spatialContext,
                inputFrame,
                calibrationSourceFrame,
                allowInverse: false,
                out var inputToCalibration,
                out var spatialPath,
                out error))
        {
            return false;
        }

        var transformed = new List<Point3d>(inputPoints.Count);
        foreach (var point in inputPoints)
        {
            if (!inputToCalibration.TryApply(point.X, point.Y, out var pixelX, out var pixelY, out error))
            {
                error = $"Spatial input transform failed: {error}";
                return false;
            }

            if (!TryPixelToWorldByRayPlane(context, distortion, pixelX, pixelY, worldPlaneZ, out var worldPointMm, out error))
            {
                error = $"Ray-plane PixelToWorld failed: {error}";
                return false;
            }

            var worldPoint = new Point3d(
                worldPointMm.X / worldUnit.MillimetersPerUnit,
                worldPointMm.Y / worldUnit.MillimetersPerUnit,
                worldPointMm.Z / worldUnit.MillimetersPerUnit);
            if (!IsFinite(worldPoint))
            {
                error = "Ray-plane PixelToWorld produced non-finite output.";
                return false;
            }

            transformed.Add(worldPoint);
        }

        outputPoints = transformed;
        frameResult = new SpatialCalibrationTransformResult(
            transformed,
            inputFrame,
            calibrationSourceFrame,
            calibrationTargetFrame,
            outputFrame,
            inputFrame.UnitSymbol,
            worldUnit.UnitSymbol,
            spatialPath.Count,
            SpatialCalibrationTransformService.DescribeTransformChain(
                spatialPath,
                calibrationSourceFrame,
                calibrationTargetFrame,
                SpatialCalibrationTransformMode.PixelToWorld),
            diagnostics.Count > 0,
            diagnostics);
        return true;
    }

    private static bool TryExecuteRayPlaneWorldToPixel(
        IReadOnlyList<Point3d> inputPoints,
        SpatialContextV1? spatialContext,
        RayPlaneContext context,
        DistortionContext distortion,
        FrameRefV1 calibrationSourceFrame,
        FrameRefV1 calibrationTargetFrame,
        string? requestedInputFrame,
        string? requestedOutputFrame,
        double worldPlaneZ,
        WorldUnitContract worldUnit,
        bool useSpatialContextAsWorldInput,
        IReadOnlyList<string> baseDiagnostics,
        out IReadOnlyList<Point3d> outputPoints,
        out SpatialCalibrationTransformResult frameResult,
        out string error)
    {
        outputPoints = Array.Empty<Point3d>();
        frameResult = EmptyRayPlaneResult(baseDiagnostics, calibrationSourceFrame, calibrationTargetFrame);
        error = string.Empty;

        if (!TryResolveRayPlaneWorldToPixelInputFrame(
                requestedInputFrame,
                spatialContext,
                useSpatialContextAsWorldInput,
                calibrationTargetFrame,
                out var inputFrame,
                out var inputDiagnostics,
                out error))
        {
            return false;
        }

        var diagnostics = baseDiagnostics.Concat(worldUnit.Diagnostics).Concat(inputDiagnostics).ToList();
        if (inputFrame.Kind != SpatialFrameKindV1.World2D)
        {
            error = $"SPATIAL_FRAME_DIRECTION_INVALID: WorldToPixel input frame must be World2D, got {inputFrame.Kind}.";
            return false;
        }

        if (!SpatialCalibrationTransformService.TryGetMillimetersPerUnit(inputFrame.Unit, out var inputMillimetersPerUnit, out var inputUnitSymbol, out error))
        {
            return false;
        }

        if (!SpatialCalibrationTransformService.TryResolveOutputFrame(
                requestedOutputFrame,
                spatialContext,
                calibrationSourceFrame,
                out var outputFrame,
                out var outputDiagnostics,
                out error))
        {
            return false;
        }

        diagnostics.AddRange(outputDiagnostics);
        if (!SpatialCalibrationTransformService.TryResolveSpatialPathForCalibration(
                spatialContext,
                calibrationSourceFrame,
                outputFrame,
                allowInverse: true,
                out var calibrationToOutput,
                out var spatialPath,
                out error))
        {
            return false;
        }

        var transformed = new List<Point3d>(inputPoints.Count);
        foreach (var point in inputPoints)
        {
            var hasExplicitZ = Math.Abs(point.Z) > Epsilon;
            var worldZmm = hasExplicitZ ? point.Z * inputMillimetersPerUnit : worldPlaneZ;
            var worldPointMm = new Point3d(point.X * inputMillimetersPerUnit, point.Y * inputMillimetersPerUnit, worldZmm);
            if (!TryWorldToPixelByProjection(context, distortion, worldPointMm, out var calibrationPixelPoint, out error))
            {
                error = $"Ray-plane WorldToPixel failed: {error}";
                return false;
            }

            if (!calibrationToOutput.TryApply(calibrationPixelPoint.X, calibrationPixelPoint.Y, out var outputX, out var outputY, out error))
            {
                error = $"Spatial output transform failed: {error}";
                return false;
            }

            var outputPoint = new Point3d(outputX, outputY, 0);
            if (!IsFinite(outputPoint))
            {
                error = "Ray-plane WorldToPixel produced non-finite output.";
                return false;
            }

            transformed.Add(outputPoint);
        }

        outputPoints = transformed;
        frameResult = new SpatialCalibrationTransformResult(
            transformed,
            inputFrame,
            calibrationSourceFrame,
            calibrationTargetFrame,
            outputFrame,
            inputUnitSymbol,
            outputFrame.UnitSymbol,
            spatialPath.Count,
            SpatialCalibrationTransformService.DescribeTransformChain(
                spatialPath,
                calibrationSourceFrame,
                calibrationTargetFrame,
                SpatialCalibrationTransformMode.WorldToPixel),
            diagnostics.Count > 0,
            diagnostics);
        return true;
    }

    private static bool TryResolveRayPlaneWorldToPixelInputFrame(
        string? requestedInputFrame,
        SpatialContextV1? spatialContext,
        bool useSpatialContextAsWorldInput,
        FrameRefV1 calibrationTargetFrame,
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
            if (!IsAutoFrameToken(requestedInputFrame))
            {
                if (!SpatialCalibrationTransformService.TryNormalizeRequestedFrame(
                        requestedInputFrame,
                        calibrationTargetFrame,
                        out var requested,
                        out var requestedDiagnostics,
                        out error))
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

        return SpatialCalibrationTransformService.TryNormalizeRequestedFrame(
            requestedInputFrame,
            calibrationTargetFrame,
            out inputFrame,
            out diagnostics,
            out error);
    }

    private static bool IsAutoFrameToken(string? value) =>
        string.IsNullOrWhiteSpace(value) || value.Trim().Equals("Auto", StringComparison.OrdinalIgnoreCase);

    private static SpatialCalibrationTransformResult EmptyRayPlaneResult(
        IReadOnlyList<string> diagnostics,
        FrameRefV1 calibrationSourceFrame,
        FrameRefV1 calibrationTargetFrame) =>
        new(
            Array.Empty<Point3d>(),
            calibrationSourceFrame,
            calibrationSourceFrame,
            calibrationTargetFrame,
            calibrationTargetFrame,
            calibrationSourceFrame.UnitSymbol,
            calibrationTargetFrame.UnitSymbol,
            0,
            Array.Empty<string>(),
            diagnostics.Count > 0,
            diagnostics);

    private OperatorExecutionOutput BuildSuccessOutput(
        Operator @operator,
        Dictionary<string, object>? inputs,
        IReadOnlyList<Point3d> inputPoints,
        IReadOnlyList<Point3d> outputPoints,
        string transformMode,
        string path,
        string model,
        CalibrationBundleV2 bundle,
        RuntimeCalibrationBundleAsset? runtimeCalibrationAsset,
        SpatialCalibrationTransformResult? frameResult,
        SpatialContextV1? inputSpatialContext,
        bool omitImageSpatialContextSidecar,
        double worldPlaneZ,
        double unitScale,
        bool generateReport,
        Dictionary<string, object>? accuracyReport,
        IReadOnlyList<string>? additionalDiagnostics)
    {
        var isPixelToWorld = transformMode.Equals("PixelToWorld", StringComparison.OrdinalIgnoreCase);
        object transformedPoints = isPixelToWorld
            ? outputPoints.Select(p => new Point3d(p.X, p.Y, p.Z)).ToList()
            : outputPoints.Select(p => new Position(p.X, p.Y)).ToList();
        var transformedPlanarPoints = outputPoints.Select(p => new Position(p.X, p.Y)).ToList();
        var outputDiagnostics = additionalDiagnostics?.ToList() ?? new List<string>();
        var hasRealInputImage = TryGetInputImage(inputs, "Image", out var imageWrapper) && imageWrapper != null;
        if (!hasRealInputImage)
        {
            outputDiagnostics.Add("SYNTHETIC_IMAGE_SPATIAL_CONTEXT_OMITTED: visualization image is synthetic preview output and has no business ImageFull SpatialContext.");
        }

        var transformResult = new Dictionary<string, object>
        {
            ["TransformMode"] = transformMode,
            ["Path"] = path,
            ["Model"] = model,
            ["InputCount"] = inputPoints.Count,
            ["OutputCount"] = outputPoints.Count,
            ["OutputPointDimension"] = isPixelToWorld ? 3 : 2,
            ["WorldPlaneZ"] = worldPlaneZ,
            ["UnitScale"] = unitScale,
            ["BundleId"] = bundle.BundleId,
            ["CalibrationBundleId"] = bundle.BundleId,
            ["CalibrationVersion"] = bundle.CalibrationVersion,
            ["DatasetFingerprint"] = bundle.DatasetFingerprint,
            ["ChecksumSha256"] = bundle.ChecksumSha256,
            ["CalibrationDataSource"] = runtimeCalibrationAsset == null ? "InlineCalibrationData" : "RuntimePackageAsset",
            ["CalibrationKind"] = bundle.CalibrationKind.ToString(),
            ["TransformModel"] = bundle.TransformModel.ToString(),
            ["SourceFrame"] = bundle.SourceFrame,
            ["TargetFrame"] = bundle.TargetFrame,
            ["InputFrame"] = frameResult?.InputFrame.FrameId ?? bundle.SourceFrame,
            ["CalibrationSourceFrame"] = frameResult?.CalibrationSourceFrame.FrameId ?? bundle.SourceFrame,
            ["CalibrationTargetFrame"] = frameResult?.CalibrationTargetFrame.FrameId ?? bundle.TargetFrame,
            ["OutputFrame"] = frameResult?.OutputFrame.FrameId ?? bundle.TargetFrame,
            ["InputUnit"] = frameResult?.InputUnit ?? (isPixelToWorld ? "px" : "mm"),
            ["OutputUnit"] = frameResult?.OutputUnit ?? (isPixelToWorld ? "mm" : "px"),
            ["AppliedSpatialTransformCount"] = frameResult?.AppliedSpatialTransformCount ?? 0,
            ["TransformChain"] = frameResult?.TransformChain.ToList() ?? new List<string>(),
            ["CompatibilityMode"] = frameResult?.CompatibilityMode ?? false,
            ["Diagnostics"] = outputDiagnostics
        };

        if (runtimeCalibrationAsset != null)
        {
            transformResult["CalibrationAssetId"] = runtimeCalibrationAsset.AssetId;
            transformResult["CalibrationAssetKind"] = runtimeCalibrationAsset.Kind;
            transformResult["CalibrationAssetVersion"] = runtimeCalibrationAsset.Version;
            transformResult["CalibrationAssetProjectRevision"] = runtimeCalibrationAsset.ProjectRevision;
            transformResult["CalibrationContentHash"] = runtimeCalibrationAsset.ContentHash;
            transformResult["CalibrationFileHash"] = runtimeCalibrationAsset.FileHash;
            transformResult["CalibrationAssetRelativePath"] = runtimeCalibrationAsset.RelativePath;
        }

        var resultData = new Dictionary<string, object>
        {
            ["TransformedPoints"] = transformedPoints,
            ["TransformedPlanarPoints"] = transformedPlanarPoints,
            ["TransformResult"] = transformResult,
            ["TransformedPointsSpatialContext"] = BuildTransformedPointsSpatialContext(@operator, frameResult, transformMode, unitScale)
        };

        if (hasRealInputImage && !omitImageSpatialContextSidecar)
        {
            resultData[RoiManagerOperator.SpatialContextOutputKey] = BuildImageSpatialContext(@operator, inputSpatialContext);
        }

        if (generateReport)
        {
            resultData["AccuracyReport"] = accuracyReport ?? CreateFallbackAccuracyReport(inputPoints, outputPoints, additionalDiagnostics);
        }

        Mat visualization;
        if (hasRealInputImage)
        {
            var image = imageWrapper!.GetMat();
            if (image.Empty())
            {
                return OperatorExecutionOutput.Failure("Input image is invalid.");
            }

            visualization = DrawVisualization(image, inputPoints, outputPoints, transformMode);
        }
        else
        {
            visualization = DrawVisualization(new Mat(480, 640, MatType.CV_8UC3, Scalar.Black), inputPoints, outputPoints, transformMode);
        }

        return OperatorExecutionOutput.Success(CreateImageOutput(visualization, resultData));
    }

    private static SpatialContextV1 BuildTransformedPointsSpatialContext(
        Operator @operator,
        SpatialCalibrationTransformResult? frameResult,
        string transformMode,
        double unitScale)
    {
        var outputFrame = frameResult?.OutputFrame ??
            (transformMode.Equals("PixelToWorld", StringComparison.OrdinalIgnoreCase)
                ? FrameRefV1.World2D()
                : FrameRefV1.ImageFull());

        return new SpatialContextV1(
            outputFrame,
            [SpatialTransform2DV1.Identity(outputFrame)],
            CreateSpatialBinding(@operator, "TransformedPoints"));
    }

    private static SpatialContextV1 BuildImageSpatialContext(
        Operator @operator,
        SpatialContextV1? inputSpatialContext)
    {
        var source = inputSpatialContext ?? SpatialContextV1.DefaultImageFull();
        return new SpatialContextV1(
            source.CurrentFrame,
            source.Transforms,
            CreateSpatialBinding(@operator, "Image"));
    }

    private static SpatialContextBindingV1 CreateSpatialBinding(Operator @operator, string outputName)
    {
        var outputPortId = @operator.OutputPorts
            .FirstOrDefault(port => port.Name.Equals(outputName, StringComparison.OrdinalIgnoreCase))
            ?.Id;
        return new SpatialContextBindingV1
        {
            SourceOperatorId = @operator.Id,
            OutputPortId = outputPortId,
            OutputName = outputName
        };
    }

    private static bool TryResolveInputSpatialContexts(
        IReadOnlyDictionary<string, object>? inputs,
        out SpatialContextV1? pointsContext,
        out SpatialContextV1? imageContext,
        out IReadOnlyList<string> diagnostics,
        out bool omitImageSpatialContextSidecar,
        out bool pointsContextProvided,
        out string error)
    {
        pointsContext = null;
        imageContext = null;
        diagnostics = Array.Empty<string>();
        omitImageSpatialContextSidecar = false;
        pointsContextProvided = false;
        error = string.Empty;
        if (inputs == null || inputs.Count == 0)
        {
            return true;
        }

        var diagnosticList = new List<string>();
        SpatialContextV1? legacyContext = null;

        if (TryGetInputValue(inputs, "PointsSpatialContext", out var rawPointsContext))
        {
            if (!SpatialCalibrationTransformService.TryReadSpatialContext(rawPointsContext, out var parsed, out var parseError))
            {
                error = $"Malformed SpatialContext input 'PointsSpatialContext': {parseError}";
                return false;
            }

            pointsContext = parsed;
            pointsContextProvided = true;
        }

        var imageContextMalformed = false;
        if (TryGetInputValue(inputs, RoiManagerOperator.ImageSpatialContextInputKey, out var rawImageContext))
        {
            if (SpatialCalibrationTransformService.TryReadSpatialContext(rawImageContext, out var parsed, out var parseError))
            {
                imageContext = parsed;
            }
            else
            {
                imageContextMalformed = true;
                omitImageSpatialContextSidecar = true;
                diagnosticList.Add($"IMAGE_SPATIAL_CONTEXT_MALFORMED: Image SpatialContext was ignored for coordinate math and Image sidecar was omitted: {parseError}");
            }
        }

        if (imageContextMalformed && pointsContext == null)
        {
            error = "Malformed SpatialContext input 'ImageSpatialContext': no valid PointsSpatialContext is available to own coordinate math.";
            return false;
        }

        if (TryGetInputValue(inputs, RoiManagerOperator.SpatialContextOutputKey, out var rawLegacyContext))
        {
            if (SpatialCalibrationTransformService.TryReadSpatialContext(rawLegacyContext, out var parsed, out var parseError))
            {
                legacyContext = parsed;
            }
            else if (pointsContext == null && imageContext == null)
            {
                error = $"Malformed SpatialContext input '{RoiManagerOperator.SpatialContextOutputKey}': {parseError}";
                return false;
            }
            else
            {
                diagnosticList.Add($"LEGACY_SPATIAL_CONTEXT_MALFORMED: scoped SpatialContext was used and legacy fallback was ignored: {parseError}");
            }
        }

        if (legacyContext != null)
        {
            if (pointsContext != null && !SpatialContextsEquivalent(pointsContext, legacyContext))
            {
                error = "SPATIAL_CONTEXT_SCOPE_CONFLICT: legacy SpatialContext conflicts with PointsSpatialContext.";
                return false;
            }

            if (imageContext != null && !SpatialContextsEquivalent(imageContext, legacyContext))
            {
                error = "SPATIAL_CONTEXT_SCOPE_CONFLICT: legacy SpatialContext conflicts with ImageSpatialContext.";
                return false;
            }

            pointsContext ??= legacyContext;
            imageContext ??= legacyContext;
        }

        if (pointsContext == null && imageContext != null)
        {
            pointsContext = imageContext;
            diagnosticList.Add("Compatibility SpatialContext fallback: coordinates used ImageSpatialContext because PointsSpatialContext was absent.");
        }

        diagnostics = diagnosticList;
        return true;
    }

    private static bool SpatialContextsEquivalent(SpatialContextV1 left, SpatialContextV1 right)
    {
        if (!Equals(left.CurrentFrame, right.CurrentFrame) ||
            !Equals(left.Binding, right.Binding) ||
            left.Transforms.Count != right.Transforms.Count)
        {
            return false;
        }

        for (var i = 0; i < left.Transforms.Count; i++)
        {
            var leftTransform = left.Transforms[i];
            var rightTransform = right.Transforms[i];
            if (!Equals(leftTransform.SourceFrame, rightTransform.SourceFrame) ||
                !Equals(leftTransform.TargetFrame, rightTransform.TargetFrame) ||
                !MatricesEqual(leftTransform.Matrix3x3, rightTransform.Matrix3x3))
            {
                return false;
            }
        }

        return true;
    }

    private static bool MatricesEqual(double[][] left, double[][] right)
    {
        if (left.Length != right.Length)
        {
            return false;
        }

        for (var row = 0; row < left.Length; row++)
        {
            if (left[row].Length != right[row].Length)
            {
                return false;
            }

            for (var column = 0; column < left[row].Length; column++)
            {
                if (Math.Abs(left[row][column] - right[row][column]) > Epsilon)
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static bool TryGetInputValue(
        IReadOnlyDictionary<string, object> inputs,
        string key,
        out object? value)
    {
        foreach (var pair in inputs)
        {
            if (pair.Key.Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                value = pair.Value;
                return true;
            }
        }

        value = null;
        return false;
    }

    private static bool TryBuildPlanarAccuracyReport(
        CalibrationPlanarTransformRuntime runtime,
        SpatialContextV1? spatialContext,
        IReadOnlyList<Point3d> inputPoints,
        IReadOnlyList<Point3d> outputPoints,
        bool isPixelToWorld,
        SpatialCalibrationTransformResult frameResult,
        IReadOnlyList<string>? diagnostics,
        out Dictionary<string, object> report,
        out string error)
    {
        report = new Dictionary<string, object>();
        error = string.Empty;

        if (inputPoints.Count != outputPoints.Count)
        {
            error = "SPATIAL_ROUND_TRIP_INVALID: output point count must match input point count.";
            return false;
        }

        var roundTripErrors = new List<double>(inputPoints.Count);
        if (isPixelToWorld)
        {
            if (!SpatialCalibrationTransformService.TryGetMillimetersPerUnit(
                    frameResult.OutputFrame.Unit,
                    out var outputMillimetersPerUnit,
                    out _,
                    out error))
            {
                return false;
            }

            if (!SpatialCalibrationTransformService.TryResolveSpatialPathForCalibration(
                    spatialContext,
                    frameResult.CalibrationSourceFrame,
                    frameResult.InputFrame,
                    allowInverse: true,
                    out var sourceToInput,
                    out _,
                    out error))
            {
                error = $"SPATIAL_ROUND_TRIP_INVALID: {error}";
                return false;
            }

            for (var i = 0; i < inputPoints.Count; i++)
            {
                var worldXmm = outputPoints[i].X * outputMillimetersPerUnit;
                var worldYmm = outputPoints[i].Y * outputMillimetersPerUnit;
                if (!runtime.TryApplyInverse(worldXmm, worldYmm, out var sourceX, out var sourceY, out error))
                {
                    error = $"SPATIAL_ROUND_TRIP_INVALID: Planar inverse transform failed: {error}";
                    return false;
                }

                if (!sourceToInput.TryApply(sourceX, sourceY, out var inputX, out var inputY, out error))
                {
                    error = $"SPATIAL_ROUND_TRIP_INVALID: Spatial reverse transform failed: {error}";
                    return false;
                }

                roundTripErrors.Add(MeasurementGeometryHelper.Distance(inputPoints[i].X, inputPoints[i].Y, inputX, inputY));
            }
        }
        else
        {
            if (!SpatialCalibrationTransformService.TryGetMillimetersPerUnit(
                    frameResult.InputFrame.Unit,
                    out var inputMillimetersPerUnit,
                    out _,
                    out error))
            {
                return false;
            }

            if (!SpatialCalibrationTransformService.TryResolveSpatialPathForCalibration(
                    spatialContext,
                    frameResult.OutputFrame,
                    frameResult.CalibrationSourceFrame,
                    allowInverse: true,
                    out var outputToSource,
                    out _,
                    out error))
            {
                error = $"SPATIAL_ROUND_TRIP_INVALID: {error}";
                return false;
            }

            for (var i = 0; i < inputPoints.Count; i++)
            {
                if (!outputToSource.TryApply(outputPoints[i].X, outputPoints[i].Y, out var sourceX, out var sourceY, out error))
                {
                    error = $"SPATIAL_ROUND_TRIP_INVALID: Spatial reverse transform failed: {error}";
                    return false;
                }

                if (!runtime.TryApplyForward(sourceX, sourceY, out var worldXmm, out var worldYmm, out error))
                {
                    error = $"SPATIAL_ROUND_TRIP_INVALID: Planar forward transform failed: {error}";
                    return false;
                }

                roundTripErrors.Add(MeasurementGeometryHelper.Distance(
                    inputPoints[i].X,
                    inputPoints[i].Y,
                    worldXmm / inputMillimetersPerUnit,
                    worldYmm / inputMillimetersPerUnit));
            }
        }

        report = BuildAccuracyReportPayload(
            inputPoints,
            outputPoints,
            roundTripErrors,
            frameResult.InputUnit,
            frameResult.InputFrame.FrameId,
            frameResult.AppliedSpatialTransformCount,
            diagnostics);
        return true;
    }

    private static bool TryBuildRayPlaneAccuracyReport(
        RayPlaneContext context,
        DistortionContext distortion,
        SpatialContextV1? spatialContext,
        IReadOnlyList<Point3d> inputPoints,
        IReadOnlyList<Point3d> outputPoints,
        bool isPixelToWorld,
        double worldPlaneZ,
        SpatialCalibrationTransformResult frameResult,
        IReadOnlyList<string>? diagnostics,
        out Dictionary<string, object> report,
        out string error)
    {
        report = new Dictionary<string, object>();
        error = string.Empty;

        if (inputPoints.Count != outputPoints.Count)
        {
            error = "SPATIAL_ROUND_TRIP_INVALID: output point count must match input point count.";
            return false;
        }

        var roundTripErrors = new List<double>(inputPoints.Count);
        if (isPixelToWorld)
        {
            if (!SpatialCalibrationTransformService.TryGetMillimetersPerUnit(
                    frameResult.OutputFrame.Unit,
                    out var outputMillimetersPerUnit,
                    out _,
                    out error))
            {
                return false;
            }

            if (!SpatialCalibrationTransformService.TryResolveSpatialPathForCalibration(
                    spatialContext,
                    frameResult.CalibrationSourceFrame,
                    frameResult.InputFrame,
                    allowInverse: true,
                    out var sourceToInput,
                    out _,
                    out error))
            {
                error = $"SPATIAL_ROUND_TRIP_INVALID: {error}";
                return false;
            }

            for (var i = 0; i < inputPoints.Count; i++)
            {
                var worldPointMm = new Point3d(
                    outputPoints[i].X * outputMillimetersPerUnit,
                    outputPoints[i].Y * outputMillimetersPerUnit,
                    outputPoints[i].Z * outputMillimetersPerUnit);
                if (!TryWorldToPixelByProjection(context, distortion, worldPointMm, out var sourcePoint, out error))
                {
                    error = $"SPATIAL_ROUND_TRIP_INVALID: Ray-plane inverse projection failed: {error}";
                    return false;
                }

                if (!sourceToInput.TryApply(sourcePoint.X, sourcePoint.Y, out var inputX, out var inputY, out error))
                {
                    error = $"SPATIAL_ROUND_TRIP_INVALID: Spatial reverse transform failed: {error}";
                    return false;
                }

                roundTripErrors.Add(MeasurementGeometryHelper.Distance(inputPoints[i].X, inputPoints[i].Y, inputX, inputY));
            }
        }
        else
        {
            if (!SpatialCalibrationTransformService.TryGetMillimetersPerUnit(
                    frameResult.InputFrame.Unit,
                    out var inputMillimetersPerUnit,
                    out _,
                    out error))
            {
                return false;
            }

            if (!SpatialCalibrationTransformService.TryResolveSpatialPathForCalibration(
                    spatialContext,
                    frameResult.OutputFrame,
                    frameResult.CalibrationSourceFrame,
                    allowInverse: true,
                    out var outputToSource,
                    out _,
                    out error))
            {
                error = $"SPATIAL_ROUND_TRIP_INVALID: {error}";
                return false;
            }

            for (var i = 0; i < inputPoints.Count; i++)
            {
                if (!outputToSource.TryApply(outputPoints[i].X, outputPoints[i].Y, out var sourceX, out var sourceY, out error))
                {
                    error = $"SPATIAL_ROUND_TRIP_INVALID: Spatial reverse transform failed: {error}";
                    return false;
                }

                var planeZmm = Math.Abs(inputPoints[i].Z) > Epsilon
                    ? inputPoints[i].Z * inputMillimetersPerUnit
                    : worldPlaneZ;
                if (!TryPixelToWorldByRayPlane(context, distortion, sourceX, sourceY, planeZmm, out var worldPointMm, out error))
                {
                    error = $"SPATIAL_ROUND_TRIP_INVALID: Ray-plane forward projection failed: {error}";
                    return false;
                }

                roundTripErrors.Add(MeasurementGeometryHelper.Distance(
                    inputPoints[i].X,
                    inputPoints[i].Y,
                    worldPointMm.X / inputMillimetersPerUnit,
                    worldPointMm.Y / inputMillimetersPerUnit));
            }
        }

        report = BuildAccuracyReportPayload(
            inputPoints,
            outputPoints,
            roundTripErrors,
            frameResult.InputUnit,
            frameResult.InputFrame.FrameId,
            frameResult.AppliedSpatialTransformCount,
            diagnostics);
        return true;
    }

    private static Dictionary<string, object> BuildAccuracyReportPayload(
        IReadOnlyList<Point3d> inputPoints,
        IReadOnlyList<Point3d> outputPoints,
        IReadOnlyList<double> roundTripErrors,
        string roundTripUnit,
        string roundTripFrame,
        int roundTripSpatialTransformCount,
        IReadOnlyList<string>? diagnostics)
    {
        var mean = roundTripErrors.Count > 0 ? roundTripErrors.Average() : 0.0;
        var max = roundTripErrors.Count > 0 ? roundTripErrors.Max() : 0.0;
        var rmse = roundTripErrors.Count > 0
            ? Math.Sqrt(roundTripErrors.Select(static error => error * error).Average())
            : 0.0;

        return new Dictionary<string, object>
        {
            ["InputPoints"] = inputPoints.Select(p => new { p.X, p.Y, p.Z }).ToList(),
            ["OutputPoints"] = outputPoints.Select(p => new { p.X, p.Y, p.Z }).ToList(),
            ["RoundTripErrors"] = roundTripErrors.ToList(),
            ["RoundTripFrame"] = roundTripFrame,
            ["RoundTripUnit"] = roundTripUnit,
            ["RoundTripSpatialTransformCount"] = roundTripSpatialTransformCount,
            ["RoundTripMean"] = mean,
            ["RoundTripMax"] = max,
            ["RoundTripP95"] = ComputePercentile(roundTripErrors, 0.95),
            ["RoundTripRmse"] = rmse,
            ["Diagnostics"] = diagnostics?.ToList() ?? new List<string>(),
            ["TimestampUtc"] = DateTime.UtcNow
        };
    }

    private static Dictionary<string, object> CreateFallbackAccuracyReport(
        IReadOnlyList<Point3d> inputPoints,
        IReadOnlyList<Point3d> outputPoints,
        IReadOnlyList<string>? diagnostics)
    {
        return new Dictionary<string, object>
        {
            ["InputPoints"] = inputPoints.Select(p => new { p.X, p.Y, p.Z }).ToList(),
            ["OutputPoints"] = outputPoints.Select(p => new { p.X, p.Y, p.Z }).ToList(),
            ["RoundTripErrors"] = new List<double>(),
            ["RoundTripFrame"] = string.Empty,
            ["RoundTripUnit"] = string.Empty,
            ["RoundTripSpatialTransformCount"] = 0,
            ["RoundTripMean"] = 0.0,
            ["RoundTripMax"] = 0.0,
            ["RoundTripP95"] = 0.0,
            ["RoundTripRmse"] = 0.0,
            ["Diagnostics"] = diagnostics?.ToList() ?? new List<string>(),
            ["TimestampUtc"] = DateTime.UtcNow
        };
    }

    private static double ComputePercentile(IReadOnlyList<double> values, double percentile)
    {
        if (values.Count == 0)
        {
            return 0.0;
        }

        var ordered = values.OrderBy(static value => value).ToArray();
        if (ordered.Length == 1)
        {
            return ordered[0];
        }

        var position = Math.Clamp(percentile, 0.0, 1.0) * (ordered.Length - 1);
        var lower = (int)Math.Floor(position);
        var upper = (int)Math.Ceiling(position);
        if (lower == upper)
        {
            return ordered[lower];
        }

        var ratio = position - lower;
        return ordered[lower] * (1.0 - ratio) + ordered[upper] * ratio;
    }

    private Mat DrawVisualization(
        Mat source,
        IReadOnlyList<Point3d> inputPoints,
        IReadOnlyList<Point3d> outputPoints,
        string transformMode)
    {
        var result = source.Clone();
        for (var i = 0; i < inputPoints.Count && i < outputPoints.Count; i++)
        {
            var x = (int)Math.Round(inputPoints[i].X);
            var y = (int)Math.Round(inputPoints[i].Y);
            Cv2.Circle(result, new Point(x, y), 4, new Scalar(0, 0, 255), -1);
            var label = transformMode.Equals("PixelToWorld", StringComparison.OrdinalIgnoreCase)
                ? $"W({outputPoints[i].X:F2},{outputPoints[i].Y:F2})"
                : $"P({outputPoints[i].X:F1},{outputPoints[i].Y:F1})";
            Cv2.PutText(
                result,
                label,
                new Point(x + 6, y - 6),
                HersheyFonts.HersheySimplex,
                0.4,
                new Scalar(0, 255, 0),
                1);
        }

        Cv2.PutText(
            result,
            $"Mode: {transformMode}",
            new Point(10, 25),
            HersheyFonts.HersheySimplex,
            0.6,
            new Scalar(255, 255, 0),
            2);
        return result;
    }

    private static bool IsSupportedPlanarKind(CalibrationKindV2 kind)
    {
        return kind == CalibrationKindV2.PlanarTransform2D || kind == CalibrationKindV2.RigidTransform2D;
    }

    private bool TryResolveCalibrationData(
        Operator @operator,
        Dictionary<string, object>? inputs,
        out string? calibrationData,
        out RuntimeCalibrationBundleAsset? runtimeCalibrationAsset,
        out string? error)
    {
        calibrationData = null;
        runtimeCalibrationAsset = null;
        error = null;
        if (inputs != null &&
            TryGetInputValue(inputs, "CalibrationData", out var dataObj) &&
            dataObj is string inlineData &&
            !string.IsNullOrWhiteSpace(inlineData))
        {
            calibrationData = inlineData;
            return true;
        }

        var inlineParameterData = GetStringParam(@operator, "CalibrationData", string.Empty);
        if (!string.IsNullOrWhiteSpace(inlineParameterData))
        {
            calibrationData = inlineParameterData;
            return true;
        }

        if (!TryGetRuntimeAssetContext(inputs, out var assetContext) || assetContext.IsEmpty)
        {
            error = "CalibrationBundleV2 data is required.";
            return false;
        }

        var requestedAssetId = ReadStringInputOrParameter(@operator, inputs, "CalibrationAssetId", "CalibrationAssetID");
        if (!string.IsNullOrWhiteSpace(requestedAssetId))
        {
            if (assetContext.TryGetCalibrationBundleByAssetId(requestedAssetId, out var asset))
            {
                return SelectRuntimeCalibrationAsset(asset, out calibrationData, out runtimeCalibrationAsset);
            }

            error = $"RUNTIME_CALIBRATION_BUNDLE_MISSING: Calibration asset '{requestedAssetId}' was not found in runtime package assets.";
            return false;
        }

        var requestedBundleId = ReadStringInputOrParameter(@operator, inputs, "CalibrationBundleId", "BundleId");
        if (!string.IsNullOrWhiteSpace(requestedBundleId))
        {
            if (assetContext.TryGetCalibrationBundleByBundleId(requestedBundleId, out var asset))
            {
                return SelectRuntimeCalibrationAsset(asset, out calibrationData, out runtimeCalibrationAsset);
            }

            error = $"RUNTIME_CALIBRATION_BUNDLE_MISSING: Calibration bundle '{requestedBundleId}' was not found in runtime package assets.";
            return false;
        }

        var candidates = assetContext.FindCalibrationBundlesByKind("CalibrationBundleV2");
        if (candidates.Count == 1)
        {
            return SelectRuntimeCalibrationAsset(candidates[0], out calibrationData, out runtimeCalibrationAsset);
        }

        if (candidates.Count == 0)
        {
            error = "RUNTIME_CALIBRATION_BUNDLE_MISSING: no CalibrationBundleV2 runtime package asset is available.";
            return false;
        }

        error = $"RUNTIME_CALIBRATION_BUNDLE_AMBIGUOUS: {candidates.Count} CalibrationBundleV2 runtime package assets are available; configure CalibrationAssetId or CalibrationBundleId.";
        return false;
    }

    private static bool SelectRuntimeCalibrationAsset(
        RuntimeCalibrationBundleAsset asset,
        out string? calibrationData,
        out RuntimeCalibrationBundleAsset? runtimeCalibrationAsset)
    {
        calibrationData = asset.PayloadJson;
        runtimeCalibrationAsset = asset;
        return !string.IsNullOrWhiteSpace(calibrationData);
    }

    private static bool TryGetRuntimeAssetContext(
        Dictionary<string, object>? inputs,
        out IRuntimeAssetContext assetContext)
    {
        assetContext = RuntimeAssetContext.Empty;
        if (inputs == null ||
            !TryGetInputValue(inputs, RuntimeAssetInputKeys.RuntimeAssetContext, out var rawContext) ||
            rawContext is not IRuntimeAssetContext typedContext)
        {
            return false;
        }

        assetContext = typedContext;
        return true;
    }

    private string ReadStringInputOrParameter(
        Operator @operator,
        Dictionary<string, object>? inputs,
        params string[] names)
    {
        if (inputs != null)
        {
            foreach (var name in names)
            {
                if (TryGetInputValue(inputs, name, out var value) &&
                    TryReadNonEmptyString(value, out var text))
                {
                    return text;
                }
            }
        }

        foreach (var name in names)
        {
            var value = GetStringParam(@operator, name, string.Empty);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return string.Empty;
    }

    private static bool TryReadNonEmptyString(object? value, out string text)
    {
        text = value switch
        {
            null => string.Empty,
            string raw => raw.Trim(),
            JsonElement { ValueKind: JsonValueKind.String } element => element.GetString()?.Trim() ?? string.Empty,
            JsonElement { ValueKind: JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False } element => element.ToString().Trim(),
            _ => value.ToString()?.Trim() ?? string.Empty
        };

        return !string.IsNullOrWhiteSpace(text);
    }

    private bool TryGetInputPoints(
        Operator @operator,
        Dictionary<string, object>? inputs,
        out List<Point3d> points,
        out string error)
    {
        points = new List<Point3d>();
        error = string.Empty;

        if (inputs != null && inputs.TryGetValue("Points", out var rawPoints) && rawPoints != null)
        {
            if (!TryAppendInputPoints(rawPoints, points, out error))
            {
                return false;
            }
        }

        if (points.Count > 0)
        {
            return true;
        }

        if (inputs != null && inputs.ContainsKey("Points"))
        {
            error = "Points input is provided but contains no valid points.";
            return false;
        }

        var x = GetDoubleParam(@operator, "InputPointX", 0.0);
        var y = GetDoubleParam(@operator, "InputPointY", 0.0);
        points.Add(new Point3d(x, y, 0));
        return true;
    }

    private static bool TryAppendInputPoints(
        object rawPoints,
        ICollection<Point3d> output,
        out string error)
    {
        error = string.Empty;

        if (rawPoints is IEnumerable<Position> positions)
        {
            foreach (var position in positions)
            {
                output.Add(new Point3d(position.X, position.Y, 0));
            }

            return true;
        }

        if (rawPoints is IEnumerable<Point2f> point2Fs)
        {
            foreach (var point in point2Fs)
            {
                output.Add(new Point3d(point.X, point.Y, 0));
            }

            return true;
        }

        if (rawPoints is IEnumerable<Point3f> point3Fs)
        {
            foreach (var point in point3Fs)
            {
                output.Add(new Point3d(point.X, point.Y, point.Z));
            }

            return true;
        }

        if (rawPoints is IEnumerable<Point3d> point3Ds)
        {
            foreach (var point in point3Ds)
            {
                output.Add(point);
            }

            return true;
        }

        if (rawPoints is string json && !string.IsNullOrWhiteSpace(json))
        {
            return TryAppendPointsFromJson(json, output, out error);
        }

        if (rawPoints is IEnumerable enumerable)
        {
            var index = 0;
            foreach (var item in enumerable)
            {
                if (!TryAppendPointObject(item, output, out error))
                {
                    error = $"Points[{index}]: {error}";
                    return false;
                }

                index++;
            }

            return true;
        }

        error = $"Unsupported Points input type: {rawPoints.GetType().Name}.";
        return false;
    }

    private static bool TryAppendPointObject(object? item, ICollection<Point3d> output, out string error)
    {
        error = string.Empty;
        switch (item)
        {
            case Position pos:
                output.Add(new Point3d(pos.X, pos.Y, 0));
                return true;
            case Point3d p3d:
                output.Add(p3d);
                return true;
            case Point2f p2f:
                output.Add(new Point3d(p2f.X, p2f.Y, 0));
                return true;
            case Point3f p3f:
                output.Add(new Point3d(p3f.X, p3f.Y, p3f.Z));
                return true;
            case JsonElement element:
                return TryAppendPointFromJsonElement(element, output, out error);
            case IDictionary<string, object> dictionary:
                return TryAppendPointFromDictionary(dictionary, output, out error);
            case null:
                error = "Point item is null.";
                return false;
        }

        if (item is IEnumerable numericTuple && item is not string)
        {
            var values = new List<double>();
            var index = 0;
            foreach (var coordinate in numericTuple)
            {
                try
                {
                    values.Add(Convert.ToDouble(coordinate, CultureInfo.InvariantCulture));
                }
                catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException)
                {
                    error = $"Point coordinate [{index}] must be numeric: {ex.Message}";
                    return false;
                }

                index++;
            }

            if (values.Count < 2)
            {
                error = "Point numeric sequence must contain at least X and Y.";
                return false;
            }

            return TryAppendPointFromScalars(
                values[0],
                values[1],
                values.Count >= 3 ? values[2] : null,
                output,
                out error);
        }

        var type = item.GetType();
        var x = type.GetProperty("X")?.GetValue(item);
        var y = type.GetProperty("Y")?.GetValue(item);
        var z = type.GetProperty("Z")?.GetValue(item);
        if (x == null || y == null)
        {
            error = $"Unsupported point item type '{type.Name}'.";
            return false;
        }

        return TryAppendPointFromScalars(x, y, z, output, out error);
    }

    private static bool TryAppendPointFromDictionary(
        IDictionary<string, object> dictionary,
        ICollection<Point3d> output,
        out string error)
    {
        error = string.Empty;
        var x = TryGetDictionaryValue(dictionary, "X", out var rawX) ? rawX : null;
        var y = TryGetDictionaryValue(dictionary, "Y", out var rawY) ? rawY : null;
        var z = TryGetDictionaryValue(dictionary, "Z", out var rawZ) ? rawZ : null;
        if (x == null || y == null)
        {
            error = "Point object must contain X and Y.";
            return false;
        }

        return TryAppendPointFromScalars(x, y, z, output, out error);
    }

    private static bool TryGetDictionaryValue(IDictionary<string, object> dictionary, string key, out object? value)
    {
        foreach (var pair in dictionary)
        {
            if (pair.Key.Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                value = pair.Value;
                return true;
            }
        }

        value = null;
        return false;
    }

    private static bool TryAppendPointFromScalars(
        object rawX,
        object rawY,
        object? rawZ,
        ICollection<Point3d> output,
        out string error)
    {
        error = string.Empty;
        try
        {
            if (!TryConvertCoordinate(rawX, out var x, out var xError))
            {
                error = $"X {xError}";
                return false;
            }

            if (!TryConvertCoordinate(rawY, out var y, out var yError))
            {
                error = $"Y {yError}";
                return false;
            }

            var z = 0.0;
            if (rawZ != null && !TryConvertCoordinate(rawZ, out z, out var zError))
            {
                error = $"Z {zError}";
                return false;
            }

            if (!double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(z))
            {
                error = "Point coordinates must be finite.";
                return false;
            }

            output.Add(new Point3d(x, y, z));
            return true;
        }
        catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException)
        {
            error = $"Point coordinates must be numeric: {ex.Message}";
            return false;
        }
    }

    private static bool TryConvertCoordinate(object raw, out double value, out string error)
    {
        value = 0.0;
        error = string.Empty;

        if (raw is JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Number)
            {
                value = element.GetDouble();
                return double.IsFinite(value) || Fail("must be finite.", out error);
            }

            if (element.ValueKind == JsonValueKind.String &&
                double.TryParse(element.GetString(), NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var parsed) &&
                double.IsFinite(parsed))
            {
                value = parsed;
                return true;
            }

            error = "must be a valid number.";
            return false;
        }

        try
        {
            value = Convert.ToDouble(raw, CultureInfo.InvariantCulture);
            return true;
        }
        catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException)
        {
            error = $"must be numeric: {ex.Message}";
            return false;
        }

        static bool Fail(string message, out string failure)
        {
            failure = message;
            return false;
        }
    }

    private static bool TryAppendPointFromJsonElement(
        JsonElement item,
        ICollection<Point3d> output,
        out string error)
    {
        error = string.Empty;
        if (item.ValueKind == JsonValueKind.Array)
        {
            var values = new List<double>();
            foreach (var coordinate in item.EnumerateArray())
            {
                if (coordinate.ValueKind != JsonValueKind.Number)
                {
                    error = "Point array coordinates must be numbers.";
                    return false;
                }

                values.Add(coordinate.GetDouble());
            }

            if (values.Count < 2)
            {
                error = "Point array must contain at least X and Y.";
                return false;
            }

            return TryAppendPointFromScalars(
                values[0],
                values[1],
                values.Count >= 3 ? values[2] : null,
                output,
                out error);
        }

        if (item.ValueKind != JsonValueKind.Object)
        {
            error = "Point item must be an object.";
            return false;
        }

        if (!TryReadNumberAny(item, ["X", "Item0"], required: true, out var x, out var xError))
        {
            error = $"X {xError}";
            return false;
        }

        if (!TryReadNumberAny(item, ["Y", "Item1"], required: true, out var y, out var yError))
        {
            error = $"Y {yError}";
            return false;
        }

        var z = 0.0;
        if (!TryReadNumberAny(item, ["Z", "Item2"], required: false, out z, out var zError))
        {
            error = $"Z {zError}";
            return false;
        }

        output.Add(new Point3d(x, y, z));
        return true;
    }

    private static bool TryAppendPointsFromJson(string json, ICollection<Point3d> output, out string error)
    {
        error = string.Empty;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                error = "Points JSON must be an array of point objects.";
                return false;
            }

            var index = 0;
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                {
                    error = $"Points[{index}] must be an object.";
                    return false;
                }

                if (!TryAppendPointFromJsonElement(item, output, out var itemError))
                {
                    error = $"Points[{index}]: {itemError}";
                    return false;
                }

                index++;
            }

            if (index == 0)
            {
                error = "Points JSON must contain at least one point.";
                return false;
            }

            return true;
        }
        catch (JsonException ex)
        {
            error = $"Invalid Points JSON: {ex.Message}";
            return false;
        }
    }

    private static bool TryReadNumber(
        JsonElement obj,
        string name,
        bool required,
        out double value,
        out string error)
    {
        value = 0;
        error = string.Empty;
        foreach (var property in obj.EnumerateObject())
        {
            if (!property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (property.Value.ValueKind == JsonValueKind.Number)
            {
                var parsed = property.Value.GetDouble();
                if (!double.IsFinite(parsed))
                {
                    error = "must be finite.";
                    return false;
                }

                value = parsed;
                return true;
            }

            if (property.Value.ValueKind == JsonValueKind.String &&
                double.TryParse(property.Value.GetString(), NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var fromString) &&
                double.IsFinite(fromString))
            {
                value = fromString;
                return true;
            }

            error = "must be a valid number.";
            return false;
        }

        if (required)
        {
            error = "is required.";
            return false;
        }

        return true;
    }

    private static bool TryReadNumberAny(
        JsonElement obj,
        IReadOnlyList<string> names,
        bool required,
        out double value,
        out string error)
    {
        foreach (var name in names)
        {
            if (TryReadNumber(obj, name, required: true, out value, out error))
            {
                return true;
            }

            if (!error.Contains("is required", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        value = 0;
        error = required
            ? $"is required; expected one of {string.Join("/", names)}."
            : string.Empty;
        return !required;
    }

    private static bool TryResolveRayPlaneCalibrationFrames(
        CalibrationBundleV2 bundle,
        out FrameRefV1 calibrationSourceFrame,
        out FrameRefV1 calibrationTargetFrame,
        out IReadOnlyList<string> diagnostics,
        out string error)
    {
        calibrationSourceFrame = FrameRefV1.ImageFull();
        calibrationTargetFrame = FrameRefV1.World2D();
        diagnostics = Array.Empty<string>();
        error = string.Empty;

        if (!TryMapRayPlaneFrame(bundle.SourceFrame, out var source))
        {
            error = $"Unsupported SourceFrame for ray-plane path: '{bundle.SourceFrame}'.";
            return false;
        }

        if (!TryMapRayPlaneFrame(bundle.TargetFrame, out var target))
        {
            error = $"Unsupported TargetFrame for ray-plane path: '{bundle.TargetFrame}'.";
            return false;
        }

        if (!((source == RayPlaneFrame.Camera && target == RayPlaneFrame.World) ||
              (source == RayPlaneFrame.World && target == RayPlaneFrame.Camera)))
        {
            error = $"Unsupported SourceFrame/TargetFrame combination for ray-plane path: '{bundle.SourceFrame}' -> '{bundle.TargetFrame}'.";
            return false;
        }

        var sourceToken = SpatialCalibrationTransformService.NormalizeFrameToken(bundle.SourceFrame);
        var targetToken = SpatialCalibrationTransformService.NormalizeFrameToken(bundle.TargetFrame);
        calibrationSourceFrame = sourceToken is "imageundistorted" or "undistorted" or "undistortedimage" ||
                                 targetToken is "imageundistorted" or "undistorted" or "undistortedimage"
            ? FrameRefV1.Undistorted()
            : FrameRefV1.ImageFull();
        calibrationTargetFrame = FrameRefV1.World2D();

        var result = new List<string>();
        var pixelFrameAlreadyExplicit =
            calibrationSourceFrame.Kind == SpatialFrameKindV1.Undistorted
                ? (bundle.SourceFrame?.Trim().Equals("image.undistorted", StringComparison.OrdinalIgnoreCase) == true ||
                   bundle.TargetFrame?.Trim().Equals("image.undistorted", StringComparison.OrdinalIgnoreCase) == true)
                : (bundle.SourceFrame?.Trim().Equals("image.full", StringComparison.OrdinalIgnoreCase) == true ||
                   bundle.TargetFrame?.Trim().Equals("image.full", StringComparison.OrdinalIgnoreCase) == true);
        if (!pixelFrameAlreadyExplicit)
        {
            result.Add($"Compatibility frame mapping: ray-plane camera/image frame '{SelectRayPlaneCameraFrameToken(bundle)}' -> '{calibrationSourceFrame.FrameId}' ({calibrationSourceFrame.Kind}, {calibrationSourceFrame.UnitSymbol}).");
        }

        var worldFrameAlreadyExplicit =
            bundle.SourceFrame?.Trim().Equals("world.2d", StringComparison.OrdinalIgnoreCase) == true ||
            bundle.TargetFrame?.Trim().Equals("world.2d", StringComparison.OrdinalIgnoreCase) == true;
        if (!worldFrameAlreadyExplicit)
        {
            result.Add($"Compatibility frame mapping: ray-plane world frame '{SelectRayPlaneWorldFrameToken(bundle)}' -> '{calibrationTargetFrame.FrameId}' ({calibrationTargetFrame.Kind}, {calibrationTargetFrame.UnitSymbol}).");
        }

        diagnostics = result;
        return true;
    }

    private static string SelectRayPlaneCameraFrameToken(CalibrationBundleV2 bundle)
    {
        return TryMapRayPlaneFrame(bundle.SourceFrame, out var source) && source == RayPlaneFrame.Camera
            ? bundle.SourceFrame ?? string.Empty
            : bundle.TargetFrame ?? string.Empty;
    }

    private static string SelectRayPlaneWorldFrameToken(CalibrationBundleV2 bundle)
    {
        return TryMapRayPlaneFrame(bundle.SourceFrame, out var source) && source == RayPlaneFrame.World
            ? bundle.SourceFrame ?? string.Empty
            : bundle.TargetFrame ?? string.Empty;
    }

    private static bool TryCreateRayPlaneContext(
        CalibrationBundleV2 bundle,
        out RayPlaneContext context,
        out string error)
    {
        context = default;
        error = string.Empty;

        if (bundle.Intrinsics == null || !CalibrationBundleV2Json.HasMatrix(bundle.Intrinsics.CameraMatrix, 3, 3))
        {
            error = "Ray-plane path requires Intrinsics.CameraMatrix (3x3).";
            return false;
        }

        if (bundle.Transform3D == null || !CalibrationBundleV2Json.HasMatrix(bundle.Transform3D.Matrix, 4, 4))
        {
            error = "Ray-plane path requires Transform3D.Matrix (4x4).";
            return false;
        }

        if (!CalibrationBundleV2Helpers.IsFiniteMatrix(bundle.Intrinsics.CameraMatrix) ||
            !CalibrationBundleV2Helpers.IsFiniteMatrix(bundle.Transform3D.Matrix))
        {
            error = "Calibration matrix data contains NaN or Infinity.";
            return false;
        }

        var k = bundle.Intrinsics.CameraMatrix;
        var fx = k[0][0];
        var fy = k[1][1];
        var cx = k[0][2];
        var cy = k[1][2];

        if (fx <= Epsilon || fy <= Epsilon)
        {
            error = "Camera matrix is invalid because fx/fy must be positive.";
            return false;
        }

        if (!TryMapRayPlaneFrame(bundle.SourceFrame, out var source))
        {
            error = $"Unsupported SourceFrame for ray-plane path: '{bundle.SourceFrame}'.";
            return false;
        }

        if (!TryMapRayPlaneFrame(bundle.TargetFrame, out var target))
        {
            error = $"Unsupported TargetFrame for ray-plane path: '{bundle.TargetFrame}'.";
            return false;
        }

        var rawTransform = bundle.Transform3D.Matrix;

        double[][] cameraToWorld;
        if (source == RayPlaneFrame.Camera && target == RayPlaneFrame.World)
        {
            cameraToWorld = CloneMatrix(rawTransform);
        }
        else if (source == RayPlaneFrame.World && target == RayPlaneFrame.Camera)
        {
            if (!TryInvert4x4(rawTransform, out cameraToWorld))
            {
                error = "Transform3D is singular and cannot be inverted.";
                return false;
            }
        }
        else
        {
            error = $"Unsupported SourceFrame/TargetFrame combination for ray-plane path: '{bundle.SourceFrame}' -> '{bundle.TargetFrame}'.";
            return false;
        }

        if (!TryInvert4x4(cameraToWorld, out var worldToCamera))
        {
            error = "Camera-to-world matrix is singular.";
            return false;
        }

        context = new RayPlaneContext(fx, fy, cx, cy, cameraToWorld, worldToCamera);
        return true;
    }

    private static bool TryPixelToWorldByRayPlane(
        RayPlaneContext context,
        DistortionContext distortion,
        double pixelX,
        double pixelY,
        double worldPlaneZ,
        out Point3d worldPoint,
        out string error)
    {
        worldPoint = default;
        error = string.Empty;

        if (!TryResolveNormalizedCameraPoint(context, distortion, pixelX, pixelY, out var normalized, out error))
        {
            return false;
        }

        var rayCamera = Normalize(new Point3d(normalized.X, normalized.Y, 1.0));

        var rayWorld = TransformDirection(context.CameraToWorld, rayCamera);
        if (Math.Abs(rayWorld.Z) <= Epsilon)
        {
            error = "Ray is parallel to the target world plane.";
            return false;
        }

        var cameraCenter = TransformPoint(context.CameraToWorld, new Point3d(0, 0, 0));
        var scale = (worldPlaneZ - cameraCenter.Z) / rayWorld.Z;
        if (!double.IsFinite(scale))
        {
            error = "Ray-plane intersection scale is not finite.";
            return false;
        }

        if (scale <= Epsilon)
        {
            error = "Ray-plane intersection is behind the camera or too close to be numerically stable.";
            return false;
        }

        worldPoint = new Point3d(
            cameraCenter.X + scale * rayWorld.X,
            cameraCenter.Y + scale * rayWorld.Y,
            worldPlaneZ);

        if (!IsFinite(worldPoint))
        {
            error = "Computed world point is not finite.";
            return false;
        }

        return true;
    }

    private static bool TryCreateDistortionContext(
        CalibrationBundleV2 bundle,
        bool useDistortion,
        out DistortionContext context,
        out string error)
    {
        context = DistortionContext.Disabled;
        error = string.Empty;

        if (!useDistortion)
        {
            return true;
        }

        var distortion = bundle.Distortion;
        if (distortion == null || distortion.Model == DistortionModelV2.None || distortion.Coefficients.Length == 0)
        {
            return true;
        }

        if (!CalibrationBundleV2Helpers.IsFiniteVector(distortion.Coefficients))
        {
            error = "Distortion coefficients contain NaN or Infinity.";
            return false;
        }

        switch (distortion.Model)
        {
            case DistortionModelV2.BrownConrady:
                if (!BrownConradyCoefficientLengths.Contains(distortion.Coefficients.Length))
                {
                    error = $"BrownConrady distortion in ray-plane path requires one of coefficient lengths: {string.Join(", ", BrownConradyCoefficientLengths.OrderBy(v => v))}.";
                    return false;
                }

                context = new DistortionContext(true, distortion.Model, distortion.Coefficients.ToArray());
                return true;
            case DistortionModelV2.KannalaBrandt:
                if (distortion.Coefficients.Length != 4)
                {
                    error = "KannalaBrandt distortion requires exactly 4 coefficients in this operator.";
                    return false;
                }

                context = new DistortionContext(true, distortion.Model, distortion.Coefficients.ToArray());
                return true;
            default:
                error = $"Unsupported distortion model in ray-plane path: {distortion.Model}.";
                return false;
        }
    }

    private static bool TryResolveNormalizedCameraPoint(
        RayPlaneContext context,
        DistortionContext distortion,
        double pixelX,
        double pixelY,
        out Point2d normalized,
        out string error)
    {
        normalized = default;
        error = string.Empty;

        if (!distortion.Enabled)
        {
            normalized = new Point2d(
                (pixelX - context.Cx) / context.Fx,
                (pixelY - context.Cy) / context.Fy);
            return true;
        }

        using var cameraMatrix = CreateCameraMatrix(context);
        using var distCoeffs = CreateDistortionVector(distortion.Coefficients);

        using var srcPoints = new Mat(1, 1, MatType.CV_64FC2);
        srcPoints.Set(0, 0, new Vec2d(pixelX, pixelY));

        using var undistortedPoints = new Mat();
        switch (distortion.Model)
        {
            case DistortionModelV2.BrownConrady:
                Cv2.UndistortPoints(srcPoints, undistortedPoints, cameraMatrix, distCoeffs);
                break;
            case DistortionModelV2.KannalaBrandt:
                Cv2.FishEye.UndistortPoints(srcPoints, undistortedPoints, cameraMatrix, distCoeffs, new Mat(), new Mat());
                break;
            default:
                error = $"Unsupported distortion model in ray-plane normalization: {distortion.Model}.";
                return false;
        }

        if (undistortedPoints.Empty())
        {
            error = "UndistortPoints returned an empty result.";
            return false;
        }

        var uv = undistortedPoints.At<Vec2d>(0, 0);
        if (!double.IsFinite(uv.Item0) || !double.IsFinite(uv.Item1))
        {
            error = "UndistortPoints produced non-finite normalized coordinates.";
            return false;
        }

        normalized = new Point2d(uv.Item0, uv.Item1);
        return true;
    }

    private static bool TryWorldToPixelByProjection(
        RayPlaneContext context,
        DistortionContext distortion,
        Point3d worldPoint,
        out Point3d pixelPoint,
        out string error)
    {
        pixelPoint = default;
        error = string.Empty;

        var cameraPoint = TransformPoint(context.WorldToCamera, worldPoint);
        if (Math.Abs(cameraPoint.Z) <= Epsilon)
        {
            error = "Point projects to infinity (camera Z is zero).";
            return false;
        }

        var x = cameraPoint.X / cameraPoint.Z;
        var y = cameraPoint.Y / cameraPoint.Z;
        double u;
        double v;

        if (!distortion.Enabled)
        {
            u = context.Fx * x + context.Cx;
            v = context.Fy * y + context.Cy;
        }
        else
        {
            if (!TryProjectWithDistortion(context, distortion, cameraPoint, out u, out v, out error))
            {
                return false;
            }
        }

        pixelPoint = new Point3d(u, v, 0);

        if (!IsFinite(pixelPoint))
        {
            error = "Projected pixel point is not finite.";
            return false;
        }

        return true;
    }

    private static bool TryProjectWithDistortion(
        RayPlaneContext context,
        DistortionContext distortion,
        Point3d cameraPoint,
        out double u,
        out double v,
        out string error)
    {
        u = 0;
        v = 0;
        error = string.Empty;

        using var cameraMatrix = CreateCameraMatrix(context);
        using var distCoeffs = CreateDistortionVector(distortion.Coefficients);
        using var objectPoints = new Mat(1, 1, MatType.CV_64FC3);
        objectPoints.Set(0, 0, new Vec3d(cameraPoint.X, cameraPoint.Y, cameraPoint.Z));
        using var zeroRvec = new Mat(3, 1, MatType.CV_64FC1, Scalar.All(0));
        using var zeroTvec = new Mat(3, 1, MatType.CV_64FC1, Scalar.All(0));
        using var imagePoints = new Mat();

        switch (distortion.Model)
        {
            case DistortionModelV2.BrownConrady:
                Cv2.ProjectPoints(objectPoints, zeroRvec, zeroTvec, cameraMatrix, distCoeffs, imagePoints, new Mat(), 0.0);
                break;
            case DistortionModelV2.KannalaBrandt:
                Cv2.FishEye.ProjectPoints(objectPoints, imagePoints, zeroRvec, zeroTvec, cameraMatrix, distCoeffs, 0.0, new Mat());
                break;
            default:
                error = $"Unsupported distortion model in projection path: {distortion.Model}.";
                return false;
        }

        if (imagePoints.Empty())
        {
            error = "Projection returned an empty result.";
            return false;
        }

        var uv = imagePoints.At<Vec2d>(0, 0);
        if (!double.IsFinite(uv.Item0) || !double.IsFinite(uv.Item1))
        {
            error = "Projection produced non-finite pixel coordinates.";
            return false;
        }

        u = uv.Item0;
        v = uv.Item1;
        return true;
    }

    private static Mat CreateCameraMatrix(RayPlaneContext context)
    {
        var cameraMatrix = new Mat(3, 3, MatType.CV_64FC1, Scalar.All(0));
        cameraMatrix.Set(0, 0, context.Fx);
        cameraMatrix.Set(1, 1, context.Fy);
        cameraMatrix.Set(0, 2, context.Cx);
        cameraMatrix.Set(1, 2, context.Cy);
        cameraMatrix.Set(2, 2, 1.0);
        return cameraMatrix;
    }

    private static Mat CreateDistortionVector(IReadOnlyList<double> coefficients)
    {
        var distCoeffs = new Mat(coefficients.Count, 1, MatType.CV_64FC1);
        for (var i = 0; i < coefficients.Count; i++)
        {
            distCoeffs.Set(i, 0, coefficients[i]);
        }

        return distCoeffs;
    }

    private static Point3d TransformPoint(double[][] matrix, Point3d point)
    {
        var x = matrix[0][0] * point.X + matrix[0][1] * point.Y + matrix[0][2] * point.Z + matrix[0][3];
        var y = matrix[1][0] * point.X + matrix[1][1] * point.Y + matrix[1][2] * point.Z + matrix[1][3];
        var z = matrix[2][0] * point.X + matrix[2][1] * point.Y + matrix[2][2] * point.Z + matrix[2][3];
        var w = matrix[3][0] * point.X + matrix[3][1] * point.Y + matrix[3][2] * point.Z + matrix[3][3];
        if (Math.Abs(w) <= Epsilon)
        {
            return new Point3d(double.NaN, double.NaN, double.NaN);
        }

        return new Point3d(x / w, y / w, z / w);
    }

    private static Point3d TransformDirection(double[][] matrix, Point3d direction)
    {
        var x = matrix[0][0] * direction.X + matrix[0][1] * direction.Y + matrix[0][2] * direction.Z;
        var y = matrix[1][0] * direction.X + matrix[1][1] * direction.Y + matrix[1][2] * direction.Z;
        var z = matrix[2][0] * direction.X + matrix[2][1] * direction.Y + matrix[2][2] * direction.Z;
        return Normalize(new Point3d(x, y, z));
    }

    private static Point3d Normalize(Point3d vector)
    {
        var norm = Math.Sqrt(vector.X * vector.X + vector.Y * vector.Y + vector.Z * vector.Z);
        if (norm <= Epsilon)
        {
            return new Point3d(double.NaN, double.NaN, double.NaN);
        }

        return new Point3d(vector.X / norm, vector.Y / norm, vector.Z / norm);
    }

    private static bool TryInvert4x4(double[][] matrix, out double[][] inverse)
    {
        inverse = Array.Empty<double[]>();
        try
        {
            using var mat = CalibrationBundleV2Helpers.ToMat(matrix);
            using var inv = new Mat();
            var invertResult = Cv2.Invert(mat, inv, DecompTypes.LU);
            if (Math.Abs(invertResult) <= Epsilon)
            {
                return false;
            }

            inverse = CalibrationBundleV2Helpers.ToJaggedMatrix(inv);
            return CalibrationBundleV2Json.HasMatrix(inverse, 4, 4) && CalibrationBundleV2Helpers.IsFiniteMatrix(inverse);
        }
        catch
        {
            return false;
        }
    }

    private static bool IsFinite(Point3d point)
    {
        return double.IsFinite(point.X) && double.IsFinite(point.Y) && double.IsFinite(point.Z);
    }

    private static double[][] CloneMatrix(double[][] source)
    {
        var clone = new double[source.Length][];
        for (var i = 0; i < source.Length; i++)
        {
            clone[i] = source[i].ToArray();
        }

        return clone;
    }

    private static bool TryMapRayPlaneFrame(string? frame, out RayPlaneFrame mapped)
    {
        mapped = default;
        var normalized = NormalizeFrameToken(frame);
        if (string.IsNullOrEmpty(normalized))
        {
            return false;
        }

        switch (normalized)
        {
            case "camera":
            case "cam":
            case "cameraframe":
            case "image":
            case "imagefull":
            case "imagepixel":
            case "imagepixels":
            case "fullimage":
            case "imageundistorted":
            case "undistorted":
            case "undistortedimage":
                mapped = RayPlaneFrame.Camera;
                return true;
            case "world":
            case "world2d":
            case "worldframe":
            case "base":
            case "robotbase":
                mapped = RayPlaneFrame.World;
                return true;
            default:
                return false;
        }
    }

    private static string NormalizeFrameToken(string? frame)
    {
        if (string.IsNullOrWhiteSpace(frame))
        {
            return string.Empty;
        }

        return new string(frame.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
    }

    private enum RayPlaneFrame
    {
        Camera = 0,
        World = 1
    }

    private readonly record struct RayPlaneContext(
        double Fx,
        double Fy,
        double Cx,
        double Cy,
        double[][] CameraToWorld,
        double[][] WorldToCamera);

    private readonly record struct DistortionContext(
        bool Enabled,
        DistortionModelV2 Model,
        double[] Coefficients)
    {
        public static DistortionContext Disabled => new(false, DistortionModelV2.None, Array.Empty<double>());
    }
}
