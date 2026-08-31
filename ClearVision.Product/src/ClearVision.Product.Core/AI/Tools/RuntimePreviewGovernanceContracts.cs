using System.Text.Json;
using System.Text.Json.Serialization;
using ClearVision.Product.Core.Entities;

namespace ClearVision.Product.Core.AI.Tools;

public static class RuntimePreviewSessionStatuses
{
    public const string Created = "created";
    public const string Configured = "configured";
    public const string ReadinessChecked = "readiness_checked";
    public const string Authorized = "authorized";
    public const string Simulated = "simulated";
    public const string Completed = "completed";
    public const string Denied = "denied";
    public const string Failed = "failed";
    public const string Cancelled = "cancelled";
}

public static class RuntimePreviewAuditEventTypes
{
    public const string SessionCreated = "session_created";
    public const string ConfigChanged = "config_changed";
    public const string CatalogLoaded = "catalog_loaded";
    public const string AllowlistChanged = "allowlist_changed";
    public const string ReadinessChecked = "readiness_checked";
    public const string PermissionDenied = "permission_denied";
    public const string PermissionGranted = "permission_granted";
    public const string SimulationStarted = "simulation_started";
    public const string SimulationCompleted = "simulation_completed";
    public const string ReportGenerated = "report_generated";
    public const string SessionReplayed = "session_replayed";
    public const string RetentionCleanup = "retention_cleanup";
    public const string DeployReadinessGenerated = "deploy_readiness_generated";
    public const string PackageReadinessGenerated = "package_readiness_generated";
    public const string ManifestDryRunGenerated = "manifest_dry_run_generated";
    public const string StationCompatibilityGenerated = "station_compatibility_generated";
    public const string OperatorContractValidationGenerated = "operator_contract_validation_generated";
    public const string PreReleaseReviewGenerated = "pre_release_review_generated";
    public const string ScenarioCorpusLoaded = "scenario_corpus_loaded";
    public const string GovernanceExported = "governance_exported";
    public const string CorruptionRecovered = "corruption_recovered";
    public const string SessionCancelled = "session_cancelled";
    public const string SessionFailed = "session_failed";
}

public static class RuntimePreviewScenarioEvidenceStatuses
{
    public const string Passed = "passed";
    public const string NotReady = "not_ready";
    public const string Denied = "denied";
}

public static class RuntimePreviewPermissionStatuses
{
    public const string Allowed = "allowed";
    public const string Denied = "denied";
    public const string NotReady = "not_ready";
}

public sealed record RuntimePreviewPermissionBrokerDecision
{
    [JsonPropertyName("allowed")]
    public bool Allowed { get; init; }

    [JsonPropertyName("status")]
    public string Status { get; init; } = RuntimePreviewPermissionStatuses.Denied;

    [JsonPropertyName("reasonCode")]
    public string ReasonCode { get; init; } = string.Empty;

    [JsonPropertyName("reason")]
    public string Reason { get; init; } = string.Empty;

    [JsonPropertyName("gate")]
    public string Gate { get; init; } = string.Empty;

    [JsonPropertyName("metadataOnly")]
    public bool MetadataOnly { get; init; } = true;

    [JsonPropertyName("runtimePreviewConsent")]
    public bool RuntimePreviewConsent { get; init; }

    [JsonPropertyName("pilotEnabled")]
    public bool PilotEnabled { get; init; }

    [JsonPropertyName("developerUi")]
    public bool DeveloperUi { get; init; }

    [JsonPropertyName("admin")]
    public bool Admin { get; init; }

    [JsonPropertyName("dangerousDenied")]
    public bool DangerousDenied { get; init; }

    [JsonPropertyName("pendingActions")]
    public IReadOnlyList<VisionAgentPendingAction> PendingActions { get; init; } = [];

    public static RuntimePreviewPermissionBrokerDecision Allow(
        string gate,
        string reasonCode,
        string reason,
        bool admin = false,
        bool developerUi = false,
        bool runtimePreviewConsent = true,
        bool pilotEnabled = true)
    {
        return new RuntimePreviewPermissionBrokerDecision
        {
            Allowed = true,
            Status = RuntimePreviewPermissionStatuses.Allowed,
            Gate = gate,
            ReasonCode = reasonCode,
            Reason = reason,
            Admin = admin,
            DeveloperUi = developerUi,
            RuntimePreviewConsent = runtimePreviewConsent,
            PilotEnabled = pilotEnabled,
            MetadataOnly = true
        };
    }

    public static RuntimePreviewPermissionBrokerDecision Deny(
        string gate,
        string reasonCode,
        string reason,
        bool admin = false,
        bool developerUi = false,
        bool runtimePreviewConsent = false,
        bool pilotEnabled = false,
        bool dangerousDenied = false,
        IReadOnlyList<VisionAgentPendingAction>? pendingActions = null)
    {
        return new RuntimePreviewPermissionBrokerDecision
        {
            Allowed = false,
            Status = RuntimePreviewPermissionStatuses.Denied,
            Gate = gate,
            ReasonCode = reasonCode,
            Reason = reason,
            Admin = admin,
            DeveloperUi = developerUi,
            RuntimePreviewConsent = runtimePreviewConsent,
            PilotEnabled = pilotEnabled,
            DangerousDenied = dangerousDenied,
            MetadataOnly = true,
            PendingActions = pendingActions ?? []
        };
    }
}

public sealed record RuntimePreviewResourceHandle
{
    [JsonPropertyName("handleId")]
    public string HandleId { get; init; } = string.Empty;

    [JsonPropertyName("resourceType")]
    public string ResourceType { get; init; } = string.Empty;

    [JsonPropertyName("logicalId")]
    public string LogicalId { get; init; } = string.Empty;

    [JsonPropertyName("displayName")]
    public string DisplayName { get; init; } = string.Empty;

    [JsonPropertyName("source")]
    public string Source { get; init; } = string.Empty;

    [JsonPropertyName("metadataOnly")]
    public bool MetadataOnly { get; init; } = true;

    [JsonPropertyName("safeForPilot")]
    public bool SafeForPilot { get; init; }

    [JsonPropertyName("redacted")]
    public bool Redacted { get; init; }

    [JsonPropertyName("reasonCode")]
    public string ReasonCode { get; init; } = string.Empty;
}

public sealed record RuntimePreviewResourceHandleSet
{
    [JsonPropertyName("catalogSnapshotId")]
    public string CatalogSnapshotId { get; init; } = string.Empty;

    [JsonPropertyName("generatedAtUtc")]
    public DateTimeOffset GeneratedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("handles")]
    public IReadOnlyList<RuntimePreviewResourceHandle> Handles { get; init; } = [];

    [JsonPropertyName("catalog")]
    public RuntimePreviewPilotCatalog Catalog { get; init; } = new();

    [JsonPropertyName("metadataOnly")]
    public bool MetadataOnly { get; init; } = true;

    [JsonPropertyName("realResourcesTouched")]
    public bool RealResourcesTouched { get; init; }
}

public sealed record RuntimePreviewAuditEvent
{
    [JsonPropertyName("eventId")]
    public string EventId { get; init; } = string.Empty;

    [JsonPropertyName("sessionId")]
    public string SessionId { get; init; } = string.Empty;

    [JsonPropertyName("eventType")]
    public string EventType { get; init; } = string.Empty;

    [JsonPropertyName("createdAtUtc")]
    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("payload")]
    public JsonElement Payload { get; init; }

    [JsonPropertyName("redacted")]
    public bool Redacted { get; init; } = true;

    [JsonPropertyName("metadataOnly")]
    public bool MetadataOnly { get; init; } = true;
}

public sealed record RuntimePreviewSession
{
    [JsonPropertyName("sessionId")]
    public string SessionId { get; init; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; init; } = RuntimePreviewSessionStatuses.Created;

