using ClearVision.Product.Infrastructure.Operators;
using FluentAssertions;
using OpenCvSharp;

namespace ClearVision.Product.Tests.Operators;

[TestClassification(TestDomain.Measurement, TestPurpose.Regression, TestLane.Pr, TestEvidenceType.Contract, TestOracleType.Contract, TestResourceRequirement.None, TestExpectedDuration.Fast, TestFlakyPolicy.Blocking, "operator-quality")]
public class CircleCaliperFitV2KernelTests
{
    [Theory]
    [InlineData(0, 230, CircleCaliperFitV2EdgePolarity.LightToDark)]
    [InlineData(230, 20, CircleCaliperFitV2EdgePolarity.DarkToLight)]
    public void Fit_WithExplicitPolarity_ShouldMeasureCircle(byte background, byte foreground, CircleCaliperFitV2EdgePolarity polarity)
    {
        using var gray = CreateFilledCircleImage(320, 240, 160.25, 120.75, 55.4, background, foreground);
        var result = CircleCaliperFitV2Kernel.Fit(gray, Request(160.25, 120.75, 55.4) with { EdgePolarity = polarity });

        result.Success.Should().BeTrue(result.FailureMessage);
        result.ContractVersion.Should().Be(CircleCaliperFitV2Request.ContractVersionValue);
        result.ResolvedPolarity.Should().Be(polarity);
        result.CenterX!.Value.Should().BeApproximately(160.25, 0.55);
        result.CenterY!.Value.Should().BeApproximately(120.75, 0.55);
        result.Radius!.Value.Should().BeApproximately(55.4, 0.45);
        result.ValidCaliperCount.Should().BeGreaterThan(70);
        result.ResidualRmse.Should().BeLessThan(0.65);
        result.EdgePoints.Should().HaveCountGreaterThan(70);
        result.InlierPoints.Should().HaveCountGreaterThan(70);
    }

    [Fact]
    public void Fit_WithAutoPolarity_ShouldUseGlobalBestHypothesis()
    {
        using var gray = CreateFilledCircleImage(300, 220, 149.4, 109.6, 48.25, 12, 220);
        var result = CircleCaliperFitV2Kernel.Fit(gray, Request(149.4, 109.6, 48.25) with
        {
            EdgePolarity = CircleCaliperFitV2EdgePolarity.Auto
        });

        result.Success.Should().BeTrue(result.FailureMessage);
        result.ResolvedPolarity.Should().Be(CircleCaliperFitV2EdgePolarity.LightToDark);
        result.CenterX!.Value.Should().BeApproximately(149.4, 0.55);
        result.CenterY!.Value.Should().BeApproximately(109.6, 0.55);
        result.Radius!.Value.Should().BeApproximately(48.25, 0.50);
    }

    [Fact]
    public void Fit_ShouldBeDeterministicAcrossRepeatedRuns()
    {
        using var gray = CreateNoisyCircleImage(360, 260, 181.2, 132.8, 61.6, seed: 7731);
        var request = Request(181.2, 132.8, 61.6) with
        {
            EdgePolarity = CircleCaliperFitV2EdgePolarity.LightToDark,
            MinEdgeStrength = 3.0,
            EdgeThreshold = 0.0
        };

        var first = CircleCaliperFitV2Kernel.Fit(gray, request);
        var second = CircleCaliperFitV2Kernel.Fit(gray, request);

        first.Success.Should().BeTrue(first.FailureMessage);
        second.Success.Should().BeTrue(second.FailureMessage);
        second.CenterX.Should().Be(first.CenterX);
        second.CenterY.Should().Be(first.CenterY);
        second.Radius.Should().Be(first.Radius);
        second.ResidualRmse.Should().Be(first.ResidualRmse);
        second.InlierPoints.Select(p => p.CaliperIndex).Should().Equal(first.InlierPoints.Select(p => p.CaliperIndex));
    }

    [Fact]
    public void Fit_WithLowContrastCircle_ShouldUseAdaptiveThreshold()
    {
        using var gray = CreateFilledCircleImage(260, 220, 130.5, 111.25, 42.75, 100, 126);
        var result = CircleCaliperFitV2Kernel.Fit(gray, Request(130.5, 111.25, 42.75) with
        {
            EdgePolarity = CircleCaliperFitV2EdgePolarity.LightToDark,
            MinEdgeStrength = 1.5,
            MaxResidualRmse = 1.2
        });

        result.Success.Should().BeTrue(result.FailureMessage);
        result.Radius!.Value.Should().BeApproximately(42.75, 0.70);
        result.MedianEdgeStrength.Should().BeGreaterThan(1.5);
    }

