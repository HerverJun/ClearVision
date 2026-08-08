using ClearVision.Product.Core.Attributes;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Operators;
using ClearVision.Product.Core.ValueObjects;
using Microsoft.Extensions.Logging;
using OpenCvSharp;

namespace ClearVision.Product.Infrastructure.Operators;

[OperatorMeta(
    DisplayName = "测量",
    Description = "统一基础二维几何测量入口，支持点点距离、点线距离、线线距离/夹角和三点角度；默认保持旧版点点测量行为。",
    CategoryId = OperatorCategoryId.Measurement,
    IconName = "measure",
    Keywords = new[] { "测量", "距离", "点线", "线线", "夹角", "三点角度", "Measure", "Distance", "Angle" }
)]
[OperatorParameterRule("MeasureType", ReasonCode = "MEASUREMENT_TYPE")]
[OperatorParameterRule("X1", DisabledWhenAny = new[] { "MeasureType==PointToLine", "MeasureType==LineToLine", "MeasureType==ThreePointAngle" }, HiddenWhenAny = new[] { "MeasureType==PointToLine", "MeasureType==LineToLine", "MeasureType==ThreePointAngle" }, IgnoredWhenAny = new[] { "MeasureType==PointToLine", "MeasureType==LineToLine", "MeasureType==ThreePointAngle" }, ReasonCode = "MEASUREMENT_COORDINATES_ONLY_FOR_POINT_DISTANCE")]
[OperatorParameterRule("Y1", DisabledWhenAny = new[] { "MeasureType==PointToLine", "MeasureType==LineToLine", "MeasureType==ThreePointAngle" }, HiddenWhenAny = new[] { "MeasureType==PointToLine", "MeasureType==LineToLine", "MeasureType==ThreePointAngle" }, IgnoredWhenAny = new[] { "MeasureType==PointToLine", "MeasureType==LineToLine", "MeasureType==ThreePointAngle" }, ReasonCode = "MEASUREMENT_COORDINATES_ONLY_FOR_POINT_DISTANCE")]
[OperatorParameterRule("X2", DisabledWhenAny = new[] { "MeasureType==PointToLine", "MeasureType==LineToLine", "MeasureType==ThreePointAngle" }, HiddenWhenAny = new[] { "MeasureType==PointToLine", "MeasureType==LineToLine", "MeasureType==ThreePointAngle" }, IgnoredWhenAny = new[] { "MeasureType==PointToLine", "MeasureType==LineToLine", "MeasureType==ThreePointAngle" }, ReasonCode = "MEASUREMENT_COORDINATES_ONLY_FOR_POINT_DISTANCE")]
[OperatorParameterRule("Y2", DisabledWhenAny = new[] { "MeasureType==PointToLine", "MeasureType==LineToLine", "MeasureType==ThreePointAngle" }, HiddenWhenAny = new[] { "MeasureType==PointToLine", "MeasureType==LineToLine", "MeasureType==ThreePointAngle" }, IgnoredWhenAny = new[] { "MeasureType==PointToLine", "MeasureType==LineToLine", "MeasureType==ThreePointAngle" }, ReasonCode = "MEASUREMENT_COORDINATES_ONLY_FOR_POINT_DISTANCE")]
[OperatorParameterRule("DistanceModel", DisabledWhenAny = new[] { "MeasureType==PointToPoint", "MeasureType==Horizontal", "MeasureType==Vertical", "MeasureType==ThreePointAngle" }, HiddenWhenAny = new[] { "MeasureType==PointToPoint", "MeasureType==Horizontal", "MeasureType==Vertical", "MeasureType==ThreePointAngle" }, IgnoredWhenAny = new[] { "MeasureType==PointToPoint", "MeasureType==Horizontal", "MeasureType==Vertical", "MeasureType==ThreePointAngle" }, ReasonCode = "MEASUREMENT_DISTANCE_MODEL_ONLY_FOR_LINE_DISTANCE")]
[OperatorParameterRule("ParallelThreshold", DisabledWhenAll = new[] { "MeasureType!=LineToLine" }, HiddenWhenAll = new[] { "MeasureType!=LineToLine" }, IgnoredWhenAll = new[] { "MeasureType!=LineToLine" }, ReasonCode = "MEASUREMENT_PARALLEL_THRESHOLD_ONLY_FOR_LINE_TO_LINE")]
[OperatorParameterRule("AngleUnit", RequiredPolicy = OperatorParameterRequiredPolicy.Required, DisabledWhenAll = new[] { "MeasureType!=ThreePointAngle" }, HiddenWhenAll = new[] { "MeasureType!=ThreePointAngle" }, IgnoredWhenAll = new[] { "MeasureType!=ThreePointAngle" }, ReasonCode = "MEASUREMENT_ANGLE_UNIT_ONLY_FOR_ANGLE")]
[OperatorOutputRule("Distance", AvailableWhenAny = new[] { "MeasureType==PointToPoint", "MeasureType==Horizontal", "MeasureType==Vertical", "MeasureType==PointToLine", "MeasureType==LineToLine" }, ReasonCode = "MEASUREMENT_DISTANCE_OUTPUT")]
[OperatorOutputRule("DeltaX", AvailableWhenAny = new[] { "MeasureType==PointToPoint", "MeasureType==Horizontal", "MeasureType==Vertical", "MeasureType==PointToLine" }, ReasonCode = "MEASUREMENT_DELTA_OUTPUT")]
[OperatorOutputRule("DeltaY", AvailableWhenAny = new[] { "MeasureType==PointToPoint", "MeasureType==Horizontal", "MeasureType==Vertical", "MeasureType==PointToLine" }, ReasonCode = "MEASUREMENT_DELTA_OUTPUT")]
[OperatorOutputRule("Angle", AvailableWhenAny = new[] { "MeasureType==LineToLine", "MeasureType==ThreePointAngle" }, ReasonCode = "MEASUREMENT_ANGLE_OUTPUT")]
[OperatorOutputRule("FootPoint", AvailableWhenAll = new[] { "MeasureType==PointToLine" }, ReasonCode = "MEASUREMENT_FOOT_POINT_OUTPUT")]
[OperatorOutputRule("Intersection", AvailableWhenAll = new[] { "MeasureType==LineToLine" }, ReasonCode = "MEASUREMENT_INTERSECTION_OUTPUT")]
[OperatorOutputRule("HasIntersection", AvailableWhenAll = new[] { "MeasureType==LineToLine" }, ReasonCode = "MEASUREMENT_INTERSECTION_OUTPUT")]
[OperatorOutputRule("IsParallel", AvailableWhenAll = new[] { "MeasureType==LineToLine" }, ReasonCode = "MEASUREMENT_PARALLEL_OUTPUT")]
[OperatorOutputRule("UncertaintyDeg", AvailableWhenAll = new[] { "MeasureType==ThreePointAngle" }, ReasonCode = "MEASUREMENT_ANGLE_UNCERTAINTY_OUTPUT")]
[OperatorGenerationDependency(typeof(MeasurementGeometryHelper))]
[InputPort("Image", "输入图像", PortDataType.Image, IsRequired = false)]
[InputPort("PointA", "点A/待测点", PortDataType.Point, IsRequired = false)]
[InputPort("PointB", "点B/角度顶点", PortDataType.Point, IsRequired = false)]
[InputPort("PointC", "点C", PortDataType.Point, IsRequired = false)]
[InputPort("Line1", "线1", PortDataType.LineData, IsRequired = false)]
[InputPort("Line2", "线2", PortDataType.LineData, IsRequired = false)]
[OutputPort("Image", "结果图像", PortDataType.Image)]
[OutputPort("Distance", "测量距离", PortDataType.Float)]
[OutputPort("DeltaX", "水平分量", PortDataType.Float)]
[OutputPort("DeltaY", "垂直分量", PortDataType.Float)]
[OutputPort("Angle", "夹角", PortDataType.Float)]
[OutputPort("Value", "主测量值", PortDataType.Float)]
[OutputPort("Unit", "单位", PortDataType.String)]
[OutputPort("MeasurementType", "实际测量类型", PortDataType.String)]
[OutputPort("StatusCode", "状态码", PortDataType.String)]
[OutputPort("StatusMessage", "状态信息", PortDataType.String)]
[OutputPort("FootPoint", "垂足", PortDataType.Point)]
[OutputPort("Intersection", "交点", PortDataType.Point)]
[OutputPort("HasIntersection", "是否相交", PortDataType.Boolean)]
[OutputPort("IsParallel", "是否平行", PortDataType.Boolean)]
[OutputPort("Confidence", "测量置信度", PortDataType.Float)]
[OutputPort("UncertaintyPx", "输入几何像素不确定度", PortDataType.Float)]
[OutputPort("UncertaintyDeg", "角度不确定度（度）", PortDataType.Float)]
[OperatorParam("X1", "起点X", "int", DefaultValue = 0)]
[OperatorParam("Y1", "起点Y", "int", DefaultValue = 0)]
[OperatorParam("X2", "终点X", "int", DefaultValue = 100)]
[OperatorParam("Y2", "终点Y", "int", DefaultValue = 100)]
[OperatorParam("MeasureType", "测量类型", "enum", Description = "默认 PointToPoint 保持旧流程。", DefaultValue = "PointToPoint", Options = new[] { "PointToPoint|点到点", "Horizontal|水平距离", "Vertical|垂直距离", "PointToLine|点到线", "LineToLine|线到线", "ThreePointAngle|三点角度" })]
[OperatorParam("DistanceModel", "线距离模型", "enum", DefaultValue = "Segment", Options = new[] { "Segment|线段", "InfiniteLine|无限直线" })]
[OperatorParam("ParallelThreshold", "平行阈值(度)", "double", DefaultValue = 2.0, Min = 0.0, Max = 45.0)]
[OperatorParam("AngleUnit", "角度单位", "enum", DefaultValue = "Degree", Options = new[] { "Degree|度", "Radian|弧度" })]
public class MeasureDistanceOperator : OperatorBase
{
    public override OperatorType OperatorType => OperatorType.Measurement;

