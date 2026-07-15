namespace ClearVision.Product.Core.ValueObjects;

public static class MeasurementEvidenceProvenance
{
    public const string Heuristic = "Heuristic";
    public const string StatisticalModel = "StatisticalModel";
    public const string CalibratedStatistical = "CalibratedStatistical";
}

public sealed record MeasurementEvidence(
    double Value,
    string Unit,
    string CoordinateFrame,
    double? Sigma,
    IReadOnlyList<double>? Covariance,
    string Provenance,
    string SourceOperator,
    string SourceAlgorithm,
    string SourceParametersFingerprint,
    IReadOnlyList<string> QualityFlags);
