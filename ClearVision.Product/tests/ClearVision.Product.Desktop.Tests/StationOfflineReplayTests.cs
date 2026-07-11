using System.Text.Json;
using System.Text.Json.Serialization;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Outcomes;
using ClearVision.Product.Desktop.Station;
using ClearVision.Product.Runtime.Abstractions;
using ClearVision.Product.Station.Sync;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ClearVision.Product.Desktop.Tests;

public sealed class StationOfflineReplayTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters =
        {
            new JsonStringEnumConverter()
        }
    };

    [Fact]
    public void OfflineSpool_ShouldReplayAccumulatedResultsInSequenceAndAcknowledgeWithoutDuplicates()
    {
        var spoolDirectory = CreateTempDirectory();
        try
        {
            var spool = CreateSpool(spoolDirectory, maxBufferedResults: 100);
            spool.Enqueue(BuildResult(0, "run-offline-1"));
            spool.Enqueue(BuildResult(0, "run-offline-2"));
            spool.Enqueue(BuildResult(0, "run-offline-3"));

            spool = CreateSpool(spoolDirectory, maxBufferedResults: 100);
            var replayBatch = spool.GetPendingBatch(10);
            var registry = CreateRegistry();
            registry.UpsertRegistration("conn-1", new StationRegistrationDto
            {
                StationId = "station-offline",
                LineName = "line-a",
                MachineName = "station-pc",
                ClientVersion = "1.0.0",
                StartedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-5)
            });

            foreach (var summary in replayBatch)
            {
                var cursor = registry.UpsertResultSummary("conn-1", summary);
                spool.Acknowledge(cursor.AckedSequenceId);
            }

            foreach (var staleDuplicate in replayBatch)
            {
                registry.UpsertResultSummary("conn-1", staleDuplicate);
            }

            var station = registry.GetStation("station-offline");
            station.Should().NotBeNull();
            station!.LastSequenceId.Should().Be(3);
            station.RecentResults.Select(result => result.SequenceId).Should().Equal(3, 2, 1);
            station.RecentResults.Select(result => result.RunId).Should().Equal("run-offline-3", "run-offline-2", "run-offline-1");
            spool.GetPendingBatch(10).Should().BeEmpty();

            var restartedSpool = CreateSpool(spoolDirectory, maxBufferedResults: 100);
            restartedSpool.AckedSequenceId.Should().Be(3);
            restartedSpool.GetPendingBatch(10).Should().BeEmpty();
        }
        finally
        {
            DeleteTempDirectory(spoolDirectory);
        }
    }

    [Fact]
    public void Acknowledge_ShouldAdvanceStateWithoutRewritingSpoolUntilCompaction()
    {
        var spoolDirectory = CreateTempDirectory();
        try
        {
            var spool = CreateSpool(spoolDirectory, maxBufferedResults: 100);
            spool.Enqueue(BuildResult(0, "run-ack-1"));
            spool.Enqueue(BuildResult(0, "run-ack-2"));
            spool.Enqueue(BuildResult(0, "run-ack-3"));

            var spoolFilePath = Path.Combine(spoolDirectory, "station-results.jsonl");
            File.ReadLines(spoolFilePath).Should().HaveCount(3);

            spool.Acknowledge(3);

            File.ReadLines(spoolFilePath).Should().HaveCount(3);
            var restarted = CreateSpool(spoolDirectory, maxBufferedResults: 100);
            restarted.AckedSequenceId.Should().Be(3);
            restarted.GetPendingBatch(10).Should().BeEmpty();
            File.ReadLines(spoolFilePath).Should().BeEmpty();
        }
        finally
        {
            DeleteTempDirectory(spoolDirectory);
        }
    }

    [Fact]
    public void OfflineSpool_ShouldPreserveCanonicalOutcomeFieldsAcrossRestart()
    {
        var spoolDirectory = CreateTempDirectory();
        try
        {
            var source = BuildResult(0, "run-canonical-replay");
            source.Outcome = RuntimeRunOutcome.Error;
            source.InspectionStatus = InspectionStatus.Error;
            source.ExecutionOutcome = ExecutionOutcome.Succeeded;
            source.DecisionOutcome = DecisionOutcome.Invalid;
            source.HasJudgmentSignal = false;
            source.DecisionSource = "FinalDecisionBinding:judge:Judgment";
            source.ReasonCode = "DecisionValueInvalid";

            var spool = CreateSpool(spoolDirectory, maxBufferedResults: 10);
            spool.Enqueue(source);

            var replayed = CreateSpool(spoolDirectory, maxBufferedResults: 10).GetPendingBatch(10).Single();
            replayed.ExecutionOutcome.Should().Be(ExecutionOutcome.Succeeded);
            replayed.DecisionOutcome.Should().Be(DecisionOutcome.Invalid);
            replayed.HasJudgmentSignal.Should().BeFalse();
            replayed.DecisionSource.Should().Be("FinalDecisionBinding:judge:Judgment");
            replayed.ReasonCode.Should().Be("DecisionValueInvalid");
        }
        finally
        {
            DeleteTempDirectory(spoolDirectory);
        }
    }

    [Fact]
    public void OfflineSpool_ShouldReplayTwentyResultsAfterStationRestart()
    {
        var spoolDirectory = CreateTempDirectory();
        try
        {
            var spool = CreateSpool(spoolDirectory, maxBufferedResults: 100);
            for (var index = 1; index <= 20; index++)
            {
                spool.Enqueue(BuildResult(0, $"run-offline-{index:D2}"));
            }

            var restartedSpool = CreateSpool(spoolDirectory, maxBufferedResults: 100);
            var registry = CreateRegistry();
            registry.UpsertRegistration("conn-20", new StationRegistrationDto
            {
                StationId = "station-offline",
                LineName = "line-a",
                MachineName = "station-pc",
                ClientVersion = "1.0.0",
                StartedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-5)
            });

            foreach (var summary in restartedSpool.GetPendingBatch(50))
            {
                var cursor = registry.UpsertResultSummary("conn-20", summary);
                restartedSpool.Acknowledge(cursor.AckedSequenceId);
            }

            var station = registry.GetStation("station-offline")!;
            station.LastSequenceId.Should().Be(20);
            station.RecentResults.Should().HaveCount(20);
            station.RecentResults.Select(result => result.SequenceId).Should().Equal(Enumerable.Range(1, 20).Reverse().Select(value => (long)value));
            restartedSpool.GetPendingBatch(50).Should().BeEmpty();
            CreateSpool(spoolDirectory, maxBufferedResults: 100).AckedSequenceId.Should().Be(20);
        }
        finally
        {
            DeleteTempDirectory(spoolDirectory);
        }
    }

    [Fact]
    public void Enqueue_ShouldIgnoreStaleOverflowRowsOnRestart_WhenOverflowTrimsPendingResults()
    {
        var spoolDirectory = CreateTempDirectory();
        try
        {
            var spool = CreateSpool(spoolDirectory, maxBufferedResults: 100);
            for (var index = 1; index <= 105; index++)
            {
                spool.Enqueue(BuildResult(0, $"run-{index:D3}"));
            }

            var spoolFilePath = Path.Combine(spoolDirectory, "station-results.jsonl");
            var persisted = File.ReadLines(spoolFilePath)
                .Select(line => JsonSerializer.Deserialize<StationResultSummaryDto>(line, JsonOptions))
                .Where(summary => summary != null)
                .Select(summary => summary!)
                .ToList();

            persisted.Should().HaveCount(105);

            var restartedSpool = CreateSpool(spoolDirectory, maxBufferedResults: 100);
            restartedSpool.GetPendingBatch(200).Select(result => result.SequenceId)
                .Should()
                .Equal(Enumerable.Range(6, 100).Select(value => (long)value));
            File.ReadLines(spoolFilePath).Should().HaveCount(100);
            restartedSpool.GetPendingUnavailableRange().Should().Be((1L, 5L));

            restartedSpool.AcknowledgeUnavailableThrough(5);
            restartedSpool.GetPendingUnavailableRange().Should().Be((0L, 0L));
        }
        finally
        {
            DeleteTempDirectory(spoolDirectory);
        }
    }

    [Fact]
    public void Enqueue_ShouldTrimPendingResults_WhenSpoolExceedsByteLimit()
    {
        var spoolDirectory = CreateTempDirectory();
        try
        {
            var spool = CreateSpool(spoolDirectory, maxBufferedResults: 100, maxSpoolMb: 1);
            for (var index = 1; index <= 5; index++)
            {
                var result = BuildResult(0, $"run-large-{index:D2}");
                result.DiagnosticMessage = new string('x', 300_000);
                spool.Enqueue(result);
            }

            spool.GetPendingBatch(100).Should().HaveCountLessThan(5);
            spool.AckedSequenceId.Should().BeGreaterThan(0);
        }
        finally
        {
            DeleteTempDirectory(spoolDirectory);
        }
    }

    [Fact]
    public void Enqueue_ShouldTrimPendingResults_WhenSpoolExceedsAgeLimit()
    {
        var spoolDirectory = CreateTempDirectory();
        try
        {
            var spool = CreateSpool(spoolDirectory, maxBufferedResults: 100, maxSpoolDays: 1);
            var oldResult = BuildResult(0, "run-old");
            oldResult.CreatedAtUtc = DateTimeOffset.UtcNow.AddDays(-2);
            spool.Enqueue(oldResult);
            spool.Enqueue(BuildResult(0, "run-new"));

            var pending = spool.GetPendingBatch(100);
            pending.Should().ContainSingle();
            pending[0].RunId.Should().Be("run-new");
            spool.AckedSequenceId.Should().Be(1);
        }
        finally
        {
            DeleteTempDirectory(spoolDirectory);
        }
    }

    private static StationSpoolStore CreateSpool(
        string spoolDirectory,
        int maxBufferedResults,
        int maxSpoolMb = 512,
        int maxSpoolDays = 7)
    {
        return new StationSpoolStore(
            Options.Create(new StationSyncOptions
            {
                MaxBufferedResults = maxBufferedResults,
                MaxSpoolMb = maxSpoolMb,
                MaxSpoolDays = maxSpoolDays,
                SpoolDirectoryPath = spoolDirectory
            }),
            NullLogger<StationSpoolStore>.Instance);
    }

    private static StationRegistryService CreateRegistry()
    {
        return new StationRegistryService(
            Options.Create(new StationIngressOptions
            {
                Enabled = true,
                OfflineThresholdSeconds = 15,
                ResultBufferPerStation = 20,
                EventBufferSize = 50
            }),
            NullLogger<StationRegistryService>.Instance);
    }

    private static StationResultSummaryDto BuildResult(long sequenceId, string runId)
    {
        return new StationResultSummaryDto
        {
            StationId = "station-offline",
            LineName = "line-a",
            SequenceId = sequenceId,
            RunId = runId,
            PackageId = "pkg-1",
            PackageName = "Package 1",
            FlowHash = "sha256:abc",
            ImageId = $"image-{runId}",
            Outcome = RuntimeRunOutcome.Ok,
            InspectionStatus = InspectionStatus.OK,
            ExecutionTimeMs = 20 + (int)Math.Max(0, sequenceId),
            DiagnosticCode = "OK",
            DiagnosticMessage = "accepted",
            StartedAtUtc = DateTimeOffset.UtcNow.AddMilliseconds(-20),
            CompletedAtUtc = DateTimeOffset.UtcNow
        };
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "ClearVisionStationSpoolTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteTempDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}
