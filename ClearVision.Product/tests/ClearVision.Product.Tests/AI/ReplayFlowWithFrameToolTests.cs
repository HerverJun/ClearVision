using System.Text.Json;
using ClearVision.Product.Core.AI.Tools;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Services;
using ClearVision.Product.Core.ValueObjects;
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
            CreateOperatorFactory(),
            flowExecution);
        using var argsDoc = JsonDocument.Parse($$"""
        {
          "temporaryFrameId": "{{temporaryFrameId}}",
          "flow": {
            "kind": "final_flow",
            "operators": [
              {
                "tempId": "cam_entry",
                "operatorType": "ImageAcquisition",
                "displayName": "Camera",
                "parameters": { "SourceType": "Camera", "CameraId": "cam-1" }
              }
            ],
            "connections": []
          },
          "entryOperatorTempId": "cam_entry"
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
        capturedInputs!.Should().NotContainKey("Image");
        capturedInputs.Keys.Should().ContainSingle(key => key.StartsWith("ProvidedFrameEnvelope:", StringComparison.OrdinalIgnoreCase));

        var payload = JsonSerializer.SerializeToElement(result.Data);
        payload.GetProperty("replayExecuted").GetBoolean().Should().BeTrue();
        payload.GetProperty("replayKind").GetString().Should().Be("real_frame_runtime_execution");
        payload.GetProperty("usedEntryOperatorTempId").GetString().Should().Be("cam_entry");
        payload.GetProperty("frame").GetProperty("byteLength").GetInt32().Should().Be(frameBytes.Length);
        payload.GetProperty("outputSummary").GetProperty("Image").GetProperty("byteLength").GetInt32().Should().Be(2);
    }

    [Fact(DisplayName = "replay_flow_with_frame should reject multiple acquisition operators without entry temp id")]
    public async Task ReplayFlowWithFrameTool_ShouldRequireEntryOperatorWhenMultipleAcquisitionOperatorsExist()
    {
        var frameStore = new VisionAgentTemporaryFrameStore();
        var temporaryFrameId = frameStore.Store([1, 2, 3], new VisionAgentTemporaryFrameMetadata
        {
            CameraBindingId = "cam-1",
            CameraId = "camera-1"
        });
        var tool = new ReplayFlowWithFrameTool(
            frameStore,
            CreateOperatorFactory(),
            Substitute.For<IFlowExecutionService>());
        using var argsDoc = JsonDocument.Parse($$"""
        {
          "temporaryFrameId": "{{temporaryFrameId}}",
          "flow": {
            "operators": [
              { "tempId": "cam_a", "operatorType": "ImageAcquisition", "parameters": {} },
              { "tempId": "cam_b", "operatorType": "ImageAcquisition", "parameters": {} }
            ],
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

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("multiple_image_acquisition_requires_entry_operator_temp_id");
    }

    private static IOperatorFactory CreateOperatorFactory()
    {
        var factory = Substitute.For<IOperatorFactory>();
        factory.GetMetadata(OperatorType.ImageAcquisition).Returns(new OperatorMetadata
        {
            Type = OperatorType.ImageAcquisition,
            DisplayName = "Image Acquisition",
            OutputPorts =
            [
                new PortDefinition
                {
                    Name = "Image",
                    DisplayName = "Image",
                    DataType = PortDataType.Image
                }
            ],
            Parameters =
            [
                new ParameterDefinition { Name = "SourceType", DataType = "string", DefaultValue = "Camera" },
                new ParameterDefinition { Name = "CameraId", DataType = "string", DefaultValue = "cam-1", IsRequired = false },
                new ParameterDefinition { Name = "FilePath", DataType = "string", DefaultValue = string.Empty, IsRequired = false }
            ]
        });
        return factory;
    }
}
