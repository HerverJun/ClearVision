using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Services;
using OpenCvSharp;

namespace ClearVision.Product.Infrastructure.Operators;

internal enum SpatialFilterMode
{
    Gaussian = 0,
    Mean = 1,
    Median = 2,
    Bilateral = 3
}

internal sealed record SpatialFilterSettings(
    SpatialFilterMode Mode,
    int KernelSize = 5,
    double SigmaX = 1.0,
    double SigmaY = 0.0,
    int BorderType = 4,
    int Diameter = 9,
    double SigmaColor = 75.0,
    double SigmaSpace = 75.0);

internal sealed record SpatialFilterAppliedSettings(
    SpatialFilterMode Mode,
    int KernelSize,
    double SigmaX,
    double SigmaY,
    int BorderType,
    int Diameter,
    double SigmaColor,
    double SigmaSpace);

internal static class SpatialFilterKernel
{
    private static readonly string[] AllDepths = ["CV_8U", "CV_16U", "CV_16S", "CV_32F", "CV_64F"];
    private static readonly string[] MedianIdentityDepths = ["CV_8U", "CV_16U", "CV_16S", "CV_32F", "CV_64F"];
    private static readonly string[] MedianSmallKernelDepths = ["CV_8U", "CV_16U", "CV_32F"];
    private static readonly string[] BilateralDepths = ["CV_8U", "CV_32F"];

    public static ImageInputContract CreateImageInputContract(OperatorType operatorType, string inputPort)
    {
        var mode = operatorType switch
        {
            OperatorType.MeanFilter => SpatialFilterMode.Mean,
            OperatorType.MedianBlur => SpatialFilterMode.Median,
            OperatorType.BilateralFilter => SpatialFilterMode.Bilateral,
            _ => (SpatialFilterMode?)null
        };
        var variants = BuildVariants();
        if (mode.HasValue)
        {
            variants = variants
                .Where(item => item.Mode.StartsWith(mode.Value.ToString(), StringComparison.Ordinal))
                .ToList();
        }
        var allowed = variants.Where(item => item.Admission == ImageContractAdmission.Allowed).ToArray();
        var supportedDepths = allowed.Select(item => item.Depth).Distinct(StringComparer.Ordinal).ToArray();
        var supportedChannels = allowed.Select(item => item.Channels).Distinct().ToArray();

        return new ImageInputContract(
            inputPort,
            supportedDepths,
            supportedChannels,
            supportedDepths,
            mode.HasValue
                ? $"{mode.Value} shared-kernel admission matrix."
                : "Unified FilterMode selects the shared Gaussian/Mean/Median/Bilateral admission matrix.",
            "None",
            "Preserve input depth and channel count.",
            "Preserve native numeric domain; floating inputs containing NaN/Infinity are rejected.",
            variants,
            "RejectNaNAndInfinity",
            "IMAGE_DEPTH_UNSUPPORTED",
            OperatorImageContractResolver.ContractVersion);
    }

