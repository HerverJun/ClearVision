using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Acme.Product.Core.Entities;
using Acme.Product.Core.Enums;
using Acme.Product.Core.ValueObjects;
using Acme.Product.Infrastructure.Operators;
using Microsoft.Extensions.Logging.Abstractions;

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

var result = await OpenCvCalibrationDatasetRunner.RunAsync(options);
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(options.OutputPath))!);
File.WriteAllText(options.OutputPath, JsonSerializer.Serialize(result, JsonSettings.Indented));

if (!string.IsNullOrWhiteSpace(options.ReportPath))
{
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(options.ReportPath))!);
    File.WriteAllText(options.ReportPath, MarkdownReport.Create(result));
}

Console.WriteLine(
    $"OpenCV calibration dataset baseline complete: {result.Summary.Passed}/{result.Summary.CaseCount} passed, " +
    $"failed={result.Summary.Failed}, reprojection={result.Summary.ReprojectionRmsPx:F4}px, output={options.OutputPath}");

return result.Summary.Failed == 0 ? 0 : 1;

internal static class OpenCvCalibrationDatasetRunner
{
    public static async Task<BaselineResult> RunAsync(RunnerOptions options)
    {
        var stopwatch = Stopwatch.StartNew();
        var allocationBefore = GC.GetTotalAllocatedBytes(precise: true);
        var tempRoot = Path.Combine(Path.GetFullPath(".tmp"), $"opencv-calibration-dataset-{Guid.NewGuid():N}");
        var tempImages = Path.Combine(tempRoot, "left-images");
        var tempRightImages = Path.Combine(tempRoot, "right-images");
        var leftBundlePath = Path.Combine(tempRoot, "left_camera_bundle.json");
        var rightBundlePath = Path.Combine(tempRoot, "right_camera_bundle.json");
        var stereoBundlePath = Path.Combine(tempRoot, "stereo_camera_bundle.json");
        Directory.CreateDirectory(tempImages);
        Directory.CreateDirectory(tempRightImages);
        var caseIds = new HashSet<string>(options.CaseIds, StringComparer.Ordinal);
        var hasCaseFilter = caseIds.Count > 0;
        bool ShouldRunCase(string caseId)
        {
            return !hasCaseFilter || caseIds.Contains(caseId);
        }

        var cases = new List<CaseResult>();
        try
        {
            var index = DatasetIndex.Load(options.IndexPath);
            foreach (var imagePath in index.SingleCameraImages)
            {
                var source = ResolveRepoPath(imagePath);
                if (!File.Exists(source))
                {
                    throw new FileNotFoundException("Calibration sample image is missing.", source);
                }

                File.Copy(source, Path.Combine(tempImages, NormalizeStereoTempFileName(Path.GetFileName(source))), overwrite: true);
            }

            var stereoPairs = index.StereoPairs ?? Array.Empty<StereoPair>();
            foreach (var pair in stereoPairs)
            {
                var rightSource = ResolveRepoPath(pair.RightImagePath);
                if (!File.Exists(rightSource))
                {
                    throw new FileNotFoundException("Right calibration sample image is missing.", rightSource);
                }

                File.Copy(rightSource, Path.Combine(tempRightImages, NormalizeStereoTempFileName(Path.GetFileName(rightSource))), overwrite: true);
            }

            if (ShouldRunCase("opencv_calibration_left_camera"))
            {
                cases.Add(await RunCameraCaseAsync(
                    id: "opencv_calibration_left_camera",
                    scenario: "OpenCV calibration sample left camera folder",
                    imageFolder: tempImages,
                    outputBundlePath: leftBundlePath,
                    options));
            }

            if (ShouldRunCase("opencv_calibration_right_camera"))
            {
                cases.Add(await RunCameraCaseAsync(
                    id: "opencv_calibration_right_camera",
                    scenario: "OpenCV calibration sample right camera folder",
                    imageFolder: tempRightImages,
                    outputBundlePath: rightBundlePath,
                    options));
            }

            if (ShouldRunCase("opencv_calibration_stereo_rig"))
            {
                cases.Add(hasCaseFilter
                    ? CreateStereoMetadataCaseResult(
                        id: "opencv_calibration_stereo_rig",
                        scenario: "OpenCV calibration sample stereo pair metadata",
                        stereoPairs: stereoPairs,
                        calibrationFiles: index.CalibrationFiles,
                        expectedPairCount: stereoPairs.Count,
                        thresholds: options.CreateThresholdMetrics())
                    : await RunStereoCaseAsync(
                        id: "opencv_calibration_stereo_rig",
                        scenario: "OpenCV calibration sample stereo rig",
                        leftFolder: tempImages,
                        rightFolder: tempRightImages,
                        outputBundlePath: stereoBundlePath,
                        expectedPairCount: stereoPairs.Count,
                        options));
            }

            if (ShouldRunCase("opencv_calibration_stereo_metadata"))
            {
                cases.Add(CreateStereoMetadataCaseResult(
                    id: "opencv_calibration_stereo_metadata",
                    scenario: "OpenCV calibration sample stereo pair metadata",
                    stereoPairs: stereoPairs,
                    calibrationFiles: index.CalibrationFiles,
                    expectedPairCount: stereoPairs.Count,
                    thresholds: options.CreateThresholdMetrics()));
            }
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            var allocationAfter = GC.GetTotalAllocatedBytes(precise: true);
            if (!hasCaseFilter || caseIds.Contains("opencv_calibration_left_camera"))
            {
                cases.Add(new CaseResult(
                    "opencv_calibration_left_camera",
                    "CameraCalibration",
                    "OpenCV calibration sample left camera folder",
                    false,
                    Math.Round(stopwatch.Elapsed.TotalMilliseconds, 3),
                    Math.Max(0, allocationAfter - allocationBefore),
                    false,
                    -1.0,
                    -1.0,
                    0,
                    0,
                    "runner_exception",
                    ex.GetBaseException().Message,
                    new Dictionary<string, object?>
                    {
                        ["Thresholds"] = options.CreateThresholdMetrics()
                    }));
            }
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }

        var failed = cases.Count(item => !item.Passed);
        var acceptedCaseCount = cases.Count(item => item.Accepted);
        var requiredAcceptedCaseCount = options.RequireAccepted ? cases.Count : 0;
        var finiteRms = cases.Select(item => item.ReprojectionRmsPx).Where(double.IsFinite).DefaultIfEmpty(-1.0).ToArray();
        var finiteMax = cases.Select(item => item.MaxReprojectionErrorPx).Where(double.IsFinite).DefaultIfEmpty(-1.0).ToArray();
        var detected = cases.Count == 0 ? 0 : cases.Min(item => Math.Max(0, item.DetectedImageCount));
        var total = cases.Sum(item => Math.Max(0, item.TotalImages));
        return new BaselineResult(
            "dataset",
            new BaselineSummary(
                DateTimeOffset.UtcNow,
                cases.Count,
                cases.Count - failed,
                failed,
                Math.Round(cases.Sum(item => item.RuntimeMs), 3),
                cases.Sum(item => item.MemoryAllocationBytes),
                !options.RequireAccepted || acceptedCaseCount == cases.Count,
                Math.Round(finiteRms.Max(), 6),
                Math.Round(finiteMax.Max(), 6),
                detected,
                total,
                "opencv-calibration-samples-left-right-stereo",
                acceptedCaseCount,
                requiredAcceptedCaseCount,
                options.CreateThresholdMetrics()),
            CreateOperatorSummaries(cases),
            cases);
    }

