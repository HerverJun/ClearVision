// InspectionResult.cs
// 图像标注数据（JSON格式）
// 作者：蘅芜君

using System.Text.Json.Serialization;
using ClearVision.Product.Core.Entities.Base;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Outcomes;

namespace ClearVision.Product.Core.Entities;

/// <summary>
/// 检测结果实体
/// </summary>
public class InspectionResult : Entity
{
    /// <summary>
    /// 所属工程ID
    /// </summary>
    public Guid ProjectId { get; private set; }

    /// <summary>
    /// 检测状态
    /// </summary>
    public InspectionStatus Status { get; private set; }

    public ExecutionOutcome? ExecutionOutcome { get; private set; }

    public DecisionOutcome? DecisionOutcome { get; private set; }

    public string? DecisionSource { get; private set; }

    public string? ReasonCode { get; private set; }

    /// <summary>
    /// 缺陷列表
    /// </summary>
    private readonly List<Defect> _defects = [];
    public IReadOnlyCollection<Defect> Defects => _defects.AsReadOnly();

    /// <summary>
    /// 处理时间（毫秒）
    /// </summary>
    [JsonInclude]
    public long ProcessingTimeMs { get; private set; }

    /// <summary>
    /// 图像ID
    /// </summary>
    public Guid? ImageId { get; private set; }

    /// <summary>
    /// 置信度分数（0-1）
    /// </summary>
    public double? ConfidenceScore { get; private set; }

    /// <summary>
    /// 错误信息
    /// </summary>
    public string? ErrorMessage { get; private set; }

    /// <summary>
    /// 输出图像数据（处理后的图像）
    /// </summary>
    [JsonInclude]
    public byte[]? OutputImage { get; private set; }

    /// <summary>
    /// 检测时间戳
    /// </summary>
    public DateTime InspectionTime { get; private set; }

    /// <summary>
    /// 其他输出数据（JSON格式存储，如文本、提取的数值等）
    /// </summary>
    public string? OutputDataJson { get; private set; }

    /// <summary>
    /// 显式分析语义数据（JSON 格式存储）
    /// </summary>
    public string? AnalysisDataJson { get; private set; }

    public string? FlowVersionHash { get; private set; }

    public string? CalibrationBundleId { get; private set; }

    public Guid? SessionId { get; private set; }

    private InspectionResult()
    {
        Status = InspectionStatus.NotInspected;
        InspectionTime = DateTime.UtcNow;
    }

    public InspectionResult(Guid projectId, Guid? imageId = null) : this()
    {
        ProjectId = projectId;
        ImageId = imageId;
    }

    /// <summary>
    /// 添加缺陷
    /// </summary>
    public void AddDefect(Defect defect)
    {
        if (defect == null)
            throw new ArgumentNullException(nameof(defect));

        _defects.Add(defect);
        MarkAsModified();
    }

    /// <summary>
    /// 设置检测结果
    /// </summary>
    public void SetResult(InspectionStatus status, long processingTimeMs, double? confidenceScore = null, string? errorMessage = null)
    {
        var legacyOutcome = LegacyInspectionStatusProjection.FromLegacy(status) with
        {
            Message = errorMessage
        };
        SetOutcome(legacyOutcome, processingTimeMs, confidenceScore);
    }

    public void SetOutcome(
        InspectionOutcome outcome,
        long processingTimeMs,
        double? confidenceScore = null)
    {
        ExecutionOutcome = outcome.Execution;
        DecisionOutcome = outcome.Decision;
        DecisionSource = NormalizeOptional(outcome.DecisionSource);
        ReasonCode = NormalizeOptional(outcome.ReasonCode);
        Status = LegacyInspectionStatusProjection.Project(outcome);
        ProcessingTimeMs = processingTimeMs;
        ConfidenceScore = confidenceScore;
        ErrorMessage = NormalizeOptional(outcome.Message);
        MarkAsModified();
    }

    /// <summary>
    /// 标记检测错误
    /// </summary>
    public void MarkAsError(string errorMessage)
    {
        SetOutcome(
            new InspectionOutcome(
                ClearVision.Product.Core.Outcomes.ExecutionOutcome.Failed,
                ClearVision.Product.Core.Outcomes.DecisionOutcome.Undetermined,
                "InspectionResult",
                "MarkedAsError",
                errorMessage),
            ProcessingTimeMs,
            ConfidenceScore);
    }

