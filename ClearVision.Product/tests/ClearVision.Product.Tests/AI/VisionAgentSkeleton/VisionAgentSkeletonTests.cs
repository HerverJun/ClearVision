using System.Diagnostics;
using System.Text.Json;
using ClearVision.Product.Core.AI.Tools;
using ClearVision.Product.Infrastructure.AI.Agent;
using FluentAssertions;
using Microsoft.Extensions.Options;

namespace ClearVision.Product.Tests.AI.VisionAgentSkeleton;

public sealed class VisionAgentSkeletonTests
{
    [Fact(DisplayName = "VisionAgentProtocolParser should parse final answer")]
    public void VisionAgentProtocolParser_ShouldParseFinalAnswer()
    {
        var parser = new VisionAgentProtocolParser();

        var message = parser.Parse("final engineering answer");

        message.IsToolCall.Should().BeFalse();
        message.FinalContent.Should().Be("final engineering answer");
        message.ToolCalls.Should().BeEmpty();
    }

    [Fact(DisplayName = "VisionAgentProtocolParser should parse tool call JSON")]
    public void VisionAgentProtocolParser_ShouldParseToolCallJson()
    {
        var parser = new VisionAgentProtocolParser();

        var message = parser.Parse("""
        {
          "kind": "tool_call",
          "toolCalls": [
            {
              "id": "call_1",
              "name": "inspect_contract",
              "arguments": { "operatorType": "TemplateMatching" }
            }
          ]
        }
        """);

        message.IsToolCall.Should().BeTrue();
        message.ToolCalls.Should().ContainSingle();
        message.ToolCalls[0].Id.Should().Be("call_1");
        message.ToolCalls[0].Name.Should().Be("inspect_contract");
        message.ToolCalls[0].Arguments.GetProperty("operatorType").GetString()
            .Should()
            .Be("TemplateMatching");
    }

    [Fact(DisplayName = "VisionAgentLoop should return structured unknown tool failure in trace")]
    public async Task VisionAgentLoop_ShouldTraceUnknownToolFailure()
    {
        var loop = CreateLoop(new FakeToolRegistry([]));
        var responses = new Queue<string>(
        [
            ToolCall("missing_tool"),
            "final after unknown tool"
        ]);

        var result = await loop.RunAsync(Request(responses), CancellationToken.None);

        result.Success.Should().BeTrue();
        result.FinalContent.Should().Be("final after unknown tool");
        result.ToolTrace.Should().ContainSingle();
        result.ToolTrace[0].ToolName.Should().Be("missing_tool");
        result.ToolTrace[0].Success.Should().BeFalse();
        result.ToolTrace[0].ErrorCode.Should().Be("unknown_tool");
    }

    [Fact(DisplayName = "VisionAgentLoop should always deny ConfigWrite tools")]
    public async Task VisionAgentLoop_ShouldDenyConfigWrite()
    {
        var tool = new FakeTool("write_config", VisionAgentToolPermission.ConfigWrite);
        var loop = CreateLoop(new FakeToolRegistry([tool]));
        var responses = new Queue<string>(
        [
            ToolCall("write_config"),
            "final after denied write"
        ]);

        var result = await loop.RunAsync(Request(
            responses,
            new VisionAgentToolContext
            {
                AllowedPermissions = new HashSet<VisionAgentToolPermission>
                {
                    VisionAgentToolPermission.ReadOnly,
                    VisionAgentToolPermission.Simulation,
                    VisionAgentToolPermission.ConfigWrite
                }
            }), CancellationToken.None);

        result.Success.Should().BeTrue();
        result.ToolTrace.Should().ContainSingle();
        result.ToolTrace[0].Success.Should().BeFalse();
        result.ToolTrace[0].ErrorCode.Should().Be("tool_permission_denied");
        result.ToolTrace[0].Permission.Should().Be(nameof(VisionAgentToolPermission.ConfigWrite));
        tool.ExecuteCount.Should().Be(0);
    }

