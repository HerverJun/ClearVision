using System.Text.Json;
using ClearVision.Product.Core.Services;

namespace ClearVision.OperatorLibrary.ReadOnlyAudit;

public static class AuditBaselinePaths
{
    public const string Review = "quality/evals/baselines/operator-audit-review.json";
    public const string Confirmed = "quality/evals/baselines/operator-audit-confirmed-baseline.json";
}

public sealed record AuditReviewEntry(
    string ReviewId,
    string FindingCode,
    string Operator,
    string Field,
    string ReviewStatus,
    string StaticDifferenceStatus,
    string ProductionReachability,
    string Verdict,
    IReadOnlyList<string> Evidence,
    string Reason);

public sealed record AuditReviewSummary(
    int SampleCount,
    int ReviewedCount,
    int StaticDifferenceCount,
    int ResolvedDifferenceCount,
    int ProductionReachableCount,
    int FixedProductionDefectCount,
    int OpenProductionDefectCount,
    int CandidateCount,
    int IntentionalDifferenceCount,
    int AuditFalsePositiveCount,
    IReadOnlyList<string> Categories);

public sealed record ConfirmedFindingBaselineEntry(
    string Code,
    string Operator,
    string Field,
    string Reason,
    string Status);

public sealed record ConfirmedFindingGateResult(
    IReadOnlyList<ConfirmedFindingBaselineEntry> Baseline,
    IReadOnlyList<AuditFinding> NewConfirmedFindings);

