using ClearVision.Product.Core.Outcomes;
using ClearVision.Product.Runtime.Abstractions;
using Microsoft.AspNetCore.SignalR.Client;

namespace ClearVision.Product.Station.Simulator;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        var options = SimulatorOptions.Parse(args);
        using var cancellation = new CancellationTokenSource(options.Duration);
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        };

        Console.WriteLine($"Studio hub: {options.StudioHubUrl}");
        Console.WriteLine($"Stations: {options.StationCount}, rate: {options.ResultsPerSecondPerStation}/s/station");

        var tasks = Enumerable.Range(1, options.StationCount)
            .Select(index => RunStationAsync(options, index, cancellation.Token))
            .ToArray();

        try
        {
            await Task.WhenAll(tasks);
            return 0;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            return 0;
        }
    }

    private static async Task RunStationAsync(SimulatorOptions options, int index, CancellationToken cancellationToken)
    {
        var random = new Random(HashCode.Combine(Environment.TickCount, index));
        var stationId = $"{options.StationPrefix}-{index:000}";
        var connection = BuildConnection(options);
        var resultSequenceId = 0L;
        var controlSequenceId = 0L;
        var okCount = 0;
        var ngCount = 0;
        var failedCount = 0;
        var lastHeartbeatAtUtc = DateTimeOffset.MinValue;
        var lastHealthAtUtc = DateTimeOffset.MinValue;
        var lastCommandPollAtUtc = DateTimeOffset.MinValue;
        var startedAtUtc = DateTimeOffset.UtcNow;

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (connection.State != HubConnectionState.Connected)
                {
                    await connection.StartAsync(cancellationToken);
                    await RegisterAsync(connection, stationId, index, startedAtUtc, cancellationToken);
                    Console.WriteLine($"{stationId} connected.");
                }

                var now = DateTimeOffset.UtcNow;
                if (now - lastHeartbeatAtUtc >= TimeSpan.FromSeconds(options.HeartbeatSeconds))
                {
                    await connection.InvokeAsync<StationAckDto>(
                        StationHubMethods.Heartbeat,
                        BuildHeartbeat(stationId, index, Interlocked.Increment(ref controlSequenceId), okCount, ngCount, failedCount),
                        cancellationToken);
                    lastHeartbeatAtUtc = now;
                }

                if (now - lastHealthAtUtc >= TimeSpan.FromSeconds(options.HealthSeconds))
                {
                    await connection.InvokeAsync<StationAckDto>(
                        StationHubMethods.PushHealth,
                        BuildHealth(stationId, Interlocked.Increment(ref controlSequenceId), startedAtUtc, random),
                        cancellationToken);
                    lastHealthAtUtc = now;
                }

                if (now - lastCommandPollAtUtc >= TimeSpan.FromSeconds(1))
                {
                    await PollCommandAsync(connection, stationId, cancellationToken);
                    lastCommandPollAtUtc = now;
                }

                if (random.NextDouble() < options.LogProbability)
                {
                    await connection.InvokeAsync<StationAckDto>(
                        StationHubMethods.PushLog,
                        BuildLog(stationId, Interlocked.Increment(ref controlSequenceId), random),
                        cancellationToken);
                }

                var result = BuildResult(
                    stationId,
                    index,
                    Interlocked.Increment(ref resultSequenceId),
                    random,
                    options.NgProbability,
                    options.ErrorProbability);
                switch (result.DecisionOutcome)
                {
                    case DecisionOutcome.Ok:
                        okCount++;
                        break;
                    case DecisionOutcome.Ng:
                        ngCount++;
                        break;
                    default:
                        failedCount++;
                        break;
                }

                await connection.InvokeAsync<StationAckDto>(StationHubMethods.PushResult, result, cancellationToken);

                if (random.NextDouble() < options.DisconnectProbability)
                {
                    await connection.StopAsync(cancellationToken);
                    await Task.Delay(TimeSpan.FromSeconds(random.Next(2, 8)), cancellationToken);
                }

                await Task.Delay(options.ResultDelay, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"{stationId} sync error: {ex.Message}");
                try
                {
                    await connection.StopAsync(CancellationToken.None);
                }
                catch
                {
                }

                await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);
            }
        }

        await connection.DisposeAsync();
    }

    private static HubConnection BuildConnection(SimulatorOptions options)
    {
        return new HubConnectionBuilder()
            .WithUrl(options.StudioHubUrl, hubOptions =>
            {
                if (!string.IsNullOrWhiteSpace(options.SharedToken))
                {
                    hubOptions.Headers[StationSyncContractDefaults.StationTokenHeaderName] = options.SharedToken;
                    hubOptions.Headers["X-Station-Token"] = options.SharedToken;
                    hubOptions.AccessTokenProvider = () => Task.FromResult<string?>(options.SharedToken);
                }
            })
            .WithAutomaticReconnect()
            .Build();
    }

    private static Task<StationRegisterAckDto> RegisterAsync(
        HubConnection connection,
        string stationId,
        int index,
        DateTimeOffset startedAtUtc,
        CancellationToken cancellationToken)
    {
        return connection.InvokeAsync<StationRegisterAckDto>(
            StationHubMethods.RegisterStation,
            new StationRegistrationDto
            {
                StationId = stationId,
                StationName = $"Simulator {index:000}",
                LineName = $"Line-{((index - 1) / 8) + 1}",
                AreaName = "Simulation",
                WorkcellName = $"Cell-{((index - 1) % 8) + 1}",
                InspectionNodeName = "sim-inspection",
                CameraAlias = "sim-camera",
                StationRole = "Simulator",
                Owner = "Studio",
                MachineName = Environment.MachineName,
                ProcessId = Environment.ProcessId,
                StationVersion = "sim-0.1.0",
                RuntimeVersion = "sim-0.1.0",
                CurrentPackageId = "sim-package",
                CurrentPackageName = "Simulator Package",
                CurrentPackageVersion = "0.1.0",
                StartedAtUtc = startedAtUtc,
                RegisteredAtUtc = DateTimeOffset.UtcNow,
                CreatedAtUtc = DateTimeOffset.UtcNow
            },
            cancellationToken);
    }

    private static StationHeartbeatDto BuildHeartbeat(
        string stationId,
        int index,
        long sequenceId,
        int okCount,
        int ngCount,
        int failedCount)
    {
        return new StationHeartbeatDto
        {
            StationId = stationId,
            SequenceId = sequenceId,
            MessageId = $"heartbeat_{stationId}_{sequenceId}_{Guid.NewGuid():N}",
            LineName = $"Line-{((index - 1) / 8) + 1}",
            RuntimeState = StationRuntimeState.Running,
            ConnectionState = "Connected",
            CurrentPackageId = "sim-package",
            CurrentPackageName = "Simulator Package",
            CurrentPackageVersion = "0.1.0",
            FlowHash = "sim-flow",
            CurrentRunId = $"sim-run-{sequenceId}",
            SessionOkCount = okCount,
            SessionNgCount = ngCount,
            SessionErrorCount = failedCount,
            SessionOutcomeStatistics = new InspectionOutcomeStatistics
            {
                TotalAttemptCount = okCount + ngCount + failedCount,
                ExecutionSucceededCount = okCount + ngCount,
                ValidDecisionCount = okCount + ngCount,
                OkCount = okCount,
                NgCount = ngCount,
                FailedCount = failedCount
            },
            StationLocalOffsetMinutes = (int)TimeZoneInfo.Local.GetUtcOffset(DateTimeOffset.Now).TotalMinutes,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
    }

    private static StationHealthSnapshotDto BuildHealth(
        string stationId,
        long sequenceId,
        DateTimeOffset startedAtUtc,
        Random random)
    {
        return new StationHealthSnapshotDto
        {
            StationId = stationId,
            SequenceId = sequenceId,
            MessageId = $"health_{stationId}_{sequenceId}_{Guid.NewGuid():N}",
            RuntimeState = StationRuntimeState.Running,
            ProcessUptimeSeconds = (long)(DateTimeOffset.UtcNow - startedAtUtc).TotalSeconds,
            CpuUsagePercent = Math.Round(random.NextDouble() * 55, 2),
            WorkingSetMb = random.Next(120, 640),
            PrivateMemoryMb = random.Next(180, 960),
            DiskFreeMb = random.Next(20_000, 200_000),
            DiskTotalMb = 256_000,
            SpoolPendingCount = random.Next(0, 5),
            SpoolBytes = random.Next(0, 4096),
            CameraStatusSummary = "Connected",
            PlcStatusSummary = "Connected",
            CurrentPackageId = "sim-package",
            CurrentPackageHealth = "Loaded",
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
    }

    private static StationResultSummaryDto BuildResult(
        string stationId,
        int index,
        long sequenceId,
        Random random,
        double ngProbability,
        double errorProbability)
    {
        var startedAtUtc = DateTimeOffset.UtcNow.AddMilliseconds(-random.Next(40, 240));
        var roll = random.NextDouble();
        var outcome = roll < errorProbability
            ? RuntimeRunOutcome.Error
            : roll < errorProbability + ngProbability
                ? RuntimeRunOutcome.Ng
                : RuntimeRunOutcome.Ok;
        var executionOutcome = outcome == RuntimeRunOutcome.Error
            ? ExecutionOutcome.Failed
            : ExecutionOutcome.Succeeded;
        var decisionOutcome = outcome switch
        {
            RuntimeRunOutcome.Ok => DecisionOutcome.Ok,
            RuntimeRunOutcome.Ng => DecisionOutcome.Ng,
            _ => DecisionOutcome.Undetermined
        };
        return new StationResultSummaryDto
        {
            StationId = stationId,
            LineName = $"Line-{((index - 1) / 8) + 1}",
            SequenceId = sequenceId,
            MessageId = $"result_{stationId}_{sequenceId}_{Guid.NewGuid():N}",
            RunId = $"run-{stationId}-{sequenceId}",
            PackageId = "sim-package",
            PackageName = "Simulator Package",
            PackageVersion = "0.1.0",
            FlowHash = "sim-flow",
            ImageId = $"frame-{sequenceId:000000}",
            Outcome = outcome,
            ExecutionOutcome = executionOutcome,
            DecisionOutcome = decisionOutcome,
            HasJudgmentSignal = decisionOutcome is DecisionOutcome.Ok or DecisionOutcome.Ng,
            DecisionSource = "Simulator",
            ReasonCode = outcome == RuntimeRunOutcome.Ok ? "SIM_OK" : outcome == RuntimeRunOutcome.Ng ? "SIM_NG" : "SIM_FAILED",
            ExecutionTimeMs = random.Next(40, 240),
            DiagnosticCode = outcome == RuntimeRunOutcome.Ok ? "OK" : outcome == RuntimeRunOutcome.Ng ? "SIM_NG" : "SIM_ERROR",
            DiagnosticMessage = outcome == RuntimeRunOutcome.Ok ? null : "Simulator generated condition.",
            PrimaryOutputsPreview = new Dictionary<string, string?>
            {
                ["score"] = Math.Round(random.NextDouble(), 4).ToString("0.0000"),
                ["slot"] = random.Next(1, 12).ToString()
            },
            StartedAtUtc = startedAtUtc,
            CompletedAtUtc = DateTimeOffset.UtcNow,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
    }

    private static StationLogSummaryDto BuildLog(string stationId, long sequenceId, Random random)
    {
        var isError = random.NextDouble() < 0.35;
        return new StationLogSummaryDto
        {
            StationId = stationId,
            SequenceId = sequenceId,
            MessageId = $"log_{stationId}_{sequenceId}_{Guid.NewGuid():N}",
            TimestampUtc = DateTimeOffset.UtcNow,
            Level = isError ? "ERROR" : "WARN",
            Source = "Simulator",
            RenderedMessage = isError ? "Simulated inspection warning escalated to error." : "Simulated threshold warning.",
            ExceptionType = isError ? "SimulatorException" : null,
            ExceptionMessage = isError ? "Synthetic error for monitoring validation." : null,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
    }

    private static async Task PollCommandAsync(HubConnection connection, string stationId, CancellationToken cancellationToken)
    {
        var command = await connection.InvokeAsync<StationCommandDto?>(
            StationHubMethods.PollCommand,
            stationId,
            cancellationToken);
        if (command == null)
        {
            return;
        }

        await ReportCommandAsync(connection, command, StationCommandStatus.Accepted, 0, "Accepted", cancellationToken);
        await ReportCommandAsync(connection, command, StationCommandStatus.Running, 50, "Running", cancellationToken);
        await Task.Delay(TimeSpan.FromMilliseconds(150), cancellationToken);
        await ReportCommandAsync(connection, command, StationCommandStatus.Succeeded, 100, $"{command.CommandType} simulated.", cancellationToken);
    }

    private static Task ReportCommandAsync(
        HubConnection connection,
        StationCommandDto command,
        StationCommandStatus status,
        int progress,
        string message,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        return connection.InvokeAsync(
            StationHubMethods.ReportCommandResult,
            new StationCommandResultDto
            {
                CommandId = command.CommandId,
                StationId = command.StationId,
                Status = status,
                ProgressPercent = progress,
                Message = message,
                StartedAtUtc = status is StationCommandStatus.Running or StationCommandStatus.Succeeded ? now : null,
                CompletedAtUtc = status == StationCommandStatus.Succeeded ? now : null,
                ReportedAtUtc = now,
                CreatedAtUtc = now
            },
            cancellationToken);
    }

    private sealed class SimulatorOptions
    {
        public Uri StudioHubUrl { get; private init; } = new("http://127.0.0.1:5000" + StationSyncContractDefaults.HubPath);

        public string SharedToken { get; private init; } = string.Empty;

        public string StationPrefix { get; private init; } = "sim-station";

        public int StationCount { get; private init; } = 4;

        public double ResultsPerSecondPerStation { get; private init; } = 1;

        public double NgProbability { get; private init; } = 0.08;

        public double ErrorProbability { get; private init; } = 0.01;

        public double LogProbability { get; private init; } = 0.02;

        public double DisconnectProbability { get; private init; }

        public int HeartbeatSeconds { get; private init; } = 5;

        public int HealthSeconds { get; private init; } = 5;

        public TimeSpan Duration { get; private init; } = TimeSpan.FromMinutes(10);

        public TimeSpan ResultDelay => TimeSpan.FromMilliseconds(Math.Max(50, 1000 / Math.Max(0.1, ResultsPerSecondPerStation)));

        public static SimulatorOptions Parse(string[] args)
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < args.Length; i++)
            {
                var key = args[i];
                if (!key.StartsWith("--", StringComparison.Ordinal))
                {
                    continue;
                }

                var value = i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal)
                    ? args[++i]
                    : "true";
                values[key[2..]] = value;
            }

            return new SimulatorOptions
            {
                StudioHubUrl = NormalizeHubUrl(Get(values, "studio", "http://127.0.0.1:5000" + StationSyncContractDefaults.HubPath)),
                SharedToken = Get(values, "token", string.Empty),
                StationPrefix = Get(values, "prefix", "sim-station"),
                StationCount = GetInt(values, "stations", 4),
                ResultsPerSecondPerStation = GetDouble(values, "rate", 1),
                NgProbability = GetDouble(values, "ng-rate", 0.08),
                ErrorProbability = GetDouble(values, "error-rate", 0.01),
                LogProbability = GetDouble(values, "log-rate", 0.02),
                DisconnectProbability = GetDouble(values, "disconnect-rate", 0),
                HeartbeatSeconds = GetInt(values, "heartbeat", 5),
                HealthSeconds = GetInt(values, "health", 5),
                Duration = TimeSpan.FromSeconds(GetInt(values, "duration", 600))
            };
        }

        private static Uri NormalizeHubUrl(string value)
        {
            var uri = new Uri(value, UriKind.Absolute);
            if (uri.AbsolutePath.Contains(StationSyncContractDefaults.HubPath, StringComparison.OrdinalIgnoreCase))
            {
                return uri;
            }

            return new Uri($"{uri.Scheme}://{uri.Authority}{StationSyncContractDefaults.HubPath}");
        }

        private static string Get(IReadOnlyDictionary<string, string> values, string key, string fallback)
        {
            return values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
                ? value
                : fallback;
        }

        private static int GetInt(IReadOnlyDictionary<string, string> values, string key, int fallback)
        {
            return values.TryGetValue(key, out var value) && int.TryParse(value, out var parsed)
                ? Math.Max(1, parsed)
                : fallback;
        }

        private static double GetDouble(IReadOnlyDictionary<string, string> values, string key, double fallback)
        {
            return values.TryGetValue(key, out var value) && double.TryParse(value, out var parsed)
                ? Math.Clamp(parsed, 0, 1_000)
                : fallback;
        }
    }
}
