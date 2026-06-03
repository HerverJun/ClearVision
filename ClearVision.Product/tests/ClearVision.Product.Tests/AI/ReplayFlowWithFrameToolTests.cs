using System.Text.Json;
using ClearVision.Product.Core.AI.Tools;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Services;
using ClearVision.Product.Infrastructure.AI.Tools;
using FluentAssertions;
using NSubstitute;

namespace ClearVision.Product.Tests.AI;

public sealed class ReplayFlowWithFrameToolTests
{
    [Fact(DisplayName = "replay_flow_with_frame should execute flow with cached frame bytes")]
    public async Task ReplayFlowWithFrameTool_ShouldExecuteWithCachedFrameBytes()
    {
        var frameStore = new VisionAgentTemporaryFrameStore();
        var frameBytes = new byte[] { 1, 2, 3, 4 };
        var temporaryFrameId = frameStore.Store(frameBytes, new VisionAgentTemporaryFrameMetadata
        {
            CameraBindingId = "cam-1",
            CameraId = "camera-1",
            CameraName = "Test Camera",
            Width = 2,
            Height = 2,
            PixelFormat = "Mono8"
        });
        Dictionary<string, object>? capturedInputs = null;
        var flowExecution = Substitute.For<IFlowExecutionService>();
        flowExecution.ValidateFlow(Arg.Any<OperatorFlow>())
            .Returns(new FlowValidationResult { IsValid = true });
        flowExecution.ExecuteFlowAsync(
                Arg.Any<OperatorFlow>(),
                Arg.Do<Dictionary<string, object>>(inputs => capturedInputs = inputs),
                false,
                Arg.Any<CancellationToken>())
            .Returns(new FlowExecutionResult
            {
                IsSuccess = true,
                ExecutionTimeMs = 12,
                OutputData = new Dictionary<string, object>
                {
                    ["Result"] = "OK",
                    ["Image"] = new byte[] { 9, 8 }
                },
                OperatorResults =
                [
                    new OperatorExecutionResult
                    {
                        OperatorName = "ResultOutput",
                        IsSuccess = true,
                        ExecutionTimeMs = 3,
                        OutputData = new Dictionary<string, object> { ["Status"] = "OK" }
                    }
                ]
            });
        var tool = new ReplayFlowWithFrameTool(
            frameStore,
            Substitute.For<IOperatorFactory>(),
            flowExecution);
        using var argsDoc = JsonDocument.Parse($$"""
        {
          "temporaryFrameId": "{{temporaryFrameId}}",
          "flow": {
            "kind": "final_flow",
            "operators": [],
            "connections": []
          }
        }
        """);

        var result = await tool.ExecuteAsync(
            new VisionAgentToolContext
            {
                AllowedPermissions = new HashSet<VisionAgentToolPermission>
                {
                    VisionAgentToolPermission.RuntimePreview
                }
            },
            argsDoc.RootElement,
            CancellationToken.None);

        result.Success.Should().BeTrue();
        await flowExecution.Received(1).ExecuteFlowAsync(
            Arg.Any<OperatorFlow>(),
            Arg.Any<Dictionary<string, object>>(),
            false,
            Arg.Any<CancellationToken>());
        capturedInputs.Should().NotBeNull();
        capturedInputs!["Image"].Should().BeSameAs(frameBytes);

        var payload = JsonSerializer.SerializeToElement(result.Data);
        payload.GetProperty("replayExecuted").GetBoolean().Should().BeTrue();
        payload.GetProperty("replayKind").GetString().Should().Be("real_frame_runtime_execution");
        payload.GetProperty("frame").GetProperty("byteLength").GetInt32().Should().Be(frameBytes.Length);
        payload.GetProperty("outputSummary").GetProperty("Image").GetProperty("byteLength").GetInt32().Should().Be(2);
    }
}
