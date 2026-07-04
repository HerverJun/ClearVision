using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using ClearVision.Product.Application.DTOs;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Services;
using ClearVision.Product.Runtime;
using ClearVision.Product.Runtime.Abstractions;
using ClearVision.Product.Station;
using ClearVision.Product.Station.Sync;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace ClearVision.Product.Desktop.Tests;

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
    public async Task LoadPackageWithLocalProfileAsync_ShouldApplyPersistedSiteProfile()
    {
        var root = Path.Combine(Path.GetTempPath(), "ClearVisionPackageProfileLoadTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var operatorId = Guid.NewGuid();
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
                        Operators =
                        [
                            new OperatorDto
                            {
                                Id = operatorId,
                                Name = "TemplateMatch",
                                Type = OperatorType.TemplateMatching,
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
                }
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

    private static ProjectDto CreateProjectWithCalibrationAsset(string name, string assetId, string bundleId)
    {
        var revision = 3;
        var payload = CreateCalibrationPayload(bundleId);
        return new ProjectDto
        {
            Id = Guid.NewGuid(),
            Name = name,
            PersistenceRevision = revision,
            Flow = new OperatorFlowDto
            {
                Id = Guid.NewGuid(),
                Name = "asset-load-flow",
                Operators =
                [
                    new OperatorDto
                    {
                        Id = Guid.NewGuid(),
                        Name = "Result",
                        Type = OperatorType.ResultOutput
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
        };
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
