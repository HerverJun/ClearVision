using Acme.Product.Core.Entities;
using Acme.Product.Core.Enums;
using Acme.Product.Core.Operators;
using OpenCvSharp;

namespace Acme.Product.Infrastructure.Operators;

/// <summary>
/// Canny边缘检测算子
/// </summary>
public class CannyEdgeOperator : IOperatorExecutor
{
    public OperatorType OperatorType => OperatorType.EdgeDetection;

    public Task<OperatorExecutionOutput> ExecuteAsync(Operator @operator, Dictionary<string, object>? inputs = null)
    {
        try
        {
            if (inputs == null || !inputs.TryGetValue("Image", out var imgObj) || imgObj is not byte[] imageData)
            {
                return Task.FromResult(OperatorExecutionOutput.Failure("未提供输入图像"));
            }

            // 获取参数
            var threshold1 = GetParameterValue(@operator, "Threshold1", 50.0);
            var threshold2 = GetParameterValue(@operator, "Threshold2", 150.0);
            var enableGaussianBlur = GetParameterValue(@operator, "EnableGaussianBlur", true); // 默认启用高斯预处理
            var gaussianKernelSize = GetParameterValue(@operator, "GaussianKernelSize", 5);
            var apertureSize = GetParameterValue(@operator, "ApertureSize", 3); // Sobel算子孔径大小
            var l2Gradient = GetParameterValue(@operator, "L2Gradient", false); // 是否使用L2范数计算梯度

            using var src = Cv2.ImDecode(imageData, ImreadModes.Grayscale);
            if (src.Empty())
            {
                return Task.FromResult(OperatorExecutionOutput.Failure("无法解码输入图像"));
            }

            using var processedSrc = new Mat();
            
            // 可选的高斯模糊预处理（Canny算法标准建议）
            if (enableGaussianBlur)
            {
                // 确保核大小为奇数
                if (gaussianKernelSize % 2 == 0)
                    gaussianKernelSize++;
                Cv2.GaussianBlur(src, processedSrc, new Size(gaussianKernelSize, gaussianKernelSize), 1.0);
            }
            else
            {
                src.CopyTo(processedSrc);
            }

            using var dst = new Mat();
            Cv2.Canny(processedSrc, dst, threshold1, threshold2, apertureSize, l2Gradient);

            var outputData = dst.ToBytes(".png");

            return Task.FromResult(OperatorExecutionOutput.Success(new Dictionary<string, object>
            {
                { "Image", outputData },
                { "Width", dst.Width },
                { "Height", dst.Height },
                { "Edges", dst }
            }));
        }
        catch (Exception ex)
        {
            return Task.FromResult(OperatorExecutionOutput.Failure($"Canny边缘检测失败: {ex.Message}"));
        }
    }

    public ValidationResult ValidateParameters(Operator @operator)
    {
        var threshold1 = GetParameterValue(@operator, "Threshold1", 50.0);
        var threshold2 = GetParameterValue(@operator, "Threshold2", 150.0);

        if (threshold1 < 0 || threshold1 > 255)
            return ValidationResult.Invalid("阈值1必须在 0-255 之间");

        if (threshold2 < 0 || threshold2 > 255)
            return ValidationResult.Invalid("阈值2必须在 0-255 之间");

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
