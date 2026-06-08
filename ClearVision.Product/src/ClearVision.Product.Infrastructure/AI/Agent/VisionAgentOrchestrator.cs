using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ClearVision.Product.Core.AI.Tools;
using ClearVision.Product.Core.DTOs;
using ClearVision.Product.Core.Services;
using ClearVision.Product.Infrastructure.AI.AgentRun;

namespace ClearVision.Product.Infrastructure.AI.Agent;

public interface IVisionAgentOrchestrator
{
    Task<VisionAgentPlanModeResult> CreatePlanAsync(
        VisionAgentPlanModeRequest request,
        CancellationToken cancellationToken);

    Task<AiFlowGenerationResult> BuildFromPlanAsync(
        AiFlowGenerationRequest request,
        CancellationToken cancellationToken);
}

public sealed class VisionAgentOrchestrator : IVisionAgentOrchestrator
{
    private static readonly Regex UnsafeTemplateMetadataRegex = new(
        @"(?i)([A-Za-z]:\\|data:image\/|sk-[A-Za-z0-9_\-]{12,}|api[_-]?key\s*[:=]|token\s*[:=]|secret\s*[:=]|\b(?:\d{1,3}\.){3}\d{1,3}\b|\bDB\d+\.DB[XBWD]\d+\b|\bM\d+(?:\.\d+)?\b|\bD\d+\b|plc:\/\/)",
        RegexOptions.Compiled);

    private static readonly JsonSerializerOptions PlanHashJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private readonly IVisionAgentToolRegistry _toolRegistry;
    private readonly IAiFlowGenerationService _generationService;
    private readonly IVisionAgentPlanPlannerService? _planPlannerService;
    private readonly IAgentRunEventSink? _eventSink;

    public VisionAgentOrchestrator(
        IVisionAgentToolRegistry toolRegistry,
        IAiFlowGenerationService generationService,
        IAgentRunEventSink? eventSink = null,
        IVisionAgentPlanPlannerService? planPlannerService = null)
    {
        _toolRegistry = toolRegistry;
        _generationService = generationService;
        _planPlannerService = planPlannerService;
        _eventSink = eventSink;
    }

    public async Task<VisionAgentPlanModeResult> CreatePlanAsync(
        VisionAgentPlanModeRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var ruleBaseline = BuildPlan(request);
        if (_planPlannerService == null)
        {
            return BuildRuleFallbackPlan(
                ruleBaseline,
                "planner_service_not_registered",
                "Plan planner service is not registered; using rule fallback plan.");
        }

        return await _planPlannerService.CreatePlanAsync(
            request,
            ruleBaseline,
            cancellationToken);
    }

    public async Task<AiFlowGenerationResult> BuildFromPlanAsync(
        AiFlowGenerationRequest request,
        CancellationToken cancellationToken)
    {
        EmitBuildPreparationEvents(request);
        return await _generationService.GenerateFlowAsync(request, cancellationToken: cancellationToken);
    }

