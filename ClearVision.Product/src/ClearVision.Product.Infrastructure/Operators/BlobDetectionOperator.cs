// BlobDetectionOperator.cs
// Blob检测算子 - 检测图像中的连通区域
// 作者：蘅芜君

using System.Globalization;
using ClearVision.Product.Core.Attributes;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Operators;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
namespace ClearVision.Product.Infrastructure.Operators;

/// <summary>
/// Blob检测算子 - 检测图像中的连通区域
/// </summary>
[OperatorMeta(
    DisplayName = "Blob分析",
    Description = "连通区域分析",
    Category = "特征提取",
    IconName = "blob",
    Keywords = new[] { "连通域", "缺陷区域", "斑点", "面积提取", "缺陷分析", "Blob", "Connected components" },
    Version = "1.2.1"
)]
[InputPort("Image", "二值图像", PortDataType.Image, IsRequired = true, Description = "用于连通域分析的二值图或可自动阈值化的灰度图。")]
[InputPort("SourceImage", "参考图像", PortDataType.Image, IsRequired = false, Description = "可选，仅作为标注结果的参考底图，不替代主 Image 输入。")]
[OutputPort("Image", "标记图像", PortDataType.Image, Description = "绘制 Blob 边界与中心点的可视化图像。")]
[OutputPort("Blobs", "Blob结果列表", PortDataType.BlobList, Description = "兼容旧流程端口名的 Blob 结果字典列表，包含边界框、中心、面积及常用度量；不是 Contour 或 Region。")]
[OutputPort("BlobFeatures", "Blob详细特征", PortDataType.BlobFeatureList, Description = "稳定输出的 Blob 详细特征列表：关闭详细特征时为空列表；开启时 Area、Circularity、CenterX 等旧字段保留在条目顶层，并提供 Features 嵌套别名。不是轮廓或像素区域。")]
[OutputPort("BlobCount", "Blob数量", PortDataType.Integer, Description = "通过面积、形状与可选特征过滤后的 Blob 数量。")]
[OperatorParam("MinArea", "最小面积", "int", DefaultValue = 100, Min = 0)]
[OperatorParam("MaxArea", "最大面积", "int", DefaultValue = 100000, Min = 0)]
[OperatorParam("Color", "目标颜色", "enum", DefaultValue = "White", Options = new[] { "White|白色", "Black|黑色" })]
[OperatorParam("MinCircularity", "最小圆度", "double", DefaultValue = 0.0, Min = 0.0, Max = 1.0)]
[OperatorParam("MinConvexity", "最小凸度", "double", DefaultValue = 0.0, Min = 0.0, Max = 1.0)]
[OperatorParam("MinInertiaRatio", "最小惯性比", "double", DefaultValue = 0.0, Min = 0.0, Max = 1.0)]
[OperatorParam("MinRectangularity", "最小矩形度", "double", DefaultValue = 0.0, Min = 0.0, Max = 1.0)]
[OperatorParam("MinEccentricity", "最小离心率", "double", DefaultValue = 0.0, Min = 0.0, Max = 1.0)]
[OperatorParam("OutputDetailedFeatures", "输出详细特征", "bool", DefaultValue = false)]
[OperatorParam("FeatureFilter", "特征过滤表达式", "string", DefaultValue = "", IsRequired = false, Description = "可选。支持 Area、ContourArea、Perimeter、Circularity、Convexity、Rectangularity、Eccentricity、EulerNumber、MeanGray、GrayDeviation、Width、Height、X、Y、CenterX、CenterY、InertiaRatio、ConvexHullArea、HoleCount；示例：Area >= 100 && Circularity >= 0.8。留空不过滤。")]
[OperatorParam("EnableColorFilter", "启用颜色过滤", "bool", DefaultValue = false, Description = "启用HSV颜色范围预过滤")]
[OperatorParam("HueLow", "色相下限", "int", DefaultValue = 0, Min = 0, Max = 180)]
[OperatorParam("HueHigh", "色相上限", "int", DefaultValue = 180, Min = 0, Max = 180)]
[OperatorParam("SatLow", "饱和度下限", "int", DefaultValue = 50, Min = 0, Max = 255)]
[OperatorParam("SatHigh", "饱和度上限", "int", DefaultValue = 255, Min = 0, Max = 255)]
[OperatorParam("ValLow", "明度下限", "int", DefaultValue = 50, Min = 0, Max = 255)]
[OperatorParam("ValHigh", "明度上限", "int", DefaultValue = 255, Min = 0, Max = 255)]
public class BlobDetectionOperator : OperatorBase
{
    public override OperatorType OperatorType => OperatorType.BlobAnalysis;

    public BlobDetectionOperator(ILogger<BlobDetectionOperator> logger) : base(logger)
    {
    }

