using System.Collections;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Operators;
using ClearVision.Product.Core.ValueObjects;
using ClearVision.Product.Infrastructure.Operators;
using Microsoft.Extensions.Logging.Abstractions;
using OpenCvSharp;

var options = BenchmarkOptions.Parse(args);
var benchmark = new ProductOperatorBenchmark(options);
var report = benchmark.Run();

Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(options.OutputPath))!);
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(options.ReportPath))!);
var jsonOptions = new JsonSerializerOptions
{
    WriteIndented = true,
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
};
File.WriteAllText(options.OutputPath, JsonSerializer.Serialize(report, jsonOptions), new UTF8Encoding(false));
File.WriteAllText(options.ReportPath, BenchmarkMarkdown.Create(report), new UTF8Encoding(false));
Console.WriteLine($"Product operator end-to-end benchmark complete: {report.Metrics.Count} metric rows; generatedDataSha256={report.Dataset.GeneratedDataSha256}");

internal sealed class ProductOperatorBenchmark
{
    private static readonly string[] ProductSourceFiles =
    [
        "ClearVision.Product/src/ClearVision.Product.Infrastructure/Operators/CircleMeasurementOperator.cs",
        "ClearVision.Product/src/ClearVision.Product.Infrastructure/Operators/CircleCaliperFitV2Kernel.cs",
        "ClearVision.Product/src/ClearVision.Product.Infrastructure/Operators/LineMeasurementOperator.cs",
        "ClearVision.Product/src/ClearVision.Product.Infrastructure/Operators/IndustrialCaliperKernel.cs",
        "ClearVision.Product/src/ClearVision.Product.Infrastructure/Operators/GeometryRefinementKernel.cs",
        "ClearVision.Product/src/ClearVision.Product.Infrastructure/Operators/OperatorBase.cs"
    ];

    private readonly BenchmarkOptions _options;
    private readonly JsonDocument _manifest;
    private readonly JsonElement _root;
    private readonly int _seed;

    public ProductOperatorBenchmark(BenchmarkOptions options)
    {
        _options = options;
        _manifest = JsonDocument.Parse(File.ReadAllBytes(options.ManifestPath));
        _root = _manifest.RootElement;
        _seed = _root.GetProperty("seed").GetInt32();
    }

    public ProductBenchmarkReport Run()
    {
        var manifestSha = HashFile(_options.ManifestPath);
        ValidateManifestCompanionHash(manifestSha);
        var circleCases = GenerateCircleCases();
        var lineCases = GenerateLineCases();
        var generatedDataSha = HashGeneratedCases(circleCases, lineCases);
        var expectedDataSha = _root.GetProperty("expectedGeneratedDataSha256").GetString() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(expectedDataSha) && !expectedDataSha.Equals(generatedDataSha, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Generated raster/truth SHA drifted. Expected={expectedDataSha}, actual={generatedDataSha}.");
        }

        var metrics = new List<ProductBenchmarkMetric>();
        AddDomainMetrics("Circle", "LegacyDefault", "radius_px", circleCases, item => EvaluateCircle(item, robust: false));
        AddDomainMetrics("Line", "L2Default", "angle_deg", lineCases, item => EvaluateLine(item, robust: false));
        if (_options.IncludeCandidates)
        {
            AddDomainMetrics("Circle", "WelschOptIn", "radius_px", circleCases, item => EvaluateCircle(item, robust: true));
            AddDomainMetrics("Line", "WelschOptIn", "angle_deg", lineCases, item => EvaluateLine(item, robust: true));
        }

        var process = Process.GetCurrentProcess();
        process.Refresh();
        return new ProductBenchmarkReport(
            "2026-07-16.operator-product-e2e-report.v1",
            "clearvision-operator-product-e2e-v1",
            _options.Label,
            DateTime.UtcNow,
            new ProductDatasetIdentity(
                _root.GetProperty("datasetId").GetString()!,
                _root.GetProperty("datasetVersion").GetString()!,
                manifestSha,
                generatedDataSha,
                _seed,
                _root.GetProperty("license").GetString()!,
                _root.GetProperty("claimBoundary").GetString()!,
                BuildSplitCounts(circleCases, lineCases)),
            BuildProductIdentity(),
            BuildHarnessIdentity(),
            BuildEnvironment(),
            new ResourceMeasurementScope(
                "Elapsed wall-clock time around the complete formal OperatorBase.ExecuteAsync call, including image decode, Canny/profile sampling, candidate selection, overlays and output construction.",
                "GC.GetAllocatedBytesForCurrentThread on the benchmark thread only; it is not full-process allocation or native OpenCV allocation.",
                "Peak working set and private bytes are full benchmark-process observations after all metric rows; they are not per-operator attribution."),
            new ProcessResourceSnapshot(process.PeakWorkingSet64, process.PrivateMemorySize64, process.WorkingSet64),
            _options.Warmup,
            _options.Iterations,
            metrics,
            BuildModeClaims());

        void AddDomainMetrics<T>(string domain, string algorithm, string unit, IReadOnlyList<T> cases, Func<T, EvaluationSample> evaluate)
            where T : ProductCase
        {
            metrics.Add(Evaluate(domain, algorithm, unit, cases, evaluate, "validation"));
            metrics.Add(Evaluate(domain, algorithm, unit, cases, evaluate, "test"));
        }
    }

