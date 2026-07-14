using ClearVision.Product.Core.Attributes;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Operators;
using Microsoft.Extensions.Logging;
using OpenCvSharp;

namespace ClearVision.Product.Infrastructure.Operators;

[OperatorMeta(
    DisplayName = "全局阈值处理",
    Description = "执行全局阈值处理，支持二值、反二值、截断、ToZero 以及 Otsu/Triangle 自动阈值。",
    CategoryId = OperatorCategoryId.SegmentationAndRegion,
    IconName = "threshold",
    Keywords = new[] { "threshold", "binarize", "segmentation", "otsu", "triangle", "二值化", "Threshold" },
    Version = "1.1.0"
)]
[OperatorImageContractProvider(typeof(ThresholdImageContractProvider))]
[InputPort("Image", "Image", PortDataType.Image, IsRequired = true)]
[OutputPort("Image", "Image", PortDataType.Image)]
[OperatorParameterRule("UseOtsu", Deprecated = true, ReasonCode = "THRESHOLD_USE_OTSU_COMPATIBILITY_ALIAS")]
[OperatorParam("Threshold", "Threshold", "double", Description = "输入像素数值域中的阈值；合法范围在运行时按 Mat 位深校验。", DefaultValue = 127.0)]
[OperatorParam("MaxValue", "Max Value", "double", Description = "输出像素数值域中的最大值；合法范围在运行时按 Mat 位深校验。", DefaultValue = 255.0)]
[OperatorParam("Type", "Type", "enum", DefaultValue = "0", Options = new[] { "0|Binary", "1|Binary Inv", "2|Trunc", "3|To Zero", "4|To Zero Inv", "8|Otsu", "16|Triangle" })]
[OperatorParam("UseOtsu", "Use Otsu", "bool", Description = "兼容旧工程的 Otsu 标志；true 时向 Type 添加 Otsu，不覆盖基础 Binary/BinaryInv 模式。", DefaultValue = false)]
public class ThresholdOperator : OperatorBase
{
    public override OperatorType OperatorType => OperatorType.Thresholding;

    public ThresholdOperator(ILogger<ThresholdOperator> logger) : base(logger)
    {
    }

    protected override Task<OperatorExecutionOutput> ExecuteCoreAsync(
        Operator @operator,
        Dictionary<string, object>? inputs,
        CancellationToken cancellationToken)
    {
        if (!TryGetInputImage(inputs, "Image", out var imageWrapper) || imageWrapper == null)
        {
            return Task.FromResult(OperatorExecutionOutput.Failure("No input image provided."));
        }

        var threshold = GetDoubleParam(@operator, "Threshold", 127.0);
        var maxValue = GetDoubleParam(@operator, "MaxValue", 255.0);
        var typeValue = GetIntParam(@operator, "Type", 0);
        var useOtsu = GetBoolParam(@operator, "UseOtsu", false);

        var src = imageWrapper.GetMat();
        if (src.Empty())
        {
            return Task.FromResult(OperatorExecutionOutput.Failure("Input image is invalid."));
        }

        if (!TryResolveThresholdType(typeValue, useOtsu, out var thresholdType, out var thresholdError))
        {
            return Task.FromResult(OperatorExecutionOutput.Failure(thresholdError));
        }

        if (!TryValidateImageContract(src, thresholdType, threshold, maxValue, out var imageContractError))
        {
            return Task.FromResult(OperatorExecutionOutput.Failure(imageContractError));
        }

        using var gray = new Mat();
        if (src.Channels() == 3)
        {
            Cv2.CvtColor(src, gray, ColorConversionCodes.BGR2GRAY);
        }
        else if (src.Channels() == 4)
        {
            Cv2.CvtColor(src, gray, ColorConversionCodes.BGRA2GRAY);
        }
        else
        {
            src.CopyTo(gray);
        }

        using var binary = new Mat();
        var actualThreshold = Cv2.Threshold(gray, binary, threshold, maxValue, thresholdType);

        var additionalData = new Dictionary<string, object>
        {
            ["ActualThreshold"] = actualThreshold,
            ["InputMatType"] = src.Type().ToString(),
            ["OutputMatType"] = binary.Type().ToString(),
            ["OutputDepthPolicy"] = "PreserveAdmittedGrayDepth",
            ["ColorConversion"] = src.Channels() switch
            {
                3 => "BGR_TO_GRAY",
                4 => "BGRA_TO_GRAY",
                _ => "None"
            }
        };

        if ((thresholdType & ThresholdTypes.Otsu) == ThresholdTypes.Otsu)
        {
            additionalData["OtsuThreshold"] = actualThreshold;
        }

        return Task.FromResult(OperatorExecutionOutput.Success(CreateImageOutput(binary.Clone(), additionalData)));
    }

    public override ValidationResult ValidateParameters(Operator @operator)
    {
        var threshold = GetDoubleParam(@operator, "Threshold", 127.0);
        if (!double.IsFinite(threshold))
        {
            return ValidationResult.Invalid("Threshold must be finite; its legal range is validated against the input Mat depth at runtime.");
        }

        var maxValue = GetDoubleParam(@operator, "MaxValue", 255.0);
        if (!double.IsFinite(maxValue))
        {
            return ValidationResult.Invalid("MaxValue must be finite; its legal range is validated against the input Mat depth at runtime.");
        }

        var typeValue = GetIntParam(@operator, "Type", 0);
        var useOtsu = GetBoolParam(@operator, "UseOtsu", false);
        if (!TryResolveThresholdType(typeValue, useOtsu, out _, out var thresholdError))
        {
            return ValidationResult.Invalid(thresholdError);
        }

        return ValidationResult.Valid();
    }

