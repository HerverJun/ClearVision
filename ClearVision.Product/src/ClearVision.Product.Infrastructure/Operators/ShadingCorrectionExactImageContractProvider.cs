using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Services;

namespace ClearVision.Product.Infrastructure.Operators;

public sealed class ShadingCorrectionExactImageContractProvider : IOperatorImageContractProvider
{
    public IReadOnlyList<ImageInputContract> GetContracts(
        OperatorType operatorType,
        IReadOnlyList<string> imageInputPorts,
        OperatorLifecycle lifecycle) =>
        imageInputPorts.Select(port => port.Equals("Background", StringComparison.OrdinalIgnoreCase)
            ? CreateBackgroundContract(port)
            : CreateImageContract(port)).ToArray();

    private static ImageInputContract CreateImageContract(string port)
    {
        var variants = new List<ImageContractVariant>();
        foreach (var method in new[] { "DivideByBackground", "GaussianModel", "MorphologicalTopHat" })
        {
            variants.AddRange(ImageContractVariantFactory.Allowed(
                $"{method}:LumaOnly", ["CV_8U", "CV_16U", "CV_32F", "CV_64F"], [1],
                $"Method={method}; ColorMode=LumaOnly; single-channel input bypasses color conversion.",
                (_, _) => ImageContractVerification.VerifiedSupport,
                "None",
                "Preserve input depth; output C1.",
                "Preserve the admitted source numeric domain.",
                "E2_EXECUTABLE_PROBE"));
            variants.AddRange(ImageContractVariantFactory.Allowed(
                $"{method}:LumaOnly", ["CV_8U", "CV_16U", "CV_32F"], [3],
                $"Method={method}; ColorMode=LumaOnly; the installed BGR/YUV path admits 8U, 16U and 32F color images.",
                (_, _) => ImageContractVerification.VerifiedConversion,
                "BGR -> YUV, correct Y, then YUV -> BGR without depth scaling.",
                "Preserve admitted input depth; output C3.",
                "Preserve the admitted source numeric domain.",
                "E2_EXECUTABLE_PROBE"));
            variants.AddRange(ImageContractVariantFactory.Rejected(
                $"{method}:LumaOnly", ["CV_64F"], [3],
                "The installed OpenCV color-conversion path does not admit CV_64FC3; reject before the first native color call.",
                "IMAGE_MODE_DEPTH_UNSUPPORTED",
                "E2_EXECUTABLE_PROBE"));
            variants.AddRange(ImageContractVariantFactory.Allowed(
                $"{method}:PerChannel", ["CV_8U", "CV_16U", "CV_32F", "CV_64F"], [1, 3],
                $"Method={method}; ColorMode=PerChannel; C3 is split, corrected per channel and merged.",
                (_, _) => ImageContractVerification.VerifiedSupport,
                "C3 split/merge only; no depth scaling. C1 is corrected directly.",
                "Preserve input depth and channel count, including CV_64F.",
                "Preserve the admitted source numeric domain.",
                "E2_EXECUTABLE_PROBE"));
        }

        return new ImageInputContract(
            port,
            ["CV_8U", "CV_16U", "CV_32F", "CV_64F"],
            [1, 3],
            ["CV_8U", "CV_16U", "CV_32F", "CV_64F"],
            "Admission is resolved by Method + ColorMode + exact depth/channel pair.",
            "Only the exact LumaOnly color variants perform BGR/YUV conversion; CV_64FC3 LumaOnly is rejected.",
            "Preserve admitted input depth and channel count.",
            "No generic normalization; each admitted depth keeps its native numeric domain.",
            variants,
            "RejectNaNAndInfinityForFloatingVariants",
            "IMAGE_DEPTH_UNSUPPORTED",
            OperatorImageContractResolver.ContractVersion);
    }

    private static ImageInputContract CreateBackgroundContract(string port)
    {
        var variants = new List<ImageContractVariant>();
        foreach (var colorMode in new[] { "LumaOnly", "PerChannel" })
        {
            variants.AddRange(ImageContractVariantFactory.Allowed(
                $"DivideByBackground:{colorMode}", ["CV_8U", "CV_16U", "CV_32F", "CV_64F"], [1, 3],
                "Background is admitted only for DivideByBackground and must match the primary image size/depth; C1 primary images require C1 background, while C3 accepts C1 or C3.",
                (_, channels) => colorMode == "LumaOnly" && channels == 3
                    ? ImageContractVerification.VerifiedConversion
                    : ImageContractVerification.VerifiedSupport,
                colorMode == "LumaOnly"
                    ? "C3 background -> gray luma; C1 is used directly."
                    : "C3 background is split per channel; C1 is shared across channels.",
                "Output follows the primary Image depth/channel contract.",
                "Background and primary Image must use the same admitted depth and size.",
                "E2_EXECUTABLE_PROBE"));
        }

        foreach (var method in new[] { "GaussianModel", "MorphologicalTopHat" })
        foreach (var colorMode in new[] { "LumaOnly", "PerChannel" })
        {
            variants.AddRange(ImageContractVariantFactory.Rejected(
                $"{method}:{colorMode}", ["CV_8U", "CV_16U", "CV_32F", "CV_64F"], [1, 3],
                $"Background is not consumed when Method={method}.",
                "IMAGE_BACKGROUND_MODE_UNSUPPORTED",
                "E2_EXECUTABLE_PROBE"));
        }

        return new ImageInputContract(
            port,
            ["CV_8U", "CV_16U", "CV_32F", "CV_64F"],
            [1, 3],
            ["CV_8U", "CV_16U", "CV_32F", "CV_64F"],
            "Background is optional except for DivideByBackground; when present, admission is mode-specific and relation checks are fail-closed.",
            "Only the declared LumaOnly C3-to-gray conversion is applied.",
            "Output follows the primary Image contract.",
            "Size, depth and channel compatibility are validated against the primary Image before native processing.",
            variants,
            "RejectNaNAndInfinityForFloatingVariants",
            "IMAGE_BACKGROUND_UNSUPPORTED",
            OperatorImageContractResolver.ContractVersion);
    }
}
