using System.Collections;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Operators;
using ClearVision.Product.Core.ValueObjects;
using ClearVision.Product.Infrastructure.Operators;
using Microsoft.Extensions.Logging.Abstractions;
using OpenCvSharp;
using Xunit;

namespace ClearVision.Product.Tests.Integration;

[TestClassification(TestDomain.Measurement, TestPurpose.Performance, TestLane.Nightly, TestEvidenceType.PerformanceProfile, TestOracleType.PerformanceBudget, TestResourceRequirement.CpuProfile, TestExpectedDuration.Long, TestFlakyPolicy.Blocking, "operator-quality", PerformanceProfile = "standard: documented warmup, scale and percentile budget")]
[Collection(PerformanceAcceptanceCollection.Name)]
[Trait("Category", "PerformanceBudget")]
public sealed class MeasurementPerformanceBudgetAcceptanceTests
{
    private const double DefaultBudgetScale = 1.5;
    private const string ReportFileStem = "measurement_performance_budget_report";

    [Fact(Timeout = 300000)]
    public async Task W5_MeasurementOperatorPerformanceBudget_512_ShouldMeetUnifiedGate()
    {
        var warmupIterations = GetEnvInt("CV_MEASUREMENT_PERF_WARMUP_ITERS", 5, 0, 100);
        var measuredIterations = GetEnvInt("CV_MEASUREMENT_PERF_MEASURE_ITERS", 24, 10, 400);
        var budgetScale = GetEnvDouble("CV_MEASUREMENT_PERF_BUDGET_SCALE", DefaultBudgetScale, 0.5, 10.0);
        var gateProfile = GetEnvString("CV_MEASUREMENT_PERF_GATE_PROFILE", "standard");

        var angle = new AngleMeasurementOperator(NullLogger<AngleMeasurementOperator>.Instance);
        var caliper = new CaliperToolOperator(NullLogger<CaliperToolOperator>.Instance);
        var circle = new CircleMeasurementOperator(NullLogger<CircleMeasurementOperator>.Instance);
        var contour = new ContourMeasurementOperator(NullLogger<ContourMeasurementOperator>.Instance);
        var color = new ColorMeasurementOperator(NullLogger<ColorMeasurementOperator>.Instance);
        var gap = new GapMeasurementOperator(NullLogger<GapMeasurementOperator>.Instance);
        var geo = new GeoMeasurementOperator(NullLogger<GeoMeasurementOperator>.Instance);
        var fitting = new GeometricFittingOperator(NullLogger<GeometricFittingOperator>.Instance);
        var tolerance = new GeometricToleranceOperator(NullLogger<GeometricToleranceOperator>.Instance);
        var histogram = new HistogramAnalysisOperator(NullLogger<HistogramAnalysisOperator>.Instance);
        var lineLineDistance = new LineLineDistanceOperator(NullLogger<LineLineDistanceOperator>.Instance);
        var line = new LineMeasurementOperator(NullLogger<LineMeasurementOperator>.Instance);
        var measureDistance = new MeasureDistanceOperator(NullLogger<MeasureDistanceOperator>.Instance);
        var pixelStats = new PixelStatisticsOperator(NullLogger<PixelStatisticsOperator>.Instance);
        var pointLineDistance = new PointLineDistanceOperator(NullLogger<PointLineDistanceOperator>.Instance);
        var sharpness = new SharpnessEvaluationOperator(NullLogger<SharpnessEvaluationOperator>.Instance);
        var width = new WidthMeasurementOperator(NullLogger<WidthMeasurementOperator>.Instance);

        var angleOp = new Operator("AngleMeasurement", OperatorType.AngleMeasurement, 0, 0);

        var caliperOp = new Operator("CaliperTool", OperatorType.CaliperTool, 0, 0);
        AddParam(caliperOp, "Direction", "Horizontal", "string");
        AddParam(caliperOp, "Polarity", "Both", "string");
        AddParam(caliperOp, "EdgeThreshold", 18.0, "double");
        AddParam(caliperOp, "ExpectedCount", 1, "int");
        AddParam(caliperOp, "MeasureMode", "edge_pairs", "string");
        AddParam(caliperOp, "SubpixelAccuracy", false, "bool");

        var circleOp = new Operator("CircleMeasurement", OperatorType.CircleMeasurement, 0, 0);
        AddParam(circleOp, "Method", "HoughCircle", "string");
        AddParam(circleOp, "MinRadius", 30, "int");
        AddParam(circleOp, "MaxRadius", 140, "int");

        var contourOp = new Operator("ContourMeasurement", OperatorType.ContourMeasurement, 0, 0);
        AddParam(contourOp, "Threshold", 100.0, "double");
        AddParam(contourOp, "MinArea", 50, "int");
        AddParam(contourOp, "MaxArea", 200000, "int");

        var colorOp = new Operator("ColorMeasurement", OperatorType.ColorMeasurement, 0, 0);
        AddParam(colorOp, "MeasurementMode", "LabDeltaE", "string");
        AddParam(colorOp, "DeltaEMethod", "CIEDE2000", "string");

        var gapOp = new Operator("GapMeasurement", OperatorType.GapMeasurement, 0, 0);
        AddParam(gapOp, "Direction", "Horizontal", "string");
        AddParam(gapOp, "ExpectedCount", 4, "int");
        AddParam(gapOp, "RobustMode", true, "bool");
        AddParam(gapOp, "OutlierSigmaK", 3.0, "double");
        AddParam(gapOp, "MultiScanCount", 8, "int");

        var geoOp = new Operator("GeoMeasurement", OperatorType.GeoMeasurement, 0, 0);
        AddParam(geoOp, "Element1Type", "Line", "string");
        AddParam(geoOp, "Element2Type", "Line", "string");
        AddParam(geoOp, "DistanceModel", "Segment", "string");

        var fittingOp = new Operator("GeometricFitting", OperatorType.GeometricFitting, 0, 0);
        AddParam(fittingOp, "FitType", "Circle", "string");
        AddParam(fittingOp, "Threshold", 100.0, "double");
        AddParam(fittingOp, "ContourSelection", "LargestContour", "string");

        var toleranceOp = new Operator("GeometricTolerance", OperatorType.GeometricTolerance, 0, 0);
        AddParam(toleranceOp, "ToleranceType", "Parallelism", "string");
        AddParam(toleranceOp, "ZoneSize", 2.0, "double");

        var histogramOp = new Operator("HistogramAnalysis", OperatorType.HistogramAnalysis, 0, 0);
        AddParam(histogramOp, "Channel", "Gray", "string");
        AddParam(histogramOp, "BinCount", 128, "int");

        var lineLineOp = new Operator("LineLineDistance", OperatorType.LineLineDistance, 0, 0);
        AddParam(lineLineOp, "ParallelThreshold", 2.0, "double");
        AddParam(lineLineOp, "DistanceModel", "Segment", "string");

        var lineOp = new Operator("LineMeasurement", OperatorType.LineMeasurement, 0, 0);
        AddParam(lineOp, "Method", "FitLine", "string");
        AddParam(lineOp, "Threshold", 80, "int");
        AddParam(lineOp, "MinLength", 80.0, "double");
        AddParam(lineOp, "MaxGap", 10.0, "double");

        var measureDistanceOp = new Operator("MeasureDistance", OperatorType.Measurement, 0, 0);
        AddParam(measureDistanceOp, "MeasureType", "PointToPoint", "string");
        AddParam(measureDistanceOp, "X1", 80, "int");
        AddParam(measureDistanceOp, "Y1", 110, "int");
        AddParam(measureDistanceOp, "X2", 420, "int");
        AddParam(measureDistanceOp, "Y2", 360, "int");

        var pixelStatsOp = new Operator("PixelStatistics", OperatorType.PixelStatistics, 0, 0);
        AddParam(pixelStatsOp, "Channel", "Gray", "string");

        var pointLineOp = new Operator("PointLineDistance", OperatorType.PointLineDistance, 0, 0);
        AddParam(pointLineOp, "DistanceModel", "Segment", "string");

        var sharpnessOp = new Operator("SharpnessEvaluation", OperatorType.SharpnessEvaluation, 0, 0);
        AddParam(sharpnessOp, "Method", "Laplacian", "string");
        AddParam(sharpnessOp, "ThresholdMode", "PerMethodDefault", "string");

        var widthOp = new Operator("WidthMeasurement", OperatorType.WidthMeasurement, 0, 0);
        AddParam(widthOp, "MeasureMode", "ManualLines", "string");
        AddParam(widthOp, "SampleCount", 20, "int");
        AddParam(widthOp, "MultiScanCount", 24, "int");
        AddParam(widthOp, "RobustMode", true, "bool");
        AddParam(widthOp, "OutlierSigmaK", 3.0, "double");

        var fixedInputs = new List<Dictionary<string, object>>();
        var angleInputs = new Dictionary<string, object> { ["Image"] = CreateMeasurementImage(512, 512) };
        fixedInputs.Add(angleInputs);
        var caliperInputs = new Dictionary<string, object> { ["Image"] = CreateCaliperImage(512, 512) };
        fixedInputs.Add(caliperInputs);
        var circleInputs = new Dictionary<string, object> { ["Image"] = CreateCircleImage(512, 512) };
        fixedInputs.Add(circleInputs);
        var contourInputs = new Dictionary<string, object> { ["Image"] = CreateMeasurementImage(512, 512) };
        fixedInputs.Add(contourInputs);
        var colorInputs = new Dictionary<string, object>
        {
            ["Image"] = CreateMeasurementImage(512, 512),
            ["ReferenceColor"] = new Dictionary<string, object> { ["L"] = 0.0, ["A"] = 0.0, ["B"] = 0.0 }
        };
        fixedInputs.Add(colorInputs);
        var gapInputs = new Dictionary<string, object> { ["Image"] = CreateGapImage(512, 512) };
        fixedInputs.Add(gapInputs);
        var geoInputs = new Dictionary<string, object>
        {
            ["Element1"] = new LineData(60, 200, 460, 200),
            ["Element2"] = new LineData(260, 40, 260, 460)
        };
        fixedInputs.Add(geoInputs);
        var fittingInputs = new Dictionary<string, object> { ["Image"] = CreateCircleImage(512, 512) };
        fixedInputs.Add(fittingInputs);
        var toleranceInputs = new Dictionary<string, object>
        {
            ["FeaturePrimary"] = new LineData(60, 120, 460, 120),
            ["DatumA"] = new LineData(60, 220, 460, 220)
        };
        fixedInputs.Add(toleranceInputs);
        var histogramInputs = new Dictionary<string, object> { ["Image"] = CreateMeasurementImage(512, 512) };
        fixedInputs.Add(histogramInputs);
        var lineLineInputs = new Dictionary<string, object>
        {
            ["Line1"] = new LineData(20, 20, 420, 20),
            ["Line2"] = new LineData(20, 160, 420, 160)
        };
        fixedInputs.Add(lineLineInputs);
        var lineInputs = new Dictionary<string, object> { ["Image"] = CreateLineImage(512, 512) };
        fixedInputs.Add(lineInputs);
        var measureDistanceInputs = new Dictionary<string, object> { ["Image"] = CreateMeasurementImage(512, 512) };
        fixedInputs.Add(measureDistanceInputs);
        var pixelStatsInputs = new Dictionary<string, object> { ["Image"] = CreateMeasurementImage(512, 512) };
        fixedInputs.Add(pixelStatsInputs);
        var pointLineInputs = new Dictionary<string, object>
        {
            ["Point"] = new Position(240, 210),
            ["Line"] = new LineData(60, 200, 460, 200)
        };
        fixedInputs.Add(pointLineInputs);
        var sharpnessInputs = new Dictionary<string, object> { ["Image"] = CreateMeasurementImage(512, 512) };
        fixedInputs.Add(sharpnessInputs);
        var widthInputs = new Dictionary<string, object>
        {
            ["Image"] = CreateWidthImage(512, 512),
            ["Line1"] = new LineData(180, 120, 180, 390),
            ["Line2"] = new LineData(300, 120, 300, 390)
        };
        fixedInputs.Add(widthInputs);

        try
        {
            var cases = new List<MeasurementBudgetCase>
            {
                new("AngleMeasurement", 10.0, () => ExecuteCaseAsync("AngleMeasurement", angle, angleOp, angleInputs)),
                new("CaliperTool", 50.0, () => ExecuteCaseAsync("CaliperTool", caliper, caliperOp, caliperInputs)),
                new("CircleMeasurement", 30.0, () => ExecuteCaseAsync("CircleMeasurement", circle, circleOp, circleInputs)),
                new("ContourMeasurement", 40.0, () => ExecuteCaseAsync("ContourMeasurement", contour, contourOp, contourInputs)),
                new("ColorMeasurement", 22.5, () => ExecuteCaseAsync("ColorMeasurement", color, colorOp, colorInputs)),
                new("GapMeasurement", 30.0, () => ExecuteCaseAsync("GapMeasurement", gap, gapOp, gapInputs)),
                new("GeoMeasurement", 20.0, () => ExecuteCaseAsync("GeoMeasurement", geo, geoOp, geoInputs)),
                new("GeometricFitting", 35.0, () => ExecuteCaseAsync("GeometricFitting", fitting, fittingOp, fittingInputs)),
                new("GeometricTolerance", 20.0, () => ExecuteCaseAsync("GeometricTolerance", tolerance, toleranceOp, toleranceInputs)),
                new("HistogramAnalysis", 10.0, () => ExecuteCaseAsync("HistogramAnalysis", histogram, histogramOp, histogramInputs)),
                new("LineLineDistance", 10.0, () => ExecuteCaseAsync("LineLineDistance", lineLineDistance, lineLineOp, lineLineInputs)),
                new("LineMeasurement", 20.0, () => ExecuteCaseAsync("LineMeasurement", line, lineOp, lineInputs)),
                new("MeasureDistance", 10.0, () => ExecuteCaseAsync("MeasureDistance", measureDistance, measureDistanceOp, measureDistanceInputs)),
                new("PixelStatistics", 10.0, () => ExecuteCaseAsync("PixelStatistics", pixelStats, pixelStatsOp, pixelStatsInputs)),
                new("PointLineDistance", 10.0, () => ExecuteCaseAsync("PointLineDistance", pointLineDistance, pointLineOp, pointLineInputs)),
                new("SharpnessEvaluation", 15.0, () => ExecuteCaseAsync("SharpnessEvaluation", sharpness, sharpnessOp, sharpnessInputs)),
                new("WidthMeasurement", 30.0, () => ExecuteCaseAsync("WidthMeasurement", width, widthOp, widthInputs))
            };

            var entries = new List<PerformanceEntry>(cases.Count);
            foreach (var testCase in cases)
            {
                var allowed = testCase.BudgetMs * budgetScale;
                try
                {
                    StabilizeMeasurementEnvironment();
                    var stats = await MeasureAsync(testCase.ExecuteAsync, warmupIterations, measuredIterations);
                    var status = stats.P95Ms <= allowed ? "PASS" : "FAIL";
                    var notes = status == "PASS" ? "Within budget." : $"p95 {stats.P95Ms:F2}ms exceeded allowed {allowed:F2}ms.";
                    entries.Add(new PerformanceEntry(testCase.Name, testCase.BudgetMs, budgetScale, allowed, stats.MeanMs, stats.P95Ms, stats.P99Ms, status, notes, stats.SamplesMs));
                }
                catch (Exception ex)
                {
                    entries.Add(new PerformanceEntry(testCase.Name, testCase.BudgetMs, budgetScale, allowed, 0.0, 0.0, 0.0, "ERROR", $"Execution failed: {ex.Message}", []));
                }
            }

            var artifacts = WriteReport(entries, warmupIterations, measuredIterations, budgetScale, gateProfile);
            Console.WriteLine($"Measurement performance budget report written: {artifacts.MarkdownPath}");

            var failed = entries.Where(entry => !entry.Status.Equals("PASS", StringComparison.OrdinalIgnoreCase)).ToList();
            Assert.True(
                failed.Count == 0,
                "Measurement performance budget gate failed: " +
                string.Join("; ", failed.Select(item => $"{item.Name}({item.Status}): {item.Notes}")));
        }
        finally
        {
            foreach (var inputs in fixedInputs)
            {
                DisposeObjectGraph(inputs, new HashSet<object>(ReferenceEqualityComparer.Instance));
            }
        }
    }

