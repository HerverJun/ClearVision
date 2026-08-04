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
    public string ContractVersion { get; init; } = "v1";
}

public sealed class VisionTaskRouteContractRegistry
{
    private static readonly IReadOnlySet<string> SupportedTaskTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "presence_detection",
        "attribute_classification",
        "object_detection",
        "template_matching",
        "surface_defect_detection",
        "measurement",
        "sequence_judgment"
    };

    private static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> ProcessingOperators =
        new Dictionary<string, IReadOnlySet<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["presence_detection"] = Set("BlobAnalysis", "TemplateMatching", "DeepLearning", "SurfaceDefectDetection", "Thresholding", "ColorDetection"),
            ["attribute_classification"] = Set("DeepLearning", "BlobAnalysis", "Thresholding", "ColorDetection", "TemplateMatching"),
            ["object_detection"] = Set("DeepLearning", "BlobAnalysis", "SurfaceDefectDetection", "TemplateMatching"),
            ["template_matching"] = Set("TemplateMatching"),
            ["surface_defect_detection"] = Set("SurfaceDefectDetection", "DeepLearning", "BlobAnalysis", "Thresholding"),
            ["measurement"] = Set("Measurement", "CircleMeasurement", "LineMeasurement", "ContourMeasurement", "AngleMeasurement", "UnitConvert"),
            ["sequence_judgment"] = Set("DetectionSequenceJudge")
        };

    public VisionTaskRouteAssessment Assess(
        string? taskType,
        IReadOnlyList<VisionAgentOperatorPipelineStep> pipeline)
    {
        var normalized = NormalizeTaskType(taskType);
        if (!SupportedTaskTypes.Contains(normalized))
        {
            return Blocked(
                normalized,
                "unsupported_task_route_contract",
                "The requested task type has no registered route contract.");
        }

        var types = pipeline
            .Select(item => item.OperatorType?.Trim() ?? string.Empty)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .ToList();
        var assessment = AssessOperators(normalized, types, null);
        if (types.Count > 1)
        {
            assessment.BlockingReasons.Add("route_graph_unverified");
            assessment.Evidence.Add("pipeline_only_assessment");
            assessment = assessment with
            {
                Satisfied = false,
                RequiresUserReview = true
            };
        }

        return assessment;
    }

    internal VisionTaskRouteAssessment Assess(
        string? taskType,
        CanonicalWorkflowGraph graph)
    {
        var normalized = NormalizeTaskType(taskType);
        if (!SupportedTaskTypes.Contains(normalized))
        {
            return Blocked(
                normalized,
                "unsupported_task_route_contract",
                "The requested task type has no registered route contract.");
        }

        return AssessGraph(normalized, graph);
    }

    public static string NormalizeTaskType(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant() ?? string.Empty;
        return normalized switch
        {
            AiVisionTaskTypes.PresenceAbsence or "presence" or "presence_detection" => "presence_detection",
            AiVisionTaskTypes.Classification or AiVisionTaskTypes.AttributeClassification or "attribute" => "attribute_classification",
            "object_detection" or "detection" or "object" => "object_detection",
            AiVisionTaskTypes.TemplateLocation or "template_matching" or "template_match" => "template_matching",
            AiVisionTaskTypes.SurfaceDefect or AiVisionTaskTypes.SurfaceOrPoseDefect or "surface_defect_detection" => "surface_defect_detection",
            AiVisionTaskTypes.GeometryMeasurement or "measurement" or "measure" => "measurement",
            AiVisionTaskTypes.WireSequence or "sequence" or "sequence_judgment" => "sequence_judgment",
            _ => normalized
        };
    }

    private static VisionTaskRouteAssessment AssessGraph(
        string taskType,
        CanonicalWorkflowGraph graph)
    {
        var normalizedTypes = graph.Nodes
            .Select(node => node.OperatorType.Trim())
            .Where(type => !string.IsNullOrWhiteSpace(type))
            .ToList();
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
        }

        var sources = graph.Nodes.Where(node => IsImageSource(node.OperatorType)).ToList();
        if (sources.Count == 0)
        {
            reasons.Add("route_missing_image_source");
        }
        else
        {
            evidence.Add("image_source_present");
        }

        var requiredProcessors = ProcessingOperators[taskType];
        var processors = graph.Nodes
            .Where(node => requiredProcessors.Contains(node.OperatorType))
            .ToList();
        if (processors.Count == 0)
        {
            reasons.Add("route_missing_task_processor");
        }
        else
        {
            evidence.Add($"task_processor:{string.Join(",", processors.Select(node => node.OperatorType).Distinct(StringComparer.OrdinalIgnoreCase))}");
        }

        var terminals = graph.Nodes.Where(node => IsTerminal(node.OperatorType)).ToList();
        if (terminals.Count == 0)
        {
            reasons.Add("route_missing_result_path");
        }
        else
        {
            evidence.Add("result_path_present");
        }

        var validConnections = graph.Connections
            .Where(connection =>
                nodeById.ContainsKey(connection.SourceTempId) &&
                nodeById.ContainsKey(connection.TargetTempId))
            .ToList();
        if (graph.Nodes.Count > 1 && validConnections.Count == 0)
        {
            reasons.Add("route_missing_connections");
        }

        foreach (var connection in graph.Connections)
        {
            if (!nodeById.TryGetValue(connection.SourceTempId, out var source) ||
                !nodeById.TryGetValue(connection.TargetTempId, out var target) ||
                !source.OutputPorts.Any(port => port.Name.Equals(connection.SourcePortName, StringComparison.OrdinalIgnoreCase)) ||
                !target.InputPorts.Any(port => port.Name.Equals(connection.TargetPortName, StringComparison.OrdinalIgnoreCase)))
            {
                reasons.Add("route_invalid_connection_endpoint");
                break;
            }
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
                    port.DataType.ToString());
                if (!string.IsNullOrWhiteSpace(resourceType))
                {
                    evidence.Add($"resource_input_pending:{node.TempId}.{port.Name}:{resourceType}");
                    continue;
                }

                if (!incomingPorts.Contains($"{node.TempId}|{port.Name}"))
                {
                    reasons.Add("route_required_input_unbound");
                    break;
                }
            }
        }

        var reachableFromSource = ReachableFromSources(sources, validConnections);
        var reachableToTerminal = ReachableToTerminals(terminals, validConnections);
        var processorOnResultPath = processors.Any(node =>
            reachableFromSource.Contains(node.TempId) &&
            reachableToTerminal.Contains(node.TempId));
        if (processors.Count > 0 && !processorOnResultPath)
        {
            reasons.Add("route_task_processor_not_on_result_path");
        }

        if (terminals.Count > 0 && !terminals.Any(node => reachableFromSource.Contains(node.TempId)))
        {
            reasons.Add("route_result_not_reachable_from_source");
        }

        var scaffold = normalizedTypes.Count > 0 &&
                       normalizedTypes.All(type => type is "ImageAcquisition" or "ResultJudgment" or "ResultOutput");
        if (scaffold)
        {
            reasons.Add("minimum_scaffold_task_incomplete");
            evidence.Add("safe_scaffold");
        }

        var distinctReasons = reasons.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return new VisionTaskRouteAssessment
        {
            TaskType = taskType,
            Supported = true,
            Satisfied = distinctReasons.Count == 0,
            SafeScaffold = scaffold,
            RequiresUserReview = distinctReasons.Count > 0,
            BlockingReasons = distinctReasons,
            Evidence = evidence,
            ContractVersion = "v1"
        };
    }

    private static VisionTaskRouteAssessment AssessOperators(
        string taskType,
        IReadOnlyList<string> operatorTypes,
        IReadOnlyList<CanonicalWorkflowConnection>? connections)
    {
        var normalizedTypes = operatorTypes
            .Select(type => type.Trim())
            .Where(type => !string.IsNullOrWhiteSpace(type))
            .ToList();
        var reasons = new List<string>();
        var evidence = new List<string>();
        if (normalizedTypes.Any(IsImageSource))
        {
            evidence.Add("image_source_present");
        }
        else
        {
            reasons.Add("route_missing_image_source");
        }

        var processors = normalizedTypes.Where(ProcessingOperators[taskType].Contains).ToList();
        if (processors.Count == 0)
        {
            reasons.Add("route_missing_task_processor");
        }

        if (normalizedTypes.Any(IsTerminal))
        {
            evidence.Add("result_path_present");
        }
        else
        {
            reasons.Add("route_missing_result_path");
        }

        if (connections == null && normalizedTypes.Count > 1)
        {
            reasons.Add("route_graph_unverified");
        }

        var scaffold = normalizedTypes.Count > 0 &&
                       normalizedTypes.All(type => type is "ImageAcquisition" or "ResultJudgment" or "ResultOutput");
        if (scaffold)
        {
            reasons.Add("minimum_scaffold_task_incomplete");
            evidence.Add("safe_scaffold");
        }

        var distinctReasons = reasons.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return new VisionTaskRouteAssessment
        {
            TaskType = taskType,
            Supported = true,
            Satisfied = distinctReasons.Count == 0,
            SafeScaffold = scaffold,
            RequiresUserReview = distinctReasons.Count > 0,
            BlockingReasons = distinctReasons,
            Evidence = evidence,
            ContractVersion = "v1"
        };
    }

    private static HashSet<string> ReachableFromSources(
        IReadOnlyList<CanonicalWorkflowNode> sources,
        IReadOnlyList<CanonicalWorkflowConnection> connections)
    {
        var reachable = sources
            .Select(source => source.TempId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
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

    private static HashSet<string> ReachableToTerminals(
        IReadOnlyList<CanonicalWorkflowNode> terminals,
        IReadOnlyList<CanonicalWorkflowConnection> connections)
    {
        var reachable = terminals
            .Select(terminal => terminal.TempId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
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

    private static bool IsImageSource(string type) =>
        type.Equals("ImageAcquisition", StringComparison.OrdinalIgnoreCase);

    private static bool IsTerminal(string type) => type is
        "ResultOutput" or
        "ResultJudgment" or
        "DetectionSequenceJudge" or
        "Measurement" or
        "CircleMeasurement" or
        "LineMeasurement" or
        "ContourMeasurement" or
        "AngleMeasurement" or
        "TemplateMatching" or
        "BlobAnalysis" or
        "DeepLearning" or
        "SurfaceDefectDetection";

    private static VisionTaskRouteAssessment Blocked(
        string taskType,
        string code,
        string message)
    {
        return new VisionTaskRouteAssessment
        {
            TaskType = taskType,
            Supported = false,
            Satisfied = false,
            RequiresUserReview = true,
            BlockingReasons = [code],
            Evidence = [message],
            ContractVersion = "v1"
        };
    }

    private static IReadOnlySet<string> Set(params string[] values) =>
        values.ToHashSet(StringComparer.OrdinalIgnoreCase);
}
