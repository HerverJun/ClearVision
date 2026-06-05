using System.Text.Json;
using ClearVision.Product.Core.AI.Tools;
using ClearVision.Product.Infrastructure.AI.Agent;
using ClearVision.Product.Infrastructure.AI.Tools;
using FluentAssertions;
using Microsoft.Extensions.Options;

namespace ClearVision.Product.Tests.AI.VisionAgentReadOnlyTools;

public sealed class VisionAgentReadOnlyToolsTests
{
    [Fact(DisplayName = "VisionAgentToolRegistry should list registered tools")]
    public void Registry_ShouldListTools()
    {
        var registry = CreateReadOnlyRegistry();

        var tools = registry.ListTools();

        tools.Select(item => item.Name).Should().BeEquivalentTo(
            "list_operator_catalog",
            "get_operator_schema",
            "retrieve_operator_knowledge",
            "match_flow_template",
            "get_flow_template_skeleton",
            "inspect_current_flow");
        tools.Should().OnlyContain(item => item.Permission == VisionAgentToolPermission.ReadOnly);
        tools.SelectMany(item => new[] { item.Name, item.DisplayName, item.Description })
            .Should()
            .NotContain(value =>
                value.Contains("shell", StringComparison.OrdinalIgnoreCase) ||
                value.Contains("powershell", StringComparison.OrdinalIgnoreCase) ||
                value.Contains("cmd", StringComparison.OrdinalIgnoreCase) ||
                value.Contains("execute_command", StringComparison.OrdinalIgnoreCase) ||
                value.Contains("system command", StringComparison.OrdinalIgnoreCase));
    }

