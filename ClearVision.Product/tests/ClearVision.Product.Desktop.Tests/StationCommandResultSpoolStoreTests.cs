using ClearVision.Product.Runtime.Abstractions;
using ClearVision.Product.Station.Sync;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ClearVision.Product.Desktop.Tests;

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
