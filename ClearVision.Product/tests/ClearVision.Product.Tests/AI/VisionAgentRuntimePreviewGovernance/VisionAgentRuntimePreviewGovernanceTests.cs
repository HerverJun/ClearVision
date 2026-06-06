using System.Text.Json;
using ClearVision.Product.Core.AI.Tools;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Infrastructure.AI.Agent;
using ClearVision.Product.Infrastructure.AI.Tools;
using FluentAssertions;

namespace ClearVision.Product.Tests.AI.VisionAgentRuntimePreviewGovernance;

public sealed class VisionAgentRuntimePreviewGovernanceTests
{
    [Theory]
    [InlineData(true, true, true, "runtime_preview_endpoint_access_allowed")]
    [InlineData(true, false, true, "runtime_preview_endpoint_access_allowed")]
    [InlineData(false, true, false, "runtime_preview_endpoint_admin_required")]
    [InlineData(false, false, false, "runtime_preview_endpoint_admin_required")]
    public void PermissionBroker_ShouldGateEndpointAccess(
        bool isAdmin,
        bool developerUi,
        bool expectedAllowed,
        string expectedReason)
    {
        var decision = new RuntimePreviewPermissionBroker()
            .EvaluateEndpointAccess("readiness", isAdmin, developerUi);

        decision.Allowed.Should().Be(expectedAllowed);
        decision.MetadataOnly.Should().BeTrue();
        decision.ReasonCode.Should().Be(expectedReason);
    }

    [Theory]
    [InlineData(false, true, true, RuntimePreviewPermissionGate.ConsentRequiredErrorCode)]
    [InlineData(true, false, true, RuntimePreviewPermissionGate.PermissionDeniedErrorCode)]
    [InlineData(true, true, false, "runtime_preview_pilot_disabled")]
    public void PermissionBroker_ShouldDenyMissingConsentPermissionOrPilot(
        bool consent,
        bool allowPermission,
        bool pilotEnabled,
        string expectedReason)
    {
        var config = PilotConfig();
        config.Enabled = pilotEnabled;
        var context = Context(config, consent, allowPermission);

        var decision = new RuntimePreviewPermissionBroker().EvaluateRuntimePreviewAccess(
            context,
            config,
            RuntimePreviewResourceAllowlistResolver.MetadataToolName);

        decision.Allowed.Should().BeFalse();
        decision.ReasonCode.Should().Be(expectedReason);
        decision.MetadataOnly.Should().BeTrue();
    }

    [Fact]
    public void PermissionBroker_ShouldAllowReadyMetadataSimulation()
    {
        var config = PilotConfig();
        var readiness = ReadyReadiness();

        var decision = new RuntimePreviewPermissionBroker().EvaluateRuntimePreviewAccess(
            Context(config),
            config,
            RuntimePreviewResourceAllowlistResolver.MetadataToolName,
            readiness);

        decision.Allowed.Should().BeTrue();
        decision.Status.Should().Be(RuntimePreviewPermissionStatuses.Allowed);
        decision.MetadataOnly.Should().BeTrue();
    }

    [Fact]
    public void PermissionBroker_ShouldDenyDangerousReadiness()
    {
        var config = PilotConfig();
        var readiness = ReadyReadiness() with
        {
            Status = RuntimePreviewPilotReadinessStatuses.Denied,
            ResourceTrace = RuntimePreviewResourceTrace.NotEvaluated() with
            {
                ReasonCode = "runtime_preview_external_path_denied"
            }
        };

        var decision = new RuntimePreviewPermissionBroker().EvaluateRuntimePreviewAccess(
            Context(config),
            config,
            RuntimePreviewResourceAllowlistResolver.MetadataToolName,
            readiness);

        decision.Allowed.Should().BeFalse();
        decision.DangerousDenied.Should().BeTrue();
        decision.ReasonCode.Should().Be("runtime_preview_external_path_denied");
    }

