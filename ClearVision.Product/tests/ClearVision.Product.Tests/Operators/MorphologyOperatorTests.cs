// MorphologyOperatorTests.cs
// MorphologyOperatorTests测试
// 作者：蘅芜君

using System.Reflection;
using ClearVision.Product.Core.Attributes;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Infrastructure.Operators;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace ClearVision.Product.Tests.Operators;

public class MorphologyOperatorTests
{
    private readonly MorphologyOperator _operator;

    public MorphologyOperatorTests()
    {
        _operator = new MorphologyOperator(Substitute.For<ILogger<MorphologyOperator>>());
    }

    [Fact]
    public void OperatorType_ShouldBeMorphology()
    {
        _operator.OperatorType.Should().Be(OperatorType.Morphology);
    }

    [Fact]
    public void Metadata_ShouldExposeChineseLegacyDisplayName()
    {
        var metadata = typeof(MorphologyOperator).GetCustomAttribute<OperatorMetaAttribute>();

        metadata.Should().NotBeNull();
        metadata!.DisplayName.Should().Be("形态学（旧版）");
        metadata.Description.Should().Contain("旧版图像形态学节点");
        metadata.Description.Should().NotContain("Legacy image morphology node");
    }

    [Fact]
    public async Task ExecuteAsync_WithNullInputs_ShouldReturnFailure()
    {
        var op = new Operator("测试", OperatorType.Morphology, 0, 0);
        var result = await _operator.ExecuteAsync(op, null);
        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_WithValidImage_ShouldReturnSuccess()
    {
        var op = new Operator("测试", OperatorType.Morphology, 0, 0);
        using var image = TestHelpers.CreateTestImage();
        var inputs = TestHelpers.CreateImageInputs(image);
        var result = await _operator.ExecuteAsync(op, inputs);
        result.IsSuccess.Should().BeTrue();
        result.OutputData.Should().ContainKey("Image");
    }

    [Fact]
    public void ValidateParameters_Default_ShouldBeValid()
    {
        var op = new Operator("测试", OperatorType.Morphology, 0, 0);
        _operator.ValidateParameters(op).IsValid.Should().BeTrue();
    }
}