    [JsonPropertyName("createdAtUtc")]
    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("updatedAtUtc")]
    public DateTimeOffset UpdatedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("workflowDraftHash")]
    public string WorkflowDraftHash { get; init; } = string.Empty;

    [JsonPropertyName("pilotConfigRevision")]
    public string PilotConfigRevision { get; init; } = string.Empty;

    [JsonPropertyName("catalogSnapshotId")]
    public string CatalogSnapshotId { get; init; } = string.Empty;

    [JsonPropertyName("readinessStatus")]
    public string ReadinessStatus { get; init; } = RuntimePreviewPilotReadinessStatuses.NotReady;

    [JsonPropertyName("permissionStatus")]
    public string PermissionStatus { get; init; } = RuntimePreviewPermissionStatuses.NotReady;

    [JsonPropertyName("auditEventIds")]
    public IReadOnlyList<string> AuditEventIds { get; init; } = [];

    [JsonPropertyName("reportId")]
    public string? ReportId { get; init; }

    [JsonPropertyName("metadataOnly")]
    public bool MetadataOnly { get; init; } = true;

    [JsonPropertyName("realResourcesTouched")]
    public bool RealResourcesTouched { get; init; }
}

public sealed record RuntimePreviewSimulationResult
{
    [JsonPropertyName("sessionId")]
    public string SessionId { get; init; } = string.Empty;

    [JsonPropertyName("success")]
    public bool Success { get; init; }

    [JsonPropertyName("status")]
    public string Status { get; init; } = RuntimePreviewSessionStatuses.Simulated;

    [JsonPropertyName("timeline")]
    public IReadOnlyList<object> Timeline { get; init; } = [];

    [JsonPropertyName("readiness")]
    public RuntimePreviewPilotReadinessResult? Readiness { get; init; }

    [JsonPropertyName("permissionDecision")]
    public RuntimePreviewPermissionBrokerDecision PermissionDecision { get; init; } = new();

    [JsonPropertyName("artifacts")]
    public IReadOnlyList<RuntimePreviewArtifactSummary> Artifacts { get; init; } = [];

    [JsonPropertyName("workflowDraftAllowed")]
    public bool WorkflowDraftAllowed { get; init; } = true;

    [JsonPropertyName("metadataOnly")]
    public bool MetadataOnly { get; init; } = true;

    [JsonPropertyName("realResourcesTouched")]
    public bool RealResourcesTouched { get; init; }
}

public sealed record RuntimePreviewSessionReport
{
    [JsonPropertyName("previewReady")]
    public bool PreviewReady { get; init; }

    [JsonPropertyName("reportId")]
    public string ReportId { get; init; } = string.Empty;

    [JsonPropertyName("sessionId")]
    public string SessionId { get; init; } = string.Empty;

    [JsonPropertyName("generatedAtUtc")]
    public DateTimeOffset GeneratedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("session")]
    public RuntimePreviewSession Session { get; init; } = new();

    [JsonPropertyName("resourceHandles")]
    public IReadOnlyList<RuntimePreviewResourceHandle> ResourceHandles { get; init; } = [];

    [JsonPropertyName("readiness")]
    public RuntimePreviewPilotReadinessResult? Readiness { get; init; }

    [JsonPropertyName("permissionDecision")]
    public RuntimePreviewPermissionBrokerDecision? PermissionDecision { get; init; }

    [JsonPropertyName("auditEvents")]
    public IReadOnlyList<RuntimePreviewAuditEvent> AuditEvents { get; init; } = [];

    [JsonPropertyName("simulation")]
    public RuntimePreviewSimulationResult? Simulation { get; init; }

    [JsonPropertyName("metadataOnly")]
    public bool MetadataOnly { get; init; } = true;

    [JsonPropertyName("realResourcesTouched")]
    public bool RealResourcesTouched { get; init; }
}

public sealed record RuntimePreviewReplayResult
{
    [JsonPropertyName("sessionId")]
    public string SessionId { get; init; } = string.Empty;

    [JsonPropertyName("reportId")]
    public string? ReportId { get; init; }

    [JsonPropertyName("replayedAtUtc")]
    public DateTimeOffset ReplayedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("timeline")]
    public IReadOnlyList<object> Timeline { get; init; } = [];

    [JsonPropertyName("auditEvents")]
    public IReadOnlyList<RuntimePreviewAuditEvent> AuditEvents { get; init; } = [];

    [JsonPropertyName("previewReady")]
    public bool PreviewReady { get; init; }

    [JsonPropertyName("metadataOnly")]
    public bool MetadataOnly { get; init; } = true;

    [JsonPropertyName("realResourcesTouched")]
    public bool RealResourcesTouched { get; init; }
}

public sealed record RuntimePreviewRetentionCleanupResult
{
    [JsonPropertyName("cleanedAtUtc")]
    public DateTimeOffset CleanedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("retentionDays")]
    public int RetentionDays { get; init; }

    [JsonPropertyName("maxSessions")]
    public int MaxSessions { get; init; }

    [JsonPropertyName("sessionsBefore")]
    public int SessionsBefore { get; init; }

    [JsonPropertyName("sessionsAfter")]
    public int SessionsAfter { get; init; }

    [JsonPropertyName("auditEventsBefore")]
    public int AuditEventsBefore { get; init; }

    [JsonPropertyName("auditEventsAfter")]
    public int AuditEventsAfter { get; init; }

    [JsonPropertyName("reportsBefore")]
    public int ReportsBefore { get; init; }

    [JsonPropertyName("reportsAfter")]
    public int ReportsAfter { get; init; }

    [JsonPropertyName("trimmedSessions")]
    public int TrimmedSessions => Math.Max(0, SessionsBefore - SessionsAfter);

    [JsonPropertyName("trimmedRecords")]
    public int TrimmedRecords => Math.Max(0, AuditEventsBefore - AuditEventsAfter) + Math.Max(0, ReportsBefore - ReportsAfter);

    [JsonPropertyName("degraded")]
    public bool Degraded { get; init; }

    [JsonPropertyName("metadataOnly")]
    public bool MetadataOnly { get; init; } = true;

    [JsonPropertyName("realResourcesTouched")]
    public bool RealResourcesTouched { get; init; }
}

public sealed record RuntimePreviewGovernanceStorageIndexSummary
{
    [JsonPropertyName("schemaVersion")]
    public string SchemaVersion { get; init; } = "2026-06-06.runtime-preview-governance-store.v4";

    [JsonPropertyName("storageVersion")]
    public string StorageVersion { get; init; } = "jsonl.v4";

    [JsonPropertyName("storageMode")]
    public string StorageMode { get; init; } = "jsonl";

    [JsonPropertyName("recordTypes")]
    public IReadOnlyList<string> RecordTypes { get; init; } = [];

    [JsonPropertyName("sessionCount")]
    public int SessionCount { get; init; }

    [JsonPropertyName("auditEventCount")]
    public int AuditEventCount { get; init; }

    [JsonPropertyName("sessionReportCount")]
    public int SessionReportCount { get; init; }

    [JsonPropertyName("deployReadinessReportCount")]
    public int DeployReadinessReportCount { get; init; }

    [JsonPropertyName("packageReadinessReportCount")]
    public int PackageReadinessReportCount { get; init; }

    [JsonPropertyName("manifestDryRunReportCount")]
    public int ManifestDryRunReportCount { get; init; }

    [JsonPropertyName("stationCompatibilityReportCount")]
    public int StationCompatibilityReportCount { get; init; }

    [JsonPropertyName("operatorContractValidationReportCount")]
    public int OperatorContractValidationReportCount { get; init; }

    [JsonPropertyName("preReleaseReviewReportCount")]
    public int PreReleaseReviewReportCount { get; init; }

    [JsonPropertyName("releaseReviewDecisionCount")]
    public int ReleaseReviewDecisionCount { get; init; }

    [JsonPropertyName("stationProfileSnapshotCount")]
    public int StationProfileSnapshotCount { get; init; }

    [JsonPropertyName("operatorContractRegistrySnapshotCount")]
    public int OperatorContractRegistrySnapshotCount { get; init; }

    [JsonPropertyName("operatorContractCoverageReportCount")]
    public int OperatorContractCoverageReportCount { get; init; }

    [JsonPropertyName("finalGovernanceExportCount")]
    public int FinalGovernanceExportCount { get; init; }

    [JsonPropertyName("agentRunEventCount")]
    public int AgentRunEventCount { get; init; }

    [JsonPropertyName("agentRunSummaryCount")]
    public int AgentRunSummaryCount { get; init; }

    [JsonPropertyName("agentRunAuditFileCount")]
    public int AgentRunAuditFileCount { get; init; }

    [JsonPropertyName("corruptLineCount")]
    public int CorruptLineCount { get; init; }

    [JsonPropertyName("retentionPolicy")]
    public string RetentionPolicy { get; init; } = "default_30_days_200_sessions";

