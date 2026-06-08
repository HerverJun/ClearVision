using ClearVision.Product.Core.DTOs;
using ClearVision.Product.Infrastructure.AI.AgentRun;
using ClearVision.Product.Infrastructure.AI.Tools;

namespace ClearVision.Product.Infrastructure.AI.Agent;

public sealed class ParameterMappingService
{
    internal BuildStepResult<ParameterMappingResolution> Map(
        BuildPlanLoad load,
        OperatorPipelineResolution pipeline)
    {
        var mappings = new List<VisionAgentParameterMapping>();
        var pending = new List<AiPendingParameterInfo>();
        var missing = new List<AiMissingResourceInfo>();

        foreach (var op in pipeline.Steps)
        {
            if (!VisionAgentReadOnlyCatalog.Schemas.TryGetValue(op.OperatorType, out var schema))
            {
                continue;
            }

            foreach (var parameter in schema.Parameters)
            {
                var mapped = MapParameterValue(op, parameter, load);
                mappings.Add(mapped);
                if (mapped.Pending)
                {
                    pending.Add(new AiPendingParameterInfo
                    {
                        OperatorId = op.TempId,
                        ActualOperatorId = op.TempId,
                        ParameterNames = [parameter.Name]
                    });
                }

                var missingKind = MissingResourceKind(op.OperatorType, parameter.Name, mapped.Pending);
                if (!string.IsNullOrWhiteSpace(missingKind))
                {
                    missing.Add(new AiMissingResourceInfo
                    {
                        ResourceType = missingKind,
                        ResourceKey = $"{op.TempId}.{parameter.Name}",
                        Description = $"{op.OperatorType}.{parameter.Name} remains pending metadata and was not guessed."
                    });
                }
            }
        }

        var resolution = new ParameterMappingResolution(
            mappings,
            VisionAgentBuildSupport.DeduplicatePending(pending),
            VisionAgentBuildSupport.DeduplicateMissing(missing));
        return VisionAgentBuildSupport.StepResult(
            resolution,
            $"Mapped {mappings.Count} parameter assumptions; {resolution.PendingParameters.Count} pending parameter group(s), {resolution.MissingResources.Count} missing resource(s).",
            AgentRunEventStatuses.Completed,
            new
            {
                mappingCount = mappings.Count,
                pendingParameterCount = resolution.PendingParameters.Count,
                missingResourceCount = resolution.MissingResources.Count,
                selections = load.UserSelections.Keys.ToList(),
                acceptedDefaults = load.AcceptedDefaults,
                metadataOnly = true
            },
            warningCode: resolution.MissingResources.Count > 0 ? "resources_pending" : string.Empty,
            applyImpact: "editable_draft_allowed",
            deploymentImpact: resolution.MissingResources.Count > 0 ? "deployment_blocked_until_resources_bound" : "no_deployment_blocker");
    }

    private static VisionAgentParameterMapping MapParameterValue(
        VisionAgentOperatorPipelineStep op,
        OperatorParameterItem parameter,
        BuildPlanLoad load)
    {
        var key = $"{op.OperatorType}.{parameter.Name}";
        if (load.UserSelections.TryGetValue(parameter.Name, out var direct) ||
            load.UserSelections.TryGetValue(key, out direct))
        {
            return new VisionAgentParameterMapping
            {
                TempId = op.TempId,
                OperatorType = op.OperatorType,
                ParameterName = parameter.Name,
                ValueSummary = VisionAgentBuildSupport.CleanValue(direct),
                Source = "user_selection",
                Pending = false,
                Impact = "User selection mapped into draft parameter metadata."
            };
        }

        var fallback = DefaultParameterValue(op.OperatorType, parameter.Name);
        var pending = parameter.Required || fallback.Contains("pending", StringComparison.OrdinalIgnoreCase);
        return new VisionAgentParameterMapping
        {
            TempId = op.TempId,
            OperatorType = op.OperatorType,
            ParameterName = parameter.Name,
            ValueSummary = fallback,
            Source = pending ? "pending_metadata" : "accepted_default",
            Pending = pending,
            Impact = pending
                ? "Canvas Apply can continue, but deployment readiness remains blocked until this metadata is bound."
                : "Default metadata keeps the draft editable."
        };
    }

    private static string DefaultParameterValue(string operatorType, string parameterName)
    {
        if (parameterName.Contains("camera", StringComparison.OrdinalIgnoreCase))
        {
            return "<pending-camera-binding>";
        }

        if (parameterName.Contains("model", StringComparison.OrdinalIgnoreCase))
        {
            return "<pending-model-resource>";
        }

        if (parameterName.Contains("template", StringComparison.OrdinalIgnoreCase))
        {
            return "<pending-template-artifact>";
        }

        if (parameterName.Contains("tolerance", StringComparison.OrdinalIgnoreCase))
        {
            return "<pending-tolerance>";
        }

        if (parameterName.Contains("channel", StringComparison.OrdinalIgnoreCase))
        {
            return "<pending-output-channel>";
        }

        return operatorType switch
        {
            "ResultJudgment" when parameterName.Equals("Rule", StringComparison.OrdinalIgnoreCase) => "OK when inspection score satisfies configured threshold.",
            "Thresholding" when parameterName.Equals("Mode", StringComparison.OrdinalIgnoreCase) => "adaptive_review",
            "TemplateMatching" when parameterName.Equals("MinScore", StringComparison.OrdinalIgnoreCase) => "0.8",
            "TemplateMatching" when parameterName.Equals("MaxMatches", StringComparison.OrdinalIgnoreCase) => "1",
            "DeepLearning" when parameterName.Equals("ConfidenceThreshold", StringComparison.OrdinalIgnoreCase) => "0.6",
            "SurfaceDefectDetection" when parameterName.Equals("ModelKind", StringComparison.OrdinalIgnoreCase) => "surface_defect",
            "SemanticSegmentation" when parameterName.Equals("ModelKind", StringComparison.OrdinalIgnoreCase) => "segmentation",
            "BlobAnalysis" when parameterName.Equals("MinArea", StringComparison.OrdinalIgnoreCase) => "20",
            "BlobAnalysis" when parameterName.Equals("MaxArea", StringComparison.OrdinalIgnoreCase) => "<pending-max-area>",
            "RoiManager" when parameterName.Equals("RoiName", StringComparison.OrdinalIgnoreCase) => "inspection_roi",
            _ => "<pending-parameter>"
        };
    }

    private static string MissingResourceKind(string operatorType, string parameterName, bool pending)
    {
        if (!pending)
        {
            return string.Empty;
        }

        if (parameterName.Contains("camera", StringComparison.OrdinalIgnoreCase))
        {
            return "camera_binding";
        }

        if (parameterName.Contains("model", StringComparison.OrdinalIgnoreCase))
        {
            return "model_resource";
        }

        if (parameterName.Contains("template", StringComparison.OrdinalIgnoreCase))
        {
            return "template_artifact";
        }

        if (parameterName.Contains("channel", StringComparison.OrdinalIgnoreCase))
        {
            return "output_channel";
        }

        if (operatorType.Contains("Measure", StringComparison.OrdinalIgnoreCase) ||
            parameterName.Contains("tolerance", StringComparison.OrdinalIgnoreCase))
        {
            return "measurement_parameter";
        }

        return string.Empty;
    }
}
