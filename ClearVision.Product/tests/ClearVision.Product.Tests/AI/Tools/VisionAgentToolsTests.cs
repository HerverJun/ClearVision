using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ClearVision.Product.Core.AI.Tools;
using ClearVision.Product.Core.DTOs;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Services;
using ClearVision.Product.Core.Cameras;
using ClearVision.Product.Infrastructure.AI.Tools;
using ClearVision.Product.Infrastructure.AI.DryRun;
using ClearVision.Product.Infrastructure.AI;
using ClearVision.Product.Infrastructure.AI.Runtime;
using ClearVision.Product.Application.DTOs;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace ClearVision.Product.Tests.AI.Tools;

public sealed class VisionAgentToolsTests
{
    private readonly IAiFlowValidator _validator = Substitute.For<IAiFlowValidator>();
    private readonly ITemplateConstraintValidator _templateConstraintValidator = Substitute.For<ITemplateConstraintValidator>();
    private readonly IFlowTemplateService _templateService = Substitute.For<IFlowTemplateService>();
    private readonly ICameraManager _cameraManager = Substitute.For<ICameraManager>();
    private readonly IOperatorFactory _operatorFactory = Substitute.For<IOperatorFactory>();
    private readonly IFlowExecutionService _flowExecutionService = Substitute.For<IFlowExecutionService>();
    private readonly IOperatorKnowledgeRetriever _knowledgeRetriever = Substitute.For<IOperatorKnowledgeRetriever>();
    private readonly IScenarioMatcher _scenarioMatcher = Substitute.For<IScenarioMatcher>();
    private readonly IServiceProvider _serviceProvider = Substitute.For<IServiceProvider>();

    private readonly DryRunService _dryRunService;

    public VisionAgentToolsTests()
    {
        _dryRunService = new DryRunService(_flowExecutionService);

        // Setup OperatorFactory default mocks to avoid null refs
        _operatorFactory.GetMetadata(Arg.Any<OperatorType>()).Returns(new OperatorMetadata
        {
            Type = OperatorType.ImageAcquisition,
            DisplayName = "图像采集",
            InputPorts = new(),
            OutputPorts = new() { new PortDefinition { Name = "Image", DataType = PortDataType.Image } },
            Parameters = new()
            {
                new ParameterDefinition { Name = "SourceType", DataType = "enum", DefaultValue = "Camera" },
                new ParameterDefinition { Name = "CameraId", DataType = "string" }
            }
        });
        
        var mockOp = new Operator("MockOp", OperatorType.ImageAcquisition, 0.0, 0.0);
        _operatorFactory.CreateOperator(Arg.Any<OperatorType>(), Arg.Any<string>(), Arg.Any<double>(), Arg.Any<double>())
            .Returns(mockOp);
    }

    [Fact(DisplayName = "inspect_current_flow should return correct summary of current workflow")]
    public async Task CurrentFlowInspectTool_Execute_ShouldSucceed()
    {
        var tool = new CurrentFlowInspectTool();
        var context = new VisionAgentToolContext
        {
            ExistingFlowJson = @"{
                ""operators"": [
                    { ""id"": ""op_1"", ""operatorType"": ""ImageAcquisition"", ""displayName"": ""采图"" },
                    { ""id"": ""op_2"", ""operatorType"": ""ResultJudgment"", ""displayName"": ""判定"" }
                ],
                ""connections"": [
                    { ""sourceId"": ""op_1"", ""sourcePortName"": ""Image"", ""targetId"": ""op_2"", ""targetPortName"": ""Value"" }
                ]
            }"
        };

