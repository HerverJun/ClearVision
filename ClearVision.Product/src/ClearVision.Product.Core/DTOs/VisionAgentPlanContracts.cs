namespace ClearVision.Product.Core.DTOs;

public static class AiRequirementMaturity
{
    public const string AbstractGoal = "abstract_goal";
    public const string Ambiguous = "ambiguous";
    public const string Actionable = "actionable";
    public const string ChatOrHelp = "chat_or_help";
    public const string ModifyExistingFlow = "modify_existing_flow";
}

public static class AiVisionTaskTypes
{
    public const string Unknown = "unknown";
    public const string AbstractGoal = "abstract_goal";
    public const string SurfaceDefect = "surface_defect";
    public const string SurfaceOrPoseDefect = "surface_or_pose_defect";
    public const string GeometryMeasurement = "geometry_measurement";
    public const string WireSequence = "wire_sequence";
    public const string CodeRecognition = "code_recognition";
    public const string BarcodeQr = "barcode_qr";
    public const string PresenceAbsence = "presence_absence";
    public const string Classification = "classification";
    public const string AttributeClassification = "attribute_classification";
    public const string TemplateLocation = "template_location";
    public const string PlcOutput = "plc_output";
}

public static class VisionAgentSemanticSources
{
    public const string Model = "model";
    public const string RuleFallback = "rule_fallback";
}

public static class VisionAgentSemanticFailureCodes
{
    public const string ModelRequestFailed = "semantic_model_request_failed";
    public const string ModelEmpty = "semantic_model_empty";
    public const string JsonParseFailed = "semantic_json_parse_failed";
    public const string Timeout = "semantic_timeout";
    public const string Unauthorized = "semantic_unauthorized";
    public const string UnknownError = "semantic_unknown_error";
}

public static class VisionAgentPlanContractVersions
{
    public const string V1 = "v1";
    public const string V2 = "v2";
}

public static class VisionAgentPlanAnswerFields
{
    public const string InspectionObject = "inspection_object";
    public const string TaskType = "task_type";
    public const string ImageSource = "image_source";
    public const string AcceptanceCriteria = "acceptance_criteria";
    public const string OutputTarget = "output_target";
    public const string TargetAttribute = "target_attribute";
    public const string DefectType = "defect_type";
    public const string MeasurementTarget = "measurement_target";

    public const string AlgorithmStrategy = "algorithm_strategy";
    public const string RoiStrategy = "roi_strategy";
    public const string TemplateStrategy = "template_strategy";
}

public static class VisionAgentPlanAnswerOrigins
{
    public const string ExplicitUserSelection = "explicit_user_selection";
    public const string AcceptedRecommendedDefault = "accepted_recommended_default";
    public const string ExplicitUserText = "explicit_user_text";
    public const string LegacyInferred = "legacy_inferred";
    public const string ResourceBound = "resource_bound";
    public const string ModelInferred = "model_inferred";
    public const string DefaultAssumption = "default_assumption";
}

public static class VisionAgentBuildBlockerCategories
{
    public const string HardRequirement = "hard_requirement";
    public const string StrategyConfirmation = "strategy_confirmation";
    public const string ResourcePending = "resource_pending";
    public const string ContractWarning = "contract_warning";
    public const string SafetyBlocker = "safety_blocker";
}

public static class VisionAgentBuildBlockerResolutionModes
{
    public const string AnswerQuestion = "answer_question";
    public const string AcceptRecommended = "accept_recommended";
    public const string ProvideResource = "provide_resource";
    public const string NonBlocking = "non_blocking";
}

public sealed record VisionAgentPlanAnswer
{
    public string QuestionId { get; init; } = string.Empty;
    public string Field { get; init; } = string.Empty;
    public string Value { get; init; } = string.Empty;
    public string Origin { get; init; } = string.Empty;
    public double Confidence { get; init; } = 1.0;
    public bool Resolved { get; init; } = true;
}

public sealed record VisionAgentBuildBlocker
{
    public string Id { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public string Field { get; init; } = string.Empty;
    public string QuestionId { get; init; } = string.Empty;
    public bool BlocksBuild { get; init; }
    public string ResolutionMode { get; init; } = string.Empty;
    public string PublicLabel { get; init; } = string.Empty;
}

public sealed record VisionAgentBuildReadinessSnapshot
{
    public bool CanBuild { get; init; }
    public List<VisionAgentBuildBlocker> Blockers { get; init; } = [];
    public List<string> ResolvedFields { get; init; } = [];
    public List<string> RemainingFields { get; init; } = [];
    public string PrimaryMessage { get; init; } = string.Empty;
    public string ContractVersion { get; init; } = VisionAgentPlanContractVersions.V2;
}

public sealed record VisionAgentSemanticExtractionRequest
{
    public string Description { get; init; } = string.Empty;
    public string? OriginalUserPrompt { get; init; }
    public string? AdditionalContext { get; init; }
    public string? SessionId { get; init; }
    public string? Mode { get; init; }
    public bool HasCurrentFlow { get; init; }
    public bool HasPendingPlan { get; init; }
    public AiTemplateSelectionInfo? TemplateSelection { get; init; }
    public VisionAgentAttachmentSummary AttachmentSummary { get; init; } = new();
    public string? HistorySummary { get; init; }
    public string? CurrentFlowSummary { get; init; }
    public bool MetadataOnly { get; init; } = true;
}

public sealed record VisionAgentSemanticExtractionResult
{
    public bool IsVisionRequest { get; init; }
    public string Intent { get; init; } = "unknown";
    public string TaskType { get; init; } = AiVisionTaskTypes.Unknown;
    public double Confidence { get; init; }
    public double TaskTypeConfidence { get; init; }

