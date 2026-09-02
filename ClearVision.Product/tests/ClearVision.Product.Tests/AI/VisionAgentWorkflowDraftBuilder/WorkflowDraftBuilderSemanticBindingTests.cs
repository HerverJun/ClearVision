using ClearVision.Product.Core.DTOs;
using ClearVision.Product.Infrastructure.AI.Agent;
using ClearVision.Product.Infrastructure.AI.Tools;
using FluentAssertions;
using FluentAssertions.Execution;

namespace ClearVision.Product.Tests.AI.VisionAgentWorkflowDraftBuilder;

[TestClassification(TestDomain.Ai, TestPurpose.Regression, TestLane.Pr, TestEvidenceType.Contract, TestOracleType.Contract, TestResourceRequirement.None, TestExpectedDuration.Fast, TestFlakyPolicy.Blocking, "vision-agent")]
public sealed class WorkflowDraftBuilderSemanticBindingTests
{
    [Fact(DisplayName = "Surface defect mask should feed BlobAnalysis instead of the preview image")]
    public async Task SurfaceDefectMask_ShouldFeedBlobAnalysis()
    {
        var draft = await BuildAsync(
            AiVisionTaskTypes.SurfaceDefect,
            ["ImageAcquisition", "SurfaceDefectDetection", "BlobAnalysis", "ResultJudgment", "ResultOutput"]);

        draft.Artifact.Graph.ShouldContainEdge("op_1", "Image", "op_2", "Image");
        draft.Artifact.Graph.ShouldContainEdge("op_2", "DefectMask", "op_3", "Image");
        draft.Artifact.Graph.Connections.Should().NotContain(edge =>
            edge.SourceTempId == "op_2" && edge.SourcePortName == "Image" &&
            edge.TargetTempId == "op_3" && edge.TargetPortName == "Image");
        draft.Artifact.Graph.ShouldContainEdge("op_2", "DefectCount", "op_4", "Value");
        draft.Artifact.Graph.ShouldContainEdge("op_3", "Blobs", "op_5", "Data");
    }

    [Fact(DisplayName = "Code recognition should bind decoded text, count and type to distinct business ports")]
    public async Task CodeRecognition_ShouldBindDistinctBusinessPorts()
    {
        var draft = await BuildAsync(
            AiVisionTaskTypes.CodeRecognition,
            ["ImageAcquisition", "CodeRecognition", "ResultJudgment", "ResultOutput"]);

        draft.Artifact.Graph.ShouldContainEdge("op_2", "CodeCount", "op_3", "Value");
        draft.Artifact.Graph.ShouldContainEdge("op_2", "Text", "op_4", "Text");
        draft.Artifact.Graph.ShouldContainEdge("op_2", "CodeType", "op_4", "Data");
        draft.Artifact.Graph.Connections.Should().NotContain(edge => edge.TargetPortName == "Confidence");
        draft.Artifact.Graph.Connections.Should().NotContain(edge =>
            edge.SourcePortName == "Image" && edge.TargetPortName == "Value");
    }

    [Fact(DisplayName = "Template matching should bind IsMatch, Score and Matches without Blob substitution")]
    public async Task TemplateMatching_ShouldBindMatchScoreAndPose()
    {
        var draft = await BuildAsync(
            AiVisionTaskTypes.TemplateLocation,
            ["ImageAcquisition", "TemplateMatching", "ResultJudgment", "ResultOutput"]);

        draft.Artifact.Graph.ShouldContainEdge("op_2", "IsMatch", "op_3", "Value");
        draft.Artifact.Graph.ShouldContainEdge("op_2", "Score", "op_3", "Confidence");
        draft.Artifact.Graph.ShouldContainEdge("op_2", "Matches", "op_4", "Data");
        draft.Artifact.Graph.ShouldContainEdge("op_3", "JudgmentResult", "op_4", "Result");
    }

