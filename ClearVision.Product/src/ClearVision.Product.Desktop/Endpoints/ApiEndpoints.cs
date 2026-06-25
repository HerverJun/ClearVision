// ApiEndpoints.cs
// API 绔偣閰嶇疆
// 浣滆€咃細铇呰姕鍚?

using System.Linq;
using System.Text.Json;
using ClearVision.Product.Application.Analysis;
using ClearVision.Product.Application.DTOs;
using ClearVision.Product.Application.Services;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Interfaces;
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
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using OpenCvSharp;

namespace ClearVision.Product.Desktop.Endpoints;

/// <summary>
/// API 绔偣閰嶇疆
/// </summary>
public static class ApiEndpoints
{
    public static IEndpointRouteBuilder MapVisionApiEndpoints(this IEndpointRouteBuilder app)
    {
        // 鍋ュ悍妫€鏌?
        app.MapGet("/api/health", () => Results.Ok(new { Status = "Healthy", Timestamp = DateTime.UtcNow }));

        // 宸ョ▼鐩稿叧绔偣
        MapProjectEndpoints(app);

        // 妫€娴嬬浉鍏崇鐐?
        MapInspectionEndpoints(app);

        // 绠楀瓙搴撶鐐?
        MapOperatorEndpoints(app);

        // 銆怭hase 3銆戣妭鐐归瑙堢鐐癸紙澶嶇敤璋冭瘯缂撳瓨鏈哄埗锛?
        app.MapPreviewNodeEndpoints();

        // 鍥惧儚鐩稿叧绔偣
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
    }

    private static void MapProjectEndpoints(IEndpointRouteBuilder app)
    {
        // 鑾峰彇宸ョ▼鍒楄〃
        app.MapGet("/api/projects", async (ProjectService service) =>
        {
            var projects = await service.GetAllAsync();
            return Results.Ok(projects);
        });

        // 鑾峰彇鏈€杩戞墦寮€鐨勫伐绋?
        app.MapGet("/api/projects/recent", async (ProjectService service, int count = 10) =>
        {
            var projects = await service.GetRecentlyOpenedAsync(count);
            return Results.Ok(projects);
        });

        // 鎼滅储宸ョ▼
        app.MapGet("/api/projects/search", async (ProjectService service, string keyword) =>
        {
            var projects = await service.SearchAsync(keyword);
            return Results.Ok(projects);
        });

        // 鑾峰彇宸ョ▼璇︽儏
        app.MapGet("/api/projects/{id:guid}", async (Guid id, ProjectService service) =>
        {
            var project = await service.GetByIdAsync(id);
            return project != null ? Results.Ok(project) : Results.NotFound();
        });

        // 鍒涘缓宸ョ▼
        app.MapPost("/api/projects", async (CreateProjectRequest request, ProjectService service) =>
        {
            try
            {
                var project = await service.CreateAsync(request);
                return Results.Created($"/api/projects/{project.Id}", project);
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { Error = ex.Message });
            }
        });

