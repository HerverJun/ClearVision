using System.Text.Json;
using ClearVision.Product.Core.AI.Tools;
using ClearVision.Product.Core.Services;
using ClearVision.Product.Infrastructure.AI.Tools;
using FluentAssertions;

namespace ClearVision.Product.Tests.AI.ParameterRuleParity;

[TestClassification(TestDomain.Ai, TestPurpose.Regression, TestLane.Nightly, TestEvidenceType.Contract, TestOracleType.Contract, TestResourceRequirement.None, TestExpectedDuration.Medium, TestFlakyPolicy.Blocking, "vision-agent")]
public sealed class VisionAgentParameterRuleParityTests
{
    [Fact(DisplayName = "parameter rule parity spec should cover required Vision Agent operator families")]
    public void SharedSpec_ShouldCoverRequiredRuleFamilies()
    {
        var spec = LoadSpec();

        spec.SchemaVersion.Should().Be("2026-07-13.vision-agent-parameter-rule-parity.v4");
        spec.Cases.Select(item => item.OperatorType).Should().Contain([
            "ImageAcquisition",
            "Filtering",
            "Measurement",
            "TemplateMatching",
            "DeepLearning",
            "EdgeDetection",
            "ResultOutput",
            "BlobAnalysis",
            "ImageSave",
            "TextSave",
            "MitsubishiMcCommunication",
            "TcpCommunication"]);
        spec.Cases.Should().Contain(item => item.CaseId == "image_camera_requires_camera_id_or_binding");
        spec.Cases.Should().Contain(item => item.CaseId == "result_output_only_uses_real_save_to_file_parameter");
        spec.Cases
            .Where(item => item.OperatorType == "ResultOutput")
            .Should().OnlyContain(item => item.Parameters.Keys.All(name => name == "SaveToFile"));
    }

