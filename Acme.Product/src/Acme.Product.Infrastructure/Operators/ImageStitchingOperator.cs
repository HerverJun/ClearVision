// ImageStitchingOperator.cs
// 图像拼接算子
// 使用特征匹配将多图配准并拼接成全景
// 作者：蘅芜君
using Acme.Product.Core.Attributes;
using Acme.Product.Core.Entities;
using Acme.Product.Core.Enums;
using Acme.Product.Core.Operators;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
namespace Acme.Product.Infrastructure.Operators;

[OperatorMeta(
    DisplayName = "图像拼接",
    Description = "Stitches two images into a larger panorama-like output.",
    Category = "图像处理",
    IconName = "stitch",
    Keywords = new[] { "stitch", "panorama", "merge image" }
)]
[InputPort("Image1", "Image 1", PortDataType.Image, IsRequired = true)]
[InputPort("Image2", "Image 2", PortDataType.Image, IsRequired = true)]
[OutputPort("Image", "Image", PortDataType.Image)]
[OutputPort("OverlapRatio", "Overlap Ratio", PortDataType.Float)]
[OperatorParam("Method", "Method", "enum", DefaultValue = "FeatureBased", Options = new[] { "FeatureBased|FeatureBased", "Manual|Manual" })]
[OperatorParam("OverlapPercent", "Overlap Percent", "double", DefaultValue = 20.0, Min = 0.0, Max = 90.0)]
[OperatorParam("BlendMode", "Blend Mode", "enum", DefaultValue = "Linear", Options = new[] { "Linear|Linear", "MultiBand|MultiBand" })]
public class ImageStitchingOperator : OperatorBase
{
    private const string LinearBlendImplementation = "FeatherDistanceBlend";
    private const string MultiBandBlendImplementation = "LaplacianPyramidMultiBand";

    public override OperatorType OperatorType => OperatorType.ImageStitching;

    public ImageStitchingOperator(ILogger<ImageStitchingOperator> logger) : base(logger)
    {
    }

    protected override Task<OperatorExecutionOutput> ExecuteCoreAsync(
        Operator @operator,
        Dictionary<string, object>? inputs,
        CancellationToken cancellationToken)
    {
        if (!TryGetInputImage(inputs, "Image1", out var image1) || image1 == null ||
            !TryGetInputImage(inputs, "Image2", out var image2) || image2 == null)
        {
            return Task.FromResult(OperatorExecutionOutput.Failure("Image1 and Image2 are required"));
        }

        var src1 = image1.GetMat();
        var src2 = image2.GetMat();
        if (src1.Empty() || src2.Empty())
        {
            return Task.FromResult(OperatorExecutionOutput.Failure("Input images are invalid"));
        }

        var method = GetStringParam(@operator, "Method", "FeatureBased");
        var overlapPercent = GetDoubleParam(@operator, "OverlapPercent", 20.0, 0.0, 90.0);
        var blendMode = GetStringParam(@operator, "BlendMode", "Linear");

        Mat stitched;
        double overlapRatio;

        if (method.Equals("FeatureBased", StringComparison.OrdinalIgnoreCase) &&
            TryFeatureBasedStitch(src1, src2, blendMode, out stitched, out overlapRatio))
        {
            // stitched by feature matches
        }
        else
        {
            stitched = ManualHorizontalStitch(src1, src2, overlapPercent, blendMode, out overlapRatio);
        }

        var output = new Dictionary<string, object>
        {
            { "OverlapRatio", overlapRatio },
            { "BlendModeApplied", blendMode },
            { "BlendImplementation", ResolveBlendImplementation(blendMode) },
            { "MethodApplied", method }
        };

        return Task.FromResult(OperatorExecutionOutput.Success(CreateImageOutput(stitched, output)));
    }

    public override ValidationResult ValidateParameters(Operator @operator)
    {
        var method = GetStringParam(@operator, "Method", "FeatureBased");
        var validMethods = new[] { "FeatureBased", "Manual" };
        if (!validMethods.Contains(method, StringComparer.OrdinalIgnoreCase))
        {
            return ValidationResult.Invalid("Method must be FeatureBased or Manual");
        }

        var overlap = GetDoubleParam(@operator, "OverlapPercent", 20.0);
        if (overlap < 0 || overlap >= 100)
        {
            return ValidationResult.Invalid("OverlapPercent must be in [0, 100)");
        }

        return ValidationResult.Valid();
    }

