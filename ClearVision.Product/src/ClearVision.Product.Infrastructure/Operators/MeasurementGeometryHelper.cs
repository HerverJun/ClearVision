using System.Collections;
using ClearVision.Product.Core.ValueObjects;
using OpenCvSharp;

namespace ClearVision.Product.Infrastructure.Operators;

internal static class MeasurementGeometryHelper
{
    public static readonly Position NoIntersection = new(double.NaN, double.NaN);

    public static double EstimatePointSigma(Position point)
    {
        return HasFractionalComponent(point.X) || HasFractionalComponent(point.Y) ? 0.05 : 0.5;
    }

    public static double EstimateAnglePointSigma(object? source, Position point)
    {
        return source switch
        {
            Position => 0.05,
            Point2d => 0.05,
            Point2f => 0.08,
            Point => 0.5,
            _ => EstimatePointSigma(point)
        };
    }

    public static double EstimateLineSigma(LineData line)
    {
        return HasFractionalComponent(line.StartX) ||
               HasFractionalComponent(line.StartY) ||
               HasFractionalComponent(line.EndX) ||
               HasFractionalComponent(line.EndY)
            ? 0.05
            : 0.5;
    }

    public static double EstimateCircleSigma(double centerX, double centerY, double radius)
    {
        return HasFractionalComponent(centerX) ||
               HasFractionalComponent(centerY) ||
               HasFractionalComponent(radius)
            ? 0.05
            : 0.5;
    }

    public static bool IsFinite(LineData line)
    {
        return double.IsFinite(line.StartX) &&
               double.IsFinite(line.StartY) &&
               double.IsFinite(line.EndX) &&
               double.IsFinite(line.EndY);
    }

    public static double NormalizeLineDirectionDegrees(double angleDegrees)
    {
        var normalized = angleDegrees % 180.0;
        if (normalized >= 90.0)
        {
            normalized -= 180.0;
        }
        else if (normalized < -90.0)
        {
            normalized += 180.0;
        }

        return normalized;
    }

    public static double AngleBetweenLineDirections(LineData first, LineData second)
    {
        var v1x = first.EndX - first.StartX;
        var v1y = first.EndY - first.StartY;
        var v2x = second.EndX - second.StartX;
        var v2y = second.EndY - second.StartY;

        var norm1 = Math.Sqrt((v1x * v1x) + (v1y * v1y));
        var norm2 = Math.Sqrt((v2x * v2x) + (v2y * v2y));
        if (norm1 < 1e-9 || norm2 < 1e-9)
        {
            return 0.0;
        }

        var cosTheta = Math.Clamp(Math.Abs((v1x * v2x) + (v1y * v2y)) / (norm1 * norm2), -1.0, 1.0);
        return Math.Acos(cosTheta) * 180.0 / Math.PI;
    }

    public static double ThreePointAngleRadians(Position first, Position vertex, Position third)
    {
        var v1x = first.X - vertex.X;
        var v1y = first.Y - vertex.Y;
        var v2x = third.X - vertex.X;
        var v2y = third.Y - vertex.Y;
        var norm1 = Math.Sqrt((v1x * v1x) + (v1y * v1y));
        var norm2 = Math.Sqrt((v2x * v2x) + (v2y * v2y));
        if (norm1 < 1e-9 || norm2 < 1e-9)
        {
            return double.NaN;
        }

        var cosine = Math.Clamp(((v1x * v2x) + (v1y * v2y)) / (norm1 * norm2), -1.0, 1.0);
        return Math.Acos(cosine);
    }

    public static double PropagateThreePointAngleUncertaintyDegrees(
        Position first,
        double firstSigmaPx,
        Position vertex,
        double vertexSigmaPx,
        Position third,
        double thirdSigmaPx)
    {
        var firstArmLength = Distance(first, vertex);
        var secondArmLength = Distance(third, vertex);
        if (firstArmLength < 1e-9 || secondArmLength < 1e-9)
        {
            return double.NaN;
        }

        var firstArmSigma = Math.Sqrt(
            (firstSigmaPx * firstSigmaPx) + (vertexSigmaPx * vertexSigmaPx));
        var secondArmSigma = Math.Sqrt(
            (thirdSigmaPx * thirdSigmaPx) + (vertexSigmaPx * vertexSigmaPx));
        var uncertaintyRadians = Math.Sqrt(
            Math.Pow(firstArmSigma / firstArmLength, 2) +
            Math.Pow(secondArmSigma / secondArmLength, 2));
        return uncertaintyRadians * 180.0 / Math.PI;
    }

