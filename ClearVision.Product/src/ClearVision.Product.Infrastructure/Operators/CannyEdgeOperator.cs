// CannyEdgeOperator.cs
// Canny 边缘检测算子
// 对输入图像执行 Canny 边缘提取处理
// 作者：蘅芜君
using ClearVision.Product.Core.Attributes;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Operators;
using ClearVision.Product.Infrastructure.AI.Runtime;
using Microsoft.Extensions.Logging;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;

namespace ClearVision.Product.Infrastructure.Operators;

[OperatorMeta(
    DisplayName = "边缘检测",
    Description = "使用 Canny 进行边缘检测，并可选自动阈值。",
    CategoryId = OperatorCategoryId.FeatureExtraction,
    IconName = "edge",
    Keywords = new[] { "Edge", "Canny", "Contour", "Threshold" }
)]
[InputPort("Image", "Image", PortDataType.Image, IsRequired = true)]
[OutputPort("Image", "Image", PortDataType.Image)]
[OutputPort("Edges", "Edges", PortDataType.Image)]
[OperatorParam("Method", "Method", "enum", DefaultValue = "Canny", Options = new[] { "Canny|Canny", "OnnxEdge|ONNX Edge" })]
[OperatorParam("Threshold1", "Low Threshold", "double", DefaultValue = 50.0, Min = 0.0, Max = 255.0)]
[OperatorParam("Threshold2", "High Threshold", "double", DefaultValue = 150.0, Min = 0.0, Max = 255.0)]
[OperatorParam("AutoThreshold", "Auto Threshold", "bool", DefaultValue = false)]
[OperatorParam("AutoThresholdSigma", "Auto Threshold Sigma", "double", DefaultValue = 0.33, Min = 0.01, Max = 1.0)]
[OperatorParam("AutoThresholdStrategy", "Auto Threshold Strategy", "enum", DefaultValue = "MedianIntensity", Options = new[] { "MedianIntensity|Median Intensity", "GradientPercentile|Gradient Percentile", "RecallGuardPercentile|Recall Guard Percentile", "OtsuGradient|Otsu Gradient" })]
[OperatorParam("EnableGaussianBlur", "Enable Gaussian Blur", "bool", DefaultValue = true)]
[OperatorParam("GaussianKernelSize", "Gaussian Kernel Size", "int", DefaultValue = 5, Min = 3, Max = 15)]
[OperatorParam("ApertureSize", "Sobel Aperture Size", "enum", DefaultValue = "3", Options = new[] { "3|3", "5|5", "7|7" })]
[OperatorParam("L2Gradient", "L2 梯度", "bool", DefaultValue = false, Description = "使用 L2 范数计算梯度幅值，更精确但稍慢")]
[OperatorParam("EdgeModelPath", "Edge Model Path", "file", DefaultValue = "", IsRequired = false)]
[OperatorParam("EdgeModelId", "Edge Model Id", "string", DefaultValue = "", IsRequired = false)]
[OperatorParam("ModelCatalogPath", "Model Catalog Path", "file", DefaultValue = "", IsRequired = false)]
[OperatorParam("EdgeBinarizationThreshold", "Edge Binarization Threshold", "double", DefaultValue = 0.5, Min = 0.0, Max = 1.0, IsRequired = false)]
public class CannyEdgeOperator : OperatorBase
{
    private static readonly string[] SupportedEdgeCatalogTypes = ["edge_detection", "edge", "onnx_edge"];

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

        var method = GetStringParam(@operator, "Method", "Canny");
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

