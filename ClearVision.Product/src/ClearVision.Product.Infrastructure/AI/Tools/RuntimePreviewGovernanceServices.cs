using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ClearVision.Product.Core.AI.Tools;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Infrastructure.AI.Agent;

namespace ClearVision.Product.Infrastructure.AI.Tools;

public sealed class RuntimePreviewGovernanceStore
{
    public const string SchemaVersion = "2026-06-06.runtime-preview-governance-store.v4";
    public const string StorageVersion = "jsonl.v4";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private readonly object _gate = new();
    private readonly string _directoryPath;

    public RuntimePreviewGovernanceStore()
        : this(GetDefaultDirectory())
    {
    }

    public RuntimePreviewGovernanceStore(string directoryPath)
    {
        _directoryPath = string.IsNullOrWhiteSpace(directoryPath)
            ? GetDefaultDirectory()
            : Path.GetFullPath(directoryPath);
        Directory.CreateDirectory(_directoryPath);
    }

    public string StorageMode => "jsonl";

    public string StorageVersionValue => StorageVersion;

    public string DirectoryPath => _directoryPath;

    public void SaveSession(RuntimePreviewSession session)
    {
        Append(SessionPath, session);
    }

    public IReadOnlyList<RuntimePreviewSession> LoadSessions()
    {
        return ReadJsonLines<RuntimePreviewSession>(SessionPath)
            .GroupBy(session => session.SessionId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(session => session.UpdatedAtUtc).First())
            .OrderByDescending(session => session.UpdatedAtUtc)
            .ToList();
    }

    public void SaveAuditEvent(RuntimePreviewAuditEvent auditEvent)
    {
        Append(AuditPath, auditEvent);
    }

    public IReadOnlyList<RuntimePreviewAuditEvent> LoadAuditEvents()
    {
        return ReadJsonLines<RuntimePreviewAuditEvent>(AuditPath)
            .GroupBy(item => item.EventId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(item => item.CreatedAtUtc).First())
            .OrderBy(item => item.CreatedAtUtc)
            .ToList();
    }

    public void SaveReport(RuntimePreviewSessionReport report)
    {
        Append(ReportPath, report);
    }

    public IReadOnlyList<RuntimePreviewSessionReport> LoadReports()
    {
        return ReadJsonLines<RuntimePreviewSessionReport>(ReportPath)
            .GroupBy(report => report.ReportId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(report => report.GeneratedAtUtc).First())
            .OrderByDescending(report => report.GeneratedAtUtc)
            .ToList();
    }

    public void SaveDeployReadinessReport(RuntimePreviewDeployReadinessReport report)
    {
        Append(DeployReadinessReportPath, report);
    }

    public IReadOnlyList<RuntimePreviewDeployReadinessReport> LoadDeployReadinessReports()
    {
        return ReadJsonLines<RuntimePreviewDeployReadinessReport>(DeployReadinessReportPath)
            .GroupBy(report => report.ReportId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(report => report.GeneratedAtUtc).First())
            .OrderByDescending(report => report.GeneratedAtUtc)
            .ToList();
    }

    public void SavePackageReadinessReport(RuntimePreviewPackageReadinessReport report)
    {
        Append(PackageReadinessReportPath, report);
    }

    public IReadOnlyList<RuntimePreviewPackageReadinessReport> LoadPackageReadinessReports()
    {
        return ReadJsonLines<RuntimePreviewPackageReadinessReport>(PackageReadinessReportPath)
            .GroupBy(report => report.ReportId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(report => report.GeneratedAtUtc).First())
            .OrderByDescending(report => report.GeneratedAtUtc)
            .ToList();
    }

    public void SaveManifestDryRunReport(RuntimePackageManifestDryRunReport report)
    {
        Append(ManifestDryRunReportPath, report);
    }

    public IReadOnlyList<RuntimePackageManifestDryRunReport> LoadManifestDryRunReports()
    {
        return ReadJsonLines<RuntimePackageManifestDryRunReport>(ManifestDryRunReportPath)
            .GroupBy(report => report.ManifestId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(report => report.GeneratedAtUtc).First())
            .OrderByDescending(report => report.GeneratedAtUtc)
            .ToList();
    }

    public void SaveStationCompatibilityReport(RuntimePreviewStationCompatibilityReport report)
    {
        Append(StationCompatibilityReportPath, report);
    }

    public IReadOnlyList<RuntimePreviewStationCompatibilityReport> LoadStationCompatibilityReports()
    {
        return ReadJsonLines<RuntimePreviewStationCompatibilityReport>(StationCompatibilityReportPath)
            .GroupBy(report => report.ReportId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(report => report.GeneratedAtUtc).First())
            .OrderByDescending(report => report.GeneratedAtUtc)
            .ToList();
    }

    public void SaveOperatorContractValidationReport(RuntimePreviewOperatorContractValidationReport report)
    {
        Append(OperatorContractValidationReportPath, report);
    }

    public IReadOnlyList<RuntimePreviewOperatorContractValidationReport> LoadOperatorContractValidationReports()
    {
        return ReadJsonLines<RuntimePreviewOperatorContractValidationReport>(OperatorContractValidationReportPath)
            .GroupBy(report => report.ReportId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(report => report.GeneratedAtUtc).First())
            .OrderByDescending(report => report.GeneratedAtUtc)
            .ToList();
    }

    public void SavePreReleaseReviewReport(RuntimePreviewPreReleaseReviewReport report)
    {
        Append(PreReleaseReviewReportPath, report);
    }

    public IReadOnlyList<RuntimePreviewPreReleaseReviewReport> LoadPreReleaseReviewReports()
    {
        return ReadJsonLines<RuntimePreviewPreReleaseReviewReport>(PreReleaseReviewReportPath)
            .GroupBy(report => report.ReviewId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(report => report.GeneratedAtUtc).First())
            .OrderByDescending(report => report.GeneratedAtUtc)
            .ToList();
    }

    public void SaveReleaseReviewDecision(RuntimePreviewReleaseReadinessDecisionMatrix decision)
    {
        Append(ReleaseReviewDecisionPath, decision);
    }

    public IReadOnlyList<RuntimePreviewReleaseReadinessDecisionMatrix> LoadReleaseReviewDecisions()
    {
        return ReadJsonLines<RuntimePreviewReleaseReadinessDecisionMatrix>(ReleaseReviewDecisionPath)
            .GroupBy(report => string.IsNullOrWhiteSpace(report.ReportId) ? report.ReviewId : report.ReportId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(report => report.GeneratedAtUtc).First())
            .OrderByDescending(report => report.GeneratedAtUtc)
            .ToList();
    }

    public void SaveStationProfileSnapshot(RuntimePreviewStationProfileDocument document)
    {
        Append(StationProfileSnapshotPath, document);
    }

    public IReadOnlyList<RuntimePreviewStationProfileDocument> LoadStationProfileSnapshots()
    {
        return ReadJsonLines<RuntimePreviewStationProfileDocument>(StationProfileSnapshotPath)
            .OrderByDescending(document => document.GeneratedAtUtc)
            .ToList();
    }

    public void SaveOperatorContractRegistrySnapshot(RuntimePreviewOperatorContractRegistryDocument document)
    {
        Append(OperatorContractRegistrySnapshotPath, document);
    }

    public IReadOnlyList<RuntimePreviewOperatorContractRegistryDocument> LoadOperatorContractRegistrySnapshots()
    {
        return ReadJsonLines<RuntimePreviewOperatorContractRegistryDocument>(OperatorContractRegistrySnapshotPath)
            .GroupBy(document => document.OperatorContractVersion, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(document => document.GeneratedAtUtc).First())
            .OrderByDescending(document => document.GeneratedAtUtc)
            .ToList();
    }

    public void SaveOperatorContractCoverageReport(RuntimePreviewOperatorContractCoverageReport report)
    {
        Append(OperatorContractCoverageReportPath, report);
    }

    public IReadOnlyList<RuntimePreviewOperatorContractCoverageReport> LoadOperatorContractCoverageReports()
    {
        return ReadJsonLines<RuntimePreviewOperatorContractCoverageReport>(OperatorContractCoverageReportPath)
            .GroupBy(report => report.ReportId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(report => report.GeneratedAtUtc).First())
            .OrderByDescending(report => report.GeneratedAtUtc)
            .ToList();
    }

    public void SaveFinalGovernanceExport(RuntimePreviewGovernanceExportManifest manifest)
    {
        Append(FinalGovernanceExportPath, manifest with { FinalGovernanceExports = [] });
    }

    public IReadOnlyList<RuntimePreviewGovernanceExportManifest> LoadFinalGovernanceExports()
    {
        return ReadJsonLines<RuntimePreviewGovernanceExportManifest>(FinalGovernanceExportPath)
            .GroupBy(manifest => manifest.ExportId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(manifest => manifest.GeneratedAtUtc).First())
            .OrderByDescending(manifest => manifest.GeneratedAtUtc)
            .ToList();
    }

    public RuntimePreviewGovernanceStorageIndexSummary BuildIndexSummary()
    {
        return new RuntimePreviewGovernanceStorageIndexSummary
        {
            SchemaVersion = SchemaVersion,
            StorageVersion = StorageVersion,
            StorageMode = StorageMode,
            RecordTypes =
            [
                "session",
                "audit",
                "session_report",
                "deploy_readiness_report",
                "package_readiness_report",
                "manifest_dry_run_report",
                "station_compatibility_report",
                "operator_contract_validation_report",
                "pre_release_review_report",
                "release_review_decision",
                "station_profile_snapshot",
                "operator_contract_registry_snapshot",
                "contract_coverage_report",
                "final_governance_export",
                "agent_run_event",
                "agent_run_summary"
            ],
            SessionCount = LoadSessions().Count,
            AuditEventCount = LoadAuditEvents().Count,
            SessionReportCount = LoadReports().Count,
            DeployReadinessReportCount = LoadDeployReadinessReports().Count,
            PackageReadinessReportCount = LoadPackageReadinessReports().Count,
            ManifestDryRunReportCount = LoadManifestDryRunReports().Count,
            StationCompatibilityReportCount = LoadStationCompatibilityReports().Count,
            OperatorContractValidationReportCount = LoadOperatorContractValidationReports().Count,
            PreReleaseReviewReportCount = LoadPreReleaseReviewReports().Count,
            ReleaseReviewDecisionCount = LoadReleaseReviewDecisions().Count,
            StationProfileSnapshotCount = LoadStationProfileSnapshots().Count,
            OperatorContractRegistrySnapshotCount = LoadOperatorContractRegistrySnapshots().Count,
            OperatorContractCoverageReportCount = LoadOperatorContractCoverageReports().Count,
            FinalGovernanceExportCount = LoadFinalGovernanceExports().Count,
            AgentRunEventCount = CountJsonLines(AgentRunEventPath),
            AgentRunSummaryCount = CountJsonLines(AgentRunSummaryPath),
            AgentRunAuditFileCount = AgentRunAuditFiles().Count,
            CorruptLineCount = CountCorruptLines(SessionPath) +
                               CountCorruptLines(AuditPath) +
                               CountCorruptLines(ReportPath) +
                               CountCorruptLines(DeployReadinessReportPath) +
                               CountCorruptLines(PackageReadinessReportPath) +
                               CountCorruptLines(ManifestDryRunReportPath) +
                               CountCorruptLines(StationCompatibilityReportPath) +
                               CountCorruptLines(OperatorContractValidationReportPath) +
                               CountCorruptLines(PreReleaseReviewReportPath) +
                               CountCorruptLines(ReleaseReviewDecisionPath) +
                               CountCorruptLines(StationProfileSnapshotPath) +
                               CountCorruptLines(OperatorContractRegistrySnapshotPath) +
                               CountCorruptLines(OperatorContractCoverageReportPath) +
                               CountCorruptLines(FinalGovernanceExportPath) +
                               CountCorruptLines(AgentRunEventPath) +
                               CountCorruptLines(AgentRunSummaryPath),
            RetentionPolicy = "default_30_days_200_sessions",
            MetadataOnly = true,
            RealResourcesTouched = false
        };
    }

    public RuntimePreviewGovernanceExportManifest ExportManifest()
    {
        var manifest = new RuntimePreviewGovernanceExportManifest
        {
            ExportId = $"rp_export_{Guid.NewGuid():N}",
            GeneratedAtUtc = DateTimeOffset.UtcNow,
            IndexSummary = BuildIndexSummary(),
            Sessions = LoadSessions(),
            AuditEvents = LoadAuditEvents(),
            SessionReports = LoadReports(),
            DeployReadinessReports = LoadDeployReadinessReports(),
            PackageReadinessReports = LoadPackageReadinessReports(),
            ManifestDryRunReports = LoadManifestDryRunReports(),
            StationCompatibilityReports = LoadStationCompatibilityReports(),
            OperatorContractValidationReports = LoadOperatorContractValidationReports(),
            PreReleaseReviewReports = LoadPreReleaseReviewReports(),
            ReleaseReviewDecisions = LoadReleaseReviewDecisions(),
            StationProfileSnapshots = LoadStationProfileSnapshots(),
            OperatorContractRegistrySnapshots = LoadOperatorContractRegistrySnapshots(),
            OperatorContractCoverageReports = LoadOperatorContractCoverageReports(),
            FinalGovernanceExports = LoadFinalGovernanceExports(),
            AgentRunAuditFiles = AgentRunAuditFiles(),
            RedactionPass = true,
            MetadataOnly = true,
            RealResourcesTouched = false
        };
        var raw = JsonSerializer.Serialize(manifest, JsonOptions);
        RuntimePreviewGovernanceRedactor.ThrowIfUnsafeStorageText(raw);
        return manifest;
    }

    public RuntimePreviewRetentionCleanupResult Cleanup(int retentionDays, int maxSessions)
    {
        var effectiveRetentionDays = retentionDays <= 0 ? 30 : Math.Min(retentionDays, 365);
        var effectiveMaxSessions = maxSessions <= 0 ? 200 : Math.Min(maxSessions, 5000);
        var cutoff = DateTimeOffset.UtcNow.AddDays(-effectiveRetentionDays);

        lock (_gate)
        {
            var sessionsBefore = LoadSessions();
            var auditBefore = LoadAuditEvents();
            var reportsBefore = LoadReports();
            var deployBefore = LoadDeployReadinessReports();
            var packageBefore = LoadPackageReadinessReports();
            var manifestBefore = LoadManifestDryRunReports();
            var stationBefore = LoadStationCompatibilityReports();
            var contractBefore = LoadOperatorContractValidationReports();
            var reviewBefore = LoadPreReleaseReviewReports();
            var decisionBefore = LoadReleaseReviewDecisions();
            var stationSnapshotBefore = LoadStationProfileSnapshots();
            var registrySnapshotBefore = LoadOperatorContractRegistrySnapshots();
            var coverageBefore = LoadOperatorContractCoverageReports();
            var finalExportBefore = LoadFinalGovernanceExports();

            var sessionsAfter = sessionsBefore
                .Where(session => session.UpdatedAtUtc >= cutoff)
                .OrderByDescending(session => session.UpdatedAtUtc)
                .Take(effectiveMaxSessions)
                .ToList();
            var sessionIds = sessionsAfter
                .Select(session => session.SessionId)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var auditAfter = auditBefore
                .Where(item => sessionIds.Contains(item.SessionId) && item.CreatedAtUtc >= cutoff)
                .ToList();
            var reportsAfter = reportsBefore
                .Where(report => sessionIds.Contains(report.SessionId) && report.GeneratedAtUtc >= cutoff)
                .ToList();
            var deployAfter = deployBefore
                .Where(report => sessionIds.Contains(report.SessionId) && report.GeneratedAtUtc >= cutoff)
                .ToList();
            var packageAfter = packageBefore
                .Where(report => sessionIds.Contains(report.SessionId) && report.GeneratedAtUtc >= cutoff)
                .ToList();
            var manifestAfter = manifestBefore
                .Where(report => sessionIds.Contains(report.SessionId) && report.GeneratedAtUtc >= cutoff)
                .ToList();
            var stationAfter = stationBefore
                .Where(report => sessionIds.Contains(report.SessionId) && report.GeneratedAtUtc >= cutoff)
                .ToList();
            var contractAfter = contractBefore
                .Where(report => sessionIds.Contains(report.SessionId) && report.GeneratedAtUtc >= cutoff)
                .ToList();
            var reviewAfter = reviewBefore
                .Where(report => sessionIds.Contains(report.SessionId) && report.GeneratedAtUtc >= cutoff)
                .ToList();
            var decisionAfter = decisionBefore
                .Where(report => string.IsNullOrWhiteSpace(report.ReviewId) ||
                                 reviewAfter.Any(review => string.Equals(review.ReviewId, report.ReviewId, StringComparison.OrdinalIgnoreCase)) ||
                                 report.GeneratedAtUtc >= cutoff)
                .ToList();
            var stationSnapshotAfter = stationSnapshotBefore
                .Where(document => document.GeneratedAtUtc >= cutoff)
                .Take(effectiveMaxSessions)
                .ToList();
            var registrySnapshotAfter = registrySnapshotBefore
                .Where(document => document.GeneratedAtUtc >= cutoff)
                .Take(effectiveMaxSessions)
                .ToList();
            var coverageAfter = coverageBefore
                .Where(report => report.GeneratedAtUtc >= cutoff)
                .Take(effectiveMaxSessions)
                .ToList();
            var finalExportAfter = finalExportBefore
                .Where(manifest => manifest.GeneratedAtUtc >= cutoff)
                .Take(effectiveMaxSessions)
                .ToList();

            Rewrite(SessionPath, sessionsAfter);
            Rewrite(AuditPath, auditAfter);
            Rewrite(ReportPath, reportsAfter);
            Rewrite(DeployReadinessReportPath, deployAfter);
            Rewrite(PackageReadinessReportPath, packageAfter);
            Rewrite(ManifestDryRunReportPath, manifestAfter);
            Rewrite(StationCompatibilityReportPath, stationAfter);
            Rewrite(OperatorContractValidationReportPath, contractAfter);
            Rewrite(PreReleaseReviewReportPath, reviewAfter);
            Rewrite(ReleaseReviewDecisionPath, decisionAfter);
            Rewrite(StationProfileSnapshotPath, stationSnapshotAfter);
            Rewrite(OperatorContractRegistrySnapshotPath, registrySnapshotAfter);
            Rewrite(OperatorContractCoverageReportPath, coverageAfter);
            Rewrite(FinalGovernanceExportPath, finalExportAfter);

            return new RuntimePreviewRetentionCleanupResult
            {
                RetentionDays = effectiveRetentionDays,
                MaxSessions = effectiveMaxSessions,
                SessionsBefore = sessionsBefore.Count,
                SessionsAfter = sessionsAfter.Count,
                AuditEventsBefore = auditBefore.Count,
                AuditEventsAfter = auditAfter.Count,
                ReportsBefore = reportsBefore.Count + deployBefore.Count + packageBefore.Count + manifestBefore.Count + stationBefore.Count + contractBefore.Count + reviewBefore.Count + decisionBefore.Count + stationSnapshotBefore.Count + registrySnapshotBefore.Count + coverageBefore.Count + finalExportBefore.Count,
                ReportsAfter = reportsAfter.Count + deployAfter.Count + packageAfter.Count + manifestAfter.Count + stationAfter.Count + contractAfter.Count + reviewAfter.Count + decisionAfter.Count + stationSnapshotAfter.Count + registrySnapshotAfter.Count + coverageAfter.Count + finalExportAfter.Count,
                MetadataOnly = true,
                RealResourcesTouched = false
            };
        }
    }

    private string SessionPath => Path.Combine(_directoryPath, "runtime_preview_sessions.jsonl");

    private string AuditPath => Path.Combine(_directoryPath, "runtime_preview_audit.jsonl");

    private string ReportPath => Path.Combine(_directoryPath, "runtime_preview_reports.jsonl");

    private string DeployReadinessReportPath => Path.Combine(_directoryPath, "runtime_preview_deploy_readiness_reports.jsonl");

    private string PackageReadinessReportPath => Path.Combine(_directoryPath, "runtime_preview_package_readiness_reports.jsonl");

    private string ManifestDryRunReportPath => Path.Combine(_directoryPath, "runtime_package_manifest_dry_run_reports.jsonl");

    private string StationCompatibilityReportPath => Path.Combine(_directoryPath, "runtime_preview_station_compatibility_reports.jsonl");

    private string OperatorContractValidationReportPath => Path.Combine(_directoryPath, "runtime_preview_operator_contract_validation_reports.jsonl");

    private string PreReleaseReviewReportPath => Path.Combine(_directoryPath, "runtime_preview_pre_release_review_reports.jsonl");

    private string ReleaseReviewDecisionPath => Path.Combine(_directoryPath, "runtime_preview_release_review_decisions.jsonl");

    private string StationProfileSnapshotPath => Path.Combine(_directoryPath, "runtime_preview_station_profile_snapshots.jsonl");

    private string OperatorContractRegistrySnapshotPath => Path.Combine(_directoryPath, "runtime_preview_operator_contract_registry_snapshots.jsonl");

    private string OperatorContractCoverageReportPath => Path.Combine(_directoryPath, "runtime_preview_operator_contract_coverage_reports.jsonl");

    private string FinalGovernanceExportPath => Path.Combine(_directoryPath, "runtime_preview_final_governance_exports.jsonl");

    private string AgentRunEventPath => Path.Combine(_directoryPath, "agent_run_events.jsonl");

    private string AgentRunSummaryPath => Path.Combine(_directoryPath, "agent_run_summary.jsonl");

    private static string GetDefaultDirectory()
    {
        var configured = Environment.GetEnvironmentVariable("CV_RUNTIME_PREVIEW_GOVERNANCE_STORE");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, "ClearVision", "RuntimePreviewGovernance");
    }

    private void Append<T>(string path, T item)
    {
        var line = JsonSerializer.Serialize(item, JsonOptions);
        RuntimePreviewGovernanceRedactor.ThrowIfUnsafeStorageText(line);
        lock (_gate)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.AppendAllText(path, line + Environment.NewLine, Encoding.UTF8);
        }
    }

    private static IReadOnlyList<T> ReadJsonLines<T>(string path)
    {
        if (!File.Exists(path))
        {
            return [];
        }

        var results = new List<T>();
        foreach (var line in File.ReadLines(path, Encoding.UTF8))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            try
            {
                var item = JsonSerializer.Deserialize<T>(line, JsonOptions);
                if (item != null)
                {
                    results.Add(item);
                }
            }
            catch (JsonException)
            {
                // Corrupt lines are ignored; subsequent append-only events remain usable.
            }
        }

        return results;
    }

    private static int CountCorruptLines(string path)
    {
        if (!File.Exists(path))
        {
            return 0;
        }

        var corrupt = 0;
        foreach (var line in File.ReadLines(path, Encoding.UTF8))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            try
            {
                using var _ = JsonDocument.Parse(line);
            }
            catch (JsonException)
            {
                corrupt++;
            }
        }

        return corrupt;
    }

    private static int CountJsonLines(string path)
    {
        if (!File.Exists(path))
        {
            return 0;
        }

        var count = 0;
        foreach (var line in File.ReadLines(path, Encoding.UTF8))
        {
            if (!string.IsNullOrWhiteSpace(line))
            {
                count++;
            }
        }

        return count;
    }

    private IReadOnlyList<string> AgentRunAuditFiles()
    {
        var files = new List<string>();
        if (File.Exists(AgentRunEventPath))
        {
            files.Add(Path.GetFileName(AgentRunEventPath));
        }

        if (File.Exists(AgentRunSummaryPath))
        {
            files.Add(Path.GetFileName(AgentRunSummaryPath));
        }

        return files;
    }

    private static void Rewrite<T>(string path, IReadOnlyList<T> items)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var lines = items.Select(item =>
        {
            var line = JsonSerializer.Serialize(item, JsonOptions);
            RuntimePreviewGovernanceRedactor.ThrowIfUnsafeStorageText(line);
            return line;
        });
        File.WriteAllLines(path, lines, Encoding.UTF8);
    }
}

public sealed class RuntimePreviewSessionStore
{
    private readonly ConcurrentDictionary<string, RuntimePreviewSession> _sessions = new(StringComparer.OrdinalIgnoreCase);
    private readonly RuntimePreviewGovernanceStore? _governanceStore;

    public RuntimePreviewSessionStore()
    {
    }

    public RuntimePreviewSessionStore(RuntimePreviewGovernanceStore governanceStore)
    {
        _governanceStore = governanceStore;
        foreach (var session in governanceStore.LoadSessions())
        {
            _sessions[session.SessionId] = session;
        }
    }

    public RuntimePreviewSession Create(
        string workflowDraftHash,
        string pilotConfigRevision,
        string catalogSnapshotId)
    {
        var session = new RuntimePreviewSession
        {
            SessionId = $"rp_session_{Guid.NewGuid():N}",
            Status = RuntimePreviewSessionStatuses.Created,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
            WorkflowDraftHash = workflowDraftHash,
            PilotConfigRevision = pilotConfigRevision,
            CatalogSnapshotId = catalogSnapshotId,
            ReadinessStatus = RuntimePreviewPilotReadinessStatuses.NotReady,
            PermissionStatus = RuntimePreviewPermissionStatuses.NotReady,
            MetadataOnly = true,
            RealResourcesTouched = false
        };
        _sessions[session.SessionId] = session;
        _governanceStore?.SaveSession(session);
        return session;
    }

    public IReadOnlyList<RuntimePreviewSession> List()
    {
        return _sessions.Values
            .OrderByDescending(session => session.UpdatedAtUtc)
            .ToList();
    }

    public RuntimePreviewSession? Get(string sessionId)
    {
        return string.IsNullOrWhiteSpace(sessionId) ? null : _sessions.GetValueOrDefault(sessionId.Trim());
    }

    public RuntimePreviewSession Update(string sessionId, Func<RuntimePreviewSession, RuntimePreviewSession> update)
    {
        var updated = _sessions.AddOrUpdate(
            sessionId,
            _ => throw new InvalidOperationException($"RuntimePreview session '{sessionId}' was not found."),
            (_, current) => update(current) with { UpdatedAtUtc = DateTimeOffset.UtcNow });
        _governanceStore?.SaveSession(updated);
        return updated;
    }
}

public sealed class RuntimePreviewAuditTrail
{
    private readonly ConcurrentDictionary<string, RuntimePreviewAuditEvent> _events = new(StringComparer.OrdinalIgnoreCase);
    private readonly RuntimePreviewGovernanceStore? _governanceStore;

    public RuntimePreviewAuditTrail()
    {
    }

    public RuntimePreviewAuditTrail(RuntimePreviewGovernanceStore governanceStore)
    {
        _governanceStore = governanceStore;
        foreach (var auditEvent in governanceStore.LoadAuditEvents())
        {
            _events[auditEvent.EventId] = auditEvent;
        }
    }

    public RuntimePreviewAuditEvent Append(string sessionId, string eventType, object? payload)
    {
        var safePayload = RuntimePreviewGovernanceRedactor.ToRedactedElement(payload);
        var auditEvent = new RuntimePreviewAuditEvent
        {
            EventId = $"rp_audit_{Guid.NewGuid():N}",
            SessionId = sessionId,
            EventType = eventType,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            Payload = safePayload,
            Redacted = true,
            MetadataOnly = true
        };
        _events[auditEvent.EventId] = auditEvent;
        _governanceStore?.SaveAuditEvent(auditEvent);
        return auditEvent;
    }

    public IReadOnlyList<RuntimePreviewAuditEvent> ListForSession(string sessionId)
    {
        return _events.Values
            .Where(item => string.Equals(item.SessionId, sessionId, StringComparison.OrdinalIgnoreCase))
            .OrderBy(item => item.CreatedAtUtc)
            .ToList();
    }

    public IReadOnlyList<RuntimePreviewAuditEvent> ListAll()
    {
        return _events.Values
            .OrderBy(item => item.CreatedAtUtc)
            .ToList();
    }
}

public sealed class RuntimePreviewReportArchive
{
    private readonly ConcurrentDictionary<string, RuntimePreviewSessionReport> _reports = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, RuntimePreviewDeployReadinessReport> _deployReadinessReports = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, RuntimePreviewPackageReadinessReport> _packageReadinessReports = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, RuntimePackageManifestDryRunReport> _manifestDryRunReports = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, RuntimePreviewStationCompatibilityReport> _stationCompatibilityReports = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, RuntimePreviewOperatorContractValidationReport> _operatorContractValidationReports = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, RuntimePreviewPreReleaseReviewReport> _preReleaseReviewReports = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, RuntimePreviewReleaseReadinessDecisionMatrix> _releaseReviewDecisions = new(StringComparer.OrdinalIgnoreCase);
    private readonly RuntimePreviewGovernanceStore? _governanceStore;

    public RuntimePreviewReportArchive()
    {
    }

    public RuntimePreviewReportArchive(RuntimePreviewGovernanceStore governanceStore)
    {
        _governanceStore = governanceStore;
        foreach (var report in governanceStore.LoadReports())
        {
            _reports[report.ReportId] = report;
        }

        foreach (var report in governanceStore.LoadDeployReadinessReports())
        {
            _deployReadinessReports[report.ReportId] = report;
        }

        foreach (var report in governanceStore.LoadPackageReadinessReports())
        {
            _packageReadinessReports[report.ReportId] = report;
        }

        foreach (var report in governanceStore.LoadManifestDryRunReports())
        {
            _manifestDryRunReports[report.ManifestId] = report;
        }

        foreach (var report in governanceStore.LoadStationCompatibilityReports())
        {
            _stationCompatibilityReports[report.ReportId] = report;
        }

        foreach (var report in governanceStore.LoadOperatorContractValidationReports())
        {
            _operatorContractValidationReports[report.ReportId] = report;
        }

        foreach (var report in governanceStore.LoadPreReleaseReviewReports())
        {
            _preReleaseReviewReports[report.ReviewId] = report;
        }

        foreach (var decision in governanceStore.LoadReleaseReviewDecisions())
        {
            var key = string.IsNullOrWhiteSpace(decision.ReportId) ? decision.ReviewId : decision.ReportId;
            if (!string.IsNullOrWhiteSpace(key))
            {
                _releaseReviewDecisions[key] = decision;
            }
        }
    }

    public RuntimePreviewSessionReport Save(RuntimePreviewSessionReport report)
    {
        _reports[report.ReportId] = report;
        _governanceStore?.SaveReport(report);
        return report;
    }

    public RuntimePreviewSessionReport? Get(string reportId)
    {
        return string.IsNullOrWhiteSpace(reportId) ? null : _reports.GetValueOrDefault(reportId.Trim());
    }

