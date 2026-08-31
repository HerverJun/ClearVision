using System.Collections;
using System.Globalization;
using System.Text.Json;
using ClearVision.Product.Core.Attributes;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Operators;
using ClearVision.Product.Core.ValueObjects;
using ClearVision.Product.Infrastructure.Calibration;
using Microsoft.Extensions.Logging;
using OpenCvSharp;

namespace ClearVision.Product.Infrastructure.Operators;

[OperatorMeta(
    DisplayName = "N点标定",
    Description = "基于全部点对鲁棒估计仿射或单应性标定模型。",
    CategoryId = OperatorCategoryId.CalibrationAndCoordinates,
    IconName = "n-point",
    Keywords = new[] { "n-point", "affine", "homography", "calibration", "ransac" }
)]
[InputPort("Image", "Image", PortDataType.Image, IsRequired = false)]
[OutputPort("CalibrationData", "Calibration Data", PortDataType.String)]
[OutputPort("ReprojectionError", "Reprojection Error", PortDataType.Float)]
[OutputPort("MeanReprojectionError", "Mean Reprojection Error", PortDataType.Float)]
[OutputPort("MaxReprojectionError", "Max Reprojection Error", PortDataType.Float)]
[OutputPort("InlierMeanReprojectionError", "Inlier Mean Reprojection Error", PortDataType.Float)]
[OutputPort("InlierMaxReprojectionError", "Inlier Max Reprojection Error", PortDataType.Float)]
[OutputPort("AllSampleMeanReprojectionError", "All Sample Mean Reprojection Error", PortDataType.Float)]
[OutputPort("AllSampleMaxReprojectionError", "All Sample Max Reprojection Error", PortDataType.Float)]
[OutputPort("ReprojectionErrorScope", "Reprojection Error Scope", PortDataType.String)]
[OutputPort("CalibrationAssetId", "Calibration Asset Id", PortDataType.String)]
[OutputPort("CalibrationAssetCandidate", "Calibration Asset Candidate", PortDataType.Boolean)]
[OutputPort("CalibrationContentHash", "Calibration Content Hash", PortDataType.String)]
[OperatorParam("CalibrationMode", "Calibration Mode", "enum", DefaultValue = "Affine", Options = new[] { "Affine|Affine", "Perspective|Perspective" })]
[OperatorParam("PointPairs", "Point Pairs", "string", DefaultValue = "")]
[OperatorParam("CalibrationAssetId", "Calibration Asset Id", "string", DefaultValue = "")]
[OperatorParam("RansacReprojectionThreshold", "RANSAC Reprojection Threshold", "double", DefaultValue = 3.0, Min = 0.000001, Max = 100000.0)]
[OperatorParam("RansacMaxIterations", "RANSAC Max Iterations", "int", DefaultValue = 3000, Min = 1, Max = 100000)]
[OperatorParam("RansacConfidence", "RANSAC Confidence", "double", DefaultValue = 0.995, Min = 0.001, Max = 0.999999)]
[OperatorParam("MaxAcceptedReprojectionError", "Max Accepted Reprojection Error", "double", DefaultValue = 3.0, Min = 0.0, Max = 100000.0)]
[OperatorParam("MinInlierCount", "Minimum Inlier Count", "int", DefaultValue = 0, Min = 0, Max = 1000000)]
[OperatorParam("MinInlierRatio", "Minimum Inlier Ratio", "double", DefaultValue = 0.5, Min = 0.0, Max = 1.0)]
[OperatorParam("CalibrationUnit", "Calibration Unit", "string", DefaultValue = "mm")]
public class NPointCalibrationOperator : OperatorBase
{
    private const double DefaultRansacReprojectionThreshold = 3.0;
    private const int DefaultRansacMaxIterations = 3000;
    private const double DefaultRansacConfidence = 0.995;
    private const double DefaultMaxAcceptedReprojectionError = 3.0;
    private const double DefaultMinInlierRatio = 0.5;
    private readonly NPointCalibrationSolver _solver = new();

    public override OperatorType OperatorType => OperatorType.NPointCalibration;

    public NPointCalibrationOperator(ILogger<NPointCalibrationOperator> logger) : base(logger)
    {
    }

