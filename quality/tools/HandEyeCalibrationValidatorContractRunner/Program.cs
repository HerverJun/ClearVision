using System.Diagnostics;
using System.Numerics;
using System.Text.Json;
using Acme.Product.Core.Entities;
using Acme.Product.Core.Enums;
using Acme.Product.Core.Operators;
using Acme.Product.Core.ValueObjects;
using Acme.Product.Infrastructure.Calibration;
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

var result = await ContractRunner.RunAsync();
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(options.OutputPath))!);
await File.WriteAllTextAsync(options.OutputPath, JsonSerializer.Serialize(result, JsonSettings.Indented));

if (!string.IsNullOrWhiteSpace(options.ReportPath))
{
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(options.ReportPath))!);
    await File.WriteAllTextAsync(options.ReportPath, MarkdownReport.Create(result));
}

Console.WriteLine(
    $"HandEyeCalibrationValidator contract baseline complete: {result.Summary.Passed}/{result.Summary.CaseCount} passed, " +
    $"failed={result.Summary.Failed}, output={options.OutputPath}");

return result.Summary.Failed == 0 ? 0 : 1;

internal static class ContractRunner
{
    private const string OperatorName = "HandEyeCalibrationValidator";

    public static async Task<BaselineResult> RunAsync()
    {
        var data = SyntheticHandEyeData.Create();
        var cases = BuildCases(data);
        var results = new List<CaseResult>();

        foreach (var testCase in cases)
        {
            results.Add(await RunCaseAsync(testCase));
        }

        var byOperator = results
            .GroupBy(item => item.Operator)
            .Select(group => new OperatorSummary(
                group.Key,
                group.Count(),
                group.Count(item => item.Passed),
                group.Count(item => !item.Passed),
                Math.Round(group.Average(item => item.RuntimeMs), 3),
                (long)Math.Round(group.Average(item => item.MemoryAllocationBytes))))
            .ToList();

        var byScenario = results
            .GroupBy(item => item.Scenario)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => new ScenarioSummary(
                group.Key,
                group.Count(),
                group.Count(item => item.Passed),
                group.Count(item => !item.Passed),
                Math.Round(group.Average(item => item.RuntimeMs), 3)))
            .ToList();

