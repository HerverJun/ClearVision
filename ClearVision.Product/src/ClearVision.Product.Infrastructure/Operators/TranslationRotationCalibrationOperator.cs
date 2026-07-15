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
    DisplayName = "平移旋转标定",
    Description = "从图像/机器人点对拟合二维刚性或相似变换，支持可选的 RANSAC 与 Huber 稳健模式。",
    CategoryId = OperatorCategoryId.CalibrationAndCoordinates,
    IconName = "calibration",
    Keywords = new[] { "calibration", "translation", "rotation", "svd", "similarity", "ransac", "huber" },
    Version = "1.1.0"
)]
[InputPort("Image", "Image", PortDataType.Image, IsRequired = false)]
[OutputPort("CalibrationData", "Calibration Data", PortDataType.String)]
[OutputPort("CalibrationError", "Calibration Error", PortDataType.Float)]
[OutputPort("MaxCalibrationError", "Max Calibration Error", PortDataType.Float)]
[OutputPort("Accepted", "Accepted", PortDataType.Boolean)]
[OutputPort("TransformModel", "Transform Model", PortDataType.String)]
[OutputPort("RotationDeg", "Rotation (deg)", PortDataType.Float)]
[OutputPort("AngleConstraintApplied", "Angle Constraint Applied", PortDataType.Boolean)]
[OutputPort("RobustMode", "Robust Mode", PortDataType.String)]
[OutputPort("InlierCount", "Inlier Count", PortDataType.Integer)]
[OutputPort("OutlierCount", "Outlier Count", PortDataType.Integer)]
[OutputPort("Residuals", "Per-point Residuals", PortDataType.Any)]
[OutputPort("Diagnostics", "Fit Diagnostics", PortDataType.Any)]
[OperatorParam("CalibrationPoints", "Calibration Points", "string", DefaultValue = "[]")]
[OperatorParam("Method", "Method", "enum", DefaultValue = "LeastSquares", Options = new[] { "LeastSquares|LeastSquares", "SVD|SVD" })]
[OperatorParam("RobustMode", "Robust Mode", "enum", DefaultValue = "None", Options = new[] { "None|None", "Ransac|RANSAC", "Huber|Huber" })]
[OperatorParam("RobustResidualThreshold", "Robust Residual Threshold", "double", DefaultValue = 0.30, Min = 1e-12, Max = 1000000000000.0)]
[OperatorParam("RobustMaxIterations", "Robust Max Iterations", "int", DefaultValue = 256, Min = 1, Max = 10000)]
[OperatorParam("RobustMinInlierRatio", "Robust Minimum Inlier Ratio", "double", DefaultValue = 0.5, Min = 0.1, Max = 1.0)]
[OperatorParam("HuberDelta", "Huber Delta", "double", DefaultValue = 0.15, Min = 1e-12, Max = 1000000000000.0)]
[OperatorParam("SavePath", "Save Path", "file", DefaultValue = "", IsRequired = false)]
public class TranslationRotationCalibrationOperator : OperatorBase
{
    private const double DegenerateThreshold = 1e-12;
    private const double CollinearityRatioThreshold = 1e-10;
    private const double MaximumSupportedScale = 1e12;
    private const double AngleToleranceDeg = 5.0;

    public override OperatorType OperatorType => OperatorType.TranslationRotationCalibration;

    public TranslationRotationCalibrationOperator(ILogger<TranslationRotationCalibrationOperator> logger)
        : base(logger)
    {
    }

