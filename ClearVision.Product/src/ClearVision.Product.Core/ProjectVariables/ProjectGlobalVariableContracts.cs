using System.Text.Json;

namespace ClearVision.Product.Core.ProjectVariables;

public enum ProjectGlobalVariableValueType
{
    String = 0,
    Int64 = 1,
    Double = 2,
    Boolean = 3
}

public enum ProjectVariableUpdatedBy
{
    Initial = 0,
    StudioManual = 1,
    StationManual = 2,
    OperatorOutput = 3,
    VariableWrite = 4,
    VariableIncrement = 5,
    Reset = 6
}

public sealed class ProjectGlobalVariableDefinition
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string? Description { get; set; }

    public ProjectGlobalVariableValueType ValueType { get; set; }

    public JsonElement InitialValue { get; set; } = JsonSerializer.SerializeToElement("");

    public double? Min { get; set; }

    public double? Max { get; set; }

    public bool ManualWriteAllowed { get; set; } = true;

    public bool IncludeInResultMetadata { get; set; }

    public int Order { get; set; }
}

public sealed class ProjectGlobalVariableSourceBinding
{
    public Guid Id { get; set; }

    public Guid VariableId { get; set; }

    public Guid OperatorId { get; set; }

    public Guid OutputPortId { get; set; }

    public string OperatorName { get; set; } = string.Empty;

    public string OutputPortName { get; set; } = string.Empty;
}

public sealed class ProjectGlobalVariableTargetBinding
{
    public Guid Id { get; set; }

    public Guid VariableId { get; set; }

    public Guid OperatorId { get; set; }

    public Guid ParameterId { get; set; }

    public string OperatorName { get; set; } = string.Empty;

    public string ParameterName { get; set; } = string.Empty;
}

public sealed class ProjectGlobalVariableSchema
{
    public string SchemaVersion { get; set; } = "1.0";

    public List<ProjectGlobalVariableDefinition> Variables { get; set; } = [];

    public List<ProjectGlobalVariableSourceBinding> SourceBindings { get; set; } = [];

    public List<ProjectGlobalVariableTargetBinding> TargetBindings { get; set; } = [];

    public static ProjectGlobalVariableSchema Empty { get; } = new();
}

public sealed record ProjectVariableValueSnapshot(
    Guid VariableId,
    JsonElement Value,
    long Version,
    DateTimeOffset UpdatedAtUtc,
    ProjectVariableUpdatedBy UpdatedBy,
    Guid? RunId,
    Guid? OperatorId);

public sealed record ProjectGlobalVariableDiagnostic(
    string Code,
    string Message,
    Guid? VariableId = null,
    Guid? OperatorId = null,
    Guid? PortId = null,
    Guid? ParameterId = null);
