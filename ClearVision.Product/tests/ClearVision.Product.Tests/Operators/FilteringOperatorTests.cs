using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.ValueObjects;
using ClearVision.Product.Infrastructure.Operators;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using OpenCvSharp;

namespace ClearVision.Product.Tests.Operators;

public class FilteringOperatorTests
{
    private readonly GaussianBlurOperator _operator =
        new(Substitute.For<ILogger<GaussianBlurOperator>>());

    [Fact]
    public void OperatorType_ShouldMapToFiltering()
    {
        _operator.OperatorType.Should().Be(OperatorType.Filtering);
    }

    [Fact]
    public async Task ExecuteAsync_WithValidImage_ShouldReturnBlurredImage()
    {
        var op = CreateOperator();
        op.AddParameter(TestHelpers.CreateParameter("KernelSize", 5, "int"));
        op.AddParameter(TestHelpers.CreateParameter("SigmaX", 1.2, "double"));

        using var image = TestHelpers.CreateShapeTestImage();
        var result = await _operator.ExecuteAsync(op, TestHelpers.CreateImageInputs(image));

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        using var outputImage = result.OutputData!["Image"].Should().BeOfType<ImageWrapper>().Subject;
        outputImage.Width.Should().Be(400);
        outputImage.Height.Should().Be(400);
        result.OutputData["Width"].Should().Be(400);
        result.OutputData["Height"].Should().Be(400);
    }

    [Fact]
    public void ValidateParameters_WithUpperBoundKernelSize_ShouldBeValid()
    {
        var op = CreateOperator();
        op.AddParameter(TestHelpers.CreateParameter("KernelSize", 31, "int"));

        _operator.ValidateParameters(op).IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_WithoutImage_ShouldReturnFailure()
    {
        var result = await _operator.ExecuteAsync(CreateOperator(), new Dictionary<string, object>());

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task ExecuteAsync_WithoutFilterMode_ShouldKeepHistoricalGaussianBehavior()
    {
        var op = CreateOperator();
        op.AddParameter(TestHelpers.CreateParameter("KernelSize", 5, "int"));
        op.AddParameter(TestHelpers.CreateParameter("SigmaX", 1.2, "double"));
        op.AddParameter(TestHelpers.CreateParameter("SigmaY", 0.0, "double"));
        op.AddParameter(TestHelpers.CreateParameter("BorderType", 4, "int"));
        using var image = TestHelpers.CreateShapeTestImage();
        using var expected = new Mat();
        Cv2.GaussianBlur(image.MatReadOnly, expected, new Size(5, 5), 1.2, 0.0, BorderTypes.Default);

        var result = await _operator.ExecuteAsync(op, TestHelpers.CreateImageInputs(image));

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        using var actual = result.OutputData!["Image"].Should().BeOfType<ImageWrapper>().Subject;
        Cv2.Norm(actual.MatReadOnly, expected, NormTypes.L1).Should().Be(0.0);
        result.OutputData["FilterMode"].Should().Be("Gaussian");
    }

    [Theory]
    [InlineData("Mean")]
    [InlineData("Median")]
    [InlineData("Bilateral")]
    public async Task ExecuteAsync_ExpandedMode_ShouldMatchProfessionalOperator(string mode)
    {
        var unifiedOperator = CreateOperator();
        unifiedOperator.AddParameter(TestHelpers.CreateParameter("FilterMode", mode, "enum"));
        unifiedOperator.AddParameter(TestHelpers.CreateParameter("KernelSize", 5, "int"));
        unifiedOperator.AddParameter(TestHelpers.CreateParameter("BorderType", 4, "int"));
        unifiedOperator.AddParameter(TestHelpers.CreateParameter("Diameter", 9, "int"));
        unifiedOperator.AddParameter(TestHelpers.CreateParameter("SigmaColor", 75.0, "double"));
        unifiedOperator.AddParameter(TestHelpers.CreateParameter("SigmaSpace", 75.0, "double"));

        var professionalOperator = new Operator(mode, mode switch
        {
            "Mean" => OperatorType.MeanFilter,
            "Median" => OperatorType.MedianBlur,
            _ => OperatorType.BilateralFilter
        }, 0, 0);
        professionalOperator.AddParameter(TestHelpers.CreateParameter("KernelSize", 5, "int"));
        professionalOperator.AddParameter(TestHelpers.CreateParameter("BorderType", 4, "int"));
        professionalOperator.AddParameter(TestHelpers.CreateParameter("Diameter", 9, "int"));
        professionalOperator.AddParameter(TestHelpers.CreateParameter("SigmaColor", 75.0, "double"));
        professionalOperator.AddParameter(TestHelpers.CreateParameter("SigmaSpace", 75.0, "double"));

        OperatorBase professionalExecutor = mode switch
        {
            "Mean" => new MeanFilterOperator(Substitute.For<ILogger<MeanFilterOperator>>()),
            "Median" => new MedianBlurOperator(Substitute.For<ILogger<MedianBlurOperator>>()),
            _ => new BilateralFilterOperator(Substitute.For<ILogger<BilateralFilterOperator>>())
        };
        using var unifiedImage = TestHelpers.CreateShapeTestImage();
        using var professionalImage = TestHelpers.CreateShapeTestImage();

        var unifiedResult = await _operator.ExecuteAsync(
            unifiedOperator,
            TestHelpers.CreateImageInputs(unifiedImage));
        var professionalResult = await professionalExecutor.ExecuteAsync(
            professionalOperator,
            TestHelpers.CreateImageInputs(professionalImage));

        unifiedResult.IsSuccess.Should().BeTrue(unifiedResult.ErrorMessage);
        professionalResult.IsSuccess.Should().BeTrue(professionalResult.ErrorMessage);
        using var unifiedOutput = unifiedResult.OutputData!["Image"].Should().BeOfType<ImageWrapper>().Subject;
        using var professionalOutput = professionalResult.OutputData!["Image"].Should().BeOfType<ImageWrapper>().Subject;
        Cv2.Norm(unifiedOutput.MatReadOnly, professionalOutput.MatReadOnly, NormTypes.L1).Should().Be(0.0);
        unifiedResult.OutputData["FilterMode"].Should().Be(mode);
        unifiedResult.OutputData["FilterDiagnostics"].Should().BeAssignableTo<IDictionary<string, object>>();
    }

    [Fact]
    public async Task ExecuteAsync_InvalidFilterMode_ShouldFailWithoutFallback()
    {
        var op = CreateOperator();
        op.AddParameter(TestHelpers.CreateParameter("FilterMode", "Frequency", "enum"));
        using var image = TestHelpers.CreateShapeTestImage();

        var result = await _operator.ExecuteAsync(op, TestHelpers.CreateImageInputs(image));

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("FilterMode");
        _operator.ValidateParameters(op).IsValid.Should().BeFalse();
    }

    private static Operator CreateOperator()
    {
        return new Operator("Filtering", OperatorType.Filtering, 0, 0);
    }
}
