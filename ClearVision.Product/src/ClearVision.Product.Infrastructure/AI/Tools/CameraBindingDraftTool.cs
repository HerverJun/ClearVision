using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ClearVision.Product.Core.AI.Tools;

namespace ClearVision.Product.Infrastructure.AI.Tools;

public sealed class CameraBindingDraftTool : IVisionAgentTool
{
    public string Name => "draft_camera_binding";
    public string DisplayName => "拟定相机绑定草稿";
    public string Description => "基于发现的目标物理相机设备，拟定一份相机绑定配置草稿。该操作不会修改系统运行中的物理配置，只返回草稿以供人工确认。";
    public string Category => "Hardware";
    public VisionAgentToolPermission Permission => VisionAgentToolPermission.ConfigDraft;

    public JsonElement ParametersSchema { get; } = JsonDocument.Parse(@"{
        ""type"": ""object"",
        ""properties"": {
            ""device"": {
                ""type"": ""object"",
                ""description"": ""通过 discover_cameras 发现的物理相机设备对象，包含 serialNumber, manufacturer, modelName, interfaceType 等""
            },
            ""suggestedDisplayName"": { ""type"": ""string"", ""description"": ""建议的显示名称，如 '主相机', '侧面相机' 等"" },
            ""triggerMode"": { ""type"": ""string"", ""description"": ""可选，建议的触发模式，如 'Software' 或 'External'，默认为 'Software'"" },
            ""pixelFormat"": { ""type"": ""string"", ""description"": ""可选，建议的像素格式，如 'BayerRG8' 或 'Mono8'"" }
        },
        ""required"": [""device"", ""suggestedDisplayName""]
    }").RootElement;

    public Task<VisionAgentToolResult> ExecuteAsync(
        VisionAgentToolContext context,
        JsonElement arguments,
        CancellationToken cancellationToken)
    {
        if (arguments.ValueKind != JsonValueKind.Object ||
            !arguments.TryGetProperty("device", out var deviceProp) ||
            deviceProp.ValueKind != JsonValueKind.Object ||
            !arguments.TryGetProperty("suggestedDisplayName", out var nameProp) ||
            nameProp.ValueKind != JsonValueKind.String)
        {
            return Task.FromResult(VisionAgentToolResult.CreateFailure("Missing or invalid required parameters ('device' and 'suggestedDisplayName')."));
        }

        var suggestedDisplayName = nameProp.GetString() ?? "Camera";
        string triggerMode = "Software";
        string pixelFormat = "BayerRG8";

        if (arguments.TryGetProperty("triggerMode", out var trigProp) && trigProp.ValueKind == JsonValueKind.String)
        {
            triggerMode = trigProp.GetString() ?? "Software";
        }

        if (arguments.TryGetProperty("pixelFormat", out var pixProp) && pixProp.ValueKind == JsonValueKind.String)
        {
            pixelFormat = pixProp.GetString() ?? "BayerRG8";
        }

        // Extract device info
        string serialNumber = "";
        string manufacturer = "";
        string ipAddress = "";
        string modelName = "";
        string interfaceType = "";

        if (deviceProp.TryGetProperty("serialNumber", out var snProp)) serialNumber = snProp.GetString() ?? "";
        if (deviceProp.TryGetProperty("manufacturer", out var manProp)) manufacturer = manProp.GetString() ?? "";
        if (deviceProp.TryGetProperty("ipAddress", out var ipProp)) ipAddress = ipProp.GetString() ?? "";
        if (deviceProp.TryGetProperty("modelName", out var modelProp)) modelName = modelProp.GetString() ?? "";
        if (deviceProp.TryGetProperty("interfaceType", out var intProp)) interfaceType = intProp.GetString() ?? "";

        var draftBinding = new
        {
            id = $"cam-{suggestedDisplayName.ToLowerInvariant()}",
            displayName = suggestedDisplayName,
            manufacturer = manufacturer,
            serialNumber = serialNumber,
            ipAddress = ipAddress,
            modelName = modelName,
            interfaceType = interfaceType,
            triggerMode = triggerMode,
            pixelFormat = pixelFormat
        };

        var result = new
        {
            draftBinding = draftBinding,
            requiresUserConfirmation = true
        };

        var summary = $"Draft camera binding prepared for '{suggestedDisplayName}' (SN: {serialNumber}). Requires user confirmation.";
        return Task.FromResult(VisionAgentToolResult.CreateSuccess(result, summary));
    }
}
