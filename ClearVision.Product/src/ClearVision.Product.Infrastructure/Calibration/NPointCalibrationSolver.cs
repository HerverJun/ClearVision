using ClearVision.Product.Core.ValueObjects;
using OpenCvSharp;

namespace ClearVision.Product.Infrastructure.Calibration;

public enum NPointCalibrationMode
{
    Affine,
    Perspective
}

public sealed record NPointCalibrationOptions(
    double RansacReprojectionThreshold,
    int RansacMaxIterations,
    double RansacConfidence,
    double MaxAcceptedReprojectionError,
    int MinInlierCount,
    double MinInlierRatio,
    string CalibrationUnit,
    string ProducerOperator,
    DateTime? GeneratedAtUtc = null);

public sealed record NPointCalibrationRequest(
    NPointCalibrationMode Mode,
    IReadOnlyList<NPointCalibrationPointPair> PointPairs,
    NPointCalibrationOptions Options);

public readonly record struct NPointCalibrationPointPair(Position ImagePoint, Position WorldPoint);

public readonly record struct NPointCalibrationErrorStats(
    double InlierMeanError,
    double InlierMaxError,
    double AllSampleMeanError,
    double AllSampleMaxError,
    int InlierCount,
    double InlierRatio)
{
    public double MeanError => InlierMeanError;

    public double MaxError => InlierMaxError;
}

public sealed class NPointCalibrationResult
{
    private NPointCalibrationResult()
    {
    }

    public bool Success { get; private init; }

    public string ErrorMessage { get; private init; } = string.Empty;

    public TransformModelV2 TransformModel { get; private init; } = TransformModelV2.None;

    public double[][] TransformMatrix { get; private init; } = Array.Empty<double[]>();

    public double? PixelSize { get; private init; }

    public double? PixelSizeX { get; private init; }

    public double? PixelSizeY { get; private init; }

    public NPointCalibrationErrorStats ErrorStats { get; private init; }

    public CalibrationBundleV2 Bundle { get; private init; } = new();

    public static NPointCalibrationResult Fail(string message) => new()
    {
        Success = false,
        ErrorMessage = message
    };

    public static NPointCalibrationResult Ok(
        TransformModelV2 transformModel,
        double[][] transformMatrix,
        double? pixelSize,
        double? pixelSizeX,
        double? pixelSizeY,
        NPointCalibrationErrorStats errorStats,
        CalibrationBundleV2 bundle) => new()
    {
        Success = true,
        TransformModel = transformModel,
        TransformMatrix = transformMatrix,
        PixelSize = pixelSize,
        PixelSizeX = pixelSizeX,
        PixelSizeY = pixelSizeY,
        ErrorStats = errorStats,
        Bundle = bundle
    };
}

public sealed class NPointCalibrationSolver
{
    public const double MinPointDistance = 1e-6;

    public NPointCalibrationResult Solve(NPointCalibrationRequest request)
    {
        var requiredCount = request.Mode == NPointCalibrationMode.Perspective ? 4 : 3;
        var pointPairs = request.PointPairs;
        if (pointPairs.Count < requiredCount)
        {
            return NPointCalibrationResult.Fail($"{request.Mode} mode requires at least {requiredCount} point pairs.");
        }

        if (!TryValidateOptions(request.Options, out var optionError))
        {
            return NPointCalibrationResult.Fail(optionError ?? "NPoint calibration options are invalid.");
        }

        var srcPoints = pointPairs.Select(pair => new Point2d(pair.ImagePoint.X, pair.ImagePoint.Y)).ToArray();
        var dstPoints = pointPairs.Select(pair => new Point2d(pair.WorldPoint.X, pair.WorldPoint.Y)).ToArray();

        if (!TryValidatePointSet(srcPoints, requiredCount, "ImagePoint", out var sourceValidationError))
        {
            return NPointCalibrationResult.Fail(sourceValidationError ?? "ImagePoint set is invalid.");
        }

        if (!TryValidatePointSet(dstPoints, requiredCount, "WorldPoint", out var targetValidationError))
        {
            return NPointCalibrationResult.Fail(targetValidationError ?? "WorldPoint set is invalid.");
        }

        return request.Mode == NPointCalibrationMode.Perspective
            ? ExecutePerspective(pointPairs, srcPoints, dstPoints, request.Options)
            : ExecuteAffine(pointPairs, srcPoints, dstPoints, request.Options);
    }