    protected override Task<OperatorExecutionOutput> ExecuteCoreAsync(
        Operator @operator,
        Dictionary<string, object>? inputs,
        CancellationToken cancellationToken)
    {
        if (!TryResolveConfiguration(@operator, out var configuration, out var configurationError))
        {
            return Task.FromResult(OperatorExecutionOutput.Failure(configurationError));
        }

        if (!TryParseCalibrationPoints(configuration.PointsJson, out var points, out var parseDiagnostics) || points.Count < 3)
        {
            return Task.FromResult(OperatorExecutionOutput.Failure(
                $"CalibrationPoints must contain at least 3 valid points. input={parseDiagnostics.InputCount}; valid={points.Count}; invalid={parseDiagnostics.Issues.Count}."));
        }

        if (configuration.RobustMode != RobustFitMode.None && parseDiagnostics.Issues.Count > 0)
        {
            return Task.FromResult(OperatorExecutionOutput.Failure(
                $"Robust calibration rejected malformed samples instead of silently discarding them: {string.Join(" | ", parseDiagnostics.Issues)}"));
        }

        if (!TryValidatePointGeometry(points, out var geometryError))
        {
            return Task.FromResult(OperatorExecutionOutput.Failure(
                $"Calibration failed: mode={configuration.RobustMode}; reason={geometryError}"));
        }

        if (!TryResolveAngleConstraint(
                points,
                configuration.RobustMode,
                configuration.MinInlierRatio,
                out var angleResolution,
                out var angleError))
        {
            return Task.FromResult(OperatorExecutionOutput.Failure(
                $"Calibration failed: mode={configuration.RobustMode}; reason={angleError}"));
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (!TryFit(points, configuration, angleResolution, cancellationToken, out var fit, out var fitError))
        {
            return Task.FromResult(OperatorExecutionOutput.Failure(
                $"Calibration failed: mode={configuration.RobustMode}; reason={fitError}"));
        }

        var inlierStats = ComputeErrorStats(fit.Residuals, fit.InlierPositions);
        var allStats = configuration.RobustMode == RobustFitMode.None
            ? inlierStats
            : ComputeErrorStats(fit.Residuals, Enumerable.Range(0, points.Count));
        var solvedRotationDeg = Math.Atan2(fit.Matrix[1][0], fit.Matrix[0][0]) * (180.0 / Math.PI);
        var accepted = fit.Converged && inlierStats.RmsError <= 0.15 && inlierStats.MaxError <= 0.30;
        var inlierOriginalIndices = fit.InlierPositions.Select(position => points[position].OriginalIndex).OrderBy(index => index).ToArray();
        var outlierOriginalIndices = fit.OutlierPositions.Select(position => points[position].OriginalIndex).OrderBy(index => index).ToArray();
        var residualOutput = new Dictionary<int, double>();
        if (configuration.RobustMode != RobustFitMode.None)
        {
            for (var i = 0; i < points.Count; i++) residualOutput[points[i].OriginalIndex] = fit.Residuals[i];
        }

        var diagnostics = new List<string>
        {
            $"method={configuration.SolveMethod}",
            $"robust_mode={configuration.RobustMode}",
            $"estimated_scale={Format(fit.Scale)}",
            $"estimated_rotation_deg={Format(solvedRotationDeg)}",
            $"accepted={accepted}"
        };
        if (configuration.RobustMode != RobustFitMode.None)
        {
            diagnostics.AddRange([
                $"input_sample_count={parseDiagnostics.InputCount}",
                $"valid_sample_count={points.Count}",
                $"invalid_sample_count={parseDiagnostics.Issues.Count}",
                $"iterations={fit.Iterations}",
                $"converged={fit.Converged}",
                $"termination_reason={fit.TerminationReason}",
                $"inlier_count={fit.InlierPositions.Count}",
                $"outlier_count={fit.OutlierPositions.Count}",
                $"inlier_indices={JsonSerializer.Serialize(inlierOriginalIndices)}",
                $"outlier_indices={JsonSerializer.Serialize(outlierOriginalIndices)}",
                $"residuals={JsonSerializer.Serialize(residualOutput)}",
                $"inlier_rms_error={Format(inlierStats.RmsError)}",
                $"inlier_max_error={Format(inlierStats.MaxError)}",
                $"all_sample_rms_error={Format(allStats.RmsError)}",
                $"all_sample_max_error={Format(allStats.MaxError)}"
            ]);
        }
        else if (parseDiagnostics.Issues.Count > 0)
        {
            diagnostics.Add($"input_sample_count={parseDiagnostics.InputCount}");
            diagnostics.Add($"valid_sample_count={points.Count}");
            diagnostics.Add($"invalid_sample_count={parseDiagnostics.Issues.Count}");
        }
        diagnostics.AddRange(parseDiagnostics.Issues.Select(issue => $"invalid_sample={issue}"));
        if (angleResolution.Constraint.HasConstraint)
        {
            diagnostics.Add("input_angle_present=true");
            diagnostics.Add($"angle_constraint_deg={Format(angleResolution.Constraint.RotationDeg)}");
            diagnostics.Add($"angle_spread_deg={Format(angleResolution.Constraint.MaxDeviationDeg)}");
            diagnostics.Add($"angle_outlier_indices={JsonSerializer.Serialize(angleResolution.AngleOutlierOriginalIndices)}");
        }

        var transformModel = configuration.SolveMethod == SolveMethod.RigidSvd
            ? TransformModelV2.Rigid
            : TransformModelV2.Similarity;
        var bundle = new CalibrationBundleV2
        {
            CalibrationKind = CalibrationKindV2.RigidTransform2D,
            TransformModel = transformModel,
            SourceFrame = "image",
            TargetFrame = "robot",
            Unit = "mm",
            Transform2D = new CalibrationTransform2DV2
            {
                Model = transformModel,
                Matrix = fit.Matrix,
                PixelSizeX = fit.Scale,
                PixelSizeY = fit.Scale
            },
            Quality = new CalibrationQualityV2
            {
                Accepted = accepted,
                MeanError = inlierStats.RmsError,
                MaxError = inlierStats.MaxError,
                InlierCount = fit.InlierPositions.Count,
                TotalSampleCount = parseDiagnostics.InputCount,
                Diagnostics = diagnostics
            },
            ProducerOperator = nameof(TranslationRotationCalibrationOperator)
        };

        var calibrationData = CalibrationBundleV2Json.Serialize(bundle);
        if (!string.IsNullOrWhiteSpace(configuration.SavePath))
        {
            TrySaveCalibrationBundle(configuration.SavePath, calibrationData);
        }

        var output = new Dictionary<string, object>
        {
            ["CalibrationData"] = calibrationData,
            ["Accepted"] = accepted,
            ["TransformModel"] = transformModel.ToString(),
            ["CalibrationError"] = inlierStats.RmsError,
            ["MaxCalibrationError"] = inlierStats.MaxError,
            ["AllPointCalibrationError"] = allStats.RmsError,
            ["AllPointMaxCalibrationError"] = allStats.MaxError,
            ["RotationDeg"] = solvedRotationDeg,
            ["AngleConstraintApplied"] = angleResolution.Constraint.HasConstraint,
            ["RobustMode"] = configuration.RobustMode.ToString(),
            ["InlierCount"] = fit.InlierPositions.Count,
            ["OutlierCount"] = fit.OutlierPositions.Count,
            ["InlierIndices"] = inlierOriginalIndices,
            ["OutlierIndices"] = outlierOriginalIndices,
            ["Residuals"] = residualOutput,
            ["Diagnostics"] = diagnostics
        };

        if (TryGetInputImage(inputs, out var imageWrapper) && imageWrapper != null)
        {
            var src = imageWrapper.GetMat();
            if (!src.Empty())
            {
                var result = src.Clone();
                DrawPoints(result, points, fit.OutlierPositions);
                return Task.FromResult(OperatorExecutionOutput.Success(CreateImageOutput(result, output)));
            }
        }

        return Task.FromResult(OperatorExecutionOutput.Success(output));
    }

    public override ValidationResult ValidateParameters(Operator @operator)
    {
        if (!TryResolveConfiguration(@operator, out var configuration, out var configurationError))
        {
            return ValidationResult.Invalid(configurationError);
        }

        if (!TryParseCalibrationPoints(configuration.PointsJson, out var points, out var parseDiagnostics) || points.Count < 3)
        {
            return ValidationResult.Invalid("CalibrationPoints must contain at least 3 valid points.");
        }

        if (configuration.RobustMode != RobustFitMode.None && parseDiagnostics.Issues.Count > 0)
        {
            return ValidationResult.Invalid("Robust modes require every calibration sample to be well formed and finite.");
        }

        if (!TryValidatePointGeometry(points, out var geometryError))
        {
            return ValidationResult.Invalid(geometryError);
        }

        if (!TryResolveAngleConstraint(points, configuration.RobustMode, configuration.MinInlierRatio, out _, out var angleError))
        {
            return ValidationResult.Invalid(angleError);
        }

        return ValidationResult.Valid();
    }

    private bool TryResolveConfiguration(Operator @operator, out FitConfiguration configuration, out string error)
    {
        var method = GetStringParam(@operator, "Method", "LeastSquares");
        var robustModeText = GetStringParam(@operator, "RobustMode", "None");
        if (!Enum.TryParse<RobustFitMode>(robustModeText, ignoreCase: true, out var robustMode))
        {
            configuration = default!;
            error = "RobustMode must be None, Ransac or Huber.";
            return false;
        }

        SolveMethod solveMethod;
        if (method.Equals("LeastSquares", StringComparison.OrdinalIgnoreCase))
        {
            solveMethod = SolveMethod.SimilarityLeastSquares;
        }
        else if (method.Equals("SVD", StringComparison.OrdinalIgnoreCase))
        {
            solveMethod = SolveMethod.RigidSvd;
        }
        else
        {
            configuration = default!;
            error = "Method must be LeastSquares or SVD.";
            return false;
        }

        configuration = new FitConfiguration(
            GetStringParam(@operator, "CalibrationPoints", string.Empty),
            GetStringParam(@operator, "SavePath", string.Empty),
            solveMethod,
            robustMode,
            GetDoubleParam(@operator, "RobustResidualThreshold", 0.30, 1e-12, 1e12),
            GetIntParam(@operator, "RobustMaxIterations", 256, 1, 10_000),
            GetDoubleParam(@operator, "RobustMinInlierRatio", 0.5, 0.1, 1.0),
            GetDoubleParam(@operator, "HuberDelta", 0.15, 1e-12, 1e12));
        error = string.Empty;
        return true;
    }

    private static bool TryFit(
        IReadOnlyList<CalibrationPoint> points,
        FitConfiguration configuration,
        AngleResolution angleResolution,
        CancellationToken cancellationToken,
        out FitResult result,
        out string error)
    {
        return configuration.RobustMode switch
        {
            RobustFitMode.None => TryFitNone(points, configuration.SolveMethod, angleResolution.Constraint, out result, out error),
            RobustFitMode.Ransac => TryFitRansac(points, configuration, angleResolution, cancellationToken, out result, out error),
            RobustFitMode.Huber => TryFitHuber(points, configuration, angleResolution, cancellationToken, out result, out error),
            _ => FailFit(out result, out error, "Unsupported robust mode.")
        };
    }

    private static bool TryFitNone(
        IReadOnlyList<CalibrationPoint> points,
        SolveMethod method,
        AngleConstraint angleConstraint,
        out FitResult result,
        out string error)
    {
        if (!TrySolveTransform(points, method, angleConstraint, out var matrix, out var scale, out error))
        {
            result = default!;
            return false;
        }

        var residuals = ComputeResiduals(points, matrix);
        var inliers = Enumerable.Range(0, points.Count).ToArray();
        result = new FitResult(matrix, scale, residuals, inliers, Array.Empty<int>(), 1, true, "least_squares_complete");
        return true;
    }

    private static bool TryFitRansac(
        IReadOnlyList<CalibrationPoint> points,
        FitConfiguration configuration,
        AngleResolution angleResolution,
        CancellationToken cancellationToken,
        out FitResult result,
        out string error)
    {
        var eligible = angleResolution.EligiblePositions;
        var minimumInliers = Math.Max(3, (int)Math.Ceiling(eligible.Count * configuration.MinInlierRatio));
        if (eligible.Count < minimumInliers)
        {
            return FailFit(out result, out error, $"RANSAC has only {eligible.Count} angle-compatible points; requires {minimumInliers}.");
        }

        int[]? bestInliers = null;
        var bestSquaredError = double.PositiveInfinity;
        var iterations = 0;
        foreach (var sample in CreateRansacSamples(eligible, configuration.MaxIterations))
        {
            cancellationToken.ThrowIfCancellationRequested();
            iterations++;
            var samplePoints = sample.Select(position => points[position]).ToArray();
            if (!TryValidatePointGeometry(samplePoints, out _) ||
                !TrySolveTransform(samplePoints, configuration.SolveMethod, angleResolution.Constraint, out var candidate, out _, out _))
            {
                continue;
            }

            var residuals = ComputeResiduals(points, candidate);
            var inliers = eligible.Where(position => residuals[position] <= configuration.ResidualThreshold).ToArray();
            if (inliers.Length < minimumInliers)
            {
                continue;
            }

            var squaredError = inliers.Sum(position => residuals[position] * residuals[position]);
            if (bestInliers is null || inliers.Length > bestInliers.Length ||
                (inliers.Length == bestInliers.Length && squaredError < bestSquaredError))
            {
                bestInliers = inliers;
                bestSquaredError = squaredError;
            }
        }

        if (bestInliers is null)
        {
            return FailFit(out result, out error, $"RANSAC found no stable consensus with threshold {Format(configuration.ResidualThreshold)} and minimum {minimumInliers} inliers.");
        }

        if (!TryRefitConsensus(points, configuration, angleResolution.Constraint, angleResolution.EligiblePositions, bestInliers, minimumInliers, out var matrix, out var scale, out var finalInliers, out error))
        {
            result = default!;
            return false;
        }

        var finalResiduals = ComputeResiduals(points, matrix);
        var inlierSet = finalInliers.ToHashSet();
        var outliers = Enumerable.Range(0, points.Count).Where(position => !inlierSet.Contains(position)).ToArray();
        result = new FitResult(matrix, scale, finalResiduals, finalInliers, outliers, iterations, true, "best_consensus_refit");
        return true;
    }

    private static bool TryRefitConsensus(
        IReadOnlyList<CalibrationPoint> points,
        FitConfiguration configuration,
        AngleConstraint angleConstraint,
        IReadOnlyList<int> eligiblePositions,
        IReadOnlyList<int> initialInliers,
        int minimumInliers,
        out double[][] matrix,
        out double scale,
        out int[] finalInliers,
        out string error)
    {
        var inliers = initialInliers.ToArray();
        matrix = Array.Empty<double[]>();
        scale = 1.0;
        for (var pass = 0; pass < 2; pass++)
        {
            var inlierPoints = inliers.Select(position => points[position]).ToArray();
            if (!TryValidatePointGeometry(inlierPoints, out var geometryError))
            {
                finalInliers = Array.Empty<int>();
                error = $"RANSAC consensus refit is degenerate: {geometryError}";
                return false;
            }
            if (!TrySolveTransform(inlierPoints, configuration.SolveMethod, angleConstraint, out matrix, out scale, out var solveError))
            {
                finalInliers = Array.Empty<int>();
                error = $"RANSAC consensus refit failed: {solveError}";
                return false;
            }

            var residuals = ComputeResiduals(points, matrix);
            inliers = eligiblePositions
                .Where(position => residuals[position] <= configuration.ResidualThreshold)
                .ToArray();
            if (inliers.Length < minimumInliers)
            {
                finalInliers = Array.Empty<int>();
                error = $"RANSAC consensus collapsed to {inliers.Length} inliers; requires {minimumInliers}.";
                return false;
            }
        }

        finalInliers = inliers;
        error = string.Empty;
        return true;
    }

    private static bool TryFitHuber(
        IReadOnlyList<CalibrationPoint> points,
        FitConfiguration configuration,
        AngleResolution angleResolution,
        CancellationToken cancellationToken,
        out FitResult result,
        out string error)
    {
        var eligibleSet = angleResolution.EligiblePositions.ToHashSet();
        var minimumInliers = Math.Max(3, (int)Math.Ceiling(eligibleSet.Count * configuration.MinInlierRatio));
        var weights = Enumerable.Range(0, points.Count).Select(index => eligibleSet.Contains(index) ? 1.0 : 0.0).ToArray();
        double[][]? previousMatrix = null;
        double[][] matrix = Array.Empty<double[]>();
        var scale = 1.0;
        var converged = false;
        var iterations = 0;

        for (var iteration = 1; iteration <= configuration.MaxIterations; iteration++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            iterations = iteration;
            if (!TrySolveWeightedTransform(points, weights, configuration.SolveMethod, angleResolution.Constraint, out matrix, out scale, out error))
            {
                result = default!;
                return false;
            }

            var residuals = ComputeResiduals(points, matrix);
            var updated = new double[points.Count];
            var maxWeightChange = 0.0;
            for (var i = 0; i < points.Count; i++)
            {
                if (!eligibleSet.Contains(i))
                {
                    updated[i] = 0.0;
                    continue;
                }

                updated[i] = residuals[i] <= configuration.HuberDelta
                    ? 1.0
                    : configuration.HuberDelta / Math.Max(residuals[i], DegenerateThreshold);
                maxWeightChange = Math.Max(maxWeightChange, Math.Abs(updated[i] - weights[i]));
            }

            var matrixChange = previousMatrix is null ? double.PositiveInfinity : MatrixDistance(previousMatrix, matrix);
            weights = updated;
            previousMatrix = matrix.Select(row => row.ToArray()).ToArray();
            if (matrixChange <= 1e-10 && maxWeightChange <= 1e-6)
            {
                converged = true;
                break;
            }
        }

        if (!converged)
        {
            return FailFit(out result, out error, $"Huber IRLS did not converge within {configuration.MaxIterations} iterations.");
        }

        var finalResiduals = ComputeResiduals(points, matrix);
        var inliers = Enumerable.Range(0, points.Count)
            .Where(position => eligibleSet.Contains(position) && finalResiduals[position] <= configuration.ResidualThreshold)
            .ToArray();
        if (inliers.Length < minimumInliers)
        {
            return FailFit(out result, out error, $"Huber retained {inliers.Length} inliers; requires {minimumInliers}.");
        }

        var inlierSet = inliers.ToHashSet();
        var outliers = Enumerable.Range(0, points.Count).Where(position => !inlierSet.Contains(position)).ToArray();
        result = new FitResult(matrix, scale, finalResiduals, inliers, outliers, iterations, true, "huber_weights_converged");
        error = string.Empty;
        return true;
    }

    private static IEnumerable<int[]> CreateRansacSamples(IReadOnlyList<int> eligible, int maxIterations)
    {
        var combinationCount = eligible.Count < 3
            ? 0L
            : (long)eligible.Count * (eligible.Count - 1) * (eligible.Count - 2) / 6;
        if (combinationCount <= maxIterations)
        {
            for (var a = 0; a < eligible.Count - 2; a++)
            for (var b = a + 1; b < eligible.Count - 1; b++)
            for (var c = b + 1; c < eligible.Count; c++)
            {
                yield return new[] { eligible[a], eligible[b], eligible[c] };
            }
            yield break;
        }

        var random = new Random(20260715);
        var seen = new HashSet<(int, int, int)>();
        var attempts = 0;
        while (seen.Count < maxIterations && attempts < maxIterations * 20)
        {
            attempts++;
            var sample = new[]
            {
                eligible[random.Next(eligible.Count)],
                eligible[random.Next(eligible.Count)],
                eligible[random.Next(eligible.Count)]
            };
            Array.Sort(sample);
            if (sample[0] == sample[1] || sample[1] == sample[2] || !seen.Add((sample[0], sample[1], sample[2])))
            {
                continue;
            }
            yield return sample;
        }
    }

    private static bool TrySolveTransform(
        IReadOnlyList<CalibrationPoint> points,
        SolveMethod method,
        AngleConstraint angleConstraint,
        out double[][] matrix,
        out double solvedScale,
        out string error) =>
        TrySolveWeightedTransform(points, weights: null, method, angleConstraint, out matrix, out solvedScale, out error);

    private static bool TrySolveWeightedTransform(
        IReadOnlyList<CalibrationPoint> points,
        IReadOnlyList<double>? weights,
        SolveMethod method,
        AngleConstraint angleConstraint,
        out double[][] matrix,
        out double solvedScale,
        out string error)
    {
        matrix = Array.Empty<double[]>();
        solvedScale = 1.0;
        error = string.Empty;
        var totalWeight = 0.0;
        var srcCx = 0.0;
        var srcCy = 0.0;
        var dstCx = 0.0;
        var dstCy = 0.0;
        for (var i = 0; i < points.Count; i++)
        {
            var weight = weights?[i] ?? 1.0;
            if (!IsFinite(weight) || weight < 0) continue;
            totalWeight += weight;
            srcCx += weight * points[i].ImageX;
            srcCy += weight * points[i].ImageY;
            dstCx += weight * points[i].RobotX;
            dstCy += weight * points[i].RobotY;
        }

        if (totalWeight <= DegenerateThreshold)
        {
            error = "Effective weight is zero; robust fit is degenerate.";
            return false;
        }

        srcCx /= totalWeight;
        srcCy /= totalWeight;
        dstCx /= totalWeight;
        dstCy /= totalWeight;
        var h00 = 0.0;
        var h01 = 0.0;
        var h10 = 0.0;
        var h11 = 0.0;
        var srcVar = 0.0;
        for (var i = 0; i < points.Count; i++)
        {
            var weight = weights?[i] ?? 1.0;
            if (weight <= 0) continue;
            var sx = points[i].ImageX - srcCx;
            var sy = points[i].ImageY - srcCy;
            var dx = points[i].RobotX - dstCx;
            var dy = points[i].RobotY - dstCy;
            h00 += weight * sx * dx;
            h01 += weight * sx * dy;
            h10 += weight * sy * dx;
            h11 += weight * sy * dy;
            srcVar += weight * ((sx * sx) + (sy * sy));
        }

        if (srcVar <= DegenerateThreshold)
        {
            error = "Source baseline is too small for a stable transform.";
            return false;
        }

        double r00;
        double r01;
        double r10;
        double r11;
        if (angleConstraint.HasConstraint)
        {
            var angleRad = angleConstraint.RotationDeg * Math.PI / 180.0;
            r00 = Math.Cos(angleRad);
            r01 = -Math.Sin(angleRad);
            r10 = Math.Sin(angleRad);
            r11 = Math.Cos(angleRad);
        }
        else
        {
            using var h = new Mat(2, 2, MatType.CV_64FC1);
            h.Set(0, 0, h00);
            h.Set(0, 1, h01);
            h.Set(1, 0, h10);
            h.Set(1, 1, h11);
            using var w = new Mat();
            using var u = new Mat();
            using var vt = new Mat();
            Cv2.SVDecomp(h, w, u, vt);
            if (w.Empty() || w.Rows * w.Cols < 2)
            {
                error = "SVD decomposition failed.";
                return false;
            }

            var singular0 = w.At<double>(0, 0);
            var singular1 = w.At<double>(1, 0);
            if (!IsFinite(singular0) || singular0 <= DegenerateThreshold || singular1 / singular0 <= CollinearityRatioThreshold)
            {
                error = $"Point set is singular or near-collinear (singular ratio={Format(singular0 <= 0 ? 0 : singular1 / singular0)}).";
                return false;
            }

            using var v = new Mat();
            using var ut = new Mat();
            Cv2.Transpose(vt, v);
            Cv2.Transpose(u, ut);
            using var rotation = new Mat();
            using var empty = new Mat();
            Cv2.Gemm(v, ut, 1.0, empty, 0.0, rotation);
            if (Cv2.Determinant(rotation) < 0)
            {
                for (var row = 0; row < 2; row++) v.Set(row, 1, -v.At<double>(row, 1));
                Cv2.Gemm(v, ut, 1.0, empty, 0.0, rotation);
            }
            r00 = rotation.At<double>(0, 0);
            r01 = rotation.At<double>(0, 1);
            r10 = rotation.At<double>(1, 0);
            r11 = rotation.At<double>(1, 1);
        }

        if (method == SolveMethod.SimilarityLeastSquares)
        {
            var numerator = 0.0;
            for (var i = 0; i < points.Count; i++)
            {
                var weight = weights?[i] ?? 1.0;
                if (weight <= 0) continue;
                var sx = points[i].ImageX - srcCx;
                var sy = points[i].ImageY - srcCy;
                var dx = points[i].RobotX - dstCx;
                var dy = points[i].RobotY - dstCy;
                numerator += weight * (dx * ((r00 * sx) + (r01 * sy)) + dy * ((r10 * sx) + (r11 * sy)));
            }
            solvedScale = numerator / srcVar;
            if (!IsFinite(solvedScale) || solvedScale <= DegenerateThreshold || solvedScale > MaximumSupportedScale)
            {
                error = $"Solved scale {Format(solvedScale)} is outside the supported finite range ({DegenerateThreshold:G}..{MaximumSupportedScale:G}).";
                return false;
            }
        }

        var tx = dstCx - solvedScale * ((r00 * srcCx) + (r01 * srcCy));
        var ty = dstCy - solvedScale * ((r10 * srcCx) + (r11 * srcCy));
        if (!IsFinite(tx) || !IsFinite(ty))
        {
            error = "Solved translation is non-finite.";
            return false;
        }

        matrix = new[]
        {
            new[] { solvedScale * r00, solvedScale * r01, tx },
            new[] { solvedScale * r10, solvedScale * r11, ty }
        };
        return true;
    }

    private static bool TryParseCalibrationPoints(string json, out List<CalibrationPoint> points, out ParseDiagnostics diagnostics)
    {
        points = new List<CalibrationPoint>();
        var issues = new List<string>();
        var inputCount = 0;
        if (string.IsNullOrWhiteSpace(json))
        {
            diagnostics = new ParseDiagnostics(0, issues);
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                diagnostics = new ParseDiagnostics(0, ["root:not_array"]);
                return false;
            }

            foreach (var item in doc.RootElement.EnumerateArray())
            {
                var originalIndex = inputCount++;
                if (item.ValueKind != JsonValueKind.Object)
                {
                    issues.Add($"{originalIndex}:not_object");
                    continue;
                }

                if (!TryGetNumber(item, "imageX", out var imageX) ||
                    !TryGetNumber(item, "imageY", out var imageY) ||
                    !TryGetNumber(item, "robotX", out var robotX) ||
                    !TryGetNumber(item, "robotY", out var robotY) ||
                    !IsFinite(imageX) || !IsFinite(imageY) || !IsFinite(robotX) || !IsFinite(robotY))
                {
                    issues.Add($"{originalIndex}:required_coordinate_missing_or_nonfinite");
                    continue;
                }

                double? angle = null;
                if (TryFindProperty(item, "angle", out var angleElement))
                {
                    if (!TryConvertNumber(angleElement, out var parsedAngle) || !IsFinite(parsedAngle))
                    {
                        issues.Add($"{originalIndex}:angle_invalid");
                    }
                    else
                    {
                        angle = NormalizeAngleDegrees(parsedAngle);
                    }
                }

                points.Add(new CalibrationPoint(originalIndex, imageX, imageY, robotX, robotY, angle));
            }

            diagnostics = new ParseDiagnostics(inputCount, issues);
            return points.Count > 0;
        }
        catch (JsonException ex)
        {
            diagnostics = new ParseDiagnostics(inputCount, [$"json:{ex.Message}"]);
            return false;
        }
    }

