using System.Text.RegularExpressions;
using ClearVision.Product.Core.DTOs;
using ClearVision.Product.Infrastructure.AI.Tools;

namespace ClearVision.Product.Infrastructure.AI.Agent;

internal sealed record VisionAgentPlanFidelityValidationResult(
    VisionAgentRecommendedRoute Route,
    VisionAgentPlanFidelityAssessment Assessment,
    IReadOnlyList<string> Risks,
    IReadOnlyList<string> RepairNotes,
    IReadOnlyList<string> Warnings);

/// <summary>
/// Fail-closed post-planner validation for user-named capabilities and promised business outputs.
/// The model may propose a route, but only the live operator catalog is authoritative.
/// </summary>
internal sealed class VisionAgentPlanFidelityValidator
{
    private static readonly Regex CannyPattern = new(
        "(?<![A-Za-z0-9])canny(?![A-Za-z0-9])|坎尼",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private readonly IVisionAgentOperatorContractCatalog _catalog;

    public VisionAgentPlanFidelityValidator()
        : this(new VisionAgentOperatorContractCatalog())
    {
    }

    internal VisionAgentPlanFidelityValidator(IVisionAgentOperatorContractCatalog catalog)
    {
        _catalog = catalog;
    }

    public VisionAgentPlanFidelityValidationResult Validate(
        VisionAgentPlanModeRequest request,
        VisionAgentPlanModeResult plan)
    {
        var taskResolution = VisionAgentTaskTypeResolver.Resolve(plan, plan.ConfirmedPlanAnswers);
        var taskType = taskResolution.CanonicalValue;
        var evidenceText = BuildEvidenceText(request, plan);
        var requestedOperators = (plan.RecommendedRoute.Operators ?? [])
            .Select(_catalog.CanonicalizeOperatorType)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToList();
        var constraints = new List<VisionAgentCapabilityConstraint>();
        var requiredOutputs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var evidence = new List<string>();
        var repairNotes = new List<string>();
        var warnings = new List<string>();
        var repaired = false;

        if (CannyPattern.IsMatch(evidenceText) && !ExplicitAlternativeAccepted(plan))
        {
            constraints.Add(new VisionAgentCapabilityConstraint
            {
                Id = "edge_detection_canny",
                OperatorType = "EdgeDetection",
                ParameterName = "Method",
                RequiredValue = "Canny",
                Source = "explicit_user_requirement",
                EvidenceText = "Canny"
            });
        }

        AddExplicitOperatorConstraints(evidenceText, constraints);
        AddRequiredOutputs(evidenceText, plan, taskType, requiredOutputs);

        foreach (var constraint in constraints
                     .GroupBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
                     .Select(group => group.First()))
        {
            if (!CatalogSatisfies(constraint, out var catalogEvidence))
            {
                evidence.Add($"catalog_capability_missing:{constraint.Id}");
                continue;
            }

            evidence.Add(catalogEvidence);
            if (!requestedOperators.Contains(constraint.OperatorType, StringComparer.OrdinalIgnoreCase))
            {
                InsertBeforeBusinessTerminals(requestedOperators, constraint.OperatorType);
                repaired = true;
                repairNotes.Add($"required_capability_restored:{constraint.Id}");
            }
        }

        foreach (var output in requiredOutputs)
        {
            if (RouteCanProduce(output, requestedOperators))
            {
                continue;
            }

            var producer = PreferredProducer(output, taskType, evidenceText);
            if (!string.IsNullOrWhiteSpace(producer) && _catalog.TryGet(producer, out _))
            {
                InsertBeforeBusinessTerminals(requestedOperators, producer);
                repaired = true;
                repairNotes.Add($"required_output_producer_restored:{output}:{producer}");
            }
        }

        if ((constraints.Count > 0 || requiredOutputs.Count > 0) &&
            !requestedOperators.Contains("ResultOutput", StringComparer.OrdinalIgnoreCase) &&
            _catalog.TryGet("ResultOutput", out _))
        {
            requestedOperators.Add("ResultOutput");
            repaired = true;
            repairNotes.Add("required_result_output_restored");
        }

        if (repaired)
        {
            requestedOperators = NormalizePipelineOrder(requestedOperators);
        }

        var satisfiedCapabilities = constraints
            .Where(constraint =>
                CatalogSatisfies(constraint, out _) &&
                requestedOperators.Contains(constraint.OperatorType, StringComparer.OrdinalIgnoreCase))
            .Select(constraint => constraint.Id)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var missingCapabilities = constraints
            .Select(constraint => constraint.Id)
            .Except(satisfiedCapabilities, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var availableOutputs = requiredOutputs
            .Where(output => RouteCanProduce(output, requestedOperators))
            .ToList();
        var missingOutputs = requiredOutputs
            .Except(availableOutputs, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var blockers = missingCapabilities
            .Select(value => $"plan_fidelity_missing_capability:{SafeKey(value)}")
            .Concat(missingOutputs.Select(value => $"plan_fidelity_missing_output:{SafeKey(value)}"))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var risks = (plan.Risks ?? [])
            .Where(risk => !ContradictsCatalogFact(risk, constraints))
            .ToList();
        if (risks.Count != (plan.Risks?.Count ?? 0))
        {
            repaired = true;
            warnings.Add("planner_catalog_fact_corrected:canny");
            repairNotes.Add("catalog_fact_contradiction_removed");
        }

        var assessment = new VisionAgentPlanFidelityAssessment
        {
            ContractVersion = VisionAgentPlanContractVersions.V2,
            TaskType = taskType,
            Satisfied = blockers.Count == 0,
            Repaired = repaired,
            RequiredCapabilities = constraints
                .GroupBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList(),
            RequiredOutputSemantics = requiredOutputs.ToList(),
            SatisfiedCapabilities = satisfiedCapabilities,
            AvailableOutputSemantics = availableOutputs,
            MissingCapabilities = missingCapabilities,
            MissingOutputSemantics = missingOutputs,
            BlockingReasons = blockers,
            Evidence = evidence
                .Append($"operator_catalog_count:{_catalog.OperatorTypes.Count}")
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList()
        };

        return new VisionAgentPlanFidelityValidationResult(
            plan.RecommendedRoute with { Operators = requestedOperators },
            assessment,
            risks,
            repairNotes,
            warnings);
    }

    private void AddExplicitOperatorConstraints(
        string evidenceText,
        ICollection<VisionAgentCapabilityConstraint> constraints)
    {
        foreach (var contract in _catalog.Operators)
        {
            if (contract.OperatorType.Length < 6 ||
                !Regex.IsMatch(
                    evidenceText,
                    $"(?<![A-Za-z0-9]){Regex.Escape(contract.OperatorType)}(?![A-Za-z0-9])",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            {
                continue;
            }

            constraints.Add(new VisionAgentCapabilityConstraint
            {
                Id = $"operator_{SafeKey(contract.OperatorType)}",
                OperatorType = contract.OperatorType,
                Source = "explicit_user_requirement",
                EvidenceText = contract.OperatorType
            });
        }
    }

    private bool CatalogSatisfies(
        VisionAgentCapabilityConstraint constraint,
        out string evidence)
    {
        evidence = string.Empty;
        if (!_catalog.TryGet(constraint.OperatorType, out var contract))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(constraint.ParameterName))
        {
            evidence = $"catalog_operator_present:{contract.OperatorType}";
            return true;
        }

        var parameter = contract.Parameters.FirstOrDefault(item =>
            item.Name.Equals(constraint.ParameterName, StringComparison.OrdinalIgnoreCase));
        if (parameter == null)
        {
            return false;
        }

        var defaultMatches = string.Equals(
            Convert.ToString(parameter.DefaultValue),
            constraint.RequiredValue,
            StringComparison.OrdinalIgnoreCase);
        var optionMatches = parameter.Options?.Any(option =>
            string.Equals(option.Value?.ToString(), constraint.RequiredValue, StringComparison.OrdinalIgnoreCase)) == true;
        if (!defaultMatches && !optionMatches)
        {
            return false;
        }

        evidence = $"catalog_parameter_present:{contract.OperatorType}.{parameter.Name}={constraint.RequiredValue}";
        return true;
    }

    private static void AddRequiredOutputs(
        string evidenceText,
        VisionAgentPlanModeResult plan,
        string taskType,
        ISet<string> outputs)
    {
        var normalized = evidenceText.ToLowerInvariant();
        var targetValues = string.Join(" ", (plan.ConfirmedPlanAnswers ?? [])
            .Where(answer =>
                VisionAgentPlanFieldPolicy.NormalizeField(answer.Field) is
                    VisionAgentPlanAnswerFields.OutputTarget or
                    VisionAgentPlanAnswerFields.MeasurementTarget)
            .Select(answer => answer.Value));
        normalized = $"{normalized} {plan.SemanticExtraction?.OutputTarget} {plan.SemanticExtraction?.MeasurementTarget} {targetValues}".ToLowerInvariant();

        if (taskType.Equals(AiVisionTaskTypes.SurfaceDefect, StringComparison.OrdinalIgnoreCase) &&
            (normalized.Contains("defect_area", StringComparison.Ordinal) ||
             normalized.Contains("面积", StringComparison.Ordinal) ||
             Regex.IsMatch(normalized, "(?<![a-z])area(?![a-z])", RegexOptions.CultureInvariant)))
        {
            outputs.Add("defect_area");
        }

        if (normalized.Contains("decoded_text", StringComparison.Ordinal) ||
            normalized.Contains("码值", StringComparison.Ordinal) ||
            normalized.Contains("识别内容", StringComparison.Ordinal))
        {
            outputs.Add("decoded_text");
        }

        if (normalized.Contains("位姿", StringComparison.Ordinal) ||
            normalized.Contains("匹配位置", StringComparison.Ordinal) ||
            normalized.Contains("matches", StringComparison.Ordinal))
        {
            outputs.Add("template_pose_matches");
        }

        if (taskType.Equals(AiVisionTaskTypes.GeometryMeasurement, StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(plan.SemanticExtraction?.MeasurementTarget))
        {
            outputs.Add("measurement_value");
        }
    }

    private bool RouteCanProduce(string semantic, IReadOnlyCollection<string> operators)
    {
        return semantic switch
        {
            "defect_area" => HasOutput(operators, "SurfaceDefectDetection", "DefectArea") ||
                             HasOutput(operators, "ContourMeasurement", "Area"),
            "decoded_text" => HasOutput(operators, "CodeRecognition", "Text"),
            "template_pose_matches" => HasOutput(operators, "TemplateMatching", "Position") &&
                                       HasOutput(operators, "TemplateMatching", "Matches"),
            "measurement_value" => operators.Any(type => type is
                "Measurement" or "CircleMeasurement" or "LineMeasurement" or
                "ContourMeasurement" or "AngleMeasurement" or "WidthMeasurement" or
                "GapMeasurement" or "ColorMeasurement"),
            _ => false
        };
    }

    private bool HasOutput(IEnumerable<string> operators, string operatorType, string portName)
    {
        return operators.Contains(operatorType, StringComparer.OrdinalIgnoreCase) &&
               _catalog.TryGet(operatorType, out var contract) &&
               contract.OutputPorts.Any(port => port.Name.Equals(portName, StringComparison.OrdinalIgnoreCase));
    }

    private static string PreferredProducer(string output, string taskType, string evidenceText)
    {
        return output switch
        {
            "defect_area" => "SurfaceDefectDetection",
            "decoded_text" => "CodeRecognition",
            "template_pose_matches" => "TemplateMatching",
            "measurement_value" when evidenceText.Contains("半径", StringComparison.OrdinalIgnoreCase) => "CircleMeasurement",
            "measurement_value" when evidenceText.Contains("角度", StringComparison.OrdinalIgnoreCase) => "AngleMeasurement",
            "measurement_value" when evidenceText.Contains("面积", StringComparison.OrdinalIgnoreCase) => "ContourMeasurement",
            "measurement_value" => taskType.Equals(AiVisionTaskTypes.GeometryMeasurement, StringComparison.OrdinalIgnoreCase)
                ? "Measurement"
                : string.Empty,
            _ => string.Empty
        };
    }

    private static void InsertBeforeBusinessTerminals(List<string> operators, string operatorType)
    {
        if (operators.Contains(operatorType, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        var index = operators.FindIndex(type => type is "ResultJudgment" or "ResultOutput");
        operators.Insert(index < 0 ? operators.Count : index, operatorType);
    }

    private static bool ExplicitAlternativeAccepted(VisionAgentPlanModeResult plan)
    {
        return (plan.ConfirmedPlanAnswers ?? []).Any(answer =>
            VisionAgentPlanFieldPolicy.NormalizeField(answer.Field)
                .Equals(VisionAgentPlanAnswerFields.AlgorithmStrategy, StringComparison.OrdinalIgnoreCase) &&
            (answer.Origin is VisionAgentPlanAnswerOrigins.ExplicitUserSelection or
                VisionAgentPlanAnswerOrigins.AcceptedRecommendedDefault ||
             answer.Origin == VisionAgentPlanAnswerOrigins.ExplicitUserText &&
             !string.IsNullOrWhiteSpace(answer.EvidenceText)) &&
            (answer.Value.Contains("threshold", StringComparison.OrdinalIgnoreCase) ||
             answer.Value.Contains("替代", StringComparison.OrdinalIgnoreCase)));
    }

    private static List<string> NormalizePipelineOrder(IReadOnlyList<string> operators)
    {
        return operators
            .Select((type, index) => new { type, index })
            .OrderBy(item => PipelineRank(item.type))
            .ThenBy(item => item.index)
            .Select(item => item.type)
            .ToList();
    }

    private static int PipelineRank(string operatorType) => operatorType switch
    {
        "ImageAcquisition" => 0,
        "RoiManager" => 10,
        "ColorConversion" or "Filtering" or "ImageEnhancement" or "Thresholding" or "EdgeDetection" => 20,
        "SurfaceDefectDetection" or "TemplateMatching" or "CodeRecognition" or "DeepLearning" or
            "CircleMeasurement" or "LineMeasurement" or "ContourMeasurement" or "AngleMeasurement" or
            "WidthMeasurement" or "GapMeasurement" or "ColorMeasurement" => 40,
        "BlobAnalysis" or "DetectionSequenceJudge" or "Measurement" => 50,
        "UnitConvert" => 60,
        "ResultJudgment" => 90,
        "ResultOutput" => 100,
        _ => 30
    };

    private static bool ContradictsCatalogFact(
        string risk,
        IEnumerable<VisionAgentCapabilityConstraint> constraints)
    {
        if (!constraints.Any(item => item.Id.Equals("edge_detection_canny", StringComparison.OrdinalIgnoreCase)) ||
            !CannyPattern.IsMatch(risk))
        {
            return false;
        }

        return risk.Contains("不存在", StringComparison.OrdinalIgnoreCase) ||
               risk.Contains("没有", StringComparison.OrdinalIgnoreCase) ||
               risk.Contains("not exist", StringComparison.OrdinalIgnoreCase) ||
               risk.Contains("unavailable", StringComparison.OrdinalIgnoreCase) ||
               risk.Contains("缺少", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildEvidenceText(
        VisionAgentPlanModeRequest request,
        VisionAgentPlanModeResult plan)
    {
        return string.Join("\n", new[]
        {
            request.OriginalUserPrompt,
            request.Description,
            request.AdditionalContext,
            plan.OriginalUserPrompt,
            plan.Goal,
            plan.SemanticExtraction?.SuggestedRoute,
            string.Join(" ", (plan.ConfirmedPlanAnswers ?? []).Select(answer => $"{answer.Value} {answer.EvidenceText}"))
        }.Where(value => !string.IsNullOrWhiteSpace(value)));
    }

    private static string SafeKey(string value)
    {
        return new string(value
            .Select(character => char.IsLetterOrDigit(character) ? char.ToLowerInvariant(character) : '_')
            .ToArray())
            .Trim('_');
    }
}
