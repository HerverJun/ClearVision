// RoiManagerOperatorTests.cs
// RoiManagerOperatorTests测试
// 作者：蘅芜君

using System.Text.Json;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Infrastructure.Calibration;
using ClearVision.Product.Infrastructure.Operators;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace ClearVision.Product.Tests.Operators;

[TestClassification(TestDomain.Core, TestPurpose.Regression, TestLane.Pr, TestEvidenceType.Contract, TestOracleType.Contract, TestResourceRequirement.None, TestExpectedDuration.Fast, TestFlakyPolicy.Blocking, "product")]
public class RoiManagerOperatorTests
{
    private readonly RoiManagerOperator _operator;
    private readonly Guid _imagePortId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private readonly Guid _maskPortId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private readonly Guid _spatialContextPortId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    public RoiManagerOperatorTests()
    {
        var logger = Substitute.For<ILogger<RoiManagerOperator>>();
        _operator = new RoiManagerOperator(logger);
    }

    [Fact]
    public void OperatorType_ShouldBeRoiManager()
    {
        _operator.OperatorType.Should().Be(OperatorType.RoiManager);
    }

    [Fact]
    public async Task ExecuteAsync_WithNullInputs_ShouldReturnFailure()
    {
        var op = new Operator("测试", OperatorType.RoiManager, 0, 0);
        var result = await _operator.ExecuteAsync(op, null);
        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_WithEmptyInputs_ShouldReturnFailure()
    {
        var op = new Operator("测试", OperatorType.RoiManager, 0, 0);
        var result = await _operator.ExecuteAsync(op, new Dictionary<string, object>());
        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_RectangleCrop_ShouldEmitLocalToFullSpatialContext()
    {
        using var image = TestHelpers.CreateTestImage(width: 10, height: 8);
        var op = CreateOperator("Rectangle", "Crop");
        AddParameter(op, "X", 3);
        AddParameter(op, "Y", 2);
        AddParameter(op, "Width", 4);
        AddParameter(op, "Height", 3);

        var result = await _operator.ExecuteAsync(op, TestHelpers.CreateImageInputs(image));

        try
        {
            result.IsSuccess.Should().BeTrue(result.ErrorMessage);
            var output = Assert.IsType<ImageWrapper>(result.OutputData!["Image"]);
            output.Width.Should().Be(4);
            output.Height.Should().Be(3);

            var context = Assert.IsType<SpatialContextV1>(result.OutputData[RoiManagerOperator.SpatialContextOutputKey]);
            context.Binding.SourceOperatorId.Should().Be(op.Id);
            context.Binding.OutputPortId.Should().Be(_imagePortId);
            context.Binding.OutputName.Should().Be("Image");
            context.CurrentFrame.Kind.Should().Be(SpatialFrameKindV1.RoiLocal);
            context.CurrentFrame.ParentFrameId.Should().Be("image.full");

            context.TryResolveTransform(context.CurrentFrame, FrameRefV1.ImageFull(), out var localToFull, out var error)
                .Should().BeTrue(error);
            localToFull.TryApply(0, 0, out var x0, out var y0, out error).Should().BeTrue(error);
            localToFull.TryApply(4, 3, out var x1, out var y1, out error).Should().BeTrue(error);
            x0.Should().BeApproximately(3, 1e-12);
            y0.Should().BeApproximately(2, 1e-12);
            x1.Should().BeApproximately(7, 1e-12);
            y1.Should().BeApproximately(5, 1e-12);

            var mask = Assert.IsType<ImageWrapper>(result.OutputData["Mask"]);
            mask.Width.Should().Be(10);
            mask.Height.Should().Be(8);

            var maskContext = Assert.IsType<SpatialContextV1>(result.OutputData[RoiManagerOperator.MaskSpatialContextOutputKey]);
            maskContext.CurrentFrame.Should().Be(FrameRefV1.ImageFull());
            maskContext.Binding.OutputPortId.Should().Be(_maskPortId);
        }
        finally
        {
            DisposeOutputImages(result.OutputData);
        }
    }

    [Fact]
    public async Task ExecuteAsync_CircleCrop_ShouldUseClampedBoundingRectangleOffset()
    {
        using var image = TestHelpers.CreateTestImage(width: 10, height: 10);
        var op = CreateOperator("Circle", "Crop");
        AddParameter(op, "CenterX", 8);
        AddParameter(op, "CenterY", 8);
        AddParameter(op, "Radius", 3);

        var result = await _operator.ExecuteAsync(op, TestHelpers.CreateImageInputs(image));

        try
        {
            result.IsSuccess.Should().BeTrue(result.ErrorMessage);
            var output = Assert.IsType<ImageWrapper>(result.OutputData!["Image"]);
            output.Width.Should().Be(5);
            output.Height.Should().Be(5);

            var context = Assert.IsType<SpatialContextV1>(result.OutputData[RoiManagerOperator.SpatialContextOutputKey]);
            context.TryResolveTransform(context.CurrentFrame, FrameRefV1.ImageFull(), out var localToFull, out var error)
                .Should().BeTrue(error);
            localToFull.TryApply(0, 0, out var x, out var y, out error).Should().BeTrue(error);
            x.Should().BeApproximately(5, 1e-12);
            y.Should().BeApproximately(5, 1e-12);
        }
        finally
        {
            DisposeOutputImages(result.OutputData);
        }
    }

    [Fact]
    public async Task ExecuteAsync_CircleCrop_ShouldIgnoreRectangleParamsAndClampIntersectingCircle()
    {
        using var image = TestHelpers.CreateTestImage(width: 10, height: 10);
        var op = CreateOperator("Circle", "Crop");
        AddParameter(op, "X", 1000);
        AddParameter(op, "Y", 1000);
        AddParameter(op, "Width", 5);
        AddParameter(op, "Height", 5);
        AddParameter(op, "CenterX", -2);
        AddParameter(op, "CenterY", 5);
        AddParameter(op, "Radius", 5);

        var result = await _operator.ExecuteAsync(op, TestHelpers.CreateImageInputs(image));

        try
        {
            result.IsSuccess.Should().BeTrue(result.ErrorMessage);
            var output = Assert.IsType<ImageWrapper>(result.OutputData!["Image"]);
            output.Width.Should().Be(3);
            output.Height.Should().Be(10);

            var context = Assert.IsType<SpatialContextV1>(result.OutputData[RoiManagerOperator.SpatialContextOutputKey]);
            context.TryResolveTransform(context.CurrentFrame, FrameRefV1.ImageFull(), out var localToFull, out var error)
                .Should().BeTrue(error);
            localToFull.TryApply(0, 0, out var x, out var y, out error).Should().BeTrue(error);
            x.Should().BeApproximately(0, 1e-12);
            y.Should().BeApproximately(0, 1e-12);
        }
        finally
        {
            DisposeOutputImages(result.OutputData);
        }
    }

    [Fact]
    public async Task ExecuteAsync_PolygonCrop_ShouldUseClampedBoundingRectangleOffset()
    {
        using var image = TestHelpers.CreateTestImage(width: 12, height: 10);
        var op = CreateOperator("Polygon", "Crop");
        AddParameter(op, "PolygonPoints", "[[2,3],[7,3],[7,8],[2,8]]", "string");

        var result = await _operator.ExecuteAsync(op, TestHelpers.CreateImageInputs(image));

        try
        {
            result.IsSuccess.Should().BeTrue(result.ErrorMessage);
            var output = Assert.IsType<ImageWrapper>(result.OutputData!["Image"]);
            output.Width.Should().Be(5);
            output.Height.Should().Be(5);

            var context = Assert.IsType<SpatialContextV1>(result.OutputData[RoiManagerOperator.SpatialContextOutputKey]);
            context.TryResolveTransform(context.CurrentFrame, FrameRefV1.ImageFull(), out var localToFull, out var error)
                .Should().BeTrue(error);
            localToFull.TryApply(0, 0, out var x, out var y, out error).Should().BeTrue(error);
            x.Should().BeApproximately(2, 1e-12);
            y.Should().BeApproximately(3, 1e-12);
        }
        finally
        {
            DisposeOutputImages(result.OutputData);
        }
    }

    [Fact]
    public async Task ExecuteAsync_PolygonCrop_WithInvalidPoints_ShouldFallbackToImageBounds()
    {
        using var image = TestHelpers.CreateTestImage(width: 10, height: 8);
        var op = CreateOperator("Polygon", "Crop");
        AddParameter(op, "X", 1000);
        AddParameter(op, "Y", 1000);
        AddParameter(op, "Width", 5);
        AddParameter(op, "Height", 5);
        AddParameter(op, "PolygonPoints", "[]", "string");

        var result = await _operator.ExecuteAsync(op, TestHelpers.CreateImageInputs(image));

        try
        {
            result.IsSuccess.Should().BeTrue(result.ErrorMessage);
            var output = Assert.IsType<ImageWrapper>(result.OutputData!["Image"]);
            output.Width.Should().Be(10);
            output.Height.Should().Be(8);

            var context = Assert.IsType<SpatialContextV1>(result.OutputData[RoiManagerOperator.SpatialContextOutputKey]);
            context.TryResolveTransform(context.CurrentFrame, FrameRefV1.ImageFull(), out var localToFull, out var error)
                .Should().BeTrue(error);
            localToFull.TryApply(0, 0, out var x, out var y, out error).Should().BeTrue(error);
            x.Should().BeApproximately(0, 1e-12);
            y.Should().BeApproximately(0, 1e-12);
        }
        finally
        {
            DisposeOutputImages(result.OutputData);
        }
    }

    [Fact]
    public async Task ExecuteAsync_MaskOperation_ShouldKeepInputFrameWithoutCropTranslation()
    {
        using var image = TestHelpers.CreateTestImage(width: 10, height: 8);
        var op = CreateOperator("Rectangle", "Mask");
        AddParameter(op, "X", 3);
        AddParameter(op, "Y", 2);
        AddParameter(op, "Width", 4);
        AddParameter(op, "Height", 3);

        var result = await _operator.ExecuteAsync(op, TestHelpers.CreateImageInputs(image));

        try
        {
            result.IsSuccess.Should().BeTrue(result.ErrorMessage);
            var output = Assert.IsType<ImageWrapper>(result.OutputData!["Image"]);
            output.Width.Should().Be(10);
            output.Height.Should().Be(8);

            var context = Assert.IsType<SpatialContextV1>(result.OutputData[RoiManagerOperator.SpatialContextOutputKey]);
            context.CurrentFrame.Should().Be(FrameRefV1.ImageFull());
            context.TryResolveTransform(context.CurrentFrame, FrameRefV1.ImageFull(), out var identity, out var error)
                .Should().BeTrue(error);
            identity.TryApply(0, 0, out var x, out var y, out error).Should().BeTrue(error);
            x.Should().BeApproximately(0, 1e-12);
            y.Should().BeApproximately(0, 1e-12);

            var maskContext = Assert.IsType<SpatialContextV1>(result.OutputData[RoiManagerOperator.MaskSpatialContextOutputKey]);
            maskContext.CurrentFrame.Should().Be(FrameRefV1.ImageFull());
        }
        finally
        {
            DisposeOutputImages(result.OutputData);
        }
    }

    [Fact]
    public async Task ExecuteAsync_WithUpstreamJsonSpatialContext_ShouldComposeMultiLevelCropToFullImage()
    {
        var firstContext = new SpatialContextV1(
            new FrameRefV1("roi.local.upstream.image", SpatialFrameKindV1.RoiLocal, SpatialUnitV1.Pixel, "image.full"),
            [
                SpatialTransform2DV1.Identity(FrameRefV1.ImageFull()),
                new SpatialTransform2DV1(
                    new FrameRefV1("roi.local.upstream.image", SpatialFrameKindV1.RoiLocal, SpatialUnitV1.Pixel, "image.full"),
                    FrameRefV1.ImageFull(),
                    [
                        [1, 0, 2],
                        [0, 1, 1],
                        [0, 0, 1]
                    ])
            ]);
        var serializedContext = JsonSerializer.SerializeToElement(firstContext, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        using var image = TestHelpers.CreateTestImage(width: 6, height: 6);
        var op = CreateOperator("Rectangle", "Crop");
        AddParameter(op, "X", 3);
        AddParameter(op, "Y", 2);
        AddParameter(op, "Width", 2);
        AddParameter(op, "Height", 2);
        var inputs = TestHelpers.CreateImageInputs(image);
        inputs[RoiManagerOperator.SpatialContextOutputKey] = serializedContext;

        var result = await _operator.ExecuteAsync(op, inputs);

        try
        {
            result.IsSuccess.Should().BeTrue(result.ErrorMessage);
            var context = Assert.IsType<SpatialContextV1>(result.OutputData![RoiManagerOperator.SpatialContextOutputKey]);
            context.TryResolveTransform(context.CurrentFrame, FrameRefV1.ImageFull(), out var localToFull, out var error)
                .Should().BeTrue(error);

            localToFull.TryApply(0, 0, out var x0, out var y0, out error).Should().BeTrue(error);
            localToFull.TryApply(2, 2, out var x1, out var y1, out error).Should().BeTrue(error);
            x0.Should().BeApproximately(5, 1e-12);
            y0.Should().BeApproximately(3, 1e-12);
            x1.Should().BeApproximately(7, 1e-12);
            y1.Should().BeApproximately(5, 1e-12);
        }
        finally
        {
            DisposeOutputImages(result.OutputData);
        }
    }

    private Operator CreateOperator(string shape, string operation)
    {
        var op = new Operator("ROI", OperatorType.RoiManager, 0, 0);
        op.LoadOutputPort(_imagePortId, "Image", PortDataType.Image);
        op.LoadOutputPort(_maskPortId, "Mask", PortDataType.Image);
        op.LoadOutputPort(_spatialContextPortId, "SpatialContext", PortDataType.Any);
        AddParameter(op, "Shape", shape, "string");
        AddParameter(op, "Operation", operation, "string");
        return op;
    }

    private static void AddParameter(Operator op, string name, object value, string dataType = "int")
    {
        op.AddParameter(TestHelpers.CreateParameter(name, value, dataType));
    }

    private static void DisposeOutputImages(Dictionary<string, object>? outputData)
    {
        if (outputData == null)
        {
            return;
        }

        foreach (var wrapper in outputData.Values.OfType<ImageWrapper>())
        {
            wrapper.Dispose();
        }
    }
}
