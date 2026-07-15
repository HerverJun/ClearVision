namespace ClearVision.Product.Infrastructure.Operators;

internal sealed record CaliperEdgeLocalizationResult(
    bool Success,
    double Position,
    double ResidualRmse,
    double SigmaSamples,
    bool Ambiguous,
    double PrimaryResponse,
    double SecondaryResponse,
    string FailureReason)
{
    public static CaliperEdgeLocalizationResult Failure(string reason) =>
        new(false, double.NaN, double.NaN, double.NaN, true, double.NaN, double.NaN, reason);
}

internal static class CaliperEdgeModelKernel
{
    public static CaliperEdgeLocalizationResult FitGaussianDerivative(
        IReadOnlyList<double> profile,
        double approximatePosition,
        string polarity,
        double sigma)
    {
        if (profile.Count < 7 || !double.IsFinite(approximatePosition) || !double.IsFinite(sigma) || sigma <= 0)
        {
            return CaliperEdgeLocalizationResult.Failure("GaussianDerivative requires a finite positive sigma and at least seven profile samples.");
        }

        var smoothed = Smooth(profile, sigma);
        var response = Derivative(smoothed);
        var sign = polarity.Equals("LightToDark", StringComparison.OrdinalIgnoreCase) ? -1.0 : 1.0;
        var center = Math.Clamp((int)Math.Round(approximatePosition), 2, response.Length - 3);
        var searchStart = Math.Max(2, center - 5);
        var searchEnd = Math.Min(response.Length - 3, center + 5);
        var peakIndex = -1;
        var peak = double.NegativeInfinity;
        for (var index = searchStart; index <= searchEnd; index++)
        {
            var value = response[index] * sign;
            if (value > peak)
            {
                peak = value;
                peakIndex = index;
            }
        }

        if (peakIndex < 0 || peak <= 1e-6)
        {
            return CaliperEdgeLocalizationResult.Failure("No signed Gaussian-derivative response was found near the detected edge.");
        }

        var secondary = double.NegativeInfinity;
        var ambiguityStart = Math.Max(2, center - 10);
        var ambiguityEnd = Math.Min(response.Length - 3, center + 10);
        for (var index = ambiguityStart; index <= ambiguityEnd; index++)
        {
            if (Math.Abs(index - peakIndex) <= 3)
            {
                continue;
            }

            secondary = Math.Max(secondary, response[index] * sign);
        }

        var left = response[peakIndex - 1] * sign;
        var right = response[peakIndex + 1] * sign;
        var denominator = left - (2.0 * peak) + right;
        var offset = Math.Abs(denominator) <= 1e-12 ? 0.0 : 0.5 * (left - right) / denominator;
        var position = peakIndex + Math.Clamp(offset, -1.0, 1.0);
        var residual = ComputeGaussianResponseResidual(response, peakIndex, position, sign, sigma, peak);
        var curvature = Math.Abs(denominator);
        var sigmaSamples = Math.Clamp((Math.Sqrt(Math.Max(residual, 1e-12)) + 0.01) / Math.Sqrt(Math.Max(curvature, 1e-6)), 0.02, 2.0);
        return new CaliperEdgeLocalizationResult(
            true,
            position,
            Math.Sqrt(Math.Max(residual, 0.0)),
            sigmaSamples,
            secondary >= peak * 0.82,
            peak,
            double.IsFinite(secondary) ? secondary : 0.0,
            string.Empty);
    }

    private static double ComputeGaussianResponseResidual(
        IReadOnlyList<double> response,
        int peakIndex,
        double position,
        double sign,
        double sigma,
        double amplitude)
    {
        double squared = 0;
        var count = 0;
        for (var index = Math.Max(1, peakIndex - 4); index <= Math.Min(response.Count - 2, peakIndex + 4); index++)
        {
            var expected = amplitude * Math.Exp(-Math.Pow(index - position, 2) / (2.0 * sigma * sigma));
            var observed = Math.Max(0.0, response[index] * sign);
            squared += Math.Pow(observed - expected, 2);
            count++;
        }

        return squared / Math.Max(count, 1);
    }

    private static double[] Smooth(IReadOnlyList<double> values, double sigma)
    {
        var radius = Math.Max(2, (int)Math.Ceiling(sigma * 3.0));
        var kernel = Enumerable.Range(-radius, (radius * 2) + 1)
            .Select(index => Math.Exp(-(index * index) / (2.0 * sigma * sigma)))
            .ToArray();
        var kernelSum = kernel.Sum();
        var smoothed = new double[values.Count];
        for (var index = 0; index < values.Count; index++)
        {
            double value = 0;
            for (var offset = -radius; offset <= radius; offset++)
            {
                value += values[Math.Clamp(index + offset, 0, values.Count - 1)] * kernel[offset + radius] / kernelSum;
            }

            smoothed[index] = value;
        }

        return smoothed;
    }

    private static double[] Derivative(IReadOnlyList<double> values)
    {
        var response = new double[values.Count];
        for (var index = 1; index < values.Count - 1; index++)
        {
            response[index] = (values[index + 1] - values[index - 1]) * 0.5;
        }

        return response;
    }
}
