using System.Text;
using System.Text.Json;
using ClearVision.Product.Station.Sync;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ClearVision.Product.Desktop.Tests;

[TestClassification(TestDomain.Desktop, TestPurpose.Regression, TestLane.Pr, TestEvidenceType.Contract, TestOracleType.Contract, TestResourceRequirement.None, TestExpectedDuration.Fast, TestFlakyPolicy.Blocking, "desktop")]
public sealed class StationSyncSettingsStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "clearvision-station-sync-settings-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void SaveConnectionSettings_ShouldPersistConnectionFieldsAndPreserveExtensionFields()
    {
        var settingsPath = Path.Combine(_root, "station-sync-settings.json");
        Directory.CreateDirectory(_root);
        File.WriteAllText(
            settingsPath,
            """
            {
              "RootExtension": true,
              "StationSync": {
                "Enabled": false,
                "StudioBaseUrl": "",
                "SharedToken": "",
                "HeartbeatIntervalSeconds": 9,
                "SpoolDirectory": "%LocalAppData%\\CustomSpool"
              }
            }
            """,
            Encoding.UTF8);

        var liveOptions = new StationSyncOptions();
        var store = new StationSyncSettingsStore(settingsPath, liveOptions);

        var saved = store.SaveConnectionSettings(new StationSyncConnectionSettings
        {
            Enabled = true,
            StudioBaseUrl = " http://192.168.137.13:5000/ ",
            SharedToken = " station-secret "
        });

        saved.StudioBaseUrl.Should().Be("http://192.168.137.13:5000");
        saved.StudioHubUrl.Should().Be("http://192.168.137.13:5000/hubs/station-ingest");
        saved.SharedToken.Should().Be("station-secret");
        liveOptions.Enabled.Should().BeTrue();
        liveOptions.ResolvedStudioHubUrl.Should().Be("http://192.168.137.13:5000/hubs/station-ingest");
        liveOptions.SharedToken.Should().Be("station-secret");

        using var document = JsonDocument.Parse(File.ReadAllText(settingsPath));
        document.RootElement.GetProperty("RootExtension").GetBoolean().Should().BeTrue();
        var stationSync = document.RootElement.GetProperty("StationSync");
        stationSync.GetProperty("Enabled").GetBoolean().Should().BeTrue();
        stationSync.GetProperty("StudioBaseUrl").GetString().Should().Be("http://192.168.137.13:5000");
        stationSync.GetProperty("StudioHubUrl").GetString().Should().Be("http://192.168.137.13:5000/hubs/station-ingest");
        stationSync.GetProperty("SharedToken").GetString().Should().Be("station-secret");
        stationSync.GetProperty("HeartbeatIntervalSeconds").GetInt32().Should().Be(9);
        stationSync.GetProperty("SpoolDirectory").GetString().Should().Be("%LocalAppData%\\CustomSpool");
        File.ReadAllText(settingsPath).Should().NotContain("********");
    }

    [Fact]
    public void ResolvedStudioHubUrl_ShouldSupportBaseUrlAndExistingHubUrl()
    {
        new StationSyncOptions
        {
            StudioBaseUrl = "http://10.0.0.8:5000"
        }.ResolvedStudioHubUrl.Should().Be("http://10.0.0.8:5000/hubs/station-ingest");

        new StationSyncOptions
        {
            StudioBaseUrl = "http://10.0.0.8:5000/hubs/station-ingest"
        }.ResolvedStudioHubUrl.Should().Be("http://10.0.0.8:5000/hubs/station-ingest");

        new StationSyncOptions
        {
            StudioBaseUrl = "http://10.0.0.8:5000",
            StudioHubUrl = "http://10.0.0.9:5000/custom-hub"
        }.ResolvedStudioHubUrl.Should().Be("http://10.0.0.9:5000/custom-hub");
    }

    [Fact]
    public async Task SaveDisabledConnectionSettings_ShouldStopHubConnectionAttempts()
    {
        var settingsPath = Path.Combine(_root, "station-sync-settings.json");
        var liveOptions = new StationSyncOptions
        {
            Enabled = true,
            StudioBaseUrl = "http://127.0.0.1:59999",
            SharedToken = "station-secret"
        };
        var store = new StationSyncSettingsStore(settingsPath, liveOptions);
        store.SaveConnectionSettings(new StationSyncConnectionSettings
        {
            Enabled = false,
            StudioBaseUrl = "http://127.0.0.1:59999",
            SharedToken = "station-secret"
        });

        await using var hubClient = new StationHubClient(
            Options.Create(liveOptions),
            NullLogger<StationHubClient>.Instance);

        var connected = await hubClient.EnsureConnectedAsync(CancellationToken.None);

        connected.Should().BeFalse();
        hubClient.LastErrorMessage.Should().Contain("disabled");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