    public MeasureDistanceOperator(ILogger<MeasureDistanceOperator> logger) : base(logger)
    {
    }

    protected override Task<OperatorExecutionOutput> ExecuteCoreAsync(
        Operator @operator,
        Dictionary<string, object>? inputs,
        CancellationToken cancellationToken)
    {
        var measureType = GetStringParam(@operator, "MeasureType", "PointToPoint").Trim();

        OperatorExecutionOutput result = measureType.ToLowerInvariant() switch
        {
            "pointtopoint" or "horizontal" or "vertical" => ExecuteLegacyPointMeasurement(@operator, inputs, measureType),
            "pointtoline" => ExecutePointToLine(@operator, inputs),
            "linetoline" => ExecuteLineToLine(@operator, inputs),
            "threepointangle" => ExecuteThreePointAngle(@operator, inputs),
            _ => OperatorExecutionOutput.Failure($"Unsupported measure type: {measureType}")
        };

        return Task.FromResult(result);
    }

    public override ValidationResult ValidateParameters(Operator @operator)
    {
        var measureType = GetStringParam(@operator, "MeasureType", "PointToPoint").Trim();
        var validTypes = new[]
        {
            "PointToPoint", "Horizontal", "Vertical", "PointToLine", "LineToLine", "ThreePointAngle"
        };
        if (!validTypes.Contains(measureType, StringComparer.OrdinalIgnoreCase))
        {
            return ValidationResult.Invalid($"Unsupported measure type: {measureType}");
        }

        if (measureType.Equals("PointToLine", StringComparison.OrdinalIgnoreCase) ||
            measureType.Equals("LineToLine", StringComparison.OrdinalIgnoreCase))
        {
            var distanceModel = GetStringParam(@operator, "DistanceModel", "Segment");
            if (!TryParseDistanceModel(distanceModel, out _))
            {
                return ValidationResult.Invalid("DistanceModel must be Segment or InfiniteLine");
            }
        }

        if (measureType.Equals("LineToLine", StringComparison.OrdinalIgnoreCase))
        {
            var parallelThreshold = GetDoubleParam(@operator, "ParallelThreshold", 2.0);
            if (!double.IsFinite(parallelThreshold) || parallelThreshold < 0.0 || parallelThreshold > 45.0)
            {
                return ValidationResult.Invalid("ParallelThreshold must be within [0, 45]");
            }
        }

        if (measureType.Equals("ThreePointAngle", StringComparison.OrdinalIgnoreCase) &&
            !TryParseAngleUnit(GetStringParam(@operator, "AngleUnit", "Degree"), out _))
        {
            return ValidationResult.Invalid("AngleUnit must be Degree or Radian");
        }

        return ValidationResult.Valid();
    }

