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

var result = await HPatchesRunner.RunAsync(options);
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(options.OutputPath))!);
await File.WriteAllTextAsync(options.OutputPath, JsonSerializer.Serialize(result, JsonSettings.Indented));

if (!string.IsNullOrWhiteSpace(options.ReportPath))
{
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(options.ReportPath))!);
    await File.WriteAllTextAsync(options.ReportPath, MarkdownReport.Create(result));
}

Console.WriteLine(
    $"HPatches {result.Summary.Operator} complete: {result.Summary.Passed}/{result.Summary.CaseCount} passed, " +
    $"passRate={result.Summary.PassRate:F3}, p95={result.Summary.P95PositionErrorPx:F3}, output={options.OutputPath}");

return result.Summary.Accepted ? 0 : 1;

internal static class HPatchesRunner
{
    public static async Task<BaselineResult> RunAsync(RunnerOptions options)
    {
        var cases = HPatchesIndex.Load(options).ToList();
        var results = new List<CaseResult>(cases.Count);
        foreach (var testCase in cases)
        {
            results.Add(await RunCaseAsync(testCase, options));
        }

        var passed = results.Count(item => item.Passed);
        var errors = results.Select(item => item.PositionErrorPx).Where(double.IsFinite).OrderBy(item => item).ToArray();
        var cornerErrors = results.Select(item => item.MaxCornerErrorPx ?? item.MeanCornerErrorPx).OfType<double>().Where(double.IsFinite).OrderBy(item => item).ToArray();
        var passRate = results.Count == 0 ? 0 : passed / (double)results.Count;
        var p95 = Percentile(errors, 0.95);
        var accepted = results.Count >= Math.Min(options.MaxSequences, 10) &&
            passRate >= options.MinPassRate &&
            p95 <= options.MaxP95PositionErrorPx;

        var summary = new DatasetSummary(
            DateTimeOffset.UtcNow,
            options.Operator,
            "HPatches",
            "public HPatches real-image homography feature matching benchmark",
            options.IndexPath.Replace('\\', '/'),
            results.Count,
            passed,
            results.Count - passed,
            Math.Round(passRate, 6),
            Math.Round(errors.Length == 0 ? 1_000_000 : errors.Average(), 6),
            Math.Round(p95, 6),
            Math.Round(cornerErrors.Length == 0 ? 1_000_000 : Percentile(cornerErrors, 0.95), 6),
            Math.Round(results.Average(item => item.Inliers), 6),
            Math.Round(results.Average(item => item.TotalMatches), 6),
            Math.Round(results.Average(item => item.Score), 6),
            Math.Round(results.Sum(item => item.RuntimeMs), 3),
            results.Sum(item => item.MemoryAllocationBytes),
            options.MinPassRate,
            options.MaxP95PositionErrorPx,
            options.MaxFeatures,
            options.MinInliers,
            options.MatchRatio,
            options.RansacThreshold,
            options.MinInlierRatio,
            options.DetectorType,
            options.ScoreThreshold,
            options.EnableMultiScale,
            options.ScaleRange,
            options.FastThreshold,
            options.EdgeThreshold,
            options.AkazeThreshold,
            options.AllowCenterOnlyProjection,
            accepted);

        return new BaselineResult(
            "dataset",
            summary,
            [
                new OperatorSummary(
                    options.Operator,
                    results.Count,
                    passed,
                    results.Count - passed,
                    Math.Round(passRate, 6),
                    Math.Round(results.Average(item => item.RuntimeMs), 3),
                    true,
                    "dataset",
                    "HPatches")
            ],
            results
                .GroupBy(item => item.SequenceType)
                .OrderBy(item => item.Key, StringComparer.Ordinal)
                .Select(group => new ScenarioSummary(
                    group.Key,
                    group.Count(),
                    group.Count(item => item.Passed),
                    group.Count(item => !item.Passed),
                    Math.Round(group.Count(item => item.Passed) / (double)group.Count(), 6),
                    Math.Round(group.Average(item => item.PositionErrorPx), 6),
                    Math.Round(group.Average(item => item.RuntimeMs), 3)))
                .ToArray(),
            results);
    }

