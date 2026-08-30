using System.Text.Json;
using ClearVision.Product.Application.DTOs;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Exceptions;
using ClearVision.Product.Core.Interfaces;
using ClearVision.Product.Core.ProjectVariables;
using ClearVision.Product.Core.Services;

namespace ClearVision.Product.Application.Services;

/// <summary>
/// The single authority for revisioned mutations of an existing project.
/// Project access is acquired before runtime mutation access and is retained
/// until the authoritative candidate has either committed or failed.
/// </summary>
public sealed class ProjectMutationAuthority
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    private readonly IProjectRepository _projectRepository;
    private readonly IProjectFlowStorage _flowStorage;
    private readonly ProjectSaveCoordinator _saveCoordinator;
    private readonly IInspectionRuntimeCoordinator? _runtimeCoordinator;
    private readonly IProjectAssetStorage? _projectAssetStorage;

    public ProjectMutationAuthority(
        IProjectRepository projectRepository,
        IProjectFlowStorage flowStorage,
        ProjectSaveCoordinator saveCoordinator,
        IInspectionRuntimeCoordinator? runtimeCoordinator = null,
        IProjectAssetStorage? projectAssetStorage = null)
    {
        _projectRepository = projectRepository ?? throw new ArgumentNullException(nameof(projectRepository));
        _flowStorage = flowStorage ?? throw new ArgumentNullException(nameof(flowStorage));
        _saveCoordinator = saveCoordinator ?? throw new ArgumentNullException(nameof(saveCoordinator));
        _runtimeCoordinator = runtimeCoordinator;
        _projectAssetStorage = projectAssetStorage;
    }

    public async Task<ProjectMutationResult> MutateAsync(
        Guid projectId,
        long expectedPersistenceRevision,
        ProjectMutationPatch patch,
        Func<OperatorFlowDto, ProjectGlobalVariableSchema, OperatorFlowDto>? prepareExplicitFlow = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(patch);
        if (!patch.HasAnyPatch)
        {
            throw new InvalidOperationException("PMU002: at least one project patch field is required.");
        }

        if (expectedPersistenceRevision < 0)
        {
            throw new InvalidOperationException("PMU003: expectedPersistenceRevision must be a non-negative integer.");
        }

        await using var projectAccess = await _saveCoordinator.AcquireProjectAccessAsync(projectId, cancellationToken);
        var project = await _projectRepository.GetByIdForUpdateAsync(projectId)
            ?? throw new ProjectNotFoundException(projectId);
        if (project.PersistenceRevision != expectedPersistenceRevision)
        {
            throw new InvalidOperationException(
                $"PSV011: stale project save request. Expected={expectedPersistenceRevision}, Current={project.PersistenceRevision}.");
        }

        // Load every project participant while the same project access lease is held.
        var authoritativeFlowJson = await _flowStorage.LoadFlowJsonAsync(projectId);
        var authoritativeFlowMetadata = await _flowStorage.LoadMetadataAsync(projectId);
        var authoritativeFlow = DeserializeFlow(authoritativeFlowJson);
        var authoritativeSchema = CloneSchema(project.GlobalVariables);
        var authoritativeAssets = _projectAssetStorage == null
            ? new ProjectAssetsDto()
            : await _projectAssetStorage.LoadAssetsAsync(projectId);
        var authoritativeAssetMetadata = _projectAssetStorage == null
            ? null
            : await _projectAssetStorage.LoadMetadataAsync(projectId);

        var nextName = patch.Name.IsSpecified ? patch.Name.Value : project.Name;
        var nextDescription = patch.Description.IsSpecified ? patch.Description.Value : project.Description;
        if (string.IsNullOrWhiteSpace(nextName))
        {
            throw new InvalidOperationException("PMU004: project name cannot be empty.");
        }

        var nextSchema = patch.GlobalVariables.IsSpecified
            ? CloneSchema(patch.GlobalVariables.Value ?? throw new InvalidOperationException("PMU005: global-variable schema is required."))
            : authoritativeSchema;

        OperatorFlowDto? nextFlow = authoritativeFlow;
        string? nextFlowJson = null;
        var flowChanged = false;
        if (patch.Flow.IsSpecified)
        {
            var explicitFlow = CloneFlow(
                patch.Flow.Value ?? throw new InvalidOperationException("PMU006: flow patch cannot be null."));
            if (string.IsNullOrWhiteSpace(explicitFlow.Name))
            {
                explicitFlow.Name = !string.IsNullOrWhiteSpace(authoritativeFlow?.Name)
                    ? authoritativeFlow.Name
                    : (!string.IsNullOrWhiteSpace(project.Flow?.Name) ? project.Flow.Name : "MainFlow");
            }

            explicitFlow.DecisionConfiguration ??=
                authoritativeFlow?.DecisionConfiguration ?? project.Flow?.DecisionConfiguration;
            nextFlow = prepareExplicitFlow == null
                ? explicitFlow
                : prepareExplicitFlow(explicitFlow, nextSchema);
            nextFlowJson = JsonSerializer.Serialize(nextFlow, JsonOptions);
            flowChanged = !FlowsEqual(authoritativeFlow, nextFlow);
            if (!flowChanged)
            {
                nextFlowJson = null;
            }
        }

        if (patch.Flow.IsSpecified || patch.GlobalVariables.IsSpecified)
        {
            ProjectGlobalVariableSchemaValidator.ThrowIfInvalid(nextSchema, nextFlow?.ToEntity());
        }

        var schemaChanged = !string.Equals(
            ProjectGlobalVariableSchemaValidator.ComputeSchemaHash(authoritativeSchema),
            ProjectGlobalVariableSchemaValidator.ComputeSchemaHash(nextSchema),
            StringComparison.Ordinal);
        var metadataChanged =
            !string.Equals(project.Name, nextName, StringComparison.Ordinal) ||
            !string.Equals(project.Description, nextDescription, StringComparison.Ordinal);
        var diff = new ProjectMutationDiff(metadataChanged, flowChanged, schemaChanged, AssetsChanged: false);

        ProjectMutationLease? runtimeLease = null;
        try
        {
            // The lease decision is based on the authoritative candidate diff,
            // not on whether a request happened to contain a field.
            if (diff.RequiresRuntimeMutationLease && _runtimeCoordinator != null)
            {
                runtimeLease = await _runtimeCoordinator.TryAcquireMutationLeaseAsync(
                    projectId,
                    patch.Reason,
                    cancellationToken);
                if (runtimeLease == null)
                {
                    throw new InvalidOperationException("PMU001: project is currently running.");
                }
            }

            if (!diff.HasChanges)
            {
                return new ProjectMutationResult(
                    project,
                    authoritativeFlow,
                    authoritativeAssets,
                    authoritativeFlowMetadata,
                    authoritativeAssetMetadata,
                    diff,
                    Changed: false);
            }

            var saved = await _saveCoordinator.SaveExistingProjectUnderProjectAccessAsync(
                projectAccess,
                new ProjectSaveRequest(
                    project,
                    expectedPersistenceRevision,
                    nextName!,
                    nextDescription,
                    authoritativeSchema,
                    nextSchema,
                    authoritativeFlowJson,
                    nextFlow,
                    nextFlowJson));

            return new ProjectMutationResult(
                saved.Project,
                nextFlow,
                authoritativeAssets,
                authoritativeFlowMetadata,
                authoritativeAssetMetadata,
                diff,
                saved.Changed);
        }
        finally
        {
            if (runtimeLease != null)
            {
                await runtimeLease.DisposeAsync();
            }
        }
    }

    private static OperatorFlowDto? DeserializeFlow(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        return JsonSerializer.Deserialize<OperatorFlowDto>(json, JsonOptions)
            ?? throw new InvalidDataException("PMU007: authoritative project flow could not be deserialized.");
    }

    private static bool FlowsEqual(OperatorFlowDto? left, OperatorFlowDto? right)
    {
        if (left == null || right == null)
        {
            return left == null && right == null;
        }

        return string.Equals(
            JsonSerializer.Serialize(left, JsonOptions),
            JsonSerializer.Serialize(right, JsonOptions),
            StringComparison.Ordinal);
    }

    private static OperatorFlowDto CloneFlow(OperatorFlowDto flow)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(flow, JsonOptions);
        return JsonSerializer.Deserialize<OperatorFlowDto>(bytes, JsonOptions)
            ?? throw new InvalidOperationException("PMU007: flow patch could not be cloned.");
    }

    private static ProjectGlobalVariableSchema CloneSchema(ProjectGlobalVariableSchema schema)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(schema, JsonOptions);
        return JsonSerializer.Deserialize<ProjectGlobalVariableSchema>(bytes, JsonOptions)
            ?? new ProjectGlobalVariableSchema();
    }
}