    [Theory(DisplayName = "Each governed measurement scalar should reach judgment and output data")]
    [InlineData("CircleMeasurement", "Radius")]
    [InlineData("LineMeasurement", "Length")]
    [InlineData("ContourMeasurement", "Area")]
    [InlineData("AngleMeasurement", "Angle")]
    public async Task MeasurementScalar_ShouldReachJudgmentAndData(string operatorType, string outputPort)
    {
        var draft = await BuildAsync(
            AiVisionTaskTypes.GeometryMeasurement,
            ["ImageAcquisition", operatorType, "ResultJudgment", "ResultOutput"]);

        draft.Artifact.Graph.ShouldContainEdge("op_2", outputPort, "op_3", "Value");
        draft.Artifact.Graph.ShouldContainEdge("op_2", outputPort, "op_4", "Data");
        draft.Artifact.Graph.Connections.Should().NotContain(edge =>
            edge.SourceTempId == "op_2" && edge.SourcePortName == "Image" && edge.TargetPortName == "Value");
    }

    [Fact(DisplayName = "Geometry route should bind two distinct elements and aggregate converted value with unit")]
    public async Task GeometryRoute_ShouldBindElementsAndAggregateValueWithUnit()
    {
        var operators = new[]
        {
            "ImageAcquisition", "CircleMeasurement", "CircleMeasurement", "Measurement",
            "UnitConvert", "Aggregator", "ResultJudgment", "ResultOutput"
        };
        var draft = await BuildAsync(AiVisionTaskTypes.GeometryMeasurement, operators);

        draft.Artifact.Graph.ShouldContainEdge("op_2", "Center", "op_4", "PointA");
        draft.Artifact.Graph.ShouldContainEdge("op_3", "Center", "op_4", "PointB");
        draft.Artifact.Graph.ShouldContainEdge("op_4", "Distance", "op_5", "Value");
        draft.Artifact.Graph.ShouldContainEdge("op_5", "Result", "op_6", "Value1");
        draft.Artifact.Graph.ShouldContainEdge("op_5", "Unit", "op_6", "Value2");
        draft.Artifact.Graph.ShouldContainEdge("op_5", "Result", "op_7", "Value");
        draft.Artifact.Graph.ShouldContainEdge("op_6", "Result", "op_8", "Data");

        var route = new VisionTaskRouteContractRegistry().Assess(
            AiVisionTaskTypes.GeometryMeasurement,
            draft.Artifact.Graph);
        route.Satisfied.Should().BeTrue(string.Join(",", route.BlockingReasons));
        route.ReachableResultSemantics.Should().Contain(VisionPortSemantics.MeasurementUnit);
    }

    [Fact(DisplayName = "Wire sequence should bind detections, boolean match and structured order details")]
    public async Task WireSequence_ShouldBindDetectionsMatchAndDetails()
    {
        var draft = await BuildAsync(
            AiVisionTaskTypes.WireSequence,
            ["ImageAcquisition", "DeepLearning", "DetectionSequenceJudge", "ResultJudgment", "ResultOutput"]);

        draft.Artifact.Graph.ShouldContainEdge("op_2", "DetectionList", "op_3", "Detections");
        draft.Artifact.Graph.ShouldContainEdge("op_3", "IsMatch", "op_4", "Value");
        draft.Artifact.Graph.ShouldContainEdge("op_3", "ActualOrder", "op_5", "Data");
    }

    [Fact(DisplayName = "Promised defect area should use DefectArea for judgment and data")]
    public async Task DefectArea_ShouldUseRealAreaScalar()
    {
        var draft = await BuildAsync(
            AiVisionTaskTypes.SurfaceDefect,
            ["ImageAcquisition", "SurfaceDefectDetection", "ResultJudgment", "ResultOutput"],
            requiredOutputs: [VisionPortSemantics.DefectArea],
            measurementTarget: "scratch area");

        draft.Artifact.Graph.ShouldContainEdge("op_2", "DefectArea", "op_3", "Value");
        draft.Artifact.Graph.ShouldContainEdge("op_2", "DefectArea", "op_4", "Data");
        draft.Artifact.Graph.Connections.Should().NotContain(edge => edge.SourcePortName == "BlobCount");

        var route = new VisionTaskRouteContractRegistry().Assess(
            AiVisionTaskTypes.SurfaceDefect,
            draft.Artifact.Graph,
            [VisionPortSemantics.DefectArea]);
        route.Satisfied.Should().BeTrue(string.Join(",", route.BlockingReasons));
    }

