using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.ValueObjects;
using ClearVision.Product.Desktop.Observation;
using ClearVision.Product.Infrastructure.Calibration;
using ClearVision.Product.Infrastructure.Operators;
using FluentAssertions;
using OpenCvSharp;

namespace ClearVision.Product.Desktop.Tests;

[TestClassification(TestDomain.Desktop, TestPurpose.Regression, TestLane.Pr, TestEvidenceType.Contract, TestOracleType.Contract, TestResourceRequirement.None, TestExpectedDuration.Fast, TestFlakyPolicy.Blocking, "desktop")]
public sealed class ExecutionObservationProjectorTests
{
    private static readonly JsonSerializerOptions CamelCaseJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    [Fact]
    public void ExecutionObservationEnvelopeV1_ShouldSerializeWithStableFieldOrder()
    {
        var envelope = new ExecutionObservationEnvelopeV1
        {
            ObservedAtUtc = DateTimeOffset.Parse("2026-07-02T01:02:03Z"),
            Identity = new ExecutionObservationIdentityV1
            {
                ProjectId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                TargetNodeId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
                DebugSessionId = Guid.Parse("33333333-3333-3333-3333-333333333333"),
                ClientRequestSequence = 7,
                FlowRevision = 9
            },
            Outcome = new ExecutionObservationOutcomeV1
            {
                Success = true,
                ExecutionTimeMs = 12,
                ExecutedOperatorCount = 1
            },
            Summary =
            [
                new ExecutionObservationSummaryItemV1
                {
                    Key = "Score",
                    DisplayValue = "0.95",
                    OriginalType = typeof(double).FullName,
                    PathHint = "$[\"Score\"]",
                    Addressable = true
                }
            ],
            Detail = new ExecutionObservationDetailNodeV1
            {
                Kind = "number",
                DisplayValue = "0.95",
                OriginalType = typeof(double).FullName,
                PathHint = "$[\"Score\"]",
                Addressable = true,
                Name = "Score"
            },
            Limits = new ExecutionObservationLimitsV1()
        };

        var json = JsonSerializer.Serialize(envelope, CamelCaseJson);

        json.Should().StartWith("{\"schemaVersion\":\"execution-observation.v1\",\"observedAtUtc\":\"2026-07-02T01:02:03+00:00\",\"identity\":");
        json.Should().Contain("\"outcome\":{\"success\":true,\"executionTimeMs\":12,\"errorMessage\":null,\"failedOperatorId\":null,\"failedOperatorName\":null,\"failedOperatorType\":null,\"executedOperatorCount\":1}");
        json.Should().Contain("\"limits\":{\"maxDepth\":4,\"maxObjectFields\":64,\"maxCollectionItems\":64,\"maxStringChars\":1024,\"maxNodes\":2048,\"maxDetailBytes\":262144}");
        json.Should().EndWith("\"truncated\":false}");
    }

    [Fact]
    public void CreatePreviewObservation_ShouldProjectScalarsDictionariesJsonElementAndEnum()
    {
        using var document = JsonDocument.Parse("{\"Nested\":{\"Flag\":true,\"Count\":3}}");
        var observation = CreateObservation(new Dictionary<string, object>
        {
            ["Score"] = 0.95d,
            ["Seen"] = 7L,
            ["Mode"] = OperatorType.Thresholding,
            ["Payload"] = document.RootElement.Clone()
        });

        observation.Summary.Select(item => item.Key)
            .Should().Contain(["Mode", "Score", "Seen"]);
        FindNode(observation.Detail, "Score").Should().NotBeNull()
            .And.Match<ExecutionObservationDetailNodeV1>(node => node!.Kind == "number" && !node.Addressable);
        FindNode(observation.Detail, "Mode")!.DisplayValue.Should().Be("Thresholding");
        FindNode(observation.Detail, "Flag")!.DisplayValue.Should().Be("true");
        observation.Diagnostics.Should().BeEmpty();
    }

    [Fact]
    public void CreatePreviewObservation_ShouldFailSoftOnCircularReferences()
    {
        var circular = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        circular["Self"] = circular;

        var observation = CreateObservation(circular);

        FindNode(observation.Detail, "Self")!.Kind.Should().Be("circular");
        observation.Diagnostics.Should().Contain(item => item.Code == "circular-reference");
        observation.Truncated.Should().BeTrue();
    }

    [Fact]
    public void CreatePreviewObservation_ShouldFailSoftOnThrowingGetter()
    {
        var observation = CreateObservation(new Dictionary<string, object>
        {
            ["Unsafe"] = new ThrowingGetterDto()
        });

        FindNode(observation.Detail, "Unsafe")!.Kind.Should().Be("objectDescriptor");
        observation.Diagnostics.Should().NotContain(item => item.Code == "getter-error");
    }

    [Fact]
    public void CreatePreviewObservation_ShouldNotCallUnknownToStringGetterOrEnumerable()
    {
        var throwingEnumerable = new ThrowingEnumerable();
        var infiniteEnumerable = new CountingInfiniteEnumerable();
        var observation = CreateObservation(new Dictionary<string, object>
        {
            ["ThrowingToString"] = new ThrowingToStringValue(),
            ["ThrowingGetter"] = new ThrowingGetterDto(),
            ["ThrowingEnumerable"] = throwingEnumerable,
            ["InfiniteEnumerable"] = infiniteEnumerable
        });

        FindNode(observation.Detail, "ThrowingToString")!.Kind.Should().Be("objectDescriptor");
        FindNode(observation.Detail, "ThrowingGetter")!.Kind.Should().Be("objectDescriptor");
        FindNode(observation.Detail, "ThrowingEnumerable")!.Kind.Should().Be("unsupportedEnumerable");
        FindNode(observation.Detail, "InfiniteEnumerable")!.Kind.Should().Be("unsupportedEnumerable");
        throwingEnumerable.GetEnumeratorCallCount.Should().Be(0);
        infiniteEnumerable.MoveNextCount.Should().Be(0);
        observation.Diagnostics.Should().Contain(item => item.Code == "unsupported-enumerable");
        observation.Diagnostics.Should().NotContain(item => item.Code == "getter-error");

        var legacy = ExecutionObservationProjector.BuildLegacyOutputData(
            new Dictionary<string, object>
            {
                ["ThrowingToString"] = new ThrowingToStringValue(),
                ["ThrowingGetter"] = new ThrowingGetterDto(),
                ["ThrowingEnumerable"] = throwingEnumerable,
                ["InfiniteEnumerable"] = infiniteEnumerable
            },
            _ => false);

        ((Dictionary<string, object?>)legacy["ThrowingToString"])["kind"].Should().Be("object");
        ((Dictionary<string, object?>)legacy["ThrowingGetter"])["kind"].Should().Be("object");
        ((Dictionary<string, object?>)legacy["ThrowingEnumerable"])["kind"].Should().Be("unsupportedEnumerable");
        ((Dictionary<string, object?>)legacy["InfiniteEnumerable"])["kind"].Should().Be("unsupportedEnumerable");
        throwingEnumerable.GetEnumeratorCallCount.Should().Be(0);
        infiniteEnumerable.MoveNextCount.Should().Be(0);
    }

    [Fact]
    public void CreatePreviewObservation_ShouldApplyTraversalLimits()
    {
        var wide = Enumerable.Range(0, 70).ToDictionary(
            index => $"Field{index:D2}",
            index => (object)index,
            StringComparer.OrdinalIgnoreCase);
        wide["A_LongString"] = new string('x', ExecutionObservationProjector.MaxStringChars + 10);
        wide["A_LongList"] = Enumerable.Range(0, 70).Cast<object>().ToList();

        var deep = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        var cursor = deep;
        for (var i = 0; i < 8; i++)
        {
            var next = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            cursor[$"L{i}"] = next;
            cursor = next;
        }

        wide["Deep"] = deep;
        var observation = CreateObservation(wide);

        observation.Diagnostics.Select(item => item.Code).Should().Contain([
            "field-limit",
            "string-limit",
            "collection-limit",
            "depth-limit"
        ]);
        observation.Truncated.Should().BeTrue();
        FindNode(observation.Detail, "A_LongString")!.DisplayValue!.Length
            .Should().Be(ExecutionObservationProjector.MaxStringChars + 3);
    }

    [Fact]
    public void CreatePreviewObservation_ShouldApplyNodeAndByteBudgets()
    {
        var large = Enumerable.Range(0, 64)
            .Select(row => Enumerable.Range(0, 64).ToDictionary(
                col => $"C{col:D2}",
                _ => (object)new string('z', 8_000),
                StringComparer.OrdinalIgnoreCase))
            .Cast<object>()
            .ToList();

        var observation = CreateObservation(new Dictionary<string, object>
        {
            ["Rows"] = large
        });

        observation.Diagnostics.Should().Contain(item =>
            item.Code == "node-limit" || item.Code == "byte-budget");
        observation.Truncated.Should().BeTrue();
        JsonSerializer.SerializeToUtf8Bytes(observation.Detail, CamelCaseJson).Length
            .Should().BeLessThanOrEqualTo(ExecutionObservationProjector.MaxDetailBytes);
        observation.Diagnostics.Count(item => item.Code == "byte-budget").Should().BeLessThanOrEqualTo(1);
    }

