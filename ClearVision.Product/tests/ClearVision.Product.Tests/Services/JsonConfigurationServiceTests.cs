using System.Text.Json;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Interfaces;
using ClearVision.Product.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace ClearVision.Product.Tests.Services;

[TestClassification(TestDomain.General, TestPurpose.Regression, TestLane.Pr, TestEvidenceType.Contract, TestOracleType.Contract, TestResourceRequirement.None, TestExpectedDuration.Fast, TestFlakyPolicy.Blocking, "product", Suites = "ServicesRegression")]
public sealed class JsonConfigurationServiceTests
{
    [Fact]
    public async Task ReadAsync_WhenFileIsMissing_ShouldInitializeDefaultsOnce()
    {
        await WithTempServiceAsync(async (service, path, _) =>
        {
            var first = await service.ReadAsync();
            var bytes = await File.ReadAllBytesAsync(path);
            var second = await service.ReadAsync();

            first.Status.Should().Be(AppConfigReadStatus.Initialized);
            first.Config!.Revision.Should().Be(0);
            second.Status.Should().Be(AppConfigReadStatus.Healthy);
            (await File.ReadAllBytesAsync(path)).Should().Equal(bytes);
        });
    }

    [Fact]
    public async Task ReadAsync_WhenFileIsValid_ShouldLoadAndCacheSnapshot()
    {
        await WithTempServiceAsync(async (service, path, _) =>
        {
            await WriteConfigAsync(path, new AppConfig
            {
                Revision = 7,
                General = new GeneralConfig { Theme = GeneralConfig.ThemeLight }
            });

            var read = await service.ReadAsync();
            read.Status.Should().Be(AppConfigReadStatus.Healthy);
            read.Config!.Revision.Should().Be(7);
            read.Config.General.Theme.Should().Be(GeneralConfig.ThemeLight);

            read.Config.General.Theme = GeneralConfig.ThemeDark;
            service.GetCurrent().General.Theme.Should().Be(GeneralConfig.ThemeLight);
        });
    }

    [Theory]
    [InlineData("{broken", JsonConfigurationService.ErrorMalformed)]
    [InlineData("", JsonConfigurationService.ErrorEmpty)]
    [InlineData("   \r\n", JsonConfigurationService.ErrorEmpty)]
    public async Task ReadAsync_WhenContentIsInvalidWithoutLastGood_ShouldFailClosed(
        string content,
        string expectedCode)
    {
        await WithTempServiceAsync(async (service, path, _) =>
        {
            await File.WriteAllTextAsync(path, content);
            var originalBytes = await File.ReadAllBytesAsync(path);

            var read = await service.ReadAsync();

            read.Status.Should().Be(AppConfigReadStatus.Unavailable);
            read.Config.Should().BeNull();
            read.ErrorCode.Should().Be(expectedCode);
            (await File.ReadAllBytesAsync(path)).Should().Equal(originalBytes);
            FluentActions.Invoking(service.GetCurrent).Should().Throw<AppConfigUnavailableException>();
        });
    }

    [Fact]
    public async Task ReadAsync_WhenMalformedAfterHealthyLoad_ShouldReturnExplicitLastGoodWithoutChangingActiveBytes()
    {
        await WithTempServiceAsync(async (service, path, _) =>
        {
            await WriteConfigAsync(path, new AppConfig
            {
                Revision = 4,
                General = new GeneralConfig { SoftwareTitle = "Last Good" }
            });
            (await service.ReadAsync()).IsHealthy.Should().BeTrue();
            await File.WriteAllTextAsync(path, "{malformed");
            var malformedBytes = await File.ReadAllBytesAsync(path);

            var degraded = await service.ReadAsync();

            degraded.Status.Should().Be(AppConfigReadStatus.DegradedLastGood);
            degraded.HasLastGood.Should().BeTrue();
            degraded.ErrorCode.Should().Be(JsonConfigurationService.ErrorMalformed);
            degraded.Config!.Revision.Should().Be(4);
            degraded.Config.General.SoftwareTitle.Should().Be("Last Good");
            (await File.ReadAllBytesAsync(path)).Should().Equal(malformedBytes);
        });
    }

