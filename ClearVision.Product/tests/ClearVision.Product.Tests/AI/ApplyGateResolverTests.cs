using ClearVision.Product.Core.AI.Tools;
using ClearVision.Product.Core.DTOs;
using ClearVision.Product.Infrastructure.AI.Agent;
using FluentAssertions;

namespace ClearVision.Product.Tests.AI;

[TestClassification(TestDomain.Ai, TestPurpose.Regression, TestLane.Pr, TestEvidenceType.Contract, TestOracleType.Contract, TestResourceRequirement.None, TestExpectedDuration.Fast, TestFlakyPolicy.Blocking, "vision-agent")]
public sealed class ApplyGateResolverTests
{
    [Fact]
    public void MissingRouteEvidence_ShouldBlockCanvasApply()
    {
        const string fingerprint = "sha256:artifact";
        var result = new ApplyGateResolver().Build(
            VisionAgentToolResult.Ok(new
            {
                blockingIssues = Array.Empty<string>(),
                validationFingerprint = fingerprint,
                fingerprintConsistent = true
            }),
            VisionAgentToolResult.Ok(new
            {
                dryRunSucceeded = true,
                dryRunFingerprint = fingerprint,
                fingerprintConsistent = true
            }),
            VisionAgentToolResult.Ok(new
            {
                readyForDeployment = true,
                precheckFingerprint = fingerprint,
                fingerprintConsistent = true
            }),
            new VisionAgentWorkflowDiff(),
            compiledFingerprint: fingerprint,
            routeAssessment: null,
            returnedFlowSemanticFingerprint: fingerprint);

        result.Payload.CanvasApplyReady.Should().BeFalse();
        result.Payload.RuntimeDraftReady.Should().BeFalse();
        result.Payload.RouteSemanticsSatisfied.Should().BeFalse();
        result.Payload.ApplyBlockers.Should().Contain("route_semantics_evidence_missing");
    }
}