    private VisionAgentPlanModeResult BuildPlan(VisionAgentPlanModeRequest request)
    {
        var description = Clean(request.Description);
        var originalPrompt = string.IsNullOrWhiteSpace(request.OriginalUserPrompt)
            ? description
            : Clean(request.OriginalUserPrompt);
        var templateSelection = RedactTemplateSelection(request.TemplateSelection);
        var scenario = DetectScenario(description, templateSelection);
        var route = BuildRoute(scenario, templateSelection);
        var questions = BuildQuestions(scenario);
        var defaults = BuildDefaults(scenario, request);
        var toolNames = _toolRegistry.ListTools()
            .Select(tool => tool.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .Take(12)
            .ToList();
        var operatorCatalogVersion = BuildCatalogVersion(toolNames);
        var hasFlow = !string.IsNullOrWhiteSpace(request.CurrentFlowSnapshot);
        var hasResult = !string.IsNullOrWhiteSpace(request.CurrentResultSnapshot);
        var attachmentCount = Math.Max(request.AttachmentSummary.Count, 0);
        var canBuild = !string.IsNullOrWhiteSpace(description);
        var blockingReasons = canBuild
            ? new List<string>()
            : ["inspection_goal_missing"];

        var result = new VisionAgentPlanModeResult
        {
            PlanId = $"plan_{Guid.NewGuid():N}",
            OriginalUserPrompt = originalPrompt,
            PlanSource = "rule_baseline",
            Goal = description.Length > 160 ? description[..160] : description,
            Intent = scenario,
            Confidence = scenario == "general_inspection" ? "medium" : "high",
            RequirementUnderstanding =
            [
                $"Inspection intent: {ToScenarioTitle(scenario)}.",
                hasFlow ? "Current canvas summary is available for Build." : "Build can start as a new workflow draft.",
                templateSelection != null
                    ? "A template selection was provided and will be considered first."
                    : "Template choice will be decided by the Build stage.",
                attachmentCount > 0
                    ? $"{attachmentCount} attachment(s) are available as redacted metadata."
                    : "No attachment metadata was provided."
            ],
            RecommendedRoute = route,
            ClarificationQuestions = questions,
            RecommendedDefaults = defaults,
            Risks = BuildRisks(scenario),
            AcceptanceCriteria = BuildAcceptanceCriteria(scenario),
            ExecutablePlan =
            [
                "Confirm recommended assumptions or answer only the high-impact questions.",
                "Prepare Build input with the plan snapshot, user selections, current flow, template, attachment, and Station boundary summaries.",
                "Choose template strategy and operator pipeline from metadata-only catalogs.",
                "Map parameters and keep unresolved resources as pending parameters or missing resources.",
                "Run schema validation, dry-run, runtime package readiness, Station compatibility, operator contract, and release review checks.",
                "Return an editable workflow draft and first fix recommendation before Apply."
            ],
            CanBuild = canBuild,
            BlockingReasons = blockingReasons,
            NextAction = canBuild
                ? "Accept recommended defaults or answer questions, then start Build."
                : "Describe the inspection target before Build can start.",
            ContextSummary = new VisionAgentPlanContextSummary
            {
                HasCurrentFlow = hasFlow,
                HasCurrentResult = hasResult,
                AttachmentCount = attachmentCount,
                TemplateSelectionMode = templateSelection?.Mode ?? string.Empty,
                TemplateId = templateSelection?.TemplateId ?? string.Empty,
                ContextKinds =
                [
                    "user_requirement",
                    hasFlow ? "current_flow" : "new_flow",
                    hasResult ? "current_result" : "no_current_result",
                    templateSelection != null ? "template_selection" : "template_catalog",
                    "operator_catalog",
                    "station_boundary"
                ],
                OperatorCatalogTools = toolNames
            },
            OperatorCatalogVersion = operatorCatalogVersion,
            TemplateCatalogVersion = templateSelection?.TemplateId is { Length: > 0 } templateId
                ? $"selected-template:{templateId}"
                : "metadata-template-catalog.v1",
            TemplateSelection = templateSelection,
            StationBoundarySummary = "metadata-only Station boundary; no camera, PLC, filesystem, or network resource is touched during Plan.",
            PlcOutputPolicy = scenario == "plc_output"
                ? "PLC output is planned as pending metadata until OK/NG address, handshake, and fail-safe policy are confirmed."
                : "Local ResultOutput first; PLC writes remain disabled until Build readiness review.",
            MetadataOnly = true
        };

        return result with
        {
            PlanHash = ComputePlanHash(result)
        };
    }

    private static VisionAgentPlanModeResult BuildRuleFallbackPlan(
        VisionAgentPlanModeResult baseline,
        string fallbackReason,
        string warning)
    {
        var result = baseline with
        {
            PlanSource = "rule_fallback",
            FallbackReason = fallbackReason,
            PlanWarnings = [warning],
            ContractRepairNotes = [],
            PublicEvents =
            [
                new VisionAgentPlanPublicEvent
                {
                    Stage = "collecting_context",
                    Status = "completed",
                    Title = "Context collected",
                    Summary = "Collected public requirement, flow, template, attachment, operator, and Station boundary metadata.",
                    MetadataOnly = true
                },
                new VisionAgentPlanPublicEvent
                {
                    Stage = "rule_fallback_used",
                    Status = "completed",
                    Title = "Rule fallback used",
                    Summary = warning,
                    Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["fallbackReason"] = fallbackReason
                    },
                    MetadataOnly = true
                },
                new VisionAgentPlanPublicEvent
                {
                    Stage = "plan_ready",
                    Status = "completed",
                    Title = "Fallback plan ready",
                    Summary = "Rule fallback PlanModeResult is ready for user confirmation.",
                    MetadataOnly = true
                }
            ],
            MetadataOnly = true
        };
        return result with
        {
            PlanHash = ComputePlanHash(result)
        };
    }

