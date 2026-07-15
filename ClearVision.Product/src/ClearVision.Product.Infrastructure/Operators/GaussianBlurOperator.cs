// GaussianBlurOperator.cs
// 验证参数
// 作者：蘅芜君

using ClearVision.Product.Core.Attributes;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Operators;
using Microsoft.Extensions.Logging;
using OpenCvSharp;

namespace ClearVision.Product.Infrastructure.Operators;

/// <summary>
/// 高斯模糊算子
/// </summary>
[OperatorMeta(
    DisplayName = "滤波",
    Description = "统一空间平滑滤波入口，支持高斯、均值/Box、中值和双边滤波；默认保持历史高斯滤波行为。",
    CategoryId = OperatorCategoryId.ImagePreprocessing,
    IconName = "filter",
    Keywords = new[] { "gaussian", "mean", "box", "median", "bilateral", "blur", "filter", "denoise", "滤波" }
)]
[InputPort("Image", "Image", PortDataType.Image, IsRequired = true)]
[OutputPort("Image", "Image", PortDataType.Image)]
[OperatorParam("KernelSize", "Kernel Size", "int", DefaultValue = 5, Min = 1, Max = 31)]
[OperatorParam("SigmaX", "Sigma X", "double", DefaultValue = 1.0, Min = 0.1, Max = 10.0)]
[OperatorParam("SigmaY", "Sigma Y", "double", DefaultValue = 0.0, Min = 0.0, Max = 10.0)]
[OperatorParam(
    "BorderType",
    "Border Type",
    "enum",
    DefaultValue = "4",
    Options = new[] { "0|Constant", "1|Replicate", "2|Reflect", "3|Wrap", "4|Default" }
)]
[AlgorithmInfo(
    Name = "Gaussian Blur (OpenCV)",
    CoreApi = "Cv2.GaussianBlur",
    TimeComplexity = "O(W*H*K^2)",
    Dependencies = new[] { "OpenCvSharp" }
)]
public class GaussianBlurOperator : OperatorBase
{
    /// <summary>
    /// 算子类型
    /// </summary>
    public override OperatorType OperatorType => OperatorType.Filtering;

    /// <summary>
    /// 构造函数
    /// </summary>
    /// <param name="logger">日志记录器</param>
    public GaussianBlurOperator(ILogger<GaussianBlurOperator> logger) : base(logger)
    {
    }

    /// <summary>
    /// 执行核心逻辑
    /// </summary>
    protected override Task<OperatorExecutionOutput> ExecuteCoreAsync(
        Operator @operator,
        Dictionary<string, object>? inputs,
        CancellationToken cancellationToken)
    {
        // 获取输入图像
        if (!TryGetInputImage(inputs, "Image", out var imageWrapper) || imageWrapper == null)
        {
            return Task.FromResult(OperatorExecutionOutput.Failure("未提供输入图像"));
        }

        // 获取参数（使用基类方法）
        var kernelSize = GetIntParam(@operator, "KernelSize", 5, min: 1, max: 31);
        var sigmaX = GetDoubleParam(@operator, "SigmaX", 1.0);
        var sigmaY = GetDoubleParam(@operator, "SigmaY", 0.0); // 0表示与sigmaX相同
        var borderType = GetIntParam(@operator, "BorderType", 4, min: 0, max: 7); // 默认BORDER_DEFAULT

        // 确保核大小为奇数
        if (kernelSize % 2 == 0)
            kernelSize++;

        // 【优化】不再手动覆盖sigmaY。OpenCV本身支持sigmaY=0时自动使用sigmaX
        // 手动覆盖反而去除了使用不同sigmaX/sigmaY的灵活性

        var src = imageWrapper.GetMat();
        if (src.Empty())
        {
            return Task.FromResult(OperatorExecutionOutput.Failure("无法解码输入图像"));
        }

        var dst = new Mat();
        var borderMode = (BorderTypes)borderType;
        Cv2.GaussianBlur(src, dst, new Size(kernelSize, kernelSize), sigmaX, sigmaY, borderMode);

        // P0: 使用ImageWrapper实现零拷贝输出
        return Task.FromResult(OperatorExecutionOutput.Success(CreateImageOutput(dst)));
    }

    /// <summary>
    /// 验证参数
    /// </summary>
    public override ValidationResult ValidateParameters(Operator @operator)
    {
        var kernelSize = GetIntParam(@operator, "KernelSize", 5);
        if (kernelSize < 1 || kernelSize > 31)
        {
            return ValidationResult.Invalid("核大小必须在 1-31 之间");
        }
        return ValidationResult.Valid();
    }
}
