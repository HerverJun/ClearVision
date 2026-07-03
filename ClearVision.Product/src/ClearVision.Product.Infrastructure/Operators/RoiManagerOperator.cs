// RoiManagerOperator.cs
// ROI管理器算子 - 矩形// 功能实现圆形// 功能实现多边形区域选择
// 作者：蘅芜君

using System.Text.Json;
using ClearVision.Product.Core.Attributes;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Operators;
using ClearVision.Product.Infrastructure.Calibration;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
namespace ClearVision.Product.Infrastructure.Operators;

/// <summary>
/// ROI管理器算子 - 矩形/圆形/多边形区域选择
/// </summary>
[OperatorMeta(
    DisplayName = "ROI管理器",
    Description = "矩形/圆形/多边形区域选择",
    Category = "辅助",
    IconName = "roi",
    Keywords = new[] { "ROI", "区域", "感兴趣区", "掩膜", "选区", "Region", "Mask", "Area of interest" }
)]
[InputPort("Image", "输入图像", PortDataType.Image, IsRequired = true)]
[OutputPort("Image", "ROI图像", PortDataType.Image)]
[OutputPort("Mask", "掩膜", PortDataType.Image)]
[OutputPort("SpatialContext", "空间上下文", PortDataType.Any)]
[OperatorParam("Shape", "形状", "enum", DefaultValue = "Rectangle", Options = new[] { "Rectangle|矩形", "Circle|圆形", "Polygon|多边形" })]
[OperatorParam("Operation", "操作", "enum", DefaultValue = "Crop", Options = new[] { "Crop|裁剪", "Mask|掩膜" })]
[OperatorParam("X", "X", "int", DefaultValue = 0, Min = 0)]
[OperatorParam("Y", "Y", "int", DefaultValue = 0, Min = 0)]
[OperatorParam("Width", "宽度", "int", DefaultValue = 200, Min = 1)]
[OperatorParam("Height", "高度", "int", DefaultValue = 200, Min = 1)]
[OperatorParam("CenterX", "圆心X", "int", DefaultValue = 100)]
[OperatorParam("CenterY", "圆心Y", "int", DefaultValue = 100)]
[OperatorParam("Radius", "半径", "int", DefaultValue = 50, Min = 1)]
[OperatorParam("PolygonPoints", "多边形顶点(JSON)", "string", DefaultValue = "[[10,10],[200,10],[200,200],[10,200]]")]
public class RoiManagerOperator : OperatorBase
{
    public const string SpatialContextOutputKey = "SpatialContext";
    public const string MaskSpatialContextOutputKey = "MaskSpatialContext";
    public const string ImageSpatialContextInputKey = "ImageSpatialContext";

    private static readonly JsonSerializerOptions SpatialJsonOptions = new(JsonSerializerDefaults.Web);

    public override OperatorType OperatorType => OperatorType.RoiManager;

    public RoiManagerOperator(ILogger<RoiManagerOperator> logger) : base(logger)
    {
    }

    protected override Task<OperatorExecutionOutput> ExecuteCoreAsync(
        Operator @operator,
        Dictionary<string, object>? inputs,
        CancellationToken cancellationToken)
    {
        // 1. 获取图像输入
        if (!TryGetInputImage(inputs, "Image", out var imageWrapper) || imageWrapper == null)
        {
            return Task.FromResult(OperatorExecutionOutput.Failure("未提供输入图像"));
        }

        // 2. 获取参数
        var shape = GetStringParam(@operator, "Shape", "Rectangle");
        var operation = GetStringParam(@operator, "Operation", "Crop");
        var x = GetIntParam(@operator, "X", 0, min: 0);
        var y = GetIntParam(@operator, "Y", 0, min: 0);
        var width = GetIntParam(@operator, "Width", 200, min: 1);
        var height = GetIntParam(@operator, "Height", 200, min: 1);
        var centerX = GetIntParam(@operator, "CenterX", 100);
        var centerY = GetIntParam(@operator, "CenterY", 100);
        var radius = GetIntParam(@operator, "Radius", 50, min: 1);
        var polygonPoints = GetStringParam(@operator, "PolygonPoints", "[[10,10],[200,10],[200,200],[10,200]]");

        // 3. 获取 Mat
        var src = imageWrapper.GetMat();
        if (src.Empty())
        {
            return Task.FromResult(OperatorExecutionOutput.Failure("无法解码输入图像"));
        }

        // 边界检查
        width = Math.Min(width, src.Width - x);
        height = Math.Min(height, src.Height - y);
        if (width <= 0 || height <= 0)
        {
            return Task.FromResult(OperatorExecutionOutput.Failure("ROI区域超出图像边界"));
        }

        var inputSpatialContext = ResolveInputSpatialContext(inputs);
        Mat resultImage;
        Mat mask = new Mat(src.Size(), MatType.CV_8UC1, Scalar.All(0));
        Rect outputBounds;

        try
        {
            switch (shape)
            {
                case "Rectangle":
                    ProcessRectangle(src, out resultImage, mask, operation, x, y, width, height, out outputBounds);
                    break;
                case "Circle":
                    ProcessCircle(src, out resultImage, mask, operation, centerX, centerY, radius, out outputBounds);
                    break;
                case "Polygon":
                    ProcessPolygon(src, out resultImage, mask, operation, polygonPoints, out outputBounds);
                    break;
                default:
                    ProcessRectangle(src, out resultImage, mask, operation, x, y, width, height, out outputBounds);
                    break;
            }

            var additionalData = new Dictionary<string, object>
            {
                { "Shape", shape },
                { "Operation", operation },
                { "ParentWidth", src.Width },
                { "ParentHeight", src.Height },
                { "Mask", new ImageWrapper(mask.Clone()) },
                { SpatialContextOutputKey, BuildImageSpatialContext(inputSpatialContext, @operator, operation, outputBounds) },
                { MaskSpatialContextOutputKey, BuildPassThroughSpatialContext(inputSpatialContext, @operator, "Mask") }
            };

            return Task.FromResult(OperatorExecutionOutput.Success(CreateImageOutput(resultImage, additionalData)));
        }
        finally
        {
            mask.Dispose();
        }
    }

