using System.Diagnostics;
using System.Text.Encodings.Web;
using System.Text.Json;
using ClearVision.Product.Contracts.Messages;
using ClearVision.Product.Core.AI.Tools;
using ClearVision.Product.Infrastructure.AI.Runtime;
using Microsoft.Extensions.Options;

namespace ClearVision.Product.Infrastructure.AI.Agent;

public sealed class VisionAgentLoop
{
    private readonly AiGenerationOrchestrator _orchestrator;
    private readonly AgentPromptBuilder _promptBuilder;
    private readonly IVisionAgentToolRegistry _toolRegistry;
    private readonly VisionAgentProtocolParser _protocolParser;
    private readonly VisionAgentLoopOptions _options;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public VisionAgentLoop(
        AiGenerationOrchestrator orchestrator,
        AgentPromptBuilder promptBuilder,
        IVisionAgentToolRegistry toolRegistry,
        VisionAgentProtocolParser protocolParser,
        IOptions<VisionAgentLoopOptions> options)
    {
        _orchestrator = orchestrator;
        _promptBuilder = promptBuilder;
        _toolRegistry = toolRegistry;
        _protocolParser = protocolParser;
        _options = options.Value;
        _options.Normalize();
    }

    public async Task<VisionAgentLoopResult> RunAsync(
        VisionAgentLoopRequest request,
        CancellationToken cancellationToken)
    {
        var tools = _toolRegistry.ListTools();
        var systemPrompt = _promptBuilder.BuildSystemPrompt(
            request.ToolContext.PromptMode,
            tools,
            request.Capabilities.SupportsJsonMode,
            request.Capabilities.SupportsToolCall);
        var useNativeToolCalls = request.Capabilities.SupportsToolCall && tools.Count > 0;
        var nativeToolDefinitions = useNativeToolCalls
            ? BuildNativeToolDefinitions(tools)
            : Array.Empty<AiNativeToolDefinition>();
        var messages = new List<ChatMessage>
        {
            new("user", request.UserPrompt)
        };
        var traces = new List<VisionAgentToolTrace>();
        var pendingActions = new List<VisionAgentPendingAction>();
        AiCompletionResult? lastCompletion = null;

        for (var round = 0; round <= _options.MaxToolRounds; round++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lastCompletion = useNativeToolCalls
                ? await _orchestrator.CompleteWithToolsAsync(
                    systemPrompt,
                    messages,
                    nativeToolDefinitions,
                    request.Model,
                    cancellationToken)
                : await _orchestrator.CompleteAsync(
                    systemPrompt,
                    messages,
                    request.Model,
                    cancellationToken);

            var usedNativeToolCallsThisRound = lastCompletion.ToolCalls.Count > 0;
            var parsed = usedNativeToolCallsThisRound
                ? VisionAgentProtocolMessage.ToolCall(ToVisionToolCalls(lastCompletion.ToolCalls))
                : _protocolParser.Parse(lastCompletion.Content);
            if (!parsed.IsToolCall)
            {
                return new VisionAgentLoopResult
                {
                    Success = true,
                    FinalContent = parsed.FinalContent,
                    Reasoning = lastCompletion.Reasoning,
                    ToolTrace = traces,
                    PendingActions = pendingActions,
                    SystemPrompt = systemPrompt,
                    UserPrompt = request.UserPrompt,
                    TokenUsage = lastCompletion.TokenUsage,
                    ToolRounds = round
                };
            }

            if (round >= _options.MaxToolRounds)
            {
                return new VisionAgentLoopResult
                {
                    Success = false,
                    FinalContent = string.Empty,
                    FailureType = "failed_with_tool_limit",
                    ErrorMessage = $"Vision agent exceeded MaxToolRounds={_options.MaxToolRounds}.",
                    Reasoning = lastCompletion.Reasoning,
                    ToolTrace = traces,
                    PendingActions = pendingActions,
                    SystemPrompt = systemPrompt,
                    UserPrompt = request.UserPrompt,
                    TokenUsage = lastCompletion.TokenUsage,
                    ToolRounds = round
                };
            }

            var toolCalls = parsed.ToolCalls.Take(_options.MaxToolCallsPerRound).ToList();
            var roundResults = await ExecuteToolRoundAsync(
                toolCalls,
                request.ToolContext with { MaxToolResultChars = _options.MaxToolResultChars },
                traces,
                pendingActions,
                cancellationToken);

            if (usedNativeToolCallsThisRound)
            {
                messages.Add(ChatMessage.AssistantToolCalls(lastCompletion.Content, lastCompletion.ToolCalls));
                messages.Add(ChatMessage.ToolResultsMessage(roundResults.Select(result => new AiNativeToolResult
                {
                    ToolCallId = result.Id,
                    Content = JsonSerializer.Serialize(result.ModelPayload, JsonOptions),
                    IsError = result.IsError
                }).ToList()));
                continue;
            }

            messages.Add(new ChatMessage("assistant", lastCompletion.Content));
            messages.Add(new ChatMessage("user", JsonSerializer.Serialize(new
            {
                kind = "tool_result",
                round = round + 1,
                toolResults = roundResults.Select(result => result.ModelPayload).ToList()
            }, JsonOptions)));
        }

        return new VisionAgentLoopResult
        {
            Success = false,
            FinalContent = string.Empty,
            FailureType = "failed_with_tool_limit",
            ErrorMessage = $"Vision agent exceeded MaxToolRounds={_options.MaxToolRounds}.",
            Reasoning = lastCompletion?.Reasoning,
            ToolTrace = traces,
            PendingActions = pendingActions,
            SystemPrompt = systemPrompt,
            UserPrompt = request.UserPrompt,
            TokenUsage = lastCompletion?.TokenUsage,
            ToolRounds = _options.MaxToolRounds
        };
    }

