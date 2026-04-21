using System.Reflection;
using Acme.Product.Infrastructure.ImageProcessing;
using FluentAssertions;
using OpenCvSharp;

namespace Acme.Product.Infrastructure.ImageProcessing.Tests;

public class StegerSubpixelEdgeDetectorTests
{
    [Fact]
    public void ComputeSubpixelPoint_WithAxisAlignedHessian_ShouldNotDropPointWhenGxyIsZero()
    {
        using var detector = new StegerSubpixelEdgeDetector
        {
            EdgeThreshold = 0.1,
            MaxOffset = 1.0
        };

        var point = InvokeComputeSubpixelPoint(
            detector,
            x: 40,
            y: 20,
            gx: 40.0,
            gy: 0.0,
            gxx: -80.0,
            gyy: -5.0,
            gxy: 0.0);

        point.Should().NotBeNull();
        point!.X.Should().BeApproximately(40.5, 1e-6);
        point.Y.Should().BeApproximately(20.0, 1e-6);
        Math.Abs(point.NormalX).Should().BeApproximately(1.0, 1e-6);
        Math.Abs(point.NormalY).Should().BeLessThan(1e-6);
    }

    [Fact]
    public void ComputeDerivatives_WithLargerSigma_ShouldSmoothGradientResponse()
    {
        using var image = CreateVerticalStepEdgeImage();
        using var smallSigmaDetector = new StegerSubpixelEdgeDetector { Sigma = 0.6 };
        using var largeSigmaDetector = new StegerSubpixelEdgeDetector { Sigma = 2.0 };
        using var smallDx = new Mat();
        using var smallDy = new Mat();
        using var smallDxx = new Mat();
        using var smallDyy = new Mat();
        using var smallDxy = new Mat();
        using var largeDx = new Mat();
        using var largeDy = new Mat();
        using var largeDxx = new Mat();
        using var largeDyy = new Mat();
        using var largeDxy = new Mat();

        InvokeComputeDerivatives(smallSigmaDetector, image, smallDx, smallDy, smallDxx, smallDyy, smallDxy);
        InvokeComputeDerivatives(largeSigmaDetector, image, largeDx, largeDy, largeDxx, largeDyy, largeDxy);

        var row = image.Rows / 2;
        var smallPeak = GetPeakAbsoluteValue(smallDx, row);
        var largePeak = GetPeakAbsoluteValue(largeDx, row);

        largePeak.Should().BeLessThan(smallPeak);
    }

    [Fact]
    public void ComputeSubpixelPoint_WithNearZeroPrincipalCurvature_ShouldReturnNull()
    {
        using var detector = new StegerSubpixelEdgeDetector
        {
            EdgeThreshold = 0.1,
            MaxOffset = 1.0
        };

        var point = InvokeComputeSubpixelPoint(
            detector,
            x: 12,
            y: 8,
            gx: 2.0,
            gy: 0.0,
            gxx: 1e-12,
            gyy: -1e-12,
            gxy: 0.0);

        point.Should().BeNull();
    }

    [Fact]
    public void ComputeSubpixelPoint_WithTinyGxyFallback_ShouldProduceFinitePoint()
    {
        using var detector = new StegerSubpixelEdgeDetector
        {
            EdgeThreshold = 0.1,
            MaxOffset = 2.0
        };

        var point = InvokeComputeSubpixelPoint(
            detector,
            x: 25,
            y: 30,
            gx: 0.0,
            gy: 10.0,
            gxx: -2.0,
            gyy: -10.0,
            gxy: 1e-12);

        point.Should().NotBeNull();
        (!double.IsNaN(point!.X) && !double.IsInfinity(point.X)).Should().BeTrue();
        point.Y.Should().BeApproximately(31.0, 1e-6);
        point.NormalX.Should().BeApproximately(0.0, 1e-6);
        Math.Abs(point.NormalY).Should().BeApproximately(1.0, 1e-6);
        point.Strength.Should().BeApproximately(10.0, 1e-6);
    }

    [Fact]
    public void DetectEdges_OnUniformImage_ShouldSkipDerivativeWorkAndReportEmptyDiagnostics()
    {
        using var image = new Mat(96, 128, MatType.CV_8UC1, new Scalar(120));
        using var detector = new StegerSubpixelEdgeDetector();

        var points = detector.DetectEdges(image);

        points.Should().BeEmpty();
        detector.LastDiagnostics.CandidateEdgePixels.Should().Be(0);
        detector.LastDiagnostics.AcceptedEdgePoints.Should().Be(0);
        detector.LastDiagnostics.UsedFullImageDerivatives.Should().BeFalse();
        detector.LastDiagnostics.DerivativePlaneCount.Should().Be(0);
        detector.LastDiagnostics.DerivativeRegionPixels.Should().Be(0);
        detector.LastDiagnostics.ApproxDerivativeBufferBytes.Should().Be(0);
        detector.LastDiagnostics.ShouldConsiderRoiCropping.Should().BeFalse();
    }

