using OpenCvSharp;

namespace ClearVision.Product.Infrastructure.Operators;

internal enum GeometryRefinementLoss
{
    L2 = 0,
    Huber = 1,
    Welsch = 2
}

internal sealed record CircleGeometryRefinementResult(
    bool Success,
    double CenterX,
    double CenterY,
    double Radius,
    bool Converged,
    bool Degenerate,
    int Iterations,
    double ResidualRmse,
    double ResidualMax,
    double RobustScale,
    IReadOnlyList<double> Weights,
    IReadOnlyList<double> Covariance,
    string FailureReason)
{
    public static CircleGeometryRefinementResult Failure(string reason, bool degenerate = false) =>
        new(false, double.NaN, double.NaN, double.NaN, false, degenerate, 0, double.NaN, double.NaN, double.NaN, Array.Empty<double>(), Array.Empty<double>(), reason);
}

internal sealed record LineGeometryRefinementResult(
    bool Success,
    double CenterX,
    double CenterY,
    double DirectionX,
    double DirectionY,
    double AngleDegrees,
    double Offset,
    bool Converged,
    bool Degenerate,
    int Iterations,
    double ResidualRmse,
    double ResidualMax,
    double RobustScale,
    double SigmaAngleDegrees,
    double SigmaOffset,
    IReadOnlyList<double> Weights,
    IReadOnlyList<double> Covariance,
    string FailureReason)
{
    public static LineGeometryRefinementResult Failure(string reason, bool degenerate = false) =>
        new(false, double.NaN, double.NaN, double.NaN, double.NaN, double.NaN, double.NaN, false, degenerate, 0, double.NaN, double.NaN, double.NaN, double.NaN, double.NaN, Array.Empty<double>(), Array.Empty<double>(), reason);
}

internal static class GeometryRefinementKernel
{
    private const double MinimumScale = 1e-6;

    public static CircleGeometryRefinementResult RefineCircle(
        IReadOnlyList<Point2d> points,
        double initialCenterX,
        double initialCenterY,
        double initialRadius,
        GeometryRefinementLoss loss,
        int maxIterations = 30,
        CancellationToken cancellationToken = default)
    {
        if (points.Count < 3 || !double.IsFinite(initialCenterX) || !double.IsFinite(initialCenterY) || !double.IsFinite(initialRadius) || initialRadius <= 0)
        {
            return CircleGeometryRefinementResult.Failure("Circle refinement requires at least three points and a finite positive seed.", true);
        }

        var centerX = initialCenterX;
        var centerY = initialCenterY;
        var radius = initialRadius;
        var weights = Enumerable.Repeat(1.0, points.Count).ToArray();
        var converged = loss == GeometryRefinementLoss.L2 && maxIterations == 0;
        var iterations = 0;

        for (var iteration = 0; iteration < Math.Max(1, maxIterations); iteration++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            iterations = iteration + 1;
            var residuals = CircleResiduals(points, centerX, centerY, radius);
            var scale = loss == GeometryRefinementLoss.L2 ? RootMeanSquare(residuals) : RobustScale(residuals);
            for (var index = 0; index < weights.Length; index++)
            {
                weights[index] = Weight(residuals[index], scale, loss);
            }

            var normal = new double[3, 3];
            var rhs = new double[3];
            for (var index = 0; index < points.Count; index++)
            {
                var dx = points[index].X - centerX;
                var dy = points[index].Y - centerY;
                var distance = Math.Sqrt((dx * dx) + (dy * dy));
                if (distance <= 1e-12)
                {
                    continue;
                }

                var jacobian = new[] { -dx / distance, -dy / distance, -1.0 };
                Accumulate(normal, rhs, jacobian, residuals[index], weights[index]);
            }

            if (!Solve(normal, rhs.Select(value => -value).ToArray(), out var delta))
            {
                return CircleGeometryRefinementResult.Failure("Circle normal equations are singular.", true);
            }

            centerX += delta[0];
            centerY += delta[1];
            radius += delta[2];
            if (!double.IsFinite(centerX) || !double.IsFinite(centerY) || !double.IsFinite(radius) || radius <= 0)
            {
                return CircleGeometryRefinementResult.Failure("Circle refinement diverged to a non-finite or non-positive radius.");
            }

            if (delta.Select(Math.Abs).Max() < 1e-8)
            {
                converged = true;
                break;
            }
        }

        var finalResiduals = CircleResiduals(points, centerX, centerY, radius);
        var finalScale = loss == GeometryRefinementLoss.L2 ? RootMeanSquare(finalResiduals) : RobustScale(finalResiduals);
        var covariance = CircleCovariance(points, centerX, centerY, weights, finalScale, out var degenerate);
        return new CircleGeometryRefinementResult(
            true,
            centerX,
            centerY,
            radius,
            converged,
            degenerate,
            iterations,
            RootMeanSquare(finalResiduals),
            finalResiduals.Select(Math.Abs).Max(),
            finalScale,
            weights,
            covariance,
            string.Empty);
    }