    public static bool TryValidatePointSet(
        IReadOnlyList<Point2d> points,
        int requiredCount,
        string pointName,
        out string? error)
    {
        error = null;
        if (points.Count < requiredCount)
        {
            error = $"{pointName} requires at least {requiredCount} points.";
            return false;
        }

        for (var i = 0; i < points.Count; i++)
        {
            if (!double.IsFinite(points[i].X) || !double.IsFinite(points[i].Y))
            {
                error = $"{pointName} contains non-finite values.";
                return false;
            }
        }

        for (var i = 0; i < points.Count; i++)
        {
            for (var j = i + 1; j < points.Count; j++)
            {
                var dx = points[i].X - points[j].X;
                var dy = points[i].Y - points[j].Y;
                if (dx * dx + dy * dy <= MinPointDistance * MinPointDistance)
                {
                    error = $"{pointName} contains duplicate or near-duplicate points.";
                    return false;
                }
            }
        }

        var maxTriangleArea = GetMaxTriangleArea(points);
        var minX = points.Min(point => point.X);
        var maxX = points.Max(point => point.X);
        var minY = points.Min(point => point.Y);
        var maxY = points.Max(point => point.Y);
        var scale = Math.Max(maxX - minX, maxY - minY);
        var minArea = Math.Max(1e-8, scale * scale * 1e-4);
        if (maxTriangleArea < minArea)
        {
            error = $"{pointName} is geometrically degenerate (nearly collinear).";
            return false;
        }

        return true;
    }

    private static NPointCalibrationResult ExecuteAffine(
        IReadOnlyList<NPointCalibrationPointPair> pointPairs,
        IReadOnlyList<Point2d> srcPoints,
        IReadOnlyList<Point2d> dstPoints,
        NPointCalibrationOptions options)
    {
        using var srcMat = InputArray.Create(srcPoints.ToArray());
        using var dstMat = InputArray.Create(dstPoints.ToArray());
        using var inlierMask = new Mat();
        using var affineMatrix = Cv2.EstimateAffine2D(
            srcMat,
            dstMat,
            inlierMask,
            RobustEstimationAlgorithms.RANSAC,
            options.RansacReprojectionThreshold,
            (ulong)options.RansacMaxIterations,
            options.RansacConfidence,
            20);

        if (affineMatrix is null || affineMatrix.Empty() || affineMatrix.Rows != 2 || affineMatrix.Cols != 3)
        {
            return NPointCalibrationResult.Fail("Failed to estimate a valid affine transform.");
        }

        var transform = ToMatrixArray(affineMatrix, 2, 3);
        if (!CalibrationBundleV2Helpers.IsFiniteMatrix(transform))
        {
            return NPointCalibrationResult.Fail("Estimated affine transform contains invalid values.");
        }

        if (!TryGetInlierFlags(inlierMask, pointPairs.Count, out var inlierFlags))
        {
            return NPointCalibrationResult.Fail("Failed to parse affine inlier mask.");
        }

        var errorStats = CalculateAffineReprojectionErrors(pointPairs, transform, inlierFlags);
        if (errorStats.InlierCount < 3)
        {
            return NPointCalibrationResult.Fail("Affine estimation failed because inliers are insufficient.");
        }

        var pixelSizeX = Math.Sqrt(transform[0][0] * transform[0][0] + transform[1][0] * transform[1][0]);
        var pixelSizeY = Math.Sqrt(transform[0][1] * transform[0][1] + transform[1][1] * transform[1][1]);
        double? pixelSize = null;
        if (pixelSizeX > 0 && pixelSizeY > 0)
        {
            var anisotropy = Math.Abs(pixelSizeX - pixelSizeY) / Math.Max(pixelSizeX, pixelSizeY);
            if (anisotropy <= 0.02)
            {
                pixelSize = (pixelSizeX + pixelSizeY) * 0.5;
            }
        }

        var accepted = IsAccepted(errorStats, options);
        var diagnostics = CreateCommonDiagnostics(
            "Affine transform estimated with all points via RANSAC.",
            errorStats,
            options,
            pointPairs.Count,
            accepted);
        var bundle = CreateBundle(
            TransformModelV2.Affine,
            transform,
            pixelSizeX,
            pixelSizeY,
            accepted,
            diagnostics,
            errorStats,
            pointPairs.Count,
            options);

        return NPointCalibrationResult.Ok(
            TransformModelV2.Affine,
            transform,
            pixelSize,
            pixelSizeX,
            pixelSizeY,
            errorStats,
            bundle);
    }