    private static bool TryResolveAngleConstraint(
        IReadOnlyList<CalibrationPoint> points,
        RobustFitMode robustMode,
        double minInlierRatio,
        out AngleResolution resolution,
        out string error)
    {
        var withAngles = points.Select((point, position) => (point, position)).Where(item => item.point.AngleDeg.HasValue).ToArray();
        if (withAngles.Length == 0)
        {
            resolution = new AngleResolution(AngleConstraint.None, Enumerable.Range(0, points.Count).ToArray(), Array.Empty<int>());
            error = string.Empty;
            return true;
        }
        if (withAngles.Length != points.Count)
        {
            resolution = default!;
            error = "Angle must be supplied for all calibration points or omitted for all calibration points.";
            return false;
        }

        if (robustMode == RobustFitMode.None)
        {
            var angles = withAngles.Select(item => item.point.AngleDeg!.Value).ToArray();
            if (!TryCircularMean(angles, out var meanAngle))
            {
                resolution = default!;
                error = "Angle inputs are inconsistent and cannot define a stable global rotation.";
                return false;
            }
            var maxDeviation = angles.Max(angle => AngularDistanceDegrees(angle, meanAngle));
            if (maxDeviation > AngleToleranceDeg)
            {
                resolution = default!;
                error = $"Angle inputs are inconsistent for a single transform (max deviation {maxDeviation:F3} deg).";
                return false;
            }
            resolution = new AngleResolution(new AngleConstraint(true, meanAngle, maxDeviation), Enumerable.Range(0, points.Count).ToArray(), Array.Empty<int>());
            error = string.Empty;
            return true;
        }

        int[]? bestPositions = null;
        var bestDeviation = double.PositiveInfinity;
        foreach (var candidate in withAngles)
        {
            var compatible = withAngles
                .Where(item => AngularDistanceDegrees(item.point.AngleDeg!.Value, candidate.point.AngleDeg!.Value) <= AngleToleranceDeg)
                .Select(item => item.position)
                .ToArray();
            if (!TryCircularMean(compatible.Select(position => points[position].AngleDeg!.Value), out var mean)) continue;
            var deviation = compatible.Sum(position => AngularDistanceDegrees(points[position].AngleDeg!.Value, mean));
            if (bestPositions is null || compatible.Length > bestPositions.Length ||
                (compatible.Length == bestPositions.Length && deviation < bestDeviation))
            {
                bestPositions = compatible;
                bestDeviation = deviation;
            }
        }

        var minimum = Math.Max(3, (int)Math.Ceiling(points.Count * minInlierRatio));
        if (bestPositions is null || bestPositions.Length < minimum ||
            !TryCircularMean(bestPositions.Select(position => points[position].AngleDeg!.Value), out var robustMean))
        {
            resolution = default!;
            error = $"Angle consensus is degenerate; requires at least {minimum} mutually consistent samples.";
            return false;
        }

        var maxRobustDeviation = bestPositions.Max(position => AngularDistanceDegrees(points[position].AngleDeg!.Value, robustMean));
        var eligibleSet = bestPositions.ToHashSet();
        var angleOutliers = Enumerable.Range(0, points.Count)
            .Where(position => !eligibleSet.Contains(position))
            .Select(position => points[position].OriginalIndex)
            .OrderBy(index => index)
            .ToArray();
        resolution = new AngleResolution(new AngleConstraint(true, robustMean, maxRobustDeviation), bestPositions, angleOutliers);
        error = string.Empty;
        return true;
    }