    public InspectionOutcome GetOutcome()
    {
        if (ExecutionOutcome.HasValue && DecisionOutcome.HasValue)
        {
            return new InspectionOutcome(
                ExecutionOutcome.Value,
                DecisionOutcome.Value,
                DecisionSource,
                ReasonCode,
                ErrorMessage);
        }

        return LegacyInspectionStatusProjection.FromLegacy(Status) with { Message = ErrorMessage };
    }

    /// <summary>
    /// 获取NG数量
    /// </summary>
    public int GetNGCount() => _defects.Count;

    /// <summary>
    /// 是否合格
    /// </summary>
    public bool IsOK => Status == InspectionStatus.OK && _defects.Count == 0;

    /// <summary>
    /// 设置输出图像
    /// </summary>
    public void SetOutputImage(byte[] imageData)
    {
        OutputImage = imageData;
        MarkAsModified();
    }

    /// <summary>
    /// 设置图像缓存引用
    /// </summary>
    public void SetImageId(Guid? imageId)
    {
        ImageId = imageId;
        MarkAsModified();
    }

    /// <summary>
    /// 设置算子额外输出数据（JSON序列化后）
    /// </summary>
    public void SetOutputDataJson(string json)
    {
        OutputDataJson = json;
        MarkAsModified();
    }

    /// <summary>
    /// 设置显式分析语义数据（JSON 序列化后）
    /// </summary>
    public void SetAnalysisDataJson(string json)
    {
        AnalysisDataJson = json;
        MarkAsModified();
    }

    public void SetTraceability(string? flowVersionHash, string? calibrationBundleId, Guid? sessionId)
    {
        FlowVersionHash = string.IsNullOrWhiteSpace(flowVersionHash) ? null : flowVersionHash.Trim();
        CalibrationBundleId = string.IsNullOrWhiteSpace(calibrationBundleId) ? null : calibrationBundleId.Trim();
        SessionId = sessionId;
        MarkAsModified();
    }

    public void RestorePersistenceMetadata(
        Guid id,
        DateTime inspectionTime,
        DateTime createdAt,
        DateTime? modifiedAt)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Inspection result id cannot be empty.", nameof(id));
        }

        if (inspectionTime == default)
        {
            throw new ArgumentException("Inspection time cannot be default.", nameof(inspectionTime));
        }

        if (createdAt == default)
        {
            throw new ArgumentException("Created time cannot be default.", nameof(createdAt));
        }

        Id = id;
        InspectionTime = inspectionTime;
        CreatedAt = createdAt;
        ModifiedAt = modifiedAt;
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>
/// 缺陷实体
/// </summary>
public class Defect : Entity
{
    /// <summary>
    /// 所属检测结果ID
    /// </summary>
    public Guid InspectionResultId { get; private set; }

    /// <summary>
    /// 缺陷类型
    /// </summary>
    public DefectType Type { get; private set; }

    /// <summary>
    /// 缺陷位置（X坐标）
    /// </summary>
    public double X { get; private set; }

    /// <summary>
    /// 缺陷位置（Y坐标）
    /// </summary>
    public double Y { get; private set; }

    /// <summary>
    /// 缺陷宽度
    /// </summary>
    public double Width { get; private set; }

    /// <summary>
    /// 缺陷高度
    /// </summary>
    public double Height { get; private set; }

    /// <summary>
    /// 置信度分数（0-1）
    /// </summary>
    public double ConfidenceScore { get; private set; }

    /// <summary>
    /// 缺陷描述
    /// </summary>
    public string? Description { get; private set; }

    /// <summary>
    /// 图像标注数据（JSON格式）
    /// </summary>
    public string? AnnotationData { get; private set; }

    private Defect()
    {
    }

    public Defect(
        Guid inspectionResultId,
        DefectType type,
        double x,
        double y,
        double width,
        double height,
        double confidenceScore,
        string? description = null,
        string? annotationData = null)
    {
        InspectionResultId = inspectionResultId;
        Type = type;
        X = x;
        Y = y;
        Width = width;
        Height = height;
        ConfidenceScore = confidenceScore;
        Description = description;
        AnnotationData = annotationData;
    }
}
