using System.Linq.Expressions;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Interfaces;
using ClearVision.Product.Core.Outcomes;
using ClearVision.Product.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace ClearVision.Product.Tests.Services;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class InspectionResultBackgroundServiceConcurrencyCollection
{
    public const string Name = "Inspection result background service concurrency";
}

[Collection(InspectionResultBackgroundServiceConcurrencyCollection.Name)]
public sealed class InspectionResultBackgroundServiceTests
{
    [Fact]
    public async Task StopAsync_ShouldSpoolResultsWhenRepositoryKeepsFailing()
    {
        var root = CreateTempPath();
        try
        {
            var repository = new CapturingInspectionResultRepository { FailAdds = true };
            await using var provider = CreateProvider(repository);
            var service = CreateService(provider, root);

            await service.StartAsync(CancellationToken.None);
            await service.WriteAsync(CreateResult(), CancellationToken.None);
            await WaitUntilAsync(() => TryGetSpoolLineCount(root, out var lineCount) && lineCount == 1);
            await service.StopAsync(CancellationToken.None);

            var spoolFile = Path.Combine(root, "inspection-results.jsonl");
            File.Exists(spoolFile).Should().BeTrue();
            File.ReadAllLines(spoolFile).Should().ContainSingle();
        }
        finally
        {
            DeleteDirectoryIfExists(root);
        }
    }

    [Fact]
    public async Task StartAsync_ShouldReplaySpooledResultsAndClearSpool()
    {
        var root = CreateTempPath();
        try
        {
            var failingRepository = new CapturingInspectionResultRepository { FailAdds = true };
            var originalResult = CreateResult(includeDefect: true);
            await using (var provider = CreateProvider(failingRepository))
            {
                var failingService = CreateService(provider, root);
                await failingService.StartAsync(CancellationToken.None);
                await failingService.WriteAsync(originalResult, CancellationToken.None);
                await WaitUntilAsync(() => TryGetSpoolLineCount(root, out var lineCount) && lineCount == 1);
                await failingService.StopAsync(CancellationToken.None);
            }

            var replayRepository = new CapturingInspectionResultRepository();
            await using (var provider = CreateProvider(replayRepository))
            {
                var replayService = CreateService(provider, root);
                await replayService.StartAsync(CancellationToken.None);
                await WaitUntilAsync(() => replayRepository.Added.Count == 1);
                await WaitUntilAsync(() => TryGetSpoolLineCount(root, out var lineCount) && lineCount == 0);
                await replayService.StopAsync(CancellationToken.None);
            }

            var replayedResult = replayRepository.Added.Single();
            replayedResult.Id.Should().Be(originalResult.Id);
            replayedResult.InspectionTime.Should().Be(originalResult.InspectionTime);
            replayedResult.Defects.Should().ContainSingle();
            replayedResult.Defects.Single().InspectionResultId.Should().Be(originalResult.Id);
            File.ReadAllLines(Path.Combine(root, "inspection-results.jsonl")).Should().BeEmpty();
        }
        finally
        {
            DeleteDirectoryIfExists(root);
        }
    }

