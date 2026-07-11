using ClearVision.Product.Core.Outcomes;
using ClearVision.Product.Runtime.Abstractions;
using System.Text.Json.Serialization;

namespace ClearVision.Product.Desktop.Station;

/// <summary>
/// Canonical Station statistics shared by the central database path and the in-memory
/// fallback. Compatibility aliases are intentionally derived from canonical counters.
/// </summary>
public sealed class StationResultStatisticsViewModel
{
    public DateTimeOffset? FromUtc { get; init; }
    public DateTimeOffset? ToUtc { get; init; }
    public InspectionOutcomeStatistics OutcomeStatistics { get; init; } = new();
    [JsonPropertyName("totalAttemptCount")] public int TotalAttemptCount => OutcomeStatistics.TotalAttemptCount;
    [JsonPropertyName("executionSucceededCount")] public int ExecutionSucceededCount => OutcomeStatistics.ExecutionSucceededCount;
    [JsonPropertyName("validDecisionCount")] public int ValidDecisionCount => OutcomeStatistics.ValidDecisionCount;
    [JsonPropertyName("okCount")] public int OkCount => OutcomeStatistics.OkCount;
    [JsonPropertyName("ngCount")] public int NgCount => OutcomeStatistics.NgCount;
    [JsonPropertyName("undeterminedCount")] public int UndeterminedCount => OutcomeStatistics.UndeterminedCount;
    [JsonPropertyName("notApplicableCount")] public int NotApplicableCount => OutcomeStatistics.NotApplicableCount;
    [JsonPropertyName("invalidCount")] public int InvalidCount => OutcomeStatistics.InvalidCount;
    [JsonPropertyName("failedCount")] public int FailedCount => OutcomeStatistics.FailedCount;
    [JsonPropertyName("cancelledCount")] public int CancelledCount => OutcomeStatistics.CancelledCount;
    [JsonPropertyName("timedOutCount")] public int TimedOutCount => OutcomeStatistics.TimedOutCount;
    [JsonPropertyName("skippedCount")] public int SkippedCount => OutcomeStatistics.SkippedCount;
    [JsonPropertyName("executionFailureCount")] public int ExecutionFailureCount => OutcomeStatistics.ExecutionFailureCount;
    [JsonPropertyName("yieldRate")] public double YieldRate => OutcomeStatistics.YieldRate;
    [JsonPropertyName("decisionCoverageRate")] public double DecisionCoverageRate => OutcomeStatistics.DecisionCoverageRate;
    public double AverageExecutionTimeMs { get; init; }
    public IReadOnlyList<StationOutcomeBreakdownViewModel> ByStation { get; init; } = [];
    public IReadOnlyList<StationDiagnosticBreakdownViewModel> ByDiagnosticCode { get; init; } = [];
    public IReadOnlyList<StationOutcomeTrendViewModel> HourlyTrend { get; init; } = [];
    public StationDiagnosticDistributionViewModel DefectDistribution => new() { Items = ByDiagnosticCode };
    public StationTrendDistributionViewModel Trend => new() { DataPoints = HourlyTrend };

    // Legacy API aliases. New clients must use the canonical fields above.
    [JsonPropertyName("totalCount")] public int TotalCount => TotalAttemptCount;
    [JsonPropertyName("ok")] public int Ok => OkCount;
    [JsonPropertyName("ng")] public int Ng => NgCount;
    [JsonPropertyName("errorCount")] public int ErrorCount => ExecutionFailureCount;
    [JsonPropertyName("error")] public int Error => ExecutionFailureCount;
    [JsonPropertyName("okRate")] public double OkRate => YieldRate;
    [JsonPropertyName("averageProcessingTimeMs")] public double AverageProcessingTimeMs => AverageExecutionTimeMs;
}

