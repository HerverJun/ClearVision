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

public sealed class StationEndpointsTests
{
    private const string StationSharedToken = "station-secret";

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
        initialChunk.Should().Contain("\"eventSequenceId\":2");

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
    public async Task CreateCommand_ShouldRequireClientRequestId()
    {
        await using var host = await StationEndpointTestHost.CreateWithCentralStoreAsync();

        using var response = await host.Client.PostAsJsonAsync(
            "/api/stations/station-a/commands",
            new StationCommandCreateRequest
            {
                CommandType = StationCommandType.Ping,
                PayloadJson = "{}"
            });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("ClientRequestIdRequired");
    }

    [Fact]
    public async Task EventsEndpoint_ShouldRedactAdminOnlyLogPayloadForMonitorReaders()
    {
        await using var host = await StationEndpointTestHost.CreateAsync();
        host.Registry.UpsertRegistration("conn-monitor", BuildRegistration("station-monitor"));
        var checkpoint = host.Registry.GetEventsAfter(0).Max(evt => evt.SequenceId);
        host.Registry.UpsertLogSummary("conn-monitor", new StationLogSummaryDto
        {
            SchemaVersion = 2,
            StationId = "station-monitor",
            SequenceId = 1,
            MessageId = "log-secret",
            TimestampUtc = DateTimeOffset.UtcNow,
            Level = "Error",
            Source = "RuntimeHost",
            RenderedMessage = "sensitive-admin-log-payload",
            CreatedAtUtc = DateTimeOffset.UtcNow
        });

        using var response = await host.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, $"/api/stations/events?afterSequence={checkpoint}"),
            HttpCompletionOption.ResponseHeadersRead);
        await using var stream = await response.Content.ReadAsStreamAsync();
        var chunk = await ReadUntilContainsAsync(stream, "event: stationLogAdded", TimeSpan.FromSeconds(2));

        chunk.Should().Contain("\"stationId\":\"station-monitor\"");
        chunk.Should().NotContain("sensitive-admin-log-payload");
    }

    [Fact]
    public async Task CreateCommand_ShouldReuseSameKeyAndRejectDifferentPayload()
    {
        await using var host = await StationEndpointTestHost.CreateWithCentralStoreAsync();
        var request = new StationCommandCreateRequest
        {
            CommandType = StationCommandType.StartRuntime,
            PayloadJson = """{"mode":"formal"}""",
            ClientRequestId = "request-http-retry"
        };

        using var firstResponse = await host.Client.PostAsJsonAsync("/api/stations/station-a/commands", request);
        using var retryResponse = await host.Client.PostAsJsonAsync("/api/stations/station-a/commands", request);
        var first = await firstResponse.Content.ReadFromJsonAsync<StationCommandDto>();
        var retry = await retryResponse.Content.ReadFromJsonAsync<StationCommandDto>();

        firstResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        retryResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        retry!.CommandId.Should().Be(first!.CommandId);
        retry.ClientRequestId.Should().Be("request-http-retry");

        request.PayloadJson = """{"mode":"preview"}""";
        using var conflictResponse = await host.Client.PostAsJsonAsync("/api/stations/station-a/commands", request);
        conflictResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);

        using var lookupResponse = await host.Client.GetAsync(
            "/api/stations/station-a/commands/by-client-request/request-http-retry?commandType=StartRuntime");
        var lookup = await lookupResponse.Content.ReadFromJsonAsync<StationCommandDto>();
        lookupResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        lookup!.CommandId.Should().Be(first.CommandId);

        using var listResponse = await host.Client.GetAsync("/api/stations/station-a/commands?take=50");
        var listed = await listResponse.Content.ReadFromJsonAsync<List<StationCommandDto>>();
        listed.Should().ContainSingle(command => command.CommandId == first.CommandId && command.Status == StationCommandStatus.Created);
    }

    [Fact]
    public async Task CommandReads_ShouldSettleExpiredCommandWhileStationIsOffline()
    {
        await using var host = await StationEndpointTestHost.CreateWithCentralStoreAsync();
        using var createResponse = await host.Client.PostAsJsonAsync(
            "/api/stations/station-a/commands",
            new StationCommandCreateRequest
            {
                CommandType = StationCommandType.StartRuntime,
                PayloadJson = "{}",
                ClientRequestId = "request-offline-expiry"
            });
        var created = await createResponse.Content.ReadFromJsonAsync<StationCommandDto>();
        createResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<VisionDbContext>();
            var entity = await db.StationCommandRecords.SingleAsync();
            entity.ExpiresAtUtc = DateTimeOffset.UtcNow.AddSeconds(-1);
            await db.SaveChangesAsync();
        }

        using var lookupResponse = await host.Client.GetAsync(
            "/api/stations/station-a/commands/by-client-request/request-offline-expiry?commandType=StartRuntime");
        var lookup = await lookupResponse.Content.ReadFromJsonAsync<StationCommandDto>();
        lookupResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        lookup.Should().Match<StationCommandDto>(command =>
            command.CommandId == created!.CommandId &&
            command.Status == StationCommandStatus.TimedOut &&
            command.CompletedAtUtc.HasValue);

        using var listResponse = await host.Client.GetAsync("/api/stations/station-a/commands?take=50");
        var listed = await listResponse.Content.ReadFromJsonAsync<List<StationCommandDto>>();
        listed.Should().ContainSingle(command =>
            command.CommandId == created!.CommandId && command.Status == StationCommandStatus.TimedOut);
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
    public async Task SensitiveReadEndpoints_ShouldRejectOperator()
    {
        await using var host = await StationEndpointTestHost.CreateWithCentralStoreAsync(role: "Operator");

        var responses = new[]
        {
            await host.Client.GetAsync("/api/station-packages"),
            await host.Client.GetAsync("/api/station-packages/package-a/download"),
            await host.Client.GetAsync("/api/stations/station-a"),
            await host.Client.GetAsync("/api/stations/station-a/logs"),
            await host.Client.GetAsync("/api/stations/station-a/commands"),
            await host.Client.GetAsync("/api/stations/station-a/commands/by-client-request/request-1?commandType=Ping"),
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
                IssuedBy = "unit-test",
                ClientRequestId = "deploy-test-package"
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
        await SeedPackageAsync(host.Services, "production-package-2", StationPackageKind.Production);
        RegisterDeployableStation(host);

        using var response = await host.Client.PostAsJsonAsync(
            "/api/stations/station-a/deploy-package",
            new StationDeployPackageRequest
            {
                PackageId = "production-package",
                IssuedBy = "unit-test",
                ClientRequestId = "deploy-production-package"
            });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        await using var scope = host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<VisionDbContext>();
        var command = await db.StationCommandRecords.SingleAsync();
        command.CommandType.Should().Be(StationCommandType.DeployPackage.ToString());
        command.PayloadJson.Should().Contain("production-package");
        command.ClientRequestId.Should().Be("deploy-production-package");

        host.Registry.MarkDisconnected("conn-station-a");
        using var retryResponse = await host.Client.PostAsJsonAsync(
            "/api/stations/station-a/deploy-package",
            new StationDeployPackageRequest
            {
                PackageId = "production-package",
                ClientRequestId = "deploy-production-package"
            });
        var retry = await retryResponse.Content.ReadFromJsonAsync<StationCommandDto>();
        retryResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        retry!.CommandId.Should().Be(command.CommandId);
        (await db.StationCommandRecords.CountAsync()).Should().Be(1);

        using var conflictResponse = await host.Client.PostAsJsonAsync(
            "/api/stations/station-a/deploy-package",
            new StationDeployPackageRequest
            {
                PackageId = "production-package-2",
                ClientRequestId = "deploy-production-package"
            });
        conflictResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await conflictResponse.Content.ReadAsStringAsync()).Should().Contain("StationCommandIdempotencyConflict");
        (await db.StationCommandRecords.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task DeployPackage_ShouldRejectUnknownStation()
    {
        await using var host = await StationEndpointTestHost.CreateWithCentralStoreAsync();
        await SeedPackageAsync(host.Services, "production-package", StationPackageKind.Production);

        using var response = await host.Client.PostAsJsonAsync(
            "/api/stations/station-a/deploy-package",
            new StationDeployPackageRequest
            {
                PackageId = "production-package",
                ClientRequestId = "deploy-unknown-station"
            });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync()).Should().Contain("StationNotFound");
    }

    [Fact]
    public async Task DeployPackage_ShouldRejectOfflineStation()
    {
        await using var host = await StationEndpointTestHost.CreateWithCentralStoreAsync();
        await SeedPackageAsync(host.Services, "production-package", StationPackageKind.Production);
        RegisterDeployableStation(host);
        host.Registry.MarkDisconnected("conn-station-a");

        using var response = await host.Client.PostAsJsonAsync(
            "/api/stations/station-a/deploy-package",
            new StationDeployPackageRequest
            {
                PackageId = "production-package",
                ClientRequestId = "deploy-offline-station"
            });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync()).Should().Contain("StationOffline");
    }

    [Fact]
    public async Task DeployPackage_ShouldRejectDisabledStation()
    {
        await using var host = await StationEndpointTestHost.CreateWithCentralStoreAsync();
        await SeedPackageAsync(host.Services, "production-package", StationPackageKind.Production);
        RegisterDeployableStation(host);
        host.Registry.UpdateIdentity(
            "station-a",
            new StationIdentityUpdateRequest { IsEnabled = false },
            "admin",
            clientIp: null);

        using var response = await host.Client.PostAsJsonAsync(
            "/api/stations/station-a/deploy-package",
            new StationDeployPackageRequest
            {
                PackageId = "production-package",
                ClientRequestId = "deploy-disabled-station"
            });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync()).Should().Contain("StationDisabled");
    }

    [Fact]
    public async Task DeployPackage_ShouldRejectNonInspectionStation()
    {
        await using var host = await StationEndpointTestHost.CreateWithCentralStoreAsync();
        await SeedPackageAsync(host.Services, "production-package", StationPackageKind.Production);
        RegisterDeployableStation(host, stationRole: "Configuration");

        using var response = await host.Client.PostAsJsonAsync(
            "/api/stations/station-a/deploy-package",
            new StationDeployPackageRequest
            {
                PackageId = "production-package",
                ClientRequestId = "deploy-wrong-role"
            });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync()).Should().Contain("StationRoleNotDeployable");
    }

    [Fact]
    public async Task DeployPackage_ShouldRejectRunningStation()
    {
        await using var host = await StationEndpointTestHost.CreateWithCentralStoreAsync();
        await SeedPackageAsync(host.Services, "production-package", StationPackageKind.Production);
        RegisterDeployableStation(host, runtimeState: StationRuntimeState.Running);

        using var response = await host.Client.PostAsJsonAsync(
            "/api/stations/station-a/deploy-package",
            new StationDeployPackageRequest
            {
                PackageId = "production-package",
                ClientRequestId = "deploy-running-station"
            });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync()).Should().Contain("StationRuntimeNotIdle");
    }

    [Fact]
    public async Task DeployPackage_ShouldRejectIncompatibleStationVersion()
    {
        await using var host = await StationEndpointTestHost.CreateWithCentralStoreAsync();
        await SeedPackageAsync(
            host.Services,
            "production-package",
            StationPackageKind.Production,
            minStationVersion: "2.0.0");
        RegisterDeployableStation(host, clientVersion: "1.9.9");

        using var response = await host.Client.PostAsJsonAsync(
            "/api/stations/station-a/deploy-package",
            new StationDeployPackageRequest
            {
                PackageId = "production-package",
                ClientRequestId = "deploy-incompatible-version"
            });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync()).Should().Contain("StationVersionIncompatible");
    }

    [Fact]
    public async Task DeployPackage_ShouldRejectIncompletePackageIdentity()
    {
        await using var host = await StationEndpointTestHost.CreateWithCentralStoreAsync();
        await SeedPackageAsync(
            host.Services,
            "production-package",
            StationPackageKind.Production,
            includeIdentity: false);
        RegisterDeployableStation(host);

        using var response = await host.Client.PostAsJsonAsync(
            "/api/stations/station-a/deploy-package",
            new StationDeployPackageRequest
            {
                PackageId = "production-package",
                ClientRequestId = "deploy-incomplete-package"
            });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync()).Should().Contain("StationPackageIdentityIncomplete");
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

    private static StationRegistrationDto BuildRegistration(
        string stationId,
        string stationRole = "Inspection",
        string clientVersion = "1.0.0")
    {
        return new StationRegistrationDto
        {
            StationId = stationId,
            StationName = $"{stationId} name",
            LineName = "line-1",
            StationRole = stationRole,
            MachineName = $"{stationId}-machine",
            ClientVersion = clientVersion,
            StartedAtUtc = DateTimeOffset.UtcNow
        };
    }

    private static void RegisterDeployableStation(
        StationEndpointTestHost host,
        string stationId = "station-a",
        string stationRole = "Inspection",
        string clientVersion = "1.0.0",
        StationRuntimeState runtimeState = StationRuntimeState.Idle)
    {
        var connectionId = $"conn-{stationId}";
        host.Registry.UpsertRegistration(
            connectionId,
            BuildRegistration(stationId, stationRole, clientVersion));
        host.Registry.UpsertHeartbeat(connectionId, new StationHeartbeatDto
        {
            StationId = stationId,
            RuntimeState = runtimeState,
            CreatedAtUtc = DateTimeOffset.UtcNow
        });
    }

    private static async Task SeedPackageAsync(
        IServiceProvider services,
        string packageId,
        StationPackageKind packageKind,
        string minStationVersion = "0.1.0",
        bool includeIdentity = true)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<VisionDbContext>();
        db.StationPackageRecords.Add(new StationPackageRecordEntity
        {
            PackageId = packageId,
            PackageName = packageKind == StationPackageKind.Test ? "Test Package" : "Production Package",
            PackageVersion = "1.0.0",
            MinStationVersion = minStationVersion,
            PackageKind = packageKind.ToString(),
            FlowHash = "sha256:test",
            SourceProjectId = includeIdentity ? Guid.Parse("11111111-1111-1111-1111-111111111111") : null,
            SourceProjectRevision = includeIdentity ? 7 : null,
            DecisionConfigurationHash = includeIdentity ? "sha256:decision" : null,
            FileName = $"{packageId}.cvpkg",
            FilePath = Path.Combine(Path.GetTempPath(), $"{packageId}.cvpkg"),
            SizeBytes = 1024,
            Sha256 = includeIdentity ? new string('a', 64) : string.Empty,
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
                    NullLogger<StationRegistryService>.Instance,
                    sp.GetRequiredService<StationCentralStore>()));
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