    public string InspectionObject { get; init; } = string.Empty;
    public string TargetAttribute { get; init; } = string.Empty;
    public string DefectType { get; init; } = string.Empty;
    public string MeasurementTarget { get; init; } = string.Empty;
    public string ImageSource { get; init; } = string.Empty;
    public string OkCondition { get; init; } = string.Empty;
    public string NgCondition { get; init; } = string.Empty;
    public string OutputTarget { get; init; } = string.Empty;
    public string SuggestedRoute { get; init; } = string.Empty;

    public bool CanPlanCandidate { get; init; }
    public bool CanBuildCandidate { get; init; }

    public IReadOnlyList<string> ObjectSignals { get; init; } = [];
    public IReadOnlyList<string> TaskSignals { get; init; } = [];
    public IReadOnlyList<string> MissingFields { get; init; } = [];
    public IReadOnlyList<string> ClarificationQuestions { get; init; } = [];

    public string Source { get; init; } = VisionAgentSemanticSources.Model;
    public string FailureCode { get; init; } = string.Empty;
    public string SanitizedErrorMessage { get; init; } = string.Empty;
    public bool MetadataOnly { get; init; } = true;
}

public sealed record AiRequirementMaturityResult
{
    public string Maturity { get; init; } = AiRequirementMaturity.Ambiguous;
    public string TaskType { get; init; } = AiVisionTaskTypes.Unknown;
    public bool CanPlan { get; init; }
    public bool CanBuild { get; init; }
    public List<string> ObjectSignals { get; init; } = [];
    public List<string> TaskSignals { get; init; } = [];
    public List<string> MissingFields { get; init; } = [];
    public List<string> BlockingReasons { get; init; } = [];
    public string PublicReason { get; init; } = string.Empty;
    public bool MetadataOnly { get; init; } = true;
}

public sealed record AiDecisionTrace
{
    public string RawUserText { get; init; } = string.Empty;
    public string TurnIntent { get; init; } = string.Empty;
    public string InteractionState { get; init; } = string.Empty;
    public List<string> BusinessSignalsHit { get; init; } = [];
    public List<string> NewFlowSignalsHit { get; init; } = [];
    public List<string> TaskTypeSignalsHit { get; init; } = [];
    public List<string> ObjectSignalsHit { get; init; } = [];
    public string MaturityLevel { get; init; } = AiRequirementMaturity.Ambiguous;
    public string TaskType { get; init; } = AiVisionTaskTypes.Unknown;
    public bool CanPlan { get; init; }
    public bool CanBuild { get; init; }
    public string FallbackReason { get; init; } = string.Empty;
    public List<string> BlockingReasons { get; init; } = [];
    public bool MetadataOnly { get; init; } = true;
}

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
    public VisionAgentSemanticExtractionResult? SemanticExtraction { get; init; }
    public string RequirementMode { get; init; } = AiRequirementModes.Strict;
    public List<VisionAgentPlanAnswer> ConfirmedPlanAnswers { get; init; } = [];
    public List<string> ResolvedPlanFields { get; init; } = [];
    public List<string> RemainingPlanFields { get; init; } = [];
}

public sealed record VisionAgentIntentRouterRequest
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
    public bool HasPendingPlan { get; init; }
    public string? PendingPlanSummary { get; init; }
    public List<VisionAgentPlanAnswer> ConfirmedPlanAnswers { get; init; } = [];
    public List<string> ResolvedPlanFields { get; init; } = [];
    public List<string> RemainingPlanFields { get; init; } = [];
    public string PendingPlanHash { get; init; } = string.Empty;
    public string RequirementMode { get; init; } = AiRequirementModes.Strict;
    public bool DeveloperDirectBuildDebug { get; init; }
    public VisionAgentSemanticExtractionResult? SemanticExtraction { get; init; }
    public bool MetadataOnly { get; init; } = true;
}

