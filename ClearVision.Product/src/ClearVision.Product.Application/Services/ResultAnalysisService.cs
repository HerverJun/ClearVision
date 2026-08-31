// ResultAnalysisService.cs
// 时间间隔枚举
// 作者：蘅芜君

using System.Globalization;
using ClearVision.Product.Application.DTOs;
using ClearVision.Product.Application.Exports;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Interfaces;
using ClearVision.Product.Core.Outcomes;

namespace ClearVision.Product.Application.Services;

/// <summary>
/// 结果分析服务 - 提供检测数据统计、报表和导出功能
/// </summary>
public interface IResultAnalysisService
{
    /// <summary>
    /// 获取检测统计概览
    /// </summary>
    Task<InspectionStatisticsDto> GetStatisticsAsync(Guid projectId, DateTime? startTime = null, DateTime? endTime = null, string? status = null, string? defectType = null);

    /// <summary>
    /// 获取缺陷类型分布
    /// </summary>
    Task<DefectDistributionDto> GetDefectDistributionAsync(Guid projectId, DateTime? startTime = null, DateTime? endTime = null, string? status = null, string? defectType = null);

    /// <summary>
    /// 获取置信度分布
    /// </summary>
    Task<ConfidenceDistributionDto> GetConfidenceDistributionAsync(Guid projectId, DateTime? startTime = null, DateTime? endTime = null, string? status = null, string? defectType = null);

    /// <summary>
    /// 获取检测趋势（按小时/天/周）
    /// </summary>
    Task<TrendAnalysisDto> GetTrendAnalysisAsync(Guid projectId, TrendInterval interval, DateTime startTime, DateTime endTime, string? status = null, string? defectType = null);

    /// <summary>
    /// 导出检测结果为CSV
    /// </summary>
    Task<string> ExportToCsvAsync(Guid projectId, DateTime? startTime = null, DateTime? endTime = null, string? status = null, string? defectType = null);

    /// <summary>
    /// 导出检测结果为JSON
    /// </summary>
    Task<string> ExportToJsonAsync(Guid projectId, DateTime? startTime = null, DateTime? endTime = null, string? status = null, string? defectType = null);

    /// <summary>
    /// 生成检测报告
    /// </summary>
    Task<InspectionReportDto> GenerateReportAsync(Guid projectId, DateTime? startTime = null, DateTime? endTime = null, string? status = null, string? defectType = null);

    /// <summary>
    /// 对比两个时间段的数据
    /// </summary>
    Task<ComparisonAnalysisDto> ComparePeriodsAsync(Guid projectId, DateTime period1Start, DateTime period1End, DateTime period2Start, DateTime period2End);

    /// <summary>
    /// 获取缺陷热点图数据
    /// </summary>
    Task<DefectHeatmapDto> GetDefectHeatmapAsync(Guid projectId, DateTime? startTime = null, DateTime? endTime = null, string? status = null, string? defectType = null);
}

/// <summary>
/// 结果分析服务实现
/// </summary>
public class ResultAnalysisService : IResultAnalysisService
{
    private readonly IInspectionResultRepository _resultRepository;
    private readonly IProjectRepository? _projectRepository;

    public ResultAnalysisService(IInspectionResultRepository resultRepository, IProjectRepository? projectRepository = null)
    {
        _resultRepository = resultRepository ?? throw new ArgumentNullException(nameof(resultRepository));
        _projectRepository = projectRepository;
    }

    /// <inheritdoc />
    public async Task<InspectionStatisticsDto> GetStatisticsAsync(Guid projectId, DateTime? startTime = null, DateTime? endTime = null, string? status = null, string? defectType = null)
    {
        var window = NormalizeWindow(startTime, endTime);
        if (!await IsActiveProjectAsync(projectId))
        {
            return EmptyStatistics(projectId, window.StartTime, window.EndTime);
        }

        var (statistics, _) = await GetStatisticsAndDistributionAsync(projectId, window, status, defectType);
        return statistics;
    }