    [Fact]
    public void Fit_WithOcclusion_ShouldRejectMissingCalipersAndKeepCoverageDiagnostics()
    {
        using var gray = CreateFilledCircleImage(320, 240, 160, 120, 56, 0, 230);
        EraseSector(gray, 160, 120, 72, startDegrees: 10, endDegrees: 100, color: 0);

        var result = CircleCaliperFitV2Kernel.Fit(gray, Request(160, 120, 56) with
        {
            EdgePolarity = CircleCaliperFitV2EdgePolarity.LightToDark,
            MinCoverageRatio = 0.55,
            MinAngularCoverageDegrees = 180
        });

        result.Success.Should().BeTrue(result.FailureMessage);
        result.RejectedCaliperCount.Should().BeGreaterThan(0);
        result.AngularCoverageDegrees.Should().BeGreaterThan(200);
    }

    [Fact]
    public void Fit_WithInsufficientCoverage_ShouldFailWithoutFakeCircle()
    {
        using var gray = CreateFilledCircleImage(320, 240, 160, 120, 56, 0, 230);
        EraseSector(gray, 160, 120, 72, startDegrees: 0, endDegrees: 165, color: 0);

        var result = CircleCaliperFitV2Kernel.Fit(gray, Request(160, 120, 56) with
        {
            EdgePolarity = CircleCaliperFitV2EdgePolarity.LightToDark,
            MinCoverageRatio = 0.80,
            MinAngularCoverageDegrees = 260
        });

        result.Success.Should().BeFalse($"code={result.FailureCode} rmse={result.ResidualRmse:F4} max={result.ResidualMax:F4} inliers={result.InlierPoints.Count} outliers={result.OutlierPoints.Count} radius={result.Radius:F4}");
        result.FailureCode.Should().Be(CircleCaliperFitV2FailureCode.InsufficientCoverage);
        result.CenterX.Should().BeNull();
        result.CenterY.Should().BeNull();
        result.Radius.Should().BeNull();
        result.CollectedPointCount.Should().BeGreaterThan(0);
        result.InlierPoints.Should().NotBeEmpty();
        result.CoverageRatio.Should().BeGreaterThan(0);
        result.AngularCoverageDegrees.Should().BeGreaterThan(0);
        AssertSceneReadyPartition(result);
    }

    [Fact]
    public void Fit_WithResidualTooHigh_ShouldPreserveResidualEvidenceWithoutFakeCircle()
    {
        using var gray = CreateCircleWithRadialOutlierSectorImage();

        var result = CircleCaliperFitV2Kernel.Fit(gray, Request(160, 120, 56) with
        {
            MaxRadius = 104,
            EdgePolarity = CircleCaliperFitV2EdgePolarity.LightToDark,
            OutlierMode = CircleCaliperFitV2OutlierMode.None,
            MaxResidualRmse = 0.50
        });

        result.Success.Should().BeFalse($"code={result.FailureCode} rmse={result.ResidualRmse:F4} inliers={result.InlierPoints.Count} outliers={result.OutlierPoints.Count}");
        result.FailureCode.Should().Be(CircleCaliperFitV2FailureCode.ResidualTooHigh);
        result.CenterX.Should().BeNull();
        result.CenterY.Should().BeNull();
        result.Radius.Should().BeNull();
        result.ResidualRmse.Should().BeGreaterThan(0.05);
        result.ResidualMax.Should().BeGreaterThanOrEqualTo(result.ResidualRmse);
        result.InlierPoints.Should().NotBeEmpty();
        AssertSceneReadyPartition(result);
    }

