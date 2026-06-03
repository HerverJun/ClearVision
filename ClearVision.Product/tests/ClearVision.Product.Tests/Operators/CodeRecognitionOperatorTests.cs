// CodeRecognitionOperatorTests.cs
// CodeRecognitionOperatorTests测试
// 作者：蘅芜君

using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Infrastructure.Operators;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using OpenCvSharp;

namespace ClearVision.Product.Tests.Operators;

public class CodeRecognitionOperatorTests
{
    private readonly CodeRecognitionOperator _operator;

    public CodeRecognitionOperatorTests()
    {
        _operator = new CodeRecognitionOperator(Substitute.For<ILogger<CodeRecognitionOperator>>());
    }

    [Fact]
    public void OperatorType_ShouldBeCodeRecognition()
    {
        _operator.OperatorType.Should().Be(OperatorType.CodeRecognition);
    }

    [Fact]
    public async Task ExecuteAsync_WithNullInputs_ShouldReturnFailure()
    {
        var op = new Operator("测试", OperatorType.CodeRecognition, 0, 0);
        var result = await _operator.ExecuteAsync(op, null);
        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_WithValidImage_ShouldReturnSuccess()
    {
        var op = new Operator("测试", OperatorType.CodeRecognition, 0, 0);
        using var image = TestHelpers.CreateTestImage();
        var inputs = TestHelpers.CreateImageInputs(image);
        var result = await _operator.ExecuteAsync(op, inputs);
        result.IsSuccess.Should().BeTrue();
        result.OutputData.Should().ContainKey("Image");
    }

    [Fact]
    public async Task ExecuteAsync_WithGrayImage_ShouldReturnSuccess()
    {
        var op = new Operator("test", OperatorType.CodeRecognition, 0, 0);
        using var image = TestHelpers.CreateGrayTestImage(80, 80, 240);

        Cv2.Rectangle(image.GetMat(), new Rect(16, 16, 48, 48), Scalar.Black, 2);

        var result = await _operator.ExecuteAsync(op, TestHelpers.CreateImageInputs(image));

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.OutputData.Should().ContainKey("Image");
        result.OutputData.Should().ContainKey("CodeCount");
    }

    [Fact]
    public void ValidateParameters_Default_ShouldBeValid()
    {
        var op = new Operator("测试", OperatorType.CodeRecognition, 0, 0);
        _operator.ValidateParameters(op).IsValid.Should().BeTrue();
    }
}
