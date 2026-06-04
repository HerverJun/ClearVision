using System.Text.Json;
using ClearVision.Product.Core.AI.Tools;

namespace ClearVision.Product.Infrastructure.AI.Agent;

public sealed class AgentPromptBuilder
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public string BuildSystemPrompt(IReadOnlyList<VisionAgentToolDescriptor> tools)
    {
        return string.Join(Environment.NewLine,
        [
            "You are a ClearVision Vision Engineering Agent skeleton.",
            "Use only the ClearVision internal tools listed in this session.",
            "Never request CMD, PowerShell, shell execution, arbitrary filesystem access, or OS-level permissions.",
            "Do not claim real camera capture, real frame replay, real Station access, or real model verification.",
            "Return either a final answer or a JSON tool_call object.",
            "Available tools:",
            JsonSerializer.Serialize(tools.Select(tool => new
            {
                tool.Name,
                tool.DisplayName,
                tool.Description,
                tool.Category,
                permission = tool.Permission.ToString(),
                parametersSchema = tool.ParametersSchema
            }), JsonOptions)
        ]);
    }
}
