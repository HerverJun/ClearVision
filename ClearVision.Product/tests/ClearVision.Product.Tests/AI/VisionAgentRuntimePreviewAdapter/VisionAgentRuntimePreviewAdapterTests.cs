using System.Text.Json;
using ClearVision.Product.Core.AI.Tools;
using ClearVision.Product.Core.DTOs;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Infrastructure.AI;
using ClearVision.Product.Infrastructure.AI.Agent;
using ClearVision.Product.Infrastructure.AI.Tools;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace ClearVision.Product.Tests.AI.VisionAgentRuntimePreviewAdapter;

public sealed class VisionAgentRuntimePreviewAdapterTests
{
    private static readonly JsonSerializerOptions CamelCaseJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    [Fact(DisplayName = "RuntimePreviewPilotConfig should migrate old runtime config to safe defaults")]
    public void RuntimePreviewPilotConfig_ShouldMigrateOldRuntimeConfigToSafeDefaults()
    {
        var config = JsonSerializer.Deserialize<AppConfig>("""{"runtime":{}}""", CamelCaseJsonOptions)!;

        config.Normalize();

        config.Runtime.RuntimePreviewPilot.Enabled.Should().BeFalse();
        config.Runtime.RuntimePreviewPilot.Mode.Should().Be(RuntimePreviewPilotConfig.ModeMetadataOnly);
        config.Runtime.RuntimePreviewPilot.FallbackToOffline.Should().BeTrue();
        config.Runtime.RuntimePreviewPilot.DenyExternalPath.Should().BeTrue();
        config.Runtime.RuntimePreviewPilot.DenyImageBytes.Should().BeTrue();
        JsonSerializer.Serialize(config, CamelCaseJsonOptions).Should().Contain("runtimePreviewPilot");
    }

    [Fact(DisplayName = "RuntimePreviewPilotConfig validator should reject wildcard paths and disabled safety flags")]
    public void RuntimePreviewPilotConfigValidator_ShouldRejectUnsafeConfiguration()
    {
        var failures = RuntimePreviewPilotConfigValidator.Validate(new RuntimePreviewPilotConfig
        {
            Enabled = true,
            AllowedCameraBindingIds = ["*", "C:\\camera\\binding.json", "100.83.146.106:8317", "token-camera"],
            AllowedResourceRoots = ["../images", "https://example.invalid/v1"],
            DenyExternalPath = false,
            DenyImageBytes = false
        });

        failures.Should().Contain(item => item.Contains("AllowedCameraBindingIds", StringComparison.OrdinalIgnoreCase));
        failures.Should().Contain(item => item.Contains("AllowedResourceRoots", StringComparison.OrdinalIgnoreCase));
        failures.Should().Contain(item => item.Contains("DenyExternalPath", StringComparison.OrdinalIgnoreCase));
        failures.Should().Contain(item => item.Contains("DenyImageBytes", StringComparison.OrdinalIgnoreCase));
    }

    [Fact(DisplayName = "RuntimePreview Pilot catalog should expose only safe metadata and redact unsafe resource identifiers")]
    public void RuntimePreviewPilotCatalog_ShouldExposeOnlySafeRedactedMetadata()
    {
        var aiStore = CreateAiConfigStore();
        aiStore.Add(new AiModelConfig
        {
            Id = "model-a",
            Name = "Planner Model",
            Provider = "OpenAI Compatible",
            BaseUrl = "https://example.invalid/v1",
            ApiKey = "catalog-secret-key"
        });
        var appConfig = new AppConfig
        {
            Cameras =
            [
                new CameraBindingConfig
                {
                    Id = "cam-a",
                    DisplayName = "Line Camera",
                    IpAddress = "100.83.146.106"
                },
                new CameraBindingConfig
                {
                    Id = "100.83.146.106:8317",
                    DisplayName = "Unsafe Camera"
                }
            ]
        };
        var catalog = new RuntimePreviewPilotResourceCatalog().Build(
            PilotConfig(),
            appConfig,
            aiStore,
            Args(AllowlistedFlow()).Clone());

        catalog.Items.Should().Contain(item => item.ResourceType == "camera" && item.Id == "cam-a" && item.Source == "app_config");
        catalog.Items.Should().Contain(item => item.ResourceType == "model" && item.Id == "model-a" && item.Source == "ai_config_store");
        catalog.Items.Should().Contain(item => item.Redacted && item.Id == "<redacted>");
        var raw = Json(catalog).GetRawText();
        raw.Should().NotContain("catalog-secret-key");
        raw.Should().NotContain("100.83.146.106");
        raw.Should().NotContain("example.invalid/v1");
    }

