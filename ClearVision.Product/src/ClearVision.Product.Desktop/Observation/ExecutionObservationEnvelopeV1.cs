using System.Text.Json.Serialization;
using ClearVision.Product.Desktop.PreviewArtifacts;

namespace ClearVision.Product.Desktop.Observation;

public sealed class ExecutionObservationEnvelopeV1
{
    [JsonPropertyOrder(0)]
    public string SchemaVersion { get; init; } = "execution-observation.v1";

    [JsonPropertyOrder(1)]
    public DateTimeOffset ObservedAtUtc { get; init; }

    [JsonPropertyOrder(2)]
    public ExecutionObservationIdentityV1 Identity { get; init; } = new();

    [JsonPropertyOrder(3)]
    public ExecutionObservationOutcomeV1 Outcome { get; init; } = new();

    [JsonPropertyOrder(4)]
    public List<ExecutionObservationSummaryItemV1> Summary { get; init; } = new();

    [JsonPropertyOrder(5)]
    public ExecutionObservationDetailNodeV1 Detail { get; init; } = new();

    [JsonPropertyOrder(6)]
    public List<ExecutionObservationDiagnosticV1> Diagnostics { get; init; } = new();

    [JsonPropertyOrder(7)]
    public ExecutionObservationLimitsV1 Limits { get; init; } = new();

    [JsonPropertyOrder(8)]
    public bool Truncated { get; set; }

    [JsonPropertyOrder(9)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ExecutionVisualSceneV1? VisualScene { get; init; }
}

public sealed class ExecutionObservationIdentityV1
{
    [JsonPropertyOrder(0)]
    public Guid ProjectId { get; init; }

    [JsonPropertyOrder(1)]
    public Guid TargetNodeId { get; init; }

    [JsonPropertyOrder(2)]
    public Guid DebugSessionId { get; init; }

    [JsonPropertyOrder(3)]
    public long? ClientRequestSequence { get; init; }

    [JsonPropertyOrder(4)]
    public long? FlowRevision { get; init; }

    [JsonPropertyOrder(5)]
    public Guid? RunId { get; init; }
}

public sealed class ExecutionObservationOutcomeV1
{
    [JsonPropertyOrder(0)]
    public bool Success { get; init; }

    [JsonPropertyOrder(1)]
    public long ExecutionTimeMs { get; init; }

    [JsonPropertyOrder(2)]
    public string? ErrorMessage { get; init; }

    [JsonPropertyOrder(3)]
    public Guid? FailedOperatorId { get; init; }

    [JsonPropertyOrder(4)]
    public string? FailedOperatorName { get; init; }

    [JsonPropertyOrder(5)]
    public string? FailedOperatorType { get; init; }

    [JsonPropertyOrder(6)]
    public int ExecutedOperatorCount { get; init; }
}

public sealed class ExecutionObservationSummaryItemV1
{
    [JsonPropertyOrder(0)]
    public string Key { get; init; } = string.Empty;

    [JsonPropertyOrder(1)]
    public string DisplayValue { get; init; } = string.Empty;

    [JsonPropertyOrder(2)]
    public string? OriginalType { get; init; }

    [JsonPropertyOrder(3)]
    public string PathHint { get; init; } = "$";

    [JsonPropertyOrder(4)]
    public bool Addressable { get; init; }

