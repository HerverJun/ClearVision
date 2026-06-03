using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using OpenCvSharp;

namespace ClearVision.Product.Infrastructure.Calibration;

public sealed class IntrinsicsCalibrationRuntime : IDisposable
{
    public IntrinsicsCalibrationRuntime(
        CalibrationBundleV2 bundle,
        Mat cameraMatrix,
        Mat distCoeffs,
        Size calibrationImageSize,
        string fingerprint,
        IntrinsicsRuntimeQualityAssessment? runtimeQualityAssessment = null)
    {
        Bundle = bundle;
        CameraMatrix = cameraMatrix;
        DistCoeffs = distCoeffs;
        CalibrationImageSize = calibrationImageSize;
        Fingerprint = fingerprint;
        RuntimeQualityAssessment = runtimeQualityAssessment ?? new IntrinsicsRuntimeQualityAssessment();
    }

    public CalibrationBundleV2 Bundle { get; }

    public Mat CameraMatrix { get; }

    public Mat DistCoeffs { get; }

    public Size CalibrationImageSize { get; }

    public string Fingerprint { get; }

    public IntrinsicsRuntimeQualityAssessment RuntimeQualityAssessment { get; }

    public void Dispose()
    {
        CameraMatrix.Dispose();
        DistCoeffs.Dispose();
    }
}

public sealed class IntrinsicsRuntimeQualityAssessment
{
    public bool GatePassed { get; set; }

    public string Status { get; set; } = "unknown";

    public string DriftRisk { get; set; } = "unknown";

    public double BaselineMeanError { get; set; }

    public double BaselineMaxError { get; set; }

    public double MeanWarningThreshold { get; set; }

    public double MeanFailureThreshold { get; set; }

    public double MaxWarningThreshold { get; set; }

    public double MaxFailureThreshold { get; set; }

    public double MeanThresholdUsage { get; set; }

    public double MaxThresholdUsage { get; set; }

    public double WorstThresholdUsage { get; set; }

    public string Summary { get; set; } = string.Empty;

    public IReadOnlyList<string> Signals { get; set; } = Array.Empty<string>();
}

public static class IntrinsicsCalibrationRuntimeFactory
{
    private static readonly HashSet<int> BrownConradyLengths = new() { 0, 4, 5, 8, 12, 14 };
    private const double RuntimeMeanWarningThreshold = 0.25;
    private const double RuntimeMeanFailureThreshold = 0.35;
    private const double RuntimeMaxWarningThreshold = 0.45;
    private const double RuntimeMaxFailureThreshold = 0.60;

    public static bool TryCreate(
        string calibrationData,
        CalibrationKindV2 expectedKind,
        DistortionModelV2[] allowedDistortionModels,
        out IntrinsicsCalibrationRuntime runtime,
        out string error)
    {
        runtime = new IntrinsicsCalibrationRuntime(
            new CalibrationBundleV2(),
            new Mat(),
            new Mat(),
            default,
            string.Empty);
        error = string.Empty;

        if (!CalibrationBundleV2Json.TryDeserialize(calibrationData, out var bundle, out error))
        {
            return false;
        }

        if (bundle.CalibrationKind != expectedKind)
        {
            error = $"CalibrationKind mismatch. Expected {expectedKind}, actual {bundle.CalibrationKind}.";
            return false;
        }

        if (!CalibrationBundleV2Json.TryRequireAccepted(bundle, out error))
        {
            return false;
        }

        if (bundle.ImageSize == null || bundle.ImageSize.Width <= 0 || bundle.ImageSize.Height <= 0)
        {
            error = "ImageSize is required and must be positive.";
            return false;
        }

        if (!CalibrationBundleV2Json.TryRequireIntrinsics(bundle, allowedDistortionModels, out var intrinsics, out var distortion, out error))
        {
            return false;
        }

        if (!ValidateCameraMatrix(intrinsics.CameraMatrix, out error))
        {
            return false;
        }

        if (!ValidateDistortion(distortion, out error))
        {
            return false;
        }

        var cameraMatrix = CalibrationBundleV2Helpers.ToMat(intrinsics.CameraMatrix);
        var distCoeffs = CreateDistortionMat(distortion.Coefficients);
        var imageSize = new Size(bundle.ImageSize.Width, bundle.ImageSize.Height);
        var fingerprint = ComputeFingerprint(calibrationData);
        var runtimeQualityAssessment = AssessRuntimeQuality(bundle);

        runtime = new IntrinsicsCalibrationRuntime(bundle, cameraMatrix, distCoeffs, imageSize, fingerprint, runtimeQualityAssessment);
        return true;
    }

