using ClearVision.Product.Application.Services;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Interfaces;
using ClearVision.Product.Infrastructure.Services;
using ClearVision.Product.Tests.TestSupport;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using OpenCvSharp;

namespace ClearVision.Product.Tests.Services;

[TestClassification(TestDomain.General, TestPurpose.Regression, TestLane.Pr, TestEvidenceType.Contract, TestOracleType.Contract, TestResourceRequirement.None, TestExpectedDuration.Fast, TestFlakyPolicy.Blocking, "product", Suites = "ServicesRegression")]
public sealed class InspectionImagePersistenceServiceTests
{
    [Fact]
    public async Task PersistAsync_WithNgOutputImage_ShouldWriteCompressedJpegToNgFolder()
    {
        var root = CreateTempDirectory();
        var outputImage = CreatePngImageBytes();
        var configService = CreateConfigService(root);
        var service = CreatePersistenceService(configService);
        var result = new InspectionResult(Guid.NewGuid());
        result.SetResult(InspectionStatus.NG, 12);
        result.SetOutputImage(outputImage);

        try
        {
            await service.PersistAsync(result);

            var filePath = FindSavedImagePath(root, result, ".jpg");
            var savedBytes = await File.ReadAllBytesAsync(filePath);

            Path.GetFileName(Path.GetDirectoryName(filePath)).Should().Be("NG");
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
    public async Task PersistAsync_WithSixteenBitBgraOutputImage_ShouldWriteJpeg()
    {
        var root = CreateTempDirectory();
        var outputImage = CreateSixteenBitBgraPngImageBytes();
        var configService = CreateConfigService(root);
        var service = CreatePersistenceService(configService);
        var result = new InspectionResult(Guid.NewGuid());
        result.SetResult(InspectionStatus.NG, 12);
        result.SetOutputImage(outputImage);

        try
        {
            await service.PersistAsync(result);

            var filePath = FindSavedImagePath(root, result, ".jpg");
            var savedBytes = await File.ReadAllBytesAsync(filePath);
            using var decoded = Cv2.ImDecode(savedBytes, ImreadModes.Unchanged);

            Path.GetFileName(Path.GetDirectoryName(filePath)).Should().Be("NG");
            savedBytes[0].Should().Be(0xFF);
            savedBytes[1].Should().Be(0xD8);
            decoded.Depth().Should().Be(MatType.CV_8U);
            decoded.Channels().Should().Be(3);
            var mean = Cv2.Mean(decoded);
            mean.Val0.Should().BeInRange(110, 145);
            mean.Val1.Should().BeInRange(110, 145);
            mean.Val2.Should().BeInRange(110, 145);
        }
        finally
        {
            DeleteTempDirectory(root);
        }
    }

    [Fact]
    public async Task PersistAsync_WithSixteenBitGrayscaleOutputImage_ShouldWriteJpegWithoutClipping()
    {
        var root = CreateTempDirectory();
        var outputImage = CreateSixteenBitGrayscalePngImageBytes();
        var configService = CreateConfigService(root);
        var service = CreatePersistenceService(configService);
        var result = new InspectionResult(Guid.NewGuid());
        result.SetResult(InspectionStatus.NG, 12);
        result.SetOutputImage(outputImage);

        try
        {
            await service.PersistAsync(result);

            var filePath = FindSavedImagePath(root, result, ".jpg");
            var savedBytes = await File.ReadAllBytesAsync(filePath);
            using var decoded = Cv2.ImDecode(savedBytes, ImreadModes.Unchanged);

            savedBytes[0].Should().Be(0xFF);
            savedBytes[1].Should().Be(0xD8);
            decoded.Depth().Should().Be(MatType.CV_8U);
            decoded.Channels().Should().Be(1);
            Cv2.Mean(decoded).Val0.Should().BeInRange(110, 145);
        }
        finally
        {
            DeleteTempDirectory(root);
        }
    }

    [Fact]
    public async Task PersistAsync_WithUndecodableOutputImage_ShouldWriteOriginalBytes()
    {
        var root = CreateTempDirectory();
        var outputImage = new byte[] { 0x01, 0x02, 0x03, 0x04 };
        var configService = CreateConfigService(root);
        var service = CreatePersistenceService(configService);
        var result = new InspectionResult(Guid.NewGuid());
        result.SetResult(InspectionStatus.NG, 12);
        result.SetOutputImage(outputImage);

        try
        {
            await service.PersistAsync(result);

            var filePath = FindSavedImagePath(root, result, ".bin");
            var savedBytes = await File.ReadAllBytesAsync(filePath);

            Path.GetFileName(Path.GetDirectoryName(filePath)).Should().Be("NG");
            savedBytes.Should().Equal(outputImage);
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
        var configService = CreateConfigService(root);
        var service = CreatePersistenceService(configService);
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
    public async Task PersistAsync_WhenCancellationRequested_ShouldThrow()
    {
        var root = CreateTempDirectory();
        var configService = CreateConfigService(root);
        var service = CreatePersistenceService(configService);
        var result = new InspectionResult(Guid.NewGuid());
        result.SetResult(InspectionStatus.NG, 7);
        result.SetOutputImage(CreatePngImageBytes());
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        try
        {
            var act = () => service.PersistAsync(result, cts.Token);

            await act.Should().ThrowAsync<OperationCanceledException>();
            Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Should().BeEmpty();
        }
        finally
        {
            DeleteTempDirectory(root);
        }
    }

    [Fact]
    public async Task NullPersistence_WhenCancellationRequested_ShouldThrow()
    {
        var result = new InspectionResult(Guid.NewGuid());
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => NullInspectionImagePersistenceService.Instance.PersistAsync(result, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task QueuedPersistence_WhenCancellationRequested_ShouldThrowWithoutEnqueueing()
    {
        var root = CreateTempDirectory();
        var configService = CreateConfigService(root);
        var inner = CreatePersistenceService(configService);
        var service = new QueuedInspectionImagePersistenceService(
            configService,
            inner,
            NullLogger<QueuedInspectionImagePersistenceService>.Instance);
        var result = new InspectionResult(Guid.NewGuid());
        result.SetResult(InspectionStatus.NG, 7);
        result.SetOutputImage(CreatePngImageBytes());
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        try
        {
            var act = () => service.PersistAsync(result, cts.Token);

            await act.Should().ThrowAsync<OperationCanceledException>();
            Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Should().BeEmpty();
        }
        finally
        {
            DeleteTempDirectory(root);
        }
    }

    [Fact]
    public async Task QueuedPersistence_ShouldSnapshotOutputImageBeforeReturning()
    {
        var root = CreateTempDirectory();
        var outputImage = CreatePngImageBytes();
        var configService = CreateConfigService(root);
        var inner = CreatePersistenceService(configService);
        var service = new QueuedInspectionImagePersistenceService(
            configService,
            inner,
            NullLogger<QueuedInspectionImagePersistenceService>.Instance);
        var result = new InspectionResult(Guid.NewGuid());
        result.SetResult(InspectionStatus.NG, 7);
        result.SetOutputImage(outputImage);
        var started = false;

        try
        {
            await service.PersistAsync(result);
            Array.Clear(outputImage);
            await service.StartAsync(CancellationToken.None);
            started = true;
            await service.StopAsync(CancellationToken.None);
            started = false;

            var filePath = FindSavedImagePath(root, result, ".jpg");
            var savedBytes = await File.ReadAllBytesAsync(filePath);

            savedBytes[0].Should().Be(0xFF);
            savedBytes[1].Should().Be(0xD8);
        }
        finally
        {
            if (started)
            {
                await service.StopAsync(CancellationToken.None);
            }

            service.Dispose();
            DeleteTempDirectory(root);
        }
    }

    [Fact]
    public async Task QueuedPersistence_WhenImageByteBudgetIsFull_ShouldSkipAdditionalSnapshots()
    {
        var root = CreateTempDirectory();
        var outputImage = CreatePngImageBytes();
        var configService = CreateConfigService(root);
        var inner = CreatePersistenceService(configService);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Performance:Persistence:MaxQueuedImageBytes"] = (outputImage.Length + 1).ToString()
            })
            .Build();
        var service = new QueuedInspectionImagePersistenceService(
            configService,
            inner,
            NullLogger<QueuedInspectionImagePersistenceService>.Instance,
            configuration);
        var first = new InspectionResult(Guid.NewGuid());
        first.SetResult(InspectionStatus.NG, 7);
        first.SetOutputImage(outputImage);
        var second = new InspectionResult(Guid.NewGuid());
        second.SetResult(InspectionStatus.NG, 8);
        second.SetOutputImage(outputImage.ToArray());
        var started = false;

        try
        {
            await service.PersistAsync(first);
            await service.PersistAsync(second);

            await service.StartAsync(CancellationToken.None);
            started = true;
            await service.StopAsync(CancellationToken.None);
            started = false;

            Directory.EnumerateFiles(root, "*.jpg", SearchOption.AllDirectories)
                .Should()
                .ContainSingle();
            FindSavedImagePath(root, first, ".jpg").Should().NotBeNullOrWhiteSpace();
            Directory.EnumerateFiles(root, $"{second.ProjectId:N}_{second.Id:N}_*.jpg", SearchOption.AllDirectories)
                .Should()
                .BeEmpty();
        }
        finally
        {
            if (started)
            {
                await service.StopAsync(CancellationToken.None);
            }

            service.Dispose();
            DeleteTempDirectory(root);
        }
    }

    [Fact]
    public async Task QueuedPersistence_StopAsync_WhenDrainTimesOut_ShouldDropPendingSnapshotsAndReleaseQueueBudget()
    {
        var root = CreateTempDirectory();
        var outputImage = CreatePngImageBytes();
        var configService = CreateConfigService(root);
        var inner = new BlockingImagePersistenceService();
        var logger = new RecordingLogger<QueuedInspectionImagePersistenceService>();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Performance:Persistence:ShutdownDrainTimeoutMs"] = "10"
            })
            .Build();
        var service = new QueuedInspectionImagePersistenceService(
            configService,
            inner,
            logger,
            configuration);
        var first = new InspectionResult(Guid.NewGuid());
        first.SetResult(InspectionStatus.NG, 7);
        first.SetOutputImage(outputImage);
        var second = new InspectionResult(Guid.NewGuid());
        second.SetResult(InspectionStatus.NG, 8);
        second.SetOutputImage(outputImage.ToArray());
        var started = false;

        try
        {
            await service.PersistAsync(first);
            await service.PersistAsync(second);
            service.QueuedImageBytes.Should().Be(outputImage.Length * 2L);

            await service.StartAsync(CancellationToken.None);
            started = true;
            await inner.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await service.StopAsync(cts.Token);
            started = false;

            inner.StartedResultIds.Should().ContainSingle().Which.Should().Be(first.Id);
            service.QueuedImageBytes.Should().Be(0);
            service.DroppedImageCount.Should().Be(2);
            logger.Entries.Should().Contain(entry =>
                entry.Level == Microsoft.Extensions.Logging.LogLevel.Warning &&
                entry.Message.Contains("已放弃 1 张尚未开始保存的图像", StringComparison.Ordinal));
        }
        finally
        {
            if (started)
            {
                await service.StopAsync(CancellationToken.None);
            }

            service.Dispose();
            DeleteTempDirectory(root);
        }
    }

    [Fact]
    public async Task PersistAsync_WhenConfiguredDriveIsUnavailable_ShouldFallbackToLocalAppDataImages()
    {
        var outputImage = CreatePngImageBytes();
        var configService = CreateConfigService(CreateUnavailableRootPath());
        var service = CreatePersistenceService(configService);
        var projectId = Guid.NewGuid();
        var result = new InspectionResult(projectId);
        result.SetResult(InspectionStatus.NG, 12);
        result.SetOutputImage(outputImage);
        var fallbackSnapshot = FallbackImageDirectorySnapshot.Capture();

        try
        {
            await service.PersistAsync(result);

            var fallbackRoot = InspectionImagePersistencePaths.GetFallbackImageSaveRoot();
            var savedPath = FindSavedImagePath(fallbackRoot, result, ".jpg");

            File.Exists(savedPath).Should().BeTrue();
        }
        finally
        {
            fallbackSnapshot.DeleteSavedFiles(projectId, result.Id);
        }
    }

    [Fact]
    public async Task PersistAsync_ShouldTrimExpiredFilesOnlyFromManifestOwnedRoot()
    {
        var root = CreateTempDirectory();
        var now = new DateTimeOffset(2026, 8, 31, 12, 0, 0, TimeSpan.Zero);
        var oldDay = Path.Combine(root, "20260801", "NG");
        Directory.CreateDirectory(oldDay);
        await File.WriteAllBytesAsync(Path.Combine(oldDay, "expired.jpg"), new byte[] { 1, 2, 3 });
        await File.WriteAllTextAsync(
            Path.Combine(root, ".clearvision-inspection-images.manifest"),
            "ClearVision.InspectionImages.v1");
        var configService = CreateConfigService(root, retentionDays: 7, minFreeSpaceGb: 0);
        var service = CreatePersistenceService(configService, new FixedFreeSpaceProvider(64L * 1024 * 1024 * 1024), () => now);
        var result = new InspectionResult(Guid.NewGuid());
        result.SetResult(InspectionStatus.NG, 12);
        result.SetOutputImage(CreatePngImageBytes());

        try
        {
            await service.PersistAsync(result);

            Directory.Exists(Path.Combine(root, "20260801")).Should().BeFalse();
            var health = service.GetStorageHealth();
            health.TrimmedFileCount.Should().Be(1);
            health.GapDetected.Should().BeTrue();
            health.Degraded.Should().BeFalse();
        }
        finally
        {
            DeleteTempDirectory(root);
        }
    }

    [Fact]
    public async Task PersistAsync_ShouldNotClaimOrCleanAnExistingUnownedDirectory()
    {
        var root = CreateTempDirectory();
        var foreignDay = Path.Combine(root, "20200101", "NG");
        Directory.CreateDirectory(foreignDay);
        var foreignFile = Path.Combine(foreignDay, "foreign.jpg");
        await File.WriteAllBytesAsync(foreignFile, new byte[] { 4, 5, 6 });
        await File.WriteAllTextAsync(Path.Combine(root, "foreign.txt"), "not owned by ClearVision");
        var configService = CreateConfigService(root, retentionDays: 1, minFreeSpaceGb: 0);
        var service = CreatePersistenceService(configService, new FixedFreeSpaceProvider(64L * 1024 * 1024 * 1024));
        var result = new InspectionResult(Guid.NewGuid());
        result.SetResult(InspectionStatus.NG, 12);
        result.SetOutputImage(CreatePngImageBytes());

        try
        {
            await service.PersistAsync(result);

            File.Exists(foreignFile).Should().BeTrue();
            File.ReadAllText(Path.Combine(root, "foreign.txt")).Should().Be("not owned by ClearVision");
            var managedRoot = Path.Combine(root, ".clearvision-managed-images");
            File.Exists(Path.Combine(managedRoot, ".clearvision-inspection-images.manifest")).Should().BeTrue();
            FindSavedImagePath(managedRoot, result, ".jpg").Should().NotBeNullOrWhiteSpace();
        }
        finally
        {
            DeleteTempDirectory(root);
        }
    }

    [Fact]
    public void EnsureProductionStartAllowed_WhenFreeSpaceIsBelowConfiguredFloor_ShouldFailClosedAndReportDegradedHealth()
    {
        var root = CreateTempDirectory();
        var configService = CreateConfigService(root, minFreeSpaceGb: 5);
        var service = CreatePersistenceService(configService, new FixedFreeSpaceProvider(4L * 1024 * 1024 * 1024));

        try
        {
            var act = () => service.EnsureProductionStartAllowed();

            act.Should().Throw<InvalidOperationException>()
                .WithMessage("INSPECTION_STORAGE_START_BLOCKED*");
            service.GetStorageHealth().Degraded.Should().BeTrue();
        }
        finally
        {
            DeleteTempDirectory(root);
        }
    }

    private static IConfigurationService CreateConfigService(
        string imageSavePath,
        string savePolicy = "NgOnly",
        int retentionDays = 30,
        int minFreeSpaceGb = 5)
    {
        var configService = Substitute.For<IConfigurationService>();
        configService.GetCurrent().Returns(new AppConfig
        {
            Storage = new StorageConfig
            {
                ImageSavePath = imageSavePath,
                SavePolicy = savePolicy,
                RetentionDays = retentionDays,
                MinFreeSpaceGb = minFreeSpaceGb
            }
        });
        return configService;
    }

    private static InspectionImagePersistenceService CreatePersistenceService(
        IConfigurationService configService,
        IInspectionStorageFreeSpaceProvider? freeSpaceProvider = null,
        Func<DateTimeOffset>? utcNow = null)
    {
        return new InspectionImagePersistenceService(
            configService,
            NullLogger<InspectionImagePersistenceService>.Instance,
            freeSpaceProvider,
            utcNow);
    }

    private static byte[] CreatePngImageBytes()
    {
        using var image = new Mat(48, 64, MatType.CV_8UC3, new Scalar(48, 96, 180));
        Cv2.Rectangle(image, new Rect(8, 8, 24, 20), new Scalar(0, 0, 255), thickness: 2);
        Cv2.ImEncode(".png", image, out var bytes);
        return bytes;
    }

    private static byte[] CreateSixteenBitBgraPngImageBytes()
    {
        using var image = new Mat(24, 32, MatType.CV_16UC4, new Scalar(32768, 32768, 32768, 65535));
        Cv2.ImEncode(".png", image, out var bytes);
        return bytes;
    }

    private static byte[] CreateSixteenBitGrayscalePngImageBytes()
    {
        using var image = new Mat(24, 32, MatType.CV_16UC1, Scalar.All(32768));
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

    private static string FindSavedImagePath(string root, InspectionResult result, string extension)
    {
        Directory.Exists(root).Should().BeTrue();

        return Directory
            .EnumerateFiles(root, $"{result.ProjectId:N}_{result.Id:N}_*{extension}", SearchOption.AllDirectories)
            .Should()
            .ContainSingle()
            .Subject;
    }

    private sealed class BlockingImagePersistenceService : IInspectionImagePersistenceService
    {
        public TaskCompletionSource<InspectionResult> Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public List<Guid> StartedResultIds { get; } = new();

        public async Task PersistAsync(InspectionResult result, CancellationToken cancellationToken = default)
        {
            StartedResultIds.Add(result.Id);
            Started.TrySetResult(result);
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
    }

    private sealed class FixedFreeSpaceProvider : IInspectionStorageFreeSpaceProvider
    {
        private readonly long? _availableBytes;

        public FixedFreeSpaceProvider(long? availableBytes)
        {
            _availableBytes = availableBytes;
        }

        public long? GetAvailableFreeBytes(string path) => _availableBytes;
    }

    private static void DeleteTempDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }

        var tempRoot = Path.Combine(Path.GetTempPath(), "ClearVisionImagePersistenceTests");
        if (Directory.Exists(tempRoot) && !Directory.EnumerateFileSystemEntries(tempRoot).Any())
        {
            Directory.Delete(tempRoot);
        }
    }

}
