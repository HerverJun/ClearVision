// MedianBlurOperator.cs
// 中值滤波算子 - 有效去除椒盐噪声同时保留边缘
// 作者：蘅芜君

using ClearVision.Product.Core.Attributes;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Operators;
using ClearVision.Product.Infrastructure.Memory;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
namespace ClearVision.Product.Infrastructure.Operators;

/// <summary>
/// 中值滤波算子 - 有效去除椒盐噪声同时保留边缘
/// </summary>
[OperatorMeta(
    DisplayName = "中值滤波",
    Description = "有效去除椒盐噪声同时保留边缘",
    CategoryId = OperatorCategoryId.ImagePreprocessing,
    IconName = "filter",
    Keywords = new[] { "中值", "滤波", "椒盐噪声", "去噪", "Median", "Filter", "Salt and pepper" },
    Version = "1.1.0"
)]
[OperatorImageContractProvider(typeof(SpatialFilterImageContractProvider))]
[OperatorGenerationDependency(typeof(SpatialFilterKernel))]
[InputPort("Image", "图像", PortDataType.Image, IsRequired = true)]
[OutputPort("Image", "图像", PortDataType.Image)]
[OperatorParam("KernelSize", "核大小", "int", DefaultValue = 5, Min = 1, Max = 31)]
public class MedianBlurOperator : OperatorBase
{
    public override OperatorType OperatorType => OperatorType.MedianBlur;

    public MedianBlurOperator(ILogger<MedianBlurOperator> logger) : base(logger)
    {
    }

    protected override Task<OperatorExecutionOutput> ExecuteCoreAsync(
        Operator @operator,
        Dictionary<string, object>? inputs,
        CancellationToken cancellationToken)
    {
        if (!TryGetInputImage(inputs, "Image", out var imageWrapper) || imageWrapper == null)
        {
            return Task.FromResult(OperatorExecutionOutput.Failure("未提供输入图像"));
        }

        var kernelSize = GetIntParam(@operator, "KernelSize", 5, min: 1, max: 31);

        var src = imageWrapper.GetMat();
        if (src.Empty())
        {
            return Task.FromResult(OperatorExecutionOutput.Failure("无法解码输入图像"));
        }

        var settings = new SpatialFilterSettings(
            SpatialFilterMode.Median,
            KernelSize: kernelSize);
        if (!SpatialFilterKernel.TryValidate(settings, out var validationError) ||
            !SpatialFilterKernel.TryValidateInput(src, settings, OperatorType.MedianBlur, out validationError))
        {
            return Task.FromResult(OperatorExecutionOutput.Failure(validationError));
        }

        var dst = MatPool.Shared.Rent(src.Width, src.Height, src.Type());
        SpatialFilterAppliedSettings applied;
        try
        {
            applied = SpatialFilterKernel.Apply(src, dst, settings, OperatorType.MedianBlur);
        }
        catch
        {
            MatPool.Shared.Return(dst);
            throw;
        }

        // P0: 使用ImageWrapper实现零拷贝输出
        return Task.FromResult(OperatorExecutionOutput.Success(CreateImageOutput(dst, new Dictionary<string, object>
        {
            ["FilterMode"] = applied.Mode.ToString(),
            ["KernelSizeApplied"] = applied.KernelSize
        })));
    }

    public override ValidationResult ValidateParameters(Operator @operator)
    {
        var kernelSize = GetIntParam(@operator, "KernelSize", 5);
        var settings = new SpatialFilterSettings(SpatialFilterMode.Median, KernelSize: kernelSize);
        return SpatialFilterKernel.TryValidate(settings, out var error)
            ? ValidationResult.Valid()
            : ValidationResult.Invalid(error);
    }
}
