using Acme.Product.Desktop.Station;
using Acme.Product.Runtime.Abstractions;
using FluentAssertions;
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
