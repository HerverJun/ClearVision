using System.Text.Json;
using ClearVision.Product.Core.AI.Tools;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Infrastructure.AI.Agent;

namespace ClearVision.Product.Infrastructure.AI.Tools;

public sealed class RuntimePreviewPilotReadinessGate
{
    private readonly RuntimePreviewResourceAllowlistResolver _resolver;

    public RuntimePreviewPilotReadinessGate(RuntimePreviewResourceAllowlistResolver resolver)
    {
        _resolver = resolver;
    }

    public RuntimePreviewPilotReadinessResult Evaluate(
        RuntimePreviewPilotConfig config,
        RuntimePreviewPilotCatalog catalog,
        string toolName,
        JsonElement arguments,
        VisionAgentToolContext? context = null)
    {
        var normalizedConfig = config.CloneNormalized();
        var effectiveContext = (context ?? new VisionAgentToolContext()) with
        {
            RuntimePreviewPilot = normalizedConfig
        };

        var validationFailures = RuntimePreviewPilotConfigValidator.Validate(config);
        if (validationFailures.Count > 0)
        {
            return BuildResult(
                RuntimePreviewPilotReadinessStatuses.Denied,
                RuntimePreviewResourceTrace.NotEvaluated() with
                {
                    Allowed = false,
                    ReasonCode = "runtime_preview_pilot_config_invalid",
                    ResourceType = "pilot_config"
                },
                normalizedConfig,
                catalog,
                validationFailures.Select(message => Issue("runtime_preview_pilot_config_invalid", message)).ToList(),
                missingResources: [],
                unsafeFindings: validationFailures.Select(message => Issue("runtime_preview_pilot_config_invalid", message)).ToList(),
                pendingActions: [],
                fallback: RuntimePreviewFallbackInfo.NotUsed());
        }

        if (!normalizedConfig.Enabled)
        {
            var trace = RuntimePreviewResourceTrace.NotEvaluated() with
            {
                Allowed = false,
                ReasonCode = "runtime_preview_pilot_disabled",
                ResourceType = "pilot"
            };
            return BuildResult(
                RuntimePreviewPilotReadinessStatuses.NotReady,
                trace,
                normalizedConfig,
                catalog,
                [Issue("runtime_preview_pilot_disabled", "RuntimePreview Pilot is disabled.")],
                missingResources: [],
                unsafeFindings: [],
                pendingActions: [BuildPendingAction(trace)],
                fallback: OfflineFallback("runtime_preview_pilot_disabled", "Pilot disabled; offline metadata fallback remains available."));
        }

        var request = new RuntimePreviewRequest
        {
            ToolName = toolName,
            Context = effectiveContext,
            Arguments = arguments,
            PreviewMode = RuntimePreviewModes.MetadataOnly
        };
        var resourceTrace = _resolver.Resolve(request);
        if (resourceTrace.Allowed)
        {
            return BuildResult(
                RuntimePreviewPilotReadinessStatuses.Ready,
                resourceTrace,
                normalizedConfig,
                catalog,
                issues: [],
                missingResources: [],
                unsafeFindings: [],
                pendingActions: [],
                fallback: RuntimePreviewFallbackInfo.NotUsed());
        }

        var denied = IsDangerousDeny(resourceTrace.ReasonCode);
        var status = denied
            ? RuntimePreviewPilotReadinessStatuses.Denied
            : RuntimePreviewPilotReadinessStatuses.NotReady;
        var message = denied
            ? "RuntimePreview Pilot denied a dangerous resource request."
            : "RuntimePreview Pilot is not ready because required metadata resources are missing or not allowlisted.";
        return BuildResult(
            status,
            resourceTrace,
            normalizedConfig,
            catalog,
            [Issue(resourceTrace.ReasonCode, message, resourceTrace.ResourceType)],
            missingResources: resourceTrace.MissingResources,
            unsafeFindings: denied ? [Issue(resourceTrace.ReasonCode, message, resourceTrace.ResourceType)] : [],
            pendingActions: denied ? [] : [BuildPendingAction(resourceTrace)],
            fallback: denied
                ? RuntimePreviewFallbackInfo.NotUsed()
                : OfflineFallback(resourceTrace.ReasonCode, "Pilot not ready; offline metadata fallback remains available."));
    }