    private static bool TryFeatureBasedStitch(Mat src1, Mat src2, string blendMode, out Mat stitched, out double overlapRatio)
    {
        stitched = new Mat();
        overlapRatio = 0.0;

        using var gray1 = new Mat();
        using var gray2 = new Mat();
        if (src1.Channels() == 1)
        {
            src1.CopyTo(gray1);
        }
        else
        {
            Cv2.CvtColor(src1, gray1, ColorConversionCodes.BGR2GRAY);
        }

        if (src2.Channels() == 1)
        {
            src2.CopyTo(gray2);
        }
        else
        {
            Cv2.CvtColor(src2, gray2, ColorConversionCodes.BGR2GRAY);
        }

        using var orb = ORB.Create(1200);
        using var desc1 = new Mat();
        using var desc2 = new Mat();
        orb.DetectAndCompute(gray1, null, out var kp1, desc1);
        orb.DetectAndCompute(gray2, null, out var kp2, desc2);

        if (desc1.Empty() || desc2.Empty() || kp1.Length < 8 || kp2.Length < 8)
        {
            return false;
        }

        using var matcher = new BFMatcher(NormTypes.Hamming, false);
        var knn = matcher.KnnMatch(desc2, desc1, 2);

        var good = new List<DMatch>();
        foreach (var pair in knn)
        {
            if (pair.Length < 2)
            {
                continue;
            }

            if (pair[0].Distance < pair[1].Distance * 0.75f)
            {
                good.Add(pair[0]);
            }
        }

        if (good.Count < 8)
        {
            return false;
        }

        var srcPts = good.Select(m => kp2[m.QueryIdx].Pt).ToArray();
        var dstPts = good.Select(m => kp1[m.TrainIdx].Pt).ToArray();

        using var mask = new Mat();
        using var homography = Cv2.FindHomography(InputArray.Create(srcPts), InputArray.Create(dstPts), HomographyMethods.Ransac, 3, mask);
        if (homography.Empty())
        {
            return false;
        }

        var src1Corners = new[]
        {
            new Point2f(0, 0),
            new Point2f(src1.Width, 0),
            new Point2f(src1.Width, src1.Height),
            new Point2f(0, src1.Height)
        };
        var src2Corners = new[]
        {
            new Point2f(0, 0),
            new Point2f(src2.Width, 0),
            new Point2f(src2.Width, src2.Height),
            new Point2f(0, src2.Height)
        };
        var warpedCorners = Cv2.PerspectiveTransform(src2Corners, homography);
        var allCorners = src1Corners.Concat(warpedCorners).ToArray();
        var minX = Math.Floor(allCorners.Min(point => point.X));
        var minY = Math.Floor(allCorners.Min(point => point.Y));
        var maxX = Math.Ceiling(allCorners.Max(point => point.X));
        var maxY = Math.Ceiling(allCorners.Max(point => point.Y));
        var width = Math.Max(1, (int)(maxX - minX));
        var height = Math.Max(1, (int)(maxY - minY));

        using var translation = Mat.Eye(3, 3, MatType.CV_64FC1).ToMat();
        translation.Set(0, 2, -minX);
        translation.Set(1, 2, -minY);
        using var translatedHomography = new Mat();
        using var noMatrix = new Mat();
        Cv2.Gemm(translation, homography, 1.0, noMatrix, 0.0, translatedHomography);

        using var warped1 = new Mat();
        using var warped2 = new Mat();
        Cv2.WarpPerspective(src1, warped1, translation, new Size(width, height));
        Cv2.WarpPerspective(src2, warped2, translatedHomography, new Size(width, height));

        using var mask1 = CreateNonZeroMask(warped1);
        using var mask2 = CreateNonZeroMask(warped2);
        stitched = BlendWarpedImages(warped1, warped2, mask1, mask2, blendMode);

        using var overlapMask = new Mat();
        Cv2.BitwiseAnd(mask1, mask2, overlapMask);
        var overlapPixels = Cv2.CountNonZero(overlapMask);
        using var unionMask = new Mat();
        Cv2.BitwiseOr(mask1, mask2, unionMask);
        var unionPixels = Cv2.CountNonZero(unionMask);
        overlapRatio = overlapPixels / (double)Math.Max(1, unionPixels);
        overlapRatio = Math.Clamp(overlapRatio, 0.0, 1.0);
        return true;
    }

