using System.Diagnostics;
using System.Text.Json;
using Acme.Product.Core.Entities;
using Acme.Product.Core.Enums;
using Acme.Product.Core.Operators;
using Acme.Product.Core.ValueObjects;
using Acme.Product.Infrastructure.Operators;
using Microsoft.Extensions.Logging.Abstractions;
using OpenCvSharp;

var options = RunnerOptions.Parse(args);
if (options.ShowHelp)
{
    RunnerOptions.PrintHelp();
    return options.ParseError is null ? 0 : 2;
}

if (options.ParseError is not null)
{
    Console.Error.WriteLine(options.ParseError);
    RunnerOptions.PrintHelp();
    return 2;
}

var result = await MeasurementGeometryOracleRunner.RunAsync(options);
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(options.OutputPath))!);
await File.WriteAllTextAsync(options.OutputPath, JsonSerializer.Serialize(result, JsonSettings.Indented));

if (!string.IsNullOrWhiteSpace(options.ReportPath))
{
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(options.ReportPath))!);
    await File.WriteAllTextAsync(options.ReportPath, MarkdownReport.Create(result));
}

Console.WriteLine(
    $"Measurement geometry oracle complete: accepted={result.Accepted}, " +
    $"{result.Summary.Passed}/{result.Summary.CaseCount} passed, p95PixelError={result.Summary.P95PixelErrorPx:0.###}, output={options.OutputPath}");

return result.Accepted ? 0 : 1;

internal static class MeasurementGeometryOracleRunner
{
    private const int CasesPerOperator = 300;
    private const int BoundaryCasesPerOperator = 40;
    private static readonly string[] BoundaryScenarios =
    [
        "blur",
        "noise",
        "low_contrast",
        "partial_edge",
        "polarity_flip",
        "subpixel_offset",
        "outlier_contour",
        "occlusion"
    ];

    public static async Task<OracleResult> RunAsync(RunnerOptions options)
    {
        var cases = new List<CaseResult>(CasesPerOperator * 5);
        for (var i = 0; i < CasesPerOperator; i++)
        {
            cases.Add(await RunCaliperCaseAsync(i));
            cases.Add(await RunArcCaliperCaseAsync(i));
            cases.Add(await RunLineCaseAsync(i));
            cases.Add(await RunCircleCaseAsync(i));
            cases.Add(await RunGeometricFittingCaseAsync(i));
        }

        var operators = cases
            .GroupBy(item => item.Operator)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => BuildOperatorSummary(group.Key, group.ToList()))
            .ToArray();

        var pixelErrors = cases
            .Where(item => item.PixelErrorPx.HasValue)
            .Select(item => item.PixelErrorPx!.Value);
        var angleErrors = cases
            .Where(item => item.AngleErrorDeg.HasValue)
            .Select(item => item.AngleErrorDeg!.Value);
        var failed = cases.Count(item => !item.Passed);
        var passRate = cases.Count == 0 ? 0 : (cases.Count - failed) / (double)cases.Count;
        var p95Pixel = Percentile(pixelErrors, 0.95);
        var p95Angle = Percentile(angleErrors, 0.95);
        var accepted = operators.All(item => item.Accepted);

