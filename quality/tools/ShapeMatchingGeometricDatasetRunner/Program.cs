using System.Collections;
using System.Diagnostics;
using System.Text.Json;
using Acme.Product.Core.Entities;
using Acme.Product.Core.Enums;
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

var result = ShapeMatchingGeometricDatasetRunner.Run(options);
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(options.OutputPath))!);
File.WriteAllText(options.OutputPath, JsonSerializer.Serialize(result, JsonSettings.Indented));

if (!string.IsNullOrWhiteSpace(options.ReportPath))
{
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(options.ReportPath))!);
    File.WriteAllText(options.ReportPath, MarkdownReport.Create(result));
}

Console.WriteLine(
    $"ShapeMatching geometric dataset complete: {result.Summary.Passed}/{result.Summary.CaseCount} passed, " +
    $"F1={result.Summary.F1:F4}, meanPoseError={result.Summary.MeanPositionErrorPx:F3}px, output={options.OutputPath}");

return result.Summary.Failed == 0 ? 0 : 1;

internal static class ShapeMatchingGeometricDatasetRunner
{
    private const string EvidenceKind = "dataset";
    private const string DatasetName = "Semi-synthetic geometric shape matching scene protocol";
    private const double PositionTolerancePx = 8.0;
    private const double AngleToleranceDeg = 6.0;
    private const double ScaleTolerance = 0.16;
    private const double MinAcceptedScore = 0.40;
    private static readonly ShapeMatchingOperator Operator = new(NullLogger<ShapeMatchingOperator>.Instance);

    public static BaselineResult Run(RunnerOptions options)
    {
        var specs = BuildCases()
            .Where(options.IncludesCase)
            .ToList();
        var results = new List<CaseResult>(specs.Count);
        foreach (var spec in specs)
        {
            results.Add(RunCase(spec));
        }

        var failed = results.Count(item => !item.Passed);
        var tp = results.Sum(item => item.TruePositiveCount);
        var fp = results.Sum(item => item.FalsePositiveCount);
        var fn = results.Sum(item => item.FalseNegativeCount);
        var precision = Precision(tp, fp, fn);
        var recall = Recall(tp, fn, fp);
        var f1 = F1(precision, recall, tp, fp, fn);
        var matched = results.SelectMany(item => item.Matches).Where(item => item.IsTruePositive).ToArray();
        var scores = results.SelectMany(item => item.Predictions).Select(item => item.Score).ToArray();
        var runtimeMs = Math.Round(results.Sum(item => item.RuntimeMs), 3);
        var memoryBytes = results.Sum(item => item.MemoryAllocationBytes);

        return new BaselineResult(
            EvidenceKind,
            new DatasetSummary(
                DateTimeOffset.UtcNow,
                DatasetName,
                "Tier B semi-synthetic geometric scenes with fixed seed, pose labels, multi-target labels, and no-match negatives.",
                specs.Count,
                results.Count - failed,
                failed,
                specs.Sum(item => item.GroundTruth.Count),
                results.Sum(item => item.PredictedCount),
                tp,
                fp,
                fn,
                Math.Round(precision, 6),
                Math.Round(recall, 6),
                Math.Round(f1, 6),
                Math.Round(matched.Length == 0 ? 0 : matched.Average(item => item.PositionErrorPx), 6),
                Math.Round(matched.Length == 0 ? 0 : matched.Average(item => item.AngleErrorDeg), 6),
                Math.Round(matched.Length == 0 ? 0 : matched.Average(item => item.ScaleError), 6),
                Math.Round(scores.Length == 0 ? 0 : scores.Min(), 6),
                Math.Round(scores.Length == 0 ? 0 : scores.Average(), 6),
                PositionTolerancePx,
                AngleToleranceDeg,
                ScaleTolerance,
                runtimeMs,
                memoryBytes,
                options.CandidateVersion,
                options.Profile),
            [
                new OperatorSummary(
                    "ShapeMatching",
                    specs.Count,
                    results.Count - failed,
                    failed,
                    Math.Round(results.Average(item => item.RuntimeMs), 3),
                    (long)Math.Round(results.Average(item => item.MemoryAllocationBytes)),
                    true,
                    "dataset",
                    DatasetName)
            ],
            results
                .GroupBy(item => item.Scenario)
                .OrderBy(group => group.Key, StringComparer.Ordinal)
                .Select(group => new ScenarioSummary(
                    group.Key,
                    group.Count(),
                    group.Count(item => item.Passed),
                    group.Count(item => !item.Passed),
                    group.Sum(item => item.GroundTruthCount),
                    group.Sum(item => item.PredictedCount),
                    group.Sum(item => item.TruePositiveCount),
                    group.Sum(item => item.FalsePositiveCount),
                    group.Sum(item => item.FalseNegativeCount),
                    Math.Round(group.Average(item => item.F1), 6),
                    Math.Round(group.Average(item => item.MeanPositionErrorPx), 6),
                    Math.Round(group.Average(item => item.RuntimeMs), 3)))
                .ToArray(),
            results);
    }