    [Fact]
    public async Task StartAsync_ShouldRoundTripCanonicalOutcomeAndExecutionIdentityAcrossRestart()
    {
        var root = CreateTempPath();
        try
        {
            var projectId = Guid.NewGuid();
            var imageId = Guid.NewGuid();
            var sessionId = Guid.NewGuid();
            var executionSnapshotId = Guid.NewGuid();
            var originalResult = new InspectionResult(projectId, imageId);
            originalResult.SetOutcome(
                new InspectionOutcome(
                    ExecutionOutcome.Succeeded,
                    DecisionOutcome.Invalid,
                    "FinalDecision",
                    "DecisionValueInvalid",
                    "Decision value is invalid.",
                    false),
                27,
                0.81);
            originalResult.SetTraceability("FLOW-V2", "BUNDLE-V2", sessionId);
            originalResult.RestoreExecutionTraceability(
                executionSnapshotId,
                42,
                "DECISION-V2",
                "PACKAGE-V2",
                "RuntimePackage",
                "StationRuntime",
                "Primary");
            originalResult.SetOutputDataJson("""{"score":0.81}""");
            originalResult.SetAnalysisDataJson("""{"cards":[]}""");
            originalResult.AddDefect(new Defect(
                originalResult.Id,
                DefectType.Scratch,
                1,
                2,
                3,
                4,
                0.91,
                "canonical defect",
                """{"shape":"rect"}"""));

            await using (var provider = CreateProvider(new CapturingInspectionResultRepository { FailAdds = true }))
            {
                var service = CreateService(provider, root);
                await service.StartAsync(CancellationToken.None);
                await service.WriteAsync(originalResult, CancellationToken.None);
                await WaitUntilAsync(() => TryGetSpoolLineCount(root, out var lineCount) && lineCount == 1);
                await service.StopAsync(CancellationToken.None);
            }

            var spoolLine = File.ReadLines(Path.Combine(root, "inspection-results.jsonl")).Single();
            using (var spoolJson = System.Text.Json.JsonDocument.Parse(spoolLine))
            {
                spoolJson.RootElement.GetProperty("schemaVersion").GetInt32().Should().Be(2);
                spoolJson.RootElement.GetProperty("sessionId").GetGuid().Should().Be(sessionId);
                spoolJson.RootElement.TryGetProperty("runId", out _).Should().BeFalse();
                spoolJson.RootElement.GetProperty("executionSnapshotId").GetGuid().Should().Be(executionSnapshotId);
            }

            var replayRepository = new CapturingInspectionResultRepository();
            await using (var provider = CreateProvider(replayRepository))
            {
                var service = CreateService(provider, root);
                await service.StartAsync(CancellationToken.None);
                await WaitUntilAsync(() => replayRepository.Added.Count == 1);
                await service.StopAsync(CancellationToken.None);
            }

            var replayed = replayRepository.Added.Should().ContainSingle().Subject;
            replayed.Id.Should().Be(originalResult.Id);
            replayed.ProjectId.Should().Be(projectId);
            replayed.ImageId.Should().Be(imageId);
            replayed.ExecutionOutcome.Should().Be(ExecutionOutcome.Succeeded);
            replayed.DecisionOutcome.Should().Be(DecisionOutcome.Invalid);
            replayed.HasJudgmentSignal.Should().BeFalse();
            replayed.DecisionSource.Should().Be("FinalDecision");
            replayed.ReasonCode.Should().Be("DecisionValueInvalid");
            replayed.ErrorMessage.Should().Be("Decision value is invalid.");
            replayed.FlowVersionHash.Should().Be("FLOW-V2");
            replayed.CalibrationBundleId.Should().Be("BUNDLE-V2");
            replayed.SessionId.Should().Be(sessionId);
            replayed.ExecutionSnapshotId.Should().Be(executionSnapshotId);
            replayed.ProjectPersistenceRevision.Should().Be(42);
            replayed.DecisionConfigurationHash.Should().Be("DECISION-V2");
            replayed.RuntimePackageId.Should().Be("PACKAGE-V2");
            replayed.ExecutionSource.Should().Be("RuntimePackage");
            replayed.ExecutionRunMode.Should().Be("StationRuntime");
            replayed.ShadowRole.Should().Be("Primary");
            replayed.OutputDataJson.Should().Be(originalResult.OutputDataJson);
            replayed.AnalysisDataJson.Should().Be(originalResult.AnalysisDataJson);
            replayed.Defects.Should().ContainSingle().Which.AnnotationData.Should().Contain("rect");
            (await replayRepository.FindByExecutionSnapshotIdAsync(projectId, executionSnapshotId))
                .Should().BeSameAs(replayed);
        }
        finally
        {
            DeleteDirectoryIfExists(root);
        }
    }

