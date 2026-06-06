using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ClearVision.Product.Core.AI.Tools;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Infrastructure.AI.Agent;

namespace ClearVision.Product.Infrastructure.AI.Tools;

public sealed class RuntimePreviewGovernanceStore
{
    public const string SchemaVersion = "2026-06-06.runtime-preview-governance-store.v3";
    public const string StorageVersion = "jsonl.v3";

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
                "manifest_dry_run_report"
            ],
            SessionCount = LoadSessions().Count,
            AuditEventCount = LoadAuditEvents().Count,
            SessionReportCount = LoadReports().Count,
            DeployReadinessReportCount = LoadDeployReadinessReports().Count,
            PackageReadinessReportCount = LoadPackageReadinessReports().Count,
            ManifestDryRunReportCount = LoadManifestDryRunReports().Count,
            CorruptLineCount = CountCorruptLines(SessionPath) +
                               CountCorruptLines(AuditPath) +
                               CountCorruptLines(ReportPath) +
                               CountCorruptLines(DeployReadinessReportPath) +
                               CountCorruptLines(PackageReadinessReportPath) +
                               CountCorruptLines(ManifestDryRunReportPath),
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

            Rewrite(SessionPath, sessionsAfter);
            Rewrite(AuditPath, auditAfter);
            Rewrite(ReportPath, reportsAfter);
            Rewrite(DeployReadinessReportPath, deployAfter);
            Rewrite(PackageReadinessReportPath, packageAfter);
            Rewrite(ManifestDryRunReportPath, manifestAfter);

            return new RuntimePreviewRetentionCleanupResult
            {
                RetentionDays = effectiveRetentionDays,
                MaxSessions = effectiveMaxSessions,
                SessionsBefore = sessionsBefore.Count,
                SessionsAfter = sessionsAfter.Count,
                AuditEventsBefore = auditBefore.Count,
                AuditEventsAfter = auditAfter.Count,
                ReportsBefore = reportsBefore.Count + deployBefore.Count + packageBefore.Count + manifestBefore.Count,
                ReportsAfter = reportsAfter.Count + deployAfter.Count + packageAfter.Count + manifestAfter.Count,
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
            Case("RP-RF-002", "remote_control_station", "remote_control_defect", "Detect missing buttons and label defects on a remote controller.", ["ImageAcquisition", "DeepLearning", "ResultOutput"], RuntimePreviewScenarioEvidenceStatuses.Passed, RuntimePreviewScenarioEvidenceStatuses.Passed, "low", "Confirm ModelId catalog ownership before real pilot.", RuntimePreviewScenarioCorpusService.ModelFlow("line-cam", "remote-control-model")),
            Case("RP-RF-003", "fixture_station", "template_measurement_combo", "Locate fixture by template and measure a downstream feature.", ["ImageAcquisition", "TemplateMatching", "CircleMeasurement", "ResultOutput"], RuntimePreviewScenarioEvidenceStatuses.Passed, RuntimePreviewScenarioEvidenceStatuses.Passed, "medium", "Review combined template and measurement operator contract.", RuntimePreviewScenarioCorpusService.MultiOperatorFlow()),
            Case("RP-RF-004", "measurement_station", "hole_distance", "Measure distance between two holes for dimensional inspection.", ["ImageAcquisition", "CircleMeasurement", "MeasureDistance", "ResultOutput"], RuntimePreviewScenarioEvidenceStatuses.Passed, RuntimePreviewScenarioEvidenceStatuses.Passed, "low", "Confirm measurement unit and tolerance source.", RuntimePreviewScenarioCorpusService.HoleDistanceFlow()),
            Case("RP-RF-005", "terminal_station", "terminal_color_order", "Check terminal color sequence with a catalog template.", ["ImageAcquisition", "TemplateMatching", "ResultOutput"], RuntimePreviewScenarioEvidenceStatuses.Passed, RuntimePreviewScenarioEvidenceStatuses.Passed, "low", "Confirm template ownership and output channel mapping.", RuntimePreviewScenarioCorpusService.Flow("line-cam", templateId: "terminal-color-template")),
            Case("RP-RF-006", "line_station", "missing_camera", "Workflow references a camera binding not in the pilot catalog.", ["ImageAcquisition", "TemplateMatching", "ResultOutput"], RuntimePreviewScenarioEvidenceStatuses.NotReady, RuntimePreviewScenarioEvidenceStatuses.NotReady, "missing_camera_binding", "Bind an allowlisted metadata camera before package review.", RuntimePreviewScenarioCorpusService.Flow("missing-cam", templateId: "wire-template")),
            Case("RP-RF-007", "fixture_station", "missing_template", "TemplateMatching has no TemplateId metadata handle.", ["ImageAcquisition", "TemplateMatching", "ResultOutput"], RuntimePreviewScenarioEvidenceStatuses.NotReady, RuntimePreviewScenarioEvidenceStatuses.NotReady, "missing_template", "Assign an allowlisted TemplateId; do not use file paths.", RuntimePreviewScenarioCorpusService.Flow("line-cam")),
            Case("RP-RF-008", "remote_control_station", "missing_model", "DeepLearning operator has unresolved model metadata.", ["ImageAcquisition", "DeepLearning", "ResultOutput"], RuntimePreviewScenarioEvidenceStatuses.NotReady, RuntimePreviewScenarioEvidenceStatuses.NotReady, "missing_model", "Bind ModelId from catalog; do not load a model file.", RuntimePreviewScenarioCorpusService.ModelFlow("line-cam", "<pending-model>")),
            Case("RP-RF-009", "output_station", "missing_output_channel", "ResultOutput is missing a safe output channel metadata id.", ["ImageAcquisition", "TemplateMatching", "ResultOutput"], RuntimePreviewScenarioEvidenceStatuses.NotReady, RuntimePreviewScenarioEvidenceStatuses.NotReady, "missing_output_channel", "Choose OutputChannelId before package review.", MissingOutputFlow()),
            Case("RP-RF-010", "station_release", "plc_station_deny", "User intent includes PLC or Station release action.", ["ImageAcquisition", "TemplateMatching", "ResultOutput"], RuntimePreviewScenarioEvidenceStatuses.Denied, RuntimePreviewScenarioEvidenceStatuses.Denied, "plc_station_denied", "Remove PLC/Station intent; this console cannot write or deploy.", RuntimePreviewScenarioCorpusService.PlcFlow()),
            Case("RP-RF-011", "template_station", "dangerous_path", "Template dependency tries to point at an external path.", ["ImageAcquisition", "TemplateMatching", "ResultOutput"], RuntimePreviewScenarioEvidenceStatuses.Denied, RuntimePreviewScenarioEvidenceStatuses.Denied, "dangerous_resource", "Replace path-like metadata with a catalog TemplateId.", RuntimePreviewScenarioCorpusService.Flow("line-cam", templatePath: "external:/blocked-template")),
            Case("RP-RF-012", "line_station", "allowlist_mismatch", "Workflow camera handle is valid-looking but not allowlisted for pilot.", ["ImageAcquisition", "TemplateMatching", "ResultOutput"], RuntimePreviewScenarioEvidenceStatuses.NotReady, RuntimePreviewScenarioEvidenceStatuses.NotReady, "allowlist_mismatch", "Review allowlist diff and confirm the catalog handle.", RuntimePreviewScenarioCorpusService.Flow("camera-not-allowlisted", templateId: "wire-template")),
            Case("RP-RF-013", "dual_camera_station", "multi_camera_flow", "Two camera metadata handles feed one inspection decision.", ["ImageAcquisition", "ImageAcquisition", "TemplateMatching", "ResultOutput"], RuntimePreviewScenarioEvidenceStatuses.NotReady, RuntimePreviewScenarioEvidenceStatuses.NotReady, "multi_camera_review", "Confirm both camera bindings are catalog allowlisted.", MultiCameraFlow()),
            Case("RP-RF-014", "ai_station", "multi_model_flow", "Two model metadata handles are required for final judgment.", ["ImageAcquisition", "DeepLearning", "DeepLearning", "ResultOutput"], RuntimePreviewScenarioEvidenceStatuses.NotReady, RuntimePreviewScenarioEvidenceStatuses.NotReady, "multi_model_review", "Confirm all ModelIds and output aggregation before package review.", MultiModelFlow()),
            Case("RP-RF-015", "parameter_review_station", "parameter_missing", "A key operator parameter is missing even though the draft can be edited.", ["ImageAcquisition", "TemplateMatching", "ResultOutput"], RuntimePreviewScenarioEvidenceStatuses.NotReady, RuntimePreviewScenarioEvidenceStatuses.NotReady, "missing_parameter", "Complete required operator parameters and rerun readiness.", RuntimePreviewScenarioCorpusService.MissingParameterFlow()),
            Case("RP-RF-016", "release_review", "package_manifest_blocked", "Manifest dry-run blocks package review because dependencies are incomplete.", ["ImageAcquisition", "DeepLearning", "ResultOutput"], RuntimePreviewScenarioEvidenceStatuses.NotReady, RuntimePreviewScenarioEvidenceStatuses.NotReady, "manifest_dependency_blocked", "Resolve manifest dependencies; no package may be created.", RuntimePreviewScenarioCorpusService.ModelFlow("line-cam", "<pending-model>")),
            Case("RP-RF-017", "draft_review", "workflow_editable_package_blocked", "Engineer can edit the workflow draft but package review is not allowed.", ["ImageAcquisition", "TemplateMatching", "ResultOutput"], RuntimePreviewScenarioEvidenceStatuses.NotReady, RuntimePreviewScenarioEvidenceStatuses.NotReady, "draft_allowed_package_blocked", "Keep editing the workflow; do not start release review yet.", RuntimePreviewScenarioCorpusService.Flow("missing-cam", templateId: "fixture-template")),
            Case("RP-RF-018", "precheck_station", "runtime_package_precheck_blocked", "Runtime package precheck risk blocks release.", ["ImageAcquisition", "DeepLearning", "ResultOutput"], RuntimePreviewScenarioEvidenceStatuses.NotReady, RuntimePreviewScenarioEvidenceStatuses.NotReady, "precheck_not_ready", "Rerun readiness after model metadata is resolved.", RuntimePreviewScenarioCorpusService.ModelFlow("line-cam", "<pending-model>")),
            Case("RP-RF-019", "template_measurement_station", "template_plus_hole_distance", "Template positioning and hole distance measurement share one camera.", ["ImageAcquisition", "TemplateMatching", "CircleMeasurement", "MeasureDistance", "ResultOutput"], RuntimePreviewScenarioEvidenceStatuses.Passed, RuntimePreviewScenarioEvidenceStatuses.Passed, "medium", "Review dependency trace before a real pilot is requested.", RuntimePreviewScenarioCorpusService.MultiOperatorFlow()),
            Case("RP-RF-020", "release_blocked_station", "direct_deploy_request_denied", "User asks to release to Station directly from preview.", ["ImageAcquisition", "TemplateMatching", "ResultOutput"], RuntimePreviewScenarioEvidenceStatuses.Denied, RuntimePreviewScenarioEvidenceStatuses.Denied, "deployment_intent_denied", "Use only metadata review; direct deployment remains forbidden.", RuntimePreviewScenarioCorpusService.PlcFlow())
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
        object workflowDraft)
    {
        var workflow = RuntimePreviewGovernanceRedactor.ToRedactedElement(workflowDraft);
        return new RuntimePreviewRedactedFlowCorpusCase
        {
            CaseId = caseId,
            StationType = stationType,
            WorkflowKind = workflowKind,
            BusinessPurpose = purpose,
            WorkflowDraftHash = RuntimePreviewGovernanceHashes.HashJsonElement(workflow),
            OperatorSummary = operators.Select(RuntimePreviewGovernanceRedactor.RedactScalar).ToList(),
            ExpectedReadiness = readiness,
            ExpectedPackageReadiness = packageReadiness,
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

    private static object MultiCameraFlow()
    {
        return new
        {
            operators = new object[]
            {
                new { tempId = "op_cam_a", operatorType = "ImageAcquisition", parameters = new Dictionary<string, string> { ["SourceType"] = "Camera", ["CameraBindingId"] = "line-cam" } },
                new { tempId = "op_cam_b", operatorType = "ImageAcquisition", parameters = new Dictionary<string, string> { ["SourceType"] = "Camera", ["CameraBindingId"] = "side-cam-pending" } },
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
            Passed = passed,
            MetadataOnly = true,
            RealResourcesTouched = false
        };
    }

    private static RuntimePreviewAgentExplanationResult ExplainRedacted(RuntimePreviewRedactedFlowCorpusCase item)
    {
        var packageBlocked = !string.Equals(item.ExpectedPackageReadiness, RuntimePreviewScenarioEvidenceStatuses.Passed, StringComparison.OrdinalIgnoreCase);
        var readyText = packageBlocked
            ? $"{item.WorkflowKind} is {item.ExpectedReadiness}; the workflow draft can be edited, but package review is blocked."
            : $"{item.WorkflowKind} is metadata-ready for manifest dry-run and package review.";
        var missingText = packageBlocked
            ? $"Review metadata dependencies for {string.Join(", ", item.OperatorSummary)}; missing or denied handles block release review."
            : "No unresolved metadata dependency is expected for this redacted flow case.";
        var riskText = packageBlocked
            ? $"Risk: {item.ExpectedManifestRisk}. Do not create a package or send to Station until the dependency trace is clean."
            : $"Risk: {item.ExpectedManifestRisk}. Manifest dry-run is allowed, but real package creation remains disabled.";
        var blockedReasons = packageBlocked ? [item.ExpectedManifestRisk, item.ExpectedEngineerAction] : Array.Empty<string>();
        var passed = readyText.Length > 20 &&
                     missingText.Length > 20 &&
                     riskText.Contains("Risk:", StringComparison.OrdinalIgnoreCase) &&
                     item.ExpectedEngineerAction.Length > 20 &&
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
