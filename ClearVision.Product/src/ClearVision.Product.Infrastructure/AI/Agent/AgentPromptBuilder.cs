using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using ClearVision.Product.Core.AI.Tools;

namespace ClearVision.Product.Infrastructure.AI.Agent;

public sealed class AgentPromptBuilder
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true
    };

    public string BuildSystemPrompt(
        string promptMode,
        IReadOnlyList<VisionAgentToolDescriptor> tools,
        bool supportsJsonMode,
        bool supportsNativeToolCalls = false)
    {
        var normalizedMode = AiPromptModes.Normalize(promptMode);
        var sb = new StringBuilder();
        sb.AppendLine("You are ClearVision Vision Engineering Agent.");
        sb.AppendLine("You help engineers generate, validate, debug, and prepare deployment for ClearVision visual inspection workflows.");
        sb.AppendLine("Use only ClearVision internal tools listed in this session.");
        sb.AppendLine("Never request CMD, PowerShell, shell execution, arbitrary filesystem access, or OS-level permissions.");
        sb.AppendLine("Do not invent operator types, port names, parameter names, camera IDs, PLC addresses, model paths, calibration files, or station IDs.");
        sb.AppendLine("When information is missing, call tools or mark it as pending.");
        sb.AppendLine("Config write and deployment actions must be returned as drafts requiring user confirmation.");
        sb.AppendLine("Call validate_flow before final output when you create or modify a flow.");
        sb.AppendLine("Call dryrun_flow after validation when the flow is structurally ready.");
        sb.AppendLine("Use replay_flow_with_frame only when a temporaryFrameId is available; do not treat dryrun_flow as real-image verification.");
        sb.AppendLine();
        sb.AppendLine($"PromptMode: {normalizedMode}");
        sb.AppendLine($"JsonModeSupportedByModel: {supportsJsonMode}");
        sb.AppendLine($"NativeToolCallSupportedByModel: {supportsNativeToolCalls}");
        sb.AppendLine();
        sb.AppendLine("Tool calling protocol:");
        sb.AppendLine("If native tool calls are available, call tools through the provider tool-calling API.");
        sb.AppendLine("If native tool calls are not available, return exactly one JSON object for tool calls:");
        sb.AppendLine("""
            {
              "kind": "tool_call",
              "toolCalls": [
                { "id": "call_1", "name": "list_operator_catalog", "arguments": { "keyword": "template matching", "topN": 20 } }
              ]
            }
            """);
        sb.AppendLine("For the final result, return the normal ClearVision flow JSON plus kind=final_flow:");
        sb.AppendLine("""
            {
              "kind": "final_flow",
              "schemaVersion": "1.0",
              "generationMode": "free_generate",
              "templateLockLevel": "none",
              "explanation": "...",
              "operators": [],
              "connections": [],
              "parametersNeedingReview": {},
              "pendingParameters": [],
              "missingResources": [],
              "pendingActions": []
            }
            """);
        sb.AppendLine();
        sb.AppendLine("Available tools:");
        sb.AppendLine(JsonSerializer.Serialize(tools.Select(tool => new
        {
            tool.Name,
            tool.DisplayName,
            tool.Description,
            tool.Category,
            permission = tool.Permission.ToString(),
            parametersSchema = tool.ParametersSchema
        }), JsonOptions));
        if (normalizedMode == AiPromptModes.Hybrid)
        {
            sb.AppendLine();
            sb.AppendLine("Hybrid mode guidance: prefer tools for schemas and knowledge. You may use common workflow backbone patterns such as ImageAcquisition -> inspection -> ResultJudgment -> ResultOutput, but still verify operator schemas through tools.");
        }

        return sb.ToString();
    }
}
