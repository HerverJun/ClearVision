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

    private static CameraManager CreateManager(Func<string, string?, ICameraProvider?> providerFactory) =>
        new(NullLoggerFactory.Instance, providerFactory, () => []);

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
