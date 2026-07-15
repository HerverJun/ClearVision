// ImageNormalizeOperator.cs
// 图像归一化算子
// 对图像像素进行范围或分布归一化处理
// 作者：蘅芜君
using ClearVision.Product.Core.Attributes;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Operators;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
namespace ClearVision.Product.Infrastructure.Operators;

[OperatorMeta(
    DisplayName = "图像归一化",
    Description = "MinMax 映射像素范围；ZScore 返回均值约 0、总体标准差约 1 的浮点标准分。",
    CategoryId = OperatorCategoryId.ImagePreprocessing,
    IconName = "normalize",
    Keywords = new[] { "normalize", "minmax", "zscore", "standard score", "equalize" },
    Version = "1.0.3"
)]
[OperatorImageContractProvider(typeof(ImageNormalizeImageContractProvider))]
[OperatorParameterRule("Method", ReasonCode = "IMAGE_NORMALIZE_METHOD")]
[OperatorParameterRule("Alpha", DisabledWhenAll = new[] { "Method!=MinMax" }, HiddenWhenAll = new[] { "Method!=MinMax" }, IgnoredWhenAll = new[] { "Method!=MinMax" }, ReasonCode = "IMAGE_NORMALIZE_RANGE_ONLY_FOR_MINMAX")]
[OperatorParameterRule("Beta", DisabledWhenAll = new[] { "Method!=MinMax" }, HiddenWhenAll = new[] { "Method!=MinMax" }, IgnoredWhenAll = new[] { "Method!=MinMax" }, ReasonCode = "IMAGE_NORMALIZE_RANGE_ONLY_FOR_MINMAX")]
[OperatorParameterRule("ColorMode", ReasonCode = "IMAGE_NORMALIZE_COLOR_MODE")]
[OperatorOutputRule("Image", ReasonCode = "IMAGE_NORMALIZE_OUTPUT")]
[OperatorOutputRule("Method", ReasonCode = "IMAGE_NORMALIZE_OUTPUT")]
[OperatorOutputRule("ColorMode", ReasonCode = "IMAGE_NORMALIZE_OUTPUT")]
[OperatorOutputRule("Channels", ReasonCode = "IMAGE_NORMALIZE_OUTPUT")]
[OperatorOutputRule("OutputMatType", ReasonCode = "IMAGE_NORMALIZE_OUTPUT")]
[OperatorOutputRule("SigmaDegenerate", ReasonCode = "IMAGE_NORMALIZE_OUTPUT")]
[InputPort("Image", "Image", PortDataType.Image, IsRequired = true)]
[OutputPort("Image", "Image", PortDataType.Image)]
[OutputPort("Method", "实际归一化方法", PortDataType.String)]
[OutputPort("ColorMode", "实际颜色模式", PortDataType.String)]
[OutputPort("Channels", "输出通道数", PortDataType.Integer)]
[OutputPort("OutputMatType", "输出 Mat 类型", PortDataType.String)]
[OutputPort("SigmaDegenerate", "标准差退化", PortDataType.Boolean)]
[OperatorParam("Method", "Method", "enum", Description = "MinMax 按目标范围映射；ZScore 返回浮点标准分；Histogram 执行 8 位直方图均衡。", DefaultValue = "MinMax", Options = new[] { "MinMax|MinMax", "ZScore|ZScore", "Histogram|Histogram" })]
[OperatorParam("Alpha", "Alpha", "double", Description = "仅用于 MinMax 的目标下界。", DefaultValue = 0.0, Min = -10000.0, Max = 10000.0)]
[OperatorParam("Beta", "Beta", "double", Description = "仅用于 MinMax 的目标上界。", DefaultValue = 255.0, Min = -10000.0, Max = 10000.0)]
[OperatorParam("ColorMode", "Color Mode", "enum", Description = "PerChannel 独立处理三个颜色通道；彩色 ZScore 不支持 LumaOnly，需显式选择 PerChannel。", DefaultValue = "LumaOnly", Options = new[] { "LumaOnly|LumaOnly", "PerChannel|PerChannel" })]
[AlgorithmInfo(
    Name = "MinMax range normalization / floating ZScore standardization / histogram equalization",
    CoreApi = "Cv2.Normalize / Cv2.MeanStdDev / Cv2.Subtract / Cv2.Divide / Cv2.EqualizeHist",
    ImplementationStrategy = "ZScore validates that all input values are finite, then uses z=(x-mean)/sigma in CV_32F without a following MinMax pass. Non-finite input or statistics fail with IMAGE_NORMALIZE_NONFINITE_INPUT; sigma<=1e-6 for finite inputs produces finite zeros. Three-channel ZScore is per-channel; ZScore+LumaOnly fails fast.",
    TimeComplexity = "O(W*H*C)",
    SpaceComplexity = "O(W*H*C)",
    SuitableUseCases = new[] { "MinMax for bounded display or downstream range contracts", "ZScore for statistical standardization before floating-point processing" },
    KnownLimitations = new[] { "Histogram mode converts non-8U inputs to an 8-bit equalization domain", "Color ZScore requires ColorMode=PerChannel" },
    Dependencies = new[] { "OpenCvSharp" }
)]
public class ImageNormalizeOperator : OperatorBase
{
    private const double SigmaEpsilon = 1e-6;
    private const string NonFiniteZScoreInputError = "IMAGE_NORMALIZE_NONFINITE_INPUT: input contains NaN or Infinity.";

