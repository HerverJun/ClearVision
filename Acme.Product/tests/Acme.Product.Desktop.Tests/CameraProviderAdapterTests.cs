using System.Runtime.InteropServices;
using Acme.Product.Core.Cameras;
using Acme.Product.Infrastructure.Cameras;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using OpenCvSharp;

namespace Acme.Product.Desktop.Tests;

public class CameraProviderAdapterTests
{
    [Theory]
    [InlineData("Huaray", "MV-CA050-10GM", "GigE", false)]
    [InlineData("MindVision", "MV-GE200", "GigE", false)]
    [InlineData("Daheng Imaging", "MER2-503", "GigE", false)]
    [InlineData("Hikvision", "MV-CA050-10GM", "GigE", true)]
    [InlineData("Hikrobot", "MV-CS060", "GigE", true)]
    [InlineData("Unknown", "MV-CA050-10GM", "GigE", true)]
    [InlineData("Unknown", "MER2-503", "GigE", false)]
    public void HikvisionDiscoveryFilter_ShouldRejectKnownThirdPartyGigECameras(
        string manufacturer,
        string model,
        string interfaceType,
        bool expected)
    {
        var method = typeof(HikvisionCamera).GetMethod("IsAcceptedHikvisionDevice", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        method.Should().NotBeNull();
        method!.Invoke(null, new object?[] { manufacturer, model, interfaceType }).Should().Be(expected);
    }

    [Theory]
    [InlineData(0x01080008u, CameraPixelFormat.BayerGR8)]
    [InlineData(0x01080009u, CameraPixelFormat.BayerRG8)]
    [InlineData(0x0108000Au, CameraPixelFormat.BayerGB8)]
    [InlineData(0x0108000Bu, CameraPixelFormat.BayerBG8)]
    public void HuarayPixelFormatMapping_ShouldUseHuarayGvspBayerConstants(uint pixelType, CameraPixelFormat expected)
    {
        var method = typeof(MindVisionCamera).GetMethod("ConvertPixelFormat", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        method.Should().NotBeNull();
        method!.Invoke(null, new object[] { pixelType }).Should().Be(expected);
    }

    [Fact]
    public async Task AcquireSingleFrameAsync_WhenNotGrabbing_ShouldExecuteSoftwareTriggerSequence()
    {
        var provider = Substitute.For<ICameraProvider>();
        provider.IsGrabbing.Returns(false);
        provider.StartGrabbing().Returns(true);
        provider.SetTriggerMode(CameraTriggerMode.Software).Returns(true);
        provider.ExecuteSoftwareTrigger().Returns(true);

        var frameBuffer = new byte[] { 0, 127, 255, 60 };
        var handle = GCHandle.Alloc(frameBuffer, GCHandleType.Pinned);

        try
        {
            provider.GetFrame(3000).Returns(new CameraFrame
            {
                DataPtr = handle.AddrOfPinnedObject(),
                Width = 2,
                Height = 2,
                Size = frameBuffer.Length,
                PixelFormat = CameraPixelFormat.Mono8,
                FrameNumber = 1,
                Timestamp = 1
            });

            var adapter = new CameraProviderAdapter("cam-1", provider, Substitute.For<ILogger<CameraProviderAdapter>>());
            var pngBytes = await adapter.AcquireSingleFrameAsync();

            pngBytes.Should().NotBeNullOrEmpty();
            pngBytes.Take(8).Should().Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });

            Received.InOrder(() =>
            {
                provider.SetTriggerMode(CameraTriggerMode.Software);
                provider.StartGrabbing();
                provider.ExecuteSoftwareTrigger();
                provider.GetFrame(3000);
            });
        }
        finally
        {
            handle.Free();
        }
    }

    [Fact]
    public async Task AcquireSingleFrameAsync_WhenAlreadyGrabbing_ShouldReuseActiveSoftwareTriggerStream()
    {
        var provider = Substitute.For<ICameraProvider>();
        provider.IsGrabbing.Returns(true);
        provider.SetTriggerMode(CameraTriggerMode.Software).Returns(true);
        provider.ExecuteSoftwareTrigger().Returns(true);

        var frameBuffer = new byte[] { 10, 20, 30, 40 };
        var handle = GCHandle.Alloc(frameBuffer, GCHandleType.Pinned);

        try
        {
            provider.GetFrame(3000).Returns(new CameraFrame
            {
                DataPtr = handle.AddrOfPinnedObject(),
                Width = 2,
                Height = 2,
                Size = frameBuffer.Length,
                PixelFormat = CameraPixelFormat.Mono8
            });

            var adapter = new CameraProviderAdapter("cam-2", provider, Substitute.For<ILogger<CameraProviderAdapter>>());
            var pngBytes = await adapter.AcquireSingleFrameAsync();

            pngBytes.Should().NotBeNullOrEmpty();
            Received.InOrder(() =>
            {
                provider.SetTriggerMode(CameraTriggerMode.Software);
                provider.ExecuteSoftwareTrigger();
                provider.GetFrame(3000);
            });
            provider.DidNotReceive().StopGrabbing();
            provider.DidNotReceive().StartGrabbing();
        }
        finally
        {
            handle.Free();
        }
    }