    private static bool TryResolveThresholdType(
        int typeValue,
        bool useOtsu,
        out ThresholdTypes thresholdType,
        out string error)
    {
        const int automaticMask = (int)(ThresholdTypes.Otsu | ThresholdTypes.Triangle);

        thresholdType = ThresholdTypes.Binary;
        error = string.Empty;

        var explicitAutomatic = typeValue & automaticMask;
        if (explicitAutomatic == automaticMask)
        {
            error = "Threshold type cannot combine Otsu and Triangle.";
            return false;
        }

        if (useOtsu && explicitAutomatic == (int)ThresholdTypes.Triangle)
        {
            error = "UseOtsu cannot be combined with Triangle threshold type.";
            return false;
        }

        var baseType = typeValue & ~automaticMask;
        if (baseType is not 0
            and not (int)ThresholdTypes.BinaryInv
            and not (int)ThresholdTypes.Trunc
            and not (int)ThresholdTypes.Tozero
            and not (int)ThresholdTypes.TozeroInv)
        {
            error = $"Unsupported threshold type value: {typeValue}.";
            return false;
        }

        var automaticType = explicitAutomatic;
        if (useOtsu)
        {
            automaticType |= (int)ThresholdTypes.Otsu;
        }

        if (automaticType == automaticMask)
        {
            error = "Threshold type cannot combine Otsu and Triangle.";
            return false;
        }

        if (automaticType != 0 &&
            baseType is not (int)ThresholdTypes.Binary and not (int)ThresholdTypes.BinaryInv)
        {
            error = "Otsu and Triangle require Binary or BinaryInv as the base threshold type.";
            return false;
        }

        thresholdType = (ThresholdTypes)(baseType | automaticType);
        return true;
    }

    private static bool TryValidateImageContract(
        Mat src,
        ThresholdTypes thresholdType,
        double threshold,
        double maxValue,
        out string error)
    {
        error = string.Empty;
        var contract = new ThresholdImageContractProvider()
            .GetContracts(OperatorType.Thresholding, ["Image"], OperatorLifecycle.Stable)
            .Single();
        var depth = src.Depth();
        var mode = (thresholdType & ThresholdTypes.Otsu) == ThresholdTypes.Otsu
            ? "Otsu"
            : (thresholdType & ThresholdTypes.Triangle) == ThresholdTypes.Triangle
                ? "Triangle"
                : "Fixed";

        if (src.Channels() > 1 && !ThresholdImageContractProvider.SupportsColorConversion(depth))
        {
            error = ImageInputRuntimeContractEvaluator.FormatFailure(
                "IMAGE_MODE_DEPTH_UNSUPPORTED",
                OperatorType.Thresholding,
                contract,
                src,
                mode,
                "BGR/BGRA to Gray supports CV_8U, CV_16U, and CV_32F only.");
            return false;
        }

        if (mode == "Otsu" && depth != MatType.CV_8U && depth != MatType.CV_16U)
        {
            error = ImageInputRuntimeContractEvaluator.FormatFailure(
                "IMAGE_MODE_DEPTH_UNSUPPORTED",
                OperatorType.Thresholding,
                contract,
                src,
                mode,
                "Otsu supports CV_8U and CV_16U only for the installed runtime contract.");
            return false;
        }

        if (mode == "Triangle" && depth != MatType.CV_8U)
        {
            error = ImageInputRuntimeContractEvaluator.FormatFailure(
                "IMAGE_MODE_DEPTH_UNSUPPORTED",
                OperatorType.Thresholding,
                contract,
                src,
                mode,
                "Triangle supports CV_8U only for the installed runtime contract.");
            return false;
        }

        if (!IsValueInDepthDomain(threshold, depth) || !IsValueInDepthDomain(maxValue, depth))
        {
            error = ImageInputRuntimeContractEvaluator.FormatFailure(
                "IMAGE_DYNAMIC_RANGE_UNDEFINED",
                OperatorType.Thresholding,
                contract,
                src,
                mode,
                $"Threshold={threshold}; MaxValue={maxValue}; AllowedDomain={DescribeDepthDomain(depth)}.");
            return false;
        }

        return true;
    }

    private static bool IsValueInDepthDomain(double value, MatType depth)
    {
        if (!double.IsFinite(value))
        {
            return false;
        }

        if (depth == MatType.CV_8U) return value is >= byte.MinValue and <= byte.MaxValue;
        if (depth == MatType.CV_16U) return value is >= ushort.MinValue and <= ushort.MaxValue;
        if (depth == MatType.CV_16S) return value is >= short.MinValue and <= short.MaxValue;
        return depth == MatType.CV_32F || depth == MatType.CV_64F;
    }

    private static string DescribeDepthDomain(MatType depth)
    {
        if (depth == MatType.CV_8U) return "[0,255]";
        if (depth == MatType.CV_16U) return "[0,65535]";
        if (depth == MatType.CV_16S) return "[-32768,32767]";
        if (depth == MatType.CV_32F || depth == MatType.CV_64F) return "AnyFiniteValue";
        return "Unsupported";
    }
}