    [Fact]
    public void PermissionBroker_ShouldKeepNotReadyPendingActions()
    {
        var config = PilotConfig();
        var readiness = ReadyReadiness() with
        {
            Status = RuntimePreviewPilotReadinessStatuses.NotReady,
            CanRunMetadataPilot = false,
            PendingActions =
            [
                new VisionAgentPendingAction
                {
                    ActionType = "RuntimePreviewPilotReadinessReview",
                    Title = "Review",
                    Summary = "Missing camera",
                    RequiresUserConfirmation = true
                }
            ],
            ResourceTrace = RuntimePreviewResourceTrace.NotEvaluated() with
            {
                ReasonCode = "runtime_preview_camera_not_allowlisted"
            }
        };

        var decision = new RuntimePreviewPermissionBroker().EvaluateRuntimePreviewAccess(
            Context(config),
            config,
            RuntimePreviewResourceAllowlistResolver.MetadataToolName,
            readiness);

        decision.Allowed.Should().BeFalse();
        decision.PendingActions.Should().Contain(action => action.ActionType == "RuntimePreviewPilotReadinessReview");
    }

    [Theory]
    [InlineData(RuntimePreviewSessionStatuses.Completed)]
    [InlineData(RuntimePreviewSessionStatuses.Denied)]
    [InlineData(RuntimePreviewSessionStatuses.Failed)]
    [InlineData(RuntimePreviewSessionStatuses.Cancelled)]
    public void PermissionBroker_ShouldDenyTerminalSessionTransition(string terminalStatus)
    {
        var session = new RuntimePreviewSession { SessionId = "s1", Status = terminalStatus };

        var decision = new RuntimePreviewPermissionBroker()
            .EvaluateSessionTransition(session, RuntimePreviewSessionStatuses.Cancelled);

        decision.Allowed.Should().BeFalse();
        decision.ReasonCode.Should().Be("runtime_preview_session_terminal");
    }

    [Fact]
    public void ResourceBroker_ShouldCreateMetadataHandlesWithoutUnsafeValues()
    {
        var appConfig = AppConfigWithCamera();
        var config = PilotConfig();
        var handles = new RuntimePreviewResourceBroker(new RuntimePreviewPilotResourceCatalog())
            .CreateSnapshot(config, appConfig, null, Args(new { flow = ValidFlow() }));

        handles.RealResourcesTouched.Should().BeFalse();
        handles.MetadataOnly.Should().BeTrue();
        handles.Handles.Should().Contain(handle => handle.ResourceType == "camera");
        var raw = JsonSerializer.Serialize(handles);
        raw.Should().NotContain("192.0.2.20");
        raw.Should().NotContain("C:\\");
    }

    [Theory]
    [InlineData("camera")]
    [InlineData("model")]
    [InlineData("template")]
    [InlineData("flow")]
    [InlineData("resourceRoot")]
    public void ResourceBroker_ShouldOnlyExposeAllowedHandleTypes(string resourceType)
    {
        var handles = new RuntimePreviewResourceBroker(new RuntimePreviewPilotResourceCatalog())
            .CreateSnapshot(PilotConfig(), AppConfigWithCamera(), null, Args(new { flow = ValidFlow() }));

        var allowedTypes = new[] { "camera", "model", "template", "flow", "resourceRoot" };
        handles.Handles.Select(handle => handle.ResourceType)
            .Should().OnlyContain(type => allowedTypes.Contains(type));
        handles.Handles.Where(handle => handle.ResourceType == resourceType)
            .All(handle => handle.MetadataOnly)
            .Should().BeTrue();
    }

    [Fact]
    public void AuditTrail_ShouldRedactSecretsPathsIpBase64StationAndPlc()
    {
        var audit = new RuntimePreviewAuditTrail();

        var auditEvent = audit.Append("s1", RuntimePreviewAuditEventTypes.SessionCreated, new
        {
            apiKey = "secret-key",
            url = "http://192.0.2.20:8317/v1?token=value",
            file = "C:\\secret\\frame.png",
            image = "data:image/png;base64,abcd",
            stationId = "station-1",
            plcAddress = "DB1.DBX0.0"
        });

        var raw = auditEvent.Payload.GetRawText();
        raw.Should().Contain("redacted");
        raw.Should().NotContain("secret-key");
        raw.Should().NotContain("192.0.2.20");
        raw.Should().NotContain("frame.png");
        raw.Should().NotContain("base64,abcd");
        raw.Should().NotContain("DB1");
    }

