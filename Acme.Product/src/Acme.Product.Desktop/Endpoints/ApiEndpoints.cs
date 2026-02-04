using Acme.Product.Application.DTOs;
using Acme.Product.Application.Services;
using Acme.Product.Core.Entities;
using Acme.Product.Core.Enums;
using Acme.Product.Core.Interfaces;
using Acme.Product.Core.Services;
using Acme.Product.Core.ValueObjects;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Acme.Product.Desktop.Endpoints;

/// <summary>
/// API 端点配置
/// </summary>
public static class ApiEndpoints
{
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

        // 图像相关端点
        MapImageEndpoints(app);

        return app;
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
            var project = await service.GetByIdAsync(id);
            return project != null ? Results.Ok(project) : Results.NotFound();
        });

        // 创建工程
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

        // 更新工程
        app.MapPut("/api/projects/{id:guid}", async (Guid id, UpdateProjectRequest request, ProjectService service) =>
        {
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

        // 删除工程
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

        // 更新流程
        app.MapPut("/api/projects/{id:guid}/flow", async (Guid id, UpdateFlowRequest request, IProjectRepository repository) =>
        {
            try
            {
                var project = await repository.GetWithFlowAsync(id);
                if (project == null)
                {
                    return Results.NotFound(new { Error = $"工程 {id} 不存在" });
                }

                // 获取现有流程或创建新流程
                var flow = project.Flow;
                
                // 清除现有算子和连接
                var existingOperators = flow.Operators.ToList();
                foreach (var op in existingOperators)
                {
                    flow.RemoveOperator(op.Id);
                }
                
                // 添加算子
                foreach (var opDto in request.Operators)
                {
                    var op = new Operator(
                        opDto.Name,
                        opDto.Type,
                        opDto.X,
                        opDto.Y
                    );
                    
                    // 添加参数
                    if (opDto.Parameters != null)
                    {
                        foreach (var param in opDto.Parameters)
                        {
                            var parameter = new Parameter(
                                Guid.NewGuid(),
                                param.Name,
                                param.DisplayName,
                                param.Description ?? string.Empty,
                                param.DataType,
                                param.DefaultValue,
                                param.MinValue,
                                param.MaxValue,
                                param.IsRequired
                            );
                            if (param.Value != null)
                            {
                                parameter.SetValue(param.Value);
                            }
                            op.AddParameter(parameter);
                        }
                    }
                    
                    flow.AddOperator(op);
                }
                
                // 添加连接
                foreach (var connDto in request.Connections)
                {
                    var connection = new OperatorConnection(
                        connDto.SourceOperatorId,
                        connDto.TargetOperatorId,
                        connDto.SourcePortId,
                        connDto.TargetPortId
                    );
                    flow.AddConnection(connection);
                }
                
                await repository.UpdateAsync(project);
                
                return Results.Ok(new { Message = "流程已更新", OperatorCount = request.Operators.Count, ConnectionCount = request.Connections.Count });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { Error = ex.Message });
            }
        });
    }

    private static void MapInspectionEndpoints(IEndpointRouteBuilder app)
    {
        // 执行检测
        app.MapPost("/api/inspection/execute", async (ExecuteInspectionRequest request, Core.Services.IInspectionService service) =>
        {
            try
            {
                if (!string.IsNullOrEmpty(request.ImageBase64))
                {
                    var imageData = Convert.FromBase64String(request.ImageBase64);
                    var result = await service.ExecuteSingleAsync(request.ProjectId, imageData);
                    return Results.Ok(result);
                }
                else if (!string.IsNullOrEmpty(request.CameraId))
                {
                    var result = await service.ExecuteSingleAsync(request.ProjectId, request.CameraId);
                    return Results.Ok(result);
                }
                else
                {
                    return Results.BadRequest(new { Error = "必须提供图像数据或相机ID" });
                }
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { Error = ex.Message });
            }
        });

        // 获取检测历史
        app.MapGet("/api/inspection/history/{projectId:guid}", async (
            Guid projectId,
            Core.Services.IInspectionService service,
            DateTime? startTime,
            DateTime? endTime,
            int pageIndex = 0,
            int pageSize = 20) =>
        {
            var results = await service.GetInspectionHistoryAsync(projectId, startTime, endTime, pageIndex, pageSize);
            return Results.Ok(results);
        });

        // 获取统计信息
        app.MapGet("/api/inspection/statistics/{projectId:guid}", async (
            Guid projectId,
            Core.Services.IInspectionService service,
            DateTime? startTime,
            DateTime? endTime) =>
        {
            var statistics = await service.GetStatisticsAsync(projectId, startTime, endTime);
            return Results.Ok(statistics);
        });
    }

    private static void MapOperatorEndpoints(IEndpointRouteBuilder app)
    {
        // 获取算子库
        app.MapGet("/api/operators/library", (IOperatorFactory factory) =>
        {
            var metadata = factory.GetAllMetadata();
            return Results.Ok(metadata);
        });

        // 获取支持的算子类型
        app.MapGet("/api/operators/types", (IOperatorFactory factory) =>
        {
            var types = factory.GetSupportedOperatorTypes();
            return Results.Ok(types);
        });

        // 获取算子元数据
        app.MapGet("/api/operators/{type}/metadata", (Core.Enums.OperatorType type, IOperatorFactory factory) =>
        {
            var metadata = factory.GetMetadata(type);
            return metadata != null ? Results.Ok(metadata) : Results.NotFound();
        });
    }

    private static void MapImageEndpoints(IEndpointRouteBuilder app)
    {
        // 上传图像
        app.MapPost("/api/images/upload", async (UploadImageRequest request, IImageCacheRepository cache) =>
        {
            try
            {
                var imageData = Convert.FromBase64String(request.DataBase64);
                var imageId = await cache.AddAsync(imageData, "png");
                return Results.Ok(new { ImageId = imageId });
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

            return Results.File(imageData, "image/png");
        });
    }
}
