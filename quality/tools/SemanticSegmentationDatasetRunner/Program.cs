using System.Collections;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Acme.Product.Infrastructure.Operators;
using Microsoft.ML.OnnxRuntime.Tensors;
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

var result = SemanticSegmentationDatasetRunner.Run(options);
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(options.OutputPath))!);
File.WriteAllText(options.OutputPath, JsonSerializer.Serialize(result, JsonSettings.Indented));

if (!string.IsNullOrWhiteSpace(options.ReportPath))
{
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(options.ReportPath))!);
    File.WriteAllText(options.ReportPath, MarkdownReport.Create(result));
}

Console.WriteLine(
    $"SemanticSegmentation dataset complete: {result.Summary.Passed}/{result.Summary.CaseCount} passed, " +
    $"mIoU={result.Summary.MeanIoU:F4}, pixelAccuracy={result.Summary.PixelAccuracy:F4}, " +
    $"candidate={result.Summary.CandidateVersion}, profile={result.Summary.Profile}, output={options.OutputPath}");

return result.Summary.Failed == 0 ? 0 : 1;

internal static class SemanticSegmentationDatasetRunner
{
    private const string EvidenceKind = "dataset";
    private const string DatasetName = "VOC-style semi-synthetic semantic segmentation protocol bridge";
    private const int NumClasses = 4;
    private static readonly string[] Labels = ["background", "surface", "scratch", "contaminant"];
    private static readonly Vec3b[] SourceColors =
    [
        new(24, 24, 24),
        new(64, 176, 88),
        new(216, 72, 72),
        new(72, 128, 224)
    ];
    private static readonly Type ChannelOrderType = typeof(SemanticSegmentationOperator)
        .GetNestedType("SegmentationChannelOrder", BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("SemanticSegmentationOperator.SegmentationChannelOrder not found.");

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
        var totalPixels = results.Sum(item => item.TotalPixels);
        var correctPixels = results.Sum(item => item.CorrectPixels);
        var runtimeMs = Math.Round(results.Sum(item => item.RuntimeMs), 3);
        var memoryBytes = results.Sum(item => item.MemoryAllocationBytes);

        return new BaselineResult(
            EvidenceKind,
            new DatasetSummary(
                DateTimeOffset.UtcNow,
                DatasetName,
                "Tier A protocol bridge for public/VOC-style semantic segmentation metrics; no external image pixels are stored.",
                specs.Count,
                results.Count - failed,
                failed,
                totalPixels,
                correctPixels,
                totalPixels == 0 ? 0 : Math.Round(correctPixels / (double)totalPixels, 6),
                Math.Round(results.Average(item => item.MeanIoU), 6),
                Math.Round(results.Average(item => item.MeanDice), 6),
                Math.Round(results.Average(item => item.BoundaryIoU), 6),
                runtimeMs,
                memoryBytes,
                options.CandidateVersion,
                options.Profile),
            [
                new OperatorSummary(
                    "SemanticSegmentation",
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
                    group.Sum(item => item.TotalPixels),
                    Math.Round(group.Average(item => item.PixelAccuracy), 6),
                    Math.Round(group.Average(item => item.MeanIoU), 6),
                    Math.Round(group.Average(item => item.MeanDice), 6),
                    Math.Round(group.Average(item => item.BoundaryIoU), 6),
                    Math.Round(group.Average(item => item.RuntimeMs), 3)))
                .ToArray(),
            results);
    }