    private static async Task<CaseResult> RunCaseAsync(HPatchesCase testCase, RunnerOptions options)
    {
        var stopwatch = Stopwatch.StartNew();
        var allocationBefore = GC.GetTotalAllocatedBytes(precise: true);
        ImageWrapper? sceneWrapper = null;
        ImageWrapper? templateWrapper = null;
        OperatorExecutionOutput? execution = null;

        try
        {
            using var templateOriginal = Cv2.ImRead(testCase.TemplatePath, ImreadModes.Color);
            using var sceneOriginal = Cv2.ImRead(testCase.ScenePath, ImreadModes.Color);
            if (templateOriginal.Empty() || sceneOriginal.Empty())
            {
                throw new FileNotFoundException($"Unable to read HPatches images for {testCase.CaseId}");
            }

            using var template = ResizeForMaxSide(templateOriginal, options.MaxSide, out var templateScaleX, out var templateScaleY);
            using var scene = ResizeForMaxSide(sceneOriginal, options.MaxSide, out var sceneScaleX, out var sceneScaleY);
            var expected = ProjectCenter(testCase.Homography, templateOriginal.Width, templateOriginal.Height, templateScaleX, templateScaleY, sceneScaleX, sceneScaleY);
            var expectedCorners = ProjectCorners(testCase.Homography, templateOriginal.Width, templateOriginal.Height, sceneScaleX, sceneScaleY);

            templateWrapper = new ImageWrapper(template.Clone());
            sceneWrapper = new ImageWrapper(scene.Clone());
            var op = CreateOperator(options);
            var executor = CreateExecutor(options.Operator);
            execution = await executor.ExecuteAsync(op, new Dictionary<string, object>
            {
                ["Image"] = sceneWrapper,
                ["Template"] = templateWrapper
            });

            stopwatch.Stop();
            var allocationAfter = GC.GetTotalAllocatedBytes(precise: true);
            var isMatch = TryGetBool(execution.OutputData, "IsMatch", out var matchValue) && matchValue;
            var score = TryGetDouble(execution.OutputData, "Score", out var scoreValue) ? scoreValue : 0;
            var inliers = TryGetAnyInt(execution.OutputData, out var inlierValue, "Inliers", "InlierCount") ? inlierValue : 0;
            var totalMatches = TryGetAnyInt(execution.OutputData, out var totalValue, "TotalMatches", "FeatureMatchCount", "MatchCount") ? totalValue : 0;
            var inlierRatio = TryGetFiniteDouble(execution.OutputData, "InlierRatio");
            var meanReprojectionError = TryGetFiniteDouble(execution.OutputData, "MeanReprojectionError");
            var maxReprojectionError = TryGetFiniteDouble(execution.OutputData, "MaxReprojectionError");
            var areaRatio = TryGetFiniteDouble(execution.OutputData, "AreaRatio");
            var cornersInsideCount = TryGetInt(execution.OutputData, "CornersInsideCount", out var cornersInsideValue)
                ? cornersInsideValue
                : CountCornersInside(execution.OutputData, scene.Size());
            var hasActual = TryGetPosition(execution.OutputData, "Position", out var position) ||
                TryGetPosition(execution.OutputData, "Center", out position);
            var actual = hasActual ? position : new Point2d(0, 0);
            var actualCorners = TryGetCorners(execution.OutputData, "Corners", out var corners) ? corners : [];
            var meanCornerError = actualCorners.Count == 4 ? MeanCornerError(expectedCorners, actualCorners) : (double?)null;
            var maxCornerError = actualCorners.Count == 4 ? MaxCornerError(expectedCorners, actualCorners) : (double?)null;
            var projectedCenterInside = TryGetBool(execution.OutputData, "ProjectedCenterInside", out var centerInsideValue)
                ? centerInsideValue
                : hasActual && IsPointInside(actual, scene.Size());
            var homographyFailureReason = TryGetString(execution.OutputData, "HomographyFailureReason") ??
                TryGetString(execution.OutputData, "FailureReason");
            var error = hasActual ? Distance(expected, actual) : 1_000_000;
            var passed = execution.IsSuccess && isMatch && error <= options.PositionTolerancePx && score >= options.MinScore && inliers >= options.MinInliers;
            var failure = passed ? null : FormatFailure(execution, isMatch, score, inliers, totalMatches, error, options);
            ReleaseImageOutputs(execution.OutputData);

            return new CaseResult(
                testCase.CaseId,
                options.Operator,
                testCase.SequenceId,
                testCase.SequenceType,
                testCase.Pair,
                passed,
                Math.Round(stopwatch.Elapsed.TotalMilliseconds, 3),
                Math.Max(0, allocationAfter - allocationBefore),
                Math.Round(expected.X, 3),
                Math.Round(expected.Y, 3),
                Math.Round(actual.X, 3),
                Math.Round(actual.Y, 3),
                Math.Round(error, 6),
                Math.Round(score, 6),
                inliers,
                totalMatches,
                RoundNullable(inlierRatio, 6),
                RoundNullable(meanReprojectionError, 6),
                RoundNullable(maxReprojectionError, 6),
                RoundNullable(areaRatio, 6),
                cornersInsideCount,
                projectedCenterInside,
                RoundNullable(meanCornerError, 6),
                RoundNullable(maxCornerError, 6),
                string.IsNullOrWhiteSpace(homographyFailureReason) ? null : homographyFailureReason,
                failure);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            var allocationAfter = GC.GetTotalAllocatedBytes(precise: true);
            return new CaseResult(
                testCase.CaseId,
                options.Operator,
                testCase.SequenceId,
                testCase.SequenceType,
                testCase.Pair,
                false,
                Math.Round(stopwatch.Elapsed.TotalMilliseconds, 3),
                Math.Max(0, allocationAfter - allocationBefore),
                0,
                0,
                0,
                0,
                1_000_000,
                0,
                0,
                0,
                null,
                null,
                null,
                null,
                0,
                false,
                null,
                null,
                ex.GetBaseException().Message,
                ex.GetBaseException().Message);
        }
        finally
        {
            sceneWrapper?.Dispose();
            templateWrapper?.Dispose();
        }
    }

