// LaplacianSharpenOperator.cs
// 拉普拉斯锐化算子 - 边缘增强
// 作者：蘅芜君

using ClearVision.Product.Core.Attributes;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Operators;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
namespace ClearVision.Product.Infrastructure.Operators;

/// <summary>
/// 拉普拉斯锐化算子 - 边缘增强
/// 【第三优先级】图像预处理算子扩展
/// </summary>
[OperatorMeta(
    DisplayName = "拉普拉斯锐化",
    Description = "在浮点域保留拉普拉斯响应符号，并按 dst = src - strength × laplacian 锐化。",
    CategoryId = OperatorCategoryId.ImagePreprocessing,
    IconName = "sharpen",
    Keywords = new[] { "laplacian", "sharpen", "signed response", "edge enhancement" },
    Version = "1.0.2"
)]
[OperatorParameterRule("KernelSize", ReasonCode = "LAPLACIAN_KERNEL_SIZE")]
[OperatorParameterRule("Scale", ReasonCode = "LAPLACIAN_SCALE")]
[OperatorParameterRule("SharpenStrength", ReasonCode = "LAPLACIAN_SHARPEN_STRENGTH")]
[OperatorOutputRule("Image", ReasonCode = "LAPLACIAN_SHARPEN_OUTPUT")]
[OperatorOutputRule("KernelSize", ReasonCode = "LAPLACIAN_SHARPEN_OUTPUT")]
[OperatorOutputRule("Scale", ReasonCode = "LAPLACIAN_SHARPEN_OUTPUT")]
[OperatorOutputRule("SharpenStrength", ReasonCode = "LAPLACIAN_SHARPEN_OUTPUT")]
[OperatorOutputRule("OutputMatType", ReasonCode = "LAPLACIAN_SHARPEN_OUTPUT")]
[OperatorOutputRule("ColorPolicy", ReasonCode = "LAPLACIAN_SHARPEN_OUTPUT")]
[InputPort("Image", "图像", PortDataType.Image, IsRequired = true)]
[OutputPort("Image", "锐化图像", PortDataType.Image)]
[OutputPort("KernelSize", "实际核大小", PortDataType.Integer)]
[OutputPort("Scale", "实际拉普拉斯缩放", PortDataType.Float)]
[OutputPort("SharpenStrength", "实际锐化强度", PortDataType.Float)]
[OutputPort("OutputMatType", "输出 Mat 类型", PortDataType.String)]
[OutputPort("ColorPolicy", "彩色图策略", PortDataType.String)]
[OperatorParam("KernelSize", "核大小", "int", Description = "范围 1-7；偶数按兼容规则向上规范化为下一奇数，并在输出元数据返回实际值。", DefaultValue = 3, Min = 1, Max = 7)]
[OperatorParam("Scale", "缩放因子", "double", Description = "缩放有符号 Laplacian 响应。", DefaultValue = 1.0, Min = 0.1, Max = 10.0)]
[OperatorParam("SharpenStrength", "锐化强度", "double", Description = "公式 dst = src - SharpenStrength × laplacian；0 为严格恒等。", DefaultValue = 1.0, Min = 0, Max = 5.0)]
[AlgorithmInfo(
    Name = "Signed Laplacian sharpening",
    CoreApi = "Cv2.CvtColor / Cv2.Laplacian / Cv2.AddWeighted / Mat.ConvertTo",
    ImplementationStrategy = "Compute a signed Laplacian in CV_32F, apply dst=src-SharpenStrength*laplacian, then explicitly saturate-convert to the input depth. Three-channel images compute luminance response and broadcast the signed correction to BGR channels.",
    TimeComplexity = "O(W*H*K^2)",
    SpaceComplexity = "O(W*H*C)",
    SuitableUseCases = new[] { "Second-derivative sharpening for grayscale and BGR industrial images" },
    KnownLimitations = new[] { "Supported input depths are 8U, 16U and 32F", "Color sharpening uses a luminance-broadcast correction rather than independent channel Laplacians" },
    Dependencies = new[] { "OpenCvSharp" }
)]
public class LaplacianSharpenOperator : OperatorBase
{
    public override OperatorType OperatorType => OperatorType.LaplacianSharpen;

