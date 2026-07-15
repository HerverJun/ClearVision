using System.Diagnostics;
using System.Globalization;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ClearVision.Product.Infrastructure.AI.Anomaly;
using ClearVision.Product.Infrastructure.Operators;
using OpenCvSharp;

var options = BenchmarkOptions.Parse(args);
var runner = new OperatorPrecisionBenchmark(options);
var report = runner.Run();

Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(options.OutputPath))!);
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(options.ReportPath))!);

var jsonOptions = new JsonSerializerOptions
{
    WriteIndented = true,
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
};
File.WriteAllText(options.OutputPath, JsonSerializer.Serialize(report, jsonOptions), new UTF8Encoding(false));
File.WriteAllText(options.ReportPath, BenchmarkMarkdown.Create(report), new UTF8Encoding(false));

Console.WriteLine($"Operator precision benchmark complete: {report.Metrics.Count} metric rows; output={options.OutputPath}");

internal sealed class OperatorPrecisionBenchmark
{
    private readonly BenchmarkOptions _options;
    private readonly JsonDocument _manifest;
    private readonly string _repoRoot;
    private readonly int _seed;

    public OperatorPrecisionBenchmark(BenchmarkOptions options)
    {
        _options = options;
        _manifest = JsonDocument.Parse(File.ReadAllBytes(options.ManifestPath));
        _repoRoot = FindRepoRoot(Path.GetDirectoryName(Path.GetFullPath(options.ManifestPath))!);
        _seed = _manifest.RootElement.GetProperty("seed").GetInt32();
    }

    public BenchmarkReport Run()
    {
        var manifestBytes = File.ReadAllBytes(_options.ManifestPath);
        var manifestSha = HashBytes(manifestBytes);
        ValidateManifestHash(manifestSha);
        var anomalyConfig = _manifest.RootElement.GetProperty("anomaly");
        var modelPath = Path.Combine(_repoRoot, NormalizeRepoPath(anomalyConfig.GetProperty("onnxModelPath").GetString()!));
        var embeddingManifestPath = Path.Combine(_repoRoot, NormalizeRepoPath(anomalyConfig.GetProperty("onnxEmbeddingManifestPath").GetString()!));
        var referenceInputPath = Path.Combine(_repoRoot, NormalizeRepoPath(anomalyConfig.GetProperty("onnxReferenceInputPath").GetString()!));
        var referenceOutputPath = Path.Combine(_repoRoot, NormalizeRepoPath(anomalyConfig.GetProperty("onnxReferenceOutputPath").GetString()!));
        var modelSha = HashBytes(File.ReadAllBytes(modelPath));
        ValidateAssetHash(anomalyConfig, "onnxModelSha256", modelPath);
        ValidateAssetHash(anomalyConfig, "onnxEmbeddingManifestSha256", embeddingManifestPath);
        ValidateAssetHash(anomalyConfig, "onnxReferenceInputSha256", referenceInputPath);
        ValidateAssetHash(anomalyConfig, "onnxReferenceOutputSha256", referenceOutputPath);
        var embeddingIdentity = AnomalyEmbeddingManifest.LoadAndValidate(embeddingManifestPath, modelPath);
        var declaredPreprocess = CreateBenchmarkPreprocessSpec(anomalyConfig.GetProperty("preprocess"));
        if (!PreprocessMatches(embeddingIdentity.Preprocess, declaredPreprocess))
        {
            throw new InvalidOperationException("Benchmark preprocessing declaration does not match the versioned embedding manifest.");
        }
        var preprocessFingerprint = embeddingIdentity.PreprocessFingerprint;

        var caliperCases = GenerateCaliperCases();
        var circleCases = GenerateCircleCases();
        var lineCases = GenerateLineCases();
        var anomalyCases = GenerateAnomalyCases();
        var generatedDataSha = HashGeneratedCases(caliperCases, circleCases, lineCases, anomalyCases);
        ValidateGeneratedDataHash(generatedDataSha);
        var metrics = new List<BenchmarkMetric>();

        AddSplitMetrics("Caliper", "LegacyGradientCentroid", "width_px", caliperCases, CaliperAlgorithms.LegacyGradientCentroid);
        AddSplitMetrics("Caliper", "Quadratic", "width_px", caliperCases, CaliperAlgorithms.Quadratic);
        AddSplitMetrics("Caliper", "GaussianDerivative", "width_px", caliperCases, CaliperAlgorithms.GaussianDerivative);
        AddSplitMetrics("Caliper", "Erf", "width_px", caliperCases, CaliperAlgorithms.Erf);

        AddSplitMetrics("Circle", "AlgebraicL2", "radius_px", circleCases, GeometryAlgorithms.CircleAlgebraic);
        AddSplitMetrics("Circle", "OrthogonalHuber", "radius_px", circleCases, item => GeometryAlgorithms.CircleRobust(item, RobustLoss.Huber));
        AddSplitMetrics("Circle", "OrthogonalWelsch", "radius_px", circleCases, item => GeometryAlgorithms.CircleRobust(item, RobustLoss.Welsch));

        AddSplitMetrics("Line", "L2", "angle_deg", lineCases, item => GeometryAlgorithms.LineFit(item, RobustLoss.L2));
        AddSplitMetrics("Line", "Huber", "angle_deg", lineCases, item => GeometryAlgorithms.LineFit(item, RobustLoss.Huber));
        AddSplitMetrics("Line", "Welsch", "angle_deg", lineCases, item => GeometryAlgorithms.LineFit(item, RobustLoss.Welsch));

        if (_options.Label.Equals("after", StringComparison.OrdinalIgnoreCase))
        {
            AddSplitMetrics("Caliper", "IntegratedGaussianDerivative", "width_px", caliperCases, ProductionAlgorithms.CaliperGaussianDerivative);
            AddSplitMetrics("Circle", "ProductionOrthogonalWelsch", "radius_px", circleCases, GeometryAlgorithms.CircleProductionWelsch);
            AddSplitMetrics("Line", "ProductionWelsch", "angle_deg", lineCases, GeometryAlgorithms.LineProductionWelsch);
        }

        metrics.Add(EvaluateUncertainty("MeasurementUncertainty", "ResidualHeuristic", circleCases, lineCases, covariance: false));
        metrics.Add(EvaluateUncertainty("MeasurementUncertainty", "Covariance", circleCases, lineCases, covariance: true));

        metrics.Add(EvaluateAnomaly("Anomaly", "TraditionalLabGradient", anomalyCases, modelPath, embeddingManifestPath, useOnnx: false));
        metrics.Add(EvaluateAnomaly("Anomaly", "OnnxManifestPreprocess", anomalyCases, modelPath, embeddingManifestPath, useOnnx: true));
        metrics.Add(EvaluatePreprocessReference(anomalyConfig, modelPath, embeddingManifestPath, preprocessFingerprint));

        return new BenchmarkReport(
            SchemaVersion: "2026-07-16.operator-precision-report.v2",
            BenchmarkId: "clearvision-operator-precision-v1",
            Label: _options.Label,
            SourceSha: _options.SourceSha,
            GeneratedAtUtc: DateTime.UtcNow,
            Dataset: new DatasetIdentity(
                _manifest.RootElement.GetProperty("datasetId").GetString()!,
                _manifest.RootElement.GetProperty("datasetVersion").GetString()!,
                manifestSha,
                generatedDataSha,
                _seed,
                _manifest.RootElement.GetProperty("license").GetString()!,
                _manifest.RootElement.GetProperty("claimBoundary").GetString()!,
                CountSplits(caliperCases, circleCases, lineCases, anomalyCases)),
            Model: new ModelIdentity(
                RepoRelative(modelPath),
                modelSha,
                anomalyConfig.GetProperty("modelSource").GetString()!,
                anomalyConfig.GetProperty("modelLicense").GetString()!,
                preprocessFingerprint,
                RepoRelative(embeddingManifestPath),
                HashBytes(File.ReadAllBytes(embeddingManifestPath)),
                HashBytes(File.ReadAllBytes(referenceInputPath)),
                HashBytes(File.ReadAllBytes(referenceOutputPath))),
            Harness: BuildHarnessIdentity(),
            Environment: new BenchmarkEnvironment(
                RuntimeInformation.FrameworkDescription,
                RuntimeInformation.OSDescription,
                RuntimeInformation.ProcessArchitecture.ToString(),
                Environment.ProcessorCount,
                Environment.Version.ToString(),
                Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER") ?? "unknown",
                _options.SdkVersion,
                Cv2.GetVersionString() ?? "unknown",
                GCSettings.IsServerGC,
                GCSettings.LatencyMode.ToString()),
            WarmupIterations: _options.Warmup,
            MeasurementIterations: _options.Iterations,
            Metrics: metrics,
            Decisions: BuildDecisions(metrics));

        void AddSplitMetrics<T>(string domain, string algorithm, string unit, IReadOnlyList<T> cases, Func<T, EvaluationSample> evaluate)
            where T : IBenchmarkCase
        {
            metrics.Add(Evaluate(domain, algorithm, unit, cases, evaluate, "validation"));
            metrics.Add(Evaluate(domain, algorithm, unit, cases, evaluate, "test"));
        }
    }

    private BenchmarkMetric Evaluate<T>(
        string domain,
        string algorithm,
        string unit,
        IReadOnlyList<T> cases,
        Func<T, EvaluationSample> evaluate,
        string split)
        where T : IBenchmarkCase
    {
        var selectedCases = cases.Where(item => item.Split == split).ToArray();
        var accuracy = selectedCases.Select(SafeEvaluate).ToArray();
        var timings = Measure(selectedCases, evaluate);
        var valid = accuracy.Where(item => !item.Failed && double.IsFinite(item.SignedError)).ToArray();
        var errors = valid.Select(item => Math.Abs(item.SignedError)).OrderBy(value => value).ToArray();

        return new BenchmarkMetric(
            domain,
            algorithm,
            split,
            unit,
            selectedCases.Length,
            valid.Length == 0 ? double.NaN : valid.Average(item => item.SignedError),
            valid.Length == 0 ? double.NaN : Math.Sqrt(valid.Average(item => item.SignedError * item.SignedError)),
            Percentile(errors, 0.95),
            accuracy.Count(item => item.Failed) / (double)Math.Max(selectedCases.Length, 1),
            accuracy.Count(item => item.Ambiguous) / (double)Math.Max(selectedCases.Length, 1),
            accuracy.Average(item => item.OutlierRate),
            timings.P50Milliseconds,
            timings.P95Milliseconds,
            timings.AllocatedBytesPerCase,
            new Dictionary<string, double>
            {
                ["coverage68"] = valid.Length == 0 ? double.NaN : valid.Count(item => Math.Abs(item.SignedError) <= item.Sigma) / (double)valid.Length,
                ["coverage95"] = valid.Length == 0 ? double.NaN : valid.Count(item => Math.Abs(item.SignedError) <= 1.96 * item.Sigma) / (double)valid.Length
            });

        EvaluationSample SafeEvaluate(T item)
        {
            try
            {
                return evaluate(item);
            }
            catch
            {
                return EvaluationSample.Failure;
            }
        }
    }

