using System.Text;
using System.Text.Json;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Interfaces;
using ClearVision.Product.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace ClearVision.Product.Tests.Services;

public sealed class InspectionEvidenceManifestServiceTests
{
    private static readonly JsonSerializerOptions ReadJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    [Fact]
    public async Task CaptureAsync_ShouldWriteManifestWithRelativePathsChecksumsAndRedactedJson()
    {
        var root = CreateTempDirectory();
        var projectId = Guid.NewGuid();
        var result = CreateResult(projectId, InspectionStatus.NG);
        result.SetOutputImage(Encoding.UTF8.GetBytes("not-real-image-binary"));
        result.SetOutputDataJson(
            """
            {
              "score": 42,
              "password": "do-not-leak",
              "tokenValue": "Bearer secret-token",
              "localPath": "C:\\Users\\A\\secret\\raw.png",
              "imageBase64": "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB"
            }
            """);

        try
        {
            var service = CreateService(root);

            await service.CaptureAsync(result);

            var manifest = await ReadManifestAsync(root, projectId, result.Id);
            manifest.SchemaVersion.Should().Be(1);
            manifest.ProjectId.Should().Be(projectId);
            manifest.InspectionResultId.Should().Be(result.Id);
            manifest.SessionId.Should().Be(result.SessionId);
            manifest.RunId.Should().BeNull();
            manifest.Checksum.Should().Be(InspectionEvidenceManifestService.ComputeManifestChecksum(manifest));
            manifest.RetentionClass.Should().Be("long");
            manifest.Items.Should().Contain(item => item.Role == "output-image");
            manifest.Items.Should().OnlyContain(item =>
                string.IsNullOrWhiteSpace(item.RelativePath) ||
                (!Path.IsPathRooted(item.RelativePath) && !item.RelativePath.Contains("..") && !item.RelativePath.Contains('\\')));

            foreach (var item in manifest.Items.Where(item => item.Available && !string.IsNullOrWhiteSpace(item.RelativePath)))
            {
                var itemPath = Path.Combine(root, projectId.ToString("N"), result.Id.ToString("N"), item.RelativePath!.Replace('/', Path.DirectorySeparatorChar));
                File.Exists(itemPath).Should().BeTrue();
                item.Sha256.Should().Be(Sha256(File.ReadAllBytes(itemPath)));
            }

            var export = await CreateService(root, CreateRepository(projectId, result.Id)).ExportAsync(projectId, result.Id);
            var json = Encoding.UTF8.GetString(export.Content);
            export.Success.Should().BeTrue();
            json.Should().NotContain("\"runId\"");
            json.Should().NotContain("do-not-leak");
            json.Should().NotContain("secret-token");
            json.Should().NotContain("C:\\\\Users\\\\A\\\\secret\\\\raw.png");
            json.Should().NotContain("BBBBBBBBBBBBBBBB");
            json.Should().Contain("[REDACTED]");
            json.Should().Contain("[REDACTED_PATH]");
            json.Should().Contain("[OMITTED_LARGE_PAYLOAD]");
            json.Should().Contain("binary-item-omitted-from-json-export");
        }
        finally
        {
            DeleteTempDirectory(root);
        }
    }

    [Fact]
    public async Task GetManifestAsync_ShouldDiscardLegacyConflatedRunIdAfterChecksumValidation()
    {
        var root = CreateTempDirectory();
        var projectId = Guid.NewGuid();
        var result = CreateResult(projectId, InspectionStatus.NG);

        try
        {
            var service = CreateService(root, CreateRepository(projectId, result.Id));
            await service.CaptureAsync(result);

            var manifest = await ReadManifestAsync(root, projectId, result.Id);
            manifest.RunId = manifest.SessionId;
            manifest.Checksum = InspectionEvidenceManifestService.ComputeManifestChecksum(manifest);
            var manifestPath = Path.Combine(root, projectId.ToString("N"), result.Id.ToString("N"), "manifest.json");
            await File.WriteAllTextAsync(
                manifestPath,
                JsonSerializer.Serialize(manifest, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));

            var read = await service.GetManifestAsync(projectId, result.Id);

            read.Found.Should().BeTrue();
            read.Manifest!.SessionId.Should().Be(result.SessionId);
            read.Manifest.RunId.Should().BeNull();
            read.Warnings.Should().ContainSingle(warning => warning.Contains("RunId was discarded"));
        }
        finally
        {
            DeleteTempDirectory(root);
        }
    }

