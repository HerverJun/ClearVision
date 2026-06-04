using System.Text.Json;

namespace ClearVision.Product.Core.AI.Tools;

public sealed record VisionAgentToolDescriptor
{
    public string Name { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public VisionAgentToolPermission Permission { get; init; } = VisionAgentToolPermission.ReadOnly;
    public JsonElement ParametersSchema { get; init; }

    public static VisionAgentToolDescriptor FromTool(IVisionAgentTool tool)
    {
        return new VisionAgentToolDescriptor
        {
            Name = tool.Name,
            DisplayName = tool.DisplayName,
            Description = tool.Description,
            Category = tool.Category,
            Permission = tool.Permission,
            ParametersSchema = tool.ParametersSchema.Clone()
        };
    }
}