        return new OracleResult(
            "2026-04-30.measurement-geometry-oracle.v1",
            DateTimeOffset.UtcNow,
            accepted,
            new ClaimBoundary(
                "This report is semisynthetic geometry-oracle evidence for measurement operators.",
                "It is not real production-site validation or sign-off.",
                "Boundary samples are stress cases over blur, noise, contrast, partial edges, polarity, subpixel offset, outliers, and occlusion."),
            new OracleSummary(
                cases.Count,
                cases.Count - failed,
                failed,
                Math.Round(passRate, 6),
                cases.Count(item => item.IsBoundary),
                0,
                Math.Round(p95Pixel, 6),
                Math.Round(p95Angle, 6),
                Math.Round(cases.Sum(item => item.RuntimeMs), 3)),
            operators,
            cases);
    }

    private static OperatorSummary BuildOperatorSummary(string operatorName, IReadOnlyList<CaseResult> cases)
    {
        var failed = cases.Count(item => !item.Passed);
        var passRate = cases.Count == 0 ? 0 : (cases.Count - failed) / (double)cases.Count;
        var p95Pixel = Percentile(cases.Where(item => item.PixelErrorPx.HasValue).Select(item => item.PixelErrorPx!.Value), 0.95);
        var p95Angle = Percentile(cases.Where(item => item.AngleErrorDeg.HasValue).Select(item => item.AngleErrorDeg!.Value), 0.95);
        var accepted = passRate >= 0.98 && p95Pixel <= 1.5;
        if (operatorName is "LineMeasurement" or "GeometricFitting")
        {
            accepted = passRate >= 0.98 && p95Pixel <= 1.5 && p95Angle <= 2.0;
        }

        return new OperatorSummary(
            operatorName,
            cases.Count,
            cases.Count(item => item.IsBoundary),
            cases.Count - failed,
            failed,
            Math.Round(passRate, 6),
            Math.Round(p95Pixel, 6),
            Math.Round(p95Angle, 6),
            Math.Round(cases.Average(item => item.RuntimeMs), 3),
            accepted);
    }

    private static async Task<CaseResult> RunCaliperCaseAsync(int index)
    {
        var scenario = ScenarioFor(index);
        var isBoundary = index < BoundaryCasesPerOperator;
        var width = 180;
        var height = 120;
        var stripeWidth = 18 + (index % 42);
        var x1 = 45 + (index % 34);
        var x2 = x1 + stripeWidth;
        var dark = scenario == "low_contrast" ? 70 : 20;
        var light = scenario == "low_contrast" ? 170 : 235;
        var polarityFlip = scenario == "polarity_flip";

        using var image = new Mat(height, width, MatType.CV_8UC3, polarityFlip ? new Scalar(light, light, light) : new Scalar(dark, dark, dark));
        var stripeColor = polarityFlip ? new Scalar(dark, dark, dark) : new Scalar(light, light, light);
        Cv2.Rectangle(image, new Rect(x1, 18, stripeWidth, height - 36), stripeColor, -1);
        ApplyStress(image, scenario, index);

        var op = CreateOperator(
            OperatorType.CaliperTool,
            ("Direction", "Horizontal"),
            ("Polarity", "Both"),
            ("ExpectedCount", 1),
            ("EdgeThreshold", scenario == "low_contrast" ? 6.0 : 10.0),
            ("SubpixelAccuracy", true),
            ("PairDirection", "any"));
        using var wrapper = new ImageWrapper(image.Clone());
        var inputs = new Dictionary<string, object>
        {
            ["Image"] = wrapper,
            ["SearchRegion"] = scenario == "occlusion"
                ? new Rect(8, 20, width - 16, 18)
                : new Rect(8, 34, width - 16, 52)
        };

        var sut = new CaliperToolOperator(NullLogger<CaliperToolOperator>.Instance);
        var stopwatch = Stopwatch.StartNew();
        var allocationBefore = GC.GetTotalAllocatedBytes(precise: true);
        var output = await sut.ExecuteAsync(op, inputs);
        var allocationAfter = GC.GetTotalAllocatedBytes(precise: true);
        stopwatch.Stop();

        var actualWidth = TryGetDouble(output.OutputData, "Width", out var value) ? value : double.NaN;
        var pixelError = Math.Abs(actualWidth - stripeWidth);
        var passed = output.IsSuccess && double.IsFinite(pixelError) && pixelError <= 1.5;
        ReleaseImageOutputs(output.OutputData);

        return new CaseResult(
            $"CaliperTool_oracle_{index:000}",
            "CaliperTool",
            scenario,
            isBoundary,
            passed,
            Math.Round(stopwatch.Elapsed.TotalMilliseconds, 3),
            Math.Max(0, allocationAfter - allocationBefore),
            RoundNullable(pixelError),
            null,
            Math.Round((double)stripeWidth, 6),
            RoundValue(actualWidth),
            passed ? null : output.ErrorMessage ?? $"widthError={pixelError:0.###}");
    }

    private static async Task<CaseResult> RunArcCaliperCaseAsync(int index)
    {
        var scenario = ScenarioFor(index);
        var isBoundary = index < BoundaryCasesPerOperator;
        var width = 220;
        var height = 220;
        var centerX = width / 2;
        var centerY = height / 2;
        var radius = 38 + index % 34;
        var startAngle = (index * 13) % 330;
        var span = scenario == "partial_edge" ? 72.0 : 118.0 + index % 90;
        var endAngle = startAngle + span;
        var lowContrast = scenario == "low_contrast";
        var polarityFlip = scenario == "polarity_flip";
        var background = polarityFlip ? (lowContrast ? 210 : 235) : (lowContrast ? 70 : 18);
        var foreground = polarityFlip ? (lowContrast ? 70 : 18) : (lowContrast ? 210 : 238);

        using var image = new Mat(height, width, MatType.CV_8UC3, new Scalar(background, background, background));
        Cv2.Circle(image, new Point(centerX, centerY), radius, new Scalar(foreground, foreground, foreground), -1, LineTypes.AntiAlias);
        ApplyStress(image, scenario, index);

        var transition = polarityFlip ? "positive" : "negative";
        var sut = new ArcCaliperOperator(NullLogger<ArcCaliperOperator>.Instance);
        var stopwatch = Stopwatch.StartNew();
        var allocationBefore = GC.GetTotalAllocatedBytes(precise: true);
        using var wrapper = new ImageWrapper(image.Clone());
        var output = await sut.ExecuteAsync(
            CreateOperator(OperatorType.ArcCaliper),
            new Dictionary<string, object>
            {
                ["Image"] = wrapper,
                ["CenterX"] = centerX,
                ["CenterY"] = centerY,
                ["Radius"] = radius,
                ["StartAngle"] = startAngle,
                ["EndAngle"] = endAngle,
                ["Transition"] = transition
            });
        var allocationAfter = GC.GetTotalAllocatedBytes(precise: true);
        stopwatch.Stop();

        var points = TryGetArcPoints(output.OutputData, out var typedPoints) ? typedPoints : [];
        var actualRadius = points.Count == 0
            ? double.NaN
            : points.Average(point => Math.Sqrt(Math.Pow(point.X - centerX, 2) + Math.Pow(point.Y - centerY, 2)));
        var pixelError = Math.Abs(actualRadius - radius);
        var minCount = Math.Max(8, (int)Math.Floor(Math.Abs(span) * 0.35));
        var passed = output.IsSuccess && points.Count >= minCount && double.IsFinite(pixelError) && pixelError <= 1.5;
        ReleaseImageOutputs(output.OutputData);

        return new CaseResult(
            $"ArcCaliper_oracle_{index:000}",
            "ArcCaliper",
            scenario,
            isBoundary,
            passed,
            Math.Round(stopwatch.Elapsed.TotalMilliseconds, 3),
            Math.Max(0, allocationAfter - allocationBefore),
            RoundNullable(pixelError),
            null,
            Math.Round((double)radius, 6),
            RoundValue(actualRadius),
            passed ? null : output.ErrorMessage ?? $"radiusError={pixelError:0.###}, points={points.Count}, min={minCount}");
    }

    private static async Task<CaseResult> RunLineCaseAsync(int index)
    {
        var scenario = ScenarioFor(index);
        var isBoundary = index < BoundaryCasesPerOperator;
        var width = 180;
        var height = 140;
        var angle = new[] { 0.0, 90.0, 45.0, 135.0 }[index % 4];
        using var image = new Mat(height, width, MatType.CV_8UC3, Scalar.Black);
        DrawLineScene(image, angle, scenario, index);
        ApplyStress(image, scenario, index);

        var op = CreateOperator(
            OperatorType.LineMeasurement,
            ("Method", "ProbabilisticHough"),
            ("Threshold", scenario == "partial_edge" ? 16 : 22),
            ("MinLength", 35.0),
            ("MaxGap", scenario == "occlusion" ? 14.0 : 8.0));
        using var wrapper = new ImageWrapper(image.Clone());
        var sut = new LineMeasurementOperator(NullLogger<LineMeasurementOperator>.Instance);

        var stopwatch = Stopwatch.StartNew();
        var allocationBefore = GC.GetTotalAllocatedBytes(precise: true);
        var output = await sut.ExecuteAsync(op, new Dictionary<string, object> { ["Image"] = wrapper });
        var allocationAfter = GC.GetTotalAllocatedBytes(precise: true);
        stopwatch.Stop();

        var actualAngle = TryGetDouble(output.OutputData, "Angle", out var angleValue) ? angleValue : double.NaN;
        var angleError = AngleError(actualAngle, angle);
        var residual = TryGetDouble(output.OutputData, "ResidualMean", out var residualValue) && double.IsFinite(residualValue)
            ? Math.Abs(residualValue)
            : 0.0;
        var passed = output.IsSuccess && double.IsFinite(angleError) && angleError <= 2.0 && residual <= 1.5;
        ReleaseImageOutputs(output.OutputData);

        return new CaseResult(
            $"LineMeasurement_oracle_{index:000}",
            "LineMeasurement",
            scenario,
            isBoundary,
            passed,
            Math.Round(stopwatch.Elapsed.TotalMilliseconds, 3),
            Math.Max(0, allocationAfter - allocationBefore),
            RoundNullable(residual),
            RoundValue(angleError),
            Math.Round(angle, 6),
            RoundValue(actualAngle),
            passed ? null : output.ErrorMessage ?? $"angleError={angleError:0.###}, residual={residual:0.###}");
    }

    private static async Task<CaseResult> RunCircleCaseAsync(int index)
    {
        var scenario = ScenarioFor(index);
        var isBoundary = index < BoundaryCasesPerOperator;
        var width = 150;
        var height = 130;
        var radius = 15 + (index % 24);
        var centerX = 42 + (index * 7 % 58);
        var centerY = 38 + (index * 5 % 48);
        centerX = Math.Clamp(centerX, radius + 8, width - radius - 8);
        centerY = Math.Clamp(centerY, radius + 8, height - radius - 8);
        using var image = new Mat(height, width, MatType.CV_8UC3, Scalar.Black);
        Cv2.Circle(image, new Point(centerX, centerY), radius, Scalar.White, -1, LineTypes.AntiAlias);
        ApplyStress(image, scenario, index);

        var op = CreateOperator(
            OperatorType.CircleMeasurement,
            ("Method", "FitEllipse"),
            ("MinRadius", Math.Max(4, radius - 8)),
            ("MaxRadius", radius + 8));
        using var wrapper = new ImageWrapper(image.Clone());
        var sut = new CircleMeasurementOperator(NullLogger<CircleMeasurementOperator>.Instance);

        var stopwatch = Stopwatch.StartNew();
        var allocationBefore = GC.GetTotalAllocatedBytes(precise: true);
        var output = await sut.ExecuteAsync(op, new Dictionary<string, object> { ["Image"] = wrapper });
        var allocationAfter = GC.GetTotalAllocatedBytes(precise: true);
        stopwatch.Stop();

        var actualRadius = TryGetDouble(output.OutputData, "Radius", out var radiusValue) ? radiusValue : double.NaN;
        var centerError = TryGetPosition(output.OutputData, "Center", out var center)
            ? Math.Sqrt(Math.Pow(center.X - centerX, 2) + Math.Pow(center.Y - centerY, 2))
            : double.NaN;
        var radiusError = Math.Abs(actualRadius - radius);
        var pixelError = Math.Max(centerError, radiusError);
        var passed = output.IsSuccess && double.IsFinite(pixelError) && pixelError <= 1.5;
        ReleaseImageOutputs(output.OutputData);

        return new CaseResult(
            $"CircleMeasurement_oracle_{index:000}",
            "CircleMeasurement",
            scenario,
            isBoundary,
            passed,
            Math.Round(stopwatch.Elapsed.TotalMilliseconds, 3),
            Math.Max(0, allocationAfter - allocationBefore),
            RoundNullable(pixelError),
            null,
            Math.Round((double)radius, 6),
            RoundValue(actualRadius),
            passed ? null : output.ErrorMessage ?? $"pixelError={pixelError:0.###}");
    }

    private static async Task<CaseResult> RunGeometricFittingCaseAsync(int index)
    {
        var scenario = ScenarioFor(index);
        var isBoundary = index < BoundaryCasesPerOperator;
        var fitType = new[] { "Line", "Circle", "Ellipse" }[index % 3];
        var width = 180;
        var height = 150;
        using var image = new Mat(height, width, MatType.CV_8UC3, Scalar.Black);

        double expectedValue;
        switch (fitType)
        {
            case "Line":
                expectedValue = new[] { 0.0, 45.0, 90.0, 135.0 }[(index / 3) % 4];
                DrawFittingLineScene(image, expectedValue, scenario == "partial_edge" ? "partial_edge" : "nominal", index);
                break;
            case "Circle":
                var radius = 20 + index % 24;
                var cx = 50 + index * 7 % 70;
                var cy = 42 + index * 5 % 54;
                cx = Math.Clamp(cx, radius + 8, width - radius - 8);
                cy = Math.Clamp(cy, radius + 8, height - radius - 8);
                expectedValue = radius;
                Cv2.Circle(image, new Point(cx, cy), radius, Scalar.White, 1, LineTypes.AntiAlias);
                break;
            default:
                var major = 26 + index % 18;
                var minor = 14 + index % 10;
                var angle = (index * 11) % 90;
                expectedValue = major * 2.0;
                Cv2.Ellipse(image, new Point(width / 2, height / 2), new Size(major, minor), angle, 0, 360, Scalar.White, 1, LineTypes.AntiAlias);
                break;
        }

        if (scenario == "outlier_contour")
        {
            ApplyGeometricOutlierStress(image, index);
        }
        else
        {
            ApplyStress(image, scenario, index);
        }

        var sut = new GeometricFittingOperator(NullLogger<GeometricFittingOperator>.Instance);
        var op = CreateOperator(
            OperatorType.GeometricFitting,
            ("FitType", fitType),
            ("Threshold", scenario == "low_contrast" ? 72.0 : 80.0),
            ("MinArea", 5),
            ("MinPoints", 5),
            ("ContourSelection", scenario == "outlier_contour" ? "LargestContour" : "BestResidual"),
            ("RobustMethod", scenario is "noise" or "outlier_contour" or "occlusion" ? "Ransac" : "LeastSquares"),
            ("RansacIterations", 320),
            ("RansacInlierThreshold", 2.5));
        using var wrapper = new ImageWrapper(image.Clone());

        var stopwatch = Stopwatch.StartNew();
        var allocationBefore = GC.GetTotalAllocatedBytes(precise: true);
        var output = await sut.ExecuteAsync(op, new Dictionary<string, object> { ["Image"] = wrapper });
        var allocationAfter = GC.GetTotalAllocatedBytes(precise: true);
        stopwatch.Stop();

        var actualValue = ExtractGeometricActualValue(output.OutputData, fitType);
        var residual = TryGetDouble(output.OutputData, "ResidualMean", out var residualValue) ? Math.Abs(residualValue) : double.NaN;
        var angleError = fitType == "Line" ? AngleError(actualValue, expectedValue) : (double?)null;
        var pixelError = double.IsFinite(residual)
            ? residual
            : Math.Abs(actualValue - expectedValue);
        var passed = output.IsSuccess &&
            double.IsFinite(pixelError) &&
            pixelError <= 1.5 &&
            (!angleError.HasValue || angleError.Value <= 2.0);
        ReleaseImageOutputs(output.OutputData);

        return new CaseResult(
            $"GeometricFitting_{fitType}_oracle_{index:000}",
            "GeometricFitting",
            scenario,
            isBoundary,
            passed,
            Math.Round(stopwatch.Elapsed.TotalMilliseconds, 3),
            Math.Max(0, allocationAfter - allocationBefore),
            RoundNullable(pixelError),
            angleError.HasValue ? RoundNullable(angleError.Value) : null,
            Math.Round(expectedValue, 6),
            RoundValue(actualValue),
            passed ? null : output.ErrorMessage ?? $"{fitType}Error={pixelError:0.###}");
    }

    private static string ScenarioFor(int index) =>
        index < BoundaryCasesPerOperator
            ? BoundaryScenarios[index % BoundaryScenarios.Length]
            : "nominal";

    private static void DrawLineScene(Mat image, double angle, string scenario, int index)
    {
        var cx = image.Width / 2.0 + ((index % 5) - 2);
        var cy = image.Height / 2.0 + ((index % 7) - 3);
        var length = scenario == "partial_edge" ? 70.0 : 118.0;
        var radians = angle * Math.PI / 180.0;
        var dx = Math.Cos(radians) * length / 2.0;
        var dy = Math.Sin(radians) * length / 2.0;
        var color = scenario == "low_contrast" ? new Scalar(185, 185, 185) : Scalar.White;
        Cv2.Line(
            image,
            new Point((int)Math.Round(cx - dx), (int)Math.Round(cy - dy)),
            new Point((int)Math.Round(cx + dx), (int)Math.Round(cy + dy)),
            color,
            scenario == "partial_edge" ? 2 : 3,
            LineTypes.AntiAlias);
    }

    private static void DrawFittingLineScene(Mat image, double angle, string scenario, int index)
    {
        var cx = image.Width / 2.0 + ((index % 5) - 2);
        var cy = image.Height / 2.0 + ((index % 7) - 3);
        var length = scenario == "partial_edge" ? 76.0 : 124.0;
        var radians = angle * Math.PI / 180.0;
        var dx = Math.Cos(radians) * length / 2.0;
        var dy = Math.Sin(radians) * length / 2.0;
        Cv2.Line(
            image,
            new Point((int)Math.Round(cx - dx), (int)Math.Round(cy - dy)),
            new Point((int)Math.Round(cx + dx), (int)Math.Round(cy + dy)),
            Scalar.White,
            1,
            LineTypes.AntiAlias);
    }

    private static void ApplyStress(Mat image, string scenario, int seed)
    {
        switch (scenario)
        {
            case "blur":
                Cv2.GaussianBlur(image, image, new Size(5, 5), 1.1);
                break;
            case "noise":
                AddDeterministicNoise(image, seed, amplitude: 8);
                break;
            case "occlusion":
                Cv2.Rectangle(image, new Rect(image.Width / 2 - 7, image.Height / 2 - 18, 14, 36), Scalar.Black, -1);
                break;
            case "outlier_contour":
                Cv2.Line(image, new Point(8 + seed % 20, 12), new Point(32 + seed % 25, 32), new Scalar(160, 160, 160), 1);
                break;
            case "subpixel_offset":
                Cv2.GaussianBlur(image, image, new Size(3, 3), 0.6);
                break;
        }
    }

    private static void ApplyGeometricOutlierStress(Mat image, int seed)
    {
        var x = image.Width - 24 - seed % 5;
        var y = 12 + seed % 7;
        Cv2.Rectangle(image, new Rect(x, y, 8, 6), new Scalar(175, 175, 175), 1);
    }

    private static void AddDeterministicNoise(Mat image, int seed, int amplitude)
    {
        var random = new Random(seed + 17);
        var indexer = image.GetGenericIndexer<Vec3b>();
        for (var y = 0; y < image.Rows; y += 2)
        {
            for (var x = 0; x < image.Cols; x += 2)
            {
                var delta = random.Next(-amplitude, amplitude + 1);
                var pixel = indexer[y, x];
                pixel.Item0 = ClampByte(pixel.Item0 + delta);
                pixel.Item1 = ClampByte(pixel.Item1 + delta);
                pixel.Item2 = ClampByte(pixel.Item2 + delta);
                indexer[y, x] = pixel;
            }
        }
    }

    private static byte ClampByte(int value) => (byte)Math.Clamp(value, 0, 255);

    private static double? RoundNullable(double value) =>
        double.IsFinite(value) ? Math.Round(value, 6) : null;

    private static double RoundValue(double value) =>
        double.IsFinite(value) ? Math.Round(value, 6) : 0.0;

    private static Operator CreateOperator(OperatorType operatorType, params (string Name, object Value)[] parameters)
    {
        var op = new Operator(operatorType.ToString(), operatorType, 0, 0);
        foreach (var (name, value) in parameters)
        {
            op.Parameters.Add(new Parameter(Guid.NewGuid(), name, name, string.Empty, InferParameterType(value), value));
        }

        return op;
    }

    private static string InferParameterType(object value) => value switch
    {
        bool => "bool",
        int or long => "int",
        float or double or decimal => "double",
        _ => "string"
    };

    private static bool TryGetDouble(IReadOnlyDictionary<string, object>? data, string key, out double value)
    {
        value = double.NaN;
        if (data is null || !data.TryGetValue(key, out var raw) || raw is not IConvertible convertible)
        {
            return false;
        }

        value = Convert.ToDouble(convertible);
        return true;
    }

    private static bool TryGetPosition(IReadOnlyDictionary<string, object>? data, string key, out Position value)
    {
        value = new Position(double.NaN, double.NaN);
        if (data is null || !data.TryGetValue(key, out var raw))
        {
            return false;
        }

        if (raw is Position position)
        {
            value = position;
            return true;
        }

        return false;
    }

    private static bool TryGetArcPoints(IReadOnlyDictionary<string, object>? data, out List<ArcCaliperPoint> points)
    {
        points = [];
        if (data is null || !data.TryGetValue("Points", out var raw) || raw is not List<ArcCaliperPoint> typedPoints)
        {
            return false;
        }

        points = typedPoints;
        return true;
    }

    private static double ExtractGeometricActualValue(IReadOnlyDictionary<string, object>? data, string fitType)
    {
        if (data is null ||
            !data.TryGetValue("FitResult", out var rawFit) ||
            rawFit is not Dictionary<string, object> fit ||
            !fit.TryGetValue("Geometry", out var rawGeometry) ||
            rawGeometry is not Dictionary<string, object> geometry)
        {
            return double.NaN;
        }

        if (fitType.Equals("Line", StringComparison.OrdinalIgnoreCase) &&
            geometry.TryGetValue("Line", out var rawLine) &&
            rawLine is Dictionary<string, object> line &&
            line.TryGetValue("Angle", out var rawAngle) &&
            rawAngle is IConvertible angle)
        {
            return Convert.ToDouble(angle);
        }

        if (fitType.Equals("Circle", StringComparison.OrdinalIgnoreCase))
        {
            if (geometry.TryGetValue("Radius", out var rawRadius) && rawRadius is IConvertible radius)
            {
                return Convert.ToDouble(radius);
            }

            if (geometry.TryGetValue("Circle", out var rawCircle) &&
                rawCircle is Dictionary<string, object> circle &&
                circle.TryGetValue("Radius", out var rawCircleRadius) &&
                rawCircleRadius is IConvertible circleRadius)
            {
                return Convert.ToDouble(circleRadius);
            }
        }

        if (fitType.Equals("Ellipse", StringComparison.OrdinalIgnoreCase) &&
            geometry.TryGetValue("MajorAxis", out var rawMajor) &&
            rawMajor is IConvertible major)
        {
            return Convert.ToDouble(major);
        }

        return double.NaN;
    }

    private static double AngleError(double actual, double expected)
    {
        if (!double.IsFinite(actual))
        {
            return double.NaN;
        }

        var delta = Math.Abs(Normalize180(actual) - Normalize180(expected));
        return Math.Min(delta, 180.0 - delta);
    }

    private static double Normalize180(double value)
    {
        value %= 180.0;
        return value < 0 ? value + 180.0 : value;
    }

    private static double Percentile(IEnumerable<double> values, double percentile)
    {
        var sorted = values.Where(double.IsFinite).OrderBy(item => item).ToArray();
        if (sorted.Length == 0)
        {
            return 0.0;
        }

        var index = (int)Math.Ceiling(sorted.Length * percentile) - 1;
        return sorted[Math.Clamp(index, 0, sorted.Length - 1)];
    }

    private static void ReleaseImageOutputs(Dictionary<string, object>? outputData)
    {
        if (outputData is null)
        {
            return;
        }

        var seen = new HashSet<ImageWrapper>();
        foreach (var image in outputData.Values.OfType<ImageWrapper>())
        {
            if (seen.Add(image))
            {
                image.Release();
            }
        }
    }
}

