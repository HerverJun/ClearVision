// ArcCaliperOperator.cs
// 圆弧卡尺算子 - 沿圆弧路径扫描边缘点
// 对标 Halcon: measure_pos on arc

using ClearVision.Product.Core.Attributes;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Operators;
using Microsoft.Extensions.Logging;
using OpenCvSharp;

namespace ClearVision.Product.Infrastructure.Operators;

[OperatorMeta(
    DisplayName = "圆弧卡尺",
    Description = "沿圆弧路径检测边缘，支持亚像素精度。",
    CategoryId = OperatorCategoryId.Measurement,
    IconName = "arc-caliper",
    Keywords = new[] { "Caliper", "Arc", "Edge", "Measurement", "Circle" },
    Version = "1.0.1"
)]
[AlgorithmInfo(
    Name = "Radial band-profile arc edge scan",
    CoreApi = "arc sampling -> IndustrialCaliperKernel.SampleBandProfile -> DetectEdges -> InterpolatePosition",
    ImplementationStrategy = "Samples one radial band profile per arc angle, applies polarity-aware edge detection on the profile, and converts the strongest edge position back to subpixel image coordinates.",
    TimeComplexity = "O(A*S)",
    TypicalLatency = "Avg 4.169 ms, max 5.747 ms over 31 synthetic golden cases",
    SpaceComplexity = "O(S+P)",
    SuitableUseCases = new[] { "Measuring circular or annular edges when center, radius, and angular search span are already constrained." },
    UnsuitableUseCases = new[] { "Discovering unknown circles without a prior center/radius estimate.", "Low-texture arcs where no edge response should be treated as a low-confidence measurement." },
    KnownLimitations = new[] { "The current scan step is fixed at one degree, so very short arcs may need a tighter dedicated measurement operator.", "The output reports detected points and count, but does not yet expose per-point uncertainty or an explicit no-edge failure status." }
)]
[InputPort("Image", "Input Image", PortDataType.Image, IsRequired = true)]
[InputPort("CenterX", "Arc Center X", PortDataType.Integer, IsRequired = true)]
[InputPort("CenterY", "Arc Center Y", PortDataType.Integer, IsRequired = true)]
[InputPort("Radius", "Arc Radius", PortDataType.Integer, IsRequired = true)]
[InputPort("StartAngle", "Start Angle (deg)", PortDataType.Float, IsRequired = false)]
[InputPort("EndAngle", "End Angle (deg)", PortDataType.Float, IsRequired = false)]
[InputPort("Transition", "Transition Type", PortDataType.String, IsRequired = false)]
[OutputPort("Points", "Detected Edge Points", PortDataType.Any)]
[OutputPort("Image", "Visualization", PortDataType.Image)]
public class ArcCaliperOperator : OperatorBase
{
    public override OperatorType OperatorType => OperatorType.ArcCaliper;

    public ArcCaliperOperator(ILogger<ArcCaliperOperator> logger) : base(logger) { }

    protected override Task<OperatorExecutionOutput> ExecuteCoreAsync(Operator @operator, Dictionary<string, object>? inputs, CancellationToken cancellationToken)
    {
        if (!TryGetInputImage(inputs, "Image", out var imageWrapper) || imageWrapper == null)
            return Task.FromResult(OperatorExecutionOutput.Failure("Image required."));

        var image = imageWrapper.GetMat();
        if (image.Empty())
            return Task.FromResult(OperatorExecutionOutput.Failure("Input image is empty."));

        int cx = GetInt(inputs, "CenterX", image.Width / 2);
        int cy = GetInt(inputs, "CenterY", image.Height / 2);
        int radius = GetInt(inputs, "Radius", Math.Min(image.Width, image.Height) / 4);
        double startAngle = GetDouble(inputs, "StartAngle", 0);
        double endAngle = GetDouble(inputs, "EndAngle", 360);
        string transition = GetString(inputs, "Transition", "all").ToLower();

        if (radius <= 0)
            return Task.FromResult(OperatorExecutionOutput.Failure("Radius must be greater than zero."));

        if (!double.IsFinite(startAngle) || !double.IsFinite(endAngle))
            return Task.FromResult(OperatorExecutionOutput.Failure("StartAngle and EndAngle must be finite."));

        double normalizedStartAngle = NormalizeAngleDegrees(startAngle);
        double arcSpan = ComputePositiveArcSpanDegrees(startAngle, endAngle);
        if (arcSpan <= 1e-6)
            return Task.FromResult(OperatorExecutionOutput.Failure("Arc span must be greater than zero."));

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        using var gray = image.Channels() == 3 ? image.CvtColor(ColorConversionCodes.BGR2GRAY) : image.Clone();

        var points = new List<ArcCaliperPoint>();
        const double angleStep = 1.0;
        int steps = Math.Max(1, (int)Math.Ceiling(arcSpan / angleStep));
        int accessibleSamples = 0;

        for (int i = 0; i <= steps; i++)
        {
            double angle = normalizedStartAngle + (arcSpan * i / steps);
            double rad = angle * Math.PI / 180.0;
            double sampleX = cx + radius * Math.Cos(rad);
            double sampleY = cy + radius * Math.Sin(rad);

            if (IsWithinSamplingRegion(gray, sampleX, sampleY))
            {
                accessibleSamples++;
            }

            if (TryLocateArcEdge(gray, cx, cy, radius, rad, transition, out double subpixX, out double subpixY, out double contrast))
            {
                points.Add(new ArcCaliperPoint
                {
                    X = subpixX,
                    Y = subpixY,
                    Angle = angle,
                    Radius = radius,
                    Contrast = contrast
                });
            }
        }

        if (accessibleSamples == 0)
            return Task.FromResult(OperatorExecutionOutput.Failure("Arc path lies outside the valid sampling region."));

        stopwatch.Stop();

        var vis = CreateVisualization(image, cx, cy, radius, normalizedStartAngle, normalizedStartAngle + arcSpan, points);

        return Task.FromResult(OperatorExecutionOutput.Success(CreateImageOutput(vis, new Dictionary<string, object>
        {
            { "Points", points },
            { "Count", points.Count },
            { "AverageContrast", points.Count > 0 ? points.Average(p => p.Contrast) : 0 },
            { "ProcessingTimeMs", stopwatch.ElapsedMilliseconds }
        })));
    }