    private static Mat ManualHorizontalStitch(Mat src1, Mat src2, double overlapPercent, string blendMode, out double overlapRatio)
    {
        var overlap = (int)Math.Round(Math.Min(src1.Width, src2.Width) * overlapPercent / 100.0);
        overlap = Math.Clamp(overlap, 0, Math.Min(src1.Width, src2.Width) - 1);

        var width = src1.Width + src2.Width - overlap;
        var height = Math.Max(src1.Height, src2.Height);
        var stitched = new Mat(height, width, src1.Type(), Scalar.Black);

        using (var roi1 = new Mat(stitched, new Rect(0, 0, src1.Width, src1.Height)))
        {
            src1.CopyTo(roi1);
        }

        var startX = src1.Width - overlap;
        using (var roi2 = new Mat(stitched, new Rect(startX, 0, src2.Width, src2.Height)))
        {
            src2.CopyTo(roi2);
        }

        if (overlap > 0)
        {
            using var warped1 = new Mat(height, width, src1.Type(), Scalar.Black);
            using var warped2 = new Mat(height, width, src1.Type(), Scalar.Black);
            using var mask1 = new Mat(height, width, MatType.CV_8UC1, Scalar.Black);
            using var mask2 = new Mat(height, width, MatType.CV_8UC1, Scalar.Black);

            using (var roi1 = new Mat(warped1, new Rect(0, 0, src1.Width, src1.Height)))
            using (var maskRoi1 = new Mat(mask1, new Rect(0, 0, src1.Width, src1.Height)))
            {
                src1.CopyTo(roi1);
                maskRoi1.SetTo(Scalar.White);
            }

            using (var roi2 = new Mat(warped2, new Rect(startX, 0, src2.Width, src2.Height)))
            using (var maskRoi2 = new Mat(mask2, new Rect(startX, 0, src2.Width, src2.Height)))
            {
                src2.CopyTo(roi2);
                maskRoi2.SetTo(Scalar.White);
            }

            stitched.Dispose();
            stitched = BlendWarpedImages(warped1, warped2, mask1, mask2, blendMode);
        }

        overlapRatio = overlap / (double)Math.Max(1, Math.Min(src1.Width, src2.Width));
        return stitched;
    }

    private static Mat CreateNonZeroMask(Mat image)
    {
        using var gray = new Mat();
        if (image.Channels() == 1)
        {
            image.CopyTo(gray);
        }
        else
        {
            Cv2.CvtColor(image, gray, ColorConversionCodes.BGR2GRAY);
        }

        var mask = new Mat();
        Cv2.Threshold(gray, mask, 1, 255, ThresholdTypes.Binary);
        return mask;
    }

    private static Mat BlendWarpedImages(Mat first, Mat second, Mat firstMask, Mat secondMask, string blendMode)
    {
        if (string.Equals(blendMode, "MultiBand", StringComparison.OrdinalIgnoreCase))
        {
            return MultiBandBlend(first, second, firstMask, secondMask);
        }

        return FeatherBlend(first, second, firstMask, secondMask);
    }

    private static string ResolveBlendImplementation(string blendMode)
    {
        return string.Equals(blendMode, "MultiBand", StringComparison.OrdinalIgnoreCase)
            ? MultiBandBlendImplementation
            : LinearBlendImplementation;
    }

