using Acme.Product.Core.Entities;
using Acme.Product.Core.Enums;
using Acme.Product.Core.Operators;
using OpenCvSharp;

namespace Acme.Product.Infrastructure.Operators;

/// <summary>
/// 距离测量算子 - 测量两点之间或点到轮廓的距离
/// </summary>
public class MeasureDistanceOperator : IOperatorExecutor
{
    public OperatorType OperatorType => OperatorType.Measurement;

    public Task<OperatorExecutionOutput> ExecuteAsync(Operator @operator, Dictionary<string, object>? inputs = null)
    {
        try
        {
            if (inputs == null || !inputs.TryGetValue("Image", out var imgObj) || imgObj is not byte[] imageData)
            {
                return Task.FromResult(OperatorExecutionOutput.Failure("未提供输入图像"));
            }

            // 获取参数
            var x1 = GetParameterValue(@operator, "X1", 0);
            var y1 = GetParameterValue(@operator, "Y1", 0);
            var x2 = GetParameterValue(@operator, "X2", 100);
            var y2 = GetParameterValue(@operator, "Y2", 100);
            var measureType = GetParameterValue(@operator, "MeasureType", "PointToPoint");

            using var src = Cv2.ImDecode(imageData, ImreadModes.Color);
            if (src.Empty())
            {
                return Task.FromResult(OperatorExecutionOutput.Failure("无法解码输入图像"));
            }

            double distance = 0;
            using var resultImg = src.Clone();
            Point pt1 = new Point(x1, y1);
            Point pt2 = new Point(x2, y2);

            switch (measureType.ToLower())
            {
                case "pointtopoint":
                    // 点到点距离
                    distance = Math.Sqrt(Math.Pow(x2 - x1, 2) + Math.Pow(y2 - y1, 2));
                    
                    // 绘制测量线
                    Cv2.Line(resultImg, pt1, pt2, new Scalar(0, 255, 0), 2);
                    Cv2.Circle(resultImg, pt1, 5, new Scalar(255, 0, 0), -1);
                    Cv2.Circle(resultImg, pt2, 5, new Scalar(255, 0, 0), -1);
                    
                    // 显示距离
                    var midPoint = new Point((x1 + x2) / 2, (y1 + y2) / 2);
                    Cv2.PutText(resultImg, $"{distance:F2}px", midPoint,
                        HersheyFonts.HersheySimplex, 0.7, new Scalar(0, 0, 255), 2);
                    break;

                case "horizontal":
                    // 水平距离
                    distance = Math.Abs(x2 - x1);
                    pt2.Y = y1; // 保持水平
                    
                    Cv2.Line(resultImg, pt1, pt2, new Scalar(0, 255, 0), 2);
                    Cv2.Circle(resultImg, pt1, 5, new Scalar(255, 0, 0), -1);
                    Cv2.Circle(resultImg, pt2, 5, new Scalar(255, 0, 0), -1);
                    
                    var hMidPoint = new Point((x1 + x2) / 2, y1 - 10);
                    Cv2.PutText(resultImg, $"H: {distance:F2}px", hMidPoint,
                        HersheyFonts.HersheySimplex, 0.7, new Scalar(0, 0, 255), 2);
                    break;

                case "vertical":
                    // 垂直距离
                    distance = Math.Abs(y2 - y1);
                    pt2.X = x1; // 保持垂直
                    
                    Cv2.Line(resultImg, pt1, pt2, new Scalar(0, 255, 0), 2);
                    Cv2.Circle(resultImg, pt1, 5, new Scalar(255, 0, 0), -1);
                    Cv2.Circle(resultImg, pt2, 5, new Scalar(255, 0, 0), -1);
                    
                    var vMidPoint = new Point(x1 + 10, (y1 + y2) / 2);
                    Cv2.PutText(resultImg, $"V: {distance:F2}px", vMidPoint,
                        HersheyFonts.HersheySimplex, 0.7, new Scalar(0, 0, 255), 2);
                    break;

                default:
                    return Task.FromResult(OperatorExecutionOutput.Failure($"不支持的测量类型: {measureType}"));
            }

            var outputData = resultImg.ToBytes(".png");

            return Task.FromResult(OperatorExecutionOutput.Success(new Dictionary<string, object>
            {
                { "Image", outputData },
                { "Width", resultImg.Width },
                { "Height", resultImg.Height },
                { "Distance", distance },
                { "X1", x1 },
                { "Y1", y1 },
                { "X2", x2 },
                { "Y2", y2 },
                { "MeasureType", measureType },
                { "DeltaX", x2 - x1 },
                { "DeltaY", y2 - y1 }
            }));
        }
        catch (Exception ex)
        {
            return Task.FromResult(OperatorExecutionOutput.Failure($"距离测量失败: {ex.Message}"));
        }
    }

    public ValidationResult ValidateParameters(Operator @operator)
    {
        var measureType = GetParameterValue(@operator, "MeasureType", "PointToPoint").ToLower();
        var validTypes = new[] { "pointtopoint", "horizontal", "vertical" };
        
        if (!validTypes.Contains(measureType))
        {
            return ValidationResult.Invalid($"不支持的测量类型: {measureType}");
        }

        return ValidationResult.Valid();
    }

    private T GetParameterValue<T>(Operator @operator, string paramName, T defaultValue)
    {
        var param = @operator.Parameters.FirstOrDefault(p => p.Name == paramName);
        if (param?.Value != null)
        {
            try
            {
                return (T)Convert.ChangeType(param.Value, typeof(T));
            }
            catch
            {
                return defaultValue;
            }
        }
        return defaultValue;
    }
}
