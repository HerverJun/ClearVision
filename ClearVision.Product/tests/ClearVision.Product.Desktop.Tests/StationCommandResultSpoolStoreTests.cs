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

    private static StationCommandResultSpoolStore CreateStore(string root)
    {
        return new StationCommandResultSpoolStore(
            Options.Create(new StationSyncOptions
            {
                SpoolDirectoryPath = root
            }),
            NullLogger<StationCommandResultSpoolStore>.Instance);
    }
}
