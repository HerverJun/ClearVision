// InspectionResultRepository.cs
// 检测结果仓储实现
// 作者：蘅芜君

using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace ClearVision.Product.Infrastructure.Repositories;

/// <summary>
/// 检测结果仓储实现
/// </summary>
public class InspectionResultRepository : RepositoryBase<InspectionResult>, IInspectionResultRepository
{
    public InspectionResultRepository(Data.VisionDbContext context) : base(context)
    {
    }

    public async Task AddRangeAsync(IEnumerable<InspectionResult> results)
    {
        ArgumentNullException.ThrowIfNull(results);

        var batch = results as IReadOnlyCollection<InspectionResult> ?? results.ToList();
        if (batch.Count == 0)
        {
            return;
        }

        await _dbSet.AddRangeAsync(batch);
        await _context.SaveChangesAsync();
    }

    public async Task<IEnumerable<InspectionResult>> GetByProjectIdAsync(Guid projectId, int pageIndex = 0, int pageSize = 20)
    {
        var items = await SelectHistoryItemsWithPayload(_dbSet
                .Where(r => r.ProjectId == projectId && !r.IsDeleted))
            .OrderByDescending(r => r.InspectionTime)
            .ThenByDescending(r => r.Id)
            .Skip(pageIndex * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return items.Select(ToInspectionResultWithoutOutputImage).ToList();
    }

    public async Task<InspectionHistoryPage> GetHistoryPageAsync(
        Guid projectId,
        DateTime? startTime = null,
        DateTime? endTime = null,
        string? status = null,
        string? defectType = null,
        int pageIndex = 0,
        int pageSize = 20,
        string? flowVersionHash = null)
    {
        pageIndex = Math.Max(0, pageIndex);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var query = BuildFilteredQuery(projectId, startTime, endTime, status, defectType, flowVersionHash);

        var totalCount = await query.CountAsync();
        var items = await SelectHistoryListItems(query)
            .OrderByDescending(r => r.InspectionTime)
            .ThenByDescending(r => r.Id)
            .Skip(pageIndex * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return new InspectionHistoryPage
        {
            Items = items,
            TotalCount = totalCount,
            PageIndex = pageIndex,
            PageSize = pageSize
        };
    }

    public async Task<InspectionHistoryDetail?> GetHistoryDetailAsync(Guid projectId, Guid resultId)
    {
        return await SelectHistoryDetails(_dbSet
                .Where(r => r.ProjectId == projectId && r.Id == resultId && !r.IsDeleted))
            .SingleOrDefaultAsync();
    }

    public async Task<InspectionHistoryDetail?> FindPreviousSuccessfulInspectionAsync(
        Guid projectId,
        DateTime beforeTime,
        string? flowVersionHash = null,
        int limit = 50)
    {
        limit = Math.Clamp(limit, 1, 200);

        var query = _dbSet
            .Where(r =>
                r.ProjectId == projectId &&
                !r.IsDeleted &&
                r.Status == InspectionStatus.OK &&
                r.InspectionTime < beforeTime);

        if (!string.IsNullOrWhiteSpace(flowVersionHash))
        {
            var normalizedFlowHash = flowVersionHash.Trim();
            query = query.Where(r => r.FlowVersionHash == normalizedFlowHash);
        }

        return await SelectHistoryDetails(query)
            .OrderByDescending(r => r.InspectionTime)
            .ThenByDescending(r => r.Id)
            .Take(limit)
            .FirstOrDefaultAsync();
    }

    public async Task<IEnumerable<InspectionResult>> GetByTimeRangeAsync(
        Guid projectId,
        DateTime startTime,
        DateTime endTime,
        string? status = null,
        string? defectType = null)
    {
        var items = await SelectHistoryItemsWithPayload(BuildFilteredQuery(projectId, startTime, endTime, status, defectType))
            .OrderByDescending(r => r.InspectionTime)
            .ThenByDescending(r => r.Id)
            .ToListAsync();

        return items.Select(ToInspectionResultWithoutOutputImage).ToList();
    }

    public async Task<InspectionStatistics> GetStatisticsAsync(
        Guid projectId,
        DateTime? startTime = null,
        DateTime? endTime = null,
        string? status = null,
        string? defectType = null)
    {
        var query = BuildFilteredQuery(projectId, startTime, endTime, status, defectType);

        var totalCount = await query.CountAsync();
        var okCount = await query.CountAsync(r => r.Status == InspectionStatus.OK);
        var ngCount = await query.CountAsync(r => r.Status == InspectionStatus.NG);
        var errorCount = await query.CountAsync(r => r.Status == InspectionStatus.Error);
        var avgTime = await query
            .Select(r => (double?)r.ProcessingTimeMs)
            .AverageAsync() ?? 0;

        return new InspectionStatistics
        {
            TotalCount = totalCount,
            OKCount = okCount,
            NGCount = ngCount,
            ErrorCount = errorCount,
            OKRate = totalCount > 0 ? (double)okCount / totalCount : 0,
            AverageProcessingTimeMs = avgTime
        };
    }

    public async Task<Dictionary<DefectType, int>> GetDefectDistributionAsync(
        Guid projectId,
        DateTime? startTime = null,
        DateTime? endTime = null,
        string? status = null,
        string? defectType = null)
    {
        var query = BuildFilteredQuery(projectId, startTime, endTime, status, defectType);

        // 使用 SelectMany 获取所有缺陷
        var defects = await query
            .SelectMany(r => r.Defects)
            .ToListAsync();

        return defects
            .GroupBy(d => d.Type)
            .ToDictionary(g => g.Key, g => g.Count());
    }

    private IQueryable<InspectionResult> BuildFilteredQuery(
        Guid projectId,
        DateTime? startTime,
        DateTime? endTime,
        string? status,
        string? defectType,
        string? flowVersionHash = null)
    {
        var query = _dbSet
            .Where(r => r.ProjectId == projectId && !r.IsDeleted)
            .AsQueryable();

        if (startTime.HasValue)
        {
            query = query.Where(r => r.InspectionTime >= startTime.Value);
        }

        if (endTime.HasValue)
        {
            query = query.Where(r => r.InspectionTime <= endTime.Value);
        }

        if (!string.IsNullOrWhiteSpace(status) &&
            Enum.TryParse<InspectionStatus>(status, true, out var parsedStatus))
        {
            query = query.Where(r => r.Status == parsedStatus);
        }

        if (!string.IsNullOrWhiteSpace(defectType))
        {
            var normalizedDefectType = defectType.Trim();
            query = query.Where(r => r.Defects.Any(d =>
                d.Type.ToString() == normalizedDefectType ||
                (d.Description != null && d.Description == normalizedDefectType)));
        }

        if (!string.IsNullOrWhiteSpace(flowVersionHash))
        {
            var normalizedFlowHash = flowVersionHash.Trim();
            query = query.Where(r => r.FlowVersionHash == normalizedFlowHash);
        }

        return query;
    }

    private static IQueryable<InspectionHistoryItem> SelectHistoryListItems(IQueryable<InspectionResult> query)
    {
        return query
            .AsNoTracking()
            .Select(r => new InspectionHistoryItem
            {
                Id = r.Id,
                ProjectId = r.ProjectId,
                Status = r.Status,
                Defects = r.Defects.Select(d => new InspectionHistoryDefectItem
                {
                    Id = d.Id,
                    Type = d.Type,
                    X = d.X,
                    Y = d.Y,
                    Width = d.Width,
                    Height = d.Height,
                    ConfidenceScore = d.ConfidenceScore,
                    Description = d.Description,
                    AnnotationData = d.AnnotationData
                }).ToList(),
                ProcessingTimeMs = r.ProcessingTimeMs,
                ImageId = r.ImageId,
                ConfidenceScore = r.ConfidenceScore,
                ErrorMessage = r.ErrorMessage,
                InspectionTime = r.InspectionTime,
                FlowVersionHash = r.FlowVersionHash,
                CalibrationBundleId = r.CalibrationBundleId,
                SessionId = r.SessionId,
                HasImage = r.ImageId != null || r.OutputImage != null,
                HasOutputData = r.OutputDataJson != null && r.OutputDataJson != "",
                HasAnalysisData = r.AnalysisDataJson != null && r.AnalysisDataJson != "",
                CreatedAt = r.CreatedAt,
                ModifiedAt = r.ModifiedAt
            });
    }

    private static IQueryable<InspectionHistoryItem> SelectHistoryItemsWithPayload(IQueryable<InspectionResult> query)
    {
        return query
            .AsNoTracking()
            .Select(r => new InspectionHistoryItem
            {
                Id = r.Id,
                ProjectId = r.ProjectId,
                Status = r.Status,
                Defects = r.Defects.Select(d => new InspectionHistoryDefectItem
                {
                    Id = d.Id,
                    Type = d.Type,
                    X = d.X,
                    Y = d.Y,
                    Width = d.Width,
                    Height = d.Height,
                    ConfidenceScore = d.ConfidenceScore,
                    Description = d.Description,
                    AnnotationData = d.AnnotationData
                }).ToList(),
                ProcessingTimeMs = r.ProcessingTimeMs,
                ImageId = r.ImageId,
                ConfidenceScore = r.ConfidenceScore,
                ErrorMessage = r.ErrorMessage,
                InspectionTime = r.InspectionTime,
                FlowVersionHash = r.FlowVersionHash,
                CalibrationBundleId = r.CalibrationBundleId,
                SessionId = r.SessionId,
                HasImage = r.ImageId != null || r.OutputImage != null,
                HasOutputData = r.OutputDataJson != null && r.OutputDataJson != "",
                HasAnalysisData = r.AnalysisDataJson != null && r.AnalysisDataJson != "",
                OutputDataJson = r.OutputDataJson,
                AnalysisDataJson = r.AnalysisDataJson,
                CreatedAt = r.CreatedAt,
                ModifiedAt = r.ModifiedAt
            });
    }

    private static IQueryable<InspectionHistoryDetail> SelectHistoryDetails(IQueryable<InspectionResult> query)
    {
        return query
            .AsNoTracking()
            .Select(r => new InspectionHistoryDetail
            {
                Id = r.Id,
                ProjectId = r.ProjectId,
                Status = r.Status,
                Defects = r.Defects.Select(d => new InspectionHistoryDefectItem
                {
                    Id = d.Id,
                    Type = d.Type,
                    X = d.X,
                    Y = d.Y,
                    Width = d.Width,
                    Height = d.Height,
                    ConfidenceScore = d.ConfidenceScore,
                    Description = d.Description,
                    AnnotationData = d.AnnotationData
                }).ToList(),
                ProcessingTimeMs = r.ProcessingTimeMs,
                ImageId = r.ImageId,
                ConfidenceScore = r.ConfidenceScore,
                ErrorMessage = r.ErrorMessage,
                InspectionTime = r.InspectionTime,
                FlowVersionHash = r.FlowVersionHash,
                CalibrationBundleId = r.CalibrationBundleId,
                SessionId = r.SessionId,
                HasImage = r.ImageId != null || r.OutputImage != null,
                HasOutputData = r.OutputDataJson != null && r.OutputDataJson != "",
                HasAnalysisData = r.AnalysisDataJson != null && r.AnalysisDataJson != "",
                OutputDataJson = r.OutputDataJson,
                AnalysisDataJson = r.AnalysisDataJson,
                CreatedAt = r.CreatedAt,
                ModifiedAt = r.ModifiedAt
            });
    }

    private static InspectionResult ToInspectionResultWithoutOutputImage(InspectionHistoryItem item)
    {
        var result = new InspectionResult(item.ProjectId, item.ImageId);
        result.SetResult(item.Status, item.ProcessingTimeMs, item.ConfidenceScore, item.ErrorMessage);

        if (!string.IsNullOrWhiteSpace(item.OutputDataJson))
        {
            result.SetOutputDataJson(item.OutputDataJson);
        }

        if (!string.IsNullOrWhiteSpace(item.AnalysisDataJson))
        {
            result.SetAnalysisDataJson(item.AnalysisDataJson);
        }

        result.SetTraceability(item.FlowVersionHash, item.CalibrationBundleId, item.SessionId);

        foreach (var defect in item.Defects)
        {
            result.AddDefect(new Defect(
                item.Id,
                defect.Type,
                defect.X,
                defect.Y,
                defect.Width,
                defect.Height,
                defect.ConfidenceScore,
                defect.Description,
                defect.AnnotationData));
        }

        var createdAt = item.CreatedAt == default ? item.InspectionTime : item.CreatedAt;
        result.RestorePersistenceMetadata(item.Id, item.InspectionTime, createdAt, item.ModifiedAt);
        return result;
    }
}
