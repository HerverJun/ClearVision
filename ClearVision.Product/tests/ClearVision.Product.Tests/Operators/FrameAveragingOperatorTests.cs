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
public class FrameAveragingOperatorTests : IDisposable
{
    private readonly IDisposable _authorityScope = TestExecutionAuthorityScope.Enter();

    public void Dispose() => _authorityScope.Dispose();

    [Fact]
    public void OperatorType_ShouldBeFrameAveraging()
    {
        var sut = CreateSut();
        Assert.Equal(OperatorType.FrameAveraging, sut.OperatorType);
    }

    [Fact]
    public async Task ExecuteAsync_MeanModeAcrossTwoFrames_ShouldIncreaseFrameCount()
    {
        var sut = CreateSut();
        var op = CreateOperator(new Dictionary<string, object>
        {
            { "FrameCount", 3 },
            { "Mode", "Mean" }
        });

        using var frame1 = CreateSolidImage(20);
        using var frame2 = CreateSolidImage(60);

        var first = await sut.ExecuteAsync(op, TestHelpers.CreateImageInputs(frame1));
        var second = await sut.ExecuteAsync(op, TestHelpers.CreateImageInputs(frame2));

        Assert.True(first.IsSuccess, first.ErrorMessage);
        Assert.True(second.IsSuccess, second.ErrorMessage);
        Assert.Equal(1, Convert.ToInt32(first.OutputData!["FrameCount"]));
        Assert.Equal(2, Convert.ToInt32(second.OutputData!["FrameCount"]));
    }

    [Fact]
    public async Task ExecuteAsync_ShouldIsolateBufferedFramesByOperatorId()
    {
        var sut = CreateSut();
        var firstOperator = CreateOperator(new Dictionary<string, object>
        {
            { "FrameCount", 3 },
            { "Mode", "Mean" }
        });
        var secondOperator = CreateOperator(new Dictionary<string, object>
        {
            { "FrameCount", 3 },
            { "Mode", "Mean" }
        });

        using var firstFrame = CreateGrayImage(20);
        using var foreignFrame = CreateGrayImage(200);
        using var secondFrame = CreateGrayImage(30);

        var first = await sut.ExecuteAsync(firstOperator, TestHelpers.CreateImageInputs(firstFrame));
        var foreign = await sut.ExecuteAsync(secondOperator, TestHelpers.CreateImageInputs(foreignFrame));
        var second = await sut.ExecuteAsync(firstOperator, TestHelpers.CreateImageInputs(secondFrame));

        Assert.True(first.IsSuccess, first.ErrorMessage);
        Assert.True(foreign.IsSuccess, foreign.ErrorMessage);
        Assert.True(second.IsSuccess, second.ErrorMessage);
        Assert.Equal(1, Convert.ToInt32(first.OutputData!["FrameCount"]));
        Assert.Equal(1, Convert.ToInt32(foreign.OutputData!["FrameCount"]));
        Assert.Equal(2, Convert.ToInt32(second.OutputData!["FrameCount"]));

        using var output = Assert.IsType<ImageWrapper>(second.OutputData["Image"]);
        using var resultMat = output.GetMat();
        Assert.Equal(25, resultMat.At<byte>(0, 0));
    }

    [Fact]
    public async Task ExecuteAsync_MeanMode_WhenWindowTrimsOldFrame_ShouldAverageRecentFramesOnly()
    {
        var sut = CreateSut();
        var op = CreateOperator(new Dictionary<string, object>
        {
            { "FrameCount", 2 },
            { "Mode", "Mean" }
        });

        using var frame1 = CreateGrayImage(10);
        using var frame2 = CreateGrayImage(50);
        using var frame3 = CreateGrayImage(90);

        _ = await sut.ExecuteAsync(op, TestHelpers.CreateImageInputs(frame1));
        _ = await sut.ExecuteAsync(op, TestHelpers.CreateImageInputs(frame2));
        var third = await sut.ExecuteAsync(op, TestHelpers.CreateImageInputs(frame3));

        Assert.True(third.IsSuccess, third.ErrorMessage);
        Assert.Equal(2, Convert.ToInt32(third.OutputData!["FrameCount"]));

        using var output = Assert.IsType<ImageWrapper>(third.OutputData["Image"]);
        using var resultMat = output.GetMat();
        Assert.Equal(70, resultMat.At<byte>(0, 0));
    }

    [Fact]
    public async Task ExecuteAsync_MeanMode_WhenShapeChanges_ShouldResetBufferedFramesAndAccumulator()
    {
        var sut = CreateSut();
        var op = CreateOperator(new Dictionary<string, object>
        {
            { "FrameCount", 3 },
            { "Mode", "Mean" }
        });

        using var frame1 = CreateGrayImage(10, rows: 80, cols: 80);
        using var frame2 = CreateGrayImage(90, rows: 40, cols: 40);

        _ = await sut.ExecuteAsync(op, TestHelpers.CreateImageInputs(frame1));
        var second = await sut.ExecuteAsync(op, TestHelpers.CreateImageInputs(frame2));

        Assert.True(second.IsSuccess, second.ErrorMessage);
        Assert.Equal(1, Convert.ToInt32(second.OutputData!["FrameCount"]));

        using var output = Assert.IsType<ImageWrapper>(second.OutputData["Image"]);
        using var resultMat = output.GetMat();
        Assert.Equal(40, resultMat.Rows);
        Assert.Equal(40, resultMat.Cols);
        Assert.Equal(90, resultMat.At<byte>(0, 0));
    }

