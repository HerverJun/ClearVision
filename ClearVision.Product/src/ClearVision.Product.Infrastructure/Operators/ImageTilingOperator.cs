// ImageTilingOperator.cs
// 图像切片算子
// 将图像按网格切分为多个子图块输出
// 作者：蘅芜君
using ClearVision.Product.Core.Attributes;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Operators;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
namespace ClearVision.Product.Infrastructure.Operators;

[OperatorMeta(
    DisplayName = "图像切片",
    Description = "将图像切分为可选重叠的分块区域。",
    CategoryId = OperatorCategoryId.ImagePreprocessing,
    IconName = "tile",
    Keywords = new[] { "tile", "grid", "split image" }
)]
[InputPort("Image", "Image", PortDataType.Image, IsRequired = true)]
[OutputPort("Tiles", "Tiles", PortDataType.Any)]
[OutputPort("Count", "Count", PortDataType.Integer)]
[OutputPort("Image", "Image", PortDataType.Image)]
[OperatorParam("Rows", "Rows", "int", DefaultValue = 2, Min = 1, Max = 100)]
[OperatorParam("Cols", "Cols", "int", DefaultValue = 2, Min = 1, Max = 100)]
[OperatorParam("Overlap", "Overlap", "int", DefaultValue = 0, Min = 0, Max = 10000)]
[OperatorParam("OutputMode", "Output Mode", "enum", DefaultValue = "Array", Options = new[] { "Array|Array", "Sequential|Sequential" })]
public class ImageTilingOperator : OperatorBase
{
    public override OperatorType OperatorType => OperatorType.ImageTiling;

    public ImageTilingOperator(ILogger<ImageTilingOperator> logger) : base(logger)
    {
    }

    protected override Task<OperatorExecutionOutput> ExecuteCoreAsync(
        Operator @operator,
        Dictionary<string, object>? inputs,
        CancellationToken cancellationToken)
    {
        if (!TryGetInputImage(inputs, out var imageWrapper) || imageWrapper == null)
        {
            return Task.FromResult(OperatorExecutionOutput.Failure("Input image is required"));
        }

        var src = imageWrapper.GetMat();
        if (src.Empty())
        {
            return Task.FromResult(OperatorExecutionOutput.Failure("Input image is invalid"));
        }

        var rows = GetIntParam(@operator, "Rows", 2, 1, 100);
        var cols = GetIntParam(@operator, "Cols", 2, 1, 100);
        var overlap = GetIntParam(@operator, "Overlap", 0, 0, 10000);

        var tileW = Math.Max(1, src.Width / cols);
        var tileH = Math.Max(1, src.Height / rows);
        var tiles = new List<ImageWrapper>();

        var annotated = src.Clone();
        for (var r = 0; r < rows; r++)
        {
            for (var c = 0; c < cols; c++)
            {
                var x = c * tileW;
                var y = r * tileH;
                var w = c == cols - 1 ? src.Width - x : tileW;
                var h = r == rows - 1 ? src.Height - y : tileH;

                var roiX = Math.Max(0, x - overlap);
                var roiY = Math.Max(0, y - overlap);
                var roiW = Math.Min(src.Width - roiX, w + overlap * 2);
                var roiH = Math.Min(src.Height - roiY, h + overlap * 2);
                var roi = new Rect(roiX, roiY, roiW, roiH);

                using var tileMat = new Mat(src, roi);
                tiles.Add(new ImageWrapper(tileMat.Clone()));

                Cv2.Rectangle(annotated, new Rect(x, y, w, h), new Scalar(0, 255, 255), 1);
            }
        }

        var output = new Dictionary<string, object>
        {
            { "Tiles", tiles },
            { "Count", tiles.Count }
        };

        return Task.FromResult(OperatorExecutionOutput.Success(CreateImageOutput(annotated, output)));
    }

    public override ValidationResult ValidateParameters(Operator @operator)
    {
        var rows = GetIntParam(@operator, "Rows", 2);
        var cols = GetIntParam(@operator, "Cols", 2);
        if (rows <= 0 || cols <= 0)
        {
            return ValidationResult.Invalid("Rows and Cols must be greater than 0");
        }

        var overlap = GetIntParam(@operator, "Overlap", 0);
        if (overlap < 0)
        {
            return ValidationResult.Invalid("Overlap must be >= 0");
        }

        return ValidationResult.Valid();
    }
}
