using System.Text.Json;
using System.Text.Json.Serialization;
using Acme.Product.Core.Enums;
using Acme.Product.Desktop.Station;
using Acme.Product.Runtime.Abstractions;
using Acme.Product.Station.Sync;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Acme.Product.Desktop.Tests;

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
    public void Enqueue_ShouldKeepDiskSpoolWithinCapacity_WhenOverflowTrimsPendingResults()
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

            persisted.Should().HaveCount(100);
            persisted.Select(result => result.SequenceId).Should().Equal(Enumerable.Range(6, 100).Select(value => (long)value));

            var restartedSpool = CreateSpool(spoolDirectory, maxBufferedResults: 100);
            restartedSpool.GetPendingBatch(200).Select(result => result.SequenceId)
                .Should()
                .Equal(Enumerable.Range(6, 100).Select(value => (long)value));
        }
        finally
        {
            DeleteTempDirectory(spoolDirectory);
        }
    }

    private static StationSpoolStore CreateSpool(string spoolDirectory, int maxBufferedResults)
    {
        return new StationSpoolStore(
            Options.Create(new StationSyncOptions
            {
                MaxBufferedResults = maxBufferedResults,
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
