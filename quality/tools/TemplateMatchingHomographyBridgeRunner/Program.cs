using System.Diagnostics;
using System.Text.Json;
using Acme.Product.Core.Entities;
using Acme.Product.Core.Enums;
using Acme.Product.Core.Operators;
using Acme.Product.Core.ValueObjects;
using Acme.Product.Infrastructure.Operators;
using Microsoft.Extensions.Logging.Abstractions;
using OpenCvSharp;

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

var result = await HomographyBridgeRunner.RunAsync(options);
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(options.OutputPath))!);
await File.WriteAllTextAsync(options.OutputPath, JsonSerializer.Serialize(result, JsonSettings.Indented));

if (!string.IsNullOrWhiteSpace(options.ReportPath))
{
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(options.ReportPath))!);
    await File.WriteAllTextAsync(options.ReportPath, MarkdownReport.Create(result));
}

Console.WriteLine(
    $"TemplateMatching homography bridge complete: {result.Summary.Passed}/{result.Summary.CaseCount} passed, " +
    $"failed={result.Summary.Failed}, candidate={result.Summary.CandidateVersion}, profile={result.Summary.Profile}, output={options.OutputPath}");

return result.Summary.Failed == 0 ? 0 : 1;

internal static class HomographyBridgeRunner
{
    private const int SceneWidth = 320;
    private const int SceneHeight = 240;
    private const int PatchWidth = 40;
    private const int PatchHeight = 32;
    private const double PositionTolerancePx = 1.5;

    public static async Task<BridgeResult> RunAsync(RunnerOptions options)
    {
        var specs = BuildCases(options)
            .Where(options.IncludesCase)
            .ToList();
        var results = new List<BridgeCaseResult>(specs.Count);
        foreach (var spec in specs)
        {
            results.Add(await RunCaseAsync(spec));
        }

        var passed = results.Count(item => item.Passed);
        var failed = results.Count - passed;
        var errors = results.Select(item => item.PositionErrorPx).Where(double.IsFinite).OrderBy(x => x).ToArray();
        var summary = new BridgeSummary(
            DateTimeOffset.UtcNow,
            "HPatches-style synthetic homography bridge",
            "In-repo public-protocol proxy for planar homography and illumination evidence",
            results.Count,
            passed,
            failed,
            PositionTolerancePx,
            Math.Round(errors.Length == 0 ? 0 : errors.Average(), 4),
            Math.Round(Percentile(errors, 0.95), 4),
            Math.Round(results.Sum(item => item.RuntimeMs), 3),
            options.CandidateVersion,
            options.Profile);

        var operators = new[]
        {
            new OperatorEvidence(
                "TemplateMatching",
                results.Count,
                passed,
                failed,
                Math.Round(results.Average(item => item.RuntimeMs), 3),
                Math.Round(results.Max(item => item.RuntimeMs), 3),
                Convert.ToInt64(Math.Round(results.Average(item => item.MemoryAllocationBytes))),
                true,
                summary.DatasetName)
        };

        return new BridgeResult(summary, operators, results);
    }

