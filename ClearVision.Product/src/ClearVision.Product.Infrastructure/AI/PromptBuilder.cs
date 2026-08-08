// PromptBuilder.cs
// Builds system prompts for ClearVision AI workflow generation.

using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Services;

namespace ClearVision.Product.Infrastructure.AI;

/// <summary>
/// Builds AI system prompts for ClearVision workflow generation.
/// </summary>
public class PromptBuilder
{
    private readonly IOperatorFactory _operatorFactory;
    private readonly IOperatorKnowledgeRetriever? _operatorKnowledgeRetriever;
    private static readonly JsonSerializerOptions _catalogJsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public PromptBuilder(
        IOperatorFactory operatorFactory,
        IOperatorKnowledgeRetriever? operatorKnowledgeRetriever = null)
    {
        _operatorFactory = operatorFactory;
        _operatorKnowledgeRetriever = operatorKnowledgeRetriever;
    }

    /// <summary>
    /// Builds the full system prompt.
    /// </summary>
    public string BuildSystemPrompt(string? userDescription = null, bool supportsJsonMode = true)
    {
        var sb = new StringBuilder();

        AppendSection(sb, "Section 1 - Role And Hard Rules", GetRoleDefinition());
        AppendSection(sb, "Section 2 - Domain Workflow Patterns", GetDomainKnowledge());
        AppendSection(sb, "Section 3 - Template First Strategy", GetTemplateFirstStrategy());
        AppendSection(sb, "Section 4 - Phase 1 Operator Extensions", GetPhase1OperatorExtensions());
        AppendSection(sb, "Section 5 - Phase 2 Operator Extensions", GetPhase2OperatorExtensions());
        AppendSection(sb, "Section 6 - Phase 3 Operator Extensions", GetPhase3OperatorExtensions());
        AppendSection(sb, "Section 7 - Operator Catalog", GetOperatorCatalog(userDescription));
        AppendSection(sb, "Section 8 - Connection Rules", GetConnectionRules());
        AppendSection(sb, "Section 9 - Parameter Inference Guide", GetParameterInferenceGuide());
        AppendSection(sb, "Section 10 - Output Format", GetOutputFormatSpec(supportsJsonMode));
        AppendSection(sb, "Section 11 - Few Shot Examples", GetFewShotExamples());

        return sb.ToString();
    }

    private static void AppendSection(StringBuilder sb, string title, string content)
    {
        if (sb.Length > 0)
            sb.AppendLine();

        sb.AppendLine($"## {title}");
        sb.AppendLine(content.Trim());
    }

    private string GetDomainKnowledge() => """
        # Industrial Vision Domain Knowledge

        ClearVision generates executable machine-vision workflows. Prefer simple, inspectable chains that an engineer can validate on site.

        ## Common patterns
        1. Defect detection:
           ImageAcquisition -> Filtering -> Thresholding -> BlobAnalysis -> ResultJudgment -> ResultOutput
        2. AI detection:
           ImageAcquisition -> ImageResize -> DeepLearning(TaskType=ObjectDetection, OutputFormat=EndToEndNms) -> optional BoxFilter -> ResultJudgment -> ResultOutput
        3. Measurement:
           ImageAcquisition -> Filtering -> feature extraction -> Measurement(choose PointToPoint/PointToLine/LineToLine/ThreePointAngle) -> UnitConvert/ResultOutput
        4. OCR or barcode:
           ImageAcquisition -> CodeRecognition/OcrRecognition -> ConditionalBranch -> DatabaseWrite/PLC output -> ResultOutput
        5. PLC decision:
           inspection result -> ConditionalBranch -> Modbus/S7/MC/FINS communication -> ResultOutput
        6. Runtime evidence:
           include ResultOutput or DatabaseWrite when the request mentions traceability, reporting, or production records.
        7. Traditional template matching:
           ImageAcquisition(current image) + ImageAcquisition(reference template) -> TemplateMatching -> ResultJudgment -> ResultOutput
        8. AI classification:
           ImageAcquisition -> DeepLearning(TaskType=ImageClassification) -> ResultJudgment -> ResultOutput
        9. AI semantic segmentation:
           ImageAcquisition -> DeepLearning(TaskType=SemanticSegmentation) -> region/statistics/judgment -> ResultOutput

        ## Practical defaults
        - Start with ImageAcquisition unless the user explicitly says the image already exists in the flow.
        - Prefer ResultJudgment before hardware output so OK/NG semantics are explicit.
        - If the user asks for traditional vision, template matching, a standard template, reference image, or golden sample comparison, use TemplateMatching and do not use DeepLearning unless the user explicitly requests an AI model.
        - Add parametersNeedingReview when thresholds, calibration files, PLC addresses, model paths, or table names cannot be inferred safely.
        - Use calibration resources for pixel-to-world conversion; do not invent millimeter values from pixel coordinates.
        - For ONNX detection models exported with candidate suppression or NMS, trust the model output: set DeepLearning.OutputFormat=EndToEndNms and do not add BoxNms.
        - Add BoxNms only when the user explicitly asks for platform-side NMS, raw candidate boxes, or a RawYolo model that does not include NMS.
        """;