    [Fact]
    public void CreatePreviewObservation_ShouldRepresentNonFiniteNumbersAsLegalJson()
    {
        var observation = CreateObservation(new Dictionary<string, object>
        {
            ["Nan"] = double.NaN,
            ["Infinity"] = float.PositiveInfinity
        });

        var json = JsonSerializer.Serialize(observation, CamelCaseJson);

        json.Should().Contain("\"displayValue\":\"NaN\"");
        json.Should().Contain("\"displayValue\":\"Infinity\"");
        observation.Diagnostics.Should().Contain(item => item.Code == "non-finite-number");
    }

    [Fact]
    public void CreatePreviewObservation_ShouldDescribeImageMatrixAndBinaryValuesWithoutContent()
    {
        using var mat = new Mat(3, 2, MatType.CV_8UC1, Scalar.All(1));
        using var wrapper = new ImageWrapper(new Mat(4, 5, MatType.CV_8UC3, Scalar.All(2)));

        var observation = CreateObservation(new Dictionary<string, object>
        {
            ["Image"] = wrapper,
            ["Matrix"] = mat,
            ["Bytes"] = new byte[] { 1, 2, 3, 4, 5 }
        });

        FindNode(observation.Detail, "Image")!.Kind.Should().Be("image");
        FindNode(observation.Detail, "Matrix")!.Kind.Should().Be("matrix");
        FindNode(observation.Detail, "Bytes")!.Kind.Should().Be("binary");
        FindNode(observation.Detail, "Image")!.Addressable.Should().BeFalse();
        JsonSerializer.Serialize(observation, CamelCaseJson).Should().NotContain("AQIDBAU=");
    }

    [Fact]
    public void CreatePreviewObservation_ShouldAttachOutputPortRelativeCanonicalResultPathMetadata()
    {
        var scorePortId = Guid.NewGuid();
        var payloadPortId = Guid.NewGuid();
        var observation = CreateObservation(
            new Dictionary<string, object>
            {
                ["Score"] = 0.95d,
                ["Payload"] = new Dictionary<string, object>
                {
                    ["Nested"] = new Dictionary<string, object>
                    {
                        ["Seen"] = 7L
                    }
                }
            },
            [
                new ExecutionObservationOutputPortV1 { Id = scorePortId, Name = "Score" },
                new ExecutionObservationOutputPortV1 { Id = payloadPortId, Name = "Payload" }
            ]);

        var score = FindNode(observation.Detail, "Score")!;
        score.OutputPortId.Should().Be(scorePortId);
        score.OutputPortName.Should().Be("Score");
        score.ResultPathVersion.Should().Be(1);
        score.ResultPath.Should().Be("$");
        score.BindableVariableTypes.Should().BeEquivalentTo(["String", "Double"]);

        var seen = FindNode(observation.Detail, "Seen")!;
        seen.OutputPortId.Should().Be(payloadPortId);
        seen.OutputPortName.Should().Be("Payload");
        seen.ResultPathVersion.Should().Be(1);
        seen.ResultPath.Should().Be("$[\"Nested\"][\"Seen\"]");
        seen.BindableVariableTypes.Should().BeEquivalentTo(["String", "Int64", "Double"]);

        var summarySeen = observation.Summary.Should().Contain(item => item.Key == "Seen").Subject;
        summarySeen.OutputPortId.Should().Be(seen.OutputPortId);
        summarySeen.OutputPortName.Should().Be(seen.OutputPortName);
        summarySeen.ResultPathVersion.Should().Be(seen.ResultPathVersion);
        summarySeen.ResultPath.Should().Be(seen.ResultPath);
        summarySeen.BindableVariableTypes.Should().BeEquivalentTo(seen.BindableVariableTypes);
    }

    [Fact]
    public void CreatePreviewObservation_ShouldOmitCanonicalResultPathMetadataForArraysResourcesAndAmbiguousPorts()
    {
        using var mat = new Mat(1, 1, MatType.CV_8UC1, Scalar.All(255));
        var ambiguousPortName = "Payload";
        var observation = CreateObservation(
            new Dictionary<string, object>
            {
                [ambiguousPortName] = new Dictionary<string, object>
                {
                    ["Score"] = 3L,
                    ["Values"] = new[] { 1, 2, 3 },
                    ["Image"] = mat
                }
            },
            [
                new ExecutionObservationOutputPortV1 { Id = Guid.NewGuid(), Name = ambiguousPortName },
                new ExecutionObservationOutputPortV1 { Id = Guid.NewGuid(), Name = ambiguousPortName }
            ]);

        FindNode(observation.Detail, "Score")!.ResultPath.Should().BeNull();
        FindNode(observation.Detail, "Score")!.Addressable.Should().BeFalse();
        FindNode(observation.Detail, "Values")!.ResultPath.Should().BeNull();
        FindNode(observation.Detail, "Image")!.ResultPath.Should().BeNull();
        observation.Diagnostics.Should().Contain(item => item.Code == "resultpath-port-ambiguous");

        var unique = CreateObservation(
            new Dictionary<string, object>
            {
                ["Payload"] = new Dictionary<string, object>
                {
                    ["Values"] = new[] { 1, 2, 3 },
                    ["Image"] = new byte[] { 1, 2, 3 }
                }
            },
            [new ExecutionObservationOutputPortV1 { Id = Guid.NewGuid(), Name = "Payload" }]);

        var values = FindNode(unique.Detail, "Values")!;
        values.Locatable.Should().BeTrue();
        values.Addressable.Should().BeFalse();
        values.BindableVariableTypes.Should().BeNull();
        values.ResultPath.Should().Be("$[\"Values\"]");
        FindNode(unique.Detail, "Image")!.ResultPath.Should().BeNull();
        unique.Summary.Any(item =>
            item.Key == "Image" &&
            item.ResultPath != null).Should().BeFalse();
    }

    [Fact]
    public void CreatePreviewObservation_ShouldOnlyAttachMetadataWhenProductionResolverCanResolveProjectedLeaf()
    {
        var portId = Guid.NewGuid();
        var observation = CreateObservation(
            new Dictionary<string, object>
            {
                ["Detection"] = new DetectionResult("defect", 0.75f, 1, 2, 3, 4)
            },
            [new ExecutionObservationOutputPortV1 { Id = portId, Name = "Detection" }]);

        var confidence = FindNode(observation.Detail, "Confidence")!;
        confidence.DisplayValue.Should().Be("0.75");
        confidence.Addressable.Should().BeFalse();
        confidence.OutputPortId.Should().BeNull();
        confidence.ResultPath.Should().BeNull();
        observation.Summary.Should().Contain(item => item.Key == "Confidence" && !item.Addressable && item.ResultPath == null);
        observation.Diagnostics.Should().Contain(item => item.Code == "resultpath-unresolvable");
    }

    [Fact]
    public void CreatePreviewObservation_ShouldKeepNonStringDictionaryKeysDisplayOnly()
    {
        var portId = Guid.NewGuid();
        var observation = CreateObservation(
            new Dictionary<string, object>
            {
                ["Payload"] = new Dictionary<int, object>
                {
                    [1] = 42L
                }
            },
            [new ExecutionObservationOutputPortV1 { Id = portId, Name = "Payload" }]);

        var numericKey = FindNode(observation.Detail, "1")!;
        numericKey.DisplayValue.Should().Be("42");
        numericKey.Addressable.Should().BeFalse();
        numericKey.OutputPortId.Should().BeNull();
        numericKey.ResultPath.Should().BeNull();
    }

    [Fact]
    public void CreatePreviewObservation_ShouldAllowStringKeysInMixedDictionaryButRejectFormattedCollisions()
    {
        var portId = Guid.NewGuid();
        var mixed = new Hashtable
        {
            ["Name"] = "ok",
            [1] = 42L
        };
        var observation = CreateObservation(
            new Dictionary<string, object>
            {
                ["Payload"] = mixed
            },
            [new ExecutionObservationOutputPortV1 { Id = portId, Name = "Payload" }]);

        var name = FindNode(observation.Detail, "Name")!;
        name.Addressable.Should().BeTrue();
        name.OutputPortId.Should().Be(portId);
        name.ResultPath.Should().Be("$[\"Name\"]");
        FindNode(observation.Detail, "1")!.Addressable.Should().BeFalse();

        var collision = new Hashtable
        {
            ["1"] = "string",
            [1] = "int"
        };
        var collisionObservation = CreateObservation(
            new Dictionary<string, object>
            {
                ["Payload"] = collision
            },
            [new ExecutionObservationOutputPortV1 { Id = portId, Name = "Payload" }]);

        var collidingNodes = FindNodes(collisionObservation.Detail, "1").ToList();
        collidingNodes.Should().HaveCount(2);
        collidingNodes.Should().OnlyContain(node => !node.Addressable && node.ResultPath == null);
        collisionObservation.Diagnostics.Should().Contain(item => item.Code == "dictionary-key-collision");
    }

