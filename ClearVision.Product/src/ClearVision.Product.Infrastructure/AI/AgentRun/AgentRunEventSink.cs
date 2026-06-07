namespace ClearVision.Product.Infrastructure.AI.AgentRun;

public interface IAgentRunEventSink
{
    void Append(string? runId, AgentRunEventDraft draft);
    void StageStarted(string? runId, string stage, string title, string summary, object? payload = null);
    void StageCompleted(string? runId, string stage, string title, string summary, object? payload = null);
    void ToolStarted(string? runId, string stage, string toolName, object? payload = null);
    void ToolCompleted(string? runId, string stage, string toolName, long durationMs, object? payload = null);
    void ToolFailed(string? runId, string stage, string toolName, long durationMs, string summary, object? payload = null);
}

public sealed class AgentRunEventSink : IAgentRunEventSink
{
    private readonly IAgentRunEventStreamService _streamService;

    public AgentRunEventSink(IAgentRunEventStreamService streamService)
    {
        _streamService = streamService;
    }

    public void Append(string? runId, AgentRunEventDraft draft)
    {
        _streamService.Append(runId, draft);
    }

    public void StageStarted(string? runId, string stage, string title, string summary, object? payload = null)
    {
        Append(runId, new AgentRunEventDraft
        {
            EventType = AgentRunEventTypes.StageStarted,
            Stage = stage,
            Title = title,
            Summary = summary,
            Status = AgentRunEventStatuses.Running,
            Payload = payload
        });
    }

    public void StageCompleted(string? runId, string stage, string title, string summary, object? payload = null)
    {
        Append(runId, new AgentRunEventDraft
        {
            EventType = AgentRunEventTypes.StageCompleted,
            Stage = stage,
            Title = title,
            Summary = summary,
            Status = AgentRunEventStatuses.Completed,
            Payload = payload
        });
    }

    public void ToolStarted(string? runId, string stage, string toolName, object? payload = null)
    {
        Append(runId, new AgentRunEventDraft
        {
            EventType = AgentRunEventTypes.ToolCallStarted,
            Stage = stage,
            Title = $"Tool started: {toolName}",
            Summary = $"Vision Agent started metadata-only tool '{toolName}'.",
            Status = AgentRunEventStatuses.Running,
            Payload = payload
        });
    }

    public void ToolCompleted(string? runId, string stage, string toolName, long durationMs, object? payload = null)
    {
        Append(runId, new AgentRunEventDraft
        {
            EventType = AgentRunEventTypes.ToolCallCompleted,
            Stage = stage,
            Title = $"Tool completed: {toolName}",
            Summary = $"Tool '{toolName}' completed in {durationMs} ms.",
            Status = AgentRunEventStatuses.Completed,
            Payload = payload
        });
    }

    public void ToolFailed(string? runId, string stage, string toolName, long durationMs, string summary, object? payload = null)
    {
        Append(runId, new AgentRunEventDraft
        {
            EventType = AgentRunEventTypes.ToolCallFailed,
            Stage = stage,
            Title = $"Tool failed: {toolName}",
            Summary = summary,
            Status = AgentRunEventStatuses.Failed,
            Payload = payload
        });
    }
}
