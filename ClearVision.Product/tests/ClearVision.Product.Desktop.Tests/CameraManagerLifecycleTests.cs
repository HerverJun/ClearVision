using System.Collections.Concurrent;
using ClearVision.Product.Core.Cameras;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Infrastructure.Cameras;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace ClearVision.Product.Desktop.Tests;

[TestClassification(TestDomain.Desktop, TestPurpose.Regression, TestLane.Pr, TestEvidenceType.Contract, TestOracleType.Contract, TestResourceRequirement.None, TestExpectedDuration.Fast, TestFlakyPolicy.Blocking, "camera", Suites = "DesktopEndpoints")]
public class CameraManagerLifecycleTests
{
    [Fact]
    public async Task ApplyBindingsAsync_WhenBindingIsDeleted_ShouldStopAndDisposeRetiredProvider()
    {
        var provider = CreateProvider("SN-OLD");
        using var manager = CreateManager((serialNumber, _) =>
            serialNumber == "SN-OLD" ? provider : null);
        manager.LoadBindings([Binding("cam-old", "SN-OLD")], "cam-old");
        await manager.GetOrCreateByBindingAsync("cam-old");

        await manager.ApplyBindingsAsync([], string.Empty);

        provider.Received(1).StopGrabbing();
        provider.Received(1).Dispose();
        manager.GetCamera("SN-OLD").Should().BeNull();
        manager.GetBindings().Should().BeEmpty();
    }

    [Fact]
    public async Task ApplyBindingsAsync_WhenSerialNumberIsReplaced_ShouldRetireOldProvider()
    {
        var oldProvider = CreateProvider("SN-OLD");
        var newProvider = CreateProvider("SN-NEW");
        using var manager = CreateManager((serialNumber, _) => serialNumber switch
        {
            "SN-OLD" => oldProvider,
            "SN-NEW" => newProvider,
            _ => null
        });
        manager.LoadBindings([Binding("cam-main", "SN-OLD")], "cam-main");
        await manager.GetOrCreateByBindingAsync("cam-main");

        await manager.ApplyBindingsAsync([Binding("cam-main", "SN-NEW")], "cam-main");

        oldProvider.Received(1).StopGrabbing();
        oldProvider.Received(1).Dispose();
        manager.GetCamera("SN-OLD").Should().BeNull();
        (await manager.GetOrCreateByBindingAsync("cam-main")).CameraId.Should().Be("SN-NEW");
        manager.GetCamera("SN-NEW").Should().NotBeNull();
    }

    [Fact]
    public async Task ApplyBindingsAsync_WhenSharedSerialIsStillReferenced_ShouldRetainProvider()
    {
        var provider = CreateProvider("SN-SHARED");
        using var manager = CreateManager((serialNumber, _) =>
            serialNumber == "SN-SHARED" ? provider : null);
        manager.LoadBindings(
            [Binding("cam-a", "SN-SHARED"), Binding("cam-b", "SN-SHARED")],
            "cam-a");
        var opened = await manager.GetOrCreateByBindingAsync("cam-a");

        await manager.ApplyBindingsAsync([Binding("cam-b", "SN-SHARED")], "cam-b");

        provider.DidNotReceive().StopGrabbing();
        provider.DidNotReceive().Dispose();
        manager.GetCamera("SN-SHARED").Should().BeSameAs(opened);
        manager.GetBindings().Should().ContainSingle(binding =>
            binding.Id == "cam-b" && binding.SerialNumber == "SN-SHARED");
    }

    [Fact]
    public async Task FailedProbes_ForTenThousandAuthorizedBindings_ShouldReturnLockRegistryToBaseline()
    {
        var probeCalls = 0;
        using var manager = CreateManager((_, _) =>
        {
            Interlocked.Increment(ref probeCalls);
            return null;
        });
        var bindings = Enumerable.Range(0, 10_000)
            .Select(index => Binding($"cam-{index:D5}", $"MISSING-{index:D5}"))
            .ToList();
        manager.LoadBindings(bindings, bindings[0].Id);
        var rejected = 0;

        for (var index = 0; index < 10_000; index++)
        {
            try
            {
                await manager.GetOrCreateCameraAsync($"cam-{index:D5}");
            }
            catch (InvalidOperationException)
            {
                rejected++;
            }
        }

        rejected.Should().Be(10_000);
        probeCalls.Should().Be(10_000);
        manager.RetainedCameraLockCount.Should().Be(0);
    }

    [Theory]
    [InlineData("SN-AUTHORIZED", "cam-authorized", false, "raw serial")]
    [InlineData("cam-disabled", "cam-disabled", false, "disabled binding")]
    [InlineData("cam-unbound", "cam-unbound", true, "missing serial")]
    public async Task Open_WhenBindingAuthorityRejectsTarget_ShouldNotProbeProvider(
        string requestedId,
        string bindingId,
        bool missingSerial,
        string reason)
    {
        var providerCalls = 0;
        using var manager = CreateManager((_, _) =>
        {
            Interlocked.Increment(ref providerCalls);
            return CreateProvider("unexpected");
        });
        manager.LoadBindings(
            [new CameraBindingConfig
            {
                Id = bindingId,
                SerialNumber = missingSerial ? string.Empty : "SN-AUTHORIZED",
                IsEnabled = !bindingId.Equals("cam-disabled", StringComparison.Ordinal)
            }],
            bindingId);

        var act = async () => await manager.GetOrCreateCameraAsync(requestedId);

        await act.Should().ThrowAsync<InvalidOperationException>(reason);
        providerCalls.Should().Be(0);
        manager.RetainedCameraLockCount.Should().Be(0);
    }

