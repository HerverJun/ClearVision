using System.Reflection;
using System.Text.Json;
using ClearVision.Product.Application.Analysis;
using ClearVision.Product.Application.DTOs;
using ClearVision.Product.Core.ValueObjects;
using ClearVision.Product.Infrastructure.Services;
using FluentAssertions;
using Xunit;

namespace ClearVision.Product.Tests.Services;

[TestClassification(TestDomain.General, TestPurpose.Regression, TestLane.Pr, TestEvidenceType.Contract, TestOracleType.Contract, TestResourceRequirement.None, TestExpectedDuration.Fast, TestFlakyPolicy.Blocking, "product", Suites = "ServicesRegression")]
public class OutputPayloadSerializationTests
{
    [Fact]
    public void BuildSerializableOutputData_DegradesOnlyThrowingInstancesOfPreviouslySerializableType()
    {
        var outputData = new Dictionary<string, object>
        {
            ["Good"] = new ContentDependentJsonValue(throwOnSerialize: false),
            ["Bad"] = new ContentDependentJsonValue(throwOnSerialize: true)
        };

        var serializable = AnalysisPayloadSerialization.BuildSerializableOutputData(outputData);

        serializable["Good"].Should().BeOfType<JsonElement>();
        var good = (JsonElement)serializable["Good"]!;
        good.GetProperty(nameof(ContentDependentJsonValue.Value)).GetString().Should().Be("ok");
        serializable["Bad"].Should().Be("fallback-bad");

        var serialize = () => JsonSerializer.Serialize(serializable);
        serialize.Should().NotThrow();
    }

    [Fact]
    public void BuildSerializableOutputData_BoundsLargeCollectionsAndStrings()
    {
        var longText = new string('x', 20_000);
        var outputData = new Dictionary<string, object>
        {
            ["Scores"] = Enumerable.Range(0, 300)
                .Select(index => new Dictionary<string, object>
                {
                    ["Index"] = index,
                    ["Trace"] = longText
                })
                .ToList(),
            ["Message"] = longText
        };

        var serializable = AnalysisPayloadSerialization.BuildSerializableOutputData(outputData);

        var scores = serializable["Scores"].Should().BeOfType<Dictionary<string, object?>>().Subject;
        scores["__truncated"].Should().Be(true);
        scores["__shownCount"].Should().Be(256);
        scores["__limit"].Should().Be(256);
        scores["__totalCount"].Should().Be(300);

        var items = scores["items"].Should().BeOfType<List<object?>>().Subject;
        items.Should().HaveCount(256);

        var first = items[0].Should().BeOfType<Dictionary<string, object?>>().Subject;
        first["Index"].Should().Be(0);
        var trace = first["Trace"].Should().BeOfType<string>().Subject;
        trace.Should().EndWith("...<truncated>");
        trace.Length.Should().BeLessThan(longText.Length);

        var message = serializable["Message"].Should().BeOfType<string>().Subject;
        message.Should().EndWith("...<truncated>");
        message.Length.Should().BeLessThan(longText.Length);

        JsonSerializer.Serialize(serializable).Should().NotContain(longText);
    }

    [Fact]
    public void BuildSerializableOutputData_DropsLargeInlineImageStringsButKeepsImageMetadata()
    {
        var imageId = Guid.NewGuid().ToString();
        var outputData = new Dictionary<string, object>
        {
            ["OutputImageBase64"] = new string('A', 1024),
            ["ImageId"] = imageId,
            ["ImageQualityScore"] = 0.91
        };

        var serializable = AnalysisPayloadSerialization.BuildSerializableOutputData(outputData);

        serializable.Should().NotContainKey("OutputImageBase64");
        serializable["ImageId"].Should().Be(imageId);
        serializable["ImageQualityScore"].Should().Be(0.91);
    }