    [Fact(DisplayName = "RuntimePreview Pilot readiness gate should return ready not_ready and denied states")]
    public void RuntimePreviewPilotReadinessGate_ShouldReturnThreeStates()
    {
        var catalogBuilder = new RuntimePreviewPilotResourceCatalog();
        var gate = new RuntimePreviewPilotReadinessGate(new RuntimePreviewResourceAllowlistResolver());
        var config = PilotConfig();
        var catalog = catalogBuilder.Build(config, new AppConfig(), null, Args(AllowlistedFlow()).Clone());

        var ready = gate.Evaluate(
            config,
            catalog,
            RuntimePreviewPermissionGate.ReplayToolName,
            Args(new { flow = AllowlistedFlow() }),
            RuntimePreviewContext(config));
        var disabled = gate.Evaluate(
            new RuntimePreviewPilotConfig(),
            catalog,
            RuntimePreviewPermissionGate.ReplayToolName,
            Args(new { flow = AllowlistedFlow() }),
            RuntimePreviewContext(new RuntimePreviewPilotConfig()));
        var denied = gate.Evaluate(
            config,
            catalog,
            RuntimePreviewPermissionGate.ReplayToolName,
            Args(new { flow = AllowlistedFlow(templatePath: "C:\\secret\\template.png") }),
            RuntimePreviewContext(config));

        ready.Status.Should().Be(RuntimePreviewPilotReadinessStatuses.Ready);
        ready.CanRunMetadataPilot.Should().BeTrue();
        disabled.Status.Should().Be(RuntimePreviewPilotReadinessStatuses.NotReady);
        disabled.WorkflowDraftAllowed.Should().BeTrue();
        disabled.PendingActions.Should().NotBeEmpty();
        denied.Status.Should().Be(RuntimePreviewPilotReadinessStatuses.Denied);
        denied.CanRunMetadataPilot.Should().BeFalse();
        denied.UnsafeFindings.Should().NotBeEmpty();
        denied.Fallback.Used.Should().BeFalse();
        Json(denied).GetRawText().Should().NotContain("secret");
    }

    [Fact(DisplayName = "RuntimePreview allowlist resolver should allow camera capture only when binding is allowlisted")]
    public void RuntimePreviewAllowlistResolver_ShouldAllowCameraCaptureWhenAllowlisted()
    {
        var resolver = new RuntimePreviewResourceAllowlistResolver();
        var decision = resolver.Resolve(new RuntimePreviewRequest
        {
            ToolName = RuntimePreviewPermissionGate.CaptureToolName,
            Context = RuntimePreviewContext(PilotConfig()),
            Arguments = Args(new { cameraBindingId = "CAM-A" })
        });

        decision.Allowed.Should().BeTrue();
        decision.ResourceType.Should().Be("camera");
        decision.NormalizedKey.Should().Be("cam-a");
        decision.ReasonCode.Should().Be("runtime_preview_resource_allowlisted");
    }

    [Fact(DisplayName = "RuntimePreview allowlist resolver should allow logical resource roots when allowlisted")]
    public void RuntimePreviewAllowlistResolver_ShouldAllowLogicalResourceRootWhenAllowlisted()
    {
        var resolver = new RuntimePreviewResourceAllowlistResolver();
        var decision = resolver.Resolve(new RuntimePreviewRequest
        {
            ToolName = RuntimePreviewPermissionGate.ReplayToolName,
            Context = RuntimePreviewContext(PilotConfig()),
            Arguments = Args(new { resourceRootId = "catalog-a", flow = AllowlistedFlow() })
        });

        decision.Allowed.Should().BeTrue();
        Json(decision).GetRawText().Should().NotContain("catalog-a/");
    }

    [Fact(DisplayName = "RuntimePreview allowlist resolver should support metadata readiness tool name")]
    public void RuntimePreviewAllowlistResolver_ShouldSupportMetadataReadinessToolName()
    {
        var resolver = new RuntimePreviewResourceAllowlistResolver();
        var decision = resolver.Resolve(new RuntimePreviewRequest
        {
            ToolName = RuntimePreviewResourceAllowlistResolver.MetadataToolName,
            Context = RuntimePreviewContext(PilotConfig()),
            Arguments = Args(new { flow = AllowlistedFlow() })
        });

        decision.Allowed.Should().BeTrue();
        decision.ResourceType.Should().Be("workflow");
        decision.ReasonCode.Should().Be("runtime_preview_resources_allowlisted");
    }

    [Fact(DisplayName = "RuntimePreview allowlist resolver should deny empty allowlist unknown and missing resources")]
    public void RuntimePreviewAllowlistResolver_ShouldDenyMissingOrUnlistedResources()
    {
        var resolver = new RuntimePreviewResourceAllowlistResolver();
        var empty = new RuntimePreviewPilotConfig { Enabled = true };
        empty.Normalize();

        var emptyDecision = resolver.Resolve(new RuntimePreviewRequest
        {
            ToolName = RuntimePreviewPermissionGate.CaptureToolName,
            Context = RuntimePreviewContext(empty),
            Arguments = Args(new { cameraBindingId = "cam-a" })
        });
        var missingDecision = resolver.Resolve(new RuntimePreviewRequest
        {
            ToolName = RuntimePreviewPermissionGate.CaptureToolName,
            Context = RuntimePreviewContext(PilotConfig()),
            Arguments = Args(new { })
        });
        var unknownDecision = resolver.Resolve(new RuntimePreviewRequest
        {
            ToolName = "unknown_preview",
            Context = RuntimePreviewContext(PilotConfig()),
            Arguments = Args(new { stationId = "station-1" })
        });

        emptyDecision.Allowed.Should().BeFalse();
        emptyDecision.ReasonCode.Should().Be("runtime_preview_camera_allowlist_empty");
        missingDecision.ReasonCode.Should().Be("runtime_preview_camera_binding_missing");
        unknownDecision.Allowed.Should().BeFalse();
        unknownDecision.ReasonCode.Should().Contain("station");
    }