    private OperatorExecutionOutput ExecuteLegacyPointMeasurement(
        Operator @operator,
        Dictionary<string, object>? inputs,
        string measureType)
    {
        if (TryGetPoint(inputs, "PointA", out var pointA) &&
            TryGetPoint(inputs, "PointB", out var pointB))
        {
            return BuildPointMeasurement(pointA, pointB, measureType, image: null);
        }

        if (!TryGetInputImage(inputs, out var imageWrapper) || imageWrapper == null)
        {
            return OperatorExecutionOutput.Failure("未提供输入图像或 PointA/PointB");
        }

        var source = imageWrapper.GetMat();
        if (source.Empty())
        {
            return OperatorExecutionOutput.Failure("输入图像无效");
        }

        var parameterPointA = new Position(
            GetIntParam(@operator, "X1", 0),
            GetIntParam(@operator, "Y1", 0));
        var parameterPointB = new Position(
            GetIntParam(@operator, "X2", 100),
            GetIntParam(@operator, "Y2", 100));
        return BuildPointMeasurement(parameterPointA, parameterPointB, measureType, source);
    }

    private OperatorExecutionOutput BuildPointMeasurement(
        Position pointA,
        Position pointB,
        string measureType,
        Mat? image)
    {
        var deltaX = pointB.X - pointA.X;
        var deltaY = pointB.Y - pointA.Y;
        var normalized = measureType.ToLowerInvariant();
        var distance = normalized switch
        {
            "pointtopoint" => MeasurementGeometryHelper.Distance(pointA, pointB),
            "horizontal" => Math.Abs(deltaX),
            "vertical" => Math.Abs(deltaY),
            _ => double.NaN
        };

        if (!double.IsFinite(distance) ||
            (normalized == "pointtopoint" && distance < 1e-9) ||
            (normalized == "horizontal" && Math.Abs(deltaX) < 1e-9) ||
            (normalized == "vertical" && Math.Abs(deltaY) < 1e-9))
        {
            return OperatorExecutionOutput.Failure("[DegenerateGeometry] Measurement distance is zero");
        }

        var resolvedEnd = normalized switch
        {
            "horizontal" => new Position(pointB.X, pointA.Y),
            "vertical" => new Position(pointA.X, pointB.Y),
            _ => pointB
        };
        var uncertainty = MeasurementGeometryHelper.PropagatePointPointDistanceUncertainty(
            pointA,
            MeasurementGeometryHelper.EstimatePointSigma(pointA),
            pointB,
            MeasurementGeometryHelper.EstimatePointSigma(pointB));

        var output = CreateCommonOutput(measureType, distance, "Pixel", uncertainty);
        output["Distance"] = distance;
        output["X1"] = pointA.X;
        output["Y1"] = pointA.Y;
        output["X2"] = resolvedEnd.X;
        output["Y2"] = resolvedEnd.Y;
        output["DeltaX"] = resolvedEnd.X - pointA.X;
        output["DeltaY"] = resolvedEnd.Y - pointA.Y;
        output["MeasureType"] = measureType;

        if (image == null)
        {
            return OperatorExecutionOutput.Success(output);
        }

        var resultImage = image.Clone();
        DrawLineDistance(resultImage, pointA, resolvedEnd, $"{distance:F2}px");
        return OperatorExecutionOutput.Success(CreateImageOutput(resultImage, output));
    }