internal sealed record RunnerOptions(string OutputPath, string ReportPath, bool ShowHelp, string? ParseError)
{
    public static RunnerOptions Parse(string[] args)
    {
        var options = new RunnerOptions(
            "quality/evals/reports/QualityFlywheel_measurement_geometry_oracle_v1.json",
            "quality/evals/reports/QualityFlywheel_measurement_geometry_oracle_v1.md",
            false,
            null);

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg is "-h" or "--help")
            {
                return options with { ShowHelp = true };
            }

            if (i + 1 >= args.Length)
            {
                return options with { ParseError = $"Missing value for {arg}" };
            }

            var value = args[++i];
            options = arg switch
            {
                "--output" => options with { OutputPath = value },
                "--report" => options with { ReportPath = value },
                _ => options with { ParseError = $"Unknown argument: {arg}" }
            };
        }

        return options;
    }

    public static void PrintHelp() =>
        Console.WriteLine("Usage: dotnet run --project quality/tools/MeasurementGeometryOracleRunner/MeasurementGeometryOracleRunner.csproj -- [--output path] [--report path]");
}

internal static class MarkdownReport
{
    public static string Create(OracleResult result)
    {
        var lines = new List<string>
        {
            "# Measurement Geometry Oracle Report",
            "",
            $"GeneratedAtUtc: `{result.GeneratedAtUtc:O}`",
            $"Accepted: `{result.Accepted}`",
            "",
            "## Claim Boundary",
            "",
            $"- {result.ClaimBoundary.EvidenceRule}",
            $"- {result.ClaimBoundary.FieldSignoffRule}",
            $"- {result.ClaimBoundary.BoundarySampleRule}",
            "",
            "## Summary",
            "",
            "| Metric | Value |",
            "| --- | ---: |",
            $"| Cases | {result.Summary.CaseCount} |",
            $"| Passed | {result.Summary.Passed} |",
            $"| Failed | {result.Summary.Failed} |",
            $"| Pass rate | {result.Summary.PassRate:0.####} |",
            $"| Boundary/failure-oriented cases | {result.Summary.BoundaryCaseCount} |",
            $"| Regression cases | {result.Summary.RegressionCaseCount} |",
            $"| P95 pixel error px | {result.Summary.P95PixelErrorPx:0.####} |",
            $"| P95 angle error deg | {result.Summary.P95AngleErrorDeg:0.####} |",
            $"| Runtime ms | {result.Summary.RuntimeMs:0.###} |",
            "",
            "## Operators",
            "",
            "| Operator | Cases | Boundary | Passed | Failed | Pass rate | P95 pixel error | P95 angle error | Avg runtime ms | Accepted |",
            "| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | --- |"
        };

        lines.AddRange(result.Operators.Select(item =>
            $"| {item.Operator} | {item.CaseCount} | {item.BoundaryCaseCount} | {item.Passed} | {item.Failed} | {item.PassRate:0.####} | {item.P95PixelErrorPx:0.####} | {item.P95AngleErrorDeg:0.####} | {item.RuntimeMsAvg:0.###} | {item.Accepted} |"));

        lines.AddRange(
        [
            "",
            "## Failed Cases",
            "",
            "| Case | Operator | Scenario | Pixel error | Angle error | Expected | Actual | Failure |",
            "| --- | --- | --- | ---: | ---: | ---: | ---: | --- |"
        ]);

        var failures = result.Cases.Where(item => !item.Passed).Take(40).ToArray();
        if (failures.Length == 0)
        {
            lines.Add("| - | - | - | - | - | - | - | - |");
        }
        else
        {
            lines.AddRange(failures.Select(item =>
                $"| {item.CaseId} | {item.Operator} | {item.Scenario} | {Format(item.PixelErrorPx)} | {Format(item.AngleErrorDeg)} | {item.ExpectedValue:0.######} | {item.ActualValue:0.######} | {item.Failure ?? "-"} |"));
        }

        lines.Add("");
        return string.Join(Environment.NewLine, lines);
    }