    public RuntimePreviewSessionReport? GetBySessionId(string sessionId)
    {
        return _reports.Values
            .Where(report => string.Equals(report.SessionId, sessionId, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(report => report.GeneratedAtUtc)
            .FirstOrDefault();
    }

    public IReadOnlyList<RuntimePreviewSessionReport> ListSessionReports()
    {
        return _reports.Values
            .OrderByDescending(report => report.GeneratedAtUtc)
            .ToList();
    }

    public RuntimePreviewDeployReadinessReport SaveDeployReadinessReport(RuntimePreviewDeployReadinessReport report)
    {
        _deployReadinessReports[report.ReportId] = report;
        _governanceStore?.SaveDeployReadinessReport(report);
        return report;
    }

    public RuntimePreviewDeployReadinessReport? GetDeployReadinessReport(string reportId)
    {
        return string.IsNullOrWhiteSpace(reportId) ? null : _deployReadinessReports.GetValueOrDefault(reportId.Trim());
    }

    public RuntimePreviewDeployReadinessReport? GetDeployReadinessReportBySessionId(string sessionId)
    {
        return _deployReadinessReports.Values
            .Where(report => string.Equals(report.SessionId, sessionId, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(report => report.GeneratedAtUtc)
            .FirstOrDefault();
    }

    public IReadOnlyList<RuntimePreviewDeployReadinessReport> ListDeployReadinessReports()
    {
        return _deployReadinessReports.Values
            .OrderByDescending(report => report.GeneratedAtUtc)
            .ToList();
    }

    public RuntimePreviewPackageReadinessReport SavePackageReadinessReport(RuntimePreviewPackageReadinessReport report)
    {
        _packageReadinessReports[report.ReportId] = report;
        _governanceStore?.SavePackageReadinessReport(report);
        return report;
    }

    public RuntimePreviewPackageReadinessReport? GetPackageReadinessReport(string reportId)
    {
        return string.IsNullOrWhiteSpace(reportId) ? null : _packageReadinessReports.GetValueOrDefault(reportId.Trim());
    }

    public RuntimePreviewPackageReadinessReport? GetPackageReadinessReportBySessionId(string sessionId)
    {
        return _packageReadinessReports.Values
            .Where(report => string.Equals(report.SessionId, sessionId, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(report => report.GeneratedAtUtc)
            .FirstOrDefault();
    }

    public IReadOnlyList<RuntimePreviewPackageReadinessReport> ListPackageReadinessReports()
    {
        return _packageReadinessReports.Values
            .OrderByDescending(report => report.GeneratedAtUtc)
            .ToList();
    }

    public RuntimePackageManifestDryRunReport SaveManifestDryRunReport(RuntimePackageManifestDryRunReport report)
    {
        _manifestDryRunReports[report.ManifestId] = report;
        _governanceStore?.SaveManifestDryRunReport(report);
        return report;
    }

    public RuntimePackageManifestDryRunReport? GetManifestDryRunReport(string manifestId)
    {
        return string.IsNullOrWhiteSpace(manifestId) ? null : _manifestDryRunReports.GetValueOrDefault(manifestId.Trim());
    }

    public RuntimePackageManifestDryRunReport? GetManifestDryRunReportByReportId(string reportId)
    {
        return _manifestDryRunReports.Values
            .Where(report => string.Equals(report.ReportId, reportId, StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(report.PackageReadinessReportId, reportId, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(report => report.GeneratedAtUtc)
            .FirstOrDefault();
    }

    public RuntimePackageManifestDryRunReport? GetManifestDryRunReportBySessionId(string sessionId)
    {
        return _manifestDryRunReports.Values
            .Where(report => string.Equals(report.SessionId, sessionId, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(report => report.GeneratedAtUtc)
            .FirstOrDefault();
    }

    public IReadOnlyList<RuntimePackageManifestDryRunReport> ListManifestDryRunReports()
    {
        return _manifestDryRunReports.Values
            .OrderByDescending(report => report.GeneratedAtUtc)
            .ToList();
    }

    public RuntimePreviewStationCompatibilityReport SaveStationCompatibilityReport(RuntimePreviewStationCompatibilityReport report)
    {
        _stationCompatibilityReports[report.ReportId] = report;
        _governanceStore?.SaveStationCompatibilityReport(report);
        return report;
    }

    public RuntimePreviewStationCompatibilityReport? GetStationCompatibilityReport(string reportId)
    {
        return string.IsNullOrWhiteSpace(reportId) ? null : _stationCompatibilityReports.GetValueOrDefault(reportId.Trim());
    }

    public RuntimePreviewStationCompatibilityReport? GetStationCompatibilityReportBySessionId(string sessionId)
    {
        return _stationCompatibilityReports.Values
            .Where(report => string.Equals(report.SessionId, sessionId, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(report => report.GeneratedAtUtc)
            .FirstOrDefault();
    }

    public IReadOnlyList<RuntimePreviewStationCompatibilityReport> GetStationCompatibilityReportsByStationProfileId(string stationProfileId)
    {
        return _stationCompatibilityReports.Values
            .Where(report => string.Equals(report.StationProfileId, stationProfileId, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(report => report.GeneratedAtUtc)
            .ToList();
    }

    public IReadOnlyList<RuntimePreviewStationCompatibilityReport> ListStationCompatibilityReports()
    {
        return _stationCompatibilityReports.Values
            .OrderByDescending(report => report.GeneratedAtUtc)
            .ToList();
    }

    public RuntimePreviewOperatorContractValidationReport SaveOperatorContractValidationReport(RuntimePreviewOperatorContractValidationReport report)
    {
        _operatorContractValidationReports[report.ReportId] = report;
        _governanceStore?.SaveOperatorContractValidationReport(report);
        return report;
    }

    public RuntimePreviewOperatorContractValidationReport? GetOperatorContractValidationReport(string reportId)
    {
        return string.IsNullOrWhiteSpace(reportId) ? null : _operatorContractValidationReports.GetValueOrDefault(reportId.Trim());
    }

    public RuntimePreviewOperatorContractValidationReport? GetOperatorContractValidationReportBySessionId(string sessionId)
    {
        return _operatorContractValidationReports.Values
            .Where(report => string.Equals(report.SessionId, sessionId, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(report => report.GeneratedAtUtc)
            .FirstOrDefault();
    }

    public IReadOnlyList<RuntimePreviewOperatorContractValidationReport> ListOperatorContractValidationReports()
    {
        return _operatorContractValidationReports.Values
            .OrderByDescending(report => report.GeneratedAtUtc)
            .ToList();
    }

    public RuntimePreviewPreReleaseReviewReport SavePreReleaseReviewReport(RuntimePreviewPreReleaseReviewReport report)
    {
        _preReleaseReviewReports[report.ReviewId] = report;
        _governanceStore?.SavePreReleaseReviewReport(report);
        if (!string.IsNullOrWhiteSpace(report.DecisionMatrix.ReviewId) ||
            !string.IsNullOrWhiteSpace(report.DecisionMatrix.ReportId))
        {
            SaveReleaseReviewDecision(report.DecisionMatrix);
        }

        return report;
    }

    public RuntimePreviewReleaseReadinessDecisionMatrix SaveReleaseReviewDecision(RuntimePreviewReleaseReadinessDecisionMatrix decision)
    {
        var key = string.IsNullOrWhiteSpace(decision.ReportId) ? decision.ReviewId : decision.ReportId;
        if (!string.IsNullOrWhiteSpace(key))
        {
            _releaseReviewDecisions[key] = decision;
        }

        _governanceStore?.SaveReleaseReviewDecision(decision);
        return decision;
    }

    public RuntimePreviewReleaseReadinessDecisionMatrix? GetReleaseReviewDecision(string reportIdOrReviewId)
    {
        if (string.IsNullOrWhiteSpace(reportIdOrReviewId))
        {
            return null;
        }

        var key = reportIdOrReviewId.Trim();
        return _releaseReviewDecisions.GetValueOrDefault(key) ??
               _releaseReviewDecisions.Values
                   .Where(report => string.Equals(report.ReviewId, key, StringComparison.OrdinalIgnoreCase))
                   .OrderByDescending(report => report.GeneratedAtUtc)
                   .FirstOrDefault();
    }

    public IReadOnlyList<RuntimePreviewReleaseReadinessDecisionMatrix> ListReleaseReviewDecisions()
    {
        return _releaseReviewDecisions.Values
            .OrderByDescending(report => report.GeneratedAtUtc)
            .ToList();
    }

    public RuntimePreviewPreReleaseReviewReport? GetPreReleaseReviewReport(string reviewId)
    {
        return string.IsNullOrWhiteSpace(reviewId) ? null : _preReleaseReviewReports.GetValueOrDefault(reviewId.Trim());
    }

    public RuntimePreviewPreReleaseReviewReport? GetPreReleaseReviewReportBySessionId(string sessionId)
    {
        return _preReleaseReviewReports.Values
            .Where(report => string.Equals(report.SessionId, sessionId, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(report => report.GeneratedAtUtc)
            .FirstOrDefault();
    }

    public RuntimePreviewPreReleaseReviewReport? GetPreReleaseReviewReportByManifestId(string manifestId)
    {
        return _preReleaseReviewReports.Values
            .Where(report => string.Equals(report.ManifestId, manifestId, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(report => report.GeneratedAtUtc)
            .FirstOrDefault();
    }

    public IReadOnlyList<RuntimePreviewPreReleaseReviewReport> GetPreReleaseReviewReportsByCaseId(string caseId)
    {
        return _preReleaseReviewReports.Values
            .Where(report => string.Equals(report.CaseId, caseId, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(report => report.GeneratedAtUtc)
            .ToList();
    }

    public IReadOnlyList<RuntimePreviewPreReleaseReviewReport> ListPreReleaseReviewReports()
    {
        return _preReleaseReviewReports.Values
            .OrderByDescending(report => report.GeneratedAtUtc)
            .ToList();
    }
}

public sealed class RuntimePreviewResourceBroker
{
    private static readonly HashSet<string> AllowedResourceTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "camera",
        "model",
        "template",
        "flow",
        "resourceRoot"
    };

    private readonly RuntimePreviewPilotResourceCatalog _catalogBuilder;

    public RuntimePreviewResourceBroker(RuntimePreviewPilotResourceCatalog catalogBuilder)
    {
        _catalogBuilder = catalogBuilder;
    }

    public RuntimePreviewResourceHandleSet CreateSnapshot(
        RuntimePreviewPilotConfig config,
        AppConfig? appConfig,
        AiConfigStore? aiConfigStore,
        JsonElement? workflowDraft)
    {
        var catalog = _catalogBuilder.Build(config, appConfig, aiConfigStore, workflowDraft);
        var handles = catalog.Items
            .Where(item => AllowedResourceTypes.Contains(item.ResourceType))
            .Select(ToHandle)
            .ToList();
        var catalogSnapshotId = $"rp_catalog_{RuntimePreviewGovernanceHashes.HashObject(new
        {
            catalog.GeneratedAtUtc,
            items = handles.Select(item => new
            {
                item.HandleId,
                item.ResourceType,
                item.LogicalId,
                item.Source,
                item.SafeForPilot,
                item.Redacted
            })
        })[..16]}";

        return new RuntimePreviewResourceHandleSet
        {
            CatalogSnapshotId = catalogSnapshotId,
            GeneratedAtUtc = DateTimeOffset.UtcNow,
            Handles = handles,
            Catalog = catalog,
            MetadataOnly = true,
            RealResourcesTouched = false
        };
    }

    private static RuntimePreviewResourceHandle ToHandle(RuntimePreviewPilotCatalogItem item)
    {
        var logicalId = RuntimePreviewGovernanceRedactor.RedactScalar(item.Id);
        var handleId = $"rp_handle_{item.ResourceType}_{RuntimePreviewGovernanceHashes.HashString(logicalId)[..12]}";
        return new RuntimePreviewResourceHandle
        {
            HandleId = handleId,
            ResourceType = item.ResourceType,
            LogicalId = logicalId,
            DisplayName = RuntimePreviewGovernanceRedactor.RedactScalar(item.DisplayName),
            Source = RuntimePreviewGovernanceRedactor.RedactScalar(item.Source),
            MetadataOnly = true,
            SafeForPilot = item.SafeForPilot && !item.Redacted,
            Redacted = item.Redacted || logicalId.Contains("<redacted>", StringComparison.OrdinalIgnoreCase),
            ReasonCode = item.ReasonCode
        };
    }
}

public sealed class RuntimePreviewPermissionBroker
{
    public RuntimePreviewPermissionBrokerDecision EvaluateEndpointAccess(
        string endpointName,
        bool isAdmin,
        bool developerUiRequested)
    {
        if (!isAdmin)
        {
            return RuntimePreviewPermissionBrokerDecision.Deny(
                "endpoint_access",
                "runtime_preview_endpoint_admin_required",
                $"RuntimePreview Pilot endpoint '{endpointName}' requires an administrator session.",
                admin: false,
                developerUi: developerUiRequested);
        }

        return RuntimePreviewPermissionBrokerDecision.Allow(
            "endpoint_access",
            "runtime_preview_endpoint_access_allowed",
            $"RuntimePreview Pilot endpoint '{endpointName}' is allowed for an administrator session.",
            admin: true,
            developerUi: developerUiRequested);
    }

    public RuntimePreviewPermissionBrokerDecision EvaluateRuntimePreviewAccess(
        VisionAgentToolContext context,
        RuntimePreviewPilotConfig config,
        string toolName,
        RuntimePreviewPilotReadinessResult? readiness = null)
    {
        var pendingActions = new List<VisionAgentPendingAction>();
        if (!context.RuntimePreviewConsent)
        {
            pendingActions.Add(RuntimePreviewPermissionGate.BuildConsentPendingAction(
                toolName,
                "RuntimePreview requires explicit consent before simulation."));
            return RuntimePreviewPermissionBrokerDecision.Deny(
                "runtime_preview_consent",
                RuntimePreviewPermissionGate.ConsentRequiredErrorCode,
                "RuntimePreview requires explicit user consent.",
                runtimePreviewConsent: false,
                pilotEnabled: config.Enabled,
                pendingActions: pendingActions);
        }

        if (!context.AllowedPermissions.Contains(VisionAgentToolPermission.RuntimePreview))
        {
            return RuntimePreviewPermissionBrokerDecision.Deny(
                "runtime_preview_permission",
                RuntimePreviewPermissionGate.PermissionDeniedErrorCode,
                "RuntimePreview permission is not enabled in this session.",
                runtimePreviewConsent: context.RuntimePreviewConsent,
                pilotEnabled: config.Enabled);
        }

        if (!config.Enabled)
        {
            return RuntimePreviewPermissionBrokerDecision.Deny(
                "runtime_preview_pilot_enabled",
                "runtime_preview_pilot_disabled",
                "RuntimePreview Pilot is disabled.",
                runtimePreviewConsent: context.RuntimePreviewConsent,
                pilotEnabled: false);
        }

        if (!string.Equals(config.Mode, RuntimePreviewPilotConfig.ModeMetadataOnly, StringComparison.OrdinalIgnoreCase))
        {
            return RuntimePreviewPermissionBrokerDecision.Deny(
                "runtime_preview_mode",
                "runtime_preview_mode_denied",
                "RuntimePreview Pilot only allows metadata_only mode.",
                runtimePreviewConsent: context.RuntimePreviewConsent,
                pilotEnabled: config.Enabled,
                dangerousDenied: true);
        }

        if (readiness != null &&
            string.Equals(readiness.Status, RuntimePreviewPilotReadinessStatuses.Denied, StringComparison.OrdinalIgnoreCase))
        {
            return RuntimePreviewPermissionBrokerDecision.Deny(
                "runtime_preview_readiness",
                readiness.ResourceTrace.ReasonCode,
                "RuntimePreview readiness denied a dangerous resource request.",
                runtimePreviewConsent: context.RuntimePreviewConsent,
                pilotEnabled: config.Enabled,
                dangerousDenied: true);
        }

        if (readiness != null &&
            !string.Equals(readiness.Status, RuntimePreviewPilotReadinessStatuses.Ready, StringComparison.OrdinalIgnoreCase))
        {
            return RuntimePreviewPermissionBrokerDecision.Deny(
                "runtime_preview_readiness",
                readiness.ResourceTrace.ReasonCode,
                "RuntimePreview readiness is not ready for simulation.",
                runtimePreviewConsent: context.RuntimePreviewConsent,
                pilotEnabled: config.Enabled,
                pendingActions: readiness.PendingActions);
        }

        return RuntimePreviewPermissionBrokerDecision.Allow(
            "runtime_preview_metadata_simulation",
            "runtime_preview_permission_allowed",
            "RuntimePreview metadata-only simulation is allowed.",
            runtimePreviewConsent: context.RuntimePreviewConsent,
            pilotEnabled: config.Enabled);
    }

    public RuntimePreviewPermissionBrokerDecision EvaluateSessionTransition(
        RuntimePreviewSession session,
        string targetStatus)
    {
        if (IsTerminal(session.Status))
        {
            return RuntimePreviewPermissionBrokerDecision.Deny(
                "session_transition",
                "runtime_preview_session_terminal",
                $"RuntimePreview session '{session.SessionId}' is terminal and cannot transition to {targetStatus}.");
        }

        return RuntimePreviewPermissionBrokerDecision.Allow(
            "session_transition",
            "runtime_preview_session_transition_allowed",
            $"RuntimePreview session '{session.SessionId}' may transition to {targetStatus}.");
    }

    private static bool IsTerminal(string status)
    {
        return string.Equals(status, RuntimePreviewSessionStatuses.Completed, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(status, RuntimePreviewSessionStatuses.Denied, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(status, RuntimePreviewSessionStatuses.Failed, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(status, RuntimePreviewSessionStatuses.Cancelled, StringComparison.OrdinalIgnoreCase);
    }
}

public sealed class RuntimePreviewSimulatedExecutionHarness
{
    private readonly RuntimePreviewSessionStore _sessionStore;
    private readonly RuntimePreviewResourceBroker _resourceBroker;
    private readonly RuntimePreviewPilotReadinessGate _readinessGate;
    private readonly RuntimePreviewPermissionBroker _permissionBroker;
    private readonly RuntimePreviewAuditTrail _auditTrail;
    private readonly RuntimePreviewReportArchive _reportArchive;

    public RuntimePreviewSimulatedExecutionHarness(
        RuntimePreviewSessionStore sessionStore,
        RuntimePreviewResourceBroker resourceBroker,
        RuntimePreviewPilotReadinessGate readinessGate,
        RuntimePreviewPermissionBroker permissionBroker,
        RuntimePreviewAuditTrail auditTrail,
        RuntimePreviewReportArchive reportArchive)
    {
        _sessionStore = sessionStore;
        _resourceBroker = resourceBroker;
        _readinessGate = readinessGate;
        _permissionBroker = permissionBroker;
        _auditTrail = auditTrail;
        _reportArchive = reportArchive;
    }

    public RuntimePreviewSession CreateMetadataSession(
        RuntimePreviewSessionCreateRequest request,
        AppConfig appConfig,
        AiConfigStore? aiConfigStore)
    {
        var config = (request.Config ?? appConfig.Runtime.RuntimePreviewPilot).CloneNormalized();
        var workflowDraft = ResolveWorkflowDraft(request);
        var snapshot = _resourceBroker.CreateSnapshot(config, appConfig, aiConfigStore, workflowDraft);
        var session = _sessionStore.Create(
            RuntimePreviewGovernanceHashes.HashJsonElement(workflowDraft),
            RuntimePreviewGovernanceHashes.HashObject(config),
            snapshot.CatalogSnapshotId);
        var created = _auditTrail.Append(session.SessionId, RuntimePreviewAuditEventTypes.SessionCreated, new
        {
            session.SessionId,
            session.WorkflowDraftHash,
            metadataOnly = true
        });
        var catalog = _auditTrail.Append(session.SessionId, RuntimePreviewAuditEventTypes.CatalogLoaded, new
        {
            snapshot.CatalogSnapshotId,
            handleCount = snapshot.Handles.Count,
            metadataOnly = true
        });
        return _sessionStore.Update(session.SessionId, current => current with
        {
            Status = RuntimePreviewSessionStatuses.Created,
            AuditEventIds = [created.EventId, catalog.EventId]
        });
    }

    public RuntimePreviewSessionReport RunEndToEnd(
        RuntimePreviewSessionCreateRequest request,
        AppConfig appConfig,
        AiConfigStore? aiConfigStore,
        bool isAdmin = true,
        bool developerUiRequested = true)
    {
        var config = (request.Config ?? appConfig.Runtime.RuntimePreviewPilot).CloneNormalized();
        var workflowDraft = ResolveWorkflowDraft(request);
        var arguments = ResolveArguments(request, workflowDraft);
        var snapshot = _resourceBroker.CreateSnapshot(config, appConfig, aiConfigStore, workflowDraft);
        var session = _sessionStore.Create(
            RuntimePreviewGovernanceHashes.HashJsonElement(workflowDraft),
            RuntimePreviewGovernanceHashes.HashObject(config),
            snapshot.CatalogSnapshotId);

        var auditIds = new List<string>();
        void Audit(string eventType, object? payload)
        {
            var auditEvent = _auditTrail.Append(session.SessionId, eventType, payload);
            auditIds.Add(auditEvent.EventId);
            session = _sessionStore.Update(session.SessionId, current => current with
            {
                AuditEventIds = auditIds.ToList()
            });
        }

        Audit(RuntimePreviewAuditEventTypes.SessionCreated, new
        {
            session.SessionId,
            session.WorkflowDraftHash,
            metadataOnly = true
        });
        Audit(RuntimePreviewAuditEventTypes.ConfigChanged, new
        {
            pilotConfigRevision = session.PilotConfigRevision,
            config.Enabled,
            config.Mode,
            denyExternalPath = config.DenyExternalPath,
            denyImageBytes = config.DenyImageBytes
        });
        Audit(RuntimePreviewAuditEventTypes.CatalogLoaded, new
        {
            snapshot.CatalogSnapshotId,
            handleCount = snapshot.Handles.Count,
            sourceSummary = snapshot.Catalog.SourceSummary
        });
        Audit(RuntimePreviewAuditEventTypes.AllowlistChanged, RuntimePreviewPilotResourceCatalog.AllowlistCounts(config));

        var toolName = string.IsNullOrWhiteSpace(request.ToolName)
            ? RuntimePreviewResourceAllowlistResolver.MetadataToolName
            : request.ToolName.Trim();
        var context = new VisionAgentToolContext
        {
            RuntimePreviewConsent = request.RuntimePreviewConsent,
            RuntimePreviewPilot = config,
            AllowedPermissions = new HashSet<VisionAgentToolPermission>
            {
                VisionAgentToolPermission.ReadOnly,
                VisionAgentToolPermission.Simulation,
                VisionAgentToolPermission.RuntimePreview
            }
        };

        var readiness = _readinessGate.Evaluate(config, snapshot.Catalog, toolName, arguments, context);
        Audit(RuntimePreviewAuditEventTypes.ReadinessChecked, new
        {
            readiness.Status,
            readiness.CanRunMetadataPilot,
            readiness.WorkflowDraftAllowed,
            readiness.ResourceTrace.ReasonCode
        });
        session = _sessionStore.Update(session.SessionId, current => current with
        {
            Status = RuntimePreviewSessionStatuses.ReadinessChecked,
            ReadinessStatus = readiness.Status
        });

        var endpointDecision = _permissionBroker.EvaluateEndpointAccess(
            "runtime-preview-pilot-session-simulate",
            isAdmin,
            developerUiRequested);
        var permissionDecision = endpointDecision.Allowed
            ? _permissionBroker.EvaluateRuntimePreviewAccess(context, config, toolName, readiness)
            : endpointDecision;
        if (!permissionDecision.Allowed)
        {
            Audit(RuntimePreviewAuditEventTypes.PermissionDenied, permissionDecision);
            session = _sessionStore.Update(session.SessionId, current => current with
            {
                Status = RuntimePreviewSessionStatuses.Denied,
                PermissionStatus = RuntimePreviewPermissionStatuses.Denied,
                ReadinessStatus = readiness.Status
            });
            return GenerateReport(session, snapshot, readiness, permissionDecision, simulation: null);
        }

        Audit(RuntimePreviewAuditEventTypes.PermissionGranted, permissionDecision);
        session = _sessionStore.Update(session.SessionId, current => current with
        {
            Status = RuntimePreviewSessionStatuses.Authorized,
            PermissionStatus = RuntimePreviewPermissionStatuses.Allowed,
            ReadinessStatus = readiness.Status
        });

        Audit(RuntimePreviewAuditEventTypes.SimulationStarted, new
        {
            toolName,
            snapshot.CatalogSnapshotId,
            metadataOnly = true
        });
        var simulation = new RuntimePreviewSimulationResult
        {
            SessionId = session.SessionId,
            Success = true,
            Status = RuntimePreviewSessionStatuses.Simulated,
            Readiness = readiness,
            PermissionDecision = permissionDecision,
            WorkflowDraftAllowed = true,
            MetadataOnly = true,
            RealResourcesTouched = false,
            Timeline =
            [
                Step("session_created", session.SessionId),
                Step("catalog_snapshot", snapshot.CatalogSnapshotId),
                Step("readiness", readiness.Status),
                Step("authorization", permissionDecision.Status),
                Step("simulated_preview", "metadata_only")
            ],
            Artifacts =
            [
                new RuntimePreviewArtifactSummary
                {
                    ArtifactId = $"rp_artifact_{RuntimePreviewGovernanceHashes.HashString(session.SessionId)[..12]}",
                    ArtifactType = "runtime_preview_session_metadata",
                    SourceTool = "runtime_preview_simulate_metadata_session",
                    MetadataOnly = true,
                    BinaryIncluded = false,
                    ByteLength = 0,
                    Metadata = new
                    {
                        session.SessionId,
                        snapshot.CatalogSnapshotId,
                        handleCount = snapshot.Handles.Count,
                        readinessStatus = readiness.Status,
                        realResourcesTouched = false
                    }
                }
            ]
        };
        Audit(RuntimePreviewAuditEventTypes.SimulationCompleted, new
        {
            simulation.Success,
            artifactCount = simulation.Artifacts.Count,
            simulation.RealResourcesTouched
        });
        session = _sessionStore.Update(session.SessionId, current => current with
        {
            Status = RuntimePreviewSessionStatuses.Completed,
            PermissionStatus = RuntimePreviewPermissionStatuses.Allowed,
            ReadinessStatus = readiness.Status
        });

        return GenerateReport(session, snapshot, readiness, permissionDecision, simulation);
    }

    public RuntimePreviewSession? Cancel(string sessionId)
    {
        var session = _sessionStore.Get(sessionId);
        if (session == null)
        {
            return null;
        }

        var transition = _permissionBroker.EvaluateSessionTransition(session, RuntimePreviewSessionStatuses.Cancelled);
        if (!transition.Allowed)
        {
            return session;
        }

        var auditEvent = _auditTrail.Append(session.SessionId, RuntimePreviewAuditEventTypes.SessionCancelled, new
        {
            session.SessionId,
            reason = "manual_cancel"
        });
        return _sessionStore.Update(session.SessionId, current => current with
        {
            Status = RuntimePreviewSessionStatuses.Cancelled,
            AuditEventIds = current.AuditEventIds.Concat([auditEvent.EventId]).ToList()
        });
    }

    public RuntimePreviewReplayResult? Replay(string sessionId)
    {
        var report = _reportArchive.GetBySessionId(sessionId);
        var auditEvents = _auditTrail.ListForSession(sessionId);
        if (report == null && auditEvents.Count == 0)
        {
            return null;
        }

        var timeline = report?.Simulation?.Timeline ??
                       auditEvents.Select(item => new
                       {
                           name = item.EventType,
                           status = "recorded",
                           item.CreatedAtUtc,
                           metadataOnly = true,
                           realResourcesTouched = false
                       }).Cast<object>().ToList();
        _auditTrail.Append(sessionId, RuntimePreviewAuditEventTypes.SessionReplayed, new
        {
            sessionId,
            reportId = report?.ReportId,
            metadataOnly = true,
            realResourcesTouched = false
        });

        return new RuntimePreviewReplayResult
        {
            SessionId = sessionId,
            ReportId = report?.ReportId,
            ReplayedAtUtc = DateTimeOffset.UtcNow,
            Timeline = timeline,
            AuditEvents = _auditTrail.ListForSession(sessionId),
            PreviewReady = report?.PreviewReady == true,
            MetadataOnly = true,
            RealResourcesTouched = false
        };
    }

    private RuntimePreviewSessionReport GenerateReport(
        RuntimePreviewSession session,
        RuntimePreviewResourceHandleSet snapshot,
        RuntimePreviewPilotReadinessResult readiness,
        RuntimePreviewPermissionBrokerDecision permissionDecision,
        RuntimePreviewSimulationResult? simulation)
    {
        var auditEvents = _auditTrail.ListForSession(session.SessionId);
        var report = new RuntimePreviewSessionReport
        {
            PreviewReady = simulation?.Success == true,
            ReportId = $"rp_report_{Guid.NewGuid():N}",
            SessionId = session.SessionId,
            GeneratedAtUtc = DateTimeOffset.UtcNow,
            Session = session,
            ResourceHandles = snapshot.Handles,
            Readiness = readiness,
            PermissionDecision = permissionDecision,
            AuditEvents = auditEvents,
            Simulation = simulation,
            MetadataOnly = true,
            RealResourcesTouched = false
        };
        _reportArchive.Save(report);
        var auditEvent = _auditTrail.Append(session.SessionId, RuntimePreviewAuditEventTypes.ReportGenerated, new
        {
            report.ReportId,
            report.MetadataOnly,
            report.RealResourcesTouched
        });
        var updated = _sessionStore.Update(session.SessionId, current => current with
        {
            ReportId = report.ReportId,
            AuditEventIds = current.AuditEventIds.Concat([auditEvent.EventId]).ToList()
        });
        var finalReport = report with
        {
            Session = updated,
            AuditEvents = _auditTrail.ListForSession(session.SessionId)
        };
        _reportArchive.Save(finalReport);
        return finalReport;
    }

    private static object Step(string name, string status)
    {
        return new
        {
            name,
            status,
            metadataOnly = true,
            realResourcesTouched = false
        };
    }

    private static JsonElement? ResolveWorkflowDraft(RuntimePreviewSessionCreateRequest request)
    {
        if (request.WorkflowDraft is { } workflowDraft &&
            workflowDraft.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
        {
            return workflowDraft.Clone();
        }

        if (request.Arguments is { } arguments &&
            arguments.ValueKind == JsonValueKind.Object)
        {
            foreach (var propertyName in new[] { "flow", "workflowDraft", "existingFlowJson" })
            {
                if (arguments.TryGetProperty(propertyName, out var value) &&
                    value.ValueKind == JsonValueKind.Object)
                {
                    return value.Clone();
                }
            }
        }

        return null;
    }

    private static JsonElement ResolveArguments(RuntimePreviewSessionCreateRequest request, JsonElement? workflowDraft)
    {
        if (request.Arguments is { } arguments &&
            arguments.ValueKind == JsonValueKind.Object)
        {
            return arguments.Clone();
        }

        if (workflowDraft is { } draft)
        {
            return RuntimePreviewGovernanceRedactor.ToRedactedElement(new
            {
                flow = draft
            });
        }

        return RuntimePreviewGovernanceRedactor.ToRedactedElement(new
        {
            flow = new
            {
                operators = Array.Empty<object>(),
                connections = Array.Empty<object>()
            }
        });
    }

    internal static JsonElement? ResolveWorkflowDraftForGovernance(RuntimePreviewSessionCreateRequest request)
    {
        return ResolveWorkflowDraft(request);
    }
}

public sealed class RuntimePreviewGovernanceMaintenanceService
{
    private readonly RuntimePreviewGovernanceStore _store;
    private readonly RuntimePreviewAuditTrail _auditTrail;

    public RuntimePreviewGovernanceMaintenanceService(
        RuntimePreviewGovernanceStore store,
        RuntimePreviewAuditTrail auditTrail)
    {
        _store = store;
        _auditTrail = auditTrail;
    }

    public RuntimePreviewRetentionCleanupResult Cleanup(int retentionDays, int maxSessions)
    {
        var result = _store.Cleanup(retentionDays, maxSessions);
        _auditTrail.Append("runtime_preview_governance", RuntimePreviewAuditEventTypes.RetentionCleanup, result);
        return result;
    }
}

public sealed class RuntimePreviewDeployReadinessService
{
    private readonly RuntimePreviewSimulatedExecutionHarness _harness;
    private readonly RuntimePreviewReportArchive _reportArchive;
    private readonly RuntimePreviewAuditTrail _auditTrail;
    private readonly RuntimePackagePrecheckTool _precheckTool;

    public RuntimePreviewDeployReadinessService(
        RuntimePreviewSimulatedExecutionHarness harness,
        RuntimePreviewReportArchive reportArchive,
        RuntimePreviewAuditTrail auditTrail,
        RuntimePackagePrecheckTool precheckTool)
    {
        _harness = harness;
        _reportArchive = reportArchive;
        _auditTrail = auditTrail;
        _precheckTool = precheckTool;
    }

    public async Task<RuntimePreviewDeployReadinessReport> GenerateAsync(
        RuntimePreviewDeployReadinessRequest request,
        AppConfig appConfig,
        AiConfigStore? aiConfigStore,
        bool isAdmin,
        bool developerUiRequested,
        CancellationToken cancellationToken = default)
    {
        var sessionRequest = new RuntimePreviewSessionCreateRequest
        {
            Config = request.Config,
            ToolName = request.ToolName,
            Arguments = request.Arguments,
            WorkflowDraft = request.WorkflowDraft,
            RuntimePreviewConsent = request.RuntimePreviewConsent
        };
        var simulationReport = _harness.RunEndToEnd(
            sessionRequest,
            appConfig,
            aiConfigStore,
            isAdmin,
            developerUiRequested);
        var workflowDraft = RuntimePreviewSimulatedExecutionHarness.ResolveWorkflowDraftForGovernance(sessionRequest);
        var precheckArgs = BuildPrecheckArguments(workflowDraft, simulationReport, request.RequireReplay);
        var context = new VisionAgentToolContext
        {
            RuntimePreviewConsent = request.RuntimePreviewConsent,
            RuntimePreviewPilot = (request.Config ?? appConfig.Runtime.RuntimePreviewPilot).CloneNormalized(),
            AllowedPermissions = new HashSet<VisionAgentToolPermission>
            {
                VisionAgentToolPermission.ReadOnly,
                VisionAgentToolPermission.Simulation,
                VisionAgentToolPermission.RuntimePreview,
                VisionAgentToolPermission.DeploymentPrepare
            }
        };
        var precheck = await _precheckTool.ExecuteAsync(context, precheckArgs, cancellationToken);
        var precheckData = RuntimePreviewGovernanceRedactor.ToRedactedElement(precheck.Data);
        var readyForDeployment = simulationReport.PreviewReady &&
                                 precheck.Success &&
                                 ReadBool(precheckData, "readyForDeployment") == true;
        var pendingActions = new List<VisionAgentPendingAction>();
        if (simulationReport.Readiness?.PendingActions is { Count: > 0 } readinessActions)
        {
            pendingActions.AddRange(readinessActions);
        }

        pendingActions.AddRange(precheck.PendingActions);
        var report = new RuntimePreviewDeployReadinessReport
        {
            ReportId = $"rp_deploy_readiness_{Guid.NewGuid():N}",
            SessionId = simulationReport.SessionId,
            GeneratedAtUtc = DateTimeOffset.UtcNow,
            WorkflowDraftHash = simulationReport.Session.WorkflowDraftHash,
            PreviewReportId = simulationReport.ReportId,
            PreviewReady = simulationReport.PreviewReady,
            ReadyForDeployment = readyForDeployment,
            DeploymentBlocked = !readyForDeployment,
            WorkflowDraftAllowed = simulationReport.Readiness?.WorkflowDraftAllowed != false,
            Readiness = simulationReport.Readiness,
            SimulationReport = simulationReport,
            RuntimePackagePrecheck = precheckData,
            ResourceHandles = simulationReport.ResourceHandles,
            PendingActions = pendingActions,
            MetadataOnly = true,
            PackageCreated = false,
            DeploymentExecuted = false,
            RealResourcesTouched = false
        };
        _reportArchive.SaveDeployReadinessReport(report);
        _auditTrail.Append(simulationReport.SessionId, RuntimePreviewAuditEventTypes.DeployReadinessGenerated, new
        {
            report.ReportId,
            report.ReadyForDeployment,
            report.DeploymentBlocked,
            report.MetadataOnly,
            report.RealResourcesTouched
        });
        return report;
    }

    private static JsonElement BuildPrecheckArguments(
        JsonElement? workflowDraft,
        RuntimePreviewSessionReport simulationReport,
        bool requireReplay)
    {
        var flow = workflowDraft is { } draft
            ? draft
            : RuntimePreviewGovernanceRedactor.ToRedactedElement(new
            {
                operators = Array.Empty<object>(),
                connections = Array.Empty<object>()
            });
        var payload = new
        {
            flow,
            manualResourceConfirmations = BuildManualResourceConfirmations(flow, simulationReport),
            validationSummary = new
            {
                isValid = simulationReport.Readiness?.Status == RuntimePreviewPilotReadinessStatuses.Ready,
                blockingIssues = simulationReport.Readiness?.BlockingIssues ?? [],
                warnings = simulationReport.Readiness?.Issues ?? [],
                missingResources = simulationReport.Readiness?.MissingResources ?? []
            },
            dryRunSummary = new
            {
                dryRunSucceeded = simulationReport.PreviewReady,
                blockingIssues = simulationReport.PreviewReady
                    ? Array.Empty<object>()
                    :
                    [
                        new
                        {
                            code = "runtime_preview_not_ready",
                            message = "RuntimePreview metadata simulation did not reach previewReady."
                        }
                    ],
                warnings = Array.Empty<object>()
            },
            requireReplay,
            replaySummary = new
            {
                success = simulationReport.PreviewReady,
                replaySucceeded = simulationReport.PreviewReady,
                metadataOnly = true,
                realResourcesTouched = false
            }
        };
        return RuntimePreviewGovernanceRedactor.ToRedactedElement(payload);
    }

    private static IReadOnlyList<object> BuildManualResourceConfirmations(
        JsonElement flow,
        RuntimePreviewSessionReport simulationReport)
    {
        if (!simulationReport.PreviewReady ||
            !string.Equals(simulationReport.Readiness?.Status, RuntimePreviewPilotReadinessStatuses.Ready, StringComparison.OrdinalIgnoreCase) ||
            flow.ValueKind != JsonValueKind.Object ||
            !TryGetProperty(flow, "operators", out var operators) ||
            operators.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var confirmations = new List<object>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var op in operators.EnumerateArray())
        {
            if (op.ValueKind != JsonValueKind.Object ||
                !TryGetProperty(op, "parameters", out var parameters) ||
                parameters.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var operatorType = ReadStringProperty(op, "operatorType") ?? string.Empty;
            var tempId = ReadStringProperty(op, "tempId") ?? operatorType;
            if (string.IsNullOrWhiteSpace(operatorType) || string.IsNullOrWhiteSpace(tempId))
            {
                continue;
            }

            if (IsOperatorType(operatorType, "ImageAcquisition") &&
                !IsFileSource(ReadParameter(parameters, "SourceType")))
            {
                AddManualConfirmation(confirmations, seen, "camera_binding", tempId, parameters, "CameraBindingId", "CameraId");
            }

            if (IsOperatorType(operatorType, "DeepLearning") ||
                IsOperatorType(operatorType, "OnnxInference") ||
                IsOperatorType(operatorType, "SemanticSegmentation") ||
                IsOperatorType(operatorType, "AnomalyDetection"))
            {
                AddManualConfirmation(confirmations, seen, "model_resource", tempId, parameters, "ModelPath", "ModelId", "ModelCatalogPath");
            }

            if (IsOperatorType(operatorType, "TemplateMatching"))
            {
                AddManualConfirmation(confirmations, seen, "template_artifact", tempId, parameters, "TemplatePath", "TemplateId", "Template");
            }

            if (IsOperatorType(operatorType, "UnitConvert"))
            {
                AddManualConfirmation(confirmations, seen, "measurement_parameter", tempId, parameters, "Scale", "PixelScale", "CalibrationScale");
            }

            if (IsOperatorType(operatorType, "ResultOutput"))
            {
                AddManualConfirmation(confirmations, seen, "output_channel", tempId, parameters, "OutputChannelId", "OutputChannel", "Channel");
            }
        }

        return confirmations;
    }

    private static void AddManualConfirmation(
        List<object> confirmations,
        HashSet<string> seen,
        string resourceType,
        string tempId,
        JsonElement parameters,
        params string[] parameterNames)
    {
        foreach (var parameterName in parameterNames)
        {
            var value = ReadParameter(parameters, parameterName);
            if (IsMissingParameterValue(value))
            {
                continue;
            }

            var resourceKey = $"{tempId}.{parameterName}";
            var uniqueKey = $"{resourceType}|{resourceKey}";
            if (!seen.Add(uniqueKey))
            {
                return;
            }

            confirmations.Add(new
            {
                confirmedAtUtc = DateTimeOffset.UtcNow,
                actor = "runtime_preview_metadata_bridge",
                resourceType,
                operatorId = tempId,
                parameterName,
                resourceKey,
                metadataOnly = true
            });
            return;
        }
    }

    private static string? ReadParameter(JsonElement parameters, string parameterName)
    {
        return TryGetProperty(parameters, parameterName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static string? ReadStringProperty(JsonElement element, string propertyName)
    {
        return TryGetProperty(element, propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static bool IsOperatorType(string operatorType, string expected)
    {
        return string.Equals(operatorType, expected, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsFileSource(string? sourceType)
    {
        return string.Equals(sourceType, "file", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(sourceType, "image", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(sourceType, "path", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsMissingParameterValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        return value.StartsWith("<pending", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("todo", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryGetProperty(JsonElement element, string propertyName, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }

    private static bool? ReadBool(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null
        };
    }
}

public sealed class RuntimePreviewPackageReadinessBridge
{
    private readonly RuntimePreviewDeployReadinessService _deployReadinessService;
    private readonly RuntimePackageManifestDryRunService _manifestDryRunService;
    private readonly RuntimePreviewReportArchive _reportArchive;
    private readonly RuntimePreviewAuditTrail _auditTrail;

    public RuntimePreviewPackageReadinessBridge(
        RuntimePreviewDeployReadinessService deployReadinessService,
        RuntimePreviewReportArchive reportArchive,
        RuntimePreviewAuditTrail auditTrail)
        : this(
            deployReadinessService,
            new RuntimePackageManifestDryRunService(reportArchive, auditTrail),
            reportArchive,
            auditTrail)
    {
    }

    public RuntimePreviewPackageReadinessBridge(
        RuntimePreviewDeployReadinessService deployReadinessService,
        RuntimePackageManifestDryRunService manifestDryRunService,
        RuntimePreviewReportArchive reportArchive,
        RuntimePreviewAuditTrail auditTrail)
    {
        _deployReadinessService = deployReadinessService;
        _manifestDryRunService = manifestDryRunService;
        _reportArchive = reportArchive;
        _auditTrail = auditTrail;
    }

    public async Task<RuntimePreviewPackageReadinessReport> GenerateAsync(
        RuntimePreviewPackageReadinessRequest request,
        AppConfig appConfig,
        AiConfigStore? aiConfigStore,
        bool isAdmin,
        bool developerUiRequested,
        CancellationToken cancellationToken = default)
    {
        var deployReport = await _deployReadinessService.GenerateAsync(
            new RuntimePreviewDeployReadinessRequest
            {
                Config = request.Config,
                ToolName = request.ToolName,
                Arguments = request.Arguments,
                WorkflowDraft = request.WorkflowDraft,
                RuntimePreviewConsent = request.RuntimePreviewConsent,
                RequireReplay = request.RequireReplay
            },
            appConfig,
            aiConfigStore,
            isAdmin,
            developerUiRequested,
            cancellationToken);

        var blockingIssues = BuildBlockingIssues(deployReport);
        var operatorTrace = BuildOperatorTrace(deployReport);
        var resourceTrace = BuildResourceTrace(deployReport);
        var dependencyTrace = BuildDependencyTrace(deployReport, blockingIssues);
        var riskSummary = BuildRiskSummary(deployReport, blockingIssues);
        var draftReport = new RuntimePreviewPackageReadinessReport
        {
            ReportId = $"rp_package_readiness_{Guid.NewGuid():N}",
            SessionId = deployReport.SessionId,
            GeneratedAtUtc = DateTimeOffset.UtcNow,
            WorkflowDraftHash = deployReport.WorkflowDraftHash,
            PreviewReportId = deployReport.PreviewReportId,
            DeployReadinessReportId = deployReport.ReportId,
            ReadyForPackage = deployReport.ReadyForDeployment,
            PackageReviewAllowed = deployReport.ReadyForDeployment,
            PackageBlocked = !deployReport.ReadyForDeployment,
            PackageCreated = false,
            DeploymentExecuted = false,
            BlockingIssues = blockingIssues,
            BlockedReason = blockingIssues.FirstOrDefault() ?? string.Empty,
            MissingResources = deployReport.Readiness?.MissingResources ?? [],
            RiskSummary = riskSummary,
            PackageRiskLevel = ResolvePackageRiskLevel(deployReport, blockingIssues),
            PackageReviewExplanation = BuildPackageReviewExplanation(deployReport, blockingIssues),
            PendingActions = deployReport.PendingActions,
            OperatorTrace = operatorTrace,
            ResourceTrace = resourceTrace,
            DependencyTrace = dependencyTrace,
            OperatorContract = operatorTrace.Select(item => $"operator:{item}:metadata_contract").ToList(),
            ResourceContract = resourceTrace.Select(item => $"resource:{item}:metadata_only").ToList(),
            WorkflowDraftAllowed = deployReport.WorkflowDraftAllowed,
            RuntimePackagePrecheck = deployReport.RuntimePackagePrecheck,
            MetadataOnly = true,
            RealResourcesTouched = false
        };
        var manifestReport = _manifestDryRunService.GenerateFromPackageReadiness(draftReport, request);
        var report = draftReport with
        {
            ManifestDryRunReportId = manifestReport.ManifestId,
            PackageReviewAllowed = manifestReport.PackageReviewAllowed,
            PackageBlocked = !manifestReport.PackageReviewAllowed,
            PackageRiskLevel = manifestReport.RiskLevel,
            DependencyTrace = manifestReport.DependencyTrace,
            OperatorContract = manifestReport.OperatorTrace.Select(item => $"operator:{item}:manifest_contract").ToList(),
            ResourceContract = manifestReport.ResourceTrace.Select(item => $"resource:{item}:manifest_contract").ToList(),
            BlockingIssues = manifestReport.BlockedReasons.Count > 0 ? manifestReport.BlockedReasons : blockingIssues,
            BlockedReason = manifestReport.BlockedReasons.FirstOrDefault() ?? string.Empty,
            PackageReviewExplanation = manifestReport.PackageReviewAllowed
                ? "Manifest dry-run found all metadata dependencies needed for package review. No package was created."
                : "Manifest dry-run blocked package review; workflow edits remain allowed while dependencies or policy findings are resolved."
        };
        _reportArchive.SavePackageReadinessReport(report);
        _auditTrail.Append(deployReport.SessionId, RuntimePreviewAuditEventTypes.PackageReadinessGenerated, new
        {
            report.ReportId,
            report.ReadyForPackage,
            report.PackageReviewAllowed,
            report.ManifestDryRunReportId,
            report.PackageBlocked,
            report.PackageCreated,
            report.DeploymentExecuted,
            report.MetadataOnly,
            report.RealResourcesTouched
        });
        return report;
    }

    private static IReadOnlyList<string> BuildBlockingIssues(RuntimePreviewDeployReadinessReport report)
    {
        var issues = new List<string>();
        if (!report.PreviewReady)
        {
            issues.Add("RuntimePreview metadata simulation is not previewReady.");
        }

        if (report.Readiness?.Status == RuntimePreviewPilotReadinessStatuses.Denied)
        {
            issues.Add($"RuntimePreview readiness denied: {report.Readiness.ResourceTrace.ReasonCode}");
        }
        else if (report.Readiness?.Status == RuntimePreviewPilotReadinessStatuses.NotReady)
        {
            issues.Add("RuntimePreview readiness is not_ready; required metadata is unresolved.");
        }

        foreach (var action in report.PendingActions)
        {
            if (!string.IsNullOrWhiteSpace(action.ActionType))
            {
                issues.Add($"Pending engineer action: {action.ActionType}");
            }
        }

        if (!report.ReadyForDeployment)
        {
            issues.Add("Runtime package precheck did not approve package readiness.");
        }

        return issues.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static IReadOnlyList<string> BuildOperatorTrace(RuntimePreviewDeployReadinessReport report)
    {
        var raw = report.RuntimePackagePrecheck.GetRawText();
        var matches = Regex.Matches(raw, @"""operatorType""\s*:\s*""(?<type>[^""]+)""", RegexOptions.IgnoreCase);
        var operators = matches
            .Select(match => RuntimePreviewGovernanceRedactor.RedactScalar(match.Groups["type"].Value))
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return operators.Count == 0 ? ["metadata_operator_trace_unavailable"] : operators;
    }

    private static IReadOnlyList<string> BuildResourceTrace(RuntimePreviewDeployReadinessReport report)
    {
        var trace = new List<string>();
        if (report.Readiness?.ResourceTrace is { } resourceTrace)
        {
            trace.Add($"allowed={resourceTrace.Allowed}");
            trace.Add($"reason={resourceTrace.ReasonCode}");
            trace.Add($"resourceType={resourceTrace.ResourceType}");
        }

        trace.AddRange(report.ResourceHandles.Select(handle =>
            $"{handle.ResourceType}:{handle.HandleId}:{(handle.SafeForPilot ? "safe" : "blocked")}"));
        return trace
            .Select(RuntimePreviewGovernanceRedactor.RedactScalar)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .ToList();
    }

    private static IReadOnlyList<string> BuildDependencyTrace(RuntimePreviewDeployReadinessReport report, IReadOnlyList<string> blockingIssues)
    {
        var trace = new List<string>();
        trace.AddRange(report.ResourceHandles.Select(handle =>
            $"dependency:{handle.ResourceType}:{handle.HandleId}:{(handle.SafeForPilot ? "resolved" : "blocked")}"));
        trace.AddRange(blockingIssues.Select(issue => $"blocking:{issue}"));
        if (report.ReadyForDeployment)
        {
            trace.Add("runtime_package_precheck:metadata_ready");
        }

        return trace
            .Select(RuntimePreviewGovernanceRedactor.RedactScalar)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string ResolvePackageRiskLevel(RuntimePreviewDeployReadinessReport report, IReadOnlyList<string> blockingIssues)
    {
        if (report.Readiness?.Status == RuntimePreviewPilotReadinessStatuses.Denied)
        {
            return "denied";
        }

        if (blockingIssues.Count == 0 && report.ReadyForDeployment)
        {
            return "low";
        }

        return report.Readiness?.MissingResources.Count > 0 ? "high" : "medium";
    }

    private static string BuildPackageReviewExplanation(RuntimePreviewDeployReadinessReport report, IReadOnlyList<string> blockingIssues)
    {
        if (report.ReadyForDeployment && blockingIssues.Count == 0)
        {
            return "Workflow draft can enter metadata package review. No real package, deployment, or hot-load is executed.";
        }

        if (report.WorkflowDraftAllowed)
        {
            return "Workflow draft remains editable, but package review is blocked until readiness, dependencies, and precheck issues are resolved.";
        }

        return "Package review is blocked by metadata readiness policy.";
    }

    private static string BuildRiskSummary(RuntimePreviewDeployReadinessReport report, IReadOnlyList<string> blockingIssues)
    {
        if (report.ReadyForDeployment)
        {
            return "Metadata checks passed. The flow can proceed to package review, but no package was created and no deployment was executed.";
        }

        if (report.Readiness?.Status == RuntimePreviewPilotReadinessStatuses.Denied)
        {
            return "Package is blocked because the request hit a denied resource or dangerous intent. Engineer must replace the unsafe metadata handle before package review.";
        }

        if (report.Readiness?.MissingResources.Count > 0)
        {
            return "Package is blocked because required camera/template/model/output metadata is still missing. Workflow editing remains allowed.";
        }

        return blockingIssues.Count > 0
            ? "Package is blocked by metadata precheck issues. Review pending actions before attempting any package workflow."
            : "Package is blocked by an unresolved metadata readiness condition.";
    }
}

public sealed class RuntimePackageManifestDryRunService
{
    private readonly RuntimePreviewReportArchive _reportArchive;
    private readonly RuntimePreviewAuditTrail _auditTrail;

    public RuntimePackageManifestDryRunService(
        RuntimePreviewReportArchive reportArchive,
        RuntimePreviewAuditTrail auditTrail)
    {
        _reportArchive = reportArchive;
        _auditTrail = auditTrail;
    }

    public RuntimePackageManifestDryRunReport GenerateFromPackageReadiness(
        RuntimePreviewPackageReadinessReport packageReport,
        RuntimePreviewPackageReadinessRequest request)
    {
        var workflowDraft = ResolveWorkflowDraft(request.WorkflowDraft, request.Arguments);
        var operatorSummaries = ExtractOperators(workflowDraft);
        var operatorTypes = operatorSummaries
            .Select(item => item.OperatorType)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(RuntimePreviewGovernanceRedactor.RedactScalar)
            .ToList();
        var cameraBindings = operatorSummaries.SelectMany(item => item.CameraBindings).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var templates = operatorSummaries.SelectMany(item => item.TemplateDependencies).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var models = operatorSummaries.SelectMany(item => item.ModelDependencies).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var outputs = operatorSummaries.SelectMany(item => item.OutputChannels).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var missingDependencies = BuildMissingDependencies(packageReport, operatorSummaries);
        var blockedReasons = BuildManifestBlockedReasons(packageReport, missingDependencies);
        var packageReviewAllowed = packageReport.ReadyForPackage &&
                                   !packageReport.PackageBlocked &&
                                   missingDependencies.Count == 0 &&
                                   !blockedReasons.Any(item => item.Contains("denied", StringComparison.OrdinalIgnoreCase));
        var dependencyTrace = BuildManifestDependencyTrace(cameraBindings, templates, models, outputs, missingDependencies);
        var resourceDependencies = cameraBindings
            .Concat(templates.Select(item => $"template:{item}"))
            .Concat(models.Select(item => $"model:{item}"))
            .Concat(outputs.Select(item => $"output:{item}"))
            .Select(RuntimePreviewGovernanceRedactor.RedactScalar)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var manifestHashPayload = new
        {
            packageReport.WorkflowDraftHash,
            operatorTypes,
            resourceDependencies,
            missingDependencies,
            packageReviewAllowed
        };
        var report = new RuntimePackageManifestDryRunReport
        {
            ManifestId = $"rp_manifest_dry_run_{Guid.NewGuid():N}",
            ReportId = $"rp_manifest_report_{Guid.NewGuid():N}",
            SessionId = packageReport.SessionId,
            PackageReadinessReportId = packageReport.ReportId,
            GeneratedAtUtc = DateTimeOffset.UtcNow,
            WorkflowDraftHash = packageReport.WorkflowDraftHash,
            ManifestHash = RuntimePreviewGovernanceHashes.HashObject(manifestHashPayload),
            OperatorCount = operatorSummaries.Count,
            OperatorTypes = operatorTypes,
            ResourceDependencies = resourceDependencies,
            ModelDependencies = models,
            TemplateDependencies = templates,
            CameraBindings = cameraBindings,
            OutputChannels = outputs,
            MissingDependencies = missingDependencies,
            BlockedReasons = blockedReasons,
            DependencyTrace = dependencyTrace,
            OperatorTrace = operatorSummaries.Select(item => $"{item.TempId}:{item.OperatorType}").Select(RuntimePreviewGovernanceRedactor.RedactScalar).ToList(),
            ResourceTrace = packageReport.ResourceTrace,
            RiskLevel = ResolveManifestRisk(packageReport, missingDependencies, blockedReasons),
            PackageReviewAllowed = packageReviewAllowed,
            WorkflowDraftAllowed = packageReport.WorkflowDraftAllowed,
            ManifestArtifactGenerated = false,
            PackageCreated = false,
            DeploymentExecuted = false,
            MetadataOnly = true,
            RealResourcesTouched = false
        };
        _reportArchive.SaveManifestDryRunReport(report);
        _auditTrail.Append(packageReport.SessionId, RuntimePreviewAuditEventTypes.ManifestDryRunGenerated, new
        {
            report.ManifestId,
            report.PackageReviewAllowed,
            report.RiskLevel,
            report.PackageCreated,
            report.DeploymentExecuted,
            report.MetadataOnly,
            report.RealResourcesTouched
        });
        return report;
    }

    private static JsonElement? ResolveWorkflowDraft(JsonElement? workflowDraft, JsonElement? arguments)
    {
        if (workflowDraft is { ValueKind: not JsonValueKind.Undefined } direct)
        {
            return direct;
        }

        if (arguments is { ValueKind: JsonValueKind.Object } args &&
            args.TryGetProperty("flow", out var flow))
        {
            return flow;
        }

        return null;
    }

    private static IReadOnlyList<ManifestOperatorSummary> ExtractOperators(JsonElement? workflowDraft)
    {
        if (workflowDraft is not { ValueKind: JsonValueKind.Object } draft ||
            !draft.TryGetProperty("operators", out var operators) ||
            operators.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var result = new List<ManifestOperatorSummary>();
        foreach (var op in operators.EnumerateArray())
        {
            var type = ReadString(op, "operatorType");
            var tempId = ReadString(op, "tempId");
            var parameters = op.TryGetProperty("parameters", out var p) && p.ValueKind == JsonValueKind.Object
                ? p
                : default;
            result.Add(new ManifestOperatorSummary(
                RuntimePreviewGovernanceRedactor.RedactScalar(tempId),
                RuntimePreviewGovernanceRedactor.RedactScalar(type),
                ExtractParameterValues(parameters, "CameraBindingId", "CameraId"),
                ExtractParameterValues(parameters, "ModelId", "ModelCatalogPath"),
                ExtractParameterValues(parameters, "TemplateId"),
                ExtractParameterValues(parameters, "OutputChannelId", "OutputChannel", "Channel")));
        }

        return result;
    }

    private static IReadOnlyList<string> ExtractParameterValues(JsonElement parameters, params string[] names)
    {
        if (parameters.ValueKind != JsonValueKind.Object)
        {
            return [];
        }

        return names
            .Select(name => parameters.TryGetProperty(name, out var value) ? RuntimePreviewGovernanceRedactor.RedactScalar(value.GetString()) : string.Empty)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string ReadString(JsonElement element, string propertyName)
    {
        return element.ValueKind == JsonValueKind.Object &&
               element.TryGetProperty(propertyName, out var value) &&
               value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;
    }

    private static IReadOnlyList<string> BuildMissingDependencies(
        RuntimePreviewPackageReadinessReport packageReport,
        IReadOnlyList<ManifestOperatorSummary> operators)
    {
        var missing = new List<string>();
        if (operators.Count == 0)
        {
            missing.Add("operator_graph_missing");
        }

        if (operators.Any(item => string.Equals(item.OperatorType, "ImageAcquisition", StringComparison.OrdinalIgnoreCase) &&
                                  item.CameraBindings.Count == 0))
        {
            missing.Add("camera_binding_missing");
        }

        if (operators.Any(item => string.Equals(item.OperatorType, "TemplateMatching", StringComparison.OrdinalIgnoreCase) &&
                                  item.TemplateDependencies.Count == 0))
        {
            missing.Add("template_dependency_missing");
        }

        if (operators.Any(item => string.Equals(item.OperatorType, "DeepLearning", StringComparison.OrdinalIgnoreCase) &&
                                  item.ModelDependencies.Count == 0))
        {
            missing.Add("model_dependency_missing");
        }

        if (operators.Any(item => string.Equals(item.OperatorType, "ResultOutput", StringComparison.OrdinalIgnoreCase) &&
                                  item.OutputChannels.Count == 0))
        {
            missing.Add("output_channel_missing");
        }

        missing.AddRange(packageReport.MissingResources.Select(item => RuntimePreviewGovernanceRedactor.RedactScalar(JsonSerializer.Serialize(item))));
        missing.AddRange(packageReport.BlockingIssues.Where(item => item.Contains("missing", StringComparison.OrdinalIgnoreCase) ||
                                                                    item.Contains("not_ready", StringComparison.OrdinalIgnoreCase)));
        return missing
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IReadOnlyList<string> BuildManifestBlockedReasons(
        RuntimePreviewPackageReadinessReport packageReport,
        IReadOnlyList<string> missingDependencies)
    {
        var reasons = new List<string>();
        if (packageReport.PackageBlocked)
        {
            reasons.Add(packageReport.BlockedReason);
            reasons.AddRange(packageReport.BlockingIssues);
        }

        reasons.AddRange(missingDependencies.Select(item => $"manifest_dependency_blocked:{item}"));
        return reasons
            .Select(RuntimePreviewGovernanceRedactor.RedactScalar)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IReadOnlyList<string> BuildManifestDependencyTrace(
        IReadOnlyList<string> cameraBindings,
        IReadOnlyList<string> templates,
        IReadOnlyList<string> models,
        IReadOnlyList<string> outputs,
        IReadOnlyList<string> missing)
    {
        return cameraBindings.Select(item => $"camera:{item}:metadata")
            .Concat(templates.Select(item => $"template:{item}:metadata"))
            .Concat(models.Select(item => $"model:{item}:metadata"))
            .Concat(outputs.Select(item => $"output:{item}:metadata"))
            .Concat(missing.Select(item => $"missing:{item}"))
            .Select(RuntimePreviewGovernanceRedactor.RedactScalar)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string ResolveManifestRisk(
        RuntimePreviewPackageReadinessReport packageReport,
        IReadOnlyList<string> missingDependencies,
        IReadOnlyList<string> blockedReasons)
    {
        if (blockedReasons.Any(item => item.Contains("denied", StringComparison.OrdinalIgnoreCase) ||
                                       item.Contains("dangerous", StringComparison.OrdinalIgnoreCase)))
        {
            return "denied";
        }

        if (missingDependencies.Count > 0)
        {
            return "high";
        }

        return packageReport.ReadyForPackage && !packageReport.PackageBlocked ? "low" : "medium";
    }

    private sealed record ManifestOperatorSummary(
        string TempId,
        string OperatorType,
        IReadOnlyList<string> CameraBindings,
        IReadOnlyList<string> ModelDependencies,
        IReadOnlyList<string> TemplateDependencies,
        IReadOnlyList<string> OutputChannels);
}

internal sealed record RuntimePreviewWorkflowOperator(
    string TempId,
    string OperatorType,
    IReadOnlyDictionary<string, string> Parameters);

internal static class RuntimePreviewWorkflowInspector
{
    public static JsonElement? ResolveWorkflowDraft(JsonElement? workflowDraft, JsonElement? arguments)
    {
        if (workflowDraft is { ValueKind: JsonValueKind.Object } direct)
        {
            return direct.Clone();
        }

        if (arguments is { ValueKind: JsonValueKind.Object } args)
        {
            foreach (var propertyName in new[] { "flow", "workflowDraft", "existingFlowJson" })
            {
                if (args.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.Object)
                {
                    return value.Clone();
                }
            }
        }

        return null;
    }

    public static IReadOnlyList<RuntimePreviewWorkflowOperator> ExtractOperators(JsonElement? workflowDraft)
    {
        if (workflowDraft is not { ValueKind: JsonValueKind.Object } draft ||
            !draft.TryGetProperty("operators", out var operators) ||
            operators.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var result = new List<RuntimePreviewWorkflowOperator>();
        foreach (var op in operators.EnumerateArray())
        {
            var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (op.TryGetProperty("parameters", out var p) && p.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in p.EnumerateObject())
                {
                    parameters[property.Name] = property.Value.ValueKind == JsonValueKind.String
                        ? RuntimePreviewGovernanceRedactor.RedactScalar(property.Value.GetString())
                        : RuntimePreviewGovernanceRedactor.RedactScalar(property.Value.GetRawText());
                }
            }

            result.Add(new RuntimePreviewWorkflowOperator(
                ReadString(op, "tempId"),
                ReadString(op, "operatorType"),
                parameters));
        }

        return result;
    }

    public static string GetParameter(RuntimePreviewWorkflowOperator op, params string[] names)
    {
        foreach (var name in names)
        {
            if (op.Parameters.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return string.Empty;
    }

    public static IReadOnlyList<string> GetParameterValues(IEnumerable<RuntimePreviewWorkflowOperator> operators, params string[] names)
    {
        return operators
            .Select(op => GetParameter(op, names))
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static bool ContainsDirectPlcOrStationIntent(
        IEnumerable<RuntimePreviewWorkflowOperator> operators,
        RuntimePackageManifestDryRunReport manifest)
    {
        if (manifest.BlockedReasons.Any(item => item.Contains("plc", StringComparison.OrdinalIgnoreCase) ||
                                                item.Contains("station", StringComparison.OrdinalIgnoreCase) ||
                                                item.Contains("deployment", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        foreach (var op in operators)
        {
            if (op.OperatorType.Contains("Communication", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(op.OperatorType, "DatabaseWrite", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(op.OperatorType, "HttpRequest", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(op.OperatorType, "MqttPublish", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            foreach (var pair in op.Parameters)
            {
                if (pair.Key.Contains("plc", StringComparison.OrdinalIgnoreCase) ||
                    pair.Key.Contains("station", StringComparison.OrdinalIgnoreCase) ||
                    pair.Value.Equals("plc", StringComparison.OrdinalIgnoreCase) ||
                    pair.Value.Contains("station", StringComparison.OrdinalIgnoreCase) ||
                    pair.Value.Contains("deploy", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }

    public static IReadOnlyList<string> InferModelKinds(
        IReadOnlyList<RuntimePreviewWorkflowOperator> operators,
        RuntimePackageManifestDryRunReport manifest)
    {
        var explicitKinds = GetParameterValues(operators, "ModelKind", "ModelType", "TaskKind");
        var inferred = new List<string>(explicitKinds);
        foreach (var model in manifest.ModelDependencies)
        {
            if (model.Contains("segment", StringComparison.OrdinalIgnoreCase))
            {
                inferred.Add("segmentation");
            }
            else if (model.Contains("class", StringComparison.OrdinalIgnoreCase))
            {
                inferred.Add("classification");
            }
            else if (!string.IsNullOrWhiteSpace(model))
            {
                inferred.Add("detection");
            }
        }

        return inferred
            .Select(RuntimePreviewGovernanceRedactor.RedactScalar)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static string RequiredRuntimeVersion(RuntimePackageManifestDryRunReport manifest)
    {
        if (manifest.OperatorTypes.Any(item => string.Equals(item, "DeepLearning", StringComparison.OrdinalIgnoreCase)))
        {
            return "1.4.0";
        }

        if (manifest.OperatorCount > 10)
        {
            return "1.3.0";
        }

        return "1.2.0";
    }

    private static string ReadString(JsonElement element, string propertyName)
    {
        return element.ValueKind == JsonValueKind.Object &&
               element.TryGetProperty(propertyName, out var value) &&
               value.ValueKind == JsonValueKind.String
            ? RuntimePreviewGovernanceRedactor.RedactScalar(value.GetString())
            : string.Empty;
    }
}

public sealed class RuntimePreviewStationProfileCatalog
{
    private readonly RuntimePreviewGovernanceStore? _governanceStore;

    public RuntimePreviewStationProfileCatalog()
    {
    }

    public RuntimePreviewStationProfileCatalog(RuntimePreviewGovernanceStore governanceStore)
    {
        _governanceStore = governanceStore;
    }

    private static readonly IReadOnlyList<string> TraditionalOperators =
    [
        "ImageAcquisition",
        "Preprocessing",
        "Filtering",
        "TemplateMatching",
        "CircleMeasurement",
        "MeasureDistance",
        "LineMeasurement",
        "GapMeasurement",
        "AngleMeasurement",
        "GeoMeasurement",
        "GeometricFitting",
        "GeometricTolerance",
        "ResultOutput",
        "ResultJudgment",
        "BlobAnalysis",
        "BlobLabeling",
        "Thresholding",
        "AdaptiveThreshold",
        "EdgeDetection",
        "ContourDetection",
        "ContourMeasurement",
        "ImageCrop",
        "ImageResize",
        "ImageNormalize",
        "ImageRotate",
        "ColorConversion",
        "ColorDetection",
        "ColorMeasurement",
        "ShapeMatching",
        "CaliperTool",
        "ArcCaliper",
        "CodeRecognition",
        "OcrRecognition",
        "RoiManager",
        "RoiTransform"
    ];

    private static readonly IReadOnlyList<string> DeepLearningOperators =
    [
        "DeepLearning",
        "OnnxInference",
        "SemanticSegmentation",
        "SurfaceDefectDetection",
        "AnomalyDetection"
    ];

    public RuntimePreviewStationProfileDocument BuildProfiles()
    {
        var profiles = CreateProfiles();
        var document = new RuntimePreviewStationProfileDocument
        {
            GeneratedAtUtc = DateTimeOffset.UtcNow,
            ProfileCount = profiles.Count,
            Profiles = profiles,
            MetadataOnly = true,
            RealResourcesTouched = false
        };
        _governanceStore?.SaveStationProfileSnapshot(document);
        return document;
    }

    public RuntimePreviewStationProfile GetOrDefault(string? stationProfileId)
    {
        var profiles = CreateProfiles();
        if (!string.IsNullOrWhiteSpace(stationProfileId))
        {
            var match = profiles.FirstOrDefault(profile =>
                string.Equals(profile.StationProfileId, stationProfileId.Trim(), StringComparison.OrdinalIgnoreCase));
            if (match != null)
            {
                return match;
            }
        }

        return profiles[0];
    }

    public static IReadOnlyList<RuntimePreviewStationProfile> CreateProfiles()
    {
        return
        [
            Profile(
                "sp-release-standard-v14",
                "standard_vision_ipc",
                "1.4.0",
                TraditionalOperators,
                ["detection", "classification"],
                ["line-cam", "side-cam"],
                ["qa-metadata", "metadata-summary", "local-log"],
                12,
                "standard release review approval for medium risk only",
                "low_or_medium_metadata_risk_allowed"),
            Profile(
                "sp-dl-review-v14",
                "deep_learning_review_ipc",
                "1.4.0",
                TraditionalOperators.Concat(DeepLearningOperators).ToList(),
                ["detection", "classification", "segmentation", "anomaly"],
                ["line-cam", "side-cam"],
                ["qa-metadata", "metadata-summary"],
                10,
                "deep learning release approval required",
                "medium_model_risk_requires_approval"),
            Profile(
                "sp-low-ipc-v12",
                "low_spec_ipc",
                "1.2.0",
                TraditionalOperators.Where(item => !string.Equals(item, "CaliperTool", StringComparison.OrdinalIgnoreCase)).ToList(),
                [],
                ["line-cam"],
                ["qa-metadata"],
                3,
                "release blocked when operator count exceeds limit",
                "high_when_capacity_exceeded"),
            Profile(
                "sp-multi-camera-v14",
                "multi_camera_station",
                "1.4.0",
                TraditionalOperators,
                ["detection", "classification"],
                ["line-cam", "side-cam", "top-cam", "angle-cam"],
                ["qa-metadata", "metadata-summary", "local-log"],
                14,
                "multi camera metadata review required",
                "medium_when_multiple_camera_bindings"),
            Profile(
                "sp-output-lite-v14",
                "output_lite_station",
                "1.4.0",
                TraditionalOperators,
                [],
                ["line-cam"],
                ["local-log"],
                8,
                "output remap required for qa metadata channels",
                "high_when_output_channel_missing"),
            Profile(
                "sp-detection-only-v14",
                "model_limited_station",
                "1.4.0",
                TraditionalOperators.Concat(["DeepLearning"]).ToList(),
                ["detection"],
                ["line-cam", "side-cam"],
                ["qa-metadata"],
                8,
                "model kind approval limited to detection metadata",
                "high_when_model_kind_unsupported"),
            Profile(
                "sp-legacy-runtime-v12",
                "legacy_runtime_station",
                "1.2.0",
                TraditionalOperators.Where(item => !string.Equals(item, "CaliperTool", StringComparison.OrdinalIgnoreCase)).ToList(),
                [],
                ["line-cam"],
                ["qa-metadata", "local-log"],
                6,
                "runtime upgrade approval required",
                "high_when_runtime_version_too_low"),
            Profile(
                "sp-multi-station-v14",
                "multi_station_review",
                "1.4.0",
                TraditionalOperators.Concat(["DeepLearning"]).ToList(),
                ["detection", "classification"],
                ["line-cam", "side-cam", "top-cam"],
                ["qa-metadata", "metadata-summary"],
                16,
                "multi station engineer approval required",
                "medium_multi_station_review"),
            Profile(
                "sp-plc-denied-v14",
                "plc_denied_station",
                "1.4.0",
                TraditionalOperators.Concat(["ModbusCommunication", "SiemensS7Communication", "MitsubishiMcCommunication", "OmronFinsCommunication"]).ToList(),
                [],
                ["line-cam"],
                ["qa-metadata"],
                8,
                "PLC writes always denied in preview",
                "denied_when_plc_or_station_intent"),
            Profile(
                "sp-release-approval-v14",
                "release_approval_station",
                "1.4.0",
                TraditionalOperators.Concat(DeepLearningOperators).ToList(),
                ["detection", "classification", "segmentation"],
                ["line-cam", "side-cam"],
                ["qa-metadata", "metadata-summary"],
                12,
                "release approval required for medium and model risk",
                "medium_requires_engineer_approval"),
            Profile(
                "sp-template-only-v14",
                "template_only_station",
                "1.4.0",
                ["ImageAcquisition", "TemplateMatching", "ShapeMatching", "ResultJudgment", "ResultOutput"],
                [],
                ["line-cam"],
                ["qa-metadata", "local-log"],
                6,
                "template metadata dependency must be closed",
                "high_when_template_missing"),
            Profile(
                "sp-measurement-only-v14",
                "measurement_only_station",
                "1.4.0",
                ["ImageAcquisition", "CircleMeasurement", "MeasureDistance", "LineMeasurement", "GapMeasurement", "AngleMeasurement", "CaliperTool", "ArcCaliper", "ResultJudgment", "ResultOutput"],
                [],
                ["line-cam", "side-cam"],
                ["qa-metadata"],
                10,
                "measurement calibration metadata review required",
                "medium_for_measurement_release")
        ];
    }

    private static RuntimePreviewStationProfile Profile(
        string stationProfileId,
        string stationType,
        string runtimeVersion,
        IEnumerable<string> supportedOperatorTypes,
        IEnumerable<string> supportedModelKinds,
        IEnumerable<string> cameraBindingSlots,
        IEnumerable<string> outputChannelKinds,
        int maxOperatorCount,
        string approvalPolicy,
        string riskPolicy)
    {
        return new RuntimePreviewStationProfile
        {
            StationProfileId = stationProfileId,
            StationType = stationType,
            RuntimeVersion = runtimeVersion,
            SupportedOperatorTypes = supportedOperatorTypes.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            SupportedModelKinds = supportedModelKinds.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            CameraBindingSlots = cameraBindingSlots.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            OutputChannelKinds = outputChannelKinds.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            MaxOperatorCount = maxOperatorCount,
            PlcWriteAllowed = false,
            ResourcePolicy = new RuntimePreviewStationProfileResourcePolicy
            {
                MetadataOnly = true,
                RealResourceAccessAllowed = false,
                ImageFileReadAllowed = false,
                ModelFileLoadAllowed = false,
                TemplateFileReadAllowed = false,
                PackageDeploymentAllowed = false
            },
            NetworkPolicy = "redacted",
            ApprovalPolicy = approvalPolicy,
            RiskPolicy = riskPolicy,
            MetadataOnly = true,
            RealResourcesTouched = false
        };
    }
}

public sealed class RuntimePreviewOperatorContractRegistry
{
    public const string Version = "operator-contract-registry.final.metadata-only";
    private readonly RuntimePreviewGovernanceStore? _governanceStore;

    public RuntimePreviewOperatorContractRegistry()
    {
    }

    public RuntimePreviewOperatorContractRegistry(RuntimePreviewGovernanceStore governanceStore)
    {
        _governanceStore = governanceStore;
    }

    public RuntimePreviewOperatorContractRegistryDocument BuildRegistry()
    {
        var contracts = CreateContracts();
        var document = new RuntimePreviewOperatorContractRegistryDocument
        {
            OperatorContractVersion = Version,
            GeneratedAtUtc = DateTimeOffset.UtcNow,
            ContractCount = contracts.Count,
            Contracts = contracts,
            MetadataOnly = true,
            RealResourcesTouched = false
        };
        _governanceStore?.SaveOperatorContractRegistrySnapshot(document);
        _governanceStore?.SaveOperatorContractCoverageReport(BuildCoverageReport());
        return document;
    }

    public RuntimePreviewOperatorContractCoverageReport BuildCoverageReport()
    {
        var contracts = CreateContracts();
        var covered = contracts
            .Select(item => item.OperatorType)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var missing = Enum.GetNames<OperatorType>()
            .Where(item => !covered.Contains(item, StringComparer.OrdinalIgnoreCase))
            .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return new RuntimePreviewOperatorContractCoverageReport
        {
            ReportId = $"rp_operator_contract_coverage_{Guid.NewGuid():N}",
            OperatorContractVersion = Version,
            GeneratedAtUtc = DateTimeOffset.UtcNow,
            CoveredOperatorTypes = covered,
            MissingOperatorTypes = missing,
            ContractCount = covered.Count,
            CoveragePass = missing.Count == 0 &&
                           covered.Contains("ImageAcquisition", StringComparer.OrdinalIgnoreCase) &&
                           covered.Contains("TemplateMatching", StringComparer.OrdinalIgnoreCase) &&
                           covered.Contains("DeepLearning", StringComparer.OrdinalIgnoreCase) &&
                           covered.Contains("ResultOutput", StringComparer.OrdinalIgnoreCase),
            MetadataOnly = true,
            RealResourcesTouched = false
        };
    }

    public RuntimePreviewOperatorContractValidationReport Validate(
        RuntimePackageManifestDryRunReport manifestReport,
        RuntimePreviewPackageReadinessReport packageReport,
        RuntimePreviewPreReleaseReviewRequest request,
        RuntimePreviewStationProfile stationProfile,
        string caseId)
    {
        var workflowDraft = RuntimePreviewWorkflowInspector.ResolveWorkflowDraft(request.WorkflowDraft, request.Arguments);
        var operators = RuntimePreviewWorkflowInspector.ExtractOperators(workflowDraft);
        var contracts = CreateContracts().ToDictionary(item => item.OperatorType, StringComparer.OrdinalIgnoreCase);
        var results = operators.Select(op => ValidateOperator(op, contracts, stationProfile)).ToList();
        if (operators.Count == 0)
        {
            results.Add(new RuntimePreviewOperatorContractValidationItem
            {
                OperatorTempId = "workflow",
                OperatorType = "operator_graph",
                ContractSatisfied = false,
                BlockedReasons = ["operator_graph_missing"]
            });
        }

        var blockedReasons = results
            .SelectMany(item => item.BlockedReasons)
            .Concat(packageReport.BlockingIssues.Where(item => item.Contains("contract", StringComparison.OrdinalIgnoreCase)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var riskTags = results
            .SelectMany(item => item.RiskTags)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var approvals = riskTags
            .Where(item => item.Contains("approval", StringComparison.OrdinalIgnoreCase) ||
                           item.Contains("deep_learning", StringComparison.OrdinalIgnoreCase) ||
                           item.Contains("multi_station", StringComparison.OrdinalIgnoreCase))
            .Select(item => $"engineer_approval:{item}")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var report = new RuntimePreviewOperatorContractValidationReport
        {
            ReportId = $"rp_operator_contract_{Guid.NewGuid():N}",
            SessionId = packageReport.SessionId,
            CaseId = caseId,
            ManifestId = manifestReport.ManifestId,
            StationProfileId = stationProfile.StationProfileId,
            WorkflowDraftHash = packageReport.WorkflowDraftHash,
            OperatorContractVersion = Version,
            GeneratedAtUtc = DateTimeOffset.UtcNow,
            OperatorContractsSatisfied = blockedReasons.Count == 0,
            ContractResults = results,
            BlockedReasons = blockedReasons,
            RiskTags = riskTags,
            RequiredEngineerApprovals = approvals,
            MetadataOnly = true,
            PackageCreated = false,
            DeploymentExecuted = false,
            RealResourcesTouched = false
        };
        return report;
    }

    public static IReadOnlyList<RuntimePreviewOperatorContractDefinition> CreateContracts()
    {
        var contracts = new List<RuntimePreviewOperatorContractDefinition>
        {
            ContractWithOptional("ImageAcquisition", [], ["image"], ["SourceType", "CameraBindingId"], ["Exposure", "Gain"], ["cameraBinding"], ["ImagePath", "FrameBytes", "RawImageBytes"], ["metadata_runtime"], ["operatorType", "parameters.CameraBindingId"], ["camera slot available"], ["camera_metadata"]),
            ContractWithOptional("TemplateMatching", ["image"], ["match"], ["TemplateId"], ["ScoreThreshold", "Roi"], ["templateMetadata"], ["TemplatePath", "TemplateFile", "ImagePath"], ["traditional_vision_runtime"], ["operatorType", "parameters.TemplateId"], ["template metadata dependency closed"], ["template_dependency"]),
            ContractWithOptional("CircleMeasurement", ["image"], ["circle"], ["Roi"], ["CaliperCount", "Polarity"], [], ["ImagePath"], ["measurement_runtime"], ["operatorType", "parameters.Roi"], ["traditional measurement supported"], ["measurement"]),
            ContractWithOptional("MeasureDistance", ["geometry"], ["distance"], ["Unit"], ["Tolerance", "CalibrationId"], [], ["ImagePath"], ["measurement_runtime"], ["operatorType", "parameters.Unit"], ["traditional measurement supported"], ["measurement"]),
            ContractWithOptional("DeepLearning", ["image"], ["inference"], ["ModelId"], ["ModelKind", "ScoreThreshold", "LabelMapId"], ["modelMetadata"], ["ModelPath", "ModelFile", "WeightsPath", "ImagePath"], ["deep_learning_runtime"], ["operatorType", "parameters.ModelId", "parameters.ModelKind"], ["DeepLearning supported", "model kind supported"], ["deep_learning_review", "engineer_approval_required"], ["deep_learning_release_review"], ["model metadata must be catalog-bound", "real model file load forbidden"]),
            ContractWithOptional("ResultOutput", ["result"], ["metadataOutput"], ["OutputChannelId"], ["Format", "Channel"], ["outputChannel"], ["PlcAddress", "StationAddress", "PackagePath", "CvpkgPath"], ["result_output_runtime"], ["operatorType", "parameters.OutputChannelId"], ["output channel kind supported", "plc write disabled"], ["output_contract"]),
            ContractWithOptional("ResultJudgment", ["result"], ["judgment"], ["RuleId"], ["Expression", "Tolerance"], [], ["ScriptPath"], ["judgment_runtime"], ["operatorType"], ["traditional judgment supported"], ["judgment"]),
            Contract("BlobAnalysis", ["image"], ["blob"], [], [], ["ImagePath"], ["traditional_vision_runtime"], ["operatorType"], ["traditional vision supported"], ["blob"]),
            Contract("Thresholding", ["image"], ["binary"], ["Threshold"], [], ["ImagePath"], ["traditional_vision_runtime"], ["operatorType"], ["traditional vision supported"], ["threshold"]),
            Contract("EdgeDetection", ["image"], ["edges"], [], [], ["ImagePath"], ["traditional_vision_runtime"], ["operatorType"], ["traditional vision supported"], ["edge"]),
            Contract("LineMeasurement", ["image"], ["line"], ["Roi"], [], ["ImagePath"], ["measurement_runtime"], ["operatorType"], ["traditional measurement supported"], ["measurement"]),
            Contract("GapMeasurement", ["image"], ["gap"], ["Unit"], [], ["ImagePath"], ["measurement_runtime"], ["operatorType"], ["traditional measurement supported"], ["measurement"]),
            Contract("CaliperTool", ["image"], ["edgePair"], ["Roi"], [], ["ImagePath"], ["measurement_runtime"], ["operatorType"], ["caliper supported"], ["measurement"]),
            Contract("ShapeMatching", ["image"], ["pose"], ["TemplateId"], ["templateMetadata"], ["TemplatePath"], ["traditional_vision_runtime"], ["operatorType"], ["shape matching supported"], ["template_dependency"]),
            Contract("ImageCrop", ["image"], ["image"], ["Roi"], [], ["ImagePath"], ["traditional_vision_runtime"], ["operatorType"], ["traditional vision supported"], ["image_processing"]),
            Contract("ImageResize", ["image"], ["image"], ["Width", "Height"], [], ["ImagePath"], ["traditional_vision_runtime"], ["operatorType"], ["traditional vision supported"], ["image_processing"]),
            Contract("ImageNormalize", ["image"], ["image"], [], [], ["ImagePath"], ["traditional_vision_runtime"], ["operatorType"], ["traditional vision supported"], ["image_processing"]),
            Contract("CodeRecognition", ["image"], ["code"], [], [], ["ImagePath"], ["traditional_vision_runtime"], ["operatorType"], ["traditional vision supported"], ["recognition"]),
            Contract("OcrRecognition", ["image"], ["text"], [], [], ["ImagePath"], ["traditional_vision_runtime"], ["operatorType"], ["traditional vision supported"], ["recognition"]),
            ContractWithOptional("OnnxInference", ["image"], ["inference"], ["ModelId"], ["ModelKind"], ["modelMetadata"], ["ModelPath", "ImagePath"], ["deep_learning_runtime"], ["operatorType"], ["ONNX model metadata supported"], ["deep_learning_review", "engineer_approval_required"], ["deep_learning_release_review"], ["ONNX model metadata must be catalog-bound"]),
            ContractWithOptional("SemanticSegmentation", ["image"], ["mask"], ["ModelId"], ["ModelKind"], ["modelMetadata"], ["ModelPath", "ImagePath"], ["deep_learning_runtime"], ["operatorType"], ["segmentation model kind supported"], ["deep_learning_review", "engineer_approval_required"], ["deep_learning_release_review"], ["segmentation model metadata must be catalog-bound"]),
            ContractWithOptional("SurfaceDefectDetection", ["image"], ["defect"], ["ModelId"], ["ModelKind"], ["modelMetadata"], ["ModelPath", "ImagePath"], ["deep_learning_runtime"], ["operatorType"], ["defect model kind supported"], ["deep_learning_review", "engineer_approval_required"], ["deep_learning_release_review"], ["defect model metadata must be catalog-bound"]),
            ContractWithOptional("AnomalyDetection", ["image"], ["anomaly"], ["ModelId"], ["ModelKind"], ["modelMetadata"], ["ModelPath", "ImagePath"], ["deep_learning_runtime"], ["operatorType"], ["anomaly model kind supported"], ["deep_learning_review", "engineer_approval_required"], ["deep_learning_release_review"], ["anomaly model metadata must be catalog-bound"]),
            Contract("ModbusCommunication", ["result"], ["plc"], [], ["plcEndpoint"], ["Address", "PlcAddress", "BaseUrl"], ["forbidden_for_preview"], ["operatorType"], ["plc write forbidden"], ["plc_write_forbidden"]),
            Contract("ModbusRtuCommunication", ["result"], ["plc"], [], ["plcEndpoint"], ["Address", "PlcAddress", "BaseUrl"], ["forbidden_for_preview"], ["operatorType"], ["plc write forbidden"], ["plc_write_forbidden"]),
            Contract("SiemensS7Communication", ["result"], ["plc"], [], ["plcEndpoint"], ["Address", "PlcAddress", "BaseUrl"], ["forbidden_for_preview"], ["operatorType"], ["plc write forbidden"], ["plc_write_forbidden"]),
            Contract("MitsubishiMcCommunication", ["result"], ["plc"], [], ["plcEndpoint"], ["Address", "PlcAddress", "BaseUrl"], ["forbidden_for_preview"], ["operatorType"], ["plc write forbidden"], ["plc_write_forbidden"]),
            Contract("OmronFinsCommunication", ["result"], ["plc"], [], ["plcEndpoint"], ["Address", "PlcAddress", "BaseUrl"], ["forbidden_for_preview"], ["operatorType"], ["plc write forbidden"], ["plc_write_forbidden"]),
            Contract("DatabaseWrite", ["result"], ["database"], [], ["databaseEndpoint"], ["ConnectionString", "BaseUrl"], ["forbidden_for_preview"], ["operatorType"], ["external write forbidden"], ["external_write_forbidden"]),
            Contract("HttpRequest", ["result"], ["http"], [], ["networkEndpoint"], ["Url", "BaseUrl", "Authorization"], ["forbidden_for_preview"], ["operatorType"], ["network access forbidden"], ["network_write_forbidden"]),
            Contract("MqttPublish", ["result"], ["mqtt"], [], ["networkEndpoint"], ["BrokerUrl", "BaseUrl"], ["forbidden_for_preview"], ["operatorType"], ["network access forbidden"], ["network_write_forbidden"]),
            Contract("ScriptOperator", ["metadata"], ["metadata"], [], [], ["ScriptPath", "Command", "Shell"], ["forbidden_for_preview"], ["operatorType"], ["system command forbidden"], ["system_command_forbidden"])
        };

        var known = contracts
            .Select(item => item.OperatorType)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var operatorType in Enum.GetNames<OperatorType>().Where(item => !known.Contains(item)))
        {
            contracts.Add(GenericContract(operatorType));
        }

        return contracts
            .GroupBy(item => item.OperatorType, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(item => item.OperatorType, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static RuntimePreviewOperatorContractValidationItem ValidateOperator(
        RuntimePreviewWorkflowOperator op,
        IReadOnlyDictionary<string, RuntimePreviewOperatorContractDefinition> contracts,
        RuntimePreviewStationProfile stationProfile)
    {
        var blocked = new List<string>();
        if (!contracts.TryGetValue(op.OperatorType, out var contract))
        {
            contract = ContractWithOptional(op.OperatorType, [], ["metadata"], [], [], [], ["ImagePath", "ModelPath", "TemplatePath", "PlcAddress", "StationAddress", "Command"], ["metadata_runtime"], ["operatorType"], ["operator support required"], ["unknown_operator"]);
        }

        var missingParameters = contract.RequiredParameters
            .Where(parameter => string.IsNullOrWhiteSpace(RuntimePreviewWorkflowInspector.GetParameter(op, parameter)))
            .ToList();
        blocked.AddRange(missingParameters.Select(parameter => $"operator_contract_missing_parameter:{op.OperatorType}:{parameter}"));

        var forbiddenHits = new List<string>();
        foreach (var forbidden in contract.ForbiddenParameters)
        {
            if (op.Parameters.TryGetValue(forbidden, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                forbiddenHits.Add($"{op.OperatorType}:{forbidden}");
            }
        }

        if (contract.RuntimeDependencies.Contains("forbidden_for_preview", StringComparer.OrdinalIgnoreCase))
        {
            forbiddenHits.Add($"{op.OperatorType}:runtime_dependency_forbidden_for_preview");
        }

        if (string.Equals(op.OperatorType, "ResultOutput", StringComparison.OrdinalIgnoreCase))
        {
            var channel = RuntimePreviewWorkflowInspector.GetParameter(op, "Channel", "OutputChannel", "OutputChannelId");
            if (string.Equals(channel, "plc", StringComparison.OrdinalIgnoreCase) && !stationProfile.PlcWriteAllowed)
            {
                forbiddenHits.Add("ResultOutput:plc_write_forbidden");
            }
        }

        blocked.AddRange(forbiddenHits.Select(hit => $"operator_contract_forbidden_parameter:{hit}"));

        IReadOnlyList<string> stationBlocks = stationProfile.SupportedOperatorTypes.Contains(op.OperatorType, StringComparer.OrdinalIgnoreCase)
            ? []
            : [$"operator_contract_station_requirement_not_met:{op.OperatorType}"];
        blocked.AddRange(stationBlocks);

        var riskTags = contract.RiskTags
            .Concat(forbiddenHits.Select(hit => hit.Contains("plc", StringComparison.OrdinalIgnoreCase) ? "plc_write_forbidden" : "operator_contract_violation"))
            .Concat(stationBlocks.Select(_ => "station_operator_unsupported"))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new RuntimePreviewOperatorContractValidationItem
        {
            OperatorTempId = op.TempId,
            OperatorType = op.OperatorType,
            ContractSatisfied = blocked.Count == 0,
            RequiredInputs = contract.RequiredInputs,
            RequiredOutputs = contract.RequiredOutputs,
            RequiredParameters = contract.RequiredParameters,
            MissingParameters = missingParameters,
            ResourceDependencies = contract.ResourceDependencies,
            ForbiddenParameterHits = forbiddenHits,
            RuntimeDependencies = contract.RuntimeDependencies,
            ManifestFields = contract.ManifestFields,
            StationCompatibilityRequirements = contract.StationCompatibilityRequirements,
            RiskTags = riskTags,
            BlockedReasons = blocked
        };
    }

    private static RuntimePreviewOperatorContractDefinition Contract(
        string operatorType,
        IReadOnlyList<string> requiredInputs,
        IReadOnlyList<string> requiredOutputs,
        IReadOnlyList<string> requiredParameters,
        IReadOnlyList<string> resourceDependencies,
        IReadOnlyList<string> forbiddenParameters,
        IReadOnlyList<string> runtimeDependencies,
        IReadOnlyList<string> manifestFields,
        IReadOnlyList<string> stationCompatibilityRequirements,
        IReadOnlyList<string> riskTags,
        IReadOnlyList<string>? approvalRequirements = null,
        IReadOnlyList<string>? packageReviewRules = null)
    {
        return ContractWithOptional(
            operatorType,
            requiredInputs,
            requiredOutputs,
            requiredParameters,
            [],
            resourceDependencies,
            forbiddenParameters,
            runtimeDependencies,
            manifestFields,
            stationCompatibilityRequirements,
            riskTags,
            approvalRequirements,
            packageReviewRules);
    }

    private static RuntimePreviewOperatorContractDefinition ContractWithOptional(
        string operatorType,
        IReadOnlyList<string> requiredInputs,
        IReadOnlyList<string> requiredOutputs,
        IReadOnlyList<string> requiredParameters,
        IReadOnlyList<string> optionalParameters,
        IReadOnlyList<string> resourceDependencies,
        IReadOnlyList<string> forbiddenParameters,
        IReadOnlyList<string> runtimeDependencies,
        IReadOnlyList<string> manifestFields,
        IReadOnlyList<string> stationCompatibilityRequirements,
        IReadOnlyList<string> riskTags,
        IReadOnlyList<string>? approvalRequirements = null,
        IReadOnlyList<string>? packageReviewRules = null)
    {
        return new RuntimePreviewOperatorContractDefinition
        {
            OperatorType = operatorType,
            RequiredInputs = requiredInputs,
            RequiredOutputs = requiredOutputs,
            RequiredParameters = requiredParameters,
            OptionalParameters = optionalParameters,
            ResourceDependencies = resourceDependencies,
            ForbiddenParameters = forbiddenParameters,
            RuntimeDependencies = runtimeDependencies,
            ManifestFields = manifestFields,
            StationCompatibilityRequirements = stationCompatibilityRequirements,
            RiskTags = riskTags,
            ApprovalRequirements = approvalRequirements ?? [],
            PackageReviewRules = packageReviewRules ?? ["metadata-only validation", "no real resource execution"],
            MetadataOnly = true
        };
    }

    private static RuntimePreviewOperatorContractDefinition GenericContract(string operatorType)
    {
        var category = operatorType.Contains("Communication", StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(operatorType, "TcpCommunication", StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(operatorType, "SerialCommunication", StringComparison.OrdinalIgnoreCase)
            ? "external_io_forbidden"
            : operatorType.Contains("Save", StringComparison.OrdinalIgnoreCase) ||
              operatorType.Contains("Write", StringComparison.OrdinalIgnoreCase)
                ? "external_write_forbidden"
                : operatorType.Contains("Deep", StringComparison.OrdinalIgnoreCase) ||
                  operatorType.Contains("Inference", StringComparison.OrdinalIgnoreCase) ||
                  operatorType.Contains("Detection", StringComparison.OrdinalIgnoreCase) && operatorType.Contains("Surface", StringComparison.OrdinalIgnoreCase)
                    ? "deep_learning_review"
                    : "metadata_operator_contract";
        string[] forbidden = category.Contains("forbidden", StringComparison.OrdinalIgnoreCase)
            ? ["Address", "BaseUrl", "ConnectionString", "Path", "Command", "PackagePath"]
            : ["ImagePath", "ModelPath", "TemplatePath", "PackagePath"];
        string[] runtime = category.Contains("forbidden", StringComparison.OrdinalIgnoreCase)
            ? ["forbidden_for_preview"]
            : ["metadata_runtime"];
        string[] approvals = category.Contains("deep_learning", StringComparison.OrdinalIgnoreCase)
            ? ["engineer_approval:deep_learning_release_review"]
            : [];
        return ContractWithOptional(
            operatorType,
            ["metadata"],
            ["metadata"],
            [],
            [],
            [],
            forbidden,
            runtime,
            ["operatorType"],
            ["operator metadata supported"],
            [category],
            approvals,
            ["metadata-only contract coverage", "no operator execution"]);
    }
}

public sealed class RuntimePreviewStationCompatibilityDryRunService
{
    private readonly RuntimePreviewReportArchive _reportArchive;
    private readonly RuntimePreviewAuditTrail _auditTrail;

    public RuntimePreviewStationCompatibilityDryRunService(
        RuntimePreviewReportArchive reportArchive,
        RuntimePreviewAuditTrail auditTrail)
    {
        _reportArchive = reportArchive;
        _auditTrail = auditTrail;
    }

    public RuntimePreviewStationCompatibilityReport Evaluate(
        RuntimePackageManifestDryRunReport manifestReport,
        RuntimePreviewPackageReadinessReport packageReport,
        RuntimePreviewPreReleaseReviewRequest request,
        RuntimePreviewStationProfile stationProfile,
        string caseId)
    {
        var workflowDraft = RuntimePreviewWorkflowInspector.ResolveWorkflowDraft(request.WorkflowDraft, request.Arguments);
        var operators = RuntimePreviewWorkflowInspector.ExtractOperators(workflowDraft);
        var blockedReasons = new List<string>();
        var engineerActions = new List<string>();
        var requiredRuntimeVersion = RuntimePreviewWorkflowInspector.RequiredRuntimeVersion(manifestReport);
        var runtimeVersionCompatible = CompareVersion(stationProfile.RuntimeVersion, requiredRuntimeVersion) >= 0;
        if (!runtimeVersionCompatible)
        {
            blockedReasons.Add($"station_runtime_version_too_low:required_{requiredRuntimeVersion}");
            engineerActions.Add("Select a Station profile with the required Runtime version or downgrade unsupported operators.");
        }

        var unsupportedOperators = manifestReport.OperatorTypes
            .Where(type => !stationProfile.SupportedOperatorTypes.Contains(type, StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var operatorSupportCompatible = unsupportedOperators.Count == 0;
        if (!operatorSupportCompatible)
        {
            blockedReasons.AddRange(unsupportedOperators.Select(type => $"station_operator_not_supported:{type}"));
            engineerActions.Add("Replace unsupported operators or select a Station profile that supports them.");
        }

        var cameraSlotsCompatible = manifestReport.CameraBindings.Count <= stationProfile.CameraBindingSlots.Count;
        if (!cameraSlotsCompatible)
        {
            blockedReasons.Add($"station_camera_slots_insufficient:required_{manifestReport.CameraBindings.Count}_available_{stationProfile.CameraBindingSlots.Count}");
            engineerActions.Add("Reduce camera bindings or choose a Station profile with more camera slots.");
        }

        var outputChannelsCompatible = manifestReport.OutputChannels.Count == 0 ||
                                       manifestReport.OutputChannels.All(channel => stationProfile.OutputChannelKinds.Contains(channel, StringComparer.OrdinalIgnoreCase));
        if (!outputChannelsCompatible)
        {
            blockedReasons.Add("station_output_channel_kind_missing");
            engineerActions.Add("Map ResultOutput to an output channel kind supported by the target Station profile.");
        }

        var modelKinds = RuntimePreviewWorkflowInspector.InferModelKinds(operators, manifestReport);
        var modelKindsCompatible = modelKinds.Count == 0 ||
                                   modelKinds.All(kind => stationProfile.SupportedModelKinds.Contains(kind, StringComparer.OrdinalIgnoreCase));
        var templateDependenciesCompatible = manifestReport.TemplateDependencies.Count == 0 ||
                                             !manifestReport.MissingDependencies.Any(item => item.Contains("template", StringComparison.OrdinalIgnoreCase));
        var modelTemplateDependenciesCompatible = modelKindsCompatible && templateDependenciesCompatible;
        if (!modelKindsCompatible)
        {
            blockedReasons.Add("station_model_kind_not_supported");
            engineerActions.Add("Use a model kind supported by the Station profile or move the flow to a compatible Station.");
        }

        if (!templateDependenciesCompatible)
        {
            blockedReasons.Add("station_template_dependency_not_closed");
            engineerActions.Add("Bind TemplateId metadata before release review.");
        }

        var operatorCountCompatible = manifestReport.OperatorCount <= stationProfile.MaxOperatorCount;
        if (!operatorCountCompatible)
        {
            blockedReasons.Add($"station_operator_count_exceeded:max_{stationProfile.MaxOperatorCount}");
            engineerActions.Add("Split the flow or target a higher-capacity IPC profile.");
        }

        var plcStationIntentCompatible = stationProfile.PlcWriteAllowed ||
                                         !RuntimePreviewWorkflowInspector.ContainsDirectPlcOrStationIntent(operators, manifestReport);
        if (!plcStationIntentCompatible)
        {
            blockedReasons.Add("station_plc_or_direct_station_intent_forbidden");
            engineerActions.Add("Remove PLC/Station direct intent from the draft; this simulator never writes PLC or deploys.");
        }

        var manifestRiskCompatible = !string.Equals(manifestReport.RiskLevel, "denied", StringComparison.OrdinalIgnoreCase) &&
                                     !manifestReport.BlockedReasons.Any(item => item.Contains("dangerous", StringComparison.OrdinalIgnoreCase) ||
                                                                               item.Contains("denied", StringComparison.OrdinalIgnoreCase));
        if (!manifestRiskCompatible)
        {
            blockedReasons.Add("station_manifest_risk_denied");
            engineerActions.Add("Clear denied manifest risk before Station compatibility review.");
        }

        var stationCompatible = runtimeVersionCompatible &&
                                operatorSupportCompatible &&
                                cameraSlotsCompatible &&
                                outputChannelsCompatible &&
                                modelTemplateDependenciesCompatible &&
                                operatorCountCompatible &&
                                plcStationIntentCompatible &&
                                manifestRiskCompatible;
        var report = new RuntimePreviewStationCompatibilityReport
        {
            ReportId = $"rp_station_compat_{Guid.NewGuid():N}",
            SessionId = packageReport.SessionId,
            CaseId = caseId,
            ManifestId = manifestReport.ManifestId,
            StationProfileId = stationProfile.StationProfileId,
            WorkflowDraftHash = packageReport.WorkflowDraftHash,
            GeneratedAtUtc = DateTimeOffset.UtcNow,
            StationProfile = stationProfile,
            StationCompatible = stationCompatible,
            RuntimeVersionCompatible = runtimeVersionCompatible,
            OperatorSupportCompatible = operatorSupportCompatible,
            CameraSlotsCompatible = cameraSlotsCompatible,
            OutputChannelsCompatible = outputChannelsCompatible,
            ModelTemplateDependenciesCompatible = modelTemplateDependenciesCompatible,
            OperatorCountCompatible = operatorCountCompatible,
            PlcStationIntentCompatible = plcStationIntentCompatible,
            ManifestRiskCompatible = manifestRiskCompatible,
            RequiredRuntimeVersion = requiredRuntimeVersion,
            BlockedReasons = blockedReasons.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            RiskLevel = ResolveRisk(stationCompatible, manifestReport, blockedReasons),
            EngineerActions = engineerActions.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            MetadataOnly = true,
            PackageCreated = false,
            DeploymentExecuted = false,
            RealResourcesTouched = false
        };
        _reportArchive.SaveStationCompatibilityReport(report);
        _auditTrail.Append(packageReport.SessionId, RuntimePreviewAuditEventTypes.StationCompatibilityGenerated, new
        {
            report.ReportId,
            report.ManifestId,
            report.StationProfileId,
            report.StationCompatible,
            report.RiskLevel,
            report.MetadataOnly,
            report.RealResourcesTouched
        });
        return report;
    }

    private static string ResolveRisk(
        bool stationCompatible,
        RuntimePackageManifestDryRunReport manifestReport,
        IReadOnlyList<string> blockedReasons)
    {
        if (!stationCompatible && blockedReasons.Any(item => item.Contains("forbidden", StringComparison.OrdinalIgnoreCase) ||
                                                             item.Contains("denied", StringComparison.OrdinalIgnoreCase)))
        {
            return "denied";
        }

        if (!stationCompatible)
        {
            return "high";
        }

        return string.Equals(manifestReport.RiskLevel, "medium", StringComparison.OrdinalIgnoreCase) ? "medium" : "low";
    }

    private static int CompareVersion(string actual, string required)
    {
        return ParseVersion(actual).CompareTo(ParseVersion(required));
    }

    private static Version ParseVersion(string value)
    {
        return Version.TryParse(value, out var version) ? version : new Version(0, 0, 0);
    }
}

public sealed class RuntimePreviewPreReleaseReviewService
{
    private readonly RuntimePreviewPackageReadinessBridge _packageReadinessBridge;
    private readonly RuntimePreviewReportArchive _reportArchive;
    private readonly RuntimePreviewAuditTrail _auditTrail;
    private readonly RuntimePreviewStationProfileCatalog _stationProfileCatalog;
    private readonly RuntimePreviewStationCompatibilityDryRunService _stationCompatibilityService;
    private readonly RuntimePreviewOperatorContractRegistry _operatorContractRegistry;

    public RuntimePreviewPreReleaseReviewService(
        RuntimePreviewPackageReadinessBridge packageReadinessBridge,
        RuntimePreviewReportArchive reportArchive,
        RuntimePreviewAuditTrail auditTrail,
        RuntimePreviewStationProfileCatalog stationProfileCatalog,
        RuntimePreviewStationCompatibilityDryRunService stationCompatibilityService,
        RuntimePreviewOperatorContractRegistry operatorContractRegistry)
    {
        _packageReadinessBridge = packageReadinessBridge;
        _reportArchive = reportArchive;
        _auditTrail = auditTrail;
        _stationProfileCatalog = stationProfileCatalog;
        _stationCompatibilityService = stationCompatibilityService;
        _operatorContractRegistry = operatorContractRegistry;
    }

    public async Task<RuntimePreviewPreReleaseReviewReport> GenerateAsync(
        RuntimePreviewPreReleaseReviewRequest request,
        AppConfig appConfig,
        AiConfigStore? aiConfigStore,
        bool isAdmin,
        bool developerUiRequested,
        CancellationToken cancellationToken = default)
    {
        var packageReport = await _packageReadinessBridge.GenerateAsync(
            new RuntimePreviewPackageReadinessRequest
            {
                Config = request.Config,
                ToolName = request.ToolName,
                Arguments = request.Arguments,
                WorkflowDraft = request.WorkflowDraft,
                RuntimePreviewConsent = request.RuntimePreviewConsent,
                RequireReplay = request.RequireReplay
            },
            appConfig,
            aiConfigStore,
            isAdmin,
            developerUiRequested,
            cancellationToken);
        var manifestReport = string.IsNullOrWhiteSpace(packageReport.ManifestDryRunReportId)
            ? null
            : _reportArchive.GetManifestDryRunReport(packageReport.ManifestDryRunReportId);
        manifestReport ??= new RuntimePackageManifestDryRunReport
        {
            ManifestId = $"rp_manifest_missing_{Guid.NewGuid():N}",
            ReportId = $"rp_manifest_report_missing_{Guid.NewGuid():N}",
            SessionId = packageReport.SessionId,
            PackageReadinessReportId = packageReport.ReportId,
            WorkflowDraftHash = packageReport.WorkflowDraftHash,
            RiskLevel = "high",
            BlockedReasons = ["manifest_dry_run_report_missing"],
            MetadataOnly = true,
            RealResourcesTouched = false
        };

        var caseId = string.IsNullOrWhiteSpace(request.CaseId) ? "ad_hoc_pre_release_review" : RuntimePreviewGovernanceRedactor.RedactScalar(request.CaseId);
        var stationProfile = _stationProfileCatalog.GetOrDefault(request.StationProfileId);
        var stationReport = _stationCompatibilityService.Evaluate(manifestReport, packageReport, request, stationProfile, caseId);
        var contractReport = _operatorContractRegistry.Validate(manifestReport, packageReport, request, stationProfile, caseId);
        _reportArchive.SaveOperatorContractValidationReport(contractReport);
        _auditTrail.Append(packageReport.SessionId, RuntimePreviewAuditEventTypes.OperatorContractValidationGenerated, new
        {
            contractReport.ReportId,
            contractReport.ManifestId,
            contractReport.OperatorContractsSatisfied,
            contractReport.MetadataOnly,
            contractReport.RealResourcesTouched
        });

        var blockedReasons = packageReport.BlockingIssues
            .Concat(manifestReport.BlockedReasons)
            .Concat(stationReport.BlockedReasons)
            .Concat(contractReport.BlockedReasons)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(RuntimePreviewGovernanceRedactor.RedactScalar)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var approvals = contractReport.RequiredEngineerApprovals
            .Concat(ResolveReviewApprovals(manifestReport, stationReport, contractReport, caseId))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var requiresEngineerApproval = approvals.Count > 0 && blockedReasons.Count == 0;
        var releaseReviewAllowed = packageReport.PackageReviewAllowed &&
                                   stationReport.StationCompatible &&
                                   contractReport.OperatorContractsSatisfied &&
                                   !requiresEngineerApproval &&
                                   blockedReasons.Count == 0;
        var engineerActions = ResolveEngineerActions(blockedReasons, approvals, stationReport, packageReport, contractReport);
        var reviewId = $"rp_review_{Guid.NewGuid():N}";
        var goNoGoDecision = ResolveGoNoGoDecision(
            releaseReviewAllowed,
            requiresEngineerApproval,
            blockedReasons,
            packageReport,
            manifestReport,
            stationReport,
            contractReport);
        var firstFixRecommendation = ResolveFirstFixRecommendation(blockedReasons, approvals, engineerActions);
        var decisionMatrix = BuildDecisionMatrix(
            reviewId,
            caseId,
            manifestReport.ManifestId,
            stationProfile.StationProfileId,
            goNoGoDecision,
            firstFixRecommendation,
            packageReport,
            manifestReport,
            stationReport,
            contractReport,
            releaseReviewAllowed,
            requiresEngineerApproval,
            blockedReasons,
            approvals);
        var report = new RuntimePreviewPreReleaseReviewReport
        {
            ReviewId = reviewId,
            CaseId = caseId,
            SessionId = packageReport.SessionId,
            WorkflowDraftHash = packageReport.WorkflowDraftHash,
            ManifestId = manifestReport.ManifestId,
            StationProfileId = stationProfile.StationProfileId,
            OperatorContractVersion = RuntimePreviewOperatorContractRegistry.Version,
            ReadinessStatus = ResolveReadinessStatus(packageReport),
            PackageReviewAllowed = packageReport.PackageReviewAllowed,
            StationCompatible = stationReport.StationCompatible,
            OperatorContractsSatisfied = contractReport.OperatorContractsSatisfied,
            ReleaseReviewAllowed = releaseReviewAllowed,
            RequiresEngineerApproval = requiresEngineerApproval,
            GoNoGoDecision = goNoGoDecision,
            BlockedReasons = blockedReasons,
            RiskLevel = ResolveReleaseRisk(blockedReasons, approvals, packageReport, manifestReport, stationReport, contractReport),
            EngineerActions = engineerActions,
            FirstFixRecommendation = firstFixRecommendation,
            WorkflowDraftAllowed = packageReport.WorkflowDraftAllowed,
            DecisionMatrix = decisionMatrix,
            PackageReadinessReportId = packageReport.ReportId,
            StationCompatibilityReportId = stationReport.ReportId,
            OperatorContractValidationReportId = contractReport.ReportId,
            GeneratedAtUtc = DateTimeOffset.UtcNow,
            MetadataOnly = true,
            PackageCreated = false,
            DeploymentExecuted = false,
            RealResourcesTouched = false
        };
        _reportArchive.SavePreReleaseReviewReport(report);
        _auditTrail.Append(packageReport.SessionId, RuntimePreviewAuditEventTypes.PreReleaseReviewGenerated, new
        {
            report.ReviewId,
            report.CaseId,
            report.ManifestId,
            report.StationProfileId,
            report.ReleaseReviewAllowed,
            report.RequiresEngineerApproval,
            report.MetadataOnly,
            report.RealResourcesTouched
        });
        return report;
    }

    private static string ResolveGoNoGoDecision(
        bool releaseReviewAllowed,
        bool requiresEngineerApproval,
        IReadOnlyList<string> blockedReasons,
        RuntimePreviewPackageReadinessReport packageReport,
        RuntimePackageManifestDryRunReport manifestReport,
        RuntimePreviewStationCompatibilityReport stationReport,
        RuntimePreviewOperatorContractValidationReport contractReport)
    {
        if (releaseReviewAllowed)
        {
            return "releaseAllowed";
        }

        if (requiresEngineerApproval)
        {
            return "requiresEngineerApproval";
        }

        if (blockedReasons.Any(item => item.Contains("forbidden", StringComparison.OrdinalIgnoreCase) ||
                                       item.Contains("denied", StringComparison.OrdinalIgnoreCase) ||
                                       item.Contains("plc", StringComparison.OrdinalIgnoreCase) ||
                                       item.Contains("station_intent", StringComparison.OrdinalIgnoreCase)))
        {
            return "forbiddenIntentDenied";
        }

        if (!packageReport.PackageReviewAllowed)
        {
            return packageReport.BlockingIssues.Any(item => item.Contains("metadata", StringComparison.OrdinalIgnoreCase) ||
                                                            item.Contains("missing", StringComparison.OrdinalIgnoreCase))
                ? "metadataIncomplete"
                : "packageReviewBlocked";
        }

        if (!stationReport.StationCompatible)
        {
            return "stationIncompatible";
        }

        if (!contractReport.OperatorContractsSatisfied)
        {
            return "operatorContractFailed";
        }

        if (string.Equals(manifestReport.RiskLevel, "high", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(manifestReport.RiskLevel, "denied", StringComparison.OrdinalIgnoreCase))
        {
            return "manifestRiskBlocked";
        }

        return "blocked";
    }

    private static string ResolveFirstFixRecommendation(
        IReadOnlyList<string> blockedReasons,
        IReadOnlyList<string> approvals,
        IReadOnlyList<string> engineerActions)
    {
        if (blockedReasons.Count > 0)
        {
            var first = blockedReasons[0];
            if (first.Contains("operator_contract", StringComparison.OrdinalIgnoreCase))
            {
                return $"Fix the first failed operator contract: {first}.";
            }

            if (first.Contains("station_", StringComparison.OrdinalIgnoreCase))
            {
                return $"Resolve target Station compatibility first: {first}.";
            }

            if (first.Contains("manifest", StringComparison.OrdinalIgnoreCase) ||
                first.Contains("dependency", StringComparison.OrdinalIgnoreCase) ||
                first.Contains("missing", StringComparison.OrdinalIgnoreCase))
            {
                return $"Close the first metadata dependency before release review: {first}.";
            }

            return $"Resolve the first blocking reason, then rerun full review: {first}.";
        }

        if (approvals.Count > 0)
        {
            return $"Request engineer approval before go decision: {approvals[0]}.";
        }

        return engineerActions.FirstOrDefault() ??
               "Keep the simulator metadata-only and do not create, deploy, or hot-load a real package.";
    }

    private static RuntimePreviewReleaseReadinessDecisionMatrix BuildDecisionMatrix(
        string reviewId,
        string caseId,
        string manifestId,
        string stationProfileId,
        string goNoGoDecision,
        string firstFixRecommendation,
        RuntimePreviewPackageReadinessReport packageReport,
        RuntimePackageManifestDryRunReport manifestReport,
        RuntimePreviewStationCompatibilityReport stationReport,
        RuntimePreviewOperatorContractValidationReport contractReport,
        bool releaseReviewAllowed,
        bool requiresEngineerApproval,
        IReadOnlyList<string> blockedReasons,
        IReadOnlyList<string> approvals)
    {
        var blockedReason = blockedReasons.Count == 0
            ? "No blocking reason is active for this decision category."
            : string.Join("; ", blockedReasons.Take(4));
        var approvalReason = approvals.Count == 0
            ? "No engineer approval is currently required."
            : string.Join("; ", approvals.Take(4));
        var workflowDraftAllowed = packageReport.WorkflowDraftAllowed;
        return new RuntimePreviewReleaseReadinessDecisionMatrix
        {
            ReportId = $"rp_release_decision_{Guid.NewGuid():N}",
            ReviewId = reviewId,
            CaseId = caseId,
            ManifestId = manifestId,
            StationProfileId = stationProfileId,
            GeneratedAtUtc = DateTimeOffset.UtcNow,
            GoNoGoDecision = goNoGoDecision,
            ReleaseAllowed = Decision(
                "releaseAllowed",
                releaseReviewAllowed
                    ? "Readiness, package review, manifest dry-run, Station compatibility, and operator contracts are clean."
                    : "Release is not allowed until all review gates are clean and approvals are resolved.",
                releaseReviewAllowed
                    ? "Keep real package creation, deployment, Station, PLC, and hot-load gates disabled for pre-pilot review."
                    : firstFixRecommendation,
                false,
                workflowDraftAllowed,
                packageReport.PackageReviewAllowed,
                releaseReviewAllowed),
            RequiresEngineerApproval = Decision(
                "requiresEngineerApproval",
                requiresEngineerApproval ? approvalReason : "Approval is not the active decision for this case.",
                requiresEngineerApproval ? firstFixRecommendation : "No approval action is required unless policy changes.",
                requiresEngineerApproval,
                workflowDraftAllowed,
                packageReport.PackageReviewAllowed,
                false),
            Blocked = Decision(
                "blocked",
                blockedReasons.Count > 0 ? blockedReason : "No blocking reason is active.",
                blockedReasons.Count > 0 ? firstFixRecommendation : "No blocking fix is required.",
                false,
                workflowDraftAllowed,
                packageReport.PackageReviewAllowed,
                false),
            ForbiddenIntentDenied = Decision(
                "forbiddenIntentDenied",
                blockedReasons.Any(item => item.Contains("forbidden", StringComparison.OrdinalIgnoreCase) ||
                                           item.Contains("denied", StringComparison.OrdinalIgnoreCase) ||
                                           item.Contains("plc", StringComparison.OrdinalIgnoreCase))
                    ? blockedReason
                    : "No forbidden PLC, Station, deploy, package, hot-load, or command intent is active.",
                "Remove forbidden intent and keep the workflow in metadata-only review.",
                false,
                workflowDraftAllowed,
                false,
                false),
            MetadataIncomplete = Decision(
                "metadataIncomplete",
                packageReport.BlockingIssues.Count > 0 || manifestReport.MissingDependencies.Count > 0
                    ? blockedReason
                    : "No incomplete metadata dependency is active.",
                "Bind missing camera, template, model, output, or manifest metadata handles from the redacted catalog.",
                false,
                workflowDraftAllowed,
                false,
                false),
            StationIncompatible = Decision(
                "stationIncompatible",
                stationReport.StationCompatible ? "Target Station is compatible in dry-run." : blockedReason,
                stationReport.StationCompatible
                    ? "No Station compatibility fix is required."
                    : string.Join("; ", stationReport.EngineerActions.DefaultIfEmpty(firstFixRecommendation).Take(3)),
                false,
                workflowDraftAllowed,
                packageReport.PackageReviewAllowed,
                false),
            OperatorContractFailed = Decision(
                "operatorContractFailed",
                contractReport.OperatorContractsSatisfied ? "Operator contracts are satisfied." : blockedReason,
                contractReport.OperatorContractsSatisfied
                    ? "No operator contract fix is required."
                    : "Fix the first failed required parameter, forbidden parameter, or resource dependency.",
                false,
                workflowDraftAllowed,
                packageReport.PackageReviewAllowed,
                false),
            ManifestRiskBlocked = Decision(
                "manifestRiskBlocked",
                string.Equals(manifestReport.RiskLevel, "high", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(manifestReport.RiskLevel, "denied", StringComparison.OrdinalIgnoreCase)
                    ? blockedReason
                    : "Manifest risk is not blocking this case.",
                "Resolve denied/high manifest risk in metadata dry-run before release review.",
                false,
                workflowDraftAllowed,
                packageReport.PackageReviewAllowed,
                false),
            PackageReviewBlocked = Decision(
                "packageReviewBlocked",
                packageReport.PackageReviewAllowed ? "Package review is allowed." : blockedReason,
                packageReport.PackageReviewAllowed ? "No package review fix is required." : firstFixRecommendation,
                false,
                workflowDraftAllowed,
                packageReport.PackageReviewAllowed,
                false),
            MetadataOnly = true,
            PackageCreated = false,
            DeploymentExecuted = false,
            RealResourcesTouched = false
        };
    }

    private static RuntimePreviewReleaseReadinessDecision Decision(
        string decisionType,
        string reason,
        string nextAction,
        bool engineerApprovalRequired,
        bool workflowDraftAllowed,
        bool packageReviewAllowed,
        bool releaseReviewAllowed)
    {
        return new RuntimePreviewReleaseReadinessDecision
        {
            DecisionType = decisionType,
            Reason = RuntimePreviewGovernanceRedactor.RedactScalar(reason),
            NextAction = RuntimePreviewGovernanceRedactor.RedactScalar(nextAction),
            EngineerApprovalRequired = engineerApprovalRequired,
            WorkflowDraftAllowed = workflowDraftAllowed,
            PackageReviewAllowed = packageReviewAllowed,
            ReleaseReviewAllowed = releaseReviewAllowed
        };
    }

    private static IReadOnlyList<string> ResolveReviewApprovals(
        RuntimePackageManifestDryRunReport manifestReport,
        RuntimePreviewStationCompatibilityReport stationReport,
        RuntimePreviewOperatorContractValidationReport contractReport,
        string caseId)
    {
        var approvals = new List<string>();
        if (manifestReport.OperatorTypes.Any(item => string.Equals(item, "DeepLearning", StringComparison.OrdinalIgnoreCase)) &&
            contractReport.OperatorContractsSatisfied &&
            stationReport.StationCompatible)
        {
            approvals.Add("engineer_approval:deep_learning_release_review");
        }

        if (string.Equals(manifestReport.RiskLevel, "medium", StringComparison.OrdinalIgnoreCase) &&
            contractReport.OperatorContractsSatisfied &&
            stationReport.StationCompatible)
        {
            approvals.Add("engineer_approval:medium_manifest_risk");
        }

        if ((caseId.Contains("MULTI", StringComparison.OrdinalIgnoreCase) ||
             stationReport.StationProfileId.Contains("multi", StringComparison.OrdinalIgnoreCase) ||
             stationReport.StationProfile.StationType.Contains("multi", StringComparison.OrdinalIgnoreCase)) &&
            stationReport.StationCompatible &&
            contractReport.OperatorContractsSatisfied)
        {
            approvals.Add("engineer_approval:multi_station_review");
        }

        return approvals;
    }

    private static IReadOnlyList<string> ResolveEngineerActions(
        IReadOnlyList<string> blockedReasons,
        IReadOnlyList<string> approvals,
        RuntimePreviewStationCompatibilityReport stationReport,
        RuntimePreviewPackageReadinessReport packageReport,
        RuntimePreviewOperatorContractValidationReport contractReport)
    {
        if (blockedReasons.Count > 0)
        {
            return stationReport.EngineerActions
                .Concat(packageReport.PendingActions.Select(item => item.Title))
                .Concat(contractReport.BlockedReasons.Select(item => $"Fix operator contract: {item}"))
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Select(RuntimePreviewGovernanceRedactor.RedactScalar)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .DefaultIfEmpty("Resolve blocked metadata dependencies, then rerun full pre-release review.")
                .ToList();
        }

        if (approvals.Count > 0)
        {
            return approvals.Select(item => $"Request {item} before release review can be allowed.").ToList();
        }

        return ["Release review simulator is allowed; keep real package, deployment, Station, PLC, and hot-load gates disabled."];
    }

    private static string ResolveReadinessStatus(RuntimePreviewPackageReadinessReport packageReport)
    {
        if (packageReport.PackageReviewAllowed)
        {
            return RuntimePreviewPilotReadinessStatuses.Ready;
        }

        return packageReport.BlockingIssues.Any(item => item.Contains("denied", StringComparison.OrdinalIgnoreCase))
            ? RuntimePreviewPilotReadinessStatuses.Denied
            : RuntimePreviewPilotReadinessStatuses.NotReady;
    }

    private static string ResolveReleaseRisk(
        IReadOnlyList<string> blockedReasons,
        IReadOnlyList<string> approvals,
        RuntimePreviewPackageReadinessReport packageReport,
        RuntimePackageManifestDryRunReport manifestReport,
        RuntimePreviewStationCompatibilityReport stationReport,
        RuntimePreviewOperatorContractValidationReport contractReport)
    {
        if (blockedReasons.Any(item => item.Contains("denied", StringComparison.OrdinalIgnoreCase) ||
                                       item.Contains("forbidden", StringComparison.OrdinalIgnoreCase)))
        {
            return "denied";
        }

        if (blockedReasons.Count > 0 ||
            string.Equals(packageReport.PackageRiskLevel, "high", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(manifestReport.RiskLevel, "high", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(stationReport.RiskLevel, "high", StringComparison.OrdinalIgnoreCase) ||
            !contractReport.OperatorContractsSatisfied)
        {
            return "high";
        }

        return approvals.Count > 0 ? "medium" : "low";
    }
}

public sealed class RuntimePreviewScenarioCorpusService
{
    public RuntimePreviewScenarioCorpusDocument BuildCorpus()
    {
        var cases = CreateCorpusCases();
        return new RuntimePreviewScenarioCorpusDocument
        {
            GeneratedAtUtc = DateTimeOffset.UtcNow,
            CaseCount = cases.Count,
            Cases = cases,
            MetadataOnly = true,
            RealResourcesTouched = false
        };
    }

    public static IReadOnlyList<RuntimePreviewScenarioCorpusCase> CreateCorpusCases()
    {
        return
        [
            Corpus("RP-SC-001", "wire_sequence", RuntimePreviewScenarioEvidenceStatuses.Passed, "low", [], "Line sequence check is package-ready after metadata camera and template handles are allowlisted.", Flow("line-cam", templateId: "wire-template")),
            Corpus("RP-SC-002", "terminal_color_order", RuntimePreviewScenarioEvidenceStatuses.Passed, "low", [], "Terminal color order inspection uses the same metadata camera with a different judgment rule.", Flow("line-cam", templateId: "terminal-color-template")),
            Corpus("RP-SC-003", "template_matching", RuntimePreviewScenarioEvidenceStatuses.Passed, "low", [], "Template matching positioning is ready when TemplateId is catalog-backed.", Flow("line-cam", templateId: "fixture-template")),
            Corpus("RP-SC-004", "hole_distance", RuntimePreviewScenarioEvidenceStatuses.Passed, "low", [], "Hole distance measurement can run metadata preview and package precheck without real image input.", HoleDistanceFlow()),
            Corpus("RP-SC-005", "remote_control_detection", RuntimePreviewScenarioEvidenceStatuses.Passed, "low", [], "Remote controller inspection uses ModelId metadata and does not load a model file.", ModelFlow("line-cam", "remote-control-model")),
            Corpus("RP-SC-006", "missing_camera", RuntimePreviewScenarioEvidenceStatuses.NotReady, "missing_camera_binding", ["RuntimePreviewPilotReadinessReview"], "Camera binding is absent, so preview/package are blocked while the draft remains editable.", Flow("missing-cam", templateId: "wire-template")),
            Corpus("RP-SC-007", "missing_template", RuntimePreviewScenarioEvidenceStatuses.NotReady, "missing_template", ["RuntimePreviewPilotReadinessReview"], "Template source is unresolved; engineer must bind TemplateId before package readiness.", Flow("line-cam")),
            Corpus("RP-SC-008", "missing_model", RuntimePreviewScenarioEvidenceStatuses.NotReady, "missing_model", ["RuntimePreviewPilotReadinessReview"], "Model metadata is unresolved; no model file is loaded and package stays blocked.", ModelFlow("line-cam", "<pending-model>")),
            Corpus("RP-SC-009", "dangerous_path", RuntimePreviewScenarioEvidenceStatuses.Denied, "dangerous_resource", ["RuntimePreviewPilotReadinessReview"], "External path-like metadata is denied and redacted before any artifact is produced.", Flow("line-cam", templatePath: "external:/blocked-template")),
            Corpus("RP-SC-010", "plc_station_deny", RuntimePreviewScenarioEvidenceStatuses.Denied, "plc_station_denied", ["RuntimePreviewPilotReadinessReview"], "PLC or Station intent is denied; no PLC write and no Station access are attempted.", PlcFlow()),
            Corpus("RP-SC-011", "precheck_blocked", RuntimePreviewScenarioEvidenceStatuses.NotReady, "precheck_not_ready", ["DeploymentPrecheckResourceReview"], "Runtime package precheck blocks packaging because replay/readiness metadata is incomplete.", ModelFlow("line-cam", "<pending-model>")),
            Corpus("RP-SC-012", "allowlist_mismatch", RuntimePreviewScenarioEvidenceStatuses.NotReady, "allowlist_mismatch", ["RuntimePreviewPilotReadinessReview"], "Workflow references a metadata handle outside the pilot allowlist.", Flow("camera-not-allowlisted", templateId: "wire-template")),
            Corpus("RP-SC-013", "multi_operator_flow", RuntimePreviewScenarioEvidenceStatuses.Passed, "medium", [], "Multi-operator measurement flow is previewable as metadata and requires only review before real pilot.", MultiOperatorFlow()),
            Corpus("RP-SC-014", "missing_parameter", RuntimePreviewScenarioEvidenceStatuses.NotReady, "missing_parameter", ["RuntimePreviewPilotReadinessReview"], "A required operator parameter is missing; workflow remains editable but package is blocked.", MissingParameterFlow()),
            Corpus("RP-SC-015", "draft_editable_package_blocked", RuntimePreviewScenarioEvidenceStatuses.NotReady, "draft_allowed_package_blocked", ["RuntimePreviewPilotReadinessReview"], "The workflow draft can still be edited even though package readiness is blocked by missing resources.", Flow("missing-cam", templateId: "fixture-template"))
        ];
    }

    private static RuntimePreviewScenarioCorpusCase Corpus(
        string caseId,
        string scenario,
        string expectedStatus,
        string expectedRisk,
        IReadOnlyList<string> pendingActions,
        string businessExplanation,
        object workflowDraft)
    {
        var workflow = RuntimePreviewGovernanceRedactor.ToRedactedElement(workflowDraft);
        return new RuntimePreviewScenarioCorpusCase
        {
            CaseId = caseId,
            Scenario = scenario,
            WorkflowDraftHash = RuntimePreviewGovernanceHashes.HashJsonElement(workflow),
            ExpectedStatus = expectedStatus,
            ExpectedRisk = expectedRisk,
            ExpectedPendingActions = pendingActions,
            BusinessExplanation = businessExplanation,
            WorkflowDraft = workflow,
            MetadataOnly = true,
            RealResourcesTouched = false
        };
    }

    internal static object Flow(string cameraBindingId, string? templateId = null, string? templatePath = null)
    {
        var parameters = new Dictionary<string, string>();
        if (!string.IsNullOrWhiteSpace(templateId))
        {
            parameters["TemplateId"] = templateId;
        }

        if (!string.IsNullOrWhiteSpace(templatePath))
        {
            parameters["TemplatePath"] = templatePath;
        }

        return Draft(
            Camera(cameraBindingId),
            new { tempId = "op_template", operatorType = "TemplateMatching", parameters },
            Output());
    }

    internal static object HoleDistanceFlow()
    {
        return Draft(
            Camera("line-cam"),
            new { tempId = "op_circle_a", operatorType = "CircleMeasurement", parameters = new Dictionary<string, string> { ["Roi"] = "hole-a" } },
            new { tempId = "op_circle_b", operatorType = "CircleMeasurement", parameters = new Dictionary<string, string> { ["Roi"] = "hole-b" } },
            new { tempId = "op_distance", operatorType = "MeasureDistance", parameters = new Dictionary<string, string> { ["Unit"] = "mm" } },
            Output());
    }

    internal static object ModelFlow(string cameraBindingId, string modelId)
    {
        return Draft(
            Camera(cameraBindingId),
            new { tempId = "op_model", operatorType = "DeepLearning", parameters = new Dictionary<string, string> { ["ModelId"] = modelId } },
            Output());
    }

    internal static object MultiOperatorFlow()
    {
        return Draft(
            Camera("line-cam"),
            new { tempId = "op_template", operatorType = "TemplateMatching", parameters = new Dictionary<string, string> { ["TemplateId"] = "fixture-template" } },
            new { tempId = "op_circle", operatorType = "CircleMeasurement", parameters = new Dictionary<string, string> { ["Roi"] = "feature-a" } },
            new { tempId = "op_model", operatorType = "DeepLearning", parameters = new Dictionary<string, string> { ["ModelId"] = "remote-control-model" } },
            Output());
    }

    internal static object MissingParameterFlow()
    {
        return Draft(
            Camera("line-cam"),
            new { tempId = "op_template", operatorType = "TemplateMatching", parameters = new Dictionary<string, string>() },
            Output());
    }

    internal static object PlcFlow()
    {
        return Draft(
            Camera("line-cam"),
            new { tempId = "op_template", operatorType = "TemplateMatching", parameters = new Dictionary<string, string> { ["TemplateId"] = "wire-template" } },
            new { tempId = "op_output", operatorType = "ResultOutput", parameters = new Dictionary<string, string> { ["Channel"] = "plc", ["PlcAddress"] = "plc-output-token" } });
    }

    private static object Camera(string cameraBindingId)
    {
        return new
        {
            tempId = "op_cam",
            operatorType = "ImageAcquisition",
            parameters = new Dictionary<string, string>
            {
                ["SourceType"] = "Camera",
                ["CameraBindingId"] = cameraBindingId
            }
        };
    }

    private static object Output()
    {
        return new
        {
            tempId = "op_output",
            operatorType = "ResultOutput",
            parameters = new Dictionary<string, string>
            {
                ["OutputChannelId"] = "qa-metadata"
            }
        };
    }

    private static object Draft(params object[] operators)
    {
        return new
        {
            operators,
            connections = Array.Empty<object>()
        };
    }
}

public sealed class RuntimePreviewRedactedFlowCorpusService
{
    public RuntimePreviewRedactedFlowCorpusDocument BuildCorpus()
    {
        var cases = CreateCases();
        return new RuntimePreviewRedactedFlowCorpusDocument
        {
            GeneratedAtUtc = DateTimeOffset.UtcNow,
            CaseCount = cases.Count,
            Cases = cases,
            MetadataOnly = true,
            RealResourcesTouched = false
        };
    }

    public static IReadOnlyList<RuntimePreviewRedactedFlowCorpusCase> CreateCases()
    {
        return
        [
            Case("RP-RF-001", "connector_line", "wire_sequence", "Verify harness wire order before release.", ["ImageAcquisition", "TemplateMatching", "ResultOutput"], RuntimePreviewScenarioEvidenceStatuses.Passed, RuntimePreviewScenarioEvidenceStatuses.Passed, "low", "Review metadata manifest and keep real pilot gate closed.", RuntimePreviewScenarioCorpusService.Flow("line-cam", templateId: "wire-template")),
            Case("RP-RF-002", "remote_control_station", "remote_control_defect", "Detect missing buttons and label defects on a remote controller.", ["ImageAcquisition", "DeepLearning", "ResultOutput"], RuntimePreviewScenarioEvidenceStatuses.Passed, RuntimePreviewScenarioEvidenceStatuses.Passed, "medium", "Request engineer approval for DeepLearning metadata release review.", RuntimePreviewScenarioCorpusService.ModelFlow("line-cam", "remote-control-model"), stationProfileId: "sp-dl-review-v14", releaseDecision: "requires_engineer_approval", requiredApprovals: ["deep_learning_release_review"]),
            Case("RP-RF-003", "fixture_station", "template_measurement_combo", "Locate fixture by template and measure a downstream feature.", ["ImageAcquisition", "TemplateMatching", "CircleMeasurement", "DeepLearning", "ResultOutput"], RuntimePreviewScenarioEvidenceStatuses.Passed, RuntimePreviewScenarioEvidenceStatuses.Passed, "medium", "Review combined template, measurement, and DeepLearning operator contracts.", RuntimePreviewScenarioCorpusService.MultiOperatorFlow(), stationProfileId: "sp-dl-review-v14", releaseDecision: "requires_engineer_approval", requiredApprovals: ["deep_learning_release_review", "medium_manifest_risk"]),
            Case("RP-RF-004", "measurement_station", "hole_distance", "Measure distance between two holes for dimensional inspection.", ["ImageAcquisition", "CircleMeasurement", "MeasureDistance", "ResultOutput"], RuntimePreviewScenarioEvidenceStatuses.Passed, RuntimePreviewScenarioEvidenceStatuses.Passed, "low", "Confirm measurement unit and tolerance source.", RuntimePreviewScenarioCorpusService.HoleDistanceFlow()),
            Case("RP-RF-005", "terminal_station", "terminal_color_order", "Check terminal color sequence with a catalog template.", ["ImageAcquisition", "TemplateMatching", "ResultOutput"], RuntimePreviewScenarioEvidenceStatuses.Passed, RuntimePreviewScenarioEvidenceStatuses.Passed, "low", "Confirm template ownership and output channel mapping.", RuntimePreviewScenarioCorpusService.Flow("line-cam", templateId: "terminal-color-template")),
            Case("RP-RF-006", "line_station", "missing_camera", "Workflow references a camera binding not in the pilot catalog.", ["ImageAcquisition", "TemplateMatching", "ResultOutput"], RuntimePreviewScenarioEvidenceStatuses.NotReady, RuntimePreviewScenarioEvidenceStatuses.NotReady, "missing_camera_binding", "Bind an allowlisted metadata camera before package review.", RuntimePreviewScenarioCorpusService.Flow("missing-cam", templateId: "wire-template")),
            Case("RP-RF-007", "fixture_station", "missing_template", "TemplateMatching has no TemplateId metadata handle.", ["ImageAcquisition", "TemplateMatching", "ResultOutput"], RuntimePreviewScenarioEvidenceStatuses.NotReady, RuntimePreviewScenarioEvidenceStatuses.NotReady, "missing_template", "Assign an allowlisted TemplateId; do not use file paths.", RuntimePreviewScenarioCorpusService.Flow("line-cam")),
            Case("RP-RF-008", "remote_control_station", "missing_model", "DeepLearning operator has unresolved model metadata.", ["ImageAcquisition", "DeepLearning", "ResultOutput"], RuntimePreviewScenarioEvidenceStatuses.NotReady, RuntimePreviewScenarioEvidenceStatuses.NotReady, "missing_model", "Bind ModelId from catalog; do not load a model file.", RuntimePreviewScenarioCorpusService.ModelFlow("line-cam", "<pending-model>")),
            Case("RP-RF-009", "output_station", "missing_output_channel", "ResultOutput is missing a safe output channel metadata id.", ["ImageAcquisition", "TemplateMatching", "ResultOutput"], RuntimePreviewScenarioEvidenceStatuses.NotReady, RuntimePreviewScenarioEvidenceStatuses.NotReady, "missing_output_channel", "Choose OutputChannelId before package review.", MissingOutputFlow()),
            Case("RP-RF-010", "station_release", "plc_station_deny", "User intent includes PLC or Station release action.", ["ImageAcquisition", "TemplateMatching", "ResultOutput"], RuntimePreviewScenarioEvidenceStatuses.Denied, RuntimePreviewScenarioEvidenceStatuses.Denied, "plc_station_denied", "Remove PLC/Station intent; this console cannot write or deploy.", RuntimePreviewScenarioCorpusService.PlcFlow(), stationCompatibility: RuntimePreviewScenarioEvidenceStatuses.Denied, blockedReasons: ["plc_station_denied", "station_plc_or_direct_station_intent_forbidden"]),
            Case("RP-RF-011", "template_station", "dangerous_path", "Template dependency tries to point at an external path.", ["ImageAcquisition", "TemplateMatching", "ResultOutput"], RuntimePreviewScenarioEvidenceStatuses.Denied, RuntimePreviewScenarioEvidenceStatuses.Denied, "dangerous_resource", "Replace path-like metadata with a catalog TemplateId.", RuntimePreviewScenarioCorpusService.Flow("line-cam", templatePath: "external:/blocked-template")),
            Case("RP-RF-012", "line_station", "allowlist_mismatch", "Workflow camera handle is valid-looking but not allowlisted for pilot.", ["ImageAcquisition", "TemplateMatching", "ResultOutput"], RuntimePreviewScenarioEvidenceStatuses.NotReady, RuntimePreviewScenarioEvidenceStatuses.NotReady, "allowlist_mismatch", "Review allowlist diff and confirm the catalog handle.", RuntimePreviewScenarioCorpusService.Flow("camera-not-allowlisted", templateId: "wire-template")),
            Case("RP-RF-013", "dual_camera_station", "multi_camera_flow", "Two camera metadata handles feed one inspection decision.", ["ImageAcquisition", "ImageAcquisition", "TemplateMatching", "ResultOutput"], RuntimePreviewScenarioEvidenceStatuses.NotReady, RuntimePreviewScenarioEvidenceStatuses.NotReady, "multi_camera_review", "Confirm both camera bindings are catalog allowlisted.", MultiCameraFlow(), stationProfileId: "sp-low-ipc-v12", stationCompatibility: RuntimePreviewScenarioEvidenceStatuses.NotReady, blockedReasons: ["multi_camera_review", "station_camera_slots_insufficient"]),
            Case("RP-RF-014", "ai_station", "multi_model_flow", "Two model metadata handles are required for final judgment.", ["ImageAcquisition", "DeepLearning", "DeepLearning", "ResultOutput"], RuntimePreviewScenarioEvidenceStatuses.NotReady, RuntimePreviewScenarioEvidenceStatuses.NotReady, "multi_model_review", "Confirm all ModelIds and output aggregation before package review.", MultiModelFlow(), stationProfileId: "sp-dl-review-v14", requiredApprovals: ["deep_learning_release_review"]),
            Case("RP-RF-015", "parameter_review_station", "parameter_missing", "A key operator parameter is missing even though the draft can be edited.", ["ImageAcquisition", "TemplateMatching", "ResultOutput"], RuntimePreviewScenarioEvidenceStatuses.NotReady, RuntimePreviewScenarioEvidenceStatuses.NotReady, "missing_parameter", "Complete required operator parameters and rerun readiness.", RuntimePreviewScenarioCorpusService.MissingParameterFlow(), contractExpectations: ["TemplateMatching.TemplateId required"]),
            Case("RP-RF-016", "release_review", "package_manifest_blocked", "Manifest dry-run blocks package review because dependencies are incomplete.", ["ImageAcquisition", "DeepLearning", "ResultOutput"], RuntimePreviewScenarioEvidenceStatuses.NotReady, RuntimePreviewScenarioEvidenceStatuses.NotReady, "manifest_dependency_blocked", "Resolve manifest dependencies; no package may be created.", RuntimePreviewScenarioCorpusService.ModelFlow("line-cam", "<pending-model>")),
            Case("RP-RF-017", "draft_review", "workflow_editable_package_blocked", "Engineer can edit the workflow draft but package review is not allowed.", ["ImageAcquisition", "TemplateMatching", "ResultOutput"], RuntimePreviewScenarioEvidenceStatuses.NotReady, RuntimePreviewScenarioEvidenceStatuses.NotReady, "draft_allowed_package_blocked", "Keep editing the workflow; do not start release review yet.", RuntimePreviewScenarioCorpusService.Flow("missing-cam", templateId: "fixture-template")),
            Case("RP-RF-018", "precheck_station", "runtime_package_precheck_blocked", "Runtime package precheck risk blocks release.", ["ImageAcquisition", "DeepLearning", "ResultOutput"], RuntimePreviewScenarioEvidenceStatuses.NotReady, RuntimePreviewScenarioEvidenceStatuses.NotReady, "precheck_not_ready", "Rerun readiness after model metadata is resolved.", RuntimePreviewScenarioCorpusService.ModelFlow("line-cam", "<pending-model>")),
            Case("RP-RF-019", "template_measurement_station", "template_plus_hole_distance", "Template positioning and hole distance measurement share one camera.", ["ImageAcquisition", "TemplateMatching", "CircleMeasurement", "DeepLearning", "ResultOutput"], RuntimePreviewScenarioEvidenceStatuses.Passed, RuntimePreviewScenarioEvidenceStatuses.Passed, "medium", "Request engineer approval for medium-risk measurement release review.", RuntimePreviewScenarioCorpusService.MultiOperatorFlow(), stationProfileId: "sp-dl-review-v14", releaseDecision: "requires_engineer_approval", requiredApprovals: ["medium_manifest_risk", "deep_learning_release_review"]),
            Case("RP-RF-020", "release_blocked_station", "direct_deploy_request_denied", "User asks to release to Station directly from preview.", ["ImageAcquisition", "TemplateMatching", "ResultOutput"], RuntimePreviewScenarioEvidenceStatuses.Denied, RuntimePreviewScenarioEvidenceStatuses.Denied, "deployment_intent_denied", "Use only metadata review; direct deployment remains forbidden.", RuntimePreviewScenarioCorpusService.PlcFlow(), stationCompatibility: RuntimePreviewScenarioEvidenceStatuses.Denied, blockedReasons: ["deployment_intent_denied", "station_plc_or_direct_station_intent_forbidden"]),
            Case("RP-RF-021", "low_spec_ipc", "low_ipc_operator_count_exceeded", "Traditional flow exceeds low-spec IPC operator limit.", ["ImageAcquisition", "TemplateMatching", "CircleMeasurement", "MeasureDistance", "LineMeasurement", "ResultOutput"], RuntimePreviewScenarioEvidenceStatuses.Passed, RuntimePreviewScenarioEvidenceStatuses.Passed, "high", "Split the workflow or target a higher-capacity IPC profile.", LowSpecOperatorCountFlow(), stationProfileId: "sp-low-ipc-v12", stationCompatibility: RuntimePreviewScenarioEvidenceStatuses.NotReady, releaseDecision: "release_blocked", blockedReasons: ["station_operator_count_exceeded"]),
            Case("RP-RF-022", "dual_camera_station", "multi_camera_slot_shortage", "Two-camera fixture flow targets a one-slot station profile.", ["ImageAcquisition", "ImageAcquisition", "TemplateMatching", "ResultOutput"], RuntimePreviewScenarioEvidenceStatuses.Passed, RuntimePreviewScenarioEvidenceStatuses.Passed, "high", "Choose a Station profile with enough camera binding slots.", MultiCameraFlow(allowlistedSecondCamera: true), stationProfileId: "sp-low-ipc-v12", stationCompatibility: RuntimePreviewScenarioEvidenceStatuses.NotReady, releaseDecision: "release_blocked", blockedReasons: ["station_camera_slots_insufficient"]),
            Case("RP-RF-023", "traditional_station", "unsupported_deep_learning", "DeepLearning flow targets a traditional-only Station profile.", ["ImageAcquisition", "DeepLearning", "ResultOutput"], RuntimePreviewScenarioEvidenceStatuses.Passed, RuntimePreviewScenarioEvidenceStatuses.Passed, "high", "Move the flow to a DeepLearning-capable Station profile.", ModelKindFlow("remote-control-model", "detection"), stationProfileId: "sp-release-standard-v14", stationCompatibility: RuntimePreviewScenarioEvidenceStatuses.NotReady, releaseDecision: "release_blocked", blockedReasons: ["station_operator_not_supported:DeepLearning"]),
            Case("RP-RF-024", "output_lite_station", "output_channel_kind_missing", "ResultOutput maps to a channel kind absent on the target Station.", ["ImageAcquisition", "TemplateMatching", "ResultOutput"], RuntimePreviewScenarioEvidenceStatuses.Passed, RuntimePreviewScenarioEvidenceStatuses.Passed, "high", "Remap ResultOutput to a Station-supported output channel kind.", RuntimePreviewScenarioCorpusService.Flow("line-cam", templateId: "wire-template"), stationProfileId: "sp-output-lite-v14", stationCompatibility: RuntimePreviewScenarioEvidenceStatuses.NotReady, releaseDecision: "release_blocked", blockedReasons: ["station_output_channel_kind_missing"]),
            Case("RP-RF-025", "plc_guard_station", "plc_write_forbidden", "ResultOutput attempts PLC-style output while Station profile forbids PLC writes.", ["ImageAcquisition", "TemplateMatching", "ResultOutput"], RuntimePreviewScenarioEvidenceStatuses.Denied, RuntimePreviewScenarioEvidenceStatuses.Denied, "plc_write_forbidden", "Remove PLC write intent and keep output metadata-only.", RuntimePreviewScenarioCorpusService.PlcFlow(), stationCompatibility: RuntimePreviewScenarioEvidenceStatuses.Denied, releaseDecision: "release_blocked", blockedReasons: ["plc_write_forbidden", "station_plc_or_direct_station_intent_forbidden"], contractExpectations: ["ResultOutput forbids PlcAddress"]),
            Case("RP-RF-026", "legacy_runtime_station", "runtime_version_too_low", "DeepLearning requires Runtime 1.4.0 but target profile is Runtime 1.2.0.", ["ImageAcquisition", "DeepLearning", "ResultOutput"], RuntimePreviewScenarioEvidenceStatuses.Passed, RuntimePreviewScenarioEvidenceStatuses.Passed, "high", "Select a Runtime 1.4.0 Station profile before release review.", ModelKindFlow("remote-control-model", "detection"), stationProfileId: "sp-low-ipc-v12", stationCompatibility: RuntimePreviewScenarioEvidenceStatuses.NotReady, releaseDecision: "release_blocked", blockedReasons: ["station_runtime_version_too_low"]),
            Case("RP-RF-027", "model_station", "model_type_incompatible", "Segmentation model metadata targets a detection-only Station profile.", ["ImageAcquisition", "DeepLearning", "ResultOutput"], RuntimePreviewScenarioEvidenceStatuses.Passed, RuntimePreviewScenarioEvidenceStatuses.Passed, "high", "Use a supported detection model or target a segmentation-capable Station profile.", ModelKindFlow("segmentation-model", "segmentation"), stationProfileId: "sp-detection-only-v14", stationCompatibility: RuntimePreviewScenarioEvidenceStatuses.NotReady, releaseDecision: "release_blocked", blockedReasons: ["station_model_kind_not_supported"]),
            Case("RP-RF-028", "template_station", "template_dependency_missing", "TemplateMatching dependency is not closed in manifest metadata.", ["ImageAcquisition", "TemplateMatching", "ResultOutput"], RuntimePreviewScenarioEvidenceStatuses.NotReady, RuntimePreviewScenarioEvidenceStatuses.NotReady, "template_dependency_missing", "Bind TemplateId metadata and rerun manifest dry-run.", RuntimePreviewScenarioCorpusService.Flow("line-cam"), stationCompatibility: RuntimePreviewScenarioEvidenceStatuses.NotReady, releaseDecision: "release_blocked", blockedReasons: ["template_dependency_missing"], contractExpectations: ["TemplateMatching.TemplateId required"]),
            Case("RP-RF-029", "traditional_release_station", "traditional_vision_release_allowed", "Traditional template and measurement flow passes full release review simulation.", ["ImageAcquisition", "TemplateMatching", "CircleMeasurement", "MeasureDistance", "ResultOutput"], RuntimePreviewScenarioEvidenceStatuses.Passed, RuntimePreviewScenarioEvidenceStatuses.Passed, "low", "Release review simulator can allow this metadata-only traditional flow.", TraditionalVisionPassFlow(), stationProfileId: "sp-release-standard-v14", releaseDecision: "release_allowed"),
            Case("RP-RF-030", "dl_review_station", "deep_learning_requires_engineer_approval", "DeepLearning metadata is compatible but requires engineer approval before release review.", ["ImageAcquisition", "DeepLearning", "ResultOutput"], RuntimePreviewScenarioEvidenceStatuses.Passed, RuntimePreviewScenarioEvidenceStatuses.Passed, "medium", "Obtain DeepLearning release approval before allowing release review.", ModelKindFlow("remote-control-model", "detection"), stationProfileId: "sp-dl-review-v14", releaseDecision: "requires_engineer_approval", requiredApprovals: ["deep_learning_release_review"]),
            Case("RP-RF-031", "multi_station_review", "multi_station_requires_engineer_approval", "A multi-station review case is compatible but requires engineer approval.", ["ImageAcquisition", "TemplateMatching", "ResultOutput"], RuntimePreviewScenarioEvidenceStatuses.Passed, RuntimePreviewScenarioEvidenceStatuses.Passed, "medium", "Obtain multi-station release approval before allowing release review.", MultiStationReviewFlow(), stationProfileId: "sp-multi-station-v14", releaseDecision: "requires_engineer_approval", requiredApprovals: ["multi_station_review"]),
            Case("RP-RF-032", "release_decision_station", "release_blocked_operator_contract", "Release review is blocked by a missing TemplateMatching contract parameter.", ["ImageAcquisition", "TemplateMatching", "ResultOutput"], RuntimePreviewScenarioEvidenceStatuses.NotReady, RuntimePreviewScenarioEvidenceStatuses.NotReady, "operator_contract_missing_parameter", "Fix TemplateMatching TemplateId before rerunning release review.", RuntimePreviewScenarioCorpusService.MissingParameterFlow(), stationCompatibility: RuntimePreviewScenarioEvidenceStatuses.NotReady, releaseDecision: "release_blocked", blockedReasons: ["operator_contract_missing_parameter"], contractExpectations: ["TemplateMatching.TemplateId required"]),
            Case("RP-RF-033", "traditional_release_station", "blob_release_allowed", "Blob analysis metadata passes release review on the standard IPC.", ["ImageAcquisition", "BlobAnalysis", "ResultOutput"], RuntimePreviewScenarioEvidenceStatuses.Passed, RuntimePreviewScenarioEvidenceStatuses.Passed, "low", "Release review simulator can allow the BlobAnalysis metadata contract.", BlobFlow(), releaseDecision: "release_allowed"),
            Case("RP-RF-034", "traditional_release_station", "threshold_release_allowed", "Thresholding metadata passes release review without image reads.", ["ImageAcquisition", "Thresholding", "ResultOutput"], RuntimePreviewScenarioEvidenceStatuses.Passed, RuntimePreviewScenarioEvidenceStatuses.Passed, "low", "Confirm threshold parameter ownership and keep package creation disabled.", ThresholdFlow(), releaseDecision: "release_allowed"),
            Case("RP-RF-035", "traditional_release_station", "edge_release_allowed", "EdgeDetection metadata passes release review on the standard IPC.", ["ImageAcquisition", "EdgeDetection", "ResultOutput"], RuntimePreviewScenarioEvidenceStatuses.Passed, RuntimePreviewScenarioEvidenceStatuses.Passed, "low", "Review edge polarity metadata before any future real pilot gate.", EdgeFlow(), releaseDecision: "release_allowed"),
            Case("RP-RF-036", "traditional_release_station", "shape_matching_release_allowed", "ShapeMatching uses TemplateId metadata and passes release review.", ["ImageAcquisition", "ShapeMatching", "ResultOutput"], RuntimePreviewScenarioEvidenceStatuses.Passed, RuntimePreviewScenarioEvidenceStatuses.Passed, "low", "Confirm ShapeMatching TemplateId ownership and leave template files unread.", ShapeMatchingFlow(), releaseDecision: "release_allowed"),
            Case("RP-RF-037", "template_only_station", "template_only_profile_pass", "Template-only Station profile accepts a TemplateMatching release review.", ["ImageAcquisition", "TemplateMatching", "ResultOutput"], RuntimePreviewScenarioEvidenceStatuses.Passed, RuntimePreviewScenarioEvidenceStatuses.Passed, "low", "Template-only Station compatibility is clean for this metadata review.", RuntimePreviewScenarioCorpusService.Flow("line-cam", templateId: "fixture-template"), stationProfileId: "sp-template-only-v14", releaseDecision: "release_allowed"),
            Case("RP-RF-038", "measurement_only_station", "measurement_only_profile_pass", "Measurement-only Station profile accepts circle and distance metadata.", ["ImageAcquisition", "CircleMeasurement", "MeasureDistance", "ResultOutput"], RuntimePreviewScenarioEvidenceStatuses.Passed, RuntimePreviewScenarioEvidenceStatuses.Passed, "low", "Measurement metadata is compatible; keep calibration review metadata-only.", RuntimePreviewScenarioCorpusService.HoleDistanceFlow(), stationProfileId: "sp-measurement-only-v14", releaseDecision: "release_allowed"),
            Case("RP-RF-039", "segmentation_review_station", "semantic_segmentation_requires_approval", "SemanticSegmentation metadata is compatible but requires engineer approval.", ["ImageAcquisition", "SemanticSegmentation", "ResultOutput"], RuntimePreviewScenarioEvidenceStatuses.Passed, RuntimePreviewScenarioEvidenceStatuses.Passed, "medium", "Request segmentation model release approval before go decision.", ModelOperatorFlow("SemanticSegmentation", "segmentation-model", "segmentation"), stationProfileId: "sp-dl-review-v14", releaseDecision: "requires_engineer_approval", requiredApprovals: ["deep_learning_release_review"]),
            Case("RP-RF-040", "defect_review_station", "surface_defect_requires_approval", "Surface defect metadata is compatible but requires engineer approval.", ["ImageAcquisition", "SurfaceDefectDetection", "ResultOutput"], RuntimePreviewScenarioEvidenceStatuses.Passed, RuntimePreviewScenarioEvidenceStatuses.Passed, "medium", "Request surface defect model approval before release review can be allowed.", ModelOperatorFlow("SurfaceDefectDetection", "remote-control-model", "detection"), stationProfileId: "sp-dl-review-v14", releaseDecision: "requires_engineer_approval", requiredApprovals: ["deep_learning_release_review"]),
            Case("RP-RF-041", "release_approval_station", "release_approval_station_dl", "Release approval Station requires engineer sign-off for DeepLearning metadata.", ["ImageAcquisition", "DeepLearning", "ResultOutput"], RuntimePreviewScenarioEvidenceStatuses.Passed, RuntimePreviewScenarioEvidenceStatuses.Passed, "medium", "Request release approval for DeepLearning on the approval Station profile.", ModelKindFlow("remote-control-model", "detection"), stationProfileId: "sp-release-approval-v14", releaseDecision: "requires_engineer_approval", requiredApprovals: ["deep_learning_release_review"]),
            Case("RP-RF-042", "multi_camera_station", "multi_camera_station_requires_approval", "Multi-camera Station profile is compatible but requires engineer review.", ["ImageAcquisition", "ImageAcquisition", "TemplateMatching", "ResultOutput"], RuntimePreviewScenarioEvidenceStatuses.Passed, RuntimePreviewScenarioEvidenceStatuses.Passed, "medium", "Request multi-camera release review approval before go decision.", MultiCameraFlow(allowlistedSecondCamera: true), stationProfileId: "sp-multi-camera-v14", releaseDecision: "requires_engineer_approval", requiredApprovals: ["multi_station_review"]),
            Case("RP-RF-043", "multi_station_review", "multi_station_template_summary_approval", "Metadata summary output across stations requires engineer approval.", ["ImageAcquisition", "TemplateMatching", "ResultOutput"], RuntimePreviewScenarioEvidenceStatuses.Passed, RuntimePreviewScenarioEvidenceStatuses.Passed, "medium", "Request multi-station summary approval before release review can be allowed.", MultiStationReviewFlow(), stationProfileId: "sp-multi-station-v14", releaseDecision: "requires_engineer_approval", requiredApprovals: ["multi_station_review"]),
            Case("RP-RF-044", "output_lite_station", "output_lite_local_log_allowed", "Output-lite Station passes when ResultOutput maps to local-log metadata.", ["ImageAcquisition", "TemplateMatching", "ResultOutput"], RuntimePreviewScenarioEvidenceStatuses.Passed, RuntimePreviewScenarioEvidenceStatuses.Passed, "low", "Release review simulator can allow local-log output mapping.", OutputChannelFlow("local-log"), stationProfileId: "sp-output-lite-v14", releaseDecision: "release_allowed"),
            Case("RP-RF-045", "low_spec_ipc", "low_spec_minimal_blob_allowed", "Low-spec IPC accepts a three-operator BlobAnalysis metadata flow.", ["ImageAcquisition", "BlobAnalysis", "ResultOutput"], RuntimePreviewScenarioEvidenceStatuses.Passed, RuntimePreviewScenarioEvidenceStatuses.Passed, "low", "Minimal BlobAnalysis flow fits low-spec IPC operator capacity.", BlobFlow(), stationProfileId: "sp-low-ipc-v12", releaseDecision: "release_allowed"),
            Case("RP-RF-046", "legacy_runtime_station", "legacy_runtime_traditional_allowed", "Legacy runtime Station accepts traditional TemplateMatching metadata.", ["ImageAcquisition", "TemplateMatching", "ResultOutput"], RuntimePreviewScenarioEvidenceStatuses.Passed, RuntimePreviewScenarioEvidenceStatuses.Passed, "low", "Traditional metadata can proceed on legacy runtime with real pilot gates closed.", RuntimePreviewScenarioCorpusService.Flow("line-cam", templateId: "wire-template"), stationProfileId: "sp-legacy-runtime-v12", releaseDecision: "release_allowed"),
            Case("RP-RF-047", "legacy_runtime_station", "legacy_runtime_deep_learning_blocked", "Legacy runtime blocks DeepLearning metadata release review.", ["ImageAcquisition", "DeepLearning", "ResultOutput"], RuntimePreviewScenarioEvidenceStatuses.Passed, RuntimePreviewScenarioEvidenceStatuses.Passed, "high", "Select a Runtime 1.4.0 DeepLearning-capable Station profile.", ModelKindFlow("remote-control-model", "detection"), stationProfileId: "sp-legacy-runtime-v12", stationCompatibility: RuntimePreviewScenarioEvidenceStatuses.NotReady, releaseDecision: "release_blocked", blockedReasons: ["station_runtime_version_too_low", "station_operator_not_supported:DeepLearning"]),
            Case("RP-RF-048", "template_only_station", "template_only_deep_learning_blocked", "Template-only Station blocks DeepLearning operator support.", ["ImageAcquisition", "DeepLearning", "ResultOutput"], RuntimePreviewScenarioEvidenceStatuses.Passed, RuntimePreviewScenarioEvidenceStatuses.Passed, "high", "Move DeepLearning metadata to a model-capable Station profile.", ModelKindFlow("remote-control-model", "detection"), stationProfileId: "sp-template-only-v14", stationCompatibility: RuntimePreviewScenarioEvidenceStatuses.NotReady, releaseDecision: "release_blocked", blockedReasons: ["station_operator_not_supported:DeepLearning"]),
            Case("RP-RF-049", "measurement_only_station", "measurement_only_template_blocked", "Measurement-only Station blocks TemplateMatching operator support.", ["ImageAcquisition", "TemplateMatching", "ResultOutput"], RuntimePreviewScenarioEvidenceStatuses.Passed, RuntimePreviewScenarioEvidenceStatuses.Passed, "high", "Move TemplateMatching metadata to a template-capable Station profile.", RuntimePreviewScenarioCorpusService.Flow("line-cam", templateId: "fixture-template"), stationProfileId: "sp-measurement-only-v14", stationCompatibility: RuntimePreviewScenarioEvidenceStatuses.NotReady, releaseDecision: "release_blocked", blockedReasons: ["station_operator_not_supported:TemplateMatching"]),
            Case("RP-RF-050", "judgment_station", "result_judgment_contract_pass", "ResultJudgment metadata with RuleId passes release review.", ["ImageAcquisition", "TemplateMatching", "ResultJudgment", "ResultOutput"], RuntimePreviewScenarioEvidenceStatuses.Passed, RuntimePreviewScenarioEvidenceStatuses.Passed, "low", "ResultJudgment rule metadata is complete and release review can be allowed.", ResultJudgmentFlow(includeRule: true), releaseDecision: "release_allowed"),
            Case("RP-RF-051", "judgment_station", "result_judgment_missing_rule_blocked", "ResultJudgment without RuleId fails operator contract validation.", ["ImageAcquisition", "TemplateMatching", "ResultJudgment", "ResultOutput"], RuntimePreviewScenarioEvidenceStatuses.Passed, RuntimePreviewScenarioEvidenceStatuses.Passed, "operator_contract_missing_parameter", "Add ResultJudgment RuleId before rerunning full release review.", ResultJudgmentFlow(includeRule: false), stationCompatibility: RuntimePreviewScenarioEvidenceStatuses.Passed, releaseDecision: "release_blocked", blockedReasons: ["operator_contract_missing_parameter:ResultJudgment:RuleId"], contractExpectations: ["ResultJudgment.RuleId required"]),
            Case("RP-RF-052", "output_station", "result_output_contract_missing_channel", "ResultOutput without OutputChannelId fails package and contract review.", ["ImageAcquisition", "TemplateMatching", "ResultOutput"], RuntimePreviewScenarioEvidenceStatuses.NotReady, RuntimePreviewScenarioEvidenceStatuses.NotReady, "operator_contract_missing_parameter", "Choose OutputChannelId before release review.", MissingOutputFlow(), stationCompatibility: RuntimePreviewScenarioEvidenceStatuses.NotReady, releaseDecision: "release_blocked", blockedReasons: ["operator_contract_missing_parameter:ResultOutput:OutputChannelId"], contractExpectations: ["ResultOutput.OutputChannelId required"]),
            Case("RP-RF-053", "network_guard_station", "http_request_forbidden_preview", "HttpRequest is blocked as external network intent in release review.", ["ImageAcquisition", "HttpRequest", "ResultOutput"], RuntimePreviewScenarioEvidenceStatuses.Passed, RuntimePreviewScenarioEvidenceStatuses.Passed, "network_write_forbidden", "Remove HttpRequest; preview cannot perform network writes or direct calls.", ExternalIntentFlow("HttpRequest"), stationCompatibility: RuntimePreviewScenarioEvidenceStatuses.NotReady, releaseDecision: "release_blocked", blockedReasons: ["station_plc_or_direct_station_intent_forbidden", "operator_contract_forbidden_parameter:HttpRequest:runtime_dependency_forbidden_for_preview"]),
            Case("RP-RF-054", "plc_guard_station", "modbus_forbidden_preview", "ModbusCommunication is blocked even when represented as metadata.", ["ImageAcquisition", "ModbusCommunication", "ResultOutput"], RuntimePreviewScenarioEvidenceStatuses.Passed, RuntimePreviewScenarioEvidenceStatuses.Passed, "plc_write_forbidden", "Remove ModbusCommunication from pre-release review; PLC writes remain forbidden.", ExternalIntentFlow("ModbusCommunication"), stationProfileId: "sp-plc-denied-v14", stationCompatibility: RuntimePreviewScenarioEvidenceStatuses.NotReady, releaseDecision: "release_blocked", blockedReasons: ["station_plc_or_direct_station_intent_forbidden", "operator_contract_forbidden_parameter:ModbusCommunication:runtime_dependency_forbidden_for_preview"]),
            Case("RP-RF-055", "plc_guard_station", "plc_direct_intent_denied_final", "Direct PLC output intent is denied before release review can proceed.", ["ImageAcquisition", "TemplateMatching", "ResultOutput"], RuntimePreviewScenarioEvidenceStatuses.Denied, RuntimePreviewScenarioEvidenceStatuses.Denied, "plc_write_forbidden", "Remove PLC output intent and use a metadata output channel.", RuntimePreviewScenarioCorpusService.PlcFlow(), stationProfileId: "sp-plc-denied-v14", stationCompatibility: RuntimePreviewScenarioEvidenceStatuses.Denied, releaseDecision: "release_blocked", blockedReasons: ["plc_write_forbidden", "station_plc_or_direct_station_intent_forbidden"], contractExpectations: ["ResultOutput forbids PlcAddress"]),
            Case("RP-RF-056", "model_guard_station", "model_path_denied_final", "ModelPath-like metadata is denied; only ModelId handles are allowed.", ["ImageAcquisition", "DeepLearning", "ResultOutput"], RuntimePreviewScenarioEvidenceStatuses.Denied, RuntimePreviewScenarioEvidenceStatuses.Denied, "dangerous_model_path", "Replace model path metadata with an allowlisted ModelId.", ModelPathFlow(), stationProfileId: "sp-dl-review-v14", stationCompatibility: RuntimePreviewScenarioEvidenceStatuses.Denied, releaseDecision: "release_blocked", blockedReasons: ["runtime_preview_external_path_denied", "model_path_denied"]),
            Case("RP-RF-057", "template_guard_station", "template_path_denied_final", "TemplatePath-like metadata is denied; only TemplateId handles are allowed.", ["ImageAcquisition", "TemplateMatching", "ResultOutput"], RuntimePreviewScenarioEvidenceStatuses.Denied, RuntimePreviewScenarioEvidenceStatuses.Denied, "dangerous_template_path", "Replace template path metadata with an allowlisted TemplateId.", RuntimePreviewScenarioCorpusService.Flow("line-cam", templatePath: "external:/blocked-template-final"), stationCompatibility: RuntimePreviewScenarioEvidenceStatuses.Denied, releaseDecision: "release_blocked", blockedReasons: ["runtime_preview_external_path_denied", "template_path_denied"]),
            Case("RP-RF-058", "image_guard_station", "image_bytes_denied_final", "Image byte-like metadata is denied before any image read can occur.", ["ImageAcquisition", "TemplateMatching", "ResultOutput"], RuntimePreviewScenarioEvidenceStatuses.Denied, RuntimePreviewScenarioEvidenceStatuses.Denied, "image_bytes_denied", "Remove image byte payloads and bind a redacted camera metadata handle.", ImageBytesDeniedFlow(), stationCompatibility: RuntimePreviewScenarioEvidenceStatuses.Denied, releaseDecision: "release_blocked", blockedReasons: ["runtime_preview_image_bytes_denied"]),
            Case("RP-RF-059", "package_guard_station", "package_path_denied_final", "PackagePath-like output intent is denied; no package path is accepted.", ["ImageAcquisition", "TemplateMatching", "ResultOutput"], RuntimePreviewScenarioEvidenceStatuses.Denied, RuntimePreviewScenarioEvidenceStatuses.Denied, "package_path_denied", "Remove package path metadata; this review never creates package files.", PackagePathDeniedFlow(), stationCompatibility: RuntimePreviewScenarioEvidenceStatuses.Denied, releaseDecision: "release_blocked", blockedReasons: ["runtime_preview_external_path_denied", "package_path_denied"]),
            Case("RP-RF-060", "output_lite_station", "manifest_ready_station_incompatible", "Manifest dry-run can be ready while release review is blocked by Station output compatibility.", ["ImageAcquisition", "TemplateMatching", "ResultOutput"], RuntimePreviewScenarioEvidenceStatuses.Passed, RuntimePreviewScenarioEvidenceStatuses.Passed, "high", "Remap output channel first, then rerun Station compatibility review.", OutputChannelFlow("qa-metadata"), stationProfileId: "sp-output-lite-v14", stationCompatibility: RuntimePreviewScenarioEvidenceStatuses.NotReady, releaseDecision: "release_blocked", blockedReasons: ["station_output_channel_kind_missing"])
        ];
    }

    private static RuntimePreviewRedactedFlowCorpusCase Case(
        string caseId,
        string stationType,
        string workflowKind,
        string purpose,
        IReadOnlyList<string> operators,
        string readiness,
        string packageReadiness,
        string manifestRisk,
        string engineerAction,
        object workflowDraft,
        string stationProfileId = "sp-release-standard-v14",
        string? stationCompatibility = null,
        string? releaseDecision = null,
        IReadOnlyList<string>? requiredApprovals = null,
        IReadOnlyList<string>? blockedReasons = null,
        IReadOnlyList<string>? contractExpectations = null)
    {
        var workflow = RuntimePreviewGovernanceRedactor.ToRedactedElement(workflowDraft);
        var effectiveStationCompatibility = string.IsNullOrWhiteSpace(stationCompatibility)
            ? packageReadiness
            : stationCompatibility;
        var effectiveReleaseDecision = string.IsNullOrWhiteSpace(releaseDecision)
            ? string.Equals(packageReadiness, RuntimePreviewScenarioEvidenceStatuses.Passed, StringComparison.OrdinalIgnoreCase)
                ? "release_allowed"
                : "release_blocked"
            : releaseDecision;
        var effectiveBlockedReasons = blockedReasons ??
                                      (string.Equals(effectiveReleaseDecision, "release_allowed", StringComparison.OrdinalIgnoreCase) ||
                                       string.Equals(effectiveReleaseDecision, "requires_engineer_approval", StringComparison.OrdinalIgnoreCase)
                                          ? []
                                          : [manifestRisk]);
        return new RuntimePreviewRedactedFlowCorpusCase
        {
            CaseId = caseId,
            StationType = stationType,
            WorkflowKind = workflowKind,
            BusinessPurpose = purpose,
            WorkflowDraftHash = RuntimePreviewGovernanceHashes.HashJsonElement(workflow),
            StationProfileId = stationProfileId,
            OperatorSummary = operators.Select(RuntimePreviewGovernanceRedactor.RedactScalar).ToList(),
            OperatorContractExpectations = contractExpectations ??
                                           operators.Select(item => $"{item}:metadata_contract").Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            ExpectedReadiness = readiness,
            ExpectedPackageReadiness = packageReadiness,
            ExpectedStationCompatibility = effectiveStationCompatibility,
            ExpectedReleaseReviewDecision = effectiveReleaseDecision,
            RequiredEngineerApprovals = requiredApprovals ?? [],
            ExpectedBlockedReasons = effectiveBlockedReasons,
            ExpectedManifestRisk = manifestRisk,
            ExpectedEngineerAction = engineerAction,
            RedactionStatus = "redacted_metadata_only",
            WorkflowDraft = workflow,
            MetadataOnly = true,
            RealResourcesTouched = false
        };
    }

    private static object MissingOutputFlow()
    {
        return new
        {
            operators = new object[]
            {
                new { tempId = "op_cam", operatorType = "ImageAcquisition", parameters = new Dictionary<string, string> { ["SourceType"] = "Camera", ["CameraBindingId"] = "line-cam" } },
                new { tempId = "op_template", operatorType = "TemplateMatching", parameters = new Dictionary<string, string> { ["TemplateId"] = "wire-template" } },
                new { tempId = "op_output", operatorType = "ResultOutput", parameters = new Dictionary<string, string>() }
            },
            connections = Array.Empty<object>()
        };
    }

    private static object MultiCameraFlow(bool allowlistedSecondCamera = false)
    {
        return new
        {
            operators = new object[]
            {
                new { tempId = "op_cam_a", operatorType = "ImageAcquisition", parameters = new Dictionary<string, string> { ["SourceType"] = "Camera", ["CameraBindingId"] = "line-cam" } },
                new { tempId = "op_cam_b", operatorType = "ImageAcquisition", parameters = new Dictionary<string, string> { ["SourceType"] = "Camera", ["CameraBindingId"] = allowlistedSecondCamera ? "side-cam" : "side-cam-pending" } },
                new { tempId = "op_template", operatorType = "TemplateMatching", parameters = new Dictionary<string, string> { ["TemplateId"] = "fixture-template" } },
                new { tempId = "op_output", operatorType = "ResultOutput", parameters = new Dictionary<string, string> { ["OutputChannelId"] = "qa-metadata" } }
            },
            connections = Array.Empty<object>()
        };
    }

    private static object MultiModelFlow()
    {
        return new
        {
            operators = new object[]
            {
                new { tempId = "op_cam", operatorType = "ImageAcquisition", parameters = new Dictionary<string, string> { ["SourceType"] = "Camera", ["CameraBindingId"] = "line-cam" } },
                new { tempId = "op_model_a", operatorType = "DeepLearning", parameters = new Dictionary<string, string> { ["ModelId"] = "remote-control-model" } },
                new { tempId = "op_model_b", operatorType = "DeepLearning", parameters = new Dictionary<string, string> { ["ModelId"] = "<pending-model>" } },
                new { tempId = "op_output", operatorType = "ResultOutput", parameters = new Dictionary<string, string> { ["OutputChannelId"] = "qa-metadata" } }
            },
            connections = Array.Empty<object>()
        };
    }

    private static object LowSpecOperatorCountFlow()
    {
        return new
        {
            operators = new object[]
            {
                new { tempId = "op_cam", operatorType = "ImageAcquisition", parameters = new Dictionary<string, string> { ["SourceType"] = "Camera", ["CameraBindingId"] = "line-cam" } },
                new { tempId = "op_template", operatorType = "TemplateMatching", parameters = new Dictionary<string, string> { ["TemplateId"] = "fixture-template" } },
                new { tempId = "op_circle", operatorType = "CircleMeasurement", parameters = new Dictionary<string, string> { ["Roi"] = "feature-a" } },
                new { tempId = "op_distance", operatorType = "MeasureDistance", parameters = new Dictionary<string, string> { ["Unit"] = "mm" } },
                new { tempId = "op_line", operatorType = "LineMeasurement", parameters = new Dictionary<string, string> { ["Roi"] = "edge-a" } },
                new { tempId = "op_output", operatorType = "ResultOutput", parameters = new Dictionary<string, string> { ["OutputChannelId"] = "qa-metadata" } }
            },
            connections = Array.Empty<object>()
        };
    }

    private static object ModelKindFlow(string modelId, string modelKind)
    {
        return new
        {
            operators = new object[]
            {
                new { tempId = "op_cam", operatorType = "ImageAcquisition", parameters = new Dictionary<string, string> { ["SourceType"] = "Camera", ["CameraBindingId"] = "line-cam" } },
                new { tempId = "op_model", operatorType = "DeepLearning", parameters = new Dictionary<string, string> { ["ModelId"] = modelId, ["ModelKind"] = modelKind } },
                new { tempId = "op_output", operatorType = "ResultOutput", parameters = new Dictionary<string, string> { ["OutputChannelId"] = "qa-metadata" } }
            },
            connections = Array.Empty<object>()
        };
    }

    private static object TraditionalVisionPassFlow()
    {
        return new
        {
            operators = new object[]
            {
                new { tempId = "op_cam", operatorType = "ImageAcquisition", parameters = new Dictionary<string, string> { ["SourceType"] = "Camera", ["CameraBindingId"] = "line-cam" } },
                new { tempId = "op_template", operatorType = "TemplateMatching", parameters = new Dictionary<string, string> { ["TemplateId"] = "fixture-template" } },
                new { tempId = "op_circle", operatorType = "CircleMeasurement", parameters = new Dictionary<string, string> { ["Roi"] = "feature-a" } },
                new { tempId = "op_distance", operatorType = "MeasureDistance", parameters = new Dictionary<string, string> { ["Unit"] = "mm" } },
                new { tempId = "op_output", operatorType = "ResultOutput", parameters = new Dictionary<string, string> { ["OutputChannelId"] = "qa-metadata" } }
            },
            connections = Array.Empty<object>()
        };
    }

    private static object MultiStationReviewFlow()
    {
        return new
        {
            operators = new object[]
            {
                new { tempId = "op_cam", operatorType = "ImageAcquisition", parameters = new Dictionary<string, string> { ["SourceType"] = "Camera", ["CameraBindingId"] = "line-cam" } },
                new { tempId = "op_template", operatorType = "TemplateMatching", parameters = new Dictionary<string, string> { ["TemplateId"] = "fixture-template" } },
                new { tempId = "op_output", operatorType = "ResultOutput", parameters = new Dictionary<string, string> { ["OutputChannelId"] = "metadata-summary" } }
            },
            connections = Array.Empty<object>()
        };
    }

    private static object BlobFlow()
    {
        return Draft(
            Camera("line-cam"),
            new { tempId = "op_blob", operatorType = "BlobAnalysis", parameters = new Dictionary<string, string>() },
            Output("qa-metadata"));
    }

    private static object ThresholdFlow()
    {
        return Draft(
            Camera("line-cam"),
            new { tempId = "op_threshold", operatorType = "Thresholding", parameters = new Dictionary<string, string> { ["Threshold"] = "128" } },
            Output("qa-metadata"));
    }

    private static object EdgeFlow()
    {
        return Draft(
            Camera("line-cam"),
            new { tempId = "op_edge", operatorType = "EdgeDetection", parameters = new Dictionary<string, string>() },
            Output("qa-metadata"));
    }

    private static object ShapeMatchingFlow()
    {
        return Draft(
            Camera("line-cam"),
            new { tempId = "op_shape", operatorType = "ShapeMatching", parameters = new Dictionary<string, string> { ["TemplateId"] = "fixture-template" } },
            Output("qa-metadata"));
    }

    private static object ModelOperatorFlow(string operatorType, string modelId, string modelKind)
    {
        return Draft(
            Camera("line-cam"),
            new { tempId = "op_model", operatorType, parameters = new Dictionary<string, string> { ["ModelId"] = modelId, ["ModelKind"] = modelKind } },
            Output("qa-metadata"));
    }

    private static object OutputChannelFlow(string outputChannelId)
    {
        return Draft(
            Camera("line-cam"),
            new { tempId = "op_template", operatorType = "TemplateMatching", parameters = new Dictionary<string, string> { ["TemplateId"] = "wire-template" } },
            Output(outputChannelId));
    }

    private static object ResultJudgmentFlow(bool includeRule)
    {
        var ruleParameters = includeRule
            ? new Dictionary<string, string> { ["RuleId"] = "metadata-judgment-rule" }
            : new Dictionary<string, string>();
        return Draft(
            Camera("line-cam"),
            new { tempId = "op_template", operatorType = "TemplateMatching", parameters = new Dictionary<string, string> { ["TemplateId"] = "fixture-template" } },
            new { tempId = "op_judgment", operatorType = "ResultJudgment", parameters = ruleParameters },
            Output("qa-metadata"));
    }

    private static object ExternalIntentFlow(string operatorType)
    {
        return Draft(
            Camera("line-cam"),
            new { tempId = "op_external", operatorType, parameters = new Dictionary<string, string>() },
            Output("qa-metadata"));
    }

    private static object ModelPathFlow()
    {
        return Draft(
            Camera("line-cam"),
            new { tempId = "op_model", operatorType = "DeepLearning", parameters = new Dictionary<string, string> { ["ModelPath"] = "external:/blocked-model-token" } },
            Output("qa-metadata"));
    }

    private static object ImageBytesDeniedFlow()
    {
        return Draft(
            new { tempId = "op_cam", operatorType = "ImageAcquisition", parameters = new Dictionary<string, string> { ["SourceType"] = "Camera", ["CameraBindingId"] = "line-cam", ["imageBytes"] = "redacted-image-token" } },
            new { tempId = "op_template", operatorType = "TemplateMatching", parameters = new Dictionary<string, string> { ["TemplateId"] = "wire-template" } },
            Output("qa-metadata"));
    }

    private static object PackagePathDeniedFlow()
    {
        return Draft(
            Camera("line-cam"),
            new { tempId = "op_template", operatorType = "TemplateMatching", parameters = new Dictionary<string, string> { ["TemplateId"] = "wire-template" } },
            new { tempId = "op_output", operatorType = "ResultOutput", parameters = new Dictionary<string, string> { ["OutputChannelId"] = "qa-metadata", ["PackagePath"] = "package-metadata-token" } });
    }

    private static object Camera(string cameraBindingId)
    {
        return new
        {
            tempId = "op_cam",
            operatorType = "ImageAcquisition",
            parameters = new Dictionary<string, string>
            {
                ["SourceType"] = "Camera",
                ["CameraBindingId"] = cameraBindingId
            }
        };
    }

    private static object Output(string outputChannelId)
    {
        return new
        {
            tempId = "op_output",
            operatorType = "ResultOutput",
            parameters = new Dictionary<string, string>
            {
                ["OutputChannelId"] = outputChannelId
            }
        };
    }

    private static object Draft(params object[] operators)
    {
        return new
        {
            operators,
            connections = Array.Empty<object>()
        };
    }
}

public sealed class RuntimePreviewAgentExplanationService
{
    private readonly RuntimePreviewScenarioCorpusService _corpusService;
    private readonly RuntimePreviewRedactedFlowCorpusService? _redactedFlowCorpusService;

    public RuntimePreviewAgentExplanationService(RuntimePreviewScenarioCorpusService corpusService)
    {
        _corpusService = corpusService;
    }

    public RuntimePreviewAgentExplanationService(
        RuntimePreviewScenarioCorpusService corpusService,
        RuntimePreviewRedactedFlowCorpusService redactedFlowCorpusService)
    {
        _corpusService = corpusService;
        _redactedFlowCorpusService = redactedFlowCorpusService;
    }

    public RuntimePreviewAgentExplanationBenchmarkDocument Run()
    {
        var results = _redactedFlowCorpusService != null
            ? _redactedFlowCorpusService.BuildCorpus().Cases.Select(ExplainRedacted).ToList()
            : _corpusService.BuildCorpus().Cases.Select(Explain).ToList();
        return new RuntimePreviewAgentExplanationBenchmarkDocument
        {
            GeneratedAtUtc = DateTimeOffset.UtcNow,
            CaseCount = results.Count,
            PassedCaseCount = results.Count(item => item.Passed),
            Accepted = results.All(item => item.Passed),
            Cases = results,
            MetadataOnly = true,
            RealResourcesTouched = false
        };
    }

    private static RuntimePreviewAgentExplanationResult Explain(RuntimePreviewScenarioCorpusCase item)
    {
        var packageBlocked = !string.Equals(item.ExpectedStatus, RuntimePreviewScenarioEvidenceStatuses.Passed, StringComparison.OrdinalIgnoreCase);
        var readyText = packageBlocked
            ? $"Scenario {item.Scenario} is {item.ExpectedStatus}; workflow editing is allowed but package readiness is blocked."
            : $"Scenario {item.Scenario} is metadata-ready for preview and package review.";
        var missingText = item.ExpectedPendingActions.Count == 0
            ? "No unresolved metadata resource is expected in this corpus case."
            : $"Engineer must resolve: {string.Join(", ", item.ExpectedPendingActions)}.";
        var riskText = packageBlocked
            ? $"Risk: {item.ExpectedRisk}. Do not package or deploy until the metadata issue is cleared."
            : $"Risk: {item.ExpectedRisk}. This is still metadata-only; real pilot gates remain required.";
        var nextAction = packageBlocked
            ? "Confirm the missing or denied metadata handle, then rerun readiness and package precheck."
            : "Review the metadata report and keep the real RuntimePreview pilot gate closed.";
        var passed = readyText.Length > 20 &&
                     missingText.Length > 20 &&
                     riskText.Contains("Risk:", StringComparison.OrdinalIgnoreCase) &&
                     nextAction.Length > 20 &&
                     !readyText.Contains("AI", StringComparison.OrdinalIgnoreCase);

        return new RuntimePreviewAgentExplanationResult
        {
            CaseId = item.CaseId,
            Scenario = item.Scenario,
            Status = item.ExpectedStatus,
            ReadyStateExplanation = readyText,
            MissingResourceExplanation = missingText,
            PackageRiskExplanation = riskText,
            AffectedOperators = ["metadata_operator_trace"],
            BlockedReasons = packageBlocked ? [item.ExpectedRisk] : [],
            ManifestRisk = item.ExpectedRisk,
            NextEngineerAction = nextAction,
            WorkflowDraftAllowed = true,
            PackageBlocked = packageBlocked,
            PackageReviewAllowed = !packageBlocked,
            ReleaseReviewAllowed = !packageBlocked,
            RequiresEngineerApproval = false,
            StationCompatible = !packageBlocked,
            OperatorContractsSatisfied = !packageBlocked,
            OperatorContractExplanation = packageBlocked
                ? $"At least one metadata-only operator contract is incomplete for {item.Scenario}."
                : "Operator contracts are metadata-only and satisfied for this scenario.",
            StationCompatibilityExplanation = packageBlocked
                ? "Target Station compatibility cannot be considered complete until readiness and metadata dependencies pass."
                : "The synthetic Station target is compatible in metadata dry-run.",
            ReleaseDecisionExplanation = packageBlocked
                ? "Release review is blocked because readiness/package evidence is incomplete."
                : "Release review simulator can allow the metadata-only case; no package or deployment is created.",
            WorkflowDraftVsReleaseExplanation = packageBlocked
                ? "workflowDraftAllowed=true means engineers may keep editing; releaseReviewAllowed=false means no release review may proceed yet."
                : "workflowDraftAllowed=true and releaseReviewAllowed=true both hold because metadata checks are clean.",
            ResourceDependencyExplanation = missingText,
            Passed = passed,
            MetadataOnly = true,
            RealResourcesTouched = false
        };
    }

    private static RuntimePreviewAgentExplanationResult ExplainRedacted(RuntimePreviewRedactedFlowCorpusCase item)
    {
        var packageBlocked = !string.Equals(item.ExpectedPackageReadiness, RuntimePreviewScenarioEvidenceStatuses.Passed, StringComparison.OrdinalIgnoreCase);
        var releaseAllowed = string.Equals(item.ExpectedReleaseReviewDecision, "release_allowed", StringComparison.OrdinalIgnoreCase);
        var requiresApproval = string.Equals(item.ExpectedReleaseReviewDecision, "requires_engineer_approval", StringComparison.OrdinalIgnoreCase);
        var stationCompatible = string.Equals(item.ExpectedStationCompatibility, RuntimePreviewScenarioEvidenceStatuses.Passed, StringComparison.OrdinalIgnoreCase);
        var operatorContractsSatisfied = !item.ExpectedBlockedReasons.Any(reason => reason.Contains("operator_contract", StringComparison.OrdinalIgnoreCase)) &&
                                         !item.OperatorContractExpectations.Any(expectation => expectation.Contains("required", StringComparison.OrdinalIgnoreCase) &&
                                                                                              item.ExpectedReleaseReviewDecision == "release_blocked");
        var readyText = packageBlocked
            ? $"{item.WorkflowKind} is {item.ExpectedReadiness}; the workflow draft can be edited, but package review is blocked."
            : $"{item.WorkflowKind} is metadata-ready for manifest dry-run and package review.";
        var missingText = packageBlocked
            ? $"Review metadata dependencies for {string.Join(", ", item.OperatorSummary)}; missing or denied handles block release review."
            : "No unresolved metadata dependency is expected for this redacted flow case.";
        var riskText = packageBlocked
            ? $"Risk: {item.ExpectedManifestRisk}. Do not create a package or send to Station until the dependency trace is clean."
            : requiresApproval
                ? $"Risk: {item.ExpectedManifestRisk}. Release review requires engineer approval before it can be allowed."
                : $"Risk: {item.ExpectedManifestRisk}. Manifest dry-run is allowed, but real package creation remains disabled.";
        var blockedReasons = item.ExpectedBlockedReasons.Count > 0
            ? item.ExpectedBlockedReasons
            : packageBlocked ? [item.ExpectedManifestRisk, item.ExpectedEngineerAction] : [];
        var stationText = stationCompatible
            ? $"Target Station profile {item.StationProfileId} is compatible in metadata dry-run."
            : $"Target Station profile {item.StationProfileId} is not compatible because {string.Join(", ", blockedReasons)}.";
        var contractText = operatorContractsSatisfied
            ? $"Operator contracts are satisfied: {string.Join(", ", item.OperatorContractExpectations.Take(4))}."
            : $"Operator contract validation fails first at: {string.Join(", ", blockedReasons.Where(reason => reason.Contains("operator", StringComparison.OrdinalIgnoreCase)).DefaultIfEmpty(item.ExpectedManifestRisk))}.";
        var releaseText = releaseAllowed
            ? "Release review simulator allows the case because readiness, package, manifest, Station, and operator contracts are clean."
            : requiresApproval
                ? $"Release review requires engineer approval: {string.Join(", ", item.RequiredEngineerApprovals)}."
                : $"Release review is blocked: {string.Join(", ", blockedReasons)}.";
        var workflowVsRelease = !releaseAllowed
            ? "workflowDraftAllowed=true only keeps the draft editable; releaseReviewAllowed=false until the listed contract, dependency, Station, or approval item is resolved."
            : "workflowDraftAllowed=true and releaseReviewAllowed=true because this remains a metadata-only release review simulator path.";
        var passed = readyText.Length > 20 &&
                     missingText.Length > 20 &&
                     riskText.Contains("Risk:", StringComparison.OrdinalIgnoreCase) &&
                     item.ExpectedEngineerAction.Length > 20 &&
                     !string.IsNullOrWhiteSpace(item.ExpectedReadiness) &&
                     !string.Equals(item.ExpectedReadiness, "None", StringComparison.OrdinalIgnoreCase) &&
                     !readyText.Contains("AI", StringComparison.OrdinalIgnoreCase);
        return new RuntimePreviewAgentExplanationResult
        {
            CaseId = item.CaseId,
            Scenario = item.WorkflowKind,
            Status = item.ExpectedReadiness,
            ReadyStateExplanation = readyText,
            MissingResourceExplanation = missingText,
            PackageRiskExplanation = riskText,
            AffectedOperators = item.OperatorSummary,
            BlockedReasons = blockedReasons,
            ManifestRisk = item.ExpectedManifestRisk,
            NextEngineerAction = item.ExpectedEngineerAction,
            WorkflowDraftAllowed = true,
            PackageBlocked = packageBlocked,
            PackageReviewAllowed = !packageBlocked,
            ReleaseReviewAllowed = releaseAllowed,
            RequiresEngineerApproval = requiresApproval,
            StationCompatible = stationCompatible,
            OperatorContractsSatisfied = operatorContractsSatisfied,
            OperatorContractExplanation = contractText,
            StationCompatibilityExplanation = stationText,
            ReleaseDecisionExplanation = releaseText,
            WorkflowDraftVsReleaseExplanation = workflowVsRelease,
            ResourceDependencyExplanation = missingText,
            Passed = passed,
            MetadataOnly = true,
            RealResourcesTouched = false
        };
    }
}

public sealed class RuntimePreviewScenarioEvidenceService
{
    private readonly RuntimePreviewDeployReadinessService _deployReadinessService;

    public RuntimePreviewScenarioEvidenceService(RuntimePreviewDeployReadinessService deployReadinessService)
    {
        _deployReadinessService = deployReadinessService;
    }

    public async Task<RuntimePreviewScenarioEvidenceDocument> RunAsync(
        AppConfig appConfig,
        AiConfigStore? aiConfigStore,
        CancellationToken cancellationToken = default)
    {
        var results = new List<RuntimePreviewScenarioEvidenceResult>();
        foreach (var scenario in CreateCases())
        {
            var request = new RuntimePreviewDeployReadinessRequest
            {
                Config = ScenarioConfig(),
                ToolName = RuntimePreviewResourceAllowlistResolver.MetadataToolName,
                Arguments = RuntimePreviewGovernanceRedactor.ToRedactedElement(new
                {
                    flow = scenario.WorkflowDraft
                }),
                RuntimePreviewConsent = true,
                RequireReplay = true
            };
            var report = await _deployReadinessService.GenerateAsync(
                request,
                appConfig,
                aiConfigStore,
                isAdmin: true,
                developerUiRequested: true,
                cancellationToken);
            results.Add(ToResult(scenario, report));
        }

        return new RuntimePreviewScenarioEvidenceDocument
        {
            GeneratedAtUtc = DateTimeOffset.UtcNow,
            CaseCount = results.Count,
            PassedCaseCount = results.Count(item => item.Passed),
            Accepted = results.All(item => item.Passed),
            Cases = results,
            MetadataOnly = true,
            RealResourcesTouched = false
        };
    }

    public static IReadOnlyList<RuntimePreviewScenarioEvidenceCase> CreateCases()
    {
        return RuntimePreviewScenarioCorpusService.CreateCorpusCases()
            .Select(item => new RuntimePreviewScenarioEvidenceCase
            {
                CaseId = item.CaseId.Replace("RP-SC-", "RP-SE-", StringComparison.OrdinalIgnoreCase),
                Scenario = item.Scenario,
                BusinessSummary = item.BusinessExplanation,
                ExpectedStatus = item.ExpectedStatus,
                ExpectedSignals = item.ExpectedStatus == RuntimePreviewScenarioEvidenceStatuses.Passed
                    ? ["previewReady", "readyForDeployment"]
                    : ["missingResources", "pendingActions", "denyReason"],
                WorkflowDraft = item.WorkflowDraft
            })
            .ToList();
    }

    private static RuntimePreviewScenarioEvidenceResult ToResult(
        RuntimePreviewScenarioEvidenceCase scenario,
        RuntimePreviewDeployReadinessReport report)
    {
        var actualStatus = report.Readiness?.Status switch
        {
            RuntimePreviewPilotReadinessStatuses.Ready when report.ReadyForDeployment => RuntimePreviewScenarioEvidenceStatuses.Passed,
            RuntimePreviewPilotReadinessStatuses.Denied => RuntimePreviewScenarioEvidenceStatuses.Denied,
            _ => RuntimePreviewScenarioEvidenceStatuses.NotReady
        };
        var missingResources = report.Readiness?.MissingResources?.ToList() ?? [];
        return new RuntimePreviewScenarioEvidenceResult
        {
            CaseId = scenario.CaseId,
            Scenario = scenario.Scenario,
            ExpectedStatus = scenario.ExpectedStatus,
            ActualStatus = actualStatus,
            Passed = string.Equals(actualStatus, scenario.ExpectedStatus, StringComparison.OrdinalIgnoreCase),
            PreviewReady = report.PreviewReady,
            ReadyForDeployment = report.ReadyForDeployment,
            MissingResources = missingResources,
            PendingActions = report.PendingActions,
            DenyReason = report.Readiness?.ResourceTrace.ReasonCode,
            PrecheckRisk = report.DeploymentBlocked ? "deployment_blocked_metadata_only" : null,
            BusinessExplanation = scenario.BusinessSummary,
            WorkflowDraftHash = report.WorkflowDraftHash,
            MetadataOnly = true,
            RealResourcesTouched = false
        };
    }

    private static RuntimePreviewPilotConfig ScenarioConfig()
    {
        var config = new RuntimePreviewPilotConfig
        {
            Enabled = true,
            Mode = RuntimePreviewPilotConfig.ModeMetadataOnly,
            AllowedCameraBindingIds = ["line-cam"],
            AllowedTemplateIds = ["wire-template", "terminal-color-template", "fixture-template"],
            AllowedModelIds = ["remote-control-model"],
            FallbackToOffline = true,
            DenyExternalPath = true,
            DenyImageBytes = true
        };
        config.Normalize();
        return config;
    }

    private static RuntimePreviewScenarioEvidenceCase Case(
        string caseId,
        string scenario,
        string summary,
        string expectedStatus,
        object workflowDraft)
    {
        return new RuntimePreviewScenarioEvidenceCase
        {
            CaseId = caseId,
            Scenario = scenario,
            BusinessSummary = summary,
            ExpectedStatus = expectedStatus,
            ExpectedSignals = expectedStatus == RuntimePreviewScenarioEvidenceStatuses.Passed
                ? ["previewReady", "readyForDeployment"]
                : ["missingResources", "pendingActions", "denyReason"],
            WorkflowDraft = RuntimePreviewGovernanceRedactor.ToRedactedElement(workflowDraft)
        };
    }

    private static object Flow(string cameraBindingId, string? templateId = null, string? templatePath = null)
    {
        var parameters = new Dictionary<string, string>();
        if (!string.IsNullOrWhiteSpace(templateId))
        {
            parameters["TemplateId"] = templateId;
        }

        if (!string.IsNullOrWhiteSpace(templatePath))
        {
            parameters["TemplatePath"] = templatePath;
        }

        return Draft(
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
                parameters
            },
            Output());
    }

    private static object HoleDistanceFlow()
    {
        return Draft(
            Camera("line-cam"),
            new { tempId = "op_circle_a", operatorType = "CircleMeasurement", parameters = new Dictionary<string, string> { ["Roi"] = "hole-a" } },
            new { tempId = "op_circle_b", operatorType = "CircleMeasurement", parameters = new Dictionary<string, string> { ["Roi"] = "hole-b" } },
            new { tempId = "op_distance", operatorType = "MeasureDistance", parameters = new Dictionary<string, string> { ["Unit"] = "mm" } },
            Output());
    }

    private static object ModelFlow(string cameraBindingId, string modelId)
    {
        return Draft(
            Camera(cameraBindingId),
            new { tempId = "op_model", operatorType = "DeepLearning", parameters = new Dictionary<string, string> { ["ModelId"] = modelId } },
            Output());
    }

    private static object PlcFlow()
    {
        return Draft(
            Camera("line-cam"),
            new { tempId = "op_template", operatorType = "TemplateMatching", parameters = new Dictionary<string, string> { ["TemplateId"] = "wire-template" } },
            new { tempId = "op_output", operatorType = "ResultOutput", parameters = new Dictionary<string, string> { ["Channel"] = "plc", ["PlcAddress"] = "plc-output-token" } });
    }

    private static object Camera(string cameraBindingId)
    {
        return new
        {
            tempId = "op_cam",
            operatorType = "ImageAcquisition",
            parameters = new Dictionary<string, string>
            {
                ["SourceType"] = "Camera",
                ["CameraBindingId"] = cameraBindingId
            }
        };
    }

    private static object Output()
    {
        return new
        {
            tempId = "op_output",
            operatorType = "ResultOutput",
            parameters = new Dictionary<string, string>
            {
                ["OutputChannelId"] = "qa-metadata"
            }
        };
    }

    private static object Draft(params object[] operators)
    {
        return new
        {
            operators,
            connections = Array.Empty<object>()
        };
    }
}

public sealed class RuntimePreviewSimulateMetadataSessionTool : VisionAgentToolBase
{
    public const string ToolName = "runtime_preview_simulate_metadata_session";

    private readonly RuntimePreviewSimulatedExecutionHarness _harness;

    public RuntimePreviewSimulateMetadataSessionTool()
        : this(CreateDefaultHarness())
    {
    }

    public RuntimePreviewSimulateMetadataSessionTool(RuntimePreviewSimulatedExecutionHarness harness)
    {
        _harness = harness;
    }

    public override string Name => ToolName;
    public override string DisplayName => "RuntimePreview metadata session simulation";
    public override string Description => "Runs metadata-only RuntimePreview governance session, broker, audit, and report simulation.";
    public override string Category => "runtime_preview";
    public override VisionAgentToolPermission Permission => VisionAgentToolPermission.Simulation;
    public override JsonElement ParametersSchema { get; } = Schema("""
        {
          "type": "object",
          "properties": {
            "flow": { "type": ["object", "string"] },
            "workflowDraft": { "type": "object" },
            "config": { "type": "object" },
            "runtimePreviewConsent": { "type": "boolean" }
          }
        }
        """);

    public override Task<VisionAgentToolResult> ExecuteAsync(
        VisionAgentToolContext context,
        JsonElement arguments,
        CancellationToken cancellationToken)
    {
        var pilotConfig = ReadPilotConfig(arguments) ?? context.RuntimePreviewPilot.CloneNormalized();
        var executionArguments = RemoveConfigArgument(arguments);
        var appConfig = new AppConfig();
        appConfig.Runtime.RuntimePreviewPilot = pilotConfig;
        var report = _harness.RunEndToEnd(
            new RuntimePreviewSessionCreateRequest
            {
                Config = pilotConfig,
                ToolName = RuntimePreviewResourceAllowlistResolver.MetadataToolName,
                Arguments = executionArguments,
                RuntimePreviewConsent = context.RuntimePreviewConsent
            },
            appConfig,
            aiConfigStore: null,
            isAdmin: true,
            developerUiRequested: true);
        return Task.FromResult(VisionAgentToolResult.Ok(report));
    }

    private static JsonElement RemoveConfigArgument(JsonElement arguments)
    {
        if (arguments.ValueKind != JsonValueKind.Object ||
            !arguments.TryGetProperty("config", out _))
        {
            return arguments.Clone();
        }

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            foreach (var property in arguments.EnumerateObject())
            {
                if (string.Equals(property.Name, "config", StringComparison.OrdinalIgnoreCase))
                    continue;

                property.WriteTo(writer);
            }

            writer.WriteEndObject();
        }

        using var document = JsonDocument.Parse(stream.ToArray());
        return document.RootElement.Clone();
    }

    private static RuntimePreviewPilotConfig? ReadPilotConfig(JsonElement arguments)
    {
        if (arguments.ValueKind != JsonValueKind.Object ||
            !arguments.TryGetProperty("config", out var value) ||
            value.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        try
        {
            return value.Deserialize<RuntimePreviewPilotConfig>(new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static RuntimePreviewSimulatedExecutionHarness CreateDefaultHarness()
    {
        var catalog = new RuntimePreviewPilotResourceCatalog();
        return new RuntimePreviewSimulatedExecutionHarness(
            new RuntimePreviewSessionStore(),
            new RuntimePreviewResourceBroker(catalog),
            new RuntimePreviewPilotReadinessGate(new RuntimePreviewResourceAllowlistResolver()),
            new RuntimePreviewPermissionBroker(),
            new RuntimePreviewAuditTrail(),
            new RuntimePreviewReportArchive());
    }
}

internal static class RuntimePreviewGovernanceHashes
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public static string HashObject(object? value)
    {
        return HashString(JsonSerializer.Serialize(value, JsonOptions));
    }

    public static string HashJsonElement(JsonElement? value)
    {
        return value is { } element
            ? HashString(element.GetRawText())
            : HashString("{}");
    }

    public static string HashString(string? value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value ?? string.Empty));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}

internal static partial class RuntimePreviewGovernanceRedactor
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public static JsonElement ToRedactedElement(object? payload)
    {
        var raw = JsonSerializer.Serialize(payload ?? new { }, JsonOptions);
        using var inputDocument = JsonDocument.Parse(raw);
        var redacted = JsonSerializer.Serialize(RedactElement(inputDocument.RootElement, null), JsonOptions);
        using var document = JsonDocument.Parse(redacted);
        return document.RootElement.Clone();
    }

    public static string RedactScalar(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : RedactText(value.Trim());
    }

    public static void ThrowIfUnsafeStorageText(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        var forbidden = StorageForbiddenRegexes()
            .FirstOrDefault(regex => regex.IsMatch(value));
        if (forbidden != null)
        {
            throw new InvalidOperationException("RuntimePreview governance storage rejected an unsafe payload fragment.");
        }
    }

    private static string RedactText(string value)
    {
        var redacted = AiSecretSanitizer.Redact(value);
        redacted = IpLikeRegex().Replace(redacted, "<redacted>");
        redacted = WindowsPathRegex().Replace(redacted, "<redacted>");
        redacted = UnixPathRegex().Replace(redacted, "<redacted>");
        redacted = ExternalResourceTokenRegex().Replace(redacted, "<redacted>");
        redacted = Base64ImageRegex().Replace(redacted, "<redacted>");
        redacted = StationPlcRegex().Replace(redacted, match =>
            $"{match.Groups["prefix"].Value}\"<redacted>\"");
        return redacted;
    }

    private static object? RedactElement(JsonElement element, string? propertyName)
    {
        if (IsSensitiveProperty(propertyName))
            return "<redacted>";

        return element.ValueKind switch
        {
            JsonValueKind.Object => RedactObject(element),
            JsonValueKind.Array => element.EnumerateArray()
                .Select(item => RedactElement(item, propertyName))
                .ToArray(),
            JsonValueKind.String => RedactScalar(element.GetString()),
            JsonValueKind.Number => element.TryGetInt64(out var longValue)
                ? longValue
                : element.TryGetDouble(out var doubleValue)
                    ? doubleValue
                    : element.GetRawText(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null
        };
    }

    private static Dictionary<string, object?> RedactObject(JsonElement element)
    {
        var redacted = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in element.EnumerateObject())
        {
            redacted[property.Name] = RedactElement(property.Value, property.Name);
        }

        return redacted;
    }

    private static bool IsSensitiveProperty(string? propertyName)
    {
        if (string.IsNullOrWhiteSpace(propertyName))
            return false;

        return propertyName.Contains("apiKey", StringComparison.OrdinalIgnoreCase)
            || propertyName.Contains("authorization", StringComparison.OrdinalIgnoreCase)
            || propertyName.Contains("token", StringComparison.OrdinalIgnoreCase)
            || propertyName.Contains("station", StringComparison.OrdinalIgnoreCase)
            || propertyName.Contains("plc", StringComparison.OrdinalIgnoreCase);
    }

    [GeneratedRegex(@"(?<!\d)(?:\d{1,3}\.){3}\d{1,3}(?::\d+)?(?:/[A-Za-z0-9._~!$&'()*+,;=:@%-]+)?", RegexOptions.IgnoreCase)]
    private static partial Regex IpLikeRegex();

    [GeneratedRegex(@"[A-Za-z]:\\[^""'\s,;{}]+", RegexOptions.IgnoreCase)]
    private static partial Regex WindowsPathRegex();

    [GeneratedRegex(@"(?<!https?:)/(?:mnt|home|var|tmp|opt|usr|data|models|images|station|plc)/[^""'\s,;{}]+", RegexOptions.IgnoreCase)]
    private static partial Regex UnixPathRegex();

    [GeneratedRegex(@"\b[A-Za-z][A-Za-z0-9_-]*:/[^""'\s,;{}]+", RegexOptions.IgnoreCase)]
    private static partial Regex ExternalResourceTokenRegex();

    [GeneratedRegex(@"data:image/[a-z0-9.+-]+;base64,[A-Za-z0-9+/=]+", RegexOptions.IgnoreCase)]
    private static partial Regex Base64ImageRegex();

    [GeneratedRegex(@"(?<prefix>[""']?(?:stationId|stationAddress|plcAddress|plc|PLCParameters)[""']?\s*[:=]\s*)[""']?[^""'\s,;{}]+[""']?", RegexOptions.IgnoreCase)]
    private static partial Regex StationPlcRegex();

    private static IReadOnlyList<Regex> StorageForbiddenRegexes()
    {
        return
        [
            new Regex(@"\bBearer\s+[A-Za-z0-9._~+/=-]{8,}", RegexOptions.IgnoreCase),
            new Regex(@"\b(?:authorization|x-api-key|api-key|apiKey|api_key)\b\s*[:=]\s*[""']?(?!\\u003c|<redacted)[^""'\s,;{}]+", RegexOptions.IgnoreCase),
            new Regex(@"https?://(?:[^/\s@]+@)?(?:\d{1,3}(?:\.\d{1,3}){3}|[A-Za-z0-9.-]+\.[A-Za-z]{2,})(?::\d+)?/[^\s`""']*", RegexOptions.IgnoreCase),
            new Regex(@"(?<!\d)(?:\d{1,3}\.){3}\d{1,3}(?::\d+)?", RegexOptions.IgnoreCase),
            new Regex(@"[A-Za-z]:\\[^""'\s,;{}]+", RegexOptions.IgnoreCase),
            new Regex(@"(?<!https?:)/(?:mnt|home|var|tmp|opt|usr|data|models|images|station|plc)/[^""'\s,;{}]+", RegexOptions.IgnoreCase),
            new Regex(@"\b[A-Za-z][A-Za-z0-9_-]*:/[^""'\s,;{}]+", RegexOptions.IgnoreCase),
            new Regex(@"data:image/[a-z0-9.+-]+;base64,[A-Za-z0-9+/=]+", RegexOptions.IgnoreCase),
            new Regex(@"\b[^""'\s,;{}]*\.(?:cvpkg|zip)\b", RegexOptions.IgnoreCase),
            new Regex(@"\bDB\d+\.(?:DBX|DBB|DBW|DBD)\d+(?:\.\d+)?\b", RegexOptions.IgnoreCase)
        ];
    }
}
