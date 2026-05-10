using System.Collections;
using Acme.Product.Core.Attributes;
using Acme.Product.Core.Entities;
using Acme.Product.Core.Enums;
using Acme.Product.Core.Operators;
using Acme.Product.Infrastructure.ImageProcessing;
using Acme.Product.Infrastructure.Memory;
using Microsoft.Extensions.Logging;
using OpenCvSharp;

namespace Acme.Product.Infrastructure.Operators;

[OperatorMeta(
    DisplayName = "颜色测量",
    Description = "Measures Lab delta-E or HSV statistics over a selected ROI.",
    Category = "颜色处理",
    IconName = "color-measure",
    Keywords = new[] { "color", "deltaE", "lab", "hsv" },
    Version = "2.0.0"
)]
[InputPort("Image", "Image", PortDataType.Image, IsRequired = true)]
[InputPort("ReferenceColor", "Reference Color", PortDataType.Any, IsRequired = false)]
[OutputPort("LabMean", "Lab Mean", PortDataType.Any)]
[OutputPort("ReferenceLab", "Reference Lab", PortDataType.Any)]
[OutputPort("DeltaE", "DeltaE", PortDataType.Float)]
[OutputPort("HueMean", "Hue Mean", PortDataType.Float)]
[OutputPort("SaturationMean", "Saturation Mean", PortDataType.Float)]
[OutputPort("ValueMean", "Value Mean", PortDataType.Float)]
[OutputPort("HueValid", "Hue Valid", PortDataType.Boolean)]
[OutputPort("Image", "Image", PortDataType.Image)]
[OperatorParam("MeasurementMode", "Measurement Mode", "enum", DefaultValue = "LabDeltaE", Options = new[] { "LabDeltaE|Lab DeltaE", "HsvStats|HSV Stats" })]
[OperatorParam("DeltaEMethod", "DeltaE Method", "enum", DefaultValue = "CIEDE2000", Options = new[] { "CIE76|CIE76", "CIEDE2000|CIEDE2000" })]
[OperatorParam("RoiX", "ROI X", "int", DefaultValue = 0)]
[OperatorParam("RoiY", "ROI Y", "int", DefaultValue = 0)]
[OperatorParam("RoiW", "ROI W", "int", DefaultValue = 0)]
[OperatorParam("RoiH", "ROI H", "int", DefaultValue = 0)]
[OperatorParam("RefL", "Ref L", "double", DefaultValue = 0.0)]
[OperatorParam("RefA", "Ref A", "double", DefaultValue = 0.0)]
[OperatorParam("RefB", "Ref B", "double", DefaultValue = 0.0)]
public class ColorMeasurementOperator : OperatorBase
{
    private const double MinHueSaturation = 12.0;
    private const double MinHueValue = 12.0;
    private const int MaxDeltaEStatisticsSamples = 4096;
    private static readonly MatPool PassthroughImagePool = new(maxPerBucket: 0, maxTotalGb: 0.0);
    private static readonly double[] HueSinLookup = CreateHueLookup(Math.Sin);
    private static readonly double[] HueCosLookup = CreateHueLookup(Math.Cos);

    public override OperatorType OperatorType => OperatorType.ColorMeasurement;

    public ColorMeasurementOperator(ILogger<ColorMeasurementOperator> logger) : base(logger)
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

        var measurementMode = ResolveMeasurementMode(@operator);
        if (measurementMode == null)
        {
            return Task.FromResult(OperatorExecutionOutput.Failure("MeasurementMode must be LabDeltaE or HsvStats"));
        }

        var roi = MeasurementRoiHelper.ResolveRoi(@operator, src.Width, src.Height);
        if (roi.Width <= 0 || roi.Height <= 0)
        {
            return Task.FromResult(OperatorExecutionOutput.Failure("ROI is invalid"));
        }

        using var roiSource = new Mat(src, roi);
        using var roiMat = EnsureColorImage(roiSource);