    [Fact(DisplayName = "BlobCount should never impersonate a promised defect area")]
    public async Task BlobCount_ShouldNotImpersonateDefectArea()
    {
        var draft = await BuildAsync(
            AiVisionTaskTypes.SurfaceDefect,
            ["ImageAcquisition", "Thresholding", "BlobAnalysis", "ResultJudgment", "ResultOutput"],
            requiredOutputs: [VisionPortSemantics.DefectArea],
            measurementTarget: "defect area");

        draft.Artifact.Graph.Connections.Should().NotContain(edge =>
            edge.SourcePortName == "BlobCount" &&
            (edge.TargetPortName == "Value" || edge.TargetPortName == "Data"));
        var route = new VisionTaskRouteContractRegistry().Assess(
            AiVisionTaskTypes.SurfaceDefect,
            draft.Artifact.Graph,
            [VisionPortSemantics.DefectArea]);
        route.Satisfied.Should().BeFalse();
        route.MissingResultSemantics.Should().Contain(VisionPortSemantics.DefectArea);
    }

    [Fact(DisplayName = "Any compatibility should not connect images to critical business ports")]
    public async Task AnyCompatibility_ShouldNotFeedCriticalBusinessPorts()
    {
        var draft = await BuildAsync(
            AiVisionTaskTypes.AttributeClassification,
            ["ImageAcquisition", "ResultJudgment", "ResultOutput"]);

        draft.Artifact.Graph.Connections.Should().NotContain(edge =>
            edge.SourcePortName == "Image" &&
            (edge.TargetPortName == "Value" ||
             edge.TargetPortName == "Confidence" ||
             edge.TargetPortName == "Data" ||
             edge.TargetPortName == "Result" ||
             edge.TargetPortName == "Text"));
        draft.Artifact.SemanticEdgeAudits
            .Where(edge => edge.CriticalBusinessEdge)
            .Should().OnlyContain(edge =>
                !string.IsNullOrWhiteSpace(edge.SourceSemantic) &&
                edge.SelectionReason == "business_semantic_allow_list");
        draft.Artifact.Graph.Connections
            .GroupBy(edge => $"{edge.TargetTempId}.{edge.TargetPortName}", StringComparer.OrdinalIgnoreCase)
            .Should().OnlyContain(group => group.Count() == 1);
        foreach (var op in draft.Artifact.CanvasProjection.Operators)
        {
            op.Metadata.Should().NotBeNull();
            op.Metadata!.Should().ContainKey("agentSemanticEdgeAudit");
            op.Metadata!["agentSemanticEdgeAudit"]!.ToString()
                .Should().Contain("business_semantic_allow_list");
        }
    }

