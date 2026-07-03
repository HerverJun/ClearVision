using ClearVision.Product.Core.ValueObjects;
using OpenCvSharp;

namespace ClearVision.Product.Infrastructure.Operators;

public enum CircleCaliperFitV2EdgePolarity
{
    Auto = 0,
    DarkToLight = 1,
    LightToDark = 2
}

public enum CircleCaliperFitV2OutlierMode
{
    None = 0,
    Mad = 1,
    Huber = 2
}

public enum CircleCaliperFitV2FailureCode
{
    None = 0,
    InvalidInput = 1,
    SearchRegionOutsideImage = 2,
    InsufficientEdges = 3,
    InsufficientCoverage = 4,
    AmbiguousEdge = 5,
    DegenerateFit = 6,
    ResidualTooHigh = 7,
    RadiusOutOfRange = 8,
    Cancelled = 9
}

public sealed record CircleCaliperFitV2Request
{
    public const string ContractVersionValue = "caliper-circle-fit.v2";
    public const int MaxCaliperCountLimit = 720;
    public const int MaxProfileSampleCountLimit = 4096;

    public double SearchCenterX { get; init; }
    public double SearchCenterY { get; init; }
    public double MinRadius { get; init; } = 10.0;
    public double MaxRadius { get; init; } = 200.0;
    public double NominalRadius { get; init; } = 100.0;
    public int CaliperCount { get; init; } = 96;
    public double AveragingThickness { get; init; } = 5.0;
    public int ProfileSampleCount { get; init; } = 129;
    public double GaussianSigma { get; init; } = 1.2;
    public CircleCaliperFitV2EdgePolarity EdgePolarity { get; init; } = CircleCaliperFitV2EdgePolarity.Auto;
    public double EdgeThreshold { get; init; }
    public double MinEdgeStrength { get; init; } = 4.0;
    public int MinValidCalipers { get; init; } = 24;
    public double MinCoverageRatio { get; init; } = 0.35;
    public double MinAngularCoverageDegrees { get; init; } = 180.0;
    public CircleCaliperFitV2OutlierMode OutlierMode { get; init; } = CircleCaliperFitV2OutlierMode.Mad;
    public double OutlierThreshold { get; init; } = 3.5;
    public int MaxOutlierIterations { get; init; } = 3;
    public double MaxResidualRmse { get; init; } = 2.0;
}

public sealed record CircleCaliperFitV2Point(
    double X,
    double Y,
    int CaliperIndex,
    double AngleDegrees,
    double Radius,
    double Strength,
    string Polarity);

public sealed record CircleCaliperFitV2Diagnostic(string Code, string Message, double? Value = null);

public sealed class CircleCaliperFitV2Result
{
    public bool Success { get; init; }
    public CircleCaliperFitV2FailureCode FailureCode { get; init; }
    public string FailureMessage { get; init; } = string.Empty;
    public double? CenterX { get; init; }
    public double? CenterY { get; init; }
    public double? Radius { get; init; }
    public IReadOnlyList<CircleCaliperFitV2Point> EdgePoints { get; init; } = Array.Empty<CircleCaliperFitV2Point>();
    public IReadOnlyList<CircleCaliperFitV2Point> InlierPoints { get; init; } = Array.Empty<CircleCaliperFitV2Point>();
    public IReadOnlyList<CircleCaliperFitV2Point> OutlierPoints { get; init; } = Array.Empty<CircleCaliperFitV2Point>();
    public int ValidCaliperCount { get; init; }
    public int RejectedCaliperCount { get; init; }
    public int CollectedPointCount { get; init; }
    public double CoverageRatio { get; init; }
    public double AngularCoverageDegrees { get; init; }
    public double ResidualRmse { get; init; } = double.NaN;
    public double ResidualMax { get; init; } = double.NaN;
    public double MedianEdgeStrength { get; init; } = double.NaN;
    public CircleCaliperFitV2EdgePolarity ResolvedPolarity { get; init; } = CircleCaliperFitV2EdgePolarity.Auto;
    public double Confidence { get; init; }
    public double UncertaintyPx { get; init; } = double.NaN;
    public IReadOnlyList<CircleCaliperFitV2Diagnostic> Diagnostics { get; init; } = Array.Empty<CircleCaliperFitV2Diagnostic>();
    public string ContractVersion { get; init; } = CircleCaliperFitV2Request.ContractVersionValue;
}

public static class CircleCaliperFitV2Kernel
{
    private const double TwoPi = Math.PI * 2.0;

