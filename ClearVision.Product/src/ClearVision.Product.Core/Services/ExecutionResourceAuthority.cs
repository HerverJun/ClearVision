using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;

namespace ClearVision.Product.Core.Services;

public sealed record ExecutionResourceAuthorityResult(bool Allowed, string Code, string Message)
{
    public static ExecutionResourceAuthorityResult Allow() => new(true, "RESOURCE_AUTHORITY_ALLOWED", string.Empty);
    public static ExecutionResourceAuthorityResult Reject(string code, string message) => new(false, code, message);
}

public interface IExecutionResourceAuthority
{
    ExecutionResourceAuthorityResult Validate(ExecutionSnapshot snapshot);
}

/// <summary>
/// Explicit server-issued resources used outside an OperatorFlow. Merely
/// placing similarly named values in a seed dictionary grants no capability.
/// </summary>
public sealed class ExecutionExternalResourceManifest
{
    public ExecutionExternalResourceManifest(string? cameraBindingId = null)
    {
        CameraBindingId = string.IsNullOrWhiteSpace(cameraBindingId)
            ? null
            : cameraBindingId.Trim();
    }

    public string? CameraBindingId { get; }
}

/// <summary>
/// Creates opaque server-side bindings for every operator field that can select
/// a filesystem, network, database, serial, PLC, or camera resource.  Values
/// are never logged or returned; only a SHA-256 fingerprint is captured.
/// </summary>
public static class ExecutionResourceBindingManifest
{
    private static readonly HashSet<string> GenericPathFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "FilePath",
        "FolderPath",
        "Directory",
        "OutputPath",
        "SavePath",
        "ImageDirectory",
        "CalibrationDirectory",
        "ImageFolder",
        "LeftImageFolder",
        "RightImageFolder",
        "CalibrationOutputPath",
        "ModelPath",
        "LabelsPath",
        "ModelCatalogPath",
        "EdgeModelPath",
        "TemplatePath",
        "FeatureBankPath",
        "SaveFeatureBankPath",
        "EmbeddingModelPath",
        "EmbeddingManifestPath"
    };

    public static IReadOnlyDictionary<string, string> Build(
        OperatorFlow flow,
        string authority,
        IReadOnlyDictionary<string, string>? seed = null,
        ExecutionExternalResourceManifest? externalResources = null)
    {
        ArgumentNullException.ThrowIfNull(flow);
        ArgumentException.ThrowIfNullOrWhiteSpace(authority);
        var result = (seed ?? new Dictionary<string, string>())
            .Where(pair =>
                !pair.Key.StartsWith("Resource:", StringComparison.Ordinal) &&
                !pair.Key.StartsWith("ExternalResource:", StringComparison.Ordinal) &&
                !pair.Key.Equals("ResourceManifestHash", StringComparison.Ordinal))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

        var externalCameraBindingId = externalResources?.CameraBindingId;
        if (!string.IsNullOrWhiteSpace(externalCameraBindingId))
        {
            result["CameraBindingId"] = externalCameraBindingId;
            result["ExternalResource:Camera"] = $"{authority}:{Fingerprint(externalCameraBindingId)}";
        }

        foreach (var @operator in NestedExecutionFlowCatalog.EnumerateEnabledOperators(flow))
        {
            var fields = GetAuthorityFields(@operator);
            if (fields.Count == 0)
            {
                continue;
            }

            var canonical = JsonSerializer.Serialize(fields
                .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase));
            var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
            result[$"Resource:{@operator.Id:N}"] = $"{authority}:{fingerprint}";
        }

        SetManifestHash(result);
        return result;
    }

    /// <summary>
    /// Narrows an already-issued outer manifest to one nested graph. Resource
    /// fingerprints are copied, never minted from the runtime child graph.
    /// The child manifest hash is only an aggregate over those copied entries.
    /// </summary>
    public static bool TryScopeToFlow(
        OperatorFlow flow,
        IReadOnlyDictionary<string, string> outerBindings,
        out IReadOnlyDictionary<string, string> scopedBindings,
        out string code,
        out string message)
    {
        ArgumentNullException.ThrowIfNull(flow);
        ArgumentNullException.ThrowIfNull(outerBindings);
        var result = outerBindings
            .Where(pair =>
                !pair.Key.StartsWith("Resource:", StringComparison.Ordinal) &&
                !pair.Key.StartsWith("ExternalResource:", StringComparison.Ordinal) &&
                !pair.Key.Equals("CameraBindingId", StringComparison.Ordinal) &&
                !pair.Key.Equals("ResourceManifestHash", StringComparison.Ordinal))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

        foreach (var @operator in NestedExecutionFlowCatalog.EnumerateEnabledOperators(flow))
        {
            if (GetAuthorityFields(@operator).Count == 0)
            {
                continue;
            }

            var key = $"Resource:{@operator.Id:N}";
            if (!outerBindings.TryGetValue(key, out var fingerprint) ||
                string.IsNullOrWhiteSpace(fingerprint))
            {
                scopedBindings = new Dictionary<string, string>();
                code = "ADMISSION_NESTED_RESOURCE_BINDING_REQUIRED";
                message = $"Nested operator {@operator.Id:N} has no resource evidence in the outer execution snapshot.";
                return false;
            }

            result[key] = fingerprint;
        }

        SetManifestHash(result);
        scopedBindings = result;
        code = string.Empty;
        message = string.Empty;
        return true;
    }

    public static IReadOnlyDictionary<string, string> GetAuthorityFields(Operator @operator)
    {
        var names = AuthorityFieldNames(@operator).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return @operator.Parameters
            .Where(parameter => names.Contains(parameter.Name))
            .ToDictionary(
                parameter => parameter.Name,
                parameter => NormalizeValue(parameter.GetValue()),
                StringComparer.OrdinalIgnoreCase);
    }

    public static IReadOnlyList<string> AuthorityFieldNames(Operator @operator)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var parameter in @operator.Parameters.Where(item => GenericPathFields.Contains(item.Name)))
        {
            names.Add(parameter.Name);
        }

        switch (@operator.Type)
        {
            case OperatorType.ImageAcquisition:
                names.UnionWith(["SourceType", "FilePath", "CameraId"]);
                break;
            case OperatorType.HttpRequest:
                names.Add("Url");
                break;
            case OperatorType.DatabaseWrite:
                names.UnionWith(["ProfileId", "TableName"]);
                break;
            case OperatorType.TcpCommunication:
                names.Add("ProfileId");
                break;
            case OperatorType.SerialCommunication:
            case OperatorType.ModbusRtuCommunication:
                names.UnionWith(["ProfileId", "Protocol"]);
                break;
            case OperatorType.ModbusCommunication when UsesModbusRtuAuthority(@operator):
                // ExecutionSnapshot canonicalizes the legacy Modbus RTU enum
                // to ModbusCommunication. Keep the server-issued resource
                // identity stable across that clone instead of silently
                // switching from the serial profile to a Modbus TCP profile.
                names.UnionWith(["ProfileId", "Protocol"]);
                break;
            case OperatorType.ModbusCommunication:
                names.UnionWith(["ProfileId", "RegisterAddress", "FunctionCode"]);
                break;
            case OperatorType.SiemensS7Communication:
            case OperatorType.MitsubishiMcCommunication:
            case OperatorType.OmronFinsCommunication:
                names.UnionWith(["ProfileId", "Address", "Operation"]);
                break;
            case OperatorType.CameraCalibration:
            case OperatorType.FisheyeCalibration:
            case OperatorType.StereoCalibration:
            case OperatorType.NPointCalibration:
            case OperatorType.TranslationRotationCalibration:
            case OperatorType.HandEyeCalibration:
                names.Add("CalibrationAssetId");
                break;
        }

        return names.OrderBy(item => item, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public static bool UsesModbusRtuAuthority(Operator @operator)
    {
        ArgumentNullException.ThrowIfNull(@operator);
        if (@operator.Type == OperatorType.ModbusRtuCommunication)
        {
            return true;
        }

        if (@operator.Type != OperatorType.ModbusCommunication)
        {
            return false;
        }

        var protocol = @operator.Parameters.FirstOrDefault(parameter =>
            string.Equals(parameter.Name, "Protocol", StringComparison.OrdinalIgnoreCase))?.GetValue();
        return string.Equals(
            Convert.ToString(protocol, System.Globalization.CultureInfo.InvariantCulture)?.Trim(),
            "RTU",
            StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsPathFieldName(string name) => GenericPathFields.Contains(name);

    private static string NormalizeValue(object? value)
    {
        if (value == null)
        {
            return string.Empty;
        }

        if (value is JsonElement element)
        {
            return element.GetRawText();
        }

        return Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)?.Trim() ?? string.Empty;
    }

    private static void SetManifestHash(IDictionary<string, string> bindings)
    {
        var manifestCanonical = string.Join(
            "\n",
            bindings.Where(pair =>
                    pair.Key.StartsWith("Resource:", StringComparison.Ordinal) ||
                    pair.Key.StartsWith("ExternalResource:", StringComparison.Ordinal))
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => $"{pair.Key}={pair.Value}"));
        bindings["ResourceManifestHash"] = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(manifestCanonical)));
    }

    private static string Fingerprint(string value) => Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(value.Trim())));
}

