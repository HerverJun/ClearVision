using System.Text.Json;
using System.Text.Json.Serialization;
using Acme.Product.Runtime;
using Acme.Product.Runtime.Abstractions;

namespace Acme.Product.Station;

public sealed class StationSiteProfileStore
{
    private const string LocalProfileId = "local-site";
    private readonly string _rootPath;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Converters =
        {
            new JsonStringEnumConverter()
        }
    };

    public StationSiteProfileStore()
        : this(null)
    {
    }

    public StationSiteProfileStore(string? rootPath)
    {
        _rootPath = string.IsNullOrWhiteSpace(rootPath)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ClearVisionStation")
            : Path.GetFullPath(rootPath);
        Directory.CreateDirectory(_rootPath);
    }

    public string GetProfilePath(RuntimePackage package)
    {
        ArgumentNullException.ThrowIfNull(package);

        var profileDirectoryName =
            $"{SanitizePathSegment(package.Manifest.PackageId, "package")}_{SanitizeFlowHash(package.Manifest.FlowHash)}";
        return Path.Combine(_rootPath, "profiles", profileDirectoryName, "site-profile.json");
    }

    public RuntimeSiteProfile LoadOrCreate(RuntimePackage package)
    {
        ArgumentNullException.ThrowIfNull(package);

        var profilePath = GetProfilePath(package);
        if (File.Exists(profilePath))
        {
            try
            {
                var json = File.ReadAllText(profilePath);
                var profile = JsonSerializer.Deserialize<RuntimeSiteProfile>(json, _jsonOptions);
                if (profile != null && MatchesPackage(profile, package))
                {
                    var validation = RuntimeParameterValidator.Validate(package.ParameterSchema, profile);
                    if (validation.IsValid)
                    {
                        return RuntimeParameterOverrideApplier.CloneProfile(profile);
                    }
                }
            }
            catch
            {
                // Invalid local profiles are ignored so the package default remains runnable.
            }
        }

        return CreateLocalProfileFromPackageDefault(package);
    }

    public RuntimeSiteProfile Save(RuntimePackage package, RuntimeSiteProfile profile)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(profile);

        var nextProfile = RuntimeParameterOverrideApplier.CloneProfile(profile);
        nextProfile.ProfileId = LocalProfileId;
        nextProfile.PackageId = package.Manifest.PackageId;
        nextProfile.FlowHash = package.Manifest.FlowHash;
        nextProfile.Revision += 1;
        nextProfile.UpdatedAtUtc = DateTimeOffset.UtcNow;
        nextProfile.UpdatedBy = "local-engineer";

        RuntimeParameterValidator.ThrowIfInvalid(package.ParameterSchema, nextProfile);
        WriteProfile(package, nextProfile);
        return RuntimeParameterOverrideApplier.CloneProfile(nextProfile);
    }

    public RuntimeSiteProfile ResetToPackageDefault(RuntimePackage package, RuntimeSiteProfile? currentProfile)
    {
        ArgumentNullException.ThrowIfNull(package);

        var revision = currentProfile?.Revision ?? package.DefaultSiteProfile.Revision;
        var profile = new RuntimeSiteProfile
        {
            ProfileId = LocalProfileId,
            PackageId = package.Manifest.PackageId,
            FlowHash = package.Manifest.FlowHash,
            Revision = revision + 1,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedBy = "local-engineer",
            Overrides = []
        };

        RuntimeParameterValidator.ThrowIfInvalid(package.ParameterSchema, profile);
        WriteProfile(package, profile);
        return RuntimeParameterOverrideApplier.CloneProfile(profile);
    }

    private RuntimeSiteProfile CreateLocalProfileFromPackageDefault(RuntimePackage package)
    {
        var profile = RuntimeParameterOverrideApplier.CloneProfile(package.DefaultSiteProfile);
        profile.ProfileId = LocalProfileId;
        profile.PackageId = package.Manifest.PackageId;
        profile.FlowHash = package.Manifest.FlowHash;
        profile.UpdatedBy = "local-engineer";
        return profile;
    }

    private void WriteProfile(RuntimePackage package, RuntimeSiteProfile profile)
    {
        var profilePath = GetProfilePath(package);
        var directory = Path.GetDirectoryName(profilePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(profile, _jsonOptions);
        File.WriteAllText(profilePath, json);
    }

    private static bool MatchesPackage(RuntimeSiteProfile profile, RuntimePackage package)
    {
        return string.Equals(profile.PackageId, package.Manifest.PackageId, StringComparison.Ordinal) &&
               string.Equals(profile.FlowHash, package.Manifest.FlowHash, StringComparison.OrdinalIgnoreCase);
    }

    private static string SanitizeFlowHash(string flowHash)
    {
        var normalized = flowHash;
        if (normalized.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized["sha256:".Length..];
        }

        return SanitizePathSegment(normalized, "flow");
    }

    private static string SanitizePathSegment(string value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(value.Trim().Select(ch => invalid.Contains(ch) ? '-' : ch).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(sanitized) ? fallback : sanitized;
    }
}