    private OperatorExecutionOutput ExecutePointToLine(Operator @operator, Dictionary<string, object>? inputs)
    {
        if (!TryGetPoint(inputs, "PointA", out var point))
        {
            return OperatorExecutionOutput.Failure("PointToLine requires PointA");
        }

        if (!TryGetLine(inputs, "Line1", out var line))
        {
            return OperatorExecutionOutput.Failure("PointToLine requires Line1");
        }

        if (line.Length < 1e-9)
        {
            return OperatorExecutionOutput.Failure("[DegenerateGeometry] Line1 is zero length");
        }

        if (!TryParseDistanceModel(GetStringParam(@operator, "DistanceModel", "Segment"), out var distanceModel))
        {
            return OperatorExecutionOutput.Failure("DistanceModel must be Segment or InfiniteLine");
        }

        var segmentModel = distanceModel == DistanceModel.Segment;
        var footPoint = segmentModel
            ? MeasurementGeometryHelper.ProjectPointToSegment(point.X, point.Y, line)
            : MeasurementGeometryHelper.ProjectPointToInfiniteLine(point.X, point.Y, line);
        var distance = segmentModel
            ? MeasurementGeometryHelper.DistancePointToSegment(point.X, point.Y, line)
            : MeasurementGeometryHelper.DistancePointToInfiniteLine(point.X, point.Y, line);
        var uncertainty = MeasurementGeometryHelper.PropagatePointLineDistanceUncertainty(
            point,
            MeasurementGeometryHelper.EstimatePointSigma(point),
            line,
            MeasurementGeometryHelper.EstimateLineSigma(line),
            segmentModel);

        var output = CreateCommonOutput("PointToLine", distance, "Pixel", uncertainty);
        output["Distance"] = distance;
        output["DistanceModel"] = distanceModel.ToString();
        output["FootPoint"] = footPoint;
        output["FootPointX"] = footPoint.X;
        output["FootPointY"] = footPoint.Y;
        output["DeltaX"] = footPoint.X - point.X;
        output["DeltaY"] = footPoint.Y - point.Y;
        return SuccessWithOptionalImage(inputs, output, image =>
        {
            Cv2.Line(image, ToCvPoint(line.StartX, line.StartY), ToCvPoint(line.EndX, line.EndY), new Scalar(0, 255, 255), 2);
            Cv2.Line(image, ToCvPoint(point), ToCvPoint(footPoint), new Scalar(0, 255, 0), 2);
        });
    }

