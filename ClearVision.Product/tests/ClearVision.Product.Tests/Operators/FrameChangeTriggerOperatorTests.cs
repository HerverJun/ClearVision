using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.ValueObjects;
using ClearVision.Product.Infrastructure.Operators;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using OpenCvSharp;

namespace ClearVision.Product.Tests.Operators;

[TestClassification(TestDomain.Core, TestPurpose.Regression, TestLane.Pr, TestEvidenceType.Contract, TestOracleType.Contract, TestResourceRequirement.None, TestExpectedDuration.Fast, TestFlakyPolicy.Blocking, "product")]
public class FrameChangeTriggerOperatorTests
{
    private readonly FrameChangeTriggerOperator _operator =
        new(Substitute.For<ILogger<FrameChangeTriggerOperator>>());

    [Fact]
    public void OperatorType_ShouldBeFrameChangeTrigger()
    {
        _operator.OperatorType.Should().Be(OperatorType.FrameChangeTrigger);
    }

    [Fact]
    public async Task ExecuteAsync_FirstFrame_ShouldBuildBaselineAndShortCircuit()
    {
        using var image = CreateGrayImage(16, 16, 10);

        var result = await _operator.ExecuteAsync(CreateOperator(), new Dictionary<string, object>
        {
            ["Image"] = image
        });

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.ShouldShortCircuitFlow.Should().BeTrue();
        result.OutputData.Should().NotBeNull();
        result.OutputData!["Triggered"].Should().Be(false);
        result.OutputData["Reason"].Should().Be("baseline");
        result.OutputData["BaselineReady"].Should().Be(true);
        result.OutputData.Should().ContainKeys(
            "TotalPixels",
            "CooldownRemainingMs",
            "EffectivePixelThreshold",
            "EffectiveMinChangeRatio");
    }

    [Fact]
    public async Task ExecuteAsync_WithLargeRoiChange_ShouldTrigger()
    {
        using var first = CreateGrayImage(16, 16, 10);
        using var second = CreateGrayImage(16, 16, 240);
        var op = CreateOperator(new Dictionary<string, object>
        {
            ["CooldownMs"] = 0,
            ["MinChangeRatio"] = 0.1,
            ["MinChangePixels"] = 10
        });

        await _operator.ExecuteAsync(op, new Dictionary<string, object> { ["Image"] = first });
        var result = await _operator.ExecuteAsync(op, new Dictionary<string, object> { ["Image"] = second });

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.ShouldShortCircuitFlow.Should().BeFalse();
        result.OutputData.Should().NotBeNull();
        result.OutputData!["Triggered"].Should().Be(true);
        result.OutputData["Reason"].Should().Be("change_detected");
        Convert.ToDouble(result.OutputData["ChangeScore"]).Should().BeGreaterThan(0.9);
    }

    [Fact]
    public async Task ExecuteAsync_ShortCircuitDisabled_ShouldPassUntriggeredFrameDownstream()
    {
        using var first = CreateGrayImage(16, 16, 10);
        using var second = CreateGrayImage(16, 16, 12);
        var op = CreateOperator(new Dictionary<string, object>
        {
            ["ShortCircuitWhenNotTriggered"] = false,
            ["MinChangeRatio"] = 0.9,
            ["MinChangePixels"] = 200
        });

        await _operator.ExecuteAsync(op, new Dictionary<string, object> { ["Image"] = first });
        var result = await _operator.ExecuteAsync(op, new Dictionary<string, object> { ["Image"] = second });

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.ShouldShortCircuitFlow.Should().BeFalse();
        result.OutputData!["Triggered"].Should().Be(false);
        result.OutputData["Reason"].Should().Be("below_threshold");
    }

    [Fact]
    public async Task ExecuteAsync_RoiPastImageBoundary_ShouldClampWithoutThrowing()
    {
        using var image = CreateGrayImage(16, 16, 10);
        var op = CreateOperator(new Dictionary<string, object>
        {
            ["RoiX"] = 99,
            ["RoiY"] = 99,
            ["RoiW"] = 50,
            ["RoiH"] = 50
        });

        var result = await _operator.ExecuteAsync(op, new Dictionary<string, object> { ["Image"] = image });

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.OutputData!["RoiX"].Should().Be(15);
        result.OutputData["RoiY"].Should().Be(15);
        result.OutputData["RoiW"].Should().Be(1);
        result.OutputData["RoiH"].Should().Be(1);
    }

    [Fact]
    public async Task ExecuteAsync_InvalidParameterType_ShouldReturnDiagnosticFailure()
    {
        using var image = CreateGrayImage(16, 16, 10);
        var op = CreateOperator(new Dictionary<string, object>
        {
            ["Enabled"] = "not-a-bool"
        });

        var result = await _operator.ExecuteAsync(op, new Dictionary<string, object> { ["Image"] = image });

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Enabled");
        result.ErrorMessage.Should().Contain("boolean");
    }