    [Theory]
    [InlineData(CameraPixelFormat.RGB8, new byte[] { 255, 0, 0, 0, 255, 0 }, 2, 1)]
    [InlineData(CameraPixelFormat.BGR8, new byte[] { 0, 0, 255, 0, 255, 0 }, 2, 1)]
    [InlineData(CameraPixelFormat.BayerRG8, new byte[] { 255, 128, 128, 0 }, 2, 2)]
    public async Task AcquireSingleFrameAsync_WhenColorFrame_ShouldEncodeThreeChannelImage(
        CameraPixelFormat pixelFormat,
        byte[] frameBuffer,
        int width,
        int height)
    {
        var provider = Substitute.For<ICameraProvider>();
        provider.IsGrabbing.Returns(true);
        provider.SetTriggerMode(CameraTriggerMode.Software).Returns(true);
        provider.ExecuteSoftwareTrigger().Returns(true);

        var handle = GCHandle.Alloc(frameBuffer, GCHandleType.Pinned);

        try
        {
            provider.GetFrame(3000).Returns(new CameraFrame
            {
                DataPtr = handle.AddrOfPinnedObject(),
                Width = width,
                Height = height,
                Size = frameBuffer.Length,
                PixelFormat = pixelFormat
            });

            var adapter = new CameraProviderAdapter("cam-color", provider, Substitute.For<ILogger<CameraProviderAdapter>>());
            var pngBytes = await adapter.AcquireSingleFrameAsync();
            using var decoded = Cv2.ImDecode(pngBytes, ImreadModes.Unchanged);

            decoded.Empty().Should().BeFalse();
            decoded.Channels().Should().Be(3);
        }
        finally
        {
            handle.Free();
        }
    }

    [Fact]
    public async Task AcquireSingleFrameAsync_WhenFrameIsNull_ShouldThrowTimeoutException()
    {
        var provider = Substitute.For<ICameraProvider>();
        provider.IsGrabbing.Returns(true);
        provider.SetTriggerMode(CameraTriggerMode.Software).Returns(true);
        provider.ExecuteSoftwareTrigger().Returns(true);
        provider.GetFrame(3000).Returns((CameraFrame?)null);

        var adapter = new CameraProviderAdapter("cam-3", provider, Substitute.For<ILogger<CameraProviderAdapter>>());

        var act = async () => await adapter.AcquireSingleFrameAsync();

        await act.Should().ThrowAsync<TimeoutException>()
            .WithMessage("*获取图像超时*");

        provider.Received(1).SetTriggerMode(CameraTriggerMode.Software);
        provider.Received(1).ExecuteSoftwareTrigger();
        provider.Received(1).GetFrame(3000);
    }

    [Fact]
    public async Task AcquireSingleFrameAsync_WhenSoftwareTriggerFails_ShouldThrowAndNotGetFrame()
    {
        var provider = Substitute.For<ICameraProvider>();
        provider.IsGrabbing.Returns(true);
        provider.SetTriggerMode(CameraTriggerMode.Software).Returns(true);
        provider.ExecuteSoftwareTrigger().Returns(false);

        var adapter = new CameraProviderAdapter("cam-trigger-fail", provider, Substitute.For<ILogger<CameraProviderAdapter>>());

        var act = async () => await adapter.AcquireSingleFrameAsync();

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*software trigger*");
        provider.DidNotReceive().GetFrame(Arg.Any<int>());
    }

    [Fact]
    public async Task SetExposureTimeAsync_WhenProviderRejectsValue_ShouldThrow()
    {
        var provider = Substitute.For<ICameraProvider>();
        provider.SetExposure(1234).Returns(false);

        var adapter = new CameraProviderAdapter("cam-exposure-fail", provider, Substitute.For<ILogger<CameraProviderAdapter>>());

        var act = async () => await adapter.SetExposureTimeAsync(1234);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*exposure*");
    }

    [Fact]
    public async Task SetTriggerModeAsync_WithExternalMode_ShouldPassHardwareTriggerSource()
    {
        var provider = Substitute.For<ICameraProvider>();
        provider.SetTriggerMode(CameraTriggerMode.External, "Line3").Returns(true);

        var adapter = new CameraProviderAdapter("cam-external", provider, Substitute.For<ILogger<CameraProviderAdapter>>());

        await adapter.SetTriggerModeAsync(CameraTriggerMode.External, "Line3");

        provider.Received(1).SetTriggerMode(CameraTriggerMode.External, "Line3");
    }

    [Fact]
    public async Task SetPixelFormatAsync_ShouldPassSelectedFormatToProvider()
    {
        var provider = Substitute.For<ICameraProvider>();
        provider.SetPixelFormat(CameraPixelFormat.RGB8).Returns(true);

        var adapter = new CameraProviderAdapter("cam-format", provider, Substitute.For<ILogger<CameraProviderAdapter>>());

        await adapter.SetPixelFormatAsync(CameraPixelFormat.RGB8);

        provider.Received(1).SetPixelFormat(CameraPixelFormat.RGB8);
    }
}
