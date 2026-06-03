using System;
using System.Text.Json;
using System.Text.RegularExpressions;
using ClearVision.Product.Core.DTOs;

namespace ClearVision.Product.Infrastructure.AI.Agent;

public sealed class VisionAgentProtocolParser
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public bool TryParseToolCalls(string rawResponse, out VisionAgentToolCallRequest? request)
    {
        request = null;
        try
        {
            var cleanedJson = ExtractJsonContent(rawResponse);
            if (string.IsNullOrWhiteSpace(cleanedJson))
                return false;

            using var doc = JsonDocument.Parse(cleanedJson);
            if (doc.RootElement.TryGetProperty("kind", out var kindProp) && 
                string.Equals(kindProp.GetString(), "tool_call", StringComparison.OrdinalIgnoreCase))
            {
                request = JsonSerializer.Deserialize<VisionAgentToolCallRequest>(cleanedJson, _jsonOptions);
                return request != null && request.ToolCalls.Count > 0;
            }
        }
        catch
        {
            // Ignore parse errors and return false
        }
        return false;
    }

    public bool TryParseFinalFlow(string rawResponse, out AiGeneratedFlowJson? flow)
    {
        flow = null;
        try
        {
            var cleanedJson = ExtractJsonContent(rawResponse);
            if (string.IsNullOrWhiteSpace(cleanedJson))
                return false;

            using var doc = JsonDocument.Parse(cleanedJson);
            
            // Check if it is marked as final_flow
            bool isFinalFlow = false;
            if (doc.RootElement.TryGetProperty("kind", out var kindProp))
            {
                isFinalFlow = string.Equals(kindProp.GetString(), "final_flow", StringComparison.OrdinalIgnoreCase);
            }
            else if (doc.RootElement.TryGetProperty("operators", out var opsProp) && opsProp.ValueKind == JsonValueKind.Array)
            {
                // Fallback: If "operators" exists as an array, treat it as a final flow even without "kind"
                isFinalFlow = true;
            }

            if (isFinalFlow)
            {
                flow = JsonSerializer.Deserialize<AiGeneratedFlowJson>(cleanedJson, _jsonOptions);
                return flow != null;
            }
        }
        catch
        {
            // Ignore parse errors and return false
        }
        return false;
    }

    public static string ExtractJsonContent(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var trimmed = text.Trim();

        // 1. Try to strip markdown code blocks: ```json ... ```
        var match = Regex.Match(trimmed, @"```(?:json)?\s*(\{[\s\S]*?\})\s*```", RegexOptions.IgnoreCase);
        if (match.Success)
        {
            return match.Groups[1].Value.Trim();
        }

        // 2. Find the first '{' and last '}'
        int firstBrace = trimmed.IndexOf('{');
        int lastBrace = trimmed.LastIndexOf('}');
        if (firstBrace >= 0 && lastBrace > firstBrace)
        {
            return trimmed.Substring(firstBrace, lastBrace - firstBrace + 1);
        }

        return trimmed;
    }
}

public sealed class VisionAgentToolCallRequest
{
    public string Kind { get; set; } = "tool_call";
    public List<VisionAgentToolCallItem> ToolCalls { get; set; } = new();
}

public sealed class VisionAgentToolCallItem
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public JsonElement Arguments { get; set; }
}