    [Fact(DisplayName = "VisionAgentToolRegistry should return structured unknown tool failure")]
    public async Task Registry_ShouldReturnUnknownToolFailure()
    {
        var registry = CreateReadOnlyRegistry();

        var result = await registry.ExecuteAsync(
            "missing_tool",
            new VisionAgentToolContext(),
            Args(),
            CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("unknown_tool");
    }

    [Fact(DisplayName = "VisionAgentToolRegistry should deny ConfigWrite tools permanently")]
    public async Task Registry_ShouldDenyConfigWrite()
    {
        var tool = new FakeTool("write_config", VisionAgentToolPermission.ConfigWrite);
        var registry = new VisionAgentToolRegistry([tool]);

        var result = await registry.ExecuteAsync(
            "write_config",
            new VisionAgentToolContext
            {
                AllowedPermissions = new HashSet<VisionAgentToolPermission>
                {
                    VisionAgentToolPermission.ConfigWrite
                }
            },
            Args(),
            CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("tool_permission_denied");
        tool.ExecuteCount.Should().Be(0);
    }

    [Fact(DisplayName = "VisionAgentToolRegistry should deny RuntimePreview by default")]
    public async Task Registry_ShouldDenyRuntimePreviewByDefault()
    {
        var tool = new FakeTool("preview_frame", VisionAgentToolPermission.RuntimePreview);
        var registry = new VisionAgentToolRegistry([tool]);

        var result = await registry.ExecuteAsync(
            "preview_frame",
            new VisionAgentToolContext(),
            Args(),
            CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("runtime_preview_consent_required");
        result.PendingActions.Should().ContainSingle(action => action.ActionType == "AuthorizeRuntimePreview");
        tool.ExecuteCount.Should().Be(0);
    }

    [Fact(DisplayName = "VisionAgentToolRegistry should not expose DeploymentPrepare real tools in ReadOnly set")]
    public void Registry_ShouldNotExposeDeploymentPrepareRealTool()
    {
        var registry = CreateReadOnlyRegistry();

        registry.ListTools()
            .Should()
            .NotContain(item => item.Permission == VisionAgentToolPermission.DeploymentPrepare);
        registry.ListTools().Select(item => item.Name)
            .Should()
            .NotContain(["runtime_package_precheck", "deploy_runtime_package"]);
    }

    [Fact(DisplayName = "VisionAgentToolRegistry duplicate tool name should use first registration deterministically")]
    public async Task Registry_ShouldUseFirstDuplicateToolNameDeterministically()
    {
        var first = new FakeTool("duplicate_tool", VisionAgentToolPermission.ReadOnly, new { source = "first" });
        var second = new FakeTool("DUPLICATE_TOOL", VisionAgentToolPermission.ReadOnly, new { source = "second" });
        var registry = new VisionAgentToolRegistry([first, second]);

        var result = await registry.ExecuteAsync(
            "duplicate_TOOL",
            new VisionAgentToolContext(),
            Args(),
            CancellationToken.None);

        result.Success.Should().BeTrue();
        Json(result.Data).GetProperty("source").GetString().Should().Be("first");
        first.ExecuteCount.Should().Be(1);
        second.ExecuteCount.Should().Be(0);
    }

    [Fact(DisplayName = "list_operator_catalog should return catalog")]
    public async Task ListOperatorCatalog_ShouldReturnCatalog()
    {
        var tool = new OperatorCatalogTool();

        var result = await tool.ExecuteAsync(
            new VisionAgentToolContext(),
            Args(("keyword", "image")),
            CancellationToken.None);

        result.Success.Should().BeTrue();
        var payload = Json(result.Data);
        payload.GetProperty("source").GetString().Should().Be("readonly_static_catalog");
        payload.GetProperty("operators").EnumerateArray()
            .Select(item => item.GetProperty("operatorType").GetString())
            .Should()
            .Contain("ImageAcquisition");
    }

    [Fact(DisplayName = "get_operator_schema should return ImageAcquisition schema")]
    public async Task GetOperatorSchema_ShouldReturnImageAcquisitionSchema()
    {
        var result = await new OperatorSchemaTool().ExecuteAsync(
            new VisionAgentToolContext(),
            Args(("operatorType", "ImageAcquisition")),
            CancellationToken.None);

        result.Success.Should().BeTrue();
        var payload = Json(result.Data);
        payload.GetProperty("operatorType").GetString().Should().Be("ImageAcquisition");
        payload.GetProperty("outputPorts").EnumerateArray()
            .Select(item => item.GetString())
            .Should()
            .Contain("Image");
        payload.GetProperty("parameters").EnumerateArray()
            .Select(item => item.GetProperty("name").GetString())
            .Should()
            .Contain("CameraBindingId");
    }

    [Fact(DisplayName = "get_operator_schema should return TemplateMatching schema")]
    public async Task GetOperatorSchema_ShouldReturnTemplateMatchingSchema()
    {
        var result = await new OperatorSchemaTool().ExecuteAsync(
            new VisionAgentToolContext(),
            Args(("operatorType", "TemplateMatching")),
            CancellationToken.None);

        result.Success.Should().BeTrue();
        var payload = Json(result.Data);
        payload.GetProperty("operatorType").GetString().Should().Be("TemplateMatching");
        payload.GetProperty("parameters").EnumerateArray()
            .Select(item => item.GetProperty("name").GetString())
            .Should()
            .Contain("TemplatePath");
    }

    [Fact(DisplayName = "get_operator_schema should return structured unknown operator failure")]
    public async Task GetOperatorSchema_ShouldReturnUnknownOperatorFailure()
    {
        var result = await new OperatorSchemaTool().ExecuteAsync(
            new VisionAgentToolContext(),
            Args(("operatorType", "NotARealOperator")),
            CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("unknown_operator_type");
    }

    [Theory(DisplayName = "match_flow_template should match common industrial requests")]
    [InlineData("terminal wire sequence inspection", "wire_sequence")]
    [InlineData("template matching alignment for bracket", "template_matching")]
    [InlineData("hole distance measurement in mm", "measurement")]
    public async Task MatchFlowTemplate_ShouldMatchCommonRequests(string request, string scenarioKey)
    {
        var result = await new FlowTemplateMatchTool().ExecuteAsync(
            new VisionAgentToolContext { UserDescription = request },
            Args(("request", request)),
            CancellationToken.None);

        result.Success.Should().BeTrue();
        var first = Json(result.Data).GetProperty("candidates")[0];
        first.GetProperty("scenarioKey").GetString().Should().Be(scenarioKey);
        first.GetProperty("score").GetDouble().Should().BeGreaterThan(0.1);
    }

    [Fact(DisplayName = "get_flow_template_skeleton should return operators and connections without real resources")]
    public async Task GetFlowTemplateSkeleton_ShouldReturnNoResourceSkeleton()
    {
        var result = await new FlowTemplateSkeletonTool().ExecuteAsync(
            new VisionAgentToolContext(),
            Args(("templateId", "wire_sequence_inspection")),
            CancellationToken.None);

        result.Success.Should().BeTrue();
        var payload = Json(result.Data);
        payload.GetProperty("operators").EnumerateArray()
            .Select(item => item.GetProperty("operatorType").GetString())
            .Should()
            .Equal("ImageAcquisition", "RoiManager", "DeepLearning", "ResultJudgment", "ResultOutput");
        payload.GetProperty("connections").GetArrayLength().Should().BeGreaterThan(0);
        payload.GetProperty("operators")[0]
            .GetProperty("parameters")
            .GetProperty("CameraBindingId")
            .GetString()
            .Should()
            .Be("<pending-camera-binding>");
    }

    [Fact(DisplayName = "inspect_current_flow should summarize existingFlowJson without execution")]
    public async Task InspectCurrentFlow_ShouldSummarizeExistingFlowJson()
    {
        var flowJson = """
        {
          "operators": [
            { "tempId": "op_cam", "operatorType": "ImageAcquisition" },
            { "tempId": "op_match", "operatorType": "TemplateMatching" }
          ],
          "connections": [
            { "sourceTempId": "op_cam", "targetTempId": "op_match" }
          ]
        }
        """;

        var result = await new CurrentFlowInspectTool().ExecuteAsync(
            new VisionAgentToolContext { ExistingFlowJson = flowJson },
            Args(),
            CancellationToken.None);

        result.Success.Should().BeTrue();
        var payload = Json(result.Data);
        payload.GetProperty("hasExistingFlow").GetBoolean().Should().BeTrue();
        payload.GetProperty("operatorCount").GetInt32().Should().Be(2);
        payload.GetProperty("connectionCount").GetInt32().Should().Be(1);
        payload.GetProperty("operatorTypes").EnumerateArray()
            .Select(item => item.GetString())
            .Should()
            .Equal("ImageAcquisition", "TemplateMatching");
    }

    [Fact(DisplayName = "VisionAgentLoop scripted call should run list_operator_catalog then final answer")]
    public async Task VisionAgentLoop_ShouldRunCatalogToolThenFinal()
    {
        var registry = CreateReadOnlyRegistry();
        var loop = CreateLoop(registry);
        var responses = new Queue<string>(
        [
            ToolCall("list_operator_catalog", new { keyword = "template" }),
            "catalog final"
        ]);

        var result = await loop.RunAsync(Request(responses), CancellationToken.None);

        result.Success.Should().BeTrue();
        result.FinalContent.Should().Be("catalog final");
        result.ToolTrace.Should().ContainSingle();
        result.ToolTrace[0].ToolName.Should().Be("list_operator_catalog");
        result.ToolTrace[0].Success.Should().BeTrue();
    }

    [Fact(DisplayName = "VisionAgentLoop scripted call should run schema and template match in one read-only round")]
    public async Task VisionAgentLoop_ShouldRunSchemaAndTemplateMatchInOneReadOnlyRound()
    {
        var registry = CreateReadOnlyRegistry();
        var loop = CreateLoop(registry);
        var responses = new Queue<string>(
        [
            ToolCalls(
                ("get_operator_schema", new { operatorType = "TemplateMatching" }),
                ("match_flow_template", new { request = "template matching alignment" })),
            "schema and template final"
        ]);

        var result = await loop.RunAsync(Request(responses), CancellationToken.None);

        result.Success.Should().BeTrue();
        result.ToolRounds.Should().Be(1);
        result.ToolTrace.Select(item => item.ToolName)
            .Should()
            .BeEquivalentTo("get_operator_schema", "match_flow_template");
        result.ToolTrace.Should().OnlyContain(item => item.Success);
    }

    [Fact(DisplayName = "VisionAgentLoop should truncate large tool result before feeding model")]
    public async Task VisionAgentLoop_ShouldTruncateLargeToolResultForModel()
    {
        var registry = new VisionAgentToolRegistry(
        [
            new FakeTool(
                "large_readonly",
                VisionAgentToolPermission.ReadOnly,
                new { text = new string('x', 2_000) })
        ]);
        var loop = CreateLoop(registry, new VisionAgentLoopOptions
        {
            MaxToolResultChars = 256
        });
        var messagesSeen = new List<IReadOnlyList<VisionAgentLoopMessage>>();
        var responses = new Queue<string>(
        [
            ToolCall("large_readonly"),
            "large result final"
        ]);

        var result = await loop.RunAsync(new VisionAgentLoopRequest
        {
            UserPrompt = "large result",
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
        toolResultMessage.Length.Should().BeLessThan(1_200);
    }

    [Fact(DisplayName = "ReadOnly tool source guard should exclude camera replay precheck hardware network and process APIs")]
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

    [Fact(DisplayName = "ReadOnly tools should not wire VisionAgentLoop into AI default mainline")]
    public void MainlineGuard_ShouldNotWireVisionAgentLoop()
    {
        var aiFlowGenerationService = File.ReadAllText(Path.Combine(
            GetProductRoot(),
            "src",
            "ClearVision.Product.Infrastructure",
            "AI",
            "AiFlowGenerationService.cs"));

        aiFlowGenerationService.Should().NotContain("VisionAgentLoop");
    }

    private static VisionAgentToolRegistry CreateReadOnlyRegistry()
    {
        return new VisionAgentToolRegistry(
        [
            new OperatorCatalogTool(),
            new OperatorSchemaTool(),
            new OperatorKnowledgeTool(),
            new FlowTemplateMatchTool(),
            new FlowTemplateSkeletonTool(),
            new CurrentFlowInspectTool()
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
            UserPrompt = "scripted readonly tools test",
            CompleteAsync = (_, _) => Task.FromResult(responses.Dequeue())
        };
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

    private sealed class FakeTool : IVisionAgentTool
    {
        private readonly object _data;

        public FakeTool(
            string name,
            VisionAgentToolPermission permission,
            object? data = null)
        {
            Name = name;
            Permission = permission;
            _data = data ?? new { ok = true, tool = name };
        }

        public string Name { get; }
        public string DisplayName => Name;
        public string Description => "Fake read-only tools test tool";
        public string Category => "test";
        public VisionAgentToolPermission Permission { get; }
        public JsonElement ParametersSchema { get; } = EmptySchema();
        public int ExecuteCount { get; private set; }

        public Task<VisionAgentToolResult> ExecuteAsync(
            VisionAgentToolContext context,
            JsonElement arguments,
            CancellationToken cancellationToken)
        {
            ExecuteCount++;
            return Task.FromResult(VisionAgentToolResult.Ok(_data));
        }
    }
}