    public static bool TryRequireExactImageSize(IntrinsicsCalibrationRuntime runtime, Size runtimeImageSize, out string error)
    {
        if (runtimeImageSize != runtime.CalibrationImageSize)
        {
            error = $"Runtime image size {runtimeImageSize.Width}x{runtimeImageSize.Height} does not match calibration image size {runtime.CalibrationImageSize.Width}x{runtime.CalibrationImageSize.Height}.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    public static string BuildCacheKey(
        IntrinsicsCalibrationRuntime runtime,
        string profile,
        Size outputSize,
        double balance = 0,
        double sizeFactor = 1.0)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{profile}|{runtime.Fingerprint}|{runtime.CalibrationImageSize.Width}x{runtime.CalibrationImageSize.Height}|{outputSize.Width}x{outputSize.Height}|b={balance:F4}|s={sizeFactor:F4}");
    }

    public static IntrinsicsRuntimeQualityAssessment AssessRuntimeQuality(CalibrationBundleV2 bundle)
    {
        var signals = new List<string>();
        var failed = false;
        var warning = false;

        if (!bundle.Quality.Accepted)
        {
            failed = true;
            signals.Add("Calibration bundle is not accepted for production use.");
        }

        EvaluateRuntimeMetric(
            "mean reprojection error",
            bundle.Quality.MeanError,
            RuntimeMeanWarningThreshold,
            RuntimeMeanFailureThreshold,
            signals,
            ref warning,
            ref failed);
        EvaluateRuntimeMetric(
            "max reprojection error",
            bundle.Quality.MaxError,
            RuntimeMaxWarningThreshold,
            RuntimeMaxFailureThreshold,
            signals,
            ref warning,
            ref failed);

        if (!failed && !warning)
        {
            signals.Add("Calibration baseline remains comfortably inside runtime monitoring thresholds.");
        }

        var status = failed ? "fail" : warning ? "warning" : "pass";
        var meanUsage = SafeThresholdUsage(bundle.Quality.MeanError, RuntimeMeanFailureThreshold);
        var maxUsage = SafeThresholdUsage(bundle.Quality.MaxError, RuntimeMaxFailureThreshold);
        var worstUsage = Math.Max(meanUsage, maxUsage);

        return new IntrinsicsRuntimeQualityAssessment
        {
            GatePassed = !failed,
            Status = status,
            DriftRisk = failed ? "high" : warning ? "moderate" : "low",
            BaselineMeanError = bundle.Quality.MeanError,
            BaselineMaxError = bundle.Quality.MaxError,
            MeanWarningThreshold = RuntimeMeanWarningThreshold,
            MeanFailureThreshold = RuntimeMeanFailureThreshold,
            MaxWarningThreshold = RuntimeMaxWarningThreshold,
            MaxFailureThreshold = RuntimeMaxFailureThreshold,
            MeanThresholdUsage = meanUsage,
            MaxThresholdUsage = maxUsage,
            WorstThresholdUsage = worstUsage,
            Summary = BuildRuntimeSummary(status),
            Signals = signals
        };
    }

    public static Dictionary<string, object> BuildRuntimeMonitoringOutput(IntrinsicsCalibrationRuntime runtime)
    {
        var assessment = runtime.RuntimeQualityAssessment;
        return new Dictionary<string, object>
        {
            ["CalibrationMeanError"] = assessment.BaselineMeanError,
            ["CalibrationMaxError"] = assessment.BaselineMaxError,
            ["RuntimeQualityGatePassed"] = assessment.GatePassed,
            ["RuntimeQualityGateStatus"] = assessment.Status,
            ["RuntimeDriftRisk"] = assessment.DriftRisk,
            ["RuntimeQualityGateSummary"] = assessment.Summary,
            ["RuntimeQualitySignals"] = assessment.Signals.ToArray(),
            ["RuntimeQualityMeanThresholdUsage"] = assessment.MeanThresholdUsage,
            ["RuntimeQualityMaxThresholdUsage"] = assessment.MaxThresholdUsage,
            ["RuntimeQualityWorstThresholdUsage"] = assessment.WorstThresholdUsage,
            ["RuntimeQualityGateThresholds"] = new Dictionary<string, double>
            {
                ["MeanWarning"] = assessment.MeanWarningThreshold,
                ["MeanFailure"] = assessment.MeanFailureThreshold,
                ["MaxWarning"] = assessment.MaxWarningThreshold,
                ["MaxFailure"] = assessment.MaxFailureThreshold
            },
            ["RuntimeQualityMonitoringMode"] = "heuristic-baseline-only"
        };
    }