    private async Task<List<ToolRoundExecutionResult>> ExecuteToolRoundAsync(
        IReadOnlyList<VisionAgentToolCall> toolCalls,
        VisionAgentToolContext context,
        List<VisionAgentToolTrace> traces,
        List<VisionAgentPendingAction> pendingActions,
        CancellationToken cancellationToken)
    {
        var allReadOnly = toolCalls.All(call =>
            _toolRegistry.TryGet(call.Name, out var tool) &&
            tool.Permission == VisionAgentToolPermission.ReadOnly);
        if (allReadOnly)
        {
            var parallel = await Task.WhenAll(toolCalls.Select(call =>
                ExecuteOneToolAsync(call, context, traces, pendingActions, cancellationToken)));
            return parallel.ToList();
        }

        var results = new List<ToolRoundExecutionResult>();
        foreach (var call in toolCalls)
        {
            results.Add(await ExecuteOneToolAsync(call, context, traces, pendingActions, cancellationToken));
        }

        return results;
    }

    private async Task<ToolRoundExecutionResult> ExecuteOneToolAsync(
        VisionAgentToolCall call,
        VisionAgentToolContext context,
        List<VisionAgentToolTrace> traces,
        List<VisionAgentPendingAction> pendingActions,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var permission = _toolRegistry.TryGet(call.Name, out var tool)
            ? tool.Permission.ToString()
            : string.Empty;
        var result = await _toolRegistry.ExecuteAsync(call.Name, context, call.Arguments, cancellationToken);
        stopwatch.Stop();

        lock (traces)
        {
            traces.Add(new VisionAgentToolTrace
            {
                ToolName = call.Name,
                Arguments = context.DebugPrompt ? JsonSerializer.Deserialize<object>(call.Arguments.GetRawText()) : SummarizeArguments(call.Arguments),
                Success = result.Success,
                ResultSummary = SummarizeResult(result),
                ErrorMessage = result.ErrorMessage,
                DurationMs = stopwatch.ElapsedMilliseconds,
                Permission = permission
            });
        }

        if (result.PendingActions.Count > 0)
        {
            lock (pendingActions)
            {
                pendingActions.AddRange(result.PendingActions);
            }
        }

        var payload = new
        {
            id = call.Id,
            name = call.Name,
            success = result.Success,
            errorCode = result.ErrorCode,
            errorMessage = result.ErrorMessage,
            data = TruncateForModel(result.Data, context.MaxToolResultChars),
            requiresUserConfirmation = result.RequiresUserConfirmation,
            pendingActions = result.PendingActions
        };
        return new ToolRoundExecutionResult(call.Id, payload, !result.Success);
    }

