using ClearVision.Product.Core.Enums;

namespace ClearVision.Product.Core.Interfaces;

public sealed class InspectionHistoryComparison
{
    public InspectionHistoryComparisonSummary LeftSummary { get; init; } = new();

    public InspectionHistoryComparisonSummary RightSummary { get; init; } = new();

    public InspectionHistoryCompatibility Compatibility { get; init; } = new();

    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

    public IReadOnlyList<InspectionHistoryFieldDiff> FieldDiffs { get; init; } = Array.Empty<InspectionHistoryFieldDiff>();

    public IReadOnlyList<InspectionHistoryFieldDiff> TraceabilityDiff { get; init; } = Array.Empty<InspectionHistoryFieldDiff>();

    public InspectionHistoryReplayAvailability SceneReplayAvailability { get; init; } = new();

    public InspectionHistoryReplayAvailability ImageReplayAvailability { get; init; } = new();
}

public sealed class InspectionHistoryComparisonSummary
{
    public Guid ResultId { get; init; }

    public Guid ProjectId { get; init; }

    public InspectionStatus Status { get; init; }

    public DateTime InspectionTime { get; init; }

    public int DefectCount { get; init; }

    public long ProcessingTimeMs { get; init; }

    public double? ConfidenceScore { get; init; }

    public string? FlowVersionHash { get; init; }

    public string? CalibrationBundleId { get; init; }

    public Guid? SessionId { get; init; }

    public Guid? RunId => SessionId;

    public Guid? ImageId { get; init; }

    public string? ImageReference { get; init; }

    public bool HasImage { get; init; }

    public bool HasOutputData { get; init; }

    public bool HasAnalysisData { get; init; }
}

public sealed class InspectionHistoryCompatibility
{
    public bool FlowVersionCompatible { get; init; } = true;

    public bool CalibrationBundleCompatible { get; init; } = true;

    public bool OnlySafePreviewComparison { get; init; }

    public bool HasUnknownFields { get; init; }
}

public sealed class InspectionHistoryFieldDiff
{
    public string Path { get; init; } = string.Empty;

    public string Label { get; init; } = string.Empty;

    public string? LeftValuePreview { get; init; }

    public string? RightValuePreview { get; init; }

    public string DiffType { get; init; } = "Unknown";

    public string Severity { get; init; } = "info";

    public string? Message { get; init; }
}

public sealed class InspectionHistoryReplayAvailability
{
    public string Kind { get; init; } = string.Empty;

    public string Mode { get; init; } = "summary-only";

    public bool IsAvailable { get; init; }

    public bool LeftAvailable { get; init; }

    public bool RightAvailable { get; init; }

    public string? LeftReference { get; init; }

    public string? RightReference { get; init; }

    public string? LeftSummary { get; init; }

    public string? RightSummary { get; init; }

    public string Message { get; init; } = string.Empty;
}

public sealed class InspectionPreviousSuccessReference
{
    public InspectionHistoryComparisonSummary CurrentSummary { get; init; } = new();

    public InspectionHistoryComparisonSummary? ReferenceSummary { get; init; }

    public bool Found { get; init; }

    public bool IsFlowVersionFallback { get; init; }

    public int QueryLimit { get; init; }

    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

    public string Message { get; init; } = string.Empty;
}
