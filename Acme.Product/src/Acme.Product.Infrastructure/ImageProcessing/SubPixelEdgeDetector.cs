// SubPixelEdgeDetector.cs
// Lightweight subpixel edge localization based on centroid and first-order gradient moments.

using OpenCvSharp;

namespace Acme.Product.Infrastructure.ImageProcessing;

/// <summary>
/// Provides lightweight subpixel edge localization helpers for 1D line profiles and small ROIs.
/// </summary>
/// <remarks>
/// The detector exposes two estimator families:
/// intensity centroid localization and first-order gradient moment localization.
/// The gradient-moment path is not a true Zernike implementation.
/// </remarks>
public class SubPixelEdgeDetector
{
    /// <summary>
    /// Minimum gradient or intensity level that participates in the localization moment.
    /// </summary>
    public byte EdgeThreshold { get; set; } = 20;

    /// <summary>
    /// Minimum accumulated weight required for a valid localization result.
    /// </summary>
    public float MinValidSum { get; set; } = 1e-6f;

    /// <summary>
    /// Estimates the subpixel edge position from the intensity centroid of a 1D line profile.
    /// </summary>
    /// <param name="lineProfile">A 1xN or Nx1 grayscale profile in <c>CV_8UC1</c>, <c>CV_32FC1</c>, or <c>CV_64FC1</c>.</param>
    /// <param name="threshold">
    /// Intensity threshold for valid samples. If set to <c>0</c>, <see cref="EdgeThreshold"/> is used.
    /// </param>
    /// <returns>
    /// The subpixel position relative to the start of the profile, or <c>-1</c> when localization fails.
    /// </returns>
    public float DetectCentroid(Mat lineProfile, byte threshold = 0)
    {
        if (lineProfile == null || lineProfile.Empty())
            return -1;

        if (lineProfile.Rows != 1 && lineProfile.Cols != 1)
            return -1;

        byte effectiveThreshold = threshold > 0 ? threshold : EdgeThreshold;

        int length = lineProfile.Rows == 1 ? lineProfile.Cols : lineProfile.Rows;
        bool isRow = lineProfile.Rows == 1;

        float[] grayValues = new float[length];
        ExtractGrayValues(lineProfile, grayValues, isRow);

        return CalculateCentroid(grayValues, effectiveThreshold);
    }

    /// <summary>
    /// Estimates the subpixel edge position from the intensity centroid using an adaptive threshold.
    /// </summary>
    /// <param name="lineProfile">A 1xN or Nx1 grayscale profile.</param>
    /// <param name="useAdaptiveThreshold">
    /// If <c>true</c>, the threshold is derived from the profile dynamic range; otherwise <see cref="EdgeThreshold"/> is used.
    /// </param>
    /// <returns>The subpixel position, or <c>-1</c> when localization fails.</returns>
    public float DetectCentroidAdaptive(Mat lineProfile, bool useAdaptiveThreshold = true)
    {
        if (lineProfile == null || lineProfile.Empty())
            return -1;

        int length = lineProfile.Rows == 1 ? lineProfile.Cols : lineProfile.Rows;
        float[] grayValues = new float[length];
        ExtractGrayValues(lineProfile, grayValues, lineProfile.Rows == 1);

        if (!useAdaptiveThreshold)
            return CalculateCentroid(grayValues, EdgeThreshold);

        float maxGray = grayValues.Max();
        float minGray = grayValues.Min();
        byte adaptiveThreshold = (byte)((maxGray + minGray) * 0.25f);
        adaptiveThreshold = Math.Max((byte)10, Math.Min((byte)200, adaptiveThreshold));

        return CalculateCentroid(grayValues, adaptiveThreshold);
    }

