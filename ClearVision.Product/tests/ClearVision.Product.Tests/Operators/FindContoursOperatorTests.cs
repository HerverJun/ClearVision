using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.ValueObjects;
using ClearVision.Product.Infrastructure.Operators;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using OpenCvSharp;

namespace ClearVision.Product.Tests.Operators;

[TestClassification(TestDomain.Core, TestPurpose.Regression, TestLane.Pr, TestEvidenceType.Contract, TestOracleType.Contract, TestResourceRequirement.None, TestExpectedDuration.Fast, TestFlakyPolicy.Blocking, "product")]
public class FindContoursOperatorTests
{
    private readonly FindContoursOperator _operator;

    public FindContoursOperatorTests()
    {
        _operator = new FindContoursOperator(Substitute.For<ILogger<FindContoursOperator>>());
    }

    [Fact]
    public void OperatorType_ShouldBeContourDetection()
    {
        _operator.OperatorType.Should().Be(OperatorType.ContourDetection);
    }

    [Fact]
    public async Task ExecuteAsync_WithNullInputs_ShouldReturnFailure()
    {
        var op = new Operator("test", OperatorType.ContourDetection, 0, 0);
        var result = await _operator.ExecuteAsync(op, null);
        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_WithValidImage_ShouldReturnSuccess()
    {
        var op = new Operator("test", OperatorType.ContourDetection, 0, 0);
        using var image = TestHelpers.CreateTestImage();
        var inputs = TestHelpers.CreateImageInputs(image);
        var result = await _operator.ExecuteAsync(op, inputs);
        result.IsSuccess.Should().BeTrue();
        result.OutputData.Should().ContainKey("Image");
    }

    [Fact]
    public void ValidateParameters_Default_ShouldBeValid()
    {
        var op = new Operator("test", OperatorType.ContourDetection, 0, 0);
        _operator.ValidateParameters(op).IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_WithGrayImage_ShouldReturnContourPointsAndHierarchy()
    {
        var op = new Operator("test", OperatorType.ContourDetection, 0, 0);
        using var mat = new Mat(120, 160, MatType.CV_8UC1, Scalar.Black);
        Cv2.Rectangle(mat, new Rect(20, 20, 80, 60), Scalar.White, -1);
        using var image = new ImageWrapper(mat.Clone());

        var result = await _operator.ExecuteAsync(op, TestHelpers.CreateImageInputs(image));

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        var contours = result.OutputData!["Contours"].Should().BeAssignableTo<IEnumerable<List<Position>>>().Subject.ToList();
        contours.Should().NotBeEmpty();
        contours[0].Count.Should().BeGreaterThan(3);
        result.OutputData.Should().ContainKey("Hierarchy");
        result.OutputData.Should().ContainKey("ContourSummaries");
    }

    [Fact]
    public async Task ExecuteAsync_WithOtsuThresholdMode_ShouldExposeForegroundDiagnostics()
    {
        var op = new Operator("test", OperatorType.ContourDetection, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("ThresholdMode", "Otsu", "string"));
        op.AddParameter(TestHelpers.CreateParameter("MinArea", 10, "int"));

        using var mat = new Mat(120, 160, MatType.CV_8UC4, new Scalar(0, 0, 0, 255));
        Cv2.Rectangle(mat, new Rect(20, 20, 80, 60), new Scalar(255, 255, 255, 255), -1);
        using var image = new ImageWrapper(mat.Clone());

        var result = await _operator.ExecuteAsync(op, TestHelpers.CreateImageInputs(image));

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.OutputData!["ThresholdMode"].Should().Be("Otsu");
        Convert.ToDouble(result.OutputData["ForegroundRatio"]).Should().BeGreaterThan(0.0);
        var outputImage = result.OutputData["Image"].Should().BeOfType<ImageWrapper>().Subject;
        outputImage.MatReadOnly.Channels().Should().Be(3);
    }

    [Fact]
    public async Task ExecuteAsync_WithInputBinaryInverse_ShouldPreserveMaxValueRange()
    {
        var op = new Operator("test", OperatorType.ContourDetection, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("ThresholdMode", "InputBinary", "string"));
        op.AddParameter(TestHelpers.CreateParameter("ThresholdType", "BinaryInv", "string"));
        op.AddParameter(TestHelpers.CreateParameter("MaxValue", 1.0, "double"));
        op.AddParameter(TestHelpers.CreateParameter("MinArea", 1, "int"));

        using var mat = new Mat(20, 20, MatType.CV_8UC1, Scalar.Black);
        Cv2.Rectangle(mat, new Rect(5, 5, 10, 10), new Scalar(1), -1);
        using var image = new ImageWrapper(mat.Clone());

        var result = await _operator.ExecuteAsync(op, TestHelpers.CreateImageInputs(image));

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        Convert.ToInt32(result.OutputData!["ForegroundPixelCount"]).Should().Be(300);
    }

    [Fact]
    public async Task ExecuteAsync_WithFilteredContours_ShouldRemapHierarchyIndices()
    {
        var op = new Operator("test", OperatorType.ContourDetection, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("Mode", "Tree", "string"));
        op.AddParameter(TestHelpers.CreateParameter("MinArea", 50, "int"));

        using var mat = new Mat(160, 160, MatType.CV_8UC1, Scalar.Black);
        Cv2.Rectangle(mat, new Rect(20, 20, 100, 100), Scalar.White, -1);
        Cv2.Rectangle(mat, new Rect(50, 50, 40, 40), Scalar.Black, -1);
        Cv2.Rectangle(mat, new Rect(130, 20, 8, 8), Scalar.White, -1);
        using var image = new ImageWrapper(mat.Clone());

        var result = await _operator.ExecuteAsync(op, TestHelpers.CreateImageInputs(image));

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        var contours = result.OutputData!["Contours"].Should().BeAssignableTo<IEnumerable<List<Position>>>().Subject.ToList();
        contours.Should().HaveCount(2);

        var hierarchy = result.OutputData["Hierarchy"]
            .Should().BeAssignableTo<IEnumerable<Dictionary<string, object>>>()
            .Subject
            .ToList();
        hierarchy.Should().HaveCount(2);

        foreach (var item in hierarchy)
        {
            foreach (var key in new[] { "Next", "Previous", "Child", "Parent" })
            {
                var index = Convert.ToInt32(item[key]);
                (index == -1 || (index >= 0 && index < contours.Count)).Should().BeTrue();
            }
        }

        hierarchy.Should().Contain(item => Convert.ToInt32(item["Child"]) == 1);
        hierarchy.Should().Contain(item => Convert.ToInt32(item["Parent"]) == 0);
    }
}
