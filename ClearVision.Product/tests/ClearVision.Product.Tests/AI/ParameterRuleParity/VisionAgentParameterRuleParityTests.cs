using System.Text.Json;
using ClearVision.Product.Core.AI.Tools;
using ClearVision.Product.Infrastructure.AI.Tools;
using FluentAssertions;

namespace ClearVision.Product.Tests.AI.ParameterRuleParity;

public sealed class VisionAgentParameterRuleParityTests
{
    [Fact(DisplayName = "parameter rule parity spec should cover required Vision Agent operator families")]
    public void SharedSpec_ShouldCoverRequiredRuleFamilies()
    {
        var spec = LoadSpec();

        spec.SchemaVersion.Should().Be("2026-06-05.vision-agent-parameter-rule-parity.v1");
        spec.Cases.Select(item => item.OperatorType).Should().Contain([
            "ImageAcquisition",
            "TemplateMatching",
            "DeepLearning",
            "ResultOutput"]);
        spec.Cases.Should().Contain(item => item.CaseId == "image_camera_requires_camera_id_or_binding");
        spec.Cases.Should().Contain(item => item.CaseId == "result_output_plc_requires_address_or_parameters");
    }

    [Fact(DisplayName = "validate_flow should match shared parameter rule parity spec")]
    public async Task FlowValidation_ShouldMatchSharedParitySpec()
    {
        foreach (var testCase in LoadSpec().Cases)
        {
            var payload = await ValidateAsync(testCase);

            MissingParameters(payload).Should().BeEquivalentTo(
                testCase.FlowValidationMissingParameters,
                because: testCase.CaseId);
        }
    }

    [Fact(DisplayName = "runtime_package_precheck should match shared parameter rule parity spec")]
    public async Task DeploymentPrecheck_ShouldMatchSharedParitySpec()
    {
        foreach (var testCase in LoadSpec().Cases)
        {
            var validationSummary = await ValidateAsync(testCase);
            var precheck = await new RuntimePackagePrecheckTool().ExecuteAsync(
                new VisionAgentToolContext(),
                Args(
                    ("flow", BuildFlow(testCase)),
                    ("validationSummary", validationSummary),
                    ("dryRunSummary", new
                    {
                        dryRunSucceeded = true,
                        warnings = Array.Empty<object>(),
                        blockingIssues = Array.Empty<object>()
                    })),
                CancellationToken.None);

            precheck.Success.Should().BeTrue(testCase.CaseId);
            MissingParameters(Json(precheck.Data)).Should().BeEquivalentTo(
                testCase.DeploymentPrecheckMissingParameters,
                because: testCase.CaseId);
        }
    }

    [Fact(DisplayName = "executable benchmark source guard should exclude real hardware process and network APIs")]
    public void ExecutableBenchmark_SourceGuard_ShouldExcludeRuntimeHardwareProcessAndNetworkApis()
    {
        var source = File.ReadAllText(Path.Combine(
            GetRepoRoot(),
            "quality",
            "tools",
            "VisionAgentBusinessBenchmarkRunner",
            "Program.cs"));
        var forbiddenFragments = new[]
        {
            ".jpg",
            ".jpeg",
            ".png",
            ".bmp",
            ".tif",
            ".tiff",
            "AcquireSingleFrameAsync",
            "EnumerateCamerasAsync",
            "GetOrCreateByBindingAsync",
            "CameraTestFrameTool",
            "ReplayFlowWithFrameTool",
            "HttpClient",
            "TcpClient",
            "Socket",
            "System.Net.",
            "File.ReadAllBytes",
            "Image.FromFile",
            "Cv2.ImRead",
            "Process.Start",
            "System.Diagnostics.Process",
            "deploy_runtime_package",
            "hot_reload",
            "plc_write",
            "Acme.Product."
        };

        foreach (var fragment in forbiddenFragments)
        {
            source.Should().NotContain(fragment);
        }
    }

