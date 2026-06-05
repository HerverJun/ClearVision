using System.Text.Json;
using ClearVision.Product.Core.AI.Tools;

namespace ClearVision.Product.Infrastructure.AI.Tools;

public sealed class FlowValidationTool : VisionAgentToolBase
{
    public override string Name => "validate_flow";
    public override string DisplayName => "Validate flow";
    public override string Description => "Runs structure-only validation on a draft flow without executing vision logic.";
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
        return Task.FromResult(VisionAgentToolResult.Ok(FlowValidationPayload.Create(validation)));
    }
}

internal static class VisionAgentFlowDraftValidator
{
    public static VisionAgentFlowValidation Validate(VisionAgentFlowDraft flow)
    {
        var blockingIssues = new List<VisionAgentFlowIssue>();
        var warnings = new List<VisionAgentFlowIssue>();
        var missingResources = new List<VisionAgentMissingResource>();

        if (flow.Operators.Count == 0)
        {
            blockingIssues.Add(new VisionAgentFlowIssue(
                "missing_operators",
                "Flow must contain at least one operator."));
        }

        foreach (var op in flow.Operators.Where(op => string.IsNullOrWhiteSpace(op.TempId)))
        {
            blockingIssues.Add(new VisionAgentFlowIssue(
                "missing_temp_id",
                $"Operator '{op.OperatorType}' is missing tempId.",
                op.TempId,
                op.OperatorType));
        }

        var duplicateTempIds = flow.Operators
            .Where(op => !string.IsNullOrWhiteSpace(op.TempId))
            .GroupBy(op => op.TempId, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key);
        foreach (var tempId in duplicateTempIds)
        {
            blockingIssues.Add(new VisionAgentFlowIssue(
                "duplicate_temp_id",
                $"Operator tempId '{tempId}' appears more than once.",
                tempId));
        }

        var operatorsById = flow.Operators
            .Where(op => !string.IsNullOrWhiteSpace(op.TempId))
            .GroupBy(op => op.TempId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        foreach (var connection in flow.Connections)
        {
            ValidateConnectionEndpoint(
                operatorsById,
                connection.SourceTempId,
                connection.SourcePortName,
                isSource: true,
                blockingIssues);
            ValidateConnectionEndpoint(
                operatorsById,
                connection.TargetTempId,
                connection.TargetPortName,
                isSource: false,
                blockingIssues);
        }

        if (flow.Operators.Count > 1 && flow.Connections.Count == 0)
        {
            warnings.Add(new VisionAgentFlowIssue(
                "missing_connections",
                "Flow has multiple operators but no connections."));
        }

        var imageAcquisitionOperators = flow.Operators
            .Where(op => string.Equals(op.OperatorType, "ImageAcquisition", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (imageAcquisitionOperators.Count > 1 &&
            string.IsNullOrWhiteSpace(flow.EntryOperatorTempId))
        {
            blockingIssues.Add(new VisionAgentFlowIssue(
                "entry_operator_required",
                "Multiple ImageAcquisition operators require entryOperatorTempId."));
        }

        if (!string.IsNullOrWhiteSpace(flow.EntryOperatorTempId) &&
            !operatorsById.ContainsKey(flow.EntryOperatorTempId))
        {
            blockingIssues.Add(new VisionAgentFlowIssue(
                "entry_operator_not_found",
                $"entryOperatorTempId '{flow.EntryOperatorTempId}' does not match a draft operator.",
                flow.EntryOperatorTempId));
        }

        foreach (var op in flow.Operators)
        {
            if (!VisionAgentReadOnlyCatalog.Schemas.ContainsKey(op.OperatorType))
            {
                warnings.Add(new VisionAgentFlowIssue(
                    "unknown_operator_schema",
                    $"Operator '{op.OperatorType}' is not in the static schema catalog.",
                    op.TempId,
                    op.OperatorType));
            }

        }

        missingResources.AddRange(
            VisionAgentParameterRuleCenter.CollectMissingResources(
                flow,
                VisionAgentParameterRuleScope.FlowValidation));

        foreach (var missingResource in missingResources)
        {
            warnings.Add(new VisionAgentFlowIssue(
                "missing_resource",
                missingResource.Message,
                missingResource.TempId,
                missingResource.OperatorType));
        }

        return new VisionAgentFlowValidation(flow, blockingIssues, warnings, missingResources);
    }

    private static void ValidateConnectionEndpoint(
        IReadOnlyDictionary<string, VisionAgentFlowOperator> operatorsById,
        string tempId,
        string portName,
        bool isSource,
        List<VisionAgentFlowIssue> blockingIssues)
    {
        if (string.IsNullOrWhiteSpace(tempId) ||
            !operatorsById.TryGetValue(tempId, out var op))
        {
            blockingIssues.Add(new VisionAgentFlowIssue(
                "broken_connection_temp_id",
                $"{(isSource ? "Source" : "Target")} tempId '{tempId}' does not match a draft operator.",
                tempId));
            return;
        }

        if (string.IsNullOrWhiteSpace(portName) ||
            !VisionAgentReadOnlyCatalog.Schemas.TryGetValue(op.OperatorType, out var schema))
        {
            return;
        }

        var ports = isSource ? schema.OutputPorts : schema.InputPorts;
        if (!ports.Contains(portName, StringComparer.OrdinalIgnoreCase))
        {
            blockingIssues.Add(new VisionAgentFlowIssue(
                isSource ? "invalid_source_port" : "invalid_target_port",
                $"{op.OperatorType}.{portName} is not a known {(isSource ? "output" : "input")} port.",
                tempId,
                op.OperatorType));
        }
    }

}

internal sealed record VisionAgentFlowValidation(
    VisionAgentFlowDraft Flow,
    IReadOnlyList<VisionAgentFlowIssue> BlockingIssues,
    IReadOnlyList<VisionAgentFlowIssue> Warnings,
    IReadOnlyList<VisionAgentMissingResource> MissingResources);

internal sealed record VisionAgentFlowIssue(
    string Code,
    string Message,
    string? TempId = null,
    string? OperatorType = null);

internal sealed record VisionAgentMissingResource(
    string ResourceKind,
    string ParameterName,
    string TempId,
    string OperatorType,
    string Message);

internal static class FlowValidationPayload
{
    public static object Create(VisionAgentFlowValidation validation)
    {
        return new
        {
            source = "simulation_static_flow_validator",
            validationMode = "structure_only",
            isValid = validation.BlockingIssues.Count == 0,
            operatorCount = validation.Flow.Operators.Count,
            connectionCount = validation.Flow.Connections.Count,
            entryOperatorTempId = validation.Flow.EntryOperatorTempId,
            imageAcquisitionCount = validation.Flow.Operators.Count(op =>
                string.Equals(op.OperatorType, "ImageAcquisition", StringComparison.OrdinalIgnoreCase)),
            blockingIssues = validation.BlockingIssues.Select(IssuePayload).ToList(),
            warnings = validation.Warnings.Select(IssuePayload).ToList(),
            missingResources = validation.MissingResources.Select(ResourcePayload).ToList(),
            checkedRules = new[]
            {
                "operators",
                "connections",
                "unique_temp_id",
                "known_ports",
                "image_acquisition_entry",
                "required_resources"
            }
        };
    }

    public static object IssuePayload(VisionAgentFlowIssue issue)
    {
        return new
        {
            code = issue.Code,
            message = issue.Message,
            tempId = issue.TempId,
            operatorType = issue.OperatorType
        };
    }

    public static object ResourcePayload(VisionAgentMissingResource resource)
    {
        return new
        {
            resourceKind = resource.ResourceKind,
            parameterName = resource.ParameterName,
            tempId = resource.TempId,
            operatorType = resource.OperatorType,
            message = resource.Message
        };
    }
}
