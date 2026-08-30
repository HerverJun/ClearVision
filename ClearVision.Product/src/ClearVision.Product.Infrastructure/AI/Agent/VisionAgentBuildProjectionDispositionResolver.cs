using System.Text.Json;
using ClearVision.Product.Infrastructure.AI.AgentRun;

namespace ClearVision.Product.Infrastructure.AI.Agent;

public enum VisionAgentBuildProjectionDisposition
{
    Project,
    Skip,
    Unknown
}

public sealed class VisionAgentBuildProjectionDispositionResolver
{
    public const string Project = "project";
    public const string Skip = "skip";
    public const string Unknown = "unknown";

    public VisionAgentBuildProjectionDisposition Resolve(
        AgentRunReplayResult replay,
        IVisionAgentBuildProjectionJournal journal,
        IConversationalFlowService conversationService)
    {
        ArgumentNullException.ThrowIfNull(replay);
        ArgumentNullException.ThrowIfNull(journal);
        ArgumentNullException.ThrowIfNull(conversationService);

        if (VisionAgentRunKindResolver.Resolve(replay) != VisionAgentRunKind.Build)
        {
            return VisionAgentBuildProjectionDisposition.Unknown;
        }

        var terminal = replay.Events
            .OrderBy(evt => evt.Sequence)
            .LastOrDefault(IsTerminalEvent);
        if (terminal == null)
        {
            return VisionAgentBuildProjectionDisposition.Unknown;
        }

        var source = TryGetTerminalSource(terminal);
        var explicitDisposition = Parse(TryReadString(source, "projectionDisposition"));
        if (explicitDisposition != VisionAgentBuildProjectionDisposition.Unknown)
        {
            return explicitDisposition;
        }

        if (journal.TryGetLatest(terminal.RunId, terminal.Sequence, terminal.EventType) != null)
        {
            return VisionAgentBuildProjectionDisposition.Project;
        }

        if (IsHostInterrupted(source))
        {
            return VisionAgentBuildProjectionDisposition.Skip;
        }

        if (IsAssociationFailure(source))
        {
            return VisionAgentBuildProjectionDisposition.Skip;
        }

        return HasLegacyProjectionEligibility(replay, terminal, source, conversationService)
            ? VisionAgentBuildProjectionDisposition.Project
            : VisionAgentBuildProjectionDisposition.Unknown;
    }

    public static string ToWireValue(VisionAgentBuildProjectionDisposition disposition)
    {
        return disposition switch
        {
            VisionAgentBuildProjectionDisposition.Project => Project,
            VisionAgentBuildProjectionDisposition.Skip => Skip,
            _ => Unknown
        };
    }

    public static VisionAgentBuildProjectionDisposition Parse(string? value)
    {
        if (string.Equals(value, Project, StringComparison.OrdinalIgnoreCase))
        {
            return VisionAgentBuildProjectionDisposition.Project;
        }

        if (string.Equals(value, Skip, StringComparison.OrdinalIgnoreCase))
        {
            return VisionAgentBuildProjectionDisposition.Skip;
        }

        return VisionAgentBuildProjectionDisposition.Unknown;
    }

    public static bool HasCompleteProjectionBasis(JsonElement? source)
    {
        return TryReadLong(source, "associationWorkspaceRevision").HasValue &&
               !string.IsNullOrWhiteSpace(TryReadString(source, "submittedBuildFingerprint")) &&
               !string.IsNullOrWhiteSpace(TryReadString(source, "planId")) &&
               !string.IsNullOrWhiteSpace(TryReadString(source, "planHash")) &&
               !string.IsNullOrWhiteSpace(TryReadString(source, "answerSetFingerprint")) &&
               !string.IsNullOrWhiteSpace(TryReadString(source, "buildIdentity"));
    }

    private static bool HasLegacyProjectionEligibility(
        AgentRunReplayResult replay,
        AgentRunEvent terminal,
        JsonElement? source,
        IConversationalFlowService conversationService)
    {
        if (!HasCompleteProjectionBasis(source))
        {
            return false;
        }

        var startPayload = replay.Events
            .OrderBy(evt => evt.Sequence)
            .FirstOrDefault(evt => string.Equals(evt.EventType, AgentRunEventTypes.RunStarted, StringComparison.OrdinalIgnoreCase))
            ?.Payload;
        var sessionId = FirstNonBlank(
            TryReadString(source, "sessionId"),
            TryReadString(ToJsonElement(startPayload), "sessionId"));
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(replay.Summary.OwnerHash))
        {
            return false;
        }

        var workspace = conversationService.GetSession(
            replay.Summary.OwnerHash,
            sessionId)?.WorkspaceSnapshot;
        return workspace != null &&
               string.Equals(workspace.BuildRunId, terminal.RunId, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsHostInterrupted(JsonElement? source)
    {
        return string.Equals(
            TryReadString(source, "failureCode"),
            "host_instance_interrupted",
            StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAssociationFailure(JsonElement? source)
    {
        var associationCommitted = TryReadBool(source, "associationCommitted");
        if (associationCommitted == true)
        {
            return false;
        }

        if (associationCommitted == false)
        {
            return true;
        }

        var failureCode = FirstNonBlank(
            TryReadString(source, "failureCode"),
            TryReadString(source, "errorCode"));
        return IsFailureCode(failureCode, "workspace_revision_required") ||
               IsFailureCode(failureCode, "workspace_revision_conflict") ||
               IsFailureCode(failureCode, "session_persistence_failed") ||
               IsFailureCode(failureCode, "primary_store_save_failed");
    }

    private static bool IsFailureCode(string candidate, string expected)
    {
        return string.Equals(candidate, expected, StringComparison.OrdinalIgnoreCase);
    }

    private static JsonElement? TryGetTerminalSource(AgentRunEvent terminal)
    {
        var payload = ToJsonElement(terminal.Payload);
        if (payload == null)
        {
            return null;
        }

        if (string.Equals(terminal.EventType, AgentRunEventTypes.RunFailed, StringComparison.OrdinalIgnoreCase) &&
            TryGetProperty(payload.Value, "diagnostic", out var diagnostic))
        {
            return diagnostic;
        }

        return payload;
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

    private static string? TryReadString(JsonElement? source, string propertyName)
    {
        if (source == null || !TryGetProperty(source.Value, propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : property.ToString();
    }

    private static bool? TryReadBool(JsonElement? source, string propertyName)
    {
        if (source == null || !TryGetProperty(source.Value, propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String when bool.TryParse(property.GetString(), out var parsed) => parsed,
            _ => null
        };
    }

    private static long? TryReadLong(JsonElement? source, string propertyName)
    {
        if (source == null || !TryGetProperty(source.Value, propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.Number when property.TryGetInt64(out var value) => value,
            JsonValueKind.String when long.TryParse(property.GetString(), out var value) => value,
            _ => null
        };
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

    private static bool IsTerminalEvent(AgentRunEvent evt)
    {
        return string.Equals(evt.EventType, AgentRunEventTypes.RunCompleted, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(evt.EventType, AgentRunEventTypes.RunFailed, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(evt.EventType, AgentRunEventTypes.RunCancelled, StringComparison.OrdinalIgnoreCase);
    }

    private static string FirstNonBlank(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;
    }
}