    private readonly record struct NormalizationResult(Mat Image, bool SigmaDegenerate);

    public override OperatorType OperatorType => OperatorType.ImageNormalize;

    public ImageNormalizeOperator(ILogger<ImageNormalizeOperator> logger) : base(logger)
    {
    }

    protected override Task<OperatorExecutionOutput> ExecuteCoreAsync(
        Operator @operator,
        Dictionary<string, object>? inputs,
        CancellationToken cancellationToken)
    {
        if (!TryGetInputImage(inputs, out var imageWrapper) || imageWrapper == null)
        {
            return Task.FromResult(OperatorExecutionOutput.Failure("Input image is required"));
        }

        var src = imageWrapper.GetMat();
        if (src.Empty())
        {
            return Task.FromResult(OperatorExecutionOutput.Failure("Input image is invalid"));
        }

        var method = GetStringParam(@operator, "Method", "MinMax");
        var alpha = 0.0;
        var beta = 255.0;
        if (method.Equals("MinMax", StringComparison.OrdinalIgnoreCase))
        {
            alpha = GetDoubleParam(@operator, "Alpha", 0.0, -10000.0, 10000.0);
            beta = GetDoubleParam(@operator, "Beta", 255.0, -10000.0, 10000.0);
        }

        var colorMode = GetStringParam(@operator, "ColorMode", "LumaOnly");

        if (method.Equals("ZScore", StringComparison.OrdinalIgnoreCase) &&
            !Cv2.CheckRange(src, quiet: true))
        {
            return Task.FromResult(OperatorExecutionOutput.Failure(NonFiniteZScoreInputError));
        }

        NormalizationResult normalization;
        try
        {
            if (src.Channels() == 1)
            {
                normalization = NormalizeSingleChannel(src, method, alpha, beta);
            }
            else if (src.Channels() == 3)
            {
                normalization = colorMode.Equals("PerChannel", StringComparison.OrdinalIgnoreCase)
                    ? ApplyPerChannel(src, channel => NormalizeSingleChannel(channel, method, alpha, beta))
                    : colorMode.Equals("LumaOnly", StringComparison.OrdinalIgnoreCase)
                        ? ApplyLumaChannel(src, channel => NormalizeSingleChannel(channel, method, alpha, beta))
                        : throw new InvalidOperationException("Unsupported color mode");
            }
            else
            {
                return Task.FromResult(OperatorExecutionOutput.Failure("Only 1-channel and 3-channel images are supported"));
            }
        }
        catch (NonFiniteZScoreInputException)
        {
            return Task.FromResult(OperatorExecutionOutput.Failure(NonFiniteZScoreInputError));
        }

        var result = normalization.Image;
        return Task.FromResult(OperatorExecutionOutput.Success(CreateImageOutput(result, new Dictionary<string, object>
        {
            { "Method", method },
            { "ColorMode", src.Channels() == 1 ? "Gray" : colorMode },
            { "Channels", result.Channels() },
            { "OutputMatType", result.Type().ToString() },
            { "SigmaDegenerate", normalization.SigmaDegenerate }
        })));
    }