    private string GetTemplateFirstStrategy() => """
        # Template First Strategy

        When the request matches a known workflow pattern, generate from that pattern first and then adapt parameters. Do not start from a random list of operators.

        Use template-like chains for defect detection, blob counting, threshold inspection, OCR/barcode, measurement, calibration, PLC output, database logging, and ordered detection tasks.

        If a template is only partially suitable:
        1. Keep the stable backbone such as acquisition, preprocessing, judgment, and result output.
        2. Replace only the task-specific operator block.
        3. Mark uncertain parameters in parametersNeedingReview instead of fabricating site values.
        """;

    private string GetPhase1OperatorExtensions() => """
        # Phase 1 Operator Extensions
        ## New workflow patterns
        1. Precision width measurement:
           ImageAcquisition -> Filtering -> CaliperTool -> WidthMeasurement -> UnitConvert -> ResultJudgment -> ResultOutput
        2. AI post-processing with model-embedded NMS:
           ImageAcquisition -> DeepLearning(OutputFormat=EndToEndNms) -> BoxFilter(optional ROI/score/class filter) -> ResultJudgment -> ResultOutput
        3. Detection sequence judgment:
           ImageAcquisition -> DeepLearning(OutputFormat=EndToEndNms) -> DetectionSequenceJudge -> ConditionalBranch/ResultOutput
        4. Legacy raw-YOLO platform-side NMS, only when explicitly required:
           ImageAcquisition -> DeepLearning(OutputFormat=RawYolo, EnableInternalNms=false) -> BoxNms -> ResultJudgment -> ResultOutput
        5. Image quality gate:
           ImageAcquisition -> SharpnessEvaluation -> ConditionalBranch -> continue or reject
        6. Calibration-assisted metrology:
           CalibrationLoader -> CoordinateTransform/PixelToWorldTransform -> measurement operators -> UnitConvert -> ResultOutput
        ## Phrase mapping additions
        - "measure width/thickness/gap" => WidthMeasurement
        - "caliper/find edge pair" => CaliperTool
        - "point to line distance" => PointLineDistance
        - "line to line distance/parallelism" => LineLineDistance
        - "remove duplicate boxes / NMS" => prefer DeepLearning.OutputFormat=EndToEndNms; use BoxNms only for explicit platform-side raw candidate suppression
        - "filter detections by class/area/score" => BoxFilter
        - "wire sequence / terminal order / connector order" => DetectionSequenceJudge
        - "traditional template matching / standard template / reference image / golden sample comparison" => TemplateMatching
        - "is image sharp / focus check / blur" => SharpnessEvaluation
        - "correct ROI position / offset compensation" => PositionCorrection
        - "N-point calibration / affine calibration" => NPointCalibration
        - "load calibration file" => CalibrationLoader
        - "pixel to mm / unit conversion" => UnitConvert
        - "cycle time / elapsed statistics" => TimerStatistics
        """;

    private string GetPhase2OperatorExtensions() => """
        # Phase 2 Operator Extensions
        ## New workflow patterns
        1. Robot vision guidance:
           ImageAcquisition -> ShapeMatching -> PixelToWorldTransform/CoordinateTransform -> PointAlignment/PointCorrection -> PlcCommunication -> ResultOutput
        2. Annular part defect inspection:
           ImageAcquisition -> CircleMeasurement(center) -> PolarUnwrap -> ShadingCorrection -> SurfaceDefectDetection -> ResultOutput
        3. Traditional surface defect detection:
           ImageAcquisition -> ShadingCorrection -> SurfaceDefectDetection -> ResultJudgment -> ResultOutput
        ## Phrase mapping additions
        - "script / custom code / formula" => ScriptOperator
        - "trigger / start / timer trigger" => TriggerModule
        - "alignment / reference point offset" => PointAlignment
        - "correction / compensation / send to robot" => PointCorrection
        - "gap / pitch / lead spacing" => GapMeasurement
        - "unwrap ring / bottle cap / bearing ring" => PolarUnwrap
        - "uneven illumination / shading / flat field" => ShadingCorrection
        - "multi-frame average / temporal denoise" => FrameAveraging
        - "affine transform / rotate scale translate" => AffineTransform
        - "color measurement / deltaE / Lab" => ColorMeasurement
        - "surface defect / scratch / stain" => SurfaceDefectDetection
        - "edge-pair defect / notch / bump" => EdgePairDefect
        - "rectangle / box / quadrilateral detection" => RectangleDetection
        - "translation-rotation calibration" => TranslationRotationCalibration
        - "hand-eye calibration / eye-in-hand / eye-to-hand" => HandEyeCalibration
        """;

