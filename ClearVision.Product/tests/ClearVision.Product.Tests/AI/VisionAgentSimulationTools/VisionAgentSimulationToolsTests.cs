using System.Text.Json;
using ClearVision.Product.Core.AI.Tools;
using ClearVision.Product.Infrastructure.AI.Agent;
using ClearVision.Product.Infrastructure.AI.Tools;
using FluentAssertions;
using Microsoft.Extensions.Options;

namespace ClearVision.Product.Tests.AI.VisionAgentSimulationTools;

public sealed class VisionAgentSimulationToolsTests
{
    [Fact(DisplayName = "VisionAgentToolRegistry should expose ReadOnly and Simulation tools only")]
    public void Registry_ShouldExposeReadOnlyAndSimulationToolsOnly()
    {
        var registry = CreateAgentToolRegistry();

        var tools = registry.ListTools();

        tools.Select(tool => tool.Name).Should().Contain("validate_flow");
        tools.Select(tool => tool.Name).Should().Contain("dryrun_flow");
        tools.Should().Contain(tool => tool.Permission == VisionAgentToolPermission.ReadOnly);
        tools.Should().Contain(tool => tool.Permission == VisionAgentToolPermission.Simulation);
        tools.Should().NotContain(tool =>
            tool.Permission == VisionAgentToolPermission.RuntimePreview ||
            tool.Permission == VisionAgentToolPermission.ConfigWrite ||
            tool.Permission == VisionAgentToolPermission.DeploymentPrepare);
        tools.Select(tool => tool.Name).Should().NotContain([
            "capture_test_frame",
            "replay_flow_with_frame",
            "runtime_package_precheck",
            "execute_command"]);
        tools.SelectMany(tool => new[] { tool.Name, tool.DisplayName, tool.Description })
            .Should()
            .NotContain(value =>
                value.Contains("shell", StringComparison.OrdinalIgnoreCase) ||
                value.Contains("powershell", StringComparison.OrdinalIgnoreCase) ||
                value.Contains("cmd", StringComparison.OrdinalIgnoreCase) ||
                value.Contains("system command", StringComparison.OrdinalIgnoreCase));
    }

    [Fact(DisplayName = "validate_flow should accept valid template skeleton")]
    public async Task ValidateFlow_ShouldAcceptValidTemplateSkeleton()
    {
        var skeleton = await BuildWireTemplateSkeletonAsync();

        var result = await new FlowValidationTool().ExecuteAsync(
            new VisionAgentToolContext(),
            Args(("flow", skeleton)),
            CancellationToken.None);

        result.Success.Should().BeTrue();
        var payload = Json(result.Data);
        payload.GetProperty("isValid").GetBoolean().Should().BeTrue();
        payload.GetProperty("blockingIssues").GetArrayLength().Should().Be(0);
        payload.GetProperty("operatorCount").GetInt32().Should().Be(5);
    }

    [Fact(DisplayName = "validate_flow should detect missing operators")]
    public async Task ValidateFlow_ShouldDetectMissingOperators()
    {
        var result = await new FlowValidationTool().ExecuteAsync(
            new VisionAgentToolContext(),
            Args(("flow", new { operators = Array.Empty<object>(), connections = Array.Empty<object>() })),
            CancellationToken.None);

        result.Success.Should().BeTrue();
        Codes(Json(result.Data), "blockingIssues").Should().Contain("missing_operators");
    }

    [Fact(DisplayName = "validate_flow should detect broken connection tempId")]
    public async Task ValidateFlow_ShouldDetectBrokenConnectionTempId()
    {
        var result = await new FlowValidationTool().ExecuteAsync(
            new VisionAgentToolContext(),
            Args(("flow", BrokenConnectionFlow())),
            CancellationToken.None);

        result.Success.Should().BeTrue();
        Codes(Json(result.Data), "blockingIssues").Should().Contain("broken_connection_temp_id");
    }

    [Fact(DisplayName = "validate_flow should detect duplicate tempId")]
    public async Task ValidateFlow_ShouldDetectDuplicateTempId()
    {
        var result = await new FlowValidationTool().ExecuteAsync(
            new VisionAgentToolContext(),
            Args(("flow", DuplicateTempIdFlow())),
            CancellationToken.None);

        result.Success.Should().BeTrue();
        Codes(Json(result.Data), "blockingIssues").Should().Contain("duplicate_temp_id");
    }

