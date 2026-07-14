using ClearVision.Product.Core.Attributes;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Operators;
using ClearVision.Product.Infrastructure.Memory;
using Microsoft.Extensions.Logging;
using OpenCvSharp;

namespace ClearVision.Product.Infrastructure.Operators;

[OperatorMeta(
    DisplayName = "清晰度评估",
    Description = "评估图像的对焦清晰度。",
    CategoryId = OperatorCategoryId.FeatureExtraction,
    IconName = "focus",
    Keywords = new[] { "sharpness", "focus", "blur", "laplacian", "tenengrad" },
    Version = "1.1.0"
)]
[OperatorImageContractProvider(typeof(SharpnessImageContractProvider))]
[InputPort("Image", "Image", PortDataType.Image, IsRequired = true)]
[OutputPort("Score", "Score", PortDataType.Float)]
[OutputPort("IsSharp", "Is Sharp", PortDataType.Boolean, Description = "仅 DecisionReady=true 时产生。")]
[OutputPort("Image", "Image", PortDataType.Image)]
[OperatorOutputRule("Image", AvailableWhenAll = new[] { "OutputImagePolicy!=None" }, ReasonCode = "SHARPNESS_IMAGE_POLICY")]
[OperatorParam("Method", "Method", "enum", DefaultValue = "Laplacian", Options = new[] { "Laplacian|Laplacian", "Brenner|Brenner", "Tenengrad|Tenengrad", "SMD|SMD" })]
[OperatorParam("ThresholdMode", "Threshold Mode", "enum", DefaultValue = "PerMethodDefault", Options = new[] { "PerMethodDefault|PerMethodDefault", "Manual|Manual" })]
[OperatorParam("Threshold", "Threshold", "double", DefaultValue = 100.0, Min = 0.0)]
[OperatorParam("RoiX", "ROI X", "int", DefaultValue = 0)]
[OperatorParam("RoiY", "ROI Y", "int", DefaultValue = 0)]
[OperatorParam("RoiW", "ROI Width", "int", DefaultValue = 0)]
[OperatorParam("RoiH", "ROI Height", "int", DefaultValue = 0)]
[OperatorParam("OutputImagePolicy", "Output Image Policy", "enum", DefaultValue = "FullOverlay", Options = new[] { "FullOverlay|Full Overlay", "Passthrough|Passthrough", "None|None" })]
public class SharpnessEvaluationOperator : OperatorBase
{
    private static readonly MatPool PassthroughImagePool = new(maxPerBucket: 0, maxTotalGb: 0.0);

    private static readonly IReadOnlyDictionary<string, double> DefaultThresholds =
        new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
        {
            ["Laplacian"] = 100.0,
            ["Brenner"] = 30.0,
            ["Tenengrad"] = 800.0,
            ["SMD"] = 10.0
        };

    public override OperatorType OperatorType => OperatorType.SharpnessEvaluation;

    public SharpnessEvaluationOperator(ILogger<SharpnessEvaluationOperator> logger) : base(logger)
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

        var method = ResolveMethod(GetStringParam(@operator, "Method", "Laplacian"));
        if (method == null)
        {
            return Task.FromResult(OperatorExecutionOutput.Failure("Method must be Laplacian/Brenner/Tenengrad/SMD"));
        }

        var thresholdMode = GetStringParam(@operator, "ThresholdMode", "PerMethodDefault");
        var thresholdUsed = ResolveThreshold(@operator, method, thresholdMode);
        if (double.IsNaN(thresholdUsed))
        {
            return Task.FromResult(OperatorExecutionOutput.Failure("ThresholdMode must be PerMethodDefault or Manual"));
        }

        var outputImagePolicy = ResolveOutputImagePolicy(GetStringParam(@operator, "OutputImagePolicy", "FullOverlay"));
        if (outputImagePolicy == null)
        {
            return Task.FromResult(OperatorExecutionOutput.Failure("OutputImagePolicy must be FullOverlay, Passthrough, or None"));
        }

        if (src.Depth() == MatType.CV_32F && outputImagePolicy == "FullOverlay")
        {
            return Task.FromResult(OperatorExecutionOutput.Failure(BuildFailure(
                "IMAGE_DYNAMIC_RANGE_UNDEFINED",
                src,
                method,
                "FullOverlay is not defined for uncalibrated CV_32F ranges; use Passthrough or None.")));
        }