        return new BaselineResult(
            new BaselineSummary(
                DateTimeOffset.UtcNow,
                results.Count,
                results.Count(item => item.Passed),
                results.Count(item => !item.Passed),
                Math.Round(results.Sum(item => item.RuntimeMs), 3)),
            byOperator,
            byScenario,
            results);
    }

    private static List<ContractCase> BuildCases(SyntheticHandEyeData data)
    {
        var goodEyeInHand = CalibrationJson(data.EyeInHand.ExpectedMatrix, RobotHandEyeCalibrationType.EyeInHand);
        var goodEyeToHand = CalibrationJson(data.EyeToHand.ExpectedMatrix, RobotHandEyeCalibrationType.EyeToHand);
        var customBundle = CalibrationJson(
            data.EyeInHand.ExpectedMatrix,
            RobotHandEyeCalibrationType.EyeInHand,
            sourceFrame: "inspection_camera",
            targetFrame: "robot_tool",
            unit: "m",
            producer: "SyntheticBaseline");
        var poorEyeToHandMatrix = data.EyeToHand.ExpectedMatrix;
        poorEyeToHandMatrix.M41 += 0.030f;
        poorEyeToHandMatrix.M42 -= 0.020f;
        var poorEyeToHand = CalibrationJson(poorEyeToHandMatrix, RobotHandEyeCalibrationType.EyeToHand);

        var cases = new List<ContractCase>
        {
            SuccessCase("eye_in_hand_good_matrix", "Good validation", data.EyeInHand, "eye_in_hand", goodEyeInHand, "good", maxMeanError: 0.0005),
            SuccessCase("eye_to_hand_good_matrix", "Good validation", data.EyeToHand, "eye_to_hand", goodEyeToHand, "good", maxMeanError: 0.0005),
            SuccessCase("eye_in_hand_json_pose_inputs", "Input parsing", data.EyeInHand.AsJsonPoseInputs(), "eye_in_hand", goodEyeInHand, "good", maxMeanError: 0.0005),
            SuccessCase("eye_to_hand_json_pose_inputs", "Input parsing", data.EyeToHand.AsJsonPoseInputs(), "eye_to_hand", goodEyeToHand, "good", maxMeanError: 0.0005),
            SuccessCase("custom_bundle_metadata_preserved", "Output bundle contract", data.EyeInHand, "eye_in_hand", customBundle, "good", maxMeanError: 0.0005, expectedSourceFrame: "inspection_camera", expectedTargetFrame: "robot_tool"),
            SuccessCase("html_report_contains_quality", "Report contract", data.EyeInHand, "eye_in_hand", goodEyeInHand, "good", maxMeanError: 0.0005, requireHtmlReport: true),
            SuccessCase("suggested_validation_poses_parseable", "Report contract", data.EyeInHand, "eye_in_hand", goodEyeInHand, "good", maxMeanError: 0.0005, requireSuggestedPoses: true),
            SuccessCase("good_quality_has_operational_suggestion", "Suggestion contract", data.EyeToHand, "eye_to_hand", goodEyeToHand, "good", maxMeanError: 0.0005, expectedSuggestionContains: "质量良好"),
            SuccessCase("low_sample_count_adds_suggestion", "Suggestion contract", data.EyeInHand.Take(5), "eye_in_hand", goodEyeInHand, "good", maxMeanError: 0.0005, expectedSuggestionContains: "增加采样姿态"),
            SuccessCase("perturbed_eye_to_hand_matrix_is_poor", "Perturbation contract", data.EyeToHand, "eye_to_hand", poorEyeToHand, "poor", minMeanError: 0.005, expectAccepted: false),
            SuccessCase("perturbed_bundle_marks_quality_rejected", "Output bundle contract", data.EyeToHand, "eye_to_hand", poorEyeToHand, "poor", minMeanError: 0.005, expectAccepted: false),
            SuccessCase("eye_in_hand_case_insensitive_type", "Parameter parsing", data.EyeInHand, "EYE_IN_HAND", goodEyeInHand, "good", maxMeanError: 0.0005),
            FailureCase("missing_calibration_data_fails", "Failure contract", data.EyeInHand, "eye_in_hand", null, expectedErrorContains: "CalibrationData"),
            FailureCase("invalid_calibration_json_fails", "Failure contract", data.EyeInHand, "eye_in_hand", "{", expectedErrorContains: "Invalid CalibrationData"),
            FailureCase("wrong_calibration_kind_fails", "Failure contract", data.EyeInHand, "eye_in_hand", WrongKindJson(), expectedErrorContains: "not HandEye"),
            FailureCase("missing_transform3d_fails", "Failure contract", data.EyeInHand, "eye_in_hand", MissingTransformJson(), expectedErrorContains: "Transform3D"),
            FailureCase("invalid_matrix_shape_fails", "Failure contract", data.EyeInHand, "eye_in_hand", InvalidMatrixJson(), expectedErrorContains: "4x4"),
            FailureCase("missing_robot_poses_fails", "Failure contract", data.EyeInHand, "eye_in_hand", goodEyeInHand, omitRobotPoses: true),
            FailureCase("missing_board_poses_fails", "Failure contract", data.EyeInHand, "eye_in_hand", goodEyeInHand, omitBoardPoses: true),
            FailureCase("pose_count_mismatch_fails", "Failure contract", data.EyeInHand.WithBoardCount(4), "eye_in_hand", goodEyeInHand, expectedErrorContains: "count"),
            ValidationCase("validate_eye_in_hand_valid", "eye_in_hand", expectedValid: true),
            ValidationCase("validate_eye_to_hand_valid", "eye_to_hand", expectedValid: true),
            ValidationCase("validate_bad_type_invalid", "eye_on_elbow", expectedValid: false),
            ValidationCase("validate_trimmed_type_valid", " eye_to_hand ", expectedValid: true)
        };

        return cases;
    }

    private static ContractCase SuccessCase(
        string caseId,
        string scenario,
        PoseDataset dataset,
        string calibrationType,
        string calibrationData,
        string expectedQuality,
        double? maxMeanError = null,
        double? minMeanError = null,
        bool? expectAccepted = true,
        string? expectedSourceFrame = null,
        string? expectedTargetFrame = null,
        bool requireHtmlReport = false,
        bool requireSuggestedPoses = false,
        string? expectedSuggestionContains = null)
    {
        return new ContractCase(caseId, OperatorName, scenario, async () =>
        {
            var op = CreateOperator(calibrationType);
            var inputs = CreateInputs(dataset, calibrationData);
            var executor = new HandEyeCalibrationValidatorOperator(NullLogger<HandEyeCalibrationValidatorOperator>.Instance);

            var stopwatch = Stopwatch.StartNew();
            var allocationBefore = GC.GetTotalAllocatedBytes(precise: true);
            var execution = await executor.ExecuteAsync(op, inputs);
            var allocationAfter = GC.GetTotalAllocatedBytes(precise: true);
            stopwatch.Stop();

            var metrics = EvaluateSuccess(
                execution,
                expectedQuality,
                maxMeanError,
                minMeanError,
                expectAccepted,
                expectedSourceFrame,
                expectedTargetFrame,
                requireHtmlReport,
                requireSuggestedPoses,
                expectedSuggestionContains);
            var passed = BoolMetric(metrics, "Passed");
            return new CaseRunResult(
                passed,
                Math.Round(stopwatch.Elapsed.TotalMilliseconds, 3),
                Math.Max(0, allocationAfter - allocationBefore),
                passed ? null : FormatFailure(execution, metrics),
                metrics);
        });
    }

    private static ContractCase FailureCase(
        string caseId,
        string scenario,
        PoseDataset dataset,
        string calibrationType,
        string? calibrationData,
        bool omitRobotPoses = false,
        bool omitBoardPoses = false,
        string? expectedErrorContains = null)
    {
        return new ContractCase(caseId, OperatorName, scenario, async () =>
        {
            var op = CreateOperator(calibrationType);
            var inputs = CreateInputs(dataset, calibrationData, omitRobotPoses, omitBoardPoses);
            var executor = new HandEyeCalibrationValidatorOperator(NullLogger<HandEyeCalibrationValidatorOperator>.Instance);

            var stopwatch = Stopwatch.StartNew();
            var allocationBefore = GC.GetTotalAllocatedBytes(precise: true);
            var execution = await executor.ExecuteAsync(op, inputs);
            var allocationAfter = GC.GetTotalAllocatedBytes(precise: true);
            stopwatch.Stop();

            var errorCorrect = !execution.IsSuccess &&
                (string.IsNullOrWhiteSpace(expectedErrorContains) ||
                 (execution.ErrorMessage ?? string.Empty).Contains(expectedErrorContains, StringComparison.OrdinalIgnoreCase));
            var metrics = new Dictionary<string, object>
            {
                ["ExpectedFailure"] = true,
                ["ActualSuccess"] = execution.IsSuccess,
                ["ErrorMessage"] = execution.ErrorMessage ?? string.Empty,
                ["ErrorCorrect"] = errorCorrect,
                ["Passed"] = errorCorrect
            };

            return new CaseRunResult(
                errorCorrect,
                Math.Round(stopwatch.Elapsed.TotalMilliseconds, 3),
                Math.Max(0, allocationAfter - allocationBefore),
                errorCorrect ? null : $"Expected failure containing '{expectedErrorContains}', got success={execution.IsSuccess}, error={execution.ErrorMessage}",
                metrics);
        });
    }

    private static ContractCase ValidationCase(string caseId, string calibrationType, bool expectedValid)
    {
        return new ContractCase(caseId, OperatorName, "Validation contract", () =>
        {
            var op = CreateOperator(calibrationType);
            var executor = new HandEyeCalibrationValidatorOperator(NullLogger<HandEyeCalibrationValidatorOperator>.Instance);
            var stopwatch = Stopwatch.StartNew();
            var allocationBefore = GC.GetTotalAllocatedBytes(precise: true);
            var validation = executor.ValidateParameters(op);
            var allocationAfter = GC.GetTotalAllocatedBytes(precise: true);
            stopwatch.Stop();

            var passed = validation.IsValid == expectedValid;
            var metrics = new Dictionary<string, object>
            {
                ["ExpectedValid"] = expectedValid,
                ["ActualValid"] = validation.IsValid,
                ["ErrorMessage"] = string.Join("; ", validation.Errors),
                ["Passed"] = passed
            };

            return Task.FromResult(new CaseRunResult(
                passed,
                Math.Round(stopwatch.Elapsed.TotalMilliseconds, 3),
                Math.Max(0, allocationAfter - allocationBefore),
                passed ? null : $"Expected validation={expectedValid}, got {validation.IsValid}",
                metrics));
        });
    }

    private static async Task<CaseResult> RunCaseAsync(ContractCase testCase)
    {
        try
        {
            var run = await testCase.RunAsync();
            return new CaseResult(
                testCase.CaseId,
                testCase.Operator,
                testCase.Scenario,
                run.Passed,
                run.RuntimeMs,
                run.MemoryAllocationBytes,
                run.ErrorMessage,
                run.Metrics);
        }
        catch (Exception ex)
        {
            return new CaseResult(
                testCase.CaseId,
                testCase.Operator,
                testCase.Scenario,
                false,
                0,
                0,
                $"{ex.GetType().Name}: {ex.Message}",
                new Dictionary<string, object>());
        }
    }

    private static Dictionary<string, object> EvaluateSuccess(
        OperatorExecutionOutput execution,
        string expectedQuality,
        double? maxMeanError,
        double? minMeanError,
        bool? expectAccepted,
        string? expectedSourceFrame,
        string? expectedTargetFrame,
        bool requireHtmlReport,
        bool requireSuggestedPoses,
        string? expectedSuggestionContains)
    {
        var metrics = new Dictionary<string, object>
        {
            ["ActualSuccess"] = execution.IsSuccess,
            ["QualityCorrect"] = false,
            ["MeanError"] = double.NaN,
            ["MeanErrorCorrect"] = false,
            ["MaxErrorFinite"] = false,
            ["RotationErrorFinite"] = false,
            ["HtmlReportCorrect"] = !requireHtmlReport,
            ["SuggestedPosesCorrect"] = !requireSuggestedPoses,
            ["SuggestionCorrect"] = expectedSuggestionContains is null,
            ["OutputBundleCorrect"] = false,
            ["AcceptedCorrect"] = expectAccepted is null,
            ["FrameMetadataCorrect"] = expectedSourceFrame is null && expectedTargetFrame is null,
            ["Passed"] = false
        };

        if (!execution.IsSuccess || execution.OutputData is null)
        {
            return metrics;
        }

        var output = execution.OutputData;
        var quality = output.TryGetValue("Quality", out var qualityObj) ? qualityObj?.ToString() ?? string.Empty : string.Empty;
        var meanError = TryGetDouble(output, "MeanError", out var mean) ? mean : double.NaN;
        var maxError = TryGetDouble(output, "MaxError", out var max) ? max : double.NaN;
        var rotationError = TryGetDouble(output, "MeanRotationError", out var rot) ? rot : double.NaN;
        var html = output.TryGetValue("HtmlReport", out var htmlObj) ? htmlObj?.ToString() ?? string.Empty : string.Empty;
        var poses = output.TryGetValue("SuggestedValidationPoses", out var posesObj) ? posesObj?.ToString() ?? string.Empty : string.Empty;
        var suggestions = output.TryGetValue("Suggestions", out var suggestionsObj) && suggestionsObj is IEnumerable<string> typed
            ? typed.ToArray()
            : output.TryGetValue("Suggestions", out suggestionsObj) && suggestionsObj is IEnumerable<object> objects
                ? objects.Select(x => x?.ToString() ?? string.Empty).ToArray()
                : Array.Empty<string>();

        var meanErrorCorrect = true;
        if (maxMeanError.HasValue)
        {
            meanErrorCorrect &= meanError <= maxMeanError.Value;
        }

        if (minMeanError.HasValue)
        {
            meanErrorCorrect &= meanError >= minMeanError.Value;
        }

        var htmlCorrect = !requireHtmlReport || (html.Contains("Hand-Eye Calibration Validation Report", StringComparison.OrdinalIgnoreCase) && html.Contains(expectedQuality, StringComparison.OrdinalIgnoreCase));
        var suggestedPosesCorrect = !requireSuggestedPoses || IsJsonArray(poses);
        var suggestionCorrect = expectedSuggestionContains is null || suggestions.Any(item => item.Contains(expectedSuggestionContains, StringComparison.OrdinalIgnoreCase));
        var outputBundleCorrect = false;
        var acceptedCorrect = expectAccepted is null;
        var frameMetadataCorrect = expectedSourceFrame is null && expectedTargetFrame is null;

        if (output.TryGetValue("CalibrationData", out var calibrationObj) &&
            calibrationObj is string calibrationData &&
            CalibrationBundleV2Json.TryDeserialize(calibrationData, out var bundle, out _))
        {
            outputBundleCorrect =
                bundle.CalibrationKind == CalibrationKindV2.HandEye &&
                bundle.TransformModel == TransformModelV2.Rigid3D &&
                bundle.Transform3D != null &&
                bundle.ProducerOperator == nameof(HandEyeCalibrationValidatorOperator);
            acceptedCorrect = expectAccepted is null || bundle.Quality.Accepted == expectAccepted.Value;
            frameMetadataCorrect =
                (expectedSourceFrame is null || bundle.SourceFrame == expectedSourceFrame) &&
                (expectedTargetFrame is null || bundle.TargetFrame == expectedTargetFrame);
        }

        var qualityCorrect = string.Equals(quality, expectedQuality, StringComparison.OrdinalIgnoreCase);
        var maxErrorFinite = double.IsFinite(maxError);
        var rotationErrorFinite = double.IsFinite(rotationError);
        var passed =
            qualityCorrect &&
            double.IsFinite(meanError) &&
            meanErrorCorrect &&
            maxErrorFinite &&
            rotationErrorFinite &&
            htmlCorrect &&
            suggestedPosesCorrect &&
            suggestionCorrect &&
            outputBundleCorrect &&
            acceptedCorrect &&
            frameMetadataCorrect;

        metrics["Quality"] = quality;
        metrics["QualityCorrect"] = qualityCorrect;
        metrics["MeanError"] = Round(meanError);
        metrics["MeanErrorCorrect"] = meanErrorCorrect;
        metrics["MaxError"] = Round(maxError);
        metrics["MaxErrorFinite"] = maxErrorFinite;
        metrics["MeanRotationError"] = Round(rotationError);
        metrics["RotationErrorFinite"] = rotationErrorFinite;
        metrics["HtmlReportCorrect"] = htmlCorrect;
        metrics["SuggestedPosesCorrect"] = suggestedPosesCorrect;
        metrics["SuggestionCorrect"] = suggestionCorrect;
        metrics["OutputBundleCorrect"] = outputBundleCorrect;
        metrics["AcceptedCorrect"] = acceptedCorrect;
        metrics["FrameMetadataCorrect"] = frameMetadataCorrect;
        metrics["Passed"] = passed;
        return metrics;
    }

    private static Operator CreateOperator(string calibrationType)
    {
        var op = new Operator("handeye_validator_contract", OperatorType.HandEyeCalibrationValidator, 0, 0);
        op.AddParameter(new Parameter(Guid.NewGuid(), "CalibrationType", "CalibrationType", string.Empty, "string", calibrationType));
        return op;
    }

    private static Dictionary<string, object> CreateInputs(
        PoseDataset dataset,
        string? calibrationData,
        bool omitRobotPoses = false,
        bool omitBoardPoses = false)
    {
        var inputs = new Dictionary<string, object>();
        if (!omitRobotPoses)
        {
            inputs["RobotPoses"] = dataset.RobotPosesInput;
        }

        if (!omitBoardPoses)
        {
            inputs["CalibrationBoardPoses"] = dataset.BoardPosesInput;
        }

        if (calibrationData is not null)
        {
            inputs["CalibrationData"] = calibrationData;
        }

        return inputs;
    }

    private static string CalibrationJson(
        Matrix4x4 matrix,
        RobotHandEyeCalibrationType type,
        string sourceFrame = "camera",
        string? targetFrame = null,
        string unit = "m",
        string producer = "SyntheticHandEyeBaseline")
    {
        Matrix4x4.Invert(matrix, out var inverse);
        var bundle = new CalibrationBundleV2
        {
            CalibrationKind = CalibrationKindV2.HandEye,
            TransformModel = TransformModelV2.Rigid3D,
            SourceFrame = sourceFrame,
            TargetFrame = targetFrame ?? (type == RobotHandEyeCalibrationType.EyeInHand ? "tool" : "base"),
            Unit = unit,
            Transform3D = new CalibrationTransform3DV2
            {
                Model = TransformModelV2.Rigid3D,
                Matrix = CalibrationBundleV2PoseHelpers.ToJaggedMatrix4x4(matrix),
                InverseMatrix = CalibrationBundleV2PoseHelpers.ToJaggedMatrix4x4(inverse)
            },
            Quality = new CalibrationQualityV2
            {
                Accepted = true,
                InlierCount = 9,
                TotalSampleCount = 9
            },
            ProducerOperator = producer
        };
        return CalibrationBundleV2Json.Serialize(bundle);
    }

    private static string WrongKindJson()
    {
        var bundle = new CalibrationBundleV2
        {
            CalibrationKind = CalibrationKindV2.CameraIntrinsics,
            TransformModel = TransformModelV2.Rigid3D,
            SourceFrame = "camera",
            TargetFrame = "tool",
            Transform3D = new CalibrationTransform3DV2
            {
                Matrix = CalibrationBundleV2PoseHelpers.ToJaggedMatrix4x4(Matrix4x4.Identity)
            },
            Quality = new CalibrationQualityV2 { Accepted = true }
        };
        return CalibrationBundleV2Json.Serialize(bundle);
    }

    private static string MissingTransformJson()
    {
        return CalibrationBundleV2Json.Serialize(new CalibrationBundleV2
        {
            CalibrationKind = CalibrationKindV2.HandEye,
            TransformModel = TransformModelV2.Rigid3D,
            SourceFrame = "camera",
            TargetFrame = "tool",
            Quality = new CalibrationQualityV2 { Accepted = true }
        });
    }

    private static string InvalidMatrixJson()
    {
        return CalibrationBundleV2Json.Serialize(new CalibrationBundleV2
        {
            CalibrationKind = CalibrationKindV2.HandEye,
            TransformModel = TransformModelV2.Rigid3D,
            SourceFrame = "camera",
            TargetFrame = "tool",
            Transform3D = new CalibrationTransform3DV2
            {
                Matrix = [new[] { 1.0 }]
            },
            Quality = new CalibrationQualityV2 { Accepted = true }
        });
    }

    private static bool TryGetDouble(IReadOnlyDictionary<string, object> output, string key, out double value)
    {
        value = 0;
        if (!output.TryGetValue(key, out var obj))
        {
            return false;
        }

        try
        {
            value = Convert.ToDouble(obj);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool BoolMetric(IReadOnlyDictionary<string, object> metrics, string key) =>
        metrics.TryGetValue(key, out var value) && value is bool b && b;

    private static bool IsJsonArray(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind == JsonValueKind.Array && document.RootElement.GetArrayLength() > 0;
        }
        catch
        {
            return false;
        }
    }

    private static double Round(double value) => double.IsFinite(value) ? Math.Round(value, 8) : value;

    private static string FormatFailure(OperatorExecutionOutput? execution, IReadOnlyDictionary<string, object> metrics)
    {
        if (execution is not null && !execution.IsSuccess)
        {
            return execution.ErrorMessage ?? "Execution failed.";
        }

        var keys = new[]
        {
            "QualityCorrect",
            "MeanError",
            "MeanErrorCorrect",
            "MaxErrorFinite",
            "RotationErrorFinite",
            "HtmlReportCorrect",
            "SuggestedPosesCorrect",
            "SuggestionCorrect",
            "OutputBundleCorrect",
            "AcceptedCorrect",
            "FrameMetadataCorrect"
        };
        return string.Join(", ", keys.Where(metrics.ContainsKey).Select(key => $"{key}={metrics[key]}"));
    }
}

