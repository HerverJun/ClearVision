using System.Reflection;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Infrastructure.Operators;
using ClearVision.Product.Tests.TestData;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace ClearVision.Product.Tests.Operators;

[TestClassification(TestDomain.Calibration, TestPurpose.Regression, TestLane.Pr, TestEvidenceType.Contract, TestOracleType.Contract, TestResourceRequirement.None, TestExpectedDuration.Fast, TestFlakyPolicy.Blocking, "operator-quality")]
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
        var outputData = result.OutputData!;
        outputData["RuntimeQualityGatePassed"].Should().Be(true);
        outputData["RuntimeQualityGateStatus"].Should().Be("pass");
        outputData["RuntimeDriftRisk"].Should().Be("low");
        outputData["RuntimeQualityMonitoringMode"].Should().Be("heuristic-baseline-only");
        outputData["CalibrationMeanError"].Should().Be(0.11);
        outputData["CalibrationMaxError"].Should().Be(0.23);
        ((string[])outputData["RuntimeQualitySignals"])
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
        var outputData = result.OutputData!;
        outputData["RuntimeQualityGatePassed"].Should().Be(true);
        outputData["RuntimeQualityGateStatus"].Should().Be("warning");
        outputData["RuntimeDriftRisk"].Should().Be("moderate");
        ((string[])outputData["RuntimeQualitySignals"])
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

    [Fact]
    public async Task Dispose_AfterCachePopulation_ShouldClearRemapCacheAndRejectFurtherExecution()
    {
        var op = new Operator("Undistort", OperatorType.Undistort, 0, 0);
        using var image = TestHelpers.CreateTestImage(width: 320, height: 240);
        var inputs = TestHelpers.CreateImageInputs(image);
        inputs["CalibrationData"] = CalibrationBundleV2TestData.CreateAcceptedCameraBundleJson();

        var result = await _operator.ExecuteAsync(op, inputs);

        result.IsSuccess.Should().BeTrue();
        GetMapCacheCount(_operator).Should().Be(1);

        _operator.Dispose();

        GetMapCacheCount(_operator).Should().Be(0);

        using var imageAfterDispose = TestHelpers.CreateTestImage(width: 320, height: 240);
        var inputsAfterDispose = TestHelpers.CreateImageInputs(imageAfterDispose);
        inputsAfterDispose["CalibrationData"] = CalibrationBundleV2TestData.CreateAcceptedCameraBundleJson();

        var afterDispose = await _operator.ExecuteAsync(op, inputsAfterDispose);

        afterDispose.IsSuccess.Should().BeFalse();
        afterDispose.ErrorMessage.Should().Contain(nameof(UndistortOperator));
    }

    private static string CreateCameraBundleWithQuality(double meanError, double maxError)
    {
        var meanText = meanError.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
        var maxText = maxError.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);

        return CalibrationBundleV2TestData.CreateAcceptedCameraBundleJson()
            .Replace("\"meanError\": 0.11", $"\"meanError\": {meanText}", StringComparison.Ordinal)
            .Replace("\"maxError\": 0.23", $"\"maxError\": {maxText}", StringComparison.Ordinal);
    }

    private static int GetMapCacheCount(UndistortOperator @operator)
    {
        var field = typeof(UndistortOperator).GetField("_mapCache", BindingFlags.Instance | BindingFlags.NonPublic);
        field.Should().NotBeNull();

        var cache = field!.GetValue(@operator);
        var countProperty = cache!.GetType().GetProperty("Count");
        countProperty.Should().NotBeNull();

        return (int)countProperty!.GetValue(cache)!;
    }
}