    private static async Task<BridgeCaseResult> RunCaseAsync(BridgeCaseSpec spec)
    {
        using var baseScene = CreateBaseScene();
        using var warpedScene = ApplyHomography(baseScene, spec);
        using var bridgeScene = ApplyPhotometric(warpedScene, spec);
        using var template = CreateTemplate(baseScene, bridgeScene, spec);
        using var sceneForSearch = spec.EnablePoseSearch
            ? CreatePoseSearchScene(bridgeScene, template, spec)
            : bridgeScene.Clone();

        ImageWrapper? sceneWrapper = null;
        ImageWrapper? templateWrapper = null;
        OperatorExecutionOutput? execution = null;
        var stopwatch = Stopwatch.StartNew();
        var allocationBefore = GC.GetTotalAllocatedBytes(precise: true);

        try
        {
            sceneWrapper = new ImageWrapper(sceneForSearch.Clone());
            templateWrapper = new ImageWrapper(template.Clone());
            var executor = new TemplateMatchOperator(NullLogger<TemplateMatchOperator>.Instance);
            var op = CreateOperator(spec);
            execution = await executor.ExecuteAsync(op, new Dictionary<string, object>
            {
                ["Image"] = sceneWrapper,
                ["Template"] = templateWrapper
            });
        }
        finally
        {
            stopwatch.Stop();
        }

        var allocationAfter = GC.GetTotalAllocatedBytes(precise: true);
        var actual = ReadPosition(execution);
        var positionError = Distance(actual, spec.ExpectedCenter);
        var score = ReadDouble(execution, "Score");
        var normalizedScore = ReadDouble(execution, "NormalizedScore");
        var subpixelOffsetX = ReadDouble(execution, "SubpixelOffsetX");
        var subpixelOffsetY = ReadDouble(execution, "SubpixelOffsetY");
        var peakCurvature = ReadDouble(execution, "PeakCurvature");
        var actualAngle = ReadDouble(execution, "Angle");
        var actualScale = ReadDouble(execution, "Scale");
        var pyramidLevels = ReadInt(execution, "PyramidLevels");
        var angleError = spec.EnablePoseSearch ? Math.Abs(NormalizeAngle(actualAngle - spec.ExpectedAngleDeg)) : 0.0;
        var scaleError = spec.EnablePoseSearch ? Math.Abs(actualScale - spec.ExpectedScale) : 0.0;
        var isMatch = execution?.IsSuccess == true &&
            execution.OutputData?.TryGetValue("IsMatch", out var isMatchObj) == true &&
            isMatchObj is bool b &&
            b;
        var passed = execution?.IsSuccess == true &&
            isMatch &&
            double.IsFinite(positionError) &&
            positionError <= PositionTolerancePx &&
            normalizedScore >= 0.75 &&
            normalizedScore <= 1.000001 &&
            (!spec.EnablePoseSearch || (angleError <= spec.PoseSearchAngleStep + 0.1 && scaleError <= spec.PoseSearchScaleStep + 0.011));

        ReleaseImageOutputs(execution?.OutputData);
        sceneWrapper?.Dispose();
        templateWrapper?.Dispose();

        return new BridgeCaseResult(
            spec.CaseId,
            "TemplateMatching",
            spec.Sequence,
            spec.TemplateSource,
            passed,
            Math.Round(stopwatch.Elapsed.TotalMilliseconds, 3),
            Math.Max(0, allocationAfter - allocationBefore),
            Math.Round(spec.ExpectedCenter.X, 3),
            Math.Round(spec.ExpectedCenter.Y, 3),
            Math.Round(actual.X, 3),
            Math.Round(actual.Y, 3),
            Math.Round(positionError, 4),
            Math.Round(score, 6),
            Math.Round(normalizedScore, 6),
            Math.Round(subpixelOffsetX, 6),
            Math.Round(subpixelOffsetY, 6),
            Math.Round(peakCurvature, 6),
            Math.Round(spec.ExpectedAngleDeg, 6),
            Math.Round(actualAngle, 6),
            Math.Round(angleError, 6),
            Math.Round(spec.ExpectedScale, 6),
            Math.Round(actualScale, 6),
            Math.Round(scaleError, 6),
            pyramidLevels,
            passed ? null : execution?.ErrorMessage ?? "Position/score contract failed");
    }