    private static IReadOnlyList<OperatorSummary> CreateOperatorSummaries(IReadOnlyList<CaseResult> cases)
    {
        return cases
            .Where(item => item.Operator is "CameraCalibration" or "StereoCalibration")
            .GroupBy(item => item.Operator, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => new OperatorSummary(
                group.Key,
                group.Count(),
                group.Count(item => item.Passed),
                group.Count(item => !item.Passed),
                Math.Round(group.Average(item => item.RuntimeMs), 3),
                Convert.ToInt64(Math.Round(group.Average(item => item.MemoryAllocationBytes))),
                true,
                "dataset",
                "OpenCV calibration samples"))
            .ToArray();
    }

    private static async Task<CaseResult> RunCameraCaseAsync(
        string id,
        string scenario,
        string imageFolder,
        string outputBundlePath,
        RunnerOptions options)
    {
        var stopwatch = Stopwatch.StartNew();
        var allocationBefore = GC.GetTotalAllocatedBytes(precise: true);
        try
        {
            var op = CreateCameraCalibrationOperator(imageFolder, outputBundlePath);
            var executor = new CameraCalibrationOperator(NullLogger<CameraCalibrationOperator>.Instance);
            var execution = await executor.ExecuteAsync(op, null);
            DisposeImageOutput(execution.OutputData);

            stopwatch.Stop();
            var allocationAfter = GC.GetTotalAllocatedBytes(precise: true);
            return CreateCameraCaseResult(
                id,
                scenario,
                execution,
                elapsedMs: stopwatch.Elapsed.TotalMilliseconds,
                allocatedBytes: Math.Max(0, allocationAfter - allocationBefore),
                minDetectedImages: options.MinDetectedImages,
                maxReprojectionRmsPx: options.MaxReprojectionRmsPx,
                requireAccepted: options.RequireAccepted,
                outputBundlePath: outputBundlePath,
                thresholds: options.CreateThresholdMetrics());
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            var allocationAfter = GC.GetTotalAllocatedBytes(precise: true);
            return Failure(id, "CameraCalibration", scenario, stopwatch.Elapsed.TotalMilliseconds, Math.Max(0, allocationAfter - allocationBefore), "runner_exception", ex.GetBaseException().Message, options.CreateThresholdMetrics());
        }
    }

    private static async Task<CaseResult> RunStereoCaseAsync(
        string id,
        string scenario,
        string leftFolder,
        string rightFolder,
        string outputBundlePath,
        int expectedPairCount,
        RunnerOptions options)
    {
        var stopwatch = Stopwatch.StartNew();
        var allocationBefore = GC.GetTotalAllocatedBytes(precise: true);
        try
        {
            var op = CreateStereoCalibrationOperator(leftFolder, rightFolder, outputBundlePath, options.MinStereoPairs);
            var executor = new StereoCalibrationOperator(NullLogger<StereoCalibrationOperator>.Instance);
            var execution = await executor.ExecuteAsync(op, null);
            DisposeImageOutput(execution.OutputData);

            stopwatch.Stop();
            var allocationAfter = GC.GetTotalAllocatedBytes(precise: true);
            return CreateStereoCaseResult(
                id,
                scenario,
                execution,
                elapsedMs: stopwatch.Elapsed.TotalMilliseconds,
                allocatedBytes: Math.Max(0, allocationAfter - allocationBefore),
                expectedPairCount,
                options,
                outputBundlePath,
                thresholds: options.CreateThresholdMetrics());
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            var allocationAfter = GC.GetTotalAllocatedBytes(precise: true);
            return Failure(id, "StereoCalibration", scenario, stopwatch.Elapsed.TotalMilliseconds, Math.Max(0, allocationAfter - allocationBefore), "runner_exception", ex.GetBaseException().Message, options.CreateThresholdMetrics());
        }
    }