    private ProductBenchmarkMetric Evaluate<T>(
        string domain,
        string algorithm,
        string unit,
        IReadOnlyList<T> cases,
        Func<T, EvaluationSample> evaluate,
        string split)
        where T : ProductCase
    {
        var selected = cases.Where(item => item.Split == split).ToArray();
        var samples = selected.Select(item => SafeEvaluate(item, evaluate)).ToArray();
        var timings = Measure(selected, evaluate);
        var valid = samples.Where(sample => !sample.Failed && double.IsFinite(sample.SignedError)).ToArray();
        var absoluteErrors = valid.Select(sample => Math.Abs(sample.SignedError)).OrderBy(value => value).ToArray();
        var secondary = valid.Where(sample => double.IsFinite(sample.SecondaryError)).Select(sample => sample.SecondaryError).OrderBy(value => value).ToArray();
        var residuals = valid.Where(sample => double.IsFinite(sample.Residual)).Select(sample => sample.Residual).OrderBy(value => value).ToArray();
        var failureTaxonomy = samples
            .Where(sample => sample.Failed)
            .GroupBy(sample => string.IsNullOrWhiteSpace(sample.FailureCode) ? "Unknown" : sample.FailureCode)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);

        return new ProductBenchmarkMetric(
            domain,
            algorithm,
            split,
            unit,
            selected.Length,
            valid.Length == 0 ? double.NaN : valid.Average(sample => sample.SignedError),
            valid.Length == 0 ? double.NaN : Math.Sqrt(valid.Average(sample => sample.SignedError * sample.SignedError)),
            Percentile(absoluteErrors, 0.95),
            samples.Count(sample => sample.Failed) / (double)Math.Max(samples.Length, 1),
            samples.Count(sample => sample.Ambiguous) / (double)Math.Max(samples.Length, 1),
            samples.Average(sample => sample.OutlierRate),
            timings.P50Milliseconds,
            timings.P95Milliseconds,
            timings.ManagedAllocatedBytesPerCase,
            new Dictionary<string, double>
            {
                ["secondaryRmse"] = secondary.Length == 0 ? double.NaN : Math.Sqrt(secondary.Average(value => value * value)),
                ["secondaryP95"] = Percentile(secondary, 0.95),
                ["residualMean"] = residuals.Length == 0 ? double.NaN : residuals.Average(),
                ["residualP95"] = Percentile(residuals, 0.95),
                ["diagnosticCompletenessRate"] = samples.Count(sample => sample.DiagnosticsComplete) / (double)Math.Max(samples.Length, 1)
            },
            failureTaxonomy);
    }

    private static EvaluationSample SafeEvaluate<T>(T item, Func<T, EvaluationSample> evaluate)
    {
        try
        {
            return evaluate(item);
        }
        catch (Exception ex)
        {
            return EvaluationSample.Failure(ex.GetType().Name);
        }
    }

    private TimingSummary Measure<T>(IReadOnlyList<T> cases, Func<T, EvaluationSample> evaluate)
    {
        foreach (var item in cases)
        {
            for (var iteration = 0; iteration < _options.Warmup; iteration++)
            {
                _ = SafeEvaluate(item, evaluate);
            }
        }

        var elapsed = new List<double>(cases.Count * _options.Iterations);
        long allocated = 0;
        foreach (var item in cases)
        {
            for (var iteration = 0; iteration < _options.Iterations; iteration++)
            {
                var before = GC.GetAllocatedBytesForCurrentThread();
                var started = Stopwatch.GetTimestamp();
                _ = SafeEvaluate(item, evaluate);
                elapsed.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
                allocated += Math.Max(0, GC.GetAllocatedBytesForCurrentThread() - before);
            }
        }

        elapsed.Sort();
        return new TimingSummary(
            Percentile(elapsed, 0.50),
            Percentile(elapsed, 0.95),
            allocated / (double)Math.Max(elapsed.Count, 1));
    }

    private EvaluationSample EvaluateCircle(CircleProductCase item, bool robust)
    {
        var config = _root.GetProperty("circle").GetProperty("operator");
        var op = CreateOperator(
            OperatorType.CircleMeasurement,
            ("Method", config.GetProperty("method").GetString()!),
            ("MinRadius", (int)Math.Floor(item.Radius - config.GetProperty("radiusSearchHalfWidth").GetInt32())),
            ("MaxRadius", (int)Math.Ceiling(item.Radius + config.GetProperty("radiusSearchHalfWidth").GetInt32())),
            ("SearchCenterMode", "Explicit"),
            ("SearchCenterX", item.SeedCenterX),
            ("SearchCenterY", item.SeedCenterY),
            ("NominalRadius", item.Radius),
            ("CaliperCount", config.GetProperty("caliperCount").GetInt32()),
            ("AveragingThickness", config.GetProperty("averagingThickness").GetDouble()),
            ("ProfileSampleCount", config.GetProperty("profileSampleCount").GetInt32()),
            ("GaussianSigma", config.GetProperty("gaussianSigma").GetDouble()),
            ("EdgePolarity", item.EdgePolarity),
            ("EdgeThreshold", 0.0),
            ("MinEdgeStrength", config.GetProperty("minEdgeStrength").GetDouble()),
            ("MinValidCalipers", config.GetProperty("minValidCalipers").GetInt32()),
            ("MinCoverageRatio", config.GetProperty("minCoverageRatio").GetDouble()),
            ("MinAngularCoverageDegrees", config.GetProperty("minAngularCoverageDegrees").GetDouble()),
            ("OutlierMode", item.OutlierMode),
            ("OutlierThreshold", config.GetProperty("outlierThreshold").GetDouble()),
            ("MaxOutlierIterations", config.GetProperty("maxOutlierIterations").GetInt32()),
            ("MaxResidualRmse", config.GetProperty("maxResidualRmse").GetDouble()));
        if (robust)
        {
            AddParameter(op, "RefinementLoss", "Welsch");
        }

        var executor = new CircleMeasurementOperator(NullLogger<CircleMeasurementOperator>.Instance);
        var output = Execute(executor, op, item.ImagePng);
        try
        {
            if (!output.IsSuccess || output.OutputData == null)
            {
                var failureCode = ReadString(output.OutputData, "StatusCode") ?? ParseFailureCode(output.ErrorMessage);
                return EvaluationSample.Failure(failureCode, failureCode.Contains("Ambiguous", StringComparison.OrdinalIgnoreCase), HasCircleDiagnostics(output.OutputData));
            }

            var radius = ReadDouble(output.OutputData, "Radius");
            var center = output.OutputData.TryGetValue("Center", out var centerValue) ? centerValue as Position : null;
            var centerError = center == null
                ? double.NaN
                : Math.Sqrt(Math.Pow(center.X - item.CenterX, 2) + Math.Pow(center.Y - item.CenterY, 2));
            var resultObject = output.OutputData.GetValueOrDefault("CaliperFitV2Result");
            var rejected = ReadReflectedInt(resultObject, "RejectedCaliperCount");
            var collected = Math.Max(ReadCollectionCount(output.OutputData.GetValueOrDefault("EdgePoints")), 1);
            var residual = ReadDouble(output.OutputData, "ResidualRmse");
            return new EvaluationSample(
                radius - item.Radius,
                centerError,
                false,
                false,
                Math.Clamp(rejected / (double)collected, 0, 1),
                residual,
                string.Empty,
                HasCircleDiagnostics(output.OutputData));
        }
        finally
        {
            ReleaseOutputImage(output);
        }
    }

    private EvaluationSample EvaluateLine(LineProductCase item, bool robust)
    {
        var config = _root.GetProperty("line").GetProperty("operator");
        var op = CreateOperator(
            OperatorType.LineMeasurement,
            ("Method", config.GetProperty("method").GetString()!),
            ("Threshold", config.GetProperty("threshold").GetInt32()),
            ("MinLength", config.GetProperty("minLength").GetDouble()),
            ("MaxGap", config.GetProperty("maxGap").GetDouble()));
        if (robust)
        {
            AddParameter(op, "FitLoss", "Welsch");
        }

        var executor = new LineMeasurementOperator(NullLogger<LineMeasurementOperator>.Instance);
        var output = Execute(executor, op, item.ImagePng);
        try
        {
            if (!output.IsSuccess || output.OutputData == null)
            {
                return EvaluationSample.Failure(ParseFailureCode(output.ErrorMessage), diagnosticsComplete: output.OutputData?.ContainsKey("StatusCode") == true);
            }

            var angle = ReadDouble(output.OutputData, "Angle");
            var lineCount = Math.Max(ReadInt(output.OutputData, "LineCount"), 0);
            var residual = ReadDouble(output.OutputData, "ResidualMean");
            var outliers = Math.Max(ReadInt(output.OutputData, "OutlierCount"), 0);
            var pointCount = Math.Max(ReadInt(output.OutputData, "RefinedPointCount"), ReadInt(output.OutputData, "FitPointCount"));
            var outlierRate = pointCount > 0 ? Math.Clamp(outliers / (double)pointCount, 0, 1) : 0;
            return new EvaluationSample(
                SignedAngleDifference(angle, item.AngleDegrees),
                double.NaN,
                false,
                lineCount > 1,
                outlierRate,
                residual,
                string.Empty,
                HasLineDiagnostics(output.OutputData, robust));
        }
        finally
        {
            ReleaseOutputImage(output);
        }
    }

    private static OperatorExecutionOutput Execute(IOperatorExecutor executor, Operator op, byte[] imagePng)
    {
        var input = new ImageWrapper(imagePng.ToArray());
        return executor.ExecuteAsync(op, new Dictionary<string, object> { ["Image"] = input }).GetAwaiter().GetResult();
    }

    private IReadOnlyList<CircleProductCase> GenerateCircleCases()
    {
        var config = _root.GetProperty("circle");
        var scenarios = config.GetProperty("scenarios").EnumerateArray().Select(item => item.GetString()!).ToArray();
        var bundles = config.GetProperty("bundleCount").GetInt32();
        var width = config.GetProperty("imageWidth").GetInt32();
        var height = config.GetProperty("imageHeight").GetInt32();
        var radiusRange = ReadRange(config, "radiusRange");
        var centerJitter = config.GetProperty("centerJitter").GetDouble();
        var seedOffsetRange = ReadRange(config, "seedOffsetRange");
        var cases = new List<CircleProductCase>(bundles * scenarios.Length);

        for (var bundle = 0; bundle < bundles; bundle++)
        {
            foreach (var (scenario, scenarioIndex) in scenarios.Select((value, index) => (value, index)))
            {
                var random = new Random(_seed + 101 + (bundle * 1009) + (scenarioIndex * 37));
                var radius = Next(random, radiusRange.Min, radiusRange.Max);
                var centerX = (width / 2.0) + Next(random, -centerJitter, centerJitter);
                var centerY = (height / 2.0) + Next(random, -centerJitter, centerJitter);
                var seedX = centerX + Next(random, seedOffsetRange.Min, seedOffsetRange.Max);
                var seedY = centerY + Next(random, seedOffsetRange.Min, seedOffsetRange.Max);
                var darkCircle = scenario == "polarity" && bundle % 2 == 1;
                var background = darkCircle ? (byte)235 : (byte)18;
                var foreground = darkCircle ? (byte)18 : (byte)235;
                using var image = new Mat(height, width, MatType.CV_8UC3, new Scalar(background, background, background));
                var center = new Point((int)Math.Round(centerX), (int)Math.Round(centerY));
                Cv2.Circle(image, center, (int)Math.Round(radius), new Scalar(foreground, foreground, foreground), -1, LineTypes.AntiAlias);

                switch (scenario)
                {
                    case "blur_noise":
                        Cv2.GaussianBlur(image, image, new Size(7, 7), 1.6);
                        AddNoise(image, random, 6.0);
                        break;
                    case "short_arc":
                        Cv2.Ellipse(image, center, new Size((int)Math.Round(radius), (int)Math.Round(radius)), 0, 18, 132, new Scalar(background, background, background), 13, LineTypes.AntiAlias);
                        Cv2.GaussianBlur(image, image, new Size(5, 5), 0.9);
                        break;
                    case "outlier_bumps":
                        foreach (var angleDegrees in new[] { 28.0, 154.0, 276.0 })
                        {
                            var radians = angleDegrees * Math.PI / 180.0;
                            var bumpCenter = new Point(
                                (int)Math.Round(centerX + ((radius + 4.5) * Math.Cos(radians))),
                                (int)Math.Round(centerY + ((radius + 4.5) * Math.Sin(radians))));
                            Cv2.Circle(image, bumpCenter, 9, new Scalar(foreground, foreground, foreground), -1, LineTypes.AntiAlias);
                        }
                        AddNoise(image, random, 2.0);
                        break;
                    case "ellipse_interference":
                        Cv2.Ellipse(image, center, new Size((int)Math.Round(radius + 9), (int)Math.Round(radius - 5)), 17, 205, 345, new Scalar(foreground, foreground, foreground), 4, LineTypes.AntiAlias);
                        AddNoise(image, random, 2.5);
                        break;
                    case "polarity":
                        Cv2.GaussianBlur(image, image, new Size(5, 5), 0.8);
                        AddNoise(image, random, 1.5);
                        break;
                }

                cases.Add(new CircleProductCase(
                    $"circle-b{bundle:D2}-{scenario}",
                    scenario,
                    SplitForBundle(bundle),
                    image.ToBytes(".png"),
                    centerX,
                    centerY,
                    radius,
                    seedX,
                    seedY,
                    darkCircle ? "DarkToLight" : "LightToDark",
                    bundle % 2 == 0 ? "Mad" : "Huber"));
            }
        }

        return cases;
    }

    private IReadOnlyList<LineProductCase> GenerateLineCases()
    {
        var config = _root.GetProperty("line");
        var scenarios = config.GetProperty("scenarios").EnumerateArray().Select(item => item.GetString()!).ToArray();
        var bundles = config.GetProperty("bundleCount").GetInt32();
        var width = config.GetProperty("imageWidth").GetInt32();
        var height = config.GetProperty("imageHeight").GetInt32();
        var angleRange = ReadRange(config, "angleDegreesRange");
        var lengthRange = ReadRange(config, "lengthRange");
        var thicknessRange = config.GetProperty("thicknessRange").EnumerateArray().Select(item => item.GetInt32()).ToArray();
        var cases = new List<LineProductCase>(bundles * scenarios.Length);

        for (var bundle = 0; bundle < bundles; bundle++)
        {
            foreach (var (scenario, scenarioIndex) in scenarios.Select((value, index) => (value, index)))
            {
                var random = new Random(_seed + 503 + (bundle * 1013) + (scenarioIndex * 41));
                var angle = Next(random, angleRange.Min, angleRange.Max);
                var radians = angle * Math.PI / 180.0;
                var length = Next(random, lengthRange.Min, lengthRange.Max);
                var centerX = (width / 2.0) + Next(random, -7.0, 7.0);
                var centerY = (height / 2.0) + Next(random, -7.0, 7.0);
                var directionX = Math.Cos(radians);
                var directionY = Math.Sin(radians);
                var normalX = -directionY;
                var normalY = directionX;
                var start = new Point(
                    (int)Math.Round(centerX - (directionX * length / 2.0)),
                    (int)Math.Round(centerY - (directionY * length / 2.0)));
                var end = new Point(
                    (int)Math.Round(centerX + (directionX * length / 2.0)),
                    (int)Math.Round(centerY + (directionY * length / 2.0)));
                var thickness = random.Next(thicknessRange[0], thicknessRange[1] + 1);
                using var image = new Mat(height, width, MatType.CV_8UC3, Scalar.Black);
                Cv2.Line(image, start, end, Scalar.White, thickness, LineTypes.AntiAlias);

                Point At(double t, double normalOffset = 0) => new(
                    (int)Math.Round(start.X + ((end.X - start.X) * t) + (normalX * normalOffset)),
                    (int)Math.Round(start.Y + ((end.Y - start.Y) * t) + (normalY * normalOffset)));

                switch (scenario)
                {
                    case "blur_noise":
                        Cv2.GaussianBlur(image, image, new Size(7, 7), 1.5);
                        AddNoise(image, random, 7.0);
                        break;
                    case "broken_edge":
                        foreach (var t in new[] { 0.24, 0.52, 0.78 }) Cv2.Circle(image, At(t), 8, Scalar.Black, -1, LineTypes.AntiAlias);
                        AddNoise(image, random, 2.0);
                        break;
                    case "spur":
                        foreach (var t in new[] { 0.30, 0.67 })
                        {
                            var anchor = At(t);
                            var p1 = new Point((int)Math.Round(anchor.X - (normalX * 28)), (int)Math.Round(anchor.Y - (normalY * 28)));
                            var p2 = new Point((int)Math.Round(anchor.X + (normalX * 20)), (int)Math.Round(anchor.Y + (normalY * 20)));
                            Cv2.Line(image, p1, p2, Scalar.White, 4, LineTypes.AntiAlias);
                        }
                        break;
                    case "occlusion":
                        foreach (var t in new[] { 0.38, 0.72 }) Cv2.Rectangle(image, new Rect(At(t).X - 10, At(t).Y - 10, 20, 20), Scalar.Black, -1);
                        AddNoise(image, random, 2.0);
                        break;
                    case "parallel_interference":
                        Cv2.Line(image, At(0.18, 12), At(0.72, 12), Scalar.White, 4, LineTypes.AntiAlias);
                        Cv2.Line(image, At(0.56, -15), At(0.88, -15), Scalar.White, 3, LineTypes.AntiAlias);
                        AddNoise(image, random, 2.5);
                        break;
                }

                cases.Add(new LineProductCase(
                    $"line-b{bundle:D2}-{scenario}",
                    scenario,
                    SplitForBundle(bundle),
                    image.ToBytes(".png"),
                    NormalizeAngle(angle)));
            }
        }

        return cases;
    }

    private ProductImplementationIdentity BuildProductIdentity()
    {
        var sourceHashes = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var relativePath in ProductSourceFiles)
        {
            var fullPath = Path.Combine(_options.ProductRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(fullPath)) sourceHashes[relativePath] = HashFile(fullPath);
        }

        return new ProductImplementationIdentity(
            _options.ProductSourceSha,
            _options.ProductRepositoryDirty,
            "CircleMeasurementOperator.ExecuteAsync and LineMeasurementOperator.ExecuteAsync via OperatorBase lifecycle with ImageWrapper raster input",
            sourceHashes,
            HashFile(typeof(CircleMeasurementOperator).Assembly.Location),
            HashFile(typeof(Operator).Assembly.Location),
            typeof(CircleMeasurementOperator).Assembly.GetName().Version?.ToString() ?? "unknown");
    }

    private HarnessIdentity BuildHarnessIdentity()
    {
        var programPath = FindHarnessSource("Program.cs");
        var projectPath = FindHarnessSource("OperatorProductE2EBenchmarkRunner.csproj");
        return new HarnessIdentity(
            _options.HarnessCommitSha,
            _options.HarnessRepositoryDirty,
            HashFile(programPath),
            HashFile(projectPath),
            HashFile(_options.ManifestPath),
            _options.AdapterInjectedIntoProductWorktree,
            _options.AdapterInjectedIntoProductWorktree ? "isolated detached worktree build" : "current committed worktree build");
    }

    private string FindHarnessSource(string fileName)
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, fileName),
            Path.Combine(_options.HarnessSourceDirectory, fileName)
        };
        return candidates.First(File.Exists);
    }

    private BenchmarkEnvironment BuildEnvironment() => new(
        RuntimeInformation.FrameworkDescription,
        RuntimeInformation.OSDescription,
        RuntimeInformation.ProcessArchitecture.ToString(),
        Environment.ProcessorCount,
        Environment.Version.ToString(),
        Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER") ?? "unknown",
        _options.SdkVersion,
        Cv2.GetVersionString() ?? "unknown",
        GCSettings.IsServerGC,
        GCSettings.LatencyMode.ToString());

    private IReadOnlyList<ModeClaim> BuildModeClaims()
    {
        var claims = new List<ModeClaim>
        {
            new("CircleMeasurement", "Method=CaliperFitV2; RefinementLoss=Legacy(default)", true, false, "Formal legacy/default product path"),
            new("LineMeasurement", "Method=FitLine; FitLoss=L2(default)", true, false, "Formal legacy/default product path")
        };
        if (_options.IncludeCandidates)
        {
            claims.Add(new("CircleMeasurement", "Method=CaliperFitV2; RefinementLoss=Welsch", false, true, "Opt-in candidate; adoption is decided by validation then independently reported on test"));
            claims.Add(new("LineMeasurement", "Method=FitLine; FitLoss=Welsch", false, true, "Opt-in candidate; adoption is decided by validation then independently reported on test"));
        }
        return claims;
    }

    private static IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>> BuildSplitCounts(
        IReadOnlyList<CircleProductCase> circles,
        IReadOnlyList<LineProductCase> lines) =>
        new Dictionary<string, IReadOnlyDictionary<string, int>>(StringComparer.Ordinal)
        {
            ["Circle"] = circles.GroupBy(item => item.Split).ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal),
            ["Line"] = lines.GroupBy(item => item.Split).ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal)
        };

    private static string HashGeneratedCases(IReadOnlyList<CircleProductCase> circles, IReadOnlyList<LineProductCase> lines)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var item in circles.Cast<ProductCase>().Concat(lines).OrderBy(item => item.CaseId, StringComparer.Ordinal))
        {
            Append(hash, item.CaseId);
            Append(hash, item.Domain);
            Append(hash, item.Scenario);
            Append(hash, item.Split);
            Append(hash, item.TruthIdentity);
            Append(hash, Convert.ToHexString(SHA256.HashData(item.ImagePng)).ToLowerInvariant());
        }
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private void ValidateManifestCompanionHash(string actual)
    {
        var companion = Path.ChangeExtension(_options.ManifestPath, ".sha256");
        if (!File.Exists(companion)) return;
        var expected = File.ReadAllText(companion).Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
        if (!expected.Equals(actual, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Product E2E manifest SHA drifted. Expected={expected}, actual={actual}.");
        }
    }

    private static void AddNoise(Mat image, Random random, double sigma)
    {
        for (var y = 0; y < image.Rows; y++)
        {
            for (var x = 0; x < image.Cols; x++)
            {
                var pixel = image.At<Vec3b>(y, x);
                var noise = NextGaussian(random) * sigma;
                pixel.Item0 = ClampByte(pixel.Item0 + noise);
                pixel.Item1 = ClampByte(pixel.Item1 + noise);
                pixel.Item2 = ClampByte(pixel.Item2 + noise);
                image.Set(y, x, pixel);
            }
        }
    }

    private static byte ClampByte(double value) => (byte)Math.Clamp((int)Math.Round(value), 0, 255);
    private static double NextGaussian(Random random) => Math.Sqrt(-2.0 * Math.Log(Math.Max(random.NextDouble(), 1e-12))) * Math.Cos(2.0 * Math.PI * random.NextDouble());
    private static double Next(Random random, double min, double max) => min + ((max - min) * random.NextDouble());
    private static (double Min, double Max) ReadRange(JsonElement parent, string name)
    {
        var values = parent.GetProperty(name).EnumerateArray().Select(item => item.GetDouble()).ToArray();
        return (values[0], values[1]);
    }

    private static string SplitForBundle(int bundle) => (bundle % 5) switch { 0 => "train", 1 => "validation", _ => "test" };

    private static Operator CreateOperator(OperatorType type, params (string Name, object Value)[] parameters)
    {
        var op = new Operator(Guid.NewGuid(), $"ProductE2E-{type}", type, 0, 0);
        foreach (var (name, value) in parameters) AddParameter(op, name, value);
        return op;
    }

    private static void AddParameter(Operator op, string name, object value) =>
        op.AddParameter(new Parameter(Guid.NewGuid(), name, name, string.Empty, InferType(value), value, isRequired: false));

    private static string InferType(object value) => value switch { int or long => "int", float or double or decimal => "double", bool => "bool", _ => "string" };

    private static double ReadDouble(Dictionary<string, object>? values, string name)
    {
        if (values == null || !values.TryGetValue(name, out var value) || value == null) return double.NaN;
        try { return Convert.ToDouble(value, CultureInfo.InvariantCulture); } catch { return double.NaN; }
    }

    private static int ReadInt(Dictionary<string, object>? values, string name)
    {
        if (values == null || !values.TryGetValue(name, out var value) || value == null) return 0;
        try { return Convert.ToInt32(value, CultureInfo.InvariantCulture); } catch { return 0; }
    }

    private static string? ReadString(Dictionary<string, object>? values, string name) =>
        values != null && values.TryGetValue(name, out var value) ? value?.ToString() : null;

    private static int ReadReflectedInt(object? value, string propertyName)
    {
        if (value == null) return 0;
        try { return Convert.ToInt32(value.GetType().GetProperty(propertyName)?.GetValue(value), CultureInfo.InvariantCulture); } catch { return 0; }
    }

    private static int ReadCollectionCount(object? value) => value switch
    {
        ICollection collection => collection.Count,
        IEnumerable enumerable => enumerable.Cast<object>().Count(),
        _ => 0
    };

    private static bool HasCircleDiagnostics(Dictionary<string, object>? values) => values != null &&
        values.ContainsKey("StatusCode") && values.ContainsKey("CaliperDiagnostics") && values.ContainsKey("CaliperProfileEvidence") && values.ContainsKey("UncertaintyPx");

    private static bool HasLineDiagnostics(Dictionary<string, object>? values, bool robust) => values != null &&
        values.ContainsKey("Method") && values.ContainsKey("LineCount") && values.ContainsKey("ResidualMean") && values.ContainsKey("ResidualMax") &&
        (!robust || (values.ContainsKey("FitLoss") && values.ContainsKey("RefineAlgorithm")));

    private static void ReleaseOutputImage(OperatorExecutionOutput output)
    {
        if (output.OutputData?.TryGetValue("Image", out var value) == true && value is ImageWrapper image) image.Dispose();
    }

    private static string ParseFailureCode(string? error)
    {
        if (string.IsNullOrWhiteSpace(error)) return "Unknown";
        var start = error.IndexOf('[');
        var end = error.IndexOf(']');
        if (start >= 0 && end > start) return error[(start + 1)..end];
        return error.Contains("No valid line", StringComparison.OrdinalIgnoreCase) ? "NoValidLine" : "ExecutionFailure";
    }

    private static double SignedAngleDifference(double actual, double expected)
    {
        if (!double.IsFinite(actual)) return double.NaN;
        var difference = NormalizeAngle(actual) - NormalizeAngle(expected);
        while (difference > 90) difference -= 180;
        while (difference < -90) difference += 180;
        return difference;
    }

    private static double NormalizeAngle(double value)
    {
        value %= 180.0;
        return value < 0 ? value + 180.0 : value;
    }

    private static double Percentile(IReadOnlyList<double> sortedValues, double percentile)
    {
        if (sortedValues.Count == 0) return double.NaN;
        var index = Math.Clamp((int)Math.Ceiling(percentile * sortedValues.Count) - 1, 0, sortedValues.Count - 1);
        return sortedValues[index];
    }

    private static string HashFile(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
    private static void Append(IncrementalHash hash, string value) => hash.AppendData(Encoding.UTF8.GetBytes(value + "\n"));
}

internal static class BenchmarkMarkdown
{
    public static string Create(ProductBenchmarkReport report)
    {
        var lines = new List<string>
        {
            $"# Product Operator E2E Benchmark ({report.Label})",
            string.Empty,
            $"- Product SHA: `{report.ProductImplementation.RepositorySha}` (dirty={report.ProductImplementation.RepositoryDirty})",
            $"- Product Infrastructure assembly SHA: `{report.ProductImplementation.InfrastructureAssemblySha256}`",
            $"- Harness commit/program SHA: `{report.Harness.CommitSha}` / `{report.Harness.ProgramSha256}` (dirty={report.Harness.RepositoryDirty})",
            $"- Dataset manifest/generated SHA: `{report.Dataset.ManifestSha256}` / `{report.Dataset.GeneratedDataSha256}`",
            $"- Claim boundary: {report.Dataset.ClaimBoundary}",
            $"- Managed allocation scope: {report.ResourceMeasurement.ManagedAllocation}",
            $"- Full-process resources: peak working set={report.ProcessResources.PeakWorkingSetBytes} B; private bytes={report.ProcessResources.PrivateBytes} B",
            string.Empty,
            "| Domain | Algorithm | Split | Cases | Bias | RMSE | P95 | Failure | Ambiguity | Outlier | Latency P50/P95 ms | Managed alloc B/case |",
            "|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|"
        };
        foreach (var metric in report.Metrics)
        {
            lines.Add($"| {metric.Domain} | {metric.Algorithm} | {metric.Split} | {metric.CaseCount} | {F(metric.Bias)} | {F(metric.Rmse)} | {F(metric.P95Error)} | {F(metric.FailureRate)} | {F(metric.AmbiguityRate)} | {F(metric.OutlierRate)} | {F(metric.LatencyP50Milliseconds)} / {F(metric.LatencyP95Milliseconds)} | {F(metric.ManagedAllocatedBytesPerCase)} |");
        }
        lines.Add(string.Empty);
        lines.Add("## Reproduction");
        lines.Add(string.Empty);
        lines.Add(report.Label.Equals("baseline", StringComparison.OrdinalIgnoreCase)
            ? "`& \"./scripts/reproduce-operator-precision-baseline.ps1\" -Profile acceptance -ResultsDirectory \".tmp/operator-product-e2e\"`"
            : "`& \"./scripts/run-operator-product-e2e-benchmark.ps1\" -Profile acceptance -Label after -IncludeCandidates -ResultsDirectory \".tmp/operator-product-e2e\"` ");
        return string.Join(Environment.NewLine, lines) + Environment.NewLine;

        static string F(double value) => double.IsFinite(value) ? value.ToString("0.000000", CultureInfo.InvariantCulture) : "NaN";
    }
}

internal sealed record BenchmarkOptions(
    string ManifestPath,
    string OutputPath,
    string ReportPath,
    string Label,
    string ProductRoot,
    string ProductSourceSha,
    bool ProductRepositoryDirty,
    string HarnessCommitSha,
    bool HarnessRepositoryDirty,
    string HarnessSourceDirectory,
    bool AdapterInjectedIntoProductWorktree,
    string SdkVersion,
    int Warmup,
    int Iterations,
    bool IncludeCandidates)
{
    public static BenchmarkOptions Parse(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < args.Length; index += 2)
        {
            if (index + 1 >= args.Length || !args[index].StartsWith("--", StringComparison.Ordinal)) throw new ArgumentException("Arguments must be --name value pairs.");
            values[args[index][2..]] = args[index + 1];
        }
        string Required(string name) => values.TryGetValue(name, out var value) ? value : throw new ArgumentException($"Missing --{name}.");
        return new BenchmarkOptions(
            Required("manifest"), Required("output"), Required("report"), Required("label"), Required("product-root"), Required("product-source-sha"),
            bool.Parse(Required("product-repository-dirty")), Required("harness-commit-sha"), bool.Parse(Required("harness-repository-dirty")),
            Required("harness-source-directory"), bool.Parse(Required("adapter-injected")), Required("sdk-version"), int.Parse(Required("warmup"), CultureInfo.InvariantCulture),
            int.Parse(Required("iterations"), CultureInfo.InvariantCulture), bool.Parse(Required("include-candidates")));
    }
}