    public static bool TryParseMode(string? raw, out SpatialFilterMode mode)
    {
        mode = SpatialFilterMode.Gaussian;
        if (string.IsNullOrWhiteSpace(raw) ||
            raw.Equals("Gaussian", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (raw.Equals("Mean", StringComparison.OrdinalIgnoreCase) ||
            raw.Equals("Box", StringComparison.OrdinalIgnoreCase))
        {
            mode = SpatialFilterMode.Mean;
            return true;
        }

        if (raw.Equals("Median", StringComparison.OrdinalIgnoreCase))
        {
            mode = SpatialFilterMode.Median;
            return true;
        }

        if (raw.Equals("Bilateral", StringComparison.OrdinalIgnoreCase))
        {
            mode = SpatialFilterMode.Bilateral;
            return true;
        }

        return false;
    }

    public static bool TryValidate(SpatialFilterSettings settings, out string error)
    {
        error = string.Empty;

        if (settings.Mode != SpatialFilterMode.Median &&
            settings.BorderType is not 0 and not 1 and not 2 and not 4)
        {
            error = "BorderType must be Constant(0), Replicate(1), Reflect(2), or Default/Reflect101(4).";
            return false;
        }

        if (settings.Mode is SpatialFilterMode.Gaussian or SpatialFilterMode.Median)
        {
            if (settings.KernelSize is < 1 or > 31)
            {
                error = $"KernelSize must be in [1, 31] for {settings.Mode} filtering.";
                return false;
            }
        }
        else if (settings.Mode == SpatialFilterMode.Mean && settings.KernelSize is < 1 or > 63)
        {
            error = $"KernelSize must be in [1, 63] for {settings.Mode} filtering.";
            return false;
        }

        if (settings.Mode == SpatialFilterMode.Gaussian)
        {
            if (!double.IsFinite(settings.SigmaX) || settings.SigmaX < 0.1 || settings.SigmaX > 10.0)
            {
                error = "SigmaX must be in [0.1, 10].";
                return false;
            }

            if (!double.IsFinite(settings.SigmaY) || settings.SigmaY < 0.0 || settings.SigmaY > 10.0)
            {
                error = "SigmaY must be in [0, 10].";
                return false;
            }
        }

        if (settings.Mode == SpatialFilterMode.Bilateral)
        {
            if (settings.Diameter is < 1 or > 25)
            {
                error = "Diameter must be in [1, 25].";
                return false;
            }

            if (!double.IsFinite(settings.SigmaColor) || settings.SigmaColor < 1.0 || settings.SigmaColor > 255.0)
            {
                error = "SigmaColor must be in [1, 255].";
                return false;
            }

            if (!double.IsFinite(settings.SigmaSpace) || settings.SigmaSpace < 1.0 || settings.SigmaSpace > 255.0)
            {
                error = "SigmaSpace must be in [1, 255].";
                return false;
            }
        }

        return true;
    }

    public static bool TryValidateInput(
        Mat source,
        SpatialFilterSettings settings,
        OperatorType operatorType,
        out string error)
    {
        error = string.Empty;
        ArgumentNullException.ThrowIfNull(source);

        var contract = CreateImageInputContract(operatorType, "Image");
        return ImageInputRuntimeContractEvaluator.TryValidateResolvedMode(
            operatorType,
            contract,
            source,
            ResolveContractMode(settings),
            out error);
    }

    public static SpatialFilterAppliedSettings Apply(
        Mat source,
        Mat destination,
        SpatialFilterSettings settings,
        OperatorType operatorType)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);

        if (source.Empty())
        {
            throw new ArgumentException("Source image must not be empty.", nameof(source));
        }

        if (!TryValidate(settings, out var error))
        {
            throw new ArgumentOutOfRangeException(nameof(settings), error);
        }

        if (!TryValidateInput(source, settings, operatorType, out error))
        {
            throw new InvalidOperationException(error);
        }

        var kernelSize = settings.Mode == SpatialFilterMode.Mean
            ? settings.KernelSize
            : NormalizeOddKernelSize(settings.KernelSize);
        var borderType = (BorderTypes)settings.BorderType;

        switch (settings.Mode)
        {
            case SpatialFilterMode.Gaussian:
                Cv2.GaussianBlur(
                    source,
                    destination,
                    new Size(kernelSize, kernelSize),
                    settings.SigmaX,
                    settings.SigmaY,
                    borderType);
                break;
            case SpatialFilterMode.Mean:
                Cv2.Blur(
                    source,
                    destination,
                    new Size(kernelSize, kernelSize),
                    new Point(-1, -1),
                    borderType);
                break;
            case SpatialFilterMode.Median:
                Cv2.MedianBlur(source, destination, kernelSize);
                break;
            case SpatialFilterMode.Bilateral:
                var effectiveDiameter = NormalizeBilateralDiameter(settings.Diameter);
                Cv2.BilateralFilter(
                    source,
                    destination,
                    effectiveDiameter,
                    settings.SigmaColor,
                    settings.SigmaSpace,
                    borderType);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(settings), settings.Mode, "Unsupported spatial filter mode.");
        }