    [Fact]
    public void SessionStore_ShouldCreateAndUpdateSafeSessionSummary()
    {
        var store = new RuntimePreviewSessionStore();

        var session = store.Create("flow-hash", "config-hash", "catalog-1");
        var updated = store.Update(session.SessionId, current => current with
        {
            Status = RuntimePreviewSessionStatuses.ReadinessChecked,
            AuditEventIds = ["audit-1"]
        });

        updated.Status.Should().Be(RuntimePreviewSessionStatuses.ReadinessChecked);
        updated.AuditEventIds.Should().Contain("audit-1");
        store.List().Should().Contain(item => item.SessionId == session.SessionId);
    }

    [Fact]
    public void SimulationHarness_ShouldRunEndToEndMetadataSession()
    {
        var harness = Harness();

        var report = harness.RunEndToEnd(SessionRequest(ValidFlow()), AppConfigWithCamera(), null);

        report.PreviewReady.Should().BeTrue();
        report.Session.Status.Should().Be(RuntimePreviewSessionStatuses.Completed);
        report.Simulation.Should().NotBeNull();
        report.AuditEvents.Select(item => item.EventType).Should().Contain(RuntimePreviewAuditEventTypes.SessionCreated);
        report.AuditEvents.Select(item => item.EventType).Should().Contain(RuntimePreviewAuditEventTypes.SimulationCompleted);
        report.RealResourcesTouched.Should().BeFalse();
    }

    [Fact]
    public void SimulationHarness_ShouldGenerateReportArchive()
    {
        var archive = new RuntimePreviewReportArchive();
        var harness = Harness(reportArchive: archive);

        var report = harness.RunEndToEnd(SessionRequest(ValidFlow()), AppConfigWithCamera(), null);

        archive.Get(report.ReportId).Should().NotBeNull();
        archive.GetBySessionId(report.SessionId)!.ReportId.Should().Be(report.ReportId);
    }

    [Fact]
    public void SimulationHarness_ShouldDenyDangerousPathWithoutArtifact()
    {
        var report = Harness().RunEndToEnd(SessionRequest(FlowWithTemplatePath()), AppConfigWithCamera(), null);

        report.PreviewReady.Should().BeFalse();
        report.PermissionDecision!.DangerousDenied.Should().BeTrue();
        report.Simulation.Should().BeNull();
        JsonSerializer.Serialize(report).Should().NotContain("secret");
    }

    [Fact]
    public void SimulationHarness_ShouldKeepWorkflowDraftAllowedWhenNotReady()
    {
        var report = Harness().RunEndToEnd(SessionRequest(ValidFlow(cameraBindingId: "cam-missing")), AppConfigWithCamera(), null);

        report.PreviewReady.Should().BeFalse();
        report.Readiness!.WorkflowDraftAllowed.Should().BeTrue();
        report.Readiness.PendingActions.Should().NotBeEmpty();
        report.Session.Status.Should().Be(RuntimePreviewSessionStatuses.Denied);
    }

    [Fact]
    public void SimulationHarness_ShouldDenyWhenConsentMissing()
    {
        var request = SessionRequest(ValidFlow()) with { RuntimePreviewConsent = false };

        var report = Harness().RunEndToEnd(request, AppConfigWithCamera(), null);

        report.PreviewReady.Should().BeFalse();
        report.PermissionDecision!.ReasonCode.Should().Be(RuntimePreviewPermissionGate.ConsentRequiredErrorCode);
        report.PermissionDecision.PendingActions.Should().NotBeEmpty();
    }

