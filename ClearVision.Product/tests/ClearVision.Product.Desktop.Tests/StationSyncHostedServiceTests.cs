using System.Diagnostics;
using System.Reflection;
using ClearVision.Product.Application.DTOs;
using ClearVision.Product.Core.Cameras;
using ClearVision.Product.Core.Decisions;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Services;
using ClearVision.Product.Infrastructure.Operators;
using ClearVision.Product.Infrastructure.Services;
using ClearVision.Product.Runtime;
using ClearVision.Product.Runtime.Abstractions;
using ClearVision.Product.Station;
using ClearVision.Product.Station.Sync;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace ClearVision.Product.Desktop.Tests;

public sealed class StationSyncHostedServiceTests
{
    [Fact]
    public void HeartbeatProjection_ShouldPreserveCurrentRuntimeExecutionIdentity()
    {
        var identity = new StationIdentityContext
        {
            StationId = "station-identity",
            LineName = "line-a",
            CurrentPackageVersion = "1.2.3"
        };
        var snapshotId = Guid.NewGuid();
        var snapshot = new RuntimeHostSnapshot
        {
            State = RuntimeHostState.Running,
            PackageId = "pkg-1",
            PackageName = "package",
            PackageFlowHash = "package-hash",
            ExecutionFlowHash = "execution-hash",
            FlowHash = "execution-hash",
            ExecutionSnapshotId = snapshotId,
            ProjectRevision = 42,
            DecisionConfigurationHash = "decision-hash",
            ExecutionRunMode = ExecutionRunMode.StationRuntime.ToString(),
            CurrentRunId = "run-1"
        };

        var heartbeat = InvokeBuildHeartbeat(identity, snapshot);

        heartbeat.PackageFlowHash.Should().Be(snapshot.PackageFlowHash);
        heartbeat.ExecutionFlowHash.Should().Be(snapshot.ExecutionFlowHash);
        heartbeat.FlowHash.Should().Be(snapshot.ExecutionFlowHash);
        heartbeat.ExecutionSnapshotId.Should().Be(snapshotId);
        heartbeat.ProjectRevision.Should().Be(42);
        heartbeat.DecisionConfigurationHash.Should().Be("decision-hash");
        heartbeat.ExecutionRunMode.Should().Be(ExecutionRunMode.StationRuntime.ToString());
    }