    [Fact]
    public void Fit_WhenOutlierRejectionLeavesTooFewInliers_ShouldPreserveInlierOutlierPartition()
    {
        using var gray = CreateCircleWithRadialOutlierSectorImage();

        var result = CircleCaliperFitV2Kernel.Fit(gray, Request(160, 120, 56) with
        {
            MaxRadius = 104,
            EdgePolarity = CircleCaliperFitV2EdgePolarity.LightToDark,
            OutlierMode = CircleCaliperFitV2OutlierMode.Mad,
            OutlierThreshold = 1.5,
            MinValidCalipers = 86,
            MaxResidualRmse = 1.4
        });

        result.Success.Should().BeFalse();
        result.FailureCode.Should().Be(CircleCaliperFitV2FailureCode.InsufficientEdges);
        result.InlierPoints.Should().NotBeEmpty();
        result.OutlierPoints.Should().NotBeEmpty();
        result.ValidCaliperCount.Should().Be(result.InlierPoints.Count);
        AssertSceneReadyPartition(result);
    }

    [Fact]
    public void Fit_WithCompetingConcentricEdges_ShouldReturnAmbiguousEdge()
    {
        using var gray = new Mat(240, 320, MatType.CV_8UC1, Scalar.Black);
        Cv2.Circle(gray, new Point(160, 120), 50, Scalar.White, 4, LineTypes.AntiAlias);
        Cv2.Circle(gray, new Point(160, 120), 60, Scalar.White, 4, LineTypes.AntiAlias);

        var result = CircleCaliperFitV2Kernel.Fit(gray, Request(160, 120, 55) with
        {
            MinRadius = 44,
            MaxRadius = 66,
            NominalRadius = 55,
            EdgePolarity = CircleCaliperFitV2EdgePolarity.LightToDark,
            EdgeThreshold = 4,
            MinEdgeStrength = 4
        });

        result.Success.Should().BeFalse();
        result.FailureCode.Should().Be(CircleCaliperFitV2FailureCode.AmbiguousEdge);
    }

    [Fact]
    public void Fit_NearImageBoundaryButInsideSearchRing_ShouldSucceed()
    {
        using var gray = CreateFilledCircleImage(180, 180, 72.5, 74.0, 44.0, 0, 235);
        var result = CircleCaliperFitV2Kernel.Fit(gray, Request(72.5, 74.0, 44.0) with
        {
            MinRadius = 38,
            MaxRadius = 50,
            NominalRadius = 44,
            EdgePolarity = CircleCaliperFitV2EdgePolarity.LightToDark
        });

        result.Success.Should().BeTrue(result.FailureMessage);
        result.CenterX!.Value.Should().BeApproximately(72.5, 0.5);
        result.CenterY!.Value.Should().BeApproximately(74.0, 0.5);
    }

    [Fact]
    public void Fit_WhenSearchRingLeavesImage_ShouldReturnSearchRegionOutsideImage()
    {
        using var gray = CreateFilledCircleImage(180, 180, 35, 35, 32, 0, 235);
        var result = CircleCaliperFitV2Kernel.Fit(gray, Request(35, 35, 32) with
        {
            MinRadius = 25,
            MaxRadius = 42,
            NominalRadius = 32
        });

        result.Success.Should().BeFalse();
        result.FailureCode.Should().Be(CircleCaliperFitV2FailureCode.SearchRegionOutsideImage);
    }

    [Fact]
    public void Fit_WithNonFiniteInput_ShouldReturnInvalidInput()
    {
        using var gray = CreateFilledCircleImage(180, 180, 90, 90, 32, 0, 235);
        var result = CircleCaliperFitV2Kernel.Fit(gray, Request(90, 90, 32) with
        {
            SearchCenterX = double.NaN
        });

        result.Success.Should().BeFalse();
        result.FailureCode.Should().Be(CircleCaliperFitV2FailureCode.InvalidInput);
    }

    [Fact]
    public void Fit_WithUnboundedCollectionRequest_ShouldReturnInvalidInput()
    {
        using var gray = CreateFilledCircleImage(180, 180, 90, 90, 32, 0, 235);
        var result = CircleCaliperFitV2Kernel.Fit(gray, Request(90, 90, 32) with
        {
            CaliperCount = CircleCaliperFitV2Request.MaxCaliperCountLimit + 1
        });

        result.Success.Should().BeFalse();
        result.FailureCode.Should().Be(CircleCaliperFitV2FailureCode.InvalidInput);
    }