    [Fact]
    public async Task ConcurrentOpen_ForSameCamera_ShouldOpenOnceAndReclaimLock()
    {
        var provider = CreateProvider("SN-CONCURRENT");
        using var factoryEntered = new ManualResetEventSlim();
        using var releaseFactory = new ManualResetEventSlim();
        var factoryCalls = 0;
        using var manager = CreateManager((serialNumber, _) =>
        {
            serialNumber.Should().Be("SN-CONCURRENT");
            Interlocked.Increment(ref factoryCalls);
            factoryEntered.Set();
            if (!releaseFactory.Wait(TimeSpan.FromSeconds(10)))
            {
                throw new TimeoutException("Test factory barrier was not released.");
            }

            return provider;
        });
        manager.LoadBindings([Binding("cam-concurrent", "SN-CONCURRENT")], "cam-concurrent");

        var opens = Enumerable.Range(0, 32)
            .Select(_ => Task.Run(() => manager.GetOrCreateCameraAsync("cam-concurrent")))
            .ToArray();

        try
        {
            factoryEntered.Wait(TimeSpan.FromSeconds(10)).Should().BeTrue();
            SpinWait.SpinUntil(
                    () => manager.GetCameraLockReferenceCount("SN-CONCURRENT") == opens.Length,
                    TimeSpan.FromSeconds(10))
                .Should().BeTrue();
        }
        finally
        {
            releaseFactory.Set();
        }

        var cameras = await Task.WhenAll(opens);

        factoryCalls.Should().Be(1);
        cameras.Should().OnlyContain(camera => ReferenceEquals(camera, cameras[0]));
        manager.RetainedCameraLockCount.Should().Be(0);
    }

    [Fact]
    public async Task ConcurrentOpen_WhenFirstProbeFails_ShouldRetryOnSameGateWithoutDoubleOpen()
    {
        var provider = CreateProvider("SN-RETRY");
        using var firstFactoryEntered = new ManualResetEventSlim();
        using var releaseFirstFactory = new ManualResetEventSlim();
        var factoryCalls = 0;
        using var manager = CreateManager((_, _) =>
        {
            var call = Interlocked.Increment(ref factoryCalls);
            if (call == 1)
            {
                firstFactoryEntered.Set();
                if (!releaseFirstFactory.Wait(TimeSpan.FromSeconds(10)))
                {
                    throw new TimeoutException("Test factory barrier was not released.");
                }

                return null;
            }

            return provider;
        });
        manager.LoadBindings([Binding("cam-retry", "SN-RETRY")], "cam-retry");

        var opens = Enumerable.Range(0, 16)
            .Select(_ => Task.Run(() => TryOpenAsync(manager, "cam-retry")))
            .ToArray();

        try
        {
            firstFactoryEntered.Wait(TimeSpan.FromSeconds(10)).Should().BeTrue();
            SpinWait.SpinUntil(
                    () => manager.GetCameraLockReferenceCount("SN-RETRY") == opens.Length,
                    TimeSpan.FromSeconds(10))
                .Should().BeTrue();
        }
        finally
        {
            releaseFirstFactory.Set();
        }

        var results = await Task.WhenAll(opens);

        results.Should().ContainSingle(result => result.Error is InvalidOperationException);
        var successfulResults = results.Where(result => result.Error == null).ToArray();
        successfulResults.Should().HaveCount(opens.Length - 1);
        successfulResults.Should().OnlyContain(result =>
            ReferenceEquals(result.Camera, successfulResults[0].Camera));
        factoryCalls.Should().Be(2);
        manager.RetainedCameraLockCount.Should().Be(0);
    }

    [Fact]
    public async Task RepeatedOpenAndClose_ShouldDisposeEachGenerationOnceAndReclaimLock()
    {
        var firstProvider = CreateProvider("SN-REOPEN");
        var secondProvider = CreateProvider("SN-REOPEN");
        var providers = new ConcurrentQueue<ICameraProvider>([firstProvider, secondProvider]);
        var factoryCalls = 0;
        using var manager = CreateManager((_, _) =>
        {
            Interlocked.Increment(ref factoryCalls);
            return providers.TryDequeue(out var provider) ? provider : null;
        });
        manager.LoadBindings([Binding("cam-reopen", "SN-REOPEN")], "cam-reopen");

        var firstCamera = await manager.GetOrCreateCameraAsync("cam-reopen");
        (await manager.GetOrCreateCameraAsync("cam-reopen")).Should().BeSameAs(firstCamera);
        await manager.CloseCameraAsync("cam-reopen");
        await manager.CloseCameraAsync("cam-reopen");

        firstProvider.Received(1).Dispose();
        factoryCalls.Should().Be(1);
        manager.GetCamera("SN-REOPEN").Should().BeNull();

        var secondCamera = await manager.GetOrCreateCameraAsync("cam-reopen");
        secondCamera.Should().NotBeSameAs(firstCamera);
        factoryCalls.Should().Be(2);

        await manager.CloseCameraAsync("cam-reopen");
        secondProvider.Received(1).Dispose();
        manager.RetainedCameraLockCount.Should().Be(0);
    }