    [Fact(DisplayName = "Semantic catalog entries should resolve to live operator contract ports")]
    public void SemanticCatalogEntries_ShouldResolveToLiveContractPorts()
    {
        var ports = new (string OperatorType, string PortName, string Semantic)[]
        {
            ("SurfaceDefectDetection", "DefectMask", VisionPortSemantics.DefectMask),
            ("SurfaceDefectDetection", "DefectArea", VisionPortSemantics.DefectArea),
            ("CodeRecognition", "Text", VisionPortSemantics.DecodedText),
            ("TemplateMatching", "IsMatch", VisionPortSemantics.IsMatch),
            ("TemplateMatching", "Score", VisionPortSemantics.Confidence),
            ("TemplateMatching", "Matches", VisionPortSemantics.TemplateMatches),
            ("CircleMeasurement", "Radius", VisionPortSemantics.MeasurementValue),
            ("LineMeasurement", "Length", VisionPortSemantics.MeasurementValue),
            ("ContourMeasurement", "Area", VisionPortSemantics.MeasurementValue),
            ("AngleMeasurement", "Angle", VisionPortSemantics.MeasurementValue),
            ("DetectionSequenceJudge", "ActualOrder", VisionPortSemantics.SequenceDetails),
            ("UnitConvert", "Unit", VisionPortSemantics.MeasurementUnit),
            ("Aggregator", "Result", VisionPortSemantics.MeasurementBundle),
            ("ResultJudgment", "JudgmentResult", VisionPortSemantics.JudgmentResult)
        };
        var contracts = new VisionAgentOperatorContractCatalog();
        var semantics = new VisionAgentPortSemanticCatalog();

        using var scope = new AssertionScope();
        foreach (var (operatorType, portName, semantic) in ports)
        {
            contracts.TryGet(operatorType, out var contract).Should().BeTrue(operatorType);
            contract.OutputPorts.Should().Contain(port => port.Name == portName, $"{operatorType}.{portName} must exist in the live catalog");
            semantics.OutputSemantics(operatorType, portName).Should().Contain(semantic);
        }
    }

    private static async Task<DraftWorkflowResolution> BuildAsync(
        string taskType,
        IReadOnlyList<string> operatorTypes,
        IReadOnlyList<string>? requiredOutputs = null,
        string measurementTarget = "",
        string acceptanceCriteria = "")
    {
        requiredOutputs ??= [];
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [VisionAgentPlanAnswerFields.TaskType] = taskType
        };
        if (!string.IsNullOrWhiteSpace(measurementTarget))
        {
            values[VisionAgentPlanAnswerFields.MeasurementTarget] = measurementTarget;
        }
        if (!string.IsNullOrWhiteSpace(acceptanceCriteria))
        {
            values[VisionAgentPlanAnswerFields.AcceptanceCriteria] = acceptanceCriteria;
        }

        var load = new BuildPlanLoad
        {
            TaskType = taskType,
            Plan = new VisionAgentPlanModeResult
            {
                SemanticExtraction = new VisionAgentSemanticExtractionResult
                {
                    IsVisionRequest = true,
                    TaskType = taskType,
                    MeasurementTarget = measurementTarget,
                    OkCondition = acceptanceCriteria
                },
                PlanFidelity = new VisionAgentPlanFidelityAssessment
                {
                    TaskType = taskType,
                    RequiredOutputSemantics = requiredOutputs.ToList()
                }
            },
            EffectiveRequirement = new VisionAgentEffectiveRequirement(
                values,
                new AiRequirementMaturityResult { TaskType = taskType },
                values.Keys.ToList(),
                []),
            RequirementMode = AiRequirementModes.Strict
        };
        var steps = operatorTypes.Select((operatorType, index) => new VisionAgentOperatorPipelineStep
        {
            TempId = $"op_{index + 1}",
            OperatorType = operatorType,
            Source = "test",
            Status = "selected"
        }).ToList();

        var result = await new WorkflowDraftBuilder().DraftAsync(
            new AiFlowGenerationRequest("semantic binding test"),
            load,
            new BuildIntentResolution("new"),
            new OperatorPipelineResolution(steps, []),
            new ParameterMappingResolution([], [], [], "test"),
            CancellationToken.None);
        return result.Payload;
    }
}

internal static class CanonicalWorkflowGraphAssertions
{
    public static void ShouldContainEdge(
        this CanonicalWorkflowGraph graph,
        string sourceTempId,
        string sourcePort,
        string targetTempId,
        string targetPort)
    {
        graph.Connections.Should().Contain(edge =>
            edge.SourceTempId == sourceTempId &&
            edge.SourcePortName == sourcePort &&
            edge.TargetTempId == targetTempId &&
            edge.TargetPortName == targetPort,
            $"expected semantic edge {sourceTempId}.{sourcePort} -> {targetTempId}.{targetPort}");
    }
}