        Dictionary<string, object> output;
        if (measurementMode == "LabDeltaE")
        {
            output = MeasureLabDeltaE(@operator, inputs, roiMat);
        }
        else
        {
            output = MeasureHsvStats(roiMat);
        }

        output["MeasurementMode"] = measurementMode;
        output["StatusCode"] = "OK";
        output["StatusMessage"] = "Success";
        var measurementUncertainty = TryReadMeasurementUncertainty(output);
        output["Confidence"] = MeasurementStatisticsHelper.ComputeConfidenceFromUncertainty(measurementUncertainty);
        output["UncertaintyPx"] = measurementUncertainty;

        return Task.FromResult(OperatorExecutionOutput.Success(CreateSharedImageOutput(src, output)));
    }

    public override ValidationResult ValidateParameters(Operator @operator)
    {
        if (ResolveMeasurementMode(@operator) == null)
        {
            return ValidationResult.Invalid("MeasurementMode must be LabDeltaE or HsvStats");
        }

        var deltaEMethod = GetStringParam(@operator, "DeltaEMethod", "CIEDE2000");
        var validMethods = new[] { "CIE76", "CIEDE2000" };
        if (!validMethods.Contains(deltaEMethod, StringComparer.OrdinalIgnoreCase))
        {
            return ValidationResult.Invalid("DeltaEMethod must be CIE76 or CIEDE2000");
        }

        var roiW = MeasurementRoiHelper.ReadIntParameter(@operator, "RoiW", 0);
        var roiH = MeasurementRoiHelper.ReadIntParameter(@operator, "RoiH", 0);
        if (roiW < 0 || roiH < 0)
        {
            return ValidationResult.Invalid("RoiW/RoiH must be >= 0");
        }

        return ValidationResult.Valid();
    }

    private static string? ResolveMeasurementMode(Operator @operator)
    {
        var measurementMode = GetOptionalParameter(@operator, "MeasurementMode");
        if (measurementMode != null)
        {
            return measurementMode switch
            {
                "LabDeltaE" => "LabDeltaE",
                "HsvStats" => "HsvStats",
                _ => null
            };
        }

        // Read-only migration path for historical flows.
        var legacyColorSpace = GetOptionalParameter(@operator, "ColorSpace");
        return legacyColorSpace switch
        {
            null => "LabDeltaE",
            "Lab" => "LabDeltaE",
            "HSV" => "HsvStats",
            _ => null
        };
    }

    private static string? GetOptionalParameter(Operator @operator, string name)
    {
        var raw = @operator.Parameters.FirstOrDefault(parameter => parameter.Name.Equals(name, StringComparison.OrdinalIgnoreCase))?.Value?.ToString();
        return raw?.Trim();
    }

    private static Mat EnsureColorImage(Mat src)
    {
        if (src.Channels() == 3)
        {
            return CreateSharedImageHeader(src);
        }

        var color = new Mat();
        var conversion = src.Channels() == 4
            ? ColorConversionCodes.BGRA2BGR
            : ColorConversionCodes.GRAY2BGR;
        Cv2.CvtColor(src, color, conversion);
        return color;
    }

    private static Mat CreateSharedImageHeader(Mat src)
    {
        return new Mat(src, new Rect(0, 0, src.Width, src.Height));
    }

    private static Dictionary<string, object> CreateSharedImageOutput(Mat src, Dictionary<string, object> additionalData)
    {
        var output = new Dictionary<string, object>
        {
            { "Image", new ImageWrapper(CreateSharedImageHeader(src), PassthroughImagePool) },
            { "Width", src.Width },
            { "Height", src.Height }
        };

        foreach (var kvp in additionalData)
        {
            if (!output.ContainsKey(kvp.Key))
            {
                output[kvp.Key] = kvp.Value;
            }
        }

        return output;
    }

    private Dictionary<string, object> MeasureLabDeltaE(Operator @operator, Dictionary<string, object>? inputs, Mat roiMat)
    {
        var labStats = ComputeLabStatistics(roiMat);
        var lValue = labStats.Mean.L;
        var aValue = labStats.Mean.A;
        var bValue = labStats.Mean.B;

        var refL = GetDoubleParam(@operator, "RefL", lValue);
        var refA = GetDoubleParam(@operator, "RefA", aValue);
        var refB = GetDoubleParam(@operator, "RefB", bValue);
        if (inputs != null && inputs.TryGetValue("ReferenceColor", out var referenceObj))
        {
            TryOverrideReferenceLab(referenceObj, ref refL, ref refA, ref refB);
        }

        var labValue = new CieLab(lValue, aValue, bValue);
        var referenceValue = new CieLab(refL, refA, refB);
        var deltaEMethod = GetStringParam(@operator, "DeltaEMethod", "CIEDE2000");
        var deltaE = deltaEMethod.Equals("CIE76", StringComparison.OrdinalIgnoreCase)
            ? ColorDifference.DeltaE76(labValue, referenceValue)
            : ColorDifference.DeltaE00(labValue, referenceValue);
        var deltaEStats = ComputeDeltaEStatistics(roiMat, referenceValue, deltaEMethod);
        var deltaEStdDev = deltaEStats.SampleCount > 0 ? deltaEStats.StdDev : 0.0;
        var deltaEStdError = deltaEStats.SampleCount > 0 ? deltaEStats.StdError : 0.0;

        return new Dictionary<string, object>
        {
            { "LabMean", new Dictionary<string, object> { { "L", lValue }, { "A", aValue }, { "B", bValue } } },
            { "LabStdDev", new Dictionary<string, object> { { "L", labStats.StdDev.L }, { "A", labStats.StdDev.A }, { "B", labStats.StdDev.B } } },
            { "ReferenceLab", new Dictionary<string, object> { { "L", refL }, { "A", refA }, { "B", refB } } },
            { "DeltaE", deltaE },
            { "DeltaEStdDev", deltaEStdDev },
            { "DeltaEStdError", deltaEStdError },
            { "SampleCount", labStats.SampleCount },
            { "DeltaESampleCount", deltaEStats.SampleCount },
            { "HueMean", double.NaN },
            { "SaturationMean", double.NaN },
            { "ValueMean", double.NaN },
            { "HueValid", false },
            { "MeasurementUncertainty", deltaEStdError }
        };
    }

    private static Dictionary<string, object> MeasureHsvStats(Mat roiMat)
    {
        using var hsv = new Mat();
        Cv2.CvtColor(roiMat, hsv, ColorConversionCodes.BGR2HSV);

        var indexer = hsv.GetGenericIndexer<Vec3b>();
        var totalPixels = hsv.Rows * hsv.Cols;
        var saturationSum = 0.0;
        var valueSum = 0.0;
        var sinSum = 0.0;
        var cosSum = 0.0;
        var validHueCount = 0;

        for (var y = 0; y < hsv.Rows; y++)
        {
            for (var x = 0; x < hsv.Cols; x++)
            {
                var pixel = indexer[y, x];
                saturationSum += pixel.Item1;
                valueSum += pixel.Item2;

                if (pixel.Item1 < MinHueSaturation || pixel.Item2 < MinHueValue)
                {
                    continue;
                }

                sinSum += HueSinLookup[pixel.Item0];
                cosSum += HueCosLookup[pixel.Item0];
                validHueCount++;
            }
        }

        var saturationMean = totalPixels > 0 ? saturationSum / totalPixels * (100.0 / 255.0) : 0.0;
        var valueMean = totalPixels > 0 ? valueSum / totalPixels * (100.0 / 255.0) : 0.0;

        if (validHueCount == 0)
        {
            return new Dictionary<string, object>
            {
                { "LabMean", new Dictionary<string, object>() },
                { "ReferenceLab", new Dictionary<string, object>() },
                { "DeltaE", double.NaN },
                { "HueMean", double.NaN },
                { "HueCircularStdDeg", double.NaN },
                { "HueStdErrorDeg", double.NaN },
                { "SaturationMean", saturationMean },
                { "ValueMean", valueMean },
                { "HueValid", false },
                { "SampleCount", 0 },
                { "MeasurementUncertainty", double.NaN }
            };
        }

        var sinMean = sinSum / validHueCount;
        var cosMean = cosSum / validHueCount;
        var meanAngle = Math.Atan2(sinMean, cosMean);
        if (meanAngle < 0.0)
        {
            meanAngle += 2.0 * Math.PI;
        }

        var meanResultantLength = Math.Sqrt((sinMean * sinMean) + (cosMean * cosMean));
        meanResultantLength = Math.Clamp(meanResultantLength, 1e-12, 1.0);
        var hueMean = meanAngle * 180.0 / Math.PI;
        var hueStdDev = Math.Sqrt(Math.Max(0.0, -2.0 * Math.Log(meanResultantLength))) * 180.0 / Math.PI;
        var hueStdError = MeasurementStatisticsHelper.ComputeStandardError(hueStdDev, validHueCount);

        return new Dictionary<string, object>
        {
            { "LabMean", new Dictionary<string, object>() },
            { "ReferenceLab", new Dictionary<string, object>() },
            { "DeltaE", double.NaN },
            { "HueMean", hueMean },
            { "HueCircularStdDeg", hueStdDev },
            { "HueStdErrorDeg", hueStdError },
            { "SaturationMean", saturationMean },
            { "ValueMean", valueMean },
            { "HueValid", true },
            { "SampleCount", validHueCount },
            { "MeasurementUncertainty", hueStdError }
        };
    }

    private static void TryOverrideReferenceLab(object? referenceObj, ref double refL, ref double refA, ref double refB)
    {
        if (referenceObj == null)
        {
            return;
        }

        if (referenceObj is double[] doubles && doubles.Length >= 3)
        {
            refL = doubles[0];
            refA = doubles[1];
            refB = doubles[2];
            return;
        }

        if (referenceObj is float[] floats && floats.Length >= 3)
        {
            refL = floats[0];
            refA = floats[1];
            refB = floats[2];
            return;
        }

        if (referenceObj is IDictionary<string, object> dict)
        {
            if (TryGetDouble(dict, "L", out var l))
            {
                refL = l;
            }

            if (TryGetDouble(dict, "A", out var a))
            {
                refA = a;
            }

            if (TryGetDouble(dict, "B", out var b))
            {
                refB = b;
            }
            return;
        }

        if (referenceObj is IDictionary legacy)
        {
            var normalized = legacy.Cast<DictionaryEntry>()
                .Where(entry => entry.Key != null)
                .ToDictionary(entry => entry.Key!.ToString() ?? string.Empty, entry => entry.Value ?? 0.0, StringComparer.OrdinalIgnoreCase);
            TryOverrideReferenceLab(normalized, ref refL, ref refA, ref refB);
        }
    }

    private static bool TryGetDouble(IDictionary<string, object> dict, string key, out double value)
    {
        value = 0;
        if (!dict.TryGetValue(key, out var raw) || raw == null)
        {
            return false;
        }

        return raw switch
        {
            double d => (value = d) == d,
            float f => (value = f) == f,
            int i => (value = i) == i,
            long l => (value = l) == l,
            _ => double.TryParse(raw.ToString(), out value)
        };
    }

    private static double TryReadMeasurementUncertainty(IReadOnlyDictionary<string, object> output)
    {
        if (output.TryGetValue("MeasurementUncertainty", out var raw) &&
            raw != null &&
            double.TryParse(raw.ToString(), out var parsed) &&
            double.IsFinite(parsed))
        {
            return parsed;
        }

        return double.NaN;
    }

    private static double[] CreateHueLookup(Func<double, double> projection)
    {
        var values = new double[180];
        for (var hue = 0; hue < values.Length; hue++)
        {
            values[hue] = projection(hue * (Math.PI / 90.0));
        }

        return values;
    }

    private static LabStatistics ComputeLabStatistics(Mat roiMat)
    {
        var indexer = roiMat.GetGenericIndexer<Vec3b>();
        var labCache = new Dictionary<int, CieLab>(capacity: 256);
        var count = 0;
        var sumL = 0.0;
        var sumA = 0.0;
        var sumB = 0.0;
        var sumL2 = 0.0;
        var sumA2 = 0.0;
        var sumB2 = 0.0;

        for (var y = 0; y < roiMat.Rows; y++)
        {
            for (var x = 0; x < roiMat.Cols; x++)
            {
                var pixel = indexer[y, x];
                var lab = GetCachedLab(pixel, labCache);
                count++;
                sumL += lab.L;
                sumA += lab.A;
                sumB += lab.B;
                sumL2 += lab.L * lab.L;
                sumA2 += lab.A * lab.A;
                sumB2 += lab.B * lab.B;
            }
        }

        if (count == 0)
        {
            return new LabStatistics(new CieLab(0.0, 0.0, 0.0), new CieLab(0.0, 0.0, 0.0), 0);
        }

        var mean = new CieLab(
            sumL / count,
            sumA / count,
            sumB / count);
        var stdDev = new CieLab(
            Math.Sqrt(Math.Max(0.0, (sumL2 / count) - (mean.L * mean.L))),
            Math.Sqrt(Math.Max(0.0, (sumA2 / count) - (mean.A * mean.A))),
            Math.Sqrt(Math.Max(0.0, (sumB2 / count) - (mean.B * mean.B))));

        return new LabStatistics(mean, stdDev, count);
    }

    private static DeltaEStatistics ComputeDeltaEStatistics(Mat roiMat, CieLab reference, string deltaEMethod)
    {
        var totalPixels = roiMat.Rows * roiMat.Cols;
        var stride = Math.Max(1, totalPixels / MaxDeltaEStatisticsSamples);
        var indexer = roiMat.GetGenericIndexer<Vec3b>();
        var labCache = new Dictionary<int, CieLab>(capacity: Math.Min(MaxDeltaEStatisticsSamples, 256));
        var useCie76 = deltaEMethod.Equals("CIE76", StringComparison.OrdinalIgnoreCase);
        var ordinal = 0;
        var nextSampleOrdinal = 0;
        var sampleCount = 0;
        var mean = 0.0;
        var m2 = 0.0;

        for (var y = 0; y < roiMat.Rows; y++)
        {
            for (var x = 0; x < roiMat.Cols; x++)
            {
                if (ordinal++ != nextSampleOrdinal)
                {
                    continue;
                }

                nextSampleOrdinal += stride;
                var pixel = indexer[y, x];
                var lab = GetCachedLab(pixel, labCache);
                var deltaE = useCie76
                    ? ColorDifference.DeltaE76(lab, reference)
                    : ColorDifference.DeltaE00(lab, reference);
                sampleCount++;
                var delta = deltaE - mean;
                mean += delta / sampleCount;
                m2 += delta * (deltaE - mean);
            }
        }

        if (sampleCount == 0)
        {
            return new DeltaEStatistics(0, 0.0, 0.0, 0.0);
        }

        var variance = Math.Max(0.0, m2 / sampleCount);
        var stdDev = Math.Sqrt(variance);
        return new DeltaEStatistics(
            sampleCount,
            mean,
            stdDev,
            MeasurementStatisticsHelper.ComputeStandardError(stdDev, sampleCount));
    }

    private static CieLab GetCachedLab(Vec3b pixel, Dictionary<int, CieLab> labCache)
    {
        var key = pixel.Item0 | (pixel.Item1 << 8) | (pixel.Item2 << 16);
        if (labCache.TryGetValue(key, out var lab))
        {
            return lab;
        }

        lab = CieLabConverter.BgrToLab(pixel.Item0, pixel.Item1, pixel.Item2);
        labCache[key] = lab;
        return lab;
    }

    private readonly record struct LabStatistics(CieLab Mean, CieLab StdDev, int SampleCount);

    private readonly record struct DeltaEStatistics(int SampleCount, double Mean, double StdDev, double StdError);
}
