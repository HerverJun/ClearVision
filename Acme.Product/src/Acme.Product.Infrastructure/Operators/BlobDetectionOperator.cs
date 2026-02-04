using Acme.Product.Core.Entities;
using Acme.Product.Core.Enums;
using Acme.Product.Core.Operators;
using OpenCvSharp;

namespace Acme.Product.Infrastructure.Operators;

/// <summary>
/// Blob检测算子 - 检测图像中的连通区域
/// </summary>
public class BlobDetectionOperator : IOperatorExecutor
{
    public OperatorType OperatorType => OperatorType.BlobAnalysis;

    public Task<OperatorExecutionOutput> ExecuteAsync(Operator @operator, Dictionary<string, object>? inputs = null)
    {
        try
        {
            if (inputs == null || !inputs.TryGetValue("Image", out var imgObj) || imgObj is not byte[] imageData)
            {
                return Task.FromResult(OperatorExecutionOutput.Failure("未提供输入图像"));
            }

            // 获取参数
            var minArea = GetParameterValue(@operator, "MinArea", 100);
            var maxArea = GetParameterValue(@operator, "MaxArea", 100000);
            var minCircularity = GetParameterValue(@operator, "MinCircularity", 0.0);
            var minConvexity = GetParameterValue(@operator, "MinConvexity", 0.0);
            var minInertiaRatio = GetParameterValue(@operator, "MinInertiaRatio", 0.0);

            using var src = Cv2.ImDecode(imageData, ImreadModes.Grayscale);
            if (src.Empty())
            {
                return Task.FromResult(OperatorExecutionOutput.Failure("无法解码输入图像"));
            }

            // 创建Blob检测器
            var detector = new SimpleBlobDetector.Params();
            detector.FilterByArea = true;
            detector.MinArea = minArea;
            detector.MaxArea = maxArea;
            
            // 只有当值大于0时才启用相应的过滤器
            if (minCircularity > 0)
            {
                detector.FilterByCircularity = true;
                detector.MinCircularity = (float)minCircularity;
            }
            
            if (minConvexity > 0)
            {
                detector.FilterByConvexity = true;
                detector.MinConvexity = (float)minConvexity;
            }
            
            if (minInertiaRatio > 0)
            {
                detector.FilterByInertia = true;
                detector.MinInertiaRatio = (float)minInertiaRatio;
            }

            using var blobDetector = SimpleBlobDetector.Create(detector);
            var keypoints = blobDetector.Detect(src);

            // 转换为彩色图像以绘制结果
            using var colorSrc = new Mat();
            Cv2.CvtColor(src, colorSrc, ColorConversionCodes.GRAY2BGR);

            // 绘制检测到的Blob
            foreach (var kp in keypoints)
            {
                Cv2.Circle(colorSrc, (int)kp.Pt.X, (int)kp.Pt.Y, (int)kp.Size / 2, new Scalar(0, 255, 0), 2);
                Cv2.Circle(colorSrc, (int)kp.Pt.X, (int)kp.Pt.Y, 3, new Scalar(0, 0, 255), -1);
            }

            var outputData = colorSrc.ToBytes(".png");

            // 构建Blob信息列表
            var blobs = keypoints.Select(kp => new Dictionary<string, object>
            {
                { "X", kp.Pt.X },
                { "Y", kp.Pt.Y },
                { "Size", kp.Size },
                { "Area", Math.PI * Math.Pow(kp.Size / 2, 2) }
            }).ToList();

            return Task.FromResult(OperatorExecutionOutput.Success(new Dictionary<string, object>
            {
                { "Image", outputData },
                { "Width", colorSrc.Width },
                { "Height", colorSrc.Height },
                { "BlobCount", keypoints.Length },
                { "Blobs", blobs }
            }));
        }
        catch (Exception ex)
        {
            return Task.FromResult(OperatorExecutionOutput.Failure($"Blob检测失败: {ex.Message}"));
        }
    }

    public ValidationResult ValidateParameters(Operator @operator)
    {
        var minArea = GetParameterValue(@operator, "MinArea", 100);
        var maxArea = GetParameterValue(@operator, "MaxArea", 100000);

        if (minArea < 0 || maxArea < 0)
        {
            return ValidationResult.Invalid("面积范围不能为负数");
        }

        if (minArea >= maxArea)
        {
            return ValidationResult.Invalid("最小面积必须小于最大面积");
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
