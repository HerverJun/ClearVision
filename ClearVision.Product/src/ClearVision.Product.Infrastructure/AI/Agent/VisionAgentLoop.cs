using System.Diagnostics;
using System.Text.Encodings.Web;
using System.Text.Json;
using ClearVision.Product.Core.AI.Tools;
using ClearVision.Product.Infrastructure.AI.AgentRun;
using Microsoft.Extensions.Options;

namespace ClearVision.Product.Infrastructure.AI.Agent;

public sealed class VisionAgentLoop
{
    private readonly IVisionAgentToolRegistry _toolRegistry;
    private readonly VisionAgentProtocolParser _protocolParser;
    private readonly AgentPromptBuilder _promptBuilder;
    private readonly VisionAgentLoopOptions _options;
    private readonly IAgentRunEventSink? _eventSink;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public VisionAgentLoop(
        IVisionAgentToolRegistry toolRegistry,
        VisionAgentProtocolParser protocolParser,
        AgentPromptBuilder promptBuilder,
        IOptions<VisionAgentLoopOptions> options,
        IAgentRunEventSink? eventSink = null)
    {
        _toolRegistry = toolRegistry;
        _protocolParser = protocolParser;
        _promptBuilder = promptBuilder;
        _options = options.Value;
        _options.Normalize();
        _eventSink = eventSink;
    }

