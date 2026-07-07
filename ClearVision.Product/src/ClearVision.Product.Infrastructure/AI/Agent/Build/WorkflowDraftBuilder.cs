using ClearVision.Product.Application.DTOs;
using ClearVision.Product.Core.DTOs;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Services;
using ClearVision.Product.Core.ValueObjects;
using ClearVision.Product.Infrastructure.AI.AgentRun;
using ClearVision.Product.Infrastructure.AI.Tools;

namespace ClearVision.Product.Infrastructure.AI.Agent;

public sealed class WorkflowDraftBuilder
{
    private readonly IVisionAgentOperatorContractCatalog _contractCatalog;

    public WorkflowDraftBuilder()
        : this(new VisionAgentOperatorContractCatalog())
    {
    }

    public WorkflowDraftBuilder(IOperatorFactory operatorFactory)
        : this(new VisionAgentOperatorContractCatalog(operatorFactory))
    {
    }

    internal WorkflowDraftBuilder(IVisionAgentOperatorContractCatalog contractCatalog)
    {
        _contractCatalog = contractCatalog;
    }

    internal Task<BuildStepResult<DraftWorkflowResolution>> DraftAsync(
        AiFlowGenerationRequest request,
        BuildPlanLoad load,
        BuildIntentResolution intent,
        OperatorPipelineResolution pipeline,
        ParameterMappingResolution parameters,
        CancellationToken cancellationToken)
    {
        var connectionSpecs = BuildConnectionSpecs(pipeline.Steps);
        var canonical = BuildCanonicalDraft(pipeline, parameters, connectionSpecs);
        var canvasFlow = BuildCanvasFlow(load, intent, pipeline, parameters, connectionSpecs);
        var generation = BuildDraftGenerationResult(canvasFlow);

        var resolution = new DraftWorkflowResolution(
            generation,
            canonical.WorkflowDraft,
            canonical.EntryOperatorTempId,
            canvasFlow,
            canonical.AddedNodeIds);
        var result = VisionAgentBuildSupport.StepResult(
            resolution,
            $"工作流草稿已生成，包含 {pipeline.Steps.Count} 个计划算子。",
            generation.Success || canvasFlow.Operators.Count > 0
                ? AgentRunEventStatuses.Completed
                : AgentRunEventStatuses.Failed,
            new
            {
                operatorTypes = pipeline.Steps.Select(item => item.OperatorType).ToList(),
                operatorCount = pipeline.Steps.Count,
                canvasOperatorCount = canvasFlow.Operators.Count,
                connectionCount = canonical.ConnectionCount,
                buildIntent = intent.BuildIntent,
                preservedExistingFlow = load.HasCurrentFlow && intent.BuildIntent != "new",
                metadataOnly = true
            },
            applyImpact: canvasFlow.Operators.Count > 0 ? "editable_draft_allowed" : "blocked",
            deploymentImpact: "requires_readiness_checks");
        return Task.FromResult(result);
    }

    private static AiFlowGenerationResult BuildDraftGenerationResult(OperatorFlowDto canvasFlow)
    {
        var hasOperators = canvasFlow.Operators.Count > 0;
        return new AiFlowGenerationResult
        {
            Success = hasOperators,
            CompletionStatus = hasOperators
                ? AiFlowGenerationResult.CompletionStatusCompleted
                : AiFlowGenerationResult.CompletionStatusFailed,
            Flow = canvasFlow,
            ClarificationRequired = false,
            RequirementBrief = null,
            FailureType = null,
            FailureSummary = null,
            ErrorMessage = null,
            InteractionState = hasOperators
                ? AiInteractionStates.Completed
                : AiInteractionStates.Failed
        };
    }

