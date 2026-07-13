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

        if (settings.Mode != SpatialFilterMode.Median && settings.BorderType is < 0 or > 7)
        {
            error = "BorderType must be in [0, 7].";
            return false;
        }

        if (settings.Mode == SpatialFilterMode.Median)
        {
            if (settings.KernelSize is < 1 or > 31)
            {
                error = "KernelSize must be in [1, 31] for Median filtering.";
                return false;
            }
        }
        else if ((settings.Mode is SpatialFilterMode.Gaussian or SpatialFilterMode.Mean) &&
                 settings.KernelSize is < 1 or > 63)
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

    public static SpatialFilterAppliedSettings Apply(Mat source, Mat destination, SpatialFilterSettings settings)
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

        var kernelSize = NormalizeOddKernelSize(settings.KernelSize);
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
                Cv2.BilateralFilter(
                    source,
                    destination,
                    settings.Diameter,
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
            settings.Diameter,
            settings.SigmaColor,
            settings.SigmaSpace);
    }

    private static int NormalizeOddKernelSize(int value)
    {
        return value % 2 == 0 ? value + 1 : value;
    }
}
