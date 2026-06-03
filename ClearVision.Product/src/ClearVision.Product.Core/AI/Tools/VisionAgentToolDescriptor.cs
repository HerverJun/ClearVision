using System.Text.Json;

namespace ClearVision.Product.Core.AI.Tools;

public sealed record VisionAgentToolDescriptor(
    string Name,
    string DisplayName,
    string Description,
    string Category,
    VisionAgentToolPermission Permission,
    JsonElement ParametersSchema
);
