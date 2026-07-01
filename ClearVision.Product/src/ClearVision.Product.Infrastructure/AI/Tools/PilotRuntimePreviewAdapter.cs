using ClearVision.Product.Core.AI.Tools;
using ClearVision.Product.Infrastructure.AI.Agent;

namespace ClearVision.Product.Infrastructure.AI.Tools;

public sealed class PilotRuntimePreviewAdapter : IRuntimePreviewAdapter
{
    public const string AdapterName = "pilot_runtime_preview";

    private readonly RuntimePreviewPilotResourceCatalog _resourceCatalog;
    private readonly RuntimePreviewPilotReadinessGate _readinessGate;
    private readonly OfflineRuntimePreviewAdapter _offlineAdapter;

    public PilotRuntimePreviewAdapter(
        RuntimePreviewPilotResourceCatalog resourceCatalog,
        RuntimePreviewPilotReadinessGate readinessGate,
        OfflineRuntimePreviewAdapter offlineAdapter)
    {
        _resourceCatalog = resourceCatalog;
        _readinessGate = readinessGate;
        _offlineAdapter = offlineAdapter;
    }

    public string Name => AdapterName;

    public IReadOnlySet<string> SupportedToolNames { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            RuntimePreviewPermissionGate.CaptureToolName,
            RuntimePreviewPermissionGate.ReplayToolName
        };

    public async Task<RuntimePreviewResult> ExecuteAsync(
        RuntimePreviewRequest request,
        CancellationToken cancellationToken)
    {
        var config = request.PilotConfig;
        config.Normalize();

        if (!config.Enabled)
        {
            return await ExecuteOfflineFallbackAsync(
                request,
                RuntimePreviewResourceTrace.NotEvaluated() with
                {
                    Allowed = false,
                    ReasonCode = "runtime_preview_pilot_disabled",
                    ResourceType = "pilot"
                },
                readiness: null,
                "runtime_preview_pilot_disabled",
                "RuntimePreview Pilot is disabled; Offline adapter was used.",
                cancellationToken);
        }

        try
        {
            var catalog = _resourceCatalog.Build(config, null, null, ExtractWorkflowDraft(request));
            var readiness = _readinessGate.Evaluate(
                config,
                catalog,
                request.ToolName,
                request.Arguments,
                request.Context);
            if (!readiness.CanRunMetadataPilot)
            {
                return string.Equals(readiness.Status, RuntimePreviewPilotReadinessStatuses.Denied, StringComparison.OrdinalIgnoreCase)
                    ? DenyDangerousRequest(request, readiness)
                    : await NotReadyWithFallbackAsync(request, readiness, cancellationToken);
            }

            var offline = await ExecuteOfflineAsync(request, cancellationToken);
            return WrapPilotMetadataResult(
                offline,
                request,
                readiness,
                PermissionDecision(
                    request,
                    allowed: offline.Success,
                    reasonCode: offline.Success
                        ? "runtime_preview_pilot_metadata_only_allowed"
                        : offline.ErrorCode ?? "runtime_preview_pilot_offline_metadata_not_ready",
                    reason: offline.Success
                        ? "RuntimePreview Pilot returned metadata-only preview through the offline skeleton."
                        : "RuntimePreview Pilot metadata preview was not ready."));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return await ExecuteOfflineFallbackAsync(
                request,
                RuntimePreviewResourceTrace.NotEvaluated() with
                {
                    Allowed = false,
                    ReasonCode = "runtime_preview_pilot_adapter_exception",
                    ResourceType = "pilot"
                },
                readiness: null,
                "runtime_preview_pilot_adapter_exception",
                "RuntimePreview Pilot adapter failed; Offline fallback was used.",
                cancellationToken);
        }
    }