    private static bool TryValidatePointGeometry(IReadOnlyList<CalibrationPoint> points, out string error)
    {
        if (points.Count < 3)
        {
            error = "At least 3 point pairs are required.";
            return false;
        }
        if (points.Select(point => (point.ImageX, point.ImageY)).Distinct().Count() < 2 ||
            points.Select(point => (point.RobotX, point.RobotY)).Distinct().Count() < 2)
        {
            error = "Point set is degenerate: at least two unique source and destination points are required.";
            return false;
        }

        var sourceBaseline = BoundingBoxDiagonal(points.Select(point => (point.ImageX, point.ImageY)));
        var destinationBaseline = BoundingBoxDiagonal(points.Select(point => (point.RobotX, point.RobotY)));
        var sourceMagnitude = points.Max(point => Math.Max(Math.Abs(point.ImageX), Math.Abs(point.ImageY)));
        var destinationMagnitude = points.Max(point => Math.Max(Math.Abs(point.RobotX), Math.Abs(point.RobotY)));
        var sourceMinimum = Math.Max(DegenerateThreshold, sourceMagnitude * 1e-14);
        var destinationMinimum = Math.Max(DegenerateThreshold, destinationMagnitude * 1e-14);
        if (!IsFinite(sourceBaseline) || sourceBaseline <= sourceMinimum)
        {
            error = $"Source baseline is too small ({Format(sourceBaseline)} <= {Format(sourceMinimum)}).";
            return false;
        }
        if (!IsFinite(destinationBaseline) || destinationBaseline <= destinationMinimum)
        {
            error = $"Destination baseline is too small ({Format(destinationBaseline)} <= {Format(destinationMinimum)}).";
            return false;
        }
        error = string.Empty;
        return true;
    }