    [Fact]
    public void BuildSerializableOutputData_BoundsLargeCollectionsInsideSerializableObjects()
    {
        var outputData = new Dictionary<string, object>
        {
            ["Batch"] = new SerializableBatch(300)
        };

        var serializable = AnalysisPayloadSerialization.BuildSerializableOutputData(outputData);

        var batch = serializable["Batch"].Should().BeOfType<Dictionary<string, object?>>().Subject;
        var detections = batch["Detections"].Should().BeOfType<Dictionary<string, object?>>().Subject;
        detections["__truncated"].Should().Be(true);
        detections["__shownCount"].Should().Be(256);
        detections["__totalCount"].Should().Be(300);

        var items = detections["items"].Should().BeOfType<List<object?>>().Subject;
        items.Should().HaveCount(256);
    }

    [Fact]
    public void BuildSerializableAnalysisData_BoundsCardFieldValuesAndMeta()
    {
        var longMessage = new string('m', 20_000);
        var detections = new DetectionList(Enumerable.Range(0, 300)
            .Select(index => new DetectionResult($"item-{index}", 0.9f, index, index + 1, 10, 11)));
        var analysisData = new AnalysisDataDto
        {
            Version = 1,
            Summary = new AnalysisSummaryDto
            {
                CardCount = 1,
                Categories = ["detection"]
            },
            Cards =
            [
                new AnalysisCardDto
                {
                    Id = "detection-card",
                    Category = "detection",
                    Title = "Detections",
                    Status = "Info",
                    Message = longMessage,
                    Fields =
                    [
                        new AnalysisFieldDto
                        {
                            Key = "Detections",
                            Label = "Detections",
                            DataType = "DetectionList",
                            Value = detections
                        },
                        new AnalysisFieldDto
                        {
                            Key = "OutputImageBase64",
                            Label = "Image",
                            Value = new string('A', 1024)
                        }
                    ],
                    Meta = new Dictionary<string, object?>
                    {
                        ["PreviewImageBase64"] = new string('B', 1024),
                        ["Source"] = "model-a"
                    }
                }
            ]
        };

        var serializable = AnalysisPayloadSerialization.BuildSerializableAnalysisData(analysisData);

        serializable.Cards.Should().ContainSingle();
        var card = serializable.Cards[0];
        card.Message.Should().EndWith("...<truncated>");
        card.Message!.Length.Should().BeLessThan(longMessage.Length);
        card.Fields.Should().ContainSingle(field => field.Key == "Detections");
        card.Fields.Should().NotContain(field => field.Key == "OutputImageBase64");
        card.Meta.Should().NotContainKey("PreviewImageBase64");
        card.Meta!["Source"].Should().Be("model-a");

        var value = card.Fields[0].Value.Should().BeOfType<Dictionary<string, object?>>().Subject;
        var detectionsPayload = value["Detections"].Should().BeOfType<Dictionary<string, object?>>().Subject;
        detectionsPayload["__truncated"].Should().Be(true);
        detectionsPayload["__shownCount"].Should().Be(256);
        detectionsPayload["__totalCount"].Should().Be(300);

        var json = JsonSerializer.Serialize(serializable);
        json.Should().NotContain(longMessage);
        json.Should().NotContain(new string('A', 1024));
        json.Should().NotContain(new string('B', 1024));
    }

    [Fact]
    public void TrySetAnalysisDataJson_ShouldPersistBoundedAnalysisPayload()
    {
        var result = new ClearVision.Product.Core.Entities.InspectionResult(Guid.NewGuid());
        var analysisData = new AnalysisDataDto
        {
            Cards =
            [
                new AnalysisCardDto
                {
                    Id = "large-card",
                    Category = "generic",
                    Title = "Large",
                    Fields =
                    [
                        new AnalysisFieldDto
                        {
                            Key = "Values",
                            Label = "Values",
                            Value = Enumerable.Range(0, 300).ToList()
                        }
                    ]
                }
            ]
        };

        AnalysisPayloadSerialization.TrySetAnalysisDataJson(
            result,
            analysisData,
            Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);

        result.AnalysisDataJson.Should().NotBeNullOrWhiteSpace();
        using var document = JsonDocument.Parse(result.AnalysisDataJson!);
        var value = document.RootElement
            .GetProperty("Cards")[0]
            .GetProperty("Fields")[0]
            .GetProperty("Value");

        value.GetProperty("__truncated").GetBoolean().Should().BeTrue();
        value.GetProperty("__shownCount").GetInt32().Should().Be(256);
        value.GetProperty("__totalCount").GetInt32().Should().Be(300);
    }

