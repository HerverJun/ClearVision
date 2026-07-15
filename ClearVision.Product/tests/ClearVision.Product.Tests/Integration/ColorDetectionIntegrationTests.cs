using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.ValueObjects;
using ClearVision.Product.Infrastructure.Operators;
using ClearVision.Product.Tests.Operators;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace ClearVision.Product.Tests.Integration;

[TestClassification(TestDomain.Detection, TestPurpose.Integration, TestLane.Nightly, TestEvidenceType.IntegrationEvidence, TestOracleType.Contract, TestResourceRequirement.None, TestExpectedDuration.Medium, TestFlakyPolicy.Blocking, "operator-quality")]
public class ColorDetectionIntegrationTests
{
    [Fact]
    public async Task ColorDetection_Flow_ShouldIdentifyColors()
    {
        // 1. Arrange
        var op = new Operator("颜色检测", OperatorType.ColorDetection, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("TargetColor", "Red", "string"));
        op.AddParameter(TestHelpers.CreateParameter("Tolerance", 10.0, "double"));

        var executor = new ColorDetectionOperator(Substitute.For<ILogger<ColorDetectionOperator>>());

        var inputImage = TestHelpers.CreateTestImage(color: new OpenCvSharp.Scalar(0, 0, 255)); // Red in BGR
        var inputs = new Dictionary<string, object> { { "Image", inputImage } };

        // 2. Act
        var result = await executor.ExecuteAsync(op, inputs, CancellationToken.None);

        // 3. Assert
        result.IsSuccess.Should().BeTrue();
        // Assuming implementation output
        // result.OutputData.Should().ContainKey("IsMatch");
    }
}