    private static CaseResult RunCase(ShapeCaseSpec spec)
    {
        var stopwatch = Stopwatch.StartNew();
        var allocationBefore = GC.GetTotalAllocatedBytes(precise: true);
        Dictionary<string, object>? outputData = null;
        try
        {
            using var templateWrapper = new ImageWrapper(CreateShapeTemplate());
            using var sceneWrapper = new ImageWrapper(CreateScene(spec, templateWrapper.MatReadOnly));
            var result = Operator.ExecuteAsync(
                    CreateOperator(spec),
                    new Dictionary<string, object>
                    {
                        ["Image"] = sceneWrapper,
                        ["Template"] = templateWrapper
                    })
                .GetAwaiter()
                .GetResult();

            outputData = result.OutputData;
            Require(result.IsSuccess, $"Expected success, got failure: {result.ErrorMessage}");
            if (outputData is null)
            {
                throw new InvalidOperationException("Expected output data.");
            }

            var predictions = ReadPredictions(outputData);
            var evaluation = Evaluate(spec.GroundTruth, predictions);
            var isMatch = RequireBool(outputData, "IsMatch");
            if (spec.GroundTruth.Count == 0)
            {
                Require(!isMatch, "Expected no-match output for negative scene.");
                RequireStringContains(outputData, "FailureReason", "No rotation-scale template match");
            }
            else
            {
                Require(isMatch, "Expected IsMatch=true for positive scene.");
            }

            var passed =
                evaluation.FalsePositiveCount == 0 &&
                evaluation.FalseNegativeCount == 0 &&
                evaluation.Matches.All(item =>
                    item.PositionErrorPx <= PositionTolerancePx &&
                    item.AngleErrorDeg <= AngleToleranceDeg &&
                    item.ScaleError <= ScaleTolerance &&
                    item.Score >= MinAcceptedScore);

            stopwatch.Stop();
            var allocationAfter = GC.GetTotalAllocatedBytes(precise: true);

            return new CaseResult(
                spec.CaseId,
                spec.Scenario,
                passed,
                Math.Round(stopwatch.Elapsed.TotalMilliseconds, 3),
                Math.Max(0, allocationAfter - allocationBefore),
                spec.Width,
                spec.Height,
                spec.GroundTruth.Count,
                predictions.Count,
                evaluation.TruePositiveCount,
                evaluation.FalsePositiveCount,
                evaluation.FalseNegativeCount,
                Math.Round(evaluation.Precision, 6),
                Math.Round(evaluation.Recall, 6),
                Math.Round(evaluation.F1, 6),
                Math.Round(evaluation.Matches.Count == 0 ? 0 : evaluation.Matches.Average(item => item.PositionErrorPx), 6),
                Math.Round(evaluation.Matches.Count == 0 ? 0 : evaluation.Matches.Average(item => item.AngleErrorDeg), 6),
                Math.Round(evaluation.Matches.Count == 0 ? 0 : evaluation.Matches.Average(item => item.ScaleError), 6),
                predictions,
                evaluation.Matches,
                passed ? null : evaluation.FailureReason);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            var allocationAfter = GC.GetTotalAllocatedBytes(precise: true);
            return new CaseResult(
                spec.CaseId,
                spec.Scenario,
                false,
                Math.Round(stopwatch.Elapsed.TotalMilliseconds, 3),
                Math.Max(0, allocationAfter - allocationBefore),
                spec.Width,
                spec.Height,
                spec.GroundTruth.Count,
                0,
                0,
                0,
                spec.GroundTruth.Count,
                0,
                0,
                0,
                0,
                0,
                0,
                [],
                [],
                ex.GetBaseException().Message);
        }
        finally
        {
            DisposeOutputImages(outputData);
        }
    }

