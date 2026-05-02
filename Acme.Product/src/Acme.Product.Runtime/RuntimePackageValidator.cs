using System.Text.Json;
using System.Text.RegularExpressions;
using Acme.Product.Runtime.Abstractions;

namespace Acme.Product.Runtime;

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

        await ScanForSecretsAsync(package.RootPath, result, cancellationToken);

        return result;
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

    [GeneratedRegex("\"[A-Za-z]:\\\\(?:Users|Temp|Windows|Program Files|ProgramData)\\\\", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LocalAbsolutePathPattern();
}
