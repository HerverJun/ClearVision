using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
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

public sealed record OperatorAlgorithmModeEvidence(
    string ModeId,
    string AlgorithmQuality,
    bool Adopted,
    bool IsDefault,
    string Verdict,
    string ClaimBoundary,
    IReadOnlyList<string> EvidenceRefs);

public sealed record OperatorQualityState(
    string Execution,
    string AlgorithmQuality,
    string ProductionReadiness,
    string FieldValidation,
    IReadOnlyList<string> EvidenceRefs)
{
    public string EvidenceScope { get; init; } = "Operator";
    public string EvidenceIdentity { get; init; } = "none";
    public IReadOnlyList<OperatorAlgorithmModeEvidence> ModeEvidence { get; init; } = Array.Empty<OperatorAlgorithmModeEvidence>();

    public static OperatorQualityState Unknown { get; } = new(
        OperatorExecutionQuality.Unknown,
        OperatorAlgorithmQuality.Unknown,
        OperatorProductionReadiness.Unknown,
        OperatorFieldValidation.NotValidated,
        Array.Empty<string>());
}

public static class OperatorQualityStateCatalog
{
    private const string ResourceName = "ClearVision.Product.Core.Evidence.OperatorQualityPhase5.json";
    private static readonly Lazy<EvidenceRegistry> Evidence = new(LoadEvidenceRegistry, LazyThreadSafetyMode.ExecutionAndPublication);

