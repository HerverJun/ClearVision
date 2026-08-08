using ClearVision.Product.Core.Attributes;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Operators;
using ClearVision.Product.Core.ValueObjects;
using Microsoft.Extensions.Logging;
using OpenCvSharp;

namespace ClearVision.Product.Infrastructure.Operators;

[OperatorMeta(
    DisplayName = "角度测量",
    Description = "通过三点或两线计算角度，兼容亚像素级输入。",
    CategoryId = OperatorCategoryId.Measurement,
    IconName = "angle-measure",
    Keywords = new[] { "Angle", "ThreePoint", "LineAngle", "Degree", "Radian" }
)]
[InputPort("Image", "Input Image", PortDataType.Image, IsRequired = true)]
[InputPort("Point1", "Point 1", PortDataType.Point, IsRequired = false)]
[InputPort("Point2", "Point 2", PortDataType.Point, IsRequired = false)]
[InputPort("Point3", "Point 3", PortDataType.Point, IsRequired = false)]
[InputPort("Line1", "Line 1", PortDataType.LineData, IsRequired = false)]
[InputPort("Line2", "Line 2", PortDataType.LineData, IsRequired = false)]
[OutputPort("Image", "Result Image", PortDataType.Image)]
[OutputPort("Angle", "Angle", PortDataType.Float)]
[OutputPort("Vertex", "Vertex", PortDataType.Point)]
[OperatorParam("Point1X", "Point1 X", "int", DefaultValue = 0)]
[OperatorParam("Point1Y", "Point1 Y", "int", DefaultValue = 0)]
[OperatorParam("Point2X", "Point2 X", "int", DefaultValue = 100)]
[OperatorParam("Point2Y", "Point2 Y", "int", DefaultValue = 100)]
[OperatorParam("Point3X", "Point3 X", "int", DefaultValue = 200)]
[OperatorParam("Point3Y", "Point3 Y", "int", DefaultValue = 0)]
[OperatorParam("Unit", "Angle Unit", "enum", DefaultValue = "Degree", Options = new[] { "Degree|Degree", "Radian|Radian" })]
public class AngleMeasurementOperator : OperatorBase
{
    public override OperatorType OperatorType => OperatorType.AngleMeasurement;

    public AngleMeasurementOperator(ILogger<AngleMeasurementOperator> logger) : base(logger)
    {
    }

    protected override Task<OperatorExecutionOutput> ExecuteCoreAsync(
        Operator @operator,
        Dictionary<string, object>? inputs,
        CancellationToken cancellationToken)
    {
        if (!TryGetInputImage(inputs, "Image", out var imageWrapper) || imageWrapper == null)
        {
            return Task.FromResult(OperatorExecutionOutput.Failure("Input image is required."));
        }

        var src = imageWrapper.GetMat();
        if (src.Empty())
        {
            return Task.FromResult(OperatorExecutionOutput.Failure("Input image is invalid."));
        }

        var unit = GetStringParam(@operator, "Unit", "Degree");
        if (!TryResolveAngleGeometry(@operator, inputs, out var geometry, out var geometryError))
        {
            return Task.FromResult(OperatorExecutionOutput.Failure(geometryError));
        }

        var v1x = geometry.Point1.X - geometry.Point2.X;
        var v1y = geometry.Point1.Y - geometry.Point2.Y;
        var v2x = geometry.Point3.X - geometry.Point2.X;
        var v2y = geometry.Point3.Y - geometry.Point2.Y;
        var len1 = Math.Sqrt(v1x * v1x + v1y * v1y);
        var len2 = Math.Sqrt(v2x * v2x + v2y * v2y);
        if (len1 < 1e-9 || len2 < 1e-9)
        {
            return Task.FromResult(OperatorExecutionOutput.Failure("[DegenerateGeometry] Angle vertex has zero-length arm"));
        }

        var angleRad = geometry.HasLineOverlay && geometry.Line1 != null && geometry.Line2 != null
            ? MeasurementGeometryHelper.AngleBetweenLineDirections(geometry.Line1, geometry.Line2) * Math.PI / 180.0
            : MeasurementGeometryHelper.ThreePointAngleRadians(
                geometry.Point1,
                geometry.Point2,
                geometry.Point3);
        var angle = unit.Equals("Radian", StringComparison.OrdinalIgnoreCase)
            ? angleRad
            : angleRad * 180.0 / Math.PI;
        var uncertaintyDeg = MeasurementGeometryHelper.PropagateThreePointAngleUncertaintyDegrees(
            geometry.Point1,
            geometry.Point1SigmaPx,
            geometry.Point2,
            geometry.VertexSigmaPx,
            geometry.Point3,
            geometry.Point3SigmaPx);

        var resultImage = src.Clone();
        if (geometry.HasLineOverlay)
        {
            DrawLineGeometry(resultImage, geometry, angle, unit);
        }
        else
        {
            DrawPointGeometry(resultImage, geometry.Point1, geometry.Point2, geometry.Point3, angle, unit);
        }

        var output = CreateImageOutput(resultImage, new Dictionary<string, object>
        {
            { "Angle", angle },
            { "Unit", unit },
            { "Vertex", geometry.VertexPosition },
            { "InputMode", geometry.SourceMode },
            { "StatusCode", "OK" },
            { "StatusMessage", "Success" },
            { "Confidence", ComputeConfidence(uncertaintyDeg) },
            { "UncertaintyPx", geometry.AveragePointSigmaPx },
            { "UncertaintyDeg", uncertaintyDeg }
        });

        return Task.FromResult(OperatorExecutionOutput.Success(output));
    }