    protected override Task<OperatorExecutionOutput> ExecuteCoreAsync(
        Operator @operator,
        Dictionary<string, object>? inputs,
        CancellationToken cancellationToken)
    {
        var mode = GetStringParam(@operator, "CalibrationMode", "Affine");
        var pointPairsRaw = ResolvePointPairsRaw(@operator, inputs);

        if (!TryParsePointPairs(pointPairsRaw, out var pointPairs) || pointPairs.Count == 0)
        {
            return Task.FromResult(OperatorExecutionOutput.Failure("PointPairs is required and must be valid JSON or list data."));
        }

        if (!TryResolveMode(mode, out var calibrationMode))
        {
            return Task.FromResult(OperatorExecutionOutput.Failure("CalibrationMode must be Affine or Perspective."));
        }

        var requiredCount = GetRequiredPointCount(calibrationMode);
        if (pointPairs.Count < requiredCount)
        {
            return Task.FromResult(OperatorExecutionOutput.Failure($"{mode} mode requires at least {requiredCount} point pairs."));
        }

        var options = ResolveCalibrationOptions(@operator, requiredCount);
        var result = _solver.Solve(new NPointCalibrationRequest(calibrationMode, pointPairs, options));
        if (!result.Success)
        {
            return Task.FromResult(OperatorExecutionOutput.Failure(result.ErrorMessage));
        }

        return Task.FromResult(BuildSuccessOutput(@operator, inputs, pointPairs, result, options));
    }

    public override ValidationResult ValidateParameters(Operator @operator)
    {
        var mode = GetStringParam(@operator, "CalibrationMode", "Affine");
        if (!mode.Equals("Affine", StringComparison.OrdinalIgnoreCase) &&
            !mode.Equals("Perspective", StringComparison.OrdinalIgnoreCase))
        {
            return ValidationResult.Invalid("CalibrationMode must be Affine or Perspective.");
        }

        var pointPairsRaw = ResolvePointPairsRaw(@operator, null);
        if (IsPointPairsRawEmpty(pointPairsRaw))
        {
            return ValidationResult.Valid();
        }

        if (!TryParsePointPairs(pointPairsRaw, out var pointPairs))
        {
            return ValidationResult.Invalid("PointPairs is not valid JSON/list format.");
        }

        if (!TryResolveMode(mode, out var calibrationMode))
        {
            return ValidationResult.Invalid("CalibrationMode must be Affine or Perspective.");
        }

        var requiredCount = GetRequiredPointCount(calibrationMode);
        if (pointPairs.Count < requiredCount)
        {
            return ValidationResult.Invalid($"{mode} mode requires at least {requiredCount} point pairs.");
        }

        var srcPoints = pointPairs.Select(p => new Point2d(p.ImagePoint.X, p.ImagePoint.Y)).ToArray();
        if (!NPointCalibrationSolver.TryValidatePointSet(srcPoints, requiredCount, "ImagePoint", out var sourceValidationError))
        {
            return ValidationResult.Invalid(sourceValidationError ?? "ImagePoint set is invalid.");
        }

        var dstPoints = pointPairs.Select(p => new Point2d(p.WorldPoint.X, p.WorldPoint.Y)).ToArray();
        if (!NPointCalibrationSolver.TryValidatePointSet(dstPoints, requiredCount, "WorldPoint", out var targetValidationError))
        {
            return ValidationResult.Invalid(targetValidationError ?? "WorldPoint set is invalid.");
        }

        var config = ResolveCalibrationOptions(@operator, requiredCount);
        if (config.RansacReprojectionThreshold <= 0 || !double.IsFinite(config.RansacReprojectionThreshold))
        {
            return ValidationResult.Invalid("RansacReprojectionThreshold must be a positive finite number.");
        }

        if (config.RansacMaxIterations < 1)
        {
            return ValidationResult.Invalid("RansacMaxIterations must be at least 1.");
        }

        if (config.RansacConfidence <= 0 || config.RansacConfidence >= 1 || !double.IsFinite(config.RansacConfidence))
        {
            return ValidationResult.Invalid("RansacConfidence must be greater than 0 and less than 1.");
        }

        if (config.MaxAcceptedReprojectionError < 0 || !double.IsFinite(config.MaxAcceptedReprojectionError))
        {
            return ValidationResult.Invalid("MaxAcceptedReprojectionError must be a non-negative finite number.");
        }

        if (config.MinInlierRatio < 0 || config.MinInlierRatio > 1 || !double.IsFinite(config.MinInlierRatio))
        {
            return ValidationResult.Invalid("MinInlierRatio must be between 0 and 1.");
        }

        if (string.IsNullOrWhiteSpace(config.CalibrationUnit))
        {
            return ValidationResult.Invalid("CalibrationUnit must not be empty.");
        }

        return ValidationResult.Valid();
    }