    private static double[] ComputeResiduals(IReadOnlyList<CalibrationPoint> points, double[][] matrix)
    {
        var residuals = new double[points.Count];
        for (var i = 0; i < points.Count; i++)
        {
            var x = (matrix[0][0] * points[i].ImageX) + (matrix[0][1] * points[i].ImageY) + matrix[0][2];
            var y = (matrix[1][0] * points[i].ImageX) + (matrix[1][1] * points[i].ImageY) + matrix[1][2];
            var dx = x - points[i].RobotX;
            var dy = y - points[i].RobotY;
            residuals[i] = Math.Sqrt((dx * dx) + (dy * dy));
        }
        return residuals;
    }

    private static CalibrationErrorStats ComputeErrorStats(IReadOnlyList<double> residuals, IEnumerable<int> positions)
    {
        var selected = positions.Select(position => residuals[position]).ToArray();
        return new CalibrationErrorStats(
            Math.Sqrt(selected.Sum(value => value * value) / Math.Max(1, selected.Length)),
            selected.DefaultIfEmpty(0.0).Max());
    }

    private static bool TryGetNumber(JsonElement obj, string name, out double value)
    {
        value = 0;
        return TryFindProperty(obj, name, out var element) && TryConvertNumber(element, out value);
    }

    private static bool TryFindProperty(JsonElement obj, string name, out JsonElement value)
    {
        foreach (var property in obj.EnumerateObject())
        {
            if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }
        value = default;
        return false;
    }

