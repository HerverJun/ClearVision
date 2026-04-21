using Acme.Product.Core.Attributes;
using Acme.Product.Core.Entities;
using Acme.Product.Core.Enums;
using Acme.Product.Core.Operators;
using Microsoft.Extensions.Logging;
using OpenCvSharp;

namespace Acme.Product.Infrastructure.Operators;

[OperatorMeta(
    DisplayName = "Contour Extrema",
    Description = "Finds extremal points of a contour in specified directions.",
    Category = "Measurement",
    IconName = "contour-extrema",
    Keywords = new[] { "Contour", "Extrema", "Min", "Max", "Boundary" }
)]
[InputPort("Contour", "Input Contour (Points)", PortDataType.Any, IsRequired = true)]
[InputPort("Direction", "Search Direction", PortDataType.String, IsRequired = false)]
[InputPort("ReferencePoint", "Reference Point (optional)", PortDataType.Any, IsRequired = false)]
[OutputPort("ExtremaPoints", "Extremal Points", PortDataType.Any)]
[OutputPort("MinPoint", "Minimum Point", PortDataType.Any)]
[OutputPort("MaxPoint", "Maximum Point", PortDataType.Any)]
[OutputPort("Image", "Visualization", PortDataType.Image)]
[OutputPort("MinValue", "Minimum Value", PortDataType.Float)]
[OutputPort("MaxValue", "Maximum Value", PortDataType.Float)]
public class ContourExtremaOperator : OperatorBase
{
    public override OperatorType OperatorType => OperatorType.ContourExtrema;

    public ContourExtremaOperator(ILogger<ContourExtremaOperator> logger) : base(logger) { }

    protected override Task<OperatorExecutionOutput> ExecuteCoreAsync(Operator @operator, Dictionary<string, object>? inputs, CancellationToken cancellationToken)
    {
        if (!TryGetContour(inputs, out var contour) || contour == null || contour.Count == 0)
            return Task.FromResult(OperatorExecutionOutput.Failure("Contour must contain at least one point."));

        string direction = GetString(inputs, "Direction", "horizontal").ToLowerInvariant();

        Point2f? refPoint = null;
        if (inputs?.TryGetValue("ReferencePoint", out var rp) == true)
        {
            if (rp is Point2f p2f)
            {
                refPoint = p2f;
            }
            else if (rp is Point p)
            {
                refPoint = new Point2f(p.X, p.Y);
            }
        }

        if (direction == "distance" && !refPoint.HasValue)
            return Task.FromResult(OperatorExecutionOutput.Failure("ReferencePoint is required when Direction is 'distance'."));

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var results = ComputeExtrema(contour, direction, refPoint);
        stopwatch.Stop();

        int padding = 50;
        var bbox = Cv2.BoundingRect(contour.Select(ToPoint).ToArray());
        int w = Math.Max(400, bbox.Width + padding * 2);
        int h = Math.Max(300, bbox.Height + padding * 2);

        var vis = new Mat(h, w, MatType.CV_8UC3, Scalar.Black);
        var shiftedContour = contour
            .Select(p => new Point((int)Math.Round(p.X - bbox.X + padding), (int)Math.Round(p.Y - bbox.Y + padding)))
            .ToArray();

        if (shiftedContour.Length > 1)
        {
            Cv2.Polylines(vis, new[] { shiftedContour }, false, Scalar.White, 2);
        }
        else
        {
            Cv2.Circle(vis, shiftedContour[0], 3, Scalar.White, -1);
        }

        var minPt = results.MinPoint;
        var maxPt = results.MaxPoint;
        var shiftedMin = new Point((int)Math.Round(minPt.X - bbox.X + padding), (int)Math.Round(minPt.Y - bbox.Y + padding));
        var shiftedMax = new Point((int)Math.Round(maxPt.X - bbox.X + padding), (int)Math.Round(maxPt.Y - bbox.Y + padding));

        Cv2.Circle(vis, shiftedMin, 6, new Scalar(0, 0, 255), -1);
        Cv2.Circle(vis, shiftedMax, 6, new Scalar(0, 255, 0), -1);
        Cv2.PutText(vis, "MIN", new Point(shiftedMin.X + 8, shiftedMin.Y), HersheyFonts.HersheySimplex, 0.5, new Scalar(0, 0, 255), 1);
        Cv2.PutText(vis, "MAX", new Point(shiftedMax.X + 8, shiftedMax.Y), HersheyFonts.HersheySimplex, 0.5, new Scalar(0, 255, 0), 1);

        if (refPoint.HasValue)
        {
            var shiftedRef = new Point(
                (int)Math.Round(refPoint.Value.X - bbox.X + padding),
                (int)Math.Round(refPoint.Value.Y - bbox.Y + padding));
            Cv2.Circle(vis, shiftedRef, 5, new Scalar(255, 0, 255), -1);
            Cv2.Line(vis, shiftedRef, shiftedMin, new Scalar(0, 0, 255), 1, LineTypes.Link8);
            Cv2.Line(vis, shiftedRef, shiftedMax, new Scalar(0, 255, 0), 1, LineTypes.Link8);
        }

        return Task.FromResult(OperatorExecutionOutput.Success(CreateImageOutput(vis, new Dictionary<string, object>
        {
            { "ExtremaPoints", results.AllExtrema },
            { "MinPoint", results.MinPoint },
            { "MaxPoint", results.MaxPoint },
            { "MinValue", results.MinValue },
            { "MaxValue", results.MaxValue },
            { "ProcessingTimeMs", stopwatch.ElapsedMilliseconds }
        })));
    }

