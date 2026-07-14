// PixelStatisticsOperator.cs
// 像素统计算子
// 统计图像像素均值、方差与分位数指标
// 作者：蘅芜君
using System.Buffers;
using System.Numerics;
using ClearVision.Product.Core.Attributes;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Operators;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
namespace ClearVision.Product.Infrastructure.Operators;

[OperatorMeta(
    DisplayName = "像素统计",
    Description = "计算 ROI 或掩码区域内的像素级统计信息。",
    CategoryId = OperatorCategoryId.FeatureExtraction,
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
            output["ChannelStats"] = CreateChannelStatsDictionary(per8BitChannelStats);
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
            StatisticsSummary aggregateStats;

            if (analysisChannels.Count == 1)
            {
                aggregateStats = ComputeStatistics(analysisChannels[0].Data, mask);
                perChannelStats[analysisChannels[0].Name] = aggregateStats;
            }
            else
            {
                aggregateStats = ComputeFlattenedStatistics(analysisChannels, mask, perChannelStats);
            }

            var output = CreateStatisticsDictionary(aggregateStats);
            output["SelectedChannel"] = channel;
            output["ChannelsAnalyzed"] = CreateChannelNameArray(analysisChannels);
            output["AggregationMode"] = analysisChannels.Count == 1 ? "SingleChannel" : "FlattenedChannels";

            if (analysisChannels.Count > 1)
            {
                output["ChannelStats"] = CreateChannelStatsDictionary(perChannelStats);
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
        if (!channel.Equals("Gray", StringComparison.OrdinalIgnoreCase) &&
            !channel.Equals("R", StringComparison.OrdinalIgnoreCase) &&
            !channel.Equals("G", StringComparison.OrdinalIgnoreCase) &&
            !channel.Equals("B", StringComparison.OrdinalIgnoreCase) &&
            !channel.Equals("All", StringComparison.OrdinalIgnoreCase))
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

        channelNames = CreateChannelNames(channelCount);
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

    private static StatisticsSummary ComputeFlattenedStatistics(
        IReadOnlyList<AnalysisChannel> analysisChannels,
        Mat mask,
        Dictionary<string, StatisticsSummary> perChannelStats)
    {
        var maxChannelSampleCount = checked(analysisChannels[0].Data.Rows * analysisChannels[0].Data.Cols);
        var aggregateCapacity = checked(maxChannelSampleCount * analysisChannels.Count);
        var aggregateValues = ArrayPool<double>.Shared.Rent(aggregateCapacity);
        var channelValues = ArrayPool<double>.Shared.Rent(maxChannelSampleCount);
        var aggregateCount = 0;

        try
        {
            foreach (var analysisChannel in analysisChannels)
            {
                var channelCount = CopyScalarValues(analysisChannel.Data, mask, channelValues);
                Array.Copy(channelValues, 0, aggregateValues, aggregateCount, channelCount);
                aggregateCount += channelCount;
                perChannelStats[analysisChannel.Name] = ComputeStatistics(channelValues, channelCount);
            }

            return ComputeStatistics(aggregateValues, aggregateCount);
        }
        finally
        {
            ArrayPool<double>.Shared.Return(channelValues);
            ArrayPool<double>.Shared.Return(aggregateValues);
        }
    }

    private static int CopyScalarValues(Mat analysis, Mat mask, double[] destination)
    {
        var hasMask = !mask.Empty();
        var maskIndex = hasMask ? mask.GetGenericIndexer<byte>() : null;

        return analysis.Depth() switch
        {
            MatType.CV_8U => CopyScalarValues<byte>(analysis, maskIndex, destination),
            MatType.CV_8S => CopyScalarValues<sbyte>(analysis, maskIndex, destination),
            MatType.CV_16U => CopyScalarValues<ushort>(analysis, maskIndex, destination),
            MatType.CV_16S => CopyScalarValues<short>(analysis, maskIndex, destination),
            MatType.CV_32S => CopyScalarValues<int>(analysis, maskIndex, destination),
            MatType.CV_32F => CopyScalarValues<float>(analysis, maskIndex, destination),
            MatType.CV_64F => CopyScalarValues<double>(analysis, maskIndex, destination),
            _ => throw new NotSupportedException($"Unsupported image depth for pixel statistics: {analysis.Depth()}.")
        };
    }

    private static int CopyScalarValues<T>(Mat analysis, MatIndexer<byte>? maskIndex, double[] destination)
        where T : unmanaged, INumberBase<T>
    {
        var source = analysis.GetGenericIndexer<T>();
        var count = 0;

        if (maskIndex == null)
        {
            for (var y = 0; y < analysis.Rows; y++)
            {
                for (var x = 0; x < analysis.Cols; x++)
                {
                    destination[count++] = double.CreateChecked(source[y, x]);
                }
            }

            return count;
        }

        for (var y = 0; y < analysis.Rows; y++)
        {
            for (var x = 0; x < analysis.Cols; x++)
            {
                if (maskIndex[y, x] == 0)
                {
                    continue;
                }

                destination[count++] = double.CreateChecked(source[y, x]);
            }
        }

        return count;
    }

    private static StatisticsSummary ComputeStatistics(double[] values, int count)
    {
        if (count == 0)
        {
            return EmptyStatisticsSummary();
        }

        var min = double.PositiveInfinity;
        var max = double.NegativeInfinity;
        var sum = 0.0;
        var sumSquares = 0.0;
        var nonZeroCount = 0;

        for (var i = 0; i < count; i++)
        {
            var value = values[i];
            min = Math.Min(min, value);
            max = Math.Max(max, value);
            sum += value;
            sumSquares += value * value;
            if (value != 0.0)
            {
                nonZeroCount++;
            }
        }

        var mean = sum / count;
        var variance = Math.Max(0.0, (sumSquares / count) - (mean * mean));
        var stdDev = Math.Sqrt(variance);
        var median = ComputeMedianInPlace(values, count);
        for (var i = 0; i < count; i++)
        {
            values[i] = Math.Abs(values[i] - median);
        }

        var medianAbsoluteDeviation = ComputeMedianInPlace(values, count);
        var stdError = MeasurementStatisticsHelper.ComputeStandardError(stdDev, count);

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
            count);
    }

    private static StatisticsSummary ComputeStatistics(Mat analysis, Mat mask)
    {
        if (analysis.Depth() == MatType.CV_8U && analysis.Channels() == 1)
        {
            return Compute8BitStatistics(analysis, mask);
        }

        var maxSampleCount = checked(analysis.Rows * analysis.Cols);
        var values = ArrayPool<double>.Shared.Rent(maxSampleCount);
        try
        {
            var count = CopyScalarValues(analysis, mask, values);
            return ComputeStatistics(values, count);
        }
        finally
        {
            ArrayPool<double>.Shared.Return(values);
        }
    }

    private static double ComputeMedianInPlace(double[] values, int count)
    {
        if (count == 0)
        {
            return 0.0;
        }

        var lowerIndex = (count - 1) / 2;
        var upperIndex = count / 2;
        var lower = SelectKth(values, count, lowerIndex);
        if (lowerIndex == upperIndex)
        {
            return lower;
        }

        var upper = SelectKth(values, count, upperIndex);
        return (lower + upper) * 0.5;
    }

    private static double SelectKth(double[] values, int count, int k)
    {
        var left = 0;
        var right = count - 1;

        while (true)
        {
            if (left == right)
            {
                return values[left];
            }

            var pivot = values[left + ((right - left) / 2)];
            var (equalStart, equalEnd) = Partition(values, left, right, pivot);
            if (k < equalStart)
            {
                right = equalStart - 1;
            }
            else if (k > equalEnd)
            {
                left = equalEnd + 1;
            }
            else
            {
                return values[k];
            }
        }
    }

    private static (int EqualStart, int EqualEnd) Partition(double[] values, int left, int right, double pivot)
    {
        var equalStart = left;
        var current = left;
        var equalEnd = right;

        while (current <= equalEnd)
        {
            var comparison = values[current].CompareTo(pivot);
            if (comparison < 0)
            {
                Swap(values, equalStart, current);
                equalStart++;
                current++;
            }
            else if (comparison > 0)
            {
                Swap(values, current, equalEnd);
                equalEnd--;
            }
            else
            {
                current++;
            }
        }

        return (equalStart, equalEnd);
    }

    private static void Swap(double[] values, int left, int right)
    {
        if (left == right)
        {
            return;
        }

        (values[left], values[right]) = (values[right], values[left]);
    }

    private static StatisticsSummary Compute8BitStatistics(Mat analysis, Mat mask)
    {
        var hasMask = !mask.Empty();
        var histogram = new int[256];
        long sampleCount = 0;
        var nonZeroCount = 0;
        var sum = 0.0;
        var sumSquares = 0.0;

        unsafe
        {
            var sourceBase = (byte*)analysis.DataPointer;
            var sourceStep = (int)analysis.Step();

            if (!hasMask)
            {
                for (var y = 0; y < analysis.Rows; y++)
                {
                    var sourceRow = sourceBase + y * sourceStep;
                    for (var x = 0; x < analysis.Cols; x++)
                    {
                        Add8BitValue(sourceRow[x], histogram, ref sampleCount, ref sum, ref sumSquares, ref nonZeroCount);
                    }
                }

                return Create8BitStatisticsSummary(histogram, sampleCount, sum, sumSquares, nonZeroCount);
            }

            var maskBase = (byte*)mask.DataPointer;
            var maskStep = (int)mask.Step();
            for (var y = 0; y < analysis.Rows; y++)
            {
                var sourceRow = sourceBase + y * sourceStep;
                var maskRow = maskBase + y * maskStep;
                for (var x = 0; x < analysis.Cols; x++)
                {
                    if (maskRow[x] == 0)
                    {
                        continue;
                    }

                    Add8BitValue(sourceRow[x], histogram, ref sampleCount, ref sum, ref sumSquares, ref nonZeroCount);
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

    private static Dictionary<string, object> CreateChannelStatsDictionary(Dictionary<string, StatisticsSummary> stats)
    {
        var result = new Dictionary<string, object>(stats.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var (name, summary) in stats)
        {
            result[name] = CreateStatisticsDictionary(summary);
        }

        return result;
    }

    private static string[] CreateChannelNameArray(IReadOnlyList<AnalysisChannel> channels)
    {
        var names = new string[channels.Count];
        for (var i = 0; i < channels.Count; i++)
        {
            names[i] = channels[i].Name;
        }

        return names;
    }

    private static string[] CreateChannelNames(int channelCount)
    {
        var names = new string[channelCount];
        for (var i = 0; i < channelCount; i++)
        {
            names[i] = i switch
            {
                0 => "B",
                1 => "G",
                2 => "R",
                3 => "A",
                _ => $"C{i}"
            };
        }

        return names;
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
