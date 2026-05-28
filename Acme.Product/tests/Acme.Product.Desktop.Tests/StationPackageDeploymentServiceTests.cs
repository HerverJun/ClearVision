using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Acme.Product.Application.DTOs;
using Acme.Product.Core.Enums;
using Acme.Product.Core.Services;
using Acme.Product.Runtime;
using Acme.Product.Runtime.Abstractions;
using Acme.Product.Station;
using Acme.Product.Station.Sync;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Acme.Product.Desktop.Tests;

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
    public async Task LoadPackageWithLocalProfileAsync_ShouldApplyPersistedSiteProfile()
    {
        var root = Path.Combine(Path.GetTempPath(), "ClearVisionPackageProfileLoadTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var operatorId = Guid.NewGuid();
            var exporter = new RuntimePackageExporter(
                new Acme.Product.Infrastructure.Services.OperatorFactory(),
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
}