    private static async Task<double> ExecuteCaseAsync(
        string name,
        OperatorBase executor,
        Operator op,
        IReadOnlyDictionary<string, object> fixedInputs)
    {
        var inputs = CreateInvocationInputs(fixedInputs);
        OperatorExecutionOutput? result = null;
        var elapsedMs = 0.0;
        try
        {
            var start = Stopwatch.GetTimestamp();
            result = await executor.ExecuteAsync(op, inputs);
            elapsedMs = (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
        }
        finally
        {
            // OperatorBase owns and releases the per-call image leases. Only dispose output resources and non-image input resources here.
            var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
            DisposeObjectGraph(result?.OutputData, visited);
            DisposeObjectGraph(inputs, visited, releaseImageWrappers: false);
        }

        if (result == null || !result.IsSuccess)
        {
            throw new InvalidOperationException($"{name} failed: {result?.ErrorMessage ?? "No execution result."}");
        }

        return elapsedMs;
    }

    private static void StabilizeMeasurementEnvironment()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private static Dictionary<string, object> CreateInvocationInputs(IReadOnlyDictionary<string, object> fixedInputs)
    {
        var inputs = new Dictionary<string, object>(fixedInputs.Count, StringComparer.Ordinal);
        var retained = new HashSet<ImageWrapper>(ReferenceEqualityComparer.Instance);
        foreach (var (name, value) in fixedInputs)
        {
            inputs[name] = value is ImageWrapper image && retained.Add(image)
                ? image.AddRef()
                : value;
        }

        return inputs;
    }

    private static async Task<PerfStats> MeasureAsync(Func<Task<double>> action, int warmupIterations, int measuredIterations)
    {
        for (var i = 0; i < warmupIterations; i++)
        {
            await action();
            await Task.Yield();
        }

        var samples = new List<double>(measuredIterations);
        for (var i = 0; i < measuredIterations; i++)
        {
            samples.Add(await action());
        }

        return new PerfStats(samples, samples.OrderBy(value => value).ToArray());
    }

    private static PerformanceReportArtifacts WriteReport(
        IReadOnlyList<PerformanceEntry> entries,
        int warmupIterations,
        int measuredIterations,
        double budgetScale,
        string gateProfile)
    {
        var reportDir = ResolvePerformanceReportDirectory("CV_MEASUREMENT_PERF_REPORT_DIR");
        Directory.CreateDirectory(reportDir);

        var reportPath = Path.Combine(reportDir, $"{ReportFileStem}.md");
        var jsonPath = Path.Combine(reportDir, $"{ReportFileStem}.json");
        var generatedAtUtc = DateTime.UtcNow;
        var environment = new PerformanceEnvironmentMetadata(
            Environment.MachineName,
            RuntimeInformation.OSDescription,
            RuntimeInformation.OSArchitecture.ToString(),
            RuntimeInformation.FrameworkDescription,
            Environment.Version.ToString(),
            RuntimeInformation.ProcessArchitecture.ToString(),
            Environment.ProcessorCount,
            Stopwatch.Frequency);
        var builder = new StringBuilder();
        builder.AppendLine("# Measurement Performance Budget Report");
        builder.AppendLine();
        builder.AppendLine($"Generated (UTC): {generatedAtUtc:O}");
        builder.AppendLine($"Gate Profile: {gateProfile}");
        builder.AppendLine($"Warmup Iterations: {warmupIterations}");
        builder.AppendLine($"Measured Iterations: {measuredIterations}");
        builder.AppendLine($"Budget Scale: {budgetScale.ToString("0.00", CultureInfo.InvariantCulture)}");
        builder.AppendLine($"Machine: {environment.MachineName}");
        builder.AppendLine($"OS: {environment.OSDescription} ({environment.OSArchitecture})");
        builder.AppendLine($"Runtime: {environment.FrameworkDescription}; .NET {environment.RuntimeVersion}");
        builder.AppendLine($"Process Architecture: {environment.ProcessArchitecture}");
        builder.AppendLine($"Processors: {environment.ProcessorCount}");
        builder.AppendLine($"Stopwatch Frequency: {environment.StopwatchFrequency}");
        builder.AppendLine($"Measured Sample Metadata: {entries.Sum(entry => entry.SamplesMs.Count)} total samples; {measuredIterations} requested samples per case");
        builder.AppendLine();
        builder.AppendLine("| Operator | Budget (ms) | Scale | Allowed P95 (ms) | Mean (ms) | P95 (ms) | P99 (ms) | Status | Notes |");
        builder.AppendLine("|---|---:|---:|---:|---:|---:|---:|---|---|");

        foreach (var entry in entries.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
        {
            builder.AppendLine(
                $"| {entry.Name} | {entry.BudgetMs.ToString("0.##", CultureInfo.InvariantCulture)} | {entry.Scale.ToString("0.00", CultureInfo.InvariantCulture)} | {entry.AllowedMs.ToString("0.##", CultureInfo.InvariantCulture)} | {entry.MeanMs.ToString("0.00", CultureInfo.InvariantCulture)} | {entry.P95Ms.ToString("0.00", CultureInfo.InvariantCulture)} | {entry.P99Ms.ToString("0.00", CultureInfo.InvariantCulture)} | {entry.Status} | {entry.Notes} |");
        }

        builder.AppendLine();
        builder.AppendLine("## Raw Measured Samples (ms)");
        builder.AppendLine();
        builder.AppendLine("| Operator | Sample Count | Samples (measurement order) |");
        builder.AppendLine("|---|---:|---|");
        foreach (var entry in entries.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
        {
            var samples = string.Join(", ", entry.SamplesMs.Select(sample => sample.ToString("0.0000", CultureInfo.InvariantCulture)));
            builder.AppendLine($"| {entry.Name} | {entry.SamplesMs.Count} | {samples} |");
        }

        File.WriteAllText(reportPath, builder.ToString());
        File.WriteAllText(
            jsonPath,
            JsonSerializer.Serialize(
                new
                {
                    GeneratedAtUtc = generatedAtUtc,
                    GateProfile = gateProfile,
                    WarmupIterations = warmupIterations,
                    MeasuredIterations = measuredIterations,
                    BudgetScale = budgetScale,
                    Environment = environment,
                    SampleMetadata = new
                    {
                        WarmupIterations = warmupIterations,
                        MeasuredIterations = measuredIterations,
                        TotalMeasuredSamples = entries.Sum(entry => entry.SamplesMs.Count),
                        SamplesPerEntry = entries.ToDictionary(entry => entry.Name, entry => entry.SamplesMs.Count, StringComparer.OrdinalIgnoreCase)
                    },
                    Entries = entries
                },
                new JsonSerializerOptions { WriteIndented = true }));
        return new PerformanceReportArtifacts(reportDir, reportPath, jsonPath, generatedAtUtc);
    }

    private static string ResolvePerformanceReportDirectory(string reportDirectoryEnvName)
    {
        var configured = GetEnvString(reportDirectoryEnvName, string.Empty);
        if (string.IsNullOrWhiteSpace(configured))
        {
            configured = GetEnvString("CV_PERF_REPORT_DIR", string.Empty);
        }

        if (!string.IsNullOrWhiteSpace(configured))
        {
            return Path.GetFullPath(configured);
        }

        var repoRoot = ResolveClearVisionProductRoot();
        return Path.Combine(repoRoot, "test_results");
    }

    private static string ResolveClearVisionProductRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null)
        {
            var srcPath = Path.Combine(current.FullName, "src");
            var testsPath = Path.Combine(current.FullName, "tests");
            if (Directory.Exists(srcPath) && Directory.Exists(testsPath))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        return Directory.GetCurrentDirectory();
    }

    private static int GetEnvInt(string name, int defaultValue, int min, int max)
    {
        var raw = Environment.GetEnvironmentVariable(name);
        if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            return defaultValue;
        }

        return Math.Clamp(parsed, min, max);
    }

    private static double GetEnvDouble(string name, double defaultValue, double min, double max)
    {
        var raw = Environment.GetEnvironmentVariable(name);
        if (!double.TryParse(raw, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var parsed))
        {
            return defaultValue;
        }

        return Math.Clamp(parsed, min, max);
    }

    private static string GetEnvString(string name, string defaultValue)
    {
        var raw = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(raw) ? defaultValue : raw.Trim();
    }

    private static void AddParam(Operator op, string name, object value, string dataType)
    {
        op.AddParameter(new Parameter(Guid.NewGuid(), name, name, string.Empty, dataType, value));
    }

    private static ImageWrapper CreateMeasurementImage(int width, int height)
    {
        var mat = new Mat(height, width, MatType.CV_8UC3, Scalar.Black);
        Cv2.Rectangle(mat, new Rect(50, 50, width / 3, height / 4), Scalar.White, -1);
        Cv2.Rectangle(mat, new Rect(width / 2, height / 2, width / 3 - 20, height / 3 - 20), new Scalar(180, 180, 180), -1);
        Cv2.Circle(mat, new Point(width / 2, height / 3), 70, new Scalar(255, 255, 255), 2);
        Cv2.Line(mat, new Point(30, height - 40), new Point(width - 30, 40), new Scalar(0, 255, 0), 3);
        Cv2.Line(mat, new Point(30, 40), new Point(width - 30, height - 40), new Scalar(0, 255, 255), 2);
        return new ImageWrapper(mat);
    }

    private static ImageWrapper CreateCircleImage(int width, int height)
    {
        var mat = new Mat(height, width, MatType.CV_8UC1, Scalar.Black);
        Cv2.Circle(mat, new Point(width / 2, height / 2), 90, Scalar.White, 3);
        Cv2.Circle(mat, new Point(width / 2, height / 2), 45, Scalar.White, 2);
        return new ImageWrapper(mat);
    }

    private static ImageWrapper CreateLineImage(int width, int height)
    {
        var mat = new Mat(height, width, MatType.CV_8UC1, Scalar.Black);
        Cv2.Line(mat, new Point(40, 80), new Point(width - 40, 100), Scalar.White, 2);
        Cv2.Line(mat, new Point(70, height / 2), new Point(width - 70, height / 2), Scalar.White, 3);
        Cv2.Line(mat, new Point(100, height - 90), new Point(width - 100, height - 130), Scalar.White, 2);
        return new ImageWrapper(mat);
    }

    private static ImageWrapper CreateCaliperImage(int width, int height)
    {
        var mat = new Mat(height, width, MatType.CV_8UC1, Scalar.Black);
        Cv2.Rectangle(mat, new Rect(width / 2 - 60, height / 4, 120, height / 2), Scalar.White, -1);
        return new ImageWrapper(mat);
    }

    private static ImageWrapper CreateGapImage(int width, int height)
    {
        var mat = new Mat(height, width, MatType.CV_8UC1, Scalar.Black);
        foreach (var x in new[] { 80, 160, 240, 320, 400 })
        {
            Cv2.Line(mat, new Point(x, 30), new Point(x, height - 30), Scalar.White, 2);
        }

        return new ImageWrapper(mat);
    }

    private static ImageWrapper CreateWidthImage(int width, int height)
    {
        var mat = new Mat(height, width, MatType.CV_8UC1, Scalar.Black);
        Cv2.Rectangle(mat, new Rect(190, 110, 100, 290), Scalar.White, -1);
        return new ImageWrapper(mat);
    }

    private static void DisposeObjectGraph(
        object? value,
        HashSet<object> visited,
        bool releaseImageWrappers = true)
    {
        if (value == null)
        {
            return;
        }

        if (value is not ValueType && value is not string && !visited.Add(value))
        {
            return;
        }

        if (value is ImageWrapper imageWrapper)
        {
            if (releaseImageWrappers)
            {
                imageWrapper.Release();
            }

            return;
        }

        if (value is IDisposable disposable)
        {
            disposable.Dispose();
            return;
        }

        if (value is IDictionary dictionary)
        {
            foreach (DictionaryEntry entry in dictionary)
            {
                DisposeObjectGraph(entry.Value, visited, releaseImageWrappers);
            }

            return;
        }

        if (value is IEnumerable enumerable && value is not string)
        {
            foreach (var item in enumerable)
            {
                DisposeObjectGraph(item, visited, releaseImageWrappers);
            }
        }
    }

    private static double Percentile(IReadOnlyList<double> orderedValues, double percentile)
    {
        if (orderedValues.Count == 0)
        {
            return 0.0;
        }

        var index = (int)Math.Ceiling(percentile * orderedValues.Count) - 1;
        index = Math.Clamp(index, 0, orderedValues.Count - 1);
        return orderedValues[index];
    }

    private sealed record MeasurementBudgetCase(string Name, double BudgetMs, Func<Task<double>> ExecuteAsync);

    private sealed record PerfStats(IReadOnlyList<double> SamplesMs, IReadOnlyList<double> OrderedSamplesMs)
    {
        public double MeanMs => SamplesMs.Count == 0 ? 0 : SamplesMs.Average();
        public double P95Ms => Percentile(OrderedSamplesMs, 0.95);
        public double P99Ms => Percentile(OrderedSamplesMs, 0.99);
    }

    private sealed record PerformanceEntry(
        string Name,
        double BudgetMs,
        double Scale,
        double AllowedMs,
        double MeanMs,
        double P95Ms,
        double P99Ms,
        string Status,
        string Notes,
        IReadOnlyList<double> SamplesMs);

    private sealed record PerformanceEnvironmentMetadata(
        string MachineName,
        string OSDescription,
        string OSArchitecture,
        string FrameworkDescription,
        string RuntimeVersion,
        string ProcessArchitecture,
        int ProcessorCount,
        long StopwatchFrequency);

    private sealed record PerformanceReportArtifacts(
        string ReportDirectory,
        string MarkdownPath,
        string JsonPath,
        DateTime GeneratedAtUtc);
}
