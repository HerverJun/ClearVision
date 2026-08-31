using ClearVision.Product.Application.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace ClearVision.Product.Desktop.Endpoints;

public static class AnalysisEndpoints
{
    public static IEndpointRouteBuilder MapAnalysisEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/analysis/statistics/{projectId}", async (
            Guid projectId,
            DateTime? startTime,
            DateTime? endTime,
            string? status,
            string? defectType,
            IResultAnalysisService analysisService) =>
        {
            try
            {
                return Results.Ok(await analysisService.GetStatisticsAsync(projectId, startTime, endTime, status, defectType));
            }
            catch (ResultAnalysisBudgetException exception)
            {
                return BudgetViolation(exception);
            }
        });

        app.MapGet("/api/analysis/defect-distribution/{projectId}", async (
            Guid projectId,
            DateTime? startTime,
            DateTime? endTime,
            string? status,
            string? defectType,
            IResultAnalysisService analysisService) =>
        {
            try
            {
                return Results.Ok(await analysisService.GetDefectDistributionAsync(projectId, startTime, endTime, status, defectType));
            }
            catch (ResultAnalysisBudgetException exception)
            {
                return BudgetViolation(exception);
            }
        });

        app.MapGet("/api/analysis/trend/{projectId}", async (
            Guid projectId,
            string interval,
            DateTime startTime,
            DateTime endTime,
            string? status,
            string? defectType,
            IResultAnalysisService analysisService) =>
        {
            if (!Enum.TryParse<TrendInterval>(interval, true, out var trendInterval))
            {
                return Results.BadRequest($"无效间隔: {interval}");
            }

            try
            {
                return Results.Ok(await analysisService.GetTrendAnalysisAsync(projectId, trendInterval, startTime, endTime, status, defectType));
            }
            catch (ResultAnalysisBudgetException exception)
            {
                return BudgetViolation(exception);
            }
        });

        app.MapGet("/api/analysis/report/{projectId}", async (
            Guid projectId,
            DateTime? startTime,
            DateTime? endTime,
            string? status,
            string? defectType,
            IResultAnalysisService analysisService) =>
        {
            try
            {
                return Results.Ok(await analysisService.GenerateReportAsync(projectId, startTime, endTime, status, defectType));
            }
            catch (ResultAnalysisBudgetException exception)
            {
                return BudgetViolation(exception);
            }
        });

        return app;
    }

    private static IResult BudgetViolation(ResultAnalysisBudgetException exception) =>
        Results.BadRequest(new
        {
            error = exception.ErrorCode,
            message = exception.Message,
            maximumWindowDays = ResultAnalysisQueryBudget.MaximumWindowDays,
            maximumTrendPoints = ResultAnalysisQueryBudget.MaximumTrendPoints,
            maximumTrendRows = ResultAnalysisQueryBudget.MaximumTrendRows
        });
}
