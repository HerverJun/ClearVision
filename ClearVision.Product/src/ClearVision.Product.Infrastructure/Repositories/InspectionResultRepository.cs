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
public class InspectionResultRepository : RepositoryBase<InspectionResult>, IInspectionResultRepository, IInspectionResultAnalysisRepository
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

        // The database groups the finite outcome state-space, so a large project never
        // materializes one row per inspection just to build dashboard counters.
        var records = await query
            .AsNoTracking()
            .GroupBy(r => new
            {
                r.Status,
                r.ExecutionOutcome,
                r.DecisionOutcome,
                r.HasJudgmentSignal
            })
            .Select(group => new
            {
                group.Key.Status,
                group.Key.ExecutionOutcome,
                group.Key.DecisionOutcome,
                group.Key.HasJudgmentSignal,
                Count = group.Count(),
                AverageProcessingTimeMs = group.Average(item => (double)item.ProcessingTimeMs)
            })
            .ToListAsync();

        var totalCount = 0;
        var executionSucceededCount = 0;
        var okCount = 0;
        var ngCount = 0;
        var undeterminedCount = 0;
        var notApplicableCount = 0;
        var invalidCount = 0;
        var failedCount = 0;
        var cancelledCount = 0;
        var timedOutCount = 0;
        var skippedCount = 0;
        var processingTimeTotal = 0d;

        foreach (var record in records)
        {
            var count = record.Count;
            totalCount += count;
            processingTimeTotal += record.AverageProcessingTimeMs * count;

            var outcome = record.ExecutionOutcome.HasValue && record.DecisionOutcome.HasValue
                ? new InspectionOutcome(
                    record.ExecutionOutcome.Value,
                    record.DecisionOutcome.Value,
                    null,
                    null,
                    null,
                    record.HasJudgmentSignal ??
                    (record.ExecutionOutcome.Value == ExecutionOutcome.Succeeded &&
                     record.DecisionOutcome.Value is DecisionOutcome.Ok or DecisionOutcome.Ng))
                : LegacyInspectionStatusProjection.FromLegacy(record.Status);

            if (outcome.Execution == ExecutionOutcome.Succeeded)
            {
                executionSucceededCount += count;
            }

            switch (InspectionOutcomeClassifier.Classify(outcome))
            {
                case CanonicalInspectionOutcomeKind.Ok:
                    okCount += count;
                    break;
                case CanonicalInspectionOutcomeKind.Ng:
                    ngCount += count;
                    break;
                case CanonicalInspectionOutcomeKind.Undetermined:
                    undeterminedCount += count;
                    break;
                case CanonicalInspectionOutcomeKind.NotApplicable:
                    notApplicableCount += count;
                    break;
                case CanonicalInspectionOutcomeKind.Invalid:
                    invalidCount += count;
                    break;
                case CanonicalInspectionOutcomeKind.Failed:
                    failedCount += count;
                    break;
                case CanonicalInspectionOutcomeKind.Cancelled:
                    cancelledCount += count;
                    break;
                case CanonicalInspectionOutcomeKind.TimedOut:
                    timedOutCount += count;
                    break;
                case CanonicalInspectionOutcomeKind.Skipped:
                    skippedCount += count;
                    break;
            }
        }

        var validDecisionCount = okCount + ngCount;
        var executionFailureCount = failedCount + timedOutCount;
        var averageProcessingTimeMs = totalCount == 0 ? 0 : processingTimeTotal / totalCount;

        return new InspectionStatistics
        {
            TotalCount = totalCount,
            OKCount = okCount,
            NGCount = ngCount,
            ErrorCount = executionFailureCount + invalidCount,
            OKRate = validDecisionCount > 0 ? okCount / (double)validDecisionCount : 0,
            YieldRate = validDecisionCount > 0 ? okCount / (double)validDecisionCount : 0,
            ExecutionSucceededCount = executionSucceededCount,
            ValidDecisionCount = validDecisionCount,
            DecisionCoverageRate = executionSucceededCount > 0 ? validDecisionCount / (double)executionSucceededCount : 0,
            ExecutionFailureCount = executionFailureCount,
            UndeterminedCount = undeterminedCount,
            NotApplicableCount = notApplicableCount,
            InvalidCount = invalidCount,
            FailedCount = failedCount,
            CancelledCount = cancelledCount,
            TimedOutCount = timedOutCount,
            SkippedCount = skippedCount,
            AverageProcessingTimeMs = averageProcessingTimeMs
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

        var defects = await query
            .AsNoTracking()
            .SelectMany(r => r.Defects)
            .GroupBy(defect => defect.Type)
            .Select(group => new { DefectType = group.Key, Count = group.Count() })
            .ToListAsync();

        return defects.ToDictionary(item => item.DefectType, item => item.Count);
    }

    public async Task<IReadOnlyList<InspectionAnalysisSample>> GetAnalysisSamplesAsync(
        InspectionAnalysisQuery query,
        int maxRows)
    {
        var boundedMaxRows = Math.Clamp(maxRows, 1, 100_000);
        return await BuildFilteredQuery(
                query.ProjectId,
                query.StartTime,
                query.EndTime,
                query.Status,
                query.DefectType)
            .AsNoTracking()
            .OrderBy(item => item.InspectionTime)
            .ThenBy(item => item.Id)
            .Select(item => new InspectionAnalysisSample(
                item.InspectionTime,
                item.Status,
                item.ExecutionOutcome,
                item.DecisionOutcome,
                item.HasJudgmentSignal,
                item.ProcessingTimeMs,
                item.Defects.Count()))
            .Take(boundedMaxRows + 1)
            .ToListAsync();
    }

    public async Task<InspectionConfidenceSummary> GetConfidenceSummaryAsync(InspectionAnalysisQuery query)
    {
        var groups = await BuildFilteredQuery(
                query.ProjectId,
                query.StartTime,
                query.EndTime,
                query.Status,
                query.DefectType)
            .AsNoTracking()
            .SelectMany(result => result.Defects)
            .GroupBy(defect => defect.ConfidenceScore >= 0.9d
                ? 0
                : defect.ConfidenceScore >= 0.8d
                    ? 1
                    : defect.ConfidenceScore >= 0.7d
                        ? 2
                        : defect.ConfidenceScore >= 0.6d
                            ? 3
                            : defect.ConfidenceScore >= 0.5d
                                ? 4
                                : 5)
            .Select(group => new
            {
                Bucket = group.Key,
                Count = group.Count(),
                TotalConfidence = group.Sum(defect => defect.ConfidenceScore)
            })
            .ToListAsync();

        var counts = new int[6];
        var totalDefects = 0;
        var totalConfidence = 0d;
        foreach (var group in groups)
        {
            counts[group.Bucket] = group.Count;
            totalDefects += group.Count;
            totalConfidence += group.TotalConfidence;
        }

        return new InspectionConfidenceSummary(
            counts[0],
            counts[1],
            counts[2],
            counts[3],
            counts[4],
            counts[5],
            totalDefects,
            totalDefects == 0 ? 0 : totalConfidence / totalDefects);
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