public sealed class StationOutcomeBreakdownViewModel
{
    public string StationId { get; init; } = string.Empty;
    public InspectionOutcomeStatistics OutcomeStatistics { get; init; } = new();
    public int TotalAttemptCount => OutcomeStatistics.TotalAttemptCount;
    public int ExecutionSucceededCount => OutcomeStatistics.ExecutionSucceededCount;
    public int ValidDecisionCount => OutcomeStatistics.ValidDecisionCount;
    public int OkCount => OutcomeStatistics.OkCount;
    public int NgCount => OutcomeStatistics.NgCount;
    public int UndeterminedCount => OutcomeStatistics.UndeterminedCount;
    public int NotApplicableCount => OutcomeStatistics.NotApplicableCount;
    public int InvalidCount => OutcomeStatistics.InvalidCount;
    public int FailedCount => OutcomeStatistics.FailedCount;
    public int CancelledCount => OutcomeStatistics.CancelledCount;
    public int TimedOutCount => OutcomeStatistics.TimedOutCount;
    public int SkippedCount => OutcomeStatistics.SkippedCount;
    public int ExecutionFailureCount => OutcomeStatistics.ExecutionFailureCount;
    public double YieldRate => OutcomeStatistics.YieldRate;
    public double DecisionCoverageRate => OutcomeStatistics.DecisionCoverageRate;
    public double AverageExecutionTimeMs { get; init; }

    // Legacy aliases.
    public int TotalCount => TotalAttemptCount;
    public int Ok => OkCount;
    public int Ng => NgCount;
    public int ErrorCount => ExecutionFailureCount;
}

public sealed class StationDiagnosticBreakdownViewModel
{
    public string DiagnosticCode { get; init; } = "Unknown";
    public string DefectType => DiagnosticCode;
    public int Count { get; init; }
}

public sealed class StationOutcomeTrendViewModel
{
    public DateTimeOffset HourUtc { get; init; }
    public InspectionOutcomeStatistics OutcomeStatistics { get; init; } = new();
    public int TotalAttemptCount => OutcomeStatistics.TotalAttemptCount;
    public int ExecutionSucceededCount => OutcomeStatistics.ExecutionSucceededCount;
    public int ValidDecisionCount => OutcomeStatistics.ValidDecisionCount;
    public int OkCount => OutcomeStatistics.OkCount;
    public int NgCount => OutcomeStatistics.NgCount;
    public int UndeterminedCount => OutcomeStatistics.UndeterminedCount;
    public int NotApplicableCount => OutcomeStatistics.NotApplicableCount;
    public int InvalidCount => OutcomeStatistics.InvalidCount;
    public int FailedCount => OutcomeStatistics.FailedCount;
    public int CancelledCount => OutcomeStatistics.CancelledCount;
    public int TimedOutCount => OutcomeStatistics.TimedOutCount;
    public int SkippedCount => OutcomeStatistics.SkippedCount;
    public int ExecutionFailureCount => OutcomeStatistics.ExecutionFailureCount;
    public double YieldRate => OutcomeStatistics.YieldRate;

    // Legacy aliases.
    public DateTimeOffset Timestamp => HourUtc;
    public int TotalCount => TotalAttemptCount;
    public int Ok => OkCount;
    public int Ng => NgCount;
    public int ErrorCount => ExecutionFailureCount;
    public int DefectCount => NgCount;
}

public sealed class StationDiagnosticDistributionViewModel
{
    public IReadOnlyList<StationDiagnosticBreakdownViewModel> Items { get; init; } = [];
}

public sealed class StationTrendDistributionViewModel
{
    public IReadOnlyList<StationOutcomeTrendViewModel> DataPoints { get; init; } = [];
}

