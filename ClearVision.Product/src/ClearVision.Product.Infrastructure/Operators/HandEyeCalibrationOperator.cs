using System.Numerics;
using ClearVision.Product.Core.Attributes;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Operators;
using ClearVision.Product.Infrastructure.Calibration;
using Microsoft.Extensions.Logging;
using OpenCvSharp;

namespace ClearVision.Product.Infrastructure.Operators;

[OperatorMeta(
    DisplayName = "手眼标定",
    Description = "根据机器人位姿和标定板位姿求解手眼或简化眼在手外标定。",
    CategoryId = OperatorCategoryId.CalibrationAndCoordinates,
    IconName = "hand-eye-calibration",
    Keywords = new[] { "handeye", "robot", "calibration", "AX=XB" },
    Version = "1.1.0"
)]
[AlgorithmInfo(
    Name = "OpenCV Hand-Eye Calibration",
    CoreApi = "OpenCvSharp.Cv2.CalibrateHandEye",
    TimeComplexity = "O(N)",
    SpaceComplexity = "O(N)",
    Dependencies = new[] { "OpenCvSharp" }
)]
[InputPort("RobotPoses", "Robot Poses", PortDataType.Any, IsRequired = true)]
[InputPort("CalibrationBoardPoses", "Calibration Board Poses", PortDataType.Any, IsRequired = true)]
[OutputPort("CalibrationData", "Calibration Data", PortDataType.String)]
[OutputPort("ReprojectionError", "Reprojection Error", PortDataType.Float)]
[OutputPort("CalibrationQuality", "Calibration Quality", PortDataType.String)]
[OutputPort("MatrixConvention", "Matrix Convention", PortDataType.String)]
[OutputPort("HtmlReport", "HTML Report", PortDataType.String)]
[OutputPort("Suggestions", "Suggestions", PortDataType.Any)]
[OutputPort("SuggestedValidationPoses", "Suggested Validation Poses", PortDataType.String)]
[OutputPort("CalibrationAssetId", "Calibration Asset Id", PortDataType.String)]
[OutputPort("CalibrationAssetCandidate", "Calibration Asset Candidate", PortDataType.Boolean)]
[OutputPort("CalibrationContentHash", "Calibration Content Hash", PortDataType.String)]
[OperatorParam("CalibrationType", "Calibration Type", "enum", DefaultValue = "eye_in_hand", Options = new[] { "eye_in_hand|Eye In Hand", "eye_to_hand|Eye To Hand" })]
[OperatorParam("Method", "Method", "enum", DefaultValue = "TSAI", Options = new[] { "TSAI|Tsai", "PARK|Park", "HORAUD|Horaud", "ANDREFF|Andreff", "DANIILIDIS|Daniilidis" })]
[OperatorParam("CameraMatrix", "Camera Matrix", "string", DefaultValue = "")]
[OperatorParam("DistortionCoeffs", "Distortion Coeffs", "string", DefaultValue = "")]
[OperatorParam("CalibrationAssetId", "Calibration Asset Id", "string", DefaultValue = "")]
public sealed class HandEyeCalibrationOperator : OperatorBase
{
    public HandEyeCalibrationOperator(ILogger<HandEyeCalibrationOperator> logger)
        : base(logger)
    {
    }

    public override OperatorType OperatorType => OperatorType.HandEyeCalibration;

    protected override async Task<OperatorExecutionOutput> ExecuteCoreAsync(
        Operator @operator,
        Dictionary<string, object>? inputs,
        CancellationToken cancellationToken)
    {
        if (!TryResolveInputs(inputs, out var robotPoses, out var boardPoses, out var inputError))
        {
            return OperatorExecutionOutput.Failure(inputError);
        }

        var calibrationType = ParseCalibrationType(@operator);
        RobotHandEyeCalibrationResult result;
        try
        {
            result = await RunCpuBoundWork(
                () => HandEyeCalibrationSolver.Solve(robotPoses, boardPoses, calibrationType, ParseMethod(@operator)),
                cancellationToken);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Hand-eye calibration failed.");
            return OperatorExecutionOutput.Failure($"Hand-eye calibration failed: {ex.Message}");
        }

        if (!result.Success)
        {
            return OperatorExecutionOutput.Failure(result.ErrorMessage ?? "Hand-eye calibration failed.");
        }

        var calibrationData = CalibrationBundleV2Json.Serialize(CreateCalibrationBundle(result, robotPoses.Count, calibrationType));
        var output = new Dictionary<string, object>
        {
            ["CalibrationData"] = calibrationData,
            ["ReprojectionError"] = result.Validation.MeanError,
            ["CalibrationQuality"] = result.Validation.Quality,
            ["MatrixConvention"] = result.MatrixConvention,
            ["HtmlReport"] = result.Validation.HtmlReport,
            ["Suggestions"] = result.Validation.Suggestions,
            ["SuggestedValidationPoses"] = result.Validation.SuggestedValidationPosesJson
        };
        CalibrationAssetCandidateOutput.AddTo(
            output,
            GetStringParam(@operator, "CalibrationAssetId", string.Empty),
            calibrationData);

        return OperatorExecutionOutput.Success(output);
    }

