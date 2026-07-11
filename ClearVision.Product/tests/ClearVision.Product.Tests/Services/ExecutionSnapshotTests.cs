using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Services;
using ClearVision.Product.Core.ValueObjects;
using FluentAssertions;

namespace ClearVision.Product.Tests.Services;

public sealed class ExecutionSnapshotTests
{
    [Fact]
    public void Snapshot_Captures_An_Immutable_Flow_Identity()
    {
        var flow = CreateFlow();
        var snapshot = new ExecutionSnapshot(
            Guid.NewGuid(),
            flow,
            persistenceRevision: 7,
            ExecutionSnapshotSource.Draft,
            ExecutionRunMode.FormalPrimary);

        var originalHash = snapshot.FlowHash;
        flow.Operators[0].UpdateParameter("Threshold", 99d);
        flow.Operators[0].UpdatePosition(200, 300);

        snapshot.FlowHash.Should().Be(originalHash);
        snapshot.CreateExecutionFlow().Operators.Single().Parameters.Single().GetValue().Should().Be(12d);
        ExecutionFlowIdentity.ComputeFlowHash(flow).Should().NotBe(originalHash);
    }

    [Fact]
    public void FlowHash_Ignores_Editor_Layout_But_Includes_Execution_Configuration()
    {
        var flow = CreateFlow();
        var sameSemanticsDifferentLayout = ExecutionFlowIdentity.CloneFlow(flow);
        sameSemanticsDifferentLayout.Operators.Single().UpdatePosition(999, -42);

        ExecutionFlowIdentity.ComputeFlowHash(sameSemanticsDifferentLayout)
            .Should().Be(ExecutionFlowIdentity.ComputeFlowHash(flow));

        sameSemanticsDifferentLayout.Operators.Single().UpdateParameter("Threshold", 13d);
        ExecutionFlowIdentity.ComputeFlowHash(sameSemanticsDifferentLayout)
            .Should().NotBe(ExecutionFlowIdentity.ComputeFlowHash(flow));
    }

    [Fact]
    public void ShadowPolicy_Rejects_External_Writes_And_Project_State_Writes()
    {
        var flow = new OperatorFlow("shadow");
        flow.AddOperator(new Operator("save", OperatorType.ImageSave, 0, 0));
        flow.AddOperator(new Operator("increment", OperatorType.VariableIncrement, 0, 0));

        var shadowViolations = ExecutionSideEffectPolicy.For(ExecutionRunMode.ShadowCandidate).Validate(flow);

        shadowViolations.Select(violation => violation.Capability)
            .Should().Contain(ExecutionSideEffect.FileWrite)
            .And.Contain(ExecutionSideEffect.StateWrite);
        ExecutionSideEffectPolicy.For(ExecutionRunMode.FormalPrimary).Validate(flow).Should().BeEmpty();
    }

    private static OperatorFlow CreateFlow()
    {
        var flow = new OperatorFlow("identity");
        var op = new Operator(Guid.NewGuid(), "threshold", OperatorType.Thresholding, 1, 2);
        op.LoadInputPort(Guid.NewGuid(), "Image", PortDataType.Image, true);
        op.LoadOutputPort(Guid.NewGuid(), "Result", PortDataType.Image);
        op.AddParameter(new Parameter(
            Guid.NewGuid(),
            "Threshold",
            "Threshold",
            string.Empty,
            "double",
            defaultValue: 10d,
            isRequired: true));
        op.UpdateParameter("Threshold", 12d);
        flow.AddOperator(op);
        return flow;
    }
}
