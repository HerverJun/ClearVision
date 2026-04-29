// CannyEdgeOperator.cs
// Canny 边缘检测算子
// 对输入图像执行 Canny 边缘提取处理
// 作者：蘅芜君
using Acme.Product.Core.Attributes;
using Acme.Product.Core.Entities;
using Acme.Product.Core.Enums;
using Acme.Product.Core.Operators;
using Microsoft.Extensions.Logging;
using OpenCvSharp;

namespace Acme.Product.Infrastructure.Operators;

[OperatorMeta(
    DisplayName = "Edge Detection",
    Description = "Detects edges with Canny and optional auto-thresholding.",
    Category = "Feature Extraction",
    IconName = "edge",
    Keywords = new[] { "Edge", "Canny", "Contour", "Threshold" }
)]
[InputPort("Image", "Image", PortDataType.Image, IsRequired = true)]
[OutputPort("Image", "Image", PortDataType.Image)]
[OutputPort("Edges", "Edges", PortDataType.Image)]
[OperatorParam("Threshold1", "Low Threshold", "double", DefaultValue = 50.0, Min = 0.0, Max = 255.0)]
[OperatorParam("Threshold2", "High Threshold", "double", DefaultValue = 150.0, Min = 0.0, Max = 255.0)]
[OperatorParam("AutoThreshold", "Auto Threshold", "bool", DefaultValue = false)]
[OperatorParam("AutoThresholdSigma", "Auto Threshold Sigma", "double", DefaultValue = 0.33, Min = 0.01, Max = 1.0)]
[OperatorParam("AutoThresholdStrategy", "Auto Threshold Strategy", "enum", DefaultValue = "MedianIntensity", Options = new[] { "MedianIntensity|Median Intensity", "GradientPercentile|Gradient Percentile" })]
[OperatorParam("EnableGaussianBlur", "Enable Gaussian Blur", "bool", DefaultValue = true)]
[OperatorParam("GaussianKernelSize", "Gaussian Kernel Size", "int", DefaultValue = 5, Min = 3, Max = 15)]
[OperatorParam("ApertureSize", "Sobel Aperture Size", "enum", DefaultValue = "3", Options = new[] { "3|3", "5|5", "7|7" })]
[OperatorParam("L2Gradient", "L2 梯度", "bool", DefaultValue = false, Description = "使用 L2 范数计算梯度幅值，更精确但稍慢")]
public class CannyEdgeOperator : OperatorBase
{
    public override OperatorType OperatorType => OperatorType.EdgeDetection;

    public CannyEdgeOperator(ILogger<CannyEdgeOperator> logger) : base(logger)
    {
    }

    protected override Task<OperatorExecutionOutput> ExecuteCoreAsync(
        Operator @operator,
        Dictionary<string, object>? inputs,
        CancellationToken cancellationToken)
    {
        if (!TryGetInputImage(inputs, out var imageWrapper) || imageWrapper == null)
        {
            return Task.FromResult(OperatorExecutionOutput.Failure("Input image is required."));
        }

        var threshold1 = GetDoubleParam(@operator, "Threshold1", 50.0, 0, 255);
        var threshold2 = GetDoubleParam(@operator, "Threshold2", 150.0, 0, 255);
        var autoThreshold = GetBoolParam(@operator, "AutoThreshold", false);
        var autoThresholdSigma = GetDoubleParam(@operator, "AutoThresholdSigma", 0.33, 0.01, 1.0);
        var autoThresholdStrategy = GetStringParam(@operator, "AutoThresholdStrategy", "MedianIntensity");
        var enableGaussianBlur = GetBoolParam(@operator, "EnableGaussianBlur", true);
        var gaussianKernelSize = GetIntParam(@operator, "GaussianKernelSize", 5, 1, 31);
        var apertureSize = GetIntParam(@operator, "ApertureSize", 3, 3, 7);
        var l2Gradient = GetBoolParam(@operator, "L2Gradient", false);

        var src = imageWrapper.GetMat();
        if (src.Empty())
        {
            return Task.FromResult(OperatorExecutionOutput.Failure("Input image is invalid."));
        }

        using var gray = OperatorImageDepthHelper.EnsureSingleChannelGray(src);
        using var workingGray = OperatorImageDepthHelper.ConvertSingleChannelToByte(gray, out _, out _);

        using var processedSrc = new Mat();
        if (enableGaussianBlur)
        {
            if (gaussianKernelSize % 2 == 0)
            {
                gaussianKernelSize++;
            }

            Cv2.GaussianBlur(workingGray, processedSrc, new Size(gaussianKernelSize, gaussianKernelSize), 1.0);
        }
        else
        {
            workingGray.CopyTo(processedSrc);
        }

        if (autoThreshold)
        {
            if (autoThresholdStrategy.Equals("GradientPercentile", StringComparison.OrdinalIgnoreCase))
            {
                (threshold1, threshold2) = ComputeGradientPercentileThresholds(processedSrc);
            }
            else
            {
                var median = ComputeMedianIntensity(processedSrc);
                threshold1 = Math.Clamp((1.0 - autoThresholdSigma) * median, 0.0, 255.0);
                threshold2 = Math.Clamp((1.0 + autoThresholdSigma) * median, 0.0, 255.0);
            }

            if (threshold2 <= threshold1)
            {
                threshold2 = Math.Min(255.0, threshold1 + 1.0);
            }
        }

        var dst = new Mat();
        Cv2.Canny(processedSrc, dst, threshold1, threshold2, apertureSize, l2Gradient);
        var edgePixelRatio = dst.Width > 0 && dst.Height > 0
            ? (double)Cv2.CountNonZero(dst) / (dst.Width * dst.Height)
            : 0.0;

        return Task.FromResult(OperatorExecutionOutput.Success(CreateImageOutput(dst, new Dictionary<string, object>
        {
            { "Edges", dst.ToBytes(".png") },
            { "Threshold1Used", threshold1 },
            { "Threshold2Used", threshold2 },
            { "AutoThreshold", autoThreshold },
            { "AutoThresholdStrategy", autoThresholdStrategy },
            { "EdgePixelRatio", edgePixelRatio },
            { "InputBitDepth", gray.Depth().ToString() }
        })));
    }