    [Fact]
    public void SimulationHarness_ShouldCreateAndCancelMetadataSession()
    {
        var harness = Harness();
        var session = harness.CreateMetadataSession(SessionRequest(ValidFlow()), AppConfigWithCamera(), null);

        var cancelled = harness.Cancel(session.SessionId);

        cancelled.Should().NotBeNull();
        cancelled!.Status.Should().Be(RuntimePreviewSessionStatuses.Cancelled);
        cancelled.AuditEventIds.Should().HaveCountGreaterThan(2);
    }

    [Fact]
    public void SimulationHarness_ShouldRefuseCancellingCompletedSession()
    {
        var harness = Harness();
        var report = harness.RunEndToEnd(SessionRequest(ValidFlow()), AppConfigWithCamera(), null);

        var cancelled = harness.Cancel(report.SessionId);

        cancelled!.Status.Should().Be(RuntimePreviewSessionStatuses.Completed);
    }

    [Fact]
    public void SimulationTool_ShouldReturnReportAndNoRealResources()
    {
        var config = PilotConfig();
        var context = Context(config);
        var tool = new RuntimePreviewSimulateMetadataSessionTool();

        var result = tool.ExecuteAsync(
            context,
            Args(new
            {
                flow = ValidFlow(),
                runtimePreviewConsent = true
            }),
            CancellationToken.None).GetAwaiter().GetResult();

        result.Success.Should().BeTrue();
        var raw = JsonSerializer.Serialize(result.Data);
        raw.Should().Contain("runtime_preview_session_metadata");
        raw.Should().Contain("\"realResourcesTouched\":false");
    }

    [Theory]
    [InlineData(RuntimePreviewAuditEventTypes.SessionCreated)]
    [InlineData(RuntimePreviewAuditEventTypes.ConfigChanged)]
    [InlineData(RuntimePreviewAuditEventTypes.CatalogLoaded)]
    [InlineData(RuntimePreviewAuditEventTypes.AllowlistChanged)]
    [InlineData(RuntimePreviewAuditEventTypes.ReadinessChecked)]
    [InlineData(RuntimePreviewAuditEventTypes.PermissionGranted)]
    [InlineData(RuntimePreviewAuditEventTypes.SimulationStarted)]
    [InlineData(RuntimePreviewAuditEventTypes.SimulationCompleted)]
    [InlineData(RuntimePreviewAuditEventTypes.ReportGenerated)]
    public void SimulationHarness_ShouldRecordExpectedAuditEvents(string eventType)
    {
        var report = Harness().RunEndToEnd(SessionRequest(ValidFlow()), AppConfigWithCamera(), null);

        report.AuditEvents.Select(item => item.EventType).Should().Contain(eventType);
    }

    [Fact]
    public void SessionReport_ShouldExposeSafeLifecycleSummary()
    {
        var report = Harness().RunEndToEnd(SessionRequest(ValidFlow()), AppConfigWithCamera(), null);

        report.Session.WorkflowDraftHash.Should().NotBeNullOrWhiteSpace();
        report.Session.PilotConfigRevision.Should().NotBeNullOrWhiteSpace();
        report.Session.CatalogSnapshotId.Should().StartWith("rp_catalog_");
        report.ResourceHandles.Should().OnlyContain(handle => handle.MetadataOnly);
        report.MetadataOnly.Should().BeTrue();
    }

    private static RuntimePreviewSimulatedExecutionHarness Harness(
        RuntimePreviewReportArchive? reportArchive = null)
    {
        var catalog = new RuntimePreviewPilotResourceCatalog();
        return new RuntimePreviewSimulatedExecutionHarness(
            new RuntimePreviewSessionStore(),
            new RuntimePreviewResourceBroker(catalog),
            new RuntimePreviewPilotReadinessGate(new RuntimePreviewResourceAllowlistResolver()),
            new RuntimePreviewPermissionBroker(),
            new RuntimePreviewAuditTrail(),
            reportArchive ?? new RuntimePreviewReportArchive());
    }