    /// <inheritdoc />
    public async Task<DefectDistributionDto> GetDefectDistributionAsync(Guid projectId, DateTime? startTime = null, DateTime? endTime = null, string? status = null, string? defectType = null)
    {
        var window = NormalizeWindow(startTime, endTime);
        if (!await IsActiveProjectAsync(projectId))
        {
            return EmptyDefectDistribution(projectId, window.StartTime, window.EndTime);
        }

        var distribution = await _resultRepository.GetDefectDistributionAsync(
            projectId,
            window.StartTime,
            window.EndTime,
            status,
            defectType);
        return MapDefectDistribution(projectId, window, distribution);
    }

    /// <inheritdoc />
    public async Task<ConfidenceDistributionDto> GetConfidenceDistributionAsync(Guid projectId, DateTime? startTime = null, DateTime? endTime = null, string? status = null, string? defectType = null)
    {
        var window = NormalizeWindow(startTime, endTime);
        if (!await IsActiveProjectAsync(projectId))
        {
            return EmptyConfidenceDistribution(projectId, window.StartTime, window.EndTime);
        }

        return await GetConfidenceDistributionCoreAsync(projectId, window, status, defectType);
    }

    /// <inheritdoc />
    public async Task<TrendAnalysisDto> GetTrendAnalysisAsync(Guid projectId, TrendInterval interval, DateTime startTime, DateTime endTime, string? status = null, string? defectType = null)
    {
        var window = ResultAnalysisQueryBudget.Validate(startTime, endTime);
        var bucketStarts = ResultAnalysisQueryBudget.BuildTrendBuckets(interval, window.StartTime, window.EndTime);
        if (!await IsActiveProjectAsync(projectId))
        {
            return EmptyTrend(projectId, interval, window.StartTime, window.EndTime);
        }

        var query = CreateAnalysisQuery(projectId, window, status, defectType);
        var samples = await GetBoundedAnalysisSamplesAsync(query);
        if (samples.Count == 0)
        {
            return new TrendAnalysisDto
            {
                ProjectId = projectId,
                Interval = interval.ToString(),
                StartTime = window.StartTime,
                EndTime = window.EndTime
            };
        }

        var accumulators = bucketStarts.Select(_ => new TrendBucketAccumulator()).ToArray();

        foreach (var sample in samples)
        {
            var bucketIndex = FindBucketIndex(bucketStarts, sample.InspectionTime);
            if (bucketIndex >= 0)
            {
                accumulators[bucketIndex].Add(sample);
            }
        }

        return new TrendAnalysisDto
        {
            ProjectId = projectId,
            Interval = interval.ToString(),
            StartTime = window.StartTime,
            EndTime = window.EndTime,
            DataPoints = bucketStarts
                .Select((bucketStart, index) => accumulators[index].ToDataPoint(bucketStart))
                .ToList()
        };
    }

    /// <inheritdoc />
    public async Task<string> ExportToCsvAsync(Guid projectId, DateTime? startTime = null, DateTime? endTime = null, string? status = null, string? defectType = null)
    {
        if (!await IsActiveProjectAsync(projectId))
        {
            return string.Empty;
        }

        var results = await _resultRepository.GetByTimeRangeAsync(projectId, startTime ?? DateTime.MinValue, endTime ?? DateTime.MaxValue, status, defectType);

        var csv = new System.Text.StringBuilder();
        csv.AppendLine(CsvSanitizer.ToCsvRow("检测ID", "工程ID", "检测时间", "兼容状态", "执行结果", "判定结果", "CanonicalOutcome", "原因码", "处理时间(ms)", "置信度", "缺陷数量", "错误信息", "缺陷类型", "X", "Y", "Width", "Height", "缺陷置信度", "缺陷描述"));

        foreach (var result in results)
        {
            var outcome = result.GetOutcome();
            csv.AppendLine(CsvSanitizer.ToCsvRow(
                result.Id,
                result.ProjectId,
                result.InspectionTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                result.Status,
                outcome.Execution,
                outcome.Decision,
                InspectionOutcomeClassifier.Classify(outcome),
                outcome.ReasonCode,
                result.ProcessingTimeMs,
                result.ConfidenceScore?.ToString("F4", CultureInfo.InvariantCulture),
                result.Defects.Count,
                result.ErrorMessage,
                null,
                null,
                null,
                null,
                null,
                null,
                null));

            foreach (var defect in result.Defects)
            {
                csv.AppendLine(CsvSanitizer.ToCsvRow(
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    defect.Type,
                    defect.X.ToString("F2", CultureInfo.InvariantCulture),
                    defect.Y.ToString("F2", CultureInfo.InvariantCulture),
                    defect.Width.ToString("F2", CultureInfo.InvariantCulture),
                    defect.Height.ToString("F2", CultureInfo.InvariantCulture),
                    defect.ConfidenceScore.ToString("F4", CultureInfo.InvariantCulture),
                    defect.Description));
            }
        }

        return csv.ToString();
    }