    private OperatorExecutionOutput BuildSuccessOutput(
        Operator @operator,
        Dictionary<string, object>? inputs,
        IReadOnlyList<NPointCalibrationPointPair> pointPairs,
        NPointCalibrationResult result,
        NPointCalibrationOptions options)
    {
        var bundle = result.Bundle;
        var errorStats = result.ErrorStats;
        var calibrationJson = CalibrationBundleV2Json.Serialize(bundle);

        var resultData = new Dictionary<string, object>
        {
            ["CalibrationData"] = calibrationJson,
            ["CalibrationBundle"] = bundle,
            ["ReprojectionError"] = errorStats.MeanError,
            ["MaxReprojectionError"] = errorStats.MaxError,
            ["MeanReprojectionError"] = errorStats.MeanError,
            ["InlierMeanReprojectionError"] = errorStats.InlierMeanError,
            ["InlierMaxReprojectionError"] = errorStats.InlierMaxError,
            ["AllSampleMeanReprojectionError"] = errorStats.AllSampleMeanError,
            ["AllSampleMaxReprojectionError"] = errorStats.AllSampleMaxError,
            ["ReprojectionErrorScope"] = "Inlier",
            ["InlierCount"] = errorStats.InlierCount,
            ["TotalSampleCount"] = pointPairs.Count,
            ["InlierRatio"] = errorStats.InlierRatio,
            ["Accepted"] = bundle.Quality.Accepted,
            ["RansacReprojectionThreshold"] = options.RansacReprojectionThreshold,
            ["RansacMaxIterations"] = options.RansacMaxIterations,
            ["RansacConfidence"] = options.RansacConfidence,
            ["MaxAcceptedReprojectionError"] = options.MaxAcceptedReprojectionError,
            ["MinInlierCount"] = options.MinInlierCount,
            ["MinInlierRatio"] = options.MinInlierRatio,
            ["ReprojectionErrorUnit"] = options.CalibrationUnit,
            ["CalibrationUnit"] = options.CalibrationUnit
        };
        CalibrationAssetCandidateOutput.AddTo(
            resultData,
            GetStringParam(@operator, "CalibrationAssetId", string.Empty),
            calibrationJson);


        if (TryGetInputImage(inputs, out var imageWrapper) && imageWrapper != null)
        {
            var src = imageWrapper.GetMat();
            if (!src.Empty())
            {
                var resultImage = src.Clone();
                DrawCalibrationPoints(resultImage, pointPairs);
                return OperatorExecutionOutput.Success(CreateImageOutput(resultImage, resultData));
            }
        }

        return OperatorExecutionOutput.Success(resultData);
    }

    private NPointCalibrationOptions ResolveCalibrationOptions(Operator @operator, int requiredPointCount)
    {
        var configuredMinInlierCount = GetIntParam(@operator, "MinInlierCount", 0, 0, 1000000);
        var minInlierCount = configuredMinInlierCount <= 0
            ? requiredPointCount
            : configuredMinInlierCount;

        return new NPointCalibrationOptions(
            GetDoubleParam(@operator, "RansacReprojectionThreshold", DefaultRansacReprojectionThreshold, 0.000001, 100000.0),
            GetIntParam(@operator, "RansacMaxIterations", DefaultRansacMaxIterations, 1, 100000),
            GetDoubleParam(@operator, "RansacConfidence", DefaultRansacConfidence, 0.001, 0.999999),
            GetDoubleParam(@operator, "MaxAcceptedReprojectionError", DefaultMaxAcceptedReprojectionError, 0.0, 100000.0),
            minInlierCount,
            GetDoubleParam(@operator, "MinInlierRatio", DefaultMinInlierRatio, 0.0, 1.0),
            NormalizeCalibrationUnit(GetStringParam(@operator, "CalibrationUnit", "mm")),
            nameof(NPointCalibrationOperator));
    }

