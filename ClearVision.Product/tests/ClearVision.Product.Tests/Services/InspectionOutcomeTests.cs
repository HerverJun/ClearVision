using ClearVision.Product.Core.Continuous;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Outcomes;
using ClearVision.Product.Core.Services;
using FluentAssertions;

namespace ClearVision.Product.Tests.Services;

[TestClassification(TestDomain.General, TestPurpose.Regression, TestLane.Pr, TestEvidenceType.Contract, TestOracleType.Contract, TestResourceRequirement.None, TestExpectedDuration.Fast, TestFlakyPolicy.Blocking, "product", Suites = "ServicesRegression")]
public class InspectionOutcomeTests
{
    public static TheoryData<ExecutionOutcome, DecisionOutcome, InspectionStatus> ProjectionCases => new()
    {
        { ExecutionOutcome.Failed, DecisionOutcome.Ok, InspectionStatus.Error },
        { ExecutionOutcome.TimedOut, DecisionOutcome.Ng, InspectionStatus.Error },
        { ExecutionOutcome.Cancelled, DecisionOutcome.Ok, InspectionStatus.NotInspected },
        { ExecutionOutcome.Skipped, DecisionOutcome.Ng, InspectionStatus.NotInspected },
        { ExecutionOutcome.Succeeded, DecisionOutcome.Ok, InspectionStatus.OK },
        { ExecutionOutcome.Succeeded, DecisionOutcome.Ng, InspectionStatus.NG },
        { ExecutionOutcome.Succeeded, DecisionOutcome.Undetermined, InspectionStatus.NotInspected },
        { ExecutionOutcome.Succeeded, DecisionOutcome.NotApplicable, InspectionStatus.NotInspected },
        { ExecutionOutcome.Succeeded, DecisionOutcome.Invalid, InspectionStatus.Error }
    };

    [Theory]
    [MemberData(nameof(ProjectionCases))]
    public void LegacyProjection_MapsCanonicalOutcome(
        ExecutionOutcome execution,
        DecisionOutcome decision,
        InspectionStatus expected)
    {
        LegacyInspectionStatusProjection.Project(execution, decision).Should().Be(expected);
    }

    [Fact]
    public void OutcomeStatistics_UsesOnlyOkAndNgAsYieldDenominator()
    {
        var statistics = InspectionOutcomeStatistics.Calculate(
        [
            new InspectionOutcome(ExecutionOutcome.Succeeded, DecisionOutcome.Ok, null, null, null),
            new InspectionOutcome(ExecutionOutcome.Succeeded, DecisionOutcome.Ng, null, null, null),
            new InspectionOutcome(ExecutionOutcome.Succeeded, DecisionOutcome.Ng, null, null, null),
            new InspectionOutcome(ExecutionOutcome.Succeeded, DecisionOutcome.Undetermined, null, null, null),
            new InspectionOutcome(ExecutionOutcome.Succeeded, DecisionOutcome.NotApplicable, null, null, null),
            new InspectionOutcome(ExecutionOutcome.Succeeded, DecisionOutcome.Invalid, null, null, null),
            new InspectionOutcome(ExecutionOutcome.Failed, DecisionOutcome.Undetermined, null, null, null),
            new InspectionOutcome(ExecutionOutcome.TimedOut, DecisionOutcome.Undetermined, null, null, null),
            new InspectionOutcome(ExecutionOutcome.Cancelled, DecisionOutcome.NotApplicable, null, null, null),
            new InspectionOutcome(ExecutionOutcome.Skipped, DecisionOutcome.NotApplicable, null, null, null)
        ]);

        statistics.TotalAttemptCount.Should().Be(10);
        statistics.ExecutionSucceededCount.Should().Be(6);
        statistics.ValidDecisionCount.Should().Be(3);
        statistics.YieldRate.Should().BeApproximately(1.0 / 3.0, 0.0001);
        statistics.DecisionCoverageRate.Should().Be(0.5);
        statistics.ExecutionFailureCount.Should().Be(2);
        statistics.UndeterminedCount.Should().Be(1);
        statistics.NotApplicableCount.Should().Be(1);
        statistics.InvalidCount.Should().Be(1);
        statistics.CancelledCount.Should().Be(1);
        statistics.SkippedCount.Should().Be(1);
    }