    /// <inheritdoc />
    public async Task<string> ExportToJsonAsync(Guid projectId, DateTime? startTime = null, DateTime? endTime = null, string? status = null, string? defectType = null)
    {
        if (!await IsActiveProjectAsync(projectId))
        {
            return System.Text.Json.JsonSerializer.Serialize(new InspectionExportDto
            {
                ProjectId = projectId,
                ExportTime = DateTime.UtcNow,
                StartTime = startTime,
                EndTime = endTime
            }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        }

        var results = await _resultRepository.GetByTimeRangeAsync(projectId, startTime ?? DateTime.MinValue, endTime ?? DateTime.MaxValue, status, defectType);

        var exportData = new InspectionExportDto
        {
            ProjectId = projectId,
            ExportTime = DateTime.UtcNow,
            StartTime = startTime,
            EndTime = endTime,
            Results = results.Select(r => new InspectionResultExportItemDto
            {
                Id = r.Id,
                InspectionTime = r.InspectionTime,
                Status = r.Status.ToString(),
                ExecutionOutcome = r.GetOutcome().Execution.ToString(),
                DecisionOutcome = r.GetOutcome().Decision.ToString(),
                CanonicalOutcome = InspectionOutcomeClassifier.Classify(r.GetOutcome()).ToString(),
                ReasonCode = r.GetOutcome().ReasonCode,
                ProcessingTimeMs = r.ProcessingTimeMs,
                ConfidenceScore = r.ConfidenceScore,
                ErrorMessage = r.ErrorMessage,
                Defects = r.Defects.Select(d => new DefectExportDto
                {
                    Type = d.Type.ToString(),
                    X = d.X,
                    Y = d.Y,
                    Width = d.Width,
                    Height = d.Height,
                    ConfidenceScore = d.ConfidenceScore,
                    Description = d.Description
                }).ToList()
            }).ToList()
        };

        return System.Text.Json.JsonSerializer.Serialize(exportData, new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true
        });
    }

    /// <inheritdoc />
    public async Task<InspectionReportDto> GenerateReportAsync(Guid projectId, DateTime? startTime = null, DateTime? endTime = null, string? status = null, string? defectType = null)
    {
        var window = NormalizeWindow(startTime, endTime);
        if (!await IsActiveProjectAsync(projectId))
        {
            return EmptyReport(projectId, window.StartTime, window.EndTime);
        }

        // Reuse the same two aggregates for summary and distribution. This prevents a
        // report from issuing a second full defect query after statistics has run.
        var (statistics, defectDistribution) = await GetStatisticsAndDistributionAsync(projectId, window, status, defectType);
        var confidenceDistribution = await GetConfidenceDistributionCoreAsync(projectId, window, status, defectType);

        var hourlyStart = window.EndTime.AddHours(-24) > window.StartTime
            ? window.EndTime.AddHours(-24)
            : window.StartTime;
        var hourlyTrend = await GetTrendAnalysisAsync(
            projectId,
            TrendInterval.Hour,
            hourlyStart,
            window.EndTime,
            status,
            defectType);

        return new InspectionReportDto
        {
            ProjectId = projectId,
            GeneratedAt = DateTime.UtcNow,
            Period = new ReportPeriodDto
            {
                StartTime = window.StartTime,
                EndTime = window.EndTime
            },
            Summary = statistics,
            DefectDistribution = defectDistribution,
            ConfidenceDistribution = confidenceDistribution,
            HourlyTrend = hourlyTrend,
            Recommendations = GenerateRecommendations(statistics, defectDistribution)
        };
    }

