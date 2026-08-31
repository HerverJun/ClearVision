namespace ClearVision.Product.Application.Services;

/// <summary>
/// Shared, server-enforced budget for the result analysis surface. The limit is kept
/// deliberately small enough that a dashboard refresh has predictable database, CPU,
/// and response costs even for long-lived projects.
/// </summary>
public static class ResultAnalysisQueryBudget
{
    public const int MaximumWindowDays = 31;
    public const int MaximumTrendPoints = MaximumWindowDays * 24;
    public const int MaximumTrendRows = 25_000;

    private static readonly TimeSpan MaximumWindow = TimeSpan.FromDays(MaximumWindowDays);

    public static ResultAnalysisWindow Normalize(
        DateTime? startTime,
        DateTime? endTime,
        DateTime utcNow)
    {
        var normalizedEnd = endTime ?? utcNow;
        var normalizedStart = startTime ?? SafeAdd(normalizedEnd, -MaximumWindow);
        return Validate(normalizedStart, normalizedEnd);
    }

    public static ResultAnalysisWindow Validate(DateTime startTime, DateTime endTime)
    {
        if (startTime > endTime)
        {
            throw new ResultAnalysisBudgetException(
                "ANALYSIS_TIME_RANGE_INVALID",
                "The analysis start time must be earlier than or equal to the end time.");
        }

        if (endTime - startTime > MaximumWindow)
        {
            throw new ResultAnalysisBudgetException(
                "ANALYSIS_TIME_RANGE_LIMIT",
                $"Analysis requests may span at most {MaximumWindowDays} days.");
        }

        return new ResultAnalysisWindow(startTime, endTime);
    }

    public static IReadOnlyList<DateTime> BuildTrendBuckets(
        TrendInterval interval,
        DateTime startTime,
        DateTime endTime)
    {
        Validate(startTime, endTime);

        var buckets = new List<DateTime>();
        var current = startTime;
        while (current < endTime)
        {
            if (buckets.Count >= MaximumTrendPoints)
            {
                throw new ResultAnalysisBudgetException(
                    "ANALYSIS_TREND_POINT_LIMIT",
                    $"Analysis trends may contain at most {MaximumTrendPoints} points.");
            }

            buckets.Add(current);
            var next = Advance(interval, current);
            if (next <= current)
            {
                throw new ResultAnalysisBudgetException(
                    "ANALYSIS_TREND_RANGE_INVALID",
                    "The analysis trend interval could not advance safely.");
            }

            current = next;
        }

        return buckets;
    }

    private static DateTime SafeAdd(DateTime value, TimeSpan delta)
    {
        try
        {
            return value.Add(delta);
        }
        catch (ArgumentOutOfRangeException)
        {
            return delta < TimeSpan.Zero ? DateTime.MinValue : DateTime.MaxValue;
        }
    }

    private static DateTime Advance(TrendInterval interval, DateTime current)
    {
        try
        {
            return interval switch
            {
                TrendInterval.Hour => current.AddHours(1),
                TrendInterval.Day => current.AddDays(1),
                TrendInterval.Week => current.AddDays(7),
                TrendInterval.Month => current.AddMonths(1),
                _ => throw new ResultAnalysisBudgetException(
                    "ANALYSIS_TREND_INTERVAL_INVALID",
                    $"Unsupported trend interval '{interval}'.")
            };
        }
        catch (ArgumentOutOfRangeException)
        {
            throw new ResultAnalysisBudgetException(
                "ANALYSIS_TREND_RANGE_INVALID",
                "The analysis trend range exceeds the supported DateTime range.");
        }
    }
}

public sealed record ResultAnalysisWindow(DateTime StartTime, DateTime EndTime);

public sealed class ResultAnalysisBudgetException : ArgumentException
{
    public ResultAnalysisBudgetException(string errorCode, string message)
        : base(message)
    {
        ErrorCode = errorCode;
    }

    public string ErrorCode { get; }
}
