namespace ClearVision.Product.Infrastructure.AI.Agent;

public sealed class AgentToolCallPolicy
{
    private static readonly HashSet<string> AllowedToolNames = new(
    [
        "list_operator_catalog",
        "get_operator_schema",
        "retrieve_operator_knowledge",
        "match_flow_template",
        "get_flow_template_skeleton",
        "inspect_current_flow",
        "validate_flow",
        "dryrun_flow",
        "runtime_package_precheck"
    ], StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> DeniedToolNames = new(
    [
        RuntimePreviewPermissionGate.CaptureToolName,
        RuntimePreviewPermissionGate.ReplayToolName
    ], StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<string> ListAllowedToolNames(bool runtimePreviewConsent = false)
    {
        var names = AllowedToolNames.AsEnumerable();
        if (runtimePreviewConsent)
        {
            names = names.Concat(DeniedToolNames);
        }

        return names
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public AgentToolCallPolicyResult Validate(
        VisionAgentProtocolMessage message,
        bool runtimePreviewConsent = false)
    {
        if (!message.IsToolCall)
        {
            return AgentToolCallPolicyResult.Allow();
        }

        foreach (var call in message.ToolCalls)
        {
            var result = ValidateToolName(call.Name, runtimePreviewConsent);
            if (!result.Allowed)
            {
                return result;
            }
        }

        return AgentToolCallPolicyResult.Allow();
    }

    public AgentToolCallPolicyResult ValidateToolName(
        string? toolName,
        bool runtimePreviewConsent = false)
    {
        if (string.IsNullOrWhiteSpace(toolName))
        {
            return AgentToolCallPolicyResult.Deny(
                "tool_name_missing",
                "Planner tool call is missing a tool name.");
        }

        if (DeniedToolNames.Contains(toolName))
        {
            if (runtimePreviewConsent)
            {
                return AgentToolCallPolicyResult.Allow();
            }

            return AgentToolCallPolicyResult.Deny(
                RuntimePreviewPermissionGate.ConsentRequiredErrorCode,
                $"Planner tool '{toolName}' requires explicit RuntimePreview consent.");
        }

        if (!string.Equals(toolName, "runtime_package_precheck", StringComparison.OrdinalIgnoreCase) &&
            LooksLikeDeploymentPrepareTool(toolName))
        {
            return AgentToolCallPolicyResult.Deny(
                "deployment_prepare_tool_denied",
                "DeploymentPrepare is only allowed for runtime_package_precheck.");
        }

        if (LooksLikeConfigWriteTool(toolName))
        {
            return AgentToolCallPolicyResult.Deny(
                "config_write_denied",
                "ConfigWrite tools are always denied for Vision Agent planner calls.");
        }

        if (!AllowedToolNames.Contains(toolName))
        {
            return AgentToolCallPolicyResult.Deny(
                "tool_not_whitelisted",
                $"Planner tool '{toolName}' is outside the allowed tool set.");
        }

        return AgentToolCallPolicyResult.Allow();
    }

    private static bool LooksLikeDeploymentPrepareTool(string toolName)
    {
        return toolName.Contains("runtime_package", StringComparison.OrdinalIgnoreCase) ||
               toolName.Contains("station_package", StringComparison.OrdinalIgnoreCase) ||
               toolName.Contains("deployment", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeConfigWriteTool(string toolName)
    {
        return toolName.Contains("config_write", StringComparison.OrdinalIgnoreCase) ||
               toolName.Contains("write_config", StringComparison.OrdinalIgnoreCase);
    }
}

public sealed record AgentToolCallPolicyResult(
    bool Allowed,
    string? ErrorCode = null,
    string? ErrorMessage = null)
{
    public static AgentToolCallPolicyResult Allow() => new(true);

    public static AgentToolCallPolicyResult Deny(string errorCode, string errorMessage) =>
        new(false, errorCode, errorMessage);
}

public sealed class AgentToolCallPolicyViolationException : Exception
{
    public AgentToolCallPolicyViolationException(string errorCode, string message)
        : base(message)
    {
        ErrorCode = errorCode;
    }

    public string ErrorCode { get; }
}
