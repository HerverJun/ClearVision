using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Services;
using OpenCvSharp;

namespace ClearVision.Product.Infrastructure.Operators;

internal static class ImageContractVariantFactory
{
    public static IEnumerable<ImageContractVariant> Allowed(
        string mode,
        IEnumerable<string> depths,
        IEnumerable<int> channels,
        string condition,
        Func<string, int, ImageContractVerification> verification,
        string conversionPolicy,
        string outputDepthPolicy,
        string dynamicRangePolicy,
        string evidenceLevel,
        string failureCode = "IMAGE_NONFINITE_INPUT",
        Func<string, ImageContractInputValuePolicy>? inputValuePolicy = null)
    {
        foreach (var depth in depths)
        {
            foreach (var channelCount in channels)
            {
                yield return new ImageContractVariant(
                    mode,
                    depth,
                    channelCount,
                    condition,
                    ImageContractAdmission.Allowed,
                    verification(depth, channelCount),
                    conversionPolicy,
                    outputDepthPolicy,
                    dynamicRangePolicy,
                    inputValuePolicy?.Invoke(depth) ?? DefaultValuePolicy(depth),
                    failureCode,
                    evidenceLevel);
            }
        }
    }

    public static IEnumerable<ImageContractVariant> Rejected(
        string mode,
        IEnumerable<string> depths,
        IEnumerable<int> channels,
        string condition,
        string failureCode,
        string evidenceLevel)
    {
        foreach (var depth in depths)
        {
            foreach (var channelCount in channels)
            {
                yield return new ImageContractVariant(
                    mode,
                    depth,
                    channelCount,
                    condition,
                    ImageContractAdmission.Rejected,
                    ImageContractVerification.VerifiedRejection,
                    "None",
                    "No output; rejected before the native image call.",
                    "Not applicable.",
                    ImageContractInputValuePolicy.Any,
                    failureCode,
                    evidenceLevel);
            }
        }
    }

    public static ImageContractInputValuePolicy DefaultValuePolicy(string depth) =>
        depth is "CV_32F" or "CV_64F"
            ? ImageContractInputValuePolicy.RejectNonFinite
            : ImageContractInputValuePolicy.Any;
}

public sealed class ThresholdImageContractProvider : IOperatorImageContractProvider
{
    internal static readonly string[] FixedSingleChannelDepths =
        ["CV_8U", "CV_16U", "CV_16S", "CV_32F", "CV_64F"];
    internal static readonly string[] FixedColorDepths = ["CV_8U", "CV_16U", "CV_32F"];
    internal static readonly string[] OtsuDepths = ["CV_8U", "CV_16U"];
    internal static readonly string[] TriangleDepths = ["CV_8U"];