    [Fact]
    public void ValidateParameters_ShouldRejectHardenedBoundaryViolations()
    {
        var op = CreateOperator(new Dictionary<string, object>
        {
            ["MinChangePixels"] = -1,
            ["CooldownMs"] = 60_001,
            ["RoiX"] = -1,
            ["BlurSize"] = 4,
            ["NormalizeMode"] = "Invalid"
        });

        var validation = _operator.ValidateParameters(op);

        validation.IsValid.Should().BeFalse();
        string.Join("; ", validation.Errors).Should().Contain("MinChangePixels");
        string.Join("; ", validation.Errors).Should().Contain("CooldownMs");
        string.Join("; ", validation.Errors).Should().Contain("RoiX");
        string.Join("; ", validation.Errors).Should().Contain("BlurSize");
        string.Join("; ", validation.Errors).Should().Contain("NormalizeMode");
    }

    [Fact]
    public async Task ExecuteAsync_WithinCooldown_ShouldShortCircuitDuplicateArrival()
    {
        using var first = CreateGrayImage(16, 16, 10);
        using var second = CreateGrayImage(16, 16, 240);
        using var third = CreateGrayImage(16, 16, 20);
        var op = CreateOperator(new Dictionary<string, object>
        {
            ["CooldownMs"] = 10_000,
            ["MinChangeRatio"] = 0.1,
            ["MinChangePixels"] = 10
        });

        await _operator.ExecuteAsync(op, new Dictionary<string, object> { ["Image"] = first });
        var triggered = await _operator.ExecuteAsync(op, new Dictionary<string, object> { ["Image"] = second });
        var duplicate = await _operator.ExecuteAsync(op, new Dictionary<string, object> { ["Image"] = third });

        triggered.ShouldShortCircuitFlow.Should().BeFalse();
        duplicate.IsSuccess.Should().BeTrue(duplicate.ErrorMessage);
        duplicate.ShouldShortCircuitFlow.Should().BeTrue();
        duplicate.OutputData!["Reason"].Should().Be("cooldown");
    }

    [Fact]
    public async Task ExecuteAsync_DifferentOperatorInstances_ShouldKeepIndependentBaselines()
    {
        using var dark = CreateGrayImage(16, 16, 10);
        using var brightForSecondOperator = CreateGrayImage(16, 16, 240);
        using var brightForFirstOperator = CreateGrayImage(16, 16, 240);
        var parameters = new Dictionary<string, object>
        {
            ["CooldownMs"] = 0,
            ["MinChangeRatio"] = 0.1,
            ["MinChangePixels"] = 10
        };
        var firstOperator = CreateOperator(parameters);
        var secondOperator = CreateOperator(parameters);

        await _operator.ExecuteAsync(firstOperator, new Dictionary<string, object> { ["Image"] = dark });
        var secondOperatorFirstFrame = await _operator.ExecuteAsync(secondOperator, new Dictionary<string, object> { ["Image"] = brightForSecondOperator });
        var firstOperatorSecondFrame = await _operator.ExecuteAsync(firstOperator, new Dictionary<string, object> { ["Image"] = brightForFirstOperator });

        secondOperatorFirstFrame.IsSuccess.Should().BeTrue(secondOperatorFirstFrame.ErrorMessage);
        secondOperatorFirstFrame.ShouldShortCircuitFlow.Should().BeTrue();
        secondOperatorFirstFrame.OutputData!["Triggered"].Should().Be(false);
        secondOperatorFirstFrame.OutputData["Reason"].Should().Be("baseline");

        firstOperatorSecondFrame.IsSuccess.Should().BeTrue(firstOperatorSecondFrame.ErrorMessage);
        firstOperatorSecondFrame.ShouldShortCircuitFlow.Should().BeFalse();
        firstOperatorSecondFrame.OutputData!["Triggered"].Should().Be(true);
        firstOperatorSecondFrame.OutputData["Reason"].Should().Be("change_detected");
    }

    private static Operator CreateOperator(Dictionary<string, object>? parameters = null)
    {
        var op = new Operator("FrameChangeTrigger", OperatorType.FrameChangeTrigger, 0, 0);
        if (parameters == null)
        {
            return op;
        }

        foreach (var (name, value) in parameters)
        {
            op.AddParameter(new Parameter(Guid.NewGuid(), name, name, string.Empty, value.GetType().Name, value));
        }

        return op;
    }

    private static ImageWrapper CreateGrayImage(int width, int height, byte value)
    {
        var mat = new Mat(height, width, MatType.CV_8UC1, new Scalar(value));
        return new ImageWrapper(mat);
    }
}