    public static LineGeometryRefinementResult RefineLine(
        IReadOnlyList<Point2d> points,
        GeometryRefinementLoss loss,
        int maxIterations = 30,
        CancellationToken cancellationToken = default)
    {
        if (points.Count < 2)
        {
            return LineGeometryRefinementResult.Failure("Line refinement requires at least two points.", true);
        }

        var weights = Enumerable.Repeat(1.0, points.Count).ToArray();
        var fit = FitWeightedLine(points, weights);
        if (!fit.Success)
        {
            return LineGeometryRefinementResult.Failure("Initial line fit is degenerate.", true);
        }

        var converged = loss == GeometryRefinementLoss.L2;
        var iterations = 0;
        for (var iteration = 0; loss != GeometryRefinementLoss.L2 && iteration < Math.Max(1, maxIterations); iteration++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            iterations = iteration + 1;
            var residuals = points.Select(point => SignedLineResidual(point, fit)).ToArray();
            var scale = RobustScale(residuals);
            for (var index = 0; index < weights.Length; index++)
            {
                weights[index] = Weight(residuals[index], scale, loss);
            }

            var next = FitWeightedLine(points, weights);
            if (!next.Success)
            {
                return LineGeometryRefinementResult.Failure("Robust line fit became degenerate.", true);
            }

            if (Math.Abs(SignedAngleDifference(next.AngleDegrees, fit.AngleDegrees)) < 1e-8 && Math.Abs(next.Offset - fit.Offset) < 1e-8)
            {
                converged = true;
                fit = next;
                break;
            }

            fit = next;
        }

        var finalResiduals = points.Select(point => SignedLineResidual(point, fit)).ToArray();
        var finalScale = loss == GeometryRefinementLoss.L2 ? RootMeanSquare(finalResiduals) : RobustScale(finalResiduals);
        var (sigmaAngle, sigmaOffset, covariance, degenerate) = LineCovariance(points, fit, weights, finalScale);
        return new LineGeometryRefinementResult(
            true,
            fit.CenterX,
            fit.CenterY,
            fit.DirectionX,
            fit.DirectionY,
            fit.AngleDegrees,
            fit.Offset,
            converged,
            degenerate,
            iterations,
            RootMeanSquare(finalResiduals),
            finalResiduals.Select(Math.Abs).Max(),
            finalScale,
            sigmaAngle,
            sigmaOffset,
            weights,
            covariance,
            string.Empty);
    }

    private static IReadOnlyList<double> CircleCovariance(
        IReadOnlyList<Point2d> points,
        double centerX,
        double centerY,
        IReadOnlyList<double> weights,
        double residualScale,
        out bool degenerate)
    {
        var normal = new double[3, 3];
        for (var index = 0; index < points.Count; index++)
        {
            var dx = points[index].X - centerX;
            var dy = points[index].Y - centerY;
            var distance = Math.Sqrt((dx * dx) + (dy * dy));
            if (distance <= 1e-12)
            {
                continue;
            }

            var jacobian = new[] { -dx / distance, -dy / distance, -1.0 };
            for (var row = 0; row < 3; row++)
            {
                for (var column = 0; column < 3; column++)
                {
                    normal[row, column] += weights[index] * jacobian[row] * jacobian[column];
                }
            }
        }

        if (!TryInvert(normal, out var inverse))
        {
            degenerate = true;
            return Array.Empty<double>();
        }

        degenerate = false;
        var scaleSquared = residualScale * residualScale;
        return Flatten(inverse, scaleSquared);
    }

