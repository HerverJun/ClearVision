using System.Text.Json;
using ClearVision.Product.Core.AI.Tools;

namespace ClearVision.Product.Infrastructure.AI.Tools;

public sealed class DryRunFlowTool : VisionAgentToolBase
{
    public override string Name => "dryrun_flow";
    public override string DisplayName => "结构模拟运行";
    public override string Description => "仅进行结构级模拟，不读取图像、不访问硬件、不部署、不做运行时回放。";
    public override string Category => "simulation";
    public override VisionAgentToolPermission Permission => VisionAgentToolPermission.Simulation;
    public override JsonElement ParametersSchema { get; } = Schema("""
        {
          "type": "object",
          "properties": {
            "flow": { "type": ["object", "string"] },
            "flowJson": { "type": "string" },
            "entryOperatorTempId": { "type": "string" }
          }
        }
        """);

    public override Task<VisionAgentToolResult> ExecuteAsync(
        VisionAgentToolContext context,
        JsonElement arguments,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var normalized = VisionAgentFlowDraftNormalizer.Normalize(arguments, context);
        if (!normalized.Success)
        {
            return Task.FromResult(VisionAgentToolResult.Fail(
                normalized.ErrorCode ?? "invalid_flow",
                normalized.ErrorMessage ?? "Flow draft could not be normalized."));
        }

        var validation = VisionAgentFlowDraftValidator.Validate(normalized.Flow);
        var dryrun = BuildDryRun(validation);
        return Task.FromResult(VisionAgentToolResult.Ok(dryrun));
    }

    internal static object BuildDryRun(VisionAgentFlowValidation validation)
    {
        var executedOperators = new List<object>();
        var skippedOperators = new List<object>();
        if (validation.BlockingIssues.Count > 0)
        {
            skippedOperators.AddRange(validation.Flow.Operators.Select(op => new
            {
                tempId = op.TempId,
                operatorType = op.OperatorType,
                reason = "validation_blocked"
            }));
        }
        else
        {
            var executionOrder = BuildExecutionOrder(validation.Flow, out var skippedByOrder);
            executedOperators.AddRange(executionOrder.Select(op => new
            {
                tempId = op.TempId,
                operatorType = op.OperatorType,
                status = SimulatedStatus(op.OperatorType),
                produced = SimulatedProducedValue(op.OperatorType)
            }));
            skippedOperators.AddRange(skippedByOrder.Select(op => new
            {
                tempId = op.TempId,
                operatorType = op.OperatorType,
                reason = "not_reachable_or_cycle"
            }));
        }

        var warnings = validation.Warnings
            .Select(FlowValidationPayload.IssuePayload)
            .ToList();
        warnings.Add(new
        {
            code = "stub_dryrun_only",
            message = "Dryrun uses structural simulation only and does not execute vision operators.",
            tempId = (string?)null,
            operatorType = (string?)null
        });

        return new
        {
            source = "simulation_static_flow_dryrun",
            dryRunMode = "structure_only_stub",
            dryRunSucceeded = validation.BlockingIssues.Count == 0,
            executedOperators,
            skippedOperators,
            warnings,
            blockingIssues = validation.BlockingIssues.Select(FlowValidationPayload.IssuePayload).ToList(),
            missingResources = validation.MissingResources.Select(FlowValidationPayload.ResourcePayload).ToList(),
            dryRunSummary = new
            {
                summary = validation.BlockingIssues.Count == 0
                    ? $"Simulated {executedOperators.Count} operators without real resources."
                    : "Dryrun stopped because validation produced blocking issues.",
                executedCount = executedOperators.Count,
                skippedCount = skippedOperators.Count,
                generatedRealImages = false,
                loadedModelFiles = false,
                accessedHardware = false,
                deployed = false
            }
        };
    }

    private static IReadOnlyList<VisionAgentFlowOperator> BuildExecutionOrder(
        VisionAgentFlowDraft flow,
        out IReadOnlyList<VisionAgentFlowOperator> skippedOperators)
    {
        var operators = flow.Operators
            .Where(op => !string.IsNullOrWhiteSpace(op.TempId))
            .GroupBy(op => op.TempId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
        var operatorsById = operators.ToDictionary(op => op.TempId, StringComparer.OrdinalIgnoreCase);
        var connections = flow.Connections
            .Where(connection =>
                operatorsById.ContainsKey(connection.SourceTempId) &&
                operatorsById.ContainsKey(connection.TargetTempId))
            .ToList();
        var outgoing = operators.ToDictionary(
            op => op.TempId,
            _ => new List<string>(),
            StringComparer.OrdinalIgnoreCase);
        var indegree = operators.ToDictionary(
            op => op.TempId,
            _ => 0,
            StringComparer.OrdinalIgnoreCase);

        foreach (var connection in connections)
        {
            outgoing[connection.SourceTempId].Add(connection.TargetTempId);
            indegree[connection.TargetTempId]++;
        }

        var ready = new Queue<string>(InitialReadyOperators(flow, operators, indegree));
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ordered = new List<VisionAgentFlowOperator>();

        while (ready.Count > 0)
        {
            var tempId = ready.Dequeue();
            if (!visited.Add(tempId))
            {
                continue;
            }

            ordered.Add(operatorsById[tempId]);
            foreach (var targetTempId in outgoing[tempId])
            {
                indegree[targetTempId]--;
                if (indegree[targetTempId] == 0)
                {
                    ready.Enqueue(targetTempId);
                }
            }
        }

        skippedOperators = operators
            .Where(op => !visited.Contains(op.TempId))
            .ToList();
        return ordered;
    }

    private static IEnumerable<string> InitialReadyOperators(
        VisionAgentFlowDraft flow,
        IReadOnlyList<VisionAgentFlowOperator> operators,
        IReadOnlyDictionary<string, int> indegree)
    {
        if (!string.IsNullOrWhiteSpace(flow.EntryOperatorTempId) &&
            indegree.ContainsKey(flow.EntryOperatorTempId))
        {
            yield return flow.EntryOperatorTempId;
            yield break;
        }

        foreach (var op in operators.Where(op => indegree[op.TempId] == 0))
        {
            yield return op.TempId;
        }
    }

    private static string SimulatedStatus(string operatorType)
    {
        return operatorType switch
        {
            "ImageAcquisition" => "simulated_stub_camera_input",
            "DeepLearning" => "simulated_stub_model_inference",
            "TemplateMatching" => "simulated_stub_template_match",
            _ => "simulated"
        };
    }

    private static string SimulatedProducedValue(string operatorType)
    {
        return operatorType switch
        {
            "ImageAcquisition" => "stub_image_token",
            "DeepLearning" => "stub_detection_result",
            "TemplateMatching" => "stub_match_result",
            "CircleMeasurement" => "stub_circle_measurement",
            "Measurement" => "stub_distance_measurement",
            "UnitConvert" => "stub_unit_conversion",
            "DetectionSequenceJudge" => "stub_sequence_judgment",
            "ResultJudgment" => "stub_judgment",
            "ResultOutput" => "stub_output_payload",
            _ => "stub_operator_output"
        };
    }
}