    private BenchmarkMetric EvaluateUncertainty(
        string domain,
        string algorithm,
        IReadOnlyList<CircleCase> circleCases,
        IReadOnlyList<LineCase> lineCases,
        bool covariance)
    {
        var selectedCircles = circleCases.Where(item => item.Split == "test").ToArray();
        var selectedLines = lineCases.Where(item => item.Split == "test").ToArray();
        var samples = new List<EvaluationSample>(selectedCircles.Length + selectedLines.Length);

        foreach (var item in selectedCircles)
        {
            var result = GeometryAlgorithms.CircleRobust(item, RobustLoss.Huber);
            var sigma = covariance ? result.Sigma : Math.Max(result.ResidualScale, 1e-6);
            samples.Add(result with { Sigma = sigma });
        }

        foreach (var item in selectedLines)
        {
            var result = GeometryAlgorithms.LineFit(item, RobustLoss.Huber);
            var sigma = covariance ? result.Sigma : Math.Max(result.ResidualScale, 1e-6);
            samples.Add(result with { Sigma = sigma });
        }

        var valid = samples.Where(item => !item.Failed && double.IsFinite(item.SignedError) && item.Sigma > 0).ToArray();
        var coverage68 = valid.Count(item => Math.Abs(item.SignedError) <= item.Sigma) / (double)Math.Max(valid.Length, 1);
        var coverage95 = valid.Count(item => Math.Abs(item.SignedError) <= 1.96 * item.Sigma) / (double)Math.Max(valid.Length, 1);
        var uncertaintyConfig = _manifest.RootElement.GetProperty("uncertainty");
        var nominalCoverage68 = uncertaintyConfig.GetProperty("nominalCoverage68").GetDouble();
        var nominalCoverage95 = uncertaintyConfig.GetProperty("nominalCoverage95").GetDouble();
        var expectedCaseCount = uncertaintyConfig.GetProperty("caseCount").GetInt32();
        if (samples.Count != expectedCaseCount)
        {
            throw new InvalidOperationException($"Uncertainty case count mismatch. Manifest={expectedCaseCount}, actual={samples.Count}.");
        }
        var errors = valid.Select(item => Math.Abs(item.SignedError)).OrderBy(value => value).ToArray();
        var timing = Measure(samples, static item => item);

        return new BenchmarkMetric(
            domain,
            algorithm,
            "test",
            "mixed_native_unit",
            samples.Count,
            valid.Length == 0 ? double.NaN : valid.Average(item => item.SignedError),
            valid.Length == 0 ? double.NaN : Math.Sqrt(valid.Average(item => item.SignedError * item.SignedError)),
            Percentile(errors, 0.95),
            samples.Count(item => item.Failed) / (double)Math.Max(samples.Count, 1),
            samples.Count(item => item.Ambiguous) / (double)Math.Max(samples.Count, 1),
            samples.Average(item => item.OutlierRate),
            timing.P50Milliseconds,
            timing.P95Milliseconds,
            timing.AllocatedBytesPerCase,
            new Dictionary<string, double>
            {
                ["coverage68"] = coverage68,
                ["coverage95"] = coverage95,
                ["coverage68AbsoluteError"] = Math.Abs(coverage68 - nominalCoverage68),
                ["coverage95AbsoluteError"] = Math.Abs(coverage95 - nominalCoverage95)
            });
    }

    private BenchmarkMetric EvaluateAnomaly(
        string domain,
        string algorithm,
        IReadOnlyList<AnomalyCase> cases,
        string modelPath,
        string embeddingManifestPath,
        bool useOnnx)
    {
        var training = cases.Where(item => item.Split == "train" && !item.IsAnomaly).ToArray();
        var validation = cases.Where(item => item.Split == "validation").ToArray();
        var test = cases.Where(item => item.Split == "test").ToArray();
        var featureOptions = new SimplePatchCoreOptions
        {
            PatchSize = 2,
            PatchStride = 2,
            CoresetRatio = 1.0,
            FeatureExtractorId = "onnx_embedding",
            EmbeddingModelId = "identity_2x2",
            EmbeddingModelPath = modelPath,
            EmbeddingManifestPath = embeddingManifestPath,
            EmbeddingModelSha256 = HashBytes(File.ReadAllBytes(modelPath)),
            PreprocessFingerprint = AnomalyEmbeddingManifest.LoadAndValidate(embeddingManifestPath, modelPath).PreprocessFingerprint,
            EmbeddingPreprocess = AnomalyEmbeddingManifest.LoadAndValidate(embeddingManifestPath, modelPath).Preprocess
        };

        var trainingFeatures = training.Select(item => Extract(item.Image, useOnnx, featureOptions)).ToArray();
        var validationRows = validation.Select(item => (Case: item, Distance: NearestDistance(Extract(item.Image, useOnnx, featureOptions), trainingFeatures))).ToArray();
        var normal95 = Percentile(validationRows.Where(item => !item.Case.IsAnomaly).Select(item => item.Distance).OrderBy(value => value).ToArray(), 0.95);
        var anomaly05 = Percentile(validationRows.Where(item => item.Case.IsAnomaly).Select(item => item.Distance).OrderBy(value => value).ToArray(), 0.05);
        var threshold = double.IsFinite(normal95) && double.IsFinite(anomaly05)
            ? (normal95 + anomaly05) / 2.0
            : normal95;

        var outcomes = test.Select(item =>
        {
            var distance = NearestDistance(Extract(item.Image, useOnnx, featureOptions), trainingFeatures);
            var predicted = distance > threshold;
            var classificationError = predicted == item.IsAnomaly ? 0.0 : 1.0;
            var ambiguity = Math.Abs(distance - threshold) <= Math.Max(1e-9, threshold * 0.05);
            return new EvaluationSample(classificationError, false, ambiguity, classificationError, 1.0, Math.Abs(distance - threshold));
        }).ToArray();
        var timing = Measure(test, item =>
        {
            _ = Extract(item.Image, useOnnx, featureOptions);
            return EvaluationSample.Success;
        });

        return new BenchmarkMetric(
            domain,
            algorithm,
            "test",
            "classification_error_0_or_1",
            test.Length,
            outcomes.Average(item => item.SignedError),
            Math.Sqrt(outcomes.Average(item => item.SignedError * item.SignedError)),
            Percentile(outcomes.Select(item => Math.Abs(item.SignedError)).OrderBy(value => value).ToArray(), 0.95),
            0,
            outcomes.Count(item => item.Ambiguous) / (double)Math.Max(outcomes.Length, 1),
            outcomes.Average(item => item.OutlierRate),
            timing.P50Milliseconds,
            timing.P95Milliseconds,
            timing.AllocatedBytesPerCase,
            new Dictionary<string, double>
            {
                ["accuracy"] = 1.0 - outcomes.Average(item => item.SignedError),
                ["threshold"] = threshold,
                ["validationNormalP95Distance"] = normal95,
                ["validationAnomalyP05Distance"] = anomaly05
            });
    }

    private BenchmarkMetric EvaluatePreprocessReference(JsonElement anomalyConfig, string modelPath, string embeddingManifestPath, string fingerprint)
    {
        var inputPath = Path.Combine(_repoRoot, NormalizeRepoPath(anomalyConfig.GetProperty("onnxReferenceInputPath").GetString()!));
        var expectedPath = Path.Combine(_repoRoot, NormalizeRepoPath(anomalyConfig.GetProperty("onnxReferenceOutputPath").GetString()!));
        using var image = Cv2.ImRead(inputPath, ImreadModes.Color);
        var options = new SimplePatchCoreOptions
        {
            PatchSize = 2,
            PatchStride = 2,
            CoresetRatio = 1.0,
            FeatureExtractorId = "onnx_embedding",
            EmbeddingModelId = "identity_2x2",
            EmbeddingModelPath = modelPath,
            EmbeddingManifestPath = embeddingManifestPath,
            EmbeddingModelSha256 = HashBytes(File.ReadAllBytes(modelPath)),
            PreprocessFingerprint = fingerprint,
            EmbeddingPreprocess = AnomalyEmbeddingManifest.LoadAndValidate(embeddingManifestPath, modelPath).Preprocess
        };
        var actual = OnnxPatchEmbeddingExtractor.ExtractEmbedding(image, options);
        using var expectedDocument = JsonDocument.Parse(File.ReadAllBytes(expectedPath));
        var expected = expectedDocument.RootElement.GetProperty("values").EnumerateArray().Select(item => item.GetSingle()).ToArray();
        Normalize(expected);
        var rmse = Math.Sqrt(actual.Zip(expected).Average(pair => Math.Pow(pair.First - pair.Second, 2)));
        var mismatchFingerprint = HashBytes(Encoding.UTF8.GetBytes(fingerprint + ":mismatch"));
        var bank = SimplePatchCoreDetector.BuildFeatureBank(new[] { image }, options);
        var mismatchRejected = false;
        var mismatchOptions = new SimplePatchCoreOptions
        {
            PatchSize = options.PatchSize,
            PatchStride = options.PatchStride,
            CoresetRatio = options.CoresetRatio,
            Backbone = options.Backbone,
            FeatureExtractorId = options.FeatureExtractorId,
            EmbeddingModelId = options.EmbeddingModelId,
            EmbeddingModelPath = options.EmbeddingModelPath,
            EmbeddingManifestPath = options.EmbeddingManifestPath,
            EmbeddingModelSha256 = options.EmbeddingModelSha256,
            PreprocessFingerprint = mismatchFingerprint,
            EmbeddingPreprocess = options.EmbeddingPreprocess
        };
        try
        {
            var unexpected = SimplePatchCoreDetector.Analyze(image, bank, 0.5, mismatchOptions);
            unexpected.Heatmap.Dispose();
            unexpected.ScoreMap.Dispose();
            unexpected.Mask.Dispose();
        }
        catch (InvalidOperationException)
        {
            mismatchRejected = true;
        }

        if (!mismatchRejected)
        {
            throw new InvalidOperationException("ONNX feature-bank preprocessing mismatch was not rejected fail-closed.");
        }
        var timing = Measure(Enumerable.Range(0, 20).ToArray(), _iteration =>
        {
            OnnxPatchEmbeddingExtractor.ExtractEmbedding(image, options);
            return EvaluationSample.Success;
        });

        return new BenchmarkMetric(
            "AnomalyPreprocess",
            "ManifestDeclaredRgbFloat01",
            "contract",
            "tensor_rmse",
            1,
            rmse,
            rmse,
            rmse,
            0,
            0,
            0,
            timing.P50Milliseconds,
            timing.P95Milliseconds,
            timing.AllocatedBytesPerCase,
            new Dictionary<string, double>
            {
                ["referenceMaxAbsoluteError"] = actual.Zip(expected).Max(pair => Math.Abs(pair.First - pair.Second)),
                ["fingerprintLength"] = fingerprint.Length,
                ["mismatchRejectedFailClosed"] = mismatchRejected ? 1 : 0
            });
    }

