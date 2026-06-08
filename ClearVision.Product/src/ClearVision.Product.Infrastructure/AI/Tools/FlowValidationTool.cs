using System.Text.Json;
using ClearVision.Product.Core.AI.Tools;
using ClearVision.Product.Core.Enums;

namespace ClearVision.Product.Infrastructure.AI.Tools;

public sealed class FlowValidationTool : VisionAgentToolBase
{
    private readonly IVisionAgentOperatorContractCatalog _contractCatalog;

    public FlowValidationTool()
        : this(new VisionAgentOperatorContractCatalog())
    {
    }

    internal FlowValidationTool(IVisionAgentOperatorContractCatalog contractCatalog)
    {
        _contractCatalog = contractCatalog;
    }

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

        var validation = VisionAgentFlowDraftValidator.Validate(normalized.Flow, _contractCatalog);
        return Task.FromResult(VisionAgentToolResult.Ok(FlowValidationPayload.Create(validation)));
    }
}

internal static class VisionAgentFlowDraftValidator
{
    public static VisionAgentFlowValidation Validate(VisionAgentFlowDraft flow)
    {
        return Validate(flow, new VisionAgentOperatorContractCatalog());
    }

    public static VisionAgentFlowValidation Validate(
        VisionAgentFlowDraft flow,
        IVisionAgentOperatorContractCatalog contractCatalog)
    {
        var blockingIssues = new List<VisionAgentFlowIssue>();
        var warnings = new List<VisionAgentFlowIssue>();
        var missingResources = new List<VisionAgentMissingResource>();
        var pendingParameters = new List<VisionAgentPendingParameter>();
        var canonicalFlow = Canonicalize(flow, contractCatalog);

        if (canonicalFlow.Operators.Count == 0)
        {
            blockingIssues.Add(new VisionAgentFlowIssue(
                "missing_operators",
                "Flow must contain at least one operator."));
        }

        foreach (var op in canonicalFlow.Operators.Where(op => string.IsNullOrWhiteSpace(op.TempId)))
        {
            blockingIssues.Add(new VisionAgentFlowIssue(
                "missing_temp_id",
                $"Operator '{op.OperatorType}' is missing tempId.",
                op.TempId,
                op.OperatorType));
        }

        var duplicateTempIds = canonicalFlow.Operators
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

        var operatorsById = canonicalFlow.Operators
            .Where(op => !string.IsNullOrWhiteSpace(op.TempId))
            .GroupBy(op => op.TempId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        foreach (var connection in canonicalFlow.Connections)
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

        if (canonicalFlow.Operators.Count > 1 && canonicalFlow.Connections.Count == 0)
        {
            warnings.Add(new VisionAgentFlowIssue(
                "missing_connections",
                "Flow has multiple operators but no connections."));
        }

        var imageAcquisitionOperators = canonicalFlow.Operators
            .Where(op => string.Equals(op.OperatorType, "ImageAcquisition", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (imageAcquisitionOperators.Count > 1 &&
            string.IsNullOrWhiteSpace(canonicalFlow.EntryOperatorTempId))
        {
            blockingIssues.Add(new VisionAgentFlowIssue(
                "entry_operator_required",
                "Multiple ImageAcquisition operators require entryOperatorTempId."));
        }

        if (!string.IsNullOrWhiteSpace(canonicalFlow.EntryOperatorTempId) &&
            !operatorsById.ContainsKey(canonicalFlow.EntryOperatorTempId))
        {
            blockingIssues.Add(new VisionAgentFlowIssue(
                "entry_operator_not_found",
                $"entryOperatorTempId '{canonicalFlow.EntryOperatorTempId}' does not match a draft operator.",
                canonicalFlow.EntryOperatorTempId));
        }

        foreach (var op in canonicalFlow.Operators)
        {
            if (!contractCatalog.TryGet(op.OperatorType, out var contract))
            {
                blockingIssues.Add(new VisionAgentFlowIssue(
                    "unknown_operator",
                    $"Operator '{op.OperatorType}' is not in the ClearVision operator catalog.",
                    op.TempId,
                    op.OperatorType));
                continue;
            }

            ValidateOperatorParameters(op, contract, blockingIssues, warnings, missingResources, pendingParameters);
            ValidateRequiredInputBindings(op, contract, canonicalFlow.Connections, blockingIssues, missingResources, pendingParameters);
        }

        ValidateConnectionsAgainstContracts(canonicalFlow, contractCatalog, blockingIssues);

        foreach (var resource in VisionAgentParameterRuleCenter.CollectMissingResources(
                     canonicalFlow,
                     VisionAgentParameterRuleScope.FlowValidation))
        {
            AddMissingResource(missingResources, resource);
        }

        foreach (var missingResource in missingResources)
        {
            warnings.Add(new VisionAgentFlowIssue(
                MissingResourceIssueCode(missingResource.ResourceKind),
                missingResource.Message,
                missingResource.TempId,
                missingResource.OperatorType));
            pendingParameters.Add(new VisionAgentPendingParameter(
                missingResource.TempId,
                missingResource.OperatorType,
                missingResource.ParameterName,
                MissingResourceIssueCode(missingResource.ResourceKind)));
        }

        return new VisionAgentFlowValidation(
            canonicalFlow,
            blockingIssues,
            warnings,
            DeduplicateMissing(missingResources),
            DeduplicatePending(pendingParameters));
    }

    private static VisionAgentFlowDraft Canonicalize(
        VisionAgentFlowDraft flow,
        IVisionAgentOperatorContractCatalog contractCatalog)
    {
        return flow with
        {
            Operators = flow.Operators
                .Select(op => op with { OperatorType = contractCatalog.CanonicalizeOperatorType(op.OperatorType) })
                .ToList()
        };
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
                "invalid_connection",
                $"{(isSource ? "Source" : "Target")} tempId '{tempId}' does not match a draft operator.",
                tempId));
            return;
        }

        if (string.IsNullOrWhiteSpace(portName))
        {
            blockingIssues.Add(new VisionAgentFlowIssue(
                "missing_port",
                $"{(isSource ? "Source" : "Target")} port name is missing.",
                tempId,
                op.OperatorType));
        }
    }

