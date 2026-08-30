using ClearVision.Product.Application.DTOs;
using ClearVision.Product.Core.Cameras;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Interfaces;
using ClearVision.Product.Desktop.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace ClearVision.Product.Desktop.Tests;

[TestClassification(TestDomain.Desktop, TestPurpose.Regression, TestLane.Pr, TestEvidenceType.Contract, TestOracleType.Contract, TestResourceRequirement.None, TestExpectedDuration.Fast, TestFlakyPolicy.Blocking, "camera", Suites = "DesktopEndpoints")]
public class CameraConfigurationCoordinatorTests
{
    [Fact]
    public async Task SaveAsync_WhenPersistFails_ShouldLeaveRuntimeTriggerAndRevisionUnchanged()
    {
        var previous = Config(Binding("cam-main", "SN-OLD"));
        var authority = new InMemoryAppConfigAuthority(previous) { FailPersist = true };
        var cameraManager = Substitute.For<ICameraManager>();
        var streamCoordinator = CreateStreamCoordinator();
        var triggerService = Substitute.For<ISerialPhotoelectricTriggerInputService>();
        var sut = CreateSut(authority, cameraManager, streamCoordinator, triggerService);

        var result = await sut.SaveAsync(Request(0, Binding("cam-main", "SN-NEW")));

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("APP_CONFIG_PERSIST_FAILED");
        authority.GetCurrent().Cameras.Should().ContainSingle(binding => binding.SerialNumber == "SN-OLD");
        authority.GetCurrent().Revision.Should().Be(0);
        authority.MutationCount.Should().Be(0);
        await cameraManager.DidNotReceive().ApplyBindingsAsync(
            Arg.Any<List<CameraBindingConfig>>(),
            Arg.Any<string>());
        await streamCoordinator.DidNotReceive().ReleaseIdleStreamAsync(Arg.Any<string>());
        triggerService.DidNotReceive().ConfigureBindings(Arg.Any<IEnumerable<CameraBindingConfig>>());
    }

    [Fact]
    public async Task SaveAsync_WhenRuntimeApplyFails_ShouldRestorePersistedAndRuntimeSnapshots()
    {
        var previous = Config(Binding("cam-main", "SN-OLD"));
        var authority = new InMemoryAppConfigAuthority(previous);
        var cameraManager = Substitute.For<ICameraManager>();
        var appliedSerials = new List<string>();
        cameraManager.ApplyBindingsAsync(
                Arg.Any<List<CameraBindingConfig>>(),
                Arg.Any<string>())
            .Returns(call =>
            {
                var serial = call.ArgAt<List<CameraBindingConfig>>(0).Single().SerialNumber;
                appliedSerials.Add(serial);
                return appliedSerials.Count == 1
                    ? Task.FromException(new InvalidOperationException("injected apply failure"))
                    : Task.CompletedTask;
            });
        var streamCoordinator = CreateStreamCoordinator();
        var triggerService = Substitute.For<ISerialPhotoelectricTriggerInputService>();
        var sut = CreateSut(authority, cameraManager, streamCoordinator, triggerService);

        var result = await sut.SaveAsync(Request(0, Binding("cam-main", "SN-NEW")));

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("APP_CONFIG_RUNTIME_APPLY_FAILED");
        result.Mutation!.Status.Should().Be(AppConfigMutationStatus.ApplyFailed);
        authority.GetCurrent().Cameras.Should().ContainSingle(binding => binding.SerialNumber == "SN-OLD");
        authority.GetCurrent().Revision.Should().Be(0);
        appliedSerials.Should().Equal("SN-NEW", "SN-OLD");
        await streamCoordinator.Received(2).ReleaseIdleStreamAsync("cam-main");
        triggerService.Received(1).ConfigureBindings(
            Arg.Is<IEnumerable<CameraBindingConfig>>(bindings => bindings.Single().SerialNumber == "SN-OLD"));
    }

