using ClearVision.Product.Core.Services;

namespace ClearVision.Product.Infrastructure.AI.Tools;

internal enum VisionAgentParameterRuleScope
{
    FlowValidation,
    DeploymentPrecheck
}

internal static class VisionAgentParameterRuleCenter
{
    public static IReadOnlyList<VisionAgentMissingResource> CollectMissingResources(
        VisionAgentFlowDraft flow,
        VisionAgentParameterRuleScope scope,
        IVisionAgentOperatorContractCatalog? contractCatalog = null)
    {
        contractCatalog ??= new VisionAgentOperatorContractCatalog();
        var missingResources = new List<VisionAgentMissingResource>();

        foreach (var issue in CollectConstraintViolations(flow, contractCatalog)
                     .Where(item =>
                         (item.Violation.Code is "required" or "at-least-one") &&
                         !string.IsNullOrWhiteSpace(item.Violation.ResourceKind)))
        {
            AddMissingResource(
                missingResources,
                issue.Violation.ResourceKind!,
                issue.Violation.ParameterNames[0],
                issue.TempId,
                issue.OperatorType,
                $"{issue.OperatorType}.{string.Join("/", issue.Violation.ParameterNames)} requires engineer-supplied {issue.Violation.ResourceKind} metadata.");
        }

        foreach (var op in flow.Operators)
        {
            AddLegacyDeepLearningResources(op, missingResources);
            AddTemplateMatchingResources(op, missingResources);
            AddPlcResources(op, missingResources);

            if (scope == VisionAgentParameterRuleScope.DeploymentPrecheck)
            {
                AddDeploymentOnlyResources(op, missingResources);
            }
        }

        return missingResources;
    }

    public static IReadOnlyList<VisionAgentParameterConstraintIssue> CollectConstraintViolations(
        VisionAgentFlowDraft flow,
        IVisionAgentOperatorContractCatalog? contractCatalog = null)
    {
        contractCatalog ??= new VisionAgentOperatorContractCatalog();
        var issues = new List<VisionAgentParameterConstraintIssue>();
        foreach (var op in flow.Operators)
        {
            if (!contractCatalog.TryGet(op.OperatorType, out var contract) ||
                contract.ParameterConstraints is not { Count: > 0 } constraints)
            {
                continue;
            }

            var values = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var pair in op.Parameters)
            {
                values[pair.Key] = pair.Value;
            }

            var metadata = new OperatorMetadata
            {
                Parameters = contract.Parameters.Select(parameter => new ParameterDefinition
                {
                    Name = parameter.Name,
                    IsRequired = parameter.IsRequired,
                    DefaultValue = parameter.DefaultValue
                }).ToList(),
                ParameterConstraints = constraints.ToList()
            };

            issues.AddRange(OperatorParameterConstraintEvaluator.Validate(metadata, values)
                .Select(violation => new VisionAgentParameterConstraintIssue(
                    op.TempId,
                    op.OperatorType,
                    violation)));
        }

        return issues
            .GroupBy(item =>
                $"{item.TempId}|{item.Violation.Code}|{string.Join('|', item.Violation.ParameterNames)}",
                StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();
    }

    private static void AddLegacyDeepLearningResources(
        VisionAgentFlowOperator op,
        List<VisionAgentMissingResource> missingResources)
    {
        if (IsOperatorType(op, "DeepLearning") ||
            (!IsOperatorType(op, "OnnxInference") &&
             !IsOperatorType(op, "SemanticSegmentation") &&
             !IsOperatorType(op, "AnomalyDetection")))
        {
            return;
        }

        AddMissingAtLeastOne(
            op,
            missingResources,
            "model_resource",
            ["ModelPath", "ModelId", "ModelCatalogPath"],
            $"{op.OperatorType}.ModelPath or ModelId is not configured.");
    }

    private static void AddTemplateMatchingResources(
        VisionAgentFlowOperator op,
        List<VisionAgentMissingResource> missingResources)
    {
        if (!IsOperatorType(op, "TemplateMatching"))
        {
            return;
        }

        AddMissingAtLeastOne(
            op,
            missingResources,
            "template_artifact",
            ["Template", "TemplateId", "TemplatePath"],
            "TemplateMatching.Template input or TemplateId is not configured.");
    }

    private static void AddPlcResources(
        VisionAgentFlowOperator op,
        List<VisionAgentMissingResource> missingResources)
    {
        if (!op.OperatorType.Contains("Plc", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        AddMissingAtLeastOne(
            op,
            missingResources,
            "plc_address",
            ["PlcAddress", "PLCParameters"],
            $"{op.OperatorType} PLC parameters are missing or pending.");
    }

    private static void AddDeploymentOnlyResources(
        VisionAgentFlowOperator op,
        List<VisionAgentMissingResource> missingResources)
    {
        _ = op;
        _ = missingResources;
    }

    private static void AddMissingAtLeastOne(
        VisionAgentFlowOperator op,
        List<VisionAgentMissingResource> missingResources,
        string resourceKind,
        IReadOnlyList<string> parameterNames,
        string message)
    {
        if (parameterNames.Any(name => !IsMissingParameter(op.Parameters, name)))
        {
            return;
        }

        AddMissingResource(
            missingResources,
            resourceKind,
            parameterNames[0],
            op.TempId,
            op.OperatorType,
            message);
    }

    private static void AddMissingResource(
        List<VisionAgentMissingResource> missingResources,
        string resourceKind,
        string parameterName,
        string tempId,
        string operatorType,
        string message)
    {
        if (missingResources.Any(resource =>
                string.Equals(resource.TempId, tempId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(resource.ParameterName, parameterName, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        missingResources.Add(new VisionAgentMissingResource(
            resourceKind,
            parameterName,
            tempId,
            operatorType,
            message));
    }

    private static bool IsOperatorType(VisionAgentFlowOperator op, string operatorType)
    {
        return string.Equals(op.OperatorType, operatorType, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsMissingParameter(
        IReadOnlyDictionary<string, string?> parameters,
        string parameterName)
    {
        return !parameters.TryGetValue(parameterName, out var value) ||
               OperatorParameterConstraintEvaluator.IsMissing(value);
    }
}

internal sealed record VisionAgentParameterConstraintIssue(
    string TempId,
    string OperatorType,
    OperatorParameterConstraintViolation Violation);
