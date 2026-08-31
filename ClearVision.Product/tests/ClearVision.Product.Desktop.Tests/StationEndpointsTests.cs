using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Outcomes;
using ClearVision.Product.Desktop.Endpoints;
using ClearVision.Product.Desktop.Middleware;
using ClearVision.Product.Desktop.Station;
using ClearVision.Product.Infrastructure.Data;
using ClearVision.Product.Runtime.Abstractions;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ClearVision.Product.Desktop.Tests;

[TestClassification(TestDomain.Desktop, TestPurpose.Regression, TestLane.Pr, TestEvidenceType.Contract, TestOracleType.Contract, TestResourceRequirement.None, TestExpectedDuration.Fast, TestFlakyPolicy.Blocking, "desktop", Suites = "DesktopEndpoints")]

public sealed class StationEndpointsTests
{
    private const string StationSharedToken = "station-secret";

    private static readonly string[] SafeStationPropertyNames =
    [
        "stationId",
        "stationName",
        "lineName",
        "onlineState",
        "state",
        "runtimeState",
        "isOnline",
        "lastSeenAtUtc",
        "sessionOutcomeStatistics",
        "lastOutcome",
        "lastInspectionStatus",
        "lastExecutionOutcome",
        "lastDecisionOutcome",
        "lastHasJudgmentSignal",
        "lastResultAtUtc",
        "averageExecutionTimeMs",
        "recentResultCount"
    ];

    private static readonly string[] SafeResultPropertyNames =
    [
        "stationId",
        "lineName",
        "sequenceId",
        "outcome",
        "inspectionStatus",
        "executionOutcome",
        "decisionOutcome",
        "hasJudgmentSignal",
        "reasonCode",
        "executionTimeMs",
        "diagnosticCode",
        "startedAtUtc",
        "completedAtUtc",
        "createdAtUtc"
    ];

    private static readonly string[] SafeHealthPropertyNames =
    [
        "stationId",
        "sequenceId",
        "runtimeState",
        "healthState",
        "createdAtUtc"
    ];

    private static readonly string[] SensitiveStationPropertyNames =
    [
        "machineName",
        "clientVersion",
        "packageId",
        "packageName",
        "packageFlowHash",
        "executionFlowHash",
        "flowHash",
        "executionSnapshotId",
        "projectRevision",
        "decisionConfigurationHash",
        "executionRunMode",
        "currentRunId",
        "spoolPendingCount",
        "spoolBytes",
        "cpuUsagePercent",
        "workingSetMb",
        "diskFreeMb",
        "diskTotalMb",
        "cameraStatusSummary",
        "plcStatusSummary",
        "currentPackageHealth",
        "lastDiagnosticMessage",
        "recentLogs",
        "recentCommands"
    ];

    private static readonly JsonSerializerOptions WebJsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task EventsEndpoint_ShouldStreamInitialSnapshotAndLiveStationUpdates()
    {
        await using var host = await StationEndpointTestHost.CreateAsync();
        host.Registry.UpsertRegistration("conn-1", new StationRegistrationDto
        {
            StationId = "station-a",
            LineName = "line-1",
            MachineName = "machine-a",
            ClientVersion = "1.0.0",
            StartedAtUtc = DateTimeOffset.UtcNow
        });

        using var response = await host.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/api/stations/events"),
            HttpCompletionOption.ResponseHeadersRead);
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        await using var stream = await response.Content.ReadAsStreamAsync();

        var initialChunk = await ReadUntilContainsAsync(stream, "event: initialState", TimeSpan.FromSeconds(2));
        initialChunk.Should().Contain("event: initialState");
        initialChunk.Should().Contain("\"stationId\":\"station-a\"");

        host.Registry.UpsertHeartbeat("conn-1", new StationHeartbeatDto
        {
            StationId = "station-a",
            LineName = "line-1",
            State = RuntimeHostState.Running,
            PackageId = "pkg-1",
            PackageName = "Package 1",
            FlowHash = "sha256:abc",
            CurrentRunId = "run-1",
            SessionOkCount = 12,
            SessionNgCount = 1,
            SessionErrorCount = 0
        });