    private static Operator CreateOperator(RunnerOptions options)
    {
        var type = options.Operator.ToUpperInvariant() switch
        {
            "AKAZEFEATUREMATCH" => OperatorType.AkazeFeatureMatch,
            "ORBFEATUREMATCH" => OperatorType.OrbFeatureMatch,
            "PLANARMATCHING" => OperatorType.PlanarMatching,
            _ => throw new InvalidOperationException($"Unsupported operator: {options.Operator}")
        };
        var op = new Operator(options.Operator, type, 0, 0);
        if (type == OperatorType.PlanarMatching)
        {
            var planarParameters = new Dictionary<string, object?>
            {
                ["TemplatePath"] = string.Empty,
                ["DetectorType"] = options.DetectorType,
                ["MaxFeatures"] = options.MaxFeatures,
                ["ScaleFactor"] = 1.2,
                ["NLevels"] = 8,
                ["MatchRatio"] = options.MatchRatio,
                ["RansacThreshold"] = options.RansacThreshold,
                ["MinMatchCount"] = Math.Max(4, options.MinInliers),
                ["MinInliers"] = Math.Max(4, options.MinInliers),
                ["MinInlierRatio"] = options.MinInlierRatio,
                ["ScoreThreshold"] = options.ScoreThreshold,
                ["UseRoi"] = false,
                ["RoiX"] = 0,
                ["RoiY"] = 0,
                ["RoiWidth"] = 0,
                ["RoiHeight"] = 0,
                ["EnableMultiScale"] = options.EnableMultiScale,
                ["ScaleRange"] = options.ScaleRange,
                ["AllowCenterOnlyProjection"] = options.AllowCenterOnlyProjection,
                ["EnableEarlyExit"] = false
            };

            foreach (var (name, value) in planarParameters)
            {
                op.AddParameter(new Parameter(Guid.NewGuid(), name, name, string.Empty, DataTypeFor(value), value));
            }

            return op;
        }

        var parameters = new Dictionary<string, object?>
        {
            ["TemplatePath"] = string.Empty,
            ["MinMatchCount"] = options.MinInliers,
            ["EnableSymmetryTest"] = true,
            ["MaxFeatures"] = options.MaxFeatures,
            ["MatchRatio"] = options.MatchRatio,
            ["RansacThreshold"] = options.RansacThreshold,
            ["MinInlierRatio"] = options.MinInlierRatio,
            ["AllowCenterOnlyProjection"] = options.AllowCenterOnlyProjection,
            ["OriginMode"] = "Center",
            ["OriginX"] = 0.0,
            ["OriginY"] = 0.0
        };
        if (type == OperatorType.AkazeFeatureMatch)
        {
            parameters["Threshold"] = options.AkazeThreshold;
        }
        else
        {
            parameters["ScaleFactor"] = 1.2;
            parameters["NLevels"] = 8;
            parameters["EdgeThreshold"] = options.EdgeThreshold;
            parameters["FastThreshold"] = options.FastThreshold;
        }

        foreach (var (name, value) in parameters)
        {
            op.AddParameter(new Parameter(Guid.NewGuid(), name, name, string.Empty, DataTypeFor(value), value));
        }

        return op;
    }

    private static OperatorBase CreateExecutor(string operatorName) =>
        operatorName.ToUpperInvariant() switch
        {
            "AKAZEFEATUREMATCH" => new AkazeFeatureMatchOperator(NullLogger<AkazeFeatureMatchOperator>.Instance),
            "ORBFEATUREMATCH" => new OrbFeatureMatchOperator(NullLogger<OrbFeatureMatchOperator>.Instance),
            "PLANARMATCHING" => new PlanarMatchingOperator(NullLogger<PlanarMatchingOperator>.Instance),
            _ => throw new InvalidOperationException($"Unsupported operator: {operatorName}")
        };

    private static Mat ResizeForMaxSide(Mat input, int maxSide, out double scaleX, out double scaleY)
    {
        var max = Math.Max(input.Width, input.Height);
        if (max <= maxSide)
        {
            scaleX = 1;
            scaleY = 1;
            return input.Clone();
        }

        var scale = maxSide / (double)max;
        var output = new Mat();
        Cv2.Resize(input, output, new Size(Math.Round(input.Width * scale), Math.Round(input.Height * scale)));
        scaleX = output.Width / (double)input.Width;
        scaleY = output.Height / (double)input.Height;
        return output;
    }