internal sealed record PoseDataset(
    IReadOnlyList<Matrix4x4> RobotPoses,
    IReadOnlyList<Matrix4x4> BoardPoses,
    Matrix4x4 ExpectedMatrix,
    object RobotPosesInput,
    object BoardPosesInput)
{
    public PoseDataset Take(int count)
    {
        return this with
        {
            RobotPoses = RobotPoses.Take(count).ToList(),
            BoardPoses = BoardPoses.Take(count).ToList(),
            RobotPosesInput = RobotPoses.Take(count).ToList(),
            BoardPosesInput = BoardPoses.Take(count).ToList()
        };
    }

    public PoseDataset WithBoardCount(int count)
    {
        return this with
        {
            BoardPoses = BoardPoses.Take(count).ToList(),
            BoardPosesInput = BoardPoses.Take(count).ToList()
        };
    }

    public PoseDataset AsJsonPoseInputs()
    {
        return this with
        {
            RobotPosesInput = JsonSerializer.Serialize(RobotPoses.Select(MatrixToJagged).ToArray()),
            BoardPosesInput = JsonSerializer.Serialize(BoardPoses.Select(MatrixToJagged).ToArray())
        };
    }

    private static double[][] MatrixToJagged(Matrix4x4 matrix) =>
    [
        [(double)matrix.M11, matrix.M12, matrix.M13, matrix.M14],
        [(double)matrix.M21, matrix.M22, matrix.M23, matrix.M24],
        [(double)matrix.M31, matrix.M32, matrix.M33, matrix.M34],
        [(double)matrix.M41, matrix.M42, matrix.M43, matrix.M44]
    ];
}