    [Fact]
    public void CreatePreviewObservation_ShouldClipLongKeysAndMakeTruncatedPathsNonAddressable()
    {
        var longKey = new string('K', 2_000);
        var observation = CreateObservation(new Dictionary<string, object>
        {
            [longKey] = 42
        });

        var node = observation.Detail.Children.Should().ContainSingle().Subject;
        node.Name.Should().EndWith("...");
        node.Addressable.Should().BeFalse();
        node.PathHint.Length.Should().BeLessThan(520);
        observation.Diagnostics.Should().Contain(item => item.Code == "path-limit");
    }

    [Fact]
    public void BuildLegacyOutputData_ShouldKeepSafeFieldsAndDowngradeUnsafeValues()
    {
        using var mat = new Mat(1, 1, MatType.CV_8UC1, Scalar.All(255));
        var circular = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        circular["Self"] = circular;
        var legacy = ExecutionObservationProjector.BuildLegacyOutputData(
            new Dictionary<string, object>
            {
                ["Image"] = new byte[] { 1, 2, 3 },
                ["Score"] = 0.95d,
                ["Seen"] = 7L,
                ["Values"] = new[] { 1, 2, 3 },
                ["Unsafe"] = new ThrowingGetterDto(),
                ["Loop"] = circular,
                ["Matrix"] = mat,
                ["BadNumber"] = double.PositiveInfinity
            },
            key => string.Equals(key, "Image", StringComparison.OrdinalIgnoreCase));

        legacy.Should().NotContainKey("Image");
        legacy["Score"].Should().Be(0.95d);
        legacy["Seen"].Should().Be(7L);
        legacy["Values"].Should().BeAssignableTo<List<object?>>();
        legacy["Unsafe"].Should().BeAssignableTo<Dictionary<string, object?>>();
        legacy["Loop"].Should().BeAssignableTo<Dictionary<string, object?>>();
        legacy["Matrix"].Should().BeAssignableTo<Dictionary<string, object?>>();
        legacy["BadNumber"].Should().BeAssignableTo<Dictionary<string, object?>>();
    }

    [Fact]
    public void BuildLegacyOutputData_ShouldApplyTopLevelAndJsonByteLimits()
    {
        var output = Enumerable.Range(0, 10_000)
            .ToDictionary(
                index => $"Field{index:D05}",
                _ => (object)new string('x', ExecutionObservationProjector.MaxStringChars * 4),
                StringComparer.OrdinalIgnoreCase);
        output[new string('K', 4_000)] = new string('y', ExecutionObservationProjector.MaxStringChars * 8);

        var legacy = ExecutionObservationProjector.BuildLegacyOutputData(output, _ => false);
        var jsonBytes = JsonSerializer.SerializeToUtf8Bytes(legacy, CamelCaseJson);

        legacy.Should().ContainKey("__truncated");
        legacy.Count.Should().BeLessThanOrEqualTo(ExecutionObservationProjector.MaxObjectFields + 1);
        jsonBytes.Length.Should().BeLessThanOrEqualTo(ExecutionObservationProjector.MaxLegacyOutputBytes);
    }

    [Fact]
    public void CreatePreviewObservation_ShouldProjectRoiRectangleVisualSceneFromParameters()
    {
        var targetOperator = CreateOperator(OperatorType.RoiManager, [
            new Parameter(Guid.NewGuid(), "Shape", "Shape", string.Empty, "enum", "Rectangle"),
            new Parameter(Guid.NewGuid(), "X", "X", string.Empty, "int", 12),
            new Parameter(Guid.NewGuid(), "Y", "Y", string.Empty, "int", 14),
            new Parameter(Guid.NewGuid(), "Width", "Width", string.Empty, "int", 80),
            new Parameter(Guid.NewGuid(), "Height", "Height", string.Empty, "int", 40)
        ]);

        var observation = CreateObservation(
            new Dictionary<string, object>
            {
                ["Width"] = 640,
                ["Height"] = 480
            },
            targetOperator: targetOperator);

        observation.VisualScene.Should().NotBeNull();
        var scene = observation.VisualScene!;
        scene.SchemaVersion.Should().Be("visual-scene.v1");
        scene.CoordinateSpace.Should().Be("image.pixel");
        scene.ImageWidth.Should().Be(640);
        scene.ImageHeight.Should().Be(480);
        scene.Primitives.Should().ContainSingle().Which.Should().Match<ExecutionVisualScenePrimitiveV1>(primitive =>
            primitive.Kind == "rectangle" &&
            primitive.Layer == "roi" &&
            primitive.Selectable == false &&
            primitive.Geometry.X == 12 &&
            primitive.Geometry.Y == 14 &&
            primitive.Geometry.Width == 80 &&
            primitive.Geometry.Height == 40 &&
            primitive.ResultPath == null);
    }

    [Fact]
    public void CreatePreviewObservation_ShouldProjectRoiCropSpatialContextBackToFullImage()
    {
        var localFrame = new FrameRefV1(
            "roi.local.test.image",
            SpatialFrameKindV1.RoiLocal,
            SpatialUnitV1.Pixel,
            "image.full");
        var spatialContext = new SpatialContextV1(
            localFrame,
            [
                SpatialTransform2DV1.Identity(FrameRefV1.ImageFull()),
                new SpatialTransform2DV1(
                    localFrame,
                    FrameRefV1.ImageFull(),
                    [
                        [1, 0, 5],
                        [0, 1, 6],
                        [0, 0, 1]
                    ])
            ]);
        var targetOperator = CreateOperator(OperatorType.RoiManager, [
            new Parameter(Guid.NewGuid(), "Shape", "Shape", string.Empty, "enum", "Circle"),
            new Parameter(Guid.NewGuid(), "Operation", "Operation", string.Empty, "enum", "Crop"),
            new Parameter(Guid.NewGuid(), "CenterX", "CenterX", string.Empty, "int", 7),
            new Parameter(Guid.NewGuid(), "CenterY", "CenterY", string.Empty, "int", 8),
            new Parameter(Guid.NewGuid(), "Radius", "Radius", string.Empty, "int", 2)
        ]);

        var observation = CreateObservation(
            new Dictionary<string, object>
            {
                ["Width"] = 4,
                ["Height"] = 3,
                ["ParentWidth"] = 20,
                ["ParentHeight"] = 18,
                [RoiManagerOperator.SpatialContextOutputKey] = JsonSerializer.SerializeToElement(spatialContext, CamelCaseJson)
            },
            targetOperator: targetOperator);

        observation.VisualScene.Should().NotBeNull();
        var scene = observation.VisualScene!;
        scene.ImageWidth.Should().Be(20);
        scene.ImageHeight.Should().Be(18);
        scene.Diagnostics.Should().BeEmpty();
        var circle = scene.Primitives.Should().ContainSingle(primitive => primitive.Kind == "circle").Subject;
        circle.PrimitiveId.Should().Be($"roi:circle:{targetOperator.Id:D}");
        circle.Geometry.CenterX.Should().Be(7);
        circle.Geometry.CenterY.Should().Be(8);
        circle.Geometry.Radius.Should().Be(2);
        circle.Selectable.Should().BeFalse();
        var bounds = scene.Primitives.Should().ContainSingle(primitive => primitive.PrimitiveId == $"roi:crop-bounds:{targetOperator.Id:D}").Subject;
        bounds.Kind.Should().Be("rectangle");
        bounds.Label.Should().Be("Crop Bounds");
        bounds.Geometry.X.Should().Be(5);
        bounds.Geometry.Y.Should().Be(6);
        bounds.Geometry.Width.Should().Be(4);
        bounds.Geometry.Height.Should().Be(3);
        bounds.Selectable.Should().BeFalse();
    }

    [Fact]
    public void CreatePreviewObservation_ShouldProjectPrimaryCircleWithoutMisusingRadiusMapping()
    {
        var radiusPortId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var targetOperator = CreateOperator(OperatorType.CircleMeasurement, []);
        var observation = CreateObservation(
            new Dictionary<string, object>
            {
                ["Center"] = new Position(50, 60),
                ["Radius"] = 12.5d,
                ["Width"] = 320,
                ["Height"] = 240
            },
            [
                new ExecutionObservationOutputPortV1 { Id = radiusPortId, Name = "Radius" }
            ],
            targetOperator);

        observation.VisualScene.Should().NotBeNull();
        var scene = observation.VisualScene!;
        var circle = scene.Primitives.Should().ContainSingle(primitive => primitive.Kind == "circle").Subject;
        circle.PrimitiveId.Should().Be("circle:primary");
        circle.Geometry.CenterX.Should().Be(50);
        circle.Geometry.CenterY.Should().Be(60);
        circle.Geometry.Radius.Should().Be(12.5d);
        circle.OutputPortId.Should().BeNull();
        circle.ResultPathVersion.Should().BeNull();
        circle.ResultPath.Should().BeNull();
        circle.Selectable.Should().BeFalse();
        FindNode(observation.Detail, "Radius")!.OutputPortId.Should().Be(radiusPortId);
    }