public static class CanonicalPathSafety
{
    public static bool TryValidateWithinRoots(
        string path,
        IEnumerable<string> approvedRoots,
        out string canonicalPath,
        out string code,
        out string message)
    {
        canonicalPath = string.Empty;
        code = string.Empty;
        message = string.Empty;
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
        {
            code = "RESOURCE_PATH_ABSOLUTE_REQUIRED";
            message = "Resource paths must be absolute and server-approved.";
            return false;
        }

        try
        {
            canonicalPath = Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            code = "RESOURCE_PATH_INVALID";
            message = "Resource path canonicalization failed.";
            return false;
        }

        foreach (var rootValue in approvedRoots.Where(item => !string.IsNullOrWhiteSpace(item)))
        {
            var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootValue));
            var relative = Path.GetRelativePath(root, canonicalPath);
            if (relative.Equals("..", StringComparison.Ordinal) ||
                relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
                Path.IsPathFullyQualified(relative))
            {
                continue;
            }

            if (ContainsReparsePoint(root, canonicalPath))
            {
                code = "RESOURCE_PATH_REPARSE_POINT_FORBIDDEN";
                message = "Resource paths cannot traverse a reparse point or symbolic link.";
                return false;
            }

            return true;
        }

        code = "RESOURCE_PATH_OUTSIDE_APPROVED_ROOT";
        message = "Resource path is outside every approved root.";
        return false;
    }

    private static bool ContainsReparsePoint(string root, string target)
    {
        var volumeRoot = Path.GetPathRoot(root);
        if (string.IsNullOrWhiteSpace(volumeRoot))
        {
            return true;
        }

        var current = volumeRoot;
        if (InspectPath(current) == PathInspection.ReparseOrInaccessible)
        {
            return true;
        }

        // Start at the volume/share root rather than the approved root. An
        // approved directory located below a junction is not authoritative.
        var relative = Path.GetRelativePath(volumeRoot, target);
        if (relative.Equals("..", StringComparison.Ordinal) ||
            relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
            Path.IsPathFullyQualified(relative))
        {
            return true;
        }

        foreach (var segment in relative.Split(
                     [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            var inspection = InspectPath(current);
            if (inspection == PathInspection.ReparseOrInaccessible)
            {
                return true;
            }

            if (inspection == PathInspection.Missing)
            {
                break;
            }
        }

        return false;
    }

    private static PathInspection InspectPath(string path)
    {
        try
        {
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0
                ? PathInspection.ReparseOrInaccessible
                : PathInspection.Regular;
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return PathInspection.Missing;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            return PathInspection.ReparseOrInaccessible;
        }
    }

    private enum PathInspection
    {
        Missing,
        Regular,
        ReparseOrInaccessible
    }
}
