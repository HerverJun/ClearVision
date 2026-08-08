using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.ValueObjects;
using ClearVision.Product.Infrastructure.Operators;
using ClearVision.Product.Tests.Operators;
using ClearVision.Product.Tests.TestData;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using OpenCvSharp;

namespace ClearVision.Product.Tests.Integration;

[TestClassification(TestDomain.Calibration, TestPurpose.Accuracy, TestLane.Pr, TestEvidenceType.IndependentOracle, TestOracleType.Mathematical, TestResourceRequirement.None, TestExpectedDuration.Fast, TestFlakyPolicy.Blocking, "operator-quality")]
public sealed class CalibrationAccuracyOracleTests
{
    [Fact]
    public async Task PixelToWorld_ScaleOffsetBundle_ShouldMatchIndependentAffineEquation()
    {
        var executor = new PixelToWorldTransformOperator(NullLogger<PixelToWorldTransformOperator>.Instance);
        var op = new Operator("calibration-accuracy", OperatorType.PixelToWorldTransform, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("TransformMode", "PixelToWorld", "string"));
        var pixel = new Position(137.5, 82.25);
        using var image = TestHelpers.CreateTestImage(width: 320, height: 240);
        var inputs = TestHelpers.CreateImageInputs(image);
        inputs["CalibrationData"] = CalibrationBundleV2TestData.CreateAcceptedScaleOffsetBundleJson();
        inputs["Points"] = new List<Position> { pixel };

        var result = await executor.ExecuteAsync(op, inputs);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        var actual = result.OutputData!["TransformedPoints"]
            .Should().BeOfType<List<Point3d>>()
            .Subject.Single();
        actual.X.Should().BeApproximately(pixel.X * 0.02, 1e-9);
        actual.Y.Should().BeApproximately(pixel.Y * 0.02, 1e-9);
        actual.Z.Should().BeApproximately(0.0, 1e-12);
        (result.OutputData["Image"] as ImageWrapper)?.Dispose();
    }
}