    /// <summary>
    /// Estimates the subpixel edge position from the first-order moment of the gradient magnitude.
    /// </summary>
    /// <remarks>
    /// This implementation computes a normalized first-order moment of the local gradient response.
    /// It is a lightweight gradient-moment estimator and does not implement true Zernike orthogonal moments.
    /// </remarks>
    /// <param name="roi">A 1xN line profile or a small 2D ROI that crosses the edge.</param>
    /// <param name="maskSize">Optional odd ROI window size. If less than or equal to zero, the full ROI is used.</param>
    /// <returns>Subpixel position within the ROI, or <c>-1</c> when localization fails.</returns>
    public float DetectGradientMoment(Mat roi, int maskSize = 5)
    {
        if (roi == null || roi.Empty())
            return -1;

        if (roi.Rows == 1 || roi.Cols == 1)
        {
            return DetectGradientMomentOnLine(roi);
        }

        return DetectGradientMomentOnPatch(roi, maskSize);
    }

    /// <summary>
    /// Backward-compatible wrapper for legacy callers that still use the historic method name.
    /// </summary>
    /// <remarks>
    /// Retained only for compatibility. The implementation uses first-order gradient moments and not true Zernike moments.
    /// Call <see cref="DetectGradientMoment(Mat, int)"/> in new code.
    /// </remarks>
    [Obsolete("DetectZernike uses first-order gradient moments rather than true Zernike moments. Use DetectGradientMoment instead.")]
    public float DetectZernike(Mat roi, int maskSize = 5)
    {
        return DetectGradientMoment(roi, maskSize);
    }

    /// <summary>
    /// Extracts a grayscale line profile from an image and localizes the edge with centroid weighting.
    /// </summary>
    /// <param name="image">Input image.</param>
    /// <param name="start">Line start point.</param>
    /// <param name="end">Line end point.</param>
    /// <returns>Subpixel position relative to <paramref name="start"/>, or <c>-1</c> when localization fails.</returns>
    public float DetectEdgeInImage(Mat image, Point start, Point end)
    {
        if (image == null || image.Empty())
            return -1;

        using var gray = new Mat();
        if (image.Channels() > 1)
            Cv2.CvtColor(image, gray, ColorConversionCodes.BGR2GRAY);
        else
            image.CopyTo(gray);

        using var lineProfile = ExtractLineProfile(gray, start, end);
        if (lineProfile == null || lineProfile.Empty())
            return -1;

        return DetectCentroid(lineProfile);
    }

    /// <summary>
    /// Copies profile samples into a contiguous float buffer for moment calculations.
    /// </summary>
    private void ExtractGrayValues(Mat lineProfile, float[] grayValues, bool isRow)
    {
        int length = grayValues.Length;

        unsafe
        {
            if (lineProfile.Type() == MatType.CV_8UC1)
            {
                byte* ptr = (byte*)lineProfile.DataPointer;
                int step = (int)lineProfile.Step();

                for (int i = 0; i < length; i++)
                {
                    grayValues[i] = isRow ? ptr[i] : ptr[i * step];
                }
            }
            else if (lineProfile.Type() == MatType.CV_32FC1)
            {
                float* ptr = (float*)lineProfile.DataPointer;
                int step = (int)lineProfile.Step() / sizeof(float);

                for (int i = 0; i < length; i++)
                {
                    grayValues[i] = isRow ? ptr[i] : ptr[i * step];
                }
            }
            else if (lineProfile.Type() == MatType.CV_64FC1)
            {
                double* ptr = (double*)lineProfile.DataPointer;
                int step = (int)lineProfile.Step() / sizeof(double);

                for (int i = 0; i < length; i++)
                {
                    grayValues[i] = (float)(isRow ? ptr[i] : ptr[i * step]);
                }
            }
            else
            {
                Array.Clear(grayValues, 0, length);
            }
        }
    }

    /// <summary>
    /// Calculates the intensity centroid from thresholded profile samples.
    /// </summary>
    private float CalculateCentroid(float[] grayValues, byte threshold)
    {
        if (grayValues == null || grayValues.Length == 0)
            return -1;

        int length = grayValues.Length;
        double weightedSum = 0;
        double graySum = 0;

        for (int i = 0; i < length; i++)
        {
            float gray = grayValues[i];
            if (gray >= threshold)
            {
                weightedSum += i * gray;
                graySum += gray;
            }
        }

        if (graySum < MinValidSum)
            return -1;

        return (float)(weightedSum / graySum);
    }

