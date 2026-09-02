using System.Text.Json;
using ClearVision.Product.Core.AI.Tools;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Services;
using ClearVision.Product.Infrastructure.AI.Agent;

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
    public override string DisplayName => "流程校验";
    public override string Description => "仅校验流程草稿结构，不执行视觉逻辑。";
    public override string Category => "simulation";
    public override VisionAgentToolPermission Permission => VisionAgentToolPermission.Simulation;
    public override JsonElement ParametersSchema { get; } = Schema("""
        {
          "type": "object",
          "properties": {
            "flow": { "type": ["object", "string"] },
            "flowJson": { "type": "string" },
            "entryOperatorTempId": { "type": "string" },
            "planHash": { "type": "string" },
            "catalogVersion": { "type": "string" },
            "buildIntent": { "type": "string" },
            "artifactFingerprint": { "type": "string" }
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

        var observation = WorkflowArtifactFingerprint.Observe(arguments, normalized.Flow, _contractCatalog);
        var validation = VisionAgentFlowDraftValidator.Validate(normalized.Flow, _contractCatalog);
        if (!observation.IsConsistent)
        {
            validation = validation with
            {
                BlockingIssues = validation.BlockingIssues
                    .Concat([new VisionAgentFlowIssue(
                        "artifact_fingerprint_mismatch",
                        "The normalized flow does not match the compiled artifact fingerprint.")])
                    .ToList()
            };
        }

        return Task.FromResult(VisionAgentToolResult.Ok(
            FlowValidationPayload.Create(validation, observation)));
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
        var canonicalFlow = Canonicalize(flow, contractCatalog, warnings);

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
                     VisionAgentParameterRuleScope.FlowValidation,
                     contractCatalog))
        {
            AddMissingResource(missingResources, resource);
        }

        foreach (var issue in VisionAgentParameterRuleCenter.CollectConstraintViolations(
                     canonicalFlow,
                     contractCatalog))
        {
            var violation = issue.Violation;
            if (IsDeferredImageSourceChoice(canonicalFlow, issue))
            {
                AddMissingResource(
                    missingResources,
                    new VisionAgentMissingResource(
                        "image_source",
                        "SourceType",
                        issue.TempId,
                        issue.OperatorType,
                        "ImageAcquisition.SourceType is intentionally deferred in this editable draft."));
                continue;
            }

            if ((violation.Code is "required" or "at-least-one") &&
                !string.IsNullOrWhiteSpace(violation.ResourceKind))
            {
                continue;
            }

            var parameterNames = string.Join(", ", violation.ParameterNames);
            var code = violation.Code switch
            {
                "at-least-one" => "missing_conditional_parameter_group",
                "mutually-exclusive" => "mutually_exclusive_parameters",
                _ => "missing_conditional_parameter"
            };
            var message = violation.Code switch
            {
                "at-least-one" => $"{issue.OperatorType} requires at least one of {parameterNames}.",
                "mutually-exclusive" => $"{issue.OperatorType} parameters {parameterNames} cannot be configured together.",
                _ => $"{issue.OperatorType}.{parameterNames} is required for the active parameter mode."
            };
            blockingIssues.Add(new VisionAgentFlowIssue(
                code,
                message,
                issue.TempId,
                issue.OperatorType));
            if (violation.Code is "required" or "at-least-one")
            {
                pendingParameters.Add(new VisionAgentPendingParameter(
                    issue.TempId,
                    issue.OperatorType,
                    violation.ParameterNames[0],
                    code));
            }
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
        IVisionAgentOperatorContractCatalog contractCatalog,
        List<VisionAgentFlowIssue> warnings)
    {
        return flow with
        {
            Operators = flow.Operators
                .Select(op =>
                {
                    var operatorType = contractCatalog.CanonicalizeOperatorType(op.OperatorType);
                    var parameters = contractCatalog.TryGet(operatorType, out var contract)
                        ? CanonicalizeParameters(op, operatorType, contract, warnings)
                        : CopyParameters(op.Parameters);
                    return op with
                    {
                        OperatorType = operatorType,
                        Parameters = parameters
                    };
                })
                .ToList()
        };
    }

    private static IReadOnlyDictionary<string, string?> CanonicalizeParameters(
        VisionAgentFlowOperator op,
        string operatorType,
        VisionAgentOperatorContract contract,
        List<VisionAgentFlowIssue> warnings)
    {
        var metadata = new OperatorMetadata
        {
            Parameters = contract.Parameters.Select(parameter => new ParameterDefinition
            {
                Name = parameter.Name,
                IsRequired = parameter.IsRequired,
                DefaultValue = parameter.DefaultValue
            }).ToList(),
            ParameterConstraints = contract.ParameterConstraints?.ToList() ?? []
        };
        var values = op.Parameters.ToDictionary(
            pair => pair.Key,
            pair => (object?)pair.Value,
            StringComparer.Ordinal);
        var canonicalization = OperatorParameterConstraintEvaluator.Canonicalize(metadata, values);

        foreach (var diagnostic in canonicalization.Diagnostics)
        {
            warnings.Add(new VisionAgentFlowIssue(
                "parameter_alias_conflict",
                $"{operatorType}.{diagnostic.Message}",
                op.TempId,
                operatorType));
        }

        return canonicalization.ExplicitValues.ToDictionary(
            pair => pair.Key,
            pair => pair.Value?.ToString(),
            StringComparer.OrdinalIgnoreCase);
    }

    private static IReadOnlyDictionary<string, string?> CopyParameters(
        IReadOnlyDictionary<string, string?> parameters)
    {
        return new Dictionary<string, string?>(parameters, StringComparer.OrdinalIgnoreCase);
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
            if (!knownParameters.ContainsKey(parameterName) &&
                !IsRecognizedMetadataParameter(op.OperatorType, parameterName, contract))
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

            if (IsRuleCenterManagedMetadata(op.OperatorType, parameter.Name, contract))
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

            if (IsRuleCenterManagedMetadata(op.OperatorType, input.Name, contract))
            {
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
        return OperatorParameterValueSemantics.IsMissing(value);
    }

    private static bool IsDeferredImageSourceChoice(
        VisionAgentFlowDraft flow,
        VisionAgentParameterConstraintIssue issue)
    {
        if (!issue.OperatorType.Equals("ImageAcquisition", StringComparison.OrdinalIgnoreCase) ||
            !issue.Violation.Code.Equals("required", StringComparison.OrdinalIgnoreCase) ||
            issue.Violation.ParameterNames.Count != 1 ||
            !issue.Violation.ParameterNames[0].Equals("SourceType", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var op = flow.Operators.FirstOrDefault(candidate =>
            candidate.TempId.Equals(issue.TempId, StringComparison.OrdinalIgnoreCase));
        return op?.Parameters.TryGetValue("SourceType", out var value) == true &&
               string.Equals(value, "<pending-image-source>", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsRecognizedMetadataParameter(
        string operatorType,
        string parameterName,
        VisionAgentOperatorContract contract)
    {
        if (contract.ParameterConstraints?.Any(constraint =>
                constraint.Parameter.Equals(parameterName, StringComparison.OrdinalIgnoreCase)) == true)
        {
            return true;
        }

        var resourceKind = VisionAgentResourceClassifier.Classify(operatorType, parameterName);
        if (string.IsNullOrWhiteSpace(resourceKind))
        {
            return false;
        }

        return contract.Parameters.Any(parameter =>
                   HasResourceKind(operatorType, parameter.Name, parameter.DataType, resourceKind)) ||
               contract.InputPorts.Any(input =>
                   HasResourceKind(operatorType, input.Name, input.DataType.ToString(), resourceKind));
    }

    private static bool IsRuleCenterManagedMetadata(
        string operatorType,
        string parameterName,
        VisionAgentOperatorContract contract)
    {
        return contract.ParameterConstraints?.Any(constraint =>
                   constraint.Parameter.Equals(parameterName, StringComparison.OrdinalIgnoreCase) &&
                   (!string.IsNullOrWhiteSpace(constraint.ResourceKind) ||
                    !string.IsNullOrWhiteSpace(constraint.AtLeastOneGroup) ||
                    !string.IsNullOrWhiteSpace(constraint.AliasFor))) == true;
    }

    private static bool HasResourceKind(
        string operatorType,
        string name,
        string? dataType,
        string expectedResourceKind)
    {
        return string.Equals(
            VisionAgentResourceClassifier.Classify(operatorType, name, dataType),
            expectedResourceKind,
            StringComparison.OrdinalIgnoreCase);
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
            "image_source" => "missing_image_source",
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
    public static object Create(
        VisionAgentFlowValidation validation,
        ArtifactFingerprintObservation? fingerprint = null)
    {
        return new
        {
            source = "real_operator_contract_validator",
            validationMode = "metadata_only_contract",
            isValid = validation.BlockingIssues.Count == 0,
            operatorCount = validation.Flow.Operators.Count,
            connectionCount = validation.Flow.Connections.Count,
            entryOperatorTempId = validation.Flow.EntryOperatorTempId,
            artifactFingerprint = fingerprint?.ComputedFingerprint ?? string.Empty,
            validationFingerprint = fingerprint?.ComputedFingerprint ?? string.Empty,
            compiledFingerprint = fingerprint?.ExpectedFingerprint ?? string.Empty,
            fingerprintConsistent = fingerprint?.IsConsistent ?? false,
            imageAcquisitionCount = validation.Flow.Operators.Count(op =>
                string.Equals(op.OperatorType, "ImageAcquisition", StringComparison.OrdinalIgnoreCase)),
            blockingIssues = validation.BlockingIssues.Select(IssuePayload).ToList(),
            warnings = validation.Warnings.Select(IssuePayload).ToList(),
            missingResources = validation.MissingResources.Select(ResourcePayload).ToList(),
            pendingParameters = validation.PendingParameters.Select(PendingPayload).ToList(),
            canonicalFlow = FlowPayload(validation.Flow),
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
                "required_resources",
                "artifact_fingerprint"
            }
        };
    }

    private static object FlowPayload(VisionAgentFlowDraft flow)
    {
        return new
        {
            operators = flow.Operators.Select(op => new
            {
                tempId = op.TempId,
                operatorType = op.OperatorType,
                parameters = op.Parameters.ToDictionary(
                    pair => pair.Key,
                    pair => pair.Value)
            }).ToList(),
            connections = flow.Connections.Select(connection => new
            {
                sourceTempId = connection.SourceTempId,
                sourcePortName = connection.SourcePortName,
                targetTempId = connection.TargetTempId,
                targetPortName = connection.TargetPortName
            }).ToList(),
            entryOperatorTempId = flow.EntryOperatorTempId
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