    private TimingSummary Measure<T>(IReadOnlyList<T> cases, Func<T, EvaluationSample> evaluate)
    {
        for (var warmup = 0; warmup < _options.Warmup; warmup++)
        {
            foreach (var item in cases)
            {
                _ = evaluate(item);
            }
        }

        var elapsed = new double[_options.Iterations];
        long totalAllocated = 0;
        for (var iteration = 0; iteration < _options.Iterations; iteration++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            var stopwatch = Stopwatch.StartNew();
            foreach (var item in cases)
            {
                _ = evaluate(item);
            }

            stopwatch.Stop();
            totalAllocated += GC.GetAllocatedBytesForCurrentThread() - before;
            elapsed[iteration] = stopwatch.Elapsed.TotalMilliseconds / Math.Max(cases.Count, 1);
        }

        Array.Sort(elapsed);
        return new TimingSummary(
            Percentile(elapsed, 0.50),
            Percentile(elapsed, 0.95),
            totalAllocated / Math.Max((long)_options.Iterations * Math.Max(cases.Count, 1), 1));
    }

    private IReadOnlyList<CaliperCase> GenerateCaliperCases()
    {
        var config = _manifest.RootElement.GetProperty("caliper");
        var count = config.GetProperty("caseCount").GetInt32();
        var sampleCount = config.GetProperty("sampleCount").GetInt32();
        var scenarios = config.GetProperty("scenarios").EnumerateArray().Select(item => item.GetString()!).ToArray();
        var edgeStartRange = ReadRange(config, "edgeStartRange");
        var widthRange = ReadRange(config, "widthRange");
        var blurRange = ReadRange(config, "blurSigmaRange");
        var cleanNoiseRange = ReadRange(config, "cleanNoiseSigmaRange");
        var scenarioNoiseRange = ReadRange(config, "scenarioNoiseSigmaRange");
        var textureRange = ReadRange(config, "textureAmplitudeRange");
        var cleanBlurSigma = config.GetProperty("cleanBlurSigma").GetDouble();
        var random = new Random(_seed + 11);
        var result = new List<CaliperCase>(count);
        for (var index = 0; index < count; index++)
        {
            var scenario = scenarios[index % scenarios.Length];
            var start = Lerp(edgeStartRange.Min, edgeStartRange.Max, random.NextDouble());
            var width = Lerp(widthRange.Min, widthRange.Max, random.NextDouble());
            var blur = scenario == "clean" ? cleanBlurSigma : Lerp(blurRange.Min, blurRange.Max, random.NextDouble());
            var noiseRange = scenario is "noise" or "texture" or "occlusion" ? scenarioNoiseRange : cleanNoiseRange;
            var noise = Lerp(noiseRange.Min, noiseRange.Max, random.NextDouble());
            var texture = scenario is "texture" or "double_edge" ? Lerp(textureRange.Min, textureRange.Max, random.NextDouble()) : 0.0;
            var inverted = scenario == "polarity" || index % 7 == 0;
            var profile = SyntheticData.CreateCaliperProfile(sampleCount, start, start + width, blur, noise, texture, inverted, scenario, random);
            result.Add(new CaliperCase(index, Split(index / scenarios.Length), scenario, profile, start, start + width, width, inverted));
        }

        return result;
    }

    private IReadOnlyList<CircleCase> GenerateCircleCases()
    {
        var config = _manifest.RootElement.GetProperty("circle");
        var count = config.GetProperty("caseCount").GetInt32();
        var pointCount = config.GetProperty("pointCount").GetInt32();
        var coverages = config.GetProperty("arcCoverageDegrees").EnumerateArray().Select(item => item.GetDouble()).ToArray();
        var scenarios = config.GetProperty("scenarios").EnumerateArray().Select(item => item.GetString()!).ToArray();
        var radiusRange = ReadRange(config, "radiusRange");
        var defaultRadiusRange = ReadRange(config, "defaultRadiusRange");
        var centerRange = ReadRange(config, "centerRange");
        var noiseRange = ReadRange(config, "noiseSigmaRange");
        var outlierRange = ReadRange(config, "outlierFractionRange");
        var cleanNoiseSigma = config.GetProperty("cleanNoiseSigma").GetDouble();
        var random = new Random(_seed + 23);
        var result = new List<CircleCase>(count);
        for (var index = 0; index < count; index++)
        {
            var scenario = scenarios[index % scenarios.Length];
            var selectedRadiusRange = scenario == "scale" ? radiusRange : defaultRadiusRange;
            var radius = Lerp(selectedRadiusRange.Min, selectedRadiusRange.Max, random.NextDouble());
            var centerX = Lerp(centerRange.Min, centerRange.Max, random.NextDouble());
            var centerY = Lerp(centerRange.Min, centerRange.Max, random.NextDouble());
            var coverage = scenario == "short_arc" ? coverages[index % 3] : coverages[3 + index % 3];
            var noise = scenario == "clean" ? cleanNoiseSigma : Lerp(noiseRange.Min, noiseRange.Max, random.NextDouble());
            var outlierFraction = scenario is "outliers" or "ellipse_interference" ? Lerp(outlierRange.Min, outlierRange.Max, random.NextDouble()) : 0.0;
            var points = SyntheticData.CreateCirclePoints(pointCount, centerX, centerY, radius, coverage, noise, outlierFraction, scenario, random);
            result.Add(new CircleCase(index, Split(index / scenarios.Length), scenario, points, centerX, centerY, radius, coverage));
        }

        return result;
    }

    private IReadOnlyList<LineCase> GenerateLineCases()
    {
        var config = _manifest.RootElement.GetProperty("line");
        var count = config.GetProperty("caseCount").GetInt32();
        var pointCount = config.GetProperty("pointCount").GetInt32();
        var scenarios = config.GetProperty("scenarios").EnumerateArray().Select(item => item.GetString()!).ToArray();
        var angleRange = ReadRange(config, "angleDegreesRange");
        var offsetRange = ReadRange(config, "offsetRange");
        var noiseRange = ReadRange(config, "noiseSigmaRange");
        var outlierRange = ReadRange(config, "outlierFractionRange");
        var cleanNoiseSigma = config.GetProperty("cleanNoiseSigma").GetDouble();
        var random = new Random(_seed + 37);
        var result = new List<LineCase>(count);
        for (var index = 0; index < count; index++)
        {
            var scenario = scenarios[index % scenarios.Length];
            var angle = Lerp(angleRange.Min, angleRange.Max, random.NextDouble());
            var offset = Lerp(offsetRange.Min, offsetRange.Max, random.NextDouble());
            var noise = scenario == "clean" ? cleanNoiseSigma : Lerp(noiseRange.Min, noiseRange.Max, random.NextDouble());
            var outlierFraction = scenario is "outliers" or "spur" ? Lerp(outlierRange.Min, outlierRange.Max, random.NextDouble()) : 0.0;
            var points = SyntheticData.CreateLinePoints(pointCount, angle, offset, noise, outlierFraction, scenario, random);
            result.Add(new LineCase(index, Split(index / scenarios.Length), scenario, points, angle, offset));
        }

        return result;
    }

    private IReadOnlyList<AnomalyCase> GenerateAnomalyCases()
    {
        var count = _manifest.RootElement.GetProperty("anomaly").GetProperty("caseCount").GetInt32();
        var random = new Random(_seed + 51);
        var result = new List<AnomalyCase>(count);
        for (var index = 0; index < count; index++)
        {
            var isAnomaly = index % 2 == 1;
            var image = SyntheticData.CreateAnomalyImage(isAnomaly, random);
            result.Add(new AnomalyCase(index, Split(index / 2), isAnomaly, image));
        }

        return result;
    }