    public override ValidationResult ValidateParameters(Operator @operator)
    {
        var threshold1 = GetDoubleParam(@operator, "Threshold1", 50.0);
        var threshold2 = GetDoubleParam(@operator, "Threshold2", 150.0);
        var autoThresholdSigma = GetDoubleParam(@operator, "AutoThresholdSigma", 0.33);

        if (threshold1 < 0 || threshold1 > 255)
        {
            return ValidationResult.Invalid("Threshold1 must be between 0 and 255.");
        }

        if (threshold2 < 0 || threshold2 > 255)
        {
            return ValidationResult.Invalid("Threshold2 must be between 0 and 255.");
        }

        if (autoThresholdSigma <= 0 || autoThresholdSigma > 1.0)
        {
            return ValidationResult.Invalid("AutoThresholdSigma must be in (0, 1].");
        }

        var autoThresholdStrategy = GetStringParam(@operator, "AutoThresholdStrategy", "MedianIntensity");
        var validAutoThresholdStrategies = new[] { "MedianIntensity", "GradientPercentile" };
        if (!validAutoThresholdStrategies.Contains(autoThresholdStrategy, StringComparer.OrdinalIgnoreCase))
        {
            return ValidationResult.Invalid("AutoThresholdStrategy must be MedianIntensity or GradientPercentile.");
        }

        return ValidationResult.Valid();
    }

    private static (double Low, double High) ComputeGradientPercentileThresholds(Mat gray)
    {
        using var gradX = new Mat();
        using var gradY = new Mat();
        using var magnitude = new Mat();
        Cv2.Sobel(gray, gradX, MatType.CV_32FC1, 1, 0, 3);
        Cv2.Sobel(gray, gradY, MatType.CV_32FC1, 0, 1, 3);
        Cv2.Magnitude(gradX, gradY, magnitude);

        var low = EstimatePositivePercentile(magnitude, 0.70);
        var high = EstimatePositivePercentile(magnitude, 0.90);
        if (high <= 0.0)
        {
            var fallbackMedian = ComputeMedianIntensity(gray);
            return (
                Math.Clamp(fallbackMedian * 0.67, 0.0, 255.0),
                Math.Clamp(Math.Max(fallbackMedian * 1.33, fallbackMedian + 1.0), 1.0, 255.0));
        }

        if (high <= low)
        {
            high = low * 1.5;
        }

        low = Math.Max(1.0, low);
        high = Math.Max(low + 1.0, high);
        return (low, high);
    }

    private static double EstimatePositivePercentile(Mat values32f, double percentile)
    {
        var values = new List<float>(Math.Min(values32f.Rows * values32f.Cols, 262_144));
        var stride = Math.Max(1, (int)Math.Sqrt(Math.Max(1, values32f.Rows * values32f.Cols / 262_144.0)));
        for (var y = 0; y < values32f.Rows; y += stride)
        {
            for (var x = 0; x < values32f.Cols; x += stride)
            {
                var value = values32f.At<float>(y, x);
                if (float.IsFinite(value) && value > 1e-3f)
                {
                    values.Add(value);
                }
            }
        }

        if (values.Count == 0)
        {
            return 0.0;
        }

        values.Sort();
        var index = (int)Math.Clamp(Math.Round((values.Count - 1) * percentile), 0, values.Count - 1);
        return values[index];
    }

    private static double ComputeMedianIntensity(Mat gray)
    {
        using var hist = new Mat();
        Cv2.CalcHist(
            new[] { gray },
            new[] { 0 },
            null,
            hist,
            1,
            new[] { 256 },
            new[] { new Rangef(0, 256) });

        double total = 0;
        for (var i = 0; i < 256; i++)
        {
            total += hist.At<float>(i);
        }

        if (total <= 0)
        {
            return 0;
        }

        var midpoint = total / 2.0;
        double cumulative = 0;
        for (var i = 0; i < 256; i++)
        {
            cumulative += hist.At<float>(i);
            if (cumulative >= midpoint)
            {
                return i;
            }
        }

        return 255;
    }
}