        var liveChunk = await ReadUntilContainsAsync(stream, "\"state\":\"Running\"", TimeSpan.FromSeconds(2));
        liveChunk.Should().Contain("event: stationUpserted");
        liveChunk.Should().Contain("\"state\":\"Running\"");
        liveChunk.Should().Contain("\"stationId\":\"station-a\"");
    }

    [Fact]
    public async Task EventsEndpoint_ShouldReplayStoredEventsAfterLastEventId()
    {
        await using var host = await StationEndpointTestHost.CreateAsync();
        host.Registry.UpsertRegistration("conn-2", new StationRegistrationDto
        {
            StationId = "station-b",
            MachineName = "machine-b",
            ClientVersion = "1.0.0",
            StartedAtUtc = DateTimeOffset.UtcNow
        });

        var checkpoint = host.Registry.GetEventsAfter(0).Max(evt => evt.SequenceId);

        host.Registry.UpsertResultSummary("conn-2", new StationResultSummaryDto
        {
            StationId = "station-b",
            SequenceId = 9,
            RunId = "run-9",
            PackageId = "pkg-9",
            PackageName = "Package 9",
            FlowHash = "sha256:def",
            ImageId = "image-9",
            Outcome = RuntimeRunOutcome.Ok,
            ExecutionOutcome = ExecutionOutcome.Succeeded,
            DecisionOutcome = DecisionOutcome.Invalid,
            HasJudgmentSignal = false,
            DecisionSource = "FinalDecisionBinding:judge:Judgment",
            ReasonCode = "DecisionValueInvalid",
            ExecutionTimeMs = 18,
            DiagnosticCode = "OK",
            StartedAtUtc = DateTimeOffset.UtcNow.AddMilliseconds(-18),
            CompletedAtUtc = DateTimeOffset.UtcNow
        });

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/stations/events");
        request.Headers.Add("Last-Event-ID", checkpoint.ToString());

        using var response = await host.Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        await using var stream = await response.Content.ReadAsStreamAsync();

        var replayChunk = await ReadUntilContainsAsync(stream, "event: stationResultAdded", TimeSpan.FromSeconds(2));
        replayChunk.Should().Contain($"id: {checkpoint + 1}");
        replayChunk.Should().Contain("\"stationId\":\"station-b\"");
        replayChunk.Should().Contain("\"diagnosticCode\":\"OK\"");
        replayChunk.Should().Contain("\"executionOutcome\":\"Succeeded\"");
        replayChunk.Should().Contain("\"decisionOutcome\":\"Invalid\"");
        replayChunk.Should().Contain("\"reasonCode\":\"DecisionValueInvalid\"");
    }

    [Theory]
    [InlineData("lastEventId")]
    [InlineData("afterSequence")]
    public async Task EventsEndpoint_ShouldReplayStoredEventsAfterQueryCursor(string cursorName)
    {
        await using var host = await StationEndpointTestHost.CreateAsync();
        host.Registry.UpsertRegistration("conn-1", BuildRegistration("station-a"));
        var checkpoint = host.Registry.GetEventsAfter(0).Max(evt => evt.SequenceId);

        host.Registry.UpsertResultSummary("conn-1", BuildResult("station-a", 7, RuntimeRunOutcome.Ng, "WIRE_SWAP", -1));

        using var response = await host.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, $"/api/stations/events?{cursorName}={checkpoint}"),
            HttpCompletionOption.ResponseHeadersRead);
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        await using var stream = await response.Content.ReadAsStreamAsync();

        var replayChunk = await ReadUntilContainsAsync(stream, "event: stationResultAdded", TimeSpan.FromSeconds(2));
        replayChunk.Should().Contain($"id: {checkpoint + 1}");
        replayChunk.Should().Contain("\"stationId\":\"station-a\"");
        replayChunk.Should().Contain("\"diagnosticCode\":\"WIRE_SWAP\"");
    }

    [Fact]
    public async Task EventsEndpoint_ShouldUseTheSameSafeProjectionForInitialLiveAndReplay()
    {
        await using var host = await StationEndpointTestHost.CreateWithCentralStoreAsync(role: "Operator");
        SeedSensitiveStationTelemetry(host.Registry, "conn-safe-sse", "station-safe-sse");

        using (var liveResponse = await host.Client.SendAsync(
                   new HttpRequestMessage(HttpMethod.Get, "/api/stations/events"),
                   HttpCompletionOption.ResponseHeadersRead))
        {
            await using var liveStream = await liveResponse.Content.ReadAsStreamAsync();
            var initial = await ReadSseEventDataAsync(liveStream, "initialState", TimeSpan.FromSeconds(2));
            AssertExactProperties(initial, ["summary", "stations", "recentResults"]);
            AssertExactProperties(initial.GetProperty("stations")[0], SafeStationPropertyNames);
            AssertExactProperties(initial.GetProperty("recentResults")[0], ["stationId", "result", "station"]);
            AssertExactProperties(initial.GetProperty("recentResults")[0].GetProperty("result"), SafeResultPropertyNames);
            AssertExactProperties(initial.GetProperty("recentResults")[0].GetProperty("station"), SafeStationPropertyNames);

            host.Registry.UpsertResultSummary("conn-safe-sse", BuildSensitiveResult("station-safe-sse", 2));
            var liveResult = await ReadSseEventDataAsync(liveStream, "stationResultAdded", TimeSpan.FromSeconds(2));
            AssertExactProperties(liveResult, ["stationId", "result", "station"]);
            AssertExactProperties(liveResult.GetProperty("result"), SafeResultPropertyNames);
            AssertExactProperties(liveResult.GetProperty("station"), SafeStationPropertyNames);

            host.Registry.UpsertLogSummary("conn-safe-sse", new StationLogSummaryDto
            {
                StationId = "station-safe-sse",
                SequenceId = 2,
                MessageId = "log-live-sensitive",
                TimestampUtc = DateTimeOffset.UtcNow,
                Level = "ERROR",
                RenderedMessage = "live-log-sensitive",
                ExceptionMessage = "live-exception-sensitive",
                CorrelationId = "live-correlation-sensitive"
            });
            host.Registry.UpsertHealthSnapshot("conn-safe-sse", new StationHealthSnapshotDto
            {
                StationId = "station-safe-sse",
                SequenceId = 2,
                MessageId = "health-live-sensitive",
                RuntimeState = StationRuntimeState.Running,
                CpuUsagePercent = 99,
                CameraStatusSummary = "live-camera-sensitive",
                PlcStatusSummary = "live-plc-sensitive",
                LastErrorMessage = "live-health-error-sensitive",
                CreatedAtUtc = DateTimeOffset.UtcNow
            });
            var liveEvents = new List<string>();
            var liveHealth = await ReadSseEventDataAsync(
                liveStream,
                "stationHealthUpdated",
                TimeSpan.FromSeconds(2),
                liveEvents);
            liveEvents.Should().NotContain("stationLogAdded");
            AssertExactProperties(liveHealth, ["stationId", "health", "station"]);
            AssertExactProperties(liveHealth.GetProperty("health"), SafeHealthPropertyNames);
            AssertExactProperties(liveHealth.GetProperty("station"), SafeStationPropertyNames);
        }

        var checkpoint = host.Registry.GetEventsAfter(0).Max(evt => evt.SequenceId);
        host.Registry.UpsertLogSummary("conn-safe-sse", new StationLogSummaryDto
        {
            StationId = "station-safe-sse",
            SequenceId = 3,
            MessageId = "log-replay-sensitive",
            TimestampUtc = DateTimeOffset.UtcNow,
            Level = "ERROR",
            RenderedMessage = "replay-log-sensitive",
            ExceptionMessage = "replay-exception-sensitive",
            CorrelationId = "replay-correlation-sensitive"
        });
        host.Registry.UpsertResultSummary("conn-safe-sse", BuildSensitiveResult("station-safe-sse", 3));

        using var replayRequest = new HttpRequestMessage(HttpMethod.Get, "/api/stations/events");
        replayRequest.Headers.Add("Last-Event-ID", checkpoint.ToString());
        using var replayResponse = await host.Client.SendAsync(replayRequest, HttpCompletionOption.ResponseHeadersRead);
        await using var replayStream = await replayResponse.Content.ReadAsStreamAsync();
        var replayEvents = new List<string>();
        var replayResult = await ReadSseEventDataAsync(
            replayStream,
            "stationResultAdded",
            TimeSpan.FromSeconds(2),
            replayEvents);
        replayEvents.Should().NotContain("stationLogAdded");
        AssertExactProperties(replayResult.GetProperty("result"), SafeResultPropertyNames);
        AssertExactProperties(replayResult.GetProperty("station"), SafeStationPropertyNames);
    }

    [Fact]
    public async Task EventsEndpoint_ShouldPreserveCompleteAdminSnapshotAndSensitiveLiveEvents()
    {
        await using var host = await StationEndpointTestHost.CreateWithCentralStoreAsync(role: "Admin");
        SeedSensitiveStationTelemetry(host.Registry, "conn-admin-sse", "station-admin-sse");

        using var response = await host.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/api/stations/events"),
            HttpCompletionOption.ResponseHeadersRead);
        await using var stream = await response.Content.ReadAsStreamAsync();
        var initial = await ReadSseEventDataAsync(stream, "initialState", TimeSpan.FromSeconds(2));
        initial.GetProperty("stations")[0].GetProperty("machineName").GetString().Should().Be("station-admin-sse-machine");
        initial.GetProperty("stations")[0].GetProperty("packageId").GetString().Should().Be("pkg-sensitive");
        initial.GetProperty("recentResults")[0].GetProperty("result").GetProperty("primaryOutputsPreview")
            .GetProperty("serialNumber").GetString().Should().Be("SN-SECRET");

        host.Registry.UpsertLogSummary("conn-admin-sse", new StationLogSummaryDto
        {
            StationId = "station-admin-sse",
            SequenceId = 2,
            MessageId = "admin-log-live",
            TimestampUtc = DateTimeOffset.UtcNow,
            Level = "ERROR",
            RenderedMessage = "admin-rendered-sensitive",
            ExceptionMessage = "admin-exception-sensitive",
            CorrelationId = "admin-correlation-sensitive"
        });

        var liveLog = await ReadSseEventDataAsync(stream, "stationLogAdded", TimeSpan.FromSeconds(2));
        liveLog.GetProperty("log").GetProperty("exceptionMessage").GetString().Should().Be("admin-exception-sensitive");
        liveLog.GetProperty("log").GetProperty("correlationId").GetString().Should().Be("admin-correlation-sensitive");
    }

    [Fact]
    public async Task ResultsEndpoint_ShouldPageAndFilterAllStationResults()
    {
        await using var host = await StationEndpointTestHost.CreateAsync();
        host.Registry.UpsertRegistration("conn-a", BuildRegistration("station-a"));
        host.Registry.UpsertRegistration("conn-b", BuildRegistration("station-b"));

        host.Registry.UpsertResultSummary("conn-a", BuildResult("station-a", 1, RuntimeRunOutcome.Ok, "OK", -3));
        host.Registry.UpsertResultSummary("conn-b", BuildResult("station-b", 1, RuntimeRunOutcome.Error, "CAMERA_TIMEOUT", -2));
        host.Registry.UpsertResultSummary("conn-a", BuildResult("station-a", 2, RuntimeRunOutcome.Ng, "WIRE_SWAP", -1));

        var all = await host.Client.GetFromJsonAsync<StationResultsPageViewModel>("/api/stations/results?pageIndex=0&pageSize=2");
        all.Should().NotBeNull();
        all!.TotalCount.Should().Be(3);
        all.Items.Should().HaveCount(2);
        all.Items.Select(item => item.SequenceId).Should().Equal(2, 1);

        var filtered = await host.Client.GetFromJsonAsync<StationResultsPageViewModel>(
            "/api/stations/results?stationId=station-a&status=Ng&diagnosticCode=WIRE_SWAP&pageSize=10");

        filtered.Should().NotBeNull();
        filtered!.TotalCount.Should().Be(1);
        filtered.Items.Should().ContainSingle(item =>
            item.StationId == "station-a" &&
            item.Outcome == RuntimeRunOutcome.Ng &&
                item.DiagnosticCode == "WIRE_SWAP");
    }

    [Theory]
    [InlineData("/api/stations/results?from=2026-08-02T00:00:00Z&to=2026-08-01T00:00:00Z", "STATION_TIME_RANGE_INVALID")]
    [InlineData("/api/stations/results?from=2026-07-01T00:00:00Z&to=2026-08-02T00:00:00Z", "STATION_TIME_RANGE_LIMIT")]
    [InlineData("/api/stations/statistics?from=2026-08-02T00:00:00Z&to=2026-08-01T00:00:00Z", "STATION_TIME_RANGE_INVALID")]
    [InlineData("/api/stations/statistics?from=2026-07-01T00:00:00Z&to=2026-08-02T00:00:00Z", "STATION_TIME_RANGE_LIMIT")]
    public async Task ResultMonitoringEndpoints_WhenTimeBudgetIsInvalid_ShouldReturnStableBadRequest(
        string path,
        string expectedError)
    {
        await using var host = await StationEndpointTestHost.CreateAsync();

        using var response = await host.Client.GetAsync(path);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        document.RootElement.GetProperty("error").GetString().Should().Be(expectedError);
        document.RootElement.GetProperty("maximumWindowDays").GetInt32()
            .Should().Be(StationResultQueryBudget.MaximumWindowDays);
    }

    [Fact]
    public async Task ResultsEndpoint_ShouldClampLargeRequestedPageSize()
    {
        await using var host = await StationEndpointTestHost.CreateAsync();
        const int total = 520;
        for (var index = 0; index < total; index++)
        {
            var stationId = $"station-{index / 20:D2}";
            var connectionId = $"conn-{stationId}";
            if (index % 20 == 0)
            {
                host.Registry.UpsertRegistration(connectionId, BuildRegistration(stationId));
            }

            host.Registry.UpsertResultSummary(
                connectionId,
                BuildResult(stationId, (index % 20) + 1, RuntimeRunOutcome.Ng, "BUDGET_TEST", -1));
        }

        var page = await host.Client.GetFromJsonAsync<StationResultsPageViewModel>(
            "/api/stations/results?pageIndex=0&pageSize=50000");

        page.Should().NotBeNull();
        page!.PageSize.Should().Be(500);
        page.Items.Should().HaveCount(500);
        page.TotalCount.Should().Be(total);
    }

    [Fact]
    public async Task StatisticsEndpoint_ShouldUseStationResultFiltersAndDashboardFieldNames()
    {
        await using var host = await StationEndpointTestHost.CreateAsync();
        host.Registry.UpsertRegistration("conn-a", BuildRegistration("station-a"));
        host.Registry.UpsertRegistration("conn-b", BuildRegistration("station-b"));

        host.Registry.UpsertResultSummary("conn-a", BuildResult("station-a", 1, RuntimeRunOutcome.Ok, "OK", -3));
        host.Registry.UpsertResultSummary("conn-b", BuildResult("station-b", 1, RuntimeRunOutcome.Error, "CAMERA_TIMEOUT", -2));
        host.Registry.UpsertResultSummary("conn-a", BuildResult("station-a", 2, RuntimeRunOutcome.Ng, "WIRE_SWAP", -1));

        var json = await host.Client.GetStringAsync(
            "/api/stations/statistics?range=all&stationId=station-a&status=Ng&diagnosticCode=WIRE_SWAP");
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        root.GetProperty("totalCount").GetInt32().Should().Be(1);
        root.GetProperty("ngCount").GetInt32().Should().Be(1);
        root.GetProperty("okCount").GetInt32().Should().Be(0);
        root.GetProperty("byDiagnosticCode")[0].GetProperty("diagnosticCode").GetString().Should().Be("WIRE_SWAP");
        root.GetProperty("defectDistribution").GetProperty("items")[0].GetProperty("defectType").GetString().Should().Be("WIRE_SWAP");
    }

    [Fact]
    public async Task CreateCommand_ShouldRejectInvalidPayloadJson()
    {
        await using var host = await StationEndpointTestHost.CreateWithCentralStoreAsync();

        using var response = await host.Client.PostAsJsonAsync(
            "/api/stations/station-a/commands",
            new StationCommandCreateRequest
            {
                CommandType = StationCommandType.Ping,
                PayloadJson = "{not-json",
                IssuedBy = "unit-test"
            });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        await using var scope = host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<VisionDbContext>();
        (await db.StationCommandRecords.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task CreateCommand_ShouldRejectAnonymousUser()
    {
        await using var host = await StationEndpointTestHost.CreateWithCentralStoreAsync(role: null);

        using var response = await host.Client.PostAsJsonAsync(
            "/api/stations/station-a/commands",
            new StationCommandCreateRequest
            {
                CommandType = StationCommandType.Ping,
                PayloadJson = "{}"
            });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task CreateCommand_ShouldRejectAdminSessionWithoutUsername()
    {
        await using var host = await StationEndpointTestHost.CreateWithCentralStoreAsync(username: "   ");

        using var response = await host.Client.PostAsJsonAsync(
            "/api/stations/station-a/commands",
            new StationCommandCreateRequest
            {
                CommandType = StationCommandType.Ping,
                PayloadJson = "{}"
            });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        await using var scope = host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<VisionDbContext>();
        (await db.StationCommandRecords.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task UpdateIdentity_ShouldRejectNonAdminUser()
    {
        await using var host = await StationEndpointTestHost.CreateWithCentralStoreAsync(role: "Operator");

        using var response = await host.Client.PatchAsJsonAsync(
            "/api/stations/station-a/identity",
            new StationIdentityUpdateRequest
            {
                StationName = "Line station A"
            });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task DeployPackage_ShouldRejectNonAdminUser()
    {
        await using var host = await StationEndpointTestHost.CreateWithCentralStoreAsync(role: "Operator");

        using var response = await host.Client.PostAsJsonAsync(
            "/api/stations/station-a/deploy-package",
            new StationDeployPackageRequest
            {
                PackageId = "package-a"
            });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task TestPackage_ShouldRejectNonAdminUser()
    {
        await using var host = await StationEndpointTestHost.CreateWithCentralStoreAsync(role: "Operator");

        using var response = await host.Client.PostAsync("/api/station-packages/test", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task SensitiveReadEndpoints_ShouldRejectOperatorWhileSafeDetailRemainsAvailable()
    {
        await using var host = await StationEndpointTestHost.CreateWithCentralStoreAsync(role: "Operator");
        host.Registry.UpsertRegistration("conn-a", BuildRegistration("station-a"));

        var responses = new[]
        {
            await host.Client.GetAsync("/api/station-packages"),
            await host.Client.GetAsync("/api/station-packages/package-a/download"),
            await host.Client.GetAsync("/api/stations/station-a/logs"),
            await host.Client.GetAsync("/api/stations/station-a/commands"),
            await host.Client.GetAsync("/api/stations/audit")
        };

        try
        {
            responses.Should().OnlyContain(response => response.StatusCode == HttpStatusCode.Forbidden);
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }

        using var detailResponse = await host.Client.GetAsync("/api/stations/station-a");
        detailResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("Operator")]
    [InlineData("Engineer")]
    public async Task SafeMonitoringEndpoints_ShouldReturnExactNonSensitiveFields(string role)
    {
        await using var host = await StationEndpointTestHost.CreateWithCentralStoreAsync(role: role);
        SeedSensitiveStationTelemetry(host.Registry, "conn-safe", "station-safe");

        using var stationsDocument = JsonDocument.Parse(await host.Client.GetStringAsync("/api/stations"));
        var station = stationsDocument.RootElement[0];
        AssertExactProperties(station, SafeStationPropertyNames);

        using var resultsDocument = JsonDocument.Parse(await host.Client.GetStringAsync("/api/stations/results?pageSize=10"));
        AssertExactProperties(resultsDocument.RootElement, ["items", "totalCount", "pageIndex", "pageSize"]);
        AssertExactProperties(resultsDocument.RootElement.GetProperty("items")[0], SafeResultPropertyNames);

        using var recentResultsDocument = JsonDocument.Parse(await host.Client.GetStringAsync("/api/stations/station-safe/results"));
        AssertExactProperties(recentResultsDocument.RootElement[0], SafeResultPropertyNames);

        using var healthDocument = JsonDocument.Parse(await host.Client.GetStringAsync("/api/stations/station-safe/health"));
        AssertExactProperties(healthDocument.RootElement[0], SafeHealthPropertyNames);

        using var detailDocument = JsonDocument.Parse(await host.Client.GetStringAsync("/api/stations/station-safe"));
        var detail = detailDocument.RootElement;
        AssertExactProperties(detail, [.. SafeStationPropertyNames, "recentResults", "recentHealth"]);
        AssertExactProperties(detail.GetProperty("recentResults")[0], SafeResultPropertyNames);
        AssertExactProperties(detail.GetProperty("recentHealth")[0], SafeHealthPropertyNames);

        foreach (var forbiddenName in SensitiveStationPropertyNames)
        {
            detail.TryGetProperty(forbiddenName, out _).Should().BeFalse($"safe Station detail must omit {forbiddenName}");
        }
    }

    [Fact]
    public async Task AdminMonitoringEndpoints_ShouldReturnTheCompleteAdminDtos()
    {
        await using var host = await StationEndpointTestHost.CreateWithCentralStoreAsync(role: "Admin");
        SeedSensitiveStationTelemetry(host.Registry, "conn-admin", "station-admin");

        using var stationsDocument = JsonDocument.Parse(await host.Client.GetStringAsync("/api/stations"));
        AssertExactProperties(
            stationsDocument.RootElement[0],
            GetSerializedPropertyNames(new StationStatusViewModel()));

        using var resultsDocument = JsonDocument.Parse(await host.Client.GetStringAsync("/api/stations/results?pageSize=10"));
        AssertExactProperties(
            resultsDocument.RootElement.GetProperty("items")[0],
            GetSerializedPropertyNames(BuildSensitiveResult("station-admin", 1)));

        using var healthDocument = JsonDocument.Parse(await host.Client.GetStringAsync("/api/stations/station-admin/health"));
        AssertExactProperties(
            healthDocument.RootElement[0],
            GetSerializedPropertyNames(new StationHealthSnapshotDto()));

        using var detailDocument = JsonDocument.Parse(await host.Client.GetStringAsync("/api/stations/station-admin"));
        var detail = detailDocument.RootElement;
        AssertExactProperties(detail, GetSerializedPropertyNames(new StationDetailViewModel()));
        detail.GetProperty("machineName").GetString().Should().Be("station-admin-machine");
        detail.GetProperty("packageId").GetString().Should().Be("pkg-sensitive");
        detail.GetProperty("recentResults")[0].GetProperty("primaryOutputsPreview").GetProperty("serialNumber").GetString().Should().Be("SN-SECRET");
        detail.GetProperty("recentHealth")[0].GetProperty("cameraStatusSummary").GetString().Should().Be("camera-sensitive");
        detail.GetProperty("recentLogs")[0].GetProperty("exceptionMessage").GetString().Should().Be("exception-sensitive");
    }

    [Fact]
    public async Task StationPackageList_ShouldAllowAdmin()
    {
        await using var host = await StationEndpointTestHost.CreateWithCentralStoreAsync();
        await SeedPackageAsync(host.Services, "production-package", StationPackageKind.Production);

        using var response = await host.Client.GetAsync("/api/station-packages");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        json.Should().Contain("production-package");
    }

    [Fact]
    public async Task DownloadPackage_ShouldAllowStationSharedToken()
    {
        await using var host = await StationEndpointTestHost.CreateWithCentralStoreAsync(role: null);
        var packageStore = host.Services.GetRequiredService<StationPackageStore>();
        var package = await packageStore.CreateTestPackageAsync(CancellationToken.None);
        var packagePath = packageStore.GetPackagePath(package.PackageId);

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/station-packages/{package.PackageId}/download");
            request.Headers.Add(StationSyncContractDefaults.StationTokenHeaderName, StationSharedToken);

            using var response = await host.Client.SendAsync(request);

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            (await response.Content.ReadAsByteArrayAsync()).Should().NotBeEmpty();
        }
        finally
        {
            var packageDirectory = string.IsNullOrWhiteSpace(packagePath)
                ? null
                : Path.GetDirectoryName(packagePath);
            if (!string.IsNullOrWhiteSpace(packageDirectory) && Directory.Exists(packageDirectory))
            {
                Directory.Delete(packageDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task DeployPackage_ShouldRejectBlankPackageId()
    {
        await using var host = await StationEndpointTestHost.CreateWithCentralStoreAsync();

        using var response = await host.Client.PostAsJsonAsync(
            "/api/stations/station-a/deploy-package",
            new StationDeployPackageRequest
            {
                PackageId = "   ",
                IssuedBy = "unit-test"
            });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        await using var scope = host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<VisionDbContext>();
        (await db.StationCommandRecords.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task DeployPackage_ShouldRejectTestPackageFromProductionEndpoint()
    {
        await using var host = await StationEndpointTestHost.CreateWithCentralStoreAsync();
        await SeedPackageAsync(host.Services, "test-package", StationPackageKind.Test);

        using var response = await host.Client.PostAsJsonAsync(
            "/api/stations/station-a/deploy-package",
            new StationDeployPackageRequest
            {
                PackageId = "test-package",
                IssuedBy = "unit-test"
            });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        await using var scope = host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<VisionDbContext>();
        (await db.StationCommandRecords.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task DeployPackage_ShouldCreateDeployCommandForProductionPackage()
    {
        await using var host = await StationEndpointTestHost.CreateWithCentralStoreAsync();
        await SeedPackageAsync(host.Services, "production-package", StationPackageKind.Production);

        using var response = await host.Client.PostAsJsonAsync(
            "/api/stations/station-a/deploy-package",
            new StationDeployPackageRequest
            {
                PackageId = "production-package",
                IssuedBy = "unit-test"
            });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        await using var scope = host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<VisionDbContext>();
        var command = await db.StationCommandRecords.SingleAsync();
        command.CommandType.Should().Be(StationCommandType.DeployPackage.ToString());
        command.PayloadJson.Should().Contain("production-package");
    }

    [Fact]
    public async Task DownloadPackage_ShouldRejectStoredPathOutsidePackageDirectory()
    {
        await using var host = await StationEndpointTestHost.CreateWithCentralStoreAsync();
        var externalPath = Path.Combine(Path.GetTempPath(), "ClearVisionStationEndpointTests", Guid.NewGuid().ToString("N"), "outside.cvpkg");
        Directory.CreateDirectory(Path.GetDirectoryName(externalPath)!);
        await File.WriteAllTextAsync(externalPath, "not a package");

        try
        {
            await using (var scope = host.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<VisionDbContext>();
                db.StationPackageRecords.Add(new StationPackageRecordEntity
                {
                    PackageId = "package-outside",
                    PackageName = "Outside",
                    PackageVersion = "1.0.0",
                    FlowHash = "sha256:test",
                    FileName = Path.GetFileName(externalPath),
                    FilePath = externalPath,
                    SizeBytes = new FileInfo(externalPath).Length,
                    Sha256 = "test",
                    CreatedAtUtc = DateTimeOffset.UtcNow
                });
                await db.SaveChangesAsync();
            }

            using var response = await host.Client.GetAsync("/api/station-packages/package-outside/download");

            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }
        finally
        {
            try
            {
                Directory.Delete(Path.GetDirectoryName(externalPath)!, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    private static void SeedSensitiveStationTelemetry(
        StationRegistryService registry,
        string connectionId,
        string stationId)
    {
        registry.UpsertRegistration(connectionId, BuildRegistration(stationId));
        registry.UpsertHeartbeat(connectionId, new StationHeartbeatDto
        {
            StationId = stationId,
            SequenceId = 1,
            MessageId = "heartbeat-sensitive",
            LineName = "line-sensitive",
            RuntimeState = StationRuntimeState.Running,
            ConnectionState = "connected-sensitive",
            CurrentPackageId = "pkg-sensitive",
            CurrentPackageName = "package-sensitive",
            CurrentPackageVersion = "9.9.9",
            PackageFlowHash = "package-flow-sensitive",
            ExecutionFlowHash = "execution-flow-sensitive",
            FlowHash = "execution-flow-sensitive",
            ExecutionSnapshotId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            ProjectRevision = 99,
            DecisionConfigurationHash = "decision-sensitive",
            ExecutionRunMode = "production-sensitive",
            CurrentRunId = "run-sensitive",
            SessionOkCount = 4,
            SessionNgCount = 1,
            SessionErrorCount = 0,
            SpoolPendingCount = 12,
            LastResultAtUtc = DateTimeOffset.UtcNow,
            CreatedAtUtc = DateTimeOffset.UtcNow
        });
        registry.UpsertResultSummary(connectionId, BuildSensitiveResult(stationId, 1));
        registry.UpsertHealthSnapshot(connectionId, new StationHealthSnapshotDto
        {
            StationId = stationId,
            SequenceId = 1,
            MessageId = "health-sensitive",
            RuntimeState = StationRuntimeState.Running,
            ProcessUptimeSeconds = 12345,
            CpuUsagePercent = 87.5,
            WorkingSetMb = 2048,
            PrivateMemoryMb = 1024,
            DiskFreeMb = 100,
            DiskTotalMb = 1000,
            SpoolPendingCount = 12,
            SpoolBytes = 4096,
            CameraStatusSummary = "camera-sensitive",
            PlcStatusSummary = "plc-sensitive",
            CurrentPackageId = "pkg-sensitive",
            CurrentPackageHealth = "package-health-sensitive",
            LastErrorCode = "error-sensitive",
            LastErrorMessage = "error-message-sensitive",
            CreatedAtUtc = DateTimeOffset.UtcNow
        });
        registry.UpsertLogSummary(connectionId, new StationLogSummaryDto
        {
            StationId = stationId,
            SequenceId = 1,
            MessageId = "log-sensitive",
            TimestampUtc = DateTimeOffset.UtcNow,
            Level = "ERROR",
            Source = "source-sensitive",
            EventId = "event-sensitive",
            MessageTemplate = "template-sensitive",
            RenderedMessage = "rendered-sensitive",
            ExceptionType = "exception-type-sensitive",
            ExceptionMessage = "exception-sensitive",
            CorrelationId = "correlation-sensitive",
            RunId = "run-sensitive",
            PackageId = "pkg-sensitive",
            CreatedAtUtc = DateTimeOffset.UtcNow
        });
    }

    private static StationResultSummaryDto BuildSensitiveResult(string stationId, long sequenceId)
    {
        var completedAtUtc = DateTimeOffset.UtcNow;
        return new StationResultSummaryDto
        {
            StationId = stationId,
            LineName = "line-sensitive",
            SequenceId = sequenceId,
            MessageId = $"message-sensitive-{sequenceId}",
            RunId = $"run-sensitive-{sequenceId}",
            PackageId = "pkg-sensitive",
            PackageName = "package-sensitive",
            PackageVersion = "9.9.9",
            PackageFlowHash = "package-flow-sensitive",
            ExecutionFlowHash = "execution-flow-sensitive",
            FlowHash = "execution-flow-sensitive",
            ProjectRevision = 99,
            DecisionConfigurationHash = "decision-sensitive",
            ExecutionSnapshotId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
            ExecutionRunMode = "production-sensitive",
            ImageId = "image-sensitive",
            Outcome = RuntimeRunOutcome.Ng,
            InspectionStatus = InspectionStatus.NG,
            ExecutionOutcome = ExecutionOutcome.Succeeded,
            DecisionOutcome = DecisionOutcome.Invalid,
            HasJudgmentSignal = false,
            DecisionSource = "decision-source-sensitive",
            ReasonCode = "DecisionValueInvalid",
            ExecutionTimeMs = 42,
            DiagnosticCode = "WIRE_SWAP",
            DiagnosticMessage = "diagnostic-message-sensitive",
            PrimaryOutputsPreview = new Dictionary<string, string?>
            {
                ["serialNumber"] = "SN-SECRET"
            },
            StartedAtUtc = completedAtUtc.AddMilliseconds(-42),
            CompletedAtUtc = completedAtUtc,
            CreatedAtUtc = completedAtUtc
        };
    }

    private static void AssertExactProperties(JsonElement element, IEnumerable<string> expectedPropertyNames)
    {
        element.ValueKind.Should().Be(JsonValueKind.Object);
        element.EnumerateObject()
            .Select(property => property.Name)
            .Should()
            .BeEquivalentTo(expectedPropertyNames);
    }

    private static IReadOnlyList<string> GetSerializedPropertyNames(object value)
    {
        return JsonSerializer.SerializeToElement(value, value.GetType(), WebJsonOptions)
            .EnumerateObject()
            .Select(property => property.Name)
            .ToList();
    }

    private static async Task<JsonElement> ReadSseEventDataAsync(
        Stream stream,
        string expectedEventName,
        TimeSpan timeout,
        ICollection<string>? observedEventNames = null)
    {
        var buffer = new byte[2048];
        var pending = new StringBuilder();
        using var cts = new CancellationTokenSource(timeout);

        while (true)
        {
            var normalized = pending.ToString().Replace("\r\n", "\n", StringComparison.Ordinal);
            var separatorIndex = normalized.IndexOf("\n\n", StringComparison.Ordinal);
            while (separatorIndex >= 0)
            {
                var frame = normalized[..separatorIndex];
                normalized = normalized[(separatorIndex + 2)..];
                pending.Clear();
                pending.Append(normalized);

                var lines = frame.Split('\n');
                var eventName = lines
                    .FirstOrDefault(line => line.StartsWith("event:", StringComparison.Ordinal))?
                    .Substring("event:".Length)
                    .Trim();
                if (string.IsNullOrWhiteSpace(eventName))
                {
                    separatorIndex = normalized.IndexOf("\n\n", StringComparison.Ordinal);
                    continue;
                }

                observedEventNames?.Add(eventName);
                if (!string.Equals(eventName, expectedEventName, StringComparison.Ordinal))
                {
                    separatorIndex = normalized.IndexOf("\n\n", StringComparison.Ordinal);
                    continue;
                }

                var data = string.Join(
                    "\n",
                    lines
                        .Where(line => line.StartsWith("data:", StringComparison.Ordinal))
                        .Select(line => line.Substring("data:".Length).TrimStart()));
                using var document = JsonDocument.Parse(data);
                return document.RootElement.Clone();
            }

            var bytesRead = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cts.Token);
            bytesRead.Should().BeGreaterThan(0);
            pending.Append(Encoding.UTF8.GetString(buffer, 0, bytesRead));
        }
    }

    private static async Task<string> ReadUntilContainsAsync(Stream stream, string marker, TimeSpan timeout)
    {
        var buffer = new byte[1024];
        var builder = new StringBuilder();
        using var cts = new CancellationTokenSource(timeout);

        while (!builder.ToString().Contains(marker, StringComparison.Ordinal))
        {
            var bytesRead = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cts.Token);
            bytesRead.Should().BeGreaterThan(0);
            builder.Append(Encoding.UTF8.GetString(buffer, 0, bytesRead));
        }

        return builder.ToString();
    }

    private static StationRegistrationDto BuildRegistration(string stationId)
    {
        return new StationRegistrationDto
        {
            StationId = stationId,
            StationName = $"{stationId} name",
            LineName = "line-1",
            MachineName = $"{stationId}-machine",
            ClientVersion = "test",
            StartedAtUtc = DateTimeOffset.UtcNow
        };
    }

    private static async Task SeedPackageAsync(
        IServiceProvider services,
        string packageId,
        StationPackageKind packageKind)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<VisionDbContext>();
        db.StationPackageRecords.Add(new StationPackageRecordEntity
        {
            PackageId = packageId,
            PackageName = packageKind == StationPackageKind.Test ? "Test Package" : "Production Package",
            PackageVersion = "1.0.0",
            PackageKind = packageKind.ToString(),
            FlowHash = "sha256:test",
            FileName = $"{packageId}.cvpkg",
            FilePath = Path.Combine(Path.GetTempPath(), $"{packageId}.cvpkg"),
            SizeBytes = 1024,
            Sha256 = "test",
            CreatedBy = "unit-test",
            CreatedAtUtc = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
    }

    private static StationResultSummaryDto BuildResult(
        string stationId,
        long sequenceId,
        RuntimeRunOutcome outcome,
        string diagnosticCode,
        int completedOffsetMinutes)
    {
        var completedAtUtc = DateTimeOffset.UtcNow.AddMinutes(completedOffsetMinutes);
        return new StationResultSummaryDto
        {
            StationId = stationId,
            LineName = "line-1",
            SequenceId = sequenceId,
            MessageId = $"{stationId}-{sequenceId}",
            RunId = $"run-{stationId}-{sequenceId}",
            PackageId = "pkg-1",
            PackageName = "Package 1",
            PackageVersion = "1.0.0",
            FlowHash = "sha256:test",
            ImageId = $"image-{sequenceId}",
            Outcome = outcome,
            InspectionStatus = outcome switch
            {
                RuntimeRunOutcome.Ok => InspectionStatus.OK,
                RuntimeRunOutcome.Ng => InspectionStatus.NG,
                _ => InspectionStatus.Error
            },
            ExecutionTimeMs = 20 + sequenceId,
            DiagnosticCode = diagnosticCode,
            DiagnosticMessage = diagnosticCode,
            PrimaryOutputsPreview = new Dictionary<string, string?>
            {
                ["station"] = stationId
            },
            StartedAtUtc = completedAtUtc.AddMilliseconds(-25),
            CompletedAtUtc = completedAtUtc,
            CreatedAtUtc = completedAtUtc
        };
    }

    private sealed class StationEndpointTestHost : IAsyncDisposable
    {
        private readonly WebApplication _app;
        private readonly string? _tempRoot;

        private StationEndpointTestHost(WebApplication app, StationRegistryService registry, string? tempRoot = null)
        {
            _app = app;
            _tempRoot = tempRoot;
            Registry = registry;
            Client = app.GetTestClient();
        }

        public HttpClient Client { get; }

        public StationRegistryService Registry { get; }

        public IServiceProvider Services => _app.Services;

        public static async Task<StationEndpointTestHost> CreateAsync()
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                EnvironmentName = Environments.Development
            });
            builder.WebHost.UseTestServer();

            builder.Services.AddSingleton(Options.Create(new StationIngressOptions
            {
                Enabled = true,
                SharedToken = StationSharedToken,
                OfflineThresholdSeconds = 15,
                ResultBufferPerStation = 20,
                EventBufferSize = 50
            }));
            builder.Services.AddSingleton<StationRegistryService>(sp =>
                new StationRegistryService(
                    sp.GetRequiredService<IOptions<StationIngressOptions>>(),
                    NullLogger<StationRegistryService>.Instance));

            var app = builder.Build();
            app.MapStationEndpoints();
            await app.StartAsync();

            return new StationEndpointTestHost(app, app.Services.GetRequiredService<StationRegistryService>());
        }

        public static async Task<StationEndpointTestHost> CreateWithCentralStoreAsync(string? role = "Admin", string? username = null)
        {
            var root = Path.Combine(Path.GetTempPath(), "ClearVisionStationEndpointTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);

            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                EnvironmentName = Environments.Development
            });
            builder.WebHost.UseTestServer();

            builder.Services.AddDbContext<VisionDbContext>(options =>
                options.UseSqlite($"Data Source={Path.Combine(root, "vision.db")}"));
            builder.Services.AddSingleton(Options.Create(new StationIngressOptions
            {
                Enabled = true,
                SharedToken = StationSharedToken,
                OfflineThresholdSeconds = 15,
                ResultBufferPerStation = 20,
                EventBufferSize = 50
            }));
            builder.Services.AddSingleton<StationRegistryService>(sp =>
                new StationRegistryService(
                    sp.GetRequiredService<IOptions<StationIngressOptions>>(),
                    NullLogger<StationRegistryService>.Instance));
            builder.Services.AddSingleton<StationCentralStore>(sp =>
                new StationCentralStore(
                    sp.GetRequiredService<IServiceScopeFactory>(),
                    NullLogger<StationCentralStore>.Instance));
            builder.Services.AddSingleton<StationPackageStore>(sp =>
                new StationPackageStore(
                    sp.GetRequiredService<IServiceScopeFactory>(),
                    NullLogger<StationPackageStore>.Instance));
            builder.Services.AddSingleton<StationIngressAuthService>(sp =>
                new StationIngressAuthService(
                    sp.GetRequiredService<IOptions<StationIngressOptions>>(),
                    NullLogger<StationIngressAuthService>.Instance));

            var app = builder.Build();
            await using (var scope = app.Services.CreateAsyncScope())
            {
                await scope.ServiceProvider.GetRequiredService<VisionDbContext>().Database.EnsureCreatedAsync();
            }

            app.Use(async (context, next) =>
            {
                if (role is not null)
                {
                    context.Items["CurrentUser"] = new UserSession
                    {
                        UserId = role.ToLowerInvariant(),
                        Username = username ?? role.ToLowerInvariant(),
                        Role = role
                    };
                }

                await next();
            });
            app.MapStationEndpoints();
            await app.StartAsync();

            return new StationEndpointTestHost(app, app.Services.GetRequiredService<StationRegistryService>(), root);
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await _app.StopAsync();
            await _app.DisposeAsync();

            if (!string.IsNullOrWhiteSpace(_tempRoot))
            {
                try
                {
                    Directory.Delete(_tempRoot, recursive: true);
                }
                catch (IOException)
                {
                }
            }
        }
    }
}