    public IReadOnlyList<ImageInputContract> GetContracts(
        OperatorType operatorType,
        IReadOnlyList<string> imageInputPorts,
        OperatorLifecycle lifecycle)
    {
        var variants = new List<ImageContractVariant>();
        variants.AddRange(ImageContractVariantFactory.Allowed(
            "Fixed",
            FixedSingleChannelDepths,
            [1],
            "Type is Binary/BinaryInv/Trunc/ToZero/ToZeroInv and UseOtsu=false.",
            (_, _) => ImageContractVerification.VerifiedSupport,
            "None",
            "Preserve admitted input depth; output C1.",
            "Threshold, MaxValue, and ActualThreshold use the native input numeric domain.",
            "E2_EXECUTABLE_PROBE"));
        variants.AddRange(ImageContractVariantFactory.Allowed(
            "Fixed",
            FixedColorDepths,
            [3, 4],
            "Type is a fixed mode; BGR/BGRA conversion is available only for 8U/16U/32F.",
            (_, _) => ImageContractVerification.VerifiedConversion,
            "BGR/BGRA -> Gray without depth scaling.",
            "Preserve admitted input depth; output C1.",
            "Threshold, MaxValue, and ActualThreshold use the native input numeric domain.",
            "E2_EXECUTABLE_PROBE"));
        variants.AddRange(ImageContractVariantFactory.Rejected(
            "Fixed",
            ["CV_16S", "CV_64F"],
            [3, 4],
            "The installed gray-conversion path does not admit these depth/channel combinations.",
            "IMAGE_MODE_DEPTH_UNSUPPORTED",
            "E2_EXECUTABLE_PROBE"));

        variants.AddRange(ImageContractVariantFactory.Allowed(
            "Otsu",
            OtsuDepths,
            [1, 3, 4],
            "Otsu or UseOtsu with Binary/BinaryInv base mode.",
            (_, channels) => channels == 1
                ? ImageContractVerification.VerifiedSupport
                : ImageContractVerification.VerifiedConversion,
            "C3/C4 -> Gray without depth scaling; C1 is native.",
            "Preserve admitted input depth; output C1.",
            "ActualThreshold uses the native 8U or 16U domain.",
            "E2_EXECUTABLE_PROBE"));
        variants.AddRange(ImageContractVariantFactory.Rejected(
            "Otsu",
            ["CV_16S", "CV_32F", "CV_64F"],
            [1, 3, 4],
            "Otsu is verified only for CV_8U and CV_16U.",
            "IMAGE_MODE_DEPTH_UNSUPPORTED",
            "E2_EXECUTABLE_PROBE"));

        variants.AddRange(ImageContractVariantFactory.Allowed(
            "Triangle",
            TriangleDepths,
            [1, 3, 4],
            "Triangle with Binary/BinaryInv base mode.",
            (_, channels) => channels == 1
                ? ImageContractVerification.VerifiedSupport
                : ImageContractVerification.VerifiedConversion,
            "C3/C4 -> Gray; C1 is native.",
            "CV_8UC1.",
            "8-bit input domain.",
            "E2_EXECUTABLE_PROBE"));
        variants.AddRange(ImageContractVariantFactory.Rejected(
            "Triangle",
            ["CV_16U", "CV_16S", "CV_32F", "CV_64F"],
            [1, 3, 4],
            "Triangle is verified only for CV_8U.",
            "IMAGE_MODE_DEPTH_UNSUPPORTED",
            "E2_EXECUTABLE_PROBE"));

        return imageInputPorts.Select(port => new ImageInputContract(
            port,
            FixedSingleChannelDepths,
            [1, 3, 4],
            FixedSingleChannelDepths,
            "Runtime admission is selected from the exact Type/UseOtsu + Depth + Channels variant.",
            "Color conversion only for explicitly listed variants; no implicit depth conversion.",
            "C1 output preserving the admitted gray depth.",
            "Native numeric domain; no implicit MinMax conversion.",
            variants,
            "RejectNaNAndInfinityForFloatingVariants",
            "IMAGE_DEPTH_UNSUPPORTED",
            OperatorImageContractResolver.ContractVersion)).ToArray();
    }

    internal static bool SupportsColorConversion(MatType depth) =>
        depth == MatType.CV_8U || depth == MatType.CV_16U || depth == MatType.CV_32F;
}

public sealed class SpatialFilterImageContractProvider : IOperatorImageContractProvider
{
    public IReadOnlyList<ImageInputContract> GetContracts(
        OperatorType operatorType,
        IReadOnlyList<string> imageInputPorts,
        OperatorLifecycle lifecycle) =>
        imageInputPorts
            .Select(port => SpatialFilterKernel.CreateImageInputContract(operatorType, port))
            .ToArray();
}