    public LaplacianSharpenOperator(ILogger<LaplacianSharpenOperator> logger) : base(logger)
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

        // 获取参数
        var kernelSize = NormalizeKernelSize(GetIntParam(@operator, "KernelSize", 3, min: 1, max: 7));
        var scale = GetDoubleParam(@operator, "Scale", 1.0, min: 0.1, max: 10.0);
        var sharpenStrength = GetDoubleParam(@operator, "SharpenStrength", 1.0, min: 0, max: 5.0);

        var src = imageWrapper.GetMat();
        if (src.Empty())
        {
            return Task.FromResult(OperatorExecutionOutput.Failure("无法解码输入图像"));
        }

        if (src.Channels() is not (1 or 3))
        {
            return Task.FromResult(OperatorExecutionOutput.Failure("Only 1-channel and 3-channel images are supported"));
        }

        if (src.Depth() is not (MatType.CV_8U or MatType.CV_16U or MatType.CV_32F))
        {
            return Task.FromResult(OperatorExecutionOutput.Failure("Supported image depths are CV_8U, CV_16U and CV_32F"));
        }

        var colorPolicy = src.Channels() == 3 ? "LuminanceBroadcast" : "SingleChannel";
        Mat dst;
        if (sharpenStrength <= 0.0)
        {
            dst = src.Clone();
        }
        else
        {
            dst = ApplySignedLaplacianSharpen(src, kernelSize, scale, sharpenStrength);
        }

        return Task.FromResult(OperatorExecutionOutput.Success(CreateImageOutput(dst, new Dictionary<string, object>
        {
            { "KernelSize", kernelSize },
            { "Scale", scale },
            { "SharpenStrength", sharpenStrength },
            { "OutputMatType", dst.Type().ToString() },
            { "ColorPolicy", colorPolicy }
        })));
    }

    private static Mat ApplySignedLaplacianSharpen(Mat src, int kernelSize, double scale, double sharpenStrength)
    {
        var workingDepth = MatType.CV_32F;
        var workingType = MatType.MakeType(workingDepth, src.Channels());
        using var srcFloat = new Mat();
        src.ConvertTo(srcFloat, workingType);
        using var signedLaplacian = new Mat();
        using var sharpenedFloat = new Mat();

        if (src.Channels() == 3)
        {
            using var gray = new Mat();
            Cv2.CvtColor(src, gray, ColorConversionCodes.BGR2GRAY);
            using var grayFloat = new Mat();
            gray.ConvertTo(grayFloat, workingDepth);
            Cv2.Laplacian(grayFloat, signedLaplacian, workingDepth, kernelSize, scale, 0, BorderTypes.Default);

            using var laplacian3C = new Mat();
            Cv2.CvtColor(signedLaplacian, laplacian3C, ColorConversionCodes.GRAY2BGR);
            Cv2.AddWeighted(srcFloat, 1.0, laplacian3C, -sharpenStrength, 0, sharpenedFloat);
        }
        else
        {
            Cv2.Laplacian(srcFloat, signedLaplacian, workingDepth, kernelSize, scale, 0, BorderTypes.Default);
            Cv2.AddWeighted(srcFloat, 1.0, signedLaplacian, -sharpenStrength, 0, sharpenedFloat);
        }

        var dst = new Mat();
        sharpenedFloat.ConvertTo(dst, src.Type());
        return dst;
    }

    public override ValidationResult ValidateParameters(Operator @operator)
    {
        var kernelSize = GetIntParam(@operator, "KernelSize", 3);
        if (kernelSize < 1 || kernelSize > 7)
            return ValidationResult.Invalid("核大小必须在 1-7 之间");

        var scale = GetDoubleParam(@operator, "Scale", 1.0);
        if (scale < 0.1 || scale > 10.0)
            return ValidationResult.Invalid("缩放因子必须在 0.1-10.0 之间");

        var sharpenStrength = GetDoubleParam(@operator, "SharpenStrength", 1.0);
        if (sharpenStrength < 0 || sharpenStrength > 5.0)
            return ValidationResult.Invalid("锐化强度必须在 0-5.0 之间");

        return ValidationResult.Valid();
    }

    private static int NormalizeKernelSize(int kernelSize)
    {
        return kernelSize % 2 == 0 ? kernelSize + 1 : kernelSize;
    }
}
