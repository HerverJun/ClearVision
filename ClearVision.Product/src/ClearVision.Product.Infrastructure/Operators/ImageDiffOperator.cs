// ImageDiffOperator.cs
// 图像差异率分析算子实现
// 作者：蘅芜君

using ClearVision.Product.Core.Attributes;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Operators;
using ClearVision.Product.Core.ValueObjects;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
namespace ClearVision.Product.Infrastructure.Operators;

/// <summary>
/// 图像差异率分析算子
/// </summary>
[OperatorMeta(
    DisplayName = "图像差异率分析",
    Description = "计算两幅同尺寸图像的绝对差异图，并输出非零差异像素占比。",
    Category = "预处理",
    IconName = "diff",
    Keywords = new[] { "image diff", "difference rate", "absolute difference", "图像对比" },
    Version = "1.0.1"
)]
[InputPort("BaseImage", "基准图", PortDataType.Image, IsRequired = true)]
[InputPort("CompareImage", "对比图", PortDataType.Image, IsRequired = true)]
[OutputPort("DiffImage", "差异图", PortDataType.Image)]
[OutputPort("DiffRate", "差异率", PortDataType.Float)]
public class ImageDiffOperator : OperatorBase
{
    public override OperatorType OperatorType => OperatorType.ImageDiff;

    public ImageDiffOperator(ILogger<ImageDiffOperator> logger) : base(logger)
    {
    }

    protected override Task<OperatorExecutionOutput> ExecuteCoreAsync(
        Operator @operator,
        Dictionary<string, object>? inputs,
        CancellationToken cancellationToken)
    {
        if (!TryGetInputImage(inputs, "BaseImage", out var imgA) || imgA == null)
            return Task.FromResult(OperatorExecutionOutput.Failure("基准图像不能为空"));

        if (!TryGetInputImage(inputs, "CompareImage", out var imgB) || imgB == null)
            return Task.FromResult(OperatorExecutionOutput.Failure("对比图像不能为空"));

        var matA = imgA.GetMat();
        var matB = imgB.GetMat();

        if (matA.Size() != matB.Size())
            return Task.FromResult(OperatorExecutionOutput.Failure("算子输入图像尺寸不一致"));

        using var diff = new Mat();
        Cv2.Absdiff(matA, matB, diff);

        using var grayDiff = diff.Channels() > 1 ? diff.CvtColor(ColorConversionCodes.BGR2GRAY) : diff.Clone();
        double diffRate = (double)Cv2.CountNonZero(grayDiff) / (matA.Width * matA.Height);

        var output = CreateImageOutput(diff.Clone());
        output["DiffRate"] = diffRate;

        return Task.FromResult(OperatorExecutionOutput.Success(output));
    }

    public override ValidationResult ValidateParameters(Operator @operator)
    {
        return ValidationResult.Valid();
    }
}