public sealed class HistogramImageContractProvider : IOperatorImageContractProvider
{
    public IReadOnlyList<ImageInputContract> GetContracts(
        OperatorType operatorType,
        IReadOnlyList<string> imageInputPorts,
        OperatorLifecycle lifecycle)
    {
        var variants = new List<ImageContractVariant>();
        variants.AddRange(ImageContractVariantFactory.Allowed(
            "Channel=Gray",
            ["CV_8U"],
            [1, 3, 4],
            "Channel=Gray.",
            (_, channels) => channels == 1
                ? ImageContractVerification.VerifiedSupport
                : ImageContractVerification.VerifiedConversion,
            "C3/C4 -> Gray; C1 is native.",
            "CV_8UC3 histogram chart.",
            "Fixed [0,256) intensity domain.",
            "E2_NUMERICAL_ORACLE"));
        variants.AddRange(ImageContractVariantFactory.Rejected(
            "Channel=Gray",
            ["CV_16U", "CV_16S", "CV_32F", "CV_64F"],
            [1, 3, 4],
            "HistogramAnalysis is intentionally restricted to the verified 8-bit domain.",
            "IMAGE_DEPTH_UNSUPPORTED",
            "E2_STAGE2_CLOSURE"));
        foreach (var channel in new[] { "B", "G", "R" })
        {
            variants.AddRange(ImageContractVariantFactory.Allowed(
                $"Channel={channel}",
                ["CV_8U"],
                [3, 4],
                $"Channel={channel} selects the corresponding native byte channel.",
                (_, _) => ImageContractVerification.VerifiedSupport,
                "Select channel without depth scaling.",
                "CV_8UC3 histogram chart.",
                "Fixed [0,256) intensity domain.",
                "E2_NUMERICAL_ORACLE"));
            variants.AddRange(ImageContractVariantFactory.Rejected(
                $"Channel={channel}",
                ["CV_8U"],
                [1],
                "A named B/G/R channel is unavailable on C1 input.",
                "IMAGE_CHANNELS_UNSUPPORTED",
                "E2_NUMERICAL_ORACLE"));
            variants.AddRange(ImageContractVariantFactory.Rejected(
                $"Channel={channel}",
                ["CV_16U", "CV_16S", "CV_32F", "CV_64F"],
                [1, 3, 4],
                "HistogramAnalysis is intentionally restricted to the verified 8-bit domain.",
                "IMAGE_DEPTH_UNSUPPORTED",
                "E2_STAGE2_CLOSURE"));
        }

        return imageInputPorts.Select(port => new ImageInputContract(
            port,
            ["CV_8U"],
            [1, 3, 4],
            ["CV_8U"],
            "8U-only histogram domain selected by the exact Channel + Depth + Channels variant.",
            "Gray conversion or native channel selection only; no depth scaling.",
            "CV_8UC3 histogram chart.",
            "Fixed [0,256) histogram range with BinCount 2..256.",
            variants,
            "NotApplicableFor8U",
            "IMAGE_DEPTH_UNSUPPORTED",
            OperatorImageContractResolver.ContractVersion)).ToArray();
    }
}

public sealed class SharpnessImageContractProvider : IOperatorImageContractProvider
{
    internal static readonly string[] SupportedDepths = ["CV_8U", "CV_16U", "CV_32F"];

