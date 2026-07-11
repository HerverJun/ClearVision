using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;

namespace ClearVision.Product.Core.Decisions;

public sealed record FinalDecisionOutputCandidate(
    Guid OperatorId,
    string OperatorName,
    Guid OutputPortId,
    string OutputName,
    DecisionValueType DataType,
    DecisionInterpretationRule Rule);

public static class FinalDecisionConfigurationCatalog
{
    public static IReadOnlyList<FinalDecisionOutputCandidate> GetEligibleOutputs(OperatorFlow? flow)
    {
        if (flow == null)
        {
            return Array.Empty<FinalDecisionOutputCandidate>();
        }

        return flow.Operators
            .Where(op => op.IsEnabled)
            .SelectMany(op => op.OutputPorts.Select(port => CreateCandidate(op, port.DataType, port.Id, port.Name)))
            .Where(candidate => candidate != null)
            .Cast<FinalDecisionOutputCandidate>()
            .OrderBy(candidate => candidate.OperatorName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(candidate => candidate.OutputName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static FinalDecisionOutputCandidate? CreateCandidate(
        Operator source,
        PortDataType portType,
        Guid portId,
        string portName)
    {
        var mapping = portType switch
        {
            PortDataType.Boolean => (DecisionValueType.Boolean, DecisionInterpretationRule.Boolean),
            PortDataType.String => (DecisionValueType.String, DecisionInterpretationRule.StringMap),
            PortDataType.Integer => (DecisionValueType.Integer, DecisionInterpretationRule.NumericComparison),
            PortDataType.Float => (DecisionValueType.Float, DecisionInterpretationRule.NumericComparison),
            _ => ((DecisionValueType DataType, DecisionInterpretationRule Rule)?)null
        };

        return mapping.HasValue
            ? new FinalDecisionOutputCandidate(
                source.Id,
                source.Name,
                portId,
                portName,
                mapping.Value.DataType,
                mapping.Value.Rule)
            : null;
    }
}
