using ClearVision.Product.Core.DTOs;
using ClearVision.Product.Core.Services;
using ClearVision.Product.Infrastructure.AI.AgentRun;
using ClearVision.Product.Infrastructure.AI.Tools;

namespace ClearVision.Product.Infrastructure.AI.Agent;

public sealed class OperatorPipelineSelector
{
    private readonly IVisionAgentOperatorContractCatalog _contractCatalog;

    public OperatorPipelineSelector()
        : this(new VisionAgentOperatorContractCatalog())
    {
    }

    public OperatorPipelineSelector(IOperatorFactory operatorFactory)
        : this(new VisionAgentOperatorContractCatalog(operatorFactory))
    {
    }

    internal OperatorPipelineSelector(IVisionAgentOperatorContractCatalog contractCatalog)
    {
        _contractCatalog = contractCatalog;
    }

    private static readonly HashSet<string> ForbiddenOperatorTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "ModbusCommunication",
        "HttpRequest",
        "ScriptOperator"
    };

    internal BuildStepResult<OperatorPipelineResolution> Select(
        BuildPlanLoad load,
        TemplateStrategyResolution template,
        PlanSelectionResolution selection,
        List<string> publicWarnings)
    {
        var source = string.IsNullOrWhiteSpace(selection.SelectionSource)
            ? "plan"
            : selection.SelectionSource;
        var requested = selection.EffectiveRoute.Operators.ToList();
        if (requested.Count == 0)
        {
            requested = ReadOperatorTypes(template.TemplateSkeleton).ToList();
            if (requested.Count > 0)
            {
                source = "template";
            }
            else if (load.Plan?.RecommendedRoute.Operators.Count > 0)
            {
                requested = load.Plan.RecommendedRoute.Operators;
                source = "plan";
            }
        }

        var allowed = _contractCatalog.OperatorTypes
            .Where(type => !ForbiddenOperatorTypes.Contains(type))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var repaired = new List<VisionAgentOperatorPipelineStep>();
        var invalid = new List<string>();
        foreach (var requestedType in requested.Select(VisionAgentBuildSupport.Clean).Where(type => !string.IsNullOrWhiteSpace(type)))
        {
            var type = _contractCatalog.CanonicalizeOperatorType(requestedType);
            if (allowed.Contains(type))
            {
                repaired.Add(new VisionAgentOperatorPipelineStep
                {
                    TempId = string.Empty,
                    OperatorType = type,
                    Source = source,
                    Status = "selected"
                });
            }
            else
            {
                invalid.Add(requestedType);
            }
        }

        if (repaired.Count == 0)
        {
            publicWarnings.Add("operator_pipeline_repaired_to_minimum");
            repaired =
            [
                new() { TempId = string.Empty, OperatorType = "ImageAcquisition", Source = "repair", Status = "selected", RepairNote = "minimum_pipeline_added" },
                new() { TempId = string.Empty, OperatorType = "ResultJudgment", Source = "repair", Status = "selected", RepairNote = "minimum_pipeline_added" },
                new() { TempId = string.Empty, OperatorType = "ResultOutput", Source = "repair", Status = "selected", RepairNote = "minimum_pipeline_added" }
            ];
        }

        var draftResultChainAdded = false;
        var hasBusinessProcessor = repaired.Any(step =>
            !step.OperatorType.Equals("ImageAcquisition", StringComparison.OrdinalIgnoreCase) &&
            !step.OperatorType.Equals("ResultJudgment", StringComparison.OrdinalIgnoreCase) &&
            !step.OperatorType.Equals("ResultOutput", StringComparison.OrdinalIgnoreCase));
        if (load.RequirementMode.Equals(AiRequirementModes.Draft, StringComparison.OrdinalIgnoreCase) &&
            hasBusinessProcessor &&
            repaired.All(step => !step.OperatorType.Equals("ResultJudgment", StringComparison.OrdinalIgnoreCase)))
        {
            var outputIndex = repaired.FindIndex(step =>
                step.OperatorType.Equals("ResultOutput", StringComparison.OrdinalIgnoreCase));
            repaired.Insert(outputIndex < 0 ? repaired.Count : outputIndex, new VisionAgentOperatorPipelineStep
            {
                TempId = string.Empty,
                OperatorType = "ResultJudgment",
                Source = "repair",
                Status = "selected",
                RepairNote = "draft_result_chain_added"
            });
            publicWarnings.Add("draft_result_judgment_added");
            draftResultChainAdded = true;
        }

        if (load.RequirementMode.Equals(AiRequirementModes.Draft, StringComparison.OrdinalIgnoreCase) &&
            hasBusinessProcessor &&
            repaired.All(step => !step.OperatorType.Equals("ResultOutput", StringComparison.OrdinalIgnoreCase)))
        {
            publicWarnings.Add("draft_terminal_output_added");
            repaired.Add(new VisionAgentOperatorPipelineStep
            {
                TempId = string.Empty,
                OperatorType = "ResultOutput",
                Source = "repair",
                Status = "selected",
                RepairNote = "draft_result_chain_added"
            });
            draftResultChainAdded = true;
        }

        if (invalid.Count > 0)
        {
            publicWarnings.Add("invalid_operator_removed");
            repaired = repaired.Select(step => step with
            {
                RepairNote = string.IsNullOrWhiteSpace(step.RepairNote)
                    ? "invalid_operator_removed"
                    : step.RepairNote
            }).ToList();
        }

        repaired = AllocateTempIds(repaired);

        var resolution = new OperatorPipelineResolution(repaired, invalid);
        return VisionAgentBuildSupport.StepResult(
            resolution,
            invalid.Count == 0
                ? $"已选择 {repaired.Count} 个目录支持的算子。"
                : $"已选择 {repaired.Count} 个目录支持的算子，并移除 {invalid.Count} 个非法算子。",
            AgentRunEventStatuses.Completed,
            new
            {
                operatorTypes = repaired.Select(item => item.OperatorType).ToList(),
                invalidOperators = invalid,
                source,
                effectiveRouteId = selection.EffectiveRoute.RouteId,
                selectionSource = selection.SelectionSource,
                selectionStrategy = selection.Strategy,
                metadataOnly = true
            },
            warningCode: invalid.Count > 0
                ? "invalid_operator_removed"
                : draftResultChainAdded ? "draft_result_chain_added" : string.Empty,
            repairAction: invalid.Count > 0
                ? "removed_invalid_operators"
                : draftResultChainAdded ? "appended_result_judgment_and_output" : string.Empty,
            applyImpact: "editable_draft_allowed",
            deploymentImpact: invalid.Count > 0 || draftResultChainAdded
                ? "operator_contract_repaired"
                : "no_deployment_blocker");
    }

    private static IEnumerable<string> ReadOperatorTypes(object? templateSkeleton)
    {
        var root = VisionAgentBuildSupport.ToJsonElementOrNull(templateSkeleton);
        if (root == null ||
            !VisionAgentBuildSupport.TryGetProperty(root.Value, "operators", out var operators) ||
            operators.ValueKind != System.Text.Json.JsonValueKind.Array)
        {
            yield break;
        }

        foreach (var op in operators.EnumerateArray())
        {
            var type = VisionAgentBuildSupport.ReadString(op, "operatorType") ??
                       VisionAgentBuildSupport.ReadString(op, "type");
            if (!string.IsNullOrWhiteSpace(type))
            {
                yield return type;
            }
        }
    }

    private static List<VisionAgentOperatorPipelineStep> AllocateTempIds(
        IReadOnlyList<VisionAgentOperatorPipelineStep> steps)
    {
        var ordinals = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var allocated = new List<VisionAgentOperatorPipelineStep>(steps.Count);
        foreach (var step in steps)
        {
            ordinals.TryGetValue(step.OperatorType, out var ordinal);
            ordinal++;
            ordinals[step.OperatorType] = ordinal;

            var preferred = PreferredTempId(step.OperatorType, ordinal);
            var candidate = preferred;
            var collisionOrdinal = 2;
            while (!used.Add(candidate))
            {
                candidate = $"{preferred}_{collisionOrdinal++}";
            }

            allocated.Add(step with { TempId = candidate });
        }

        if (allocated.Any(step => string.IsNullOrWhiteSpace(step.TempId)) ||
            allocated.Select(step => step.TempId).Distinct(StringComparer.OrdinalIgnoreCase).Count() != allocated.Count)
        {
            throw new InvalidOperationException("operator_pipeline_duplicate_temp_id");
        }

        return allocated;
    }

    private static string PreferredTempId(string operatorType, int typeOrdinal)
    {
        var firstId = operatorType switch
        {
            "ImageAcquisition" => "op_cam",
            "RoiManager" => "op_roi",
            "SurfaceDefectDetection" => "op_surface_defect",
            "DeepLearning" => "op_detect",
            "SemanticSegmentation" => "op_segment",
            "TemplateMatching" => "op_match",
            "BlobAnalysis" => "op_blob",
            "Thresholding" => "op_threshold",
            "CircleMeasurement" => "op_circle_a",
            "Measurement" => "op_distance",
            "UnitConvert" => "op_calibration",
            "Aggregator" => "op_aggregate",
            "DetectionSequenceJudge" => "op_sequence",
            "ResultJudgment" => "op_judge",
            "ResultOutput" => "op_out",
            _ => $"op_{new string(operatorType.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray())}"
        };

        if (operatorType.Equals("CircleMeasurement", StringComparison.OrdinalIgnoreCase))
        {
            return typeOrdinal switch
            {
                1 => "op_circle_a",
                2 => "op_circle_b",
                _ => $"op_circle_{typeOrdinal}"
            };
        }

        return typeOrdinal == 1 ? firstId : $"{firstId}_{typeOrdinal}";
    }
}