    [JsonPropertyName("metadataOnly")]
    public bool MetadataOnly { get; init; } = true;

    [JsonPropertyName("realResourcesTouched")]
    public bool RealResourcesTouched { get; init; }
}

public sealed record RuntimePreviewGovernanceExportManifest
{
    [JsonPropertyName("schemaVersion")]
    public string SchemaVersion { get; init; } = "2026-06-06.runtime-preview-governance-export.v4";

    [JsonPropertyName("exportId")]
    public string ExportId { get; init; } = string.Empty;

    [JsonPropertyName("generatedAtUtc")]
    public DateTimeOffset GeneratedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("indexSummary")]
    public RuntimePreviewGovernanceStorageIndexSummary IndexSummary { get; init; } = new();

    [JsonPropertyName("sessions")]
    public IReadOnlyList<RuntimePreviewSession> Sessions { get; init; } = [];

    [JsonPropertyName("auditEvents")]
    public IReadOnlyList<RuntimePreviewAuditEvent> AuditEvents { get; init; } = [];

    [JsonPropertyName("sessionReports")]
    public IReadOnlyList<RuntimePreviewSessionReport> SessionReports { get; init; } = [];

    [JsonPropertyName("deployReadinessReports")]
    public IReadOnlyList<RuntimePreviewDeployReadinessReport> DeployReadinessReports { get; init; } = [];

    [JsonPropertyName("packageReadinessReports")]
    public IReadOnlyList<RuntimePreviewPackageReadinessReport> PackageReadinessReports { get; init; } = [];

    [JsonPropertyName("manifestDryRunReports")]
    public IReadOnlyList<RuntimePackageManifestDryRunReport> ManifestDryRunReports { get; init; } = [];

    [JsonPropertyName("stationCompatibilityReports")]
    public IReadOnlyList<RuntimePreviewStationCompatibilityReport> StationCompatibilityReports { get; init; } = [];

    [JsonPropertyName("operatorContractValidationReports")]
    public IReadOnlyList<RuntimePreviewOperatorContractValidationReport> OperatorContractValidationReports { get; init; } = [];

    [JsonPropertyName("preReleaseReviewReports")]
    public IReadOnlyList<RuntimePreviewPreReleaseReviewReport> PreReleaseReviewReports { get; init; } = [];

    [JsonPropertyName("releaseReviewDecisions")]
    public IReadOnlyList<RuntimePreviewReleaseReadinessDecisionMatrix> ReleaseReviewDecisions { get; init; } = [];

    [JsonPropertyName("stationProfileSnapshots")]
    public IReadOnlyList<RuntimePreviewStationProfileDocument> StationProfileSnapshots { get; init; } = [];

    [JsonPropertyName("operatorContractRegistrySnapshots")]
    public IReadOnlyList<RuntimePreviewOperatorContractRegistryDocument> OperatorContractRegistrySnapshots { get; init; } = [];

    [JsonPropertyName("operatorContractCoverageReports")]
    public IReadOnlyList<RuntimePreviewOperatorContractCoverageReport> OperatorContractCoverageReports { get; init; } = [];

    [JsonPropertyName("finalGovernanceExports")]
    public IReadOnlyList<RuntimePreviewGovernanceExportManifest> FinalGovernanceExports { get; init; } = [];

    [JsonPropertyName("agentRunAuditFiles")]
    public IReadOnlyList<string> AgentRunAuditFiles { get; init; } = [];

    [JsonPropertyName("redactionPass")]
    public bool RedactionPass { get; init; } = true;

    [JsonPropertyName("metadataOnly")]
    public bool MetadataOnly { get; init; } = true;

    [JsonPropertyName("realResourcesTouched")]
    public bool RealResourcesTouched { get; init; }
}

public sealed record RuntimePreviewDeployReadinessReport
{
    [JsonPropertyName("reportId")]
    public string ReportId { get; init; } = string.Empty;

    [JsonPropertyName("sessionId")]
    public string SessionId { get; init; } = string.Empty;

    [JsonPropertyName("generatedAtUtc")]
    public DateTimeOffset GeneratedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("workflowDraftHash")]
    public string WorkflowDraftHash { get; init; } = string.Empty;

    [JsonPropertyName("previewReportId")]
    public string PreviewReportId { get; init; } = string.Empty;

    [JsonPropertyName("previewReady")]
    public bool PreviewReady { get; init; }

    [JsonPropertyName("readyForDeployment")]
    public bool ReadyForDeployment { get; init; }

    [JsonPropertyName("deploymentBlocked")]
    public bool DeploymentBlocked { get; init; } = true;

    [JsonPropertyName("workflowDraftAllowed")]
    public bool WorkflowDraftAllowed { get; init; } = true;

    [JsonPropertyName("readiness")]
    public RuntimePreviewPilotReadinessResult? Readiness { get; init; }

    [JsonPropertyName("simulationReport")]
    public RuntimePreviewSessionReport? SimulationReport { get; init; }

    [JsonPropertyName("runtimePackagePrecheck")]
    public JsonElement RuntimePackagePrecheck { get; init; }

    [JsonPropertyName("resourceHandles")]
    public IReadOnlyList<RuntimePreviewResourceHandle> ResourceHandles { get; init; } = [];

    [JsonPropertyName("pendingActions")]
    public IReadOnlyList<VisionAgentPendingAction> PendingActions { get; init; } = [];

    [JsonPropertyName("metadataOnly")]
    public bool MetadataOnly { get; init; } = true;

    [JsonPropertyName("packageCreated")]
    public bool PackageCreated { get; init; }

    [JsonPropertyName("deploymentExecuted")]
    public bool DeploymentExecuted { get; init; }

    [JsonPropertyName("realResourcesTouched")]
    public bool RealResourcesTouched { get; init; }
}

public sealed record RuntimePreviewPackageReadinessReport
{
    [JsonPropertyName("reportId")]
    public string ReportId { get; init; } = string.Empty;

    [JsonPropertyName("sessionId")]
    public string SessionId { get; init; } = string.Empty;

    [JsonPropertyName("generatedAtUtc")]
    public DateTimeOffset GeneratedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("workflowDraftHash")]
    public string WorkflowDraftHash { get; init; } = string.Empty;

    [JsonPropertyName("previewReportId")]
    public string PreviewReportId { get; init; } = string.Empty;

    [JsonPropertyName("deployReadinessReportId")]
    public string DeployReadinessReportId { get; init; } = string.Empty;

    [JsonPropertyName("readyForPackage")]
    public bool ReadyForPackage { get; init; }

    [JsonPropertyName("packageReviewAllowed")]
    public bool PackageReviewAllowed { get; init; }

    [JsonPropertyName("packageBlocked")]
    public bool PackageBlocked { get; init; } = true;

    [JsonPropertyName("packageCreated")]
    public bool PackageCreated { get; init; }

    [JsonPropertyName("deploymentExecuted")]
    public bool DeploymentExecuted { get; init; }

    [JsonPropertyName("blockingIssues")]
    public IReadOnlyList<string> BlockingIssues { get; init; } = [];

    [JsonPropertyName("blockedReason")]
    public string BlockedReason { get; init; } = string.Empty;

    [JsonPropertyName("missingResources")]
    public IReadOnlyList<object> MissingResources { get; init; } = [];

    [JsonPropertyName("riskSummary")]
    public string RiskSummary { get; init; } = string.Empty;

    [JsonPropertyName("packageRiskLevel")]
    public string PackageRiskLevel { get; init; } = "unknown";

    [JsonPropertyName("packageReviewExplanation")]
    public string PackageReviewExplanation { get; init; } = string.Empty;

    [JsonPropertyName("manifestDryRunReportId")]
    public string ManifestDryRunReportId { get; init; } = string.Empty;

    [JsonPropertyName("pendingActions")]
    public IReadOnlyList<VisionAgentPendingAction> PendingActions { get; init; } = [];

    [JsonPropertyName("operatorTrace")]
    public IReadOnlyList<string> OperatorTrace { get; init; } = [];

    [JsonPropertyName("resourceTrace")]
    public IReadOnlyList<string> ResourceTrace { get; init; } = [];

    [JsonPropertyName("dependencyTrace")]
    public IReadOnlyList<string> DependencyTrace { get; init; } = [];

    [JsonPropertyName("operatorContract")]
    public IReadOnlyList<string> OperatorContract { get; init; } = [];

    [JsonPropertyName("resourceContract")]
    public IReadOnlyList<string> ResourceContract { get; init; } = [];

    [JsonPropertyName("workflowDraftAllowed")]
    public bool WorkflowDraftAllowed { get; init; } = true;

    [JsonPropertyName("runtimePackagePrecheck")]
    public JsonElement RuntimePackagePrecheck { get; init; }

    [JsonPropertyName("metadataOnly")]
    public bool MetadataOnly { get; init; } = true;

    [JsonPropertyName("realResourcesTouched")]
    public bool RealResourcesTouched { get; init; }
}