internal sealed record SyntheticHandEyeData(PoseDataset EyeInHand, PoseDataset EyeToHand)
{
    public static SyntheticHandEyeData Create()
    {
        var eyeInHand = CreateSyntheticEyeInHandDataset();
        var eyeToHand = CreateSyntheticEyeToHandDataset();
        return new SyntheticHandEyeData(eyeInHand, eyeToHand);
    }

    private static PoseDataset CreateSyntheticEyeInHandDataset()
    {
        var expectedCameraToTool = CreateTransform(new Vector3(0.030f, -0.015f, 0.080f), 5f, -8f, 12f);
        var targetToBase = CreateTransform(new Vector3(0.450f, 0.120f, 0.250f), 0f, 0f, 0f);
        var inverseCameraToTool = Invert(expectedCameraToTool);

        var robotPoses = new List<Matrix4x4>();
        var boardPoses = new List<Matrix4x4>();

        foreach (var (translation, roll, pitch, yaw) in EyeInHandSamples())
        {
            var baseToTool = CreateTransform(translation, roll, pitch, yaw);
            var targetToCamera = targetToBase * baseToTool * inverseCameraToTool;
            var cameraToTarget = Invert(targetToCamera);

            robotPoses.Add(baseToTool);
            boardPoses.Add(cameraToTarget);
        }

        return new PoseDataset(robotPoses, boardPoses, expectedCameraToTool, robotPoses, boardPoses);
    }