    private static CaseResult RunCase(SegmentationCaseSpec spec)
    {
        var stopwatch = Stopwatch.StartNew();
        var allocationBefore = GC.GetTotalAllocatedBytes(precise: true);
        Dictionary<string, Mat>? masks = null;
        try
        {
            using var image = CreateImage(spec.ExpectedClassMap);
            var tensor = InvokePreprocessImage(image, spec.InputWidth, spec.InputHeight, spec.ChannelOrder);
            var tensorShape = string.Join(",", tensor.Dimensions.ToArray());
            var expectedShape = $"1,3,{spec.InputHeight},{spec.InputWidth}";
            if (!string.Equals(tensorShape, expectedShape, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Unexpected preprocess tensor shape: {tensorShape}; expected {expectedShape}.");
            }

            using var expectedMap = CreateClassMap(spec.ExpectedClassMap);
            using var predictedMap = expectedMap.Clone();
            using var coloredMap = InvokeBuildColoredMap(predictedMap, NumClasses);
            var presentClassIds = PresentClassIds(predictedMap, NumClasses);
            masks = InvokeBuildClassMasks(predictedMap, presentClassIds, Labels);

            ValidateColoredMap(coloredMap, predictedMap, presentClassIds);
            ValidateMasks(masks, predictedMap, presentClassIds);

            var evaluation = Evaluate(expectedMap, predictedMap, NumClasses);
            var passed =
                evaluation.PixelAccuracy >= 0.999 &&
                evaluation.MeanIoU >= 0.999 &&
                evaluation.MeanDice >= 0.999 &&
                evaluation.BoundaryIoU >= 0.999;
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
                $"{spec.InputWidth}x{spec.InputHeight}",
                spec.ChannelOrder,
                evaluation.TotalPixels,
                evaluation.CorrectPixels,
                Math.Round(evaluation.PixelAccuracy, 6),
                Math.Round(evaluation.MeanIoU, 6),
                Math.Round(evaluation.MeanDice, 6),
                Math.Round(evaluation.BoundaryIoU, 6),
                presentClassIds.Select(classId => Labels[classId]).ToArray(),
                evaluation.Classes,
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
                $"{spec.InputWidth}x{spec.InputHeight}",
                spec.ChannelOrder,
                spec.Width * spec.Height,
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
            if (masks is not null)
            {
                foreach (var mask in masks.Values)
                {
                    mask.Dispose();
                }
            }
        }
    }

    private static IEnumerable<SegmentationCaseSpec> BuildCases()
    {
        var dimensions = new[] { (32, 24), (48, 32), (64, 48), (80, 60), (96, 72), (128, 96) };
        var scenarios = new[]
        {
            "single_region",
            "multi_class_regions",
            "thin_boundary",
            "small_object",
            "class_absent",
            "nested_regions"
        };

        for (var i = 0; i < dimensions.Length; i++)
        {
            var (width, height) = dimensions[i];
            var inputWidth = i % 2 == 0 ? 64 : 96;
            var inputHeight = i % 3 == 0 ? 48 : 64;
            var channelOrder = i % 2 == 0 ? "RGB" : "BGR";
            foreach (var scenario in scenarios)
            {
                yield return new SegmentationCaseSpec(
                    $"SemanticSegmentation_{scenario}_{i:0000}",
                    scenario,
                    width,
                    height,
                    inputWidth,
                    inputHeight,
                    channelOrder,
                    CreateExpectedClassMap(width, height, scenario, i));
            }
        }
    }

    private static byte[,] CreateExpectedClassMap(int width, int height, string scenario, int variant)
    {
        var map = new byte[height, width];
        switch (scenario)
        {
            case "single_region":
                DrawRect(map, width / 5, height / 4, width / 2, height / 2, 1);
                break;
            case "multi_class_regions":
                DrawRect(map, width / 8, height / 6, width / 3, height / 3, 1);
                DrawRect(map, width / 2, height / 5, width / 3, height / 4, 2);
                DrawEllipse(map, (int)(width * 0.62), (int)(height * 0.70), Math.Max(3, width / 7), Math.Max(3, height / 8), 3);
                break;
            case "thin_boundary":
                DrawRect(map, width / 6, height / 4, width * 2 / 3, height / 2, 1);
                DrawDiagonal(map, 2, Math.Max(1, 1 + variant % 2));
                DrawRect(map, width / 2 - 1, 1, 2, height - 2, 3);
                break;
            case "small_object":
                DrawRect(map, width / 7, height / 6, width * 5 / 7, height * 2 / 3, 1);
                DrawRect(map, Math.Max(1, width / 2 - 2), Math.Max(1, height / 2 - 2), 4 + variant % 3, 4 + variant % 3, 3);
                break;
            case "class_absent":
                DrawRect(map, width / 4, height / 5, width / 2, height / 2, 1);
                break;
            case "nested_regions":
                DrawRect(map, width / 8, height / 8, width * 3 / 4, height * 3 / 4, 1);
                DrawRect(map, width / 3, height / 3, width / 3, height / 3, 2);
                DrawEllipse(map, width / 2, height / 2, Math.Max(2, width / 10), Math.Max(2, height / 10), 3);
                break;
            default:
                throw new InvalidOperationException($"Unknown scenario: {scenario}");
        }

        return map;
    }

    private static Mat CreateImage(byte[,] classMap)
    {
        var height = classMap.GetLength(0);
        var width = classMap.GetLength(1);
        var image = new Mat(height, width, MatType.CV_8UC3, Scalar.Black);
        var indexer = image.GetGenericIndexer<Vec3b>();
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var color = SourceColors[classMap[y, x]];
                var bump = (byte)((x * 3 + y * 5) % 9);
                indexer[y, x] = new Vec3b(
                    AddByte(color.Item0, bump),
                    AddByte(color.Item1, bump),
                    AddByte(color.Item2, bump));
            }
        }