    [Fact]
    public async Task SaveAsync_WhenRuntimeApplyAndRollbackFail_ShouldFenceFutureMutations()
    {
        var authority = new InMemoryAppConfigAuthority(Config(Binding("cam-main", "SN-OLD")));
        var cameraManager = Substitute.For<ICameraManager>();
        cameraManager.ApplyBindingsAsync(
                Arg.Any<List<CameraBindingConfig>>(),
                Arg.Any<string>())
            .Returns(Task.FromException(new InvalidOperationException("injected runtime failure")));
        var streamCoordinator = CreateStreamCoordinator();
        var triggerService = Substitute.For<ISerialPhotoelectricTriggerInputService>();
        var sut = CreateSut(authority, cameraManager, streamCoordinator, triggerService);

        var result = await sut.SaveAsync(Request(0, Binding("cam-main", "SN-NEW")));
        var retry = await sut.SaveAsync(Request(0, Binding("cam-main", "SN-RETRY")));

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("APP_CONFIG_FENCED");
        result.Mutation!.Status.Should().Be(AppConfigMutationStatus.Fenced);
        authority.IsFenced.Should().BeTrue();
        authority.GetCurrent().Cameras.Should().ContainSingle(binding => binding.SerialNumber == "SN-OLD");
        retry.ErrorCode.Should().Be("APP_CONFIG_FENCED");
    }

    [Fact]
    public async Task SaveAsync_WhenDirectAcquisitionIsActive_ShouldReturnConflictWithZeroSideEffects()
    {
        var authority = new InMemoryAppConfigAuthority(Config(Binding("cam-main", "SN-OLD")));
        var activeCamera = Substitute.For<ICamera>();
        activeCamera.IsAcquiring.Returns(true);
        var cameraManager = Substitute.For<ICameraManager>();
        cameraManager.GetCamera("SN-OLD").Returns(activeCamera);
        var streamCoordinator = CreateStreamCoordinator();
        var triggerService = Substitute.For<ISerialPhotoelectricTriggerInputService>();
        var sut = CreateSut(authority, cameraManager, streamCoordinator, triggerService);

        var result = await sut.SaveAsync(Request(0, Binding("cam-main", "SN-NEW")));

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(CameraConfigurationCoordinator.ErrorRuntimeConflict);
        result.RuntimeConflicts.Should().ContainSingle(conflict => conflict.DirectAcquisition);
        authority.MutationCount.Should().Be(0);
        authority.GetCurrent().Cameras.Should().ContainSingle(binding => binding.SerialNumber == "SN-OLD");
        await cameraManager.DidNotReceive().ApplyBindingsAsync(
            Arg.Any<List<CameraBindingConfig>>(),
            Arg.Any<string>());
        await streamCoordinator.DidNotReceive().ReleaseIdleStreamAsync(Arg.Any<string>());
    }

    [Fact]
    public async Task ConcurrentSaveAndReset_ShouldUseOneCameraGateAndConvergeToFinalRevision()
    {
        var authority = new InMemoryAppConfigAuthority(Config(Binding("cam-main", "SN-OLD")));
        var cameraManager = Substitute.For<ICameraManager>();
        var firstApplyStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirstApply = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var appliedSerials = new List<string>();
        var activeApplies = 0;
        var maxConcurrentApplies = 0;
        var applyCount = 0;
        cameraManager.ApplyBindingsAsync(
                Arg.Any<List<CameraBindingConfig>>(),
                Arg.Any<string>())
            .Returns(call => ApplyAsync(call.ArgAt<List<CameraBindingConfig>>(0)));
        var streamCoordinator = CreateStreamCoordinator();
        var triggerService = Substitute.For<ISerialPhotoelectricTriggerInputService>();
        var sut = CreateSut(authority, cameraManager, streamCoordinator, triggerService);

        var saveTask = sut.SaveAsync(Request(0, Binding("cam-main", "SN-NEW")));
        await firstApplyStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var resetTask = sut.ResetAsync(expectedRevision: 1);
        releaseFirstApply.TrySetResult();
        var results = await Task.WhenAll(saveTask, resetTask);

        results.Should().OnlyContain(result => result.IsSuccess);
        maxConcurrentApplies.Should().Be(1);
        appliedSerials.Should().Equal("SN-NEW", string.Empty);
        authority.GetCurrent().Revision.Should().Be(2);
        authority.GetCurrent().Cameras.Should().BeEmpty();
        authority.GetCurrent().ActiveCameraId.Should().BeEmpty();
        triggerService.Received(2).ConfigureBindings(Arg.Any<IEnumerable<CameraBindingConfig>>());

        async Task ApplyAsync(List<CameraBindingConfig> bindings)
        {
            var active = Interlocked.Increment(ref activeApplies);
            maxConcurrentApplies = Math.Max(maxConcurrentApplies, active);
            var currentApply = Interlocked.Increment(ref applyCount);
            appliedSerials.Add(bindings.SingleOrDefault()?.SerialNumber ?? string.Empty);
            try
            {
                if (currentApply == 1)
                {
                    firstApplyStarted.TrySetResult();
                    await releaseFirstApply.Task.WaitAsync(TimeSpan.FromSeconds(5));
                }
            }
            finally
            {
                Interlocked.Decrement(ref activeApplies);
            }
        }
    }