    private static IEnumerable<BridgeCaseSpec> BuildCases(RunnerOptions options)
    {
        var anchors = new[]
        {
            new Point2d(70, 58),
            new Point2d(132, 72),
            new Point2d(214, 62),
            new Point2d(82, 148),
            new Point2d(166, 152),
            new Point2d(238, 164)
        };

        var sequences = new[]
        {
            new SequenceSpec("illumination_translation", Translation(18, 12), "Source", 1.18, 14),
            new SequenceSpec("viewpoint_translation", Translation(-16, 19), "WarpedScene", 1.0, 0),
            new SequenceSpec("homography_shear", HomographyFromCorners(
                new Point2f(0, 0), new Point2f(SceneWidth - 1, 0), new Point2f(SceneWidth - 1, SceneHeight - 1), new Point2f(0, SceneHeight - 1),
                new Point2f(18, 8), new Point2f(SceneWidth - 24, 14), new Point2f(SceneWidth - 9, SceneHeight - 18), new Point2f(28, SceneHeight - 7)), "WarpedScene", 1.0, 0),
            new SequenceSpec("homography_perspective", HomographyFromCorners(
                new Point2f(0, 0), new Point2f(SceneWidth - 1, 0), new Point2f(SceneWidth - 1, SceneHeight - 1), new Point2f(0, SceneHeight - 1),
                new Point2f(10, 18), new Point2f(SceneWidth - 34, 4), new Point2f(SceneWidth - 19, SceneHeight - 24), new Point2f(34, SceneHeight - 16)), "WarpedScene", 0.92, 8)
        };

        foreach (var sequence in sequences)
        {
            for (var i = 0; i < anchors.Length; i++)
            {
                var anchor = anchors[i];
                var projected = Transform(anchor, sequence.Homography);
                var topLeft = new Point2d(
                    Math.Clamp(Math.Round(projected.X - PatchWidth / 2.0), 0, SceneWidth - PatchWidth),
                    Math.Clamp(Math.Round(projected.Y - PatchHeight / 2.0), 0, SceneHeight - PatchHeight));
                var center = new Point2d(topLeft.X + PatchWidth / 2.0, topLeft.Y + PatchHeight / 2.0);
                yield return new BridgeCaseSpec(
                    $"TemplateMatching_{sequence.Name}_{i:0000}",
                    sequence.Name,
                    sequence.TemplateSource,
                    sequence.Homography,
                    sequence.Alpha,
                    sequence.Beta,
                    anchor,
                    topLeft,
                    center,
                    PatchWidth,
                    PatchHeight);
            }
        }

        if (!options.IncludesPoseSearch)
        {
            yield break;
        }

        foreach (var spec in BuildPoseSearchCases())
        {
            yield return spec;
        }
    }