    private OperatorExecutionOutput ExecuteLineToLine(Operator @operator, Dictionary<string, object>? inputs)
    {
        if (!TryGetLine(inputs, "Line1", out var line1) || !TryGetLine(inputs, "Line2", out var line2))
        {
            return OperatorExecutionOutput.Failure("LineToLine requires Line1 and Line2");
        }

        if (line1.Length < 1e-9 || line2.Length < 1e-9)
        {
            return OperatorExecutionOutput.Failure("[DegenerateGeometry] Input line is zero length");
        }

        if (!TryParseDistanceModel(GetStringParam(@operator, "DistanceModel", "Segment"), out var distanceModel))
        {
            return OperatorExecutionOutput.Failure("DistanceModel must be Segment or InfiniteLine");
        }

        var parallelThreshold = GetDoubleParam(@operator, "ParallelThreshold", 2.0, 0.0, 45.0);
        var angle = MeasurementGeometryHelper.AngleBetweenLineDirections(line1, line2);
        var isParallel = angle <= parallelThreshold;
        var segmentModel = distanceModel == DistanceModel.Segment;
        var hasInfiniteIntersection = MeasurementGeometryHelper.TryGetInfiniteLineIntersection(line1, line2, out var infiniteIntersection);
        var hasSegmentIntersection = MeasurementGeometryHelper.TryGetSegmentIntersection(line1, line2, out var segmentIntersection);
        var hasIntersection = segmentModel ? hasSegmentIntersection : !isParallel && hasInfiniteIntersection;
        var intersection = segmentModel
            ? (hasSegmentIntersection ? segmentIntersection : MeasurementGeometryHelper.NoIntersection)
            : (hasInfiniteIntersection ? infiniteIntersection : MeasurementGeometryHelper.NoIntersection);
        var distance = segmentModel
            ? MeasurementGeometryHelper.DistanceSegmentToSegment(line1, line2)
            : isParallel
                ? MeasurementGeometryHelper.DistancePointToInfiniteLine(line1.StartX, line1.StartY, line2)
                : 0.0;
        var uncertainty = MeasurementGeometryHelper.PropagateLineLineDistanceUncertainty(
            line1,
            MeasurementGeometryHelper.EstimateLineSigma(line1),
            line2,
            MeasurementGeometryHelper.EstimateLineSigma(line2),
            segmentModel,
            parallelThreshold);

        var output = CreateCommonOutput("LineToLine", distance, "Pixel", uncertainty);
        output["Distance"] = distance;
        output["Angle"] = angle;
        output["DistanceModel"] = distanceModel.ToString();
        output["Intersection"] = hasIntersection ? intersection : MeasurementGeometryHelper.NoIntersection;
        output["HasIntersection"] = hasIntersection;
        output["IsParallel"] = isParallel;
        return SuccessWithOptionalImage(inputs, output, image =>
        {
            Cv2.Line(image, ToCvPoint(line1.StartX, line1.StartY), ToCvPoint(line1.EndX, line1.EndY), new Scalar(0, 255, 255), 2);
            Cv2.Line(image, ToCvPoint(line2.StartX, line2.StartY), ToCvPoint(line2.EndX, line2.EndY), new Scalar(255, 128, 0), 2);
        });
    }

