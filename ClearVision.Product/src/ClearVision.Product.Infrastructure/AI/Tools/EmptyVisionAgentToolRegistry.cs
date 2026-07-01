using System.Text.Json;
using ClearVision.Product.Core.AI.Tools;

namespace ClearVision.Product.Infrastructure.AI.Tools;

public sealed class EmptyVisionAgentToolRegistry : IVisionAgentToolRegistry
{
    public IReadOnlyList<VisionAgentToolDescriptor> ListTools()
    {
        return Array.Empty<VisionAgentToolDescriptor>();
    }

    public bool TryGet(string name, out IVisionAgentTool tool)
    {
        tool = null!;
        return false;
    }

    public Task<VisionAgentToolResult> ExecuteAsync(
        string name,
        VisionAgentToolContext context,
        JsonElement arguments,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(VisionAgentToolResult.Fail(
            "unknown_tool",
            $"Vision agent tool '{name}' is not registered."));
    }
}