    [Theory]
    [InlineData(InjectedReadFailure.AccessDenied, JsonConfigurationService.ErrorAccessDenied)]
    [InlineData(InjectedReadFailure.Locked, JsonConfigurationService.ErrorLocked)]
    [InlineData(InjectedReadFailure.Io, JsonConfigurationService.ErrorIo)]
    public async Task ReadAsync_WhenFileReadFails_ShouldReturnStableDegradedCodeAndPreserveBytes(
        InjectedReadFailure failure,
        string expectedCode)
    {
        await WithTempServiceAsync(async (service, path, store) =>
        {
            await WriteConfigAsync(path, new AppConfig { Revision = 2 });
            (await service.ReadAsync()).IsHealthy.Should().BeTrue();
            var bytes = await File.ReadAllBytesAsync(path);
            store.ReadFailure = failure;

            var degraded = await service.ReadAsync();

            degraded.Status.Should().Be(AppConfigReadStatus.DegradedLastGood);
            degraded.ErrorCode.Should().Be(expectedCode);
            degraded.Config!.Revision.Should().Be(2);
            (await File.ReadAllBytesAsync(path)).Should().Equal(bytes);
        });
    }

    [Fact]
    public async Task MutateAsync_ShouldPreserveAbsentSectionsAndIncrementRevisionExactlyOnce()
    {
        await WithTempServiceAsync(async (service, _, _) =>
        {
            var initial = await service.ReadAsync();
            initial.Config!.Communication.S7.IpAddress = "not-authoritative";
            var seed = await service.MutateAsync(0, candidate =>
            {
                candidate.Communication.S7.IpAddress = "10.0.0.8";
                candidate.Storage.RetentionDays = 91;
            });

            var result = await service.MutateAsync(seed.ActualRevision!.Value, candidate =>
                candidate.General.Theme = GeneralConfig.ThemeLight);

            result.Status.Should().Be(AppConfigMutationStatus.Applied);
            result.ActualRevision.Should().Be(seed.ActualRevision + 1);
            result.Config!.Communication.S7.IpAddress.Should().Be("10.0.0.8");
            result.Config.Storage.RetentionDays.Should().Be(91);
            result.Config.General.Theme.Should().Be(GeneralConfig.ThemeLight);
        });
    }

    [Fact]
    public async Task MutateAsync_WhenPatchIsNoOp_ShouldNotIncrementRevisionOrRewriteFile()
    {
        await WithTempServiceAsync(async (service, path, _) =>
        {
            var initial = await service.ReadAsync();
            var bytes = await File.ReadAllBytesAsync(path);

            var result = await service.MutateAsync(initial.Config!.Revision, candidate =>
                candidate.General.Theme = GeneralConfig.ThemeDark);

            result.Status.Should().Be(AppConfigMutationStatus.NoChange);
            result.ActualRevision.Should().Be(0);
            (await File.ReadAllBytesAsync(path)).Should().Equal(bytes);
        });
    }

    [Fact]
    public async Task MutateAsync_WhenRevisionIsStale_ShouldReturnConflictWithoutSideEffects()
    {
        await WithTempServiceAsync(async (service, path, _) =>
        {
            await service.ReadAsync();
            var first = await service.MutateAsync(0, candidate => candidate.Storage.RetentionDays = 45);
            var bytes = await File.ReadAllBytesAsync(path);

            var stale = await service.MutateAsync(0, candidate => candidate.Storage.RetentionDays = 10);

            stale.Status.Should().Be(AppConfigMutationStatus.RevisionConflict);
            stale.ErrorCode.Should().Be(JsonConfigurationService.ErrorRevisionConflict);
            stale.ActualRevision.Should().Be(first.ActualRevision);
            service.GetCurrent().Storage.RetentionDays.Should().Be(45);
            (await File.ReadAllBytesAsync(path)).Should().Equal(bytes);
        });
    }