    private static bool ValidateCameraMatrix(double[][] cameraMatrix, out string error)
    {
        if (!CalibrationBundleV2Json.HasMatrix(cameraMatrix, 3, 3))
        {
            error = "CameraMatrix must be 3x3.";
            return false;
        }

        if (!CalibrationBundleV2Helpers.IsFiniteMatrix(cameraMatrix))
        {
            error = "CameraMatrix contains non-finite values.";
            return false;
        }

        var fx = cameraMatrix[0][0];
        var fy = cameraMatrix[1][1];
        if (fx <= 0 || fy <= 0)
        {
            error = "CameraMatrix focal lengths fx/fy must be positive.";
            return false;
        }

        if (!NearZero(cameraMatrix[2][0]) || !NearZero(cameraMatrix[2][1]) || !NearOne(cameraMatrix[2][2]))
        {
            error = "CameraMatrix last row must be [0, 0, 1].";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static bool ValidateDistortion(CalibrationDistortionV2 distortion, out string error)
    {
        if (!CalibrationBundleV2Helpers.IsFiniteVector(distortion.Coefficients))
        {
            error = "Distortion coefficients contain non-finite values.";
            return false;
        }

        switch (distortion.Model)
        {
            case DistortionModelV2.None:
                if (distortion.Coefficients.Length != 0)
                {
                    error = "Distortion model None requires empty coefficients.";
                    return false;
                }

                break;
            case DistortionModelV2.BrownConrady:
                if (!BrownConradyLengths.Contains(distortion.Coefficients.Length))
                {
                    error = $"BrownConrady supports coefficient lengths: {string.Join(", ", BrownConradyLengths.OrderBy(v => v))}.";
                    return false;
                }

                break;
            case DistortionModelV2.KannalaBrandt:
                if (distortion.Coefficients.Length != 4)
                {
                    error = "KannalaBrandt requires exactly 4 distortion coefficients.";
                    return false;
                }

                break;
            default:
                error = $"Unsupported distortion model: {distortion.Model}.";
                return false;
        }

        error = string.Empty;
        return true;
    }

    private static Mat CreateDistortionMat(double[] coefficients)
    {
        if (coefficients.Length == 0)
        {
            return new Mat();
        }

        var mat = new Mat(coefficients.Length, 1, MatType.CV_64FC1);
        for (var i = 0; i < coefficients.Length; i++)
        {
            mat.Set(i, 0, coefficients[i]);
        }

        return mat;
    }

    private static string ComputeFingerprint(string text)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(bytes);
    }

    private static void EvaluateRuntimeMetric(
        string metricName,
        double value,
        double warningThreshold,
        double failureThreshold,
        ICollection<string> signals,
        ref bool warning,
        ref bool failed)
    {
        if (value > failureThreshold)
        {
            failed = true;
            signals.Add($"{metricName}={value:F4}px exceeds runtime fail threshold {failureThreshold:F4}px.");
            return;
        }

        if (value > warningThreshold)
        {
            warning = true;
            signals.Add($"{metricName}={value:F4}px exceeds runtime warning threshold {warningThreshold:F4}px.");
        }
    }

    private static double SafeThresholdUsage(double value, double threshold)
    {
        if (threshold <= 0)
        {
            return 0;
        }

        return value / threshold;
    }

    private static string BuildRuntimeSummary(string status)
    {
        return status switch
        {
            "fail" => "Runtime quality gate failed. Treat this as a drift-risk alert and verify calibration offline; this heuristic does not recalibrate the camera.",
            "warning" => "Runtime quality gate entered warning state. Continue using the calibration with caution and schedule offline verification if error trends worsen; this heuristic does not recalibrate the camera.",
            "pass" => "Runtime quality gate passed with healthy baseline headroom. This heuristic monitors calibration health only and does not recalibrate the camera.",
            _ => "Runtime quality state is unknown. This heuristic monitors calibration health only and does not recalibrate the camera."
        };
    }

    private static bool NearZero(double value)
    {
        return Math.Abs(value) < 1e-9;
    }

    private static bool NearOne(double value)
    {
        return Math.Abs(value - 1.0) < 1e-9;
    }
}