    [Fact]
    public async Task CaptureAsync_ShouldApplyOutcomePoliciesForOkNgAndError()
    {
        var root = CreateTempDirectory();
        var projectId = Guid.NewGuid();
        var ok = CreateResult(projectId, InspectionStatus.OK);
        var ng = CreateResult(projectId, InspectionStatus.NG);
        var error = CreateResult(projectId, InspectionStatus.Error);
        ok.SetOutputImage([1, 2, 3]);
        ng.SetOutputImage([4, 5, 6]);
        error.SetOutputImage([7, 8, 9]);

        try
        {
            var service = CreateService(root);

            await service.CaptureAsync(ok);
            await service.CaptureAsync(ng);
            await service.CaptureAsync(error);

            var okManifest = await ReadManifestAsync(root, projectId, ok.Id);
            var ngManifest = await ReadManifestAsync(root, projectId, ng.Id);
            var errorManifest = await ReadManifestAsync(root, projectId, error.Id);

            okManifest.RetentionClass.Should().Be("short");
            okManifest.Items.Should().NotContain(item => item.Role == "output-image");
            ngManifest.RetentionClass.Should().Be("long");
            ngManifest.Items.Should().Contain(item => item.Role == "output-image");
            errorManifest.RetentionClass.Should().Be("long");
            errorManifest.Items.Should().Contain(item => item.Role == "output-image");
            okManifest.RetentionExpiresAtUtc.Should().BeBefore(ngManifest.RetentionExpiresAtUtc!.Value);
        }
        finally
        {
            DeleteTempDirectory(root);
        }
    }

    [Fact]
    public async Task GetSummaryAsync_WhenManifestIsMissing_ShouldFailSoft()
    {
        var root = CreateTempDirectory();
        var result = CreateDetail(Guid.NewGuid(), Guid.NewGuid());

        try
        {
            var service = CreateService(root);

            var summary = await service.GetSummaryAsync(result);

            summary.HasEvidenceManifest.Should().BeFalse();
            summary.EvidenceStatus.Should().Be("missing");
            summary.Message.Should().Contain("证据清单缺失或已清理");
        }
        finally
        {
            DeleteTempDirectory(root);
        }
    }

    [Fact]
    public async Task GetManifestAsync_WhenManifestIsCorrupt_ShouldReturnStableMissingStatus()
    {
        var root = CreateTempDirectory();
        var projectId = Guid.NewGuid();
        var resultId = Guid.NewGuid();
        var repository = CreateRepository(projectId, resultId);
        var manifestPath = Path.Combine(root, projectId.ToString("N"), resultId.ToString("N"), "manifest.json");
        Directory.CreateDirectory(Path.GetDirectoryName(manifestPath)!);
        await File.WriteAllTextAsync(manifestPath, "{not-json");

        try
        {
            var service = CreateService(root, repository);

            var read = await service.GetManifestAsync(projectId, resultId);

            read.Found.Should().BeFalse();
            read.Status.Should().Be("missing");
            read.ErrorCode.Should().Be("EvidenceManifestUnreadable");
            read.Summary.EvidenceStatus.Should().Be("missing");
        }
        finally
        {
            DeleteTempDirectory(root);
        }
    }

    [Fact]
    public async Task ExportAsync_WhenPackageExceedsLimit_ShouldFailSoft()
    {
        var root = CreateTempDirectory();
        var projectId = Guid.NewGuid();
        var result = CreateResult(projectId, InspectionStatus.NG);
        result.SetOutputDataJson("""{"notes":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"}""");
        var repository = CreateRepository(projectId, result.Id);

        try
        {
            var service = CreateService(root, repository, new Dictionary<string, string?>
            {
                ["Evidence:Studio:MaxExportBytes"] = "64"
            });
            await service.CaptureAsync(result);

            var export = await service.ExportAsync(projectId, result.Id);

            export.Success.Should().BeFalse();
            export.ErrorCode.Should().Be("EvidenceExportTooLarge");
            export.Status.Should().Be("too-large");
        }
        finally
        {
            DeleteTempDirectory(root);
        }
    }