    [Fact(DisplayName = "VisionAgentLoop should deny RuntimePreview by default")]
    public async Task VisionAgentLoop_ShouldDenyRuntimePreviewByDefault()
    {
        var tool = new FakeTool("preview_frame", VisionAgentToolPermission.RuntimePreview);
        var loop = CreateLoop(new FakeToolRegistry([tool]));
        var responses = new Queue<string>(
        [
            ToolCall("preview_frame"),
            "final after denied preview"
        ]);

        var result = await loop.RunAsync(Request(responses), CancellationToken.None);

        result.Success.Should().BeTrue();
        result.ToolTrace.Should().ContainSingle();
        result.ToolTrace[0].Success.Should().BeFalse();
        result.ToolTrace[0].ErrorCode.Should().Be("tool_permission_denied");
        result.ToolTrace[0].Permission.Should().Be(nameof(VisionAgentToolPermission.RuntimePreview));
        tool.ExecuteCount.Should().Be(0);
    }

    [Fact(DisplayName = "VisionAgentLoop should allow RuntimePreview only when context explicitly enables it")]
    public async Task VisionAgentLoop_ShouldAllowRuntimePreviewWhenExplicitlyEnabled()
    {
        var tool = new FakeTool("preview_frame", VisionAgentToolPermission.RuntimePreview);
        var loop = CreateLoop(new FakeToolRegistry([tool]));
        var responses = new Queue<string>(
        [
            ToolCall("preview_frame"),
            "final after allowed preview"
        ]);

        var result = await loop.RunAsync(Request(
            responses,
            new VisionAgentToolContext
            {
                AllowedPermissions = new HashSet<VisionAgentToolPermission>
                {
                    VisionAgentToolPermission.ReadOnly,
                    VisionAgentToolPermission.Simulation,
                    VisionAgentToolPermission.RuntimePreview
                }
            }), CancellationToken.None);

        result.Success.Should().BeTrue();
        result.ToolTrace.Should().ContainSingle();
        result.ToolTrace[0].Success.Should().BeTrue();
        result.ToolTrace[0].Permission.Should().Be(nameof(VisionAgentToolPermission.RuntimePreview));
        tool.ExecuteCount.Should().Be(1);
    }

