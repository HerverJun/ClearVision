using System.Text;
using System.Text.Json;
using ClearVision.Product.Infrastructure.AI;

namespace ClearVision.Product.Infrastructure.AI.Agent;

public sealed class AgentPlannerPromptComposer
{
    public AgentPlannerPrompt Compose(
        AgentPlannerCompletionRequest request,
        AgentPlannerCompletionOptions options)
    {
        options.Normalize();
        return new AgentPlannerPrompt(
            BuildSystemPrompt(request),
            BuildMessages(request, options));
    }

    public AgentPlannerPrompt ComposeRepair(
        AgentPlannerCompletionRequest request,
        string invalidOutput,
        string failureReason,
        AgentPlannerCompletionOptions options)
    {
        options.Normalize();
        var systemPrompt = string.Join(Environment.NewLine,
        [
            "Repair a ClearVision Vision Engineering Agent planner response.",
            "Return exactly one valid JSON object and no prose.",
            "Keep the original intent, but make it match the tool_call or final protocol.",
            BuildProtocolContract(request.AllowedToolNames)
        ]);
        var messages = new List<ChatMessage>
        {
            new("user", string.Join(Environment.NewLine,
            [
                $"Repair reason: {Truncate(failureReason, 1_000)}",
                "Invalid planner output:",
                Truncate(invalidOutput, options.MaxMessageChars),
                "Planner context:",
                BuildContextBlock(request, options)
            ]))
        };
        return new AgentPlannerPrompt(systemPrompt, messages);
    }

    private static string BuildSystemPrompt(AgentPlannerCompletionRequest request)
    {
        return string.Join(Environment.NewLine,
        [
            "You are the controlled ClearVision Vision Engineering Agent planner.",
            "You plan or edit workflow drafts by selecting allowed static tools and by returning final draft JSON.",
            "For shadow-eval and planning tasks, output the complete ordered tool plan in a single tool_call response when more than one tool is needed.",
            "Do not output only the next action unless the user explicitly asks for one step. The default is full_plan, not next_action.",
            "Do not request real resource access, deployment, packaging, hot loading, configuration writes, image loading, model loading, camera access, PLC access, station access, or external network actions.",
            BuildFullPlanInstruction(),
            BuildPreviewInstruction(request),
            BuildProtocolContract(request.AllowedToolNames),
            BuildFewShotExamples()
        ]);
    }

    private static string BuildPreviewInstruction(AgentPlannerCompletionRequest request)
    {
        var runtimePreviewAllowed = request.AllowedToolNames.Any(RuntimePreviewPermissionGate.IsRuntimePreviewTool);
        return runtimePreviewAllowed
            ? "Runtime preview tools are enabled for this request only as metadata-only stubs; do not request real capture, binary artifacts, or runtime resource access."
            : $"Denied preview tools: {RuntimePreviewPermissionGate.CaptureToolName}, {RuntimePreviewPermissionGate.ReplayToolName}.";
    }

    private static string BuildProtocolContract(IReadOnlyList<string> allowedToolNames)
    {
        var tools = allowedToolNames.Count == 0
            ? "(none provided)"
            : string.Join(", ", allowedToolNames.OrderBy(name => name, StringComparer.OrdinalIgnoreCase));
        return string.Join(Environment.NewLine,
        [
            "Allowed tools: " + tools,
            "Return one of these JSON objects:",
            "{\"kind\":\"tool_call\",\"toolCalls\":[{\"name\":\"list_operator_catalog\",\"arguments\":{}}]}",
            "{\"kind\":\"final\",\"workflowDraft\":{\"operators\":[],\"connections\":[]},\"missingResources\":[],\"pendingActions\":[],\"validationPreview\":{}}",
            "{\"kind\":\"final\",\"draftEdits\":[{\"op\":\"replace_operator_parameter\",\"tempId\":\"op_match\",\"parameterName\":\"TemplatePath\",\"value\":\"<pending-template-path>\"}]}",
            "Missing CameraBindingId, ModelPath, TemplatePath, PLC parameters, and output channels must remain as workflow draft placeholders plus missingResources/pendingActions; they must not block workflowDraftAllowed."
        ]);
    }

