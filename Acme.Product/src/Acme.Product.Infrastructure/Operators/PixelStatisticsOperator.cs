// PixelStatisticsOperator.cs
// 像素统计算子
// 统计图像像素均值、方差与分位数指标
// 作者：蘅芜君
using Acme.Product.Core.Entities;
using Acme.Product.Core.Enums;
using Acme.Product.Core.Operators;
using Microsoft.Extensions.Logging;
using OpenCvSharp;

using Acme.Product.Core.Attributes;
namespace Acme.Product.Infrastructure.Operators;

[OperatorMeta(
    DisplayName = "像素统计",
    Description = "Computes ROI/masked pixel-level statistics.",
    Category = "检测",
    IconName = "pixel-stats",
    Keywords = new[] { "pixel statistics", "mean", "stddev", "min max", "non-zero" }
)]
[InputPort("Image", "Image", PortDataType.Image, IsRequired = true)]
[InputPort("Mask", "Mask", PortDataType.Image, IsRequired = false)]
[OutputPort("Mean", "Mean", PortDataType.Float)]
[OutputPort("StdDev", "StdDev", PortDataType.Float)]
[OutputPort("Min", "Min", PortDataType.Integer)]
[OutputPort("Max", "Max", PortDataType.Integer)]
[OutputPort("Median", "Median", PortDataType.Integer)]
[OutputPort("NonZeroCount", "NonZero Count", PortDataType.Integer)]
[OperatorParam("RoiX", "ROI X", "int", DefaultValue = 0, Min = 0)]
[OperatorParam("RoiY", "ROI Y", "int", DefaultValue = 0, Min = 0)]
[OperatorParam("RoiW", "ROI W", "int", DefaultValue = 0, Min = 0)]
[OperatorParam("RoiH", "ROI H", "int", DefaultValue = 0, Min = 0)]
[OperatorParam("Channel", "Channel", "enum", DefaultValue = "Gray", Options = new[] { "Gray|Gray", "R|R", "G|G", "B|B", "All|All" })]
public class PixelStatisticsOperator : OperatorBase
{
    public override OperatorType OperatorType => OperatorType.PixelStatistics;

    public PixelStatisticsOperator(ILogger<PixelStatisticsOperator> logger) : base(logger)
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

        var channel = GetStringParam(@operator, "Channel", "Gray");
        var roi = MeasurementRoiHelper.ResolveRoi(@operator, src.Width, src.Height);
        if (roi.Width <= 0 || roi.Height <= 0)
        {
            return Task.FromResult(OperatorExecutionOutput.Failure("ROI is invalid"));
        }

        using var roiMat = new Mat(src, roi);
        using var mask = ResolveMask(inputs, roi, src.Size(), out var maskError);
        if (maskError != null)
        {
            return Task.FromResult(OperatorExecutionOutput.Failure(maskError));
        }

        if (channel.Equals("All", StringComparison.OrdinalIgnoreCase) &&
            TryComputeAll8BitStatistics(roiMat, mask, out var aggregate8BitStats, out var per8BitChannelStats, out var channelNames))
        {
            var output = CreateStatisticsDictionary(aggregate8BitStats);
            output["SelectedChannel"] = channel;
            output["ChannelsAnalyzed"] = channelNames;
            output["AggregationMode"] = "FlattenedChannels";
            output["ChannelStats"] = per8BitChannelStats.ToDictionary(
                kvp => kvp.Key,
                kvp => (object)CreateStatisticsDictionary(kvp.Value),
                StringComparer.OrdinalIgnoreCase);
            output["StatusCode"] = "OK";
            output["StatusMessage"] = "Success";
            output["Confidence"] = MeasurementStatisticsHelper.ComputeConfidenceFromUncertainty(aggregate8BitStats.StdError);
            output["UncertaintyPx"] = aggregate8BitStats.StdError;

            return Task.FromResult(OperatorExecutionOutput.Success(output));
        }

