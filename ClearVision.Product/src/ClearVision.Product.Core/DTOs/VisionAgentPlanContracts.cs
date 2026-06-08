namespace ClearVision.Product.Core.DTOs;

public sealed record VisionAgentPlanModeRequest
{
    public string Description { get; init; } = string.Empty;
    public string? OriginalUserPrompt { get; init; }
    public string? AdditionalContext { get; init; }
    public string? SessionId { get; init; }
    public string? Mode { get; init; }
    public string? CurrentFlowSnapshot { get; init; }
    public string? CurrentResultSnapshot { get; init; }
    public AiTemplateSelectionInfo? TemplateSelection { get; init; }
    public VisionAgentAttachmentSummary AttachmentSummary { get; init; } = new();
    public string? HistorySummary { get; init; }
}

public sealed record VisionAgentPlanModeResult
{
    public string PlanId { get; init; } = string.Empty;
    public string PlanHash { get; init; } = string.Empty;
    public string OriginalUserPrompt { get; init; } = string.Empty;
    public string Goal { get; init; } = string.Empty;
    public string Intent { get; init; } = string.Empty;
    public string Confidence { get; init; } = "medium";
    public List<string> RequirementUnderstanding { get; init; } = [];
    public VisionAgentRecommendedRoute RecommendedRoute { get; init; } = new();
    public List<VisionAgentClarificationQuestion> ClarificationQuestions { get; init; } = [];
    public List<VisionAgentDefaultAssumption> RecommendedDefaults { get; init; } = [];
    public List<string> Risks { get; init; } = [];
    public List<string> AcceptanceCriteria { get; init; } = [];
    public List<string> ExecutablePlan { get; init; } = [];
    public bool CanBuild { get; init; } = true;
    public List<string> BlockingReasons { get; init; } = [];
    public string NextAction { get; init; } = string.Empty;
    public VisionAgentPlanContextSummary ContextSummary { get; init; } = new();
    public string OperatorCatalogVersion { get; init; } = string.Empty;
    public string TemplateCatalogVersion { get; init; } = string.Empty;
    public AiTemplateSelectionInfo? TemplateSelection { get; init; }
    public string StationBoundarySummary { get; init; } = string.Empty;
    public string PlcOutputPolicy { get; init; } = string.Empty;
    public bool MetadataOnly { get; init; } = true;
}

public sealed record VisionAgentRecommendedRoute
{
    public string RouteId { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
    public List<string> Operators { get; init; } = [];
    public string TemplateDecision { get; init; } = string.Empty;
}

public sealed record VisionAgentClarificationQuestion
{
    public string Id { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Why { get; init; } = string.Empty;
    public string DefaultValue { get; init; } = string.Empty;
    public string DefaultAssumption { get; init; } = string.Empty;
    public string Impact { get; init; } = string.Empty;
    public List<VisionAgentClarificationOption> Options { get; init; } = [];
}

public sealed record VisionAgentClarificationOption
{
    public string Value { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
    public bool Recommended { get; init; }
    public string Description { get; init; } = string.Empty;
    public string Impact { get; init; } = string.Empty;
}

public sealed record VisionAgentDefaultAssumption
{
    public string Id { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
    public string Value { get; init; } = string.Empty;
    public string Impact { get; init; } = string.Empty;
}

public sealed record VisionAgentPlanContextSummary
{
    public bool HasCurrentFlow { get; init; }
    public bool HasCurrentResult { get; init; }
    public int AttachmentCount { get; init; }
    public string TemplateSelectionMode { get; init; } = string.Empty;
    public string TemplateId { get; init; } = string.Empty;
    public List<string> ContextKinds { get; init; } = [];
    public List<string> OperatorCatalogTools { get; init; } = [];
}

public sealed record VisionAgentAttachmentSummary
{
    public int Count { get; init; }
    public List<string> ResourceKinds { get; init; } = [];
    public bool PathsRedacted { get; init; } = true;
}

public sealed record VisionAgentBuildFromPlanRequest
{
    public string PlanId { get; init; } = string.Empty;
    public string PlanHash { get; init; } = string.Empty;
    public VisionAgentPlanModeResult? PlanSnapshot { get; init; }
    public Dictionary<string, string> UserSelections { get; init; } =
        new(StringComparer.OrdinalIgnoreCase);
    public List<string> AcceptedDefaults { get; init; } = [];
    public string? CurrentFlowSnapshot { get; init; }
    public AiTemplateSelectionInfo? TemplateSelection { get; init; }
    public VisionAgentAttachmentSummary AttachmentSummary { get; init; } = new();
    public string OperatorCatalogVersion { get; init; } = string.Empty;
    public string StationBoundarySummary { get; init; } = string.Empty;
    public string PlcOutputPolicy { get; init; } = string.Empty;
    public string BuildIntent { get; init; } = "new";
    public string OriginalUserPrompt { get; init; } = string.Empty;
    public bool AcceptedRecommendedDefaults { get; init; }
    public bool MetadataOnly { get; init; } = true;
}
