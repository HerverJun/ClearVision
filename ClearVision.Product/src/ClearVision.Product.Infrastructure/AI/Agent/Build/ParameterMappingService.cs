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
                        Description = $"{op.OperatorType}.{parameter.Name} 仍为待绑定元数据，系统未进行猜测。"
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
            $"已映射 {mappings.Count} 个参数假设；仍有 {resolution.PendingParameters.Count} 组待确认参数、{resolution.MissingResources.Count} 个缺失资源。",
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
            Impact = "用户选择已写入草稿参数元数据。"
        };
        }

        var fallback = DefaultParameterValue(op.OperatorType, parameter.Name, load);
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
                ? "画布可继续应用草稿，但部署就绪会保持阻断，直到该元数据完成绑定。"
                : "默认元数据会让草稿保持可编辑。"
        };
    }

    private static string DefaultParameterValue(
        string operatorType,
        string parameterName,
        BuildPlanLoad load)
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

        if (parameterName.Equals("Unit", StringComparison.OrdinalIgnoreCase) &&
            IsMeasurementScenario(load))
        {
            return "<pending-calibration-unit-or-pixel-scale>";
        }

        if (parameterName.Contains("tolerance", StringComparison.OrdinalIgnoreCase))
        {
            return IsMeasurementScenario(load)
                ? "<pending-measurement-threshold>"
                : "<pending-tolerance>";
        }

        if (parameterName.Contains("channel", StringComparison.OrdinalIgnoreCase))
        {
            return "<pending-output-channel>";
        }

        return operatorType switch
        {
            "ResultJudgment" when parameterName.Equals("Rule", StringComparison.OrdinalIgnoreCase) && IsWireSequenceScenario(load) => "按待确认端子线序规则校验检测到的类别顺序。",
            "ResultJudgment" when parameterName.Equals("Rule", StringComparison.OrdinalIgnoreCase) && IsMeasurementScenario(load) => "当测量距离处于待确认容差阈值内时判定为 OK。",
            "ResultJudgment" when parameterName.Equals("Rule", StringComparison.OrdinalIgnoreCase) => "当检测分数满足配置阈值时判定为 OK。",
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

    private static bool IsWireSequenceScenario(BuildPlanLoad load)
    {
        var text = $"{load.Plan?.Intent} {load.Plan?.Goal} {load.OriginalUserPrompt}";
        return text.Contains("wire", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("sequence", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("terminal", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("line order", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsMeasurementScenario(BuildPlanLoad load)
    {
        var text = $"{load.Plan?.Intent} {load.Plan?.Goal} {load.OriginalUserPrompt}";
        return text.Contains("measurement", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("distance", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("spacing", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("hole", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("circle", StringComparison.OrdinalIgnoreCase);
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
