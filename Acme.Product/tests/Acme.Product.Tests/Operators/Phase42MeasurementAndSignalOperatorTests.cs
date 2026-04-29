using System.Numerics;
using System.Diagnostics;
using Acme.Product.Core.Entities;
using Acme.Product.Core.Enums;
using Acme.Product.Core.ValueObjects;
using Acme.Product.Infrastructure.Operators;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using OpenCvSharp;
using Xunit;

namespace Acme.Product.Tests.Operators;

public class Phase42MeasurementAndSignalOperatorTests
{
    [Fact]
    public async Task ArcCaliper_ShouldDetectArcEdges()
    {
        var sut = new ArcCaliperOperator(Substitute.For<ILogger<ArcCaliperOperator>>());
        var op = new Operator("ArcCaliper", OperatorType.ArcCaliper, 0, 0);
        using var image = CreateArcEdgeImage();

        var result = await sut.ExecuteAsync(op, new Dictionary<string, object>
        {
            ["Image"] = image,
            ["CenterX"] = 100,
            ["CenterY"] = 100,
            ["Radius"] = 55,
            ["StartAngle"] = 20.0,
            ["EndAngle"] = 160.0,
            ["Transition"] = "positive"
        });

        result.IsSuccess.Should().BeTrue();
        Convert.ToInt32(result.OutputData!["Count"]).Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task ArcCaliper_ShouldFail_OnEmptyImage()
    {
        var sut = new ArcCaliperOperator(Substitute.For<ILogger<ArcCaliperOperator>>());
        var op = new Operator("ArcCaliper", OperatorType.ArcCaliper, 0, 0);
        using var empty = new Mat();

        var result = await sut.ExecuteAsync(op, new Dictionary<string, object>
        {
            ["Image"] = empty
        });

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("empty");
    }

    [Fact]
    public async Task ArcCaliper_ShouldFail_WhenRadiusPlacesArcOutsideSamplingRegion()
    {
        var sut = new ArcCaliperOperator(Substitute.For<ILogger<ArcCaliperOperator>>());
        var op = new Operator("ArcCaliper", OperatorType.ArcCaliper, 0, 0);
        using var image = CreateArcEdgeImage();

        var result = await sut.ExecuteAsync(op, new Dictionary<string, object>
        {
            ["Image"] = image,
            ["CenterX"] = 100,
            ["CenterY"] = 100,
            ["Radius"] = 160,
            ["StartAngle"] = 25.0,
            ["EndAngle"] = 155.0
        });

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("sampling region");
    }

    [Fact]
    public async Task ArcCaliper_ShouldFail_WhenArcSpanIsZero()
    {
        var sut = new ArcCaliperOperator(Substitute.For<ILogger<ArcCaliperOperator>>());
        var op = new Operator("ArcCaliper", OperatorType.ArcCaliper, 0, 0);
        using var image = CreateArcEdgeImage();

        var result = await sut.ExecuteAsync(op, new Dictionary<string, object>
        {
            ["Image"] = image,
            ["CenterX"] = 100,
            ["CenterY"] = 100,
            ["Radius"] = 55,
            ["StartAngle"] = 45.0,
            ["EndAngle"] = 45.0
        });

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("span");
    }

    [Fact]
    public async Task ArcCaliper_ShouldFail_WhenAnglesAreNotFinite()
    {
        var sut = new ArcCaliperOperator(Substitute.For<ILogger<ArcCaliperOperator>>());
        var op = new Operator("ArcCaliper", OperatorType.ArcCaliper, 0, 0);
        using var image = CreateArcEdgeImage();

        var result = await sut.ExecuteAsync(op, new Dictionary<string, object>
        {
            ["Image"] = image,
            ["CenterX"] = 100,
            ["CenterY"] = 100,
            ["Radius"] = 55,
            ["StartAngle"] = double.NaN,
            ["EndAngle"] = 135.0
        });

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("finite");
    }

    [Fact]
    public async Task ContourExtrema_ShouldReportMinAndMaxPoints()
    {
        var sut = new ContourExtremaOperator(Substitute.For<ILogger<ContourExtremaOperator>>());
        var op = new Operator("ContourExtrema", OperatorType.ContourExtrema, 0, 0);
        var contour = new[]
        {
            new Point2f(20, 40),
            new Point2f(10, 12),
            new Point2f(50, 18),
            new Point2f(40, 60)
        };

        var result = await sut.ExecuteAsync(op, new Dictionary<string, object>
        {
            ["Contour"] = contour,
            ["Direction"] = "vertical"
        });

        result.IsSuccess.Should().BeTrue();
        var minPoint = (Point2f)result.OutputData!["MinPoint"];
        var maxPoint = (Point2f)result.OutputData["MaxPoint"];
        minPoint.Y.Should().BeApproximately(12, 0.1f);
        maxPoint.Y.Should().BeApproximately(60, 0.1f);
    }

    [Fact]
    public async Task ContourExtrema_ShouldFail_OnEmptyContour()
    {
        var sut = new ContourExtremaOperator(Substitute.For<ILogger<ContourExtremaOperator>>());
        var op = new Operator("ContourExtrema", OperatorType.ContourExtrema, 0, 0);

        var result = await sut.ExecuteAsync(op, new Dictionary<string, object>
        {
            ["Contour"] = Array.Empty<Point2f>()
        });

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("at least one point");
    }

    [Fact]
    public async Task ContourExtrema_ShouldHandleSinglePointContour()
    {
        var sut = new ContourExtremaOperator(Substitute.For<ILogger<ContourExtremaOperator>>());
        var op = new Operator("ContourExtrema", OperatorType.ContourExtrema, 0, 0);
        var point = new Point2f(12, 34);

        var result = await sut.ExecuteAsync(op, new Dictionary<string, object>
        {
            ["Contour"] = new[] { point },
            ["Direction"] = "horizontal"
        });

        result.IsSuccess.Should().BeTrue();
        ((Point2f)result.OutputData!["MinPoint"]).Should().Be(point);
        ((Point2f)result.OutputData["MaxPoint"]).Should().Be(point);
        result.OutputData["ExtremaPoints"].Should().BeOfType<List<Point2f>>().Subject.Should().ContainSingle();
    }

    [Fact]
    public async Task ContourExtrema_ShouldUseDeterministicTieBreak_ForCollinearContour()
    {
        var sut = new ContourExtremaOperator(Substitute.For<ILogger<ContourExtremaOperator>>());
        var op = new Operator("ContourExtrema", OperatorType.ContourExtrema, 0, 0);
        var contour = new[]
        {
            new Point2f(40, 10),
            new Point2f(10, 10),
            new Point2f(25, 10)
        };

        var result = await sut.ExecuteAsync(op, new Dictionary<string, object>
        {
            ["Contour"] = contour,
            ["Direction"] = "vertical"
        });

        result.IsSuccess.Should().BeTrue();
        var minPoint = (Point2f)result.OutputData!["MinPoint"];
        var maxPoint = (Point2f)result.OutputData["MaxPoint"];
        minPoint.X.Should().BeApproximately(10, 0.1f);
        maxPoint.X.Should().BeApproximately(40, 0.1f);
    }

    [Fact]
    public async Task ContourExtrema_ShouldFail_ForDistanceDirectionWithoutReferencePoint()
    {
        var sut = new ContourExtremaOperator(Substitute.For<ILogger<ContourExtremaOperator>>());
        var op = new Operator("ContourExtrema", OperatorType.ContourExtrema, 0, 0);
        var contour = new[]
        {
            new Point2f(1, 1),
            new Point2f(4, 5)
        };

        var result = await sut.ExecuteAsync(op, new Dictionary<string, object>
        {
            ["Contour"] = contour,
            ["Direction"] = "distance"
        });

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("ReferencePoint");
    }

    [Fact]
    public async Task ContourExtrema_ShouldComputeDistanceExtrema_WithReferencePoint()
    {
        var sut = new ContourExtremaOperator(Substitute.For<ILogger<ContourExtremaOperator>>());
        var op = new Operator("ContourExtrema", OperatorType.ContourExtrema, 0, 0);
        var contour = new[]
        {
            new Point2f(1, 1),
            new Point2f(3, 4),
            new Point2f(8, 6)
        };

        var result = await sut.ExecuteAsync(op, new Dictionary<string, object>
        {
            ["Contour"] = contour,
            ["Direction"] = "distance",
            ["ReferencePoint"] = new Point2f(0, 0)
        });

        result.IsSuccess.Should().BeTrue();
        ((Point2f)result.OutputData!["MinPoint"]).Should().Be(new Point2f(1, 1));
        ((Point2f)result.OutputData["MaxPoint"]).Should().Be(new Point2f(8, 6));
    }

    [Fact]
    public async Task FftFilterAndInverseFft_ShouldPreserveSignalLength()
    {
        var fft = new FFT1DOperator(Substitute.For<ILogger<FFT1DOperator>>());
        var filter = new FrequencyFilterOperator(Substitute.For<ILogger<FrequencyFilterOperator>>());
        var inverse = new InverseFFT1DOperator(Substitute.For<ILogger<InverseFFT1DOperator>>());

        var signal = Enumerable.Range(0, 64)
            .Select(i => Math.Sin(2 * Math.PI * i / 8.0) + 0.25 * Math.Sin(2 * Math.PI * i / 3.0))
            .ToArray();

        var fftResult = await fft.ExecuteAsync(new Operator("FFT1D", OperatorType.FFT1D, 0, 0), new Dictionary<string, object>
        {
            ["Input"] = signal
        });
        fftResult.IsSuccess.Should().BeTrue();
        var spectrum = fftResult.OutputData!["Spectrum"].Should().BeOfType<Complex[]>().Subject;
        spectrum.Length.Should().Be(signal.Length);

        var filterResult = await filter.ExecuteAsync(new Operator("FrequencyFilter", OperatorType.FrequencyFilter, 0, 0), new Dictionary<string, object>
        {
            ["Spectrum"] = spectrum,
            ["FilterType"] = "lowpass",
            ["CutoffLow"] = 0.2,
            ["CutoffHigh"] = 0.5
        });
        filterResult.IsSuccess.Should().BeTrue();
        var filteredSpectrum = filterResult.OutputData!["FilteredSpectrum"].Should().BeOfType<Complex[]>().Subject;
        filteredSpectrum.Length.Should().Be(signal.Length);

        var inverseResult = await inverse.ExecuteAsync(new Operator("InverseFFT1D", OperatorType.InverseFFT1D, 0, 0), new Dictionary<string, object>
        {
            ["Spectrum"] = filteredSpectrum
        });
        inverseResult.IsSuccess.Should().BeTrue();
        var reconstructed = inverseResult.OutputData!["Signal"].Should().BeOfType<double[]>().Subject;
        reconstructed.Length.Should().Be(signal.Length);
    }

    [Fact]
    public async Task FrequencyFilter_WithUnknownFilterType_ShouldFailClosed()
    {
        var sut = new FrequencyFilterOperator(Substitute.For<ILogger<FrequencyFilterOperator>>());
        var spectrum = new[]
        {
            Complex.One,
            new Complex(0.5, -0.25),
            Complex.Zero,
            new Complex(-0.5, 0.25)
        };

        var result = await sut.ExecuteAsync(new Operator("FrequencyFilter", OperatorType.FrequencyFilter, 0, 0), new Dictionary<string, object>
        {
            ["Spectrum"] = spectrum,
            ["FilterType"] = "identity-by-typo",
            ["CutoffLow"] = 0.2,
            ["CutoffHigh"] = 0.4
        });

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("FilterType");
    }

    [Fact]
    public async Task FftAndInverseFft_ShouldReconstructBinAlignedSignalWithinTolerance()
    {
        var fft = new FFT1DOperator(Substitute.For<ILogger<FFT1DOperator>>());
        var inverse = new InverseFFT1DOperator(Substitute.For<ILogger<InverseFFT1DOperator>>());

        const int sampleCount = 128;
        var signal = Enumerable.Range(0, sampleCount)
            .Select(i => 1.2 * Math.Sin(2 * Math.PI * 4 * i / sampleCount) + 0.35 * Math.Sin(2 * Math.PI * 12 * i / sampleCount))
            .ToArray();

        var fftResult = await fft.ExecuteAsync(new Operator("FFT1D", OperatorType.FFT1D, 0, 0), new Dictionary<string, object>
        {
            ["Input"] = signal
        });

        fftResult.IsSuccess.Should().BeTrue();

        var inverseResult = await inverse.ExecuteAsync(new Operator("InverseFFT1D", OperatorType.InverseFFT1D, 0, 0), new Dictionary<string, object>
        {
            ["Spectrum"] = fftResult.OutputData!["Spectrum"]
        });

        inverseResult.IsSuccess.Should().BeTrue();

        var reconstructed = inverseResult.OutputData!["Signal"].Should().BeOfType<double[]>().Subject;
        reconstructed.Length.Should().Be(sampleCount);

        var mse = signal.Zip(reconstructed, (expected, actual) => Math.Pow(expected - actual, 2)).Average();
        var maxAbs = signal.Zip(reconstructed, (expected, actual) => Math.Abs(expected - actual)).Max();

        mse.Should().BeLessThan(1e-6, "FFT/IFFT round-trip should preserve bin-aligned lab signal energy");
        maxAbs.Should().BeLessThan(5e-3, "FFT/IFFT round-trip should preserve sample amplitudes within mill-level tolerance");
    }

    [Fact]
    public async Task FrequencyOperators_LabBudget1024PointChain_ShouldStayWithinBudgetAndAttenuateHighFrequency()
    {
        var fft = new FFT1DOperator(Substitute.For<ILogger<FFT1DOperator>>());
        var filter = new FrequencyFilterOperator(Substitute.For<ILogger<FrequencyFilterOperator>>());
        var inverse = new InverseFFT1DOperator(Substitute.For<ILogger<InverseFFT1DOperator>>());

        const int sampleCount = 1024;
        const int iterations = 24;
        var signal = Enumerable.Range(0, sampleCount)
            .Select(i => Math.Sin(2 * Math.PI * 8 * i / sampleCount) + 0.45 * Math.Sin(2 * Math.PI * 192 * i / sampleCount))
            .ToArray();

        var elapsedSamples = new List<double>(iterations);
        double attenuationRatio = 1.0;

        for (var i = 0; i < iterations; i++)
        {
            var sw = Stopwatch.StartNew();

            var fftResult = await fft.ExecuteAsync(new Operator("FFT1D", OperatorType.FFT1D, 0, 0), new Dictionary<string, object>
            {
                ["Input"] = signal
            });
            fftResult.IsSuccess.Should().BeTrue();

            var spectrum = fftResult.OutputData!["Spectrum"].Should().BeOfType<Complex[]>().Subject;
            var originalHigh = spectrum[192].Magnitude;

            var filterResult = await filter.ExecuteAsync(new Operator("FrequencyFilter", OperatorType.FrequencyFilter, 0, 0), new Dictionary<string, object>
            {
                ["Spectrum"] = spectrum,
                ["FilterType"] = "lowpass",
                ["CutoffLow"] = 0.08,
                ["CutoffHigh"] = 0.2,
                ["Order"] = 4
            });
            filterResult.IsSuccess.Should().BeTrue();

            var filteredSpectrum = filterResult.OutputData!["FilteredSpectrum"].Should().BeOfType<Complex[]>().Subject;
            attenuationRatio = filteredSpectrum[192].Magnitude / originalHigh;

            var inverseResult = await inverse.ExecuteAsync(new Operator("InverseFFT1D", OperatorType.InverseFFT1D, 0, 0), new Dictionary<string, object>
            {
                ["Spectrum"] = filteredSpectrum
            });
            inverseResult.IsSuccess.Should().BeTrue();
            inverseResult.OutputData!["Signal"].Should().BeOfType<double[]>().Subject.Length.Should().Be(sampleCount);

            sw.Stop();
            elapsedSamples.Add(sw.Elapsed.TotalMilliseconds);
        }

        attenuationRatio.Should().BeLessThan(0.1, "low-pass filter should strongly attenuate high-frequency bin-aligned component");

        var averageMs = elapsedSamples.Average();
        var budgetMs = GetEnvDouble("CV_FREQUENCY_CHAIN_BUDGET_MS", 25.0, 5.0, 200.0);
        averageMs.Should().BeLessThan(budgetMs, $"1024-point FFT -> lowpass -> IFFT lab chain should stay within the configured audit budget ({budgetMs:0.##} ms)");
    }

    [Fact]
    public async Task FrequencyOperators_ShouldPreserveConjugateSymmetry_ForRealSignals()
    {
        var fft = new FFT1DOperator(Substitute.For<ILogger<FFT1DOperator>>());
        var filter = new FrequencyFilterOperator(Substitute.For<ILogger<FrequencyFilterOperator>>());
        var inverse = new InverseFFT1DOperator(Substitute.For<ILogger<InverseFFT1DOperator>>());

        const int sampleCount = 256;
        const int bin = 12;
        var signal = Enumerable.Range(0, sampleCount)
            .Select(i => Math.Sin(2 * Math.PI * bin * i / sampleCount))
            .ToArray();

        var fftResult = await fft.ExecuteAsync(new Operator("FFT1D", OperatorType.FFT1D, 0, 0), new Dictionary<string, object>
        {
            ["Input"] = signal
        });
        fftResult.IsSuccess.Should().BeTrue();

        var spectrum = fftResult.OutputData!["Spectrum"].Should().BeOfType<Complex[]>().Subject;
        var filterResult = await filter.ExecuteAsync(new Operator("FrequencyFilter", OperatorType.FrequencyFilter, 0, 0), new Dictionary<string, object>
        {
            ["Spectrum"] = spectrum,
            ["FilterType"] = "lowpass",
            ["CutoffLow"] = 0.08,
            ["Order"] = 4
        });

        filterResult.IsSuccess.Should().BeTrue();
        var filtered = filterResult.OutputData!["FilteredSpectrum"].Should().BeOfType<Complex[]>().Subject;

        var ratioPositive = filtered[bin].Magnitude / spectrum[bin].Magnitude;
        var ratioNegative = filtered[sampleCount - bin].Magnitude / spectrum[sampleCount - bin].Magnitude;
        ratioPositive.Should().BeApproximately(ratioNegative, 1e-6, "signed-bin filter masks must preserve conjugate symmetry for real-valued inputs");

        var inverseResult = await inverse.ExecuteAsync(new Operator("InverseFFT1D", OperatorType.InverseFFT1D, 0, 0), new Dictionary<string, object>
        {
            ["Spectrum"] = filtered
        });

        inverseResult.IsSuccess.Should().BeTrue();
        var imaginary = inverseResult.OutputData!["Imaginary"].Should().BeOfType<double[]>().Subject;
        imaginary.Max(static value => Math.Abs(value)).Should().BeLessThan(1e-4, "conjugate-symmetric spectra should reconstruct with negligible imaginary residue");
    }

    [Fact]
    public async Task FftAndInverseFft_ShouldRoundTrip_ImageSpectrum()
    {
        var fft = new FFT1DOperator(Substitute.For<ILogger<FFT1DOperator>>());
        var inverse = new InverseFFT1DOperator(Substitute.For<ILogger<InverseFFT1DOperator>>());
        using var image = CreateFrequencyImage();
        using var reference = image.GetMat().Clone();

        var fftResult = await fft.ExecuteAsync(new Operator("FFT1D", OperatorType.FFT1D, 0, 0), new Dictionary<string, object>
        {
            ["Input"] = image
        });

        fftResult.IsSuccess.Should().BeTrue();
        var spectrum = fftResult.OutputData!["Spectrum"].Should().BeOfType<ImageWrapper>().Subject;

        var inverseResult = await inverse.ExecuteAsync(new Operator("InverseFFT1D", OperatorType.InverseFFT1D, 0, 0), new Dictionary<string, object>
        {
            ["Spectrum"] = spectrum
        });

        inverseResult.IsSuccess.Should().BeTrue();
        using var reconstructedWrapper = inverseResult.OutputData!["Signal"].Should().BeOfType<ImageWrapper>().Subject;
        using var reconstructed = reconstructedWrapper.GetMat();

        var mse = 0.0;
        for (var y = 0; y < reference.Rows; y++)
        {
            for (var x = 0; x < reference.Cols; x++)
            {
                var diff = reconstructed.At<float>(y, x) - reference.At<byte>(y, x);
                mse += diff * diff;
            }
        }

        mse /= (reference.Rows * reference.Cols);
        mse.Should().BeLessThan(1e-3, "2D complex spectra should round-trip without lossy normalization");
    }

    [Fact]
    public async Task PhaseClosure_ShouldRecoverSmoothWrappedRamp()
    {
        var sut = new PhaseClosureOperator(Substitute.For<ILogger<PhaseClosureOperator>>());
        var op = new Operator("PhaseClosure", OperatorType.PhaseClosure, 0, 0);
        using var phase = CreateWrappedPhaseImage();

        var result = await sut.ExecuteAsync(op, new Dictionary<string, object>
        {
            ["PhaseImage"] = phase,
            ["UnwrapMethod"] = "itoh"
        });

        result.IsSuccess.Should().BeTrue();
        result.OutputData.Should().ContainKey("UnwrappedPhase");
        result.OutputData.Should().ContainKey("Quality");

        using var unwrappedWrapper = result.OutputData["UnwrappedPhase"].Should().BeOfType<ImageWrapper>().Subject;
        using var unwrapped = unwrappedWrapper.GetMat();

        var mae = 0.0;
        for (var y = 0; y < unwrapped.Rows; y++)
        {
            for (var x = 0; x < unwrapped.Cols; x++)
            {
                var expected = (x * 0.25) + (y * 0.18);
                mae += Math.Abs(unwrapped.At<float>(y, x) - expected);
            }
        }

        mae /= (unwrapped.Rows * unwrapped.Cols);
        mae.Should().BeLessThan(0.25, "phase unwrap should preserve the original wrapped ramp instead of re-normalizing it");
    }

    [Fact]
    public async Task PhaseClosure_ShouldKeepUniformPhaseStable()
    {
        var sut = new PhaseClosureOperator(Substitute.For<ILogger<PhaseClosureOperator>>());
        var op = new Operator("PhaseClosure", OperatorType.PhaseClosure, 0, 0);
        using var phase = CreateUniformPhaseImage(size: 40, value: 1.25f);

        var result = await sut.ExecuteAsync(op, new Dictionary<string, object>
        {
            ["PhaseImage"] = phase,
            ["UnwrapMethod"] = "itoh"
        });

        result.IsSuccess.Should().BeTrue();
        Convert.ToDouble(result.OutputData!["Quality"]).Should().BeGreaterThan(0.99);

        using var unwrappedWrapper = result.OutputData["UnwrappedPhase"].Should().BeOfType<ImageWrapper>().Subject;
        using var unwrapped = unwrappedWrapper.GetMat();
        using var discontinuitiesWrapper = result.OutputData["Discontinuities"].Should().BeOfType<ImageWrapper>().Subject;
        using var discontinuities = discontinuitiesWrapper.GetMat();

        Cv2.CountNonZero(discontinuities).Should().Be(0);
        CountNonFinitePixels(unwrapped).Should().Be(0);
        unwrapped.At<float>(0, 0).Should().BeApproximately(1.25f, 1e-4f);
    }

    [Fact]
    public async Task PhaseClosure_ShouldUseExternalQualityMap_ForQualityMethod()
    {
        var sut = new PhaseClosureOperator(Substitute.For<ILogger<PhaseClosureOperator>>());
        var op = new Operator("PhaseClosure", OperatorType.PhaseClosure, 0, 0);
        using var phase = CreateWrappedPhaseImage();
        using var qualityMap = CreatePhaseQualityMap(phase.Rows, phase.Cols);

        var result = await sut.ExecuteAsync(op, new Dictionary<string, object>
        {
            ["PhaseImage"] = phase,
            ["QualityMap"] = qualityMap,
            ["UnwrapMethod"] = "quality"
        });

        result.IsSuccess.Should().BeTrue();
        result.OutputData!["Method"].Should().Be("quality");

        using var unwrappedWrapper = result.OutputData["UnwrappedPhase"].Should().BeOfType<ImageWrapper>().Subject;
        using var unwrapped = unwrappedWrapper.GetMat();

        CountNonFinitePixels(unwrapped).Should().Be(0);
        ComputeRampMaeAllowingGlobalOffset(unwrapped).Should().BeLessThan(0.35);
    }

    [Fact]
    public async Task PhaseClosure_ShouldFail_WhenQualityMapSizeDoesNotMatch()
    {
        var sut = new PhaseClosureOperator(Substitute.For<ILogger<PhaseClosureOperator>>());
        var op = new Operator("PhaseClosure", OperatorType.PhaseClosure, 0, 0);
        using var phase = CreateWrappedPhaseImage();
        using var qualityMap = CreatePhaseQualityMap(phase.Rows / 2, phase.Cols / 2);

        var result = await sut.ExecuteAsync(op, new Dictionary<string, object>
        {
            ["PhaseImage"] = phase,
            ["QualityMap"] = qualityMap,
            ["UnwrapMethod"] = "quality"
        });

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("QualityMap");
    }

    [Fact]
    public async Task PhaseClosure_FloodFill_ShouldMarkLargeDiscontinuities()
    {
        var sut = new PhaseClosureOperator(Substitute.For<ILogger<PhaseClosureOperator>>());
        var op = new Operator("PhaseClosure", OperatorType.PhaseClosure, 0, 0);
        using var phase = CreateWrappedPhaseImageWithDiscontinuity();

        var result = await sut.ExecuteAsync(op, new Dictionary<string, object>
        {
            ["PhaseImage"] = phase,
            ["UnwrapMethod"] = "floodfill"
        });

        result.IsSuccess.Should().BeTrue();
        result.OutputData!["Method"].Should().Be("floodfill");
        Convert.ToDouble(result.OutputData["Quality"]).Should().BeGreaterThan(0.0).And.BeLessOrEqualTo(1.0);

        using var unwrappedWrapper = result.OutputData["UnwrappedPhase"].Should().BeOfType<ImageWrapper>().Subject;
        using var unwrapped = unwrappedWrapper.GetMat();
        using var discontinuitiesWrapper = result.OutputData["Discontinuities"].Should().BeOfType<ImageWrapper>().Subject;
        using var discontinuities = discontinuitiesWrapper.GetMat();

        CountNonFinitePixels(unwrapped).Should().Be(0);
        Cv2.CountNonZero(discontinuities).Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task MorphologicalOperation_ShouldSupportTopHatAndBlackHat()
    {
        var sut = new MorphologicalOperationOperator(Substitute.For<ILogger<MorphologicalOperationOperator>>());

        foreach (var operation in new[] { "TopHat", "BlackHat" })
        {
            using var image = CreateMorphologyDetailImage();
            var op = new Operator($"Morph_{operation}", OperatorType.MorphologicalOperation, 0, 0);
            op.Parameters.Add(TestHelpers.CreateParameter("Operation", operation, "string"));

            var result = await sut.ExecuteAsync(op, TestHelpers.CreateImageInputs(image));

            result.IsSuccess.Should().BeTrue();
            result.OutputData.Should().ContainKey("Image");
            result.OutputData["Operation"].Should().Be(operation);
        }
    }

    private static ImageWrapper CreateArcEdgeImage()
    {
        var mat = new Mat(200, 200, MatType.CV_8UC3, Scalar.Black);
        Cv2.Circle(mat, new Point(100, 100), 55, Scalar.White, 4);
        return new ImageWrapper(mat);
    }

    private static Mat CreateWrappedPhaseImage()
    {
        var phase = new Mat(64, 64, MatType.CV_32FC1);
        for (var y = 0; y < phase.Rows; y++)
        {
            for (var x = 0; x < phase.Cols; x++)
            {
                var value = ((x * 0.25) + (y * 0.18)) % (2 * Math.PI);
                phase.Set(y, x, (float)value);
            }
        }

        return phase;
    }

    private static Mat CreateUniformPhaseImage(int size, float value)
    {
        return new Mat(size, size, MatType.CV_32FC1, Scalar.All(value));
    }

    private static Mat CreateWrappedPhaseImageWithDiscontinuity()
    {
        var phase = new Mat(48, 48, MatType.CV_32FC1);
        for (var y = 0; y < phase.Rows; y++)
        {
            for (var x = 0; x < phase.Cols; x++)
            {
                var value = y * 0.04;
                if (x >= phase.Cols / 2)
                {
                    value += Math.PI - 0.05;
                }

                phase.Set(y, x, WrapPhase(value));
            }
        }

        return phase;
    }

    private static Mat CreatePhaseQualityMap(int rows, int cols)
    {
        var quality = new Mat(rows, cols, MatType.CV_8UC1);
        var centerX = (cols - 1) / 2.0;
        var centerY = (rows - 1) / 2.0;

        for (var y = 0; y < rows; y++)
        {
            for (var x = 0; x < cols; x++)
            {
                var distance = Math.Sqrt(Math.Pow(x - centerX, 2) + Math.Pow(y - centerY, 2));
                var value = 255.0 - (distance * 6.0);
                quality.Set(y, x, (byte)Math.Clamp((int)Math.Round(value), 1, 255));
            }
        }

        return quality;
    }

    private static ImageWrapper CreateFrequencyImage()
    {
        var mat = new Mat(48, 64, MatType.CV_8UC1);
        for (var y = 0; y < mat.Rows; y++)
        {
            for (var x = 0; x < mat.Cols; x++)
            {
                var value = 90 + (35 * Math.Sin((2 * Math.PI * x) / 16.0)) + (20 * Math.Cos((2 * Math.PI * y) / 12.0));
                mat.Set(y, x, (byte)Math.Clamp((int)Math.Round(value), 0, 255));
            }
        }

        return new ImageWrapper(mat);
    }

    private static ImageWrapper CreateMorphologyDetailImage()
    {
        var mat = new Mat(120, 120, MatType.CV_8UC3, new Scalar(30, 30, 30));
        Cv2.Rectangle(mat, new Rect(20, 20, 80, 60), new Scalar(70, 70, 70), -1);
        Cv2.Circle(mat, new Point(60, 50), 8, Scalar.White, -1);
        Cv2.Circle(mat, new Point(90, 80), 6, Scalar.Black, -1);
        return new ImageWrapper(mat);
    }

    private static double GetEnvDouble(string name, double defaultValue, double minValue, double maxValue)
    {
        var raw = Environment.GetEnvironmentVariable(name);
        if (!double.TryParse(raw, out var parsed))
        {
            return defaultValue;
        }

        return Math.Clamp(parsed, minValue, maxValue);
    }

    private static double ComputeRampMae(Mat unwrapped)
    {
        var mae = 0.0;
        for (var y = 0; y < unwrapped.Rows; y++)
        {
            for (var x = 0; x < unwrapped.Cols; x++)
            {
                var expected = (x * 0.25) + (y * 0.18);
                mae += Math.Abs(unwrapped.At<float>(y, x) - expected);
            }
        }

        return mae / (unwrapped.Rows * unwrapped.Cols);
    }

    private static double ComputeRampMaeAllowingGlobalOffset(Mat unwrapped)
    {
        var globalOffset = unwrapped.At<float>(0, 0);
        var mae = 0.0;

        for (var y = 0; y < unwrapped.Rows; y++)
        {
            for (var x = 0; x < unwrapped.Cols; x++)
            {
                var expected = (x * 0.25) + (y * 0.18);
                mae += Math.Abs((unwrapped.At<float>(y, x) - globalOffset) - expected);
            }
        }

        return mae / (unwrapped.Rows * unwrapped.Cols);
    }

    private static int CountNonFinitePixels(Mat mat)
    {
        var count = 0;
        for (var y = 0; y < mat.Rows; y++)
        {
            for (var x = 0; x < mat.Cols; x++)
            {
                var value = mat.At<float>(y, x);
                if (float.IsNaN(value) || float.IsInfinity(value))
                {
                    count++;
                }
            }
        }

        return count;
    }

    private static float WrapPhase(double value)
    {
        return (float)Math.Atan2(Math.Sin(value), Math.Cos(value));
    }
}