    private static (double SigmaAngleDegrees, double SigmaOffset, IReadOnlyList<double> Covariance, bool Degenerate) LineCovariance(
        IReadOnlyList<Point2d> points,
        LineFit fit,
        IReadOnlyList<double> weights,
        double residualScale)
    {
        double projectedEnergy = 0;
        double weightSum = 0;
        for (var index = 0; index < points.Count; index++)
        {
            var dx = points[index].X - fit.CenterX;
            var dy = points[index].Y - fit.CenterY;
            var projected = (dx * fit.DirectionX) + (dy * fit.DirectionY);
            projectedEnergy += weights[index] * projected * projected;
            weightSum += weights[index];
        }

        if (projectedEnergy <= 1e-12 || weightSum <= 1e-12)
        {
            return (double.NaN, double.NaN, Array.Empty<double>(), true);
        }

        var varianceAngleRadians = (residualScale * residualScale) / projectedEnergy;
        var varianceOffset = (residualScale * residualScale) / weightSum;
        var sigmaAngleDegrees = Math.Sqrt(varianceAngleRadians) * 180.0 / Math.PI;
        var sigmaOffset = Math.Sqrt(varianceOffset);
        return (sigmaAngleDegrees, sigmaOffset, new[] { varianceAngleRadians, 0.0, 0.0, varianceOffset }, false);
    }

    private static LineFit FitWeightedLine(IReadOnlyList<Point2d> points, IReadOnlyList<double> weights)
    {
        var weightSum = weights.Sum();
        if (weightSum <= 1e-12)
        {
            return LineFit.Failure;
        }

        var meanX = points.Select((point, index) => point.X * weights[index]).Sum() / weightSum;
        var meanY = points.Select((point, index) => point.Y * weights[index]).Sum() / weightSum;
        double sxx = 0;
        double syy = 0;
        double sxy = 0;
        for (var index = 0; index < points.Count; index++)
        {
            var dx = points[index].X - meanX;
            var dy = points[index].Y - meanY;
            sxx += weights[index] * dx * dx;
            syy += weights[index] * dy * dy;
            sxy += weights[index] * dx * dy;
        }

        if (sxx + syy <= 1e-12)
        {
            return LineFit.Failure;
        }

        var angle = 0.5 * Math.Atan2(2 * sxy, sxx - syy);
        var directionX = Math.Cos(angle);
        var directionY = Math.Sin(angle);
        var normalX = -directionY;
        var normalY = directionX;
        return new LineFit(true, meanX, meanY, directionX, directionY, normalX, normalY, (normalX * meanX) + (normalY * meanY), angle * 180.0 / Math.PI);
    }

    private static double[] CircleResiduals(IReadOnlyList<Point2d> points, double centerX, double centerY, double radius) =>
        points.Select(point => Math.Sqrt(Math.Pow(point.X - centerX, 2) + Math.Pow(point.Y - centerY, 2)) - radius).ToArray();

    private static double SignedLineResidual(Point2d point, LineFit fit) => (fit.NormalX * point.X) + (fit.NormalY * point.Y) - fit.Offset;

    private static double Weight(double residual, double scale, GeometryRefinementLoss loss)
    {
        if (loss == GeometryRefinementLoss.L2)
        {
            return 1.0;
        }

        var normalized = Math.Abs(residual) / Math.Max(scale, MinimumScale);
        return loss switch
        {
            GeometryRefinementLoss.Huber => normalized <= 1.345 ? 1.0 : 1.345 / normalized,
            GeometryRefinementLoss.Welsch => Math.Exp(-Math.Pow(normalized / 2.9846, 2)),
            _ => 1.0
        };
    }