    private static IReadOnlyList<BenchmarkDecision> BuildDecisions(IReadOnlyList<BenchmarkMetric> metrics)
    {
        var decisions = new List<BenchmarkDecision>();
        AddWinner("Caliper", "LegacyGradientCentroid", ["Quadratic", "GaussianDerivative", "Erf"]);
        AddWinner("Circle", "AlgebraicL2", ["OrthogonalHuber", "OrthogonalWelsch"]);
        AddWinner("Line", "L2", ["Huber", "Welsch"]);

        var heuristic = Metric("MeasurementUncertainty", "ResidualHeuristic", "test");
        var covariance = Metric("MeasurementUncertainty", "Covariance", "test");
        var heuristicCalibration = heuristic.Extra["coverage68AbsoluteError"] + heuristic.Extra["coverage95AbsoluteError"];
        var covarianceCalibration = covariance.Extra["coverage68AbsoluteError"] + covariance.Extra["coverage95AbsoluteError"];
        decisions.Add(new BenchmarkDecision(
            "MeasurementUncertainty",
            "ResidualHeuristic",
            covarianceCalibration < heuristicCalibration ? "Covariance" : "ResidualHeuristic",
            heuristicCalibration,
            Math.Min(heuristicCalibration, covarianceCalibration),
            covarianceCalibration < heuristicCalibration,
            covarianceCalibration < heuristicCalibration ? "Coverage calibration error improved." : "Covariance did not improve 68%/95% coverage calibration."));

        decisions.Add(new BenchmarkDecision(
            "Anomaly",
            "TraditionalLabGradient",
            "TraditionalLabGradient",
            Metric("Anomaly", "TraditionalLabGradient", "test").Rmse,
            Metric("Anomaly", "TraditionalLabGradient", "test").Rmse,
            false,
            "Traditional mode remains the compatibility default; manifest binding is a fail-closed identity upgrade, not a default feature switch."));
        return decisions;

        void AddWinner(string domain, string baselineName, IReadOnlyList<string> candidates)
        {
            var baseline = Metric(domain, baselineName, "validation");
            var candidate = candidates.Select(name => Metric(domain, name, "validation"))
                .OrderBy(item => Score(item))
                .First();
            var adopted = Score(candidate) < Score(baseline) * 0.98 &&
                          candidate.FailureRate <= baseline.FailureRate + 0.005 &&
                          candidate.AmbiguityRate <= baseline.AmbiguityRate + 0.02;
            decisions.Add(new BenchmarkDecision(
                domain,
                baselineName,
                adopted ? candidate.Algorithm : baselineName,
                Score(baseline),
                adopted ? Score(candidate) : Score(baseline),
                adopted,
                adopted ? "Candidate reduced combined RMSE/P95 without material failure-rate regression." : "No candidate met the repeatable improvement and failure-rate guard."));
        }

        static double Score(BenchmarkMetric metric) => metric.Rmse + metric.P95Error + (metric.FailureRate * 100.0) + (metric.AmbiguityRate * 10.0);
        BenchmarkMetric Metric(string domain, string algorithm, string split) =>
            metrics.Single(item => item.Domain == domain && item.Algorithm == algorithm && item.Split == split);
    }

    private static float[] Extract(Mat image, bool useOnnx, SimplePatchCoreOptions options)
    {
        if (useOnnx)
        {
            return OnnxPatchEmbeddingExtractor.ExtractEmbedding(image, options);
        }

        using var gray = new Mat();
        Cv2.CvtColor(image, gray, ColorConversionCodes.BGR2GRAY);
        Cv2.MeanStdDev(image, out var mean, out var stddev);
        using var gx = new Mat();
        using var gy = new Mat();
        Cv2.Sobel(gray, gx, MatType.CV_32F, 1, 0, 3);
        Cv2.Sobel(gray, gy, MatType.CV_32F, 0, 1, 3);
        using var magnitude = new Mat();
        Cv2.Magnitude(gx, gy, magnitude);
        var feature = new[]
        {
            (float)(mean.Val0 / 255.0),
            (float)(mean.Val1 / 255.0),
            (float)(mean.Val2 / 255.0),
            (float)(stddev.Val0 / 255.0),
            (float)(stddev.Val1 / 255.0),
            (float)(stddev.Val2 / 255.0),
            (float)(Cv2.Mean(magnitude).Val0 / 255.0)
        };
        Normalize(feature);
        return feature;
    }

    private static double NearestDistance(IReadOnlyList<float> feature, IReadOnlyList<float[]> bank)
    {
        return bank.Min(candidate => Math.Sqrt(feature.Zip(candidate).Sum(pair => Math.Pow(pair.First - pair.Second, 2))));
    }

    private static string Split(int index) => (index % 5) switch { 0 => "train", 1 => "validation", _ => "test" };
    private static double Lerp(double min, double max, double t) => min + ((max - min) * t);
    private static string NormalizeRepoPath(string path) => path.Replace('/', Path.DirectorySeparatorChar);
    private static string HashBytes(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static string HashCanonicalJson(JsonElement element)
    {
        var canonical = JsonSerializer.Serialize(element, new JsonSerializerOptions { WriteIndented = false });
        return HashBytes(Encoding.UTF8.GetBytes(canonical));
    }

    private static NumericRange ReadRange(JsonElement parent, string propertyName)
    {
        var values = parent.GetProperty(propertyName).EnumerateArray().Select(item => item.GetDouble()).ToArray();
        if (values.Length != 2 || !values.All(double.IsFinite) || values[1] < values[0])
        {
            throw new InvalidOperationException($"Manifest range '{propertyName}' must contain finite [min,max] values.");
        }

        return new NumericRange(values[0], values[1]);
    }

    private static void ValidateAssetHash(JsonElement config, string propertyName, string path)
    {
        var expected = config.GetProperty(propertyName).GetString()?.Trim().ToLowerInvariant() ?? string.Empty;
        var actual = HashBytes(File.ReadAllBytes(path));
        if (expected.Length != 64 || !string.Equals(expected, actual, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Benchmark asset SHA mismatch for {propertyName}. Expected={expected}, actual={actual}, path={path}.");
        }
    }

    private void ValidateGeneratedDataHash(string actual)
    {
        var expected = _manifest.RootElement.GetProperty("expectedGeneratedDataSha256").GetString()?.Trim().ToLowerInvariant() ?? string.Empty;
        if (expected.Length != 64 || !string.Equals(expected, actual, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Generated benchmark input/truth SHA mismatch. Expected={expected}, actual={actual}.");
        }
    }

    private string HashGeneratedCases(
        IReadOnlyList<CaliperCase> caliperCases,
        IReadOnlyList<CircleCase> circleCases,
        IReadOnlyList<LineCase> lineCases,
        IReadOnlyList<AnomalyCase> anomalyCases)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendString("clearvision-operator-precision-generated-data-v2");
        foreach (var item in caliperCases)
        {
            AppendString("caliper"); AppendInt(item.Index); AppendString(item.Split); AppendString(item.Scenario); AppendDouble(item.FirstEdge); AppendDouble(item.SecondEdge); AppendDouble(item.TrueWidth); AppendBool(item.Inverted);
            foreach (var value in item.Profile) AppendDouble(value);
        }
        foreach (var item in circleCases)
        {
            AppendString("circle"); AppendInt(item.Index); AppendString(item.Split); AppendString(item.Scenario); AppendDouble(item.CenterX); AppendDouble(item.CenterY); AppendDouble(item.Radius); AppendDouble(item.CoverageDegrees);
            foreach (var point in item.Points) { AppendDouble(point.X); AppendDouble(point.Y); AppendBool(point.IsOutlier); }
        }
        foreach (var item in lineCases)
        {
            AppendString("line"); AppendInt(item.Index); AppendString(item.Split); AppendString(item.Scenario); AppendDouble(item.AngleDegrees); AppendDouble(item.Offset);
            foreach (var point in item.Points) { AppendDouble(point.X); AppendDouble(point.Y); AppendBool(point.IsOutlier); }
        }
        foreach (var item in anomalyCases)
        {
            AppendString("anomaly"); AppendInt(item.Index); AppendString(item.Split); AppendBool(item.IsAnomaly); AppendInt(item.Image.Rows); AppendInt(item.Image.Cols); AppendInt(item.Image.Type().Value);
            var length = checked((int)(item.Image.Step() * item.Image.Rows));
            var bytes = new byte[length];
            Marshal.Copy(item.Image.Data, bytes, 0, length);
            hash.AppendData(bytes);
        }

        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();

        void AppendString(string value)
        {
            var bytes = Encoding.UTF8.GetBytes(value);
            AppendInt(bytes.Length);
            hash.AppendData(bytes);
        }
        void AppendInt(int value) => hash.AppendData(BitConverter.GetBytes(value));
        void AppendBool(bool value) => hash.AppendData(new[] { value ? (byte)1 : (byte)0 });
        void AppendDouble(double value) => hash.AppendData(BitConverter.GetBytes(BitConverter.DoubleToInt64Bits(value)));
    }

    private static IReadOnlyDictionary<string, int> CountSplits(
        IReadOnlyList<CaliperCase> caliperCases,
        IReadOnlyList<CircleCase> circleCases,
        IReadOnlyList<LineCase> lineCases,
        IReadOnlyList<AnomalyCase> anomalyCases)
    {
        var result = new SortedDictionary<string, int>(StringComparer.Ordinal);
        Add("caliper", caliperCases);
        Add("circle", circleCases);
        Add("line", lineCases);
        Add("anomaly", anomalyCases);
        return result;

        void Add<T>(string domain, IReadOnlyList<T> cases) where T : IBenchmarkCase
        {
            foreach (var group in cases.GroupBy(item => item.Split, StringComparer.Ordinal))
            {
                result[$"{domain}.{group.Key}"] = group.Count();
            }
        }
    }

    private HarnessIdentity BuildHarnessIdentity()
    {
        var programPath = Path.Combine(_repoRoot, "quality", "tools", "OperatorPrecisionBenchmarkRunner", "Program.cs");
        var projectPath = Path.Combine(_repoRoot, "quality", "tools", "OperatorPrecisionBenchmarkRunner", "OperatorPrecisionBenchmarkRunner.csproj");
        var scriptPath = Path.Combine(_repoRoot, "scripts", "run-operator-precision-benchmark.ps1");
        return new HarnessIdentity(
            _options.HarnessCommitSha,
            HashBytes(File.ReadAllBytes(programPath)),
            HashBytes(File.ReadAllBytes(projectPath)),
            HashBytes(File.ReadAllBytes(scriptPath)),
            _options.RepositoryDirty);
    }

    private static AnomalyEmbeddingPreprocessSpec CreateBenchmarkPreprocessSpec(JsonElement element)
    {
        return new AnomalyEmbeddingPreprocessSpec(
            element.GetProperty("resizeMode").GetString()!,
            element.GetProperty("interpolation").GetString()!,
            element.GetProperty("colorOrder").GetString()!,
            element.GetProperty("scale").GetDouble(),
            element.GetProperty("mean").EnumerateArray().Select(item => item.GetDouble()).ToArray(),
            element.GetProperty("std").EnumerateArray().Select(item => item.GetDouble()).ToArray(),
            element.GetProperty("tensorLayout").GetString()!,
            element.GetProperty("inputDataType").GetString()!,
            element.GetProperty("outputNormalization").GetString()!);
    }

    private static bool PreprocessMatches(AnomalyEmbeddingPreprocessSpec expected, AnomalyEmbeddingPreprocessSpec actual)
    {
        return expected.ResizeMode.Equals(actual.ResizeMode, StringComparison.OrdinalIgnoreCase) &&
               expected.Interpolation.Equals(actual.Interpolation, StringComparison.OrdinalIgnoreCase) &&
               expected.ColorOrder.Equals(actual.ColorOrder, StringComparison.OrdinalIgnoreCase) &&
               expected.Scale.Equals(actual.Scale) &&
               expected.Mean.SequenceEqual(actual.Mean) &&
               expected.Std.SequenceEqual(actual.Std) &&
               expected.TensorLayout.Equals(actual.TensorLayout, StringComparison.OrdinalIgnoreCase) &&
               expected.InputDataType.Equals(actual.InputDataType, StringComparison.OrdinalIgnoreCase) &&
               expected.OutputNormalization.Equals(actual.OutputNormalization, StringComparison.OrdinalIgnoreCase);
    }

    private void ValidateManifestHash(string actualSha)
    {
        var hashPath = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(_options.ManifestPath))!, "manifest.sha256");
        if (!File.Exists(hashPath))
        {
            throw new FileNotFoundException("Benchmark manifest hash file is required.", hashPath);
        }

        var expectedSha = File.ReadAllText(hashPath, Encoding.UTF8)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)[0]
            .Trim()
            .ToLowerInvariant();
        if (!string.Equals(actualSha, expectedSha, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Benchmark manifest SHA mismatch. Expected {expectedSha}, actual {actualSha}.");
        }
    }