    private static CaseResult CreateStereoMetadataCaseResult(
        string id,
        string scenario,
        IReadOnlyList<StereoPair> stereoPairs,
        IReadOnlyDictionary<string, string> calibrationFiles,
        int expectedPairCount,
        IReadOnlyDictionary<string, object?> thresholds)
    {
        var stopwatch = Stopwatch.StartNew();
        var allocationBefore = GC.GetTotalAllocatedBytes(precise: true);
        var errors = new List<string>();
        var pairIndices = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (stereoPairs.Count == 0)
        {
            errors.Add("manifest contains no stereo pairs");
        }

        foreach (var pair in stereoPairs)
        {
            if (!pairIndices.Add(pair.Index))
            {
                errors.Add($"duplicate stereo pair index {pair.Index}");
            }

            if (!File.Exists(ResolveRepoPath(pair.LeftImagePath)))
            {
                errors.Add($"left image missing for stereo pair {pair.Index}: {pair.LeftImagePath}");
            }

            if (!File.Exists(ResolveRepoPath(pair.RightImagePath)))
            {
                errors.Add($"right image missing for stereo pair {pair.Index}: {pair.RightImagePath}");
            }
        }

        foreach (var required in new[] { "intrinsics", "left_intrinsics", "stereo_calib" })
        {
            if (!calibrationFiles.TryGetValue(required, out var path) || string.IsNullOrWhiteSpace(path))
            {
                errors.Add($"calibration file metadata missing: {required}");
                continue;
            }

            if (!File.Exists(ResolveRepoPath(path)))
            {
                errors.Add($"calibration file missing: {required}={path}");
            }
        }

        stopwatch.Stop();
        var allocationAfter = GC.GetTotalAllocatedBytes(precise: true);
        var passed = errors.Count == 0 && stereoPairs.Count == expectedPairCount;
        return new CaseResult(
            id,
            "StereoMetadata",
            scenario,
            passed,
            Math.Round(stopwatch.Elapsed.TotalMilliseconds, 3),
            Math.Max(0, allocationAfter - allocationBefore),
            true,
            0.0,
            0.0,
            stereoPairs.Count,
            expectedPairCount,
            passed ? null : "stereo_metadata_failure",
            passed ? null : string.Join("; ", errors),
            new Dictionary<string, object?>
            {
                ["ExpectedPairsFromManifest"] = expectedPairCount,
                ["StereoPairCount"] = stereoPairs.Count,
                ["UniquePairIndexCount"] = pairIndices.Count,
                ["CalibrationFiles"] = calibrationFiles,
                ["Thresholds"] = thresholds
            });
    }

    private static CaseResult CreateCaseResult(
        string id,
        string scenario,
        Acme.Product.Core.Operators.OperatorExecutionOutput execution,
        double elapsedMs,
        long allocatedBytes,
        int minDetectedImages,
        double maxReprojectionRmsPx,
        bool requireAccepted,
        string outputBundlePath,
        IReadOnlyDictionary<string, object?> thresholds)
    {
        return CreateCameraCaseResult(id, scenario, execution, elapsedMs, allocatedBytes, minDetectedImages, maxReprojectionRmsPx, requireAccepted, outputBundlePath, thresholds);
    }