    private async Task<RuntimePreviewResult> NotReadyWithFallbackAsync(
        RuntimePreviewRequest request,
        RuntimePreviewPilotReadinessResult readiness,
        CancellationToken cancellationToken)
    {
        var offline = request.PilotConfig.FallbackToOffline
            ? await ExecuteOfflineAsync(request, cancellationToken)
            : RuntimePreviewResult.Fail(
                Name,
                readiness.ResourceTrace.ReasonCode,
                "RuntimePreview Pilot is not ready and offline fallback is disabled.");
        var message = "RuntimePreview Pilot requires ready allowlisted metadata resources before pilot preview can run.";
        return RuntimePreviewResult.Fail(
            Name,
            readiness.ResourceTrace.ReasonCode,
            message,
            readiness.MissingResources.Count > 0 ? readiness.MissingResources : null) with
        {
            PreviewMode = RuntimePreviewModes.MetadataOnly,
            WorkflowDraftAllowed = true,
            Source = "runtime_preview_pilot_adapter",
            PermissionDecision = PermissionDecision(
                request,
                allowed: false,
                reasonCode: readiness.ResourceTrace.ReasonCode,
                reason: message),
            ResourceTrace = readiness.ResourceTrace,
            Readiness = readiness,
            Fallback = readiness.Fallback,
            MissingResources = readiness.MissingResources,
            Issues = readiness.Issues,
            PendingActions = readiness.PendingActions,
            Warnings =
            [
                new
                {
                    code = "runtime_preview_metadata_only_boundary",
                    message = "RuntimePreview Pilot v0.8 is metadata-only and did not access real resources."
                }
            ],
            Artifacts = offline.Artifacts.Take(request.PilotConfig.MaxPreviewArtifacts).ToList(),
            BinaryIncluded = false,
            CapturedRealFrame = false,
            LoadedModelFiles = false,
            AccessedHardware = false,
            StationTouched = false
        };
    }

    private RuntimePreviewResult DenyDangerousRequest(
        RuntimePreviewRequest request,
        RuntimePreviewPilotReadinessResult readiness)
    {
        var message = "RuntimePreview Pilot denied a dangerous resource request.";
        return RuntimePreviewResult.Fail(
            Name,
            readiness.ResourceTrace.ReasonCode,
            message,
            readiness.BlockingIssues.Count > 0 ? readiness.BlockingIssues : readiness.UnsafeFindings) with
        {
            PreviewMode = RuntimePreviewModes.MetadataOnly,
            WorkflowDraftAllowed = true,
            Source = "runtime_preview_pilot_adapter",
            PermissionDecision = PermissionDecision(
                request,
                allowed: false,
                reasonCode: readiness.ResourceTrace.ReasonCode,
                reason: message),
            ResourceTrace = readiness.ResourceTrace,
            Readiness = readiness,
            Fallback = RuntimePreviewFallbackInfo.NotUsed(),
            Issues = readiness.Issues,
            MissingResources = readiness.MissingResources,
            PendingActions = readiness.PendingActions,
            Artifacts = [],
            BinaryIncluded = false,
            CapturedRealFrame = false,
            LoadedModelFiles = false,
            AccessedHardware = false,
            StationTouched = false
        };
    }

    private async Task<RuntimePreviewResult> ExecuteOfflineFallbackAsync(
        RuntimePreviewRequest request,
        RuntimePreviewResourceTrace resourceTrace,
        RuntimePreviewPilotReadinessResult? readiness,
        string reasonCode,
        string reason,
        CancellationToken cancellationToken)
    {
        var offline = await ExecuteOfflineAsync(request, cancellationToken);
        return offline with
        {
            AdapterName = OfflineRuntimePreviewAdapter.AdapterName,
            PreviewMode = RuntimePreviewModes.OfflineFixture,
            WorkflowDraftAllowed = true,
            PermissionDecision = PermissionDecision(
                request,
                allowed: offline.Success,
                reasonCode: offline.Success ? reasonCode : offline.ErrorCode ?? reasonCode,
                reason: offline.Success ? reason : "Offline fallback was not ready."),
            ResourceTrace = resourceTrace,
            Readiness = readiness,
            Fallback = new RuntimePreviewFallbackInfo
            {
                Used = true,
                FallbackAdapterName = OfflineRuntimePreviewAdapter.AdapterName,
                ReasonCode = reasonCode,
                Reason = reason
            },
            Issues = offline.Issues.Concat(new object[]
            {
                new
                {
                    code = reasonCode,
                    message = reason
                }
            }).ToList(),
            BinaryIncluded = false,
            CapturedRealFrame = false,
            LoadedModelFiles = false,
            AccessedHardware = false,
            StationTouched = false
        };
    }