    private static void ValidateConnectionsAgainstContracts(
        VisionAgentFlowDraft flow,
        IVisionAgentOperatorContractCatalog contractCatalog,
        List<VisionAgentFlowIssue> blockingIssues)
    {
        var operatorsById = flow.Operators
            .Where(op => !string.IsNullOrWhiteSpace(op.TempId))
            .GroupBy(op => op.TempId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        foreach (var connection in flow.Connections)
        {
            if (!operatorsById.TryGetValue(connection.SourceTempId, out var source) ||
                !operatorsById.TryGetValue(connection.TargetTempId, out var target))
            {
                continue;
            }

            if (!contractCatalog.TryGet(source.OperatorType, out var sourceContract) ||
                !contractCatalog.TryGet(target.OperatorType, out var targetContract))
            {
                continue;
            }

            var sourcePort = sourceContract.OutputPorts.FirstOrDefault(port =>
                string.Equals(port.Name, connection.SourcePortName, StringComparison.OrdinalIgnoreCase));
            var targetPort = targetContract.InputPorts.FirstOrDefault(port =>
                string.Equals(port.Name, connection.TargetPortName, StringComparison.OrdinalIgnoreCase));

            if (sourcePort == null)
            {
                blockingIssues.Add(new VisionAgentFlowIssue(
                    "missing_port",
                    $"{source.OperatorType}.{connection.SourcePortName} is not a known output port.",
                    source.TempId,
                    source.OperatorType));
                continue;
            }

            if (targetPort == null)
            {
                blockingIssues.Add(new VisionAgentFlowIssue(
                    "missing_port",
                    $"{target.OperatorType}.{connection.TargetPortName} is not a known input port.",
                    target.TempId,
                    target.OperatorType));
                continue;
            }

            if (!PortDataTypeCompatibility.AreCompatible(sourcePort.DataType, targetPort.DataType))
            {
                blockingIssues.Add(new VisionAgentFlowIssue(
                    "incompatible_port_type",
                    $"{source.OperatorType}.{sourcePort.Name} ({sourcePort.DataType}) cannot connect to {target.OperatorType}.{targetPort.Name} ({targetPort.DataType}).",
                    source.TempId,
                    source.OperatorType));
            }
        }
    }

    private static void ValidateOperatorParameters(
        VisionAgentFlowOperator op,
        VisionAgentOperatorContract contract,
        List<VisionAgentFlowIssue> blockingIssues,
        List<VisionAgentFlowIssue> warnings,
        List<VisionAgentMissingResource> missingResources,
        List<VisionAgentPendingParameter> pendingParameters)
    {
        var knownParameters = contract.Parameters
            .ToDictionary(parameter => parameter.Name, StringComparer.OrdinalIgnoreCase);

        foreach (var parameterName in op.Parameters.Keys)
        {
            if (!knownParameters.ContainsKey(parameterName))
            {
                blockingIssues.Add(new VisionAgentFlowIssue(
                    "unknown_parameter",
                    $"{op.OperatorType}.{parameterName} is not a known parameter.",
                    op.TempId,
                    op.OperatorType));
            }
        }

        foreach (var parameter in contract.Parameters)
        {
            op.Parameters.TryGetValue(parameter.Name, out var value);
            if (!IsRequiredPending(op.OperatorType, parameter, value))
            {
                continue;
            }

            warnings.Add(new VisionAgentFlowIssue(
                "missing_required_parameter",
                $"{op.OperatorType}.{parameter.Name} requires engineer-supplied metadata before deployment.",
                op.TempId,
                op.OperatorType));
            pendingParameters.Add(new VisionAgentPendingParameter(
                op.TempId,
                op.OperatorType,
                parameter.Name,
                "missing_required_parameter"));

            var resourceKind = VisionAgentResourceClassifier.Classify(op.OperatorType, parameter.Name, parameter.DataType);
            if (!string.IsNullOrWhiteSpace(resourceKind))
            {
                AddMissingResource(
                    missingResources,
                    new VisionAgentMissingResource(
                        resourceKind,
                        parameter.Name,
                        op.TempId,
                        op.OperatorType,
                        $"{op.OperatorType}.{parameter.Name} is pending metadata and was not guessed."));
            }
        }
    }

    private static void ValidateRequiredInputBindings(
        VisionAgentFlowOperator op,
        VisionAgentOperatorContract contract,
        IReadOnlyList<VisionAgentFlowConnection> connections,
        List<VisionAgentFlowIssue> blockingIssues,
        List<VisionAgentMissingResource> missingResources,
        List<VisionAgentPendingParameter> pendingParameters)
    {
        foreach (var input in contract.InputPorts.Where(port => port.IsRequired))
        {
            var hasConnection = connections.Any(connection =>
                string.Equals(connection.TargetTempId, op.TempId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(connection.TargetPortName, input.Name, StringComparison.OrdinalIgnoreCase));
            if (hasConnection)
            {
                continue;
            }

            var resourceKind = VisionAgentResourceClassifier.Classify(op.OperatorType, input.Name, input.DataType.ToString());
            if (string.IsNullOrWhiteSpace(resourceKind))
            {
                blockingIssues.Add(new VisionAgentFlowIssue(
                    "missing_required_input",
                    $"{op.OperatorType}.{input.Name} is required but is not connected.",
                    op.TempId,
                    op.OperatorType));
                continue;
            }

            AddMissingResource(
                missingResources,
                new VisionAgentMissingResource(
                    resourceKind,
                    input.Name,
                    op.TempId,
                    op.OperatorType,
                    $"{op.OperatorType}.{input.Name} input is not bound in the metadata-only draft."));
            pendingParameters.Add(new VisionAgentPendingParameter(
                op.TempId,
                op.OperatorType,
                input.Name,
                "missing_required_input"));
        }
    }

    private static bool IsRequiredPending(
        string operatorType,
        VisionAgentParameterContract parameter,
        string? value)
    {
        if (!parameter.IsRequired)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(value) && LooksPending(value))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (!IsEmptyDefault(parameter.DefaultValue))
        {
            return false;
        }

        var resourceKind = VisionAgentResourceClassifier.Classify(operatorType, parameter.Name, parameter.DataType);
        return IsDeploymentCriticalResourceParameter(operatorType, parameter.Name, resourceKind);
    }

    private static bool IsDeploymentCriticalResourceParameter(
        string operatorType,
        string parameterName,
        string resourceKind)
    {
        return resourceKind switch
        {
            "camera_binding" => parameterName.Equals("CameraId", StringComparison.OrdinalIgnoreCase) ||
                                parameterName.Equals("CameraBindingId", StringComparison.OrdinalIgnoreCase),
            "model_resource" => parameterName.Equals("ModelPath", StringComparison.OrdinalIgnoreCase) ||
                                (!operatorType.Equals("DeepLearning", StringComparison.OrdinalIgnoreCase) &&
                                 parameterName.Equals("ModelId", StringComparison.OrdinalIgnoreCase)),
            "template_artifact" => parameterName.Equals("TemplateId", StringComparison.OrdinalIgnoreCase),
            "measurement_parameter" => operatorType.Equals("UnitConvert", StringComparison.OrdinalIgnoreCase) &&
                                       parameterName.Equals("Scale", StringComparison.OrdinalIgnoreCase),
            "plc_address" => parameterName.Contains("Address", StringComparison.OrdinalIgnoreCase) ||
                             parameterName.Contains("PLC", StringComparison.OrdinalIgnoreCase),
            "output_channel" => parameterName.Contains("Channel", StringComparison.OrdinalIgnoreCase),
            _ => false
        };
    }

    private static bool IsEmptyDefault(object? value)
    {
        return value == null || string.IsNullOrWhiteSpace(value.ToString());
    }

    private static bool LooksPending(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        return value.StartsWith("<pending", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("todo", StringComparison.OrdinalIgnoreCase);
    }

    private static void AddMissingResource(
        List<VisionAgentMissingResource> missingResources,
        VisionAgentMissingResource resource)
    {
        if (missingResources.Any(item =>
                string.Equals(item.TempId, resource.TempId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(item.ParameterName, resource.ParameterName, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(item.ResourceKind, resource.ResourceKind, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        missingResources.Add(resource);
    }

    private static IReadOnlyList<VisionAgentMissingResource> DeduplicateMissing(
        IEnumerable<VisionAgentMissingResource> resources)
    {
        return resources
            .GroupBy(item => $"{item.ResourceKind}|{item.TempId}|{item.ParameterName}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
    }

    private static IReadOnlyList<VisionAgentPendingParameter> DeduplicatePending(
        IEnumerable<VisionAgentPendingParameter> pending)
    {
        return pending
            .GroupBy(item => $"{item.TempId}|{item.ParameterName}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
    }

    private static string MissingResourceIssueCode(string resourceKind)
    {
        return resourceKind switch
        {
            "model_resource" => "missing_model_resource",
            "template_artifact" => "missing_template_resource",
            "measurement_parameter" => "missing_calibration_parameter",
            _ => "missing_resource"
        };
    }
}

internal sealed record VisionAgentFlowValidation(
    VisionAgentFlowDraft Flow,
    IReadOnlyList<VisionAgentFlowIssue> BlockingIssues,
    IReadOnlyList<VisionAgentFlowIssue> Warnings,
    IReadOnlyList<VisionAgentMissingResource> MissingResources,
    IReadOnlyList<VisionAgentPendingParameter> PendingParameters);

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

internal sealed record VisionAgentPendingParameter(
    string TempId,
    string OperatorType,
    string ParameterName,
    string ReasonCode);

internal static class FlowValidationPayload
{
    public static object Create(VisionAgentFlowValidation validation)
    {
        return new
        {
            source = "real_operator_contract_validator",
            validationMode = "metadata_only_contract",
            isValid = validation.BlockingIssues.Count == 0,
            operatorCount = validation.Flow.Operators.Count,
            connectionCount = validation.Flow.Connections.Count,
            entryOperatorTempId = validation.Flow.EntryOperatorTempId,
            imageAcquisitionCount = validation.Flow.Operators.Count(op =>
                string.Equals(op.OperatorType, "ImageAcquisition", StringComparison.OrdinalIgnoreCase)),
            blockingIssues = validation.BlockingIssues.Select(IssuePayload).ToList(),
            warnings = validation.Warnings.Select(IssuePayload).ToList(),
            missingResources = validation.MissingResources.Select(ResourcePayload).ToList(),
            pendingParameters = validation.PendingParameters.Select(PendingPayload).ToList(),
            checkedRules = new[]
            {
                "operators",
                "operator_contract",
                "known_parameters",
                "connections",
                "unique_temp_id",
                "known_ports",
                "port_type_compatibility",
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
            resourceType = resource.ResourceKind,
            parameterName = resource.ParameterName,
            tempId = resource.TempId,
            operatorType = resource.OperatorType,
            message = resource.Message
        };
    }

    private static object PendingPayload(VisionAgentPendingParameter pending)
    {
        return new
        {
            tempId = pending.TempId,
            operatorType = pending.OperatorType,
            parameterName = pending.ParameterName,
            reasonCode = pending.ReasonCode
        };
    }
}