    private static string FindRepoRoot(string start)
    {
        var directory = new DirectoryInfo(start);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AGENTS.md")) && Directory.Exists(Path.Combine(directory.FullName, "ClearVision.Product")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Unable to locate repository root.");
    }

    private string RepoRelative(string path) => Path.GetRelativePath(_repoRoot, path).Replace('\\', '/');

    private static double Percentile(IReadOnlyList<double> sorted, double percentile)
    {
        if (sorted.Count == 0)
        {
            return double.NaN;
        }

        var position = Math.Clamp(percentile, 0, 1) * (sorted.Count - 1);
        var lower = (int)Math.Floor(position);
        var upper = (int)Math.Ceiling(position);
        if (lower == upper)
        {
            return sorted[lower];
        }

        return sorted[lower] + ((sorted[upper] - sorted[lower]) * (position - lower));
    }

    private static void Normalize(float[] values)
    {
        var norm = Math.Sqrt(values.Sum(value => value * value));
        if (norm <= 1e-12)
        {
            return;
        }

        for (var index = 0; index < values.Length; index++)
        {
            values[index] = (float)(values[index] / norm);
        }
    }
}

internal static class ProductionAlgorithms
{
    public static EvaluationSample CaliperGaussianDerivative(CaliperCase item)
    {
        var threshold = Math.Max(18.0, IndustrialCaliperKernel.EstimateEdgeThreshold(item.Profile, minimumThreshold: 3.0));
        var detected = IndustrialCaliperKernel.DetectEdges(item.Profile, threshold, "Both");
        var refined = new List<(double Position, IndustrialCaliperPolarity Polarity, CaliperEdgeLocalizationResult Fit)>();
        foreach (var edge in detected)
        {
            var fit = CaliperEdgeModelKernel.FitGaussianDerivative(item.Profile, edge.Position, edge.Polarity.ToString(), 1.0);
            if (!fit.Success)
            {
                return EvaluationSample.Failure;
            }

            refined.Add((fit.Position, edge.Polarity, fit));
        }

        refined.Sort(static (left, right) => left.Position.CompareTo(right.Position));
        var pairIndex = -1;
        for (var index = 0; index < refined.Count - 1; index++)
        {
            if (refined[index].Polarity != refined[index + 1].Polarity)
            {
                pairIndex = index;
                break;
            }
        }

        if (pairIndex < 0)
        {
            return EvaluationSample.Failure;
        }

        var first = refined[pairIndex].Fit;
        var second = refined[pairIndex + 1].Fit;
        if (second.Position <= first.Position)
        {
            return EvaluationSample.Failure;
        }

        var error = (second.Position - first.Position) - item.TrueWidth;
        return new EvaluationSample(
            error,
            false,
            first.Ambiguous || second.Ambiguous,
            Math.Abs(error) > 1.0 ? 1.0 : 0.0,
            Math.Max(0.02, (first.SigmaSamples + second.SigmaSamples) / Math.Sqrt(2.0)),
            Math.Max(first.ResidualRmse, second.ResidualRmse));
    }
}

internal static class CaliperAlgorithms
{
    public static EvaluationSample LegacyGradientCentroid(CaliperCase item) => Evaluate(item, EdgeFitModel.Centroid);
    public static EvaluationSample Quadratic(CaliperCase item) => Evaluate(item, EdgeFitModel.Quadratic);
    public static EvaluationSample GaussianDerivative(CaliperCase item) => Evaluate(item, EdgeFitModel.GaussianDerivative);
    public static EvaluationSample Erf(CaliperCase item) => Evaluate(item, EdgeFitModel.Erf);

    private static EvaluationSample Evaluate(CaliperCase item, EdgeFitModel model)
    {
        var response = model == EdgeFitModel.GaussianDerivative ? GaussianDerivativeResponse(item.Profile, 1.0) : Derivative(item.Profile);
        var firstSign = item.Inverted ? -1 : 1;
        var first = Locate(response, item.Profile, 4, item.Profile.Length / 2, firstSign, model);
        var second = Locate(response, item.Profile, item.Profile.Length / 2, item.Profile.Length - 5, -firstSign, model);
        if (!first.Success || !second.Success || second.Position <= first.Position)
        {
            return EvaluationSample.Failure;
        }

        var measured = second.Position - first.Position;
        var error = measured - item.TrueWidth;
        return new EvaluationSample(
            error,
            false,
            first.Ambiguous || second.Ambiguous,
            Math.Abs(error) > 1.0 ? 1.0 : 0.0,
            Math.Max(0.02, (first.Sigma + second.Sigma) / Math.Sqrt(2.0)),
            Math.Max(first.Sigma, second.Sigma));
    }

    private static EdgeLocation Locate(double[] response, double[] profile, int start, int end, int sign, EdgeFitModel model)
    {
        var peakIndex = -1;
        var peak = double.NegativeInfinity;
        for (var index = Math.Max(start, 2); index <= Math.Min(end, response.Length - 3); index++)
        {
            var value = response[index] * sign;
            if (value > peak)
            {
                peak = value;
                peakIndex = index;
            }
        }

        if (peakIndex < 0 || peak <= 1e-6)
        {
            return EdgeLocation.Failure;
        }

        var second = double.NegativeInfinity;
        for (var index = Math.Max(start, 2); index <= Math.Min(end, response.Length - 3); index++)
        {
            if (Math.Abs(index - peakIndex) <= 3)
            {
                continue;
            }

            second = Math.Max(second, response[index] * sign);
        }

        var position = model switch
        {
            EdgeFitModel.Centroid => WeightedCentroid(response, peakIndex, sign),
            EdgeFitModel.Erf => FitErf(profile, peakIndex, sign),
            _ => QuadraticPeak(response, peakIndex, sign)
        };
        var curvature = Math.Abs((response[peakIndex - 1] * sign) - (2 * peak) + (response[peakIndex + 1] * sign));
        var sigma = Math.Clamp(1.0 / Math.Sqrt(Math.Max(curvature, 1e-4)), 0.02, 2.0);
        return new EdgeLocation(true, position, second > peak * 0.82, sigma);
    }

    private static double WeightedCentroid(IReadOnlyList<double> response, int peakIndex, int sign)
    {
        double weighted = 0;
        double total = 0;
        for (var index = peakIndex - 2; index <= peakIndex + 2; index++)
        {
            var weight = Math.Max(0, response[index] * sign);
            weighted += index * weight;
            total += weight;
        }

        return total > 1e-12 ? weighted / total : peakIndex;
    }

    private static double QuadraticPeak(IReadOnlyList<double> response, int peakIndex, int sign)
    {
        var left = response[peakIndex - 1] * sign;
        var center = response[peakIndex] * sign;
        var right = response[peakIndex + 1] * sign;
        var denominator = left - (2 * center) + right;
        var offset = Math.Abs(denominator) <= 1e-12 ? 0 : 0.5 * (left - right) / denominator;
        return peakIndex + Math.Clamp(offset, -1.0, 1.0);
    }

    private static double FitErf(IReadOnlyList<double> profile, int peakIndex, int sign)
    {
        var bestPosition = (double)peakIndex;
        var bestError = double.PositiveInfinity;
        for (var sigma = 0.35; sigma <= 3.0; sigma += 0.15)
        {
            for (var offset = -1.5; offset <= 1.5; offset += 0.05)
            {
                var position = peakIndex + offset;
                var xValues = new List<double>();
                var yValues = new List<double>();
                for (var index = Math.Max(0, peakIndex - 5); index <= Math.Min(profile.Count - 1, peakIndex + 5); index++)
                {
                    xValues.Add(0.5 * (1.0 + Erf(sign * (index - position) / (Math.Sqrt(2.0) * sigma))));
                    yValues.Add(profile[index]);
                }

                var meanX = xValues.Average();
                var meanY = yValues.Average();
                var varianceX = xValues.Sum(value => Math.Pow(value - meanX, 2));
                if (varianceX <= 1e-12)
                {
                    continue;
                }

                var amplitude = xValues.Zip(yValues).Sum(pair => (pair.First - meanX) * (pair.Second - meanY)) / varianceX;
                var baseline = meanY - (amplitude * meanX);
                var error = xValues.Zip(yValues).Sum(pair => Math.Pow(pair.Second - (baseline + (amplitude * pair.First)), 2));
                if (error < bestError)
                {
                    bestError = error;
                    bestPosition = position;
                }
            }
        }

        return bestPosition;
    }