public sealed record VisionAgentIntentRouterResult
{
    public string Intent { get; init; } = "ambiguous_vision_requirement";
    public string Confidence { get; init; } = "low";
    public bool ShouldOpenPlan { get; init; }
    public bool ShouldBuildDirectly { get; init; }
    public bool CanBuild { get; init; }
    public bool NeedsClarification { get; init; } = true;
    public string PublicReason { get; init; } = string.Empty;
    public string AssistantReply { get; init; } = string.Empty;
    public List<string> ClarificationQuestions { get; init; } = [];
    public bool FallbackAllowed { get; init; } = true;
    public string RouterSource { get; init; } = string.Empty;
    public string FallbackReason { get; init; } = string.Empty;
    public VisionAgentSemanticExtractionResult? SemanticExtraction { get; init; }
    public AiRequirementMaturityResult? RequirementMaturity { get; init; }
    public AiDecisionTrace? DecisionTrace { get; init; }
    public bool ShouldMergeIntoPendingPlan { get; init; }
    public bool ShouldResetPendingPlan { get; init; }
    public List<VisionAgentPlanAnswer> PlanAnswerUpdates { get; init; } = [];
    public List<string> ResolvedPlanFields { get; init; } = [];
    public List<string> RemainingPlanFields { get; init; } = [];
    public bool MetadataOnly { get; init; } = true;
}

public sealed record VisionAgentPlanModeResult
{
    public string PlanContractVersion { get; init; } = string.Empty;
    public string PlanId { get; init; } = string.Empty;
    public string PlanHash { get; init; } = string.Empty;
    public string PlanSource { get; init; } = string.Empty;
    public string FallbackReason { get; init; } = string.Empty;
    public string PlannerFailureStage { get; init; } = string.Empty;
    public string PlannerFailureCode { get; init; } = string.Empty;
    public string SanitizedErrorKind { get; init; } = string.Empty;
    public string SanitizedErrorMessage { get; init; } = string.Empty;
    public string OriginalUserPrompt { get; init; } = string.Empty;
    public string Goal { get; init; } = string.Empty;
    public string Intent { get; init; } = string.Empty;
    public string Confidence { get; init; } = "medium";
    public List<string> RequirementUnderstanding { get; init; } = [];
    public List<VisionAgentPlanAnswer> ConfirmedPlanAnswers { get; init; } = [];
    public List<string> ResolvedPlanFields { get; init; } = [];
    public List<string> RemainingPlanFields { get; init; } = [];
    public VisionAgentRecommendedRoute RecommendedRoute { get; init; } = new();
    public List<VisionAgentClarificationQuestion> ClarificationQuestions { get; init; } = [];
    public List<VisionAgentDefaultAssumption> RecommendedDefaults { get; init; } = [];
    public List<string> Risks { get; init; } = [];
    public List<string> AcceptanceCriteria { get; init; } = [];
    public List<string> ExecutablePlan { get; init; } = [];
    public bool CanBuild { get; init; }
    public List<string> BlockingReasons { get; init; } = [];
    public VisionAgentBuildReadinessSnapshot BuildReadiness { get; init; } = new();
    public VisionAgentSemanticExtractionResult? SemanticExtraction { get; init; }
    public AiRequirementMaturityResult? RequirementMaturity { get; init; }
    public AiDecisionTrace? DecisionTrace { get; init; }
    public string NextAction { get; init; } = string.Empty;
    public VisionAgentPlanContextSummary ContextSummary { get; init; } = new();
    public string OperatorCatalogVersion { get; init; } = string.Empty;
    public string TemplateCatalogVersion { get; init; } = string.Empty;
    public AiTemplateSelectionInfo? TemplateSelection { get; init; }
    public string StationBoundarySummary { get; init; } = string.Empty;
    public string PlcOutputPolicy { get; init; } = string.Empty;
    public List<string> PlanWarnings { get; init; } = [];
    public List<string> ContractRepairNotes { get; init; } = [];
    public List<VisionAgentPlanPublicEvent> PublicEvents { get; init; } = [];
    public bool MetadataOnly { get; init; } = true;
}

public sealed record VisionAgentPlannerCandidate
{
    public string Goal { get; init; } = string.Empty;
    public string Intent { get; init; } = string.Empty;
    public string Confidence { get; init; } = "medium";
    public List<string>? RequirementUnderstanding { get; init; }
    public VisionAgentRecommendedRoute? RecommendedRoute { get; init; }
    public List<VisionAgentClarificationQuestion>? ClarificationQuestions { get; init; }
    public List<VisionAgentDefaultAssumption>? RecommendedDefaults { get; init; }
    public List<string>? Risks { get; init; }
    public List<string>? AcceptanceCriteria { get; init; }
    public List<string>? ExecutablePlan { get; init; }
    public bool CanBuildCandidate { get; init; }
    public bool? CanBuild { get; init; }
    public List<string>? BlockingReasons { get; init; }
    public string NextAction { get; init; } = string.Empty;
}

public sealed record VisionAgentPlanPublicEvent
{
    public string Stage { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
    public Dictionary<string, string> Metadata { get; init; } =
        new(StringComparer.OrdinalIgnoreCase);
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
    public string Field { get; init; } = string.Empty;
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
    public List<VisionAgentPlanAnswer> ConfirmedAnswers { get; init; } = [];
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
    public AiRequirementMaturityResult? RequirementMaturity { get; init; }
    public AiDecisionTrace? DecisionTrace { get; init; }
    public bool MetadataOnly { get; init; } = true;
}
