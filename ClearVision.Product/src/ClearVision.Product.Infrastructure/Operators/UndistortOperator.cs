using ClearVision.Product.Core.Attributes;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Operators;
using ClearVision.Product.Infrastructure.Calibration;
using Microsoft.Extensions.Logging;
using OpenCvSharp;

namespace ClearVision.Product.Infrastructure.Operators;

[OperatorMeta(
    DisplayName = "畸变校正",
    Description = "使用标定数据校正镜头畸变。",
    CategoryId = OperatorCategoryId.CalibrationAndCoordinates,
    IconName = "undistort",
    Keywords = new[] { "Undistort", "Distortion", "Calibration" }
)]
[InputPort("Image", "Input Image", PortDataType.Image, IsRequired = true)]
[InputPort("CalibrationData", "Calibration Data", PortDataType.String, IsRequired = false)]
[OutputPort("Image", "Undistorted Image", PortDataType.Image)]
public class UndistortOperator : OperatorBase, IDisposable
{
    private const int MaxCacheEntries = 16;
    private readonly Dictionary<string, (Mat map1, Mat map2)> _mapCache = new(StringComparer.Ordinal);
    private readonly Queue<string> _cacheOrder = new();
    private readonly List<(Mat map1, Mat map2)> _retiredMaps = new();
    private readonly object _cacheLock = new();
    private int _activeMapLeases;
    private bool _disposed;

    public override OperatorType OperatorType => OperatorType.Undistort;

    public UndistortOperator(ILogger<UndistortOperator> logger) : base(logger)
    {
    }

    protected override Task<OperatorExecutionOutput> ExecuteCoreAsync(
        Operator @operator,
        Dictionary<string, object>? inputs,
        CancellationToken cancellationToken)
    {
        if (!TryGetInputImage(inputs, "Image", out var imageWrapper) || imageWrapper == null)
        {
            return Task.FromResult(OperatorExecutionOutput.Failure("Input image is required."));
        }

        if (!TryResolveCalibrationData(@operator, inputs, out var calibrationData, out var resolveError))
        {
            return Task.FromResult(OperatorExecutionOutput.Failure(resolveError));
        }

        var src = imageWrapper.GetMat();
        if (src.Empty())
        {
            return Task.FromResult(OperatorExecutionOutput.Failure("Input image is invalid."));
        }

        if (!IntrinsicsCalibrationRuntimeFactory.TryCreate(
                calibrationData!,
                CalibrationKindV2.CameraIntrinsics,
                new[] { DistortionModelV2.BrownConrady, DistortionModelV2.None },
                out var runtime,
                out var parseError))
        {
            return Task.FromResult(OperatorExecutionOutput.Failure($"Invalid CalibrationBundleV2 for Undistort: {parseError}"));
        }

        using (runtime)
        {
            if (!IntrinsicsCalibrationRuntimeFactory.TryRequireExactImageSize(runtime, src.Size(), out var sizeError))
            {
                return Task.FromResult(OperatorExecutionOutput.Failure(sizeError));
            }

            var cacheKey = IntrinsicsCalibrationRuntimeFactory.BuildCacheKey(
                runtime,
                profile: "undistort-brown",
                outputSize: src.Size());

            using var remap = GetOrCreateRemap(cacheKey, runtime.CameraMatrix, runtime.DistCoeffs, src.Size());
            var dst = new Mat();
            Cv2.Remap(src, dst, remap.Map1, remap.Map2, InterpolationFlags.Linear, BorderTypes.Constant);

            var diagnostics = runtime.Bundle.Quality.Diagnostics?.Count > 0
                ? string.Join("; ", runtime.Bundle.Quality.Diagnostics)
                : "No diagnostics";
            var runtimeMonitoring = IntrinsicsCalibrationRuntimeFactory.BuildRuntimeMonitoringOutput(runtime);
            var gateStatus = runtime.RuntimeQualityAssessment.Status;

            if (!string.Equals(gateStatus, "pass", StringComparison.Ordinal))
            {
                Logger.LogWarning(
                    "Undistort runtime quality gate status={Status}. Mean={MeanError:F4}px, Max={MaxError:F4}px. {Summary}",
                    gateStatus,
                    runtime.RuntimeQualityAssessment.BaselineMeanError,
                    runtime.RuntimeQualityAssessment.BaselineMaxError,
                    runtime.RuntimeQualityAssessment.Summary);
            }

            var output = runtimeMonitoring;
            output["Applied"] = true;
            output["Accepted"] = runtime.Bundle.Quality.Accepted;
            output["CalibrationKind"] = runtime.Bundle.CalibrationKind.ToString();
            output["DistortionModel"] = runtime.Bundle.Distortion?.Model.ToString() ?? DistortionModelV2.None.ToString();
            output["Message"] = "Undistortion applied using CalibrationBundleV2.";
            output["Diagnostics"] = diagnostics;

            return Task.FromResult(OperatorExecutionOutput.Success(CreateImageOutput(dst, output)));
        }
    }