    public async Task<VisionAgentLoopResult> RunAsync(
        VisionAgentLoopRequest request,
        CancellationToken cancellationToken)
    {
        if (request.CompleteAsync == null)
        {
            AppendLoopEvent(
                request.EmitPublicEvents,
                request.ToolContext.AgentRunId,
                AgentRunEventTypes.ToolLoopFailed,
                "tool_loop",
                "Tool Loop completion source missing",
                "VisionAgentLoop requires a completion source before it can run.",
                AgentRunEventStatuses.Failed,
                new
                {
                    failureType = "completion_source_missing",
                    metadataOnly = true
                });
            return new VisionAgentLoopResult
            {
                Success = false,
                FailureType = "completion_source_missing",
                ErrorMessage = "VisionAgentLoop requires a scripted completion source in skeleton v0.1."
            };
        }

        var traces = new List<VisionAgentToolTrace>();
        var pendingActions = new List<VisionAgentPendingAction>();
        var systemPrompt = _promptBuilder.BuildSystemPrompt(_toolRegistry.ListTools());
        var messages = new List<VisionAgentLoopMessage>
        {
            new("system", systemPrompt),
            new("user", request.UserPrompt)
        };
        AppendLoopEvent(
            request.EmitPublicEvents,
            request.ToolContext.AgentRunId,
            AgentRunEventTypes.ToolLoopStarted,
            "tool_loop",
            "Tool Loop 实验已启动",
            "实验模式正在权限门禁内请求 LLM 选择下一步工具。",
            AgentRunEventStatuses.Running,
            new
            {
                maxToolRounds = _options.MaxToolRounds,
                maxToolCallsPerRound = _options.MaxToolCallsPerRound,
                allowedPermissions = request.ToolContext.AllowedPermissions.Select(permission => permission.ToString()).ToList(),
                metadataOnly = true
            });

        for (var round = 0; ; round++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AppendLoopEvent(
                request.EmitPublicEvents,
                request.ToolContext.AgentRunId,
                AgentRunEventTypes.ToolLoopRoundStarted,
                "tool_loop",
                $"Tool Loop 第 {round + 1} 轮",
                "正在请求 LLM 选择公开工具调用或给出 final 结果。",
                AgentRunEventStatuses.Running,
                new
                {
                    round = round + 1,
                    messageCount = messages.Count,
                    metadataOnly = true
                });
            string completion;
            try
            {
                completion = await request.CompleteAsync(messages, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                AppendLoopEvent(
                    request.EmitPublicEvents,
                    request.ToolContext.AgentRunId,
                    AgentRunEventTypes.ToolLoopFailed,
                    "tool_loop",
                    "Tool Loop completion 失败",
                    "实验 Tool Loop completion 未能产出合法公开协议，将回退稳定构建链路。",
                    AgentRunEventStatuses.Failed,
                    new
                    {
                        failureType = "completion_failed",
                        error = ex.Message,
                        metadataOnly = true
                    });
                return new VisionAgentLoopResult
                {
                    Success = false,
                    FailureType = "completion_failed",
                    ErrorMessage = ex.Message,
                    ToolTrace = traces,
                    PendingActions = pendingActions,
                    ToolRounds = round,
                    SystemPrompt = systemPrompt
                };
            }

            var parsed = _protocolParser.Parse(completion);
            if (!parsed.IsToolCall)
            {
                AppendLoopEvent(
                    request.EmitPublicEvents,
                    request.ToolContext.AgentRunId,
                    AgentRunEventTypes.ToolLoopFinalized,
                    "tool_loop",
                    "Tool Loop 已生成 final 结果",
                    "LLM 已停止自主工具调用；后续结果将继续走公开校验和稳定产物补齐。",
                    AgentRunEventStatuses.Completed,
                    new
                    {
                        round,
                        toolCallCount = traces.Count,
                        finalKind = DetectFinalKind(parsed.FinalContent),
                        metadataOnly = true
                    });
                return new VisionAgentLoopResult
                {
                    Success = true,
                    FinalContent = parsed.FinalContent,
                    ToolTrace = traces,
                    PendingActions = pendingActions,
                    ToolRounds = round,
                    SystemPrompt = systemPrompt
                };
            }

            if (round >= _options.MaxToolRounds)
            {
                AppendLoopEvent(
                    request.EmitPublicEvents,
                    request.ToolContext.AgentRunId,
                    AgentRunEventTypes.ToolLoopFailed,
                    "tool_loop",
                    "Tool Loop 超过最大轮次",
                    $"实验 Tool Loop 超过 MaxToolRounds={_options.MaxToolRounds}，将回退稳定构建链路。",
                    AgentRunEventStatuses.Failed,
                    new
                    {
                        failureType = "failed_with_tool_limit",
                        maxToolRounds = _options.MaxToolRounds,
                        toolCallCount = traces.Count,
                        metadataOnly = true
                    });
                return new VisionAgentLoopResult
                {
                    Success = false,
                    FailureType = "failed_with_tool_limit",
                    ErrorMessage = $"Vision agent exceeded MaxToolRounds={_options.MaxToolRounds}.",
                    ToolTrace = traces,
                    PendingActions = pendingActions,
                    ToolRounds = round,
                    SystemPrompt = systemPrompt
                };
            }

            var toolCalls = parsed.ToolCalls
                .Take(_options.MaxToolCallsPerRound)
                .ToList();
            var roundResults = await ExecuteToolRoundAsync(
                toolCalls,
                request.ToolContext with { MaxToolResultChars = _options.MaxToolResultChars },
                traces,
                pendingActions,
                request.EmitPublicEvents,
                cancellationToken);

            messages.Add(new VisionAgentLoopMessage("assistant", completion));
            messages.Add(new VisionAgentLoopMessage("user", JsonSerializer.Serialize(new
            {
                kind = "tool_result",
                round = round + 1,
                toolResults = roundResults
            }, JsonOptions)));
            AppendLoopEvent(
                request.EmitPublicEvents,
                request.ToolContext.AgentRunId,
                AgentRunEventTypes.ToolResultAppended,
                "tool_loop",
                "Tool result 已追加",
                "公开工具结果摘要已回填给下一轮 LLM 上下文。",
                AgentRunEventStatuses.Completed,
                new
                {
                    round = round + 1,
                    toolResultCount = roundResults.Count,
                    metadataOnly = true
                });
        }
    }

    private async Task<IReadOnlyList<object>> ExecuteToolRoundAsync(
        IReadOnlyList<VisionAgentToolCall> toolCalls,
        VisionAgentToolContext context,
        List<VisionAgentToolTrace> traces,
        List<VisionAgentPendingAction> pendingActions,
        bool emitPublicEvents,
        CancellationToken cancellationToken)
    {
        var allReadOnly = toolCalls.All(call =>
            _toolRegistry.TryGet(call.Name, out var tool) &&
            tool.Permission == VisionAgentToolPermission.ReadOnly);
        if (allReadOnly)
        {
            var parallelResults = await Task.WhenAll(toolCalls.Select(call =>
                ExecuteOneToolAsync(call, context, traces, pendingActions, emitPublicEvents, cancellationToken)));
            return parallelResults;
        }

        var results = new List<object>();
        foreach (var call in toolCalls)
        {
            results.Add(await ExecuteOneToolAsync(call, context, traces, pendingActions, emitPublicEvents, cancellationToken));
        }

        return results;
    }

    private async Task<object> ExecuteOneToolAsync(
        VisionAgentToolCall call,
        VisionAgentToolContext context,
        List<VisionAgentToolTrace> traces,
        List<VisionAgentPendingAction> pendingActions,
        bool emitPublicEvents,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var knownTool = _toolRegistry.TryGet(call.Name, out var tool);
        var permission = knownTool ? tool.Permission.ToString() : string.Empty;
        VisionAgentToolResult result;
        AppendLoopEvent(
            emitPublicEvents,
            context.AgentRunId,
            AgentRunEventTypes.ToolCallRequested,
            "tool_loop",
            $"Tool call requested: {call.Name}",
            "LLM requested a metadata-only Vision Agent tool.",
            AgentRunEventStatuses.Running,
            new
            {
                toolCallId = call.Id,
                toolName = call.Name,
                permission,
                metadataOnly = true
            });
        _eventSink?.ToolStarted(context.AgentRunId, ResolveToolStage(call.Name), call.Name, new
        {
            toolName = call.Name,
            permission,
            arguments = context.DebugTrace
                ? CloneJsonCompatible(call.Arguments)
                : SummarizeArguments(call.Arguments),
            metadataOnly = true
        });

        if (!knownTool)
        {
            result = VisionAgentToolResult.Fail(
                "unknown_tool",
                $"Vision agent tool '{call.Name}' is not registered.");
        }
        else if (tool.Permission == VisionAgentToolPermission.ConfigWrite)
        {
            result = VisionAgentToolResult.Fail(
                "tool_permission_denied",
                $"Tool '{tool.Name}' requires ConfigWrite, which is always denied in Vision Agent skeleton v0.1.");
        }
        else if (tool.Permission == VisionAgentToolPermission.DeploymentPrepare &&
                 !CanRunDeploymentPrepareTool(tool, context))
        {
            result = VisionAgentToolResult.Fail(
                "tool_permission_denied",
                $"Tool '{tool.Name}' requires DeploymentPrepare and is not allowed in this session.");
        }
        else if (tool.Permission == VisionAgentToolPermission.RuntimePreview &&
                 !RuntimePreviewPermissionGate.CanRun(context))
        {
            result = RuntimePreviewPermissionGate.DeniedToolResult(tool.Name, context);
        }
        else if (!context.AllowedPermissions.Contains(tool.Permission))
        {
            result = VisionAgentToolResult.Fail(
                "tool_permission_denied",
                $"Tool '{tool.Name}' requires permission '{tool.Permission}', which is not allowed in this session.");
        }
        else
        {
            try
            {
                using var toolTimeout = CreateToolTimeout(cancellationToken);
                result = await _toolRegistry.ExecuteAsync(
                    call.Name,
                    context,
                    call.Arguments,
                    toolTimeout.Token);
            }
            catch (OperationCanceledException)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }

                result = VisionAgentToolResult.Fail(
                    "tool_timeout",
                    $"Tool '{call.Name}' exceeded timeout {_options.ToolTimeoutMs} ms.");
            }
            catch (Exception ex)
            {
                result = VisionAgentToolResult.Fail("tool_exception", ex.Message);
            }
        }