    public static OperatorQualityState Resolve(OperatorType type, OperatorLifecycle lifecycle)
    {
        var registeredEvidence = Evidence.Value.ByType.GetValueOrDefault(type);
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
            registeredEvidence?.AlgorithmQuality ?? OperatorAlgorithmQuality.Unknown,
            productionReadiness,
            OperatorFieldValidation.NotValidated,
            registeredEvidence?.EvidenceRefs ?? Array.Empty<string>())
        {
            EvidenceScope = registeredEvidence?.EvidenceScope ?? "Operator",
            EvidenceIdentity = registeredEvidence == null
                ? Evidence.Value.ManifestIdentity
                : $"{Evidence.Value.ManifestIdentity}/{registeredEvidence.EvidenceIdentity}",
            ModeEvidence = registeredEvidence?.Modes ?? Array.Empty<OperatorAlgorithmModeEvidence>()
        };
    }

    private static EvidenceRegistry LoadEvidenceRegistry()
    {
        try
        {
            using var stream = typeof(OperatorQualityStateCatalog).Assembly.GetManifestResourceStream(ResourceName)
                ?? throw new InvalidOperationException($"Embedded operator quality evidence resource '{ResourceName}' is missing.");
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            var bytes = memory.ToArray();
            var manifestSha = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            var manifest = JsonSerializer.Deserialize<EvidenceManifest>(bytes, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? throw new InvalidOperationException("Operator quality evidence manifest is empty.");

            if (!string.Equals(manifest.SchemaVersion, "2026-07-16.operator-quality-evidence.v1", StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(manifest.ManifestId) ||
                string.IsNullOrWhiteSpace(manifest.ManifestVersion) ||
                string.IsNullOrWhiteSpace(manifest.ClaimBoundary) ||
                manifest.Artifacts.Count == 0 ||
                manifest.Artifacts.Any(artifact =>
                    string.IsNullOrWhiteSpace(artifact.Id) ||
                    string.IsNullOrWhiteSpace(artifact.Path) ||
                    artifact.Sha256.Length != 64 ||
                    string.IsNullOrWhiteSpace(artifact.Kind)))
            {
                throw new InvalidOperationException("Operator quality evidence manifest header is invalid.");
            }

            var validQualities = new HashSet<string>(StringComparer.Ordinal)
            {
                OperatorAlgorithmQuality.Unknown,
                OperatorAlgorithmQuality.SyntheticBenchmarkEvidence,
                OperatorAlgorithmQuality.SyntheticBenchmarkValidated,
                OperatorAlgorithmQuality.PublicDatasetEvidence
            };
            var byType = new Dictionary<OperatorType, RegisteredEvidence>();
            foreach (var item in manifest.Operators)
            {
                if (!Enum.TryParse<OperatorType>(item.OperatorType, ignoreCase: false, out var type) ||
                    !validQualities.Contains(item.AlgorithmQuality) ||
                    string.IsNullOrWhiteSpace(item.EvidenceScope) ||
                    string.IsNullOrWhiteSpace(item.EvidenceIdentity) ||
                    item.EvidenceRefs.Count == 0 ||
                    item.EvidenceRefs.Any(string.IsNullOrWhiteSpace) ||
                    item.Modes.Any(mode =>
                        string.IsNullOrWhiteSpace(mode.ModeId) ||
                        !validQualities.Contains(mode.AlgorithmQuality) ||
                        string.IsNullOrWhiteSpace(mode.Verdict) ||
                        string.IsNullOrWhiteSpace(mode.ClaimBoundary) ||
                        mode.EvidenceRefs.Count == 0))
                {
                    throw new InvalidOperationException($"Operator quality evidence entry '{item.OperatorType}' is invalid.");
                }

                if (!byType.TryAdd(type, new RegisteredEvidence(
                        item.AlgorithmQuality,
                        item.EvidenceScope,
                        item.EvidenceIdentity,
                        item.EvidenceRefs.ToArray(),
                        item.Modes.Select(mode => new OperatorAlgorithmModeEvidence(
                            mode.ModeId,
                            mode.AlgorithmQuality,
                            mode.Adopted,
                            mode.IsDefault,
                            mode.Verdict,
                            mode.ClaimBoundary,
                            mode.EvidenceRefs.ToArray())).ToArray())))
                {
                    throw new InvalidOperationException($"Duplicate operator quality evidence entry '{item.OperatorType}'.");
                }
            }

            return new EvidenceRegistry(
                $"{manifest.ManifestId}@{manifest.ManifestVersion}:{manifestSha}",
                byType);
        }
        catch
        {
            return new EvidenceRegistry("invalid-evidence-manifest", new Dictionary<OperatorType, RegisteredEvidence>());
        }
    }

    private sealed record EvidenceRegistry(string ManifestIdentity, IReadOnlyDictionary<OperatorType, RegisteredEvidence> ByType);
    private sealed record RegisteredEvidence(
        string AlgorithmQuality,
        string EvidenceScope,
        string EvidenceIdentity,
        IReadOnlyList<string> EvidenceRefs,
        IReadOnlyList<OperatorAlgorithmModeEvidence> Modes);

    private sealed class EvidenceManifest
    {
        public string SchemaVersion { get; set; } = string.Empty;
        public string ManifestId { get; set; } = string.Empty;
        public string ManifestVersion { get; set; } = string.Empty;
        public string ClaimBoundary { get; set; } = string.Empty;
        public List<EvidenceArtifact> Artifacts { get; set; } = [];
        public List<EvidenceOperator> Operators { get; set; } = [];
    }

    private sealed class EvidenceArtifact
    {
        public string Id { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public string Sha256 { get; set; } = string.Empty;
        public string Kind { get; set; } = string.Empty;
    }

    private sealed class EvidenceOperator
    {
        public string OperatorType { get; set; } = string.Empty;
        public string AlgorithmQuality { get; set; } = string.Empty;
        public string EvidenceScope { get; set; } = string.Empty;
        public string EvidenceIdentity { get; set; } = string.Empty;
        public List<string> EvidenceRefs { get; set; } = [];
        public List<EvidenceMode> Modes { get; set; } = [];
    }

    private sealed class EvidenceMode
    {
        public string ModeId { get; set; } = string.Empty;
        public string AlgorithmQuality { get; set; } = string.Empty;
        public bool Adopted { get; set; }
        public bool IsDefault { get; set; }
        public string Verdict { get; set; } = string.Empty;
        public string ClaimBoundary { get; set; } = string.Empty;
        public List<string> EvidenceRefs { get; set; } = [];
    }
}
