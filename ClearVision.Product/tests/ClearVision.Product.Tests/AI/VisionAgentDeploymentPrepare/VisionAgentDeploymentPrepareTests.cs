using System.Text.Json;
using ClearVision.Product.Core.AI.Tools;
using ClearVision.Product.Infrastructure.AI.Agent;
using ClearVision.Product.Infrastructure.AI.Tools;
using FluentAssertions;
using Microsoft.Extensions.Options;

namespace ClearVision.Product.Tests.AI.VisionAgentDeploymentPrepare;

public sealed class VisionAgentDeploymentPrepareTests
{
    [Fact(DisplayName = "runtime_package_precheck should allow workflow draft when resources are missing")]
    public async Task Precheck_ShouldAllowWorkflowDraftWhenResourcesAreMissing()
    {
        var result = await new RuntimePackagePrecheckTool().ExecuteAsync(
            new VisionAgentToolContext(),
            Args(
                ("flow", MissingVisionResourceFlow()),
                ("validationSummary", await ValidateAsync(MissingVisionResourceFlow())),
                ("dryRunSummary", await DryRunAsync(MissingVisionResourceFlow()))),
            CancellationToken.None);

        result.Success.Should().BeTrue();
        var payload = Json(result.Data);
        payload.GetProperty("workflowDraftAllowed").GetBoolean().Should().BeTrue();
        payload.GetProperty("readyForDeployment").GetBoolean().Should().BeFalse();
        payload.GetProperty("deploymentBlocked").GetBoolean().Should().BeTrue();
        payload.GetProperty("pendingActions").GetArrayLength().Should().BeGreaterThan(0);
    }

    [Fact(DisplayName = "runtime_package_precheck should block deployment for missing CameraBindingId ModelPath and TemplatePath")]
    public async Task Precheck_ShouldBlockDeploymentForMissingVisionResources()
    {
        var result = await new RuntimePackagePrecheckTool().ExecuteAsync(
            new VisionAgentToolContext(),
            Args(
                ("flow", MissingVisionResourceFlow()),
                ("validationSummary", await ValidateAsync(MissingVisionResourceFlow())),
                ("dryRunSummary", await DryRunAsync(MissingVisionResourceFlow()))),
            CancellationToken.None);

        var missingParameters = Json(result.Data)
            .GetProperty("missingResources")
            .EnumerateArray()
            .Select(item => item.GetProperty("parameterName").GetString())
            .ToList();

        missingParameters.Should().Contain("CameraBindingId");
        missingParameters.Should().Contain("ModelPath");
        missingParameters.Should().Contain("TemplatePath");
        Json(result.Data).GetProperty("readyForDeployment").GetBoolean().Should().BeFalse();
    }

    [Fact(DisplayName = "runtime_package_precheck should report PLC parameters and output channel as missing deployment resources")]
    public async Task Precheck_ShouldReportPlcAndOutputResources()
    {
        var flow = PlcAndOutputMissingFlow();
        var result = await new RuntimePackagePrecheckTool().ExecuteAsync(
            new VisionAgentToolContext(),
            Args(
                ("flow", flow),
                ("validationSummary", await ValidateAsync(flow)),
                ("dryRunSummary", await DryRunAsync(flow))),
            CancellationToken.None);

        var missingKinds = Json(result.Data)
            .GetProperty("missingResources")
            .EnumerateArray()
            .Select(item => item.GetProperty("resourceKind").GetString())
            .ToList();

        missingKinds.Should().Contain("plc_parameters");
        missingKinds.Should().Contain("output_channel");
        Json(result.Data).GetProperty("workflowDraftAllowed").GetBoolean().Should().BeTrue();
    }

    [Fact(DisplayName = "runtime_package_precheck should block deployment for structural validation issues")]
    public async Task Precheck_ShouldBlockDeploymentForStructuralErrors()
    {
        var flow = BrokenConnectionFlow();
        var result = await new RuntimePackagePrecheckTool().ExecuteAsync(
            new VisionAgentToolContext(),
            Args(
                ("flow", flow),
                ("validationSummary", await ValidateAsync(flow)),
                ("dryRunSummary", await DryRunAsync(flow))),
            CancellationToken.None);

        var payload = Json(result.Data);
        payload.GetProperty("readyForDeployment").GetBoolean().Should().BeFalse();
        Codes(payload, "blockingIssues").Should().Contain("broken_connection_temp_id");
    }

