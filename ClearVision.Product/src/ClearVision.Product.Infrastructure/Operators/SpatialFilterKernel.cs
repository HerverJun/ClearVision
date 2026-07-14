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
    private static readonly string[] MedianDepths = ["CV_8U", "CV_16U", "CV_16S", "CV_32F", "CV_64F"];
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
        var supportedDepths = mode switch
        {
            SpatialFilterMode.Bilateral => BilateralDepths,
            _ => AllDepths
        };
        var supportedChannels = mode == SpatialFilterMode.Bilateral ? new[] { 1, 3 } : new[] { 1, 3, 4 };
        var restrictions = new List<ImageModeRestriction>
        {
            BuildModeRestriction(SpatialFilterMode.Gaussian),
            BuildModeRestriction(SpatialFilterMode.Mean),
            BuildModeRestriction(SpatialFilterMode.Median),
            BuildModeRestriction(SpatialFilterMode.Bilateral)
        };

        if (mode.HasValue)
        {
            restrictions = restrictions.Where(item => item.Mode == mode.Value.ToString()).ToList();
        }

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
            restrictions,
            "RejectNaNAndInfinity",
            "IMAGE_DEPTH_UNSUPPORTED",
            OperatorImageContractResolver.ContractVersion,
            mode is SpatialFilterMode.Gaussian or SpatialFilterMode.Mean
                ? ImageContractStatus.Native
                : ImageContractStatus.Restricted,
            "E2_EXECUTABLE_PROBE");
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
        var depth = ImageInputRuntimeContractEvaluator.ToDepthName(source.Depth());
        var channels = source.Channels();
        if (channels is not 1 and not 3 and not 4)
        {
            error = ImageInputRuntimeContractEvaluator.FormatFailure(
                "IMAGE_CHANNELS_UNSUPPORTED",
                operatorType,
                contract,
                source,
                settings.Mode.ToString(),
                $"Channels={channels} is not admitted by the shared spatial-filter kernel.");
            return false;
        }

        var depthSupported = settings.Mode switch
        {
            SpatialFilterMode.Gaussian or SpatialFilterMode.Mean => AllDepths.Contains(depth, StringComparer.Ordinal),
            SpatialFilterMode.Median => IsMedianDepthSupported(depth, NormalizeOddKernelSize(settings.KernelSize)),
            SpatialFilterMode.Bilateral => BilateralDepths.Contains(depth, StringComparer.Ordinal),
            _ => false
        };
        if (!depthSupported)
        {
            var failureCode = settings.Mode == SpatialFilterMode.Bilateral
                ? "IMAGE_DEPTH_UNSUPPORTED"
                : "IMAGE_MODE_DEPTH_UNSUPPORTED";
            error = ImageInputRuntimeContractEvaluator.FormatFailure(
                failureCode,
                operatorType,
                contract,
                source,
                settings.Mode.ToString(),
                $"EffectiveKernel={NormalizeOddKernelSize(settings.KernelSize)}; Diameter={NormalizeBilateralDiameter(settings.Diameter)}.");
            return false;
        }

        if (settings.Mode == SpatialFilterMode.Bilateral && channels == 4)
        {
            error = ImageInputRuntimeContractEvaluator.FormatFailure(
                "IMAGE_CHANNELS_UNSUPPORTED",
                operatorType,
                contract,
                source,
                settings.Mode.ToString(),
                "BilateralFilter supports C1/C3 only.");
            return false;
        }

        if ((source.Depth() == MatType.CV_32F || source.Depth() == MatType.CV_64F) &&
            !Cv2.CheckRange(source, quiet: true))
        {
            error = ImageInputRuntimeContractEvaluator.FormatFailure(
                "IMAGE_NONFINITE_INPUT",
                operatorType,
                contract,
                source,
                settings.Mode.ToString(),
                "Input contains NaN or Infinity.");
            return false;
        }

        return true;
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

    private static int NormalizeOddKernelSize(int value)
    {
        return value % 2 == 0 ? value + 1 : value;
    }

    private static int NormalizeBilateralDiameter(int value)
    {
        return Math.Max(3, ((value / 2) * 2) + 1);
    }

    private static bool IsMedianDepthSupported(string depth, int effectiveKernel)
    {
        if (effectiveKernel <= 1)
        {
            return MedianDepths.Contains(depth, StringComparer.Ordinal);
        }

        if (effectiveKernel <= 5)
        {
            return depth is "CV_8U" or "CV_16U" or "CV_16S" or "CV_32F";
        }

        return depth == "CV_8U";
    }

    private static ImageModeRestriction BuildModeRestriction(SpatialFilterMode mode)
    {
        return mode switch
        {
            SpatialFilterMode.Gaussian => new ImageModeRestriction(
                "Gaussian", ImageContractStatus.Native, AllDepths, [1, 3, 4], "None",
                "Preserve input depth/channels.", "Preserve native numeric domain.",
                "IMAGE_DEPTH_UNSUPPORTED", "E2_EXECUTABLE_PROBE",
                "Kernel 1..31; effective kernel is odd; border 0/1/2/4."),
            SpatialFilterMode.Mean => new ImageModeRestriction(
                "Mean", ImageContractStatus.Native, AllDepths, [1, 3, 4], "None",
                "Preserve input depth/channels.", "Preserve native numeric domain.",
                "IMAGE_DEPTH_UNSUPPORTED", "E2_EXECUTABLE_PROBE",
                "Kernel 1..63; even kernels remain even; border 0/1/2/4."),
            SpatialFilterMode.Median => new ImageModeRestriction(
                "Median", ImageContractStatus.Restricted, MedianDepths, [1, 3, 4], "None",
                "Preserve input depth/channels.", "Preserve native numeric domain.",
                "IMAGE_MODE_DEPTH_UNSUPPORTED", "E2_EXECUTABLE_PROBE",
                "Kernel=1 identity for listed depths; effective kernel 3/5 admits 8U/16U/16S/32F; >=7 admits 8U only."),
            SpatialFilterMode.Bilateral => new ImageModeRestriction(
                "Bilateral", ImageContractStatus.Restricted, BilateralDepths, [1, 3], "None",
                "Preserve input depth/channels.", "Preserve native numeric domain.",
                "IMAGE_DEPTH_UNSUPPORTED", "E2_EXECUTABLE_PROBE",
                "Effective diameter=max(3,2*floor(d/2)+1); border 0/1/2/4."),
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null)
        };
    }
}