        if (method.Equals("OnnxEdge", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(ExecuteOnnxEdge(@operator, src, workingGray));
        }

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
            else if (autoThresholdStrategy.Equals("RecallGuardPercentile", StringComparison.OrdinalIgnoreCase))
            {
                (threshold1, threshold2) = ComputeGradientPercentileThresholds(processedSrc, 0.50, 0.82);
            }
            else if (autoThresholdStrategy.Equals("OtsuGradient", StringComparison.OrdinalIgnoreCase))
            {
                (threshold1, threshold2) = ComputeOtsuGradientThresholds(processedSrc);
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
            { "Method", method },
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
        var method = GetStringParam(@operator, "Method", "Canny");

        var validMethods = new[] { "Canny", "OnnxEdge" };
        if (!validMethods.Contains(method, StringComparer.OrdinalIgnoreCase))
        {
            return ValidationResult.Invalid("Method must be Canny or OnnxEdge.");
        }

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
        var validAutoThresholdStrategies = new[] { "MedianIntensity", "GradientPercentile", "RecallGuardPercentile", "OtsuGradient" };
        if (!validAutoThresholdStrategies.Contains(autoThresholdStrategy, StringComparer.OrdinalIgnoreCase))
        {
            return ValidationResult.Invalid("AutoThresholdStrategy must be MedianIntensity, GradientPercentile, RecallGuardPercentile or OtsuGradient.");
        }

        if (method.Equals("OnnxEdge", StringComparison.OrdinalIgnoreCase))
        {
            var hasModelSource =
                !string.IsNullOrWhiteSpace(GetStringParam(@operator, "EdgeModelPath", string.Empty)) ||
                !string.IsNullOrWhiteSpace(GetStringParam(@operator, "EdgeModelId", string.Empty)) ||
                !string.IsNullOrWhiteSpace(GetStringParam(@operator, "ModelCatalogPath", string.Empty));
            if (!hasModelSource)
            {
                return ValidationResult.Invalid("OnnxEdge requires EdgeModelPath, EdgeModelId, or ModelCatalogPath.");
            }

            var edgeThreshold = GetDoubleParam(@operator, "EdgeBinarizationThreshold", 0.5);
            if (edgeThreshold is < 0 or > 1)
            {
                return ValidationResult.Invalid("EdgeBinarizationThreshold must be between 0 and 1.");
            }
        }

        return ValidationResult.Valid();
    }

    private static (double Low, double High) ComputeGradientPercentileThresholds(Mat gray)
    {
        return ComputeGradientPercentileThresholds(gray, 0.70, 0.90);
    }

    private static (double Low, double High) ComputeGradientPercentileThresholds(Mat gray, double lowPercentile, double highPercentile)
    {
        using var gradX = new Mat();
        using var gradY = new Mat();
        using var magnitude = new Mat();
        Cv2.Sobel(gray, gradX, MatType.CV_32FC1, 1, 0, 3);
        Cv2.Sobel(gray, gradY, MatType.CV_32FC1, 0, 1, 3);
        Cv2.Magnitude(gradX, gradY, magnitude);

        var low = EstimatePositivePercentile(magnitude, lowPercentile);
        var high = EstimatePositivePercentile(magnitude, highPercentile);
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

    private static (double Low, double High) ComputeOtsuGradientThresholds(Mat gray)
    {
        using var gradX = new Mat();
        using var gradY = new Mat();
        using var magnitude = new Mat();
        using var magnitudeByte = new Mat();
        using var otsuMask = new Mat();
        Cv2.Sobel(gray, gradX, MatType.CV_32FC1, 1, 0, 3);
        Cv2.Sobel(gray, gradY, MatType.CV_32FC1, 0, 1, 3);
        Cv2.Magnitude(gradX, gradY, magnitude);
        Cv2.Normalize(magnitude, magnitudeByte, 0, 255, NormTypes.MinMax, MatType.CV_8UC1);
        var otsu = Cv2.Threshold(magnitudeByte, otsuMask, 0, 255, ThresholdTypes.Binary | ThresholdTypes.Otsu);
        if (otsu <= 0)
        {
            return ComputeGradientPercentileThresholds(gray, 0.55, 0.85);
        }

        var low = Math.Clamp(otsu * 0.50, 1.0, 255.0);
        var high = Math.Clamp(Math.Max(low + 1.0, otsu * 1.15), 1.0, 255.0);
        return (low, high);
    }

    private OperatorExecutionOutput ExecuteOnnxEdge(Operator @operator, Mat src, Mat gray)
    {
        var edgeThreshold = GetDoubleParam(@operator, "EdgeBinarizationThreshold", 0.5, 0, 1);
        ResolvedModelTarget modelTarget;
        try
        {
            modelTarget = ModelCatalog.ResolveExplicitOrCatalog(
                GetStringParam(@operator, "EdgeModelPath", string.Empty),
                GetStringParam(@operator, "EdgeModelId", string.Empty),
                GetStringParam(@operator, "ModelCatalogPath", string.Empty),
                SupportedEdgeCatalogTypes);
        }
        catch (Exception ex)
        {
            return OperatorExecutionOutput.Failure($"Edge model could not be resolved: {ex.Message}");
        }

        if (!File.Exists(modelTarget.ResolvedPath))
        {
            return OperatorExecutionOutput.Failure($"Edge model not found: {modelTarget.ResolvedPath}");
        }

        using var session = new InferenceSession(modelTarget.ResolvedPath);
        var input = session.InputMetadata.First();
        var (inputHeight, inputWidth) = ResolveOnnxInputSize(input.Value.Dimensions, src.Size());
        var tensor = BuildOnnxEdgeInput(src, inputWidth, inputHeight);
        using var results = session.Run([NamedOnnxValue.CreateFromTensor(input.Key, tensor)]);
        var firstOutput = results.FirstOrDefault();
        if (firstOutput is null)
        {
            return OperatorExecutionOutput.Failure("Edge model produced no outputs.");
        }

        using var probability = EdgeOutputToProbabilityMap(firstOutput.AsTensor<float>(), inputWidth, inputHeight);
        using var resizedProbability = new Mat();
        Cv2.Resize(probability, resizedProbability, gray.Size(), 0, 0, InterpolationFlags.Linear);

        using var probabilityByte = new Mat();
        resizedProbability.ConvertTo(probabilityByte, MatType.CV_8UC1, 255.0);
        var dst = new Mat();
        Cv2.Threshold(probabilityByte, dst, Math.Clamp(edgeThreshold, 0, 1) * 255.0, 255, ThresholdTypes.Binary);

        var edgePixelRatio = dst.Width > 0 && dst.Height > 0
            ? (double)Cv2.CountNonZero(dst) / (dst.Width * dst.Height)
            : 0.0;

        return OperatorExecutionOutput.Success(CreateImageOutput(dst, new Dictionary<string, object>
        {
            { "Edges", dst.ToBytes(".png") },
            { "Method", "OnnxEdge" },
            { "Threshold1Used", 0.0 },
            { "Threshold2Used", 0.0 },
            { "AutoThreshold", false },
            { "AutoThresholdStrategy", "OnnxEdge" },
            { "EdgeBinarizationThreshold", edgeThreshold },
            { "EdgePixelRatio", edgePixelRatio },
            { "ModelSource", modelTarget.Source },
            { "EdgeModelId", modelTarget.ModelId },
            { "ResolvedModelCatalogPath", modelTarget.CatalogPath },
            { "InputBitDepth", gray.Depth().ToString() }
        }));
    }

    private static (int Height, int Width) ResolveOnnxInputSize(IReadOnlyList<int> dimensions, Size fallback)
    {
        if (dimensions.Count >= 4)
        {
            var height = dimensions[2] > 0 ? dimensions[2] : fallback.Height;
            var width = dimensions[3] > 0 ? dimensions[3] : fallback.Width;
            return (Math.Max(1, height), Math.Max(1, width));
        }

        return (Math.Max(1, fallback.Height), Math.Max(1, fallback.Width));
    }

    private static DenseTensor<float> BuildOnnxEdgeInput(Mat src, int width, int height)
    {
        using var resized = new Mat();
        Cv2.Resize(src, resized, new Size(width, height), 0, 0, InterpolationFlags.Linear);
        using var rgb = new Mat();
        if (resized.Channels() == 1)
        {
            Cv2.CvtColor(resized, rgb, ColorConversionCodes.GRAY2RGB);
        }
        else
        {
            Cv2.CvtColor(resized, rgb, ColorConversionCodes.BGR2RGB);
        }

        var tensor = new DenseTensor<float>([1, 3, height, width]);
        var indexer = rgb.GetGenericIndexer<Vec3b>();
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var pixel = indexer[y, x];
                tensor[0, 0, y, x] = pixel.Item0 / 255f;
                tensor[0, 1, y, x] = pixel.Item1 / 255f;
                tensor[0, 2, y, x] = pixel.Item2 / 255f;
            }
        }

        return tensor;
    }