    [Fact]
    public async Task StartAsync_ShouldReadLegacySpoolWithoutInventingCanonicalIdentity()
    {
        var root = CreateTempPath();
        try
        {
            var resultId = Guid.NewGuid();
            var projectId = Guid.NewGuid();
            var spoolFile = Path.Combine(root, "inspection-results.jsonl");
            await File.WriteAllTextAsync(
                spoolFile,
                $$"""
                {"id":"{{resultId:D}}","projectId":"{{projectId:D}}","status":"NG","processingTimeMs":19,"inspectionTime":"2026-01-02T03:04:05Z","createdAt":"2026-01-02T03:04:00Z","flowVersionHash":"LEGACY-FLOW","defects":[]}
                """);

            var repository = new CapturingInspectionResultRepository();
            await using var provider = CreateProvider(repository);
            var service = CreateService(provider, root);
            await service.StartAsync(CancellationToken.None);
            await WaitUntilAsync(() => repository.Added.Count == 1);
            await service.StopAsync(CancellationToken.None);

            var replayed = repository.Added.Should().ContainSingle().Subject;
            replayed.Id.Should().Be(resultId);
            replayed.Status.Should().Be(InspectionStatus.NG);
            replayed.ExecutionOutcome.Should().BeNull();
            replayed.DecisionOutcome.Should().BeNull();
            replayed.HasJudgmentSignal.Should().BeNull();
            replayed.ExecutionSnapshotId.Should().BeNull();
            replayed.ProjectPersistenceRevision.Should().BeNull();
            replayed.DecisionConfigurationHash.Should().BeNull();
            replayed.RuntimePackageId.Should().BeNull();
            replayed.ExecutionSource.Should().BeNull();
            replayed.ExecutionRunMode.Should().BeNull();
            replayed.ShadowRole.Should().BeNull();
            replayed.GetOutcome().Decision.Should().Be(DecisionOutcome.Ng);
        }
        finally
        {
            DeleteDirectoryIfExists(root);
        }
    }

    [Fact]
    public async Task StartAsync_WhenSpoolReplayStillFails_ShouldKeepSpooledResults()
    {
        var root = CreateTempPath();
        try
        {
            var failingRepository = new CapturingInspectionResultRepository { FailAdds = true };
            await using (var provider = CreateProvider(failingRepository))
            {
                var failingService = CreateService(provider, root);
                await failingService.StartAsync(CancellationToken.None);
                await failingService.WriteAsync(CreateResult(), CancellationToken.None);
                await failingService.WriteAsync(CreateResult(), CancellationToken.None);
                await failingService.WriteAsync(CreateResult(), CancellationToken.None);
                await WaitUntilAsync(() => TryGetSpoolLineCount(root, out var lineCount) && lineCount == 3);
                await failingService.StopAsync(CancellationToken.None);
            }

            var replayRepository = new CapturingInspectionResultRepository { FailAdds = true };
            await using (var provider = CreateProvider(replayRepository))
            {
                var replayService = CreateService(provider, root);
                await replayService.StartAsync(CancellationToken.None);
                await WaitUntilAsync(() => replayRepository.AddRangeCallCount >= 3);
                await WaitUntilAsync(() => TryGetSpoolLineCount(root, out var lineCount) && lineCount == 3);
                await WaitUntilAsync(() => !File.Exists(Path.Combine(root, "inspection-results.jsonl.tmp")));
                await replayService.StopAsync(CancellationToken.None);
            }

            File.Exists(Path.Combine(root, "inspection-results.jsonl.tmp")).Should().BeFalse();
        }
        finally
        {
            DeleteDirectoryIfExists(root);
        }
    }

    [Fact]
    public void TryWrite_WhenQueuedImageBudgetIsFull_ShouldRejectAdditionalImageResults()
    {
        var root = CreateTempPath();
        try
        {
            var repository = new CapturingInspectionResultRepository();
            using var provider = CreateProvider(repository);
            var service = CreateService(
                provider,
                root,
                new Dictionary<string, string?>
                {
                    ["Performance:Persistence:QueueCapacity"] = "10",
                    ["Performance:Persistence:MaxQueuedImageBytes"] = "256"
                });

            service.TryWrite(CreateResult(outputImageBytes: 128)).Should().BeTrue();
            service.TryWrite(CreateResult(outputImageBytes: 128)).Should().BeTrue();
            service.TryWrite(CreateResult(outputImageBytes: 128)).Should().BeFalse();
        }
        finally
        {
            DeleteDirectoryIfExists(root);
        }
    }

