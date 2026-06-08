using ClearVision.Product.Application.DTOs;
using ClearVision.Product.Core.DTOs;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Services;
using ClearVision.Product.Infrastructure.AI.AgentRun;
using ClearVision.Product.Infrastructure.AI.Tools;

namespace ClearVision.Product.Infrastructure.AI.Agent;

public sealed class WorkflowDraftBuilder
{
    private readonly IAiFlowGenerationService _generationService;

    public WorkflowDraftBuilder(IAiFlowGenerationService generationService)
    {
        _generationService = generationService;
    }

    internal async Task<BuildStepResult<DraftWorkflowResolution>> DraftAsync(
        AiFlowGenerationRequest request,
        BuildPlanLoad load,
        BuildIntentResolution intent,
        OperatorPipelineResolution pipeline,
        ParameterMappingResolution parameters,
        CancellationToken cancellationToken)
    {
        var generationRequest = request with
        {
            ExistingFlowJson = load.CurrentFlowSnapshot,
            Mode = ToGenerateFlowMode(intent.BuildIntent),
            TemplateSelection = load.TemplateSelection
        };
        var generation = await _generationService.GenerateFlowAsync(
            generationRequest,
            cancellationToken: cancellationToken);

        var canonical = BuildCanonicalDraft(pipeline, parameters);
        var canvasFlow = VisionAgentBuildSupport.FlowOperatorCount(generation.Flow) > 0
            ? generation.Flow as OperatorFlowDto ?? BuildCanvasFlow(load, intent, pipeline, parameters)
            : BuildCanvasFlow(load, intent, pipeline, parameters);
        generation.Flow ??= canvasFlow;

        var resolution = new DraftWorkflowResolution(
            generation,
            canonical.WorkflowDraft,
            canonical.EntryOperatorTempId,
            canvasFlow,
            canonical.AddedNodeIds);
        return VisionAgentBuildSupport.StepResult(
            resolution,
            $"Workflow draft produced with {pipeline.Steps.Count} planned operator(s).",
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
    }

    internal BuildStepResult<RepairDraftResolution> Repair(
        DraftWorkflowResolution draft,
        OperatorPipelineResolution pipeline,
        ParameterMappingResolution parameters)
    {
        var repaired = BuildCanonicalDraft(pipeline, parameters, forceLinearConnections: true);
        var record = new VisionAgentBuildRepairRecord
        {
            Stage = "validate_schema",
            RepairReason = "validation_or_dryrun_blocking_issue",
            DiffSummary = "Rebuilt metadata-only draft connections from the repaired operator pipeline.",
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
            "One automatic repair rebuilt draft connections from the operator pipeline.",
            AgentRunEventStatuses.Completed,
            new
            {
                repairReason = record.RepairReason,
                diffSummary = record.DiffSummary,
                resultStatus = record.ResultStatus,
                metadataOnly = true
            },
            repairAction: "rebuild_linear_connections",
            applyImpact: "editable_draft_allowed",
            deploymentImpact: "readiness_recheck_required");
    }

    private static CanonicalDraft BuildCanonicalDraft(
        OperatorPipelineResolution pipeline,
        ParameterMappingResolution parameters,
        bool forceLinearConnections = false)
    {
        var operators = pipeline.Steps.Select(step => new
        {
            tempId = step.TempId,
            operatorType = step.OperatorType,
            displayName = step.OperatorType,
            parameters = parameters.Mappings
                .Where(item => string.Equals(item.TempId, step.TempId, StringComparison.OrdinalIgnoreCase))
                .ToDictionary(item => item.ParameterName, item => item.ValueSummary, StringComparer.OrdinalIgnoreCase)
        }).ToList<object>();
        var connections = BuildCanonicalConnections(pipeline.Steps, forceLinearConnections).ToList();
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

    private static IEnumerable<object> BuildCanonicalConnections(
        IReadOnlyList<VisionAgentOperatorPipelineStep> steps,
        bool forceLinearConnections)
    {
        for (var index = 0; index < steps.Count - 1; index++)
        {
            var source = steps[index];
            var target = steps[index + 1];
            VisionAgentReadOnlyCatalog.Schemas.TryGetValue(source.OperatorType, out var sourceSchema);
            VisionAgentReadOnlyCatalog.Schemas.TryGetValue(target.OperatorType, out var targetSchema);
            var sourcePort = sourceSchema?.OutputPorts.FirstOrDefault();
            var targetPort = targetSchema?.InputPorts.FirstOrDefault();
            if (string.IsNullOrWhiteSpace(sourcePort) ||
                string.IsNullOrWhiteSpace(targetPort))
            {
                if (!forceLinearConnections)
                {
                    continue;
                }
            }

            yield return new
            {
                sourceTempId = source.TempId,
                sourcePortName = sourcePort ?? "Output",
                targetTempId = target.TempId,
                targetPortName = targetPort ?? "Input"
            };
        }
    }

    private static OperatorFlowDto BuildCanvasFlow(
        BuildPlanLoad load,
        BuildIntentResolution intent,
        OperatorPipelineResolution pipeline,
        ParameterMappingResolution parameters)
    {
        var flow = intent.BuildIntent != "new"
            ? VisionAgentBuildSupport.TryReadExistingCanvasFlow(load.CurrentFlowSnapshot) ?? new OperatorFlowDto()
            : new OperatorFlowDto();
        flow.Id = flow.Id == Guid.Empty ? Guid.NewGuid() : flow.Id;
        flow.Name = string.IsNullOrWhiteSpace(flow.Name)
            ? VisionAgentBuildSupport.FirstNonEmpty(load.Plan?.Goal, "Vision Agent workflow draft")
            : flow.Name;

        var existingNames = flow.Operators
            .Select(op => op.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var step in pipeline.Steps)
        {
            if (existingNames.Contains(step.TempId))
            {
                continue;
            }

            var op = BuildCanvasOperator(step, parameters, flow.Operators.Count);
            flow.Operators.Add(op);
            existingNames.Add(step.TempId);
        }

        if (flow.Connections.Count == 0)
        {
            AddCanvasConnections(flow);
        }

        return flow;
    }

    private static OperatorDto BuildCanvasOperator(
        VisionAgentOperatorPipelineStep step,
        ParameterMappingResolution parameters,
        int index)
    {
        var id = Guid.NewGuid();
        VisionAgentReadOnlyCatalog.Schemas.TryGetValue(step.OperatorType, out var schema);
        var inputPorts = schema?.InputPorts ?? Array.Empty<string>();
        var outputPorts = schema?.OutputPorts ?? Array.Empty<string>();
        return new OperatorDto
        {
            Id = id,
            Name = step.TempId,
            Type = ToOperatorType(step.OperatorType),
            X = 160 + index * 180,
            Y = 180,
            InputPorts = inputPorts.Select(name => new PortDto
            {
                Id = Guid.NewGuid(),
                Name = name,
                Direction = PortDirection.Input,
                DataType = PortDataType.Any,
                IsRequired = true
            }).ToList(),
            OutputPorts = outputPorts.Select(name => new PortDto
            {
                Id = Guid.NewGuid(),
                Name = name,
                Direction = PortDirection.Output,
                DataType = string.Equals(name, "Image", StringComparison.OrdinalIgnoreCase)
                    ? PortDataType.Image
                    : PortDataType.Any,
                IsRequired = false
            }).ToList(),
            Parameters = parameters.Mappings
                .Where(item => string.Equals(item.TempId, step.TempId, StringComparison.OrdinalIgnoreCase))
                .Select(item => new ParameterDto
                {
                    Id = Guid.NewGuid(),
                    Name = item.ParameterName,
                    DisplayName = item.ParameterName,
                    DataType = "string",
                    Value = item.ValueSummary,
                    DefaultValue = item.ValueSummary,
                    IsRequired = item.Pending
                })
                .ToList(),
            IsEnabled = true
        };
    }

    private static void AddCanvasConnections(OperatorFlowDto flow)
    {
        for (var index = 0; index < flow.Operators.Count - 1; index++)
        {
            var source = flow.Operators[index];
            var target = flow.Operators[index + 1];
            var sourcePort = source.OutputPorts.FirstOrDefault();
            var targetPort = target.InputPorts.FirstOrDefault();
            if (sourcePort == null || targetPort == null)
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

    private static GenerateFlowMode ToGenerateFlowMode(string buildIntent)
    {
        return buildIntent switch
        {
            "modify" or "refactor" => GenerateFlowMode.Modify,
            "explain" => GenerateFlowMode.Explain,
            "review_pending_parameters" => GenerateFlowMode.ReviewPendingParameters,
            _ => GenerateFlowMode.New
        };
    }

    private static OperatorType ToOperatorType(string operatorType)
    {
        if (Enum.TryParse<OperatorType>(operatorType, ignoreCase: true, out var parsed))
        {
            return parsed;
        }

        return operatorType switch
        {
            "MeasureDistance" => OperatorType.Measurement,
            "SemanticSegmentation" => OperatorType.DeepLearning,
            "ImageCompose" => OperatorType.ImageAdd,
            _ => OperatorType.DeepLearning
        };
    }
}
