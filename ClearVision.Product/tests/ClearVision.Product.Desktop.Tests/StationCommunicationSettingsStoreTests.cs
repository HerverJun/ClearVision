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
        var generationId = studioDocument.RootElement.GetProperty("GenerationId").GetString();
        generationId.Should().MatchRegex("^[0-9a-f]{32}$");
        var ingress = studioDocument.RootElement.GetProperty("StationIngress");
        ingress.GetProperty("Enabled").GetBoolean().Should().BeTrue();
        ingress.GetProperty("ListenMode").GetString().Should().Be("Loopback");
        ingress.GetProperty("Port").GetInt32().Should().Be(5010);
        var sharedToken = ingress.GetProperty("SharedToken").GetString();
        sharedToken.Should().MatchRegex(@"^\d{6}$");
        ingress.GetProperty("AllowInsecureDevelopment").GetBoolean().Should().BeFalse();

        using var stationDocument = JsonDocument.Parse(File.ReadAllText(store.StationSyncSettingsPath));
        stationDocument.RootElement.GetProperty("GenerationId").GetString().Should().Be(generationId);
        result.Settings.GenerationId.Should().Be(generationId);
        var stationSync = stationDocument.RootElement.GetProperty("StationSync");
        stationSync.GetProperty("Enabled").GetBoolean().Should().BeTrue();
        stationSync.GetProperty("StudioBaseUrl").GetString().Should().Be("http://127.0.0.1:5010");
        stationSync.GetProperty("StudioHubUrl").GetString().Should().BeEmpty();
        stationSync.GetProperty("SharedToken").GetString().Should().Be(sharedToken);
        stationSync.TryGetProperty("HeartbeatIntervalSeconds", out _).Should().BeFalse();
        stationSync.TryGetProperty("SpoolDirectory", out _).Should().BeFalse();

        using var commitMarker = JsonDocument.Parse(File.ReadAllText(store.CommitMarkerPath));
        commitMarker.RootElement.GetProperty("State").GetString().Should().Be("Committed");
        commitMarker.RootElement.GetProperty("GenerationId").GetString().Should().Be(generationId);
        var generationDirectory = Path.Combine(store.TransactionRootPath, generationId!);
        File.Exists(Path.Combine(generationDirectory, "studio.candidate.json")).Should().BeTrue();
        File.Exists(Path.Combine(generationDirectory, "station.candidate.json")).Should().BeTrue();
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
    public async Task SaveAndRegenerate_AcrossCanonicalPathStoreInstances_ShouldShareOneAuthority()
    {
        var seedStore = new StationCommunicationSettingsStore(_root);
        var running = CreateRunningIngress();
        var seed = seedStore.SaveSettings(
            new StationCommunicationSettingsUpdateRequest
            {
                Mode = "LocalLoopback",
                Port = 5100
            },
            running);
        seed.Success.Should().BeTrue();
        var seedToken = seedStore.RevealToken(running).Token;

        using var firstEntered = new ManualResetEventSlim();
        using var releaseFirst = new ManualResetEventSlim();
        using var secondTaskStarted = new ManualResetEventSlim();
        using var secondEntered = new ManualResetEventSlim();
        var firstSnapshot = 0;
        var firstFault = new DelegateFaultInjector((stage, _) =>
        {
            if (stage == StationCommunicationPersistenceStage.AuthoritativeSnapshotRead &&
                Interlocked.CompareExchange(ref firstSnapshot, 1, 0) == 0)
            {
                firstEntered.Set();
                if (!releaseFirst.Wait(TimeSpan.FromSeconds(10)))
                {
                    throw new TimeoutException("Timed out waiting to release the first Station mutation.");
                }
            }
        });
        var secondFault = new DelegateFaultInjector((stage, _) =>
        {
            if (stage == StationCommunicationPersistenceStage.OperationEntered)
            {
                secondEntered.Set();
            }
        });
        var studioAlias = Path.Combine(
            Path.GetDirectoryName(seedStore.StudioSettingsPath)!,
            ".",
            Path.GetFileName(seedStore.StudioSettingsPath));
        var firstStore = new StationCommunicationSettingsStore(
            studioAlias,
            seedStore.StationSyncSettingsPath,
            firstFault);
        var secondStore = new StationCommunicationSettingsStore(
            seedStore.StudioSettingsPath,
            seedStore.StationSyncSettingsPath,
            secondFault);

        var saveTask = Task.Run(() => firstStore.SaveSettings(
            new StationCommunicationSettingsUpdateRequest
            {
                Mode = "LanController",
                Port = 5111,
                LanHost = "10.20.30.40"
            },
            running));

        try
        {
            firstEntered.Wait(TimeSpan.FromSeconds(10)).Should().BeTrue();
            var regenerateTask = Task.Run(() =>
            {
                secondTaskStarted.Set();
                return secondStore.RegenerateToken(running);
            });
            secondTaskStarted.Wait(TimeSpan.FromSeconds(10)).Should().BeTrue();
            var secondWasSerialized = !secondEntered.Wait(TimeSpan.FromMilliseconds(200));

            releaseFirst.Set();
            var saveResult = await saveTask;
            var regenerateResult = await regenerateTask;

            secondWasSerialized.Should().BeTrue();
            secondEntered.IsSet.Should().BeTrue();
            saveResult.Success.Should().BeTrue();
            regenerateResult.Success.Should().BeTrue();
            regenerateResult.Token.Should().NotBe(seedToken);

            var final = secondStore.GetSettings(running);
            final.Mode.Should().Be("LanController");
            final.Port.Should().Be(5111);
            final.Token.Last4.Should().Be(regenerateResult.TokenInfo.Last4);
            ReadGeneration(seedStore.StudioSettingsPath).Should().Be(final.GenerationId);
            ReadGeneration(seedStore.StationSyncSettingsPath).Should().Be(final.GenerationId);
            ReadSharedToken(seedStore.StudioSettingsPath, "StationIngress")
                .Should().Be(ReadSharedToken(seedStore.StationSyncSettingsPath, "StationSync"));
        }
        finally
        {
            releaseFirst.Set();
        }
    }

    [Fact]
    public void CandidatePermissionFailure_ShouldReturnStructuredFailureAndKeepPreviousGeneration()
    {
        var seedStore = new StationCommunicationSettingsStore(_root);
        var running = CreateRunningIngress();
        seedStore.SaveSettings(
            new StationCommunicationSettingsUpdateRequest { Mode = "LocalLoopback", Port = 5120 },
            running).Success.Should().BeTrue();
        var previousStudio = File.ReadAllBytes(seedStore.StudioSettingsPath);
        var previousStation = File.ReadAllBytes(seedStore.StationSyncSettingsPath);

        var fault = new DelegateFaultInjector((stage, _) =>
        {
            if (stage == StationCommunicationPersistenceStage.StudioCandidateWrite)
            {
                throw new UnauthorizedAccessException("injected candidate permission failure");
            }
        });
        var store = CreateStore(seedStore, fault);

        var result = store.SaveSettings(
            new StationCommunicationSettingsUpdateRequest { Mode = "LocalLoopback", Port = 5121 },
            running);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("STATION_COMMUNICATION_PERMISSION_DENIED");
        result.Stage.Should().Be("candidate");
        result.Retryable.Should().BeTrue();
        result.Settings.Should().BeNull();
        File.ReadAllBytes(store.StudioSettingsPath).Should().Equal(previousStudio);
        File.ReadAllBytes(store.StationSyncSettingsPath).Should().Equal(previousStation);
    }

    [Fact]
    public void StationPublishIoFailure_ShouldRollbackBothFilesAndReturnNoFalseSuccess()
    {
        var seedStore = new StationCommunicationSettingsStore(_root);
        var running = CreateRunningIngress();
        seedStore.SaveSettings(
            new StationCommunicationSettingsUpdateRequest { Mode = "LocalLoopback", Port = 5130 },
            running).Success.Should().BeTrue();
        var previousStudio = File.ReadAllBytes(seedStore.StudioSettingsPath);
        var previousStation = File.ReadAllBytes(seedStore.StationSyncSettingsPath);
        var previousMarker = File.ReadAllBytes(seedStore.CommitMarkerPath);

        var fault = new DelegateFaultInjector((stage, _) =>
        {
            if (stage == StationCommunicationPersistenceStage.StationPublish)
            {
                throw new IOException("injected Station publish failure");
            }
        });
        var store = CreateStore(seedStore, fault);

        var result = store.SaveSettings(
            new StationCommunicationSettingsUpdateRequest { Mode = "LanController", Port = 5131, LanHost = "10.0.0.31" },
            running);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("STATION_COMMUNICATION_IO_FAILED");
        result.Stage.Should().Be("station-publish");
        File.ReadAllBytes(store.StudioSettingsPath).Should().Equal(previousStudio);
        File.ReadAllBytes(store.StationSyncSettingsPath).Should().Equal(previousStation);
        File.ReadAllBytes(store.CommitMarkerPath).Should().Equal(previousMarker);
    }

    [Fact]
    public void CandidateInterruptionBeforeIntent_RestartShouldKeepPreviousCompleteGeneration()
    {
        var seedStore = new StationCommunicationSettingsStore(_root);
        var running = CreateRunningIngress();
        var seed = seedStore.SaveSettings(
            new StationCommunicationSettingsUpdateRequest { Mode = "LocalLoopback", Port = 5140 },
            running);
        seed.Success.Should().BeTrue();
        var previousGeneration = seed.Settings!.GenerationId;

        var fault = new DelegateFaultInjector((stage, _) =>
        {
            if (stage == StationCommunicationPersistenceStage.StationCandidateWrite)
            {
                throw new StationCommunicationPersistenceInterruptionException(stage.ToString());
            }
        });
        var interruptedStore = CreateStore(seedStore, fault);

        var interrupted = interruptedStore.SaveSettings(
            new StationCommunicationSettingsUpdateRequest { Mode = "LocalLoopback", Port = 5141 },
            running);
        interrupted.Success.Should().BeFalse();
        interrupted.ErrorCode.Should().Be("STATION_COMMUNICATION_INTERRUPTED");

        var restarted = new StationCommunicationSettingsStore(
            seedStore.StudioSettingsPath,
            seedStore.StationSyncSettingsPath);
        var recovered = restarted.GetSettings(running);

        recovered.GenerationId.Should().Be(previousGeneration);
        recovered.Port.Should().Be(5140);
        ReadGeneration(restarted.StudioSettingsPath).Should().Be(previousGeneration);
        ReadGeneration(restarted.StationSyncSettingsPath).Should().Be(previousGeneration);
    }

    [Fact]
    public void HalfCommitInterruption_RestartShouldRollForwardOneCompleteGeneration()
    {
        var seedStore = new StationCommunicationSettingsStore(_root);
        var running = CreateRunningIngress();
        var seed = seedStore.SaveSettings(
            new StationCommunicationSettingsUpdateRequest { Mode = "LocalLoopback", Port = 5150 },
            running);
        seed.Success.Should().BeTrue();
        var previousGeneration = seed.Settings!.GenerationId;

        var fault = new DelegateFaultInjector((stage, _) =>
        {
            if (stage == StationCommunicationPersistenceStage.StudioPublished)
            {
                throw new StationCommunicationPersistenceInterruptionException(stage.ToString());
            }
        });
        var interruptedStore = CreateStore(seedStore, fault);
        var interrupted = interruptedStore.SaveSettings(
            new StationCommunicationSettingsUpdateRequest
            {
                Mode = "LanController",
                Port = 5151,
                LanHost = "10.0.0.51"
            },
            running);

        interrupted.Success.Should().BeFalse();
        interrupted.ErrorCode.Should().Be("STATION_COMMUNICATION_INTERRUPTED");
        var intendedGeneration = ReadGeneration(seedStore.StudioSettingsPath);
        intendedGeneration.Should().NotBe(previousGeneration);
        ReadGeneration(seedStore.StationSyncSettingsPath).Should().Be(previousGeneration);

        var restarted = new StationCommunicationSettingsStore(
            seedStore.StudioSettingsPath,
            seedStore.StationSyncSettingsPath);
        var recovered = restarted.GetSettings(running);

        recovered.GenerationId.Should().Be(intendedGeneration);
        recovered.Mode.Should().Be("LanController");
        recovered.Port.Should().Be(5151);
        ReadGeneration(restarted.StudioSettingsPath).Should().Be(intendedGeneration);
        ReadGeneration(restarted.StationSyncSettingsPath).Should().Be(intendedGeneration);
    }

    [Fact]
    public void HalfCommitWithMissingCandidate_RestartShouldRollbackOneCompletePreviousGeneration()
    {
        var seedStore = new StationCommunicationSettingsStore(_root);
        var running = CreateRunningIngress();
        var seed = seedStore.SaveSettings(
            new StationCommunicationSettingsUpdateRequest { Mode = "LocalLoopback", Port = 5160 },
            running);
        seed.Success.Should().BeTrue();
        var previousGeneration = seed.Settings!.GenerationId;

        var fault = new DelegateFaultInjector((stage, _) =>
        {
            if (stage == StationCommunicationPersistenceStage.StudioPublished)
            {
                throw new StationCommunicationPersistenceInterruptionException(stage.ToString());
            }
        });
        var interruptedStore = CreateStore(seedStore, fault);
        interruptedStore.SaveSettings(
            new StationCommunicationSettingsUpdateRequest { Mode = "LocalLoopback", Port = 5161 },
            running).Success.Should().BeFalse();
        var interruptedGeneration = ReadGeneration(seedStore.StudioSettingsPath);
        File.Delete(Path.Combine(
            seedStore.TransactionRootPath,
            interruptedGeneration,
            "station.candidate.json"));

        var restarted = new StationCommunicationSettingsStore(
            seedStore.StudioSettingsPath,
            seedStore.StationSyncSettingsPath);
        var recovered = restarted.GetSettings(running);

        recovered.GenerationId.Should().Be(previousGeneration);
        recovered.Port.Should().Be(5160);
        ReadGeneration(restarted.StudioSettingsPath).Should().Be(previousGeneration);
        ReadGeneration(restarted.StationSyncSettingsPath).Should().Be(previousGeneration);
    }

    [Fact]
    public void RegenerateFailure_ShouldNotReturnAnUncommittedToken()
    {
        var seedStore = new StationCommunicationSettingsStore(_root);
        var running = CreateRunningIngress();
        seedStore.SaveSettings(
            new StationCommunicationSettingsUpdateRequest { Mode = "LocalLoopback", Port = 5170 },
            running).Success.Should().BeTrue();
        var previousToken = seedStore.RevealToken(running).Token;
        var fault = new DelegateFaultInjector((stage, _) =>
        {
            if (stage == StationCommunicationPersistenceStage.StudioCandidateWrite)
            {
                throw new IOException("injected token candidate failure");
            }
        });
        var store = CreateStore(seedStore, fault);

        var result = store.RegenerateToken(running);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("STATION_COMMUNICATION_IO_FAILED");
        result.Token.Should().BeEmpty();
        result.TokenInfo.HasToken.Should().BeFalse();
        new StationCommunicationSettingsStore(
                seedStore.StudioSettingsPath,
                seedStore.StationSyncSettingsPath)
            .RevealToken(running).Token.Should().Be(previousToken);
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

    private static StationIngressOptions CreateRunningIngress()
    {
        return new StationIngressOptions
        {
            Enabled = false,
            ListenMode = StationIngressListenMode.Loopback,
            Port = 5000,
            SharedToken = string.Empty,
            AllowInsecureDevelopment = false
        };
    }

    private static StationCommunicationSettingsStore CreateStore(
        StationCommunicationSettingsStore paths,
        IStationCommunicationPersistenceFaultInjector faultInjector)
    {
        return new StationCommunicationSettingsStore(
            paths.StudioSettingsPath,
            paths.StationSyncSettingsPath,
            faultInjector);
    }

    private static string ReadGeneration(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement.GetProperty("GenerationId").GetString()!;
    }

    private static string ReadSharedToken(string path, string section)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement.GetProperty(section).GetProperty("SharedToken").GetString()!;
    }

    private sealed class DelegateFaultInjector : IStationCommunicationPersistenceFaultInjector
    {
        private readonly Action<StationCommunicationPersistenceStage, string> _callback;

        public DelegateFaultInjector(Action<StationCommunicationPersistenceStage, string> callback)
        {
            _callback = callback;
        }

        public void OnStage(StationCommunicationPersistenceStage stage, string generationId)
        {
            _callback(stage, generationId);
        }
    }
}