public sealed record RuntimePackageManifestDryRunReport
{
    [JsonPropertyName("schemaVersion")]
    public string SchemaVersion { get; init; } = "2026-06-06.runtime-package-manifest-dry-run.v1";

    [JsonPropertyName("manifestId")]
    public string ManifestId { get; init; } = string.Empty;

    [JsonPropertyName("reportId")]
    public string ReportId { get; init; } = string.Empty;

    [JsonPropertyName("sessionId")]
    public string SessionId { get; init; } = string.Empty;

    [JsonPropertyName("packageReadinessReportId")]
    public string PackageReadinessReportId { get; init; } = string.Empty;

    [JsonPropertyName("generatedAtUtc")]
    public DateTimeOffset GeneratedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("workflowDraftHash")]
    public string WorkflowDraftHash { get; init; } = string.Empty;

    [JsonPropertyName("manifestHash")]
    public string ManifestHash { get; init; } = string.Empty;

    [JsonPropertyName("operatorCount")]
    public int OperatorCount { get; init; }

    [JsonPropertyName("operatorTypes")]
    public IReadOnlyList<string> OperatorTypes { get; init; } = [];

    [JsonPropertyName("resourceDependencies")]
    public IReadOnlyList<string> ResourceDependencies { get; init; } = [];

    [JsonPropertyName("modelDependencies")]
    public IReadOnlyList<string> ModelDependencies { get; init; } = [];

    [JsonPropertyName("templateDependencies")]
    public IReadOnlyList<string> TemplateDependencies { get; init; } = [];

    [JsonPropertyName("cameraBindings")]
    public IReadOnlyList<string> CameraBindings { get; init; } = [];

    [JsonPropertyName("outputChannels")]
    public IReadOnlyList<string> OutputChannels { get; init; } = [];

    [JsonPropertyName("missingDependencies")]
    public IReadOnlyList<string> MissingDependencies { get; init; } = [];

    [JsonPropertyName("blockedReasons")]
    public IReadOnlyList<string> BlockedReasons { get; init; } = [];

    [JsonPropertyName("dependencyTrace")]
    public IReadOnlyList<string> DependencyTrace { get; init; } = [];

    [JsonPropertyName("operatorTrace")]
    public IReadOnlyList<string> OperatorTrace { get; init; } = [];

    [JsonPropertyName("resourceTrace")]
    public IReadOnlyList<string> ResourceTrace { get; init; } = [];

    [JsonPropertyName("riskLevel")]
    public string RiskLevel { get; init; } = "unknown";

    [JsonPropertyName("packageReviewAllowed")]
    public bool PackageReviewAllowed { get; init; }

    [JsonPropertyName("workflowDraftAllowed")]
    public bool WorkflowDraftAllowed { get; init; } = true;

    [JsonPropertyName("manifestArtifactGenerated")]
    public bool ManifestArtifactGenerated { get; init; }

    [JsonPropertyName("packageCreated")]
    public bool PackageCreated { get; init; }

    [JsonPropertyName("deploymentExecuted")]
    public bool DeploymentExecuted { get; init; }

    [JsonPropertyName("metadataOnly")]
    public bool MetadataOnly { get; init; } = true;

    [JsonPropertyName("realResourcesTouched")]
    public bool RealResourcesTouched { get; init; }
}

public sealed record RuntimePreviewStationProfileResourcePolicy
{
    [JsonPropertyName("metadataOnly")]
    public bool MetadataOnly { get; init; } = true;

    [JsonPropertyName("realResourceAccessAllowed")]
    public bool RealResourceAccessAllowed { get; init; }

    [JsonPropertyName("imageFileReadAllowed")]
    public bool ImageFileReadAllowed { get; init; }

    [JsonPropertyName("modelFileLoadAllowed")]
    public bool ModelFileLoadAllowed { get; init; }

    [JsonPropertyName("templateFileReadAllowed")]
    public bool TemplateFileReadAllowed { get; init; }

    [JsonPropertyName("packageDeploymentAllowed")]
    public bool PackageDeploymentAllowed { get; init; }
}

public sealed record RuntimePreviewStationProfile
{
    [JsonPropertyName("stationProfileId")]
    public string StationProfileId { get; init; } = string.Empty;

    [JsonPropertyName("stationType")]
    public string StationType { get; init; } = string.Empty;

    [JsonPropertyName("runtimeVersion")]
    public string RuntimeVersion { get; init; } = string.Empty;

    [JsonPropertyName("supportedOperatorTypes")]
    public IReadOnlyList<string> SupportedOperatorTypes { get; init; } = [];

    [JsonPropertyName("supportedModelKinds")]
    public IReadOnlyList<string> SupportedModelKinds { get; init; } = [];

    [JsonPropertyName("cameraBindingSlots")]
    public IReadOnlyList<string> CameraBindingSlots { get; init; } = [];

    [JsonPropertyName("outputChannelKinds")]
    public IReadOnlyList<string> OutputChannelKinds { get; init; } = [];

    [JsonPropertyName("maxOperatorCount")]
    public int MaxOperatorCount { get; init; }

    [JsonPropertyName("plcWriteAllowed")]
    public bool PlcWriteAllowed { get; init; }

    [JsonPropertyName("resourcePolicy")]
    public RuntimePreviewStationProfileResourcePolicy ResourcePolicy { get; init; } = new();

    [JsonPropertyName("networkPolicy")]
    public string NetworkPolicy { get; init; } = "redacted";

    [JsonPropertyName("approvalPolicy")]
    public string ApprovalPolicy { get; init; } = "metadata_engineer_review";

    [JsonPropertyName("riskPolicy")]
    public string RiskPolicy { get; init; } = "fail_closed_metadata_only";

    [JsonPropertyName("metadataOnly")]
    public bool MetadataOnly { get; init; } = true;

    [JsonPropertyName("realResourcesTouched")]
    public bool RealResourcesTouched { get; init; }
}

public sealed record RuntimePreviewStationProfileDocument
{
    [JsonPropertyName("schemaVersion")]
    public string SchemaVersion { get; init; } = "2026-06-07.runtime-preview-station-profiles.final.v1";

    [JsonPropertyName("generatedAtUtc")]
    public DateTimeOffset GeneratedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("profileCount")]
    public int ProfileCount { get; init; }

    [JsonPropertyName("profiles")]
    public IReadOnlyList<RuntimePreviewStationProfile> Profiles { get; init; } = [];

    [JsonPropertyName("metadataOnly")]
    public bool MetadataOnly { get; init; } = true;

    [JsonPropertyName("realResourcesTouched")]
    public bool RealResourcesTouched { get; init; }
}

public sealed record RuntimePreviewOperatorContractDefinition
{
    [JsonPropertyName("operatorType")]
    public string OperatorType { get; init; } = string.Empty;

    [JsonPropertyName("requiredInputs")]
    public IReadOnlyList<string> RequiredInputs { get; init; } = [];

    [JsonPropertyName("requiredOutputs")]
    public IReadOnlyList<string> RequiredOutputs { get; init; } = [];

    [JsonPropertyName("requiredParameters")]
    public IReadOnlyList<string> RequiredParameters { get; init; } = [];

    [JsonPropertyName("optionalParameters")]
    public IReadOnlyList<string> OptionalParameters { get; init; } = [];

    [JsonPropertyName("resourceDependencies")]
    public IReadOnlyList<string> ResourceDependencies { get; init; } = [];

    [JsonPropertyName("forbiddenParameters")]
    public IReadOnlyList<string> ForbiddenParameters { get; init; } = [];

    [JsonPropertyName("runtimeDependencies")]
    public IReadOnlyList<string> RuntimeDependencies { get; init; } = [];

    [JsonPropertyName("manifestFields")]
    public IReadOnlyList<string> ManifestFields { get; init; } = [];

    [JsonPropertyName("stationCompatibilityRequirements")]
    public IReadOnlyList<string> StationCompatibilityRequirements { get; init; } = [];

