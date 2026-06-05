using System.Text.Json;
using FluentAssertions;

namespace ClearVision.Product.Tests.AI.AgentEvaluation;

public sealed class AgentEngineeringEvaluationHarnessTests
{
    public static IEnumerable<object[]> Cases =>
        AgentEngineeringEvaluationCases.All.Select(item => new object[] { item });

    [Theory(DisplayName = "Agent engineering evaluation case should match expected behavior")]
    [MemberData(nameof(Cases))]
    public async Task AgentEngineeringEvaluationCase_ShouldMatchExpectedBehavior(
        AgentEngineeringEvaluationCase evaluationCase)
    {
        var harness = new AgentEvaluationHarness();

        var result = await harness.RunAsync(evaluationCase);

        evaluationCase.CaseId.Should().NotBeNullOrWhiteSpace();
        evaluationCase.UserRequest.Should().NotBeNullOrWhiteSpace();
        evaluationCase.MockToolResponses.Should().NotBeEmpty();
        evaluationCase.MockToolResponses.Should().OnlyContain(response =>
            !string.IsNullOrWhiteSpace(response.ToolName));
        evaluationCase.ExpectedToolCalls.Should().NotBeEmpty();
        evaluationCase.ExpectedPassFailReason.Should().NotBeNullOrWhiteSpace();

        result.CaseId.Should().Be(evaluationCase.CaseId);
        result.ActualToolCalls.Select(item => item.ToolName)
            .Should()
            .Equal(evaluationCase.ExpectedToolCalls);
        result.ActualToolCalls
            .Where(item => item.ExecutedByMock)
            .Should()
            .OnlyContain(item => item.MockSource == AgentEvaluationHarness.MockSource);
        result.ActualFlowStructure.Should().BeEquivalentTo(
            evaluationCase.ExpectedFlowStructure,
            options => options.WithStrictOrdering());
        result.ActualPendingActions.Should().Equal(evaluationCase.ExpectedPendingActions);
        result.ActualValidationPreview.Should().BeEquivalentTo(
            evaluationCase.ExpectedValidationPreview,
            options => options.WithStrictOrdering());
        result.ActualPermissionDecision.Should().BeEquivalentTo(
            evaluationCase.ExpectedPermissionBehavior,
            options => options.WithStrictOrdering());
        result.ActualBlockingIssues.Should().Equal(evaluationCase.ExpectedBlockingIssues);
        result.Passed.Should().Be(evaluationCase.ExpectedPassed);

        if (evaluationCase.ExpectedPassed)
        {
            result.PassReason.Should().Be(evaluationCase.ExpectedPassFailReason);
            result.FailReason.Should().BeNull();
        }
        else
        {
            result.FailReason.Should().Be(evaluationCase.ExpectedPassFailReason);
        }
    }

    [Fact(DisplayName = "Agent engineering harness should cover v0.1 scenario set")]
    public void AgentEngineeringHarness_ShouldCoverRequiredV01Scenarios()
    {
        AgentEngineeringEvaluationCases.All.Select(item => item.CaseId)
            .Should()
            .Equal(
                "wire_sequence_flow_generation",
                "template_matching_flow_generation",
                "hole_distance_measurement_flow_generation",
                "missing_camera_binding",
                "missing_model_path",
                "station_offline",
                "multiple_image_acquisition_requires_entry",
                "runtime_preview_denies_capture_by_default",
                "runtime_preview_authorized_replay",
                "precheck_blocks_camera_flow_without_replay");
        AgentEngineeringEvaluationCases.All.Should().HaveCount(10);
    }

    [Fact(DisplayName = "Agent engineering harness should keep runtime boundaries mocked and command tools absent")]
    public void AgentEngineeringHarness_ShouldKeepRuntimeBoundariesMockedAndCommandToolsAbsent()
    {
        var forbiddenToolNameFragments = new[]
        {
            "shell",
            "cmd",
            "powershell",
            "terminal",
            "execute_command"
        };
        var runtimeBoundaryTools = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "capture_test_frame",
            "replay_flow_with_frame",
            "runtime_package_precheck",
            "check_station_status"
        };

        var allToolNames = AgentEngineeringEvaluationCases.All
            .SelectMany(item => item.ToolCalls.Select(call => call.ToolName)
                .Concat(item.MockToolResponses.Select(response => response.ToolName)))
            .ToList();
        allToolNames.Should().OnlyContain(toolName =>
            forbiddenToolNameFragments.All(fragment =>
                !toolName.Contains(fragment, StringComparison.OrdinalIgnoreCase)));