    [JsonPropertyOrder(5)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Guid? OutputPortId { get; init; }

    [JsonPropertyOrder(6)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? OutputPortName { get; init; }

    [JsonPropertyOrder(7)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? ResultPathVersion { get; init; }

    [JsonPropertyOrder(8)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ResultPath { get; init; }

    [JsonPropertyOrder(9)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? BindableVariableTypes { get; init; }
}

public sealed class ExecutionObservationDetailNodeV1
{
    [JsonPropertyOrder(0)]
    public string Kind { get; init; } = "unknown";

    [JsonPropertyOrder(1)]
    public string? DisplayValue { get; init; }

    [JsonPropertyOrder(2)]
    public string? OriginalType { get; init; }

    [JsonPropertyOrder(3)]
    public List<ExecutionObservationDetailNodeV1> Children { get; init; } = new();

    [JsonPropertyOrder(4)]
    public bool Truncated { get; set; }

    [JsonPropertyOrder(5)]
    public string PathHint { get; init; } = "$";

    [JsonPropertyOrder(6)]
    public bool Addressable { get; init; }

    [JsonPropertyOrder(7)]
    public string? Name { get; init; }

    [JsonPropertyOrder(8)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public PreviewArtifactReferenceV1? Artifact { get; init; }

    [JsonPropertyOrder(9)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Guid? OutputPortId { get; init; }

    [JsonPropertyOrder(10)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? OutputPortName { get; init; }

    [JsonPropertyOrder(11)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? ResultPathVersion { get; init; }

    [JsonPropertyOrder(12)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ResultPath { get; init; }

    [JsonPropertyOrder(13)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? BindableVariableTypes { get; init; }
}

public sealed class ExecutionObservationDiagnosticV1
{
    [JsonPropertyOrder(0)]
    public string Code { get; init; } = string.Empty;

    [JsonPropertyOrder(1)]
    public string Message { get; init; } = string.Empty;

    [JsonPropertyOrder(2)]
    public string PathHint { get; init; } = "$";
}

public sealed class ExecutionObservationLimitsV1
{
    [JsonPropertyOrder(0)]
    public int MaxDepth { get; init; } = ExecutionObservationProjector.MaxDepth;

    [JsonPropertyOrder(1)]
    public int MaxObjectFields { get; init; } = ExecutionObservationProjector.MaxObjectFields;

    [JsonPropertyOrder(2)]
    public int MaxCollectionItems { get; init; } = ExecutionObservationProjector.MaxCollectionItems;

    [JsonPropertyOrder(3)]
    public int MaxStringChars { get; init; } = ExecutionObservationProjector.MaxStringChars;

    [JsonPropertyOrder(4)]
    public int MaxNodes { get; init; } = ExecutionObservationProjector.MaxNodes;

    [JsonPropertyOrder(5)]
    public int MaxDetailBytes { get; init; } = ExecutionObservationProjector.MaxDetailBytes;
}

public sealed class ExecutionObservationPreviewInput
{
    public Guid ProjectId { get; init; }
    public Guid TargetNodeId { get; init; }
    public Guid DebugSessionId { get; init; }
    public long? ClientRequestSequence { get; init; }
    public long? FlowRevision { get; init; }
    public bool Success { get; init; }
    public long ExecutionTimeMs { get; init; }
    public string? ErrorMessage { get; init; }
    public Guid? FailedOperatorId { get; init; }
    public string? FailedOperatorName { get; init; }
    public string? FailedOperatorType { get; init; }
    public int ExecutedOperatorCount { get; init; }
    public IReadOnlyDictionary<string, object>? OutputData { get; init; }
    public IReadOnlyList<ExecutionObservationOutputPortV1> OutputPorts { get; init; } = [];
    public ClearVision.Product.Core.Entities.Operator? TargetOperator { get; init; }
    public DateTimeOffset? ObservedAtUtc { get; init; }
}

public sealed class ExecutionObservationOutputPortV1
{
    public Guid Id { get; init; }

    public string Name { get; init; } = string.Empty;
}

public sealed class ExecutionVisualSceneV1
{
    [JsonPropertyOrder(0)]
    public string SchemaVersion { get; init; } = "visual-scene.v1";

    [JsonPropertyOrder(1)]
    public string CoordinateSpace { get; init; } = "image.pixel";

    [JsonPropertyOrder(2)]
    public int ImageWidth { get; init; }

    [JsonPropertyOrder(3)]
    public int ImageHeight { get; init; }

    [JsonPropertyOrder(4)]
    public List<ExecutionVisualScenePrimitiveV1> Primitives { get; init; } = new();

    [JsonPropertyOrder(5)]
    public List<ExecutionVisualSceneDiagnosticV1> Diagnostics { get; init; } = new();

    [JsonPropertyOrder(6)]
    public bool Truncated { get; init; }
}

public sealed class ExecutionVisualScenePrimitiveV1
{
    [JsonPropertyOrder(0)]
    public string PrimitiveId { get; init; } = string.Empty;

    [JsonPropertyOrder(1)]
    public string Kind { get; init; } = string.Empty;

    [JsonPropertyOrder(2)]
    public string Layer { get; init; } = "measurement";

    [JsonPropertyOrder(3)]
    public int ZOrder { get; init; }

    [JsonPropertyOrder(4)]
    public bool Visible { get; init; } = true;

    [JsonPropertyOrder(5)]
    public bool Selectable { get; init; } = true;

    [JsonPropertyOrder(6)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Label { get; init; }

    [JsonPropertyOrder(7)]
    public ExecutionVisualSceneGeometryV1 Geometry { get; init; } = new();

    [JsonPropertyOrder(8)]
    public ExecutionVisualSceneStyleV1 Style { get; init; } = new();

    [JsonPropertyOrder(9)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Guid? OutputPortId { get; init; }

    [JsonPropertyOrder(10)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? ResultPathVersion { get; init; }

    [JsonPropertyOrder(11)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ResultPath { get; init; }
}

public sealed class ExecutionVisualSceneGeometryV1
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? X { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? Y { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? Width { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? Height { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? CenterX { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? CenterY { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? Radius { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<ExecutionVisualScenePointV1>? Points { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Text { get; init; }
}

public sealed class ExecutionVisualScenePointV1
{
    public double X { get; init; }

    public double Y { get; init; }
}

public sealed class ExecutionVisualSceneStyleV1
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Stroke { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Fill { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? StrokeWidth { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? FontSize { get; init; }
}

public sealed class ExecutionVisualSceneDiagnosticV1
{
    [JsonPropertyOrder(0)]
    public string Code { get; init; } = string.Empty;

    [JsonPropertyOrder(1)]
    public string Message { get; init; } = string.Empty;

    [JsonPropertyOrder(2)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PrimitiveId { get; init; }
}