    private static double[] Derivative(IReadOnlyList<double> values)
    {
        var result = new double[values.Count];
        for (var index = 1; index < values.Count - 1; index++)
        {
            result[index] = (values[index + 1] - values[index - 1]) * 0.5;
        }

        return result;
    }

    private static double[] GaussianDerivativeResponse(IReadOnlyList<double> values, double sigma)
    {
        var radius = Math.Max(2, (int)Math.Ceiling(sigma * 3));
        var kernel = Enumerable.Range(-radius, (radius * 2) + 1)
            .Select(index => Math.Exp(-(index * index) / (2 * sigma * sigma)))
            .ToArray();
        var sum = kernel.Sum();
        var smoothed = new double[values.Count];
        for (var index = 0; index < values.Count; index++)
        {
            double value = 0;
            for (var offset = -radius; offset <= radius; offset++)
            {
                value += values[Math.Clamp(index + offset, 0, values.Count - 1)] * kernel[offset + radius] / sum;
            }

            smoothed[index] = value;
        }

        return Derivative(smoothed);
    }

    private static double Erf(double value)
    {
        var sign = Math.Sign(value);
        var x = Math.Abs(value);
        var t = 1.0 / (1.0 + (0.3275911 * x));
        var y = 1.0 - (((((1.061405429 * t - 1.453152027) * t) + 1.421413741) * t - 0.284496736) * t + 0.254829592) * t * Math.Exp(-x * x);
        return sign * y;
    }

    private enum EdgeFitModel { Centroid, Quadratic, GaussianDerivative, Erf }
    private readonly record struct EdgeLocation(bool Success, double Position, bool Ambiguous, double Sigma)
    {
        public static EdgeLocation Failure => new(false, double.NaN, true, double.NaN);
    }
}

internal static class GeometryAlgorithms
{
    public static EvaluationSample CircleProductionWelsch(CircleCase item)
    {
        var seed = FitCircleAlgebraic(item.Points, null);
        if (!seed.Success)
        {
            return EvaluationSample.Failure;
        }

        var result = GeometryRefinementKernel.RefineCircle(
            item.Points.Select(point => new Point2d(point.X, point.Y)).ToArray(),
            seed.CenterX,
            seed.CenterY,
            seed.Radius,
            GeometryRefinementLoss.Welsch);
        if (!result.Success || result.Degenerate)
        {
            return EvaluationSample.Failure;
        }

        var sigma = result.Covariance.Count == 9 ? Math.Sqrt(Math.Max(result.Covariance[8], 1e-12)) : result.RobustScale;
        return new EvaluationSample(
            result.Radius - item.Radius,
            false,
            !result.Converged || item.CoverageDegrees < 70 || result.RobustScale > 2.0,
            result.Weights.Count(weight => weight < 0.25) / (double)result.Weights.Count,
            sigma,
            result.RobustScale);
    }

    public static EvaluationSample LineProductionWelsch(LineCase item)
    {
        var result = GeometryRefinementKernel.RefineLine(
            item.Points.Select(point => new Point2d(point.X, point.Y)).ToArray(),
            GeometryRefinementLoss.Welsch);
        if (!result.Success || result.Degenerate)
        {
            return EvaluationSample.Failure;
        }

        return new EvaluationSample(
            SignedAngleDifference(result.AngleDegrees, item.AngleDegrees),
            false,
            !result.Converged || result.RobustScale > 2.0,
            result.Weights.Count(weight => weight < 0.25) / (double)result.Weights.Count,
            result.SigmaAngleDegrees,
            result.RobustScale);
    }

    public static EvaluationSample CircleAlgebraic(CircleCase item)
    {
        var fit = FitCircleAlgebraic(item.Points, null);
        if (!fit.Success)
        {
            return EvaluationSample.Failure;
        }

        var residuals = CircleResiduals(item.Points, fit);
        var scale = RootMeanSquare(residuals);
        return new EvaluationSample(fit.Radius - item.Radius, false, item.CoverageDegrees < 60 || scale > 2.0, 0, Math.Max(scale, 1e-6), scale);
    }

    public static EvaluationSample CircleRobust(CircleCase item, RobustLoss loss)
    {
        var fit = FitCircleAlgebraic(item.Points, null);
        if (!fit.Success)
        {
            return EvaluationSample.Failure;
        }

        var weights = Enumerable.Repeat(1.0, item.Points.Count).ToArray();
        var converged = false;
        for (var iteration = 0; iteration < 30; iteration++)
        {
            var residuals = CircleResiduals(item.Points, fit);
            var scale = RobustScale(residuals);
            for (var index = 0; index < weights.Length; index++)
            {
                weights[index] = RobustWeight(residuals[index], scale, loss);
            }

            var normal = new double[3, 3];
            var rhs = new double[3];
            for (var index = 0; index < item.Points.Count; index++)
            {
                var point = item.Points[index];
                var dx = point.X - fit.CenterX;
                var dy = point.Y - fit.CenterY;
                var distance = Math.Sqrt((dx * dx) + (dy * dy));
                if (distance <= 1e-12)
                {
                    continue;
                }

                var jacobian = new[] { -dx / distance, -dy / distance, -1.0 };
                Accumulate(normal, rhs, jacobian, residuals[index], weights[index]);
            }

            if (!Solve(normal, rhs.Select(value => -value).ToArray(), out var delta))
            {
                return EvaluationSample.Failure;
            }

            fit = new CircleFit(true, fit.CenterX + delta[0], fit.CenterY + delta[1], fit.Radius + delta[2]);
            if (delta.Select(Math.Abs).Max() < 1e-8)
            {
                converged = true;
                break;
            }
        }

        if (!fit.Success || fit.Radius <= 0 || item.CoverageDegrees < 40)
        {
            return EvaluationSample.Failure;
        }

        var finalResiduals = CircleResiduals(item.Points, fit);
        var residualScale = RobustScale(finalResiduals);
        var covarianceSigma = CircleRadiusSigma(item.Points, fit, weights, residualScale);
        var outlierRate = weights.Count(weight => weight < 0.25) / (double)weights.Length;
        return new EvaluationSample(
            fit.Radius - item.Radius,
            false,
            !converged || item.CoverageDegrees < 70 || residualScale > 2.0,
            outlierRate,
            covarianceSigma,
            residualScale);
    }

    public static EvaluationSample LineFit(LineCase item, RobustLoss loss)
    {
        var weights = Enumerable.Repeat(1.0, item.Points.Count).ToArray();
        var fit = FitWeightedLine(item.Points, weights);
        if (!fit.Success)
        {
            return EvaluationSample.Failure;
        }

        var converged = loss == RobustLoss.L2;
        for (var iteration = 0; loss != RobustLoss.L2 && iteration < 30; iteration++)
        {
            var residuals = item.Points.Select(point => SignedLineResidual(point, fit)).ToArray();
            var scale = RobustScale(residuals);
            for (var index = 0; index < weights.Length; index++)
            {
                weights[index] = RobustWeight(residuals[index], scale, loss);
            }

            var next = FitWeightedLine(item.Points, weights);
            if (!next.Success)
            {
                return EvaluationSample.Failure;
            }

            if (AngleDifference(next.AngleDegrees, fit.AngleDegrees) < 1e-8 && Math.Abs(next.Offset - fit.Offset) < 1e-8)
            {
                converged = true;
                fit = next;
                break;
            }

            fit = next;
        }

        var finalResiduals = item.Points.Select(point => SignedLineResidual(point, fit)).ToArray();
        var residualScale = loss == RobustLoss.L2 ? RootMeanSquare(finalResiduals) : RobustScale(finalResiduals);
        var sigmaAngle = LineAngleSigma(item.Points, fit, weights, residualScale);
        var error = SignedAngleDifference(fit.AngleDegrees, item.AngleDegrees);
        return new EvaluationSample(
            error,
            false,
            !converged || residualScale > 2.0,
            weights.Count(weight => weight < 0.25) / (double)weights.Length,
            sigmaAngle,
            residualScale);
    }

    private static CircleFit FitCircleAlgebraic(IReadOnlyList<ObservedPoint> points, IReadOnlyList<double>? weights)
    {
        if (points.Count < 3)
        {
            return CircleFit.Failure;
        }

        var normal = new double[3, 3];
        var rhs = new double[3];
        for (var index = 0; index < points.Count; index++)
        {
            var point = points[index];
            var weight = weights?[index] ?? 1.0;
            var row = new[] { point.X, point.Y, 1.0 };
            var target = -((point.X * point.X) + (point.Y * point.Y));
            for (var r = 0; r < 3; r++)
            {
                rhs[r] += weight * row[r] * target;
                for (var c = 0; c < 3; c++)
                {
                    normal[r, c] += weight * row[r] * row[c];
                }
            }
        }

        if (!Solve(normal, rhs, out var solution))
        {
            return CircleFit.Failure;
        }

        var centerX = -solution[0] / 2.0;
        var centerY = -solution[1] / 2.0;
        var radiusSquared = (centerX * centerX) + (centerY * centerY) - solution[2];
        return radiusSquared > 0 && double.IsFinite(radiusSquared)
            ? new CircleFit(true, centerX, centerY, Math.Sqrt(radiusSquared))
            : CircleFit.Failure;
    }