        var roi = MeasurementRoiHelper.ResolveRoi(@operator, src.Width, src.Height);
        using var roiSource = new Mat(src, roi);
        using var convertedRoiGray = new Mat();
        var roiGray = roiSource;
        if (roiSource.Channels() != 1)
        {
            Cv2.CvtColor(
                roiSource,
                convertedRoiGray,
                roiSource.Channels() == 4 ? ColorConversionCodes.BGRA2GRAY : ColorConversionCodes.BGR2GRAY);
            roiGray = convertedRoiGray;
        }

        if ((method == "Brenner" && roiGray.Cols < 3) ||
            (method == "SMD" && (roiGray.Cols < 2 || roiGray.Rows < 2)))
        {
            return Task.FromResult(OperatorExecutionOutput.Failure(BuildFailure(
                "IMAGE_MODE_DEPTH_UNSUPPORTED",
                src,
                method,
                "ROI is too small for the selected sharpness method.")));
        }

        var score = method switch
        {
            "Laplacian" => ComputeLaplacianVariance(roiGray),
            "Brenner" => ComputeBrenner(roiGray),
            "Tenengrad" => ComputeTenengrad(roiGray),
            "SMD" => ComputeSmd(roiGray),
            _ => ComputeLaplacianVariance(roiGray)
        };
        var tileScores = ComputeTileScores(roiGray, method);
        var tileStdDev = tileScores.Count > 0
            ? MeasurementStatisticsHelper.ComputePopulationStdDev(tileScores, tileScores.Average())
            : 0.0;
        var tileStdError = MeasurementStatisticsHelper.ComputeStandardError(tileStdDev, tileScores.Count);

        var usesManualThreshold = thresholdMode.Equals("Manual", StringComparison.OrdinalIgnoreCase);
        var decisionReady = usesManualThreshold || roiGray.Depth() == MatType.CV_8U;
        var isSharp = decisionReady ? score >= thresholdUsed : (bool?)null;
        var marginToThreshold = decisionReady ? score - thresholdUsed : double.NaN;
        var normalizedScore = decisionReady && thresholdUsed > 1e-9 ? score / thresholdUsed : double.NaN;
        var scoreUnit = method == "SMD" ? "NativeIntensity" : "NativeIntensitySquared";
        var thresholdCalibration = usesManualThreshold
            ? "ManualNativeScoreUnit"
            : roiGray.Depth() == MatType.CV_8U
                ? "PerMethodDefault8U"
                : "PerMethodDefault8UOnly_NotCalibratedForInputDepth";

        var additionalData = new Dictionary<string, object>
        {
            { "Score", score },
            { "Method", method },
            { "ThresholdMode", thresholdMode },
            { "ThresholdUsed", thresholdUsed },
            { "DecisionReady", decisionReady },
            { "ScoreUnit", scoreUnit },
            { "ThresholdCalibration", thresholdCalibration },
            { "TileCount", tileScores.Count },
            { "ScoreStdDev", tileStdDev },
            { "ScoreStdError", tileStdError },
            { "StatusCode", "OK" },
            { "StatusMessage", "Success" },
            { "Confidence", MeasurementStatisticsHelper.ComputeConfidenceFromUncertainty(tileStdError) },
            { "UncertaintyPx", tileStdError }
        };
        if (decisionReady)
        {
            additionalData["IsSharp"] = isSharp!.Value;
            additionalData["NormalizedScore"] = normalizedScore;
            additionalData["MarginToThreshold"] = marginToThreshold;
        }

        var output = outputImagePolicy switch
        {
            "Passthrough" => CreateSharedImageOutput(src, additionalData),
            "None" => CreateDataOutput(src, additionalData),
            _ => CreateImageOutput(CreateOverlayImage(src, roi, method, score, isSharp), additionalData)
        };