    private void ProcessRectangle(
        Mat src,
        out Mat resultImage,
        Mat mask,
        string operation,
        int x,
        int y,
        int width,
        int height,
        out Rect outputBounds)
    {
        var rect = new Rect(x, y, width, height);

        if (operation == "Crop")
        {
            // 裁剪模式
            resultImage = new Mat(src, rect);
            outputBounds = rect;
            // 创建完整尺寸的掩膜
            Cv2.Rectangle(mask, rect, Scalar.All(255), -1);
        }
        else
        {
            // 掩膜模式 - 保留原图，应用掩膜
            resultImage = src.Clone();
            outputBounds = new Rect(0, 0, src.Width, src.Height);
            Cv2.Rectangle(mask, rect, Scalar.All(255), -1);
            Cv2.BitwiseAnd(src, src, resultImage, mask);
        }
    }

    private void ProcessCircle(
        Mat src,
        out Mat resultImage,
        Mat mask,
        string operation,
        int centerX,
        int centerY,
        int radius,
        out Rect outputBounds)
    {
        var center = new Point(centerX, centerY);

        // 计算外接矩形
        var rectX = Math.Max(0, centerX - radius);
        var rectY = Math.Max(0, centerY - radius);
        var rectWidth = Math.Min(radius * 2, src.Width - rectX);
        var rectHeight = Math.Min(radius * 2, src.Height - rectY);

        // 在掩膜上绘制圆形
        Cv2.Circle(mask, center, radius, Scalar.All(255), -1);

        if (operation == "Crop")
        {
            // 裁剪模式 - 裁剪外接矩形并应用圆形掩膜
            var rect = new Rect(rectX, rectY, rectWidth, rectHeight);
            outputBounds = rect;
            using var cropped = new Mat(src, rect);
            using var croppedMask = new Mat(mask, rect);
            resultImage = new Mat(cropped.Size(), src.Type(), Scalar.All(0));
            Cv2.BitwiseAnd(cropped, cropped, resultImage, croppedMask);
        }
        else
        {
            // 掩膜模式
            resultImage = src.Clone();
            outputBounds = new Rect(0, 0, src.Width, src.Height);
            Cv2.BitwiseAnd(src, src, resultImage, mask);
        }
    }

    private void ProcessPolygon(
        Mat src,
        out Mat resultImage,
        Mat mask,
        string operation,
        string polygonPointsJson,
        out Rect outputBounds)
    {
        Point[][]? points = null;
        try
        {
            var pointArrays = JsonSerializer.Deserialize<int[][]>(polygonPointsJson);
            if (pointArrays != null && pointArrays.Length >= 3)
            {
                points = new[] { pointArrays.Select(p => new Point(p[0], p[1])).ToArray() };
            }
        }
        catch
        {
            points = null;
        }

        if (points == null)
        {
            // 解析失败，使用默认矩形
            points = new[] { new[] { new Point(10, 10), new Point(200, 10), new Point(200, 200), new Point(10, 200) } };
        }

        // 在掩膜上填充多边形
        Cv2.FillPoly(mask, points, Scalar.All(255));

        if (operation == "Crop")
        {
            // 裁剪模式 - 计算多边形外接矩形
            var allPoints = points[0];
            var minX = allPoints.Min(p => p.X);
            var minY = allPoints.Min(p => p.Y);
            var maxX = allPoints.Max(p => p.X);
            var maxY = allPoints.Max(p => p.Y);

            minX = Math.Max(0, minX);
            minY = Math.Max(0, minY);
            maxX = Math.Min(src.Width, maxX);
            maxY = Math.Min(src.Height, maxY);

            var rect = new Rect(minX, minY, maxX - minX, maxY - minY);
            outputBounds = rect;
            using var cropped = new Mat(src, rect);
            using var croppedMask = new Mat(mask, rect);
            resultImage = new Mat(cropped.Size(), src.Type(), Scalar.All(0));
            Cv2.BitwiseAnd(cropped, cropped, resultImage, croppedMask);
        }
        else
        {
            // 掩膜模式
            resultImage = src.Clone();
            outputBounds = new Rect(0, 0, src.Width, src.Height);
            Cv2.BitwiseAnd(src, src, resultImage, mask);
        }
    }

