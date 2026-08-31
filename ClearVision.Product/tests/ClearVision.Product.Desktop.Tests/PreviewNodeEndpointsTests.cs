using System.Collections;
using System.Diagnostics;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using ClearVision.Product.Application.DTOs;
using ClearVision.Product.Application.Services;
using ClearVision.Product.Core.Cameras;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Interfaces;
using ClearVision.Product.Core.Operators;
using ClearVision.Product.Core.ProjectVariables;
using ClearVision.Product.Core.Services;
using ClearVision.Product.Core.ValueObjects;
using ClearVision.Product.Desktop.Endpoints;
using ClearVision.Product.Desktop.Observation;
using ClearVision.Product.Desktop.PreviewArtifacts;
using ClearVision.Product.Infrastructure.Operators;
using ClearVision.Product.Infrastructure.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using OpenCvSharp;
using DetectionResultValue = ClearVision.Product.Core.ValueObjects.DetectionResult;

namespace ClearVision.Product.Desktop.Tests;

[TestClassification(TestDomain.Desktop, TestPurpose.Regression, TestLane.Pr, TestEvidenceType.Contract, TestOracleType.Contract, TestResourceRequirement.None, TestExpectedDuration.Fast, TestFlakyPolicy.Blocking, "desktop", Suites = "DesktopEndpoints")]

public class PreviewNodeEndpointsTests
{
    private const string ArtifactOwnerTestUserId = "preview-owner-user";
    private const string OtherArtifactTestUserId = "preview-other-user";

    [Fact]
    public async Task PreviewNode_UsesBreakAtOperatorAndReturnsTargetOutput()
    {
        var projectId = Guid.NewGuid();
        var targetNodeId = Guid.NewGuid();
        var debugSessionId = Guid.NewGuid();
        var outputBytes = new byte[] { 1, 2, 3, 4 };
        DebugOptions? capturedOptions = null;
        OperatorFlow? capturedFlow = null;

        await using var host = await PreviewNodeTestHost.CreateAsync(flowExecution =>
        {
            flowExecution.ExecuteDebugWithSnapshotAsync(
                    Arg.Any<ExecutionSnapshot>(),
                    Arg.Any<DebugOptions>(),
                    Arg.Any<Dictionary<string, object>?>(),
                    Arg.Any<ClearVision.Product.Core.ProjectVariables.ProjectVariableExecutionContext?>(),
                    Arg.Any<CancellationToken>())
                .Returns(callInfo =>
                {
                    capturedFlow = callInfo.ArgAt<ExecutionSnapshot>(0).CreateExecutionFlow();
                    capturedOptions = callInfo.ArgAt<DebugOptions>(1);

                    return Task.FromResult(new FlowDebugExecutionResult
                    {
                        IsSuccess = true,
                        DebugSessionId = debugSessionId,
                        ExecutionTimeMs = 12,
                        IntermediateResults = new Dictionary<Guid, Dictionary<string, object>>
                        {
                            [targetNodeId] = new()
                            {
                                ["Image"] = outputBytes,
                                ["Score"] = 0.95
                            }
                        },
                        DebugOperatorResults = new List<OperatorDebugResult>
                        {
                            new()
                            {
                                OperatorId = targetNodeId,
                                OperatorName = "Threshold",
                                IsSuccess = true,
                                ExecutionOrder = 0,
                                ExecutionTimeMs = 12
                            }
                        }
                    });
                });
        });

        var request = new PreviewNodeRequest
        {
            ProjectId = projectId,
            TargetNodeId = targetNodeId,
            DebugSessionId = debugSessionId,
            ImageFormat = ".bmp",
            Parameters = new Dictionary<string, object>
            {
                ["Threshold"] = 180
            },
            FlowData = CreateUpdateFlowRequest(
                CreateOperatorDto(
                    targetNodeId,
                    "Threshold",
                    OperatorType.Thresholding,
                    parameters: new Dictionary<string, object> { ["Threshold"] = 128 }))
        };

        using var response = await host.Client.PostAsJsonAsync("/api/flows/preview-node", request);

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        var payload = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(payload);
        document.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
        document.RootElement.GetProperty("targetNodeId").GetGuid().Should().Be(targetNodeId);
        document.RootElement.GetProperty("debugSessionId").GetGuid().Should().Be(debugSessionId);
        document.RootElement.GetProperty("outputImageBase64").GetString().Should().Be(Convert.ToBase64String(outputBytes));
        document.RootElement.GetProperty("executedOperators").GetArrayLength().Should().Be(1);
        var oldClientObservationIdentity = document.RootElement.GetProperty("observation").GetProperty("identity");
        oldClientObservationIdentity.GetProperty("clientRequestSequence").ValueKind.Should().Be(JsonValueKind.Null);
        oldClientObservationIdentity.GetProperty("flowRevision").ValueKind.Should().Be(JsonValueKind.Null);

        capturedOptions.Should().NotBeNull();
        capturedOptions!.BreakAtOperatorId.Should().Be(targetNodeId);
        capturedOptions.DebugSessionId.Should().Be(debugSessionId);
        capturedOptions.ImageFormat.Should().Be(".bmp");
        capturedOptions.EnableIntermediateCache.Should().BeTrue();

        capturedFlow.Should().NotBeNull();
        var thresholdParameter = capturedFlow!.Operators
            .Single(op => op.Id == targetNodeId)
            .Parameters
            .Single(param => param.Name == "Threshold")
            .GetValue();

        ReadIntValue(thresholdParameter).Should().Be(180);
    }

    [Fact]
    public async Task PreviewNode_ArtifactModeReferences_ReturnsArtifactRefsAndSafeBlobEndpoint()
    {
        var projectId = Guid.NewGuid();
        var targetNodeId = Guid.NewGuid();
        var debugSessionId = Guid.NewGuid();
        var imageBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4 };

        await using var host = await PreviewNodeTestHost.CreateAsync(flowExecution =>
        {
            flowExecution.ExecuteDebugWithSnapshotAsync(
                    Arg.Any<ExecutionSnapshot>(),
                    Arg.Any<DebugOptions>(),
                    Arg.Any<Dictionary<string, object>?>(),
                    Arg.Any<ClearVision.Product.Core.ProjectVariables.ProjectVariableExecutionContext?>(),
                    Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(new FlowDebugExecutionResult
                {
                    IsSuccess = true,
                    DebugSessionId = debugSessionId,
                    ExecutionTimeMs = 12,
                    IntermediateResults = new Dictionary<Guid, Dictionary<string, object>>
                    {
                        [targetNodeId] = new()
                        {
                            ["Image"] = imageBytes,
                            ["Score"] = 0.95
                        }
                    },
                    DebugOperatorResults = new List<OperatorDebugResult>
                    {
                        new()
                        {
                            OperatorId = targetNodeId,
                            OperatorName = "Threshold",
                            IsSuccess = true,
                            ExecutionOrder = 0,
                            ExecutionTimeMs = 12
                        }
                    }
                }));
        });

        using var response = await host.Client.PostAsJsonAsync("/api/flows/preview-node", new PreviewNodeRequest
        {
            ProjectId = projectId,
            TargetNodeId = targetNodeId,
            DebugSessionId = debugSessionId,
            ClientRequestSequence = 10,
            FlowRevision = 20,
            ArtifactMode = "references",
            InputImageBase64 = Convert.ToBase64String(imageBytes),
            FlowData = CreateUpdateFlowRequest(
                CreateOperatorDto(targetNodeId, "Threshold", OperatorType.Thresholding))
        });

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        document.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
        document.RootElement.GetProperty("inputImageBase64").ValueKind.Should().Be(JsonValueKind.Null);
        document.RootElement.GetProperty("outputImageBase64").ValueKind.Should().Be(JsonValueKind.Null);
        document.RootElement.GetProperty("outputData").GetProperty("Score").GetDouble().Should().Be(0.95);

        var artifacts = document.RootElement.GetProperty("artifacts").EnumerateArray().ToList();
        artifacts.Should().HaveCountGreaterThanOrEqualTo(2);
        artifacts.Should().OnlyContain(artifact =>
            artifact.GetProperty("artifactId").GetString()!.Length == 43 &&
            artifact.GetProperty("contentType").GetString() == "image/png" &&
            artifact.GetProperty("length").GetInt64() == imageBytes.Length);
        artifacts.Should().Contain(artifact => artifact.GetProperty("role").GetString() == "inputImage");
        artifacts.Should().Contain(artifact => artifact.GetProperty("role").GetString() == "outputImage");

        var artifactId = artifacts.First().GetProperty("artifactId").GetString()!;
        using var artifactResponse = await host.Client.GetAsync($"/api/preview-artifacts/{artifactId}");
        artifactResponse.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        artifactResponse.Content.Headers.ContentType!.MediaType.Should().Be("image/png");
        artifactResponse.Content.Headers.ContentLength.Should().Be(imageBytes.Length);
        artifactResponse.Headers.CacheControl!.NoStore.Should().BeTrue();
        artifactResponse.Headers.ETag!.Tag.Should().StartWith("\"sha256-");
        artifactResponse.Headers.GetValues("X-Content-Type-Options").Should().Contain("nosniff");
        (await artifactResponse.Content.ReadAsByteArrayAsync()).Should().Equal(imageBytes);

        using var otherUserReadRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/preview-artifacts/{artifactId}");
        otherUserReadRequest.Headers.Add("X-Test-User", OtherArtifactTestUserId);
        using var otherUserReadResponse = await host.Client.SendAsync(otherUserReadRequest);
        otherUserReadResponse.StatusCode.Should().Be(System.Net.HttpStatusCode.NotFound);

        using var otherUserDeleteRequest = new HttpRequestMessage(HttpMethod.Delete, $"/api/preview-artifacts/{artifactId}");
        otherUserDeleteRequest.Headers.Add("X-Test-User", OtherArtifactTestUserId);
        using var otherUserDeleteResponse = await host.Client.SendAsync(otherUserDeleteRequest);
        otherUserDeleteResponse.StatusCode.Should().Be(System.Net.HttpStatusCode.NotFound);

