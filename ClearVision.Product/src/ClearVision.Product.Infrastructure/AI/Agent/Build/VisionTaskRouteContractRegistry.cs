using ClearVision.Product.Core.DTOs;
using ClearVision.Product.Infrastructure.AI.Tools;

namespace ClearVision.Product.Infrastructure.AI.Agent;

public sealed record VisionTaskRouteAssessment
{
    public string TaskType { get; init; } = string.Empty;
    public bool Supported { get; init; }
    public bool Satisfied { get; init; }
    public bool SafeScaffold { get; init; }
    public bool RequiresUserReview { get; init; }
    public List<string> BlockingReasons { get; init; } = [];
    public List<string> Evidence { get; init; } = [];
    public List<string> RequiredCapabilities { get; init; } = [];
    public List<string> MatchedCapabilities { get; init; } = [];
    public List<string> MissingCapabilities { get; init; } = [];
    public List<string> RequiredResultSemantics { get; init; } = [];
    public List<string> ReachableResultSemantics { get; init; } = [];
    public List<string> MissingResultSemantics { get; init; } = [];
    public List<string> LegalTerminals { get; init; } = ["ResultOutput"];
    public List<string> ReachedTerminals { get; init; } = [];
    public string ContractVersion { get; init; } = VisionAgentPlanContractVersions.V2;
}

public sealed record VisionTaskRouteContractDefinition
{
    public string CanonicalTaskType { get; init; } = string.Empty;
    public string RouteKey { get; init; } = string.Empty;
    public List<List<string>> ProcessorAlternatives { get; init; } = [];
    public List<string> AllowedProcessors { get; init; } = [];
    public List<string> JudgmentValueSemantics { get; init; } = [];
    public List<string> RequiredResultSemantics { get; init; } = [];
    public List<string> LegalTerminals { get; init; } = ["ResultOutput"];
    public List<string> Aliases { get; init; } = [];
    public string ContractVersion { get; init; } = VisionAgentPlanContractVersions.V2;
}

public sealed class VisionTaskRouteContractRegistry
{
    private const string CodeAcceptanceSemantic = "code_acceptance";

    private readonly VisionAgentPortSemanticCatalog _portSemantics = new();

