namespace ClearVision.Product.Core.DTOs;

public enum AiAgentGenerateFlowPolicyKind
{
    Production,
    DeveloperOnly,
    OfflineEvaluation,
    Deprecated
}

public sealed record AiAgentGenerateFlowModeDecision(
    bool Allowed,
    string RequestedMode,
    string EffectiveMode,
    string FailureCode,
    string FailureMessage);

public static class AiAgentGenerateFlowModePolicy
{
    public const string PlannerRetiredCode = "agent_generate_flow_planner_retired";
    public const string ToolLoopUnavailableCode = "build_tool_loop_not_available_in_production";
    public const string InvalidModeCode = "agent_generate_flow_mode_invalid";

    public static AiAgentGenerateFlowModeDecision Evaluate(
        string? requestedMode,
        AiAgentGenerateFlowPolicyKind policyKind)
    {
        var requested = requestedMode?.Trim().ToLowerInvariant() ?? string.Empty;
        var normalized = string.IsNullOrWhiteSpace(requested)
            ? AiAgentGenerateFlowModes.Scripted
            : requested;

        if (!IsKnown(normalized))
        {
            return Reject(
                normalized,
                InvalidModeCode,
                "Agent GenerateFlow 模式无效；请使用 scripted，或返回 Plan 视图后通过正式 BuildFromPlan 重试。");
        }

        if (policyKind is AiAgentGenerateFlowPolicyKind.Production or AiAgentGenerateFlowPolicyKind.Deprecated)
        {
            if (normalized.Equals(AiAgentGenerateFlowModes.Planner, StringComparison.OrdinalIgnoreCase))
            {
                return Reject(
                    normalized,
                    PlannerRetiredCode,
                    "旧 Planner 已从生产 GenerateFlow 入口退役；请返回 Plan 视图修正需求或使用正式 BuildFromPlan 重试。");
            }

            if (normalized.Equals(AiAgentGenerateFlowModes.ToolLoop, StringComparison.OrdinalIgnoreCase))
            {
                return Reject(
                    normalized,
                    ToolLoopUnavailableCode,
                    "Tool Loop 不可用于生产构建；请返回 Plan 视图修正需求或使用正式 BuildFromPlan 重试。");
            }
        }

        return new AiAgentGenerateFlowModeDecision(
            true,
            normalized,
            normalized,
            string.Empty,
            string.Empty);
    }

    public static bool IsKnown(string? mode)
    {
        return mode?.Trim().ToLowerInvariant() is
            AiAgentGenerateFlowModes.Scripted or
            AiAgentGenerateFlowModes.Planner or
            AiAgentGenerateFlowModes.ToolLoop;
    }

    public static AiAgentGenerateFlowPolicyKind ParsePolicy(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "production" => AiAgentGenerateFlowPolicyKind.Production,
            "developer_only" => AiAgentGenerateFlowPolicyKind.DeveloperOnly,
            "offline_evaluation" => AiAgentGenerateFlowPolicyKind.OfflineEvaluation,
            "deprecated" => AiAgentGenerateFlowPolicyKind.Deprecated,
            // Unknown values must never opt into the retired runtime. The
            // options validator rejects them during host startup.
            _ => AiAgentGenerateFlowPolicyKind.Production
        };
    }

    public static bool IsKnownPolicy(string? value)
    {
        return value?.Trim().ToLowerInvariant() is
            "production" or
            "developer_only" or
            "offline_evaluation" or
            "deprecated";
    }

    private static AiAgentGenerateFlowModeDecision Reject(
        string requested,
        string code,
        string message)
    {
        return new AiAgentGenerateFlowModeDecision(
            false,
            requested,
            string.Empty,
            code,
            message);
    }
}
