using System.Text.Json;
using System.Text.RegularExpressions;
using ClearVision.Product.Application.DTOs;
using ClearVision.Product.Runtime.Abstractions;

namespace ClearVision.Product.Runtime;

public sealed partial class RuntimePackageValidator
{
    public async Task<RuntimePackageValidationResult> ValidateAsync(RuntimePackage package, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(package);

        var result = new RuntimePackageValidationResult();
        var manifest = package.Manifest;

        if (!File.Exists(Path.Combine(package.RootPath, "package.json")))
        {
            AddIssue(result, RuntimeIssueSeverity.Error, "PackageManifestMissing", "package.json is required.");
        }

        if (!File.Exists(package.FlowFilePath))
        {
            AddIssue(result, RuntimeIssueSeverity.Error, "FlowFileMissing", "The entry flow file is missing.", manifest.EntryFlow);
        }

        if (!File.Exists(Path.Combine(package.RootPath, "runtime-profile.json")))
        {
            AddIssue(result, RuntimeIssueSeverity.Error, "RuntimeProfileMissing", "runtime-profile.json is required.");
        }

        if (!File.Exists(Path.Combine(package.RootPath, "quality", "validation-report.json")))
        {
            AddIssue(result, RuntimeIssueSeverity.Error, "ValidationReportMissing", "quality/validation-report.json is required.");
        }

        if (package.Flow.Operators.Count == 0)
        {
            AddIssue(result, RuntimeIssueSeverity.Error, "FlowEmpty", "The package flow does not contain any operators.");
        }

        if (!string.Equals(manifest.RuntimeApiVersion, "1.0", StringComparison.Ordinal))
        {
            AddIssue(
                result,
                RuntimeIssueSeverity.Error,
                "RuntimeApiVersionMismatch",
                $"Unsupported runtimeApiVersion '{manifest.RuntimeApiVersion}'. Expected '1.0'.");
        }

        if (!manifest.ExportAllowed)
        {
            AddIssue(result, RuntimeIssueSeverity.Error, "ExportBlocked", "The package manifest marks exportAllowed as false.");
        }

        if (manifest.PendingParameters.Count > 0)
        {
            AddIssue(
                result,
                RuntimeIssueSeverity.Error,
                "PendingParameters",
                "The package manifest still contains pending parameters.");
        }

        if (manifest.MissingResources.Count > 0)
        {
            AddIssue(
                result,
                RuntimeIssueSeverity.Error,
                "MissingResources",
                "The package manifest still contains missing resources.");
        }

        var computedHash = RuntimePathGuard.ComputeSha256(package.FlowBytes);
        if (!string.Equals(manifest.FlowHash, computedHash, StringComparison.OrdinalIgnoreCase))
        {
            AddIssue(
                result,
                RuntimeIssueSeverity.Error,
                "FlowHashMismatch",
                $"The manifest flowHash '{manifest.FlowHash}' does not match the actual flow hash '{computedHash}'.");
        }

        if (manifest.EntryFlow.Contains("..", StringComparison.Ordinal))
        {
            AddIssue(
                result,
                RuntimeIssueSeverity.Error,
                "EntryFlowTraversal",
                "entryFlow must stay under the package root.",
                manifest.EntryFlow);
        }

        if (!package.ValidationReport.IsValid)
        {
            AddIssue(
                result,
                RuntimeIssueSeverity.Warning,
                "ValidationReportMarkedInvalid",
                "The embedded validation report is marked invalid.");
        }

        ValidateProjectAssetsRoot(package, result);
        await ValidateProjectAssetsAsync(package, result, cancellationToken);
        await ScanForSecretsAsync(package.RootPath, result, cancellationToken);

        return result;
    }

    private static void ValidateProjectAssetsRoot(RuntimePackage package, RuntimePackageValidationResult result)
    {
        var projectAssetsRoot = package.Manifest.FieldExtensions.ProjectAssets;
        if (string.IsNullOrWhiteSpace(projectAssetsRoot))
        {
            return;
        }

        try
        {
            var resolved = RuntimePathGuard.ResolveAssetPath(package.RootPath, projectAssetsRoot);
            if (!Directory.Exists(resolved))
            {
                AddIssue(
                    result,
                    RuntimeIssueSeverity.Error,
                    "ProjectAssetsRootMissing",
                    "The package manifest points to a missing project assets root.",
                    projectAssetsRoot);
            }
        }
        catch (RuntimePackageException ex)
        {
            AddIssue(
                result,
                RuntimeIssueSeverity.Error,
                "ProjectAssetsRootInvalid",
                ex.Message,
                projectAssetsRoot);
        }
    }