    public override ValidationResult ValidateParameters(Operator @operator)
    {
        try
        {
            _ = ParseCalibrationType(@operator);
            _ = ParseMethod(@operator);
            return ValidationResult.Valid();
        }
        catch (Exception ex)
        {
            return ValidationResult.Invalid(ex.Message);
        }
    }

    internal static bool TryResolveInputs(
        Dictionary<string, object>? inputs,
        out List<Matrix4x4> robotPoses,
        out List<Matrix4x4> boardPoses,
        out string error)
    {
        robotPoses = new List<Matrix4x4>();
        boardPoses = new List<Matrix4x4>();
        error = string.Empty;

        if (inputs == null)
        {
            error = "RobotPoses and CalibrationBoardPoses are required.";
            return false;
        }

        if (!inputs.TryGetValue("RobotPoses", out var robotPoseObj) ||
            !Pose3DSerialization.TryParsePoseList(robotPoseObj, out robotPoses, out error))
        {
            return false;
        }

        if (!inputs.TryGetValue("CalibrationBoardPoses", out var boardPoseObj) ||
            !Pose3DSerialization.TryParsePoseList(boardPoseObj, out boardPoses, out error))
        {
            return false;
        }

        if (robotPoses.Count != boardPoses.Count)
        {
            error = "Robot pose count must match calibration board pose count.";
            return false;
        }

        return true;
    }

    internal static RobotHandEyeCalibrationType ParseCalibrationType(Operator @operator)
    {
        return GetCalibrationType(GetStringParamStatic(@operator, "CalibrationType", "eye_in_hand"));
    }

    internal static HandEyeCalibrationMethod ParseMethod(Operator @operator)
    {
        var raw = GetStringParamStatic(@operator, "Method", "TSAI");
        if (Enum.TryParse<HandEyeCalibrationMethod>(raw, true, out var method))
        {
            return method;
        }

        throw new InvalidOperationException("Method must be TSAI, PARK, HORAUD, ANDREFF or DANIILIDIS.");
    }

    internal static RobotHandEyeCalibrationType GetCalibrationType(string raw)
    {
        return raw.Trim().ToLowerInvariant() switch
        {
            "eye_in_hand" => RobotHandEyeCalibrationType.EyeInHand,
            "eye_to_hand" => RobotHandEyeCalibrationType.EyeToHand,
            _ => throw new InvalidOperationException("CalibrationType must be 'eye_in_hand' or 'eye_to_hand'.")
        };
    }

    private static CalibrationBundleV2 CreateCalibrationBundle(
        RobotHandEyeCalibrationResult result,
        int sampleCount,
        RobotHandEyeCalibrationType calibrationType)
    {
        var accepted = !string.Equals(result.Validation.Quality, "poor", StringComparison.OrdinalIgnoreCase);
        var diagnostics = new List<string>
        {
            $"quality={result.Validation.Quality}",
            $"method={result.Method}",
            $"calibration_type={calibrationType}"
        };
        diagnostics.AddRange(result.Validation.Suggestions.Take(3));

        return new CalibrationBundleV2
        {
            CalibrationKind = CalibrationKindV2.HandEye,
            TransformModel = TransformModelV2.Rigid3D,
            SourceFrame = "camera",
            TargetFrame = calibrationType == RobotHandEyeCalibrationType.EyeInHand ? "tool" : "base",
            Unit = "m",
            Transform3D = new CalibrationTransform3DV2
            {
                Model = TransformModelV2.Rigid3D,
                Matrix = CalibrationBundleV2PoseHelpers.ToJaggedMatrix4x4(result.HandEyeMatrix),
                InverseMatrix = CalibrationBundleV2PoseHelpers.ToJaggedMatrix4x4(result.InverseHandEyeMatrix)
            },
            Quality = new CalibrationQualityV2
            {
                Accepted = accepted,
                MeanError = result.Validation.MeanError,
                MaxError = result.Validation.MaxError,
                InlierCount = sampleCount,
                TotalSampleCount = sampleCount,
                Diagnostics = diagnostics
            },
            ProducerOperator = nameof(HandEyeCalibrationOperator)
        };
    }

    private static string GetStringParamStatic(Operator @operator, string name, string defaultValue)
    {
        var parameter = @operator.Parameters.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
        if (parameter?.Value == null)
        {
            return defaultValue;
        }

        return Convert.ToString(parameter.Value, System.Globalization.CultureInfo.InvariantCulture) ?? defaultValue;
    }
}