    [Fact]
    public async Task ExportAsync_ShouldScopeLookupByProjectAndResult()
    {
        var root = CreateTempDirectory();
        var requestedProject = Guid.NewGuid();
        var requestedResult = Guid.NewGuid();
        var repository = Substitute.For<IInspectionResultRepository>();
        repository.GetHistoryDetailAsync(requestedProject, requestedResult)
            .Returns(Task.FromResult<InspectionHistoryDetail?>(null));

        try
        {
            var service = CreateService(root, repository);

            var export = await service.ExportAsync(requestedProject, requestedResult);

            export.Success.Should().BeFalse();
            export.ErrorCode.Should().Be("InspectionResultNotFound");
            await repository.Received(1).GetHistoryDetailAsync(requestedProject, requestedResult);
        }
        finally
        {
            DeleteTempDirectory(root);
        }
    }

    [Fact]
    public async Task ApplyRetentionAsync_ShouldDeleteExpiredThenCapacityCandidatesDeterministically()
    {
        var root = CreateTempDirectory();
        var projectId = Guid.NewGuid();
        var expiredId = Guid.NewGuid();
        var shortId = Guid.NewGuid();
        var longId = Guid.NewGuid();

        try
        {
            await WriteSyntheticManifestAsync(root, projectId, expiredId, "short", DateTimeOffset.UtcNow.AddDays(-2), 96);
            await WriteSyntheticManifestAsync(root, projectId, shortId, "short", DateTimeOffset.UtcNow.AddDays(10), 96);
            await WriteSyntheticManifestAsync(root, projectId, longId, "long", DateTimeOffset.UtcNow.AddDays(10), 96);
            var longRoot = Path.Combine(root, projectId.ToString("N"), longId.ToString("N"));
            var capacityBytes = Directory.EnumerateFiles(longRoot, "*", SearchOption.AllDirectories)
                .Sum(path => new FileInfo(path).Length) + 16;

            var service = CreateService(root, additionalConfiguration: new Dictionary<string, string?>
            {
                ["Evidence:Studio:MaxTotalBytes"] = capacityBytes.ToString()
            });

            var cleanup = await service.ApplyRetentionAsync();

            cleanup.DeletedManifestCount.Should().Be(2);
            Directory.Exists(Path.Combine(root, projectId.ToString("N"), expiredId.ToString("N"))).Should().BeFalse();
            Directory.Exists(Path.Combine(root, projectId.ToString("N"), shortId.ToString("N"))).Should().BeFalse();
            Directory.Exists(Path.Combine(root, projectId.ToString("N"), longId.ToString("N"))).Should().BeTrue();
        }
        finally
        {
            DeleteTempDirectory(root);
        }
    }

    [Fact]
    public async Task ManifestReads_WhenProjectIsNotActive_ShouldReturnNotFoundWithoutReadingResult()
    {
        var root = CreateTempDirectory();
        try
        {
            var projectId = Guid.NewGuid();
            var resultId = Guid.NewGuid();
            var repository = Substitute.For<IInspectionResultRepository>();
            var projectRepository = Substitute.For<IProjectRepository>();
            projectRepository.GetByIdFreshAsync(projectId).Returns(Task.FromResult<Project?>(null));
            var service = CreateService(root, repository, projectRepository: projectRepository);

            var manifest = await service.GetManifestAsync(projectId, resultId);
            var export = await service.ExportAsync(projectId, resultId);

            manifest.Found.Should().BeFalse();
            manifest.ErrorCode.Should().Be("InspectionResultNotFound");
            export.Success.Should().BeFalse();
            export.ErrorCode.Should().Be("InspectionResultNotFound");
            repository.ReceivedCalls().Should().BeEmpty();
        }
        finally
        {
            DeleteTempDirectory(root);
        }
    }

    private static InspectionEvidenceManifestService CreateService(
        string root,
        IInspectionResultRepository? repository = null,
        Dictionary<string, string?>? additionalConfiguration = null,
        IProjectRepository? projectRepository = null)
    {
        var configuration = new Dictionary<string, string?>
        {
            ["Evidence:Studio:RootPath"] = root
        };
        if (additionalConfiguration != null)
        {
            foreach (var pair in additionalConfiguration)
            {
                configuration[pair.Key] = pair.Value;
            }
        }

        return new InspectionEvidenceManifestService(
            repository ?? Substitute.For<IInspectionResultRepository>(),
            NullLogger<InspectionEvidenceManifestService>.Instance,
            new ConfigurationBuilder().AddInMemoryCollection(configuration).Build(),
            projectRepository: projectRepository);
    }