    public static CircleCaliperFitV2Result Fit(Mat gray, CircleCaliperFitV2Request request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var validation = Validate(gray, request);
        if (validation != null)
        {
            return validation;
        }

        var polarities = request.EdgePolarity == CircleCaliperFitV2EdgePolarity.Auto
            ? new[] { CircleCaliperFitV2EdgePolarity.DarkToLight, CircleCaliperFitV2EdgePolarity.LightToDark }
            : new[] { request.EdgePolarity };

        var evaluations = new List<HypothesisEvaluation>(polarities.Length);
        foreach (var polarity in polarities)
        {
            cancellationToken.ThrowIfCancellationRequested();
            evaluations.Add(EvaluateHypothesis(gray, request, polarity, cancellationToken));
        }

        if (evaluations.Count == 1)
        {
            return evaluations[0].ToResult(request.CaliperCount);
        }

        var successes = evaluations
            .Where(static item => item.Success)
            .OrderByDescending(static item => item.Score)
            .ToArray();

        if (successes.Length == 0)
        {
            var bestFailure = evaluations
                .OrderByDescending(static item => item.EdgePoints.Count)
                .ThenBy(static item => FailurePriority(item.FailureCode))
                .First();
            return bestFailure.ToResult(request.CaliperCount);
        }

        if (successes.Length > 1 && IsGlobalPolarityAmbiguous(successes[0], successes[1]))
        {
            return Failure(
                CircleCaliperFitV2FailureCode.AmbiguousEdge,
                "Auto polarity produced indistinguishable circle hypotheses.",
                successes[0].ResolvedPolarity,
                successes[0].EdgePoints,
                successes[0].Diagnostics);
        }

        return successes[0].ToResult(request.CaliperCount);
    }

