using ClearVision.Product.Runtime.Abstractions;
using ClearVision.Product.Station.Sync;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ClearVision.Product.Desktop.Tests;

[TestClassification(TestDomain.Desktop, TestPurpose.Regression, TestLane.Pr, TestEvidenceType.Contract, TestOracleType.Contract, TestResourceRequirement.None, TestExpectedDuration.Fast, TestFlakyPolicy.Blocking, "desktop")]
public sealed class StationCommandResultSpoolStoreTests
{
    [Fact]
    public void CommandResultSpool_ShouldSurviveRestartAndAcknowledgeSentStatus()
    {
        var root = Path.Combine(Path.GetTempPath(), "ClearVisionStationCommandResultSpoolTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var store = CreateStore(root);
            store.Enqueue(new StationCommandResultDto
            {
                CommandId = "cmd-1",
                StationId = "station-1",
                Status = StationCommandStatus.Succeeded,
                ProgressPercent = 100,
                Message = "done",
                CompletedAtUtc = DateTimeOffset.UtcNow
            });

            var restarted = CreateStore(root);
            restarted.GetPendingBatch(10).Should().ContainSingle(item =>
                item.CommandId == "cmd-1" &&
                item.Status == StationCommandStatus.Succeeded);

            restarted.Acknowledge("cmd-1", StationCommandStatus.Succeeded);
            File.ReadLines(Path.Combine(root, "station-command-results.jsonl")).Should().HaveCount(2);
            CreateStore(root).GetPendingBatch(10).Should().BeEmpty();
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void CommandResultSpool_ShouldAppendUpdatesAndReplayLatestStatus()
    {
        var root = Path.Combine(Path.GetTempPath(), "ClearVisionStationCommandResultSpoolTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var store = CreateStore(root);
            store.Enqueue(BuildResult("cmd-append", StationCommandStatus.Running, "first"));
            store.Enqueue(BuildResult("cmd-append", StationCommandStatus.Running, "second"));

            var filePath = Path.Combine(root, "station-command-results.jsonl");
            File.ReadLines(filePath).Should().HaveCount(2);

            var restarted = CreateStore(root);
            var pending = restarted.GetPendingBatch(10).Should().ContainSingle().Subject;
            pending.CommandId.Should().Be("cmd-append");
            pending.Status.Should().Be(StationCommandStatus.Running);
            pending.Message.Should().Be("second");
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void CommandResultSpool_ShouldCompactOperationLogAfterThreshold()
    {
        var root = Path.Combine(Path.GetTempPath(), "ClearVisionStationCommandResultSpoolTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var store = CreateStore(root);
            for (var index = 1; index <= 520; index++)
            {
                store.Enqueue(BuildResult("cmd-compact", StationCommandStatus.Running, $"progress-{index:D3}"));
            }

            var filePath = Path.Combine(root, "station-command-results.jsonl");
            File.ReadLines(filePath).Should().HaveCountLessThan(520);

            var restarted = CreateStore(root);
            var pending = restarted.GetPendingBatch(10).Should().ContainSingle().Subject;
            pending.CommandId.Should().Be("cmd-compact");
            pending.Message.Should().Be("progress-520");
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void CommandResultSpool_ShouldTrimOldestPendingRecordsAndPreserveStableReplayOrder()
    {
        var root = Path.Combine(Path.GetTempPath(), "ClearVisionStationCommandResultSpoolTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var now = new DateTimeOffset(2026, 8, 31, 8, 0, 0, TimeSpan.Zero);

        try
        {
            var options = Options.Create(new StationSyncOptions
            {
                SpoolDirectoryPath = root,
                MaxCommandResultSpoolRecords = 2,
                MaxCommandResultSpoolMb = 1,
                MaxCommandResultSpoolDays = 7
            });
            var store = new StationCommandResultSpoolStore(
                options,
                NullLogger<StationCommandResultSpoolStore>.Instance,
                () => now);

            var first = BuildResult("cmd-1", StationCommandStatus.Running, "first");
            first.CreatedAtUtc = now;
            store.Enqueue(first);
            now = now.AddMinutes(1);
            var second = BuildResult("cmd-2", StationCommandStatus.Running, "second");
            second.CreatedAtUtc = now;
            store.Enqueue(second);
            now = now.AddMinutes(1);
            var third = BuildResult("cmd-3", StationCommandStatus.Succeeded, "third");
            third.CreatedAtUtc = now;
            store.Enqueue(third);

            var health = store.GetHealth();
            health.PendingCount.Should().Be(2);
            health.PendingBytes.Should().BeGreaterThan(0);
            health.OldestPendingAtUtc.Should().Be(now.AddMinutes(-1));
            health.TrimmedCount.Should().Be(1);
            health.GapDetected.Should().BeTrue();
            health.Degraded.Should().BeFalse();

            var restarted = new StationCommandResultSpoolStore(
                options,
                NullLogger<StationCommandResultSpoolStore>.Instance,
                () => now);
            var pending = restarted.GetPendingBatch(10);
            pending.Select(item => item.CommandId).Should().ContainInOrder("cmd-2", "cmd-3");
            pending.Select(item => (item.CommandId, item.Status)).Should().OnlyHaveUniqueItems();
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static StationCommandResultSpoolStore CreateStore(string root)
    {
        return new StationCommandResultSpoolStore(
            Options.Create(new StationSyncOptions
            {
                SpoolDirectoryPath = root
            }),
            NullLogger<StationCommandResultSpoolStore>.Instance);
    }

    private static StationCommandResultDto BuildResult(string commandId, StationCommandStatus status, string message)
    {
        return new StationCommandResultDto
        {
            CommandId = commandId,
            StationId = "station-1",
            Status = status,
            ProgressPercent = status == StationCommandStatus.Succeeded ? 100 : 50,
            Message = message,
            ReportedAtUtc = DateTimeOffset.UtcNow
        };
    }
}