public sealed record ProjectMutationPatch(
    ProjectPatchValue<string> Name,
    ProjectPatchValue<string?> Description,
    ProjectPatchValue<OperatorFlowDto> Flow,
    ProjectPatchValue<ProjectGlobalVariableSchema> GlobalVariables,
    string Reason)
{
    public bool HasAnyPatch =>
        Name.IsSpecified || Description.IsSpecified || Flow.IsSpecified || GlobalVariables.IsSpecified;

    public static ProjectMutationPatch Metadata(
        ProjectPatchValue<string> name,
        ProjectPatchValue<string?> description) =>
        new(name, description, ProjectPatchValue<OperatorFlowDto>.Absent(),
            ProjectPatchValue<ProjectGlobalVariableSchema>.Absent(), "project-metadata-patch");

    public static ProjectMutationPatch FlowOnly(OperatorFlowDto flow) =>
        new(ProjectPatchValue<string>.Absent(), ProjectPatchValue<string?>.Absent(),
            ProjectPatchValue<OperatorFlowDto>.Present(flow),
            ProjectPatchValue<ProjectGlobalVariableSchema>.Absent(), "project-flow-patch");

    public static ProjectMutationPatch GlobalVariableSchema(ProjectGlobalVariableSchema schema) =>
        new(ProjectPatchValue<string>.Absent(), ProjectPatchValue<string?>.Absent(),
            ProjectPatchValue<OperatorFlowDto>.Absent(),
            ProjectPatchValue<ProjectGlobalVariableSchema>.Present(schema), "project-global-variable-schema-patch");
}

public readonly record struct ProjectPatchValue<T>(bool IsSpecified, T? Value)
{
    public static ProjectPatchValue<T> Absent() => new(false, default);

    public static ProjectPatchValue<T> Present(T? value) => new(true, value);
}

public sealed record ProjectMutationDiff(
    bool MetadataChanged,
    bool FlowChanged,
    bool GlobalVariablesChanged,
    bool AssetsChanged)
{
    public bool HasChanges => MetadataChanged || FlowChanged || GlobalVariablesChanged || AssetsChanged;

    public bool RequiresRuntimeMutationLease => FlowChanged || GlobalVariablesChanged || AssetsChanged;
}

public sealed record ProjectMutationResult(
    Project Project,
    OperatorFlowDto? Flow,
    ProjectAssetsDto Assets,
    ProjectFlowStorageMetadata? FlowMetadata,
    ProjectAssetStorageMetadata? AssetMetadata,
    ProjectMutationDiff Diff,
    bool Changed);
