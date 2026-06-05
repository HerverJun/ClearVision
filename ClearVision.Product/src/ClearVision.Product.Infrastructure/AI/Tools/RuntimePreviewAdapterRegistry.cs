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
        var adapterName = string.IsNullOrWhiteSpace(request.AdapterName)
            ? DefaultAdapterName
            : request.AdapterName!.Trim();
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

        return await adapter.ExecuteAsync(request, cancellationToken);
    }

    public static RuntimePreviewAdapterRegistry CreateDefault()
    {
        return new RuntimePreviewAdapterRegistry(
        [
            new OfflineRuntimePreviewAdapter(new RuntimePreviewArtifactStore())
        ]);
    }
}
