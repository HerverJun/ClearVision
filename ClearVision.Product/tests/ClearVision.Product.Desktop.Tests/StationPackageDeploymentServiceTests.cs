using System.Reflection;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using ClearVision.Product.Application.DTOs;
using ClearVision.Product.Core.Decisions;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Services;
using ClearVision.Product.Desktop.Station;
using ClearVision.Product.Infrastructure.Data;
using ClearVision.Product.Runtime;
using ClearVision.Product.Runtime.Abstractions;
using ClearVision.Product.Station;
using ClearVision.Product.Station.Sync;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace ClearVision.Product.Desktop.Tests;

[TestClassification(TestDomain.Desktop, TestPurpose.Regression, TestLane.Pr, TestEvidenceType.Contract, TestOracleType.Contract, TestResourceRequirement.None, TestExpectedDuration.Fast, TestFlakyPolicy.Blocking, "desktop")]
public sealed class StationPackageDeploymentServiceTests
{
    [Fact]
    public async Task VerifyHashAsync_ShouldRejectDownloadedPackageHashMismatch()
    {
        var packagePath = Path.Combine(Path.GetTempPath(), "ClearVisionPackageHashTests", $"{Guid.NewGuid():N}.cvpkg");
        Directory.CreateDirectory(Path.GetDirectoryName(packagePath)!);
        await File.WriteAllTextAsync(packagePath, "package-bytes");

        try
        {
            var method = typeof(StationPackageDeploymentService).GetMethod(
                "VerifyHashAsync",
                BindingFlags.NonPublic | BindingFlags.Static);
            method.Should().NotBeNull();

            var wrongHash = "sha256:" + new string('0', 64);
            var task = (Task)method!.Invoke(null, [packagePath, wrongHash, CancellationToken.None])!;
            Func<Task> act = async () => await task;

            await act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("*hash does not match*");
        }
        finally
        {
            if (File.Exists(packagePath))
            {
                File.Delete(packagePath);
            }
        }
    }

    [Fact]
    public async Task VerifyHashAsync_ShouldRejectMissingHash()
    {
        var packagePath = Path.Combine(Path.GetTempPath(), "ClearVisionPackageHashTests", $"{Guid.NewGuid():N}.cvpkg");
        Directory.CreateDirectory(Path.GetDirectoryName(packagePath)!);
        await File.WriteAllTextAsync(packagePath, "package-bytes");

        try
        {
            var method = typeof(StationPackageDeploymentService).GetMethod(
                "VerifyHashAsync",
                BindingFlags.NonPublic | BindingFlags.Static);
            method.Should().NotBeNull();

            var task = (Task)method!.Invoke(null, [packagePath, null, CancellationToken.None])!;
            Func<Task> act = async () => await task;

            await act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("*missing sha256*");
        }
        finally
        {
            if (File.Exists(packagePath))
            {
                File.Delete(packagePath);
            }
        }
    }

    [Fact]
    public async Task VerifyHashAsync_ShouldAcceptMatchingSha256WithOrWithoutPrefix()
    {
        var packagePath = Path.Combine(Path.GetTempPath(), "ClearVisionPackageHashTests", $"{Guid.NewGuid():N}.cvpkg");
        Directory.CreateDirectory(Path.GetDirectoryName(packagePath)!);
        await File.WriteAllTextAsync(packagePath, "package-bytes");

        try
        {
            await using var stream = File.OpenRead(packagePath);
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream)).ToLowerInvariant();
            var method = typeof(StationPackageDeploymentService).GetMethod(
                "VerifyHashAsync",
                BindingFlags.NonPublic | BindingFlags.Static);