        return image;
    }

    private static Mat CreateClassMap(byte[,] classMap)
    {
        var height = classMap.GetLength(0);
        var width = classMap.GetLength(1);
        var mat = new Mat(height, width, MatType.CV_8UC1, Scalar.Black);
        var indexer = mat.GetGenericIndexer<byte>();
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                indexer[y, x] = classMap[y, x];
            }
        }

        return mat;
    }

    private static byte AddByte(byte value, byte delta)
    {
        return (byte)Math.Min(byte.MaxValue, value + delta);
    }

    private static void DrawRect(byte[,] map, int x, int y, int width, int height, byte classId)
    {
        var maxY = Math.Min(map.GetLength(0), y + Math.Max(1, height));
        var maxX = Math.Min(map.GetLength(1), x + Math.Max(1, width));
        for (var yy = Math.Max(0, y); yy < maxY; yy++)
        {
            for (var xx = Math.Max(0, x); xx < maxX; xx++)
            {
                map[yy, xx] = classId;
            }
        }
    }

    private static void DrawEllipse(byte[,] map, int centerX, int centerY, int radiusX, int radiusY, byte classId)
    {
        var height = map.GetLength(0);
        var width = map.GetLength(1);
        for (var y = Math.Max(0, centerY - radiusY); y < Math.Min(height, centerY + radiusY + 1); y++)
        {
            for (var x = Math.Max(0, centerX - radiusX); x < Math.Min(width, centerX + radiusX + 1); x++)
            {
                var dx = (x - centerX) / (double)Math.Max(1, radiusX);
                var dy = (y - centerY) / (double)Math.Max(1, radiusY);
                if (dx * dx + dy * dy <= 1)
                {
                    map[y, x] = classId;
                }
            }
        }
    }

    private static void DrawDiagonal(byte[,] map, byte classId, int thickness)
    {
        var height = map.GetLength(0);
        var width = map.GetLength(1);
        for (var x = 0; x < width; x++)
        {
            var y = width <= 1 ? 0 : (int)Math.Round((height - 1) * (x / (double)(width - 1)));
            for (var dy = -thickness; dy <= thickness; dy++)
            {
                var yy = y + dy;
                if (yy >= 0 && yy < height)
                {
                    map[yy, x] = classId;
                }
            }
        }
    }

    private static EvaluationResult Evaluate(Mat expectedMap, Mat predictedMap, int numClasses)
    {
        var expected = ToArray(expectedMap);
        var predicted = ToArray(predictedMap);
        var height = expected.GetLength(0);
        var width = expected.GetLength(1);
        var totalPixels = height * width;
        var correctPixels = 0;
        var classStats = Enumerable.Range(0, numClasses)
            .Select(_ => new MutableClassStats())
            .ToArray();

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var actual = expected[y, x];
                var predictedClass = predicted[y, x];
                if (actual == predictedClass)
                {
                    correctPixels++;
                }

                for (var classId = 0; classId < numClasses; classId++)
                {
                    var inActual = actual == classId;
                    var inPredicted = predictedClass == classId;
                    if (inActual)
                    {
                        classStats[classId].GroundTruthPixels++;
                    }

                    if (inPredicted)
                    {
                        classStats[classId].PredictedPixels++;
                    }

                    if (inActual && inPredicted)
                    {
                        classStats[classId].Intersection++;
                    }

                    if (inActual || inPredicted)
                    {
                        classStats[classId].Union++;
                    }
                }
            }
        }

        var metrics = classStats
            .Select((stats, classId) =>
            {
                double? iou = stats.Union == 0 ? null : stats.Intersection / (double)stats.Union;
                var diceDenominator = stats.GroundTruthPixels + stats.PredictedPixels;
                double? dice = diceDenominator == 0 ? null : 2d * stats.Intersection / diceDenominator;
                return new ClassMetric(
                    classId,
                    Labels[classId],
                    stats.GroundTruthPixels,
                    stats.PredictedPixels,
                    stats.Intersection,
                    stats.Union,
                    iou is null ? null : Math.Round(iou.Value, 6),
                    dice is null ? null : Math.Round(dice.Value, 6));
            })
            .ToArray();

        var meanIoU = metrics.Where(item => item.IoU.HasValue).Select(item => item.IoU!.Value).DefaultIfEmpty(1).Average();
        var meanDice = metrics.Where(item => item.Dice.HasValue).Select(item => item.Dice!.Value).DefaultIfEmpty(1).Average();
        var boundaryIoU = ComputeBoundaryIoU(expected, predicted);
        var pixelAccuracy = totalPixels == 0 ? 0 : correctPixels / (double)totalPixels;
        var failures = new List<string>();
        if (pixelAccuracy < 0.999)
        {
            failures.Add($"PixelAccuracy={pixelAccuracy:0.######}");
        }

        if (meanIoU < 0.999)
        {
            failures.Add($"MeanIoU={meanIoU:0.######}");
        }

        if (meanDice < 0.999)
        {
            failures.Add($"MeanDice={meanDice:0.######}");
        }

        if (boundaryIoU < 0.999)
        {
            failures.Add($"BoundaryIoU={boundaryIoU:0.######}");
        }

        return new EvaluationResult(
            totalPixels,
            correctPixels,
            pixelAccuracy,
            meanIoU,
            meanDice,
            boundaryIoU,
            metrics,
            failures.Count == 0 ? null : string.Join("; ", failures));
    }

    private static byte[,] ToArray(Mat classMap)
    {
        var data = new byte[classMap.Rows, classMap.Cols];
        var indexer = classMap.GetGenericIndexer<byte>();
        for (var y = 0; y < classMap.Rows; y++)
        {
            for (var x = 0; x < classMap.Cols; x++)
            {
                data[y, x] = indexer[y, x];
            }
        }

        return data;
    }

    private static double ComputeBoundaryIoU(byte[,] expected, byte[,] predicted)
    {
        var height = expected.GetLength(0);
        var width = expected.GetLength(1);
        var intersection = 0;
        var union = 0;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var expectedBoundary = IsBoundary(expected, x, y);
                var predictedBoundary = IsBoundary(predicted, x, y);
                if (expectedBoundary && predictedBoundary)
                {
                    intersection++;
                }

                if (expectedBoundary || predictedBoundary)
                {
                    union++;
                }
            }
        }

        return union == 0 ? 1d : intersection / (double)union;
    }

    private static bool IsBoundary(byte[,] map, int x, int y)
    {
        var height = map.GetLength(0);
        var width = map.GetLength(1);
        var value = map[y, x];
        return (x > 0 && map[y, x - 1] != value) ||
               (x + 1 < width && map[y, x + 1] != value) ||
               (y > 0 && map[y - 1, x] != value) ||
               (y + 1 < height && map[y + 1, x] != value);
    }

    private static IReadOnlyList<int> PresentClassIds(Mat classMap, int numClasses)
    {
        var present = new SortedSet<int>();
        var indexer = classMap.GetGenericIndexer<byte>();
        for (var y = 0; y < classMap.Rows; y++)
        {
            for (var x = 0; x < classMap.Cols; x++)
            {
                var classId = indexer[y, x];
                if (classId >= 0 && classId < numClasses)
                {
                    present.Add(classId);
                }
            }
        }

        return present.ToArray();
    }

    private static void ValidateColoredMap(Mat coloredMap, Mat classMap, IReadOnlyList<int> presentClassIds)
    {
        if (coloredMap.Type() != MatType.CV_8UC3)
        {
            throw new InvalidOperationException($"Expected colored map CV_8UC3, got {coloredMap.Type()}.");
        }

        if (coloredMap.Rows != classMap.Rows || coloredMap.Cols != classMap.Cols)
        {
            throw new InvalidOperationException("Colored map size does not match class map size.");
        }

        var classIndexer = classMap.GetGenericIndexer<byte>();
        var colorIndexer = coloredMap.GetGenericIndexer<Vec3b>();
        foreach (var classId in presentClassIds)
        {
            var expected = InvokeGetPaletteColor(classId, NumClasses);
            var matched = false;
            for (var y = 0; y < classMap.Rows && !matched; y++)
            {
                for (var x = 0; x < classMap.Cols; x++)
                {
                    if (classIndexer[y, x] == classId)
                    {
                        RequireVecEquals(colorIndexer[y, x], expected, $"Palette mismatch for class {classId}.");
                        matched = true;
                        break;
                    }
                }
            }
        }
    }

    private static void ValidateMasks(Dictionary<string, Mat> masks, Mat classMap, IReadOnlyList<int> presentClassIds)
    {
        var expectedNames = presentClassIds.Select(classId => Labels[classId]).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!masks.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(expectedNames))
        {
            throw new InvalidOperationException("Class mask keys do not match present classes.");
        }

        var classIndexer = classMap.GetGenericIndexer<byte>();
        foreach (var classId in presentClassIds)
        {
            var label = Labels[classId];
            var mask = masks[label];
            if (mask.Rows != classMap.Rows || mask.Cols != classMap.Cols || mask.Type() != MatType.CV_8UC1)
            {
                throw new InvalidOperationException($"Mask shape/type mismatch for {label}.");
            }

            var expectedPixels = 0;
            for (var y = 0; y < classMap.Rows; y++)
            {
                for (var x = 0; x < classMap.Cols; x++)
                {
                    if (classIndexer[y, x] == classId)
                    {
                        expectedPixels++;
                    }
                }
            }

            var actualPixels = Cv2.CountNonZero(mask);
            if (actualPixels != expectedPixels)
            {
                throw new InvalidOperationException($"Mask pixel count mismatch for {label}: {actualPixels} != {expectedPixels}.");
            }
        }
    }

    private static DenseTensor<float> InvokePreprocessImage(Mat image, int inputWidth, int inputHeight, string channelOrder)
    {
        return (DenseTensor<float>)InvokeStatic(
            "PreprocessImage",
            image,
            inputWidth,
            inputHeight,
            Enum.Parse(ChannelOrderType, channelOrder, ignoreCase: true),
            new float[] { 0f, 0f, 0f },
            new float[] { 1f, 1f, 1f },
            true)!;
    }

    private static Mat InvokeBuildColoredMap(Mat classMap, int numClasses)
    {
        return (Mat)InvokeStatic("BuildColoredMap", classMap, numClasses)!;
    }

    private static Dictionary<string, Mat> InvokeBuildClassMasks(Mat classMap, IReadOnlyList<int> presentClasses, string[] classNames)
    {
        return (Dictionary<string, Mat>)InvokeStatic("BuildClassMasks", classMap, presentClasses, classNames)!;
    }

    private static Vec3b InvokeGetPaletteColor(int classId, int numClasses)
    {
        return (Vec3b)InvokeStatic("GetPaletteColor", classId, numClasses)!;
    }

    private static object? InvokeStatic(string methodName, params object?[] args)
    {
        var method = typeof(SemanticSegmentationOperator).GetMethod(methodName, BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(nameof(SemanticSegmentationOperator), methodName);
        return method.Invoke(null, args);
    }

    private static void RequireVecEquals(Vec3b actual, Vec3b expected, string message)
    {
        if (actual.Item0 != expected.Item0 || actual.Item1 != expected.Item1 || actual.Item2 != expected.Item2)
        {
            throw new InvalidOperationException(message);
        }
    }
}

