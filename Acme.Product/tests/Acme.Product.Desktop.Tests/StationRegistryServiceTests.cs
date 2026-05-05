using Acme.Product.Desktop.Station;
using Acme.Product.Infrastructure.Data;
using Acme.Product.Runtime.Abstractions;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Acme.Product.Desktop.Tests;

public sealed class StationRegistryServiceTests
{
    [Fact]
    public void UpsertResultSummary_ShouldIgnoreDuplicateSequenceIds()
    {
        var registry = CreateRegistry();
        registry.UpsertRegistration("conn-1", new StationRegistrationDto
        {
            StationId = "station-a",
            LineName = "line-1",
            MachineName = "machine-a",
            ClientVersion = "1.0.0",
            StartedAtUtc = DateTimeOffset.UtcNow
        });

        registry.UpsertSnapshot("conn-1", new StationSnapshotDto
        {
            StationId = "station-a",
            LineName = "line-1",
            State = RuntimeHostState.Running,
            SessionOkCount = 10,
            SessionNgCount = 2,
            SessionErrorCount = 1
        });

        registry.UpsertResultSummary("conn-1", BuildResult(sequenceId: 7, diagnosticCode: "NG-1"));
        registry.UpsertResultSummary("conn-1", BuildResult(sequenceId: 7, diagnosticCode: "NG-duplicate"));

        var station = registry.GetStation("station-a");

        station.Should().NotBeNull();
        station!.RecentResults.Should().HaveCount(1);
        station.LastSequenceId.Should().Be(7);
        station.LastDiagnosticCode.Should().Be("NG-1");
    }

    [Fact]
    public void UpsertResultSummary_ShouldAckOnlyContiguousSequencesAcrossGaps()
    {
        var registry = CreateRegistry();
        registry.UpsertRegistration("conn-gap", new StationRegistrationDto
        {
            StationId = "station-gap",
            MachineName = "machine-gap",
            ClientVersion = "1.0.0",
            StartedAtUtc = DateTimeOffset.UtcNow
        });

        var ack1 = registry.UpsertResultSummary("conn-gap", BuildResult(sequenceId: 1, stationId: "station-gap", diagnosticCode: "SEQ-1"));
        var ack3 = registry.UpsertResultSummary("conn-gap", BuildResult(sequenceId: 3, stationId: "station-gap", diagnosticCode: "SEQ-3"));
        var ack2 = registry.UpsertResultSummary("conn-gap", BuildResult(sequenceId: 2, stationId: "station-gap", diagnosticCode: "SEQ-2"));

        ack1.AckedSequenceId.Should().Be(1);
        ack3.AckedSequenceId.Should().Be(1);
        ack2.AckedSequenceId.Should().Be(3);
        registry.GetStation("station-gap")!.RecentResults
            .Select(result => result.SequenceId)
            .Should().Equal(2, 3, 1);
    }

    [Fact]
    public void UpsertResultSummary_ShouldNotAddRepeatedResultToRecentResults()
    {
        var registry = CreateRegistry();
        registry.UpsertRegistration("conn-repeat", new StationRegistrationDto
        {
            StationId = "station-repeat",
            MachineName = "machine-repeat",
            ClientVersion = "1.0.0",
            StartedAtUtc = DateTimeOffset.UtcNow
        });

        for (var i = 0; i < 10; i++)
        {
            registry.UpsertResultSummary("conn-repeat", BuildResult(sequenceId: 1, stationId: "station-repeat", diagnosticCode: $"DUP-{i}"));
        }

        var station = registry.GetStation("station-repeat")!;
        station.LastSequenceId.Should().Be(1);
        station.RecentResults.Should().ContainSingle();
        station.RecentResults[0].DiagnosticCode.Should().Be("DUP-0");
    }

