namespace ClearVision.Product.Core.RuntimeAssets;

public static class RuntimeAssetInputKeys
{
    public const string RuntimeAssetContext = "__ClearVisionRuntimeAssetContext";
}

public interface IRuntimeAssetContext
{
    bool IsEmpty { get; }

    IReadOnlyList<RuntimeCalibrationBundleAsset> CalibrationBundles { get; }

    bool TryGetCalibrationBundleByAssetId(string assetId, out RuntimeCalibrationBundleAsset asset);

    bool TryGetCalibrationBundleByBundleId(string bundleId, out RuntimeCalibrationBundleAsset asset);

    IReadOnlyList<RuntimeCalibrationBundleAsset> FindCalibrationBundlesByKind(string kind);
}

public sealed record RuntimeCalibrationBundleAsset(
    string AssetId,
    string BundleId,
    string Kind,
    string Version,
    long ProjectRevision,
    string ContentHash,
    string FileHash,
    string RelativePath,
    string PayloadJson);

public sealed class RuntimeAssetContext : IRuntimeAssetContext
{
    public static RuntimeAssetContext Empty { get; } = new([]);

    private readonly RuntimeCalibrationBundleAsset[] _calibrationBundles;
    private readonly Dictionary<string, RuntimeCalibrationBundleAsset> _calibrationByAssetId;
    private readonly Dictionary<string, RuntimeCalibrationBundleAsset> _calibrationByBundleId;

    public RuntimeAssetContext(IEnumerable<RuntimeCalibrationBundleAsset>? calibrationBundles)
    {
        _calibrationBundles = (calibrationBundles ?? [])
            .Where(asset => !string.IsNullOrWhiteSpace(asset.AssetId))
            .OrderBy(asset => asset.AssetId, StringComparer.Ordinal)
            .ToArray();

        _calibrationByAssetId = _calibrationBundles
            .GroupBy(asset => asset.AssetId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        _calibrationByBundleId = _calibrationBundles
            .Where(asset => !string.IsNullOrWhiteSpace(asset.BundleId))
            .GroupBy(asset => asset.BundleId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
    }

    public bool IsEmpty => _calibrationBundles.Length == 0;

    public IReadOnlyList<RuntimeCalibrationBundleAsset> CalibrationBundles => _calibrationBundles;

    public bool TryGetCalibrationBundleByAssetId(string assetId, out RuntimeCalibrationBundleAsset asset)
    {
        if (!string.IsNullOrWhiteSpace(assetId) &&
            _calibrationByAssetId.TryGetValue(assetId.Trim(), out asset!))
        {
            return true;
        }

        asset = default!;
        return false;
    }

    public bool TryGetCalibrationBundleByBundleId(string bundleId, out RuntimeCalibrationBundleAsset asset)
    {
        if (!string.IsNullOrWhiteSpace(bundleId) &&
            _calibrationByBundleId.TryGetValue(bundleId.Trim(), out asset!))
        {
            return true;
        }

        asset = default!;
        return false;
    }

    public IReadOnlyList<RuntimeCalibrationBundleAsset> FindCalibrationBundlesByKind(string kind)
    {
        if (string.IsNullOrWhiteSpace(kind))
        {
            return [];
        }

        return _calibrationBundles
            .Where(asset => string.Equals(asset.Kind, kind.Trim(), StringComparison.Ordinal))
            .ToArray();
    }
}
