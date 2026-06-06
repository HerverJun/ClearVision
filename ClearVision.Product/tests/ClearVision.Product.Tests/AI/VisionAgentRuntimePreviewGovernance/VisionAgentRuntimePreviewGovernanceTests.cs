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
        raw.Should().NotContain("external:/");
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
            file = "external:/blocked-frame",
            image = "data:image/png;base64,abcd",
            stationId = "station-1",
            plcAddress = "plc-output-token"
        });

        var raw = auditEvent.Payload.GetRawText();
        raw.Should().Contain("redacted");
        raw.Should().NotContain("secret-key");
        raw.Should().NotContain("192.0.2.20");
        raw.Should().NotContain("blocked-frame");
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

    [Fact]
    public void GovernanceStore_ShouldPersistSessionsAuditAndReportsAsJsonl()
    {
        var directory = TempDirectory();
        try
        {
            var store = new RuntimePreviewGovernanceStore(directory);
            var sessionStore = new RuntimePreviewSessionStore(store);
            var auditTrail = new RuntimePreviewAuditTrail(store);
            var archive = new RuntimePreviewReportArchive(store);
            var harness = Harness(sessionStore, auditTrail, archive);

            var report = harness.RunEndToEnd(SessionRequest(ValidFlow()), AppConfigWithCamera(), null);

            var reloadedStore = new RuntimePreviewGovernanceStore(directory);
            var reloadedSessions = new RuntimePreviewSessionStore(reloadedStore);
            var reloadedAudit = new RuntimePreviewAuditTrail(reloadedStore);
            var reloadedArchive = new RuntimePreviewReportArchive(reloadedStore);

            reloadedSessions.Get(report.SessionId).Should().NotBeNull();
            reloadedAudit.ListForSession(report.SessionId).Should().Contain(item => item.EventType == RuntimePreviewAuditEventTypes.ReportGenerated);
            reloadedArchive.Get(report.ReportId).Should().NotBeNull();
            Directory.GetFiles(directory, "*.jsonl").Should().NotBeEmpty();
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void GovernanceStore_ShouldRejectUnsafeUnredactedPayloadFragments()
    {
        var directory = TempDirectory();
        try
        {
            var store = new RuntimePreviewGovernanceStore(directory);
            var auditEvent = new RuntimePreviewAuditEvent
            {
                EventId = "audit_unsafe",
                SessionId = "session_unsafe",
                EventType = RuntimePreviewAuditEventTypes.ConfigChanged,
                Payload = Args(new
                {
                    apiKey = "secret-value",
                    url = "http://192.0.2.20:8317/v1?token=value",
                    path = "external:/unsafe-frame"
                })
            };

            var act = () => store.SaveAuditEvent(auditEvent);

            act.Should().Throw<InvalidOperationException>()
                .WithMessage("*unsafe payload*");
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void GovernanceStore_ShouldCleanupOldSessionsAuditAndReports()
    {
        var directory = TempDirectory();
        try
        {
            var store = new RuntimePreviewGovernanceStore(directory);
            var oldSession = new RuntimePreviewSession
            {
                SessionId = "rp_session_old",
                WorkflowDraftHash = "old_hash",
                PilotConfigRevision = "old_config",
                CatalogSnapshotId = "old_catalog",
                CreatedAtUtc = DateTimeOffset.UtcNow.AddDays(-10),
                UpdatedAtUtc = DateTimeOffset.UtcNow.AddDays(-10)
            };
            var recentSession = new RuntimePreviewSession
            {
                SessionId = "rp_session_recent",
                WorkflowDraftHash = "recent_hash",
                PilotConfigRevision = "recent_config",
                CatalogSnapshotId = "recent_catalog",
                CreatedAtUtc = DateTimeOffset.UtcNow,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            };
            store.SaveSession(oldSession);
            store.SaveSession(recentSession);
            store.SaveAuditEvent(new RuntimePreviewAuditEvent { EventId = "audit_old", SessionId = oldSession.SessionId, EventType = RuntimePreviewAuditEventTypes.SessionCreated, CreatedAtUtc = oldSession.CreatedAtUtc, Payload = Args(new { metadataOnly = true }) });
            store.SaveAuditEvent(new RuntimePreviewAuditEvent { EventId = "audit_recent", SessionId = recentSession.SessionId, EventType = RuntimePreviewAuditEventTypes.SessionCreated, CreatedAtUtc = recentSession.CreatedAtUtc, Payload = Args(new { metadataOnly = true }) });

            var result = store.Cleanup(retentionDays: 1, maxSessions: 10);

            result.SessionsBefore.Should().Be(2);
            result.SessionsAfter.Should().Be(1);
            result.AuditEventsAfter.Should().Be(1);
            store.LoadSessions().Should().OnlyContain(session => session.SessionId == recentSession.SessionId);
            store.LoadAuditEvents().Should().OnlyContain(audit => audit.SessionId == recentSession.SessionId);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void SimulationHarness_ShouldReplayPersistedMetadataTimeline()
    {
        var auditTrail = new RuntimePreviewAuditTrail();
        var archive = new RuntimePreviewReportArchive();
        var harness = Harness(auditTrail: auditTrail, reportArchive: archive);
        var report = harness.RunEndToEnd(SessionRequest(ValidFlow()), AppConfigWithCamera(), null);

        var replay = harness.Replay(report.SessionId);

        replay.Should().NotBeNull();
        replay!.Timeline.Should().NotBeEmpty();
        replay.AuditEvents.Should().Contain(item => item.EventType == RuntimePreviewAuditEventTypes.SessionReplayed);
        replay.MetadataOnly.Should().BeTrue();
        replay.RealResourcesTouched.Should().BeFalse();
    }

    [Fact]
    public async Task DeployReadinessService_ShouldGenerateMetadataOnlyPreDeploymentReport()
    {
        var archive = new RuntimePreviewReportArchive();
        var auditTrail = new RuntimePreviewAuditTrail();
        var service = new RuntimePreviewDeployReadinessService(
            Harness(auditTrail: auditTrail, reportArchive: archive),
            archive,
            auditTrail,
            new RuntimePackagePrecheckTool());

        var report = await service.GenerateAsync(
            new RuntimePreviewDeployReadinessRequest
            {
                Config = PilotConfig(),
                ToolName = RuntimePreviewResourceAllowlistResolver.MetadataToolName,
                Arguments = Args(new { flow = ValidFlow() }),
                RuntimePreviewConsent = true,
                RequireReplay = true
            },
            AppConfigWithCamera(),
            null,
            isAdmin: true,
            developerUiRequested: true,
            CancellationToken.None);

        report.PreviewReady.Should().BeTrue();
        report.ReadyForDeployment.Should().BeTrue();
        report.PackageCreated.Should().BeFalse();
        report.DeploymentExecuted.Should().BeFalse();
        report.RealResourcesTouched.Should().BeFalse();
        report.RuntimePackagePrecheck.GetProperty("packageCreated").GetBoolean().Should().BeFalse();
        archive.GetDeployReadinessReport(report.ReportId).Should().NotBeNull();
        auditTrail.ListForSession(report.SessionId).Should().Contain(item => item.EventType == RuntimePreviewAuditEventTypes.DeployReadinessGenerated);
    }

    [Fact]
    public async Task DeployReadinessService_ShouldBlockDangerousPathWithoutPackageOrDeployment()
    {
        var archive = new RuntimePreviewReportArchive();
        var auditTrail = new RuntimePreviewAuditTrail();
        var service = new RuntimePreviewDeployReadinessService(
            Harness(auditTrail: auditTrail, reportArchive: archive),
            archive,
            auditTrail,
            new RuntimePackagePrecheckTool());

        var report = await service.GenerateAsync(
            new RuntimePreviewDeployReadinessRequest
            {
                Config = PilotConfig(),
                ToolName = RuntimePreviewResourceAllowlistResolver.MetadataToolName,
                Arguments = Args(new { flow = FlowWithTemplatePath() }),
                RuntimePreviewConsent = true,
                RequireReplay = true
            },
            AppConfigWithCamera(),
            null,
            isAdmin: true,
            developerUiRequested: true,
            CancellationToken.None);

        report.PreviewReady.Should().BeFalse();
        report.ReadyForDeployment.Should().BeFalse();
        report.DeploymentBlocked.Should().BeTrue();
        report.PackageCreated.Should().BeFalse();
        report.DeploymentExecuted.Should().BeFalse();
        report.RealResourcesTouched.Should().BeFalse();
        JsonSerializer.Serialize(report).Should().NotContain("blocked-template");
    }

    [Fact]
    public void ReportArchive_ShouldPersistDeployReadinessReports()
    {
        var directory = TempDirectory();
        try
        {
            var store = new RuntimePreviewGovernanceStore(directory);
            var archive = new RuntimePreviewReportArchive(store);
            var report = new RuntimePreviewDeployReadinessReport
            {
                ReportId = "rp_deploy_readiness_test",
                SessionId = "rp_session_test",
                WorkflowDraftHash = "hash",
                RuntimePackagePrecheck = Args(new { readyForDeployment = false, packageCreated = false, deployed = false }),
                MetadataOnly = true,
                RealResourcesTouched = false,
                PackageCreated = false,
                DeploymentExecuted = false
            };

            archive.SaveDeployReadinessReport(report);

            var reloaded = new RuntimePreviewReportArchive(new RuntimePreviewGovernanceStore(directory));
            reloaded.GetDeployReadinessReport(report.ReportId).Should().NotBeNull();
            reloaded.GetDeployReadinessReportBySessionId(report.SessionId)!.ReportId.Should().Be(report.ReportId);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void MaintenanceService_ShouldAppendRetentionCleanupAuditEvent()
    {
        var directory = TempDirectory();
        try
        {
            var store = new RuntimePreviewGovernanceStore(directory);
            var auditTrail = new RuntimePreviewAuditTrail(store);
            var service = new RuntimePreviewGovernanceMaintenanceService(store, auditTrail);

            var cleanup = service.Cleanup(retentionDays: 30, maxSessions: 200);

            cleanup.MetadataOnly.Should().BeTrue();
            cleanup.RealResourcesTouched.Should().BeFalse();
            auditTrail.ListForSession("runtime_preview_governance")
                .Should().Contain(item => item.EventType == RuntimePreviewAuditEventTypes.RetentionCleanup);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public async Task ScenarioEvidenceService_ShouldCoverMetadataOnlyBusinessScenarios()
    {
        var archive = new RuntimePreviewReportArchive();
        var auditTrail = new RuntimePreviewAuditTrail();
        var service = new RuntimePreviewScenarioEvidenceService(new RuntimePreviewDeployReadinessService(
            Harness(auditTrail: auditTrail, reportArchive: archive),
            archive,
            auditTrail,
            new RuntimePackagePrecheckTool()));

        var document = await service.RunAsync(AppConfigWithCamera(), null, CancellationToken.None);

        document.CaseCount.Should().BeGreaterThanOrEqualTo(14);
        document.Cases.Select(item => item.Scenario).Should().Contain(new[]
        {
            "wire_sequence",
            "template_matching",
            "hole_distance",
            "remote_control_detection",
            "missing_camera",
            "dangerous_path",
            "plc_station_deny",
            "precheck_blocked"
        });
        document.Cases.Should().OnlyContain(item => item.MetadataOnly && !item.RealResourcesTouched);
        document.Cases.Should().Contain(item => item.ActualStatus == RuntimePreviewScenarioEvidenceStatuses.Denied);
        document.Cases.Should().Contain(item => item.ActualStatus == RuntimePreviewScenarioEvidenceStatuses.NotReady);
        JsonSerializer.Serialize(document).Should().NotContain("192.0.2.20");
        JsonSerializer.Serialize(document).Should().NotContain("DB1");
        JsonSerializer.Serialize(document).Should().NotContain("external:/");
    }

    [Fact]
    public void ScenarioEvidenceCases_ShouldExposeExpectedSignalsWithoutUnsafeDraftFragments()
    {
        var cases = RuntimePreviewScenarioEvidenceService.CreateCases();

        cases.Should().HaveCountGreaterThanOrEqualTo(14);
        cases.Should().Contain(item => item.ExpectedSignals.Contains("previewReady"));
        cases.Should().Contain(item => item.ExpectedSignals.Contains("denyReason"));
        cases.Should().OnlyContain(item => item.WorkflowDraft.ValueKind == JsonValueKind.Object);
        var raw = JsonSerializer.Serialize(cases);
        raw.Should().NotContain("DB1");
        raw.Should().NotContain("external:/");
        raw.Should().NotContain("base64");
    }

    [Fact]
    public void ScenarioCorpusService_ShouldExposeBusinessCorpusWithoutUnsafeResources()
    {
        var corpus = new RuntimePreviewScenarioCorpusService().BuildCorpus();

        corpus.CaseCount.Should().BeGreaterThanOrEqualTo(14);
        corpus.Cases.Should().OnlyContain(item => item.MetadataOnly && !item.RealResourcesTouched);
        corpus.Cases.Should().OnlyContain(item => !string.IsNullOrWhiteSpace(item.WorkflowDraftHash));
        corpus.Cases.Select(item => item.Scenario).Should().Contain(new[]
        {
            "wire_sequence",
            "template_matching",
            "hole_distance",
            "remote_control_detection",
            "missing_camera",
            "missing_template",
            "missing_model",
            "dangerous_path",
            "plc_station_deny",
            "precheck_blocked",
            "allowlist_mismatch",
            "multi_operator_flow",
            "missing_parameter",
            "draft_editable_package_blocked"
        });
        var raw = JsonSerializer.Serialize(corpus);
        raw.Should().NotContain("external:/");
        raw.Should().NotContain("192.0.2.20");
        raw.Should().NotContain("DB1");
        raw.Should().NotContain("base64");
    }

    [Theory]
    [MemberData(nameof(CorpusCaseIds))]
    public void ScenarioCorpusCases_ShouldCarryExpectedRiskAndEngineerExplanation(string caseId)
    {
        var item = RuntimePreviewScenarioCorpusService.CreateCorpusCases()
            .Single(entry => entry.CaseId == caseId);

        item.WorkflowDraftHash.Should().NotBeNullOrWhiteSpace();
        item.ExpectedRisk.Should().NotBeNullOrWhiteSpace();
        item.BusinessExplanation.Should().NotBeNullOrWhiteSpace();
        item.MetadataOnly.Should().BeTrue();
        item.RealResourcesTouched.Should().BeFalse();
        JsonSerializer.Serialize(item).Should().NotContain("external:/");
    }

    [Theory]
    [MemberData(nameof(CorpusCaseIds))]
    public void AgentExplanationService_ShouldExplainCorpusCasesForEngineers(string caseId)
    {
        var benchmark = new RuntimePreviewAgentExplanationService(new RuntimePreviewScenarioCorpusService()).Run();
        var result = benchmark.Cases.Single(item => item.CaseId == caseId);

        result.Passed.Should().BeTrue();
        result.ReadyStateExplanation.Should().NotContain("AI");
        result.PackageRiskExplanation.Should().Contain("Risk:");
        result.NextEngineerAction.Should().NotBeNullOrWhiteSpace();
        result.WorkflowDraftAllowed.Should().BeTrue();
        result.MetadataOnly.Should().BeTrue();
        result.RealResourcesTouched.Should().BeFalse();
    }

    [Fact]
    public void RedactedFlowCorpusService_ShouldExposeRealWorldMetadataOnlyCorpus()
    {
        var corpus = new RuntimePreviewRedactedFlowCorpusService().BuildCorpus();

        corpus.CaseCount.Should().BeGreaterThanOrEqualTo(20);
        corpus.Cases.Should().OnlyContain(item => item.MetadataOnly && !item.RealResourcesTouched);
        corpus.Cases.Select(item => item.WorkflowKind).Should().Contain(new[]
        {
            "wire_sequence",
            "remote_control_defect",
            "template_measurement_combo",
            "hole_distance",
            "terminal_color_order",
            "missing_camera",
            "missing_template",
            "missing_model",
            "missing_output_channel",
            "plc_station_deny",
            "dangerous_path",
            "allowlist_mismatch",
            "multi_camera_flow",
            "multi_model_flow",
            "parameter_missing",
            "package_manifest_blocked",
            "workflow_editable_package_blocked"
        });
        var raw = JsonSerializer.Serialize(corpus);
        raw.Should().NotContain("external:/");
        raw.Should().NotContain(".cvpkg");
        raw.Should().NotContain("192.0.2.20");
        raw.Should().NotContain("DB1");
        raw.Should().NotContain("base64");
    }

    [Theory]
    [MemberData(nameof(RedactedFlowCaseIds))]
    public void RedactedFlowCorpusCases_ShouldCarryManifestReviewMetadata(string caseId)
    {
        var item = RuntimePreviewRedactedFlowCorpusService.CreateCases()
            .Single(entry => entry.CaseId == caseId);

        item.StationType.Should().NotBeNullOrWhiteSpace();
        item.WorkflowKind.Should().NotBeNullOrWhiteSpace();
        item.BusinessPurpose.Should().NotBeNullOrWhiteSpace();
        item.WorkflowDraftHash.Should().NotBeNullOrWhiteSpace();
        item.OperatorSummary.Should().NotBeEmpty();
        item.ExpectedManifestRisk.Should().NotBeNullOrWhiteSpace();
        item.ExpectedEngineerAction.Should().NotBeNullOrWhiteSpace();
        item.RedactionStatus.Should().Be("redacted_metadata_only");
        item.MetadataOnly.Should().BeTrue();
        item.RealResourcesTouched.Should().BeFalse();
        JsonSerializer.Serialize(item).Should().NotContain("external:/");
    }

    [Theory]
    [MemberData(nameof(RedactedFlowCaseIds))]
    public void ManifestDryRunService_ShouldGenerateMetadataManifestForRedactedFlowCase(string caseId)
    {
        var item = RuntimePreviewRedactedFlowCorpusService.CreateCases()
            .Single(entry => entry.CaseId == caseId);
        var ready = item.ExpectedPackageReadiness == RuntimePreviewScenarioEvidenceStatuses.Passed;
        var archive = new RuntimePreviewReportArchive();
        var auditTrail = new RuntimePreviewAuditTrail();
        var service = new RuntimePackageManifestDryRunService(archive, auditTrail);
        var packageReport = new RuntimePreviewPackageReadinessReport
        {
            ReportId = $"rp_package_{caseId}",
            SessionId = $"rp_session_{caseId}",
            WorkflowDraftHash = item.WorkflowDraftHash,
            ReadyForPackage = ready,
            PackageReviewAllowed = ready,
            PackageBlocked = !ready,
            BlockingIssues = ready ? [] : [item.ExpectedManifestRisk],
            BlockedReason = ready ? string.Empty : item.ExpectedManifestRisk,
            RiskSummary = item.ExpectedManifestRisk,
            RuntimePackagePrecheck = Args(new { packageCreated = false, deploymentExecuted = false }),
            WorkflowDraftAllowed = true,
            MetadataOnly = true,
            RealResourcesTouched = false
        };

        var report = service.GenerateFromPackageReadiness(
            packageReport,
            new RuntimePreviewPackageReadinessRequest
            {
                Arguments = Args(new { flow = item.WorkflowDraft }),
                RuntimePreviewConsent = true
            });

        report.ManifestId.Should().StartWith("rp_manifest_dry_run_");
        report.ManifestHash.Should().NotBeNullOrWhiteSpace();
        report.OperatorCount.Should().BeGreaterThan(0);
        report.OperatorTypes.Should().NotBeEmpty();
        report.DependencyTrace.Should().NotBeEmpty();
        report.WorkflowDraftAllowed.Should().BeTrue();
        report.PackageCreated.Should().BeFalse();
        report.DeploymentExecuted.Should().BeFalse();
        report.ManifestArtifactGenerated.Should().BeFalse();
        report.MetadataOnly.Should().BeTrue();
        report.RealResourcesTouched.Should().BeFalse();
        report.PackageReviewAllowed.Should().Be(ready && report.MissingDependencies.Count == 0);
        archive.GetManifestDryRunReport(report.ManifestId).Should().NotBeNull();
        auditTrail.ListForSession(packageReport.SessionId).Should().Contain(entry => entry.EventType == RuntimePreviewAuditEventTypes.ManifestDryRunGenerated);
        JsonSerializer.Serialize(report).Should().NotContain(".cvpkg");
    }

    [Fact]
    public async Task PackageReadinessBridge_ShouldGenerateMetadataOnlyPackageReport()
    {
        var archive = new RuntimePreviewReportArchive();
        var auditTrail = new RuntimePreviewAuditTrail();
        var bridge = new RuntimePreviewPackageReadinessBridge(
            new RuntimePreviewDeployReadinessService(
                Harness(auditTrail: auditTrail, reportArchive: archive),
                archive,
                auditTrail,
                new RuntimePackagePrecheckTool()),
            archive,
            auditTrail);

        var report = await bridge.GenerateAsync(
            new RuntimePreviewPackageReadinessRequest
            {
                Config = PilotConfig(),
                ToolName = RuntimePreviewResourceAllowlistResolver.MetadataToolName,
                Arguments = Args(new { flow = ValidFlow() }),
                RuntimePreviewConsent = true,
                RequireReplay = true
            },
            AppConfigWithCamera(),
            null,
            isAdmin: true,
            developerUiRequested: true,
            CancellationToken.None);

        report.ReadyForPackage.Should().BeTrue();
        report.PackageReviewAllowed.Should().BeTrue();
        report.PackageBlocked.Should().BeFalse();
        report.ManifestDryRunReportId.Should().NotBeNullOrWhiteSpace();
        report.PackageCreated.Should().BeFalse();
        report.DeploymentExecuted.Should().BeFalse();
        report.DependencyTrace.Should().NotBeEmpty();
        report.PackageRiskLevel.Should().Be("low");
        report.OperatorTrace.Should().NotBeEmpty();
        report.ResourceTrace.Should().NotBeEmpty();
        archive.GetPackageReadinessReport(report.ReportId).Should().NotBeNull();
        archive.GetManifestDryRunReport(report.ManifestDryRunReportId).Should().NotBeNull();
        auditTrail.ListForSession(report.SessionId).Should().Contain(item => item.EventType == RuntimePreviewAuditEventTypes.PackageReadinessGenerated);
        auditTrail.ListForSession(report.SessionId).Should().Contain(item => item.EventType == RuntimePreviewAuditEventTypes.ManifestDryRunGenerated);
    }

    [Fact]
    public async Task PackageReadinessBridge_ShouldBlockMissingResourcesButKeepDraftEditable()
    {
        var archive = new RuntimePreviewReportArchive();
        var auditTrail = new RuntimePreviewAuditTrail();
        var bridge = new RuntimePreviewPackageReadinessBridge(
            new RuntimePreviewDeployReadinessService(
                Harness(auditTrail: auditTrail, reportArchive: archive),
                archive,
                auditTrail,
                new RuntimePackagePrecheckTool()),
            archive,
            auditTrail);

        var report = await bridge.GenerateAsync(
            new RuntimePreviewPackageReadinessRequest
            {
                Config = PilotConfig(),
                ToolName = RuntimePreviewResourceAllowlistResolver.MetadataToolName,
                Arguments = Args(new { flow = ValidFlow(cameraBindingId: "cam-missing") }),
                RuntimePreviewConsent = true,
                RequireReplay = true
            },
            AppConfigWithCamera(),
            null,
            isAdmin: true,
            developerUiRequested: true,
            CancellationToken.None);

        report.ReadyForPackage.Should().BeFalse();
        report.PackageReviewAllowed.Should().BeFalse();
        report.PackageBlocked.Should().BeTrue();
        report.WorkflowDraftAllowed.Should().BeTrue();
        report.PackageCreated.Should().BeFalse();
        report.RiskSummary.Should().Contain("blocked");
        report.PackageReviewExplanation.Should().Contain("workflow edits");
        report.ManifestDryRunReportId.Should().NotBeNullOrWhiteSpace();
        report.BlockingIssues.Should().NotBeEmpty();
    }

    [Fact]
    public void GovernanceStore_ShouldExposeIndexSummaryAndRecoverCorruptLines()
    {
        var directory = TempDirectory();
        try
        {
            var store = new RuntimePreviewGovernanceStore(directory);
            store.SaveSession(new RuntimePreviewSession
            {
                SessionId = "rp_session_index",
                WorkflowDraftHash = "hash",
                PilotConfigRevision = "config",
                CatalogSnapshotId = "catalog"
            });
            File.AppendAllText(Path.Combine(directory, "runtime_preview_sessions.jsonl"), "{bad json" + Environment.NewLine);

            var summary = store.BuildIndexSummary();
            var sessions = store.LoadSessions();

            summary.SchemaVersion.Should().Be(RuntimePreviewGovernanceStore.SchemaVersion);
            summary.StorageVersion.Should().Be(RuntimePreviewGovernanceStore.StorageVersion);
            summary.CorruptLineCount.Should().BeGreaterThan(0);
            sessions.Should().Contain(item => item.SessionId == "rp_session_index");
            summary.MetadataOnly.Should().BeTrue();
            summary.RealResourcesTouched.Should().BeFalse();
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void GovernanceStore_ShouldExportRedactedManifestWithPackageReports()
    {
        var directory = TempDirectory();
        try
        {
            var store = new RuntimePreviewGovernanceStore(directory);
            store.SaveSession(new RuntimePreviewSession
            {
                SessionId = "rp_session_export",
                WorkflowDraftHash = "hash",
                PilotConfigRevision = "config",
                CatalogSnapshotId = "catalog"
            });
            store.SavePackageReadinessReport(new RuntimePreviewPackageReadinessReport
            {
                ReportId = "rp_package_readiness_export",
                SessionId = "rp_session_export",
                WorkflowDraftHash = "hash",
                RiskSummary = "Metadata only package readiness export.",
                RuntimePackagePrecheck = Args(new { packageCreated = false, deploymentExecuted = false }),
                MetadataOnly = true,
                RealResourcesTouched = false
            });
            store.SaveManifestDryRunReport(new RuntimePackageManifestDryRunReport
            {
                ManifestId = "rp_manifest_export",
                ReportId = "rp_manifest_report_export",
                SessionId = "rp_session_export",
                WorkflowDraftHash = "hash",
                ManifestHash = "manifest-hash",
                OperatorCount = 1,
                OperatorTypes = ["ImageAcquisition"],
                MetadataOnly = true,
                RealResourcesTouched = false
            });

            var manifest = store.ExportManifest();

            manifest.ExportId.Should().StartWith("rp_export_");
            manifest.IndexSummary.PackageReadinessReportCount.Should().Be(1);
            manifest.IndexSummary.ManifestDryRunReportCount.Should().Be(1);
            manifest.PackageReadinessReports.Should().Contain(item => item.ReportId == "rp_package_readiness_export");
            manifest.ManifestDryRunReports.Should().Contain(item => item.ManifestId == "rp_manifest_export");
            manifest.RedactionPass.Should().BeTrue();
            var raw = JsonSerializer.Serialize(manifest);
            raw.Should().NotContain("192.0.2.20");
            raw.Should().NotContain("external:/");
            raw.Should().NotContain("apiKey");
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void ReportArchive_ShouldLookupPackageReadinessBySessionAndReport()
    {
        var archive = new RuntimePreviewReportArchive();
        var report = new RuntimePreviewPackageReadinessReport
        {
            ReportId = "rp_package_readiness_lookup",
            SessionId = "rp_session_lookup",
            RuntimePackagePrecheck = Args(new { packageCreated = false }),
            MetadataOnly = true
        };

        archive.SavePackageReadinessReport(report);

        archive.GetPackageReadinessReport("rp_package_readiness_lookup").Should().NotBeNull();
        archive.GetPackageReadinessReportBySessionId("rp_session_lookup")!.ReportId.Should().Be(report.ReportId);
        archive.ListPackageReadinessReports().Should().ContainSingle();
    }

    [Fact]
    public void ReportArchive_ShouldLookupManifestDryRunByManifestSessionAndReport()
    {
        var archive = new RuntimePreviewReportArchive();
        var report = new RuntimePackageManifestDryRunReport
        {
            ManifestId = "rp_manifest_lookup",
            ReportId = "rp_manifest_report_lookup",
            SessionId = "rp_session_manifest_lookup",
            PackageReadinessReportId = "rp_package_lookup",
            MetadataOnly = true
        };

        archive.SaveManifestDryRunReport(report);

        archive.GetManifestDryRunReport("rp_manifest_lookup").Should().NotBeNull();
        archive.GetManifestDryRunReportBySessionId("rp_session_manifest_lookup")!.ManifestId.Should().Be(report.ManifestId);
        archive.GetManifestDryRunReportByReportId("rp_package_lookup")!.ManifestId.Should().Be(report.ManifestId);
        archive.ListManifestDryRunReports().Should().ContainSingle();
    }

    [Fact]
    public void AgentExplanationBenchmark_ShouldBeAcceptedForFullCorpus()
    {
        var benchmark = new RuntimePreviewAgentExplanationService(
            new RuntimePreviewScenarioCorpusService(),
            new RuntimePreviewRedactedFlowCorpusService()).Run();

        benchmark.CaseCount.Should().BeGreaterThanOrEqualTo(20);
        benchmark.PassedCaseCount.Should().Be(benchmark.CaseCount);
        benchmark.Accepted.Should().BeTrue();
        benchmark.Cases.Should().OnlyContain(item => item.WorkflowDraftAllowed && item.MetadataOnly && !item.RealResourcesTouched);
        benchmark.Cases.Should().OnlyContain(item => !string.IsNullOrWhiteSpace(item.Status));
        benchmark.Cases.Should().OnlyContain(item => !string.IsNullOrWhiteSpace(item.ManifestRisk));
        benchmark.Cases.Should().Contain(item => item.PackageReviewAllowed);
        benchmark.Cases.Should().Contain(item => !item.PackageReviewAllowed && item.BlockedReasons.Count > 0);
    }

    [Fact]
    public void PackageReadinessReport_ShouldSerializeWithoutUnsafeFragments()
    {
        var report = new RuntimePreviewPackageReadinessReport
        {
            ReportId = "rp_package_readiness_safe",
            SessionId = "rp_session_safe",
            RuntimePackagePrecheck = Args(new { packageCreated = false, deploymentExecuted = false, baseUrl = "<redacted>" }),
            BlockingIssues = ["RuntimePreview metadata simulation is not previewReady."],
            RiskSummary = "Package is blocked by metadata precheck issues.",
            MetadataOnly = true,
            RealResourcesTouched = false
        };

        var raw = JsonSerializer.Serialize(report);

        raw.Should().Contain("\"packageCreated\":false");
        raw.Should().Contain("\"deploymentExecuted\":false");
        raw.Should().Contain("\"packageReviewAllowed\":false");
        raw.Should().NotContain("http://");
        raw.Should().NotContain("external:/");
        raw.Should().NotContain("apiKey");
        raw.Should().NotContain(".cvpkg");
    }

    public static IEnumerable<object[]> CorpusCaseIds()
    {
        return RuntimePreviewScenarioCorpusService.CreateCorpusCases()
            .Select(item => new object[] { item.CaseId });
    }

    public static IEnumerable<object[]> RedactedFlowCaseIds()
    {
        return RuntimePreviewRedactedFlowCorpusService.CreateCases()
            .Select(item => new object[] { item.CaseId });
    }

    private static RuntimePreviewSimulatedExecutionHarness Harness(
        RuntimePreviewSessionStore? sessionStore = null,
        RuntimePreviewAuditTrail? auditTrail = null,
        RuntimePreviewReportArchive? reportArchive = null)
    {
        var catalog = new RuntimePreviewPilotResourceCatalog();
        return new RuntimePreviewSimulatedExecutionHarness(
            sessionStore ?? new RuntimePreviewSessionStore(),
            new RuntimePreviewResourceBroker(catalog),
            new RuntimePreviewPilotReadinessGate(new RuntimePreviewResourceAllowlistResolver()),
            new RuntimePreviewPermissionBroker(),
            auditTrail ?? new RuntimePreviewAuditTrail(),
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
                        ["TemplatePath"] = "external:/blocked-template"
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

    private static string TempDirectory()
    {
        return Path.Combine(Path.GetTempPath(), $"cv-runtime-preview-governance-{Guid.NewGuid():N}");
    }

    private static void DeleteDirectory(string directory)
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
