// BilateralFilterOperator.cs
// 双边滤波算子 - 边缘保留的平滑滤波
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
/// 双边滤波算子 - 边缘保留的平滑滤波
/// </summary>
[OperatorMeta(
    DisplayName = "双边滤波",
    Description = "边缘保留的平滑滤波",
    CategoryId = OperatorCategoryId.ImagePreprocessing,
    IconName = "filter",
    Keywords = new[] { "双边", "滤波", "边缘保留", "平滑", "纹理", "Bilateral", "Edge-preserving" },
    Version = "1.1.0"
)]
[OperatorImageContractProvider(typeof(SpatialFilterImageContractProvider))]
[OperatorGenerationDependency(typeof(SpatialFilterKernel))]
[InputPort("Image", "图像", PortDataType.Image, IsRequired = true)]
[OutputPort("Image", "图像", PortDataType.Image)]
[OperatorParam("Diameter", "直径", "int", DefaultValue = 9, Min = 1, Max = 25)]
[OperatorParam("SigmaColor", "色彩Sigma", "double", DefaultValue = 75.0, Min = 1.0, Max = 255.0)]
[OperatorParam("SigmaSpace", "空间Sigma", "double", DefaultValue = 75.0, Min = 1.0, Max = 255.0)]
public class BilateralFilterOperator : OperatorBase
{
    public override OperatorType OperatorType => OperatorType.BilateralFilter;

    public BilateralFilterOperator(ILogger<BilateralFilterOperator> logger) : base(logger)
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

        var diameter = GetIntParam(@operator, "Diameter", 9, min: 1, max: 25);
        var sigmaColor = GetDoubleParam(@operator, "SigmaColor", 75.0, min: 1, max: 255);
        var sigmaSpace = GetDoubleParam(@operator, "SigmaSpace", 75.0, min: 1, max: 255);

        var src = imageWrapper.GetMat();
        if (src.Empty())
        {
            return Task.FromResult(OperatorExecutionOutput.Failure("无法解码输入图像"));
        }

        var settings = new SpatialFilterSettings(
            SpatialFilterMode.Bilateral,
            BorderType: 4,
            Diameter: diameter,
            SigmaColor: sigmaColor,
            SigmaSpace: sigmaSpace);
        if (!SpatialFilterKernel.TryValidate(settings, out var validationError) ||
            !SpatialFilterKernel.TryValidateInput(src, settings, OperatorType.BilateralFilter, out validationError))
        {
            return Task.FromResult(OperatorExecutionOutput.Failure(validationError));
        }

        var dst = MatPool.Shared.Rent(src.Width, src.Height, src.Type());
        SpatialFilterAppliedSettings applied;
        try
        {
            applied = SpatialFilterKernel.Apply(src, dst, settings, OperatorType.BilateralFilter);
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
            ["DiameterRequested"] = diameter,
            ["DiameterApplied"] = applied.Diameter,
            ["SigmaColorApplied"] = applied.SigmaColor,
            ["SigmaSpaceApplied"] = applied.SigmaSpace
        })));
    }

    public override ValidationResult ValidateParameters(Operator @operator)
    {
        var diameter = GetIntParam(@operator, "Diameter", 9);
        var sigmaColor = GetDoubleParam(@operator, "SigmaColor", 75.0);
        var sigmaSpace = GetDoubleParam(@operator, "SigmaSpace", 75.0);

        var settings = new SpatialFilterSettings(
            SpatialFilterMode.Bilateral,
            BorderType: 4,
            Diameter: diameter,
            SigmaColor: sigmaColor,
            SigmaSpace: sigmaSpace);
        return SpatialFilterKernel.TryValidate(settings, out var error)
            ? ValidationResult.Valid()
            : ValidationResult.Invalid(error);
    }
}
