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
