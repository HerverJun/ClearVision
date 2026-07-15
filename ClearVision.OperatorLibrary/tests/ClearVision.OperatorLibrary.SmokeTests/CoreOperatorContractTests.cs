using System.Text.Json;
using ClearVision.OperatorLibrary.Abstractions.Adapters;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Operators;
using ClearVision.Product.Core.ValueObjects;
using ClearVision.Product.Infrastructure.Operators;
using Microsoft.Extensions.Logging.Abstractions;
using OpenCvSharp;
using ImageContractAdmission = ClearVision.Product.Core.Services.ImageContractAdmission;
using ImageContractStatus = ClearVision.Product.Core.Services.ImageContractStatus;
using ImageContractVerification = ClearVision.Product.Core.Services.ImageContractVerification;
using ImageInputContract = ClearVision.Product.Core.Services.ImageInputContract;
using OperatorImageContractResolver = ClearVision.Product.Core.Services.OperatorImageContractResolver;

namespace ClearVision.OperatorLibrary.SmokeTests;

[TestClassification(TestDomain.OperatorLibrary, TestPurpose.Smoke, TestLane.Pr, TestEvidenceType.PackageSmoke, TestOracleType.Contract, TestResourceRequirement.PackageFeed, TestExpectedDuration.Fast, TestFlakyPolicy.Blocking, "operator-library", Suites = "OperatorLibrarySmoke")]
public class CoreOperatorContractTests
{
    [Fact]
    public void ImageContractPresentation_PreservesExactPairsEvidenceAndLegacyApi()
    {
        var contract = new ThresholdImageContractProvider()
            .GetContracts(OperatorType.Thresholding, ["Image"], OperatorLifecycle.Stable)
            .Single();
        var presentation = contract.Presentation;
        var fixedAllowed = presentation.ExactVariantGroups
            .Where(group => group.Mode == "Fixed" && group.Admission == ImageContractAdmission.Allowed)
            .SelectMany(group => group.ExactInputTypes)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains("CV_64FC1", fixedAllowed);
        Assert.DoesNotContain("CV_64FC3", fixedAllowed);
        Assert.Contains(presentation.ExactVariantGroups, group =>
            group.Mode == "Fixed" &&
            group.Verification == ImageContractVerification.VerifiedRejection &&
            group.ExactInputTypes.Contains("CV_64FC3", StringComparer.Ordinal));

        var json = JsonSerializer.Serialize(contract);
        Assert.Contains("\"Admission\":\"Allowed\"", json, StringComparison.Ordinal);
        Assert.Contains("\"Verification\":\"VerifiedSupport\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Presentation\"", json, StringComparison.Ordinal);
        Assert.Contains("CV_64FC1", JsonSerializer.Serialize(contract.Presentation), StringComparison.Ordinal);

#pragma warning disable CS0618
        var legacy = new ImageInputContract(
            "Image",
            ["CV_8U"],
            [1],
            ["CV_8U"],
            "Legacy",
            "None",
            "Preserve",
            "8-bit",
            [],
            "NotApplicable",
            "IMAGE_DEPTH_UNSUPPORTED",
            OperatorImageContractResolver.ContractVersion,
            ImageContractStatus.Restricted,
            "E0_SOURCE_AUDIT");
        Assert.Equal(ImageContractStatus.Restricted, legacy.Status);
#pragma warning restore CS0618
        Assert.True(legacy.Presentation.CompatibilityOnly);
        Assert.False(legacy.Presentation.HasProductionSupport);
    }

    [Fact]
    public void OperatorExecutionOutputAdapter_ShouldPreserveShortCircuitContract()
    {
        var source = OperatorExecutionOutput.ShortCircuit(
            new Dictionary<string, object> { ["Reason"] = "EmptyFrame" },
            executionTimeMs: 12);

        var model = source.ToModel();

        Assert.True(model.IsSuccess);
        Assert.True(model.ShouldShortCircuitFlow);
        Assert.Equal(12, model.ExecutionTimeMs);
        Assert.Equal("EmptyFrame", model.OutputData?["Reason"]);
    }

    [Fact]
    public async Task MeanFilterOperator_WithKernelOne_PreservesImagePixelsAndContract()
    {
        using var source = new Mat(5, 5, MatType.CV_8UC1, Scalar.Black);
        source.Set(2, 2, 255);

        var op = CreateOperator(
            OperatorType.MeanFilter,
            ("KernelSize", 1),
            ("BorderType", 4));
        var executor = new MeanFilterOperator(NullLogger<MeanFilterOperator>.Instance);

        var result = await executor.ExecuteAsync(op, CreateImageInputs(source));

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.NotNull(result.OutputData);
        var output = result.OutputData!;
        using var outputImage = Assert.IsType<ImageWrapper>(output["Image"]);
        Assert.Equal(5, Assert.IsType<int>(output["Width"]));
        Assert.Equal(5, Assert.IsType<int>(output["Height"]));
        Assert.Equal(255, outputImage.GetMat().At<byte>(2, 2));
    }

