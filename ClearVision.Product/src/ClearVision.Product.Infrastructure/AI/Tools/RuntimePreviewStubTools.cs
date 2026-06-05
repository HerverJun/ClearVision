using System.Text.Json;
using ClearVision.Product.Core.AI.Tools;
using ClearVision.Product.Infrastructure.AI.Agent;

namespace ClearVision.Product.Infrastructure.AI.Tools;

public sealed class RuntimePreviewCaptureStubTool : VisionAgentToolBase
{
    private readonly RuntimePreviewAdapterRegistry _adapterRegistry;

    public RuntimePreviewCaptureStubTool(RuntimePreviewAdapterRegistry? adapterRegistry = null)
    {
        _adapterRegistry = adapterRegistry ?? RuntimePreviewAdapterRegistry.CreateDefault();
    }

    public override string Name => RuntimePreviewPermissionGate.CaptureToolName;
    public override string DisplayName => "Capture test frame stub";
    public override string Description => "Calls the offline RuntimePreview adapter and returns test-frame metadata only.";
    public override string Category => "runtime_preview";
    public override VisionAgentToolPermission Permission => VisionAgentToolPermission.RuntimePreview;
    public override JsonElement ParametersSchema { get; } = Schema("""
        {
          "type": "object",
            "properties": {
            "adapterName": { "type": "string" },
            "cameraBindingId": { "type": "string" },
            "operatorTempId": { "type": "string" },
            "reason": { "type": "string" }
          }
        }
        """);

    public override Task<VisionAgentToolResult> ExecuteAsync(
        VisionAgentToolContext context,
        JsonElement arguments,
        CancellationToken cancellationToken)
    {
        var request = CreateRequest(Name, context, arguments);
        return ExecuteAdapterAsync(request, cancellationToken);
    }

    internal static RuntimePreviewRequest CreateRequest(
        string toolName,
        VisionAgentToolContext context,
        JsonElement arguments)
    {
        return new RuntimePreviewRequest
        {
            ToolName = toolName,
            AdapterName = ReadArgumentString(arguments, "adapterName"),
            PreviewMode = RuntimePreviewModes.OfflineFixture,
            Context = context,
            Arguments = arguments
        };
    }

    internal async Task<VisionAgentToolResult> ExecuteAdapterAsync(
        RuntimePreviewRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _adapterRegistry.ExecuteAsync(request, cancellationToken);
        return result.Success
            ? VisionAgentToolResult.Ok(result)
            : VisionAgentToolResult.Fail(
                result.ErrorCode ?? "runtime_preview_not_ready",
                result.ErrorMessage ?? "RuntimePreview adapter did not produce a ready preview.",
                result);
    }

    private static string? ReadArgumentString(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase) &&
                property.Value.ValueKind == JsonValueKind.String)
            {
                return property.Value.GetString();
            }
        }

        return null;
    }
}

public sealed class RuntimePreviewReplayStubTool : VisionAgentToolBase
{
    private readonly RuntimePreviewAdapterRegistry _adapterRegistry;

    public RuntimePreviewReplayStubTool(RuntimePreviewAdapterRegistry? adapterRegistry = null)
    {
        _adapterRegistry = adapterRegistry ?? RuntimePreviewAdapterRegistry.CreateDefault();
    }

    public override string Name => RuntimePreviewPermissionGate.ReplayToolName;
    public override string DisplayName => "Replay flow with frame stub";
    public override string Description => "Calls the offline RuntimePreview adapter and returns structure-only replay metadata.";
    public override string Category => "runtime_preview";
    public override VisionAgentToolPermission Permission => VisionAgentToolPermission.RuntimePreview;
    public override JsonElement ParametersSchema { get; } = Schema("""
        {
          "type": "object",
            "properties": {
            "adapterName": { "type": "string" },
            "frameId": { "type": "string" },
            "flow": { "type": ["object", "string"] },
            "flowJson": { "type": "string" },
            "entryOperatorTempId": { "type": "string" }
          }
        }
        """);

    public override Task<VisionAgentToolResult> ExecuteAsync(
        VisionAgentToolContext context,
        JsonElement arguments,
        CancellationToken cancellationToken)
    {
        var request = RuntimePreviewCaptureStubTool.CreateRequest(Name, context, arguments);
        return new RuntimePreviewCaptureStubTool(_adapterRegistry)
            .ExecuteAdapterAsync(request, cancellationToken);
    }
}