    private bool TryLocateArcEdge(
        Mat gray,
        int cx,
        int cy,
        int radius,
        double angle,
        string transition,
        out double subpixX,
        out double subpixY,
        out double contrast)
    {
        subpixX = cx + radius * Math.Cos(angle);
        subpixY = cy + radius * Math.Sin(angle);
        contrast = 0;

        double dx = Math.Cos(angle);
        double dy = Math.Sin(angle);
        double px = subpixX;
        double py = subpixY;

        if (!IsWithinSamplingRegion(gray, px, py))
        {
            return false;
        }

        const double searchHalfLength = 6.0;
        const double averagingThickness = 5.0;
        const int sampleCount = 33;
        var start = new Point2d(px - (dx * searchHalfLength), py - (dy * searchHalfLength));
        var end = new Point2d(px + (dx * searchHalfLength), py + (dy * searchHalfLength));
        var profile = IndustrialCaliperKernel.SampleBandProfile(gray, start, end, averagingThickness, sampleCount);
        var threshold = Math.Max(6.0, IndustrialCaliperKernel.EstimateEdgeThreshold(profile, minimumThreshold: 4.0));
        var polarity = transition switch
        {
            "positive" => "DarkToLight",
            "negative" => "LightToDark",
            _ => "Both"
        };

        var edges = IndustrialCaliperKernel.DetectEdges(profile, threshold, polarity, sigma: 1.2);
        if (edges.Count == 0)
        {
            return false;
        }

        var bestEdge = edges.OrderByDescending(edge => edge.Strength).First();
        var point = IndustrialCaliperKernel.InterpolatePosition(start, end, bestEdge.Position, sampleCount);
        subpixX = point.X;
        subpixY = point.Y;
        contrast = bestEdge.Strength;
        return true;
    }

    private static bool IsWithinSamplingRegion(Mat gray, double px, double py)
    {
        return px >= 6 && px < gray.Width - 6 && py >= 6 && py < gray.Height - 6;
    }

    private static double NormalizeAngleDegrees(double angle)
    {
        var normalized = angle % 360.0;
        return normalized < 0 ? normalized + 360.0 : normalized;
    }

    private static double ComputePositiveArcSpanDegrees(double startAngle, double endAngle)
    {
        const double epsilon = 1e-6;
        var rawSpan = endAngle - startAngle;
        if (Math.Abs(rawSpan) <= epsilon)
        {
            return 0.0;
        }

        if (Math.Abs(rawSpan) > 360.0)
        {
            rawSpan %= 360.0;
            if (Math.Abs(rawSpan) <= epsilon)
            {
                return 360.0;
            }
        }

        while (rawSpan < 0.0)
        {
            rawSpan += 360.0;
        }

        return rawSpan;
    }

    private Mat CreateVisualization(Mat image, int cx, int cy, int radius, double start, double end, List<ArcCaliperPoint> points)
    {
        var vis = image.Channels() == 1 ? image.CvtColor(ColorConversionCodes.GRAY2BGR) : image.Clone();

        // 绘制圆弧
        double arcStart = Math.Min(start, end) * Math.PI / 180;
        double arcEnd = Math.Max(start, end) * Math.PI / 180;
        Cv2.Ellipse(vis, new Point(cx, cy), new Size(radius, radius), 0, start, end, new Scalar(0, 255, 255), 1);

        // 绘制中心
        Cv2.Circle(vis, new Point(cx, cy), 3, new Scalar(0, 0, 255), -1);

        // 绘制检测点
        foreach (var pt in points)
        {
            Cv2.Circle(vis, new Point((int)pt.X, (int)pt.Y), 3, new Scalar(0, 255, 0), -1);
        }

        Cv2.PutText(vis, $"Edges: {points.Count}", new Point(10, 30), HersheyFonts.HersheySimplex, 0.7, new Scalar(0, 255, 0), 2);
        return vis;
    }

    private int GetInt(Dictionary<string, object>? inputs, string key, int defaultVal) =>
        inputs?.TryGetValue(key, out var v) == true && v is int i ? i : defaultVal;

    private double GetDouble(Dictionary<string, object>? inputs, string key, double defaultVal) =>
        inputs?.TryGetValue(key, out var v) == true ? Convert.ToDouble(v) : defaultVal;

    private string GetString(Dictionary<string, object>? inputs, string key, string defaultVal) =>
        inputs?.TryGetValue(key, out var v) == true ? v?.ToString() ?? defaultVal : defaultVal;

    public override ValidationResult ValidateParameters(Operator @operator) => ValidationResult.Valid();
}

public class ArcCaliperPoint
{
    public double X { get; set; }
    public double Y { get; set; }
    public double Angle { get; set; }
    public double Radius { get; set; }
    public double Contrast { get; set; }
}