    [Fact]
    public void OutcomeResolver_SuccessWithoutSignal_IsSucceededUndeterminedWithoutError()
    {
        var outcome = InspectionOutcomeResolver.Resolve(new FlowExecutionResult
        {
            IsSuccess = true,
            OutputData = new Dictionary<string, object> { ["BlobCount"] = 2 }
        });

        outcome.Execution.Should().Be(ExecutionOutcome.Succeeded);
        outcome.Decision.Should().Be(DecisionOutcome.Undetermined);
        outcome.ReasonCode.Should().Be("MissingDecisionConfiguration");
        outcome.Message.Should().BeNull();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OutcomeResolver_NullOrEmptyOutput_IsSucceededUndetermined(bool useEmptyOutput)
    {
        var outcome = InspectionOutcomeResolver.Resolve(new FlowExecutionResult
        {
            IsSuccess = true,
            OutputData = useEmptyOutput ? new Dictionary<string, object>() : null
        });

        outcome.Execution.Should().Be(ExecutionOutcome.Succeeded);
        outcome.Decision.Should().Be(DecisionOutcome.Undetermined);
        outcome.Message.Should().BeNull();
    }

    [Fact]
    public void PreviewResolver_WithoutDecisionBinding_ExecutesSuccessfullyAsUndetermined()
    {
        var flow = new OperatorFlow("preview");
        flow.AddOperator(new Operator(Guid.NewGuid(), "Preview", OperatorType.Thresholding, 0, 0));

        var outcome = InspectionOutcomeResolver.ResolvePreview(
            new FlowExecutionResult
            {
                IsSuccess = true,
                OutputData = new Dictionary<string, object> { ["Measurement"] = 12.5 }
            },
            flow);

        outcome.Execution.Should().Be(ExecutionOutcome.Succeeded);
        outcome.Decision.Should().Be(DecisionOutcome.Undetermined);
        outcome.DecisionSource.Should().Be("LegacyHeuristic:None");
        outcome.HasJudgmentSignal.Should().BeFalse();
    }

    [Fact]
    public void OutcomeResolver_ExecutionFailure_PreservesRealError()
    {
        var outcome = InspectionOutcomeResolver.Resolve(new FlowExecutionResult
        {
            IsSuccess = false,
            ErrorMessage = "camera disconnected"
        });

        outcome.Execution.Should().Be(ExecutionOutcome.Failed);
        outcome.Decision.Should().Be(DecisionOutcome.Undetermined);
        outcome.Message.Should().Be("camera disconnected");
    }

    [Fact]
    public void OutcomeResolver_ShortCircuit_IsSkippedNotApplicable()
    {
        var outcome = InspectionOutcomeResolver.Resolve(new FlowExecutionResult
        {
            IsSuccess = true,
            WasShortCircuited = true
        });

        outcome.Execution.Should().Be(ExecutionOutcome.Skipped);
        outcome.Decision.Should().Be(DecisionOutcome.NotApplicable);
    }

    [Fact]
    public void SetResult_LegacyCompatibility_SynchronizesCanonicalFields()
    {
        var result = new InspectionResult(Guid.NewGuid());
        result.SetResult(InspectionStatus.NG, 12);

        result.GetOutcome().Should().Be(new InspectionOutcome(
            ExecutionOutcome.Succeeded,
            DecisionOutcome.Ng,
            "LegacyInspectionStatus",
            "LegacyInspectionStatusProjection",
            null,
            true));
    }

    [Fact]
    public void MarkAsError_SetsExecutionFailed()
    {
        var result = new InspectionResult(Guid.NewGuid());
        result.MarkAsError("boom");

        result.GetOutcome().Execution.Should().Be(ExecutionOutcome.Failed);
        result.Status.Should().Be(InspectionStatus.Error);
    }

    [Fact]
    public void ContinuousPolicy_UndeterminedDoesNotIncrementNgOrBackoff()
    {
        var policy = ContinuousInspectionOutcomePolicy.Evaluate(
            2,
            new InspectionOutcome(
                ExecutionOutcome.Succeeded,
                DecisionOutcome.Undetermined,
                "None",
                "MissingJudgmentSignal",
                null));

        policy.ConsecutiveNgCount.Should().Be(2);
        policy.IsExecutionFailure.Should().BeFalse();
        policy.ShouldUseNormalInterval.Should().BeTrue();
        policy.IsNgStopCandidate.Should().BeFalse();
    }

    [Fact]
    public void ContinuousPolicy_ExecutionFailureBacksOffButInvalidDecisionDoesNot()
    {
        ContinuousInspectionOutcomePolicy.Evaluate(
                0,
                new InspectionOutcome(ExecutionOutcome.Failed, DecisionOutcome.Undetermined, null, null, "boom"))
            .IsExecutionFailure.Should().BeTrue();

        ContinuousInspectionOutcomePolicy.Evaluate(
                0,
                new InspectionOutcome(ExecutionOutcome.Succeeded, DecisionOutcome.Invalid, null, null, "bad judgment"))
            .IsExecutionFailure.Should().BeFalse();
    }

    [Fact]
    public void ShadowComparison_TwoUndeterminedOutcomes_AreNotComparableOrMatched()
    {
        var undetermined = new InspectionOutcome(
            ExecutionOutcome.Succeeded,
            DecisionOutcome.Undetermined,
            "None",
            "MissingJudgmentSignal",
            null);

        var comparison = ShadowOutcomeComparison.Evaluate(undetermined, undetermined);

        comparison.Comparable.Should().BeFalse();
        comparison.Matched.Should().BeNull();
    }
}