    [Fact]
    public async Task ExecuteAsync_WhenRepositoryAndSpoolFail_ShouldDeadLetterAndReleaseImageBudget()
    {
        var root = CreateTempPath();
        try
        {
            var repository = new CapturingInspectionResultRepository { FailAdds = true };
            await using var provider = CreateProvider(repository);
            var service = CreateService(
                provider,
                root,
                new Dictionary<string, string?>
                {
                    ["Performance:Persistence:MaxQueuedImageBytes"] = "128"
                });
            Directory.CreateDirectory(Path.Combine(root, "inspection-results.jsonl"));

            await service.StartAsync(CancellationToken.None);
            await service.WriteAsync(CreateResult(outputImageBytes: 128), CancellationToken.None);

            var acceptedAfterRelease = false;
            await WaitUntilAsync(
                () =>
                {
                    if (!TryGetDeadLetterLineCount(root, out var lineCount) || lineCount != 1)
                    {
                        return false;
                    }

                    acceptedAfterRelease = service.TryWrite(CreateResult(outputImageBytes: 128));
                    return acceptedAfterRelease;
                },
                TimeSpan.FromSeconds(5));
            acceptedAfterRelease.Should().BeTrue("a failed repository+spool batch must release queued image budget");
            await service.StopAsync(CancellationToken.None);
        }
        finally
        {
            DeleteDirectoryIfExists(root);
        }
    }