    private async Task<RuntimePreviewResult> ExecuteOfflineAsync(
        RuntimePreviewRequest request,
        CancellationToken cancellationToken)
    {
        var offlineRequest = request with
        {
            AdapterName = OfflineRuntimePreviewAdapter.AdapterName,
            RequestedAdapterName = request.RequestedAdapterName ?? request.AdapterName,
            PreviewMode = RuntimePreviewModes.OfflineFixture
        };
        return await _offlineAdapter.ExecuteAsync(offlineRequest, cancellationToken);
    }

    private static RuntimePreviewResult WrapPilotMetadataResult(
        RuntimePreviewResult result,
        RuntimePreviewRequest request,
        RuntimePreviewPilotReadinessResult readiness,
        RuntimePreviewPermissionDecision permissionDecision)
    {
        var warnings = result.Warnings.Concat(new object[]
        {
            new
            {
                code = "runtime_preview_pilot_metadata_only",
                message = "RuntimePreview Pilot returned metadata only; no image bytes, real camera, Station, model file, or PLC resource was touched."
            }
        }).ToList();

        return result with
        {
            AdapterName = PilotRuntimePreviewAdapter.AdapterName,
            PreviewMode = RuntimePreviewModes.MetadataOnly,
            WorkflowDraftAllowed = true,
            Source = "runtime_preview_pilot_adapter",
            PermissionDecision = permissionDecision,
            ResourceTrace = readiness.ResourceTrace,
            Readiness = readiness,
            Fallback = RuntimePreviewFallbackInfo.NotUsed(),
            Warnings = warnings,
            Artifacts = result.Artifacts
                .Take(request.PilotConfig.MaxPreviewArtifacts)
                .ToList(),
            BinaryIncluded = false,
            CapturedRealFrame = false,
            LoadedModelFiles = false,
            AccessedHardware = false,
            StationTouched = false
        };
    }

    private static RuntimePreviewPermissionDecision PermissionDecision(
        RuntimePreviewRequest request,
        bool allowed,
        string reasonCode,
        string reason)
    {
        return new RuntimePreviewPermissionDecision
        {
            Allowed = allowed,
            ReasonCode = reasonCode,
            Reason = reason,
            RuntimePreviewConsent = request.Context.RuntimePreviewConsent,
            PilotEnabled = request.PilotConfig.Enabled,
            MetadataOnly = true,
            RequestedAdapterName = request.RequestedAdapterName ?? request.AdapterName,
            EffectiveAdapterName = allowed ? PilotRuntimePreviewAdapter.AdapterName : OfflineRuntimePreviewAdapter.AdapterName,
            AllowlistCounts = new
            {
                camera = request.PilotConfig.AllowedCameraBindingIds.Count,
                model = request.PilotConfig.AllowedModelIds.Count,
                template = request.PilotConfig.AllowedTemplateIds.Count,
                flow = request.PilotConfig.AllowedFlowIds.Count,
                resourceRoot = request.PilotConfig.AllowedResourceRoots.Count
            }
        };
    }

    private static System.Text.Json.JsonElement? ExtractWorkflowDraft(RuntimePreviewRequest request)
    {
        if (request.Arguments.ValueKind != System.Text.Json.JsonValueKind.Object)
        {
            return null;
        }

        foreach (var propertyName in new[] { "flow", "workflowDraft", "existingFlowJson" })
        {
            if (request.Arguments.TryGetProperty(propertyName, out var value) &&
                value.ValueKind == System.Text.Json.JsonValueKind.Object)
            {
                return value.Clone();
            }
        }

        return null;
    }
}
