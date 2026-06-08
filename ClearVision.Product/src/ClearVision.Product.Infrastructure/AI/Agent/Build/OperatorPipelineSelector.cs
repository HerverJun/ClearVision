using ClearVision.Product.Core.DTOs;
using ClearVision.Product.Infrastructure.AI.AgentRun;
using ClearVision.Product.Infrastructure.AI.Tools;

namespace ClearVision.Product.Infrastructure.AI.Agent;

internal sealed class OperatorPipelineSelector
{
    private static readonly HashSet<string> ForbiddenOperatorTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "ModbusCommunication",
        "HttpRequest",
        "ScriptOperator"
    };

    public BuildStepResult<OperatorPipelineResolution> Select(
        BuildPlanLoad load,
        TemplateStrategyResolution template,
        List<string> publicWarnings)
    {
        var source = "plan";
        var requested = VisionAgentBuildSupport.ReadOperatorTypes(template.TemplateSkeleton).ToList();
        if (requested.Count > 0)
        {
            source = "template";
        }
        else if (load.Plan?.RecommendedRoute.Operators.Count > 0)
        {
            requested = load.Plan.RecommendedRoute.Operators;
        }

        var allowed = VisionAgentReadOnlyCatalog.Schemas.Keys
            .Where(type => !ForbiddenOperatorTypes.Contains(type))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var repaired = new List<VisionAgentOperatorPipelineStep>();
        var invalid = new List<string>();
        foreach (var type in requested.Select(VisionAgentBuildSupport.Clean).Where(type => !string.IsNullOrWhiteSpace(type)))
        {
            if (allowed.Contains(type))
            {
                repaired.Add(new VisionAgentOperatorPipelineStep
                {
                    TempId = VisionAgentBuildSupport.TempIdFor(type, repaired.Count + 1),
                    OperatorType = type,
                    Source = source,
                    Status = "selected"
                });
            }
            else
            {
                invalid.Add(type);
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
                ? $"Selected {repaired.Count} catalog-backed operators."
                : $"Selected {repaired.Count} catalog-backed operators and removed {invalid.Count} invalid operators.",
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
}