    private static IEnumerable<BridgeCaseSpec> BuildPoseSearchCases()
    {
        var specs = new[]
        {
            new PoseReplaySpec("pose_small_rotation", new Point2d(70, 58), new Point2d(118, 38), -5.0, 1.00, -5.0, 10.0, 1.0, 1.00, 1.00, 0.05),
            new PoseReplaySpec("pose_small_rotation", new Point2d(132, 72), new Point2d(190, 44), 5.0, 1.00, -5.0, 10.0, 1.0, 1.00, 1.00, 0.05),
            new PoseReplaySpec("pose_medium_rotation", new Point2d(214, 62), new Point2d(72, 104), -12.0, 1.00, -15.0, 30.0, 1.0, 1.00, 1.00, 0.05),
            new PoseReplaySpec("pose_medium_rotation", new Point2d(82, 148), new Point2d(186, 108), 14.0, 1.00, -15.0, 30.0, 1.0, 1.00, 1.00, 0.05),
            new PoseReplaySpec("pose_scale", new Point2d(166, 152), new Point2d(58, 166), 0.0, 0.90, 0.0, 0.0, 1.0, 0.90, 1.10, 0.05),
            new PoseReplaySpec("pose_scale", new Point2d(238, 164), new Point2d(166, 166), 0.0, 1.10, 0.0, 0.0, 1.0, 0.90, 1.10, 0.05),
            new PoseReplaySpec("pose_rotation_scale", new Point2d(132, 72), new Point2d(250, 70), 8.0, 0.95, -15.0, 30.0, 1.0, 0.90, 1.10, 0.05),
            new PoseReplaySpec("pose_rotation_scale", new Point2d(166, 152), new Point2d(244, 150), -10.0, 1.05, -15.0, 30.0, 1.0, 0.90, 1.10, 0.05)
        };

        var counters = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var pose in specs)
        {
            counters.TryGetValue(pose.Sequence, out var index);
            counters[pose.Sequence] = index + 1;
            var size = TransformedSize(new Size(PatchWidth, PatchHeight), pose.AngleDeg, pose.Scale);
            var targetTopLeft = new Point2d(
                Math.Clamp(Math.Round(pose.TargetTopLeft.X), 0, SceneWidth - size.Width),
                Math.Clamp(Math.Round(pose.TargetTopLeft.Y), 0, SceneHeight - size.Height));
            yield return new BridgeCaseSpec(
                $"TemplateMatching_{pose.Sequence}_{index:0000}",
                pose.Sequence,
                "Source",
                Translation(0, 0),
                1.0,
                0.0,
                pose.SourceAnchor,
                targetTopLeft,
                new Point2d(targetTopLeft.X + (size.Width / 2.0), targetTopLeft.Y + (size.Height / 2.0)),
                size.Width,
                size.Height,
                true,
                pose.AngleDeg,
                pose.Scale,
                pose.SearchAngleStart,
                pose.SearchAngleExtent,
                pose.SearchAngleStep,
                pose.SearchScaleMin,
                pose.SearchScaleMax,
                pose.SearchScaleStep);
        }
    }

    private static Mat CreateBaseScene()
    {
        var scene = new Mat(SceneHeight, SceneWidth, MatType.CV_8UC3, new Scalar(35, 38, 42));
        for (var y = 0; y < SceneHeight; y += 16)
        {
            Cv2.Line(scene, new Point(0, y), new Point(SceneWidth - 1, y), new Scalar(55 + y % 80, 70, 90), 1);
        }

        for (var x = 0; x < SceneWidth; x += 20)
        {
            Cv2.Line(scene, new Point(x, 0), new Point(x, SceneHeight - 1), new Scalar(80, 45 + x % 100, 65), 1);
        }

        for (var i = 0; i < 36; i++)
        {
            var x = 18 + (i * 47) % (SceneWidth - 42);
            var y = 22 + (i * 31) % (SceneHeight - 50);
            var color = new Scalar(60 + (i * 17) % 170, 50 + (i * 29) % 170, 70 + (i * 37) % 150);
            Cv2.Rectangle(scene, new Rect(x, y, 11 + i % 17, 8 + i % 13), color, -1);
            Cv2.Circle(scene, new Point((x + 23) % SceneWidth, (y + 19) % SceneHeight), 4 + i % 6, color, -1);
        }

        Cv2.PutText(scene, "CV-H", new Point(116, 124), HersheyFonts.HersheySimplex, 0.9, new Scalar(230, 230, 210), 2);
        Cv2.GaussianBlur(scene, scene, new Size(3, 3), 0.2);
        return scene;
    }

    private static Mat ApplyHomography(Mat source, BridgeCaseSpec spec)
    {
        var output = new Mat();
        using var homography = Mat.FromArray(spec.Homography);
        Cv2.WarpPerspective(source, output, homography, new Size(SceneWidth, SceneHeight), InterpolationFlags.Linear, BorderTypes.Constant, new Scalar(18, 20, 24));
        return output;
    }

    private static Mat ApplyPhotometric(Mat source, BridgeCaseSpec spec)
    {
        var output = new Mat();
        source.ConvertTo(output, source.Type(), spec.Alpha, spec.Beta);
        return output;
    }

    private static Mat CreateTemplate(Mat baseScene, Mat targetScene, BridgeCaseSpec spec)
    {
        if (spec.TemplateSource == "Source")
        {
            var sourceTopLeft = new Point(
                (int)Math.Round(spec.SourceAnchor.X - PatchWidth / 2.0),
                (int)Math.Round(spec.SourceAnchor.Y - PatchHeight / 2.0));
            return new Mat(baseScene, new Rect(sourceTopLeft.X, sourceTopLeft.Y, PatchWidth, PatchHeight)).Clone();
        }

        return new Mat(targetScene, new Rect((int)spec.TargetTopLeft.X, (int)spec.TargetTopLeft.Y, PatchWidth, PatchHeight)).Clone();
    }

    private static Mat CreatePoseSearchScene(Mat sourceScene, Mat template, BridgeCaseSpec spec)
    {
        var scene = sourceScene.Clone();
        using var transformed = TransformTemplate(template, spec.ExpectedAngleDeg, spec.ExpectedScale);
        using var roi = new Mat(scene, new Rect((int)spec.TargetTopLeft.X, (int)spec.TargetTopLeft.Y, transformed.Width, transformed.Height));
        transformed.CopyTo(roi);
        return scene;
    }

    private static Operator CreateOperator(BridgeCaseSpec spec)
    {
        var op = new Operator("TemplateMatchingHomographyBridge", OperatorType.TemplateMatching, 0, 0);
        op.Parameters.Add(new Parameter(Guid.NewGuid(), "Method", "Method", string.Empty, "string", "CCoeffNormed"));
        op.Parameters.Add(new Parameter(Guid.NewGuid(), "Domain", "Domain", string.Empty, "string", "Gray"));
        op.Parameters.Add(new Parameter(Guid.NewGuid(), "Threshold", "Threshold", string.Empty, "double", spec.EnablePoseSearch ? 0.55 : 0.72));
        op.Parameters.Add(new Parameter(Guid.NewGuid(), "MaxMatches", "MaxMatches", string.Empty, "int", 1));
        var roiX = Math.Max(0, (int)spec.TargetTopLeft.X - 18);
        var roiY = Math.Max(0, (int)spec.TargetTopLeft.Y - 18);
        var roiRight = Math.Min(SceneWidth, (int)spec.TargetTopLeft.X + spec.TargetWidth + 18);
        var roiBottom = Math.Min(SceneHeight, (int)spec.TargetTopLeft.Y + spec.TargetHeight + 18);
        op.Parameters.Add(new Parameter(Guid.NewGuid(), "UseRoi", "UseRoi", string.Empty, "bool", true));
        op.Parameters.Add(new Parameter(Guid.NewGuid(), "RoiX", "RoiX", string.Empty, "int", roiX));
        op.Parameters.Add(new Parameter(Guid.NewGuid(), "RoiY", "RoiY", string.Empty, "int", roiY));
        op.Parameters.Add(new Parameter(Guid.NewGuid(), "RoiWidth", "RoiWidth", string.Empty, "int", Math.Max(spec.TargetWidth, roiRight - roiX)));
        op.Parameters.Add(new Parameter(Guid.NewGuid(), "RoiHeight", "RoiHeight", string.Empty, "int", Math.Max(spec.TargetHeight, roiBottom - roiY)));
        if (spec.EnablePoseSearch)
        {
            op.Parameters.Add(new Parameter(Guid.NewGuid(), "EnablePoseSearch", "EnablePoseSearch", string.Empty, "bool", true));
            op.Parameters.Add(new Parameter(Guid.NewGuid(), "AngleStart", "AngleStart", string.Empty, "double", spec.PoseSearchAngleStart));
            op.Parameters.Add(new Parameter(Guid.NewGuid(), "AngleExtent", "AngleExtent", string.Empty, "double", spec.PoseSearchAngleExtent));
            op.Parameters.Add(new Parameter(Guid.NewGuid(), "AngleStep", "AngleStep", string.Empty, "double", spec.PoseSearchAngleStep));
            op.Parameters.Add(new Parameter(Guid.NewGuid(), "ScaleMin", "ScaleMin", string.Empty, "double", spec.PoseSearchScaleMin));
            op.Parameters.Add(new Parameter(Guid.NewGuid(), "ScaleMax", "ScaleMax", string.Empty, "double", spec.PoseSearchScaleMax));
            op.Parameters.Add(new Parameter(Guid.NewGuid(), "ScaleStep", "ScaleStep", string.Empty, "double", spec.PoseSearchScaleStep));
            op.Parameters.Add(new Parameter(Guid.NewGuid(), "PyramidLevels", "PyramidLevels", string.Empty, "int", 3));
        }

        return op;
    }

    private static double[,] Translation(double dx, double dy)
    {
        return new[,] { { 1d, 0d, dx }, { 0d, 1d, dy }, { 0d, 0d, 1d } };
    }

    private static double[,] HomographyFromCorners(
        Point2f s0,
        Point2f s1,
        Point2f s2,
        Point2f s3,
        Point2f d0,
        Point2f d1,
        Point2f d2,
        Point2f d3)
    {
        using var src = Mat.FromArray(new[] { s0, s1, s2, s3 });
        using var dst = Mat.FromArray(new[] { d0, d1, d2, d3 });
        using var h = Cv2.GetPerspectiveTransform(src, dst);
        return new[,]
        {
            { h.At<double>(0, 0), h.At<double>(0, 1), h.At<double>(0, 2) },
            { h.At<double>(1, 0), h.At<double>(1, 1), h.At<double>(1, 2) },
            { h.At<double>(2, 0), h.At<double>(2, 1), h.At<double>(2, 2) }
        };
    }

    private static Point2d Transform(Point2d point, double[,] h)
    {
        var denominator = h[2, 0] * point.X + h[2, 1] * point.Y + h[2, 2];
        return new Point2d(
            (h[0, 0] * point.X + h[0, 1] * point.Y + h[0, 2]) / denominator,
            (h[1, 0] * point.X + h[1, 1] * point.Y + h[1, 2]) / denominator);
    }

    private static Mat TransformTemplate(Mat source, double angleDeg, double scale)
    {
        var center = new Point2f(source.Width / 2f, source.Height / 2f);
        using var matrix = Cv2.GetRotationMatrix2D(center, angleDeg, scale);
        var size = TransformedSize(source.Size(), angleDeg, scale);
        matrix.Set(0, 2, matrix.Get<double>(0, 2) + (size.Width / 2.0) - center.X);
        matrix.Set(1, 2, matrix.Get<double>(1, 2) + (size.Height / 2.0) - center.Y);

        var transformed = new Mat();
        Cv2.WarpAffine(source, transformed, matrix, size, InterpolationFlags.Linear, BorderTypes.Constant, Scalar.Black);
        return transformed;
    }

    private static Size TransformedSize(Size sourceSize, double angleDeg, double scale)
    {
        var center = new Point2f(sourceSize.Width / 2f, sourceSize.Height / 2f);
        using var matrix = Cv2.GetRotationMatrix2D(center, angleDeg, scale);
        var m00 = matrix.Get<double>(0, 0);
        var m01 = matrix.Get<double>(0, 1);
        var m10 = matrix.Get<double>(1, 0);
        var m11 = matrix.Get<double>(1, 1);
        return new Size(
            Math.Max(1, (int)Math.Ceiling((sourceSize.Width * Math.Abs(m00)) + (sourceSize.Height * Math.Abs(m01)))),
            Math.Max(1, (int)Math.Ceiling((sourceSize.Width * Math.Abs(m10)) + (sourceSize.Height * Math.Abs(m11)))));
    }

    private static double NormalizeAngle(double angle)
    {
        var value = angle;
        while (value > 180.0)
        {
            value -= 360.0;
        }

        while (value < -180.0)
        {
            value += 360.0;
        }

        return value;
    }

    private static Point2d ReadPosition(OperatorExecutionOutput? output)
    {
        if (output?.OutputData is null || !output.OutputData.TryGetValue("Position", out var raw))
        {
            return new Point2d(double.NaN, double.NaN);
        }

        if (raw is Position pos)
        {
            return new Point2d(pos.X, pos.Y);
        }

        return new Point2d(double.NaN, double.NaN);
    }

    private static double ReadDouble(OperatorExecutionOutput? output, string key)
    {
        if (output?.OutputData is null || !output.OutputData.TryGetValue(key, out var raw))
        {
            return double.NaN;
        }

        try
        {
            return Convert.ToDouble(raw);
        }
        catch
        {
            return double.NaN;
        }
    }

    private static int ReadInt(OperatorExecutionOutput? output, string key)
    {
        if (output?.OutputData is null || !output.OutputData.TryGetValue(key, out var raw))
        {
            return 0;
        }

        try
        {
            return Convert.ToInt32(raw);
        }
        catch
        {
            return 0;
        }
    }

    private static double Distance(Point2d a, Point2d b)
    {
        if (!double.IsFinite(a.X) || !double.IsFinite(a.Y))
        {
            return double.NaN;
        }

        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private static double Percentile(IReadOnlyList<double> ordered, double p)
    {
        if (ordered.Count == 0)
        {
            return 0;
        }

        var index = Math.Clamp((int)Math.Ceiling(p * ordered.Count) - 1, 0, ordered.Count - 1);
        return ordered[index];
    }

    private static void ReleaseImageOutputs(IReadOnlyDictionary<string, object>? outputData)
    {
        if (outputData is null)
        {
            return;
        }

        foreach (var value in outputData.Values)
        {
            if (value is ImageWrapper image)
            {
                image.Release();
            }
        }
    }
}

