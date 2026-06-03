using System.Text.Json;
using ClearVision.Product.Core.AI.Tools;
using ClearVision.Product.Infrastructure.AI;
using ClearVision.Product.Infrastructure.AI.Agent;
using ClearVision.Product.Infrastructure.AI.Runtime;
using ClearVision.Product.Infrastructure.AI.Tools;
using FluentAssertions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace ClearVision.Product.Tests.AI;

public sealed class VisionAgentLoopTests
{
    [Fact(DisplayName = "AgentPromptBuilder should keep agent_tools prompt constrained to internal tools")]
    public void AgentPromptBuilder_ShouldBuildConstrainedAgentToolsPrompt()
    {
        var builder = new AgentPromptBuilder();
        var prompt = builder.BuildSystemPrompt(
            AiPromptModes.AgentTools,
            [VisionAgentToolDescriptor.FromTool(new FakeVisionAgentTool("list_operator_catalog"))],
            supportsJsonMode: true);

        prompt.Should().Contain("PromptMode: agent_tools");
        prompt.Should().Contain("list_operator_catalog");
        prompt.Should().Contain("Never request CMD, PowerShell, shell execution");
        prompt.Should().NotContain("ImageAcquisition -> inspection -> ResultJudgment -> ResultOutput");
    }

    [Fact(DisplayName = "VisionAgentProtocolParser should parse fenced tool call JSON")]
    public void VisionAgentProtocolParser_ShouldParseFencedToolCall()
    {
        var parser = new VisionAgentProtocolParser();

        var message = parser.Parse("""
            ```json
            {
              "kind": "tool_call",
              "tool_calls": [
                {
                  "name": "get_operator_schema",
                  "arguments": { "operatorType": "ImageAcquisition" }
                }
              ]
            }
            ```
            """);

        message.IsToolCall.Should().BeTrue();
        message.ToolCalls.Should().ContainSingle();
        message.ToolCalls[0].Name.Should().Be("get_operator_schema");
        message.ToolCalls[0].Arguments.GetProperty("operatorType").GetString()
            .Should().Be("ImageAcquisition");
    }