    private string GetPhase3OperatorExtensions() => """
        # Phase 3 Operator Extensions
        ## New workflow patterns
        1. Large-area tiled inspection:
           ImageAcquisition -> ImageTiling -> ForEach(per-tile inspection) -> ResultJudgment -> ResultOutput
        2. Multi-view stitched inspection:
           ImageAcquisition(Image1) + ImageAcquisition(Image2) -> ImageStitching -> inspection -> ResultOutput
        3. Precision geometry chain:
           ImageAcquisition -> positioning -> GeoMeasurement(point/line/circle) -> UnitConvert -> ResultOutput
        ## Phrase mapping additions
        - "corner / vertex / corner point" => CornerDetection
        - "intersection / line crossing" => EdgeIntersection
        - "parallel lines / dual edge rails" => ParallelLineFind
        - "quadrilateral / polygon four-edge" => QuadrilateralFind
        - "geometry measurement / line-circle / circle-circle" => GeoMeasurement
        - "stitch / panorama / large image merge" => ImageStitching
        - "tiling / split grid / image blocks" => ImageTiling
        - "normalize image / standardize brightness" => ImageNormalize
        - "compose images / concat / channel merge" => ImageCompose
        - "pad border / expand image border" => CopyMakeBorder
        - "save text / export csv / save json log" => TextSave
        - "point set sort/filter/merge" => PointSetTool
        - "blob labeling / classify connected components" => BlobLabeling
        - "histogram / gray distribution" => HistogramAnalysis
        - "pixel statistics / roi mean brightness" => PixelStatistics
        """;

    private string GetRoleDefinition() => """
        # Role And Hard Rules

        You are the ClearVision workflow generation assistant. Produce a machine-vision flow that can be executed by the ClearVision operator runtime.

        Hard rules:
        1. Use only operatorType values that appear in the operator catalog.
        2. Use only port names and parameter names from the catalog or validated knowledge slice.
        3. Return strict JSON matching the output format. Do not wrap JSON in Markdown unless the caller explicitly allows it.
        4. Do not invent hardware addresses, model paths, calibration files, SQL tables, or production thresholds. Put them in parametersNeedingReview.
        5. Every operator except pure source/resource loaders should be connected to the flow. Avoid isolated nodes.
        6. Prefer safe defaults and explain assumptions briefly in explanation.
        7. When asked to use Chinese text, localize only user-visible displayName, explanation, and notes. Never translate operatorType, port names, parameter names, or runtime JSON keys.
        8. Prefer operators with default_ai_recommendation=true. Never choose Legacy or Deprecated operators for a new flow. Use Reference only when no Stable/Experimental alternative fits, and disclose its lifecycle boundary. Always disclose Experimental lifecycle notes.
        9. Respect parameter_conditions and output_conditions for the selected mode; do not configure ignored parameters or connect outputs that are unavailable.
        """;

    private string GetParameterInferenceGuide() => """
        # Parameter Inference Guide

        1. Thresholds and tolerances
        - Infer rough starting values only when the user gives a clear requirement such as "defects larger than 0.5 mm".
        - Otherwise use conservative defaults from the catalog and list the parameter in parametersNeedingReview.

        2. Calibration and physical units
        - Use CalibrationBundleV2 as the runtime calibration contract.
        - If the request mentions millimeters, robot coordinates, or physical position, include CalibrationLoader or CoordinateTransform when available.
        - Feed calibration consumers through CalibrationData when possible, and mark calibration bundle paths and coordinate-system choices for review.

        3. AI models
        - Always set DeepLearning.TaskType explicitly unless the model catalog type or ONNX output shape can reliably resolve Auto.
        - For ObjectDetection, use InputSize, Confidence, OutputFormat, TargetClasses, and NMS parameters; prefer OutputFormat=EndToEndNms when the model owns NMS.
        - For ImageClassification, use ClassificationInputSize, ClassificationScoreMode, TopK, and ClassNames when metadata/catalog labels are unavailable. Do not add detection thresholds or BoxNms.
        - For SemanticSegmentation, use SegmentationInputSize, NumClasses, MaxClassMasks, and ClassNames. Do not require detection labels, Confidence, or NMS parameters.
        - Do not set EnableInternalNms=false unless the user explicitly says an ObjectDetection ONNX model emits raw YOLO candidates for downstream platform-side NMS.
        - Do not add BoxNms after ObjectDetection when OutputFormat=EndToEndNms; the ONNX model owns candidate suppression/NMS.
        - If any model resource is missing, list it in parametersNeedingReview.

        4. Generalized operators
        - Set Filtering.FilterMode to Gaussian, Mean, Median, or Bilateral and only configure parameters used by that mode.
        - Set Measurement.MeasureType explicitly and connect the matching PointA/PointB/PointC or Line1/Line2 inputs; use legacy X1/Y1/X2/Y2 only for point-distance modes without point inputs.

        5. Hardware and database output
        - Do not invent IP addresses, PLC station numbers, register addresses, credentials, or table names.
        - Use placeholder-safe defaults only when the operator requires a value, and mark them for review.
        """;

