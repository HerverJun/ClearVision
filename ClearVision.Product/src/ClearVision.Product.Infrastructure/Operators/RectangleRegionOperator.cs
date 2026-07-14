using ClearVision.Product.Core.Attributes;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Operators;
using Microsoft.Extensions.Logging;

namespace ClearVision.Product.Infrastructure.Operators;

[OperatorMeta(
    DisplayName = "矩形框定义",
    Description = "根据 X、Y、宽度和高度参数生成 Rectangle 矩形框，供需要 Rectangle 输入的算子使用。",
    CategoryId = OperatorCategoryId.SegmentationAndRegion,
    IconName = "rectangle-region",
    Keywords = new[] { "rectangle", "region", "search region", "caliper", "矩形区域" },
    Version = "1.0.1"
)]
[OutputPort("Rectangle", "Rectangle", PortDataType.Rectangle)]
[OperatorParam("X", "X", "int", DefaultValue = 0, Min = 0)]
[OperatorParam("Y", "Y", "int", DefaultValue = 0, Min = 0)]
[OperatorParam("Width", "Width", "int", DefaultValue = 1, Min = 1)]
[OperatorParam("Height", "Height", "int", DefaultValue = 1, Min = 1)]
public sealed class RectangleRegionOperator : OperatorBase
{
    public override OperatorType OperatorType => OperatorType.RectangleRegion;

    public RectangleRegionOperator(ILogger<RectangleRegionOperator> logger) : base(logger)
    {
    }

    protected override Task<OperatorExecutionOutput> ExecuteCoreAsync(
        Operator @operator,
        Dictionary<string, object>? inputs,
        CancellationToken cancellationToken)
    {
        var x = GetIntParam(@operator, "X", 0, min: 0);
        var y = GetIntParam(@operator, "Y", 0, min: 0);
        var width = GetIntParam(@operator, "Width", 1, min: 1);
        var height = GetIntParam(@operator, "Height", 1, min: 1);

        return Task.FromResult(OperatorExecutionOutput.Success(new Dictionary<string, object>
        {
            ["Rectangle"] = new Dictionary<string, object>
            {
                ["X"] = x,
                ["Y"] = y,
                ["Width"] = width,
                ["Height"] = height
            }
        }));
    }

    public override ValidationResult ValidateParameters(Operator @operator)
    {
        var width = GetIntParam(@operator, "Width", 1);
        var height = GetIntParam(@operator, "Height", 1);
        if (width < 1 || height < 1)
        {
            return ValidationResult.Invalid("Width and Height must be greater than zero.");
        }

        return ValidationResult.Valid();
    }
}