    [Theory(DisplayName = "RuntimePreview allowlist resolver should deny paths Station PLC and image bytes")]
    [InlineData("templatePath", "C:\\secret\\template.png", "runtime_preview_external_path_denied")]
    [InlineData("filePath", "..\\live\\frame.png", "runtime_preview_path_traversal_denied")]
    [InlineData("stationId", "station-1", "runtime_preview_station_denied")]
    [InlineData("plcAddress", "DB1.DBX0.0", "runtime_preview_plc_denied")]
    [InlineData("imageBase64", "data:image/png;base64,abcd", "runtime_preview_image_bytes_denied")]
    public void RuntimePreviewAllowlistResolver_ShouldDenyDangerousResources(
        string field,
        string value,
        string expectedReason)
    {
        var resolver = new RuntimePreviewResourceAllowlistResolver();
        var payload = new Dictionary<string, string> { [field] = value };

        var decision = resolver.Resolve(new RuntimePreviewRequest
        {
            ToolName = RuntimePreviewPermissionGate.ReplayToolName,
            Context = RuntimePreviewContext(PilotConfig()),
            Arguments = Args(payload)
        });

        decision.Allowed.Should().BeFalse();
        decision.ReasonCode.Should().Be(expectedReason);
        Json(decision).GetRawText().Should().NotContain("secret");
        Json(decision).GetRawText().Should().NotContain("base64");
    }

    [Fact(DisplayName = "RuntimePreview adapter tools should reject capture and replay without consent")]
    public async Task RuntimePreviewTools_ShouldRejectCaptureAndReplayWithoutConsent()
    {
        var registry = CreateRegistry();
        var context = new VisionAgentToolContext
        {
            AllowedPermissions = new HashSet<VisionAgentToolPermission>
            {
                VisionAgentToolPermission.RuntimePreview
            }
        };

        var capture = await registry.ExecuteAsync(
            RuntimePreviewPermissionGate.CaptureToolName,
            context,
            Args(new { cameraBindingId = "mock-cam" }),
            CancellationToken.None);
        var replay = await registry.ExecuteAsync(
            RuntimePreviewPermissionGate.ReplayToolName,
            context,
            Args(new { frameId = "offline-frame", flow = ValidFlow() }),
            CancellationToken.None);

        capture.Success.Should().BeFalse();
        replay.Success.Should().BeFalse();
        capture.ErrorCode.Should().Be(RuntimePreviewPermissionGate.ConsentRequiredErrorCode);
        replay.ErrorCode.Should().Be(RuntimePreviewPermissionGate.ConsentRequiredErrorCode);
        capture.PendingActions.Should().Contain(action => action.ActionType == "AuthorizeRuntimePreview");
        replay.PendingActions.Should().Contain(action => action.ActionType == "AuthorizeRuntimePreview");
    }

    [Fact(DisplayName = "Authorized capture should call OfflineRuntimePreviewAdapter")]
    public async Task AuthorizedCapture_ShouldCallOfflineAdapter()
    {
        var result = await CreateRegistry().ExecuteAsync(
            RuntimePreviewPermissionGate.CaptureToolName,
            RuntimePreviewContext(),
            Args(new { cameraBindingId = "mock-cam", operatorTempId = "op_cam" }),
            CancellationToken.None);

        result.Success.Should().BeTrue();
        var payload = Json(result.Data);
        payload.GetProperty("adapterName").GetString().Should().Be(OfflineRuntimePreviewAdapter.AdapterName);
        payload.GetProperty("previewMode").GetString().Should().Be(RuntimePreviewModes.OfflineFixture);
        payload.GetProperty("previewReady").GetBoolean().Should().BeTrue();
    }

    [Fact(DisplayName = "RuntimePreview default disabled should route to Offline adapter even with consent")]
    public async Task RuntimePreviewDefaultDisabled_ShouldRouteToOfflineAdapter()
    {
        var result = await CreatePilotRegistry().ExecuteAsync(
            RuntimePreviewPermissionGate.CaptureToolName,
            RuntimePreviewContext(),
            Args(new { cameraBindingId = "cam-a", operatorTempId = "op_cam" }),
            CancellationToken.None);

        result.Success.Should().BeTrue();
        var payload = Json(result.Data);
        payload.GetProperty("adapterName").GetString().Should().Be(OfflineRuntimePreviewAdapter.AdapterName);
        payload.GetProperty("permissionDecision").GetProperty("pilotEnabled").GetBoolean().Should().BeFalse();
    }

