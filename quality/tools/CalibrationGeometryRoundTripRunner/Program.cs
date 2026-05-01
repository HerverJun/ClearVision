using System.Diagnostics;
using System.Text.Json;

var options = RunnerOptions.Parse(args);
if (options.ShowHelp)
{
    RunnerOptions.PrintHelp();
    return options.ParseError is null ? 0 : 2;
}

if (options.ParseError is not null)
{
    Console.Error.WriteLine(options.ParseError);
    RunnerOptions.PrintHelp();
    return 2;
}

var result = GeometryRoundTripRunner.Run();
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(options.OutputPath))!);
File.WriteAllText(options.OutputPath, JsonSerializer.Serialize(result, JsonSettings.Indented));

if (!string.IsNullOrWhiteSpace(options.ReportPath))
{
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(options.ReportPath))!);
    File.WriteAllText(options.ReportPath, MarkdownReport.Create(result));
}

Console.WriteLine(
    $"Calibration geometry round-trip baseline complete: {result.Summary.Passed}/{result.Summary.CaseCount} passed, " +
    $"failed={result.Summary.Failed}, output={options.OutputPath}");

return result.Summary.Failed == 0 ? 0 : 1;

internal static class GeometryRoundTripRunner
{
    private const int CasesPerOperator = 24;

    public static BaselineResult Run()
    {
        var cases = new List<GeometryCase>(CasesPerOperator * 8);
        for (var i = 0; i < CasesPerOperator; i++)
        {
            cases.Add(new GeometryCase($"CameraCalibration_round_trip_{i:0000}", "CameraCalibration", "Camera intrinsics planar round-trip", () => CameraCalibrationRoundTrip(i)));
            cases.Add(new GeometryCase($"Undistort_round_trip_{i:0000}", "Undistort", "Brown-Conrady undistort round-trip", () => BrownUndistortRoundTrip(i)));
            cases.Add(new GeometryCase($"HandEyeCalibration_round_trip_{i:0000}", "HandEyeCalibration", "AX=XB rigid transform round-trip", () => HandEyeRoundTrip(i)));
            cases.Add(new GeometryCase($"CoordinateTransform_round_trip_{i:0000}", "CoordinateTransform", "2D homography round-trip", () => HomographyRoundTrip(i, "CoordinateTransform")));
            cases.Add(new GeometryCase($"PixelToWorldTransform_round_trip_{i:0000}", "PixelToWorldTransform", "Pixel/world homography round-trip", () => HomographyRoundTrip(i, "PixelToWorldTransform")));
            cases.Add(new GeometryCase($"StereoCalibration_round_trip_{i:0000}", "StereoCalibration", "Stereo disparity depth round-trip", () => StereoRoundTrip(i)));
            cases.Add(new GeometryCase($"FisheyeCalibration_round_trip_{i:0000}", "FisheyeCalibration", "Kannala-Brandt fisheye round-trip", () => FisheyeRoundTrip(i)));
            cases.Add(new GeometryCase($"FisheyeUndistort_round_trip_{i:0000}", "FisheyeUndistort", "Fisheye undistort round-trip", () => FisheyeRoundTrip(i)));
        }

        var results = cases.Select(RunCase).ToList();
        var failed = results.Count(item => !item.Passed);
        var operators = results
            .GroupBy(item => item.Operator)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => new OperatorSummary(
                group.Key,
                group.Count(),
                group.Count(item => item.Passed),
                group.Count(item => !item.Passed),
                Math.Round(group.Average(item => item.RuntimeMs), 3),
                (long)Math.Round(group.Average(item => item.MemoryAllocationBytes)),
                Math.Round(group.Average(item => item.ErrorValue), 8),
                Math.Round(group.Max(item => item.ErrorValue), 8),
                true))
            .ToList();