    private static bool TryConvertNumber(JsonElement element, out double value)
    {
        if (element.ValueKind == JsonValueKind.Number) return element.TryGetDouble(out value);
        if (element.ValueKind == JsonValueKind.String)
            return double.TryParse(element.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out value);
        value = 0;
        return false;
    }

    private void TrySaveCalibrationBundle(string savePath, string calibrationData)
    {
        try
        {
            var dir = Path.GetDirectoryName(savePath);
            if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(savePath, calibrationData);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to save calibration bundle to {Path}", savePath);
        }
    }

    private static void DrawPoints(Mat image, IReadOnlyList<CalibrationPoint> points, IReadOnlyList<int> outlierPositions)
    {
        var outliers = outlierPositions.ToHashSet();
        for (var i = 0; i < points.Count; i++)
        {
            var x = (int)Math.Round(points[i].ImageX);
            var y = (int)Math.Round(points[i].ImageY);
            var color = outliers.Contains(i) ? new Scalar(0, 0, 255) : new Scalar(0, 255, 0);
            Cv2.Circle(image, new Point(x, y), 4, color, -1);
            Cv2.PutText(image, (points[i].OriginalIndex + 1).ToString(CultureInfo.InvariantCulture), new Point(x + 5, y - 5), HersheyFonts.HersheySimplex, 0.45, color, 1);
        }
    }