    public static bool TryParsePoint(object? value, out Position point)
    {
        point = new Position(0, 0);
        switch (value)
        {
            case Position position:
                point = position;
                return double.IsFinite(point.X) && double.IsFinite(point.Y);
            case Point integerPoint:
                point = new Position(integerPoint.X, integerPoint.Y);
                return true;
            case Point2f singlePoint:
                point = new Position(singlePoint.X, singlePoint.Y);
                return double.IsFinite(point.X) && double.IsFinite(point.Y);
            case Point2d doublePoint:
                point = new Position(doublePoint.X, doublePoint.Y);
                return double.IsFinite(point.X) && double.IsFinite(point.Y);
            case IDictionary<string, object> dictionary:
                return TryParsePointDictionary(dictionary, out point);
            case IDictionary legacyDictionary:
                var normalized = legacyDictionary.Cast<DictionaryEntry>()
                    .Where(entry => entry.Key != null)
                    .ToDictionary(
                        entry => entry.Key!.ToString() ?? string.Empty,
                        entry => entry.Value ?? 0.0,
                        StringComparer.OrdinalIgnoreCase);
                return TryParsePointDictionary(normalized, out point);
        }

        var text = value?.ToString()?.Trim('(', ')', '[', ']', ' ');
        var parts = text?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts is not { Length: 2 } ||
            !double.TryParse(parts[0], out var x) ||
            !double.TryParse(parts[1], out var y) ||
            !double.IsFinite(x) ||
            !double.IsFinite(y))
        {
            return false;
        }

