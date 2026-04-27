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

var result = EdgeDetectionDatasetBenchmarkRunner.Run(options);
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(options.OutputPath))!);
File.WriteAllText(options.OutputPath, JsonSerializer.Serialize(result, JsonSettings.Indented));

if (!string.IsNullOrWhiteSpace(options.ReportPath))
{
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(options.ReportPath))!);
    File.WriteAllText(options.ReportPath, MarkdownReport.Create(result));
}

Console.WriteLine(
    $"EdgeDetection dataset benchmark complete: {result.Summary.Passed}/{result.Summary.CaseCount} passed, " +
    $"F1={result.Summary.F1:F4}, boundaryF1={result.Summary.MeanBoundaryF1:F4}, output={options.OutputPath}");

return result.Summary.Failed == 0 ? 0 : 1;

internal static class EdgeDetectionDatasetBenchmarkRunner
{
    private const string EvidenceKind = "dataset";
    private const string DatasetName = "BSDS-style semi-synthetic edge benchmark protocol bridge";
    private const int BoundaryTolerancePixels = 1;
    private static readonly CannyEdgeOperator Operator = new(NullLogger<CannyEdgeOperator>.Instance);

    public static BaselineResult Run(RunnerOptions options)
    {
        var specs = BuildCases().ToList();
        var results = new List<CaseResult>(specs.Count);
        foreach (var spec in specs)
        {
            results.Add(RunCase(spec));
        }

        var failed = results.Count(item => !item.Passed);
        var truePositives = results.Sum(item => item.TruePositivePixels);
        var falsePositives = results.Sum(item => item.FalsePositivePixels);
        var falseNegatives = results.Sum(item => item.FalseNegativePixels);
        var precision = Precision(truePositives, falsePositives, falseNegatives);
        var recall = Recall(truePositives, falseNegatives, falsePositives);
        var f1 = F1(precision, recall, truePositives, falsePositives, falseNegatives);
        var runtimeMs = Math.Round(results.Sum(item => item.RuntimeMs), 3);
        var memoryBytes = results.Sum(item => item.MemoryAllocationBytes);

        return new BaselineResult(
            EvidenceKind,
            new DatasetSummary(
                DateTimeOffset.UtcNow,
                DatasetName,
                "Tier A protocol bridge for public/BSDS-style edge-detection metrics; no external image pixels are stored.",
                specs.Count,
                results.Count - failed,
                failed,
                results.Sum(item => item.TotalPixels),
                results.Sum(item => item.ExpectedEdgePixels),
                results.Sum(item => item.PredictedEdgePixels),
                truePositives,
                falsePositives,
                falseNegatives,
                Math.Round(precision, 6),
                Math.Round(recall, 6),
                Math.Round(f1, 6),
                Math.Round(results.Average(item => item.BoundaryF1), 6),
                BoundaryTolerancePixels,
                runtimeMs,
                memoryBytes),
            [
                new OperatorSummary(
                    "EdgeDetection",
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
                    group.Sum(item => item.ExpectedEdgePixels),
                    group.Sum(item => item.PredictedEdgePixels),
                    group.Sum(item => item.FalsePositivePixels),
                    group.Sum(item => item.FalseNegativePixels),
                    Math.Round(group.Average(item => item.F1), 6),
                    Math.Round(group.Average(item => item.BoundaryF1), 6),
                    Math.Round(group.Average(item => item.RuntimeMs), 3)))
                .ToArray(),
            results);
    }