    [Fact]
    public void Fit_WhenSamplingWorkBudgetExceeded_ShouldReturnInvalidInputBeforeSampling()
    {
        using var gray = CreateFilledCircleImage(240, 240, 120, 120, 40, 0, 235);
        var result = CircleCaliperFitV2Kernel.Fit(gray, Request(120, 120, 40) with
        {
            CaliperCount = 720,
            ProfileSampleCount = 4096,
            AveragingThickness = 3
        });

        result.Success.Should().BeFalse();
        result.FailureCode.Should().Be(CircleCaliperFitV2FailureCode.InvalidInput);
        result.CollectedPointCount.Should().Be(0);
        result.Diagnostics.Should().Contain(diagnostic => diagnostic.Code == "sampling.work-budget");
    }

    [Fact]
    public void Fit_WithAutoPolarity_ShouldBudgetBothHypothesesBeforeSampling()
    {
        using var gray = CreateFilledCircleImage(640, 640, 320, 320, 120, 0, 235);
        var result = CircleCaliperFitV2Kernel.Fit(gray, Request(320, 320, 120) with
        {
            MinRadius = 100,
            MaxRadius = 140,
            NominalRadius = 120,
            EdgePolarity = CircleCaliperFitV2EdgePolarity.Auto,
            CaliperCount = 512,
            ProfileSampleCount = 4096,
            AveragingThickness = 2
        });

        result.Success.Should().BeFalse();
        result.FailureCode.Should().Be(CircleCaliperFitV2FailureCode.InvalidInput);
        result.CollectedPointCount.Should().Be(0);
        result.Diagnostics.Should().Contain(diagnostic =>
            diagnostic.Code == "sampling.work-budget" &&
            diagnostic.Value.HasValue &&
            diagnostic.Value.Value > CircleCaliperFitV2Request.MaxSamplingWorkUnits);
    }

    [Fact]
    public void Fit_WithProfileEvidence_ShouldReturnVersionedBoundedFullCircleProfilesFromSingleRun()
    {
        using var gray = CreateFilledCircleImage(320, 240, 160.25, 120.75, 55.4, 0, 230);
        var result = CircleCaliperFitV2Kernel.Fit(gray, Request(160.25, 120.75, 55.4) with
        {
            EdgePolarity = CircleCaliperFitV2EdgePolarity.LightToDark,
            ProfileSampleCount = 257,
            IncludeProfileEvidence = true
        });

        result.Success.Should().BeTrue(result.FailureMessage);
        result.ProfileEvidence.Should().HaveCount(CircleCaliperFitV2Request.MaxProfileEvidenceCount);
        result.ProfileEvidence.Sum(item => item.Samples.Count).Should().BeLessThanOrEqualTo(CircleCaliperFitV2Request.MaxProfileEvidenceTotalSamples);
        result.ProfileEvidence.Should().OnlyContain(item =>
            item.ContractVersion == CircleCaliperFitV2ProfileEvidence.ContractVersionValue &&
            item.Samples.Count <= CircleCaliperFitV2Request.MaxProfileEvidenceSamplesPerProfile &&
            item.OriginalSampleCount == 257);
        result.ProfileEvidence.Select(item => item.CaliperIndex).Should().OnlyHaveUniqueItems();
        result.ProfileEvidence.Min(item => item.CaliperIndex).Should().Be(0);
        result.ProfileEvidence.Max(item => item.CaliperIndex).Should().BeGreaterThan(80);
    }

