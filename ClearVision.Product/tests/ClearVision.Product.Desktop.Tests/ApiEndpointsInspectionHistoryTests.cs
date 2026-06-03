using System.Text.Json;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Interfaces;
using ClearVision.Product.Desktop.Endpoints;
using FluentAssertions;

namespace ClearVision.Product.Desktop.Tests;

public sealed class ApiEndpointsInspectionHistoryTests
{
    [Fact]
    public void ToInspectionExecutionResponse_WithImageId_ShouldOmitInlineOutputImage()
    {
        var projectId = Guid.NewGuid();
        var imageId = Guid.NewGuid();
        var outputImage = new byte[] { 1, 2, 3, 4, 5 };
        var result = new InspectionResult(projectId, imageId);
        result.SetResult(InspectionStatus.NG, 88, 0.76, "ng");
        result.SetOutputImage(outputImage);
        result.SetOutputDataJson("""{"score":88}""");
        result.AddDefect(new Defect(result.Id, DefectType.Stain, 1, 2, 3, 4, 0.91, "stain"));

        var response = ApiEndpoints.ToInspectionExecutionResponse(result);
        var json = JsonSerializer.Serialize(response, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        json.Should().NotContain(Convert.ToBase64String(outputImage));

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        root.GetProperty("id").GetGuid().Should().Be(result.Id);
        root.GetProperty("projectId").GetGuid().Should().Be(projectId);
        root.GetProperty("imageId").GetGuid().Should().Be(imageId);
        root.GetProperty("outputImage").ValueKind.Should().Be(JsonValueKind.Null);
        root.GetProperty("outputData").GetProperty("score").GetInt32().Should().Be(88);
        root.GetProperty("defectCount").GetInt32().Should().Be(1);
    }

    [Fact]
    public void ToInspectionExecutionResponse_WithoutImageId_ShouldKeepInlineImageFallback()
    {
        var projectId = Guid.NewGuid();
        var outputImage = new byte[] { 6, 7, 8 };
        var result = new InspectionResult(projectId);
        result.SetResult(InspectionStatus.OK, 12);
        result.SetOutputImage(outputImage);

        var response = ApiEndpoints.ToInspectionExecutionResponse(result);
        var json = JsonSerializer.Serialize(response, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        root.GetProperty("imageId").ValueKind.Should().Be(JsonValueKind.Null);
        root.GetProperty("outputImage").GetString().Should().Be(Convert.ToBase64String(outputImage));
    }

    [Fact]
    public void ToInspectionHistoryListResponse_ShouldExcludeInlineOutputImage()
    {
        var projectId = Guid.NewGuid();
        var imageId = Guid.NewGuid();
        var outputImage = new byte[] { 1, 2, 3, 4, 5 };

        var result = new InspectionHistoryItem
        {
            Id = Guid.NewGuid(),
            ProjectId = projectId,
            Status = InspectionStatus.NG,
            ProcessingTimeMs = 123,
            ImageId = imageId,
            ConfidenceScore = 0.87,
            ErrorMessage = "threshold failed",
            InspectionTime = DateTime.UtcNow,
            OutputDataJson = """{"score":42,"label":"part-a"}""",
            AnalysisDataJson =
                """
                {
                  "version": 1,
                  "cards": [
                    {
                      "id": "card-1",
                      "category": "measurement",
                      "sourceOperatorId": "00000000-0000-0000-0000-000000000000",
                      "sourceOperatorType": "Threshold",
                      "title": "Threshold",
                      "status": "NG",
                      "priority": 1,
                      "fields": [
                        { "key": "score", "label": "Score", "value": 42 }
                      ]
                    }
                  ],
                  "summary": { "cardCount": 1, "categories": [ "measurement" ] }
                }
                """,
            Defects =
            [
                new InspectionHistoryDefectItem
                {
                    Id = Guid.NewGuid(),
                    Type = DefectType.Scratch,
                    X = 1,
                    Y = 2,
                    Width = 3,
                    Height = 4,
                    ConfidenceScore = 0.91,
                    Description = "scratch",
                    AnnotationData = """{"shape":"rect"}"""
                }
            ]
        };

        var page = new InspectionHistoryPage
        {
            Items = [result],
            TotalCount = 1,
            PageIndex = 0,
            PageSize = 20
        };

        var response = ApiEndpoints.ToInspectionHistoryListResponse(page);
        var json = JsonSerializer.Serialize(response, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        json.Should().NotContain("outputImage");
        json.Should().NotContain("OutputImage");
        json.Should().NotContain(Convert.ToBase64String(outputImage));

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        root.GetProperty("totalCount").GetInt32().Should().Be(1);

        var item = root.GetProperty("items")[0];
        item.TryGetProperty("outputImage", out _).Should().BeFalse();
        item.GetProperty("imageId").GetGuid().Should().Be(imageId);
        item.GetProperty("defectCount").GetInt32().Should().Be(1);
        item.GetProperty("processingTimeMs").GetInt64().Should().Be(123);
        item.GetProperty("outputData").GetProperty("score").GetInt32().Should().Be(42);
        item.GetProperty("analysisData").GetProperty("cards").GetArrayLength().Should().Be(1);
    }

    [Fact]
    public void ToInspectionHistoryListResponse_ShouldIgnoreInvalidStoredJsonPayloads()
    {
        var page = new InspectionHistoryPage
        {
            Items =
            [
                new InspectionHistoryItem
                {
                    Id = Guid.NewGuid(),
                    ProjectId = Guid.NewGuid(),
                    Status = InspectionStatus.Error,
                    ProcessingTimeMs = 12,
                    InspectionTime = DateTime.UtcNow,
                    OutputDataJson = "{not-json",
                    AnalysisDataJson = "{not-json"
                }
            ],
            TotalCount = 1,
            PageIndex = 0,
            PageSize = 20
        };

        var response = ApiEndpoints.ToInspectionHistoryListResponse(page);
        var json = JsonSerializer.Serialize(response, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        using var document = JsonDocument.Parse(json);
        var item = document.RootElement.GetProperty("items")[0];
        item.GetProperty("outputData").ValueKind.Should().Be(JsonValueKind.Null);
        item.GetProperty("analysisData").ValueKind.Should().Be(JsonValueKind.Null);
    }
}
