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

            Rewrite(SessionPath, sessionsAfter);
            Rewrite(AuditPath, auditAfter);
            Rewrite(ReportPath, reportsAfter);
            Rewrite(DeployReadinessReportPath, deployAfter);

            return new RuntimePreviewRetentionCleanupResult
            {
                RetentionDays = effectiveRetentionDays,
                MaxSessions = effectiveMaxSessions,
                SessionsBefore = sessionsBefore.Count,
                SessionsAfter = sessionsAfter.Count,
                AuditEventsBefore = auditBefore.Count,
                AuditEventsAfter = auditAfter.Count,
                ReportsBefore = reportsBefore.Count + deployBefore.Count,
                ReportsAfter = reportsAfter.Count + deployAfter.Count,
                MetadataOnly = true,
                RealResourcesTouched = false
            };
        }
    }

    private string SessionPath => Path.Combine(_directoryPath, "runtime_preview_sessions.jsonl");

    private string AuditPath => Path.Combine(_directoryPath, "runtime_preview_audit.jsonl");

    private string ReportPath => Path.Combine(_directoryPath, "runtime_preview_reports.jsonl");

    private string DeployReadinessReportPath => Path.Combine(_directoryPath, "runtime_preview_deploy_readiness_reports.jsonl");

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
        return
        [
            Case("RP-SE-001", "wire_sequence", "Line sequence inspection with allowlisted camera/template metadata.", RuntimePreviewScenarioEvidenceStatuses.Passed, Flow("line-cam", templateId: "wire-template")),
            Case("RP-SE-002", "template_matching", "Template matching localization with catalog TemplateId.", RuntimePreviewScenarioEvidenceStatuses.Passed, Flow("line-cam", templateId: "fixture-template")),
            Case("RP-SE-003", "hole_distance", "Hole center distance measurement using metadata camera handle.", RuntimePreviewScenarioEvidenceStatuses.Passed, HoleDistanceFlow()),
            Case("RP-SE-004", "remote_control_detection", "Remote controller defect detection using ModelId metadata.", RuntimePreviewScenarioEvidenceStatuses.Passed, ModelFlow("line-cam", "remote-control-model")),
            Case("RP-SE-005", "missing_resource", "Camera resource is missing from allowlist; workflow draft stays editable.", RuntimePreviewScenarioEvidenceStatuses.NotReady, Flow("missing-cam", templateId: "wire-template")),
            Case("RP-SE-006", "dangerous_path", "TemplatePath attempts to reference an external resource and must be denied.", RuntimePreviewScenarioEvidenceStatuses.Denied, Flow("line-cam", templatePath: "external:/blocked-template")),
            Case("RP-SE-007", "station_plc_deny", "PLC output request must be denied before any write or Station access.", RuntimePreviewScenarioEvidenceStatuses.Denied, PlcFlow()),
            Case("RP-SE-008", "precheck_not_ready", "Runtime package precheck remains blocked when ModelId metadata is missing.", RuntimePreviewScenarioEvidenceStatuses.NotReady, ModelFlow("line-cam", "<pending-model>"))
        ];
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
            AllowedTemplateIds = ["wire-template", "fixture-template"],
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
            new Regex(@"\bDB\d+\.(?:DBX|DBB|DBW|DBD)\d+(?:\.\d+)?\b", RegexOptions.IgnoreCase)
        ];
    }
}