    private void EmitBuildPreparationEvents(AiFlowGenerationRequest request)
    {
        var runId = request.AgentRunId;
        var build = request.BuildFromPlan;
        var plan = build?.PlanSnapshot;
        var hasExistingFlow = !string.IsNullOrWhiteSpace(request.ExistingFlowJson) ||
                              !string.IsNullOrWhiteSpace(build?.CurrentFlowSnapshot);
        EmitPlanHashDiagnosticIfNeeded(runId, build);

        _eventSink?.StageStarted(
            runId,
            "understand_requirement",
            "Understanding requirement",
            "Reading the user goal and public plan snapshot.",
            new
            {
                mode = request.Mode.ToWireValue(),
                buildIntent = build?.BuildIntent ?? request.Mode.ToWireValue(),
                hasExistingFlow,
                metadataOnly = true
            });
        _eventSink?.StageCompleted(
            runId,
            "understand_requirement",
            "Requirement understood",
            "Public requirement and plan context were normalized.",
            new
            {
                goal = plan?.Goal ?? request.Description,
                intent = plan?.Intent ?? "build_without_plan_snapshot",
                confidence = plan?.Confidence ?? "unknown",
                metadataOnly = true
            });
        _eventSink?.StageStarted(
            runId,
            "context_collection",
            "Collecting engineering context",
            "Collecting current flow, template, attachment, operator catalog, and Station boundary metadata.",
            new
            {
                metadataOnly = true
            });
        _eventSink?.StageCompleted(
            runId,
            "context_collection",
            "Engineering context collected",
            "Build context was collected as public metadata.",
            new
            {
                contextKinds = plan?.ContextSummary.ContextKinds ?? ["user_requirement", "operator_catalog", "station_boundary"],
                hasExistingFlow,
                attachmentCount = build?.AttachmentSummary.Count ?? request.Attachments?.Count ?? 0,
                templateSelectionMode = build?.TemplateSelection?.Mode ?? request.TemplateSelection?.Mode ?? string.Empty,
                templateId = build?.TemplateSelection?.TemplateId ?? request.TemplateSelection?.TemplateId ?? string.Empty,
                operatorCatalogVersion = build?.OperatorCatalogVersion ?? plan?.OperatorCatalogVersion ?? string.Empty,
                metadataOnly = true
            });
        _eventSink?.StageCompleted(
            runId,
            "plan_generation",
            build == null ? "Plan generated for Build" : "Plan loaded for Build",
            build == null
                ? "Build started without a Plan snapshot; a minimal public build plan was inferred."
                : "Confirmed Plan Mode snapshot and selected options were loaded.",
            new
            {
                planId = build?.PlanId ?? plan?.PlanId ?? string.Empty,
                planHash = build?.PlanHash ?? plan?.PlanHash ?? string.Empty,
                planSnapshot = plan,
                userSelections = build?.UserSelections ?? new Dictionary<string, string>(),
                acceptedDefaults = build?.AcceptedDefaults ?? [],
                metadataOnly = true
            });
        _eventSink?.StageCompleted(
            runId,
            "assumption_confirmation",
            "Assumptions confirmed",
            "Build Mode received structured selections and accepted defaults.",
            new
            {
                acceptedRecommendedDefaults = build?.AcceptedRecommendedDefaults ?? false,
                defaultCount = build?.AcceptedDefaults.Count ?? plan?.RecommendedDefaults.Count ?? 0,
                plcOutputPolicy = build?.PlcOutputPolicy ?? plan?.PlcOutputPolicy ?? string.Empty,
                metadataOnly = true
            });
        _eventSink?.StageStarted(
            runId,
            "requirement_parsing",
            "Requirement parsing",
            "Normalizing the structured BuildFromPlan request for controlled tool execution.",
            new
            {
                mode = request.Mode.ToWireValue(),
                hasExistingFlow,
                attachmentCount = build?.AttachmentSummary.Count ?? request.Attachments?.Count ?? 0,
                usePlanner = string.Equals(
                    AiAgentGenerateFlowModes.Normalize(request.AgentGenerateFlowMode),
                    AiAgentGenerateFlowModes.Planner,
                    StringComparison.OrdinalIgnoreCase),
                metadataOnly = true
            });
        _eventSink?.StageCompleted(
            runId,
            "requirement_parsing",
            "Requirement parsing complete",
            "Structured plan, selections, and metadata-only context are ready for Build tools.",
            new
            {
                buildInputSummary = BuildInputSummary(request),
                metadataOnly = true
            });
    }

    private static object BuildInputSummary(AiFlowGenerationRequest request)
    {
        var build = request.BuildFromPlan;
        return new
        {
            planId = build?.PlanId ?? string.Empty,
            planHash = build?.PlanHash ?? build?.PlanSnapshot?.PlanHash ?? string.Empty,
            buildIntent = build?.BuildIntent ?? request.Mode.ToWireValue(),
            currentFlowSnapshotIncluded = !string.IsNullOrWhiteSpace(request.ExistingFlowJson) ||
                                          !string.IsNullOrWhiteSpace(build?.CurrentFlowSnapshot),
            templateSelectionMode = build?.TemplateSelection?.Mode ?? request.TemplateSelection?.Mode ?? string.Empty,
            templateId = build?.TemplateSelection?.TemplateId ?? request.TemplateSelection?.TemplateId ?? string.Empty,
            attachmentCount = build?.AttachmentSummary.Count ?? request.Attachments?.Count ?? 0,
            operatorCatalogVersion = build?.OperatorCatalogVersion ?? string.Empty,
            stationBoundarySummary = build?.StationBoundarySummary ?? string.Empty,
            plcOutputPolicy = build?.PlcOutputPolicy ?? string.Empty,
            metadataOnly = true
        };
    }

