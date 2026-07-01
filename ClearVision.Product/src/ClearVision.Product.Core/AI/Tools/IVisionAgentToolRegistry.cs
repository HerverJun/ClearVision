using System.Text.Json;

namespace ClearVision.Product.Core.AI.Tools;

public interface IVisionAgentToolRegistry
{
    IReadOnlyList<VisionAgentToolDescriptor> ListTools();

    bool TryGet(string name, out IVisionAgentTool tool);

    Task<VisionAgentToolResult> ExecuteAsync(
        string name,
        VisionAgentToolContext context,
        JsonElement arguments,
        CancellationToken cancellationToken);
}