    [Fact(DisplayName = "shared parity spec constraints should match the canonical provider facts")]
    public void SharedSpecConstraints_ShouldMatchCanonicalProviderFacts()
    {
        var spec = LoadSpec();
        var catalog = new VisionAgentOperatorContractCatalog();
        var migratedOperators = new[]
        {
            "ImageAcquisition",
            "Filtering",
            "Measurement",
            "TemplateMatching",
            "DeepLearning",
            "EdgeDetection",
            "ResultOutput",
            "BlobAnalysis",
            "ImageSave",
            "TextSave",
            "MitsubishiMcCommunication",
            "TcpCommunication"
        };

        spec.OperatorConstraints.Keys.Should().BeEquivalentTo(migratedOperators);
        foreach (var operatorType in migratedOperators)
        {
            catalog.TryGet(operatorType, out var contract).Should().BeTrue(operatorType);
            var actual = contract.ParameterConstraints!
                .Select(ParameterConstraintSpec.From)
                .Select(item => item.Identity());
            var expected = spec.OperatorConstraints[operatorType]
                .Select(item => item.Identity());

            expected.Should().Equal(actual, operatorType);
        }
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
                    }),
                    ("manualResourceConfirmations", ManualConfirmationsFor(testCase))),
                CancellationToken.None);

            precheck.Success.Should().BeTrue(testCase.CaseId);
            MissingParameters(Json(precheck.Data)).Should().BeEquivalentTo(
                testCase.DeploymentPrecheckMissingParameters,
                because: testCase.CaseId);
        }
    }

    [Fact(DisplayName = "Agent validation should keep an explicit canonical default over conflicting aliases")]
    public async Task FlowValidation_ShouldCanonicalizeAliasesAndKeepExplicitCanonicalDefaults()
    {
        var result = await new FlowValidationTool().ExecuteAsync(
            new VisionAgentToolContext(),
            Args(("flow", new
            {
                operators = new[]
                {
                    new
                    {
                        tempId = "op_camera",
                        operatorType = "ImageAcquisition",
                        parameters = new Dictionary<string, object?>(StringComparer.Ordinal)
                        {
                            ["SourceType"] = "Camera",
                            ["CameraId"] = string.Empty,
                            ["CameraBindingId"] = "binding-camera",
                            ["cameraId"] = "legacy-camera"
                        }
                    }
                },
                connections = Array.Empty<object>()
            })),
            CancellationToken.None);

        result.Success.Should().BeTrue();
        var payload = Json(result.Data);
        var parameters = payload.GetProperty("canonicalFlow")
            .GetProperty("operators")[0]
            .GetProperty("parameters");
        parameters.GetProperty("CameraId").GetString().Should().BeEmpty();
        parameters.TryGetProperty("CameraBindingId", out _).Should().BeFalse();
        parameters.TryGetProperty("cameraId", out _).Should().BeFalse();
        payload.GetProperty("warnings")
            .EnumerateArray()
            .Count(item => item.GetProperty("code").GetString() == "parameter_alias_conflict")
            .Should().Be(2);
        MissingParameters(payload).Should().ContainSingle().Which.Should().Be("CameraId");
    }

    [Theory(DisplayName = "Agent validation and precheck should share the exact pending sentinel contract")]
    [InlineData("<pending-camera-binding>", true)]
    [InlineData("todo-line-camera", false)]
    [InlineData("customer-todo-approved", false)]
    [InlineData("<pending-camera binding>", false)]
    public async Task AgentAndPrecheck_ShouldSharePendingSentinelContract(string cameraId, bool expectedMissing)
    {
        var flow = new
        {
            operators = new[]
            {
                new
                {
                    tempId = "op_camera",
                    operatorType = "ImageAcquisition",
                    parameters = new Dictionary<string, object?>
                    {
                        ["SourceType"] = "Camera",
                        ["CameraBindingId"] = cameraId
                    }
                }
            },
            connections = Array.Empty<object>()
        };
        var validationResult = await new FlowValidationTool().ExecuteAsync(
            new VisionAgentToolContext(),
            Args(("flow", flow)),
            CancellationToken.None);
        var validation = Json(validationResult.Data);
        MissingParameters(validation).Contains("CameraId").Should().Be(expectedMissing);

        var confirmations = expectedMissing
            ? Array.Empty<object>()
            : new object[]
            {
                new
                {
                    resourceType = "camera_binding",
                    operatorId = "op_camera",
                    parameterName = "CameraBindingId",
                    resourceKey = "op_camera.CameraBindingId",
                    metadataOnly = true
                }
            };
        var precheckResult = await new RuntimePackagePrecheckTool().ExecuteAsync(
            new VisionAgentToolContext(),
            Args(
                ("flow", flow),
                ("validationSummary", validation),
                ("dryRunSummary", new
                {
                    dryRunSucceeded = true,
                    warnings = Array.Empty<object>(),
                    blockingIssues = Array.Empty<object>()
                }),
                ("manualResourceConfirmations", confirmations)),
            CancellationToken.None);
        var precheck = Json(precheckResult.Data);
        MissingParameters(precheck).Contains("CameraId").Should().Be(expectedMissing);
    }

    [Theory(DisplayName = "Agent and precheck should block only active TCP parse requirements")]
    [InlineData("tcp_regex_parse_requires_pattern", true)]
    [InlineData("tcp_no_response_ignores_stale_regex_values", false)]
    public async Task AgentAndPrecheck_ShouldShareTcpActiveModeSemantics(string caseId, bool expectedBlocked)
    {
        var testCase = LoadSpec().Cases.Single(item => item.CaseId == caseId);
        var validation = await ValidateAsync(testCase);
        HasIssue(validation, "blockingIssues", "missing_conditional_parameter").Should().Be(expectedBlocked);

        var precheckResult = await new RuntimePackagePrecheckTool().ExecuteAsync(
            new VisionAgentToolContext(),
            Args(
                ("flow", BuildFlow(testCase)),
                ("validationSummary", validation),
                ("dryRunSummary", new
                {
                    dryRunSucceeded = true,
                    warnings = Array.Empty<object>(),
                    blockingIssues = Array.Empty<object>()
                }),
                ("manualResourceConfirmations", ManualConfirmationsFor(testCase))),
            CancellationToken.None);
        var precheck = Json(precheckResult.Data);

        HasIssue(precheck, "blockingIssues", "missing_conditional_parameter").Should().Be(expectedBlocked);
    }

    [Fact(DisplayName = "Agent and precheck should share TCP delimiter at-least-one groups")]
    public async Task AgentAndPrecheck_ShouldShareTcpDelimiterGroupSemantics()
    {
        var testCase = LoadSpec().Cases.Single(item =>
            item.CaseId == "tcp_key_value_requires_delimiter_groups");
        var validation = await ValidateAsync(testCase);
        HasIssue(validation, "blockingIssues", "missing_conditional_parameter_group").Should().BeTrue();

        var precheckResult = await new RuntimePackagePrecheckTool().ExecuteAsync(
            new VisionAgentToolContext(),
            Args(
                ("flow", BuildFlow(testCase)),
                ("validationSummary", validation),
                ("dryRunSummary", new
                {
                    dryRunSucceeded = true,
                    warnings = Array.Empty<object>(),
                    blockingIssues = Array.Empty<object>()
                }),
                ("manualResourceConfirmations", ManualConfirmationsFor(testCase))),
            CancellationToken.None);
        var precheck = Json(precheckResult.Data);

        HasIssue(precheck, "blockingIssues", "missing_conditional_parameter_group").Should().BeTrue();
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
            string.Concat("Ac", "me.Product.")
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

    private static bool HasIssue(JsonElement payload, string propertyName, string code)
    {
        return payload.GetProperty(propertyName)
            .EnumerateArray()
            .Any(item => item.GetProperty("code").GetString() == code);
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

    private static object[] ManualConfirmationsFor(ParameterRuleParityCase testCase)
    {
        var confirmations = new List<object>();
        AddIfConfigured(confirmations, testCase, "camera_binding", "CameraId", "CameraBindingId");
        AddIfConfigured(
            confirmations,
            testCase,
            "model_resource",
            "ModelPath",
            "ModelId",
            "EdgeModelPath",
            "EdgeModelId",
            "ModelCatalogPath");
        AddIfConfigured(confirmations, testCase, "template_artifact", "Template", "TemplateId", "TemplatePath");
        AddIfConfigured(confirmations, testCase, "output_file", "Directory", "FolderPath", "FilePath");
        AddIfConfigured(confirmations, testCase, "plc_endpoint", "IpAddress");
        AddIfConfigured(confirmations, testCase, "plc_address", "Address");
        AddIfConfigured(confirmations, testCase, "tcp_profile", "ProfileId");
        AddIfConfigured(confirmations, testCase, "network_endpoint", "IpAddress");
        return confirmations.ToArray();
    }

    private static void AddIfConfigured(
        List<object> confirmations,
        ParameterRuleParityCase testCase,
        string resourceType,
        params string[] parameterNames)
    {
        foreach (var parameterName in parameterNames)
        {
            if (!testCase.Parameters.TryGetValue(parameterName, out var rawValue))
            {
                continue;
            }

            var value = rawValue.ValueKind == JsonValueKind.String
                ? rawValue.GetString()
                : rawValue.GetRawText();
            if (OperatorParameterValueSemantics.IsMissing(value))
            {
                continue;
            }

            confirmations.Add(new
            {
                resourceType,
                operatorId = "op_under_test",
                parameterName,
                resourceKey = $"op_under_test.{parameterName}",
                metadataOnly = true
            });
            return;
        }
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
        public IReadOnlyDictionary<string, IReadOnlyList<ParameterConstraintSpec>> OperatorConstraints { get; init; } =
            new Dictionary<string, IReadOnlyList<ParameterConstraintSpec>>(StringComparer.OrdinalIgnoreCase);
        public IReadOnlyList<ParameterRuleParityCase> Cases { get; init; } = [];
    }

    private sealed record ParameterConstraintSpec
    {
        public string Parameter { get; init; } = string.Empty;
        public string RequiredPolicy { get; init; } = OperatorParameterRequiredPolicies.Metadata;
        public ParameterConditionSetSpec? RequiredWhen { get; init; }
        public ParameterConditionSetSpec? EnabledWhen { get; init; }
        public ParameterConditionSetSpec? DisabledWhen { get; init; }
        public string? AtLeastOneGroup { get; init; }
        public string? MutuallyExclusiveGroup { get; init; }
        public string? AliasFor { get; init; }
        public bool Deprecated { get; init; }
        public string? ResourceKind { get; init; }
        public string ReasonCode { get; init; } = string.Empty;

        public string Identity() => string.Join("|",
            Parameter,
            RequiredPolicy,
            RequiredWhen?.Identity() ?? string.Empty,
            EnabledWhen?.Identity() ?? string.Empty,
            DisabledWhen?.Identity() ?? string.Empty,
            AtLeastOneGroup ?? string.Empty,
            MutuallyExclusiveGroup ?? string.Empty,
            AliasFor ?? string.Empty,
            Deprecated,
            ResourceKind ?? string.Empty,
            ReasonCode);

        public static ParameterConstraintSpec From(OperatorParameterConstraint constraint) => new()
        {
            Parameter = constraint.Parameter,
            RequiredPolicy = constraint.RequiredPolicy,
            RequiredWhen = ParameterConditionSetSpec.From(constraint.RequiredWhen),
            EnabledWhen = ParameterConditionSetSpec.From(constraint.EnabledWhen),
            DisabledWhen = ParameterConditionSetSpec.From(constraint.DisabledWhen),
            AtLeastOneGroup = constraint.AtLeastOneGroup,
            MutuallyExclusiveGroup = constraint.MutuallyExclusiveGroup,
            AliasFor = constraint.AliasFor,
            Deprecated = constraint.Deprecated,
            ResourceKind = constraint.ResourceKind,
            ReasonCode = constraint.ReasonCode
        };
    }

    private sealed record ParameterConditionSetSpec
    {
        public IReadOnlyList<ParameterConditionSpec>? All { get; init; }
        public IReadOnlyList<ParameterConditionSpec>? Any { get; init; }

        public string Identity() =>
            $"all=[{string.Join(';', All?.Select(item => item.Identity()) ?? [])}];" +
            $"any=[{string.Join(';', Any?.Select(item => item.Identity()) ?? [])}]";

        public static ParameterConditionSetSpec? From(OperatorParameterConditionSet? set) => set is null
            ? null
            : new ParameterConditionSetSpec
            {
                All = set.All?.Select(ParameterConditionSpec.From).ToArray(),
                Any = set.Any?.Select(ParameterConditionSpec.From).ToArray()
            };
    }

    private sealed record ParameterConditionSpec
    {
        public string Parameter { get; init; } = string.Empty;
        public string Comparison { get; init; } = string.Empty;
        public JsonElement? Value { get; init; }

        public string Identity() =>
            $"{Parameter}:{Comparison}:{(Value.HasValue ? Value.Value.GetRawText() : string.Empty)}";

        public static ParameterConditionSpec From(OperatorParameterCondition condition) => new()
        {
            Parameter = condition.Parameter,
            Comparison = condition.Comparison,
            Value = condition.Value is null ? null : JsonSerializer.SerializeToElement(condition.Value, JsonOptions)
        };
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
