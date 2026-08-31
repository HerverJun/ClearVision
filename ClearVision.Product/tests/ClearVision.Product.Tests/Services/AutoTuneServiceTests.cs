using System.Text.Json;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Services;
using ClearVision.Product.Core.ValueObjects;
using ClearVision.Product.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using DetectionResultValue = ClearVision.Product.Core.ValueObjects.DetectionResult;

namespace ClearVision.Product.Tests.Services;

[TestClassification(TestDomain.General, TestPurpose.Regression, TestLane.Pr, TestEvidenceType.Contract, TestOracleType.Contract, TestResourceRequirement.None, TestExpectedDuration.Fast, TestFlakyPolicy.Blocking, "product", Suites = "ServicesRegression")]
public class AutoTuneServiceTests
{

    [Fact]
    public async Task AutoTuneScenarioAsync_ShouldOnlyTuneBoxNmsThresholds()
    {
        var flowNodePreviewService = Substitute.For<IFlowNodePreviewService>();
        var service = new AutoTuneService(
            NullLogger<AutoTuneService>.Instance,
            flowNodePreviewService);

        var flow = new OperatorFlow("WireSequenceFlow");
        var boxNms = new Operator("BoxNms", OperatorType.BoxNms, 0, 0);
        boxNms.AddParameter(new Parameter(Guid.NewGuid(), "ScoreThreshold", "ScoreThreshold", string.Empty, "double", 0.25d));
        boxNms.AddParameter(new Parameter(Guid.NewGuid(), "IouThreshold", "IouThreshold", string.Empty, "double", 0.45d));
        var judge = new Operator("Judge", OperatorType.DetectionSequenceJudge, 0, 0);

        flow.AddOperator(boxNms);
        flow.AddOperator(judge);
        flow.Connections.Add(new OperatorConnection(boxNms.Id, Guid.NewGuid(), judge.Id, Guid.NewGuid()));

        var seenThresholds = new List<(double Score, double Iou)>();
        var previewCall = 0;
        flowNodePreviewService.PreviewWithMetricsAsync(
                Arg.Any<OperatorFlow>(),
                Arg.Any<Guid>(),
                Arg.Any<byte[]?>(),
                Arg.Any<Guid>(),
                Arg.Any<long>(),
                Arg.Any<ExecutionRequestAuthority>(),
                Arg.Any<ClearVision.Product.Core.ProjectVariables.ProjectVariableExecutionContext?>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                previewCall++;
                var callFlow = callInfo.ArgAt<OperatorFlow>(0);
                var callBoxNms = callFlow.Operators.Single(item => item.Type == OperatorType.BoxNms);
                seenThresholds.Add((
                    ReadDoubleParam(callBoxNms, "ScoreThreshold"),
                    ReadDoubleParam(callBoxNms, "IouThreshold")));

                return Task.FromResult(previewCall == 1
                    ? new FlowNodePreviewWithMetricsResult
                    {
                        Success = true,
                        TargetNodeId = judge.Id,
                        Outputs = new Dictionary<string, object>
                        {
                            ["DetectionList"] = new DetectionList(new[]
                            {
                                new DetectionResultValue("Wire_Brown", 0.52f, 10f, 10f, 8f, 8f),
                                new DetectionResultValue("Wire_Brown", 0.48f, 12f, 10f, 8f, 8f)
                            }),
                            ["ExpectedLabels"] = new[] { "Wire_Brown", "Wire_Black", "Wire_Blue" },
                            ["ExpectedCount"] = 3,
                            ["RequiredMinConfidence"] = 0.6d,
                            ["IsMatch"] = false
                        },
                        Metrics = new PreviewMetrics
                        {
                            OverallScore = 0.25,
                            Diagnostics =
                            [
                                PreviewDiagnosticTags.DuplicateDetectedClass,
                                PreviewDiagnosticTags.DetectionCountMismatch,
                                PreviewDiagnosticTags.LowDetectionConfidence
                            ]
                        },
                        DiagnosticCodes =
                        [
                            "duplicate_detected_class",
                            "detection_count_mismatch",
                            "low_detection_confidence"
                        ]
                    }
                    : new FlowNodePreviewWithMetricsResult
                    {
                        Success = true,
                        TargetNodeId = judge.Id,
                        Outputs = new Dictionary<string, object>
                        {
                            ["DetectionList"] = new DetectionList(new[]
                            {
                                new DetectionResultValue("Wire_Brown", 0.92f, 10f, 10f, 8f, 8f),
                                new DetectionResultValue("Wire_Black", 0.90f, 20f, 10f, 8f, 8f),
                                new DetectionResultValue("Wire_Blue", 0.89f, 30f, 10f, 8f, 8f)
                            }),
                            ["ExpectedLabels"] = new[] { "Wire_Brown", "Wire_Black", "Wire_Blue" },
                            ["ExpectedCount"] = 3,
                            ["RequiredMinConfidence"] = 0.6d,
                            ["ActualOrder"] = new[] { "Wire_Brown", "Wire_Black", "Wire_Blue" },
                            ["IsMatch"] = true
                        },
                        Metrics = new PreviewMetrics
                        {
                            OverallScore = 0.92,
                            Diagnostics = new List<string>()
                        },
                        DiagnosticCodes = new List<string>()
                    });
            });

