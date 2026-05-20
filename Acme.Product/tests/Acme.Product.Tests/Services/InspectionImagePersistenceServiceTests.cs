using Acme.Product.Core.Entities;
using Acme.Product.Core.Enums;
using Acme.Product.Core.Interfaces;
using Acme.Product.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using OpenCvSharp;

namespace Acme.Product.Tests.Services;

public sealed class InspectionImagePersistenceServiceTests
{
    [Fact]
    public async Task PersistAsync_WithNgOutputImage_ShouldWriteCompressedJpegToNgFolder()
    {
        var root = CreateTempDirectory();
        var outputImage = CreatePngImageBytes();
        var configService = Substitute.For<IConfigurationService>();
        configService.GetCurrent().Returns(new AppConfig
        {
            Storage = new StorageConfig
            {
                ImageSavePath = root,
                SavePolicy = "NgOnly"
            }
        });
        var service = new InspectionImagePersistenceService(
            configService,
            NullLogger<InspectionImagePersistenceService>.Instance);
        var result = new InspectionResult(Guid.NewGuid());
        result.SetResult(InspectionStatus.NG, 12);
        result.SetOutputImage(outputImage);

        try
        {
            await service.PersistAsync(result);

            var targetDir = Path.Combine(root, DateTime.Now.ToString("yyyyMMdd"), "NG");
            var filePath = Directory.EnumerateFiles(targetDir, "*.jpg").Should().ContainSingle().Subject;
            var savedBytes = await File.ReadAllBytesAsync(filePath);

            savedBytes.Should().NotEqual(outputImage);
            savedBytes[0].Should().Be(0xFF);
            savedBytes[1].Should().Be(0xD8);
        }
        finally
        {
            DeleteTempDirectory(root);
        }
    }

    [Fact]
    public async Task PersistAsync_WithNgOnlyPolicy_ShouldSkipOkOutputImages()
    {
        var root = CreateTempDirectory();
        var configService = Substitute.For<IConfigurationService>();
        configService.GetCurrent().Returns(new AppConfig
        {
            Storage = new StorageConfig
            {
                ImageSavePath = root,
                SavePolicy = "NgOnly"
            }
        });
        var service = new InspectionImagePersistenceService(
            configService,
            NullLogger<InspectionImagePersistenceService>.Instance);
        var result = new InspectionResult(Guid.NewGuid());
        result.SetResult(InspectionStatus.OK, 7);
        result.SetOutputImage(CreatePngImageBytes());

        try
        {
            await service.PersistAsync(result);

            Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Should().BeEmpty();
        }
        finally
        {
            DeleteTempDirectory(root);
        }
    }

    [Fact]
    public async Task PersistAsync_WhenConfiguredDriveIsUnavailable_ShouldFallbackToLocalAppDataImages()
    {
        var outputImage = CreatePngImageBytes();
        var configService = Substitute.For<IConfigurationService>();
        configService.GetCurrent().Returns(new AppConfig
        {
            Storage = new StorageConfig
            {
                ImageSavePath = CreateUnavailableRootPath(),
                SavePolicy = "NgOnly"
            }
        });
        var service = new InspectionImagePersistenceService(
            configService,
            NullLogger<InspectionImagePersistenceService>.Instance);
        var projectId = Guid.NewGuid();
        var result = new InspectionResult(projectId);
        result.SetResult(InspectionStatus.NG, 12);
        result.SetOutputImage(outputImage);

        try
        {
            await service.PersistAsync(result);

            var fallbackDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ClearVision",
                "Images",
                DateTime.Now.ToString("yyyyMMdd"),
                "NG");
            var savedPath = Directory
                .EnumerateFiles(fallbackDir, $"{projectId:N}_{result.Id:N}_*.jpg")
                .Should()
                .ContainSingle()
                .Subject;

            File.Exists(savedPath).Should().BeTrue();
        }
        finally
        {
            DeleteFallbackSavedFiles(projectId, result.Id);
        }
    }

    private static byte[] CreatePngImageBytes()
    {
        using var image = new Mat(48, 64, MatType.CV_8UC3, new Scalar(48, 96, 180));
        Cv2.Rectangle(image, new Rect(8, 8, 24, 20), new Scalar(0, 0, 255), thickness: 2);
        Cv2.ImEncode(".png", image, out var bytes);
        return bytes;
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "ClearVisionImagePersistenceTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static string CreateUnavailableRootPath()
    {
        foreach (var letter in Enumerable.Range(0, 26).Select(offset => (char)('Z' - offset)))
        {
            var root = $"{letter}:\\";
            if (!Directory.Exists(root))
            {
                return Path.Combine(root, "ClearVisionImagePersistenceTests", Guid.NewGuid().ToString("N"));
            }
        }

        return string.Empty;
    }

    private static void DeleteTempDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private static void DeleteFallbackSavedFiles(Guid projectId, Guid resultId)
    {
        var fallbackDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ClearVision",
            "Images",
            DateTime.Now.ToString("yyyyMMdd"),
            "NG");
        if (!Directory.Exists(fallbackDir))
        {
            return;
        }

        foreach (var path in Directory.EnumerateFiles(fallbackDir, $"{projectId:N}_{resultId:N}_*"))
        {
            File.Delete(path);
        }
    }
}
