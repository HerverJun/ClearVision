using System.Text.Json;

namespace ClearVision.Product.Core.AI.Tools;

public interface IVisionAgentTool
{
    string Name { get; }
    string DisplayName { get; }
    string Description { get; }
    string Category { get; }
    VisionAgentToolPermission Permission { get; }
    JsonElement ParametersSchema { get; }

    Task<VisionAgentToolResult> ExecuteAsync(
        VisionAgentToolContext context,
        JsonElement arguments,
        CancellationToken cancellationToken);
}