    [Fact(DisplayName = "runtime_package_precheck should mark valid flow ready when validation dryrun and station status are good")]
    public async Task Precheck_ShouldAllowReadyDeploymentForValidInputs()
    {
        var flow = ValidFlow();
        var result = await new RuntimePackagePrecheckTool(new FakeStationStatusReader(true)).ExecuteAsync(
            new VisionAgentToolContext(),
            Args(
                ("flow", flow),
                ("validationSummary", await ValidateAsync(flow)),
                ("dryRunSummary", await DryRunAsync(flow)),
                ("targetStationId", "station_1")),
            CancellationToken.None);

        var payload = Json(result.Data);
        payload.GetProperty("readyForDeployment").GetBoolean().Should().BeTrue();
        payload.GetProperty("workflowDraftAllowed").GetBoolean().Should().BeTrue();
        payload.GetProperty("deployed").GetBoolean().Should().BeFalse();
        payload.GetProperty("packageCreated").GetBoolean().Should().BeFalse();
        payload.GetProperty("stationTouched").GetBoolean().Should().BeFalse();
    }

    [Fact(DisplayName = "runtime_package_precheck should block deployment but allow draft when dryrun is missing")]
    public async Task Precheck_ShouldBlockDeploymentWhenDryRunMissing()
    {
        var flow = ValidFlow();
        var result = await new RuntimePackagePrecheckTool().ExecuteAsync(
            new VisionAgentToolContext(),
            Args(("flow", flow), ("validationSummary", await ValidateAsync(flow))),
            CancellationToken.None);

        var payload = Json(result.Data);
        payload.GetProperty("workflowDraftAllowed").GetBoolean().Should().BeTrue();
        payload.GetProperty("readyForDeployment").GetBoolean().Should().BeFalse();
        Codes(payload, "blockingIssues").Should().Contain("dryrun_missing");
    }

    [Fact(DisplayName = "runtime_package_precheck should warn only when targetStationId is missing")]
    public async Task Precheck_ShouldWarnOnlyWhenTargetStationMissing()
    {
        var flow = ValidFlow();
        var result = await new RuntimePackagePrecheckTool().ExecuteAsync(
            new VisionAgentToolContext(),
            Args(
                ("flow", flow),
                ("validationSummary", await ValidateAsync(flow)),
                ("dryRunSummary", await DryRunAsync(flow))),
            CancellationToken.None);

        var payload = Json(result.Data);
        payload.GetProperty("readyForDeployment").GetBoolean().Should().BeTrue();
        Codes(payload, "warnings").Should().Contain("target_station_missing");
        Codes(payload, "blockingIssues").Should().NotContain("target_station_missing");
    }

    [Fact(DisplayName = "runtime_package_precheck should block deployment when NoOp reader has no station status")]
    public async Task Precheck_ShouldBlockDeploymentWhenNoOpReaderHasNoStatus()
    {
        var flow = ValidFlow();
        var result = await new RuntimePackagePrecheckTool().ExecuteAsync(
            new VisionAgentToolContext(),
            Args(
                ("flow", flow),
                ("validationSummary", await ValidateAsync(flow)),
                ("dryRunSummary", await DryRunAsync(flow)),
                ("targetStationId", "station_1")),
            CancellationToken.None);

        var payload = Json(result.Data);
        payload.GetProperty("readyForDeployment").GetBoolean().Should().BeFalse();
        payload.GetProperty("deploymentBlocked").GetBoolean().Should().BeTrue();
        Codes(payload, "blockingIssues").Should().Contain("station_status_unavailable");
    }

    [Fact(DisplayName = "runtime_package_precheck should block deployment when replay is required but missing")]
    public async Task Precheck_ShouldBlockDeploymentWhenReplayRequiredButMissing()
    {
        var flow = ValidFlow();
        var result = await new RuntimePackagePrecheckTool(new FakeStationStatusReader(true)).ExecuteAsync(
            new VisionAgentToolContext(),
            Args(
                ("flow", flow),
                ("validationSummary", await ValidateAsync(flow)),
                ("dryRunSummary", await DryRunAsync(flow)),
                ("targetStationId", "station_1"),
                ("requireReplay", true)),
            CancellationToken.None);

        var payload = Json(result.Data);
        payload.GetProperty("workflowDraftAllowed").GetBoolean().Should().BeTrue();
        payload.GetProperty("readyForDeployment").GetBoolean().Should().BeFalse();
        Codes(payload, "blockingIssues").Should().Contain("replay_required");
    }