    private static CircleCaliperFitV2Result? Validate(Mat gray, CircleCaliperFitV2Request request)
    {
        var diagnostics = new List<CircleCaliperFitV2Diagnostic>();
        if (gray == null || gray.Empty())
        {
            return Failure(CircleCaliperFitV2FailureCode.InvalidInput, "Input grayscale image is empty.", diagnostics: diagnostics);
        }

        if (gray.Channels() != 1)
        {
            return Failure(CircleCaliperFitV2FailureCode.InvalidInput, "Input image must be a single-channel grayscale Mat.", diagnostics: diagnostics);
        }

        if (!IsFinite(request.SearchCenterX) ||
            !IsFinite(request.SearchCenterY) ||
            !IsFinite(request.MinRadius) ||
            !IsFinite(request.MaxRadius) ||
            !IsFinite(request.NominalRadius) ||
            !IsFinite(request.AveragingThickness) ||
            !IsFinite(request.GaussianSigma) ||
            !IsFinite(request.EdgeThreshold) ||
            !IsFinite(request.MinEdgeStrength) ||
            !IsFinite(request.MinCoverageRatio) ||
            !IsFinite(request.MinAngularCoverageDegrees) ||
            !IsFinite(request.OutlierThreshold) ||
            !IsFinite(request.MaxResidualRmse))
        {
            return Failure(CircleCaliperFitV2FailureCode.InvalidInput, "All numeric inputs must be finite.", diagnostics: diagnostics);
        }

        if (request.MinRadius < 1.0 || request.MaxRadius <= request.MinRadius)
        {
            return Failure(CircleCaliperFitV2FailureCode.InvalidInput, "Radius range must satisfy 1 <= MinRadius < MaxRadius.", diagnostics: diagnostics);
        }

        if (request.NominalRadius < request.MinRadius || request.NominalRadius > request.MaxRadius)
        {
            return Failure(CircleCaliperFitV2FailureCode.InvalidInput, "NominalRadius must be inside MinRadius and MaxRadius.", diagnostics: diagnostics);
        }

        if (request.CaliperCount < 3 || request.CaliperCount > CircleCaliperFitV2Request.MaxCaliperCountLimit)
        {
            return Failure(CircleCaliperFitV2FailureCode.InvalidInput, $"CaliperCount must be in [3, {CircleCaliperFitV2Request.MaxCaliperCountLimit}].", diagnostics: diagnostics);
        }

        if (request.ProfileSampleCount < 16 || request.ProfileSampleCount > CircleCaliperFitV2Request.MaxProfileSampleCountLimit)
        {
            return Failure(CircleCaliperFitV2FailureCode.InvalidInput, $"ProfileSampleCount must be in [16, {CircleCaliperFitV2Request.MaxProfileSampleCountLimit}].", diagnostics: diagnostics);
        }

        if (request.AveragingThickness < 1.0 || request.AveragingThickness > 128.0)
        {
            return Failure(CircleCaliperFitV2FailureCode.InvalidInput, "AveragingThickness must be in [1, 128].", diagnostics: diagnostics);
        }

        if (request.GaussianSigma < 0.0 || request.GaussianSigma > 12.0)
        {
            return Failure(CircleCaliperFitV2FailureCode.InvalidInput, "GaussianSigma must be in [0, 12].", diagnostics: diagnostics);
        }

        if (!Enum.IsDefined(request.EdgePolarity) ||
            !Enum.IsDefined(request.OutlierMode) ||
            request.EdgePolarity == CircleCaliperFitV2EdgePolarity.Auto && request.OutlierMode < CircleCaliperFitV2OutlierMode.None)
        {
            return Failure(CircleCaliperFitV2FailureCode.InvalidInput, "Enum inputs are outside the supported contract.", diagnostics: diagnostics);
        }

        if (request.EdgeThreshold < 0.0 || request.EdgeThreshold > 255.0 || request.MinEdgeStrength < 0.0 || request.MinEdgeStrength > 255.0)
        {
            return Failure(CircleCaliperFitV2FailureCode.InvalidInput, "Edge thresholds must be in [0, 255].", diagnostics: diagnostics);
        }

        if (request.MinValidCalipers < 3 || request.MinValidCalipers > request.CaliperCount)
        {
            return Failure(CircleCaliperFitV2FailureCode.InvalidInput, "MinValidCalipers must be in [3, CaliperCount].", diagnostics: diagnostics);
        }

        if (request.MinCoverageRatio < 0.0 || request.MinCoverageRatio > 1.0)
        {
            return Failure(CircleCaliperFitV2FailureCode.InvalidInput, "MinCoverageRatio must be in [0, 1].", diagnostics: diagnostics);
        }

        if (request.MinAngularCoverageDegrees < 0.0 || request.MinAngularCoverageDegrees > 360.0)
        {
            return Failure(CircleCaliperFitV2FailureCode.InvalidInput, "MinAngularCoverageDegrees must be in [0, 360].", diagnostics: diagnostics);
        }

        if (request.OutlierThreshold <= 0.0 || request.OutlierThreshold > 20.0 || request.MaxOutlierIterations < 0 || request.MaxOutlierIterations > 20)
        {
            return Failure(CircleCaliperFitV2FailureCode.InvalidInput, "Outlier settings are outside bounded limits.", diagnostics: diagnostics);
        }

        if (request.MaxResidualRmse <= 0.0 || request.MaxResidualRmse > 128.0)
        {
            return Failure(CircleCaliperFitV2FailureCode.InvalidInput, "MaxResidualRmse must be in (0, 128].", diagnostics: diagnostics);
        }

        var border = (request.AveragingThickness * 0.5) + 1.0;
        if (request.SearchCenterX - request.MaxRadius - border < 0.0 ||
            request.SearchCenterY - request.MaxRadius - border < 0.0 ||
            request.SearchCenterX + request.MaxRadius + border > gray.Width - 1.0 ||
            request.SearchCenterY + request.MaxRadius + border > gray.Height - 1.0)
        {
            diagnostics.Add(new CircleCaliperFitV2Diagnostic("search.radius", "The full search ring extends outside the image.", request.MaxRadius));
            return Failure(CircleCaliperFitV2FailureCode.SearchRegionOutsideImage, "Search ring must fit inside the image.", diagnostics: diagnostics);
        }

        return null;
    }