    private static IEnumerable<ShapeCaseSpec> BuildCases()
    {
        for (var i = 0; i < 6; i++)
        {
            yield return DirectPoseCase(i);
            yield return RotatedPoseCase(i);
            yield return ScaledPoseCase(i);
            yield return MultiTargetCase(i);
            yield return TopLeftOriginCase(i);
            yield return BlankNegativeCase(i);
        }
    }

    private static ShapeCaseSpec DirectPoseCase(int index)
    {
        var x = 32 + index * 11;
        var y = 48 + index * 7;
        return new ShapeCaseSpec(
            $"ShapeMatching_direct_pose_{index:0000}",
            "direct_pose",
            240,
            220,
            0,
            0,
            1.0,
            1.0,
            1.0,
            0.10,
            1,
            1,
            "Center",
            [new GroundTruthPose(x + 24, y + 24, 0, 1.0)],
            [new TargetInstance(x, y, 0, 1.0)]);
    }

    private static ShapeCaseSpec RotatedPoseCase(int index)
    {
        var angles = new[] { 12.0, 20.0, 28.0, 36.0, 44.0, 52.0 };
        var angle = angles[index];
        var x = 58 + index * 8;
        var y = 50 + index * 6;
        using var template = CreateShapeTemplate();
        using var transformed = TransformTemplate(template, angle, 1.0);
        return new ShapeCaseSpec(
            $"ShapeMatching_rotated_pose_{index:0000}",
            "rotated_pose",
            280,
            260,
            -70,
            140,
            1.0,
            1.0,
            1.0,
            0.10,
            2,
            1,
            "Center",
            [new GroundTruthPose(x + transformed.Width / 2.0, y + transformed.Height / 2.0, angle, 1.0)],
            [new TargetInstance(x, y, angle, 1.0)]);
    }

    private static ShapeCaseSpec ScaledPoseCase(int index)
    {
        var scales = new[] { 0.85, 0.95, 1.05, 1.15, 1.25, 1.35 };
        var scale = scales[index];
        var angle = index % 2 == 0 ? 0.0 : 18.0;
        var x = 60 + index * 7;
        var y = 70 + index * 5;
        using var template = CreateShapeTemplate();
        using var transformed = TransformTemplate(template, angle, scale);
        return new ShapeCaseSpec(
            $"ShapeMatching_scaled_pose_{index:0000}",
            "scaled_pose",
            300,
            280,
            -45,
            90,
            1.0,
            0.75,
            1.45,
            0.05,
            3,
            1,
            "Center",
            [new GroundTruthPose(x + transformed.Width / 2.0, y + transformed.Height / 2.0, angle, scale)],
            [new TargetInstance(x, y, angle, scale)]);
    }

    private static ShapeCaseSpec MultiTargetCase(int index)
    {
        var firstX = 24 + index;
        var firstY = 34 + index;
        var secondX = 160 - index;
        var secondY = 148 + index;
        return new ShapeCaseSpec(
            $"ShapeMatching_multi_target_{index:0000}",
            "multi_target",
            260,
            250,
            0,
            0,
            1.0,
            1.0,
            1.0,
            0.10,
            1,
            2,
            "Center",
            [
                new GroundTruthPose(firstX + 24, firstY + 24, 0, 1.0),
                new GroundTruthPose(secondX + 24, secondY + 24, 0, 1.0)
            ],
            [
                new TargetInstance(firstX, firstY, 0, 1.0),
                new TargetInstance(secondX, secondY, 0, 1.0)
            ]);
    }

    private static ShapeCaseSpec TopLeftOriginCase(int index)
    {
        var x = 46 + index * 9;
        var y = 42 + index * 8;
        return new ShapeCaseSpec(
            $"ShapeMatching_top_left_origin_{index:0000}",
            "top_left_origin",
            240,
            220,
            0,
            0,
            1.0,
            1.0,
            1.0,
            0.10,
            1,
            1,
            "TopLeft",
            [new GroundTruthPose(x, y, 0, 1.0)],
            [new TargetInstance(x, y, 0, 1.0)]);
    }

    private static ShapeCaseSpec BlankNegativeCase(int index)
    {
        return new ShapeCaseSpec(
            $"ShapeMatching_blank_negative_{index:0000}",
            "blank_negative",
            240,
            220,
            -30,
            60,
            1.0,
            1.0,
            1.0,
            0.10,
            1,
            1,
            "Center",
            [],
            []);
    }