    private static NPointCalibrationResult ExecutePerspective(
        IReadOnlyList<NPointCalibrationPointPair> pointPairs,
        IReadOnlyList<Point2d> srcPoints,
        IReadOnlyList<Point2d> dstPoints,
        NPointCalibrationOptions options)
    {
        using var srcMat = InputArray.Create(srcPoints.ToArray());
        using var dstMat = InputArray.Create(dstPoints.ToArray());
        using var inlierMask = new Mat();
        using var homography = Cv2.FindHomography(
            srcMat,
            dstMat,
            HomographyMethods.Ransac,
            options.RansacReprojectionThreshold,
            inlierMask,
            options.RansacMaxIterations,
            options.RansacConfidence);

        if (homography is null || homography.Empty() || homography.Rows != 3 || homography.Cols != 3)
        {
            return NPointCalibrationResult.Fail("Failed to estimate a valid homography.");
        }

        var transform = ToMatrixArray(homography, 3, 3);
        if (!CalibrationBundleV2Helpers.IsFiniteMatrix(transform))
        {
            return NPointCalibrationResult.Fail("Estimated homography contains invalid values.");
        }

        var det = Cv2.Determinant(homography);
        if (!double.IsFinite(det) || Math.Abs(det) <= 1e-12)
        {
            return NPointCalibrationResult.Fail("Estimated homography is singular.");
        }

        if (!TryGetInlierFlags(inlierMask, pointPairs.Count, out var inlierFlags))
        {
            return NPointCalibrationResult.Fail("Failed to parse homography inlier mask.");
        }

        var errorStats = CalculateHomographyReprojectionErrors(pointPairs, transform, inlierFlags);
        if (errorStats.InlierCount < 4)
        {
            return NPointCalibrationResult.Fail("Homography estimation failed because inliers are insufficient.");
        }

        var accepted = IsAccepted(errorStats, options);
        var diagnostics = CreateCommonDiagnostics(
            "Perspective transform estimated with all points via FindHomography(RANSAC).",
            errorStats,
            options,
            pointPairs.Count,
            accepted);
        diagnostics.Add("PixelSize is intentionally not reported for homography model.");

        var bundle = CreateBundle(
            TransformModelV2.Homography,
            transform,
            pixelSizeX: null,
            pixelSizeY: null,
            accepted,
            diagnostics,
            errorStats,
            pointPairs.Count,
            options);

        return NPointCalibrationResult.Ok(
            TransformModelV2.Homography,
            transform,
            pixelSize: null,
            pixelSizeX: null,
            pixelSizeY: null,
            errorStats,
            bundle);
    }