    /// <inheritdoc />
    public async Task<ComparisonAnalysisDto> ComparePeriodsAsync(Guid projectId, DateTime period1Start, DateTime period1End, DateTime period2Start, DateTime period2End)
    {
        if (!await IsActiveProjectAsync(projectId))
        {
            return new ComparisonAnalysisDto
            {
                ProjectId = projectId,
                Period1 = new ReportPeriodDto { StartTime = period1Start, EndTime = period1End },
                Period2 = new ReportPeriodDto { StartTime = period2Start, EndTime = period2End }
            };
        }

        var period1Stats = await GetStatisticsAsync(projectId, period1Start, period1End);
        var period2Stats = await GetStatisticsAsync(projectId, period2Start, period2End);

        var comparisons = new List<MetricComparisonDto>
        {
            new()
            {
                Metric = "总检测数",
                Period1Value = period1Stats.TotalCount,
                Period2Value = period2Stats.TotalCount,
                Change = CalculateChange(period1Stats.TotalCount, period2Stats.TotalCount),
                IsPositive = period2Stats.TotalCount >= period1Stats.TotalCount
            },
            new()
            {
                Metric = "OK率",
                Period1Value = period1Stats.OKRate * 100,
                Period2Value = period2Stats.OKRate * 100,
                Change = CalculateChange(period1Stats.OKRate, period2Stats.OKRate),
                IsPositive = period2Stats.OKRate >= period1Stats.OKRate
            },
            new()
            {
                Metric = "NG率",
                Period1Value = period1Stats.NGRate * 100,
                Period2Value = period2Stats.NGRate * 100,
                Change = CalculateChange(period1Stats.NGRate, period2Stats.NGRate),
                IsPositive = period2Stats.NGRate <= period1Stats.NGRate // NG率越低越好
            },
            new()
            {
                Metric = "平均处理时间(ms)",
                Period1Value = period1Stats.AverageProcessingTimeMs,
                Period2Value = period2Stats.AverageProcessingTimeMs,
                Change = CalculateChange(period1Stats.AverageProcessingTimeMs, period2Stats.AverageProcessingTimeMs),
                IsPositive = period2Stats.AverageProcessingTimeMs <= period1Stats.AverageProcessingTimeMs // 处理时间越短越好
            }
        };

        return new ComparisonAnalysisDto
        {
            ProjectId = projectId,
            Period1 = new ReportPeriodDto { StartTime = period1Start, EndTime = period1End },
            Period2 = new ReportPeriodDto { StartTime = period2Start, EndTime = period2End },
            Comparisons = comparisons,
            Summary = GenerateComparisonSummary(comparisons)
        };
    }