    private static CaseResult CreateCameraCaseResult(
        string id,
        string scenario,
        Acme.Product.Core.Operators.OperatorExecutionOutput execution,
        double elapsedMs,
        long allocatedBytes,
        int minDetectedImages,
        double maxReprojectionRmsPx,
        bool requireAccepted,
        string outputBundlePath,
        IReadOnlyDictionary<string, object?> thresholds)
    {
        if (!execution.IsSuccess || execution.OutputData is null)
        {
            return Failure(id, "CameraCalibration", scenario, elapsedMs, allocatedBytes, "operator_failure", execution.ErrorMessage ?? "operator returned failure", thresholds);
        }

        var output = execution.OutputData;
        var accepted = GetBool(output, "Accepted");
        var reprojection = GetDouble(output, "ReprojectionError");
        var maxReprojection = GetDouble(output, "MaxReprojectionError");
        var imageCount = GetInt(output, "ImageCount");
        var totalImages = GetInt(output, "TotalImages");
        var rejectedDetectionCount = GetInt(output, "RejectedDetectionCount");
        var rejectedOutlierCount = GetInt(output, "RejectedOutlierCount");
        var calibrationJson = output.TryGetValue("CalibrationData", out var calibrationObj) ? calibrationObj?.ToString() : "";
        var bundleRoundTripValid = !string.IsNullOrWhiteSpace(calibrationJson) && IsValidCalibrationBundleJson(calibrationJson!);
        var outputFileWritten = File.Exists(outputBundlePath);

        var errors = new List<string>();
        if (requireAccepted && !accepted)
        {
            errors.Add("calibration bundle was not accepted");
        }

        if (!double.IsFinite(reprojection) || reprojection > maxReprojectionRmsPx)
        {
            errors.Add($"reprojection RMS {reprojection:F4}px exceeds {maxReprojectionRmsPx:F4}px");
        }

        if (imageCount < minDetectedImages)
        {
            errors.Add($"detected image count {imageCount} is below {minDetectedImages}");
        }

        if (!bundleRoundTripValid)
        {
            errors.Add("calibration bundle JSON failed schema round-trip smoke");
        }

        if (!outputFileWritten)
        {
            errors.Add("calibration output file was not written");
        }

        return new CaseResult(
            id,
            "CameraCalibration",
            scenario,
            errors.Count == 0,
            Math.Round(elapsedMs, 3),
            allocatedBytes,
            accepted,
            Math.Round(reprojection, 6),
            Math.Round(maxReprojection, 6),
            imageCount,
            totalImages,
            ClassifyCameraFailure(errors, accepted, reprojection, maxReprojectionRmsPx, imageCount, minDetectedImages, bundleRoundTripValid, outputFileWritten),
            errors.Count == 0 ? null : string.Join("; ", errors),
            new Dictionary<string, object?>
            {
                ["RejectedDetectionCount"] = rejectedDetectionCount,
                ["RejectedOutlierCount"] = rejectedOutlierCount,
                ["BundleRoundTripValid"] = bundleRoundTripValid,
                ["OutputFileWritten"] = outputFileWritten,
                ["Diagnostics"] = GetStringList(output, "Diagnostics"),
                ["Thresholds"] = thresholds
            });
    }

    private static CaseResult CreateStereoCaseResult(
        string id,
        string scenario,
        Acme.Product.Core.Operators.OperatorExecutionOutput execution,
        double elapsedMs,
        long allocatedBytes,
        int expectedPairCount,
        RunnerOptions options,
        string outputBundlePath,
        IReadOnlyDictionary<string, object?> thresholds)
    {
        if (!execution.IsSuccess || execution.OutputData is null)
        {
            return Failure(id, "StereoCalibration", scenario, elapsedMs, allocatedBytes, "operator_failure", execution.ErrorMessage ?? "operator returned failure", thresholds);
        }

        var output = execution.OutputData;
        var accepted = GetBool(output, "Accepted");
        var leftRms = GetDouble(output, "ReprojectionErrorLeft");
        var rightRms = GetDouble(output, "ReprojectionErrorRight");
        var stereoRms = GetDouble(output, "ReprojectionErrorStereo");
        var maxPerView = GetDouble(output, "MaxPerViewError");
        var maxLeft = GetDouble(output, "MaxPerViewErrorLeft");
        var maxRight = GetDouble(output, "MaxPerViewErrorRight");
        var epipolar = GetDouble(output, "EpipolarError");
        var validPairs = GetInt(output, "ValidPairs");
        var totalPairs = GetInt(output, "TotalPairs");
        var failedPairs = GetInt(output, "FailedPairs");
        var calibrationJson = output.TryGetValue("CalibrationData", out var calibrationObj) ? calibrationObj?.ToString() : "";
        var bundleRoundTripValid = !string.IsNullOrWhiteSpace(calibrationJson) && IsValidCalibrationBundleJson(calibrationJson!, stereo: true);
        var outputFileWritten = File.Exists(outputBundlePath);

        var errors = new List<string>();
        if (expectedPairCount > 0 && totalPairs != expectedPairCount)
        {
            errors.Add($"stereo metadata pair count {totalPairs} does not match manifest {expectedPairCount}");
        }

        if (options.RequireAccepted && !accepted)
        {
            errors.Add("stereo calibration bundle was not accepted");
        }

        if (validPairs < options.MinStereoPairs)
        {
            errors.Add($"valid stereo pair count {validPairs} is below {options.MinStereoPairs}");
        }

        if (!double.IsFinite(stereoRms) || stereoRms > options.MaxStereoReprojectionRmsPx)
        {
            errors.Add($"stereo RMS {stereoRms:F4}px exceeds {options.MaxStereoReprojectionRmsPx:F4}px");
        }

        if (!double.IsFinite(leftRms) || leftRms > options.MaxReprojectionRmsPx)
        {
            errors.Add($"left camera RMS {leftRms:F4}px exceeds {options.MaxReprojectionRmsPx:F4}px");
        }

        if (!double.IsFinite(rightRms) || rightRms > options.MaxReprojectionRmsPx)
        {
            errors.Add($"right camera RMS {rightRms:F4}px exceeds {options.MaxReprojectionRmsPx:F4}px");
        }

        if (!double.IsFinite(epipolar) || epipolar > options.MaxEpipolarErrorPx)
        {
            errors.Add($"epipolar error {epipolar:F4}px exceeds {options.MaxEpipolarErrorPx:F4}px");
        }

        if (!bundleRoundTripValid)
        {
            errors.Add("stereo calibration bundle JSON failed schema round-trip smoke");
        }

        if (!outputFileWritten)
        {
            errors.Add("stereo calibration output file was not written");
        }

        return new CaseResult(
            id,
            "StereoCalibration",
            scenario,
            errors.Count == 0,
            Math.Round(elapsedMs, 3),
            allocatedBytes,
            accepted,
            Math.Round(stereoRms, 6),
            Math.Round(maxPerView, 6),
            validPairs,
            totalPairs,
            ClassifyStereoFailure(errors, accepted, validPairs, options.MinStereoPairs, stereoRms, options.MaxStereoReprojectionRmsPx, epipolar, options.MaxEpipolarErrorPx, bundleRoundTripValid, outputFileWritten),
            errors.Count == 0 ? null : string.Join("; ", errors),
            new Dictionary<string, object?>
            {
                ["ReprojectionErrorLeft"] = Math.Round(leftRms, 6),
                ["ReprojectionErrorRight"] = Math.Round(rightRms, 6),
                ["ReprojectionErrorStereo"] = Math.Round(stereoRms, 6),
                ["MaxPerViewErrorLeft"] = Math.Round(maxLeft, 6),
                ["MaxPerViewErrorRight"] = Math.Round(maxRight, 6),
                ["EpipolarError"] = Math.Round(epipolar, 6),
                ["ExpectedPairsFromManifest"] = expectedPairCount,
                ["FailedPairs"] = failedPairs,
                ["BundleRoundTripValid"] = bundleRoundTripValid,
                ["OutputFileWritten"] = outputFileWritten,
                ["Thresholds"] = thresholds
            });
    }

