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
    private const int BaselineCasesPerOperator = 300;
    private const int BoundaryCasesPerOperator = 40;
    private const int StressV2CasesPerOperator = 120;
    private const int StressV2VariantOffset = 1000;
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
    private static readonly string[] StressV2Scenarios =
    [
        "blur",
        "noise",
        "low_contrast",
        "occlusion",
        "polarity_flip",
        "subpixel_offset",
        "outlier_contour",
        "weak_edge"
    ];

    public static async Task<OracleResult> RunAsync(RunnerOptions options)
    {
        var profile = RunProfile.Resolve(options.Profile);
        var cases = new List<CaseResult>(profile.CasesPerOperator * 5);
        for (var i = 0; i < profile.CasesPerOperator; i++)
        {
            cases.Add(await RunCaliperCaseAsync(i, profile));
            cases.Add(await RunArcCaliperCaseAsync(i, profile));
            cases.Add(await RunLineCaseAsync(i, profile));
            cases.Add(await RunCircleCaseAsync(i, profile));
            cases.Add(await RunGeometricFittingCaseAsync(i, profile));
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
        var accepted = operators.All(item => item.Accepted) &&
            (!profile.StressOnly || operators.All(item => item.StressCaseCount >= 100));

        return new OracleResult(
            profile.SchemaVersion,
            DateTimeOffset.UtcNow,
            accepted,
            new ClaimBoundary(
                profile.EvidenceRule,
                "It is not real production-site validation or sign-off.",
                profile.BoundarySampleRule),
            new OracleSummary(
                cases.Count,
                cases.Count - failed,
                failed,
                Math.Round(passRate, 6),
                cases.Count(item => item.IsBoundary),
                cases.Count(item => item.IsBoundary),
                0,
                Math.Round(p95Pixel, 6),
                Math.Round(p95Angle, 6),
                Math.Round(AverageNullable(cases.Select(item => item.UncertaintyPx)), 6),
                Math.Round(OutlierRate(cases), 6),
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
            cases.Count(item => item.IsBoundary),
            cases.Count - failed,
            failed,
            Math.Round(passRate, 6),
            Math.Round(p95Pixel, 6),
            Math.Round(p95Angle, 6),
            Math.Round(AverageNullable(cases.Select(item => item.UncertaintyPx)), 6),
            Math.Round(OutlierRate(cases), 6),
            Math.Round(cases.Average(item => item.RuntimeMs), 3),
            accepted);
    }

    private static async Task<CaseResult> RunCaliperCaseAsync(int index, RunProfile profile)
    {
        var scenario = profile.ScenarioFor(index);
        var isBoundary = profile.IsStressCase(index);
        var variant = profile.VariantIndex(index);
        var multiPairCount = profile.StressOnly && (variant % 6) == 0 ? 3 : profile.StressOnly && (variant % 6) == 1 ? 2 : 1;
        var width = multiPairCount > 1 ? 260 : 180;
        var height = 120;
        var stripeWidth = 18 + (variant % 32);
        var x1 = multiPairCount > 1 ? 24 + (variant % 8) : 45 + (variant % 34);
        var dark = scenario == "weak_edge" ? 82 : IsLowSignalScenario(scenario) ? 70 : 20;
        var light = scenario == "weak_edge" ? 178 : IsLowSignalScenario(scenario) ? 170 : 235;
        var polarityFlip = scenario == "polarity_flip";

        using var image = new Mat(height, width, MatType.CV_8UC3, polarityFlip ? new Scalar(light, light, light) : new Scalar(dark, dark, dark));
        var stripeColor = polarityFlip ? new Scalar(dark, dark, dark) : new Scalar(light, light, light);
        for (var pair = 0; pair < multiPairCount; pair++)
        {
            var gap = 16 + (variant % 5);
            var stripeX = x1 + (pair * (stripeWidth + gap));
            Cv2.Rectangle(image, new Rect(stripeX, 18, stripeWidth, height - 36), stripeColor, -1);
        }

        ApplyStress(image, scenario, variant);

        var op = CreateOperator(
            OperatorType.CaliperTool,
            ("Direction", "Horizontal"),
            ("Polarity", "Both"),
            ("ExpectedCount", multiPairCount),
            ("EdgeThreshold", IsLowSignalScenario(scenario) ? 5.0 : 10.0),
            ("SubpixelAccuracy", true),
            ("PairDirection", "any"));
        using var wrapper = new ImageWrapper(image.Clone());
        var inputs = new Dictionary<string, object>
        {
            ["Image"] = wrapper,
            ["SearchRegion"] = scenario == "occlusion"
                ? new Rect(8, 20, width - 16, 22)
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
        var pairCount = TryGetInt(output.OutputData, "PairCount", out var detectedPairCount) ? detectedPairCount : 0;
        var pairDistances = TryGetDoubleList(output.OutputData, "PairDistances");
        var uncertainty = TryGetDouble(output.OutputData, "UncertaintyPx", out var uncertaintyValue) ? uncertaintyValue : double.NaN;
        var outlierCount = CountOutliers(pairDistances);
        var edgeCount = pairCount * 2;
        var passed = output.IsSuccess && double.IsFinite(pixelError) && pixelError <= 1.5;
        var taxonomy = BuildMeasurementTaxonomy(
            scenario,
            passed,
            edgeCount,
            multiPairCount * 2,
            pixelError,
            uncertainty,
            outlierCount,
            output.ErrorMessage);
        ReleaseImageOutputs(output.OutputData);

        return new CaseResult(
            profile.CaseId("CaliperTool", index),
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
            passed ? null : output.ErrorMessage ?? $"widthError={pixelError:0.###}",
            null,
            RoundNullable(pixelError),
            RoundNullable(pixelError),
            edgeCount,
            RoundNullable(uncertainty),
            outlierCount,
            taxonomy);
    }

    private static async Task<CaseResult> RunArcCaliperCaseAsync(int index, RunProfile profile)
    {
        var scenario = profile.ScenarioFor(index);
        var isBoundary = profile.IsStressCase(index);
        var variant = profile.VariantIndex(index);
        var width = 220;
        var height = 220;
        var centerX = width / 2;
        var centerY = height / 2;
        var radius = 38 + variant % 34;
        var startAngle = (variant * 13) % 330;
        var span = scenario == "partial_edge" || scenario == "occlusion" ? 72.0 : 118.0 + variant % 90;
        var endAngle = startAngle + span;
        var lowContrast = IsLowSignalScenario(scenario);
        var polarityFlip = scenario == "polarity_flip";
        var background = polarityFlip ? (lowContrast ? 210 : 235) : (lowContrast ? 70 : 18);
        var foreground = polarityFlip ? (lowContrast ? 70 : 18) : (lowContrast ? 210 : 238);
        if (scenario == "weak_edge")
        {
            background = polarityFlip ? 178 : 82;
            foreground = polarityFlip ? 82 : 178;
        }

        using var image = new Mat(height, width, MatType.CV_8UC3, new Scalar(background, background, background));
        Cv2.Circle(image, new Point(centerX, centerY), radius, new Scalar(foreground, foreground, foreground), -1, LineTypes.AntiAlias);
        ApplyStress(image, scenario, variant);

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
        var radialDistances = points
            .Select(point => Math.Sqrt(Math.Pow(point.X - centerX, 2) + Math.Pow(point.Y - centerY, 2)))
            .ToArray();
        var actualRadius = radialDistances.Length == 0 ? double.NaN : radialDistances.Average();
        var pixelError = Math.Abs(actualRadius - radius);
        var minCount = Math.Max(8, (int)Math.Floor(Math.Abs(span) * 0.35));
        var uncertainty = StandardDeviation(radialDistances);
        var outlierCount = CountOutliers(radialDistances);
        var passed = output.IsSuccess && points.Count >= minCount && double.IsFinite(pixelError) && pixelError <= 1.5;
        var taxonomy = BuildMeasurementTaxonomy(
            scenario,
            passed,
            points.Count,
            minCount,
            pixelError,
            uncertainty,
            outlierCount,
            output.ErrorMessage);
        ReleaseImageOutputs(output.OutputData);

        return new CaseResult(
            profile.CaseId("ArcCaliper", index),
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
            passed ? null : output.ErrorMessage ?? $"radiusError={pixelError:0.###}, points={points.Count}, min={minCount}",
            null,
            RoundNullable(pixelError),
            RoundNullable(pixelError),
            points.Count,
            RoundNullable(uncertainty),
            outlierCount,
            taxonomy);
    }

    private static async Task<CaseResult> RunLineCaseAsync(int index, RunProfile profile)
    {
        var scenario = profile.ScenarioFor(index);
        var isBoundary = profile.IsStressCase(index);
        var variant = profile.VariantIndex(index);
        var width = 180;
        var height = 140;
        var angle = new[] { 0.0, 90.0, 45.0, 135.0 }[variant % 4];
        var background = scenario == "polarity_flip" ? Scalar.White : Scalar.Black;
        using var image = new Mat(height, width, MatType.CV_8UC3, background);
        DrawLineScene(image, angle, scenario, variant);
        ApplyStress(image, scenario, variant);

        var op = CreateOperator(
            OperatorType.LineMeasurement,
            ("Method", "ProbabilisticHough"),
            ("Threshold", scenario is "partial_edge" or "weak_edge" or "low_contrast" ? 14 : 22),
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
        var lineCount = TryGetInt(output.OutputData, "LineCount", out var detectedLineCount) ? detectedLineCount : 0;
        var uncertainty = TryGetDouble(output.OutputData, "UncertaintyPx", out var uncertaintyValue) ? uncertaintyValue : residual;
        var outlierCount = double.IsFinite(residual) && residual > 1.5 ? 1 : 0;
        var passed = output.IsSuccess && double.IsFinite(angleError) && angleError <= 2.0 && residual <= 1.5;
        var taxonomy = BuildMeasurementTaxonomy(
            scenario,
            passed,
            lineCount,
            1,
            residual,
            uncertainty,
            outlierCount,
            output.ErrorMessage);
        ReleaseImageOutputs(output.OutputData);

        return new CaseResult(
            profile.CaseId("LineMeasurement", index),
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
            passed ? null : output.ErrorMessage ?? $"angleError={angleError:0.###}, residual={residual:0.###}",
            RoundNullable(residual),
            RoundNullable(residual),
            null,
            lineCount,
            RoundNullable(uncertainty),
            outlierCount,
            taxonomy);
    }

    private static async Task<CaseResult> RunCircleCaseAsync(int index, RunProfile profile)
    {
        var scenario = profile.ScenarioFor(index);
        var isBoundary = profile.IsStressCase(index);
        var variant = profile.VariantIndex(index);
        var width = 150;
        var height = 130;
        var radius = 15 + (variant % 24);
        var centerX = 42 + (variant * 7 % 58);
        var centerY = 38 + (variant * 5 % 48);
        centerX = Math.Clamp(centerX, radius + 8, width - radius - 8);
        centerY = Math.Clamp(centerY, radius + 8, height - radius - 8);
        var background = scenario == "polarity_flip" ? Scalar.White : Scalar.Black;
        var foreground = scenario == "polarity_flip"
            ? Scalar.Black
            : scenario == "weak_edge" ? new Scalar(190, 190, 190) : IsLowSignalScenario(scenario) ? new Scalar(185, 185, 185) : Scalar.White;
        using var image = new Mat(height, width, MatType.CV_8UC3, background);
        Cv2.Circle(image, new Point(centerX, centerY), radius, foreground, -1, LineTypes.AntiAlias);
        ApplyStress(image, scenario, variant);

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
        var circleCount = TryGetInt(output.OutputData, "CircleCount", out var detectedCircleCount) ? detectedCircleCount : 0;
        var uncertainty = TryGetDouble(output.OutputData, "UncertaintyPx", out var uncertaintyValue) ? uncertaintyValue : double.NaN;
        var outlierCount = double.IsFinite(pixelError) && pixelError > 1.5 ? 1 : 0;
        var passed = output.IsSuccess && double.IsFinite(pixelError) && pixelError <= 1.5;
        var taxonomy = BuildMeasurementTaxonomy(
            scenario,
            passed,
            circleCount,
            1,
            pixelError,
            uncertainty,
            outlierCount,
            output.ErrorMessage);
        ReleaseImageOutputs(output.OutputData);

        return new CaseResult(
            profile.CaseId("CircleMeasurement", index),
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
            passed ? null : output.ErrorMessage ?? $"pixelError={pixelError:0.###}",
            RoundNullable(centerError),
            RoundNullable(radiusError),
            RoundNullable(radiusError),
            circleCount,
            RoundNullable(uncertainty),
            outlierCount,
            taxonomy);
    }

    private static async Task<CaseResult> RunGeometricFittingCaseAsync(int index, RunProfile profile)
    {
        var scenario = profile.ScenarioFor(index);
        var isBoundary = profile.IsStressCase(index);
        var variant = profile.VariantIndex(index);
        var fitType = new[] { "Line", "Circle", "Ellipse" }[variant % 3];
        var width = 180;
        var height = 150;
        using var image = new Mat(height, width, MatType.CV_8UC3, Scalar.Black);
        var drawColor = scenario == "weak_edge"
            ? new Scalar(190, 190, 190)
            : IsLowSignalScenario(scenario) ? new Scalar(185, 185, 185) : Scalar.White;

        double expectedValue;
        switch (fitType)
        {
            case "Line":
                expectedValue = new[] { 0.0, 45.0, 90.0, 135.0 }[(variant / 3) % 4];
                DrawFittingLineScene(image, expectedValue, scenario == "partial_edge" ? "partial_edge" : "nominal", variant, drawColor);
                break;
            case "Circle":
                var radius = 20 + variant % 24;
                var cx = 50 + variant * 7 % 70;
                var cy = 42 + variant * 5 % 54;
                cx = Math.Clamp(cx, radius + 8, width - radius - 8);
                cy = Math.Clamp(cy, radius + 8, height - radius - 8);
                expectedValue = radius;
                Cv2.Circle(image, new Point(cx, cy), radius, drawColor, 1, LineTypes.AntiAlias);
                break;
            default:
                var major = 26 + variant % 18;
                var minor = 14 + variant % 10;
                var angle = (variant * 11) % 90;
                expectedValue = major * 2.0;
                Cv2.Ellipse(image, new Point(width / 2, height / 2), new Size(major, minor), angle, 0, 360, drawColor, 1, LineTypes.AntiAlias);
                break;
        }

        if (scenario == "outlier_contour")
        {
            ApplyGeometricOutlierStress(image, variant);
        }
        else
        {
            ApplyStress(image, scenario, variant);
        }

        var sut = new GeometricFittingOperator(NullLogger<GeometricFittingOperator>.Instance);
        var op = CreateOperator(
            OperatorType.GeometricFitting,
            ("FitType", fitType),
            ("Threshold", IsLowSignalScenario(scenario) ? 72.0 : 80.0),
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
        var uncertainty = TryGetDouble(output.OutputData, "UncertaintyPx", out var uncertaintyValue) ? uncertaintyValue : residual;
        var pointCount = TryGetInt(output.OutputData, "PointCount", out var detectedPointCount) ? detectedPointCount : 0;
        var outlierCount = TryGetGeometricOutlierCount(output.OutputData, pointCount);
        var angleError = fitType == "Line" ? AngleError(actualValue, expectedValue) : (double?)null;
        var pixelError = double.IsFinite(residual)
            ? residual
            : Math.Abs(actualValue - expectedValue);
        var passed = output.IsSuccess &&
            double.IsFinite(pixelError) &&
            pixelError <= 1.5 &&
            (!angleError.HasValue || angleError.Value <= 2.0);
        var taxonomy = BuildMeasurementTaxonomy(
            scenario,
            passed,
            pointCount,
            fitType == "Ellipse" ? 5 : fitType == "Circle" ? 3 : 2,
            pixelError,
            uncertainty,
            outlierCount,
            output.ErrorMessage);
        ReleaseImageOutputs(output.OutputData);

        return new CaseResult(
            profile.CaseId($"GeometricFitting_{fitType}", index),
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
            passed ? null : output.ErrorMessage ?? $"{fitType}Error={pixelError:0.###}",
            fitType == "Line" ? RoundNullable(pixelError) : null,
            RoundNullable(pixelError),
            fitType is "Circle" or "Ellipse" ? RoundNullable(Math.Abs(actualValue - expectedValue)) : null,
            pointCount,
            RoundNullable(uncertainty),
            outlierCount,
            taxonomy);
    }

    private static bool IsLowSignalScenario(string scenario) =>
        scenario is "low_contrast" or "weak_edge";

    private static void DrawLineScene(Mat image, double angle, string scenario, int index)
    {
        var cx = image.Width / 2.0 + ((index % 5) - 2);
        var cy = image.Height / 2.0 + ((index % 7) - 3);
        var length = scenario == "partial_edge" ? 70.0 : 118.0;
        var radians = angle * Math.PI / 180.0;
        var dx = Math.Cos(radians) * length / 2.0;
        var dy = Math.Sin(radians) * length / 2.0;
        var color = scenario == "polarity_flip"
            ? Scalar.Black
            : scenario == "weak_edge" ? new Scalar(150, 150, 150) : IsLowSignalScenario(scenario) ? new Scalar(185, 185, 185) : Scalar.White;
        Cv2.Line(
            image,
            new Point((int)Math.Round(cx - dx), (int)Math.Round(cy - dy)),
            new Point((int)Math.Round(cx + dx), (int)Math.Round(cy + dy)),
            color,
            scenario == "partial_edge" ? 2 : 3,
            LineTypes.AntiAlias);
    }

    private static void DrawFittingLineScene(Mat image, double angle, string scenario, int index, Scalar color)
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
            color,
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
            case "weak_edge":
                Cv2.GaussianBlur(image, image, new Size(3, 3), 0.8);
                AddDeterministicNoise(image, seed, amplitude: 2);
                break;
        }
    }

    private static void ApplyGeometricOutlierStress(Mat image, int seed)
    {
        var x = image.Width - 24 - seed % 5;
        var y = 12 + seed % 7;
        Cv2.Rectangle(image, new Rect(x, y, 8, 6), new Scalar(175, 175, 175), 1);
    }

    private static IReadOnlyList<string> BuildMeasurementTaxonomy(
        string scenario,
        bool passed,
        int edgeCount,
        int expectedEdgeCount,
        double primaryError,
        double uncertainty,
        int outlierCount,
        string? failure)
    {
        var labels = new List<string>();
        if (scenario == "weak_edge" || failure?.Contains("NoFeature", StringComparison.OrdinalIgnoreCase) == true)
        {
            labels.Add("weak-gradient");
        }

        if (scenario == "polarity_flip" && !passed)
        {
            labels.Add("polarity-mismatch");
        }

        if (double.IsFinite(primaryError) && primaryError > 1.5)
        {
            labels.Add("pair-distance-outlier");
        }

        if (scenario == "occlusion" && (edgeCount < expectedEdgeCount || !passed))
        {
            labels.Add("occluded-edge");
        }

        if (double.IsFinite(uncertainty) && uncertainty > 1.0)
        {
            labels.Add("unstable-subpixel-peak");
        }

        if (outlierCount > 0)
        {
            labels.Add("outlier-contour");
        }

        if (labels.Count == 0 && !passed)
        {
            labels.Add("unclassified-measurement-failure");
        }

        return labels.Distinct(StringComparer.Ordinal).ToArray();
    }

    private static double AverageNullable(IEnumerable<double?> values)
    {
        var finite = values
            .Where(value => value.HasValue && double.IsFinite(value.Value))
            .Select(value => value!.Value)
            .ToArray();
        return finite.Length == 0 ? 0.0 : finite.Average();
    }

    private static double OutlierRate(IReadOnlyList<CaseResult> cases)
    {
        if (cases.Count == 0)
        {
            return 0.0;
        }

        return cases.Count(item => item.OutlierCount > 0) / (double)cases.Count;
    }

    private static int CountOutliers(IReadOnlyList<double> values)
    {
        if (values.Count < 3)
        {
            return 0;
        }

        var center = values.Average();
        var sigma = StandardDeviation(values);
        var threshold = Math.Max(1.0, sigma * 3.0);
        return values.Count(value => double.IsFinite(value) && Math.Abs(value - center) > threshold);
    }

    private static double StandardDeviation(IReadOnlyList<double> values)
    {
        var finite = values.Where(double.IsFinite).ToArray();
        if (finite.Length <= 1)
        {
            return 0.0;
        }

        var mean = finite.Average();
        var variance = finite.Select(value => (value - mean) * (value - mean)).Sum() / (finite.Length - 1);
        return Math.Sqrt(Math.Max(0.0, variance));
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

    private static bool TryGetInt(IReadOnlyDictionary<string, object>? data, string key, out int value)
    {
        value = 0;
        if (data is null || !data.TryGetValue(key, out var raw) || raw is null)
        {
            return false;
        }

        try
        {
            value = Convert.ToInt32(raw);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
        catch (InvalidCastException)
        {
            return false;
        }
    }

    private static IReadOnlyList<double> TryGetDoubleList(IReadOnlyDictionary<string, object>? data, string key)
    {
        if (data is null || !data.TryGetValue(key, out var raw) || raw is null)
        {
            return Array.Empty<double>();
        }

        if (raw is IEnumerable<double> typed)
        {
            return typed.Where(double.IsFinite).ToArray();
        }

        if (raw is string)
        {
            return Array.Empty<double>();
        }

        if (raw is System.Collections.IEnumerable enumerable)
        {
            var values = new List<double>();
            foreach (var item in enumerable)
            {
                if (item is IConvertible convertible)
                {
                    var value = Convert.ToDouble(convertible);
                    if (double.IsFinite(value))
                    {
                        values.Add(value);
                    }
                }
            }

            return values;
        }

        return Array.Empty<double>();
    }

    private static int TryGetGeometricOutlierCount(IReadOnlyDictionary<string, object>? data, int pointCount)
    {
        if (data is null ||
            !data.TryGetValue("FitResult", out var rawFit) ||
            rawFit is not Dictionary<string, object> fit)
        {
            return 0;
        }

        if (fit.TryGetValue("InlierCount", out var rawInlier) && rawInlier is IConvertible inlier)
        {
            return Math.Max(0, pointCount - Convert.ToInt32(inlier));
        }

        if (fit.TryGetValue("ResidualMax", out var rawMax) &&
            rawMax is IConvertible max &&
            Convert.ToDouble(max) > 2.5)
        {
            return 1;
        }

        return 0;
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

    private sealed record RunProfile(
        string Name,
        string SchemaVersion,
        int CasesPerOperator,
        int VariantOffset,
        bool StressOnly,
        string CaseIdSuffix,
        string EvidenceRule,
        string BoundarySampleRule)
    {
        public static RunProfile Resolve(string? profile)
        {
            var normalized = profile?.Trim().ToLowerInvariant() ?? "oracle-v1";
            return normalized switch
            {
                "stress-v2" or "measurement-precision-stress-v2" => new RunProfile(
                    "stress-v2",
                    "2026-04-30.measurement-precision-stress.v2",
                    StressV2CasesPerOperator,
                    StressV2VariantOffset,
                    true,
                    "stress_v2",
                    "This report is semisynthetic stress evidence for measurement-operator precision and robustness.",
                    "Stress samples cover blur, noise, low contrast, occlusion, polarity flip, subpixel offset, outlier contour, and weak edge cases."),
                "oracle-v1" or "measurement-geometry-oracle-v1" => new RunProfile(
                    "oracle-v1",
                    "2026-04-30.measurement-geometry-oracle.v1",
                    BaselineCasesPerOperator,
                    0,
                    false,
                    string.Empty,
                    "This report is semisynthetic geometry-oracle evidence for measurement operators.",
                    "Boundary samples are stress cases over blur, noise, contrast, partial edges, polarity, subpixel offset, outliers, and occlusion."),
                _ => throw new ArgumentException($"Unknown measurement oracle profile: {profile}")
            };
        }

        public string ScenarioFor(int index) =>
            StressOnly
                ? StressV2Scenarios[index % StressV2Scenarios.Length]
                : index < BoundaryCasesPerOperator
                    ? BoundaryScenarios[index % BoundaryScenarios.Length]
                    : "nominal";

        public bool IsStressCase(int index) => StressOnly || index < BoundaryCasesPerOperator;

        public int VariantIndex(int index) => VariantOffset + index;

        public string CaseId(string prefix, int index) =>
            string.IsNullOrWhiteSpace(CaseIdSuffix)
                ? $"{prefix}_oracle_{index:000}"
                : $"{prefix}_{CaseIdSuffix}_{index:000}";
    }
}

internal sealed record RunnerOptions(string OutputPath, string ReportPath, string Profile, bool ShowHelp, string? ParseError)
{
    public static RunnerOptions Parse(string[] args)
    {
        string? outputPath = null;
        string? reportPath = null;
        var profile = "oracle-v1";
        var showHelp = false;
        string? parseError = null;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg is "-h" or "--help")
            {
                showHelp = true;
                continue;
            }

            if (i + 1 >= args.Length)
            {
                parseError = $"Missing value for {arg}";
                break;
            }

            var value = args[++i];
            switch (arg)
            {
                case "--output":
                    outputPath = value;
                    break;
                case "--report":
                    reportPath = value;
                    break;
                case "--profile":
                    profile = value;
                    break;
                default:
                    parseError = $"Unknown argument: {arg}";
                    break;
            }
        }

        var defaults = DefaultPaths(profile);
        return new RunnerOptions(
            outputPath ?? defaults.Output,
            reportPath ?? defaults.Report,
            profile,
            showHelp,
            parseError);
    }

    private static (string Output, string Report) DefaultPaths(string profile)
    {
        var normalized = profile.Trim().ToLowerInvariant();
        if (normalized is "stress-v2" or "measurement-precision-stress-v2")
        {
            return (
                "quality/evals/reports/QualityFlywheel_measurement_precision_stress_v2.json",
                "quality/evals/reports/QualityFlywheel_measurement_precision_stress_v2.md");
        }

        return (
            "quality/evals/reports/QualityFlywheel_measurement_geometry_oracle_v1.json",
            "quality/evals/reports/QualityFlywheel_measurement_geometry_oracle_v1.md");
    }

    public static void PrintHelp() =>
        Console.WriteLine("Usage: dotnet run --project quality/tools/MeasurementGeometryOracleRunner/MeasurementGeometryOracleRunner.csproj -- [--profile oracle-v1|stress-v2] [--output path] [--report path]");
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
            $"| Stress cases | {result.Summary.StressCaseCount} |",
            $"| Regression cases | {result.Summary.RegressionCaseCount} |",
            $"| P95 pixel error px | {result.Summary.P95PixelErrorPx:0.####} |",
            $"| P95 angle error deg | {result.Summary.P95AngleErrorDeg:0.####} |",
            $"| Mean uncertainty px | {result.Summary.MeanUncertaintyPx:0.####} |",
            $"| Outlier rate | {result.Summary.OutlierRate:0.####} |",
            $"| Runtime ms | {result.Summary.RuntimeMs:0.###} |",
            "",
            "## Operators",
            "",
            "| Operator | Cases | Stress | Passed | Failed | Pass rate | P95 pixel error | P95 angle error | Mean uncertainty | Outlier rate | Avg runtime ms | Accepted |",
            "| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | --- |"
        };

        lines.AddRange(result.Operators.Select(item =>
            $"| {item.Operator} | {item.CaseCount} | {item.StressCaseCount} | {item.Passed} | {item.Failed} | {item.PassRate:0.####} | {item.P95PixelErrorPx:0.####} | {item.P95AngleErrorDeg:0.####} | {item.MeanUncertaintyPx:0.####} | {item.OutlierRate:0.####} | {item.RuntimeMsAvg:0.###} | {item.Accepted} |"));

        lines.AddRange(
        [
            "",
            "## Failed Cases",
            "",
            "| Case | Operator | Scenario | Pixel error | Angle error | Edge count | Uncertainty | Outliers | Taxonomy | Failure |",
            "| --- | --- | --- | ---: | ---: | ---: | ---: | ---: | --- | --- |"
        ]);

        var failures = result.Cases.Where(item => !item.Passed).Take(40).ToArray();
        if (failures.Length == 0)
        {
            lines.Add("| - | - | - | - | - | - | - | - | - | - |");
        }
        else
        {
            lines.AddRange(failures.Select(item =>
                $"| {item.CaseId} | {item.Operator} | {item.Scenario} | {Format(item.PixelErrorPx)} | {Format(item.AngleErrorDeg)} | {item.EdgeCount} | {Format(item.UncertaintyPx)} | {item.OutlierCount} | {FormatTaxonomy(item.Taxonomy)} | {item.Failure ?? "-"} |"));
        }

        lines.Add("");
        return string.Join(Environment.NewLine, lines);
    }

    private static string Format(double? value) =>
        value.HasValue && double.IsFinite(value.Value) ? value.Value.ToString("0.####") : "-";

    private static string FormatTaxonomy(IReadOnlyList<string>? taxonomy) =>
        taxonomy is { Count: > 0 } ? string.Join(", ", taxonomy) : "-";
}

internal sealed record ClaimBoundary(string EvidenceRule, string FieldSignoffRule, string BoundarySampleRule);
internal sealed record OracleSummary(
    int CaseCount,
    int Passed,
    int Failed,
    double PassRate,
    int BoundaryCaseCount,
    int StressCaseCount,
    int RegressionCaseCount,
    double P95PixelErrorPx,
    double P95AngleErrorDeg,
    double MeanUncertaintyPx,
    double OutlierRate,
    double RuntimeMs);
internal sealed record OperatorSummary(
    string Operator,
    int CaseCount,
    int BoundaryCaseCount,
    int StressCaseCount,
    int Passed,
    int Failed,
    double PassRate,
    double P95PixelErrorPx,
    double P95AngleErrorDeg,
    double MeanUncertaintyPx,
    double OutlierRate,
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
    string? Failure,
    double? PositionErrorPx = null,
    double? MeasurementErrorPx = null,
    double? RadiusOrDistanceErrorPx = null,
    int EdgeCount = 0,
    double? UncertaintyPx = null,
    int OutlierCount = 0,
    IReadOnlyList<string>? Taxonomy = null);
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
