using System.Text.Json.Serialization;

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
    public string OperatorType { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public List<string> Aliases { get; set; } = new();
    public List<string> IntentTags { get; set; } = new();
    public List<string> ScenarioTags { get; set; } = new();
    public List<OperatorKnowledgePort> Inputs { get; set; } = new();
    public List<OperatorKnowledgePort> Outputs { get; set; } = new();
    public List<OperatorKnowledgeParameter> Parameters { get; set; } = new();
    public List<string> RequiredResources { get; set; } = new();
    public List<string> TypicalUpstream { get; set; } = new();
    public List<string> TypicalDownstream { get; set; } = new();
    public List<string> AntiPatterns { get; set; } = new();
    public List<string> KnownLimitations { get; set; } = new();
    public OperatorKnowledgeEvidence Evidence { get; set; } = new();
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
    public string SchemaVersion { get; set; } = "2026-05.operator-knowledge-graph.v1";
    public DateTime GeneratedAtUtc { get; set; } = DateTime.UtcNow;
    public string Source { get; set; } = "OperatorMetadata + FlowTemplate + operator_quality_evidence_manifest";
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
