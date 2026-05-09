using System.Text.Json;
using System.Text.Json.Serialization;

namespace Acme.Product.Infrastructure.Plugins;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum OperatorIntegrationMaturity
{
    Delivered,
    Experimental,
    PlaceholderDisabled
}

public sealed class OperatorPluginManifest
{
    [JsonPropertyName("schemaVersion")]
    public string SchemaVersion { get; set; } = OperatorPluginManifestDefaults.CurrentSchemaVersion;

    [JsonPropertyName("pluginId")]
    public string PluginId { get; set; } = string.Empty;

    [JsonPropertyName("packageId")]
    public string PackageId { get; set; } = string.Empty;

    [JsonPropertyName("packageVersion")]
    public string PackageVersion { get; set; } = "0.0.0";

    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("maturity")]
    public OperatorIntegrationMaturity Maturity { get; set; } = OperatorIntegrationMaturity.Experimental;

    [JsonPropertyName("hostCompatibility")]
    public OperatorPluginHostCompatibility HostCompatibility { get; set; } = new();

    [JsonPropertyName("nativeProfiles")]
    public List<string> NativeProfiles { get; set; } = new();

    [JsonPropertyName("capabilities")]
    public List<string> Capabilities { get; set; } = new();

    [JsonPropertyName("operators")]
    public List<OperatorPluginOperatorManifest> Operators { get; set; } = new();
}

public sealed class OperatorPluginHostCompatibility
{
    [JsonPropertyName("minHostVersion")]
    public string MinHostVersion { get; set; } = "0.0.0";

    [JsonPropertyName("maxHostVersion")]
    public string? MaxHostVersion { get; set; }

    [JsonPropertyName("operatorContractVersion")]
    public string OperatorContractVersion { get; set; } = OperatorPluginManifestDefaults.OperatorContractVersion;
}

public sealed class OperatorPluginOperatorManifest
{
    [JsonPropertyName("operatorType")]
    public string OperatorType { get; set; } = string.Empty;

    [JsonPropertyName("runtimeTypeName")]
    public string RuntimeTypeName { get; set; } = string.Empty;

    [JsonPropertyName("maturity")]
    public OperatorIntegrationMaturity Maturity { get; set; } = OperatorIntegrationMaturity.Experimental;

    [JsonPropertyName("enabledByDefault")]
    public bool EnabledByDefault { get; set; }

    [JsonPropertyName("tags")]
    public List<string> Tags { get; set; } = new();
}

public sealed class OperatorPluginCompatibilityResult
{
    public bool IsCompatible => Issues.Count == 0;

    public List<OperatorPluginCompatibilityIssue> Issues { get; } = new();
}

public sealed record OperatorPluginCompatibilityIssue(string Code, string Message);

public static class OperatorPluginManifestDefaults
{
    public const string CurrentSchemaVersion = "1.0";

    public const string OperatorContractVersion = "1.0";
}

public static class OperatorPluginManifestJson
{
    public static JsonSerializerOptions SerializerOptions { get; } = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };
}

public static class OperatorPluginManifestCompatibility
{
    public static OperatorPluginCompatibilityResult Evaluate(
        OperatorPluginManifest? manifest,
        Version hostVersion,
        string operatorContractVersion = OperatorPluginManifestDefaults.OperatorContractVersion)
    {
        ArgumentNullException.ThrowIfNull(hostVersion);

        var result = new OperatorPluginCompatibilityResult();
        if (manifest == null)
        {
            result.Issues.Add(new OperatorPluginCompatibilityIssue("ManifestMissing", "Plugin manifest is required."));
            return result;
        }

        if (!string.Equals(manifest.SchemaVersion, OperatorPluginManifestDefaults.CurrentSchemaVersion, StringComparison.Ordinal))
        {
            result.Issues.Add(new OperatorPluginCompatibilityIssue(
                "UnsupportedSchemaVersion",
                $"Unsupported plugin manifest schema version '{manifest.SchemaVersion}'."));
        }

        if (string.IsNullOrWhiteSpace(manifest.PluginId))
        {
            result.Issues.Add(new OperatorPluginCompatibilityIssue("PluginIdMissing", "PluginId is required."));
        }

        if (!Version.TryParse(manifest.PackageVersion, out _))
        {
            result.Issues.Add(new OperatorPluginCompatibilityIssue(
                "InvalidPackageVersion",
                $"PackageVersion '{manifest.PackageVersion}' is not a valid version."));
        }

        EvaluateHostVersion(manifest.HostCompatibility, hostVersion, operatorContractVersion, result);
        EvaluateOperators(manifest.Operators, result);

        return result;
    }

    private static void EvaluateHostVersion(
        OperatorPluginHostCompatibility compatibility,
        Version hostVersion,
        string operatorContractVersion,
        OperatorPluginCompatibilityResult result)
    {
        if (!Version.TryParse(compatibility.MinHostVersion, out var minHostVersion))
        {
            result.Issues.Add(new OperatorPluginCompatibilityIssue(
                "InvalidMinHostVersion",
                $"MinHostVersion '{compatibility.MinHostVersion}' is not a valid version."));
        }
        else if (hostVersion < minHostVersion)
        {
            result.Issues.Add(new OperatorPluginCompatibilityIssue(
                "HostVersionTooOld",
                $"Host version {hostVersion} is lower than plugin minimum {minHostVersion}."));
        }

        if (!string.IsNullOrWhiteSpace(compatibility.MaxHostVersion))
        {
            if (!Version.TryParse(compatibility.MaxHostVersion, out var maxHostVersion))
            {
                result.Issues.Add(new OperatorPluginCompatibilityIssue(
                    "InvalidMaxHostVersion",
                    $"MaxHostVersion '{compatibility.MaxHostVersion}' is not a valid version."));
            }
            else if (hostVersion > maxHostVersion)
            {
                result.Issues.Add(new OperatorPluginCompatibilityIssue(
                    "HostVersionTooNew",
                    $"Host version {hostVersion} is higher than plugin maximum {maxHostVersion}."));
            }
        }

        if (!string.Equals(compatibility.OperatorContractVersion, operatorContractVersion, StringComparison.Ordinal))
        {
            result.Issues.Add(new OperatorPluginCompatibilityIssue(
                "OperatorContractMismatch",
                $"Plugin operator contract '{compatibility.OperatorContractVersion}' does not match host contract '{operatorContractVersion}'."));
        }
    }

    private static void EvaluateOperators(
        IReadOnlyCollection<OperatorPluginOperatorManifest> operators,
        OperatorPluginCompatibilityResult result)
    {
        if (operators.Count == 0)
        {
            result.Issues.Add(new OperatorPluginCompatibilityIssue("OperatorsMissing", "At least one operator entry is required."));
            return;
        }

        foreach (var op in operators)
        {
            if (string.IsNullOrWhiteSpace(op.OperatorType))
            {
                result.Issues.Add(new OperatorPluginCompatibilityIssue("OperatorTypeMissing", "OperatorType is required for every plugin operator."));
            }

            if (string.IsNullOrWhiteSpace(op.RuntimeTypeName))
            {
                result.Issues.Add(new OperatorPluginCompatibilityIssue("RuntimeTypeMissing", $"RuntimeTypeName is required for operator '{op.OperatorType}'."));
            }

            if (op.Maturity == OperatorIntegrationMaturity.PlaceholderDisabled && op.EnabledByDefault)
            {
                result.Issues.Add(new OperatorPluginCompatibilityIssue(
                    "PlaceholderEnabledByDefault",
                    $"Placeholder-disabled operator '{op.OperatorType}' cannot be enabled by default."));
            }
        }
    }
}