    private static Mat EdgeOutputToProbabilityMap(Tensor<float> output, int fallbackWidth, int fallbackHeight)
    {
        var dimensions = output.Dimensions.ToArray();
        var (height, width) = ResolveOutputMapSize(dimensions, fallbackWidth, fallbackHeight);
        var map = new Mat(height, width, MatType.CV_32FC1);
        var minValue = float.PositiveInfinity;
        var maxValue = float.NegativeInfinity;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var value = ReadEdgeOutputValue(output, dimensions, y, x);
                minValue = Math.Min(minValue, value);
                maxValue = Math.Max(maxValue, value);
                map.Set(y, x, value);
            }
        }

        if (!float.IsFinite(minValue) || !float.IsFinite(maxValue))
        {
            map.SetTo(Scalar.Black);
            return map;
        }

        if (minValue < -0.001f || maxValue > 1.001f)
        {
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var value = map.At<float>(y, x);
                    map.Set(y, x, 1.0f / (1.0f + MathF.Exp(-value)));
                }
            }
        }

        return map;
    }

    private static (int Height, int Width) ResolveOutputMapSize(IReadOnlyList<int> dimensions, int fallbackWidth, int fallbackHeight)
    {
        return dimensions.Count switch
        {
            4 when dimensions[1] <= 4 => (Math.Max(1, dimensions[2]), Math.Max(1, dimensions[3])),
            4 => (Math.Max(1, dimensions[1]), Math.Max(1, dimensions[2])),
            3 when dimensions[0] <= 4 => (Math.Max(1, dimensions[1]), Math.Max(1, dimensions[2])),
            3 => (Math.Max(1, dimensions[0]), Math.Max(1, dimensions[1])),
            2 => (Math.Max(1, dimensions[0]), Math.Max(1, dimensions[1])),
            _ => (Math.Max(1, fallbackHeight), Math.Max(1, fallbackWidth))
        };
    }

    private static float ReadEdgeOutputValue(Tensor<float> output, IReadOnlyList<int> dimensions, int y, int x)
    {
        return dimensions.Count switch
        {
            4 when dimensions[1] <= 4 => output[0, 0, y, x],
            4 => output[0, y, x, 0],
            3 when dimensions[0] <= 4 => output[0, y, x],
            3 => output[y, x, 0],
            2 => output[y, x],
            _ => output.Length > 0 ? output.GetValue(0) : 0f
        };
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