    private static bool TryCircularMean(IEnumerable<double> angles, out double mean)
    {
        var values = angles.ToArray();
        var sumCos = values.Sum(angle => Math.Cos(angle * Math.PI / 180.0));
        var sumSin = values.Sum(angle => Math.Sin(angle * Math.PI / 180.0));
        if (values.Length == 0 || (Math.Abs(sumCos) <= DegenerateThreshold && Math.Abs(sumSin) <= DegenerateThreshold))
        {
            mean = 0;
            return false;
        }
        mean = NormalizeAngleDegrees(Math.Atan2(sumSin, sumCos) * 180.0 / Math.PI);
        return true;
    }

    private static double BoundingBoxDiagonal(IEnumerable<(double X, double Y)> values)
    {
        var array = values.ToArray();
        var dx = array.Max(value => value.X) - array.Min(value => value.X);
        var dy = array.Max(value => value.Y) - array.Min(value => value.Y);
        return Math.Sqrt((dx * dx) + (dy * dy));
    }

    private static double MatrixDistance(double[][] first, double[][] second)
    {
        var maximum = 0.0;
        for (var row = 0; row < 2; row++)
        for (var column = 0; column < 3; column++)
        {
            var scale = Math.Max(1.0, Math.Max(Math.Abs(first[row][column]), Math.Abs(second[row][column])));
            maximum = Math.Max(maximum, Math.Abs(first[row][column] - second[row][column]) / scale);
        }
        return maximum;
    }