    private static async Task ValidateProjectAssetsAsync(
        RuntimePackage package,
        RuntimePackageValidationResult result,
        CancellationToken cancellationToken)
    {
        var assets = package.Manifest.Assets;
        if (assets == null)
        {
            return;
        }

        if (assets.SchemaVersion != 1)
        {
            AddIssue(
                result,
                RuntimeIssueSeverity.Error,
                "ProjectAssetsSchemaVersionUnsupported",
                $"Unsupported runtime package assets schemaVersion '{assets.SchemaVersion}'. Expected '1'.");
        }

        var relativePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var calibrationAssets = assets.CalibrationAssets ?? [];
        var spatialAssets = assets.SpatialAssets ?? [];

        foreach (var asset in calibrationAssets)
        {
            await ValidateProjectAssetFileAsync(
                package,
                asset,
                relativePaths,
                "CalibrationBundleV2",
                DeserializeCalibrationAsset,
                ValidateCalibrationAssetFile,
                result,
                cancellationToken);
        }

        foreach (var asset in spatialAssets)
        {
            await ValidateProjectAssetFileAsync(
                package,
                asset,
                relativePaths,
                null,
                DeserializeSpatialAsset,
                ValidateSpatialAssetFile,
                result,
                cancellationToken);
        }
    }

