using System.Text.Json;
using ClearVision.Product.Core.AI.Tools;
using ClearVision.Product.Infrastructure.AI.Tools;
using FluentAssertions;

namespace ClearVision.Product.Tests.AI.VisionAgentSimulationTools;

[TestClassification(TestDomain.Ai, TestPurpose.Regression, TestLane.Nightly, TestEvidenceType.Contract, TestOracleType.Contract, TestResourceRequirement.None, TestExpectedDuration.Medium, TestFlakyPolicy.Blocking, "vision-agent")]
public sealed class VisionAgentFlowContractValidatorTests
{
    [Fact(DisplayName = "Flow contract validator should reject unknown operator type")]
    public async Task ValidateFlow_ShouldRejectUnknownOperatorType()
    {
        var payload = await ValidateAsync(new
        {
            operators = new[] { Operator("op_magic", "QuantumScratchMagic") },
            connections = Array.Empty<object>()
        });

        Codes(payload, "blockingIssues").Should().Contain("unknown_operator");
    }

    [Fact(DisplayName = "Flow contract validator should reject unknown parameter name")]
    public async Task ValidateFlow_ShouldRejectUnknownParameterName()
    {
        var payload = await ValidateAsync(new
        {
            operators = new[]
            {
                Operator("op_judge", "ResultJudgment", new Dictionary<string, string> { ["Rule"] = "OK when true" })
            },
            connections = Array.Empty<object>()
        });

        Codes(payload, "blockingIssues").Should().Contain("unknown_parameter");
    }

    [Fact(DisplayName = "Flow contract validator should reject missing and incompatible ports")]
    public async Task ValidateFlow_ShouldRejectMissingAndIncompatiblePorts()
    {
        var payload = await ValidateAsync(new
        {
            operators = new[]
            {
                Operator("op_cam", "ImageAcquisition", new Dictionary<string, string> { ["SourceType"] = "Camera", ["CameraId"] = "cam_1" }),
                Operator("op_match", "TemplateMatching"),
                Operator("op_blob", "BlobAnalysis")
            },
            connections = new object[]
            {
                Connection("op_cam", "Image", "op_match", "Image"),
                Connection("op_cam", "Image", "op_match", "Template"),
                Connection("op_match", "Score", "op_blob", "Image"),
                Connection("op_match", "NotARealPort", "op_blob", "SourceImage")
            }
        });

        var codes = Codes(payload, "blockingIssues");
        codes.Should().Contain("missing_port");
        codes.Should().Contain("incompatible_port_type");
    }

    [Fact(DisplayName = "Flow contract validator should classify missing template input without fake TemplatePath")]
    public async Task ValidateFlow_ShouldClassifyMissingTemplateInputWithoutFakeParameter()
    {
        var payload = await ValidateAsync(new
        {
            operators = new[]
            {
                Operator("op_cam", "ImageAcquisition", new Dictionary<string, string> { ["SourceType"] = "Camera", ["CameraId"] = "cam_1" }),
                Operator("op_match", "TemplateMatching")
            },
            connections = new object[]
            {
                Connection("op_cam", "Image", "op_match", "Image")
            }
        });

        payload.GetProperty("isValid").GetBoolean().Should().BeTrue();
        Codes(payload, "warnings").Should().Contain("missing_template_resource");
        var resources = payload.GetProperty("missingResources").EnumerateArray().ToList();
        resources.Should().Contain(item =>
            item.GetProperty("resourceType").GetString() == "template_artifact" &&
            item.GetProperty("parameterName").GetString() == "Template");
        resources.Should().NotContain(item =>
            item.GetProperty("parameterName").GetString() == "TemplatePath");
    }

    [Fact(DisplayName = "Flow contract validator should canonicalize parameter casing for runtime readers")]
    public async Task ValidateFlow_ShouldCanonicalizeParameterCasingForRuntimeReaders()
    {
        var payload = await ValidateAsync(new
        {
            operators = new[]
            {
                Operator("op_note", "Comment", new Dictionary<string, string> { ["text"] = "metadata-only note" })
            },
            connections = Array.Empty<object>()
        });

        Codes(payload, "blockingIssues").Should().NotContain("unknown_parameter");
        var parameters = payload
            .GetProperty("canonicalFlow")
            .GetProperty("operators")
            .EnumerateArray()
            .Single()
            .GetProperty("parameters");
        parameters.TryGetProperty("Text", out var text).Should().BeTrue();
        text.GetString().Should().Be("metadata-only note");
        parameters.TryGetProperty("text", out _).Should().BeFalse();
    }

    [Fact(DisplayName = "Flow contract validator should canonicalize legacy MeasureDistance to Measurement")]
    public async Task ValidateFlow_ShouldCanonicalizeLegacyMeasureDistance()
    {
        var dryRun = await new DryRunFlowTool().ExecuteAsync(
            new VisionAgentToolContext(),
            Args(("flow", new
            {
                operators = new object[]
                {
                    Operator("op_cam", "ImageAcquisition", new Dictionary<string, string> { ["SourceType"] = "Camera", ["CameraId"] = "cam_1" }),
                    Operator("op_circle_a", "CircleMeasurement"),
                    Operator("op_circle_b", "CircleMeasurement"),
                    Operator("op_distance", "MeasureDistance")
                },
                connections = new object[]
                {
                    Connection("op_cam", "Image", "op_circle_a", "Image"),
                    Connection("op_cam", "Image", "op_circle_b", "Image"),
                    Connection("op_circle_a", "Center", "op_distance", "PointA"),
                    Connection("op_circle_b", "Center", "op_distance", "PointB")
                }
            })),
            CancellationToken.None);

        dryRun.Success.Should().BeTrue();
        var payload = Json(dryRun.Data);
        payload.GetProperty("dryRunSucceeded").GetBoolean().Should().BeTrue();
        payload.GetProperty("executedOperators").EnumerateArray()
            .Should().Contain(item =>
                item.GetProperty("tempId").GetString() == "op_distance" &&
                item.GetProperty("operatorType").GetString() == "Measurement");
    }

    private static async Task<JsonElement> ValidateAsync(object flow)
    {
        var result = await new FlowValidationTool().ExecuteAsync(
            new VisionAgentToolContext(),
            Args(("flow", flow)),
            CancellationToken.None);
        result.Success.Should().BeTrue();
        return Json(result.Data);
    }

    private static object Operator(
        string tempId,
        string operatorType,
        IReadOnlyDictionary<string, string>? parameters = null)
    {
        return new
        {
            tempId,
            operatorType,
            parameters = parameters ?? new Dictionary<string, string>()
        };
    }

    private static object Connection(
        string sourceTempId,
        string sourcePortName,
        string targetTempId,
        string targetPortName)
    {
        return new
        {
            sourceTempId,
            sourcePortName,
            targetTempId,
            targetPortName
        };
    }

    private static IReadOnlyList<string> Codes(JsonElement payload, string propertyName)
    {
        return payload.GetProperty(propertyName)
            .EnumerateArray()
            .Select(issue => issue.GetProperty("code").GetString() ?? string.Empty)
            .ToList();
    }

    private static JsonElement Args(params (string Key, object? Value)[] values)
    {
        var dict = values.ToDictionary(
            item => item.Key,
            item => item.Value,
            StringComparer.OrdinalIgnoreCase);
        return JsonSerializer.SerializeToElement(dict, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
    }

    private static JsonElement Json(object? value)
    {
        return JsonSerializer.SerializeToElement(value, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });
    }
}
