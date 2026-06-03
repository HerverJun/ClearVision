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
        var allTools = _toolRegistry.ListTools();
        var requestedToolCallingMode = AiToolCallingModes.Normalize(request.Model.ToolCallingMode);
        var nativeToolCallsEnabled = ShouldUseNativeToolCalls(requestedToolCallingMode, request.Capabilities, allTools.Count);
        var tools = requestedToolCallingMode == AiToolCallingModes.Disabled
            ? Array.Empty<VisionAgentToolDescriptor>()
            : allTools;
        var toolCallingModeLabel = ResolveToolCallingModeLabel(
            requestedToolCallingMode,
            nativeToolCallsEnabled,
            tools.Count);
        var systemPrompt = _promptBuilder.BuildSystemPrompt(
            request.ToolContext.PromptMode,
            tools,
            request.Capabilities.SupportsJsonMode,
            nativeToolCallsEnabled,
            toolCallingModeLabel);
        var nativeToolDefinitions = nativeToolCallsEnabled
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
            try
            {
                lastCompletion = nativeToolCallsEnabled
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
            }
            catch (Exception ex) when (nativeToolCallsEnabled)
            {
                request.Progress?.Invoke(new VisionAgentToolProgress(
                    "tool_failed",
                    "native_tool_call",
                    $"原生工具调用失败，已切换 JSON fallback：{ex.Message}",
                    null));
                nativeToolCallsEnabled = false;
                toolCallingModeLabel = AiToolCallingModes.ToDisplayLabel(AiToolCallingModes.JsonFallback);
                systemPrompt = _promptBuilder.BuildSystemPrompt(
                    request.ToolContext.PromptMode,
                    tools,
                    request.Capabilities.SupportsJsonMode,
                    supportsNativeToolCalls: false,
                    toolCallingMode: toolCallingModeLabel);
                lastCompletion = await _orchestrator.CompleteAsync(
                    systemPrompt,
                    messages,
                    request.Model,
                    cancellationToken);
            }

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
                    ToolRounds = round,
                    ToolCallingMode = toolCallingModeLabel
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
                    ToolRounds = round,
                    ToolCallingMode = toolCallingModeLabel
                };
            }

            var toolCalls = parsed.ToolCalls.Take(_options.MaxToolCallsPerRound).ToList();
            var roundResults = await ExecuteToolRoundAsync(
                toolCalls,
                request.ToolContext with
                {
                    MaxToolResultChars = _options.MaxToolResultChars,
                    ToolCallingMode = toolCallingModeLabel
                },
                request.Progress,
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
            ToolRounds = _options.MaxToolRounds,
            ToolCallingMode = toolCallingModeLabel
        };
    }

    private async Task<List<ToolRoundExecutionResult>> ExecuteToolRoundAsync(
        IReadOnlyList<VisionAgentToolCall> toolCalls,
        VisionAgentToolContext context,
        Action<VisionAgentToolProgress>? progress,
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
                ExecuteOneToolAsync(call, context, progress, traces, pendingActions, cancellationToken)));
            return parallel.ToList();
        }

        var results = new List<ToolRoundExecutionResult>();
        foreach (var call in toolCalls)
        {
            results.Add(await ExecuteOneToolAsync(call, context, progress, traces, pendingActions, cancellationToken));
        }

        return results;
    }

    private async Task<ToolRoundExecutionResult> ExecuteOneToolAsync(
        VisionAgentToolCall call,
        VisionAgentToolContext context,
        Action<VisionAgentToolProgress>? progress,
        List<VisionAgentToolTrace> traces,
        List<VisionAgentPendingAction> pendingActions,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var permission = _toolRegistry.TryGet(call.Name, out var tool)
            ? tool.Permission.ToString()
            : string.Empty;
        progress?.Invoke(new VisionAgentToolProgress(
            "tool_start",
            call.Name,
            $"正在调用 {call.Name}...",
            null));
        var result = await _toolRegistry.ExecuteAsync(call.Name, context, call.Arguments, cancellationToken);
        stopwatch.Stop();
        var issueCount = CountToolIssues(result.Data);
        progress?.Invoke(new VisionAgentToolProgress(
            result.Success ? "tool_end" : "tool_failed",
            call.Name,
            result.Success
                ? issueCount > 0
                    ? $"{call.Name} 返回 {issueCount} 个问题，正在修复..."
                    : $"{call.Name} 调用完成。"
                : $"{call.Name} 调用失败：{result.ErrorCode ?? "tool_failed"} {result.ErrorMessage}",
            issueCount));

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
                Permission = permission,
                ToolCallingMode = context.ToolCallingMode
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

    private static int CountToolIssues(object? data)
    {
        if (data == null)
        {
            return 0;
        }

        try
        {
            using var doc = JsonDocument.Parse(JsonSerializer.Serialize(data, JsonOptions));
            return CountArrayProperty(doc.RootElement, "errors") +
                   CountArrayProperty(doc.RootElement, "blockingIssues");
        }
        catch (JsonException)
        {
            return 0;
        }
    }

    private static int CountArrayProperty(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty(propertyName, out var value) ||
            value.ValueKind != JsonValueKind.Array)
        {
            return 0;
        }

        return value.GetArrayLength();
    }

    private static bool ShouldUseNativeToolCalls(
        string requestedMode,
        AiModelCapabilities capabilities,
        int toolCount)
    {
        if (toolCount == 0 ||
            requestedMode is AiToolCallingModes.Disabled or AiToolCallingModes.JsonFallback)
        {
            return false;
        }

        if (requestedMode == AiToolCallingModes.Native)
        {
            return true;
        }

        return capabilities.SupportsToolCall;
    }

    private static string ResolveToolCallingModeLabel(
        string requestedMode,
        bool nativeToolCallsEnabled,
        int toolCount)
    {
        if (toolCount == 0 || requestedMode == AiToolCallingModes.Disabled)
        {
            return "Disabled";
        }

        if (nativeToolCallsEnabled)
        {
            return "Native";
        }

        return "JSON fallback";
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
    public Action<VisionAgentToolProgress>? Progress { get; init; }
}

public sealed record VisionAgentToolProgress(
    string Stage,
    string ToolName,
    string Message,
    int? IssueCount);

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
    public string ToolCallingMode { get; init; } = string.Empty;
}