    [Fact]
    public async Task MutateAsync_ShouldSerializeConcurrentWritersUnderOneGate()
    {
        await WithTempServiceAsync(async (service, _, _) =>
        {
            await service.ReadAsync();
            var first = service.MutateAsync(0, candidate => candidate.Storage.RetentionDays = 10);
            var second = service.MutateAsync(0, candidate => candidate.Storage.RetentionDays = 20);

            var results = await Task.WhenAll(first, second);

            results.Should().ContainSingle(result => result.Status == AppConfigMutationStatus.Applied);
            results.Should().ContainSingle(result => result.Status == AppConfigMutationStatus.RevisionConflict);
            service.GetCurrent().Revision.Should().Be(1);
        });
    }

    [Fact]
    public async Task MutateAsync_WhenValidationFails_ShouldReturnValidationResultWithoutPersisting()
    {
        await WithTempServiceAsync(async (service, path, _) =>
        {
            await service.ReadAsync();
            var bytes = await File.ReadAllBytesAsync(path);

            var result = await service.MutateAsync(
                0,
                candidate => candidate.Storage.RetentionDays = -1,
                _ => [new AppConfigValidationError("storage.retentionDays", "Invalid retention.")]);

            result.Status.Should().Be(AppConfigMutationStatus.ValidationFailed);
            result.ValidationErrors.Should().ContainSingle();
            service.GetCurrent().Revision.Should().Be(0);
            (await File.ReadAllBytesAsync(path)).Should().Equal(bytes);
        });
    }

    [Fact]
    public async Task MutateAsync_WhenCandidateReplaceFails_ShouldPreserveFileCacheAndRevision()
    {
        await WithTempServiceAsync(async (service, path, store) =>
        {
            await service.ReadAsync();
            var bytes = await File.ReadAllBytesAsync(path);
            store.FailReplace = true;

            var result = await service.MutateAsync(0, candidate => candidate.Storage.RetentionDays = 88);

            result.Status.Should().Be(AppConfigMutationStatus.StorageFailure);
            result.ErrorCode.Should().Be(JsonConfigurationService.ErrorPersist);
            service.GetCurrent().Revision.Should().Be(0);
            service.GetCurrent().Storage.RetentionDays.Should().Be(30);
            (await File.ReadAllBytesAsync(path)).Should().Equal(bytes);
            Directory.GetFiles(Path.GetDirectoryName(path)!, "*.candidate").Should().BeEmpty();
        });
    }

    [Fact]
    public async Task MutateAndApplyAsync_WhenApplyFails_ShouldRestorePreviousDurableSnapshot()
    {
        await WithTempServiceAsync(async (service, path, _) =>
        {
            await service.ReadAsync();
            var bytes = await File.ReadAllBytesAsync(path);
            var rollbackCalled = false;

            var result = await service.MutateAndApplyAsync(
                0,
                candidate => candidate.Cameras = [new CameraBindingConfig { Id = "new", SerialNumber = "SN-NEW" }],
                validate: null,
                (_, _) => throw new InvalidOperationException("apply failed"),
                (_, _) =>
                {
                    rollbackCalled = true;
                    return Task.CompletedTask;
                });

            result.Status.Should().Be(AppConfigMutationStatus.ApplyFailed);
            rollbackCalled.Should().BeTrue();
            service.GetCurrent().Revision.Should().Be(0);
            service.GetCurrent().Cameras.Should().BeEmpty();
            (await File.ReadAllBytesAsync(path)).Should().Equal(bytes);
        });
    }

    [Fact]
    public async Task MutateAndApplyAsync_WhenCandidateIsUnchanged_ShouldReconcileRuntimeWithoutRewritingFile()
    {
        await WithTempServiceAsync(async (service, path, _) =>
        {
            var initial = await service.ReadAsync();
            var bytes = await File.ReadAllBytesAsync(path);
            var applyCount = 0;

            var result = await service.MutateAndApplyAsync(
                initial.Config!.Revision,
                candidate => candidate.General.Theme = GeneralConfig.ThemeDark,
                validate: null,
                (_, _) =>
                {
                    applyCount++;
                    return Task.CompletedTask;
                });

            result.Status.Should().Be(AppConfigMutationStatus.NoChange);
            result.ActualRevision.Should().Be(0);
            applyCount.Should().Be(1);
            (await File.ReadAllBytesAsync(path)).Should().Equal(bytes);
        });
    }

