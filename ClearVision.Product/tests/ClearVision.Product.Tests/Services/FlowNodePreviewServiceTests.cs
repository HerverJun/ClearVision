using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Services;
using ClearVision.Product.Core.ValueObjects;
using ClearVision.Product.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using OpenCvSharp;

namespace ClearVision.Product.Tests.Services;

public class FlowNodePreviewServiceTests
{
    [Fact]
    public async Task PreviewWithMetricsAsync_ShouldInjectExternalImage_WhenFileImageAcquisitionWithoutFilePathExistsUpstream()
    {
        var flowExecution = Substitute.For<IFlowExecutionService>();
        Dictionary<string, object>? capturedInput = null;
        var acquisition = new Operator("Acquire", OperatorType.ImageAcquisition, 0, 0);
        var target = new Operator("Resize", OperatorType.ImageResize, 0, 0);
        var flow = new OperatorFlow("preview-flow");
        flow.AddOperator(acquisition);
        flow.AddOperator(target);
        flow.Connections.Add(new OperatorConnection(acquisition.Id, Guid.NewGuid(), target.Id, Guid.NewGuid()));

        flowExecution.ExecuteDebugWithSnapshotAsync(
                Arg.Any<ExecutionSnapshot>(),
                Arg.Any<DebugOptions>(),
                Arg.Any<Dictionary<string, object>?>(),
                Arg.Any<ClearVision.Product.Core.ProjectVariables.ProjectVariableExecutionContext?>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                capturedInput = callInfo.ArgAt<Dictionary<string, object>?>(2);
                return Task.FromResult(new FlowDebugExecutionResult
                {
                    IsSuccess = true,
                    DebugSessionId = Guid.NewGuid(),
                    IntermediateResults = new Dictionary<Guid, Dictionary<string, object>>
                    {
                        [target.Id] = new()
                        {
                            ["Image"] = CreatePreviewImageBytes()
                        }
                    }
                });
            });

        var service = new FlowNodePreviewService(
            NullLogger<FlowNodePreviewService>.Instance,
            flowExecution,
            Substitute.For<IPreviewMetricsAnalyzer>());

        var result = await service.PreviewWithMetricsAsync(flow, target.Id, new byte[] { 9, 9, 9 });