    private static HypothesisEvaluation EvaluateHypothesis(
        Mat gray,
        CircleCaliperFitV2Request request,
        CircleCaliperFitV2EdgePolarity polarity,
        CancellationToken cancellationToken)
    {
        var diagnostics = new List<CircleCaliperFitV2Diagnostic>
        {
            new("polarity", $"Evaluating {polarity}.")
        };
        var edgePoints = new List<CircleCaliperFitV2Point>(request.CaliperCount);
        var ambiguousCalipers = 0;
        var polarityName = polarity.ToString();
        var radiusSpan = request.MaxRadius - request.MinRadius;
        var expectedT = (request.NominalRadius - request.MinRadius) / radiusSpan;
        var expectedProfilePosition = expectedT * (request.ProfileSampleCount - 1);

        for (var i = 0; i < request.CaliperCount; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var angle = i * TwoPi / request.CaliperCount;
            var dirX = Math.Cos(angle);
            var dirY = Math.Sin(angle);
            var start = new Point2d(
                request.SearchCenterX + (request.MinRadius * dirX),
                request.SearchCenterY + (request.MinRadius * dirY));
            var end = new Point2d(
                request.SearchCenterX + (request.MaxRadius * dirX),
                request.SearchCenterY + (request.MaxRadius * dirY));

            var profile = IndustrialCaliperKernel.SampleBandProfile(
                gray,
                start,
                end,
                request.AveragingThickness,
                request.ProfileSampleCount);
            var threshold = request.EdgeThreshold > 0.0
                ? request.EdgeThreshold
                : IndustrialCaliperKernel.EstimateEdgeThreshold(profile, Math.Max(1.0, request.MinEdgeStrength));
            threshold = Math.Max(threshold, request.MinEdgeStrength);

            var edges = IndustrialCaliperKernel.DetectEdges(profile, threshold, polarityName, request.GaussianSigma)
                .Where(edge => edge.Strength >= request.MinEdgeStrength)
                .OrderBy(edge => Math.Abs(edge.Position - expectedProfilePosition))
                .ThenByDescending(edge => edge.Strength)
                .ThenBy(edge => edge.Position)
                .ToArray();

            if (edges.Length == 0)
            {
                continue;
            }

            if (edges.Length > 1 && IsCaliperAmbiguous(edges[0], edges[1], expectedProfilePosition, radiusSpan, request.ProfileSampleCount))
            {
                ambiguousCalipers++;
            }

            var selected = edges[0];
            var point = IndustrialCaliperKernel.InterpolatePosition(start, end, selected.Position, request.ProfileSampleCount);
            var radius = request.MinRadius + ((selected.Position / Math.Max(request.ProfileSampleCount - 1, 1)) * radiusSpan);
            edgePoints.Add(new CircleCaliperFitV2Point(
                point.X,
                point.Y,
                i,
                angle * 180.0 / Math.PI,
                radius,
                selected.Strength,
                selected.Polarity.ToString()));
        }

        diagnostics.Add(new CircleCaliperFitV2Diagnostic("edges.collected", "Collected caliper edge points.", edgePoints.Count));
        diagnostics.Add(new CircleCaliperFitV2Diagnostic("edges.ambiguousCalipers", "Calipers with competing edge candidates.", ambiguousCalipers));

        if (edgePoints.Count < request.MinValidCalipers)
        {
            return HypothesisEvaluation.Fail(
                polarity,
                CircleCaliperFitV2FailureCode.InsufficientEdges,
                $"Collected {edgePoints.Count} edge points, below MinValidCalipers {request.MinValidCalipers}.",
                edgePoints,
                diagnostics);
        }

        if (ambiguousCalipers >= Math.Max(3, edgePoints.Count / 3))
        {
            return HypothesisEvaluation.Fail(
                polarity,
                CircleCaliperFitV2FailureCode.AmbiguousEdge,
                "Too many calipers had competing edges near the nominal radius.",
                edgePoints,
                diagnostics);
        }

        var inlierIndexes = Enumerable.Range(0, edgePoints.Count).ToList();
        var currentFit = FitCircle(edgePoints);
        if (!currentFit.IsValid)
        {
            return HypothesisEvaluation.Fail(
                polarity,
                CircleCaliperFitV2FailureCode.DegenerateFit,
                "Initial circle fit was degenerate.",
                edgePoints,
                diagnostics);
        }

        if (request.OutlierMode != CircleCaliperFitV2OutlierMode.None)
        {
            for (var iteration = 0; iteration < request.MaxOutlierIterations; iteration++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var residuals = inlierIndexes
                    .Select(index => Math.Abs(ComputeSignedResidual(edgePoints[index], currentFit)))
                    .ToArray();
                var median = MeasurementStatisticsHelper.ComputeMedian(residuals);
                var sigma = MeasurementStatisticsHelper.ComputeScaledMedianAbsoluteDeviation(residuals, median);
                var threshold = Math.Max(0.35, request.OutlierThreshold * Math.Max(sigma, 0.05));
                if (request.OutlierMode == CircleCaliperFitV2OutlierMode.Huber)
                {
                    threshold = Math.Max(threshold, request.MaxResidualRmse * 1.5);
                }

                var nextIndexes = inlierIndexes
                    .Where(index => Math.Abs(ComputeSignedResidual(edgePoints[index], currentFit)) <= threshold)
                    .ToList();

                if (nextIndexes.Count < request.MinValidCalipers || nextIndexes.Count == inlierIndexes.Count)
                {
                    break;
                }

                inlierIndexes = nextIndexes;
                currentFit = FitCircle(inlierIndexes.Select(index => edgePoints[index]).ToArray());
                if (!currentFit.IsValid)
                {
                    return HypothesisEvaluation.Fail(
                        polarity,
                        CircleCaliperFitV2FailureCode.DegenerateFit,
                        "Circle fit became degenerate during outlier rejection.",
                        edgePoints,
                        diagnostics);
                }
            }
        }

        currentFit = FitCircle(inlierIndexes.Select(index => edgePoints[index]).ToArray());
        if (!currentFit.IsValid)
        {
            return HypothesisEvaluation.Fail(
                polarity,
                CircleCaliperFitV2FailureCode.DegenerateFit,
                "Final circle fit was degenerate.",
                edgePoints,
                diagnostics);
        }

        var inlierSet = inlierIndexes.ToHashSet();
        var inliers = inlierIndexes.Select(index => edgePoints[index]).ToList();
        var outliers = edgePoints.Where((_, index) => !inlierSet.Contains(index)).ToList();
        var residualsFinal = inliers.Select(point => ComputeSignedResidual(point, currentFit)).ToArray();
        var residualRmse = Math.Sqrt(residualsFinal.Select(static value => value * value).Average());
        var residualMax = residualsFinal.Select(Math.Abs).DefaultIfEmpty(double.NaN).Max();
        var coverageRatio = (double)inliers.Count / request.CaliperCount;
        var angularCoverage = ComputeAngularCoverageDegrees(inliers);
        var medianStrength = MeasurementStatisticsHelper.ComputeMedian(inliers.Select(static point => point.Strength).ToArray());

        diagnostics.Add(new CircleCaliperFitV2Diagnostic("fit.radius", "Final fitted radius.", currentFit.Radius));
        diagnostics.Add(new CircleCaliperFitV2Diagnostic("fit.residualRmse", "Final residual RMSE.", residualRmse));
        diagnostics.Add(new CircleCaliperFitV2Diagnostic("coverage.ratio", "Caliper coverage ratio.", coverageRatio));
        diagnostics.Add(new CircleCaliperFitV2Diagnostic("coverage.degrees", "Angular coverage in degrees.", angularCoverage));

        if (currentFit.Radius < request.MinRadius || currentFit.Radius > request.MaxRadius)
        {
            return HypothesisEvaluation.Fail(
                polarity,
                CircleCaliperFitV2FailureCode.RadiusOutOfRange,
                "Fitted radius is outside the search radius range.",
                edgePoints,
                diagnostics);
        }

        if (inliers.Count < request.MinValidCalipers)
        {
            return HypothesisEvaluation.Fail(
                polarity,
                CircleCaliperFitV2FailureCode.InsufficientEdges,
                "Inlier count is below MinValidCalipers after outlier rejection.",
                edgePoints,
                diagnostics);
        }

        if (coverageRatio < request.MinCoverageRatio || angularCoverage < request.MinAngularCoverageDegrees)
        {
            return HypothesisEvaluation.Fail(
                polarity,
                CircleCaliperFitV2FailureCode.InsufficientCoverage,
                "Inlier coverage is below the configured gate.",
                edgePoints,
                diagnostics);
        }

        if (residualRmse > request.MaxResidualRmse)
        {
            return HypothesisEvaluation.Fail(
                polarity,
                CircleCaliperFitV2FailureCode.ResidualTooHigh,
                "Residual RMSE is above MaxResidualRmse.",
                edgePoints,
                diagnostics);
        }

        var residualScore = 1.0 - Math.Clamp(residualRmse / Math.Max(request.MaxResidualRmse, 1e-6), 0.0, 1.0);
        var strengthScore = Math.Clamp(medianStrength / 64.0, 0.0, 1.0);
        var countScore = Math.Clamp((double)inliers.Count / request.CaliperCount, 0.0, 1.0);
        var coverageScore = Math.Clamp(angularCoverage / 360.0, 0.0, 1.0);
        var score = (residualScore * 0.35) + (strengthScore * 0.20) + (countScore * 0.20) + (coverageScore * 0.25);
        var uncertainty = Math.Max(residualRmse, 1.0 / Math.Sqrt(Math.Max(inliers.Count, 1)));
        var confidence = Math.Clamp(score * MeasurementStatisticsHelper.ComputeConfidenceFromUncertainty(uncertainty), 0.0, 1.0);

        return HypothesisEvaluation.Pass(
            polarity,
            currentFit,
            edgePoints,
            inliers,
            outliers,
            diagnostics,
            coverageRatio,
            angularCoverage,
            residualRmse,
            residualMax,
            medianStrength,
            confidence,
            uncertainty,
            score);
    }

