using System.Security.Cryptography;

namespace Acme.Product.Runtime;

internal static class RuntimePathGuard
{
    public static string ResolveChildPath(string rootPath, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            throw new RuntimePackageException("Package path is empty.");
        }

        if (Path.IsPathFullyQualified(relativePath) || Path.IsPathRooted(relativePath))
        {
            throw new RuntimePackageException($"Package path must be relative: {relativePath}");
        }

        var candidate = Path.GetFullPath(Path.Combine(rootPath, relativePath));
        var normalizedRoot = Path.GetFullPath(rootPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;

        if (!candidate.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new RuntimePackageException($"Path escapes package root: {relativePath}");
        }

        return candidate;
    }

    public static string GetDefaultStudioExportRoot()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ClearVision",
            "RuntimePackages");
    }

    public static string ResolveControlledExportRoot(string? requestedRoot)
    {
        var defaultRoot = Path.GetFullPath(GetDefaultStudioExportRoot());
        if (string.IsNullOrWhiteSpace(requestedRoot))
        {
            Directory.CreateDirectory(defaultRoot);
            return defaultRoot;
        }

        var candidate = Path.GetFullPath(Environment.ExpandEnvironmentVariables(requestedRoot.Trim()));
        var allowedRoots = GetAllowedExportRoots(defaultRoot);
        if (!allowedRoots.Any(root => IsUnderRoot(candidate, root)))
        {
            throw new RuntimePackageException(
                "Runtime package export root is outside the controlled export directories. " +
                "Use the default Studio export root, .tmp/publish-check, the system temp directory, " +
                "or configure CV_RUNTIME_EXPORT_ALLOWED_ROOTS.");
        }

        Directory.CreateDirectory(candidate);
        return candidate;
    }

    public static string GetDefaultStationDataRoot()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ClearVisionStation");
    }

    public static string SanitizeFileName(string value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(value.Trim().Select(ch => invalid.Contains(ch) ? '-' : ch).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(sanitized) ? fallback : sanitized;
    }

    public static string ComputeSha256(byte[] bytes)
    {
        var hash = SHA256.HashData(bytes);
        return $"sha256:{Convert.ToHexString(hash).ToLowerInvariant()}";
    }

    private static IReadOnlyList<string> GetAllowedExportRoots(string defaultRoot)
    {
        var roots = new List<string>
        {
            defaultRoot,
            Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), ".tmp", "publish-check")),
            Path.GetFullPath(Path.GetTempPath())
        };

        var configured = Environment.GetEnvironmentVariable("CV_RUNTIME_EXPORT_ALLOWED_ROOTS");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            roots.AddRange(
                configured.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(path => Path.GetFullPath(Environment.ExpandEnvironmentVariables(path))));
        }

        return roots
            .Select(path => path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool IsUnderRoot(string candidate, string root)
    {
        var normalizedCandidate = candidate.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return normalizedCandidate.StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }
}