    private static string BuildFullPlanInstruction()
    {
        return string.Join(Environment.NewLine,
        [
            "Full ordered-plan policy:",
            "- Generation tasks normally require: match_flow_template -> get_flow_template_skeleton -> validate_flow -> dryrun_flow.",
            "- Parameter completion tasks normally require: get_operator_schema -> validate_flow -> runtime_package_precheck.",
            "- RuntimePreview with runtimePreviewConsent=true requires: validate_flow -> capture_test_frame -> replay_flow_with_frame.",
            "- RuntimePreview with runtimePreviewConsent=false must not call capture_test_frame or replay_flow_with_frame; return final pending authorization or only validate_flow.",
            "- DeploymentPrepare allows only runtime_package_precheck; any other deployment-like tool is forbidden.",
            "- ConfigWrite is permanently forbidden.",
            "- Non-whitelisted tools are forbidden.",
            "- Return planner protocol JSON only; no prose, markdown, comments, or natural-language explanation."
        ]);
    }

    private static string BuildFewShotExamples()
    {
        return string.Join(Environment.NewLine,
        [
            "Few-shot full ordered-plan examples:",
            "wire_sequence generation full plan:",
            "{\"kind\":\"tool_call\",\"toolCalls\":[{\"name\":\"match_flow_template\",\"arguments\":{\"task\":\"wire_sequence\"}},{\"name\":\"get_flow_template_skeleton\",\"arguments\":{\"templateId\":\"wire_sequence\"}},{\"name\":\"validate_flow\",\"arguments\":{}},{\"name\":\"dryrun_flow\",\"arguments\":{}}]}",
            "template_matching generation full plan:",
            "{\"kind\":\"tool_call\",\"toolCalls\":[{\"name\":\"match_flow_template\",\"arguments\":{\"task\":\"template_matching\"}},{\"name\":\"get_flow_template_skeleton\",\"arguments\":{\"templateId\":\"template_matching\"}},{\"name\":\"validate_flow\",\"arguments\":{}},{\"name\":\"dryrun_flow\",\"arguments\":{}}]}",
            "hole_distance generation full plan:",
            "{\"kind\":\"tool_call\",\"toolCalls\":[{\"name\":\"match_flow_template\",\"arguments\":{\"task\":\"hole_distance\"}},{\"name\":\"get_flow_template_skeleton\",\"arguments\":{\"templateId\":\"hole_distance\"}},{\"name\":\"validate_flow\",\"arguments\":{}},{\"name\":\"dryrun_flow\",\"arguments\":{}}]}",
            "parameter completion then validate/precheck:",
            "{\"kind\":\"tool_call\",\"toolCalls\":[{\"name\":\"get_operator_schema\",\"arguments\":{\"operatorType\":\"TemplateMatching\"}},{\"name\":\"validate_flow\",\"arguments\":{}},{\"name\":\"runtime_package_precheck\",\"arguments\":{}}]}",
            "RuntimePreview consent=true:",
            "{\"kind\":\"tool_call\",\"toolCalls\":[{\"name\":\"validate_flow\",\"arguments\":{}},{\"name\":\"capture_test_frame\",\"arguments\":{\"mode\":\"offline_metadata_only\"}},{\"name\":\"replay_flow_with_frame\",\"arguments\":{\"mode\":\"offline_metadata_only\"}}]}",
            "RuntimePreview consent=false:",
            "{\"kind\":\"final\",\"missingResources\":[],\"pendingActions\":[{\"type\":\"runtime_preview_authorization_required\",\"message\":\"RuntimePreview requires explicit consent before capture/replay.\"}],\"validationPreview\":{}}",
            "ConfigWrite denied:",
            "{\"kind\":\"final\",\"missingResources\":[],\"pendingActions\":[{\"type\":\"config_write_denied\",\"message\":\"ConfigWrite tools are permanently denied.\"}],\"validationPreview\":{}}",
            "DeploymentPrepare non-precheck denied:",
            "{\"kind\":\"tool_call\",\"toolCalls\":[{\"name\":\"runtime_package_precheck\",\"arguments\":{}}]}"
        ]);
    }