    [Fact]
    public void BuildSerializableAnalysisData_KeepsMessageOnlyCardsAndRecomputesSummary()
    {
        var analysisData = new AnalysisDataDto
        {
            Summary = new AnalysisSummaryDto
            {
                CardCount = 2,
                Categories = ["message", "image"]
            },
            Cards =
            [
                new AnalysisCardDto
                {
                    Id = "message-card",
                    Category = "message",
                    Title = "Message",
                    Message = "Only message"
                },
                new AnalysisCardDto
                {
                    Id = "image-card",
                    Category = "image",
                    Title = "Image",
                    Fields =
                    [
                        new AnalysisFieldDto
                        {
                            Key = "OutputImageBase64",
                            Label = "Image",
                            Value = new string('A', 1024)
                        }
                    ]
                }
            ]
        };

        var serializable = AnalysisPayloadSerialization.BuildSerializableAnalysisData(analysisData);

        serializable.Cards.Should().ContainSingle(card => card.Id == "message-card");
        serializable.Cards.Should().NotContain(card => card.Id == "image-card");
        serializable.Summary.Should().NotBeNull();
        serializable.Summary!.CardCount.Should().Be(1);
        serializable.Summary.Categories.Should().Equal("message");
    }

    [Fact]
    public void FlowExecutionOutputNormalization_DegradesOnlyThrowingInstancesOfPreviouslySerializableType()
    {
        var good = NormalizeWithPrivateMethod(
            typeof(FlowExecutionService),
            new ContentDependentJsonValue(throwOnSerialize: false));
        var bad = NormalizeWithPrivateMethod(
            typeof(FlowExecutionService),
            new ContentDependentJsonValue(throwOnSerialize: true));

        good.Should().BeOfType<JsonElement>();
        ((JsonElement)good!).GetProperty(nameof(ContentDependentJsonValue.Value)).GetString().Should().Be("ok");
        bad.Should().Be("fallback-bad");

        var serialize = () => JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["Good"] = good,
            ["Bad"] = bad
        });
        serialize.Should().NotThrow();
    }

    [Fact]
    public void OperatorPreviewOutputNormalization_DegradesOnlyThrowingInstancesOfPreviouslySerializableType()
    {
        var good = NormalizeWithPrivateMethod(
            typeof(OperatorPreviewService),
            new ContentDependentJsonValue(throwOnSerialize: false));
        var bad = NormalizeWithPrivateMethod(
            typeof(OperatorPreviewService),
            new ContentDependentJsonValue(throwOnSerialize: true));

        good.Should().BeOfType<JsonElement>();
        ((JsonElement)good!).GetProperty(nameof(ContentDependentJsonValue.Value)).GetString().Should().Be("ok");
        bad.Should().Be("fallback-bad");

        var serialize = () => JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["Good"] = good,
            ["Bad"] = bad
        });
        serialize.Should().NotThrow();
    }

    private static object? NormalizeWithPrivateMethod(Type ownerType, object value)
    {
        var method = ownerType.GetMethod(
            "TryNormalizeOutputValue",
            BindingFlags.NonPublic | BindingFlags.Static);

        method.Should().NotBeNull();

        var args = new object?[] { value, null, 0 };
        var success = (bool)method!.Invoke(null, args)!;

        success.Should().BeTrue();
        return args[1];
    }

    private sealed class ContentDependentJsonValue
    {
        public ContentDependentJsonValue(bool throwOnSerialize)
        {
            ThrowOnSerialize = throwOnSerialize;
        }

        public bool ThrowOnSerialize { get; }

        public string Value => ThrowOnSerialize
            ? throw new InvalidOperationException("bad")
            : "ok";

        public override string ToString() => ThrowOnSerialize
            ? "fallback-bad"
            : "fallback-ok";
    }

    private sealed class SerializableBatch
    {
        public SerializableBatch(int count)
        {
            Detections = Enumerable.Range(0, count).ToList();
        }

        public List<int> Detections { get; }
    }
}