            await ((Task)method!.Invoke(null, [packagePath, hash, CancellationToken.None])!);
            await ((Task)method.Invoke(null, [packagePath, $"sha256:{hash}", CancellationToken.None])!);
        }
        finally
        {
            if (File.Exists(packagePath))
            {
                File.Delete(packagePath);
            }
        }
    }

    [Fact]
    public void SanitizePackageFileSegment_ShouldRemovePathSeparators()
    {
        var method = typeof(StationPackageDeploymentService).GetMethod(
            "SanitizePackageFileSegment",
            BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();

        var result = (string)method!.Invoke(null, ["../pkg\\evil"])!;

        result.Should().NotContain("..");
        result.Should().NotContain(Path.DirectorySeparatorChar.ToString());
        result.Should().NotContain(Path.AltDirectorySeparatorChar.ToString());
        result.Should().Be("_pkg_evil");
    }

    [Theory]
    [InlineData("PackageVersion")]
    [InlineData("FlowHash")]
    [InlineData("SourceProjectId")]
    [InlineData("SourceProjectRevision")]
    [InlineData("DecisionConfigurationHash")]
    [InlineData("Sha256")]
    public void ValidateExtractedPackage_ShouldRejectDeploymentIdentityMismatch(string mismatchedField)
    {
        var root = Path.Combine(Path.GetTempPath(), "ClearVisionPackageIdentityTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var sourceProjectId = Guid.NewGuid();
        var manifest = new StationPackageManifestDto
        {
            PackageId = "pkg-identity",
            PackageName = "Identity package",
            PackageVersion = "1.0",
            PackageKind = StationPackageKind.Production,
            FlowHash = "sha256:flow",
            SourceProjectId = sourceProjectId,
            SourceProjectRevision = 12,
            DecisionConfigurationHash = "sha256:decision",
            Sha256 = "sha256:artifact",
            MinStationVersion = "0.1.0"
        };
        File.WriteAllText(Path.Combine(root, "manifest.json"), JsonSerializer.Serialize(manifest));

        try
        {
            var payloadType = typeof(StationPackageDeploymentService).GetNestedType(
                "DeployPackagePayload",
                BindingFlags.NonPublic)!;
            var payload = Activator.CreateInstance(payloadType)!;
            SetProperty(payload, "PackageId", manifest.PackageId);
            SetProperty(payload, "PackageVersion", manifest.PackageVersion);
            SetProperty(payload, "PackageKind", manifest.PackageKind);
            SetProperty(payload, "FlowHash", manifest.FlowHash);
            SetProperty(payload, "SourceProjectId", manifest.SourceProjectId);
            SetProperty(payload, "SourceProjectRevision", manifest.SourceProjectRevision);
            SetProperty(payload, "DecisionConfigurationHash", manifest.DecisionConfigurationHash);
            SetProperty(payload, "Sha256", manifest.Sha256);
            SetProperty(payload, mismatchedField, mismatchedField switch
            {
                "SourceProjectId" => Guid.NewGuid(),
                "SourceProjectRevision" => 13L,
                "PackageVersion" => "2.0",
                _ => "sha256:different"
            });

            var method = typeof(StationPackageDeploymentService).GetMethod(
                "ValidateExtractedPackage",
                BindingFlags.NonPublic | BindingFlags.Static)!;
            Action act = () => method.Invoke(null, [root, root, payload]);

            act.Should().Throw<TargetInvocationException>()
                .WithInnerException<InvalidOperationException>()
                .WithMessage("*does not match*");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void RollBack_ShouldRemoveFailedActive_WhenNoLastKnownGoodExists()
    {
        var root = Path.Combine(Path.GetTempPath(), "ClearVisionPackageRollbackTests", Guid.NewGuid().ToString("N"));
        var activeRoot = Path.Combine(root, "active");
        var lastKnownGoodRoot = Path.Combine(root, "last-known-good");
        Directory.CreateDirectory(activeRoot);
        File.WriteAllText(Path.Combine(activeRoot, "package.json"), "failed-active");

        try
        {
            InvokeRollBack(activeRoot, lastKnownGoodRoot);

            Directory.Exists(activeRoot).Should().BeFalse("a failed first deployment must not leave an invalid active package on disk.");
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
    public void RollBack_ShouldRestoreLastKnownGood_WhenAvailable()
    {
        var root = Path.Combine(Path.GetTempPath(), "ClearVisionPackageRollbackTests", Guid.NewGuid().ToString("N"));
        var activeRoot = Path.Combine(root, "active");
        var lastKnownGoodRoot = Path.Combine(root, "last-known-good");
        Directory.CreateDirectory(activeRoot);
        Directory.CreateDirectory(lastKnownGoodRoot);
        File.WriteAllText(Path.Combine(activeRoot, "package.json"), "failed-active");
        File.WriteAllText(Path.Combine(lastKnownGoodRoot, "package.json"), "last-known-good");

        try
        {
            InvokeRollBack(activeRoot, lastKnownGoodRoot);

            File.ReadAllText(Path.Combine(activeRoot, "package.json")).Should().Be("last-known-good");
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
    public async Task VirtualStation_ExportRegisterDownloadDeployTerminalAndActiveIdentity_ShouldCloseLoop()
    {
        var root = Path.Combine(Path.GetTempPath(), "ClearVisionVirtualStationDeploymentTests", Guid.NewGuid().ToString("N"));
        var dbPath = Path.Combine(root, "vision.db");
        Directory.CreateDirectory(root);
        string? registeredPackageDirectory = null;

        try
        {
            var project = CreateProjectWithCalibrationAsset(
                "virtual-station-deploy",
                "asset-virtual-station",
                "bundle-virtual-station");
            var exporter = new RuntimePackageExporter(
                new ClearVision.Product.Infrastructure.Services.OperatorFactory(),
                NullLogger<RuntimePackageExporter>.Instance);
            var export = await exporter.ExportAsync(new RuntimePackageExportRequest
            {
                TargetRootDirectory = Path.Combine(root, "exports"),
                Project = project
            });

            await using var provider = new ServiceCollection()
                .AddLogging()
                .AddDbContext<VisionDbContext>(options => options.UseSqlite($"Data Source={dbPath}"))
                .AddSingleton<StationPackageStore>()
                .BuildServiceProvider();
            await using (var scope = provider.CreateAsyncScope())
            {
                await scope.ServiceProvider.GetRequiredService<VisionDbContext>().Database.EnsureCreatedAsync();
            }

            var packageStore = provider.GetRequiredService<StationPackageStore>();
            var package = await packageStore.ImportRuntimePackageAsync(
                export.PackageRootPath,
                "virtual-studio-admin",
                CancellationToken.None);
            packageStore.TryGetPackageFileForDownload(package.PackageId, out var packageFile).Should().BeTrue();
            registeredPackageDirectory = Path.GetDirectoryName(packageFile);

            using var httpClient = new HttpClient(new PackageDownloadHandler(packageFile));
            await using var runtimeHost = new RuntimeHost(
                Substitute.For<IFlowExecutionService>(),
                new RuntimePackageLoader(new RuntimePackageValidator(), NullLogger<RuntimePackageLoader>.Instance),
                new RuntimeResultNormalizer(),
                NullLogger<RuntimeHost>.Instance);
            var settingsStore = new StationLocalSettingsStore(Path.Combine(root, "station-settings"));
            var deployment = new StationPackageDeploymentService(
                Options.Create(new StationSyncOptions
                {
                    StudioBaseUrl = "http://studio.virtual",
                    PackageDirectory = Path.Combine(root, "station-packages")
                }),
                runtimeHost,
                settingsStore,
                new StationSiteProfileStore(Path.Combine(root, "station-profiles")),
                NullLogger<StationPackageDeploymentService>.Instance,
                httpClient);

            var payloadJson = JsonSerializer.Serialize(new
            {
                packageId = package.PackageId,
                packageName = package.PackageName,
                packageVersion = package.PackageVersion,
                packageKind = package.PackageKind,
                sha256 = package.Sha256,
                flowHash = package.FlowHash,
                sourceProjectId = package.SourceProjectId,
                sourceProjectRevision = package.SourceProjectRevision,
                decisionConfigurationHash = package.DecisionConfigurationHash,
                downloadUrl = $"/api/station-packages/{Uri.EscapeDataString(package.PackageId)}/download"
            });
            var centralStore = new StationCentralStore(
                provider.GetRequiredService<IServiceScopeFactory>(),
                NullLogger<StationCentralStore>.Instance);
            var created = centralStore.CreateCommand(
                "station-virtual",
                StationCommandType.DeployPackage,
                payloadJson,
                "virtual-studio-admin",
                TimeSpan.FromMinutes(30),
                "virtual-deploy-request-1");
            var delivered = centralStore.PollCommand("station-virtual");
            delivered.Should().NotBeNull();
            centralStore.ReportCommandResult(BuildCommandResult(created, StationCommandStatus.Accepted, 0));
            centralStore.ReportCommandResult(BuildCommandResult(created, StationCommandStatus.Running, 50));

            var deploymentMessage = await deployment.DeployAsync(delivered!.PayloadJson, CancellationToken.None);
            var succeeded = centralStore.ReportCommandResult(
                BuildCommandResult(created, StationCommandStatus.Succeeded, 100, deploymentMessage));
            var snapshot = runtimeHost.GetSnapshot();
            var identity = new StationIdentityResolver(settingsStore).GetOrCreate();
            var registry = new StationRegistryService(
                Options.Create(new StationIngressOptions()),
                NullLogger<StationRegistryService>.Instance,
                centralStore);
            registry.UpsertRegistration("virtual-connection", new StationRegistrationDto
            {
                StationId = "station-virtual",
                MachineName = "VIRTUAL-STATION",
                StationVersion = "1.0.0",
                RuntimeVersion = "1.0.0",
                ClientVersion = "1.0.0",
                CurrentPackageId = snapshot.PackageId,
                CurrentPackageName = snapshot.PackageName,
                CurrentPackageVersion = snapshot.PackageVersion,
                CurrentPackageSha256 = identity.CurrentPackageSha256,
                SourceProjectId = snapshot.SourceProjectId,
                SourceProjectRevision = snapshot.SourceProjectRevision
            });
            registry.UpsertHeartbeat("virtual-connection", new StationHeartbeatDto
            {
                StationId = "station-virtual",
                RuntimeState = StationRuntimeState.Idle,
                CurrentPackageId = snapshot.PackageId,
                CurrentPackageName = snapshot.PackageName,
                CurrentPackageVersion = snapshot.PackageVersion,
                CurrentPackageSha256 = identity.CurrentPackageSha256,
                SourceProjectId = snapshot.SourceProjectId,
                SourceProjectRevision = snapshot.SourceProjectRevision,
                PackageFlowHash = snapshot.PackageFlowHash,
                DecisionConfigurationHash = snapshot.DecisionConfigurationHash
            });
            var active = registry.GetStation("station-virtual");

            succeeded.Should().NotBeNull();
            succeeded!.Status.Should().Be(StationCommandStatus.Succeeded);
            centralStore.GetCommands("station-virtual", 10).Should().ContainSingle(command =>
                command.CommandId == created.CommandId && command.Status == StationCommandStatus.Succeeded);
            active.Should().NotBeNull();
            active!.PackageId.Should().Be(package.PackageId);
            active.PackageVersion.Should().Be(package.PackageVersion);
            active.PackageSha256.Should().Be(package.Sha256);
            active.SourceProjectId.Should().Be(package.SourceProjectId);
            active.SourceProjectRevision.Should().Be(package.SourceProjectRevision);
            active.PackageFlowHash.Should().Be(package.FlowHash);
            active.DecisionConfigurationHash.Should().Be(package.DecisionConfigurationHash);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
            if (!string.IsNullOrWhiteSpace(registeredPackageDirectory) && Directory.Exists(registeredPackageDirectory))
            {
                Directory.Delete(registeredPackageDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task LoadPackageWithLocalProfileAsync_ShouldApplyPersistedSiteProfile()
    {
        var root = Path.Combine(Path.GetTempPath(), "ClearVisionPackageProfileLoadTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var operatorId = Guid.NewGuid();
            var decisionPortId = Guid.NewGuid();
            var exporter = new RuntimePackageExporter(
                new ClearVision.Product.Infrastructure.Services.OperatorFactory(),
                NullLogger<RuntimePackageExporter>.Instance);
            var export = await exporter.ExportAsync(new RuntimePackageExportRequest
            {
                TargetRootDirectory = root,
                Project = new ProjectDto
                {
                    Id = Guid.NewGuid(),
                    Name = "profile-load",
                    Flow = new OperatorFlowDto
                    {
                        Id = Guid.NewGuid(),
                        Name = "main",
                        DecisionConfiguration = new DecisionConfiguration
                        {
                            FinalDecisionBinding = new FinalDecisionBinding
                            {
                                SourceOperatorId = operatorId,
                                SourceOutputPortId = decisionPortId,
                                SourceOutputName = "IsMatch",
                                DataType = DecisionValueType.Boolean,
                                Rule = DecisionInterpretationRule.Boolean,
                                TrueMeansOk = true
                            }
                        },
                        Operators =
                        [
                            new OperatorDto
                            {
                                Id = operatorId,
                                Name = "TemplateMatch",
                                Type = OperatorType.TemplateMatching,
                                OutputPorts =
                                [
                                    new PortDto
                                    {
                                        Id = decisionPortId,
                                        Name = "IsMatch",
                                        Direction = PortDirection.Output,
                                        DataType = PortDataType.Boolean
                                    }
                                ],
                                Parameters =
                                [
                                    new ParameterDto
                                    {
                                        Id = Guid.NewGuid(),
                                        Name = "threshold",
                                        DisplayName = "Match threshold",
                                        DataType = "double",
                                        Value = 0.8d,
                                        DefaultValue = 0.8d,
                                        MinValue = 0.0d,
                                        MaxValue = 1.0d,
                                        IsRequired = true
                                    }
                                ]
                            }
                        ]
                    }
                }.WithStringDecisionBinding()
            });

            var loader = new RuntimePackageLoader(new RuntimePackageValidator(), NullLogger<RuntimePackageLoader>.Instance);
            var package = await loader.LoadAsync(export.PackageRootPath);
            var siteProfileStore = new StationSiteProfileStore(Path.Combine(root, "station"));
            var parameterId = $"node.{operatorId:D}.threshold";
            siteProfileStore.Save(package, new RuntimeSiteProfile
            {
                PackageId = package.Manifest.PackageId,
                FlowHash = package.Manifest.FlowHash,
                Overrides =
                [
                    new RuntimeParameterOverride
                    {
                        ParameterId = parameterId,
                        Value = JsonSerializer.SerializeToElement(0.9d)
                    }
                ]
            });

            await using var runtimeHost = new RuntimeHost(
                Substitute.For<IFlowExecutionService>(),
                loader,
                new RuntimeResultNormalizer(),
                NullLogger<RuntimeHost>.Instance);
            var service = new StationPackageDeploymentService(
                Options.Create(new StationSyncOptions
                {
                    PackageDirectory = Path.Combine(root, "packages"),
                    StudioHubUrl = "http://localhost/hubs/station-ingest"
                }),
                runtimeHost,
                new StationLocalSettingsStore(Path.Combine(root, "settings")),
                siteProfileStore,
                NullLogger<StationPackageDeploymentService>.Instance);

            var method = typeof(StationPackageDeploymentService).GetMethod(
                "LoadPackageWithLocalProfileAsync",
                BindingFlags.NonPublic | BindingFlags.Instance);
            method.Should().NotBeNull();
            await ((Task)method!.Invoke(service, [export.PackageRootPath, CancellationToken.None])!);

            var activeProfile = runtimeHost.ActiveSiteProfile;
            activeProfile.Should().NotBeNull();
            activeProfile!.Overrides.Should().ContainSingle();
            activeProfile.Overrides.Single().ParameterId.Should().Be(parameterId);
            activeProfile.Overrides.Single().Value.GetDouble().Should().Be(0.9d);
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
    public async Task LoadPackageWithLocalProfileAsync_ShouldLoadRuntimePackageCalibrationAssets()
    {
        var root = Path.Combine(Path.GetTempPath(), "ClearVisionPackageAssetLoadTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            const string assetId = "asset-station-load";
            const string bundleId = "bundle-station-load";
            var exporter = new RuntimePackageExporter(
                new ClearVision.Product.Infrastructure.Services.OperatorFactory(),
                NullLogger<RuntimePackageExporter>.Instance);
            var export = await exporter.ExportAsync(new RuntimePackageExportRequest
            {
                TargetRootDirectory = root,
                Project = CreateProjectWithCalibrationAsset("station-asset-load", assetId, bundleId)
            });

            var loader = new RuntimePackageLoader(new RuntimePackageValidator(), NullLogger<RuntimePackageLoader>.Instance);
            await using var runtimeHost = new RuntimeHost(
                Substitute.For<IFlowExecutionService>(),
                loader,
                new RuntimeResultNormalizer(),
                NullLogger<RuntimeHost>.Instance);
            var service = new StationPackageDeploymentService(
                Options.Create(new StationSyncOptions
                {
                    PackageDirectory = Path.Combine(root, "packages"),
                    StudioHubUrl = "http://localhost/hubs/station-ingest"
                }),
                runtimeHost,
                new StationLocalSettingsStore(Path.Combine(root, "settings")),
                new StationSiteProfileStore(Path.Combine(root, "station")),
                NullLogger<StationPackageDeploymentService>.Instance);

            var method = typeof(StationPackageDeploymentService).GetMethod(
                "LoadPackageWithLocalProfileAsync",
                BindingFlags.NonPublic | BindingFlags.Instance);
            method.Should().NotBeNull();
            await ((Task)method!.Invoke(service, [export.PackageRootPath, CancellationToken.None])!);

            runtimeHost.LoadedPackage.Should().NotBeNull();
            runtimeHost.LoadedPackage!.AssetContext.TryGetCalibrationBundleByAssetId(assetId, out var asset).Should().BeTrue();
            asset.BundleId.Should().Be(bundleId);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static void InvokeRollBack(string activeRoot, string lastKnownGoodRoot)
    {
        var method = typeof(StationPackageDeploymentService).GetMethod(
            "RollBack",
            BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();
        method!.Invoke(null, [activeRoot, lastKnownGoodRoot]);
    }

    private static StationCommandResultDto BuildCommandResult(
        StationCommandDto command,
        StationCommandStatus status,
        int progress,
        string? message = null)
    {
        return new StationCommandResultDto
        {
            CommandId = command.CommandId,
            StationId = command.StationId,
            Status = status,
            ProgressPercent = progress,
            Message = message ?? status.ToString()
        };
    }

    private static void SetProperty(object target, string propertyName, object? value)
    {
        target.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance)!
            .SetValue(target, value);
    }

    private sealed class PackageDownloadHandler(string packagePath) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            request.RequestUri.Should().Be(new Uri("http://studio.virtual/api/station-packages/" +
                Uri.EscapeDataString(Path.GetFileNameWithoutExtension(packagePath)) + "/download"));
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(File.OpenRead(packagePath))
            };
            return Task.FromResult(response);
        }
    }

    private static ProjectDto CreateProjectWithCalibrationAsset(string name, string assetId, string bundleId)
    {
        var revision = 3;
        var payload = CreateCalibrationPayload(bundleId);
        var decisionOperatorId = Guid.NewGuid();
        var decisionPortId = Guid.NewGuid();
        return new ProjectDto
        {
            Id = Guid.NewGuid(),
            Name = name,
            PersistenceRevision = revision,
            Flow = new OperatorFlowDto
            {
                Id = Guid.NewGuid(),
                Name = "asset-load-flow",
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
                Operators =
                [
                    new OperatorDto
                    {
                        Id = decisionOperatorId,
                        Name = "Decision",
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
                        ]
                    }
                ]
            },
            Assets = new ProjectAssetsDto
            {
                CalibrationAssets =
                [
                    new ProjectCalibrationAssetDto
                    {
                        AssetId = assetId,
                        Kind = "CalibrationBundleV2",
                        Version = "2.0",
                        Producer = "StationPackageDeploymentServiceTests",
                        SourceDraftSessionId = "station-load",
                        ImageIdentity = "image:none",
                        ContentHash = ProjectAssetJson.ComputePayloadHash(payload),
                        ProjectRevision = revision,
                        CreatedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1),
                        UpdatedAtUtc = DateTimeOffset.UtcNow,
                        Status = "authority",
                        Payload = payload
                    }
                ]
            }
        }.WithStringDecisionBinding();
    }

    private static JsonElement CreateCalibrationPayload(string bundleId) =>
        JsonSerializer.SerializeToElement(
            new
            {
                schemaVersion = 2,
                bundleId,
                calibrationKind = "rigidTransform2D",
                transformModel = "scaleOffset",
                sourceFrame = "image",
                targetFrame = "world",
                unit = "mm",
                transform2D = new
                {
                    model = "scaleOffset",
                    matrix = new[]
                    {
                        new[] { 0.02d, 0.0d, 0.0d },
                        new[] { 0.0d, 0.02d, 0.0d }
                    }
                },
                quality = new
                {
                    accepted = true,
                    meanError = 0.05d,
                    maxError = 0.09d,
                    inlierCount = 8,
                    totalSampleCount = 8,
                    diagnostics = Array.Empty<string>()
                },
                producerOperator = "StationPackageDeploymentServiceTests"
            });
}