    private static Point2d ProjectCenter(double[,] h, int width, int height, double templateScaleX, double templateScaleY, double sceneScaleX, double sceneScaleY)
    {
        var sourceX = width / 2.0;
        var sourceY = height / 2.0;
        var denom = h[2, 0] * sourceX + h[2, 1] * sourceY + h[2, 2];
        var projectedX = (h[0, 0] * sourceX + h[0, 1] * sourceY + h[0, 2]) / denom;
        var projectedY = (h[1, 0] * sourceX + h[1, 1] * sourceY + h[1, 2]) / denom;
        return new Point2d(projectedX * sceneScaleX, projectedY * sceneScaleY);
    }

    private static IReadOnlyList<Point2d> ProjectCorners(double[,] h, int width, int height, double sceneScaleX, double sceneScaleY) =>
    [
        ProjectPoint(h, 0, 0, sceneScaleX, sceneScaleY),
        ProjectPoint(h, width, 0, sceneScaleX, sceneScaleY),
        ProjectPoint(h, width, height, sceneScaleX, sceneScaleY),
        ProjectPoint(h, 0, height, sceneScaleX, sceneScaleY)
    ];

    private static Point2d ProjectPoint(double[,] h, double sourceX, double sourceY, double sceneScaleX, double sceneScaleY)
    {
        var denom = h[2, 0] * sourceX + h[2, 1] * sourceY + h[2, 2];
        var projectedX = (h[0, 0] * sourceX + h[0, 1] * sourceY + h[0, 2]) / denom;
        var projectedY = (h[1, 0] * sourceX + h[1, 1] * sourceY + h[1, 2]) / denom;
        return new Point2d(projectedX * sceneScaleX, projectedY * sceneScaleY);
    }

    private static bool TryGetBool(IReadOnlyDictionary<string, object>? output, string key, out bool value)
    {
        value = false;
        return output is not null && output.TryGetValue(key, out var obj) && bool.TryParse(obj?.ToString(), out value);
    }