    private static PoseDataset CreateSyntheticEyeToHandDataset()
    {
        var expectedCameraToBase = CreateTransform(new Vector3(-0.220f, 0.080f, 0.550f), -2f, 11f, 18f);
        var targetToTool = CreateTransform(new Vector3(0.012f, -0.018f, 0.040f), 4f, -3f, 7f);
        var baseToCamera = Invert(expectedCameraToBase);

        var robotPoses = new List<Matrix4x4>();
        var boardPoses = new List<Matrix4x4>();

        foreach (var (translation, roll, pitch, yaw) in EyeToHandSamples())
        {
            var baseToTool = CreateTransform(translation, roll, pitch, yaw);
            var toolToBase = Invert(baseToTool);
            var targetToCamera = targetToTool * toolToBase * baseToCamera;
            var cameraToTarget = Invert(targetToCamera);

            robotPoses.Add(baseToTool);
            boardPoses.Add(cameraToTarget);
        }

        return new PoseDataset(robotPoses, boardPoses, expectedCameraToBase, robotPoses, boardPoses);
    }

    private static IReadOnlyList<(Vector3 Translation, float Roll, float Pitch, float Yaw)> EyeInHandSamples() =>
    [
        (new Vector3(0.10f, 0.02f, 0.35f), 0f, 5f, -8f),
        (new Vector3(0.12f, -0.04f, 0.32f), 6f, -4f, 15f),
        (new Vector3(0.08f, 0.06f, 0.38f), -10f, 7f, -12f),
        (new Vector3(0.15f, -0.01f, 0.40f), 8f, 9f, 4f),
        (new Vector3(0.18f, 0.03f, 0.34f), -6f, -7f, 18f),
        (new Vector3(0.11f, -0.06f, 0.36f), 11f, 3f, -16f),
        (new Vector3(0.16f, 0.08f, 0.42f), -12f, 10f, 9f),
        (new Vector3(0.09f, -0.02f, 0.31f), 4f, -9f, 20f),
        (new Vector3(0.14f, 0.01f, 0.39f), -8f, 6f, -5f)
    ];

