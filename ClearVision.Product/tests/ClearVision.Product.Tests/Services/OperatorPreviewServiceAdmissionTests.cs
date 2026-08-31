using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Interfaces;
using ClearVision.Product.Core.Services;
using ClearVision.Product.Infrastructure.Operators;
using ClearVision.Product.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using OpenCvSharp;

namespace ClearVision.Product.Tests.Services;

[TestClassification(TestDomain.General, TestPurpose.Regression, TestLane.Pr, TestEvidenceType.Contract, TestOracleType.Contract, TestResourceRequirement.None, TestExpectedDuration.Fast, TestFlakyPolicy.Blocking, "product", Suites = "ServicesRegression")]
public sealed class OperatorPreviewServiceAdmissionTests
{
    [Fact]
    public async Task GovernedAdapter_ShouldBlockSingleOperatorSideEffectsWithoutCallingRawEngine()
    {
        var engine = Substitute.For<IFlowExecutionEngine>();
        var adapter = new GovernedFlowExecutionService(engine);
        var sideEffectOperator = new OperatorFactory().CreateOperator(OperatorType.TextSave, "TextSave", 0, 0);
        var snapshot = CreatePreviewSnapshot(sideEffectOperator, ExecutionSideEffect.FileWrite);

        var result = await adapter.ExecuteOperatorWithSnapshotAsync(snapshot);

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("ADMISSION_DRAFT_PREVIEW_SIDE_EFFECT_BLOCKED");
        await engine.DidNotReceiveWithAnyArgs().ExecuteOperatorAsync(
            Arg.Any<Operator>(),
            Arg.Any<Dictionary<string, object>?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PreviewAsync_WithSideEffectOperator_ShouldReturnBlockedWithoutExecuting()
    {
        var flowExecution = Substitute.For<IFlowExecutionService>();
        var admission = new ExecutionAdmissionService(Substitute.For<IProjectRepository>());
        using var image = new Mat(4, 4, MatType.CV_8UC3, Scalar.All(0));
        var service = new OperatorPreviewService(
            new OperatorFactory(),
            flowExecution,
            admission,
            NullLogger<OperatorPreviewService>.Instance);
        var authority = CreateAuthority(ExecutionSideEffect.FileWrite, revision: 4);

        var result = await service.PreviewAsync(
            OperatorType.TextSave,
            parameters: null,
            image,
            Guid.NewGuid(),
            projectRevision: 4,
            Guid.NewGuid(),
            authority,
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("ADMISSION_DRAFT_PREVIEW_SIDE_EFFECT_BLOCKED");
        await flowExecution.DidNotReceiveWithAnyArgs().ExecuteOperatorWithSnapshotAsync(
            Arg.Any<ExecutionSnapshot>(),
            Arg.Any<Dictionary<string, object>?>(),
            Arg.Any<CancellationToken>());
    }

    private static ExecutionSnapshot CreatePreviewSnapshot(
        Operator @operator,
        ExecutionSideEffect capabilities)
    {
        const long revision = 7;
        var flow = new OperatorFlow("Single operator preview");
        flow.AddOperator(@operator);
        var projectId = Guid.NewGuid();
        var hash = ExecutionFlowIdentity.ComputeFlowHash(flow);
        return new ExecutionSnapshot(
            projectId,
            flow,
            revision,
            ExecutionSnapshotSource.Draft,
            ExecutionRunMode.Preview,
            new Dictionary<string, string>
            {
                ["ProjectRevision"] = revision.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["FlowHash"] = hash
            },
            principal: new ExecutionPrincipal("engineer-1", "Engineer", "Engineer", true),
            capabilityManifest: new ExecutionCapabilityManifest(capabilities, isExplicit: true),
            expectedProjectRevision: revision,
            confirmationId: Guid.NewGuid().ToString("D"),
            auditId: Guid.NewGuid().ToString("D"));
    }

    private static ExecutionRequestAuthority CreateAuthority(
        ExecutionSideEffect capabilities,
        long revision) =>
        new(
            new ExecutionPrincipal("engineer-1", "Engineer", "Engineer", true),
            revision,
            new ExecutionCapabilityManifest(capabilities, isExplicit: true),
            Guid.NewGuid().ToString("D"),
            Guid.NewGuid().ToString("D"));
}