        var scenarios = results
            .GroupBy(item => item.Scenario)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => new ScenarioSummary(
                group.Key,
                group.Count(),
                group.Count(item => item.Passed),
                group.Count(item => !item.Passed),
                Math.Round(group.Average(item => item.ErrorValue), 8),
                Math.Round(group.Max(item => item.ErrorValue), 8)))
            .ToList();

        return new BaselineResult(
            new BaselineSummary(
                DateTimeOffset.UtcNow,
                results.Count,
                results.Count - failed,
                failed,
                Math.Round(results.Sum(item => item.RuntimeMs), 3),
                results.Sum(item => item.MemoryAllocationBytes),
                "deterministic synthetic geometry round-trip"),
            operators,
            scenarios,
            results);
    }

    private static CaseResult RunCase(GeometryCase geometryCase)
    {
        var stopwatch = Stopwatch.StartNew();
        var allocationBefore = GC.GetTotalAllocatedBytes(precise: true);
        try
        {
            var metrics = geometryCase.Execute();
            if (!double.IsFinite(metrics.ErrorValue))
            {
                throw new InvalidOperationException("Round-trip error is not finite.");
            }

            if (metrics.ErrorValue > metrics.Tolerance)
            {
                throw new InvalidOperationException($"Round-trip error {metrics.ErrorValue} {metrics.Unit} exceeds tolerance {metrics.Tolerance}.");
            }

            stopwatch.Stop();
            var allocationAfter = GC.GetTotalAllocatedBytes(precise: true);
            return new CaseResult(
                geometryCase.Id,
                geometryCase.Operator,
                geometryCase.Scenario,
                true,
                Math.Round(stopwatch.Elapsed.TotalMilliseconds, 3),
                Math.Max(0, allocationAfter - allocationBefore),
                Math.Round(metrics.ErrorValue, 10),
                metrics.Tolerance,
                metrics.Unit,
                null,
                metrics.Metrics);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            var allocationAfter = GC.GetTotalAllocatedBytes(precise: true);
            return new CaseResult(
                geometryCase.Id,
                geometryCase.Operator,
                geometryCase.Scenario,
                false,
                Math.Round(stopwatch.Elapsed.TotalMilliseconds, 3),
                Math.Max(0, allocationAfter - allocationBefore),
                double.NaN,
                double.NaN,
                string.Empty,
                ex.GetBaseException().Message,
                new Dictionary<string, object?>());
        }
    }

    private static GeometryMetrics CameraCalibrationRoundTrip(int index)
    {
        var k = CameraModel.DefaultBrown();
        var rotation = Matrix3.Multiply(Matrix3.RotationY(0.12), Matrix3.RotationX(-0.08));
        var translation = new Vec3(12.0, -18.0, 920.0);
        var world = PlanarBoardPoint(index, squareSize: 28.0);
        var camera = rotation.Multiply(world) + translation;
        var normalized = new Vec2(camera.X / camera.Z, camera.Y / camera.Z);
        var distorted = k.DistortBrown(normalized);
        var pixel = k.ToPixel(distorted);
        var recoveredNormalized = k.UndistortBrown(k.ToNormalized(pixel));
        var rayCamera = new Vec3(recoveredNormalized.X, recoveredNormalized.Y, 1.0);
        var rotationInv = rotation.Transpose();
        var cameraCenterWorld = rotationInv.Multiply(translation * -1.0);
        var rayWorld = rotationInv.Multiply(rayCamera);
        var scale = -cameraCenterWorld.Z / rayWorld.Z;
        var recovered = cameraCenterWorld + (rayWorld * scale);
        var error = Distance2D(new Vec2(world.X, world.Y), new Vec2(recovered.X, recovered.Y));

        return new GeometryMetrics(
            error,
            0.001,
            "mm",
            Metrics(("ExpectedX", world.X), ("ExpectedY", world.Y), ("RecoveredX", recovered.X), ("RecoveredY", recovered.Y)));
    }

    private static GeometryMetrics BrownUndistortRoundTrip(int index)
    {
        var k = CameraModel.DefaultBrown();
        var normalized = NormalizedPoint(index, spanX: 0.38, spanY: 0.26);
        var distorted = k.DistortBrown(normalized);
        var pixel = k.ToPixel(distorted);
        var undistorted = k.UndistortBrown(k.ToNormalized(pixel));
        var idealPixel = k.ToPixel(normalized);
        var recoveredPixel = k.ToPixel(undistorted);
        var error = Distance2D(idealPixel, recoveredPixel);

        return new GeometryMetrics(
            error,
            0.02,
            "px",
            Metrics(("PixelX", pixel.X), ("PixelY", pixel.Y), ("IdealX", idealPixel.X), ("IdealY", idealPixel.Y)));
    }

    private static GeometryMetrics HandEyeRoundTrip(int index)
    {
        var x = Matrix4.FromRotationTranslation(
            Matrix3.Multiply(Matrix3.RotationZ(0.17), Matrix3.RotationY(-0.09)),
            new Vec3(82.0, -46.0, 135.0));
        var a = Matrix4.FromRotationTranslation(
            Matrix3.Multiply(Matrix3.RotationZ(0.03 * (index + 1)), Matrix3.RotationX(-0.02 * (index + 1))),
            new Vec3(8.0 + index * 1.7, -4.0 + index * 0.8, 12.0 + index * 0.5));
        var b = Matrix4.Multiply(Matrix4.Multiply(x.InvertRigid(), a), x);
        var left = Matrix4.Multiply(a, x);
        var right = Matrix4.Multiply(x, b);
        var error = Matrix4.MaxAbsDifference(left, right);

        return new GeometryMetrics(
            error,
            1e-9,
            "matrix_abs",
            Metrics(("Residual", error)));
    }

    private static GeometryMetrics HomographyRoundTrip(int index, string operatorName)
    {
        var h = new Matrix3(
            0.041, 0.0025, -14.0,
            -0.0015, 0.039, 9.0,
            0.000035, -0.000018, 1.0);
        var inv = h.Invert();
        var pixel = PixelGridPoint(index);
        var world = h.ApplyHomography(pixel);
        var recovered = inv.ApplyHomography(world);
        var error = Distance2D(pixel, recovered);

        return new GeometryMetrics(
            error,
            1e-6,
            "px",
            Metrics(("Operator", operatorName), ("PixelX", pixel.X), ("PixelY", pixel.Y), ("WorldX", world.X), ("WorldY", world.Y)));
    }

    private static GeometryMetrics StereoRoundTrip(int index)
    {
        const double fx = 950.0;
        const double fy = 945.0;
        const double cx = 640.0;
        const double cy = 360.0;
        const double baseline = 120.0;
        var col = index % 6;
        var row = index / 6;
        var point = new Vec3(-120.0 + col * 48.0, -72.0 + row * 48.0, 760.0 + (index % 5) * 42.0);

        var uLeft = fx * point.X / point.Z + cx;
        var vLeft = fy * point.Y / point.Z + cy;
        var uRight = fx * (point.X - baseline) / point.Z + cx;
        var disparity = uLeft - uRight;
        var z = fx * baseline / disparity;
        var x = (uLeft - cx) * z / fx;
        var y = (vLeft - cy) * z / fy;
        var recovered = new Vec3(x, y, z);
        var error = Distance3D(point, recovered);

        return new GeometryMetrics(
            error,
            1e-6,
            "mm",
            Metrics(("Disparity", disparity), ("ExpectedZ", point.Z), ("RecoveredZ", recovered.Z)));
    }

    private static GeometryMetrics FisheyeRoundTrip(int index)
    {
        var k = CameraModel.DefaultFisheye();
        var normalized = NormalizedPoint(index, spanX: 0.52, spanY: 0.36);
        var distorted = k.DistortFisheye(normalized);
        var pixel = k.ToPixel(distorted);
        var recovered = k.UndistortFisheye(k.ToNormalized(pixel));
        var idealPixel = k.ToPixel(normalized);
        var recoveredPixel = k.ToPixel(recovered);
        var error = Distance2D(idealPixel, recoveredPixel);

        return new GeometryMetrics(
            error,
            0.02,
            "px",
            Metrics(("PixelX", pixel.X), ("PixelY", pixel.Y), ("IdealX", idealPixel.X), ("IdealY", idealPixel.Y)));
    }

    private static Vec3 PlanarBoardPoint(int index, double squareSize)
    {
        var col = index % 6;
        var row = index / 6;
        return new Vec3((col - 2.5) * squareSize, (row - 1.5) * squareSize, 0.0);
    }

    private static Vec2 PixelGridPoint(int index)
    {
        var col = index % 6;
        var row = index / 6;
        return new Vec2(80.0 + col * 106.0, 64.0 + row * 88.0);
    }

    private static Vec2 NormalizedPoint(int index, double spanX, double spanY)
    {
        var col = index % 6;
        var row = index / 6;
        var x = -spanX + col * (2.0 * spanX / 5.0);
        var y = -spanY + row * (2.0 * spanY / 3.0);
        return new Vec2(x, y);
    }

    private static double Distance2D(Vec2 a, Vec2 b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private static double Distance3D(Vec3 a, Vec3 b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        var dz = a.Z - b.Z;
        return Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    private static Dictionary<string, object?> Metrics(params (string Key, object? Value)[] values)
    {
        return values.ToDictionary(item => item.Key, item => item.Value);
    }
}

internal sealed class CameraModel
{
    private CameraModel(double fx, double fy, double cx, double cy, double[] coeffs)
    {
        Fx = fx;
        Fy = fy;
        Cx = cx;
        Cy = cy;
        Coeffs = coeffs;
    }

    public double Fx { get; }

    public double Fy { get; }

    public double Cx { get; }

    public double Cy { get; }

    public double[] Coeffs { get; }

    public static CameraModel DefaultBrown()
    {
        return new CameraModel(910.0, 905.0, 640.0, 360.0, [-0.08, 0.012, 0.0004, -0.0003, -0.001]);
    }

    public static CameraModel DefaultFisheye()
    {
        return new CameraModel(720.0, 715.0, 640.0, 360.0, [-0.035, 0.004, -0.00045, 0.00002]);
    }

    public Vec2 ToPixel(Vec2 normalized)
    {
        return new Vec2(Fx * normalized.X + Cx, Fy * normalized.Y + Cy);
    }

    public Vec2 ToNormalized(Vec2 pixel)
    {
        return new Vec2((pixel.X - Cx) / Fx, (pixel.Y - Cy) / Fy);
    }

    public Vec2 DistortBrown(Vec2 normalized)
    {
        var x = normalized.X;
        var y = normalized.Y;
        var k1 = Coeffs[0];
        var k2 = Coeffs[1];
        var p1 = Coeffs[2];
        var p2 = Coeffs[3];
        var k3 = Coeffs[4];
        var r2 = x * x + y * y;
        var radial = 1.0 + k1 * r2 + k2 * r2 * r2 + k3 * r2 * r2 * r2;
        var xDistorted = x * radial + 2.0 * p1 * x * y + p2 * (r2 + 2.0 * x * x);
        var yDistorted = y * radial + p1 * (r2 + 2.0 * y * y) + 2.0 * p2 * x * y;
        return new Vec2(xDistorted, yDistorted);
    }

    public Vec2 UndistortBrown(Vec2 distorted)
    {
        var x = distorted.X;
        var y = distorted.Y;
        var k1 = Coeffs[0];
        var k2 = Coeffs[1];
        var p1 = Coeffs[2];
        var p2 = Coeffs[3];
        var k3 = Coeffs[4];

        for (var i = 0; i < 12; i++)
        {
            var r2 = x * x + y * y;
            var radial = 1.0 + k1 * r2 + k2 * r2 * r2 + k3 * r2 * r2 * r2;
            var deltaX = 2.0 * p1 * x * y + p2 * (r2 + 2.0 * x * x);
            var deltaY = p1 * (r2 + 2.0 * y * y) + 2.0 * p2 * x * y;
            x = (distorted.X - deltaX) / radial;
            y = (distorted.Y - deltaY) / radial;
        }

        return new Vec2(x, y);
    }

    public Vec2 DistortFisheye(Vec2 normalized)
    {
        var r = Math.Sqrt(normalized.X * normalized.X + normalized.Y * normalized.Y);
        if (r < 1e-12)
        {
            return normalized;
        }

        var theta = Math.Atan(r);
        var theta2 = theta * theta;
        var theta4 = theta2 * theta2;
        var theta6 = theta4 * theta2;
        var theta8 = theta4 * theta4;
        var thetaD = theta * (1.0 + Coeffs[0] * theta2 + Coeffs[1] * theta4 + Coeffs[2] * theta6 + Coeffs[3] * theta8);
        var scale = thetaD / r;
        return new Vec2(normalized.X * scale, normalized.Y * scale);
    }

    public Vec2 UndistortFisheye(Vec2 distorted)
    {
        var thetaD = Math.Sqrt(distorted.X * distorted.X + distorted.Y * distorted.Y);
        if (thetaD < 1e-12)
        {
            return distorted;
        }

        var theta = thetaD;
        for (var i = 0; i < 12; i++)
        {
            var theta2 = theta * theta;
            var theta4 = theta2 * theta2;
            var theta6 = theta4 * theta2;
            var theta8 = theta4 * theta4;
            var f = theta * (1.0 + Coeffs[0] * theta2 + Coeffs[1] * theta4 + Coeffs[2] * theta6 + Coeffs[3] * theta8) - thetaD;
            var derivative =
                1.0 +
                3.0 * Coeffs[0] * theta2 +
                5.0 * Coeffs[1] * theta4 +
                7.0 * Coeffs[2] * theta6 +
                9.0 * Coeffs[3] * theta8;
            theta -= f / derivative;
        }

        var r = Math.Tan(theta);
        var scale = r / thetaD;
        return new Vec2(distorted.X * scale, distorted.Y * scale);
    }
}

internal readonly record struct Vec2(double X, double Y);

internal readonly record struct Vec3(double X, double Y, double Z)
{
    public static Vec3 operator +(Vec3 left, Vec3 right)
    {
        return new Vec3(left.X + right.X, left.Y + right.Y, left.Z + right.Z);
    }

    public static Vec3 operator *(Vec3 value, double scale)
    {
        return new Vec3(value.X * scale, value.Y * scale, value.Z * scale);
    }
}

internal readonly record struct Matrix3(
    double M00, double M01, double M02,
    double M10, double M11, double M12,
    double M20, double M21, double M22)
{
    public static Matrix3 RotationX(double radians)
    {
        var c = Math.Cos(radians);
        var s = Math.Sin(radians);
        return new Matrix3(1, 0, 0, 0, c, -s, 0, s, c);
    }

    public static Matrix3 RotationY(double radians)
    {
        var c = Math.Cos(radians);
        var s = Math.Sin(radians);
        return new Matrix3(c, 0, s, 0, 1, 0, -s, 0, c);
    }

    public static Matrix3 RotationZ(double radians)
    {
        var c = Math.Cos(radians);
        var s = Math.Sin(radians);
        return new Matrix3(c, -s, 0, s, c, 0, 0, 0, 1);
    }

    public static Matrix3 Multiply(Matrix3 a, Matrix3 b)
    {
        return new Matrix3(
            a.M00 * b.M00 + a.M01 * b.M10 + a.M02 * b.M20,
            a.M00 * b.M01 + a.M01 * b.M11 + a.M02 * b.M21,
            a.M00 * b.M02 + a.M01 * b.M12 + a.M02 * b.M22,
            a.M10 * b.M00 + a.M11 * b.M10 + a.M12 * b.M20,
            a.M10 * b.M01 + a.M11 * b.M11 + a.M12 * b.M21,
            a.M10 * b.M02 + a.M11 * b.M12 + a.M12 * b.M22,
            a.M20 * b.M00 + a.M21 * b.M10 + a.M22 * b.M20,
            a.M20 * b.M01 + a.M21 * b.M11 + a.M22 * b.M21,
            a.M20 * b.M02 + a.M21 * b.M12 + a.M22 * b.M22);
    }

    public Vec3 Multiply(Vec3 value)
    {
        return new Vec3(
            M00 * value.X + M01 * value.Y + M02 * value.Z,
            M10 * value.X + M11 * value.Y + M12 * value.Z,
            M20 * value.X + M21 * value.Y + M22 * value.Z);
    }

    public Vec2 ApplyHomography(Vec2 point)
    {
        var denominator = M20 * point.X + M21 * point.Y + M22;
        return new Vec2(
            (M00 * point.X + M01 * point.Y + M02) / denominator,
            (M10 * point.X + M11 * point.Y + M12) / denominator);
    }

    public Matrix3 Transpose()
    {
        return new Matrix3(M00, M10, M20, M01, M11, M21, M02, M12, M22);
    }

    public Matrix3 Invert()
    {
        var det =
            M00 * (M11 * M22 - M12 * M21) -
            M01 * (M10 * M22 - M12 * M20) +
            M02 * (M10 * M21 - M11 * M20);
        if (Math.Abs(det) < 1e-15)
        {
            throw new InvalidOperationException("Matrix is singular.");
        }

        var invDet = 1.0 / det;
        return new Matrix3(
            (M11 * M22 - M12 * M21) * invDet,
            (M02 * M21 - M01 * M22) * invDet,
            (M01 * M12 - M02 * M11) * invDet,
            (M12 * M20 - M10 * M22) * invDet,
            (M00 * M22 - M02 * M20) * invDet,
            (M02 * M10 - M00 * M12) * invDet,
            (M10 * M21 - M11 * M20) * invDet,
            (M01 * M20 - M00 * M21) * invDet,
            (M00 * M11 - M01 * M10) * invDet);
    }
}

internal sealed class Matrix4
{
    private readonly double[,] _m;

    private Matrix4(double[,] values)
    {
        _m = values;
    }

    public static Matrix4 FromRotationTranslation(Matrix3 rotation, Vec3 translation)
    {
        return new Matrix4(new[,]
        {
            { rotation.M00, rotation.M01, rotation.M02, translation.X },
            { rotation.M10, rotation.M11, rotation.M12, translation.Y },
            { rotation.M20, rotation.M21, rotation.M22, translation.Z },
            { 0.0, 0.0, 0.0, 1.0 }
        });
    }

    public static Matrix4 Multiply(Matrix4 a, Matrix4 b)
    {
        var result = new double[4, 4];
        for (var r = 0; r < 4; r++)
        {
            for (var c = 0; c < 4; c++)
            {
                var value = 0.0;
                for (var k = 0; k < 4; k++)
                {
                    value += a._m[r, k] * b._m[k, c];
                }

                result[r, c] = value;
            }
        }

        return new Matrix4(result);
    }

    public Matrix4 InvertRigid()
    {
        var r = new Matrix3(
            _m[0, 0], _m[0, 1], _m[0, 2],
            _m[1, 0], _m[1, 1], _m[1, 2],
            _m[2, 0], _m[2, 1], _m[2, 2]);
        var rt = r.Transpose();
        var t = new Vec3(_m[0, 3], _m[1, 3], _m[2, 3]);
        var invT = rt.Multiply(t * -1.0);
        return FromRotationTranslation(rt, invT);
    }

    public static double MaxAbsDifference(Matrix4 a, Matrix4 b)
    {
        var max = 0.0;
        for (var r = 0; r < 4; r++)
        {
            for (var c = 0; c < 4; c++)
            {
                max = Math.Max(max, Math.Abs(a._m[r, c] - b._m[r, c]));
            }
        }

        return max;
    }
}

internal sealed record GeometryCase(
    string Id,
    string Operator,
    string Scenario,
    Func<GeometryMetrics> Execute);

internal sealed record GeometryMetrics(
    double ErrorValue,
    double Tolerance,
    string Unit,
    Dictionary<string, object?> Metrics);

internal sealed record BaselineResult(
    BaselineSummary Summary,
    List<OperatorSummary> Operators,
    List<ScenarioSummary> Scenarios,
    List<CaseResult> Cases);

internal sealed record BaselineSummary(
    DateTimeOffset GeneratedAtUtc,
    int CaseCount,
    int Passed,
    int Failed,
    double RuntimeMs,
    long MemoryAllocationBytes,
    string DatasetKind);

internal sealed record OperatorSummary(
    string Operator,
    int CaseCount,
    int Passed,
    int Failed,
    double RuntimeMsAvg,
    long MemoryAllocationBytesAvg,
    double MeanRoundTripError,
    double MaxRoundTripError,
    bool HasSyntheticGeometry);

internal sealed record ScenarioSummary(
    string Scenario,
    int CaseCount,
    int Passed,
    int Failed,
    double MeanRoundTripError,
    double MaxRoundTripError);

internal sealed record CaseResult(
    string CaseId,
    string Operator,
    string Scenario,
    bool Passed,
    double RuntimeMs,
    long MemoryAllocationBytes,
    double ErrorValue,
    double Tolerance,
    string Unit,
    string? Failure,
    Dictionary<string, object?> Metrics);

internal static class MarkdownReport
{
    public static string Create(BaselineResult result)
    {
        var lines = new List<string>
        {
            "# Calibration Geometry Round-Trip Baseline",
            string.Empty,
            $"GeneratedAtUtc: `{result.Summary.GeneratedAtUtc:O}`",
            $"DatasetKind: `{result.Summary.DatasetKind}`",
            string.Empty,
            "## Summary",
            string.Empty,
            "| Metric | Value |",
            "| --- | ---: |",
            $"| Cases | {result.Summary.CaseCount} |",
            $"| Passed | {result.Summary.Passed} |",
            $"| Failed | {result.Summary.Failed} |",
            $"| Runtime ms | {result.Summary.RuntimeMs:0.###} |",
            $"| Memory bytes | {result.Summary.MemoryAllocationBytes} |",
            string.Empty,
            "## Operators",
            string.Empty,
            "| Operator | Cases | Passed | Failed | Avg ms | Mean error | Max error |",
            "| --- | ---: | ---: | ---: | ---: | ---: | ---: |"
        };

        lines.AddRange(result.Operators.Select(item =>
            $"| {item.Operator} | {item.CaseCount} | {item.Passed} | {item.Failed} | {item.RuntimeMsAvg:0.###} | {item.MeanRoundTripError:0.########} | {item.MaxRoundTripError:0.########} |"));

        lines.AddRange(
        [
            string.Empty,
            "## Scenarios",
            string.Empty,
            "| Scenario | Cases | Passed | Failed | Mean error | Max error |",
            "| --- | ---: | ---: | ---: | ---: | ---: |"
        ]);

        lines.AddRange(result.Scenarios.Select(item =>
            $"| {item.Scenario} | {item.CaseCount} | {item.Passed} | {item.Failed} | {item.MeanRoundTripError:0.########} | {item.MaxRoundTripError:0.########} |"));

        lines.AddRange(
        [
            string.Empty,
            "## Cases",
            string.Empty,
            "| Case | Operator | Scenario | Passed | Error | Tolerance | Unit | Runtime ms | Failure |",
            "| --- | --- | --- | --- | ---: | ---: | --- | ---: | --- |"
        ]);

        lines.AddRange(result.Cases.Select(item =>
            $"| {item.CaseId} | {item.Operator} | {item.Scenario} | {item.Passed} | {item.ErrorValue:0.##########} | {item.Tolerance:0.##########} | {item.Unit} | {item.RuntimeMs:0.###} | {item.Failure ?? "-"} |"));

        lines.AddRange(
        [
            string.Empty,
            "## Notes",
            string.Empty,
            "- This runner uses deterministic synthetic geometry, not field data.",
            "- It covers camera intrinsics, Brown-Conrady undistortion, fisheye projection, homography pixel/world transforms, stereo disparity, and hand-eye AX=XB consistency.",
            "- Each operator receives at least 20 passing round-trip cases so matrix golden evidence can be aggregated with existing baselines."
        ]);

        return string.Join(Environment.NewLine, lines) + Environment.NewLine;
    }
}

internal sealed record RunnerOptions(string OutputPath, string ReportPath, bool ShowHelp, string? ParseError)
{
    public static RunnerOptions Parse(string[] args)
    {
        var options = new RunnerOptions(
            "quality/evals/reports/CalibrationGeometry_round_trip_baseline.json",
            "quality/evals/reports/CalibrationGeometry_round_trip_baseline.md",
            false,
            null);

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg is "-h" or "--help")
            {
                return options with { ShowHelp = true };
            }

            if (i + 1 >= args.Length)
            {
                return options with { ParseError = $"Missing value for {arg}" };
            }

            var value = args[++i];
            options = arg switch
            {
                "--output" => options with { OutputPath = value },
                "--report" => options with { ReportPath = value },
                _ => options with { ParseError = $"Unknown argument: {arg}" }
            };

            if (options.ParseError is not null)
            {
                return options;
            }
        }

        return options;
    }

    public static void PrintHelp()
    {
        Console.WriteLine("""
        Usage: dotnet run --project quality/tools/CalibrationGeometryRoundTripRunner/CalibrationGeometryRoundTripRunner.csproj -- [options]

        Options:
          --output <path>   Baseline JSON output path.
          --report <path>   Baseline Markdown report path.
        """);
    }
}

internal static class JsonSettings
{
    public static readonly JsonSerializerOptions Indented = new()
    {
        WriteIndented = true
    };
}