    /// <inheritdoc />
    public async Task<DefectHeatmapDto> GetDefectHeatmapAsync(Guid projectId, DateTime? startTime = null, DateTime? endTime = null, string? status = null, string? defectType = null)
    {
        if (!await IsActiveProjectAsync(projectId))
        {
            return new DefectHeatmapDto
            {
                ProjectId = projectId,
                StartTime = startTime,
                EndTime = endTime
            };
        }

        var results = await _resultRepository.GetByTimeRangeAsync(projectId, startTime ?? DateTime.MinValue, endTime ?? DateTime.MaxValue, status, defectType);
        var allDefects = results.SelectMany(r => r.Defects).ToList();

        if (!allDefects.Any())
        {
            return new DefectHeatmapDto
            {
                ProjectId = projectId,
                StartTime = startTime,
                EndTime = endTime,
                TotalDefects = 0,
                GridSize = 10,
                Cells = new List<HeatmapCellDto>()
            };
        }

        // 计算边界
        var minX = allDefects.Min(d => d.X);
        var maxX = allDefects.Max(d => d.X + d.Width);
        var minY = allDefects.Min(d => d.Y);
        var maxY = allDefects.Max(d => d.Y + d.Height);

        var width = maxX - minX;
        var height = maxY - minY;

        // 创建10x10网格
        const int gridSize = 10;
        var cellWidth = width / gridSize;
        var cellHeight = height / gridSize;

        var cells = new List<HeatmapCellDto>();

        for (int row = 0; row < gridSize; row++)
        {
            for (int col = 0; col < gridSize; col++)
            {
                var cellMinX = minX + col * cellWidth;
                var cellMaxX = cellMinX + cellWidth;
                var cellMinY = minY + row * cellHeight;
                var cellMaxY = cellMinY + cellHeight;

                var count = allDefects.Count(d =>
                    d.X >= cellMinX && d.X < cellMaxX &&
                    d.Y >= cellMinY && d.Y < cellMaxY);

                cells.Add(new HeatmapCellDto
                {
                    Row = row,
                    Column = col,
                    X = cellMinX,
                    Y = cellMinY,
                    Width = cellWidth,
                    Height = cellHeight,
                    DefectCount = count,
                    Density = (double)count / allDefects.Count
                });
            }
        }

        return new DefectHeatmapDto
        {
            ProjectId = projectId,
            StartTime = startTime,
            EndTime = endTime,
            TotalDefects = allDefects.Count,
            ImageBounds = new BoundsDto { MinX = minX, MinY = minY, MaxX = maxX, MaxY = maxY },
            GridSize = gridSize,
            Cells = cells
        };
    }

    #region Private Methods

    private static ResultAnalysisWindow NormalizeWindow(DateTime? startTime, DateTime? endTime) =>
        ResultAnalysisQueryBudget.Normalize(startTime, endTime, DateTime.UtcNow);

    private async Task<(InspectionStatisticsDto Statistics, DefectDistributionDto Distribution)> GetStatisticsAndDistributionAsync(
        Guid projectId,
        ResultAnalysisWindow window,
        string? status,
        string? defectType)
    {
        // The repository uses separate compact DB aggregates; run them serially because
        // both operations share the request-scoped DbContext.
        var statistics = await _resultRepository.GetStatisticsAsync(
            projectId,
            window.StartTime,
            window.EndTime,
            status,
            defectType);
        var distribution = await _resultRepository.GetDefectDistributionAsync(
            projectId,
            window.StartTime,
            window.EndTime,
            status,
            defectType);
        var distributionDto = MapDefectDistribution(projectId, window, distribution);

        return (new InspectionStatisticsDto
        {
            ProjectId = projectId,
            StartTime = window.StartTime,
            EndTime = window.EndTime,
            TotalCount = statistics.TotalCount,
            OKCount = statistics.OKCount,
            NGCount = statistics.NGCount,
            ErrorCount = statistics.ErrorCount,
            OKRate = statistics.OKRate,
            YieldRate = statistics.YieldRate,
            ExecutionSucceededCount = statistics.ExecutionSucceededCount,
            ValidDecisionCount = statistics.ValidDecisionCount,
            DecisionCoverageRate = statistics.DecisionCoverageRate,
            ExecutionFailureCount = statistics.ExecutionFailureCount,
            UndeterminedCount = statistics.UndeterminedCount,
            NotApplicableCount = statistics.NotApplicableCount,
            InvalidCount = statistics.InvalidCount,
            FailedCount = statistics.FailedCount,
            CancelledCount = statistics.CancelledCount,
            TimedOutCount = statistics.TimedOutCount,
            SkippedCount = statistics.SkippedCount,
            NGRate = statistics.ValidDecisionCount > 0 ? (double)statistics.NGCount / statistics.ValidDecisionCount : 0,
            ErrorRate = statistics.TotalCount > 0 ? (double)statistics.ErrorCount / statistics.TotalCount : 0,
            AverageProcessingTimeMs = statistics.AverageProcessingTimeMs,
            TotalDefects = distributionDto.TotalDefects
        }, distributionDto);
    }