    [Fact(DisplayName = "Pilot RuntimePreview should return metadata-only result when resources are allowlisted")]
    public async Task PilotRuntimePreview_ShouldReturnMetadataOnlyWhenAllowlisted()
    {
        var result = await CreatePilotRegistry().ExecuteAsync(
            RuntimePreviewPermissionGate.ReplayToolName,
            RuntimePreviewContext(PilotConfig()),
            Args(new { frameId = "pilot-frame", flow = AllowlistedFlow() }),
            CancellationToken.None);

        result.Success.Should().BeTrue();
        var payload = Json(result.Data);
        payload.GetProperty("adapterName").GetString().Should().Be(PilotRuntimePreviewAdapter.AdapterName);
        payload.GetProperty("previewMode").GetString().Should().Be(RuntimePreviewModes.MetadataOnly);
        payload.GetProperty("permissionDecision").GetProperty("allowed").GetBoolean().Should().BeTrue();
        payload.GetProperty("resourceTrace").GetProperty("allowed").GetBoolean().Should().BeTrue();
        payload.GetProperty("binaryIncluded").GetBoolean().Should().BeFalse();
        payload.GetProperty("capturedRealFrame").GetBoolean().Should().BeFalse();
        payload.GetProperty("loadedModelFiles").GetBoolean().Should().BeFalse();
        payload.GetProperty("accessedHardware").GetBoolean().Should().BeFalse();
        payload.GetProperty("stationTouched").GetBoolean().Should().BeFalse();
        payload.GetRawText().ToLowerInvariant().Should().NotContain("base64");
    }

