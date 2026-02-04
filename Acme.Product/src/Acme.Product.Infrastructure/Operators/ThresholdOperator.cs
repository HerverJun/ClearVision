using Acme.Product.Core.Entities;
using Acme.Product.Core.Enums;
using Acme.Product.Core.Operators;
using OpenCvSharp;

namespace Acme.Product.Infrastructure.Operators;

/// <summary>
/// 阈值二值化算子
/// </summary>
public class ThresholdOperator : IOperatorExecutor
{
    public OperatorType OperatorType => OperatorType.Thresholding;

    public Task<OperatorExecutionOutput> ExecuteAsync(Operator @operator, Dictionary<string, object>? inputs = null)
    {
        try
        {
            if (inputs == null || !inputs.TryGetValue("Image", out var imgObj) || imgObj is not byte[] imageData)
            {
                return Task.FromResult(OperatorExecutionOutput.Failure("未提供输入图像"));
            }

            // 获取参数
            var thresh = GetParameterValue(@operator, "Threshold", 127.0);
            var maxval = GetParameterValue(@operator, "MaxValue", 255.0);
            var type = GetParameterValue(@operator, "Type", 0); // 0 = Binary
            var useOtsu = GetParameterValue(@operator, "UseOtsu", false); // 是否使用Otsu自动阈值

            using var src = Cv2.ImDecode(imageData, ImreadModes.Grayscale);
            if (src.Empty())
            {
                return Task.FromResult(OperatorExecutionOutput.Failure("无法解码输入图像"));
            }

            using var dst = new Mat();
            
            // 组合阈值类型
            var thresholdType = (ThresholdTypes)type;
            if (useOtsu)
            {
                // Otsu会自动计算最优阈值，忽略传入的thresh值
                thresholdType |= ThresholdTypes.Otsu;
            }

            var actualThreshold = Cv2.Threshold(src, dst, thresh, maxval, thresholdType);

            var outputData = dst.ToBytes(".png");

            // 构建输出结果，包含实际使用的阈值（Otsu时会自动计算）
            var output = new Dictionary<string, object>
            {
                { "Image", outputData },
                { "Width", dst.Width },
                { "Height", dst.Height },
                { "ActualThreshold", actualThreshold }
            };

            // 如果是Otsu模式，添加说明
            if (useOtsu)
            {
                output["OtsuThreshold"] = actualThreshold;
            }

            return Task.FromResult(OperatorExecutionOutput.Success(output));
        }
        catch (Exception ex)
        {
            return Task.FromResult(OperatorExecutionOutput.Failure($"阈值二值化失败: {ex.Message}"));
        }
    }

    public ValidationResult ValidateParameters(Operator @operator)
    {
        var thresh = GetParameterValue(@operator, "Threshold", 127.0);
        if (thresh < 0 || thresh > 255)
            return ValidationResult.Invalid("阈值必须在 0-255 之间");

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