    public IReadOnlyList<ImageInputContract> GetContracts(
        OperatorType operatorType,
        IReadOnlyList<string> imageInputPorts,
        OperatorLifecycle lifecycle)
    {
        var variants = new List<ImageContractVariant>();
        foreach (var method in new[] { "Laplacian", "Brenner", "Tenengrad", "SMD" })
        {
            foreach (var thresholdMode in new[] { "PerMethodDefault", "Manual" })
            {
                foreach (var outputPolicy in new[] { "FullOverlay", "Passthrough", "None" })
                {
                    var mode = $"{method}:{thresholdMode}:{outputPolicy}";
                    var condition = thresholdMode == "PerMethodDefault"
                        ? "Per-method default decisions are calibrated only for CV_8U; higher depths return DecisionReady=false."
                        : "Manual threshold is interpreted in the method's native score unit.";
                    var admittedDepths = outputPolicy == "FullOverlay"
                        ? new[] { "CV_8U", "CV_16U" }
                        : SupportedDepths;
                    variants.AddRange(ImageContractVariantFactory.Allowed(
                        mode,
                        admittedDepths,
                        [1, 3, 4],
                        condition,
                        (_, channels) => channels == 1
                            ? ImageContractVerification.VerifiedSupport
                            : ImageContractVerification.VerifiedConversion,
                        "C3/C4 -> Gray for score computation; output policy remains explicit.",
                        outputPolicy switch
                        {
                            "Passthrough" => "Preserve input type.",
                            "None" => "No Image output.",
                            _ => "Overlay preserves admitted integer input type."
                        },
                        method == "SMD" ? "Native intensity score unit." : "Native intensity squared score unit.",
                        "E2_NUMERICAL_ORACLE"));

                    if (outputPolicy == "FullOverlay")
                    {
                        variants.AddRange(ImageContractVariantFactory.Rejected(
                            mode,
                            ["CV_32F"],
                            [1, 3, 4],
                            "FullOverlay is undefined for uncalibrated floating display ranges.",
                            "IMAGE_DYNAMIC_RANGE_UNDEFINED",
                            "E2_NUMERICAL_ORACLE"));
                    }
                }
            }
        }

        return imageInputPorts.Select(port => new ImageInputContract(
            port,
            SupportedDepths,
            [1, 3, 4],
            SupportedDepths,
            "Admission is exact for Method + ThresholdMode + OutputImagePolicy + Depth + Channels.",
            "Color -> Gray only for score computation; no dynamic-range conversion.",
            "Passthrough preserves input type; None omits Image; overlay is variant-restricted.",
            "Scores use the admitted input's native numeric domain.",
            variants,
            "RejectNaNAndInfinityForFloatingVariants",
            "IMAGE_DEPTH_UNSUPPORTED",
            OperatorImageContractResolver.ContractVersion)).ToArray();
    }
}

public sealed class ImageNormalizeImageContractProvider : IOperatorImageContractProvider
{
    private static readonly string[] Depths = ["CV_8U", "CV_16U", "CV_32F", "CV_64F"];

    public IReadOnlyList<ImageInputContract> GetContracts(
        OperatorType operatorType,
        IReadOnlyList<string> imageInputPorts,
        OperatorLifecycle lifecycle)
    {
        var variants = new List<ImageContractVariant>();
        AddGrayVariants(variants);
        AddMinMaxColorVariants(variants);
        AddZScoreColorVariants(variants);
        AddHistogramColorVariants(variants);

        return imageInputPorts.Select(port => new ImageInputContract(
            port,
            Depths,
            [1, 3],
            Depths,
            "Admission is exact for Method + effective ColorMode + Depth + Channels.",
            "Only explicitly listed normalization conversions are allowed.",
            "MinMax preserves depth; ZScore outputs CV_32F; Histogram outputs CV_8U.",
            "Explicit business-semantic normalization; no generic implicit MinMax conversion.",
            variants,
            "ModeSpecific",
            "IMAGE_DEPTH_UNSUPPORTED",
            OperatorImageContractResolver.ContractVersion)).ToArray();
    }

    private static void AddGrayVariants(List<ImageContractVariant> variants)
    {
        variants.AddRange(ImageContractVariantFactory.Allowed(
            "MinMax:Gray", Depths, [1], "C1 input; ColorMode is validated but has no channel-selection effect.",
            (_, _) => ImageContractVerification.VerifiedSupport,
            "Explicit MinMax normalization.", "Preserve input depth.", "Explicit Alpha/Beta target range.",
            "E2_STAGE2_CLOSURE", failureCode: "IMAGE_NORMALIZE_NONFINITE_INPUT"));
        variants.AddRange(ImageContractVariantFactory.Allowed(
            "ZScore:Gray", Depths, [1], "C1 input; finite values must be representable after the explicit CV_32F narrowing.",
            (_, _) => ImageContractVerification.VerifiedConversion,
            "Explicit conversion to CV_32F before z-score.", "CV_32F.", "Mean 0 / population sigma 1 when non-degenerate.",
            "E2_STAGE2_CLOSURE", failureCode: "IMAGE_NORMALIZE_NONFINITE_INPUT",
            inputValuePolicy: depth => depth == "CV_64F"
                ? ImageContractInputValuePolicy.RequireFiniteFloat32Representable
                : ImageContractVariantFactory.DefaultValuePolicy(depth)));
        variants.AddRange(ImageContractVariantFactory.Allowed(
            "Histogram:Gray", Depths, [1], "C1 input in the explicit histogram-normalization business mode.",
            (_, _) => ImageContractVerification.VerifiedConversion,
            "Explicit conversion to the 8-bit equalization domain.", "CV_8U.", "8-bit equalization domain.",
            "E2_STAGE2_CLOSURE", failureCode: "IMAGE_NORMALIZE_NONFINITE_INPUT"));
    }