        AgentEngineeringEvaluationCases.All
            .SelectMany(item => item.MockToolResponses)
            .Where(response => runtimeBoundaryTools.Contains(response.ToolName))
            .Should()
            .OnlyContain(response =>
                response.Data != null ||
                !string.IsNullOrWhiteSpace(response.ErrorCode));
    }

    [Fact(DisplayName = "AgentEvaluation harness should be self-contained and decoupled from production Agent types")]
    public void AgentEngineeringHarness_ShouldBeSelfContainedAndDecoupledFromProductionAgentTypes()
    {
        var source = ReadAgentEvaluationSource();

        source.Should().NotContain("ClearVision.Product.Core.AI.Tools");
        source.Should().NotContain("ClearVision.Product.Infrastructure.AI.Tools");
        source.Should().NotContain("IVisionAgentTool");
        source.Should().NotContain("VisionAgentToolRegistry");
        source.Should().NotContain("VisionAgentLoop");
    }

    [Fact(DisplayName = "AgentEvaluation cases should not use real images, hardware, models, stations, or network")]
    public void AgentEngineeringHarness_ShouldAvoidRealRuntimeResources()
    {
        var serializedCases = JsonSerializer.Serialize(AgentEngineeringEvaluationCases.All);
        var source = ReadAgentEvaluationSource();
        var forbiddenImagePathFragments = new[]
        {
            ".jpg",
            ".jpeg",
            ".png",
            ".bmp",
            ".tif",
            ".tiff",
            "C:\\",
            "file://"
        };
        var forbiddenRuntimeAccessApis = new[]
        {
            "AcquireSingleFrameAsync",
            "GetOrCreateByBindingAsync",
            "EnumerateCamerasAsync",
            "HttpClient",
            "Socket",
            "TcpClient",
            "File.ReadAllBytes",
            "Image.FromFile",
            "Cv2.ImRead",
            "ProcessStartInfo",
            "Process.Start"
        };

        forbiddenImagePathFragments.Should().OnlyContain(fragment =>
            !serializedCases.Contains(fragment, StringComparison.OrdinalIgnoreCase));
        forbiddenRuntimeAccessApis.Should().OnlyContain(fragment =>
            !source.Contains(fragment, StringComparison.OrdinalIgnoreCase));
        serializedCases.Should().Contain("mock://models");
        serializedCases.Should().Contain("mock://templates");
    }

    [Fact(DisplayName = "Production AI mainline should keep Agent controlled and RuntimePreview tools gated")]
    public void ProductionAiMainline_ShouldKeepAgentControlledAndRuntimePreviewToolsGated()
    {
        var productRoot = GetProductRoot();
        var aiFlowGenerationService = File.ReadAllText(Path.Combine(
            productRoot,
            "src",
            "ClearVision.Product.Infrastructure",
            "AI",
            "AiFlowGenerationService.cs"));
        var serviceExtensions = File.ReadAllText(Path.Combine(
            productRoot,
            "src",
            "ClearVision.Product.Infrastructure",
            "AI",
            "AiGenerationServiceExtensions.cs"));
        var program = File.ReadAllText(Path.Combine(
            productRoot,
            "src",
            "ClearVision.Product.Desktop",
            "Program.cs"));

        aiFlowGenerationService.Should().NotContain("VisionAgentLoop");
        aiFlowGenerationService.Should().NotContain("BuildAgentAllowedPermissions");
        aiFlowGenerationService.Should().Contain("ShouldRunAgentGenerateFlow");
        aiFlowGenerationService.Should().Contain("request.UseVisionAgentGenerateFlow");
        serviceExtensions.Should().Contain("AgentGenerateFlowOptions");
        serviceExtensions.Should().Contain("IVisionAgentToolRegistry");
        serviceExtensions.Should().Contain("NoOpVisionAgentStationStatusReader");
        serviceExtensions.Should().Contain("RuntimePreviewCaptureStubTool");
        serviceExtensions.Should().Contain("RuntimePreviewReplayStubTool");
        serviceExtensions.Should().NotContain("ReplayFlowWithFrameTool");
        serviceExtensions.Should().NotContain("CameraTestFrameTool");
        program.Should().NotContain("VisionAgentStationStatusReader");
    }

    [Fact(DisplayName = "RuntimePreview default deny and explicit replay authorization should be distinguishable")]
    public async Task RuntimePreviewPermission_ShouldDefaultDenyCaptureAndAllowAuthorizedReplay()
    {
        var harness = new AgentEvaluationHarness();
        var denyCase = AgentEngineeringEvaluationCases.All.Single(item =>
            item.CaseId == "runtime_preview_denies_capture_by_default");
        var allowCase = AgentEngineeringEvaluationCases.All.Single(item =>
            item.CaseId == "runtime_preview_authorized_replay");

        var denied = await harness.RunAsync(denyCase);
        var allowed = await harness.RunAsync(allowCase);

        denied.ActualPermissionDecision.RuntimePreviewAllowed.Should().BeFalse();
        denied.ActualPermissionDecision.DeniedToolNames.Should().Equal("capture_test_frame");
        denied.ActualToolCalls.Single(item => item.ToolName == "capture_test_frame")
            .ExecutedByMock
            .Should()
            .BeFalse();

        allowed.ActualPermissionDecision.RuntimePreviewAllowed.Should().BeTrue();
        allowed.ActualPermissionDecision.DeniedToolNames.Should().BeEmpty();
        allowed.ActualPermissionDecision.RuntimePreviewExecutedToolNames
            .Should()
            .Equal("capture_test_frame", "replay_flow_with_frame");
        allowed.ActualValidationPreview.FrameReplayStatus.Should().Be("ok");
    }

    private static string ReadAgentEvaluationSource()
    {
        var agentEvaluationDirectory = Path.Combine(
            GetProductRoot(),
            "tests",
            "ClearVision.Product.Tests",
            "AI",
            "AgentEvaluation");

        return string.Join(
            Environment.NewLine,
            Directory.EnumerateFiles(agentEvaluationDirectory, "*.cs")
                .Where(path => !Path.GetFileName(path).EndsWith("Tests.cs", StringComparison.OrdinalIgnoreCase))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .Select(File.ReadAllText));
    }

    private static string GetProductRoot()
    {
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.."));
    }
}