internal sealed record SegmentationCaseSpec(
    string CaseId,
    string Scenario,
    int Width,
    int Height,
    int InputWidth,
    int InputHeight,
    string ChannelOrder,
    byte[,] ExpectedClassMap);

internal sealed class MutableClassStats
{
    public int GroundTruthPixels { get; set; }
    public int PredictedPixels { get; set; }
    public int Intersection { get; set; }
    public int Union { get; set; }
}

internal sealed record EvaluationResult(
    int TotalPixels,
    int CorrectPixels,
    double PixelAccuracy,
    double MeanIoU,
    double MeanDice,
    double BoundaryIoU,
    IReadOnlyList<ClassMetric> Classes,
    string? FailureReason);

internal sealed record ClassMetric(
    int ClassId,
    string ClassName,
    int GroundTruthPixels,
    int PredictedPixels,
    int Intersection,
    int Union,
    double? IoU,
    double? Dice);

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
    long TotalPixels,
    long CorrectPixels,
    double PixelAccuracy,
    double MeanIoU,
    double MeanDice,
    double MeanBoundaryIoU,
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
    long TotalPixels,
    double PixelAccuracy,
    double MeanIoU,
    double MeanDice,
    double BoundaryIoU,
    double RuntimeMsAvg);

internal sealed record CaseResult(
    string CaseId,
    string Scenario,
    bool Passed,
    double RuntimeMs,
    long MemoryAllocationBytes,
    int Width,
    int Height,
    string InputSize,
    string ChannelOrder,
    int TotalPixels,
    int CorrectPixels,
    double PixelAccuracy,
    double MeanIoU,
    double MeanDice,
    double BoundaryIoU,
    IReadOnlyList<string> PresentClasses,
    IReadOnlyList<ClassMetric> Classes,
    string? Failure);