    private static Mat FeatherBlend(Mat first, Mat second, Mat firstMask, Mat secondMask)
    {
        using var firstDistance = new Mat();
        using var secondDistance = new Mat();
        Cv2.DistanceTransform(firstMask, firstDistance, DistanceTypes.L2, DistanceTransformMasks.Mask3);
        Cv2.DistanceTransform(secondMask, secondDistance, DistanceTypes.L2, DistanceTransformMasks.Mask3);

        using var firstFloat = new Mat();
        using var secondFloat = new Mat();
        first.ConvertTo(firstFloat, MatType.MakeType(MatType.CV_32F, first.Channels()));
        second.ConvertTo(secondFloat, MatType.MakeType(MatType.CV_32F, second.Channels()));

        using var blendedFloat = new Mat(first.Size(), firstFloat.Type(), Scalar.All(0));
        var result = new Mat(first.Size(), first.Type(), Scalar.Black);

        for (var y = 0; y < first.Rows; y++)
        {
            for (var x = 0; x < first.Cols; x++)
            {
                var inFirst = firstMask.At<byte>(y, x) > 0;
                var inSecond = secondMask.At<byte>(y, x) > 0;
                if (!inFirst && !inSecond)
                {
                    continue;
                }

                if (first.Channels() == 1)
                {
                    var value = ResolveBlendValue(
                        inFirst ? firstFloat.At<float>(y, x) : 0f,
                        inSecond ? secondFloat.At<float>(y, x) : 0f,
                        inFirst ? firstDistance.At<float>(y, x) : 0f,
                        inSecond ? secondDistance.At<float>(y, x) : 0f,
                        inFirst,
                        inSecond);
                    blendedFloat.Set(y, x, value);
                    continue;
                }

                var firstPixel = inFirst ? firstFloat.At<Vec3f>(y, x) : new Vec3f();
                var secondPixel = inSecond ? secondFloat.At<Vec3f>(y, x) : new Vec3f();
                var firstWeight = inFirst ? firstDistance.At<float>(y, x) : 0f;
                var secondWeight = inSecond ? secondDistance.At<float>(y, x) : 0f;
                blendedFloat.Set(y, x, ResolveBlendValue(firstPixel, secondPixel, firstWeight, secondWeight, inFirst, inSecond));
            }
        }

        blendedFloat.ConvertTo(result, first.Type());
        return result;
    }

    private static Mat MultiBandBlend(Mat first, Mat second, Mat firstMask, Mat secondMask)
    {
        using var firstFloat = new Mat();
        using var secondFloat = new Mat();
        first.ConvertTo(firstFloat, MatType.MakeType(MatType.CV_32F, first.Channels()));
        second.ConvertTo(secondFloat, MatType.MakeType(MatType.CV_32F, second.Channels()));

        using var firstWeight = CreateFeatherWeight(firstMask, secondMask);
        var levels = DeterminePyramidLevels(first.Size());
        using var blendedFloat = BlendPyramid(firstFloat, secondFloat, firstWeight, levels);

        var result = new Mat(first.Size(), first.Type(), Scalar.Black);
        blendedFloat.ConvertTo(result, first.Type());
        return result;
    }

    private static Mat CreateFeatherWeight(Mat firstMask, Mat secondMask)
    {
        using var firstDistance = new Mat();
        using var secondDistance = new Mat();
        Cv2.DistanceTransform(firstMask, firstDistance, DistanceTypes.L2, DistanceTransformMasks.Mask3);
        Cv2.DistanceTransform(secondMask, secondDistance, DistanceTypes.L2, DistanceTransformMasks.Mask3);

        var weight = new Mat(firstMask.Size(), MatType.CV_32FC1, Scalar.All(0));
        for (var y = 0; y < firstMask.Rows; y++)
        {
            for (var x = 0; x < firstMask.Cols; x++)
            {
                var inFirst = firstMask.At<byte>(y, x) > 0;
                var inSecond = secondMask.At<byte>(y, x) > 0;
                if (inFirst && !inSecond)
                {
                    weight.Set(y, x, 1f);
                    continue;
                }

                if (!inFirst || !inSecond)
                {
                    continue;
                }

                var firstWeight = firstDistance.At<float>(y, x);
                var secondWeight = secondDistance.At<float>(y, x);
                var sum = Math.Max(1e-6f, firstWeight + secondWeight);
                weight.Set(y, x, firstWeight / sum);
            }
        }

        return weight;
    }

    private static int DeterminePyramidLevels(Size size)
    {
        var minDimension = Math.Min(size.Width, size.Height);
        var levels = 1;
        while (levels < 5 && minDimension >= 32)
        {
            levels++;
            minDimension /= 2;
        }

        return levels;
    }

