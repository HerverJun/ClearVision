using ClearVision.Product.Application.DTOs;
using ClearVision.Product.Core.Decisions;
using ClearVision.Product.Core.Enums;

namespace ClearVision.Product.Desktop.Tests;

internal static class DecisionBindingTestSupport
{
    public static ProjectDto WithStringDecisionBinding(this ProjectDto project)
    {
        project.Flow ??= new OperatorFlowDto
        {
            Id = Guid.NewGuid(),
            Name = "test-flow"
        };
        project.Flow.WithStringDecisionBinding();
        return project;
    }

    public static OperatorFlowDto WithStringDecisionBinding(this OperatorFlowDto flow)
    {
        var source = flow.Operators.FirstOrDefault(candidate => candidate.Type == OperatorType.ResultJudgment);
        if (source is null)
        {
            source = new OperatorDto
            {
                Id = Guid.NewGuid(),
                Name = "Test Final Decision",
                Type = OperatorType.ResultJudgment,
                Parameters =
                [
                    CreateParameter("FieldName", "string", "Value"),
                    CreateParameter("Condition", "enum", "Equal"),
                    CreateParameter("ExpectValue", "string", "1"),
                    CreateParameter("ExpectValueMin", "string", "0"),
                    CreateParameter("ExpectValueMax", "string", "1"),
                    CreateParameter("MinConfidence", "double", 0.0d)
                ]
            };
            flow.Operators.Add(source);
        }

        var port = source.OutputPorts.FirstOrDefault(candidate =>
            candidate.Name.Equals("JudgmentResult", StringComparison.OrdinalIgnoreCase));
        if (port is null)
        {
            port = new PortDto
            {
                Id = Guid.NewGuid(),
                Name = "JudgmentResult",
                Direction = PortDirection.Output,
                DataType = PortDataType.String
            };
            source.OutputPorts.Add(port);
        }

        flow.DecisionConfiguration = new DecisionConfiguration
        {
            FinalDecisionBinding = new FinalDecisionBinding
            {
                SourceOperatorId = source.Id,
                SourceOutputPortId = port.Id,
                SourceOutputName = port.Name,
                DataType = DecisionValueType.String,
                Rule = DecisionInterpretationRule.StringMap,
                OkValue = "OK",
                NgValue = "NG"
            },
            MissingDecisionPolicy = MissingDecisionPolicy.Undetermined
        };
        return flow;
    }

    private static ParameterDto CreateParameter(string name, string dataType, object value) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        DisplayName = name,
        DataType = dataType,
        Value = value,
        DefaultValue = value
    };
}
