// RoiManagerOperator.cs
// ROI 裁剪与掩膜算子 - 支持矩形、圆形和多边形区域
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
/// ROI 裁剪与掩膜算子 - 矩形/圆形/多边形区域选择
/// </summary>
[OperatorMeta(
    DisplayName = "ROI裁剪与掩膜",
    Description = "按矩形、圆形或多边形 ROI 裁剪图像或应用掩膜，并输出空间上下文。",
    CategoryId = OperatorCategoryId.ImagePreprocessing,
    IconName = "roi",
    Keywords = new[] { "ROI", "区域", "感兴趣区", "掩膜", "选区", "Region", "Mask", "Area of interest", "ROI管理器" }
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

        var inputSpatialContext = ResolveInputSpatialContext(inputs);
        Mat resultImage;
        Mat mask = new Mat(src.Size(), MatType.CV_8UC1, Scalar.All(0));
        Rect outputBounds;

        try
        {
            switch (shape)
            {
                case "Rectangle":
                    if (!TryCreateRectangleBounds(src, x, y, width, height, out var rectangleBounds))
                    {
                        return Task.FromResult(OperatorExecutionOutput.Failure("ROI区域超出图像边界"));
                    }

                    ProcessRectangle(src, out resultImage, mask, operation, rectangleBounds, out outputBounds);
                    break;
                case "Circle":
                    if (!TryCreateCircleBounds(src, centerX, centerY, radius, out var circleBounds))
                    {
                        return Task.FromResult(OperatorExecutionOutput.Failure("ROI区域超出图像边界"));
                    }

                    ProcessCircle(src, out resultImage, mask, operation, centerX, centerY, radius, circleBounds, out outputBounds);
                    break;
                case "Polygon":
                    if (!TryResolvePolygonPoints(polygonPoints, src.Size(), out var resolvedPolygonPoints, out var polygonBounds))
                    {
                        return Task.FromResult(OperatorExecutionOutput.Failure("ROI区域超出图像边界"));
                    }

                    ProcessPolygon(src, out resultImage, mask, operation, resolvedPolygonPoints, polygonBounds, out outputBounds);
                    break;
                default:
                    if (!TryCreateRectangleBounds(src, x, y, width, height, out var defaultBounds))
                    {
                        return Task.FromResult(OperatorExecutionOutput.Failure("ROI区域超出图像边界"));
                    }

                    ProcessRectangle(src, out resultImage, mask, operation, defaultBounds, out outputBounds);
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
        Rect rect,
        out Rect outputBounds)
    {
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
        Rect bounds,
        out Rect outputBounds)
    {
        var center = new Point(centerX, centerY);

        // 在掩膜上绘制圆形
        Cv2.Circle(mask, center, Math.Max(1, radius), Scalar.All(255), -1);

        if (operation == "Crop")
        {
            // 裁剪模式 - 裁剪外接矩形并应用圆形掩膜
            outputBounds = bounds;
            using var cropped = new Mat(src, bounds);
            using var croppedMask = new Mat(mask, bounds);
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
        Point[] polygonPoints,
        Rect bounds,
        out Rect outputBounds)
    {
        var points = new[] { polygonPoints };

        // 在掩膜上填充多边形
        Cv2.FillPoly(mask, points, Scalar.All(255));

        if (operation == "Crop")
        {
            outputBounds = bounds;
            using var cropped = new Mat(src, bounds);
            using var croppedMask = new Mat(mask, bounds);
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

    private static bool TryCreateRectangleBounds(Mat src, int x, int y, int width, int height, out Rect bounds)
    {
        bounds = default;
        if (src.Width <= 0 || src.Height <= 0)
        {
            return false;
        }

        var left = Math.Max(0, x);
        var top = Math.Max(0, y);
        var right = Math.Min(src.Width, (long)x + Math.Max(1, width));
        var bottom = Math.Min(src.Height, (long)y + Math.Max(1, height));
        if (right <= left || bottom <= top)
        {
            return false;
        }

        bounds = new Rect(left, top, (int)(right - left), (int)(bottom - top));
        return true;
    }

    private static bool TryCreateCircleBounds(Mat src, int centerX, int centerY, int radius, out Rect bounds)
    {
        bounds = default;
        if (src.Width <= 0 || src.Height <= 0)
        {
            return false;
        }

        var safeRadius = Math.Max(1, radius);
        var left = Math.Max(0, (long)centerX - safeRadius);
        var top = Math.Max(0, (long)centerY - safeRadius);
        var right = Math.Min(src.Width, (long)centerX + safeRadius);
        var bottom = Math.Min(src.Height, (long)centerY + safeRadius);
        if (right <= left || bottom <= top)
        {
            return false;
        }

        bounds = new Rect((int)left, (int)top, (int)(right - left), (int)(bottom - top));
        return true;
    }

    private static bool TryResolvePolygonPoints(string polygonPointsJson, Size imageSize, out Point[] points, out Rect bounds)
    {
        points = TryParsePolygonPoints(polygonPointsJson) ?? [];
        if (points.Length < 3)
        {
            points = CreateDefaultPolygonPoints(imageSize);
        }

        points = points
            .Select(point => new Point(
                Math.Clamp(point.X, 0, imageSize.Width),
                Math.Clamp(point.Y, 0, imageSize.Height)))
            .ToArray();

        if (TryCreatePolygonBounds(points, imageSize, out bounds))
        {
            return true;
        }

        points = CreateDefaultPolygonPoints(imageSize);
        return TryCreatePolygonBounds(points, imageSize, out bounds);
    }

    private static Point[]? TryParsePolygonPoints(string polygonPointsJson)
    {
        try
        {
            using var document = JsonDocument.Parse(polygonPointsJson);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var points = new List<Point>();
            foreach (var item in document.RootElement.EnumerateArray())
            {
                if (TryReadPoint(item, out var point))
                {
                    points.Add(point);
                }
            }

            return points.ToArray();
        }
        catch
        {
            return null;
        }
    }

    private static bool TryReadPoint(JsonElement item, out Point point)
    {
        point = default;
        if (item.ValueKind == JsonValueKind.Array)
        {
            var coordinates = item.EnumerateArray().Take(2).ToArray();
            if (coordinates.Length < 2 ||
                !TryReadCoordinate(coordinates[0], out var x) ||
                !TryReadCoordinate(coordinates[1], out var y))
            {
                return false;
            }

            point = new Point(x, y);
            return true;
        }

        if (item.ValueKind == JsonValueKind.Object &&
            TryReadNamedCoordinate(item, ["x", "X", "imageX", "ImageX", "pixelX", "PixelX"], out var objectX) &&
            TryReadNamedCoordinate(item, ["y", "Y", "imageY", "ImageY", "pixelY", "PixelY"], out var objectY))
        {
            point = new Point(objectX, objectY);
            return true;
        }

        return false;
    }

    private static bool TryReadNamedCoordinate(JsonElement item, string[] names, out int value)
    {
        foreach (var name in names)
        {
            if (item.TryGetProperty(name, out var property) && TryReadCoordinate(property, out value))
            {
                return true;
            }
        }

        value = 0;
        return false;
    }

    private static bool TryReadCoordinate(JsonElement item, out int value)
    {
        value = 0;
        if (item.ValueKind != JsonValueKind.Number || !item.TryGetDouble(out var number) || !double.IsFinite(number))
        {
            return false;
        }

        value = (int)Math.Round(Math.Clamp(number, int.MinValue, int.MaxValue));
        return true;
    }

    private static Point[] CreateDefaultPolygonPoints(Size imageSize)
    {
        if (imageSize.Width <= 0 || imageSize.Height <= 0)
        {
            return [];
        }

        return
        [
            new Point(0, 0),
            new Point(imageSize.Width, 0),
            new Point(imageSize.Width, imageSize.Height),
            new Point(0, imageSize.Height)
        ];
    }

    private static bool TryCreatePolygonBounds(Point[] points, Size imageSize, out Rect bounds)
    {
        bounds = default;
        if (imageSize.Width <= 0 || imageSize.Height <= 0 || points.Length < 3)
        {
            return false;
        }

        var minX = Math.Max(0, points.Min(point => point.X));
        var minY = Math.Max(0, points.Min(point => point.Y));
        var maxX = Math.Min(imageSize.Width, points.Max(point => point.X));
        var maxY = Math.Min(imageSize.Height, points.Max(point => point.Y));
        if (maxX <= minX || maxY <= minY)
        {
            return false;
        }

        bounds = new Rect(minX, minY, maxX - minX, maxY - minY);
        return true;
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