        point = new Position(x, y);
        return true;
    }

    public static bool TryParseLine(object? value, out LineData line)
    {
        line = new LineData();
        if (value is LineData lineData)
        {
            line = lineData;
            return IsFinite(line);
        }

        if (value is IDictionary<string, object> dictionary)
        {
            return TryParseLineDictionary(dictionary, out line);
        }

        if (value is IDictionary legacyDictionary)
        {
            var normalized = legacyDictionary.Cast<DictionaryEntry>()
                .Where(entry => entry.Key != null)
                .ToDictionary(
                    entry => entry.Key!.ToString() ?? string.Empty,
                    entry => entry.Value ?? 0.0,
                    StringComparer.OrdinalIgnoreCase);
            return TryParseLineDictionary(normalized, out line);
        }

        return false;
    }

    public static double Distance(Position first, Position second)
    {
        return Distance(first.X, first.Y, second.X, second.Y);
    }

    public static double Distance(double ax, double ay, double bx, double by)
    {
        var dx = bx - ax;
        var dy = by - ay;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }

    public static double DistancePointToInfiniteLine(double px, double py, LineData line)
    {
        var a = line.EndY - line.StartY;
        var b = line.StartX - line.EndX;
        var c = (line.EndX * line.StartY) - (line.StartX * line.EndY);
        var denominator = Math.Sqrt((a * a) + (b * b));
        if (denominator < 1e-9)
        {
            return 0.0;
        }

        return Math.Abs((a * px) + (b * py) + c) / denominator;
    }

    public static Position ProjectPointToInfiniteLine(double px, double py, LineData line)
    {
        var dx = line.EndX - line.StartX;
        var dy = line.EndY - line.StartY;
        var norm2 = (dx * dx) + (dy * dy);
        if (norm2 < 1e-9)
        {
            return new Position(line.StartX, line.StartY);
        }

        var t = (((px - line.StartX) * dx) + ((py - line.StartY) * dy)) / norm2;
        return new Position(line.StartX + (t * dx), line.StartY + (t * dy));
    }

    public static Position ProjectPointToSegment(double px, double py, LineData line)
    {
        var dx = line.EndX - line.StartX;
        var dy = line.EndY - line.StartY;
        var norm2 = (dx * dx) + (dy * dy);
        if (norm2 < 1e-9)
        {
            return new Position(line.StartX, line.StartY);
        }

        var t = (((px - line.StartX) * dx) + ((py - line.StartY) * dy)) / norm2;
        t = Math.Clamp(t, 0.0, 1.0);
        return new Position(line.StartX + (t * dx), line.StartY + (t * dy));
    }

    public static double DistancePointToSegment(double px, double py, LineData line)
    {
        var foot = ProjectPointToSegment(px, py, line);
        return Distance(px, py, foot.X, foot.Y);
    }

    public static bool TryGetInfiniteLineIntersection(LineData first, LineData second, out Position intersection)
    {
        var denominator = ((first.StartX - first.EndX) * (second.StartY - second.EndY)) -
                          ((first.StartY - first.EndY) * (second.StartX - second.EndX));
        if (Math.Abs(denominator) < 1e-9)
        {
            intersection = NoIntersection;
            return false;
        }

        var determinant1 = (first.StartX * first.EndY) - (first.StartY * first.EndX);
        var determinant2 = (second.StartX * second.EndY) - (second.StartY * second.EndX);

        var px = ((determinant1 * (second.StartX - second.EndX)) - ((first.StartX - first.EndX) * determinant2)) / denominator;
        var py = ((determinant1 * (second.StartY - second.EndY)) - ((first.StartY - first.EndY) * determinant2)) / denominator;
        intersection = new Position(px, py);
        return true;
    }

    public static bool TryGetSegmentIntersection(LineData first, LineData second, out Position intersection)
    {
        if (!TryGetInfiniteLineIntersection(first, second, out var cross))
        {
            intersection = NoIntersection;
            return false;
        }

        if (IsPointOnSegment(cross, first) && IsPointOnSegment(cross, second))
        {
            intersection = cross;
            return true;
        }

        intersection = NoIntersection;
        return false;
    }

    public static bool IsPointOnSegment(Position point, LineData line)
    {
        var minX = Math.Min(line.StartX, line.EndX) - 1e-6;
        var maxX = Math.Max(line.StartX, line.EndX) + 1e-6;
        var minY = Math.Min(line.StartY, line.EndY) - 1e-6;
        var maxY = Math.Max(line.StartY, line.EndY) + 1e-6;
        return point.X >= minX && point.X <= maxX && point.Y >= minY && point.Y <= maxY;
    }

    public static double DistanceSegmentToSegment(LineData first, LineData second)
    {
        if (TryGetSegmentIntersection(first, second, out _))
        {
            return 0.0;
        }

        var d1 = DistancePointToSegment(first.StartX, first.StartY, second);
        var d2 = DistancePointToSegment(first.EndX, first.EndY, second);
        var d3 = DistancePointToSegment(second.StartX, second.StartY, first);
        var d4 = DistancePointToSegment(second.EndX, second.EndY, first);
        return Math.Min(Math.Min(d1, d2), Math.Min(d3, d4));
    }

    public static double PropagatePointLineDistanceUncertainty(
        Position point,
        double pointSigmaPx,
        LineData line,
        double lineSigmaPx,
        bool segmentModel)
    {
        var variables = new[] { point.X, point.Y, (double)line.StartX, line.StartY, line.EndX, line.EndY };
        var sigmas = new[] { pointSigmaPx, pointSigmaPx, lineSigmaPx, lineSigmaPx, lineSigmaPx, lineSigmaPx };

        return PropagateCoordinateUncertainty(
            variables,
            sigmas,
            values =>
            {
                var candidateLine = new LineData(
                    (float)values[2],
                    (float)values[3],
                    (float)values[4],
                    (float)values[5]);

                return segmentModel
                    ? DistancePointToSegment(values[0], values[1], candidateLine)
                    : DistancePointToInfiniteLine(values[0], values[1], candidateLine);
            });
    }

    public static double PropagateLineLineDistanceUncertainty(
        LineData first,
        double firstSigmaPx,
        LineData second,
        double secondSigmaPx,
        bool segmentModel,
        double parallelThresholdDeg)
    {
        var variables = new[]
        {
            (double)first.StartX, first.StartY, first.EndX, first.EndY,
            (double)second.StartX, second.StartY, second.EndX, second.EndY
        };
        var sigmas = new[]
        {
            firstSigmaPx, firstSigmaPx, firstSigmaPx, firstSigmaPx,
            secondSigmaPx, secondSigmaPx, secondSigmaPx, secondSigmaPx
        };

        return PropagateCoordinateUncertainty(
            variables,
            sigmas,
            values =>
            {
                var candidateFirst = new LineData(
                    (float)values[0],
                    (float)values[1],
                    (float)values[2],
                    (float)values[3]);
                var candidateSecond = new LineData(
                    (float)values[4],
                    (float)values[5],
                    (float)values[6],
                    (float)values[7]);

                if (segmentModel)
                {
                    return DistanceSegmentToSegment(candidateFirst, candidateSecond);
                }

                var angleDeg = AngleBetweenLineDirections(candidateFirst, candidateSecond);
                return angleDeg <= parallelThresholdDeg
                    ? DistancePointToInfiniteLine(candidateFirst.StartX, candidateFirst.StartY, candidateSecond)
                    : 0.0;
            });
    }

    public static double PropagatePointPointDistanceUncertainty(
        Position first,
        double firstSigmaPx,
        Position second,
        double secondSigmaPx)
    {
        var variables = new[] { first.X, first.Y, second.X, second.Y };
        var sigmas = new[] { firstSigmaPx, firstSigmaPx, secondSigmaPx, secondSigmaPx };

        return PropagateCoordinateUncertainty(
            variables,
            sigmas,
            values => Distance(values[0], values[1], values[2], values[3]));
    }

    public static double PropagatePointCircleGapUncertainty(
        Position point,
        double pointSigmaPx,
        Position center,
        double centerSigmaPx,
        double radius,
        double radiusSigmaPx)
    {
        var variables = new[] { point.X, point.Y, center.X, center.Y, radius };
        var sigmas = new[] { pointSigmaPx, pointSigmaPx, centerSigmaPx, centerSigmaPx, radiusSigmaPx };

        return PropagateCoordinateUncertainty(
            variables,
            sigmas,
            values => Math.Abs(Distance(values[0], values[1], values[2], values[3]) - values[4]));
    }

    public static double PropagateLineCircleGapUncertainty(
        LineData line,
        double lineSigmaPx,
        Position center,
        double centerSigmaPx,
        double radius,
        double radiusSigmaPx,
        bool segmentModel)
    {
        var variables = new[]
        {
            (double)line.StartX, line.StartY, line.EndX, line.EndY,
            center.X, center.Y, radius
        };
        var sigmas = new[]
        {
            lineSigmaPx, lineSigmaPx, lineSigmaPx, lineSigmaPx,
            centerSigmaPx, centerSigmaPx, radiusSigmaPx
        };

        return PropagateCoordinateUncertainty(
            variables,
            sigmas,
            values =>
            {
                var candidateLine = new LineData(
                    (float)values[0],
                    (float)values[1],
                    (float)values[2],
                    (float)values[3]);
                var centerDistance = segmentModel
                    ? DistancePointToSegment(values[4], values[5], candidateLine)
                    : DistancePointToInfiniteLine(values[4], values[5], candidateLine);

                return Math.Max(0.0, centerDistance - values[6]);
            });
    }

    public static double PropagateCircleCircleGapUncertainty(
        Position firstCenter,
        double firstCenterSigmaPx,
        double firstRadius,
        double firstRadiusSigmaPx,
        Position secondCenter,
        double secondCenterSigmaPx,
        double secondRadius,
        double secondRadiusSigmaPx)
    {
        var variables = new[]
        {
            firstCenter.X, firstCenter.Y, firstRadius,
            secondCenter.X, secondCenter.Y, secondRadius
        };
        var sigmas = new[]
        {
            firstCenterSigmaPx, firstCenterSigmaPx, firstRadiusSigmaPx,
            secondCenterSigmaPx, secondCenterSigmaPx, secondRadiusSigmaPx
        };

        return PropagateCoordinateUncertainty(
            variables,
            sigmas,
            values =>
            {
                var centerDistance = Distance(values[0], values[1], values[3], values[4]);
                var radiusSum = values[2] + values[5];
                var radiusDelta = Math.Abs(values[2] - values[5]);

                if (centerDistance > radiusSum)
                {
                    return centerDistance - radiusSum;
                }

                if (centerDistance < radiusDelta)
                {
                    return radiusDelta - centerDistance;
                }

                return 0.0;
            });
    }

    public static double PropagateCustomCoordinateUncertainty(
        IReadOnlyList<double> variables,
        IReadOnlyList<double> sigmas,
        Func<double[], double> evaluator)
    {
        return PropagateCoordinateUncertainty(variables, sigmas, evaluator);
    }

    private static double PropagateCoordinateUncertainty(
        IReadOnlyList<double> variables,
        IReadOnlyList<double> sigmas,
        Func<double[], double> evaluator)
    {
        if (variables.Count != sigmas.Count)
        {
            return double.NaN;
        }

        var baseVector = variables.ToArray();
        var baseValue = evaluator(baseVector);
        if (!double.IsFinite(baseValue))
        {
            return double.NaN;
        }

        var variance = 0.0;
        for (var i = 0; i < baseVector.Length; i++)
        {
            var sigma = sigmas[i];
            if (!double.IsFinite(sigma) || sigma <= 0.0)
            {
                continue;
            }

            var step = DetermineFiniteDifferenceStep(baseVector[i]);
            var plus = (double[])baseVector.Clone();
            var minus = (double[])baseVector.Clone();
            plus[i] += step;
            minus[i] -= step;

            var fPlus = evaluator(plus);
            var fMinus = evaluator(minus);
            if (!double.IsFinite(fPlus) || !double.IsFinite(fMinus))
            {
                continue;
            }

            var derivative = (fPlus - fMinus) / (2.0 * step);
            variance += derivative * derivative * sigma * sigma;
        }

        return Math.Sqrt(Math.Max(variance, 0.0));
    }

    private static double DetermineFiniteDifferenceStep(double value)
    {
        return Math.Max(1e-4, Math.Abs(value) * 1e-4);
    }

    private static bool HasFractionalComponent(double value)
    {
        return Math.Abs(value - Math.Round(value)) > 1e-6;
    }

    private static bool TryParsePointDictionary(IDictionary<string, object> dictionary, out Position point)
    {
        point = new Position(0, 0);
        if (!TryReadDouble(dictionary, "X", out var x) ||
            !TryReadDouble(dictionary, "Y", out var y) ||
            !double.IsFinite(x) ||
            !double.IsFinite(y))
        {
            return false;
        }

        point = new Position(x, y);
        return true;
    }

    private static bool TryParseLineDictionary(IDictionary<string, object> dictionary, out LineData line)
    {
        line = new LineData();
        var startX = 0.0;
        var startY = 0.0;
        var endX = 0.0;
        var endY = 0.0;
        var hasNamedCoordinates =
            TryReadDouble(dictionary, "StartX", out startX) &&
            TryReadDouble(dictionary, "StartY", out startY) &&
            TryReadDouble(dictionary, "EndX", out endX) &&
            TryReadDouble(dictionary, "EndY", out endY);

        if (!hasNamedCoordinates)
        {
            hasNamedCoordinates =
                TryReadDouble(dictionary, "X1", out startX) &&
                TryReadDouble(dictionary, "Y1", out startY) &&
                TryReadDouble(dictionary, "X2", out endX) &&
                TryReadDouble(dictionary, "Y2", out endY);
        }

        if (!hasNamedCoordinates ||
            !double.IsFinite(startX) ||
            !double.IsFinite(startY) ||
            !double.IsFinite(endX) ||
            !double.IsFinite(endY))
        {
            return false;
        }

        line = new LineData((float)startX, (float)startY, (float)endX, (float)endY);
        return IsFinite(line);
    }

    private static bool TryReadDouble(IDictionary<string, object> dictionary, string key, out double value)
    {
        value = 0.0;
        if (!dictionary.TryGetValue(key, out var raw) || raw == null)
        {
            return false;
        }

        return raw switch
        {
            double d => (value = d) == d,
            float f => (value = f) == f,
            int i => (value = i) == i,
            long l => (value = l) == l,
            decimal m => (value = (double)m) == (double)m,
            _ => double.TryParse(raw.ToString(), out value)
        };
    }
}