    internal BuildStepResult<RepairDraftResolution> Repair(
        DraftWorkflowResolution draft,
        OperatorPipelineResolution pipeline,
        ParameterMappingResolution parameters,
        IReadOnlyList<string> issueCodes,
        int repairRound)
    {
        var connectionSpecs = BuildConnectionSpecs(pipeline.Steps, forceBestEffort: true);
        var repaired = BuildCanonicalDraft(pipeline, parameters, connectionSpecs);
        var normalizedCodes = issueCodes
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(code => code, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var reason = normalizedCodes.Count == 0
            ? "validation_or_dryrun_blocking_issue"
            : string.Join(",", normalizedCodes);
        var record = new VisionAgentBuildRepairRecord
        {
            Stage = "validate_schema",
            RepairReason = reason,
            DiffSummary = $"第 {repairRound} 轮：按真实算子契约重建元数据草稿参数和兼容连线。",
            ResultStatus = "repaired",
            MetadataOnly = true
        };
        var nextDraft = draft with
        {
            WorkflowDraft = repaired.WorkflowDraft,
            EntryOperatorTempId = repaired.EntryOperatorTempId,
            AddedNodeIds = repaired.AddedNodeIds
        };
        return VisionAgentBuildSupport.StepResult(
            new RepairDraftResolution(nextDraft, record),
            $"已完成第 {repairRound} 轮自动修复：{record.RepairReason}。",
            AgentRunEventStatuses.Completed,
            new
            {
                repairRound,
                issueCodes = normalizedCodes,
                repairReason = record.RepairReason,
                diffSummary = record.DiffSummary,
                resultStatus = record.ResultStatus,
                metadataOnly = true
            },
            repairAction: $"contract_repair_round_{repairRound}",
            applyImpact: "editable_draft_allowed",
            deploymentImpact: "readiness_recheck_required");
    }

    private CanonicalDraft BuildCanonicalDraft(
        OperatorPipelineResolution pipeline,
        ParameterMappingResolution parameters,
        IReadOnlyList<ConnectionSpec> connectionSpecs)
    {
        var operators = pipeline.Steps.Select(step =>
        {
            _contractCatalog.TryGet(step.OperatorType, out var contract);
            var allowedParameters = contract?.Parameters
                .Select(parameter => parameter.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];
            var mappedParameters = parameters.Mappings
                .Where(item => string.Equals(item.TempId, step.TempId, StringComparison.OrdinalIgnoreCase))
                .Where(item => allowedParameters.Contains(item.ParameterName))
                .ToDictionary(item => item.ParameterName, item => item.ValueSummary, StringComparer.OrdinalIgnoreCase);

            return new
            {
                tempId = step.TempId,
                operatorType = _contractCatalog.CanonicalizeOperatorType(step.OperatorType),
                displayName = contract?.DisplayName ?? step.OperatorType,
                parameters = mappedParameters
            };
        }).ToList<object>();
        var connections = connectionSpecs.Select(spec => new
        {
            sourceTempId = spec.SourceTempId,
            sourcePortName = spec.SourcePortName,
            targetTempId = spec.TargetTempId,
            targetPortName = spec.TargetPortName
        }).ToList<object>();
        var draft = new
        {
            operators,
            connections,
            entryOperatorTempId = pipeline.Steps.FirstOrDefault()?.TempId ?? string.Empty,
            metadataOnly = true
        };
        return new CanonicalDraft(
            draft,
            pipeline.Steps.FirstOrDefault()?.TempId ?? string.Empty,
            pipeline.Steps.Select(item => item.TempId).ToList(),
            connections.Count);
    }

    private OperatorFlowDto BuildCanvasFlow(
        BuildPlanLoad load,
        BuildIntentResolution intent,
        OperatorPipelineResolution pipeline,
        ParameterMappingResolution parameters,
        IReadOnlyList<ConnectionSpec> connectionSpecs)
    {
        var flow = intent.BuildIntent != "new"
            ? VisionAgentBuildSupport.TryReadExistingCanvasFlow(load.CurrentFlowSnapshot) ?? new OperatorFlowDto()
            : new OperatorFlowDto();
        flow.Id = flow.Id == Guid.Empty ? Guid.NewGuid() : flow.Id;
        flow.Name = string.IsNullOrWhiteSpace(flow.Name)
            ? VisionAgentBuildSupport.FirstNonEmpty(load.Plan?.Goal, "Vision Agent workflow draft")
            : flow.Name;

        var pipelineTempIds = pipeline.Steps
            .Select(step => step.TempId)
            .Where(tempId => !string.IsNullOrWhiteSpace(tempId))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var operatorsByTempId = flow.Operators
            .Where(op => !string.IsNullOrWhiteSpace(op.Name) && pipelineTempIds.Contains(op.Name))
            .GroupBy(op => op.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        foreach (var step in pipeline.Steps)
        {
            if (operatorsByTempId.ContainsKey(step.TempId))
            {
                continue;
            }

            var op = BuildCanvasOperator(step, parameters, flow.Operators.Count);
            flow.Operators.Add(op);
            operatorsByTempId[step.TempId] = op;
        }

        AddCanvasConnections(flow, connectionSpecs, operatorsByTempId);
        return flow;
    }

    private OperatorDto BuildCanvasOperator(
        VisionAgentOperatorPipelineStep step,
        ParameterMappingResolution parameters,
        int index)
    {
        if (!_contractCatalog.TryGet(step.OperatorType, out var contract))
        {
            throw new InvalidOperationException($"Unknown operator type '{step.OperatorType}' cannot be added to canvas draft.");
        }

        var mapped = parameters.Mappings
            .Where(item => string.Equals(item.TempId, step.TempId, StringComparison.OrdinalIgnoreCase))
            .ToDictionary(item => item.ParameterName, item => item.ValueSummary, StringComparer.OrdinalIgnoreCase);
        return new OperatorDto
        {
            Id = Guid.NewGuid(),
            Name = CanvasOperatorName(contract, step.OperatorType),
            Type = ToOperatorType(step.OperatorType),
            X = 160 + index * 180,
            Y = 180,
            InputPorts = contract.InputPorts.Select(port => new PortDto
            {
                Id = Guid.NewGuid(),
                Name = port.Name,
                Direction = PortDirection.Input,
                DataType = port.DataType,
                IsRequired = port.IsRequired
            }).ToList(),
            OutputPorts = contract.OutputPorts.Select(port => new PortDto
            {
                Id = Guid.NewGuid(),
                Name = port.Name,
                Direction = PortDirection.Output,
                DataType = port.DataType,
                IsRequired = false
            }).ToList(),
            Parameters = contract.Parameters.Select(parameter =>
            {
                mapped.TryGetValue(parameter.Name, out var mappedValue);
                var value = string.IsNullOrWhiteSpace(mappedValue)
                    ? parameter.DefaultValue
                    : mappedValue;
                return new ParameterDto
                {
                    Id = Guid.NewGuid(),
                    Name = parameter.Name,
                    DisplayName = string.IsNullOrWhiteSpace(parameter.DisplayName) ? parameter.Name : parameter.DisplayName,
                    Description = parameter.Description,
                    DataType = parameter.DataType,
                    Value = value,
                    DefaultValue = parameter.DefaultValue,
                    MinValue = parameter.MinValue,
                    MaxValue = parameter.MaxValue,
                    IsRequired = parameter.IsRequired,
                    Options = CloneOptions(parameter.Options)
                };
            }).ToList(),
            IsEnabled = true
        };
    }

    private IReadOnlyList<ConnectionSpec> BuildConnectionSpecs(
        IReadOnlyList<VisionAgentOperatorPipelineStep> steps,
        bool forceBestEffort = false)
    {
        var specs = new List<ConnectionSpec>();
        var targetPorts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(ConnectionSpec? spec)
        {
            if (spec == null)
            {
                return;
            }

            var key = $"{spec.TargetTempId}.{spec.TargetPortName}";
            if (!targetPorts.Add(key))
            {
                return;
            }

            if (specs.Any(existing =>
                    string.Equals(existing.SourceTempId, spec.SourceTempId, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(existing.SourcePortName, spec.SourcePortName, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(existing.TargetTempId, spec.TargetTempId, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(existing.TargetPortName, spec.TargetPortName, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            specs.Add(spec);
        }

        for (var targetIndex = 1; targetIndex < steps.Count; targetIndex++)
        {
            var target = steps[targetIndex];
            if (!_contractCatalog.TryGet(target.OperatorType, out var targetContract))
            {
                continue;
            }

            foreach (var input in targetContract.InputPorts.Where(port => port.IsRequired))
            {
                if (!string.IsNullOrWhiteSpace(VisionAgentResourceClassifier.Classify(target.OperatorType, input.Name, input.DataType.ToString())))
                {
                    continue;
                }

                Add(FindLatestCompatibleSource(steps, targetIndex, target, input.Name, PreferredSourcePorts(target.OperatorType, input.Name)));
            }

            switch (target.OperatorType)
            {
                case "RoiManager":
                case "DeepLearning":
                case "SurfaceDefectDetection":
                case "BlobAnalysis":
                case "TemplateMatching":
                case "CircleMeasurement":
                    Add(FindLatestCompatibleSource(steps, targetIndex, target, "Image", ["Image", "DefectMask", "Mask"]));
                    break;
                case "DetectionSequenceJudge":
                    Add(FindLatestCompatibleSource(steps, targetIndex, target, "Detections", ["DetectionList", "Defects", "Objects", "SortedDetections"]));
                    break;
                case "Measurement":
                    AddMeasurementPointConnections(steps, targetIndex, target, Add);
                    Add(FindLatestCompatibleSource(steps, targetIndex, target, "Image", ["Image"]));
                    break;
                case "UnitConvert":
                    Add(FindLatestCompatibleSource(steps, targetIndex, target, "Value", ["Distance", "Radius", "Diameter", "DefectArea"]));
                    break;
                case "ResultJudgment":
                    Add(FindLatestCompatibleSource(steps, targetIndex, target, "Value",
                        ["TopClassLabel", "Result", "IsMatch", "BlobCount", "DefectCount", "ObjectCount", "MatchCount", "Score", "Distance", "JudgmentResult"]));
                    Add(FindLatestCompatibleSource(steps, targetIndex, target, "Confidence", ["TopClassConfidence", "Score", "NormalizedScore"]));
                    break;
                case "ResultOutput":
                    Add(FindLatestCompatibleSource(steps, targetIndex, target, "Result",
                        ["JudgmentResult", "IsOk", "ConditionResult", "Result", "Data", "DefectCount", "BlobCount", "MatchCount"]));
                    Add(FindLatestCompatibleSource(steps, targetIndex, target, "Image", ["Image"]));
                    break;
            }

            if (forceBestEffort)
            {
                foreach (var input in targetContract.InputPorts)
                {
                    Add(FindLatestCompatibleSource(steps, targetIndex, target, input.Name, PreferredSourcePorts(target.OperatorType, input.Name)));
                }
            }
        }

        return specs;
    }

    private void AddMeasurementPointConnections(
        IReadOnlyList<VisionAgentOperatorPipelineStep> steps,
        int targetIndex,
        VisionAgentOperatorPipelineStep target,
        Action<ConnectionSpec?> add)
    {
        var circleSources = steps
            .Take(targetIndex)
            .Where(step => string.Equals(step.OperatorType, "CircleMeasurement", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (circleSources.Count > 0)
        {
            add(BuildConnection(circleSources[0], "Center", target, "PointA"));
        }

        if (circleSources.Count > 1)
        {
            add(BuildConnection(circleSources[1], "Center", target, "PointB"));
        }
    }

    private ConnectionSpec? FindLatestCompatibleSource(
        IReadOnlyList<VisionAgentOperatorPipelineStep> steps,
        int targetIndex,
        VisionAgentOperatorPipelineStep target,
        string targetPortName,
        IReadOnlyList<string> preferredSourcePorts)
    {
        if (!_contractCatalog.TryGet(target.OperatorType, out var targetContract))
        {
            return null;
        }

        var targetPort = targetContract.InputPorts.FirstOrDefault(port =>
            string.Equals(port.Name, targetPortName, StringComparison.OrdinalIgnoreCase));
        if (targetPort == null)
        {
            return null;
        }

        for (var sourceIndex = targetIndex - 1; sourceIndex >= 0; sourceIndex--)
        {
            var source = steps[sourceIndex];
            if (!_contractCatalog.TryGet(source.OperatorType, out var sourceContract))
            {
                continue;
            }

            foreach (var preferredPort in preferredSourcePorts)
            {
                var sourcePort = sourceContract.OutputPorts.FirstOrDefault(port =>
                    string.Equals(port.Name, preferredPort, StringComparison.OrdinalIgnoreCase));
                if (sourcePort != null &&
                    PortDataTypeCompatibility.AreCompatible(sourcePort.DataType, targetPort.DataType))
                {
                    return new ConnectionSpec(source.TempId, sourcePort.Name, target.TempId, targetPort.Name);
                }
            }

            var compatible = sourceContract.OutputPorts.FirstOrDefault(port =>
                PortDataTypeCompatibility.AreCompatible(port.DataType, targetPort.DataType));
            if (compatible != null)
            {
                return new ConnectionSpec(source.TempId, compatible.Name, target.TempId, targetPort.Name);
            }
        }

        return null;
    }

    private ConnectionSpec? BuildConnection(
        VisionAgentOperatorPipelineStep source,
        string sourcePortName,
        VisionAgentOperatorPipelineStep target,
        string targetPortName)
    {
        if (!_contractCatalog.TryGet(source.OperatorType, out var sourceContract) ||
            !_contractCatalog.TryGet(target.OperatorType, out var targetContract))
        {
            return null;
        }

        var sourcePort = sourceContract.OutputPorts.FirstOrDefault(port =>
            string.Equals(port.Name, sourcePortName, StringComparison.OrdinalIgnoreCase));
        var targetPort = targetContract.InputPorts.FirstOrDefault(port =>
            string.Equals(port.Name, targetPortName, StringComparison.OrdinalIgnoreCase));
        if (sourcePort == null ||
            targetPort == null ||
            !PortDataTypeCompatibility.AreCompatible(sourcePort.DataType, targetPort.DataType))
        {
            return null;
        }

        return new ConnectionSpec(source.TempId, sourcePort.Name, target.TempId, targetPort.Name);
    }

    private static IReadOnlyList<string> PreferredSourcePorts(string operatorType, string targetPortName)
    {
        if (targetPortName.Equals("Image", StringComparison.OrdinalIgnoreCase))
        {
            return ["Image", "DefectMask", "Mask"];
        }

        if (targetPortName.Equals("Value", StringComparison.OrdinalIgnoreCase))
        {
            return ["TopClassLabel", "Result", "Distance", "DefectCount", "BlobCount", "MatchCount", "Score", "IsMatch"];
        }

        if (targetPortName.Equals("Result", StringComparison.OrdinalIgnoreCase))
        {
            return ["JudgmentResult", "IsOk", "Result", "Data"];
        }

        return operatorType switch
        {
            "DetectionSequenceJudge" => ["DetectionList", "Defects", "Objects"],
            "UnitConvert" => ["Distance", "Radius", "Diameter"],
            _ => ["Output", "Result", "Image", "Data"]
        };
    }

    private static void AddCanvasConnections(
        OperatorFlowDto flow,
        IReadOnlyList<ConnectionSpec> connectionSpecs,
        IReadOnlyDictionary<string, OperatorDto> operatorsByTempId)
    {
        foreach (var spec in connectionSpecs)
        {
            if (!operatorsByTempId.TryGetValue(spec.SourceTempId, out var source) ||
                !operatorsByTempId.TryGetValue(spec.TargetTempId, out var target))
            {
                continue;
            }

            var sourcePort = source.OutputPorts.FirstOrDefault(port =>
                string.Equals(port.Name, spec.SourcePortName, StringComparison.OrdinalIgnoreCase));
            var targetPort = target.InputPorts.FirstOrDefault(port =>
                string.Equals(port.Name, spec.TargetPortName, StringComparison.OrdinalIgnoreCase));
            if (sourcePort == null ||
                targetPort == null ||
                flow.Connections.Any(connection =>
                    connection.SourceOperatorId == source.Id &&
                    connection.SourcePortId == sourcePort.Id &&
                    connection.TargetOperatorId == target.Id &&
                    connection.TargetPortId == targetPort.Id))
            {
                continue;
            }

            flow.Connections.Add(new OperatorConnectionDto
            {
                Id = Guid.NewGuid(),
                SourceOperatorId = source.Id,
                SourcePortId = sourcePort.Id,
                TargetOperatorId = target.Id,
                TargetPortId = targetPort.Id
            });
        }
    }

    private static string CanvasOperatorName(
        VisionAgentOperatorContract contract,
        string requestedOperatorType)
    {
        return VisionAgentBuildSupport.FirstNonEmpty(
            contract.DisplayName,
            contract.OperatorType,
            requestedOperatorType,
            "Operator");
    }

    private OperatorType ToOperatorType(string operatorType)
    {
        var canonical = _contractCatalog.CanonicalizeOperatorType(operatorType);
        if (Enum.TryParse<OperatorType>(canonical, ignoreCase: true, out var parsed))
        {
            return OperatorTypeAliasResolver.Resolve(parsed);
        }

        throw new InvalidOperationException($"Operator type '{operatorType}' is not a ClearVision OperatorType.");
    }

    private static List<ParameterOption>? CloneOptions(IReadOnlyList<ParameterOption>? options)
    {
        return options?.Select(option => new ParameterOption
        {
            Label = option.Label,
            Value = option.Value
        }).ToList();
    }

    private sealed record ConnectionSpec(
        string SourceTempId,
        string SourcePortName,
        string TargetTempId,
        string TargetPortName);
}
