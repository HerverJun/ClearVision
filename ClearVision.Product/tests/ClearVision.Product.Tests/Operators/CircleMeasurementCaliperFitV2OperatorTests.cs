using ClearVision.Product.Application.DTOs;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.ValueObjects;
using ClearVision.Product.Infrastructure.Operators;
using ClearVision.Product.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using OpenCvSharp;
using System.Text.Json;

namespace ClearVision.Product.Tests.Operators;

public class CircleMeasurementCaliperFitV2OperatorTests
{
    [Fact]
    public async Task ExecuteAsync_WithCaliperFitV2_ShouldMapLegacyAndAdditiveOutputs()
    {
        var sut = new CircleMeasurementOperator(Substitute.For<ILogger<CircleMeasurementOperator>>());
        var op = CreateV2Operator(centerX: 160.25, centerY: 120.75, radius: 55.4);

        using var image = IndustrialMeasurementSceneFactory.CreateFilledCircleImage(
            320,
            240,
            new Point2d(160.25, 120.75),
            55.4,
            supersample: 16);
        var result = await sut.ExecuteAsync(op, TestHelpers.CreateImageInputs(image));

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.OutputData.Should().NotBeNull();
        result.OutputData!["StatusCode"].Should().Be("OK");
        result.OutputData["Method"].Should().Be("CaliperFitV2");
        result.OutputData["CircleCount"].Should().Be(1);
        result.OutputData["Circle"].Should().BeOfType<CircleData>();
        result.OutputData["Center"].Should().BeOfType<Position>();
        Convert.ToDouble(result.OutputData["Radius"]).Should().BeApproximately(55.4, 0.60);
        Convert.ToDouble(result.OutputData["ResidualRmse"]).Should().BeLessThan(0.80);
        Convert.ToDouble(result.OutputData["Confidence"]).Should().BeGreaterThan(0.25);
        double.IsFinite(Convert.ToDouble(result.OutputData["UncertaintyPx"])).Should().BeTrue();

        var v2 = result.OutputData["CaliperFitV2Result"].Should().BeOfType<CircleCaliperFitV2Result>().Subject;
        v2.Success.Should().BeTrue();
        v2.ContractVersion.Should().Be(CircleCaliperFitV2Request.ContractVersionValue);
        result.OutputData["EdgePoints"].Should().BeAssignableTo<IReadOnlyList<Position>>().Which.Should().NotBeEmpty();
        result.OutputData["InlierPoints"].Should().BeAssignableTo<IReadOnlyList<Position>>().Which.Should().NotBeEmpty();
        result.OutputData["OutlierPoints"].Should().BeAssignableTo<IReadOnlyList<Position>>();
        result.OutputData["CaliperDiagnostics"].Should().BeAssignableTo<IReadOnlyList<CircleCaliperFitV2Diagnostic>>();
        result.OutputData["CaliperProfileEvidence"].Should().BeAssignableTo<IReadOnlyList<CircleCaliperFitV2ProfileEvidence>>()
            .Which.Should().HaveCount(CircleCaliperFitV2Request.MaxProfileEvidenceCount);
    }

    [Fact]
    public async Task ExecuteAsync_WithCaliperFitV2Failure_ShouldNotFallbackOrEmitFakeCircle()
    {
        var sut = new CircleMeasurementOperator(Substitute.For<ILogger<CircleMeasurementOperator>>());
        var op = CreateV2Operator(centerX: 100, centerY: 100, radius: 42);

        using var image = TestHelpers.CreateGrayTestImage(220, 220, 0);
        var result = await sut.ExecuteAsync(op, TestHelpers.CreateImageInputs(image));

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().StartWith("[InsufficientEdges] CaliperFitV2:");
        result.OutputData.Should().NotBeNull();
        result.OutputData!.Keys.Should().NotContain("Center");
        result.OutputData.Keys.Should().NotContain("Radius");
        result.OutputData.Keys.Should().NotContain("Circle");
        result.OutputData.Keys.Should().NotContain("CircleCount");
        result.OutputData["StatusCode"].Should().Be("InsufficientEdges");
        var v2 = result.OutputData["CaliperFitV2Result"].Should().BeOfType<CircleCaliperFitV2Result>().Subject;
        v2.Success.Should().BeFalse();
        v2.FailureCode.Should().Be(CircleCaliperFitV2FailureCode.InsufficientEdges);
    }