internal static class StationOutcomeStatisticsBuilder
{
    public static StationResultStatisticsViewModel Build(
        IEnumerable<StationResultSummaryDto> results,
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc)
    {
        var materialized = results.ToList();
        var statistics = Calculate(materialized);
        return new StationResultStatisticsViewModel
        {
            FromUtc = fromUtc,
            ToUtc = toUtc,
            OutcomeStatistics = statistics,
            AverageExecutionTimeMs = materialized.Count == 0
                ? 0
                : materialized.Average(item => item.ExecutionTimeMs),
            ByStation = materialized
                .GroupBy(item => item.StationId, StringComparer.OrdinalIgnoreCase)
                .Select(group => new StationOutcomeBreakdownViewModel
                {
                    StationId = group.Key,
                    OutcomeStatistics = Calculate(group),
                    AverageExecutionTimeMs = group.Average(item => item.ExecutionTimeMs)
                })
                .OrderByDescending(item => item.TotalAttemptCount)
                .ThenBy(item => item.StationId, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            ByDiagnosticCode = materialized
                .GroupBy(
                    item => string.IsNullOrWhiteSpace(item.DiagnosticCode) ? "Unknown" : item.DiagnosticCode,
                    StringComparer.OrdinalIgnoreCase)
                .Select(group => new StationDiagnosticBreakdownViewModel
                {
                    DiagnosticCode = group.Key,
                    Count = group.Count()
                })
                .OrderByDescending(item => item.Count)
                .ThenBy(item => item.DiagnosticCode, StringComparer.OrdinalIgnoreCase)
                .Take(20)
                .ToList(),
            HourlyTrend = materialized
                .GroupBy(item => new DateTimeOffset(
                    item.CompletedAtUtc.Year,
                    item.CompletedAtUtc.Month,
                    item.CompletedAtUtc.Day,
                    item.CompletedAtUtc.Hour,
                    0,
                    0,
                    TimeSpan.Zero))
                .Select(group => new StationOutcomeTrendViewModel
                {
                    HourUtc = group.Key,
                    OutcomeStatistics = Calculate(group)
                })
                .OrderBy(item => item.HourUtc)
                .ToList()
        };
    }

    public static InspectionOutcomeStatistics Calculate(IEnumerable<StationResultSummaryDto> results)
    {
        return InspectionOutcomeStatistics.Calculate(results.Select(StationCanonicalOutcomeProjection.Resolve));
    }

    public static InspectionOutcomeStatistics Combine(IEnumerable<InspectionOutcomeStatistics> statistics)
    {
        var source = statistics.ToList();
        return new InspectionOutcomeStatistics
        {
            TotalAttemptCount = source.Sum(item => item.TotalAttemptCount),
            ExecutionSucceededCount = source.Sum(item => item.ExecutionSucceededCount),
            ValidDecisionCount = source.Sum(item => item.ValidDecisionCount),
            OkCount = source.Sum(item => item.OkCount),
            NgCount = source.Sum(item => item.NgCount),
            UndeterminedCount = source.Sum(item => item.UndeterminedCount),
            NotApplicableCount = source.Sum(item => item.NotApplicableCount),
            InvalidCount = source.Sum(item => item.InvalidCount),
            FailedCount = source.Sum(item => item.FailedCount),
            CancelledCount = source.Sum(item => item.CancelledCount),
            TimedOutCount = source.Sum(item => item.TimedOutCount),
            SkippedCount = source.Sum(item => item.SkippedCount)
        };
    }

    public static InspectionOutcomeStatistics ProjectLegacySession(int okCount, int ngCount, int errorCount)
    {
        var ok = Math.Max(0, okCount);
        var ng = Math.Max(0, ngCount);
        var failed = Math.Max(0, errorCount);
        return new InspectionOutcomeStatistics
        {
            TotalAttemptCount = ok + ng + failed,
            ExecutionSucceededCount = ok + ng,
            ValidDecisionCount = ok + ng,
            OkCount = ok,
            NgCount = ng,
            FailedCount = failed
        };
    }

    public static bool MatchesStatus(StationResultSummaryDto result, string? requestedStatus)
    {
        if (string.IsNullOrWhiteSpace(requestedStatus) ||
            string.Equals(requestedStatus, "all", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var normalized = NormalizeToken(requestedStatus);
        var outcome = StationCanonicalOutcomeProjection.Resolve(result);
        var category = InspectionOutcomeClassifier.Classify(outcome).ToString();
        if (NormalizeToken(category) == normalized)
        {
            return true;
        }

        // Legacy Error filters only select execution failures. Invalid is deliberately
        // excluded so it is not silently presented as a runtime failure.
        return normalized == "error" && outcome.Execution is ExecutionOutcome.Failed or ExecutionOutcome.TimedOut;
    }

    private static string NormalizeToken(string value)
    {
        return new string(value.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
    }
}