    private static async Task ValidateProjectAssetFileAsync<TAsset>(
        RuntimePackage package,
        RuntimePackageProjectAsset manifestAsset,
        HashSet<string> relativePaths,
        string? requiredKind,
        Func<byte[], TAsset?> deserialize,
        Action<RuntimePackageProjectAsset, TAsset, RuntimePackageValidationResult> validateAsset,
        RuntimePackageValidationResult result,
        CancellationToken cancellationToken)
        where TAsset : class
    {
        if (string.IsNullOrWhiteSpace(manifestAsset.AssetId))
        {
            AddIssue(result, RuntimeIssueSeverity.Error, "ProjectAssetIdMissing", "Project asset id is required.", manifestAsset.RelativePath);
        }

        if (string.IsNullOrWhiteSpace(manifestAsset.Kind))
        {
            AddIssue(result, RuntimeIssueSeverity.Error, "ProjectAssetKindMissing", "Project asset kind is required.", manifestAsset.RelativePath);
        }

        if (requiredKind != null &&
            !string.Equals(manifestAsset.Kind, requiredKind, StringComparison.Ordinal))
        {
            AddIssue(
                result,
                RuntimeIssueSeverity.Error,
                "ProjectAssetKindMismatch",
                $"Project asset kind must be {requiredKind}.",
                manifestAsset.RelativePath);
        }

        if (!string.Equals(manifestAsset.Status, "authority", StringComparison.OrdinalIgnoreCase))
        {
            AddIssue(result, RuntimeIssueSeverity.Error, "ProjectAssetNotAuthority", "Project asset must be an authority asset.", manifestAsset.RelativePath);
        }

        if (!IsSha256Hash(manifestAsset.ContentHash))
        {
            AddIssue(result, RuntimeIssueSeverity.Error, "ProjectAssetContentHashInvalid", "Project asset contentHash must be a sha256 hash.", manifestAsset.RelativePath);
        }

        if (!IsSha256Hash(manifestAsset.FileHash))
        {
            AddIssue(result, RuntimeIssueSeverity.Error, "ProjectAssetFileHashInvalid", "Project asset fileHash must be a sha256 hash.", manifestAsset.RelativePath);
        }

        if (!relativePaths.Add(manifestAsset.RelativePath))
        {
            AddIssue(result, RuntimeIssueSeverity.Error, "ProjectAssetPathDuplicate", "Project asset relativePath is duplicated.", manifestAsset.RelativePath);
        }

        string assetPath;
        try
        {
            assetPath = RuntimePathGuard.ResolveAssetPath(package.RootPath, manifestAsset.RelativePath);
        }
        catch (RuntimePackageException ex)
        {
            AddIssue(result, RuntimeIssueSeverity.Error, "ProjectAssetPathInvalid", ex.Message, manifestAsset.RelativePath);
            return;
        }

        if (!File.Exists(assetPath))
        {
            AddIssue(result, RuntimeIssueSeverity.Error, "ProjectAssetFileMissing", "Project asset file is missing.", manifestAsset.RelativePath);
            return;
        }

        byte[] bytes;
        try
        {
            bytes = await File.ReadAllBytesAsync(assetPath, cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AddIssue(result, RuntimeIssueSeverity.Error, "ProjectAssetFileUnreadable", ex.Message, manifestAsset.RelativePath);
            return;
        }

        var actualFileHash = RuntimePathGuard.ComputeSha256(bytes);
        if (!string.Equals(actualFileHash, manifestAsset.FileHash, StringComparison.Ordinal))
        {
            AddIssue(
                result,
                RuntimeIssueSeverity.Error,
                "ProjectAssetFileHashMismatch",
                $"Project asset fileHash '{manifestAsset.FileHash}' does not match actual hash '{actualFileHash}'.",
                manifestAsset.RelativePath);
            return;
        }

        TAsset? asset;
        try
        {
            asset = deserialize(bytes);
        }
        catch (JsonException ex)
        {
            AddIssue(result, RuntimeIssueSeverity.Error, "ProjectAssetJsonMalformed", ex.Message, manifestAsset.RelativePath);
            return;
        }

        if (asset == null)
        {
            AddIssue(result, RuntimeIssueSeverity.Error, "ProjectAssetJsonMalformed", "Project asset JSON could not be parsed.", manifestAsset.RelativePath);
            return;
        }

        validateAsset(manifestAsset, asset, result);
    }

    private static ProjectCalibrationAssetDto? DeserializeCalibrationAsset(byte[] bytes) =>
        JsonSerializer.Deserialize<ProjectCalibrationAssetDto>(bytes, ProjectAssetJson.Options);

    private static ProjectSpatialAssetDto? DeserializeSpatialAsset(byte[] bytes) =>
        JsonSerializer.Deserialize<ProjectSpatialAssetDto>(bytes, ProjectAssetJson.Options);

    private static void ValidateCalibrationAssetFile(
        RuntimePackageProjectAsset manifestAsset,
        ProjectCalibrationAssetDto asset,
        RuntimePackageValidationResult result)
    {
        ValidateCommonAssetFile(
            manifestAsset,
            asset.AssetId,
            asset.Kind,
            asset.Version,
            asset.ProjectRevision,
            asset.Status,
            asset.ContentHash,
            asset.Payload,
            result);

        if (!string.Equals(asset.Kind, "CalibrationBundleV2", StringComparison.Ordinal))
        {
            AddIssue(result, RuntimeIssueSeverity.Error, "ProjectAssetKindMismatch", "Calibration asset file must be CalibrationBundleV2.", manifestAsset.RelativePath);
        }
    }

    private static void ValidateSpatialAssetFile(
        RuntimePackageProjectAsset manifestAsset,
        ProjectSpatialAssetDto asset,
        RuntimePackageValidationResult result) =>
        ValidateCommonAssetFile(
            manifestAsset,
            asset.AssetId,
            asset.Kind,
            asset.Version,
            asset.ProjectRevision,
            asset.Status,
            asset.ContentHash,
            asset.Payload,
            result);

    private static void ValidateCommonAssetFile(
        RuntimePackageProjectAsset manifestAsset,
        string assetId,
        string kind,
        string version,
        long projectRevision,
        string status,
        string contentHash,
        JsonElement payload,
        RuntimePackageValidationResult result)
    {
        if (!string.Equals(assetId, manifestAsset.AssetId, StringComparison.Ordinal))
        {
            AddIssue(result, RuntimeIssueSeverity.Error, "ProjectAssetMetadataMismatch", "Project asset file assetId does not match manifest.", manifestAsset.RelativePath);
        }

        if (!string.Equals(kind, manifestAsset.Kind, StringComparison.Ordinal))
        {
            AddIssue(result, RuntimeIssueSeverity.Error, "ProjectAssetMetadataMismatch", "Project asset file kind does not match manifest.", manifestAsset.RelativePath);
        }

        if (!string.Equals(version, manifestAsset.Version, StringComparison.Ordinal))
        {
            AddIssue(result, RuntimeIssueSeverity.Error, "ProjectAssetMetadataMismatch", "Project asset file version does not match manifest.", manifestAsset.RelativePath);
        }

        if (projectRevision != manifestAsset.ProjectRevision)
        {
            AddIssue(result, RuntimeIssueSeverity.Error, "ProjectAssetMetadataMismatch", "Project asset file projectRevision does not match manifest.", manifestAsset.RelativePath);
        }

        if (!string.Equals(status, manifestAsset.Status, StringComparison.OrdinalIgnoreCase))
        {
            AddIssue(result, RuntimeIssueSeverity.Error, "ProjectAssetMetadataMismatch", "Project asset file status does not match manifest.", manifestAsset.RelativePath);
        }

        if (!string.Equals(contentHash, manifestAsset.ContentHash, StringComparison.Ordinal))
        {
            AddIssue(result, RuntimeIssueSeverity.Error, "ProjectAssetMetadataMismatch", "Project asset file contentHash does not match manifest.", manifestAsset.RelativePath);
        }

        try
        {
            var payloadHash = ProjectAssetJson.ComputePayloadHash(payload);
            if (!string.Equals(payloadHash, manifestAsset.ContentHash, StringComparison.Ordinal))
            {
                AddIssue(
                    result,
                    RuntimeIssueSeverity.Error,
                    "ProjectAssetContentHashMismatch",
                    $"Project asset payload hash '{payloadHash}' does not match manifest contentHash '{manifestAsset.ContentHash}'.",
                    manifestAsset.RelativePath);
            }
        }
        catch (InvalidOperationException ex)
        {
            AddIssue(result, RuntimeIssueSeverity.Error, "ProjectAssetPayloadInvalid", ex.Message, manifestAsset.RelativePath);
        }
    }

    private static async Task ScanForSecretsAsync(string root, RuntimePackageValidationResult result, CancellationToken cancellationToken)
    {
        foreach (var path in Directory.EnumerateFiles(root, "*.json", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
            var text = await File.ReadAllTextAsync(path, cancellationToken);

            if (SecretKeyPattern().IsMatch(text))
            {
                AddIssue(result, RuntimeIssueSeverity.Error, "SecretLikeField", "Package contains a suspicious secret-like field name.", relative);
            }

            if (SecretValuePattern().IsMatch(text))
            {
                AddIssue(result, RuntimeIssueSeverity.Error, "SecretLikeValue", "Package contains a suspicious high-risk token value.", relative);
            }

            if (LocalAbsolutePathPattern().IsMatch(text))
            {
                AddIssue(result, RuntimeIssueSeverity.Error, "LocalAbsolutePath", "Package contains a local absolute path.", relative);
            }
        }
    }

    private static bool IsSha256Hash(string? value)
    {
        const string prefix = "sha256:";
        if (string.IsNullOrWhiteSpace(value) ||
            value.Length != prefix.Length + 64 ||
            !value.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        return value[prefix.Length..].All(ch =>
            ch is >= '0' and <= '9' ||
            ch is >= 'a' and <= 'f');
    }

    private static void AddIssue(
        RuntimePackageValidationResult result,
        RuntimeIssueSeverity severity,
        string code,
        string message,
        string? relativePath = null)
    {
        result.Issues.Add(new RuntimePackageValidationIssue
        {
            Severity = severity,
            Code = code,
            Message = message,
            RelativePath = relativePath
        });
    }

    [GeneratedRegex("\"(?:apiKey|api_key|token|secret|password|privateKey|accessKey)\"\\s*:", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SecretKeyPattern();

    [GeneratedRegex("(?:sk-[A-Za-z0-9_-]{20,}|ghp_[A-Za-z0-9_]{20,}|AKIA[0-9A-Z]{16})", RegexOptions.CultureInvariant)]
    private static partial Regex SecretValuePattern();

    [GeneratedRegex("\"[A-Za-z]:(?:\\\\\\\\|/)(?:Users|Temp|Windows|Program Files|ProgramData)(?:\\\\\\\\|/)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LocalAbsolutePathPattern();
}
