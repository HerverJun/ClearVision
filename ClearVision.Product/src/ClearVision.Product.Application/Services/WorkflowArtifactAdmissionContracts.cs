using ClearVision.Product.Application.DTOs;
using ClearVision.Product.Core.Entities;

namespace ClearVision.Product.Application.Services;

public enum WorkflowArtifactAdmissionDisposition
{
    Canonical,
    RepairableLegacy,
    Quarantined
}

public sealed record WorkflowArtifactDiagnostic(
    string Code,
    string Message,
    string OperatorId = "",
    string OperatorType = "",
    string PortName = "",
    string ParameterName = "");

public sealed record WorkflowArtifactRepair(
    string Code,
    string Message,
    string OperatorId = "",
    string FromValue = "",
    string ToValue = "");

public sealed record WorkflowArtifactRouteEvidence
{
    public string TaskType { get; init; } = string.Empty;
    public string ContractVersion { get; init; } = string.Empty;
    public bool Supported { get; init; }
    public bool Satisfied { get; init; }
    public List<string> RequiredCapabilities { get; init; } = [];
    public List<string> MatchedCapabilities { get; init; } = [];
    public List<string> MissingCapabilities { get; init; } = [];
    public List<string> RequiredResultSemantics { get; init; } = [];
    public List<string> ReachableResultSemantics { get; init; } = [];
    public List<string> MissingResultSemantics { get; init; } = [];
    public List<string> LegalTerminals { get; init; } = [];
    public List<string> ReachedTerminals { get; init; } = [];
    public List<string> Evidence { get; init; } = [];
}

public sealed record WorkflowQuarantineReport
{
    public string ReportId { get; init; } = string.Empty;
    public string Source { get; init; } = string.Empty;
    public WorkflowArtifactAdmissionDisposition Disposition { get; init; }
    public string OriginalArtifactHash { get; init; } = string.Empty;
    public string AdmittedArtifactHash { get; init; } = string.Empty;
    public bool OriginalArtifactPreserved { get; init; }
    public bool CanRun { get; init; }
    public bool CanExport { get; init; }
    public bool CanSyncStation { get; init; }
    public bool PreviewOnly { get; init; }
    public List<WorkflowArtifactDiagnostic> Diagnostics { get; init; } = [];
    public WorkflowArtifactDiagnostic? PrimaryDiagnostic { get; init; }
    public List<WorkflowArtifactDiagnostic> SecondaryDiagnostics { get; init; } = [];
    public List<WorkflowArtifactRepair> Repairs { get; init; } = [];
    public WorkflowArtifactRouteEvidence? RouteEvidence { get; init; }

    public string PublicMessage => Disposition switch
    {
        WorkflowArtifactAdmissionDisposition.Canonical => "Workflow artifact passed the canonical admission gate.",
        WorkflowArtifactAdmissionDisposition.RepairableLegacy => "Workflow artifact was admitted only after an unambiguous versioned repair.",
        _ => "Workflow artifact was quarantined because its operator, port, parameter, connection, or route semantics could not be proven."
    };
}

public sealed record WorkflowArtifactAdmissionResult
{
    public WorkflowArtifactAdmissionDisposition Disposition { get; init; }
    public OperatorFlowDto? Flow { get; init; }
    public OperatorFlow? Entity { get; init; }
    public WorkflowQuarantineReport Report { get; init; } = new();

    public bool AllowedToPersist => Disposition != WorkflowArtifactAdmissionDisposition.Quarantined;
    public bool AllowedToRun => Report.CanRun;
    public bool AllowedToExport => Report.CanExport;
    public bool AllowedToSyncStation => Report.CanSyncStation;
}

public sealed record WorkflowArtifactAdmissionContext
{
    public string TaskType { get; init; } = string.Empty;
    public bool? RouteSemanticsSatisfied { get; init; }
    public string ArtifactFingerprint { get; init; } = string.Empty;

    /// <summary>
    /// Historical persisted flows may be materialized for fail-closed execution of a disabled
    /// compatibility executor. Creation, import, persistence, export and AI admission never set this.
    /// </summary>
    public bool AllowHistoricalDisabledOperators { get; init; }
}

public static class WorkflowArtifactAdmissionClassifier
{
    public static bool IsAiArtifact(OperatorFlowDto flow)
    {
        ArgumentNullException.ThrowIfNull(flow);
        return flow.Operators.Any(operatorDto =>
            operatorDto.Metadata?.Keys.Any(IsAgentMetadataKey) == true);
    }

    public static bool IsAiArtifact(OperatorFlow flow)
    {
        ArgumentNullException.ThrowIfNull(flow);
        return flow.Operators.Any(operatorEntity =>
            operatorEntity.Metadata?.Keys.Any(IsAgentMetadataKey) == true);
    }

    private static bool IsAgentMetadataKey(string key) =>
        !string.IsNullOrWhiteSpace(key) &&
        key.StartsWith("agent", StringComparison.OrdinalIgnoreCase);
}

public sealed class WorkflowArtifactAdmissionException : InvalidOperationException
{
    public WorkflowArtifactAdmissionException(WorkflowQuarantineReport report)
        : base($"WORKFLOW_ARTIFACT_{report.Disposition.ToString().ToUpperInvariant()}: {report.PublicMessage}")
    {
        Report = report;
    }

    public WorkflowQuarantineReport Report { get; }
}

public static class WorkflowArtifactAdmissionFailures
{
    public static WorkflowArtifactAdmissionException GateUnavailable(string source)
    {
        var diagnostic = new WorkflowArtifactDiagnostic(
            "workflow_artifact_admission_gate_unavailable",
            "The workflow artifact admission gate is unavailable; the operation was fail-closed.");
        var report = new WorkflowQuarantineReport
        {
            ReportId = $"admission_gate_unavailable_{Guid.NewGuid():N}",
            Source = source,
            Disposition = WorkflowArtifactAdmissionDisposition.Quarantined,
            OriginalArtifactPreserved = false,
            CanRun = false,
            CanExport = false,
            CanSyncStation = false,
            Diagnostics =
            [
                diagnostic
            ],
            PrimaryDiagnostic = diagnostic
        };
        return new WorkflowArtifactAdmissionException(report);
    }
}

public sealed record WorkflowArtifactQuarantineRecord
{
    public string RecordId { get; init; } = string.Empty;
    public DateTimeOffset RecordedAtUtc { get; init; }
    public string Source { get; init; } = string.Empty;
    public WorkflowQuarantineReport Report { get; init; } = new();
    public string OriginalSnapshot { get; init; } = string.Empty;
}

public interface IWorkflowArtifactAdmissionGate
{
    WorkflowArtifactAdmissionResult Inspect(
        OperatorFlowDto flow,
        string source,
        string? originalSnapshot = null,
        WorkflowArtifactAdmissionContext? context = null);

    WorkflowArtifactAdmissionResult Inspect(
        OperatorFlow flow,
        string source,
        string? originalSnapshot = null,
        WorkflowArtifactAdmissionContext? context = null);

    WorkflowArtifactAdmissionResult InspectJson(
        string originalSnapshot,
        string source,
        WorkflowArtifactAdmissionContext? context = null);
}

public interface IWorkflowArtifactQuarantineStore
{
    void Preserve(WorkflowArtifactQuarantineRecord record);
}