    private static string Format(double? value) =>
        value.HasValue && double.IsFinite(value.Value) ? value.Value.ToString("0.####") : "-";
}

internal sealed record ClaimBoundary(string EvidenceRule, string FieldSignoffRule, string BoundarySampleRule);
internal sealed record OracleSummary(
    int CaseCount,
    int Passed,
    int Failed,
    double PassRate,
    int BoundaryCaseCount,
    int RegressionCaseCount,
    double P95PixelErrorPx,
    double P95AngleErrorDeg,
    double RuntimeMs);
internal sealed record OperatorSummary(
    string Operator,
    int CaseCount,
    int BoundaryCaseCount,
    int Passed,
    int Failed,
    double PassRate,
    double P95PixelErrorPx,
    double P95AngleErrorDeg,
    double RuntimeMsAvg,
    bool Accepted);
internal sealed record CaseResult(
    string CaseId,
    string Operator,
    string Scenario,
    bool IsBoundary,
    bool Passed,
    double RuntimeMs,
    long MemoryAllocationBytes,
    double? PixelErrorPx,
    double? AngleErrorDeg,
    double ExpectedValue,
    double ActualValue,
    string? Failure);
internal sealed record OracleResult(
    string SchemaVersion,
    DateTimeOffset GeneratedAtUtc,
    bool Accepted,
    ClaimBoundary ClaimBoundary,
    OracleSummary Summary,
    IReadOnlyList<OperatorSummary> Operators,
    IReadOnlyList<CaseResult> Cases);

internal static class JsonSettings
{
    public static readonly JsonSerializerOptions Indented = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = null
    };
}