    [Fact(DisplayName = "VisionAgentLoop should return structured failure when max tool rounds are exceeded")]
    public async Task VisionAgentLoop_ShouldFailWhenMaxToolRoundsExceeded()
    {
        var tool = new FakeTool("inspect_contract", VisionAgentToolPermission.ReadOnly);
        var loop = CreateLoop(new FakeToolRegistry([tool]), new VisionAgentLoopOptions
        {
            MaxToolRounds = 1,
            MaxToolCallsPerRound = 2
        });
        var responses = new Queue<string>(
        [
            ToolCall("inspect_contract"),
            ToolCall("inspect_contract")
        ]);

        var result = await loop.RunAsync(Request(responses), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.FailureType.Should().Be("failed_with_tool_limit");
        result.ErrorMessage.Should().Contain("MaxToolRounds=1");
        result.ToolTrace.Should().ContainSingle();
        tool.ExecuteCount.Should().Be(1);
    }

    [Fact(DisplayName = "VisionAgentLoop tool trace should include permission, success, duration, and summarized errors")]
    public async Task VisionAgentLoop_ShouldEmitToolTraceShape()
    {
        var tool = new FakeTool("inspect_contract", VisionAgentToolPermission.ReadOnly);
        var loop = CreateLoop(new FakeToolRegistry([tool]));
        var responses = new Queue<string>(
        [
            """
            {
              "kind": "tool_call",
              "toolCalls": [
                {
                  "name": "inspect_contract",
                  "arguments": {
                    "operatorType": "ImageAcquisition",
                    "largeObject": { "nested": true },
                    "items": [1, 2, 3],
                    "longText": "abcdefghijklmnopqrstuvwxyzabcdefghijklmnopqrstuvwxyzabcdefghijklmnopqrstuvwxyzabcdefghijklmnopqrstuvwxyz"
                  }
                }
              ]
            }
            """,
            "final after trace"
        ]);

        var result = await loop.RunAsync(Request(responses), CancellationToken.None);

        result.Success.Should().BeTrue();
        result.ToolTrace.Should().ContainSingle();
        var trace = result.ToolTrace[0];
        trace.ToolName.Should().Be("inspect_contract");
        trace.Permission.Should().Be(nameof(VisionAgentToolPermission.ReadOnly));
        trace.Success.Should().BeTrue();
        trace.DurationMs.Should().BeGreaterThanOrEqualTo(0);
        trace.ErrorCode.Should().BeNull();

        var args = JsonSerializer.SerializeToElement(trace.Arguments);
        args.GetProperty("operatorType").GetString().Should().Be("ImageAcquisition");
        args.GetProperty("largeObject").GetString().Should().Be("{...}");
        args.GetProperty("items").GetString().Should().Be("[3]");
        args.GetProperty("longText").GetString()!.Length.Should().BeLessThan(90);
    }

    [Fact(DisplayName = "VisionAgentLoop should run read-only tools in parallel")]
    public async Task VisionAgentLoop_ShouldRunReadOnlyToolsInParallel()
    {
        var probe = new ConcurrencyProbe(delayMs: 80);
        var toolA = new FakeTool("read_a", VisionAgentToolPermission.ReadOnly, probe);
        var toolB = new FakeTool("read_b", VisionAgentToolPermission.ReadOnly, probe);
        var loop = CreateLoop(new FakeToolRegistry([toolA, toolB]));
        var responses = new Queue<string>(
        [
            ToolCall(["read_a", "read_b"]),
            "final after parallel"
        ]);

        var result = await loop.RunAsync(Request(responses), CancellationToken.None);

        result.Success.Should().BeTrue();
        toolA.ExecuteCount.Should().Be(1);
        toolB.ExecuteCount.Should().Be(1);
        probe.MaxConcurrent.Should().Be(2);
    }

    [Fact(DisplayName = "VisionAgentLoop should run non-read-only tool rounds serially")]
    public async Task VisionAgentLoop_ShouldRunNonReadOnlyToolRoundsSerially()
    {
        var probe = new ConcurrencyProbe(delayMs: 40);
        var toolA = new FakeTool("simulate_a", VisionAgentToolPermission.Simulation, probe);
        var toolB = new FakeTool("read_b", VisionAgentToolPermission.ReadOnly, probe);
        var loop = CreateLoop(new FakeToolRegistry([toolA, toolB]));
        var responses = new Queue<string>(
        [
            ToolCall(["simulate_a", "read_b"]),
            "final after serial"
        ]);

        var result = await loop.RunAsync(Request(responses), CancellationToken.None);

        result.Success.Should().BeTrue();
        toolA.ExecuteCount.Should().Be(1);
        toolB.ExecuteCount.Should().Be(1);
        probe.MaxConcurrent.Should().Be(1);
        probe.ExecutionOrder.Should().Equal("simulate_a", "read_b");
    }

    [Fact(DisplayName = "VisionAgent skeleton should not include real hardware/resource tools or API access")]
    public void VisionAgentSkeleton_ShouldNotIncludeRealHardwareOrResourceAccess()
    {
        var source = ReadSourceUnder(Path.Combine(GetProductRoot(), "src", "ClearVision.Product.Infrastructure", "AI", "Agent")) +
                     ReadSourceUnder(Path.Combine(GetProductRoot(), "src", "ClearVision.Product.Infrastructure", "AI", "Tools"));
        var forbidden = new[]
        {
            "CameraTestFrameTool",
            "ReplayFlowWithFrameTool",
            "RuntimePackagePrecheckTool",
            "AcquireSingleFrameAsync",
            "GetOrCreateByBindingAsync",
            "EnumerateCamerasAsync",
            "HttpClient",
            "TcpClient",
            "Socket",
            "File.ReadAllBytes",
            "Cv2.ImRead",
            "Image.FromFile",
            "Process.Start",
            "ProcessStartInfo"
        };

        forbidden.Should().OnlyContain(fragment =>
            !source.Contains(fragment, StringComparison.OrdinalIgnoreCase));
    }

    [Fact(DisplayName = "VisionAgent skeleton should not be wired into AI default flow or frontend RuntimePreview UI")]
    public void VisionAgentSkeleton_ShouldNotBeWiredIntoDefaultFlowOrFrontend()
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
        frontendSource.Should().NotContain("RuntimePreview");
        frontendSource.Should().NotContain("capture_test_frame");
        frontendSource.Should().NotContain("replay_flow_with_frame");
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
        VisionAgentToolContext? context = null)
    {
        return new VisionAgentLoopRequest
        {
            UserPrompt = "scripted skeleton test",
            ToolContext = context ?? new VisionAgentToolContext(),
            CompleteAsync = (_, _) => Task.FromResult(responses.Dequeue())
        };
    }

