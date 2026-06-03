// IInspectionResultRepository.cs
// 检测统计信息
// 作者：蘅芜君

using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;

namespace ClearVision.Product.Core.Interfaces;

/// <summary>
/// 检测结果仓储接口
/// </summary>
public interface IInspectionResultRepository : IRepository<InspectionResult>
{
    /// <summary>
    /// Batch insert inspection results with a single unit-of-work save.
    /// </summary>
    Task AddRangeAsync(IEnumerable<InspectionResult> results);

    /// <summary>
    /// 根据工程ID获取结果列表
    /// </summary>
    Task<IEnumerable<InspectionResult>> GetByProjectIdAsync(Guid projectId, int pageIndex = 0, int pageSize = 20);

    /// <summary>
    /// 获取统一分页语义的检测历史记录。
    /// </summary>
    Task<InspectionHistoryPage> GetHistoryPageAsync(
        Guid projectId,
        DateTime? startTime = null,
        DateTime? endTime = null,
        string? status = null,
        string? defectType = null,
        int pageIndex = 0,
        int pageSize = 20);

    /// <summary>
    /// 根据时间范围获取结果
    /// </summary>
    Task<IEnumerable<InspectionResult>> GetByTimeRangeAsync(
        Guid projectId,
        DateTime startTime,
        DateTime endTime,
        string? status = null,
        string? defectType = null);

    /// <summary>
    /// 获取统计信息
    /// </summary>
    Task<InspectionStatistics> GetStatisticsAsync(
        Guid projectId,
        DateTime? startTime = null,
        DateTime? endTime = null,
        string? status = null,
        string? defectType = null);

    /// <summary>
    /// 获取缺陷分布统计
    /// </summary>
    Task<Dictionary<Enums.DefectType, int>> GetDefectDistributionAsync(
        Guid projectId,
        DateTime? startTime = null,
        DateTime? endTime = null,
        string? status = null,
        string? defectType = null);
}

/// <summary>
/// 检测统计信息
/// </summary>
public class InspectionStatistics
{
    public int TotalCount { get; set; }
    public int OKCount { get; set; }
    public int NGCount { get; set; }
    public int ErrorCount { get; set; }
    public double OKRate { get; set; }
    public double AverageProcessingTimeMs { get; set; }
}

public class InspectionHistoryPage
{
    public IReadOnlyList<InspectionHistoryItem> Items { get; set; } = Array.Empty<InspectionHistoryItem>();
    public int TotalCount { get; set; }
    public int PageIndex { get; set; }
    public int PageSize { get; set; }
}

public class InspectionHistoryItem
{
    public Guid Id { get; set; }
    public Guid ProjectId { get; set; }
    public InspectionStatus Status { get; set; }
    public IReadOnlyList<InspectionHistoryDefectItem> Defects { get; set; } = Array.Empty<InspectionHistoryDefectItem>();
    public long ProcessingTimeMs { get; set; }
    public Guid? ImageId { get; set; }
    public double? ConfidenceScore { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime InspectionTime { get; set; }
    public string? OutputDataJson { get; set; }
    public string? AnalysisDataJson { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ModifiedAt { get; set; }
}

public class InspectionHistoryDefectItem
{
    public Guid Id { get; set; }
    public DefectType Type { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public double ConfidenceScore { get; set; }
    public string? Description { get; set; }
    public string? AnnotationData { get; set; }
}