    private static IReadOnlyList<(Vector3 Translation, float Roll, float Pitch, float Yaw)> EyeToHandSamples() =>
    [
        (new Vector3(0.22f, -0.04f, 0.28f), 3f, 5f, -10f),
        (new Vector3(0.18f, 0.02f, 0.32f), -4f, 12f, 15f),
        (new Vector3(0.25f, 0.06f, 0.30f), 8f, -6f, 20f),
        (new Vector3(0.21f, -0.08f, 0.34f), -9f, 7f, -16f),
        (new Vector3(0.17f, 0.05f, 0.29f), 11f, -10f, 6f),
        (new Vector3(0.24f, -0.01f, 0.36f), -7f, 9f, 12f),
        (new Vector3(0.19f, 0.09f, 0.31f), 5f, -12f, -8f),
        (new Vector3(0.23f, -0.05f, 0.27f), -11f, 4f, 17f),
        (new Vector3(0.16f, 0.00f, 0.35f), 9f, 8f, -14f)
    ];

    private static Matrix4x4 CreateTransform(Vector3 translation, float rollDeg, float pitchDeg, float yawDeg)
    {
        var rotation = Matrix4x4.CreateFromYawPitchRoll(
            DegreesToRadians(yawDeg),
            DegreesToRadians(pitchDeg),
            DegreesToRadians(rollDeg));
        rotation.M41 = translation.X;
        rotation.M42 = translation.Y;
        rotation.M43 = translation.Z;
        rotation.M44 = 1f;
        return rotation;
    }