    public static string ComputePlanHash(VisionAgentPlanModeResult? plan)
    {
        if (plan == null)
        {
            return string.Empty;
        }

        var payload = new
        {
            goal = Clean(plan.Goal),
            intent = Clean(plan.Intent),
            confidence = Clean(plan.Confidence),
            requirementUnderstanding = NormalizeList(plan.RequirementUnderstanding),
            recommendedRoute = new
            {
                routeId = Clean(plan.RecommendedRoute.RouteId),
                title = Clean(plan.RecommendedRoute.Title),
                summary = Clean(plan.RecommendedRoute.Summary),
                operators = NormalizeList(plan.RecommendedRoute.Operators),
                templateDecision = Clean(plan.RecommendedRoute.TemplateDecision)
            },
            clarificationQuestions = plan.ClarificationQuestions
                .Select(question => new
                {
                    id = Clean(question.Id),
                    title = Clean(question.Title),
                    why = Clean(question.Why),
                    defaultValue = Clean(question.DefaultValue),
                    defaultAssumption = Clean(question.DefaultAssumption),
                    impact = Clean(question.Impact),
                    options = question.Options.Select(option => new
                    {
                        value = Clean(option.Value),
                        label = Clean(option.Label),
                        recommended = option.Recommended,
                        description = Clean(option.Description),
                        impact = Clean(option.Impact)
                    }).ToList()
                })
                .ToList(),
            recommendedDefaults = plan.RecommendedDefaults
                .Select(item => new
                {
                    id = Clean(item.Id),
                    label = Clean(item.Label),
                    value = Clean(item.Value),
                    impact = Clean(item.Impact)
                })
                .ToList(),
            risks = NormalizeList(plan.Risks),
            acceptanceCriteria = NormalizeList(plan.AcceptanceCriteria),
            executablePlan = NormalizeList(plan.ExecutablePlan),
            canBuild = plan.CanBuild,
            blockingReasons = NormalizeList(plan.BlockingReasons),
            nextAction = Clean(plan.NextAction),
            contextSummary = new
            {
                hasCurrentFlow = plan.ContextSummary.HasCurrentFlow,
                hasCurrentResult = plan.ContextSummary.HasCurrentResult,
                attachmentCount = Math.Max(plan.ContextSummary.AttachmentCount, 0),
                templateSelectionMode = Clean(plan.ContextSummary.TemplateSelectionMode),
                templateId = Clean(plan.ContextSummary.TemplateId),
                contextKinds = NormalizeList(plan.ContextSummary.ContextKinds),
                operatorCatalogTools = NormalizeList(plan.ContextSummary.OperatorCatalogTools)
            },
            operatorCatalogVersion = Clean(plan.OperatorCatalogVersion),
            templateCatalogVersion = Clean(plan.TemplateCatalogVersion),
            templateSelection = NormalizeTemplateSelectionForHash(plan.TemplateSelection),
            stationBoundarySummary = Clean(plan.StationBoundarySummary),
            plcOutputPolicy = Clean(plan.PlcOutputPolicy),
            metadataOnly = plan.MetadataOnly
        };

        var json = JsonSerializer.Serialize(payload, PlanHashJsonOptions);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return $"sha256:{Convert.ToHexString(bytes).ToLowerInvariant()}";
    }