    private async Task<ConfidenceDistributionDto> GetConfidenceDistributionCoreAsync(
        Guid projectId,
        ResultAnalysisWindow window,
        string? status,
        string? defectType)
    {
        var query = CreateAnalysisQuery(projectId, window, status, defectType);
        if (_resultRepository is IInspectionResultAnalysisRepository analysisRepository)
        {
            return MapConfidenceDistribution(
                projectId,
                window,
                await analysisRepository.GetConfidenceSummaryAsync(query));
        }

        // Compatibility for narrow test doubles and third-party repository implementations.
        // Built-in production storage takes the aggregate path above.
        var results = await _resultRepository.GetByTimeRangeAsync(
            projectId,
            window.StartTime,
            window.EndTime,
            status,
            defectType);
        var defects = results
            .SelectMany(result => result.Defects)
            .Take(ResultAnalysisQueryBudget.MaximumTrendRows + 1)
            .ToList();
        if (defects.Count > ResultAnalysisQueryBudget.MaximumTrendRows)
        {
            throw new ResultAnalysisBudgetException(
                "ANALYSIS_QUERY_ROW_LIMIT",
                $"Analysis requests may scan at most {ResultAnalysisQueryBudget.MaximumTrendRows} result rows.");
        }

        return MapConfidenceDistribution(projectId, window, BuildConfidenceSummary(defects));
    }

    private async Task<IReadOnlyList<InspectionAnalysisSample>> GetBoundedAnalysisSamplesAsync(InspectionAnalysisQuery query)
    {
        IReadOnlyList<InspectionAnalysisSample> samples;
        if (_resultRepository is IInspectionResultAnalysisRepository analysisRepository)
        {
            samples = await analysisRepository.GetAnalysisSamplesAsync(
                query,
                ResultAnalysisQueryBudget.MaximumTrendRows);
        }
        else
        {
            var results = await _resultRepository.GetByTimeRangeAsync(
                query.ProjectId,
                query.StartTime,
                query.EndTime,
                query.Status,
                query.DefectType);
            samples = results
                .Take(ResultAnalysisQueryBudget.MaximumTrendRows + 1)
                .Select(result => new InspectionAnalysisSample(
                    result.InspectionTime,
                    result.Status,
                    result.ExecutionOutcome,
                    result.DecisionOutcome,
                    result.HasJudgmentSignal,
                    result.ProcessingTimeMs,
                    result.Defects.Count))
                .ToList();
        }

        if (samples.Count > ResultAnalysisQueryBudget.MaximumTrendRows)
        {
            throw new ResultAnalysisBudgetException(
                "ANALYSIS_QUERY_ROW_LIMIT",
                $"Analysis requests may scan at most {ResultAnalysisQueryBudget.MaximumTrendRows} result rows.");
        }

        return samples;
    }

    private static InspectionAnalysisQuery CreateAnalysisQuery(
        Guid projectId,
        ResultAnalysisWindow window,
        string? status,
        string? defectType) =>
        new(projectId, window.StartTime, window.EndTime, status, defectType);

    private static DefectDistributionDto MapDefectDistribution(
        Guid projectId,
        ResultAnalysisWindow window,
        IReadOnlyDictionary<DefectType, int> distribution)
    {
        var total = distribution.Values.Sum();
        return new DefectDistributionDto
        {
            ProjectId = projectId,
            StartTime = window.StartTime,
            EndTime = window.EndTime,
            TotalDefects = total,
            Items = distribution
                .Select(item => new DefectDistributionItemDto
                {
                    DefectType = item.Key.ToString(),
                    Count = item.Value,
                    Percentage = total > 0 ? item.Value / (double)total * 100 : 0
                })
                .OrderByDescending(item => item.Count)
                .ThenBy(item => item.DefectType, StringComparer.Ordinal)
                .ToList()
        };
    }

