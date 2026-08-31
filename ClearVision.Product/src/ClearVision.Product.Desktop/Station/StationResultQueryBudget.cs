namespace ClearVision.Product.Desktop.Station;

/// <summary>
/// Bounds result-monitoring reads independently from the size of the retained Station
/// history. Both the REST surface and central-store path use this contract.
/// </summary>
public static class StationResultQueryBudget
{
    public const int MaximumWindowDays = 31;
    public const int DefaultWindowDays = 7;
    public const int MaximumHourlyTrendPoints = MaximumWindowDays * 24;

    private static readonly TimeSpan MaximumWindow = TimeSpan.FromDays(MaximumWindowDays);

    public static StationResultWindow Normalize(
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc,
        DateTimeOffset utcNow)
    {
        var end = (toUtc ?? utcNow).ToUniversalTime();
        var start = (fromUtc ?? end.AddDays(-DefaultWindowDays)).ToUniversalTime();

        if (fromUtc.HasValue && !toUtc.HasValue)
        {
            end = start.AddDays(DefaultWindowDays);
        }
        else if (!fromUtc.HasValue && toUtc.HasValue)
        {
            start = end.AddDays(-DefaultWindowDays);
        }

        if (start > end)
        {
            throw new StationResultQueryBudgetException(
                "STATION_TIME_RANGE_INVALID",
                "The Station result start time must be earlier than or equal to the end time.");
        }

        if (end - start > MaximumWindow)
        {
            throw new StationResultQueryBudgetException(
                "STATION_TIME_RANGE_LIMIT",
                $"Station result requests may span at most {MaximumWindowDays} days.");
        }

        return new StationResultWindow(start, end);
    }
}

public sealed record StationResultWindow(DateTimeOffset FromUtc, DateTimeOffset ToUtc);

public sealed class StationResultQueryBudgetException : ArgumentException
{
    public StationResultQueryBudgetException(string errorCode, string message)
        : base(message)
    {
        ErrorCode = errorCode;
    }

    public string ErrorCode { get; }
}