    private static InspectionResultBackgroundService CreateService(
        IServiceProvider provider,
        string spoolRoot,
        Dictionary<string, string?>? overrides = null)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Performance:Persistence:SpoolDirectory"] = spoolRoot,
            ["Performance:Persistence:BatchSize"] = "1",
            ["Performance:Persistence:MaxSaveRetries"] = "1",
            ["Performance:Persistence:QueueCapacity"] = "4"
        };

        if (overrides != null)
        {
            foreach (var (key, value) in overrides)
            {
                settings[key] = value;
            }
        }

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();

        return new InspectionResultBackgroundService(
            NullLogger<InspectionResultBackgroundService>.Instance,
            provider,
            configuration);
    }

    private static ServiceProvider CreateProvider(IInspectionResultRepository repository)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => repository);
        return services.BuildServiceProvider();
    }

    private static InspectionResult CreateResult(bool includeDefect = false, int outputImageBytes = 0)
    {
        var result = new InspectionResult(Guid.NewGuid(), null);
        result.SetResult(InspectionStatus.OK, 12, 0.95, null);
        result.RestorePersistenceMetadata(
            result.Id,
            new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc),
            new DateTime(2026, 1, 2, 3, 4, 0, DateTimeKind.Utc),
            result.ModifiedAt);

        if (outputImageBytes > 0)
        {
            result.SetOutputImage(Enumerable.Repeat((byte)7, outputImageBytes).ToArray());
        }

        if (includeDefect)
        {
            result.AddDefect(new Defect(
                result.Id,
                DefectType.Scratch,
                x: 1,
                y: 2,
                width: 3,
                height: 4,
                confidenceScore: 0.9,
                description: "spooled defect"));
        }

        return result;
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan? timeout = null)
    {
        using var cts = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(30));
        while (!condition())
        {
            cts.Token.ThrowIfCancellationRequested();
            await Task.Delay(25, cts.Token);
        }
    }

    private static bool TryGetSpoolLineCount(string root, out int lineCount)
    {
        return TryGetLineCount(Path.Combine(root, "inspection-results.jsonl"), out lineCount);
    }

    private static bool TryGetDeadLetterLineCount(string root, out int lineCount)
    {
        return TryGetLineCount(Path.Combine(root, "inspection-results.deadletter.jsonl"), out lineCount);
    }

    private static bool TryGetLineCount(string path, out int lineCount)
    {
        lineCount = 0;
        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            while (reader.ReadLine() is not null)
            {
                lineCount += 1;
            }

            return true;
        }
        catch (IOException)
        {
            return false;
        }
    }

    private static string CreateTempPath()
    {
        var path = Path.Combine(Path.GetTempPath(), "ClearVision.ResultSpool.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteDirectoryIfExists(string path)
    {
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, recursive: true);
                }

                return;
            }
            catch (IOException) when (attempt < 5)
            {
                Thread.Sleep(50 * attempt);
            }
        }
    }

    private sealed class CapturingInspectionResultRepository : IInspectionResultRepository
    {
        public bool FailAdds { get; set; }

        public int AddRangeCallCount { get; private set; }

        public List<InspectionResult> Added { get; } = [];

        public Task AddRangeAsync(IEnumerable<InspectionResult> results)
        {
            AddRangeCallCount++;

            if (FailAdds)
            {
                throw new IOException("Simulated repository failure.");
            }

            Added.AddRange(results);
            return Task.CompletedTask;
        }

        public Task<InspectionResult> AddAsync(InspectionResult entity)
        {
            Added.Add(entity);
            return Task.FromResult(entity);
        }

        public Task DeleteAsync(InspectionResult entity) => Task.CompletedTask;

        public Task DeleteByIdAsync(Guid id) => Task.CompletedTask;

        public Task<bool> ExistsAsync(Guid id) => Task.FromResult(false);

        public Task<IEnumerable<InspectionResult>> FindAsync(Expression<Func<InspectionResult, bool>> predicate)
        {
            return Task.FromResult(Enumerable.Empty<InspectionResult>());
        }

        public Task<IEnumerable<InspectionResult>> GetAllAsync()
        {
            return Task.FromResult<IEnumerable<InspectionResult>>(Added);
        }

        public Task<InspectionResult?> GetByIdAsync(Guid id)
        {
            return Task.FromResult<InspectionResult?>(Added.FirstOrDefault(result => result.Id == id));
        }

        public Task<IEnumerable<InspectionResult>> GetByProjectIdAsync(Guid projectId, int pageIndex = 0, int pageSize = 20)
        {
            return Task.FromResult<IEnumerable<InspectionResult>>(Added.Where(result => result.ProjectId == projectId));
        }

        public Task<InspectionResult?> FindByExecutionSnapshotIdAsync(Guid projectId, Guid executionSnapshotId)
        {
            return Task.FromResult<InspectionResult?>(Added.FirstOrDefault(result =>
                result.ProjectId == projectId && result.ExecutionSnapshotId == executionSnapshotId));
        }

        public Task<InspectionHistoryPage> GetHistoryPageAsync(Guid projectId, DateTime? startTime = null, DateTime? endTime = null, string? status = null, string? defectType = null, int pageIndex = 0, int pageSize = 20, string? flowVersionHash = null)
        {
            return Task.FromResult(new InspectionHistoryPage());
        }

        public Task<InspectionHistoryDetail?> GetHistoryDetailAsync(Guid projectId, Guid resultId)
        {
            return Task.FromResult<InspectionHistoryDetail?>(null);
        }

        public Task<InspectionHistoryDetail?> FindPreviousSuccessfulInspectionAsync(
            Guid projectId,
            DateTime beforeTime,
            string? flowVersionHash = null,
            int limit = 50)
        {
            return Task.FromResult<InspectionHistoryDetail?>(null);
        }

        public Task<IEnumerable<InspectionResult>> GetByTimeRangeAsync(Guid projectId, DateTime startTime, DateTime endTime, string? status = null, string? defectType = null)
        {
            return Task.FromResult<IEnumerable<InspectionResult>>(Added.Where(result => result.ProjectId == projectId));
        }

        public Task<InspectionStatistics> GetStatisticsAsync(Guid projectId, DateTime? startTime = null, DateTime? endTime = null, string? status = null, string? defectType = null)
        {
            return Task.FromResult(new InspectionStatistics());
        }

        public Task<Dictionary<DefectType, int>> GetDefectDistributionAsync(Guid projectId, DateTime? startTime = null, DateTime? endTime = null, string? status = null, string? defectType = null)
        {
            return Task.FromResult(new Dictionary<DefectType, int>());
        }

        public Task UpdateAsync(InspectionResult entity) => Task.CompletedTask;
    }
}