    private static CaseResult Failure(
        string id,
        string operatorName,
        string scenario,
        double elapsedMs,
        long allocatedBytes,
        string failureReasonCode,
        string message,
        IReadOnlyDictionary<string, object?> thresholds)
    {
        return new CaseResult(
            id,
            operatorName,
            scenario,
            false,
            Math.Round(elapsedMs, 3),
            allocatedBytes,
            false,
            -1.0,
            -1.0,
            0,
            0,
            failureReasonCode,
            message,
            new Dictionary<string, object?>
            {
                ["Thresholds"] = thresholds
            });
    }

    private static Operator CreateCameraCalibrationOperator(string imageFolder, string outputPath)
    {
        var op = new Operator("OpenCV calibration dataset", OperatorType.CameraCalibration, 0, 0);
        op.AddParameter(Parameter("PatternType", "string", "Chessboard"));
        op.AddParameter(Parameter("BoardWidth", "int", 9));
        op.AddParameter(Parameter("BoardHeight", "int", 6));
        op.AddParameter(Parameter("SquareSize", "double", 25.0));
        op.AddParameter(Parameter("Mode", "string", "FolderCalibration"));
        op.AddParameter(Parameter("ImageFolder", "string", imageFolder));
        op.AddParameter(Parameter("CalibrationOutputPath", "string", outputPath));
        return op;
    }

    private static Operator CreateStereoCalibrationOperator(string leftFolder, string rightFolder, string outputPath, int minValidPairs)
    {
        var op = new Operator("OpenCV stereo calibration dataset", OperatorType.StereoCalibration, 0, 0);
        op.AddParameter(Parameter("PatternType", "string", "Chessboard"));
        op.AddParameter(Parameter("BoardWidth", "int", 9));
        op.AddParameter(Parameter("BoardHeight", "int", 6));
        op.AddParameter(Parameter("SquareSize", "double", 25.0));
        op.AddParameter(Parameter("Mode", "string", "FolderCalibration"));
        op.AddParameter(Parameter("LeftImageFolder", "string", leftFolder));
        op.AddParameter(Parameter("RightImageFolder", "string", rightFolder));
        op.AddParameter(Parameter("CalibrationOutputPath", "string", outputPath));
        op.AddParameter(Parameter("MinValidPairs", "int", minValidPairs));
        op.AddParameter(Parameter("ZeroDisparity", "bool", false));
        op.AddParameter(Parameter("Alpha", "double", 0.0));
        return op;
    }

    private static Parameter Parameter(string name, string dataType, object value)
    {
        return new Parameter(Guid.NewGuid(), name, name, string.Empty, dataType, value);
    }

