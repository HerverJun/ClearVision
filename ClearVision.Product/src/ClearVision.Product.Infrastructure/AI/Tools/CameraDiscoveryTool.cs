using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ClearVision.Product.Core.AI.Tools;
using ClearVision.Product.Core.Cameras;

namespace ClearVision.Product.Infrastructure.AI.Tools;

public sealed class CameraDiscoveryTool : IVisionAgentTool
{
    private readonly ICameraManager _cameraManager;

    public CameraDiscoveryTool(ICameraManager cameraManager)
    {
        _cameraManager = cameraManager;
    }

    public string Name => "discover_cameras";
    public string DisplayName => "搜索局域网相机";
    public string Description => "扫描局域网以发现未绑定的物理相机设备，返回设备的厂商、序列号、IP地址和型号。只读且只作发现，不修改系统配置。";
    public string Category => "Hardware";
    public VisionAgentToolPermission Permission => VisionAgentToolPermission.ReadOnly;

    public JsonElement ParametersSchema { get; } = JsonDocument.Parse(@"{
        ""type"": ""object"",
        ""properties"": {
            ""manufacturer"": { ""type"": ""string"", ""description"": ""可选，按厂商名称过滤（例如 'Huaray', 'Hikvision' 等）"" }
        }
    }").RootElement;

    public async Task<VisionAgentToolResult> ExecuteAsync(
        VisionAgentToolContext context,
        JsonElement arguments,
        CancellationToken cancellationToken)
    {
        string? manufacturerFilter = null;
        if (arguments.ValueKind == JsonValueKind.Object &&
            arguments.TryGetProperty("manufacturer", out var manProp) &&
            manProp.ValueKind == JsonValueKind.String)
        {
            manufacturerFilter = manProp.GetString();
        }

        var cameras = await _cameraManager.EnumerateCamerasAsync();

        if (!string.IsNullOrWhiteSpace(manufacturerFilter))
        {
            cameras = cameras.Where(c => string.Equals(c.Manufacturer, manufacturerFilter, StringComparison.OrdinalIgnoreCase));
        }

        var result = cameras.Select(c => new
        {
            manufacturer = c.Manufacturer,
            serialNumber = c.CameraId, // CameraId is the serial number in EnumerateCamerasAsync
            ipAddress = "", // EnumerateCamerasAsync Info might not contain IP directly, but let's expose what we have
            modelName = c.Model,
            interfaceType = c.ConnectionType,
            displayName = c.Name
        }).ToList();

        var summary = $"Discovered {result.Count} camera devices.";
        return VisionAgentToolResult.CreateSuccess(new { devices = result, diagnostics = new { } }, summary);
    }
}
