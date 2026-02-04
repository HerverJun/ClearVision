using Acme.Product.Core.Entities;
using Acme.Product.Core.Enums;
using Acme.Product.Core.Operators;
using OpenCvSharp;

namespace Acme.Product.Infrastructure.Operators;

/// <summary>
/// 自适应阈值算子 - 支持Mean和Gaussian自适应阈值
/// </summary>
public class AdaptiveThresholdOperator : IOperatorExecutor
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
            var maxValue = GetParameterValue(@operator, "MaxValue", 255.0);
            var adaptiveMethod = GetParameterValue(@operator, "AdaptiveMethod", "Gaussian"); // Mean 或 Gaussian
            var thresholdType = GetParameterValue(@operator, "ThresholdType", "Binary"); // Binary 或 BinaryInv
            var blockSize = GetParameterValue(@operator, "BlockSize", 11); // 必须是奇数
            var c = GetParameterValue(@operator, "C", 2.0); // 从均值/加权值中减去的常数

            using var src = Cv2.ImDecode(imageData, ImreadModes.Grayscale);
            if (src.Empty())
            {
                return Task.FromResult(OperatorExecutionOutput.Failure("无法解码输入图像"));
            }

            // 确保blockSize为奇数且至少为3
            if (blockSize % 2 == 0)
                blockSize++;
            if (blockSize < 3)
                blockSize = 3;

            // 解析自适应方法
            var adaptiveType = adaptiveMethod.ToLower() switch
            {
                "mean" => AdaptiveThresholdTypes.MeanC,
                "gaussian" => AdaptiveThresholdTypes.GaussianC,
                _ => AdaptiveThresholdTypes.GaussianC
            };

            // 解析阈值类型
            var threshType = thresholdType.ToLower() switch
            {
                "binary" => ThresholdTypes.Binary,
                "binaryinv" => ThresholdTypes.BinaryInv,
                _ => ThresholdTypes.Binary
            };

            using var dst = new Mat();
            Cv2.AdaptiveThreshold(src, dst, maxValue, adaptiveType, threshType, blockSize, c);

            var outputData = dst.ToBytes(".png");

            return Task.FromResult(OperatorExecutionOutput.Success(new Dictionary<string, object>
            {
                { "Image", outputData },
                { "Width", dst.Width },
                { "Height", dst.Height },
                { "AdaptiveMethod", adaptiveMethod },
                { "BlockSize", blockSize },
                { "C", c }
            }));
        }
        catch (Exception ex)
        {
            return Task.FromResult(OperatorExecutionOutput.Failure($"自适应阈值失败: {ex.Message}"));
        }
    }

    public ValidationResult ValidateParameters(Operator @operator)
    {
        var maxValue = GetParameterValue(@operator, "MaxValue", 255.0);
        if (maxValue < 0 || maxValue > 255)
            return ValidationResult.Invalid("最大值必须在 0-255 之间");

        var blockSize = GetParameterValue(@operator, "BlockSize", 11);
        if (blockSize < 3 || blockSize > 51)
            return ValidationResult.Invalid("块大小必须在 3-51 之间");

        var adaptiveMethod = GetParameterValue(@operator, "AdaptiveMethod", "Gaussian").ToLower();
        var validMethods = new[] { "mean", "gaussian" };
        if (!validMethods.Contains(adaptiveMethod))
            return ValidationResult.Invalid($"不支持的自适应方法: {adaptiveMethod}");

        var thresholdType = GetParameterValue(@operator, "ThresholdType", "Binary").ToLower();
        var validTypes = new[] { "binary", "binaryinv" };
        if (!validTypes.Contains(thresholdType))
            return ValidationResult.Invalid($"不支持的阈值类型: {thresholdType}");

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