        return Task.FromResult(OperatorExecutionOutput.Success(output));
    }

    public override ValidationResult ValidateParameters(Operator @operator)
    {
        var method = ResolveMethod(GetStringParam(@operator, "Method", "Laplacian"));
        if (method == null)
        {
            return ValidationResult.Invalid("Method must be Laplacian/Brenner/Tenengrad/SMD");
        }

        var thresholdMode = GetStringParam(@operator, "ThresholdMode", "PerMethodDefault");
        if (!thresholdMode.Equals("PerMethodDefault", StringComparison.OrdinalIgnoreCase) &&
            !thresholdMode.Equals("Manual", StringComparison.OrdinalIgnoreCase))
        {
            return ValidationResult.Invalid("ThresholdMode must be PerMethodDefault or Manual");
        }

        var threshold = GetDoubleParam(@operator, "Threshold", 100);
        if (!double.IsFinite(threshold) || threshold < 0)
        {
            return ValidationResult.Invalid("Threshold must be finite and >= 0");
        }

        if (ResolveOutputImagePolicy(GetStringParam(@operator, "OutputImagePolicy", "FullOverlay")) == null)
        {
            return ValidationResult.Invalid("OutputImagePolicy must be FullOverlay, Passthrough, or None");
        }

        return ValidationResult.Valid();
    }

    private static string? ResolveMethod(string raw)
    {
        return raw switch
        {
            "Laplacian" => "Laplacian",
            "Brenner" => "Brenner",
            "Tenengrad" => "Tenengrad",
            "SMD" => "SMD",
            _ => null
        };
    }

    private static string? ResolveOutputImagePolicy(string raw)
    {
        return raw switch
        {
            "FullOverlay" => "FullOverlay",
            "Passthrough" => "Passthrough",
            "None" => "None",
            _ => null
        };
    }

    private static Dictionary<string, object> CreateSharedImageOutput(Mat src, Dictionary<string, object> additionalData)
    {
        var output = new Dictionary<string, object>
        {
            { "Image", new ImageWrapper(new Mat(src, new Rect(0, 0, src.Width, src.Height)), PassthroughImagePool) },
            { "Width", src.Width },
            { "Height", src.Height }
        };

        AddAdditionalData(output, additionalData);
        return output;
    }

    private static Dictionary<string, object> CreateDataOutput(Mat src, Dictionary<string, object> additionalData)
    {
        var output = new Dictionary<string, object>
        {
            { "Width", src.Width },
            { "Height", src.Height }
        };

        AddAdditionalData(output, additionalData);
        return output;
    }

    private static void AddAdditionalData(Dictionary<string, object> output, Dictionary<string, object> additionalData)
    {
        foreach (var kvp in additionalData)
        {
            if (!output.ContainsKey(kvp.Key))
            {
                output[kvp.Key] = kvp.Value;
            }
        }
    }

    private static Mat CreateOverlayImage(Mat src, Rect roi, string method, double score, bool? isSharp)
    {
        var resultImage = src.Clone();
        var palette = ResolveOverlayPalette(resultImage);
        Cv2.Rectangle(resultImage, roi, palette.Accent, 1);
        Cv2.PutText(resultImage, $"Sharpness[{method}]={score:F2}", new Point(8, 24), HersheyFonts.HersheySimplex, 0.6, palette.Text, 2);
        var decisionText = isSharp.HasValue ? (isSharp.Value ? "Sharp" : "Blur") : "Decision N/A";
        var decisionColor = isSharp.HasValue
            ? (isSharp.Value ? palette.Positive : palette.Negative)
            : palette.Accent;
        Cv2.PutText(resultImage, decisionText, new Point(8, 48), HersheyFonts.HersheySimplex, 0.7, decisionColor, 2);
        return resultImage;
    }

    private double ResolveThreshold(Operator @operator, string method, string thresholdMode)
    {
        if (thresholdMode.Equals("Manual", StringComparison.OrdinalIgnoreCase))
        {
            return GetDoubleParam(@operator, "Threshold", 100.0, 0);
        }

        if (thresholdMode.Equals("PerMethodDefault", StringComparison.OrdinalIgnoreCase) &&
            DefaultThresholds.TryGetValue(method, out var defaultThreshold))
        {
            return defaultThreshold;
        }

        return double.NaN;
    }

    private static double ComputeLaplacianVariance(Mat gray)
    {
        using var gray64 = new Mat();
        gray.ConvertTo(gray64, MatType.CV_64FC1);
        using var lap = new Mat();
        Cv2.Laplacian(gray64, lap, MatType.CV_64F);
        Cv2.MeanStdDev(lap, out _, out var stddev);
        return stddev[0] * stddev[0];
    }

    private static double ComputeBrenner(Mat gray)
    {
        using var gray64 = new Mat();
        gray.ConvertTo(gray64, MatType.CV_64FC1);
        var sum = 0.0;
        var idx = gray64.GetGenericIndexer<double>();
        for (var y = 0; y < gray64.Rows; y++)
        {
            for (var x = 0; x < gray64.Cols - 2; x++)
            {
                var diff = idx[y, x + 2] - idx[y, x];
                sum += diff * diff;
            }
        }

        return sum / Math.Max(1, gray64.Rows * gray64.Cols);
    }

    private static double ComputeTenengrad(Mat gray)
    {
        using var gray64 = new Mat();
        gray.ConvertTo(gray64, MatType.CV_64FC1);
        using var gradX = new Mat();
        using var gradY = new Mat();
        Cv2.Sobel(gray64, gradX, MatType.CV_64F, 1, 0, 3);
        Cv2.Sobel(gray64, gradY, MatType.CV_64F, 0, 1, 3);

        var idxX = gradX.GetGenericIndexer<double>();
        var idxY = gradY.GetGenericIndexer<double>();
        var sum = 0.0;

        for (var y = 0; y < gray.Rows; y++)
        {
            for (var x = 0; x < gray.Cols; x++)
            {
                var gx = idxX[y, x];
                var gy = idxY[y, x];
                sum += (gx * gx) + (gy * gy);
            }
        }

        return sum / Math.Max(1, gray.Rows * gray.Cols);
    }

    private static double ComputeSmd(Mat gray)
    {
        using var gray64 = new Mat();
        gray.ConvertTo(gray64, MatType.CV_64FC1);
        var idx = gray64.GetGenericIndexer<double>();
        var sum = 0.0;
        for (var y = 0; y < gray64.Rows - 1; y++)
        {
            for (var x = 0; x < gray64.Cols - 1; x++)
            {
                sum += Math.Abs(idx[y, x] - idx[y, x + 1]);
                sum += Math.Abs(idx[y, x] - idx[y + 1, x]);
            }
        }

        return sum / Math.Max(1, gray64.Rows * gray64.Cols);
    }

    private static List<double> ComputeTileScores(Mat gray, string method)
    {
        var tilesX = Math.Clamp(gray.Cols / 32, 1, 4);
        var tilesY = Math.Clamp(gray.Rows / 32, 1, 4);
        var tileScores = new List<double>(tilesX * tilesY);

        for (var tileY = 0; tileY < tilesY; tileY++)
        {
            var y0 = tileY * gray.Rows / tilesY;
            var y1 = (tileY + 1) * gray.Rows / tilesY;
            for (var tileX = 0; tileX < tilesX; tileX++)
            {
                var x0 = tileX * gray.Cols / tilesX;
                var x1 = (tileX + 1) * gray.Cols / tilesX;
                var rect = new Rect(x0, y0, Math.Max(1, x1 - x0), Math.Max(1, y1 - y0));

                using var tile = new Mat(gray, rect);
                var score = method switch
                {
                    "Laplacian" => ComputeLaplacianVariance(tile),
                    "Brenner" => ComputeBrenner(tile),
                    "Tenengrad" => ComputeTenengrad(tile),
                    "SMD" => ComputeSmd(tile),
                    _ => ComputeLaplacianVariance(tile)
                };

                if (double.IsFinite(score))
                {
                    tileScores.Add(score);
                }
            }
        }

        return tileScores;
    }

    private static string BuildFailure(string code, Mat src, string method, string diagnostic)
    {
        var contract = new SharpnessImageContractProvider()
            .GetContracts(OperatorType.SharpnessEvaluation, ["Image"], OperatorLifecycle.Stable)
            .Single();
        return ImageInputRuntimeContractEvaluator.FormatFailure(
            code,
            OperatorType.SharpnessEvaluation,
            contract,
            src,
            method,
            diagnostic);
    }

    private static (Scalar Text, Scalar Accent, Scalar Positive, Scalar Negative) ResolveOverlayPalette(Mat image)
    {
        var max = image.Depth() == MatType.CV_16U ? ushort.MaxValue : byte.MaxValue;
        if (image.Channels() == 1)
        {
            return (new Scalar(max), new Scalar(max * 0.75), new Scalar(max), new Scalar(max * 0.5));
        }

        if (image.Channels() == 4)
        {
            return (
                new Scalar(max, max, max, max),
                new Scalar(0, max, max, max),
                new Scalar(0, max, 0, max),
                new Scalar(0, 0, max, max));
        }

        return (
            new Scalar(max, max, max),
            new Scalar(0, max, max),
            new Scalar(0, max, 0),
            new Scalar(0, 0, max));
    }
}
