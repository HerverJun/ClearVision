using System.Text.Json;
using ClearVision.Product.Core.Enums;
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
            .And.Match<ExecutionObservationDetailNodeV1>(node => node!.Kind == "number" && node.Addressable);
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

        FindNode(observation.Detail, "Explodes")!.Kind.Should().Be("propertyError");
        observation.Diagnostics.Should().Contain(item => item.Code == "getter-error");
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

    private static ExecutionObservationEnvelopeV1 CreateObservation(IReadOnlyDictionary<string, object> outputData)
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

    private sealed class ThrowingGetterDto
    {
        public int Safe => 1;

        public int Explodes => throw new InvalidOperationException("getter failed");
    }
}