    private static ConfidenceDistributionDto MapConfidenceDistribution(
        Guid projectId,
        ResultAnalysisWindow window,
        InspectionConfidenceSummary summary)
    {
        var buckets = new (string Range, int Count)[]
        {
            ("90-100%", summary.NinetyToOneHundred),
            ("80-90%", summary.EightyToNinety),
            ("70-80%", summary.SeventyToEighty),
            ("60-70%", summary.SixtyToSeventy),
            ("50-60%", summary.FiftyToSixty),
            ("<50%", summary.BelowFifty)
        };

        return new ConfidenceDistributionDto
        {
            ProjectId = projectId,
            StartTime = window.StartTime,
            EndTime = window.EndTime,
            TotalDefects = summary.TotalDefects,
            Buckets = buckets
                .Select(bucket => new ConfidenceBucketDto
                {
                    Range = bucket.Range,
                    Count = bucket.Count,
                    Percentage = summary.TotalDefects > 0
                        ? bucket.Count / (double)summary.TotalDefects * 100
                        : 0
                })
                .ToList(),
            AverageConfidence = summary.AverageConfidence
        };
    }

    private static InspectionConfidenceSummary BuildConfidenceSummary(IEnumerable<Defect> defects)
    {
        var counts = new int[6];
        var total = 0;
        var confidenceTotal = 0d;
        foreach (var defect in defects)
        {
            var score = defect.ConfidenceScore;
            var bucket = score >= 0.9d ? 0
                : score >= 0.8d ? 1
                : score >= 0.7d ? 2
                : score >= 0.6d ? 3
                : score >= 0.5d ? 4
                : 5;
            counts[bucket]++;
            total++;
            confidenceTotal += score;
        }

        return new InspectionConfidenceSummary(
            counts[0], counts[1], counts[2], counts[3], counts[4], counts[5],
            total,
            total == 0 ? 0 : confidenceTotal / total);
    }