    public override ValidationResult ValidateParameters(Operator @operator)
    {
        return ValidationResult.Valid();
    }

    public void Dispose()
    {
        List<(Mat map1, Mat map2)>? mapsToDispose = null;

        lock (_cacheLock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            RetireMapsLocked(_mapCache.Values);
            _mapCache.Clear();
            _cacheOrder.Clear();

            while (_activeMapLeases > 0)
            {
                Monitor.Wait(_cacheLock);
            }

            mapsToDispose = DrainRetiredMapsLocked();
        }

        DisposeMaps(mapsToDispose);
    }

    private bool TryResolveCalibrationData(
        Operator @operator,
        Dictionary<string, object>? inputs,
        out string? calibrationData,
        out string error)
    {
        calibrationData = null;
        error = "Calibration data is required.";

        if (inputs != null &&
            inputs.TryGetValue("CalibrationData", out var calibrationObj) &&
            calibrationObj is string calibrationText &&
            !string.IsNullOrWhiteSpace(calibrationText))
        {
            calibrationData = calibrationText;
            error = string.Empty;
            return true;
        }

        var inlineParameterData = GetStringParam(@operator, "CalibrationData", "");
        if (string.IsNullOrWhiteSpace(inlineParameterData))
        {
            return false;
        }

        calibrationData = inlineParameterData;
        error = string.Empty;
        return true;
    }

    private RemapLease GetOrCreateRemap(string key, Mat cameraMatrix, Mat distCoeffs, Size imageSize)
    {
        List<(Mat map1, Mat map2)>? mapsToDispose = null;
        Mat map1;
        Mat map2;

        lock (_cacheLock)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(UndistortOperator));
            }

            if (_mapCache.TryGetValue(key, out var cached))
            {
                map1 = cached.map1;
                map2 = cached.map2;
            }
            else
            {
                map1 = new Mat();
                map2 = new Mat();
                using var rectification = new Mat();
                Cv2.InitUndistortRectifyMap(
                    cameraMatrix,
                    distCoeffs,
                    rectification,
                    cameraMatrix,
                    imageSize,
                    MatType.CV_32FC1,
                    map1,
                    map2);

                _mapCache[key] = (map1, map2);
                _cacheOrder.Enqueue(key);
                mapsToDispose = TrimCacheIfNeededLocked();
            }

            _activeMapLeases++;
        }

        DisposeMaps(mapsToDispose);
        return new RemapLease(this, map1, map2);
    }

    private List<(Mat map1, Mat map2)>? TrimCacheIfNeededLocked()
    {
        List<(Mat map1, Mat map2)>? mapsToDispose = null;

        while (_cacheOrder.Count > MaxCacheEntries)
        {
            var oldestKey = _cacheOrder.Dequeue();
            if (!_mapCache.Remove(oldestKey, out var maps))
            {
                continue;
            }

            if (_activeMapLeases > 0)
            {
                _retiredMaps.Add(maps);
            }
            else
            {
                mapsToDispose ??= new List<(Mat map1, Mat map2)>();
                mapsToDispose.Add(maps);
            }
        }

        return mapsToDispose;
    }

    private void ReleaseRemapLease()
    {
        List<(Mat map1, Mat map2)>? mapsToDispose = null;

        lock (_cacheLock)
        {
            _activeMapLeases--;
            if (_activeMapLeases == 0)
            {
                if (_disposed)
                {
                    Monitor.PulseAll(_cacheLock);
                }
                else
                {
                    mapsToDispose = DrainRetiredMapsLocked();
                }
            }
        }

        DisposeMaps(mapsToDispose);
    }

    private void RetireMapsLocked(IEnumerable<(Mat map1, Mat map2)> maps)
    {
        _retiredMaps.AddRange(maps);
    }

    private List<(Mat map1, Mat map2)>? DrainRetiredMapsLocked()
    {
        if (_retiredMaps.Count == 0)
        {
            return null;
        }

        var mapsToDispose = new List<(Mat map1, Mat map2)>(_retiredMaps);
        _retiredMaps.Clear();
        return mapsToDispose;
    }

    private static void DisposeMaps(List<(Mat map1, Mat map2)>? maps)
    {
        if (maps == null)
        {
            return;
        }

        foreach (var (map1, map2) in maps)
        {
            map1.Dispose();
            map2.Dispose();
        }
    }

    private sealed class RemapLease : IDisposable
    {
        private UndistortOperator? _owner;

        public RemapLease(UndistortOperator owner, Mat map1, Mat map2)
        {
            _owner = owner;
            Map1 = map1;
            Map2 = map2;
        }

        public Mat Map1 { get; }

        public Mat Map2 { get; }

        public void Dispose()
        {
            var owner = Interlocked.Exchange(ref _owner, null);
            owner?.ReleaseRemapLease();
        }
    }
}
