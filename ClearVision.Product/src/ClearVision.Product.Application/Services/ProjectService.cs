// ProjectService.cs
// 将 OperatorConnection 值对象映射为 DTO
// 作者：蘅芜君

using System.Text.Json;
using ClearVision.Product.Application.DTOs;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Exceptions;
using ClearVision.Product.Core.Interfaces;
using ClearVision.Product.Core.ProjectVariables;
using ClearVision.Product.Core.Services;
using ClearVision.Product.Core.ValueObjects;
using Microsoft.Extensions.Logging;

namespace ClearVision.Product.Application.Services;

public interface IProjectApplicationService
{
    Task<ProjectDto?> GetByIdAsync(Guid id);
}

/// <summary>
/// 工程应用服务
/// </summary>
public class ProjectService : IProjectApplicationService
{
    private readonly IProjectRepository _projectRepository;
    private readonly IProjectFlowStorage _flowStorage;
    private readonly IOperatorFactory _operatorFactory;
    private readonly ILogger<ProjectService>? _logger;
    private readonly ProjectVariableSessionRegistry? _projectVariableSessions;
    private readonly ProjectSaveCoordinator _saveCoordinator;
    private readonly IProjectAssetStorage? _projectAssetStorage;
    private readonly IWorkflowArtifactAdmissionGate? _workflowArtifactAdmissionGate;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    public ProjectService(IProjectRepository projectRepository, IProjectFlowStorage flowStorage, IOperatorFactory operatorFactory)
        : this(projectRepository, flowStorage, operatorFactory, null)
    {
    }

    public ProjectService(
        IProjectRepository projectRepository,
        IProjectFlowStorage flowStorage,
        IOperatorFactory operatorFactory,
        ILogger<ProjectService>? logger)
        : this(projectRepository, flowStorage, operatorFactory, logger, null)
    {
    }

    public ProjectService(
        IProjectRepository projectRepository,
        IProjectFlowStorage flowStorage,
        IOperatorFactory operatorFactory,
        ILogger<ProjectService>? logger,
        ProjectVariableSessionRegistry? projectVariableSessions,
        ProjectSaveCoordinator? saveCoordinator = null,
        IProjectAssetStorage? projectAssetStorage = null,
        IWorkflowArtifactAdmissionGate? workflowArtifactAdmissionGate = null)
    {
        _projectRepository = projectRepository;
        _flowStorage = flowStorage;
        _operatorFactory = operatorFactory;
        _logger = logger;
        _projectVariableSessions = projectVariableSessions;
        _projectAssetStorage = projectAssetStorage;
        _workflowArtifactAdmissionGate = workflowArtifactAdmissionGate;
        _saveCoordinator = saveCoordinator ?? new ProjectSaveCoordinator(
            projectRepository,
            flowStorage,
            projectVariableSessions,
            projectAssetStorage: projectAssetStorage);
    }

    /// <summary>
    /// 创建工程
    /// </summary>
    public async Task<ProjectDto> CreateAsync(CreateProjectRequest request)
    {
        if (!request.ClientOperationId.HasValue)
        {
            _logger?.LogWarning(
                "Deprecated legacy project create contract used. Flow/global-variable create compatibility remains enabled outside Studio UI Next F04.");
        }

        var project = new Project(request.Name, request.Description);
        var globalVariables = request.GlobalVariables ?? new ProjectGlobalVariableSchema();
        if (request.Flow != null)
        {
            MigrateFlowDto(request.Flow);
            request.Flow = AdmitFlowForPersistence(request.Flow, "project.create.input");

            EnrichFlowDtoWithMetadata(request.Flow);
            request.Flow = AdmitFlowForPersistence(request.Flow, "project.create");
        }

        ProjectGlobalVariableSchemaValidator.ThrowIfInvalid(globalVariables, request.Flow?.ToEntity());
        project.UpdateGlobalVariables(globalVariables);
        await _projectRepository.AddAsync(project);

        // 如果创建时带有流程（通常是空的，但为了完整性）
        if (request.Flow != null)
        {
            var json = JsonSerializer.Serialize(request.Flow);
            await _flowStorage.SaveFlowJsonAsync(project.Id, json);
        }

        return MapToDto(project);
    }

    public async Task<ProjectDto> CreateBlankFromOperationAsync(
        ProjectLifecycleOperation operation,
        DateTimeOffset completedAtUtc,
        DateTimeOffset expiresAtUtc)
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (operation.Kind != ProjectLifecycleOperationKind.Create ||
            operation.Status is not (ProjectLifecycleOperationStatus.Pending or ProjectLifecycleOperationStatus.FailedRetryable))
        {
            throw new InvalidOperationException("Project create operation is not executable.");
        }