    private static CaseResult RunCase(EdgeCaseSpec spec)
    {
        var stopwatch = Stopwatch.StartNew();
        var allocationBefore = GC.GetTotalAllocatedBytes(precise: true);
        Dictionary<string, object>? outputData = null;
        try
        {
            using var source = CreateScene(spec);
            using var input = new ImageWrapper(source.Clone());
            using var reference = BuildReferenceEdges(source, spec);

            var result = Operator.ExecuteAsync(CreateOperator(spec), new Dictionary<string, object> { ["Image"] = input })
                .GetAwaiter()
                .GetResult();
            outputData = result.OutputData;
            Require(result.IsSuccess, $"Expected success, got failure: {result.ErrorMessage}");
            if (outputData is null)
            {
                throw new InvalidOperationException("Expected output data.");
            }

            Require(outputData.ContainsKey("Edges"), "Expected Edges output.");

            using var predicted = GetOutputImage(outputData).Clone();
            var threshold1Used = RequireDouble(outputData, "Threshold1Used");
            var threshold2Used = RequireDouble(outputData, "Threshold2Used");
            RequireNear(threshold1Used, reference.Threshold1Used, 1e-9, "Threshold1Used");
            RequireNear(threshold2Used, reference.Threshold2Used, 1e-9, "Threshold2Used");

            var evaluation = Evaluate(reference.Edges, predicted, BoundaryTolerancePixels);
            var passed =
                evaluation.FalsePositivePixels == 0 &&
                evaluation.FalseNegativePixels == 0 &&
                evaluation.F1 >= 0.999 &&
                evaluation.BoundaryF1 >= 0.999;

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
                spec.SourceKind,
                spec.AutoThreshold,
                spec.EnableGaussianBlur,
                spec.GaussianKernelSize,
                spec.ApertureSize,
                spec.L2Gradient,
                Math.Round(threshold1Used, 6),
                Math.Round(threshold2Used, 6),
                evaluation.TotalPixels,
                evaluation.ExpectedEdgePixels,
                evaluation.PredictedEdgePixels,
                evaluation.TruePositivePixels,
                evaluation.FalsePositivePixels,
                evaluation.FalseNegativePixels,
                Math.Round(evaluation.Precision, 6),
                Math.Round(evaluation.Recall, 6),
                Math.Round(evaluation.F1, 6),
                Math.Round(evaluation.BoundaryPrecision, 6),
                Math.Round(evaluation.BoundaryRecall, 6),
                Math.Round(evaluation.BoundaryF1, 6),
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
                spec.SourceKind,
                spec.AutoThreshold,
                spec.EnableGaussianBlur,
                spec.GaussianKernelSize,
                spec.ApertureSize,
                spec.L2Gradient,
                0,
                0,
                spec.Width * spec.Height,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                ex.GetBaseException().Message);
        }
        finally
        {
            DisposeOutputImages(outputData);
        }
    }

    private static IEnumerable<EdgeCaseSpec> BuildCases()
    {
        var dimensions = new[] { (64, 48), (96, 64), (128, 96), (160, 120), (192, 128), (256, 144) };
        var scenarios = new[]
        {
            "hard_step_shapes",
            "diagonal_edges",
            "thin_lines",
            "low_contrast_auto_threshold",
            "blurred_noise",
            "color_input_edges"
        };

        for (var i = 0; i < dimensions.Length; i++)
        {
            var (width, height) = dimensions[i];
            foreach (var scenario in scenarios)
            {
                yield return CreateSpec(scenario, i, width, height);
            }
        }
    }

    private static EdgeCaseSpec CreateSpec(string scenario, int index, int width, int height)
    {
        var aperture = index % 3 == 2 ? 5 : 3;
        return scenario switch
        {
            "hard_step_shapes" => new(
                $"EdgeDetection_{scenario}_{index:0000}",
                scenario,
                width,
                height,
                "grayscale",
                false,
                35 + index,
                120 + index,
                0.33,
                false,
                5,
                3,
                false,
                index),
            "diagonal_edges" => new(
                $"EdgeDetection_{scenario}_{index:0000}",
                scenario,
                width,
                height,
                "grayscale",
                false,
                28 + index,
                96 + index,
                0.33,
                false,
                5,
                aperture,
                true,
                index),
            "thin_lines" => new(
                $"EdgeDetection_{scenario}_{index:0000}",
                scenario,
                width,
                height,
                "grayscale",
                false,
                24 + index,
                82 + index,
                0.33,
                false,
                5,
                3,
                false,
                index),
            "low_contrast_auto_threshold" => new(
                $"EdgeDetection_{scenario}_{index:0000}",
                scenario,
                width,
                height,
                "grayscale",
                true,
                50,
                150,
                0.28 + (index % 3) * 0.04,
                true,
                4 + (index % 2),
                3,
                false,
                index),
            "blurred_noise" => new(
                $"EdgeDetection_{scenario}_{index:0000}",
                scenario,
                width,
                height,
                "grayscale",
                false,
                45 + index,
                130 + index,
                0.33,
                true,
                5,
                3,
                index % 2 == 0,
                index),
            "color_input_edges" => new(
                $"EdgeDetection_{scenario}_{index:0000}",
                scenario,
                width,
                height,
                "bgr",
                false,
                42 + index,
                136 + index,
                0.33,
                false,
                5,
                3,
                false,
                index),
            _ => throw new InvalidOperationException($"Unknown scenario: {scenario}")
        };
    }

    private static Mat CreateScene(EdgeCaseSpec spec)
    {
        return spec.Scenario switch
        {
            "hard_step_shapes" => CreateHardStepShapes(spec.Width, spec.Height, spec.Variant),
            "diagonal_edges" => CreateDiagonalEdges(spec.Width, spec.Height, spec.Variant),
            "thin_lines" => CreateThinLines(spec.Width, spec.Height, spec.Variant),
            "low_contrast_auto_threshold" => CreateLowContrastAutoThreshold(spec.Width, spec.Height, spec.Variant),
            "blurred_noise" => CreateBlurredNoise(spec.Width, spec.Height, spec.Variant),
            "color_input_edges" => CreateColorInputEdges(spec.Width, spec.Height, spec.Variant),
            _ => throw new InvalidOperationException($"Unknown scenario: {spec.Scenario}")
        };
    }

    private static Mat CreateHardStepShapes(int width, int height, int index)
    {
        var mat = new Mat(height, width, MatType.CV_8UC1, Scalar.Black);
        Cv2.Rectangle(mat, new Rect(8 + index % 5, 8, width / 3, height / 3), Scalar.White, -1);
        Cv2.Circle(mat, new Point(width * 2 / 3, height / 2), Math.Max(6, Math.Min(width, height) / 8), new Scalar(180), -1);
        Cv2.Line(mat, new Point(4, height - 8), new Point(width - 6, 12 + index % 7), new Scalar(120), 2);
        return mat;
    }

    private static Mat CreateDiagonalEdges(int width, int height, int index)
    {
        var mat = new Mat(height, width, MatType.CV_8UC1, new Scalar(18));
        Cv2.Line(mat, new Point(4, height - 6), new Point(width - 8, 4 + index % 9), new Scalar(235), 2);
        Cv2.Line(mat, new Point(6 + index % 11, 6), new Point(width - 10, height - 8), new Scalar(150), 1);
        Cv2.Rectangle(mat, new Rect(width / 5, height / 5, width / 4, height / 3), new Scalar(85), -1);
        return mat;
    }

    private static Mat CreateThinLines(int width, int height, int index)
    {
        var mat = new Mat(height, width, MatType.CV_8UC1, new Scalar(12));
        for (var x = 12 + index % 4; x < width - 8; x += Math.Max(14, width / 6))
        {
            Cv2.Line(mat, new Point(x, 6), new Point(x, height - 7), new Scalar(210), 1);
        }

        Cv2.Line(mat, new Point(5, height / 2), new Point(width - 6, height / 2 + index % 3 - 1), new Scalar(180), 1);
        Cv2.Rectangle(mat, new Rect(width / 3, height / 4, width / 4, height / 3), new Scalar(110), 1);
        return mat;
    }

    private static Mat CreateLowContrastAutoThreshold(int width, int height, int index)
    {
        var mat = new Mat(height, width, MatType.CV_8UC1, Scalar.Black);
        var indexer = mat.GetGenericIndexer<byte>();
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                indexer[y, x] = (byte)Math.Clamp(72 + x * 28 / Math.Max(1, width - 1) + y * 12 / Math.Max(1, height - 1), 0, 255);
            }
        }

        Cv2.Rectangle(mat, new Rect(width / 5, height / 4, width / 3, height / 3), new Scalar(125 + index % 8), -1);
        Cv2.Circle(mat, new Point(width * 3 / 4, height * 2 / 3), Math.Max(5, Math.Min(width, height) / 10), new Scalar(45), -1);
        return mat;
    }

    private static Mat CreateBlurredNoise(int width, int height, int index)
    {
        var mat = new Mat(height, width, MatType.CV_8UC1, Scalar.Black);
        var indexer = mat.GetGenericIndexer<byte>();
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var noise = (x * 17 + y * 31 + index * 13) % 27;
                indexer[y, x] = (byte)(32 + noise);
            }
        }

        Cv2.Rectangle(mat, new Rect(width / 6, height / 5, width / 3, height / 3), new Scalar(205), -1);
        Cv2.Ellipse(mat, new Point(width * 2 / 3, height / 2), new Size(width / 8, height / 5), 15 + index * 3, 0, 360, new Scalar(145), -1);
        return mat;
    }

    private static Mat CreateColorInputEdges(int width, int height, int index)
    {
        var mat = new Mat(height, width, MatType.CV_8UC3, new Scalar(24, 28, 32));
        Cv2.Rectangle(mat, new Rect(width / 8, height / 5, width / 3, height / 3), new Scalar(40, 210, 80), -1);
        Cv2.Circle(mat, new Point(width * 2 / 3, height / 2), Math.Max(6, Math.Min(width, height) / 8), new Scalar(220, 70, 70), -1);
        Cv2.Line(mat, new Point(5, height - 10), new Point(width - 8, 8 + index % 7), new Scalar(70, 90, 230), 2);
        return mat;
    }

    private static EdgeReference BuildReferenceEdges(Mat source, EdgeCaseSpec spec)
    {
        using var gray = new Mat();
        if (source.Channels() == 3)
        {
            Cv2.CvtColor(source, gray, ColorConversionCodes.BGR2GRAY);
        }
        else
        {
            source.CopyTo(gray);
        }

        using var processed = new Mat();
        if (spec.EnableGaussianBlur)
        {
            var kernelSize = spec.GaussianKernelSize % 2 == 0 ? spec.GaussianKernelSize + 1 : spec.GaussianKernelSize;
            Cv2.GaussianBlur(gray, processed, new Size(kernelSize, kernelSize), 1.0);
        }
        else
        {
            gray.CopyTo(processed);
        }

        var threshold1 = spec.Threshold1;
        var threshold2 = spec.Threshold2;
        if (spec.AutoThreshold)
        {
            var median = ComputeMedianIntensity(processed);
            threshold1 = Math.Clamp((1.0 - spec.AutoThresholdSigma) * median, 0.0, 255.0);
            threshold2 = Math.Clamp((1.0 + spec.AutoThresholdSigma) * median, 0.0, 255.0);
            if (threshold2 <= threshold1)
            {
                threshold2 = Math.Min(255.0, threshold1 + 1.0);
            }
        }

        var edges = new Mat();
        Cv2.Canny(processed, edges, threshold1, threshold2, spec.ApertureSize, spec.L2Gradient);
        return new EdgeReference(edges, threshold1, threshold2);
    }

    private static EdgeEvaluation Evaluate(Mat expected, Mat predicted, int tolerancePixels)
    {
        Require(expected.Rows == predicted.Rows && expected.Cols == predicted.Cols, "Predicted edge map size mismatch.");
        var expectedIndexer = expected.GetGenericIndexer<byte>();
        var predictedIndexer = predicted.GetGenericIndexer<byte>();
        var totalPixels = expected.Rows * expected.Cols;
        var expectedEdges = 0;
        var predictedEdges = 0;
        var tp = 0;
        var fp = 0;
        var fn = 0;
        var boundaryPrecisionHits = 0;
        var boundaryRecallHits = 0;

        for (var y = 0; y < expected.Rows; y++)
        {
            for (var x = 0; x < expected.Cols; x++)
            {
                var expectedEdge = expectedIndexer[y, x] != 0;
                var predictedEdge = predictedIndexer[y, x] != 0;
                if (expectedEdge)
                {
                    expectedEdges++;
                    if (HasEdgeWithin(predictedIndexer, predicted.Rows, predicted.Cols, x, y, tolerancePixels))
                    {
                        boundaryRecallHits++;
                    }
                }

                if (predictedEdge)
                {
                    predictedEdges++;
                    if (HasEdgeWithin(expectedIndexer, expected.Rows, expected.Cols, x, y, tolerancePixels))
                    {
                        boundaryPrecisionHits++;
                    }
                }

                if (expectedEdge && predictedEdge)
                {
                    tp++;
                }
                else if (!expectedEdge && predictedEdge)
                {
                    fp++;
                }
                else if (expectedEdge)
                {
                    fn++;
                }
            }
        }

        var precision = Precision(tp, fp, fn);
        var recall = Recall(tp, fn, fp);
        var f1 = F1(precision, recall, tp, fp, fn);
        var boundaryPrecision = predictedEdges == 0 ? (expectedEdges == 0 ? 1d : 0d) : boundaryPrecisionHits / (double)predictedEdges;
        var boundaryRecall = expectedEdges == 0 ? (predictedEdges == 0 ? 1d : 0d) : boundaryRecallHits / (double)expectedEdges;
        var boundaryF1 = boundaryPrecision + boundaryRecall <= 0 ? 0 : 2d * boundaryPrecision * boundaryRecall / (boundaryPrecision + boundaryRecall);
        var failures = new List<string>();
        if (fp > 0)
        {
            failures.Add($"FP={fp}");
        }

        if (fn > 0)
        {
            failures.Add($"FN={fn}");
        }

        if (f1 < 0.999)
        {
            failures.Add($"F1={f1:0.######}");
        }

        if (boundaryF1 < 0.999)
        {
            failures.Add($"BoundaryF1={boundaryF1:0.######}");
        }

        return new EdgeEvaluation(
            totalPixels,
            expectedEdges,
            predictedEdges,
            tp,
            fp,
            fn,
            precision,
            recall,
            f1,
            boundaryPrecision,
            boundaryRecall,
            boundaryF1,
            failures.Count == 0 ? null : string.Join("; ", failures));
    }

    private static bool HasEdgeWithin(MatIndexer<byte> indexer, int rows, int cols, int x, int y, int tolerance)
    {
        for (var yy = Math.Max(0, y - tolerance); yy <= Math.Min(rows - 1, y + tolerance); yy++)
        {
            for (var xx = Math.Max(0, x - tolerance); xx <= Math.Min(cols - 1, x + tolerance); xx++)
            {
                if (indexer[yy, xx] != 0)
                {
                    return true;
                }
            }
        }

        return false;
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

    private static double ComputeMedianIntensity(Mat gray)
    {
        using var hist = new Mat();
        Cv2.CalcHist(
            [gray],
            [0],
            null,
            hist,
            1,
            [256],
            [new Rangef(0, 256)]);

        double total = 0;
        for (var i = 0; i < 256; i++)
        {
            total += hist.At<float>(i);
        }

        if (total <= 0)
        {
            return 0;
        }

        var midpoint = total / 2.0;
        double cumulative = 0;
        for (var i = 0; i < 256; i++)
        {
            cumulative += hist.At<float>(i);
            if (cumulative >= midpoint)
            {
                return i;
            }
        }

        return 255;
    }

    private static Operator CreateOperator(EdgeCaseSpec spec)
    {
        var op = new Operator(Guid.NewGuid(), "EdgeDetectionDatasetBenchmark", OperatorType.EdgeDetection, 0, 0);
        AddParameter(op, "Threshold1", spec.Threshold1);
        AddParameter(op, "Threshold2", spec.Threshold2);
        AddParameter(op, "AutoThreshold", spec.AutoThreshold);
        AddParameter(op, "AutoThresholdSigma", spec.AutoThresholdSigma);
        AddParameter(op, "EnableGaussianBlur", spec.EnableGaussianBlur);
        AddParameter(op, "GaussianKernelSize", spec.GaussianKernelSize);
        AddParameter(op, "ApertureSize", spec.ApertureSize);
        AddParameter(op, "L2Gradient", spec.L2Gradient);
        return op;
    }

    private static void AddParameter(Operator op, string name, object value)
    {
        op.AddParameter(new Parameter(Guid.NewGuid(), name, name, string.Empty, InferParameterType(value), value, isRequired: false));
    }

    private static string InferParameterType(object value)
    {
        return value switch
        {
            bool => "bool",
            int or long => "int",
            float or double or decimal => "double",
            _ => "string"
        };
    }

    private static Mat GetOutputImage(Dictionary<string, object> outputData)
    {
        if (!outputData.TryGetValue("Image", out var raw))
        {
            throw new InvalidOperationException("Missing Image output.");
        }

        if (raw is not ImageWrapper wrapper)
        {
            throw new InvalidOperationException("Image output should be ImageWrapper.");
        }

        return wrapper.MatReadOnly;
    }

    private static double RequireDouble(Dictionary<string, object> outputData, string key)
    {
        Require(outputData.TryGetValue(key, out var raw), $"Missing {key} output.");
        return Convert.ToDouble(raw);
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

    private static void RequireNear(double actual, double expected, double tolerance, string name)
    {
        Require(Math.Abs(actual - expected) <= tolerance, $"{name}: expected {expected}, got {actual}.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}

internal sealed record EdgeCaseSpec(
    string CaseId,
    string Scenario,
    int Width,
    int Height,
    string SourceKind,
    bool AutoThreshold,
    double Threshold1,
    double Threshold2,
    double AutoThresholdSigma,
    bool EnableGaussianBlur,
    int GaussianKernelSize,
    int ApertureSize,
    bool L2Gradient,
    int Variant);

internal sealed class EdgeReference(Mat edges, double threshold1Used, double threshold2Used) : IDisposable
{
    public Mat Edges { get; } = edges;
    public double Threshold1Used { get; } = threshold1Used;
    public double Threshold2Used { get; } = threshold2Used;

    public void Dispose()
    {
        Edges.Dispose();
    }
}

internal sealed record EdgeEvaluation(
    int TotalPixels,
    int ExpectedEdgePixels,
    int PredictedEdgePixels,
    int TruePositivePixels,
    int FalsePositivePixels,
    int FalseNegativePixels,
    double Precision,
    double Recall,
    double F1,
    double BoundaryPrecision,
    double BoundaryRecall,
    double BoundaryF1,
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
    int TotalPixels,
    int ExpectedEdgePixels,
    int PredictedEdgePixels,
    int TruePositivePixels,
    int FalsePositivePixels,
    int FalseNegativePixels,
    double Precision,
    double Recall,
    double F1,
    double MeanBoundaryF1,
    int BoundaryTolerancePixels,
    double RuntimeMs,
    long MemoryAllocationBytes);

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
    int ExpectedEdgePixels,
    int PredictedEdgePixels,
    int FalsePositivePixels,
    int FalseNegativePixels,
    double F1,
    double BoundaryF1,
    double RuntimeMsAvg);

internal sealed record CaseResult(
    string CaseId,
    string Scenario,
    bool Passed,
    double RuntimeMs,
    long MemoryAllocationBytes,
    int Width,
    int Height,
    string SourceKind,
    bool AutoThreshold,
    bool EnableGaussianBlur,
    int GaussianKernelSize,
    int ApertureSize,
    bool L2Gradient,
    double Threshold1Used,
    double Threshold2Used,
    int TotalPixels,
    int ExpectedEdgePixels,
    int PredictedEdgePixels,
    int TruePositivePixels,
    int FalsePositivePixels,
    int FalseNegativePixels,
    double Precision,
    double Recall,
    double F1,
    double BoundaryPrecision,
    double BoundaryRecall,
    double BoundaryF1,
    string? Failure);

internal static class MarkdownReport
{
    public static string Create(BaselineResult result)
    {
        var lines = new List<string>
        {
            "# EdgeDetection Dataset Benchmark Baseline",
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
            $"| Expected edge pixels | {result.Summary.ExpectedEdgePixels} |",
            $"| Predicted edge pixels | {result.Summary.PredictedEdgePixels} |",
            $"| True positives | {result.Summary.TruePositivePixels} |",
            $"| False positives | {result.Summary.FalsePositivePixels} |",
            $"| False negatives | {result.Summary.FalseNegativePixels} |",
            $"| Precision | {result.Summary.Precision:0.####} |",
            $"| Recall | {result.Summary.Recall:0.####} |",
            $"| F1 | {result.Summary.F1:0.####} |",
            $"| Mean boundary F1 | {result.Summary.MeanBoundaryF1:0.####} |",
            $"| Boundary tolerance px | {result.Summary.BoundaryTolerancePixels} |",
            $"| Runtime ms | {result.Summary.RuntimeMs:0.###} |",
            "",
            "## Scenarios",
            "",
            "| Scenario | Cases | Passed | Failed | Expected edges | Predicted edges | FP | FN | F1 | Boundary F1 | Avg ms |",
            "| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |"
        };

        lines.AddRange(result.Scenarios.Select(item =>
            $"| {item.Scenario} | {item.CaseCount} | {item.Passed} | {item.Failed} | {item.ExpectedEdgePixels} | {item.PredictedEdgePixels} | {item.FalsePositivePixels} | {item.FalseNegativePixels} | {item.F1:0.####} | {item.BoundaryF1:0.####} | {item.RuntimeMsAvg:0.###} |"));

        lines.AddRange(
        [
            "",
            "## Failure Boundaries",
            "",
            "- `hard_step_shapes` verifies canonical Canny step-edge extraction on rectangles, circles, and lines.",
            "- `diagonal_edges` verifies diagonal and L2-gradient edge behavior.",
            "- `thin_lines` verifies 1 px line structures and sparse edge maps.",
            "- `low_contrast_auto_threshold` verifies auto-threshold median logic and low-contrast boundaries.",
            "- `blurred_noise` verifies the Gaussian prefilter path under deterministic noise.",
            "- `color_input_edges` verifies color input conversion into edge benchmark scoring.",
            "- This bridge records BSDS-style edge benchmark metrics for the EdgeDetection Canny path; it is not field-image accuracy evidence.",
            "",
            "## Cases",
            "",
            "| Case | Scenario | Passed | Size | Source | Auto | Blur | Aperture | L2 | Thresholds | Expected | Predicted | FP | FN | F1 | Boundary F1 | Runtime ms | Failure |",
            "| --- | --- | --- | --- | --- | --- | --- | ---: | --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | --- |"
        ]);

        lines.AddRange(result.Cases.Select(item =>
            $"| {item.CaseId} | {item.Scenario} | {item.Passed} | {item.Width}x{item.Height} | {item.SourceKind} | {item.AutoThreshold} | {item.EnableGaussianBlur} | {item.ApertureSize} | {item.L2Gradient} | {item.Threshold1Used:0.###}/{item.Threshold2Used:0.###} | {item.ExpectedEdgePixels} | {item.PredictedEdgePixels} | {item.FalsePositivePixels} | {item.FalseNegativePixels} | {item.F1:0.####} | {item.BoundaryF1:0.####} | {item.RuntimeMs:0.###} | {item.Failure ?? "-"} |"));

        lines.Add("");
        return string.Join(Environment.NewLine, lines);
    }
}

internal sealed record RunnerOptions(string OutputPath, string ReportPath, bool ShowHelp, string? ParseError)
{
    public static RunnerOptions Parse(string[] args)
    {
        var options = new RunnerOptions(
            "quality/evals/reports/EdgeDetection_dataset_baseline.json",
            "quality/evals/reports/EdgeDetection_dataset_baseline.md",
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
        Usage: dotnet run --project quality/tools/EdgeDetectionDatasetBenchmarkRunner/EdgeDetectionDatasetBenchmarkRunner.csproj -- [options]

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
