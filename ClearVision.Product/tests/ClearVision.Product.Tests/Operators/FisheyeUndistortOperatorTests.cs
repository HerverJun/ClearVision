using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Infrastructure.Operators;
using ClearVision.Product.Tests.TestData;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace ClearVision.Product.Tests.Operators;

[TestClassification(TestDomain.Calibration, TestPurpose.Regression, TestLane.Pr, TestEvidenceType.Contract, TestOracleType.Contract, TestResourceRequirement.None, TestExpectedDuration.Fast, TestFlakyPolicy.Blocking, "operator-quality")]
public class FisheyeUndistortOperatorTests
{
    private readonly FisheyeUndistortOperator _operator;

    public FisheyeUndistortOperatorTests()
    {
        _operator = new FisheyeUndistortOperator(Substitute.For<ILogger<FisheyeUndistortOperator>>());
    }

    [Fact]
    public void OperatorType_ShouldBeFisheyeUndistort()
    {
        _operator.OperatorType.Should().Be(OperatorType.FisheyeUndistort);
    }

    [Fact]
    public async Task ExecuteAsync_WithNullInputs_ShouldReturnFailure()
    {
        var op = new Operator("FisheyeUndistort", OperatorType.FisheyeUndistort, 0, 0);
        var result = await _operator.ExecuteAsync(op, null);
        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_WithoutCalibrationData_ShouldReturnFailure()
    {
        var op = new Operator("FisheyeUndistort", OperatorType.FisheyeUndistort, 0, 0);
        using var image = TestHelpers.CreateTestImage(width: 320, height: 240);
        var inputs = TestHelpers.CreateImageInputs(image);

        var result = await _operator.ExecuteAsync(op, inputs);
        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_WithValidFisheyeCalibration_ShouldReturnSuccess()
    {
        var op = new Operator("FisheyeUndistort", OperatorType.FisheyeUndistort, 0, 0);
        using var image = TestHelpers.CreateTestImage(width: 320, height: 240);
        var inputs = TestHelpers.CreateImageInputs(image);
        inputs["CalibrationData"] = CalibrationBundleV2TestData.CreateAcceptedCameraBundleJson(fisheye: true);

        var result = await _operator.ExecuteAsync(op, inputs);
        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.OutputData.Should().ContainKey("Image");
    }

    [Fact]
    public async Task ExecuteAsync_WithHealthyFisheyeCalibration_ShouldEmitRuntimeQualityGateOutputs()
    {
        var op = new Operator("FisheyeUndistort", OperatorType.FisheyeUndistort, 0, 0);
        using var image = TestHelpers.CreateTestImage(width: 320, height: 240);
        var inputs = TestHelpers.CreateImageInputs(image);
        inputs["CalibrationData"] = CalibrationBundleV2TestData.CreateAcceptedCameraBundleJson(fisheye: true);

        var result = await _operator.ExecuteAsync(op, inputs);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        var outputData = result.OutputData!;
        outputData["RuntimeQualityGatePassed"].Should().Be(true);
        outputData["RuntimeQualityGateStatus"].Should().Be("pass");
        outputData["RuntimeDriftRisk"].Should().Be("low");
        outputData["RuntimeQualityMonitoringMode"].Should().Be("heuristic-baseline-only");
        outputData["CalibrationMeanError"].Should().Be(0.11);
        outputData["CalibrationMaxError"].Should().Be(0.23);
    }

    [Fact]
    public async Task ExecuteAsync_WithPoorFisheyeCalibration_ShouldEmitRuntimeFailureSignal()
    {
        var op = new Operator("FisheyeUndistort", OperatorType.FisheyeUndistort, 0, 0);
        using var image = TestHelpers.CreateTestImage(width: 320, height: 240);
        var inputs = TestHelpers.CreateImageInputs(image);
        inputs["CalibrationData"] = CreateFisheyeBundleWithQuality(meanError: 0.40, maxError: 0.70);

        var result = await _operator.ExecuteAsync(op, inputs);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        var outputData = result.OutputData!;
        outputData["RuntimeQualityGatePassed"].Should().Be(false);
        outputData["RuntimeQualityGateStatus"].Should().Be("fail");
        outputData["RuntimeDriftRisk"].Should().Be("high");
        ((string[])outputData["RuntimeQualitySignals"])
            .Should()
            .Contain(signal => signal.Contains("fail threshold", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExecuteAsync_WithStandardCalibration_ShouldReturnFailure()
    {
        var op = new Operator("FisheyeUndistort", OperatorType.FisheyeUndistort, 0, 0);
        using var image = TestHelpers.CreateTestImage(width: 320, height: 240);
        var inputs = TestHelpers.CreateImageInputs(image);
        inputs["CalibrationData"] = CalibrationBundleV2TestData.CreateAcceptedCameraBundleJson();

        var result = await _operator.ExecuteAsync(op, inputs);
        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public void ValidateParameters_WithValidBalance_ShouldBeValid()
    {
        var op = new Operator("FisheyeUndistort", OperatorType.FisheyeUndistort, 0, 0);
        op.Parameters.Add(TestHelpers.CreateParameter("Balance", 0.5));

        _operator.ValidateParameters(op).IsValid.Should().BeTrue();
    }

    [Fact]
    public void ValidateParameters_WithInvalidBalance_ShouldBeInvalid()
    {
        var op = new Operator("FisheyeUndistort", OperatorType.FisheyeUndistort, 0, 0);
        op.Parameters.Add(TestHelpers.CreateParameter("Balance", 1.5)); // 超出范围

        _operator.ValidateParameters(op).IsValid.Should().BeFalse();
    }
    private static string CreateFisheyeBundleWithQuality(double meanError, double maxError)
    {
        var meanText = meanError.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
        var maxText = maxError.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);

        return CalibrationBundleV2TestData.CreateAcceptedCameraBundleJson(fisheye: true)
            .Replace("\"meanError\": 0.11", $"\"meanError\": {meanText}", StringComparison.Ordinal)
            .Replace("\"maxError\": 0.23", $"\"maxError\": {maxText}", StringComparison.Ordinal);
    }
}