    private static bool TryResolveMode(string mode, out NPointCalibrationMode calibrationMode)
    {
        if (mode.Equals("Perspective", StringComparison.OrdinalIgnoreCase))
        {
            calibrationMode = NPointCalibrationMode.Perspective;
            return true;
        }

        if (mode.Equals("Affine", StringComparison.OrdinalIgnoreCase))
        {
            calibrationMode = NPointCalibrationMode.Affine;
            return true;
        }

        calibrationMode = NPointCalibrationMode.Affine;
        return false;
    }

    private static int GetRequiredPointCount(NPointCalibrationMode mode)
    {
        return mode == NPointCalibrationMode.Perspective ? 4 : 3;
    }

    private static string NormalizeCalibrationUnit(string rawUnit)
    {
        return string.IsNullOrWhiteSpace(rawUnit)
            ? "mm"
            : rawUnit.Trim();
    }

    private static object? ResolvePointPairsRaw(Operator @operator, Dictionary<string, object>? inputs)
    {
        if (inputs != null && inputs.TryGetValue("PointPairs", out var pairObj) && pairObj != null)
        {
            return pairObj;
        }

        return @operator.Parameters.FirstOrDefault(p =>
                   p.Name.Equals("PointPairs", StringComparison.OrdinalIgnoreCase))
               ?.Value;
    }

    private static bool IsPointPairsRawEmpty(object? raw)
    {
        return raw == null || raw is string text && string.IsNullOrWhiteSpace(text);
    }

    private static bool TryParsePointPairs(object? raw, out List<NPointCalibrationPointPair> pointPairs)
    {
        pointPairs = new List<NPointCalibrationPointPair>();
        if (raw == null)
        {
            return false;
        }

        if (raw is string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            try
            {
                using var doc = JsonDocument.Parse(text);
                if (doc.RootElement.ValueKind != JsonValueKind.Array)
                {
                    return false;
                }

                foreach (var item in doc.RootElement.EnumerateArray())
                {
                    if (TryParsePointPair(item, out var pair))
                    {
                        pointPairs.Add(pair);
                    }
                }

                return pointPairs.Count > 0;
            }
            catch
            {
                return false;
            }
        }

        if (raw is IEnumerable enumerable)
        {
            foreach (var item in enumerable)
            {
                if (TryParsePointPair(item, out var pair))
                {
                    pointPairs.Add(pair);
                }
            }

            return pointPairs.Count > 0;
        }

        return false;
    }

    private static bool TryParsePointPair(object? raw, out NPointCalibrationPointPair pair)
    {
        pair = default;
        if (raw == null)
        {
            return false;
        }

        if (raw is JsonElement element)
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            if (IsPointPairDisabled(element))
            {
                return false;
            }

            if (TryGetNumberProperty(element, "ImageX", out var imageX) &&
                TryGetNumberProperty(element, "ImageY", out var imageY) &&
                TryGetNumberProperty(element, "WorldX", out var worldX) &&
                TryGetNumberProperty(element, "WorldY", out var worldY))
            {
                pair = new NPointCalibrationPointPair(new Position(imageX, imageY), new Position(worldX, worldY));
                return true;
            }

            if (TryGetNestedPoint(element, "ImagePoint", out var imagePoint) &&
                TryGetNestedPoint(element, "WorldPoint", out var worldPoint))
            {
                pair = new NPointCalibrationPointPair(imagePoint, worldPoint);
                return true;
            }

            if (TryGetNumberProperty(element, "PixelX", out imageX) &&
                TryGetNumberProperty(element, "PixelY", out imageY) &&
                TryGetNumberProperty(element, "PhysicalX", out worldX) &&
                TryGetNumberProperty(element, "PhysicalY", out worldY))
            {
                pair = new NPointCalibrationPointPair(new Position(imageX, imageY), new Position(worldX, worldY));
                return true;
            }

            return false;
        }

        if (raw is IDictionary<string, object> dict)
        {
            if (IsPointPairDisabled(dict))
            {
                return false;
            }

            if (TryGetDouble(dict, "ImageX", out var imageX) &&
                TryGetDouble(dict, "ImageY", out var imageY) &&
                TryGetDouble(dict, "WorldX", out var worldX) &&
                TryGetDouble(dict, "WorldY", out var worldY))
            {
                pair = new NPointCalibrationPointPair(new Position(imageX, imageY), new Position(worldX, worldY));
                return true;
            }

            if (dict.TryGetValue("ImagePoint", out var imagePointObj) &&
                dict.TryGetValue("WorldPoint", out var worldPointObj) &&
                TryParsePoint(imagePointObj, out var imagePoint) &&
                TryParsePoint(worldPointObj, out var worldPoint))
            {
                pair = new NPointCalibrationPointPair(imagePoint, worldPoint);
                return true;
            }

            return false;
        }

