// ApiEndpoints.cs
// API 端点配置
// 作者：蘅芜君

using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using ClearVision.Product.Application.Analysis;
using ClearVision.Product.Application.DTOs;
using ClearVision.Product.Application.Services;
using ClearVision.Product.Core.Decisions;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Exceptions;
using ClearVision.Product.Core.Interfaces;
using ClearVision.Product.Core.Outcomes;
using ClearVision.Product.Core.ProjectVariables;
using ClearVision.Product.Core.Services;
using ClearVision.Product.Core.ValueObjects;
using ClearVision.Product.Desktop.Handlers;
using ClearVision.Product.Desktop.Middleware;
using ClearVision.Product.Desktop.Station;
using ClearVision.Product.Infrastructure.AI;
using ClearVision.Product.Infrastructure.Data;
using ClearVision.Product.Infrastructure.Services;
using ClearVision.Product.Runtime;
using ClearVision.Product.Runtime.Abstractions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenCvSharp;

namespace ClearVision.Product.Desktop.Endpoints;

/// <summary>
/// API 端点配置
/// </summary>
public static class ApiEndpoints
{
    private static readonly JsonSerializerOptions OptionalRequestJsonOptions = new(JsonSerializerDefaults.Web);

    public static IEndpointRouteBuilder MapVisionApiEndpoints(this IEndpointRouteBuilder app)
    {
        // 健康检查
        app.MapGet("/api/health", () => Results.Ok(new { Status = "Healthy", Timestamp = DateTime.UtcNow }));

        // 工程相关端点
        MapProjectEndpoints(app);

        // 检测相关端点
        MapInspectionEndpoints(app);

        // 算子库端点
        MapOperatorEndpoints(app);

        // 【Phase 3】节点预览端点（复用调试缓存机制）
        app.MapPreviewNodeEndpoints();
        app.MapPreviewArtifactEndpoints();
        app.MapCalibrationDraftEndpoints();

        // 图像相关端点
        MapImageEndpoints(app);

        return app;
    }

    public class OperatorPreviewRequest
    {
        public string ImageBase64 { get; set; } = string.Empty;
        public Dictionary<string, object>? Parameters { get; set; }
    }

    public class OperatorParameterRecommendationRequest
    {
        public string ImageBase64 { get; set; } = string.Empty;
    }

    public class TemplateUpsertRequest
    {
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Industry { get; set; } = string.Empty;
        public List<string> Tags { get; set; } = new();
        public string? FlowJson { get; set; }
        public object? FlowData { get; set; }
    }

    public class ExportRuntimePackageRequest
    {
        public string? TargetRootDirectory { get; set; }

        public OperatorFlowDto? Flow { get; set; }

        public bool RegisterForStationDeployment { get; set; } = true;
    }

    public sealed class ProjectVariableValueWriteRequest
    {
        public object? Value { get; set; }

        public long? ExpectedVersion { get; set; }
    }

    public sealed class ProjectVariableResetRequest
    {
        public long? ExpectedVersion { get; set; }
    }

    public sealed class ProjectVariableResetAllRequest
    {
        public Dictionary<Guid, long>? ExpectedVersions { get; set; }
    }

    private static void MapProjectEndpoints(IEndpointRouteBuilder app)
    {
        // 获取工程列表
        app.MapGet("/api/projects", async (ProjectService service) =>
        {
            var projects = await service.GetAllAsync();
            return Results.Ok(projects);
        });

        // 获取最近打开的工程
        app.MapGet("/api/projects/recent", async (ProjectService service, int count = 10) =>
        {
            var projects = await service.GetRecentlyOpenedAsync(count);
            return Results.Ok(projects);
        });

        // 搜索工程
        app.MapGet("/api/projects/search", async (ProjectService service, string keyword) =>
        {
            var projects = await service.SearchAsync(keyword);
            return Results.Ok(projects);
        });

        // 获取工程详情
        app.MapGet("/api/projects/{id:guid}", async (Guid id, ProjectService service) =>
        {
            try
            {
                var project = await service.GetByIdAsync(id);
                return project != null
                    ? Results.Ok(project)
                    : Results.NotFound(new { Code = "PROJECT_NOT_FOUND", Error = "Project was not found." });
            }
            catch (Exception ex)
            {
                return ex is ProjectNotFoundException
                    ? ToProjectLifecycleFailure(ex)
                    : ToBadRequest(ex);
            }
        });

        // 创建工程
        app.MapPost("/api/projects", async (
            CreateProjectRequest request,
            ProjectService service,
            ProjectLifecycleCoordinator lifecycleCoordinator,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            try
            {
                if (request.ClientOperationId.HasValue)
                {
                    var result = await lifecycleCoordinator.CreateBlankAsync(
                        GetAuthenticatedUserId(context),
                        request,
                        cancellationToken);
                    var response = new
                    {
                        projectId = result.Project.Id,
                        project = result.Project,
                        operationReplayed = result.OperationReplayed,
                        operation = result.Operation
                    };
                    return result.OperationReplayed
                        ? Results.Ok(response)
                        : Results.Created($"/api/projects/{result.Project.Id}", response);
                }

                var project = await service.CreateAsync(request);
                return Results.Created($"/api/projects/{project.Id}", project);
            }
            catch (Exception ex)
            {
                return request.ClientOperationId.HasValue
                    ? ToProjectLifecycleFailure(ex)
                    : Results.BadRequest(new { Error = ex.Message });
            }
        })
        .RequireClearVisionPermission(ClearVisionPermissionPolicies.CanEditProject);

        app.MapGet("/api/project-operations/{clientOperationId:guid}", async (
            Guid clientOperationId,
            string? kind,
            ProjectLifecycleCoordinator lifecycleCoordinator,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var operationKind = kind?.Trim().ToLowerInvariant() switch
                {
                    "create" => ProjectLifecycleOperationKind.Create,
                    "delete" => ProjectLifecycleOperationKind.Delete,
                    _ => throw new ProjectLifecycleValidationException(
                        "PROJECT_VALIDATION_OPERATION_KIND_INVALID",
                        "kind must be create or delete.")
                };
                var operation = await lifecycleCoordinator.GetOperationAsync(
                    GetAuthenticatedUserId(context),
                    clientOperationId,
                    operationKind,
                    cancellationToken);
                return Results.Ok(operation);
            }
            catch (Exception ex)
            {
                return ToProjectLifecycleFailure(ex);
            }
        });

        app.MapPost("/api/projects/{id:guid}/open", async (
            Guid id,
            ProjectService service) =>
        {
            try
            {
                var opened = await service.OpenAsync(id, DateTime.UtcNow);
                return Results.Ok(opened);
            }
            catch (Exception ex)
            {
                return ToProjectLifecycleFailure(ex);
            }
        });

        // 更新工程
        app.MapPut("/api/projects/{id:guid}", async (
            Guid id,
            UpdateProjectRequest request,
            ProjectService service,
            IInspectionRuntimeCoordinator runtimeCoordinator) =>
        {
            var requiresMutationLease = request.Flow != null || request.GlobalVariables != null;
            await using var mutationLease = requiresMutationLease
                ? await runtimeCoordinator.TryAcquireMutationLeaseAsync(id, "project-update", CancellationToken.None)
                : null;
            if (requiresMutationLease && mutationLease == null)
            {
                return Results.Conflict(new
                {
                    Code = "PROJECT_MUTATION_CONFLICT",
                    CompatibilityCode = "GV031",
                    Error = "Project is currently running."
                });
            }

            try
            {
                var project = await service.UpdateAsync(id, request);
                return Results.Ok(project);
            }
            catch (Exception ex)
            {
                return ToProjectUpdateFailure(ex);
            }
        })
        .RequireClearVisionPermission(ClearVisionPermissionPolicies.CanEditProject);