    [Fact]
    public async Task ExecuteAsync_WithCaliperFitV2CoverageFailure_ShouldMapEvidencePoints()
    {
        var sut = new CircleMeasurementOperator(Substitute.For<ILogger<CircleMeasurementOperator>>());
        var op = CreateV2Operator(centerX: 160, centerY: 120, radius: 56);
        op.Parameters.First(parameter => parameter.Name == "MinCoverageRatio").SetValue(0.80);
        op.Parameters.First(parameter => parameter.Name == "MinAngularCoverageDegrees").SetValue(260.0);

        using var image = IndustrialMeasurementSceneFactory.CreateFilledCircleImage(
            320,
            240,
            new Point2d(160, 120),
            56,
            supersample: 8);
        EraseSector(image.GetMat(), 160, 120, 72, startDegrees: 0, endDegrees: 165, color: 0);

        var result = await sut.ExecuteAsync(op, TestHelpers.CreateImageInputs(image));

        result.IsSuccess.Should().BeFalse();
        result.OutputData.Should().NotBeNull();
        result.OutputData!.Keys.Should().NotContain("Center");
        result.OutputData.Keys.Should().NotContain("Radius");
        result.OutputData.Keys.Should().NotContain("Circle");
        result.OutputData.Keys.Should().NotContain("CircleCount");
        result.OutputData["EdgePoints"].Should().BeAssignableTo<IReadOnlyList<Position>>().Which.Should().NotBeEmpty();
        result.OutputData["InlierPoints"].Should().BeAssignableTo<IReadOnlyList<Position>>().Which.Should().NotBeEmpty();

        var v2 = result.OutputData["CaliperFitV2Result"].Should().BeOfType<CircleCaliperFitV2Result>().Subject;
        v2.FailureCode.Should().Be(CircleCaliperFitV2FailureCode.InsufficientCoverage);
        v2.CoverageRatio.Should().BeGreaterThan(0);
        v2.AngularCoverageDegrees.Should().BeGreaterThan(0);
        v2.CenterX.Should().BeNull();
        v2.Radius.Should().BeNull();
    }

    [Fact]
    public async Task ExecuteAsync_WithHoughCircle_ShouldIgnoreCaliperFitV2Parameters()
    {
        var sut = new CircleMeasurementOperator(Substitute.For<ILogger<CircleMeasurementOperator>>());
        var baseline = new Operator("circle-hough-baseline", OperatorType.CircleMeasurement, 0, 0);
        baseline.AddParameter(TestHelpers.CreateParameter("Method", "HoughCircle", "enum"));
        baseline.AddParameter(TestHelpers.CreateParameter("MinRadius", 52, "int"));
        baseline.AddParameter(TestHelpers.CreateParameter("MaxRadius", 68, "int"));
        baseline.AddParameter(TestHelpers.CreateParameter("Param2", 20.0, "double"));

        var withV2Params = new Operator("circle-hough-v2-params", OperatorType.CircleMeasurement, 0, 0);
        withV2Params.AddParameter(TestHelpers.CreateParameter("Method", "HoughCircle", "enum"));
        withV2Params.AddParameter(TestHelpers.CreateParameter("MinRadius", 52, "int"));
        withV2Params.AddParameter(TestHelpers.CreateParameter("MaxRadius", 68, "int"));
        withV2Params.AddParameter(TestHelpers.CreateParameter("Param2", 20.0, "double"));
        withV2Params.AddParameter(TestHelpers.CreateParameter("SearchCenterMode", "Explicit", "enum"));
        withV2Params.AddParameter(TestHelpers.CreateParameter("SearchCenterX", 1.0, "double"));
        withV2Params.AddParameter(TestHelpers.CreateParameter("SearchCenterY", 2.0, "double"));
        withV2Params.AddParameter(TestHelpers.CreateParameter("NominalRadius", 12.0, "double"));
        withV2Params.AddParameter(TestHelpers.CreateParameter("CaliperCount", 3, "int"));
        withV2Params.AddParameter(TestHelpers.CreateParameter("EdgePolarity", "DarkToLight", "enum"));

        using var baselineImage = TestHelpers.CreateGrayShapeTestImage();
        using var v2ParamImage = TestHelpers.CreateGrayShapeTestImage();
        var baselineResult = await sut.ExecuteAsync(baseline, TestHelpers.CreateImageInputs(baselineImage));
        var withV2ParamsResult = await sut.ExecuteAsync(withV2Params, TestHelpers.CreateImageInputs(v2ParamImage));

        baselineResult.IsSuccess.Should().BeTrue(baselineResult.ErrorMessage);
        withV2ParamsResult.IsSuccess.Should().BeTrue(withV2ParamsResult.ErrorMessage);
        ((Position)withV2ParamsResult.OutputData!["Center"]).X.Should().Be(((Position)baselineResult.OutputData!["Center"]).X);
        ((Position)withV2ParamsResult.OutputData["Center"]).Y.Should().Be(((Position)baselineResult.OutputData["Center"]).Y);
        Convert.ToDouble(withV2ParamsResult.OutputData["Radius"]).Should().Be(Convert.ToDouble(baselineResult.OutputData["Radius"]));
        withV2ParamsResult.OutputData.Keys.Should().NotContain("CaliperFitV2Result");
    }

