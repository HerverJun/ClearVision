// InspectionResultRepository.cs
// 检测结果仓储实现
// 作者：蘅芜君

using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Interfaces;
using ClearVision.Product.Core.Outcomes;
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

    public async Task<InspectionResult?> FindByExecutionSnapshotIdAsync(Guid projectId, Guid executionSnapshotId)
    {
        if (projectId == Guid.Empty || executionSnapshotId == Guid.Empty)
        {
            return null;
        }

        return await _dbSet
            .Include(result => result.Defects)
            .Where(result => result.ProjectId == projectId &&
                             result.ExecutionSnapshotId == executionSnapshotId &&
                             !result.IsDeleted)
            .OrderByDescending(result => result.InspectionTime)
            .ThenByDescending(result => result.Id)
            .FirstOrDefaultAsync();
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
                ((r.ExecutionOutcome == ExecutionOutcome.Succeeded && r.DecisionOutcome == DecisionOutcome.Ok) ||
                 ((!r.ExecutionOutcome.HasValue || !r.DecisionOutcome.HasValue) && r.Status == InspectionStatus.OK)) &&
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

        var records = await query
            .AsNoTracking()
            .Select(r => new
            {
                r.Status,
                r.ExecutionOutcome,
                r.DecisionOutcome,
                r.HasJudgmentSignal,
                r.ProcessingTimeMs
            })
            .ToListAsync();
        var outcomeStatistics = InspectionOutcomeStatistics.Calculate(records.Select(record =>
            record.ExecutionOutcome.HasValue && record.DecisionOutcome.HasValue
                ? new InspectionOutcome(
                    record.ExecutionOutcome.Value,
                    record.DecisionOutcome.Value,
                    null,
                    null,
                    null,
                    record.HasJudgmentSignal ??
                    (record.ExecutionOutcome.Value == ExecutionOutcome.Succeeded &&
                     record.DecisionOutcome.Value is DecisionOutcome.Ok or DecisionOutcome.Ng))
                : LegacyInspectionStatusProjection.FromLegacy(record.Status)));
        var avgTime = records.Count > 0 ? records.Average(record => record.ProcessingTimeMs) : 0;

        return new InspectionStatistics
        {
            TotalCount = outcomeStatistics.TotalAttemptCount,
            OKCount = outcomeStatistics.OkCount,
            NGCount = outcomeStatistics.NgCount,
            ErrorCount = outcomeStatistics.ExecutionFailureCount + outcomeStatistics.InvalidCount,
            OKRate = outcomeStatistics.YieldRate,
            YieldRate = outcomeStatistics.YieldRate,
            ExecutionSucceededCount = outcomeStatistics.ExecutionSucceededCount,
            ValidDecisionCount = outcomeStatistics.ValidDecisionCount,
            DecisionCoverageRate = outcomeStatistics.DecisionCoverageRate,
            ExecutionFailureCount = outcomeStatistics.ExecutionFailureCount,
            UndeterminedCount = outcomeStatistics.UndeterminedCount,
            NotApplicableCount = outcomeStatistics.NotApplicableCount,
            InvalidCount = outcomeStatistics.InvalidCount,
            FailedCount = outcomeStatistics.FailedCount,
            CancelledCount = outcomeStatistics.CancelledCount,
            TimedOutCount = outcomeStatistics.TimedOutCount,
            SkippedCount = outcomeStatistics.SkippedCount,
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

        if (!string.IsNullOrWhiteSpace(status))
        {
            query = ApplyOutcomeFilter(query, status.Trim());
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

    private static IQueryable<InspectionResult> ApplyOutcomeFilter(
        IQueryable<InspectionResult> query,
        string status)
    {
        if (Enum.TryParse<CanonicalInspectionOutcomeKind>(status, true, out var outcomeKind))
        {
            return outcomeKind switch
            {
                CanonicalInspectionOutcomeKind.Ok => query.Where(r =>
                    (r.ExecutionOutcome == ExecutionOutcome.Succeeded && r.DecisionOutcome == DecisionOutcome.Ok) ||
                    ((!r.ExecutionOutcome.HasValue || !r.DecisionOutcome.HasValue) && r.Status == InspectionStatus.OK)),
                CanonicalInspectionOutcomeKind.Ng => query.Where(r =>
                    (r.ExecutionOutcome == ExecutionOutcome.Succeeded && r.DecisionOutcome == DecisionOutcome.Ng) ||
                    ((!r.ExecutionOutcome.HasValue || !r.DecisionOutcome.HasValue) && r.Status == InspectionStatus.NG)),
                CanonicalInspectionOutcomeKind.Undetermined => query.Where(r =>
                    r.ExecutionOutcome == ExecutionOutcome.Succeeded && r.DecisionOutcome == DecisionOutcome.Undetermined ||
                    ((!r.ExecutionOutcome.HasValue || !r.DecisionOutcome.HasValue) && r.Status == InspectionStatus.Inspecting)),
                CanonicalInspectionOutcomeKind.NotApplicable => query.Where(r =>
                    r.ExecutionOutcome == ExecutionOutcome.Succeeded && r.DecisionOutcome == DecisionOutcome.NotApplicable),
                CanonicalInspectionOutcomeKind.Invalid => query.Where(r =>
                    r.ExecutionOutcome == ExecutionOutcome.Succeeded && r.DecisionOutcome == DecisionOutcome.Invalid),
                CanonicalInspectionOutcomeKind.Failed => query.Where(r =>
                    r.ExecutionOutcome == ExecutionOutcome.Failed ||
                    ((!r.ExecutionOutcome.HasValue || !r.DecisionOutcome.HasValue) && r.Status == InspectionStatus.Error)),
                CanonicalInspectionOutcomeKind.Cancelled => query.Where(r => r.ExecutionOutcome == ExecutionOutcome.Cancelled),
                CanonicalInspectionOutcomeKind.TimedOut => query.Where(r => r.ExecutionOutcome == ExecutionOutcome.TimedOut),
                CanonicalInspectionOutcomeKind.Skipped => query.Where(r =>
                    r.ExecutionOutcome == ExecutionOutcome.Skipped ||
                    ((!r.ExecutionOutcome.HasValue || !r.DecisionOutcome.HasValue) && r.Status == InspectionStatus.NotInspected)),
                _ => query
            };
        }

        if (!Enum.TryParse<InspectionStatus>(status, true, out var legacyStatus))
        {
            return query;
        }

        return legacyStatus switch
        {
            InspectionStatus.OK => ApplyOutcomeFilter(query, CanonicalInspectionOutcomeKind.Ok.ToString()),
            InspectionStatus.NG => ApplyOutcomeFilter(query, CanonicalInspectionOutcomeKind.Ng.ToString()),
            InspectionStatus.Error => query.Where(r =>
                r.ExecutionOutcome == ExecutionOutcome.Failed ||
                r.ExecutionOutcome == ExecutionOutcome.TimedOut ||
                (r.ExecutionOutcome == ExecutionOutcome.Succeeded && r.DecisionOutcome == DecisionOutcome.Invalid) ||
                ((!r.ExecutionOutcome.HasValue || !r.DecisionOutcome.HasValue) && r.Status == InspectionStatus.Error)),
            InspectionStatus.NotInspected => query.Where(r =>
                r.ExecutionOutcome == ExecutionOutcome.Cancelled ||
                r.ExecutionOutcome == ExecutionOutcome.Skipped ||
                (r.ExecutionOutcome == ExecutionOutcome.Succeeded &&
                 (r.DecisionOutcome == DecisionOutcome.Undetermined || r.DecisionOutcome == DecisionOutcome.NotApplicable)) ||
                ((!r.ExecutionOutcome.HasValue || !r.DecisionOutcome.HasValue) && r.Status == InspectionStatus.NotInspected)),
            InspectionStatus.Inspecting => ApplyOutcomeFilter(query, CanonicalInspectionOutcomeKind.Undetermined.ToString()),
            _ => query
        };
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
                ExecutionOutcome = r.ExecutionOutcome,
                DecisionOutcome = r.DecisionOutcome,
                DecisionSource = r.DecisionSource,
                ReasonCode = r.ReasonCode,
                HasJudgmentSignal = r.HasJudgmentSignal,
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
                ExecutionOutcome = r.ExecutionOutcome,
                DecisionOutcome = r.DecisionOutcome,
                DecisionSource = r.DecisionSource,
                ReasonCode = r.ReasonCode,
                HasJudgmentSignal = r.HasJudgmentSignal,
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
                ExecutionOutcome = r.ExecutionOutcome,
                DecisionOutcome = r.DecisionOutcome,
                DecisionSource = r.DecisionSource,
                ReasonCode = r.ReasonCode,
                HasJudgmentSignal = r.HasJudgmentSignal,
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
                ExecutionSnapshotId = r.ExecutionSnapshotId,
                ProjectPersistenceRevision = r.ProjectPersistenceRevision,
                DecisionConfigurationHash = r.DecisionConfigurationHash,
                RuntimePackageId = r.RuntimePackageId,
                ExecutionSource = r.ExecutionSource,
                ExecutionRunMode = r.ExecutionRunMode,
                ShadowRole = r.ShadowRole,
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
        if (item.ExecutionOutcome.HasValue && item.DecisionOutcome.HasValue)
        {
            result.SetOutcome(
                new ClearVision.Product.Core.Outcomes.InspectionOutcome(
                    item.ExecutionOutcome.Value,
                    item.DecisionOutcome.Value,
                    item.DecisionSource,
                    item.ReasonCode,
                    item.ErrorMessage,
                    item.HasJudgmentSignal ??
                    (item.ExecutionOutcome.Value == ClearVision.Product.Core.Outcomes.ExecutionOutcome.Succeeded &&
                     item.DecisionOutcome.Value is ClearVision.Product.Core.Outcomes.DecisionOutcome.Ok or ClearVision.Product.Core.Outcomes.DecisionOutcome.Ng)),
                item.ProcessingTimeMs,
                item.ConfidenceScore);
        }
        else
        {
            result.SetResult(item.Status, item.ProcessingTimeMs, item.ConfidenceScore, item.ErrorMessage);
        }

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