        var projectId = Guid.NewGuid();
        const long persistenceRevision = 7;
        var authority = CreateAuthority(flow, persistenceRevision);
        var result = await service.AutoTuneScenarioAsync(
            "wire-sequence-terminal",
            flow,
            CreateInputImage(),
            new AutoTuneGoal(),
            projectId,
            persistenceRevision,
            authority,
            maxIterations: 5,
            ct: CancellationToken.None);

        result.Success.Should().BeTrue();
        result.IsGoalAchieved.Should().BeTrue();
        result.TotalIterations.Should().Be(2);
        result.FinalParameters.Keys.Should().BeEquivalentTo("BoxNms.ScoreThreshold", "BoxNms.IouThreshold");
        Convert.ToDouble(result.FinalParameters["BoxNms.ScoreThreshold"]).Should().BeApproximately(0.2d, 0.0001d);
        Convert.ToDouble(result.FinalParameters["BoxNms.IouThreshold"]).Should().BeApproximately(0.4d, 0.0001d);
        seenThresholds.Should().HaveCount(2);
        seenThresholds[0].Should().Be((0.25d, 0.45d));
        seenThresholds[1].Should().Be((0.2d, 0.4d));
    }

    [Fact]
    public async Task AutoTuneScenarioAsync_ShouldTuneDeepLearningConfidenceWhenBoxNmsIsAbsent()
    {
        var flowNodePreviewService = Substitute.For<IFlowNodePreviewService>();
        var service = new AutoTuneService(
            NullLogger<AutoTuneService>.Instance,
            flowNodePreviewService);

        var flow = new OperatorFlow("WireSequenceFlow");
        var deepLearning = new Operator("DeepLearning", OperatorType.DeepLearning, 0, 0);
        deepLearning.AddParameter(new Parameter(Guid.NewGuid(), "Confidence", "Confidence", string.Empty, "double", 0.05d));
        var judge = new Operator("Judge", OperatorType.DetectionSequenceJudge, 0, 0);

        flow.AddOperator(deepLearning);
        flow.AddOperator(judge);
        flow.Connections.Add(new OperatorConnection(deepLearning.Id, Guid.NewGuid(), judge.Id, Guid.NewGuid()));

        var seenConfidence = new List<double>();
        var previewCall = 0;
        flowNodePreviewService.PreviewWithMetricsAsync(
                Arg.Any<OperatorFlow>(),
                Arg.Any<Guid>(),
                Arg.Any<byte[]?>(),
                Arg.Any<Guid>(),
                Arg.Any<long>(),
                Arg.Any<ExecutionRequestAuthority>(),
                Arg.Any<ClearVision.Product.Core.ProjectVariables.ProjectVariableExecutionContext?>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                previewCall++;
                var callFlow = callInfo.ArgAt<OperatorFlow>(0);
                var callDeepLearning = callFlow.Operators.Single(item => item.Type == OperatorType.DeepLearning);
                seenConfidence.Add(ReadDoubleParam(callDeepLearning, "Confidence"));

                return Task.FromResult(previewCall == 1
                    ? new FlowNodePreviewWithMetricsResult
                    {
                        Success = true,
                        TargetNodeId = judge.Id,
                        Outputs = new Dictionary<string, object>
                        {
                            ["DetectionList"] = new DetectionList(new[]
                            {
                                new DetectionResultValue("Wire_Brown", 0.52f, 10f, 10f, 8f, 8f)
                            }),
                            ["ExpectedLabels"] = new[] { "Wire_Brown", "Wire_Black" },
                            ["ExpectedCount"] = 2,
                            ["RequiredMinConfidence"] = 0.6d,
                            ["IsMatch"] = false
                        },
                        Metrics = new PreviewMetrics { OverallScore = 0.25 },
                        DiagnosticCodes =
                        [
                            "missing_expected_class",
                            "detection_count_mismatch",
                            "low_detection_confidence"
                        ]
                    }
                    : new FlowNodePreviewWithMetricsResult
                    {
                        Success = true,
                        TargetNodeId = judge.Id,
                        Outputs = new Dictionary<string, object>
                        {
                            ["DetectionList"] = new DetectionList(new[]
                            {
                                new DetectionResultValue("Wire_Brown", 0.92f, 10f, 10f, 8f, 8f),
                                new DetectionResultValue("Wire_Black", 0.90f, 20f, 10f, 8f, 8f)
                            }),
                            ["ExpectedLabels"] = new[] { "Wire_Brown", "Wire_Black" },
                            ["ExpectedCount"] = 2,
                            ["RequiredMinConfidence"] = 0.6d,
                            ["ActualOrder"] = new[] { "Wire_Brown", "Wire_Black" },
                            ["IsMatch"] = true
                        },
                        Metrics = new PreviewMetrics { OverallScore = 0.92 },
                        DiagnosticCodes = new List<string>()
                    });
            });

        var projectId = Guid.NewGuid();
        const long persistenceRevision = 11;
        var authority = CreateAuthority(flow, persistenceRevision);
        var result = await service.AutoTuneScenarioAsync(
            "wire-sequence-terminal",
            flow,
            CreateInputImage(),
            new AutoTuneGoal(),
            projectId,
            persistenceRevision,
            authority,
            maxIterations: 5,
            ct: CancellationToken.None);

        result.Success.Should().BeTrue();
        result.IsGoalAchieved.Should().BeTrue();
        result.TotalIterations.Should().Be(2);
        result.FinalParameters.Keys.Should().BeEquivalentTo("DeepLearning.Confidence");
        Convert.ToDouble(result.FinalParameters["DeepLearning.Confidence"]).Should().BeApproximately(0.0d, 0.0001d);
        seenConfidence.Should().Equal(0.05d, 0.0d);
    }

    [Fact]
    public async Task AutoTuneScenarioAsync_ShouldClampOversizedIterationCountToFive()
    {
        var flowNodePreviewService = Substitute.For<IFlowNodePreviewService>();
        var service = new AutoTuneService(
            NullLogger<AutoTuneService>.Instance,
            flowNodePreviewService);
        var flow = CreateBoxNmsScenarioFlow();

        flowNodePreviewService.PreviewWithMetricsAsync(
                Arg.Any<OperatorFlow>(),
                Arg.Any<Guid>(),
                Arg.Any<byte[]?>(),
                Arg.Any<Guid>(),
                Arg.Any<long>(),
                Arg.Any<ExecutionRequestAuthority>(),
                Arg.Any<ClearVision.Product.Core.ProjectVariables.ProjectVariableExecutionContext?>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new FlowNodePreviewWithMetricsResult
            {
                Success = true,
                Outputs = new Dictionary<string, object> { ["IsMatch"] = false },
                Metrics = new PreviewMetrics { OverallScore = 0.1d },
                DiagnosticCodes = ["duplicate_detected_class"]
            }));

        var projectId = Guid.NewGuid();
        const long persistenceRevision = 13;
        var authority = CreateAuthority(flow, persistenceRevision);
        var result = await service.AutoTuneScenarioAsync(
            "wire-sequence-terminal",
            flow,
            CreateInputImage(),
            new AutoTuneGoal(),
            projectId,
            persistenceRevision,
            authority,
            maxIterations: 50,
            ct: CancellationToken.None);

        result.TotalIterations.Should().Be(5);
        await flowNodePreviewService.Received(5).PreviewWithMetricsAsync(
            Arg.Any<OperatorFlow>(),
            Arg.Any<Guid>(),
            Arg.Any<byte[]?>(),
            projectId,
            persistenceRevision,
            authority,
            Arg.Any<ClearVision.Product.Core.ProjectVariables.ProjectVariableExecutionContext?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AutoTuneScenarioAsync_ShouldObserveCancellationBeforePreviewDispatch()
    {
        var flowNodePreviewService = Substitute.For<IFlowNodePreviewService>();
        var service = new AutoTuneService(
            NullLogger<AutoTuneService>.Instance,
            flowNodePreviewService);
        var flow = CreateBoxNmsScenarioFlow();
        var projectId = Guid.NewGuid();
        const long persistenceRevision = 17;
        var authority = CreateAuthority(flow, persistenceRevision);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var act = () => service.AutoTuneScenarioAsync(
            "wire-sequence-terminal",
            flow,
            CreateInputImage(),
            new AutoTuneGoal(),
            projectId,
            persistenceRevision,
            authority,
            maxIterations: 5,
            ct: cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        await flowNodePreviewService.DidNotReceiveWithAnyArgs().PreviewWithMetricsAsync(
            Arg.Any<OperatorFlow>(),
            Arg.Any<Guid>(),
            Arg.Any<byte[]?>(),
            Arg.Any<Guid>(),
            Arg.Any<long>(),
            Arg.Any<ExecutionRequestAuthority>(),
            Arg.Any<ClearVision.Product.Core.ProjectVariables.ProjectVariableExecutionContext?>(),
            Arg.Any<CancellationToken>());
    }

    private static OperatorFlow CreateBoxNmsScenarioFlow()
    {
        var flow = new OperatorFlow("WireSequenceFlow");
        var boxNms = new Operator("BoxNms", OperatorType.BoxNms, 0, 0);
        boxNms.AddParameter(new Parameter(Guid.NewGuid(), "ScoreThreshold", "ScoreThreshold", string.Empty, "double", 0.25d));
        boxNms.AddParameter(new Parameter(Guid.NewGuid(), "IouThreshold", "IouThreshold", string.Empty, "double", 0.45d));
        var judge = new Operator("Judge", OperatorType.DetectionSequenceJudge, 0, 0);
        flow.AddOperator(boxNms);
        flow.AddOperator(judge);
        flow.Connections.Add(new OperatorConnection(boxNms.Id, Guid.NewGuid(), judge.Id, Guid.NewGuid()));
        return flow;
    }

    private static byte[] CreateInputImage() => [1, 2, 3];

    private static ExecutionRequestAuthority CreateAuthority(OperatorFlow flow, long persistenceRevision) =>
        new(
            new ExecutionPrincipal("engineer-1", "engineer", "Engineer", IsAuthenticated: true),
            expectedProjectRevision: persistenceRevision,
            capabilityManifest: ExecutionCapabilityManifest.Derive(flow, isExplicit: true),
            confirmationId: Guid.NewGuid().ToString(),
            auditId: Guid.NewGuid().ToString());

    private static double ReadDoubleParam(Operator @operator, string name)
    {
        return @operator.Parameters.Single(item => item.Name == name).GetValue() switch
        {
            double doubleValue => doubleValue,
            float floatValue => floatValue,
            int intValue => intValue,
            long longValue => longValue,
            decimal decimalValue => (double)decimalValue,
            JsonElement jsonElement when jsonElement.ValueKind == JsonValueKind.Number => jsonElement.GetDouble(),
            string stringValue => double.Parse(stringValue),
            _ => throw new InvalidOperationException($"Unexpected parameter value for {name}")
        };
    }
}
