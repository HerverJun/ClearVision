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
    public const long MaxSamplingWorkUnits = 8_000_000;

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
    private const int MaxDiagnosticCount = 64;
    private const int MaxDiagnosticMessageLength = 160;
    private const double MinimumRobustScale = 0.05;
    private const double MinimumClassificationThreshold = 0.35;

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
            var best = successes[0];
            return HypothesisEvaluation.FailWithEvidence(
                best.ResolvedPolarity,
                CircleCaliperFitV2FailureCode.AmbiguousEdge,
                "Auto polarity produced indistinguishable circle hypotheses.",
                best.Fit,
                best.EdgePoints,
                best.InlierPoints,
                best.OutlierPoints,
                best.Diagnostics,
                best.CoverageRatio,
                best.AngularCoverageDegrees,
                best.ResidualRmse,
                best.ResidualMax,
                best.MedianEdgeStrength).ToResult(request.CaliperCount);
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

        var workUnits = ComputeSamplingWorkUnits(request);
        if (workUnits > CircleCaliperFitV2Request.MaxSamplingWorkUnits)
        {
            diagnostics.Add(new CircleCaliperFitV2Diagnostic(
                "sampling.work-budget",
                "Sampling work units exceed the fixed CaliperFitV2 budget.",
                workUnits));
            return Failure(
                CircleCaliperFitV2FailureCode.InvalidInput,
                $"Sampling work units {workUnits} exceed maximum {CircleCaliperFitV2Request.MaxSamplingWorkUnits}.",
                diagnostics: diagnostics);
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
                request.ProfileSampleCount,
                cancellationToken);
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

        var initialFit = FitCircle(edgePoints);
        if (!initialFit.IsValid)
        {
            return HypothesisEvaluation.Fail(
                polarity,
                CircleCaliperFitV2FailureCode.DegenerateFit,
                "Initial circle fit was degenerate.",
                edgePoints,
                diagnostics);
        }

        var robust = BuildRobustFit(edgePoints, initialFit, request, diagnostics, cancellationToken);
        if (!robust.Fit.IsValid)
        {
            return HypothesisEvaluation.Fail(
                polarity,
                CircleCaliperFitV2FailureCode.DegenerateFit,
                "Final circle fit was degenerate.",
                edgePoints,
                diagnostics);
        }

        var currentFit = robust.Fit;
        var inliers = robust.InlierIndexes.Select(index => edgePoints[index]).ToList();
        var outlierSet = robust.OutlierIndexes.ToHashSet();
        var outliers = edgePoints
            .Where((_, index) => outlierSet.Contains(index))
            .ToList();
        var (residualRmse, residualMax) = ComputeResidualStats(inliers, currentFit);
        var coverageRatio = (double)inliers.Count / request.CaliperCount;
        var angularCoverage = ComputeAngularCoverageDegrees(inliers);
        var medianStrength = inliers.Count > 0
            ? MeasurementStatisticsHelper.ComputeMedian(inliers.Select(static point => point.Strength).ToArray())
            : double.NaN;

        diagnostics.Add(new CircleCaliperFitV2Diagnostic("fit.radius", "Final fitted radius.", currentFit.Radius));
        diagnostics.Add(new CircleCaliperFitV2Diagnostic("fit.residualRmse", "Final residual RMSE.", residualRmse));
        diagnostics.Add(new CircleCaliperFitV2Diagnostic("coverage.ratio", "Caliper coverage ratio.", coverageRatio));
        diagnostics.Add(new CircleCaliperFitV2Diagnostic("coverage.degrees", "Angular coverage in degrees.", angularCoverage));

        if (currentFit.Radius < request.MinRadius || currentFit.Radius > request.MaxRadius)
        {
            return HypothesisEvaluation.FailWithEvidence(
                polarity,
                CircleCaliperFitV2FailureCode.RadiusOutOfRange,
                "Fitted radius is outside the search radius range.",
                currentFit,
                edgePoints,
                inliers,
                outliers,
                diagnostics,
                coverageRatio,
                angularCoverage,
                residualRmse,
                residualMax,
                medianStrength);
        }

        if (inliers.Count < request.MinValidCalipers)
        {
            return HypothesisEvaluation.FailWithEvidence(
                polarity,
                CircleCaliperFitV2FailureCode.InsufficientEdges,
                "Inlier count is below MinValidCalipers after outlier rejection.",
                currentFit,
                edgePoints,
                inliers,
                outliers,
                diagnostics,
                coverageRatio,
                angularCoverage,
                residualRmse,
                residualMax,
                medianStrength);
        }

        if (coverageRatio < request.MinCoverageRatio || angularCoverage < request.MinAngularCoverageDegrees)
        {
            return HypothesisEvaluation.FailWithEvidence(
                polarity,
                CircleCaliperFitV2FailureCode.InsufficientCoverage,
                "Inlier coverage is below the configured gate.",
                currentFit,
                edgePoints,
                inliers,
                outliers,
                diagnostics,
                coverageRatio,
                angularCoverage,
                residualRmse,
                residualMax,
                medianStrength);
        }

        if (residualRmse > request.MaxResidualRmse)
        {
            return HypothesisEvaluation.FailWithEvidence(
                polarity,
                CircleCaliperFitV2FailureCode.ResidualTooHigh,
                "Residual RMSE is above MaxResidualRmse.",
                currentFit,
                edgePoints,
                inliers,
                outliers,
                diagnostics,
                coverageRatio,
                angularCoverage,
                residualRmse,
                residualMax,
                medianStrength);
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

    private static RobustFitResult BuildRobustFit(
        IReadOnlyList<CircleCaliperFitV2Point> edgePoints,
        CircleFit initialFit,
        CircleCaliperFitV2Request request,
        List<CircleCaliperFitV2Diagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        return request.OutlierMode switch
        {
            CircleCaliperFitV2OutlierMode.None => BuildNoOutlierFit(edgePoints, initialFit, diagnostics),
            CircleCaliperFitV2OutlierMode.Huber => BuildHuberFit(edgePoints, initialFit, request, diagnostics, cancellationToken),
            _ => BuildMadFit(edgePoints, initialFit, request, diagnostics, cancellationToken)
        };
    }

    private static RobustFitResult BuildNoOutlierFit(
        IReadOnlyList<CircleCaliperFitV2Point> edgePoints,
        CircleFit initialFit,
        List<CircleCaliperFitV2Diagnostic> diagnostics)
    {
        diagnostics.Add(new CircleCaliperFitV2Diagnostic("outlier.mode", "Outlier rejection disabled; all collected edge points remain inliers."));
        var inlierIndexes = Enumerable.Range(0, edgePoints.Count).ToArray();
        return new RobustFitResult(initialFit, inlierIndexes, Array.Empty<int>(), double.NaN, double.PositiveInfinity, 0, true);
    }

    private static RobustFitResult BuildMadFit(
        IReadOnlyList<CircleCaliperFitV2Point> edgePoints,
        CircleFit initialFit,
        CircleCaliperFitV2Request request,
        List<CircleCaliperFitV2Diagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        var currentFit = initialFit;
        var inlierIndexes = Enumerable.Range(0, edgePoints.Count).ToList();
        var robustScale = double.NaN;
        var threshold = double.PositiveInfinity;
        var iterations = 0;
        var converged = true;

        for (var iteration = 0; iteration < request.MaxOutlierIterations; iteration++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            iterations = iteration + 1;
            var residuals = inlierIndexes
                .Select(index => ComputeSignedResidual(edgePoints[index], currentFit))
                .ToArray();
            robustScale = ComputeRobustScale(residuals);
            threshold = Math.Max(MinimumClassificationThreshold, request.OutlierThreshold * robustScale);
            var nextIndexes = inlierIndexes
                .Where(index => Math.Abs(ComputeSignedResidual(edgePoints[index], currentFit)) <= threshold)
                .ToList();

            if (nextIndexes.Count == inlierIndexes.Count)
            {
                break;
            }

            if (nextIndexes.Count < 3)
            {
                converged = false;
                break;
            }

            var nextFit = FitCircle(nextIndexes.Select(index => edgePoints[index]).ToArray());
            if (!nextFit.IsValid)
            {
                return RobustFitResult.Invalid;
            }

            inlierIndexes = nextIndexes;
            currentFit = nextFit;
        }

        var inlierSet = inlierIndexes.ToHashSet();
        var outlierIndexes = Enumerable.Range(0, edgePoints.Count)
            .Where(index => !inlierSet.Contains(index))
            .ToArray();

        diagnostics.Add(new CircleCaliperFitV2Diagnostic("outlier.mode", "MAD hard-rejection outlier mode."));
        diagnostics.Add(new CircleCaliperFitV2Diagnostic("outlier.iterations", "MAD outlier rejection iterations.", iterations));
        diagnostics.Add(new CircleCaliperFitV2Diagnostic("outlier.robustScale", "Final MAD robust scale.", robustScale));
        diagnostics.Add(new CircleCaliperFitV2Diagnostic("outlier.threshold", "Final MAD classification threshold.", threshold));
        diagnostics.Add(new CircleCaliperFitV2Diagnostic("outlier.converged", converged ? "MAD rejection converged." : "MAD rejection stopped before convergence.", converged ? 1.0 : 0.0));

        return new RobustFitResult(currentFit, inlierIndexes.ToArray(), outlierIndexes, robustScale, threshold, iterations, converged);
    }

    private static RobustFitResult BuildHuberFit(
        IReadOnlyList<CircleCaliperFitV2Point> edgePoints,
        CircleFit initialFit,
        CircleCaliperFitV2Request request,
        List<CircleCaliperFitV2Diagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        var currentFit = initialFit;
        var weights = Enumerable.Repeat(1.0, edgePoints.Count).ToArray();
        var robustScale = double.NaN;
        var delta = double.PositiveInfinity;
        var iterations = 0;
        var converged = request.MaxOutlierIterations == 0;

        for (var iteration = 0; iteration < request.MaxOutlierIterations; iteration++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            iterations = iteration + 1;
            var residuals = edgePoints
                .Select(point => ComputeSignedResidual(point, currentFit))
                .ToArray();
            robustScale = ComputeRobustScale(residuals);
            delta = Math.Max(MinimumClassificationThreshold, request.OutlierThreshold * robustScale);
            var nextWeights = residuals
                .Select(residual =>
                {
                    var absResidual = Math.Abs(residual);
                    return absResidual <= delta || absResidual <= 1e-12
                        ? 1.0
                        : delta / absResidual;
                })
                .ToArray();

            var nextFit = FitCircleWeighted(edgePoints, nextWeights);
            if (!nextFit.IsValid)
            {
                return RobustFitResult.Invalid;
            }

            var centerDelta = Math.Sqrt(
                Math.Pow(nextFit.CenterX - currentFit.CenterX, 2) +
                Math.Pow(nextFit.CenterY - currentFit.CenterY, 2));
            var radiusDelta = Math.Abs(nextFit.Radius - currentFit.Radius);
            var maxWeightDelta = nextWeights
                .Zip(weights, static (next, previous) => Math.Abs(next - previous))
                .DefaultIfEmpty(0.0)
                .Max();

            weights = nextWeights;
            currentFit = nextFit;

            if (centerDelta <= 1e-6 || radiusDelta <= 1e-6 || maxWeightDelta <= 1e-6)
            {
                converged = true;
                break;
            }
        }

        var finalResiduals = edgePoints
            .Select(point => ComputeSignedResidual(point, currentFit))
            .ToArray();
        robustScale = ComputeRobustScale(finalResiduals);
        delta = Math.Max(MinimumClassificationThreshold, request.OutlierThreshold * robustScale);
        var inlierIndexes = finalResiduals
            .Select((residual, index) => new { residual, index })
            .Where(item => Math.Abs(item.residual) <= delta)
            .Select(item => item.index)
            .ToArray();
        var inlierSet = inlierIndexes.ToHashSet();
        var outlierIndexes = Enumerable.Range(0, edgePoints.Count)
            .Where(index => !inlierSet.Contains(index))
            .ToArray();

        diagnostics.Add(new CircleCaliperFitV2Diagnostic("outlier.mode", "Huber IRLS weighted robust fit mode."));
        diagnostics.Add(new CircleCaliperFitV2Diagnostic("huber.iterations", "Huber IRLS iteration count.", iterations));
        diagnostics.Add(new CircleCaliperFitV2Diagnostic("huber.robustScale", "Final Huber robust scale.", robustScale));
        diagnostics.Add(new CircleCaliperFitV2Diagnostic("huber.delta", "Final Huber delta.", delta));
        diagnostics.Add(new CircleCaliperFitV2Diagnostic("huber.converged", converged ? "Huber IRLS converged." : "Huber IRLS reached iteration limit.", converged ? 1.0 : 0.0));

        return new RobustFitResult(currentFit, inlierIndexes, outlierIndexes, robustScale, delta, iterations, converged);
    }

    private static double ComputeRobustScale(IReadOnlyList<double> residuals)
    {
        if (residuals.Count == 0)
        {
            return MinimumRobustScale;
        }

        var finite = residuals.Where(IsFinite).ToArray();
        if (finite.Length == 0)
        {
            return MinimumRobustScale;
        }

        var median = MeasurementStatisticsHelper.ComputeMedian(finite);
        var scale = MeasurementStatisticsHelper.ComputeScaledMedianAbsoluteDeviation(finite, median);
        return Math.Max(MinimumRobustScale, scale);
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

    private static CircleFit FitCircleWeighted(IReadOnlyList<CircleCaliperFitV2Point> points, IReadOnlyList<double> weights)
    {
        if (points.Count < 3 || points.Count != weights.Count)
        {
            return CircleFit.Invalid;
        }

        var weightSum = weights.Where(IsFinite).Where(static weight => weight > 0.0).Sum();
        if (weightSum <= 1e-12)
        {
            return CircleFit.Invalid;
        }

        var meanX = 0.0;
        var meanY = 0.0;
        for (var i = 0; i < points.Count; i++)
        {
            var weight = Math.Max(0.0, weights[i]);
            meanX += points[i].X * weight;
            meanY += points[i].Y * weight;
        }

        meanX /= weightSum;
        meanY /= weightSum;

        var scale = 0.0;
        for (var i = 0; i < points.Count; i++)
        {
            var weight = Math.Max(0.0, weights[i]);
            scale += weight * Math.Sqrt(Math.Pow(points[i].X - meanX, 2) + Math.Pow(points[i].Y - meanY, 2));
        }

        scale /= weightSum;
        if (!IsFinite(scale) || scale < 1e-9)
        {
            return CircleFit.Invalid;
        }

        double sumW = 0.0;
        double sumX = 0.0;
        double sumY = 0.0;
        double sumX2 = 0.0;
        double sumY2 = 0.0;
        double sumXY = 0.0;
        double sumX3 = 0.0;
        double sumY3 = 0.0;
        double sumX2Y = 0.0;
        double sumXY2 = 0.0;

        for (var i = 0; i < points.Count; i++)
        {
            var weight = Math.Max(0.0, weights[i]);
            if (weight <= 0.0 || !IsFinite(weight))
            {
                continue;
            }

            var x = (points[i].X - meanX) / scale;
            var y = (points[i].Y - meanY) / scale;
            sumW += weight;
            sumX += weight * x;
            sumY += weight * y;
            sumX2 += weight * x * x;
            sumY2 += weight * y * y;
            sumXY += weight * x * y;
            sumX3 += weight * x * x * x;
            sumY3 += weight * y * y * y;
            sumX2Y += weight * x * x * y;
            sumXY2 += weight * x * y * y;
        }

        if (sumW <= 1e-12)
        {
            return CircleFit.Invalid;
        }

        var a = (sumW * sumX2) - (sumX * sumX);
        var b = (sumW * sumXY) - (sumX * sumY);
        var c = (sumW * sumY2) - (sumY * sumY);
        var d = 0.5 * ((sumW * sumX3) + (sumW * sumXY2) - (sumX * sumX2) - (sumX * sumY2));
        var e = 0.5 * ((sumW * sumX2Y) + (sumW * sumY3) - (sumY * sumX2) - (sumY * sumY2));

        var det = (a * c) - (b * b);
        if (Math.Abs(det) < 1e-10)
        {
            return CircleFit.Invalid;
        }

        var normalizedCx = ((d * c) - (b * e)) / det;
        var normalizedCy = ((a * e) - (b * d)) / det;
        var normalizedRadiusSquared =
            (sumX2 / sumW) - ((2.0 * normalizedCx * sumX) / sumW) + (normalizedCx * normalizedCx) +
            (sumY2 / sumW) - ((2.0 * normalizedCy * sumY) / sumW) + (normalizedCy * normalizedCy);
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

    private static (double Rmse, double Max) ComputeResidualStats(IReadOnlyList<CircleCaliperFitV2Point> points, CircleFit fit)
    {
        if (points.Count == 0 || !fit.IsValid)
        {
            return (double.NaN, double.NaN);
        }

        var residuals = points.Select(point => ComputeSignedResidual(point, fit)).ToArray();
        return (
            Math.Sqrt(residuals.Select(static value => value * value).Average()),
            residuals.Select(Math.Abs).DefaultIfEmpty(double.NaN).Max());
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

    private static long ComputeSamplingWorkUnits(CircleCaliperFitV2Request request)
    {
        checked
        {
            var acrossCount = (long)Math.Ceiling(Math.Max(1.0, request.AveragingThickness));
            return (long)request.CaliperCount * request.ProfileSampleCount * acrossCount;
        }
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
            ValidCaliperCount = 0,
            RejectedCaliperCount = 0,
            ResolvedPolarity = polarity,
            Diagnostics = BoundDiagnostics(diagnostics)
        };
    }

    private static IReadOnlyList<CircleCaliperFitV2Diagnostic> BoundDiagnostics(
        IReadOnlyList<CircleCaliperFitV2Diagnostic>? diagnostics)
    {
        if (diagnostics == null || diagnostics.Count == 0)
        {
            return Array.Empty<CircleCaliperFitV2Diagnostic>();
        }

        return diagnostics
            .Take(MaxDiagnosticCount)
            .Select(static diagnostic => new CircleCaliperFitV2Diagnostic(
                BoundDiagnosticCode(diagnostic.Code),
                BoundDiagnosticMessage(diagnostic.Message),
                diagnostic.Value))
            .ToArray();
    }

    private static string BoundDiagnosticCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return "diagnostic";
        }

        code = code.Trim();
        return code.Length <= 64 ? code : code[..64];
    }

    private static string BoundDiagnosticMessage(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return string.Empty;
        }

        message = message.Trim();
        return message.Length <= MaxDiagnosticMessageLength ? message : message[..MaxDiagnosticMessageLength];
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

    private readonly record struct RobustFitResult(
        CircleFit Fit,
        IReadOnlyList<int> InlierIndexes,
        IReadOnlyList<int> OutlierIndexes,
        double RobustScale,
        double Threshold,
        int Iterations,
        bool Converged)
    {
        public static RobustFitResult Invalid => new(
            CircleFit.Invalid,
            Array.Empty<int>(),
            Array.Empty<int>(),
            double.NaN,
            double.NaN,
            0,
            false);
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

        public static HypothesisEvaluation FailWithEvidence(
            CircleCaliperFitV2EdgePolarity polarity,
            CircleCaliperFitV2FailureCode code,
            string message,
            CircleFit fit,
            IReadOnlyList<CircleCaliperFitV2Point> edgePoints,
            IReadOnlyList<CircleCaliperFitV2Point> inlierPoints,
            IReadOnlyList<CircleCaliperFitV2Point> outlierPoints,
            IReadOnlyList<CircleCaliperFitV2Diagnostic> diagnostics,
            double coverageRatio,
            double angularCoverageDegrees,
            double residualRmse,
            double residualMax,
            double medianEdgeStrength)
        {
            return new HypothesisEvaluation(
                false,
                polarity,
                code,
                BoundMessage(message),
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
                return new CircleCaliperFitV2Result
                {
                    Success = false,
                    FailureCode = FailureCode,
                    FailureMessage = BoundMessage(FailureMessage),
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
                    Confidence = 0.0,
                    UncertaintyPx = double.NaN,
                    Diagnostics = BoundDiagnostics(Diagnostics)
                };
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
                Diagnostics = BoundDiagnostics(Diagnostics)
            };
        }
    }
}
