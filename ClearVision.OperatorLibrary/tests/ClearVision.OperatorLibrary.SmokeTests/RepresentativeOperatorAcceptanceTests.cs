using System.Numerics;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.ValueObjects;
using ClearVision.Product.Infrastructure.Operators;
using Microsoft.Extensions.Logging.Abstractions;
using OpenCvSharp;

namespace ClearVision.OperatorLibrary.SmokeTests;

public class RepresentativeOperatorAcceptanceTests
{
    [Fact]
    public async Task MeanFilterOperator_ShouldHandleBoundaryKernelSizeAndReturnImage()
    {
        using var source = new Mat(64, 64, MatType.CV_8UC1, Scalar.Black);
        Cv2.Rectangle(source, new Rect(8, 8, 20, 20), Scalar.White, -1);

        using var inputImage = new ImageWrapper(source.Clone());
        var op = CreateOperator(
            OperatorType.MeanFilter,
            ("KernelSize", 64), // out of range and even; runtime should clamp then force odd
            ("BorderType", 4));
        var executor = new MeanFilterOperator(NullLogger<MeanFilterOperator>.Instance);

        var result = await executor.ExecuteAsync(op, new Dictionary<string, object> { ["Image"] = inputImage });

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.OutputData);
        Assert.Equal(64, Assert.IsType<int>(result.OutputData!["Width"]));
        Assert.Equal(64, Assert.IsType<int>(result.OutputData!["Height"]));
    }

    [Fact]
    public async Task CaliperToolOperator_ShouldCoverSuccessAndMissingInputFailurePaths()
    {
        using var source = new Mat(80, 200, MatType.CV_8UC1, Scalar.Black);
        Cv2.Rectangle(source, new Rect(70, 0, 60, 80), Scalar.White, -1);

        var executor = new CaliperToolOperator(NullLogger<CaliperToolOperator>.Instance);

        var successOperator = CreateOperator(
            OperatorType.CaliperTool,
            ("Direction", "Horizontal"),
            ("Polarity", "Both"),
            ("EdgeThreshold", 8.0),
            ("ExpectedCount", 1),
            ("MeasureMode", "edge_pairs"),
            ("PairDirection", "any"));

        using (var successInput = new ImageWrapper(source.Clone()))
        {
            var success = await executor.ExecuteAsync(successOperator, new Dictionary<string, object> { ["Image"] = successInput });
            Assert.True(success.IsSuccess);
            Assert.NotNull(success.OutputData);
            Assert.True(Assert.IsType<int>(success.OutputData!["PairCount"]) >= 1);
        }

        var failure = await executor.ExecuteAsync(successOperator, inputs: null);
        Assert.False(failure.IsSuccess);
        Assert.NotNull(failure.ErrorMessage);
    }

    [Fact]
    public void CameraCalibrationOperator_ShouldRejectInvalidModeInValidation()
    {
        var op = CreateOperator(
            OperatorType.CameraCalibration,
            ("BoardWidth", 9),
            ("BoardHeight", 6),
            ("SquareSize", 25.0),
            ("Mode", "BadMode"));
        var executor = new CameraCalibrationOperator(NullLogger<CameraCalibrationOperator>.Instance);

        var validation = executor.ValidateParameters(op);

        Assert.False(validation.IsValid);
        Assert.Contains("Mode", validation.Errors.FirstOrDefault() ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CameraCalibrationOperator_ShouldReturnFailureWhenFolderDoesNotExist()
    {
        var missingFolder = Path.Combine(Path.GetTempPath(), "clearvision-oplib-missing-folder-" + Guid.NewGuid().ToString("N"));
        var op = CreateOperator(
            OperatorType.CameraCalibration,
            ("Mode", "FolderCalibration"),
            ("ImageFolder", missingFolder));
        var executor = new CameraCalibrationOperator(NullLogger<CameraCalibrationOperator>.Instance);

        var result = await executor.ExecuteAsync(op);

        Assert.False(result.IsSuccess);
        Assert.Contains("ImageFolder", result.ErrorMessage ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ModbusCommunicationOperator_ShouldRejectOutOfRangePortInValidation()
    {
        var op = CreateOperator(
            OperatorType.ModbusCommunication,
            ("Protocol", "TCP"),
            ("Port", 0),
            ("SlaveId", 1),
            ("RegisterCount", 1));
        var executor = new ModbusCommunicationOperator(NullLogger<ModbusCommunicationOperator>.Instance);

        var validation = executor.ValidateParameters(op);

        Assert.False(validation.IsValid);
        Assert.NotEmpty(validation.Errors);
    }

    [Fact]
    public void ModbusCommunicationOperator_ShouldRejectUnsupportedProtocolInValidation()
    {
        var op = CreateOperator(
            OperatorType.ModbusCommunication,
            ("Protocol", "TcpRtuBridge"),
            ("SlaveId", 1),
            ("RegisterAddress", 0),
            ("RegisterCount", 1),
            ("FunctionCode", "ReadHolding"));
        var executor = new ModbusCommunicationOperator(NullLogger<ModbusCommunicationOperator>.Instance);

        var validation = executor.ValidateParameters(op);

        Assert.False(validation.IsValid);
        Assert.NotEmpty(validation.Errors);
    }

    [Fact]
    public async Task TryCatchOperator_ShouldPassInputThroughTryBranch()
    {
        var op = CreateOperator(
            OperatorType.TryCatch,
            ("EnableCatch", false),
            ("CatchOutputError", true),
            ("CatchOutputStackTrace", false));
        var executor = new TryCatchOperator(NullLogger<TryCatchOperator>.Instance);
        var payload = "package-acceptance";

        var result = await executor.ExecuteAsync(op, new Dictionary<string, object> { ["Input"] = payload });

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.OutputData);
        var outputValues = result.OutputData!.Values.ToList();
        Assert.Contains(payload, outputValues);
        if (result.OutputData.TryGetValue("HasError", out var hasError))
        {
            Assert.Equal(false, hasError);
        }
    }

    [Fact]
    public void DeepLearningOperator_ShouldRejectMissingModelPathInValidation()
    {
        var op = CreateOperator(
            OperatorType.DeepLearning,
            ("ModelPath", string.Empty),
            ("Confidence", 0.5));
        var executor = new DeepLearningOperator(NullLogger<DeepLearningOperator>.Instance);

        var validation = executor.ValidateParameters(op);

        Assert.False(validation.IsValid);
        Assert.NotEmpty(validation.Errors);
    }

    [Fact]
    public async Task DeepLearningOperator_ShouldFailWhenModelPathIsNotProvidedAtRuntime()
    {
        using var source = new Mat(64, 64, MatType.CV_8UC3, Scalar.Black);
        using var inputImage = new ImageWrapper(source.Clone());

        var op = CreateOperator(
            OperatorType.DeepLearning,
            ("ModelPath", string.Empty),
            ("Confidence", 0.4),
            ("InputSize", 640),
            ("ModelVersion", "Auto"));
        var executor = new DeepLearningOperator(NullLogger<DeepLearningOperator>.Instance);

        var result = await executor.ExecuteAsync(op, new Dictionary<string, object> { ["Image"] = inputImage });

        Assert.False(result.IsSuccess);
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public async Task TemplateMatchOperator_ShouldFindKnownTemplateAndExposeMatchMetadata()
    {
        using var template = new Mat(18, 18, MatType.CV_8UC1, Scalar.Black);
        Cv2.Rectangle(template, new Rect(3, 3, 12, 12), Scalar.White, -1);
        Cv2.Line(template, new Point(3, 3), new Point(14, 14), Scalar.Black, 2);

        using var source = new Mat(80, 80, MatType.CV_8UC1, Scalar.Black);
        template.CopyTo(new Mat(source, new Rect(31, 24, template.Width, template.Height)));

        using var inputImage = new ImageWrapper(source.Clone());
        using var templateImage = new ImageWrapper(template.Clone());
        var op = CreateOperator(
            OperatorType.TemplateMatching,
            ("Method", "CCorrNormed"),
            ("Domain", "Gray"),
            ("Threshold", 0.8),
            ("MaxMatches", 1));
        var executor = new TemplateMatchOperator(NullLogger<TemplateMatchOperator>.Instance);

        var result = await executor.ExecuteAsync(op, new Dictionary<string, object>
        {
            ["Image"] = inputImage,
            ["Template"] = templateImage
        });

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.NotNull(result.OutputData);
        Assert.True(Assert.IsType<bool>(result.OutputData!["IsMatch"]));
        Assert.True(Assert.IsType<int>(result.OutputData["MatchCount"]) >= 1);
        var position = Assert.IsType<Position>(result.OutputData["Position"]);
        Assert.InRange(position.X, 38.0, 42.0);
        Assert.InRange(position.Y, 31.0, 35.0);
    }

    [Fact]
    public async Task RegionUnionOperator_ShouldMergeOverlappingRunLengthRegions()
    {
        var first = new Region(new[]
        {
            new RunLength(2, 0, 2),
            new RunLength(3, 0, 0)
        });
        var second = new Region(new[]
        {
            new RunLength(2, 2, 4),
            new RunLength(4, 1, 1)
        });
        var op = CreateOperator(OperatorType.RegionUnion);
        var executor = new RegionUnionOperator(NullLogger<RegionUnionOperator>.Instance);

        var result = await executor.ExecuteAsync(op, new Dictionary<string, object>
        {
            ["Region1"] = first,
            ["Region2"] = second
        });

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.NotNull(result.OutputData);
        var union = Assert.IsType<Region>(result.OutputData!["Region"]);
        Assert.Equal(7, union.Area);
        Assert.Contains(union.RunLengths, run => run.Y == 2 && run.StartX == 0 && run.EndX == 4);
        Assert.Equal(7, Assert.IsType<int>(result.OutputData["Area"]));
    }

    [Fact]
    public async Task MorphologyOperator_ShouldExecuteLegacyImagePathAndRejectUnsupportedOperation()
    {
        using var source = new Mat(9, 9, MatType.CV_8UC1, Scalar.Black);
        source.Set(4, 4, 255);
        using var inputImage = new ImageWrapper(source.Clone());
        var validOp = CreateOperator(
            OperatorType.Morphology,
            ("Operation", "Dilate"),
            ("KernelSize", 3),
            ("KernelShape", "Rect"),
            ("Iterations", 1));
        var executor = new MorphologyOperator(NullLogger<MorphologyOperator>.Instance);

        var result = await executor.ExecuteAsync(validOp, new Dictionary<string, object> { ["Image"] = inputImage });

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.NotNull(result.OutputData);
        using var outputImage = Assert.IsType<ImageWrapper>(result.OutputData!["Image"]);
        Assert.True(Cv2.CountNonZero(outputImage.GetMat()) > 1);

        var invalidOp = CreateOperator(OperatorType.Morphology, ("Operation", "Unsupported"));
        var validation = executor.ValidateParameters(invalidOp);
        Assert.False(validation.IsValid);
        Assert.NotEmpty(validation.Errors);
    }

    [Fact]
    public async Task FFT1DOperator_ShouldTransformNumericSignal()
    {
        var op = CreateOperator(OperatorType.FFT1D);
        var executor = new FFT1DOperator(NullLogger<FFT1DOperator>.Instance);

        var result = await executor.ExecuteAsync(op, new Dictionary<string, object>
        {
            ["Input"] = new double[] { 1.0, 0.0, -1.0, 0.0 }
        });

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.NotNull(result.OutputData);
        var spectrum = Assert.IsType<Complex[]>(result.OutputData!["Spectrum"]);
        Assert.Equal(4, spectrum.Length);
        Assert.Equal("1DSignal", Assert.IsType<string>(result.OutputData["TransformKind"]));
        Assert.Equal(4, Assert.IsType<double[]>(result.OutputData["Magnitude"]).Length);
    }

    [Fact]
    public void SemanticSegmentationOperator_ShouldRejectMissingModelTargetInValidation()
    {
        var op = CreateOperator(OperatorType.SemanticSegmentation, ("ModelPath", string.Empty));
        var executor = new SemanticSegmentationOperator(NullLogger<SemanticSegmentationOperator>.Instance);

        var validation = executor.ValidateParameters(op);

        Assert.False(validation.IsValid);
        Assert.NotEmpty(validation.Errors);
    }

    [Fact]
    public void AnomalyDetectionOperator_ShouldRejectMissingFeatureBankForInference()
    {
        var missingBank = Path.Combine(Path.GetTempPath(), "clearvision-oplib-missing-bank-" + Guid.NewGuid().ToString("N") + ".json");
        var op = CreateOperator(
            OperatorType.AnomalyDetection,
            ("Mode", "inference"),
            ("FeatureBankPath", missingBank));
        var executor = new AnomalyDetectionOperator(NullLogger<AnomalyDetectionOperator>.Instance);

        var validation = executor.ValidateParameters(op);

        Assert.False(validation.IsValid);
        Assert.Contains("Feature bank", validation.Errors.FirstOrDefault() ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SurfaceDefectDetectionOperator_ShouldDetectGradientDefectAndReturnDiagnostics()
    {
        using var source = new Mat(96, 96, MatType.CV_8UC1, Scalar.Black);
        Cv2.Line(source, new Point(16, 48), new Point(80, 48), Scalar.White, 3);

        using var inputImage = new ImageWrapper(source.Clone());
        var op = CreateOperator(
            OperatorType.SurfaceDefectDetection,
            ("Method", "GradientMagnitude"),
            ("ThresholdMode", "Manual"),
            ("Threshold", 20.0),
            ("MinArea", 1),
            ("MaxArea", 10_000),
            ("MorphMode", "None"),
            ("NormalizationMode", "None"));
        var executor = new SurfaceDefectDetectionOperator(NullLogger<SurfaceDefectDetectionOperator>.Instance);

        var result = await executor.ExecuteAsync(op, new Dictionary<string, object> { ["Image"] = inputImage });

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.NotNull(result.OutputData);
        Assert.True(Assert.IsType<int>(result.OutputData!["DefectCount"]) >= 1);
        Assert.True(Assert.IsType<double>(result.OutputData["DefectArea"]) > 0.0);
        Assert.IsAssignableFrom<IDictionary<string, object>>(result.OutputData["Diagnostics"]);
    }

    private static Operator CreateOperator(OperatorType operatorType, params (string Name, object? Value)[] parameters)
    {
        var op = new Operator($"{operatorType}-acceptance", operatorType, 0, 0);
        foreach (var (name, value) in parameters)
        {
            op.AddParameter(new Parameter(
                id: Guid.NewGuid(),
                name: name,
                displayName: name,
                description: $"Acceptance parameter {name}",
                dataType: "object",
                defaultValue: value,
                isRequired: false));
        }

        return op;
    }
}