    private string GetOperatorCatalog(string? userDescription)
    {
        var allMetadata = _operatorFactory
            .GetAllMetadata()
            .Where(metadata => !metadata.DefaultHidden)
            .OrderByDescending(metadata => ImageContractPresentationBuilder.IsDefaultAiRecommendation(
                metadata.Lifecycle,
                metadata.ImageInputContracts))
            .ThenBy(metadata => OperatorCategoryCatalog.GetOrder(metadata.CategoryId))
            .ThenBy(metadata => metadata.Type.ToString(), StringComparer.OrdinalIgnoreCase)
            .ToList();

        OperatorKnowledgeSlice? slice = null;
        List<OperatorMetadata> relevantMetadata;
        if (_operatorKnowledgeRetriever == null)
        {
            relevantMetadata = string.IsNullOrWhiteSpace(userDescription)
                ? allMetadata
                : GetRelevantOperators(userDescription)
                    .OrderByDescending(meta => ImageContractPresentationBuilder.IsDefaultAiRecommendation(
                        meta.Lifecycle,
                        meta.ImageInputContracts))
                    .ThenBy(meta => OperatorCategoryCatalog.GetOrder(meta.CategoryId))
                    .ThenBy(meta => meta.Type.ToString(), StringComparer.OrdinalIgnoreCase)
                    .ToList();
        }
        else
        {
            slice = _operatorKnowledgeRetriever.RetrieveAsync(new OperatorKnowledgeQuery
            {
                Description = userDescription,
                TopN = string.IsNullOrWhiteSpace(userDescription) ? allMetadata.Count : 28
            })
                .GetAwaiter()
                .GetResult();
            slice = FilterValidatedKnowledgeSlice(slice, allMetadata);

            relevantMetadata = slice.PrioritizedOperatorTypes.Count == 0
                ? allMetadata
                : allMetadata
                    .Where(meta => slice.PrioritizedOperatorTypes.Contains(meta.Type.ToString(), StringComparer.OrdinalIgnoreCase))
                    .OrderByDescending(meta => ImageContractPresentationBuilder.IsDefaultAiRecommendation(
                        meta.Lifecycle,
                        meta.ImageInputContracts))
                    .ThenBy(meta => OperatorCategoryCatalog.GetOrder(meta.CategoryId))
                    .ThenBy(meta => meta.Type.ToString(), StringComparer.OrdinalIgnoreCase)
                    .ToList();
        }

        var sb = new StringBuilder();

        if (string.IsNullOrWhiteSpace(userDescription))
        {
            sb.AppendLine("# Full Operator Catalog");
        }
        else
        {
            sb.AppendLine("# Prioritized Operator Catalog");
            sb.AppendLine("This section lists operators that are most relevant to the current request.");
            if (!string.IsNullOrWhiteSpace(slice?.RetrievalSummary))
                sb.AppendLine($"Retrieval summary: {slice.RetrievalSummary}");
            sb.AppendLine("If a required operator is not listed here, use the full fallback catalog below.");
        }

        sb.AppendLine();
        sb.AppendLine("```json");
        sb.AppendLine(SerializeOperatorCatalog(relevantMetadata, includeFullDetails: true));
        sb.AppendLine("```");

        if (slice is { Cards.Count: > 0 })
        {
            sb.AppendLine();
            sb.AppendLine("# Operator Knowledge Slice");
            sb.AppendLine("These validated knowledge cards came from the scenario retrieval graph and may constrain generation:");
            sb.AppendLine("- requiredResources: models, calibration bundles, or configuration resources that must exist.");
            sb.AppendLine("- antiPatterns: combinations to avoid.");
            sb.AppendLine("- typicalUpstream/typicalDownstream: common industrial wiring relationships.");
            sb.AppendLine("- knownLimitations/evidence: risk boundaries and validation status.");
            sb.AppendLine();
            sb.AppendLine("```json");
            sb.AppendLine(SerializeKnowledgeSlice(slice));
            sb.AppendLine("```");
        }

        if (!string.IsNullOrWhiteSpace(userDescription))
        {
            sb.AppendLine();
            sb.AppendLine("# Full Catalog Fallback");
            sb.AppendLine("Use this compact fallback catalog if the relevant operator subset is not enough.");
            sb.AppendLine();
            sb.AppendLine("```json");
            sb.AppendLine(SerializeOperatorCatalog(allMetadata, includeFullDetails: false));
            sb.AppendLine("```");
        }

        return sb.ToString();
    }

