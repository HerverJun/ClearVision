using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Services;
using OpenCvSharp;

namespace ClearVision.Product.Infrastructure.Operators;

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
        return imageInputPorts.Select(port => new ImageInputContract(
            port,
            FixedSingleChannelDepths,
            [1, 3, 4],
            FixedSingleChannelDepths,
            "Fixed modes accept native C1 values for 8U/16U/16S/32F/64F. C3/C4 are explicitly converted to gray only for 8U/16U/32F.",
            "C3 BGR or C4 BGRA -> C1 Gray without depth scaling; no implicit depth conversion.",
            "C1 output preserving the admitted gray input depth.",
            "Threshold and ActualThreshold use the input pixel numeric domain; MaxValue uses the output numeric domain.",
            [
                new ImageModeRestriction(
                    "Fixed:Binary/BinaryInv/Trunc/ToZero/ToZeroInv",
                    ImageContractStatus.Restricted,
                    FixedSingleChannelDepths,
                    [1, 3, 4],
                    "Color conversion only; C1 is native.",
                    "Preserve admitted input depth; output C1.",
                    "Native numeric domain.",
                    "IMAGE_MODE_DEPTH_UNSUPPORTED",
                    "E2_EXECUTABLE_PROBE",
                    "C3/C4 exclude CV_16S and CV_64F because OpenCV gray conversion does not admit those depths."),
                new ImageModeRestriction(
                    "Otsu",
                    ImageContractStatus.Restricted,
                    OtsuDepths,
                    [1, 3, 4],
                    "Color -> Gray; UseOtsu is a compatibility alias for the Otsu flag.",
                    "Preserve admitted input depth; output C1.",
                    "ActualThreshold uses the native 8U or 16U input domain.",
                    "IMAGE_MODE_DEPTH_UNSUPPORTED",
                    "E2_EXECUTABLE_PROBE",
                    "Only Binary and BinaryInv base modes."),
                new ImageModeRestriction(
                    "Triangle",
                    ImageContractStatus.Restricted,
                    TriangleDepths,
                    [1, 3, 4],
                    "Color -> Gray.",
                    "CV_8UC1.",
                    "8-bit input domain.",
                    "IMAGE_MODE_DEPTH_UNSUPPORTED",
                    "E2_EXECUTABLE_PROBE",
                    "Only Binary and BinaryInv base modes.")
            ],
            "RejectNaNAndInfinity",
            "IMAGE_DEPTH_UNSUPPORTED",
            OperatorImageContractResolver.ContractVersion,
            ImageContractStatus.Restricted,
            "E2_EXECUTABLE_PROBE")).ToArray();
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
        return imageInputPorts.Select(port => new ImageInputContract(
            port,
            ["CV_8U"],
            [1, 3, 4],
            ["CV_8U"],
            "8U-only native histogram domain for Stage 2; non-8U inputs are rejected before statistics are computed.",
            "C3 BGR/C4 BGRA may convert to C1 Gray, or a B/G/R channel is selected without depth scaling.",
            "Histogram visualization is fixed CV_8UC3; statistics remain in the 0..255 intensity domain.",
            "Fixed [0,256) histogram range with BinCount 2..256 and explicit BinWidth=256/BinCount.",
            [
                new ImageModeRestriction(
                    "Channel=Gray",
                    ImageContractStatus.Restricted,
                    ["CV_8U"],
                    [1, 3, 4],
                    "C3/C4 -> Gray.",
                    "CV_8UC3 chart.",
                    "0..255 intensity units.",
                    "IMAGE_CHANNELS_UNSUPPORTED",
                    "E2_NUMERICAL_ORACLE"),
                new ImageModeRestriction(
                    "Channel=B/G/R",
                    ImageContractStatus.Restricted,
                    ["CV_8U"],
                    [3, 4],
                    "Select native byte channel.",
                    "CV_8UC3 chart.",
                    "0..255 intensity units.",
                    "IMAGE_CHANNELS_UNSUPPORTED",
                    "E2_NUMERICAL_ORACLE")
            ],
            "NotApplicableFor8U",
            "IMAGE_DEPTH_UNSUPPORTED",
            OperatorImageContractResolver.ContractVersion,
            ImageContractStatus.Restricted,
            "E2_NUMERICAL_ORACLE")).ToArray();
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
        var methodRules = new[] { "Laplacian", "Brenner", "Tenengrad", "SMD" }
            .Select(method => new ImageModeRestriction(
                method,
                ImageContractStatus.Restricted,
                SupportedDepths,
                [1, 3, 4],
                "C3 BGR/C4 BGRA -> Gray without depth scaling; score computation widens values without changing their numeric domain.",
                "Passthrough preserves input type; None omits Image; FullOverlay is restricted for uncalibrated float ranges.",
                method == "SMD"
                    ? "Score scales linearly with the native intensity domain."
                    : "Score scales quadratically with the native intensity domain.",
                "IMAGE_MODE_DEPTH_UNSUPPORTED",
                "E2_NUMERICAL_ORACLE",
                "PerMethodDefault thresholds are calibrated for CV_8U only; high-depth default decisions are not ready."))
            .ToArray();

        return imageInputPorts.Select(port => new ImageInputContract(
            port,
            SupportedDepths,
            [1, 3, 4],
            SupportedDepths,
            "Native-value score computation for 8U/16U/32F; depth-specific decision calibration is separate from score support.",
            "Color -> Gray only; internal widening is value-preserving and is not a dynamic-range conversion.",
            "Passthrough preserves input type; None omits Image; overlay policy is mode/depth restricted.",
            "Scores use the admitted input's native numeric domain. PerMethodDefault thresholds are 8U-only.",
            methodRules,
            "RejectNaNAndInfinity",
            "IMAGE_DEPTH_UNSUPPORTED",
            OperatorImageContractResolver.ContractVersion,
            ImageContractStatus.Restricted,
            "E2_NUMERICAL_ORACLE")).ToArray();
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
        return imageInputPorts.Select(port => new ImageInputContract(
            port,
            Depths,
            [1, 3],
            Depths,
            "Mode-dependent business normalization contract retained from Stage 1.",
            "MinMax and Histogram may intentionally change the numeric domain; ZScore widens to CV_32F.",
            "MinMax preserves admitted depth; ZScore outputs CV_32F; Histogram outputs CV_8U.",
            "Explicit business-semantic normalization; data-dependent MinMax is allowed only inside this operator.",
            [
                new ImageModeRestriction("MinMax", ImageContractStatus.Native, Depths, [1, 3], "Explicit normalization.", "Preserve input depth.", "Explicit target range.", "IMAGE_MODE_DEPTH_UNSUPPORTED", "E2_STAGE1_REGRESSION"),
                new ImageModeRestriction("ZScore", ImageContractStatus.Converted, Depths, [1, 3], "Value-preserving conversion to CV_32F before z-score.", "CV_32F.", "Mean 0 / population sigma 1 when non-degenerate.", "IMAGE_NONFINITE_INPUT", "E2_STAGE1_REGRESSION"),
                new ImageModeRestriction("Histogram", ImageContractStatus.Converted, Depths, [1, 3], "Explicit histogram-normalization business mode.", "CV_8U.", "8-bit equalization domain.", "IMAGE_MODE_DEPTH_UNSUPPORTED", "E1_SOURCE_AUDIT")
            ],
            "ModeSpecific",
            "IMAGE_DEPTH_UNSUPPORTED",
            OperatorImageContractResolver.ContractVersion,
            ImageContractStatus.Restricted,
            "E2_STAGE1_REGRESSION")).ToArray();
    }
}