    [Fact]
    public void Fit_WithHuberMode_ShouldUseDeterministicWeightedFitDistinctFromMadAndNone()
    {
        using var gray = CreateCircleWithRadialOutlierSectorImage();
        var baseRequest = Request(160, 120, 56) with
        {
            MaxRadius = 104,
            EdgePolarity = CircleCaliperFitV2EdgePolarity.LightToDark,
            MinValidCalipers = 24,
            MaxResidualRmse = 20.0,
            OutlierThreshold = 1.5,
            MaxOutlierIterations = 6
        };

        var none = CircleCaliperFitV2Kernel.Fit(gray, baseRequest with { OutlierMode = CircleCaliperFitV2OutlierMode.None });
        var mad = CircleCaliperFitV2Kernel.Fit(gray, baseRequest with { OutlierMode = CircleCaliperFitV2OutlierMode.Mad });
        var huber = CircleCaliperFitV2Kernel.Fit(gray, baseRequest with { OutlierMode = CircleCaliperFitV2OutlierMode.Huber });
        var huberRepeat = CircleCaliperFitV2Kernel.Fit(gray, baseRequest with { OutlierMode = CircleCaliperFitV2OutlierMode.Huber });

        none.Success.Should().BeTrue(none.FailureMessage);
        mad.Success.Should().BeTrue(mad.FailureMessage);
        huber.Success.Should().BeTrue(huber.FailureMessage);
        huber.Diagnostics.Should().Contain(diagnostic => diagnostic.Code == "huber.iterations");
        huber.Diagnostics.Should().Contain(diagnostic => diagnostic.Code == "huber.robustScale");
        huber.Diagnostics.Should().Contain(diagnostic => diagnostic.Code == "huber.delta");
        Math.Abs(huber.Radius!.Value - none.Radius!.Value).Should().BeGreaterThan(0.01, $"none r={none.Radius:F4} rmse={none.ResidualRmse:F4} mad r={mad.Radius:F4} rmse={mad.ResidualRmse:F4} huber r={huber.Radius:F4} rmse={huber.ResidualRmse:F4}");
        huber.Diagnostics.Select(diagnostic => diagnostic.Code).Should().NotEqual(mad.Diagnostics.Select(diagnostic => diagnostic.Code));
        huber.ResidualRmse.Should().BeLessThan(none.ResidualRmse);
        huberRepeat.CenterX.Should().Be(huber.CenterX);
        huberRepeat.CenterY.Should().Be(huber.CenterY);
        huberRepeat.Radius.Should().Be(huber.Radius);
        huberRepeat.InlierPoints.Select(point => point.CaliperIndex).Should().Equal(huber.InlierPoints.Select(point => point.CaliperIndex));
    }

    [Fact]
    public void Fit_WithMadModeAtIterationLimit_ShouldReportNotConverged()
    {
        using var gray = CreateCircleWithRadialOutlierSectorImage();
        var result = CircleCaliperFitV2Kernel.Fit(gray, Request(160, 120, 56) with
        {
            MaxRadius = 104,
            EdgePolarity = CircleCaliperFitV2EdgePolarity.LightToDark,
            MinValidCalipers = 24,
            MaxResidualRmse = 20.0,
            OutlierThreshold = 1.5,
            OutlierMode = CircleCaliperFitV2OutlierMode.Mad,
            MaxOutlierIterations = 1
        });

        result.Diagnostics.Should().Contain(diagnostic =>
            diagnostic.Code == "outlier.iterations" &&
            diagnostic.Value == 1.0);
        result.Diagnostics.Should().Contain(diagnostic =>
            diagnostic.Code == "outlier.converged" &&
            diagnostic.Value == 0.0 &&
            diagnostic.Message.Contains("stopped before convergence", StringComparison.Ordinal));
    }

    [Fact]
    public void Fit_WithCancellation_ShouldPropagateOperationCanceledException()
    {
        using var gray = CreateFilledCircleImage(220, 220, 110, 110, 44, 0, 235);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Action act = () => CircleCaliperFitV2Kernel.Fit(gray, Request(110, 110, 44), cts.Token);

        act.Should().Throw<OperationCanceledException>();
    }

    private static void AssertSceneReadyPartition(CircleCaliperFitV2Result result)
    {
        result.EdgePoints.Should().HaveCountLessThanOrEqualTo(96);
        result.CollectedPointCount.Should().Be(result.EdgePoints.Count);
        result.ValidCaliperCount.Should().Be(result.InlierPoints.Count);
        result.RejectedCaliperCount.Should().BeGreaterThanOrEqualTo(result.OutlierPoints.Count);
        result.Diagnostics.Should().HaveCountLessThanOrEqualTo(64);
        result.Diagnostics.Should().OnlyContain(diagnostic => diagnostic.Message.Length <= 160);

        var edgeIndexes = result.EdgePoints.Select(point => point.CaliperIndex).OrderBy(index => index).ToArray();
        var partitionIndexes = result.InlierPoints
            .Concat(result.OutlierPoints)
            .Select(point => point.CaliperIndex)
            .OrderBy(index => index)
            .ToArray();
        partitionIndexes.Should().Equal(edgeIndexes);
        result.InlierPoints.Select(point => point.CaliperIndex)
            .Intersect(result.OutlierPoints.Select(point => point.CaliperIndex))
            .Should().BeEmpty();
    }