    private Task<OperatorExecutionOutput> ExecuteCoreAsync_Legacy(
        Operator @operator,
        Dictionary<string, object>? inputs,
        CancellationToken cancellationToken)
    {
        if (!TryGetInputImage(inputs, "Image", out var imageWrapper) || imageWrapper == null)
        {
            return Task.FromResult(OperatorExecutionOutput.Failure("Input image is required"));
        }

        var minArea = GetFloatParam(@operator, "MinArea", 100f, min: 0);
        var maxArea = GetFloatParam(@operator, "MaxArea", 100000f, min: 0);
        var color = GetStringParam(@operator, "Color", "White");
        var minCircularity = GetDoubleParam(@operator, "MinCircularity", 0.0, min: 0, max: 1.0);
        var minConvexity = GetDoubleParam(@operator, "MinConvexity", 0.0, min: 0, max: 1.0);
        var minInertiaRatio = GetDoubleParam(@operator, "MinInertiaRatio", 0.0, min: 0, max: 1.0);
        var enableColorFilter = GetBoolParam(@operator, "EnableColorFilter", false);
        var hueLow = GetIntParam(@operator, "HueLow", 0, 0, 180);
        var hueHigh = GetIntParam(@operator, "HueHigh", 180, 0, 180);
        var satLow = GetIntParam(@operator, "SatLow", 50, 0, 255);
        var satHigh = GetIntParam(@operator, "SatHigh", 255, 0, 255);
        var valLow = GetIntParam(@operator, "ValLow", 50, 0, 255);
        var valHigh = GetIntParam(@operator, "ValHigh", 255, 0, 255);

        var src = imageWrapper.GetMat();
        if (src.Empty())
        {
            return Task.FromResult(OperatorExecutionOutput.Failure("无法解码输入图像"));
        }

        // 颜色预过滤
        Mat processedSrc = src;
        Mat? colorMask = null;
        if (enableColorFilter)
        {
            colorMask = ApplyColorFilter(src, hueLow, hueHigh, satLow, satHigh, valLow, valHigh);
            if (colorMask != null)
            {
                // 应用掩码到原图
                processedSrc = new Mat();
                Cv2.BitwiseAnd(src, src, processedSrc, colorMask);
            }
        }

        // SimpleBlobDetector 内部会自动处理灰度转换，支持彩色和灰度输入
        var detector = new SimpleBlobDetector.Params();
        detector.FilterByArea = true;
        detector.MinArea = minArea;
        detector.MaxArea = maxArea;
        detector.FilterByColor = true;
        detector.BlobColor = color.Equals("Black", StringComparison.OrdinalIgnoreCase) ? (byte)0 : (byte)255;

        if (minCircularity > 0)
        {
            detector.FilterByCircularity = true;
            detector.MinCircularity = (float)minCircularity;
        }

        if (minConvexity > 0)
        {
            detector.FilterByConvexity = true;
            detector.MinConvexity = (float)minConvexity;
        }

        if (minInertiaRatio > 0)
        {
            detector.FilterByInertia = true;
            detector.MinInertiaRatio = (float)minInertiaRatio;
        }

        using var blobDetector = SimpleBlobDetector.Create(detector);
        var keypoints = blobDetector.Detect(processedSrc);

        // 准备彩色结果图（用于绘制彩色标注）
        var colorSrc = new Mat();
        if (processedSrc.Channels() == 1)
            Cv2.CvtColor(processedSrc, colorSrc, ColorConversionCodes.GRAY2BGR);
        else
            processedSrc.CopyTo(colorSrc);

        foreach (var kp in keypoints)
        {
            Cv2.Circle(colorSrc, (int)kp.Pt.X, (int)kp.Pt.Y, (int)kp.Size / 2, new Scalar(0, 255, 0), 2);
            Cv2.Circle(colorSrc, (int)kp.Pt.X, (int)kp.Pt.Y, 3, new Scalar(0, 0, 255), -1);
        }

        // P0: 使用ImageWrapper实现零拷贝输出
        var additionalData = new Dictionary<string, object>
        {
            { "BlobCount", keypoints.Length },
            { "Blobs", keypoints.Select(kp => new Dictionary<string, object>
            {
                { "X", kp.Pt.X },
                { "Y", kp.Pt.Y },
                { "Size", kp.Size },
                { "Area", Math.PI * Math.Pow(kp.Size / 2, 2) }
            }).ToList() }
        };

        return Task.FromResult(OperatorExecutionOutput.Success(CreateImageOutput(colorSrc, additionalData)));
    }