    [Fact(DisplayName = "validate_flow should detect multi ImageAcquisition without entryOperatorTempId")]
    public async Task ValidateFlow_ShouldDetectMultiImageAcquisitionWithoutEntry()
    {
        var result = await new FlowValidationTool().ExecuteAsync(
            new VisionAgentToolContext(),
            Args(("flow", MultiCameraFlow())),
            CancellationToken.None);

        result.Success.Should().BeTrue();
        var payload = Json(result.Data);
        Codes(payload, "blockingIssues").Should().Contain("entry_operator_required");
        payload.GetProperty("imageAcquisitionCount").GetInt32().Should().Be(2);
    }

    [Fact(DisplayName = "validate_flow should report missing ModelPath TemplatePath and CameraBindingId")]
    public async Task ValidateFlow_ShouldReportMissingResources()
    {
        var result = await new FlowValidationTool().ExecuteAsync(
            new VisionAgentToolContext(),
            Args(("flow", MissingResourceFlow())),
            CancellationToken.None);

        result.Success.Should().BeTrue();
        var missingParameters = Json(result.Data)
            .GetProperty("missingResources")
            .EnumerateArray()
            .Select(item => item.GetProperty("parameterName").GetString())
            .ToList();

        missingParameters.Should().Contain("CameraBindingId");
        missingParameters.Should().Contain("ModelPath");
        missingParameters.Should().Contain("TemplatePath");
    }

    [Fact(DisplayName = "dryrun_flow should return simulated execution summary")]
    public async Task DryRunFlow_ShouldReturnSimulatedExecutionSummary()
    {
        var result = await new DryRunFlowTool().ExecuteAsync(
            new VisionAgentToolContext(),
            Args(("flow", TemplateMatchingFlowWithResources())),
            CancellationToken.None);

        result.Success.Should().BeTrue();
        var payload = Json(result.Data);
        payload.GetProperty("dryRunSucceeded").GetBoolean().Should().BeTrue();
        payload.GetProperty("executedOperators").EnumerateArray()
            .Select(item => item.GetProperty("status").GetString())
            .Should()
            .Contain("simulated_stub_camera_input")
            .And.Contain("simulated_stub_template_match");
        payload.GetProperty("dryRunSummary").GetProperty("executedCount").GetInt32().Should().Be(4);
    }

    [Fact(DisplayName = "dryrun_flow should never access real image camera model or station resources")]
    public async Task DryRunFlow_ShouldNeverAccessRealResources()
    {
        var result = await new DryRunFlowTool().ExecuteAsync(
            new VisionAgentToolContext(),
            Args(("flow", TemplateMatchingFlowWithResources())),
            CancellationToken.None);

        result.Success.Should().BeTrue();
        var summary = Json(result.Data).GetProperty("dryRunSummary");
        summary.GetProperty("generatedRealImages").GetBoolean().Should().BeFalse();
        summary.GetProperty("loadedModelFiles").GetBoolean().Should().BeFalse();
        summary.GetProperty("accessedHardware").GetBoolean().Should().BeFalse();
        summary.GetProperty("deployed").GetBoolean().Should().BeFalse();
    }

    [Fact(DisplayName = "VisionAgentLoop should call ReadOnly tools then Simulation tools then final")]
    public async Task VisionAgentLoop_ShouldCallReadOnlyThenSimulationToolsThenFinal()
    {
        var flow = await BuildWireTemplateSkeletonAsync();
        var responses = new Queue<string>(
        [
            ToolCall("list_operator_catalog", new { keyword = "wire" }),
            ToolCall("get_flow_template_skeleton", new { templateId = "wire_sequence_inspection" }),
            ToolCall("validate_flow", new { flow }),
            ToolCall("dryrun_flow", new { flow }),
            "simulation final"
        ]);
        var loop = CreateLoop(CreateAgentToolRegistry(), new VisionAgentLoopOptions
        {
            MaxToolRounds = 6
        });

        var result = await loop.RunAsync(Request(responses), CancellationToken.None);

        result.Success.Should().BeTrue();
        result.FinalContent.Should().Be("simulation final");
        result.ToolTrace.Select(trace => trace.ToolName).Should().Equal(
            "list_operator_catalog",
            "get_flow_template_skeleton",
            "validate_flow",
            "dryrun_flow");
        result.ToolTrace.Select(trace => trace.Permission).Should().Equal(
            "ReadOnly",
            "ReadOnly",
            "Simulation",
            "Simulation");
    }