    private static SpatialContextV1 ResolveInputSpatialContext(Dictionary<string, object>? inputs)
    {
        if (inputs != null &&
            TryGetDictionaryValue(inputs, ImageSpatialContextInputKey, out var imageScopedContext) &&
            TryReadSpatialContext(imageScopedContext, out var scopedContext))
        {
            return scopedContext;
        }

        if (inputs != null &&
            TryGetDictionaryValue(inputs, SpatialContextOutputKey, out var rawContext) &&
            TryReadSpatialContext(rawContext, out var context))
        {
            return context;
        }

        return SpatialContextV1.DefaultImageFull();
    }

    private static bool TryGetDictionaryValue(
        IDictionary<string, object> dictionary,
        string key,
        out object? value)
    {
        foreach (var pair in dictionary)
        {
            if (pair.Key.Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                value = pair.Value;
                return true;
            }
        }

        value = null;
        return false;
    }

    private static bool TryReadSpatialContext(object? raw, out SpatialContextV1 context)
    {
        context = SpatialContextV1.DefaultImageFull();
        switch (raw)
        {
            case SpatialContextV1 typed:
                context = typed;
                return true;
            case JsonElement element:
                try
                {
                    var parsed = element.Deserialize<SpatialContextV1>(SpatialJsonOptions);
                    if (parsed != null)
                    {
                        context = parsed;
                        return true;
                    }
                }
                catch
                {
                    return false;
                }

                return false;
            case string text when !string.IsNullOrWhiteSpace(text):
                try
                {
                    var parsed = JsonSerializer.Deserialize<SpatialContextV1>(text, SpatialJsonOptions);
                    if (parsed != null)
                    {
                        context = parsed;
                        return true;
                    }
                }
                catch
                {
                    return false;
                }

                return false;
            default:
                return false;
        }
    }

    private static SpatialContextV1 BuildImageSpatialContext(
        SpatialContextV1 inputContext,
        Operator @operator,
        string operation,
        Rect outputBounds)
    {
        if (!operation.Equals("Crop", StringComparison.OrdinalIgnoreCase))
        {
            return BuildPassThroughSpatialContext(inputContext, @operator, "Image");
        }

        var currentFrame = new FrameRefV1(
            $"roi.local.{@operator.Id:N}.image",
            SpatialFrameKindV1.RoiLocal,
            SpatialUnitV1.Pixel,
            inputContext.CurrentFrame.FrameId);
        var localToParent = new SpatialTransform2DV1(
            currentFrame,
            inputContext.CurrentFrame,
            [
                [1, 0, outputBounds.X],
                [0, 1, outputBounds.Y],
                [0, 0, 1]
            ]);

        var transforms = inputContext.Transforms.ToList();
        transforms.Add(localToParent);
        return new SpatialContextV1(
            currentFrame,
            transforms,
            CreateSpatialBinding(@operator, "Image"));
    }

    private static SpatialContextV1 BuildPassThroughSpatialContext(
        SpatialContextV1 inputContext,
        Operator @operator,
        string outputName)
    {
        return new SpatialContextV1(
            inputContext.CurrentFrame,
            inputContext.Transforms,
            CreateSpatialBinding(@operator, outputName));
    }

    private static SpatialContextBindingV1 CreateSpatialBinding(Operator @operator, string outputName)
    {
        var outputPortId = @operator.OutputPorts
            .FirstOrDefault(port => port.Name.Equals(outputName, StringComparison.OrdinalIgnoreCase))
            ?.Id;
        return new SpatialContextBindingV1
        {
            SourceOperatorId = @operator.Id,
            OutputPortId = outputPortId,
            OutputName = outputName
        };
    }

    public override ValidationResult ValidateParameters(Operator @operator)
    {
        var shape = GetStringParam(@operator, "Shape", "Rectangle");
        var validShapes = new[] { "Rectangle", "Circle", "Polygon" };
        if (!validShapes.Contains(shape))
            return ValidationResult.Invalid($"形状必须是: {string.Join(", ", validShapes)}");

        var operation = GetStringParam(@operator, "Operation", "Crop");
        var validOperations = new[] { "Crop", "Mask" };
        if (!validOperations.Contains(operation))
            return ValidationResult.Invalid($"操作必须是: {string.Join(", ", validOperations)}");

        var width = GetIntParam(@operator, "Width", 200);
        if (width < 1)
            return ValidationResult.Invalid("宽度必须大于0");

        var height = GetIntParam(@operator, "Height", 200);
        if (height < 1)
            return ValidationResult.Invalid("高度必须大于0");

        var radius = GetIntParam(@operator, "Radius", 50);
        if (radius < 1)
            return ValidationResult.Invalid("半径必须大于0");

        return ValidationResult.Valid();
    }
}