    [Fact]
    public void ValidateParameters_WithCaliperFitV2_ShouldValidateV2OnlyInputs()
    {
        var sut = new CircleMeasurementOperator(Substitute.For<ILogger<CircleMeasurementOperator>>());
        var valid = CreateV2Operator(centerX: 100, centerY: 100, radius: 42);
        valid.AddParameter(TestHelpers.CreateParameter("Dp", 999.0, "double"));
        valid.AddParameter(TestHelpers.CreateParameter("Param1", -50.0, "double"));
        valid.AddParameter(TestHelpers.CreateParameter("Param2", 999.0, "double"));
        sut.ValidateParameters(valid).IsValid.Should().BeTrue();

        var invalid = CreateV2Operator(centerX: 100, centerY: 100, radius: 42);
        invalid.Parameters.First(parameter => parameter.Name == "SearchCenterMode").SetValue("Magic");
        sut.ValidateParameters(invalid).IsValid.Should().BeFalse();

        var invalidNumericEnum = CreateV2Operator(centerX: 100, centerY: 100, radius: 42);
        invalidNumericEnum.Parameters.First(parameter => parameter.Name == "EdgePolarity").SetValue("999");
        sut.ValidateParameters(invalidNumericEnum).IsValid.Should().BeFalse();

        var overBudget = CreateV2Operator(centerX: 100, centerY: 100, radius: 42);
        overBudget.Parameters.First(parameter => parameter.Name == "CaliperCount").SetValue(720);
        overBudget.Parameters.First(parameter => parameter.Name == "ProfileSampleCount").SetValue(4096);
        overBudget.Parameters.First(parameter => parameter.Name == "AveragingThickness").SetValue(3.0);
        sut.ValidateParameters(overBudget).IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_WithCaliperFitV2_ShouldIgnoreInvalidHoughOnlyParameters()
    {
        var sut = new CircleMeasurementOperator(Substitute.For<ILogger<CircleMeasurementOperator>>());
        var op = CreateV2Operator(centerX: 160.25, centerY: 120.75, radius: 55.4);
        op.AddParameter(TestHelpers.CreateParameter("Dp", 999.0, "double"));
        op.AddParameter(TestHelpers.CreateParameter("Param1", -50.0, "double"));
        op.AddParameter(TestHelpers.CreateParameter("Param2", 999.0, "double"));

        using var image = IndustrialMeasurementSceneFactory.CreateFilledCircleImage(
            320,
            240,
            new Point2d(160.25, 120.75),
            55.4,
            supersample: 16);
        var result = await sut.ExecuteAsync(op, TestHelpers.CreateImageInputs(image));

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.OutputData!["Method"].Should().Be("CaliperFitV2");
        result.OutputData["CaliperFitV2Result"].Should().BeOfType<CircleCaliperFitV2Result>().Subject.Success.Should().BeTrue();
    }

    [Fact]
    public void ValidateParameters_WithFitEllipse_ShouldIgnoreHoughAndV2OnlyParameters()
    {
        var sut = new CircleMeasurementOperator(Substitute.For<ILogger<CircleMeasurementOperator>>());
        var op = new Operator("circle-ellipse", OperatorType.CircleMeasurement, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("Method", "FitEllipse", "enum"));
        op.AddParameter(TestHelpers.CreateParameter("MinRadius", 10, "int"));
        op.AddParameter(TestHelpers.CreateParameter("MaxRadius", 100, "int"));
        op.AddParameter(TestHelpers.CreateParameter("Dp", 999.0, "double"));
        op.AddParameter(TestHelpers.CreateParameter("Param1", -50.0, "double"));
        op.AddParameter(TestHelpers.CreateParameter("SearchCenterMode", "Magic", "enum"));
        op.AddParameter(TestHelpers.CreateParameter("NominalRadius", -100.0, "double"));

        sut.ValidateParameters(op).IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_WithoutMethodParameter_ShouldKeepHoughCircleDefault()
    {
        var sut = new CircleMeasurementOperator(Substitute.For<ILogger<CircleMeasurementOperator>>());
        var op = new Operator("circle-default", OperatorType.CircleMeasurement, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("MinRadius", 52, "int"));
        op.AddParameter(TestHelpers.CreateParameter("MaxRadius", 68, "int"));
        op.AddParameter(TestHelpers.CreateParameter("Param2", 20.0, "double"));

        using var image = TestHelpers.CreateGrayShapeTestImage();
        var result = await sut.ExecuteAsync(op, TestHelpers.CreateImageInputs(image));

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.OutputData!["Method"].Should().Be("HoughCircle");
        result.OutputData.Keys.Should().NotContain("CaliperFitV2Result");
    }

    [Fact]
    public async Task ExecuteAsync_WithCaliperFitV2Cancellation_ShouldPropagateOperationCanceledException()
    {
        var sut = new CircleMeasurementOperator(Substitute.For<ILogger<CircleMeasurementOperator>>());
        var op = CreateV2Operator(centerX: 160, centerY: 120, radius: 56);
        using var image = IndustrialMeasurementSceneFactory.CreateFilledCircleImage(
            320,
            240,
            new Point2d(160, 120),
            56,
            supersample: 8);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = async () => await sut.ExecuteAsync(op, TestHelpers.CreateImageInputs(image), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public void Metadata_ShouldExposeCaliperFitV2ContractAdditively()
    {
        var metadata = new OperatorMetadataScanner().Scan().Single(item => item.Type == OperatorType.CircleMeasurement);

        metadata.Version.Should().Be("1.1.2");
        metadata.OutputPorts.Select(port => port.Name).Should().Contain(new[]
        {
            "CaliperFitV2Result",
            "EdgePoints",
            "InlierPoints",
            "OutlierPoints",
            "CaliperDiagnostics",
            "CaliperProfileEvidence"
        });
        metadata.Parameters.First(parameter => parameter.Name == "Method")
            .Options!.Select(option => option.Value)
            .Should().Contain("CaliperFitV2");
        metadata.Parameters.Select(parameter => parameter.Name).Should().Contain(new[]
        {
            "SearchCenterMode",
            "NominalRadius",
            "CaliperCount",
            "EdgePolarity",
            "OutlierMode",
            "MaxResidualRmse"
        });
    }

    [Fact]
    public void CircleMeasurementMethods_ShouldReloadFromSavedFlowJson()
    {
        var savedFlow = new OperatorFlowDto
        {
            Name = "circle-method-compat",
            Operators =
            [
                ToDto(CreateMethodOperator("hough", "HoughCircle")),
                ToDto(CreateMethodOperator("ellipse", "FitEllipse")),
                ToDto(CreateV2Operator(centerX: 160.25, centerY: 120.75, radius: 55.4))
            ]
        };

        var json = JsonSerializer.Serialize(savedFlow);
        var reloadedFlow = JsonSerializer.Deserialize<OperatorFlowDto>(json)!.ToEntity();
        var operators = reloadedFlow.Operators;

        operators.Should().HaveCount(3);
        ReadParameterValue(operators[0], "Method").Should().Be("HoughCircle");
        ReadParameterValue(operators[1], "Method").Should().Be("FitEllipse");
        ReadParameterValue(operators[2], "Method").Should().Be("CaliperFitV2");
        ReadParameterValue(operators[2], "SearchCenterMode").Should().Be("Explicit");
        ReadParameterValue(operators[2], "EdgePolarity").Should().Be("LightToDark");
        ReadParameterValue(operators[2], "OutlierMode").Should().Be("Mad");
        ReadParameterValue(operators[2], "CaliperCount").Should().Be(96);
        ReadParameterValue(operators[2], "ProfileSampleCount").Should().Be(129);
        ReadParameterValue(operators[2], "MinCoverageRatio").Should().Be(0.35);
    }

    private static Operator CreateV2Operator(double centerX, double centerY, double radius)
    {
        var op = new Operator("circle-v2", OperatorType.CircleMeasurement, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("Method", "CaliperFitV2", "enum"));
        op.AddParameter(TestHelpers.CreateParameter("MinRadius", (int)Math.Floor(radius - 8), "int"));
        op.AddParameter(TestHelpers.CreateParameter("MaxRadius", (int)Math.Ceiling(radius + 8), "int"));
        op.AddParameter(TestHelpers.CreateParameter("SearchCenterMode", "Explicit", "enum"));
        op.AddParameter(TestHelpers.CreateParameter("SearchCenterX", centerX, "double"));
        op.AddParameter(TestHelpers.CreateParameter("SearchCenterY", centerY, "double"));
        op.AddParameter(TestHelpers.CreateParameter("NominalRadius", radius, "double"));
        op.AddParameter(TestHelpers.CreateParameter("CaliperCount", 96, "int"));
        op.AddParameter(TestHelpers.CreateParameter("AveragingThickness", 5.0, "double"));
        op.AddParameter(TestHelpers.CreateParameter("ProfileSampleCount", 129, "int"));
        op.AddParameter(TestHelpers.CreateParameter("GaussianSigma", 1.2, "double"));
        op.AddParameter(TestHelpers.CreateParameter("EdgePolarity", "LightToDark", "enum"));
        op.AddParameter(TestHelpers.CreateParameter("EdgeThreshold", 0.0, "double"));
        op.AddParameter(TestHelpers.CreateParameter("MinEdgeStrength", 4.0, "double"));
        op.AddParameter(TestHelpers.CreateParameter("MinValidCalipers", 28, "int"));
        op.AddParameter(TestHelpers.CreateParameter("MinCoverageRatio", 0.35, "double"));
        op.AddParameter(TestHelpers.CreateParameter("MinAngularCoverageDegrees", 180.0, "double"));
        op.AddParameter(TestHelpers.CreateParameter("OutlierMode", "Mad", "enum"));
        op.AddParameter(TestHelpers.CreateParameter("OutlierThreshold", 3.5, "double"));
        op.AddParameter(TestHelpers.CreateParameter("MaxOutlierIterations", 3, "int"));
        op.AddParameter(TestHelpers.CreateParameter("MaxResidualRmse", 1.4, "double"));
        return op;
    }

    private static Operator CreateMethodOperator(string name, string method)
    {
        var op = new Operator(name, OperatorType.CircleMeasurement, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("Method", method, "enum"));
        return op;
    }

    private static OperatorDto ToDto(Operator op)
    {
        return new OperatorDto
        {
            Id = op.Id,
            Name = op.Name,
            Type = op.Type,
            X = op.Position.X,
            Y = op.Position.Y,
            IsEnabled = op.IsEnabled,
            Parameters = op.Parameters.Select(parameter => new ParameterDto
            {
                Id = parameter.Id,
                Name = parameter.Name,
                DisplayName = parameter.DisplayName,
                Description = parameter.Description,
                DataType = parameter.DataType,
                DefaultValue = parameter.DefaultValue,
                Value = parameter.GetValue(),
                MinValue = parameter.MinValue,
                MaxValue = parameter.MaxValue,
                IsRequired = parameter.IsRequired,
                Options = parameter.Options
            }).ToList()
        };
    }

    private static object? ReadParameterValue(Operator op, string parameterName)
    {
        var value = op.Parameters.Single(parameter => parameter.Name == parameterName).GetValue();
        if (value is JsonElement jsonElement)
        {
            return jsonElement.ValueKind switch
            {
                JsonValueKind.String => jsonElement.GetString(),
                JsonValueKind.Number => jsonElement.TryGetInt64(out var longValue)
                    ? longValue
                    : jsonElement.GetDouble(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Null => null,
                _ => jsonElement.GetRawText()
            };
        }

        return value switch
        {
            int intValue => intValue,
            long longValue => longValue,
            double doubleValue => doubleValue,
            string stringValue => stringValue,
            _ => value
        };
    }

    private static void EraseSector(Mat gray, double centerX, double centerY, double radius, double startDegrees, double endDegrees, byte color)
    {
        var points = new List<Point> { new((int)Math.Round(centerX), (int)Math.Round(centerY)) };
        for (var angle = startDegrees; angle <= endDegrees; angle += 3.0)
        {
            var radians = angle * Math.PI / 180.0;
            points.Add(new Point(
                (int)Math.Round(centerX + (Math.Cos(radians) * radius)),
                (int)Math.Round(centerY + (Math.Sin(radians) * radius))));
        }

        Cv2.FillConvexPoly(gray, points, new Scalar(color), LineTypes.AntiAlias);
    }
}
