using System.Text.Json.Serialization;
using ClearVision.Product.Core.Services;

namespace ClearVision.Product.Infrastructure.AI;

public sealed class OperatorKnowledgeParameter
{
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string DataType { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? DefaultValue { get; set; }
    public string? MinValue { get; set; }
    public string? MaxValue { get; set; }
    public bool IsRequired { get; set; }
    public List<string> AllowedValues { get; set; } = new();
}

public sealed class OperatorKnowledgePort
{
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string DataType { get; set; } = string.Empty;
    public bool IsRequired { get; set; }
    public string? Description { get; set; }
}

public sealed class OperatorKnowledgeEvidence
{
    public string Contract { get; set; } = string.Empty;
    public string Golden { get; set; } = string.Empty;
    public string Dataset { get; set; } = string.Empty;
    public string FieldReplay { get; set; } = string.Empty;
    public string PrecisionClaim { get; set; } = string.Empty;
    public string IndustrialStatus { get; set; } = string.Empty;
    public string? QScore { get; set; }
}

public sealed class OperatorKnowledgeCard
{
    public string SchemaVersion { get; set; } = "2026-07.operator-knowledge-card.v2";
    public string OperatorType { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string CategoryId { get; set; } = string.Empty;
    public int CategoryOrder { get; set; }
    public string Category { get; set; } = string.Empty;
    public string Lifecycle { get; set; } = string.Empty;
    public string? LifecycleNote { get; set; }
    public bool DefaultHidden { get; set; }
    public bool DefaultAiRecommendation { get; set; }
    public bool RequiresLifecycleDisclosure { get; set; }
    public OperatorQualityState QualityState { get; set; } = OperatorQualityState.Unknown;
    public List<string> Aliases { get; set; } = new();
    public List<string> IntentTags { get; set; } = new();
    public List<string> ScenarioTags { get; set; } = new();
    public List<OperatorKnowledgePort> Inputs { get; set; } = new();
    public List<OperatorKnowledgePort> Outputs { get; set; } = new();
    public List<OperatorKnowledgeParameter> Parameters { get; set; } = new();
    public List<OperatorParameterConstraint> ParameterConditions { get; set; } = new();
    public List<OperatorOutputAvailabilityRule> OutputConditions { get; set; } = new();
    [JsonIgnore]
    public List<ImageInputContract> ImageInputContracts { get; set; } = new();

    [JsonPropertyName("ImageInputContracts")]
    public List<ImageInputContractPresentation> ImageInputContractPresentations { get; set; } = new();
    public List<OperatorKnowledgeResourceRequirement> ResourceRequirements { get; set; } = new();
    public List<string> GenerationDependencies { get; set; } = new();
    public string GenerationFingerprint { get; set; } = string.Empty;
    public List<string> RequiredResources { get; set; } = new();
    public List<string> TypicalUpstream { get; set; } = new();
    public List<string> TypicalDownstream { get; set; } = new();
    public List<string> AntiPatterns { get; set; } = new();
    public List<string> KnownLimitations { get; set; } = new();
    public OperatorKnowledgeEvidence Evidence { get; set; } = new();
}

public sealed class OperatorKnowledgeResourceRequirement
{
    public string Parameter { get; set; } = string.Empty;
    public string ResourceKind { get; set; } = string.Empty;
    public string ReasonCode { get; set; } = string.Empty;
    public string? AtLeastOneGroup { get; set; }
    public OperatorParameterConditionSet? RequiredWhen { get; set; }
}

public sealed class OperatorKnowledgeEdge
{
    public string RelationType { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string Target { get; set; } = string.Empty;
    public double Weight { get; set; } = 1.0;
    public Dictionary<string, string> Metadata { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class OperatorKnowledgeGraph
{
    public string SchemaVersion { get; set; } = "2026-07.operator-knowledge-graph.v4";
    public DateTime GeneratedAtUtc { get; set; } = DateTime.UtcNow;
    public string Source { get; set; } = "OperatorMetadata + FlowTemplate + operator_quality_evidence_manifest";
    public string GenerationFingerprint { get; set; } = string.Empty;
    public List<OperatorKnowledgeCard> Cards { get; set; } = new();
    public List<OperatorKnowledgeEdge> Edges { get; set; } = new();
}

public sealed class OperatorKnowledgeQuery
{
    public string? Description { get; set; }
    public string? AdditionalContext { get; set; }
    public IReadOnlyList<string>? AttachmentNames { get; set; }
    public IReadOnlyList<string>? ScenarioHints { get; set; }
    public int TopN { get; set; } = 24;
}

public sealed class OperatorKnowledgeSlice
{
    public List<string> PrioritizedOperatorTypes { get; set; } = new();
    public List<OperatorKnowledgeCard> Cards { get; set; } = new();
    public List<string> MatchedScenarioKeys { get; set; } = new();
    public string RetrievalSummary { get; set; } = string.Empty;

    [JsonIgnore]
    public bool IsEmpty => PrioritizedOperatorTypes.Count == 0;
}