    [JsonPropertyName("riskTags")]
    public IReadOnlyList<string> RiskTags { get; init; } = [];

    [JsonPropertyName("approvalRequirements")]
    public IReadOnlyList<string> ApprovalRequirements { get; init; } = [];

    [JsonPropertyName("packageReviewRules")]
    public IReadOnlyList<string> PackageReviewRules { get; init; } = [];

    [JsonPropertyName("metadataOnly")]
    public bool MetadataOnly { get; init; } = true;
}

public sealed record RuntimePreviewOperatorContractRegistryDocument
{
    [JsonPropertyName("schemaVersion")]
    public string SchemaVersion { get; init; } = "2026-06-07.runtime-preview-operator-contract-registry.final.v1";

    [JsonPropertyName("operatorContractVersion")]
    public string OperatorContractVersion { get; init; } = "operator-contract-registry.final.metadata-only";

    [JsonPropertyName("generatedAtUtc")]
    public DateTimeOffset GeneratedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("contractCount")]
    public int ContractCount { get; init; }

    [JsonPropertyName("contracts")]
    public IReadOnlyList<RuntimePreviewOperatorContractDefinition> Contracts { get; init; } = [];

    [JsonPropertyName("metadataOnly")]
    public bool MetadataOnly { get; init; } = true;

    [JsonPropertyName("realResourcesTouched")]
    public bool RealResourcesTouched { get; init; }
}

public sealed record RuntimePreviewOperatorContractCoverageReport
{
    [JsonPropertyName("schemaVersion")]
    public string SchemaVersion { get; init; } = "2026-06-07.runtime-preview-operator-contract-coverage.final.v1";

    [JsonPropertyName("reportId")]
    public string ReportId { get; init; } = string.Empty;

    [JsonPropertyName("operatorContractVersion")]
    public string OperatorContractVersion { get; init; } = "operator-contract-registry.final.metadata-only";

    [JsonPropertyName("generatedAtUtc")]
    public DateTimeOffset GeneratedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("coveredOperatorTypes")]
    public IReadOnlyList<string> CoveredOperatorTypes { get; init; } = [];

    [JsonPropertyName("missingOperatorTypes")]
    public IReadOnlyList<string> MissingOperatorTypes { get; init; } = [];

    [JsonPropertyName("contractCount")]
    public int ContractCount { get; init; }

    [JsonPropertyName("coveragePass")]
    public bool CoveragePass { get; init; }

    [JsonPropertyName("metadataOnly")]
    public bool MetadataOnly { get; init; } = true;

    [JsonPropertyName("realResourcesTouched")]
    public bool RealResourcesTouched { get; init; }
}

public sealed record RuntimePreviewOperatorContractValidationItem
{
    [JsonPropertyName("operatorTempId")]
    public string OperatorTempId { get; init; } = string.Empty;

    [JsonPropertyName("operatorType")]
    public string OperatorType { get; init; } = string.Empty;

    [JsonPropertyName("contractSatisfied")]
    public bool ContractSatisfied { get; init; }

    [JsonPropertyName("requiredInputs")]
    public IReadOnlyList<string> RequiredInputs { get; init; } = [];

    [JsonPropertyName("requiredOutputs")]
    public IReadOnlyList<string> RequiredOutputs { get; init; } = [];

    [JsonPropertyName("requiredParameters")]
    public IReadOnlyList<string> RequiredParameters { get; init; } = [];

    [JsonPropertyName("missingParameters")]
    public IReadOnlyList<string> MissingParameters { get; init; } = [];

    [JsonPropertyName("resourceDependencies")]
    public IReadOnlyList<string> ResourceDependencies { get; init; } = [];

    [JsonPropertyName("forbiddenParameterHits")]
    public IReadOnlyList<string> ForbiddenParameterHits { get; init; } = [];

    [JsonPropertyName("runtimeDependencies")]
    public IReadOnlyList<string> RuntimeDependencies { get; init; } = [];

    [JsonPropertyName("manifestFields")]
    public IReadOnlyList<string> ManifestFields { get; init; } = [];

    [JsonPropertyName("stationCompatibilityRequirements")]
    public IReadOnlyList<string> StationCompatibilityRequirements { get; init; } = [];

    [JsonPropertyName("riskTags")]
    public IReadOnlyList<string> RiskTags { get; init; } = [];

    [JsonPropertyName("blockedReasons")]
    public IReadOnlyList<string> BlockedReasons { get; init; } = [];
}

public sealed record RuntimePreviewOperatorContractValidationReport
{
    [JsonPropertyName("schemaVersion")]
    public string SchemaVersion { get; init; } = "2026-06-07.runtime-preview-operator-contract-validation.final.v1";

    [JsonPropertyName("reportId")]
    public string ReportId { get; init; } = string.Empty;

    [JsonPropertyName("sessionId")]
    public string SessionId { get; init; } = string.Empty;

    [JsonPropertyName("caseId")]
    public string CaseId { get; init; } = string.Empty;

    [JsonPropertyName("manifestId")]
    public string ManifestId { get; init; } = string.Empty;

    [JsonPropertyName("stationProfileId")]
    public string StationProfileId { get; init; } = string.Empty;

    [JsonPropertyName("workflowDraftHash")]
    public string WorkflowDraftHash { get; init; } = string.Empty;

    [JsonPropertyName("operatorContractVersion")]
    public string OperatorContractVersion { get; init; } = "operator-contract-registry.final.metadata-only";

    [JsonPropertyName("generatedAtUtc")]
    public DateTimeOffset GeneratedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("operatorContractsSatisfied")]
    public bool OperatorContractsSatisfied { get; init; }

    [JsonPropertyName("contractResults")]
    public IReadOnlyList<RuntimePreviewOperatorContractValidationItem> ContractResults { get; init; } = [];

    [JsonPropertyName("blockedReasons")]
    public IReadOnlyList<string> BlockedReasons { get; init; } = [];

    [JsonPropertyName("riskTags")]
    public IReadOnlyList<string> RiskTags { get; init; } = [];

    [JsonPropertyName("requiredEngineerApprovals")]
    public IReadOnlyList<string> RequiredEngineerApprovals { get; init; } = [];

    [JsonPropertyName("metadataOnly")]
    public bool MetadataOnly { get; init; } = true;

    [JsonPropertyName("packageCreated")]
    public bool PackageCreated { get; init; }

    [JsonPropertyName("deploymentExecuted")]
    public bool DeploymentExecuted { get; init; }

    [JsonPropertyName("realResourcesTouched")]
    public bool RealResourcesTouched { get; init; }
}

public sealed record RuntimePreviewStationCompatibilityReport
{
    [JsonPropertyName("schemaVersion")]
    public string SchemaVersion { get; init; } = "2026-06-07.runtime-preview-station-compatibility-dry-run.final.v1";

    [JsonPropertyName("reportId")]
    public string ReportId { get; init; } = string.Empty;

    [JsonPropertyName("sessionId")]
    public string SessionId { get; init; } = string.Empty;

    [JsonPropertyName("caseId")]
    public string CaseId { get; init; } = string.Empty;

    [JsonPropertyName("manifestId")]
    public string ManifestId { get; init; } = string.Empty;

    [JsonPropertyName("stationProfileId")]
    public string StationProfileId { get; init; } = string.Empty;

    [JsonPropertyName("workflowDraftHash")]
    public string WorkflowDraftHash { get; init; } = string.Empty;