    private static List<ChatMessage> BuildMessages(
        AgentPlannerCompletionRequest request,
        AgentPlannerCompletionOptions options)
    {
        var messages = new List<ChatMessage>
        {
            new("user", BuildContextBlock(request, options))
        };

        foreach (var message in request.Messages
                     .TakeLast(options.MaxMessages)
                     .Where(message => !string.IsNullOrWhiteSpace(message.Content)))
        {
            var role = string.Equals(message.Role, "assistant", StringComparison.OrdinalIgnoreCase)
                ? "assistant"
                : "user";
            messages.Add(new ChatMessage(role, FormatLoopMessage(message, options.MaxMessageChars)));
        }

        return messages;
    }

    private static string BuildContextBlock(
        AgentPlannerCompletionRequest request,
        AgentPlannerCompletionOptions options)
    {
        var generation = request.GenerationRequest;
        var builder = new StringBuilder();
        builder.AppendLine("Planner context:");
        builder.AppendLine($"userRequest={Truncate(generation.Description, options.MaxMessageChars)}");
        builder.AppendLine($"mode={(string.IsNullOrWhiteSpace(generation.ExistingFlowJson) ? "new_workflow_draft" : "edit_existing_workflow_draft")}");
        builder.AppendLine($"agentGenerateFlowMode={generation.AgentGenerateFlowMode}");
        builder.AppendLine($"runtimePreviewConsent={generation.RuntimePreviewConsent.ToString().ToLowerInvariant()}");
        if (!string.IsNullOrWhiteSpace(request.PlannerPrompt))
        {
            builder.AppendLine("plannerPolicy:");
            builder.AppendLine(Truncate(request.PlannerPrompt, options.MaxSummaryChars));
        }

        if (!string.IsNullOrWhiteSpace(generation.ExistingFlowJson))
        {
            builder.AppendLine("existingFlowJsonSummary:");
            builder.AppendLine(SummarizeJsonText(generation.ExistingFlowJson, options.MaxSummaryChars));
        }

        AppendJsonSummary(builder, "flowDraftSummary", request.FlowDraft, options.MaxSummaryChars);
        AppendJsonSummary(builder, "validationSummary", request.ValidationSummary, options.MaxSummaryChars);
        AppendJsonSummary(builder, "dryRunSummary", request.DryRunSummary, options.MaxSummaryChars);
        AppendJsonSummary(builder, "deploymentPrecheckSummary", request.DeploymentPrecheck, options.MaxSummaryChars);
        return builder.ToString();
    }

    private static string FormatLoopMessage(VisionAgentLoopMessage message, int maxChars)
    {
        return string.Join(Environment.NewLine,
        [
            $"loopMessageRole={message.Role}",
            Truncate(message.Content, maxChars)
        ]);
    }

    private static void AppendJsonSummary(
        StringBuilder builder,
        string label,
        JsonElement element,
        int maxChars)
    {
        if (element.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            return;
        }

        builder.AppendLine(label + ":");
        builder.AppendLine(Truncate(element.GetRawText(), maxChars));
    }

    private static string SummarizeJsonText(string text, int maxChars)
    {
        try
        {
            using var doc = JsonDocument.Parse(text);
            return Truncate(doc.RootElement.GetRawText(), maxChars);
        }
        catch (JsonException)
        {
            return Truncate(text, maxChars);
        }
    }

    private static string Truncate(string? text, int maxChars)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        return text.Length <= maxChars
            ? text
            : text[..maxChars] + "...[truncated]";
    }
}

public sealed record AgentPlannerPrompt(
    string SystemPrompt,
    List<ChatMessage> Messages);