    private static Mat CreateScene(ShapeCaseSpec spec, Mat template)
    {
        var scene = new Mat(spec.Height, spec.Width, MatType.CV_8UC3, Scalar.Black);
        AddBackgroundGrid(scene, spec);
        foreach (var target in spec.Targets)
        {
            using var transformed = TransformTemplate(template, target.AngleDeg, target.Scale);
            CopyTemplate(scene, transformed, target.X, target.Y);
        }

        return scene;
    }

    private static Mat CreateShapeTemplate()
    {
        var mat = new Mat(48, 48, MatType.CV_8UC3, Scalar.Black);
        Cv2.Rectangle(mat, new Rect(2, 2, 44, 44), Scalar.White, -1);
        Cv2.Line(mat, new Point(4, 24), new Point(44, 24), Scalar.Black, 2);
        Cv2.Line(mat, new Point(24, 4), new Point(24, 44), Scalar.Black, 2);
        Cv2.Circle(mat, new Point(15, 15), 5, Scalar.Black, -1);
        return mat;
    }

    private static void AddBackgroundGrid(Mat scene, ShapeCaseSpec spec)
    {
        if (spec.Scenario == "blank_negative")
        {
            return;
        }

        for (var x = 0; x < scene.Width; x += 32)
        {
            Cv2.Line(scene, new Point(x, 0), new Point(x, scene.Height - 1), new Scalar(12, 12, 12), 1);
        }

        for (var y = 0; y < scene.Height; y += 28)
        {
            Cv2.Line(scene, new Point(0, y), new Point(scene.Width - 1, y), new Scalar(10, 10, 10), 1);
        }
    }

    private static Mat TransformTemplate(Mat template, double angleDeg, double scale)
    {
        using var scaled = new Mat();
        if (Math.Abs(scale - 1.0) > 1e-9)
        {
            Cv2.Resize(template, scaled, new Size(), scale, scale, InterpolationFlags.Linear);
        }
        else
        {
            template.CopyTo(scaled);
        }

        return RotateExpanded(scaled, angleDeg);
    }

    private static Mat RotateExpanded(Mat src, double angle)
    {
        if (Math.Abs(angle) <= 1e-9)
        {
            return src.Clone();
        }

        var center = new Point2f(src.Width / 2f, src.Height / 2f);
        using var rotMatrix = Cv2.GetRotationMatrix2D(center, angle, 1.0);
        var cos = Math.Abs(rotMatrix.Get<double>(0, 0));
        var sin = Math.Abs(rotMatrix.Get<double>(0, 1));
        var boundWidth = Math.Max(1, (int)Math.Ceiling((src.Height * sin) + (src.Width * cos)));
        var boundHeight = Math.Max(1, (int)Math.Ceiling((src.Height * cos) + (src.Width * sin)));
        rotMatrix.Set(0, 2, rotMatrix.Get<double>(0, 2) + boundWidth / 2.0 - center.X);
        rotMatrix.Set(1, 2, rotMatrix.Get<double>(1, 2) + boundHeight / 2.0 - center.Y);

        var rotated = new Mat();
        Cv2.WarpAffine(src, rotated, rotMatrix, new Size(boundWidth, boundHeight), InterpolationFlags.Linear, BorderTypes.Constant, Scalar.Black);
        return rotated;
    }

    private static void CopyTemplate(Mat scene, Mat template, int x, int y)
    {
        using var roi = new Mat(scene, new Rect(x, y, template.Width, template.Height));
        template.CopyTo(roi);
    }

    private static Operator CreateOperator(ShapeCaseSpec spec)
    {
        var op = new Operator(Guid.NewGuid(), "ShapeMatchingGeometricDataset", OperatorType.ShapeMatching, 0, 0);
        AddParameter(op, "MinScore", spec.MinScore);
        AddParameter(op, "MaxMatches", spec.MaxMatches);
        AddParameter(op, "AngleStart", spec.AngleStart);
        AddParameter(op, "AngleExtent", spec.AngleExtent);
        AddParameter(op, "AngleStep", spec.AngleStep);
        AddParameter(op, "ScaleMin", spec.ScaleMin);
        AddParameter(op, "ScaleMax", spec.ScaleMax);
        AddParameter(op, "ScaleStep", spec.ScaleStep);
        AddParameter(op, "NumLevels", spec.NumLevels);
        AddParameter(op, "OriginMode", spec.OriginMode);
        return op;
    }