    private static Mat BlendPyramid(Mat first, Mat second, Mat weight, int levels)
    {
        var firstGaussian = BuildGaussianPyramid(first, levels);
        var secondGaussian = BuildGaussianPyramid(second, levels);
        var weightGaussian = BuildGaussianPyramid(weight, levels);
        var firstLaplacian = BuildLaplacianPyramid(firstGaussian);
        var secondLaplacian = BuildLaplacianPyramid(secondGaussian);
        var blendedLevels = new List<Mat>(levels);

        try
        {
            for (var i = 0; i < levels; i++)
            {
                using var weightForChannels = ExpandWeightChannels(weightGaussian[i], first.Channels());
                using var inverseWeight = new Mat();
                Cv2.Subtract(Scalar.All(1.0), weightForChannels, inverseWeight);

                using var weightedFirst = new Mat();
                using var weightedSecond = new Mat();
                Cv2.Multiply(firstLaplacian[i], weightForChannels, weightedFirst);
                Cv2.Multiply(secondLaplacian[i], inverseWeight, weightedSecond);

                var blended = new Mat();
                Cv2.Add(weightedFirst, weightedSecond, blended);
                blendedLevels.Add(blended);
            }

            var current = blendedLevels[^1].Clone();
            for (var i = levels - 2; i >= 0; i--)
            {
                using var up = new Mat();
                Cv2.PyrUp(current, up, blendedLevels[i].Size());
                current.Dispose();
                current = new Mat();
                Cv2.Add(up, blendedLevels[i], current);
            }

            return current;
        }
        finally
        {
            foreach (var mat in firstGaussian.Concat(secondGaussian).Concat(weightGaussian).Concat(firstLaplacian).Concat(secondLaplacian).Concat(blendedLevels))
            {
                mat.Dispose();
            }
        }
    }

    private static List<Mat> BuildGaussianPyramid(Mat source, int levels)
    {
        var pyramid = new List<Mat>(levels) { source.Clone() };
        for (var i = 1; i < levels; i++)
        {
            var down = new Mat();
            Cv2.PyrDown(pyramid[i - 1], down);
            pyramid.Add(down);
        }

        return pyramid;
    }

    private static List<Mat> BuildLaplacianPyramid(IReadOnlyList<Mat> gaussian)
    {
        var laplacian = new List<Mat>(gaussian.Count);
        for (var i = 0; i < gaussian.Count - 1; i++)
        {
            using var up = new Mat();
            Cv2.PyrUp(gaussian[i + 1], up, gaussian[i].Size());
            var level = new Mat();
            Cv2.Subtract(gaussian[i], up, level);
            laplacian.Add(level);
        }

        laplacian.Add(gaussian[^1].Clone());
        return laplacian;
    }

    private static Mat ExpandWeightChannels(Mat weight, int channels)
    {
        if (channels == 1)
        {
            return weight.Clone();
        }

        var weights = Enumerable.Repeat(weight, channels).ToArray();
        var expanded = new Mat();
        Cv2.Merge(weights, expanded);
        return expanded;
    }

    private static float ResolveBlendValue(float firstValue, float secondValue, float firstWeight, float secondWeight, bool inFirst, bool inSecond)
    {
        if (inFirst && !inSecond)
        {
            return firstValue;
        }

        if (!inFirst && inSecond)
        {
            return secondValue;
        }

        var weightSum = Math.Max(1e-6f, firstWeight + secondWeight);
        return (firstValue * (firstWeight / weightSum)) + (secondValue * (secondWeight / weightSum));
    }

    private static Vec3f ResolveBlendValue(Vec3f firstValue, Vec3f secondValue, float firstWeight, float secondWeight, bool inFirst, bool inSecond)
    {
        if (inFirst && !inSecond)
        {
            return firstValue;
        }

        if (!inFirst && inSecond)
        {
            return secondValue;
        }

        var weightSum = Math.Max(1e-6f, firstWeight + secondWeight);
        var firstRatio = firstWeight / weightSum;
        var secondRatio = secondWeight / weightSum;
        return new Vec3f(
            (firstValue.Item0 * firstRatio) + (secondValue.Item0 * secondRatio),
            (firstValue.Item1 * firstRatio) + (secondValue.Item1 * secondRatio),
            (firstValue.Item2 * firstRatio) + (secondValue.Item2 * secondRatio));
    }
}