public sealed class LaplacianSharpenImageContractProvider : IOperatorImageContractProvider
{
    public IReadOnlyList<ImageInputContract> GetContracts(
        OperatorType operatorType,
        IReadOnlyList<string> imageInputPorts,
        OperatorLifecycle lifecycle)
    {
        return imageInputPorts.Select(port => new ImageInputContract(
            port,
            ["CV_8U", "CV_16U", "CV_32F"],
            [1, 3],
            ["CV_8U", "CV_16U", "CV_32F"],
            "Stage 1 native-value Laplacian sharpening contract.",
            "Color -> Gray for derivative computation; result is restored in the source numeric domain.",
            "Preserve source depth and channel count.",
            "No MinMax conversion; native-domain Laplacian response.",
            [],
            "RejectNaNAndInfinity",
            "IMAGE_DEPTH_UNSUPPORTED",
            OperatorImageContractResolver.ContractVersion,
            ImageContractStatus.Restricted,
            "E2_STAGE1_REGRESSION")).ToArray();
    }
}

public sealed class SurfaceDefectDetectionImageContractProvider : IOperatorImageContractProvider
{
    public IReadOnlyList<ImageInputContract> GetContracts(
        OperatorType operatorType,
        IReadOnlyList<string> imageInputPorts,
        OperatorLifecycle lifecycle)
    {
        return imageInputPorts.Select(port => new ImageInputContract(
            port,
            ["CV_8U"],
            [1, 3, 4],
            ["CV_8U"],
            port == "Reference"
                ? "Optional 8U reference image admitted for ReferenceDiff paths covered by operator tests."
                : "8U C1/C3/C4 execution retained for GradientMagnitude, LocalContrast, and ReferenceDiff paths covered by operator and package tests.",
            "C3 BGR/C4 BGRA -> C1 Gray for analysis; no depth scaling.",
            "Image output is CV_8UC3; masks and response images remain in the 8-bit domain.",
            "Thresholds and response diagnostics use the legacy 0..255 intensity domain.",
            [
                new ImageModeRestriction(
                    port == "Reference" ? "Method=ReferenceDiff" : "GradientMagnitude/LocalContrast/ReferenceDiff",
                    ImageContractStatus.Restricted,
                    ["CV_8U"],
                    [1, 3, 4],
                    "Color -> Gray only; no depth conversion.",
                    "CV_8U outputs.",
                    "Legacy 8-bit intensity domain.",
                    "IMAGE_MODE_DEPTH_UNSUPPORTED",
                    "E2_OPERATOR_AND_PACKAGE_TESTS",
                    port == "Reference" ? "Reference input is only consumed by ReferenceDiff." : null)
            ],
            "NotApplicableFor8U",
            "IMAGE_DEPTH_UNSUPPORTED",
            OperatorImageContractResolver.ContractVersion,
            ImageContractStatus.Restricted,
            "E2_OPERATOR_AND_PACKAGE_TESTS")).ToArray();
    }
}
