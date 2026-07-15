using ClearVision.Product.Core.Entities;
using ClearVision.Product.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace ClearVision.Product.Tests.Services;

[TestClassification(TestDomain.General, TestPurpose.Regression, TestLane.Pr, TestEvidenceType.Contract, TestOracleType.Contract, TestResourceRequirement.None, TestExpectedDuration.Fast, TestFlakyPolicy.Blocking, "product", Suites = "ServicesRegression")]
public sealed class JsonConfigurationServiceTests
{
    [Fact]
    public async Task GetCurrent_ShouldReturnSnapshotNotMutableCache()
    {
        var root = CreateTempPath();
        try
        {
            var service = CreateService(root);
            await service.SaveAsync(new AppConfig
            {
                General = new GeneralConfig { Theme = GeneralConfig.ThemeLight }
            });

            var snapshot = service.GetCurrent();
            snapshot.General.Theme = GeneralConfig.ThemeDark;

            service.GetCurrent().General.Theme.Should().Be(GeneralConfig.ThemeLight);
        }
        finally
        {
            DeleteDirectoryIfExists(root);
        }
    }

    [Fact]
    public async Task SaveAsync_ShouldIncrementRevisionAndWriteAtomically()
    {
        var root = CreateTempPath();
        try
        {
            var service = CreateService(root);

            await service.SaveAsync(new AppConfig());
            var first = service.GetCurrent().Revision;

            await service.SaveAsync(service.GetCurrent());
            var second = service.GetCurrent().Revision;

            second.Should().Be(first + 1);
            File.Exists(Path.Combine(root, "config.json.tmp")).Should().BeFalse();
            File.ReadAllText(Path.Combine(root, "config.json")).Should().Contain("\"revision\"");
        }
        finally
        {
            DeleteDirectoryIfExists(root);
        }
    }

    [Fact]
    public async Task SaveAsync_ShouldSerializeConcurrentSaves()
    {
        var root = CreateTempPath();
        try
        {
            var service = CreateService(root);
            var saves = Enumerable.Range(0, 10)
                .Select(i => service.SaveAsync(new AppConfig
                {
                    General = new GeneralConfig
                    {
                        Theme = i % 2 == 0 ? GeneralConfig.ThemeDark : GeneralConfig.ThemeLight
                    }
                }));

            await Task.WhenAll(saves);

            service.GetCurrent().Revision.Should().Be(10);
            Action parse = () => System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "config.json"))).Dispose();
            parse.Should().NotThrow();
        }
        finally
        {
            DeleteDirectoryIfExists(root);
        }
    }

    [Fact]
    public async Task LoadAsync_ShouldMigrateLegacyMissingMaterialTimeoutDefault()
    {
        var root = CreateTempPath();
        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(root, "config.json"),
                """
                {
                  "runtime": {
                    "autoRun": false,
                    "stopOnConsecutiveNg": 0,
                    "missingMaterialTimeoutSeconds": 30,
                    "applyProtectionRules": true
                  }
                }
                """);

            var service = CreateService(root);
            var config = await service.LoadAsync();

            config.Runtime.MissingMaterialTimeoutSeconds.Should().Be(RuntimeConfig.DefaultMissingMaterialTimeoutSeconds);
            service.GetCurrent().Runtime.MissingMaterialTimeoutSeconds.Should().Be(RuntimeConfig.DefaultMissingMaterialTimeoutSeconds);
        }
        finally
        {
            DeleteDirectoryIfExists(root);
        }
    }

    [Fact]
    public async Task LoadAsync_ShouldKeepLegacyRetiredSettingsFieldsReadable()
    {
        var root = CreateTempPath();
        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(root, "config.json"),
                """
                {
                  "general": {
                    "softwareTitle": "Legacy Station",
                    "theme": "light",
                    "autoStart": true
                  },
                  "storage": {
                    "imageSavePath": "D:\\VisionData",
                    "savePolicy": "NgOnly",
                    "retentionDays": 30,
                    "minFreeSpaceGb": 17
                  },
                  "security": {
                    "passwordMinLength": 8,
                    "sessionTimeoutMinutes": 777,
                    "loginFailureLockoutCount": 6
                  }
                }
                """);

            var service = CreateService(root);
            var config = await service.LoadAsync();

            config.General.AutoStart.Should().BeTrue();
            config.Storage.MinFreeSpaceGb.Should().Be(17);
            config.Security.SessionTimeoutMinutes.Should().Be(777);
        }
        finally
        {
            DeleteDirectoryIfExists(root);
        }
    }

    private static JsonConfigurationService CreateService(string root)
    {
        return new JsonConfigurationService(
            NullLogger<JsonConfigurationService>.Instance,
            Path.Combine(root, "config.json"));
    }

    private static string CreateTempPath()
    {
        var path = Path.Combine(Path.GetTempPath(), "ClearVision.Config.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteDirectoryIfExists(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}