    private static Matrix4x4 Invert(Matrix4x4 matrix)
    {
        if (!Matrix4x4.Invert(matrix, out var inverted))
        {
            throw new InvalidOperationException("Synthetic transform is not invertible.");
        }

        return inverted;
    }

    private static float DegreesToRadians(float degrees) => degrees * (MathF.PI / 180f);
}

internal sealed record ContractCase(
    string CaseId,
    string Operator,
    string Scenario,
    Func<Task<CaseRunResult>> RunAsync);

internal sealed record CaseRunResult(
    bool Passed,
    double RuntimeMs,
    long MemoryAllocationBytes,
    string? ErrorMessage,
    Dictionary<string, object> Metrics);

internal sealed record BaselineResult(
    BaselineSummary Summary,
    IReadOnlyList<OperatorSummary> Operators,
    IReadOnlyList<ScenarioSummary> Scenarios,
    IReadOnlyList<CaseResult> Cases);

internal sealed record BaselineSummary(DateTimeOffset GeneratedAtUtc, int CaseCount, int Passed, int Failed, double RuntimeMs);

internal sealed record OperatorSummary(string Operator, int CaseCount, int Passed, int Failed, double RuntimeMsAvg, long MemoryAllocationBytesAvg);

internal sealed record ScenarioSummary(string Scenario, int CaseCount, int Passed, int Failed, double RuntimeMsAvg);