internal sealed record BridgeCaseSpec(
    string CaseId,
    string Sequence,
    string TemplateSource,
    double[,] Homography,
    double Alpha,
    double Beta,
    Point2d SourceAnchor,
    Point2d TargetTopLeft,
    Point2d ExpectedCenter,
    int TargetWidth,
    int TargetHeight,
    bool EnablePoseSearch = false,
    double ExpectedAngleDeg = 0.0,
    double ExpectedScale = 1.0,
    double PoseSearchAngleStart = 0.0,
    double PoseSearchAngleExtent = 0.0,
    double PoseSearchAngleStep = 1.0,
    double PoseSearchScaleMin = 1.0,
    double PoseSearchScaleMax = 1.0,
    double PoseSearchScaleStep = 0.05);

internal sealed record PoseReplaySpec(
    string Sequence,
    Point2d SourceAnchor,
    Point2d TargetTopLeft,
    double AngleDeg,
    double Scale,
    double SearchAngleStart,
    double SearchAngleExtent,
    double SearchAngleStep,
    double SearchScaleMin,
    double SearchScaleMax,
    double SearchScaleStep);

internal sealed record SequenceSpec(
    string Name,
    double[,] Homography,
    string TemplateSource,
    double Alpha,
    double Beta);

internal sealed record BridgeResult(
    BridgeSummary Summary,
    IReadOnlyList<OperatorEvidence> Operators,
    IReadOnlyList<BridgeCaseResult> Cases);