    [Fact]
    public void CreatePreviewObservation_ShouldMapCircleDataListItemsToCanonicalIndexPaths()
    {
        var circlesPortId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var targetOperator = CreateOperator(OperatorType.CircleMeasurement, []);
        var circles = new List<CircleData>
        {
            new(10, 20, 5),
            new(30, 40, 7)
        };
        var observation = CreateObservation(
            new Dictionary<string, object>
            {
                ["CircleDataList"] = circles,
                ["Width"] = 320,
                ["Height"] = 240
            },
            [
                new ExecutionObservationOutputPortV1 { Id = circlesPortId, Name = "CircleDataList" }
            ],
            targetOperator);

        observation.VisualScene.Should().NotBeNull();
        var scene = observation.VisualScene!;
        var first = scene.Primitives.Should().ContainSingle(primitive => primitive.PrimitiveId == "circle:data-list:0").Subject;
        first.OutputPortId.Should().Be(circlesPortId);
        first.ResultPathVersion.Should().Be(1);
        first.ResultPath.Should().Be("$[0]");
        first.Selectable.Should().BeTrue();
        var second = scene.Primitives.Should().ContainSingle(primitive => primitive.PrimitiveId == "circle:data-list:1").Subject;
        second.ResultPath.Should().Be("$[1]");
        second.Selectable.Should().BeTrue();

        var listNode = FindNode(observation.Detail, "CircleDataList")!;
        listNode.Locatable.Should().BeTrue();
        listNode.Addressable.Should().BeFalse();
        listNode.BindableVariableTypes.Should().BeNull();
        listNode.OutputPortId.Should().Be(circlesPortId);
        listNode.ResultPathVersion.Should().Be(1);
        listNode.ResultPath.Should().Be("$");

        var firstItemNode = FindNode(observation.Detail, "0")!;
        firstItemNode.Locatable.Should().BeTrue();
        firstItemNode.Addressable.Should().BeFalse();
        firstItemNode.BindableVariableTypes.Should().BeNull();
        firstItemNode.OutputPortId.Should().Be(circlesPortId);
        firstItemNode.ResultPathVersion.Should().Be(1);
        firstItemNode.ResultPath.Should().Be("$[0]");
    }

    [Fact]
    public void CreatePreviewObservation_ShouldMapCircleRootObjectToCanonicalRootPath()
    {
        var circlePortId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
        var targetOperator = CreateOperator(OperatorType.CircleMeasurement, []);
        var observation = CreateObservation(
            new Dictionary<string, object>
            {
                ["Circle"] = new CircleData(10, 20, 5),
                ["Width"] = 320,
                ["Height"] = 240
            },
            [
                new ExecutionObservationOutputPortV1 { Id = circlePortId, Name = "Circle" }
            ],
            targetOperator);

        var primitive = observation.VisualScene!.Primitives.Should().ContainSingle(item => item.PrimitiveId == "circle:primary").Subject;
        primitive.OutputPortId.Should().Be(circlePortId);
        primitive.ResultPathVersion.Should().Be(1);
        primitive.ResultPath.Should().Be("$");
        primitive.Selectable.Should().BeTrue();

        var detailNode = FindNode(observation.Detail, "Circle")!;
        detailNode.Locatable.Should().BeTrue();
        detailNode.Addressable.Should().BeFalse();
        detailNode.BindableVariableTypes.Should().BeNull();
        detailNode.OutputPortId.Should().Be(circlePortId);
        detailNode.ResultPathVersion.Should().Be(1);
        detailNode.ResultPath.Should().Be("$");
    }

    [Fact]
    public void CreatePreviewObservation_ShouldMapCirclesDictionaryListItemsToCanonicalIndexPaths()
    {
        var circlesPortId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
        var targetOperator = CreateOperator(OperatorType.CircleMeasurement, []);
        var observation = CreateObservation(
            new Dictionary<string, object>
            {
                ["Circles"] = new List<Dictionary<string, object>>
                {
                    new() { ["CenterX"] = 10d, ["CenterY"] = 20d, ["Radius"] = 5d },
                    new() { ["CenterX"] = 30d, ["CenterY"] = 40d, ["Radius"] = 7d }
                },
                ["Width"] = 320,
                ["Height"] = 240
            },
            [
                new ExecutionObservationOutputPortV1 { Id = circlesPortId, Name = "Circles" }
            ],
            targetOperator);

        var first = observation.VisualScene!.Primitives.Should().ContainSingle(primitive => primitive.PrimitiveId == "circle:circles:0").Subject;
        first.OutputPortId.Should().Be(circlesPortId);
        first.ResultPathVersion.Should().Be(1);
        first.ResultPath.Should().Be("$[0]");
        first.Selectable.Should().BeTrue();
        FindNode(observation.Detail, "0")!.ResultPath.Should().Be("$[0]");
    }

    [Fact]
    public void CreatePreviewObservation_ShouldDisableSceneSelectionWhenDetailBudgetOmitsLocator()
    {
        var circlesPortId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");
        var targetOperator = CreateOperator(OperatorType.CircleMeasurement, []);
        var circles = Enumerable.Range(0, ExecutionObservationProjector.MaxCollectionItems + 2)
            .Select(index => new CircleData(index + 10, index + 20, 5))
            .ToList();

        var observation = CreateObservation(
            new Dictionary<string, object>
            {
                ["CircleDataList"] = circles,
                ["Width"] = 320,
                ["Height"] = 240
            },
            [
                new ExecutionObservationOutputPortV1 { Id = circlesPortId, Name = "CircleDataList" }
            ],
            targetOperator);

        var retained = observation.VisualScene!.Primitives.Should().ContainSingle(primitive => primitive.PrimitiveId == "circle:data-list:0").Subject;
        retained.Selectable.Should().BeTrue();
        var omitted = observation.VisualScene!.Primitives.Should().ContainSingle(primitive =>
            primitive.PrimitiveId == $"circle:data-list:{ExecutionObservationProjector.MaxCollectionItems.ToString(CultureInfo.InvariantCulture)}").Subject;
        omitted.ResultPath.Should().Be($"$[{ExecutionObservationProjector.MaxCollectionItems.ToString(CultureInfo.InvariantCulture)}]");
        omitted.Selectable.Should().BeFalse();
        observation.VisualScene!.Diagnostics.Should().Contain(item => item.Code == "visual-scene-detail-locator-missing");
    }

    [Fact]
    public void CreatePreviewObservation_ShouldBoundSceneLocatorDiagnosticsForLargeCircleList()
    {
        var circlesPortId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");
        var targetOperator = CreateOperator(OperatorType.CircleMeasurement, []);
        var circles = Enumerable.Range(0, ExecutionVisualSceneProjector.MaxPrimitives)
            .Select(index => new CircleData(index + 10, index + 20, 5))
            .ToList();

        var first = CreateObservation(
            new Dictionary<string, object>
            {
                ["CircleDataList"] = circles,
                ["Width"] = 320,
                ["Height"] = 240
            },
            [
                new ExecutionObservationOutputPortV1 { Id = circlesPortId, Name = "CircleDataList" }
            ],
            targetOperator);
        var second = CreateObservation(
            new Dictionary<string, object>
            {
                ["CircleDataList"] = circles,
                ["Width"] = 320,
                ["Height"] = 240
            },
            [
                new ExecutionObservationOutputPortV1 { Id = circlesPortId, Name = "CircleDataList" }
            ],
            targetOperator);

        var scene = first.VisualScene!;
        scene.Primitives.Should().HaveCount(ExecutionVisualSceneProjector.MaxPrimitives);
        FindNode(first.Detail, "CircleDataList")!.Children.Should().HaveCount(ExecutionObservationProjector.MaxCollectionItems);
        scene.Primitives
            .Where(primitive => ParseCircleDataListIndex(primitive.ResultPath) >= ExecutionObservationProjector.MaxCollectionItems)
            .Should()
            .OnlyContain(primitive => primitive.Selectable == false);
        scene.Diagnostics.Should().HaveCount(ExecutionVisualSceneProjector.MaxDiagnostics);
        scene.Diagnostics.Should().Contain(item => item.Code == "visual-scene-detail-locator-diagnostics-truncated");
        scene.Truncated.Should().BeTrue();

        JsonSerializer.Serialize(first.VisualScene, CamelCaseJson)
            .Should()
            .Be(JsonSerializer.Serialize(second.VisualScene, CamelCaseJson));
    }