    private static CircleCaliperFitV2Request Request(double centerX, double centerY, double radius)
    {
        return new CircleCaliperFitV2Request
        {
            SearchCenterX = centerX,
            SearchCenterY = centerY,
            MinRadius = radius - 8,
            MaxRadius = radius + 8,
            NominalRadius = radius,
            CaliperCount = 96,
            AveragingThickness = 5,
            ProfileSampleCount = 129,
            GaussianSigma = 1.2,
            EdgePolarity = CircleCaliperFitV2EdgePolarity.Auto,
            EdgeThreshold = 0,
            MinEdgeStrength = 4,
            MinValidCalipers = 28,
            MinCoverageRatio = 0.35,
            MinAngularCoverageDegrees = 180,
            OutlierMode = CircleCaliperFitV2OutlierMode.Mad,
            OutlierThreshold = 3.5,
            MaxOutlierIterations = 3,
            MaxResidualRmse = 1.4
        };
    }

    private static Mat CreateFilledCircleImage(
        int width,
        int height,
        double centerX,
        double centerY,
        double radius,
        byte background,
        byte foreground,
        int supersample = 8)
    {
        var scale = Math.Max(2, supersample);
        using var hiRes = new Mat(height * scale, width * scale, MatType.CV_8UC1, new Scalar(background));
        Cv2.Circle(
            hiRes,
            new Point((int)Math.Round(centerX * scale), (int)Math.Round(centerY * scale)),
            Math.Max(1, (int)Math.Round(radius * scale)),
            new Scalar(foreground),
            -1,
            LineTypes.AntiAlias);
        var lowRes = new Mat();
        Cv2.Resize(hiRes, lowRes, new Size(width, height), 0, 0, InterpolationFlags.Area);
        return lowRes;
    }

    private static Mat CreateNoisyCircleImage(int width, int height, double centerX, double centerY, double radius, int seed)
    {
        using var gray = CreateFilledCircleImage(width, height, centerX, centerY, radius, 18, 225);
        var noisy = gray.Clone();
        var random = new Random(seed);
        for (var y = 0; y < noisy.Height; y++)
        {
            for (var x = 0; x < noisy.Width; x++)
            {
                var noise = random.Next(-6, 7);
                var value = Math.Clamp(noisy.At<byte>(y, x) + noise, 0, 255);
                noisy.Set(y, x, (byte)value);
            }
        }

        return noisy;
    }

    private static Mat CreateCircleWithRadialOutlierSectorImage()
    {
        var gray = CreateFilledCircleImage(320, 240, 160, 120, 56, 0, 230);
        EraseSector(gray, 160, 120, 72, startDegrees: 18, endDegrees: 98, color: 0);
        DrawAnnularSector(gray, 160, 120, innerRadius: 82, outerRadius: 96, startDegrees: 18, endDegrees: 98, color: 230);
        return gray;
    }

    private static void DrawAnnularSector(
        Mat gray,
        double centerX,
        double centerY,
        double innerRadius,
        double outerRadius,
        double startDegrees,
        double endDegrees,
        byte color)
    {
        var points = new List<Point>();
        for (var angle = startDegrees; angle <= endDegrees; angle += 3.0)
        {
            points.Add(PointOnCircle(centerX, centerY, outerRadius, angle));
        }

        for (var angle = endDegrees; angle >= startDegrees; angle -= 3.0)
        {
            points.Add(PointOnCircle(centerX, centerY, innerRadius, angle));
        }

        Cv2.FillPoly(gray, [points], new Scalar(color), LineTypes.AntiAlias);
    }

    private static void EraseSector(Mat gray, double centerX, double centerY, double radius, double startDegrees, double endDegrees, byte color)
    {
        var points = new List<Point> { new((int)Math.Round(centerX), (int)Math.Round(centerY)) };
        for (var angle = startDegrees; angle <= endDegrees; angle += 3.0)
        {
            points.Add(PointOnCircle(centerX, centerY, radius, angle));
        }

        Cv2.FillConvexPoly(gray, points, new Scalar(color), LineTypes.AntiAlias);
    }

    private static Point PointOnCircle(double centerX, double centerY, double radius, double angleDegrees)
    {
        var radians = angleDegrees * Math.PI / 180.0;
        return new Point(
            (int)Math.Round(centerX + (Math.Cos(radians) * radius)),
            (int)Math.Round(centerY + (Math.Sin(radians) * radius)));
    }
}
