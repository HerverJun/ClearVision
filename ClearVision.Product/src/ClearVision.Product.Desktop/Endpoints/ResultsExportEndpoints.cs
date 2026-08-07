using ClearVision.Product.Application.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace ClearVision.Product.Desktop.Endpoints;

public static class ResultsExportEndpoints
{
    public sealed class CreateResultsExportRequest
    {
        public Guid ProjectId { get; set; }
        public string? Source { get; set; }
        public string? Format { get; set; }
        public DateTime? StartTime { get; set; }
        public DateTime? EndTime { get; set; }
        public string? Status { get; set; }
        public string? DefectType { get; set; }
        public string? DiagnosticCode { get; set; }
        public Guid ClientOperationId { get; set; }
    }

    public static IEndpointRouteBuilder MapResultsExportEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/results/exports", async (
            CreateResultsExportRequest request,
            IResultsExportJobService exportService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var format = ParseFormat(request.Format);
                var result = await exportService.CreateAsync(
                    new ResultsExportRequest(
                        request.ProjectId,
                        request.Source ?? string.Empty,
                        format,
                        request.StartTime,
                        request.EndTime,
                        request.Status,
                        request.DefectType,
                        request.DiagnosticCode,
                        request.ClientOperationId),
                    cancellationToken);

                return Results.Ok(result);
            }
            catch (ResultsExportValidationException error)
            {
                return Validation(error);
            }
            catch (ResultsExportIdentityConflictException error)
            {
                return Results.Conflict(new
                {
                    errorCode = "RESULTS_EXPORT_OPERATION_ID_CONFLICT",
                    message = error.Message
                });
            }
            catch (ResultsExportProjectNotFoundException error)
            {
                return Results.NotFound(new
                {
                    errorCode = "RESULTS_EXPORT_PROJECT_NOT_FOUND",
                    projectId = error.ProjectId,
                    message = "指定工程不存在或已被删除。"
                });
            }
        })
        .RequireClearVisionPermission(ClearVisionPermissionPolicies.CanOperateHardware);

        app.MapGet("/api/results/exports/{exportId:guid}", (
            Guid exportId,
            IResultsExportJobService exportService) =>
        {
            var snapshot = exportService.Get(exportId);
            return snapshot is null
                ? ExportNotFound(exportId)
                : Results.Ok(snapshot);
        })
        .RequireClearVisionPermission(ClearVisionPermissionPolicies.CanOperateHardware);

        app.MapGet("/api/results/exports/by-operation/{clientOperationId:guid}", (
            Guid clientOperationId,
            IResultsExportJobService exportService) =>
        {
            var snapshot = exportService.FindByClientOperationId(clientOperationId);
            return snapshot is null
                ? ExportNotFound(clientOperationId)
                : Results.Ok(snapshot);
        })
        .RequireClearVisionPermission(ClearVisionPermissionPolicies.CanOperateHardware);

        app.MapPost("/api/results/exports/{exportId:guid}/cancel", (
            Guid exportId,
            IResultsExportJobService exportService) =>
        {
            var snapshot = exportService.Cancel(exportId);
            return snapshot is null
                ? ExportNotFound(exportId)
                : Results.Ok(snapshot);
        })
        .RequireClearVisionPermission(ClearVisionPermissionPolicies.CanOperateHardware);

        app.MapGet("/api/results/exports/{exportId:guid}/download", (
            Guid exportId,
            HttpContext httpContext,
            IResultsExportJobService exportService) =>
        {
            var snapshot = exportService.Get(exportId);
            if (snapshot is null)
            {
                return ExportNotFound(exportId);
            }

            if (snapshot.State != ResultsExportJobState.Completed)
            {
                return Results.Conflict(new
                {
                    errorCode = "RESULTS_EXPORT_NOT_COMPLETED",
                    state = snapshot.State.ToString(),
                    message = "结果导出尚未完成，当前没有可下载文件。"
                });
            }

            if (!exportService.TryReadArtifact(exportId, out var artifact) || artifact is null)
            {
                return Results.Json(
                    new
                    {
                        errorCode = "RESULTS_EXPORT_ARTIFACT_EXPIRED",
                        message = "结果导出文件已过期，请重新发起导出。"
                    },
                    statusCode: StatusCodes.Status410Gone);
            }

            httpContext.Response.Headers["X-Artifact-Sha256"] = artifact.Sha256;
            return Results.File(artifact.Bytes, artifact.ContentType, artifact.FileName);
        })
        .RequireClearVisionPermission(ClearVisionPermissionPolicies.CanOperateHardware);

        return app;
    }

    private static ResultsExportFormat ParseFormat(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "csv" => ResultsExportFormat.Csv,
            "json" => ResultsExportFormat.Json,
            _ => throw new ResultsExportValidationException(
                "RESULTS_EXPORT_FORMAT_UNSUPPORTED",
                "结果导出格式仅支持 CSV 或 JSON。")
        };
    }

    private static IResult Validation(ResultsExportValidationException error) =>
        Results.BadRequest(new
        {
            errorCode = error.Code,
            message = error.Message
        });

    private static IResult ExportNotFound(Guid identity) =>
        Results.NotFound(new
        {
            errorCode = "RESULTS_EXPORT_NOT_FOUND",
            identity,
            message = "结果导出任务不存在或已过期。"
        });
}