    private static void AddParameter(Operator op, string name, object value)
    {
        op.AddParameter(new Parameter(Guid.NewGuid(), name, name, string.Empty, ParameterType(value), value, isRequired: false));
    }

    private static string ParameterType(object value)
    {
        return value switch
        {
            bool => "bool",
            int or long => "int",
            float or double or decimal => "double",
            _ => "string"
        };
    }

    private static IReadOnlyList<PredictionPose> ReadPredictions(Dictionary<string, object> outputData)
    {
        if (!outputData.TryGetValue("Matches", out var raw) || raw is null)
        {
            return [];
        }

        if (raw is not IEnumerable enumerable)
        {
            return [];
        }

        var predictions = new List<PredictionPose>();
        foreach (var item in enumerable)
        {
            var dict = ToDictionary(item);
            if (dict.Count == 0)
            {
                continue;
            }

            predictions.Add(new PredictionPose(
                ReadDouble(dict, "ReferenceX"),
                ReadDouble(dict, "ReferenceY"),
                ReadDouble(dict, "Angle"),
                ReadDouble(dict, "Scale"),
                ReadDouble(dict, "Score")));
        }

        return predictions
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.X)
            .ThenBy(item => item.Y)
            .ToArray();
    }

    private static Dictionary<string, object?> ToDictionary(object item)
    {
        if (item is Dictionary<string, object?> nullableDict)
        {
            return nullableDict;
        }

        if (item is Dictionary<string, object> dict)
        {
            return dict.ToDictionary(pair => pair.Key, pair => (object?)pair.Value);
        }

        return [];
    }

    private static ShapeEvaluation Evaluate(IReadOnlyList<GroundTruthPose> groundTruth, IReadOnlyList<PredictionPose> predictions)
    {
        var matchedGroundTruth = new bool[groundTruth.Count];
        var matches = new List<MatchedPose>();
        var falsePositives = 0;

        foreach (var prediction in predictions)
        {
            var bestIndex = -1;
            var bestDistance = double.PositiveInfinity;
            var bestAngleError = double.PositiveInfinity;
            var bestScaleError = double.PositiveInfinity;

            for (var i = 0; i < groundTruth.Count; i++)
            {
                if (matchedGroundTruth[i])
                {
                    continue;
                }

                var gt = groundTruth[i];
                var distance = Distance(prediction.X, prediction.Y, gt.X, gt.Y);
                var angleError = AngleError(prediction.AngleDeg, gt.AngleDeg);
                var scaleError = Math.Abs(prediction.Scale - gt.Scale);
                if (distance < bestDistance)
                {
                    bestIndex = i;
                    bestDistance = distance;
                    bestAngleError = angleError;
                    bestScaleError = scaleError;
                }
            }

            if (bestIndex >= 0 &&
                bestDistance <= PositionTolerancePx &&
                bestAngleError <= AngleToleranceDeg &&
                bestScaleError <= ScaleTolerance &&
                prediction.Score >= MinAcceptedScore)
            {
                matchedGroundTruth[bestIndex] = true;
                matches.Add(new MatchedPose(
                    groundTruth[bestIndex],
                    prediction,
                    true,
                    bestDistance,
                    bestAngleError,
                    bestScaleError,
                    prediction.Score));
            }
            else
            {
                falsePositives++;
                matches.Add(new MatchedPose(
                    bestIndex >= 0 ? groundTruth[bestIndex] : null,
                    prediction,
                    false,
                    double.IsInfinity(bestDistance) ? 0 : bestDistance,
                    double.IsInfinity(bestAngleError) ? 0 : bestAngleError,
                    double.IsInfinity(bestScaleError) ? 0 : bestScaleError,
                    prediction.Score));
            }
        }

        var truePositives = matches.Count(item => item.IsTruePositive);
        var falseNegatives = groundTruth.Count - matchedGroundTruth.Count(item => item);
        var precision = Precision(truePositives, falsePositives, falseNegatives);
        var recall = Recall(truePositives, falseNegatives, falsePositives);
        var f1 = F1(precision, recall, truePositives, falsePositives, falseNegatives);
        var failures = new List<string>();
        if (falsePositives > 0)
        {
            failures.Add($"FP={falsePositives}");
        }

        if (falseNegatives > 0)
        {
            failures.Add($"FN={falseNegatives}");
        }

        foreach (var match in matches.Where(item => item.IsTruePositive))
        {
            if (match.PositionErrorPx > PositionTolerancePx)
            {
                failures.Add($"PositionError={match.PositionErrorPx:0.###}");
            }

            if (match.AngleErrorDeg > AngleToleranceDeg)
            {
                failures.Add($"AngleError={match.AngleErrorDeg:0.###}");
            }

            if (match.ScaleError > ScaleTolerance)
            {
                failures.Add($"ScaleError={match.ScaleError:0.###}");
            }

            if (match.Score < MinAcceptedScore)
            {
                failures.Add($"Score={match.Score:0.###}");
            }
        }

        return new ShapeEvaluation(
            truePositives,
            falsePositives,
            falseNegatives,
            precision,
            recall,
            f1,
            matches,
            failures.Count == 0 ? null : string.Join("; ", failures));
    }

    private static double Distance(double x1, double y1, double x2, double y2)
    {
        var dx = x1 - x2;
        var dy = y1 - y2;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private static double AngleError(double actual, double expected)
    {
        var delta = Math.Abs(actual - expected) % 360.0;
        return delta > 180.0 ? 360.0 - delta : delta;
    }

    private static double Precision(int truePositives, int falsePositives, int falseNegatives)
    {
        return truePositives + falsePositives == 0 ? (falseNegatives == 0 ? 1d : 0d) : truePositives / (double)(truePositives + falsePositives);
    }

    private static double Recall(int truePositives, int falseNegatives, int falsePositives)
    {
        return truePositives + falseNegatives == 0 ? (falsePositives == 0 ? 1d : 0d) : truePositives / (double)(truePositives + falseNegatives);
    }

    private static double F1(double precision, double recall, int truePositives, int falsePositives, int falseNegatives)
    {
        if (truePositives == 0 && falsePositives == 0 && falseNegatives == 0)
        {
            return 1d;
        }

        return precision + recall <= 0 ? 0 : 2d * precision * recall / (precision + recall);
    }

    private static double ReadDouble(Dictionary<string, object?> values, string key)
    {
        if (!values.TryGetValue(key, out var raw) || raw is null)
        {
            throw new InvalidOperationException($"Missing match field {key}.");
        }

        return Convert.ToDouble(raw);
    }

    private static bool RequireBool(Dictionary<string, object> outputData, string key)
    {
        if (!outputData.TryGetValue(key, out var raw) || raw is not bool value)
        {
            throw new InvalidOperationException($"Missing bool output {key}.");
        }

        return value;
    }

    private static void RequireStringContains(Dictionary<string, object> outputData, string key, string expected)
    {
        if (!outputData.TryGetValue(key, out var raw))
        {
            throw new InvalidOperationException($"Missing output {key}.");
        }

        var actual = Convert.ToString(raw) ?? string.Empty;
        if (actual.IndexOf(expected, StringComparison.OrdinalIgnoreCase) < 0)
        {
            throw new InvalidOperationException($"Expected {key} to contain '{expected}', got '{actual}'.");
        }
    }

    private static void DisposeOutputImages(Dictionary<string, object>? outputData)
    {
        if (outputData is null)
        {
            return;
        }

        foreach (var value in outputData.Values)
        {
            if (value is ImageWrapper wrapper)
            {
                wrapper.Dispose();
            }
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}

internal sealed record ShapeCaseSpec(
    string CaseId,
    string Scenario,
    int Width,
    int Height,
    double AngleStart,
    double AngleExtent,
    double AngleStep,
    double ScaleMin,
    double ScaleMax,
    double ScaleStep,
    int NumLevels,
    int MaxMatches,
    string OriginMode,
    IReadOnlyList<GroundTruthPose> GroundTruth,
    IReadOnlyList<TargetInstance> Targets)
{
    public double MinScore => Scenario == "blank_negative" ? 0.75 : Scenario == "rotated_pose" || Scenario == "scaled_pose" ? 0.45 : 0.60;
}

internal sealed record TargetInstance(int X, int Y, double AngleDeg, double Scale);
internal sealed record GroundTruthPose(double X, double Y, double AngleDeg, double Scale);
internal sealed record PredictionPose(double X, double Y, double AngleDeg, double Scale, double Score);

internal sealed record MatchedPose(
    GroundTruthPose? GroundTruth,
    PredictionPose Prediction,
    bool IsTruePositive,
    double PositionErrorPx,
    double AngleErrorDeg,
    double ScaleError,
    double Score);

internal sealed record ShapeEvaluation(
    int TruePositiveCount,
    int FalsePositiveCount,
    int FalseNegativeCount,
    double Precision,
    double Recall,
    double F1,
    IReadOnlyList<MatchedPose> Matches,
    string? FailureReason);

internal sealed record BaselineResult(
    string EvidenceKind,
    DatasetSummary Summary,
    IReadOnlyList<OperatorSummary> Operators,
    IReadOnlyList<ScenarioSummary> Scenarios,
    IReadOnlyList<CaseResult> Cases);

internal sealed record DatasetSummary(
    DateTimeOffset GeneratedAtUtc,
    string DatasetName,
    string DatasetKind,
    int CaseCount,
    int Passed,
    int Failed,
    int GroundTruthCount,
    int PredictedCount,
    int TruePositiveCount,
    int FalsePositiveCount,
    int FalseNegativeCount,
    double Precision,
    double Recall,
    double F1,
    double MeanPositionErrorPx,
    double MeanAngleErrorDeg,
    double MeanScaleError,
    double MinScore,
    double MeanScore,
    double PositionTolerancePx,
    double AngleToleranceDeg,
    double ScaleTolerance,
    double RuntimeMs,
    long MemoryAllocationBytes,
    string CandidateVersion,
    string Profile);

internal sealed record OperatorSummary(
    string Operator,
    int CaseCount,
    int Passed,
    int Failed,
    double RuntimeMsAvg,
    long MemoryAllocationBytesAvg,
    bool HasPublicDataset,
    string EvidenceKind,
    string DatasetName);

internal sealed record ScenarioSummary(
    string Scenario,
    int CaseCount,
    int Passed,
    int Failed,
    int GroundTruthCount,
    int PredictedCount,
    int TruePositiveCount,
    int FalsePositiveCount,
    int FalseNegativeCount,
    double F1,
    double MeanPositionErrorPx,
    double RuntimeMsAvg);

internal sealed record CaseResult(
    string CaseId,
    string Scenario,
    bool Passed,
    double RuntimeMs,
    long MemoryAllocationBytes,
    int Width,
    int Height,
    int GroundTruthCount,
    int PredictedCount,
    int TruePositiveCount,
    int FalsePositiveCount,
    int FalseNegativeCount,
    double Precision,
    double Recall,
    double F1,
    double MeanPositionErrorPx,
    double MeanAngleErrorDeg,
    double MeanScaleError,
    IReadOnlyList<PredictionPose> Predictions,
    IReadOnlyList<MatchedPose> Matches,
    string? Failure);

internal static class MarkdownReport
{
    public static string Create(BaselineResult result)
    {
        var lines = new List<string>
        {
            "# ShapeMatching Geometric Dataset Baseline",
            "",
            $"EvidenceKind: `{result.EvidenceKind}`",
            $"GeneratedAtUtc: `{result.Summary.GeneratedAtUtc:O}`",
            $"Dataset: `{result.Summary.DatasetName}`",
            $"DatasetKind: `{result.Summary.DatasetKind}`",
            $"CandidateVersion: `{result.Summary.CandidateVersion}`",
            $"Profile: `{result.Summary.Profile}`",
            "",
            "## Summary",
            "",
            "| Metric | Value |",
            "| --- | ---: |",
            $"| Cases | {result.Summary.CaseCount} |",
            $"| Passed | {result.Summary.Passed} |",
            $"| Failed | {result.Summary.Failed} |",
            $"| Ground truth poses | {result.Summary.GroundTruthCount} |",
            $"| Predicted poses | {result.Summary.PredictedCount} |",
            $"| True positives | {result.Summary.TruePositiveCount} |",
            $"| False positives | {result.Summary.FalsePositiveCount} |",
            $"| False negatives | {result.Summary.FalseNegativeCount} |",
            $"| Precision | {result.Summary.Precision:0.####} |",
            $"| Recall | {result.Summary.Recall:0.####} |",
            $"| F1 | {result.Summary.F1:0.####} |",
            $"| Mean position error px | {result.Summary.MeanPositionErrorPx:0.###} |",
            $"| Mean angle error deg | {result.Summary.MeanAngleErrorDeg:0.###} |",
            $"| Mean scale error | {result.Summary.MeanScaleError:0.###} |",
            $"| Min score | {result.Summary.MinScore:0.####} |",
            $"| Mean score | {result.Summary.MeanScore:0.####} |",
            $"| Position tolerance px | {result.Summary.PositionTolerancePx:0.###} |",
            $"| Angle tolerance deg | {result.Summary.AngleToleranceDeg:0.###} |",
            $"| Scale tolerance | {result.Summary.ScaleTolerance:0.###} |",
            $"| Runtime ms | {result.Summary.RuntimeMs:0.###} |",
            "",
            "## Scenarios",
            "",
            "| Scenario | Cases | Passed | Failed | GT | Pred | TP | FP | FN | F1 | Pos err px | Avg ms |",
            "| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |"
        };

        lines.AddRange(result.Scenarios.Select(item =>
            $"| {item.Scenario} | {item.CaseCount} | {item.Passed} | {item.Failed} | {item.GroundTruthCount} | {item.PredictedCount} | {item.TruePositiveCount} | {item.FalsePositiveCount} | {item.FalseNegativeCount} | {item.F1:0.####} | {item.MeanPositionErrorPx:0.###} | {item.RuntimeMsAvg:0.###} |"));

        lines.AddRange(
        [
            "",
            "## Failure Boundaries",
            "",
            "- `direct_pose` verifies exact translation pose recovery for fixed-size templates.",
            "- `rotated_pose` verifies rotation search over positive angle transforms.",
            "- `scaled_pose` verifies scale search and mixed rotation/scale transforms.",
            "- `multi_target` verifies MaxMatches and non-maximum suppression for two same-pose targets.",
            "- `top_left_origin` verifies reference-origin reporting when OriginMode is TopLeft.",
            "- `blank_negative` verifies empty scenes reject with zero matches and structured no-match reason.",
            "- This bridge records semi-synthetic geometric-scene metrics for the ShapeMatching rotation-scale template path; it is not field-image accuracy evidence.",
            "",
            "## Cases",
            "",
            "| Case | Scenario | Passed | Size | GT | Pred | TP | FP | FN | F1 | Pos err px | Angle err | Scale err | Runtime ms | Failure |",
            "| --- | --- | --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | --- |"
        ]);

        lines.AddRange(result.Cases.Select(item =>
            $"| {item.CaseId} | {item.Scenario} | {item.Passed} | {item.Width}x{item.Height} | {item.GroundTruthCount} | {item.PredictedCount} | {item.TruePositiveCount} | {item.FalsePositiveCount} | {item.FalseNegativeCount} | {item.F1:0.####} | {item.MeanPositionErrorPx:0.###} | {item.MeanAngleErrorDeg:0.###} | {item.MeanScaleError:0.###} | {item.RuntimeMs:0.###} | {item.Failure ?? "-"} |"));

        lines.Add("");
        return string.Join(Environment.NewLine, lines);
    }
}

internal sealed record RunnerOptions(
    string OutputPath,
    string ReportPath,
    string CandidateVersion,
    string Profile,
    IReadOnlySet<string> CaseIds,
    bool ShowHelp,
    string? ParseError)
{
    public bool IncludesCase(ShapeCaseSpec spec) => CaseIds.Count == 0 || CaseIds.Contains(spec.CaseId);

    public static RunnerOptions Parse(string[] args)
    {
        var options = new RunnerOptions(
            "quality/evals/reports/ShapeMatching_dataset_baseline.json",
            "quality/evals/reports/ShapeMatching_dataset_baseline.md",
            "control",
            "baseline_geometric_dataset",
            new HashSet<string>(StringComparer.Ordinal),
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
                "--candidate-version" => options with { CandidateVersion = value },
                "--profile" => options with { Profile = value },
                "--case-ids" => options with { CaseIds = SplitCaseIds(value) },
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
        Usage: dotnet run --project quality/tools/ShapeMatchingGeometricDatasetRunner/ShapeMatchingGeometricDatasetRunner.csproj -- [options]

        Options:
          --output <path>   Baseline JSON output path.
          --report <path>   Baseline Markdown report path.
          --candidate-version <id>  Candidate version label to record.
          --profile <name>          Candidate profile label to record.
          --case-ids <ids>          Comma-separated case ids to execute.
        """);
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