    private static ExtremaResult ComputeExtrema(List<Point2f> contour, string direction, Point2f? refPoint)
    {
        var values = contour
            .Select(pt => (Point: pt, Value: GetExtremaValue(pt, direction, refPoint)))
            .ToList();

        var minPoint = OrderExtrema(values, direction, descending: false).First();
        var maxPoint = OrderExtrema(values, direction, descending: true).First();

        return new ExtremaResult
        {
            MinPoint = minPoint.Point,
            MaxPoint = maxPoint.Point,
            MinValue = minPoint.Value,
            MaxValue = maxPoint.Value,
            AllExtrema = AreSamePoint(minPoint.Point, maxPoint.Point)
                ? new List<Point2f> { minPoint.Point }
                : new List<Point2f> { minPoint.Point, maxPoint.Point }
        };
    }

    private static double GetExtremaValue(Point2f pt, string direction, Point2f? refPoint)
    {
        return direction switch
        {
            "horizontal" or "x" => pt.X,
            "vertical" or "y" => pt.Y,
            "distance" when refPoint.HasValue => Distance(pt, refPoint.Value),
            _ => pt.X
        };
    }

    private static IOrderedEnumerable<(Point2f Point, double Value)> OrderExtrema(
        IEnumerable<(Point2f Point, double Value)> values,
        string direction,
        bool descending)
    {
        return direction switch
        {
            "vertical" or "y" => descending
                ? values.OrderByDescending(v => v.Value).ThenByDescending(v => v.Point.X).ThenByDescending(v => v.Point.Y)
                : values.OrderBy(v => v.Value).ThenBy(v => v.Point.X).ThenBy(v => v.Point.Y),
            "distance" => descending
                ? values.OrderByDescending(v => v.Value).ThenByDescending(v => v.Point.X).ThenByDescending(v => v.Point.Y)
                : values.OrderBy(v => v.Value).ThenBy(v => v.Point.X).ThenBy(v => v.Point.Y),
            _ => descending
                ? values.OrderByDescending(v => v.Value).ThenByDescending(v => v.Point.Y).ThenByDescending(v => v.Point.X)
                : values.OrderBy(v => v.Value).ThenBy(v => v.Point.Y).ThenBy(v => v.Point.X)
        };
    }

    private static bool AreSamePoint(Point2f a, Point2f b)
    {
        return Math.Abs(a.X - b.X) < 1e-6f && Math.Abs(a.Y - b.Y) < 1e-6f;
    }

    private static double Distance(Point2f a, Point2f b)
    {
        return Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
    }

    private bool TryGetContour(Dictionary<string, object>? inputs, out List<Point2f>? contour)
    {
        contour = null;
        if (inputs?.TryGetValue("Contour", out var val) != true || val == null)
            return false;

        if (val is IEnumerable<Point2f> pts2f) { contour = pts2f.ToList(); return true; }
        if (val is IEnumerable<Point> pts) { contour = pts.Select(p => new Point2f(p.X, p.Y)).ToList(); return true; }
        if (val is Point[] arr) { contour = arr.Select(p => new Point2f(p.X, p.Y)).ToList(); return true; }
        if (val is Point2f[] arr2f) { contour = arr2f.ToList(); return true; }
        return false;
    }

    private static Point ToPoint(Point2f point)
    {
        return new Point((int)Math.Round(point.X), (int)Math.Round(point.Y));
    }

    private string GetString(Dictionary<string, object>? inputs, string key, string defaultVal) =>
        inputs?.TryGetValue(key, out var v) == true ? v?.ToString() ?? defaultVal : defaultVal;

    public override ValidationResult ValidateParameters(Operator @operator) => ValidationResult.Valid();
}

public class ExtremaResult
{
    public Point2f MinPoint { get; set; }
    public Point2f MaxPoint { get; set; }
    public double MinValue { get; set; }
    public double MaxValue { get; set; }
    public List<Point2f> AllExtrema { get; set; } = new();
}