    private static bool IsCaliperAmbiguous(
        IndustrialCaliperEdge best,
        IndustrialCaliperEdge second,
        double expectedProfilePosition,
        double radiusSpan,
        int sampleCount)
    {
        var bestDistance = Math.Abs(best.Position - expectedProfilePosition);
        var secondDistance = Math.Abs(second.Position - expectedProfilePosition);
        var samplePitch = radiusSpan / Math.Max(sampleCount - 1, 1);
        var radiusTolerancePx = Math.Max(1.5, radiusSpan * 0.30);
        var sampleTolerance = radiusTolerancePx / Math.Max(samplePitch, 1e-6);
        return secondDistance <= bestDistance + sampleTolerance &&
            second.Strength >= best.Strength * 0.80;
    }

    private static bool IsGlobalPolarityAmbiguous(HypothesisEvaluation best, HypothesisEvaluation second)
    {
        if (best.Score - second.Score > 0.05)
        {
            return false;
        }

        if (!best.Fit.IsValid || !second.Fit.IsValid)
        {
            return false;
        }

        var centerDistance = Math.Sqrt(
            Math.Pow(best.Fit.CenterX - second.Fit.CenterX, 2) +
            Math.Pow(best.Fit.CenterY - second.Fit.CenterY, 2));
        var radiusDistance = Math.Abs(best.Fit.Radius - second.Fit.Radius);
        return centerDistance > 1.0 || radiusDistance > 1.0;
    }