        result.Success.Should().BeTrue();
        capturedInput.Should().NotBeNull();
        capturedInput!["Image"].Should().BeEquivalentTo(new byte[] { 9, 9, 9 });
    }

    [Fact]
    public async Task PreviewWithMetricsAsync_ShouldInjectExternalImage_WhenTargetIsFileImageAcquisitionWithoutFilePath()
    {
        var flowExecution = Substitute.For<IFlowExecutionService>();
        Dictionary<string, object>? capturedInput = null;
        var target = new Operator("Acquire", OperatorType.ImageAcquisition, 0, 0);
        var flow = new OperatorFlow("preview-flow");
        flow.AddOperator(target);

        flowExecution.ExecuteDebugWithSnapshotAsync(
                Arg.Any<ExecutionSnapshot>(),
                Arg.Any<DebugOptions>(),
                Arg.Any<Dictionary<string, object>?>(),
                Arg.Any<ClearVision.Product.Core.ProjectVariables.ProjectVariableExecutionContext?>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                capturedInput = callInfo.ArgAt<Dictionary<string, object>?>(2);
                return Task.FromResult(new FlowDebugExecutionResult
                {
                    IsSuccess = true,
                    DebugSessionId = Guid.NewGuid(),
                    IntermediateResults = new Dictionary<Guid, Dictionary<string, object>>
                    {
                        [target.Id] = new()
                        {
                            ["Message"] = "captured-from-camera"
                        }
                    }
                });
            });

        var service = new FlowNodePreviewService(
            NullLogger<FlowNodePreviewService>.Instance,
            flowExecution,
            Substitute.For<IPreviewMetricsAnalyzer>());

        var result = await service.PreviewWithMetricsAsync(flow, target.Id, new byte[] { 9, 9, 9 });

        result.Success.Should().BeTrue();
        capturedInput.Should().NotBeNull();
        capturedInput!["Image"].Should().BeEquivalentTo(new byte[] { 9, 9, 9 });
        result.InputImage.Should().Equal(new byte[] { 9, 9, 9 });
    }

    [Fact]
    public async Task PreviewWithMetricsAsync_ShouldNotInjectExternalImage_WhenFileImageAcquisitionHasExplicitFilePath()
    {
        var flowExecution = Substitute.For<IFlowExecutionService>();
        Dictionary<string, object>? capturedInput = null;
        var acquisition = new Operator("Acquire", OperatorType.ImageAcquisition, 0, 0);
        acquisition.AddParameter(new Parameter(Guid.NewGuid(), "SourceType", "SourceType", string.Empty, "enum", "File"));
        acquisition.AddParameter(new Parameter(Guid.NewGuid(), "FilePath", "FilePath", string.Empty, "file", "C:\\missing\\image.png"));
        var target = new Operator("Resize", OperatorType.ImageResize, 0, 0);
        var flow = new OperatorFlow("preview-flow");
        flow.AddOperator(acquisition);
        flow.AddOperator(target);
        flow.Connections.Add(new OperatorConnection(acquisition.Id, Guid.NewGuid(), target.Id, Guid.NewGuid()));

        flowExecution.ExecuteDebugWithSnapshotAsync(
                Arg.Any<ExecutionSnapshot>(),
                Arg.Any<DebugOptions>(),
                Arg.Any<Dictionary<string, object>?>(),
                Arg.Any<ClearVision.Product.Core.ProjectVariables.ProjectVariableExecutionContext?>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                capturedInput = callInfo.ArgAt<Dictionary<string, object>?>(2);
                return Task.FromResult(new FlowDebugExecutionResult
                {
                    IsSuccess = false,
                    DebugSessionId = Guid.NewGuid(),
                    DebugOperatorResults =
                    [
                        new OperatorDebugResult
                        {
                            OperatorId = acquisition.Id,
                            OperatorName = acquisition.Name,
                            IsSuccess = false,
                            ErrorMessage = "图像文件不存在: C:\\missing\\image.png"
                        }
                    ]
                });
            });

        var service = new FlowNodePreviewService(
            NullLogger<FlowNodePreviewService>.Instance,
            flowExecution,
            Substitute.For<IPreviewMetricsAnalyzer>());

        var result = await service.PreviewWithMetricsAsync(flow, target.Id, new byte[] { 9, 9, 9 });

        result.Success.Should().BeFalse();
        capturedInput.Should().BeNull();
        result.ErrorMessage.Should().Contain("图像文件不存在");
    }

    [Fact]
    public async Task PreviewWithMetricsAsync_ShouldUseBundledLabels_WhenLabelsPathIsBlank()
    {
        var flowExecution = Substitute.For<IFlowExecutionService>();
        var target = new Operator("DeepLearning", OperatorType.DeepLearning, 0, 0);
        target.AddParameter(new Parameter(Guid.NewGuid(), "ModelPath", "ModelPath", string.Empty, "string", Path.GetTempFileName()));
        target.AddParameter(new Parameter(Guid.NewGuid(), "LabelsPath", "LabelsPath", string.Empty, "string", string.Empty));
        target.AddParameter(new Parameter(Guid.NewGuid(), "TargetClasses", "TargetClasses", string.Empty, "string", "Wire_Black,Wire_Blue"));

        var flow = new OperatorFlow("wire-sequence-flow");
        flow.AddOperator(target);

        flowExecution.ExecuteDebugWithSnapshotAsync(
                Arg.Any<ExecutionSnapshot>(),
                Arg.Any<DebugOptions>(),
                Arg.Any<Dictionary<string, object>?>(),
                Arg.Any<ClearVision.Product.Core.ProjectVariables.ProjectVariableExecutionContext?>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new FlowDebugExecutionResult
            {
                IsSuccess = true,
                DebugSessionId = Guid.NewGuid(),
                IntermediateResults = new Dictionary<Guid, Dictionary<string, object>>
                {
                    [target.Id] = new()
                    {
                        ["Image"] = CreatePreviewImageBytes()
                    }
                }
            }));

        var service = new FlowNodePreviewService(
            NullLogger<FlowNodePreviewService>.Instance,
            flowExecution,
            Substitute.For<IPreviewMetricsAnalyzer>());

        try
        {
            var result = await service.PreviewWithMetricsAsync(flow, target.Id, null);

            result.Success.Should().BeTrue();
            result.MissingResources.Should().NotContain(item => item.ResourceKey == "DeepLearning.LabelsPath");
        }
        finally
        {
            var modelPath = target.Parameters.Single(item => item.Name == "ModelPath").GetValue()?.ToString();
            if (!string.IsNullOrWhiteSpace(modelPath) && File.Exists(modelPath))
            {
                File.Delete(modelPath);
            }
        }
    }

    [Fact]
    public async Task PreviewWithMetricsAsync_ShouldReportMissingLabels_WhenNoTargetClassesAndNoMetadataOrLabelsFile()
    {
        var flowExecution = Substitute.For<IFlowExecutionService>();
        var target = new Operator("DeepLearning", OperatorType.DeepLearning, 0, 0);
        var modelPath = Path.GetTempFileName();
        target.AddParameter(new Parameter(Guid.NewGuid(), "ModelPath", "ModelPath", string.Empty, "string", modelPath));
        target.AddParameter(new Parameter(Guid.NewGuid(), "LabelsPath", "LabelsPath", string.Empty, "string", string.Empty));
        target.AddParameter(new Parameter(Guid.NewGuid(), "TargetClasses", "TargetClasses", string.Empty, "string", string.Empty));

        var flow = new OperatorFlow("strict-label-contract-flow");
        flow.AddOperator(target);

        flowExecution.ExecuteDebugWithSnapshotAsync(
                Arg.Any<ExecutionSnapshot>(),
                Arg.Any<DebugOptions>(),
                Arg.Any<Dictionary<string, object>?>(),
                Arg.Any<ClearVision.Product.Core.ProjectVariables.ProjectVariableExecutionContext?>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new FlowDebugExecutionResult
            {
                IsSuccess = true,
                DebugSessionId = Guid.NewGuid(),
                IntermediateResults = new Dictionary<Guid, Dictionary<string, object>>
                {
                    [target.Id] = new()
                    {
                        ["Image"] = CreatePreviewImageBytes()
                    }
                }
            }));

        var service = new FlowNodePreviewService(
            NullLogger<FlowNodePreviewService>.Instance,
            flowExecution,
            Substitute.For<IPreviewMetricsAnalyzer>());

        try
        {
            var result = await service.PreviewWithMetricsAsync(flow, target.Id, null);

            result.MissingResources.Should().Contain(item => item.ResourceKey == "DeepLearning.LabelsPath");
            result.DiagnosticCodes.Should().Contain("missing_labels");
        }
        finally
        {
            if (File.Exists(modelPath))
            {
                File.Delete(modelPath);
            }
        }
    }

    [Fact]
    public async Task PreviewWithMetricsAsync_ShouldResolveDeepLearningModelFromCatalog()
    {
        var flowExecution = Substitute.For<IFlowExecutionService>();
        var target = new Operator("DeepLearning", OperatorType.DeepLearning, 0, 0);
        var modelPath = Path.GetTempFileName();
        var catalogPath = CreateTempModelCatalog("demo_detection", "detection", modelPath);
        target.AddParameter(new Parameter(Guid.NewGuid(), "ModelId", "ModelId", string.Empty, "string", "demo_detection"));
        target.AddParameter(new Parameter(Guid.NewGuid(), "ModelCatalogPath", "ModelCatalogPath", string.Empty, "string", catalogPath));
        target.AddParameter(new Parameter(Guid.NewGuid(), "LabelsPath", "LabelsPath", string.Empty, "string", string.Empty));
        target.AddParameter(new Parameter(Guid.NewGuid(), "TargetClasses", "TargetClasses", string.Empty, "string", "Wire_Black,Wire_Blue"));

        var flow = new OperatorFlow("catalog-preview-flow");
        flow.AddOperator(target);

        flowExecution.ExecuteDebugWithSnapshotAsync(
                Arg.Any<ExecutionSnapshot>(),
                Arg.Any<DebugOptions>(),
                Arg.Any<Dictionary<string, object>?>(),
                Arg.Any<ClearVision.Product.Core.ProjectVariables.ProjectVariableExecutionContext?>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new FlowDebugExecutionResult
            {
                IsSuccess = true,
                DebugSessionId = Guid.NewGuid(),
                IntermediateResults = new Dictionary<Guid, Dictionary<string, object>>
                {
                    [target.Id] = new()
                    {
                        ["Image"] = CreatePreviewImageBytes()
                    }
                }
            }));

        var service = new FlowNodePreviewService(
            NullLogger<FlowNodePreviewService>.Instance,
            flowExecution,
            Substitute.For<IPreviewMetricsAnalyzer>());

        try
        {
            var result = await service.PreviewWithMetricsAsync(flow, target.Id, null);

            result.Success.Should().BeTrue();
            result.MissingResources.Should().NotContain(item => item.ResourceKey == "DeepLearning.ModelPath");
            result.MissingResources.Should().NotContain(item => item.ResourceKey == "DeepLearning.LabelsPath");
        }
        finally
        {
            if (File.Exists(modelPath))
            {
                File.Delete(modelPath);
            }

            if (File.Exists(catalogPath))
            {
                File.Delete(catalogPath);
            }
        }
    }

    [Fact]
    public async Task PreviewWithMetricsAsync_ShouldExcludeOriginalImageFromOutputs()
    {
        var flowExecution = Substitute.For<IFlowExecutionService>();
        var target = new Operator("Resize", OperatorType.ImageResize, 0, 0);
        var flow = new OperatorFlow("preview-flow");
        flow.AddOperator(target);
        var previewImage = new byte[] { 1, 2, 3, 4 };
        var originalImage = new byte[] { 9, 8, 7, 6 };

        flowExecution.ExecuteDebugWithSnapshotAsync(
                Arg.Any<ExecutionSnapshot>(),
                Arg.Any<DebugOptions>(),
                Arg.Any<Dictionary<string, object>?>(),
                Arg.Any<ClearVision.Product.Core.ProjectVariables.ProjectVariableExecutionContext?>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new FlowDebugExecutionResult
            {
                IsSuccess = true,
                DebugSessionId = Guid.NewGuid(),
                IntermediateResults = new Dictionary<Guid, Dictionary<string, object>>
                {
                    [target.Id] = new()
                    {
                        ["Image"] = previewImage,
                        ["OriginalImage"] = originalImage,
                        ["ObjectCount"] = 2
                    }
                }
            }));

        var service = new FlowNodePreviewService(
            NullLogger<FlowNodePreviewService>.Instance,
            flowExecution,
            Substitute.For<IPreviewMetricsAnalyzer>());

        var result = await service.PreviewWithMetricsAsync(flow, target.Id, null);

        result.Success.Should().BeTrue();
        result.PreviewImage.Should().Equal(previewImage);
        result.PreviewImage.Should().NotEqual(originalImage);
        result.Outputs.Should().ContainKey("ObjectCount");
        result.Outputs.Should().NotContainKey("Image");
        result.Outputs.Should().NotContainKey("OriginalImage");
    }

    private static byte[] CreatePreviewImageBytes()
    {
        using var image = new Mat(4, 4, MatType.CV_8UC1, Scalar.All(255));
        Cv2.ImEncode(".png", image, out var encoded);
        return encoded;
    }

    private static string CreateTempModelCatalog(string modelId, string type, string artifactPath)
    {
        var catalogPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.json");
        var escapedArtifactPath = artifactPath.Replace("\\", "\\\\");
        var json =
            $$"""
            {
              "models": [
                {
                  "id": "{{modelId}}",
                  "name": "Demo Detection",
                  "type": "{{type}}",
                  "path": "{{escapedArtifactPath}}",
                  "version": "1.0.0",
                  "execution_provider": "cpu"
                }
              ]
            }
            """;

        File.WriteAllText(catalogPath, json);
        return catalogPath;
    }
}
