using ClearVision.Product.Runtime.Abstractions;
using ClearVision.Product.Station.Sync;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ClearVision.Product.Desktop.Tests;

public sealed class StationCommandExecutionJournalStoreTests
{
    [Fact]
    public void CommandExecutionJournal_ShouldSurviveRestartAndReturnTerminalResult()
    {
        var root = Path.Combine(Path.GetTempPath(), "ClearVisionStationCommandExecutionJournalTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var command = BuildCommand("cmd-1", "{}");
            var store = CreateStore(root);
            store.RecordTerminalResult(
                command,
                new StationCommandResultDto
                {
                    CommandId = command.CommandId,
                    StationId = command.StationId,
                    Status = StationCommandStatus.Succeeded,
                    ProgressPercent = 100,
                    Message = "done",
                    CompletedAtUtc = DateTimeOffset.UtcNow
                });

            var restarted = CreateStore(root);

            restarted.TryGetTerminalResult(command, out var result).Should().BeTrue();
            result.Status.Should().Be(StationCommandStatus.Succeeded);
            result.Message.Should().Be("done");
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
    public void CommandExecutionJournal_ShouldIgnoreNonTerminalResults()
    {
        var root = Path.Combine(Path.GetTempPath(), "ClearVisionStationCommandExecutionJournalTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var command = BuildCommand("cmd-running", "{}");
            var store = CreateStore(root);
            store.RecordTerminalResult(
                command,
                new StationCommandResultDto
                {
                    CommandId = command.CommandId,
                    StationId = command.StationId,
                    Status = StationCommandStatus.Running,
                    ProgressPercent = 50,
                    Message = "running"
                });

            CreateStore(root).TryGetTerminalResult(command, out _).Should().BeFalse();
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
    public void CommandExecutionJournal_ShouldNotReplayWhenPayloadChangedForSameCommandId()
    {
        var root = Path.Combine(Path.GetTempPath(), "ClearVisionStationCommandExecutionJournalTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var original = BuildCommand("cmd-1", """{"packageId":"pkg-a"}""");
            var changed = BuildCommand("cmd-1", """{"packageId":"pkg-b"}""");
            var store = CreateStore(root);
            store.RecordTerminalResult(
                original,
                new StationCommandResultDto
                {
                    CommandId = original.CommandId,
                    StationId = original.StationId,
                    Status = StationCommandStatus.Succeeded,
                    ProgressPercent = 100,
                    Message = "done",
                    CompletedAtUtc = DateTimeOffset.UtcNow
                });

            CreateStore(root).TryGetTerminalResult(changed, out _).Should().BeFalse();
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static StationCommandExecutionJournalStore CreateStore(string root)
    {
        return new StationCommandExecutionJournalStore(
            Options.Create(new StationSyncOptions
            {
                SpoolDirectoryPath = root
            }),
            NullLogger<StationCommandExecutionJournalStore>.Instance);
    }

    private static StationCommandDto BuildCommand(string commandId, string payloadJson)
    {
        return new StationCommandDto
        {
            CommandId = commandId,
            StationId = "station-1",
            CommandType = StationCommandType.DeployPackage,
            PayloadJson = payloadJson,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(5),
            IssuedBy = "unit-test"
        };
    }
}