    private static int FailurePriority(CircleCaliperFitV2FailureCode code)
    {
        return code switch
        {
            CircleCaliperFitV2FailureCode.AmbiguousEdge => 0,
            CircleCaliperFitV2FailureCode.InsufficientCoverage => 1,
            CircleCaliperFitV2FailureCode.ResidualTooHigh => 2,
            CircleCaliperFitV2FailureCode.RadiusOutOfRange => 3,
            CircleCaliperFitV2FailureCode.DegenerateFit => 4,
            CircleCaliperFitV2FailureCode.InsufficientEdges => 5,
            _ => 6
        };
    }

    private static CircleFit FitCircle(IReadOnlyList<CircleCaliperFitV2Point> points)
    {
        var n = points.Count;
        if (n < 3)
        {
            return CircleFit.Invalid;
        }

        var meanX = points.Average(static point => point.X);
        var meanY = points.Average(static point => point.Y);
        var scale = points
            .Select(point => Math.Sqrt(Math.Pow(point.X - meanX, 2) + Math.Pow(point.Y - meanY, 2)))
            .DefaultIfEmpty(0.0)
            .Average();
        if (!IsFinite(scale) || scale < 1e-9)
        {
            return CircleFit.Invalid;
        }

        double sumX = 0.0;
        double sumY = 0.0;
        double sumX2 = 0.0;
        double sumY2 = 0.0;
        double sumXY = 0.0;
        double sumX3 = 0.0;
        double sumY3 = 0.0;
        double sumX2Y = 0.0;
        double sumXY2 = 0.0;

        foreach (var point in points)
        {
            var x = (point.X - meanX) / scale;
            var y = (point.Y - meanY) / scale;
            sumX += x;
            sumY += y;
            sumX2 += x * x;
            sumY2 += y * y;
            sumXY += x * y;
            sumX3 += x * x * x;
            sumY3 += y * y * y;
            sumX2Y += x * x * y;
            sumXY2 += x * y * y;
        }

        var a = (n * sumX2) - (sumX * sumX);
        var b = (n * sumXY) - (sumX * sumY);
        var c = (n * sumY2) - (sumY * sumY);
        var d = 0.5 * ((n * sumX3) + (n * sumXY2) - (sumX * sumX2) - (sumX * sumY2));
        var e = 0.5 * ((n * sumX2Y) + (n * sumY3) - (sumY * sumX2) - (sumY * sumY2));

        var det = (a * c) - (b * b);
        if (Math.Abs(det) < 1e-10)
        {
            return CircleFit.Invalid;
        }

        var normalizedCx = ((d * c) - (b * e)) / det;
        var normalizedCy = ((a * e) - (b * d)) / det;
        var normalizedRadiusSquared =
            (sumX2 / n) - ((2.0 * normalizedCx * sumX) / n) + (normalizedCx * normalizedCx) +
            (sumY2 / n) - ((2.0 * normalizedCy * sumY) / n) + (normalizedCy * normalizedCy);
        if (normalizedRadiusSquared <= 0.0)
        {
            return CircleFit.Invalid;
        }

        var centerX = (normalizedCx * scale) + meanX;
        var centerY = (normalizedCy * scale) + meanY;
        var radius = Math.Sqrt(normalizedRadiusSquared) * scale;
        return IsFinite(centerX) && IsFinite(centerY) && IsFinite(radius) && radius > 0.0
            ? new CircleFit(true, centerX, centerY, radius)
            : CircleFit.Invalid;
    }

