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
    public const string SyntheticBenchmarkEvidence = "SyntheticBenchmarkEvidence";
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

    private static readonly IReadOnlyDictionary<OperatorType, AlgorithmEvidence> AlgorithmEvidenceByType =
        new Dictionary<OperatorType, AlgorithmEvidence>
        {
            [OperatorType.CaliperTool] = new(
                OperatorAlgorithmQuality.SyntheticBenchmarkEvidence,
                new[] { Phase5Benchmark, Phase5Governance },
                "clearvision-operator-precision-synthetic-v1@1.0.0",
                "Caliper candidate measured; formal integration rejected"),
            [OperatorType.CircleMeasurement] = new(
                OperatorAlgorithmQuality.SyntheticBenchmarkValidated,
                new[] { Phase5Benchmark, Phase5Governance },
                "clearvision-operator-precision-synthetic-v1@1.0.0",
                "OrthogonalWelsch opt-in path accepted"),
            [OperatorType.LineMeasurement] = new(
                OperatorAlgorithmQuality.SyntheticBenchmarkValidated,
                new[] { Phase5Benchmark, Phase5Governance },
                "clearvision-operator-precision-synthetic-v1@1.0.0",
                "Welsch opt-in path accepted"),
            [OperatorType.AnomalyDetection] = new(
                OperatorAlgorithmQuality.PublicDatasetEvidence,
                new[] { Phase5Benchmark, "quality/evals/reports/AnomalyDetection_mvtec_baseline.json", Phase5Governance },
                "MVTec-public-baseline + manifest-contract",
                "Traditional default retained; ONNX identity is fail-closed")
        };

    public static OperatorQualityState Resolve(OperatorType type, OperatorLifecycle lifecycle)
    {
        var registeredEvidence = AlgorithmEvidenceByType.GetValueOrDefault(type);
        var algorithmQuality = registeredEvidence?.AlgorithmQuality ?? OperatorAlgorithmQuality.Unknown;
        var evidence = registeredEvidence?.EvidenceRefs ?? Array.Empty<string>();
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

    private sealed record AlgorithmEvidence(
        string AlgorithmQuality,
        IReadOnlyList<string> EvidenceRefs,
        string EvidenceIdentity,
        string Verdict);
}
