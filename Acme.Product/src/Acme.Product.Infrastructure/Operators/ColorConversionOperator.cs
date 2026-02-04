using Acme.Product.Core.Entities;
using Acme.Product.Core.Enums;
using Acme.Product.Core.Operators;
using OpenCvSharp;

namespace Acme.Product.Infrastructure.Operators;

/// <summary>
/// 颜色空间转换算子 - 支持BGR/GRAY/HSV/Lab/YUV等颜色空间转换
/// </summary>
public class ColorConversionOperator : IOperatorExecutor
{
    public OperatorType OperatorType => OperatorType.Preprocessing;

    public Task<OperatorExecutionOutput> ExecuteAsync(Operator @operator, Dictionary<string, object>? inputs = null)
    {
        try
        {
            if (inputs == null || !inputs.TryGetValue("Image", out var imgObj) || imgObj is not byte[] imageData)
            {
                return Task.FromResult(OperatorExecutionOutput.Failure("未提供输入图像"));
            }

            // 获取参数
            var conversionCode = GetParameterValue(@operator, "ConversionCode", "BGR2GRAY");
            var srcChannels = GetParameterValue(@operator, "SourceChannels", 3);

            // 根据源通道数选择ImreadModes
            var imreadMode = srcChannels switch
            {
                1 => ImreadModes.Grayscale,
                3 => ImreadModes.Color,
                4 => ImreadModes.Unchanged,
                _ => ImreadModes.Color
            };

            using var src = Cv2.ImDecode(imageData, imreadMode);
            if (src.Empty())
            {
                return Task.FromResult(OperatorExecutionOutput.Failure("无法解码输入图像"));
            }

            // 解析颜色转换代码
            var colorCode = conversionCode.ToUpper() switch
            {
                "BGR2GRAY" => ColorConversionCodes.BGR2GRAY,
                "GRAY2BGR" => ColorConversionCodes.GRAY2BGR,
                "BGR2HSV" => ColorConversionCodes.BGR2HSV,
                "HSV2BGR" => ColorConversionCodes.HSV2BGR,
                "BGR2Lab" => ColorConversionCodes.BGR2Lab,
                "Lab2BGR" => ColorConversionCodes.Lab2BGR,
                "BGR2YUV" => ColorConversionCodes.BGR2YUV,
                "YUV2BGR" => ColorConversionCodes.YUV2BGR,
                "BGR2RGB" => ColorConversionCodes.BGR2RGB,
                "RGB2BGR" => ColorConversionCodes.RGB2BGR,
                "BGR2RGBA" => ColorConversionCodes.BGR2RGBA,
                "BGR2XYZ" => ColorConversionCodes.BGR2XYZ,
                "XYZ2BGR" => ColorConversionCodes.XYZ2BGR,
                "BGR2HLS" => ColorConversionCodes.BGR2HLS,
                "HLS2BGR" => ColorConversionCodes.HLS2BGR,
                _ => ColorConversionCodes.BGR2GRAY
            };

            using var dst = new Mat();
            Cv2.CvtColor(src, dst, colorCode);

            var outputData = dst.ToBytes(".png");

            return Task.FromResult(OperatorExecutionOutput.Success(new Dictionary<string, object>
            {
                { "Image", outputData },
                { "Width", dst.Width },
                { "Height", dst.Height },
                { "Channels", dst.Channels() },
                { "ConversionCode", conversionCode }
            }));
        }
        catch (Exception ex)
        {
            return Task.FromResult(OperatorExecutionOutput.Failure($"颜色空间转换失败: {ex.Message}"));
        }
    }

    public ValidationResult ValidateParameters(Operator @operator)
    {
        var conversionCode = GetParameterValue(@operator, "ConversionCode", "BGR2GRAY").ToUpper();
        var validCodes = new[] 
        { 
            "BGR2GRAY", "GRAY2BGR", "BGR2HSV", "HSV2BGR",
            "BGR2Lab", "Lab2BGR", "BGR2YUV", "YUV2BGR",
            "BGR2RGB", "RGB2BGR", "BGR2RGBA", "BGR2XYZ",
            "XYZ2BGR", "BGR2HLS", "HLS2BGR"
        };
        
        if (!validCodes.Contains(conversionCode))
        {
            return ValidationResult.Invalid($"不支持的颜色转换代码: {conversionCode}");
        }

        var srcChannels = GetParameterValue(@operator, "SourceChannels", 3);
        if (srcChannels is not (1 or 3 or 4))
        {
            return ValidationResult.Invalid("源通道数必须是1、3或4");
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