    [Fact]
    public async Task CentralStore_ShouldRecoverContiguousCursorAcrossStudioRestart()
    {
        var root = Path.Combine(Path.GetTempPath(), "ClearVisionStationCentralStoreTests", Guid.NewGuid().ToString("N"));
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

            var firstStore = CreateCentralStore(provider);
            var ack1 = firstStore.UpsertResultSummary(BuildResult(1, stationId: "station-central", diagnosticCode: "SEQ-1"));
            var ack3 = firstStore.UpsertResultSummary(BuildResult(3, stationId: "station-central", diagnosticCode: "SEQ-3"));
            var ack2 = firstStore.UpsertResultSummary(BuildResult(2, stationId: "station-central", diagnosticCode: "SEQ-2"));

            ack1.LastPersistedSequenceId.Should().Be(1);
            ack3.LastPersistedSequenceId.Should().Be(1);
            ack2.LastPersistedSequenceId.Should().Be(3);

            var restartedStore = CreateCentralStore(provider);
            var duplicateAck = restartedStore.UpsertResultSummary(BuildResult(2, stationId: "station-central", diagnosticCode: "DUP-2"));
            duplicateAck.Duplicate.Should().BeTrue();
            duplicateAck.LastPersistedSequenceId.Should().Be(3);

            await using var verifyScope = provider.CreateAsyncScope();
            var db = verifyScope.ServiceProvider.GetRequiredService<VisionDbContext>();
            (await db.StationResultSummaries.CountAsync(item => item.StationId == "station-central")).Should().Be(3);
            (await db.StationSyncCursors.SingleAsync(item => item.StationId == "station-central"))
                .LastPersistedSequenceId
                .Should()
                .Be(3);
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
                // SQLite can keep a short-lived file handle on Windows after disposal.
            }
        }
    }

    [Fact]
    public void MarkDisconnected_ShouldMoveStationIntoOfflineSummaryBucket()
    {
        var registry = CreateRegistry();
        registry.UpsertRegistration("conn-2", new StationRegistrationDto
        {
            StationId = "station-b",
            MachineName = "machine-b",
            ClientVersion = "1.0.0",
            StartedAtUtc = DateTimeOffset.UtcNow
        });

        registry.MarkDisconnected("conn-2");

        var summary = registry.GetSummary();
        var station = registry.GetStation("station-b");

        summary.TotalStations.Should().Be(1);
        summary.OnlineStations.Should().Be(0);
        summary.OfflineStations.Should().Be(1);
        station.Should().NotBeNull();
        station!.IsOnline.Should().BeFalse();
    }

    [Fact]
    public void GetSseSnapshot_ShouldIncludeRecentResultEnvelope()
    {
        var registry = CreateRegistry();
        registry.UpsertRegistration("conn-3", new StationRegistrationDto
        {
            StationId = "station-c",
            MachineName = "machine-c",
            ClientVersion = "1.0.0",
            StartedAtUtc = DateTimeOffset.UtcNow
        });
        registry.UpsertResultSummary("conn-3", BuildResult(sequenceId: 11, stationId: "station-c", diagnosticCode: "OK"));

        var snapshot = registry.GetSseSnapshot();

        snapshot.Stations.Should().ContainSingle(station => station.StationId == "station-c");
        snapshot.RecentResults.Should().ContainSingle(item =>
            item.StationId == "station-c" &&
            item.Result.SequenceId == 11 &&
            item.Result.DiagnosticCode == "OK");
    }

    [Fact]
    public void UpsertHealthAndLog_ShouldSurfaceTelemetryInDetail()
    {
        var registry = CreateRegistry();
        registry.UpsertRegistration("conn-4", new StationRegistrationDto
        {
            StationId = "station-d",
            MachineName = "machine-d",
            ClientVersion = "1.0.0",
            StartedAtUtc = DateTimeOffset.UtcNow
        });

        registry.UpsertHealthSnapshot("conn-4", new StationHealthSnapshotDto
        {
            StationId = "station-d",
            SequenceId = 1,
            MessageId = "health-1",
            RuntimeState = StationRuntimeState.Running,
            WorkingSetMb = 256,
            DiskFreeMb = 5000,
            DiskTotalMb = 10000,
            SpoolPendingCount = 3,
            CurrentPackageHealth = "Loaded",
            CreatedAtUtc = DateTimeOffset.UtcNow
        });
        registry.UpsertLogSummary("conn-4", new StationLogSummaryDto
        {
            StationId = "station-d",
            SequenceId = 1,
            MessageId = "log-1",
            Level = "WARN",
            Source = "RuntimeHost",
            RenderedMessage = "sample warning",
            TimestampUtc = DateTimeOffset.UtcNow,
            CreatedAtUtc = DateTimeOffset.UtcNow
        });

        var station = registry.GetStation("station-d");

        station.Should().NotBeNull();
        station!.RuntimeState.Should().Be(StationRuntimeState.Running);
        station.SpoolPendingCount.Should().Be(3);
        station.RecentHealth.Should().ContainSingle(item => item.MessageId == "health-1");
        station.RecentLogs.Should().ContainSingle(item => item.MessageId == "log-1");
    }

    [Fact]
    public void UpdateIdentity_ShouldUpdateMemoryAndDisableOnlineState()
    {
        var registry = CreateRegistry();
        registry.UpsertRegistration("conn-5", new StationRegistrationDto
        {
            StationId = "station-e",
            MachineName = "machine-e",
            ClientVersion = "1.0.0",
            StartedAtUtc = DateTimeOffset.UtcNow
        });

        var updated = registry.UpdateIdentity(
            "station-e",
            new StationIdentityUpdateRequest
            {
                StationName = "Press Station",
                LineName = "line-7",
                IsEnabled = false,
                UpdatedBy = "unit-test"
            },
            "unit-test",
            null);

        updated.StationName.Should().Be("Press Station");
        updated.LineName.Should().Be("line-7");
        updated.IsEnabled.Should().BeFalse();
        updated.IsOnline.Should().BeFalse();
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

    private static StationCentralStore CreateCentralStore(ServiceProvider provider)
    {
        return new StationCentralStore(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<StationCentralStore>.Instance);
    }

    private static StationResultSummaryDto BuildResult(long sequenceId, string stationId = "station-a", string diagnosticCode = "NG")
    {
        return new StationResultSummaryDto
        {
            StationId = stationId,
            LineName = "line-1",
            SequenceId = sequenceId,
            RunId = $"run-{sequenceId}",
            PackageId = "pkg-1",
            PackageName = "Package 1",
            FlowHash = "sha256:abc",
            ImageId = $"image-{sequenceId}",
            Outcome = RuntimeRunOutcome.Ng,
            ExecutionTimeMs = 32,
            DiagnosticCode = diagnosticCode,
            DiagnosticMessage = "sample",
            StartedAtUtc = DateTimeOffset.UtcNow.AddMilliseconds(-32),
            CompletedAtUtc = DateTimeOffset.UtcNow
        };
    }
}