    private static RuntimePreviewPilotReadinessResult BuildResult(
        string status,
        RuntimePreviewResourceTrace resourceTrace,
        RuntimePreviewPilotConfig config,
        RuntimePreviewPilotCatalog catalog,
        IReadOnlyList<object> issues,
        IReadOnlyList<object> missingResources,
        IReadOnlyList<object> unsafeFindings,
        IReadOnlyList<VisionAgentPendingAction> pendingActions,
        RuntimePreviewFallbackInfo fallback)
    {
        var ready = string.Equals(status, RuntimePreviewPilotReadinessStatuses.Ready, StringComparison.OrdinalIgnoreCase);
        var denied = string.Equals(status, RuntimePreviewPilotReadinessStatuses.Denied, StringComparison.OrdinalIgnoreCase);
        return new RuntimePreviewPilotReadinessResult
        {
            Status = status,
            CanRunMetadataPilot = ready,
            WorkflowDraftAllowed = true,
            Issues = issues,
            BlockingIssues = denied ? issues : [],
            MissingResources = missingResources,
            UnsafeFindings = unsafeFindings,
            AllowlistCoverage = BuildCoverage(config, catalog),
            ResourceTrace = resourceTrace,
            PendingActions = pendingActions,
            Fallback = fallback,
            BinaryIncluded = false,
            CapturedRealFrame = false,
            LoadedModelFiles = false,
            AccessedHardware = false,
            StationTouched = false
        };
    }

    private static object BuildCoverage(RuntimePreviewPilotConfig config, RuntimePreviewPilotCatalog catalog)
    {
        return new
        {
            counts = RuntimePreviewPilotResourceCatalog.AllowlistCounts(config),
            catalogCounts = catalog.Items
                .GroupBy(item => item.ResourceType, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase),
            allowlistedCatalogItems = catalog.Items.Count(item => IsAllowlisted(item, config)),
            safeCatalogItems = catalog.Items.Count(item => item.SafeForPilot),
            metadataOnly = true
        };
    }

    private static bool IsAllowlisted(RuntimePreviewPilotCatalogItem item, RuntimePreviewPilotConfig config)
    {
        return item.ResourceType switch
        {
            "camera" => config.AllowedCameraBindingIds.Contains(item.Id, StringComparer.OrdinalIgnoreCase),
            "model" => config.AllowedModelIds.Contains(item.Id, StringComparer.OrdinalIgnoreCase),
            "template" => config.AllowedTemplateIds.Contains(item.Id, StringComparer.OrdinalIgnoreCase),
            "flow" => config.AllowedFlowIds.Contains(item.Id, StringComparer.OrdinalIgnoreCase),
            "resourceRoot" => config.AllowedResourceRoots.Contains(item.Id, StringComparer.OrdinalIgnoreCase),
            _ => false
        };
    }

    public static bool IsDangerousDeny(string? reasonCode)
    {
        return (reasonCode ?? string.Empty).Contains("path", StringComparison.OrdinalIgnoreCase) ||
               (reasonCode ?? string.Empty).Contains("file", StringComparison.OrdinalIgnoreCase) ||
               (reasonCode ?? string.Empty).Contains("plc", StringComparison.OrdinalIgnoreCase) ||
               (reasonCode ?? string.Empty).Contains("station", StringComparison.OrdinalIgnoreCase) ||
               (reasonCode ?? string.Empty).Contains("image_bytes", StringComparison.OrdinalIgnoreCase) ||
               (reasonCode ?? string.Empty).Contains("mode_denied", StringComparison.OrdinalIgnoreCase) ||
               (reasonCode ?? string.Empty).Contains("config_invalid", StringComparison.OrdinalIgnoreCase);
    }

    private static object Issue(string code, string message, string? resourceType = null)
    {
        return new
        {
            code,
            message,
            resourceType
        };
    }

    private static RuntimePreviewFallbackInfo OfflineFallback(string reasonCode, string reason)
    {
        return new RuntimePreviewFallbackInfo
        {
            Used = true,
            FallbackAdapterName = OfflineRuntimePreviewAdapter.AdapterName,
            ReasonCode = reasonCode,
            Reason = reason
        };
    }

    private static VisionAgentPendingAction BuildPendingAction(RuntimePreviewResourceTrace trace)
    {
        return new VisionAgentPendingAction
        {
            ActionType = "RuntimePreviewPilotReadinessReview",
            Title = "Review RuntimePreview Pilot readiness",
            Summary = $"RuntimePreview Pilot is not ready for {trace.ResourceType}: {trace.ReasonCode}.",
            RequiresUserConfirmation = true,
            Payload = new
            {
                resourceType = trace.ResourceType,
                reasonCode = trace.ReasonCode,
                missingResources = trace.MissingResources
            }
        };
    }
}
