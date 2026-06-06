using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ClearVision.Product.Core.AI.Tools;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Infrastructure.AI.Agent;

namespace ClearVision.Product.Infrastructure.AI.Tools;

public sealed class RuntimePreviewSessionStore
{
    private readonly ConcurrentDictionary<string, RuntimePreviewSession> _sessions = new(StringComparer.OrdinalIgnoreCase);

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
        return _sessions.AddOrUpdate(
            sessionId,
            _ => throw new InvalidOperationException($"RuntimePreview session '{sessionId}' was not found."),
            (_, current) => update(current) with { UpdatedAtUtc = DateTimeOffset.UtcNow });
    }
}

public sealed class RuntimePreviewAuditTrail
{
    private readonly ConcurrentDictionary<string, RuntimePreviewAuditEvent> _events = new(StringComparer.OrdinalIgnoreCase);

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

    public RuntimePreviewSessionReport Save(RuntimePreviewSessionReport report)
    {
        _reports[report.ReportId] = report;
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

    private static string RedactText(string value)
    {
        var redacted = AiSecretSanitizer.Redact(value);
        redacted = IpLikeRegex().Replace(redacted, "<redacted>");
        redacted = WindowsPathRegex().Replace(redacted, "<redacted>");
        redacted = UnixPathRegex().Replace(redacted, "<redacted>");
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

    [GeneratedRegex(@"data:image/[a-z0-9.+-]+;base64,[A-Za-z0-9+/=]+", RegexOptions.IgnoreCase)]
    private static partial Regex Base64ImageRegex();

    [GeneratedRegex(@"(?<prefix>[""']?(?:stationId|stationAddress|plcAddress|plc|PLCParameters)[""']?\s*[:=]\s*)[""']?[^""'\s,;{}]+[""']?", RegexOptions.IgnoreCase)]
    private static partial Regex StationPlcRegex();
}
