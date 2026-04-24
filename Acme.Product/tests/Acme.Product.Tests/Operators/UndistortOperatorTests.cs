using Acme.Product.Core.Entities;
using Acme.Product.Core.Enums;
using Acme.Product.Infrastructure.Operators;
using Acme.Product.Tests.TestData;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Acme.Product.Tests.Operators;

public class UndistortOperatorTests
{
    private readonly UndistortOperator _operator;

    public UndistortOperatorTests()
    {
        _operator = new UndistortOperator(Substitute.For<ILogger<UndistortOperator>>());
    }

    [Fact]
    public void OperatorType_ShouldBeUndistort()
    {
        _operator.OperatorType.Should().Be(OperatorType.Undistort);
    }

    [Fact]
    public async Task ExecuteAsync_WithNullInputs_ShouldReturnFailure()
    {
        var op = new Operator("Undistort", OperatorType.Undistort, 0, 0);
        var result = await _operator.ExecuteAsync(op, null);
        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_With1dCameraMatrixCalibration_ShouldReturnSuccess()
    {
        var op = new Operator("Undistort", OperatorType.Undistort, 0, 0);
        using var image = TestHelpers.CreateTestImage(width: 320, height: 240);
        var inputs = TestHelpers.CreateImageInputs(image);
        inputs["CalibrationData"] = CalibrationBundleV2TestData.CreateAcceptedCameraBundleJson();

        var result = await _operator.ExecuteAsync(op, inputs);
        result.IsSuccess.Should().BeTrue();
        result.OutputData.Should().ContainKey("Image");
    }

    [Fact]
    public async Task ExecuteAsync_With2dCameraMatrixCalibration_ShouldReturnSuccess()
    {
        var op = new Operator("Undistort", OperatorType.Undistort, 0, 0);
        using var image = TestHelpers.CreateTestImage(width: 320, height: 240);
        var inputs = TestHelpers.CreateImageInputs(image);
        inputs["CalibrationData"] = CalibrationBundleV2TestData.CreateAcceptedCameraBundleJson();

        var result = await _operator.ExecuteAsync(op, inputs);
        result.IsSuccess.Should().BeTrue();
        result.OutputData.Should().ContainKey("Image");
    }

    [Fact]
    public async Task ExecuteAsync_WithHealthyCalibration_ShouldEmitRuntimeQualityGateOutputs()
    {
        var op = new Operator("Undistort", OperatorType.Undistort, 0, 0);
        using var image = TestHelpers.CreateTestImage(width: 320, height: 240);
        var inputs = TestHelpers.CreateImageInputs(image);
        inputs["CalibrationData"] = CalibrationBundleV2TestData.CreateAcceptedCameraBundleJson();

        var result = await _operator.ExecuteAsync(op, inputs);

        result.IsSuccess.Should().BeTrue();
        result.OutputData["RuntimeQualityGatePassed"].Should().Be(true);
        result.OutputData["RuntimeQualityGateStatus"].Should().Be("pass");
        result.OutputData["RuntimeDriftRisk"].Should().Be("low");
        result.OutputData["RuntimeQualityMonitoringMode"].Should().Be("heuristic-baseline-only");
        result.OutputData["CalibrationMeanError"].Should().Be(0.11);
        result.OutputData["CalibrationMaxError"].Should().Be(0.23);
        ((string[])result.OutputData["RuntimeQualitySignals"])
            .Should()
            .Contain(signal => signal.Contains("comfortably inside runtime monitoring thresholds.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExecuteAsync_WithNearLimitCalibration_ShouldEmitRuntimeWarning()
    {
        var op = new Operator("Undistort", OperatorType.Undistort, 0, 0);
        using var image = TestHelpers.CreateTestImage(width: 320, height: 240);
        var inputs = TestHelpers.CreateImageInputs(image);
        inputs["CalibrationData"] = CreateCameraBundleWithQuality(meanError: 0.30, maxError: 0.50);

        var result = await _operator.ExecuteAsync(op, inputs);

        result.IsSuccess.Should().BeTrue();
        result.OutputData["RuntimeQualityGatePassed"].Should().Be(true);
        result.OutputData["RuntimeQualityGateStatus"].Should().Be("warning");
        result.OutputData["RuntimeDriftRisk"].Should().Be("moderate");
        ((string[])result.OutputData["RuntimeQualitySignals"])
            .Should()
            .Contain(signal => signal.Contains("warning threshold", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExecuteAsync_WithoutCameraMatrix_ShouldReturnFailure()
    {
        var op = new Operator("Undistort", OperatorType.Undistort, 0, 0);
        using var image = TestHelpers.CreateTestImage(width: 320, height: 240);
        var inputs = TestHelpers.CreateImageInputs(image);
        inputs["CalibrationData"] = "{\"DistCoeffs\":[0,0,0,0,0]}";

        var result = await _operator.ExecuteAsync(op, inputs);
        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public void ValidateParameters_Default_ShouldBeValid()
    {
        var op = new Operator("Undistort", OperatorType.Undistort, 0, 0);
        _operator.ValidateParameters(op).IsValid.Should().BeTrue();
    }

    private static string CreateCameraBundleWithQuality(double meanError, double maxError)
    {
        var meanText = meanError.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
        var maxText = maxError.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);

        return CalibrationBundleV2TestData.CreateAcceptedCameraBundleJson()
            .Replace("\"meanError\": 0.11", $"\"meanError\": {meanText}", StringComparison.Ordinal)
            .Replace("\"maxError\": 0.23", $"\"maxError\": {maxText}", StringComparison.Ordinal);
    }
}
