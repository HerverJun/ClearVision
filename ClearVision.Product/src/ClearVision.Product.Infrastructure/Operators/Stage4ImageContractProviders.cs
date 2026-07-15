using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Services;

namespace ClearVision.Product.Infrastructure.Operators;

public sealed class PixelStatisticsImageContractProvider : IOperatorImageContractProvider
{
    public IReadOnlyList<ImageInputContract> GetContracts(OperatorType operatorType, IReadOnlyList<string> imageInputPorts, OperatorLifecycle lifecycle)
    {
        return imageInputPorts.Select(port =>
        {
            if (port.Equals("Mask", StringComparison.OrdinalIgnoreCase))
            {
                var maskVariants = ImageContractVariantFactory.Allowed(
                    "Default", ["CV_8U"], [1, 3, 4],
                    "Mask is converted to single-channel binary 8U before sampling.",
                    (_, channels) => channels == 1 ? ImageContractVerification.VerifiedSupport : ImageContractVerification.VerifiedConversion,
                    "C3/C4 mask -> Gray -> binary 8U; C1 is thresholded directly.",
                    "No image output.", "Non-zero mask values select samples.", "E2_EXECUTABLE_PROBE").ToArray();
                return new ImageInputContract(
                    port, ["CV_8U"], [1, 3, 4], ["CV_8U"],
                    "Mask is an 8-bit selection image.",
                    "Optional color-to-gray conversion followed by binary thresholding.",
                    "No image output.", "Non-zero mask domain.", maskVariants,
                    "NotApplicableFor8U", "IMAGE_DEPTH_UNSUPPORTED", OperatorImageContractResolver.ContractVersion);
            }

            var variants = new List<ImageContractVariant>();
            variants.AddRange(ImageContractVariantFactory.Allowed(
                "Default", ["CV_8U", "CV_16U", "CV_32F"], [1, 3, 4],
                "Statistics are computed in the admitted input numeric domain; Channel selects Gray/R/G/B/All at runtime.",
                (_, channels) => channels == 1 ? ImageContractVerification.VerifiedSupport : ImageContractVerification.VerifiedConversion,
                "Channel split or supported color-to-gray conversion; no depth scaling.",
                "No image output.", "Exact source numeric values are accumulated as double.", "E2_NUMERICAL_ORACLE"));
            variants.AddRange(ImageContractVariantFactory.Allowed(
                "Default", ["CV_8S", "CV_16S", "CV_32S", "CV_64F"], [1],
                "Single-channel statistics use typed scalar reads without depth conversion.",
                (_, _) => ImageContractVerification.VerifiedSupport,
                "None.", "No image output.", "Exact source numeric values are accumulated as double.", "E2_EXECUTABLE_PROBE"));
            return new ImageInputContract(
                port,
                ["CV_8U", "CV_8S", "CV_16U", "CV_16S", "CV_32S", "CV_32F", "CV_64F"],
                [1, 3, 4],
                ["CV_8U", "CV_8S", "CV_16U", "CV_16S", "CV_32S", "CV_32F", "CV_64F"],
                "Exact support is declared per depth/channel pair.",
                "No implicit depth normalization; explicit channel selection only.",
                "No image output.", "Statistics retain the admitted input numeric domain.", variants,
                "RejectNaNAndInfinityForFloatingVariants", "IMAGE_DEPTH_UNSUPPORTED", OperatorImageContractResolver.ContractVersion);
        }).ToArray();
    }
}

public sealed class GlcmTextureImageContractProvider : IOperatorImageContractProvider
{
    public IReadOnlyList<ImageInputContract> GetContracts(OperatorType operatorType, IReadOnlyList<string> imageInputPorts, OperatorLifecycle lifecycle)
    {
        var variants = ImageContractVariantFactory.Allowed(
            "Default", ["CV_8U", "CV_16U", "CV_32F"], [1, 3, 4],
            "The selected ROI is converted to gray and explicitly quantized to an 8-bit affine range domain before GLCM construction.",
            (depth, channels) => depth == "CV_8U" && channels == 1
                ? ImageContractVerification.VerifiedSupport
                : ImageContractVerification.VerifiedConversion,
            "C3/C4 -> Gray; non-8U gray -> CV_32F -> per-ROI affine range mapping -> CV_8U.",
            "No image output.", "Texture features are computed in the explicit per-ROI 8-bit quantization domain.",
            "E2_NUMERICAL_ORACLE").ToArray();
        return imageInputPorts.Select(port => new ImageInputContract(
            port, ["CV_8U", "CV_16U", "CV_32F"], [1, 3, 4], ["CV_8U"],
            "Exact support is declared for the verified gray conversion and quantization path.",
            "Explicit color-to-gray conversion followed by per-ROI affine range mapping to CV_8U.",
            "No image output.", "Per-ROI 8-bit quantization domain.", variants,
            "RejectNaNAndInfinityForFloatingVariants", "IMAGE_DEPTH_UNSUPPORTED", OperatorImageContractResolver.ContractVersion)).ToArray();
    }
}

public sealed class ShadingCorrectionImageContractProvider : IOperatorImageContractProvider
{
    public IReadOnlyList<ImageInputContract> GetContracts(OperatorType operatorType, IReadOnlyList<string> imageInputPorts, OperatorLifecycle lifecycle)
    {
        var variants = ImageContractVariantFactory.Allowed(
            "Default", ["CV_8U", "CV_16U", "CV_32F", "CV_64F"], [1, 3],
            "Gaussian, top-hat and explicit background correction preserve the admitted source depth; ColorMode controls luma-only versus per-channel processing.",
            (_, channels) => channels == 1 ? ImageContractVerification.VerifiedSupport : ImageContractVerification.VerifiedConversion,
            "C3 luma path uses an explicit byte-compatible color conversion; per-channel path preserves channel depth.",
            "Preserve input depth and channel count.",
            "Integer inputs retain their native output range; floating inputs retain their floating depth.",
            "E2_EXECUTABLE_PROBE").ToArray();
        return imageInputPorts.Select(port => new ImageInputContract(
            port, ["CV_8U", "CV_16U", "CV_32F", "CV_64F"], [1, 3], ["CV_8U", "CV_16U", "CV_32F", "CV_64F"],
            "Exact support is declared for one- and three-channel shading correction inputs.",
            "Only the documented luma/color processing conversions are applied.",
            "Preserve input depth and channel count.",
            "No hidden generic normalization outside the documented luma conversion path.", variants,
            "RejectNaNAndInfinityForFloatingVariants", "IMAGE_DEPTH_UNSUPPORTED", OperatorImageContractResolver.ContractVersion)).ToArray();
    }
}
