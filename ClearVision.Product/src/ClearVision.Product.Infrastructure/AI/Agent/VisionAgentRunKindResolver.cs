using System.Text.Json;
using ClearVision.Product.Infrastructure.AI.AgentRun;

namespace ClearVision.Product.Infrastructure.AI.Agent;

public enum VisionAgentRunKind
{
    Unknown,
    Plan,
    Build
}

public static class VisionAgentRunKindResolver
{
    public const string Plan = "plan";
    public const string Build = "build";
    public const string Unknown = "unknown";

    public static VisionAgentRunKind Resolve(AgentRunReplayResult replay)
    {
        ArgumentNullException.ThrowIfNull(replay);

        var terminalIntentKind = Parse(replay.Summary.TerminalIntent?.RunType);
        if (terminalIntentKind != VisionAgentRunKind.Unknown)
        {
            return terminalIntentKind;
        }

        var explicitKind = replay.Events
            .OrderBy(evt => evt.Sequence)
            .Select(evt => Parse(TryReadString(evt.Payload, "runKind")))
            .FirstOrDefault(kind => kind != VisionAgentRunKind.Unknown);
        if (explicitKind != VisionAgentRunKind.Unknown)
        {
            return explicitKind;
        }

        if (HasPlanEvidence(replay))
        {
            return VisionAgentRunKind.Plan;
        }

        if (HasBuildFromPlanEvidence(replay))
        {
            return VisionAgentRunKind.Build;
        }

        if (HasLegacyPlanMode(replay))
        {
            return VisionAgentRunKind.Plan;
        }

        return VisionAgentRunKind.Unknown;
    }

    public static string ToWireValue(VisionAgentRunKind kind)
    {
        return kind switch
        {
            VisionAgentRunKind.Plan => Plan,
            VisionAgentRunKind.Build => Build,
            _ => Unknown
        };
    }

    public static VisionAgentRunKind Parse(string? value)
    {
        if (string.Equals(value, Plan, StringComparison.OrdinalIgnoreCase))
        {
            return VisionAgentRunKind.Plan;
        }

        if (string.Equals(value, Build, StringComparison.OrdinalIgnoreCase))
        {
            return VisionAgentRunKind.Build;
        }

        return VisionAgentRunKind.Unknown;
    }

    private static bool HasPlanEvidence(AgentRunReplayResult replay)
    {
        return replay.Events.Any(evt =>
            string.Equals(evt.EventType, AgentRunEventTypes.PlanCreated, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(evt.EventType, AgentRunEventTypes.PlanStarted, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(evt.EventType, AgentRunEventTypes.PlanCompleted, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(evt.EventType, AgentRunEventTypes.PlanFailed, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(evt.EventType, AgentRunEventTypes.PlanCancelled, StringComparison.OrdinalIgnoreCase));
    }

    private static bool HasBuildFromPlanEvidence(AgentRunReplayResult replay)
    {
        if (HasPlanEvidence(replay))
        {
            return false;
        }

        return replay.Events.Any(evt =>
            HasProperty(evt.Payload, "buildFromPlan") ||
            HasProperty(evt.Payload, "buildInputSummary") ||
            HasProperty(evt.Payload, "buildReadiness") ||
            HasProperty(evt.Payload, "buildResult"));
    }

    private static bool HasLegacyPlanMode(AgentRunReplayResult replay)
    {
        return replay.Events
            .Where(evt => string.Equals(evt.EventType, AgentRunEventTypes.RunStarted, StringComparison.OrdinalIgnoreCase))
            .Any(evt =>
                string.Equals(TryReadString(evt.Payload, "mode"), Plan, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(TryReadString(evt.Payload, "generationMode"), Plan, StringComparison.OrdinalIgnoreCase));
    }

    private static bool HasProperty(object? payload, string propertyName)
    {
        var element = ToJsonElement(payload);
        return element.HasValue && TryGetProperty(element.Value, propertyName, out _);
    }

    private static string? TryReadString(object? payload, string propertyName)
    {
        var element = ToJsonElement(payload);
        if (!element.HasValue || !TryGetProperty(element.Value, propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : property.ToString();
    }

    private static JsonElement? ToJsonElement(object? payload)
    {
        if (payload == null)
        {
            return null;
        }

        if (payload is JsonElement element)
        {
            return element;
        }

        try
        {
            return JsonSerializer.Deserialize<JsonElement>(
                JsonSerializer.Serialize(payload, AgentRunEventJson.Options),
                AgentRunEventJson.Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool TryGetProperty(JsonElement source, string propertyName, out JsonElement property)
    {
        if (source.ValueKind == JsonValueKind.Object)
        {
            foreach (var item in source.EnumerateObject())
            {
                if (string.Equals(item.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    property = item.Value;
                    return true;
                }
            }
        }

        property = default;
        return false;
    }
}