    private OperatorExecutionOutput ExecuteThreePointAngle(Operator @operator, Dictionary<string, object>? inputs)
    {
        if (!TryGetAnglePoint(inputs, "PointA", out var pointA, out var pointASigmaPx) ||
            !TryGetAnglePoint(inputs, "PointB", out var vertex, out var vertexSigmaPx) ||
            !TryGetAnglePoint(inputs, "PointC", out var pointC, out var pointCSigmaPx))
        {
            return OperatorExecutionOutput.Failure("ThreePointAngle requires PointA, PointB and PointC");
        }

        if (!TryParseAngleUnit(GetStringParam(@operator, "AngleUnit", "Degree"), out var angleUnit))
        {
            return OperatorExecutionOutput.Failure("AngleUnit must be Degree or Radian");
        }

        var radians = MeasurementGeometryHelper.ThreePointAngleRadians(pointA, vertex, pointC);
        if (!double.IsFinite(radians))
        {
            return OperatorExecutionOutput.Failure("[DegenerateGeometry] Angle vertex has zero-length arm");
        }

        var value = angleUnit == AngleUnit.Radian ? radians : radians * 180.0 / Math.PI;
        var unit = angleUnit == AngleUnit.Radian ? "Radian" : "Degree";
        var uncertaintyDeg = MeasurementGeometryHelper.PropagateThreePointAngleUncertaintyDegrees(
            pointA,
            pointASigmaPx,
            vertex,
            vertexSigmaPx,
            pointC,
            pointCSigmaPx);
        var averageInputSigmaPx = (pointASigmaPx + vertexSigmaPx + pointCSigmaPx) / 3.0;
        var output = CreateCommonOutput("ThreePointAngle", value, unit, averageInputSigmaPx);
        output["Angle"] = value;
        output["Confidence"] = ComputeAngleConfidence(uncertaintyDeg);
        output["UncertaintyDeg"] = uncertaintyDeg;
        output["Vertex"] = vertex;
        return SuccessWithOptionalImage(inputs, output, image =>
        {
            Cv2.Line(image, ToCvPoint(pointA), ToCvPoint(vertex), new Scalar(0, 255, 255), 2);
            Cv2.Line(image, ToCvPoint(vertex), ToCvPoint(pointC), new Scalar(0, 255, 255), 2);
        });
    }

    private static Dictionary<string, object> CreateCommonOutput(
        string measurementType,
        double value,
        string unit,
        double uncertainty)
    {
        return new Dictionary<string, object>
        {
            ["MeasurementType"] = measurementType,
            ["Value"] = value,
            ["Unit"] = unit,
            ["StatusCode"] = "OK",
            ["StatusMessage"] = "Success",
            ["Confidence"] = ComputeConfidence(uncertainty),
            ["UncertaintyPx"] = uncertainty
        };
    }

    private OperatorExecutionOutput SuccessWithOptionalImage(
        Dictionary<string, object>? inputs,
        Dictionary<string, object> output,
        Action<Mat> draw)
    {
        if (!TryGetOptionalImage(inputs, out var source))
        {
            return OperatorExecutionOutput.Success(output);
        }

        var resultImage = source.Clone();
        draw(resultImage);
        return OperatorExecutionOutput.Success(CreateImageOutput(resultImage, output));
    }