    private static LineFitResult FitWeightedLine(IReadOnlyList<ObservedPoint> points, IReadOnlyList<double> weights)
    {
        var weightSum = weights.Sum();
        if (points.Count < 2 || weightSum <= 1e-12)
        {
            return LineFitResult.Failure;
        }

        var meanX = points.Select((point, index) => point.X * weights[index]).Sum() / weightSum;
        var meanY = points.Select((point, index) => point.Y * weights[index]).Sum() / weightSum;
        double sxx = 0;
        double syy = 0;
        double sxy = 0;
        for (var index = 0; index < points.Count; index++)
        {
            var dx = points[index].X - meanX;
            var dy = points[index].Y - meanY;
            sxx += weights[index] * dx * dx;
            syy += weights[index] * dy * dy;
            sxy += weights[index] * dx * dy;
        }

        if (sxx + syy <= 1e-12)
        {
            return LineFitResult.Failure;
        }

        var angle = 0.5 * Math.Atan2(2 * sxy, sxx - syy);
        var directionX = Math.Cos(angle);
        var directionY = Math.Sin(angle);
        var normalX = -directionY;
        var normalY = directionX;
        var offset = (normalX * meanX) + (normalY * meanY);
        return new LineFitResult(true, meanX, meanY, directionX, directionY, normalX, normalY, offset, angle * 180.0 / Math.PI);
    }

    private static double[] CircleResiduals(IReadOnlyList<ObservedPoint> points, CircleFit fit) =>
        points.Select(point => Math.Sqrt(Math.Pow(point.X - fit.CenterX, 2) + Math.Pow(point.Y - fit.CenterY, 2)) - fit.Radius).ToArray();

    private static double SignedLineResidual(ObservedPoint point, LineFitResult fit) => (fit.NormalX * point.X) + (fit.NormalY * point.Y) - fit.Offset;

    private static double CircleRadiusSigma(IReadOnlyList<ObservedPoint> points, CircleFit fit, IReadOnlyList<double> weights, double residualScale)
    {
        var normal = new double[3, 3];
        for (var index = 0; index < points.Count; index++)
        {
            var dx = points[index].X - fit.CenterX;
            var dy = points[index].Y - fit.CenterY;
            var distance = Math.Sqrt((dx * dx) + (dy * dy));
            if (distance <= 1e-12)
            {
                continue;
            }

            var jacobian = new[] { -dx / distance, -dy / distance, -1.0 };
            for (var r = 0; r < 3; r++)
            {
                for (var c = 0; c < 3; c++)
                {
                    normal[r, c] += weights[index] * jacobian[r] * jacobian[c];
                }
            }
        }

        return TryInvert(normal, out var inverse)
            ? Math.Sqrt(Math.Max(inverse[2, 2] * residualScale * residualScale, 1e-12))
            : Math.Max(residualScale, 1e-6);
    }

    private static double LineAngleSigma(IReadOnlyList<ObservedPoint> points, LineFitResult fit, IReadOnlyList<double> weights, double residualScale)
    {
        double projectedEnergy = 0;
        for (var index = 0; index < points.Count; index++)
        {
            var dx = points[index].X - fit.CenterX;
            var dy = points[index].Y - fit.CenterY;
            var projected = (dx * fit.DirectionX) + (dy * fit.DirectionY);
            projectedEnergy += weights[index] * projected * projected;
        }

        var sigmaRadians = residualScale / Math.Sqrt(Math.Max(projectedEnergy, 1e-12));
        return Math.Max(sigmaRadians * 180.0 / Math.PI, 1e-6);
    }

    private static void Accumulate(double[,] normal, double[] rhs, IReadOnlyList<double> jacobian, double residual, double weight)
    {
        for (var r = 0; r < jacobian.Count; r++)
        {
            rhs[r] += weight * jacobian[r] * residual;
            for (var c = 0; c < jacobian.Count; c++)
            {
                normal[r, c] += weight * jacobian[r] * jacobian[c];
            }
        }
    }

    private static double RobustWeight(double residual, double scale, RobustLoss loss)
    {
        if (loss == RobustLoss.L2)
        {
            return 1.0;
        }

        var normalized = Math.Abs(residual) / Math.Max(scale, 1e-9);
        return loss switch
        {
            RobustLoss.Huber => normalized <= 1.345 ? 1.0 : 1.345 / normalized,
            RobustLoss.Welsch => Math.Exp(-Math.Pow(normalized / 2.9846, 2)),
            _ => 1.0
        };
    }

    private static double RobustScale(IReadOnlyList<double> residuals)
    {
        var median = Median(residuals);
        var deviations = residuals.Select(value => Math.Abs(value - median)).ToArray();
        return Math.Max(Median(deviations) * 1.4826, 1e-6);
    }

    private static double Median(IReadOnlyList<double> values)
    {
        var sorted = values.OrderBy(value => value).ToArray();
        return sorted.Length % 2 == 1
            ? sorted[sorted.Length / 2]
            : (sorted[(sorted.Length / 2) - 1] + sorted[sorted.Length / 2]) / 2.0;
    }

    private static double RootMeanSquare(IReadOnlyList<double> values) => Math.Sqrt(values.Average(value => value * value));
    private static double AngleDifference(double first, double second) => Math.Abs(SignedAngleDifference(first, second));
    private static double SignedAngleDifference(double first, double second)
    {
        var difference = first - second;
        while (difference > 90) difference -= 180;
        while (difference < -90) difference += 180;
        return difference;
    }

    private static bool Solve(double[,] matrix, double[] rhs, out double[] solution)
    {
        var size = rhs.Length;
        var augmented = new double[size, size + 1];
        for (var row = 0; row < size; row++)
        {
            for (var column = 0; column < size; column++) augmented[row, column] = matrix[row, column];
            augmented[row, size] = rhs[row];
        }

        for (var pivot = 0; pivot < size; pivot++)
        {
            var best = pivot;
            for (var row = pivot + 1; row < size; row++)
            {
                if (Math.Abs(augmented[row, pivot]) > Math.Abs(augmented[best, pivot])) best = row;
            }

            if (Math.Abs(augmented[best, pivot]) <= 1e-12)
            {
                solution = [];
                return false;
            }

            if (best != pivot)
            {
                for (var column = pivot; column <= size; column++)
                {
                    (augmented[pivot, column], augmented[best, column]) = (augmented[best, column], augmented[pivot, column]);
                }
            }

            var divisor = augmented[pivot, pivot];
            for (var column = pivot; column <= size; column++) augmented[pivot, column] /= divisor;
            for (var row = 0; row < size; row++)
            {
                if (row == pivot) continue;
                var factor = augmented[row, pivot];
                for (var column = pivot; column <= size; column++) augmented[row, column] -= factor * augmented[pivot, column];
            }
        }

        solution = Enumerable.Range(0, size).Select(row => augmented[row, size]).ToArray();
        return solution.All(double.IsFinite);
    }

    private static bool TryInvert(double[,] matrix, out double[,] inverse)
    {
        var size = matrix.GetLength(0);
        inverse = new double[size, size];
        for (var column = 0; column < size; column++)
        {
            var rhs = new double[size];
            rhs[column] = 1;
            if (!Solve(matrix, rhs, out var solution)) return false;
            for (var row = 0; row < size; row++) inverse[row, column] = solution[row];
        }

        return true;
    }

    private readonly record struct CircleFit(bool Success, double CenterX, double CenterY, double Radius)
    {
        public static CircleFit Failure => new(false, double.NaN, double.NaN, double.NaN);
    }

    private readonly record struct LineFitResult(bool Success, double CenterX, double CenterY, double DirectionX, double DirectionY, double NormalX, double NormalY, double Offset, double AngleDegrees)
    {
        public static LineFitResult Failure => new(false, double.NaN, double.NaN, double.NaN, double.NaN, double.NaN, double.NaN, double.NaN, double.NaN);
    }
}

internal static class SyntheticData
{
    public static double[] CreateCaliperProfile(int count, double firstEdge, double secondEdge, double blurSigma, double noiseSigma, double textureAmplitude, bool inverted, string scenario, Random random)
    {
        var result = new double[count];
        var background = scenario == "saturation" ? 0.0 : 28.0;
        var foreground = scenario == "saturation" ? 255.0 : 224.0;
        if (inverted) (background, foreground) = (foreground, background);
        for (var index = 0; index < count; index++)
        {
            var firstStep = 0.5 * (1 + Erf((index - firstEdge) / (Math.Sqrt(2) * blurSigma)));
            var secondStep = 0.5 * (1 + Erf((index - secondEdge) / (Math.Sqrt(2) * blurSigma)));
            var inside = firstStep - secondStep;
            var value = background + ((foreground - background) * inside);
            value += textureAmplitude * Math.Sin((index * 0.47) + 0.3);
            if (scenario == "double_edge") value += 38.0 * Math.Exp(-Math.Pow(index - (firstEdge + 8.0), 2) / 3.0);
            if (scenario == "occlusion" && index > firstEdge + 5 && index < firstEdge + 14) value = (background + foreground) / 2.0;
            value += NextGaussian(random) * noiseSigma;
            result[index] = Math.Clamp(value, 0, 255);
        }

        return result;
    }

    public static IReadOnlyList<ObservedPoint> CreateCirclePoints(int count, double centerX, double centerY, double radius, double coverageDegrees, double noiseSigma, double outlierFraction, string scenario, Random random)
    {
        var points = new List<ObservedPoint>(count);
        var start = Lerp(-Math.PI, Math.PI, random.NextDouble());
        var coverage = coverageDegrees * Math.PI / 180.0;
        var outliers = (int)Math.Round(count * outlierFraction);
        for (var index = 0; index < count; index++)
        {
            var t = count == 1 ? 0 : index / (double)(count - 1);
            var angle = start + (coverage * t);
            var localRadius = scenario == "ellipse_interference" && index < outliers
                ? radius * (1.0 + (0.18 * Math.Cos(2 * angle)))
                : radius;
            var isOutlier = index < outliers;
            var radialNoise = NextGaussian(random) * noiseSigma;
            if (isOutlier && scenario != "ellipse_interference") radialNoise += Lerp(-radius * 0.35, radius * 0.35, random.NextDouble());
            points.Add(new ObservedPoint(
                centerX + ((localRadius + radialNoise) * Math.Cos(angle)),
                centerY + ((localRadius + radialNoise) * Math.Sin(angle)),
                isOutlier));
        }

        Shuffle(points, random);
        return points;
    }

