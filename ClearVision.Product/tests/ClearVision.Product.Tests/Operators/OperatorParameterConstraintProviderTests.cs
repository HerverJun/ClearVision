using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Services;
using ClearVision.Product.Infrastructure.AI.Tools;
using ClearVision.Product.Infrastructure.Services;
using FluentAssertions;

namespace ClearVision.Product.Tests.Operators;

[TestClassification(TestDomain.Core, TestPurpose.Regression, TestLane.Pr, TestEvidenceType.Contract, TestOracleType.Contract, TestResourceRequirement.None, TestExpectedDuration.Fast, TestFlakyPolicy.Blocking, "product")]
public sealed class OperatorParameterConstraintProviderTests
{
    private readonly OperatorFactory _factory = new();

    [Fact]
    public void DeclaredMetadata_ShouldExposeMigratedParameterRuleFamiliesWithoutProviderTable()
    {
        foreach (var operatorType in new[]
                 {
                     OperatorType.ImageAcquisition,
                     OperatorType.Filtering,
                     OperatorType.Measurement,
                     OperatorType.DeepLearning,
                     OperatorType.EdgeDetection,
                     OperatorType.ResultOutput,
                     OperatorType.BlobAnalysis,
                     OperatorType.ImageSave,
                     OperatorType.TextSave,
                     OperatorType.MitsubishiMcCommunication,
                     OperatorType.TcpCommunication
                 })
        {
            _factory.GetMetadata(operatorType)!.ParameterConstraints.Should().NotBeEmpty(operatorType.ToString());
        }

        typeof(OperatorMetadata).Assembly
            .GetType("ClearVision.Product.Core.Services.OperatorParameterConstraintProvider")
            .Should().BeNull("parameter rules now come from operator declarations instead of a provider table");

        typeof(OperatorParameterConstraint).GetProperties().Select(property => property.Name).Should().Contain(
        [
            nameof(OperatorParameterConstraint.RequiredPolicy),
            nameof(OperatorParameterConstraint.RequiredWhen),
            nameof(OperatorParameterConstraint.EnabledWhen),
            nameof(OperatorParameterConstraint.DisabledWhen),
            nameof(OperatorParameterConstraint.VisibleWhen),
            nameof(OperatorParameterConstraint.HiddenWhen),
            nameof(OperatorParameterConstraint.IgnoredWhen),
            nameof(OperatorParameterConstraint.AtLeastOneGroup),
            nameof(OperatorParameterConstraint.MutuallyExclusiveGroup),
            nameof(OperatorParameterConstraint.AliasFor),
            nameof(OperatorParameterConstraint.Deprecated),
            nameof(OperatorParameterConstraint.ResourceKind),
            nameof(OperatorParameterConstraint.ReasonCode)
        ]);
        typeof(OperatorParameterConstraint).GetProperties().Select(property => property.Name)
            .Should().NotContain("RequirementKind");
    }

    [Fact]
    public void ImageAcquisition_ShouldApplyConditionalFileAndCameraFacts()
    {
        var metadata = _factory.GetMetadata(OperatorType.ImageAcquisition)!;

        var fileStates = States(metadata, new Dictionary<string, object?> { ["SourceType"] = "File" });
        fileStates["FilePath"].EffectiveRequired.Should().BeTrue();
        fileStates["CameraId"].EffectiveDisabled.Should().BeTrue();
        fileStates["ExposureTime"].EffectiveDisabled.Should().BeTrue();

        var cameraViolations = OperatorParameterConstraintEvaluator.Validate(
            metadata,
            new Dictionary<string, object?> { ["SourceType"] = "Camera" });
        cameraViolations.Should().ContainSingle(item =>
            item.Code == "at-least-one" &&
            item.ParameterNames.SequenceEqual(new[] { "CameraId" }));

        OperatorParameterConstraintEvaluator.Validate(
                metadata,
                new Dictionary<string, object?>
                {
                    ["SourceType"] = "Camera",
                    ["CameraBindingId"] = "station-camera-1"
                })
            .Should().NotContain(item => item.Code == "required" || item.Code == "at-least-one");
    }