    private static object? SummarizeArguments(JsonElement arguments)
    {
        if (arguments.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return arguments.EnumerateObject()
            .ToDictionary(
                property => property.Name,
                property => property.Value.ValueKind switch
                {
                    JsonValueKind.Object => "{...}",
                    JsonValueKind.Array => $"[{property.Value.GetArrayLength()}]",
                    JsonValueKind.String => property.Value.GetString(),
                    JsonValueKind.Number => property.Value.GetRawText(),
                    JsonValueKind.True => "true",
                    JsonValueKind.False => "false",
                    _ => string.Empty
                },
                StringComparer.OrdinalIgnoreCase);
    }

    private static object? SummarizeResult(VisionAgentToolResult result)
    {
        if (!result.Success)
        {
            return new { result.ErrorCode, result.ErrorMessage };
        }

        var json = JsonSerializer.Serialize(result.Data, JsonOptions);
        if (json.Length <= 600)
        {
            return result.Data;
        }

        return new
        {
            summary = json[..600] + "...",
            charLength = json.Length,
            pendingActionCount = result.PendingActions.Count
        };
    }

    private static object? TruncateForModel(object? data, int maxChars)
    {
        if (data == null)
        {
            return null;
        }

        var json = JsonSerializer.Serialize(data, JsonOptions);
        if (json.Length <= maxChars)
        {
            return data;
        }

        return new
        {
            truncated = true,
            charLength = json.Length,
            json = json[..maxChars]
        };
    }

    private static AiNativeToolDefinition[] BuildNativeToolDefinitions(
        IReadOnlyList<VisionAgentToolDescriptor> tools)
    {
        return tools.Select(tool => new AiNativeToolDefinition
        {
            Name = tool.Name,
            Description = tool.Description,
            ParametersSchema = tool.ParametersSchema.Clone()
        }).ToArray();
    }

    private static IReadOnlyList<VisionAgentToolCall> ToVisionToolCalls(
        IReadOnlyList<AiNativeToolCall> calls)
    {
        return calls.Select(call => new VisionAgentToolCall
        {
            Id = call.Id,
            Name = call.Name,
            Arguments = call.Arguments.Clone()
        }).ToList();
    }

    private sealed record ToolRoundExecutionResult(string Id, object ModelPayload, bool IsError);
}

public sealed record VisionAgentLoopRequest
{
    public string UserPrompt { get; init; } = string.Empty;
    public AiModelConfig Model { get; init; } = new();
    public AiModelCapabilities Capabilities { get; init; } = new();
    public VisionAgentToolContext ToolContext { get; init; } = new();
}

public sealed record VisionAgentLoopResult
{
    public bool Success { get; init; }
    public string FinalContent { get; init; } = string.Empty;
    public string? Reasoning { get; init; }
    public string? FailureType { get; init; }
    public string? ErrorMessage { get; init; }
    public List<VisionAgentToolTrace> ToolTrace { get; init; } = new();
    public List<VisionAgentPendingAction> PendingActions { get; init; } = new();
    public string SystemPrompt { get; init; } = string.Empty;
    public string UserPrompt { get; init; } = string.Empty;
    public AiTokenUsage? TokenUsage { get; init; }
    public int ToolRounds { get; init; }
}