    [Fact]
    public void ReconcileVisualSceneWithDetail_ShouldRespectDiagnosticBudgetBoundary()
    {
        var outputPortId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");
        var detail = new ExecutionObservationDetailNodeV1 { Kind = "object", PathHint = "$" };
        var primitives = new List<ExecutionVisualScenePrimitiveV1>
        {
            CreateScenePrimitive("circle:missing:0", outputPortId, "$[0]"),
            CreateScenePrimitive("circle:missing:1", outputPortId, "$[1]")
        };

        var withSixtyThree = ReconcileVisualSceneForTest(
            CreateVisualSceneForTest(primitives, existingDiagnosticCount: 63),
            detail);
        withSixtyThree.Diagnostics.Should().HaveCount(ExecutionVisualSceneProjector.MaxDiagnostics);
        withSixtyThree.Diagnostics.Last().Code.Should().Be("visual-scene-detail-locator-diagnostics-truncated");
        withSixtyThree.Diagnostics.Last().Message.Should().Contain("2");
        withSixtyThree.Truncated.Should().BeTrue();

        var withSixtyFour = ReconcileVisualSceneForTest(
            CreateVisualSceneForTest(primitives, existingDiagnosticCount: 64),
            detail);
        withSixtyFour.Diagnostics.Should().HaveCount(ExecutionVisualSceneProjector.MaxDiagnostics);
        withSixtyFour.Diagnostics.Should().OnlyContain(item => item.Code.StartsWith("existing-", StringComparison.Ordinal));
        withSixtyFour.Truncated.Should().BeTrue();

        var withSixtyFive = ReconcileVisualSceneForTest(
            CreateVisualSceneForTest([], existingDiagnosticCount: 65),
            detail);
        withSixtyFive.Diagnostics.Should().HaveCount(ExecutionVisualSceneProjector.MaxDiagnostics);
        withSixtyFive.Diagnostics.Last().Code.Should().Be("existing-63");
        withSixtyFive.Truncated.Should().BeTrue();
    }

    [Fact]
    public void CreatePreviewObservation_ShouldProjectNPointImageSamplesWithoutWorldProjection()
    {
        var targetOperator = CreateOperator(OperatorType.NPointCalibration, [
            new Parameter(
                Guid.NewGuid(),
                "PointPairs",
                "PointPairs",
                string.Empty,
                "string",
                """
                [
                  {"ImagePoint":{"X":10,"Y":20},"WorldPoint":{"X":1,"Y":2}},
                  {"ImageX":30,"ImageY":40,"WorldX":3,"WorldY":4},
                  {"ImageX":50,"ImageY":60,"WorldX":5,"WorldY":6,"Enabled":false}
                ]
                """)
        ]);

        var observation = CreateObservation(new Dictionary<string, object>(), targetOperator: targetOperator);

        observation.VisualScene.Should().NotBeNull();
        var scene = observation.VisualScene!;
        scene.Primitives.Should().Contain(primitive => primitive.Kind == "polyline");
        scene.Primitives.Where(primitive => primitive.Kind == "point").Should().HaveCount(2);
        scene.Primitives.Where(primitive => primitive.Kind == "text").Should().HaveCount(2);
        scene.Primitives.Should().OnlyContain(primitive =>
            primitive.ResultPath == null && primitive.OutputPortId == null && primitive.Selectable == false);
    }

    [Fact]
    public void CreatePreviewObservation_ShouldProjectNPointDraftSceneLayersAndResultPaths()
    {
        var draftPortId = Guid.Parse("aaaaaaaa-1111-1111-1111-111111111111");
        var targetOperator = CreateOperator(OperatorType.NPointCalibration, []);
        var draft = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
        {
            ["SchemaVersion"] = "calibration-draft-session.v1",
            ["SessionId"] = "session-scene",
            ["Mode"] = "Affine",
            ["Unit"] = "mm",
            ["Status"] = "Solved",
            ["LastSolveResult"] = new Dictionary<string, object>
            {
                ["Accepted"] = true,
                ["InlierCount"] = 1,
                ["MeanError"] = 0.25,
                ["MaxError"] = 4.2
            },
            ["Samples"] = new List<Dictionary<string, object>>
            {
                new()
                {
                    ["SampleId"] = "s1",
                    ["Order"] = 1,
                    ["PixelX"] = 10d,
                    ["PixelY"] = 20d,
                    ["WorldX"] = 1d,
                    ["WorldY"] = 2d,
                    ["Enabled"] = true,
                    ["Inlier"] = true,
                    ["ReprojectionX"] = 10.5d,
                    ["ReprojectionY"] = 20.25d,
                    ["Error"] = 0.56d
                },
                new()
                {
                    ["SampleId"] = "s2",
                    ["Order"] = 2,
                    ["PixelX"] = 30d,
                    ["PixelY"] = 40d,
                    ["WorldX"] = 3d,
                    ["WorldY"] = 4d,
                    ["Enabled"] = false,
                    ["Inlier"] = false,
                    ["ReprojectionX"] = 33d,
                    ["ReprojectionY"] = 43d,
                    ["Error"] = 4.2d
                }
            }
        };

        var observation = CreateObservation(
            new Dictionary<string, object>
            {
                ["Width"] = 320,
                ["Height"] = 240,
                ["CalibrationDraft"] = draft
            },
            [
                new ExecutionObservationOutputPortV1
                {
                    Id = draftPortId,
                    Name = "CalibrationDraft"
                }
            ],
            targetOperator);

        observation.VisualScene.Should().NotBeNull();
        var scene = observation.VisualScene!;
        scene.Primitives.Select(primitive => primitive.Layer).Should().Contain(new[]
        {
            "calibration-samples",
            "calibration-disabled",
            "calibration-inliers",
            "calibration-outliers",
            "calibration-reprojection",
            "calibration-error-vectors",
            "calibration-labels",
            "calibration-quality"
        });

        var firstSample = scene.Primitives.Should()
            .ContainSingle(primitive => primitive.PrimitiveId == "calibration-draft:session-scene:s1:calibration-samples")
            .Subject;
        firstSample.OutputPortId.Should().Be(draftPortId);
        firstSample.ResultPathVersion.Should().Be(1);
        firstSample.ResultPath.Should().Be("$[\"Samples\"][0]");
        firstSample.Selectable.Should().BeTrue();

        scene.Primitives.Should().Contain(primitive =>
            primitive.PrimitiveId == "calibration-draft:session-scene:s2:calibration-error-vectors" &&
            primitive.ResultPath == "$[\"Samples\"][1]");
        scene.Primitives.Should().ContainSingle(primitive =>
            primitive.Layer == "calibration-quality" &&
            primitive.Selectable == false &&
            primitive.Geometry.Text!.Contains("accepted=true", StringComparison.Ordinal));
    }

    [Fact]
    public void CreatePreviewObservation_ShouldFailSoftAndTruncateInvalidVisualScenePrimitives()
    {
        var targetOperator = CreateOperator(OperatorType.CircleMeasurement, []);
        var circles = Enumerable.Range(0, ExecutionVisualSceneProjector.MaxPrimitives + 20)
            .Select(index => new CircleData(index + 1, index + 2, 5))
            .ToList();
        circles.Insert(0, new CircleData(10, 10, -1));

        var observation = CreateObservation(
            new Dictionary<string, object>
            {
                ["CircleDataList"] = circles
            },
            targetOperator: targetOperator);

        observation.Outcome.Success.Should().BeTrue();
        observation.VisualScene.Should().NotBeNull();
        var scene = observation.VisualScene!;
        scene.Primitives.Count.Should().Be(ExecutionVisualSceneProjector.MaxPrimitives);
        scene.Truncated.Should().BeTrue();
        scene.Diagnostics.Should().Contain(item => item.Code == "visual-scene-geometry-invalid");
        scene.Diagnostics.Should().Contain(item => item.Code == "visual-scene-primitive-limit");
    }

    [Fact]
    public void CreatePreviewObservation_ShouldProjectCaliperFitV2SuccessVisualScene()
    {
        var edgePortId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
        var inlierPortId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002");
        var circlePortId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000003");
        var targetOperator = CreateCaliperFitV2Operator();
        var edgePoints = Enumerable.Range(0, 8)
            .Select(index => CreateCaliperPoint(index, 100, 90, 30))
            .ToArray();
        var result = new CircleCaliperFitV2Result
        {
            Success = true,
            CenterX = 100,
            CenterY = 90,
            Radius = 30,
            EdgePoints = edgePoints,
            InlierPoints = edgePoints.Take(6).ToArray(),
            OutlierPoints = edgePoints.Skip(6).Take(1).ToArray(),
            CoverageRatio = 0.82,
            ResidualRmse = 0.41,
            ResidualMax = 0.9,
            CollectedPointCount = edgePoints.Length
        };

        var observation = CreateObservation(
            new Dictionary<string, object>
            {
                ["Width"] = 240,
                ["Height"] = 180,
                ["Circle"] = new CircleData(100, 90, 30),
                ["CaliperFitV2Result"] = result,
                ["EdgePoints"] = edgePoints.Select(point => new Position(point.X, point.Y)).ToList(),
                ["InlierPoints"] = result.InlierPoints.Select(point => new Position(point.X, point.Y)).ToList(),
                ["OutlierPoints"] = result.OutlierPoints.Select(point => new Position(point.X, point.Y)).ToList()
            },
            [
                new ExecutionObservationOutputPortV1 { Id = edgePortId, Name = "EdgePoints" },
                new ExecutionObservationOutputPortV1 { Id = inlierPortId, Name = "InlierPoints" },
                new ExecutionObservationOutputPortV1 { Id = circlePortId, Name = "Circle" }
            ],
            targetOperator);

        var scene = observation.VisualScene!;
        scene.Primitives.Should().Contain(primitive => primitive.PrimitiveId.StartsWith("circle-search-region:min:", StringComparison.Ordinal));
        scene.Primitives.Should().Contain(primitive => primitive.PrimitiveId.StartsWith("circle-search-nominal:", StringComparison.Ordinal));
        scene.Primitives.Should().Contain(primitive => primitive.Layer == "circle-search-calipers" && primitive.Kind == "polyline");
        var fit = scene.Primitives.Should().ContainSingle(primitive => primitive.PrimitiveId.StartsWith("circle-search-fit:", StringComparison.Ordinal)).Subject;
        fit.OutputPortId.Should().Be(circlePortId);
        fit.ResultPath.Should().Be("$");
        fit.Selectable.Should().BeTrue();

        var accepted = scene.Primitives.Single(primitive => primitive.Layer == "circle-search-accepted" && primitive.ResultPath == "$[0]");
        accepted.OutputPortId.Should().Be(inlierPortId);
        accepted.ResultPathVersion.Should().Be(1);
        accepted.ResultPath.Should().Be("$[0]");
        accepted.Selectable.Should().BeTrue();
        scene.Diagnostics.Should().NotContain(item => item.Code == "visual-scene-circle-output-missing");
    }