        return new SpatialFilterAppliedSettings(
            settings.Mode,
            kernelSize,
            settings.SigmaX,
            settings.SigmaY,
            settings.BorderType,
            NormalizeBilateralDiameter(settings.Diameter),
            settings.SigmaColor,
            settings.SigmaSpace);
    }

    internal static int NormalizeOddKernelSize(int value)
    {
        return value % 2 == 0 ? value + 1 : value;
    }

    internal static int NormalizeBilateralDiameter(int value)
    {
        return Math.Max(3, ((value / 2) * 2) + 1);
    }

    internal static string ResolveContractMode(SpatialFilterSettings settings)
    {
        if (settings.Mode != SpatialFilterMode.Median)
        {
            return settings.Mode.ToString();
        }

        var effectiveKernel = NormalizeOddKernelSize(settings.KernelSize);
        if (effectiveKernel <= 1)
        {
            return "Median:Identity";
        }

        return effectiveKernel <= 5 ? "Median:Kernel3Or5" : "Median:Kernel7Plus";
    }

    private static List<ImageContractVariant> BuildVariants()
    {
        var variants = new List<ImageContractVariant>();
        variants.AddRange(ImageContractVariantFactory.Allowed(
            "Gaussian", AllDepths, [1, 3, 4],
            "KernelSize 1..31; effective kernel is odd; border 0/1/2/4.",
            (_, _) => ImageContractVerification.VerifiedSupport,
            "None", "Preserve input depth/channels.", "Preserve native numeric domain.",
            "E2_EXECUTABLE_PROBE"));
        variants.AddRange(ImageContractVariantFactory.Allowed(
            "Mean", AllDepths, [1, 3, 4],
            "KernelSize 1..63; even kernels remain even; border 0/1/2/4.",
            (_, _) => ImageContractVerification.VerifiedSupport,
            "None", "Preserve input depth/channels.", "Preserve native numeric domain.",
            "E2_EXECUTABLE_PROBE"));
        variants.AddRange(ImageContractVariantFactory.Allowed(
            "Median:Identity", MedianIdentityDepths, [1, 3, 4],
            "Effective kernel equals 1 and behaves as identity.",
            (_, _) => ImageContractVerification.VerifiedSupport,
            "None", "Preserve input depth/channels.", "Preserve native numeric domain.",
            "E2_EXECUTABLE_PROBE"));
        variants.AddRange(ImageContractVariantFactory.Allowed(
            "Median:Kernel3Or5", MedianSmallKernelDepths, [1, 3, 4],
            "Effective kernel is 3 or 5.",
            (_, _) => ImageContractVerification.VerifiedSupport,
            "None", "Preserve input depth/channels.", "Preserve native numeric domain.",
            "E2_EXECUTABLE_PROBE"));
        variants.AddRange(ImageContractVariantFactory.Rejected(
            "Median:Kernel3Or5", ["CV_16S", "CV_64F"], [1, 3, 4],
            "The installed median kernel 3/5 path admits only 8U/16U/32F.",
            "IMAGE_MODE_DEPTH_UNSUPPORTED", "E2_EXECUTABLE_PROBE"));
        variants.AddRange(ImageContractVariantFactory.Allowed(
            "Median:Kernel7Plus", ["CV_8U"], [1, 3, 4],
            "Effective kernel is >=7 and <=31.",
            (_, _) => ImageContractVerification.VerifiedSupport,
            "None", "Preserve input depth/channels.", "Preserve native numeric domain.",
            "E2_EXECUTABLE_PROBE"));
        variants.AddRange(ImageContractVariantFactory.Rejected(
            "Median:Kernel7Plus", ["CV_16U", "CV_16S", "CV_32F", "CV_64F"], [1, 3, 4],
            "The installed median kernels >=7 path admits CV_8U only.",
            "IMAGE_MODE_DEPTH_UNSUPPORTED", "E2_EXECUTABLE_PROBE"));
        variants.AddRange(ImageContractVariantFactory.Allowed(
            "Bilateral", BilateralDepths, [1, 3],
            "Diameter 1..25; effective diameter=max(3,2*floor(d/2)+1); border 0/1/2/4.",
            (_, _) => ImageContractVerification.VerifiedSupport,
            "None", "Preserve input depth/channels.", "Preserve native numeric domain.",
            "E2_EXECUTABLE_PROBE"));
        variants.AddRange(ImageContractVariantFactory.Rejected(
            "Bilateral", BilateralDepths, [4],
            "The installed bilateral path admits C1/C3 only.",
            "IMAGE_CHANNELS_UNSUPPORTED", "E2_EXECUTABLE_PROBE"));
        variants.AddRange(ImageContractVariantFactory.Rejected(
            "Bilateral", ["CV_16U", "CV_16S", "CV_64F"], [1, 3, 4],
            "The installed bilateral path admits CV_8U/CV_32F only.",
            "IMAGE_DEPTH_UNSUPPORTED", "E2_EXECUTABLE_PROBE"));
        return variants;
    }
}
