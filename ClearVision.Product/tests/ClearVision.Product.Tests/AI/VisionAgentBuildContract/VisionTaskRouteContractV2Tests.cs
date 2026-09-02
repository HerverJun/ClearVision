using ClearVision.Product.Core.DTOs;
using ClearVision.Product.Infrastructure.AI.Agent;
using ClearVision.Product.Infrastructure.AI.Tools;
using FluentAssertions;

namespace ClearVision.Product.Tests.AI.VisionAgentBuildContract;

[TestClassification(TestDomain.Ai, TestPurpose.Regression, TestLane.Nightly, TestEvidenceType.Contract, TestOracleType.Contract, TestResourceRequirement.None, TestExpectedDuration.Fast, TestFlakyPolicy.Blocking, "vision-agent")]
public sealed class VisionTaskRouteContractV2Tests
{
    public static TheoryData<string> CanonicalTasks => new()
    {
        AiVisionTaskTypes.PresenceAbsence,
        AiVisionTaskTypes.AttributeClassification,
        AiVisionTaskTypes.ObjectDetection,
        AiVisionTaskTypes.TemplateLocation,
        AiVisionTaskTypes.SurfaceDefect,
        AiVisionTaskTypes.GeometryMeasurement,
        AiVisionTaskTypes.WireSequence,
        AiVisionTaskTypes.CodeRecognition
    };

    [Theory(DisplayName = "Route v2 should accept complete task-aware business result graphs")]
    [MemberData(nameof(CanonicalTasks))]
    public void Assess_ShouldAcceptCompleteTaskGraph(string taskType)
    {
        var graph = GraphFor(taskType);

        var assessment = new VisionTaskRouteContractRegistry().Assess(taskType, graph);

        assessment.ContractVersion.Should().Be(VisionAgentPlanContractVersions.V2);
        assessment.Supported.Should().BeTrue();
        assessment.Satisfied.Should().BeTrue(string.Join(",", assessment.BlockingReasons));
        assessment.MatchedCapabilities.Should().NotBeEmpty();
        assessment.MissingCapabilities.Should().BeEmpty();
        assessment.MissingResultSemantics.Should().BeEmpty();
        assessment.ReachedTerminals.Should().Contain("ResultOutput");
        assessment.Evidence.Should().Contain(item => item.StartsWith("semantic_edge:", StringComparison.Ordinal));
    }