    [Fact(DisplayName = "VisionAgentToolRegistry should reject DeploymentPrepare by default")]
    public async Task Registry_ShouldRejectDeploymentPrepareByDefault()
    {
        var registry = new VisionAgentToolRegistry([new RuntimePackagePrecheckTool()]);

        var result = await registry.ExecuteAsync(
            "runtime_package_precheck",
            new VisionAgentToolContext(),
            Args(),
            CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("tool_permission_denied");
    }

    [Fact(DisplayName = "VisionAgentToolRegistry should explicitly allow only runtime_package_precheck for DeploymentPrepare")]
    public async Task Registry_ShouldExplicitlyAllowOnlyRuntimePackagePrecheck()
    {
        var precheck = new RuntimePackagePrecheckTool(new FakeStationStatusReader(true));
        var blockedDeploy = new FakeDeploymentPrepareTool("deploy_runtime_package");
        var registry = new VisionAgentToolRegistry([precheck, blockedDeploy]);
        var context = new VisionAgentToolContext
        {
            AllowedPermissions = new HashSet<VisionAgentToolPermission>
            {
                VisionAgentToolPermission.ReadOnly,
                VisionAgentToolPermission.Simulation,
                VisionAgentToolPermission.DeploymentPrepare
            }
        };
        var flow = ValidFlow();

        var allowed = await registry.ExecuteAsync(
            "runtime_package_precheck",
            context,
            Args(
                ("flow", flow),
                ("validationSummary", await ValidateAsync(flow)),
                ("dryRunSummary", await DryRunAsync(flow)),
                ("targetStationId", "station_1")),
            CancellationToken.None);
        var denied = await registry.ExecuteAsync(
            "deploy_runtime_package",
            context,
            Args(),
            CancellationToken.None);

        allowed.Success.Should().BeTrue();
        denied.Success.Should().BeFalse();
        denied.ErrorCode.Should().Be("tool_permission_denied");
        blockedDeploy.ExecuteCount.Should().Be(0);
    }

    [Fact(DisplayName = "VisionAgentLoop scripted chain should reach runtime_package_precheck then final")]
    public async Task VisionAgentLoop_ShouldReachPrecheckThenFinal()
    {
        var flow = ValidFlow();
        var responses = new Queue<string>(
        [
            ToolCall("validate_flow", new { flow }),
            ToolCall("dryrun_flow", new { flow }),
            ToolCall(
                "runtime_package_precheck",
                new
                {
                    flow,
                    validationSummary = await ValidateAsync(flow),
                    dryRunSummary = await DryRunAsync(flow),
                    targetStationId = "station_1"
                }),
            "precheck final"
        ]);
        var registry = new VisionAgentToolRegistry(
        [
            new FlowValidationTool(),
            new DryRunFlowTool(),
            new RuntimePackagePrecheckTool(new FakeStationStatusReader(true))
        ]);

        var result = await CreateLoop(registry, new VisionAgentLoopOptions { MaxToolRounds = 4 })
            .RunAsync(Request(responses, DeploymentContext()), CancellationToken.None);

        result.Success.Should().BeTrue();
        result.FinalContent.Should().Be("precheck final");
        result.ToolTrace.Select(trace => trace.ToolName).Should().Equal(
            "validate_flow",
            "dryrun_flow",
            "runtime_package_precheck");
        result.ToolTrace.Last().Permission.Should().Be(nameof(VisionAgentToolPermission.DeploymentPrepare));
        result.ToolTrace.Last().Success.Should().BeTrue();
    }

    [Fact(DisplayName = "DeploymentPrepare source guard should exclude camera replay hardware network and process APIs")]
    public void SourceGuard_ShouldExcludeRuntimePreviewAndExternalAccess()
    {
        var source = ReadSourceUnder(Path.Combine(
            GetProductRoot(),
            "src",
            "ClearVision.Product.Infrastructure",
            "AI",
            "Tools"));
        var forbidden = new[]
        {
            "CameraTestFrameTool",
            "ReplayFlowWithFrameTool",
            "AcquireSingleFrameAsync",
            "EnumerateCamerasAsync",
            "GetOrCreateByBindingAsync",
            "HttpClient",
            "TcpClient",
            "Socket",
            "File.ReadAllBytes",
            "Cv2.ImRead",
            "Image.FromFile",
            "Process.Start",
            "ProcessStartInfo",
            "powershell",
            "cmd.exe",
            "execute_command"
        };

        forbidden.Should().OnlyContain(fragment =>
            !source.Contains(fragment, StringComparison.OrdinalIgnoreCase));
    }

    [Fact(DisplayName = "DeploymentPrepare should not wire VisionAgentLoop into AI default mainline or frontend")]
    public void MainlineGuard_ShouldNotWireVisionAgentLoopOrFrontend()
    {
        var productRoot = GetProductRoot();
        var aiFlowGenerationService = File.ReadAllText(Path.Combine(
            productRoot,
            "src",
            "ClearVision.Product.Infrastructure",
            "AI",
            "AiFlowGenerationService.cs"));
        var frontendSource = ReadSourceUnder(Path.Combine(
            productRoot,
            "src",
            "ClearVision.Product.Desktop",
            "wwwroot",
            "src"));

        aiFlowGenerationService.Should().NotContain("VisionAgentLoop");
        frontendSource.Should().NotContain("runtime_package_precheck");
        frontendSource.Should().NotContain("capture_test_frame");
        frontendSource.Should().NotContain("replay_flow_with_frame");
    }

    private static async Task<object> ValidateAsync(object flow)
    {
        var result = await new FlowValidationTool().ExecuteAsync(
            new VisionAgentToolContext(),
            Args(("flow", flow)),
            CancellationToken.None);

        result.Success.Should().BeTrue();
        return result.Data!;
    }

    private static async Task<object> DryRunAsync(object flow)
    {
        var result = await new DryRunFlowTool().ExecuteAsync(
            new VisionAgentToolContext(),
            Args(("flow", flow)),
            CancellationToken.None);

        result.Success.Should().BeTrue();
        return result.Data!;
    }

    private static object MissingVisionResourceFlow()
    {
        return new
        {
            operators = new object[]
            {
                Operator("op_cam", "ImageAcquisition"),
                Operator("op_detect", "DeepLearning"),
                Operator("op_match", "TemplateMatching"),
                Operator("op_out", "ResultOutput", new Dictionary<string, string> { ["Channel"] = "result_bus" })
            },
            connections = new object[]
            {
                Connection("op_cam", "Image", "op_detect", "Image"),
                Connection("op_cam", "Image", "op_match", "Image"),
                Connection("op_detect", "Detections", "op_out", "Input")
            }
        };
    }

    private static object PlcAndOutputMissingFlow()
    {
        return new
        {
            operators = new object[]
            {
                Operator("op_cam", "ImageAcquisition", new Dictionary<string, string> { ["CameraBindingId"] = "cam_1" }),
                Operator("op_plc", "PlcResultOutput"),
                Operator("op_out", "ResultOutput")
            },
            connections = new object[]
            {
                Connection("op_cam", "Image", "op_plc", "Input"),
                Connection("op_plc", "Result", "op_out", "Input")
            }
        };
    }

    private static object BrokenConnectionFlow()
    {
        return new
        {
            operators = new object[]
            {
                Operator("op_cam", "ImageAcquisition", new Dictionary<string, string> { ["CameraBindingId"] = "cam_1" }),
                Operator("op_match", "TemplateMatching", new Dictionary<string, string> { ["TemplatePath"] = "template://fixture" })
            },
            connections = new object[]
            {
                Connection("op_missing", "Image", "op_match", "Image")
            }
        };
    }

    private static object ValidFlow()
    {
        return new
        {
            operators = new object[]
            {
                Operator("op_cam", "ImageAcquisition", new Dictionary<string, string> { ["CameraBindingId"] = "cam_1" }),
                Operator("op_match", "TemplateMatching", new Dictionary<string, string> { ["TemplatePath"] = "template://fixture" }),
                Operator("op_judge", "ResultJudgment"),
                Operator("op_out", "ResultOutput", new Dictionary<string, string> { ["Channel"] = "result_bus" })
            },
            connections = new object[]
            {
                Connection("op_cam", "Image", "op_match", "Image"),
                Connection("op_match", "Score", "op_judge", "Input"),
                Connection("op_judge", "Result", "op_out", "Input")
            }
        };
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

    private static VisionAgentLoop CreateLoop(
        IVisionAgentToolRegistry registry,
        VisionAgentLoopOptions? options = null)
    {
        return new VisionAgentLoop(
            registry,
            new VisionAgentProtocolParser(),
            new AgentPromptBuilder(),
            Options.Create(options ?? new VisionAgentLoopOptions()));
    }

    private static VisionAgentLoopRequest Request(
        Queue<string> responses,
        VisionAgentToolContext context)
    {
        return new VisionAgentLoopRequest
        {
            UserPrompt = "scripted deployment prepare test",
            ToolContext = context,
            CompleteAsync = (_, _) => Task.FromResult(responses.Dequeue())
        };
    }

    private static VisionAgentToolContext DeploymentContext()
    {
        return new VisionAgentToolContext
        {
            AllowedPermissions = new HashSet<VisionAgentToolPermission>
            {
                VisionAgentToolPermission.ReadOnly,
                VisionAgentToolPermission.Simulation,
                VisionAgentToolPermission.DeploymentPrepare
            }
        };
    }

    private static string ToolCall(string name, object? arguments = null)
    {
        return JsonSerializer.Serialize(new
        {
            kind = "tool_call",
            toolCalls = new[]
            {
                new
                {
                    id = "call_1",
                    name,
                    arguments = arguments ?? new { }
                }
            }
        });
    }

    private static JsonElement Args(params (string Key, object? Value)[] values)
    {
        var dict = values.ToDictionary(
            item => item.Key,
            item => item.Value,
            StringComparer.OrdinalIgnoreCase);
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(dict));
        return doc.RootElement.Clone();
    }

    private static JsonElement Json(object? value)
    {
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(value));
        return doc.RootElement.Clone();
    }