    [JsonPropertyName("generatedAtUtc")]
    public DateTimeOffset GeneratedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("stationProfile")]
    public RuntimePreviewStationProfile StationProfile { get; init; } = new();

    [JsonPropertyName("stationCompatible")]
    public bool StationCompatible { get; init; }

    [JsonPropertyName("runtimeVersionCompatible")]
    public bool RuntimeVersionCompatible { get; init; }

    [JsonPropertyName("operatorSupportCompatible")]
    public bool OperatorSupportCompatible { get; init; }

    [JsonPropertyName("cameraSlotsCompatible")]
    public bool CameraSlotsCompatible { get; init; }

    [JsonPropertyName("outputChannelsCompatible")]
    public bool OutputChannelsCompatible { get; init; }

    [JsonPropertyName("modelTemplateDependenciesCompatible")]
    public bool ModelTemplateDependenciesCompatible { get; init; }

    [JsonPropertyName("operatorCountCompatible")]
    public bool OperatorCountCompatible { get; init; }

    [JsonPropertyName("plcStationIntentCompatible")]
    public bool PlcStationIntentCompatible { get; init; }

    [JsonPropertyName("manifestRiskCompatible")]
    public bool ManifestRiskCompatible { get; init; }

    [JsonPropertyName("requiredRuntimeVersion")]
    public string RequiredRuntimeVersion { get; init; } = string.Empty;

    [JsonPropertyName("blockedReasons")]
    public IReadOnlyList<string> BlockedReasons { get; init; } = [];

    [JsonPropertyName("riskLevel")]
    public string RiskLevel { get; init; } = "unknown";

    [JsonPropertyName("engineerActions")]
    public IReadOnlyList<string> EngineerActions { get; init; } = [];

    [JsonPropertyName("metadataOnly")]
    public bool MetadataOnly { get; init; } = true;

    [JsonPropertyName("packageCreated")]
    public bool PackageCreated { get; init; }

    [JsonPropertyName("deploymentExecuted")]
    public bool DeploymentExecuted { get; init; }

    [JsonPropertyName("realResourcesTouched")]
    public bool RealResourcesTouched { get; init; }
}

public sealed record RuntimePreviewReleaseReadinessDecision
{
    [JsonPropertyName("decisionType")]
    public string DecisionType { get; init; } = string.Empty;

    [JsonPropertyName("reason")]
    public string Reason { get; init; } = string.Empty;

    [JsonPropertyName("nextAction")]
    public string NextAction { get; init; } = string.Empty;

    [JsonPropertyName("engineerApprovalRequired")]
    public bool EngineerApprovalRequired { get; init; }

    [JsonPropertyName("workflowDraftAllowed")]
    public bool WorkflowDraftAllowed { get; init; } = true;

    [JsonPropertyName("packageReviewAllowed")]
    public bool PackageReviewAllowed { get; init; }

    [JsonPropertyName("releaseReviewAllowed")]
    public bool ReleaseReviewAllowed { get; init; }
}

public sealed record RuntimePreviewReleaseReadinessDecisionMatrix
{
    [JsonPropertyName("schemaVersion")]
    public string SchemaVersion { get; init; } = "2026-06-07.runtime-preview-release-readiness-decision-matrix.final.v1";

    [JsonPropertyName("reportId")]
    public string ReportId { get; init; } = string.Empty;

    [JsonPropertyName("reviewId")]
    public string ReviewId { get; init; } = string.Empty;

    [JsonPropertyName("caseId")]
    public string CaseId { get; init; } = string.Empty;

    [JsonPropertyName("manifestId")]
    public string ManifestId { get; init; } = string.Empty;

    [JsonPropertyName("stationProfileId")]
    public string StationProfileId { get; init; } = string.Empty;

    [JsonPropertyName("generatedAtUtc")]
    public DateTimeOffset GeneratedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("goNoGoDecision")]
    public string GoNoGoDecision { get; init; } = "blocked";

    [JsonPropertyName("releaseAllowed")]
    public RuntimePreviewReleaseReadinessDecision ReleaseAllowed { get; init; } = new() { DecisionType = "releaseAllowed" };

    [JsonPropertyName("requiresEngineerApproval")]
    public RuntimePreviewReleaseReadinessDecision RequiresEngineerApproval { get; init; } = new() { DecisionType = "requiresEngineerApproval" };

    [JsonPropertyName("blocked")]
    public RuntimePreviewReleaseReadinessDecision Blocked { get; init; } = new() { DecisionType = "blocked" };

    [JsonPropertyName("forbiddenIntentDenied")]
    public RuntimePreviewReleaseReadinessDecision ForbiddenIntentDenied { get; init; } = new() { DecisionType = "forbiddenIntentDenied" };

    [JsonPropertyName("metadataIncomplete")]
    public RuntimePreviewReleaseReadinessDecision MetadataIncomplete { get; init; } = new() { DecisionType = "metadataIncomplete" };

    [JsonPropertyName("stationIncompatible")]
    public RuntimePreviewReleaseReadinessDecision StationIncompatible { get; init; } = new() { DecisionType = "stationIncompatible" };

    [JsonPropertyName("operatorContractFailed")]
    public RuntimePreviewReleaseReadinessDecision OperatorContractFailed { get; init; } = new() { DecisionType = "operatorContractFailed" };

    [JsonPropertyName("manifestRiskBlocked")]
    public RuntimePreviewReleaseReadinessDecision ManifestRiskBlocked { get; init; } = new() { DecisionType = "manifestRiskBlocked" };

    [JsonPropertyName("packageReviewBlocked")]
    public RuntimePreviewReleaseReadinessDecision PackageReviewBlocked { get; init; } = new() { DecisionType = "packageReviewBlocked" };

    [JsonPropertyName("metadataOnly")]
    public bool MetadataOnly { get; init; } = true;

    [JsonPropertyName("packageCreated")]
    public bool PackageCreated { get; init; }

    [JsonPropertyName("deploymentExecuted")]
    public bool DeploymentExecuted { get; init; }

    [JsonPropertyName("realResourcesTouched")]
    public bool RealResourcesTouched { get; init; }
}

public sealed record RuntimePreviewPreReleaseReviewReport
{
    [JsonPropertyName("schemaVersion")]
    public string SchemaVersion { get; init; } = "2026-06-07.runtime-preview-pre-release-review.final.v1";

    [JsonPropertyName("reviewId")]
    public string ReviewId { get; init; } = string.Empty;

    [JsonPropertyName("caseId")]
    public string CaseId { get; init; } = string.Empty;

    [JsonPropertyName("sessionId")]
    public string SessionId { get; init; } = string.Empty;

    [JsonPropertyName("workflowDraftHash")]
    public string WorkflowDraftHash { get; init; } = string.Empty;

    [JsonPropertyName("manifestId")]
    public string ManifestId { get; init; } = string.Empty;

    [JsonPropertyName("stationProfileId")]
    public string StationProfileId { get; init; } = string.Empty;

    [JsonPropertyName("operatorContractVersion")]
    public string OperatorContractVersion { get; init; } = "operator-contract-registry.final.metadata-only";

    [JsonPropertyName("readinessStatus")]
    public string ReadinessStatus { get; init; } = RuntimePreviewPilotReadinessStatuses.NotReady;

    [JsonPropertyName("packageReviewAllowed")]
    public bool PackageReviewAllowed { get; init; }

    [JsonPropertyName("stationCompatible")]
    public bool StationCompatible { get; init; }

    [JsonPropertyName("operatorContractsSatisfied")]
    public bool OperatorContractsSatisfied { get; init; }

    [JsonPropertyName("releaseReviewAllowed")]
    public bool ReleaseReviewAllowed { get; init; }

    [JsonPropertyName("requiresEngineerApproval")]
    public bool RequiresEngineerApproval { get; init; }

    [JsonPropertyName("goNoGoDecision")]
    public string GoNoGoDecision { get; init; } = "blocked";

    [JsonPropertyName("blockedReasons")]
    public IReadOnlyList<string> BlockedReasons { get; init; } = [];

    [JsonPropertyName("riskLevel")]
    public string RiskLevel { get; init; } = "unknown";

    [JsonPropertyName("engineerActions")]
    public IReadOnlyList<string> EngineerActions { get; init; } = [];

    [JsonPropertyName("firstFixRecommendation")]
    public string FirstFixRecommendation { get; init; } = string.Empty;

    [JsonPropertyName("workflowDraftAllowed")]
    public bool WorkflowDraftAllowed { get; init; } = true;

    [JsonPropertyName("decisionMatrix")]
    public RuntimePreviewReleaseReadinessDecisionMatrix DecisionMatrix { get; init; } = new();

    [JsonPropertyName("packageReadinessReportId")]
    public string PackageReadinessReportId { get; init; } = string.Empty;

    [JsonPropertyName("stationCompatibilityReportId")]
    public string StationCompatibilityReportId { get; init; } = string.Empty;

    [JsonPropertyName("operatorContractValidationReportId")]
    public string OperatorContractValidationReportId { get; init; } = string.Empty;

