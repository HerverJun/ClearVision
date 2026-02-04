using Acme.Product.Core.Entities;
using Acme.Product.Core.Enums;
using Acme.Product.Core.Operators;
using Acme.Product.Core.ValueObjects;
using OpenCvSharp;

namespace Acme.Product.Infrastructure.Operators;

/// <summary>
/// 图像采集算子
/// </summary>
public class ImageAcquisitionOperator : IOperatorExecutor
{
    public OperatorType OperatorType => OperatorType.ImageAcquisition;

    public Task<OperatorExecutionOutput> ExecuteAsync(Operator @operator, Dictionary<string, object>? inputs = null)
    {
        try
        {
            // 从输入获取图像数据或文件路径
            if (inputs != null && inputs.TryGetValue("ImagePath", out var pathObj) && pathObj is string imagePath)
            {
                // 从文件加载图像
                if (!File.Exists(imagePath))
                {
                    return Task.FromResult(OperatorExecutionOutput.Failure($"图像文件不存在: {imagePath}"));
                }

                using var mat = Cv2.ImRead(imagePath, ImreadModes.Color);
                if (mat.Empty())
                {
                    return Task.FromResult(OperatorExecutionOutput.Failure("无法加载图像文件"));
                }

                // 转换为字节数组
                var imageData = mat.ToBytes(".png");

                return Task.FromResult(OperatorExecutionOutput.Success(new Dictionary<string, object>
                {
                    { "Image", imageData },
                    { "Width", mat.Width },
                    { "Height", mat.Height },
                    { "Channels", mat.Channels() }
                }));
            }
            else if (inputs != null && inputs.TryGetValue("Image", out var imgObj) && imgObj is byte[] imageData)
            {
                // 直接返回输入的图像数据
                using var mat = Cv2.ImDecode(imageData, ImreadModes.Color);

                return Task.FromResult(OperatorExecutionOutput.Success(new Dictionary<string, object>
                {
                    { "Image", imageData },
                    { "Width", mat.Width },
                    { "Height", mat.Height },
                    { "Channels", mat.Channels() }
                }));
            }
            else
            {
                return Task.FromResult(OperatorExecutionOutput.Failure("未提供图像数据或文件路径"));
            }
        }
        catch (Exception ex)
        {
            return Task.FromResult(OperatorExecutionOutput.Failure($"图像采集失败: {ex.Message}"));
        }
    }

    public ValidationResult ValidateParameters(Operator @operator)
    {
        return ValidationResult.Valid();
    }
}
