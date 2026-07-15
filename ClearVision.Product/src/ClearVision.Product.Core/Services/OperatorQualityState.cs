using ClearVision.Product.Core.Enums;

namespace ClearVision.Product.Core.Services;

public static class OperatorExecutionQuality
{
    public const string Implemented = "Implemented";
    public const string Unknown = "Unknown";
}

public static class OperatorAlgorithmQuality
{
    public const string Unknown = "Unknown";
    public const string SyntheticBenchmarkValidated = "SyntheticBenchmarkValidated";
    public const string PublicDatasetEvidence = "PublicDatasetEvidence";
}

public static class OperatorProductionReadiness
{
    public const string Unknown = "Unknown";
    public const string Experimental = "Experimental";
    public const string Reference = "Reference";
    public const string CompatibilityOnly = "CompatibilityOnly";
    public const string Deprecated = "Deprecated";
}

public static class OperatorFieldValidation
{
    public const string NotValidated = "NotValidated";
}

public sealed record OperatorQualityState(
    string Execution,
    string AlgorithmQuality,
    string ProductionReadiness,
    string FieldValidation,
    IReadOnlyList<string> EvidenceRefs)
{
    public static OperatorQualityState Unknown { get; } = new(
        OperatorExecutionQuality.Unknown,
        OperatorAlgorithmQuality.Unknown,
        OperatorProductionReadiness.Unknown,
        OperatorFieldValidation.NotValidated,
        Array.Empty<string>());
}

public static class OperatorQualityStateCatalog
{
    private const string Phase5Benchmark = "quality/evals/reports/operator-precision-after-acceptance.json";
    private const string Phase5Governance = "docs/operator-quality/operator-quality-phase5-closeout.md";

    public static OperatorQualityState Resolve(OperatorType type, OperatorLifecycle lifecycle)
    {
        var algorithmQuality = type switch
        {
            OperatorType.CaliperTool or OperatorType.CircleMeasurement or OperatorType.LineMeasurement =>
                OperatorAlgorithmQuality.SyntheticBenchmarkValidated,
            OperatorType.AnomalyDetection => OperatorAlgorithmQuality.PublicDatasetEvidence,
            _ => OperatorAlgorithmQuality.Unknown
        };
        var evidence = type switch
        {
            OperatorType.CaliperTool or OperatorType.CircleMeasurement or OperatorType.LineMeasurement =>
                new[] { Phase5Benchmark, Phase5Governance },
            OperatorType.AnomalyDetection =>
                new[] { Phase5Benchmark, "quality/evals/reports/AnomalyDetection_mvtec_baseline.json", Phase5Governance },
            _ => Array.Empty<string>()
        };
        var productionReadiness = lifecycle switch
        {
            OperatorLifecycle.Experimental => OperatorProductionReadiness.Experimental,
            OperatorLifecycle.Reference => OperatorProductionReadiness.Reference,
            OperatorLifecycle.Legacy => OperatorProductionReadiness.CompatibilityOnly,
            OperatorLifecycle.Deprecated => OperatorProductionReadiness.Deprecated,
            _ => OperatorProductionReadiness.Unknown
        };

        return new OperatorQualityState(
            OperatorExecutionQuality.Implemented,
            algorithmQuality,
            productionReadiness,
            OperatorFieldValidation.NotValidated,
            evidence);
    }
}