internal sealed record BridgeSummary(
    DateTimeOffset GeneratedAtUtc,
    string DatasetName,
    string DatasetKind,
    int CaseCount,
    int Passed,
    int Failed,
    double PositionTolerancePx,
    double MeanPositionErrorPx,
    double P95PositionErrorPx,
    double RuntimeMs,
    string CandidateVersion,
    string Profile);

internal sealed record OperatorEvidence(
    string Operator,
    int CaseCount,
    int Passed,
    int Failed,
    double RuntimeMsAvg,
    double RuntimeMsMax,
    long MemoryAllocationBytesAvg,
    bool HasPublicDataset,
    string DatasetName);

internal sealed record BridgeCaseResult(
    string CaseId,
    string Operator,
    string Sequence,
    string TemplateSource,
    bool Passed,
    double RuntimeMs,
    long MemoryAllocationBytes,
    double ExpectedX,
    double ExpectedY,
    double ActualX,
    double ActualY,
    double PositionErrorPx,
    double Score,
    double NormalizedScore,
    double SubpixelOffsetX,
    double SubpixelOffsetY,
    double PeakCurvature,
    double ExpectedAngleDeg,
    double ActualAngleDeg,
    double AngleErrorDeg,
    double ExpectedScale,
    double ActualScale,
    double ScaleError,
    int PyramidLevels,
    string? ErrorMessage);