public static class AuditBaselineStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly IReadOnlySet<string> ReviewStatuses =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "reviewed",
            "unreviewed"
        };

    private static readonly IReadOnlySet<string> Verdicts =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "fixed-production-defect",
            "open-production-defect",
            "candidate",
            "intentional-difference",
            "audit-false-positive"
        };

    private static readonly IReadOnlySet<string> StaticDifferenceStatuses =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "present",
            "resolved"
        };

    private static readonly IReadOnlySet<string> ProductionReachabilities =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "reachable",
            "not-proven",
            "not-applicable"
        };

    public static IReadOnlyList<AuditReviewEntry> ReadReviewBaseline(
        string repoRoot,
        IReadOnlyList<AuditFinding> findings,
        IReadOnlyList<OperatorMetadata> metadata)
    {
        var path = Path.Combine(repoRoot, AuditBaselinePaths.Review.Replace('/', Path.DirectorySeparatorChar));
        var entries = ReadArray<AuditReviewEntry>(path, "review baseline");
        if (entries.Count < 15)
        {
            throw new InvalidDataException("Operator audit review baseline must contain at least 15 entries.");
        }

        var categories = metadata
            .GroupBy(item => item.Type.ToString(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => string.IsNullOrWhiteSpace(group.First().Category) ? "uncategorized" : group.First().Category,
                StringComparer.OrdinalIgnoreCase);
        var findingIdentities = findings
            .Select(FindingIdentity)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var errors = new List<string>();

        foreach (var duplicate in entries
                     .GroupBy(item => item.ReviewId, StringComparer.OrdinalIgnoreCase)
                     .Where(group => group.Count() > 1))
        {
            errors.Add($"duplicate reviewId:{duplicate.Key}");
        }

        foreach (var entry in entries)
        {
            if (string.IsNullOrWhiteSpace(entry.ReviewId) ||
                string.IsNullOrWhiteSpace(entry.FindingCode) ||
                string.IsNullOrWhiteSpace(entry.Operator) ||
                string.IsNullOrWhiteSpace(entry.Field) ||
                entry.Evidence is not { Count: > 0 } ||
                entry.Evidence.Any(string.IsNullOrWhiteSpace) ||
                string.IsNullOrWhiteSpace(entry.Reason))
            {
                errors.Add($"invalid review shape:{entry.ReviewId}");
            }

            if (!ReviewStatuses.Contains(entry.ReviewStatus))
            {
                errors.Add($"invalid reviewStatus:{entry.ReviewId}");
            }

            if (!StaticDifferenceStatuses.Contains(entry.StaticDifferenceStatus))
            {
                errors.Add($"invalid staticDifferenceStatus:{entry.ReviewId}");
            }

            if (!ProductionReachabilities.Contains(entry.ProductionReachability))
            {
                errors.Add($"invalid productionReachability:{entry.ReviewId}");
            }

            if (!Verdicts.Contains(entry.Verdict))
            {
                errors.Add($"invalid verdict:{entry.ReviewId}");
            }

            if (entry.ReviewStatus == "unreviewed" && entry.Verdict != "candidate")
            {
                errors.Add($"unreviewed verdict:{entry.ReviewId}");
            }

            var findingExists = findingIdentities.Contains(FindingIdentity(entry.FindingCode, entry.Operator, entry.Field));
            if (entry.Verdict == "fixed-production-defect")
            {
                if (entry.StaticDifferenceStatus != "resolved" ||
                    entry.ProductionReachability != "reachable" ||
                    findingExists)
                {
                    errors.Add($"invalid fixed production defect:{entry.ReviewId}");
                }
            }
            else
            {
                if (entry.StaticDifferenceStatus != "present" || !findingExists)
                {
                    errors.Add($"review finding missing:{entry.ReviewId}");
                }
            }

            if (entry.Verdict == "open-production-defect" && entry.ProductionReachability != "reachable")
            {
                errors.Add($"open production defect must be reachable:{entry.ReviewId}");
            }
        }

        var reviewedCategories = entries
            .Where(item => item.ReviewStatus == "reviewed")
            .Select(item => categories.GetValueOrDefault(item.Operator, "audit-boundary"))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (reviewedCategories.Length < 6)
        {
            errors.Add("reviewed category coverage must be at least 6");
        }

        if (errors.Count > 0)
        {
            throw new InvalidDataException("Invalid operator audit review baseline: " + string.Join("; ", errors));
        }

        return entries
            .OrderBy(item => item.ReviewId, StringComparer.Ordinal)
            .ToArray();
    }

    public static AuditReviewSummary SummarizeReviews(
        IReadOnlyList<AuditReviewEntry> entries,
        IReadOnlyList<OperatorMetadata> metadata)
    {
        var categories = metadata
            .GroupBy(item => item.Type.ToString(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => string.IsNullOrWhiteSpace(group.First().Category) ? "uncategorized" : group.First().Category,
                StringComparer.OrdinalIgnoreCase);
        var reviewed = entries.Where(item => item.ReviewStatus == "reviewed").ToArray();
        return new AuditReviewSummary(
            entries.Count,
            reviewed.Length,
            reviewed.Count(item => item.StaticDifferenceStatus == "present"),
            reviewed.Count(item => item.StaticDifferenceStatus == "resolved"),
            reviewed.Count(item => item.ProductionReachability == "reachable"),
            reviewed.Count(item => item.Verdict == "fixed-production-defect"),
            reviewed.Count(item => item.Verdict == "open-production-defect"),
            reviewed.Count(item => item.Verdict == "candidate"),
            reviewed.Count(item => item.Verdict == "intentional-difference"),
            reviewed.Count(item => item.Verdict == "audit-false-positive"),
            reviewed
                .Select(item => categories.GetValueOrDefault(item.Operator, "audit-boundary"))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
                .ToArray());
    }

    public static IReadOnlyList<ConfirmedFindingBaselineEntry> ReadConfirmedBaseline(string repoRoot)
    {
        var path = Path.Combine(repoRoot, AuditBaselinePaths.Confirmed.Replace('/', Path.DirectorySeparatorChar));
        return ParseConfirmedBaseline(File.ReadAllText(path), path);
    }

    public static IReadOnlyList<ConfirmedFindingBaselineEntry> ParseConfirmedBaseline(
        string json,
        string source = "confirmed baseline")
    {
        IReadOnlyList<ConfirmedFindingBaselineEntry> entries;
        try
        {
            entries = JsonSerializer.Deserialize<List<ConfirmedFindingBaselineEntry>>(json, JsonOptions) ?? [];
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Malformed {source}: {ex.Message}", ex);
        }

        var errors = new List<string>();
        foreach (var entry in entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Code) ||
                string.IsNullOrWhiteSpace(entry.Operator) ||
                string.IsNullOrWhiteSpace(entry.Field) ||
                string.IsNullOrWhiteSpace(entry.Reason) ||
                entry.Status != "intentional-difference")
            {
                errors.Add($"invalid confirmed baseline entry:{entry.Code}/{entry.Operator}/{entry.Field}");
            }
        }

        foreach (var duplicate in entries
                     .GroupBy(BaselineIdentity, StringComparer.OrdinalIgnoreCase)
                     .Where(group => group.Count() > 1))
        {
            errors.Add($"duplicate confirmed identity:{duplicate.Key}");
        }

        if (errors.Count > 0)
        {
            throw new InvalidDataException($"Malformed {source}: {string.Join("; ", errors)}");
        }

        return entries
            .OrderBy(item => item.Code, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Operator, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Field, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<T> ReadArray<T>(string path, string label)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Operator audit {label} is missing: {path}", path);
        }

        try
        {
            return JsonSerializer.Deserialize<List<T>>(File.ReadAllText(path), JsonOptions) ?? [];
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Malformed operator audit {label}: {ex.Message}", ex);
        }
    }

    internal static string FindingIdentity(AuditFinding finding) =>
        FindingIdentity(finding.Code, finding.Operator, finding.Field);

    internal static string FindingIdentity(string code, string @operator, string field) =>
        $"{code?.Trim() ?? string.Empty}\u001f{@operator?.Trim() ?? string.Empty}\u001f{field?.Trim() ?? string.Empty}";

    internal static string BaselineIdentity(ConfirmedFindingBaselineEntry entry) =>
        FindingIdentity(entry.Code, entry.Operator, entry.Field);
}

public static class ConfirmedFindingGate
{
    public static ConfirmedFindingGateResult Evaluate(
        IReadOnlyList<AuditFinding> findings,
        IReadOnlyList<ConfirmedFindingBaselineEntry> baseline)
    {
        var baselineIdentities = baseline
            .Select(AuditBaselineStore.BaselineIdentity)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var newConfirmed = findings
            .Where(item => item.Classification == "confirmed")
            .Where(item => !baselineIdentities.Contains(AuditBaselineStore.FindingIdentity(item)))
            .OrderBy(item => item.Code, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Operator, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Field, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return new ConfirmedFindingGateResult(baseline, newConfirmed);
    }
}