    private void EmitPlanHashDiagnosticIfNeeded(
        string? runId,
        VisionAgentBuildFromPlanRequest? build)
    {
        if (build?.PlanSnapshot == null || string.IsNullOrWhiteSpace(build.PlanHash))
        {
            return;
        }

        var computed = ComputePlanHash(build.PlanSnapshot);
        if (string.IsNullOrWhiteSpace(computed) ||
            string.Equals(build.PlanHash, computed, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _eventSink?.StageCompleted(
            runId,
            "plan_hash_validation",
            "Plan hash mismatch detected",
            "Build is continuing with the public plan snapshot; review plan provenance before applying.",
            new
            {
                warningCode = "plan_hash_mismatch",
                planId = build.PlanId,
                providedPlanHash = build.PlanHash,
                computedPlanHash = computed,
                publicDiagnosticsOnly = true,
                metadataOnly = true
            });
    }

    private static string DetectScenario(string description, AiTemplateSelectionInfo? templateSelection)
    {
        var text = description.ToLowerInvariant();
        if (ContainsAny(text, "plc", "ok/ng", "ok ng", "ng output", "result output", "plc输出", "结果输出", "输出信号", "握手", "地址"))
        {
            return "plc_output";
        }

        if (ContainsAny(text, "wire", "terminal", "harness", "sequence", "线序", "端子", "线束", "排线", "插线"))
        {
            return "wire_sequence";
        }

        if (ContainsAny(text, "barcode", "qr", "datamatrix", "code", "二维码", "条码", "读码", "扫码"))
        {
            return "code_recognition";
        }

        if (ContainsAny(text, "measure", "distance", "diameter", "width", "hole", "calibration", "测量", "孔距", "直径", "宽度", "尺寸", "标定", "距离"))
        {
            return "measurement";
        }

        if (ContainsAny(text, "template", "align", "position", "locate", "matching", "模板", "定位", "匹配", "对位", "找正") ||
            string.Equals(templateSelection?.Mode, "template_fill", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(templateSelection?.Mode, "template_adapt", StringComparison.OrdinalIgnoreCase))
        {
            return "template_location";
        }

        if (ContainsAny(text, "remote", "button", "keypad", "key press", "遥控器", "按键", "按钮", "键盘"))
        {
            return "button_inspection";
        }

        if (ContainsAny(text, "scratch", "metal", "surface", "defect", "crack", "dent", "划痕", "刮伤", "金属", "表面", "缺陷", "裂纹", "凹坑"))
        {
            return "surface_defect";
        }

        return "general_inspection";
    }

    private static VisionAgentRecommendedRoute BuildRoute(
        string scenario,
        AiTemplateSelectionInfo? templateSelection)
    {
        var templateDecision = templateSelection == null
            ? "Build will match templates from metadata and may proceed without a template."
            : $"Use selected template mode '{templateSelection.Mode}' before adapting parameters.";

        return scenario switch
        {
            "wire_sequence" => new VisionAgentRecommendedRoute
            {
                RouteId = "wire_sequence_template_first",
                Title = "Template-first wire sequence inspection",
                Summary = "Use a wire/terminal sequence template, bind model metadata, then judge order and output OK/NG.",
                Operators = ["ImageAcquisition", "DeepLearning", "DetectionSequenceJudge", "ResultOutput"],
                TemplateDecision = templateDecision
            },
            "code_recognition" => new VisionAgentRecommendedRoute
            {
                RouteId = "code_recognition",
                Title = "Code recognition route",
                Summary = "Acquire image, isolate code ROI, decode QR/DataMatrix/barcode, and publish structured result.",
                Operators = ["ImageAcquisition", "RoiManager", "CodeRecognition", "ResultJudgment", "ResultOutput"],
                TemplateDecision = templateDecision
            },
            "measurement" => new VisionAgentRecommendedRoute
            {
                RouteId = "measurement_with_calibration",
                Title = "Calibration-backed measurement route",
                Summary = "Load calibration, locate geometry, measure dimensions, and compare tolerance.",
                Operators = ["ImageAcquisition", "CalibrationLoader", "CircleMeasurement", "GeoMeasurement", "ResultOutput"],
                TemplateDecision = templateDecision
            },
            "template_location" => new VisionAgentRecommendedRoute
            {
                RouteId = "template_location",
                Title = "Template positioning route",
                Summary = "Match target template, normalize pose, then pass aligned ROI to downstream inspection.",
                Operators = ["ImageAcquisition", "TemplateMatching", "AffineTransform", "ResultJudgment", "ResultOutput"],
                TemplateDecision = templateDecision
            },
            "button_inspection" => new VisionAgentRecommendedRoute
            {
                RouteId = "button_inspection",
                Title = "Remote/keypad button inspection route",
                Summary = "Locate the panel, segment key positions, classify presence/state, and judge layout.",
                Operators = ["ImageAcquisition", "TemplateMatching", "RoiManager", "DeepLearning", "ResultJudgment", "ResultOutput"],
                TemplateDecision = templateDecision
            },
            "plc_output" => new VisionAgentRecommendedRoute
            {
                RouteId = "inspection_with_plc_pending",
                Title = "Inspection with PLC output pending",
                Summary = "Generate inspection draft and keep PLC OK/NG output as metadata until address policy is confirmed.",
                Operators = ["ImageAcquisition", "InspectionOperator", "ResultJudgment", "ResultOutput"],
                TemplateDecision = templateDecision
            },
            _ => new VisionAgentRecommendedRoute
            {
                RouteId = "surface_defect_detection",
                Title = "Surface defect inspection route",
                Summary = "Normalize illumination, enhance defects, segment candidates, and judge by area/contrast.",
                Operators = ["ImageAcquisition", "ShadingCorrection", "SurfaceDefectDetection", "BlobAnalysis", "ResultJudgment", "ResultOutput"],
                TemplateDecision = templateDecision
            }
        };
    }

    private static List<VisionAgentClarificationQuestion> BuildQuestions(string scenario)
    {
        return scenario switch
        {
            "wire_sequence" =>
            [
                Question("sequence_rule", "Which sequence rule should be checked first?", "Sequence order drives model labels and judgment logic.", "left_to_right", "Judge terminals left-to-right using model label order.", "Wrong sequence policy changes OK/NG semantics.",
                [
                    Option("left_to_right", "Left to right", true, "Read terminal order from left to right.", "Fastest for common harness layouts."),
                    Option("color_order", "Color order", false, "Judge by expected color/label list.", "Requires explicit expected sequence."),
                    Option("custom_rule", "Custom rule pending", false, "Keep sequence rule as pending metadata.", "Build can draft but will block readiness.")
                ]),
                Question("model_binding", "What model resource should Build assume?", "Wire sequence inspection usually needs trained labels.", "model_pending", "Create DeepLearning operator with model path pending.", "Without model metadata deployment readiness remains blocked.",
                [
                    Option("model_pending", "Model pending", true, "Expose ModelPath as pending parameter.", "Keeps draft editable without inventing paths."),
                    Option("existing_model", "Existing model", false, "Bind to an existing metadata model handle.", "Requires model handle selection."),
                    Option("template_only", "Template only", false, "Use template/ROI logic before model binding.", "Less robust for color and terminal variation.")
                ])
            ],
            "measurement" =>
            [
                Question("measurement_target", "Which dimension is the primary measurement?", "Target type determines geometry operators and tolerance fields.", "hole_distance", "Measure hole or feature distance in calibrated units.", "Wrong target changes operator chain.", [
                    Option("hole_distance", "Hole distance", true, "Detect circles and measure center distance.", "Best for common fixture checks."),
                    Option("diameter", "Diameter", false, "Measure circle diameter.", "Requires edge quality and calibration."),
                    Option("width", "Width", false, "Measure part width or gap.", "Requires line/edge extraction.")
                ]),
                Question("calibration_policy", "How should pixel-to-mm calibration be handled?", "Measurement must not invent scale.", "calibration_pending", "Keep calibration file or scale as pending metadata.", "Build remains editable but not deploy-ready.", [
                    Option("calibration_pending", "Calibration pending", true, "Expose calibration as pending parameter.", "Safest metadata-only path."),
                    Option("known_scale", "Known scale", false, "Use provided scale metadata.", "Requires user-provided scale."),
                    Option("pixel_only", "Pixel only", false, "Report pixels first.", "Fast but not production measurement.")
                ])
            ],
            "template_location" =>
            [
                Question("template_asset", "What template asset should be used?", "Template matching cannot deploy without a template handle.", "template_pending", "Create TemplateMatching with template path pending.", "Draft can be edited; readiness blocks until template metadata exists.", [
                    Option("template_pending", "Template pending", true, "Expose TemplatePath as missing resource.", "Safest without local paths."),
                    Option("selected_template", "Use selected template", false, "Use templateSelection metadata from the UI.", "Best if a template was selected."),
                    Option("auto_locate", "No template yet", false, "Use detector or ROI placeholder.", "More flexible but less deterministic.")
                ]),
                Question("pose_tolerance", "How much pose drift should be allowed?", "Search range affects speed and false positives.", "moderate", "Use moderate rotation/scale search.", "Wide search slows dry-run and can reduce confidence.", [
                    Option("moderate", "Moderate", true, "Allow small rotation/translation drift.", "Balanced default."),
                    Option("fixed_pose", "Fixed pose", false, "Assume stable fixture.", "Fastest but fragile."),
                    Option("wide_search", "Wide search", false, "Allow larger pose drift.", "Robust but slower.")
                ])
            ],
            "button_inspection" =>
            [
                Question("button_layout", "How should button positions be modeled?", "Button inspection needs stable layout references.", "template_layout", "Use template location plus named ROIs for keys.", "Template or ROI metadata is needed before deployment.", [
                    Option("template_layout", "Template layout", true, "Locate remote body then inspect key ROIs.", "Best balance for production."),
                    Option("fixed_grid", "Fixed grid", false, "Use fixed ROI grid.", "Fast when fixture is stable."),
                    Option("detector", "Detector", false, "Use model-based key detection.", "Requires model resource.")
                ]),
                Question("button_defect", "What button issue matters most?", "Different issues require different operators.", "presence_state", "Check presence and visual state.", "Wear/printing checks may need extra training data.", [
                    Option("presence_state", "Presence/state", true, "Check missing, pressed, or wrong key state.", "Good first draft."),
                    Option("print_defect", "Print defect", false, "Inspect label/printing quality.", "Needs samples and thresholds."),
                    Option("color_mismatch", "Color mismatch", false, "Check color or cap mismatch.", "Needs lighting constraints.")
                ])
            ],
            "code_recognition" =>
            [
                Question("code_type", "Which code type should be decoded?", "Decoder settings and grading depend on code family.", "auto_code", "Try QR/DataMatrix/barcode metadata decoder settings.", "Auto mode is flexible but may need tightening.", [
                    Option("auto_code", "Auto code", true, "Keep decoder family flexible.", "Best first draft."),
                    Option("qr", "QR", false, "Use QR-specific decode parameters.", "Faster and stricter."),
                    Option("datamatrix", "DataMatrix", false, "Use DataMatrix-specific decode parameters.", "Best for industrial marks.")
                ]),
                Question("decode_policy", "What should happen on unreadable codes?", "Failure handling affects output and Station policy.", "ng_on_unreadable", "Return NG when decode fails.", "Conservative production default.", [
                    Option("ng_on_unreadable", "NG on unreadable", true, "Unreadable code becomes NG.", "Safe default."),
                    Option("retry", "Retry pending", false, "Plan retry or second exposure.", "Needs station timing confirmation."),
                    Option("manual_review", "Manual review", false, "Flag for review instead of immediate NG.", "Needs operator workflow.")
                ])
            ],
            "plc_output" =>
            [
                Question("plc_policy", "How should PLC OK/NG output be represented?", "PLC addresses and network details must stay redacted until confirmed.", "metadata_pending", "Create ResultOutput with PLC policy pending.", "Prevents unsafe address guessing.", [
                    Option("metadata_pending", "PLC pending", true, "Expose PLC address and handshake as pending.", "Safest path."),
                    Option("local_first", "Local first", false, "Output locally before PLC integration.", "Good for lab validation."),
                    Option("station_profile", "Station profile", false, "Use selected Station profile metadata.", "Requires profile confirmation.")
                ]),
                Question("failsafe", "What fail-safe should apply if output fails?", "Fail-safe policy is part of release readiness.", "ng_on_failure", "Treat output failure as NG/pending intervention.", "Conservative default.", [
                    Option("ng_on_failure", "NG on failure", true, "Default to NG on failed output.", "Safer production behavior."),
                    Option("hold_last", "Hold last", false, "Hold previous signal.", "Requires PLC handshake review."),
                    Option("block_release", "Block release", false, "Block deployment until confirmed.", "Most conservative.")
                ])
            ],
            _ =>
            [
                Question("defect_definition", "What should count as a defect?", "Defect definition controls thresholds and judgment.", "scratch_or_blob", "Detect visible scratches/blobs and judge by area/contrast.", "Thresholds need sample confirmation.", [
                    Option("scratch_or_blob", "Scratch/blob", true, "Use general surface defect candidates.", "Good first draft."),
                    Option("crack", "Crack", false, "Emphasize thin dark/bright crack-like defects.", "Needs contrast assumptions."),
                    Option("dent_or_stain", "Dent/stain", false, "Look for dents, stains, or discoloration.", "Needs lighting/sample confirmation.")
                ]),
                Question("roi_strategy", "Which ROI strategy should be used?", "ROI choice changes false positive rate and parameter completeness.", "main_surface", "Inspect the main visible part surface.", "Keeps draft focused.", [
                    Option("main_surface", "Main surface", true, "Use one primary ROI placeholder.", "Best default."),
                    Option("full_frame", "Full frame", false, "Inspect full frame.", "Fewer setup fields but noisier."),
                    Option("auto_locate", "Auto locate", false, "Locate part before defect inspection.", "More robust but more complex.")
                ])
            ]
        };
    }

    private static List<VisionAgentDefaultAssumption> BuildDefaults(
        string scenario,
        VisionAgentPlanModeRequest request)
    {
        var defaults = new List<VisionAgentDefaultAssumption>
        {
            new()
            {
                Id = "metadata_only",
                Label = "Public diagnostics only",
                Value = "redacted_metadata",
                Impact = "No raw local paths, image bytes, Station network details, tokens, or prompts are exposed."
            },
            new()
            {
                Id = "draft_policy",
                Label = "Editable workflow draft",
                Value = "allow_editable_draft_when_not_deploy_ready",
                Impact = "Apply to canvas can remain available while deployment readiness is blocked."
            },
            new()
            {
                Id = "resource_policy",
                Label = "Missing resources stay pending",
                Value = "pending_parameters",
                Impact = "Model, template, camera, calibration, and PLC metadata are surfaced instead of guessed."
            }
        };

        if (request.TemplateSelection != null)
        {
            defaults.Add(new VisionAgentDefaultAssumption
            {
                Id = "template_selection",
                Label = "Respect selected template metadata",
                Value = request.TemplateSelection.Mode ?? "selected",
                Impact = "Build will prefer the user-selected template before falling back to catalog matching."
            });
        }

        if (scenario == "measurement")
        {
            defaults.Add(new VisionAgentDefaultAssumption
            {
                Id = "measurement_units",
                Label = "Metric units require calibration",
                Value = "calibration_pending",
                Impact = "Measurement output is not release-ready until scale or calibration metadata is confirmed."
            });
        }

        return defaults;
    }

    private static List<string> BuildRisks(string scenario)
    {
        var common = new List<string>
        {
            "Field thresholds need representative images before production.",
            "Camera, model, template, calibration, and PLC resources remain metadata-only until confirmed.",
            "Station compatibility can block release while the canvas draft remains editable."
        };
        if (scenario == "plc_output")
        {
            common.Add("PLC OK/NG output must not be enabled without address, handshake, and fail-safe review.");
        }
        if (scenario == "measurement")
        {
            common.Add("Measurement accuracy depends on calibration and lens distortion control.");
        }
        return common;
    }

    private static List<string> BuildAcceptanceCriteria(string scenario)
    {
        var criteria = new List<string>
        {
            "Workflow draft contains acquisition, inspection, judgment, and output stages.",
            "Plan snapshot, user selections, defaults, and Build input summary are replayable from AgentRun.",
            "Readiness, dry-run, package, Station, contract, and release review events are replayable.",
            "Pending parameters and missing resources are visible before Apply or deployment."
        };
        if (scenario == "measurement")
        {
            criteria.Add("Calibration or scale metadata is pending or confirmed before measurement release.");
        }
        if (scenario == "code_recognition")
        {
            criteria.Add("Decode failure policy is represented in ResultJudgment or pending output policy.");
        }
        return criteria;
    }

    private static VisionAgentClarificationQuestion Question(
        string id,
        string title,
        string why,
        string defaultValue,
        string defaultAssumption,
        string impact,
        List<VisionAgentClarificationOption> options)
    {
        return new VisionAgentClarificationQuestion
        {
            Id = id,
            Title = title,
            Why = why,
            DefaultValue = defaultValue,
            DefaultAssumption = defaultAssumption,
            Impact = impact,
            Options = options
        };
    }

    private static VisionAgentClarificationOption Option(
        string value,
        string label,
        bool recommended,
        string description,
        string impact)
    {
        return new VisionAgentClarificationOption
        {
            Value = value,
            Label = label,
            Recommended = recommended,
            Description = description,
            Impact = impact
        };
    }

    private static bool ContainsAny(string text, params string[] values)
    {
        return values.Any(value => text.Contains(value, StringComparison.OrdinalIgnoreCase));
    }

    private static string ToScenarioTitle(string scenario)
    {
        return scenario switch
        {
            "wire_sequence" => "wire sequence inspection",
            "code_recognition" => "code recognition",
            "measurement" => "measurement inspection",
            "template_location" => "template location",
            "button_inspection" => "button inspection",
            "plc_output" => "inspection with PLC output",
            "surface_defect" => "surface defect inspection",
            _ => "general visual inspection"
        };
    }

    private static string BuildCatalogVersion(IReadOnlyList<string> toolNames)
    {
        var joined = string.Join("|", toolNames);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(joined));
        return $"vision-agent-tools:{toolNames.Count}:{Convert.ToHexString(hash)[..12].ToLowerInvariant()}";
    }

    private static AiTemplateSelectionInfo? RedactTemplateSelection(AiTemplateSelectionInfo? selection)
    {
        var mode = SafeTemplateToken(selection?.Mode, string.Empty).ToLowerInvariant();
        var templateId = SafeTemplateToken(selection?.TemplateId, "redacted_template");
        var scenarioKey = SafeTemplateToken(selection?.ScenarioKey, string.Empty);

        if (string.IsNullOrWhiteSpace(mode) &&
            string.IsNullOrWhiteSpace(templateId) &&
            string.IsNullOrWhiteSpace(scenarioKey))
        {
            return null;
        }

        return new AiTemplateSelectionInfo
        {
            Mode = mode,
            TemplateId = string.IsNullOrWhiteSpace(templateId) ? null : templateId,
            ScenarioKey = string.IsNullOrWhiteSpace(scenarioKey) ? null : scenarioKey
        };
    }

    private static object? NormalizeTemplateSelectionForHash(AiTemplateSelectionInfo? selection)
    {
        var redacted = RedactTemplateSelection(selection);
        return redacted == null
            ? null
            : new
            {
                mode = Clean(redacted.Mode),
                templateId = Clean(redacted.TemplateId),
                scenarioKey = Clean(redacted.ScenarioKey)
            };
    }

    private static List<string> NormalizeList(IEnumerable<string>? values)
    {
        return values?
            .Select(Clean)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToList() ?? [];
    }

    private static string Clean(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
    }

    private static string SafeTemplateToken(string? value, string fallback)
    {
        var text = Clean(value);
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        if (UnsafeTemplateMetadataRegex.IsMatch(text) || text.Length > 160)
        {
            return fallback;
        }

        var safe = new string(text
            .Where(ch => char.IsLetterOrDigit(ch) || ch is '_' or '-' or '.')
            .ToArray());
        return string.IsNullOrWhiteSpace(safe) ? fallback : safe;
    }
}