internal static class MarkdownReport
{
    public static string Create(BaselineResult result)
    {
        var lines = new List<string>
        {
            "# SemanticSegmentation Dataset Baseline",
            "",
            $"EvidenceKind: `{result.EvidenceKind}`",
            $"GeneratedAtUtc: `{result.Summary.GeneratedAtUtc:O}`",
            $"Dataset: `{result.Summary.DatasetName}`",
            $"DatasetKind: `{result.Summary.DatasetKind}`",
            "",
            "## Summary",
            "",
            "| Metric | Value |",
            "| --- | ---: |",
            $"| Cases | {result.Summary.CaseCount} |",
            $"| Passed | {result.Summary.Passed} |",
            $"| Failed | {result.Summary.Failed} |",
            $"| Total pixels | {result.Summary.TotalPixels} |",
            $"| Correct pixels | {result.Summary.CorrectPixels} |",
            $"| Pixel accuracy | {result.Summary.PixelAccuracy:0.####} |",
            $"| Mean IoU | {result.Summary.MeanIoU:0.####} |",
            $"| Mean Dice | {result.Summary.MeanDice:0.####} |",
            $"| Mean boundary IoU | {result.Summary.MeanBoundaryIoU:0.####} |",
            $"| Runtime ms | {result.Summary.RuntimeMs:0.###} |",
            $"| Candidate version | {result.Summary.CandidateVersion} |",
            $"| Profile | {result.Summary.Profile} |",
            "",
            "## Scenarios",
            "",
            "| Scenario | Cases | Passed | Failed | Pixels | Pixel accuracy | mIoU | Dice | Boundary IoU | Avg ms |",
            "| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |"
        };

        lines.AddRange(result.Scenarios.Select(item =>
            $"| {item.Scenario} | {item.CaseCount} | {item.Passed} | {item.Failed} | {item.TotalPixels} | {item.PixelAccuracy:0.####} | {item.MeanIoU:0.####} | {item.MeanDice:0.####} | {item.BoundaryIoU:0.####} | {item.RuntimeMsAvg:0.###} |"));

        lines.AddRange(
        [
            "",
            "## Failure Boundaries",
            "",
            "- `single_region` verifies large foreground masks and background separation.",
            "- `multi_class_regions` verifies multiple positive classes and per-class IoU accounting.",
            "- `thin_boundary` verifies 1-2 px structures and boundary-IoU sensitivity.",
            "- `small_object` verifies small connected regions remain represented in masks.",
            "- `class_absent` verifies missing classes do not create extra masks or denominator drift.",
            "- `nested_regions` verifies overlapping overwrite order, class masks, and colored-map palette stability.",
            "- This bridge records VOC-style segmentation metrics for SemanticSegmentation preprocessing, mask, and visualization paths; it is not production model accuracy evidence.",
            "",
            "## Cases",
            "",
            "| Case | Scenario | Passed | Size | Input | Order | Pixel accuracy | mIoU | Dice | Boundary IoU | Present classes | Runtime ms | Failure |",
            "| --- | --- | --- | --- | --- | --- | ---: | ---: | ---: | ---: | --- | ---: | --- |"
        ]);

        lines.AddRange(result.Cases.Select(item =>
            $"| {item.CaseId} | {item.Scenario} | {item.Passed} | {item.Width}x{item.Height} | {item.InputSize} | {item.ChannelOrder} | {item.PixelAccuracy:0.####} | {item.MeanIoU:0.####} | {item.MeanDice:0.####} | {item.BoundaryIoU:0.####} | {string.Join(", ", item.PresentClasses)} | {item.RuntimeMs:0.###} | {item.Failure ?? "-"} |"));

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
    public bool IncludesCase(SegmentationCaseSpec spec) => CaseIds.Count == 0 || CaseIds.Contains(spec.CaseId);

    public static RunnerOptions Parse(string[] args)
    {
        var options = new RunnerOptions(
            "quality/evals/reports/SemanticSegmentation_dataset_baseline.json",
            "quality/evals/reports/SemanticSegmentation_dataset_baseline.md",
            "control",
            "baseline_protocol_bridge",
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
        Usage: dotnet run --project quality/tools/SemanticSegmentationDatasetRunner/SemanticSegmentationDatasetRunner.csproj -- [options]

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