    [Fact]
    public void CreatePreviewObservation_ShouldUseLegacyCircleSceneWhenCircleSearchV2FeatureIsOff()
    {
        var targetOperator = CreateCaliperFitV2Operator();
        var result = new CircleCaliperFitV2Result
        {
            Success = true,
            CenterX = 100,
            CenterY = 90,
            Radius = 30,
            EdgePoints = [CreateCaliperPoint(0, 100, 90, 30)]
        };

        var observation = CreateObservation(
            new Dictionary<string, object>
            {
                ["Circle"] = new CircleData(100, 90, 30),
                ["CaliperFitV2Result"] = result
            },
            targetOperator: targetOperator,
            featureFlags: new Dictionary<string, bool>
            {
                ["Studio:CircleSearchV2ToolEnabled"] = false
            });

        var scene = observation.VisualScene!;
        scene.Primitives.Should().ContainSingle(primitive => primitive.PrimitiveId == "circle:primary");
        scene.Primitives.Should().NotContain(primitive => primitive.PrimitiveId.StartsWith("circle-search-", StringComparison.Ordinal));
    }

    [Fact]
    public void CreatePreviewObservation_ShouldProjectCaliperFitV2FailureEvidenceWithoutFitCircle()
    {
        var targetOperator = CreateCaliperFitV2Operator();
        var edgePoints = Enumerable.Range(0, 6)
            .Select(index => CreateCaliperPoint(index, 100, 90, 30))
            .ToArray();
        var result = new CircleCaliperFitV2Result
        {
            Success = false,
            FailureCode = CircleCaliperFitV2FailureCode.InsufficientCoverage,
            FailureMessage = "coverage too low",
            EdgePoints = edgePoints,
            InlierPoints = edgePoints.Take(3).ToArray(),
            OutlierPoints = edgePoints.Skip(3).Take(2).ToArray(),
            CoverageRatio = 0.2,
            CollectedPointCount = edgePoints.Length
        };

        var observation = CreateObservation(
            new Dictionary<string, object>
            {
                ["Width"] = 240,
                ["Height"] = 180,
                ["CaliperFitV2Result"] = result,
                ["EdgePoints"] = edgePoints.Select(point => new Position(point.X, point.Y)).ToList(),
                ["InlierPoints"] = result.InlierPoints.Select(point => new Position(point.X, point.Y)).ToList(),
                ["OutlierPoints"] = result.OutlierPoints.Select(point => new Position(point.X, point.Y)).ToList()
            },
            targetOperator: targetOperator);

        var scene = observation.VisualScene!;
        scene.Primitives.Should().Contain(primitive => primitive.Layer == "circle-search-region");
        scene.Primitives.Should().Contain(primitive => primitive.Layer == "circle-search-candidates");
        scene.Primitives.Should().Contain(primitive => primitive.Layer == "circle-search-accepted");
        scene.Primitives.Should().Contain(primitive => primitive.Layer == "circle-search-rejected");
        scene.Primitives.Should().NotContain(primitive => primitive.PrimitiveId.StartsWith("circle-search-fit:", StringComparison.Ordinal));
        scene.Primitives.Should().Contain(primitive =>
            primitive.Layer == "circle-search-quality" &&
            primitive.Geometry.Text!.Contains("InsufficientCoverage", StringComparison.Ordinal));
    }

    [Fact]
    public void CreatePreviewObservation_ShouldProjectBoundedCaliperFitV2SummaryInsteadOfRawTypedResult()
    {
        var targetOperator = CreateCaliperFitV2Operator();
        var edgePoints = Enumerable.Range(0, 8)
            .Select(index => CreateCaliperPoint(index, 100, 90, 30))
            .ToArray();
        var result = new CircleCaliperFitV2Result
        {
            Success = true,
            CenterX = 100,
            CenterY = 90,
            Radius = 30,
            EdgePoints = edgePoints,
            InlierPoints = edgePoints.Take(6).ToArray(),
            OutlierPoints = edgePoints.Skip(6).Take(1).ToArray(),
            CoverageRatio = 0.82,
            AngularCoverageDegrees = 270,
            ResidualRmse = 0.41,
            ResidualMax = 0.9,
            CollectedPointCount = edgePoints.Length,
            ValidCaliperCount = 6,
            RejectedCaliperCount = 2,
            ResolvedPolarity = CircleCaliperFitV2EdgePolarity.LightToDark,
            Confidence = 0.72,
            UncertaintyPx = 0.44,
            Diagnostics =
            [
                new CircleCaliperFitV2Diagnostic("fit.residualRmse", "Final residual RMSE.", 0.41)
            ],
            ProfileEvidence =
            [
                new CircleCaliperFitV2ProfileEvidence(
                    CircleCaliperFitV2ProfileEvidence.ContractVersionValue,
                    0,
                    0,
                    80,
                    90,
                    120,
                    90,
                    129,
                    3,
                    4,
                    64,
                    18,
                    "LightToDark",
                    [1.0, 2.0, 3.0])
            ]
        };

        var observation = CreateObservation(
            new Dictionary<string, object>
            {
                ["Width"] = 240,
                ["Height"] = 180,
                ["CaliperFitV2Result"] = result
            },
            targetOperator: targetOperator);

        var summary = observation.Detail.Children.Should().ContainSingle(child => child.Name == "CaliperFitV2Result").Subject;
        summary.Kind.Should().Be("caliperFitV2Summary");
        summary.Truncated.Should().BeTrue();
        summary.Children.Select(child => child.Name).Should().Contain(new[]
        {
            "Success",
            "FailureCode",
            "Circle",
            "PointCount",
            "CoverageRatio",
            "ResidualRmse",
            "Polarity",
            "Confidence",
            "UncertaintyPx",
            "ProfileEvidenceCount",
            "Diagnostics"
        });
        summary.Children.Select(child => child.Name).Should().NotContain(new[] { "EdgePoints", "InlierPoints", "OutlierPoints" });
    }

    [Fact]
    public void CreatePreviewObservation_ShouldDownsampleCaliperFitV2SceneWithinPrimitiveBudget()
    {
        var edgePortId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
        var targetOperator = CreateCaliperFitV2Operator(caliperCount: 720);
        var edgePoints = Enumerable.Range(0, 720)
            .Select(index => CreateCaliperPoint(index, 100, 90, 30))
            .ToArray();
        var result = new CircleCaliperFitV2Result
        {
            Success = false,
            FailureCode = CircleCaliperFitV2FailureCode.InsufficientCoverage,
            EdgePoints = edgePoints,
            InlierPoints = edgePoints.Take(220).ToArray(),
            OutlierPoints = edgePoints.Skip(220).Take(140).ToArray(),
            CollectedPointCount = edgePoints.Length
        };

        var observation = CreateObservation(
            new Dictionary<string, object>
            {
                ["Width"] = 240,
                ["Height"] = 180,
                ["CaliperFitV2Result"] = result,
                ["EdgePoints"] = edgePoints.Select(point => new Position(point.X, point.Y)).ToList(),
                ["InlierPoints"] = result.InlierPoints.Select(point => new Position(point.X, point.Y)).ToList(),
                ["OutlierPoints"] = result.OutlierPoints.Select(point => new Position(point.X, point.Y)).ToList()
            },
            [
                new ExecutionObservationOutputPortV1 { Id = edgePortId, Name = "EdgePoints" }
            ],
            targetOperator: targetOperator);

        var scene = observation.VisualScene!;
        scene.Primitives.Should().HaveCountLessThanOrEqualTo(ExecutionVisualSceneProjector.MaxPrimitives);
        scene.Primitives.Count(primitive => primitive.Layer == "circle-search-calipers").Should().BeLessThanOrEqualTo(48);
        var candidatePrimitives = scene.Primitives
            .Where(primitive => primitive.Layer == "circle-search-candidates")
            .ToArray();
        var candidateCaliperIndexes = candidatePrimitives
            .Select(primitive => int.Parse(primitive.PrimitiveId.Split(':').Last(), CultureInfo.InvariantCulture))
            .ToArray();
        candidateCaliperIndexes.Should().Contain(0);
        candidateCaliperIndexes.Max().Should().BeGreaterThan(650);
        candidateCaliperIndexes.Should().Contain(index => index > 80);
        var highCandidate = candidatePrimitives
            .OrderByDescending(primitive => int.Parse(primitive.PrimitiveId.Split(':').Last(), CultureInfo.InvariantCulture))
            .First();
        highCandidate.ResultPath.Should().Be($"$[{candidateCaliperIndexes.Max().ToString(CultureInfo.InvariantCulture)}]");
        scene.Truncated.Should().BeTrue();
        scene.Diagnostics.Should().Contain(item => item.Code == "visual-scene-circle-calipers-truncated");
    }