    [Fact]
    public async Task Close_WithConcurrentLeases_ShouldDelayDisposeAndReopenUntilLastLeaseReleases()
    {
        var firstProvider = CreateProvider("SN-LEASED");
        var secondProvider = CreateProvider("SN-LEASED");
        var factoryCalls = 0;
        using var manager = CreateManager((_, _) => Interlocked.Increment(ref factoryCalls) switch
        {
            1 => firstProvider,
            2 => secondProvider,
            _ => throw new InvalidOperationException("Unexpected extra provider creation.")
        });
        manager.LoadBindings([Binding("cam-leased", "SN-LEASED")], "cam-leased");
        var firstLease = await manager.AcquireByBindingLeaseAsync("cam-leased");
        var secondLease = await manager.AcquireByBindingLeaseAsync("cam-leased");

        await manager.CloseCameraAsync("cam-leased");

        manager.GetCamera("SN-LEASED").Should().BeNull();
        firstProvider.DidNotReceive().Dispose();

        var reopenTask = manager.AcquireByBindingLeaseAsync("cam-leased");
        reopenTask.IsCompleted.Should().BeFalse();
        factoryCalls.Should().Be(1);

        firstLease.Dispose();
        firstLease.Dispose();
        firstProvider.DidNotReceive().Dispose();
        factoryCalls.Should().Be(1);

        await secondLease.DisposeAsync();
        firstProvider.Received(1).Dispose();

        var replacementLease = await reopenTask;
        factoryCalls.Should().Be(2);
        replacementLease.Camera.Should().NotBeSameAs(firstLease.Camera);
        manager.RetainedCameraLockCount.Should().Be(0);

        await manager.CloseCameraAsync("cam-leased");
        secondProvider.DidNotReceive().Dispose();
        await replacementLease.DisposeAsync();
        secondProvider.Received(1).Dispose();
    }

    [Fact]
    public async Task ApplyBindingsAsync_WithPinnedOldGeneration_ShouldRetireWithoutDisposingActiveLease()
    {
        var oldProvider = CreateProvider("SN-CONFIG-OLD");
        var newProvider = CreateProvider("SN-CONFIG-NEW");
        using var manager = CreateManager((serialNumber, _) => serialNumber switch
        {
            "SN-CONFIG-OLD" => oldProvider,
            "SN-CONFIG-NEW" => newProvider,
            _ => null
        });
        manager.LoadBindings([Binding("cam-config-race", "SN-CONFIG-OLD")], "cam-config-race");
        var oldLease = await manager.AcquireByBindingLeaseAsync("cam-config-race");

        await manager.ApplyBindingsAsync(
            [Binding("cam-config-race", "SN-CONFIG-NEW")],
            "cam-config-race");

        manager.GetCamera("SN-CONFIG-OLD").Should().BeNull();
        oldLease.Camera.IsConnected.Should().BeTrue();
        oldProvider.DidNotReceive().Dispose();

        var replacementLease = await manager.AcquireByBindingLeaseAsync("cam-config-race");
        replacementLease.Camera.CameraId.Should().Be("SN-CONFIG-NEW");
        replacementLease.Camera.Should().NotBeSameAs(oldLease.Camera);
        oldProvider.DidNotReceive().Dispose();

        await oldLease.DisposeAsync();
        oldProvider.Received(1).Dispose();

        await manager.ApplyBindingsAsync([], string.Empty);
        newProvider.DidNotReceive().Dispose();
        await replacementLease.DisposeAsync();
        newProvider.Received(1).Dispose();
        manager.RetainedCameraLockCount.Should().Be(0);
    }

    private static CameraManager CreateManager(Func<string, string?, ICameraProvider?> providerFactory) =>
        new(NullLoggerFactory.Instance, providerFactory, () => []);

    private static async Task<(ICamera? Camera, Exception? Error)> TryOpenAsync(
        CameraManager manager,
        string cameraId)
    {
        try
        {
            return (await manager.GetOrCreateCameraAsync(cameraId), null);
        }
        catch (Exception ex)
        {
            return (null, ex);
        }
    }

    private static ICameraProvider CreateProvider(string serialNumber)
    {
        var provider = Substitute.For<ICameraProvider>();
        provider.IsConnected.Returns(true);
        provider.IsGrabbing.Returns(false);
        provider.StopGrabbing().Returns(true);
        provider.CurrentDevice.Returns(new CameraDeviceInfo
        {
            SerialNumber = serialNumber,
            UserDefinedName = serialNumber
        });
        return provider;
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
