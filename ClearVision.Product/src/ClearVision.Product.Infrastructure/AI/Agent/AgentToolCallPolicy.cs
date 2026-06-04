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
        "capture_" + "test_frame",
        "replay_" + "flow_with_frame"
    ], StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<string> ListAllowedToolNames()
    {
        return AllowedToolNames
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public AgentToolCallPolicyResult Validate(VisionAgentProtocolMessage message)
    {
        if (!message.IsToolCall)
        {
            return AgentToolCallPolicyResult.Allow();
        }

        foreach (var call in message.ToolCalls)
        {
            var result = ValidateToolName(call.Name);
            if (!result.Allowed)
            {
                return result;
            }
        }

        return AgentToolCallPolicyResult.Allow();
    }

    public AgentToolCallPolicyResult ValidateToolName(string? toolName)
    {
        if (string.IsNullOrWhiteSpace(toolName))
        {
            return AgentToolCallPolicyResult.Deny(
                "tool_name_missing",
                "Planner tool call is missing a tool name.");
        }

        if (DeniedToolNames.Contains(toolName))
        {
            return AgentToolCallPolicyResult.Deny(
                "tool_policy_denied",
                $"Planner tool '{toolName}' is not allowed in this phase.");
        }

        if (!AllowedToolNames.Contains(toolName))
        {
            return AgentToolCallPolicyResult.Deny(
                "tool_not_whitelisted",
                $"Planner tool '{toolName}' is outside the allowed tool set.");
        }

        if (!string.Equals(toolName, "runtime_package_precheck", StringComparison.OrdinalIgnoreCase) &&
            toolName.Contains("precheck", StringComparison.OrdinalIgnoreCase))
        {
            return AgentToolCallPolicyResult.Deny(
                "deployment_prepare_tool_denied",
                "DeploymentPrepare is only allowed for runtime_package_precheck.");
        }

        return AgentToolCallPolicyResult.Allow();
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