        var project = new Project(
            operation.ProjectId,
            operation.ProjectName ?? throw new InvalidOperationException("Reserved project name is missing."),
            operation.ProjectDescription);
        var dto = MapToDto(project);
        operation.CompleteCreate(
            JsonSerializer.Serialize(new ProjectLifecycleOperationResultDto { Project = dto }, _jsonOptions),
            completedAtUtc,
            expiresAtUtc);
        await _projectRepository.AddWithLifecycleOperationAsync(project, operation);
        return dto;
    }

    /// <summary>
    /// 获取工程
    /// </summary>
    public async Task<ProjectDto?> GetByIdAsync(Guid id)
    {
        await using var access = await _saveCoordinator.AcquireProjectAccessAsync(id);
        return await GetByIdUnderProjectAccessAsync(id);
    }

    public async Task<ProjectDto?> GetByIdUnderProjectAccessAsync(Guid id)
    {
        var project = await _projectRepository.GetByIdAsync(id);
        if (project == null)
            return null;

        var dto = MapToDto(project);
        dto.Assets = await LoadProjectAssetsAsync(id);

        // 从文件加载流程数据覆盖 DB 数据 (如果有)
        var flowJson = await _flowStorage.LoadFlowJsonAsync(id);
        if (!string.IsNullOrEmpty(flowJson))
        {
            try
            {
                var flowDto = JsonSerializer.Deserialize<OperatorFlowDto>(flowJson, _jsonOptions);
                if (flowDto != null)
                {
                    dto.Flow = flowDto;
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to deserialize flow JSON for project {ProjectId}; falling back to database flow.", id);
                // 忽略反序列化错误，回退到 DB 数据
            }
        }

        // 【统一修复】无论数据来自 DB 还是 JSON，都尝试回填缺失的 Options
        if (dto.Flow != null)
        {
            var migrated = MigrateFlowDto(dto.Flow);
            EnrichFlowDtoWithMetadata(dto.Flow);

            if (migrated)
            {
                _logger?.LogInformation(
                    "Project {ProjectId} flow DTO was migrated in memory; migration will persist on next explicit save.",
                    id);
            }
        }

        return dto;
    }

    public async Task<ProjectExportDocumentV1> ExportAsync(Guid id)
    {
        var project = await GetByIdAsync(id)
            ?? throw new ProjectNotFoundException(id);
        var flow = project.Flow ?? new OperatorFlowDto
        {
            Id = id,
            Name = "MainFlow"
        };

        return new ProjectExportDocumentV1
        {
            DocumentType = ProjectJsonContract.DocumentType,
            SchemaVersion = ProjectJsonContract.SchemaVersion,
            Identity = new ProjectExportIdentityV1
            {
                SourceProjectId = project.Id,
                SourcePersistenceRevision = project.PersistenceRevision
            },
            Project = new ProjectExportMetadataV1
            {
                Name = project.Name,
                Description = project.Description,
                Version = project.Version
            },
            Flow = CloneFlow(flow),
            GlobalVariables = CloneSchema(project.GlobalVariables),
            Assets = ProjectAssetJson.Clone(project.Assets)
        };
    }

    public async Task<ProjectDto> ApplyImportAsync(
        Guid id,
        ProjectExportDocumentV1 document,
        long expectedPersistenceRevision)
    {
        var normalized = NormalizeAndValidateImportDocument(document);
        Project project;
        ProjectGlobalVariableSchema previousGlobalVariables;
        string? previousFlowJson;
        await using (await _saveCoordinator.AcquireProjectAccessAsync(id))
        {
            project = await _projectRepository.GetByIdAsync(id)
                ?? throw new ProjectNotFoundException(id);
            previousGlobalVariables = CloneSchema(project.GlobalVariables);
            previousFlowJson = await _flowStorage.LoadFlowJsonAsync(id);
        }

        ProjectAssetSaveCandidate? assetCandidate = null;
        if (_projectAssetStorage == null)
        {
            if (ProjectAssetJson.HasAssets(normalized.Assets))
            {
                throw new ProjectLifecycleValidationException(
                    "PROJECT_IMPORT_ASSET_STORAGE_UNAVAILABLE",
                    "Formal project assets cannot be imported because asset persistence is unavailable.");
            }
        }
        else
        {
            assetCandidate = ProjectAssetSaveCandidate.Create(
                normalized.Assets,
                expectedPersistenceRevision,
                expectedPersistenceRevision + 1,
                "project-import");
        }

        var flowJson = JsonSerializer.Serialize(normalized.Flow, _jsonOptions);
        var saveResult = await _saveCoordinator.SaveExistingProjectAsync(new ProjectSaveRequest(
            project,
            expectedPersistenceRevision,
            normalized.Project.Name,
            normalized.Project.Description,
            previousGlobalVariables,
            normalized.GlobalVariables,
            previousFlowJson,
            normalized.Flow,
            flowJson,
            assetCandidate,
            normalized.Project.Version));

        var dto = MapToDto(saveResult.Project);
        dto.Flow = normalized.Flow;
        dto.Assets = _projectAssetStorage == null
            ? new ProjectAssetsDto()
            : await _projectAssetStorage.LoadAssetsAsync(id);
        return dto;
    }

    public async Task<bool> IsImportAppliedAsync(
        Guid id,
        ProjectExportDocumentV1 document,
        long expectedPersistenceRevision)
    {
        var current = await ExportAsync(id);
        if (current.Identity.SourcePersistenceRevision != expectedPersistenceRevision + 1)
        {
            return false;
        }

        var normalized = NormalizeAndValidateImportDocument(document);
        current.Identity = new ProjectExportIdentityV1();
        normalized.Identity = new ProjectExportIdentityV1();
        return string.Equals(
            ProjectJsonContract.Serialize(current),
            ProjectJsonContract.Serialize(normalized),
            StringComparison.Ordinal);
    }

    public async Task<ProjectDto> CreateImportedFromOperationAsync(
        ProjectLifecycleOperation operation,
        ProjectExportDocumentV1 document)
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (operation.Kind != ProjectLifecycleOperationKind.Import)
        {
            throw new InvalidOperationException("Project import operation is not executable.");
        }

        var normalized = NormalizeAndValidateImportDocument(document);
        var existing = await _projectRepository.GetByIdIncludingDeletedAsync(operation.ProjectId);
        if (existing is { IsDeleted: true })
        {
            throw new ProjectLifecycleValidationException(
                "PROJECT_IMPORT_PROJECT_ID_CONFLICT",
                "The reserved project identity is already tombstoned.");
        }

        if (existing == null)
        {
            var project = new Project(
                operation.ProjectId,
                normalized.Project.Name,
                normalized.Project.Description);
            project.UpdateVersion(normalized.Project.Version);
            await _projectRepository.AddWithLifecycleOperationAsync(project, operation);
            existing = project;
        }

        var expectedRevision = operation.ExpectedPersistenceRevision ?? existing.PersistenceRevision;
        return await ApplyImportAsync(operation.ProjectId, normalized, expectedRevision);
    }

    private ProjectExportDocumentV1 NormalizeAndValidateImportDocument(ProjectExportDocumentV1? document)
    {
        if (document == null)
        {
            throw new ProjectLifecycleValidationException(
                "PROJECT_IMPORT_DOCUMENT_REQUIRED",
                "Project import document is required.");
        }

        if (!string.Equals(document.DocumentType, ProjectJsonContract.DocumentType, StringComparison.Ordinal))
        {
            throw new ProjectLifecycleValidationException(
                "PROJECT_IMPORT_DOCUMENT_TYPE_UNSUPPORTED",
                "The project import document type is not supported.");
        }

        if (document.SchemaVersion != ProjectJsonContract.SchemaVersion)
        {
            throw new ProjectLifecycleValidationException(
                "PROJECT_IMPORT_SCHEMA_UNSUPPORTED",
                $"Project import schema version '{document.SchemaVersion}' is not supported.");
        }

        var normalized = ProjectJsonContract.Deserialize(ProjectJsonContract.Serialize(document));
        normalized.GlobalVariables ??= new ProjectGlobalVariableSchema();
        normalized.GlobalVariables.Variables ??= [];
        normalized.GlobalVariables.SourceBindings ??= [];
        normalized.GlobalVariables.TargetBindings ??= [];
        var metadata = normalized.Project ?? throw new ProjectLifecycleValidationException(
            "PROJECT_IMPORT_METADATA_REQUIRED",
            "Project import metadata is required.");
        metadata.Name = NormalizeImportedName(metadata.Name);
        metadata.Description = NormalizeImportedDescription(metadata.Description);
        metadata.Version = NormalizeImportedVersion(metadata.Version);

        var flow = normalized.Flow ?? throw new ProjectLifecycleValidationException(
            "PROJECT_IMPORT_FLOW_REQUIRED",
            "Project import flow is required.");
        flow.Operators ??= [];
        flow.Connections ??= [];
        if (string.IsNullOrWhiteSpace(flow.Name))
        {
            throw new ProjectLifecycleValidationException(
                "PROJECT_IMPORT_FLOW_NAME_REQUIRED",
                "Project import flow name is required.");
        }

        ValidateImportedFlow(flow);
        normalized.Assets = ProjectAssetJson.Normalize(normalized.Assets ?? new ProjectAssetsDto());
        if (normalized.Assets.SchemaVersion != 1)
        {
            throw new ProjectLifecycleValidationException(
                "PROJECT_IMPORT_ASSET_SCHEMA_UNSUPPORTED",
                $"Project asset schema version '{normalized.Assets.SchemaVersion}' is not supported.");
        }

        MigrateFlowDto(flow);
        EnrichFlowDtoWithMetadata(flow);
        NormalizeProjectVariableOperatorNames(flow, normalized.GlobalVariables);
        try
        {
            ProjectGlobalVariableSchemaValidator.ThrowIfInvalid(normalized.GlobalVariables, flow.ToEntity());
        }
        catch (ProjectGlobalVariableSchemaValidationException ex)
        {
            throw new ProjectLifecycleValidationException(
                "PROJECT_IMPORT_GLOBAL_VARIABLES_INVALID",
                ex.Message);
        }

        return normalized;
    }

    private void ValidateImportedFlow(OperatorFlowDto flow)
    {
        var operatorIds = new HashSet<Guid>();
        var outputPorts = new HashSet<Guid>();
        var inputPorts = new HashSet<Guid>();
        foreach (var op in flow.Operators)
        {
            op.InputPorts ??= [];
            op.OutputPorts ??= [];
            op.Parameters ??= [];
            if (op.Id == Guid.Empty || !operatorIds.Add(op.Id))
            {
                throw new ProjectLifecycleValidationException(
                    "PROJECT_IMPORT_OPERATOR_ID_INVALID",
                    "Imported operator ids must be non-empty and unique.");
            }

            var canonicalType = OperatorTypeAliasResolver.Resolve(op.Type);
            var metadata = _operatorFactory.GetMetadata(canonicalType);
            if (metadata == null)
            {
                throw new ProjectLifecycleValidationException(
                    "PROJECT_IMPORT_UNKNOWN_OPERATOR",
                    $"Imported operator type '{op.Type}' is not registered.");
            }

            var parameterNames = metadata.Parameters
                .Select(parameter => parameter.Name)
                .Concat(metadata.ParameterConstraints.Select(constraint => constraint.Parameter))
                .Concat(metadata.ParameterConstraints
                    .Where(constraint => !string.IsNullOrWhiteSpace(constraint.AliasFor))
                    .Select(constraint => constraint.Parameter))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var seenParameters = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var parameter in op.Parameters)
            {
                if (string.IsNullOrWhiteSpace(parameter.Name) || !seenParameters.Add(parameter.Name))
                {
                    throw new ProjectLifecycleValidationException(
                        "PROJECT_IMPORT_PARAMETER_INVALID",
                        $"Imported parameters for operator '{op.Id}' must have unique names.");
                }

                if (!parameterNames.Contains(parameter.Name))
                {
                    throw new ProjectLifecycleValidationException(
                        "PROJECT_IMPORT_UNKNOWN_PARAMETER",
                        $"Parameter '{parameter.Name}' is not registered for operator '{op.Type}'.");
                }
            }

            foreach (var port in op.InputPorts)
            {
                if (port.Id == Guid.Empty || !inputPorts.Add(port.Id))
                {
                    throw new ProjectLifecycleValidationException(
                        "PROJECT_IMPORT_PORT_INVALID",
                        "Imported input port ids must be non-empty and unique.");
                }
            }

            foreach (var port in op.OutputPorts)
            {
                if (port.Id == Guid.Empty || !outputPorts.Add(port.Id))
                {
                    throw new ProjectLifecycleValidationException(
                        "PROJECT_IMPORT_PORT_INVALID",
                        "Imported output port ids must be non-empty and unique.");
                }
            }
        }

        foreach (var connection in flow.Connections)
        {
            if (!operatorIds.Contains(connection.SourceOperatorId) ||
                !operatorIds.Contains(connection.TargetOperatorId) ||
                !outputPorts.Contains(connection.SourcePortId) ||
                !inputPorts.Contains(connection.TargetPortId))
            {
                throw new ProjectLifecycleValidationException(
                    "PROJECT_IMPORT_CONNECTION_INVALID",
                    "Imported connections must reference existing operator output and input ports.");
            }
        }
    }

    private static string NormalizeImportedName(string? name)
    {
        var normalized = name?.Trim() ?? string.Empty;
        if (normalized.Length == 0)
        {
            throw new ProjectLifecycleValidationException(
                "PROJECT_VALIDATION_NAME_REQUIRED",
                "Project name is required.");
        }

        if (normalized.Length > 200)
        {
            throw new ProjectLifecycleValidationException(
                "PROJECT_VALIDATION_NAME_TOO_LONG",
                "Project name cannot exceed 200 characters.");
        }

        return normalized;
    }

    private static string? NormalizeImportedDescription(string? description)
    {
        var normalized = description?.Trim();
        if (string.IsNullOrEmpty(normalized)) return null;
        if (normalized.Length > 1000)
        {
            throw new ProjectLifecycleValidationException(
                "PROJECT_VALIDATION_DESCRIPTION_TOO_LONG",
                "Project description cannot exceed 1000 characters.");
        }

        return normalized;
    }

    private static string NormalizeImportedVersion(string? version)
    {
        var normalized = version?.Trim() ?? string.Empty;
        if (normalized.Length == 0 || normalized.Length > 50)
        {
            throw new ProjectLifecycleValidationException(
                "PROJECT_IMPORT_VERSION_INVALID",
                "Project version must contain between 1 and 50 characters.");
        }

        return normalized;
    }

    private static OperatorFlowDto CloneFlow(OperatorFlowDto flow) =>
        JsonSerializer.Deserialize<OperatorFlowDto>(
            JsonSerializer.Serialize(flow, ProjectJsonContract.Options),
            ProjectJsonContract.Options) ?? new OperatorFlowDto();

    private void EnrichFlowDtoWithMetadata(OperatorFlowDto flowDto)
    {
        foreach (var opDto in flowDto.Operators)
        {
            var metadata = _operatorFactory.GetMetadata(opDto.Type);
            if (metadata == null)
                continue;

            foreach (var paramDto in opDto.Parameters)
            {
                // 如果 Options 为空且 DataType 是 enum，尝试从元数据恢复
                if ((paramDto.Options == null || paramDto.Options.Count == 0) &&
                    (paramDto.DataType.Equals("enum", StringComparison.OrdinalIgnoreCase) ||
                     paramDto.DataType.Equals("select", StringComparison.OrdinalIgnoreCase)))
                {
                    var paramDef = metadata.Parameters.FirstOrDefault(p => p.Name == paramDto.Name);
                    if (paramDef != null && paramDef.Options != null)
                    {
                        paramDto.Options = paramDef.Options;
                    }
                }
            }
        }
    }

    /// <summary>
    /// 获取所有工程
    /// </summary>
    public async Task<IEnumerable<ProjectDto>> GetAllAsync()
    {
        var projects = await _projectRepository.GetAllAsync();
        // GetAll 通常不返回详细的 Flow 内容以优化性能，或者我们可以选择加载
        // 这里暂时保持原样，仅返回轻量级列表
        return await MapProjectListWithAccessAsync(projects);
    }

    /// <summary>
    /// 更新工程
    /// </summary>
    public async Task<ProjectDto> UpdateAsync(Guid id, UpdateProjectRequest request)
    {
        Project project;
        ProjectGlobalVariableSchema previousGlobalVariables;
        string? previousFlowJson;
        OperatorFlowDto? nextFlow;
        long expectedRevision;
        await using (await _saveCoordinator.AcquireProjectAccessAsync(id))
        {
            project = await _projectRepository.GetByIdAsync(id)
                ?? throw new ProjectNotFoundException(id);
            expectedRevision = request.ExpectedPersistenceRevision ?? project.PersistenceRevision;
            previousGlobalVariables = CloneSchema(project.GlobalVariables);
            previousFlowJson = await _flowStorage.LoadFlowJsonAsync(id);
            nextFlow = request.Flow ?? await LoadStoredFlowDtoAsync(id);
        }

        var nextSchema = request.GlobalVariables ?? previousGlobalVariables;
        var flowChanged = request.Flow != null;
        if (nextFlow != null)
        {
            if (request.Flow != null)
            {
                nextFlow = AdmitFlowForPersistence(nextFlow, "project.update.input");
            }

            flowChanged |= MigrateFlowDto(nextFlow);
            EnrichFlowDtoWithMetadata(nextFlow);
            flowChanged |= NormalizeProjectVariableOperatorNames(nextFlow, nextSchema);

            nextFlow = AdmitFlowForPersistence(nextFlow, "project.update");
        }

        ProjectGlobalVariableSchemaValidator.ThrowIfInvalid(nextSchema, nextFlow?.ToEntity());
        var nextFlowJson = flowChanged && nextFlow != null ? JsonSerializer.Serialize(nextFlow, _jsonOptions) : null;

        var saveResult = await _saveCoordinator.SaveExistingProjectAsync(new ProjectSaveRequest(
            project,
            expectedRevision,
            request.Name,
            request.Description,
            previousGlobalVariables,
            nextSchema,
            previousFlowJson,
            nextFlow,
            nextFlowJson));

        var dto = MapToDto(saveResult.Project);
        dto.Flow = nextFlow;
        dto.Assets = await LoadProjectAssetsAsync(id);
        return dto;
    }

    public async Task<ProjectCalibrationAssetSaveResponse> SaveCalibrationAssetAsync(
        Guid id,
        ProjectCalibrationAssetSaveRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (_projectAssetStorage == null)
        {
            throw new InvalidOperationException("PSV017: project asset persistence is unavailable.");
        }

        var payload = request.Payload.Clone();
        ValidateCalibrationAssetPayload(payload);
        var contentHash = ProjectAssetJson.ComputePayloadHash(payload);
        if (!string.IsNullOrWhiteSpace(request.ExpectedContentHash) &&
            !string.Equals(request.ExpectedContentHash.Trim(), contentHash, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("PSV019: calibration candidate checksum mismatch.");
        }

        Project project;
        ProjectGlobalVariableSchema previousGlobalVariables;
        string? previousFlowJson;
        ProjectAssetsDto currentAssets;
        long expectedRevision;
        await using (await _saveCoordinator.AcquireProjectAccessAsync(id))
        {
            project = await _projectRepository.GetByIdAsync(id)
                ?? throw new ProjectNotFoundException(id);
            expectedRevision = request.ExpectedPersistenceRevision ?? project.PersistenceRevision;
            previousGlobalVariables = CloneSchema(project.GlobalVariables);
            previousFlowJson = await _flowStorage.LoadFlowJsonAsync(id);
            currentAssets = await _projectAssetStorage.LoadAssetsAsync(id);
        }

        var assetId = NormalizeAssetId(request.AssetId, payload);
        var now = DateTimeOffset.UtcNow;
        var nextRevision = expectedRevision + 1;
        var nextAssets = ProjectAssetJson.Clone(currentAssets);
        var existingAsset = nextAssets.CalibrationAssets
            .FirstOrDefault(asset => string.Equals(asset.AssetId, assetId, StringComparison.Ordinal));
        nextAssets.CalibrationAssets.RemoveAll(asset =>
            string.Equals(asset.AssetId, assetId, StringComparison.Ordinal));
        nextAssets.CalibrationAssets.Add(new ProjectCalibrationAssetDto
        {
            AssetId = assetId,
            Kind = "CalibrationBundleV2",
            Version = NormalizeOptional(request.Version) ?? ReadPayloadString(payload, "calibrationVersion") ?? "1",
            Producer = NormalizeOptional(request.Producer) ?? "NPointCalibrationDraftWorkbench",
            SourceDraftSessionId = NormalizeOptional(request.SourceDraftSessionId) ?? string.Empty,
            TargetNodeId = request.TargetNodeId,
            ImageIdentity = NormalizeOptional(request.ImageIdentity) ?? string.Empty,
            ContentHash = contentHash,
            ProjectRevision = nextRevision,
            CreatedAtUtc = existingAsset?.CreatedAtUtc == default ? now : existingAsset?.CreatedAtUtc ?? now,
            UpdatedAtUtc = now,
            Status = "authority",
            Payload = payload
        });

        var assetCandidate = ProjectAssetSaveCandidate.Create(
            nextAssets,
            expectedRevision,
            nextRevision,
            "calibration-draft-formal-save");

        var saved = await _saveCoordinator.SaveExistingProjectAsync(new ProjectSaveRequest(
            project,
            expectedRevision,
            project.Name,
            project.Description,
            previousGlobalVariables,
            previousGlobalVariables,
            previousFlowJson,
            null,
            null,
            assetCandidate));

        var savedAsset = assetCandidate.Assets.CalibrationAssets.Single(asset =>
            string.Equals(asset.AssetId, assetId, StringComparison.Ordinal));
        return new ProjectCalibrationAssetSaveResponse
        {
            ProjectId = saved.Project.Id,
            PersistenceRevision = saved.Project.PersistenceRevision,
            AssetsHash = assetCandidate.AssetsHash,
            Asset = savedAsset,
            Assets = assetCandidate.Assets
        };
    }

    /// <summary>
    /// 更新工程流程
    /// </summary>
    public async Task<ProjectDto> UpdateFlowAsync(Guid id, UpdateFlowRequest request)
    {
        // 1. 验证工程存在
        Project project;
        OperatorFlowDto? existingFlow;
        await using (await _saveCoordinator.AcquireProjectAccessAsync(id))
        {
            project = await _projectRepository.GetByIdAsync(id)
                ?? throw new ProjectNotFoundException(id);
            existingFlow = await LoadStoredFlowDtoAsync(id);
        }

        // 2. 构造流程DTO
        var flowDto = new OperatorFlowDto
        {
            Name = ResolveFlowName(request.Name, existingFlow?.Name, project.Flow?.Name),
            DecisionConfiguration = request.DecisionConfiguration ?? existingFlow?.DecisionConfiguration,
            Operators = request.Operators,
            Connections = request.Connections
        };
        return await UpdateAsync(id, new UpdateProjectRequest
        {
            Name = project.Name,
            Description = project.Description,
            ExpectedPersistenceRevision = request.ExpectedPersistenceRevision ?? project.PersistenceRevision,
            Flow = flowDto
        });

        // 4. 更新工程修改时间 (可选，但推荐)
        // project.LastModified = DateTime.UtcNow; // 如果 Project 有这个字段
        // await _projectRepository.UpdateAsync(project);
    }

    private static string ResolveFlowName(string? requestedName, string? storedName, string? databaseName)
    {
        if (!string.IsNullOrWhiteSpace(requestedName))
        {
            return requestedName.Trim();
        }

        if (!string.IsNullOrWhiteSpace(storedName))
        {
            return storedName.Trim();
        }

        if (!string.IsNullOrWhiteSpace(databaseName))
        {
            return databaseName.Trim();
        }

        return "MainFlow";
    }

    /// <summary>
    /// 将OperatorFlowDto转换为Core实体
    /// </summary>
    private OperatorFlow MapDtoToFlow(OperatorFlowDto dto, Guid? flowId = null)
    {
        var flow = new OperatorFlow(dto.Name)
        {
            DecisionConfiguration = dto.DecisionConfiguration
        };

        // 【关键修复】如果指定了 flowId (通常是 Project.Id)，强制设置它
        // EF Core Table Splitting 要求 Project.Id == Flow.Id
        if (flowId.HasValue)
        {
            // Flow继承自Entity，Id定义在Entity中
            typeof(ClearVision.Product.Core.Entities.Base.Entity)
                .GetProperty("Id")?
                .SetValue(flow, flowId.Value);
        }

        // 添加算子
        foreach (var opDto in dto.Operators)
        {
            var canonicalType = OperatorTypeAliasResolver.Resolve(opDto.Type);
            var op = new Operator(
                opDto.Name,
                canonicalType,
                opDto.X,
                opDto.Y
            )
            {
                Metadata = opDto.Metadata == null
                    ? null
                    : new Dictionary<string, object?>(opDto.Metadata, StringComparer.OrdinalIgnoreCase)
            };

            // 设置ID（如果提供了）
            if (opDto.Id != Guid.Empty)
            {
                // 使用反射设置ID，因为构造函数会生成新的ID
                typeof(Operator).GetProperty("Id")?.SetValue(op, opDto.Id);
            }

            // 恢复输入端口（保留ID以维持连线）
            foreach (var portDto in opDto.InputPorts)
            {
                op.LoadInputPort(portDto.Id, portDto.Name, portDto.DataType, portDto.IsRequired);
            }

            // 恢复输出端口（保留ID以维持连线）
            foreach (var portDto in opDto.OutputPorts)
            {
                op.LoadOutputPort(portDto.Id, portDto.Name, portDto.DataType);
            }

            // 添加参数
            foreach (var paramDto in opDto.Parameters)
            {
                var param = new Parameter(
                    paramDto.Id == Guid.Empty ? Guid.NewGuid() : paramDto.Id,
                    paramDto.Name,
                    paramDto.DisplayName,
                    paramDto.Description ?? string.Empty,
                    paramDto.DataType,
                    paramDto.DefaultValue,
                    paramDto.MinValue,
                    paramDto.MaxValue,
                    paramDto.IsRequired,
                    paramDto.Options
                );

                if (paramDto.Value != null)
                {
                    param.SetValue(paramDto.Value);
                }

                op.AddParameter(param);
            }

            flow.AddOperator(op);
        }

        // 添加连接
        foreach (var connDto in dto.Connections)
        {
            // 【修复】修正参数顺序：sourceOperatorId, sourcePortId, targetOperatorId, targetPortId
            var connection = new OperatorConnection(
                connDto.SourceOperatorId,
                connDto.SourcePortId,        // 修正：第2个参数应该是 SourcePortId
                connDto.TargetOperatorId,    // 修正：第3个参数应该是 TargetOperatorId
                connDto.TargetPortId
            );

            // 设置连接ID
            if (connDto.Id != Guid.Empty)
            {
                typeof(OperatorConnection).GetProperty("Id")?.SetValue(connection, connDto.Id);
            }

            flow.AddConnection(connection);
        }

        return flow;
    }

    /// <summary>
    /// 删除工程
    /// </summary>
    public async Task<ProjectLifecycleOperationResultDto> TombstoneFromOperationAsync(
        ProjectLifecycleOperation operation,
        DateTimeOffset completedAtUtc,
        DateTimeOffset expiresAtUtc)
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (operation.Kind != ProjectLifecycleOperationKind.Delete ||
            operation.ExpectedPersistenceRevision == null)
        {
            throw new InvalidOperationException("Project delete operation is not executable.");
        }

        await using var access = await _saveCoordinator.TryAcquireProjectAccessAsync(operation.ProjectId);
        if (access == null)
        {
            throw new ProjectMutationConflictException(operation.ProjectId);
        }

        var project = await _projectRepository.GetByIdForUpdateAsync(operation.ProjectId)
            ?? throw new ProjectNotFoundException(operation.ProjectId);
        if (project.PersistenceRevision != operation.ExpectedPersistenceRevision.Value)
        {
            throw new ProjectRevisionConflictException(
                project.Id,
                operation.ExpectedPersistenceRevision.Value,
                project.PersistenceRevision);
        }

        var result = new ProjectLifecycleOperationResultDto
        {
            Deleted = true,
            AlreadyDeleted = false,
            CleanupStatus = "cleanup-pending"
        };
        project.MarkAsDeleted();
        operation.CompleteDelete(
            JsonSerializer.Serialize(result, _jsonOptions),
            completedAtUtc,
            expiresAtUtc);
        await _projectRepository.TombstoneWithLifecycleOperationAsync(project, operation);
        _saveCoordinator.ClearProjectState(project.Id);
        return result;
    }

    public async Task CleanupDeletedProjectAsync(Guid id)
    {
        var project = await _projectRepository.GetByIdIncludingDeletedAsync(id)
            ?? throw new ProjectNotFoundException(id);
        if (!project.IsDeleted)
        {
            throw new InvalidOperationException($"Project '{id}' is not tombstoned.");
        }

        try
        {
            _projectVariableSessions?.Delete(id);
            await _flowStorage.DeleteFlowJsonAsync(id);
            if (_projectAssetStorage != null)
            {
                await _projectAssetStorage.DeleteAssetsAsync(id);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to clean derived project state after deleting project {ProjectId}.", id);
            throw;
        }
        finally
        {
            _saveCoordinator.ClearProjectState(id);
        }
    }

    public async Task<ProjectOpenResponse> OpenAsync(Guid id, DateTime openedAtUtc)
    {
        await using var access = await _saveCoordinator.AcquireProjectAccessAsync(id);
        var lastOpenedAt = await _projectRepository.RecordOpenAsync(id, openedAtUtc)
            ?? throw new ProjectNotFoundException(id);
        return new ProjectOpenResponse
        {
            ProjectId = id,
            LastOpenedAtUtc = DateTime.SpecifyKind(lastOpenedAt, DateTimeKind.Utc)
        };
    }

    /// <summary>
    /// 搜索工程
    /// </summary>
    public async Task<IEnumerable<ProjectDto>> SearchAsync(string keyword)
    {
        var projects = await _projectRepository.SearchAsync(keyword);
        return await MapProjectListWithAccessAsync(projects);
    }

    /// <summary>
    /// 获取最近打开的工程
    /// </summary>
    public async Task<IEnumerable<ProjectDto>> GetRecentlyOpenedAsync(int count = 10)
    {
        var projects = await _projectRepository.GetRecentlyOpenedAsync(count);
        return await MapProjectListWithAccessAsync(projects);
    }

    public async Task<ProjectGlobalVariableSchema> UpdateGlobalVariablesAsync(Guid id, ProjectGlobalVariableSchema schema)
    {
        Project project;
        await using (await _saveCoordinator.AcquireProjectAccessAsync(id))
        {
            project = await _projectRepository.GetByIdAsync(id)
                ?? throw new ProjectNotFoundException(id);
        }

        var updated = await UpdateAsync(id, new UpdateProjectRequest
        {
            Name = project.Name,
            Description = project.Description,
            GlobalVariables = schema
        });
        return updated.GlobalVariables;
    }

    private static ProjectGlobalVariableSchema CloneSchema(ProjectGlobalVariableSchema schema)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(schema, _jsonOptions);
        return JsonSerializer.Deserialize<ProjectGlobalVariableSchema>(bytes, _jsonOptions) ?? new ProjectGlobalVariableSchema();
    }

    private async Task<ProjectAssetsDto> LoadProjectAssetsAsync(Guid projectId) =>
        _projectAssetStorage == null
            ? new ProjectAssetsDto()
            : await _projectAssetStorage.LoadAssetsAsync(projectId);

    public async Task<ProjectAssetStorageMetadata?> GetProjectAssetStorageMetadataUnderProjectAccessAsync(Guid projectId) =>
        _projectAssetStorage == null
            ? null
            : await _projectAssetStorage.LoadMetadataAsync(projectId);

    private static void ValidateCalibrationAssetPayload(JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException("PSV025: calibration asset payload must be a JSON object.");
        }

        if (!TryGetPropertyIgnoreCase(payload, "schemaVersion", out var schemaVersion) ||
            schemaVersion.ValueKind != JsonValueKind.Number ||
            schemaVersion.GetInt32() != 2)
        {
            throw new InvalidOperationException("PSV025: calibration asset payload must be CalibrationBundleV2.");
        }

        if (!TryGetPropertyIgnoreCase(payload, "quality", out var quality) ||
            quality.ValueKind != JsonValueKind.Object ||
            !TryGetPropertyIgnoreCase(quality, "accepted", out var accepted) ||
            accepted.ValueKind != JsonValueKind.True)
        {
            throw new InvalidOperationException("PSV025: calibration asset payload must be accepted before formal save.");
        }
    }

    private static string NormalizeAssetId(string? raw, JsonElement payload)
    {
        var candidate = NormalizeOptional(raw) ?? ReadPayloadString(payload, "bundleId");
        return string.IsNullOrWhiteSpace(candidate)
            ? $"calibration-{Guid.NewGuid():N}"
            : candidate;
    }

    private static string? NormalizeOptional(string? raw) =>
        string.IsNullOrWhiteSpace(raw) ? null : raw.Trim();

    private static string? ReadPayloadString(JsonElement payload, string propertyName)
    {
        if (!TryGetPropertyIgnoreCase(payload, propertyName, out var value) ||
            value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return NormalizeOptional(value.GetString());
    }

    private static bool TryGetPropertyIgnoreCase(
        JsonElement element,
        string propertyName,
        out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }

    private async Task<IReadOnlyList<ProjectDto>> MapProjectListWithAccessAsync(IEnumerable<Project> candidates)
    {
        var result = new List<ProjectDto>();
        foreach (var candidate in candidates)
        {
            try
            {
                await using var access = await _saveCoordinator.AcquireProjectAccessAsync(candidate.Id);
                var current = await _projectRepository.GetByIdFreshAsync(candidate.Id);
                if (current != null)
                {
                    result.Add(MapToDto(current));
                }
            }
            catch (InvalidOperationException ex) when (IsProjectRecoveryRequired(ex))
            {
                _logger?.LogWarning(
                    ex,
                    "Skipping project {ProjectId} in project list because save recovery is required.",
                    candidate.Id);
            }
        }

        return result;
    }

    private static bool IsProjectRecoveryRequired(InvalidOperationException ex) =>
        ex.Message.StartsWith("PSV001:", StringComparison.Ordinal);

    private async Task<OperatorFlowDto?> LoadStoredFlowDtoAsync(Guid projectId)
    {
        var flowJson = await _flowStorage.LoadFlowJsonAsync(projectId);
        if (string.IsNullOrWhiteSpace(flowJson))
        {
            return null;
        }

        return JsonSerializer.Deserialize<OperatorFlowDto>(flowJson, _jsonOptions);
    }

    private OperatorFlowDto AdmitFlowForPersistence(OperatorFlowDto flow, string source)
    {
        // The admission gate is an AI-artifact boundary. Existing hand-authored
        // project flows continue through the established persistence validators;
        // AI-produced flows must never bypass the gate or run without it.
        if (!WorkflowArtifactAdmissionClassifier.IsAiArtifact(flow))
        {
            return flow;
        }

        if (_workflowArtifactAdmissionGate == null)
        {
            throw WorkflowArtifactAdmissionFailures.GateUnavailable(source);
        }

        var originalSnapshot = JsonSerializer.Serialize(flow, _jsonOptions);
        var admission = _workflowArtifactAdmissionGate.Inspect(flow, source, originalSnapshot);
        if (!admission.AllowedToPersist || admission.Flow == null)
        {
            throw new WorkflowArtifactAdmissionException(admission.Report);
        }

        return admission.Flow;
    }

    private async Task<OperatorFlow?> LoadStoredFlowEntityAsync(Guid projectId)
    {
        var flowJson = await _flowStorage.LoadFlowJsonAsync(projectId);
        if (string.IsNullOrWhiteSpace(flowJson))
        {
            return null;
        }

        var flowDto = JsonSerializer.Deserialize<OperatorFlowDto>(flowJson, _jsonOptions);
        return flowDto?.ToEntity();
    }

    private ProjectDto MapToDto(Project project)
    {
        return new ProjectDto
        {
            Id = project.Id,
            Name = project.Name,
            Description = project.Description,
            Version = project.Version,
            PersistenceRevision = project.PersistenceRevision,
            CreatedAt = project.CreatedAt,
            ModifiedAt = project.ModifiedAt,
            LastOpenedAt = project.LastOpenedAt,
            GlobalSettings = project.GlobalSettings,
            GlobalVariables = project.GlobalVariables,
            // 修复：添加 Flow 字段映射
            Flow = project.Flow != null ? MapFlowToDto(project.Flow) : null
        };
    }

    /// <summary>
    /// 将 OperatorFlow 实体映射为 DTO
    /// </summary>
    private OperatorFlowDto MapFlowToDto(OperatorFlow flow)
    {
        return new OperatorFlowDto
        {
            Id = flow.Id,
            Name = flow.Name,
            DecisionConfiguration = flow.DecisionConfiguration,
            Operators = flow.Operators.Select(MapOperatorToDto).ToList(),
            Connections = flow.Connections.Select(MapConnectionToDto).ToList()
        };
    }

    /// <summary>
    /// 将 Operator 实体映射为 DTO
    /// </summary>
    private OperatorDto MapOperatorToDto(Operator op)
    {
        return new OperatorDto
        {
            Id = op.Id,
            Name = op.Name,
            Type = OperatorTypeAliasResolver.Resolve(op.Type),
            Metadata = op.Metadata == null
                ? null
                : new Dictionary<string, object?>(op.Metadata, StringComparer.OrdinalIgnoreCase),
            X = op.Position.X,
            Y = op.Position.Y,
            InputPorts = op.InputPorts.Select(MapPortToDto).ToList(),
            OutputPorts = op.OutputPorts.Select(MapPortToDto).ToList(),
            Parameters = op.Parameters.Select(MapParameterToDto).ToList(),
            IsEnabled = op.IsEnabled,
            ExecutionStatus = op.ExecutionStatus,
            ExecutionTimeMs = op.ExecutionTimeMs,
            ErrorMessage = op.ErrorMessage
        };
    }

    /// <summary>
    /// 将 Port 值对象映射为 DTO
    /// </summary>
    private PortDto MapPortToDto(Port port)
    {
        return new PortDto
        {
            Id = port.Id,
            Name = port.Name,
            Direction = port.Direction,
            DataType = port.DataType,
            IsRequired = port.IsRequired
        };
    }

    /// <summary>
    /// 将 Parameter 值对象映射为 DTO
    /// </summary>
    private ParameterDto MapParameterToDto(Parameter param)
    {
        return new ParameterDto
        {
            Id = param.Id,
            Name = param.Name,
            DisplayName = param.DisplayName,
            Description = param.Description,
            DataType = param.DataType,
            // Preserve a persisted explicit null for the editor and save round-trip.
            // Runtime resolution remains Parameter.GetValue(), where null means use DefaultValue.
            Value = param.Value,
            DefaultValue = param.DefaultValue,
            MinValue = param.MinValue,
            MaxValue = param.MaxValue,
            IsRequired = param.IsRequired,
            Options = param.Options
        };
    }

    /// <summary>
    /// 将 OperatorConnection 值对象映射为 DTO
    /// </summary>
    private OperatorConnectionDto MapConnectionToDto(OperatorConnection conn)
    {
        return new OperatorConnectionDto
        {
            Id = conn.Id,
            SourceOperatorId = conn.SourceOperatorId,
            SourcePortId = conn.SourcePortId,
            TargetOperatorId = conn.TargetOperatorId,
            TargetPortId = conn.TargetPortId
        };
    }

    private static readonly HashSet<string> LegacyPortNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "input",
        "output",
        "in",
        "out"
    };

    private bool MigrateFlowDto(OperatorFlowDto flowDto)
    {
        var changed = false;
        foreach (var opDto in flowDto.Operators)
        {
            var canonicalType = OperatorTypeAliasResolver.Resolve(opDto.Type);
            if (canonicalType != opDto.Type)
            {
                opDto.Type = canonicalType;
                changed = true;
            }

            var metadata = _operatorFactory.GetMetadata(opDto.Type);
            if (metadata == null)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(opDto.Name))
            {
                opDto.Name = metadata.DisplayName;
                changed = true;
            }

            changed |= NormalizePorts(opDto.InputPorts, metadata.InputPorts, PortDirection.Input);
            changed |= NormalizePorts(opDto.OutputPorts, metadata.OutputPorts, PortDirection.Output);
            changed |= CanonicalizeParameterAliases(opDto, metadata);
            changed |= NormalizeParameters(opDto.Parameters, metadata.Parameters);
        }

        return changed;
    }

    private bool CanonicalizeParameterAliases(OperatorDto opDto, OperatorMetadata metadata)
    {
        var aliases = metadata.ParameterConstraints
            .Where(constraint => !string.IsNullOrWhiteSpace(constraint.AliasFor))
            .ToArray();
        if (aliases.Length == 0)
        {
            return false;
        }

        var values = new Dictionary<string, object?>(StringComparer.Ordinal);
        var explicitNames = new HashSet<string>(StringComparer.Ordinal);
        var aliasNames = aliases.Select(alias => alias.Parameter).ToHashSet(StringComparer.Ordinal);
        foreach (var parameter in opDto.Parameters)
        {
            values[parameter.Name] = parameter.Value;
            explicitNames.Add(parameter.Name);
        }

        var canonicalization = OperatorParameterConstraintEvaluator.Canonicalize(
            metadata,
            values,
            explicitNames);
        foreach (var diagnostic in canonicalization.Diagnostics)
        {
            _logger?.LogWarning(
                "Operator {OperatorType} parameter canonicalization: {Diagnostic}",
                opDto.Type,
                diagnostic.Message);
        }

        var changed = false;
        foreach (var canonicalName in aliases
                     .Select(alias => metadata.Parameters.FirstOrDefault(parameter =>
                         parameter.Name.Equals(alias.AliasFor, StringComparison.OrdinalIgnoreCase))?.Name ?? alias.AliasFor!)
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!canonicalization.ExplicitValues.TryGetValue(canonicalName, out var canonicalValue))
            {
                continue;
            }

            var canonicalParameter = opDto.Parameters.FirstOrDefault(parameter =>
                parameter.Name.Equals(canonicalName, StringComparison.Ordinal));
            if (canonicalParameter == null)
            {
                canonicalParameter = AddParameter(opDto, canonicalName);
                changed = true;
            }

            if (!ParameterMetadataValueEquals(canonicalParameter.Value, canonicalValue))
            {
                canonicalParameter.Value = canonicalValue;
                changed = true;
            }
        }

        var removed = opDto.Parameters.RemoveAll(parameter => aliasNames.Contains(parameter.Name));
        return changed || removed > 0;
    }

    private static bool NormalizeProjectVariableOperatorNames(
        OperatorFlowDto flowDto,
        ProjectGlobalVariableSchema schema)
    {
        var variablesById = schema.Variables
            .Where(variable => variable.Id != Guid.Empty)
            .GroupBy(variable => variable.Id)
            .ToDictionary(group => group.Key, group => group.First());
        var variablesByName = schema.Variables
            .Where(variable => !string.IsNullOrWhiteSpace(variable.Name))
            .GroupBy(variable => variable.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        if (variablesById.Count == 0)
        {
            return false;
        }

        var changed = false;
        foreach (var opDto in flowDto.Operators)
        {
            if (opDto.Type is not (OperatorType.VariableRead or OperatorType.VariableWrite or OperatorType.VariableIncrement))
            {
                continue;
            }

            if (!string.Equals(GetParameterString(opDto, "Scope"), "Project", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var variableIdParameter = GetParameter(opDto, "VariableId");
            var variableNameParameter = GetParameter(opDto, "VariableName");
            ProjectGlobalVariableDefinition? definition = null;
            var variableIdText = variableIdParameter?.Value?.ToString();
            var hasParsedVariableId = Guid.TryParse(variableIdText, out var variableId);
            if (hasParsedVariableId)
            {
                variablesById.TryGetValue(variableId, out definition);
            }

            var variableNameText = variableNameParameter?.Value?.ToString();
            ProjectGlobalVariableDefinition? definitionByName = null;
            if (!string.IsNullOrWhiteSpace(variableNameText))
            {
                variablesByName.TryGetValue(variableNameText, out definitionByName);
            }

            if (definition != null &&
                definitionByName != null &&
                definition.Id != definitionByName.Id)
            {
                continue;
            }

            if (definition == null && !hasParsedVariableId)
            {
                if (definitionByName == null)
                {
                    continue;
                }

                definition = definitionByName;
            }

            if (definition == null)
            {
                continue;
            }

            variableIdParameter ??= AddParameter(opDto, "VariableId");
            var currentId = variableIdParameter.Value?.ToString();
            var nextId = definition.Id.ToString("D");
            if (!string.Equals(currentId, nextId, StringComparison.OrdinalIgnoreCase))
            {
                variableIdParameter.Value = nextId;
                changed = true;
            }

            variableNameParameter ??= AddParameter(opDto, "VariableName");
            var currentName = variableNameParameter.Value?.ToString();
            if (string.Equals(currentName, definition.Name, StringComparison.Ordinal))
            {
                continue;
            }

            variableNameParameter.Value = definition.Name;
            changed = true;
        }

        return changed;
    }

    private static ParameterDto AddParameter(OperatorDto opDto, string name)
    {
        var parameter = new ParameterDto
        {
            Id = Guid.NewGuid(),
            Name = name,
            DisplayName = name,
            DataType = "string"
        };
        opDto.Parameters.Add(parameter);
        return parameter;
    }

    private static ParameterDto? GetParameter(OperatorDto opDto, string name)
    {
        return opDto.Parameters.FirstOrDefault(parameter =>
            string.Equals(parameter.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    private static string? GetParameterString(OperatorDto opDto, string name)
    {
        return GetParameter(opDto, name)?.Value?.ToString();
    }

    private static bool NormalizePorts(List<PortDto> ports, List<PortDefinition> metadataPorts, PortDirection direction)
    {
        if (metadataPorts.Count == 0)
        {
            return false;
        }

        var changed = false;
        var shouldRebuild = ports.Count == 0 ||
            (ports.Count == metadataPorts.Count &&
             ports.All(port => LegacyPortNames.Contains(port.Name) || port.Id == Guid.Empty));

        if (shouldRebuild)
        {
            ports.Clear();
            foreach (var definition in metadataPorts)
            {
                ports.Add(new PortDto
                {
                    Id = Guid.NewGuid(),
                    Name = definition.Name,
                    Direction = direction,
                    DataType = definition.DataType,
                    IsRequired = direction == PortDirection.Input && definition.IsRequired
                });
            }

            return true;
        }

        var count = Math.Min(ports.Count, metadataPorts.Count);
        for (var index = 0; index < count; index += 1)
        {
            var port = ports[index];
            var definition = metadataPorts[index];

            if (port.Id == Guid.Empty)
            {
                port.Id = Guid.NewGuid();
                changed = true;
            }

            if (LegacyPortNames.Contains(port.Name))
            {
                port.Name = definition.Name;
                changed = true;
            }

            if (port.DataType != definition.DataType)
            {
                port.DataType = definition.DataType;
                changed = true;
            }

            if (port.Direction != direction)
            {
                port.Direction = direction;
                changed = true;
            }

            if (direction == PortDirection.Input && port.IsRequired != definition.IsRequired)
            {
                port.IsRequired = definition.IsRequired;
                changed = true;
            }
        }

        return changed;
    }

    private static bool NormalizeParameters(List<ParameterDto> parameters, List<ParameterDefinition> metadataParameters)
    {
        var changed = false;

        foreach (var definition in metadataParameters)
        {
            var parameter = parameters.FirstOrDefault(item =>
                string.Equals(item.Name, definition.Name, StringComparison.OrdinalIgnoreCase));

            if (parameter == null)
            {
                parameters.Add(new ParameterDto
                {
                    Id = Guid.NewGuid(),
                    Name = definition.Name,
                    DisplayName = definition.DisplayName,
                    Description = definition.Description,
                    DataType = definition.DataType,
                    Value = definition.DefaultValue,
                    DefaultValue = definition.DefaultValue,
                    MinValue = definition.MinValue,
                    MaxValue = definition.MaxValue,
                    IsRequired = definition.IsRequired,
                    Options = definition.Options
                });
                changed = true;
                continue;
            }

            if (parameter.Id == Guid.Empty)
            {
                parameter.Id = Guid.NewGuid();
                changed = true;
            }

            if (!string.Equals(parameter.Name, definition.Name, StringComparison.Ordinal))
            {
                parameter.Name = definition.Name;
                changed = true;
            }

            if (!string.Equals(parameter.DisplayName, definition.DisplayName, StringComparison.Ordinal))
            {
                parameter.DisplayName = definition.DisplayName;
                changed = true;
            }

            if (parameter.Description != definition.Description)
            {
                parameter.Description = definition.Description;
                changed = true;
            }

            if (!string.Equals(parameter.DataType, definition.DataType, StringComparison.OrdinalIgnoreCase))
            {
                parameter.DataType = definition.DataType;
                changed = true;
            }

            if (!ParameterMetadataValueEquals(parameter.DefaultValue, definition.DefaultValue))
            {
                parameter.DefaultValue = definition.DefaultValue;
                changed = true;
            }

            if (!ParameterMetadataValueEquals(parameter.MinValue, definition.MinValue))
            {
                parameter.MinValue = definition.MinValue;
                changed = true;
            }

            if (!ParameterMetadataValueEquals(parameter.MaxValue, definition.MaxValue))
            {
                parameter.MaxValue = definition.MaxValue;
                changed = true;
            }

            if (parameter.IsRequired != definition.IsRequired)
            {
                parameter.IsRequired = definition.IsRequired;
                changed = true;
            }

            if ((parameter.Options == null || parameter.Options.Count == 0) && definition.Options != null)
            {
                parameter.Options = definition.Options;
                changed = true;
            }
        }

        return changed;
    }

    private static bool ParameterMetadataValueEquals(object? current, object? expected)
    {
        if (current == null || expected == null)
        {
            return current == null && expected == null;
        }

        if (Equals(current, expected))
        {
            return true;
        }

        return JsonSerializer.Serialize(current, _jsonOptions) == JsonSerializer.Serialize(expected, _jsonOptions);
    }
}