    [Fact]
    public async Task ResultIngress_ShouldDropTelemetryInsteadOfBlocking_WhenOutboundQueueIsFull()
    {
        var root = Path.Combine(Path.GetTempPath(), "ClearVisionStationSyncHostedServiceTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        await using var fixture = CreateFixture(root, outboundQueueCapacity: 1);
        try
        {
            InvokeResultAvailable(fixture.Service, BuildResult("run-1"));

            var stopwatch = Stopwatch.StartNew();
            var secondInvoke = Task.Run(() => InvokeResultAvailable(fixture.Service, BuildResult("run-2")));
            var completed = await Task.WhenAny(secondInvoke, Task.Delay(TimeSpan.FromMilliseconds(250)));
            stopwatch.Stop();

            completed.Should().Be(secondInvoke, "Station telemetry must not block the runtime result callback when the sync queue is saturated.");
            await secondInvoke;
            stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromMilliseconds(250));
            ReadPrivateLong(fixture.Service, "_droppedResultSummaries").Should().Be(1);
            ReadPrivateLong(fixture.Service, "_queuedResultSummaries").Should().Be(1);
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
    public async Task Health_ShouldSampleCpuUsageAfterInitialSnapshot()
    {
        var root = Path.Combine(Path.GetTempPath(), "ClearVisionStationSyncHealthCpuTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            await using var fixture = CreateFixture(root, outboundQueueCapacity: 4);

            var first = InvokeBuildHealth(fixture);
            first.CpuUsagePercent.Should().BeNull();

            await Task.Delay(TimeSpan.FromMilliseconds(25));

            var second = InvokeBuildHealth(fixture);
            second.CpuUsagePercent.Should().NotBeNull();
            second.CpuUsagePercent!.Value.Should().BeInRange(0d, 100d);
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
    public async Task Health_ShouldReportConnectedCameraBinding_WhenLoadedPackageUsesCameraSource()
    {
        var root = Path.Combine(Path.GetTempPath(), "ClearVisionStationSyncHealthCameraTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        var camera = Substitute.For<ICamera>();
        camera.IsConnected.Returns(true);

        var cameraManager = Substitute.For<ICameraManager>();
        cameraManager.GetBindings().Returns(new List<CameraBindingConfig>
        {
            new()
            {
                Id = "cam-a",
                DisplayName = "Station Camera A",
                SerialNumber = "SN-A",
                IsEnabled = true
            }
        });
        cameraManager.GetCamera("SN-A").Returns(camera);

        try
        {
            await using var fixture = CreateFixture(root, outboundQueueCapacity: 4, cameraManager);
            var export = await ExportCameraRuntimePackageAsync(root, "cam-a");
            await fixture.RuntimeHost.LoadPackageAsync(export.PackageRootPath);

            var health = InvokeBuildHealth(fixture);

            health.CameraStatusSummary.Should().Contain("Connected");
            health.CameraStatusSummary.Should().Contain("Station Camera A");
            health.CameraStatusSummary.Should().Contain("SN-A");
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
    public async Task Health_ShouldReportPendingPlc_WhenLoadedPackageUsesPlcButConnectionIsNotOpened()
    {
        var root = Path.Combine(Path.GetTempPath(), "ClearVisionStationSyncHealthPlcPendingTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            await using var fixture = CreateFixture(root, outboundQueueCapacity: 4);
            var export = await ExportPlcRuntimePackageAsync(root);
            await fixture.RuntimeHost.LoadPackageAsync(export.PackageRootPath);

            var health = InvokeBuildHealth(fixture);

            health.PlcStatusSummary.Should().StartWith("Pending:");
            health.PlcStatusSummary.Should().Contain("1 PLC operator");
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
    public async Task PlcHealthCore_ShouldReportDisconnected_WhenRuntimeConnectionStateIsOffline()
    {
        var root = Path.Combine(Path.GetTempPath(), "ClearVisionStationSyncHealthPlcDisconnectedTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            await using var fixture = CreateFixture(root, outboundQueueCapacity: 4);
            var export = await ExportPlcRuntimePackageAsync(root);
            await fixture.RuntimeHost.LoadPackageAsync(export.PackageRootPath);

            var summary = InvokeBuildPlcStatusSummaryCore(
                fixture.RuntimeHost.LoadedPackage!,
                fixture.RuntimeHost.GetSnapshot(),
                new Dictionary<string, bool>
                {
                    ["S7:192.168.10.25:102:S71200:0:1"] = false
                },
                new Dictionary<string, bool>());

            summary.Should().StartWith("Disconnected:");
            summary.Should().Contain("S7:192.168.10.25:102:S71200:0:1");
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
    public async Task CommandReplay_ShouldSpoolCachedTerminalResult_WhenCommandWasAlreadyCompletedLocally()
    {
        var root = Path.Combine(Path.GetTempPath(), "ClearVisionStationSyncCommandReplayTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            await using var fixture = CreateFixture(root, outboundQueueCapacity: 4, syncEnabled: false);
            var command = BuildCommand("cmd-replay-1", StationCommandType.DeployPackage, """{"packageId":"pkg-1"}""");
            fixture.CommandExecutionJournalStore.RecordTerminalResult(
                command,
                new StationCommandResultDto
                {
                    CommandId = command.CommandId,
                    StationId = command.StationId,
                    Status = StationCommandStatus.Succeeded,
                    ProgressPercent = 100,
                    Message = "Package pkg-1 deployed.",
                    CompletedAtUtc = DateTimeOffset.UtcNow
                });

            var replayed = await InvokeTryReplayCompletedCommandAsync(fixture.Service, command);

            replayed.Should().BeTrue();
            fixture.CommandResultSpoolStore.GetPendingBatch(10)
                .Select(item => item.Status)
                .Should()
                .ContainInOrder(
                    StationCommandStatus.Accepted,
                    StationCommandStatus.Running,
                    StationCommandStatus.Succeeded);
            fixture.CommandResultSpoolStore.GetPendingBatch(10).Last().Message.Should().Contain("replaying cached terminal result");
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
    public async Task ReportCommandAsync_WhenJournalWriteFails_ShouldStillReportRemoteResult()
    {
        var root = Path.Combine(Path.GetTempPath(), "ClearVisionStationSyncCommandJournalFailureTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var hubConnection = new RecordingStationHubConnection();

        try
        {
            await using var fixture = CreateFixture(
                root,
                outboundQueueCapacity: 4,
                connectionFactory: (_, _) => hubConnection);
            SetPrivateStringField(
                fixture.CommandExecutionJournalStore,
                "_filePath",
                Path.Combine(root, "missing-journal-dir", "journal.jsonl"));
            var command = BuildCommand("cmd-journal-fail", StationCommandType.Ping, "{}");

            await InvokeReportCommandAsync(
                fixture.Service,
                command,
                StationCommandStatus.Succeeded,
                100,
                "done");

            hubConnection.ReportedCommandResults.Should().ContainSingle(result =>
                result.CommandId == command.CommandId &&
                result.Status == StationCommandStatus.Succeeded);
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
    public async Task ReportCommandAsync_WhenRemoteAndSpoolWritesFail_ShouldNotThrow()
    {
        var root = Path.Combine(Path.GetTempPath(), "ClearVisionStationSyncCommandSpoolFailureTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var hubConnection = new RecordingStationHubConnection { FailInvocations = true };

        try
        {
            await using var fixture = CreateFixture(
                root,
                outboundQueueCapacity: 4,
                connectionFactory: (_, _) => hubConnection);
            SetPrivateStringField(
                fixture.CommandResultSpoolStore,
                "_filePath",
                Path.Combine(root, "missing-spool-dir", "command-results.jsonl"));
            var command = BuildCommand("cmd-spool-fail", StationCommandType.Ping, "{}");

            var act = () => InvokeReportCommandAsync(
                fixture.Service,
                command,
                StationCommandStatus.Running,
                50,
                "running");

            await act.Should().NotThrowAsync();
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
    public async Task TryPollAndExecuteCommandAsync_WhenCommandTypeIsUnsupported_ShouldReportChineseFailureMessage()
    {
        var root = Path.Combine(Path.GetTempPath(), "ClearVisionStationSyncUnsupportedCommandTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var hubConnection = new RecordingStationHubConnection
        {
            NextCommand = BuildCommand("cmd-unsupported", (StationCommandType)9999, "{}")
        };

        try
        {
            await using var fixture = CreateFixture(
                root,
                outboundQueueCapacity: 4,
                connectionFactory: (_, _) => hubConnection);

            var didWork = await InvokeTryPollAndExecuteCommandAsync(fixture.Service);

            didWork.Should().BeTrue();
            hubConnection.ReportedCommandResults.Should().Contain(result =>
                result.CommandId == "cmd-unsupported" &&
                result.Status == StationCommandStatus.Failed &&
                result.ErrorCode == "NotSupported" &&
                result.Message != null &&
                result.Message.Contains("当前 Station 版本不支持命令") &&
                !result.Message.Contains("is not supported by this Station build", StringComparison.Ordinal));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static StationSyncHostedServiceFixture CreateFixture(
        string root,
        int outboundQueueCapacity,
        ICameraManager? cameraManager = null,
        bool syncEnabled = true,
        Func<string, string, IStationHubConnection>? connectionFactory = null)
    {
        if (cameraManager == null)
        {
            cameraManager = Substitute.For<ICameraManager>();
            cameraManager.GetBindings().Returns(new List<CameraBindingConfig>());
        }

        var options = Options.Create(new StationSyncOptions
        {
            Enabled = syncEnabled,
            StudioBaseUrl = "http://127.0.0.1:5000",
            SharedToken = "station-secret",
            OutboundQueueCapacity = outboundQueueCapacity,
            SpoolDirectoryPath = Path.Combine(root, "spool"),
            PackageDirectory = Path.Combine(root, "packages"),
            LogDirectory = Path.Combine(root, "logs")
        });

        var runtimeHost = new RuntimeHost(
            Substitute.For<IFlowExecutionService>(),
            new RuntimePackageLoader(new RuntimePackageValidator(), NullLogger<RuntimePackageLoader>.Instance),
            new RuntimeResultNormalizer(),
            NullLogger<RuntimeHost>.Instance);
        var settingsStore = new StationLocalSettingsStore(Path.Combine(root, "settings"));
        settingsStore.UpdateStationIdentity("station-sync-test", "line-a");
        var siteProfileStore = new StationSiteProfileStore(Path.Combine(root, "profiles"));
        var identityResolver = new StationIdentityResolver(settingsStore);
        var hubClient = new StationHubClient(options, NullLogger<StationHubClient>.Instance, connectionFactory);
        var spoolStore = new StationSpoolStore(options, NullLogger<StationSpoolStore>.Instance);
        var commandResultSpoolStore = new StationCommandResultSpoolStore(options, NullLogger<StationCommandResultSpoolStore>.Instance);
        var commandExecutionJournalStore = new StationCommandExecutionJournalStore(options, NullLogger<StationCommandExecutionJournalStore>.Instance);
        var syncSettingsStore = new StationSyncSettingsStore(Path.Combine(root, "station-sync.json"), options.Value);

        var service = new StationSyncHostedService(
            runtimeHost,
            identityResolver,
            spoolStore,
            commandResultSpoolStore,
            commandExecutionJournalStore,
            hubClient,
            new StationPackageDeploymentService(
                options,
                runtimeHost,
                settingsStore,
                siteProfileStore,
                NullLogger<StationPackageDeploymentService>.Instance),
            new StationLogRelayService(identityResolver, settingsStore, options),
            settingsStore,
            siteProfileStore,
            syncSettingsStore,
            cameraManager,
            options,
            NullLogger<StationSyncHostedService>.Instance);

        return new StationSyncHostedServiceFixture(
            service,
            runtimeHost,
            hubClient,
            identityResolver,
            spoolStore,
            commandResultSpoolStore,
            commandExecutionJournalStore);
    }

    private static void InvokeResultAvailable(StationSyncHostedService service, RuntimeNormalizedResult result)
    {
        var method = typeof(StationSyncHostedService).GetMethod(
            "HandleResultAvailable",
            BindingFlags.Instance | BindingFlags.NonPublic);
        method.Should().NotBeNull();
        method!.Invoke(service, [result]);
    }

    private static StationHealthSnapshotDto InvokeBuildHealth(StationSyncHostedServiceFixture fixture)
    {
        var method = typeof(StationSyncHostedService).GetMethod(
            "BuildHealthDto",
            BindingFlags.Instance | BindingFlags.NonPublic);
        method.Should().NotBeNull();
        return (StationHealthSnapshotDto)method!.Invoke(
            fixture.Service,
            [fixture.IdentityResolver.GetOrCreate(), fixture.RuntimeHost.GetSnapshot(), fixture.SpoolStore])!;
    }

    private static StationHeartbeatDto InvokeBuildHeartbeat(
        StationIdentityContext identity,
        RuntimeHostSnapshot snapshot)
    {
        var method = typeof(StationSyncHostedService).GetMethod(
            "BuildHeartbeatDto",
            BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();
        return (StationHeartbeatDto)method!.Invoke(null, [identity, snapshot])!;
    }

    private static string InvokeBuildPlcStatusSummaryCore(
        RuntimePackage package,
        RuntimeHostSnapshot snapshot,
        IReadOnlyDictionary<string, bool> industrialConnectionStates,
        IReadOnlyDictionary<string, bool> modbusConnectionStates)
    {
        var method = typeof(StationSyncHostedService).GetMethod(
            "BuildPlcStatusSummaryCore",
            BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();
        return (string)method!.Invoke(null, [package, snapshot, industrialConnectionStates, modbusConnectionStates])!;
    }

    private static async Task<bool> InvokeTryReplayCompletedCommandAsync(
        StationSyncHostedService service,
        StationCommandDto command)
    {
        var method = typeof(StationSyncHostedService).GetMethod(
            "TryReplayCompletedCommandAsync",
            BindingFlags.Instance | BindingFlags.NonPublic);
        method.Should().NotBeNull();
        return await (Task<bool>)method!.Invoke(service, [command, CancellationToken.None])!;
    }

    private static async Task<bool> InvokeTryPollAndExecuteCommandAsync(StationSyncHostedService service)
    {
        var method = typeof(StationSyncHostedService).GetMethod(
            "TryPollAndExecuteCommandAsync",
            BindingFlags.Instance | BindingFlags.NonPublic);
        method.Should().NotBeNull();
        return await (Task<bool>)method!.Invoke(service, [CancellationToken.None])!;
    }

    private static async Task InvokeReportCommandAsync(
        StationSyncHostedService service,
        StationCommandDto command,
        StationCommandStatus status,
        int progress,
        string message)
    {
        var method = typeof(StationSyncHostedService).GetMethod(
            "ReportCommandAsync",
            BindingFlags.Instance | BindingFlags.NonPublic);
        method.Should().NotBeNull();
        await (Task)method!.Invoke(
            service,
            [command, status, progress, message, CancellationToken.None, null, null, true])!;
    }

    private static long ReadPrivateLong(object instance, string fieldName)
    {
        var field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        field.Should().NotBeNull();
        return (long)field!.GetValue(instance)!;
    }

    private static void SetPrivateStringField(object instance, string fieldName, string value)
    {
        var field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        field.Should().NotBeNull();
        field!.SetValue(instance, value);
    }

    private static RuntimeNormalizedResult BuildResult(string runId)
    {
        var now = DateTimeOffset.UtcNow;
        return new RuntimeNormalizedResult
        {
            RunId = runId,
            PackageId = "pkg-1",
            PackageName = "Package 1",
            FlowHash = "sha256:abc",
            ImageId = $"image-{runId}",
            Outcome = RuntimeRunOutcome.Ok,
            InspectionStatus = InspectionStatus.OK,
            ExecutionTimeMs = 12,
            DiagnosticCode = "OK",
            DiagnosticMessage = "accepted",
            HasJudgmentSignal = true,
            StartedAtUtc = now.AddMilliseconds(-12),
            CompletedAtUtc = now,
            PrimaryOutputs = new Dictionary<string, object?>
            {
                ["Result"] = "OK"
            }
        };
    }

    private static StationCommandDto BuildCommand(string commandId, StationCommandType commandType, string payloadJson)
    {
        return new StationCommandDto
        {
            CommandId = commandId,
            StationId = "station-sync-test",
            CommandType = commandType,
            PayloadJson = payloadJson,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(5),
            IssuedBy = "unit-test"
        };
    }

    private static async Task<RuntimePackageExportResult> ExportCameraRuntimePackageAsync(string root, string cameraId)
    {
        return await ExportRuntimePackageAsync(
            root,
            "camera-health",
            new OperatorDto
            {
                Id = Guid.NewGuid(),
                Name = "Image acquisition",
                Type = OperatorType.ImageAcquisition,
                Parameters =
                [
                    CreateParameter("SourceType", "enum", "Camera"),
                    CreateParameter("CameraId", "cameraBinding", cameraId)
                ],
                ExecutionStatus = OperatorExecutionStatus.NotExecuted
            });
    }

    private static async Task<RuntimePackageExportResult> ExportPlcRuntimePackageAsync(string root)
    {
        return await ExportRuntimePackageAsync(
            root,
            "plc-health",
            new OperatorDto
            {
                Id = Guid.NewGuid(),
                Name = "S7 trigger read",
                Type = OperatorType.SiemensS7Communication,
                Parameters =
                [
                    CreateParameter("IpAddress", "string", "192.168.10.25"),
                    CreateParameter("Port", "int", 102),
                    CreateParameter("CpuType", "enum", "S71200"),
                    CreateParameter("Rack", "int", 0),
                    CreateParameter("Slot", "int", 1)
                ],
                ExecutionStatus = OperatorExecutionStatus.NotExecuted
            });
    }

    private static async Task<RuntimePackageExportResult> ExportRuntimePackageAsync(
        string root,
        string projectName,
        params OperatorDto[] operators)
    {
        var decisionOperatorId = Guid.NewGuid();
        var decisionPortId = Guid.NewGuid();
        var packagedOperators = operators.ToList();
        packagedOperators.Add(new OperatorDto
        {
            Id = decisionOperatorId,
            Name = "Station test decision",
            Type = OperatorType.ResultJudgment,
            OutputPorts =
            [
                new PortDto
                {
                    Id = decisionPortId,
                    Name = "IsOk",
                    Direction = PortDirection.Output,
                    DataType = PortDataType.Boolean
                }
            ],
            ExecutionStatus = OperatorExecutionStatus.NotExecuted
        });
        var exporter = new RuntimePackageExporter(
            new OperatorFactory(),
            NullLogger<RuntimePackageExporter>.Instance);

        return await exporter.ExportAsync(new RuntimePackageExportRequest
        {
            TargetRootDirectory = Path.Combine(root, "exports"),
            Project = new ProjectDto
            {
                Id = Guid.NewGuid(),
                Name = projectName,
                Flow = new OperatorFlowDto
                {
                    Id = Guid.NewGuid(),
                    Name = "main",
                    DecisionConfiguration = new DecisionConfiguration
                    {
                        FinalDecisionBinding = new FinalDecisionBinding
                        {
                            SourceOperatorId = decisionOperatorId,
                            SourceOutputPortId = decisionPortId,
                            SourceOutputName = "IsOk",
                            DataType = DecisionValueType.Boolean,
                            Rule = DecisionInterpretationRule.Boolean,
                            TrueMeansOk = true
                        }
                    },
                    Operators = packagedOperators
                }
            }
        });
    }

    private static ParameterDto CreateParameter(string name, string dataType, object? value)
    {
        return new ParameterDto
        {
            Id = Guid.NewGuid(),
            Name = name,
            DisplayName = name,
            DataType = dataType,
            Value = value
        };
    }

    private sealed record StationSyncHostedServiceFixture(
        StationSyncHostedService Service,
        RuntimeHost RuntimeHost,
        StationHubClient HubClient,
        StationIdentityResolver IdentityResolver,
        StationSpoolStore SpoolStore,
        StationCommandResultSpoolStore CommandResultSpoolStore,
        StationCommandExecutionJournalStore CommandExecutionJournalStore) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await HubClient.DisposeAsync();
            await RuntimeHost.DisposeAsync();
        }
    }

    private sealed class RecordingStationHubConnection : IStationHubConnection
    {
        public List<StationCommandResultDto> ReportedCommandResults { get; } = [];

        public bool FailInvocations { get; init; }

        public StationCommandDto? NextCommand { get; init; }

        public HubConnectionState State { get; private set; } = HubConnectionState.Disconnected;

        public event Func<Exception?, Task>? Closed
        {
            add { }
            remove { }
        }

        public event Func<Exception?, Task>? Reconnecting
        {
            add { }
            remove { }
        }

        public event Func<string?, Task>? Reconnected
        {
            add { }
            remove { }
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            State = HubConnectionState.Connected;
            return Task.CompletedTask;
        }

        public Task<T> InvokeAsync<T>(string methodName, object payload, CancellationToken cancellationToken)
        {
            if (methodName == StationHubMethods.PollCommand &&
                typeof(T) == typeof(StationCommandDto))
            {
                return Task.FromResult((T)(object)NextCommand!);
            }

            return Task.FromResult(default(T)!);
        }

        public Task InvokeAsync(string methodName, object payload, CancellationToken cancellationToken)
        {
            if (FailInvocations)
            {
                throw new InvalidOperationException("synthetic remote failure");
            }

            if (payload is StationCommandResultDto commandResult)
            {
                ReportedCommandResults.Add(commandResult);
            }

            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            State = HubConnectionState.Disconnected;
            return ValueTask.CompletedTask;
        }
    }
}
