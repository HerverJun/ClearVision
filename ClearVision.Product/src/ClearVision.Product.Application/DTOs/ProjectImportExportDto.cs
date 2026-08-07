using System.Text.Json;
using System.Text.Json.Serialization;

namespace ClearVision.Product.Application.DTOs;

public static class ProjectJsonContract
{
    public const string DocumentType = "clearvision-project";
    public const int SchemaVersion = 1;

    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static string Serialize(ProjectExportDocumentV1 document) =>
        JsonSerializer.Serialize(document, Options);

    public static ProjectExportDocumentV1 Deserialize(string json) =>
        JsonSerializer.Deserialize<ProjectExportDocumentV1>(json, Options)
        ?? throw new JsonException("Project export document is empty.");
}

public enum ProjectImportMode
{
    CreateNew = 0,
    OverwriteExisting = 1
}

public sealed class ProjectExportDocumentV1
{
    public string DocumentType { get; set; } = ProjectJsonContract.DocumentType;

    public int SchemaVersion { get; set; } = ProjectJsonContract.SchemaVersion;

    public ProjectExportIdentityV1 Identity { get; set; } = new();

    public ProjectExportMetadataV1 Project { get; set; } = new();

    public OperatorFlowDto Flow { get; set; } = new();

    public ClearVision.Product.Core.ProjectVariables.ProjectGlobalVariableSchema GlobalVariables { get; set; } = new();

    public ProjectAssetsDto Assets { get; set; } = new();
}

public sealed class ProjectExportIdentityV1
{
    public Guid? SourceProjectId { get; set; }

    public long SourcePersistenceRevision { get; set; }
}

public sealed class ProjectExportMetadataV1
{
    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public string Version { get; set; } = "1.0.0";
}

public sealed class ProjectImportRequest
{
    public string Mode { get; set; } = string.Empty;

    public Guid ClientOperationId { get; set; }

    public Guid? TargetProjectId { get; set; }

    public long? ExpectedPersistenceRevision { get; set; }

    public ProjectExportDocumentV1? Document { get; set; }
}

public sealed class ProjectImportExecutionPayload
{
    public ProjectImportMode Mode { get; set; }

    public long? ExpectedPersistenceRevision { get; set; }

    public ProjectExportDocumentV1 Document { get; set; } = new();
}

public sealed record ProjectImportCommandResult(
    Guid ProjectId,
    ProjectDto Project,
    bool OperationReplayed,
    ProjectLifecycleOperationDto Operation);