    private static OperatorKnowledgeSlice FilterValidatedKnowledgeSlice(
        OperatorKnowledgeSlice slice,
        IReadOnlyCollection<OperatorMetadata> metadata)
    {
        var metadataByType = metadata
            .ToDictionary(item => item.Type.ToString(), StringComparer.OrdinalIgnoreCase);

        var validCards = slice.Cards
            .Where(card => IsValidatedKnowledgeCard(card, metadataByType))
            .OrderBy(card => card.OperatorType, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var validTypes = validCards
            .Select(card => card.OperatorType)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return new OperatorKnowledgeSlice
        {
            PrioritizedOperatorTypes = slice.PrioritizedOperatorTypes
                .Where(type => validTypes.Contains(type))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            Cards = validCards,
            MatchedScenarioKeys = slice.MatchedScenarioKeys
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            RetrievalSummary = slice.RetrievalSummary
        };
    }

    private static bool IsValidatedKnowledgeCard(
        OperatorKnowledgeCard card,
        IReadOnlyDictionary<string, OperatorMetadata> metadataByType)
    {
        if (string.IsNullOrWhiteSpace(card.OperatorType))
            return false;

        if (!Enum.TryParse<OperatorType>(card.OperatorType, ignoreCase: true, out var parsedType) ||
            !Enum.IsDefined(typeof(OperatorType), parsedType))
        {
            return false;
        }

        if (!metadataByType.TryGetValue(card.OperatorType, out var metadata))
            return false;

        return HaveSameNameSet(
                   card.Inputs.Select(item => item.Name),
                   metadata.InputPorts.Select(item => item.Name))
               && HaveSameNameSet(
                   card.Outputs.Select(item => item.Name),
                   metadata.OutputPorts.Select(item => item.Name))
               && HaveSameNameSet(
                   card.Parameters.Select(item => item.Name),
                   metadata.Parameters.Select(item => item.Name));
    }

    private static bool HaveSameNameSet(IEnumerable<string> left, IEnumerable<string> right)
    {
        var leftSet = left
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var rightSet = right
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return leftSet.SetEquals(rightSet);
    }

    private static string SerializeKnowledgeSlice(OperatorKnowledgeSlice slice)
    {
        var payload = new
        {
            retrievalSummary = slice.RetrievalSummary,
            matchedScenarioKeys = slice.MatchedScenarioKeys,
            cards = slice.Cards
                .OrderBy(card => card.OperatorType, StringComparer.OrdinalIgnoreCase)
                .Select(card => new
                {
                    operatorType = card.OperatorType,
                    displayName = card.DisplayName,
                    categoryId = card.CategoryId,
                    categoryOrder = card.CategoryOrder,
                    category = card.Category,
                    lifecycle = card.Lifecycle,
                    lifecycleNote = card.LifecycleNote,
                    defaultAiRecommendation = card.DefaultAiRecommendation,
                    requiresLifecycleDisclosure = card.RequiresLifecycleDisclosure,
                    intentTags = card.IntentTags,
                    scenarioTags = card.ScenarioTags,
                    requiredResources = card.RequiredResources,
                    resourceRequirements = card.ResourceRequirements,
                    parameterConditions = card.ParameterConditions,
                    outputConditions = card.OutputConditions,
                    imageInputContracts = card.ImageInputContracts.Count > 0
                        ? ImageContractPresentationBuilder.BuildModeIndex(card.ImageInputContracts)
                        : ImageContractPresentationBuilder.BuildModeIndex(card.ImageInputContractPresentations),
                    typicalUpstream = card.TypicalUpstream,
                    typicalDownstream = card.TypicalDownstream,
                    antiPatterns = card.AntiPatterns,
                    knownLimitations = card.KnownLimitations,
                    evidence = new
                    {
                        contract = card.Evidence.Contract,
                        golden = card.Evidence.Golden,
                        dataset = card.Evidence.Dataset,
                        fieldReplay = card.Evidence.FieldReplay,
                        precisionClaim = card.Evidence.PrecisionClaim,
                        industrialStatus = card.Evidence.IndustrialStatus,
                        qScore = card.Evidence.QScore
                    }
                })
                .ToList()
        };

        return JsonSerializer.Serialize(payload, _catalogJsonOptions);
    }

    private static string SerializeOperatorCatalog(IEnumerable<OperatorMetadata> metadata, bool includeFullDetails)
    {
        if (!includeFullDetails)
        {
            var fallbackCatalog = metadata.Select(m => new
            {
                operator_id = m.Type.ToString(),
                name = m.DisplayName,
                category_id = m.CategoryId.ToString(),
                category_order = OperatorCategoryCatalog.GetOrder(m.CategoryId),
                category = m.Category,
                lifecycle = m.Lifecycle.ToString(),
                lifecycle_note = m.LifecycleNote,
                default_ai_recommendation = ImageContractPresentationBuilder.IsDefaultAiRecommendation(
                    m.Lifecycle,
                    m.ImageInputContracts),
                requires_lifecycle_disclosure = ImageContractPresentationBuilder.RequiresAiDisclosure(
                    m.Lifecycle,
                    m.ImageInputContracts),
                image_contract = ImageContractPresentationBuilder.Summarize(m.ImageInputContracts)
            });

            return JsonSerializer.Serialize(fallbackCatalog, _catalogJsonOptions);
        }

        var detailedCatalog = metadata.Select(m => new
        {
            operator_id = m.Type.ToString(),
            name = m.DisplayName,
            category_id = m.CategoryId.ToString(),
            category_order = OperatorCategoryCatalog.GetOrder(m.CategoryId),
            category = m.Category,
            description = m.Description,
            lifecycle = m.Lifecycle.ToString(),
            lifecycle_note = m.LifecycleNote,
            default_hidden = m.DefaultHidden,
            default_ai_recommendation = ImageContractPresentationBuilder.IsDefaultAiRecommendation(
                m.Lifecycle,
                m.ImageInputContracts),
            requires_lifecycle_disclosure = ImageContractPresentationBuilder.RequiresAiDisclosure(
                m.Lifecycle,
                m.ImageInputContracts),
            keywords = m.Keywords ?? Array.Empty<string>(),
            inputs = m.InputPorts.Select(p => new
            {
                port_name = p.Name,
                display_name = p.DisplayName,
                data_type = p.DataType.ToString(),
                required = p.IsRequired
            }),
            outputs = m.OutputPorts.Select(p => new
            {
                port_name = p.Name,
                display_name = p.DisplayName,
                data_type = p.DataType.ToString()
            }),
            parameters = m.Parameters.Select(p => new
            {
                param_name = p.Name,
                display_name = p.DisplayName,
                type = p.DataType,
                default_value = p.DefaultValue?.ToString(),
                required = p.IsRequired,
                description = p.Description ?? string.Empty,
                min_value = p.MinValue?.ToString(),
                max_value = p.MaxValue?.ToString(),
                options = p.Options?.Select(o => new { label = o.Label, value = o.Value })
            }),
            parameter_conditions = m.ParameterConstraints,
            output_conditions = m.OutputAvailabilityRules,
            image_input_contracts = ImageContractPresentationBuilder.BuildModeIndex(m.ImageInputContracts),
            generation_dependencies = m.GenerationDependencies
        });

        return JsonSerializer.Serialize(detailedCatalog, _catalogJsonOptions);
    }

    private List<OperatorMetadata> GetRelevantOperators(string userDescription)
    {
        var allMetadata = _operatorFactory.GetAllMetadata().ToList();
        if (allMetadata.Count == 0 || string.IsNullOrWhiteSpace(userDescription))
            return allMetadata;

        var keywords = ExtractKeywords(userDescription);
        var matched = allMetadata
            .Where(metadata => IsRelevantByKeywords(metadata, keywords))
            .ToList();

        if (matched.Count < 8)
        {
            var categoryHints = keywords
                .Select(GetCategoryHint)
                .Where(hint => hint.HasValue)
                .Select(hint => hint!.Value)
                .Distinct()
                .ToList();

            if (categoryHints.Count > 0)
            {
                matched.AddRange(allMetadata.Where(metadata =>
                    categoryHints.Contains(metadata.CategoryId)));
            }
        }

        matched.AddRange(GetCoreOperators(allMetadata));

        var distinct = matched
            .GroupBy(metadata => metadata.Type)
            .Select(group => group.First())
            .ToList();

        return distinct.Count > 0 ? distinct : allMetadata;
    }

    private static HashSet<string> ExtractKeywords(string description)
    {
        var normalized = description.ToLowerInvariant();
        var keywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match match in Regex.Matches(normalized, @"[\p{L}\p{Nd}_]+"))
        {
            var token = match.Value.Trim();
            if (token.Length >= 2)
                keywords.Add(token);
        }

        AddIntentTokensIfMatched(keywords, normalized, ["measurement", "measure", "gap", "distance", "width", "caliper", "mm", "um"], ["measurement", "gap", "distance", "width", "caliper"]);
        AddIntentTokensIfMatched(keywords, normalized, ["defect", "blob", "threshold", "ng"], ["defect", "blob", "threshold"]);
        AddIntentTokensIfMatched(keywords, normalized, ["communication", "plc", "modbus", "s7", "tcp"], ["communication", "modbus", "siemens", "mitsubishi", "omron"]);
        AddIntentTokensIfMatched(keywords, normalized, ["ocr", "barcode", "recognition", "code"], ["ocr", "code", "barcode", "recognition"]);
        AddIntentTokensIfMatched(keywords, normalized, ["ai", "yolo", "deeplearning", "inference"], ["ai", "deeplearning", "inference"]);
        AddIntentTokensIfMatched(keywords, normalized, ["calibration", "undistort", "coordinate"], ["calibration", "undistort", "coordinate"]);
        AddIntentTokensIfMatched(keywords, normalized, ["传统视觉", "模板匹配", "标准模板", "参考图", "基准图", "template matching", "reference image", "golden sample"], ["模板匹配", "template", "match", "locate"]);

        return keywords;
    }

