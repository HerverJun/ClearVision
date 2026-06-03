using System.Text.Json;
using ClearVision.Product.Core.AI.Tools;
using Microsoft.Extensions.Logging;

namespace ClearVision.Product.Infrastructure.AI.Tools;

public sealed class VisionAgentToolRegistry : IVisionAgentToolRegistry
{
    private readonly IReadOnlyDictionary<string, IVisionAgentTool> _tools;
    private readonly Microsoft.Extensions.Logging.ILogger<VisionAgentToolRegistry> _logger;

    public VisionAgentToolRegistry(
        IEnumerable<IVisionAgentTool> tools,
        Microsoft.Extensions.Logging.ILogger<VisionAgentToolRegistry> logger)
    {
        _tools = tools
            .GroupBy(tool => tool.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToDictionary(tool => tool.Name, StringComparer.OrdinalIgnoreCase);
        _logger = logger;
    }

    public IReadOnlyList<VisionAgentToolDescriptor> ListTools()
    {
        return _tools.Values
            .OrderBy(tool => tool.Category, StringComparer.OrdinalIgnoreCase)
            .ThenBy(tool => tool.Name, StringComparer.OrdinalIgnoreCase)
            .Select(VisionAgentToolDescriptor.FromTool)
            .ToList();
    }

    public bool TryGet(string name, out IVisionAgentTool tool)
    {
        if (!string.IsNullOrWhiteSpace(name) &&
            _tools.TryGetValue(name.Trim(), out var found))
        {
            tool = found;
            return true;
        }

        tool = null!;
        return false;
    }

    public async Task<VisionAgentToolResult> ExecuteAsync(
        string name,
        VisionAgentToolContext context,
        JsonElement arguments,
        CancellationToken cancellationToken)
    {
        if (!TryGet(name, out var tool))
        {
            return VisionAgentToolResult.Fail(
                "unknown_tool",
                $"Vision agent tool '{name}' is not registered.");
        }

        if (tool.Permission == VisionAgentToolPermission.ConfigWrite)
        {
            return VisionAgentToolResult.Fail(
                "permission_denied",
                $"Tool '{tool.Name}' requires ConfigWrite, which is never allowed for Vision Agent v1.");
        }

        if (tool.Permission == VisionAgentToolPermission.DeploymentPrepare &&
            !string.Equals(tool.Name, "runtime_package_precheck", StringComparison.OrdinalIgnoreCase))
        {
            return VisionAgentToolResult.Fail(
                "permission_denied",
                $"Tool '{tool.Name}' requests DeploymentPrepare, but Vision Agent v1 only allows deployment precheck.");
        }

        if (!context.AllowedPermissions.Contains(tool.Permission))
        {
            return VisionAgentToolResult.Fail(
                "permission_denied",
                $"Tool '{tool.Name}' requires permission '{tool.Permission}', which is not allowed in this session.");
        }

        try
        {
            return await tool.ExecuteAsync(context, arguments, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Vision agent tool {ToolName} failed: {Error}", tool.Name, ex.Message);
            return VisionAgentToolResult.Fail("tool_exception", ex.Message);
        }
    }
}
