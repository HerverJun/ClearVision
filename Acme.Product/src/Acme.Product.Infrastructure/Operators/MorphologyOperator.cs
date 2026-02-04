using Acme.Product.Core.Entities;
using Acme.Product.Core.Enums;
using Acme.Product.Core.Operators;
using OpenCvSharp;

namespace Acme.Product.Infrastructure.Operators;

/// <summary>
/// 形态学算子 - 支持腐蚀、膨胀、开运算、闭运算
/// </summary>
public class MorphologyOperator : IOperatorExecutor
{
    public OperatorType OperatorType => OperatorType.Morphology;

    public Task<OperatorExecutionOutput> ExecuteAsync(Operator @operator, Dictionary<string, object>? inputs = null)
    {
        try
        {
            if (inputs == null || !inputs.TryGetValue("Image", out var imgObj) || imgObj is not byte[] imageData)
            {
                return Task.FromResult(OperatorExecutionOutput.Failure("未提供输入图像"));
            }

            // 获取参数
            var operation = GetParameterValue(@operator, "Operation", "Erode");
            var kernelSize = GetParameterValue(@operator, "KernelSize", 3);
            var kernelShape = GetParameterValue(@operator, "KernelShape", "Rect"); // Rect, Ellipse, Cross
            var iterations = GetParameterValue(@operator, "Iterations", 1);
            var anchorX = GetParameterValue(@operator, "AnchorX", -1); // -1表示中心
            var anchorY = GetParameterValue(@operator, "AnchorY", -1);

            using var src = Cv2.ImDecode(imageData, ImreadModes.Color);
            if (src.Empty())
            {
                return Task.FromResult(OperatorExecutionOutput.Failure("无法解码输入图像"));
            }

            // 解析核形状
            var shape = kernelShape.ToLower() switch
            {
                "rect" => MorphShapes.Rect,
                "ellipse" => MorphShapes.Ellipse,
                "cross" => MorphShapes.Cross,
                _ => MorphShapes.Rect
            };

            // 创建结构元素，支持自定义锚点
            var anchor = new Point(anchorX, anchorY);
            using var kernel = Cv2.GetStructuringElement(shape, new Size(kernelSize, kernelSize), anchor);
            using var dst = new Mat();

            // 执行形态学操作
            var morphOp = operation.ToLower() switch
            {
                "erode" => MorphTypes.Erode,
                "dilate" => MorphTypes.Dilate,
                "open" => MorphTypes.Open,
                "close" => MorphTypes.Close,
                "gradient" => MorphTypes.Gradient,
                "tophat" => MorphTypes.TopHat,
                "blackhat" => MorphTypes.BlackHat,
                _ => MorphTypes.Erode
            };

            Cv2.MorphologyEx(src, dst, morphOp, kernel, iterations: iterations);

            var outputData = dst.ToBytes(".png");

            return Task.FromResult(OperatorExecutionOutput.Success(new Dictionary<string, object>
            {
                { "Image", outputData },
                { "Width", dst.Width },
                { "Height", dst.Height },
                { "Operation", operation }
            }));
        }
        catch (Exception ex)
        {
            return Task.FromResult(OperatorExecutionOutput.Failure($"形态学操作失败: {ex.Message}"));
        }
    }

    public ValidationResult ValidateParameters(Operator @operator)
    {
        var kernelSize = GetParameterValue(@operator, "KernelSize", 3);
        if (kernelSize < 1 || kernelSize > 21)
        {
            return ValidationResult.Invalid("核大小必须在 1-21 之间");
        }

        var iterations = GetParameterValue(@operator, "Iterations", 1);
        if (iterations < 1 || iterations > 10)
        {
            return ValidationResult.Invalid("迭代次数必须在 1-10 之间");
        }

        var operation = GetParameterValue(@operator, "Operation", "Erode").ToLower();
        var validOperations = new[] { "erode", "dilate", "open", "close", "gradient", "tophat", "blackhat" };
        if (!validOperations.Contains(operation))
        {
            return ValidationResult.Invalid($"不支持的操作类型: {operation}");
        }

        var kernelShape = GetParameterValue(@operator, "KernelShape", "Rect").ToLower();
        var validShapes = new[] { "rect", "ellipse", "cross" };
        if (!validShapes.Contains(kernelShape))
        {
            return ValidationResult.Invalid($"不支持的核形状: {kernelShape}");
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