    [Fact(DisplayName = "VisionAgentLoop should run mixed ReadOnly and Simulation round serially")]
    public async Task VisionAgentLoop_ShouldRunMixedReadOnlyAndSimulationRoundSerially()
    {
        var order = new List<string>();
        var registry = new VisionAgentToolRegistry(
        [
            new RecordingTool("record_read", VisionAgentToolPermission.ReadOnly, order),
            new RecordingTool("record_sim", VisionAgentToolPermission.Simulation, order)
        ]);
        var responses = new Queue<string>(
        [
            ToolCalls(("record_read", new { }), ("record_sim", new { })),
            "serial final"
        ]);

        var result = await CreateLoop(registry).RunAsync(Request(responses), CancellationToken.None);

        result.Success.Should().BeTrue();
        order.Should().Equal(
            "record_read:start",
            "record_read:end",
            "record_sim:start",
            "record_sim:end");
    }

    [Fact(DisplayName = "VisionAgentLoop should truncate large dryrun result before feeding model")]
    public async Task VisionAgentLoop_ShouldTruncateLargeDryRunResultForModel()
    {
        var registry = new VisionAgentToolRegistry([new DryRunFlowTool()]);
        var responses = new Queue<string>(
        [
            ToolCall("dryrun_flow", new { flow = LargeLinearFlow(80) }),
            "large dryrun final"
        ]);
        var messagesSeen = new List<IReadOnlyList<VisionAgentLoopMessage>>();
        var loop = CreateLoop(registry, new VisionAgentLoopOptions
        {
            MaxToolResultChars = 256
        });

        var result = await loop.RunAsync(new VisionAgentLoopRequest
        {
            UserPrompt = "large dryrun",
            CompleteAsync = (messages, _) =>
            {
                messagesSeen.Add(messages.ToList());
                return Task.FromResult(responses.Dequeue());
            }
        }, CancellationToken.None);

        result.Success.Should().BeTrue();
        messagesSeen.Should().HaveCount(2);
        var toolResultMessage = messagesSeen[1].Last().Content;
        toolResultMessage.Should().Contain("\"truncated\":true");
        toolResultMessage.Length.Should().BeLessThan(1_500);
    }

    [Fact(DisplayName = "Simulation tool source guard should exclude camera replay precheck hardware network and process APIs")]
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
            "RuntimePackagePrecheckTool",
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