    private static IReadOnlyList<string> Codes(JsonElement payload, string propertyName)
    {
        return payload.GetProperty(propertyName)
            .EnumerateArray()
            .Select(issue => issue.GetProperty("code").GetString() ?? string.Empty)
            .ToList();
    }

    private static JsonElement EmptySchema()
    {
        using var doc = JsonDocument.Parse("""{"type":"object","properties":{}}""");
        return doc.RootElement.Clone();
    }

    private static string ReadSourceUnder(string directory)
    {
        return string.Join(
            Environment.NewLine,
            Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .Select(File.ReadAllText));
    }

    private static string GetProductRoot()
    {
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.."));
    }

    private sealed class FakeStationStatusReader : IVisionAgentStationStatusReader
    {
        private readonly bool _isOnline;

        public FakeStationStatusReader(bool isOnline)
        {
            _isOnline = isOnline;
        }

        public Task<VisionAgentStationStatus?> TryReadAsync(
            string targetStationId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<VisionAgentStationStatus?>(new VisionAgentStationStatus
            {
                StationId = targetStationId,
                IsOnline = _isOnline,
                Status = _isOnline ? "online" : "offline"
            });
        }
    }

    private sealed class FakeDeploymentPrepareTool : IVisionAgentTool
    {
        public FakeDeploymentPrepareTool(string name)
        {
            Name = name;
        }

        public string Name { get; }
        public string DisplayName => Name;
        public string Description => "Fake deployment prepare tool";
        public string Category => "test";
        public VisionAgentToolPermission Permission => VisionAgentToolPermission.DeploymentPrepare;
        public JsonElement ParametersSchema { get; } = EmptySchema();
        public int ExecuteCount { get; private set; }

        public Task<VisionAgentToolResult> ExecuteAsync(
            VisionAgentToolContext context,
            JsonElement arguments,
            CancellationToken cancellationToken)
        {
            ExecuteCount++;
            return Task.FromResult(VisionAgentToolResult.Ok(new { executed = true }));
        }
    }
}
