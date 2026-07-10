using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Services;
using ClearVision.Product.Infrastructure.AI.Tools;
using ClearVision.Product.Infrastructure.Services;
using FluentAssertions;

namespace ClearVision.Product.Tests.Operators;

public sealed class OperatorParameterConstraintProviderTests
{
    private readonly OperatorFactory _factory = new();

    [Fact]
    public void Provider_ShouldExposeOnlyTheFiveMigratedOperators()
    {
        var provider = OperatorParameterConstraintProvider.Instance;

        provider.GetConstraints(OperatorType.ImageAcquisition).Should().NotBeEmpty();
        provider.GetConstraints(OperatorType.DeepLearning).Should().NotBeEmpty();
        provider.GetConstraints(OperatorType.EdgeDetection).Should().NotBeEmpty();
        provider.GetConstraints(OperatorType.ResultOutput).Should().NotBeEmpty();
        provider.GetConstraints(OperatorType.BlobAnalysis).Should().NotBeEmpty();
        provider.GetConstraints(OperatorType.TemplateMatching).Should().BeEmpty();

        typeof(OperatorParameterConstraint).GetProperties().Select(property => property.Name).Should().Contain(
        [
            nameof(OperatorParameterConstraint.RequiredPolicy),
            nameof(OperatorParameterConstraint.RequiredWhen),
            nameof(OperatorParameterConstraint.EnabledWhen),
            nameof(OperatorParameterConstraint.DisabledWhen),
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
            item.ParameterNames.Contains("CameraId") &&
            item.ParameterNames.Contains("CameraBindingId"));

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