    private static IInspectionResultRepository CreateRepository(Guid projectId, Guid resultId)
    {
        var repository = Substitute.For<IInspectionResultRepository>();
        repository.GetHistoryDetailAsync(projectId, resultId)
            .Returns(Task.FromResult<InspectionHistoryDetail?>(CreateDetail(projectId, resultId)));
        return repository;
    }

    private static InspectionResult CreateResult(Guid projectId, InspectionStatus status)
    {
        var result = new InspectionResult(projectId);
        result.SetResult(status, 25, status == InspectionStatus.OK ? 0.99 : 0.42, status == InspectionStatus.Error ? "failed" : null);
        result.SetTraceability("FLOW-HASH", "bundle-authority", Guid.NewGuid());
        result.SetAnalysisDataJson("""{"cards":[{"fields":[{"key":"apiKey","value":"hidden"}]}]}""");
        result.AddDefect(new Defect(result.Id, DefectType.Other, 1, 2, 3, 4, 0.8, "defect"));
        return result;
    }

    private static InspectionHistoryDetail CreateDetail(Guid projectId, Guid resultId)
    {
        return new InspectionHistoryDetail
        {
            Id = resultId,
            ProjectId = projectId,
            Status = InspectionStatus.NG,
            InspectionTime = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            ProcessingTimeMs = 25,
            HasOutputData = true,
            OutputDataJson = """{"score":42}"""
        };
    }

    private static async Task<InspectionEvidenceManifestV1> ReadManifestAsync(string root, Guid projectId, Guid resultId)
    {
        var manifestPath = Path.Combine(root, projectId.ToString("N"), resultId.ToString("N"), "manifest.json");
        var json = await File.ReadAllTextAsync(manifestPath);
        return JsonSerializer.Deserialize<InspectionEvidenceManifestV1>(json, ReadJsonOptions)!;
    }

    private static async Task WriteSyntheticManifestAsync(
        string root,
        Guid projectId,
        Guid resultId,
        string retentionClass,
        DateTimeOffset expiresAt,
        int itemBytes)
    {
        var resultRoot = Path.Combine(root, projectId.ToString("N"), resultId.ToString("N"));
        var itemRelativePath = "items/payload.json";
        var itemPath = Path.Combine(resultRoot, "items", "payload.json");
        Directory.CreateDirectory(Path.GetDirectoryName(itemPath)!);
        var payload = Encoding.UTF8.GetBytes(new string('x', itemBytes));
        await File.WriteAllBytesAsync(itemPath, payload);

        var manifest = new InspectionEvidenceManifestV1
        {
            ManifestId = $"synthetic_{resultId:N}",
            ProjectId = projectId,
            InspectionResultId = resultId,
            Status = "NG",
            Outcome = "NG",
            CreatedAtUtc = expiresAt.AddDays(-10),
            RetentionClass = retentionClass,
            RetentionExpiresAtUtc = expiresAt,
            TotalBytes = payload.Length,
            Items =
            [
                new InspectionEvidenceItemV1
                {
                    Id = "payload",
                    Role = "report-json",
                    ContentType = "application/json",
                    RelativePath = itemRelativePath,
                    SizeBytes = payload.Length,
                    Sha256 = Sha256(payload),
                    CreatedAtUtc = expiresAt.AddDays(-10),
                    RetentionClass = retentionClass
                }
            ]
        };
        manifest.Checksum = InspectionEvidenceManifestService.ComputeManifestChecksum(manifest);
        await File.WriteAllTextAsync(
            Path.Combine(resultRoot, "manifest.json"),
            JsonSerializer.Serialize(manifest, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
    }

    private static string Sha256(byte[] bytes)
    {
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "ClearVisionEvidenceManifestTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteTempDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }

        var tempRoot = Path.Combine(Path.GetTempPath(), "ClearVisionEvidenceManifestTests");
        if (Directory.Exists(tempRoot) && !Directory.EnumerateFileSystemEntries(tempRoot).Any())
        {
            Directory.Delete(tempRoot);
        }
    }
}
