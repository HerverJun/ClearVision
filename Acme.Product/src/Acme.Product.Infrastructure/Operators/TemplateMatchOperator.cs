using Acme.Product.Core.Entities;
using Acme.Product.Core.Enums;
using Acme.Product.Core.Operators;
using OpenCvSharp;

namespace Acme.Product.Infrastructure.Operators;

/// <summary>
/// 模板匹配算子 - 在图像中查找模板位置
/// </summary>
public class TemplateMatchOperator : IOperatorExecutor
{
    public OperatorType OperatorType => OperatorType.TemplateMatching;

    public Task<OperatorExecutionOutput> ExecuteAsync(Operator @operator, Dictionary<string, object>? inputs = null)
    {
        try
        {
            if (inputs == null || !inputs.TryGetValue("Image", out var imgObj) || imgObj is not byte[] imageData)
            {
                return Task.FromResult(OperatorExecutionOutput.Failure("未提供输入图像"));
            }

            if (!inputs.TryGetValue("Template", out var templateObj) || templateObj is not byte[] templateData)
            {
                return Task.FromResult(OperatorExecutionOutput.Failure("未提供模板图像"));
            }

            // 获取参数
            var threshold = GetParameterValue(@operator, "Threshold", 0.8);
            var method = GetParameterValue(@operator, "Method", "CCoeffNormed");

            using var src = Cv2.ImDecode(imageData, ImreadModes.Color);
            using var template = Cv2.ImDecode(templateData, ImreadModes.Color);

            if (src.Empty() || template.Empty())
            {
                return Task.FromResult(OperatorExecutionOutput.Failure("无法解码图像"));
            }

            if (template.Width > src.Width || template.Height > src.Height)
            {
                return Task.FromResult(OperatorExecutionOutput.Failure("模板尺寸不能大于源图像"));
            }

            // 选择匹配方法
            var matchMethod = method.ToLower() switch
            {
                "sqdiff" => TemplateMatchModes.SqDiff,
                "sqdiffnormed" => TemplateMatchModes.SqDiffNormed,
                "ccorr" => TemplateMatchModes.CCorr,
                "ccorrnormed" => TemplateMatchModes.CCorrNormed,
                "ccoeff" => TemplateMatchModes.CCoeff,
                "ccoeffnormed" => TemplateMatchModes.CCoeffNormed,
                _ => TemplateMatchModes.CCoeffNormed
            };

            // 执行模板匹配
            using var result = new Mat();
            Cv2.MatchTemplate(src, template, result, matchMethod);

            // 查找最佳匹配位置
            Cv2.MinMaxLoc(result, out double minVal, out double maxVal, out Point minLoc, out Point maxLoc);

            // 根据方法确定最佳匹配值和位置
            bool isSqDiff = matchMethod == TemplateMatchModes.SqDiff || matchMethod == TemplateMatchModes.SqDiffNormed;
            double matchValue = isSqDiff ? minVal : maxVal;
            Point matchLoc = isSqDiff ? minLoc : maxLoc;

            // 归一化匹配值到0-1范围
            double normalizedScore = isSqDiff ? 1.0 - matchValue : matchValue;

            // 绘制结果
            using var resultImg = src.Clone();
            bool found = normalizedScore >= threshold;
            
            if (found)
            {
                Cv2.Rectangle(resultImg, matchLoc, new Point(matchLoc.X + template.Width, matchLoc.Y + template.Height),
                    new Scalar(0, 255, 0), 2);
                Cv2.PutText(resultImg, $"Score: {normalizedScore:F3}",
                    new Point(matchLoc.X, matchLoc.Y - 10),
                    HersheyFonts.HersheySimplex, 0.6, new Scalar(0, 255, 0), 2);
            }

            var outputData = resultImg.ToBytes(".png");

            return Task.FromResult(OperatorExecutionOutput.Success(new Dictionary<string, object>
            {
                { "Image", outputData },
                { "Width", resultImg.Width },
                { "Height", resultImg.Height },
                { "Found", found },
                { "Score", normalizedScore },
                { "X", matchLoc.X },
                { "Y", matchLoc.Y },
                { "TemplateWidth", template.Width },
                { "TemplateHeight", template.Height }
            }));
        }
        catch (Exception ex)
        {
            return Task.FromResult(OperatorExecutionOutput.Failure($"模板匹配失败: {ex.Message}"));
        }
    }

    public ValidationResult ValidateParameters(Operator @operator)
    {
        var threshold = GetParameterValue(@operator, "Threshold", 0.8);
        if (threshold < 0 || threshold > 1)
        {
            return ValidationResult.Invalid("阈值必须在 0-1 之间");
        }

        var method = GetParameterValue(@operator, "Method", "CCoeffNormed").ToLower();
        var validMethods = new[] { "sqdiff", "sqdiffnormed", "ccorr", "ccorrnormed", "ccoeff", "ccoeffnormed" };
        if (!validMethods.Contains(method))
        {
            return ValidationResult.Invalid($"不支持的匹配方法: {method}");
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
