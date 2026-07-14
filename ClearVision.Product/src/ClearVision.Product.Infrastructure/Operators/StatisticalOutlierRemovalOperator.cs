using ClearVision.Product.Core.Attributes;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Operators;
using ClearVision.Product.Infrastructure.PointCloud.Filters;
using Microsoft.Extensions.Logging;
using PointCloudModel = ClearVision.Product.Infrastructure.PointCloud.PointCloud;

namespace ClearVision.Product.Infrastructure.Operators;

[OperatorMeta(
    DisplayName = "点云统计离群点去除（SOR）",
    Description = "对点云执行统计离群点去除（SOR），输出过滤点云、保留点数和移除点数。",
    CategoryId = OperatorCategoryId.PointCloud3D,
    IconName = "filter",
    Keywords = new[] { "PointCloud", "Filter", "Outlier", "SOR", "3D", "统计滤波" },
    Version = "1.0.1"
)]
[InputPort("PointCloud", "Point Cloud", PortDataType.Any, IsRequired = true)]
[OutputPort("PointCloud", "Point Cloud", PortDataType.Any)]
[OutputPort("PointCount", "Point Count", PortDataType.Integer)]
[OutputPort("RemovedCount", "Removed Count", PortDataType.Integer)]
[OperatorParam("MeanK", "MeanK", "int", DefaultValue = 50, Min = 1, Max = 500)]
[OperatorParam("StddevMul", "StddevMul", "double", DefaultValue = 1.0, Min = 0.0, Max = 10.0)]
public sealed class StatisticalOutlierRemovalOperator : OperatorBase
{
    public override OperatorType OperatorType => OperatorType.StatisticalOutlierRemoval;

    public StatisticalOutlierRemovalOperator(ILogger<StatisticalOutlierRemovalOperator> logger) : base(logger)
    {
    }

    protected override async Task<OperatorExecutionOutput> ExecuteCoreAsync(
        Operator @operator,
        Dictionary<string, object>? inputs,
        CancellationToken cancellationToken)
    {
        if (inputs == null || !inputs.TryGetValue("PointCloud", out var cloudObj) || cloudObj is null)
        {
            return OperatorExecutionOutput.Failure("PointCloud input is required.");
        }

        if (cloudObj is not PointCloudModel cloud)
        {
            return OperatorExecutionOutput.Failure($"PointCloud input must be {nameof(PointCloudModel)}.");
        }

        var meanK = GetIntParam(@operator, "MeanK", 50, min: 1, max: 500);
        var stddevMul = GetDoubleParam(@operator, "StddevMul", 1.0, min: 0.0, max: 10.0);

        var filter = new StatisticalOutlierRemoval();
        PointCloudModel filtered;

        try
        {
            filtered = await RunCpuBoundWork(
                () => filter.Filter(cloud, meanK: meanK, stddevMul: stddevMul),
                cancellationToken);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Statistical outlier removal failed.");
            return OperatorExecutionOutput.Failure($"Statistical outlier removal failed: {ex.Message}");
        }

        return OperatorExecutionOutput.Success(new Dictionary<string, object>
        {
            ["PointCloud"] = filtered,
            ["PointCount"] = filtered.Count,
            ["RemovedCount"] = Math.Max(0, cloud.Count - filtered.Count),
            ["MeanK"] = meanK,
            ["StddevMul"] = stddevMul
        });
    }

    public override ValidationResult ValidateParameters(Operator @operator)
    {
        _ = GetIntParam(@operator, "MeanK", 50, min: 1, max: 500);
        _ = GetDoubleParam(@operator, "StddevMul", 1.0, min: 0.0, max: 10.0);
        return ValidationResult.Valid();
    }
}