    [Fact(DisplayName = "Simulation tools should not wire VisionAgentLoop into AI default mainline or frontend")]
    public void MainlineGuard_ShouldNotWireVisionAgentLoopOrRuntimePreviewUi()
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
        frontendSource.Should().NotContain("capture_test_frame");
        frontendSource.Should().NotContain("replay_flow_with_frame");
    }

    private static VisionAgentToolRegistry CreateAgentToolRegistry()
    {
        return new VisionAgentToolRegistry(
        [
            new OperatorCatalogTool(),
            new OperatorSchemaTool(),
            new OperatorKnowledgeTool(),
            new FlowTemplateMatchTool(),
            new FlowTemplateSkeletonTool(),
            new CurrentFlowInspectTool(),
            new FlowValidationTool(),
            new DryRunFlowTool()
        ]);
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

    private static VisionAgentLoopRequest Request(Queue<string> responses)
    {
        return new VisionAgentLoopRequest
        {
            UserPrompt = "scripted simulation tools test",
            CompleteAsync = (_, _) => Task.FromResult(responses.Dequeue())
        };
    }

    private static async Task<object> BuildWireTemplateSkeletonAsync()
    {
        var result = await new FlowTemplateSkeletonTool().ExecuteAsync(
            new VisionAgentToolContext(),
            Args(("templateId", "wire_sequence_inspection")),
            CancellationToken.None);
        result.Success.Should().BeTrue();
        return result.Data!;
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

    private static object DuplicateTempIdFlow()
    {
        return new
        {
            operators = new object[]
            {
                Operator("op_dup", "ImageAcquisition", new Dictionary<string, string> { ["CameraBindingId"] = "cam_1" }),
                Operator("op_dup", "TemplateMatching", new Dictionary<string, string> { ["TemplatePath"] = "template://fixture" })
            },
            connections = Array.Empty<object>()
        };
    }

    private static object MultiCameraFlow()
    {
        return new
        {
            operators = new object[]
            {
                Operator("op_cam_a", "ImageAcquisition", new Dictionary<string, string> { ["CameraBindingId"] = "cam_a" }),
                Operator("op_cam_b", "ImageAcquisition", new Dictionary<string, string> { ["CameraBindingId"] = "cam_b" }),
                Operator("op_compose", "ImageCompose")
            },
            connections = new object[]
            {
                Connection("op_cam_a", "Image", "op_compose", "ImageA"),
                Connection("op_cam_b", "Image", "op_compose", "ImageB")
            }
        };
    }

    private static object MissingResourceFlow()
    {
        return new
        {
            operators = new object[]
            {
                Operator("op_cam", "ImageAcquisition"),
                Operator("op_detect", "DeepLearning"),
                Operator("op_match", "TemplateMatching")
            },
            connections = new object[]
            {
                Connection("op_cam", "Image", "op_detect", "Image"),
                Connection("op_cam", "Image", "op_match", "Image")
            }
        };
    }

    private static object TemplateMatchingFlowWithResources()
    {
        return new
        {
            operators = new object[]
            {
                Operator("op_cam", "ImageAcquisition", new Dictionary<string, string> { ["CameraBindingId"] = "cam_1" }),
                Operator("op_match", "TemplateMatching", new Dictionary<string, string> { ["TemplatePath"] = "template://fixture" }),
                Operator("op_judge", "ResultJudgment"),
                Operator("op_out", "ResultOutput")
            },
            connections = new object[]
            {
                Connection("op_cam", "Image", "op_match", "Image"),
                Connection("op_match", "Score", "op_judge", "Input"),
                Connection("op_judge", "Result", "op_out", "Input")
            }
        };
    }

    private static object LargeLinearFlow(int count)
    {
        var operators = new List<object>
        {
            Operator("op_cam", "ImageAcquisition", new Dictionary<string, string> { ["CameraBindingId"] = "cam_1" })
        };
        var connections = new List<object>();
        var previous = "op_cam";
        var previousPort = "Image";
        for (var i = 0; i < count; i++)
        {
            var tempId = $"op_judge_{i}";
            operators.Add(Operator(tempId, "ResultJudgment"));
            connections.Add(Connection(previous, previousPort, tempId, "Input"));
            previous = tempId;
            previousPort = "Result";
        }

        operators.Add(Operator("op_out", "ResultOutput"));
        connections.Add(Connection(previous, previousPort, "op_out", "Input"));
        return new { operators, connections };
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

    private static string ToolCall(string name, object? arguments = null)
    {
        return ToolCalls((name, arguments ?? new { }));
    }

    private static string ToolCalls(params (string Name, object Arguments)[] calls)
    {
        return JsonSerializer.Serialize(new
        {
            kind = "tool_call",
            toolCalls = calls.Select((call, index) => new
            {
                id = $"call_{index + 1}",
                name = call.Name,
                arguments = call.Arguments
            })
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

    private sealed class RecordingTool : IVisionAgentTool
    {
        private readonly List<string> _order;

        public RecordingTool(
            string name,
            VisionAgentToolPermission permission,
            List<string> order)
        {
            Name = name;
            Permission = permission;
            _order = order;
        }

        public string Name { get; }
        public string DisplayName => Name;
        public string Description => "Records execution order for loop scheduling tests.";
        public string Category => "test";
        public VisionAgentToolPermission Permission { get; }
        public JsonElement ParametersSchema { get; } = EmptySchema();

        public async Task<VisionAgentToolResult> ExecuteAsync(
            VisionAgentToolContext context,
            JsonElement arguments,
            CancellationToken cancellationToken)
        {
            lock (_order)
            {
                _order.Add($"{Name}:start");
            }

            await Task.Delay(30, cancellationToken);

            lock (_order)
            {
                _order.Add($"{Name}:end");
            }

            return VisionAgentToolResult.Ok(new { name = Name });
        }
    }
}
