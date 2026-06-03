using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ClearVision.Product.Core.AI.Tools;

namespace ClearVision.Product.Infrastructure.AI.Tools;

public sealed class CurrentFlowInspectTool : IVisionAgentTool
{
    public string Name => "inspect_current_flow";
    public string DisplayName => "检查当前工作流";
    public string Description => "获取当前画布上已有工作流的结构摘要（包括算子临时ID、类型、显示名称以及连线关系），主要用于修改已有流程的场景。";
    public string Category => "Context";
    public VisionAgentToolPermission Permission => VisionAgentToolPermission.ReadOnly;

    public JsonElement ParametersSchema { get; } = JsonDocument.Parse(@"{
        ""type"": ""object"",
        ""properties"": {}
    }").RootElement;

    public Task<VisionAgentToolResult> ExecuteAsync(
        VisionAgentToolContext context,
        JsonElement arguments,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(context.ExistingFlowJson))
        {
            var emptyResult = new
            {
                hasFlow = false,
                operatorCount = 0,
                connectionCount = 0,
                operators = Array.Empty<object>(),
                connections = Array.Empty<string>(),
                warnings = new[] { "No existing flow found in context." }
            };
            return Task.FromResult(VisionAgentToolResult.CreateSuccess(emptyResult, "No existing flow."));
        }

        try
        {
            using var doc = JsonDocument.Parse(context.ExistingFlowJson);
            var root = doc.RootElement;

            var operatorsList = new List<object>();
            if (root.TryGetProperty("operators", out var opsProp) && opsProp.ValueKind == JsonValueKind.Array)
            {
                foreach (var op in opsProp.EnumerateArray())
                {
                    string id = op.TryGetProperty("id", out var idProp) ? idProp.GetString() ?? "" : "";
                    if (string.IsNullOrEmpty(id) && op.TryGetProperty("tempId", out var tempIdProp))
                    {
                        id = tempIdProp.GetString() ?? "";
                    }

                    string type = op.TryGetProperty("operatorType", out var typeProp) ? typeProp.GetString() ?? "" : "";
                    string disp = op.TryGetProperty("displayName", out var dispProp) ? dispProp.GetString() ?? "" : "";

                    operatorsList.Add(new
                    {
                        id,
                        operatorType = type,
                        displayName = disp
                    });
                }
            }

            var connectionsList = new List<string>();
            if (root.TryGetProperty("connections", out var connsProp) && connsProp.ValueKind == JsonValueKind.Array)
            {
                foreach (var conn in connsProp.EnumerateArray())
                {
                    string src = conn.TryGetProperty("sourceTempId", out var srcProp) ? srcProp.GetString() ?? "" : "";
                    if (string.IsNullOrEmpty(src) && conn.TryGetProperty("sourceId", out var srcIdProp))
                    {
                        src = srcIdProp.GetString() ?? "";
                    }

                    string srcPort = conn.TryGetProperty("sourcePortName", out var srcPortProp) ? srcPortProp.GetString() ?? "" : "";
                    
                    string tgt = conn.TryGetProperty("targetTempId", out var tgtProp) ? tgtProp.GetString() ?? "" : "";
                    if (string.IsNullOrEmpty(tgt) && conn.TryGetProperty("targetId", out var tgtIdProp))
                    {
                        tgt = tgtIdProp.GetString() ?? "";
                    }

                    string tgtPort = conn.TryGetProperty("targetPortName", out var tgtPortProp) ? tgtPortProp.GetString() ?? "" : "";

                    connectionsList.Add($"{src}.{srcPort} -> {tgt}.{tgtPort}");
                }
            }

            var result = new
            {
                hasFlow = true,
                operatorCount = operatorsList.Count,
                connectionCount = connectionsList.Count,
                operators = operatorsList,
                connections = connectionsList,
                warnings = Array.Empty<string>()
            };

            var summary = $"Existing flow has {operatorsList.Count} operators, {connectionsList.Count} connections";
            return Task.FromResult(VisionAgentToolResult.CreateSuccess(result, summary));
        }
        catch (Exception ex)
        {
            var errResult = new
            {
                hasFlow = false,
                operatorCount = 0,
                connectionCount = 0,
                operators = Array.Empty<object>(),
                connections = Array.Empty<string>(),
                warnings = new[] { $"Failed to parse existing flow: {ex.Message}" }
            };
            return Task.FromResult(VisionAgentToolResult.CreateSuccess(errResult, $"Error parsing flow: {ex.Message}"));
        }
    }
}