    [Fact]
    public void CreatePreviewObservation_ShouldProjectPixelToWorldPointsOnWorldNeutralPlane()
    {
        var targetOperator = CreateOperator(OperatorType.PixelToWorldTransform, []);
        var transformedPortId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var outputPorts = new[]
        {
            new ExecutionObservationOutputPortV1
            {
                Id = transformedPortId,
                Name = "TransformedPoints"
            }
        };
        var worldPoints = new List<Point3d>
        {
            new(-12.5, 4.0, 0),
            new(25.0, -8.0, 0)
        };

        var observation = CreateObservation(
            new Dictionary<string, object>
            {
                ["Width"] = 320,
                ["Height"] = 240,
                ["TransformedPoints"] = worldPoints,
                ["TransformResult"] = new Dictionary<string, object>
                {
                    ["TransformMode"] = "PixelToWorld",
                    ["InputFrame"] = "roi.local.depth2",
                    ["CalibrationSourceFrame"] = "image.full",
                    ["CalibrationTargetFrame"] = "world.2d",
                    ["OutputFrame"] = "world.2d",
                    ["OutputUnit"] = "mm",
                    ["BundleId"] = "bundle-world",
                    ["Diagnostics"] = new List<string> { "Compatibility frame mapping: 'image' -> 'image.full' (ImageFull, px)." }
                }
            },
            outputPorts,
            targetOperator);

        observation.VisualScene.Should().NotBeNull();
        var scene = observation.VisualScene!;
        scene.CoordinateSpace.Should().Be("world.2d.neutral-plane");
        scene.FrameId.Should().Be("world.2d");
        scene.FrameKind.Should().Be("World2D");
        scene.Unit.Should().Be("mm");
        scene.ImageWidth.Should().Be(512);
        scene.ImageHeight.Should().Be(512);
        scene.WorldMinX.Should().Be(-12.5);
        scene.WorldMaxX.Should().Be(25.0);
        scene.WorldMinY.Should().Be(-8.0);
        scene.WorldMaxY.Should().Be(4.0);
        scene.Diagnostics.Should().Contain(item =>
            item.Code == "visual-scene-pixel-to-world-world2d" &&
            item.Message.Contains("BundleId=bundle-world", StringComparison.Ordinal));
        scene.Diagnostics.Should().Contain(item => item.Code == "visual-scene-frame-compatibility");

        var first = scene.Primitives.Should().ContainSingle(primitive => primitive.PrimitiveId == "pixel-to-world:world-point:0").Subject;
        first.Selectable.Should().BeTrue();
        first.OutputPortId.Should().Be(transformedPortId);
        first.ResultPathVersion.Should().Be(1);
        first.ResultPath.Should().Be("$[0]");
        first.FrameId.Should().Be("world.2d");
        first.Unit.Should().Be("mm");
        first.Geometry.WorldX.Should().Be(-12.5);
        first.Geometry.WorldY.Should().Be(4.0);
        first.Geometry.X.Should().BeGreaterThanOrEqualTo(0);
        first.Geometry.Y.Should().BeGreaterThanOrEqualTo(0);
        first.Geometry.X.Should().BeLessThanOrEqualTo(512);
        first.Geometry.Y.Should().BeLessThanOrEqualTo(512);
    }

    [Fact]
    public void CreatePreviewObservation_ShouldProjectWorldToPixelPointsInImageCoordinateSpace()
    {
        var targetOperator = CreateOperator(OperatorType.PixelToWorldTransform, []);
        var transformedPortId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var outputPorts = new[]
        {
            new ExecutionObservationOutputPortV1
            {
                Id = transformedPortId,
                Name = "TransformedPoints"
            }
        };
        var pixelPoints = new List<Position> { new(42.0, 17.0) };

        var observation = CreateObservation(
            new Dictionary<string, object>
            {
                ["Width"] = 320,
                ["Height"] = 240,
                ["TransformedPoints"] = pixelPoints,
                ["TransformResult"] = new Dictionary<string, object>
                {
                    ["TransformMode"] = "WorldToPixel",
                    ["InputFrame"] = "world.2d",
                    ["CalibrationSourceFrame"] = "image.full",
                    ["CalibrationTargetFrame"] = "world.2d",
                    ["OutputFrame"] = "image.full",
                    ["OutputUnit"] = "px",
                    ["BundleId"] = "bundle-world-to-pixel"
                }
            },
            outputPorts,
            targetOperator);

        observation.VisualScene.Should().NotBeNull();
        var scene = observation.VisualScene!;
        scene.CoordinateSpace.Should().Be("image.pixel");
        scene.ImageWidth.Should().Be(320);
        scene.ImageHeight.Should().Be(240);
        scene.Diagnostics.Should().Contain(item => item.Code == "visual-scene-pixel-to-world-image-frame");
        var primitive = scene.Primitives.Should().ContainSingle().Subject;
        primitive.OutputPortId.Should().Be(transformedPortId);
        primitive.ResultPath.Should().Be("$[0]");
        primitive.FrameId.Should().Be("image.full");
        primitive.Unit.Should().Be("px");
        primitive.Geometry.X.Should().Be(42.0);
        primitive.Geometry.Y.Should().Be(17.0);
    }

    [Fact]
    public void CreatePreviewObservation_ShouldProjectCommonFiniteCollectionsWithStructuredCounts()
    {
        var readOnly = new StableReadOnlyCollection<int>([1, 2, 3, 4, 5]);
        var values = new Dictionary<string, object>
        {
            ["List"] = new List<int> { 1, 2, 3, 4, 5 },
            ["Array"] = new[] { 1, 2, 3, 4, 5 },
            ["IList"] = new ArrayList { 1, 2, 3, 4, 5 },
            ["ReadOnly"] = readOnly,
            ["Long"] = Enumerable.Range(0, 120).ToArray()
        };

        var observation = CreateObservation(values);

        foreach (var name in new[] { "List", "Array", "IList", "ReadOnly" })
        {
            var node = FindNode(observation.Detail, name)!;
            node.SemanticKind.Should().Be("collection");
            node.VisibleItemCount.Should().Be(5);
            node.TotalItemCount.Should().Be(5);
            node.Children.Should().HaveCount(5);
        }

        var longNode = FindNode(observation.Detail, "Long")!;
        longNode.VisibleItemCount.Should().Be(ExecutionObservationProjector.MaxCollectionItems);
        longNode.TotalItemCount.Should().Be(120);
        longNode.Truncated.Should().BeTrue();
        readOnly.MoveNextCount.Should().Be(5);
    }