    public override ValidationResult ValidateParameters(Operator @operator)
    {
        var method = GetStringParam(@operator, "Method", "MinMax");
        var validMethods = new[] { "MinMax", "ZScore", "Histogram" };
        if (!validMethods.Contains(method, StringComparer.OrdinalIgnoreCase))
        {
            return ValidationResult.Invalid("Method must be MinMax, ZScore or Histogram");
        }

        var colorMode = GetStringParam(@operator, "ColorMode", "LumaOnly");
        var validColorModes = new[] { "LumaOnly", "PerChannel" };
        if (!validColorModes.Contains(colorMode, StringComparer.OrdinalIgnoreCase))
        {
            return ValidationResult.Invalid("ColorMode must be LumaOnly or PerChannel");
        }

        return ValidationResult.Valid();
    }

    private static NormalizationResult NormalizeSingleChannel(Mat src, string method, double alpha, double beta)
    {
        return method.ToLowerInvariant() switch
        {
            "minmax" => NormalizeMinMax(src, alpha, beta),
            "zscore" => NormalizeZScore(src),
            "histogram" => NormalizeHistogram(src),
            _ => throw new InvalidOperationException("Unsupported normalize method")
        };
    }

    private static NormalizationResult NormalizeMinMax(Mat src, double alpha, double beta)
    {
        var (resolvedAlpha, resolvedBeta) = ResolveTargetRange(src, alpha, beta);
        var normalized = new Mat();
        Cv2.Normalize(src, normalized, resolvedAlpha, resolvedBeta, NormTypes.MinMax, GetMatchingSingleChannelType(src));
        return new NormalizationResult(normalized, SigmaDegenerate: false);
    }

    private static NormalizationResult NormalizeZScore(Mat src)
    {
        using var src32 = new Mat();
        src.ConvertTo(src32, MatType.CV_32FC1);
        Cv2.MeanStdDev(src32, out var mean, out var stddev);
        var sigma = stddev.Val0;

        if (!double.IsFinite(mean.Val0) || !double.IsFinite(sigma))
        {
            throw new NonFiniteZScoreInputException();
        }

        if (sigma <= SigmaEpsilon)
        {
            return new NormalizationResult(
                new Mat(src.Rows, src.Cols, MatType.CV_32FC1, Scalar.All(0)),
                SigmaDegenerate: true);
        }

        using var centered = new Mat();
        Cv2.Subtract(src32, new Scalar(mean.Val0), centered);
        var z = new Mat();
        Cv2.Divide(centered, new Scalar(sigma), z);
        return new NormalizationResult(z, SigmaDegenerate: false);
    }

    private sealed class NonFiniteZScoreInputException : Exception
    {
    }

    private static NormalizationResult NormalizeHistogram(Mat src)
    {
        using var byteChannel = ConvertToByteChannel(src);
        var normalized = new Mat();
        Cv2.EqualizeHist(byteChannel, normalized);
        return new NormalizationResult(normalized, SigmaDegenerate: false);
    }

    private static Mat ConvertToByteChannel(Mat src)
    {
        if (src.Depth() == MatType.CV_8U)
        {
            return src.Clone();
        }

        var normalized = new Mat();
        Cv2.Normalize(src, normalized, 0, 255, NormTypes.MinMax, MatType.CV_8UC1);
        return normalized;
    }

    private static NormalizationResult ApplyPerChannel(Mat src, Func<Mat, NormalizationResult> processor)
    {
        Cv2.Split(src, out var channels);
        var processed = new Mat[channels.Length];
        var sigmaDegenerate = false;

        try
        {
            for (var i = 0; i < channels.Length; i++)
            {
                var channelResult = processor(channels[i]);
                processed[i] = channelResult.Image;
                sigmaDegenerate |= channelResult.SigmaDegenerate;
            }

            var result = new Mat();
            Cv2.Merge(processed, result);
            return new NormalizationResult(result, sigmaDegenerate);
        }
        finally
        {
            foreach (var channel in channels)
            {
                channel.Dispose();
            }

            foreach (var channel in processed)
            {
                channel?.Dispose();
            }
        }
    }

    private static NormalizationResult ApplyLumaChannel(Mat src, Func<Mat, NormalizationResult> processor)
    {
        return ApplyLumaChannel(src, processor, allowByteFallback: true);
    }

