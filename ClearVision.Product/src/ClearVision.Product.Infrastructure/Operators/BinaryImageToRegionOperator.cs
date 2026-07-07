using ClearVision.Product.Core.Attributes;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Operators;
using ClearVision.Product.Core.ValueObjects;
using Microsoft.Extensions.Logging;
using OpenCvSharp;

namespace ClearVision.Product.Infrastructure.Operators;

[OperatorMeta(
    DisplayName = "Binary Image To Region",
    Description = "Converts a binary or grayscale image into a run-length encoded Region.",
    Category = "Region",
    IconName = "binary-image-to-region",
    Keywords = new[] { "binary", "image", "region", "mask", "rle", "threshold-to-region", "image-to-region" },
    Version = "1.0.0"
)]
[InputPort("Image", "Binary Image", PortDataType.Image, IsRequired = true)]
[OutputPort("Region", "Region", PortDataType.Region)]
[OutputPort("Image", "Visualization", PortDataType.Image)]
[OutputPort("Area", "Region Area", PortDataType.Integer)]
[OperatorParam("ForegroundMode", "Foreground Mode", "enum", DefaultValue = "NonZero", Options = new[] { "NonZero|Non-zero pixels", "Threshold|Threshold or above" })]
[OperatorParam("Threshold", "Threshold", "int", DefaultValue = 1, Min = 0, Max = 255)]
[OperatorParam("Invert", "Invert Foreground", "bool", DefaultValue = false)]
public sealed class BinaryImageToRegionOperator : OperatorBase
{
    public override OperatorType OperatorType => OperatorType.BinaryImageToRegion;

    public BinaryImageToRegionOperator(ILogger<BinaryImageToRegionOperator> logger) : base(logger)
    {
    }

    protected override Task<OperatorExecutionOutput> ExecuteCoreAsync(
        Operator @operator,
        Dictionary<string, object>? inputs,
        CancellationToken cancellationToken)
    {
        if (!TryGetInputImage(inputs, "Image", out var imageWrapper) || imageWrapper == null)
        {
            return Task.FromResult(OperatorExecutionOutput.Failure("BinaryImageToRegion requires an Image input."));
        }

        var src = imageWrapper.GetMat();
        if (src.Empty())
        {
            return Task.FromResult(OperatorExecutionOutput.Failure("Input image is empty."));
        }

        var foregroundMode = NormalizeForegroundMode(GetStringParam(@operator, "ForegroundMode", "NonZero"));
        var threshold = GetIntParam(@operator, "Threshold", 1, 0, 255);
        var invert = GetBoolParam(@operator, "Invert", false);

        using var gray = CreateGrayMat(src);
        using var binary = new Mat();
        if (foregroundMode.Equals("Threshold", StringComparison.OrdinalIgnoreCase))
        {
            Cv2.Threshold(gray, binary, threshold - 1, 255, ThresholdTypes.Binary);
        }
        else
        {
            Cv2.Threshold(gray, binary, 0, 255, ThresholdTypes.Binary);
        }

        if (invert)
        {
            Cv2.BitwiseNot(binary, binary);
        }

        var region = Region.FromMat(binary, threshold: 1).MergeAdjacentRuns();
        var visualization = CreateVisualization(src, binary, region);

        return Task.FromResult(OperatorExecutionOutput.Success(CreateImageOutput(visualization, new Dictionary<string, object>
        {
            ["Region"] = region,
            ["Area"] = region.Area,
            ["RunCount"] = region.RunLengths.Count,
            ["ForegroundMode"] = foregroundMode,
            ["Threshold"] = threshold,
            ["Invert"] = invert
        })));
    }

    public override ValidationResult ValidateParameters(Operator @operator)
    {
        var foregroundMode = GetStringParam(@operator, "ForegroundMode", "NonZero");
        if (!IsSupportedForegroundMode(foregroundMode))
        {
            return ValidationResult.Invalid("ForegroundMode must be NonZero or Threshold.");
        }

        var threshold = GetIntParam(@operator, "Threshold", 1);
        if (threshold is < 0 or > 255)
        {
            return ValidationResult.Invalid("Threshold must be between 0 and 255.");
        }

        return ValidationResult.Valid();
    }

    private static string NormalizeForegroundMode(string? value)
    {
        return string.Equals(value?.Trim(), "Threshold", StringComparison.OrdinalIgnoreCase)
            ? "Threshold"
            : "NonZero";
    }

    private static bool IsSupportedForegroundMode(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ||
            normalized.Equals("NonZero", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("Threshold", StringComparison.OrdinalIgnoreCase);
    }

    private static Mat CreateGrayMat(Mat src)
    {
        var gray = new Mat();
        var channels = src.Channels();
        if (channels == 1)
        {
            src.CopyTo(gray);
        }
        else if (channels == 3)
        {
            Cv2.CvtColor(src, gray, ColorConversionCodes.BGR2GRAY);
        }
        else if (channels == 4)
        {
            Cv2.CvtColor(src, gray, ColorConversionCodes.BGRA2GRAY);
        }
        else
        {
            Cv2.ExtractChannel(src, gray, 0);
        }

        return gray;
    }

    private static Mat CreateVisualization(Mat src, Mat binary, Region region)
    {
        using var color = CreateColorMat(src);
        var visualization = color.Clone();
        using var overlay = color.Clone();
        overlay.SetTo(new Scalar(0, 255, 0), binary);
        Cv2.AddWeighted(overlay, 0.35, visualization, 0.65, 0, visualization);
        Cv2.PutText(
            visualization,
            $"Region area: {region.Area}",
            new Point(10, 30),
            HersheyFonts.HersheySimplex,
            0.6,
            new Scalar(0, 255, 0),
            2);
        return visualization;
    }

    private static Mat CreateColorMat(Mat src)
    {
        var color = new Mat();
        var channels = src.Channels();
        if (channels == 1)
        {
            Cv2.CvtColor(src, color, ColorConversionCodes.GRAY2BGR);
        }
        else if (channels == 3)
        {
            src.CopyTo(color);
        }
        else if (channels == 4)
        {
            Cv2.CvtColor(src, color, ColorConversionCodes.BGRA2BGR);
        }
        else
        {
            using var gray = new Mat();
            Cv2.ExtractChannel(src, gray, 0);
            Cv2.CvtColor(gray, color, ColorConversionCodes.GRAY2BGR);
        }

        return color;
    }
}