    [Fact]
    public async Task MutateAndApplyAsync_WhenRuntimeRollbackFails_ShouldFenceFutureMutations()
    {
        await WithTempServiceAsync(async (service, path, _) =>
        {
            await service.ReadAsync();
            var bytes = await File.ReadAllBytesAsync(path);

            var result = await service.MutateAndApplyAsync(
                0,
                candidate => candidate.Cameras = [new CameraBindingConfig { Id = "new", SerialNumber = "SN-NEW" }],
                validate: null,
                (_, _) => throw new InvalidOperationException("apply failed"),
                (_, _) => throw new InvalidOperationException("rollback failed"));
            var retry = await service.MutateAsync(0, candidate => candidate.Storage.RetentionDays = 60);

            result.Status.Should().Be(AppConfigMutationStatus.Fenced);
            result.ErrorCode.Should().Be(JsonConfigurationService.ErrorFenced);
            retry.Status.Should().Be(AppConfigMutationStatus.Fenced);
            service.GetCurrent().Revision.Should().Be(0);
            service.GetCurrent().Cameras.Should().BeEmpty();
            (await File.ReadAllBytesAsync(path)).Should().Equal(bytes);
        });
    }

    [Fact]
    public async Task LoadAsync_ShouldMigrateLegacyMissingMaterialTimeoutDefault()
    {
        await WithTempServiceAsync(async (service, path, _) =>
        {
            await File.WriteAllTextAsync(path, """
                {
                  "runtime": {
                    "missingMaterialTimeoutSeconds": 30
                  }
                }
                """);

            var config = await service.LoadAsync();

            config.Runtime.MissingMaterialTimeoutSeconds.Should().Be(RuntimeConfig.DefaultMissingMaterialTimeoutSeconds);
        });
    }

    private static async Task WithTempServiceAsync(
        Func<JsonConfigurationService, string, FaultInjectingFileStore, Task> action)
    {
        var root = Path.Combine(Path.GetTempPath(), "ClearVision.Config.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "config.json");
            var store = new FaultInjectingFileStore();
            var service = new JsonConfigurationService(
                NullLogger<JsonConfigurationService>.Instance,
                path,
                store);
            await action(service, path, store);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static Task WriteConfigAsync(string path, AppConfig config)
    {
        config.Normalize();
        return File.WriteAllTextAsync(path, JsonSerializer.Serialize(config, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        }));
    }

    public enum InjectedReadFailure
    {
        None,
        AccessDenied,
        Locked,
        Io
    }

    public sealed class FaultInjectingFileStore : IAppConfigFileStore
    {
        public InjectedReadFailure ReadFailure { get; set; }
        public bool FailReplace { get; set; }

        public bool Exists(string path) => PhysicalAppConfigFileStore.Instance.Exists(path);

        public Task<byte[]> ReadAllBytesAsync(string path, CancellationToken cancellationToken)
        {
            return ReadFailure switch
            {
                InjectedReadFailure.AccessDenied => Task.FromException<byte[]>(new UnauthorizedAccessException("denied")),
                InjectedReadFailure.Locked => Task.FromException<byte[]>(new SharingViolationIOException()),
                InjectedReadFailure.Io => Task.FromException<byte[]>(new IOException("io failure")),
                _ => PhysicalAppConfigFileStore.Instance.ReadAllBytesAsync(path, cancellationToken)
            };
        }

        public Task WriteAllBytesAsync(string path, byte[] bytes, CancellationToken cancellationToken) =>
            PhysicalAppConfigFileStore.Instance.WriteAllBytesAsync(path, bytes, cancellationToken);

        public void CreateDirectory(string path) => PhysicalAppConfigFileStore.Instance.CreateDirectory(path);

        public void Replace(string candidatePath, string activePath)
        {
            if (FailReplace)
            {
                throw new IOException("replace failed");
            }

            PhysicalAppConfigFileStore.Instance.Replace(candidatePath, activePath);
        }

        public void DeleteIfExists(string path) => PhysicalAppConfigFileStore.Instance.DeleteIfExists(path);
    }

    private sealed class SharingViolationIOException : IOException
    {
        public SharingViolationIOException()
            : base("locked", unchecked((int)0x80070020))
        {
        }
    }
}