    private static void AddMinMaxColorVariants(List<ImageContractVariant> variants)
    {
        variants.AddRange(ImageContractVariantFactory.Allowed(
            "MinMax:PerChannel", Depths, [3], "Each channel is normalized independently.",
            (_, _) => ImageContractVerification.VerifiedSupport,
            "Split/normalize/merge without depth conversion.", "Preserve input depth and C3.", "Per-channel Alpha/Beta target range.",
            "E2_STAGE2_CLOSURE", failureCode: "IMAGE_NORMALIZE_NONFINITE_INPUT"));
        variants.AddRange(ImageContractVariantFactory.Allowed(
            "MinMax:LumaOnly", ["CV_8U", "CV_16U", "CV_32F"], [3], "BGR->YUV luma normalization; CvtColor supports 8U/16U/32F.",
            (_, _) => ImageContractVerification.VerifiedConversion,
            "BGR->YUV, normalize Y, YUV->BGR.", "Preserve input depth and C3.", "Luma Alpha/Beta target range.",
            "E2_STAGE2_CLOSURE", failureCode: "IMAGE_NORMALIZE_NONFINITE_INPUT"));
        variants.AddRange(ImageContractVariantFactory.Rejected(
            "MinMax:LumaOnly", ["CV_64F"], [3],
            "The installed BGR<->YUV conversion path does not admit CV_64F.",
            "IMAGE_MODE_DEPTH_UNSUPPORTED", "E2_STAGE2_CLOSURE"));
    }

    private static void AddZScoreColorVariants(List<ImageContractVariant> variants)
    {
        variants.AddRange(ImageContractVariantFactory.Allowed(
            "ZScore:PerChannel", Depths, [3], "Each channel is narrowed to CV_32F and standardized independently.",
            (_, _) => ImageContractVerification.VerifiedConversion,
            "Split, explicit CV_32F conversion, z-score, merge.", "CV_32FC3.", "Per-channel mean 0 / population sigma 1 when non-degenerate.",
            "E2_STAGE2_CLOSURE", failureCode: "IMAGE_NORMALIZE_NONFINITE_INPUT",
            inputValuePolicy: depth => depth == "CV_64F"
                ? ImageContractInputValuePolicy.RequireFiniteFloat32Representable
                : ImageContractVariantFactory.DefaultValuePolicy(depth)));
        variants.AddRange(ImageContractVariantFactory.Rejected(
            "ZScore:LumaOnly", Depths, [3],
            "Color ZScore requires ColorMode=PerChannel.",
            "IMAGE_MODE_UNSUPPORTED", "E2_STAGE1_REGRESSION"));
    }

    private static void AddHistogramColorVariants(List<ImageContractVariant> variants)
    {
        variants.AddRange(ImageContractVariantFactory.Allowed(
            "Histogram:PerChannel", Depths, [3], "Each channel is explicitly converted to and equalized in CV_8U.",
            (_, _) => ImageContractVerification.VerifiedConversion,
            "Split, explicit 8-bit histogram normalization, merge.", "CV_8UC3.", "Per-channel 8-bit equalization domain.",
            "E2_STAGE2_CLOSURE", failureCode: "IMAGE_NORMALIZE_NONFINITE_INPUT"));
        variants.AddRange(ImageContractVariantFactory.Allowed(
            "Histogram:LumaOnly", ["CV_8U", "CV_16U", "CV_32F"], [3], "BGR->YUV luma equalization with explicit 8-bit fallback when required.",
            (_, _) => ImageContractVerification.VerifiedConversion,
            "BGR->YUV, explicit 8-bit luma equalization, YUV->BGR.", "CV_8UC3 when luma conversion requires the explicit byte domain.", "8-bit equalization domain.",
            "E2_STAGE2_CLOSURE", failureCode: "IMAGE_NORMALIZE_NONFINITE_INPUT"));
        variants.AddRange(ImageContractVariantFactory.Rejected(
            "Histogram:LumaOnly", ["CV_64F"], [3],
            "The installed BGR<->YUV conversion path does not admit CV_64F.",
            "IMAGE_MODE_DEPTH_UNSUPPORTED", "E2_STAGE2_CLOSURE"));
    }
}