        if (raw is IDictionary legacy)
        {
            var normalized = legacy.Cast<DictionaryEntry>()
                .Where(entry => entry.Key != null)
                .ToDictionary(
                    entry => entry.Key!.ToString() ?? string.Empty,
                    entry => entry.Value ?? 0.0,
                    StringComparer.OrdinalIgnoreCase);
            return TryParsePointPair(normalized, out pair);
        }

        return false;
    }

    private static bool IsPointPairDisabled(JsonElement element)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (!property.Name.Equals("Enabled", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return property.Value.ValueKind switch
            {
                JsonValueKind.False => true,
                JsonValueKind.True => false,
                JsonValueKind.String => bool.TryParse(property.Value.GetString(), out var enabled) && !enabled,
                JsonValueKind.Number => property.Value.TryGetDouble(out var number) && Math.Abs(number) <= double.Epsilon,
                _ => false
            };
        }

        return false;
    }

    private static bool IsPointPairDisabled(IDictionary<string, object> dict)
    {
        if (!dict.TryGetValue("Enabled", out var raw) || raw == null)
        {
            return false;
        }

        return raw switch
        {
            bool enabled => !enabled,
            string text => bool.TryParse(text, out var enabled) && !enabled,
            int number => number == 0,
            long number => number == 0,
            double number => Math.Abs(number) <= double.Epsilon,
            float number => Math.Abs(number) <= float.Epsilon,
            _ => false
        };
    }

    private static bool TryGetNumberProperty(JsonElement obj, string propertyName, out double value)
    {
        value = 0;
        foreach (var property in obj.EnumerateObject())
        {
            if (!property.Name.Equals(propertyName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (property.Value.ValueKind == JsonValueKind.Number)
            {
                return property.Value.TryGetDouble(out value);
            }

            if (property.Value.ValueKind == JsonValueKind.String &&
                double.TryParse(property.Value.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out value))
            {
                return true;
            }

            return false;
        }

        return false;
    }

    private static bool TryGetNestedPoint(JsonElement parent, string name, out Position point)
    {
        point = new Position(0, 0);
        foreach (var property in parent.EnumerateObject())
        {
            if (!property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return TryParsePoint(property.Value, out point);
        }

        return false;
    }

    private static bool TryParsePoint(object? raw, out Position point)
    {
        point = new Position(0, 0);
        if (raw == null)
        {
            return false;
        }

        if (raw is Position position)
        {
            point = position;
            return true;
        }

        if (raw is JsonElement element)
        {
            if (TryGetNumberProperty(element, "X", out var x) && TryGetNumberProperty(element, "Y", out var y))
            {
                point = new Position(x, y);
                return true;
            }

            return false;
        }

        if (raw is IDictionary<string, object> dict &&
            TryGetDouble(dict, "X", out var dx) &&
            TryGetDouble(dict, "Y", out var dy))
        {
            point = new Position(dx, dy);
            return true;
        }

        if (raw is IDictionary legacy)
        {
            var normalized = legacy.Cast<DictionaryEntry>()
                .Where(entry => entry.Key != null)
                .ToDictionary(
                    entry => entry.Key!.ToString() ?? string.Empty,
                    entry => entry.Value ?? 0.0,
                    StringComparer.OrdinalIgnoreCase);
            return TryParsePoint(normalized, out point);
        }

        return false;
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
            _ => double.TryParse(raw.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out value)
        };
    }

    private static void DrawCalibrationPoints(Mat image, IReadOnlyList<NPointCalibrationPointPair> pointPairs)
    {
        for (var i = 0; i < pointPairs.Count; i++)
        {
            var x = (int)Math.Round(pointPairs[i].ImagePoint.X);
            var y = (int)Math.Round(pointPairs[i].ImagePoint.Y);
            Cv2.Circle(image, new Point(x, y), 4, new Scalar(0, 255, 0), -1);
            Cv2.PutText(
                image,
                (i + 1).ToString(CultureInfo.InvariantCulture),
                new Point(x + 6, y - 6),
                HersheyFonts.HersheySimplex,
                0.5,
                new Scalar(0, 255, 255),
                1);
        }
    }
}
