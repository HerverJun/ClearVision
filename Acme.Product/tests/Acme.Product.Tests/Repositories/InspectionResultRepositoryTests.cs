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