public sealed class LaplacianSharpenImageContractProvider : IOperatorImageContractProvider
{
    public IReadOnlyList<ImageInputContract> GetContracts(
        OperatorType operatorType,
        IReadOnlyList<string> imageInputPorts,
        OperatorLifecycle lifecycle)
    {
        var depths = new[] { "CV_8U", "CV_16U", "CV_32F" };
        var variants = ImageContractVariantFactory.Allowed(
            "Default", depths, [1, 3], "Stage 1 native-value Laplacian sharpening path.",
            (_, channels) => channels == 1
                ? ImageContractVerification.VerifiedSupport
                : ImageContractVerification.VerifiedConversion,
            "Color -> Gray for derivative computation; restore result in source domain.",
            "Preserve source depth and channel count.",
            "No MinMax conversion; native-domain Laplacian response.",
            "E2_STAGE1_REGRESSION").ToList();
        variants.AddRange(ImageContractVariantFactory.Rejected(
            "Default", ["CV_64F"], [1, 3],
            "The Stage 1 Laplacian contract intentionally excludes CV_64F.",
            "IMAGE_DEPTH_UNSUPPORTED", "E2_STAGE1_REGRESSION"));
        return imageInputPorts.Select(port => new ImageInputContract(
            port, depths, [1, 3], depths,
            "Stage 1 native-value Laplacian sharpening contract.",
            "Color -> Gray for derivative computation; result is restored in the source numeric domain.",
            "Preserve source depth and channel count.",
            "No MinMax conversion; native-domain Laplacian response.",
            variants,
            "RejectNaNAndInfinityForFloatingVariants",
            "IMAGE_DEPTH_UNSUPPORTED",
            OperatorImageContractResolver.ContractVersion)).ToArray();
    }
}

public sealed class SurfaceDefectDetectionImageContractProvider : IOperatorImageContractProvider
{
    public IReadOnlyList<ImageInputContract> GetContracts(
        OperatorType operatorType,
        IReadOnlyList<string> imageInputPorts,
        OperatorLifecycle lifecycle)
    {
        return imageInputPorts.Select(port =>
        {
            var variants = ImageContractVariantFactory.Allowed(
                "Default", ["CV_8U"], [1, 3, 4],
                port == "Reference"
                    ? "Reference is consumed only by ReferenceDiff."
                    : "GradientMagnitude/LocalContrast/ReferenceDiff legacy 8U paths.",
                (_, channels) => channels == 1
                    ? ImageContractVerification.VerifiedSupport
                    : ImageContractVerification.VerifiedConversion,
                "C3/C4 -> Gray for analysis; no depth scaling.",
                "CV_8U outputs.",
                "Legacy 8-bit intensity domain.",
                "E2_OPERATOR_AND_PACKAGE_TESTS").ToArray();
            return new ImageInputContract(
                port, ["CV_8U"], [1, 3, 4], ["CV_8U"],
                port == "Reference"
                    ? "Optional 8U reference image for ReferenceDiff."
                    : "8U C1/C3/C4 execution for the verified defect modes.",
                "C3/C4 -> Gray for analysis; no depth scaling.",
                "Image output is CV_8UC3; masks and responses remain 8-bit.",
                "Legacy 0..255 intensity domain.",
                variants,
                "NotApplicableFor8U",
                "IMAGE_DEPTH_UNSUPPORTED",
                OperatorImageContractResolver.ContractVersion);
        }).ToArray();
    }
}
