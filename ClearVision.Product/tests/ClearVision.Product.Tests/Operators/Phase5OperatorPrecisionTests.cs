using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.ValueObjects;
using ClearVision.Product.Infrastructure.AI.Anomaly;
using ClearVision.Product.Infrastructure.Operators;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using OpenCvSharp;

namespace ClearVision.Product.Tests.Operators;

[TestClassification(TestDomain.General, TestPurpose.Regression, TestLane.Pr, TestEvidenceType.Contract, TestOracleType.Contract, TestResourceRequirement.None, TestExpectedDuration.Fast, TestFlakyPolicy.Blocking, "product", Suites = "OperatorPrecisionPhase5")]
public class Phase5OperatorPrecisionTests
{
    [Fact]
    public void GeometryRefinementKernel_WelschCircle_ShouldConvergeWithOutliersAndExposeCovariance()
    {
        const double centerX = 12.5;
        const double centerY = -7.25;
        const double radius = 42.0;
        var points = Enumerable.Range(0, 96)
            .Select(index =>
            {
                var angle = index * Math.PI * 2.0 / 96.0;
                var radialOffset = index % 13 == 0 ? 12.0 : Math.Sin(index * 0.7) * 0.08;
                return new Point2d(
                    centerX + ((radius + radialOffset) * Math.Cos(angle)),
                    centerY + ((radius + radialOffset) * Math.Sin(angle)));
            })
            .ToArray();

        var result = GeometryRefinementKernel.RefineCircle(points, centerX + 1.0, centerY - 1.0, radius + 1.5, GeometryRefinementLoss.Welsch);

        result.Success.Should().BeTrue(result.FailureReason);
        result.Converged.Should().BeTrue();
        result.Degenerate.Should().BeFalse();
        result.Radius.Should().BeApproximately(radius, 0.25);
        result.CenterX.Should().BeApproximately(centerX, 0.25);
        result.CenterY.Should().BeApproximately(centerY, 0.25);
        result.Covariance.Should().HaveCount(9);
        result.Weights.Count(weight => weight < 0.25).Should().BeGreaterThan(0);
    }

    [Fact]
    public void GeometryRefinementKernel_WelschLine_ShouldRejectSpursAndReportDiagnostics()
    {
        const double angleDegrees = 27.5;
        var angle = angleDegrees * Math.PI / 180.0;
        var direction = new Point2d(Math.Cos(angle), Math.Sin(angle));
        var normal = new Point2d(-direction.Y, direction.X);
        var points = Enumerable.Range(0, 120)
            .Select(index =>
            {
                var t = -80.0 + (160.0 * index / 119.0);
                var noise = Math.Sin(index * 0.9) * 0.06 + (index % 19 == 0 ? 16.0 : 0.0);
                return new Point2d((direction.X * t) + (normal.X * noise), (direction.Y * t) + (normal.Y * noise));
            })
            .ToArray();

        var result = GeometryRefinementKernel.RefineLine(points, GeometryRefinementLoss.Welsch);

        result.Success.Should().BeTrue(result.FailureReason);
        result.Converged.Should().BeTrue();
        result.Degenerate.Should().BeFalse();
        NormalizeLineAngle(result.AngleDegrees).Should().BeApproximately(angleDegrees, 0.15);
        result.Weights.Count(weight => weight < 0.25).Should().BeGreaterThan(0);
        result.Covariance.Should().HaveCount(4);
        result.SigmaAngleDegrees.Should().BePositive();
    }