    [Fact(DisplayName = "VisionAgentToolRegistry should deny tools outside allowed permissions")]
    public async Task VisionAgentToolRegistry_ShouldDenyDisallowedPermissions()
    {
        var tool = new FakeVisionAgentTool(
            "draft_camera_binding",
            VisionAgentToolPermission.ConfigDraft,
            VisionAgentToolResult.Ok(new { ok = true }));
        var registry = new VisionAgentToolRegistry(
            [tool],
            Substitute.For<Microsoft.Extensions.Logging.ILogger<VisionAgentToolRegistry>>());

        using var argsDoc = JsonDocument.Parse("{}");
        var result = await registry.ExecuteAsync(
            tool.Name,
            new VisionAgentToolContext
            {
                AllowedPermissions = new HashSet<VisionAgentToolPermission>
                {
                    VisionAgentToolPermission.ReadOnly
                }
            },
            argsDoc.RootElement,
            CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("permission_denied");
        tool.ExecuteCount.Should().Be(0);
    }

    [Fact(DisplayName = "VisionAgentLoop should execute JSON tool call and return trace with pending actions")]
    public async Task VisionAgentLoop_ShouldExecuteToolCallAndReturnTrace()
    {
        var tool = new FakeVisionAgentTool(
            "draft_camera_binding",
            VisionAgentToolPermission.ConfigDraft,
            VisionAgentToolResult.Ok(
                new { draftBinding = new { id = "cam_SN1", serialNumber = "SN-1" } },
                requiresUserConfirmation: true,
                pendingActions:
                [
                    new VisionAgentPendingAction
                    {
                        ActionType = "cameraBindingDraft.apply",
                        Title = "Apply camera binding draft",
                        Summary = "Draft camera binding for SN-1.",
                        Payload = new { id = "cam_SN1", serialNumber = "SN-1" },
                        RequiresUserConfirmation = true
                    }
                ]));
        var registry = new VisionAgentToolRegistry(
            [tool],
            Substitute.For<Microsoft.Extensions.Logging.ILogger<VisionAgentToolRegistry>>());
        var connector = new QueueConnector(
            """
            {
              "kind": "tool_call",
              "toolCalls": [
                {
                  "id": "call_1",
                  "name": "draft_camera_binding",
                  "arguments": { "serialNumber": "SN-1" }
                }
              ]
            }
            """,
            """{"kind":"final_flow","operators":[],"connections":[]}""");
        var model = new AiModelConfig
        {
            Id = "fake-model",
            Name = "Fake Model",
            Provider = "OpenAI Compatible",
            Model = "fake-json"
        };
        var orchestrator = new AiGenerationOrchestrator(
            new FixedModelSelector(model),
            new FixedConnectorFactory(connector));
        var loop = new VisionAgentLoop(
            orchestrator,
            new AgentPromptBuilder(),
            registry,
            new VisionAgentProtocolParser(),
            Options.Create(new VisionAgentLoopOptions
            {
                MaxToolRounds = 2,
                MaxToolCallsPerRound = 2,
                MaxToolResultChars = 2_000
            }));

        var result = await loop.RunAsync(new VisionAgentLoopRequest
        {
            UserPrompt = "draft camera binding",
            Model = model,
            Capabilities = new AiModelCapabilities { SupportsJsonMode = true },
            ToolContext = new VisionAgentToolContext
            {
                PromptMode = AiPromptModes.AgentTools,
                DebugPrompt = true,
                UserDescription = "draft camera binding",
                AllowedPermissions = new HashSet<VisionAgentToolPermission>
                {
                    VisionAgentToolPermission.ReadOnly,
                    VisionAgentToolPermission.ConfigDraft
                }
            }
        }, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.FinalContent.Should().Contain("final_flow");
        result.ToolRounds.Should().Be(1);
        result.ToolTrace.Should().ContainSingle();
        result.ToolTrace[0].ToolName.Should().Be("draft_camera_binding");
        result.ToolTrace[0].Success.Should().BeTrue();
        result.PendingActions.Should().ContainSingle();
        result.PendingActions[0].ActionType.Should().Be("cameraBindingDraft.apply");
        tool.ExecuteCount.Should().Be(1);
        connector.Calls.Should().HaveCount(2);
        connector.Calls[1].Messages.Last().Content.Should().Contain("tool_result");
        connector.Calls[1].Messages.Last().Content.Should().Contain("cameraBindingDraft.apply");
    }

    [Fact(DisplayName = "VisionAgentLoop should use native provider tool calls when capability is enabled")]
    public async Task VisionAgentLoop_ShouldUseNativeToolCallsWhenSupported()
    {
        var tool = new FakeVisionAgentTool(
            "get_operator_schema",
            VisionAgentToolPermission.ReadOnly,
            VisionAgentToolResult.Ok(new { operatorType = "ImageAcquisition", ports = new[] { "Image" } }));
        var registry = new VisionAgentToolRegistry(
            [tool],
            Substitute.For<Microsoft.Extensions.Logging.ILogger<VisionAgentToolRegistry>>());
        var connector = new NativeQueueConnector(
            new AiCompletionResult
            {
                ToolCalls =
                [
                    new AiNativeToolCall
                    {
                        Id = "call_native_1",
                        Name = "get_operator_schema",
                        Arguments = ParseSchema("""{"operatorType":"ImageAcquisition"}""")
                    }
                ]
            },
            new AiCompletionResult
            {
                Content = """{"kind":"final_flow","operators":[],"connections":[]}"""
            });
        var model = new AiModelConfig
        {
            Id = "native-model",
            Name = "Native Model",
            Provider = "OpenAI",
            Protocol = AiModelConfig.ProtocolOpenAiCompatible,
            Model = "gpt-native",
            Capabilities = new AiModelCapabilities { SupportsToolCall = true, SupportsJsonMode = true }
        };
        var orchestrator = new AiGenerationOrchestrator(
            new FixedModelSelector(model),
            new FixedConnectorFactory(connector));
        var loop = new VisionAgentLoop(
            orchestrator,
            new AgentPromptBuilder(),
            registry,
            new VisionAgentProtocolParser(),
            Options.Create(new VisionAgentLoopOptions
            {
                MaxToolRounds = 2,
                MaxToolCallsPerRound = 2,
                MaxToolResultChars = 2_000
            }));

        var result = await loop.RunAsync(new VisionAgentLoopRequest
        {
            UserPrompt = "build acquisition flow",
            Model = model,
            Capabilities = model.GetEffectiveCapabilities(),
            ToolContext = new VisionAgentToolContext
            {
                PromptMode = AiPromptModes.AgentTools,
                UserDescription = "build acquisition flow",
                AllowedPermissions = new HashSet<VisionAgentToolPermission>
                {
                    VisionAgentToolPermission.ReadOnly
                }
            }
        }, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.ToolTrace.Should().ContainSingle();
        tool.ExecuteCount.Should().Be(1);
        connector.NativeCalls.Should().HaveCount(2);
        connector.NativeCalls[0].Tools.Should().ContainSingle(t => t.Name == "get_operator_schema");
        connector.NativeCalls[1].Messages.Should().Contain(message => message.HasToolCalls);
        connector.NativeCalls[1].Messages.Should().Contain(message => message.HasToolResults);
        connector.CompleteCalls.Should().Be(0);
    }

    private sealed class FakeVisionAgentTool : IVisionAgentTool
    {
        private readonly VisionAgentToolResult _result;

        public FakeVisionAgentTool(
            string name,
            VisionAgentToolPermission permission = VisionAgentToolPermission.ReadOnly,
            VisionAgentToolResult? result = null)
        {
            Name = name;
            Permission = permission;
            _result = result ?? VisionAgentToolResult.Ok(new { ok = true });
            ParametersSchema = ParseSchema("""{"type":"object","properties":{}}""");
        }

        public string Name { get; }
        public string DisplayName => Name;
        public string Description => "Fake test tool";
        public string Category => "test";
        public VisionAgentToolPermission Permission { get; }
        public JsonElement ParametersSchema { get; }
        public int ExecuteCount { get; private set; }

        public Task<VisionAgentToolResult> ExecuteAsync(
            VisionAgentToolContext context,
            JsonElement arguments,
            CancellationToken cancellationToken)
        {
            ExecuteCount++;
            return Task.FromResult(_result);
        }
    }

    private sealed class QueueConnector : IAiConnector
    {
        private readonly Queue<string> _responses;

        public QueueConnector(params string[] responses)
        {
            _responses = new Queue<string>(responses);
        }

        public List<(string SystemPrompt, List<ChatMessage> Messages)> Calls { get; } = new();

        public Task<AiCompletionResult> CompleteAsync(
            string systemPrompt,
            List<ChatMessage> messages,
            CancellationToken cancellationToken = default)
        {
            Calls.Add((systemPrompt, messages.ToList()));
            return Task.FromResult(new AiCompletionResult
            {
                Content = _responses.Dequeue()
            });
        }

        public Task<AiCompletionResult> StreamCompleteAsync(
            string systemPrompt,
            List<ChatMessage> messages,
            Action<ClearVision.Product.Contracts.Messages.AiStreamChunk> onChunk,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class NativeQueueConnector : IAiConnector, IAiToolCallingConnector
    {
        private readonly Queue<AiCompletionResult> _responses;

        public NativeQueueConnector(params AiCompletionResult[] responses)
        {
            _responses = new Queue<AiCompletionResult>(responses);
        }

        public List<(string SystemPrompt, List<ChatMessage> Messages, IReadOnlyList<AiNativeToolDefinition> Tools)> NativeCalls { get; } = new();
        public int CompleteCalls { get; private set; }

        public Task<AiCompletionResult> CompleteAsync(
            string systemPrompt,
            List<ChatMessage> messages,
            CancellationToken cancellationToken = default)
        {
            CompleteCalls++;
            return Task.FromResult(new AiCompletionResult
            {
                Content = """{"kind":"final_flow","operators":[],"connections":[]}"""
            });
        }

        public Task<AiCompletionResult> CompleteWithToolsAsync(
            string systemPrompt,
            List<ChatMessage> messages,
            IReadOnlyList<AiNativeToolDefinition> tools,
            CancellationToken cancellationToken = default)
        {
            NativeCalls.Add((systemPrompt, messages.ToList(), tools.ToList()));
            return Task.FromResult(_responses.Dequeue());
        }

        public Task<AiCompletionResult> StreamCompleteAsync(
            string systemPrompt,
            List<ChatMessage> messages,
            Action<ClearVision.Product.Contracts.Messages.AiStreamChunk> onChunk,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class FixedConnectorFactory : IAiConnectorFactory
    {
        private readonly IAiConnector _connector;

        public FixedConnectorFactory(IAiConnector connector)
        {
            _connector = connector;
        }

        public IAiConnector CreateConnector(AiModelConfig modelConfig) => _connector;
    }

    private sealed class FixedModelSelector : IAiModelSelector
    {
        private readonly AiModelConfig _model;

        public FixedModelSelector(AiModelConfig model)
        {
            _model = model;
        }

        public AiModelConfig SelectGenerationModel() => _model;
        public AiModelConfig SelectModelForRole(string role) => _model;
        public (AiModelConfig Model, string Reason) SelectModelForRoleWithReason(string role) =>
            (_model, "fixed-test-model");
    }

    private static JsonElement ParseSchema(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }
}