    protected override Task<OperatorExecutionOutput> ExecuteCoreAsync(
        Operator @operator,
        Dictionary<string, object>? inputs,
        CancellationToken cancellationToken)
    {
        if (!TryGetInputImage(inputs, "Image", out var imageWrapper) || imageWrapper == null)
        {
            return Task.FromResult(OperatorExecutionOutput.Failure("Input image is required"));
        }

        ImageWrapper? sourceWrapper = null;
        TryGetInputImage(inputs, "SourceImage", out sourceWrapper);

        var minArea = GetFloatParam(@operator, "MinArea", 100f, min: 0);
        var maxArea = GetFloatParam(@operator, "MaxArea", 100000f, min: 0);
        var color = GetStringParam(@operator, "Color", "White");
        var minCircularity = GetDoubleParam(@operator, "MinCircularity", 0.0, min: 0, max: 1.0);
        var minConvexity = GetDoubleParam(@operator, "MinConvexity", 0.0, min: 0, max: 1.0);
        var minInertiaRatio = GetDoubleParam(@operator, "MinInertiaRatio", 0.0, min: 0, max: 1.0);
        var minRectangularity = GetDoubleParam(@operator, "MinRectangularity", 0.0, min: 0, max: 1.0);
        var minEccentricity = GetDoubleParam(@operator, "MinEccentricity", 0.0, min: 0, max: 1.0);
        var outputDetailedFeatures = GetBoolParam(@operator, "OutputDetailedFeatures", false);
        var featureFilter = GetStringParam(@operator, "FeatureFilter", string.Empty);
        var enableColorFilter = GetBoolParam(@operator, "EnableColorFilter", false);
        var hueLow = GetIntParam(@operator, "HueLow", 0, 0, 180);
        var hueHigh = GetIntParam(@operator, "HueHigh", 180, 0, 180);
        var satLow = GetIntParam(@operator, "SatLow", 50, 0, 255);
        var satHigh = GetIntParam(@operator, "SatHigh", 255, 0, 255);
        var valLow = GetIntParam(@operator, "ValLow", 50, 0, 255);
        var valHigh = GetIntParam(@operator, "ValHigh", 255, 0, 255);

        var src = imageWrapper.GetMat();
            if (src.Empty())
            {
                return Task.FromResult(OperatorExecutionOutput.Failure("Input image is invalid"));
            }

            CompiledFeatureFilter? compiledFeatureFilter = null;
            if (!string.IsNullOrWhiteSpace(featureFilter) &&
                !TryCompileFeatureFilter(featureFilter, out compiledFeatureFilter, out var compileError))
            {
                return Task.FromResult(OperatorExecutionOutput.Failure(FormatFeatureFilterError(compileError)));
            }

            Mat sourceMat = sourceWrapper?.GetMat() ?? src;
        if (sourceMat.Empty())
        {
            sourceMat = src;
        }

        Mat? graySource = null;
        Mat? colorMask = null;

        try
        {
            if (!sourceMat.Empty())
            {
                graySource = new Mat();
                if (sourceMat.Channels() == 1)
                {
                    sourceMat.CopyTo(graySource);
                }
                else
                {
                    Cv2.CvtColor(sourceMat, graySource, ColorConversionCodes.BGR2GRAY);
                }
            }

            using var gray = new Mat();
            if (src.Channels() == 1)
            {
                src.CopyTo(gray);
            }
            else
            {
                Cv2.CvtColor(src, gray, ColorConversionCodes.BGR2GRAY);
            }

            using var binary = new Mat();
            ApplyAutomaticThreshold(gray, binary);

            if (color.Equals("Black", StringComparison.OrdinalIgnoreCase))
            {
                Cv2.BitwiseNot(binary, binary);
            }

            if (enableColorFilter)
            {
                colorMask = ApplyColorFilter(sourceMat ?? src, hueLow, hueHigh, satLow, satHigh, valLow, valHigh);
                if (colorMask != null && colorMask.Size() == binary.Size())
                {
                    Cv2.BitwiseAnd(binary, colorMask, binary);
                }
            }

            using var labels = new Mat();
            using var stats = new Mat();
            using var centroids = new Mat();
            var labelCount = Cv2.ConnectedComponentsWithStats(binary, labels, stats, centroids, PixelConnectivity.Connectivity8, MatType.CV_32S);

            var resultImage = new Mat();
            if (sourceMat!.Channels() == 1)
            {
                Cv2.CvtColor(sourceMat, resultImage, ColorConversionCodes.GRAY2BGR);
            }
            else
            {
                sourceMat.CopyTo(resultImage);
            }

            var blobs = new List<Dictionary<string, object>>();
            var blobFeatures = new List<Dictionary<string, object>>();
            var nextId = 1;
            string? filterError = null;

            for (var label = 1; label < labelCount; label++)
            {
                var area = stats.At<int>(label, (int)ConnectedComponentsTypes.Area);
                if (area < minArea || area > maxArea)
                {
                    continue;
                }

                var left = stats.At<int>(label, (int)ConnectedComponentsTypes.Left);
                var top = stats.At<int>(label, (int)ConnectedComponentsTypes.Top);
                var width = stats.At<int>(label, (int)ConnectedComponentsTypes.Width);
                var height = stats.At<int>(label, (int)ConnectedComponentsTypes.Height);

                if (width <= 0 || height <= 0)
                {
                    continue;
                }

                var rect = new Rect(left, top, width, height);

                using var labelRoi = new Mat(labels, rect);
                using var mask = new Mat();
                Cv2.Compare(labelRoi, label, mask, CmpType.EQ);

                Cv2.FindContours(mask, out Point[][] contours, out HierarchyIndex[] hierarchy, RetrievalModes.CComp, ContourApproximationModes.ApproxSimple);
                if (contours.Length == 0)
                {
                    continue;
                }

                var externalIndex = FindExternalContourIndex(hierarchy);
                if (externalIndex < 0 || externalIndex >= contours.Length)
                {
                    externalIndex = 0;
                }

                var contour = contours[externalIndex];
                if (contour.Length < 3)
                {
                    continue;
                }

                var perimeter = Math.Max(1e-6, Cv2.ArcLength(contour, true));
                var contourArea = Math.Abs(Cv2.ContourArea(contour));
                var hull = Cv2.ConvexHull(contour);
                var hullArea = hull.Length >= 3 ? Math.Abs(Cv2.ContourArea(hull)) : 0.0;
                var convexity = hullArea > 0 ? contourArea / hullArea : 0.0;

                var rectArea = (double)width * height;
                var rectangularity = rectArea > 0 ? contourArea / rectArea : 0.0;

                var circularity = ComputeCircularity(contour, contourArea, perimeter);

                var moments = Cv2.Moments(mask, true);
                var (eccentricity, inertiaRatio) = ComputeEccentricityAndInertia(moments);

                var holeCount = CountHoles(hierarchy, externalIndex);
                var eulerNumber = 1 - holeCount;

                var centerX = centroids.At<double>(label, 0);
                var centerY = centroids.At<double>(label, 1);

                var meanGray = 0.0;
                var grayDeviation = 0.0;
                if (graySource != null && !graySource.Empty() &&
                    rect.X >= 0 && rect.Y >= 0 &&
                    rect.X + rect.Width <= graySource.Width &&
                    rect.Y + rect.Height <= graySource.Height)
                {
                    using var grayRoi = new Mat(graySource, rect);
                    Cv2.MeanStdDev(grayRoi, out Scalar mean, out Scalar stddev, mask);
                    meanGray = mean.Val0;
                    grayDeviation = stddev.Val0;
                }

                if (minCircularity > 0 && circularity < minCircularity)
                {
                    continue;
                }

                if (minConvexity > 0 && convexity < minConvexity)
                {
                    continue;
                }

                if (minInertiaRatio > 0 && inertiaRatio < minInertiaRatio)
                {
                    continue;
                }

                if (minRectangularity > 0 && rectangularity < minRectangularity)
                {
                    continue;
                }

                if (minEccentricity > 0 && eccentricity < minEccentricity)
                {
                    continue;
                }

                var featureValues = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Area"] = area,
                    ["ContourArea"] = contourArea,
                    ["Perimeter"] = perimeter,
                    ["Circularity"] = circularity,
                    ["Convexity"] = convexity,
                    ["Rectangularity"] = rectangularity,
                    ["Eccentricity"] = eccentricity,
                    ["EulerNumber"] = eulerNumber,
                    ["MeanGray"] = meanGray,
                    ["GrayDeviation"] = grayDeviation,
                    ["Width"] = width,
                    ["Height"] = height,
                    ["X"] = left,
                    ["Y"] = top,
                    ["CenterX"] = centerX,
                    ["CenterY"] = centerY,
                    ["InertiaRatio"] = inertiaRatio,
                    ["ConvexHullArea"] = hullArea,
                    ["HoleCount"] = holeCount
                };

                if (compiledFeatureFilter != null)
                {
                    if (!compiledFeatureFilter.TryEvaluate(featureValues, out var passed, out var errorMessage))
                    {
                        filterError = errorMessage;
                        break;
                    }

                    if (!passed)
                    {
                        continue;
                    }
                }

                var blobId = nextId++;
                var blobInfo = new Dictionary<string, object>
                {
                    { "Id", blobId },
                    { "Area", area },
                    { "ContourArea", contourArea },
                    { "Perimeter", perimeter },
                    { "Circularity", circularity },
                    { "Convexity", convexity },
                    { "Rectangularity", rectangularity },
                    { "Eccentricity", eccentricity },
                    { "EulerNumber", eulerNumber },
                    { "MeanGray", meanGray },
                    { "GrayDeviation", grayDeviation },
                    { "X", left },
                    { "Y", top },
                    { "Width", width },
                    { "Height", height },
                    { "CenterX", centerX },
                    { "CenterY", centerY },
                    { "InertiaRatio", inertiaRatio },
                    { "ConvexHullArea", hullArea },
                    { "HoleCount", holeCount }
                };

                blobs.Add(blobInfo);

                if (outputDetailedFeatures)
                {
                    var legacyCompatibleFeature = new Dictionary<string, object>(blobInfo, StringComparer.OrdinalIgnoreCase)
                    {
                        ["BlobId"] = blobId,
                        ["Features"] = featureValues.ToDictionary(
                            pair => pair.Key,
                            pair => (object)pair.Value,
                            StringComparer.OrdinalIgnoreCase)
                    };

                    blobFeatures.Add(legacyCompatibleFeature);
                }

                var offsetContour = contour.Select(p => new Point(p.X + rect.X, p.Y + rect.Y)).ToArray();
                Cv2.DrawContours(resultImage, new[] { offsetContour }, -1, new Scalar(0, 255, 0), 2);
                Cv2.Circle(resultImage, (int)Math.Round(centerX), (int)Math.Round(centerY), 3, new Scalar(0, 255, 0), -1);
            }

            if (!string.IsNullOrWhiteSpace(filterError))
            {
                resultImage.Dispose();
                return Task.FromResult(OperatorExecutionOutput.Failure(
                    FormatFeatureFilterError(filterError)));
            }

            var additionalData = new Dictionary<string, object>
            {
                { "BlobCount", blobs.Count },
                { "Blobs", blobs },
                { "BlobFeatures", blobFeatures }
            };

            return Task.FromResult(OperatorExecutionOutput.Success(CreateImageOutput(resultImage, additionalData)));
        }
        finally
        {
            graySource?.Dispose();
            colorMask?.Dispose();
        }
    }

    public override ValidationResult ValidateParameters(Operator @operator)
    {
        var minArea = GetFloatParam(@operator, "MinArea", 100f);
        var maxArea = GetFloatParam(@operator, "MaxArea", 100000f);

        if (minArea < 0 || maxArea < 0)
        {
            return ValidationResult.Invalid("面积范围不能为负数");
        }

        if (minArea >= maxArea)
        {
            return ValidationResult.Invalid("最小面积必须小于最大面积");
        }

        var color = GetStringParam(@operator, "Color", "White");
        var validColors = new[] { "White", "Black" };
        if (!validColors.Contains(color, StringComparer.OrdinalIgnoreCase))
        {
            return ValidationResult.Invalid("Color must be White or Black.");
        }

        return ValidationResult.Valid();
    }

    private static (double eccentricity, double inertiaRatio) ComputeEccentricityAndInertia(Moments moments)
    {
        var m00 = moments.M00;
        if (m00 <= 0)
        {
            return (0, 0);
        }

        var a = moments.Mu20 / m00;
        var b = 2 * moments.Mu11 / m00;
        var c = moments.Mu02 / m00;
        var temp = Math.Sqrt(Math.Max(0, (a - c) * (a - c) + b * b));

        var lambda1 = (a + c + temp) / 2.0;
        var lambda2 = (a + c - temp) / 2.0;

        if (lambda1 <= 1e-12)
        {
            return (0, 0);
        }

        var inertiaRatio = lambda2 / lambda1;
        if (inertiaRatio < 0)
        {
            inertiaRatio = 0;
        }

        var eccentricity = Math.Sqrt(Math.Max(0, 1 - inertiaRatio));
        return (eccentricity, inertiaRatio);
    }

    private static int FindExternalContourIndex(HierarchyIndex[] hierarchy)
    {
        if (hierarchy == null || hierarchy.Length == 0)
        {
            return -1;
        }

        for (var i = 0; i < hierarchy.Length; i++)
        {
            if (hierarchy[i].Parent < 0)
            {
                return i;
            }
        }

        return -1;
    }

    private static void ApplyAutomaticThreshold(Mat gray, Mat binary)
    {
        Cv2.MinMaxLoc(gray, out double minVal, out double maxVal);
        if (maxVal <= minVal)
        {
            // Low-dynamic frames are effectively uniform; keep output stable and avoid full-foreground masks.
            binary.Create(gray.Rows, gray.Cols, MatType.CV_8UC1);
            binary.SetTo(Scalar.Black);
            return;
        }

        Cv2.Threshold(gray, binary, 0, 255, ThresholdTypes.Binary | ThresholdTypes.Otsu);
    }

    private static int CountHoles(HierarchyIndex[] hierarchy, int externalIndex)
    {
        if (hierarchy == null || hierarchy.Length == 0 || externalIndex < 0)
        {
            return 0;
        }

        var holes = 0;
        for (var i = 0; i < hierarchy.Length; i++)
        {
            if (hierarchy[i].Parent == externalIndex)
            {
                holes++;
            }
        }

        return holes;
    }

    private static double ComputeCircularity(Point[] contour, double contourArea, double contourPerimeter)
    {
        if (contourArea <= 0 || contourPerimeter <= 1e-12)
        {
            return 0.0;
        }

        try
        {
            // Raw circularity based on pixel contour is sensitive to rasterization.
            // We approximate the contour to suppress staircase artifacts and use the
            // simplified perimeter for a closer-to-analytic estimate.
            var perimeter = contourPerimeter;
            if (contour.Length >= 12)
            {
                var epsilon = Math.Max(1.0, contourPerimeter * 0.002);
                var approx = Cv2.ApproxPolyDP(contour, epsilon, true);
                if (approx.Length >= 3)
                {
                    perimeter = Math.Max(1e-6, Cv2.ArcLength(approx, true));
                }
            }

            var circularity = 4 * Math.PI * contourArea / (perimeter * perimeter);

            if (double.IsNaN(circularity) || double.IsInfinity(circularity))
            {
                return 0.0;
            }

            return Math.Clamp(circularity, 0.0, 1.0);
        }
        catch
        {
            var circularity = 4 * Math.PI * contourArea / (contourPerimeter * contourPerimeter);
            if (double.IsNaN(circularity) || double.IsInfinity(circularity))
            {
                return 0.0;
            }

            return Math.Clamp(circularity, 0.0, 1.0);
        }
    }

    private static readonly IReadOnlySet<string> FeatureFilterFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Area",
        "ContourArea",
        "Perimeter",
        "Circularity",
        "Convexity",
        "Rectangularity",
        "Eccentricity",
        "EulerNumber",
        "MeanGray",
        "GrayDeviation",
        "Width",
        "Height",
        "X",
        "Y",
        "CenterX",
        "CenterY",
        "InertiaRatio",
        "ConvexHullArea",
        "HoleCount"
    };

    private static bool TryCompileFeatureFilter(
        string filter,
        out CompiledFeatureFilter? compiled,
        out string? errorMessage)
    {
        compiled = null;
        errorMessage = null;

        try
        {
            var parser = new FeatureFilterParser(filter, FeatureFilterFields);
            compiled = new CompiledFeatureFilter(parser.Parse());
            return true;
        }
        catch (FeatureFilterParseException ex)
        {
            errorMessage = ex.Message;
            return false;
        }
    }

    private static string FormatFeatureFilterError(string? errorMessage)
    {
        return $"FeatureFilter 表达式无效：{errorMessage ?? "未知错误"}。支持字段：Area、Circularity、Convexity、Rectangularity、Eccentricity、MeanGray、Width、Height、CenterX、CenterY 等；示例：Area >= 100 && Circularity >= 0.8。";
    }

    private sealed class CompiledFeatureFilter
    {
        private readonly FeatureFilterNode _root;

        public CompiledFeatureFilter(FeatureFilterNode root)
        {
            _root = root;
        }

        public bool TryEvaluate(
            IReadOnlyDictionary<string, double> values,
            out bool passed,
            out string? errorMessage)
        {
            try
            {
                var result = _root.Evaluate(values);
                passed = Math.Abs(result) > 1e-12;
                errorMessage = null;
                return true;
            }
            catch (Exception ex)
            {
                passed = false;
                errorMessage = ex.Message;
                return false;
            }
        }
    }

    private abstract class FeatureFilterNode
    {
        public abstract double Evaluate(IReadOnlyDictionary<string, double> values);

        protected static double EnsureFinite(double value)
        {
            if (!double.IsFinite(value))
            {
                throw new InvalidOperationException("计算结果不是有限数");
            }

            return value;
        }

        protected static bool IsTrue(double value) => Math.Abs(value) > 1e-12;
    }

    private sealed class FeatureFilterLiteralNode : FeatureFilterNode
    {
        private readonly double _value;

        public FeatureFilterLiteralNode(double value)
        {
            _value = value;
        }

        public override double Evaluate(IReadOnlyDictionary<string, double> values) => _value;
    }

    private sealed class FeatureFilterFieldNode : FeatureFilterNode
    {
        private readonly string _name;

        public FeatureFilterFieldNode(string name)
        {
            _name = name;
        }

        public override double Evaluate(IReadOnlyDictionary<string, double> values)
        {
            if (!values.TryGetValue(_name, out var value))
            {
                throw new InvalidOperationException($"运行时缺少 FeatureFilter 字段：{_name}");
            }

            return value;
        }
    }

    private sealed class FeatureFilterUnaryNode : FeatureFilterNode
    {
        private readonly FeatureFilterUnaryOperator _operator;
        private readonly FeatureFilterNode _operand;

        public FeatureFilterUnaryNode(FeatureFilterUnaryOperator @operator, FeatureFilterNode operand)
        {
            _operator = @operator;
            _operand = operand;
        }

        public override double Evaluate(IReadOnlyDictionary<string, double> values)
        {
            var operand = _operand.Evaluate(values);
            return _operator switch
            {
                FeatureFilterUnaryOperator.Plus => operand,
                FeatureFilterUnaryOperator.Minus => EnsureFinite(-operand),
                FeatureFilterUnaryOperator.Not => IsTrue(operand) ? 0 : 1,
                _ => throw new InvalidOperationException("未知的一元运算符")
            };
        }
    }

    private sealed class FeatureFilterBinaryNode : FeatureFilterNode
    {
        private readonly FeatureFilterBinaryOperator _operator;
        private readonly FeatureFilterNode _left;
        private readonly FeatureFilterNode _right;

        public FeatureFilterBinaryNode(
            FeatureFilterBinaryOperator @operator,
            FeatureFilterNode left,
            FeatureFilterNode right)
        {
            _operator = @operator;
            _left = left;
            _right = right;
        }

        public override double Evaluate(IReadOnlyDictionary<string, double> values)
        {
            var left = _left.Evaluate(values);
            if (_operator == FeatureFilterBinaryOperator.And)
            {
                return IsTrue(left) && IsTrue(_right.Evaluate(values)) ? 1 : 0;
            }

            if (_operator == FeatureFilterBinaryOperator.Or)
            {
                return IsTrue(left) || IsTrue(_right.Evaluate(values)) ? 1 : 0;
            }

            var right = _right.Evaluate(values);
            return _operator switch
            {
                FeatureFilterBinaryOperator.Add => EnsureFinite(left + right),
                FeatureFilterBinaryOperator.Subtract => EnsureFinite(left - right),
                FeatureFilterBinaryOperator.Multiply => EnsureFinite(left * right),
                FeatureFilterBinaryOperator.Divide => right == 0
                    ? throw new InvalidOperationException("FeatureFilter 不允许除以零")
                    : EnsureFinite(left / right),
                FeatureFilterBinaryOperator.Modulo => right == 0
                    ? throw new InvalidOperationException("FeatureFilter 不允许对零取模")
                    : EnsureFinite(left % right),
                FeatureFilterBinaryOperator.Equal => NearlyEqual(left, right) ? 1 : 0,
                FeatureFilterBinaryOperator.NotEqual => NearlyEqual(left, right) ? 0 : 1,
                FeatureFilterBinaryOperator.Less => left < right ? 1 : 0,
                FeatureFilterBinaryOperator.LessOrEqual => left <= right ? 1 : 0,
                FeatureFilterBinaryOperator.Greater => left > right ? 1 : 0,
                FeatureFilterBinaryOperator.GreaterOrEqual => left >= right ? 1 : 0,
                _ => throw new InvalidOperationException("未知的二元运算符")
            };
        }

        private static bool NearlyEqual(double left, double right)
        {
            return Math.Abs(left - right) <= 1e-12;
        }
    }

    private enum FeatureFilterUnaryOperator
    {
        Plus,
        Minus,
        Not
    }

    private enum FeatureFilterBinaryOperator
    {
        And,
        Or,
        Add,
        Subtract,
        Multiply,
        Divide,
        Modulo,
        Equal,
        NotEqual,
        Less,
        LessOrEqual,
        Greater,
        GreaterOrEqual
    }

    private sealed class FeatureFilterParser
    {
        private readonly FeatureFilterLexer _lexer;
        private readonly IReadOnlySet<string> _allowedFields;
        private FeatureFilterToken _current;

        public FeatureFilterParser(string expression, IReadOnlySet<string> allowedFields)
        {
            _lexer = new FeatureFilterLexer(expression);
            _allowedFields = allowedFields;
            _current = _lexer.Next();
        }

        public FeatureFilterNode Parse()
        {
            var expression = ParseOr();
            if (_current.Kind != FeatureFilterTokenKind.End)
            {
                throw Error($"表达式包含无法识别的内容“{_current.Text}”");
            }

            return expression;
        }

        private FeatureFilterNode ParseOr()
        {
            var left = ParseAnd();
            while (Match(FeatureFilterTokenKind.Or))
            {
                left = new FeatureFilterBinaryNode(FeatureFilterBinaryOperator.Or, left, ParseAnd());
            }

            return left;
        }

        private FeatureFilterNode ParseAnd()
        {
            var left = ParseComparison();
            while (Match(FeatureFilterTokenKind.And))
            {
                left = new FeatureFilterBinaryNode(FeatureFilterBinaryOperator.And, left, ParseComparison());
            }

            return left;
        }

        private FeatureFilterNode ParseComparison()
        {
            var left = ParseAdditive();
            if (!TryReadComparisonOperator(out var @operator))
            {
                return left;
            }

            var right = ParseAdditive();
            if (IsComparisonOperator(_current.Kind))
            {
                throw Error("FeatureFilter 每个表达式只允许一个比较运算符");
            }

            return new FeatureFilterBinaryNode(@operator, left, right);
        }

        private FeatureFilterNode ParseAdditive()
        {
            var left = ParseMultiplicative();
            while (_current.Kind is FeatureFilterTokenKind.Plus or FeatureFilterTokenKind.Minus)
            {
                var @operator = _current.Kind == FeatureFilterTokenKind.Plus
                    ? FeatureFilterBinaryOperator.Add
                    : FeatureFilterBinaryOperator.Subtract;
                Advance();
                left = new FeatureFilterBinaryNode(@operator, left, ParseMultiplicative());
            }

            return left;
        }

        private FeatureFilterNode ParseMultiplicative()
        {
            var left = ParseUnary();
            while (_current.Kind is FeatureFilterTokenKind.Star or FeatureFilterTokenKind.Slash or FeatureFilterTokenKind.Percent)
            {
                var @operator = _current.Kind switch
                {
                    FeatureFilterTokenKind.Star => FeatureFilterBinaryOperator.Multiply,
                    FeatureFilterTokenKind.Slash => FeatureFilterBinaryOperator.Divide,
                    _ => FeatureFilterBinaryOperator.Modulo
                };
                Advance();
                left = new FeatureFilterBinaryNode(@operator, left, ParseUnary());
            }

            return left;
        }

        private FeatureFilterNode ParseUnary()
        {
            if (_current.Kind is FeatureFilterTokenKind.Plus or FeatureFilterTokenKind.Minus or FeatureFilterTokenKind.Not)
            {
                var @operator = _current.Kind switch
                {
                    FeatureFilterTokenKind.Plus => FeatureFilterUnaryOperator.Plus,
                    FeatureFilterTokenKind.Minus => FeatureFilterUnaryOperator.Minus,
                    _ => FeatureFilterUnaryOperator.Not
                };
                Advance();
                return new FeatureFilterUnaryNode(@operator, ParseUnary());
            }

            return ParsePrimary();
        }

        private FeatureFilterNode ParsePrimary()
        {
            var token = _current;
            switch (token.Kind)
            {
                case FeatureFilterTokenKind.Number:
                    Advance();
                    return new FeatureFilterLiteralNode(token.Number);
                case FeatureFilterTokenKind.True:
                    Advance();
                    return new FeatureFilterLiteralNode(1);
                case FeatureFilterTokenKind.False:
                    Advance();
                    return new FeatureFilterLiteralNode(0);
                case FeatureFilterTokenKind.Identifier:
                    Advance();
                    if (!_allowedFields.Contains(token.Text))
                    {
                        throw Error($"未知 FeatureFilter 字段“{token.Text}”");
                    }

                    return new FeatureFilterFieldNode(token.Text);
                case FeatureFilterTokenKind.LeftParen:
                    Advance();
                    var nested = ParseOr();
                    Expect(FeatureFilterTokenKind.RightParen, "缺少右括号“)”");
                    return nested;
                case FeatureFilterTokenKind.End:
                    throw Error("表达式意外结束");
                default:
                    throw Error($"此处需要字段、数字或括号，实际为“{token.Text}”");
            }
        }

        private bool TryReadComparisonOperator(out FeatureFilterBinaryOperator @operator)
        {
            @operator = default;
            @operator = _current.Kind switch
            {
                FeatureFilterTokenKind.Equal => FeatureFilterBinaryOperator.Equal,
                FeatureFilterTokenKind.NotEqual => FeatureFilterBinaryOperator.NotEqual,
                FeatureFilterTokenKind.Less => FeatureFilterBinaryOperator.Less,
                FeatureFilterTokenKind.LessOrEqual => FeatureFilterBinaryOperator.LessOrEqual,
                FeatureFilterTokenKind.Greater => FeatureFilterBinaryOperator.Greater,
                FeatureFilterTokenKind.GreaterOrEqual => FeatureFilterBinaryOperator.GreaterOrEqual,
                _ => default
            };

            if (!IsComparisonOperator(_current.Kind))
            {
                return false;
            }

            Advance();
            return true;
        }

        private static bool IsComparisonOperator(FeatureFilterTokenKind kind)
        {
            return kind is FeatureFilterTokenKind.Equal or
                FeatureFilterTokenKind.NotEqual or
                FeatureFilterTokenKind.Less or
                FeatureFilterTokenKind.LessOrEqual or
                FeatureFilterTokenKind.Greater or
                FeatureFilterTokenKind.GreaterOrEqual;
        }

        private bool Match(FeatureFilterTokenKind kind)
        {
            if (_current.Kind != kind)
            {
                return false;
            }

            Advance();
            return true;
        }

        private void Expect(FeatureFilterTokenKind kind, string message)
        {
            if (!Match(kind))
            {
                throw Error(message);
            }
        }

        private void Advance()
        {
            _current = _lexer.Next();
        }

        private FeatureFilterParseException Error(string message)
        {
            return new FeatureFilterParseException($"{message}（位置 {_current.Position}）");
        }
    }

    private sealed class FeatureFilterLexer
    {
        private readonly string _expression;
        private int _position;

        public FeatureFilterLexer(string expression)
        {
            _expression = expression;
        }

        public FeatureFilterToken Next()
        {
            SkipWhitespace();
            if (_position >= _expression.Length)
            {
                return new FeatureFilterToken(FeatureFilterTokenKind.End, string.Empty, 0, _position);
            }

            var start = _position;
            var current = _expression[_position];
            if (char.IsDigit(current) || (current == '.' && _position + 1 < _expression.Length && char.IsDigit(_expression[_position + 1])))
            {
                return ReadNumber(start);
            }

            if (char.IsLetter(current) || current == '_')
            {
                return ReadIdentifier(start);
            }

            _position++;
            return current switch
            {
                '(' => new FeatureFilterToken(FeatureFilterTokenKind.LeftParen, "(", 0, start),
                ')' => new FeatureFilterToken(FeatureFilterTokenKind.RightParen, ")", 0, start),
                '+' => new FeatureFilterToken(FeatureFilterTokenKind.Plus, "+", 0, start),
                '-' => new FeatureFilterToken(FeatureFilterTokenKind.Minus, "-", 0, start),
                '*' => new FeatureFilterToken(FeatureFilterTokenKind.Star, "*", 0, start),
                '/' => new FeatureFilterToken(FeatureFilterTokenKind.Slash, "/", 0, start),
                '%' => new FeatureFilterToken(FeatureFilterTokenKind.Percent, "%", 0, start),
                '&' when ConsumeIf('&') => new FeatureFilterToken(FeatureFilterTokenKind.And, "&&", 0, start),
                '|' when ConsumeIf('|') => new FeatureFilterToken(FeatureFilterTokenKind.Or, "||", 0, start),
                '=' when ConsumeIf('=') => new FeatureFilterToken(FeatureFilterTokenKind.Equal, "==", 0, start),
                '=' => new FeatureFilterToken(FeatureFilterTokenKind.Equal, "=", 0, start),
                '!' when ConsumeIf('=') => new FeatureFilterToken(FeatureFilterTokenKind.NotEqual, "!=", 0, start),
                '!' => new FeatureFilterToken(FeatureFilterTokenKind.Not, "!", 0, start),
                '<' when ConsumeIf('=') => new FeatureFilterToken(FeatureFilterTokenKind.LessOrEqual, "<=", 0, start),
                '<' when ConsumeIf('>') => new FeatureFilterToken(FeatureFilterTokenKind.NotEqual, "<>", 0, start),
                '<' => new FeatureFilterToken(FeatureFilterTokenKind.Less, "<", 0, start),
                '>' when ConsumeIf('=') => new FeatureFilterToken(FeatureFilterTokenKind.GreaterOrEqual, ">=", 0, start),
                '>' => new FeatureFilterToken(FeatureFilterTokenKind.Greater, ">", 0, start),
                '&' => throw Error("单独的“&”无效，请使用“&&”"),
                '|' => throw Error("单独的“|”无效，请使用“||”"),
                _ => throw Error($"无法识别字符“{current}”")
            };
        }

        private FeatureFilterToken ReadNumber(int start)
        {
            while (_position < _expression.Length && (char.IsDigit(_expression[_position]) || _expression[_position] == '.'))
            {
                _position++;
            }

            if (_position < _expression.Length && (_expression[_position] == 'e' || _expression[_position] == 'E'))
            {
                _position++;
                if (_position < _expression.Length && (_expression[_position] == '+' || _expression[_position] == '-'))
                {
                    _position++;
                }

                var exponentStart = _position;
                while (_position < _expression.Length && char.IsDigit(_expression[_position]))
                {
                    _position++;
                }

                if (exponentStart == _position)
                {
                    throw Error("科学计数法缺少指数数字");
                }
            }

            var text = _expression[start.._position];
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) || !double.IsFinite(number))
            {
                throw Error($"数字“{text}”无效");
            }

            return new FeatureFilterToken(FeatureFilterTokenKind.Number, text, number, start);
        }

        private FeatureFilterToken ReadIdentifier(int start)
        {
            _position++;
            while (_position < _expression.Length && (char.IsLetterOrDigit(_expression[_position]) || _expression[_position] == '_'))
            {
                _position++;
            }

            var text = _expression[start.._position];
            var kind = text.ToUpperInvariant() switch
            {
                "AND" => FeatureFilterTokenKind.And,
                "OR" => FeatureFilterTokenKind.Or,
                "NOT" => FeatureFilterTokenKind.Not,
                "TRUE" => FeatureFilterTokenKind.True,
                "FALSE" => FeatureFilterTokenKind.False,
                _ => FeatureFilterTokenKind.Identifier
            };
            return new FeatureFilterToken(kind, text, 0, start);
        }

        private bool ConsumeIf(char expected)
        {
            if (_position >= _expression.Length || _expression[_position] != expected)
            {
                return false;
            }

            _position++;
            return true;
        }

        private void SkipWhitespace()
        {
            while (_position < _expression.Length && char.IsWhiteSpace(_expression[_position]))
            {
                _position++;
            }
        }

        private FeatureFilterParseException Error(string message)
        {
            return new FeatureFilterParseException($"{message}（位置 {_position}）");
        }
    }

    private readonly record struct FeatureFilterToken(
        FeatureFilterTokenKind Kind,
        string Text,
        double Number,
        int Position);

    private enum FeatureFilterTokenKind
    {
        End,
        Number,
        Identifier,
        True,
        False,
        LeftParen,
        RightParen,
        Plus,
        Minus,
        Star,
        Slash,
        Percent,
        And,
        Or,
        Not,
        Equal,
        NotEqual,
        Less,
        LessOrEqual,
        Greater,
        GreaterOrEqual
    }

    private sealed class FeatureFilterParseException : Exception
    {
        public FeatureFilterParseException(string message) : base(message)
        {
        }
    }

    /// <summary>
    /// 应用HSV颜色范围过滤
    /// </summary>
    private Mat? ApplyColorFilter(Mat src, int hueLow, int hueHigh, int satLow, int satHigh, int valLow, int valHigh)
    {
        try
        {
            using var hsv = new Mat();
            if (src.Channels() == 3)
            {
                Cv2.CvtColor(src, hsv, ColorConversionCodes.BGR2HSV);
            }
            else if (src.Channels() == 1)
            {
                // 灰度图无法应用HSV过滤，返回空掩码
                return null;
            }
            else
            {
                return null;
            }

            // 创建HSV范围掩码
            var normalizedHueLow = NormalizeHueBound(hueLow);
            var normalizedHueHigh = NormalizeHueBound(hueHigh);
            var mask = new Mat();

            if (normalizedHueLow <= normalizedHueHigh)
            {
                var lower = new Scalar(normalizedHueLow, satLow, valLow);
                var upper = new Scalar(normalizedHueHigh, satHigh, valHigh);
                Cv2.InRange(hsv, lower, upper, mask);
            }
            else
            {
                using var lowerWrapMask = new Mat();
                using var upperWrapMask = new Mat();
                Cv2.InRange(hsv, new Scalar(0, satLow, valLow), new Scalar(normalizedHueHigh, satHigh, valHigh), lowerWrapMask);
                Cv2.InRange(hsv, new Scalar(normalizedHueLow, satLow, valLow), new Scalar(179, satHigh, valHigh), upperWrapMask);
                Cv2.BitwiseOr(lowerWrapMask, upperWrapMask, mask);
            }

            return mask;
        }
        catch
        {
            return null;
        }
    }

    private static int NormalizeHueBound(int hue)
    {
        return Math.Clamp(hue, 0, 179);
    }
}
