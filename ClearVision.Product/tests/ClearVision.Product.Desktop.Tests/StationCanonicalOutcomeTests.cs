using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Outcomes;
using ClearVision.Product.Desktop.Station;
using ClearVision.Product.Infrastructure.Data;
using ClearVision.Product.Runtime.Abstractions;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ClearVision.Product.Desktop.Tests;

public sealed class StationCanonicalOutcomeTests
{
    [Fact]
    public async Task CentralStoreAndMemoryFallback_ShouldUseIdenticalCanonicalStatisticsAndFilters()
    {
        var root = Path.Combine(Path.GetTempPath(), "ClearVisionStationCanonicalOutcomeTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var dbPath = Path.Combine(root, "vision.db");
        var provider = new ServiceCollection()
            .AddDbContext<VisionDbContext>(options => options.UseSqlite($"Data Source={dbPath}"))
            .BuildServiceProvider();

        try
        {
            await using (var scope = provider.CreateAsyncScope())
            {
                await scope.ServiceProvider.GetRequiredService<VisionDbContext>().Database.EnsureCreatedAsync();
            }

            var central = new StationCentralStore(
                provider.GetRequiredService<IServiceScopeFactory>(),
                NullLogger<StationCentralStore>.Instance);
            var centralRegistry = CreateRegistry(central);
            var memoryRegistry = CreateRegistry();
            var now = DateTimeOffset.UtcNow;
            var results = new[]
            {
                BuildResult(1, ExecutionOutcome.Succeeded, DecisionOutcome.Ok, now.AddSeconds(-9)),
                BuildResult(2, ExecutionOutcome.Succeeded, DecisionOutcome.Ng, now.AddSeconds(-8)),
                BuildResult(3, ExecutionOutcome.Succeeded, DecisionOutcome.Undetermined, now.AddSeconds(-7)),
                BuildResult(4, ExecutionOutcome.Succeeded, DecisionOutcome.NotApplicable, now.AddSeconds(-6)),
                BuildResult(5, ExecutionOutcome.Succeeded, DecisionOutcome.Invalid, now.AddSeconds(-5)),
                BuildResult(6, ExecutionOutcome.Failed, DecisionOutcome.Undetermined, now.AddSeconds(-4)),
                BuildResult(7, ExecutionOutcome.Cancelled, DecisionOutcome.NotApplicable, now.AddSeconds(-3)),
                BuildResult(8, ExecutionOutcome.TimedOut, DecisionOutcome.Undetermined, now.AddSeconds(-2)),
                BuildResult(9, ExecutionOutcome.Skipped, DecisionOutcome.NotApplicable, now.AddSeconds(-1))
            };

            foreach (var result in results)
            {
                centralRegistry.UpsertResultSummary("central", result);
                memoryRegistry.UpsertResultSummary("memory", result);
            }

            var centralStatistics = centralRegistry.GetStatistics(now.AddMinutes(-1), now.AddMinutes(1), "station-canonical", null, null);
            var memoryStatistics = memoryRegistry.GetStatistics(now.AddMinutes(-1), now.AddMinutes(1), "station-canonical", null, null);

            centralStatistics.OutcomeStatistics.Should().BeEquivalentTo(memoryStatistics.OutcomeStatistics);
            centralStatistics.TotalAttemptCount.Should().Be(9);
            centralStatistics.ExecutionSucceededCount.Should().Be(5);
            centralStatistics.ValidDecisionCount.Should().Be(2);
            centralStatistics.OkCount.Should().Be(1);
            centralStatistics.NgCount.Should().Be(1);
            centralStatistics.UndeterminedCount.Should().Be(1);
            centralStatistics.NotApplicableCount.Should().Be(1);
            centralStatistics.InvalidCount.Should().Be(1);
            centralStatistics.FailedCount.Should().Be(1);
            centralStatistics.CancelledCount.Should().Be(1);
            centralStatistics.TimedOutCount.Should().Be(1);
            centralStatistics.SkippedCount.Should().Be(1);
            centralStatistics.ExecutionFailureCount.Should().Be(2);
            centralStatistics.YieldRate.Should().Be(0.5d);
            centralStatistics.DecisionCoverageRate.Should().Be(0.4d);

            var centralInvalidPage = centralRegistry.GetResultsPage("station-canonical", now.AddMinutes(-1), now.AddMinutes(1), "invalid", null, 0, 20);
            var memoryInvalidPage = memoryRegistry.GetResultsPage("station-canonical", now.AddMinutes(-1), now.AddMinutes(1), "invalid", null, 0, 20);
            centralInvalidPage.TotalCount.Should().Be(1);
            memoryInvalidPage.TotalCount.Should().Be(1);
            centralInvalidPage.Items.Single().DecisionOutcome.Should().Be(DecisionOutcome.Invalid);
            memoryInvalidPage.Items.Single().DecisionOutcome.Should().Be(DecisionOutcome.Invalid);
        }
        finally
        {
            await provider.DisposeAsync();
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    [Fact]
    public async Task LegacyDatabaseRow_ShouldRemainNullAndUseControlledProjectionWhenRead()
    {
        var root = Path.Combine(Path.GetTempPath(), "ClearVisionStationLegacyOutcomeTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var dbPath = Path.Combine(root, "vision.db");
        var provider = new ServiceCollection()
            .AddDbContext<VisionDbContext>(options => options.UseSqlite($"Data Source={dbPath}"))
            .BuildServiceProvider();

        try
        {
            await using (var scope = provider.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<VisionDbContext>();
                await db.Database.EnsureCreatedAsync();
                db.StationResultSummaries.Add(new StationResultSummaryEntity
                {
                    StationId = "station-legacy",
                    SequenceId = 1,
                    MessageId = "legacy-1",
                    RunId = "legacy-run",
                    PackageId = "legacy-pkg",
                    PackageName = "Legacy package",
                    PackageVersion = "1.0",
                    FlowHash = "legacy-flow",
                    ImageId = "legacy-image",
                    Outcome = RuntimeRunOutcome.Error.ToString(),
                    InspectionStatus = InspectionStatus.Error.ToString(),
                    ExecutionTimeMs = 8,
                    DiagnosticCode = "LegacyError",
                    PrimaryOutputsPreviewJson = "{}",
                    StartedAtUtc = DateTimeOffset.UtcNow.AddMilliseconds(-8),
                    CompletedAtUtc = DateTimeOffset.UtcNow,
                    CreatedAtUtc = DateTimeOffset.UtcNow,
                    ReceivedAtUtc = DateTimeOffset.UtcNow
                });
                await db.SaveChangesAsync();
            }

            var store = new StationCentralStore(
                provider.GetRequiredService<IServiceScopeFactory>(),
                NullLogger<StationCentralStore>.Instance);
            var page = store.GetResultsPage("station-legacy", null, null, "failed", null, 0, 10);
            var statistics = store.GetStatistics(null, null, "station-legacy", null, null);

            page.TotalCount.Should().Be(1);
            page.Items.Single().ExecutionOutcome.Should().BeNull();
            page.Items.Single().DecisionOutcome.Should().BeNull();
            statistics.FailedCount.Should().Be(1);
            statistics.InvalidCount.Should().Be(0);
            statistics.ExecutionFailureCount.Should().Be(1);
        }
        finally
        {
            await provider.DisposeAsync();
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    private static StationRegistryService CreateRegistry(StationCentralStore? centralStore = null)
    {
        return new StationRegistryService(
            Options.Create(new StationIngressOptions
            {
                Enabled = true,
                ResultBufferPerStation = 50,
                EventBufferSize = 50
            }),
            NullLogger<StationRegistryService>.Instance,
            centralStore);
    }

    private static StationResultSummaryDto BuildResult(
        long sequenceId,
        ExecutionOutcome executionOutcome,
        DecisionOutcome decisionOutcome,
        DateTimeOffset completedAtUtc)
    {
        var canonical = new InspectionOutcome(
            executionOutcome,
            decisionOutcome,
            "FinalDecisionBinding:judge:Judgment",
            $"Reason-{sequenceId}",
            null,
            executionOutcome == ExecutionOutcome.Succeeded && decisionOutcome is DecisionOutcome.Ok or DecisionOutcome.Ng);
        return new StationResultSummaryDto
        {
            StationId = "station-canonical",
            SequenceId = sequenceId,
            MessageId = $"message-{sequenceId}",
            RunId = $"run-{sequenceId}",
            PackageId = "package",
            PackageName = "Package",
            PackageVersion = "1.0",
            FlowHash = "flow",
            ImageId = $"image-{sequenceId}",
            Outcome = StationCanonicalOutcomeProjection.ProjectRuntimeOutcome(canonical),
            InspectionStatus = LegacyInspectionStatusProjection.Project(canonical),
            ExecutionOutcome = executionOutcome,
            DecisionOutcome = decisionOutcome,
            HasJudgmentSignal = canonical.HasJudgmentSignal,
            DecisionSource = canonical.DecisionSource,
            ReasonCode = canonical.ReasonCode,
            ExecutionTimeMs = 10,
            DiagnosticCode = $"D{sequenceId}",
            StartedAtUtc = completedAtUtc.AddMilliseconds(-10),
            CompletedAtUtc = completedAtUtc,
            CreatedAtUtc = completedAtUtc
        };
    }
}