        // 鏇存柊宸ョ▼
        app.MapPut("/api/projects/{id:guid}", async (
            Guid id,
            UpdateProjectRequest request,
            ProjectService service,
            IInspectionRuntimeCoordinator runtimeCoordinator) =>
        {
            if (request.GlobalVariables != null && IsProjectRuntimeBusy(id, runtimeCoordinator))
            {
                return Results.Conflict(new { Code = "GV031", Error = "Project is currently running." });
            }

            try
            {
                var project = await service.UpdateAsync(id, request);
                return Results.Ok(project);
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { Error = ex.Message });
            }
        });

        // 鍒犻櫎宸ョ▼
        app.MapDelete("/api/projects/{id:guid}", async (Guid id, ProjectService service) =>
        {
            try
            {
                await service.DeleteAsync(id);
                return Results.NoContent();
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { Error = ex.Message });
            }
        });

        // 鏇存柊娴佺▼
        app.MapPut("/api/projects/{id:guid}/flow", async (Guid id, UpdateFlowRequest request, ProjectService service) =>
        {
            try
            {
                // 浣跨敤 ProjectService 澶勭悊鏇存柊锛屽畠鐜板湪浣跨敤鏂囦欢瀛樺偍
                // 杩欑鏂瑰紡瀹屽叏缁曡繃浜?EF Core 鐨勫鏉傜姸鎬佺鐞嗗拰 Table Splitting 闂
                await service.UpdateFlowAsync(id, request);

                return Results.Ok(new { Message = "娴佺▼宸叉洿鏂?(File Based)", OperatorCount = request.Operators.Count, ConnectionCount = request.Connections.Count });
            }
            catch (Exception ex)
            {
                // 鏃ュ織宸茬敱鍏ㄥ眬寮傚父涓棿浠惰褰?
                return Results.BadRequest(new { Error = ex.Message });
            }
        });

        app.MapGet("/api/projects/{id:guid}/global-variables", async (Guid id, ProjectService service) =>
        {
            var project = await service.GetByIdAsync(id);
            return project != null ? Results.Ok(project.GlobalVariables) : Results.NotFound();
        });

        app.MapPut("/api/projects/{id:guid}/global-variables", async (
            Guid id,
            ProjectGlobalVariableSchema schema,
            ProjectService service,
            IInspectionRuntimeCoordinator runtimeCoordinator) =>
        {
            if (IsProjectRuntimeBusy(id, runtimeCoordinator))
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
                return Results.BadRequest(new { Error = ex.Message });
            }
        });

        app.MapGet("/api/projects/{id:guid}/global-variable-values", async (
            Guid id,
            ProjectService service,
            ProjectVariableSessionRegistry sessions) =>
        {
            var project = await service.GetByIdAsync(id);
            if (project == null)
            {
                return Results.NotFound();
            }

            var session = sessions.GetOrCreate(project);
            return Results.Ok(ToProjectVariableValueDtos(project.GlobalVariables, session));
        });

        app.MapPut("/api/projects/{id:guid}/global-variable-values/{variableId:guid}", async (
            Guid id,
            Guid variableId,
            ProjectVariableValueWriteRequest request,
            ProjectService service,
            ProjectVariableSessionRegistry sessions,
            IInspectionRuntimeCoordinator runtimeCoordinator) =>
        {
            if (IsProjectRuntimeBusy(id, runtimeCoordinator))
            {
                return Results.Conflict(new { Code = "GV031", Error = "Project is currently running." });
            }

            var project = await service.GetByIdAsync(id);
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

            try
            {
                session.SetValue(variableId, request.Value, ProjectVariableUpdatedBy.StudioManual);
                sessions.Save(id, session);
                return Results.Ok(ToProjectVariableValueDtos(project.GlobalVariables, session));
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { Error = ex.Message });
            }
        });

        app.MapPost("/api/projects/{id:guid}/global-variable-values/reset", async (
            Guid id,
            ProjectService service,
            ProjectVariableSessionRegistry sessions,
            IInspectionRuntimeCoordinator runtimeCoordinator) =>
        {
            if (IsProjectRuntimeBusy(id, runtimeCoordinator))
            {
                return Results.Conflict(new { Code = "GV031", Error = "Project is currently running." });
            }

            var project = await service.GetByIdAsync(id);
            if (project == null)
            {
                return Results.NotFound();
            }

            var session = sessions.GetOrCreate(project);
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

            session.ResetAll(ProjectVariableUpdatedBy.Reset);
            sessions.Save(id, session);
            return Results.Ok(ToProjectVariableValueDtos(project.GlobalVariables, session));
        });

        app.MapPost("/api/projects/{id:guid}/global-variable-values/{variableId:guid}/reset", async (
            Guid id,
            Guid variableId,
            ProjectService service,
            ProjectVariableSessionRegistry sessions,
            IInspectionRuntimeCoordinator runtimeCoordinator) =>
        {
            if (IsProjectRuntimeBusy(id, runtimeCoordinator))
            {
                return Results.Conflict(new { Code = "GV031", Error = "Project is currently running." });
            }

            var project = await service.GetByIdAsync(id);
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

            session.Reset(variableId, ProjectVariableUpdatedBy.Reset);
            sessions.Save(id, session);
            return Results.Ok(ToProjectVariableValueDtos(project.GlobalVariables, session));
        });

        app.MapPost("/api/projects/{id:guid}/runtime-package/export", async (
            Guid id,
            ExportRuntimePackageRequest? request,
            ProjectService service,
            RuntimePackageExporter exporter,
            StationPackageStore packageStore,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            try
            {
                if (!IsAdmin(context))
                {
                    return Results.Json(new { Error = "AdminRequired" }, statusCode: StatusCodes.Status403Forbidden);
                }

                var project = await service.GetByIdAsync(id);
                if (project == null)
                {
                    return Results.NotFound(new { Error = "Project not found." });
                }

                if (request?.Flow != null)
                {
                    project.Flow = request.Flow;
                }

                var exportResult = await exporter.ExportAsync(
                    new RuntimePackageExportRequest
                    {
                        Project = project,
                        TargetRootDirectory = request?.TargetRootDirectory
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
                return Results.BadRequest(new { Error = ex.Message });
            }
        });
    }

    private static void MapInspectionEndpoints(IEndpointRouteBuilder app)
    {
        // 鎵ц妫€娴?
        app.MapPost("/api/inspection/execute", async (ExecuteInspectionRequest request, Core.Services.IInspectionService service) =>
        {
            try
            {
                if (!string.IsNullOrEmpty(request.ImageBase64))
                {
                    if (!ImagePayloadDecoder.TryDecodeBytes(request.ImageBase64, "ImageBase64", out var imageData, out var decodeError, out var statusCode))
                    {
                        return ImagePayloadDecoder.ToErrorResult(decodeError, statusCode);
                    }

                    var result = await service.ExecuteSingleAsync(request.ProjectId, imageData, request.FlowData?.ToEntity());
                    return Results.Ok(ToInspectionExecutionResponse(result));
                }
                else if (!string.IsNullOrEmpty(request.CameraId))
                {
                    var result = await service.ExecuteSingleAsync(request.ProjectId, request.CameraId, request.FlowData?.ToEntity());
                    return Results.Ok(ToInspectionExecutionResponse(result));
                }
                else
                {
                    // 銆愬叧閿慨澶嶃€戝鏋滃墠绔彁渚涗簡娴佺▼鏁版嵁锛屽垯杞崲骞朵娇鐢?
                    // 杩欑‘淇濆墠绔紪杈戠殑鍙傛暟鍊艰兘姝ｇ‘浼犻€掑埌鍚庣鎵ц
                    OperatorFlow? flow = request.FlowData?.ToEntity();
                    // 鍓嶇娴佺▼鏁版嵁宸查€氳繃鏃ュ織涓棿浠惰褰?

                    var result = await service.ExecuteSingleAsync(request.ProjectId, (byte[])null!, flow);
                    return Results.Ok(ToInspectionExecutionResponse(result));
                }
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(new { Code = "GV031", Error = ex.Message });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { Error = ex.Message });
            }
        });

        // 鑾峰彇妫€娴嬪巻鍙?
        app.MapGet("/api/inspection/history/{projectId:guid}", async (
        Guid projectId,
        Core.Services.IInspectionService service,
        DateTime? startTime,
        DateTime? endTime,
        string? status,
        string? defectType,
        int pageIndex = 0,
        int pageSize = 20) =>
        {
            var results = await service.GetInspectionHistoryAsync(projectId, startTime, endTime, status, defectType, pageIndex, pageSize);
            return Results.Ok(ToInspectionHistoryListResponse(results));
        });

        // 鑾峰彇缁熻淇℃伅
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

        // 銆愮浜屼紭鍏堢骇銆戝惎鍔ㄥ疄鏃舵娴?
        app.MapPost("/api/inspection/realtime/start", async (
            StartRealtimeInspectionRequest request,
            Core.Services.IInspectionService service,
            WebMessageHandler webMessageHandler,
            CancellationToken cancellationToken) =>
        {
            try
            {
                // 鏍规嵁杩愯妯″紡閫夋嫨鍚姩鏂瑰紡
                var runMode = request.RunMode?.ToLower() ?? "camera";
                var requestFlow = request.FlowData?.ToEntity();

                if (runMode == "flow" && requestFlow != null)
                {
                    // 娴佺▼椹卞姩妯″紡
                    await service.StartRealtimeInspectionFlowAsync(
                        request.ProjectId,
                        requestFlow,
                        request.CameraId,
                        cancellationToken,
                        result => webMessageHandler.NotifyInspectionResult(result, request.ProjectId));

                    return Results.Ok(new
                    {
                        Message = "瀹炴椂妫€娴嬪凡鍚姩 (娴佺▼椹卞姩妯″紡)",
                        ProjectId = request.ProjectId,
                        RunMode = "flow",
                        CameraId = request.CameraId
                    });
                }
                else if (requestFlow?.Operators?.Count > 0)
                {
                    // 鐩告満椹卞姩妯″紡涔熷繀椤讳娇鐢ㄥ墠绔綋鍓嶆祦绋嬶紝閬垮厤鐢诲竷鍙傛暟鏇存柊鍚庝粛鎵ц鎸佷箙鍖栨棫娴佺▼銆?
                    await service.StartRealtimeInspectionFlowAsync(
                        request.ProjectId,
                        requestFlow,
                        request.CameraId,
                        cancellationToken,
                        result => webMessageHandler.NotifyInspectionResult(result, request.ProjectId));

                    return Results.Ok(new
                    {
                        Message = "瀹炴椂妫€娴嬪凡鍚姩 (鐩告満椹卞姩妯″紡)",
                        ProjectId = request.ProjectId,
                        RunMode = "camera",
                        CameraId = request.CameraId
                    });
                }
                else
                {
                    // 鐩告満椹卞姩妯″紡
                    await service.StartRealtimeInspectionAsync(
                        request.ProjectId,
                        request.CameraId,
                        cancellationToken,
                        result => webMessageHandler.NotifyInspectionResult(result, request.ProjectId));

                    return Results.Ok(new
                    {
                        Message = "瀹炴椂妫€娴嬪凡鍚姩 (鐩告満椹卞姩妯″紡)",
                        ProjectId = request.ProjectId,
                        RunMode = "camera",
                        CameraId = request.CameraId
                    });
                }
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(new { Error = ex.Message });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { Error = ex.Message });
            }
        });

        // 銆愮浜屼紭鍏堢骇銆戝仠姝㈠疄鏃舵娴?
        app.MapPost("/api/inspection/realtime/stop", async (
            StopRealtimeInspectionRequest request,
            Core.Services.IInspectionService service) =>
        {
            try
            {
                await service.StopRealtimeInspectionAsync(request.ProjectId);
                return Results.Ok(new
                {
                    Message = "瀹炴椂妫€娴嬪凡鍋滄",
                    ProjectId = request.ProjectId
                });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { Error = ex.Message });
            }
        });
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

    private static bool IsProjectRuntimeBusy(Guid projectId, IInspectionRuntimeCoordinator runtimeCoordinator)
    {
        var state = runtimeCoordinator.GetState(projectId);
        return state?.Status is RuntimeStatus.Starting or RuntimeStatus.Running or RuntimeStatus.Stopping;
    }

    private static void MapOperatorEndpoints(IEndpointRouteBuilder app)
    {
        // 鑾峰彇绠楀瓙搴?
        app.MapGet("/api/operators/library", (IOperatorFactory factory) =>
        {
            var metadata = factory
                .GetAllMetadata()
                .Where(m => m.Type != OperatorType.Morphology);
            return Results.Ok(metadata);
        });

        // 鑾峰彇鏀寔鐨勭畻瀛愮被鍨?
        app.MapGet("/api/operators/types", (IOperatorFactory factory) =>
        {
            var types = factory.GetSupportedOperatorTypes();
            return Results.Ok(types);
        });

        // 鑾峰彇绠楀瓙鍏冩暟鎹?
        app.MapGet("/api/operators/{type}/metadata", (Core.Enums.OperatorType type, IOperatorFactory factory) =>
        {
            var metadata = factory.GetMetadata(type);
            return metadata != null ? Results.Ok(metadata) : Results.NotFound();
        });

        // 鑾峰彇娴佺▼妯℃澘鍒楄〃
        app.MapGet("/api/templates", async (
            IFlowTemplateService templateService,
            string? industry,
            CancellationToken cancellationToken) =>
        {
            var templates = await templateService.GetTemplatesAsync(industry, cancellationToken);
            return Results.Ok(templates);
        });

        // 鑾峰彇鍗曚釜娴佺▼妯℃澘璇︽儏
        app.MapGet("/api/templates/{id:guid}", async (
            Guid id,
            IFlowTemplateService templateService,
            CancellationToken cancellationToken) =>
        {
            var template = await templateService.GetTemplateAsync(id, cancellationToken);
            return template != null ? Results.Ok(template) : Results.NotFound();
        });

        // 鍒涘缓娴佺▼妯℃澘
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
        });

        // 鏇存柊娴佺▼妯℃澘
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
        });

        // 鎺ㄨ崘绠楀瓙鍙傛暟
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

        // 鍗曠畻瀛愯皟鍙傞瑙?
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
        return new
        {
            id = result.Id,
            projectId = result.ProjectId,
            status = result.Status.ToString(),
            defects = result.Defects.Select(ToInspectionDefectListItem).ToList(),
            defectCount = result.Defects.Count,
            processingTime = result.ProcessingTimeMs,
            processingTimeMs = result.ProcessingTimeMs,
            timestamp = result.InspectionTime,
            inspectionTime = result.InspectionTime,
            confidenceScore = result.ConfidenceScore,
            imageId = result.ImageId,
            outputData = TryDeserializeOutputData(result.OutputDataJson),
            analysisData = TryDeserializeAnalysisData(result.AnalysisDataJson),
            errorMessage = result.ErrorMessage
        };
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
        return new
        {
            id = result.Id,
            projectId = result.ProjectId,
            status = result.Status.ToString(),
            defects = result.Defects.Select(ToInspectionDefectListItem).ToList(),
            defectCount = result.Defects.Count,
            processingTime = result.ProcessingTimeMs,
            processingTimeMs = result.ProcessingTimeMs,
            timestamp = result.InspectionTime,
            inspectionTime = result.InspectionTime,
            confidenceScore = result.ConfidenceScore,
            imageId = result.ImageId,
            outputImage = result.ImageId.HasValue
                ? null
                : (result.OutputImage != null ? Convert.ToBase64String(result.OutputImage) : null),
            outputData = TryDeserializeOutputData(result.OutputDataJson),
            analysisData = TryDeserializeAnalysisData(result.AnalysisDataJson),
            errorMessage = result.ErrorMessage
        };
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
        // 涓婁紶鍥惧儚
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

        // 鑾峰彇鍥惧儚
        app.MapGet("/api/images/{id:guid}", async (Guid id, IImageCacheRepository cache) =>
        {
            var imageData = await cache.GetAsync(id);
            if (imageData == null)
            {
                return Results.NotFound();
            }

            return Results.File(imageData, "image/png");
        });
    }

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