    private static NormalizationResult ApplyLumaChannel(Mat src, Func<Mat, NormalizationResult> processor, bool allowByteFallback)
    {
        using var yuv = new Mat();
        Cv2.CvtColor(src, yuv, ColorConversionCodes.BGR2YUV);
        Cv2.Split(yuv, out var channels);

        try
        {
            var lumaResult = processor(channels[0]);
            using var processedLuma = lumaResult.Image;
            if (processedLuma.Type() != channels[0].Type())
            {
                if (!allowByteFallback || processedLuma.Depth() != MatType.CV_8U)
                {
                    throw new InvalidOperationException("Luma-only normalization requires matching channel depths before merge.");
                }

                // When luma normalization collapses to 8-bit, re-run on an 8-bit color view so Y/U/V stay merge-compatible.
                using var byteSrc = ConvertToByteCompatibleImage(src);
                return ApplyLumaChannel(byteSrc, processor, allowByteFallback: false);
            }

            channels[0].Dispose();
            channels[0] = processedLuma.Clone();

            using var merged = new Mat();
            Cv2.Merge(channels, merged);

            var result = new Mat();
            Cv2.CvtColor(merged, result, ColorConversionCodes.YUV2BGR);
            return new NormalizationResult(result, lumaResult.SigmaDegenerate);
        }
        finally
        {
            foreach (var channel in channels)
            {
                channel.Dispose();
            }
        }
    }

    private static Mat ConvertToByteCompatibleImage(Mat src)
    {
        if (src.Depth() == MatType.CV_8U)
        {
            return src.Clone();
        }

        var converted = new Mat();
        var targetType = MatType.MakeType(MatType.CV_8U, src.Channels());

        switch (src.Depth())
        {
            case MatType.CV_16U:
                src.ConvertTo(converted, targetType, 1.0 / 256.0);
                break;
            case MatType.CV_32F:
            case MatType.CV_64F:
                var (floatMin, floatMax) = GetGlobalMinMax(src);
                if (floatMin >= 0d && floatMax <= 1d)
                {
                    src.ConvertTo(converted, targetType, 255.0);
                }
                else if (floatMin >= 0d && floatMax <= 255d)
                {
                    src.ConvertTo(converted, targetType);
                }
                else
                {
                    ConvertToByteCompatibleImageWithRangeNormalization(src, converted, targetType, floatMin, floatMax);
                }

                break;
            default:
                var (minValue, maxValue) = GetGlobalMinMax(src);
                ConvertToByteCompatibleImageWithRangeNormalization(src, converted, targetType, minValue, maxValue);
                break;
        }

        return converted;
    }

    private static void ConvertToByteCompatibleImageWithRangeNormalization(Mat src, Mat dst, MatType targetType, double minValue, double maxValue)
    {
        if (!double.IsFinite(minValue) || !double.IsFinite(maxValue))
        {
            throw new InvalidOperationException("Input image contains non-finite values and cannot be converted to 8-bit color.");
        }

        if (maxValue <= minValue)
        {
            src.ConvertTo(dst, targetType, 0.0, 0.0);
            return;
        }

        var scale = 255.0 / (maxValue - minValue);
        var shift = -minValue * scale;
        src.ConvertTo(dst, targetType, scale, shift);
    }

    private static (double Min, double Max) GetGlobalMinMax(Mat src)
    {
        if (src.Channels() == 1)
        {
            double minValue;
            double maxValue;
            Cv2.MinMaxLoc(src, out minValue, out maxValue);
            return (minValue, maxValue);
        }

        Cv2.Split(src, out var channels);
        try
        {
            double minValue = double.PositiveInfinity;
            double maxValue = double.NegativeInfinity;

            foreach (var channel in channels)
            {
                double channelMin;
                double channelMax;
                Cv2.MinMaxLoc(channel, out channelMin, out channelMax);
                minValue = Math.Min(minValue, channelMin);
                maxValue = Math.Max(maxValue, channelMax);
            }

            return (minValue, maxValue);
        }
        finally
        {
            foreach (var channel in channels)
            {
                channel.Dispose();
            }
        }
    }

    private static (double Alpha, double Beta) ResolveTargetRange(Mat src, double alpha, double beta)
    {
        if (Math.Abs(alpha) > 1e-9 || Math.Abs(beta - 255.0) > 1e-9)
        {
            return (alpha, beta);
        }

        return src.Depth() switch
        {
            MatType.CV_16U => (0.0, ushort.MaxValue),
            MatType.CV_32F or MatType.CV_64F => (0.0, 1.0),
            _ => (alpha, beta)
        };
    }

    private static MatType GetMatchingSingleChannelType(Mat src)
    {
        return src.Depth() switch
        {
            MatType.CV_8U => MatType.CV_8UC1,
            MatType.CV_16U => MatType.CV_16UC1,
            MatType.CV_32F => MatType.CV_32FC1,
            MatType.CV_64F => MatType.CV_64FC1,
            _ => MatType.CV_8UC1
        };
    }
}
