using Acme.Product.Core.Entities;
using Acme.Product.Core.Enums;
using Acme.Product.Infrastructure.Data;
using Acme.Product.Infrastructure.Repositories;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Acme.Product.Tests.Repositories;

public sealed class InspectionResultRepositoryTests
{
    [Fact]
    public async Task GetStatisticsAsync_ShouldAggregateInDatabase()
    {
        var root = CreateTempPath();
        try
        {
            await using var db = CreateContext(root);
            await db.Database.EnsureCreatedAsync();
            var repository = new InspectionResultRepository(db);
            var projectId = Guid.NewGuid();

            await repository.AddRangeAsync(
            [
                CreateResult(projectId, InspectionStatus.OK, 10),
                CreateResult(projectId, InspectionStatus.NG, 20),
                CreateResult(projectId, InspectionStatus.Error, 30),
                CreateResult(Guid.NewGuid(), InspectionStatus.OK, 100)
            ]);

            var stats = await repository.GetStatisticsAsync(projectId);

            stats.TotalCount.Should().Be(3);
            stats.OKCount.Should().Be(1);
            stats.NGCount.Should().Be(1);
            stats.ErrorCount.Should().Be(1);
            stats.AverageProcessingTimeMs.Should().Be(20);
            stats.OKRate.Should().BeApproximately(1.0 / 3.0, 0.0001);
        }
        finally
        {
            DeleteDirectoryIfExists(root);
        }
    }

    [Fact]
    public async Task GetHistoryPageAsync_ShouldReturnLightweightItemsWithoutOutputImage()
    {
        var root = CreateTempPath();
        try
        {
            await using var db = CreateContext(root);
            await db.Database.EnsureCreatedAsync();
            var repository = new InspectionResultRepository(db);
            var projectId = Guid.NewGuid();
            var imageId = Guid.NewGuid();
            var result = CreateResult(projectId, InspectionStatus.NG, 42);
            result.SetImageId(imageId);
            result.SetOutputImage([1, 2, 3, 4, 5]);
            result.SetOutputDataJson("""{"score":42}""");
            result.SetAnalysisDataJson("""{"version":1,"cards":[]}""");
            result.AddDefect(new Defect(result.Id, DefectType.Scratch, 1, 2, 3, 4, 0.9, "scratch"));

            await repository.AddRangeAsync([result]);
            db.ChangeTracker.Clear();

            var page = await repository.GetHistoryPageAsync(projectId);

            page.TotalCount.Should().Be(1);
            var item = page.Items.Should().ContainSingle().Subject;
            item.Id.Should().Be(result.Id);
            item.ProjectId.Should().Be(projectId);
            item.Status.Should().Be(InspectionStatus.NG);
            item.ImageId.Should().Be(imageId);
            item.ProcessingTimeMs.Should().Be(42);
            item.OutputDataJson.Should().Contain("score");
            item.Defects.Should().ContainSingle();
            item.GetType().GetProperty("OutputImage").Should().BeNull();
        }
        finally
        {
            DeleteDirectoryIfExists(root);
        }
    }

    [Fact]
    public async Task GetByTimeRangeAsync_ShouldReturnAnalysisItemsWithoutOutputImage()
    {
        var root = CreateTempPath();
        try
        {
            await using var db = CreateContext(root);
            await db.Database.EnsureCreatedAsync();
            var repository = new InspectionResultRepository(db);
            var projectId = Guid.NewGuid();
            var imageId = Guid.NewGuid();
            var outputImage = new byte[] { 9, 8, 7, 6 };
            var result = CreateResult(projectId, InspectionStatus.NG, 64);
            result.SetImageId(imageId);
            result.SetOutputImage(outputImage);
            result.SetOutputDataJson("""{"score":64}""");
            result.AddDefect(new Defect(result.Id, DefectType.Stain, 5, 6, 7, 8, 0.85, "stain"));

            await repository.AddRangeAsync([result]);
            db.ChangeTracker.Clear();

            var rangeResults = await repository.GetByTimeRangeAsync(
                projectId,
                DateTime.UtcNow.AddDays(-1),
                DateTime.UtcNow.AddDays(1));

            var item = rangeResults.Should().ContainSingle().Subject;
            item.Id.Should().Be(result.Id);
            item.ProjectId.Should().Be(projectId);
            item.Status.Should().Be(InspectionStatus.NG);
            item.ProcessingTimeMs.Should().Be(64);
            item.ImageId.Should().Be(imageId);
            item.OutputImage.Should().BeNull();
            item.OutputDataJson.Should().Contain("score");
            item.Defects.Should().ContainSingle();

            var persisted = await db.InspectionResults
                .AsNoTracking()
                .SingleAsync(stored => stored.Id == result.Id);
            persisted.OutputImage.Should().Equal(outputImage);
        }
        finally
        {
            DeleteDirectoryIfExists(root);
        }
    }

    [Fact]
    public async Task GetByProjectIdAsync_ShouldReturnLightweightItemsWithoutOutputImage()
    {
        var root = CreateTempPath();
        try
        {
            await using var db = CreateContext(root);
            await db.Database.EnsureCreatedAsync();
            var repository = new InspectionResultRepository(db);
            var projectId = Guid.NewGuid();
            var outputImage = new byte[] { 4, 3, 2, 1 };
            var result = CreateResult(projectId, InspectionStatus.OK, 21);
            result.SetOutputImage(outputImage);

            await repository.AddRangeAsync([result]);
            db.ChangeTracker.Clear();

            var projectResults = await repository.GetByProjectIdAsync(projectId);

            var item = projectResults.Should().ContainSingle().Subject;
            item.Id.Should().Be(result.Id);
            item.ProjectId.Should().Be(projectId);
            item.Status.Should().Be(InspectionStatus.OK);
            item.ProcessingTimeMs.Should().Be(21);
            item.OutputImage.Should().BeNull();

            var persisted = await db.InspectionResults
                .AsNoTracking()
                .SingleAsync(stored => stored.Id == result.Id);
            persisted.OutputImage.Should().Equal(outputImage);
        }
        finally
        {
            DeleteDirectoryIfExists(root);
        }
    }

    private static VisionDbContext CreateContext(string root)
    {
        var options = new DbContextOptionsBuilder<VisionDbContext>()
            .UseSqlite($"Data Source={Path.Combine(root, "vision.db")}")
            .Options;
        return new VisionDbContext(options);
    }

    private static InspectionResult CreateResult(Guid projectId, InspectionStatus status, long processingTimeMs)
    {
        var result = new InspectionResult(projectId);
        result.SetResult(status, processingTimeMs);
        return result;
    }

    private static string CreateTempPath()
    {
        var path = Path.Combine(Path.GetTempPath(), "ClearVision.Repository.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteDirectoryIfExists(string path)
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}