    private static readonly IReadOnlyDictionary<string, VisionTaskRouteContractDefinition> ContractsByRouteKey =
        BuildContracts().ToDictionary(item => item.RouteKey, StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyCollection<VisionTaskRouteContractDefinition> Contracts =>
        ContractsByRouteKey.Values.ToList();

    public VisionTaskRouteAssessment Assess(
        string? taskType,
        IReadOnlyList<VisionAgentOperatorPipelineStep> pipeline)
    {
        var routeKey = NormalizeTaskType(taskType);
        if (!ContractsByRouteKey.TryGetValue(routeKey, out var contract))
        {
            return Blocked(routeKey, "unsupported_task_route_contract", "The requested task type has no registered route v2 contract.");
        }

        var types = pipeline
            .Select(item => item.OperatorType?.Trim() ?? string.Empty)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .ToList();
        var requiredCapabilities = contract.ProcessorAlternatives
            .Select(AlternativeKey)
            .ToList();
        var matched = contract.ProcessorAlternatives
            .Where(alternative => alternative.All(type => types.Contains(type, StringComparer.OrdinalIgnoreCase)))
            .Select(AlternativeKey)
            .ToList();
        var reasons = new List<string>();
        if (!types.Contains("ImageAcquisition", StringComparer.OrdinalIgnoreCase))
        {
            reasons.Add("route_missing_image_source");
        }
        if (matched.Count == 0)
        {
            reasons.Add("route_missing_task_processor");
        }
        if (!types.Contains("ResultJudgment", StringComparer.OrdinalIgnoreCase))
        {
            reasons.Add("route_missing_judgment");
        }
        if (!types.Contains("ResultOutput", StringComparer.OrdinalIgnoreCase))
        {
            reasons.Add("route_missing_result_output");
        }
        if (types.Count > 1)
        {
            reasons.Add("route_graph_unverified");
        }

        return Assessment(
            contract,
            reasons,
            ["pipeline_only_assessment"],
            requiredCapabilities,
            matched,
            contract.RequiredResultSemantics,
            [],
            types.Where(type => type.Equals("ResultOutput", StringComparison.OrdinalIgnoreCase)).ToList(),
            scaffold: IsScaffold(types));
    }

    internal VisionTaskRouteAssessment Assess(
        string? taskType,
        CanonicalWorkflowGraph graph,
        IReadOnlyCollection<string>? promisedOutputSemantics = null)
    {
        var routeKey = NormalizeTaskType(taskType);
        if (!ContractsByRouteKey.TryGetValue(routeKey, out var contract))
        {
            return Blocked(routeKey, "unsupported_task_route_contract", "The requested task type has no registered route v2 contract.");
        }

        return AssessGraph(contract, graph, promisedOutputSemantics ?? []);
    }

    public static string NormalizeTaskType(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant() ?? string.Empty;
        return AiVisionTaskCatalog.GetRouteContractKey(normalized) is { Length: > 0 } routeKey
            ? routeKey
            : normalized;
    }

    private VisionTaskRouteAssessment AssessGraph(
        VisionTaskRouteContractDefinition contract,
        CanonicalWorkflowGraph graph,
        IReadOnlyCollection<string> promisedOutputSemantics)
    {
        var reasons = new List<string>();
        var evidence = new List<string>();
        var nodeById = graph.Nodes
            .Where(node => !string.IsNullOrWhiteSpace(node.TempId))
            .GroupBy(node => node.TempId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var duplicateIds = graph.Nodes
            .Where(node => !string.IsNullOrWhiteSpace(node.TempId))
            .GroupBy(node => node.TempId, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToList();
        if (duplicateIds.Count > 0)
        {
            reasons.Add("route_duplicate_node_identity");
            evidence.Add($"duplicate_node_ids:{string.Join(",", duplicateIds)}");
        }

        var sources = graph.Nodes.Where(node => IsImageSource(node.OperatorType)).ToList();
        if (sources.Count == 0)
        {
            reasons.Add("route_missing_image_source");
        }
        else
        {
            evidence.Add($"image_sources:{string.Join(",", sources.Select(node => node.TempId))}");
        }

        var resultOutputs = graph.Nodes
            .Where(node => node.OperatorType.Equals("ResultOutput", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (resultOutputs.Count == 0)
        {
            reasons.Add("route_missing_result_output");
        }

        var judgments = graph.Nodes
            .Where(node => node.OperatorType.Equals("ResultJudgment", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (judgments.Count == 0)
        {
            reasons.Add("route_missing_judgment");
        }

        var validConnections = graph.Connections
            .Where(connection => IsValidEndpoint(connection, nodeById))
            .ToList();
        if (graph.Connections.Count != validConnections.Count)
        {
            reasons.Add("route_invalid_connection_endpoint");
        }
        if (graph.Nodes.Count > 1 && validConnections.Count == 0)
        {
            reasons.Add("route_missing_connections");
        }

        var incomingPorts = validConnections
            .Select(connection => $"{connection.TargetTempId}|{connection.TargetPortName}")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var node in graph.Nodes)
        {
            foreach (var port in node.InputPorts.Where(port => port.Required))
            {
                var resourceType = VisionAgentResourceClassifier.Classify(
                    node.OperatorType,
                    port.Name,
                    port.DataType);
                if (!string.IsNullOrWhiteSpace(resourceType))
                {
                    evidence.Add($"resource_input_pending:{node.TempId}.{port.Name}:{resourceType}");
                    continue;
                }

                if (!incomingPorts.Contains($"{node.TempId}|{port.Name}"))
                {
                    reasons.Add("route_required_input_unbound");
                    evidence.Add($"required_input_unbound:{node.TempId}.{port.Name}");
                }
            }
        }

        var reachableFromSource = ReachableFrom(sources.Select(node => node.TempId), validConnections);
        var reachableToOutput = ReachableTo(resultOutputs.Select(node => node.TempId), validConnections);
        var alternativeKeys = contract.ProcessorAlternatives.Select(AlternativeKey).ToList();
        var matchedAlternatives = contract.ProcessorAlternatives
            .Where(alternative => alternative.All(type =>
                graph.Nodes.Any(node =>
                    node.OperatorType.Equals(type, StringComparison.OrdinalIgnoreCase) &&
                    reachableFromSource.Contains(node.TempId) &&
                    reachableToOutput.Contains(node.TempId))))
            .Select(AlternativeKey)
            .ToList();
        if (matchedAlternatives.Count == 0)
        {
            reasons.Add(graph.Nodes.Any(node => contract.AllowedProcessors.Contains(node.OperatorType, StringComparer.OrdinalIgnoreCase))
                ? "route_task_processor_not_on_result_path"
                : "route_missing_task_processor");
        }
        else
        {
            evidence.AddRange(matchedAlternatives.Select(value => $"matched_processing_capability:{value}"));
        }

        if (resultOutputs.Count > 0 && !resultOutputs.Any(node => reachableFromSource.Contains(node.TempId)))
        {
            reasons.Add("route_result_not_reachable_from_source");
        }
        if (judgments.Count > 0 && !judgments.Any(node =>
                reachableFromSource.Contains(node.TempId) && reachableToOutput.Contains(node.TempId)))
        {
            reasons.Add("route_judgment_not_on_result_path");
        }

        var requiredResults = contract.RequiredResultSemantics
            .Concat(promisedOutputSemantics.Select(NormalizePromisedSemantic))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var reachableResults = new List<string>();
        foreach (var required in requiredResults)
        {
            if (RequiredSemanticIsReachable(
                    required,
                    graph,
                    nodeById,
                    validConnections,
                    judgments,
                    resultOutputs,
                    evidence))
            {
                reachableResults.Add(required);
            }
            else
            {
                reasons.Add($"route_required_result_unreachable_{SafeKey(required)}");
            }
        }

        var scaffold = IsScaffold(graph.Nodes.Select(node => node.OperatorType));
        if (scaffold)
        {
            reasons.Add("minimum_scaffold_task_incomplete");
            evidence.Add("safe_scaffold");
        }

        return Assessment(
            contract,
            reasons,
            evidence,
            alternativeKeys,
            matchedAlternatives,
            requiredResults,
            reachableResults,
            resultOutputs.Where(node => reachableFromSource.Contains(node.TempId)).Select(node => node.OperatorType).ToList(),
            scaffold);
    }

    private bool RequiredSemanticIsReachable(
        string required,
        CanonicalWorkflowGraph graph,
        IReadOnlyDictionary<string, CanonicalWorkflowNode> nodeById,
        IReadOnlyList<CanonicalWorkflowConnection> connections,
        IReadOnlyList<CanonicalWorkflowNode> judgments,
        IReadOnlyList<CanonicalWorkflowNode> resultOutputs,
        ICollection<string> evidence)
    {
        bool EdgeTo(string targetOperator, string targetPort, params string[] sourceSemantics)
        {
            var match = connections.FirstOrDefault(connection =>
                nodeById.TryGetValue(connection.SourceTempId, out var source) &&
                nodeById.TryGetValue(connection.TargetTempId, out var target) &&
                target.OperatorType.Equals(targetOperator, StringComparison.OrdinalIgnoreCase) &&
                connection.TargetPortName.Equals(targetPort, StringComparison.OrdinalIgnoreCase) &&
                _portSemantics.OutputSemantics(source.OperatorType, connection.SourcePortName)
                    .Any(semantic => sourceSemantics.Contains(semantic, StringComparer.OrdinalIgnoreCase)));
            if (match == null)
            {
                return false;
            }

            var sourceNode = nodeById[match.SourceTempId];
            evidence.Add($"semantic_edge:{sourceNode.OperatorType}.{match.SourcePortName}->{targetOperator}.{targetPort}:{required}");
            return true;
        }

        return required switch
        {
            VisionPortSemantics.JudgmentResult =>
                judgments.Count > 0 && resultOutputs.Count > 0 &&
                EdgeTo("ResultOutput", "Result", VisionPortSemantics.JudgmentResult, VisionPortSemantics.BooleanResult),
            VisionPortSemantics.PresenceCount =>
                EdgeTo("ResultJudgment", "Value", VisionPortSemantics.PresenceCount) &&
                EdgeTo("ResultOutput", "Data", VisionPortSemantics.PresenceCount, VisionPortSemantics.StructuredData),
            VisionPortSemantics.Label =>
                EdgeTo("ResultJudgment", "Value", VisionPortSemantics.Label),
            VisionPortSemantics.Confidence =>
                EdgeTo("ResultJudgment", "Confidence", VisionPortSemantics.Confidence),
            VisionPortSemantics.ClassificationDetails =>
                EdgeTo("ResultOutput", "Data", VisionPortSemantics.ClassificationDetails, VisionPortSemantics.Label),
            VisionPortSemantics.Detections =>
                EdgeTo("ResultOutput", "Data", VisionPortSemantics.Detections, VisionPortSemantics.StructuredData),
            VisionPortSemantics.ObjectCount =>
                EdgeTo("ResultJudgment", "Value", VisionPortSemantics.ObjectCount, VisionPortSemantics.PresenceCount),
            VisionPortSemantics.IsMatch =>
                EdgeTo("ResultJudgment", "Value", VisionPortSemantics.IsMatch),
            VisionPortSemantics.TemplateMatches =>
                EdgeTo("ResultOutput", "Data", VisionPortSemantics.TemplateMatches, VisionPortSemantics.TemplatePose),
            "defect_evidence" =>
                EdgeTo("ResultJudgment", "Value", VisionPortSemantics.DefectCount, VisionPortSemantics.DefectArea) &&
                EdgeTo("ResultOutput", "Data", VisionPortSemantics.DefectFeatures, VisionPortSemantics.DefectCount, VisionPortSemantics.DefectArea),
            VisionPortSemantics.DefectArea =>
                EdgeTo("ResultOutput", "Data", VisionPortSemantics.DefectArea) &&
                EdgeTo("ResultJudgment", "Value", VisionPortSemantics.DefectArea),
            VisionPortSemantics.MeasurementValue =>
                EdgeTo("ResultJudgment", "Value", VisionPortSemantics.MeasurementValue) &&
                EdgeTo("ResultOutput", "Data", VisionPortSemantics.MeasurementValue, VisionPortSemantics.MeasurementDetails, VisionPortSemantics.StructuredData),
            VisionPortSemantics.MeasurementUnit =>
                EdgeTo("Aggregator", "Value1", VisionPortSemantics.MeasurementValue) &&
                EdgeTo("Aggregator", "Value2", VisionPortSemantics.MeasurementUnit) &&
                EdgeTo(
                    "ResultOutput",
                    "Data",
                    VisionPortSemantics.MeasurementBundle,
                    VisionPortSemantics.StructuredData,
                    VisionPortSemantics.MeasurementDetails),
            VisionPortSemantics.SequenceDetails =>
                EdgeTo("ResultOutput", "Data", VisionPortSemantics.SequenceDetails),
            VisionPortSemantics.DecodedText =>
                EdgeTo("ResultOutput", "Text", VisionPortSemantics.DecodedText),
            VisionPortSemantics.CodeCount =>
                EdgeTo("ResultJudgment", "Value", VisionPortSemantics.CodeCount, VisionPortSemantics.PresenceCount),
            CodeAcceptanceSemantic =>
                EdgeTo(
                    "ResultJudgment",
                    "Value",
                    VisionPortSemantics.CodeCount,
                    VisionPortSemantics.PresenceCount,
                    VisionPortSemantics.DecodedText),
            VisionPortSemantics.CodeType =>
                EdgeTo("ResultOutput", "Data", VisionPortSemantics.CodeType, VisionPortSemantics.StructuredData),
            _ => false
        };
    }

    private static bool IsValidEndpoint(
        CanonicalWorkflowConnection connection,
        IReadOnlyDictionary<string, CanonicalWorkflowNode> nodeById)
    {
        return nodeById.TryGetValue(connection.SourceTempId, out var source) &&
               nodeById.TryGetValue(connection.TargetTempId, out var target) &&
               source.OutputPorts.Any(port => port.Name.Equals(connection.SourcePortName, StringComparison.OrdinalIgnoreCase)) &&
               target.InputPorts.Any(port => port.Name.Equals(connection.TargetPortName, StringComparison.OrdinalIgnoreCase));
    }

    private static HashSet<string> ReachableFrom(
        IEnumerable<string> startIds,
        IReadOnlyList<CanonicalWorkflowConnection> connections)
    {
        var reachable = startIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var pending = new Queue<string>(reachable);
        while (pending.Count > 0)
        {
            var current = pending.Dequeue();
            foreach (var connection in connections.Where(item => item.SourceTempId.Equals(current, StringComparison.OrdinalIgnoreCase)))
            {
                if (reachable.Add(connection.TargetTempId))
                {
                    pending.Enqueue(connection.TargetTempId);
                }
            }
        }

        return reachable;
    }

    private static HashSet<string> ReachableTo(
        IEnumerable<string> terminalIds,
        IReadOnlyList<CanonicalWorkflowConnection> connections)
    {
        var reachable = terminalIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var pending = new Queue<string>(reachable);
        while (pending.Count > 0)
        {
            var current = pending.Dequeue();
            foreach (var connection in connections.Where(item => item.TargetTempId.Equals(current, StringComparison.OrdinalIgnoreCase)))
            {
                if (reachable.Add(connection.SourceTempId))
                {
                    pending.Enqueue(connection.SourceTempId);
                }
            }
        }

        return reachable;
    }

    private static VisionTaskRouteAssessment Assessment(
        VisionTaskRouteContractDefinition contract,
        IEnumerable<string> reasons,
        IEnumerable<string> evidence,
        IReadOnlyCollection<string> requiredCapabilities,
        IReadOnlyCollection<string> matchedCapabilities,
        IReadOnlyCollection<string> requiredResults,
        IReadOnlyCollection<string> reachableResults,
        IReadOnlyCollection<string> reachedTerminals,
        bool scaffold)
    {
        var distinctReasons = reasons.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return new VisionTaskRouteAssessment
        {
            TaskType = contract.CanonicalTaskType,
            Supported = true,
            Satisfied = distinctReasons.Count == 0,
            SafeScaffold = scaffold,
            RequiresUserReview = distinctReasons.Count > 0,
            BlockingReasons = distinctReasons,
            Evidence = evidence.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            RequiredCapabilities = requiredCapabilities.ToList(),
            MatchedCapabilities = matchedCapabilities.ToList(),
            MissingCapabilities = matchedCapabilities.Count > 0
                ? []
                : requiredCapabilities.ToList(),
            RequiredResultSemantics = requiredResults.ToList(),
            ReachableResultSemantics = reachableResults.ToList(),
            MissingResultSemantics = requiredResults
                .Except(reachableResults, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            LegalTerminals = contract.LegalTerminals.ToList(),
            ReachedTerminals = reachedTerminals.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            ContractVersion = VisionAgentPlanContractVersions.V2
        };
    }

    private static VisionTaskRouteAssessment Blocked(string taskType, string code, string message) => new()
    {
        TaskType = taskType,
        Supported = false,
        Satisfied = false,
        RequiresUserReview = true,
        BlockingReasons = [code],
        Evidence = [message],
        ContractVersion = VisionAgentPlanContractVersions.V2
    };

    private static IReadOnlyList<VisionTaskRouteContractDefinition> BuildContracts()
    {
        return AiVisionTaskCatalog.PrimaryTasks.Select(task => task.CanonicalValue switch
        {
            AiVisionTaskTypes.PresenceAbsence => Contract(
                task,
                [["BlobAnalysis"], ["TemplateMatching"], ["DeepLearning"], ["SurfaceDefectDetection"]],
                [VisionPortSemantics.PresenceCount, VisionPortSemantics.JudgmentResult],
                [VisionPortSemantics.PresenceCount, VisionPortSemantics.BooleanResult, VisionPortSemantics.IsMatch]),
            AiVisionTaskTypes.AttributeClassification => Contract(
                task,
                [["DeepLearning"]],
                [VisionPortSemantics.Label, VisionPortSemantics.Confidence, VisionPortSemantics.ClassificationDetails, VisionPortSemantics.JudgmentResult],
                [VisionPortSemantics.Label]),
            AiVisionTaskTypes.ObjectDetection => Contract(
                task,
                [["DeepLearning"]],
                [VisionPortSemantics.Detections, VisionPortSemantics.ObjectCount, VisionPortSemantics.JudgmentResult],
                [VisionPortSemantics.ObjectCount, VisionPortSemantics.PresenceCount]),
            AiVisionTaskTypes.TemplateLocation => Contract(
                task,
                [["TemplateMatching"]],
                [VisionPortSemantics.IsMatch, VisionPortSemantics.TemplateMatches, VisionPortSemantics.Confidence, VisionPortSemantics.JudgmentResult],
                [VisionPortSemantics.IsMatch]),
            AiVisionTaskTypes.SurfaceDefect => Contract(
                task,
                [["SurfaceDefectDetection"], ["DeepLearning"], ["EdgeDetection", "BlobAnalysis"]],
                ["defect_evidence", VisionPortSemantics.JudgmentResult],
                [VisionPortSemantics.DefectCount, VisionPortSemantics.DefectArea]),
            AiVisionTaskTypes.GeometryMeasurement => Contract(
                task,
                [["Measurement"], ["CircleMeasurement"], ["LineMeasurement"], ["ContourMeasurement"], ["AngleMeasurement"], ["WidthMeasurement"], ["GapMeasurement"], ["ColorMeasurement"]],
                [VisionPortSemantics.MeasurementValue, VisionPortSemantics.MeasurementUnit, VisionPortSemantics.JudgmentResult],
                [VisionPortSemantics.MeasurementValue]),
            AiVisionTaskTypes.WireSequence => Contract(
                task,
                [["DeepLearning", "DetectionSequenceJudge"]],
                [VisionPortSemantics.IsMatch, VisionPortSemantics.SequenceDetails, VisionPortSemantics.JudgmentResult],
                [VisionPortSemantics.IsMatch]),
            AiVisionTaskTypes.CodeRecognition => Contract(
                task,
                [["CodeRecognition"]],
                [VisionPortSemantics.DecodedText, CodeAcceptanceSemantic, VisionPortSemantics.CodeType, VisionPortSemantics.JudgmentResult],
                [VisionPortSemantics.CodeCount, VisionPortSemantics.DecodedText]),
            _ => throw new InvalidOperationException($"Task '{task.CanonicalValue}' has no route v2 definition.")
        }).ToList();
    }

    private static VisionTaskRouteContractDefinition Contract(
        AiVisionTaskDescriptor task,
        List<List<string>> processorAlternatives,
        List<string> requiredResults,
        List<string> judgmentSemantics) => new()
    {
        CanonicalTaskType = task.CanonicalValue,
        RouteKey = task.RouteContractKey,
        ProcessorAlternatives = processorAlternatives,
        AllowedProcessors = processorAlternatives.SelectMany(item => item).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
        JudgmentValueSemantics = judgmentSemantics,
        RequiredResultSemantics = requiredResults,
        LegalTerminals = ["ResultOutput"],
        Aliases = task.Aliases.ToList(),
        ContractVersion = VisionAgentPlanContractVersions.V2
    };

    private static bool IsImageSource(string type) =>
        type.Equals("ImageAcquisition", StringComparison.OrdinalIgnoreCase);

    private static bool IsScaffold(IEnumerable<string> types)
    {
        var normalized = types.Where(type => !string.IsNullOrWhiteSpace(type)).ToList();
        return normalized.Count > 0 && normalized.All(type => type is "ImageAcquisition" or "ResultJudgment" or "ResultOutput");
    }

    private static string AlternativeKey(IEnumerable<string> alternative) =>
        string.Join("+", alternative);

    private static string NormalizePromisedSemantic(string semantic) => semantic.Trim().ToLowerInvariant() switch
    {
        "defect_area" => VisionPortSemantics.DefectArea,
        "decoded_text" => VisionPortSemantics.DecodedText,
        "template_pose_matches" => VisionPortSemantics.TemplateMatches,
        "measurement_value" => VisionPortSemantics.MeasurementValue,
        var value => value
    };

    private static string SafeKey(string value) =>
        new(value.Select(character => char.IsLetterOrDigit(character) ? char.ToLowerInvariant(character) : '_').ToArray());
}
