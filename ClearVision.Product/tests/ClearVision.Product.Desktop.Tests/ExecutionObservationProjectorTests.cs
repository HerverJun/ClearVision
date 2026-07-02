using System.Collections;
using System.Text.Json;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.ValueObjects;
using ClearVision.Product.Desktop.Observation;
using ClearVision.Product.Infrastructure.Operators;
using FluentAssertions;
using OpenCvSharp;

namespace ClearVision.Product.Desktop.Tests;

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

        FindNode(unique.Detail, "Values")!.ResultPath.Should().BeNull();
        FindNode(unique.Detail, "Image")!.ResultPath.Should().BeNull();
        unique.Summary.Any(item =>
            (item.Key == "Values" || item.Key == "Image") &&
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

    private static ExecutionObservationEnvelopeV1 CreateObservation(
        IReadOnlyDictionary<string, object> outputData,
        IReadOnlyList<ExecutionObservationOutputPortV1>? outputPorts = null)
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
            ObservedAtUtc = DateTimeOffset.Parse("2026-07-02T01:02:03Z")
        });
    }

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
}
