using System.Globalization;
using System.Text.RegularExpressions;
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
        var operatorOrdinals = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var op in pipeline.Steps)
        {
            operatorOrdinals.TryGetValue(op.OperatorType, out var operatorIndex);
            operatorOrdinals[op.OperatorType] = operatorIndex + 1;
            var operatorKey = VisionAgentResourceIdentity.OperatorKey(op.OperatorType, operatorIndex);
            if (!_contractCatalog.TryGet(op.OperatorType, out var schema))
            {
                continue;
            }

            var mappedByName = schema.Parameters.ToDictionary(
                parameter => parameter.Name,
                parameter => MapParameterValue(op, parameter, load, parameterStrategy),
                StringComparer.OrdinalIgnoreCase);
            var disabledParameters = ResolveDisabledParameters(schema, mappedByName.Values);

            foreach (var parameter in schema.Parameters)
            {
                if (disabledParameters.Contains(parameter.Name))
                {
                    continue;
                }

                var mapped = mappedByName[parameter.Name];
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
                    var canonicalId = VisionAgentResourceIdentity.CreateCanonicalId(
                        missingKind,
                        operatorKey,
                        parameter.Name);
                    var bound = load.ResourceDecisions.Any(decision =>
                        decision.Status.Equals(VisionAgentResourceStatuses.Bound, StringComparison.OrdinalIgnoreCase) &&
                        decision.CanonicalId.Equals(canonicalId, StringComparison.OrdinalIgnoreCase));
                    if (bound)
                    {
                        continue;
                    }

                    missing.Add(new AiMissingResourceInfo
                    {
                        CanonicalId = canonicalId,
                        ResourceType = VisionAgentResourceIdentity.NormalizeResourceType(missingKind),
                        ResourceName = ResourceName(missingKind),
                        ResourceKey = $"{op.TempId}.{parameter.Name}",
                        OperatorKey = operatorKey,
                        OperatorId = op.TempId,
                        OperatorType = op.OperatorType,
                        OperatorIndex = operatorIndex,
                        ParameterName = parameter.Name,
                        Status = VisionAgentResourceStatuses.Pending,
                        BlockingScope = VisionAgentResourceBlockingScopes.DeployRun,
                        Source = "parameter_mapping",
                        ResolutionTarget = ResolutionTarget(missingKind),
                        DraftPolicy = DraftPolicy(missingKind),
                        Aliases = VisionAgentResourceIdentity.BuildAliases(
                            canonicalId,
                            $"{op.TempId}.{parameter.Name}",
                            missingKind,
                            operatorKey,
                            parameter.Name).ToList(),
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

    private static HashSet<string> ResolveDisabledParameters(
        VisionAgentOperatorContract schema,
        IEnumerable<VisionAgentParameterMapping> mappings)
    {
        var mappedParameters = mappings.ToList();
        if (schema.ParameterConstraints is not { Count: > 0 })
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        var values = mappedParameters.ToDictionary(
            mapping => mapping.ParameterName,
            mapping => (object?)mapping.ValueSummary,
            StringComparer.OrdinalIgnoreCase);

        var disabled = OperatorParameterConstraintEvaluator.ResolveStates(schema.Metadata, values)
            .Where(state => state.EffectiveDisabled)
            .Select(state => state.Constraint.Parameter)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // A deferred source choice is a real third draft state, not an implicit camera/file
        // choice. Keep both conditional resource branches out of the draft until the user
        // selects one; otherwise their shared operator aliases can also collapse two distinct
        // resources into one misleading requirement.
        var sourceType = mappedParameters.FirstOrDefault(mapping =>
            mapping.ParameterName.Equals("SourceType", StringComparison.OrdinalIgnoreCase));
        if (schema.OperatorType.Equals("ImageAcquisition", StringComparison.OrdinalIgnoreCase) &&
            sourceType?.Pending == true)
        {
            disabled.Add("FilePath");
            disabled.Add("CameraId");
            disabled.Add("CameraBindingId");
        }

        return disabled;
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
            !IsTaskAwareJudgmentParameter(op.OperatorType, parameter.Name) &&
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

    private static bool IsTaskAwareJudgmentParameter(string operatorType, string parameterName)
    {
        return operatorType.Equals("ResultJudgment", StringComparison.OrdinalIgnoreCase) &&
               parameterName is "FieldName" or "Condition" or "ExpectValue" or
                   "ExpectValueMin" or "ExpectValueMax" or "MinConfidence";
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
            return VisionAgentImageSourceResolver.Resolve(EffectiveImageSource(load)).SourceType;
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

        if (IsOutputChannelBindingParameter(operatorType, parameterName))
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

        if (operatorType.Equals("ResultJudgment", StringComparison.OrdinalIgnoreCase))
        {
            var judgmentValue = TaskAwareJudgmentParameterValue(parameterName, load);
            if (judgmentValue != null)
            {
                return judgmentValue;
            }
        }

        return operatorType switch
        {
            "DeepLearning" when parameterName.Equals("TaskType", StringComparison.OrdinalIgnoreCase) =>
                EffectiveDeepLearningTaskType(load, parameterStrategy),
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

    private static string? TaskAwareJudgmentParameterValue(
        string parameterName,
        BuildPlanLoad load)
    {
        var strategy = ResolveJudgmentStrategy(load);
        return parameterName switch
        {
            "FieldName" => strategy.FieldName,
            "Condition" => strategy.Condition,
            "ExpectValue" => strategy.ExpectValue,
            "ExpectValueMin" => strategy.ExpectValueMin,
            "ExpectValueMax" => strategy.ExpectValueMax,
            "MinConfidence" => strategy.MinConfidence,
            _ => null
        };
    }

    private static JudgmentStrategy ResolveJudgmentStrategy(BuildPlanLoad load)
    {
        var rawTaskType = EffectiveTaskType(load);
        var taskType = AiVisionTaskCatalog.TryNormalizePrimary(rawTaskType, out var canonicalTaskType)
            ? canonicalTaskType
            : rawTaskType;
        var acceptance = AcceptanceText(load);

        if (taskType.Equals(AiVisionTaskTypes.TemplateLocation, StringComparison.OrdinalIgnoreCase))
        {
            return new JudgmentStrategy(
                "IsMatch",
                "Equal",
                "true",
                string.Empty,
                string.Empty,
                ResolveConfidence(load, acceptance, "0.8"));
        }

        if (taskType.Equals(AiVisionTaskTypes.WireSequence, StringComparison.OrdinalIgnoreCase) ||
            IsWireSequenceScenario(load))
        {
            return new JudgmentStrategy("IsMatch", "Equal", "true", string.Empty, string.Empty, "0");
        }

        if (taskType.Equals(AiVisionTaskTypes.SurfaceDefect, StringComparison.OrdinalIgnoreCase))
        {
            var areaJudgment = MentionsArea(load, acceptance);
            var upperBound = FirstExplicitValue(
                load,
                areaJudgment
                    ? ["max_defect_area", "defect_area_max", "defect_upper_bound", "upper_bound", "ResultJudgment.ExpectValue", "ExpectValue"]
                    : ["max_defect_count", "defect_count_max", "defect_upper_bound", "upper_bound", "ResultJudgment.ExpectValue", "ExpectValue"]);
            upperBound = NormalizeNumericValue(upperBound) ?? ExtractUpperBound(acceptance);
            if (string.IsNullOrWhiteSpace(upperBound) && MeansNoDefect(AcceptedOutcomeText(acceptance)))
            {
                upperBound = "0";
            }

            return new JudgmentStrategy(
                areaJudgment ? "DefectArea" : "DefectCount",
                "LessOrEqual",
                FirstNonEmpty(upperBound, "<pending-defect-upper-bound>"),
                string.Empty,
                string.Empty,
                "0");
        }

        if (taskType.Equals(AiVisionTaskTypes.PresenceAbsence, StringComparison.OrdinalIgnoreCase))
        {
            var expectation = ResolvePresenceExpectation(load, acceptance);
            return expectation switch
            {
                PresenceExpectation.Present => new JudgmentStrategy(
                    "PresenceCount", "GreaterOrEqual", "1", string.Empty, string.Empty, "0"),
                PresenceExpectation.Absent => new JudgmentStrategy(
                    "PresenceCount", "Equal", "0", string.Empty, string.Empty, "0"),
                _ => new JudgmentStrategy(
                    "PresenceCount", "GreaterOrEqual", "<pending-presence-expectation>", string.Empty, string.Empty, "0")
            };
        }

        if (taskType.Equals(AiVisionTaskTypes.AttributeClassification, StringComparison.OrdinalIgnoreCase) ||
            IsAttributeClassificationScenario(load))
        {
            return new JudgmentStrategy(
                "TopClassLabel",
                "Equal",
                ExpectedClassificationOkValue(load),
                string.Empty,
                string.Empty,
                ResolveConfidence(load, acceptance, "0.6"));
        }

        if (taskType.Equals(AiVisionTaskTypes.GeometryMeasurement, StringComparison.OrdinalIgnoreCase) ||
            IsMeasurementScenario(load))
        {
            var minimum = NormalizeNumericValue(FirstExplicitValue(
                load,
                "ResultJudgment.ExpectValueMin",
                "ExpectValueMin",
                "measurement_min",
                "lower_bound",
                "min_value"));
            var maximum = NormalizeNumericValue(FirstExplicitValue(
                load,
                "ResultJudgment.ExpectValueMax",
                "ExpectValueMax",
                "measurement_max",
                "upper_bound",
                "max_value"));
            if (TryExtractRange(acceptance, out var rangeMin, out var rangeMax))
            {
                minimum ??= rangeMin;
                maximum ??= rangeMax;
            }

            minimum ??= ExtractLowerBound(acceptance);
            maximum ??= ExtractUpperBound(acceptance);
            return new JudgmentStrategy(
                "Value",
                "Range",
                string.Empty,
                FirstNonEmpty(minimum, "<pending-measurement-minimum>"),
                FirstNonEmpty(maximum, "<pending-measurement-maximum>"),
                "0");
        }

        if (taskType.Equals(AiVisionTaskTypes.CodeRecognition, StringComparison.OrdinalIgnoreCase))
        {
            var expectedCode = ResolveExpectedCode(load, acceptance);
            return !string.IsNullOrWhiteSpace(expectedCode)
                ? new JudgmentStrategy("Text", "Equal", expectedCode, string.Empty, string.Empty, "0")
                : new JudgmentStrategy("CodeCount", "GreaterOrEqual", "1", string.Empty, string.Empty, "0");
        }

        if (taskType.Equals(AiVisionTaskTypes.ObjectDetection, StringComparison.OrdinalIgnoreCase))
        {
            return ResolveObjectCountStrategy(load, acceptance);
        }

        return new JudgmentStrategy(
            "Value",
            "Equal",
            "<pending-judgment-rule>",
            string.Empty,
            string.Empty,
            "0");
    }

    private static JudgmentStrategy ResolveObjectCountStrategy(BuildPlanLoad load, string acceptance)
    {
        var exact = NormalizeNumericValue(FirstExplicitValue(
            load,
            "expected_object_count",
            "object_count",
            "ResultJudgment.ExpectValue",
            "ExpectValue")) ?? ExtractExactCount(acceptance);
        var minimum = NormalizeNumericValue(FirstExplicitValue(
            load,
            "min_object_count",
            "object_count_min",
            "lower_bound")) ?? ExtractLowerBound(acceptance);
        var maximum = NormalizeNumericValue(FirstExplicitValue(
            load,
            "max_object_count",
            "object_count_max",
            "upper_bound")) ?? ExtractUpperBound(acceptance);

        if (!string.IsNullOrWhiteSpace(exact))
        {
            return new JudgmentStrategy("ObjectCount", "Equal", exact, string.Empty, string.Empty, "0");
        }

        if (!string.IsNullOrWhiteSpace(minimum) && !string.IsNullOrWhiteSpace(maximum))
        {
            return new JudgmentStrategy("ObjectCount", "Range", string.Empty, minimum, maximum, "0");
        }

        if (!string.IsNullOrWhiteSpace(minimum))
        {
            return new JudgmentStrategy("ObjectCount", "GreaterOrEqual", minimum, string.Empty, string.Empty, "0");
        }

        if (!string.IsNullOrWhiteSpace(maximum))
        {
            return new JudgmentStrategy("ObjectCount", "LessOrEqual", maximum, string.Empty, string.Empty, "0");
        }

        return new JudgmentStrategy(
            "ObjectCount",
            "GreaterOrEqual",
            "<pending-object-count-acceptance>",
            string.Empty,
            string.Empty,
            "0");
    }

    private static string ResolveConfidence(BuildPlanLoad load, string acceptance, string acceptedDefault)
    {
        var selected = NormalizeNumericValue(FirstExplicitValue(
            load,
            "ResultJudgment.MinConfidence",
            "MinConfidence",
            "min_confidence",
            "classification_min_confidence",
            "template_min_confidence"));
        var value = selected ?? ExtractConfidence(acceptance);
        if (value == null ||
            !double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
        {
            return acceptedDefault;
        }

        if (parsed > 1 && parsed <= 100)
        {
            parsed /= 100;
        }

        return parsed is >= 0 and <= 1
            ? parsed.ToString("G15", CultureInfo.InvariantCulture)
            : acceptedDefault;
    }

    private static PresenceExpectation ResolvePresenceExpectation(BuildPlanLoad load, string acceptance)
    {
        var explicitValue = FirstExplicitValue(
            load,
            "expected_presence",
            "presence_expected",
            "presence_state",
            "expected_state",
            "ResultJudgment.ExpectValue",
            "ExpectValue");
        var text = FirstNonEmpty(explicitValue, AcceptedOutcomeText(acceptance)).ToLowerInvariant();
        if (ContainsAny(text, "不得缺失", "不可缺失", "不能缺失", "must be present", "must exist", "not missing"))
        {
            return PresenceExpectation.Present;
        }

        if (ContainsAny(text, "absent", "missing", "not present", "not exist", "without", "缺失", "不存在", "未安装", "无此", "没有"))
        {
            return PresenceExpectation.Absent;
        }

        if (ContainsAny(text, "present", "exists", "exist", "installed", "detected", "存在", "已安装", "到位", "有此", "true", "yes") ||
            text == "1")
        {
            return PresenceExpectation.Present;
        }

        if (text is "false" or "no" or "0")
        {
            return PresenceExpectation.Absent;
        }

        return PresenceExpectation.Unknown;
    }

    private static string ResolveExpectedCode(BuildPlanLoad load, string acceptance)
    {
        var selected = FirstExplicitValue(
            load,
            "expected_code",
            "code_value",
            "expected_text",
            "ResultJudgment.ExpectValue",
            "ExpectValue");
        if (!string.IsNullOrWhiteSpace(selected))
        {
            return VisionAgentBuildSupport.CleanValue(selected);
        }

        var text = AcceptedOutcomeText(acceptance);
        var quoted = Regex.Match(
            text,
            "(?:code|barcode|qr|码值|条码|二维码|内容)\\s*(?:==|=|equals?|is|应为|必须为|为)\\s*[\\\"“'](?<value>[^\\\"”']+)[\\\"”']",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (quoted.Success)
        {
            return quoted.Groups["value"].Value.Trim();
        }

        var token = Regex.Match(
            text,
            "(?:code|barcode|qr|码值|条码|二维码|内容)\\s*(?:==|=|equals?|is|应为|必须为|为)\\s*(?<value>[A-Za-z0-9_.:/-]+)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!token.Success)
        {
            return string.Empty;
        }

        var candidate = token.Groups["value"].Value.Trim();
        return IsCodeOutcomeWord(candidate) ? string.Empty : candidate;
    }

    private static bool IsCodeOutcomeWord(string value) =>
        value.Equals("decode", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("decoded", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("read", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("recognized", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("detected", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("found", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("valid", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("success", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("successful", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("successfully", StringComparison.OrdinalIgnoreCase);

    private static string EffectiveImageSource(BuildPlanLoad load)
    {
        return VisionAgentBuildSupport.FirstNonEmpty(
            EffectiveValue(load, VisionAgentPlanAnswerFields.ImageSource),
            load.RequirementAnswers.TryGetValue(VisionAgentPlanAnswerFields.ImageSource, out var requirementSource)
                ? requirementSource
                : string.Empty,
            load.Plan?.SemanticExtraction?.ImageSource);
    }

    private static string AcceptanceText(BuildPlanLoad load)
    {
        return string.Join(
            "; ",
            new[]
            {
                EffectiveValue(load, VisionAgentPlanAnswerFields.AcceptanceCriteria),
                load.RequirementAnswers.TryGetValue(VisionAgentPlanAnswerFields.AcceptanceCriteria, out var requirementAcceptance)
                    ? requirementAcceptance
                    : string.Empty,
                load.Plan?.SemanticExtraction?.OkCondition,
                load.Plan?.SemanticExtraction?.NgCondition,
                load.Plan?.AcceptanceCriteria is { Count: > 0 }
                    ? string.Join("; ", load.Plan.AcceptanceCriteria)
                    : string.Empty,
                load.OriginalUserPrompt
            }
            .Where(value => !string.IsNullOrWhiteSpace(value)));
    }

    private static string AcceptedOutcomeText(string acceptance)
    {
        var parsed = VisionAgentPlanFieldPolicy.ParseAcceptanceCriteria(acceptance);
        return FirstNonEmpty(parsed.Ok, acceptance);
    }

    private static bool MentionsArea(BuildPlanLoad load, string acceptance)
    {
        var text = $"{acceptance} {EffectiveValue(load, VisionAgentPlanAnswerFields.MeasurementTarget)} " +
                   $"{string.Join(' ', load.Plan?.PlanFidelity?.RequiredOutputSemantics ?? [])}";
        return ContainsAny(text, "area", "面积", "defect_area");
    }

    private static bool MeansNoDefect(string text) =>
        ContainsAny(text, "no defect", "zero defect", "without defect", "无缺陷", "不得有缺陷", "缺陷为零", "缺陷数为0");

    private static string? FirstExplicitValue(BuildPlanLoad load, params string[] keys)
    {
        foreach (var key in keys)
        {
            if ((load.ParameterSelections.TryGetValue(key, out var value) ||
                 load.RequirementAnswers.TryGetValue(key, out value) ||
                 load.BuildDecisions.TryGetValue(key, out value)) &&
                !string.IsNullOrWhiteSpace(value) &&
                !VisionAgentPlanFieldPolicy.IsPlaceholderValue(value))
            {
                return VisionAgentBuildSupport.CleanValue(value);
            }
        }

        return null;
    }

    private static string? NormalizeNumericValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var direct) &&
            double.IsFinite(direct))
        {
            return direct.ToString("G15", CultureInfo.InvariantCulture);
        }

        var match = Regex.Match(value, "[-+]?(?:\\d+(?:\\.\\d+)?|\\.\\d+)", RegexOptions.CultureInvariant);
        return match.Success &&
               double.TryParse(match.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) &&
               double.IsFinite(parsed)
            ? parsed.ToString("G15", CultureInfo.InvariantCulture)
            : null;
    }

    private static bool TryExtractRange(string text, out string minimum, out string maximum)
    {
        minimum = string.Empty;
        maximum = string.Empty;
        var match = Regex.Match(
            text,
            "(?<min>[-+]?(?:\\d+(?:\\.\\d+)?|\\.\\d+))\\s*(?:~|～|至|到|\\bto\\b|-)\\s*(?<max>[-+]?(?:\\d+(?:\\.\\d+)?|\\.\\d+))",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!match.Success)
        {
            match = Regex.Match(
                text,
                "(?:between|范围)\\s*(?<min>[-+]?(?:\\d+(?:\\.\\d+)?|\\.\\d+))\\s*(?:and|与|到|至)\\s*(?<max>[-+]?(?:\\d+(?:\\.\\d+)?|\\.\\d+))",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        if (!match.Success)
        {
            return false;
        }

        minimum = NormalizeNumericValue(match.Groups["min"].Value) ?? string.Empty;
        maximum = NormalizeNumericValue(match.Groups["max"].Value) ?? string.Empty;
        return !string.IsNullOrWhiteSpace(minimum) && !string.IsNullOrWhiteSpace(maximum);
    }

    private static string? ExtractUpperBound(string text) => ExtractNumberAfterMarker(
        text,
        "(?:<=|≤|不超过|不得超过|至多|最多|at\\s+most|no\\s+more\\s+than|上限(?:为)?|max(?:imum)?(?:\\s+(?:value|count|area))?)");

    private static string? ExtractLowerBound(string text) => ExtractNumberAfterMarker(
        text,
        "(?:>=|≥|不少于|不低于|至少|at\\s+least|no\\s+fewer\\s+than|下限(?:为)?|min(?:imum)?(?:\\s+(?:value|count))?)");

    private static string? ExtractExactCount(string text) => ExtractNumberAfterMarker(
        text,
        "(?:exactly|等于|恰好|数量为|个数为|count\\s*(?:==|=|is))");

    private static string? ExtractConfidence(string text) => ExtractNumberAfterMarker(
        text,
        "(?:confidence|score|置信度|匹配分数)\\s*(?:>=|>|≥|不低于|至少|为)?");

    private static string? ExtractNumberAfterMarker(string text, string markerPattern)
    {
        var match = Regex.Match(
            text,
            $"{markerPattern}[^0-9+.-]*(?<value>[-+]?(?:\\d+(?:\\.\\d+)?|\\.\\d+))",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return match.Success ? NormalizeNumericValue(match.Groups["value"].Value) : null;
    }

    private static bool ContainsAny(string value, params string[] terms) =>
        terms.Any(term => value.Contains(term, StringComparison.OrdinalIgnoreCase));

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;

    private enum PresenceExpectation
    {
        Unknown,
        Present,
        Absent
    }

    private sealed record JudgmentStrategy(
        string FieldName,
        string Condition,
        string ExpectValue,
        string ExpectValueMin,
        string ExpectValueMax,
        string MinConfidence);

    private static bool IsPendingParameter(
        string operatorType,
        VisionAgentParameterContract parameter,
        string fallback,
        BuildPlanLoad load)
    {
        if (fallback.Contains("pending", StringComparison.OrdinalIgnoreCase) ||
            fallback.Contains("unsupported-image-source", StringComparison.OrdinalIgnoreCase))
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
            "image_file" => operatorType.Equals("ImageAcquisition", StringComparison.OrdinalIgnoreCase) &&
                            parameterName.Equals("FilePath", StringComparison.OrdinalIgnoreCase),
            "camera_binding" => parameterName.Equals("CameraId", StringComparison.OrdinalIgnoreCase) ||
                                parameterName.Equals("CameraBindingId", StringComparison.OrdinalIgnoreCase),
            "model_resource" => IsPreferredModelParameter(parameterName),
            "template_artifact" => parameterName.Equals("TemplateId", StringComparison.OrdinalIgnoreCase),
            "measurement_parameter" => operatorType.Equals("UnitConvert", StringComparison.OrdinalIgnoreCase) &&
                                       parameterName.Equals("Scale", StringComparison.OrdinalIgnoreCase),
            "plc_address" => parameterName.Contains("Address", StringComparison.OrdinalIgnoreCase) ||
                             parameterName.Contains("PLC", StringComparison.OrdinalIgnoreCase),
            "output_channel" => IsOutputChannelBindingParameter(operatorType, parameterName),
            _ => false
        };
    }

    private static bool IsOutputChannelBindingParameter(string operatorType, string parameterName)
    {
        return parameterName.Equals("OutputChannel", StringComparison.OrdinalIgnoreCase) ||
               parameterName.Equals("OutputChannelId", StringComparison.OrdinalIgnoreCase) ||
               (operatorType.Equals("ResultOutput", StringComparison.OrdinalIgnoreCase) &&
                parameterName.Equals("Channel", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsPreferredModelParameter(string parameterName)
    {
        return parameterName.Equals("ModelPath", StringComparison.OrdinalIgnoreCase) ||
               parameterName.Equals("ModelId", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsWireSequenceScenario(BuildPlanLoad load)
    {
        var taskType = EffectiveTaskType(load);
        var text = $"{EffectiveValue(load, VisionAgentPlanAnswerFields.AcceptanceCriteria)} {load.Plan?.Intent} {load.Plan?.Goal} {load.OriginalUserPrompt}";
        return taskType.Equals(AiVisionTaskTypes.WireSequence, StringComparison.OrdinalIgnoreCase) ||
               text.Contains("wire", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("sequence", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("terminal", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("line order", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsMeasurementScenario(BuildPlanLoad load)
    {
        var taskType = EffectiveTaskType(load);
        var text = $"{EffectiveValue(load, VisionAgentPlanAnswerFields.MeasurementTarget)} {EffectiveValue(load, VisionAgentPlanAnswerFields.AcceptanceCriteria)} {load.Plan?.Intent} {load.Plan?.Goal} {load.OriginalUserPrompt}";
        return taskType.Equals(AiVisionTaskTypes.GeometryMeasurement, StringComparison.OrdinalIgnoreCase) ||
               text.Contains("measurement", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("distance", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("spacing", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("hole", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("circle", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAttributeClassificationScenario(BuildPlanLoad load)
    {
        var taskType = EffectiveTaskType(load);
        var targetAttribute = EffectiveValue(load, VisionAgentPlanAnswerFields.TargetAttribute);
        var acceptance = EffectiveValue(load, VisionAgentPlanAnswerFields.AcceptanceCriteria);
        var text = $"{targetAttribute} {acceptance} {load.Plan?.Intent} {load.Plan?.Goal} {load.OriginalUserPrompt} {load.Plan?.RecommendedRoute?.RouteId}";
        return taskType.Equals(AiVisionTaskTypes.AttributeClassification, StringComparison.OrdinalIgnoreCase) ||
               taskType.Equals(AiVisionTaskTypes.Classification, StringComparison.OrdinalIgnoreCase) ||
               text.Contains("attribute_classification", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("classification", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("classify", StringComparison.OrdinalIgnoreCase);
    }

    private static string EffectiveDeepLearningTaskType(BuildPlanLoad load, string parameterStrategy)
    {
        if (parameterStrategy.Equals("deep_learning_classification", StringComparison.OrdinalIgnoreCase) ||
            IsAttributeClassificationScenario(load))
        {
            return "ImageClassification";
        }

        return IsSemanticSegmentationScenario(load)
            ? "SemanticSegmentation"
            : "ObjectDetection";
    }

    private static bool IsSemanticSegmentationScenario(BuildPlanLoad load)
    {
        var taskType = EffectiveTaskType(load);
        var text = $"{EffectiveValue(load, VisionAgentPlanAnswerFields.TargetAttribute)} {EffectiveValue(load, VisionAgentPlanAnswerFields.AcceptanceCriteria)} {load.Plan?.Intent} {load.Plan?.Goal} {load.OriginalUserPrompt} {load.Plan?.RecommendedRoute?.RouteId}";
        return taskType.Contains("segmentation", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("semantic segmentation", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("segmentation mask", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("pixel segmentation", StringComparison.OrdinalIgnoreCase);
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
                     "ok_label"
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
        var fromOkCondition = ExtractOkClassLabel(EffectiveValue(load, VisionAgentPlanAnswerFields.AcceptanceCriteria));
        if (!string.IsNullOrWhiteSpace(fromOkCondition))
        {
            return fromOkCondition;
        }

        fromOkCondition = ExtractOkClassLabel(semantic?.OkCondition);
        if (!string.IsNullOrWhiteSpace(fromOkCondition))
        {
            return fromOkCondition;
        }

        return "<pending-ok-class-label>";
    }

    private static string EffectiveTaskType(BuildPlanLoad load)
    {
        return VisionAgentBuildSupport.FirstNonEmpty(
            EffectiveValue(load, VisionAgentPlanAnswerFields.TaskType),
            load.Plan?.SemanticExtraction?.TaskType);
    }

    private static string EffectiveValue(BuildPlanLoad load, string field)
    {
        return load.EffectiveRequirement.Values.TryGetValue(field, out var value)
            ? VisionAgentBuildSupport.CleanValue(value)
            : string.Empty;
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

        return string.Empty;
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

        if (operatorType.Equals("ImageAcquisition", StringComparison.OrdinalIgnoreCase) &&
            parameterName.Equals("SourceType", StringComparison.OrdinalIgnoreCase))
        {
            return "image_source";
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

    private static string ResourceName(string resourceType)
    {
        return VisionAgentResourceIdentity.NormalizeResourceType(resourceType) switch
        {
            "image_source" => "图像来源",
            "image_file" => "图像文件",
            "camera_binding" => "相机绑定",
            "model_resource" => "模型资源",
            "template_artifact" => "模板资源",
            "calibration_resource" => "标定参数",
            "plc_output" => "外部输出资源",
            "output_channel" => "输出通道",
            _ => "工程资源"
        };
    }

    private static string ResolutionTarget(string resourceType)
    {
        return VisionAgentResourceIdentity.NormalizeResourceType(resourceType) switch
        {
            "image_source" => VisionAgentResourceResolutionTargets.PlanWorkbench,
            "image_file" => VisionAgentResourceResolutionTargets.ImageFilePicker,
            "camera_binding" => VisionAgentResourceResolutionTargets.CameraSettings,
            "model_resource" => VisionAgentResourceResolutionTargets.ModelPicker,
            "template_artifact" => VisionAgentResourceResolutionTargets.TemplatePicker,
            "calibration_resource" => VisionAgentResourceResolutionTargets.CalibrationSettings,
            "plc_output" or "output_channel" => VisionAgentResourceResolutionTargets.OutputSettings,
            _ => VisionAgentResourceResolutionTargets.PlanWorkbench
        };
    }

    private static string DraftPolicy(string resourceType)
    {
        return VisionAgentResourceIdentity.NormalizeResourceType(resourceType) is "plc_output" or "output_channel"
            ? VisionAgentResourceDraftPolicies.BuildRequired
            : VisionAgentResourceDraftPolicies.DraftAllowed;
    }
}
