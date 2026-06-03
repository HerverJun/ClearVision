using System.Collections.Generic;

namespace ClearVision.Product.Core.AI.Tools;

public class VisionAgentToolContext
{
    public string SessionId { get; set; } = string.Empty;
    public string? ExistingFlowJson { get; set; }
    public string? TargetStationId { get; set; }
    public Dictionary<string, object> ExtraProperties { get; } = new();
}