    private static bool FailFit(out FitResult result, out string error, string message)
    {
        result = default!;
        error = message;
        return false;
    }

    private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    private static string Format(double value) => value.ToString("G17", CultureInfo.InvariantCulture);

    private static double NormalizeAngleDegrees(double angle)
    {
        var normalized = angle % 360.0;
        if (normalized <= -180.0) normalized += 360.0;
        else if (normalized > 180.0) normalized -= 360.0;
        return normalized;
    }

    private static double AngularDistanceDegrees(double first, double second) => Math.Abs(NormalizeAngleDegrees(first - second));

    private enum SolveMethod { SimilarityLeastSquares, RigidSvd }
    private enum RobustFitMode { None, Ransac, Huber }

    private sealed record CalibrationPoint(int OriginalIndex, double ImageX, double ImageY, double RobotX, double RobotY, double? AngleDeg);
    private sealed record ParseDiagnostics(int InputCount, IReadOnlyList<string> Issues);
    private sealed record AngleConstraint(bool HasConstraint, double RotationDeg, double MaxDeviationDeg)
    {
        public static AngleConstraint None => new(false, 0.0, 0.0);
    }
    private sealed record AngleResolution(AngleConstraint Constraint, IReadOnlyList<int> EligiblePositions, IReadOnlyList<int> AngleOutlierOriginalIndices);
    private sealed record FitConfiguration(
        string PointsJson,
        string SavePath,
        SolveMethod SolveMethod,
        RobustFitMode RobustMode,
        double ResidualThreshold,
        int MaxIterations,
        double MinInlierRatio,
        double HuberDelta);
    private sealed record FitResult(
        double[][] Matrix,
        double Scale,
        IReadOnlyList<double> Residuals,
        IReadOnlyList<int> InlierPositions,
        IReadOnlyList<int> OutlierPositions,
        int Iterations,
        bool Converged,
        string TerminationReason);
    private sealed record CalibrationErrorStats(double RmsError, double MaxError);
}