    [Fact]
    public async Task CaliperTool_UnprovenEdgeModels_ShouldRemainOutOfFormalOperator()
    {
        var sut = new CaliperToolOperator(Substitute.For<ILogger<CaliperToolOperator>>());
        using var mat = new Mat(64, 192, MatType.CV_8UC1, Scalar.Black);
        Cv2.Rectangle(mat, new Rect(50, 0, 80, mat.Height), Scalar.White, -1);
        Cv2.GaussianBlur(mat, mat, new Size(0, 0), 1.2);
        using var image = new ImageWrapper(mat.Clone());
        var op = new Operator("caliper-phase5", OperatorType.CaliperTool, 0, 0);
        Add(op, "Direction", "Horizontal", "string");
        Add(op, "Polarity", "Both", "string");
        Add(op, "EdgeThreshold", 5.0, "double");
        Add(op, "ExpectedCount", 1, "int");
        Add(op, "SubpixelAccuracy", true, "bool");

        var result = await sut.ExecuteAsync(op, TestHelpers.CreateImageInputs(image));

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.OutputData!["EdgeModel"].Should().Be("Legacy");
        Convert.ToDouble(result.OutputData["Width"]).Should().BeApproximately(80.0, 0.8);
        double.IsNaN(Convert.ToDouble(result.OutputData["FitResidual"])).Should().BeTrue();
        result.OutputData["EdgeDiagnostics"].Should().BeAssignableTo<IEnumerable<Dictionary<string, object>>>();
        var evidence = result.OutputData["MeasurementEvidence"].Should().BeOfType<MeasurementEvidence>().Subject;
        evidence.Provenance.Should().Be(MeasurementEvidenceProvenance.Heuristic);
        evidence.SourceAlgorithm.Should().StartWith("Legacy/");
        evidence.QualityFlags.Should().Contain("HeuristicUncertainty");
        evidence.QualityFlags.Should().Contain("LegacyCompatibility");
        (result.OutputData["Image"] as ImageWrapper)?.Dispose();
    }

    [Fact]
    public async Task LineMeasurement_Welsch_ShouldUseRobustRefineWithoutLegacyFallback()
    {
        var sut = new LineMeasurementOperator(Substitute.For<ILogger<LineMeasurementOperator>>());
        using var mat = new Mat(256, 256, MatType.CV_8UC1, Scalar.Black);
        Cv2.Line(mat, new Point(20, 58), new Point(235, 168), Scalar.White, 5, LineTypes.AntiAlias);
        Cv2.Line(mat, new Point(100, 20), new Point(100, 80), Scalar.White, 3, LineTypes.AntiAlias);
        using var image = new ImageWrapper(mat.Clone());
        var op = new Operator("line-phase5", OperatorType.LineMeasurement, 0, 0);
        Add(op, "Method", "FitLine", "string");
        Add(op, "Threshold", 40, "int");
        Add(op, "MinLength", 40.0, "double");
        Add(op, "MaxGap", 12.0, "double");
        Add(op, "FitLoss", "Welsch", "string");

        var result = await sut.ExecuteAsync(op, TestHelpers.CreateImageInputs(image));

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.OutputData!["FitLoss"].Should().Be("Welsch");
        result.OutputData["RefineAlgorithm"].ToString().Should().Contain("Welsch");
        result.OutputData["SeedAlgorithm"].Should().Be("HoughSegment");
        result.OutputData["CovarianceCalibrated"].Should().Be(false);
        var evidence = result.OutputData["MeasurementEvidence"].Should().BeOfType<MeasurementEvidence>().Subject;
        evidence.QualityFlags.Should().Contain("UncalibratedCovariance");
        (result.OutputData["Image"] as ImageWrapper)?.Dispose();
    }

    [Fact]
    public async Task LineMeasurement_L2Evidence_ShouldNotTreatPixelResidualAsDegreeSigma()
    {
        var sut = new LineMeasurementOperator(Substitute.For<ILogger<LineMeasurementOperator>>());
        using var mat = new Mat(192, 192, MatType.CV_8UC1, Scalar.Black);
        Cv2.Line(mat, new Point(16, 40), new Point(176, 136), Scalar.White, 4, LineTypes.AntiAlias);
        using var image = new ImageWrapper(mat.Clone());
        var op = new Operator("line-l2-evidence", OperatorType.LineMeasurement, 0, 0);
        Add(op, "Method", "FitLine", "string");
        Add(op, "Threshold", 30, "int");
        Add(op, "MinLength", 30.0, "double");
        Add(op, "MaxGap", 8.0, "double");
        Add(op, "FitLoss", "L2", "string");

        var result = await sut.ExecuteAsync(op, TestHelpers.CreateImageInputs(image));

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        Convert.ToDouble(result.OutputData!["UncertaintyPx"]).Should().BeGreaterThanOrEqualTo(0.0);
        var evidence = result.OutputData["MeasurementEvidence"].Should().BeOfType<MeasurementEvidence>().Subject;
        evidence.Unit.Should().Be("deg");
        evidence.Sigma.Should().BeNull();
        evidence.QualityFlags.Should().Contain(new[] { "AngleSigmaUnavailable", "ResidualUncertaintyOnly", "LegacyCompatibility" });
        (result.OutputData["Image"] as ImageWrapper)?.Dispose();
    }