    [Fact(DisplayName = "Pilot RuntimePreview should deny allowlist miss with pending action and offline fallback metadata")]
    public async Task PilotRuntimePreview_ShouldDenyAllowlistMissWithPendingActionAndFallback()
    {
        var result = await CreatePilotRegistry().ExecuteAsync(
            RuntimePreviewPermissionGate.ReplayToolName,
            RuntimePreviewContext(PilotConfig()),
            Args(new { frameId = "pilot-frame", flow = AllowlistedFlow(cameraBindingId: "cam-missing") }),
            CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("runtime_preview_camera_not_allowlisted");
        result.PendingActions.Should().Contain(action => action.ActionType == "RuntimePreviewPilotReadinessReview");
        var payload = Json(result.Data);
        payload.GetProperty("workflowDraftAllowed").GetBoolean().Should().BeTrue();
        payload.GetProperty("readiness").GetProperty("status").GetString().Should().Be(RuntimePreviewPilotReadinessStatuses.NotReady);
        payload.GetProperty("readiness").GetProperty("workflowDraftAllowed").GetBoolean().Should().BeTrue();
        payload.GetProperty("fallback").GetProperty("used").GetBoolean().Should().BeTrue();
        payload.GetProperty("fallback").GetProperty("fallbackAdapterName").GetString()
            .Should().Be(OfflineRuntimePreviewAdapter.AdapterName);
        payload.GetProperty("resourceTrace").GetProperty("allowed").GetBoolean().Should().BeFalse();
    }

    [Fact(DisplayName = "Pilot RuntimePreview should deny external paths without leaking path fragments")]
    public async Task PilotRuntimePreview_ShouldDenyExternalPathWithoutLeakingFragments()
    {
        var flow = AllowlistedFlow(templatePath: "C:\\secret\\template.png");

        var result = await CreatePilotRegistry().ExecuteAsync(
            RuntimePreviewPermissionGate.ReplayToolName,
            RuntimePreviewContext(PilotConfig()),
            Args(new { frameId = "pilot-frame", flow }),
            CancellationToken.None);

        result.Success.Should().BeFalse();
        var raw = Json(result.Data).GetRawText().ToLowerInvariant();
        raw.Should().Contain("runtime_preview_external_path_denied");
        raw.Should().NotContain("secret");
        raw.Should().NotContain(".png");
        raw.Should().NotContain("base64");
        var payload = Json(result.Data);
        payload.GetProperty("readiness").GetProperty("status").GetString().Should().Be(RuntimePreviewPilotReadinessStatuses.Denied);
        payload.GetProperty("fallback").GetProperty("used").GetBoolean().Should().BeFalse();
        payload.GetProperty("artifacts").EnumerateArray().Should().BeEmpty();
    }

    [Fact(DisplayName = "Pilot RuntimePreview adapter exception should fallback Offline and keep workflow draft editable")]
    public async Task PilotRuntimePreviewAdapterException_ShouldFallbackOffline()
    {
        var offline = new OfflineRuntimePreviewAdapter(new RuntimePreviewArtifactStore());
        var registry = new VisionAgentToolRegistry(
        [
            new RuntimePreviewReplayStubTool(new RuntimePreviewAdapterRegistry(
            [
                offline,
                new ThrowingRuntimePreviewAdapter()
            ]))
        ]);

        var result = await registry.ExecuteAsync(
            RuntimePreviewPermissionGate.ReplayToolName,
            RuntimePreviewContext(PilotConfig()),
            Args(new { adapterName = ThrowingRuntimePreviewAdapter.AdapterName, frameId = "pilot-frame", flow = AllowlistedFlow() }),
            CancellationToken.None);

        result.Success.Should().BeTrue();
        var payload = Json(result.Data);
        payload.GetProperty("adapterName").GetString().Should().Be(OfflineRuntimePreviewAdapter.AdapterName);
        payload.GetProperty("fallback").GetProperty("used").GetBoolean().Should().BeTrue();
        payload.GetProperty("workflowDraftAllowed").GetBoolean().Should().BeTrue();
    }

    [Fact(DisplayName = "Pilot RuntimePreview structural failure should not block workflow draft editing")]
    public async Task PilotRuntimePreviewStructuralFailure_ShouldKeepWorkflowDraftAllowed()
    {
        var result = await CreatePilotRegistry().ExecuteAsync(
            RuntimePreviewPermissionGate.ReplayToolName,
            RuntimePreviewContext(PilotConfig()),
            Args(new { frameId = "pilot-frame", flow = BrokenAllowlistedFlow() }),
            CancellationToken.None);

        result.Success.Should().BeFalse();
        var payload = Json(result.Data);
        payload.GetProperty("adapterName").GetString().Should().Be(PilotRuntimePreviewAdapter.AdapterName);
        payload.GetProperty("workflowDraftAllowed").GetBoolean().Should().BeTrue();
        payload.GetProperty("binaryIncluded").GetBoolean().Should().BeFalse();
    }

    [Fact(DisplayName = "Authorized replay should call OfflineRuntimePreviewAdapter")]
    public async Task AuthorizedReplay_ShouldCallOfflineAdapter()
    {
        var result = await CreateRegistry().ExecuteAsync(
            RuntimePreviewPermissionGate.ReplayToolName,
            RuntimePreviewContext(),
            Args(new { frameId = "offline-frame", flow = ValidFlow() }),
            CancellationToken.None);

        result.Success.Should().BeTrue();
        var payload = Json(result.Data);
        payload.GetProperty("adapterName").GetString().Should().Be(OfflineRuntimePreviewAdapter.AdapterName);
        payload.GetProperty("previewReady").GetBoolean().Should().BeTrue();
        payload.GetProperty("replaySummary").GetProperty("adapterName").GetString().Should().Be(OfflineRuntimePreviewAdapter.AdapterName);
    }

    [Fact(DisplayName = "RuntimePreview adapter registry should expose offline adapter deterministically")]
    public void AdapterRegistry_ShouldExposeOfflineAdapterDeterministically()
    {
        var registry = CreateAdapterRegistry();

        registry.ListAdapterNames().Should().Contain(OfflineRuntimePreviewAdapter.AdapterName);
        registry.TryGet("OFFLINE_RUNTIME_PREVIEW", out var adapter).Should().BeTrue();
        adapter.Name.Should().Be(OfflineRuntimePreviewAdapter.AdapterName);
        adapter.SupportedToolNames.Should().Contain(RuntimePreviewPermissionGate.CaptureToolName);
        adapter.SupportedToolNames.Should().Contain(RuntimePreviewPermissionGate.ReplayToolName);
    }

    [Fact(DisplayName = "Offline capture should return metadata-only frame artifact")]
    public async Task OfflineCapture_ShouldReturnMetadataOnlyFrameArtifact()
    {
        var result = await CreateRegistry().ExecuteAsync(
            RuntimePreviewPermissionGate.CaptureToolName,
            RuntimePreviewContext(),
            Args(new { cameraBindingId = "mock-cam", operatorTempId = "op_cam" }),
            CancellationToken.None);

        var artifact = Json(result.Data)
            .GetProperty("artifacts")
            .EnumerateArray()
            .Single();
        artifact.GetProperty("artifactType").GetString().Should().Be("frame_metadata");
        artifact.GetProperty("metadataOnly").GetBoolean().Should().BeTrue();
        artifact.GetProperty("binaryIncluded").GetBoolean().Should().BeFalse();
        artifact.GetProperty("byteLength").GetInt64().Should().Be(0);
    }

    [Fact(DisplayName = "Offline capture should redact path-like camera binding in artifact metadata")]
    public async Task OfflineCapture_ShouldRedactPathLikeCameraBindingMetadata()
    {
        var result = await CreateRegistry().ExecuteAsync(
            RuntimePreviewPermissionGate.CaptureToolName,
            RuntimePreviewContext(),
            Args(new { cameraBindingId = "C:\\secret\\camera.json", operatorTempId = "op_cam" }),
            CancellationToken.None);

        var artifact = Json(result.Data)
            .GetProperty("artifacts")
            .EnumerateArray()
            .Single();
        artifact.GetProperty("metadata").GetProperty("cameraBinding").GetString().Should().Be("<redacted>");
        var artifactRaw = artifact.GetRawText().ToLowerInvariant();
        artifactRaw.Should().NotContain("secret");
        artifactRaw.Should().NotContain(".json");
    }

    [Fact(DisplayName = "Offline replay should return operator result metadata artifacts")]
    public async Task OfflineReplay_ShouldReturnOperatorResultMetadata()
    {
        var result = await CreateRegistry().ExecuteAsync(
            RuntimePreviewPermissionGate.ReplayToolName,
            RuntimePreviewContext(),
            Args(new { frameId = "offline-frame", flow = ValidFlow() }),
            CancellationToken.None);

        var artifacts = Json(result.Data).GetProperty("artifacts").EnumerateArray().ToList();
        artifacts.Select(item => item.GetProperty("artifactType").GetString())
            .Should()
            .Contain("operator_result_metadata");
        artifacts.Should().Contain(item =>
            item.GetProperty("metadataOnly").GetBoolean() &&
            !item.GetProperty("binaryIncluded").GetBoolean() &&
            item.GetProperty("byteLength").GetInt64() == 0);
    }

    [Fact(DisplayName = "Offline replay should return previewReady false for structural errors")]
    public async Task OfflineReplay_ShouldReturnPreviewNotReadyForStructuralErrors()
    {
        var result = await CreateRegistry().ExecuteAsync(
            RuntimePreviewPermissionGate.ReplayToolName,
            RuntimePreviewContext(),
            Args(new { frameId = "offline-frame", flow = BrokenFlow() }),
            CancellationToken.None);

        result.Success.Should().BeFalse();
        var payload = Json(result.Data);
        payload.GetProperty("previewReady").GetBoolean().Should().BeFalse();
        payload.GetProperty("workflowDraftAllowed").GetBoolean().Should().BeTrue();
        payload.GetProperty("blockingIssues").EnumerateArray()
            .Select(item => item.GetProperty("code").GetString())
            .Should()
            .Contain("invalid_connection");
    }

    [Fact(DisplayName = "Offline replay should not block workflow draft when resources are missing")]
    public async Task OfflineReplay_ShouldAllowWorkflowDraftWhenResourcesAreMissing()
    {
        var result = await CreateRegistry().ExecuteAsync(
            RuntimePreviewPermissionGate.ReplayToolName,
            RuntimePreviewContext(),
            Args(new { frameId = "offline-frame", flow = ValidFlow() }),
            CancellationToken.None);

        var payload = Json(result.Data);
        payload.GetProperty("workflowDraftAllowed").GetBoolean().Should().BeTrue();
        payload.GetProperty("missingResources").EnumerateArray()
            .Select(item => item.GetProperty("parameterName").GetString())
            .Should()
            .Contain(["CameraBindingId", "Template"]);
        payload.GetProperty("previewReady").GetBoolean().Should().BeTrue();
    }

    [Fact(DisplayName = "RuntimePreview artifacts should not include base64 image bytes or external paths")]
    public async Task RuntimePreviewArtifacts_ShouldNotLeakBinaryOrExternalPaths()
    {
        var flow = new
        {
            operators = new object[]
            {
                Operator("op_cam", "ImageAcquisition", new Dictionary<string, string> { ["CameraBindingId"] = "C:\\secret\\camera.json" }),
                Operator("op_match", "TemplateMatching", new Dictionary<string, string> { ["TemplatePath"] = "C:\\secret\\template.png" })
            },
            connections = new object[]
            {
                Connection("op_cam", "Image", "op_match", "Image")
            }
        };

        var result = await CreateRegistry().ExecuteAsync(
            RuntimePreviewPermissionGate.ReplayToolName,
            RuntimePreviewContext(),
            Args(new { frameId = "offline-frame", flow }),
            CancellationToken.None);

        var artifactsRaw = Json(result.Data).GetProperty("artifacts").GetRawText().ToLowerInvariant();
        artifactsRaw.Should().NotContain("base64");
        artifactsRaw.Should().NotContain("secret");
        artifactsRaw.Should().NotContain(".png");
        artifactsRaw.Should().NotContain(".json");
    }

    [Fact(DisplayName = "RuntimePreview adapter registry should return controlled failure for unknown adapter")]
    public async Task AdapterRegistry_ShouldReturnControlledFailureForUnknownAdapter()
    {
        var result = await CreateRegistry().ExecuteAsync(
            RuntimePreviewPermissionGate.CaptureToolName,
            RuntimePreviewContext(),
            Args(new { adapterName = "missing_adapter", cameraBindingId = "mock-cam" }),
            CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("runtime_preview_adapter_not_found");
        Json(result.Data).GetProperty("adapterName").GetString().Should().Be("missing_adapter");
    }

    [Fact(DisplayName = "ToolTrace should record RuntimePreview permission decision and adapterName")]
    public async Task ToolTrace_ShouldRecordPermissionDecisionAndAdapterName()
    {
        var flow = ValidFlow();
        var service = CreatePlannerService(new DelegatePlannerCompletionSource((_, index) => index switch
        {
            0 => ToolCall(RuntimePreviewPermissionGate.ReplayToolName, new { frameId = "offline-frame", flow }),
            _ => FinalWorkflowDraft(flow)
        }));

        var result = await service.GenerateFlowAsync(
            PlannerRequest("offline replay") with { RuntimePreviewConsent = true },
            CancellationToken.None);

        var trace = Trace(result).Single(item =>
            item.GetProperty("toolName").GetString() == RuntimePreviewPermissionGate.ReplayToolName);
        trace.GetProperty("permission").GetString().Should().Be(nameof(VisionAgentToolPermission.RuntimePreview));
        trace.GetProperty("adapterName").GetString().Should().Be(OfflineRuntimePreviewAdapter.AdapterName);
        trace.GetProperty("permissionDecision").GetProperty("runtimePreviewConsent").GetBoolean().Should().BeTrue();
        Json(result.ValidationPreview)
            .GetProperty("runtimePreview")
            .GetProperty("adapterName")
            .GetString()
            .Should()
            .Be(OfflineRuntimePreviewAdapter.AdapterName);
    }

    [Fact(DisplayName = "RuntimePreview adapter source guard should avoid real hardware station network and process APIs")]
    public void SourceGuard_ShouldAvoidDisallowedRuntimePreviewApis()
    {
        var source = ReadSourceUnder(Path.Combine(GetProductRoot(), "src", "ClearVision.Product.Infrastructure", "AI", "Agent")) +
                     ReadSourceUnder(Path.Combine(GetProductRoot(), "src", "ClearVision.Product.Infrastructure", "AI", "Tools")) +
                     ReadSourceUnder(Path.Combine(GetProductRoot(), "src", "ClearVision.Product.Core", "AI", "Tools"));
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
            "File.OpenRead",
            "Cv2.ImRead",
            "Image.FromFile",
            "Process.Start",
            "ProcessStartInfo",
            "powershell.exe",
            "cmd.exe",
            "execute_command",
            "StationRuntimeClient"
        };

        forbidden.Should().OnlyContain(fragment =>
            !source.Contains(fragment, StringComparison.OrdinalIgnoreCase));
    }

    private static VisionAgentToolRegistry CreateRegistry()
    {
        var adapterRegistry = CreateAdapterRegistry();
        return new VisionAgentToolRegistry(
        [
            new FlowValidationTool(),
            new DryRunFlowTool(),
            new RuntimePreviewCaptureStubTool(adapterRegistry),
            new RuntimePreviewReplayStubTool(adapterRegistry)
        ]);
    }

    private static RuntimePreviewAdapterRegistry CreateAdapterRegistry()
    {
        return new RuntimePreviewAdapterRegistry(
        [
            new OfflineRuntimePreviewAdapter(new RuntimePreviewArtifactStore())
        ]);
    }

    private static VisionAgentToolRegistry CreatePilotRegistry()
    {
        var adapterRegistry = CreatePilotAdapterRegistry();
        return new VisionAgentToolRegistry(
        [
            new RuntimePreviewCaptureStubTool(adapterRegistry),
            new RuntimePreviewReplayStubTool(adapterRegistry)
        ]);
    }

    private static RuntimePreviewAdapterRegistry CreatePilotAdapterRegistry()
    {
        var offline = new OfflineRuntimePreviewAdapter(new RuntimePreviewArtifactStore());
        return new RuntimePreviewAdapterRegistry(
        [
            offline,
            new PilotRuntimePreviewAdapter(
                new RuntimePreviewPilotResourceCatalog(),
                new RuntimePreviewPilotReadinessGate(new RuntimePreviewResourceAllowlistResolver()),
                offline)
        ]);
    }

    private static VisionAgentGenerateFlowService CreatePlannerService(
        IVisionAgentPlannerCompletionSource completionSource)
    {
        var loopOptions = new VisionAgentLoopOptions
        {
            MaxToolRounds = 8,
            MaxToolCallsPerRound = 4,
            MaxToolResultChars = 64_000
        };
        var parser = new VisionAgentProtocolParser();
        var planner = new VisionAgentPlannerService(
            completionSource,
            parser,
            new AgentToolCallPolicy(),
            new AgentPlannerPromptBuilder());

        return new VisionAgentGenerateFlowService(
            new VisionAgentLoop(
                CreateRegistry(),
                new VisionAgentProtocolParser(),
                new AgentPromptBuilder(),
                Options.Create(loopOptions)),
            Options.Create(loopOptions),
            Substitute.For<Microsoft.Extensions.Logging.ILogger<VisionAgentGenerateFlowService>>(),
            Options.Create(new AgentGenerateFlowOptions
            {
                Mode = AiAgentGenerateFlowModes.Planner,
                FallbackToScriptedOnPlannerFailure = false
            }),
            planner,
            parser,
            new AgentWorkflowDraftEditor());
    }

    private static VisionAgentToolContext RuntimePreviewContext(RuntimePreviewPilotConfig? pilotConfig = null)
    {
        return new VisionAgentToolContext
        {
            RuntimePreviewConsent = true,
            RuntimePreviewPilot = pilotConfig ?? new RuntimePreviewPilotConfig(),
            AllowedPermissions = new HashSet<VisionAgentToolPermission>
            {
                VisionAgentToolPermission.ReadOnly,
                VisionAgentToolPermission.Simulation,
                VisionAgentToolPermission.RuntimePreview
            }
        };
    }

    private static RuntimePreviewPilotConfig PilotConfig()
    {
        var config = new RuntimePreviewPilotConfig
        {
            Enabled = true,
            AllowedCameraBindingIds = ["cam-a"],
            AllowedModelIds = ["model-a"],
            AllowedTemplateIds = ["template-a"],
            AllowedFlowIds = ["flow-a"],
            AllowedResourceRoots = ["catalog-a"],
            MaxPreviewArtifacts = 10
        };
        config.Normalize();
        return config;
    }

    private static AiConfigStore CreateAiConfigStore()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"cv-rp-catalog-{Guid.NewGuid():N}");
        return new AiConfigStore(
            Options.Create(new AiGenerationOptions
            {
                Provider = "OpenAI Compatible",
                Model = "gpt-4o-mini",
                ApiKey = string.Empty,
                BaseUrl = string.Empty,
                TimeoutSeconds = 90
            }),
            NullLogger<AiConfigStore>.Instance,
            directory);
    }

    private static AiFlowGenerationRequest PlannerRequest(string description)
    {
        return new AiFlowGenerationRequest(description)
        {
            UseVisionAgentGenerateFlow = true,
            AgentGenerateFlowMode = AiAgentGenerateFlowModes.Planner
        };
    }

    private static object ValidFlow()
    {
        return new
        {
            operators = new object[]
            {
                Operator("op_cam", "ImageAcquisition", new Dictionary<string, string> { ["CameraBindingId"] = "<pending-camera-binding>" }),
                Operator("op_match", "TemplateMatching", new Dictionary<string, string> { ["TemplatePath"] = "<pending-template-path>" })
            },
            connections = new object[]
            {
                Connection("op_cam", "Image", "op_match", "Image")
            }
        };
    }

    private static object AllowlistedFlow(
        string cameraBindingId = "cam-a",
        string templateId = "template-a",
        string? templatePath = null)
    {
        var templateParameters = new Dictionary<string, string>();
        if (!string.IsNullOrWhiteSpace(templatePath))
        {
            templateParameters["TemplatePath"] = templatePath;
        }
        else
        {
            templateParameters["TemplateId"] = templateId;
        }

        return new
        {
            operators = new object[]
            {
                Operator("op_cam", "ImageAcquisition", new Dictionary<string, string>
                {
                    ["SourceType"] = "Camera",
                    ["CameraBindingId"] = cameraBindingId
                }),
                Operator("op_match", "TemplateMatching", templateParameters)
            },
            connections = new object[]
            {
                Connection("op_cam", "Image", "op_match", "Image")
            }
        };
    }

    private static object BrokenAllowlistedFlow()
    {
        return new
        {
            operators = new object[]
            {
                Operator("op_cam", "ImageAcquisition", new Dictionary<string, string>
                {
                    ["SourceType"] = "Camera",
                    ["CameraBindingId"] = "cam-a"
                }),
                Operator("op_match", "TemplateMatching", new Dictionary<string, string> { ["TemplateId"] = "template-a" })
            },
            connections = new object[]
            {
                Connection("op_cam", "Image", "op_missing", "Image")
            }
        };
    }

    private static object BrokenFlow()
    {
        return new
        {
            operators = new object[]
            {
                Operator("op_cam", "ImageAcquisition", new Dictionary<string, string> { ["CameraBindingId"] = "mock-cam" })
            },
            connections = new object[]
            {
                Connection("op_cam", "Image", "op_missing", "Image")
            }
        };
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
            displayName = operatorType,
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

    private static string ToolCall(string name, object arguments)
    {
        return JsonSerializer.Serialize(new
        {
            kind = "tool_call",
            toolCalls = new[]
            {
                new
                {
                    id = "call_1",
                    name,
                    arguments
                }
            }
        });
    }

    private static string FinalWorkflowDraft(object flow)
    {
        return JsonSerializer.Serialize(new
        {
            kind = "final",
            workflowDraft = flow
        });
    }

    private static JsonElement Args(object? value = null)
    {
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(value ?? new { }));
        return doc.RootElement.Clone();
    }

    private static JsonElement Json(object? value)
    {
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(value));
        return doc.RootElement.Clone();
    }

    private static IReadOnlyList<JsonElement> Trace(AiFlowGenerationResult result)
    {
        return Json(result.ToolTrace)
            .EnumerateArray()
            .Select(item => item.Clone())
            .ToList();
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

    private sealed class DelegatePlannerCompletionSource : IVisionAgentPlannerCompletionSource
    {
        private readonly Func<AgentPlannerCompletionRequest, int, string> _next;
        private int _index;

        public DelegatePlannerCompletionSource(Func<AgentPlannerCompletionRequest, int, string> next)
        {
            _next = next;
        }

        public Task<string> CompleteAsync(
            AgentPlannerCompletionRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_next(request, _index++));
        }
    }

    private sealed class ThrowingRuntimePreviewAdapter : IRuntimePreviewAdapter
    {
        public const string AdapterName = "throwing_runtime_preview";

        public string Name => AdapterName;

        public IReadOnlySet<string> SupportedToolNames { get; } =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                RuntimePreviewPermissionGate.ReplayToolName
            };

        public Task<RuntimePreviewResult> ExecuteAsync(
            RuntimePreviewRequest request,
            CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("synthetic adapter failure");
        }
    }
}
