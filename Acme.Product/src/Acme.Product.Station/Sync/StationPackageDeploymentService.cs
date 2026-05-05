using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Acme.Product.Runtime;
using Acme.Product.Runtime.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Acme.Product.Station.Sync;

public sealed class StationPackageDeploymentService
{
    private readonly StationSyncOptions _options;
    private readonly RuntimeHost _runtimeHost;
    private readonly StationLocalSettingsStore _settingsStore;
    private readonly ILogger<StationPackageDeploymentService> _logger;
    private readonly HttpClient _httpClient = new();

    public StationPackageDeploymentService(
        IOptions<StationSyncOptions> options,
        RuntimeHost runtimeHost,
        StationLocalSettingsStore settingsStore,
        ILogger<StationPackageDeploymentService> logger)
    {
        _options = options.Value;
        _runtimeHost = runtimeHost;
        _settingsStore = settingsStore;
        _logger = logger;
    }

    public async Task<string> DeployAsync(string payloadJson, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Deserialize<DeployPackagePayload>(
            payloadJson,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException("DeployPackage payload is empty.");

        if (string.IsNullOrWhiteSpace(payload.PackageId))
        {
            throw new InvalidOperationException("DeployPackage payload is missing packageId.");
        }

        if (string.IsNullOrWhiteSpace(payload.DownloadUrl))
        {
            throw new InvalidOperationException("DeployPackage payload is missing downloadUrl.");
        }

        var packageRoot = Path.GetFullPath(_options.ResolvedPackageDirectory);
        var stagingRoot = Path.Combine(packageRoot, "staging");
        var activeRoot = Path.Combine(packageRoot, "active");
        var lastKnownGoodRoot = Path.Combine(packageRoot, "last-known-good");
        var archiveRoot = Path.Combine(packageRoot, "archive");
        Directory.CreateDirectory(packageRoot);
        Directory.CreateDirectory(archiveRoot);

        var downloadPath = Path.Combine(packageRoot, $"{payload.PackageId}.cvpkg.download");
        await DownloadAsync(payload, downloadPath, cancellationToken);
        await VerifyHashAsync(downloadPath, payload.Sha256, cancellationToken);

        ResetDirectory(stagingRoot);
        ZipFile.ExtractToDirectory(downloadPath, stagingRoot);
        var runtimeRoot = ResolveRuntimeRoot(stagingRoot);
        ValidateExtractedPackage(stagingRoot, runtimeRoot, payload);

        if (Directory.Exists(activeRoot))
        {
            ResetDirectory(lastKnownGoodRoot);
            CopyDirectory(activeRoot, lastKnownGoodRoot);
        }

        var archiveTarget = Path.Combine(archiveRoot, $"{payload.PackageId}-{DateTime.UtcNow:yyyyMMddHHmmss}");
        if (Directory.Exists(archiveTarget))
        {
            Directory.Delete(archiveTarget, recursive: true);
        }

        if (Directory.Exists(activeRoot))
        {
            Directory.Move(activeRoot, archiveTarget);
        }

        Directory.Move(runtimeRoot, activeRoot);

        try
        {
            if (!File.Exists(Path.Combine(activeRoot, "package.json")))
            {
                throw new InvalidOperationException("Package is missing runtime package.json.");
            }

            await _runtimeHost.LoadPackageAsync(activeRoot, cancellationToken);
            _settingsStore.UpdateLastGoodPackage(activeRoot);
            return $"Package {payload.PackageId} deployed.";
        }
        catch
        {
            RollBack(activeRoot, lastKnownGoodRoot);
            throw;
        }
        finally
        {
            TryDelete(downloadPath);
            if (Directory.Exists(stagingRoot))
            {
                Directory.Delete(stagingRoot, recursive: true);
            }
        }
    }

    private async Task DownloadAsync(DeployPackagePayload payload, string targetPath, CancellationToken cancellationToken)
    {
        var downloadUri = BuildDownloadUri(payload.DownloadUrl);
        using var request = new HttpRequestMessage(HttpMethod.Get, downloadUri);
        if (!string.IsNullOrWhiteSpace(_options.SharedToken))
        {
            request.Headers.TryAddWithoutValidation(StationSyncContractDefaults.StationTokenHeaderName, _options.SharedToken);
            request.Headers.TryAddWithoutValidation("X-Station-Token", _options.SharedToken);
        }

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var fileStream = File.Create(targetPath);
        await responseStream.CopyToAsync(fileStream, cancellationToken);
    }

    private Uri BuildDownloadUri(string downloadUrl)
    {
        var hubUri = new Uri(_options.ResolvedStudioHubUrl, UriKind.Absolute);
        var baseUri = new Uri($"{hubUri.Scheme}://{hubUri.Authority}");
        if (Uri.TryCreate(downloadUrl, UriKind.Absolute, out var absolute))
        {
            if (!IsSameOrigin(baseUri, absolute))
            {
                throw new InvalidOperationException("DeployPackage downloadUrl must point to the configured Studio origin.");
            }

            return absolute;
        }

        return new Uri(baseUri, downloadUrl);
    }

    private static bool IsSameOrigin(Uri expectedOrigin, Uri candidate)
    {
        return string.Equals(expectedOrigin.Scheme, candidate.Scheme, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(expectedOrigin.Host, candidate.Host, StringComparison.OrdinalIgnoreCase) &&
               expectedOrigin.Port == candidate.Port;
    }

    private static async Task VerifyHashAsync(string path, string? expectedSha256, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(expectedSha256))
        {
            return;
        }

        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        var actual = Convert.ToHexString(hash).ToLowerInvariant();
        var expected = expectedSha256.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)
            ? expectedSha256["sha256:".Length..]
            : expectedSha256;

        if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Downloaded package hash does not match the Studio manifest.");
        }
    }

    private static string ResolveRuntimeRoot(string stagingRoot)
    {
        var rootPackageJson = Path.Combine(stagingRoot, "package.json");
        if (File.Exists(rootPackageJson))
        {
            return stagingRoot;
        }

        var nestedPackage = Path.Combine(stagingRoot, "package");
        return Directory.Exists(nestedPackage) ? nestedPackage : stagingRoot;
    }

    private static void ValidateExtractedPackage(string stagingRoot, string runtimeRoot, DeployPackagePayload payload)
    {
        var manifestPath = Path.Combine(stagingRoot, "manifest.json");
        if (!File.Exists(manifestPath))
        {
            manifestPath = Path.Combine(runtimeRoot, "manifest.json");
        }

        if (!File.Exists(manifestPath))
        {
            throw new InvalidOperationException("Package manifest is missing.");
        }

        var manifest = JsonSerializer.Deserialize<StationPackageManifestDto>(
            File.ReadAllText(manifestPath),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException("Package manifest is invalid.");

        if (manifest.SchemaVersion <= 0)
        {
            throw new InvalidOperationException("Package manifest schemaVersion is invalid.");
        }

        if (string.IsNullOrWhiteSpace(manifest.PackageId))
        {
            throw new InvalidOperationException("Package manifest packageId is missing.");
        }

        if (!string.Equals(manifest.PackageId, payload.PackageId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Package manifest packageId does not match deploy payload.");
        }

        if (!string.IsNullOrWhiteSpace(payload.Sha256) &&
            !string.IsNullOrWhiteSpace(manifest.Sha256) &&
            !string.Equals(NormalizeSha256(payload.Sha256), NormalizeSha256(manifest.Sha256), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Package manifest sha256 does not match deploy payload.");
        }

        if (!IsStationVersionSupported(manifest.MinStationVersion))
        {
            throw new InvalidOperationException($"Package requires Station version {manifest.MinStationVersion} or newer.");
        }
    }

    private static bool IsStationVersionSupported(string? minStationVersion)
    {
        if (string.IsNullOrWhiteSpace(minStationVersion))
        {
            return true;
        }

        var current = typeof(StationPackageDeploymentService).Assembly.GetName().Version ?? new Version(0, 1, 0);
        return TryParseVersion(minStationVersion, out var minimum) && current >= minimum;
    }

    private static bool TryParseVersion(string value, out Version version)
    {
        var normalized = value.Split('-', '+')[0];
        return Version.TryParse(normalized, out version!);
    }

    private static string NormalizeSha256(string value)
    {
        return value.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)
            ? value["sha256:".Length..]
            : value;
    }

    private static void RollBack(string activeRoot, string lastKnownGoodRoot)
    {
        if (!Directory.Exists(lastKnownGoodRoot))
        {
            return;
        }

        if (Directory.Exists(activeRoot))
        {
            Directory.Delete(activeRoot, recursive: true);
        }

        CopyDirectory(lastKnownGoodRoot, activeRoot);
    }

    private static void ResetDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }

        Directory.CreateDirectory(path);
    }

    private static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var sourcePath in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(source, sourcePath);
            var targetPath = Path.Combine(target, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            File.Copy(sourcePath, targetPath, overwrite: true);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }

    private sealed class DeployPackagePayload
    {
        public string PackageId { get; set; } = string.Empty;

        public string? PackageName { get; set; }

        public string? PackageVersion { get; set; }

        public string DownloadUrl { get; set; } = string.Empty;

        public string? Sha256 { get; set; }
    }
}