    private static double ComputeSignedResidual(CircleCaliperFitV2Point point, CircleFit fit)
    {
        return Math.Sqrt(Math.Pow(point.X - fit.CenterX, 2) + Math.Pow(point.Y - fit.CenterY, 2)) - fit.Radius;
    }

    private static double ComputeAngularCoverageDegrees(IReadOnlyList<CircleCaliperFitV2Point> points)
    {
        if (points.Count == 0)
        {
            return 0.0;
        }

        if (points.Count == 1)
        {
            return 0.0;
        }

        var angles = points
            .Select(static point => NormalizeDegrees(point.AngleDegrees))
            .OrderBy(static angle => angle)
            .ToArray();
        var maxGap = 0.0;
        for (var i = 0; i < angles.Length; i++)
        {
            var current = angles[i];
            var next = i == angles.Length - 1 ? angles[0] + 360.0 : angles[i + 1];
            maxGap = Math.Max(maxGap, next - current);
        }

        return Math.Clamp(360.0 - maxGap, 0.0, 360.0);
    }

    private static double NormalizeDegrees(double angle)
    {
        var normalized = angle % 360.0;
        return normalized < 0.0 ? normalized + 360.0 : normalized;
    }

    private static CircleCaliperFitV2Result Failure(
        CircleCaliperFitV2FailureCode code,
        string message,
        CircleCaliperFitV2EdgePolarity polarity = CircleCaliperFitV2EdgePolarity.Auto,
        IReadOnlyList<CircleCaliperFitV2Point>? edgePoints = null,
        IReadOnlyList<CircleCaliperFitV2Diagnostic>? diagnostics = null)
    {
        var points = edgePoints ?? Array.Empty<CircleCaliperFitV2Point>();
        return new CircleCaliperFitV2Result
        {
            Success = false,
            FailureCode = code,
            FailureMessage = BoundMessage(message),
            EdgePoints = points.ToArray(),
            InlierPoints = Array.Empty<CircleCaliperFitV2Point>(),
            OutlierPoints = Array.Empty<CircleCaliperFitV2Point>(),
            CollectedPointCount = points.Count,
            ValidCaliperCount = points.Count,
            RejectedCaliperCount = 0,
            ResolvedPolarity = polarity,
            Diagnostics = diagnostics?.ToArray() ?? Array.Empty<CircleCaliperFitV2Diagnostic>()
        };
    }

    private static string BoundMessage(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return string.Empty;
        }