    private static int FindBucketIndex(IReadOnlyList<DateTime> bucketStarts, DateTime timestamp)
    {
        if (bucketStarts.Count == 0 || timestamp < bucketStarts[0])
        {
            return -1;
        }

        var low = 0;
        var high = bucketStarts.Count - 1;
        while (low <= high)
        {
            var middle = low + (high - low) / 2;
            if (bucketStarts[middle] <= timestamp)
            {
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        return high;
    }

    private sealed class TrendBucketAccumulator
    {
        private int _totalCount;
        private int _executionSucceededCount;
        private int _okCount;
        private int _ngCount;
        private int _undeterminedCount;
        private int _invalidCount;
        private int _failedCount;
        private int _timedOutCount;
        private int _defectCount;
        private double _processingTimeTotal;

        public void Add(InspectionAnalysisSample sample)
        {
            _totalCount++;
            _defectCount += sample.DefectCount;
            _processingTimeTotal += sample.ProcessingTimeMs;

            var outcome = sample.ToOutcome();
            if (outcome.Execution == ExecutionOutcome.Succeeded)
            {
                _executionSucceededCount++;
            }

            switch (InspectionOutcomeClassifier.Classify(outcome))
            {
                case CanonicalInspectionOutcomeKind.Ok:
                    _okCount++;
                    break;
                case CanonicalInspectionOutcomeKind.Ng:
                    _ngCount++;
                    break;
                case CanonicalInspectionOutcomeKind.Undetermined:
                    _undeterminedCount++;
                    break;
                case CanonicalInspectionOutcomeKind.Invalid:
                    _invalidCount++;
                    break;
                case CanonicalInspectionOutcomeKind.Failed:
                    _failedCount++;
                    break;
                case CanonicalInspectionOutcomeKind.TimedOut:
                    _timedOutCount++;
                    break;
            }
        }

        public TrendDataPointDto ToDataPoint(DateTime timestamp)
        {
            var validDecisionCount = _okCount + _ngCount;
            var executionFailureCount = _failedCount + _timedOutCount;
            var yieldRate = validDecisionCount > 0 ? _okCount / (double)validDecisionCount : 0;
            return new TrendDataPointDto
            {
                Timestamp = timestamp,
                TotalCount = _totalCount,
                OKCount = _okCount,
                NGCount = _ngCount,
                ErrorCount = executionFailureCount + _invalidCount,
                OKRate = yieldRate,
                YieldRate = yieldRate,
                ValidDecisionCount = validDecisionCount,
                ExecutionFailureCount = executionFailureCount,
                UndeterminedCount = _undeterminedCount,
                InvalidCount = _invalidCount,
                DefectCount = _defectCount,
                AverageProcessingTime = _totalCount == 0 ? 0 : _processingTimeTotal / _totalCount
            };
        }
    }

    private List<string> GenerateRecommendations(InspectionStatisticsDto statistics, DefectDistributionDto defectDistribution)
    {
        var recommendations = new List<string>();

        if (statistics.OKRate < 0.8)
        {
            recommendations.Add($"OK率较低({statistics.OKRate:P1})，建议检查检测参数或光源配置");
        }

        if (statistics.AverageProcessingTimeMs > 500)
        {
            recommendations.Add($"平均处理时间较长({statistics.AverageProcessingTimeMs:F0}ms)，建议优化算子流程或降低图像分辨率");
        }

        if (defectDistribution.Items.Any())
        {
            var topDefect = defectDistribution.Items.First();
            recommendations.Add($"主要缺陷类型为{topDefect.DefectType}（{topDefect.Percentage:F1}%），建议重点关注");
        }

        if (statistics.ErrorRate > 0.05)
        {
            recommendations.Add($"错误率较高({statistics.ErrorRate:P1})，建议检查硬件连接和软件稳定性");
        }

        return recommendations;
    }

    private async Task<bool> IsActiveProjectAsync(Guid projectId)
    {
        if (_projectRepository == null)
        {
            return true;
        }

        return projectId != Guid.Empty && await _projectRepository.GetByIdFreshAsync(projectId) != null;
    }

    private static InspectionStatisticsDto EmptyStatistics(Guid projectId, DateTime? startTime, DateTime? endTime) =>
        new()
        {
            ProjectId = projectId,
            StartTime = startTime,
            EndTime = endTime
        };

    private static DefectDistributionDto EmptyDefectDistribution(Guid projectId, DateTime? startTime, DateTime? endTime) =>
        new()
        {
            ProjectId = projectId,
            StartTime = startTime,
            EndTime = endTime
        };

    private static ConfidenceDistributionDto EmptyConfidenceDistribution(Guid projectId, DateTime? startTime, DateTime? endTime) =>
        new()
        {
            ProjectId = projectId,
            StartTime = startTime,
            EndTime = endTime
        };

    private static TrendAnalysisDto EmptyTrend(Guid projectId, TrendInterval interval, DateTime startTime, DateTime endTime) =>
        new()
        {
            ProjectId = projectId,
            Interval = interval.ToString(),
            StartTime = startTime,
            EndTime = endTime
        };

    private static InspectionReportDto EmptyReport(Guid projectId, DateTime? startTime, DateTime? endTime) =>
        new()
        {
            ProjectId = projectId,
            GeneratedAt = DateTime.UtcNow,
            Period = new ReportPeriodDto { StartTime = startTime, EndTime = endTime },
            Summary = EmptyStatistics(projectId, startTime, endTime),
            DefectDistribution = EmptyDefectDistribution(projectId, startTime, endTime),
            ConfidenceDistribution = EmptyConfidenceDistribution(projectId, startTime, endTime),
            HourlyTrend = EmptyTrend(projectId, TrendInterval.Hour, startTime ?? DateTime.MinValue, endTime ?? DateTime.UtcNow)
        };

    private string GenerateComparisonSummary(List<MetricComparisonDto> comparisons)
    {
        var improvements = comparisons.Count(c => c.IsPositive);
        var total = comparisons.Count;

        return $"在{total}项指标中，有{improvements}项改善，{total - improvements}项下降";
    }

    private double CalculateChange(double oldValue, double newValue)
    {
        if (oldValue == 0)
            return newValue > 0 ? 100 : 0;
        return (newValue - oldValue) / oldValue * 100;
    }

    #endregion
}

/// <summary>
/// 时间间隔枚举
/// </summary>
public enum TrendInterval
{
    Hour,
    Day,
    Week,
    Month
}