internal static class MarkdownReport
{
    public static string Create(BridgeResult result)
    {
        var lines = new List<string>
        {
            "# TemplateMatching Homography Bridge Baseline",
            string.Empty,
            $"GeneratedAtUtc: `{result.Summary.GeneratedAtUtc:O}`",
            $"Dataset: `{result.Summary.DatasetName}`",
            $"DatasetKind: `{result.Summary.DatasetKind}`",
            string.Empty,
            "## Summary",
            string.Empty,
            "| Metric | Value |",
            "| --- | ---: |",
            $"| Cases | {result.Summary.CaseCount} |",
            $"| Passed | {result.Summary.Passed} |",
            $"| Failed | {result.Summary.Failed} |",
            $"| Position tolerance px | {result.Summary.PositionTolerancePx:0.###} |",
            $"| Mean position error px | {result.Summary.MeanPositionErrorPx:0.####} |",
            $"| P95 position error px | {result.Summary.P95PositionErrorPx:0.####} |",
            $"| Runtime ms | {result.Summary.RuntimeMs:0.###} |",
            $"| Candidate version | {result.Summary.CandidateVersion} |",
            $"| Profile | {result.Summary.Profile} |",
            string.Empty,
            "## Operators",
            string.Empty,
            "| Operator | Cases | Passed | Failed | Avg ms | Max ms | Avg bytes | Public/Alternative Dataset |",
            "| --- | ---: | ---: | ---: | ---: | ---: | ---: | --- |"
        };

        lines.AddRange(result.Operators.Select(item =>
            $"| {item.Operator} | {item.CaseCount} | {item.Passed} | {item.Failed} | {item.RuntimeMsAvg:0.###} | {item.RuntimeMsMax:0.###} | {item.MemoryAllocationBytesAvg} | {item.DatasetName} |"));

        lines.AddRange(
        [
            string.Empty,
            "## Cases",
            string.Empty,
            "| Case | Sequence | Template | Passed | Pos Error Px | Angle Err | Scale Err | Pyramid Levels | Score | Norm Score | Subpixel X | Subpixel Y | Peak Curvature | Runtime Ms | Error |",
            "| --- | --- | --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | --- |"
        ]);

        lines.AddRange(result.Cases.Select(item =>
            $"| {item.CaseId} | {item.Sequence} | {item.TemplateSource} | {item.Passed} | {item.PositionErrorPx:0.####} | {item.AngleErrorDeg:0.###} | {item.ScaleError:0.###} | {item.PyramidLevels} | {item.Score:0.######} | {item.NormalizedScore:0.######} | {item.SubpixelOffsetX:0.######} | {item.SubpixelOffsetY:0.######} | {item.PeakCurvature:0.######} | {item.RuntimeMs:0.###} | {item.ErrorMessage ?? "-"} |"));

        lines.Add(string.Empty);
        return string.Join(Environment.NewLine, lines);
    }
}