    [Fact]
    public void DetectEdges_OnNoisyStepEdge_ShouldReturnFinitePointsNearEdge()
    {
        const int width = 160;
        const int height = 120;
        const int edgeX = 80;

        using var image = CreateLowContrastNoisyStepEdgeImage(width, height, edgeX, leftValue: 24, rightValue: 232, noiseSigma: 5.0, seed: 1337);
        using var detector = new StegerSubpixelEdgeDetector
        {
            Sigma = 1.0,
            EdgeThreshold = 0.5,
            MaxOffset = 3.0
        };

        var points = detector.DetectEdges(image, cannyLow: 16, cannyHigh: 48);
        var nearEdgePoints = points.Where(point => Math.Abs(point.X - edgeX) <= 2.0).ToList();

        points.Should().NotBeEmpty();
        nearEdgePoints.Count.Should().BeGreaterThan(height / 3);
        nearEdgePoints.Average(point => point.X).Should().BeApproximately(edgeX, 1.0);
        points.Should().OnlyContain(point =>
            !double.IsNaN(point.X) &&
            !double.IsInfinity(point.X) &&
            !double.IsNaN(point.Y) &&
            !double.IsInfinity(point.Y) &&
            !double.IsNaN(point.NormalX) &&
            !double.IsInfinity(point.NormalX) &&
            !double.IsNaN(point.NormalY) &&
            !double.IsInfinity(point.NormalY) &&
            !double.IsNaN(point.Strength) &&
            !double.IsInfinity(point.Strength));
        points.Should().OnlyContain(point =>
            Math.Abs(Math.Sqrt((point.NormalX * point.NormalX) + (point.NormalY * point.NormalY)) - 1.0) < 1e-6);
        detector.LastDiagnostics.CandidateEdgePixels.Should().BeGreaterThan(0);
        detector.LastDiagnostics.AcceptedEdgePoints.Should().Be(points.Count);
        detector.LastDiagnostics.UsedFullImageDerivatives.Should().BeTrue();
    }

    private static SubpixelEdgePoint? InvokeComputeSubpixelPoint(
        StegerSubpixelEdgeDetector detector,
        int x,
        int y,
        double gx,
        double gy,
        double gxx,
        double gyy,
        double gxy)
    {
        var method = typeof(StegerSubpixelEdgeDetector).GetMethod(
            "ComputeSubpixelPoint",
            BindingFlags.Instance | BindingFlags.NonPublic);

        method.Should().NotBeNull();

        return (SubpixelEdgePoint?)method!.Invoke(detector, new object[] { x, y, gx, gy, gxx, gyy, gxy });
    }

    private static void InvokeComputeDerivatives(
        StegerSubpixelEdgeDetector detector,
        Mat gray,
        Mat dx,
        Mat dy,
        Mat dxx,
        Mat dyy,
        Mat dxy)
    {
        var method = typeof(StegerSubpixelEdgeDetector).GetMethod(
            "ComputeDerivatives",
            BindingFlags.Instance | BindingFlags.NonPublic);

        method.Should().NotBeNull();
        method!.Invoke(detector, new object[] { gray, dx, dy, dxx, dyy, dxy });
    }

    private static double GetPeakAbsoluteValue(Mat image, int row)
    {
        double peak = 0;
        for (var x = 0; x < image.Cols; x++)
        {
            peak = Math.Max(peak, Math.Abs(image.At<double>(row, x)));
        }

        return peak;
    }

    private static Mat CreateVerticalStepEdgeImage(int width = 96, int height = 96)
    {
        var image = new Mat(height, width, MatType.CV_8UC1, Scalar.Black);
        var edgeX = width / 2;
        Cv2.Rectangle(image, new Rect(edgeX, 0, width - edgeX, height), Scalar.White, -1);
        return image;
    }

    private static Mat CreateLowContrastNoisyStepEdgeImage(
        int width,
        int height,
        int edgeX,
        double leftValue,
        double rightValue,
        double noiseSigma,
        int seed)
    {
        var image = new Mat(height, width, MatType.CV_8UC1);
        var random = new Random(seed);

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var baseline = x < edgeX ? leftValue : rightValue;
                var noisyValue = baseline + (NextGaussian(random) * noiseSigma);
                image.Set(y, x, (byte)Math.Clamp((int)Math.Round(noisyValue), 0, 255));
            }
        }

        return image;
    }

    private static double NextGaussian(Random random)
    {
        var u1 = 1.0 - random.NextDouble();
        var u2 = 1.0 - random.NextDouble();
        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
    }
}