    [Fact]
    public void ValidateParameters_WithInvalidMode_ShouldReturnInvalid()
    {
        var sut = CreateSut();
        var op = CreateOperator(new Dictionary<string, object> { { "Mode", "WeightedMean" } });

        var validation = sut.ValidateParameters(op);

        Assert.False(validation.IsValid);
    }

    [Fact]
    public async Task ExecuteAsync_MedianMode_GrayFrames_ShouldReturnMedianValue()
    {
        var sut = CreateSut();
        var op = CreateOperator(new Dictionary<string, object>
        {
            { "FrameCount", 3 },
            { "Mode", "Median" }
        });

        using var frame1 = CreateGrayImage(10);
        using var frame2 = CreateGrayImage(200);
        using var frame3 = CreateGrayImage(100);

        _ = await sut.ExecuteAsync(op, TestHelpers.CreateImageInputs(frame1));
        _ = await sut.ExecuteAsync(op, TestHelpers.CreateImageInputs(frame2));
        var third = await sut.ExecuteAsync(op, TestHelpers.CreateImageInputs(frame3));

        Assert.True(third.IsSuccess, third.ErrorMessage);
        Assert.NotNull(third.OutputData);

        using var output = Assert.IsType<ImageWrapper>(third.OutputData!["Image"]);
        using var resultMat = output.GetMat();
        Assert.Equal(100, resultMat.At<byte>(0, 0));
    }

    [Fact]
    public async Task ExecuteAsync_MedianMode_ColorFrames_ShouldUseMedianInsteadOfMean()
    {
        var sut = CreateSut();
        var op = CreateOperator(new Dictionary<string, object>
        {
            { "FrameCount", 3 },
            { "Mode", "Median" }
        });

        using var frame1 = CreateSolidImage(0);
        using var frame2 = CreateSolidImage(0);
        using var frame3 = CreateSolidImage(255);

        _ = await sut.ExecuteAsync(op, TestHelpers.CreateImageInputs(frame1));
        _ = await sut.ExecuteAsync(op, TestHelpers.CreateImageInputs(frame2));
        var third = await sut.ExecuteAsync(op, TestHelpers.CreateImageInputs(frame3));

        Assert.True(third.IsSuccess, third.ErrorMessage);
        Assert.NotNull(third.OutputData);

        using var output = Assert.IsType<ImageWrapper>(third.OutputData!["Image"]);
        using var resultMat = output.GetMat();
        var pixel = resultMat.At<Vec3b>(0, 0);

        Assert.Equal(0, pixel.Item0);
        Assert.Equal(0, pixel.Item1);
        Assert.Equal(0, pixel.Item2);
    }

    [Fact]
    public async Task ExecuteAsync_MedianMode_EvenFrameCount_ShouldUseUpperMedianOrder()
    {
        var sut = CreateSut();
        var op = CreateOperator(new Dictionary<string, object>
        {
            { "FrameCount", 4 },
            { "Mode", "Median" }
        });

        using var frame1 = CreateGrayImage(10);
        using var frame2 = CreateGrayImage(20);
        using var frame3 = CreateGrayImage(30);
        using var frame4 = CreateGrayImage(200);

        _ = await sut.ExecuteAsync(op, TestHelpers.CreateImageInputs(frame1));
        _ = await sut.ExecuteAsync(op, TestHelpers.CreateImageInputs(frame2));
        _ = await sut.ExecuteAsync(op, TestHelpers.CreateImageInputs(frame3));
        var fourth = await sut.ExecuteAsync(op, TestHelpers.CreateImageInputs(frame4));

        Assert.True(fourth.IsSuccess, fourth.ErrorMessage);
        Assert.NotNull(fourth.OutputData);

        using var output = Assert.IsType<ImageWrapper>(fourth.OutputData!["Image"]);
        using var resultMat = output.GetMat();
        Assert.Equal(30, resultMat.At<byte>(0, 0));
    }

    private static FrameAveragingOperator CreateSut()
    {
        return new FrameAveragingOperator(Substitute.For<ILogger<FrameAveragingOperator>>());
    }

    private static Operator CreateOperator(Dictionary<string, object>? parameters = null)
    {
        var op = new Operator("FrameAveraging", OperatorType.FrameAveraging, 0, 0);

        if (parameters != null)
        {
            foreach (var (name, value) in parameters)
            {
                op.AddParameter(new Parameter(Guid.NewGuid(), name, name, string.Empty, "string", value));
            }
        }

        return op;
    }

    private static ImageWrapper CreateSolidImage(byte value)
    {
        var mat = new Mat(80, 80, MatType.CV_8UC3, new Scalar(value, value, value));
        return new ImageWrapper(mat);
    }

    private static ImageWrapper CreateGrayImage(byte value, int rows = 80, int cols = 80)
    {
        var mat = new Mat(rows, cols, MatType.CV_8UC1, new Scalar(value));
        return new ImageWrapper(mat);
    }
}