        var analysisChannels = ResolveAnalysisChannels(roiMat, channel);
        try
        {
            var perChannelStats = new Dictionary<string, StatisticsSummary>(StringComparer.OrdinalIgnoreCase);
            var aggregateValues = analysisChannels.Count > 1 ? new List<double>() : null;

            foreach (var analysisChannel in analysisChannels)
            {
                if (aggregateValues == null)
                {
                    perChannelStats[analysisChannel.Name] = ComputeStatistics(analysisChannel.Data, mask);
                }
                else
                {
                    var values = ExtractValues(analysisChannel.Data, mask);
                    perChannelStats[analysisChannel.Name] = ComputeStatistics(values);
                    aggregateValues.AddRange(values);
                }
            }

            var aggregateStats = aggregateValues == null
                ? perChannelStats[analysisChannels[0].Name]
                : ComputeStatistics(aggregateValues);

            var output = CreateStatisticsDictionary(aggregateStats);
            output["SelectedChannel"] = channel;
            output["ChannelsAnalyzed"] = analysisChannels.Select(item => item.Name).ToArray();
            output["AggregationMode"] = aggregateValues == null ? "SingleChannel" : "FlattenedChannels";

            if (analysisChannels.Count > 1)
            {
                output["ChannelStats"] = perChannelStats.ToDictionary(
                    kvp => kvp.Key,
                    kvp => (object)CreateStatisticsDictionary(kvp.Value),
                    StringComparer.OrdinalIgnoreCase);
            }

            output["StatusCode"] = "OK";
            output["StatusMessage"] = "Success";
            output["Confidence"] = MeasurementStatisticsHelper.ComputeConfidenceFromUncertainty(aggregateStats.StdError);
            output["UncertaintyPx"] = aggregateStats.StdError;

            return Task.FromResult(OperatorExecutionOutput.Success(output));
        }
        finally
        {
            foreach (var analysisChannel in analysisChannels)
            {
                analysisChannel.Dispose();
            }
        }
    }

    public override ValidationResult ValidateParameters(Operator @operator)
    {
        var channel = GetStringParam(@operator, "Channel", "Gray");
        var validChannels = new[] { "Gray", "R", "G", "B", "All" };
        if (!validChannels.Contains(channel, StringComparer.OrdinalIgnoreCase))
        {
            return ValidationResult.Invalid("Channel must be Gray, R, G, B or All");
        }

        return ValidationResult.Valid();
    }

    private static List<AnalysisChannel> ResolveAnalysisChannels(Mat src, string channel)
    {
        if (channel.Equals("All", StringComparison.OrdinalIgnoreCase))
        {
            if (src.Channels() == 1)
            {
                return new List<AnalysisChannel> { new("Gray", CreateSharedImageHeader(src)) };
            }

            var channels = src.Split();
            var channelNames = new[] { "B", "G", "R", "A" };
            var results = new List<AnalysisChannel>(channels.Length);
            for (var i = 0; i < channels.Length; i++)
            {
                var name = i < channelNames.Length ? channelNames[i] : $"C{i}";
                results.Add(new AnalysisChannel(name, channels[i]));
            }

            return results;
        }

        if (channel.Equals("Gray", StringComparison.OrdinalIgnoreCase))
        {
            return new List<AnalysisChannel> { new("Gray", ExtractGray(src)) };
        }

        if (src.Channels() == 1)
        {
            return new List<AnalysisChannel> { new("Gray", CreateSharedImageHeader(src)) };
        }

        var selectedIndex = channel.ToUpperInvariant() switch
        {
            "R" => 2,
            "G" => 1,
            "B" => 0,
            _ => 0
        };

        var selected = new Mat();
        Cv2.ExtractChannel(src, selected, selectedIndex);
        return new List<AnalysisChannel> { new(channel.ToUpperInvariant(), selected) };
    }

    private static Mat CreateSharedImageHeader(Mat src)
    {
        return new Mat(src, new Rect(0, 0, src.Width, src.Height));
    }

    private static bool TryComputeAll8BitStatistics(
        Mat analysis,
        Mat mask,
        out StatisticsSummary aggregateStats,
        out Dictionary<string, StatisticsSummary> perChannelStats,
        out string[] channelNames)
    {
        aggregateStats = EmptyStatisticsSummary();
        perChannelStats = new Dictionary<string, StatisticsSummary>(StringComparer.OrdinalIgnoreCase);
        channelNames = Array.Empty<string>();

        var channelCount = analysis.Channels();
        if (analysis.Depth() != MatType.CV_8U || (channelCount != 3 && channelCount != 4))
        {
            return false;
        }

        channelNames = new[] { "B", "G", "R", "A" }.Take(channelCount).ToArray();
        var histograms = new int[channelCount][];
        var sampleCounts = new long[channelCount];
        var nonZeroCounts = new int[channelCount];
        var sums = new double[channelCount];
        var sumSquares = new double[channelCount];
        for (var i = 0; i < channelCount; i++)
        {
            histograms[i] = new int[256];
        }

        var aggregateHistogram = new int[256];
        var aggregateSampleCount = 0L;
        var aggregateNonZeroCount = 0;
        var aggregateSum = 0.0;
        var aggregateSumSquares = 0.0;
        var hasMask = !mask.Empty();
        var maskIndex = hasMask ? mask.GetGenericIndexer<byte>() : null;

        if (channelCount == 3)
        {
            var source = analysis.GetGenericIndexer<Vec3b>();
            for (var y = 0; y < analysis.Rows; y++)
            {
                for (var x = 0; x < analysis.Cols; x++)
                {
                    if (maskIndex != null && maskIndex[y, x] == 0)
                    {
                        continue;
                    }

                    var pixel = source[y, x];
                    Add8BitValue(pixel.Item0, histograms[0], ref sampleCounts[0], ref sums[0], ref sumSquares[0], ref nonZeroCounts[0]);
                    Add8BitValue(pixel.Item1, histograms[1], ref sampleCounts[1], ref sums[1], ref sumSquares[1], ref nonZeroCounts[1]);
                    Add8BitValue(pixel.Item2, histograms[2], ref sampleCounts[2], ref sums[2], ref sumSquares[2], ref nonZeroCounts[2]);
                    Add8BitValue(pixel.Item0, aggregateHistogram, ref aggregateSampleCount, ref aggregateSum, ref aggregateSumSquares, ref aggregateNonZeroCount);
                    Add8BitValue(pixel.Item1, aggregateHistogram, ref aggregateSampleCount, ref aggregateSum, ref aggregateSumSquares, ref aggregateNonZeroCount);
                    Add8BitValue(pixel.Item2, aggregateHistogram, ref aggregateSampleCount, ref aggregateSum, ref aggregateSumSquares, ref aggregateNonZeroCount);
                }
            }
        }
        else
        {
            var source = analysis.GetGenericIndexer<Vec4b>();
            for (var y = 0; y < analysis.Rows; y++)
            {
                for (var x = 0; x < analysis.Cols; x++)
                {
                    if (maskIndex != null && maskIndex[y, x] == 0)
                    {
                        continue;
                    }

                    var pixel = source[y, x];
                    Add8BitValue(pixel.Item0, histograms[0], ref sampleCounts[0], ref sums[0], ref sumSquares[0], ref nonZeroCounts[0]);
                    Add8BitValue(pixel.Item1, histograms[1], ref sampleCounts[1], ref sums[1], ref sumSquares[1], ref nonZeroCounts[1]);
                    Add8BitValue(pixel.Item2, histograms[2], ref sampleCounts[2], ref sums[2], ref sumSquares[2], ref nonZeroCounts[2]);
                    Add8BitValue(pixel.Item3, histograms[3], ref sampleCounts[3], ref sums[3], ref sumSquares[3], ref nonZeroCounts[3]);
                    Add8BitValue(pixel.Item0, aggregateHistogram, ref aggregateSampleCount, ref aggregateSum, ref aggregateSumSquares, ref aggregateNonZeroCount);
                    Add8BitValue(pixel.Item1, aggregateHistogram, ref aggregateSampleCount, ref aggregateSum, ref aggregateSumSquares, ref aggregateNonZeroCount);
                    Add8BitValue(pixel.Item2, aggregateHistogram, ref aggregateSampleCount, ref aggregateSum, ref aggregateSumSquares, ref aggregateNonZeroCount);
                    Add8BitValue(pixel.Item3, aggregateHistogram, ref aggregateSampleCount, ref aggregateSum, ref aggregateSumSquares, ref aggregateNonZeroCount);
                }
            }
        }

        for (var i = 0; i < channelCount; i++)
        {
            perChannelStats[channelNames[i]] = Create8BitStatisticsSummary(
                histograms[i],
                sampleCounts[i],
                sums[i],
                sumSquares[i],
                nonZeroCounts[i]);
        }

        aggregateStats = Create8BitStatisticsSummary(
            aggregateHistogram,
            aggregateSampleCount,
            aggregateSum,
            aggregateSumSquares,
            aggregateNonZeroCount);
        return true;
    }

    private static Mat ResolveMask(Dictionary<string, object>? inputs, Rect roi, Size sourceSize, out string? error)
    {
        error = null;
        if (inputs == null ||
            !ImageWrapper.TryGetFromInputs(inputs, "Mask", out var maskWrapper) ||
            maskWrapper == null)
        {
            return new Mat();
        }

        var maskSrc = maskWrapper.GetMat();
        if (maskSrc.Empty())
        {
            return new Mat();
        }

        var grayMask = new Mat();
        if (maskSrc.Channels() == 1)
        {
            maskSrc.CopyTo(grayMask);
        }
        else
        {
            Cv2.CvtColor(maskSrc, grayMask, ColorConversionCodes.BGR2GRAY);
        }

        Mat roiMask;
        if (grayMask.Size() == sourceSize)
        {
            if (roi.Right > grayMask.Width || roi.Bottom > grayMask.Height)
            {
                grayMask.Dispose();
                error = "Mask ROI exceeds mask image bounds";
                return new Mat();
            }

            roiMask = new Mat(grayMask, roi).Clone();
            grayMask.Dispose();
            grayMask = roiMask;
        }
        else if (grayMask.Size() != roi.Size)
        {
            grayMask.Dispose();
            error = "Mask must match the full image size or the resolved ROI size";
            return new Mat();
        }

        Cv2.Threshold(grayMask, grayMask, 1, 255, ThresholdTypes.Binary);
        return grayMask;
    }

    private static Mat ExtractGray(Mat src)
    {
        if (src.Channels() == 1)
        {
            return CreateSharedImageHeader(src);
        }

        var gray = new Mat();
        Cv2.CvtColor(src, gray, ColorConversionCodes.BGR2GRAY);
        return gray;
    }

    private static List<double> ExtractValues(Mat analysis, Mat mask)
    {
        var values = new List<double>();
        for (var y = 0; y < analysis.Rows; y++)
        {
            for (var x = 0; x < analysis.Cols; x++)
            {
                if (!mask.Empty() && mask.At<byte>(y, x) == 0)
                {
                    continue;
                }

                values.Add(ReadScalarValue(analysis, x, y));
            }
        }

        return values;
    }

    private static double ReadScalarValue(Mat mat, int x, int y)
    {
        return mat.Depth() switch
        {
            MatType.CV_8U => mat.At<byte>(y, x),
            MatType.CV_8S => mat.At<sbyte>(y, x),
            MatType.CV_16U => mat.At<ushort>(y, x),
            MatType.CV_16S => mat.At<short>(y, x),
            MatType.CV_32S => mat.At<int>(y, x),
            MatType.CV_32F => mat.At<float>(y, x),
            MatType.CV_64F => mat.At<double>(y, x),
            _ => throw new NotSupportedException($"Unsupported image depth for pixel statistics: {mat.Depth()}.")
        };
    }

    private static StatisticsSummary ComputeStatistics(List<double> values)
    {
        if (values.Count == 0)
        {
            return EmptyStatisticsSummary();
        }

        var min = double.PositiveInfinity;
        var max = double.NegativeInfinity;
        var sum = 0.0;
        var sumSquares = 0.0;
        var nonZeroCount = 0;

        foreach (var value in values)
        {
            min = Math.Min(min, value);
            max = Math.Max(max, value);
            sum += value;
            sumSquares += value * value;
            if (value != 0.0)
            {
                nonZeroCount++;
            }
        }

        var mean = sum / values.Count;
        var variance = Math.Max(0.0, (sumSquares / values.Count) - (mean * mean));
        var stdDev = Math.Sqrt(variance);
        var median = MeasurementStatisticsHelper.ComputeMedian(values);
        var medianAbsoluteDeviation = MeasurementStatisticsHelper.ComputeMedianAbsoluteDeviation(values, median);
        var stdError = MeasurementStatisticsHelper.ComputeStandardError(stdDev, values.Count);

        return new StatisticsSummary(
            mean,
            stdDev,
            min,
            max,
            median,
            max - min,
            medianAbsoluteDeviation,
            stdError,
            nonZeroCount,
            values.Count);
    }

    private static StatisticsSummary ComputeStatistics(Mat analysis, Mat mask)
    {
        if (analysis.Depth() == MatType.CV_8U && analysis.Channels() == 1)
        {
            return Compute8BitStatistics(analysis, mask);
        }

        return ComputeStatistics(ExtractValues(analysis, mask));
    }

    private static StatisticsSummary Compute8BitStatistics(Mat analysis, Mat mask)
    {
        var hasMask = !mask.Empty();
        var source = analysis.GetGenericIndexer<byte>();
        var maskIndex = hasMask ? mask.GetGenericIndexer<byte>() : null;
        var histogram = new int[256];
        long sampleCount = 0;
        var nonZeroCount = 0;
        var sum = 0.0;
        var sumSquares = 0.0;

        for (var y = 0; y < analysis.Rows; y++)
        {
            for (var x = 0; x < analysis.Cols; x++)
            {
                if (maskIndex != null && maskIndex[y, x] == 0)
                {
                    continue;
                }

                var value = source[y, x];
                histogram[value]++;
                sampleCount++;
                sum += value;
                sumSquares += value * value;
                if (value != 0)
                {
                    nonZeroCount++;
                }
            }
        }

        return Create8BitStatisticsSummary(histogram, sampleCount, sum, sumSquares, nonZeroCount);
    }

    private static void Add8BitValue(
        byte value,
        int[] histogram,
        ref long sampleCount,
        ref double sum,
        ref double sumSquares,
        ref int nonZeroCount)
    {
        histogram[value]++;
        sampleCount++;
        sum += value;
        sumSquares += value * value;
        if (value != 0)
        {
            nonZeroCount++;
        }
    }

    private static StatisticsSummary Create8BitStatisticsSummary(
        IReadOnlyList<int> histogram,
        long sampleCount,
        double sum,
        double sumSquares,
        int nonZeroCount)
    {
        if (sampleCount == 0)
        {
            return EmptyStatisticsSummary();
        }

        var min = FirstHistogramValue(histogram);
        var max = LastHistogramValue(histogram);
        var mean = sum / sampleCount;
        var variance = Math.Max(0.0, (sumSquares / sampleCount) - (mean * mean));
        var stdDev = Math.Sqrt(variance);
        var median = MedianFromHistogram(histogram, sampleCount);
        var medianAbsoluteDeviation = MedianAbsoluteDeviationFromHistogram(histogram, sampleCount, median);
        var stdError = MeasurementStatisticsHelper.ComputeStandardError(stdDev, (int)sampleCount);

        return new StatisticsSummary(
            mean,
            stdDev,
            min,
            max,
            median,
            max - min,
            medianAbsoluteDeviation,
            stdError,
            nonZeroCount,
            (int)sampleCount);
    }

    private static StatisticsSummary EmptyStatisticsSummary()
    {
        return new StatisticsSummary(0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0, 0);
    }

    private static int FirstHistogramValue(IReadOnlyList<int> histogram)
    {
        for (var i = 0; i < histogram.Count; i++)
        {
            if (histogram[i] > 0)
            {
                return i;
            }
        }

        return 0;
    }

    private static int LastHistogramValue(IReadOnlyList<int> histogram)
    {
        for (var i = histogram.Count - 1; i >= 0; i--)
        {
            if (histogram[i] > 0)
            {
                return i;
            }
        }

        return 0;
    }

    private static double MedianFromHistogram(IReadOnlyList<int> histogram, long sampleCount)
    {
        var lowerTarget = (sampleCount - 1) / 2;
        var upperTarget = sampleCount / 2;
        var seen = 0L;
        int? lower = null;
        int? upper = null;

        for (var i = 0; i < histogram.Count; i++)
        {
            seen += histogram[i];
            if (lower == null && seen > lowerTarget)
            {
                lower = i;
            }

            if (seen > upperTarget)
            {
                upper = i;
                break;
            }
        }

        return ((lower ?? 0) + (upper ?? lower ?? 0)) * 0.5;
    }

    private static double MedianAbsoluteDeviationFromHistogram(IReadOnlyList<int> histogram, long sampleCount, double median)
    {
        var deviationHistogram = new int[511];
        for (var value = 0; value < histogram.Count; value++)
        {
            var count = histogram[value];
            if (count == 0)
            {
                continue;
            }

            var deviationKey = (int)Math.Round(Math.Abs(value - median) * 2.0, MidpointRounding.AwayFromZero);
            deviationHistogram[deviationKey] += count;
        }

        return MedianFromHistogram(deviationHistogram, sampleCount) * 0.5;
    }

    private static Dictionary<string, object> CreateStatisticsDictionary(StatisticsSummary stats)
    {
        return new Dictionary<string, object>
        {
            { "Mean", stats.Mean },
            { "StdDev", stats.StdDev },
            { "Min", stats.Min },
            { "Max", stats.Max },
            { "Median", stats.Median },
            { "Range", stats.Range },
            { "MedianAbsoluteDeviation", stats.MedianAbsoluteDeviation },
            { "StdError", stats.StdError },
            { "NonZeroCount", stats.NonZeroCount },
            { "SampleCount", stats.SampleCount }
        };
    }

    private sealed record AnalysisChannel(string Name, Mat Data) : IDisposable
    {
        public void Dispose()
        {
            Data.Dispose();
        }
    }

    private sealed record StatisticsSummary(
        double Mean,
        double StdDev,
        double Min,
        double Max,
        double Median,
        double Range,
        double MedianAbsoluteDeviation,
        double StdError,
        int NonZeroCount,
        int SampleCount);
}
