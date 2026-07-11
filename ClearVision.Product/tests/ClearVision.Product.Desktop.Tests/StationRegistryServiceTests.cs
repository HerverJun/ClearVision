using ClearVision.Product.Desktop.Station;
using ClearVision.Product.Infrastructure.Data;
using ClearVision.Product.Runtime.Abstractions;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ClearVision.Product.Desktop.Tests;

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
    public async Task CentralStore_ShouldFilterResultPagesAndStatisticsByDateRange_WithSqlite()
    {
        var root = Path.Combine(Path.GetTempPath(), "ClearVisionStationCentralStoreDateRangeTests", Guid.NewGuid().ToString("N"));
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

            var now = DateTimeOffset.UtcNow;
            var oldResult = BuildResult(1, stationId: "station-date", diagnosticCode: "OLD_DEFECT");
            oldResult.StartedAtUtc = now.AddHours(-6).AddMilliseconds(-32);
            oldResult.CompletedAtUtc = now.AddHours(-6);

            var matchingResult = BuildResult(2, stationId: "station-date", diagnosticCode: "WIRE_SWAP");
            matchingResult.StartedAtUtc = now.AddMinutes(-20).AddMilliseconds(-32);
            matchingResult.CompletedAtUtc = now.AddMinutes(-20);

            var store = CreateCentralStore(provider);
            store.UpsertResultSummary(oldResult);
            store.UpsertResultSummary(matchingResult);

            var fromUtc = now.AddHours(-1);
            var toUtc = now.AddMinutes(1);
            var page = store.GetResultsPage("station-date", fromUtc, toUtc, "Ng", "WIRE_SWAP", 0, 10);
            var statisticsJson = System.Text.Json.JsonSerializer.Serialize(
                store.GetStatistics(fromUtc, toUtc, "station-date", "Ng", "WIRE_SWAP"));
            using var statistics = System.Text.Json.JsonDocument.Parse(statisticsJson);

            page.TotalCount.Should().Be(1);
            page.Items.Should().ContainSingle(item =>
                item.SequenceId == 2 &&
                item.DiagnosticCode == "WIRE_SWAP");
            statistics.RootElement.GetProperty("totalCount").GetInt32().Should().Be(1);
            statistics.RootElement.GetProperty("ngCount").GetInt32().Should().Be(1);
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
    public void ReportResultGap_ShouldAdvanceMemoryCursorAcrossUnavailableRange()
    {
        var registry = CreateRegistry();
        registry.UpsertRegistration("conn-gap-report", new StationRegistrationDto
        {
            StationId = "station-gap-report",
            MachineName = "machine-gap-report",
            ClientVersion = "1.0.0",
            StartedAtUtc = DateTimeOffset.UtcNow
        });

        var gapAck = registry.ReportResultGap("conn-gap-report", new StationResultGapDto
        {
            StationId = "station-gap-report",
            DroppedFromSequenceId = 101,
            DroppedThroughSequenceId = 200,
            Reason = "unit-test"
        });
        var resultAck = registry.UpsertResultSummary("conn-gap-report", BuildResult(201, stationId: "station-gap-report", diagnosticCode: "SEQ-201"));

        gapAck.LastPersistedSequenceId.Should().Be(200);
        resultAck.AckedSequenceId.Should().Be(201);
        registry.GetStation("station-gap-report")!.LastSequenceId.Should().Be(201);
    }

    [Fact]
    public async Task CentralStore_ShouldAdvanceCursorAcrossUnavailableResultGap()
    {
        var root = Path.Combine(Path.GetTempPath(), "ClearVisionStationResultGapTests", Guid.NewGuid().ToString("N"));
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

            var store = CreateCentralStore(provider);
            var gapAck = store.ReportResultGap(new StationResultGapDto
            {
                StationId = "station-central-gap",
                DroppedFromSequenceId = 101,
                DroppedThroughSequenceId = 200,
                Reason = "unit-test"
            });
            var resultAck = store.UpsertResultSummary(BuildResult(201, stationId: "station-central-gap", diagnosticCode: "SEQ-201"));

            gapAck.LastPersistedSequenceId.Should().Be(200);
            resultAck.LastPersistedSequenceId.Should().Be(201);

            await using var verifyScope = provider.CreateAsyncScope();
            var db = verifyScope.ServiceProvider.GetRequiredService<VisionDbContext>();
            (await db.StationSyncCursors.SingleAsync(item => item.StationId == "station-central-gap"))
                .LastPersistedSequenceId
                .Should()
                .Be(201);
            (await db.StationConnectionEvents.CountAsync(item =>
                item.StationId == "station-central-gap" &&
                item.EventType == "ResultGap")).Should().Be(1);
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
    public async Task CentralStore_ShouldPersistDeployCommandFailuresAndAuditTrail()
    {
        var root = Path.Combine(Path.GetTempPath(), "ClearVisionStationCommandAuditTests", Guid.NewGuid().ToString("N"));
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

            var store = CreateCentralStore(provider);
            var command = store.CreateCommand(
                "station-deploy",
                StationCommandType.DeployPackage,
                """{"packageId":"pkg-bad","sha256":"sha256:bad"}""",
                "unit-test",
                TimeSpan.FromMinutes(30));
            var delivered = store.PollCommand("station-deploy");
            delivered.Should().NotBeNull();
            delivered!.Status.Should().Be(StationCommandStatus.Delivered);

            store.ReportCommandResult(new StationCommandResultDto
            {
                CommandId = command.CommandId,
                StationId = "station-deploy",
                Status = StationCommandStatus.Accepted,
                ProgressPercent = 0,
                Message = "Accepted"
            });
            store.ReportCommandResult(new StationCommandResultDto
            {
                CommandId = command.CommandId,
                StationId = "station-deploy",
                Status = StationCommandStatus.Running,
                ProgressPercent = 50,
                Message = "Verifying package"
            });
            var failed = store.ReportCommandResult(new StationCommandResultDto
            {
                CommandId = command.CommandId,
                StationId = "station-deploy",
                Status = StationCommandStatus.Failed,
                ProgressPercent = 100,
                Message = "Downloaded package hash does not match the Studio manifest.",
                ErrorCode = "CommandFailed",
                ErrorDetail = "hash mismatch"
            });

            failed.Should().NotBeNull();
            failed!.Status.Should().Be(StationCommandStatus.Failed);
            failed.ErrorCode.Should().Be("CommandFailed");

            await using var verifyScope = provider.CreateAsyncScope();
            var db = verifyScope.ServiceProvider.GetRequiredService<VisionDbContext>();
            var persisted = await db.StationCommandRecords.SingleAsync(item => item.CommandId == command.CommandId);
            persisted.Status.Should().Be(StationCommandStatus.Failed.ToString());
            persisted.ResultMessage.Should().Contain("hash");

            var audits = db.StationAuditRecords
                .Where(item => item.CommandId == command.CommandId)
                .AsEnumerable()
                .OrderBy(item => item.CreatedAtUtc)
                .ToList();
            audits.Select(item => item.Action).Should().Contain(StationCommandType.DeployPackage.ToString());
            audits.Select(item => item.Action).Should().Contain("CommandCompleted");
            audits.Last().Result.Should().Be(StationCommandStatus.Failed.ToString());
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
    public async Task CentralStore_ShouldPersistStartRuntimeAndApplySiteProfileCommandLifecycle()
    {
        var root = Path.Combine(Path.GetTempPath(), "ClearVisionStationCommandLifecycleTests", Guid.NewGuid().ToString("N"));
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

            var store = CreateCentralStore(provider);
            var start = store.CreateCommand(
                "station-command",
                StationCommandType.StartRuntime,
                """{"imagePath":"D:\\station\\samples\\a.png"}""",
                "unit-test",
                TimeSpan.FromMinutes(5));
            var deliveredStart = store.PollCommand("station-command");

            deliveredStart.Should().NotBeNull();
            deliveredStart!.CommandId.Should().Be(start.CommandId);
            deliveredStart.CommandType.Should().Be(StationCommandType.StartRuntime);

            store.ReportCommandResult(new StationCommandResultDto
            {
                CommandId = start.CommandId,
                StationId = "station-command",
                Status = StationCommandStatus.Accepted,
                ProgressPercent = 0,
                Message = "Accepted"
            });
            store.ReportCommandResult(new StationCommandResultDto
            {
                CommandId = start.CommandId,
                StationId = "station-command",
                Status = StationCommandStatus.Succeeded,
                ProgressPercent = 100,
                Message = "Runtime completed: Ok"
            });

            var apply = store.CreateCommand(
                "station-command",
                StationCommandType.ApplySiteProfile,
                """{"profile":{"packageId":"pkg-1","flowHash":"sha256:abc","overrides":[]}}""",
                "unit-test",
                TimeSpan.FromMinutes(5));
            var deliveredApply = store.PollCommand("station-command");

            deliveredApply.Should().NotBeNull();
            deliveredApply!.CommandId.Should().Be(apply.CommandId);
            deliveredApply.CommandType.Should().Be(StationCommandType.ApplySiteProfile);
            store.ReportCommandResult(new StationCommandResultDto
            {
                CommandId = apply.CommandId,
                StationId = "station-command",
                Status = StationCommandStatus.Accepted,
                ProgressPercent = 0,
                Message = "Accepted"
            });
            store.ReportCommandResult(new StationCommandResultDto
            {
                CommandId = apply.CommandId,
                StationId = "station-command",
                Status = StationCommandStatus.Succeeded,
                ProgressPercent = 100,
                Message = "Site profile applied"
            });

            await using var verifyScope = provider.CreateAsyncScope();
            var db = verifyScope.ServiceProvider.GetRequiredService<VisionDbContext>();
            var commands = await db.StationCommandRecords
                .Where(item => item.StationId == "station-command")
                .ToListAsync();
            commands.Should().HaveCount(2);
            commands.Should().OnlyContain(item => item.Status == StationCommandStatus.Succeeded.ToString());

            var audits = await db.StationAuditRecords
                .Where(item => item.TargetStationId == "station-command")
                .ToListAsync();
            audits.Select(item => item.Action).Should().Contain(StationCommandType.StartRuntime.ToString());
            audits.Select(item => item.Action).Should().Contain(StationCommandType.ApplySiteProfile.ToString());
            audits.Count(item => item.Action == "CommandCompleted").Should().Be(2);
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
    public async Task CentralStore_ShouldRedeliverStaleDeliveredCommand()
    {
        var root = Path.Combine(Path.GetTempPath(), "ClearVisionStationCommandRedeliveryTests", Guid.NewGuid().ToString("N"));
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

            var store = CreateCentralStore(provider);
            var command = store.CreateCommand(
                "station-redelivery",
                StationCommandType.Ping,
                "{}",
                "unit-test",
                TimeSpan.FromMinutes(30));

            var delivered = store.PollCommand("station-redelivery");
            var immediateRetry = store.PollCommand("station-redelivery");

            delivered.Should().NotBeNull();
            delivered!.CommandId.Should().Be(command.CommandId);
            delivered.Status.Should().Be(StationCommandStatus.Delivered);
            immediateRetry.Should().BeNull();

            await using (var mutateScope = provider.CreateAsyncScope())
            {
                var db = mutateScope.ServiceProvider.GetRequiredService<VisionDbContext>();
                var persisted = await db.StationCommandRecords.SingleAsync(item => item.CommandId == command.CommandId);
                persisted.DeliveredAtUtc = DateTimeOffset.UtcNow.AddSeconds(-30);
                await db.SaveChangesAsync();
            }

            var redelivered = store.PollCommand("station-redelivery");

            redelivered.Should().NotBeNull();
            redelivered!.CommandId.Should().Be(command.CommandId);
            redelivered.Status.Should().Be(StationCommandStatus.Delivered);
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

    [Fact]
    public async Task ExecutionIdentity_ShouldRoundTripRegistryDatabaseSseAndDetail()
    {
        var root = Path.Combine(Path.GetTempPath(), "ClearVisionStationIdentityTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var provider = new ServiceCollection()
            .AddDbContext<VisionDbContext>(options => options.UseSqlite($"Data Source={Path.Combine(root, "vision.db")}"))
            .BuildServiceProvider();
        try
        {
            await using (var scope = provider.CreateAsyncScope())
            {
                await scope.ServiceProvider.GetRequiredService<VisionDbContext>().Database.EnsureCreatedAsync();
            }

            var snapshotId = Guid.NewGuid();
            var central = CreateCentralStore(provider);
            var registry = CreateRegistry(central);
            registry.UpsertHeartbeat("conn", new StationHeartbeatDto
            {
                StationId = "station-identity",
                State = RuntimeHostState.Running,
                PackageId = "pkg-site",
                PackageFlowHash = "sha256:package",
                ExecutionFlowHash = "sha256:site-profile",
                FlowHash = "sha256:site-profile",
                ExecutionSnapshotId = snapshotId,
                ProjectRevision = 42,
                DecisionConfigurationHash = "sha256:decision",
                ExecutionRunMode = "StationRuntime",
                CurrentRunId = "run-site"
            });
            var result = BuildResult(1, "station-identity");
            result.PackageId = "pkg-site";
            result.PackageFlowHash = "sha256:package";
            result.ExecutionFlowHash = "sha256:site-profile";
            result.FlowHash = result.ExecutionFlowHash;
            result.ExecutionSnapshotId = snapshotId;
            result.ProjectRevision = 42;
            result.DecisionConfigurationHash = "sha256:decision";
            result.ExecutionRunMode = "StationRuntime";
            result.RunId = "run-site";
            registry.UpsertResultSummary("conn", result);

            var detail = registry.GetStation("station-identity")!;
            var sse = registry.GetSseSnapshot().Stations.Single(item => item.StationId == "station-identity");
            detail.ExecutionFlowHash.Should().Be(result.ExecutionFlowHash);
            detail.ExecutionSnapshotId.Should().Be(snapshotId);
            detail.ProjectRevision.Should().Be(42);
            detail.DecisionConfigurationHash.Should().Be(result.DecisionConfigurationHash);
            sse.Should().BeEquivalentTo(detail, options => options.ExcludingMissingMembers());

            var reloaded = CreateRegistry(CreateCentralStore(provider)).GetStation("station-identity")!;
            reloaded.PackageFlowHash.Should().Be("sha256:package");
            reloaded.ExecutionFlowHash.Should().Be("sha256:site-profile");
            reloaded.FlowHash.Should().Be(reloaded.ExecutionFlowHash);
            reloaded.ExecutionSnapshotId.Should().Be(snapshotId);
            reloaded.ProjectRevision.Should().Be(42);
            reloaded.DecisionConfigurationHash.Should().Be("sha256:decision");
            reloaded.ExecutionRunMode.Should().Be("StationRuntime");
            reloaded.CurrentRunId.Should().Be("run-site");

            var persistedResult = central.GetRecentResults("station-identity", 10).Single();
            persistedResult.PackageFlowHash.Should().Be(result.PackageFlowHash);
            persistedResult.ExecutionFlowHash.Should().Be(result.ExecutionFlowHash);
            persistedResult.FlowHash.Should().Be(result.ExecutionFlowHash);
            persistedResult.ExecutionSnapshotId.Should().Be(result.ExecutionSnapshotId);
            persistedResult.ProjectRevision.Should().Be(result.ProjectRevision);
            persistedResult.DecisionConfigurationHash.Should().Be(result.DecisionConfigurationHash);
            persistedResult.ExecutionRunMode.Should().Be(result.ExecutionRunMode);
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
    public void LegacyHeartbeat_ShouldProjectFlowHashWithoutInventingCanonicalIdentity()
    {
        var registry = CreateRegistry();
        registry.UpsertHeartbeat("legacy", new StationHeartbeatDto
        {
            StationId = "legacy-station",
            FlowHash = "sha256:legacy"
        });

        var station = registry.GetStation("legacy-station")!;
        station.ExecutionFlowHash.Should().Be("sha256:legacy");
        station.FlowHash.Should().Be("sha256:legacy");
        station.PackageFlowHash.Should().BeNull();
        station.ExecutionSnapshotId.Should().BeNull();
        station.ProjectRevision.Should().BeNull();
        station.DecisionConfigurationHash.Should().BeNull();
    }

    private static StationRegistryService CreateRegistry(StationCentralStore centralStore)
    {
        return new StationRegistryService(
            Options.Create(new StationIngressOptions
            {
                Enabled = true,
                OfflineThresholdSeconds = 15,
                ResultBufferPerStation = 20,
                EventBufferSize = 50
            }),
            NullLogger<StationRegistryService>.Instance,
            centralStore);
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