        stopwatch.Stop();
        var resultSummary = SummarizeResult(result);
        lock (traces)
        {
            traces.Add(new VisionAgentToolTrace
            {
                ToolName = call.Name,
                Arguments = context.DebugTrace
                    ? CloneJsonCompatible(call.Arguments)
                    : SummarizeArguments(call.Arguments),
                Success = result.Success,
                ResultSummary = resultSummary,
                ErrorCode = result.ErrorCode,
                ErrorMessage = result.ErrorMessage,
                DurationMs = stopwatch.ElapsedMilliseconds,
                Permission = permission,
                PermissionDecision = knownTool
                    ? RuntimePreviewPermissionGate.PermissionDecision(tool.Permission, context, result)
                    : null,
                AdapterName = ExtractAdapterName(result.Data)
            });
        }

        var eventPayload = new
        {
            toolName = call.Name,
            status = result.Success
                ? AgentRunEventStatuses.Completed
                : AgentRunEventStatuses.Failed,
            durationMs = stopwatch.ElapsedMilliseconds,
            permission,
            summary = resultSummary,
            errorCode = result.ErrorCode,
            errorMessage = result.ErrorMessage,
            reportId = ExtractReportId(result.Data),
            blockedReasons = ExtractBlockedReasons(result.Data),
            firstFixRecommendation = result.Success
                ? null
                : BuildFirstFixRecommendation(result.ErrorCode, result.ErrorMessage),
            metadataOnly = true
        };
        if (result.Success)
        {
            AppendLoopEvent(
                emitPublicEvents,
                context.AgentRunId,
                AgentRunEventTypes.ToolCallLoopCompleted,
                "tool_loop",
                $"Tool call completed: {call.Name}",
                "LLM requested tool completed with public metadata.",
                AgentRunEventStatuses.Completed,
                eventPayload);
            _eventSink?.ToolCompleted(
                context.AgentRunId,
                ResolveToolStage(call.Name),
                call.Name,
                stopwatch.ElapsedMilliseconds,
                eventPayload);
        }
        else
        {
            AppendLoopEvent(
                emitPublicEvents,
                context.AgentRunId,
                IsDeniedResult(result) ? AgentRunEventTypes.ToolCallDenied : AgentRunEventTypes.ToolLoopFailed,
                "tool_loop",
                IsDeniedResult(result)
                    ? $"Tool call denied: {call.Name}"
                    : $"Tool call failed: {call.Name}",
                IsDeniedResult(result)
                    ? "LLM requested tool was denied by the experimental permission gate."
                    : "LLM requested tool failed and the run will use public fallback handling.",
                IsDeniedResult(result) ? AgentRunEventStatuses.Blocked : AgentRunEventStatuses.Failed,
                eventPayload);
            _eventSink?.ToolFailed(
                context.AgentRunId,
                ResolveToolStage(call.Name),
                call.Name,
                stopwatch.ElapsedMilliseconds,
                $"Tool '{call.Name}' failed: {result.ErrorCode ?? "tool_failed"}.",
                eventPayload);
        }

