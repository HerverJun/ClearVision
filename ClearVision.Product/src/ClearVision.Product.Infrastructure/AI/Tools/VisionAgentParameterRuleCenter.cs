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
        _ = scope;

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

            issues.AddRange(OperatorParameterConstraintEvaluator.Validate(
                    contract.Metadata,
                    values,
                    requireExplicitResourceConfiguration: true)
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

}

internal sealed record VisionAgentParameterConstraintIssue(
    string TempId,
    string OperatorType,
    OperatorParameterConstraintViolation Violation);