    public override ValidationResult ValidateParameters(Operator @operator)
    {
        var unit = GetStringParam(@operator, "Unit", "Degree");
        if (!unit.Equals("Degree", StringComparison.OrdinalIgnoreCase) &&
            !unit.Equals("Radian", StringComparison.OrdinalIgnoreCase))
        {
            return ValidationResult.Invalid("Unit must be Degree or Radian.");
        }

        return ValidationResult.Valid();
    }

    private bool TryResolveAngleGeometry(
        Operator @operator,
        Dictionary<string, object>? inputs,
        out AngleGeometry geometry,
        out string error)
    {
        error = string.Empty;

        if (TryResolveLineMode(inputs, out geometry))
        {
            return true;
        }

        var hasPoint1 = TryResolvePoint(inputs, "Point1", out var point1, out var sigma1);
        var hasPoint2 = TryResolvePoint(inputs, "Point2", out var point2, out var sigma2);
        var hasPoint3 = TryResolvePoint(inputs, "Point3", out var point3, out var sigma3);

        geometry = new AngleGeometry(
            hasPoint1 ? point1 : new Position(GetIntParam(@operator, "Point1X", 0), GetIntParam(@operator, "Point1Y", 0)),
            hasPoint2 ? point2 : new Position(GetIntParam(@operator, "Point2X", 100), GetIntParam(@operator, "Point2Y", 100)),
            hasPoint3 ? point3 : new Position(GetIntParam(@operator, "Point3X", 200), GetIntParam(@operator, "Point3Y", 0)),
            hasPoint1 ? sigma1 : 0.5,
            hasPoint2 ? sigma2 : 0.5,
            hasPoint3 ? sigma3 : 0.5,
            null,
            null,
            hasPoint1 || hasPoint2 || hasPoint3 ? "ThreePointsInput" : "ThreePointsParameters");
        return true;
    }

    private static bool TryResolveLineMode(Dictionary<string, object>? inputs, out AngleGeometry geometry)
    {
        geometry = default;
        if (!TryResolveLine(inputs, "Line1", out var line1, out var sigma1) ||
            !TryResolveLine(inputs, "Line2", out var line2, out var sigma2))
        {
            return false;
        }

        if (!MeasurementGeometryHelper.IsFinite(line1) || !MeasurementGeometryHelper.IsFinite(line2))
        {
            return false;
        }

        var hasCross = MeasurementGeometryHelper.TryGetInfiniteLineIntersection(line1, line2, out var intersection);
        var vertex = hasCross
            ? intersection
            : new Position((line1.MidX + line2.MidX) * 0.5, (line1.MidY + line2.MidY) * 0.5);
        var point1 = new Position(line1.StartX, line1.StartY);
        var point3 = new Position(line2.EndX, line2.EndY);

        geometry = new AngleGeometry(
            point1,
            vertex,
            point3,
            sigma1,
            Math.Max(sigma1, sigma2),
            sigma2,
            line1,
            line2,
            hasCross ? "TwoLines" : "TwoLinesNoIntersection");
        return true;
    }