        using var ownerReadAfterDeniedDelete = await host.Client.GetAsync($"/api/preview-artifacts/{artifactId}");
        ownerReadAfterDeniedDelete.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);

        using var pathInjection = await host.Client.GetAsync("/api/preview-artifacts/..%2Fsecret");
        pathInjection.StatusCode.Should().Be(System.Net.HttpStatusCode.NotFound);

        using var doubleEncodedPath = await host.Client.GetAsync("/api/preview-artifacts/..%252Fsecret");
        doubleEncodedPath.StatusCode.Should().Be(System.Net.HttpStatusCode.NotFound);

        using var overlongToken = await host.Client.GetAsync($"/api/preview-artifacts/{new string('a', 44)}");
        overlongToken.StatusCode.Should().Be(System.Net.HttpStatusCode.NotFound);

        var variantArtifactId = MutateArtifactId(artifactId);
        using var variantResponse = await host.Client.GetAsync($"/api/preview-artifacts/{variantArtifactId}");
        variantResponse.StatusCode.Should().Be(System.Net.HttpStatusCode.NotFound);

        using var deleteResponse = await host.Client.DeleteAsync($"/api/preview-artifacts/{artifactId}");
        deleteResponse.StatusCode.Should().Be(System.Net.HttpStatusCode.NoContent);
        using var afterDelete = await host.Client.GetAsync($"/api/preview-artifacts/{artifactId}");
        afterDelete.StatusCode.Should().Be(System.Net.HttpStatusCode.NotFound);

        var remainingArtifactId = artifacts
            .Select(item => item.GetProperty("artifactId").GetString()!)
            .First(item => item != artifactId);
        var store = host.Services.GetRequiredService<PreviewArtifactStore>();
        store.RevokeOwner(new PreviewArtifactOwnerScope(
                projectId,
                targetNodeId,
                debugSessionId,
                10,
                20,
                ArtifactOwnerTestUserId))
            .Should()
            .BeGreaterThan(0);
        using var afterRevoke = await host.Client.GetAsync($"/api/preview-artifacts/{remainingArtifactId}");
        afterRevoke.StatusCode.Should().Be(System.Net.HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task PreviewNode_WithChineseFileImageAcquisitionFlowData_ShouldRejectBeforeFileDispatch()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"ClearVision-预览链路-{Guid.NewGuid():N}");
        var filePath = Path.Combine(directory, "中文文件名-样张.png");
        Directory.CreateDirectory(directory);

        try
        {
            using var sourceImage = new Mat(12, 16, MatType.CV_8UC3, new Scalar(32, 128, 240));
            File.WriteAllBytes(filePath, sourceImage.ToBytes(".png"));

            var project = new Project("preview-image-acquisition");
            var acquisitionId = Guid.NewGuid();
            var acquisitionOutputPort = CreatePort("Image", PortDataType.Image, PortDirection.Output);
            var thresholdId = Guid.NewGuid();
            var thresholdInputPort = CreatePort("Image", PortDataType.Image, PortDirection.Input, isRequired: true);
            var thresholdOutputPort = CreatePort("Image", PortDataType.Image, PortDirection.Output);
            var debugSessionId = Guid.NewGuid();

            var acquisition = CreateOperatorDto(
                acquisitionId,
                "ImageAcquisition",
                OperatorType.ImageAcquisition,
                outputPorts: [acquisitionOutputPort],
                parameters: new Dictionary<string, object>
                {
                    ["SourceType"] = "文件",
                    ["FilePath"] = filePath
                });
            var threshold = CreateOperatorDto(
                thresholdId,
                "Threshold",
                OperatorType.Thresholding,
                inputPorts: [thresholdInputPort],
                outputPorts: [thresholdOutputPort],
                parameters: new Dictionary<string, object>
                {
                    ["Threshold"] = 127.0,
                    ["Type"] = "0",
                    ["MaxValue"] = 255.0
                });

            var acquisitionExecutor = new CountingOperatorExecutor(
                new ImageAcquisitionOperator(
                    NullLogger<ImageAcquisitionOperator>.Instance,
                    Substitute.For<ICameraManager>()));
            await using var host = await PreviewNodeTestHost.CreateWithRealFlowExecutionAsync(
                project,
                new ProjectVariableSessionRegistry(),
                [
                    acquisitionExecutor,
                    new ThresholdOperator(NullLogger<ThresholdOperator>.Instance)
                ]);

            using var response = await host.Client.PostAsJsonAsync("/api/flows/preview-node", new PreviewNodeRequest
            {
                ProjectId = project.Id,
                TargetNodeId = thresholdId,
                DebugSessionId = debugSessionId,
                ClientRequestSequence = 11,
                FlowRevision = 22,
                ArtifactMode = "references",
                FlowData = CreateUpdateFlowRequest(
                    acquisition,
                    threshold,
                    CreateConnection(acquisitionId, acquisitionOutputPort.Id, thresholdId, thresholdInputPort.Id))
            });

            var payload = await response.Content.ReadAsStringAsync();
            response.StatusCode.Should().Be(System.Net.HttpStatusCode.BadRequest, payload);
            payload.Should().Contain("ADMISSION_DRAFT_PREVIEW_SIDE_EFFECT_BLOCKED");
            acquisitionExecutor.ExecuteCount.Should().Be(0);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task PreviewNode_WithFileImageAcquisitionTargetAndRuntimeImage_ShouldUseProvidedImage()
    {
        var project = new Project("preview-runtime-image-acquisition");
        var acquisitionId = Guid.NewGuid();
        var acquisitionOutputPort = CreatePort("Image", PortDataType.Image, PortDirection.Output);
        var inputImage = CreateBinaryPreviewImageBytes();

        await using var host = await PreviewNodeTestHost.CreateWithRealFlowExecutionAsync(
            project,
            new ProjectVariableSessionRegistry(),
            [
                new ImageAcquisitionOperator(
                    NullLogger<ImageAcquisitionOperator>.Instance,
                    Substitute.For<ICameraManager>())
            ]);

        using var response = await host.Client.PostAsJsonAsync("/api/flows/preview-node", new PreviewNodeRequest
        {
            ProjectId = project.Id,
            TargetNodeId = acquisitionId,
            InputImageBase64 = Convert.ToBase64String(inputImage),
            FlowData = CreateUpdateFlowRequest(
                CreateOperatorDto(
                    acquisitionId,
                    "ImageAcquisition",
                    OperatorType.ImageAcquisition,
                    outputPorts: [acquisitionOutputPort],
                    parameters: new Dictionary<string, object>
                    {
                        ["SourceType"] = "File",
                        ["FilePath"] = string.Empty
                    }))
        });

        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK, payload);
        using var document = JsonDocument.Parse(payload);
        document.RootElement.GetProperty("success").GetBoolean().Should().BeTrue(payload);
        document.RootElement.GetProperty("outputImageBase64").GetString().Should().NotBeNullOrWhiteSpace();
        document.RootElement.GetProperty("outputData").GetProperty("Source").GetString().Should().Be("provided-image");
    }

    [Fact]
    public async Task PreviewNode_WithDownstreamTargetAndRuntimeImageAcquisitionUpstream_ShouldUseProvidedImage()
    {
        var project = new Project("preview-runtime-image-downstream");
        var acquisitionId = Guid.NewGuid();
        var acquisitionOutputPort = CreatePort("Image", PortDataType.Image, PortDirection.Output);
        var thresholdId = Guid.NewGuid();
        var thresholdInputPort = CreatePort("Image", PortDataType.Image, PortDirection.Input, isRequired: true);
        var thresholdOutputPort = CreatePort("Image", PortDataType.Image, PortDirection.Output);
        var inputImage = CreateBinaryPreviewImageBytes();

        await using var host = await PreviewNodeTestHost.CreateWithRealFlowExecutionAsync(
            project,
            new ProjectVariableSessionRegistry(),
            [
                new ImageAcquisitionOperator(
                    NullLogger<ImageAcquisitionOperator>.Instance,
                    Substitute.For<ICameraManager>()),
                new ThresholdOperator(NullLogger<ThresholdOperator>.Instance)
            ]);

        using var response = await host.Client.PostAsJsonAsync("/api/flows/preview-node", new PreviewNodeRequest
        {
            ProjectId = project.Id,
            TargetNodeId = thresholdId,
            InputImageBase64 = Convert.ToBase64String(inputImage),
            FlowData = CreateUpdateFlowRequest(
                CreateOperatorDto(
                    acquisitionId,
                    "ImageAcquisition",
                    OperatorType.ImageAcquisition,
                    outputPorts: [acquisitionOutputPort],
                    parameters: new Dictionary<string, object>
                    {
                        ["SourceType"] = "File",
                        ["FilePath"] = string.Empty
                    }),
                CreateOperatorDto(
                    thresholdId,
                    "Threshold",
                    OperatorType.Thresholding,
                    inputPorts: [thresholdInputPort],
                    outputPorts: [thresholdOutputPort],
                    parameters: new Dictionary<string, object>
                    {
                        ["Threshold"] = 127.0,
                        ["Type"] = "0",
                        ["MaxValue"] = 255.0
                    }),
                CreateConnection(acquisitionId, acquisitionOutputPort.Id, thresholdId, thresholdInputPort.Id))
        });

        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK, payload);
        using var document = JsonDocument.Parse(payload);
        document.RootElement.GetProperty("success").GetBoolean().Should().BeTrue(payload);
        document.RootElement.GetProperty("outputImageBase64").GetString().Should().NotBeNullOrWhiteSpace();
        payload.Should().NotContain("FilePath is required");
    }

    [Fact]
    public async Task PreviewNode_WithCapturedCameraFrameSource_ShouldExecuteDownstreamWithoutReadingCamera()
    {
        var project = new Project("preview-captured-camera-frame-downstream");
        var cameraManager = Substitute.For<ICameraManager>();
        var acquisitionId = Guid.NewGuid();
        var acquisitionOutputPort = CreatePort("Image", PortDataType.Image, PortDirection.Output);
        var equalizationId = Guid.NewGuid();
        var equalizationInputPort = CreatePort("Image", PortDataType.Image, PortDirection.Input, isRequired: true);
        var equalizationOutputPort = CreatePort("Image", PortDataType.Image, PortDirection.Output);
        var blobId = Guid.NewGuid();
        var blobInputPort = CreatePort("Image", PortDataType.Image, PortDirection.Input, isRequired: true);
        var blobOutputPort = CreatePort("Image", PortDataType.Image, PortDirection.Output);
        var blobCountOutputPort = CreatePort("BlobCount", PortDataType.Integer, PortDirection.Output);
        var inputImage = CreateTwoAreaBlobPreviewImageBytes();

        await using var host = await PreviewNodeTestHost.CreateWithRealFlowExecutionAsync(
            project,
            new ProjectVariableSessionRegistry(),
            [
                new ImageAcquisitionOperator(
                    NullLogger<ImageAcquisitionOperator>.Instance,
                    cameraManager),
                new HistogramEqualizationOperator(NullLogger<HistogramEqualizationOperator>.Instance),
                new BlobDetectionOperator(NullLogger<BlobDetectionOperator>.Instance)
            ]);

        var flowData = CreateUpdateFlowRequest(
            CreateOperatorDto(
                acquisitionId,
                "ImageAcquisition",
                OperatorType.ImageAcquisition,
                outputPorts: [acquisitionOutputPort],
                parameters: new Dictionary<string, object>
                {
                    ["SourceType"] = "Camera",
                    ["CameraId"] = "cam-preview"
                }),
            CreateOperatorDto(
                equalizationId,
                "HistogramEqualization",
                OperatorType.HistogramEqualization,
                inputPorts: [equalizationInputPort],
                outputPorts: [equalizationOutputPort],
                parameters: new Dictionary<string, object>
                {
                    ["Method"] = "Global"
                }),
            CreateOperatorDto(
                blobId,
                "BlobAnalysis",
                OperatorType.BlobAnalysis,
                inputPorts: [blobInputPort],
                outputPorts: [blobOutputPort, blobCountOutputPort],
                parameters: new Dictionary<string, object>
                {
                    ["MinArea"] = 1,
                    ["MaxArea"] = 100000,
                    ["Color"] = "White"
                }),
            CreateConnection(acquisitionId, acquisitionOutputPort.Id, equalizationId, equalizationInputPort.Id),
            CreateConnection(equalizationId, equalizationOutputPort.Id, blobId, blobInputPort.Id));

        using var response = await host.Client.PostAsJsonAsync("/api/flows/preview-node", new PreviewNodeRequest
        {
            ProjectId = project.Id,
            TargetNodeId = blobId,
            InputImageSourceNodeId = acquisitionId,
            InputImageBase64 = Convert.ToBase64String(inputImage),
            FlowData = flowData
        });

        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK, payload);
        using var document = JsonDocument.Parse(payload);
        document.RootElement.GetProperty("success").GetBoolean().Should().BeTrue(payload);
        document.RootElement.GetProperty("executedOperators").EnumerateArray()
            .Select(item => item.GetProperty("operatorId").GetGuid())
            .Should().Contain([acquisitionId, equalizationId, blobId]);
        document.RootElement.GetProperty("outputData").GetProperty("BlobCount").GetInt32().Should().Be(2);
        flowData.Operators.Single(item => item.Id == acquisitionId).Parameters
            .Single(item => item.Name == "SourceType").Value.Should().Be("Camera");
        await cameraManager.DidNotReceive().GetOrCreateByBindingAsync(Arg.Any<string>());
    }

    [Fact]
    public async Task PreviewNode_WithCameraFlowButNoCapturedSource_ShouldRemainAdmissionBlocked()
    {
        var projectId = Guid.NewGuid();
        var acquisitionId = Guid.NewGuid();
        var targetNodeId = Guid.NewGuid();
        var acquisitionOutput = CreatePort("Image", PortDataType.Image, PortDirection.Output);
        var targetInput = CreatePort("Image", PortDataType.Image, PortDirection.Input, isRequired: true);
        var targetOutput = CreatePort("Image", PortDataType.Image, PortDirection.Output);

        await using var host = await PreviewNodeTestHost.CreateAsync(_ => { });
        using var response = await host.Client.PostAsJsonAsync("/api/flows/preview-node", new PreviewNodeRequest
        {
            ProjectId = projectId,
            TargetNodeId = targetNodeId,
            InputImageBase64 = Convert.ToBase64String(CreateBinaryPreviewImageBytes()),
            FlowData = CreateUpdateFlowRequest(
                CreateOperatorDto(
                    acquisitionId,
                    "Camera",
                    OperatorType.ImageAcquisition,
                    outputPorts: [acquisitionOutput],
                    parameters: new Dictionary<string, object>
                    {
                        ["SourceType"] = "Camera",
                        ["CameraId"] = "cam-preview"
                    }),
                CreateOperatorDto(
                    targetNodeId,
                    "Resize",
                    OperatorType.ImageResize,
                    inputPorts: [targetInput],
                    outputPorts: [targetOutput]),
                CreateConnection(acquisitionId, acquisitionOutput.Id, targetNodeId, targetInput.Id))
        });

        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.BadRequest, payload);
        payload.Should().Contain("ADMISSION_DRAFT_PREVIEW_SIDE_EFFECT_BLOCKED");
    }

    [Theory]
    [InlineData("missing-image")]
    [InlineData("missing-source")]
    [InlineData("not-upstream")]
    [InlineData("not-acquisition")]
    [InlineData("not-camera-mode")]
    [InlineData("disabled")]
    [InlineData("not-root")]
    public async Task PreviewNode_WithInvalidCapturedFrameSource_ShouldFailClosed(string scenario)
    {
        var projectId = Guid.NewGuid();
        var upstreamId = Guid.NewGuid();
        var sourceNodeId = scenario == "missing-source" ? Guid.NewGuid() : upstreamId;
        var targetNodeId = Guid.NewGuid();
        var upstreamOutput = CreatePort("Image", PortDataType.Image, PortDirection.Output);
        var targetInput = CreatePort("Image", PortDataType.Image, PortDirection.Input, isRequired: true);
        var targetOutput = CreatePort("Image", PortDataType.Image, PortDirection.Output);
        var items = new List<object>();

        if (scenario == "not-root")
        {
            var predecessorId = Guid.NewGuid();
            var predecessorOutput = CreatePort("Image", PortDataType.Image, PortDirection.Output);
            var upstreamInput = CreatePort("Image", PortDataType.Image, PortDirection.Input);
            items.Add(CreateOperatorDto(predecessorId, "Predecessor", OperatorType.ImageResize, outputPorts: [predecessorOutput]));
            items.Add(CreateOperatorDto(
                upstreamId,
                "Camera",
                OperatorType.ImageAcquisition,
                inputPorts: [upstreamInput],
                outputPorts: [upstreamOutput],
                parameters: new Dictionary<string, object>
                {
                    ["SourceType"] = "Camera",
                    ["CameraId"] = "cam-preview"
                }));
            items.Add(CreateConnection(predecessorId, predecessorOutput.Id, upstreamId, upstreamInput.Id));
        }
        else
        {
            var source = CreateOperatorDto(
                upstreamId,
                "Source",
                scenario == "not-acquisition" ? OperatorType.ImageResize : OperatorType.ImageAcquisition,
                outputPorts: [upstreamOutput],
                parameters: scenario == "not-acquisition"
                    ? null
                    : new Dictionary<string, object>
                    {
                        ["SourceType"] = scenario == "not-camera-mode" ? "File" : "Camera",
                        ["CameraId"] = "cam-preview"
                    });
            source.IsEnabled = scenario != "disabled";
            items.Add(source);
        }

        if (scenario == "not-upstream")
        {
            sourceNodeId = Guid.NewGuid();
            items.Add(CreateOperatorDto(
                sourceNodeId,
                "Unrelated camera",
                OperatorType.ImageAcquisition,
                outputPorts: [CreatePort("Image", PortDataType.Image, PortDirection.Output)],
                parameters: new Dictionary<string, object>
                {
                    ["SourceType"] = "Camera",
                    ["CameraId"] = "cam-unrelated"
                }));
        }

        items.Add(CreateOperatorDto(
            targetNodeId,
            "Target",
            OperatorType.ImageResize,
            inputPorts: [targetInput],
            outputPorts: [targetOutput]));
        items.Add(CreateConnection(upstreamId, upstreamOutput.Id, targetNodeId, targetInput.Id));

        await using var host = await PreviewNodeTestHost.CreateAsync(_ => { });
        using var response = await host.Client.PostAsJsonAsync("/api/flows/preview-node", new PreviewNodeRequest
        {
            ProjectId = projectId,
            TargetNodeId = targetNodeId,
            InputImageSourceNodeId = sourceNodeId,
            InputImageBase64 = scenario == "missing-image"
                ? null
                : Convert.ToBase64String(CreateBinaryPreviewImageBytes()),
            FlowData = CreateUpdateFlowRequest(items.ToArray())
        });

        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.BadRequest, payload);
        payload.Should().Contain("PREVIEW_CAPTURED_FRAME_SOURCE_INVALID");
    }

    [Fact]
    public async Task PreviewNode_WithAnotherCameraUpstream_ShouldKeepRemainingDeviceReadBlocked()
    {
        var projectId = Guid.NewGuid();
        var firstCameraId = Guid.NewGuid();
        var secondCameraId = Guid.NewGuid();
        var targetNodeId = Guid.NewGuid();
        var firstOutput = CreatePort("Image", PortDataType.Image, PortDirection.Output);
        var secondOutput = CreatePort("Image", PortDataType.Image, PortDirection.Output);
        var firstInput = CreatePort("First", PortDataType.Image, PortDirection.Input);
        var secondInput = CreatePort("Second", PortDataType.Image, PortDirection.Input);

        await using var host = await PreviewNodeTestHost.CreateAsync(_ => { });
        using var response = await host.Client.PostAsJsonAsync("/api/flows/preview-node", new PreviewNodeRequest
        {
            ProjectId = projectId,
            TargetNodeId = targetNodeId,
            InputImageSourceNodeId = firstCameraId,
            InputImageBase64 = Convert.ToBase64String(CreateBinaryPreviewImageBytes()),
            FlowData = CreateUpdateFlowRequest(
                CreateOperatorDto(
                    firstCameraId,
                    "Camera A",
                    OperatorType.ImageAcquisition,
                    outputPorts: [firstOutput],
                    parameters: new Dictionary<string, object> { ["SourceType"] = "Camera", ["CameraId"] = "cam-a" }),
                CreateOperatorDto(
                    secondCameraId,
                    "Camera B",
                    OperatorType.ImageAcquisition,
                    outputPorts: [secondOutput],
                    parameters: new Dictionary<string, object> { ["SourceType"] = "Camera", ["CameraId"] = "cam-b" }),
                CreateOperatorDto(
                    targetNodeId,
                    "Target",
                    OperatorType.ResultJudgment,
                    inputPorts: [firstInput, secondInput]),
                CreateConnection(firstCameraId, firstOutput.Id, targetNodeId, firstInput.Id),
                CreateConnection(secondCameraId, secondOutput.Id, targetNodeId, secondInput.Id))
        });

        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.BadRequest, payload);
        payload.Should().Contain("ADMISSION_DRAFT_PREVIEW_SIDE_EFFECT_BLOCKED");
        payload.Should().Contain("device side effects");
    }

    [Fact]
    public async Task PreviewNode_BlobAnalysis_ShouldPersistMaxAreaAndReturnOnlyFilteredBlobsInArtifactPreview()
    {
        var project = new Project("preview-blob-analysis");
        var acquisitionId = Guid.NewGuid();
        var acquisitionOutput = CreatePort("Image", PortDataType.Image, PortDirection.Output);
        var blobId = Guid.NewGuid();
        var blobInput = CreatePort("Image", PortDataType.Image, PortDirection.Input, isRequired: true);
        var blobImageOutput = CreatePort("Image", PortDataType.Image, PortDirection.Output);
        var blobListOutput = CreatePort("Blobs", PortDataType.BlobList, PortDirection.Output);
        var blobCountOutput = CreatePort("BlobCount", PortDataType.Integer, PortDirection.Output);
        var blobExecutor = new CountingOperatorExecutor(
            new BlobDetectionOperator(NullLogger<BlobDetectionOperator>.Instance));
        var acquisition = CreateOperatorDto(
            acquisitionId,
            "ImageAcquisition",
            OperatorType.ImageAcquisition,
            outputPorts: [acquisitionOutput],
            parameters: new Dictionary<string, object>
            {
                ["SourceType"] = "File",
                ["FilePath"] = string.Empty
            });
        var blob = CreateOperatorDto(
            blobId,
            "BlobAnalysis",
            OperatorType.BlobAnalysis,
            inputPorts: [blobInput],
            outputPorts: [blobImageOutput, blobListOutput, blobCountOutput],
            parameters: new Dictionary<string, object>
            {
                ["MinArea"] = 0,
                ["MaxArea"] = 200,
                ["FeatureFilter"] = string.Empty
            });
        var flowData = CreateUpdateFlowRequest(
            acquisition,
            blob,
            CreateConnection(acquisitionId, acquisitionOutput.Id, blobId, blobInput.Id));

        await using var host = await PreviewNodeTestHost.CreateWithRealFlowExecutionAsync(
            project,
            new ProjectVariableSessionRegistry(),
            [
                new ImageAcquisitionOperator(
                    NullLogger<ImageAcquisitionOperator>.Instance,
                    Substitute.For<ICameraManager>()),
                blobExecutor
            ]);

        var request = new PreviewNodeRequest
        {
            ProjectId = project.Id,
            TargetNodeId = blobId,
            DebugSessionId = Guid.NewGuid(),
            ClientRequestSequence = 1,
            FlowRevision = 1,
            ArtifactMode = "references",
            InputImageBase64 = Convert.ToBase64String(CreateTwoAreaBlobPreviewImageBytes()),
            FlowData = flowData
        };

        using var response = await host.Client.PostAsJsonAsync("/api/flows/preview-node", request);
        var payload = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK, payload);
        using var document = JsonDocument.Parse(payload);
        document.RootElement.GetProperty("success").GetBoolean().Should().BeTrue(payload);
        var outputData = document.RootElement.GetProperty("outputData");
        outputData.GetProperty("BlobCount").GetInt32().Should().Be(1);
        outputData.TryGetProperty("Blobs", out var blobs).Should().BeTrue();
        (blobs.ValueKind is JsonValueKind.Array or JsonValueKind.Object).Should().BeTrue(
            "the filtered Blob result remains available to the bounded preview transport");
        document.RootElement.GetProperty("observation").GetProperty("summary").EnumerateArray()
            .Should().Contain(item =>
                item.GetProperty("key").GetString() == "BlobCount" &&
                item.GetProperty("displayValue").GetString() == "1");

        blobExecutor.LastExecutedOperator.Should().NotBeNull();
        ReadIntValue(blobExecutor.LastExecutedOperator!.Parameters.Single(parameter => parameter.Name == "MaxArea").GetValue())
            .Should().Be(200);

        var outputArtifactId = document.RootElement.GetProperty("artifacts").EnumerateArray()
            .Single(artifact => artifact.GetProperty("role").GetString() == "outputImage")
            .GetProperty("artifactId").GetString()!;
        using var artifactResponse = await host.Client.GetAsync($"/api/preview-artifacts/{outputArtifactId}");
        artifactResponse.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        using var previewImage = Cv2.ImDecode(await artifactResponse.Content.ReadAsByteArrayAsync(), ImreadModes.Color);
        CountGreenPixels(previewImage, new OpenCvSharp.Rect(6, 6, 20, 20)).Should().BeGreaterThan(0,
            "the retained Blob is marked in the preview image");
        CountGreenPixels(previewImage, new OpenCvSharp.Rect(55, 5, 40, 30)).Should().Be(0,
            "the filtered-out large Blob remains in the source background without a pass marker");

        blob.Parameters.Single(parameter => parameter.Name == "MaxArea").Value = 1000;
        request.DebugSessionId = Guid.NewGuid();
        request.ClientRequestSequence = 2;
        request.FlowRevision = 2;
        using var secondResponse = await host.Client.PostAsJsonAsync("/api/flows/preview-node", request);
        var secondPayload = await secondResponse.Content.ReadAsStringAsync();

        secondResponse.StatusCode.Should().Be(System.Net.HttpStatusCode.OK, secondPayload);
        using var secondDocument = JsonDocument.Parse(secondPayload);
        secondDocument.RootElement.GetProperty("outputData").GetProperty("BlobCount").GetInt32().Should().Be(2);
        secondDocument.RootElement.GetProperty("outputData").TryGetProperty("Blobs", out _).Should().BeTrue();
        ReadIntValue(blobExecutor.LastExecutedOperator!.Parameters.Single(parameter => parameter.Name == "MaxArea").GetValue())
            .Should().Be(1000);
    }

    [Fact]
    public async Task PreviewNode_BlobAnalysis_ShouldPreserveFiveItemListSemanticsAndDetailedFeatureSwitch()
    {
        var project = new Project("preview-blob-list-semantics");
        var acquisitionId = Guid.NewGuid();
        var blobId = Guid.NewGuid();
        var acquisitionOutput = CreatePort("Image", PortDataType.Image, PortDirection.Output);
        var blobInput = CreatePort("Image", PortDataType.Image, PortDirection.Input, isRequired: true);
        var outputPorts = new List<PortDto>
        {
            CreatePort("Image", PortDataType.Image, PortDirection.Output),
            CreatePort("Blobs", PortDataType.BlobList, PortDirection.Output),
            CreatePort("BlobFeatures", PortDataType.BlobFeatureList, PortDirection.Output),
            CreatePort("BlobCount", PortDataType.Integer, PortDirection.Output)
        };
        var acquisition = CreateOperatorDto(
            acquisitionId,
            "ImageAcquisition",
            OperatorType.ImageAcquisition,
            outputPorts: [acquisitionOutput],
            parameters: new Dictionary<string, object> { ["SourceType"] = "File", ["FilePath"] = string.Empty });
        var blob = CreateOperatorDto(
            blobId,
            "BlobAnalysis",
            OperatorType.BlobAnalysis,
            inputPorts: [blobInput],
            outputPorts: outputPorts,
            parameters: new Dictionary<string, object>
            {
                ["MinArea"] = 1,
                ["MaxArea"] = 1000,
                ["Color"] = "White",
                ["OutputDetailedFeatures"] = false
            });

        await using var host = await PreviewNodeTestHost.CreateWithRealFlowExecutionAsync(
            project,
            new ProjectVariableSessionRegistry(),
            [
                new ImageAcquisitionOperator(NullLogger<ImageAcquisitionOperator>.Instance, Substitute.For<ICameraManager>()),
                new BlobDetectionOperator(NullLogger<BlobDetectionOperator>.Instance)
            ]);
        var request = new PreviewNodeRequest
        {
            ProjectId = project.Id,
            TargetNodeId = blobId,
            DebugSessionId = Guid.NewGuid(),
            ClientRequestSequence = 1,
            FlowRevision = 1,
            ArtifactMode = "references",
            InputImageBase64 = Convert.ToBase64String(CreateFiveBlobPreviewImageBytes()),
            FlowData = CreateUpdateFlowRequest(
                acquisition,
                blob,
                CreateConnection(acquisitionId, acquisitionOutput.Id, blobId, blobInput.Id))
        };

        using var response = await host.Client.PostAsJsonAsync("/api/flows/preview-node", request);
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK, payload);
        using var document = JsonDocument.Parse(payload);
        var outputData = document.RootElement.GetProperty("outputData");
        outputData.GetProperty("BlobCount").GetInt32().Should().Be(5);
        outputData.GetProperty("Blobs").GetArrayLength().Should().Be(5);
        outputData.GetProperty("BlobFeatures").GetArrayLength().Should().Be(0);
        var blobsNode = GetObservationOutputNode(document.RootElement, "Blobs");
        blobsNode.GetProperty("semanticKind").GetString().Should().Be("blob-list");
        blobsNode.GetProperty("declaredPortDataType").GetString().Should().Be("BlobList");
        blobsNode.GetProperty("visibleItemCount").GetInt32().Should().Be(5);
        blobsNode.GetProperty("totalItemCount").GetInt32().Should().Be(5);
        var firstBlob = blobsNode.GetProperty("children")[0];
        firstBlob.GetProperty("children").EnumerateArray().Select(item => item.GetProperty("name").GetString())
            .Should().Contain(["Id", "Area", "X", "Y", "Width", "Height", "CenterX", "CenterY", "Circularity"]);

        blob.Parameters.Single(parameter => parameter.Name == "OutputDetailedFeatures").Value = true;
        request.DebugSessionId = Guid.NewGuid();
        request.ClientRequestSequence = 2;
        request.FlowRevision = 2;
        using var detailedResponse = await host.Client.PostAsJsonAsync("/api/flows/preview-node", request);
        var detailedPayload = await detailedResponse.Content.ReadAsStringAsync();
        detailedResponse.StatusCode.Should().Be(System.Net.HttpStatusCode.OK, detailedPayload);
        using var detailedDocument = JsonDocument.Parse(detailedPayload);
        detailedDocument.RootElement.GetProperty("outputData").GetProperty("BlobFeatures").GetArrayLength().Should().Be(5);
        var featuresNode = GetObservationOutputNode(detailedDocument.RootElement, "BlobFeatures");
        featuresNode.GetProperty("semanticKind").GetString().Should().Be("blob-feature-list");
        featuresNode.GetProperty("totalItemCount").GetInt32().Should().Be(5);
    }

    [Fact]
    public async Task PreviewNode_BoxNms_ShouldExposeRealDetectionCountsInsteadOfWrapperFieldCounts()
    {
        var project = new Project("preview-box-nms-semantics");
        var sourceId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var sourceOutput = CreatePort("Detections", PortDataType.DetectionList, PortDirection.Output);
        var targetInput = CreatePort("Detections", PortDataType.DetectionList, PortDirection.Input, isRequired: true);
        var targetOutputs = new List<PortDto>
        {
            CreatePort("Detections", PortDataType.DetectionList, PortDirection.Output),
            CreatePort("Count", PortDataType.Integer, PortDirection.Output),
            CreatePort("InputCount", PortDataType.Integer, PortDirection.Output),
            CreatePort("SuppressedCount", PortDataType.Integer, PortDirection.Output),
            CreatePort("SuppressedDetections", PortDataType.DetectionList, PortDirection.Output),
            CreatePort("Diagnostics", PortDataType.Any, PortDirection.Output)
        };
        var detections = new DetectionList([
            new DetectionResultValue("a", 0.99f, 0, 0, 10, 10),
            new DetectionResultValue("b", 0.98f, 20, 0, 10, 10),
            new DetectionResultValue("c", 0.97f, 40, 0, 10, 10),
            new DetectionResultValue("d", 0.96f, 60, 0, 10, 10),
            new DetectionResultValue("e", 0.95f, 80, 0, 10, 10)
        ]);
        var source = CreateOperatorDto(sourceId, "DetectionSource", OperatorType.ConditionalBranch, outputPorts: [sourceOutput]);
        var target = CreateOperatorDto(
            targetId,
            "BoxNms",
            OperatorType.BoxNms,
            inputPorts: [targetInput],
            outputPorts: targetOutputs,
            parameters: new Dictionary<string, object> { ["ScoreThreshold"] = 0.1, ["IouThreshold"] = 0.5 });

        await using var host = await PreviewNodeTestHost.CreateWithRealFlowExecutionAsync(
            project,
            new ProjectVariableSessionRegistry(),
            [
                new FixedOutputOperatorExecutor(OperatorType.ConditionalBranch, new Dictionary<string, object> { ["Detections"] = detections }),
                new BoxNmsOperator(NullLogger<BoxNmsOperator>.Instance)
            ]);
        using var response = await host.Client.PostAsJsonAsync("/api/flows/preview-node", new PreviewNodeRequest
        {
            ProjectId = project.Id,
            TargetNodeId = targetId,
            DebugSessionId = Guid.NewGuid(),
            ClientRequestSequence = 1,
            FlowRevision = 1,
            ArtifactMode = "references",
            FlowData = CreateUpdateFlowRequest(source, target, CreateConnection(sourceId, sourceOutput.Id, targetId, targetInput.Id))
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK, payload);
        using var document = JsonDocument.Parse(payload);
        document.RootElement.GetProperty("outputData").GetProperty("Detections").GetProperty("Count").GetInt32().Should().Be(5);
        var node = GetObservationOutputNode(document.RootElement, "Detections");
        node.GetProperty("children").EnumerateArray().Select(item => item.GetProperty("name").GetString())
            .Should().Contain(["Count", "Detections", "AverageConfidence"]);
        node.GetProperty("totalItemCount").GetInt32().Should().Be(5);
        node.GetProperty("semanticKind").GetString().Should().Be("detection-list");
        GetObservationOutputNode(document.RootElement, "SuppressedDetections").GetProperty("totalItemCount").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task PreviewNode_PointSetTool_ShouldExposePointListAndGeometrySemantics()
    {
        var project = new Project("preview-point-set-semantics");
        var sourceId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var sourceOutput = CreatePort("Points", PortDataType.PointList, PortDirection.Output);
        var targetInput = CreatePort("Points1", PortDataType.PointList, PortDirection.Input, isRequired: true);
        var targetOutputs = new List<PortDto>
        {
            CreatePort("Points", PortDataType.PointList, PortDirection.Output),
            CreatePort("Count", PortDataType.Integer, PortDirection.Output),
            CreatePort("Center", PortDataType.Point, PortDirection.Output),
            CreatePort("BoundingBox", PortDataType.Rectangle, PortDirection.Output)
        };
        var points = Enumerable.Range(0, 5).Select(index => new Position(index * 10, index * 2)).ToList();
        var source = CreateOperatorDto(sourceId, "PointSource", OperatorType.ConditionalBranch, outputPorts: [sourceOutput]);
        var target = CreateOperatorDto(targetId, "PointSetTool", OperatorType.PointSetTool, inputPorts: [targetInput], outputPorts: targetOutputs);
        await using var host = await PreviewNodeTestHost.CreateWithRealFlowExecutionAsync(
            project,
            new ProjectVariableSessionRegistry(),
            [
                new FixedOutputOperatorExecutor(OperatorType.ConditionalBranch, new Dictionary<string, object> { ["Points"] = points }),
                new PointSetToolOperator(NullLogger<PointSetToolOperator>.Instance)
            ]);
        using var response = await host.Client.PostAsJsonAsync("/api/flows/preview-node", new PreviewNodeRequest
        {
            ProjectId = project.Id,
            TargetNodeId = targetId,
            DebugSessionId = Guid.NewGuid(),
            ClientRequestSequence = 1,
            FlowRevision = 1,
            ArtifactMode = "references",
            FlowData = CreateUpdateFlowRequest(source, target, CreateConnection(sourceId, sourceOutput.Id, targetId, targetInput.Id))
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK, payload);
        using var document = JsonDocument.Parse(payload);
        GetObservationOutputNode(document.RootElement, "Points").GetProperty("totalItemCount").GetInt32().Should().Be(5);
        GetObservationOutputNode(document.RootElement, "Center").GetProperty("displayValue").GetString().Should().Be("(20, 4)");
        GetObservationOutputNode(document.RootElement, "BoundingBox").GetProperty("displayValue").GetString().Should().Be("0, 0, 40 x 8");
    }

    [Fact]
    public async Task PreviewNode_BinaryImageToRegion_ShouldExposeRegionSemanticInsteadOfObjectFieldCount()
    {
        var project = new Project("preview-region-semantics");
        var acquisitionId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var acquisitionOutput = CreatePort("Image", PortDataType.Image, PortDirection.Output);
        var targetInput = CreatePort("Image", PortDataType.Image, PortDirection.Input, isRequired: true);
        var targetOutputs = new List<PortDto>
        {
            CreatePort("Region", PortDataType.Region, PortDirection.Output),
            CreatePort("Image", PortDataType.Image, PortDirection.Output),
            CreatePort("Area", PortDataType.Integer, PortDirection.Output)
        };
        var acquisition = CreateOperatorDto(
            acquisitionId,
            "ImageAcquisition",
            OperatorType.ImageAcquisition,
            outputPorts: [acquisitionOutput],
            parameters: new Dictionary<string, object> { ["SourceType"] = "File", ["FilePath"] = string.Empty });
        var target = CreateOperatorDto(
            targetId,
            "BinaryImageToRegion",
            OperatorType.BinaryImageToRegion,
            inputPorts: [targetInput],
            outputPorts: targetOutputs);
        await using var host = await PreviewNodeTestHost.CreateWithRealFlowExecutionAsync(
            project,
            new ProjectVariableSessionRegistry(),
            [
                new ImageAcquisitionOperator(NullLogger<ImageAcquisitionOperator>.Instance, Substitute.For<ICameraManager>()),
                new BinaryImageToRegionOperator(NullLogger<BinaryImageToRegionOperator>.Instance)
            ]);

        using var response = await host.Client.PostAsJsonAsync("/api/flows/preview-node", new PreviewNodeRequest
        {
            ProjectId = project.Id,
            TargetNodeId = targetId,
            DebugSessionId = Guid.NewGuid(),
            ClientRequestSequence = 1,
            FlowRevision = 1,
            ArtifactMode = "references",
            InputImageBase64 = Convert.ToBase64String(CreateBinaryPreviewImageBytes()),
            FlowData = CreateUpdateFlowRequest(
                acquisition,
                target,
                CreateConnection(acquisitionId, acquisitionOutput.Id, targetId, targetInput.Id))
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK, payload);
        using var document = JsonDocument.Parse(payload);
        document.RootElement.GetProperty("outputData").GetProperty("Area").GetInt32().Should().Be(2);
        var region = GetObservationOutputNode(document.RootElement, "Region");
        region.GetProperty("declaredPortDataType").GetString().Should().Be("Region");
        region.GetProperty("semanticKind").GetString().Should().Be("geometry");
    }

    [Fact]
    public async Task PreviewNode_StoredProjectFlowRoundTrip_ShouldPreserveBlobParametersAndFilterResult()
    {
        var repository = Substitute.For<IProjectRepository>();
        Project? persistedProject = null;
        repository.AddAsync(Arg.Any<Project>())
            .Returns(callInfo =>
            {
                persistedProject = callInfo.Arg<Project>();
                return Task.FromResult(persistedProject);
            });
        repository.GetByIdAsync(Arg.Any<Guid>())
            .Returns(_ => Task.FromResult(persistedProject));
        repository.GetByIdFreshAsync(Arg.Any<Guid>())
            .Returns(_ => Task.FromResult(persistedProject));
        repository.GetByIdForUpdateAsync(Arg.Any<Guid>())
            .Returns(_ => Task.FromResult(persistedProject));

        var flowRoot = Path.Combine(Path.GetTempPath(), $"ClearVision.BlobRoundTrip.{Guid.NewGuid():N}");
        var flowStorage = new JsonFileProjectFlowStorage(flowRoot);
        try
        {
            var acquisitionId = Guid.NewGuid();
            var blobId = Guid.NewGuid();
            var acquisitionOutput = CreatePort("Image", PortDataType.Image, PortDirection.Output);
            var blobInput = CreatePort("Image", PortDataType.Image, PortDirection.Input, isRequired: true);
            var blobImageOutput = CreatePort("Image", PortDataType.Image, PortDirection.Output);
            var blobListOutput = CreatePort("Blobs", PortDataType.BlobList, PortDirection.Output);
            var blobCountOutput = CreatePort("BlobCount", PortDataType.Integer, PortDirection.Output);
            var acquisition = CreateOperatorDto(
                acquisitionId,
                "ImageAcquisition",
                OperatorType.ImageAcquisition,
                outputPorts: [acquisitionOutput],
                parameters: new Dictionary<string, object>
                {
                    ["SourceType"] = "File",
                    ["FilePath"] = string.Empty
                });
            var blob = CreateOperatorDto(
                blobId,
                "BlobAnalysis",
                OperatorType.BlobAnalysis,
                inputPorts: [blobInput],
                outputPorts: [blobImageOutput, blobListOutput, blobCountOutput],
                parameters: new Dictionary<string, object>
                {
                    ["MinArea"] = 0,
                    ["MaxArea"] = 200,
                    ["FeatureFilter"] = string.Empty
                });
            var flow = new OperatorFlowDto
            {
                Id = Guid.NewGuid(),
                Name = "BlobRoundTripFlow",
                Operators = [acquisition, blob],
                Connections =
                [
                    CreateConnection(acquisitionId, acquisitionOutput.Id, blobId, blobInput.Id)
                ]
            };

            var projectService = new ProjectService(repository, flowStorage, new OperatorFactory());
            var created = await projectService.CreateAsync(new CreateProjectRequest
            {
                Name = "Blob Round Trip Project",
                Flow = flow
            });
            var loaded = await projectService.GetByIdAsync(created.Id);

            loaded.Should().NotBeNull();
            var loadedBlob = loaded!.Flow!.Operators.Single(op => op.Id == blobId);
            loadedBlob.Parameters.Single(parameter => parameter.Name == "MaxArea").Value
                .Should().BeOfType<JsonElement>().Which.GetInt32().Should().Be(200);
            loadedBlob.Parameters.Single(parameter => parameter.Name == "FeatureFilter").Value
                .Should().BeOfType<JsonElement>().Which.GetString().Should().BeEmpty();

            var blobExecutor = new CountingOperatorExecutor(
                new BlobDetectionOperator(NullLogger<BlobDetectionOperator>.Instance));
            await using var host = await PreviewNodeTestHost.CreateWithRealFlowExecutionAsync(
                persistedProject!,
                new ProjectVariableSessionRegistry(),
                [
                    new ImageAcquisitionOperator(
                        NullLogger<ImageAcquisitionOperator>.Instance,
                        Substitute.For<ICameraManager>()),
                    blobExecutor
                ],
                projectRepository: repository,
                flowStorage: flowStorage);

            using var response = await host.Client.PostAsJsonAsync("/api/flows/preview-node", new PreviewNodeRequest
            {
                ProjectId = created.Id,
                TargetNodeId = blobId,
                DebugSessionId = Guid.NewGuid(),
                ClientRequestSequence = 1,
                FlowRevision = 1,
                ArtifactMode = "references",
                InputImageBase64 = Convert.ToBase64String(CreateTwoAreaBlobPreviewImageBytes())
            });
            var payload = await response.Content.ReadAsStringAsync();

            response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK, payload);
            using var document = JsonDocument.Parse(payload);
            document.RootElement.GetProperty("success").GetBoolean().Should().BeTrue(payload);
            document.RootElement.GetProperty("outputData").GetProperty("BlobCount").GetInt32().Should().Be(1);
            blobExecutor.LastExecutedOperator.Should().NotBeNull();
            ReadIntValue(blobExecutor.LastExecutedOperator!.Parameters.Single(parameter => parameter.Name == "MaxArea").GetValue())
                .Should().Be(200);
            blobExecutor.LastExecutedOperator.Parameters.Single(parameter => parameter.Name == "FeatureFilter").GetValue()
                .Should().NotBeNull();
            ReadStringValue(blobExecutor.LastExecutedOperator.Parameters.Single(parameter => parameter.Name == "FeatureFilter").GetValue())
                .Should().BeEmpty();
        }
        finally
        {
            if (Directory.Exists(flowRoot))
            {
                Directory.Delete(flowRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task PreviewNode_WithExplicitMissingFilePathAndRuntimeImage_ShouldRejectBeforeFileDispatch()
    {
        var project = new Project("preview-explicit-missing-file");
        var acquisitionId = Guid.NewGuid();
        var acquisitionOutputPort = CreatePort("Image", PortDataType.Image, PortDirection.Output);
        var missingPath = Path.Combine(Path.GetTempPath(), $"missing-preview-{Guid.NewGuid():N}.png");

        var acquisitionExecutor = new CountingOperatorExecutor(
            new ImageAcquisitionOperator(
                NullLogger<ImageAcquisitionOperator>.Instance,
                Substitute.For<ICameraManager>()));
        await using var host = await PreviewNodeTestHost.CreateWithRealFlowExecutionAsync(
            project,
            new ProjectVariableSessionRegistry(),
            [
                acquisitionExecutor
            ]);

        using var response = await host.Client.PostAsJsonAsync("/api/flows/preview-node", new PreviewNodeRequest
        {
            ProjectId = project.Id,
            TargetNodeId = acquisitionId,
            InputImageBase64 = Convert.ToBase64String(CreateBinaryPreviewImageBytes()),
            FlowData = CreateUpdateFlowRequest(
                CreateOperatorDto(
                    acquisitionId,
                    "ImageAcquisition",
                    OperatorType.ImageAcquisition,
                    outputPorts: [acquisitionOutputPort],
                    parameters: new Dictionary<string, object>
                    {
                        ["SourceType"] = "File",
                        ["FilePath"] = missingPath
                    }))
        });

        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.BadRequest, payload);
        payload.Should().Contain("ADMISSION_DRAFT_PREVIEW_SIDE_EFFECT_BLOCKED");
        acquisitionExecutor.ExecuteCount.Should().Be(0);
    }

    [Fact]
    public async Task PreviewNode_WithFileImageAcquisitionTargetWithoutRuntimeImage_ShouldKeepClearMissingInputFailure()
    {
        var project = new Project("preview-missing-runtime-image");
        var acquisitionId = Guid.NewGuid();
        var acquisitionOutputPort = CreatePort("Image", PortDataType.Image, PortDirection.Output);

        await using var host = await PreviewNodeTestHost.CreateWithRealFlowExecutionAsync(
            project,
            new ProjectVariableSessionRegistry(),
            [
                new ImageAcquisitionOperator(
                    NullLogger<ImageAcquisitionOperator>.Instance,
                    Substitute.For<ICameraManager>())
            ]);

        using var response = await host.Client.PostAsJsonAsync("/api/flows/preview-node", new PreviewNodeRequest
        {
            ProjectId = project.Id,
            TargetNodeId = acquisitionId,
            FlowData = CreateUpdateFlowRequest(
                CreateOperatorDto(
                    acquisitionId,
                    "ImageAcquisition",
                    OperatorType.ImageAcquisition,
                    outputPorts: [acquisitionOutputPort],
                    parameters: new Dictionary<string, object>
                    {
                        ["SourceType"] = "File",
                        ["FilePath"] = string.Empty
                    }))
        });

        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK, payload);
        using var document = JsonDocument.Parse(payload);
        document.RootElement.GetProperty("success").GetBoolean().Should().BeFalse(payload);
        document.RootElement.GetProperty("errorMessage").GetString()
            .Should().Contain("FilePath is required when SourceType is File and no runtime Image input was provided.");
    }

    [Fact]
    public async Task PreviewNode_ShouldReturnObservationEnvelopeForSuccessfulPreview()
    {
        var projectId = Guid.NewGuid();
        var targetNodeId = Guid.NewGuid();
        var debugSessionId = Guid.NewGuid();

        await using var host = await PreviewNodeTestHost.CreateAsync(flowExecution =>
        {
            flowExecution.ExecuteDebugWithSnapshotAsync(
                    Arg.Any<ExecutionSnapshot>(),
                    Arg.Any<DebugOptions>(),
                    Arg.Any<Dictionary<string, object>?>(),
                    Arg.Any<ClearVision.Product.Core.ProjectVariables.ProjectVariableExecutionContext?>(),
                    Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(new FlowDebugExecutionResult
                {
                    IsSuccess = true,
                    DebugSessionId = debugSessionId,
                    ExecutionTimeMs = 21,
                    IntermediateResults = new Dictionary<Guid, Dictionary<string, object>>
                    {
                        [targetNodeId] = new()
                        {
                            ["Score"] = 0.98d,
                            ["Seen"] = 3L
                        }
                    },
                    DebugOperatorResults =
                    [
                        new OperatorDebugResult
                        {
                            OperatorId = targetNodeId,
                            OperatorName = "Threshold",
                            IsSuccess = true,
                            ExecutionOrder = 0,
                            ExecutionTimeMs = 21
                        }
                    ]
                }));
        });

        using var response = await host.Client.PostAsJsonAsync("/api/flows/preview-node", new PreviewNodeRequest
        {
            ProjectId = projectId,
            TargetNodeId = targetNodeId,
            DebugSessionId = debugSessionId,
            ClientRequestSequence = 42,
            FlowRevision = 9,
            FlowData = CreateUpdateFlowRequest(
                CreateOperatorDto(targetNodeId, "Threshold", OperatorType.Thresholding))
        });

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        root.GetProperty("success").GetBoolean().Should().BeTrue();
        root.GetProperty("outputData").GetProperty("Score").GetDouble().Should().BeApproximately(0.98d, 0.001d);

        var observation = root.GetProperty("observation");
        observation.GetProperty("schemaVersion").GetString().Should().Be("execution-observation.v1");
        var identity = observation.GetProperty("identity");
        identity.GetProperty("projectId").GetGuid().Should().Be(projectId);
        identity.GetProperty("targetNodeId").GetGuid().Should().Be(targetNodeId);
        identity.GetProperty("debugSessionId").GetGuid().Should().Be(debugSessionId);
        identity.GetProperty("clientRequestSequence").GetInt64().Should().Be(42);
        identity.GetProperty("flowRevision").GetInt64().Should().Be(9);
        identity.GetProperty("runId").ValueKind.Should().Be(JsonValueKind.Null);

        var outcome = observation.GetProperty("outcome");
        outcome.GetProperty("success").GetBoolean().Should().BeTrue();
        outcome.GetProperty("executionTimeMs").GetInt64().Should().Be(21);
        outcome.GetProperty("executedOperatorCount").GetInt32().Should().Be(1);
        observation.GetProperty("summary").EnumerateArray()
            .Should().Contain(item => item.GetProperty("key").GetString() == "Score");
    }

    [Fact]
    public async Task PreviewNode_ShouldReturnObservationEnvelopeForExecutionFailure()
    {
        var projectId = Guid.NewGuid();
        var targetNodeId = Guid.NewGuid();
        var debugSessionId = Guid.NewGuid();

        await using var host = await PreviewNodeTestHost.CreateAsync(flowExecution =>
        {
            flowExecution.ExecuteDebugWithSnapshotAsync(
                    Arg.Any<ExecutionSnapshot>(),
                    Arg.Any<DebugOptions>(),
                    Arg.Any<Dictionary<string, object>?>(),
                    Arg.Any<ClearVision.Product.Core.ProjectVariables.ProjectVariableExecutionContext?>(),
                    Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(new FlowDebugExecutionResult
                {
                    IsSuccess = false,
                    DebugSessionId = debugSessionId,
                    ExecutionTimeMs = 17,
                    ErrorMessage = "Target failed",
                    DebugOperatorResults =
                    [
                        new OperatorDebugResult
                        {
                            OperatorId = targetNodeId,
                            OperatorName = "Threshold",
                            IsSuccess = false,
                            ExecutionOrder = 0,
                            ExecutionTimeMs = 17,
                            ErrorMessage = "Target failed",
                            OutputSnapshot = new Dictionary<string, object>
                            {
                                ["Score"] = 0.42d
                            }
                        }
                    ]
                }));
        });

        using var response = await host.Client.PostAsJsonAsync("/api/flows/preview-node", new PreviewNodeRequest
        {
            ProjectId = projectId,
            TargetNodeId = targetNodeId,
            DebugSessionId = debugSessionId,
            ClientRequestSequence = 43,
            FlowRevision = 10,
            FlowData = CreateUpdateFlowRequest(
                CreateOperatorDto(targetNodeId, "Threshold", OperatorType.Thresholding))
        });

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        root.GetProperty("success").GetBoolean().Should().BeFalse();
        root.GetProperty("outputData").GetProperty("Score").GetDouble().Should().BeApproximately(0.42d, 0.001d);

        var observation = root.GetProperty("observation");
        observation.GetProperty("identity").GetProperty("clientRequestSequence").GetInt64().Should().Be(43);
        observation.GetProperty("identity").GetProperty("flowRevision").GetInt64().Should().Be(10);
        var outcome = observation.GetProperty("outcome");
        outcome.GetProperty("success").GetBoolean().Should().BeFalse();
        outcome.GetProperty("errorMessage").GetString().Should().Contain("Target failed");
        outcome.GetProperty("failedOperatorId").GetGuid().Should().Be(targetNodeId);
        outcome.GetProperty("failedOperatorName").GetString().Should().Be("Threshold");
    }

    [Fact]
    public async Task PreviewNode_ShouldRejectUnsafeClientObservationIdentityValues()
    {
        await using var host = await PreviewNodeTestHost.CreateAsync(_ => { });

        using var response = await host.Client.PostAsJsonAsync("/api/flows/preview-node", new PreviewNodeRequest
        {
            ProjectId = Guid.NewGuid(),
            TargetNodeId = Guid.NewGuid(),
            ClientRequestSequence = -1,
            FlowRevision = 0
        });

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.BadRequest);
        var payload = await response.Content.ReadAsStringAsync();
        payload.Should().Contain("clientRequestSequence");
    }

    [Fact]
    public async Task PreviewNode_ShouldRejectEmptyProjectIdWithAdmissionMessage()
    {
        var targetNodeId = Guid.NewGuid();
        await using var host = await PreviewNodeTestHost.CreateAsync(_ => { });

        using var response = await host.Client.PostAsJsonAsync("/api/flows/preview-node", new PreviewNodeRequest
        {
            ProjectId = Guid.Empty,
            TargetNodeId = targetNodeId,
            FlowData = CreateUpdateFlowRequest(
                CreateOperatorDto(targetNodeId, "Threshold", OperatorType.Thresholding))
        });

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.BadRequest);
        var payload = await response.Content.ReadAsStringAsync();
        payload.Should().Contain("ADMISSION_PROJECT_REQUIRED");
        payload.Should().Contain("active projectId is required");
    }

    [Fact]
    public async Task PreviewNode_MissingDraftAuthority_ShouldFailClosedWhenTestEnrichmentIsDisabled()
    {
        var targetNodeId = Guid.NewGuid();
        await using var host = await PreviewNodeTestHost.CreateAsync(
            _ => { },
            enrichDraftAuthority: false);

        using var response = await host.Client.PostAsJsonAsync("/api/flows/preview-node", new PreviewNodeRequest
        {
            ProjectId = Guid.NewGuid(),
            TargetNodeId = targetNodeId,
            FlowData = CreateUpdateFlowRequest(
                CreateOperatorDto(targetNodeId, "Threshold", OperatorType.Thresholding))
        });

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("ADMISSION_DRAFT_REVISION_REQUIRED");
    }

    [Fact]
    public async Task PreviewNode_StaleDraftRevision_ShouldFailClosedWhenTestEnrichmentIsDisabled()
    {
        var project = new Project("stale-preview-authority");
        var targetNodeId = Guid.NewGuid();
        await using var host = await PreviewNodeTestHost.CreateAsync(
            _ => { },
            configureProjectRepository: repository =>
                repository.GetByIdFreshAsync(project.Id).Returns(project),
            enrichDraftAuthority: false);

        using var response = await host.Client.PostAsJsonAsync("/api/flows/preview-node", new PreviewNodeRequest
        {
            ProjectId = project.Id,
            TargetNodeId = targetNodeId,
            ExpectedProjectRevision = project.PersistenceRevision + 1,
            CapabilityManifest = new List<string>(),
            ConfirmationId = Guid.NewGuid().ToString("D"),
            AuditId = Guid.NewGuid().ToString("D"),
            FlowData = CreateUpdateFlowRequest(
                CreateOperatorDto(targetNodeId, "Threshold", OperatorType.Thresholding))
        });

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("ADMISSION_DRAFT_REVISION_REQUIRED");
    }

    [Fact]
    public async Task PreviewNode_ForgedCapabilityManifest_ShouldFailClosedWhenTestEnrichmentIsDisabled()
    {
        var project = new Project("forged-preview-authority");
        var targetNodeId = Guid.NewGuid();
        await using var host = await PreviewNodeTestHost.CreateAsync(
            _ => { },
            configureProjectRepository: repository =>
                repository.GetByIdFreshAsync(project.Id).Returns(project),
            enrichDraftAuthority: false);

        using var response = await host.Client.PostAsJsonAsync("/api/flows/preview-node", new PreviewNodeRequest
        {
            ProjectId = project.Id,
            TargetNodeId = targetNodeId,
            ExpectedProjectRevision = project.PersistenceRevision,
            CapabilityManifest = [ExecutionSideEffect.NetworkWrite.ToString()],
            ConfirmationId = Guid.NewGuid().ToString("D"),
            AuditId = Guid.NewGuid().ToString("D"),
            FlowData = CreateUpdateFlowRequest(
                CreateOperatorDto(targetNodeId, "Threshold", OperatorType.Thresholding))
        });

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("ADMISSION_CAPABILITY_MANIFEST_MISMATCH");
    }

    [Fact]
    public async Task PreviewNode_ShouldDowngradeSingleUnsafeValueWithoutEndpointFailure()
    {
        var targetNodeId = Guid.NewGuid();
        var circular = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        circular["Self"] = circular;

        using var mat = new Mat(1, 1, MatType.CV_8UC1, Scalar.All(255));
        await using var host = await PreviewNodeTestHost.CreateAsync(flowExecution =>
        {
            flowExecution.ExecuteDebugWithSnapshotAsync(
                    Arg.Any<ExecutionSnapshot>(),
                    Arg.Any<DebugOptions>(),
                    Arg.Any<Dictionary<string, object>?>(),
                    Arg.Any<ClearVision.Product.Core.ProjectVariables.ProjectVariableExecutionContext?>(),
                    Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(new FlowDebugExecutionResult
                {
                    IsSuccess = true,
                    ExecutionTimeMs = 5,
                    IntermediateResults = new Dictionary<Guid, Dictionary<string, object>>
                    {
                        [targetNodeId] = new()
                        {
                            ["Score"] = 0.95d,
                            ["Unsafe"] = new UnsafePreviewValue(),
                            ["Loop"] = circular,
                            ["Matrix"] = mat,
                            ["BadNumber"] = double.NaN
                        }
                    },
                    DebugOperatorResults =
                    [
                        new OperatorDebugResult
                        {
                            OperatorId = targetNodeId,
                            OperatorName = "Unsafe",
                            IsSuccess = true,
                            ExecutionOrder = 0
                        }
                    ]
                }));
        });

        using var response = await host.Client.PostAsJsonAsync("/api/flows/preview-node", new PreviewNodeRequest
        {
            ProjectId = Guid.NewGuid(),
            TargetNodeId = targetNodeId,
            FlowData = CreateUpdateFlowRequest(
                CreateOperatorDto(targetNodeId, "Unsafe", OperatorType.Thresholding))
        });

        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK, payload);
        using var document = JsonDocument.Parse(payload);
        var outputData = document.RootElement.GetProperty("outputData");
        outputData.GetProperty("Score").GetDouble().Should().BeApproximately(0.95d, 0.001d);
        outputData.GetProperty("Unsafe").GetProperty("kind").GetString().Should().Be("object");
        outputData.GetProperty("Loop").GetProperty("Self").GetProperty("kind").GetString().Should().Be("circular");
        outputData.GetProperty("Matrix").GetProperty("kind").GetString().Should().Be("matrix");
        outputData.GetProperty("BadNumber").GetProperty("kind").GetString().Should().Be("nonFiniteNumber");
        document.RootElement.GetProperty("observation").GetProperty("diagnostics").EnumerateArray()
            .Should().NotContain(item => item.GetProperty("code").GetString() == "getter-error");
    }

    [Fact]
    public async Task PreviewNode_ShouldFailSoftForAdversarialObservationValues()
    {
        var targetNodeId = Guid.NewGuid();
        var nodeOutput = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
        {
            ["Score"] = 0.95d,
            ["Seen"] = 7L,
            ["ThrowingToString"] = new ThrowingToStringPreviewValue(),
            ["ThrowingGetter"] = new UnsafePreviewValue(),
            ["ThrowingEnumerable"] = new ThrowingEnumerable(),
            ["Objects"] = new CountingInfiniteEnumerable(),
            [new string('K', 3_000)] = new string('x', 20_000),
            ["Nan"] = double.NaN,
            ["Infinity"] = float.PositiveInfinity,
            ["Bytes"] = new byte[] { 1, 2, 3, 4 }
        };
        using var mat = new Mat(1, 1, MatType.CV_8UC1, Scalar.All(255));
        using var wrapper = new ImageWrapper(new Mat(1, 1, MatType.CV_8UC1, Scalar.All(128)));
        nodeOutput["Matrix"] = mat;
        nodeOutput["Wrapper"] = wrapper;
        for (var index = 0; index < 10_000; index++)
        {
            nodeOutput[$"ZField{index:D05}"] = new string('z', 8_000);
        }

        await using var host = await PreviewNodeTestHost.CreateAsync(flowExecution =>
        {
            flowExecution.ExecuteDebugWithSnapshotAsync(
                    Arg.Any<ExecutionSnapshot>(),
                    Arg.Any<DebugOptions>(),
                    Arg.Any<Dictionary<string, object>?>(),
                    Arg.Any<ClearVision.Product.Core.ProjectVariables.ProjectVariableExecutionContext?>(),
                    Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(new FlowDebugExecutionResult
                {
                    IsSuccess = true,
                    ExecutionTimeMs = 5,
                    IntermediateResults = new Dictionary<Guid, Dictionary<string, object>>
                    {
                        [targetNodeId] = nodeOutput
                    },
                    DebugOperatorResults =
                    [
                        new OperatorDebugResult
                        {
                            OperatorId = targetNodeId,
                            OperatorName = "Adversarial",
                            IsSuccess = true,
                            ExecutionOrder = 0
                        }
                    ]
                }));
        });

        var stopwatch = Stopwatch.StartNew();
        using var response = await host.Client.PostAsJsonAsync("/api/flows/preview-node", new PreviewNodeRequest
        {
            ProjectId = Guid.NewGuid(),
            TargetNodeId = targetNodeId,
            FlowData = CreateUpdateFlowRequest(
                CreateOperatorDto(targetNodeId, "Adversarial", OperatorType.Thresholding))
        });
        stopwatch.Stop();

        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK, payload);
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5));
        ((CountingInfiniteEnumerable)nodeOutput["Objects"]).MoveNextCount.Should().Be(0);
        ((ThrowingEnumerable)nodeOutput["ThrowingEnumerable"]).GetEnumeratorCallCount.Should().Be(0);

        using var document = JsonDocument.Parse(payload);
        var outputData = document.RootElement.GetProperty("outputData");
        outputData.GetProperty("Score").GetDouble().Should().BeApproximately(0.95d, 0.001d);
        outputData.GetProperty("Seen").GetInt64().Should().Be(7L);
        outputData.GetProperty("ThrowingToString").GetProperty("kind").GetString().Should().Be("object");
        outputData.GetProperty("ThrowingGetter").GetProperty("kind").GetString().Should().Be("object");
        outputData.GetProperty("ThrowingEnumerable").GetProperty("kind").GetString().Should().Be("unsupportedEnumerable");
        outputData.GetProperty("Objects").GetProperty("kind").GetString().Should().Be("unsupportedEnumerable");
        outputData.GetProperty("Matrix").GetProperty("kind").GetString().Should().Be("matrix");
        outputData.GetProperty("Wrapper").GetProperty("kind").GetString().Should().Be("image");
        outputData.GetProperty("Bytes").GetProperty("kind").GetString().Should().Be("binary");
        outputData.GetProperty("Nan").GetProperty("kind").GetString().Should().Be("nonFiniteNumber");
        outputData.GetProperty("Infinity").GetProperty("kind").GetString().Should().Be("nonFiniteNumber");

        document.RootElement.GetProperty("metrics").GetProperty("diagnostics").EnumerateArray()
            .Select(item => item.GetString())
            .Should().Contain("PreviewMetricsUnsupportedEnumerable");
        var observation = document.RootElement.GetProperty("observation");
        Encoding.UTF8.GetByteCount(observation.GetProperty("detail").GetRawText())
            .Should().BeLessThanOrEqualTo(ExecutionObservationProjector.MaxDetailBytes);
        observation.GetProperty("diagnostics").EnumerateArray()
            .Select(item => item.GetProperty("code").GetString())
            .Should().NotContain("getter-error");
    }

    [Fact]
    public async Task PreviewNode_ShouldBoundLargeObservationResponseJson()
    {
        var targetNodeId = Guid.NewGuid();
        var rows = Enumerable.Range(0, 64)
            .Select(row => Enumerable.Range(0, 64).ToDictionary(
                col => $"C{col:D2}",
                _ => (object)new string('x', 8_000),
                StringComparer.OrdinalIgnoreCase))
            .Cast<object>()
            .ToList();

        await using var host = await PreviewNodeTestHost.CreateAsync(flowExecution =>
        {
            flowExecution.ExecuteDebugWithSnapshotAsync(
                    Arg.Any<ExecutionSnapshot>(),
                    Arg.Any<DebugOptions>(),
                    Arg.Any<Dictionary<string, object>?>(),
                    Arg.Any<ClearVision.Product.Core.ProjectVariables.ProjectVariableExecutionContext?>(),
                    Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(new FlowDebugExecutionResult
                {
                    IsSuccess = true,
                    ExecutionTimeMs = 5,
                    IntermediateResults = new Dictionary<Guid, Dictionary<string, object>>
                    {
                        [targetNodeId] = new()
                        {
                            ["Rows"] = rows
                        }
                    },
                    DebugOperatorResults =
                    [
                        new OperatorDebugResult
                        {
                            OperatorId = targetNodeId,
                            OperatorName = "Large",
                            IsSuccess = true,
                            ExecutionOrder = 0
                        }
                    ]
                }));
        });

        using var response = await host.Client.PostAsJsonAsync("/api/flows/preview-node", new PreviewNodeRequest
        {
            ProjectId = Guid.NewGuid(),
            TargetNodeId = targetNodeId,
            FlowData = CreateUpdateFlowRequest(
                CreateOperatorDto(targetNodeId, "Large", OperatorType.Thresholding))
        });

        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK, payload);
        payload.Length.Should().BeLessThan(900_000);
        using var document = JsonDocument.Parse(payload);
        document.RootElement.GetProperty("observation").GetProperty("truncated").GetBoolean().Should().BeTrue();
        document.RootElement.GetProperty("observation").GetProperty("diagnostics").EnumerateArray()
            .Should().Contain(item =>
                item.GetProperty("code").GetString() == "byte-budget" ||
                item.GetProperty("code").GetString() == "node-limit");
    }

    [Fact]
    public async Task PreviewNode_WithProjectVariables_ShouldPropagateSourceToTargetInPreviewClone()
    {
        var variableId = Guid.NewGuid();
        var unrelatedVariableId = Guid.NewGuid();
        var sourcePortId = Guid.NewGuid();
        var unrelatedPortId = Guid.NewGuid();
        var targetInputPortId = Guid.NewGuid();
        var targetParameterId = Guid.NewGuid();
        var source = CreateOperatorDto(
            Guid.NewGuid(),
            "Source",
            OperatorType.Thresholding,
            outputPorts:
            [
                new PortDto
                {
                    Id = sourcePortId,
                    Name = "Count",
                    DataType = PortDataType.Integer,
                    Direction = PortDirection.Output
                }
            ]);
        var unrelated = CreateOperatorDto(
            Guid.NewGuid(),
            "Unrelated",
            OperatorType.Thresholding,
            outputPorts:
            [
                new PortDto
                {
                    Id = unrelatedPortId,
                    Name = "Count",
                    DataType = PortDataType.Integer,
                    Direction = PortDirection.Output
                }
            ]);
        var target = CreateOperatorDto(
            Guid.NewGuid(),
            "Target",
            OperatorType.ResultJudgment,
            inputPorts:
            [
                new PortDto
                {
                    Id = targetInputPortId,
                    Name = "Image",
                    DataType = PortDataType.Image,
                    Direction = PortDirection.Input
                }
            ],
            parameters: new Dictionary<string, object> { ["ExpectedCount"] = 0 });
        target.Parameters.Single().Id = targetParameterId;

        var schema = CreatePreviewVariableSchema(variableId, 0L);
        schema.Variables.Add(new ProjectGlobalVariableDefinition
        {
            Id = unrelatedVariableId,
            Name = "stats.unrelated",
            DisplayName = "Unrelated",
            ValueType = ProjectGlobalVariableValueType.Int64,
            InitialValue = JsonSerializer.SerializeToElement(0L),
            ManualWriteAllowed = true
        });
        schema.SourceBindings.Add(new ProjectGlobalVariableSourceBinding
        {
            Id = Guid.NewGuid(),
            VariableId = variableId,
            OperatorId = source.Id,
            OutputPortId = sourcePortId,
            OperatorName = source.Name,
            OutputPortName = "Count"
        });
        schema.SourceBindings.Add(new ProjectGlobalVariableSourceBinding
        {
            Id = Guid.NewGuid(),
            VariableId = unrelatedVariableId,
            OperatorId = unrelated.Id,
            OutputPortId = unrelatedPortId,
            OperatorName = unrelated.Name,
            OutputPortName = "Count"
        });
        schema.TargetBindings.Add(new ProjectGlobalVariableTargetBinding
        {
            Id = Guid.NewGuid(),
            VariableId = variableId,
            OperatorId = target.Id,
            ParameterId = targetParameterId,
            OperatorName = target.Name,
            ParameterName = "ExpectedCount"
        });
        var project = new Project("preview-project");
        project.UpdateGlobalVariables(schema);
        var registry = new ProjectVariableSessionRegistry();
        var formalSession = registry.GetOrCreate(project.Id, schema);
        formalSession.SetValue(variableId, 2L, ProjectVariableUpdatedBy.StudioManual);

        var sourceExecutor = Substitute.For<IOperatorExecutor>();
        sourceExecutor.OperatorType.Returns(OperatorType.Thresholding);
        sourceExecutor.ExecuteAsync(Arg.Any<Operator>(), Arg.Any<Dictionary<string, object>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(OperatorExecutionOutput.Success(new Dictionary<string, object>
            {
                ["Count"] = 7L
            })));
        sourceExecutor.ValidateParameters(Arg.Any<Operator>()).Returns(ValidationResult.Valid());

        var targetExecutor = Substitute.For<IOperatorExecutor>();
        targetExecutor.OperatorType.Returns(OperatorType.ResultJudgment);
        targetExecutor.ExecuteAsync(
                Arg.Any<Operator>(),
                Arg.Is<Dictionary<string, object>>(inputs => Convert.ToInt64(inputs["ExpectedCount"]) == 7L),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(OperatorExecutionOutput.Success(new Dictionary<string, object>
            {
                ["Seen"] = 7L
            })));
        targetExecutor.ValidateParameters(Arg.Any<Operator>()).Returns(ValidationResult.Valid());

        await using var host = await PreviewNodeTestHost.CreateWithRealFlowExecutionAsync(
            project,
            registry,
            [sourceExecutor, targetExecutor]);

        using var response = await host.Client.PostAsJsonAsync("/api/flows/preview-node", new PreviewNodeRequest
        {
            ProjectId = project.Id,
            TargetNodeId = target.Id,
            FlowData = CreateUpdateFlowRequest(
                source,
                unrelated,
                target,
                CreateConnection(source.Id, sourcePortId, target.Id, targetInputPortId))
        });

        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK, payload);
        using var document = JsonDocument.Parse(payload);
        document.RootElement.GetProperty("success").GetBoolean().Should().BeTrue(payload);
        document.RootElement.GetProperty("outputData").GetProperty("Seen").GetInt64().Should().Be(7L);
        formalSession.TryGetSnapshot(variableId, out var formal).Should().BeTrue();
        ProjectVariableValueConverter.ToObject(formal.Value).Should().Be(2L);
        formal.Version.Should().Be(1);
        formal.UpdatedBy.Should().Be(ProjectVariableUpdatedBy.StudioManual);
    }

    [Fact]
    public async Task PreviewNode_WithVariableIncrement_ShouldUseCurrentFormalSnapshotAndNotCommit()
    {
        var variableId = Guid.NewGuid();
        var increment = CreateOperatorDto(
            Guid.NewGuid(),
            "Increment",
            OperatorType.VariableIncrement,
            parameters: new Dictionary<string, object>
            {
                ["Scope"] = "Project",
                ["VariableId"] = variableId.ToString(),
                ["VariableName"] = "stats.count",
                ["Delta"] = 5
            });

        var schema = CreatePreviewVariableSchema(variableId, 1L);
        var project = new Project("preview-increment");
        project.UpdateGlobalVariables(schema);
        var registry = new ProjectVariableSessionRegistry();
        var formalSession = registry.GetOrCreate(project.Id, schema);
        formalSession.SetValue(variableId, 4L, ProjectVariableUpdatedBy.StudioManual);
        var accessor = new ProjectVariableExecutionContextAccessor();

        await using var host = await PreviewNodeTestHost.CreateWithRealFlowExecutionAsync(
            project,
            registry,
            [
                new VariableIncrementOperator(
                    NullLogger<VariableIncrementOperator>.Instance,
                    new VariableContext(),
                    accessor)
            ],
            accessor);

        using var response = await host.Client.PostAsJsonAsync("/api/flows/preview-node", new PreviewNodeRequest
        {
            ProjectId = project.Id,
            TargetNodeId = increment.Id,
            FlowData = CreateUpdateFlowRequest(increment)
        });

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        document.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
        document.RootElement.GetProperty("outputData").GetProperty("NewValue").GetInt64().Should().Be(9L);
        formalSession.TryGetSnapshot(variableId, out var formal).Should().BeTrue();
        ProjectVariableValueConverter.ToObject(formal.Value).Should().Be(4L);
        formal.Version.Should().Be(1);
        formal.UpdatedBy.Should().Be(ProjectVariableUpdatedBy.StudioManual);
    }

    [Fact]
    public async Task PreviewNode_WithVariableReadAndSameDebugSession_ShouldReturnFreshProjectVariableValue()
    {
        var variableId = Guid.NewGuid();
        var debugSessionId = Guid.NewGuid();
        var read = CreateOperatorDto(
            Guid.NewGuid(),
            "Read",
            OperatorType.VariableRead,
            parameters: new Dictionary<string, object>
            {
                ["Scope"] = "Project",
                ["VariableId"] = variableId.ToString(),
                ["VariableName"] = "stats.count",
                ["DefaultValue"] = "0",
                ["DataType"] = "Int"
            });

        var schema = CreatePreviewVariableSchema(variableId, 1L);
        var project = new Project("preview-read-cache");
        project.UpdateGlobalVariables(schema);
        var registry = new ProjectVariableSessionRegistry();
        var formalSession = registry.GetOrCreate(project.Id, schema);
        formalSession.SetValue(variableId, 4L, ProjectVariableUpdatedBy.StudioManual);
        var accessor = new ProjectVariableExecutionContextAccessor();
        var readExecutor = new CountingOperatorExecutor(new VariableReadOperator(
            NullLogger<VariableReadOperator>.Instance,
            new VariableContext(),
            accessor));

        await using var host = await PreviewNodeTestHost.CreateWithRealFlowExecutionAsync(
            project,
            registry,
            [readExecutor],
            accessor);

        var first = await PostPreviewReadAsync(host, project.Id, read, debugSessionId);
        formalSession.SetValue(variableId, 10L, ProjectVariableUpdatedBy.StudioManual);
        var second = await PostPreviewReadAsync(host, project.Id, read, debugSessionId);

        first.GetProperty("success").GetBoolean().Should().BeTrue(first.ToString());
        second.GetProperty("success").GetBoolean().Should().BeTrue(second.ToString());
        first.GetProperty("outputData").GetProperty("Value").GetInt64().Should().Be(4L);
        second.GetProperty("outputData").GetProperty("Value").GetInt64().Should().Be(10L);
        readExecutor.ExecuteCount.Should().Be(2, "Project-scope VariableRead must not reuse PreviewNode debug cache");
        formalSession.TryGetSnapshot(variableId, out var formal).Should().BeTrue();
        ProjectVariableValueConverter.ToObject(formal.Value).Should().Be(10L);
        formal.UpdatedBy.Should().Be(ProjectVariableUpdatedBy.StudioManual);
    }

    [Fact]
    public async Task PreviewNode_WhenProjectHasNoGlobalVariables_ShouldUseExistingExecutionPath()
    {
        var project = new Project("no-global-variables");
        var targetNodeId = Guid.NewGuid();

        await using var host = await PreviewNodeTestHost.CreateAsync(
            flowExecution =>
            {
                flowExecution.ExecuteDebugWithSnapshotAsync(
                        Arg.Any<ExecutionSnapshot>(),
                        Arg.Any<DebugOptions>(),
                        Arg.Any<Dictionary<string, object>?>(),
                        Arg.Any<ClearVision.Product.Core.ProjectVariables.ProjectVariableExecutionContext?>(),
                        Arg.Any<CancellationToken>())
                    .Returns(Task.FromResult(new FlowDebugExecutionResult
                    {
                        IsSuccess = true,
                        IntermediateResults = new Dictionary<Guid, Dictionary<string, object>>
                        {
                            [targetNodeId] = new() { ["Value"] = 3L }
                        }
                    }));
            },
            projectRepository => projectRepository.GetByIdFreshAsync(project.Id).Returns(project));

        using var response = await host.Client.PostAsJsonAsync("/api/flows/preview-node", new PreviewNodeRequest
        {
            ProjectId = project.Id,
            TargetNodeId = targetNodeId,
            FlowData = CreateUpdateFlowRequest(CreateOperatorDto(targetNodeId, "Target", OperatorType.ResultJudgment))
        });

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task PreviewNode_WithDeletedProjectAndInlineFlow_ShouldRejectBeforeExecution()
    {
        var projectId = Guid.NewGuid();
        var targetNodeId = Guid.NewGuid();
        IFlowExecutionService? flowExecution = null;

        await using var host = await PreviewNodeTestHost.CreateAsync(
            configuredFlowExecution =>
            {
                flowExecution = configuredFlowExecution;
                configuredFlowExecution.ExecuteDebugWithSnapshotAsync(
                        Arg.Any<ExecutionSnapshot>(),
                        Arg.Any<DebugOptions>(),
                        Arg.Any<Dictionary<string, object>?>(),
                        Arg.Any<ClearVision.Product.Core.ProjectVariables.ProjectVariableExecutionContext?>(),
                        Arg.Any<CancellationToken>())
                    .Returns(Task.FromResult(new FlowDebugExecutionResult { IsSuccess = true }));
            },
            projectRepository => projectRepository.GetByIdFreshAsync(projectId).Returns((Project?)null));

        using var response = await host.Client.PostAsJsonAsync("/api/flows/preview-node", new PreviewNodeRequest
        {
            ProjectId = projectId,
            TargetNodeId = targetNodeId,
            FlowData = CreateUpdateFlowRequest(CreateOperatorDto(targetNodeId, "Target", OperatorType.ResultJudgment))
        });

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.BadRequest);
        var payload = await response.Content.ReadAsStringAsync();
        payload.Should().Contain("ADMISSION_PROJECT_NOT_ACTIVE");
        await flowExecution!.DidNotReceiveWithAnyArgs().ExecuteDebugWithSnapshotAsync(
            Arg.Any<ExecutionSnapshot>(),
            Arg.Any<DebugOptions>(),
            Arg.Any<Dictionary<string, object>?>(),
            Arg.Any<ClearVision.Product.Core.ProjectVariables.ProjectVariableExecutionContext?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PreviewNode_WithInlineSideEffectOperator_ShouldRejectBeforeDebugExecution()
    {
        var projectId = Guid.NewGuid();
        var targetNodeId = Guid.NewGuid();
        IFlowExecutionService? flowExecution = null;

        await using var host = await PreviewNodeTestHost.CreateAsync(configuredFlowExecution =>
        {
            flowExecution = configuredFlowExecution;
            configuredFlowExecution.ExecuteDebugWithSnapshotAsync(
                    Arg.Any<ExecutionSnapshot>(),
                    Arg.Any<DebugOptions>(),
                    Arg.Any<Dictionary<string, object>?>(),
                    Arg.Any<ClearVision.Product.Core.ProjectVariables.ProjectVariableExecutionContext?>(),
                    Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(new FlowDebugExecutionResult { IsSuccess = true }));
        });

        using var response = await host.Client.PostAsJsonAsync("/api/flows/preview-node", new PreviewNodeRequest
        {
            ProjectId = projectId,
            TargetNodeId = targetNodeId,
            FlowData = CreateUpdateFlowRequest(CreateOperatorDto(targetNodeId, "HttpRequest", OperatorType.HttpRequest))
        });

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.BadRequest);
        var payload = await response.Content.ReadAsStringAsync();
        payload.Should().Contain("ADMISSION_DRAFT_PREVIEW_SIDE_EFFECT_BLOCKED");
        await flowExecution!.DidNotReceiveWithAnyArgs().ExecuteDebugWithSnapshotAsync(
            Arg.Any<ExecutionSnapshot>(),
            Arg.Any<DebugOptions>(),
            Arg.Any<Dictionary<string, object>?>(),
            Arg.Any<ClearVision.Product.Core.ProjectVariables.ProjectVariableExecutionContext?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PreviewNode_WithImageSaveTarget_ShouldDryRunWithoutWritingFile()
    {
        var projectId = Guid.NewGuid();
        var sourceNodeId = Guid.NewGuid();
        var targetNodeId = Guid.NewGuid();
        var debugSessionId = Guid.NewGuid();
        var outputDir = Path.Combine(Path.GetTempPath(), $"ClearVision-ImageSavePreview-{Guid.NewGuid():N}");
        var sourceOutputPort = CreatePort("Image", PortDataType.Image, PortDirection.Output);
        var imageSaveInputPort = CreatePort("Image", PortDataType.Image, PortDirection.Input, isRequired: true);
        var imageBytes = CreateBinaryPreviewImageBytes();
        DebugOptions? capturedOptions = null;
        OperatorFlow? capturedFlow = null;

        try
        {
            await using var host = await PreviewNodeTestHost.CreateAsync(flowExecution =>
            {
                flowExecution.ExecuteDebugWithSnapshotAsync(
                        Arg.Any<ExecutionSnapshot>(),
                        Arg.Any<DebugOptions>(),
                        Arg.Any<Dictionary<string, object>?>(),
                        Arg.Any<ClearVision.Product.Core.ProjectVariables.ProjectVariableExecutionContext?>(),
                        Arg.Any<CancellationToken>())
                    .Returns(callInfo =>
                    {
                        capturedFlow = callInfo.ArgAt<ExecutionSnapshot>(0).CreateExecutionFlow();
                        capturedOptions = callInfo.ArgAt<DebugOptions>(1);

                        return Task.FromResult(new FlowDebugExecutionResult
                        {
                            IsSuccess = true,
                            DebugSessionId = debugSessionId,
                            ExecutionTimeMs = 9,
                            IntermediateResults = new Dictionary<Guid, Dictionary<string, object>>
                            {
                                [sourceNodeId] = new()
                                {
                                    ["Image"] = imageBytes
                                }
                            },
                            DebugOperatorResults = new List<OperatorDebugResult>
                            {
                                new()
                                {
                                    OperatorId = sourceNodeId,
                                    OperatorName = "EdgePreparation",
                                    IsSuccess = true,
                                    ExecutionOrder = 0,
                                    ExecutionTimeMs = 9,
                                    OutputSnapshot = new Dictionary<string, object>
                                    {
                                        ["Image"] = imageBytes
                                    }
                                }
                            }
                        });
                    });
            });

            Directory.Exists(outputDir).Should().BeFalse();

            using var response = await host.Client.PostAsJsonAsync("/api/flows/preview-node", new PreviewNodeRequest
            {
                ProjectId = projectId,
                TargetNodeId = targetNodeId,
                DebugSessionId = debugSessionId,
                FlowData = CreateUpdateFlowRequest(
                    CreateOperatorDto(
                        sourceNodeId,
                        "EdgePreparation",
                        OperatorType.Thresholding,
                        outputPorts: [sourceOutputPort]),
                    CreateOperatorDto(
                        targetNodeId,
                        "ImageSave",
                        OperatorType.ImageSave,
                        inputPorts: [imageSaveInputPort],
                        parameters: new Dictionary<string, object>
                        {
                            ["Directory"] = outputDir,
                            ["FileNameTemplate"] = "edge_{timestamp}_{Guid}.jpg",
                            ["Quality"] = 88
                        }),
                    CreateConnection(sourceNodeId, sourceOutputPort.Id, targetNodeId, imageSaveInputPort.Id))
            });

            var payload = await response.Content.ReadAsStringAsync();
            response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK, payload);
            using var document = JsonDocument.Parse(payload);
            document.RootElement.GetProperty("success").GetBoolean().Should().BeTrue(payload);
            document.RootElement.GetProperty("inputImageBase64").GetString().Should().Be(Convert.ToBase64String(imageBytes));
            document.RootElement.GetProperty("outputImageBase64").GetString().Should().Be(Convert.ToBase64String(imageBytes));

            var outputData = document.RootElement.GetProperty("outputData");
            outputData.GetProperty("PreviewMode").GetString().Should().Be("ImageSaveDryRun");
            outputData.GetProperty("PreviewBlocked").GetBoolean().Should().BeFalse();
            outputData.GetProperty("WillWriteToDisk").GetBoolean().Should().BeFalse();
            outputData.GetProperty("Message").GetString().Should().Be("预览模式不会写入磁盘；点击运行流程后才会保存图像。");
            outputData.GetProperty("Directory").GetString().Should().Be(outputDir);
            outputData.GetProperty("FileNameTemplate").GetString().Should().Be("edge_{timestamp}_{Guid}.jpg");
            outputData.GetProperty("Format").GetString().Should().Be("jpg");
            outputData.GetProperty("Quality").GetInt32().Should().Be(88);
            outputData.GetProperty("EstimatedFileName").GetString().Should().EndWith(".jpg");

            Directory.Exists(outputDir).Should().BeFalse("节点预览不能创建 ImageSave 目录或写盘");
            capturedOptions.Should().NotBeNull();
            capturedOptions!.BreakAtOperatorId.Should().Be(sourceNodeId);
            capturedFlow.Should().NotBeNull();
            capturedFlow!.Operators.Select(op => op.Type).Should().NotContain(OperatorType.ImageSave);
        }
        finally
        {
            if (Directory.Exists(outputDir))
            {
                Directory.Delete(outputDir, recursive: true);
            }
        }
    }

    [Fact]
    public async Task PreviewNode_WithImageSaveTargetAndNoInputImage_ShouldReturnMissingInputPreviewMessage()
    {
        var projectId = Guid.NewGuid();
        var targetNodeId = Guid.NewGuid();
        var outputDir = Path.Combine(Path.GetTempPath(), $"ClearVision-ImageSavePreviewMissing-{Guid.NewGuid():N}");
        var imageSaveInputPort = CreatePort("Image", PortDataType.Image, PortDirection.Input, isRequired: true);
        IFlowExecutionService? flowExecution = null;

        try
        {
            await using var host = await PreviewNodeTestHost.CreateAsync(configuredFlowExecution =>
            {
                flowExecution = configuredFlowExecution;
            });

            using var response = await host.Client.PostAsJsonAsync("/api/flows/preview-node", new PreviewNodeRequest
            {
                ProjectId = projectId,
                TargetNodeId = targetNodeId,
                FlowData = CreateUpdateFlowRequest(
                    CreateOperatorDto(
                        targetNodeId,
                        "ImageSave",
                        OperatorType.ImageSave,
                        inputPorts: [imageSaveInputPort],
                        parameters: new Dictionary<string, object>
                        {
                            ["Directory"] = outputDir,
                            ["FileNameTemplate"] = "missing_{timestamp}.png",
                            ["Quality"] = 90
                        }))
            });

            var payload = await response.Content.ReadAsStringAsync();
            response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK, payload);
            using var document = JsonDocument.Parse(payload);
            document.RootElement.GetProperty("success").GetBoolean().Should().BeTrue(payload);
            document.RootElement.GetProperty("errorMessage").ValueKind.Should().Be(JsonValueKind.Null);
            document.RootElement.GetProperty("inputImageBase64").ValueKind.Should().Be(JsonValueKind.Null);
            document.RootElement.GetProperty("outputImageBase64").ValueKind.Should().Be(JsonValueKind.Null);

            var outputData = document.RootElement.GetProperty("outputData");
            outputData.GetProperty("Message").GetString().Should().Be("缺少输入图像，无法生成保存预览");
            outputData.GetProperty("_previewWarning").GetString().Should().Be("缺少输入图像，无法生成保存预览");
            outputData.GetProperty("PreviewBlocked").GetBoolean().Should().BeFalse();
            payload.Should().NotContain("ADMISSION_NODE_PREVIEW_SIDE_EFFECT_BLOCKED");
            Directory.Exists(outputDir).Should().BeFalse("缺输入图像的 ImageSave 预览也不能创建目录");

            await flowExecution!.DidNotReceiveWithAnyArgs().ExecuteDebugWithSnapshotAsync(
                Arg.Any<ExecutionSnapshot>(),
                Arg.Any<DebugOptions>(),
                Arg.Any<Dictionary<string, object>?>(),
                Arg.Any<ClearVision.Product.Core.ProjectVariables.ProjectVariableExecutionContext?>(),
                Arg.Any<CancellationToken>());
        }
        finally
        {
            if (Directory.Exists(outputDir))
            {
                Directory.Delete(outputDir, recursive: true);
            }
        }
    }

    [Fact]
    public async Task PreviewNode_WithImageSaveTargetAndUpstreamFailure_ShouldReturnUpstreamMissingImagePreviewMessage()
    {
        var projectId = Guid.NewGuid();
        var sourceNodeId = Guid.NewGuid();
        var targetNodeId = Guid.NewGuid();
        var debugSessionId = Guid.NewGuid();
        var outputDir = Path.Combine(Path.GetTempPath(), $"ClearVision-ImageSavePreviewUpstreamFail-{Guid.NewGuid():N}");
        var sourceOutputPort = CreatePort("Image", PortDataType.Image, PortDirection.Output);
        var imageSaveInputPort = CreatePort("Image", PortDataType.Image, PortDirection.Input, isRequired: true);

        try
        {
            await using var host = await PreviewNodeTestHost.CreateAsync(flowExecution =>
            {
                flowExecution.ExecuteDebugWithSnapshotAsync(
                        Arg.Any<ExecutionSnapshot>(),
                        Arg.Any<DebugOptions>(),
                        Arg.Any<Dictionary<string, object>?>(),
                        Arg.Any<ClearVision.Product.Core.ProjectVariables.ProjectVariableExecutionContext?>(),
                        Arg.Any<CancellationToken>())
                    .Returns(callInfo => Task.FromResult(new FlowDebugExecutionResult
                    {
                        IsSuccess = false,
                        ErrorMessage = "上游阈值算子执行失败",
                        DebugSessionId = debugSessionId,
                        ExecutionTimeMs = 4,
                        IntermediateResults = new Dictionary<Guid, Dictionary<string, object>>(),
                        DebugOperatorResults = new List<OperatorDebugResult>
                        {
                            new()
                            {
                                OperatorId = sourceNodeId,
                                OperatorName = "EdgePreparation",
                                IsSuccess = false,
                                ExecutionOrder = 0,
                                ExecutionTimeMs = 4
                            }
                        }
                    }));
            });

            Directory.Exists(outputDir).Should().BeFalse();

            using var response = await host.Client.PostAsJsonAsync("/api/flows/preview-node", new PreviewNodeRequest
            {
                ProjectId = projectId,
                TargetNodeId = targetNodeId,
                DebugSessionId = debugSessionId,
                FlowData = CreateUpdateFlowRequest(
                    CreateOperatorDto(
                        sourceNodeId,
                        "EdgePreparation",
                        OperatorType.Thresholding,
                        outputPorts: [sourceOutputPort]),
                    CreateOperatorDto(
                        targetNodeId,
                        "ImageSave",
                        OperatorType.ImageSave,
                        inputPorts: [imageSaveInputPort],
                        parameters: new Dictionary<string, object>
                        {
                            ["Directory"] = outputDir,
                            ["FileNameTemplate"] = "edge_{timestamp}.png",
                            ["Quality"] = 90
                        }),
                    CreateConnection(sourceNodeId, sourceOutputPort.Id, targetNodeId, imageSaveInputPort.Id))
            });

            var payload = await response.Content.ReadAsStringAsync();
            response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK, payload);
            using var document = JsonDocument.Parse(payload);
            document.RootElement.GetProperty("success").GetBoolean().Should().BeTrue(payload);

            var outputData = document.RootElement.GetProperty("outputData");
            outputData.GetProperty("Message").GetString().Should().Be("预览模式未写入磁盘；但上游未产出图像，请先检查上游节点。");
            outputData.GetProperty("_previewWarning").GetString().Should().Be("预览模式未写入磁盘；但上游未产出图像，请先检查上游节点。");
            outputData.GetProperty("UpstreamPreviewMessage").GetString().Should().Be("上游阈值算子执行失败");
            outputData.GetProperty("PreviewMode").GetString().Should().Be("ImageSaveDryRun");
            outputData.GetProperty("PreviewSafe").GetBoolean().Should().BeTrue();
            outputData.GetProperty("WillWriteToDisk").GetBoolean().Should().BeFalse();

            Directory.Exists(outputDir).Should().BeFalse("上游失败的 ImageSave 预览不能创建目录或写盘");
        }
        finally
        {
            if (Directory.Exists(outputDir))
            {
                Directory.Delete(outputDir, recursive: true);
            }
        }
    }

    [Fact]
    public async Task PreviewNode_WithTextSaveTarget_ShouldDryRunWithoutWritingFile()
    {
        var projectId = Guid.NewGuid();
        var sourceNodeId = Guid.NewGuid();
        var targetNodeId = Guid.NewGuid();
        var debugSessionId = Guid.NewGuid();
        var outputDir = Path.Combine(Path.GetTempPath(), $"ClearVision-TextSavePreview-{Guid.NewGuid():N}");
        var outputPath = Path.Combine(outputDir, "preview.txt");
        var sourceOutputPort = CreatePort("Text", PortDataType.String, PortDirection.Output);
        var textSaveInputPort = CreatePort("Text", PortDataType.String, PortDirection.Input);
        DebugOptions? capturedOptions = null;
        OperatorFlow? capturedFlow = null;

        try
        {
            await using var host = await PreviewNodeTestHost.CreateAsync(flowExecution =>
            {
                flowExecution.ExecuteDebugWithSnapshotAsync(
                        Arg.Any<ExecutionSnapshot>(),
                        Arg.Any<DebugOptions>(),
                        Arg.Any<Dictionary<string, object>?>(),
                        Arg.Any<ClearVision.Product.Core.ProjectVariables.ProjectVariableExecutionContext?>(),
                        Arg.Any<CancellationToken>())
                    .Returns(callInfo =>
                    {
                        capturedFlow = callInfo.ArgAt<ExecutionSnapshot>(0).CreateExecutionFlow();
                        capturedOptions = callInfo.ArgAt<DebugOptions>(1);

                        return Task.FromResult(new FlowDebugExecutionResult
                        {
                            IsSuccess = true,
                            DebugSessionId = debugSessionId,
                            ExecutionTimeMs = 7,
                            IntermediateResults = new Dictionary<Guid, Dictionary<string, object>>
                            {
                                [sourceNodeId] = new()
                                {
                                    ["Text"] = "dry-run text payload"
                                }
                            },
                            DebugOperatorResults = new List<OperatorDebugResult>
                            {
                                new()
                                {
                                    OperatorId = sourceNodeId,
                                    OperatorName = "StringFormat",
                                    IsSuccess = true,
                                    ExecutionOrder = 0,
                                    ExecutionTimeMs = 7,
                                    OutputSnapshot = new Dictionary<string, object>
                                    {
                                        ["Text"] = "dry-run text payload"
                                    }
                                }
                            }
                        });
                    });
            });

            Directory.Exists(outputDir).Should().BeFalse();

            using var response = await host.Client.PostAsJsonAsync("/api/flows/preview-node", new PreviewNodeRequest
            {
                ProjectId = projectId,
                TargetNodeId = targetNodeId,
                DebugSessionId = debugSessionId,
                FlowData = CreateUpdateFlowRequest(
                    CreateOperatorDto(
                        sourceNodeId,
                        "StringFormat",
                        OperatorType.StringFormat,
                        outputPorts: [sourceOutputPort]),
                    CreateOperatorDto(
                        targetNodeId,
                        "TextSave",
                        OperatorType.TextSave,
                        inputPorts: [textSaveInputPort],
                        parameters: new Dictionary<string, object>
                        {
                            ["FilePath"] = outputPath,
                            ["Format"] = "Text",
                            ["AppendMode"] = false,
                            ["Encoding"] = "UTF8"
                        }),
                    CreateConnection(sourceNodeId, sourceOutputPort.Id, targetNodeId, textSaveInputPort.Id))
            });

            var payload = await response.Content.ReadAsStringAsync();
            response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK, payload);
            using var document = JsonDocument.Parse(payload);
            document.RootElement.GetProperty("success").GetBoolean().Should().BeTrue(payload);
            document.RootElement.GetProperty("outputImageBase64").ValueKind.Should().Be(JsonValueKind.Null);

            var outputData = document.RootElement.GetProperty("outputData");
            outputData.GetProperty("PreviewMode").GetString().Should().Be("TextSaveDryRun");
            outputData.GetProperty("DryRun").GetBoolean().Should().BeTrue();
            outputData.GetProperty("WillWriteToDisk").GetBoolean().Should().BeFalse();
            outputData.GetProperty("Message").GetString().Should().Be("预览模式不会写入磁盘；点击运行流程后才会保存文本。");
            outputData.GetProperty("FilePathTemplate").GetString().Should().Be(outputPath);
            outputData.GetProperty("AppendMode").GetBoolean().Should().BeFalse();
            outputData.GetProperty("ContentSummary").GetString().Should().Contain("dry-run text payload");

            Directory.Exists(outputDir).Should().BeFalse("节点预览不能创建 TextSave 目录或写盘");
            File.Exists(outputPath).Should().BeFalse("节点预览不能写入 TextSave 文件");
            capturedOptions.Should().NotBeNull();
            capturedOptions!.BreakAtOperatorId.Should().BeNull();
            capturedFlow.Should().NotBeNull();
            capturedFlow!.Operators.Select(op => op.Type).Should().NotContain(OperatorType.TextSave);
        }
        finally
        {
            if (Directory.Exists(outputDir))
            {
                Directory.Delete(outputDir, recursive: true);
            }
        }
    }

    [Fact]
    public async Task PreviewNode_WithTextSaveTargetAndNoInputText_ShouldReturnMissingTextPreviewMessage()
    {
        var projectId = Guid.NewGuid();
        var sourceNodeId = Guid.NewGuid();
        var targetNodeId = Guid.NewGuid();
        var debugSessionId = Guid.NewGuid();
        var outputDir = Path.Combine(Path.GetTempPath(), $"ClearVision-TextSavePreviewNoText-{Guid.NewGuid():N}");
        var outputPath = Path.Combine(outputDir, "preview.txt");
        var sourceOutputPort = CreatePort("Text", PortDataType.String, PortDirection.Output);
        var textSaveInputPort = CreatePort("Text", PortDataType.String, PortDirection.Input);

        try
        {
            await using var host = await PreviewNodeTestHost.CreateAsync(flowExecution =>
            {
                flowExecution.ExecuteDebugWithSnapshotAsync(
                        Arg.Any<ExecutionSnapshot>(),
                        Arg.Any<DebugOptions>(),
                        Arg.Any<Dictionary<string, object>?>(),
                        Arg.Any<ClearVision.Product.Core.ProjectVariables.ProjectVariableExecutionContext?>(),
                        Arg.Any<CancellationToken>())
                    .Returns(_ => Task.FromResult(new FlowDebugExecutionResult
                    {
                        IsSuccess = true,
                        DebugSessionId = debugSessionId,
                        ExecutionTimeMs = 6,
                        IntermediateResults = new Dictionary<Guid, Dictionary<string, object>>
                        {
                            [sourceNodeId] = new()
                        },
                        DebugOperatorResults = new List<OperatorDebugResult>
                        {
                            new()
                            {
                                OperatorId = sourceNodeId,
                                OperatorName = "StringFormat",
                                IsSuccess = true,
                                ExecutionOrder = 0,
                                ExecutionTimeMs = 6,
                                OutputSnapshot = new Dictionary<string, object>()
                            }
                        }
                    }));
            });

            Directory.Exists(outputDir).Should().BeFalse();

            using var response = await host.Client.PostAsJsonAsync("/api/flows/preview-node", new PreviewNodeRequest
            {
                ProjectId = projectId,
                TargetNodeId = targetNodeId,
                DebugSessionId = debugSessionId,
                FlowData = CreateUpdateFlowRequest(
                    CreateOperatorDto(
                        sourceNodeId,
                        "StringFormat",
                        OperatorType.StringFormat,
                        outputPorts: [sourceOutputPort]),
                    CreateOperatorDto(
                        targetNodeId,
                        "TextSave",
                        OperatorType.TextSave,
                        inputPorts: [textSaveInputPort],
                        parameters: new Dictionary<string, object>
                        {
                            ["FilePath"] = outputPath,
                            ["Format"] = "Text",
                            ["AppendMode"] = false,
                            ["Encoding"] = "UTF8"
                        }),
                    CreateConnection(sourceNodeId, sourceOutputPort.Id, targetNodeId, textSaveInputPort.Id))
            });

            var payload = await response.Content.ReadAsStringAsync();
            response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK, payload);
            using var document = JsonDocument.Parse(payload);
            document.RootElement.GetProperty("success").GetBoolean().Should().BeTrue(payload);

            var outputData = document.RootElement.GetProperty("outputData");
            outputData.GetProperty("PreviewMode").GetString().Should().Be("TextSaveDryRun");
            outputData.GetProperty("WillWriteToDisk").GetBoolean().Should().BeFalse();
            outputData.GetProperty("PreviewSafe").GetBoolean().Should().BeTrue();
            outputData.GetProperty("Message").GetString().Should().Be("缺少输入文本，无法生成文本保存预览");
            outputData.GetProperty("_previewWarning").GetString().Should().Be("缺少输入文本，无法生成文本保存预览");
            outputData.GetProperty("ContentSummary").GetString().Should().Be("缺少输入文本");

            Directory.Exists(outputDir).Should().BeFalse("缺输入文本的 TextSave 预览不能创建目录");
            File.Exists(outputPath).Should().BeFalse("缺输入文本的 TextSave 预览不能写入文件");
        }
        finally
        {
            if (Directory.Exists(outputDir))
            {
                Directory.Delete(outputDir, recursive: true);
            }
        }
    }

    [Fact]
    public async Task PreviewNode_WithResultOutputSaveToFileTrue_ShouldDryRunWithoutWritingFile()
    {
        var projectId = Guid.NewGuid();
        var sourceNodeId = Guid.NewGuid();
        var targetNodeId = Guid.NewGuid();
        var debugSessionId = Guid.NewGuid();
        var sourceOutputPort = CreatePort("Result", PortDataType.Any, PortDirection.Output);
        var resultOutputInputPort = CreatePort("Result", PortDataType.Any, PortDirection.Input);
        DebugOptions? capturedOptions = null;
        OperatorFlow? capturedFlow = null;

        await using var host = await PreviewNodeTestHost.CreateAsync(flowExecution =>
        {
            flowExecution.ExecuteDebugWithSnapshotAsync(
                    Arg.Any<ExecutionSnapshot>(),
                    Arg.Any<DebugOptions>(),
                    Arg.Any<Dictionary<string, object>?>(),
                    Arg.Any<ClearVision.Product.Core.ProjectVariables.ProjectVariableExecutionContext?>(),
                    Arg.Any<CancellationToken>())
                .Returns(callInfo =>
                {
                    capturedFlow = callInfo.ArgAt<ExecutionSnapshot>(0).CreateExecutionFlow();
                    capturedOptions = callInfo.ArgAt<DebugOptions>(1);

                    return Task.FromResult(new FlowDebugExecutionResult
                    {
                        IsSuccess = true,
                        DebugSessionId = debugSessionId,
                        ExecutionTimeMs = 5,
                        IntermediateResults = new Dictionary<Guid, Dictionary<string, object>>
                        {
                            [sourceNodeId] = new()
                            {
                                ["Result"] = new Dictionary<string, object>
                                {
                                    ["Score"] = 0.93,
                                    ["Status"] = "OK"
                                }
                            }
                        },
                        DebugOperatorResults = new List<OperatorDebugResult>
                        {
                            new()
                            {
                                OperatorId = sourceNodeId,
                                OperatorName = "ResultJudgment",
                                IsSuccess = true,
                                ExecutionOrder = 0,
                                ExecutionTimeMs = 5,
                                OutputSnapshot = new Dictionary<string, object>
                                {
                                    ["Result"] = new Dictionary<string, object>
                                    {
                                        ["Score"] = 0.93,
                                        ["Status"] = "OK"
                                    }
                                }
                            }
                        }
                    });
                });
        });

        using var response = await host.Client.PostAsJsonAsync("/api/flows/preview-node", new PreviewNodeRequest
        {
            ProjectId = projectId,
            TargetNodeId = targetNodeId,
            DebugSessionId = debugSessionId,
            FlowData = CreateUpdateFlowRequest(
                CreateOperatorDto(
                    sourceNodeId,
                    "ResultJudgment",
                    OperatorType.ResultJudgment,
                    outputPorts: [sourceOutputPort]),
                CreateOperatorDto(
                    targetNodeId,
                    "ResultOutput",
                    OperatorType.ResultOutput,
                    inputPorts: [resultOutputInputPort],
                    parameters: new Dictionary<string, object>
                    {
                        ["SaveToFile"] = true,
                        ["Format"] = "JSON"
                    }),
                CreateConnection(sourceNodeId, sourceOutputPort.Id, targetNodeId, resultOutputInputPort.Id))
        });

        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK, payload);
        using var document = JsonDocument.Parse(payload);
        document.RootElement.GetProperty("success").GetBoolean().Should().BeTrue(payload);

        var outputData = document.RootElement.GetProperty("outputData");
        outputData.GetProperty("PreviewMode").GetString().Should().Be("ResultOutputDryRun");
        outputData.GetProperty("DryRun").GetBoolean().Should().BeTrue();
        outputData.GetProperty("SaveToFile").GetBoolean().Should().BeTrue();
        outputData.GetProperty("WillWriteToDisk").GetBoolean().Should().BeFalse();
        outputData.GetProperty("Message").GetString().Should().Be("预览模式不会写入磁盘；点击运行流程后才会保存结果文件。");
        outputData.GetProperty("EstimatedFilePath").GetString().Should().NotBeNullOrWhiteSpace();
        File.Exists(outputData.GetProperty("EstimatedFilePath").GetString()!).Should().BeFalse("ResultOutput 预览不能写入结果文件");

        capturedOptions.Should().NotBeNull();
        capturedOptions!.BreakAtOperatorId.Should().BeNull();
        capturedFlow.Should().NotBeNull();
        capturedFlow!.Operators.Select(op => op.Type).Should().NotContain(OperatorType.ResultOutput);
    }

    [Fact]
    public async Task PreviewNode_WithResultOutputSaveToFileFalse_ShouldReturnStructuredPreview()
    {
        var projectId = Guid.NewGuid();
        var targetNodeId = Guid.NewGuid();

        await using var host = await PreviewNodeTestHost.CreateAsync(flowExecution =>
        {
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
                    ExecutionTimeMs = 4,
                    IntermediateResults = new Dictionary<Guid, Dictionary<string, object>>
                    {
                        [targetNodeId] = new()
                        {
                            ["Output"] = "OK",
                            ["Result"] = new Dictionary<string, object>
                            {
                                ["Score"] = 0.91
                            }
                        }
                    }
                }));
        });

        using var response = await host.Client.PostAsJsonAsync("/api/flows/preview-node", new PreviewNodeRequest
        {
            ProjectId = projectId,
            TargetNodeId = targetNodeId,
            FlowData = CreateUpdateFlowRequest(
                CreateOperatorDto(
                    targetNodeId,
                    "ResultOutput",
                    OperatorType.ResultOutput,
                    parameters: new Dictionary<string, object>
                    {
                        ["SaveToFile"] = false,
                        ["Format"] = "JSON"
                    }))
        });

        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK, payload);
        using var document = JsonDocument.Parse(payload);
        document.RootElement.GetProperty("success").GetBoolean().Should().BeTrue(payload);
        var outputData = document.RootElement.GetProperty("outputData");
        outputData.GetProperty("Output").GetString().Should().Be("OK");
        outputData.TryGetProperty("DryRun", out _).Should().BeFalse();
    }

    [Fact]
    public async Task PreviewNode_ShouldPropagateRequestCancellationToFlowExecution()
    {
        var projectId = Guid.NewGuid();
        var targetNodeId = Guid.NewGuid();
        var enteredExecution = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancellationObserved = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var capturedToken = CancellationToken.None;

        await using var host = await PreviewNodeTestHost.CreateAsync(flowExecution =>
        {
            flowExecution.ExecuteDebugWithSnapshotAsync(
                    Arg.Any<ExecutionSnapshot>(),
                    Arg.Any<DebugOptions>(),
                    Arg.Any<Dictionary<string, object>?>(),
                    Arg.Any<ClearVision.Product.Core.ProjectVariables.ProjectVariableExecutionContext?>(),
                    Arg.Any<CancellationToken>())
                .Returns(callInfo =>
                {
                    capturedToken = callInfo.ArgAt<CancellationToken>(4);
                    enteredExecution.TrySetResult(null);
                    return CompleteWhenCanceledAsync(targetNodeId, capturedToken, cancellationObserved);
                });
        });

        using var cts = new CancellationTokenSource();
        var requestTask = host.Client.PostAsJsonAsync("/api/flows/preview-node", new PreviewNodeRequest
        {
            ProjectId = projectId,
            TargetNodeId = targetNodeId,
            FlowData = CreateUpdateFlowRequest(
                CreateOperatorDto(targetNodeId, "Threshold", OperatorType.Thresholding))
        }, cts.Token);

        await enteredExecution.Task.WaitAsync(TimeSpan.FromSeconds(5));
        capturedToken.CanBeCanceled.Should().BeTrue();

        cts.Cancel();
        await cancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));

        try
        {
            using var _ = await requestTask;
        }
        catch (OperationCanceledException)
        {
            // The client side is expected to observe cancellation once the request is aborted.
        }
    }

    [Fact]
    public async Task PreviewNode_ShouldPreferStoredFlow_WhenRequestOmitsInlineFlowData()
    {
        var projectId = Guid.NewGuid();
        var storedNodeId = Guid.NewGuid();
        var databaseNodeId = Guid.NewGuid();
        OperatorFlow? capturedFlow = null;

        await using var host = await PreviewNodeTestHost.CreateAsync(
            configureFlowExecution: flowExecution =>
            {
                flowExecution.ExecuteDebugWithSnapshotAsync(
                        Arg.Any<ExecutionSnapshot>(),
                        Arg.Any<DebugOptions>(),
                        Arg.Any<Dictionary<string, object>?>(),
                        Arg.Any<ClearVision.Product.Core.ProjectVariables.ProjectVariableExecutionContext?>(),
                        Arg.Any<CancellationToken>())
                    .Returns(callInfo =>
                    {
                        capturedFlow = callInfo.ArgAt<ExecutionSnapshot>(0).CreateExecutionFlow();
                        return Task.FromResult(new FlowDebugExecutionResult
                        {
                            IsSuccess = true,
                            DebugSessionId = Guid.NewGuid(),
                            ExecutionTimeMs = 6,
                            IntermediateResults = new Dictionary<Guid, Dictionary<string, object>>
                            {
                                [storedNodeId] = new()
                                {
                                    ["Image"] = new byte[] { 1, 2, 3 }
                                }
                            }
                        });
                    });
            },
            configureProjectRepository: projectRepository =>
            {
                var project = new Project("preview-stored-flow");
                var databaseFlow = new OperatorFlow("DatabaseFlow");
                databaseFlow.AddOperator(new Operator(databaseNodeId, "db-node", OperatorType.ResultOutput, 0, 0));
                project.UpdateFlow(databaseFlow);
                projectRepository.GetWithFlowAsync(projectId).Returns(project);
            },
            configureFlowStorage: flowStorage =>
            {
                flowStorage.LoadFlowJsonAsync(projectId).Returns(CreateStoredFlowJson(
                    CreateOperatorDto(storedNodeId, "stored-node", OperatorType.ResultOutput)));
            });

        using var response = await host.Client.PostAsJsonAsync("/api/flows/preview-node", new PreviewNodeRequest
        {
            ProjectId = projectId,
            TargetNodeId = storedNodeId
        });

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        capturedFlow.Should().NotBeNull();
        capturedFlow!.Operators.Should().ContainSingle(operatorEntity => operatorEntity.Id == storedNodeId);
        capturedFlow.Operators.Should().NotContain(operatorEntity => operatorEntity.Id == databaseNodeId);
    }

    [Fact]
    public async Task PreviewNode_WithStoredFlow_ShouldPruneUnrelatedSideEffectBranchBeforeAdmissionAndExecution()
    {
        var projectId = Guid.NewGuid();
        var sourceNodeId = Guid.NewGuid();
        var targetNodeId = Guid.NewGuid();
        var httpNodeId = Guid.NewGuid();
        var textSaveNodeId = Guid.NewGuid();
        var sourceOutputPort = CreatePort("Result", PortDataType.Any, PortDirection.Output);
        var targetInputPort = CreatePort("Result", PortDataType.Any, PortDirection.Input);
        var httpOutputPort = CreatePort("Response", PortDataType.String, PortDirection.Output);
        var textSaveInputPort = CreatePort("Text", PortDataType.String, PortDirection.Input);
        OperatorFlow? capturedFlow = null;

        await using var host = await PreviewNodeTestHost.CreateAsync(
            configureFlowExecution: flowExecution =>
            {
                flowExecution.ExecuteDebugWithSnapshotAsync(
                        Arg.Any<ExecutionSnapshot>(),
                        Arg.Any<DebugOptions>(),
                        Arg.Any<Dictionary<string, object>?>(),
                        Arg.Any<ClearVision.Product.Core.ProjectVariables.ProjectVariableExecutionContext?>(),
                        Arg.Any<CancellationToken>())
                    .Returns(callInfo =>
                    {
                        capturedFlow = callInfo.ArgAt<ExecutionSnapshot>(0).CreateExecutionFlow();
                        return Task.FromResult(new FlowDebugExecutionResult
                        {
                            IsSuccess = true,
                            DebugSessionId = Guid.NewGuid(),
                            ExecutionTimeMs = 6,
                            IntermediateResults = new Dictionary<Guid, Dictionary<string, object>>
                            {
                                [targetNodeId] = new()
                                {
                                    ["Output"] = "OK"
                                }
                            }
                        });
                    });
            },
            configureProjectRepository: projectRepository =>
            {
                projectRepository.GetWithFlowAsync(projectId).Returns(new Project("preview-stored-flow"));
            },
            configureFlowStorage: flowStorage =>
            {
                flowStorage.LoadFlowJsonAsync(projectId).Returns(CreateStoredFlowJsonWithConnections(
                    [
                        CreateOperatorDto(
                            sourceNodeId,
                            "StringFormat",
                            OperatorType.StringFormat,
                            outputPorts: [sourceOutputPort]),
                        CreateOperatorDto(
                            targetNodeId,
                            "ResultJudgment",
                            OperatorType.ResultJudgment,
                            inputPorts: [targetInputPort]),
                        CreateOperatorDto(
                            httpNodeId,
                            "HttpRequest",
                            OperatorType.HttpRequest,
                            outputPorts: [httpOutputPort]),
                        CreateOperatorDto(
                            textSaveNodeId,
                            "TextSave",
                            OperatorType.TextSave,
                            inputPorts: [textSaveInputPort],
                            parameters: new Dictionary<string, object>
                            {
                                ["FilePath"] = "unrelated-branch.txt"
                            })
                    ],
                    [
                        CreateConnection(sourceNodeId, sourceOutputPort.Id, targetNodeId, targetInputPort.Id),
                        CreateConnection(httpNodeId, httpOutputPort.Id, textSaveNodeId, textSaveInputPort.Id)
                    ]));
            });

        using var response = await host.Client.PostAsJsonAsync("/api/flows/preview-node", new PreviewNodeRequest
        {
            ProjectId = projectId,
            TargetNodeId = targetNodeId
        });

        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK, payload);
        capturedFlow.Should().NotBeNull();
        capturedFlow!.Operators.Select(item => item.Id).Should().BeEquivalentTo(new[] { sourceNodeId, targetNodeId });
        capturedFlow.Operators.Select(item => item.Type).Should().NotContain(OperatorType.HttpRequest);
        capturedFlow.Operators.Select(item => item.Type).Should().NotContain(OperatorType.TextSave);
        capturedFlow.Connections.Should().ContainSingle(connection =>
            connection.SourceOperatorId == sourceNodeId &&
            connection.TargetOperatorId == targetNodeId);
    }

    [Fact]
    public async Task PreviewNode_WithStoredFlowUpstreamSideEffect_ShouldRejectBeforeDebugExecution()
    {
        var projectId = Guid.NewGuid();
        var httpNodeId = Guid.NewGuid();
        var targetNodeId = Guid.NewGuid();
        var httpOutputPort = CreatePort("Response", PortDataType.String, PortDirection.Output);
        var targetInputPort = CreatePort("Result", PortDataType.Any, PortDirection.Input);
        IFlowExecutionService? flowExecution = null;

        await using var host = await PreviewNodeTestHost.CreateAsync(
            configureFlowExecution: configuredFlowExecution =>
            {
                flowExecution = configuredFlowExecution;
                configuredFlowExecution.ExecuteDebugWithSnapshotAsync(
                        Arg.Any<ExecutionSnapshot>(),
                        Arg.Any<DebugOptions>(),
                        Arg.Any<Dictionary<string, object>?>(),
                        Arg.Any<ClearVision.Product.Core.ProjectVariables.ProjectVariableExecutionContext?>(),
                        Arg.Any<CancellationToken>())
                    .Returns(Task.FromResult(new FlowDebugExecutionResult { IsSuccess = true }));
            },
            configureProjectRepository: projectRepository =>
            {
                projectRepository.GetWithFlowAsync(projectId).Returns(new Project("preview-stored-side-effect-flow"));
            },
            configureFlowStorage: flowStorage =>
            {
                flowStorage.LoadFlowJsonAsync(projectId).Returns(CreateStoredFlowJsonWithConnections(
                    [
                        CreateOperatorDto(
                            httpNodeId,
                            "HttpRequest",
                            OperatorType.HttpRequest,
                            outputPorts: [httpOutputPort]),
                        CreateOperatorDto(
                            targetNodeId,
                            "ResultJudgment",
                            OperatorType.ResultJudgment,
                            inputPorts: [targetInputPort])
                    ],
                    [
                        CreateConnection(httpNodeId, httpOutputPort.Id, targetNodeId, targetInputPort.Id)
                    ]));
            });

        using var response = await host.Client.PostAsJsonAsync("/api/flows/preview-node", new PreviewNodeRequest
        {
            ProjectId = projectId,
            TargetNodeId = targetNodeId
        });

        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.BadRequest, payload);
        payload.Should().Contain("ADMISSION_DRAFT_PREVIEW_SIDE_EFFECT_BLOCKED");
        payload.Should().Contain("file, network, database, or device side effects");
        await flowExecution!.DidNotReceiveWithAnyArgs().ExecuteDebugWithSnapshotAsync(
            Arg.Any<ExecutionSnapshot>(),
            Arg.Any<DebugOptions>(),
            Arg.Any<Dictionary<string, object>?>(),
            Arg.Any<ClearVision.Product.Core.ProjectVariables.ProjectVariableExecutionContext?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PreviewNode_ReturnsMinimalFeedbackMetrics()
    {
        var projectId = Guid.NewGuid();
        var targetNodeId = Guid.NewGuid();
        var previewImage = CreateBinaryPreviewImageBytes();

        await using var host = await PreviewNodeTestHost.CreateAsync(flowExecution =>
        {
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
                    ExecutionTimeMs = 24,
                    IntermediateResults = new Dictionary<Guid, Dictionary<string, object>>
                    {
                        [targetNodeId] = new()
                        {
                            ["Image"] = previewImage,
                            ["Defects"] = new List<Dictionary<string, object>>
                            {
                                new() { ["Area"] = 4.0 },
                                new() { ["Area"] = 6.0 }
                            }
                        }
                    }
                }));
        });

        using var response = await host.Client.PostAsJsonAsync("/api/flows/preview-node", new PreviewNodeRequest
        {
            ProjectId = projectId,
            TargetNodeId = targetNodeId,
            FlowData = CreateUpdateFlowRequest(
                CreateOperatorDto(targetNodeId, "Threshold", OperatorType.Thresholding))
        });

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var metrics = document.RootElement.GetProperty("metrics");
        metrics.GetProperty("blobCount").GetInt32().Should().Be(2);
        metrics.GetProperty("binaryRatio").GetDouble().Should().BeApproximately(0.5d, 0.001d);
        metrics.GetProperty("areaStats").GetProperty("min").GetDouble().Should().Be(4d);
        metrics.GetProperty("areaStats").GetProperty("max").GetDouble().Should().Be(6d);
        metrics.GetProperty("areaStats").GetProperty("mean").GetDouble().Should().Be(5d);
    }

    [Fact]
    public async Task PreviewNode_ReturnsDetectionFeedbackMetrics()
    {
        var projectId = Guid.NewGuid();
        var targetNodeId = Guid.NewGuid();

        await using var host = await PreviewNodeTestHost.CreateAsync(flowExecution =>
        {
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
                    ExecutionTimeMs = 18,
                    IntermediateResults = new Dictionary<Guid, Dictionary<string, object>>
                    {
                        [targetNodeId] = new()
                        {
                            ["DetectionList"] = new DetectionList(new[]
                            {
                                new DetectionResultValue("Wire_Brown", 0.98f, 10f, 10f, 8f, 8f),
                                new DetectionResultValue("Wire_Black", 0.66f, 30f, 10f, 8f, 8f)
                            }),
                            ["ObjectCount"] = 2,
                            ["ExpectedLabels"] = new[] { "Wire_Brown", "Wire_Black", "Wire_Blue" },
                            ["RequiredMinConfidence"] = 0.8
                        }
                    }
                }));
        });

        using var response = await host.Client.PostAsJsonAsync("/api/flows/preview-node", new PreviewNodeRequest
        {
            ProjectId = projectId,
            TargetNodeId = targetNodeId,
            FlowData = CreateUpdateFlowRequest(
                CreateOperatorDto(targetNodeId, "Judge", OperatorType.DetectionSequenceJudge))
        });

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var metrics = document.RootElement.GetProperty("metrics");

        metrics.GetProperty("detectionCount").GetInt32().Should().Be(2);
        metrics.GetProperty("objectCount").GetInt32().Should().Be(2);
        metrics.GetProperty("sortedLabels")[0].GetString().Should().Be("Wire_Brown");
        metrics.GetProperty("sortedLabels")[1].GetString().Should().Be("Wire_Black");
        metrics.GetProperty("missingLabels")[0].GetString().Should().Be("Wire_Blue");
        metrics.GetProperty("diagnostics").EnumerateArray().Select(item => item.GetString()).Should()
            .Contain(new[]
            {
                PreviewDiagnosticTags.MissingExpectedClass,
                PreviewDiagnosticTags.DetectionCountMismatch,
                PreviewDiagnosticTags.LowDetectionConfidence,
                PreviewDiagnosticTags.OrderMismatch
            });

        var perClassCount = metrics.GetProperty("perClassCount");
        perClassCount.GetProperty("Wire_Brown").GetInt32().Should().Be(1);
        perClassCount.GetProperty("Wire_Black").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task PreviewNode_ShouldInjectExternalImage_WhenTargetPathContainsFileImageAcquisitionWithoutFilePath()
    {
        var projectId = Guid.NewGuid();
        var acquisitionId = Guid.NewGuid();
        var targetNodeId = Guid.NewGuid();
        var acquisitionOutput = CreatePort("Image", PortDataType.Image, PortDirection.Output);
        var targetInput = CreatePort("Image", PortDataType.Image, PortDirection.Input, isRequired: true);
        var targetOutput = CreatePort("Image", PortDataType.Image, PortDirection.Output);
        Dictionary<string, object>? capturedInput = null;

        await using var host = await PreviewNodeTestHost.CreateAsync(flowExecution =>
        {
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
                            [targetNodeId] = new()
                            {
                                ["Image"] = new byte[] { 1, 2, 3 }
                            }
                        }
                    });
                });
        });

        using var response = await host.Client.PostAsJsonAsync("/api/flows/preview-node", new PreviewNodeRequest
        {
            ProjectId = projectId,
            TargetNodeId = targetNodeId,
            InputImageBase64 = Convert.ToBase64String(new byte[] { 9, 9, 9 }),
            FlowData = CreateUpdateFlowRequest(
                CreateOperatorDto(acquisitionId, "图像采集", OperatorType.ImageAcquisition, outputPorts: [acquisitionOutput]),
                CreateOperatorDto(targetNodeId, "图像缩放", OperatorType.ImageResize,
                    inputPorts: [targetInput],
                    outputPorts: [targetOutput]),
                CreateConnection(acquisitionId, acquisitionOutput.Id, targetNodeId, targetInput.Id))
        });

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        capturedInput.Should().NotBeNull();
        capturedInput!["Image"].Should().BeEquivalentTo(new byte[] { 9, 9, 9 });
    }

    [Fact]
    public async Task PreviewNode_ShouldInjectExternalImage_WhenTargetIsFileImageAcquisitionWithoutFilePath()
    {
        var projectId = Guid.NewGuid();
        var targetNodeId = Guid.NewGuid();
        var targetOutput = CreatePort("Image", PortDataType.Image, PortDirection.Output);
        Dictionary<string, object>? capturedInput = null;

        await using var host = await PreviewNodeTestHost.CreateAsync(flowExecution =>
        {
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
                            [targetNodeId] = new()
                            {
                                ["Image"] = new byte[] { 1, 2, 3 }
                            }
                        }
                    });
                });
        });

        using var response = await host.Client.PostAsJsonAsync("/api/flows/preview-node", new PreviewNodeRequest
        {
            ProjectId = projectId,
            TargetNodeId = targetNodeId,
            InputImageBase64 = Convert.ToBase64String(new byte[] { 9, 9, 9 }),
            FlowData = CreateUpdateFlowRequest(
                CreateOperatorDto(targetNodeId, "图像采集", OperatorType.ImageAcquisition, outputPorts: [targetOutput]))
        });

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        capturedInput.Should().NotBeNull();
        capturedInput!["Image"].Should().BeEquivalentTo(new byte[] { 9, 9, 9 });
    }

    [Fact]
    public async Task PreviewNode_ShouldInjectExternalImage_WhenNoImageAcquisitionExistsUpstream()
    {
        var projectId = Guid.NewGuid();
        var targetNodeId = Guid.NewGuid();
        var targetInput = CreatePort("Image", PortDataType.Image, PortDirection.Input, isRequired: true);
        var targetOutput = CreatePort("Image", PortDataType.Image, PortDirection.Output);
        Dictionary<string, object>? capturedInput = null;

        await using var host = await PreviewNodeTestHost.CreateAsync(flowExecution =>
        {
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
                            [targetNodeId] = new()
                            {
                                ["Image"] = new byte[] { 1, 2, 3 }
                            }
                        }
                    });
                });
        });

        var externalImage = Convert.ToBase64String(new byte[] { 7, 8, 9 });
        using var response = await host.Client.PostAsJsonAsync("/api/flows/preview-node", new PreviewNodeRequest
        {
            ProjectId = projectId,
            TargetNodeId = targetNodeId,
            InputImageBase64 = externalImage,
            FlowData = CreateUpdateFlowRequest(
                CreateOperatorDto(targetNodeId, "图像缩放", OperatorType.ImageResize,
                    inputPorts: [targetInput],
                    outputPorts: [targetOutput]))
        });

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        capturedInput.Should().NotBeNull();
        var input = capturedInput!;
        input.Should().ContainKey("Image");
        ((byte[])input["Image"]).Should().Equal(new byte[] { 7, 8, 9 });
    }

    [Fact]
    public async Task PreviewNode_ShouldFallbackInputImageToUpstreamImageAcquisitionOutput()
    {
        var projectId = Guid.NewGuid();
        var acquisitionId = Guid.NewGuid();
        var targetNodeId = Guid.NewGuid();
        var acquisitionOutput = CreatePort("Image", PortDataType.Image, PortDirection.Output);
        var targetInput = CreatePort("Image", PortDataType.Image, PortDirection.Input, isRequired: true);
        var targetOutput = CreatePort("Image", PortDataType.Image, PortDirection.Output);
        var acquisitionImage = CreateBinaryPreviewImageBytes();
        var targetImage = new byte[] { 4, 5, 6 };

        await using var host = await PreviewNodeTestHost.CreateAsync(flowExecution =>
        {
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
                    ExecutionTimeMs = 22,
                    IntermediateResults = new Dictionary<Guid, Dictionary<string, object>>
                    {
                        [targetNodeId] = new()
                        {
                            ["Image"] = targetImage
                        }
                    },
                    DebugOperatorResults = new List<OperatorDebugResult>
                    {
                        new()
                        {
                            OperatorId = acquisitionId,
                            OperatorName = "Acquire",
                            IsSuccess = true,
                            ExecutionOrder = 0,
                            OutputSnapshot = new Dictionary<string, object>
                            {
                                ["Image"] = acquisitionImage
                            }
                        },
                        new()
                        {
                            OperatorId = targetNodeId,
                            OperatorName = "Resize",
                            IsSuccess = true,
                            ExecutionOrder = 1,
                            InputSnapshot = new Dictionary<string, object>(),
                            OutputSnapshot = new Dictionary<string, object>
                            {
                                ["Image"] = targetImage
                            }
                        }
                    }
                }));
        });

        using var response = await host.Client.PostAsJsonAsync("/api/flows/preview-node", new PreviewNodeRequest
        {
            ProjectId = projectId,
            TargetNodeId = targetNodeId,
            FlowData = CreateUpdateFlowRequest(
                CreateOperatorDto(
                    acquisitionId,
                    "Acquire",
                    OperatorType.ImageAcquisition,
                    outputPorts: [acquisitionOutput],
                    parameters: new Dictionary<string, object> { ["SourceType"] = "ProvidedFrame" }),
                CreateOperatorDto(
                    targetNodeId,
                    "Resize",
                    OperatorType.ImageResize,
                    inputPorts: [targetInput],
                    outputPorts: [targetOutput]),
                CreateConnection(acquisitionId, acquisitionOutput.Id, targetNodeId, targetInput.Id))
        });

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        document.RootElement.GetProperty("inputImageBase64").GetString().Should().Be(Convert.ToBase64String(acquisitionImage));
        document.RootElement.GetProperty("outputImageBase64").GetString().Should().Be(Convert.ToBase64String(targetImage));
    }

    [Fact]
    public async Task PreviewNode_ShouldExtractImageFromImageWrapperIntermediateOutput()
    {
        var projectId = Guid.NewGuid();
        var targetNodeId = Guid.NewGuid();
        using var previewMat = new Mat(2, 2, MatType.CV_8UC1, Scalar.All(0));
        previewMat.Set(0, 0, 255);
        var expectedBytes = previewMat.ToBytes(".png");

        await using var host = await PreviewNodeTestHost.CreateAsync(flowExecution =>
        {
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
                    ExecutionTimeMs = 10,
                    IntermediateResults = new Dictionary<Guid, Dictionary<string, object>>
                    {
                        [targetNodeId] = new()
                        {
                            ["Image"] = new ImageWrapper(previewMat.Clone())
                        }
                    }
                }));
        });

        using var response = await host.Client.PostAsJsonAsync("/api/flows/preview-node", new PreviewNodeRequest
        {
            ProjectId = projectId,
            TargetNodeId = targetNodeId,
            FlowData = CreateUpdateFlowRequest(
                CreateOperatorDto(targetNodeId, "Threshold", OperatorType.Thresholding))
        });

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        document.RootElement.GetProperty("outputImageBase64").GetString().Should().Be(Convert.ToBase64String(expectedBytes));
    }

    [Fact]
    public async Task PreviewNode_ShouldHideOriginalImageFromOutputData_AndKeepPreviewBoundToImage()
    {
        var projectId = Guid.NewGuid();
        var targetNodeId = Guid.NewGuid();
        var previewImage = new byte[] { 1, 2, 3, 4 };
        var originalImage = new byte[] { 9, 8, 7, 6 };

        await using var host = await PreviewNodeTestHost.CreateAsync(flowExecution =>
        {
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
                    ExecutionTimeMs = 10,
                    IntermediateResults = new Dictionary<Guid, Dictionary<string, object>>
                    {
                        [targetNodeId] = new()
                        {
                            ["Image"] = previewImage,
                            ["OriginalImage"] = originalImage,
                            ["Count"] = 1
                        }
                    }
                }));
        });

        using var response = await host.Client.PostAsJsonAsync("/api/flows/preview-node", new PreviewNodeRequest
        {
            ProjectId = projectId,
            TargetNodeId = targetNodeId,
            FlowData = CreateUpdateFlowRequest(
                CreateOperatorDto(targetNodeId, "Resize", OperatorType.ImageResize))
        });

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        document.RootElement.GetProperty("outputImageBase64").GetString().Should().Be(Convert.ToBase64String(previewImage));

        var outputData = document.RootElement.GetProperty("outputData");
        outputData.TryGetProperty("OriginalImage", out _).Should().BeFalse();
        outputData.TryGetProperty("Image", out _).Should().BeFalse();
        outputData.GetProperty("Count").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task PreviewNode_ShouldIgnoreInvalidDownstreamConnectionsOutsideTargetSubgraph()
    {
        var projectId = Guid.NewGuid();
        var acquisitionId = Guid.NewGuid();
        var integerNodeId = Guid.NewGuid();
        var invalidTargetNodeId = Guid.NewGuid();
        var acquisitionOutput = CreatePort("Image", PortDataType.Image, PortDirection.Output);
        var integerOutput = CreatePort("Count", PortDataType.Integer, PortDirection.Output);
        var detectionInput = CreatePort("Detections", PortDataType.DetectionList, PortDirection.Input, isRequired: true);
        var previewImage = new byte[] { 4, 3, 2, 1 };

        await using var host = await PreviewNodeTestHost.CreateAsync(flowExecution =>
        {
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
                    ExecutionTimeMs = 8,
                    IntermediateResults = new Dictionary<Guid, Dictionary<string, object>>
                    {
                        [acquisitionId] = new()
                        {
                            ["Image"] = previewImage
                        }
                    }
                }));
        });

        using var response = await host.Client.PostAsJsonAsync("/api/flows/preview-node", new PreviewNodeRequest
        {
            ProjectId = projectId,
            TargetNodeId = acquisitionId,
            FlowData = CreateUpdateFlowRequest(
                CreateOperatorDto(
                    acquisitionId,
                    "Acquire",
                    OperatorType.ImageAcquisition,
                    outputPorts: [acquisitionOutput],
                    parameters: new Dictionary<string, object> { ["SourceType"] = "ProvidedFrame" }),
                CreateOperatorDto(
                    integerNodeId,
                    "Counter",
                    OperatorType.VariableIncrement,
                    outputPorts: [integerOutput]),
                CreateOperatorDto(
                    invalidTargetNodeId,
                    "BoxFilter",
                    OperatorType.BoxFilter,
                    inputPorts: [detectionInput]),
                CreateConnection(integerNodeId, integerOutput.Id, invalidTargetNodeId, detectionInput.Id))
        });

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        document.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
        document.RootElement.GetProperty("outputImageBase64").GetString().Should().Be(Convert.ToBase64String(previewImage));
    }

    [Fact]
    public async Task PreviewNode_ShouldRepairIncompatiblePreferredPortIdsWithinTargetSubgraph()
    {
        var projectId = Guid.NewGuid();
        var sourceId = Guid.NewGuid();
        var targetNodeId = Guid.NewGuid();
        var sourceImageOutput = CreatePort("Image", PortDataType.Image, PortDirection.Output);
        var sourceCountOutput = CreatePort("Count", PortDataType.Integer, PortDirection.Output);
        var targetImageInput = CreatePort("Image", PortDataType.Image, PortDirection.Input, isRequired: true);
        var targetImageOutput = CreatePort("Image", PortDataType.Image, PortDirection.Output);
        OperatorFlow? capturedFlow = null;

        await using var host = await PreviewNodeTestHost.CreateAsync(flowExecution =>
        {
            flowExecution.ExecuteDebugWithSnapshotAsync(
                    Arg.Any<ExecutionSnapshot>(),
                    Arg.Any<DebugOptions>(),
                    Arg.Any<Dictionary<string, object>?>(),
                    Arg.Any<ClearVision.Product.Core.ProjectVariables.ProjectVariableExecutionContext?>(),
                    Arg.Any<CancellationToken>())
                .Returns(callInfo =>
                {
                    capturedFlow = callInfo.ArgAt<ExecutionSnapshot>(0).CreateExecutionFlow();
                    return Task.FromResult(new FlowDebugExecutionResult
                    {
                        IsSuccess = true,
                        DebugSessionId = Guid.NewGuid(),
                        ExecutionTimeMs = 9,
                        IntermediateResults = new Dictionary<Guid, Dictionary<string, object>>
                        {
                            [targetNodeId] = new()
                            {
                                ["Image"] = new byte[] { 1, 2, 3 },
                                ["Count"] = 1
                            }
                        }
                    });
                });
        });

        using var response = await host.Client.PostAsJsonAsync("/api/flows/preview-node", new PreviewNodeRequest
        {
            ProjectId = projectId,
            TargetNodeId = targetNodeId,
            FlowData = CreateUpdateFlowRequest(
                CreateOperatorDto(
                    sourceId,
                    "Source",
                    OperatorType.ImageResize,
                    outputPorts: [sourceImageOutput, sourceCountOutput]),
                CreateOperatorDto(
                    targetNodeId,
                    "Target",
                    OperatorType.ImageResize,
                    inputPorts: [targetImageInput],
                    outputPorts: [targetImageOutput]),
                CreateConnection(sourceId, sourceCountOutput.Id, targetNodeId, targetImageInput.Id))
        });

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        capturedFlow.Should().NotBeNull();

        var source = capturedFlow!.Operators.Single(op => op.Id == sourceId);
        var repairedConnection = capturedFlow.Connections.Single(conn =>
            conn.SourceOperatorId == sourceId &&
            conn.TargetOperatorId == targetNodeId);
        var repairedSourcePort = source.OutputPorts.Single(port => port.Id == repairedConnection.SourcePortId);

        repairedSourcePort.Name.Should().Be("Image");
        repairedSourcePort.DataType.Should().Be(PortDataType.Image);
    }

    [Fact]
    public async Task PreviewNode_ShouldPreferFailedOperatorInputSnapshotOverAcquisitionFallback()
    {
        var projectId = Guid.NewGuid();
        var acquisitionId = Guid.NewGuid();
        var resizeId = Guid.NewGuid();
        var targetNodeId = Guid.NewGuid();
        var acquisitionOutput = CreatePort("Image", PortDataType.Image, PortDirection.Output);
        var resizeInput = CreatePort("Image", PortDataType.Image, PortDirection.Input, isRequired: true);
        var resizeOutput = CreatePort("Image", PortDataType.Image, PortDirection.Output);
        var targetInput = CreatePort("Image", PortDataType.Image, PortDirection.Input, isRequired: true);
        var acquisitionImage = new byte[] { 1, 2, 3, 4 };
        var transformedImage = new byte[] { 9, 8, 7, 6 };

        await using var host = await PreviewNodeTestHost.CreateAsync(flowExecution =>
        {
            flowExecution.ExecuteDebugWithSnapshotAsync(
                    Arg.Any<ExecutionSnapshot>(),
                    Arg.Any<DebugOptions>(),
                    Arg.Any<Dictionary<string, object>?>(),
                    Arg.Any<ClearVision.Product.Core.ProjectVariables.ProjectVariableExecutionContext?>(),
                    Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(new FlowDebugExecutionResult
                {
                    IsSuccess = false,
                    DebugSessionId = Guid.NewGuid(),
                    ExecutionTimeMs = 11,
                    ErrorMessage = "Resize failed",
                    DebugOperatorResults = new List<OperatorDebugResult>
                    {
                        new()
                        {
                            OperatorId = acquisitionId,
                            OperatorName = "Acquire",
                            IsSuccess = true,
                            ExecutionOrder = 0,
                            OutputSnapshot = new Dictionary<string, object>
                            {
                                ["Image"] = acquisitionImage
                            }
                        },
                        new()
                        {
                            OperatorId = resizeId,
                            OperatorName = "Resize",
                            IsSuccess = false,
                            ErrorMessage = "Resize failed",
                            ExecutionOrder = 1,
                            InputSnapshot = new Dictionary<string, object>
                            {
                                ["Image"] = transformedImage
                            }
                        }
                    }
                }));
        });

        using var response = await host.Client.PostAsJsonAsync("/api/flows/preview-node", new PreviewNodeRequest
        {
            ProjectId = projectId,
            TargetNodeId = targetNodeId,
            FlowData = CreateUpdateFlowRequest(
                CreateOperatorDto(
                    acquisitionId,
                    "Acquire",
                    OperatorType.ImageAcquisition,
                    outputPorts: [acquisitionOutput],
                    parameters: new Dictionary<string, object> { ["SourceType"] = "ProvidedFrame" }),
                CreateOperatorDto(
                    resizeId,
                    "Resize",
                    OperatorType.ImageResize,
                    inputPorts: [resizeInput],
                    outputPorts: [resizeOutput]),
                CreateOperatorDto(
                    targetNodeId,
                    "Threshold",
                    OperatorType.Thresholding,
                    inputPorts: [targetInput]),
                CreateConnection(acquisitionId, acquisitionOutput.Id, resizeId, resizeInput.Id),
                CreateConnection(resizeId, resizeOutput.Id, targetNodeId, targetInput.Id))
        });

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        document.RootElement.GetProperty("success").GetBoolean().Should().BeFalse();
        document.RootElement.GetProperty("inputImageBase64").GetString().Should().Be(Convert.ToBase64String(transformedImage));
    }

    [Fact]
    public async Task PreviewNode_ShouldReturnGatewayTimeout_WhenPreviewExecutionExceedsTimeout()
    {
        var projectId = Guid.NewGuid();
        var targetNodeId = Guid.NewGuid();
        var cancellationObserved = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var host = await PreviewNodeTestHost.CreateAsync(flowExecution =>
        {
            flowExecution.ExecuteDebugWithSnapshotAsync(
                    Arg.Any<ExecutionSnapshot>(),
                    Arg.Any<DebugOptions>(),
                    Arg.Any<Dictionary<string, object>?>(),
                    Arg.Any<ClearVision.Product.Core.ProjectVariables.ProjectVariableExecutionContext?>(),
                    Arg.Any<CancellationToken>())
                .Returns(async callInfo =>
                {
                    var token = callInfo.ArgAt<CancellationToken>(4);
                    try
                    {
                        await Task.Delay(TimeSpan.FromSeconds(10), token);
                    }
                    catch (OperationCanceledException)
                    {
                        cancellationObserved.TrySetResult(null);
                        throw;
                    }

                    return new FlowDebugExecutionResult { IsSuccess = true };
                });
        });

        using var response = await host.Client.PostAsJsonAsync("/api/flows/preview-node", new PreviewNodeRequest
        {
            ProjectId = projectId,
            TargetNodeId = targetNodeId,
            TimeoutMs = 1,
            FlowData = CreateUpdateFlowRequest(
                CreateOperatorDto(targetNodeId, "Threshold", OperatorType.Thresholding))
        });

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.GatewayTimeout);
        await cancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task PreviewNode_ShouldOmitLargeOutputImagePayload_AndKeepStructuredSummary()
    {
        var projectId = Guid.NewGuid();
        var targetNodeId = Guid.NewGuid();
        var largeImage = new byte[(8 * 1024 * 1024) + 1];
        largeImage[0] = 1;

        await using var host = await PreviewNodeTestHost.CreateAsync(flowExecution =>
        {
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
                    ExecutionTimeMs = 5,
                    IntermediateResults = new Dictionary<Guid, Dictionary<string, object>>
                    {
                        [targetNodeId] = new()
                        {
                            ["Image"] = largeImage,
                            ["Score"] = 0.98
                        }
                    }
                }));
        });

        using var response = await host.Client.PostAsJsonAsync("/api/flows/preview-node", new PreviewNodeRequest
        {
            ProjectId = projectId,
            TargetNodeId = targetNodeId,
            FlowData = CreateUpdateFlowRequest(
                CreateOperatorDto(targetNodeId, "Threshold", OperatorType.Thresholding))
        });

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        document.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
        document.RootElement.GetProperty("outputImageBase64").ValueKind.Should().Be(JsonValueKind.Null);
        var outputData = document.RootElement.GetProperty("outputData");
        outputData.GetProperty("Score").GetDouble().Should().BeApproximately(0.98d, 0.001d);
        outputData.GetProperty("_previewWarning").GetString().Should().Contain("过大");
    }

    [Fact]
    public async Task PreviewNode_ShouldOmitLargeFailureInputImagePayload_AndKeepStructuredSummary()
    {
        var projectId = Guid.NewGuid();
        var targetNodeId = Guid.NewGuid();
        var largeInputImage = new byte[(8 * 1024 * 1024) + 1];
        largeInputImage[0] = 1;

        await using var host = await PreviewNodeTestHost.CreateAsync(flowExecution =>
        {
            flowExecution.ExecuteDebugWithSnapshotAsync(
                    Arg.Any<ExecutionSnapshot>(),
                    Arg.Any<DebugOptions>(),
                    Arg.Any<Dictionary<string, object>?>(),
                    Arg.Any<ClearVision.Product.Core.ProjectVariables.ProjectVariableExecutionContext?>(),
                    Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(new FlowDebugExecutionResult
                {
                    IsSuccess = false,
                    DebugSessionId = Guid.NewGuid(),
                    ExecutionTimeMs = 7,
                    ErrorMessage = "Target failed",
                    DebugOperatorResults = new List<OperatorDebugResult>
                    {
                        new()
                        {
                            OperatorId = targetNodeId,
                            OperatorName = "Threshold",
                            IsSuccess = false,
                            ExecutionOrder = 0,
                            ErrorMessage = "Target failed",
                            InputSnapshot = new Dictionary<string, object>
                            {
                                ["Image"] = largeInputImage
                            },
                            OutputSnapshot = new Dictionary<string, object>
                            {
                                ["Score"] = 0.42
                            }
                        }
                    }
                }));
        });

        using var response = await host.Client.PostAsJsonAsync("/api/flows/preview-node", new PreviewNodeRequest
        {
            ProjectId = projectId,
            TargetNodeId = targetNodeId,
            FlowData = CreateUpdateFlowRequest(
                CreateOperatorDto(targetNodeId, "Threshold", OperatorType.Thresholding))
        });

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        document.RootElement.GetProperty("success").GetBoolean().Should().BeFalse();
        document.RootElement.GetProperty("inputImageBase64").ValueKind.Should().Be(JsonValueKind.Null);
        var outputData = document.RootElement.GetProperty("outputData");
        outputData.GetProperty("Score").GetDouble().Should().BeApproximately(0.42d, 0.001d);
        outputData.GetProperty("_previewWarning").GetString().Should().Contain("过大");
    }

    private static int ReadIntValue(object? value)
    {
        return value switch
        {
            int intValue => intValue,
            long longValue => (int)longValue,
            double doubleValue => (int)doubleValue,
            JsonElement jsonElement when jsonElement.ValueKind == JsonValueKind.Number => jsonElement.GetInt32(),
            _ => throw new InvalidOperationException($"Unsupported parameter value: {value?.GetType().FullName ?? "<null>"}")
        };
    }

    private static string ReadStringValue(object? value)
    {
        return value switch
        {
            string stringValue => stringValue,
            JsonElement jsonElement when jsonElement.ValueKind == JsonValueKind.String => jsonElement.GetString() ?? string.Empty,
            _ => Convert.ToString(value) ?? string.Empty
        };
    }

    private static byte[] CreateBinaryPreviewImageBytes()
    {
        using var image = new Mat(2, 2, MatType.CV_8UC1, Scalar.All(0));
        image.Set(0, 0, 255);
        image.Set(1, 1, 255);
        return image.ToBytes(".png");
    }

    private static byte[] CreateTwoAreaBlobPreviewImageBytes()
    {
        using var image = new Mat(100, 120, MatType.CV_8UC1, Scalar.Black);
        Cv2.Rectangle(image, new OpenCvSharp.Rect(10, 10, 10, 10), Scalar.White, -1);
        Cv2.Rectangle(image, new OpenCvSharp.Rect(60, 10, 30, 20), Scalar.White, -1);
        return image.ToBytes(".png");
    }

    private static byte[] CreateFiveBlobPreviewImageBytes()
    {
        using var image = new Mat(100, 140, MatType.CV_8UC1, Scalar.Black);
        for (var index = 0; index < 5; index++)
        {
            Cv2.Rectangle(image, new OpenCvSharp.Rect(8 + (index * 25), 20, 10, 10), Scalar.White, -1);
        }

        return image.ToBytes(".png");
    }

    private static JsonElement GetObservationOutputNode(JsonElement responseRoot, string outputName)
    {
        return responseRoot
            .GetProperty("observation")
            .GetProperty("detail")
            .GetProperty("children")
            .EnumerateArray()
            .Single(item => string.Equals(item.GetProperty("name").GetString(), outputName, StringComparison.Ordinal));
    }

    private static int CountGreenPixels(Mat image, OpenCvSharp.Rect region)
    {
        using var roi = new Mat(image, region);
        using var greenMask = new Mat();
        Cv2.InRange(roi, new Scalar(0, 180, 0), new Scalar(80, 255, 80), greenMask);
        return Cv2.CountNonZero(greenMask);
    }

    private static async Task<FlowDebugExecutionResult> CompleteWhenCanceledAsync(
        Guid targetNodeId,
        CancellationToken cancellationToken,
        TaskCompletionSource<object?> cancellationObserved)
    {
        using var registration = cancellationToken.Register(() => cancellationObserved.TrySetResult(null));
        await cancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));

        return new FlowDebugExecutionResult
        {
            IsSuccess = false,
            ErrorMessage = "Canceled",
            IntermediateResults = new Dictionary<Guid, Dictionary<string, object>>
            {
                [targetNodeId] = new()
            }
        };
    }

    private static UpdateFlowRequest CreateUpdateFlowRequest(params object[] items)
    {
        var flow = new UpdateFlowRequest();
        foreach (var item in items)
        {
            switch (item)
            {
                case OperatorDto operatorDto:
                    flow.Operators.Add(operatorDto);
                    break;
                case OperatorConnectionDto connectionDto:
                    flow.Connections.Add(connectionDto);
                    break;
            }
        }

        return flow;
    }

    private static ProjectGlobalVariableSchema CreatePreviewVariableSchema(Guid variableId, long initialValue)
    {
        return new ProjectGlobalVariableSchema
        {
            Variables =
            [
                new ProjectGlobalVariableDefinition
                {
                    Id = variableId,
                    Name = "stats.count",
                    DisplayName = "Count",
                    ValueType = ProjectGlobalVariableValueType.Int64,
                    InitialValue = JsonSerializer.SerializeToElement(initialValue),
                    ManualWriteAllowed = true
                }
            ]
        };
    }

    private static async Task<JsonElement> PostPreviewReadAsync(
        PreviewNodeTestHost host,
        Guid projectId,
        OperatorDto read,
        Guid debugSessionId)
    {
        using var response = await host.Client.PostAsJsonAsync("/api/flows/preview-node", new PreviewNodeRequest
        {
            ProjectId = projectId,
            TargetNodeId = read.Id,
            DebugSessionId = debugSessionId,
            FlowData = CreateUpdateFlowRequest(read)
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK, payload);
        using var document = JsonDocument.Parse(payload);
        return document.RootElement.Clone();
    }

    private static OperatorDto CreateOperatorDto(
        Guid id,
        string name,
        OperatorType type,
        List<PortDto>? inputPorts = null,
        List<PortDto>? outputPorts = null,
        Dictionary<string, object>? parameters = null)
    {
        return new OperatorDto
        {
            Id = id,
            Name = name,
            Type = type,
            X = 0,
            Y = 0,
            IsEnabled = true,
            InputPorts = inputPorts ?? new List<PortDto>(),
            OutputPorts = outputPorts ?? new List<PortDto>(),
            Parameters = parameters?.Select(kvp => new ParameterDto
            {
                Id = Guid.NewGuid(),
                Name = kvp.Key,
                DisplayName = kvp.Key,
                DataType = kvp.Value switch
                {
                    int => "int",
                    long => "int",
                    float => "double",
                    double => "double",
                    bool => "bool",
                    _ => "string"
                },
                Value = kvp.Value,
                DefaultValue = kvp.Value,
                IsRequired = false
            }).ToList() ?? new List<ParameterDto>()
        };
    }

    private static PortDto CreatePort(
        string name,
        PortDataType dataType,
        PortDirection direction,
        bool isRequired = false)
    {
        return new PortDto
        {
            Id = Guid.NewGuid(),
            Name = name,
            DataType = dataType,
            Direction = direction,
            IsRequired = isRequired
        };
    }

    private static OperatorConnectionDto CreateConnection(
        Guid sourceOperatorId,
        Guid sourcePortId,
        Guid targetOperatorId,
        Guid targetPortId)
    {
        return new OperatorConnectionDto
        {
            Id = Guid.NewGuid(),
            SourceOperatorId = sourceOperatorId,
            SourcePortId = sourcePortId,
            TargetOperatorId = targetOperatorId,
            TargetPortId = targetPortId
        };
    }

    private static string CreateStoredFlowJson(params OperatorDto[] operators)
    {
        var flowDto = new OperatorFlowDto
        {
            Id = Guid.NewGuid(),
            Name = "StoredPreviewFlow",
            Operators = operators.ToList(),
            Connections = new List<OperatorConnectionDto>()
        };

        return JsonSerializer.Serialize(flowDto, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
        });
    }

    private static string CreateStoredFlowJsonWithConnections(
        IEnumerable<OperatorDto> operators,
        IEnumerable<OperatorConnectionDto> connections)
    {
        var flowDto = new OperatorFlowDto
        {
            Id = Guid.NewGuid(),
            Name = "StoredPreviewFlow",
            Operators = operators.ToList(),
            Connections = connections.ToList()
        };

        return JsonSerializer.Serialize(flowDto, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
        });
    }

    private static string MutateArtifactId(string artifactId)
    {
        var chars = artifactId.ToCharArray();
        chars[0] = chars[0] switch
        {
            >= 'a' and <= 'z' => char.ToUpperInvariant(chars[0]),
            >= 'A' and <= 'Z' => char.ToLowerInvariant(chars[0]),
            '0' => '1',
            '1' => '2',
            '-' => '_',
            '_' => '-',
            _ => '0'
        };

        return new string(chars);
    }

    private sealed class UnsafePreviewValue
    {
        public int Safe => 1;

        public int Explodes => throw new InvalidOperationException("getter failed");
    }

    private sealed class ThrowingToStringPreviewValue
    {
        public override string ToString() => throw new InvalidOperationException("ToString failed");
    }

    private sealed class ThrowingEnumerable : IEnumerable
    {
        public int GetEnumeratorCallCount { get; private set; }

        public IEnumerator GetEnumerator()
        {
            GetEnumeratorCallCount++;
            throw new InvalidOperationException("enumeration failed");
        }
    }

    private sealed class CountingInfiniteEnumerable : IEnumerable
    {
        public int MoveNextCount { get; private set; }

        public IEnumerator GetEnumerator()
        {
            while (true)
            {
                MoveNextCount++;
                yield return MoveNextCount;
            }
        }
    }

    private sealed class PreviewNodeTestHost : IAsyncDisposable
    {
        private readonly WebApplication _app;

        private PreviewNodeTestHost(WebApplication app, HttpClient client)
        {
            _app = app;
            Client = client;
        }

        public HttpClient Client { get; }
        public IServiceProvider Services => _app.Services;

        public static async Task<PreviewNodeTestHost> CreateAsync(
            Action<IFlowExecutionService> configureFlowExecution,
            Action<IProjectRepository>? configureProjectRepository = null,
            Action<IProjectFlowStorage>? configureFlowStorage = null,
            bool enrichDraftAuthority = true)
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                EnvironmentName = Environments.Development
            });

            builder.WebHost.UseTestServer();
            builder.Services.AddLogging(logging => logging.ClearProviders());

            var flowExecution = Substitute.For<IFlowExecutionService>();
            var projectRepository = Substitute.For<IProjectRepository>();
            var flowStorage = Substitute.For<IProjectFlowStorage>();
            var registry = new ProjectVariableSessionRegistry();
            projectRepository.GetByIdFreshAsync(Arg.Any<Guid>()).Returns(new Project("active-preview-project"));
            configureFlowExecution(flowExecution);
            configureProjectRepository?.Invoke(projectRepository);
            configureFlowStorage?.Invoke(flowStorage);

            builder.Services.AddSingleton(flowExecution);
            builder.Services.AddSingleton(projectRepository);
            builder.Services.AddSingleton<IExecutionAdmissionService>(new ExecutionAdmissionService(projectRepository));
            builder.Services.AddSingleton(flowStorage);
            builder.Services.AddSingleton(registry);
            builder.Services.AddPreviewArtifactServices();

            var app = builder.Build();
            UseTestUserIdentity(app, projectRepository, flowStorage, enrichDraftAuthority);
            app.MapPreviewNodeEndpoints();
            app.MapPreviewArtifactEndpoints();
            await app.StartAsync();

            return new PreviewNodeTestHost(app, app.GetTestClient());
        }

        public static async Task<PreviewNodeTestHost> CreateWithRealFlowExecutionAsync(
            Project project,
            ProjectVariableSessionRegistry registry,
            IEnumerable<IOperatorExecutor> executors,
            ProjectVariableExecutionContextAccessor? accessor = null,
            IProjectRepository? projectRepository = null,
            IProjectFlowStorage? flowStorage = null,
            bool enrichDraftAuthority = true)
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                EnvironmentName = Environments.Development
            });

            builder.WebHost.UseTestServer();
            builder.Services.AddLogging(logging => logging.ClearProviders());
            accessor ??= new ProjectVariableExecutionContextAccessor();
            projectRepository ??= Substitute.For<IProjectRepository>();
            flowStorage ??= Substitute.For<IProjectFlowStorage>();
            var flowExecution = new FlowExecutionService(
                executors,
                NullLogger<FlowExecutionService>.Instance,
                new VariableContext(),
                accessor);
            projectRepository.GetByIdAsync(project.Id).Returns(project);
            projectRepository.GetByIdFreshAsync(project.Id).Returns(project);
            projectRepository.GetWithFlowAsync(project.Id).Returns(project);

            builder.Services.AddSingleton<IFlowExecutionEngine>(flowExecution);
            builder.Services.AddSingleton<IFlowExecutionService>(new GovernedFlowExecutionService(flowExecution));
            builder.Services.AddSingleton(projectRepository);
            builder.Services.AddSingleton<IExecutionAdmissionService>(new ExecutionAdmissionService(projectRepository));
            builder.Services.AddSingleton(flowStorage);
            builder.Services.AddSingleton(registry);
            builder.Services.AddPreviewArtifactServices();

            var app = builder.Build();
            UseTestUserIdentity(app, projectRepository, flowStorage, enrichDraftAuthority);
            app.MapPreviewNodeEndpoints();
            app.MapPreviewArtifactEndpoints();
            await app.StartAsync();

            return new PreviewNodeTestHost(app, app.GetTestClient());
        }

        private static void UseTestUserIdentity(
            WebApplication app,
            IProjectRepository projectRepository,
            IProjectFlowStorage flowStorage,
            bool enrichDraftAuthority)
        {
            app.Use(async (context, next) =>
            {
                var userId = context.Request.Headers["X-Test-User"].FirstOrDefault();
                if (string.IsNullOrWhiteSpace(userId))
                {
                    userId = ArtifactOwnerTestUserId;
                }

                context.User = new ClaimsPrincipal(new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, userId)
                ], "PreviewNodeTest"));
                context.Items["CurrentUser"] = new ClearVision.Product.Desktop.Middleware.UserSession
                {
                    UserId = userId,
                    Username = "Preview Test Engineer",
                    Role = "Engineer"
                };

                if (enrichDraftAuthority &&
                    HttpMethods.IsPost(context.Request.Method) &&
                    context.Request.Path == "/api/flows/preview-node")
                {
                    var enrichedBody = await CreateEnrichedRequestBodyAsync(
                        context,
                        projectRepository,
                        flowStorage);
                    if (enrichedBody != null)
                    {
                        await using (enrichedBody)
                        {
                            context.Request.Body = enrichedBody;
                            context.Request.ContentLength = enrichedBody.Length;
                            await next();
                        }

                        return;
                    }
                }

                await next();
            });
        }

        private static async Task<MemoryStream?> CreateEnrichedRequestBodyAsync(
            HttpContext context,
            IProjectRepository projectRepository,
            IProjectFlowStorage flowStorage)
        {
            context.Request.EnableBuffering();
            using var reader = new StreamReader(
                context.Request.Body,
                Encoding.UTF8,
                detectEncodingFromByteOrderMarks: false,
                leaveOpen: true);
            var payload = await reader.ReadToEndAsync(context.RequestAborted);
            context.Request.Body.Position = 0;
            if (string.IsNullOrWhiteSpace(payload))
            {
                return null;
            }

            PreviewNodeRequest? request;
            try
            {
                request = JsonSerializer.Deserialize<PreviewNodeRequest>(payload, RequestJsonOptions);
            }
            catch (JsonException)
            {
                return null;
            }

            if (request == null)
            {
                return null;
            }

            var project = await projectRepository.GetByIdFreshAsync(request.ProjectId);
            if (project == null)
            {
                return null;
            }

            request.ExpectedProjectRevision = project.PersistenceRevision;
            request.ConfirmationId = Guid.NewGuid().ToString("D");
            request.AuditId = Guid.NewGuid().ToString("D");
            OperatorFlow? requestedFlow;
            try
            {
                requestedFlow = await ResolveRequestedFlowAsync(request, projectRepository, flowStorage);
            }
            catch
            {
                requestedFlow = null;
            }
            request.CapabilityManifest = requestedFlow == null
                ? new List<string>()
                : ExpandCapabilities(ExecutionCapabilityManifest.Derive(requestedFlow).Capabilities);

            return new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(request, RequestJsonOptions));
        }

        private static async Task<OperatorFlow?> ResolveRequestedFlowAsync(
            PreviewNodeRequest request,
            IProjectRepository projectRepository,
            IProjectFlowStorage flowStorage)
        {
            OperatorFlow? flow;
            if (request.FlowData is { Operators.Count: > 0 } inlineFlow)
            {
                flow = FlowEntityMapper.ToPreviewEntity(inlineFlow, request.TargetNodeId, "PreviewFlow");
            }
            else
            {
                var project = await projectRepository.GetWithFlowAsync(request.ProjectId);
                if (project == null)
                {
                    return null;
                }

                flow = null;
                try
                {
                    var storedJson = await flowStorage.LoadFlowJsonAsync(request.ProjectId);
                    var storedDto = string.IsNullOrWhiteSpace(storedJson)
                        ? null
                        : JsonSerializer.Deserialize<OperatorFlowDto>(storedJson, StoredFlowJsonOptions);
                    if (storedDto?.Operators?.Count > 0)
                    {
                        flow = storedDto.ToEntity();
                    }
                }
                catch
                {
                }

                if (flow == null && project.Flow?.Operators?.Count > 0)
                {
                    flow = ExecutionFlowIdentity.CloneFlow(project.Flow);
                }
            }

            if (flow != null && request.Parameters is { Count: > 0 })
            {
                var target = flow.Operators.FirstOrDefault(item => item.Id == request.TargetNodeId);
                if (target != null)
                {
                    foreach (var (name, value) in request.Parameters)
                    {
                        target.Parameters.FirstOrDefault(parameter => parameter.Name == name)?.SetValue(value);
                    }
                }
            }

            return flow;
        }

        private static List<string> ExpandCapabilities(ExecutionSideEffect capabilities) =>
            Enum.GetValues<ExecutionSideEffect>()
                .Where(value => value != ExecutionSideEffect.None && capabilities.HasFlag(value))
                .Select(value => value.ToString())
                .ToList();

        private static readonly JsonSerializerOptions RequestJsonOptions = new(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = true
        };

        private static readonly JsonSerializerOptions StoredFlowJsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
        };

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }

    private sealed class CountingOperatorExecutor : IOperatorExecutor
    {
        private readonly IOperatorExecutor _inner;

        public CountingOperatorExecutor(IOperatorExecutor inner)
        {
            _inner = inner;
        }

        public OperatorType OperatorType => _inner.OperatorType;

        public int ExecuteCount { get; private set; }
        public Operator? LastExecutedOperator { get; private set; }

        public Task<OperatorExecutionOutput> ExecuteAsync(
            Operator @operator,
            Dictionary<string, object>? inputs = null,
            CancellationToken cancellationToken = default)
        {
            ExecuteCount++;
            LastExecutedOperator = @operator;
            return _inner.ExecuteAsync(@operator, inputs, cancellationToken);
        }

        public ValidationResult ValidateParameters(Operator @operator)
        {
            return _inner.ValidateParameters(@operator);
        }
    }

    private sealed class FixedOutputOperatorExecutor : IOperatorExecutor
    {
        private readonly Dictionary<string, object> _output;

        public FixedOutputOperatorExecutor(OperatorType operatorType, Dictionary<string, object> output)
        {
            OperatorType = operatorType;
            _output = output;
        }

        public OperatorType OperatorType { get; }

        public Task<OperatorExecutionOutput> ExecuteAsync(
            Operator @operator,
            Dictionary<string, object>? inputs = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(OperatorExecutionOutput.Success(new Dictionary<string, object>(_output, StringComparer.OrdinalIgnoreCase)));

        public ValidationResult ValidateParameters(Operator @operator) => ValidationResult.Valid();
    }
}