    public static IReadOnlyList<ObservedPoint> CreateLinePoints(int count, double angleDegrees, double offset, double noiseSigma, double outlierFraction, string scenario, Random random)
    {
        var radians = angleDegrees * Math.PI / 180.0;
        var dx = Math.Cos(radians);
        var dy = Math.Sin(radians);
        var nx = -dy;
        var ny = dx;
        var points = new List<ObservedPoint>(count);
        var outliers = (int)Math.Round(count * outlierFraction);
        for (var index = 0; index < count; index++)
        {
            var t = Lerp(-90, 90, index / (double)Math.Max(count - 1, 1));
            if (scenario is "broken_edge" or "occlusion" && t > -12 && t < 18) t += 32;
            var normalNoise = NextGaussian(random) * noiseSigma;
            var isOutlier = index < outliers;
            if (isOutlier) normalNoise += Lerp(-28, 28, random.NextDouble());
            if (scenario == "spur" && index % 17 == 0) normalNoise += 18;
            points.Add(new ObservedPoint((dx * t) + (nx * (offset + normalNoise)), (dy * t) + (ny * (offset + normalNoise)), isOutlier || (scenario == "spur" && index % 17 == 0)));
        }

        Shuffle(points, random);
        return points;
    }

    public static Mat CreateAnomalyImage(bool anomaly, Random random)
    {
        var image = new Mat(2, 2, MatType.CV_8UC3);
        var baseValue = 96 + random.Next(-5, 6);
        for (var y = 0; y < 2; y++)
        {
            for (var x = 0; x < 2; x++)
            {
                var jitter = random.Next(-3, 4);
                image.Set(y, x, new Vec3b((byte)(baseValue + jitter), (byte)(baseValue + jitter), (byte)(baseValue + jitter)));
            }
        }

        if (anomaly)
        {
            image.Set(1, 1, new Vec3b((byte)random.Next(0, 24), (byte)random.Next(210, 256), (byte)random.Next(0, 24)));
        }

        return image;
    }

    private static void Shuffle<T>(IList<T> items, Random random)
    {
        for (var index = items.Count - 1; index > 0; index--)
        {
            var swap = random.Next(index + 1);
            (items[index], items[swap]) = (items[swap], items[index]);
        }
    }

    private static double NextGaussian(Random random)
    {
        var u1 = Math.Max(random.NextDouble(), 1e-12);
        var u2 = random.NextDouble();
        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
    }

    private static double Lerp(double min, double max, double t) => min + ((max - min) * t);
    private static double Erf(double value)
    {
        var sign = Math.Sign(value);
        var x = Math.Abs(value);
        var t = 1.0 / (1.0 + (0.3275911 * x));
        var y = 1.0 - (((((1.061405429 * t - 1.453152027) * t) + 1.421413741) * t - 0.284496736) * t + 0.254829592) * t * Math.Exp(-x * x);
        return sign * y;
    }
}

internal static class BenchmarkMarkdown
{
    public static string Create(BenchmarkReport report)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# ClearVision Operator Precision Benchmark");
        builder.AppendLine();
        builder.AppendLine($"- Label: `{report.Label}`");
        builder.AppendLine($"- Source SHA: `{report.SourceSha}`");
        builder.AppendLine($"- Dataset: `{report.Dataset.DatasetId}` `{report.Dataset.Version}` manifest SHA `{report.Dataset.ManifestSha256}` generated-data SHA `{report.Dataset.GeneratedDataSha256}`");
        builder.AppendLine($"- Seed: `{report.Dataset.Seed}`");
        builder.AppendLine($"- Model SHA: `{report.Model.Sha256}`");
        builder.AppendLine($"- Preprocess fingerprint: `{report.Model.PreprocessFingerprint}`");
        builder.AppendLine($"- Harness: `{report.Harness.CommitSha}` / source SHA `{report.Harness.ProgramSha256}` / dirty `{report.Harness.RepositoryDirty}`");
        builder.AppendLine($"- Environment: `{report.Environment.Framework}` / `{report.Environment.Os}` / `{report.Environment.Architecture}`");
        builder.AppendLine();
        builder.AppendLine("> Synthetic mathematical and preprocessing-contract evidence only. This report is not field validation, release readiness, E4, or commercial-grade accuracy evidence.");
        builder.AppendLine();
        builder.AppendLine("## Metrics");
        builder.AppendLine();
        builder.AppendLine("| Domain | Algorithm | Split | Cases | Bias | RMSE | P95 error | Failure | Ambiguity | Outlier | Latency P50/P95 ms | Allocation B/case |");
        builder.AppendLine("|---|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|");
        foreach (var metric in report.Metrics)
        {
            builder.AppendLine($"| {metric.Domain} | {metric.Algorithm} | {metric.Split} | {metric.Cases} | {Format(metric.Bias)} | {Format(metric.Rmse)} | {Format(metric.P95Error)} | {Format(metric.FailureRate)} | {Format(metric.AmbiguityRate)} | {Format(metric.OutlierRate)} | {Format(metric.LatencyP50Milliseconds)} / {Format(metric.LatencyP95Milliseconds)} | {metric.AllocatedBytesPerCase} |");
        }

        builder.AppendLine();
        builder.AppendLine("## Decisions");
        builder.AppendLine();
        builder.AppendLine("| Domain | Baseline | Winner | Baseline score | Winner score | Adopted | Reason |");
        builder.AppendLine("|---|---|---|---:|---:|---|---|");
        foreach (var decision in report.Decisions)
        {
            builder.AppendLine($"| {decision.Domain} | {decision.Baseline} | {decision.Winner} | {Format(decision.BaselineScore)} | {Format(decision.WinnerScore)} | {decision.Adopted} | {decision.Reason} |");
        }

        builder.AppendLine();
        builder.AppendLine("## Reproduction");
        builder.AppendLine();
        builder.AppendLine("```powershell");
        builder.AppendLine($"& \"./scripts/run-operator-precision-benchmark.ps1\" -Profile acceptance -Label {report.Label}");
        builder.AppendLine("```");
        return builder.ToString();
    }

    private static string Format(double value) => double.IsFinite(value) ? value.ToString("0.######", CultureInfo.InvariantCulture) : "n/a";
}

internal sealed record BenchmarkOptions(string ManifestPath, string Label, string SourceSha, string HarnessCommitSha, string SdkVersion, bool RepositoryDirty, int Warmup, int Iterations, string OutputPath, string ReportPath)
{
    public static BenchmarkOptions Parse(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < args.Length; index += 2)
        {
            if (index + 1 >= args.Length || !args[index].StartsWith("--", StringComparison.Ordinal)) throw new ArgumentException("Arguments must be --name value pairs.");
            values[args[index][2..]] = args[index + 1];
        }

        return new BenchmarkOptions(
            Require("manifest"),
            Require("label"),
            Require("source-sha"),
            Require("harness-commit-sha"),
            Require("sdk-version"),
            bool.Parse(Require("repository-dirty")),
            int.Parse(Require("warmup"), CultureInfo.InvariantCulture),
            int.Parse(Require("iterations"), CultureInfo.InvariantCulture),
            Require("output"),
            Require("report"));

        string Require(string name) => values.TryGetValue(name, out var value) ? value : throw new ArgumentException($"Missing --{name}.");
    }
}

internal sealed record BenchmarkReport(
    string SchemaVersion,
    string BenchmarkId,
    string Label,
    string SourceSha,
    DateTime GeneratedAtUtc,
    DatasetIdentity Dataset,
    ModelIdentity Model,
    HarnessIdentity Harness,
    BenchmarkEnvironment Environment,
    int WarmupIterations,
    int MeasurementIterations,
    IReadOnlyList<BenchmarkMetric> Metrics,
    IReadOnlyList<BenchmarkDecision> Decisions);

internal sealed record DatasetIdentity(string DatasetId, string Version, string ManifestSha256, string GeneratedDataSha256, int Seed, string License, string ClaimBoundary, IReadOnlyDictionary<string, int> SplitCounts);
internal sealed record ModelIdentity(string Path, string Sha256, string Source, string License, string PreprocessFingerprint, string ManifestPath, string ManifestSha256, string ReferenceInputSha256, string ReferenceOutputSha256);
internal sealed record HarnessIdentity(string CommitSha, string ProgramSha256, string ProjectSha256, string RunScriptSha256, bool RepositoryDirty);
internal sealed record BenchmarkEnvironment(string Framework, string Os, string Architecture, int ProcessorCount, string RuntimeVersion, string ProcessorDescription, string SdkVersion, string OpenCvVersion, bool ServerGc, string GcLatencyMode);
internal sealed record BenchmarkDecision(string Domain, string Baseline, string Winner, double BaselineScore, double WinnerScore, bool Adopted, string Reason);
internal sealed record BenchmarkMetric(string Domain, string Algorithm, string Split, string Unit, int Cases, double Bias, double Rmse, double P95Error, double FailureRate, double AmbiguityRate, double OutlierRate, double LatencyP50Milliseconds, double LatencyP95Milliseconds, long AllocatedBytesPerCase, IReadOnlyDictionary<string, double> Extra);
internal readonly record struct TimingSummary(double P50Milliseconds, double P95Milliseconds, long AllocatedBytesPerCase);
internal readonly record struct EvaluationSample(double SignedError, bool Failed, bool Ambiguous, double OutlierRate, double Sigma, double ResidualScale)
{
    public static EvaluationSample Success => new(0, false, false, 0, 1, 0);
    public static EvaluationSample Failure => new(double.NaN, true, true, 1, double.NaN, double.NaN);
}

internal interface IBenchmarkCase { string Split { get; } }
internal sealed record CaliperCase(int Index, string Split, string Scenario, double[] Profile, double FirstEdge, double SecondEdge, double TrueWidth, bool Inverted) : IBenchmarkCase;
internal sealed record CircleCase(int Index, string Split, string Scenario, IReadOnlyList<ObservedPoint> Points, double CenterX, double CenterY, double Radius, double CoverageDegrees) : IBenchmarkCase;
internal sealed record LineCase(int Index, string Split, string Scenario, IReadOnlyList<ObservedPoint> Points, double AngleDegrees, double Offset) : IBenchmarkCase;
internal sealed record AnomalyCase(int Index, string Split, bool IsAnomaly, Mat Image) : IBenchmarkCase;
internal readonly record struct ObservedPoint(double X, double Y, bool IsOutlier);
internal readonly record struct NumericRange(double Min, double Max);
internal enum RobustLoss { L2, Huber, Welsch }