    [JsonPropertyName("generatedAtUtc")]
    public DateTimeOffset GeneratedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("metadataOnly")]
    public bool MetadataOnly { get; init; } = true;

    [JsonPropertyName("packageCreated")]
    public bool PackageCreated { get; init; }

    [JsonPropertyName("deploymentExecuted")]
    public bool DeploymentExecuted { get; init; }

    [JsonPropertyName("realResourcesTouched")]
    public bool RealResourcesTouched { get; init; }
}

public sealed record RuntimePreviewScenarioCorpusCase
{
    [JsonPropertyName("caseId")]
    public string CaseId { get; init; } = string.Empty;

    [JsonPropertyName("scenario")]
    public string Scenario { get; init; } = string.Empty;

    [JsonPropertyName("workflowDraftHash")]
    public string WorkflowDraftHash { get; init; } = string.Empty;

    [JsonPropertyName("expectedStatus")]
    public string ExpectedStatus { get; init; } = RuntimePreviewScenarioEvidenceStatuses.Passed;

    [JsonPropertyName("expectedRisk")]
    public string ExpectedRisk { get; init; } = string.Empty;

    [JsonPropertyName("expectedPendingActions")]
    public IReadOnlyList<string> ExpectedPendingActions { get; init; } = [];

    [JsonPropertyName("businessExplanation")]
    public string BusinessExplanation { get; init; } = string.Empty;

    [JsonPropertyName("workflowDraft")]
    public JsonElement WorkflowDraft { get; init; }

    [JsonPropertyName("metadataOnly")]
    public bool MetadataOnly { get; init; } = true;

    [JsonPropertyName("realResourcesTouched")]
    public bool RealResourcesTouched { get; init; }
}

public sealed record RuntimePreviewScenarioCorpusDocument
{
    [JsonPropertyName("schemaVersion")]
    public string SchemaVersion { get; init; } = "2026-06-06.runtime-preview-scenario-corpus.v1";

    [JsonPropertyName("generatedAtUtc")]
    public DateTimeOffset GeneratedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("caseCount")]
    public int CaseCount { get; init; }

    [JsonPropertyName("cases")]
    public IReadOnlyList<RuntimePreviewScenarioCorpusCase> Cases { get; init; } = [];

    [JsonPropertyName("metadataOnly")]
    public bool MetadataOnly { get; init; } = true;

    [JsonPropertyName("realResourcesTouched")]
    public bool RealResourcesTouched { get; init; }
}

public sealed record RuntimePreviewRedactedFlowCorpusCase
{
    [JsonPropertyName("caseId")]
    public string CaseId { get; init; } = string.Empty;

    [JsonPropertyName("stationType")]
    public string StationType { get; init; } = string.Empty;

    [JsonPropertyName("workflowKind")]
    public string WorkflowKind { get; init; } = string.Empty;

    [JsonPropertyName("businessPurpose")]
    public string BusinessPurpose { get; init; } = string.Empty;

    [JsonPropertyName("workflowDraftHash")]
    public string WorkflowDraftHash { get; init; } = string.Empty;

    [JsonPropertyName("stationProfileId")]
    public string StationProfileId { get; init; } = string.Empty;

    [JsonPropertyName("operatorSummary")]
    public IReadOnlyList<string> OperatorSummary { get; init; } = [];

    [JsonPropertyName("operatorContractExpectations")]
    public IReadOnlyList<string> OperatorContractExpectations { get; init; } = [];

    [JsonPropertyName("expectedReadiness")]
    public string ExpectedReadiness { get; init; } = RuntimePreviewScenarioEvidenceStatuses.Passed;

    [JsonPropertyName("expectedPackageReadiness")]
    public string ExpectedPackageReadiness { get; init; } = RuntimePreviewScenarioEvidenceStatuses.Passed;

    [JsonPropertyName("expectedPackageReview")]
    public string ExpectedPackageReview { get; init; } = RuntimePreviewScenarioEvidenceStatuses.Passed;

    [JsonPropertyName("expectedStationCompatibility")]
    public string ExpectedStationCompatibility { get; init; } = RuntimePreviewScenarioEvidenceStatuses.Passed;

    [JsonPropertyName("expectedOperatorContractResult")]
    public string ExpectedOperatorContractResult { get; init; } = "satisfied";

    [JsonPropertyName("expectedReleaseReviewDecision")]
    public string ExpectedReleaseReviewDecision { get; init; } = "release_allowed";

    [JsonPropertyName("expectedReleaseDecision")]
    public string ExpectedReleaseDecision { get; init; } = "release_allowed";

    [JsonPropertyName("requiredEngineerApprovals")]
    public IReadOnlyList<string> RequiredEngineerApprovals { get; init; } = [];

    [JsonPropertyName("expectedBlockedReasons")]
    public IReadOnlyList<string> ExpectedBlockedReasons { get; init; } = [];

    [JsonPropertyName("expectedManifestRisk")]
    public string ExpectedManifestRisk { get; init; } = "low";

    [JsonPropertyName("expectedEngineerAction")]
    public string ExpectedEngineerAction { get; init; } = string.Empty;

    [JsonPropertyName("expectedEngineerActions")]
    public IReadOnlyList<string> ExpectedEngineerActions { get; init; } = [];

    [JsonPropertyName("redactionStatus")]
    public string RedactionStatus { get; init; } = "redacted_metadata_only";

    [JsonPropertyName("workflowDraft")]
    public JsonElement WorkflowDraft { get; init; }

    [JsonPropertyName("metadataOnly")]
    public bool MetadataOnly { get; init; } = true;

    [JsonPropertyName("realResourcesTouched")]
    public bool RealResourcesTouched { get; init; }
}

public sealed record RuntimePreviewRedactedFlowCorpusDocument
{
    [JsonPropertyName("schemaVersion")]
    public string SchemaVersion { get; init; } = "2026-06-06.runtime-preview-redacted-flow-corpus.v2";

    [JsonPropertyName("generatedAtUtc")]
    public DateTimeOffset GeneratedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("caseCount")]
    public int CaseCount { get; init; }

    [JsonPropertyName("cases")]
    public IReadOnlyList<RuntimePreviewRedactedFlowCorpusCase> Cases { get; init; } = [];

    [JsonPropertyName("metadataOnly")]
    public bool MetadataOnly { get; init; } = true;

    [JsonPropertyName("realResourcesTouched")]
    public bool RealResourcesTouched { get; init; }
}

public sealed record RuntimePreviewScenarioEvidenceCase
{
    [JsonPropertyName("caseId")]
    public string CaseId { get; init; } = string.Empty;

    [JsonPropertyName("scenario")]
    public string Scenario { get; init; } = string.Empty;

    [JsonPropertyName("businessSummary")]
    public string BusinessSummary { get; init; } = string.Empty;

    [JsonPropertyName("expectedStatus")]
    public string ExpectedStatus { get; init; } = RuntimePreviewScenarioEvidenceStatuses.Passed;

    [JsonPropertyName("expectedSignals")]
    public IReadOnlyList<string> ExpectedSignals { get; init; } = [];

    [JsonPropertyName("workflowDraft")]
    public JsonElement WorkflowDraft { get; init; }
}

public sealed record RuntimePreviewScenarioEvidenceResult
{
    [JsonPropertyName("caseId")]
    public string CaseId { get; init; } = string.Empty;

    [JsonPropertyName("scenario")]
    public string Scenario { get; init; } = string.Empty;

    [JsonPropertyName("expectedStatus")]
    public string ExpectedStatus { get; init; } = string.Empty;

    [JsonPropertyName("actualStatus")]
    public string ActualStatus { get; init; } = string.Empty;

    [JsonPropertyName("passed")]
    public bool Passed { get; init; }

    [JsonPropertyName("previewReady")]
    public bool PreviewReady { get; init; }

    [JsonPropertyName("readyForDeployment")]
    public bool ReadyForDeployment { get; init; }

    [JsonPropertyName("missingResources")]
    public IReadOnlyList<object> MissingResources { get; init; } = [];

    [JsonPropertyName("pendingActions")]
    public IReadOnlyList<VisionAgentPendingAction> PendingActions { get; init; } = [];

    [JsonPropertyName("denyReason")]
    public string? DenyReason { get; init; }

    [JsonPropertyName("precheckRisk")]
    public string? PrecheckRisk { get; init; }

    [JsonPropertyName("businessExplanation")]
    public string BusinessExplanation { get; init; } = string.Empty;

    [JsonPropertyName("workflowDraftHash")]
    public string WorkflowDraftHash { get; init; } = string.Empty;