        // 删除工程
        app.MapPost("/api/projects/{id:guid}/delete", async (
            Guid id,
            DeleteProjectRequest request,
            ProjectLifecycleCoordinator lifecycleCoordinator,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var deleted = await lifecycleCoordinator.DeleteAsync(
                    GetAuthenticatedUserId(context),
                    id,
                    request,
                    waitForCleanup: false,
                    cancellationToken);
                return Results.Ok(new
                {
                    projectId = deleted.ProjectId,
                    operationReplayed = deleted.OperationReplayed,
                    operation = deleted.Operation
                });
            }
            catch (Exception ex)
            {
                return ToProjectLifecycleFailure(ex);
            }
        })
        .RequireClearVisionPermission(ClearVisionPermissionPolicies.CanEditProject);

        app.MapDelete("/api/projects/{id:guid}", async (
            Guid id,
            ProjectLifecycleCoordinator lifecycleCoordinator,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            try
            {
                await lifecycleCoordinator.DeleteLegacyAsync(
                    GetAuthenticatedUserId(context),
                    id,
                    cancellationToken);
                return Results.NoContent();
            }
            catch (Exception ex)
            {
                return ToProjectLifecycleFailure(ex);
            }
        })
        .RequireClearVisionPermission(ClearVisionPermissionPolicies.CanEditProject);

        // 更新流程
        app.MapPut("/api/projects/{id:guid}/flow", async (
            Guid id,
            UpdateFlowRequest request,
            ProjectService service,
            IInspectionRuntimeCoordinator runtimeCoordinator) =>
        {
            await using var mutationLease = await runtimeCoordinator.TryAcquireMutationLeaseAsync(id, "flow-update", CancellationToken.None);
            if (mutationLease == null)
            {
                return Results.Conflict(new { Code = "GV031", Error = "Project is currently running." });
            }

            try
            {
                // 使用 ProjectService 处理更新，它现在使用文件存储
                // 这种方式完全绕过了 EF Core 的复杂状态管理和 Table Splitting 问题
                var project = await service.UpdateFlowAsync(id, request);

                return Results.Ok(new
                {
                    Message = "流程已更新 (File Based)",
                    OperatorCount = request.Operators.Count,
                    ConnectionCount = request.Connections.Count,
                    ProjectId = project.Id,
                    PersistenceRevision = project.PersistenceRevision,
                    Flow = project.Flow
                });
            }
            catch (Exception ex)
            {
                // 日志已由全局异常中间件记录
                return ToProjectUpdateFailure(ex);
            }
        })
        .RequireClearVisionPermission(ClearVisionPermissionPolicies.CanEditProject);

        app.MapGet("/api/projects/{id:guid}/global-variables", async (
            Guid id,
            ProjectService service,
            IServiceProvider serviceProvider) =>
        {
            try
            {
                await using var projectAccess = await AcquireProjectAccessAsync(serviceProvider, id);
                var project = await service.GetByIdUnderProjectAccessAsync(id);
                return project != null ? Results.Ok(project.GlobalVariables) : Results.NotFound();
            }
            catch (Exception ex)
            {
                return ToBadRequest(ex);
            }
        });

        app.MapPut("/api/projects/{id:guid}/global-variables", async (
            Guid id,
            ProjectGlobalVariableSchema schema,
            ProjectService service,
            IInspectionRuntimeCoordinator runtimeCoordinator) =>
        {
            await using var mutationLease = await runtimeCoordinator.TryAcquireMutationLeaseAsync(id, "global-variable-schema-update", CancellationToken.None);
            if (mutationLease == null)
            {
                return Results.Conflict(new { Code = "GV031", Error = "Project is currently running." });
            }

            try
            {
                var saved = await service.UpdateGlobalVariablesAsync(id, schema);
                return Results.Ok(saved);
            }
            catch (Exception ex)
            {
                return ToProjectUpdateFailure(ex);
            }
        })
        .RequireClearVisionPermission(ClearVisionPermissionPolicies.CanEditProject);

        app.MapGet("/api/projects/{id:guid}/global-variable-values", async (
            Guid id,
            ProjectService service,
            ProjectVariableSessionRegistry sessions,
            IServiceProvider serviceProvider) =>
        {
            try
            {
                await using var projectAccess = await AcquireProjectAccessAsync(serviceProvider, id);
                var project = await service.GetByIdUnderProjectAccessAsync(id);
                if (project == null)
                {
                    return Results.NotFound();
                }

                var session = sessions.GetOrCreate(project);
                return Results.Ok(ToProjectVariableValueDtos(project.GlobalVariables, session));
            }
            catch (Exception ex)
            {
                return ToBadRequest(ex);
            }
        });

        app.MapPut("/api/projects/{id:guid}/global-variable-values/{variableId:guid}", async (
            Guid id,
            Guid variableId,
            ProjectVariableValueWriteRequest request,
            ProjectService service,
            ProjectVariableSessionRegistry sessions,
            IInspectionRuntimeCoordinator runtimeCoordinator,
            IServiceProvider serviceProvider) =>
        {
            await using var mutationLease = await runtimeCoordinator.TryAcquireMutationLeaseAsync(id, "global-variable-manual-write", CancellationToken.None);
            if (mutationLease == null)
            {
                return Results.Conflict(new { Code = "GV031", Error = "Project is currently running." });
            }

            try
            {
                await using var projectAccess = await AcquireProjectAccessAsync(serviceProvider, id);
                var project = await service.GetByIdUnderProjectAccessAsync(id);
                if (project == null)
                {
                    return Results.NotFound();
                }

                var session = sessions.GetOrCreate(project);
                if (!session.TryGetDefinition(variableId, out var definition))
                {
                    return Results.NotFound(new { Error = "Variable not found." });
                }

                if (!definition.ManualWriteAllowed)
                {
                    return Results.BadRequest(new { Code = "GV030", Error = "Manual write is not allowed for this variable." });
                }

                if (!sessions.TryMutateAndPersist(
                        id,
                        project.GlobalVariables,
                        candidate => candidate.SetValue(variableId, request.Value, ProjectVariableUpdatedBy.StudioManual),
                        request.ExpectedVersion.HasValue
                            ? new Dictionary<Guid, long> { [variableId] = request.ExpectedVersion.Value }
                            : null,
                        out var updatedSession,
                        out var error))
                {
                    return ToProjectVariableMutationFailure(error);
                }

                return Results.Ok(ToProjectVariableValueDtos(project.GlobalVariables, updatedSession));
            }
            catch (Exception ex)
            {
                return ToBadRequest(ex);
            }
        })
        .RequireClearVisionPermission(ClearVisionPermissionPolicies.CanEditProject);

        app.MapPost("/api/projects/{id:guid}/global-variable-values/reset", async (
            Guid id,
            ProjectService service,
            ProjectVariableSessionRegistry sessions,
            IInspectionRuntimeCoordinator runtimeCoordinator,
            IServiceProvider serviceProvider,
            HttpContext context) =>
        {
            await using var mutationLease = await runtimeCoordinator.TryAcquireMutationLeaseAsync(id, "global-variable-reset-all", CancellationToken.None);
            if (mutationLease == null)
            {
                return Results.Conflict(new { Code = "GV031", Error = "Project is currently running." });
            }

            try
            {
                await using var projectAccess = await AcquireProjectAccessAsync(serviceProvider, id);
                var project = await service.GetByIdUnderProjectAccessAsync(id);
                if (project == null)
                {
                    return Results.NotFound();
                }

                var blockedVariable = project.GlobalVariables.Variables.FirstOrDefault(variable => !variable.ManualWriteAllowed);
                if (blockedVariable != null)
                {
                    return Results.BadRequest(new
                    {
                        Code = "GV030",
                        Error = "Manual reset is not allowed for one or more variables.",
                        blockedVariable.Id,
                        blockedVariable.Name
                    });
                }

                var request = await ReadOptionalJsonBodyAsync<ProjectVariableResetAllRequest>(context);
                if (!sessions.TryMutateAndPersist(
                        id,
                        project.GlobalVariables,
                        candidate => candidate.ResetAll(ProjectVariableUpdatedBy.Reset),
                        request?.ExpectedVersions?.Count > 0 ? request.ExpectedVersions : null,
                        out var updatedSession,
                        out var error))
                {
                    return ToProjectVariableMutationFailure(error);
                }

                return Results.Ok(ToProjectVariableValueDtos(project.GlobalVariables, updatedSession));
            }
            catch (Exception ex)
            {
                return ToBadRequest(ex);
            }
        })
        .RequireClearVisionPermission(ClearVisionPermissionPolicies.CanEditProject);

        app.MapPost("/api/projects/{id:guid}/global-variable-values/{variableId:guid}/reset", async (
            Guid id,
            Guid variableId,
            ProjectService service,
            ProjectVariableSessionRegistry sessions,
            IInspectionRuntimeCoordinator runtimeCoordinator,
            IServiceProvider serviceProvider,
            HttpContext context) =>
        {
            await using var mutationLease = await runtimeCoordinator.TryAcquireMutationLeaseAsync(id, "global-variable-reset-one", CancellationToken.None);
            if (mutationLease == null)
            {
                return Results.Conflict(new { Code = "GV031", Error = "Project is currently running." });
            }

            try
            {
                await using var projectAccess = await AcquireProjectAccessAsync(serviceProvider, id);
                var project = await service.GetByIdUnderProjectAccessAsync(id);
                if (project == null)
                {
                    return Results.NotFound();
                }

                var session = sessions.GetOrCreate(project);
                if (!session.TryGetDefinition(variableId, out var definition))
                {
                    return Results.NotFound(new { Error = "Variable not found." });
                }

                if (!definition.ManualWriteAllowed)
                {
                    return Results.BadRequest(new { Code = "GV030", Error = "Manual reset is not allowed for this variable." });
                }

                var request = await ReadOptionalJsonBodyAsync<ProjectVariableResetRequest>(context);
                if (!sessions.TryMutateAndPersist(
                        id,
                        project.GlobalVariables,
                        candidate => candidate.Reset(variableId, ProjectVariableUpdatedBy.Reset),
                        request?.ExpectedVersion.HasValue == true
                            ? new Dictionary<Guid, long> { [variableId] = request.ExpectedVersion.Value }
                            : null,
                        out var updatedSession,
                        out var error))
                {
                    return ToProjectVariableMutationFailure(error);
                }

                return Results.Ok(ToProjectVariableValueDtos(project.GlobalVariables, updatedSession));
            }
            catch (Exception ex)
            {
                return ToBadRequest(ex);
            }
        })
        .RequireClearVisionPermission(ClearVisionPermissionPolicies.CanEditProject);

        app.MapPost("/api/projects/{id:guid}/runtime-package/export", async (
            Guid id,
            ExportRuntimePackageRequest? request,
            ProjectService service,
            RuntimePackageExporter exporter,
            StationPackageStore packageStore,
            IInspectionRuntimeCoordinator runtimeCoordinator,
            IServiceProvider serviceProvider,
            CancellationToken cancellationToken) =>
        {
            try
            {
                await using var mutationLease = await runtimeCoordinator.TryAcquireMutationLeaseAsync(id, "runtime-package-export", cancellationToken);
                if (mutationLease == null)
                {
                    return Results.Conflict(new { Code = "GV031", Error = "Project is currently running." });
                }

                await using var projectAccess = await AcquireProjectAccessAsync(serviceProvider, id, cancellationToken);
                var project = projectAccess == null
                    ? await service.GetByIdAsync(id)
                    : await service.GetByIdUnderProjectAccessAsync(id);
                if (project == null)
                {
                    return Results.NotFound(new { Error = "Project not found." });
                }

                if (request?.Flow != null)
                {
                    project.Flow = request.Flow;
                }

                var assetStorageMetadata = await service.GetProjectAssetStorageMetadataUnderProjectAccessAsync(id);
                var exportResult = await exporter.ExportAsync(
                    new RuntimePackageExportRequest
                    {
                        Project = project,
                        TargetRootDirectory = request?.TargetRootDirectory,
                        ProjectAssetStorageMetadata = assetStorageMetadata,
                        RequireProjectAssetStorageMetadata = ProjectAssetJson.HasAssets(project.Assets)
                    },
                    cancellationToken);
                StationPackageManifestDto? stationPackage = null;
                if (request?.RegisterForStationDeployment != false)
                {
                    stationPackage = await packageStore.ImportRuntimePackageAsync(
                        exportResult.PackageRootPath,
                        exportResult.Manifest.CreatedBy,
                        cancellationToken);
                }

                return Results.Ok(new
                {
                    exportResult.PackageRootPath,
                    PackageId = exportResult.Manifest.PackageId,
                    PackageName = exportResult.Manifest.PackageName,
                    FlowHash = exportResult.Manifest.FlowHash,
                    DecisionConfigurationHash = exportResult.Manifest.DecisionConfigurationHash,
                    RegisteredForStationDeployment = stationPackage != null,
                    StationPackageId = stationPackage?.PackageId,
                    StationPackage = stationPackage,
                    exportResult.ValidationReport,
                    exportResult.ReadmePath
                });
            }
            catch (RuntimePackageException ex)
            {
                return Results.BadRequest(new
                {
                    Error = ex.Message,
                    Validation = ex.ValidationResult
                });
            }
            catch (Exception ex)
            {
                return ToBadRequest(ex);
            }
        })
        .RequireClearVisionPermission(ClearVisionPermissionPolicies.RequireAdmin);
    }

    private static void MapInspectionEndpoints(IEndpointRouteBuilder app)
    {
        app.MapPost("/api/inspection/decision-configuration/validate", (OperatorFlowDto request) =>
        {
            var flow = request.ToEntity();
            var issues = FinalDecisionResolver.Validate(flow);
            var candidates = FinalDecisionConfigurationCatalog.GetEligibleOutputs(flow);
            return Results.Ok(new
            {
                IsValid = issues.Count == 0,
                Issues = issues.Select(ToDecisionConfigurationIssueResponse).ToList(),
                EligibleOutputs = candidates
            });
        })
        .RequireClearVisionPermission(ClearVisionPermissionPolicies.CanEditProject);

        // Admission is deliberately a projection of the persisted Project snapshot.
        // It does not create a Runtime session, reservation, or execute capability.
        app.MapPost("/api/inspection/admission", async (
            StudioInspectionRunAdmissionRequest request,
            Core.Services.IInspectionService service,
            CancellationToken cancellationToken) =>
        {
            if (request.ProjectId == Guid.Empty || request.ClientSnapshotId == Guid.Empty ||
                request.ExpectedPersistenceRevision < 0)
            {
                return Results.BadRequest(new
                {
                    Code = "ADMISSION_IDENTITY_INVALID",
                    Error = "ProjectId, clientSnapshotId, and a non-negative persistence revision are required."
                });
            }

            try
            {
                var admission = await service.AdmitPersistedStudioRunAsync(
                    request.ProjectId,
                    request.ExpectedPersistenceRevision,
                    request.ClientSnapshotId,
                    cancellationToken);
                return Results.Ok(new
                {
                    allowed = true,
                    code = (string?)null,
                    message = "Persisted Project snapshot admitted.",
                    projectId = admission.ProjectId,
                    clientSnapshotId = admission.ClientSnapshotId,
                    projectPersistenceRevision = admission.PersistenceRevision,
                    canonicalFlowHash = admission.CanonicalFlowHash,
                    decisionConfigurationHash = admission.DecisionConfigurationHash,
                    violations = Array.Empty<object>()
                });
            }
            catch (ExecutionAdmissionService.ExecutionAdmissionRejectedException ex)
            {
                return Results.Ok(new
                {
                    allowed = false,
                    code = ex.Admission.Code,
                    message = ex.Admission.Message,
                    projectId = request.ProjectId,
                    clientSnapshotId = request.ClientSnapshotId,
                    projectPersistenceRevision = (long?)null,
                    canonicalFlowHash = (string?)null,
                    decisionConfigurationHash = (string?)null,
                    violations = ex.Admission.Violations
                });
            }
            catch (StudioInspectionRunIdentityException ex)
            {
                return Results.Conflict(new { Code = ex.Code, Error = ex.Message });
            }
            catch (ProjectNotFoundException)
            {
                return Results.NotFound(new { Code = "ADMISSION_PROJECT_NOT_FOUND", Error = "Project was not found." });
            }
        })
        .RequireClearVisionPermission(ClearVisionPermissionPolicies.CanOperateHardware);

        app.MapPost("/api/inspection/stop", async (
            StudioInspectionRunIdentityRequest request,
            Core.Services.IInspectionService service,
            CancellationToken cancellationToken) =>
        {
            if (!TryCreateStudioRunIdentity(request, out var identity, out var error))
            {
                return Results.BadRequest(new { Code = "RUN_IDENTITY_INVALID", Error = error });
            }

            var reconciliation = await service.StopPersistedStudioRunAsync(identity, cancellationToken);
            return Results.Ok(ToInspectionRunReconciliationResponse(reconciliation));
        })
        .RequireClearVisionPermission(ClearVisionPermissionPolicies.CanOperateHardware);

        app.MapPost("/api/inspection/reconcile", async (
            StudioInspectionRunIdentityRequest request,
            Core.Services.IInspectionService service,
            CancellationToken cancellationToken) =>
        {
            if (!TryCreateStudioRunIdentity(request, out var identity, out var error))
            {
                return Results.BadRequest(new { Code = "RUN_IDENTITY_INVALID", Error = error });
            }

            var reconciliation = await service.ReconcilePersistedStudioRunAsync(identity, cancellationToken);
            return Results.Ok(ToInspectionRunReconciliationResponse(reconciliation));
        })
        .RequireClearVisionPermission(ClearVisionPermissionPolicies.CanOperateHardware);

        // 执行检测
        app.MapPost("/api/inspection/execute", async (
            ExecuteInspectionRequest request,
            Core.Services.IInspectionService service,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var hasWorkspaceIdentity = request.ClientSnapshotId.HasValue ||
                    request.ExpectedPersistenceRevision.HasValue ||
                    !string.IsNullOrWhiteSpace(request.ExpectedCanonicalFlowHash) ||
                    !string.IsNullOrWhiteSpace(request.ExpectedDecisionConfigurationHash);
                if (hasWorkspaceIdentity)
                {
                    if (request.ClientSnapshotId is not { } clientSnapshotId || clientSnapshotId == Guid.Empty ||
                        request.ExpectedPersistenceRevision is not { } expectedPersistenceRevision ||
                        expectedPersistenceRevision < 0 ||
                        string.IsNullOrWhiteSpace(request.ExpectedCanonicalFlowHash) ||
                        string.IsNullOrWhiteSpace(request.ExpectedDecisionConfigurationHash))
                    {
                        return Results.BadRequest(new
                        {
                            Code = "ADMISSION_IDENTITY_INVALID",
                            Error = "Workspace execute requires a complete persisted snapshot identity."
                        });
                    }

                    if (request.FlowData != null || !string.IsNullOrEmpty(request.ImageBase64) ||
                        !string.IsNullOrEmpty(request.CameraId))
                    {
                        return Results.BadRequest(new
                        {
                            Code = "ADMISSION_PERSISTED_SNAPSHOT_REQUIRED",
                            Error = "Workspace Run executes the admitted persisted Project snapshot and does not accept FlowData or ad-hoc input."
                        });
                    }

                    var result = await service.ExecutePersistedStudioRunAsync(
                        request.ProjectId,
                        expectedPersistenceRevision,
                        clientSnapshotId,
                        request.ExpectedCanonicalFlowHash,
                        request.ExpectedDecisionConfigurationHash,
                        cancellationToken);
                    return Results.Ok(ToInspectionExecutionResponse(result));
                }

                if (!string.IsNullOrEmpty(request.ImageBase64))
                {
                    if (!ImagePayloadDecoder.TryDecodeBytes(request.ImageBase64, "ImageBase64", out var imageData, out var decodeError, out var statusCode))
                    {
                        return ImagePayloadDecoder.ToErrorResult(decodeError, statusCode);
                    }

                    var result = await service.ExecuteSingleAsync(
                        request.ProjectId,
                        imageData,
                        request.FlowData?.ToEntity(),
                        cancellationToken);
                    return Results.Ok(ToInspectionExecutionResponse(result));
                }
                else if (!string.IsNullOrEmpty(request.CameraId))
                {
                    var result = await service.ExecuteSingleAsync(
                        request.ProjectId,
                        request.CameraId,
                        request.FlowData?.ToEntity(),
                        cancellationToken);
                    return Results.Ok(ToInspectionExecutionResponse(result));
                }
                else
                {
                    // 【关键修复】如果前端提供了流程数据，则转换并使用
                    // 这确保前端编辑的参数值能正确传递到后端执行
                    OperatorFlow? flow = request.FlowData?.ToEntity();
                    // 前端流程数据已通过日志中间件记录

                    var result = await service.ExecuteSingleAsync(
                        request.ProjectId,
                        (byte[])null!,
                        flow,
                        cancellationToken);
                    return Results.Ok(ToInspectionExecutionResponse(result));
                }
            }
            catch (ExecutionAdmissionService.ExecutionAdmissionRejectedException ex)
            {
                return ToAdmissionFailure(ex.Admission);
            }
            catch (StudioInspectionRunIdentityException ex)
            {
                return Results.Conflict(new { Code = ex.Code, Error = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                if (TryParseStableError(ex.Message, out var code, out var message))
                {
                    return Results.BadRequest(new { Code = code, Error = message });
                }

                return Results.Conflict(new { Code = "GV031", Error = ex.Message });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { Error = ex.Message });
            }
        })
        .RequireClearVisionPermission(ClearVisionPermissionPolicies.CanOperateHardware);

        // 获取检测历史
        app.MapGet("/api/inspection/history/{projectId:guid}", async (
        Guid projectId,
        [FromServices] Core.Services.IInspectionService service,
        DateTime? startTime,
        DateTime? endTime,
        string? status,
        string? defectType,
        string? flowVersionHash,
        int pageIndex = 0,
        int pageSize = 20) =>
        {
            var results = await service.GetInspectionHistoryAsync(projectId, startTime, endTime, status, defectType, pageIndex, pageSize, flowVersionHash);
            return Results.Ok(ToInspectionHistoryListResponse(results));
        });

        app.MapGet("/api/inspection/history/{projectId:guid}/compare", async (
        Guid projectId,
        Guid? leftId,
        Guid? rightId,
        [FromServices] Core.Services.IInspectionService service) =>
        {
            if (!leftId.HasValue || !rightId.HasValue || leftId.Value == Guid.Empty || rightId.Value == Guid.Empty)
            {
                return Results.BadRequest(new { Error = "leftId and rightId are required." });
            }

            var comparison = await service.CompareInspectionHistoryAsync(projectId, leftId.Value, rightId.Value);
            return comparison == null
                ? Results.NotFound(new { Error = "Inspection history comparison result was not found." })
                : Results.Ok(ToInspectionHistoryComparisonResponse(comparison));
        });

        app.MapGet("/api/inspection/history/{projectId:guid}/{resultId:guid}", async (
        Guid projectId,
        Guid resultId,
        [FromServices] Core.Services.IInspectionService service,
        [FromServices] IInspectionEvidenceManifestService evidenceService) =>
        {
            var result = await service.GetInspectionHistoryDetailAsync(projectId, resultId);
            if (result == null)
            {
                return Results.NotFound(new { Error = "Inspection history result was not found." });
            }

            var evidenceSummary = await evidenceService.GetSummaryAsync(result);
            return Results.Ok(ToInspectionHistoryDetailResponse(result, evidenceSummary));
        });

        app.MapGet("/api/inspection/history/{projectId:guid}/{resultId:guid}/evidence/manifest", async (
        Guid projectId,
        Guid resultId,
        [FromServices] IInspectionEvidenceManifestService evidenceService) =>
        {
            var manifest = await evidenceService.GetManifestAsync(projectId, resultId);
            return string.Equals(manifest.ErrorCode, "InspectionResultNotFound", StringComparison.Ordinal)
                ? Results.NotFound(new { manifest.ErrorCode, manifest.Message })
                : Results.Ok(ToInspectionEvidenceManifestResponse(manifest));
        });

        app.MapGet("/api/inspection/history/{projectId:guid}/{resultId:guid}/evidence/export", async (
        Guid projectId,
        Guid resultId,
        HttpContext httpContext,
        [FromServices] IInspectionEvidenceManifestService evidenceService) =>
        {
            var export = await evidenceService.ExportAsync(projectId, resultId);
            if (!export.Success)
            {
                if (string.Equals(export.ErrorCode, "InspectionResultNotFound", StringComparison.Ordinal))
                {
                    return Results.NotFound(new { export.ErrorCode, export.Message });
                }

                var statusCode = string.Equals(export.ErrorCode, "EvidenceExportTooLarge", StringComparison.Ordinal)
                    ? StatusCodes.Status413PayloadTooLarge
                    : StatusCodes.Status409Conflict;
                return Results.Json(new
                {
                    export.Status,
                    export.ErrorCode,
                    export.Message,
                    export.TotalBytes
                }, statusCode: statusCode);
            }

            if (!string.IsNullOrWhiteSpace(export.Sha256))
            {
                httpContext.Response.Headers["X-Evidence-Export-Sha256"] = export.Sha256;
            }

            return Results.File(export.Content, export.ContentType, export.FileName);
        });

        app.MapGet("/api/inspection/history/{projectId:guid}/{resultId:guid}/previous-success", async (
        Guid projectId,
        Guid resultId,
        [FromServices] Core.Services.IInspectionService service,
        int limit = 50) =>
        {
            var reference = await service.FindPreviousSuccessfulInspectionAsync(projectId, resultId, limit <= 0 ? 50 : limit);
            return reference == null
                ? Results.NotFound(new { Error = "Inspection history result was not found." })
                : Results.Ok(ToInspectionPreviousSuccessResponse(reference));
        });

        // 获取统计信息
        app.MapGet("/api/inspection/statistics/{projectId:guid}", async (
        Guid projectId,
        Core.Services.IInspectionService service,
        DateTime? startTime,
        DateTime? endTime,
        string? status,
        string? defectType) =>
        {
            var statistics = await service.GetStatisticsAsync(projectId, startTime, endTime, status, defectType);
            return Results.Ok(statistics);
        });

        // 【第二优先级】启动实时检测
        app.MapPost("/api/inspection/realtime/start", async (
            StartRealtimeInspectionRequest request,
            Core.Services.IInspectionService service,
            WebMessageHandler webMessageHandler,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var hasPersistedIdentity = request.ClientSnapshotId.HasValue ||
                    request.ExpectedPersistenceRevision.HasValue ||
                    !string.IsNullOrWhiteSpace(request.ExpectedCanonicalFlowHash) ||
                    !string.IsNullOrWhiteSpace(request.ExpectedDecisionConfigurationHash);
                if (hasPersistedIdentity)
                {
                    if (request.ProjectId == Guid.Empty ||
                        request.ClientSnapshotId is not { } clientSnapshotId || clientSnapshotId == Guid.Empty ||
                        request.ExpectedPersistenceRevision is not { } expectedRevision || expectedRevision < 0 ||
                        string.IsNullOrWhiteSpace(request.ExpectedCanonicalFlowHash) ||
                        string.IsNullOrWhiteSpace(request.ExpectedDecisionConfigurationHash))
                    {
                        return Results.BadRequest(new
                        {
                            Code = "ADMISSION_IDENTITY_INVALID",
                            Error = "Continuous inspection requires a complete persisted Project identity."
                        });
                    }

                    if (request.FlowData != null)
                    {
                        return Results.BadRequest(new
                        {
                            Code = "ADMISSION_PERSISTED_SNAPSHOT_REQUIRED",
                            Error = "Studio UI Next continuous inspection does not accept FlowData."
                        });
                    }

                    var admission = await service.StartPersistedRealtimeInspectionAsync(
                        new StudioInspectionRunIdentity(
                            request.ProjectId,
                            clientSnapshotId,
                            expectedRevision,
                            request.ExpectedCanonicalFlowHash,
                            request.ExpectedDecisionConfigurationHash),
                        request.CameraId,
                        cancellationToken,
                        result => webMessageHandler.NotifyInspectionResult(result, request.ProjectId));
                    return Results.Ok(new
                    {
                        message = "Persisted Project continuous inspection started.",
                        projectId = admission.ProjectId,
                        clientSnapshotId = admission.ClientSnapshotId,
                        persistenceRevision = admission.PersistenceRevision,
                        canonicalFlowHash = admission.CanonicalFlowHash,
                        decisionConfigurationHash = admission.DecisionConfigurationHash,
                        runMode = "canonical-project",
                        cameraId = request.CameraId
                    });
                }

                // 根据运行模式选择启动方式
                var runMode = request.RunMode?.ToLower() ?? "camera";
                var requestFlow = request.FlowData?.ToEntity();

                if (runMode == "flow" && requestFlow != null)
                {
                    // 流程驱动模式
                    await service.StartRealtimeInspectionFlowAsync(
                        request.ProjectId,
                        requestFlow,
                        request.CameraId,
                        cancellationToken,
                        result => webMessageHandler.NotifyInspectionResult(result, request.ProjectId));

                    return Results.Ok(new
                    {
                        Message = "实时检测已启动 (流程驱动模式)",
                        ProjectId = request.ProjectId,
                        RunMode = "flow",
                        CameraId = request.CameraId
                    });
                }
                else if (requestFlow?.Operators?.Count > 0)
                {
                    // 相机驱动模式也必须使用前端当前流程，避免画布参数更新后仍执行持久化旧流程。
                    await service.StartRealtimeInspectionFlowAsync(
                        request.ProjectId,
                        requestFlow,
                        request.CameraId,
                        cancellationToken,
                        result => webMessageHandler.NotifyInspectionResult(result, request.ProjectId));

                    return Results.Ok(new
                    {
                        Message = "实时检测已启动 (相机驱动模式)",
                        ProjectId = request.ProjectId,
                        RunMode = "camera",
                        CameraId = request.CameraId
                    });
                }
                else
                {
                    // 相机驱动模式
                    await service.StartRealtimeInspectionAsync(
                        request.ProjectId,
                        request.CameraId,
                        cancellationToken,
                        result => webMessageHandler.NotifyInspectionResult(result, request.ProjectId));

                    return Results.Ok(new
                    {
                        Message = "实时检测已启动 (相机驱动模式)",
                        ProjectId = request.ProjectId,
                        RunMode = "camera",
                        CameraId = request.CameraId
                    });
                }
            }
            catch (ExecutionAdmissionService.ExecutionAdmissionRejectedException ex)
            {
                return ToAdmissionFailure(ex.Admission);
            }
            catch (StudioInspectionRunIdentityException ex)
            {
                return Results.Conflict(new { Code = ex.Code, Error = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                if (TryParseStableError(ex.Message, out var code, out var message))
                {
                    return Results.BadRequest(new { Code = code, Error = message });
                }

                return Results.Conflict(new { Error = ex.Message });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { Error = ex.Message });
            }
        })
        .RequireClearVisionPermission(ClearVisionPermissionPolicies.CanOperateHardware);

        // 【第二优先级】停止实时检测
        app.MapPost("/api/inspection/realtime/stop", async (
            StopRealtimeInspectionRequest request,
            Core.Services.IInspectionService service) =>
        {
            try
            {
                var hasPersistedIdentity = request.ClientSnapshotId.HasValue ||
                    request.ExpectedPersistenceRevision.HasValue ||
                    !string.IsNullOrWhiteSpace(request.ExpectedCanonicalFlowHash) ||
                    !string.IsNullOrWhiteSpace(request.ExpectedDecisionConfigurationHash);
                if (hasPersistedIdentity)
                {
                    if (request.ProjectId == Guid.Empty ||
                        request.ClientSnapshotId is not { } clientSnapshotId || clientSnapshotId == Guid.Empty ||
                        request.ExpectedPersistenceRevision is not { } expectedRevision || expectedRevision < 0 ||
                        string.IsNullOrWhiteSpace(request.ExpectedCanonicalFlowHash) ||
                        string.IsNullOrWhiteSpace(request.ExpectedDecisionConfigurationHash))
                    {
                        return Results.BadRequest(new { Code = "RUN_IDENTITY_INVALID", Error = "A complete continuous inspection identity is required." });
                    }
                    await service.StopPersistedRealtimeInspectionAsync(new StudioInspectionRunIdentity(
                        request.ProjectId, clientSnapshotId, expectedRevision,
                        request.ExpectedCanonicalFlowHash, request.ExpectedDecisionConfigurationHash));
                }
                else
                {
                    await service.StopRealtimeInspectionAsync(request.ProjectId);
                }
                return Results.Ok(new
                {
                    Message = "实时检测已停止",
                    ProjectId = request.ProjectId
                });
            }
            catch (StudioInspectionRunIdentityException ex)
            {
                return Results.Conflict(new { Code = ex.Code, Error = ex.Message });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { Error = ex.Message });
            }
        })
        .RequireClearVisionPermission(ClearVisionPermissionPolicies.CanOperateHardware);
    }

    private static object ToProjectVariableValueDtos(
        ProjectGlobalVariableSchema schema,
        IProjectVariableSession session)
    {
        var definitionsById = schema.Variables.ToDictionary(variable => variable.Id);
        return session.GetSnapshots().Select(snapshot =>
        {
            definitionsById.TryGetValue(snapshot.VariableId, out var definition);
            return new
            {
                snapshot.VariableId,
                Name = definition?.Name ?? string.Empty,
                DisplayName = definition?.DisplayName ?? definition?.Name ?? string.Empty,
                ValueType = definition?.ValueType.ToString() ?? string.Empty,
                Value = ToProjectVariableApiValue(definition, snapshot.Value),
                snapshot.Version,
                snapshot.UpdatedAtUtc,
                UpdatedBy = snapshot.UpdatedBy.ToString(),
                snapshot.RunId,
                snapshot.OperatorId,
                ManualWriteAllowed = definition?.ManualWriteAllowed ?? false,
                IncludeInResultMetadata = definition?.IncludeInResultMetadata ?? false
            };
        }).ToList();
    }

    private static object? ToProjectVariableApiValue(
        ProjectGlobalVariableDefinition? definition,
        System.Text.Json.JsonElement value)
    {
        if (definition?.ValueType == ProjectGlobalVariableValueType.Int64)
        {
            return value.ValueKind switch
            {
                System.Text.Json.JsonValueKind.Number when value.TryGetInt64(out var longValue) => longValue.ToString(System.Globalization.CultureInfo.InvariantCulture),
                System.Text.Json.JsonValueKind.String => value.GetString(),
                _ => ProjectVariableValueConverter.ToObject(value)?.ToString()
            };
        }

        return ProjectVariableValueConverter.ToObject(value);
    }

    private static IResult ToBadRequest(Exception ex)
    {
        return TryParseStableError(ex.Message, out var code, out var message)
            ? Results.BadRequest(new { Code = code, Error = message })
            : Results.BadRequest(new { Error = ex.Message });
    }

    private static object ToDecisionConfigurationIssueResponse(DecisionConfigurationIssue issue) => new
    {
        issue.Code,
        issue.Message,
        issue.OperatorId,
        issue.OutputName,
        Field = issue.Code switch
        {
            "DECISION_FLOW_REQUIRED" => "flow",
            "DECISION_BINDING_REQUIRED" => "decisionConfiguration.finalDecisionBinding",
            "DECISION_SOURCE_OPERATOR_NOT_FOUND" or "DECISION_SOURCE_OPERATOR_DISABLED" =>
                "decisionConfiguration.finalDecisionBinding.sourceOperatorId",
            "DECISION_SOURCE_OUTPUT_NOT_FOUND" or "DECISION_SOURCE_OUTPUT_MISMATCH" or
                "DECISION_SOURCE_OUTPUT_INELIGIBLE" =>
                "decisionConfiguration.finalDecisionBinding.sourceOutput",
            "DECISION_SOURCE_TYPE_MISMATCH" => "decisionConfiguration.finalDecisionBinding.dataType",
            "DECISION_RULE_CONTRACT_MISMATCH" or "DECISION_RULE_TYPE_MISMATCH" =>
                "decisionConfiguration.finalDecisionBinding.rule",
            "DECISION_STRING_MAP_VALUES_REQUIRED" or "DECISION_STRING_MAP_VALUES_CONFLICT" or
                "DECISION_STRING_MAP_CONSTRAINT_MISMATCH" =>
                "decisionConfiguration.finalDecisionBinding.stringMap",
            "DECISION_NUMERIC_COMPARISON_REQUIRED" =>
                "decisionConfiguration.finalDecisionBinding.numericComparison",
            _ => "decisionConfiguration.finalDecisionBinding"
        }
    };

    private static object ToGlobalVariableDiagnosticResponse(ProjectGlobalVariableDiagnostic diagnostic) => new
    {
        diagnostic.Code,
        diagnostic.Message,
        diagnostic.VariableId,
        diagnostic.OperatorId,
        diagnostic.PortId,
        diagnostic.ParameterId,
        Severity = diagnostic.Severity.ToString(),
        Field = diagnostic.Code switch
        {
            "GV001" => "globalVariables.schemaVersion",
            "GV002" or "GV003" => "globalVariables.variables.id",
            "GV004" or "GV013" => "globalVariables.variables.name",
            "GV005" or "GV014" or "GV015" => "globalVariables.variables.initialValue",
            "GV018" => "globalVariables.variables.min",
            "GV019" => "globalVariables.variables.max",
            "GV021" => "globalVariables.variables.range",
            "GV006" => "globalVariables.sourceBindings",
            "GV007" => "globalVariables.targetBindings",
            "GV008" => diagnostic.PortId.HasValue
                ? "globalVariables.sourceBindings.variableId"
                : "globalVariables.targetBindings.variableId",
            "GV009" or "GV020" => diagnostic.PortId.HasValue
                ? "globalVariables.sourceBindings.operatorId"
                : "globalVariables.targetBindings.operatorId",
            "GV010" => "globalVariables.sourceBindings.outputPortId",
            "GV011" => "globalVariables.targetBindings.parameterId",
            "GV017" or "GV022" => diagnostic.PortId.HasValue
                ? "globalVariables.sourceBindings.conversionMode"
                : "globalVariables.targetBindings.conversionMode",
            "RP101" or "RP107" or "RP122" => "globalVariables.sourceBindings.resultPath",
            "GV033" => diagnostic.PortId.HasValue
                ? "globalVariables.sourceBindings.expression"
                : "globalVariables.targetBindings.expression",
            _ => "globalVariables"
        }
    };

    private static IResult ToProjectUpdateFailure(Exception ex)
    {
        if (ex is ProjectNotFoundException)
        {
            return Results.NotFound(new { Code = "PROJECT_NOT_FOUND", Error = "Project was not found." });
        }

        if (ex is ProjectGlobalVariableSchemaValidationException validation)
        {
            var diagnostics = validation.Diagnostics
                .Select(ToGlobalVariableDiagnosticResponse)
                .ToList();
            return Results.BadRequest(new
            {
                Code = validation.Diagnostics[0].Code,
                Error = "Project global variable validation failed.",
                Diagnostics = diagnostics
            });
        }

        if (ex is ProjectSaveRevisionConflictException conflict)
        {
            return Results.Conflict(new
            {
                Code = "PROJECT_REVISION_CONFLICT",
                CompatibilityCode = "PSV011",
                Error = "Project flow was updated by another save. Refresh and retry.",
                ExpectedRevision = conflict.ExpectedRevision,
                ActualRevision = conflict.ActualRevision
            });
        }

        if (TryParseStableError(ex.Message, out var code, out var message))
        {
            return string.Equals(code, "PSV011", StringComparison.Ordinal)
                ? Results.Conflict(new
                {
                    Code = "PROJECT_REVISION_CONFLICT",
                    CompatibilityCode = code,
                    Error = "Project flow was updated by another save. Refresh and retry.",
                    Detail = message
                })
                : Results.BadRequest(new { Code = code, Error = message });
        }

        return Results.BadRequest(new { Error = ex.Message });
    }

    private static IResult ToProjectLifecycleFailure(Exception ex)
    {
        var code = ex is DomainException domainException
            ? domainException.ErrorCode
            : "PROJECT_OPERATION_RETRYABLE";
        var error = code switch
        {
            "PROJECT_NOT_FOUND" => "Project was not found.",
            "PROJECT_OPERATION_NOT_FOUND" => "Project operation was not found.",
            "PROJECT_REVISION_CONFLICT" => "Project revision changed. Refresh and retry with the server revision.",
            "PROJECT_MUTATION_CONFLICT" => "Project has an active run, save, or mutation.",
            "OPERATION_PAYLOAD_MISMATCH" => "clientOperationId was already used with a different payload.",
            "PROJECT_OPERATION_RETRYABLE" => "Project operation outcome must be reconciled before retrying.",
            "PROJECT_CLEANUP_RETRYABLE" => "Project is deleted; cleanup remains queued for retry.",
            _ when code.StartsWith("PROJECT_VALIDATION_", StringComparison.Ordinal) => ex.Message,
            _ => "Project lifecycle operation failed."
        };

        var body = new { Code = code, Error = error };
        return code switch
        {
            "PROJECT_NOT_FOUND" or "PROJECT_OPERATION_NOT_FOUND" => Results.NotFound(body),
            "PROJECT_REVISION_CONFLICT" or "PROJECT_MUTATION_CONFLICT" or "OPERATION_PAYLOAD_MISMATCH" => Results.Conflict(body),
            "PROJECT_OPERATION_RETRYABLE" or "PROJECT_CLEANUP_RETRYABLE" => Results.Json(body, statusCode: StatusCodes.Status503ServiceUnavailable),
            _ when code.StartsWith("PROJECT_VALIDATION_", StringComparison.Ordinal) => Results.BadRequest(body),
            _ => Results.Json(body, statusCode: StatusCodes.Status500InternalServerError)
        };
    }

    private static string GetAuthenticatedUserId(HttpContext context)
    {
        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ProjectLifecycleValidationException(
                "PROJECT_VALIDATION_USER_REQUIRED",
                "Authenticated user identity is required.");
        }

        return userId;
    }

    private static IResult ToProjectVariableMutationFailure(string? error)
    {
        if (TryParseStableError(error, out var code, out var message))
        {
            return code == "GV025"
                ? Results.Conflict(new { Code = code, Error = message })
                : Results.BadRequest(new { Code = code, Error = message });
        }

        return Results.BadRequest(new { Code = "GV032", Error = error });
    }

    private static async ValueTask<ProjectAccessLease?> AcquireProjectAccessAsync(
        IServiceProvider serviceProvider,
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        var coordinator = serviceProvider.GetService<ProjectSaveCoordinator>();
        return coordinator == null
            ? null
            : await coordinator.AcquireProjectAccessAsync(projectId, cancellationToken);
    }

    private static async ValueTask<T?> ReadOptionalJsonBodyAsync<T>(HttpContext context)
        where T : class
    {
        using var reader = new StreamReader(context.Request.Body, leaveOpen: true);
        var body = await reader.ReadToEndAsync();
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        return JsonSerializer.Deserialize<T>(body, OptionalRequestJsonOptions);
    }

    private static bool TryParseStableError(string? message, out string code, out string error)
    {
        code = string.Empty;
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(message))
        {
            return false;
        }

        var separatorIndex = message.IndexOf(':', StringComparison.Ordinal);
        var candidateCode = separatorIndex > 0 ? message[..separatorIndex] : message;
        if (!candidateCode.StartsWith("GV", StringComparison.OrdinalIgnoreCase) &&
            !candidateCode.StartsWith("PSV", StringComparison.OrdinalIgnoreCase) &&
            !candidateCode.StartsWith("ADMISSION", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        code = candidateCode;
        error = separatorIndex >= 0
            ? message[(separatorIndex + 1)..].TrimStart()
            : message;
        return true;
    }

    private static bool IsProjectRuntimeBusy(Guid projectId, IInspectionRuntimeCoordinator runtimeCoordinator)
    {
        var state = runtimeCoordinator.GetState(projectId);
        return state?.Status is RuntimeStatus.Starting or RuntimeStatus.Running or RuntimeStatus.Stopping;
    }

    internal static IReadOnlyList<OperatorMetadata> GetOperatorEndpointMetadata(
        IEnumerable<OperatorMetadata> metadata,
        bool includeCompatibility)
    {
        ArgumentNullException.ThrowIfNull(metadata);

        return metadata
            .Where(item => includeCompatibility || !item.DefaultHidden)
            .OrderBy(item => OperatorCategoryCatalog.GetOrder(item.CategoryId))
            .ThenBy(item => item.DisplayName, StringComparer.Ordinal)
            .ToList();
    }

    private static void MapOperatorEndpoints(IEndpointRouteBuilder app)
    {
        // 获取算子库
        app.MapGet("/api/operators/library", (IOperatorFactory factory, bool? includeCompatibility) =>
        {
            var metadata = GetOperatorEndpointMetadata(
                factory.GetAllMetadata(),
                includeCompatibility == true);
            return Results.Ok(metadata);
        });

        // 获取支持的算子类型
        app.MapGet("/api/operators/types", (IOperatorFactory factory, bool? includeCompatibility) =>
        {
            var types = GetOperatorEndpointMetadata(
                    factory.GetAllMetadata(),
                    includeCompatibility == true)
                .Select(item => item.Type);
            return Results.Ok(types);
        });

        // 获取算子元数据
        app.MapGet("/api/operators/{type}/metadata", (Core.Enums.OperatorType type, IOperatorFactory factory) =>
        {
            var metadata = factory.GetMetadata(type);
            return metadata != null ? Results.Ok(metadata) : Results.NotFound();
        });

        // 获取流程模板列表
        app.MapGet("/api/templates", async (
            IFlowTemplateService templateService,
            string? industry,
            CancellationToken cancellationToken) =>
        {
            var templates = await templateService.GetTemplatesAsync(industry, cancellationToken);
            return Results.Ok(templates);
        });

        // 获取单个流程模板详情
        app.MapGet("/api/templates/{id:guid}", async (
            Guid id,
            IFlowTemplateService templateService,
            CancellationToken cancellationToken) =>
        {
            var template = await templateService.GetTemplateAsync(id, cancellationToken);
            return template != null ? Results.Ok(template) : Results.NotFound();
        });

        // 创建流程模板
        app.MapPost("/api/templates", async (
            TemplateUpsertRequest request,
            IFlowTemplateService templateService,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request.Name))
                return Results.BadRequest(new { Error = "Template name is required." });

            var flowJson = ResolveFlowJson(request);
            if (string.IsNullOrWhiteSpace(flowJson))
                return Results.BadRequest(new { Error = "FlowJson or FlowData is required." });

            var template = new FlowTemplate
            {
                Id = Guid.Empty,
                Name = request.Name.Trim(),
                Description = request.Description?.Trim() ?? string.Empty,
                Industry = request.Industry?.Trim() ?? string.Empty,
                Tags = request.Tags?.Where(tag => !string.IsNullOrWhiteSpace(tag))
                    .Select(tag => tag.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList() ?? new List<string>(),
                FlowJson = flowJson
            };

            var created = await templateService.CreateTemplateAsync(template, cancellationToken);
            return Results.Created($"/api/templates/{created.Id}", created);
        })
        .RequireClearVisionPermission(ClearVisionPermissionPolicies.RequireEngineerOrAdmin);

        // 更新流程模板
        app.MapPut("/api/templates/{id:guid}", async (
            Guid id,
            TemplateUpsertRequest request,
            IFlowTemplateService templateService,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request.Name))
                return Results.BadRequest(new { Error = "Template name is required." });

            var flowJson = ResolveFlowJson(request);
            if (string.IsNullOrWhiteSpace(flowJson))
                return Results.BadRequest(new { Error = "FlowJson or FlowData is required." });

            var template = new FlowTemplate
            {
                Id = id,
                Name = request.Name.Trim(),
                Description = request.Description?.Trim() ?? string.Empty,
                Industry = request.Industry?.Trim() ?? string.Empty,
                Tags = request.Tags?.Where(tag => !string.IsNullOrWhiteSpace(tag))
                    .Select(tag => tag.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList() ?? new List<string>(),
                FlowJson = flowJson
            };

            var updated = await templateService.UpdateTemplateAsync(id, template, cancellationToken);
            return updated != null ? Results.Ok(updated) : Results.NotFound();
        })
        .RequireClearVisionPermission(ClearVisionPermissionPolicies.RequireEngineerOrAdmin);

        // 推荐算子参数
        app.MapPost("/api/operators/{type}/recommend-parameters", (
            Core.Enums.OperatorType type,
            OperatorParameterRecommendationRequest request,
            ParameterRecommender recommender) =>
        {
            if (!TryDecodeImage(request.ImageBase64, out var image, out var decodeError, out var statusCode))
            {
                return ImagePayloadDecoder.ToErrorResult(decodeError, statusCode);
            }

            using (image)
            {
                var parameters = recommender.Recommend(type, image);
                return Results.Ok(new
                {
                    OperatorType = type.ToString(),
                    Parameters = parameters
                });
            }
        });

        // 单算子调参预览
        app.MapPost("/api/operators/{type}/preview", async (
            Core.Enums.OperatorType type,
            OperatorPreviewRequest request,
            OperatorPreviewService previewService,
            CancellationToken cancellationToken) =>
        {
            if (!TryDecodeImage(request.ImageBase64, out var image, out var decodeError, out var statusCode))
            {
                return ImagePayloadDecoder.ToErrorResult(decodeError, statusCode);
            }

            using (image)
            {
                var preview = await previewService.PreviewAsync(type, request.Parameters, image, cancellationToken);
                return Results.Ok(preview);
            }
        });
    }

    private static bool TryDecodeImage(string? imageBase64, out Mat image, out string errorMessage, out int statusCode)
    {
        return ImagePayloadDecoder.TryDecodeImage(imageBase64, out image, out errorMessage, out statusCode);
    }

    private static string? ResolveFlowJson(TemplateUpsertRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.FlowJson))
            return request.FlowJson;

        if (request.FlowData == null)
            return null;

        return JsonSerializer.Serialize(request.FlowData);
    }

    internal static object ToInspectionHistoryListResponse(InspectionHistoryPage page)
    {
        return new
        {
            items = page.Items.Select(ToInspectionHistoryListItem).ToList(),
            totalCount = page.TotalCount,
            pageIndex = page.PageIndex,
            pageSize = page.PageSize
        };
    }

    private static object ToInspectionHistoryListItem(InspectionHistoryItem result)
    {
        var outcome = ResolveHistoryOutcome(
            result.Status,
            result.ExecutionOutcome,
            result.DecisionOutcome,
            result.DecisionSource,
            result.ReasonCode,
            result.HasJudgmentSignal,
            result.ErrorMessage);
        return new
        {
            id = result.Id,
            resultId = result.Id,
            projectId = result.ProjectId,
            status = result.Status.ToString(),
            executionOutcome = outcome.Execution.ToString(),
            decisionOutcome = outcome.Decision.ToString(),
            decisionSource = outcome.DecisionSource,
            reasonCode = outcome.ReasonCode,
            hasJudgmentSignal = outcome.HasJudgmentSignal,
            defectCount = result.Defects.Count,
            processingTime = result.ProcessingTimeMs,
            processingTimeMs = result.ProcessingTimeMs,
            executionTimeMs = result.ProcessingTimeMs,
            timestamp = result.InspectionTime,
            inspectionTime = result.InspectionTime,
            startedAt = result.CreatedAt,
            completedAt = result.InspectionTime,
            confidenceScore = result.ConfidenceScore,
            flowVersionHash = result.FlowVersionHash,
            calibrationBundleId = result.CalibrationBundleId,
            sessionId = result.SessionId,
            runId = result.SessionId,
            imageId = result.ImageId,
            hasImage = result.HasImage,
            imageReference = BuildImageReference(result.ImageId),
            hasOutputData = result.HasOutputData,
            hasAnalysisData = result.HasAnalysisData,
            diagnosticCode = outcome.ReasonCode,
            diagnosticMessage = outcome.Message,
            errorMessage = result.ErrorMessage,
            isHistoryListItem = true
        };
    }

    internal static object ToInspectionHistoryDetailResponse(
        InspectionHistoryDetail result,
        InspectionEvidenceSummary? evidenceSummary = null)
    {
        var outcome = ResolveHistoryOutcome(
            result.Status,
            result.ExecutionOutcome,
            result.DecisionOutcome,
            result.DecisionSource,
            result.ReasonCode,
            result.HasJudgmentSignal,
            result.ErrorMessage);
        var outputPreview = SafeJsonPreviewBuilder.Build(result.OutputDataJson);
        var analysisPreview = SafeJsonPreviewBuilder.Build(result.AnalysisDataJson);
        var hasImageReference = result.ImageId.HasValue;
        evidenceSummary ??= new InspectionEvidenceSummary
        {
            HasEvidenceManifest = false,
            EvidenceStatus = "missing",
            EvidenceManifestReference = $"/api/inspection/history/{result.ProjectId:D}/{result.Id:D}/evidence/manifest",
            Message = "证据清单缺失或已清理"
        };

        return new
        {
            id = result.Id,
            resultId = result.Id,
            projectId = result.ProjectId,
            status = result.Status.ToString(),
            executionOutcome = outcome.Execution.ToString(),
            decisionOutcome = outcome.Decision.ToString(),
            decisionSource = outcome.DecisionSource,
            reasonCode = outcome.ReasonCode,
            hasJudgmentSignal = outcome.HasJudgmentSignal,
            defects = result.Defects.Select(ToInspectionDefectListItem).ToList(),
            defectCount = result.Defects.Count,
            processingTime = result.ProcessingTimeMs,
            processingTimeMs = result.ProcessingTimeMs,
            executionTimeMs = result.ProcessingTimeMs,
            timestamp = result.InspectionTime,
            inspectionTime = result.InspectionTime,
            startedAt = result.CreatedAt,
            completedAt = result.InspectionTime,
            confidenceScore = result.ConfidenceScore,
            flowVersionHash = result.FlowVersionHash,
            calibrationBundleId = result.CalibrationBundleId,
            sessionId = result.SessionId,
            runId = result.SessionId,
            traceability = new
            {
                flowVersionHash = result.FlowVersionHash,
                calibrationBundleId = result.CalibrationBundleId,
                sessionId = result.SessionId,
                runId = result.SessionId,
                projectPersistenceRevision = result.ProjectPersistenceRevision,
                decisionConfigurationHash = result.DecisionConfigurationHash,
                packageId = (string?)null,
                stationId = (string?)null
            },
            imageId = result.ImageId,
            hasImage = result.HasImage,
            imageReference = BuildImageReference(result.ImageId),
            imageMissing = result.HasImage && !hasImageReference,
            imageMissingMessage = result.HasImage && !hasImageReference
                ? "图像文件不存在或已清理"
                : null,
            hasOutputData = result.HasOutputData,
            hasAnalysisData = result.HasAnalysisData,
            hasEvidenceManifest = evidenceSummary.HasEvidenceManifest,
            evidenceStatus = evidenceSummary.EvidenceStatus,
            evidenceManifestReference = evidenceSummary.EvidenceManifestReference,
            evidenceTotalBytes = evidenceSummary.EvidenceTotalBytes,
            retentionExpiresAtUtc = evidenceSummary.RetentionExpiresAtUtc,
            evidenceMessage = evidenceSummary.Message,
            outputDataPreview = outputPreview,
            analysisDataPreview = analysisPreview,
            diagnosticCode = outcome.ReasonCode,
            diagnosticMessage = outcome.Message,
            errorMessage = result.ErrorMessage,
            isHistoryDetail = true
        };
    }

    private static InspectionOutcome ResolveHistoryOutcome(
        InspectionStatus status,
        ExecutionOutcome? execution,
        DecisionOutcome? decision,
        string? decisionSource,
        string? reasonCode,
        bool? hasJudgmentSignal,
        string? message)
    {
        return execution.HasValue && decision.HasValue
            ? new InspectionOutcome(
                execution.Value,
                decision.Value,
                decisionSource,
                reasonCode,
                message,
                hasJudgmentSignal ??
                (execution.Value == ExecutionOutcome.Succeeded && decision.Value is DecisionOutcome.Ok or DecisionOutcome.Ng))
            : LegacyInspectionStatusProjection.FromLegacy(status) with { Message = message };
    }

    internal static object ToInspectionEvidenceManifestResponse(InspectionEvidenceManifestReadResult result)
    {
        return new
        {
            found = result.Found,
            status = result.Status,
            errorCode = result.ErrorCode,
            message = result.Message,
            summary = result.Summary,
            manifest = result.Manifest,
            warnings = result.Warnings
        };
    }

    internal static object ToInspectionHistoryComparisonResponse(InspectionHistoryComparison comparison)
    {
        return new
        {
            leftSummary = ToInspectionHistoryComparisonSummary(comparison.LeftSummary),
            rightSummary = ToInspectionHistoryComparisonSummary(comparison.RightSummary),
            compatibility = new
            {
                flowVersionCompatible = comparison.Compatibility.FlowVersionCompatible,
                calibrationBundleCompatible = comparison.Compatibility.CalibrationBundleCompatible,
                onlySafePreviewComparison = comparison.Compatibility.OnlySafePreviewComparison,
                hasUnknownFields = comparison.Compatibility.HasUnknownFields
            },
            warnings = comparison.Warnings,
            fieldDiffs = comparison.FieldDiffs.Select(ToInspectionHistoryFieldDiff).ToList(),
            traceabilityDiff = comparison.TraceabilityDiff.Select(ToInspectionHistoryFieldDiff).ToList(),
            sceneReplayAvailability = ToInspectionHistoryReplayAvailability(comparison.SceneReplayAvailability),
            imageReplayAvailability = ToInspectionHistoryReplayAvailability(comparison.ImageReplayAvailability)
        };
    }

    internal static object ToInspectionPreviousSuccessResponse(InspectionPreviousSuccessReference reference)
    {
        return new
        {
            currentSummary = ToInspectionHistoryComparisonSummary(reference.CurrentSummary),
            referenceSummary = reference.ReferenceSummary == null
                ? null
                : ToInspectionHistoryComparisonSummary(reference.ReferenceSummary),
            found = reference.Found,
            isFlowVersionFallback = reference.IsFlowVersionFallback,
            queryLimit = reference.QueryLimit,
            warnings = reference.Warnings,
            message = reference.Message
        };
    }

    private static object ToInspectionHistoryComparisonSummary(InspectionHistoryComparisonSummary summary)
    {
        return new
        {
            resultId = summary.ResultId,
            id = summary.ResultId,
            projectId = summary.ProjectId,
            status = summary.Status.ToString(),
            executionOutcome = summary.ExecutionOutcome.ToString(),
            decisionOutcome = summary.DecisionOutcome.ToString(),
            timestamp = summary.InspectionTime,
            inspectionTime = summary.InspectionTime,
            defectCount = summary.DefectCount,
            processingTimeMs = summary.ProcessingTimeMs,
            processingTime = summary.ProcessingTimeMs,
            executionTimeMs = summary.ProcessingTimeMs,
            confidenceScore = summary.ConfidenceScore,
            flowVersionHash = summary.FlowVersionHash,
            calibrationBundleId = summary.CalibrationBundleId,
            sessionId = summary.SessionId,
            runId = summary.RunId,
            imageId = summary.ImageId,
            imageReference = summary.ImageReference,
            hasImage = summary.HasImage,
            hasOutputData = summary.HasOutputData,
            hasAnalysisData = summary.HasAnalysisData
        };
    }

    private static object ToInspectionHistoryFieldDiff(InspectionHistoryFieldDiff diff)
    {
        return new
        {
            path = diff.Path,
            label = diff.Label,
            leftValuePreview = diff.LeftValuePreview,
            rightValuePreview = diff.RightValuePreview,
            diffType = diff.DiffType,
            severity = diff.Severity,
            message = diff.Message
        };
    }

    private static object ToInspectionHistoryReplayAvailability(InspectionHistoryReplayAvailability availability)
    {
        return new
        {
            kind = availability.Kind,
            mode = availability.Mode,
            isAvailable = availability.IsAvailable,
            leftAvailable = availability.LeftAvailable,
            rightAvailable = availability.RightAvailable,
            leftReference = availability.LeftReference,
            rightReference = availability.RightReference,
            leftSummary = availability.LeftSummary,
            rightSummary = availability.RightSummary,
            message = availability.Message
        };
    }

    private static string? BuildImageReference(Guid? imageId)
    {
        return imageId.HasValue
            ? $"/api/images/{imageId.Value:D}"
            : null;
    }

    private static object ToInspectionDefectListItem(InspectionHistoryDefectItem defect)
    {
        return new
        {
            id = defect.Id,
            type = defect.Type.ToString(),
            x = defect.X,
            y = defect.Y,
            width = defect.Width,
            height = defect.Height,
            confidenceScore = defect.ConfidenceScore,
            description = defect.Description,
            annotationData = defect.AnnotationData
        };
    }

    internal static object ToInspectionExecutionResponse(InspectionResult result)
    {
        var outcome = result.GetOutcome();
        return new
        {
            id = result.Id,
            projectId = result.ProjectId,
            status = result.Status.ToString(),
            executionOutcome = outcome.Execution.ToString(),
            decisionOutcome = outcome.Decision.ToString(),
            decisionSource = outcome.DecisionSource,
            reasonCode = outcome.ReasonCode,
            hasJudgmentSignal = outcome.HasJudgmentSignal,
            defects = result.Defects.Select(ToInspectionDefectListItem).ToList(),
            defectCount = result.Defects.Count,
            processingTime = result.ProcessingTimeMs,
            processingTimeMs = result.ProcessingTimeMs,
            timestamp = result.InspectionTime,
            inspectionTime = result.InspectionTime,
            confidenceScore = result.ConfidenceScore,
            flowVersionHash = result.FlowVersionHash,
            executionSnapshotId = result.ExecutionSnapshotId,
            projectPersistenceRevision = result.ProjectPersistenceRevision,
            decisionConfigurationHash = result.DecisionConfigurationHash,
            imageId = result.ImageId,
            outputImage = result.ImageId.HasValue
                ? null
                : (result.OutputImage != null ? Convert.ToBase64String(result.OutputImage) : null),
            outputData = TryDeserializeOutputData(result.OutputDataJson),
            analysisData = TryDeserializeAnalysisData(result.AnalysisDataJson),
            errorMessage = result.ErrorMessage
        };
    }

    internal static object ToInspectionRunReconciliationResponse(StudioInspectionRunReconciliation reconciliation)
    {
        return new
        {
            status = reconciliation.Status switch
            {
                StudioInspectionRunReconciliationStatus.StillRunning => "still-running",
                StudioInspectionRunReconciliationStatus.CancelRequested => "cancel-requested",
                StudioInspectionRunReconciliationStatus.Cancelled => "cancelled",
                StudioInspectionRunReconciliationStatus.Succeeded => "succeeded",
                StudioInspectionRunReconciliationStatus.Failed => "failed",
                StudioInspectionRunReconciliationStatus.ResultNotFound => "result-not-found",
                StudioInspectionRunReconciliationStatus.IdentityMismatch => "identity-mismatch",
                _ => "identity-mismatch"
            },
            code = reconciliation.Code,
            message = reconciliation.Message,
            projectId = reconciliation.ProjectId,
            clientSnapshotId = reconciliation.ClientSnapshotId,
            projectPersistenceRevision = reconciliation.PersistenceRevision,
            canonicalFlowHash = reconciliation.CanonicalFlowHash,
            decisionConfigurationHash = reconciliation.DecisionConfigurationHash,
            result = reconciliation.Result == null ? null : ToInspectionExecutionResponse(reconciliation.Result)
        };
    }

    private static bool TryCreateStudioRunIdentity(
        StudioInspectionRunIdentityRequest request,
        out StudioInspectionRunIdentity identity,
        out string error)
    {
        var flowHash = request.ExpectedCanonicalFlowHash?.Trim() ?? string.Empty;
        var decisionHash = request.ExpectedDecisionConfigurationHash?.Trim() ?? string.Empty;
        identity = new StudioInspectionRunIdentity(
            request.ProjectId,
            request.ClientSnapshotId,
            request.ExpectedPersistenceRevision,
            flowHash,
            decisionHash);
        if (request.ProjectId == Guid.Empty || request.ClientSnapshotId == Guid.Empty ||
            request.ExpectedPersistenceRevision < 0 || string.IsNullOrWhiteSpace(flowHash) ||
            string.IsNullOrWhiteSpace(decisionHash))
        {
            error = "ProjectId, clientSnapshotId, persistence revision, Flow hash, and decision hash are required.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static IResult ToAdmissionFailure(ExecutionAdmissionResult admission)
    {
        return Results.BadRequest(new
        {
            Code = admission.Code,
            Error = admission.Message,
            Violations = admission.Violations,
            Action = admission.Code.Contains("DECISION", StringComparison.OrdinalIgnoreCase)
                ? "ConfigureFinalDecision"
                : null
        });
    }

    private static object ToInspectionDefectListItem(Defect defect)
    {
        return new
        {
            id = defect.Id,
            type = defect.Type.ToString(),
            x = defect.X,
            y = defect.Y,
            width = defect.Width,
            height = defect.Height,
            confidenceScore = defect.ConfidenceScore,
            description = defect.Description,
            annotationData = defect.AnnotationData
        };
    }

    private static Dictionary<string, object>? TryDeserializeOutputData(string? json)
    {
        try
        {
            return AnalysisPayloadSerialization.DeserializeJsonDictionary(json);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }

    private static AnalysisDataDto? TryDeserializeAnalysisData(string? json)
    {
        try
        {
            return AnalysisPayloadSerialization.DeserializeAnalysisData(json);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }

    private static void MapImageEndpoints(IEndpointRouteBuilder app)
    {
        // 上传图像
        app.MapPost("/api/images/upload", async (UploadImageRequest request, IImageCacheRepository cache) =>
        {
            try
            {
                if (!TryDecodeImageUpload(request.DataBase64, out var imageData, out var decodeError, out var statusCode))
                {
                    return ImagePayloadDecoder.ToErrorResult(decodeError, statusCode);
                }

                var imageId = await cache.AddAsync(imageData, "png");
                return Results.Ok(new { ImageId = imageId });
            }
            catch (ArgumentException ex)
            {
                return Results.Json(new { Error = ex.Message }, statusCode: StatusCodes.Status413PayloadTooLarge);
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { Error = ex.Message });
            }
        });

        // 获取图像
        app.MapGet("/api/images/{id:guid}", async (Guid id, IImageCacheRepository cache) =>
        {
            var imageData = await cache.GetAsync(id);
            if (imageData == null)
            {
                return Results.NotFound();
            }

            return Results.File(imageData, GetImageResponseContentType(imageData));
        });
    }

    private static string GetImageResponseContentType(byte[] imageData) =>
        InspectionImageFormatDetector.GuessContentType(imageData);

    private static bool IsAdmin(HttpContext context)
    {
        if (!context.Items.TryGetValue("CurrentUser", out var userObj))
        {
            return false;
        }

        var role = userObj switch
        {
            ClearVision.Product.Application.Services.UserSession user => user.Role,
            ClearVision.Product.Desktop.Middleware.UserSession user => user.Role,
            _ => null
        };

        return string.Equals(role, UserRole.Admin.ToString(), StringComparison.Ordinal);
    }

    private static bool TryDecodeImageUpload(
        string? dataBase64,
        out byte[] imageData,
        out string errorMessage,
        out int statusCode)
    {
        return ImagePayloadDecoder.TryDecodeBytes(dataBase64, "DataBase64", out imageData, out errorMessage, out statusCode);
    }
}
