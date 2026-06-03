using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ClearVision.Product.Core.AI.Tools;

namespace ClearVision.Product.Infrastructure.AI.Tools;

public sealed class VisionAgentToolRegistry : IVisionAgentToolRegistry
{
    private readonly Dictionary<string, IVisionAgentTool> _tools;

    public VisionAgentToolRegistry(IEnumerable<IVisionAgentTool> tools)
    {
        _tools = tools.ToDictionary(
            t => t.Name,
            t => t,
            StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<VisionAgentToolDescriptor> ListTools()
    {
        return _tools.Values
            .Select(t => new VisionAgentToolDescriptor(
                t.Name,
                t.DisplayName,
                t.Description,
                t.Category,
                t.Permission,
                t.ParametersSchema))
            .ToList();
    }

    public bool TryGet(string name, out IVisionAgentTool tool)
    {
        return _tools.TryGetValue(name, out tool!);
    }

    public async Task<VisionAgentToolResult> ExecuteAsync(
        string name,
        VisionAgentToolContext context,
        JsonElement arguments,
        CancellationToken cancellationToken)
    {
        if (!TryGet(name, out var tool))
        {
            return VisionAgentToolResult.CreateFailure($"Tool '{name}' not found in registry.");
        }

        try
        {
            return await tool.ExecuteAsync(context, arguments, cancellationToken);
        }
        catch (Exception ex)
        {
            return VisionAgentToolResult.CreateFailure($"Error executing tool '{name}': {ex.Message}");
        }
    }
}
