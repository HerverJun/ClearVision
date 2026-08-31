using System.Text.Json;
using FluentAssertions;
using DetectionAdapter = ClearVision.Product.Core.Services.DetectionResultAdapter;
using VisionDetection = ClearVision.Product.Core.ValueObjects.DetectionResult;
using VisionDetectionList = ClearVision.Product.Core.ValueObjects.DetectionList;

namespace ClearVision.Product.Tests.Services;

[TestClassification(TestDomain.Core, TestPurpose.Regression, TestLane.Pr, TestEvidenceType.Contract, TestOracleType.Contract, TestResourceRequirement.None, TestExpectedDuration.Fast, TestFlakyPolicy.Blocking, "product", Suites = "ServicesRegression")]
public sealed class DetectionResultAdapterTests
{
    [Fact]
    public void TryExtract_WithTypedDetectionList_ShouldCloneCanonicalDetection()
    {
        var source = new VisionDetection("typed-label", 0.91f, 11f, 12f, 13f, 14f);

        var success = DetectionAdapter.TryExtract(
            new VisionDetectionList([source]),
            out var detections);

        success.Should().BeTrue();
        detections.Should().ContainSingle();
        detections.Single().Should().NotBeSameAs(source);
        detections.Single().Label.Should().Be("typed-label");
        detections.Single().Confidence.Should().BeApproximately(0.91f, 0.0001f);
    }

    [Fact]
    public void TryExtract_WithDictionaryPayload_ShouldNormalizeAliases()
    {
        var payload = new Dictionary<string, object>
        {
            ["Defects"] = new object[]
            {
                new Dictionary<string, object>
                {
                    ["ClassName"] = "dictionary-label",
                    ["Score"] = "0.75",
                    ["Left"] = 1,
                    ["Top"] = 2,
                    ["Width"] = 3,
                    ["Height"] = 4
                }
            }
        };

        var success = DetectionAdapter.TryExtract(payload, out var detections);

        success.Should().BeTrue();
        detections.Should().ContainSingle();
        detections.Single().Label.Should().Be("dictionary-label");
        detections.Single().Confidence.Should().BeApproximately(0.75f, 0.0001f);
        detections.Single().X.Should().Be(1);
        detections.Single().Y.Should().Be(2);
    }

    [Theory]
    [InlineData("{\"DetectionList\":[{\"Label\":\"json-label\",\"Confidence\":0.8,\"X\":5,\"Y\":6,\"Width\":7,\"Height\":8}]}")]
    [InlineData("[{\"Label\":\"json-label\",\"Confidence\":0.8,\"X\":5,\"Y\":6,\"Width\":7,\"Height\":8}]")]
    public void TryExtract_WithJsonPayload_ShouldNormalizeCanonicalDetection(string json)
    {
        var success = DetectionAdapter.TryExtract(json, out var detections);

        success.Should().BeTrue();
        detections.Should().ContainSingle();
        detections.Single().Label.Should().Be("json-label");
        detections.Single().Width.Should().Be(7);
        detections.Single().Height.Should().Be(8);
    }

    [Fact]
    public void TryExtractFromOutput_WithMalformedRecognizedPayload_ShouldFailClosed()
    {
        using var document = JsonDocument.Parse(
            "{\"DetectionList\":[{\"Label\":\"missing-bounds\",\"Confidence\":0.8,\"X\":1}]}" );
        var output = new Dictionary<string, object>
        {
            ["DetectionList"] = document.RootElement.Clone()
        };

        var success = DetectionAdapter.TryExtractFromOutput(
            output,
            out var detections,
            out var hasDetectionPayload);

        success.Should().BeFalse();
        hasDetectionPayload.Should().BeTrue();
        detections.Should().BeEmpty();
    }
}
