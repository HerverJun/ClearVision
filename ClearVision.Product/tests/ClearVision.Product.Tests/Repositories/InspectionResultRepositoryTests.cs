using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Interfaces;
using ClearVision.Product.Core.Outcomes;
using ClearVision.Product.Infrastructure.Data;
using ClearVision.Product.Infrastructure.Repositories;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ClearVision.Product.Tests.Repositories;

[TestClassification(TestDomain.Data, TestPurpose.Regression, TestLane.Pr, TestEvidenceType.Contract, TestOracleType.Contract, TestResourceRequirement.None, TestExpectedDuration.Fast, TestFlakyPolicy.Blocking, "data-platform")]
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
            stats.OKRate.Should().Be(0.5);
            stats.YieldRate.Should().Be(0.5);
            stats.ValidDecisionCount.Should().Be(2);
            stats.ExecutionFailureCount.Should().Be(1);
            stats.DecisionCoverageRate.Should().Be(1);
        }
        finally
        {
            DeleteDirectoryIfExists(root);
        }
    }

    [Fact]
    public async Task GetStatisticsAsync_ShouldExcludeNonDecisionsAndProjectLegacyRows()
    {
        var root = CreateTempPath();
        try
        {
            await using var db = CreateContext(root);
            await db.Database.EnsureCreatedAsync();
            var repository = new InspectionResultRepository(db);
            var projectId = Guid.NewGuid();
            var ok = CreateOutcomeResult(projectId, ExecutionOutcome.Succeeded, DecisionOutcome.Ok);
            var ng = CreateOutcomeResult(projectId, ExecutionOutcome.Succeeded, DecisionOutcome.Ng);
            var undetermined = CreateOutcomeResult(projectId, ExecutionOutcome.Succeeded, DecisionOutcome.Undetermined);
            var invalid = CreateOutcomeResult(projectId, ExecutionOutcome.Succeeded, DecisionOutcome.Invalid);
            var failed = CreateOutcomeResult(projectId, ExecutionOutcome.Failed, DecisionOutcome.Undetermined);
            var timedOut = CreateOutcomeResult(projectId, ExecutionOutcome.TimedOut, DecisionOutcome.Undetermined);
            var legacyNg = CreateResult(projectId, InspectionStatus.NG, 10);

            await repository.AddRangeAsync([ok, ng, undetermined, invalid, failed, timedOut, legacyNg]);
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE InspectionResults SET ExecutionOutcome = NULL, DecisionOutcome = NULL, HasJudgmentSignal = NULL WHERE Id = {legacyNg.Id}");
            db.ChangeTracker.Clear();

            var stats = await repository.GetStatisticsAsync(projectId);

            stats.TotalCount.Should().Be(7);
            stats.OKCount.Should().Be(1);
            stats.NGCount.Should().Be(2);
            stats.ValidDecisionCount.Should().Be(3);
            stats.YieldRate.Should().BeApproximately(1.0 / 3.0, 0.0001);
            stats.UndeterminedCount.Should().Be(1);
            stats.InvalidCount.Should().Be(1);
            stats.FailedCount.Should().Be(1);
            stats.TimedOutCount.Should().Be(1);
            stats.ExecutionFailureCount.Should().Be(2);
            stats.DecisionCoverageRate.Should().Be(0.6);
        }
        finally
        {
            DeleteDirectoryIfExists(root);
        }
    }

    [Fact]
    public async Task GetAnalysisSamplesAsync_ShouldProjectOnlyBoundedRowsPlusOne()
    {
        var root = CreateTempPath();
        try
        {
            await using var db = CreateContext(root);
            await db.Database.EnsureCreatedAsync();
            var repository = new InspectionResultRepository(db);
            var projectId = Guid.NewGuid();
            var start = new DateTime(2026, 8, 1, 8, 0, 0, DateTimeKind.Utc);
            var results = Enumerable.Range(0, 5)
                .Select(index =>
                {
                    var result = CreateResult(projectId, InspectionStatus.NG, 20 + index);
                    result.SetOutputImage(Enumerable.Repeat((byte)(index + 1), 4096).ToArray());
                    result.SetOutputDataJson($"{{\"payload\":\"{new string('x', 2048)}\"}}");
                    result.RestorePersistenceMetadata(
                        Guid.NewGuid(),
                        start.AddMinutes(index),
                        start.AddMinutes(index),
                        null);
                    return result;
                })
                .ToList();
            await repository.AddRangeAsync(results);
            db.ChangeTracker.Clear();

            var samples = await repository.GetAnalysisSamplesAsync(
                new InspectionAnalysisQuery(projectId, start, start.AddHours(1), null, null),
                maxRows: 2);

            samples.Should().HaveCount(3, "the repository must return only the budget sentinel row");
            samples.Select(sample => sample.InspectionTime)
                .Should().BeInAscendingOrder();
            samples.Should().OnlyContain(sample => sample.DefectCount == 0);
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
            item.HasImage.Should().BeTrue();
            item.HasOutputData.Should().BeTrue();
            item.HasAnalysisData.Should().BeTrue();
            item.OutputDataJson.Should().BeNull();
            item.AnalysisDataJson.Should().BeNull();
            item.Defects.Should().ContainSingle();
            item.GetType().GetProperty("OutputImage").Should().BeNull();
        }
        finally
        {
            DeleteDirectoryIfExists(root);
        }
    }

    [Fact]
    public async Task GetHistoryPageAsync_ShouldUseStableOrderClampAndFilters()
    {
        var root = CreateTempPath();
        try
        {
            await using var db = CreateContext(root);
            await db.Database.EnsureCreatedAsync();
            var repository = new InspectionResultRepository(db);
            var projectId = Guid.NewGuid();
            var otherProjectId = Guid.NewGuid();
            var timestamp = new DateTime(2026, 7, 4, 8, 0, 0, DateTimeKind.Utc);
            var older = CreateResult(projectId, InspectionStatus.OK, 10);
            older.RestorePersistenceMetadata(
                Guid.Parse("00000000-0000-0000-0000-000000000009"),
                timestamp.AddMinutes(-10),
                timestamp.AddMinutes(-10),
                null);
            older.SetTraceability("FLOW-A", null, null);
            var first = CreateResult(projectId, InspectionStatus.NG, 20);
            first.RestorePersistenceMetadata(
                Guid.Parse("00000000-0000-0000-0000-000000000001"),
                timestamp,
                timestamp,
                null);
            first.SetTraceability("FLOW-A", null, null);
            var second = CreateResult(projectId, InspectionStatus.NG, 30);
            second.RestorePersistenceMetadata(
                Guid.Parse("00000000-0000-0000-0000-000000000002"),
                timestamp,
                timestamp,
                null);
            second.SetTraceability("FLOW-A", null, null);
            var otherFlow = CreateResult(projectId, InspectionStatus.NG, 40);
            otherFlow.RestorePersistenceMetadata(
                Guid.Parse("00000000-0000-0000-0000-000000000003"),
                timestamp,
                timestamp,
                null);
            otherFlow.SetTraceability("FLOW-B", null, null);
            var otherProject = CreateResult(otherProjectId, InspectionStatus.NG, 50);
            otherProject.RestorePersistenceMetadata(
                Guid.Parse("00000000-0000-0000-0000-000000000004"),
                timestamp,
                timestamp,
                null);
            otherProject.SetTraceability("FLOW-A", null, null);

            await repository.AddRangeAsync([older, first, second, otherFlow, otherProject]);
            db.ChangeTracker.Clear();

            var page = await repository.GetHistoryPageAsync(
                projectId,
                startTime: timestamp.AddMinutes(-1),
                endTime: timestamp.AddMinutes(1),
                status: "NG",
                pageIndex: 0,
                pageSize: 500,
                flowVersionHash: "FLOW-A");

            page.PageSize.Should().Be(200);
            page.TotalCount.Should().Be(2);
            page.Items.Select(item => item.Id).Should().Equal(second.Id, first.Id);
            page.Items.Should().OnlyContain(item => item.ProjectId == projectId);
            page.Items.Should().OnlyContain(item => item.Status == InspectionStatus.NG);
            page.Items.Should().OnlyContain(item => item.FlowVersionHash == "FLOW-A");
        }
        finally
        {
            DeleteDirectoryIfExists(root);
        }
    }

    [Fact]
    public async Task GetHistoryDetailAsync_ShouldReturnProjectScopedPayloadWithoutOutputImage()
    {
        var root = CreateTempPath();
        try
        {
            await using var db = CreateContext(root);
            await db.Database.EnsureCreatedAsync();
            var repository = new InspectionResultRepository(db);
            var projectId = Guid.NewGuid();
            var otherProjectId = Guid.NewGuid();
            var sessionId = Guid.NewGuid();
            var result = CreateResult(projectId, InspectionStatus.Error, 99);
            result.SetOutputImage([1, 2, 3, 4]);
            result.SetOutputDataJson("""{"score":99}""");
            result.SetAnalysisDataJson("""{"cards":[]}""");
            result.SetTraceability("FLOW-DETAIL", "bundle-detail", sessionId);
            var otherProject = CreateResult(otherProjectId, InspectionStatus.OK, 10);

            await repository.AddRangeAsync([result, otherProject]);
            db.ChangeTracker.Clear();

            var detail = await repository.GetHistoryDetailAsync(projectId, result.Id);
            var notFoundForOtherProject = await repository.GetHistoryDetailAsync(otherProjectId, result.Id);
            var notFoundMissing = await repository.GetHistoryDetailAsync(projectId, Guid.NewGuid());

            detail.Should().NotBeNull();
            detail!.Id.Should().Be(result.Id);
            detail.ProjectId.Should().Be(projectId);
            detail.OutputDataJson.Should().Contain("score");
            detail.AnalysisDataJson.Should().Contain("cards");
            detail.FlowVersionHash.Should().Be("FLOW-DETAIL");
            detail.CalibrationBundleId.Should().Be("bundle-detail");
            detail.SessionId.Should().Be(sessionId);
            detail.HasImage.Should().BeTrue();
            detail.GetType().GetProperty("OutputImage").Should().BeNull();
            notFoundForOtherProject.Should().BeNull();
            notFoundMissing.Should().BeNull();
        }
        finally
        {
            DeleteDirectoryIfExists(root);
        }
    }

    [Fact]
    public async Task FindPreviousSuccessfulInspectionAsync_ShouldReturnLatestProjectScopedOkBeforeTime()
    {
        var root = CreateTempPath();
        try
        {
            await using var db = CreateContext(root);
            await db.Database.EnsureCreatedAsync();
            var repository = new InspectionResultRepository(db);
            var projectId = Guid.NewGuid();
            var otherProjectId = Guid.NewGuid();
            var timestamp = new DateTime(2026, 7, 4, 8, 30, 0, DateTimeKind.Utc);
            var latestSameFlow = CreateResult(projectId, InspectionStatus.OK, 10);
            latestSameFlow.RestorePersistenceMetadata(
                Guid.Parse("00000000-0000-0000-0000-000000000111"),
                timestamp.AddMinutes(-1),
                timestamp.AddMinutes(-1),
                null);
            latestSameFlow.SetTraceability("FLOW-A", "bundle-a", Guid.NewGuid());
            var olderSameFlow = CreateResult(projectId, InspectionStatus.OK, 11);
            olderSameFlow.RestorePersistenceMetadata(
                Guid.Parse("00000000-0000-0000-0000-000000000112"),
                timestamp.AddMinutes(-5),
                timestamp.AddMinutes(-5),
                null);
            olderSameFlow.SetTraceability("FLOW-A", "bundle-a", Guid.NewGuid());
            var otherFlow = CreateResult(projectId, InspectionStatus.OK, 12);
            otherFlow.RestorePersistenceMetadata(
                Guid.Parse("00000000-0000-0000-0000-000000000113"),
                timestamp.AddMinutes(-30),
                timestamp.AddMinutes(-30),
                null);
            otherFlow.SetTraceability("FLOW-B", "bundle-b", Guid.NewGuid());
            var afterFailure = CreateResult(projectId, InspectionStatus.OK, 13);
            afterFailure.RestorePersistenceMetadata(
                Guid.Parse("00000000-0000-0000-0000-000000000114"),
                timestamp.AddMinutes(1),
                timestamp.AddMinutes(1),
                null);
            afterFailure.SetTraceability("FLOW-A", "bundle-a", Guid.NewGuid());
            var notOk = CreateResult(projectId, InspectionStatus.NG, 14);
            notOk.RestorePersistenceMetadata(
                Guid.Parse("00000000-0000-0000-0000-000000000115"),
                timestamp.AddMinutes(-1),
                timestamp.AddMinutes(-1),
                null);
            notOk.SetTraceability("FLOW-A", "bundle-a", Guid.NewGuid());
            var otherProject = CreateResult(otherProjectId, InspectionStatus.OK, 15);
            otherProject.RestorePersistenceMetadata(
                Guid.Parse("00000000-0000-0000-0000-000000000116"),
                timestamp.AddMinutes(-1),
                timestamp.AddMinutes(-1),
                null);
            otherProject.SetTraceability("FLOW-A", "bundle-a", Guid.NewGuid());

            await repository.AddRangeAsync([latestSameFlow, olderSameFlow, otherFlow, afterFailure, notOk, otherProject]);
            db.ChangeTracker.Clear();

            var sameFlow = await repository.FindPreviousSuccessfulInspectionAsync(
                projectId,
                timestamp,
                flowVersionHash: "FLOW-A",
                limit: 5);
            var fallback = await repository.FindPreviousSuccessfulInspectionAsync(
                projectId,
                timestamp,
                flowVersionHash: null,
                limit: 5);
            var notFound = await repository.FindPreviousSuccessfulInspectionAsync(
                otherProjectId,
                timestamp.AddMinutes(-10),
                flowVersionHash: "FLOW-A",
                limit: 5);

            sameFlow.Should().NotBeNull();
            sameFlow!.Id.Should().Be(latestSameFlow.Id);
            sameFlow.ProjectId.Should().Be(projectId);
            sameFlow.Status.Should().Be(InspectionStatus.OK);
            sameFlow.FlowVersionHash.Should().Be("FLOW-A");
            fallback.Should().NotBeNull();
            fallback!.Id.Should().Be(latestSameFlow.Id);
            notFound.Should().BeNull();
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

    private static InspectionResult CreateOutcomeResult(
        Guid projectId,
        ExecutionOutcome execution,
        DecisionOutcome decision)
    {
        var result = new InspectionResult(projectId);
        result.SetOutcome(new InspectionOutcome(execution, decision, "test", "test", null), 10);
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
