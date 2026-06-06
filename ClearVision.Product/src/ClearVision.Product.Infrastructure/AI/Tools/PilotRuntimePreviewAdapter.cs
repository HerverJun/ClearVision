using ClearVision.Product.Core.AI.Tools;
using ClearVision.Product.Infrastructure.AI.Agent;

namespace ClearVision.Product.Infrastructure.AI.Tools;

public sealed class PilotRuntimePreviewAdapter : IRuntimePreviewAdapter
{
    public const string AdapterName = "pilot_runtime_preview";

    private readonly RuntimePreviewResourceAllowlistResolver _allowlistResolver;
    private readonly OfflineRuntimePreviewAdapter _offlineAdapter;

    public PilotRuntimePreviewAdapter(
        RuntimePreviewResourceAllowlistResolver allowlistResolver,
        OfflineRuntimePreviewAdapter offlineAdapter)
    {
        _allowlistResolver = allowlistResolver;
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
                "runtime_preview_pilot_disabled",
                "RuntimePreview Pilot is disabled; Offline adapter was used.",
                cancellationToken);
        }

        try
        {
            var resourceTrace = _allowlistResolver.Resolve(request);
            if (!resourceTrace.Allowed)
            {
                return await DenyWithFallbackAsync(request, resourceTrace, cancellationToken);
            }

            var offline = await ExecuteOfflineAsync(request, cancellationToken);
            return WrapPilotMetadataResult(
                offline,
                request,
                resourceTrace,
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
                "runtime_preview_pilot_adapter_exception",
                "RuntimePreview Pilot adapter failed; Offline fallback was used.",
                cancellationToken);
        }
    }

    private async Task<RuntimePreviewResult> DenyWithFallbackAsync(
        RuntimePreviewRequest request,
        RuntimePreviewResourceTrace resourceTrace,
        CancellationToken cancellationToken)
    {
        var fallback = RuntimePreviewFallbackInfo.NotUsed();
        IReadOnlyList<RuntimePreviewArtifactSummary> artifacts = [];
        if (request.PilotConfig.FallbackToOffline && !IsDangerousDeny(resourceTrace.ReasonCode))
        {
            var offline = await ExecuteOfflineAsync(request, cancellationToken);
            artifacts = offline.Artifacts;
            fallback = new RuntimePreviewFallbackInfo
            {
                Used = true,
                FallbackAdapterName = OfflineRuntimePreviewAdapter.AdapterName,
                ReasonCode = resourceTrace.ReasonCode,
                Reason = "Pilot was denied by resource allowlist; offline metadata fallback was retained."
            };
        }
        else if (request.PilotConfig.FallbackToOffline)
        {
            fallback = new RuntimePreviewFallbackInfo
            {
                Used = true,
                FallbackAdapterName = OfflineRuntimePreviewAdapter.AdapterName,
                ReasonCode = resourceTrace.ReasonCode,
                Reason = "Pilot was denied before executing fallback metadata because the request referenced a dangerous resource."
            };
        }

        var pendingAction = BuildPilotPendingAction(resourceTrace);
        var message = resourceTrace.MissingResources.Count > 0
            ? "RuntimePreview Pilot requires allowlisted metadata resources before pilot preview can run."
            : "RuntimePreview Pilot denied the request.";
        return RuntimePreviewResult.Fail(
            Name,
            resourceTrace.ReasonCode,
            message,
            resourceTrace.MissingResources.Count > 0 ? resourceTrace.MissingResources : null) with
        {
            PreviewMode = RuntimePreviewModes.MetadataOnly,
            WorkflowDraftAllowed = true,
            Source = "runtime_preview_pilot_adapter",
            PermissionDecision = PermissionDecision(
                request,
                allowed: false,
                reasonCode: resourceTrace.ReasonCode,
                reason: message),
            ResourceTrace = resourceTrace,
            Fallback = fallback,
            MissingResources = resourceTrace.MissingResources,
            Issues =
            [
                new
                {
                    code = resourceTrace.ReasonCode,
                    message,
                    resourceType = resourceTrace.ResourceType
                }
            ],
            PendingActions = [pendingAction],
            Warnings =
            [
                new
                {
                    code = "runtime_preview_metadata_only_boundary",
                    message = "RuntimePreview Pilot v0.7 is metadata-only and did not access real resources."
                }
            ],
            Artifacts = artifacts.Take(request.PilotConfig.MaxPreviewArtifacts).ToList(),
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
        RuntimePreviewResourceTrace resourceTrace,
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
            ResourceTrace = resourceTrace,
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

    private static VisionAgentPendingAction BuildPilotPendingAction(RuntimePreviewResourceTrace resourceTrace)
    {
        return new VisionAgentPendingAction
        {
            ActionType = "RuntimePreviewPilotAllowlistReview",
            Title = "Review RuntimePreview Pilot allowlist",
            Summary = $"RuntimePreview Pilot denied {resourceTrace.ResourceType}: {resourceTrace.ReasonCode}.",
            RequiresUserConfirmation = true,
            Payload = new
            {
                resourceType = resourceTrace.ResourceType,
                reasonCode = resourceTrace.ReasonCode,
                missingResources = resourceTrace.MissingResources
            }
        };
    }

    private static bool IsDangerousDeny(string reasonCode)
    {
        return reasonCode.Contains("path", StringComparison.OrdinalIgnoreCase) ||
               reasonCode.Contains("file", StringComparison.OrdinalIgnoreCase) ||
               reasonCode.Contains("plc", StringComparison.OrdinalIgnoreCase) ||
               reasonCode.Contains("station", StringComparison.OrdinalIgnoreCase) ||
               reasonCode.Contains("image_bytes", StringComparison.OrdinalIgnoreCase);
    }
}
