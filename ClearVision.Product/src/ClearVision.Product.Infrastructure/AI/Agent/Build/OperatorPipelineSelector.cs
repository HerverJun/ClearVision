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
        List<string> publicWarnings)
    {
        var source = "plan";
        var requested = ReadOperatorTypes(template.TemplateSkeleton).ToList();
        if (requested.Count > 0)
        {
            source = "template";
        }
        else if (load.Plan?.RecommendedRoute.Operators.Count > 0)
        {
            requested = load.Plan.RecommendedRoute.Operators;
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
                    TempId = TempIdFor(type, repaired.Count + 1),
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
                new() { TempId = "op_cam", OperatorType = "ImageAcquisition", Source = "repair", Status = "selected", RepairNote = "minimum_pipeline_added" },
                new() { TempId = "op_judge", OperatorType = "ResultJudgment", Source = "repair", Status = "selected", RepairNote = "minimum_pipeline_added" },
                new() { TempId = "op_out", OperatorType = "ResultOutput", Source = "repair", Status = "selected", RepairNote = "minimum_pipeline_added" }
            ];
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
                metadataOnly = true
            },
            warningCode: invalid.Count > 0 ? "invalid_operator_removed" : string.Empty,
            repairAction: invalid.Count > 0 ? "removed_invalid_operators" : string.Empty,
            applyImpact: "editable_draft_allowed",
            deploymentImpact: invalid.Count > 0 ? "operator_contract_repaired" : "no_deployment_blocker");
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

    private static string TempIdFor(string operatorType, int ordinal)
    {
        return operatorType switch
        {
            "ImageAcquisition" => "op_cam",
            "RoiManager" => "op_roi",
            "SurfaceDefectDetection" => "op_surface_defect",
            "DeepLearning" => "op_detect",
            "SemanticSegmentation" => "op_segment",
            "TemplateMatching" => "op_match",
            "BlobAnalysis" => "op_blob",
            "Thresholding" => "op_threshold",
            "CircleMeasurement" => ordinal <= 2 ? "op_circle_a" : "op_circle_b",
            "Measurement" => "op_distance",
            "UnitConvert" => "op_calibration",
            "DetectionSequenceJudge" => "op_sequence",
            "ResultJudgment" => "op_judge",
            "ResultOutput" => "op_out",
            _ => $"op_{new string(operatorType.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray())}_{ordinal}"
        };
    }
}