internal abstract record ProductCase(string CaseId, string Domain, string Scenario, string Split, byte[] ImagePng)
{
    public abstract string TruthIdentity { get; }
}

internal sealed record CircleProductCase(
    string Id, string CaseScenario, string CaseSplit, byte[] Png, double CenterX, double CenterY, double Radius,
    double SeedCenterX, double SeedCenterY, string EdgePolarity, string OutlierMode)
    : ProductCase(Id, "Circle", CaseScenario, CaseSplit, Png)
{
    public override string TruthIdentity => string.Join("|", CenterX.ToString("R", CultureInfo.InvariantCulture), CenterY.ToString("R", CultureInfo.InvariantCulture), Radius.ToString("R", CultureInfo.InvariantCulture), SeedCenterX.ToString("R", CultureInfo.InvariantCulture), SeedCenterY.ToString("R", CultureInfo.InvariantCulture), EdgePolarity, OutlierMode);
}

internal sealed record LineProductCase(string Id, string CaseScenario, string CaseSplit, byte[] Png, double AngleDegrees)
    : ProductCase(Id, "Line", CaseScenario, CaseSplit, Png)
{
    public override string TruthIdentity => AngleDegrees.ToString("R", CultureInfo.InvariantCulture);
}

internal readonly record struct EvaluationSample(
    double SignedError, double SecondaryError, bool Failed, bool Ambiguous, double OutlierRate, double Residual,
    string FailureCode, bool DiagnosticsComplete)
{
    public static EvaluationSample Failure(string code, bool ambiguous = false, bool diagnosticsComplete = false) =>
        new(double.NaN, double.NaN, true, ambiguous, 0, double.NaN, code, diagnosticsComplete);
}