    [Fact]
    public void MeasurementEvidence_InvalidSigmaOrCovariance_ShouldFailClosedWithoutChangingMatrixShape()
    {
        var op = new Operator("evidence-invalid", OperatorType.LineMeasurement, 0, 0);

        var evidence = MeasurementEvidenceFactory.Create(
            op,
            12.0,
            "deg",
            "ImagePixel",
            -0.1,
            new[] { 1.0, double.NaN, 0.0, 1.0 },
            MeasurementEvidenceProvenance.Heuristic,
            "test");

        evidence.Sigma.Should().BeNull();
        evidence.Covariance.Should().BeNull();
        evidence.QualityFlags.Should().Contain(new[] { "InvalidSigma", "InvalidCovariance" });
    }

    [Fact]
    public void OnnxSessionCache_ShouldRejectSamePathSameLengthModelContentReplacement()
    {
        var tempDirectory = Directory.CreateTempSubdirectory("anomaly-cache-phase5-");
        var sourceModelPath = ResolveRepoPath("ClearVision.Product/tests/TestData/model_test_suite/identity_2x2/identity_2x2.onnx");
        var manifestPath = ResolveRepoPath("ClearVision.Product/tests/TestData/model_test_suite/identity_2x2/embedding_manifest.json");
        var copiedModelPath = Path.Combine(tempDirectory.FullName, "identity-copy.onnx");
        File.Copy(sourceModelPath, copiedModelPath);
        var originalWriteTime = File.GetLastWriteTimeUtc(copiedModelPath);
        var identity = AnomalyEmbeddingManifest.LoadAndValidate(manifestPath, copiedModelPath);
        var options = new SimplePatchCoreOptions
        {
            PatchSize = 2,
            PatchStride = 2,
            CoresetRatio = 1.0,
            FeatureExtractorId = "onnx_embedding",
            EmbeddingModelPath = copiedModelPath,
            EmbeddingManifestPath = manifestPath,
            EmbeddingModelSha256 = identity.ModelSha256,
            PreprocessFingerprint = identity.PreprocessFingerprint,
            EmbeddingPreprocess = identity.Preprocess
        };
        using var image = new Mat(2, 2, MatType.CV_8UC3, new Scalar(80, 90, 100));

        _ = OnnxPatchEmbeddingExtractor.ExtractEmbedding(image, options);
        var bytes = File.ReadAllBytes(copiedModelPath);
        bytes[^1] ^= 0x01;
        File.WriteAllBytes(copiedModelPath, bytes);
        File.SetLastWriteTimeUtc(copiedModelPath, originalWriteTime);

        Action act = () => OnnxPatchEmbeddingExtractor.ExtractEmbedding(image, options);
        act.Should().Throw<InvalidOperationException>().WithMessage("*SHA mismatch*");

        OnnxPatchEmbeddingExtractor.ClearSessionCache();
        Directory.Delete(tempDirectory.FullName, recursive: true);
    }

    [Fact]
    public void CircleCaliperFitV2_WelschRefinement_ShouldExposeUncalibratedCovarianceDiagnostics()
    {
        using var gray = new Mat(256, 256, MatType.CV_8UC1, Scalar.Black);
        Cv2.Circle(gray, new Point(128, 128), 70, Scalar.White, -1, LineTypes.AntiAlias);
        Cv2.GaussianBlur(gray, gray, new Size(0, 0), 1.1);
        var request = new CircleCaliperFitV2Request
        {
            SearchCenterX = 128,
            SearchCenterY = 128,
            MinRadius = 55,
            MaxRadius = 85,
            NominalRadius = 70,
            CaliperCount = 96,
            ProfileSampleCount = 129,
            AveragingThickness = 5,
            GaussianSigma = 1.2,
            EdgePolarity = CircleCaliperFitV2EdgePolarity.LightToDark,
            MinEdgeStrength = 3,
            MinValidCalipers = 40,
            MinCoverageRatio = 0.4,
            MinAngularCoverageDegrees = 240,
            OutlierMode = CircleCaliperFitV2OutlierMode.Mad,
            RefinementLoss = CircleCaliperFitV2RefinementLoss.Welsch,
            MaxOutlierIterations = 3,
            MaxResidualRmse = 2.0
        };

        var result = CircleCaliperFitV2Kernel.Fit(gray, request);

        result.Success.Should().BeTrue(result.FailureMessage);
        result.Radius.Should().BeApproximately(70.0, 0.8);
        result.Diagnostics.Should().Contain(diagnostic => diagnostic.Code == "refinement.loss");
        result.Diagnostics.Should().Contain(diagnostic => diagnostic.Code == "refinement.covarianceCalibrated" && diagnostic.Value == 0.0);
        result.Diagnostics.Count(diagnostic => diagnostic.Code.StartsWith("refinement.covariance.", StringComparison.Ordinal) && diagnostic.Code != "refinement.covarianceCalibrated").Should().Be(9);
    }

