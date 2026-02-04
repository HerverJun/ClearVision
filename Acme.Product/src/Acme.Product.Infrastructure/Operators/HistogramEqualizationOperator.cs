using Acme.Product.Core.Entities;
using Acme.Product.Core.Enums;
using Acme.Product.Core.Operators;
using OpenCvSharp;

namespace Acme.Product.Infrastructure.Operators;

/// <summary>
/// 直方图均衡化算子 - 支持全局均衡化和CLAHE自适应均衡化
/// </summary>
public class HistogramEqualizationOperator : IOperatorExecutor
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
            var method = GetParameterValue(@operator, "Method", "Global"); // Global 或 CLAHE
            var clipLimit = GetParameterValue(@operator, "ClipLimit", 2.0); // CLAHE裁剪限制
            var tileSize = GetParameterValue(@operator, "TileSize", 8); // CLAHE网格大小
            var applyToEachChannel = GetParameterValue(@operator, "ApplyToEachChannel", false); // 是否对每个通道单独处理

            using var src = Cv2.ImDecode(imageData, ImreadModes.Color);
            if (src.Empty())
            {
                return Task.FromResult(OperatorExecutionOutput.Failure("无法解码输入图像"));
            }

            using var dst = new Mat();

            if (method.ToLower() == "clahe")
            {
                // CLAHE (对比度受限的自适应直方图均衡化)
                using var clahe = Cv2.CreateCLAHE(clipLimit, new Size(tileSize, tileSize));

                if (applyToEachChannel && src.Channels() == 3)
                {
                    // 对每个通道单独应用CLAHE
                    Cv2.Split(src, out var channels);

                    var equalizedChannels = new Mat[3];
                    for (int i = 0; i < 3; i++)
                    {
                        equalizedChannels[i] = new Mat();
                        clahe.Apply(channels[i], equalizedChannels[i]);
                        channels[i].Dispose();
                    }

                    Cv2.Merge(equalizedChannels, dst);

                    // 清理临时Mat
                    foreach (var mat in equalizedChannels)
                    {
                        mat.Dispose();
                    }
                }
                else
                {
                    // 转换为Lab颜色空间，仅对L通道应用CLAHE
                    using var lab = new Mat();
                    Cv2.CvtColor(src, lab, ColorConversionCodes.BGR2Lab);

                    Cv2.Split(lab, out var labChannels);

                    using var lChannel = new Mat();
                    clahe.Apply(labChannels[0], lChannel);

                    // 合并通道
                    using var mergedLab = new Mat();
                    Cv2.Merge(new Mat[] { lChannel, labChannels[1], labChannels[2] }, mergedLab);
                    
                    // 清理通道
                    foreach (var ch in labChannels)
                    {
                        ch.Dispose();
                    }

                    // 转换回BGR
                    Cv2.CvtColor(mergedLab, dst, ColorConversionCodes.Lab2BGR);
                }
            }
            else
            {
                // 全局直方图均衡化
                if (applyToEachChannel && src.Channels() == 3)
                {
                    // 对每个通道单独处理
                    Cv2.Split(src, out var channels);

                    var equalizedChannels = new Mat[3];
                    for (int i = 0; i < 3; i++)
                    {
                        equalizedChannels[i] = new Mat();
                        Cv2.EqualizeHist(channels[i], equalizedChannels[i]);
                        channels[i].Dispose();
                    }

                    Cv2.Merge(equalizedChannels, dst);

                    // 清理临时Mat
                    foreach (var mat in equalizedChannels)
                    {
                        mat.Dispose();
                    }
                }
                else
                {
                    // 转换为YUV，仅对Y通道处理
                    using var yuv = new Mat();
                    Cv2.CvtColor(src, yuv, ColorConversionCodes.BGR2YUV);

                    Cv2.Split(yuv, out var yuvChannels);

                    using var yChannel = new Mat();
                    Cv2.EqualizeHist(yuvChannels[0], yChannel);

                    // 合并通道
                    using var mergedYuv = new Mat();
                    Cv2.Merge(new Mat[] { yChannel, yuvChannels[1], yuvChannels[2] }, mergedYuv);
                    
                    // 清理通道
                    foreach (var ch in yuvChannels)
                    {
                        ch.Dispose();
                    }

                    // 转换回BGR
                    Cv2.CvtColor(mergedYuv, dst, ColorConversionCodes.YUV2BGR);
                }
            }

            var outputData = dst.ToBytes(".png");

            return Task.FromResult(OperatorExecutionOutput.Success(new Dictionary<string, object>
            {
                { "Image", outputData },
                { "Width", dst.Width },
                { "Height", dst.Height },
                { "Method", method },
                { "Channels", dst.Channels() }
            }));
        }
        catch (Exception ex)
        {
            return Task.FromResult(OperatorExecutionOutput.Failure($"直方图均衡化失败: {ex.Message}"));
        }
    }

    public ValidationResult ValidateParameters(Operator @operator)
    {
        var method = GetParameterValue(@operator, "Method", "Global").ToLower();
        var validMethods = new[] { "global", "clahe" };
        if (!validMethods.Contains(method))
            return ValidationResult.Invalid($"不支持的均衡化方法: {method}");

        var clipLimit = GetParameterValue(@operator, "ClipLimit", 2.0);
        if (clipLimit < 0 || clipLimit > 40)
            return ValidationResult.Invalid("裁剪限制必须在 0-40 之间");

        var tileSize = GetParameterValue(@operator, "TileSize", 8);
        if (tileSize < 2 || tileSize > 32)
            return ValidationResult.Invalid("网格大小必须在 2-32 之间");

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