        message = message.Trim();
        return message.Length <= 240 ? message : message[..240];
    }

    private static bool IsFinite(double value)
    {
        return !double.IsNaN(value) && !double.IsInfinity(value);
    }

    private readonly record struct CircleFit(bool IsValid, double CenterX, double CenterY, double Radius)
    {
        public static CircleFit Invalid => new(false, double.NaN, double.NaN, double.NaN);
    }

    private sealed class HypothesisEvaluation
    {
        private HypothesisEvaluation(
            bool success,
            CircleCaliperFitV2EdgePolarity resolvedPolarity,
            CircleCaliperFitV2FailureCode failureCode,
            string failureMessage,
            CircleFit fit,
            IReadOnlyList<CircleCaliperFitV2Point> edgePoints,
            IReadOnlyList<CircleCaliperFitV2Point> inlierPoints,
            IReadOnlyList<CircleCaliperFitV2Point> outlierPoints,
            IReadOnlyList<CircleCaliperFitV2Diagnostic> diagnostics,
            double coverageRatio,
            double angularCoverageDegrees,
            double residualRmse,
            double residualMax,
            double medianEdgeStrength,
            double confidence,
            double uncertaintyPx,
            double score)
        {
            Success = success;
            ResolvedPolarity = resolvedPolarity;
            FailureCode = failureCode;
            FailureMessage = failureMessage;
            Fit = fit;
            EdgePoints = edgePoints;
            InlierPoints = inlierPoints;
            OutlierPoints = outlierPoints;
            Diagnostics = diagnostics;
            CoverageRatio = coverageRatio;
            AngularCoverageDegrees = angularCoverageDegrees;
            ResidualRmse = residualRmse;
            ResidualMax = residualMax;
            MedianEdgeStrength = medianEdgeStrength;
            Confidence = confidence;
            UncertaintyPx = uncertaintyPx;
            Score = score;
        }

        public bool Success { get; }
        public CircleCaliperFitV2EdgePolarity ResolvedPolarity { get; }
        public CircleCaliperFitV2FailureCode FailureCode { get; }
        public string FailureMessage { get; }
        public CircleFit Fit { get; }
        public IReadOnlyList<CircleCaliperFitV2Point> EdgePoints { get; }
        public IReadOnlyList<CircleCaliperFitV2Point> InlierPoints { get; }
        public IReadOnlyList<CircleCaliperFitV2Point> OutlierPoints { get; }
        public IReadOnlyList<CircleCaliperFitV2Diagnostic> Diagnostics { get; }
        public double CoverageRatio { get; }
        public double AngularCoverageDegrees { get; }
        public double ResidualRmse { get; }
        public double ResidualMax { get; }
        public double MedianEdgeStrength { get; }
        public double Confidence { get; }
        public double UncertaintyPx { get; }
        public double Score { get; }

        public static HypothesisEvaluation Fail(
            CircleCaliperFitV2EdgePolarity polarity,
            CircleCaliperFitV2FailureCode code,
            string message,
            IReadOnlyList<CircleCaliperFitV2Point> edgePoints,
            IReadOnlyList<CircleCaliperFitV2Diagnostic> diagnostics)
        {
            return new HypothesisEvaluation(
                false,
                polarity,
                code,
                BoundMessage(message),
                CircleFit.Invalid,
                edgePoints.ToArray(),
                Array.Empty<CircleCaliperFitV2Point>(),
                Array.Empty<CircleCaliperFitV2Point>(),
                diagnostics.ToArray(),
                0.0,
                0.0,
                double.NaN,
                double.NaN,
                edgePoints.Count > 0 ? MeasurementStatisticsHelper.ComputeMedian(edgePoints.Select(static point => point.Strength).ToArray()) : double.NaN,
                0.0,
                double.NaN,
                0.0);
        }

        public static HypothesisEvaluation Pass(
            CircleCaliperFitV2EdgePolarity polarity,
            CircleFit fit,
            IReadOnlyList<CircleCaliperFitV2Point> edgePoints,
            IReadOnlyList<CircleCaliperFitV2Point> inlierPoints,
            IReadOnlyList<CircleCaliperFitV2Point> outlierPoints,
            IReadOnlyList<CircleCaliperFitV2Diagnostic> diagnostics,
            double coverageRatio,
            double angularCoverageDegrees,
            double residualRmse,
            double residualMax,
            double medianEdgeStrength,
            double confidence,
            double uncertaintyPx,
            double score)
        {
            return new HypothesisEvaluation(
                true,
                polarity,
                CircleCaliperFitV2FailureCode.None,
                string.Empty,
                fit,
                edgePoints.ToArray(),
                inlierPoints.ToArray(),
                outlierPoints.ToArray(),
                diagnostics.ToArray(),
                coverageRatio,
                angularCoverageDegrees,
                residualRmse,
                residualMax,
                medianEdgeStrength,
                confidence,
                uncertaintyPx,
                score);
        }

        public CircleCaliperFitV2Result ToResult(int caliperCount)
        {
            if (!Success)
            {
                return Failure(FailureCode, FailureMessage, ResolvedPolarity, EdgePoints, Diagnostics);
            }

            return new CircleCaliperFitV2Result
            {
                Success = true,
                FailureCode = CircleCaliperFitV2FailureCode.None,
                CenterX = Fit.CenterX,
                CenterY = Fit.CenterY,
                Radius = Fit.Radius,
                EdgePoints = EdgePoints.ToArray(),
                InlierPoints = InlierPoints.ToArray(),
                OutlierPoints = OutlierPoints.ToArray(),
                ValidCaliperCount = InlierPoints.Count,
                RejectedCaliperCount = Math.Max(0, caliperCount - InlierPoints.Count),
                CollectedPointCount = EdgePoints.Count,
                CoverageRatio = CoverageRatio,
                AngularCoverageDegrees = AngularCoverageDegrees,
                ResidualRmse = ResidualRmse,
                ResidualMax = ResidualMax,
                MedianEdgeStrength = MedianEdgeStrength,
                ResolvedPolarity = ResolvedPolarity,
                Confidence = Confidence,
                UncertaintyPx = UncertaintyPx,
                Diagnostics = Diagnostics.ToArray()
            };
        }
    }
}
