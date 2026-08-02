using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using ClearVision.Product.Application.Services;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Interfaces;
using ClearVision.Product.Core.Outcomes;
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
    public async Task HistoryCompareEndpoint_ShouldRequireDesktopAuthMiddleware()
    {
        var service = Substitute.For<IInspectionService>();
        var authService = Substitute.For<IAuthService>();
        await using var host = await HistoryEndpointTestHost.CreateAsync(service, authService);

        using var response = await host.Client.GetAsync($"/api/inspection/history/{Guid.NewGuid()}/compare?leftId={Guid.NewGuid()}&rightId={Guid.NewGuid()}");

        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized, body);
    }

    [Fact]
    public async Task EvidenceManifestEndpoint_ShouldRequireDesktopAuthMiddleware()
    {
        var service = Substitute.For<IInspectionService>();
        var authService = Substitute.For<IAuthService>();
        await using var host = await HistoryEndpointTestHost.CreateAsync(service, authService);

        using var response = await host.Client.GetAsync($"/api/inspection/history/{Guid.NewGuid()}/{Guid.NewGuid()}/evidence/manifest");

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
    public async Task HistoryCompareEndpoint_ShouldScopeLookupByProjectAndResultIds()
    {
        var service = Substitute.For<IInspectionService>();
        var authService = Substitute.For<IAuthService>();
        var projectId = Guid.NewGuid();
        var leftId = Guid.NewGuid();
        var rightId = Guid.NewGuid();
        authService.GetSessionAsync("desktop-token").Returns(Task.FromResult<ClearVision.Product.Application.Services.UserSession?>(new ClearVision.Product.Application.Services.UserSession
        {
            UserId = "history-user",
            Username = "history-user",
            Role = "Engineer",
            ExpiresAt = DateTime.UtcNow.AddMinutes(30)
        }));
        service.CompareInspectionHistoryAsync(projectId, leftId, rightId)
            .Returns(Task.FromResult<InspectionHistoryComparison?>(null));
        await using var host = await HistoryEndpointTestHost.CreateAsync(service, authService);

        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/inspection/history/{projectId}/compare?leftId={leftId}&rightId={rightId}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "desktop-token");
        using var response = await host.Client.SendAsync(request);

        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.NotFound, body);
        _ = service.Received(1).CompareInspectionHistoryAsync(projectId, leftId, rightId);
    }

    [Fact]
    public async Task PreviousSuccessEndpoint_ShouldScopeLookupByProjectAndResultId()
    {
        var service = Substitute.For<IInspectionService>();
        var authService = Substitute.For<IAuthService>();
        var projectId = Guid.NewGuid();
        var resultId = Guid.NewGuid();
        var referenceId = Guid.NewGuid();
        authService.GetSessionAsync("desktop-token").Returns(Task.FromResult<ClearVision.Product.Application.Services.UserSession?>(new ClearVision.Product.Application.Services.UserSession
        {
            UserId = "history-user",
            Username = "history-user",
            Role = "Engineer",
            ExpiresAt = DateTime.UtcNow.AddMinutes(30)
        }));
        service.FindPreviousSuccessfulInspectionAsync(projectId, resultId, 25)
            .Returns(new InspectionPreviousSuccessReference
            {
                Found = true,
                QueryLimit = 25,
                Message = "已找到失败前成功参考",
                CurrentSummary = new InspectionHistoryComparisonSummary
                {
                    ResultId = resultId,
                    ProjectId = projectId,
                    Status = InspectionStatus.NG,
                    InspectionTime = DateTime.UtcNow,
                    FlowVersionHash = "FLOW-A"
                },
                ReferenceSummary = new InspectionHistoryComparisonSummary
                {
                    ResultId = referenceId,
                    ProjectId = projectId,
                    Status = InspectionStatus.OK,
                    InspectionTime = DateTime.UtcNow.AddMinutes(-1),
                    FlowVersionHash = "FLOW-A"
                }
            });
        await using var host = await HistoryEndpointTestHost.CreateAsync(service, authService);

        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/inspection/history/{projectId}/{resultId}/previous-success?limit=25");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "desktop-token");
        using var response = await host.Client.SendAsync(request);

        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        _ = service.Received(1).FindPreviousSuccessfulInspectionAsync(projectId, resultId, 25);

        using var document = JsonDocument.Parse(body);
        document.RootElement.GetProperty("found").GetBoolean().Should().BeTrue();
        document.RootElement.GetProperty("referenceSummary").GetProperty("resultId").GetGuid().Should().Be(referenceId);
    }

    [Fact]
    public async Task EvidenceManifestEndpoint_ShouldScopeLookupByProjectAndResultId()
    {
        var service = Substitute.For<IInspectionService>();
        var evidenceService = Substitute.For<IInspectionEvidenceManifestService>();
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
        evidenceService.GetManifestAsync(projectId, resultId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new InspectionEvidenceManifestReadResult
            {
                Found = false,
                Status = "missing",
                ErrorCode = "EvidenceManifestMissing",
                Message = "证据清单缺失或已清理",
                Summary = new InspectionEvidenceSummary
                {
                    EvidenceStatus = "missing",
                    Message = "证据清单缺失或已清理"
                }
            }));
        await using var host = await HistoryEndpointTestHost.CreateAsync(service, authService, evidenceService);

        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/inspection/history/{projectId}/{resultId}/evidence/manifest");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "desktop-token");
        using var response = await host.Client.SendAsync(request);

        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        _ = evidenceService.Received(1).GetManifestAsync(projectId, resultId, Arg.Any<CancellationToken>());

        using var document = JsonDocument.Parse(body);
        document.RootElement.GetProperty("status").GetString().Should().Be("missing");
        document.RootElement.GetProperty("errorCode").GetString().Should().Be("EvidenceManifestMissing");
    }

    [Fact]
    public async Task EvidenceExportEndpoint_ShouldReturnBoundedPackageAndChecksumHeader()
    {
        var service = Substitute.For<IInspectionService>();
        var evidenceService = Substitute.For<IInspectionEvidenceManifestService>();
        var authService = Substitute.For<IAuthService>();
        var projectId = Guid.NewGuid();
        var resultId = Guid.NewGuid();
        var payload = """{"schemaVersion":1,"manifest":{"items":[]}}"""u8.ToArray();
        authService.GetSessionAsync("desktop-token").Returns(Task.FromResult<ClearVision.Product.Application.Services.UserSession?>(new ClearVision.Product.Application.Services.UserSession
        {
            UserId = "history-user",
            Username = "history-user",
            Role = "Engineer",
            ExpiresAt = DateTime.UtcNow.AddMinutes(30)
        }));
        evidenceService.ExportAsync(projectId, resultId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new InspectionEvidenceExportResult
            {
                Success = true,
                Status = "available",
                FileName = "evidence.json",
                ContentType = "application/json",
                Content = payload,
                TotalBytes = payload.Length,
                Sha256 = "abc123"
            }));
        await using var host = await HistoryEndpointTestHost.CreateAsync(service, authService, evidenceService);

        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/inspection/history/{projectId}/{resultId}/evidence/export");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "desktop-token");
        using var response = await host.Client.SendAsync(request);

        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
        response.Headers.GetValues("X-Evidence-Export-Sha256").Should().ContainSingle().Which.Should().Be("abc123");
        body.Should().Contain("schemaVersion");
        _ = evidenceService.Received(1).ExportAsync(projectId, resultId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EvidenceExportEndpoint_WhenProjectScopeMisses_ShouldReturnNotFound()
    {
        var service = Substitute.For<IInspectionService>();
        var evidenceService = Substitute.For<IInspectionEvidenceManifestService>();
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
        evidenceService.ExportAsync(projectId, resultId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new InspectionEvidenceExportResult
            {
                Success = false,
                Status = "not-found",
                ErrorCode = "InspectionResultNotFound",
                Message = "Inspection history result was not found."
            }));
        await using var host = await HistoryEndpointTestHost.CreateAsync(service, authService, evidenceService);

        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/inspection/history/{projectId}/{resultId}/evidence/export");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "desktop-token");
        using var response = await host.Client.SendAsync(request);

        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.NotFound, body);
        body.Should().Contain("InspectionResultNotFound");
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
        root.GetProperty("executionOutcome").GetString().Should().Be("Succeeded");
        root.GetProperty("decisionOutcome").GetString().Should().Be("Ng");
        root.GetProperty("hasJudgmentSignal").GetBoolean().Should().BeTrue();
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
        root.GetProperty("executionOutcome").GetString().Should().Be("Succeeded");
        root.GetProperty("decisionOutcome").GetString().Should().Be("Ok");
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
            ExecutionOutcome = ExecutionOutcome.Succeeded,
            DecisionOutcome = DecisionOutcome.Ng,
            DecisionSource = "JudgmentResult",
            ReasonCode = "DerivedFromJudgmentResult",
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
        item.GetProperty("status").GetString().Should().Be("NG");
        item.GetProperty("executionOutcome").GetString().Should().Be("Succeeded");
        item.GetProperty("decisionOutcome").GetString().Should().Be("Ng");
        item.GetProperty("decisionSource").GetString().Should().Be("JudgmentResult");
        item.GetProperty("reasonCode").GetString().Should().Be("DerivedFromJudgmentResult");
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
        root.GetProperty("status").GetString().Should().Be("Error");
        root.GetProperty("executionOutcome").GetString().Should().Be("Failed");
        root.GetProperty("decisionOutcome").GetString().Should().Be("Undetermined");
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
            ExecutionSnapshotId = Guid.NewGuid(),
            ProjectPersistenceRevision = 23,
            DecisionConfigurationHash = "DECISION-HASH-23",
            RuntimePackageId = "PACKAGE-23",
            ExecutionSource = "RuntimePackage",
            ExecutionRunMode = "StationRuntime",
            ShadowRole = "Primary",
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
        var traceability = root.GetProperty("traceability");
        traceability.GetProperty("projectPersistenceRevision").GetInt64().Should().Be(23);
        traceability.GetProperty("decisionConfigurationHash").GetString().Should().Be("DECISION-HASH-23");
        traceability.GetProperty("executionSnapshotId").GetGuid().Should().Be(detail.ExecutionSnapshotId!.Value);
        traceability.GetProperty("packageId").GetString().Should().Be("PACKAGE-23");
        traceability.GetProperty("runtimePackageId").GetString().Should().Be("PACKAGE-23");
        traceability.GetProperty("executionSource").GetString().Should().Be("RuntimePackage");
        traceability.GetProperty("executionRunMode").GetString().Should().Be("StationRuntime");
        traceability.GetProperty("shadowRole").GetString().Should().Be("Primary");
        root.GetProperty("imageReference").GetString().Should().Be($"/api/images/{imageId:D}");
        root.GetProperty("outputDataPreview").GetProperty("wasTruncated").GetBoolean().Should().BeTrue();
        root.GetProperty("outputDataPreview").GetProperty("wasRedacted").GetBoolean().Should().BeTrue();
        root.GetProperty("analysisDataPreview").GetProperty("wasRedacted").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public void ToInspectionHistoryComparisonResponse_ShouldExposeBoundedDiffContract()
    {
        var projectId = Guid.NewGuid();
        var leftId = Guid.NewGuid();
        var rightId = Guid.NewGuid();
        var comparison = new InspectionHistoryComparison
        {
            LeftSummary = new InspectionHistoryComparisonSummary
            {
                ResultId = leftId,
                ProjectId = projectId,
                Status = InspectionStatus.OK,
                InspectionTime = DateTime.UtcNow,
                FlowVersionHash = "FLOW-A",
                CalibrationBundleId = "BUNDLE-A",
                HasImage = true,
                ImageId = Guid.NewGuid(),
                ImageReference = "/api/images/left"
            },
            RightSummary = new InspectionHistoryComparisonSummary
            {
                ResultId = rightId,
                ProjectId = projectId,
                Status = InspectionStatus.NG,
                InspectionTime = DateTime.UtcNow,
                FlowVersionHash = "FLOW-B",
                CalibrationBundleId = "BUNDLE-B",
                HasImage = true
            },
            Compatibility = new InspectionHistoryCompatibility
            {
                FlowVersionCompatible = false,
                CalibrationBundleCompatible = false,
                OnlySafePreviewComparison = true,
                HasUnknownFields = true
            },
            Warnings = ["流程版本不一致，对比仅供参考", "仅比较安全预览字段"],
            FieldDiffs =
            [
                new InspectionHistoryFieldDiff
                {
                    Path = """$["outputDataPreview"]["score"]""",
                    Label = "score",
                    LeftValuePreview = "42",
                    RightValuePreview = "45",
                    DiffType = "Changed",
                    Severity = "info"
                }
            ],
            SceneReplayAvailability = new InspectionHistoryReplayAvailability
            {
                Kind = "scene",
                Mode = "summary-only",
                Message = "暂无 Scene evidence，已降级为摘要回放"
            },
            ImageReplayAvailability = new InspectionHistoryReplayAvailability
            {
                Kind = "image",
                Mode = "image-reference",
                LeftReference = "/api/images/left",
                Message = "图像引用可用"
            }
        };

        var response = ApiEndpoints.ToInspectionHistoryComparisonResponse(comparison);
        var json = JsonSerializer.Serialize(response, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        json.Should().NotContain("outputImage");
        json.Should().NotContain("imageBase64");
        json.Should().NotContain("artifactPayload");
        json.Should().NotContain("VisualScene");

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        root.GetProperty("leftSummary").GetProperty("resultId").GetGuid().Should().Be(leftId);
        root.GetProperty("compatibility").GetProperty("onlySafePreviewComparison").GetBoolean().Should().BeTrue();
        root.GetProperty("warnings")[0].GetString().Should().Contain("流程版本不一致");
        root.GetProperty("sceneReplayAvailability").GetProperty("message").GetString().Should().Be("暂无 Scene evidence，已降级为摘要回放");
        root.GetProperty("fieldDiffs")[0].GetProperty("diffType").GetString().Should().Be("Changed");
        root.GetProperty("imageReplayAvailability").GetProperty("leftReference").GetString().Should().Be("/api/images/left");
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
            IAuthService authService,
            IInspectionEvidenceManifestService? evidenceService = null)
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                EnvironmentName = Environments.Development
            });

            builder.WebHost.UseTestServer();
            builder.Services.AddLogging();
            builder.Services.AddSingleton(service);
            builder.Services.AddSingleton(authService);
            builder.Services.AddSingleton(evidenceService ?? Substitute.For<IInspectionEvidenceManifestService>());

            var app = builder.Build();
            app.UseMiddleware<AuthMiddleware>();
            app.MapGet("/api/inspection/history/{projectId:guid}", () => Results.Ok(new InspectionHistoryPage()));
            app.MapGet("/api/inspection/history/{projectId:guid}/compare", async (
                Guid projectId,
                Guid leftId,
                Guid rightId,
                [FromServices] IInspectionService inspectionService) =>
            {
                var result = await inspectionService.CompareInspectionHistoryAsync(projectId, leftId, rightId);
                return result == null
                    ? Results.NotFound(new { Error = "Inspection history comparison result was not found." })
                    : Results.Ok(ApiEndpoints.ToInspectionHistoryComparisonResponse(result));
            });
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
            app.MapGet("/api/inspection/history/{projectId:guid}/{resultId:guid}/previous-success", async (
                Guid projectId,
                Guid resultId,
                int limit,
                [FromServices] IInspectionService inspectionService) =>
            {
                var result = await inspectionService.FindPreviousSuccessfulInspectionAsync(projectId, resultId, limit);
                return result == null
                    ? Results.NotFound(new { Error = "Inspection history result was not found." })
                    : Results.Ok(ApiEndpoints.ToInspectionPreviousSuccessResponse(result));
            });
            app.MapGet("/api/inspection/history/{projectId:guid}/{resultId:guid}/evidence/manifest", async (
                Guid projectId,
                Guid resultId,
                [FromServices] IInspectionEvidenceManifestService inspectionEvidenceService) =>
            {
                var result = await inspectionEvidenceService.GetManifestAsync(projectId, resultId);
                return string.Equals(result.ErrorCode, "InspectionResultNotFound", StringComparison.Ordinal)
                    ? Results.NotFound(new { result.ErrorCode, result.Message })
                    : Results.Ok(ApiEndpoints.ToInspectionEvidenceManifestResponse(result));
            });
            app.MapGet("/api/inspection/history/{projectId:guid}/{resultId:guid}/evidence/export", async (
                Guid projectId,
                Guid resultId,
                HttpContext httpContext,
                [FromServices] IInspectionEvidenceManifestService inspectionEvidenceService) =>
            {
                var result = await inspectionEvidenceService.ExportAsync(projectId, resultId);
                if (!result.Success)
                {
                    return string.Equals(result.ErrorCode, "InspectionResultNotFound", StringComparison.Ordinal)
                        ? Results.NotFound(new { result.ErrorCode, result.Message })
                        : Results.Json(new { result.Status, result.ErrorCode, result.Message }, statusCode: StatusCodes.Status409Conflict);
                }

                if (!string.IsNullOrWhiteSpace(result.Sha256))
                {
                    httpContext.Response.Headers["X-Evidence-Export-Sha256"] = result.Sha256;
                }

                return Results.File(result.Content, result.ContentType, result.FileName);
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
