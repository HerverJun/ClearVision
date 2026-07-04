using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using ClearVision.Product.Application.Services;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Interfaces;
using ClearVision.Product.Core.Services;
using ClearVision.Product.Desktop.Endpoints;
using ClearVision.Product.Desktop.Middleware;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;

namespace ClearVision.Product.Desktop.Tests;

public sealed class ApiEndpointsInspectionHistoryTests
{
    [Fact]
    public async Task HistoryEndpoints_ShouldRequireDesktopAuthMiddleware()
    {
        var service = Substitute.For<IInspectionService>();
        var authService = Substitute.For<IAuthService>();
        await using var host = await HistoryEndpointTestHost.CreateAsync(service, authService);

        using var response = await host.Client.GetAsync($"/api/inspection/history/{Guid.NewGuid()}");

        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized, body);
    }

    [Fact]
    public async Task HistoryDetailEndpoint_ShouldScopeLookupByProjectAndResultId()
    {
        var service = Substitute.For<IInspectionService>();
        var authService = Substitute.For<IAuthService>();
        var projectId = Guid.NewGuid();
        var resultId = Guid.NewGuid();
        authService.GetSessionAsync("desktop-token").Returns(Task.FromResult<ClearVision.Product.Application.Services.UserSession?>(new ClearVision.Product.Application.Services.UserSession
        {
            UserId = "history-user",
            Username = "history-user",
            Role = "Engineer",
            ExpiresAt = DateTime.UtcNow.AddMinutes(30)
        }));
        service.GetInspectionHistoryDetailAsync(projectId, resultId)
            .Returns(Task.FromResult<InspectionHistoryDetail?>(null));
        await using var host = await HistoryEndpointTestHost.CreateAsync(service, authService);

        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/inspection/history/{projectId}/{resultId}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "desktop-token");
        using var response = await host.Client.SendAsync(request);

        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.NotFound, body);
        _ = service.Received(1).GetInspectionHistoryDetailAsync(projectId, resultId);
    }

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
    public void ToInspectionHistoryListResponse_ShouldReturnLightweightTraceableItems()
    {
        var projectId = Guid.NewGuid();
        var imageId = Guid.NewGuid();
        var outputImage = new byte[] { 1, 2, 3, 4, 5 };
        var sessionId = Guid.NewGuid();

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
            CreatedAt = DateTime.UtcNow.AddSeconds(-2),
            FlowVersionHash = "FLOW-HASH-1",
            CalibrationBundleId = "bundle-authority-1",
            SessionId = sessionId,
            HasImage = true,
            HasOutputData = true,
            HasAnalysisData = true,
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
        json.Should().NotContain("outputData");
        json.Should().NotContain("analysisData");
        json.Should().NotContain("part-a");

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        root.GetProperty("totalCount").GetInt32().Should().Be(1);

        var item = root.GetProperty("items")[0];
        item.TryGetProperty("outputImage", out _).Should().BeFalse();
        item.TryGetProperty("outputData", out _).Should().BeFalse();
        item.TryGetProperty("analysisData", out _).Should().BeFalse();
        item.GetProperty("imageId").GetGuid().Should().Be(imageId);
        item.GetProperty("hasImage").GetBoolean().Should().BeTrue();
        item.GetProperty("imageReference").GetString().Should().Be($"/api/images/{imageId:D}");
        item.GetProperty("hasOutputData").GetBoolean().Should().BeTrue();
        item.GetProperty("hasAnalysisData").GetBoolean().Should().BeTrue();
        item.GetProperty("flowVersionHash").GetString().Should().Be("FLOW-HASH-1");
        item.GetProperty("calibrationBundleId").GetString().Should().Be("bundle-authority-1");
        item.GetProperty("sessionId").GetGuid().Should().Be(sessionId);
        item.GetProperty("defectCount").GetInt32().Should().Be(1);
        item.GetProperty("processingTimeMs").GetInt64().Should().Be(123);
    }

    [Fact]
    public void ToInspectionHistoryDetailResponse_ShouldFailSoftForMalformedStoredJson()
    {
        var detail = new InspectionHistoryDetail
        {
            Id = Guid.NewGuid(),
            ProjectId = Guid.NewGuid(),
            Status = InspectionStatus.Error,
            ProcessingTimeMs = 12,
            InspectionTime = DateTime.UtcNow,
            HasOutputData = true,
            HasAnalysisData = true,
            OutputDataJson = "{not-json",
            AnalysisDataJson = "{not-json"
        };

        var response = ApiEndpoints.ToInspectionHistoryDetailResponse(detail);
        var json = JsonSerializer.Serialize(response, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        root.GetProperty("outputDataPreview").GetProperty("error").GetString().Should().Be("MalformedJson");
        root.GetProperty("analysisDataPreview").GetProperty("error").GetString().Should().Be("MalformedJson");
        root.TryGetProperty("outputData", out _).Should().BeFalse();
        root.TryGetProperty("analysisData", out _).Should().BeFalse();
    }

    [Fact]
    public void ToInspectionHistoryDetailResponse_ShouldRedactPathsSecretsAndLargePayloads()
    {
        var imageId = Guid.NewGuid();
        var longText = new string('A', 900);
        var detail = new InspectionHistoryDetail
        {
            Id = Guid.NewGuid(),
            ProjectId = Guid.NewGuid(),
            Status = InspectionStatus.OK,
            ProcessingTimeMs = 34,
            ImageId = imageId,
            InspectionTime = DateTime.UtcNow,
            HasImage = true,
            HasOutputData = true,
            HasAnalysisData = true,
            FlowVersionHash = "FLOW-HASH-2",
            CalibrationBundleId = "bundle-2",
            SessionId = Guid.NewGuid(),
            OutputDataJson = $$"""
            {
              "score": 42,
              "password": "do-not-leak",
              "localPath": "C:\\Users\\A\\secret\\image.png",
              "notes": "{{longText}}",
              "outputImageBase64": "{{new string('B', 400)}}"
            }
            """,
            AnalysisDataJson = """{"cards":[{"title":"Card","fields":[{"key":"token","value":"secret-token"}]}]}"""
        };

        var response = ApiEndpoints.ToInspectionHistoryDetailResponse(detail);
        var json = JsonSerializer.Serialize(response, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        json.Should().NotContain("do-not-leak");
        json.Should().NotContain("secret-token");
        json.Should().NotContain("C:\\\\Users\\\\A\\\\secret\\\\image.png");
        json.Should().NotContain(new string('B', 200));
        json.Should().Contain("[REDACTED]");
        json.Should().Contain("[REDACTED_PATH]");
        json.Should().Contain("[OMITTED_LARGE_PAYLOAD]");

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        root.GetProperty("imageReference").GetString().Should().Be($"/api/images/{imageId:D}");
        root.GetProperty("outputDataPreview").GetProperty("wasTruncated").GetBoolean().Should().BeTrue();
        root.GetProperty("outputDataPreview").GetProperty("wasRedacted").GetBoolean().Should().BeTrue();
        root.GetProperty("analysisDataPreview").GetProperty("wasRedacted").GetBoolean().Should().BeTrue();
    }

    private sealed class HistoryEndpointTestHost : IAsyncDisposable
    {
        private readonly WebApplication _app;

        private HistoryEndpointTestHost(WebApplication app, HttpClient client)
        {
            _app = app;
            Client = client;
        }

        public HttpClient Client { get; }

        public static async Task<HistoryEndpointTestHost> CreateAsync(
            IInspectionService service,
            IAuthService authService)
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                EnvironmentName = Environments.Development
            });

            builder.WebHost.UseTestServer();
            builder.Services.AddLogging();
            builder.Services.AddSingleton(service);
            builder.Services.AddSingleton(authService);

            var app = builder.Build();
            app.UseMiddleware<AuthMiddleware>();
            app.MapGet("/api/inspection/history/{projectId:guid}", () => Results.Ok(new InspectionHistoryPage()));
            app.MapGet("/api/inspection/history/{projectId:guid}/{resultId:guid}", async (
                Guid projectId,
                Guid resultId,
                [FromServices] IInspectionService inspectionService) =>
            {
                var result = await inspectionService.GetInspectionHistoryDetailAsync(projectId, resultId);
                return result == null
                    ? Results.NotFound(new { Error = "Inspection history result was not found." })
                    : Results.Ok(ApiEndpoints.ToInspectionHistoryDetailResponse(result));
            });
            await app.StartAsync();

            return new HistoryEndpointTestHost(app, app.GetTestClient());
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }
}
