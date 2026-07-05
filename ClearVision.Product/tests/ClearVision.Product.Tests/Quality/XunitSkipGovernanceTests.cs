using System.Text.RegularExpressions;

namespace ClearVision.Product.Tests.Quality;

public sealed class XunitSkipGovernanceTests
{
    private static readonly Regex SkipAttributePattern = new(
        @"\[(?:Fact|Theory)\s*\(\s*Skip\s*=\s*""(?<reason>(?:\\""|[^""])*)""",
        RegexOptions.Compiled);

    private static readonly Regex ExpiryPattern = new(
        @"(?:^|[;\s])expires=(?<date>\d{4}-\d{2}-\d{2})(?:$|[;\s])",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    [Fact]
    public void XunitSkipAttributes_ShouldDeclareOwnerAndUnexpiredExpiry()
    {
        var testProjectRoot = FindTestProjectRoot();
        var violations = new List<string>();

        foreach (var file in Directory.EnumerateFiles(testProjectRoot, "*.cs", SearchOption.AllDirectories))
        {
            if (IsGeneratedOrBuildOutput(file))
            {
                continue;
            }

            var text = File.ReadAllText(file);
            foreach (Match match in SkipAttributePattern.Matches(text))
            {
                var reason = match.Groups["reason"].Value.Replace("\\\"", "\"", StringComparison.Ordinal);
                var line = 1 + text.Take(match.Index).Count(ch => ch == '\n');
                var relativePath = Path.GetRelativePath(testProjectRoot, file);

                if (!reason.Contains("owner=", StringComparison.OrdinalIgnoreCase))
                {
                    violations.Add($"{relativePath}:{line} skip reason must include owner=...");
                }

                var expiryMatch = ExpiryPattern.Match(reason);
                if (!expiryMatch.Success)
                {
                    violations.Add($"{relativePath}:{line} skip reason must include expires=yyyy-MM-dd");
                    continue;
                }

                if (!DateOnly.TryParse(expiryMatch.Groups["date"].Value, out var expiresAt))
                {
                    violations.Add($"{relativePath}:{line} skip expiry is not a valid date");
                    continue;
                }

                if (expiresAt < DateOnly.FromDateTime(DateTime.UtcNow))
                {
                    violations.Add($"{relativePath}:{line} skip expiry {expiresAt:yyyy-MM-dd} is in the past");
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            "Permanent xUnit skips are not allowed. " + string.Join(Environment.NewLine, violations));
    }

    private static string FindTestProjectRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "ClearVision.Product.Tests.csproj")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Unable to locate ClearVision.Product.Tests project root.");
    }

    private static bool IsGeneratedOrBuildOutput(string file)
    {
        var normalized = file.Replace('\\', '/');
        return normalized.Contains("/bin/", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("/obj/", StringComparison.OrdinalIgnoreCase);
    }
}