    [Fact]
    public async Task MeanFilterOperator_WithEvenKernel_PreservesEvenBoxKernel()
    {
        using var source = new Mat(5, 5, MatType.CV_8UC1, Scalar.Black);
        source.Set(2, 2, 255);

        var op = CreateOperator(
            OperatorType.MeanFilter,
            ("KernelSize", 2),
            ("BorderType", 4));
        var executor = new MeanFilterOperator(NullLogger<MeanFilterOperator>.Instance);

        var result = await executor.ExecuteAsync(op, CreateImageInputs(source));

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.NotNull(result.OutputData);
        var output = result.OutputData!;
        using var outputImage = Assert.IsType<ImageWrapper>(output["Image"]);
        var center = outputImage.GetMat().At<byte>(2, 2);
        Assert.Equal(64, center);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(64)]
    public void MeanFilterOperator_ValidationRejectsKernelOutsideDeclaredRange(int kernelSize)
    {
        var op = CreateOperator(OperatorType.MeanFilter, ("KernelSize", kernelSize));
        var executor = new MeanFilterOperator(NullLogger<MeanFilterOperator>.Instance);

        var validation = executor.ValidateParameters(op);

        Assert.False(validation.IsValid);
        Assert.Contains(validation.Errors, error => error.Contains("KernelSize", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task MeanFilterOperator_WithoutImage_ReturnsFailure()
    {
        var op = CreateOperator(OperatorType.MeanFilter, ("KernelSize", 3));
        var executor = new MeanFilterOperator(NullLogger<MeanFilterOperator>.Instance);

        var result = await executor.ExecuteAsync(op, inputs: null);

        Assert.False(result.IsSuccess);
        Assert.Contains("input image", result.ErrorMessage ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CaliperToolOperator_HorizontalEdgePair_ReportsMeasurementContract()
    {
        using var source = new Mat(80, 200, MatType.CV_8UC1, Scalar.Black);
        Cv2.Rectangle(source, new Rect(70, 0, 60, 80), Scalar.White, -1);

        var op = CreateCaliperOperator(("Direction", "Horizontal"));
        var executor = new CaliperToolOperator(NullLogger<CaliperToolOperator>.Instance);

        var result = await executor.ExecuteAsync(op, CreateImageInputs(source));

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.NotNull(result.OutputData);
        var output = result.OutputData!;
        using var outputImage = Assert.IsType<ImageWrapper>(output["Image"]);
        Assert.Equal(200, outputImage.Width);
        Assert.Equal(80, Assert.IsType<int>(output["Height"]));
        Assert.Equal(1, Assert.IsType<int>(output["PairCount"]));
        Assert.InRange(Assert.IsType<double>(output["Width"]), 58.0, 62.0);
        Assert.NotEmpty(Assert.IsAssignableFrom<IReadOnlyCollection<Position>>(output["EdgePairs"]));
    }

    [Fact]
    public async Task CaliperToolOperator_VerticalEdgePair_WithDictionarySearchRegion_ReportsMeasurement()
    {
        using var source = new Mat(120, 120, MatType.CV_8UC1, Scalar.Black);
        Cv2.Rectangle(source, new Rect(0, 45, 120, 30), Scalar.White, -1);

        var op = CreateCaliperOperator(("Direction", "Vertical"));
        var inputs = CreateImageInputs(source);
        inputs["SearchRegion"] = new Dictionary<string, object>
        {
            ["X"] = -20,
            ["Y"] = -10,
            ["Width"] = 160,
            ["Height"] = 150
        };
        var executor = new CaliperToolOperator(NullLogger<CaliperToolOperator>.Instance);

        var result = await executor.ExecuteAsync(op, inputs);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.NotNull(result.OutputData);
        var output = result.OutputData!;
        using var outputImage = Assert.IsType<ImageWrapper>(output["Image"]);
        Assert.Equal(120, outputImage.Width);
        Assert.Equal(120, outputImage.Height);
        Assert.Equal(1, Assert.IsType<int>(output["PairCount"]));
        Assert.InRange(Assert.IsType<double>(output["Width"]), 28.0, 32.0);
    }

    [Fact]
    public async Task CaliperToolOperator_WhenNoEdgesFound_ReturnsNoFeatureFailure()
    {
        using var source = new Mat(80, 120, MatType.CV_8UC1, Scalar.Black);
        var op = CreateCaliperOperator(("ExpectedCount", 1));
        var executor = new CaliperToolOperator(NullLogger<CaliperToolOperator>.Instance);

        var result = await executor.ExecuteAsync(op, CreateImageInputs(source));

        Assert.False(result.IsSuccess);
        Assert.Contains("NoFeature", result.ErrorMessage ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("Direction", "Diagonal")]
    [InlineData("Polarity", "BrightOnly")]
    public void CaliperToolOperator_ValidationRejectsUnsupportedEnumValues(string name, string value)
    {
        var op = CreateCaliperOperator((name, value));
        var executor = new CaliperToolOperator(NullLogger<CaliperToolOperator>.Instance);

        var validation = executor.ValidateParameters(op);

        Assert.False(validation.IsValid);
        Assert.Contains(validation.Errors, error => error.Contains(name, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task CameraCalibrationOperator_SingleImageBlankPreview_ReturnsDiagnosticsContract()
    {
        using var source = new Mat(96, 128, MatType.CV_8UC3, Scalar.Black);
        var op = CreateOperator(
            OperatorType.CameraCalibration,
            ("Mode", "SingleImage"),
            ("PatternType", "Chessboard"),
            ("BoardWidth", 9),
            ("BoardHeight", 6),
            ("SquareSize", 25.0));
        var executor = new CameraCalibrationOperator(NullLogger<CameraCalibrationOperator>.Instance);

        var result = await executor.ExecuteAsync(op, CreateImageInputs(source));

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.NotNull(result.OutputData);
        var output = result.OutputData!;
        using var outputImage = Assert.IsType<ImageWrapper>(output["Image"]);
        Assert.Equal(128, outputImage.Width);
        Assert.Equal(96, outputImage.Height);
        Assert.False(Assert.IsType<bool>(output["Found"]));
        var message = Assert.IsType<string>(output["Message"]);
        Assert.Contains("preview", message, StringComparison.OrdinalIgnoreCase);

        var calibrationData = Assert.IsType<string>(output["CalibrationData"]);
        if (!string.IsNullOrWhiteSpace(calibrationData))
        {
            using var json = JsonDocument.Parse(calibrationData);
            Assert.True(json.RootElement.ValueKind == JsonValueKind.Object);
        }
    }

    [Fact]
    public async Task CameraCalibrationOperator_SingleImageWithoutInput_ReturnsFailure()
    {
        var op = CreateOperator(OperatorType.CameraCalibration, ("Mode", "SingleImage"));
        var executor = new CameraCalibrationOperator(NullLogger<CameraCalibrationOperator>.Instance);

        var result = await executor.ExecuteAsync(op, inputs: null);

        Assert.False(result.IsSuccess);
        Assert.Contains("Input image", result.ErrorMessage ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CameraCalibrationOperator_FolderCalibrationWithEmptyFolder_ReturnsFailure()
    {
        var folder = Path.Combine(Path.GetTempPath(), "clearvision-oplib-empty-calibration-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);

        try
        {
            var op = CreateOperator(
                OperatorType.CameraCalibration,
                ("Mode", "FolderCalibration"),
                ("ImageFolder", folder));
            var executor = new CameraCalibrationOperator(NullLogger<CameraCalibrationOperator>.Instance);

            var result = await executor.ExecuteAsync(op);

            Assert.False(result.IsSuccess);
            Assert.Contains("No calibration image", result.ErrorMessage ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    private static Dictionary<string, object> CreateImageInputs(Mat source)
    {
        return new Dictionary<string, object> { ["Image"] = new ImageWrapper(source.Clone()) };
    }

    private static Operator CreateCaliperOperator(params (string Name, object? Value)[] overrides)
    {
        var parameters = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["Direction"] = "Horizontal",
            ["Polarity"] = "Both",
            ["EdgeThreshold"] = 8.0,
            ["ExpectedCount"] = 1,
            ["MeasureMode"] = "edge_pairs",
            ["PairDirection"] = "any",
            ["SubpixelAccuracy"] = false,
            ["SubPixelMode"] = "gradient_centroid"
        };

        foreach (var (name, value) in overrides)
        {
            parameters[name] = value;
        }

        return CreateOperator(
            OperatorType.CaliperTool,
            parameters.Select(kvp => (kvp.Key, kvp.Value)).ToArray());
    }

    private static Operator CreateOperator(OperatorType operatorType, params (string Name, object? Value)[] parameters)
    {
        var op = new Operator($"{operatorType}-contract", operatorType, 0, 0);
        foreach (var (name, value) in parameters)
        {
            op.AddParameter(new Parameter(
                id: Guid.NewGuid(),
                name: name,
                displayName: name,
                description: $"Contract parameter {name}",
                dataType: "object",
                defaultValue: value,
                isRequired: false));
        }

        return op;
    }
}
