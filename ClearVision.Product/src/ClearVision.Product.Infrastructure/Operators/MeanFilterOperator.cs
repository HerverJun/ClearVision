// MeanFilterOperator.cs
// 均值滤波算子
// 使用均值核对图像进行平滑降噪处理
// 作者：蘅芜君
using ClearVision.Product.Core.Attributes;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Operators;
using Microsoft.Extensions.Logging;
using OpenCvSharp;

namespace ClearVision.Product.Infrastructure.Operators;

/// <summary>
/// Applies a mean (box blur) filter to the input image.
/// </summary>
[OperatorMeta(
    DisplayName = "均值滤波",
    Description = "使用均值（方框）滤波平滑图像噪声。",
    CategoryId = OperatorCategoryId.ImagePreprocessing,
    IconName = "filter",
    Keywords = new[] { "mean filter", "box blur", "box filter", "smooth", "denoise" },
    Version = "1.1.0"
)]
[OperatorImageContractProvider(typeof(SpatialFilterImageContractProvider))]
[OperatorGenerationDependency(typeof(SpatialFilterKernel))]
[InputPort("Image", "Image", PortDataType.Image, IsRequired = true)]
[OutputPort("Image", "Image", PortDataType.Image)]
[OperatorParam("KernelSize", "Kernel Size", "int", DefaultValue = 5, Min = 1, Max = 63)]
[OperatorParam(
    "BorderType",
    "Border Type",
    "enum",
    DefaultValue = "4",
    Options = new[] { "0|Constant", "1|Replicate", "2|Reflect", "3|Wrap", "4|Default" }
)]
public class MeanFilterOperator : OperatorBase
{
    public override OperatorType OperatorType => OperatorType.MeanFilter;

    public MeanFilterOperator(ILogger<MeanFilterOperator> logger) : base(logger)
    {
    }

    protected override Task<OperatorExecutionOutput> ExecuteCoreAsync(
        Operator @operator,
        Dictionary<string, object>? inputs,
        CancellationToken cancellationToken)
    {
        if (!TryGetInputImage(inputs, "Image", out var imageWrapper) || imageWrapper == null)
        {
            return Task.FromResult(OperatorExecutionOutput.Failure("No input image provided"));
        }

        var kernelSize = GetIntParam(@operator, "KernelSize", 5, min: 1, max: 63);
        var borderType = GetIntParam(@operator, "BorderType", 4, min: 0, max: 7);

        var src = imageWrapper.GetMat();
        if (src.Empty())
        {
            return Task.FromResult(OperatorExecutionOutput.Failure("Input image is invalid"));
        }

        var settings = new SpatialFilterSettings(
            SpatialFilterMode.Mean,
            KernelSize: kernelSize,
            BorderType: borderType);
        if (!SpatialFilterKernel.TryValidate(settings, out var validationError) ||
            !SpatialFilterKernel.TryValidateInput(src, settings, OperatorType.MeanFilter, out validationError))
        {
            return Task.FromResult(OperatorExecutionOutput.Failure(validationError));
        }

        var dst = new Mat();
        SpatialFilterAppliedSettings applied;
        try
        {
            applied = SpatialFilterKernel.Apply(src, dst, settings, OperatorType.MeanFilter);
        }
        catch
        {
            dst.Dispose();
            throw;
        }

        return Task.FromResult(OperatorExecutionOutput.Success(CreateImageOutput(dst, new Dictionary<string, object>
        {
            ["FilterMode"] = applied.Mode.ToString(),
            ["KernelSizeApplied"] = applied.KernelSize,
            ["BorderTypeApplied"] = applied.BorderType
        })));
    }

    public override ValidationResult ValidateParameters(Operator @operator)
    {
        var kernelSize = GetIntParam(@operator, "KernelSize", 5);
        var settings = new SpatialFilterSettings(
            SpatialFilterMode.Mean,
            KernelSize: kernelSize,
            BorderType: GetIntParam(@operator, "BorderType", 4));
        return SpatialFilterKernel.TryValidate(settings, out var error)
            ? ValidationResult.Valid()
            : ValidationResult.Invalid(error);
    }
}
