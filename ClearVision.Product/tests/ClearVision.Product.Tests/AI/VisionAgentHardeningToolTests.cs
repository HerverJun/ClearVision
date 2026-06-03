using System.Text.Json;
using ClearVision.Product.Core.AI.Tools;
using ClearVision.Product.Core.Cameras;
using ClearVision.Product.Core.DTOs;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Interfaces;
using ClearVision.Product.Core.Services;
using ClearVision.Product.Infrastructure.AI.Tools;
using FluentAssertions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace ClearVision.Product.Tests.AI;

public sealed class VisionAgentHardeningToolTests
{
    [Fact(DisplayName = "capture_test_frame should reject non software/manual trigger bindings")]
    public async Task CaptureTestFrame_ShouldRejectUnsupportedTriggerMode()
    {
        var cameraManager = Substitute.For<ICameraManager>();
        cameraManager.GetBindings().Returns(
        [
            new CameraBindingConfig
            {
                Id = "cam-ext",
                DisplayName = "External Trigger Camera",
                TriggerMode = "External"
            }
        ]);
        var tool = new CameraTestFrameTool(cameraManager, new VisionAgentTemporaryFrameStore());
        using var args = JsonDocument.Parse("""{"cameraBindingId":"cam-ext"}""");

        var result = await tool.ExecuteAsync(
            new VisionAgentToolContext
            {
                AllowedPermissions = new HashSet<VisionAgentToolPermission> { VisionAgentToolPermission.RuntimePreview }
            },
            args.RootElement,
            CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("unsupported_trigger_mode_for_test_frame");
        JsonSerializer.Serialize(result.Data).Should().Contain("software_only");
        await cameraManager.DidNotReceive().GetOrCreateByBindingAsync(Arg.Any<string>());
    }

    [Fact(DisplayName = "capture_test_frame configured_trigger_snapshot should remain unsupported without touching camera")]
    public async Task CaptureTestFrame_ConfiguredTriggerSnapshot_ShouldRemainUnsupported()
    {
        var cameraManager = Substitute.For<ICameraManager>();
        var tool = new CameraTestFrameTool(cameraManager, new VisionAgentTemporaryFrameStore());
        using var args = JsonDocument.Parse("""{"cameraBindingId":"cam-ext","captureMode":"configured_trigger_snapshot"}""");

        var result = await tool.ExecuteAsync(
            new VisionAgentToolContext
            {
                AllowedPermissions = new HashSet<VisionAgentToolPermission> { VisionAgentToolPermission.RuntimePreview }
            },
            args.RootElement,
            CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("unsupported_capture_mode");
        cameraManager.DidNotReceive().GetBindings();
        await cameraManager.DidNotReceive().GetOrCreateByBindingAsync(Arg.Any<string>());
    }

    [Fact(DisplayName = "capture_test_frame should be denied by registry without RuntimePreview permission")]
    public async Task CaptureTestFrame_ShouldBeDeniedWithoutRuntimePreviewPermission()
    {
        var cameraManager = Substitute.For<ICameraManager>();
        var registry = new VisionAgentToolRegistry(
            [new CameraTestFrameTool(cameraManager, new VisionAgentTemporaryFrameStore())],
            Substitute.For<Microsoft.Extensions.Logging.ILogger<VisionAgentToolRegistry>>());
        using var args = JsonDocument.Parse("""{"cameraBindingId":"cam-1"}""");

        var result = await registry.ExecuteAsync(
            "capture_test_frame",
            new VisionAgentToolContext
            {
                AllowedPermissions = new HashSet<VisionAgentToolPermission>
                {
                    VisionAgentToolPermission.ReadOnly,
                    VisionAgentToolPermission.Simulation
                }
            },
            args.RootElement,
            CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("tool_permission_denied");
        cameraManager.DidNotReceive().GetBindings();
        await cameraManager.DidNotReceive().GetOrCreateByBindingAsync(Arg.Any<string>());
    }

    [Fact(DisplayName = "discover_cameras should filter by manufacturer and include diagnostics")]
    public async Task DiscoverCameras_ShouldFilterManufacturer()
    {
        var cameraManager = Substitute.For<ICameraManager>();
        cameraManager.EnumerateCamerasAsync().Returns(
        [
            new CameraInfo { CameraId = "h1", Manufacturer = "Huaray" },
            new CameraInfo { CameraId = "hk1", Manufacturer = "Hikvision" },
            new CameraInfo { CameraId = "x1" }
        ]);
        var tool = new CameraDiscoveryTool(cameraManager);
        using var args = JsonDocument.Parse("""{"manufacturer":"Hikvision"}""");

        var result = await tool.ExecuteAsync(new VisionAgentToolContext(), args.RootElement, CancellationToken.None);

        result.Success.Should().BeTrue();
        var payload = JsonSerializer.SerializeToElement(result.Data);
        GetProperty(payload, "manufacturer", "Manufacturer").GetString().Should().Be("Hikvision");
        var cameras = GetProperty(payload, "cameras", "Cameras");
        cameras.GetArrayLength().Should().Be(1);
        GetProperty(cameras[0], "cameraId", "CameraId").GetString().Should().Be("hk1");
        GetProperty(GetProperty(payload, "diagnostics", "Diagnostics"), "totalDiscovered", "TotalDiscovered")
            .GetInt32()
            .Should()
            .Be(3);
    }

    [Fact(DisplayName = "temporary frame store should evict by count, bytes, ttl, and remove on demand")]
    public async Task TemporaryFrameStore_ShouldEnforceLimitsAndTtl()
    {
        using var store = new VisionAgentTemporaryFrameStore(Options.Create(new VisionAgentTemporaryFrameStoreOptions
        {
            MaxFrameCount = 2,
            MaxTotalBytes = 5,
            MaxSingleFrameBytes = 4,
            TtlSeconds = 1,
            CleanupIntervalSeconds = 60
        }));

        var first = store.Store([1, 2], new VisionAgentTemporaryFrameMetadata());
        var second = store.Store([3, 4], new VisionAgentTemporaryFrameMetadata());
        var third = store.Store([5, 6], new VisionAgentTemporaryFrameMetadata());

        store.TryGet(first, out _).Should().BeFalse();
        store.TryGet(second, out _).Should().BeTrue();
        store.TryGet(third, out _).Should().BeTrue();
        store.GetStats().FrameCount.Should().Be(2);
        store.Remove(second).Should().BeTrue();
        store.TryGet(second, out _).Should().BeFalse();

        await Task.Delay(1100);
        store.CleanupExpired().Should().BeGreaterThan(0);
        store.GetStats().FrameCount.Should().Be(0);
        Action tooLarge = () => store.Store([1, 2, 3, 4, 5], new VisionAgentTemporaryFrameMetadata());
        tooLarge.Should().Throw<InvalidOperationException>();
    }

    [Fact(DisplayName = "runtime_package_precheck should block offline station and missing replay for strict camera flow")]
    public async Task RuntimePackagePrecheck_ShouldUseReplayAndStationState()
    {
        var validator = Substitute.For<IAiFlowValidator>();
        validator.Validate(Arg.Any<AiGeneratedFlowJson>()).Returns(AiValidationResult.Success());
        var cameraManager = Substitute.For<ICameraManager>();
        cameraManager.GetBindings().Returns(
        [
            new CameraBindingConfig { Id = "cam-1", SerialNumber = "SN-1" }
        ]);
        var configuration = Substitute.For<IConfigurationService>();
        configuration.GetCurrent().Returns(new AppConfig
        {
            Cameras = [new CameraBindingConfig { Id = "cam-1", SerialNumber = "SN-1" }]
        });
        var stationReader = Substitute.For<IVisionAgentStationStatusReader>();
        stationReader.GetStationsAsync(Arg.Any<CancellationToken>()).Returns(
        [
            new VisionAgentStationStatus { StationId = "station-1", Online = false }
        ]);
        var tool = new RuntimePackagePrecheckTool(validator, cameraManager, configuration, stationReader);
        using var args = JsonDocument.Parse("""
        {
          "targetStationId": "station-1",
          "requireReplayForCameraFlow": true,
          "dryRunSummary": { "dryRunSucceeded": false, "warnings": ["coverage low"] },
          "replaySummary": { "replaySucceeded": false, "errors": ["camera mismatch"] },
          "flow": {
            "operators": [
              {
                "tempId": "cam",
                "operatorType": "ImageAcquisition",
                "parameters": { "CameraBindingId": "cam-1" }
              }
            ],
            "connections": []
          }
        }
        """);

        var result = await tool.ExecuteAsync(
            new VisionAgentToolContext
            {
                AllowedPermissions = new HashSet<VisionAgentToolPermission> { VisionAgentToolPermission.DeploymentPrepare }
            },
            args.RootElement,
            CancellationToken.None);

        result.Success.Should().BeTrue();
        var payload = JsonSerializer.SerializeToElement(result.Data);
        payload.GetProperty("ready").GetBoolean().Should().BeFalse();
        payload.GetProperty("blockingIssues").EnumerateArray()
            .Select(item => item.GetString())
            .Should().Contain(item => item!.Contains("requires a successful replay_flow_with_frame"));
        payload.GetProperty("blockingIssues").EnumerateArray()
            .Select(item => item.GetString())
            .Should().Contain(item => item!.Contains("offline"));
        payload.GetProperty("blockingIssues").EnumerateArray()
            .Select(item => item.GetString())
            .Should().Contain(item => item!.Contains("dryrun summary reports failure"));
        payload.GetProperty("blockingIssues").EnumerateArray()
            .Select(item => item.GetString())
            .Should().Contain(item => item!.Contains("replay summary reports failure"));
        payload.GetProperty("warnings").EnumerateArray()
            .Select(item => item.GetString())
            .Should().Contain(item => item!.Contains("coverage low"));
        payload.GetProperty("requiredUserActions").GetArrayLength().Should().BeGreaterThan(0);
    }

    private static JsonElement GetProperty(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (element.TryGetProperty(name, out var property))
            {
                return property;
            }
        }

        throw new KeyNotFoundException($"None of the expected properties were present: {string.Join(", ", names)}");
    }
}