    private static bool TryResolvePoint(
        Dictionary<string, object>? inputs,
        string key,
        out Position point,
        out double sigmaPx)
    {
        point = new Position(0, 0);
        sigmaPx = 0.0;

        if (inputs == null || !inputs.TryGetValue(key, out var raw) || raw == null)
        {
            return false;
        }

        return TryParsePoint(raw, out point, out sigmaPx);
    }

    private static bool TryParsePoint(object raw, out Position point, out double sigmaPx)
    {
        if (!MeasurementGeometryHelper.TryParsePoint(raw, out point))
        {
            sigmaPx = 0.0;
            return false;
        }

        sigmaPx = MeasurementGeometryHelper.EstimateAnglePointSigma(raw, point);
        return true;
    }

    private static bool TryResolveLine(
        Dictionary<string, object>? inputs,
        string key,
        out LineData line,
        out double sigmaPx)
    {
        line = new LineData();
        sigmaPx = 0.0;

        if (inputs == null || !inputs.TryGetValue(key, out var raw) || raw == null)
        {
            return false;
        }

        if (!MeasurementGeometryHelper.TryParseLine(raw, out line))
        {
            return false;
        }

        sigmaPx = MeasurementGeometryHelper.EstimateLineSigma(line);
        return true;
    }

    private static double ComputeConfidence(double uncertaintyDeg)
    {
        if (!double.IsFinite(uncertaintyDeg))
        {
            return 0.0;
        }

        return Math.Clamp(1.0 / (1.0 + uncertaintyDeg * 4.0), 0.0, 1.0);
    }

    private static void DrawPointGeometry(Mat image, Position p1, Position p2, Position p3, double angle, string unit)
    {
        var point1 = ToCvPoint(p1);
        var point2 = ToCvPoint(p2);
        var point3 = ToCvPoint(p3);

        Cv2.Circle(image, point1, 5, new Scalar(0, 0, 255), -1);
        Cv2.Circle(image, point2, 5, new Scalar(0, 255, 0), -1);
        Cv2.Circle(image, point3, 5, new Scalar(255, 0, 0), -1);
        Cv2.Line(image, point1, point2, new Scalar(0, 255, 255), 2);
        Cv2.Line(image, point2, point3, new Scalar(0, 255, 255), 2);
        Cv2.PutText(
            image,
            $"Angle: {angle:F4} {unit}",
            new Point(point2.X + 8, point2.Y - 8),
            HersheyFonts.HersheySimplex,
            0.7,
            new Scalar(255, 255, 255),
            2);
    }

    private static void DrawLineGeometry(Mat image, AngleGeometry geometry, double angle, string unit)
    {
        if (geometry.Line1 == null || geometry.Line2 == null)
        {
            DrawPointGeometry(image, geometry.Point1, geometry.Point2, geometry.Point3, angle, unit);
            return;
        }

        Cv2.Line(image, ToCvPoint(geometry.Line1.StartX, geometry.Line1.StartY), ToCvPoint(geometry.Line1.EndX, geometry.Line1.EndY), new Scalar(0, 255, 255), 2);
        Cv2.Line(image, ToCvPoint(geometry.Line2.StartX, geometry.Line2.StartY), ToCvPoint(geometry.Line2.EndX, geometry.Line2.EndY), new Scalar(255, 128, 0), 2);
        var vertex = ToCvPoint(geometry.VertexPosition);
        Cv2.Circle(image, vertex, 5, new Scalar(0, 255, 0), -1);
        Cv2.PutText(
            image,
            $"Angle: {angle:F4} {unit}",
            new Point(vertex.X + 8, vertex.Y - 8),
            HersheyFonts.HersheySimplex,
            0.7,
            new Scalar(255, 255, 255),
            2);
    }

    private static Point ToCvPoint(Position position)
    {
        return ToCvPoint(position.X, position.Y);
    }

    private static Point ToCvPoint(double x, double y)
    {
        return new Point((int)Math.Round(x), (int)Math.Round(y));
    }

    private readonly record struct AngleGeometry(
        Position Point1,
        Position Point2,
        Position Point3,
        double Point1SigmaPx,
        double VertexSigmaPx,
        double Point3SigmaPx,
        LineData? Line1,
        LineData? Line2,
        string SourceMode)
    {
        public bool HasLineOverlay => Line1 != null && Line2 != null;
        public Position VertexPosition => Point2;
        public double AveragePointSigmaPx => (Point1SigmaPx + VertexSigmaPx + Point3SigmaPx) / 3.0;
    }
}