    private float DetectGradientMomentOnLine(Mat lineProfile)
    {
        int length = lineProfile.Rows == 1 ? lineProfile.Cols : lineProfile.Rows;
        if (length < 2)
            return -1;

        float[] values = new float[length];
        ExtractGrayValues(lineProfile, values, lineProfile.Rows == 1);

        int gradientLength = length - 1;
        double center = (gradientLength - 1) / 2.0;
        double radius = Math.Max(center, 1.0);
        double gradientSum = 0.0;
        double firstOrderMoment = 0.0;
        double threshold = EdgeThreshold;

        for (int i = 0; i < gradientLength; i++)
        {
            double gradientMagnitude = Math.Abs(values[i + 1] - values[i]);
            if (gradientMagnitude < threshold)
            {
                continue;
            }

            gradientSum += gradientMagnitude;
            firstOrderMoment += gradientMagnitude * ((i - center) / radius);
        }

        if (gradientSum < MinValidSum)
            return -1;

        double normalizedOffset = (firstOrderMoment / gradientSum) * 2.0;
        double offset = normalizedOffset * (radius / 2.0);
        double position = center + offset + 0.5;

        return (float)Math.Clamp(position, 0.0, length - 1.0);
    }

    private float DetectGradientMomentOnPatch(Mat roi, int maskSize)
    {
        int minDim = Math.Min(roi.Rows, roi.Cols);
        if (minDim < 3)
            return -1;

        int size = maskSize <= 0 ? minDim : Math.Min(maskSize, minDim);
        if (size % 2 == 0)
            size -= 1;
        if (size < 3)
            size = Math.Min(minDim, 3);

        int offsetX = (roi.Cols - size) / 2;
        int offsetY = (roi.Rows - size) / 2;
        offsetX = Math.Clamp(offsetX, 0, Math.Max(roi.Cols - size, 0));
        offsetY = Math.Clamp(offsetY, 0, Math.Max(roi.Rows - size, 0));

        using var patch = new Mat(roi, new Rect(offsetX, offsetY, size, size));
        using var patchFloat = new Mat();
        patch.ConvertTo(patchFloat, MatType.CV_32FC1);

        using var gradX = new Mat();
        Cv2.Sobel(patchFloat, gradX, MatType.CV_32FC1, 1, 0, 3);

        int center = size / 2;
        double radius = Math.Max(center, 1.0);
        double gradientSum = 0.0;
        double firstOrderMoment = 0.0;
        double threshold = EdgeThreshold;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                double gradientMagnitude = Math.Abs(gradX.At<float>(y, x));
                if (gradientMagnitude < threshold)
                {
                    continue;
                }

                gradientSum += gradientMagnitude;
                firstOrderMoment += gradientMagnitude * ((x - center) / radius);
            }
        }

        if (gradientSum < MinValidSum)
            return -1;

        double normalizedOffset = (firstOrderMoment / gradientSum) * 2.0;
        double offset = normalizedOffset * (radius / 2.0);
        double position = offsetX + center + offset;

        return (float)Math.Clamp(position, 0.0, roi.Cols - 1.0);
    }

    /// <summary>
    /// Extracts a grayscale line profile from an image between two points.
    /// </summary>
    private Mat ExtractLineProfile(Mat image, Point start, Point end)
    {
        double dx = end.X - start.X;
        double dy = end.Y - start.Y;
        int length = (int)Math.Ceiling(Math.Sqrt(dx * dx + dy * dy));

        if (length < 2)
            return new Mat();

        Mat lineProfile = new Mat(1, length, MatType.CV_8UC1);

        unsafe
        {
            byte* imgPtr = (byte*)image.DataPointer;
            byte* linePtr = (byte*)lineProfile.DataPointer;
            int step = (int)image.Step();
            int width = image.Cols;
            int height = image.Rows;

            for (int i = 0; i < length; i++)
            {
                double t = (double)i / (length - 1);
                int x = (int)(start.X + dx * t);
                int y = (int)(start.Y + dy * t);

                x = Math.Max(0, Math.Min(width - 1, x));
                y = Math.Max(0, Math.Min(height - 1, y));

                linePtr[i] = imgPtr[y * step + x];
            }
        }

        return lineProfile;
    }
}
