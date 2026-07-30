using ClearVision.Product.Core.Cameras;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Streaming;
using ClearVision.Product.Core.ValueObjects;
using ClearVision.Product.Infrastructure.Operators;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using OpenCvSharp;

namespace ClearVision.Product.Tests.Operators;

[TestClassification(TestDomain.Core, TestPurpose.Regression, TestLane.Pr, TestEvidenceType.Contract, TestOracleType.Contract, TestResourceRequirement.None, TestExpectedDuration.Fast, TestFlakyPolicy.Blocking, "product")]
public class ImageAcquisitionOperatorTests
{
    private readonly ImageAcquisitionOperator _operator;

    public ImageAcquisitionOperatorTests()
    {
        _operator = new ImageAcquisitionOperator(
            Substitute.For<ILogger<ImageAcquisitionOperator>>(),
            Substitute.For<ICameraManager>());
    }

    [Fact]
    public void OperatorType_ShouldBeImageAcquisition()
    {
        _operator.OperatorType.Should().Be(OperatorType.ImageAcquisition);
    }

    [Fact]
    public async Task ExecuteAsync_WithNullInputs_ShouldReturnFailure()
    {
        var op = CreateTestOperator();

        var result = await _operator.ExecuteAsync(op, null);

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task ExecuteAsync_WithEmptyInputs_ShouldReturnFailure()
    {
        var op = CreateTestOperator();
        var inputs = new Dictionary<string, object>();

        var result = await _operator.ExecuteAsync(op, inputs);

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task ExecuteAsync_WithUnicodeFilePath_ShouldLoadImage()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"ClearVision-图像采集-{Guid.NewGuid():N}");
        var filePath = Path.Combine(directory, "菠萝苑 LOGO 样张.png");
        Directory.CreateDirectory(directory);

        try
        {
            using var source = new Mat(9, 15, MatType.CV_8UC3, new Scalar(10, 120, 240));
            File.WriteAllBytes(filePath, source.ToBytes(".png"));

            var op = CreateTestOperator();
            op.AddParameter(new Parameter(Guid.NewGuid(), "SourceType", "SourceType", string.Empty, "enum", "File"));
            op.AddParameter(new Parameter(Guid.NewGuid(), "FilePath", "FilePath", string.Empty, "file", filePath));

            var result = await _operator.ExecuteAsync(op, new Dictionary<string, object>());

            result.IsSuccess.Should().BeTrue(result.ErrorMessage);
            result.OutputData.Should().NotBeNull();
            using var image = result.OutputData!["Image"].Should().BeOfType<ImageWrapper>().Subject;
            result.OutputData["Width"].Should().Be(15);
            result.OutputData["Height"].Should().Be(9);
            result.OutputData["Channels"].Should().Be(3);
            result.OutputData["Source"].Should().Be("file");
            result.OutputData["FilePath"].Should().Be(filePath);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task ExecuteAsync_WithLocalizedFileSourceType_ShouldLoadImage()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"ClearVision-采集源-{Guid.NewGuid():N}");
        var filePath = Path.Combine(directory, "本地图像.png");
        Directory.CreateDirectory(directory);

        try
        {
            using var source = new Mat(6, 10, MatType.CV_8UC3, new Scalar(40, 90, 180));
            File.WriteAllBytes(filePath, source.ToBytes(".png"));

            var op = CreateTestOperator();
            op.AddParameter(new Parameter(Guid.NewGuid(), "SourceType", "SourceType", string.Empty, "enum", "文件"));
            op.AddParameter(new Parameter(Guid.NewGuid(), "FilePath", "FilePath", string.Empty, "file", filePath));

            var result = await _operator.ExecuteAsync(op, new Dictionary<string, object>());

            result.IsSuccess.Should().BeTrue(result.ErrorMessage);
            result.OutputData.Should().NotBeNull();
            using var image = result.OutputData!["Image"].Should().BeOfType<ImageWrapper>().Subject;
            result.OutputData!["Width"].Should().Be(10);
            result.OutputData["Height"].Should().Be(6);
            result.OutputData["Channels"].Should().Be(3);
            result.OutputData["Source"].Should().Be("file");
            result.OutputData["FilePath"].Should().Be(filePath);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task ExecuteAsync_WithAsciiFilePath_ShouldLoadImageAndReleaseOutput()
    {
        var filePath = Path.Combine(Path.GetTempPath(), $"clearvision-acquisition-{Guid.NewGuid():N}.png");

        try
        {
            using var source = new Mat(8, 12, MatType.CV_8UC3, new Scalar(20, 80, 160));
            File.WriteAllBytes(filePath, source.ToBytes(".png"));

            var op = CreateTestOperator();
            op.AddParameter(new Parameter(Guid.NewGuid(), "SourceType", "SourceType", string.Empty, "enum", "File"));
            op.AddParameter(new Parameter(Guid.NewGuid(), "FilePath", "FilePath", string.Empty, "file", filePath));

            var result = await _operator.ExecuteAsync(op, new Dictionary<string, object>());

            result.IsSuccess.Should().BeTrue(result.ErrorMessage);
            result.OutputData.Should().NotBeNull();
            var image = result.OutputData!["Image"].Should().BeOfType<ImageWrapper>().Subject;
            result.OutputData["Width"].Should().Be(12);
            result.OutputData["Height"].Should().Be(8);
            result.OutputData["Channels"].Should().Be(3);
            result.OutputData["Source"].Should().Be("file");
            result.OutputData["FilePath"].Should().Be(filePath);

            image.Dispose();
            Action readDisposedImage = () => _ = image.Width;
            readDisposedImage.Should().Throw<ObjectDisposedException>();
        }
        finally
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
    }

    [Fact]
    public async Task ExecuteAsync_WithUndecodableFile_ShouldReturnControlledFailure()
    {
        var filePath = Path.Combine(Path.GetTempPath(), $"clearvision-invalid-image-{Guid.NewGuid():N}.png");

        try
        {
            await File.WriteAllTextAsync(filePath, "not an encoded image");
            var op = CreateTestOperator();
            op.AddParameter(new Parameter(Guid.NewGuid(), "SourceType", "SourceType", string.Empty, "enum", "File"));
            op.AddParameter(new Parameter(Guid.NewGuid(), "FilePath", "FilePath", string.Empty, "file", filePath));

            var result = await _operator.ExecuteAsync(op, new Dictionary<string, object>());

            result.IsSuccess.Should().BeFalse();
            result.ErrorMessage.Should().Be("无法加载图像文件，格式可能不受支持");
            result.OutputData.Should().BeNull();
        }
        finally
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
    }

    [Fact]
    public async Task ExecuteAsync_WithRuntimeImageAndNoFilePath_ShouldUseProvidedImage()
    {
        using var mat = new Mat(7, 13, MatType.CV_8UC3, new Scalar(10, 20, 30));
        var op = CreateTestOperator();
        op.AddParameter(new Parameter(Guid.NewGuid(), "SourceType", "SourceType", string.Empty, "enum", "File"));

        var result = await _operator.ExecuteAsync(op, new Dictionary<string, object>
        {
            ["Image"] = mat.ToBytes(".png")
        });

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.OutputData.Should().NotBeNull();
        result.OutputData!["Width"].Should().Be(13);
        result.OutputData["Height"].Should().Be(7);
        result.OutputData["Source"].Should().Be("provided-image");
    }

    [Fact]
    public async Task ExecuteAsync_WithFileSourceAndNoFilePathOrRuntimeImage_ShouldExplainMissingRuntimeImage()
    {
        var op = CreateTestOperator();
        op.AddParameter(new Parameter(Guid.NewGuid(), "SourceType", "SourceType", string.Empty, "enum", "File"));

        var result = await _operator.ExecuteAsync(op, new Dictionary<string, object>());

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Be("FilePath is required when SourceType is File and no runtime Image input was provided.");
    }

    [Fact]
    public async Task ExecuteAsync_WithRuntimeImageAndExplicitMissingFilePath_ShouldNotOverrideFilePath()
    {
        using var mat = new Mat(7, 13, MatType.CV_8UC3, new Scalar(10, 20, 30));
        var missingPath = Path.Combine(Path.GetTempPath(), $"missing-image-{Guid.NewGuid():N}.png");
        var op = CreateTestOperator();
        op.AddParameter(new Parameter(Guid.NewGuid(), "SourceType", "SourceType", string.Empty, "enum", "File"));
        op.AddParameter(new Parameter(Guid.NewGuid(), "FilePath", "FilePath", string.Empty, "file", missingPath));

        var result = await _operator.ExecuteAsync(op, new Dictionary<string, object>
        {
            ["Image"] = mat.ToBytes(".png")
        });

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain(missingPath);
        result.ErrorMessage.Should().NotContain("provided-image");
    }

    [Fact]
    public async Task ExecuteAsync_WithRuntimeImageAndInvalidSourceType_ShouldReturnFailure()
    {
        using var mat = new Mat(7, 13, MatType.CV_8UC3, new Scalar(10, 20, 30));
        var op = CreateTestOperator();
        op.AddParameter(new Parameter(Guid.NewGuid(), "SourceType", "SourceType", string.Empty, "enum", "Invalid"));

        var result = await _operator.ExecuteAsync(op, new Dictionary<string, object>
        {
            ["Image"] = mat.ToBytes(".png")
        });

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Be("SourceType must be File or Camera.");
    }

    [Fact]
    public async Task ExecuteAsync_WithCameraSource_ShouldIgnoreFilePathAndAcquireCameraFrame()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"cv-image-{Guid.NewGuid():N}.png");
        using var mat = new Mat(8, 8, MatType.CV_8UC3, new Scalar(10, 20, 30));
        Cv2.ImWrite(tempFile, mat);
        using var cameraMat = new Mat(6, 12, MatType.CV_8UC3, new Scalar(80, 90, 100));
        var cameraFrame = cameraMat.ToBytes(".png");

        var camera = Substitute.For<ICamera>();
        camera.SetExposureTimeAsync(Arg.Any<double>()).Returns(Task.CompletedTask);
        camera.SetGainAsync(Arg.Any<double>()).Returns(Task.CompletedTask);
        camera.AcquireSingleFrameAsync().Returns(Task.FromResult(cameraFrame));

        var cameraManager = Substitute.For<ICameraManager>();
        cameraManager.GetBindings().Returns(new List<CameraBindingConfig>());
        cameraManager.GetOrCreateByBindingAsync("cam-1").Returns(Task.FromResult(camera));
        var sut = new ImageAcquisitionOperator(
            Substitute.For<ILogger<ImageAcquisitionOperator>>(),
            cameraManager);

        try
        {
            var op = CreateTestOperator();
            op.AddParameter(new Parameter(Guid.NewGuid(), "SourceType", "SourceType", string.Empty, "enum", "Camera"));
            op.AddParameter(new Parameter(Guid.NewGuid(), "FilePath", "FilePath", string.Empty, "file", tempFile));
            op.AddParameter(new Parameter(Guid.NewGuid(), "CameraId", "CameraId", string.Empty, "cameraBinding", "cam-1"));

            var result = await sut.ExecuteAsync(op, new Dictionary<string, object>());

            result.IsSuccess.Should().BeTrue(result.ErrorMessage);
            result.OutputData.Should().NotBeNull();
            var outputData = result.OutputData!;
            outputData.Should().ContainKey("Image");
            outputData["Width"].Should().Be(12);
            outputData["Height"].Should().Be(6);
            outputData["Source"].Should().Be("camera");
            await cameraManager.Received(1).GetOrCreateByBindingAsync("cam-1");
            await camera.Received(1).AcquireSingleFrameAsync();
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    [Fact]
    public async Task ExecuteAsync_WithCameraBindingAlias_ShouldReachRealCameraExecution()
    {
        using var cameraMat = new Mat(6, 12, MatType.CV_8UC3, new Scalar(80, 90, 100));
        var camera = Substitute.For<ICamera>();
        camera.SetExposureTimeAsync(Arg.Any<double>()).Returns(Task.CompletedTask);
        camera.SetGainAsync(Arg.Any<double>()).Returns(Task.CompletedTask);
        camera.AcquireSingleFrameAsync().Returns(Task.FromResult(cameraMat.ToBytes(".png")));
        var cameraManager = Substitute.For<ICameraManager>();
        cameraManager.GetBindings().Returns([]);
        cameraManager.GetOrCreateByBindingAsync("alias-camera").Returns(Task.FromResult(camera));
        var sut = new ImageAcquisitionOperator(
            Substitute.For<ILogger<ImageAcquisitionOperator>>(),
            cameraManager);
        var op = CreateTestOperator();
        op.AddParameter(new Parameter(
            Guid.NewGuid(),
            "SourceType",
            "SourceType",
            string.Empty,
            "enum",
            "Camera"));
        op.AddParameter(new Parameter(
            Guid.NewGuid(),
            "CameraBindingId",
            "CameraBindingId",
            string.Empty,
            "string",
            "alias-camera"));

        var result = await sut.ExecuteAsync(op, new Dictionary<string, object>());

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        await cameraManager.Received(1).GetOrCreateByBindingAsync("alias-camera");
        (result.OutputData!["Image"] as ImageWrapper)?.Release();
    }

    [Fact]
    public async Task ExecuteAsync_WithCameraSourceAndRuntimeImage_ShouldUseProvidedImage()
    {
        using var runtimeMat = new Mat(4, 9, MatType.CV_8UC3, new Scalar(10, 20, 30));
        var runtimeInput = runtimeMat.ToBytes(".png");
        using var cameraMat = new Mat(6, 12, MatType.CV_8UC3, new Scalar(80, 90, 100));
        var cameraFrame = cameraMat.ToBytes(".png");

        var camera = Substitute.For<ICamera>();
        camera.SetExposureTimeAsync(Arg.Any<double>()).Returns(Task.CompletedTask);
        camera.SetGainAsync(Arg.Any<double>()).Returns(Task.CompletedTask);
        camera.AcquireSingleFrameAsync().Returns(Task.FromResult(cameraFrame));

        var cameraManager = Substitute.For<ICameraManager>();
        cameraManager.GetBindings().Returns(new List<CameraBindingConfig>());
        cameraManager.GetOrCreateByBindingAsync("cam-1").Returns(Task.FromResult(camera));
        var sut = new ImageAcquisitionOperator(
            Substitute.For<ILogger<ImageAcquisitionOperator>>(),
            cameraManager);

        var op = CreateTestOperator();
        op.AddParameter(new Parameter(Guid.NewGuid(), "SourceType", "SourceType", string.Empty, "enum", "Camera"));
        op.AddParameter(new Parameter(Guid.NewGuid(), "CameraId", "CameraId", string.Empty, "cameraBinding", "cam-1"));

        var result = await sut.ExecuteAsync(op, new Dictionary<string, object>
        {
            ["Image"] = runtimeInput
        });

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.OutputData.Should().NotBeNull();
        result.OutputData!["Width"].Should().Be(9);
        result.OutputData["Height"].Should().Be(4);
        result.OutputData["Source"].Should().Be("provided-image");
        await cameraManager.DidNotReceive().GetOrCreateByBindingAsync("cam-1");
        await camera.DidNotReceive().AcquireSingleFrameAsync();
    }

    [Fact]
    public async Task ExecuteAsync_WithProvidedFrameEnvelope_ShouldUseInjectedFrame()
    {
        using var mat = new Mat(7, 11, MatType.CV_8UC3, new Scalar(30, 60, 90));
        var envelope = new FrameEnvelope(
            "cam-envelope",
            42,
            DateTimeOffset.UtcNow,
            11,
            7,
            "image/png",
            FramePayloadKind.EncodedImage,
            mat.ToBytes(".png"),
            TimestampSource: FrameTimestampSource.HostFallback,
            CorrelationId: "corr-42");

        var cameraManager = Substitute.For<ICameraManager>();
        var sut = new ImageAcquisitionOperator(
            Substitute.For<ILogger<ImageAcquisitionOperator>>(),
            cameraManager);

        var op = CreateTestOperator();
        op.AddParameter(new Parameter(Guid.NewGuid(), "SourceType", "SourceType", string.Empty, "enum", "Camera"));
        op.AddParameter(new Parameter(Guid.NewGuid(), "CameraId", "CameraId", string.Empty, "cameraBinding", "cam-1"));

        var result = await sut.ExecuteAsync(op, new Dictionary<string, object>
        {
            ["ProvidedFrameEnvelope"] = envelope
        });

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.OutputData.Should().NotBeNull();
        result.OutputData!["Width"].Should().Be(11);
        result.OutputData["Height"].Should().Be(7);
        result.OutputData["Source"].Should().Be("provided-frame-envelope");
        result.OutputData["CameraId"].Should().Be("cam-envelope");
        result.OutputData["Sequence"].Should().Be(42L);
        result.OutputData["CorrelationId"].Should().Be("corr-42");
        await cameraManager.DidNotReceive().GetOrCreateByBindingAsync(Arg.Any<string>());
    }

    [Fact]
    public async Task ExecuteAsync_WithFrameDrivenCameraBinding_ShouldUseSharedStreamWithoutOpeningCameraDirectly()
    {
        using var frameMat = new Mat(5, 7, MatType.CV_8UC3, new Scalar(20, 40, 60));
        var cameraManager = Substitute.For<ICameraManager>();
        cameraManager.GetBindings().Returns(new List<CameraBindingConfig>
        {
            new()
            {
                Id = "cam-shared",
                SerialNumber = "SN-SHARED",
                TriggerMode = "External"
            }
        });

        var streamCoordinator = Substitute.For<ICameraFrameStreamCoordinator>();
        streamCoordinator.AcquireFrameAsync("cam-shared", Arg.Any<CancellationToken>())
            .Returns(new CameraStreamFrame("cam-shared", frameMat.ToBytes(".png"), "image/png", 7, 5, 1, DateTime.UtcNow));

        var sut = new ImageAcquisitionOperator(
            Substitute.For<ILogger<ImageAcquisitionOperator>>(),
            cameraManager,
            streamCoordinator);

        var op = CreateTestOperator();
        op.AddParameter(new Parameter(Guid.NewGuid(), "SourceType", "SourceType", string.Empty, "enum", "Camera"));
        op.AddParameter(new Parameter(Guid.NewGuid(), "CameraId", "CameraId", string.Empty, "cameraBinding", "cam-shared"));

        var result = await sut.ExecuteAsync(op, new Dictionary<string, object>());

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.OutputData.Should().NotBeNull();
        result.OutputData!["Width"].Should().Be(7);
        result.OutputData["Height"].Should().Be(5);
        result.OutputData["Source"].Should().Be("external");
        await streamCoordinator.Received(1).AcquireFrameAsync("cam-shared", Arg.Any<CancellationToken>());
        await cameraManager.DidNotReceive().GetOrCreateByBindingAsync(Arg.Any<string>());
    }

    [Fact]
    public void ValidateParameters_WithValidOperator_ShouldReturnValid()
    {
        var op = CreateTestOperator();

        var result = _operator.ValidateParameters(op);

        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void ValidateParameters_WithLocalizedCameraSourceAndMissingCamera_ShouldRequireCameraId()
    {
        var op = CreateTestOperator();
        op.AddParameter(new Parameter(Guid.NewGuid(), "SourceType", "SourceType", string.Empty, "enum", "相机"));

        var result = _operator.ValidateParameters(op);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain("CameraId is required when SourceType is Camera.");
    }

    private static Operator CreateTestOperator()
    {
        return new Operator("TestOperator", OperatorType.ImageAcquisition, 0, 0);
    }
}

[TestClassification(TestDomain.Core, TestPurpose.Regression, TestLane.Pr, TestEvidenceType.Contract, TestOracleType.Contract, TestResourceRequirement.None, TestExpectedDuration.Fast, TestFlakyPolicy.Blocking, "product")]
public class GaussianBlurOperatorTests
{
    private readonly GaussianBlurOperator _operator;

    public GaussianBlurOperatorTests()
    {
        _operator = new GaussianBlurOperator(Substitute.For<ILogger<GaussianBlurOperator>>());
    }

    [Fact]
    public void OperatorType_ShouldBeFiltering()
    {
        _operator.OperatorType.Should().Be(OperatorType.Filtering);
    }

    [Fact]
    public async Task ExecuteAsync_WithoutImageInput_ShouldReturnFailure()
    {
        var op = CreateTestOperator();
        var inputs = new Dictionary<string, object>();

        var result = await _operator.ExecuteAsync(op, inputs);

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void ValidateParameters_WithValidKernelSize_ShouldReturnValid()
    {
        var op = CreateTestOperator();
        op.AddParameter(TestHelpers.CreateParameter(
            "KernelSize", "KernelSize", "int", 5, 1, 31, true));

        var result = _operator.ValidateParameters(op);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void ValidateParameters_WithInvalidKernelSize_ShouldReturnInvalid()
    {
        var op = CreateTestOperator();
        op.AddParameter(TestHelpers.CreateParameter(
            "KernelSize", "KernelSize", "int", 50, 1, 31, true));

        var result = _operator.ValidateParameters(op);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain("核大小必须在 1-31 之间");
    }

    private static Operator CreateTestOperator()
    {
        return new Operator("GaussianBlur", OperatorType.Filtering, 0, 0);
    }
}

[TestClassification(TestDomain.Core, TestPurpose.Regression, TestLane.Pr, TestEvidenceType.Contract, TestOracleType.Contract, TestResourceRequirement.None, TestExpectedDuration.Fast, TestFlakyPolicy.Blocking, "product")]
public class CannyEdgeOperatorTests
{
    private readonly CannyEdgeOperator _operator;

    public CannyEdgeOperatorTests()
    {
        _operator = new CannyEdgeOperator(Substitute.For<ILogger<CannyEdgeOperator>>());
    }

    [Fact]
    public void OperatorType_ShouldBeEdgeDetection()
    {
        _operator.OperatorType.Should().Be(OperatorType.EdgeDetection);
    }

    [Fact]
    public void ValidateParameters_WithValidThresholds_ShouldReturnValid()
    {
        var op = CreateTestOperator();
        op.AddParameter(TestHelpers.CreateParameter(
            "Threshold1", "Threshold1", "double", 50.0, 0.0, 255.0, true));
        op.AddParameter(TestHelpers.CreateParameter(
            "Threshold2", "Threshold2", "double", 150.0, 0.0, 255.0, true));

        var result = _operator.ValidateParameters(op);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void ValidateParameters_WithInvalidThreshold1_ShouldReturnInvalid()
    {
        var op = CreateTestOperator();
        op.AddParameter(TestHelpers.CreateParameter(
            "Threshold1", "Threshold1", "double", 300.0, 0.0, 255.0, true));

        var result = _operator.ValidateParameters(op);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("Threshold1"));
    }

    [Fact]
    public void ValidateParameters_WithCannyAndEmptyModelSources_ShouldReturnValid()
    {
        var op = CreateTestOperator();
        op.AddParameter(TestHelpers.CreateParameter("Method", "Canny", "enum"));
        AddEdgeModelParameters(op);

        var result = _operator.ValidateParameters(op);

        result.IsValid.Should().BeTrue(result.Errors.FirstOrDefault());
    }

    [Fact]
    public void ValidateParameters_WithOnnxEdgeAndEmptyModelSources_ShouldReturnInvalid()
    {
        var op = CreateTestOperator();
        op.AddParameter(TestHelpers.CreateParameter("Method", "OnnxEdge", "enum"));
        AddEdgeModelParameters(op);

        var result = _operator.ValidateParameters(op);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain("OnnxEdge requires EdgeModelPath, EdgeModelId, or ModelCatalogPath.");
    }

    [Theory]
    [InlineData("EdgeModelPath", "C:\\drafts\\edge.onnx")]
    [InlineData("EdgeModelId", "edge-catalog-model")]
    [InlineData("ModelCatalogPath", "C:\\drafts\\model_catalog.json")]
    public void ValidateParameters_WithOnnxEdgeAndAnyModelSource_ShouldReturnValid(
        string parameterName,
        string parameterValue)
    {
        var op = CreateTestOperator();
        op.AddParameter(TestHelpers.CreateParameter("Method", "OnnxEdge", "enum"));
        AddEdgeModelParameters(op, parameterName, parameterValue);

        var result = _operator.ValidateParameters(op);

        result.IsValid.Should().BeTrue(result.Errors.FirstOrDefault());
    }

    [Fact]
    public void ValidateParameters_WithOnnxEdgeAndInvalidBinarizationThreshold_ShouldReturnInvalid()
    {
        var op = CreateTestOperator();
        op.AddParameter(TestHelpers.CreateParameter("Method", "OnnxEdge", "enum"));
        AddEdgeModelParameters(op, "EdgeModelId", "edge-catalog-model");
        op.AddParameter(TestHelpers.CreateParameter("EdgeBinarizationThreshold", 1.1, "double"));

        var result = _operator.ValidateParameters(op);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain("EdgeBinarizationThreshold must be between 0 and 1.");
    }

    [Fact]
    public async Task ExecuteAsync_WithValidImage_ShouldOutputEdgesAsBytes()
    {
        var op = CreateTestOperator();
        using var image = TestHelpers.CreateShapeTestImage();
        var inputs = TestHelpers.CreateImageInputs(image);

        var result = await _operator.ExecuteAsync(op, inputs);

        result.IsSuccess.Should().BeTrue();
        result.OutputData.Should().NotBeNull();
        result.OutputData.Should().ContainKey("Edges");
        result.OutputData!["Edges"].Should().BeOfType<byte[]>();
    }

    [Fact]
    public async Task ExecuteAsync_WithAutoThreshold_ShouldExposeThresholdsUsed()
    {
        var op = CreateTestOperator();
        op.AddParameter(TestHelpers.CreateParameter("AutoThreshold", true, "bool"));
        op.AddParameter(TestHelpers.CreateParameter("AutoThresholdSigma", 0.33, "double"));

        using var image = TestHelpers.CreateShapeTestImage();
        var inputs = TestHelpers.CreateImageInputs(image);

        var result = await _operator.ExecuteAsync(op, inputs);

        result.IsSuccess.Should().BeTrue();
        result.OutputData.Should().NotBeNull();
        result.OutputData.Should().ContainKey("Threshold1Used");
        result.OutputData.Should().ContainKey("Threshold2Used");
        result.OutputData!["Threshold1Used"].Should().BeOfType<double>();
        result.OutputData["Threshold2Used"].Should().BeOfType<double>();
    }

    private static Operator CreateTestOperator()
    {
        return new Operator("CannyEdge", OperatorType.EdgeDetection, 0, 0);
    }

    private static void AddEdgeModelParameters(
        Operator op,
        string nonEmptyParameterName = "",
        string nonEmptyValue = "")
    {
        foreach (var name in new[] { "EdgeModelPath", "EdgeModelId", "ModelCatalogPath" })
        {
            var value = string.Equals(name, nonEmptyParameterName, StringComparison.OrdinalIgnoreCase)
                ? nonEmptyValue
                : string.Empty;
            op.AddParameter(TestHelpers.CreateParameter(name, value, "string"));
        }
    }
}

[TestClassification(TestDomain.Core, TestPurpose.Regression, TestLane.Pr, TestEvidenceType.Contract, TestOracleType.Contract, TestResourceRequirement.None, TestExpectedDuration.Fast, TestFlakyPolicy.Blocking, "product")]
public class ThresholdOperatorTests
{
    private readonly ThresholdOperator _operator;

    public ThresholdOperatorTests()
    {
        _operator = new ThresholdOperator(Substitute.For<ILogger<ThresholdOperator>>());
    }

    [Fact]
    public void OperatorType_ShouldBeThresholding()
    {
        _operator.OperatorType.Should().Be(OperatorType.Thresholding);
    }

    [Fact]
    public void ValidateParameters_WithValidThreshold_ShouldReturnValid()
    {
        var op = CreateTestOperator();
        op.AddParameter(TestHelpers.CreateParameter(
            "Threshold", "Threshold", "double", 127.0, 0.0, 255.0, true));

        var result = _operator.ValidateParameters(op);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void ValidateParameters_WithFiniteNegativeThreshold_ShouldDeferToRuntimeDepthValidation()
    {
        var op = CreateTestOperator();
        op.AddParameter(TestHelpers.CreateParameter("Threshold", -10.0, "double"));

        var result = _operator.ValidateParameters(op);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void ValidateParameters_WithTriangleAndUseOtsu_ShouldReturnInvalid()
    {
        var op = CreateTestOperator();
        op.AddParameter(TestHelpers.CreateParameter("Type", (int)ThresholdTypes.Triangle, "int"));
        op.AddParameter(TestHelpers.CreateParameter("UseOtsu", true, "bool"));

        var result = _operator.ValidateParameters(op);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain("UseOtsu cannot be combined with Triangle threshold type.");
    }

    [Fact]
    public async Task ExecuteAsync_WithTriangleAndUseOtsu_ShouldReturnFailure()
    {
        var op = CreateTestOperator();
        op.AddParameter(TestHelpers.CreateParameter("Type", (int)ThresholdTypes.Triangle, "int"));
        op.AddParameter(TestHelpers.CreateParameter("UseOtsu", true, "bool"));

        using var image = CreateTwoToneGrayImage();
        var result = await _operator.ExecuteAsync(op, TestHelpers.CreateImageInputs(image));

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("UseOtsu");
    }

    [Fact]
    public async Task ExecuteAsync_WithBinaryInv_ShouldProduceSingleChannelMaskWithExpectedPolarity()
    {
        var op = CreateTestOperator();
        op.AddParameter(TestHelpers.CreateParameter("Threshold", 100.0, "double"));
        op.AddParameter(TestHelpers.CreateParameter("MaxValue", 255.0, "double"));
        op.AddParameter(TestHelpers.CreateParameter("Type", (int)ThresholdTypes.BinaryInv, "int"));

        using var image = CreateTwoToneGrayImage();
        var result = await _operator.ExecuteAsync(op, TestHelpers.CreateImageInputs(image));

        result.IsSuccess.Should().BeTrue();
        using var outputImage = result.OutputData!["Image"].Should().BeOfType<ImageWrapper>().Subject;
        outputImage.Channels.Should().Be(1);

        var output = outputImage.MatReadOnly;
        output.At<byte>(0, 0).Should().Be(255);
        output.At<byte>(0, 1).Should().Be(0);
        Convert.ToDouble(result.OutputData["ActualThreshold"]).Should().Be(100.0);
    }

    private static Operator CreateTestOperator()
    {
        return new Operator("Threshold", OperatorType.Thresholding, 0, 0);
    }

    private static ImageWrapper CreateTwoToneGrayImage()
    {
        var mat = new Mat(1, 2, MatType.CV_8UC1);
        mat.Set(0, 0, (byte)50);
        mat.Set(0, 1, (byte)200);
        return new ImageWrapper(mat);
    }
}
