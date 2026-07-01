using ClearVision.Product.Core.AI.Tools;

namespace ClearVision.Product.Infrastructure.AI.Tools;

public sealed class RuntimePreviewAdapterRegistry
{
    public const string DefaultAdapterName = OfflineRuntimePreviewAdapter.AdapterName;

    private readonly IReadOnlyDictionary<string, IRuntimePreviewAdapter> _adapters;

    public RuntimePreviewAdapterRegistry(IEnumerable<IRuntimePreviewAdapter> adapters)
    {
        _adapters = adapters
            .Where(adapter => !string.IsNullOrWhiteSpace(adapter.Name))
            .GroupBy(adapter => adapter.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.First(),
                StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<string> ListAdapterNames()
    {
        return _adapters.Keys
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public bool TryGet(string? adapterName, out IRuntimePreviewAdapter adapter)
    {
        var name = string.IsNullOrWhiteSpace(adapterName)
            ? DefaultAdapterName
            : adapterName.Trim();
        return _adapters.TryGetValue(name, out adapter!);
    }

    public async Task<RuntimePreviewResult> ExecuteAsync(
        RuntimePreviewRequest request,
        CancellationToken cancellationToken)
    {
        var adapterName = ResolveAdapterName(request);
        if (!_adapters.TryGetValue(adapterName, out var adapter))
        {
            return RuntimePreviewResult.Fail(
                adapterName,
                "runtime_preview_adapter_not_found",
                $"RuntimePreview adapter '{adapterName}' is not registered.");
        }

        if (!adapter.SupportedToolNames.Contains(request.ToolName, StringComparer.OrdinalIgnoreCase))
        {
            return RuntimePreviewResult.Fail(
                adapter.Name,
                "runtime_preview_adapter_tool_not_supported",
                $"RuntimePreview adapter '{adapter.Name}' does not support tool '{request.ToolName}'.");
        }

        try
        {
            return await adapter.ExecuteAsync(request with
            {
                AdapterName = adapter.Name,
                RequestedAdapterName = request.RequestedAdapterName ?? request.AdapterName
            }, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            if (request.Context.RuntimePreviewPilot.FallbackToOffline &&
                !string.Equals(adapter.Name, OfflineRuntimePreviewAdapter.AdapterName, StringComparison.OrdinalIgnoreCase) &&
                _adapters.TryGetValue(OfflineRuntimePreviewAdapter.AdapterName, out var offline))
            {
                var fallback = await offline.ExecuteAsync(request with
                {
                    AdapterName = OfflineRuntimePreviewAdapter.AdapterName,
                    RequestedAdapterName = request.RequestedAdapterName ?? request.AdapterName,
                    PreviewMode = RuntimePreviewModes.OfflineFixture
                }, cancellationToken);
                return fallback with
                {
                    WorkflowDraftAllowed = true,
                    PermissionDecision = new RuntimePreviewPermissionDecision
                    {
                        Allowed = fallback.Success,
                        ReasonCode = "runtime_preview_adapter_exception_fallback",
                        Reason = "RuntimePreview adapter failed; Offline fallback was used.",
                        RuntimePreviewConsent = request.Context.RuntimePreviewConsent,
                        PilotEnabled = request.Context.RuntimePreviewPilot.Enabled,
                        MetadataOnly = true,
                        RequestedAdapterName = request.RequestedAdapterName ?? request.AdapterName,
                        EffectiveAdapterName = OfflineRuntimePreviewAdapter.AdapterName
                    },
                    Fallback = new RuntimePreviewFallbackInfo
                    {
                        Used = true,
                        FallbackAdapterName = OfflineRuntimePreviewAdapter.AdapterName,
                        ReasonCode = "runtime_preview_adapter_exception_fallback",
                        Reason = "RuntimePreview adapter failed; Offline fallback was used."
                    },
                    BinaryIncluded = false,
                    CapturedRealFrame = false,
                    LoadedModelFiles = false,
                    AccessedHardware = false,
                    StationTouched = false
                };
            }

            return RuntimePreviewResult.Fail(
                adapter.Name,
                "runtime_preview_adapter_exception",
                "RuntimePreview adapter failed.");
        }
    }

    public static RuntimePreviewAdapterRegistry CreateDefault()
    {
        var artifactStore = new RuntimePreviewArtifactStore();
        var offline = new OfflineRuntimePreviewAdapter(artifactStore);
        return new RuntimePreviewAdapterRegistry(
        [
            offline,
            new PilotRuntimePreviewAdapter(
                new RuntimePreviewPilotResourceCatalog(),
                new RuntimePreviewPilotReadinessGate(new RuntimePreviewResourceAllowlistResolver()),
                offline)
        ]);
    }

    private static string ResolveAdapterName(RuntimePreviewRequest request)
    {
        var requested = string.IsNullOrWhiteSpace(request.AdapterName)
            ? null
            : request.AdapterName.Trim();
        if (!request.Context.RuntimePreviewPilot.Enabled)
        {
            return string.IsNullOrWhiteSpace(requested) ||
                   string.Equals(requested, PilotRuntimePreviewAdapter.AdapterName, StringComparison.OrdinalIgnoreCase)
                ? DefaultAdapterName
                : requested;
        }

        if (string.IsNullOrWhiteSpace(requested) ||
            string.Equals(requested, PilotRuntimePreviewAdapter.AdapterName, StringComparison.OrdinalIgnoreCase))
        {
            return PilotRuntimePreviewAdapter.AdapterName;
        }

        return requested;
    }
}
