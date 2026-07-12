using ClearVision.Product.Core.Decisions;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;

namespace ClearVision.Product.Tests.TestSupport;

internal static class DecisionBindingTestSupport
{
    public static OperatorFlow BindStringDecision(
        this OperatorFlow flow,
        Operator sourceOperator,
        string outputName = "JudgmentResult",
        string okValue = "OK",
        string ngValue = "NG")
    {
        if (!FinalDecisionConfigurationCatalog.IsEligibleContract(sourceOperator.Type, outputName))
        {
            sourceOperator = EnsureJudgmentOperator(flow);
            outputName = "JudgmentResult";
        }

        var port = sourceOperator.OutputPorts.FirstOrDefault(candidate =>
            candidate.Name.Equals(outputName, StringComparison.OrdinalIgnoreCase));
        if (port == null)
        {
            sourceOperator.AddOutputPort(outputName, PortDataType.String);
            port = sourceOperator.OutputPorts.Last();
        }

        flow.DecisionConfiguration = new DecisionConfiguration
        {
            FinalDecisionBinding = new FinalDecisionBinding
            {
                SourceOperatorId = sourceOperator.Id,
                SourceOutputPortId = port.Id,
                SourceOutputName = port.Name,
                DataType = DecisionValueType.String,
                Rule = DecisionInterpretationRule.StringMap,
                OkValue = okValue,
                NgValue = ngValue
            },
            MissingDecisionPolicy = MissingDecisionPolicy.Undetermined
        };
        return flow;
    }

    public static OperatorFlow BindBooleanDecision(
        this OperatorFlow flow,
        Operator sourceOperator,
        string outputName = "IsOk",
        bool trueMeansOk = true)
    {
        if (!FinalDecisionConfigurationCatalog.IsEligibleContract(sourceOperator.Type, outputName))
        {
            sourceOperator = EnsureJudgmentOperator(flow);
            outputName = "IsOk";
        }

        var port = sourceOperator.OutputPorts.FirstOrDefault(candidate =>
            candidate.Name.Equals(outputName, StringComparison.OrdinalIgnoreCase));
        if (port == null)
        {
            sourceOperator.AddOutputPort(outputName, PortDataType.Boolean);
            port = sourceOperator.OutputPorts.Last();
        }

        flow.DecisionConfiguration = new DecisionConfiguration
        {
            FinalDecisionBinding = new FinalDecisionBinding
            {
                SourceOperatorId = sourceOperator.Id,
                SourceOutputPortId = port.Id,
                SourceOutputName = port.Name,
                DataType = DecisionValueType.Boolean,
                Rule = DecisionInterpretationRule.Boolean,
                TrueMeansOk = trueMeansOk
            }
        };
        return flow;
    }

    private static Operator EnsureJudgmentOperator(OperatorFlow flow)
    {
        var judgment = flow.Operators.FirstOrDefault(candidate =>
            candidate.Type == OperatorType.ResultJudgment &&
            candidate.Name.Equals("Test Final Decision", StringComparison.Ordinal));
        if (judgment != null)
        {
            return judgment;
        }

        judgment = new Operator(Guid.NewGuid(), "Test Final Decision", OperatorType.ResultJudgment, 0, 0);
        flow.AddOperator(judgment);
        return judgment;
    }
}