        var result = await tool.ExecuteAsync(context, JsonDocument.Parse("{}").RootElement, CancellationToken.None);
        result.Success.Should().BeTrue();
        result.Summary.As<string>().Should().Contain("2 operators");
        result.Summary.As<string>().Should().Contain("1 connections");
    }

    [Fact(DisplayName = "validate_flow should return validation errors if invalid")]
    public async Task FlowValidationTool_Execute_ShouldValidateSuccessfully()
    {
        var flowJson = new AiGeneratedFlowJson
        {
            Operators = new() { new AiGeneratedOperator { TempId = "op_1", OperatorType = "ImageAcquisition", DisplayName = "采图" } }
        };

        var valResult = new AiValidationResult();
        valResult.AddError("Missing required parameters", "MISSING_PARAM", "Validation");
        _validator.Validate(Arg.Any<AiGeneratedFlowJson>()).Returns(valResult);

        var tool = new FlowValidationTool(_validator, _templateConstraintValidator, _templateService);
        var context = new VisionAgentToolContext();
        var arguments = JsonDocument.Parse("{\"flow\": {\"operators\": [{\"tempId\": \"op_1\", \"operatorType\": \"ImageAcquisition\"}]}}").RootElement;

        var result = await tool.ExecuteAsync(context, arguments, CancellationToken.None);
        result.Success.Should().BeTrue();
        result.Summary.As<string>().Should().Contain("Validation failed with 1 errors");
    }

    [Fact(DisplayName = "dryrun_flow should execute dry run successfully")]
    public async Task DryRunFlowTool_Execute_ShouldSucceed()
    {
        var flowExecutionResult = new FlowExecutionResult
        {
            IsSuccess = true,
            OperatorResults = new() { new OperatorExecutionResult { OperatorName = "ImageAcquisition", IsSuccess = true } }
        };

        _flowExecutionService.ExecuteFlowAsync(Arg.Any<OperatorFlow>(), Arg.Any<Dictionary<string, object>>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(flowExecutionResult);

        var tool = new DryRunFlowTool(_dryRunService, _operatorFactory);
        var context = new VisionAgentToolContext();
        var arguments = JsonDocument.Parse("{\"flow\": {\"operators\": [{\"tempId\": \"op_1\", \"operatorType\": \"ImageAcquisition\", \"displayName\": \"采集\", \"parameters\": {}}]}}").RootElement;

        var result = await tool.ExecuteAsync(context, arguments, CancellationToken.None);
        result.Success.Should().BeTrue();
        result.Summary.As<string>().Should().Contain("DryRun passed");
    }

    [Fact(DisplayName = "replay_flow_with_frame should read from frame cache and execute replay")]
    public async Task ReplayFlowWithFrameTool_Execute_ShouldSucceed()
    {
        var frameId = "test-frame-123";
        TemporaryFrameCache.Add(frameId, new byte[] { 1, 2, 3, 4 }, 640, 480, "png", TimeSpan.FromMinutes(5));

        var flowExecutionResult = new FlowExecutionResult
        {
            IsSuccess = true,
            OperatorResults = new() { new OperatorExecutionResult { OperatorName = "ImageAcquisition", IsSuccess = true } }
        };

        _flowExecutionService.ExecuteFlowAsync(Arg.Any<OperatorFlow>(), Arg.Any<Dictionary<string, object>>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(flowExecutionResult);

        var tool = new ReplayFlowWithFrameTool(_dryRunService, _operatorFactory);
        var context = new VisionAgentToolContext();
        var arguments = JsonDocument.Parse("{\"temporaryFrameId\": \"" + frameId + "\", \"flow\": {\"operators\": [{\"tempId\": \"op_1\", \"operatorType\": \"ImageAcquisition\", \"displayName\": \"采集\", \"parameters\": {}}]}}").RootElement;

        var result = await tool.ExecuteAsync(context, arguments, CancellationToken.None);
        result.Success.Should().BeTrue();
        result.Summary.As<string>().Should().Contain("Replay validation succeeded");
    }

    [Fact(DisplayName = "list_camera_bindings should return camera list")]
    public async Task CameraBindingsTool_Execute_ShouldReturnBindings()
    {
        var bindingConfig = new CameraBindingConfig
        {
            Id = "cam-1",
            DisplayName = "MainCamera",
            Manufacturer = "Hikvision",
            SerialNumber = "SN-12345",
            ModelName = "MV-123",
            InterfaceType = "GigE",
            TriggerMode = "Software",
            PixelFormat = "Mono8"
        };
        _cameraManager.GetBindings().Returns(new List<CameraBindingConfig> { bindingConfig });

        var tool = new CameraBindingsTool(_cameraManager);
        var result = await tool.ExecuteAsync(new VisionAgentToolContext(), JsonDocument.Parse("{}").RootElement, CancellationToken.None);
        result.Success.Should().BeTrue();
        result.Summary.As<string>().Should().Contain("Retrieved 1 camera bindings");
    }

    [Fact(DisplayName = "discover_cameras should list network cameras")]
    public async Task CameraDiscoveryTool_Execute_ShouldReturnDevices()
    {
        var cameraInfo = new CameraInfo
        {
            CameraId = "SN-54321",
            Manufacturer = "Huaray",
            Model = "HR-ABC",
            ConnectionType = "USB3",
            Name = "Huaray Camera"
        };
        _cameraManager.EnumerateCamerasAsync().Returns(new List<CameraInfo> { cameraInfo });

        var tool = new CameraDiscoveryTool(_cameraManager);
        var result = await tool.ExecuteAsync(new VisionAgentToolContext(), JsonDocument.Parse("{}").RootElement, CancellationToken.None);
        result.Success.Should().BeTrue();
        result.Summary.As<string>().Should().Contain("Discovered 1 camera devices");
    }

    [Fact(DisplayName = "capture_test_frame should trigger capture and return temporary frame ID")]
    public async Task CameraTestFrameTool_Execute_ShouldAcquireFrame()
    {
        var mockCamera = Substitute.For<ICamera>();
        mockCamera.AcquireSingleFrameAsync().Returns(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }); // mock png header
        _cameraManager.GetOrCreateByBindingAsync("cam-1").Returns(mockCamera);

        var tool = new CameraTestFrameTool(_cameraManager);
        var arguments = JsonDocument.Parse("{\"cameraBindingId\": \"cam-1\"}").RootElement;
        
        var result = await tool.ExecuteAsync(new VisionAgentToolContext(), arguments, CancellationToken.None);
        result.Success.Should().BeTrue();
        result.Summary.As<string>().Should().Contain("temporaryFrameId: agent-frame-");
    }

    [Fact(DisplayName = "draft_camera_binding should output draft successfully")]
    public async Task CameraBindingDraftTool_Execute_ShouldCreateDraft()
    {
        var tool = new CameraBindingDraftTool();
        var arguments = JsonDocument.Parse(@"{
            ""device"": {
                ""serialNumber"": ""SN-99999"",
                ""manufacturer"": ""Huaray"",
                ""modelName"": ""HR-XYZ"",
                ""interfaceType"": ""GigE""
            },
            ""suggestedDisplayName"": ""工位侧相机""
        }").RootElement;

        var result = await tool.ExecuteAsync(new VisionAgentToolContext(), arguments, CancellationToken.None);
        result.Success.Should().BeTrue();
        result.Summary.As<string>().Should().Contain("Draft camera binding prepared");
    }

    [Fact(DisplayName = "runtime_package_precheck should validate deployment prerequisites")]
    public async Task RuntimePackagePrecheckTool_Execute_ShouldPrecheckSuccessfully()
    {
        _validator.Validate(Arg.Any<AiGeneratedFlowJson>()).Returns(new AiValidationResult());
        _cameraManager.GetBindings().Returns(new List<CameraBindingConfig>
        {
            new CameraBindingConfig { Id = "cam-main" }
        });

        var tool = new RuntimePackagePrecheckTool(_validator, _cameraManager);
        var arguments = JsonDocument.Parse(@"{
            ""targetStationId"": ""station-1"",
            ""flow"": {
                ""operators"": [
                    {
                        ""tempId"": ""op_1"",
                        ""operatorType"": ""ImageAcquisition"",
                        ""displayName"": ""主相机"",
                        ""parameters"": {
                            ""SourceType"": ""Camera"",
                            ""CameraId"": ""cam-main""
                        }
                    }
                ],
                ""connections"": []
            }
        }").RootElement;

        var result = await tool.ExecuteAsync(new VisionAgentToolContext(), arguments, CancellationToken.None);
        result.Success.Should().BeTrue();
        result.Summary.As<string>().Should().Contain("Precheck passed");
    }

    [Fact(DisplayName = "draft_runtime_package_manifest should output package manifest successfully")]
    public async Task RuntimePackageManifestDraftTool_Execute_ShouldDraftManifest()
    {
        var tool = new RuntimePackageManifestDraftTool();
        var arguments = JsonDocument.Parse(@"{
            ""flow"": {
                ""operators"": [
                    { ""tempId"": ""op_dl"", ""operatorType"": ""DeepLearning"", ""displayName"": ""缺陷检测"", ""parameters"": { ""ModelPath"": ""C:\\models\\defect.onnx"" } }
                ]
            }
        }").RootElement;

        var result = await tool.ExecuteAsync(new VisionAgentToolContext(), arguments, CancellationToken.None);
        result.Success.Should().BeTrue();
        result.Summary.As<string>().Should().Contain("Draft deployment manifest created");
    }

    [Fact(DisplayName = "retrieve_operator_knowledge should query knowledge cards")]
    public async Task OperatorKnowledgeTool_Execute_ShouldSucceed()
    {
        var slice = new OperatorKnowledgeSlice
        {
            RetrievalSummary = "Match on template matching",
            Cards = new() { new OperatorKnowledgeCard { OperatorType = "TemplateMatching", DisplayName = "Template Matching Guide" } }
        };
        _knowledgeRetriever.RetrieveAsync(Arg.Any<OperatorKnowledgeQuery>(), Arg.Any<CancellationToken>()).Returns(slice);

        var tool = new OperatorKnowledgeTool(_knowledgeRetriever);
        var arguments = JsonDocument.Parse("{\"description\": \"template matching\"}").RootElement;

        var result = await tool.ExecuteAsync(new VisionAgentToolContext(), arguments, CancellationToken.None);
        result.Success.Should().BeTrue();
        result.Summary.As<string>().Should().Contain("Retrieved 1 operator knowledge cards");
    }

    [Fact(DisplayName = "match_flow_template should match templates")]
    public async Task FlowTemplateMatchTool_Execute_ShouldSucceed()
    {
        var scenario = new ScenarioDefinition
        {
            ScenarioKey = "wire-sequence",
            TemplateName = "端子线序检测",
            TemplateId = "temp-guid"
        };
        _scenarioMatcher.MatchAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<List<string>>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new List<ScenarioMatchResult> { new ScenarioMatchResult { Scenario = scenario, Confidence = 0.95 } });

        var tool = new FlowTemplateMatchTool(_scenarioMatcher);
        var arguments = JsonDocument.Parse("{\"description\": \"wire sequence detection\"}").RootElement;

        var result = await tool.ExecuteAsync(new VisionAgentToolContext(), arguments, CancellationToken.None);
        result.Success.Should().BeTrue();
        result.Summary.As<string>().Should().Contain("Matched template '端子线序检测'");
    }

    [Fact(DisplayName = "get_flow_template_skeleton should fetch skeletons")]
    public async Task FlowTemplateSkeletonTool_Execute_ShouldSucceed()
    {
        var template = new FlowTemplate
        {
            Id = Guid.NewGuid(),
            Name = "AirCon inspection",
            ScenarioKey = "aircon",
            FlowJson = "{\"operators\":[]}"
        };

        _templateService.GetTemplateAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(template);

        var tool = new FlowTemplateSkeletonTool(_templateService);
        var arguments = JsonDocument.Parse("{\"templateId\": \"" + template.Id.ToString() + "\"}").RootElement;

        var result = await tool.ExecuteAsync(new VisionAgentToolContext(), arguments, CancellationToken.None);
        result.Success.Should().BeTrue();
        result.Summary.As<string>().Should().Contain("Successfully retrieved skeleton for template");
    }
}