internal readonly record struct TimingSummary(double P50Milliseconds, double P95Milliseconds, double ManagedAllocatedBytesPerCase);

internal sealed record ProductBenchmarkReport(
    string SchemaVersion,
    string BenchmarkId,
    string Label,
    DateTime GeneratedAtUtc,
    ProductDatasetIdentity Dataset,
    ProductImplementationIdentity ProductImplementation,
    HarnessIdentity Harness,
    BenchmarkEnvironment Environment,
    ResourceMeasurementScope ResourceMeasurement,
    ProcessResourceSnapshot ProcessResources,
    int WarmupIterations,
    int MeasurementIterations,
    IReadOnlyList<ProductBenchmarkMetric> Metrics,
    IReadOnlyList<ModeClaim> ModeClaims);

internal sealed record ProductDatasetIdentity(
    string Id, string Version, string ManifestSha256, string GeneratedDataSha256, int Seed, string License,
    string ClaimBoundary, IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>> SplitCounts);

internal sealed record ProductImplementationIdentity(
    string RepositorySha, bool RepositoryDirty, string FormalEntrypoints, IReadOnlyDictionary<string, string> SourceFilesSha256,
    string InfrastructureAssemblySha256, string CoreAssemblySha256, string AssemblyVersion);

internal sealed record HarnessIdentity(
    string CommitSha, bool RepositoryDirty, string ProgramSha256, string ProjectSha256, string ManifestSha256,
    bool AdapterInjectedIntoProductWorktree, string BuildIsolation);

internal sealed record BenchmarkEnvironment(
    string Framework, string OperatingSystem, string Architecture, int ProcessorCount, string RuntimeVersion,
    string ProcessorIdentifier, string SdkVersion, string OpenCvVersion, bool ServerGc, string GcLatencyMode);

internal sealed record ResourceMeasurementScope(string Latency, string ManagedAllocation, string ProcessResources);
internal sealed record ProcessResourceSnapshot(long PeakWorkingSetBytes, long PrivateBytes, long WorkingSetBytes);
internal sealed record ModeClaim(string OperatorType, string Mode, bool IsDefault, bool IsCandidate, string Claim);

internal sealed record ProductBenchmarkMetric(
    string Domain, string Algorithm, string Split, string Unit, int CaseCount, double Bias, double Rmse, double P95Error,
    double FailureRate, double AmbiguityRate, double OutlierRate, double LatencyP50Milliseconds, double LatencyP95Milliseconds,
    double ManagedAllocatedBytesPerCase, IReadOnlyDictionary<string, double> Extra, IReadOnlyDictionary<string, int> FailureTaxonomy);