    [Fact(DisplayName = "executable benchmark report should expose actual execution result fields")]
    public void ExecutableBenchmarkReport_ShouldExposeActualExecutionFields()
    {
        var reportPath = Path.Combine(
            GetRepoRoot(),
            "quality",
            "evals",
            "reports",
            "VisionAgent_business_benchmark_baseline.json");

        File.Exists(reportPath).Should().BeTrue("the executable benchmark report is a quality artifact");
        using var doc = JsonDocument.Parse(File.ReadAllText(reportPath));
        var root = doc.RootElement;
        root.GetProperty("benchmarkId").GetString().Should().Be("vision_agent_executable_business_benchmark");
        root.GetProperty("mode").GetString().Should().Be("offline_metadata_only");
        root.GetProperty("summary").GetProperty("caseCount").GetInt32().Should().BeGreaterThanOrEqualTo(55);
        root.GetProperty("summary").GetProperty("accepted").GetBoolean().Should().BeTrue();

        var cases = root.GetProperty("cases").EnumerateArray().ToList();
        foreach (var item in cases)
        {
            HasProperty(item, "expectedBusinessActions").Should().BeTrue(item.GetProperty("caseId").GetString());
            HasProperty(item, "expectedToolCalls").Should().BeTrue(item.GetProperty("caseId").GetString());
            HasProperty(item, "actualToolCalls").Should().BeTrue(item.GetProperty("caseId").GetString());
            HasProperty(item, "actualValidationResult").Should().BeTrue(item.GetProperty("caseId").GetString());
            HasProperty(item, "actualDryRunResult").Should().BeTrue(item.GetProperty("caseId").GetString());
            HasProperty(item, "actualPrecheckResult").Should().BeTrue(item.GetProperty("caseId").GetString());
            HasProperty(item, "actualRuntimePreviewResult").Should().BeTrue(item.GetProperty("caseId").GetString());
        }

        cases.Should().OnlyContain(item => item.GetProperty("actualToolCalls").GetArrayLength() > 0);

        var expectedTools = cases
            .SelectMany(item => item.GetProperty("expectedToolCalls").EnumerateArray())
            .Select(item => item.GetString())
            .ToList();
        expectedTools.Should().NotContain([
            "list_camera_bindings",
            "propose_flow_patch",
            "propose_parameter_patch",
            "runtime_preview_metadata"]);
    }

    private static async Task<JsonElement> ValidateAsync(ParameterRuleParityCase testCase)
    {
        var result = await new FlowValidationTool().ExecuteAsync(
            new VisionAgentToolContext(),
            Args(("flow", BuildFlow(testCase))),
            CancellationToken.None);

        result.Success.Should().BeTrue(testCase.CaseId);
        return Json(result.Data);
    }

    private static bool HasProperty(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out _);
    }

    private static object BuildFlow(ParameterRuleParityCase testCase)
    {
        return new
        {
            operators = new[]
            {
                new
                {
                    tempId = "op_under_test",
                    operatorType = testCase.OperatorType,
                    parameters = testCase.Parameters
                }
            },
            connections = Array.Empty<object>()
        };
    }

    private static IReadOnlyList<string> MissingParameters(JsonElement payload)
    {
        return payload.GetProperty("missingResources")
            .EnumerateArray()
            .Select(item => item.GetProperty("parameterName").GetString() ?? string.Empty)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .ToList();
    }

    private static ParameterRuleParitySpec LoadSpec()
    {
        var path = Path.Combine(
            GetRepoRoot(),
            "quality",
            "evals",
            "specs",
            "vision_agent_parameter_rule_parity_cases.json");
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<ParameterRuleParitySpec>(json, JsonOptions)
            ?? throw new InvalidOperationException("Parameter rule parity spec could not be loaded.");
    }

    private static JsonElement Args(params (string Key, object? Value)[] values)
    {
        var dict = values.ToDictionary(
            item => item.Key,
            item => item.Value,
            StringComparer.OrdinalIgnoreCase);
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(dict, JsonOptions));
        return doc.RootElement.Clone();
    }

    private static JsonElement Json(object? value)
    {
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(value, JsonOptions));
        return doc.RootElement.Clone();
    }

    private static string GetRepoRoot()
    {
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../.."));
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private sealed record ParameterRuleParitySpec
    {
        public string SchemaVersion { get; init; } = string.Empty;
        public IReadOnlyList<ParameterRuleParityCase> Cases { get; init; } = [];
    }

    private sealed record ParameterRuleParityCase
    {
        public string CaseId { get; init; } = string.Empty;
        public string OperatorType { get; init; } = string.Empty;
        public IReadOnlyDictionary<string, JsonElement> Parameters { get; init; } =
            new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        public IReadOnlyList<string> FlowValidationMissingParameters { get; init; } = [];
        public IReadOnlyList<string> DeploymentPrecheckMissingParameters { get; init; } = [];
    }
}
