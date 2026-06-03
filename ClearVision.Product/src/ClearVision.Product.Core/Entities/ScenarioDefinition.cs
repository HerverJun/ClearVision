namespace ClearVision.Product.Core.Entities;

public sealed class ScenarioDefinition
{
    public string ScenarioKey { get; set; } = string.Empty;
    public string ScenarioName { get; set; } = string.Empty;
    public string Industry { get; set; } = string.Empty;
    public List<string> Keywords { get; set; } = new();
    public List<string> Synonyms { get; set; } = new();
    public List<string> NegativeKeywords { get; set; } = new();
    public List<string> IntentTypes { get; set; } = new();
    public List<string> ObjectTypes { get; set; } = new();
    public List<string> DefectTypes { get; set; } = new();
    public List<string> MeasurementTargets { get; set; } = new();
    public List<string> RequiredResources { get; set; } = new();
    public string? TemplateId { get; set; }
    public string TemplateName { get; set; } = string.Empty;
    public string TemplateVersion { get; set; } = "1.0.0";
}

public sealed class ScenarioMatchResult
{
    public ScenarioDefinition Scenario { get; set; } = new();
    public FlowTemplate? Template { get; set; }
    public double Confidence { get; set; }
    public string MatchReason { get; set; } = string.Empty;
    public List<string> MatchedFields { get; set; } = new();
    public List<string> MissingSignals { get; set; } = new();
}
