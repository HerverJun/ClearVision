using ClearVision.Product.Core.DTOs;
using ClearVision.Product.Core.Services;
using ClearVision.Product.Infrastructure.AI.AgentRun;
using ClearVision.Product.Infrastructure.AI.Tools;

namespace ClearVision.Product.Infrastructure.AI.Agent;

public sealed class ParameterMappingService
{
    private readonly IVisionAgentOperatorContractCatalog _contractCatalog;

    public ParameterMappingService()
        : this(new VisionAgentOperatorContractCatalog())
    {
    }

    public ParameterMappingService(IOperatorFactory operatorFactory)
        : this(new VisionAgentOperatorContractCatalog(operatorFactory))
    {
    }

    internal ParameterMappingService(IVisionAgentOperatorContractCatalog contractCatalog)
    {
        _contractCatalog = contractCatalog;
    }

    internal BuildStepResult<ParameterMappingResolution> Map(
        BuildPlanLoad load,
        OperatorPipelineResolution pipeline,
        PlanSelectionResolution selection)
    {
        var parameterStrategy = ResolveParameterStrategy(load, pipeline, selection);
        var mappings = new List<VisionAgentParameterMapping>();
        var pending = new List<AiPendingParameterInfo>();
        var missing = new List<AiMissingResourceInfo>();

        foreach (var op in pipeline.Steps)
        {
            if (!_contractCatalog.TryGet(op.OperatorType, out var schema))
            {
                continue;
            }

            foreach (var parameter in schema.Parameters)
            {
                var mapped = MapParameterValue(op, parameter, load, parameterStrategy);
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

                var missingKind = MissingResourceKind(op.OperatorType, parameter.Name, mapped.Pending, parameterStrategy);
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
            VisionAgentBuildSupport.DeduplicateMissing(missing),
            parameterStrategy);
        return VisionAgentBuildSupport.StepResult(
            resolution,
            $"已映射 {mappings.Count} 个参数假设；仍有 {resolution.PendingParameters.Count} 组待确认参数、{resolution.MissingResources.Count} 个缺失资源。",
            AgentRunEventStatuses.Completed,
            new
            {
                mappingCount = mappings.Count,
                pendingParameterCount = resolution.PendingParameters.Count,
                missingResourceCount = resolution.MissingResources.Count,
                parameterStrategy = resolution.ParameterStrategy,
                selections = load.ParameterSelections.Keys.ToList(),
                acceptedDefaults = load.AcceptedDefaults,
                metadataOnly = true
            },
            warningCode: resolution.MissingResources.Count > 0 ? "resources_pending" : string.Empty,
            applyImpact: "editable_draft_allowed",
            deploymentImpact: resolution.MissingResources.Count > 0 ? "deployment_blocked_until_resources_bound" : "no_deployment_blocker");
    }

    private static string ResolveParameterStrategy(
        BuildPlanLoad load,
        OperatorPipelineResolution pipeline,
        PlanSelectionResolution selection)
    {
        if (!string.IsNullOrWhiteSpace(selection.ParameterStrategy))
        {
            return selection.ParameterStrategy;
        }

        if (!IsAttributeClassificationScenario(load))
        {
            return string.Empty;
        }

        var operators = pipeline.Steps
            .Select(step => step.OperatorType)
            .ToList();
        if (operators.Any(op => op.Equals("Thresholding", StringComparison.OrdinalIgnoreCase)) &&
            operators.Any(op => op.Equals("BlobAnalysis", StringComparison.OrdinalIgnoreCase)))
        {
            return "traditional_numeric_rule";
        }

        if (operators.Any(op => op.Equals("DeepLearning", StringComparison.OrdinalIgnoreCase)))
        {
            return "deep_learning_classification";
        }

        return string.Empty;
    }

    private static VisionAgentParameterMapping MapParameterValue(
        VisionAgentOperatorPipelineStep op,
        VisionAgentParameterContract parameter,
        BuildPlanLoad load,
        string parameterStrategy)
    {
        var key = $"{op.OperatorType}.{parameter.Name}";
        if (!IsTraditionalNumericRuleProtectedParameter(op.OperatorType, parameter.Name, parameterStrategy) &&
            (load.ParameterSelections.TryGetValue(parameter.Name, out var direct) ||
             load.ParameterSelections.TryGetValue(key, out direct)))
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

        var fallback = DefaultParameterValue(op.OperatorType, parameter, load, parameterStrategy);
        var pending = IsPendingParameter(op.OperatorType, parameter, fallback, load);
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

    private static bool IsTraditionalNumericRuleProtectedParameter(
        string operatorType,
        string parameterName,
        string parameterStrategy)
    {
        if (!IsTraditionalNumericRule(parameterStrategy))
        {
            return false;
        }

        if (operatorType.Equals("ResultJudgment", StringComparison.OrdinalIgnoreCase) &&
            (parameterName.Equals("FieldName", StringComparison.OrdinalIgnoreCase) ||
             parameterName.Equals("Condition", StringComparison.OrdinalIgnoreCase) ||
             parameterName.Equals("ExpectValue", StringComparison.OrdinalIgnoreCase) ||
             parameterName.Equals("ExpectValueMin", StringComparison.OrdinalIgnoreCase) ||
             parameterName.Equals("ExpectValueMax", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return operatorType.Equals("Thresholding", StringComparison.OrdinalIgnoreCase) ||
               operatorType.Equals("BlobAnalysis", StringComparison.OrdinalIgnoreCase);
    }

    private static string DefaultParameterValue(
        string operatorType,
        VisionAgentParameterContract parameter,
        BuildPlanLoad load,
        string parameterStrategy)
    {
        var parameterName = parameter.Name;
        if (operatorType.Equals("ImageAcquisition", StringComparison.OrdinalIgnoreCase) &&
            parameterName.Equals("SourceType", StringComparison.OrdinalIgnoreCase))
        {
            return "Camera";
        }

        if (parameterName.Contains("camera", StringComparison.OrdinalIgnoreCase))
        {
            return "<pending-camera-binding>";
        }

        if (IsPreferredModelParameter(parameterName))
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

        if (operatorType.Equals("UnitConvert", StringComparison.OrdinalIgnoreCase) &&
            parameterName.Equals("Scale", StringComparison.OrdinalIgnoreCase) &&
            IsMeasurementScenario(load))
        {
            return "<pending-pixel-to-world-scale>";
        }

        if (parameterName.Equals("Tolerance", StringComparison.OrdinalIgnoreCase))
        {
            return IsMeasurementScenario(load)
                ? "<pending-measurement-threshold>"
                : "<pending-tolerance>";
        }

        if (parameterName.Contains("channel", StringComparison.OrdinalIgnoreCase))
        {
            return "<pending-output-channel>";
        }

        if (IsTraditionalNumericRule(parameterStrategy))
        {
            var traditional = TraditionalNumericRuleParameterValue(operatorType, parameterName);
            if (!string.IsNullOrWhiteSpace(traditional))
            {
                return traditional;
            }
        }

        return operatorType switch
        {
            "ResultJudgment" when parameterName.Equals("FieldName", StringComparison.OrdinalIgnoreCase) && IsWireSequenceScenario(load) => "Value",
            "ResultJudgment" when parameterName.Equals("Condition", StringComparison.OrdinalIgnoreCase) && IsWireSequenceScenario(load) => "Equal",
            "ResultJudgment" when parameterName.Equals("ExpectValue", StringComparison.OrdinalIgnoreCase) && IsWireSequenceScenario(load) => "线序待确认",
            "ResultJudgment" when parameterName.Equals("FieldName", StringComparison.OrdinalIgnoreCase) && IsMeasurementScenario(load) => "Value",
            "ResultJudgment" when parameterName.Equals("Condition", StringComparison.OrdinalIgnoreCase) && IsMeasurementScenario(load) => "Range",
            "ResultJudgment" when parameterName.Equals("ExpectValueMin", StringComparison.OrdinalIgnoreCase) && IsMeasurementScenario(load) => "<pending-measurement-threshold>",
            "ResultJudgment" when parameterName.Equals("ExpectValueMax", StringComparison.OrdinalIgnoreCase) && IsMeasurementScenario(load) => "<pending-measurement-threshold>",
            "ResultJudgment" when parameterName.Equals("FieldName", StringComparison.OrdinalIgnoreCase) && IsAttributeClassificationScenario(load) => "TopClassLabel",
            "ResultJudgment" when parameterName.Equals("Condition", StringComparison.OrdinalIgnoreCase) && IsAttributeClassificationScenario(load) => "Equal",
            "ResultJudgment" when parameterName.Equals("ExpectValue", StringComparison.OrdinalIgnoreCase) && IsAttributeClassificationScenario(load) => ExpectedClassificationOkValue(load),
            "ResultJudgment" when parameterName.Equals("MinConfidence", StringComparison.OrdinalIgnoreCase) && IsAttributeClassificationScenario(load) => "0.6",
            "ResultJudgment" when parameterName.Equals("Condition", StringComparison.OrdinalIgnoreCase) => "GreaterOrEqual",
            "ResultJudgment" when parameterName.Equals("ExpectValue", StringComparison.OrdinalIgnoreCase) => "1",
            "DetectionSequenceJudge" when parameterName.Equals("ExpectedLabels", StringComparison.OrdinalIgnoreCase) && IsWireSequenceScenario(load) => "<pending-wire-sequence-labels>",
            "DetectionSequenceJudge" when parameterName.Equals("Direction", StringComparison.OrdinalIgnoreCase) && IsWireSequenceScenario(load) => "LeftToRight",
            "Thresholding" when parameterName.Equals("Mode", StringComparison.OrdinalIgnoreCase) => "adaptive_review",
            "TemplateMatching" when parameterName.Equals("Threshold", StringComparison.OrdinalIgnoreCase) => "0.8",
            "TemplateMatching" when parameterName.Equals("MaxMatches", StringComparison.OrdinalIgnoreCase) => "1",
            "DeepLearning" when parameterName.Equals("Confidence", StringComparison.OrdinalIgnoreCase) => "0.6",
            "DeepLearning" when parameterName.Equals("TargetClasses", StringComparison.OrdinalIgnoreCase) && IsAttributeClassificationScenario(load) => ExpectedClassificationOkValue(load),
            "DeepLearning" when parameterName.Equals("DetectionMode", StringComparison.OrdinalIgnoreCase) && IsAttributeClassificationScenario(load) => "Object",
            "BlobAnalysis" when parameterName.Equals("MinArea", StringComparison.OrdinalIgnoreCase) => "20",
            "BlobAnalysis" when parameterName.Equals("MaxArea", StringComparison.OrdinalIgnoreCase) => "<pending-max-area>",
            "RoiManager" when parameterName.Equals("RoiName", StringComparison.OrdinalIgnoreCase) => "inspection_roi",
            _ => parameter.DefaultValue?.ToString() ?? string.Empty
        };
    }

    private static string TraditionalNumericRuleParameterValue(
        string operatorType,
        string parameterName)
    {
        return operatorType switch
        {
            "Thresholding" when parameterName.Equals("Threshold", StringComparison.OrdinalIgnoreCase) => "<pending-threshold-calibration>",
            "Thresholding" when parameterName.Equals("MaxValue", StringComparison.OrdinalIgnoreCase) => "255",
            "Thresholding" when parameterName.Equals("Type", StringComparison.OrdinalIgnoreCase) => "0",
            "Thresholding" when parameterName.Equals("UseOtsu", StringComparison.OrdinalIgnoreCase) => "false",
            "BlobAnalysis" when parameterName.Equals("MinArea", StringComparison.OrdinalIgnoreCase) => "<pending-min-area-calibration>",
            "BlobAnalysis" when parameterName.Equals("MaxArea", StringComparison.OrdinalIgnoreCase) => "<pending-max-area-calibration>",
            "BlobAnalysis" when parameterName.Equals("Color", StringComparison.OrdinalIgnoreCase) => "White",
            "BlobAnalysis" when parameterName.Equals("OutputDetailedFeatures", StringComparison.OrdinalIgnoreCase) => "true",
            "ResultJudgment" when parameterName.Equals("FieldName", StringComparison.OrdinalIgnoreCase) => "BlobCount",
            "ResultJudgment" when parameterName.Equals("Condition", StringComparison.OrdinalIgnoreCase) => "GreaterOrEqual",
            "ResultJudgment" when parameterName.Equals("ExpectValue", StringComparison.OrdinalIgnoreCase) => "<pending-blob-count-threshold>",
            "ResultJudgment" when parameterName.Equals("ExpectValueMin", StringComparison.OrdinalIgnoreCase) => string.Empty,
            "ResultJudgment" when parameterName.Equals("ExpectValueMax", StringComparison.OrdinalIgnoreCase) => string.Empty,
            "ResultJudgment" when parameterName.Equals("MinConfidence", StringComparison.OrdinalIgnoreCase) => "0",
            _ => string.Empty
        };
    }

    private static bool IsPendingParameter(
        string operatorType,
        VisionAgentParameterContract parameter,
        string fallback,
        BuildPlanLoad load)
    {
        if (fallback.Contains("pending", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var resourceKind = VisionAgentResourceClassifier.Classify(operatorType, parameter.Name, parameter.DataType);
        if (string.IsNullOrWhiteSpace(resourceKind))
        {
            return false;
        }

        if (!IsPreferredResourceParameter(operatorType, parameter.Name, resourceKind))
        {
            return false;
        }

        return string.IsNullOrWhiteSpace(fallback) || IsMeasurementScenario(load);
    }

    private static bool IsPreferredResourceParameter(
        string operatorType,
        string parameterName,
        string resourceKind)
    {
        return resourceKind switch
        {
            "camera_binding" => parameterName.Equals("CameraId", StringComparison.OrdinalIgnoreCase) ||
                                parameterName.Equals("CameraBindingId", StringComparison.OrdinalIgnoreCase),
            "model_resource" => IsPreferredModelParameter(parameterName),
            "template_artifact" => parameterName.Equals("TemplateId", StringComparison.OrdinalIgnoreCase),
            "measurement_parameter" => operatorType.Equals("UnitConvert", StringComparison.OrdinalIgnoreCase) &&
                                       parameterName.Equals("Scale", StringComparison.OrdinalIgnoreCase),
            "plc_address" => parameterName.Contains("Address", StringComparison.OrdinalIgnoreCase) ||
                             parameterName.Contains("PLC", StringComparison.OrdinalIgnoreCase),
            "output_channel" => parameterName.Contains("Channel", StringComparison.OrdinalIgnoreCase),
            _ => false
        };
    }

    private static bool IsPreferredModelParameter(string parameterName)
    {
        return parameterName.Equals("ModelPath", StringComparison.OrdinalIgnoreCase) ||
               parameterName.Equals("ModelId", StringComparison.OrdinalIgnoreCase);
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

    private static bool IsAttributeClassificationScenario(BuildPlanLoad load)
    {
        var taskType = load.Plan?.SemanticExtraction?.TaskType ?? string.Empty;
        var text = $"{load.Plan?.Intent} {load.Plan?.Goal} {load.OriginalUserPrompt} {load.Plan?.RecommendedRoute?.RouteId}";
        return taskType.Equals(AiVisionTaskTypes.AttributeClassification, StringComparison.OrdinalIgnoreCase) ||
               taskType.Equals(AiVisionTaskTypes.Classification, StringComparison.OrdinalIgnoreCase) ||
               text.Contains("attribute_classification", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("classification", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsTraditionalNumericRule(string parameterStrategy)
    {
        return parameterStrategy.Equals("traditional_numeric_rule", StringComparison.OrdinalIgnoreCase);
    }

    private static string ExpectedClassificationOkValue(BuildPlanLoad load)
    {
        foreach (var key in new[]
                 {
                     "ResultJudgment.ExpectValue",
                     "ExpectValue",
                     "classification_ok_label",
                     "ok_label",
            "target_attribute",
            "targetAttribute"
         })
        {
            if ((load.ParameterSelections.TryGetValue(key, out var selected) ||
                 load.RequirementAnswers.TryGetValue(key, out selected)) &&
                !string.IsNullOrWhiteSpace(selected))
            {
                return VisionAgentBuildSupport.CleanValue(selected);
            }
        }

        var semantic = load.Plan?.SemanticExtraction;
        var fromOkCondition = ExtractOkClassLabel(semantic?.OkCondition);
        if (!string.IsNullOrWhiteSpace(fromOkCondition))
        {
            return fromOkCondition;
        }

        var target = VisionAgentBuildSupport.CleanValue(semantic?.TargetAttribute);
        return string.IsNullOrWhiteSpace(target) ? "<pending-ok-class-label>" : target;
    }

    private static string ExtractOkClassLabel(string? okCondition)
    {
        var text = VisionAgentBuildSupport.CleanValue(okCondition);
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var candidates = new[]
        {
            " is OK",
            " is ok",
            " as OK",
            " as ok",
            "=> OK",
            "=> ok",
            "= OK",
            "= ok",
            "为 OK",
            "为OK",
            "判为 OK",
            "判为OK",
            "则 OK",
            "则OK"
        };
        foreach (var marker in candidates)
        {
            var index = text.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (index > 0)
            {
                return text[..index].Trim(' ', ',', '.', ';', ':', '，', '。', '；', '：');
            }
        }

        return text.Replace("OK", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("ok", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Trim(' ', ',', '.', ';', ':', '，', '。', '；', '：');
    }

    private static string MissingResourceKind(
        string operatorType,
        string parameterName,
        bool pending,
        string parameterStrategy)
    {
        if (!pending)
        {
            return string.Empty;
        }

        if (IsTraditionalNumericRule(parameterStrategy))
        {
            if (operatorType.Equals("Thresholding", StringComparison.OrdinalIgnoreCase) &&
                parameterName.Equals("Threshold", StringComparison.OrdinalIgnoreCase))
            {
                return "threshold_parameter";
            }

            if (operatorType.Equals("BlobAnalysis", StringComparison.OrdinalIgnoreCase) &&
                (parameterName.Equals("MinArea", StringComparison.OrdinalIgnoreCase) ||
                 parameterName.Equals("MaxArea", StringComparison.OrdinalIgnoreCase)))
            {
                return "area_range_parameter";
            }

            if (operatorType.Equals("ResultJudgment", StringComparison.OrdinalIgnoreCase) &&
                parameterName.Equals("ExpectValue", StringComparison.OrdinalIgnoreCase))
            {
                return "calibration_parameter";
            }
        }

        var resourceKind = VisionAgentResourceClassifier.Classify(operatorType, parameterName);
        return IsPreferredResourceParameter(operatorType, parameterName, resourceKind)
            ? resourceKind
            : string.Empty;
    }
}