    [Fact]
    public void Canonicalization_ShouldPreferCanonicalThenAliasThenMetadataDefault()
    {
        var metadata = _factory.GetMetadata(OperatorType.ImageAcquisition)!;

        var aliasOnly = OperatorParameterConstraintEvaluator.Canonicalize(
            metadata,
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["sourceType"] = "Camera",
                ["CameraBindingId"] = "binding-camera"
            });
        aliasOnly.EffectiveValues["SourceType"].Should().Be("Camera");
        aliasOnly.EffectiveValues["CameraId"].Should().Be("binding-camera");
        aliasOnly.ExplicitValues.Keys.Should().Contain(["SourceType", "CameraId"]);
        aliasOnly.ExplicitValues.Keys.Should().NotContain(["sourceType", "CameraBindingId"]);
        aliasOnly.Diagnostics.Should().BeEmpty();

        var conflict = OperatorParameterConstraintEvaluator.Canonicalize(
            metadata,
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["CameraId"] = "canonical-camera",
                ["CameraBindingId"] = "binding-camera",
                ["cameraId"] = "legacy-camera"
            });
        conflict.EffectiveValues["CameraId"].Should().Be("canonical-camera");
        conflict.Diagnostics.Should().HaveCount(2);
        conflict.Diagnostics.Should().OnlyContain(item => item.Code == "canonical-overrides-alias");

        var defaultConflict = OperatorParameterConstraintEvaluator.Canonicalize(
            metadata,
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["CameraId"] = string.Empty,
                ["CameraBindingId"] = "binding-camera"
            });
        defaultConflict.ExplicitValues["CameraId"].Should().Be(string.Empty);
        defaultConflict.EffectiveValues["CameraId"].Should().Be(string.Empty);
        defaultConflict.Diagnostics.Should().ContainSingle(item =>
            item.Code == "canonical-overrides-alias" && item.AliasParameter == "CameraBindingId");
    }

    [Theory]
    [InlineData("<pending>", true)]
    [InlineData("<pending-camera-binding>", true)]
    [InlineData(" <PENDING-model-resource> ", true)]
    [InlineData("<pending-camera binding>", false)]
    [InlineData("<pending-camera-binding", false)]
    [InlineData("<pendingish>", false)]
    [InlineData("todo-line-camera", false)]
    [InlineData("customer-todo-approved", false)]
    public void PendingSentinel_ShouldRecognizeOnlyTheExplicitContract(string value, bool expected)
    {
        OperatorParameterValueSemantics.IsPendingSentinel(value).Should().Be(expected);
        OperatorParameterConstraintEvaluator.IsMissing(value).Should().Be(expected);
    }

    [Fact]
    public void Groups_ShouldCountOnlyActiveNonDisabledParameters()
    {
        var metadata = new OperatorMetadata
        {
            Parameters =
            [
                new ParameterDefinition { Name = "Mode", DefaultValue = "A" },
                new ParameterDefinition { Name = "A" },
                new ParameterDefinition { Name = "B" }
            ],
            ParameterConstraints =
            [
                new OperatorParameterConstraint(
                    "A",
                    OperatorParameterRequiredPolicies.Optional,
                    new OperatorParameterConditionSet(All:
                    [
                        new OperatorParameterCondition("Mode", OperatorParameterConditionComparisons.Equal, "A")
                    ]),
                    null,
                    new OperatorParameterConditionSet(All:
                    [
                        new OperatorParameterCondition("Mode", OperatorParameterConditionComparisons.Equal, "B")
                    ]),
                    "active-source",
                    "active-source",
                    null,
                    false,
                    "test_resource",
                    "ACTIVE_A"),
                new OperatorParameterConstraint(
                    "B",
                    OperatorParameterRequiredPolicies.Optional,
                    new OperatorParameterConditionSet(All:
                    [
                        new OperatorParameterCondition("Mode", OperatorParameterConditionComparisons.Equal, "B")
                    ]),
                    null,
                    new OperatorParameterConditionSet(All:
                    [
                        new OperatorParameterCondition("Mode", OperatorParameterConditionComparisons.Equal, "A")
                    ]),
                    "active-source",
                    "active-source",
                    null,
                    false,
                    "test_resource",
                    "ACTIVE_B")
            ]
        };

        var staleDisabled = OperatorParameterConstraintEvaluator.Validate(
            metadata,
            new Dictionary<string, object?>
            {
                ["Mode"] = "A",
                ["B"] = "stale-disabled-value"
            });
        staleDisabled.Should().ContainSingle(item =>
            item.Code == "at-least-one" && item.ParameterNames.SequenceEqual(new[] { "A" }));
        staleDisabled.Should().NotContain(item => item.Code == "mutually-exclusive");

        OperatorParameterConstraintEvaluator.Validate(
                metadata,
                new Dictionary<string, object?>
                {
                    ["Mode"] = "A",
                    ["A"] = "active-value",
                    ["B"] = "stale-disabled-value"
                })
            .Should().BeEmpty();
    }

    [Fact]
    public void ImageAcquisition_RuntimeImageInput_ShouldSatisfyDeclaredSourceResource()
    {
        var metadata = _factory.GetMetadata(OperatorType.ImageAcquisition)!;
        var satisfiedInputPorts = new HashSet<string>(["Image"], StringComparer.OrdinalIgnoreCase);

        var fileViolations = OperatorParameterConstraintEvaluator.Validate(
            metadata,
            new Dictionary<string, object?>
            {
                ["SourceType"] = "File",
                ["FilePath"] = string.Empty
            },
            requireExplicitResourceConfiguration: true,
            satisfiedInputPorts: satisfiedInputPorts);

        fileViolations.Should().BeEmpty();
        metadata.ParameterConstraints.Single(rule => rule.Parameter == "FilePath")
            .SatisfiedByInputPorts.Should().Contain("Image");

        var cameraViolations = OperatorParameterConstraintEvaluator.Validate(
            metadata,
            new Dictionary<string, object?>
            {
                ["SourceType"] = "Camera",
                ["CameraId"] = string.Empty
            },
            requireExplicitResourceConfiguration: true,
            satisfiedInputPorts: satisfiedInputPorts);

        cameraViolations.Should().BeEmpty();
    }

    [Fact]
    public void DeepLearning_ShouldRequirePathOrModelIdAndGateCatalogPath()
    {
        var metadata = _factory.GetMetadata(OperatorType.DeepLearning)!;

        OperatorParameterConstraintEvaluator.Validate(metadata, new Dictionary<string, object?>())
            .Should().ContainSingle(item => item.Code == "at-least-one");

        var catalogWithoutId = States(
            metadata,
            new Dictionary<string, object?> { ["ModelCatalogPath"] = "models/catalog.json" });
        catalogWithoutId["ModelCatalogPath"].EffectiveDisabled.Should().BeTrue();

        OperatorParameterConstraintEvaluator.Validate(
                metadata,
                new Dictionary<string, object?>
                {
                    ["ModelId"] = "detector-v1",
                    ["ModelCatalogPath"] = "models/catalog.json"
                })
            .Should().BeEmpty();

        OperatorParameterConstraintEvaluator.Validate(
                metadata,
                new Dictionary<string, object?>
                {
                    ["ModelId"] = "detector-v1",
                    ["ModelPath"] = "models/detector.onnx"
                })
            .Should().ContainSingle(item => item.Code == "mutually-exclusive");
    }

    [Fact]
    public void Filtering_ShouldHideParametersThatTheSelectedKernelDoesNotUse()
    {
        var metadata = _factory.GetMetadata(OperatorType.Filtering)!;

        var gaussian = States(metadata, new Dictionary<string, object?> { ["FilterMode"] = "Gaussian" });
        gaussian["KernelSize"].EffectiveDisabled.Should().BeFalse();
        gaussian["SigmaX"].EffectiveDisabled.Should().BeFalse();
        gaussian["BorderType"].EffectiveDisabled.Should().BeFalse();
        gaussian["Diameter"].EffectiveDisabled.Should().BeTrue();

        var median = States(metadata, new Dictionary<string, object?> { ["FilterMode"] = "Median" });
        median["KernelSize"].EffectiveDisabled.Should().BeFalse();
        median["SigmaX"].EffectiveDisabled.Should().BeTrue();
        median["BorderType"].EffectiveDisabled.Should().BeTrue();
        median["Diameter"].EffectiveDisabled.Should().BeTrue();

        var bilateral = States(metadata, new Dictionary<string, object?> { ["FilterMode"] = "Bilateral" });
        bilateral["KernelSize"].EffectiveDisabled.Should().BeTrue();
        bilateral["SigmaX"].EffectiveDisabled.Should().BeTrue();
        bilateral["BorderType"].EffectiveDisabled.Should().BeFalse();
        bilateral["Diameter"].EffectiveDisabled.Should().BeFalse();
        bilateral["SigmaColor"].EffectiveDisabled.Should().BeFalse();
        bilateral["SigmaSpace"].EffectiveDisabled.Should().BeFalse();
    }

    [Fact]
    public void Measurement_ShouldHideParametersOutsideTheSelectedGeometryMode()
    {
        var metadata = _factory.GetMetadata(OperatorType.Measurement)!;

        var point = States(metadata, new Dictionary<string, object?> { ["MeasureType"] = "PointToPoint" });
        point["X1"].EffectiveDisabled.Should().BeFalse();
        point["DistanceModel"].EffectiveDisabled.Should().BeTrue();
        point["ParallelThreshold"].EffectiveDisabled.Should().BeTrue();
        point["AngleUnit"].EffectiveDisabled.Should().BeTrue();

        var pointLine = States(metadata, new Dictionary<string, object?> { ["MeasureType"] = "PointToLine" });
        pointLine["X1"].EffectiveDisabled.Should().BeTrue();
        pointLine["DistanceModel"].EffectiveDisabled.Should().BeFalse();
        pointLine["ParallelThreshold"].EffectiveDisabled.Should().BeTrue();
        pointLine["AngleUnit"].EffectiveDisabled.Should().BeTrue();

        var lineLine = States(metadata, new Dictionary<string, object?> { ["MeasureType"] = "LineToLine" });
        lineLine["DistanceModel"].EffectiveDisabled.Should().BeFalse();
        lineLine["ParallelThreshold"].EffectiveDisabled.Should().BeFalse();

        var angle = States(metadata, new Dictionary<string, object?> { ["MeasureType"] = "ThreePointAngle" });
        angle["X1"].EffectiveDisabled.Should().BeTrue();
        angle["DistanceModel"].EffectiveDisabled.Should().BeTrue();
        angle["ParallelThreshold"].EffectiveDisabled.Should().BeTrue();
        angle["AngleUnit"].EffectiveDisabled.Should().BeFalse();
    }

    [Fact]
    public void DeepLearning_ShouldExposeOnlyParametersUsedByTheExplicitTask()
    {
        var metadata = _factory.GetMetadata(OperatorType.DeepLearning)!;

        var detection = States(metadata, new Dictionary<string, object?> { ["TaskType"] = "ObjectDetection" });
        detection["Confidence"].EffectiveDisabled.Should().BeFalse();
        detection["TopK"].EffectiveDisabled.Should().BeTrue();
        detection["SegmentationInputSize"].EffectiveDisabled.Should().BeTrue();
        detection["ScaleToUnitRange"].EffectiveDisabled.Should().BeTrue();
        detection["ClassNames"].EffectiveDisabled.Should().BeTrue();
        detection["LabelsPath"].EffectiveDisabled.Should().BeFalse();

        var classification = States(metadata, new Dictionary<string, object?> { ["TaskType"] = "ImageClassification" });
        classification["Confidence"].EffectiveDisabled.Should().BeTrue();
        classification["OutputFormat"].EffectiveDisabled.Should().BeTrue();
        classification["TopK"].EffectiveDisabled.Should().BeFalse();
        classification["ClassificationInputSize"].EffectiveDisabled.Should().BeFalse();
        classification["SegmentationInputSize"].EffectiveDisabled.Should().BeTrue();
        classification["ScaleToUnitRange"].EffectiveDisabled.Should().BeFalse();
        classification["ClassNames"].EffectiveDisabled.Should().BeFalse();
        classification["LabelsPath"].EffectiveDisabled.Should().BeFalse();

        var segmentation = States(metadata, new Dictionary<string, object?> { ["TaskType"] = "SemanticSegmentation" });
        segmentation["Confidence"].EffectiveDisabled.Should().BeTrue();
        segmentation["TopK"].EffectiveDisabled.Should().BeTrue();
        segmentation["SegmentationInputSize"].EffectiveDisabled.Should().BeFalse();
        segmentation["NumClasses"].EffectiveDisabled.Should().BeFalse();
        segmentation["ScaleToUnitRange"].EffectiveDisabled.Should().BeFalse();
        segmentation["ClassNames"].EffectiveDisabled.Should().BeFalse();
        segmentation["LabelsPath"].EffectiveDisabled.Should().BeTrue();

        var auto = States(metadata, new Dictionary<string, object?> { ["TaskType"] = "Auto" });
        auto["Confidence"].EffectiveDisabled.Should().BeFalse();
        auto["TopK"].EffectiveDisabled.Should().BeFalse();
        auto["SegmentationInputSize"].EffectiveDisabled.Should().BeFalse();
        auto["ScaleToUnitRange"].EffectiveDisabled.Should().BeFalse();
    }

    [Fact]
    public void EdgeDetection_ShouldMatchRuntimeModelResolutionSemantics()
    {
        var metadata = _factory.GetMetadata(OperatorType.EdgeDetection)!;

        OperatorParameterConstraintEvaluator.Validate(
                metadata,
                new Dictionary<string, object?> { ["Method"] = "Canny" })
            .Should().BeEmpty();

        OperatorParameterConstraintEvaluator.Validate(
                metadata,
                new Dictionary<string, object?>
                {
                    ["Method"] = "OnnxEdge",
                    ["ModelCatalogPath"] = "models/catalog.json"
                })
            .Should().ContainSingle(item => item.Code == "at-least-one");

        OperatorParameterConstraintEvaluator.Validate(
                metadata,
                new Dictionary<string, object?>
                {
                    ["Method"] = "OnnxEdge",
                    ["EdgeModelId"] = "edge-v1",
                    ["ModelCatalogPath"] = "models/catalog.json"
                })
            .Should().BeEmpty();
    }

    [Fact]
    public void ResultOutput_ShouldNotInventOutputChannelOrPathParameters()
    {
        var metadata = _factory.GetMetadata(OperatorType.ResultOutput)!;
        var constraints = metadata.ParameterConstraints;

        constraints.Should().ContainSingle(item => item.Parameter == "SaveToFile");
        constraints.Select(item => item.Parameter).Should().NotContain(
        [
            "Channel",
            "OutputChannel",
            "OutputChannelId",
            "FilePath",
            "OutputPath",
            "PlcAddress",
            "PLCParameters"
        ]);
    }

    [Fact]
    public void BlobAnalysis_ShouldKeepFeatureFilterOptionalAndDisableHsvUntilEnabled()
    {
        var metadata = _factory.GetMetadata(OperatorType.BlobAnalysis)!;
        var disabledStates = States(
            metadata,
            new Dictionary<string, object?> { ["EnableColorFilter"] = false });

        disabledStates["FeatureFilter"].EffectiveRequired.Should().BeFalse();
        foreach (var name in new[] { "HueLow", "HueHigh", "SatLow", "SatHigh", "ValLow", "ValHigh" })
        {
            disabledStates[name].EffectiveDisabled.Should().BeTrue();
        }

        var enabledStates = States(
            metadata,
            new Dictionary<string, object?> { ["EnableColorFilter"] = true });
        enabledStates["HueLow"].EffectiveDisabled.Should().BeFalse();
    }

    [Fact]
    public void ImageSave_ShouldCanonicalizeLegacyAliasesAndPreferExplicitCanonicalValues()
    {
        var metadata = _factory.GetMetadata(OperatorType.ImageSave)!;
        var legacy = OperatorParameterConstraintEvaluator.Canonicalize(
            metadata,
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["FolderPath"] = "legacy-output",
                ["FileName"] = "legacy.jpg",
                ["JpegQuality"] = 81
            });

        legacy.ExplicitValues["Directory"].Should().Be("legacy-output");
        legacy.ExplicitValues["FileNameTemplate"].Should().Be("legacy.jpg");
        legacy.ExplicitValues["Quality"].Should().Be(81);

        var conflict = OperatorParameterConstraintEvaluator.Canonicalize(
            metadata,
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["Directory"] = "canonical-output",
                ["FolderPath"] = "legacy-output"
            });
        conflict.EffectiveValues["Directory"].Should().Be("canonical-output");
        conflict.Diagnostics.Should().ContainSingle(item =>
            item.Code == "canonical-overrides-alias" && item.AliasParameter == "FolderPath");
    }

    [Fact]
    public void FileOutputOperators_ShouldExposeRequiredOutputResourceFacts()
    {
        var imageSave = _factory.GetMetadata(OperatorType.ImageSave)!;
        OperatorParameterConstraintEvaluator.Validate(
                imageSave,
                new Dictionary<string, object?>
                {
                    ["Directory"] = "",
                    ["FileNameTemplate"] = ""
                })
            .SelectMany(item => item.ParameterNames)
            .Should().BeEquivalentTo(["Directory", "FileNameTemplate"]);

        var textSave = _factory.GetMetadata(OperatorType.TextSave)!;
        OperatorParameterConstraintEvaluator.Validate(
                textSave,
                new Dictionary<string, object?> { ["FilePath"] = "<pending-output-file>" })
            .Should().ContainSingle(item =>
                item.Code == "required" &&
                item.ParameterNames.SequenceEqual(new[] { "FilePath" }) &&
                item.ResourceKind == "output_file");
    }

    [Fact]
    public void ResourceDefaults_ShouldRequireAnExplicitConfigurationForDeploymentValidation()
    {
        var imageSave = _factory.GetMetadata(OperatorType.ImageSave)!;

        OperatorParameterConstraintEvaluator.Validate(
                imageSave,
                new Dictionary<string, object?>(),
                requireExplicitResourceConfiguration: true)
            .Should().ContainSingle(item =>
                item.Code == "required" &&
                item.ParameterNames.SequenceEqual(new[] { "Directory" }) &&
                item.ResourceKind == "output_file");

        OperatorParameterConstraintEvaluator.Validate(
                imageSave,
                new Dictionary<string, object?>
                {
                    ["Directory"] = "C:\\ClearVision\\NG_Images"
                },
                requireExplicitResourceConfiguration: true)
            .Should().BeEmpty("an explicitly supplied resource value may equal its metadata default");
    }

    [Fact]
    public void MitsubishiMc_ShouldDisableReadAndPollingValuesOutsideTheirActiveModes()
    {
        var metadata = _factory.GetMetadata(OperatorType.MitsubishiMcCommunication)!;
        var writeStates = States(
            metadata,
            new Dictionary<string, object?>
            {
                ["Operation"] = "Write",
                ["PollingMode"] = "WaitForValue",
                ["Length"] = 99,
                ["PollingValue"] = "stale"
            });
        writeStates["Length"].EffectiveDisabled.Should().BeTrue();
        writeStates["PollingMode"].EffectiveDisabled.Should().BeTrue();
        writeStates["PollingValue"].EffectiveDisabled.Should().BeTrue();
        writeStates["WriteValue"].EffectiveDisabled.Should().BeFalse();

        var pollingStates = States(
            metadata,
            new Dictionary<string, object?>
            {
                ["Operation"] = "Read",
                ["PollingMode"] = "WaitForValue"
            });
        pollingStates["PollingCondition"].EffectiveDisabled.Should().BeFalse();
        pollingStates["PollingTimeout"].EffectiveDisabled.Should().BeFalse();

        OperatorParameterConstraintEvaluator.Validate(
                metadata,
                new Dictionary<string, object?>
                {
                    ["UseGlobalFallback"] = false,
                    ["IpAddress"] = "192.168.3.39",
                    ["Port"] = 5002,
                    ["Address"] = "D100",
                    ["Operation"] = "Write",
                    ["WriteValue"] = ""
                })
            .Should().BeEmpty("WriteValue may be supplied by the Data input at execution time");

        OperatorParameterConstraintEvaluator.Validate(
                metadata,
                new Dictionary<string, object?>
                {
                    ["UseGlobalFallback"] = false,
                    ["IpAddress"] = "",
                    ["Port"] = "",
                    ["Address"] = ""
                })
            .SelectMany(item => item.ParameterNames)
            .Should().BeEquivalentTo(["IpAddress", "Port", "Address"]);
    }

    [Fact]
    public void TcpCommunication_ShouldProjectProfileResponseAndParseModeFacts()
    {
        var metadata = _factory.GetMetadata(OperatorType.TcpCommunication)!;

        OperatorParameterConstraintEvaluator.Validate(
                metadata,
                new Dictionary<string, object?>
                {
                    ["UseGlobalProfile"] = true,
                    ["ProfileId"] = ""
                })
            .Should().ContainSingle(item =>
                item.ParameterNames.SequenceEqual(new[] { "ProfileId" }) &&
                item.ResourceKind == "tcp_profile");

        var profileStates = States(
            metadata,
            new Dictionary<string, object?> { ["ProfileId"] = "robot-profile" });
        profileStates["Mode"].EffectiveDisabled.Should().BeTrue();
        profileStates["IpAddress"].EffectiveDisabled.Should().BeTrue();
        profileStates["Port"].EffectiveDisabled.Should().BeTrue();
        profileStates["Timeout"].EffectiveDisabled.Should().BeTrue();

        var noResponseStates = States(
            metadata,
            new Dictionary<string, object?>
            {
                ["WaitResponse"] = false,
                ["ResponseParseMode"] = "Regex",
                ["ResponseRegexPattern"] = "<pending-regex>"
            });
        noResponseStates["ResponseTimeoutMs"].EffectiveDisabled.Should().BeTrue();
        noResponseStates["ResponseParseMode"].EffectiveDisabled.Should().BeTrue();
        noResponseStates["ResponseRegexPattern"].EffectiveDisabled.Should().BeTrue();
        noResponseStates["FailOnParseError"].EffectiveDisabled.Should().BeTrue();
        noResponseStates["FailOnUnexpectedResponse"].EffectiveDisabled.Should().BeTrue();
        noResponseStates["RequiredResponseFields"].EffectiveDisabled.Should().BeTrue();
        noResponseStates["ResponseStartMarker"].EffectiveDisabled.Should().BeTrue();
        noResponseStates["ExpectedResponse"].EffectiveDisabled.Should().BeTrue();
        noResponseStates["ResponseMatchMode"].EffectiveDisabled.Should().BeTrue();
        OperatorParameterConstraintEvaluator.Validate(
                metadata,
                new Dictionary<string, object?>
                {
                    ["WaitResponse"] = false,
                    ["ResponseParseMode"] = "Regex",
                    ["ResponseRegexPattern"] = "<pending-regex>"
                })
            .Should().BeEmpty();

        OperatorParameterConstraintEvaluator.Validate(
                metadata,
                new Dictionary<string, object?>
                {
                    ["WaitResponse"] = true,
                    ["ResponseParseMode"] = "Regex",
                    ["ResponseRegexPattern"] = ""
                })
            .Should().ContainSingle(item => item.ParameterNames.SequenceEqual(new[] { "ResponseRegexPattern" }));

        OperatorParameterConstraintEvaluator.Validate(
                metadata,
                new Dictionary<string, object?>
                {
                    ["WaitResponse"] = true,
                    ["ResponseParseMode"] = "KeyValue",
                    ["ResponseKeyValuePairDelimiter"] = "",
                    ["ResponseKeyValuePairDelimiters"] = "",
                    ["ResponseKeyValueSeparator"] = "",
                    ["ResponseKeyValueSeparators"] = ""
                })
            .Should().Contain(items =>
                items.Code == "at-least-one" &&
                items.ParameterNames.Contains("ResponseKeyValuePairDelimiter") &&
                items.ParameterNames.Contains("ResponseKeyValuePairDelimiters"));

        OperatorParameterConstraintEvaluator.Validate(
                metadata,
                new Dictionary<string, object?>
                {
                    ["WaitResponse"] = true,
                    ["ResponseParseMode"] = "KeyValue",
                    ["ResponseKeyValuePairDelimiter"] = "",
                    ["ResponseKeyValuePairDelimiters"] = ",",
                    ["ResponseKeyValueSeparator"] = "",
                    ["ResponseKeyValueSeparators"] = ":"
                })
            .Should().BeEmpty();
    }

    [Fact]
    public void VisionAgentContract_ShouldCarryTheSameProviderFacts()
    {
        var catalog = new VisionAgentOperatorContractCatalog(_factory);

        catalog.TryGet("DeepLearning", out var deepLearning).Should().BeTrue();
        deepLearning.ParameterConstraints.Should().BeEquivalentTo(
            _factory.GetMetadata(OperatorType.DeepLearning)!.ParameterConstraints);
    }

    private static Dictionary<string, OperatorParameterConstraintState> States(
        OperatorMetadata metadata,
        IReadOnlyDictionary<string, object?> values)
    {
        return OperatorParameterConstraintEvaluator.ResolveStates(metadata, values)
            .GroupBy(item => item.Constraint.Parameter, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
    }
}