    [Fact]
    public void CreatePreviewObservation_ShouldPreferDeclaredPortSemanticsWithoutOutputNameHeuristics()
    {
        var blobPort = Guid.NewGuid();
        var featuresPort = Guid.NewGuid();
        var pointsPort = Guid.NewGuid();
        var detectionsPort = Guid.NewGuid();
        var pointPort = Guid.NewGuid();
        var rectanglePort = Guid.NewGuid();
        var regionPort = Guid.NewGuid();
        var observation = CreateObservation(
            new Dictionary<string, object>
            {
                ["BusinessA"] = Enumerable.Range(0, 5).Select(index => new Dictionary<string, object> { ["Id"] = index }).ToList(),
                ["BusinessB"] = new List<Dictionary<string, object>>(),
                ["BusinessC"] = Enumerable.Range(0, 5).Select(index => new Position(index, index + 1)).ToList(),
                ["BusinessD"] = new DetectionList(Enumerable.Range(0, 5)
                    .Select(index => new DetectionResult($"d{index}", 0.9f, index, index, 2, 3))
                    .ToList()),
                ["GeometryA"] = new Position(12.5, 7.25),
                ["GeometryB"] = new Dictionary<string, object> { ["X"] = 1, ["Y"] = 2, ["Width"] = 30, ["Height"] = 40 },
                ["GeometryC"] = new ClearVision.Product.Core.ValueObjects.Region([
                    new RunLength(2, 3, 7),
                    new RunLength(3, 3, 7)
                ])
            },
            [
                new ExecutionObservationOutputPortV1 { Id = blobPort, Name = "BusinessA", DataType = PortDataType.BlobList },
                new ExecutionObservationOutputPortV1 { Id = featuresPort, Name = "BusinessB", DataType = PortDataType.BlobFeatureList },
                new ExecutionObservationOutputPortV1 { Id = pointsPort, Name = "BusinessC", DataType = PortDataType.PointList },
                new ExecutionObservationOutputPortV1 { Id = detectionsPort, Name = "BusinessD", DataType = PortDataType.DetectionList },
                new ExecutionObservationOutputPortV1 { Id = pointPort, Name = "GeometryA", DataType = PortDataType.Point },
                new ExecutionObservationOutputPortV1 { Id = rectanglePort, Name = "GeometryB", DataType = PortDataType.Rectangle },
                new ExecutionObservationOutputPortV1 { Id = regionPort, Name = "GeometryC", DataType = PortDataType.Region }
            ]);

        FindNode(observation.Detail, "BusinessA")!.Should().Match<ExecutionObservationDetailNodeV1>(node =>
            node.SemanticKind == "blob-list" && node.TotalItemCount == 5 && node.DeclaredPortDataType == "BlobList");
        FindNode(observation.Detail, "BusinessB")!.Should().Match<ExecutionObservationDetailNodeV1>(node =>
            node.SemanticKind == "blob-feature-list" && node.TotalItemCount == 0);
        FindNode(observation.Detail, "BusinessC")!.Should().Match<ExecutionObservationDetailNodeV1>(node =>
            node.SemanticKind == "collection" && node.TotalItemCount == 5);
        FindNode(observation.Detail, "BusinessD")!.Should().Match<ExecutionObservationDetailNodeV1>(node =>
            node.SemanticKind == "detection-list" && node.TotalItemCount == 5 && node.Children.Count == 2);
        FindNode(observation.Detail, "GeometryA")!.DisplayValue.Should().Be("(12.5, 7.25)");
        FindNode(observation.Detail, "GeometryB")!.DisplayValue.Should().Be("1, 2, 30 x 40");
        FindNode(observation.Detail, "GeometryC")!.Should().Match<ExecutionObservationDetailNodeV1>(node =>
            node.Kind == "region" && node.SemanticKind == "geometry" && node.DisplayValue!.Contains("Area 10"));
    }

    private static ExecutionObservationEnvelopeV1 CreateObservation(
        IReadOnlyDictionary<string, object> outputData,
        IReadOnlyList<ExecutionObservationOutputPortV1>? outputPorts = null,
        Operator? targetOperator = null,
        IReadOnlyDictionary<string, bool>? featureFlags = null)
    {
        return ExecutionObservationProjector.CreatePreviewObservation(new ExecutionObservationPreviewInput
        {
            ProjectId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            TargetNodeId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
            DebugSessionId = Guid.Parse("33333333-3333-3333-3333-333333333333"),
            ClientRequestSequence = 1,
            FlowRevision = 2,
            Success = true,
            ExecutionTimeMs = 5,
            ExecutedOperatorCount = 1,
            OutputData = outputData,
            OutputPorts = outputPorts ?? [],
            TargetOperator = targetOperator,
            FeatureFlags = featureFlags ?? new Dictionary<string, bool>(),
            ObservedAtUtc = DateTimeOffset.Parse("2026-07-02T01:02:03Z")
        });
    }

    private static Operator CreateOperator(OperatorType type, IEnumerable<Parameter> parameters)
    {
        var @operator = new Operator(Guid.Parse("22222222-2222-2222-2222-222222222222"), type.ToString(), type, 0, 0);
        foreach (var parameter in parameters)
        {
            @operator.AddParameter(parameter);
        }

        return @operator;
    }

    private static Operator CreateCaliperFitV2Operator(int caliperCount = 96) =>
        CreateOperator(OperatorType.CircleMeasurement, [
            new Parameter(Guid.NewGuid(), "Method", "Method", string.Empty, "enum", "CaliperFitV2"),
            new Parameter(Guid.NewGuid(), "SearchCenterMode", "SearchCenterMode", string.Empty, "enum", "Explicit"),
            new Parameter(Guid.NewGuid(), "SearchCenterX", "SearchCenterX", string.Empty, "double", 100.0),
            new Parameter(Guid.NewGuid(), "SearchCenterY", "SearchCenterY", string.Empty, "double", 90.0),
            new Parameter(Guid.NewGuid(), "MinRadius", "MinRadius", string.Empty, "int", 20),
            new Parameter(Guid.NewGuid(), "NominalRadius", "NominalRadius", string.Empty, "double", 30.0),
            new Parameter(Guid.NewGuid(), "MaxRadius", "MaxRadius", string.Empty, "int", 40),
            new Parameter(Guid.NewGuid(), "CaliperCount", "CaliperCount", string.Empty, "int", caliperCount)
        ]);

    private static CircleCaliperFitV2Point CreateCaliperPoint(int index, double centerX, double centerY, double radius)
    {
        var angle = index * 13.0;
        var radians = angle * Math.PI / 180.0;
        return new CircleCaliperFitV2Point(
            centerX + (Math.Cos(radians) * radius),
            centerY + (Math.Sin(radians) * radius),
            index,
            angle,
            radius,
            24.0,
            "LightToDark");
    }

    private static int ParseCircleDataListIndex(string? resultPath)
    {
        if (string.IsNullOrWhiteSpace(resultPath) ||
            !resultPath.StartsWith("$[", StringComparison.Ordinal) ||
            !resultPath.EndsWith(']'))
        {
            return -1;
        }

        return int.TryParse(resultPath[2..^1], NumberStyles.None, CultureInfo.InvariantCulture, out var index)
            ? index
            : -1;
    }

    private static ExecutionVisualSceneV1 ReconcileVisualSceneForTest(
        ExecutionVisualSceneV1 visualScene,
        ExecutionObservationDetailNodeV1 detail)
    {
        var method = typeof(ExecutionObservationProjector).GetMethod(
            "ReconcileVisualSceneWithDetail",
            BindingFlags.Static | BindingFlags.NonPublic)!;
        return (ExecutionVisualSceneV1)method.Invoke(null, [visualScene, detail])!;
    }

    private static ExecutionVisualSceneV1 CreateVisualSceneForTest(
        IReadOnlyList<ExecutionVisualScenePrimitiveV1> primitives,
        int existingDiagnosticCount) =>
        new()
        {
            Primitives = primitives.ToList(),
            Diagnostics = Enumerable.Range(0, existingDiagnosticCount)
                .Select(index => new ExecutionVisualSceneDiagnosticV1
                {
                    Code = $"existing-{index.ToString("D2", CultureInfo.InvariantCulture)}",
                    Message = "existing diagnostic"
                })
                .ToList()
        };

    private static ExecutionVisualScenePrimitiveV1 CreateScenePrimitive(
        string primitiveId,
        Guid outputPortId,
        string resultPath) =>
        new()
        {
            PrimitiveId = primitiveId,
            Kind = "circle",
            Layer = "measurement",
            ZOrder = 10,
            Visible = true,
            Selectable = true,
            Geometry = new ExecutionVisualSceneGeometryV1
            {
                CenterX = 10,
                CenterY = 20,
                Radius = 5
            },
            Style = new ExecutionVisualSceneStyleV1(),
            OutputPortId = outputPortId,
            ResultPathVersion = 1,
            ResultPath = resultPath
        };

    private static ExecutionObservationDetailNodeV1? FindNode(ExecutionObservationDetailNodeV1 root, string name)
    {
        if (string.Equals(root.Name, name, StringComparison.Ordinal))
        {
            return root;
        }

        foreach (var child in root.Children)
        {
            var found = FindNode(child, name);
            if (found != null)
            {
                return found;
            }
        }

        return null;
    }

    private static IEnumerable<ExecutionObservationDetailNodeV1> FindNodes(ExecutionObservationDetailNodeV1 root, string name)
    {
        if (string.Equals(root.Name, name, StringComparison.Ordinal))
        {
            yield return root;
        }

        foreach (var child in root.Children)
        {
            foreach (var found in FindNodes(child, name))
            {
                yield return found;
            }
        }
    }

    private sealed class ThrowingGetterDto
    {
        public int Safe => 1;

        public int Explodes => throw new InvalidOperationException("getter failed");
    }

    private sealed class ThrowingToStringValue
    {
        public override string ToString() => throw new InvalidOperationException("ToString failed");
    }

    private sealed class ThrowingEnumerable : IEnumerable
    {
        public int GetEnumeratorCallCount { get; private set; }

        public IEnumerator GetEnumerator()
        {
            GetEnumeratorCallCount++;
            throw new InvalidOperationException("enumeration failed");
        }
    }

    private sealed class CountingInfiniteEnumerable : IEnumerable
    {
        public int MoveNextCount { get; private set; }

        public IEnumerator GetEnumerator()
        {
            while (true)
            {
                MoveNextCount++;
                yield return MoveNextCount;
            }
        }
    }

    private sealed class StableReadOnlyCollection<T> : IReadOnlyCollection<T>
    {
        private readonly IReadOnlyList<T> _items;

        public StableReadOnlyCollection(IReadOnlyList<T> items)
        {
            _items = items;
        }

        public int Count => _items.Count;

        public int MoveNextCount { get; private set; }

        public IEnumerator<T> GetEnumerator()
        {
            foreach (var item in _items)
            {
                MoveNextCount++;
                yield return item;
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
