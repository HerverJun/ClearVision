using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Outcomes;

namespace ClearVision.Product.Core.Interfaces;

/// <summary>
/// Optional capability exposed by result repositories that can serve bounded analysis
/// projections without materializing inspection payloads or defect collections.
/// </summary>
public interface IInspectionResultAnalysisRepository
{
    Task<IReadOnlyList<InspectionAnalysisSample>> GetAnalysisSamplesAsync(
        InspectionAnalysisQuery query,
        int maxRows);

    Task<InspectionConfidenceSummary> GetConfidenceSummaryAsync(InspectionAnalysisQuery query);
}

/// <summary>
/// Immutable server-side filter used by the bounded analysis projections.
/// </summary>
public sealed record InspectionAnalysisQuery(
    Guid ProjectId,
    DateTime StartTime,
    DateTime EndTime,
    string? Status,
    string? DefectType);

/// <summary>
/// Compact result projection required for a trend bucket. It deliberately excludes
/// image, output, analysis, and defect payloads.
/// </summary>
public sealed record InspectionAnalysisSample(
    DateTime InspectionTime,
    InspectionStatus Status,
    ExecutionOutcome? ExecutionOutcome,
    DecisionOutcome? DecisionOutcome,
    bool? HasJudgmentSignal,
    long ProcessingTimeMs,
    int DefectCount)
{
    public InspectionOutcome ToOutcome() =>
        ExecutionOutcome.HasValue && DecisionOutcome.HasValue
            ? new InspectionOutcome(
                ExecutionOutcome.Value,
                DecisionOutcome.Value,
                null,
                null,
                null,
                HasJudgmentSignal ??
                (ExecutionOutcome.Value == global::ClearVision.Product.Core.Outcomes.ExecutionOutcome.Succeeded &&
                 DecisionOutcome.Value is global::ClearVision.Product.Core.Outcomes.DecisionOutcome.Ok or global::ClearVision.Product.Core.Outcomes.DecisionOutcome.Ng))
            : LegacyInspectionStatusProjection.FromLegacy(Status);
}

/// <summary>
/// Database aggregate for the six fixed confidence buckets.
/// </summary>
public sealed record InspectionConfidenceSummary(
    int NinetyToOneHundred,
    int EightyToNinety,
    int SeventyToEighty,
    int SixtyToSeventy,
    int FiftyToSixty,
    int BelowFifty,
    int TotalDefects,
    double AverageConfidence);
