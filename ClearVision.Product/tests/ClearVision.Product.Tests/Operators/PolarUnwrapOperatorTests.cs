using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.ValueObjects;
using ClearVision.Product.Infrastructure.Operators;
using Microsoft.Extensions.Logging;
using NSubstitute;
using OpenCvSharp;
using Xunit;

namespace ClearVision.Product.Tests.Operators;

[TestClassification(TestDomain.Core, TestPurpose.Regression, TestLane.Pr, TestEvidenceType.Contract, TestOracleType.Contract, TestResourceRequirement.None, TestExpectedDuration.Fast, TestFlakyPolicy.Blocking, "product")]
[Trait("Category", "Sprint5_Phase2")]
public class PolarUnwrapOperatorTests
{
    [Fact]
    public void OperatorType_ShouldBePolarUnwrap()
    {
        var sut = CreateSut();
        Assert.Equal(OperatorType.PolarUnwrap, sut.OperatorType);
    }

    [Fact]
    public async Task ExecuteAsync_WithValidRingImage_ShouldReturnUnwrappedImage()
    {
        var sut = CreateSut();
        var op = CreateOperator(new Dictionary<string, object>
        {
            { "CenterX", 60 },
            { "CenterY", 60 },
            { "InnerRadius", 20 },
            { "OuterRadius", 50 },
            { "OutputWidth", 180 }
        });

        using var image = CreateRingImage();
        var result = await sut.ExecuteAsync(op, TestHelpers.CreateImageInputs(image));

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.OutputData);
        Assert.Equal(180, Convert.ToInt32(result.OutputData!["Width"]));
        Assert.Equal(30, Convert.ToInt32(result.OutputData["Height"]));

        using var outputImage = Assert.IsType<ImageWrapper>(result.OutputData["Image"]);
        var outputMat = outputImage.GetMat();
        Assert.Equal(180, outputMat.Width);
        Assert.Equal(30, outputMat.Height);
    }

    [Fact]
    public void ValidateParameters_WithInvalidRadii_ShouldReturnInvalid()
    {
        var sut = CreateSut();
        var op = CreateOperator(new Dictionary<string, object>
        {
            { "InnerRadius", 50 },
            { "OuterRadius", 20 }
        });

        var validation = sut.ValidateParameters(op);

        Assert.False(validation.IsValid);
    }

    [Fact]
    public void ValidateParameters_WithoutOuterRadius_ShouldUseMetadataDefault()
    {
        var sut = CreateSut();
        var op = CreateOperator(new Dictionary<string, object>
        {
            { "InnerRadius", 20 }
        });

        var validation = sut.ValidateParameters(op);

        Assert.True(validation.IsValid);
    }

    [Fact]
    public async Task ExecuteAsync_WithoutOuterRadius_ShouldUseMetadataDefault()
    {
        var sut = CreateSut();
        var op = CreateOperator(new Dictionary<string, object>
        {
            { "InnerRadius", 20 },
            { "OutputWidth", 180 },
            { "UseWarpPolar", false }
        });

        using var image = CreateRingImage(300);
        var result = await sut.ExecuteAsync(op, TestHelpers.CreateImageInputs(image));

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.OutputData);
        Assert.Equal(180, Convert.ToInt32(result.OutputData!["Width"]));
        Assert.Equal(80, Convert.ToInt32(result.OutputData["Height"]));
        using var outputImage = Assert.IsType<ImageWrapper>(result.OutputData["Image"]);
        Assert.Equal(180, outputImage.GetMat().Width);
        Assert.Equal(80, outputImage.GetMat().Height);
    }

    private static PolarUnwrapOperator CreateSut()
    {
        return new PolarUnwrapOperator(Substitute.For<ILogger<PolarUnwrapOperator>>());
    }

    private static Operator CreateOperator(Dictionary<string, object>? parameters = null)
    {
        var op = new Operator("PolarUnwrap", OperatorType.PolarUnwrap, 0, 0);

        if (parameters != null)
        {
            foreach (var (name, value) in parameters)
            {
                op.AddParameter(new Parameter(Guid.NewGuid(), name, name, string.Empty, "string", value));
            }
        }

        return op;
    }

    private static ImageWrapper CreateRingImage(int size = 120)
    {
        var center = size / 2;
        var mat = new Mat(size, size, MatType.CV_8UC3, Scalar.Black);
        Cv2.Circle(mat, new Point(center, center), Math.Max(2, size / 3), Scalar.White, 2);
        Cv2.Circle(mat, new Point(center, center), Math.Max(1, size / 5), new Scalar(127, 127, 127), 2);
        return new ImageWrapper(mat);
    }
}
