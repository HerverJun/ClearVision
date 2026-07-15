using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.ValueObjects;
using ClearVision.Product.Infrastructure.Operators;
using ClearVision.Product.Tests.Operators;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using OpenCvSharp;

namespace ClearVision.Product.Tests.Integration;

[TestClassification(TestDomain.Detection, TestPurpose.Accuracy, TestLane.Nightly, TestEvidenceType.IndependentOracle, TestOracleType.AnnotatedGroundTruth, TestResourceRequirement.None, TestExpectedDuration.Fast, TestFlakyPolicy.Blocking, "operator-quality")]
public sealed class DetectionAccuracyOracleTests
{
    [Fact]
    public async Task BlobDetection_OnAnnotatedBinaryRectangle_ShouldMatchPixelGroundTruth()
    {
        const int x = 20;
        const int y = 15;
        const int width = 30;
        const int height = 20;
        var executor = new BlobDetectionOperator(NullLogger<BlobDetectionOperator>.Instance);
        var op = new Operator("blob-accuracy", OperatorType.BlobAnalysis, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("MinArea", 1, "int"));
        op.AddParameter(TestHelpers.CreateParameter("MaxArea", 10000, "int"));

        using var mat = new Mat(80, 100, MatType.CV_8UC1, Scalar.Black);
        Cv2.Rectangle(mat, new Rect(x, y, width, height), Scalar.White, -1);
        using var image = new ImageWrapper(mat.Clone());

        var result = await executor.ExecuteAsync(op, TestHelpers.CreateImageInputs(image));

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        Convert.ToInt32(result.OutputData!["BlobCount"]).Should().Be(1);
        var blob = result.OutputData["Blobs"]
            .Should().BeOfType<List<Dictionary<string, object>>>()
            .Subject.Single();
        Convert.ToDouble(blob["Area"]).Should().Be(width * height);
        Convert.ToDouble(blob["CenterX"]).Should().BeApproximately(x + ((width - 1) / 2.0), 0.01);
        Convert.ToDouble(blob["CenterY"]).Should().BeApproximately(y + ((height - 1) / 2.0), 0.01);
        (result.OutputData["Image"] as ImageWrapper)?.Dispose();
    }

    [Fact]
    public async Task ColorDetection_OnAnnotatedSolidRedImage_ShouldReportFullCoverage()
    {
        var executor = new ColorDetectionOperator(NullLogger<ColorDetectionOperator>.Instance);
        var op = new Operator("color-accuracy", OperatorType.ColorDetection, 0, 0);
        foreach (var (name, value) in new Dictionary<string, object>
                 {
                     ["AnalysisMode"] = "HsvInspection",
                     ["HueLow"] = 170,
                     ["HueHigh"] = 10,
                     ["SatLow"] = 150,
                     ["SatHigh"] = 255,
                     ["ValLow"] = 150,
                     ["ValHigh"] = 255
                 })
        {
            op.AddParameter(TestHelpers.CreateParameter(name, value, "string"));
        }

        using var image = new ImageWrapper(new Mat(60, 60, MatType.CV_8UC3, new Scalar(0, 0, 255)));
        var result = await executor.ExecuteAsync(op, TestHelpers.CreateImageInputs(image));

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        Convert.ToDouble(result.OutputData!["Coverage"]).Should().BeApproximately(1.0, 1e-9);
        (result.OutputData["Image"] as ImageWrapper)?.Dispose();
    }
}