    private static bool TryGetInt(IReadOnlyDictionary<string, object>? output, string key, out int value)
    {
        value = 0;
        if (output is null || !output.TryGetValue(key, out var obj))
        {
            return false;
        }

        try
        {
            value = Convert.ToInt32(obj);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryGetAnyInt(IReadOnlyDictionary<string, object>? output, out int value, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (TryGetInt(output, key, out value))
            {
                return true;
            }
        }

        value = 0;
        return false;
    }

    private static bool TryGetDouble(IReadOnlyDictionary<string, object>? output, string key, out double value)
    {
        value = 0;
        if (output is null || !output.TryGetValue(key, out var obj))
        {
            return false;
        }

        try
        {
            value = Convert.ToDouble(obj);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static double? TryGetFiniteDouble(IReadOnlyDictionary<string, object>? output, string key)
    {
        return TryGetDouble(output, key, out var value) && double.IsFinite(value) ? value : null;
    }

    private static double? RoundNullable(double? value, int digits)
    {
        return value.HasValue ? Math.Round(value.Value, digits) : null;
    }

    private static string? TryGetString(IReadOnlyDictionary<string, object>? output, string key)
    {
        if (output is null || !output.TryGetValue(key, out var obj))
        {
            return null;
        }

        return obj?.ToString();
    }

    private static bool TryGetPosition(IReadOnlyDictionary<string, object>? output, string key, out Point2d value)
    {
        value = default;
        if (output is null || !output.TryGetValue(key, out var obj))
        {
            return false;
        }

        return TryConvertPoint(obj, out value);
    }

    private static bool TryGetCorners(IReadOnlyDictionary<string, object>? output, string key, out IReadOnlyList<Point2d> corners)
    {
        var values = new List<Point2d>(4);
        corners = values;
        if (output is null || !output.TryGetValue(key, out var obj) || obj is string)
        {
            return false;
        }

        if (obj is System.Collections.IEnumerable enumerable)
        {
            foreach (var item in enumerable)
            {
                if (TryConvertPoint(item, out var point))
                {
                    values.Add(point);
                }
            }
        }

        return values.Count == 4;
    }

    private static bool TryConvertPoint(object? obj, out Point2d value)
    {
        value = default;
        switch (obj)
        {
            case Position position:
                value = new Point2d(position.X, position.Y);
                return true;
            case Point2d point:
                value = point;
                return true;
            case Point2f point:
                value = new Point2d(point.X, point.Y);
                return true;
            case Point point:
                value = new Point2d(point.X, point.Y);
                return true;
            default:
                return false;
        }
    }

    private static int CountCornersInside(IReadOnlyDictionary<string, object>? output, Size sceneSize)
    {
        if (output is null || !output.TryGetValue("Corners", out var obj))
        {
            return 0;
        }

        var count = 0;
        if (obj is System.Collections.IEnumerable enumerable && obj is not string)
        {
            foreach (var item in enumerable)
            {
                if (TryConvertPoint(item, out var point) && IsPointInside(point, sceneSize))
                {
                    count++;
                }
            }
        }

        return count;
    }

    private static bool IsPointInside(Point2d point, Size sceneSize)
    {
        return double.IsFinite(point.X) &&
               double.IsFinite(point.Y) &&
               point.X >= -1 &&
               point.Y >= -1 &&
               point.X <= sceneSize.Width + 1 &&
               point.Y <= sceneSize.Height + 1;
    }

    private static void ReleaseImageOutputs(Dictionary<string, object>? outputData)
    {
        if (outputData is null)
        {
            return;
        }

        foreach (var image in outputData.Values.OfType<ImageWrapper>())
        {
            image.Release();
        }

        foreach (var mat in outputData.Values.OfType<Mat>())
        {
            mat.Dispose();
        }
    }

    private static string FormatFailure(OperatorExecutionOutput? execution, bool isMatch, double score, int inliers, int totalMatches, double error, RunnerOptions options)
    {
        if (execution is not null && !execution.IsSuccess)
        {
            return execution.ErrorMessage ?? "execution failed";
        }

        return $"isMatch={isMatch}, score={score:0.###}, inliers={inliers}, totalMatches={totalMatches}, error={error:0.###}, tolerance={options.PositionTolerancePx:0.###}";
    }

    private static double Distance(Point2d a, Point2d b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private static double MeanCornerError(IReadOnlyList<Point2d> expected, IReadOnlyList<Point2d> actual) =>
        expected.Zip(actual, Distance).Average();

    private static double MaxCornerError(IReadOnlyList<Point2d> expected, IReadOnlyList<Point2d> actual) =>
        expected.Zip(actual, Distance).Max();

    private static double Percentile(double[] values, double percentile)
    {
        if (values.Length == 0)
        {
            return 1_000_000;
        }

        var index = Math.Clamp((int)Math.Ceiling(values.Length * percentile) - 1, 0, values.Length - 1);
        return values[index];
    }

    private static string DataTypeFor(object? value) => value switch
    {
        bool => "bool",
        int => "int",
        double or float or decimal => "double",
        _ => "string"
    };
}

internal static class HPatchesIndex
{
    public static IEnumerable<HPatchesCase> Load(RunnerOptions options)
    {
        var repoRoot = FindRepoRoot();
        var indexPath = ResolveRepoPath(repoRoot, options.IndexPath);
        using var document = JsonDocument.Parse(File.ReadAllText(indexPath));
        var count = 0;
        foreach (var record in document.RootElement.GetProperty("records").EnumerateArray())
        {
            if (count >= options.MaxSequences)
            {
                yield break;
            }

            var sequenceType = record.GetProperty("sequence_type").GetString() ?? "unknown";
            if (!options.IncludeIllumination && sequenceType == "illumination")
            {
                continue;
            }

            if (!options.IncludeViewpoint && sequenceType == "viewpoint")
            {
                continue;
            }

            var images = record.GetProperty("images").EnumerateArray().Select(item => ResolveRepoPath(repoRoot, item.GetString() ?? "")).ToArray();
            var homographies = record.GetProperty("homographies").EnumerateArray().Select(item => ResolveRepoPath(repoRoot, item.GetString() ?? "")).ToArray();
            if (images.Length < 2 || homographies.Length < 1)
            {
                continue;
            }

            var pairIndex = Math.Clamp(options.PairIndex, 2, Math.Min(images.Length, homographies.Length + 1));
            var caseId = $"{record.GetProperty("id").GetString()}_1_{pairIndex}";
            if (options.CaseIds.Count > 0 && !options.CaseIds.Contains(caseId))
            {
                continue;
            }

            count++;
            yield return new HPatchesCase(
                caseId,
                record.GetProperty("id").GetString() ?? "",
                sequenceType,
                $"1-{pairIndex}",
                images[0],
                images[pairIndex - 1],
                LoadHomography(homographies[pairIndex - 2]));
        }
    }

    private static double[,] LoadHomography(string path)
    {
        var values = File.ReadAllLines(path)
            .SelectMany(line => line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries))
            .Select(value => double.Parse(value, System.Globalization.CultureInfo.InvariantCulture))
            .ToArray();
        if (values.Length != 9)
        {
            throw new InvalidDataException($"Invalid HPatches homography file: {path}");
        }

        var h = new double[3, 3];
        for (var i = 0; i < 9; i++)
        {
            h[i / 3, i % 3] = values[i];
        }

        return h;
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(Environment.CurrentDirectory);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, ".git")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return Environment.CurrentDirectory;
    }

    private static string ResolveRepoPath(string repoRoot, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException("HPatches path must not be empty.");
        }

        return Path.IsPathRooted(value) ? value : Path.GetFullPath(Path.Combine(repoRoot, value));
    }
}

internal sealed record HPatchesCase(string CaseId, string SequenceId, string SequenceType, string Pair, string TemplatePath, string ScenePath, double[,] Homography);
internal sealed record BaselineResult(string EvidenceKind, DatasetSummary Summary, IReadOnlyList<OperatorSummary> Operators, IReadOnlyList<ScenarioSummary> Scenarios, IReadOnlyList<CaseResult> Cases);
internal sealed record DatasetSummary(DateTimeOffset GeneratedAtUtc, string Operator, string DatasetName, string DatasetKind, string IndexPath, int CaseCount, int Passed, int Failed, double PassRate, double MeanPositionErrorPx, double P95PositionErrorPx, double P95CornerErrorPx, double MeanInliers, double MeanTotalMatches, double MeanScore, double RuntimeMs, long MemoryAllocationBytes, double MinPassRate, double MaxP95PositionErrorPx, int MaxFeatures, int MinInliers, double MatchRatio, double RansacThreshold, double MinInlierRatio, string DetectorType, double ScoreThreshold, bool EnableMultiScale, double ScaleRange, int FastThreshold, int EdgeThreshold, double AkazeThreshold, bool AllowCenterOnlyProjection, bool Accepted);
internal sealed record OperatorSummary(string Operator, int CaseCount, int Passed, int Failed, double PassRate, double RuntimeMsAvg, bool HasPublicDataset, string EvidenceKind, string DatasetName);
internal sealed record ScenarioSummary(string Scenario, int CaseCount, int Passed, int Failed, double PassRate, double MeanPositionErrorPx, double RuntimeMsAvg);
internal sealed record CaseResult(string CaseId, string Operator, string SequenceId, string SequenceType, string Pair, bool Passed, double RuntimeMs, long MemoryAllocationBytes, double ExpectedX, double ExpectedY, double ActualX, double ActualY, double PositionErrorPx, double Score, int Inliers, int TotalMatches, double? InlierRatio, double? MeanReprojectionError, double? MaxReprojectionError, double? AreaRatio, int CornersInsideCount, bool ProjectedCenterInside, double? MeanCornerErrorPx, double? MaxCornerErrorPx, string? HomographyFailureReason, string? Failure);

internal static class MarkdownReport
{
    public static string Create(BaselineResult result)
    {
        var lines = new List<string>
        {
            "# HPatches Feature Match Dataset Baseline",
            "",
            $"EvidenceKind: `{result.EvidenceKind}`",
            $"GeneratedAtUtc: `{result.Summary.GeneratedAtUtc:O}`",
            $"Operator: `{result.Summary.Operator}`",
            $"Dataset: `{result.Summary.DatasetName}`",
            $"DatasetKind: `{result.Summary.DatasetKind}`",
            $"Accepted: `{result.Summary.Accepted}`",
            "",
            "## Summary",
            "",
            "| Metric | Value |",
            "| --- | ---: |",
            $"| Cases | {result.Summary.CaseCount} |",
            $"| Passed | {result.Summary.Passed} |",
            $"| Failed | {result.Summary.Failed} |",
            $"| Pass rate | {result.Summary.PassRate:0.####} |",
            $"| Mean position error px | {result.Summary.MeanPositionErrorPx:0.###} |",
            $"| P95 position error px | {result.Summary.P95PositionErrorPx:0.###} |",
            $"| P95 corner error px | {result.Summary.P95CornerErrorPx:0.###} |",
            $"| Mean inliers | {result.Summary.MeanInliers:0.###} |",
            $"| Mean score | {result.Summary.MeanScore:0.####} |",
            $"| Runtime ms | {result.Summary.RuntimeMs:0.###} |",
            $"| Max features | {result.Summary.MaxFeatures} |",
            $"| Min inliers | {result.Summary.MinInliers} |",
            $"| Match ratio | {result.Summary.MatchRatio:0.###} |",
            $"| RANSAC threshold px | {result.Summary.RansacThreshold:0.###} |",
            $"| Min inlier ratio | {result.Summary.MinInlierRatio:0.###} |",
            $"| Detector type | {result.Summary.DetectorType} |",
            $"| Score threshold | {result.Summary.ScoreThreshold:0.###} |",
            $"| Multi-scale | {result.Summary.EnableMultiScale} |",
            $"| Scale range | {result.Summary.ScaleRange:0.###} |",
            $"| ORB FAST threshold | {result.Summary.FastThreshold} |",
            $"| ORB edge threshold | {result.Summary.EdgeThreshold} |",
            $"| AKAZE detector threshold | {result.Summary.AkazeThreshold:0.######} |",
            $"| Allow center-only projection | {result.Summary.AllowCenterOnlyProjection} |",
            "",
            "## Cases",
            "",
            "| Case | Type | Pair | Passed | Error px | Mean corner px | Max corner px | Score | Inliers | Inlier ratio | Mean reproj | Max reproj | Area ratio | Corners in | Center in | Runtime ms | Homography failure | Failure |",
            "| --- | --- | --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | --- | ---: | --- | --- |"
        };

        lines.AddRange(result.Cases.Select(item =>
            $"| {item.CaseId} | {item.SequenceType} | {item.Pair} | {item.Passed} | {item.PositionErrorPx:0.###} | {FormatOptional(item.MeanCornerErrorPx)} | {FormatOptional(item.MaxCornerErrorPx)} | {item.Score:0.####} | {item.Inliers}/{item.TotalMatches} | {FormatOptional(item.InlierRatio)} | {FormatOptional(item.MeanReprojectionError)} | {FormatOptional(item.MaxReprojectionError)} | {FormatOptional(item.AreaRatio)} | {item.CornersInsideCount} | {item.ProjectedCenterInside} | {item.RuntimeMs:0.###} | {item.HomographyFailureReason ?? "-"} | {item.Failure ?? "-"} |"));
        lines.Add("");
        return string.Join(Environment.NewLine, lines);
    }