internal sealed record RunnerOptions(
    string OutputPath,
    string? ReportPath,
    string CandidateVersion,
    string Profile,
    IReadOnlySet<string> CaseIds,
    bool ShowHelp,
    string? ParseError)
{
    public bool IncludesCase(BridgeCaseSpec spec) => CaseIds.Count == 0 || CaseIds.Contains(spec.CaseId);
    public bool IncludesPoseSearch =>
        Profile.Contains("precision_v2", StringComparison.OrdinalIgnoreCase) ||
        Profile.Contains("pose", StringComparison.OrdinalIgnoreCase);

    public static RunnerOptions Parse(string[] args)
    {
        var output = "quality/evals/reports/TemplateMatching_public_bridge_baseline.json";
        string? report = "quality/evals/reports/TemplateMatching_public_bridge_baseline.md";
        var candidateVersion = "control";
        var profile = "baseline_homography_bridge";
        IReadOnlySet<string> caseIds = new HashSet<string>(StringComparer.Ordinal);
        var showHelp = false;
        string? parseError = null;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "-h":
                case "--help":
                    showHelp = true;
                    break;
                case "--output":
                    output = NextValue(args, ref i, "--output", ref parseError);
                    break;
                case "--report":
                    report = NextValue(args, ref i, "--report", ref parseError);
                    break;
                case "--candidate-version":
                    candidateVersion = NextValue(args, ref i, "--candidate-version", ref parseError);
                    break;
                case "--profile":
                    profile = NextValue(args, ref i, "--profile", ref parseError);
                    break;
                case "--case-ids":
                    caseIds = SplitCaseIds(NextValue(args, ref i, "--case-ids", ref parseError));
                    break;
                default:
                    parseError = $"Unknown argument: {args[i]}";
                    break;
            }
        }

        return new RunnerOptions(output, report, candidateVersion, profile, caseIds, showHelp, parseError);
    }

    public static void PrintHelp()
    {
        Console.WriteLine(
            """
            TemplateMatching homography bridge runner

            Options:
              --output <path>  Baseline JSON output path.
              --report <path>  Markdown report output path.
              --candidate-version <id>  Candidate version label to record.
              --profile <name>          Candidate profile label to record.
              --case-ids <ids>          Comma-separated case ids to execute.
              --help           Show help.
            """);
    }

    private static string NextValue(string[] args, ref int index, string optionName, ref string? parseError)
    {
        if (index + 1 >= args.Length)
        {
            parseError = $"Missing value for {optionName}";
            return string.Empty;
        }

        index++;
        return args[index];
    }

    private static IReadOnlySet<string> SplitCaseIds(string raw) =>
        raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.Ordinal);
}

internal static class JsonSettings
{
    public static readonly JsonSerializerOptions Indented = new()
    {
        WriteIndented = true
    };
}