    private static string ToolCall(string name)
    {
        return ToolCall([name]);
    }

    private static string ToolCall(IReadOnlyList<string> names)
    {
        var calls = names.Select((name, index) => new
        {
            id = $"call_{index + 1}",
            name,
            arguments = new { value = name }
        });
        return JsonSerializer.Serialize(new
        {
            kind = "tool_call",
            toolCalls = calls
        });
    }

    private static JsonElement Schema()
    {
        using var doc = JsonDocument.Parse("""{"type":"object","properties":{}}""");
        return doc.RootElement.Clone();
    }

    private static string ReadSourceUnder(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return string.Empty;
        }

        return string.Join(
            Environment.NewLine,
            Directory.EnumerateFiles(directory, "*.*", SearchOption.AllDirectories)
                .Where(path => path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
                               path.EndsWith(".js", StringComparison.OrdinalIgnoreCase))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .Select(File.ReadAllText));
    }

    private static string GetProductRoot()
    {
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.."));
    }

    private sealed class FakeToolRegistry : IVisionAgentToolRegistry
    {
        private readonly IReadOnlyDictionary<string, FakeTool> _tools;

        public FakeToolRegistry(IReadOnlyList<FakeTool> tools)
        {
            _tools = tools.ToDictionary(tool => tool.Name, StringComparer.OrdinalIgnoreCase);
        }

        public IReadOnlyList<VisionAgentToolDescriptor> ListTools()
        {
            return _tools.Values.Select(VisionAgentToolDescriptor.FromTool).ToList();
        }

        public bool TryGet(string name, out IVisionAgentTool tool)
        {
            if (_tools.TryGetValue(name, out var found))
            {
                tool = found;
                return true;
            }

            tool = null!;
            return false;
        }

        public Task<VisionAgentToolResult> ExecuteAsync(
            string name,
            VisionAgentToolContext context,
            JsonElement arguments,
            CancellationToken cancellationToken)
        {
            return _tools.TryGetValue(name, out var tool)
                ? tool.ExecuteAsync(context, arguments, cancellationToken)
                : Task.FromResult(VisionAgentToolResult.Fail("unknown_tool", $"Unknown fake tool '{name}'."));
        }
    }

    private sealed class FakeTool : IVisionAgentTool
    {
        private readonly ConcurrencyProbe? _probe;

        public FakeTool(
            string name,
            VisionAgentToolPermission permission,
            ConcurrencyProbe? probe = null)
        {
            Name = name;
            Permission = permission;
            _probe = probe;
        }

        public string Name { get; }
        public string DisplayName => Name;
        public string Description => "Fake skeleton test tool";
        public string Category => "test";
        public VisionAgentToolPermission Permission { get; }
        public JsonElement ParametersSchema { get; } = Schema();
        public int ExecuteCount { get; private set; }

        public async Task<VisionAgentToolResult> ExecuteAsync(
            VisionAgentToolContext context,
            JsonElement arguments,
            CancellationToken cancellationToken)
        {
            ExecuteCount++;
            if (_probe != null)
            {
                await _probe.RunAsync(Name, cancellationToken);
            }

            return VisionAgentToolResult.Ok(new { ok = true, tool = Name });
        }
    }

    private sealed class ConcurrencyProbe
    {
        private readonly int _delayMs;
        private readonly object _gate = new();
        private int _current;

        public ConcurrencyProbe(int delayMs)
        {
            _delayMs = delayMs;
        }

        public int MaxConcurrent { get; private set; }
        public List<string> ExecutionOrder { get; } = new();

        public async Task RunAsync(string name, CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                _current++;
                MaxConcurrent = Math.Max(MaxConcurrent, _current);
                ExecutionOrder.Add(name);
            }

            try
            {
                await Task.Delay(_delayMs, cancellationToken);
            }
            finally
            {
                lock (_gate)
                {
                    _current--;
                }
            }
        }
    }
}