    [Fact]
    public async Task AnomalyOnnx_InferenceWithDifferentPreprocessManifest_ShouldFailClosed()
    {
        var sut = new AnomalyDetectionOperator(Substitute.For<ILogger<AnomalyDetectionOperator>>());
        var tempDirectory = Directory.CreateTempSubdirectory("anomaly-phase5-");
        var featureBankPath = Path.Combine(tempDirectory.FullName, "bank.json");
        var mismatchedManifestPath = Path.Combine(tempDirectory.FullName, "mismatch-manifest.json");
        var modelPath = ResolveRepoPath("ClearVision.Product/tests/TestData/model_test_suite/identity_2x2/identity_2x2.onnx");
        var manifestPath = ResolveRepoPath("ClearVision.Product/tests/TestData/model_test_suite/identity_2x2/embedding_manifest.json");

        var train = CreateAnomalyOperator("train", featureBankPath);
        Add(train, "SaveFeatureBankPath", featureBankPath, "file");
        Add(train, "FeatureExtractorId", "onnx_embedding", "string");
        Add(train, "EmbeddingModelPath", modelPath, "file");
        Add(train, "EmbeddingManifestPath", manifestPath, "file");
        using var normalA = new ImageWrapper(new Mat(32, 32, MatType.CV_8UC3, new Scalar(90, 90, 90)));
        using var normalB = new ImageWrapper(new Mat(32, 32, MatType.CV_8UC3, new Scalar(92, 92, 92)));
        var trainResult = await sut.ExecuteAsync(train, new Dictionary<string, object> { ["NormalImages"] = new[] { normalA, normalB }, ["Image"] = normalA });
        trainResult.IsSuccess.Should().BeTrue(trainResult.ErrorMessage);

        var mismatchedManifest = File.ReadAllText(manifestPath).Replace("\"mean\": [0.0, 0.0, 0.0]", "\"mean\": [0.1, 0.0, 0.0]", StringComparison.Ordinal);
        File.WriteAllText(mismatchedManifestPath, mismatchedManifest);
        var inference = CreateAnomalyOperator("inference", featureBankPath);
        Add(inference, "FeatureBankPath", featureBankPath, "file");
        Add(inference, "EmbeddingManifestPath", mismatchedManifestPath, "file");
        using var sample = new ImageWrapper(new Mat(32, 32, MatType.CV_8UC3, new Scalar(90, 90, 90)));

        var result = await sut.ExecuteAsync(inference, TestHelpers.CreateImageInputs(sample));

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("identity mismatch");
        DisposeImageOutputs(trainResult.OutputData);
        Directory.Delete(tempDirectory.FullName, recursive: true);
    }

    private static Operator CreateAnomalyOperator(string mode, string featureBankPath)
    {
        var op = new Operator($"anomaly-{mode}", OperatorType.AnomalyDetection, 0, 0);
        Add(op, "Mode", mode, "string");
        Add(op, "PatchSize", 16, "int");
        Add(op, "PatchStride", 16, "int");
        Add(op, "CoresetRatio", 1.0, "double");
        Add(op, "Threshold", 0.15, "double");
        return op;
    }

    private static void Add(Operator op, string name, object value, string type) =>
        op.AddParameter(new Parameter(Guid.NewGuid(), name, name, string.Empty, type, value));

    private static double NormalizeLineAngle(double angle)
    {
        while (angle > 90) angle -= 180;
        while (angle < -90) angle += 180;
        return angle;
    }

    private static string ResolveRepoPath(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "AGENTS.md"))) directory = directory.Parent;
        return Path.Combine(directory?.FullName ?? throw new DirectoryNotFoundException(), relativePath.Replace('/', Path.DirectorySeparatorChar));
    }

    private static void DisposeImageOutputs(Dictionary<string, object>? outputData)
    {
        if (outputData == null) return;
        foreach (var image in outputData.Values.OfType<ImageWrapper>()) image.Dispose();
    }
}