    private static string FormatOptional(double? value)
    {
        return value.HasValue ? value.Value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) : "-";
    }
}

internal sealed record RunnerOptions(
    string Operator,
    string IndexPath,
    string OutputPath,
    string ReportPath,
    int MaxSequences,
    int PairIndex,
    int MaxSide,
    int MaxFeatures,
    int MinInliers,
    double MinScore,
    double PositionTolerancePx,
    double MinPassRate,
    double MaxP95PositionErrorPx,
    double MatchRatio,
    double RansacThreshold,
    double MinInlierRatio,
    string DetectorType,
    double ScoreThreshold,
    bool EnableMultiScale,
    double ScaleRange,
    int FastThreshold,
    int EdgeThreshold,
    double AkazeThreshold,
    bool AllowCenterOnlyProjection,
    bool IncludeIllumination,
    bool IncludeViewpoint,
    IReadOnlySet<string> CaseIds,
    bool ShowHelp,
    string? ParseError)
{
    public static RunnerOptions Parse(string[] args)
    {
        var options = new RunnerOptions(
            "AkazeFeatureMatch",
            "quality/datasets/hpatches_index.json",
            "quality/evals/reports/AkazeFeatureMatch_hpatches_baseline.json",
            "quality/evals/reports/AkazeFeatureMatch_hpatches_baseline.md",
            80,
            2,
            800,
            1200,
            6,
            0.05,
            35,
            0.35,
            60,
            0.75,
            5.0,
            0.25,
            "ORB",
            0.5,
            true,
            0.2,
            20,
            15,
            0.001,
            false,
            true,
            true,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            false,
            null);

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg is "-h" or "--help")
            {
                return options with { ShowHelp = true };
            }

            if (arg is "--illumination-only")
            {
                options = options with { IncludeIllumination = true, IncludeViewpoint = false };
                continue;
            }

            if (arg is "--viewpoint-only")
            {
                options = options with { IncludeIllumination = false, IncludeViewpoint = true };
                continue;
            }

            if (arg is "--disable-multiscale")
            {
                options = options with { EnableMultiScale = false };
                continue;
            }

            if (arg is "--enable-multiscale")
            {
                options = options with { EnableMultiScale = true };
                continue;
            }

            if (arg is "--allow-center-only-projection")
            {
                options = options with { AllowCenterOnlyProjection = true };
                continue;
            }

            if (arg is "--disable-center-only-projection")
            {
                options = options with { AllowCenterOnlyProjection = false };
                continue;
            }

            if (i + 1 >= args.Length)
            {
                return options with { ParseError = $"Missing value for {arg}" };
            }

            var value = args[++i];
            options = arg switch
            {
                "--operator" => IsSupportedOperator(value)
                    ? options with { Operator = NormalizeOperator(value) }
                    : options with { ParseError = "--operator must be AkazeFeatureMatch, OrbFeatureMatch, or PlanarMatching." },
                "--index" => options with { IndexPath = value },
                "--output" => options with { OutputPath = value },
                "--report" => options with { ReportPath = value },
                "--max-sequences" => int.TryParse(value, out var maxSequences) && maxSequences > 0 ? options with { MaxSequences = maxSequences } : options with { ParseError = "--max-sequences must be positive." },
                "--pair-index" => int.TryParse(value, out var pairIndex) && pairIndex is >= 2 and <= 6 ? options with { PairIndex = pairIndex } : options with { ParseError = "--pair-index must be 2..6." },
                "--max-side" => int.TryParse(value, out var maxSide) && maxSide >= 128 ? options with { MaxSide = maxSide } : options with { ParseError = "--max-side must be >= 128." },
                "--max-features" => int.TryParse(value, out var maxFeatures) && maxFeatures is >= 100 and <= 5000 ? options with { MaxFeatures = maxFeatures } : options with { ParseError = "--max-features must be 100..5000." },
                "--min-inliers" => int.TryParse(value, out var minInliers) && minInliers is >= 3 and <= 100 ? options with { MinInliers = minInliers } : options with { ParseError = "--min-inliers must be 3..100." },
                "--min-score" => double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var minScore) ? options with { MinScore = minScore } : options with { ParseError = "--min-score must be numeric." },
                "--position-tolerance-px" => double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var tolerance) ? options with { PositionTolerancePx = tolerance } : options with { ParseError = "--position-tolerance-px must be numeric." },
                "--min-pass-rate" => double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var passRate) ? options with { MinPassRate = passRate } : options with { ParseError = "--min-pass-rate must be numeric." },
                "--max-p95-position-error-px" => double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var p95) ? options with { MaxP95PositionErrorPx = p95 } : options with { ParseError = "--max-p95-position-error-px must be numeric." },
                "--match-ratio" => double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var matchRatio) && matchRatio is >= 0.5 and <= 0.95 ? options with { MatchRatio = matchRatio } : options with { ParseError = "--match-ratio must be 0.5..0.95." },
                "--ransac-threshold" => double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var ransacThreshold) && ransacThreshold is >= 0.5 and <= 10.0 ? options with { RansacThreshold = ransacThreshold } : options with { ParseError = "--ransac-threshold must be 0.5..10.0." },
                "--min-inlier-ratio" => double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var minInlierRatio) && minInlierRatio is >= 0.1 and <= 1.0 ? options with { MinInlierRatio = minInlierRatio } : options with { ParseError = "--min-inlier-ratio must be 0.1..1.0." },
                "--detector-type" => TryNormalizeDetectorType(value, out var detectorType) ? options with { DetectorType = detectorType } : options with { ParseError = "--detector-type must be ORB, AKAZE, or BRISK." },
                "--score-threshold" => double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var scoreThreshold) && scoreThreshold is >= 0.0 and <= 1.0 ? options with { ScoreThreshold = scoreThreshold } : options with { ParseError = "--score-threshold must be 0.0..1.0." },
                "--scale-range" => double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var scaleRange) && scaleRange is >= 0.0 and <= 1.0 ? options with { ScaleRange = scaleRange } : options with { ParseError = "--scale-range must be 0.0..1.0." },
                "--fast-threshold" => int.TryParse(value, out var fastThreshold) && fastThreshold is >= 1 and <= 100 ? options with { FastThreshold = fastThreshold } : options with { ParseError = "--fast-threshold must be 1..100." },
                "--edge-threshold" => int.TryParse(value, out var edgeThreshold) && edgeThreshold is >= 3 and <= 100 ? options with { EdgeThreshold = edgeThreshold } : options with { ParseError = "--edge-threshold must be 3..100." },
                "--akaze-threshold" => double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var akazeThreshold) && akazeThreshold is >= 0.0001 and <= 0.1 ? options with { AkazeThreshold = akazeThreshold } : options with { ParseError = "--akaze-threshold must be 0.0001..0.1." },
                "--case-ids" => options with { CaseIds = ParseCaseIds(value) },
                _ => options with { ParseError = $"Unknown argument: {arg}" }
            };

            if (options.ParseError is not null)
            {
                return options;
            }
        }

        if (options.Operator == "OrbFeatureMatch" && options.OutputPath.EndsWith("AkazeFeatureMatch_hpatches_baseline.json", StringComparison.OrdinalIgnoreCase))
        {
            options = options with
            {
                OutputPath = "quality/evals/reports/OrbFeatureMatch_hpatches_baseline.json",
                ReportPath = "quality/evals/reports/OrbFeatureMatch_hpatches_baseline.md",
                MinPassRate = 0.2
            };
        }

        if (options.Operator == "PlanarMatching" && options.OutputPath.EndsWith("AkazeFeatureMatch_hpatches_baseline.json", StringComparison.OrdinalIgnoreCase))
        {
            options = options with
            {
                OutputPath = "quality/evals/reports/PlanarMatching_hpatches_baseline.json",
                ReportPath = "quality/evals/reports/PlanarMatching_hpatches_baseline.md",
                MinPassRate = 0.2
            };
        }

        return options;
    }

    private static bool IsSupportedOperator(string value) =>
        value.Equals("AkazeFeatureMatch", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("OrbFeatureMatch", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("PlanarMatching", StringComparison.OrdinalIgnoreCase);

    private static string NormalizeOperator(string value)
    {
        if (value.Equals("AkazeFeatureMatch", StringComparison.OrdinalIgnoreCase))
        {
            return "AkazeFeatureMatch";
        }

        if (value.Equals("OrbFeatureMatch", StringComparison.OrdinalIgnoreCase))
        {
            return "OrbFeatureMatch";
        }

        return "PlanarMatching";
    }

    private static bool TryNormalizeDetectorType(string value, out string detectorType)
    {
        detectorType = value.Trim().ToUpperInvariant();
        return detectorType is "ORB" or "AKAZE" or "BRISK";
    }

    private static IReadOnlySet<string> ParseCaseIds(string value)
    {
        return value
            .Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public static void PrintHelp()
    {
        Console.WriteLine("Usage: dotnet run --project quality/tools/HPatchesFeatureMatchDatasetRunner/HPatchesFeatureMatchDatasetRunner.csproj -- --operator AkazeFeatureMatch|OrbFeatureMatch|PlanarMatching --index quality/datasets/hpatches_index.json --output <json> --report <md> [--viewpoint-only] [--detector-type ORB] [--score-threshold 0.5] [--match-ratio 0.75] [--ransac-threshold 5.0] [--min-inlier-ratio 0.25] [--fast-threshold 20] [--edge-threshold 15] [--akaze-threshold 0.001] [--allow-center-only-projection] [--case-ids id1,id2]");
    }
}

internal static class JsonSettings
{
    public static readonly JsonSerializerOptions Indented = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = null
    };
}