    private static double RobustScale(IReadOnlyList<double> residuals)
    {
        var median = Median(residuals);
        return Math.Max(Median(residuals.Select(value => Math.Abs(value - median)).ToArray()) * 1.4826, MinimumScale);
    }

    private static double RootMeanSquare(IReadOnlyList<double> values) => Math.Sqrt(values.Average(value => value * value));

    private static double Median(IReadOnlyList<double> values)
    {
        var sorted = values.OrderBy(value => value).ToArray();
        return sorted.Length % 2 == 1 ? sorted[sorted.Length / 2] : (sorted[(sorted.Length / 2) - 1] + sorted[sorted.Length / 2]) / 2.0;
    }

    private static void Accumulate(double[,] normal, double[] rhs, IReadOnlyList<double> jacobian, double residual, double weight)
    {
        for (var row = 0; row < jacobian.Count; row++)
        {
            rhs[row] += weight * jacobian[row] * residual;
            for (var column = 0; column < jacobian.Count; column++)
            {
                normal[row, column] += weight * jacobian[row] * jacobian[column];
            }
        }
    }

    private static IReadOnlyList<double> Flatten(double[,] matrix, double multiplier)
    {
        var rows = matrix.GetLength(0);
        var columns = matrix.GetLength(1);
        var values = new double[rows * columns];
        for (var row = 0; row < rows; row++)
        {
            for (var column = 0; column < columns; column++)
            {
                values[(row * columns) + column] = matrix[row, column] * multiplier;
            }
        }

        return values;
    }

    private static bool Solve(double[,] matrix, double[] rhs, out double[] solution)
    {
        var size = rhs.Length;
        var augmented = new double[size, size + 1];
        for (var row = 0; row < size; row++)
        {
            for (var column = 0; column < size; column++) augmented[row, column] = matrix[row, column];
            augmented[row, size] = rhs[row];
        }

        for (var pivot = 0; pivot < size; pivot++)
        {
            var best = pivot;
            for (var row = pivot + 1; row < size; row++) if (Math.Abs(augmented[row, pivot]) > Math.Abs(augmented[best, pivot])) best = row;
            if (Math.Abs(augmented[best, pivot]) <= 1e-12)
            {
                solution = Array.Empty<double>();
                return false;
            }

            if (best != pivot)
            {
                for (var column = pivot; column <= size; column++) (augmented[pivot, column], augmented[best, column]) = (augmented[best, column], augmented[pivot, column]);
            }

            var divisor = augmented[pivot, pivot];
            for (var column = pivot; column <= size; column++) augmented[pivot, column] /= divisor;
            for (var row = 0; row < size; row++)
            {
                if (row == pivot) continue;
                var factor = augmented[row, pivot];
                for (var column = pivot; column <= size; column++) augmented[row, column] -= factor * augmented[pivot, column];
            }
        }

        solution = Enumerable.Range(0, size).Select(row => augmented[row, size]).ToArray();
        return solution.All(double.IsFinite);
    }

    private static bool TryInvert(double[,] matrix, out double[,] inverse)
    {
        var size = matrix.GetLength(0);
        inverse = new double[size, size];
        for (var column = 0; column < size; column++)
        {
            var rhs = new double[size];
            rhs[column] = 1;
            if (!Solve(matrix, rhs, out var solution)) return false;
            for (var row = 0; row < size; row++) inverse[row, column] = solution[row];
        }

        return true;
    }

    private static double SignedAngleDifference(double first, double second)
    {
        var difference = first - second;
        while (difference > 90) difference -= 180;
        while (difference < -90) difference += 180;
        return difference;
    }

    private readonly record struct LineFit(bool Success, double CenterX, double CenterY, double DirectionX, double DirectionY, double NormalX, double NormalY, double Offset, double AngleDegrees)
    {
        public static LineFit Failure => new(false, double.NaN, double.NaN, double.NaN, double.NaN, double.NaN, double.NaN, double.NaN, double.NaN);
    }
}