    private static RuntimePreviewSessionCreateRequest SessionRequest(object flow)
    {
        return new RuntimePreviewSessionCreateRequest
        {
            Config = PilotConfig(),
            ToolName = RuntimePreviewResourceAllowlistResolver.MetadataToolName,
            Arguments = Args(new { flow }),
            RuntimePreviewConsent = true
        };
    }

    private static RuntimePreviewPilotConfig PilotConfig()
    {
        var config = new RuntimePreviewPilotConfig
        {
            Enabled = true,
            Mode = RuntimePreviewPilotConfig.ModeMetadataOnly,
            AllowedCameraBindingIds = ["cam-a"],
            AllowedTemplateIds = ["template-a"],
            FallbackToOffline = true,
            DenyExternalPath = true,
            DenyImageBytes = true
        };
        config.Normalize();
        return config;
    }

    private static VisionAgentToolContext Context(
        RuntimePreviewPilotConfig config,
        bool consent = true,
        bool allowPermission = true)
    {
        return new VisionAgentToolContext
        {
            RuntimePreviewConsent = consent,
            RuntimePreviewPilot = config,
            AllowedPermissions = allowPermission
                ? new HashSet<VisionAgentToolPermission>
                {
                    VisionAgentToolPermission.ReadOnly,
                    VisionAgentToolPermission.Simulation,
                    VisionAgentToolPermission.RuntimePreview
                }
                : new HashSet<VisionAgentToolPermission>
                {
                    VisionAgentToolPermission.ReadOnly,
                    VisionAgentToolPermission.Simulation
                }
        };
    }

    private static RuntimePreviewPilotReadinessResult ReadyReadiness()
    {
        return new RuntimePreviewPilotReadinessResult
        {
            Status = RuntimePreviewPilotReadinessStatuses.Ready,
            CanRunMetadataPilot = true,
            WorkflowDraftAllowed = true,
            ResourceTrace = RuntimePreviewResourceTrace.NotEvaluated() with
            {
                Allowed = true,
                ReasonCode = "runtime_preview_resources_allowlisted",
                ResourceType = "workflow"
            }
        };
    }

    private static AppConfig AppConfigWithCamera()
    {
        var config = new AppConfig
        {
            Cameras =
            [
                new CameraBindingConfig
                {
                    Id = "cam-a",
                    DisplayName = "Line Camera",
                    IpAddress = "192.0.2.20"
                }
            ],
            Runtime = new RuntimeConfig
            {
                RuntimePreviewPilot = PilotConfig()
            }
        };
        config.Normalize();
        return config;
    }

    private static object ValidFlow(string cameraBindingId = "cam-a")
    {
        return new
        {
            operators = new object[]
            {
                new
                {
                    tempId = "op_cam",
                    operatorType = "ImageAcquisition",
                    parameters = new Dictionary<string, string>
                    {
                        ["SourceType"] = "Camera",
                        ["CameraBindingId"] = cameraBindingId
                    }
                },
                new
                {
                    tempId = "op_template",
                    operatorType = "TemplateMatching",
                    parameters = new Dictionary<string, string>
                    {
                        ["TemplateId"] = "template-a"
                    }
                }
            },
            connections = Array.Empty<object>()
        };
    }

    private static object FlowWithTemplatePath()
    {
        return new
        {
            operators = new object[]
            {
                new
                {
                    tempId = "op_cam",
                    operatorType = "ImageAcquisition",
                    parameters = new Dictionary<string, string>
                    {
                        ["SourceType"] = "Camera",
                        ["CameraBindingId"] = "cam-a"
                    }
                },
                new
                {
                    tempId = "op_template",
                    operatorType = "TemplateMatching",
                    parameters = new Dictionary<string, string>
                    {
                        ["TemplatePath"] = "C:\\secret\\template.png"
                    }
                }
            },
            connections = Array.Empty<object>()
        };
    }

    private static JsonElement Args(object value)
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(value));
        return document.RootElement.Clone();
    }
}