    private static string ResolveRepoPath(string path)
    {
        if (Path.IsPathRooted(path))
        {
            return path;
        }

        return Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), path));
    }

    private static string NormalizeStereoTempFileName(string fileName)
    {
        var extension = Path.GetExtension(fileName);
        var stem = Path.GetFileNameWithoutExtension(fileName);
        foreach (var side in new[] { "left", "right" })
        {
            if (stem.StartsWith(side, StringComparison.OrdinalIgnoreCase) &&
                stem.Length > side.Length &&
                char.IsDigit(stem[side.Length]))
            {
                return $"{side}_{stem[side.Length..]}{extension}";
            }
        }

        return fileName;
    }

    private static bool IsValidCalibrationBundleJson(string json, bool stereo = false)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var requiredPayload = stereo
            ? root.TryGetProperty("stereo", out _)
            : root.TryGetProperty("intrinsics", out _) && root.TryGetProperty("distortion", out _);

        return root.TryGetProperty("schemaVersion", out var schemaVersion) &&
               schemaVersion.GetInt32() == 2 &&
               root.TryGetProperty("quality", out var quality) &&
               quality.TryGetProperty("accepted", out _) &&
               requiredPayload;
    }

    private static bool GetBool(Dictionary<string, object> output, string key)
    {
        return output.TryGetValue(key, out var value) && value switch
        {
            bool item => item,
            JsonElement { ValueKind: JsonValueKind.True } => true,
            JsonElement { ValueKind: JsonValueKind.False } => false,
            _ => bool.TryParse(value?.ToString(), out var parsed) && parsed
        };
    }

    private static double GetDouble(Dictionary<string, object> output, string key)
    {
        if (!output.TryGetValue(key, out var value) || value is null)
        {
            return double.NaN;
        }

        return value switch
        {
            double item => item,
            float item => item,
            decimal item => (double)item,
            int item => item,
            long item => item,
            JsonElement element when element.TryGetDouble(out var parsed) => parsed,
            _ => double.TryParse(value.ToString(), out var parsed) ? parsed : double.NaN
        };
    }

    private static int GetInt(Dictionary<string, object> output, string key)
    {
        if (!output.TryGetValue(key, out var value) || value is null)
        {
            return 0;
        }

        return value switch
        {
            int item => item,
            long item => (int)item,
            JsonElement element when element.TryGetInt32(out var parsed) => parsed,
            _ => int.TryParse(value.ToString(), out var parsed) ? parsed : 0
        };
    }

    private static IReadOnlyList<string> GetStringList(Dictionary<string, object> output, string key)
    {
        if (!output.TryGetValue(key, out var value) || value is null)
        {
            return Array.Empty<string>();
        }

        if (value is IReadOnlyList<string> strings)
        {
            return strings;
        }

        if (value is IEnumerable<string> enumerable)
        {
            return enumerable.ToArray();
        }

        if (value is JsonElement { ValueKind: JsonValueKind.Array } element)
        {
            return element.EnumerateArray().Select(item => item.ToString()).ToArray();
        }

        return new[] { value.ToString() ?? string.Empty };
    }

    private static string? ClassifyCameraFailure(
        IReadOnlyList<string> errors,
        bool accepted,
        double reprojection,
        double maxReprojectionRmsPx,
        int imageCount,
        int minDetectedImages,
        bool bundleRoundTripValid,
        bool outputFileWritten)
    {
        if (errors.Count == 0)
        {
            return null;
        }

        if (imageCount < minDetectedImages)
        {
            return "corner_detection_failure";
        }

        if (!double.IsFinite(reprojection) || reprojection > maxReprojectionRmsPx)
        {
            return "high_reprojection_error";
        }

        if (!bundleRoundTripValid)
        {
            return "round_trip_serialization_failure";
        }

        if (!outputFileWritten)
        {
            return "output_write_failure";
        }

        return accepted ? "threshold_failure" : "rejected_calibration";
    }

    private static string? ClassifyStereoFailure(
        IReadOnlyList<string> errors,
        bool accepted,
        int validPairs,
        int minStereoPairs,
        double stereoRms,
        double maxStereoRms,
        double epipolar,
        double maxEpipolar,
        bool bundleRoundTripValid,
        bool outputFileWritten)
    {
        if (errors.Count == 0)
        {
            return null;
        }

        if (validPairs < minStereoPairs)
        {
            return "stereo_pair_detection_failure";
        }

        if (!double.IsFinite(stereoRms) || stereoRms > maxStereoRms)
        {
            return "high_stereo_reprojection_error";
        }

        if (!double.IsFinite(epipolar) || epipolar > maxEpipolar)
        {
            return "high_epipolar_error";
        }

        if (!bundleRoundTripValid)
        {
            return "round_trip_serialization_failure";
        }

        if (!outputFileWritten)
        {
            return "output_write_failure";
        }

        return accepted ? "threshold_failure" : "rejected_calibration";
    }

    private static void DisposeImageOutput(Dictionary<string, object>? output)
    {
        if (output is null)
        {
            return;
        }

        if (output.TryGetValue("Image", out var image) && image is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}

internal sealed record DatasetIndex(
    [property: JsonPropertyName("name")]
    string Name,
    [property: JsonPropertyName("local_root")]
    string LocalRoot,
    [property: JsonPropertyName("single_camera_images")]
    IReadOnlyList<string> SingleCameraImages,
    [property: JsonPropertyName("stereo_pairs")]
    IReadOnlyList<StereoPair> StereoPairs,
    [property: JsonPropertyName("calibration_files")]
    IReadOnlyDictionary<string, string> CalibrationFiles)
{
    public static DatasetIndex Load(string path)
    {
        var jsonPath = Path.GetFullPath(path);
        if (!File.Exists(jsonPath))
        {
            throw new FileNotFoundException("Dataset index not found.", jsonPath);
        }

        var json = File.ReadAllText(jsonPath);
        return JsonSerializer.Deserialize<DatasetIndex>(json, JsonSettings.CamelCase) ??
               throw new InvalidOperationException($"Failed to parse dataset index: {jsonPath}");
    }
}

internal sealed record StereoPair(
    [property: JsonPropertyName("index")]
    string Index,
    [property: JsonPropertyName("left_image_path")]
    string LeftImagePath,
    [property: JsonPropertyName("right_image_path")]
    string RightImagePath);

internal sealed record BaselineResult(
    string EvidenceKind,
    BaselineSummary Summary,
    IReadOnlyList<OperatorSummary> Operators,
    IReadOnlyList<CaseResult> Cases);

internal sealed record OperatorSummary(
    string Operator,
    int CaseCount,
    int Passed,
    int Failed,
    double RuntimeMsAvg,
    long MemoryAllocationBytesAvg,
    bool HasPublicDataset,
    string EvidenceKind,
    string Dataset);

internal sealed record BaselineSummary(
    DateTimeOffset GeneratedAtUtc,
    int CaseCount,
    int Passed,
    int Failed,
    double RuntimeMs,
    long MemoryAllocationBytes,
    bool Accepted,
    double ReprojectionRmsPx,
    double MaxReprojectionErrorPx,
    int DetectedImageCount,
    int TotalImages,
    string Dataset,
    int AcceptedCaseCount,
    int RequiredAcceptedCaseCount,
    IReadOnlyDictionary<string, object?> Thresholds);

internal sealed record CaseResult(
    string Id,
    string Operator,
    string Scenario,
    bool Passed,
    double RuntimeMs,
    long MemoryAllocationBytes,
    bool Accepted,
    double ReprojectionRmsPx,
    double MaxReprojectionErrorPx,
    int DetectedImageCount,
    int TotalImages,
    string? FailureReasonCode,
    string? Error,
    IReadOnlyDictionary<string, object?> Metrics);

internal static class MarkdownReport
{
    public static string Create(BaselineResult result)
    {
        var lines = new List<string>
        {
            "# OpenCV Calibration Dataset Baseline",
            "",
            $"GeneratedAtUtc: `{result.Summary.GeneratedAtUtc:O}`",
            $"Dataset: `{result.Summary.Dataset}`",
            "",
            "## Summary",
            "",
            $"Passed: `{result.Summary.Passed}/{result.Summary.CaseCount}`",
            $"Failed: `{result.Summary.Failed}`",
            $"Accepted: `{result.Summary.Accepted}`",
            $"AcceptedCaseCount: `{result.Summary.AcceptedCaseCount}/{result.Summary.CaseCount}`",
            $"RequireAcceptedCaseCount: `{result.Summary.RequiredAcceptedCaseCount}`",
            $"DetectedImageCount: `{result.Summary.DetectedImageCount}/{result.Summary.TotalImages}`",
            $"WorstReprojectionRmsPx: `{result.Summary.ReprojectionRmsPx:F6}`",
            $"MaxReprojectionErrorPx: `{result.Summary.MaxReprojectionErrorPx:F6}`",
            $"RuntimeMs: `{result.Summary.RuntimeMs:F3}`",
            $"Thresholds: `{JsonSerializer.Serialize(result.Summary.Thresholds, JsonSettings.Compact)}`",
            "",
            "## Cases",
            "",
            "| Id | Operator | Passed | Accepted | Samples | RMS px | Max px | Runtime ms | Failure reason | Error |",
            "|---|---|---:|---:|---:|---:|---:|---:|---|---|"
        };

        foreach (var item in result.Cases)
        {
            lines.Add(
                $"| {item.Id} | {item.Operator} | {item.Passed} | {item.Accepted} | {item.DetectedImageCount}/{item.TotalImages} | {item.ReprojectionRmsPx:F6} | {item.MaxReprojectionErrorPx:F6} | {item.RuntimeMs:F3} | {item.FailureReasonCode ?? ""} | {item.Error ?? ""} |");
        }

        lines.Add("");
        lines.Add("## Stereo Metadata");
        lines.Add("");
        var stereo = result.Cases.FirstOrDefault(item => item.Id == "opencv_calibration_stereo_metadata");
        if (stereo is not null)
        {
            lines.Add($"ExpectedPairsFromManifest: `{GetMetric(stereo, "ExpectedPairsFromManifest")}`");
            lines.Add($"ValidPairs: `{stereo.DetectedImageCount}/{stereo.TotalImages}`");
            lines.Add($"UniquePairIndexCount: `{GetMetric(stereo, "UniquePairIndexCount")}`");
            lines.Add($"CalibrationFiles: `{JsonSerializer.Serialize(GetMetric(stereo, "CalibrationFiles"), JsonSettings.Compact)}`");
        }

        lines.Add("");
        return string.Join(Environment.NewLine, lines);
    }

    private static object? GetMetric(CaseResult result, string key)
    {
        return result.Metrics.TryGetValue(key, out var value) ? value : null;
    }
}

internal sealed class RunnerOptions
{
    public string IndexPath { get; init; } = "quality/datasets/opencv_calibration_samples_index.json";
    public string OutputPath { get; init; } = "quality/evals/reports/CameraCalibration_opencv_samples_baseline.json";
    public string ReportPath { get; init; } = "quality/evals/reports/CameraCalibration_opencv_samples_baseline.md";
    public int MinDetectedImages { get; init; } = 10;
    public double MaxReprojectionRmsPx { get; init; } = 1.0;
    public int MinStereoPairs { get; init; } = 10;
    public double MaxStereoReprojectionRmsPx { get; init; } = 1.0;
    public double MaxEpipolarErrorPx { get; init; } = 2.5;
    public IReadOnlySet<string> CaseIds { get; init; } = new HashSet<string>(StringComparer.Ordinal);
    public string CandidateVersion { get; init; } = "v1";
    public string Profile { get; init; } = "camera_calibration";
    public bool RequireAccepted { get; init; }
    public bool ShowHelp { get; init; }
    public string? ParseError { get; init; }

    public static RunnerOptions Parse(string[] args)
    {
        var options = new MutableOptions();
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            switch (arg)
            {
                case "-h":
                case "--help":
                    options.ShowHelp = true;
                    break;
                case "--index":
                    options.IndexPath = RequireValue(args, ref i, arg);
                    break;
                case "--output":
                    options.OutputPath = RequireValue(args, ref i, arg);
                    break;
                case "--report":
                    options.ReportPath = RequireValue(args, ref i, arg);
                    break;
                case "--min-detected-images":
                    options.MinDetectedImages = int.Parse(RequireValue(args, ref i, arg));
                    break;
                case "--max-reprojection-rms":
                    options.MaxReprojectionRmsPx = double.Parse(RequireValue(args, ref i, arg));
                    break;
                case "--min-stereo-pairs":
                    options.MinStereoPairs = int.Parse(RequireValue(args, ref i, arg));
                    break;
                case "--max-stereo-reprojection-rms":
                    options.MaxStereoReprojectionRmsPx = double.Parse(RequireValue(args, ref i, arg));
                    break;
                case "--max-epipolar-error":
                    options.MaxEpipolarErrorPx = double.Parse(RequireValue(args, ref i, arg));
                    break;
                case "--case-ids":
                    options.CaseIds = ParseCaseIds(RequireValue(args, ref i, arg));
                    break;
                case "--candidate-version":
                    options.CandidateVersion = RequireValue(args, ref i, arg);
                    break;
                case "--profile":
                    options.Profile = RequireValue(args, ref i, arg);
                    break;
                case "--allow-rejected":
                    options.RequireAccepted = false;
                    break;
                case "--require-accepted":
                    options.RequireAccepted = true;
                    break;
                default:
                    return new RunnerOptions { ShowHelp = true, ParseError = $"Unknown argument: {arg}" };
            }
        }

        return options.ToImmutable();
    }

    public static void PrintHelp()
    {
        Console.WriteLine(
            "Usage: OpenCvCalibrationDatasetRunner --index <index.json> --output <baseline.json> --report <report.md> [--case-ids id1,id2,id3] [--candidate-version v1] [--profile camera_calibration] [--min-detected-images N] [--max-reprojection-rms PX] [--min-stereo-pairs N] [--max-stereo-reprojection-rms PX] [--max-epipolar-error PX] [--require-accepted]"
        );
    }

    public IReadOnlyDictionary<string, object?> CreateThresholdMetrics()
    {
        return new Dictionary<string, object?>
        {
            ["RequireAccepted"] = RequireAccepted,
            ["MinDetectedImages"] = MinDetectedImages,
            ["MaxReprojectionRmsPx"] = MaxReprojectionRmsPx,
            ["MinStereoPairs"] = MinStereoPairs,
            ["MaxStereoReprojectionRmsPx"] = MaxStereoReprojectionRmsPx,
            ["MaxEpipolarErrorPx"] = MaxEpipolarErrorPx,
            ["CandidateVersion"] = CandidateVersion,
            ["Profile"] = Profile,
        };
    }

    private static HashSet<string> ParseCaseIds(string value)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in value.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var normalized = item.Trim();
            if (!string.IsNullOrWhiteSpace(normalized))
            {
                result.Add(normalized);
            }
        }

        return result;
    }

    private static string RequireValue(string[] args, ref int index, string name)
    {
        if (index + 1 >= args.Length)
        {
            throw new ArgumentException($"{name} requires a value.");
        }

        index++;
        return args[index];
    }

    private sealed class MutableOptions
    {
        public string IndexPath { get; set; } = "quality/datasets/opencv_calibration_samples_index.json";
        public string OutputPath { get; set; } = "quality/evals/reports/CameraCalibration_opencv_samples_baseline.json";
        public string ReportPath { get; set; } = "quality/evals/reports/CameraCalibration_opencv_samples_baseline.md";
        public int MinDetectedImages { get; set; } = 10;
        public double MaxReprojectionRmsPx { get; set; } = 1.0;
        public int MinStereoPairs { get; set; } = 10;
        public double MaxStereoReprojectionRmsPx { get; set; } = 1.0;
        public double MaxEpipolarErrorPx { get; set; } = 2.5;
        public HashSet<string> CaseIds { get; set; } = new();
        public string CandidateVersion { get; set; } = "v1";
        public string Profile { get; set; } = "camera_calibration";
        public bool RequireAccepted { get; set; }
        public bool ShowHelp { get; set; }

        public RunnerOptions ToImmutable()
        {
            return new RunnerOptions
            {
                IndexPath = IndexPath,
                OutputPath = OutputPath,
                ReportPath = ReportPath,
                MinDetectedImages = MinDetectedImages,
                MaxReprojectionRmsPx = MaxReprojectionRmsPx,
                MinStereoPairs = MinStereoPairs,
                MaxStereoReprojectionRmsPx = MaxStereoReprojectionRmsPx,
                MaxEpipolarErrorPx = MaxEpipolarErrorPx,
                RequireAccepted = RequireAccepted,
                CaseIds = new HashSet<string>(CaseIds, StringComparer.Ordinal),
                CandidateVersion = CandidateVersion,
                Profile = Profile,
                ShowHelp = ShowHelp
            };
        }

    }
}

internal static class JsonSettings
{
    public static readonly JsonSerializerOptions Indented = new()
    {
        WriteIndented = true
    };

    public static readonly JsonSerializerOptions CamelCase = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public static readonly JsonSerializerOptions Compact = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
}
