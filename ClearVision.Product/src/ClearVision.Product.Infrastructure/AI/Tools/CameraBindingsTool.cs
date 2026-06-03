using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ClearVision.Product.Core.AI.Tools;
using ClearVision.Product.Core.Cameras;

namespace ClearVision.Product.Infrastructure.AI.Tools;

public sealed class CameraBindingsTool : IVisionAgentTool
{
    private readonly ICameraManager _cameraManager;

    public CameraBindingsTool(ICameraManager cameraManager)
    {
        _cameraManager = cameraManager;
    }

    public string Name => "list_camera_bindings";
    public string DisplayName => "获取相机绑定列表";
    public string Description => "获取当前系统中已经配置好的相机绑定关系（包括绑定ID、显示名称、序列号、IP地址、触发模式、像素格式及连接状态）。";
    public string Category => "Hardware";
    public VisionAgentToolPermission Permission => VisionAgentToolPermission.ReadOnly;

    public JsonElement ParametersSchema { get; } = JsonDocument.Parse(@"{
        ""type"": ""object"",
        ""properties"": {}
    }").RootElement;

    public Task<VisionAgentToolResult> ExecuteAsync(
        VisionAgentToolContext context,
        JsonElement arguments,
        CancellationToken cancellationToken)
    {
        var bindings = _cameraManager.GetBindings() ?? new();

        var result = bindings.Select(b => new
        {
            id = b.Id,
            displayName = b.DisplayName,
            manufacturer = b.Manufacturer,
            serialNumber = b.SerialNumber,
            ipAddress = b.IpAddress,
            modelName = b.ModelName,
            interfaceType = b.InterfaceType,
            triggerMode = b.TriggerMode,
            pixelFormat = b.PixelFormat,
            connectionStatus = "Configured"
        }).ToList();

        var summary = $"Retrieved {result.Count} camera bindings.";
        return Task.FromResult(VisionAgentToolResult.CreateSuccess(new { bindings = result }, summary));
    }
}
