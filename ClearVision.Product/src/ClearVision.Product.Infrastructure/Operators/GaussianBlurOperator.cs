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
    Category = "Filtering",
    IconName = "filter",
    Keywords = new[] { "gaussian", "mean", "box", "median", "bilateral", "blur", "filter", "denoise", "滤波" },
    Version = "1.1.0"
)]
[InputPort("Image", "Image", PortDataType.Image, IsRequired = true)]
[OutputPort("Image", "Image", PortDataType.Image)]
[OutputPort("FilterMode", "实际滤波模式", PortDataType.String)]
[OutputPort("FilterDiagnostics", "滤波诊断", PortDataType.Any)]
[OperatorParam("FilterMode", "滤波模式", "enum", Description = "默认 Gaussian 保持旧流程行为。", DefaultValue = "Gaussian", Options = new[] { "Gaussian|高斯滤波", "Mean|均值/Box滤波", "Median|中值滤波", "Bilateral|双边滤波" })]
[OperatorParam("KernelSize", "Kernel Size", "int", Description = "Gaussian/Mean/Median 使用；偶数会向上调整为奇数。", DefaultValue = 5, Min = 1, Max = 63)]
[OperatorParam("SigmaX", "Sigma X", "double", DefaultValue = 1.0, Min = 0.1, Max = 10.0)]
[OperatorParam("SigmaY", "Sigma Y", "double", DefaultValue = 0.0, Min = 0.0, Max = 10.0)]
[OperatorParam(
    "BorderType",
    "Border Type",
    "enum",
    DefaultValue = "4",
    Options = new[] { "0|Constant", "1|Replicate", "2|Reflect", "3|Wrap", "4|Default" }
)]
[OperatorParam("Diameter", "双边直径", "int", DefaultValue = 9, Min = 1, Max = 25)]
[OperatorParam("SigmaColor", "双边色彩Sigma", "double", DefaultValue = 75.0, Min = 1.0, Max = 255.0)]
[OperatorParam("SigmaSpace", "双边空间Sigma", "double", DefaultValue = 75.0, Min = 1.0, Max = 255.0)]
[AlgorithmInfo(
    Name = "Unified spatial smoothing filters (OpenCV)",
    CoreApi = "Cv2.GaussianBlur / Cv2.Blur / Cv2.MedianBlur / Cv2.BilateralFilter",
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

        var modeRaw = GetStringParam(@operator, "FilterMode", "Gaussian");
        if (!SpatialFilterKernel.TryParseMode(modeRaw, out var mode))
        {
            return Task.FromResult(OperatorExecutionOutput.Failure(
                "FilterMode must be Gaussian, Mean, Box, Median or Bilateral."));
        }

        var kernelSize = GetIntParam(@operator, "KernelSize", 5, min: 1, max: 63);
        var sigmaX = GetDoubleParam(@operator, "SigmaX", 1.0);
        var sigmaY = GetDoubleParam(@operator, "SigmaY", 0.0);
        var borderType = GetIntParam(@operator, "BorderType", 4, min: 0, max: 7);
        var diameter = GetIntParam(@operator, "Diameter", 9, min: 1, max: 25);
        var sigmaColor = GetDoubleParam(@operator, "SigmaColor", 75.0, min: 1, max: 255);
        var sigmaSpace = GetDoubleParam(@operator, "SigmaSpace", 75.0, min: 1, max: 255);

        var settings = new SpatialFilterSettings(
            mode,
            kernelSize,
            sigmaX,
            sigmaY,
            borderType,
            diameter,
            sigmaColor,
            sigmaSpace);
        if (!SpatialFilterKernel.TryValidate(settings, out var validationError))
        {
            return Task.FromResult(OperatorExecutionOutput.Failure(validationError));
        }

        var src = imageWrapper.GetMat();
        if (src.Empty())
        {
            return Task.FromResult(OperatorExecutionOutput.Failure("无法解码输入图像"));
        }

        var dst = new Mat();
        SpatialFilterAppliedSettings applied;
        try
        {
            applied = SpatialFilterKernel.Apply(src, dst, settings);
        }
        catch
        {
            dst.Dispose();
            throw;
        }

        var diagnostics = new Dictionary<string, object>
        {
            ["Mode"] = applied.Mode.ToString(),
            ["KernelSizeApplied"] = applied.KernelSize,
            ["BorderTypeApplied"] = applied.BorderType,
            ["SigmaXApplied"] = applied.SigmaX,
            ["SigmaYApplied"] = applied.SigmaY,
            ["DiameterApplied"] = applied.Diameter,
            ["SigmaColorApplied"] = applied.SigmaColor,
            ["SigmaSpaceApplied"] = applied.SigmaSpace
        };
        return Task.FromResult(OperatorExecutionOutput.Success(CreateImageOutput(dst, new Dictionary<string, object>
        {
            ["FilterMode"] = applied.Mode.ToString(),
            ["FilterDiagnostics"] = diagnostics,
            ["KernelSizeApplied"] = applied.KernelSize,
            ["BorderTypeApplied"] = applied.BorderType
        })));
    }

    /// <summary>
    /// 验证参数
    /// </summary>
    public override ValidationResult ValidateParameters(Operator @operator)
    {
        if (!SpatialFilterKernel.TryParseMode(GetStringParam(@operator, "FilterMode", "Gaussian"), out var mode))
        {
            return ValidationResult.Invalid("FilterMode must be Gaussian, Mean, Box, Median or Bilateral.");
        }

        var settings = new SpatialFilterSettings(
            mode,
            GetIntParam(@operator, "KernelSize", 5),
            GetDoubleParam(@operator, "SigmaX", 1.0),
            GetDoubleParam(@operator, "SigmaY", 0.0),
            GetIntParam(@operator, "BorderType", 4),
            GetIntParam(@operator, "Diameter", 9),
            GetDoubleParam(@operator, "SigmaColor", 75.0),
            GetDoubleParam(@operator, "SigmaSpace", 75.0));

        return SpatialFilterKernel.TryValidate(settings, out var error)
            ? ValidationResult.Valid()
            : ValidationResult.Invalid(error);
    }
}