    [Theory(DisplayName = "Route v2 should reject every task when ResultOutput is missing")]
    [MemberData(nameof(CanonicalTasks))]
    public void Assess_ShouldRejectMissingResultOutput(string taskType)
    {
        var graph = GraphFor(taskType);
        var outputIds = graph.Nodes
            .Where(node => node.OperatorType == "ResultOutput")
            .Select(node => node.TempId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        graph = graph with
        {
            Nodes = graph.Nodes.Where(node => !outputIds.Contains(node.TempId)).ToList(),
            Connections = graph.Connections.Where(edge => !outputIds.Contains(edge.TargetTempId)).ToList()
        };

        var assessment = new VisionTaskRouteContractRegistry().Assess(taskType, graph);

        assessment.Satisfied.Should().BeFalse();
        assessment.BlockingReasons.Should().Contain("route_missing_result_output");
    }

    [Theory(DisplayName = "Route v2 should reject each task when only a type-compatible non-task processor exists")]
    [MemberData(nameof(CanonicalTasks))]
    public void Assess_ShouldRejectWrongProcessor(string taskType)
    {
        var graph = Graph(
            ["ImageAcquisition", "Thresholding", "ResultJudgment", "ResultOutput"],
            [
                ("ImageAcquisition", "Image", "Thresholding", "Image"),
                ("Thresholding", "Image", "ResultJudgment", "Value"),
                ("ResultJudgment", "JudgmentResult", "ResultOutput", "Result")
            ]);

        var assessment = new VisionTaskRouteContractRegistry().Assess(taskType, graph);

        assessment.Satisfied.Should().BeFalse();
        assessment.BlockingReasons.Should().Contain("route_missing_task_processor");
    }

    [Fact(DisplayName = "Route v2 should reject Blob-only template location even when count reaches judgment and output")]
    public void Assess_ShouldRejectBlobOnlyTemplateLocation()
    {
        var graph = Graph(
            ["ImageAcquisition", "BlobAnalysis", "ResultJudgment", "ResultOutput"],
            [
                ("ImageAcquisition", "Image", "BlobAnalysis", "Image"),
                ("BlobAnalysis", "BlobCount", "ResultJudgment", "Value"),
                ("BlobAnalysis", "BlobFeatures", "ResultOutput", "Data"),
                ("ResultJudgment", "JudgmentResult", "ResultOutput", "Result")
            ]);

        var assessment = new VisionTaskRouteContractRegistry().Assess(
            AiVisionTaskTypes.TemplateLocation,
            graph);

        assessment.Satisfied.Should().BeFalse();
        assessment.BlockingReasons.Should().Contain("route_missing_task_processor");
        assessment.MissingResultSemantics.Should().Contain(
            [VisionPortSemantics.IsMatch, VisionPortSemantics.TemplateMatches, VisionPortSemantics.Confidence]);
    }

    [Fact(DisplayName = "Route v2 should reject a task processor used as the terminal")]
    public void Assess_ShouldRejectProcessorAsTerminal()
    {
        var graph = Graph(
            ["ImageAcquisition", "TemplateMatching"],
            [("ImageAcquisition", "Image", "TemplateMatching", "Image")]);

        var assessment = new VisionTaskRouteContractRegistry().Assess(
            AiVisionTaskTypes.TemplateLocation,
            graph);

        assessment.Satisfied.Should().BeFalse();
        assessment.BlockingReasons.Should().Contain("route_missing_result_output");
        assessment.ReachedTerminals.Should().BeEmpty();
    }

    [Fact(DisplayName = "Route v2 should require promised defect area to reach both judgment and ResultOutput data")]
    public void Assess_ShouldRequirePromisedDefectAreaReachability()
    {
        var graph = GraphFor(AiVisionTaskTypes.SurfaceDefect);

        var assessment = new VisionTaskRouteContractRegistry().Assess(
            AiVisionTaskTypes.SurfaceDefect,
            graph,
            ["defect_area"]);

        assessment.Satisfied.Should().BeFalse();
        assessment.BlockingReasons.Should().Contain("route_required_result_unreachable_defect_area");
        assessment.MissingResultSemantics.Should().Contain(VisionPortSemantics.DefectArea);
    }

    private static CanonicalWorkflowGraph GraphFor(string taskType)
    {
        return taskType switch
        {
            AiVisionTaskTypes.PresenceAbsence => Graph(
                ["ImageAcquisition", "BlobAnalysis", "ResultJudgment", "ResultOutput"],
                [
                    ("ImageAcquisition", "Image", "BlobAnalysis", "Image"),
                    ("BlobAnalysis", "BlobCount", "ResultJudgment", "Value"),
                    ("BlobAnalysis", "BlobCount", "ResultOutput", "Data"),
                    ("ResultJudgment", "JudgmentResult", "ResultOutput", "Result")
                ]),
            AiVisionTaskTypes.AttributeClassification => Graph(
                ["ImageAcquisition", "DeepLearning", "ResultJudgment", "ResultOutput"],
                [
                    ("ImageAcquisition", "Image", "DeepLearning", "Image"),
                    ("DeepLearning", "TopClassLabel", "ResultJudgment", "Value"),
                    ("DeepLearning", "TopClassConfidence", "ResultJudgment", "Confidence"),
                    ("DeepLearning", "ClassificationResult", "ResultOutput", "Data"),
                    ("ResultJudgment", "JudgmentResult", "ResultOutput", "Result")
                ]),
            AiVisionTaskTypes.ObjectDetection => Graph(
                ["ImageAcquisition", "DeepLearning", "ResultJudgment", "ResultOutput"],
                [
                    ("ImageAcquisition", "Image", "DeepLearning", "Image"),
                    ("DeepLearning", "ObjectCount", "ResultJudgment", "Value"),
                    ("DeepLearning", "DetectionList", "ResultOutput", "Data"),
                    ("ResultJudgment", "JudgmentResult", "ResultOutput", "Result")
                ]),
            AiVisionTaskTypes.TemplateLocation => Graph(
                ["ImageAcquisition", "TemplateMatching", "ResultJudgment", "ResultOutput"],
                [
                    ("ImageAcquisition", "Image", "TemplateMatching", "Image"),
                    ("TemplateMatching", "IsMatch", "ResultJudgment", "Value"),
                    ("TemplateMatching", "Score", "ResultJudgment", "Confidence"),
                    ("TemplateMatching", "Matches", "ResultOutput", "Data"),
                    ("ResultJudgment", "JudgmentResult", "ResultOutput", "Result")
                ]),
            AiVisionTaskTypes.SurfaceDefect => Graph(
                ["ImageAcquisition", "SurfaceDefectDetection", "ResultJudgment", "ResultOutput"],
                [
                    ("ImageAcquisition", "Image", "SurfaceDefectDetection", "Image"),
                    ("SurfaceDefectDetection", "DefectCount", "ResultJudgment", "Value"),
                    ("SurfaceDefectDetection", "Diagnostics", "ResultOutput", "Data"),
                    ("ResultJudgment", "JudgmentResult", "ResultOutput", "Result")
                ]),
            AiVisionTaskTypes.GeometryMeasurement => Graph(
                ["ImageAcquisition", "CircleMeasurement", "UnitConvert", "Aggregator", "ResultJudgment", "ResultOutput"],
                [
                    ("ImageAcquisition", "Image", "CircleMeasurement", "Image"),
                    ("CircleMeasurement", "Radius", "UnitConvert", "Value"),
                    ("UnitConvert", "Result", "ResultJudgment", "Value"),
                    ("UnitConvert", "Result", "Aggregator", "Value1"),
                    ("UnitConvert", "Unit", "Aggregator", "Value2"),
                    ("Aggregator", "MergedList", "ResultOutput", "Data"),
                    ("ResultJudgment", "JudgmentResult", "ResultOutput", "Result")
                ]),
            AiVisionTaskTypes.WireSequence => Graph(
                ["ImageAcquisition", "DeepLearning", "DetectionSequenceJudge", "ResultJudgment", "ResultOutput"],
                [
                    ("ImageAcquisition", "Image", "DeepLearning", "Image"),
                    ("DeepLearning", "DetectionList", "DetectionSequenceJudge", "Detections"),
                    ("DetectionSequenceJudge", "IsMatch", "ResultJudgment", "Value"),
                    ("DetectionSequenceJudge", "ActualOrder", "ResultOutput", "Data"),
                    ("ResultJudgment", "JudgmentResult", "ResultOutput", "Result")
                ]),
            AiVisionTaskTypes.CodeRecognition => Graph(
                ["ImageAcquisition", "CodeRecognition", "ResultJudgment", "ResultOutput"],
                [
                    ("ImageAcquisition", "Image", "CodeRecognition", "Image"),
                    ("CodeRecognition", "CodeCount", "ResultJudgment", "Value"),
                    ("CodeRecognition", "Text", "ResultOutput", "Text"),
                    ("CodeRecognition", "CodeType", "ResultOutput", "Data"),
                    ("ResultJudgment", "JudgmentResult", "ResultOutput", "Result")
                ]),
            _ => throw new ArgumentOutOfRangeException(nameof(taskType), taskType, null)
        };
    }

    private static CanonicalWorkflowGraph Graph(
        IReadOnlyList<string> operatorTypes,
        IReadOnlyList<(string SourceType, string SourcePort, string TargetType, string TargetPort)> connections)
    {
        var catalog = new VisionAgentOperatorContractCatalog();
        var ordinals = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var nodes = operatorTypes.Select(type =>
        {
            catalog.TryGet(type, out var contract).Should().BeTrue($"operator {type} must exist");
            ordinals.TryGetValue(type, out var ordinal);
            ordinals[type] = ordinal + 1;
            var id = $"{type}_{ordinal + 1}";
            return new CanonicalWorkflowNode(
                id,
                type,
                type,
                contract.Parameters.ToDictionary(item => item.Name, item => item.DefaultValue?.ToString(), StringComparer.OrdinalIgnoreCase),
                contract.InputPorts.Select(port => new VisionAgentPortFingerprint
                {
                    Name = port.Name,
                    DataType = port.DataType.ToString(),
                    Required = port.IsRequired
                }).ToList(),
                contract.OutputPorts.Select(port => new VisionAgentPortFingerprint
                {
                    Name = port.Name,
                    DataType = port.DataType.ToString()
                }).ToList());
        }).ToList();
        var consumedSource = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var consumedTarget = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var edges = connections.Select(edge =>
        {
            consumedSource.TryGetValue(edge.SourceType, out var sourceOrdinal);
            consumedTarget.TryGetValue(edge.TargetType, out var targetOrdinal);
            var source = nodes.Where(node => node.OperatorType == edge.SourceType).ElementAt(sourceOrdinal);
            var target = nodes.Where(node => node.OperatorType == edge.TargetType).ElementAt(targetOrdinal);
            return new CanonicalWorkflowConnection(source.TempId, edge.SourcePort, target.TempId, edge.TargetPort);
        }).ToList();

        return new CanonicalWorkflowGraph(nodes, edges, nodes.First().TempId);
    }
}