    private static bool TryValidateOptions(NPointCalibrationOptions options, out string? error)
    {
        error = null;
        if (options.RansacReprojectionThreshold <= 0 || !double.IsFinite(options.RansacReprojectionThreshold))
        {
            error = "RansacReprojectionThreshold must be a positive finite number.";
            return false;
        }

        if (options.RansacMaxIterations < 1)
        {
            error = "RansacMaxIterations must be at least 1.";
            return false;
        }

        if (options.RansacConfidence <= 0 || options.RansacConfidence >= 1 || !double.IsFinite(options.RansacConfidence))
        {
            error = "RansacConfidence must be greater than 0 and less than 1.";
            return false;
        }

        if (options.MaxAcceptedReprojectionError < 0 || !double.IsFinite(options.MaxAcceptedReprojectionError))
        {
            error = "MaxAcceptedReprojectionError must be a non-negative finite number.";
            return false;
        }

        if (options.MinInlierRatio < 0 || options.MinInlierRatio > 1 || !double.IsFinite(options.MinInlierRatio))
        {
            error = "MinInlierRatio must be between 0 and 1.";
            return false;
        }

        if (options.MinInlierCount < 0)
        {
            error = "MinInlierCount must be non-negative.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(options.CalibrationUnit))
        {
            error = "CalibrationUnit must not be empty.";
            return false;
        }

        return true;
    }

    private static bool IsAccepted(NPointCalibrationErrorStats errorStats, NPointCalibrationOptions options)
    {
        return errorStats.InlierMaxError <= options.MaxAcceptedReprojectionError &&
               errorStats.InlierCount >= options.MinInlierCount &&
               errorStats.InlierRatio >= options.MinInlierRatio;
    }

    private static List<string> CreateCommonDiagnostics(
        string firstLine,
        NPointCalibrationErrorStats errorStats,
        NPointCalibrationOptions options,
        int sampleCount,
        bool accepted)
    {
        var diagnostics = new List<string>
        {
            firstLine,
            $"RansacReprojectionThreshold={options.RansacReprojectionThreshold:F6} {options.CalibrationUnit}",
            $"RansacMaxIterations={options.RansacMaxIterations}",
            $"RansacConfidence={options.RansacConfidence:F6}",
            $"MaxAcceptedReprojectionError={options.MaxAcceptedReprojectionError:F6} {options.CalibrationUnit}",
            $"MinInlierCount={options.MinInlierCount}",
            $"MinInlierRatio={options.MinInlierRatio:F3}",
            $"InlierMeanReprojectionError={errorStats.InlierMeanError:F6} {options.CalibrationUnit}",
            $"InlierMaxReprojectionError={errorStats.InlierMaxError:F6} {options.CalibrationUnit}",
            $"AllSampleMeanReprojectionError={errorStats.AllSampleMeanError:F6} {options.CalibrationUnit}",
            $"AllSampleMaxReprojectionError={errorStats.AllSampleMaxError:F6} {options.CalibrationUnit}",
            $"InlierRatio={errorStats.InlierRatio:F3}",
            $"InlierCount={errorStats.InlierCount}/{sampleCount}"
        };
        if (!accepted)
        {
            diagnostics.Add($"Acceptance failed: inlier max error {errorStats.InlierMaxError:F4} {options.CalibrationUnit}, inliers {errorStats.InlierCount}, ratio {errorStats.InlierRatio:F3}.");
        }

        return diagnostics;
    }

    private static CalibrationBundleV2 CreateBundle(
        TransformModelV2 model,
        double[][] transformMatrix,
        double? pixelSizeX,
        double? pixelSizeY,
        bool accepted,
        IReadOnlyList<string> diagnostics,
        NPointCalibrationErrorStats errorStats,
        int sampleCount,
        NPointCalibrationOptions options)
    {
        return new CalibrationBundleV2
        {
            SchemaVersion = 2,
            CalibrationKind = CalibrationKindV2.PlanarTransform2D,
            TransformModel = model,
            SourceFrame = "image",
            TargetFrame = "world",
            Unit = options.CalibrationUnit,
            Transform2D = new CalibrationTransform2DV2
            {
                Model = model,
                Matrix = transformMatrix,
                PixelSizeX = pixelSizeX,
                PixelSizeY = pixelSizeY
            },
            Quality = new CalibrationQualityV2
            {
                Accepted = accepted,
                MeanError = errorStats.MeanError,
                MaxError = errorStats.MaxError,
                InlierCount = errorStats.InlierCount,
                TotalSampleCount = sampleCount,
                Diagnostics = diagnostics.ToList()
            },
            GeneratedAtUtc = options.GeneratedAtUtc ?? DateTime.UtcNow,
            ProducerOperator = string.IsNullOrWhiteSpace(options.ProducerOperator)
                ? nameof(NPointCalibrationSolver)
                : options.ProducerOperator
        };
    }

    private static bool TryGetInlierFlags(Mat inlierMask, int pointCount, out bool[] inlierFlags)
    {
        inlierFlags = Enumerable.Repeat(true, pointCount).ToArray();
        if (inlierMask.Empty())
        {
            return true;
        }

        try
        {
            if (inlierMask.Rows == pointCount && inlierMask.Cols >= 1)
            {
                for (var i = 0; i < pointCount; i++)
                {
                    inlierFlags[i] = inlierMask.At<byte>(i, 0) != 0;
                }

                return true;
            }

            if (inlierMask.Cols == pointCount && inlierMask.Rows >= 1)
            {
                for (var i = 0; i < pointCount; i++)
                {
                    inlierFlags[i] = inlierMask.At<byte>(0, i) != 0;
                }

                return true;
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    private static NPointCalibrationErrorStats CalculateAffineReprojectionErrors(
        IReadOnlyList<NPointCalibrationPointPair> pairs,
        IReadOnlyList<double[]> matrix,
        IReadOnlyList<bool> inliers)
    {
        var allErrors = new List<double>(pairs.Count);
        var inlierErrors = new List<double>(pairs.Count);
        var inlierCount = 0;

        for (var i = 0; i < pairs.Count; i++)
        {
            var x = pairs[i].ImagePoint.X;
            var y = pairs[i].ImagePoint.Y;
            var px = matrix[0][0] * x + matrix[0][1] * y + matrix[0][2];
            var py = matrix[1][0] * x + matrix[1][1] * y + matrix[1][2];
            var dx = px - pairs[i].WorldPoint.X;
            var dy = py - pairs[i].WorldPoint.Y;
            var error = Math.Sqrt(dx * dx + dy * dy);
            allErrors.Add(error);

            if (inliers[i])
            {
                inlierCount++;
                inlierErrors.Add(error);
            }
        }

        return CreateReprojectionErrorStats(allErrors, inlierErrors, inlierCount, pairs.Count);
    }

    private static NPointCalibrationErrorStats CalculateHomographyReprojectionErrors(
        IReadOnlyList<NPointCalibrationPointPair> pairs,
        IReadOnlyList<double[]> matrix,
        IReadOnlyList<bool> inliers)
    {
        var allErrors = new List<double>(pairs.Count);
        var inlierErrors = new List<double>(pairs.Count);
        var inlierCount = 0;

        for (var i = 0; i < pairs.Count; i++)
        {
            var x = pairs[i].ImagePoint.X;
            var y = pairs[i].ImagePoint.Y;

            var w = matrix[2][0] * x + matrix[2][1] * y + matrix[2][2];
            if (Math.Abs(w) <= 1e-12)
            {
                var invalidError = double.MaxValue / 4;
                allErrors.Add(invalidError);
                if (inliers[i])
                {
                    inlierCount++;
                    inlierErrors.Add(invalidError);
                }

                continue;
            }

            var px = (matrix[0][0] * x + matrix[0][1] * y + matrix[0][2]) / w;
            var py = (matrix[1][0] * x + matrix[1][1] * y + matrix[1][2]) / w;
            var dx = px - pairs[i].WorldPoint.X;
            var dy = py - pairs[i].WorldPoint.Y;
            var error = Math.Sqrt(dx * dx + dy * dy);
            allErrors.Add(error);

            if (inliers[i])
            {
                inlierCount++;
                inlierErrors.Add(error);
            }
        }

        return CreateReprojectionErrorStats(allErrors, inlierErrors, inlierCount, pairs.Count);
    }

    private static NPointCalibrationErrorStats CreateReprojectionErrorStats(
        IReadOnlyList<double> allErrors,
        IReadOnlyList<double> inlierErrors,
        int inlierCount,
        int sampleCount)
    {
        var selected = inlierErrors.Count > 0 ? inlierErrors : allErrors;
        return new NPointCalibrationErrorStats(
            selected.Average(),
            selected.Max(),
            allErrors.Average(),
            allErrors.Max(),
            inlierCount,
            sampleCount == 0 ? 0 : inlierCount / (double)sampleCount);
    }

    private static double[][] ToMatrixArray(Mat matrix, int rows, int cols)
    {
        var result = new double[rows][];
        for (var row = 0; row < rows; row++)
        {
            result[row] = new double[cols];
            for (var col = 0; col < cols; col++)
            {
                result[row][col] = matrix.At<double>(row, col);
            }
        }

        return result;
    }

    private static double GetMaxTriangleArea(IReadOnlyList<Point2d> points)
    {
        var maxArea = 0.0;
        for (var i = 0; i < points.Count - 2; i++)
        {
            for (var j = i + 1; j < points.Count - 1; j++)
            {
                for (var k = j + 1; k < points.Count; k++)
                {
                    var area = Math.Abs(
                        points[i].X * (points[j].Y - points[k].Y) +
                        points[j].X * (points[k].Y - points[i].Y) +
                        points[k].X * (points[i].Y - points[j].Y)) * 0.5;
                    if (area > maxArea)
                    {
                        maxArea = area;
                    }
                }
            }
        }

        return maxArea;
    }
}
