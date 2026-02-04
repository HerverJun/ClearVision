using Acme.Product.Core.Entities;
using Acme.Product.Core.Enums;
using Acme.Product.Core.Operators;
using OpenCvSharp;

namespace Acme.Product.Infrastructure.Operators;

/// <summary>
/// 高斯模糊算子
/// </summary>
public class GaussianBlurOperator : IOperatorExecutor
{
    public OperatorType OperatorType => OperatorType.Filtering;

    public Task<OperatorExecutionOutput> ExecuteAsync(Operator @operator, Dictionary<string, object>? inputs = null)
    {
        try
        {
            if (inputs == null || !inputs.TryGetValue("Image", out var imgObj) || imgObj is not byte[] imageData)
            {
                return Task.FromResult(OperatorExecutionOutput.Failure("未提供输入图像"));
            }

            // 获取参数
            var kernelSize = GetParameterValue(@operator, "KernelSize", 5);
            var sigmaX = GetParameterValue(@operator, "SigmaX", 1.0);
            var sigmaY = GetParameterValue(@operator, "SigmaY", 0.0); // 0表示与sigmaX相同
            var borderType = GetParameterValue(@operator, "BorderType", 4); // 默认BORDER_DEFAULT

            // 确保核大小为奇数
            if (kernelSize % 2 == 0)
                kernelSize++;

            // sigmaY为0时，自动设为sigmaX的值（OpenCV标准行为）
            if (sigmaY == 0)
                sigmaY = sigmaX;

            using var src = Cv2.ImDecode(imageData, ImreadModes.Color);
            if (src.Empty())
            {
                return Task.FromResult(OperatorExecutionOutput.Failure("无法解码输入图像"));
            }

            using var dst = new Mat();
            var borderMode = (BorderTypes)borderType;
            Cv2.GaussianBlur(src, dst, new Size(kernelSize, kernelSize), sigmaX, sigmaY, borderMode);

            var outputData = dst.ToBytes(".png");

            return Task.FromResult(OperatorExecutionOutput.Success(new Dictionary<string, object>
            {
                { "Image", outputData },
                { "Width", dst.Width },
                { "Height", dst.Height }
            }));
        }
        catch (Exception ex)
        {
            return Task.FromResult(OperatorExecutionOutput.Failure($"高斯模糊失败: {ex.Message}"));
        }
    }

    public ValidationResult ValidateParameters(Operator @operator)
    {
        var kernelSize = GetParameterValue(@operator, "KernelSize", 5);
        if (kernelSize < 1 || kernelSize > 31)
        {
            return ValidationResult.Invalid("核大小必须在 1-31 之间");
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