    [JsonPropertyName("metadataOnly")]
    public bool MetadataOnly { get; init; } = true;

    [JsonPropertyName("realResourcesTouched")]
    public bool RealResourcesTouched { get; init; }
}

public sealed record RuntimePreviewAgentExplanationResult
{
    [JsonPropertyName("caseId")]
    public string CaseId { get; init; } = string.Empty;

    [JsonPropertyName("scenario")]
    public string Scenario { get; init; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; init; } = string.Empty;

    [JsonPropertyName("readyStateExplanation")]
    public string ReadyStateExplanation { get; init; } = string.Empty;

    [JsonPropertyName("missingResourceExplanation")]
    public string MissingResourceExplanation { get; init; } = string.Empty;

    [JsonPropertyName("packageRiskExplanation")]
    public string PackageRiskExplanation { get; init; } = string.Empty;

    [JsonPropertyName("affectedOperators")]
    public IReadOnlyList<string> AffectedOperators { get; init; } = [];

    [JsonPropertyName("blockedReasons")]
    public IReadOnlyList<string> BlockedReasons { get; init; } = [];

    [JsonPropertyName("manifestRisk")]
    public string ManifestRisk { get; init; } = string.Empty;

    [JsonPropertyName("nextEngineerAction")]
    public string NextEngineerAction { get; init; } = string.Empty;

    [JsonPropertyName("workflowDraftAllowed")]
    public bool WorkflowDraftAllowed { get; init; } = true;

    [JsonPropertyName("packageBlocked")]
    public bool PackageBlocked { get; init; }

    [JsonPropertyName("packageReviewAllowed")]
    public bool PackageReviewAllowed { get; init; }

    [JsonPropertyName("releaseReviewAllowed")]
    public bool ReleaseReviewAllowed { get; init; }

    [JsonPropertyName("requiresEngineerApproval")]
    public bool RequiresEngineerApproval { get; init; }

    [JsonPropertyName("stationCompatible")]
    public bool StationCompatible { get; init; }

    [JsonPropertyName("operatorContractsSatisfied")]
    public bool OperatorContractsSatisfied { get; init; }

    [JsonPropertyName("operatorContractExplanation")]
    public string OperatorContractExplanation { get; init; } = string.Empty;

    [JsonPropertyName("stationCompatibilityExplanation")]
    public string StationCompatibilityExplanation { get; init; } = string.Empty;

    [JsonPropertyName("releaseDecisionExplanation")]
    public string ReleaseDecisionExplanation { get; init; } = string.Empty;

    [JsonPropertyName("workflowDraftVsReleaseExplanation")]
    public string WorkflowDraftVsReleaseExplanation { get; init; } = string.Empty;

    [JsonPropertyName("resourceDependencyExplanation")]
    public string ResourceDependencyExplanation { get; init; } = string.Empty;

    [JsonPropertyName("passed")]
    public bool Passed { get; init; }

    [JsonPropertyName("metadataOnly")]
    public bool MetadataOnly { get; init; } = true;

    [JsonPropertyName("realResourcesTouched")]
    public bool RealResourcesTouched { get; init; }
}

public sealed record RuntimePreviewAgentExplanationBenchmarkDocument
{
    [JsonPropertyName("schemaVersion")]
    public string SchemaVersion { get; init; } = "2026-06-06.runtime-preview-agent-explanation-benchmark.v1";

    [JsonPropertyName("generatedAtUtc")]
    public DateTimeOffset GeneratedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("caseCount")]
    public int CaseCount { get; init; }

    [JsonPropertyName("passedCaseCount")]
    public int PassedCaseCount { get; init; }

    [JsonPropertyName("accepted")]
    public bool Accepted { get; init; }

    [JsonPropertyName("cases")]
    public IReadOnlyList<RuntimePreviewAgentExplanationResult> Cases { get; init; } = [];

    [JsonPropertyName("metadataOnly")]
    public bool MetadataOnly { get; init; } = true;

    [JsonPropertyName("realResourcesTouched")]
    public bool RealResourcesTouched { get; init; }
}

public sealed record RuntimePreviewScenarioEvidenceDocument
{
    [JsonPropertyName("schemaVersion")]
    public string SchemaVersion { get; init; } = "2026-06-06.runtime-preview-scenario-evidence.v1";

    [JsonPropertyName("generatedAtUtc")]
    public DateTimeOffset GeneratedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("caseCount")]
    public int CaseCount { get; init; }

    [JsonPropertyName("passedCaseCount")]
    public int PassedCaseCount { get; init; }

    [JsonPropertyName("accepted")]
    public bool Accepted { get; init; }

    [JsonPropertyName("cases")]
    public IReadOnlyList<RuntimePreviewScenarioEvidenceResult> Cases { get; init; } = [];

    [JsonPropertyName("metadataOnly")]
    public bool MetadataOnly { get; init; } = true;

    [JsonPropertyName("realResourcesTouched")]
    public bool RealResourcesTouched { get; init; }
}

public sealed record RuntimePreviewSessionCreateRequest
{
    [JsonPropertyName("config")]
    public RuntimePreviewPilotConfig? Config { get; init; }

    [JsonPropertyName("toolName")]
    public string? ToolName { get; init; }

    [JsonPropertyName("arguments")]
    public JsonElement? Arguments { get; init; }

    [JsonPropertyName("workflowDraft")]
    public JsonElement? WorkflowDraft { get; init; }

    [JsonPropertyName("runtimePreviewConsent")]
    public bool RuntimePreviewConsent { get; init; } = true;
}

public sealed record RuntimePreviewDeployReadinessRequest
{
    [JsonPropertyName("config")]
    public RuntimePreviewPilotConfig? Config { get; init; }

    [JsonPropertyName("toolName")]
    public string? ToolName { get; init; }

    [JsonPropertyName("arguments")]
    public JsonElement? Arguments { get; init; }

    [JsonPropertyName("workflowDraft")]
    public JsonElement? WorkflowDraft { get; init; }

    [JsonPropertyName("runtimePreviewConsent")]
    public bool RuntimePreviewConsent { get; init; } = true;

    [JsonPropertyName("targetStationId")]
    public string? TargetStationId { get; init; }

    [JsonPropertyName("requireReplay")]
    public bool RequireReplay { get; init; } = true;
}

public sealed record RuntimePreviewPackageReadinessRequest
{
    [JsonPropertyName("config")]
    public RuntimePreviewPilotConfig? Config { get; init; }

    [JsonPropertyName("toolName")]
    public string? ToolName { get; init; }

    [JsonPropertyName("arguments")]
    public JsonElement? Arguments { get; init; }

    [JsonPropertyName("workflowDraft")]
    public JsonElement? WorkflowDraft { get; init; }

    [JsonPropertyName("runtimePreviewConsent")]
    public bool RuntimePreviewConsent { get; init; } = true;

    [JsonPropertyName("requireReplay")]
    public bool RequireReplay { get; init; } = true;
}

public sealed record RuntimePackageManifestDryRunRequest
{
    [JsonPropertyName("config")]
    public RuntimePreviewPilotConfig? Config { get; init; }

    [JsonPropertyName("toolName")]
    public string? ToolName { get; init; }

    [JsonPropertyName("arguments")]
    public JsonElement? Arguments { get; init; }

    [JsonPropertyName("workflowDraft")]
    public JsonElement? WorkflowDraft { get; init; }

    [JsonPropertyName("runtimePreviewConsent")]
    public bool RuntimePreviewConsent { get; init; } = true;

    [JsonPropertyName("requireReplay")]
    public bool RequireReplay { get; init; } = true;
}

public sealed record RuntimePreviewPreReleaseReviewRequest
{
    [JsonPropertyName("config")]
    public RuntimePreviewPilotConfig? Config { get; init; }

    [JsonPropertyName("toolName")]
    public string? ToolName { get; init; }

    [JsonPropertyName("arguments")]
    public JsonElement? Arguments { get; init; }

    [JsonPropertyName("workflowDraft")]
    public JsonElement? WorkflowDraft { get; init; }

    [JsonPropertyName("runtimePreviewConsent")]
    public bool RuntimePreviewConsent { get; init; } = true;

    [JsonPropertyName("requireReplay")]
    public bool RequireReplay { get; init; } = true;

    [JsonPropertyName("caseId")]
    public string? CaseId { get; init; }

    [JsonPropertyName("stationProfileId")]
    public string? StationProfileId { get; init; }
}