    [Fact]
    public async Task SaveAsync_AfterRestartWithCommittedConfig_ShouldReconcileRuntimeAsNoOp()
    {
        var committed = Config(Binding("cam-main", "SN-COMMITTED"));
        committed.Revision = 9;
        var authority = new InMemoryAppConfigAuthority(committed);
        var cameraManager = Substitute.For<ICameraManager>();
        cameraManager.ApplyBindingsAsync(
            Arg.Any<List<CameraBindingConfig>>(),
            Arg.Any<string>()).Returns(Task.CompletedTask);
        var streamCoordinator = CreateStreamCoordinator();
        var triggerService = Substitute.For<ISerialPhotoelectricTriggerInputService>();
        var sut = CreateSut(authority, cameraManager, streamCoordinator, triggerService);

        var result = await sut.SaveAsync(Request(9, Binding("cam-main", "SN-COMMITTED")));

        result.IsSuccess.Should().BeTrue();
        result.Mutation!.Status.Should().Be(AppConfigMutationStatus.NoChange);
        result.Revision.Should().Be(9);
        authority.GetCurrent().Revision.Should().Be(9);
        await cameraManager.Received(1).ApplyBindingsAsync(
            Arg.Is<List<CameraBindingConfig>>(bindings =>
                bindings.Single().SerialNumber == "SN-COMMITTED"),
            "cam-main");
        triggerService.Received(1).ConfigureBindings(
            Arg.Is<IEnumerable<CameraBindingConfig>>(bindings =>
                bindings.Single().SerialNumber == "SN-COMMITTED"));
    }

    private static CameraConfigurationCoordinator CreateSut(
        IConfigurationService authority,
        ICameraManager cameraManager,
        ICameraFrameStreamCoordinator streamCoordinator,
        ISerialPhotoelectricTriggerInputService triggerService) =>
        new(
            authority,
            cameraManager,
            streamCoordinator,
            triggerService,
            NullLogger<CameraConfigurationCoordinator>.Instance);

    private static ICameraFrameStreamCoordinator CreateStreamCoordinator()
    {
        var streamCoordinator = Substitute.For<ICameraFrameStreamCoordinator>();
        streamCoordinator.ReleaseIdleStreamAsync(Arg.Any<string>()).Returns(Task.CompletedTask);
        return streamCoordinator;
    }

    private static UpdateCameraBindingsRequest Request(long revision, params CameraBindingConfig[] bindings) => new()
    {
        ExpectedRevision = revision,
        Bindings = bindings.ToList(),
        ActiveCameraId = bindings.FirstOrDefault()?.Id ?? string.Empty
    };

    private static AppConfig Config(params CameraBindingConfig[] bindings)
    {
        var config = new AppConfig
        {
            Cameras = bindings.ToList(),
            ActiveCameraId = bindings.FirstOrDefault()?.Id ?? string.Empty
        };
        config.Normalize();
        return config;
    }

    private static CameraBindingConfig Binding(string id, string serialNumber) => new()
    {
        Id = id,
        DisplayName = id,
        SerialNumber = serialNumber,
        TriggerMode = "Software",
        ExposureTimeUs = 5000,
        GainDb = 0
    };
}
