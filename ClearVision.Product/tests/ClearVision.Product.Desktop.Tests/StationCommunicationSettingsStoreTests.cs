using System.Text;
using System.Text.Json;
using ClearVision.Product.Desktop.Station;
using ClearVision.Product.Station.Sync;
using FluentAssertions;
using Microsoft.Extensions.Configuration;

namespace ClearVision.Product.Desktop.Tests;

[TestClassification(TestDomain.Desktop, TestPurpose.Regression, TestLane.Pr, TestEvidenceType.Contract, TestOracleType.Contract, TestResourceRequirement.None, TestExpectedDuration.Fast, TestFlakyPolicy.Blocking, "desktop")]
public sealed class StationCommunicationSettingsStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "clearvision-station-communication-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void SaveSettings_ShouldPersistLoopbackStudioIngressAndLocalStationSync()
    {
        var store = new StationCommunicationSettingsStore(_root);
        var running = new StationIngressOptions
        {
            Enabled = false,
            ListenMode = StationIngressListenMode.Loopback,
            Port = 5000,
            SharedToken = string.Empty,
            AllowInsecureDevelopment = false
        };

        var result = store.SaveSettings(
            new StationCommunicationSettingsUpdateRequest
            {
                Mode = "LocalLoopback",
                Port = 5010,
                LanHost = "192.168.1.20"
            },
            running);

        result.Success.Should().BeTrue();
        result.Settings.Should().NotBeNull();
        result.Settings!.Mode.Should().Be("LocalLoopback");
        result.Settings.RequiresRestart.Studio.Should().BeTrue();
        result.Settings.RequiresRestart.LocalStation.Should().BeTrue();
        result.Settings.Token.HasToken.Should().BeTrue();

        using var studioDocument = JsonDocument.Parse(File.ReadAllText(store.StudioSettingsPath));
        var ingress = studioDocument.RootElement.GetProperty("StationIngress");
        ingress.GetProperty("Enabled").GetBoolean().Should().BeTrue();
        ingress.GetProperty("ListenMode").GetString().Should().Be("Loopback");
        ingress.GetProperty("Port").GetInt32().Should().Be(5010);
        var sharedToken = ingress.GetProperty("SharedToken").GetString();
        sharedToken.Should().MatchRegex(@"^\d{6}$");
        ingress.GetProperty("AllowInsecureDevelopment").GetBoolean().Should().BeFalse();

        using var stationDocument = JsonDocument.Parse(File.ReadAllText(store.StationSyncSettingsPath));
        var stationSync = stationDocument.RootElement.GetProperty("StationSync");
        stationSync.GetProperty("Enabled").GetBoolean().Should().BeTrue();
        stationSync.GetProperty("StudioBaseUrl").GetString().Should().Be("http://127.0.0.1:5010");
        stationSync.GetProperty("StudioHubUrl").GetString().Should().BeEmpty();
        stationSync.GetProperty("SharedToken").GetString().Should().Be(sharedToken);
        stationSync.TryGetProperty("HeartbeatIntervalSeconds", out _).Should().BeFalse();
        stationSync.TryGetProperty("SpoolDirectory", out _).Should().BeFalse();
    }

    [Fact]
    public void SaveSettings_ShouldMapLanControllerButKeepLocalStationOnLoopback()
    {
        var store = new StationCommunicationSettingsStore(_root);
        var running = new StationIngressOptions { Enabled = false, Port = 5000 };

        var result = store.SaveSettings(
            new StationCommunicationSettingsUpdateRequest
            {
                Mode = "LanController",
                Port = 5020,
                LanHost = "10.10.0.8"
            },
            running);

        result.Success.Should().BeTrue();
        result.Settings!.Mode.Should().Be("LanController");
        result.Settings.RemoteStationBaseUrl.Should().Be("http://10.10.0.8:5020");
        result.Settings.LocalStationBaseUrl.Should().Be("http://127.0.0.1:5020");

        using var studioDocument = JsonDocument.Parse(File.ReadAllText(store.StudioSettingsPath));
        studioDocument.RootElement.GetProperty("StationIngress").GetProperty("ListenMode").GetString().Should().Be("Lan");

        using var stationDocument = JsonDocument.Parse(File.ReadAllText(store.StationSyncSettingsPath));
        stationDocument.RootElement.GetProperty("StationSync").GetProperty("StudioBaseUrl").GetString()
            .Should().Be("http://127.0.0.1:5020");
    }

    [Fact]
    public void SaveSettings_ShouldRejectOutOfRangePortWithoutWritingFiles()
    {
        var store = new StationCommunicationSettingsStore(_root);

        var result = store.SaveSettings(
            new StationCommunicationSettingsUpdateRequest
            {
                Mode = "LocalLoopback",
                Port = 70000
            },
            new StationIngressOptions());

        result.Success.Should().BeFalse();
        result.Errors.Should().Contain(error => error.Field == "port");
        File.Exists(store.StudioSettingsPath).Should().BeFalse();
        File.Exists(store.StationSyncSettingsPath).Should().BeFalse();
    }

    [Fact]
    public void SaveSettings_ShouldRejectInvalidLanHostWithoutWritingFiles()
    {
        var store = new StationCommunicationSettingsStore(_root);

        var result = store.SaveSettings(
            new StationCommunicationSettingsUpdateRequest
            {
                Mode = "LanController",
                Port = 5010,
                LanHost = "http://192.168.1.20/path"
            },
            new StationIngressOptions());

        result.Success.Should().BeFalse();
        result.Errors.Should().Contain(error => error.Field == "lanHost");
        File.Exists(store.StudioSettingsPath).Should().BeFalse();
        File.Exists(store.StationSyncSettingsPath).Should().BeFalse();
    }

    [Fact]
    public void GetSettings_ShouldKeepLocalStationRestartRequiredUntilStationAppliesOverride()
    {
        var store = new StationCommunicationSettingsStore(_root);
        var running = new StationIngressOptions
        {
            Enabled = true,
            ListenMode = StationIngressListenMode.Loopback,
            Port = 5010
        };

        var saveResult = store.SaveSettings(
            new StationCommunicationSettingsUpdateRequest
            {
                Mode = "LocalLoopback",
                Port = 5010
            },
            running);

        saveResult.Success.Should().BeTrue();
        store.GetSettings(running).RequiresRestart.LocalStation.Should().BeTrue();

        var settingsWriteTime = File.GetLastWriteTimeUtc(store.StationSyncSettingsPath);
        Directory.CreateDirectory(Path.GetDirectoryName(store.StationSyncAppliedMarkerPath)!);
        File.WriteAllText(store.StationSyncAppliedMarkerPath, DateTimeOffset.UtcNow.ToString("O"));
        File.SetLastWriteTimeUtc(store.StationSyncAppliedMarkerPath, settingsWriteTime.AddSeconds(1));

        store.GetSettings(running).RequiresRestart.LocalStation.Should().BeFalse();
    }

    [Fact]
    public void SaveSettings_ShouldPreserveExistingStationSyncExtensionFields()
    {
        var store = new StationCommunicationSettingsStore(_root);
        Directory.CreateDirectory(Path.GetDirectoryName(store.StationSyncSettingsPath)!);
        File.WriteAllText(
            store.StationSyncSettingsPath,
            """
            {
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

        var result = store.SaveSettings(
            new StationCommunicationSettingsUpdateRequest
            {
                Mode = "LocalLoopback",
                Port = 5012
            },
            new StationIngressOptions());

        result.Success.Should().BeTrue();
        using var stationDocument = JsonDocument.Parse(File.ReadAllText(store.StationSyncSettingsPath));
        var stationSync = stationDocument.RootElement.GetProperty("StationSync");
        stationSync.GetProperty("StudioBaseUrl").GetString().Should().Be("http://127.0.0.1:5012");
        stationSync.GetProperty("HeartbeatIntervalSeconds").GetInt32().Should().Be(9);
        stationSync.GetProperty("SpoolDirectory").GetString().Should().Be("%LocalAppData%\\CustomSpool");
    }

    [Fact]
    public void StationSyncJsonOverride_ShouldOverrideDefaultAppsettingsValues()
    {
        var appsettingsPath = Path.Combine(_root, "appsettings.json");
        var overridePath = Path.Combine(_root, "station-sync-settings.json");
        Directory.CreateDirectory(_root);
        File.WriteAllText(
            appsettingsPath,
            """
            {
              "StationSync": {
                "Enabled": false,
                "StudioBaseUrl": "",
                "SharedToken": ""
              }
            }
            """,
            Encoding.UTF8);
        File.WriteAllText(
            overridePath,
            """
            {
              "StationSync": {
                "Enabled": true,
                "StudioBaseUrl": "http://127.0.0.1:5123",
                "SharedToken": "override-token"
              }
            }
            """,
            Encoding.UTF8);

        var configuration = new ConfigurationBuilder()
            .AddJsonFile(appsettingsPath)
            .AddJsonFile(overridePath)
            .Build();

        var options = configuration.GetSection(StationSyncOptions.SectionName).Get<StationSyncOptions>();

        options.Should().NotBeNull();
        options!.Enabled.Should().BeTrue();
        options.StudioBaseUrl.Should().Be("http://127.0.0.1:5123");
        options.ResolvedStudioHubUrl.Should().Be("http://127.0.0.1:5123/hubs/station-ingest");
        options.SharedToken.Should().Be("override-token");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