        if (result.PendingActions.Count > 0)
        {
            lock (pendingActions)
            {
                pendingActions.AddRange(result.PendingActions);
            }
        }

        return new
        {
            id = call.Id,
            name = call.Name,
            success = result.Success,
            errorCode = result.ErrorCode,
            errorMessage = result.ErrorMessage,
            data = TruncateForModel(result.Data, context.MaxToolResultChars),
            pendingActions = result.PendingActions
        };
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
                    JsonValueKind.String => SummarizeString(property.Value.GetString()),
                    JsonValueKind.Number => property.Value.GetRawText(),
                    JsonValueKind.True => "true",
                    JsonValueKind.False => "false",
                    _ => string.Empty
                },
                StringComparer.OrdinalIgnoreCase);
    }

    private CancellationTokenSource CreateToolTimeout(CancellationToken cancellationToken)
    {
        var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linked.CancelAfter(TimeSpan.FromMilliseconds(_options.ToolTimeoutMs));
        return linked;
    }

    private void AppendLoopEvent(
        bool emitPublicEvents,
        string? runId,
        string eventType,
        string stage,
        string title,
        string summary,
        string status,
        object? payload)
    {
        if (!emitPublicEvents)
        {
            return;
        }

        _eventSink?.Append(runId, new AgentRunEventDraft
        {
            EventType = eventType,
            Stage = stage,
            Title = title,
            Summary = summary,
            Status = status,
            Payload = payload,
            MetadataOnly = true
        });
    }

    private static bool IsDeniedResult(VisionAgentToolResult result)
    {
        return string.Equals(result.ErrorCode, "unknown_tool", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(result.ErrorCode, "tool_permission_denied", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(result.ErrorCode, RuntimePreviewPermissionGate.ConsentRequiredErrorCode, StringComparison.OrdinalIgnoreCase);
    }

    private static string DetectFinalKind(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return "empty_final";
        }

        try
        {
            using var doc = JsonDocument.Parse(content);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return "final_answer";
            }

            if (doc.RootElement.TryGetProperty("workflowDraft", out var draft) &&
                draft.ValueKind == JsonValueKind.Object)
            {
                return "workflow_draft";
            }

            if (doc.RootElement.TryGetProperty("draftEdits", out var edits) &&
                edits.ValueKind == JsonValueKind.Array)
            {
                return "draft_edits";
            }

            return "final_answer";
        }
        catch (JsonException)
        {
            return "final_answer";
        }
    }

    private static string SummarizeString(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value.Length <= 80
            ? value
            : value[..80] + "...";
    }

    private static object? SummarizeResult(VisionAgentToolResult result)
    {
        if (!result.Success)
        {
            return new { result.ErrorCode, result.ErrorMessage };
        }

        if (result.Data == null)
        {
            return null;
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

    private static string ResolveToolStage(string toolName)
    {
        return toolName.ToLowerInvariant() switch
        {
            "list_operator_catalog" or "get_operator_schema" or "get_operator_knowledge" or "match_flow_template" or "inspect_current_flow" => "planner",
            "get_flow_template_skeleton" => "workflow_draft",
            "validate_flow" => "readiness",
            "dryrun_flow" => "manifest_dry_run",
            "runtime_package_precheck" => "package_readiness",
            _ => "tool_call"
        };
    }

    private static string? ExtractReportId(object? data)
    {
        return TryFindString(data, "reportId") ??
               TryFindString(data, "manifestId") ??
               TryFindString(data, "reviewId") ??
               TryFindString(data, "sessionId");
    }

    private static IReadOnlyList<string> ExtractBlockedReasons(object? data)
    {
        return TryFindStringArray(data, "blockedReasons")
            .Concat(TryFindStringArray(data, "blockingReasons"))
            .Concat(TryFindStringArray(data, "deniedReasons"))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(12)
            .ToList();
    }

    private static string BuildFirstFixRecommendation(string? errorCode, string? errorMessage)
    {
        if (string.Equals(errorCode, "tool_permission_denied", StringComparison.OrdinalIgnoreCase))
        {
            return "Remove the blocked tool intent or retry in a mode that only uses metadata-only review tools.";
        }

        if (!string.IsNullOrWhiteSpace(errorMessage) &&
            errorMessage.Contains("resource", StringComparison.OrdinalIgnoreCase))
        {
            return "Provide the missing metadata resource reference, then rerun the Vision Agent request.";
        }

        return "Inspect the tool failure summary, adjust the request or required metadata, and retry.";
    }

    private static string? TryFindString(object? data, string propertyName)
    {
        if (data == null)
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(JsonSerializer.Serialize(data, JsonOptions));
            return TryFindString(doc.RootElement, propertyName);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? TryFindString(JsonElement element, string propertyName)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase) &&
                    property.Value.ValueKind == JsonValueKind.String)
                {
                    return property.Value.GetString();
                }

                var nested = TryFindString(property.Value, propertyName);
                if (!string.IsNullOrWhiteSpace(nested))
                {
                    return nested;
                }
            }
        }

        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                var nested = TryFindString(item, propertyName);
                if (!string.IsNullOrWhiteSpace(nested))
                {
                    return nested;
                }
            }
        }

        return null;
    }

    private static IReadOnlyList<string> TryFindStringArray(object? data, string propertyName)
    {
        if (data == null)
        {
            return [];
        }

        try
        {
            using var doc = JsonDocument.Parse(JsonSerializer.Serialize(data, JsonOptions));
            return TryFindStringArray(doc.RootElement, propertyName);
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static IReadOnlyList<string> TryFindStringArray(JsonElement element, string propertyName)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase) &&
                    property.Value.ValueKind == JsonValueKind.Array)
                {
                    return property.Value
                        .EnumerateArray()
                        .Where(item => item.ValueKind == JsonValueKind.String)
                        .Select(item => item.GetString())
                        .Where(item => !string.IsNullOrWhiteSpace(item))
                        .Cast<string>()
                        .ToList();
                }

                var nested = TryFindStringArray(property.Value, propertyName);
                if (nested.Count > 0)
                {
                    return nested;
                }
            }
        }

        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                var nested = TryFindStringArray(item, propertyName);
                if (nested.Count > 0)
                {
                    return nested;
                }
            }
        }

        return [];
    }

    private static string? ExtractAdapterName(object? data)
    {
        if (data is RuntimePreviewResult preview)
        {
            return string.IsNullOrWhiteSpace(preview.AdapterName) ? null : preview.AdapterName;
        }

        if (data == null)
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(JsonSerializer.Serialize(data, JsonOptions));
            if (TryReadString(doc.RootElement, "adapterName", out var adapterName))
            {
                return adapterName;
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
    }

    private static bool TryReadString(JsonElement element, string propertyName, out string? value)
    {
        value = null;
        if (element.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase) &&
                property.Value.ValueKind == JsonValueKind.String)
            {
                value = property.Value.GetString();
                return !string.IsNullOrWhiteSpace(value);
            }
        }

        return false;
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

    private static object? CloneJsonCompatible(JsonElement value)
    {
        return JsonSerializer.Deserialize<object>(value.GetRawText(), JsonOptions);
    }

    private static bool CanRunDeploymentPrepareTool(
        IVisionAgentTool tool,
        VisionAgentToolContext context)
    {
        return string.Equals(tool.Name, "runtime_package_precheck", StringComparison.OrdinalIgnoreCase) &&
               context.AllowedPermissions.Contains(VisionAgentToolPermission.DeploymentPrepare);
    }
}

public sealed record VisionAgentLoopRequest
{
    public string UserPrompt { get; init; } = string.Empty;
    public VisionAgentToolContext ToolContext { get; init; } = new();
    public Func<IReadOnlyList<VisionAgentLoopMessage>, CancellationToken, Task<string>>? CompleteAsync { get; init; }
    public bool EmitPublicEvents { get; init; }
}

public sealed record VisionAgentLoopMessage(string Role, string Content);

public sealed record VisionAgentLoopResult
{
    public bool Success { get; init; }
    public string FinalContent { get; init; } = string.Empty;
    public string? FailureType { get; init; }
    public string? ErrorMessage { get; init; }
    public List<VisionAgentToolTrace> ToolTrace { get; init; } = new();
    public List<VisionAgentPendingAction> PendingActions { get; init; } = new();
    public int ToolRounds { get; init; }
    public string SystemPrompt { get; init; } = string.Empty;
}