    private static bool TryGetOptionalImage(Dictionary<string, object>? inputs, out Mat source)
    {
        source = null!;
        if (inputs == null || !inputs.TryGetValue("Image", out var raw) || raw == null)
        {
            return false;
        }

        ImageWrapper? wrapper = raw switch
        {
            ImageWrapper imageWrapper => imageWrapper,
            byte[] bytes => new ImageWrapper(bytes),
            _ => null
        };
        if (wrapper == null)
        {
            return false;
        }

        source = wrapper.GetMat();
        return !source.Empty();
    }

    private static bool TryGetPoint(Dictionary<string, object>? inputs, string key, out Position point)
    {
        point = new Position(0, 0);
        return inputs != null &&
               inputs.TryGetValue(key, out var raw) &&
               MeasurementGeometryHelper.TryParsePoint(raw, out point);
    }

    private static bool TryGetAnglePoint(
        Dictionary<string, object>? inputs,
        string key,
        out Position point,
        out double sigmaPx)
    {
        point = new Position(0, 0);
        sigmaPx = 0.0;
        if (inputs == null ||
            !inputs.TryGetValue(key, out var raw) ||
            !MeasurementGeometryHelper.TryParsePoint(raw, out point))
        {
            return false;
        }

        sigmaPx = MeasurementGeometryHelper.EstimateAnglePointSigma(raw, point);
        return true;
    }

    private static bool TryGetLine(Dictionary<string, object>? inputs, string key, out LineData line)
    {
        line = new LineData();
        return inputs != null &&
               inputs.TryGetValue(key, out var raw) &&
               MeasurementGeometryHelper.TryParseLine(raw, out line);
    }

    private static bool TryParseDistanceModel(string raw, out DistanceModel model)
    {
        model = DistanceModel.Segment;
        if (raw.Equals("Segment", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (raw.Equals("InfiniteLine", StringComparison.OrdinalIgnoreCase))
        {
            model = DistanceModel.InfiniteLine;
            return true;
        }

        return false;
    }

    private static bool TryParseAngleUnit(string raw, out AngleUnit unit)
    {
        unit = AngleUnit.Degree;
        if (raw.Equals("Degree", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (raw.Equals("Radian", StringComparison.OrdinalIgnoreCase))
        {
            unit = AngleUnit.Radian;
            return true;
        }

        return false;
    }

    private static double ComputeConfidence(double uncertainty)
    {
        return !double.IsFinite(uncertainty) || uncertainty < 0.0
            ? 0.0
            : 1.0 / (1.0 + uncertainty);
    }

    private static double ComputeAngleConfidence(double uncertaintyDeg)
    {
        return !double.IsFinite(uncertaintyDeg) || uncertaintyDeg < 0.0
            ? 0.0
            : Math.Clamp(1.0 / (1.0 + (uncertaintyDeg * 4.0)), 0.0, 1.0);
    }

    private static void DrawLineDistance(Mat image, Position start, Position end, string label)
    {
        var p1 = ToCvPoint(start);
        var p2 = ToCvPoint(end);
        Cv2.Line(image, p1, p2, new Scalar(0, 255, 0), 2);
        Cv2.Circle(image, p1, 5, new Scalar(255, 0, 0), -1);
        Cv2.Circle(image, p2, 5, new Scalar(255, 0, 0), -1);
        Cv2.PutText(image, label, new Point(((p1.X + p2.X) / 2) + 6, ((p1.Y + p2.Y) / 2) - 6), HersheyFonts.HersheySimplex, 0.7, new Scalar(0, 0, 255), 2);
    }

    private static Point ToCvPoint(Position point) => ToCvPoint(point.X, point.Y);

    private static Point ToCvPoint(double x, double y) =>
        new((int)Math.Round(x), (int)Math.Round(y));

    private enum DistanceModel
    {
        Segment = 0,
        InfiniteLine = 1
    }

    private enum AngleUnit
    {
        Degree = 0,
        Radian = 1
    }
}