internal sealed record CaseResult(
    string CaseId,
    string Operator,
    string Scenario,
    bool Passed,
    double RuntimeMs,
    long MemoryAllocationBytes,
    string? ErrorMessage,
    IReadOnlyDictionary<string, object> Metrics);

internal static class MarkdownReport
{
    public static string Create(BaselineResult result)
    {
        var lines = new List<string>
        {
            "# HandEyeCalibrationValidator Contract Baseline",
            string.Empty,
            $"GeneratedAtUtc: `{result.Summary.GeneratedAtUtc:O}`",
            string.Empty,
            "## Summary",
            string.Empty,
            "| Metric | Value |",
            "| --- | ---: |",
            $"| Cases | {result.Summary.CaseCount} |",
            $"| Passed | {result.Summary.Passed} |",
            $"| Failed | {result.Summary.Failed} |",
            $"| Runtime ms | {result.Summary.RuntimeMs:0.###} |",
            string.Empty,
            "## Operators",
            string.Empty,
            "| Operator | Cases | Passed | Failed | Avg ms | Avg bytes |",
            "| --- | ---: | ---: | ---: | ---: | ---: |"
        };

        foreach (var op in result.Operators)
        {
            lines.Add($"| {op.Operator} | {op.CaseCount} | {op.Passed} | {op.Failed} | {op.RuntimeMsAvg:0.###} | {op.MemoryAllocationBytesAvg} |");
        }

        lines.AddRange(new[]
        {
            string.Empty,
            "## Scenarios",
            string.Empty,
            "| Scenario | Cases | Passed | Failed | Avg ms |",
            "| --- | ---: | ---: | ---: | ---: |"
        });

        foreach (var scenario in result.Scenarios)
        {
            lines.Add($"| {scenario.Scenario} | {scenario.CaseCount} | {scenario.Passed} | {scenario.Failed} | {scenario.RuntimeMsAvg:0.###} |");
        }

        lines.AddRange(new[]
        {
            string.Empty,
            "## Cases",
            string.Empty,
            "| Case | Scenario | Passed | Runtime ms | Quality | Mean Error | Failure |",
            "| --- | --- | --- | ---: | --- | ---: | --- |"
        });

        foreach (var item in result.Cases)
        {
            item.Metrics.TryGetValue("Quality", out var quality);
            item.Metrics.TryGetValue("MeanError", out var mean);
            lines.Add($"| {item.CaseId} | {item.Scenario} | {(item.Passed ? "Yes" : "No")} | {item.RuntimeMs:0.###} | {quality ?? "-"} | {mean ?? "-"} | {item.ErrorMessage ?? "-"} |");
        }

        lines.AddRange(new[]
        {
            string.Empty,
            "## Notes",
            string.Empty,
            "- This baseline uses deterministic synthetic eye-in-hand and eye-to-hand pose sets.",
            "- It validates good, perturbed, malformed input, output bundle, HTML report, suggestion, pose parsing, and parameter validation contracts.",
            "- It is a validator contract baseline; the upstream hand-eye solver has separate unit coverage."
        });

        return string.Join(Environment.NewLine, lines) + Environment.NewLine;
    }
}

internal sealed record RunnerOptions(string OutputPath, string? ReportPath, bool ShowHelp, string? ParseError)
{
    public static RunnerOptions Parse(string[] args)
    {
        var output = "quality/evals/reports/HandEyeCalibrationValidator_contract_baseline.json";
        string? report = "quality/evals/reports/HandEyeCalibrationValidator_contract_baseline.md";
        var showHelp = false;
        string? parseError = null;

        for (var index = 0; index < args.Length; index++)
        {
            var arg = args[index];
            switch (arg)
            {
                case "--output":
                    output = ReadValue(args, ref index, arg, ref parseError) ?? output;
                    break;
                case "--report":
                    report = ReadValue(args, ref index, arg, ref parseError);
                    break;
                case "--no-report":
                    report = null;
                    break;
                case "--help":
                case "-h":
                    showHelp = true;
                    break;
                default:
                    parseError = $"Unknown argument: {arg}";
                    break;
            }
        }

        return new RunnerOptions(output, report, showHelp, parseError);
    }

    public static void PrintHelp()
    {
        Console.WriteLine(
            """
            HandEyeCalibrationValidator contract runner

            Options:
              --output <path>     JSON baseline output path.
              --report <path>     Markdown report output path.
              --no-report         Skip markdown report generation.
            """);
    }

    private static string? ReadValue(string[] args, ref int index, string name, ref string? parseError)
    {
        if (index + 1 >= args.Length)
        {
            parseError = $"{name} requires a value.";
            return null;
        }

        index++;
        return args[index];
    }
}

internal static class JsonSettings
{
    public static readonly JsonSerializerOptions Indented = new()
    {
        WriteIndented = true
    };
}
