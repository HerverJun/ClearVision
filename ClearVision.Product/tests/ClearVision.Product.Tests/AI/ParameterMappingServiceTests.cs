using ClearVision.Product.Core.DTOs;
using ClearVision.Product.Infrastructure.AI.Agent;
using ClearVision.Product.Infrastructure.Services;
using FluentAssertions;

namespace ClearVision.Product.Tests.AI;

[TestClassification(TestDomain.Ai, TestPurpose.Regression, TestLane.Nightly, TestEvidenceType.Contract, TestOracleType.Contract, TestResourceRequirement.None, TestExpectedDuration.Medium, TestFlakyPolicy.Blocking, "vision-agent")]
public sealed class ParameterMappingServiceTests
{
    [Fact]
    public void Map_ClassificationIntent_ShouldSetTaskTypeAndExcludeDetectionOnlyParameters()
    {
        var resolution = Map(
            "DeepLearning",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Confidence"] = "0.99",
                ["TopK"] = "3"
            },
            "classify product appearance into the expected class");

        resolution.ParameterStrategy.Should().Be("deep_learning_classification");
        resolution.Mappings.Should().Contain(mapping =>
            mapping.ParameterName == "TaskType" &&
            mapping.ValueSummary == "ImageClassification");
        resolution.Mappings.Should().Contain(mapping =>
            mapping.ParameterName == "TopK" &&
            mapping.ValueSummary == "3");
        resolution.Mappings.Should().Contain(mapping =>
            mapping.ParameterName == "ChannelOrder" &&
            mapping.ValueSummary == "RGB" &&
            !mapping.Pending);
        resolution.MissingResources.Should().NotContain(resource =>
            resource.ParameterName == "ChannelOrder" ||
            resource.ResourceType == "output_channel");
        resolution.Mappings.Select(mapping => mapping.ParameterName).Should().Contain(
            ["ClassificationInputSize", "ClassificationScoreMode", "ClassNames"]);
        resolution.Mappings.Select(mapping => mapping.ParameterName).Should().NotContain(
            [
                "Confidence", "ModelVersion", "InputSize", "TargetClasses", "EnableInternalNms",
                "NmsIouThreshold", "OutputFormat", "DetectionMode", "SegmentationInputSize",
                "NumClasses", "MaxClassMasks"
            ]);
    }

    [Fact]
    public void Map_SemanticSegmentationIntent_ShouldExcludeDetectionAndClassificationOnlyParameters()
    {
        var resolution = Map(
            "DeepLearning",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            "run semantic segmentation and return a segmentation mask");

        resolution.Mappings.Should().Contain(mapping =>
            mapping.ParameterName == "TaskType" &&
            mapping.ValueSummary == "SemanticSegmentation");
        resolution.Mappings.Select(mapping => mapping.ParameterName).Should().Contain(
            ["SegmentationInputSize", "NumClasses", "MaxClassMasks", "ClassNames"]);
        resolution.Mappings.Select(mapping => mapping.ParameterName).Should().NotContain(
            [
                "Confidence", "ModelVersion", "InputSize", "TargetClasses", "EnableInternalNms",
                "NmsIouThreshold", "OutputFormat", "DetectionMode", "TopK",
                "ClassificationInputSize", "ClassificationScoreMode", "LabelsPath"
            ]);
    }

    [Fact]
    public void Map_MedianFilter_ShouldExcludeParametersDisabledByFilterMode()
    {
        var resolution = Map(
            "Filtering",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["FilterMode"] = "Median",
                ["SigmaX"] = "9.0"
            });

        resolution.Mappings.Should().Contain(mapping =>
            mapping.ParameterName == "FilterMode" &&
            mapping.ValueSummary == "Median");
        resolution.Mappings.Select(mapping => mapping.ParameterName).Should().Contain("KernelSize");
        resolution.Mappings.Select(mapping => mapping.ParameterName).Should().NotContain(
            ["SigmaX", "SigmaY", "BorderType", "Diameter", "SigmaColor", "SigmaSpace"]);
    }

    [Fact]
    public void Map_LineToLineMeasurement_ShouldExposeOnlyLineDistanceParameters()
    {
        var resolution = Map(
            "Measurement",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["MeasureType"] = "LineToLine",
                ["X1"] = "99"
            });

        resolution.Mappings.Should().Contain(mapping =>
            mapping.ParameterName == "MeasureType" &&
            mapping.ValueSummary == "LineToLine");
        resolution.Mappings.Select(mapping => mapping.ParameterName).Should().Contain(
            ["DistanceModel", "ParallelThreshold"]);
        resolution.Mappings.Select(mapping => mapping.ParameterName).Should().NotContain(
            ["X1", "Y1", "X2", "Y2", "AngleUnit"]);
    }

    [Theory]
    [InlineData(AiVisionTaskTypes.TemplateLocation, "IsMatch")]
    [InlineData(AiVisionTaskTypes.WireSequence, "IsMatch")]
    public void Map_BooleanTask_ShouldCompareActualBooleanToTrue(string taskType, string fieldName)
    {
        var resolution = Map(
            "ResultJudgment",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            taskType: taskType,
            acceptanceCriteria: "OK when the task-specific match succeeds");

        Mapping(resolution, "FieldName").ValueSummary.Should().Be(fieldName);
        Mapping(resolution, "Condition").ValueSummary.Should().Be("Equal");
        Mapping(resolution, "ExpectValue").ValueSummary.Should().Be("true");
        Mapping(resolution, "ExpectValue").Pending.Should().BeFalse();
    }

    [Fact]
    public void Map_SurfaceDefectArea_ShouldUseConfirmedInclusiveUpperBound()
    {
        var resolution = Map(
            "ResultJudgment",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            taskType: AiVisionTaskTypes.SurfaceDefect,
            acceptanceCriteria: "OK: defect area <= 2.5 mm2; NG: defect area exceeds 2.5 mm2",
            measurementTarget: "defect area");

        Mapping(resolution, "FieldName").ValueSummary.Should().Be("DefectArea");
        Mapping(resolution, "Condition").ValueSummary.Should().Be("LessOrEqual");
        Mapping(resolution, "ExpectValue").ValueSummary.Should().Be("2.5");
        Mapping(resolution, "ExpectValue").Pending.Should().BeFalse();
    }

    [Fact]
    public void Map_SurfaceDefectWithoutLimit_ShouldRemainPendingInsteadOfDefaultingToOne()
    {
        var resolution = Map(
            "ResultJudgment",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            taskType: AiVisionTaskTypes.SurfaceDefect,
            acceptanceCriteria: "review defect evidence");

        Mapping(resolution, "Condition").ValueSummary.Should().Be("LessOrEqual");
        Mapping(resolution, "ExpectValue").ValueSummary.Should().Be("<pending-defect-upper-bound>");
        Mapping(resolution, "ExpectValue").Pending.Should().BeTrue();
        resolution.Mappings.Should().NotContain(mapping =>
            mapping.OperatorType == "ResultJudgment" &&
            mapping.ParameterName == "ExpectValue" &&
            mapping.ValueSummary == "1");
    }

    [Theory]
    [InlineData("OK: part is present; NG: part is missing", "GreaterOrEqual", "1")]
    [InlineData("OK: part is absent; NG: part is present", "Equal", "0")]
    public void Map_PresenceTask_ShouldRespectConfirmedPolarity(
        string acceptanceCriteria,
        string expectedCondition,
        string expectedValue)
    {
        var resolution = Map(
            "ResultJudgment",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            taskType: AiVisionTaskTypes.PresenceAbsence,
            acceptanceCriteria: acceptanceCriteria);

        Mapping(resolution, "FieldName").ValueSummary.Should().Be("PresenceCount");
        Mapping(resolution, "Condition").ValueSummary.Should().Be(expectedCondition);
        Mapping(resolution, "ExpectValue").ValueSummary.Should().Be(expectedValue);
    }

    [Fact]
    public void Map_PresenceTaskWithoutPolarity_ShouldRemainPending()
    {
        var resolution = Map(
            "ResultJudgment",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            taskType: AiVisionTaskTypes.PresenceAbsence,
            acceptanceCriteria: "inspect the component");

        Mapping(resolution, "ExpectValue").ValueSummary.Should().Be("<pending-presence-expectation>");
        Mapping(resolution, "ExpectValue").Pending.Should().BeTrue();
    }

    [Fact]
    public void Map_ClassificationTask_ShouldUseExpectedLabelAndRealConfidenceGate()
    {
        var resolution = Map(
            "ResultJudgment",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["classification_ok_label"] = "ripe"
            },
            taskType: AiVisionTaskTypes.AttributeClassification,
            acceptanceCriteria: "OK when confidence >= 0.85");

        Mapping(resolution, "FieldName").ValueSummary.Should().Be("TopClassLabel");
        Mapping(resolution, "Condition").ValueSummary.Should().Be("Equal");
        Mapping(resolution, "ExpectValue").ValueSummary.Should().Be("ripe");
        Mapping(resolution, "MinConfidence").ValueSummary.Should().Be("0.85");
    }

    [Fact]
    public void Map_ClassificationWithoutExpectedLabel_ShouldRemainPending()
    {
        var resolution = Map(
            "ResultJudgment",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            taskType: AiVisionTaskTypes.AttributeClassification,
            acceptanceCriteria: "confidence >= 0.8");

        Mapping(resolution, "ExpectValue").ValueSummary.Should().Be("<pending-ok-class-label>");
        Mapping(resolution, "ExpectValue").Pending.Should().BeTrue();
    }

    [Fact]
    public void Map_MeasurementTask_ShouldUseInclusiveConfirmedRange()
    {
        var resolution = Map(
            "ResultJudgment",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            taskType: AiVisionTaskTypes.GeometryMeasurement,
            acceptanceCriteria: "OK: distance 10 to 12 mm; NG: outside range",
            measurementTarget: "distance");

        Mapping(resolution, "Condition").ValueSummary.Should().Be("Range");
        Mapping(resolution, "ExpectValueMin").ValueSummary.Should().Be("10");
        Mapping(resolution, "ExpectValueMax").ValueSummary.Should().Be("12");
        Mapping(resolution, "ExpectValueMin").Pending.Should().BeFalse();
        Mapping(resolution, "ExpectValueMax").Pending.Should().BeFalse();
    }

    [Fact]
    public void Map_MeasurementTaskWithMissingBounds_ShouldKeepBothBoundsPending()
    {
        var resolution = Map(
            "ResultJudgment",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            taskType: AiVisionTaskTypes.GeometryMeasurement,
            acceptanceCriteria: "measure the distance",
            measurementTarget: "distance");

        Mapping(resolution, "ExpectValueMin").ValueSummary.Should().Be("<pending-measurement-minimum>");
        Mapping(resolution, "ExpectValueMax").ValueSummary.Should().Be("<pending-measurement-maximum>");
        Mapping(resolution, "ExpectValueMin").Pending.Should().BeTrue();
        Mapping(resolution, "ExpectValueMax").Pending.Should().BeTrue();
    }

    [Theory]
    [InlineData("OK: code = \"ABC-123\"; NG: code differs", "Text", "Equal", "ABC-123")]
    [InlineData("OK when at least one code is decoded", "CodeCount", "GreaterOrEqual", "1")]
    [InlineData("OK when code is read successfully", "CodeCount", "GreaterOrEqual", "1")]
    [InlineData("OK when code is recognized", "CodeCount", "GreaterOrEqual", "1")]
    public void Map_CodeRecognition_ShouldUseTextOnlyWhenExpectedCodeIsExplicit(
        string acceptanceCriteria,
        string expectedField,
        string expectedCondition,
        string expectedValue)
    {
        var resolution = Map(
            "ResultJudgment",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            taskType: AiVisionTaskTypes.CodeRecognition,
            acceptanceCriteria: acceptanceCriteria);

        Mapping(resolution, "FieldName").ValueSummary.Should().Be(expectedField);
        Mapping(resolution, "Condition").ValueSummary.Should().Be(expectedCondition);
        Mapping(resolution, "ExpectValue").ValueSummary.Should().Be(expectedValue);
    }

    [Theory]
    [InlineData("OK when at least 2 objects are detected", "GreaterOrEqual", "2")]
    [InlineData("OK when no more than 3 objects are detected", "LessOrEqual", "3")]
    [InlineData("OK when exactly 1 object is detected", "Equal", "1")]
    public void Map_ObjectDetection_ShouldGenerateOnlyExplicitCountConditions(
        string acceptanceCriteria,
        string expectedCondition,
        string expectedValue)
    {
        var resolution = Map(
            "ResultJudgment",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            taskType: AiVisionTaskTypes.ObjectDetection,
            acceptanceCriteria: acceptanceCriteria);

        Mapping(resolution, "FieldName").ValueSummary.Should().Be("ObjectCount");
        Mapping(resolution, "Condition").ValueSummary.Should().Be(expectedCondition);
        Mapping(resolution, "ExpectValue").ValueSummary.Should().Be(expectedValue);
    }

    [Fact]
    public void Map_ObjectDetectionWithoutCountAcceptance_ShouldRemainPending()
    {
        var resolution = Map(
            "ResultJudgment",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            taskType: AiVisionTaskTypes.ObjectDetection,
            acceptanceCriteria: "return detected objects");

        Mapping(resolution, "ExpectValue").ValueSummary.Should().Be("<pending-object-count-acceptance>");
        Mapping(resolution, "ExpectValue").Pending.Should().BeTrue();
    }

    [Theory]
    [InlineData("file_sample", "File")]
    [InlineData("image_folder", "File")]
    [InlineData("station_camera", "Camera")]
    [InlineData("line_camera", "Camera")]
    [InlineData("industrial_camera", "Camera")]
    [InlineData("camera", "Camera")]
    public void Map_ImageSourceAliases_ShouldUseSingleCanonicalSourceType(string imageSource, string sourceType)
    {
        var resolution = Map(
            "ImageAcquisition",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            imageSource: imageSource);

        Mapping(resolution, "SourceType").ValueSummary.Should().Be(sourceType);
    }

    [Fact]
    public void Map_FileSource_ShouldRequireOnlyImageFileResource()
    {
        var resolution = Map(
            "ImageAcquisition",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            imageSource: "file_sample");

        resolution.Mappings.Select(mapping => mapping.ParameterName).Should().Contain("FilePath");
        resolution.Mappings.Select(mapping => mapping.ParameterName).Should().NotContain("CameraId");
        resolution.MissingResources.Should().ContainSingle(resource =>
            resource.ResourceType == "image_file" && resource.ParameterName == "FilePath");
        resolution.MissingResources.Should().NotContain(resource => resource.ResourceType == "camera_binding");
    }

    [Fact]
    public void Map_CameraSource_ShouldRequireOnlyCameraResource()
    {
        var resolution = Map(
            "ImageAcquisition",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            imageSource: "station_camera");

        resolution.Mappings.Select(mapping => mapping.ParameterName).Should().Contain("CameraId");
        resolution.Mappings.Select(mapping => mapping.ParameterName).Should().NotContain("FilePath");
        resolution.MissingResources.Should().ContainSingle(resource =>
            resource.ResourceType == "camera_binding" && resource.ParameterName == "CameraId");
        resolution.MissingResources.Should().NotContain(resource => resource.ResourceType == "image_file");
    }

    [Fact]
    public void Map_PendingImageSource_ShouldKeepConditionalResourcesInactive()
    {
        var resolution = Map(
            "ImageAcquisition",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            imageSource: string.Empty);

        Mapping(resolution, "SourceType").ValueSummary.Should().Be("<pending-image-source>");
        Mapping(resolution, "SourceType").Pending.Should().BeTrue();
        resolution.Mappings.Select(mapping => mapping.ParameterName).Should().NotContain(["FilePath", "CameraId"]);
        resolution.MissingResources.Should().ContainSingle(resource =>
            resource.ResourceType == "image_source" && resource.ParameterName == "SourceType");
    }

    [Theory]
    [InlineData("video_stream")]
    [InlineData("unknown_source")]
    public void Map_UnsupportedImageSource_ShouldFailClosedWithoutCameraFallback(string imageSource)
    {
        var resolution = Map(
            "ImageAcquisition",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            imageSource: imageSource);

        Mapping(resolution, "SourceType").ValueSummary.Should().Be("<unsupported-image-source>");
        Mapping(resolution, "SourceType").Pending.Should().BeTrue();
        resolution.Mappings.Select(mapping => mapping.ParameterName).Should().NotContain(["FilePath", "CameraId"]);
        resolution.Mappings.Should().NotContain(mapping =>
            mapping.ParameterName == "SourceType" && mapping.ValueSummary == "Camera");
    }

    private static VisionAgentParameterMapping Mapping(ParameterMappingResolution resolution, string parameterName) =>
        resolution.Mappings.Single(mapping =>
            mapping.OperatorType == "ResultJudgment" && mapping.ParameterName == parameterName ||
            mapping.OperatorType == "ImageAcquisition" && mapping.ParameterName == parameterName);

    private static ParameterMappingResolution Map(
        string operatorType,
        IReadOnlyDictionary<string, string> parameterSelections,
        string originalUserPrompt = "",
        string taskType = "",
        string acceptanceCriteria = "",
        string imageSource = "",
        string measurementTarget = "")
    {
        var effectiveValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(taskType)) effectiveValues[VisionAgentPlanAnswerFields.TaskType] = taskType;
        if (!string.IsNullOrWhiteSpace(acceptanceCriteria)) effectiveValues[VisionAgentPlanAnswerFields.AcceptanceCriteria] = acceptanceCriteria;
        if (!string.IsNullOrWhiteSpace(imageSource)) effectiveValues[VisionAgentPlanAnswerFields.ImageSource] = imageSource;
        if (!string.IsNullOrWhiteSpace(measurementTarget)) effectiveValues[VisionAgentPlanAnswerFields.MeasurementTarget] = measurementTarget;
        var load = new BuildPlanLoad
        {
            OriginalUserPrompt = originalUserPrompt,
            ParameterSelections = parameterSelections,
            EffectiveRequirement = new VisionAgentEffectiveRequirement(
                effectiveValues,
                new AiRequirementMaturityResult { TaskType = taskType },
                effectiveValues.Keys.ToList(),
                [])
        };
        var pipeline = new OperatorPipelineResolution(
            [
                new VisionAgentOperatorPipelineStep
                {
                    TempId = "op_test",
                    OperatorType = operatorType,
                    Source = "test",
                    Status = "selected"
                }
            ],
            []);
        var selection = new PlanSelectionResolution(
            new VisionAgentRecommendedRoute(),
            SelectionSource: "test",
            Strategy: string.Empty,
            StrategyConfirmed: true,
            StrategyConfirmationSource: "test",
            UnresolvedStrategyBlockers: [],
            ParameterStrategy: string.Empty,
            BlockingReasons: [],
            Evidence: []);

        return new ParameterMappingService(new OperatorFactory())
            .Map(load, pipeline, selection)
            .Payload;
    }
}