    private static void AddIntentTokensIfMatched(
        HashSet<string> keywords,
        string normalizedDescription,
        IEnumerable<string> triggers,
        IEnumerable<string> tokensToAdd)
    {
        if (!triggers.Any(trigger => normalizedDescription.Contains(trigger, StringComparison.OrdinalIgnoreCase)))
            return;

        foreach (var token in tokensToAdd)
            keywords.Add(token);
    }

    private static bool IsRelevantByKeywords(OperatorMetadata metadata, HashSet<string> keywords)
    {
        if (keywords.Count == 0)
            return false;

        if (keywords.Any(keyword =>
                ContainsIgnoreCase(metadata.DisplayName, keyword) ||
                ContainsIgnoreCase(metadata.Description, keyword) ||
                ContainsIgnoreCase(metadata.Category, keyword)))
        {
            return true;
        }

        return metadata.Keywords != null &&
               metadata.Keywords.Any(operatorKeyword =>
                   keywords.Any(keyword => ContainsIgnoreCase(operatorKeyword, keyword)));
    }

    private static bool ContainsIgnoreCase(string? source, string keyword)
    {
        if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(keyword))
            return false;

        return source.Contains(keyword, StringComparison.OrdinalIgnoreCase);
    }

    private static OperatorCategoryId? GetCategoryHint(string keyword)
    {
        if (keyword.Contains("measure", StringComparison.OrdinalIgnoreCase) ||
            keyword.Contains("distance", StringComparison.OrdinalIgnoreCase) ||
            keyword.Contains("width", StringComparison.OrdinalIgnoreCase))
        {
            return OperatorCategoryId.Measurement;
        }

        if (keyword.Contains("defect", StringComparison.OrdinalIgnoreCase) ||
            keyword.Contains("blob", StringComparison.OrdinalIgnoreCase) ||
            keyword.Contains("ng", StringComparison.OrdinalIgnoreCase))
        {
            return OperatorCategoryId.DefectDetection;
        }

        if (keyword.Contains("communication", StringComparison.OrdinalIgnoreCase) ||
            keyword.Contains("plc", StringComparison.OrdinalIgnoreCase) ||
            keyword.Contains("modbus", StringComparison.OrdinalIgnoreCase))
        {
            return OperatorCategoryId.Communication;
        }

        if (keyword.Contains("ocr", StringComparison.OrdinalIgnoreCase) ||
            keyword.Contains("barcode", StringComparison.OrdinalIgnoreCase) ||
            keyword.Contains("recognition", StringComparison.OrdinalIgnoreCase))
        {
            return OperatorCategoryId.FeatureExtraction;
        }

        if (keyword.Contains("calibration", StringComparison.OrdinalIgnoreCase) ||
            keyword.Contains("undistort", StringComparison.OrdinalIgnoreCase) ||
            keyword.Contains("coordinate", StringComparison.OrdinalIgnoreCase))
        {
            return OperatorCategoryId.CalibrationAndCoordinates;
        }

        if (keyword.Contains("template", StringComparison.OrdinalIgnoreCase) ||
            keyword.Contains("match", StringComparison.OrdinalIgnoreCase) ||
            keyword.Contains("模板", StringComparison.OrdinalIgnoreCase))
        {
            return OperatorCategoryId.MatchingAndLocalization;
        }

        if (keyword.Contains("ai", StringComparison.OrdinalIgnoreCase) ||
            keyword.Contains("onnx", StringComparison.OrdinalIgnoreCase) ||
            keyword.Contains("inference", StringComparison.OrdinalIgnoreCase))
        {
            return OperatorCategoryId.AiInference;
        }

        if (keyword.Contains("segment", StringComparison.OrdinalIgnoreCase) ||
            keyword.Contains("region", StringComparison.OrdinalIgnoreCase))
        {
            return OperatorCategoryId.SegmentationAndRegion;
        }

        if (keyword.Contains("pointcloud", StringComparison.OrdinalIgnoreCase) ||
            keyword.Contains("3d", StringComparison.OrdinalIgnoreCase))
        {
            return OperatorCategoryId.PointCloud3D;
        }

        return null;
    }

    private static List<OperatorMetadata> GetCoreOperators(IEnumerable<OperatorMetadata> allMetadata)
    {
        var coreTypes = new HashSet<OperatorType>
        {
            OperatorType.ImageAcquisition,
            OperatorType.ResultOutput,
            OperatorType.ResultJudgment,
            OperatorType.ConditionalBranch
        };

        return allMetadata
            .Where(metadata => coreTypes.Contains(metadata.Type))
            .ToList();
    }

    private string GetConnectionRules() => """
        # Connection Rules

        Connect compatible data types only:
        - Image output to Image input.
        - Integer/Float numeric outputs to numeric inputs.
        - Boolean outputs to Boolean or branch condition inputs.
        - String/Text outputs to String/Text inputs.
        - Point/Rectangle/Contour outputs to matching geometry inputs.
        - Any accepts mixed payloads but should be used deliberately.

        Flow quality rules:
        - A camera or file acquisition node normally starts the graph.
        - ResultOutput, DatabaseWrite, or a hardware communication operator normally ends the graph.
        - Use ConditionalBranch when OK/NG paths differ.
        - Do not connect a node to itself.
        - Keep port names exact; never translate or rename catalog ports.
        """;

    private string GetOutputFormatSpec(bool supportsJsonMode = true)
    {
        var baseSpec = """
        # Output Format

        Return exactly one JSON object with this shape:
        {
          "explanation": "Short explanation of the workflow and assumptions.",
          "operators": [
            {
              "tempId": "op_1",
              "operatorType": "ImageAcquisition",
              "displayName": "Image Acquisition",
              "parameters": { "ParameterName": "value" }
            }
          ],
          "connections": [
            {
              "sourceTempId": "op_1",
              "sourcePortName": "Image",
              "targetTempId": "op_2",
              "targetPortName": "Image"
            }
          ],
          "parametersNeedingReview": {
            "op_1": ["ParameterName"]
          }
        }

        Requirements:
        - tempId values must be stable within the response and referenced by connections.
        - operatorType must exactly match an operator_id from the catalog.
        - port and parameter names must exactly match the catalog.
        - The top-level JSON object must contain operators as an array and connections as an array.
        - Do not return an outer envelope such as {"workflow": ...}, {"flow": ...}, {"result": ...}, {"data": ...}, or {"answer": ...}.
        - Do not return the JSON object as an escaped string value.
        - Do not use alternate top-level names such as nodes/edges/steps/modules in the final answer.
        - Parameter values should be strings. For unknown values, omit the parameter or add it to parametersNeedingReview.
        - parametersNeedingReview may be omitted only when no uncertain values remain.
        - The first character of the response must be { and the last character must be }.
        - Do not add Markdown code block markers, comments, or explanatory text outside the JSON object.
        - Ensure all brackets and braces are properly paired before sending.
        - Before replying, mentally validate that the response can be parsed by JSON.parse and deserialized into the exact shape above.
        """;

        if (!supportsJsonMode)
        {
            return baseSpec + """

                ## strict JSON output (model does not support JSON Mode)
                Your response MUST satisfy the following:
                1. Do not use Markdown code block markers.
                2. Do not add explanatory text before or after the JSON.
                3. The first character must be { and the last must be }.
                4. All string values must use double quotes.
                5. Do not add comments in JSON.
                6. Ensure all brackets and braces are properly paired.
                7. Do not wrap the object in another object and do not return JSON as a string.
                """;
        }

        return baseSpec;
    }

    private string GetFewShotExamples() => """
        # Few Shot Examples

        ## Example 1
        User request: "detect bright defects and output OK/NG"
        Expected output:
        {
          "explanation": "Capture an image, smooth noise, threshold bright regions, count blobs, judge OK/NG, and output the result. Blob area limits need site tuning.",
          "operators": [
            {"tempId": "op_1", "operatorType": "ImageAcquisition", "displayName": "Image Acquisition", "parameters": {"SourceType": "Camera", "TriggerMode": "Software"}},
            {"tempId": "op_2", "operatorType": "Filtering", "displayName": "Noise Filter", "parameters": {"FilterMode": "Gaussian", "KernelSize": "5"}},
            {"tempId": "op_3", "operatorType": "Thresholding", "displayName": "Bright Region Threshold", "parameters": {"UseOtsu": "true"}},
            {"tempId": "op_4", "operatorType": "BlobAnalysis", "displayName": "Defect Blob Analysis", "parameters": {"MinArea": "50", "MaxArea": "5000"}},
            {"tempId": "op_5", "operatorType": "ResultOutput", "displayName": "Result Output", "parameters": {"Format": "JSON", "SaveToFile": "false"}}
          ],
          "connections": [
            {"sourceTempId": "op_1", "sourcePortName": "Image", "targetTempId": "op_2", "targetPortName": "Image"},
            {"sourceTempId": "op_2", "sourcePortName": "Image", "targetTempId": "op_3", "targetPortName": "Image"},
            {"sourceTempId": "op_3", "sourcePortName": "Image", "targetTempId": "op_4", "targetPortName": "Image"},
            {"sourceTempId": "op_4", "sourcePortName": "BlobCount", "targetTempId": "op_5", "targetPortName": "Result"}
          ],
          "parametersNeedingReview": {"op_4": ["MinArea", "MaxArea"]}
        }

        ## Example 2
        User request: "read a QR code and send the result to PLC over Modbus TCP"
        Expected output:
        {
          "explanation": "Capture an image, decode the QR code, write the decoded text to a Modbus endpoint, and output the operation result. PLC connection details must be reviewed.",
          "operators": [
            {"tempId": "op_1", "operatorType": "ImageAcquisition", "displayName": "Image Acquisition", "parameters": {"SourceType": "Camera", "TriggerMode": "Software"}},
            {"tempId": "op_2", "operatorType": "CodeRecognition", "displayName": "QR Code Recognition", "parameters": {"CodeType": "QR", "MaxResults": "1"}},
            {"tempId": "op_3", "operatorType": "ModbusCommunication", "displayName": "Modbus Write", "parameters": {"Protocol": "TCP", "Port": "502", "FunctionCode": "WriteMultiple"}},
            {"tempId": "op_4", "operatorType": "ResultOutput", "displayName": "Result Output", "parameters": {"Format": "JSON", "SaveToFile": "false"}}
          ],
          "connections": [
            {"sourceTempId": "op_1", "sourcePortName": "Image", "targetTempId": "op_2", "targetPortName": "Image"},
            {"sourceTempId": "op_2", "sourcePortName": "Text", "targetTempId": "op_3", "targetPortName": "Data"},
            {"sourceTempId": "op_3", "sourcePortName": "Response", "targetTempId": "op_4", "targetPortName": "Text"}
          ],
          "parametersNeedingReview": {"op_3": ["IpAddress", "SlaveId", "RegisterAddress"]}
        }

        ## Example 3
        User request: "convert pixel point (120,160) to millimeter coordinates and output it"
        Expected output:
        {
          "explanation": "Load calibration data, convert the pixel point into physical coordinates, and output the converted values. The calibration file path needs review.",
          "operators": [
            {"tempId": "op_1", "operatorType": "CalibrationLoader", "displayName": "Calibration Loader", "parameters": {"FilePath": "calibration_bundle_v2.json"}},
            {"tempId": "op_2", "operatorType": "CoordinateTransform", "displayName": "像素到物理坐标（单点）", "parameters": {"PixelX": "120", "PixelY": "160"}},
            {"tempId": "op_3", "operatorType": "ResultOutput", "displayName": "Result Output", "parameters": {"Format": "JSON", "SaveToFile": "false"}}
          ],
          "connections": [
            {"sourceTempId": "op_1", "sourcePortName": "CalibrationData", "targetTempId": "op_2", "targetPortName": "CalibrationData"},
            {"sourceTempId": "op_2", "sourcePortName": "PhysicalX", "targetTempId": "op_3", "targetPortName": "Result"}
          ],
          "parametersNeedingReview": {"op_1": ["FilePath"]}
        }
        """;
}
