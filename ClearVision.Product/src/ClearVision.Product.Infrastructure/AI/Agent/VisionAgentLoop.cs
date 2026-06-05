using System.Diagnostics;
using System.Text.Encodings.Web;
using System.Text.Json;
using ClearVision.Product.Core.AI.Tools;
using Microsoft.Extensions.Options;

namespace ClearVision.Product.Infrastructure.AI.Agent;

public sealed class VisionAgentLoop
{
    private readonly IVisionAgentToolRegistry _toolRegistry;
    private readonly VisionAgentProtocolParser _protocolParser;
    private readonly AgentPromptBuilder _promptBuilder;
    private readonly VisionAgentLoopOptions _options;

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
        IOptions<VisionAgentLoopOptions> options)
    {
        _toolRegistry = toolRegistry;
        _protocolParser = protocolParser;
        _promptBuilder = promptBuilder;
        _options = options.Value;
        _options.Normalize();
    }

    public async Task<VisionAgentLoopResult> RunAsync(
        VisionAgentLoopRequest request,
        CancellationToken cancellationToken)
    {
        if (request.CompleteAsync == null)
        {
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

        for (var round = 0; ; round++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var completion = await request.CompleteAsync(messages, cancellationToken);
            var parsed = _protocolParser.Parse(completion);
            if (!parsed.IsToolCall)
            {
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
                cancellationToken);

            messages.Add(new VisionAgentLoopMessage("assistant", completion));
            messages.Add(new VisionAgentLoopMessage("user", JsonSerializer.Serialize(new
            {
                kind = "tool_result",
                round = round + 1,
                toolResults = roundResults
            }, JsonOptions)));
        }
    }

    private async Task<IReadOnlyList<object>> ExecuteToolRoundAsync(
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
            var parallelResults = await Task.WhenAll(toolCalls.Select(call =>
                ExecuteOneToolAsync(call, context, traces, pendingActions, cancellationToken)));
            return parallelResults;
        }

        var results = new List<object>();
        foreach (var call in toolCalls)
        {
            results.Add(await ExecuteOneToolAsync(call, context, traces, pendingActions, cancellationToken));
        }

        return results;
    }

    private async Task<object> ExecuteOneToolAsync(
        VisionAgentToolCall call,
        VisionAgentToolContext context,
        List<VisionAgentToolTrace> traces,
        List<VisionAgentPendingAction> pendingActions,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var knownTool = _toolRegistry.TryGet(call.Name, out var tool);
        var permission = knownTool ? tool.Permission.ToString() : string.Empty;
        VisionAgentToolResult result;

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
                result = await _toolRegistry.ExecuteAsync(
                    call.Name,
                    context,
                    call.Arguments,
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                result = VisionAgentToolResult.Fail("tool_exception", ex.Message);
            }
        }

        stopwatch.Stop();
        lock (traces)
        {
            traces.Add(new VisionAgentToolTrace
            {
                ToolName = call.Name,
                Arguments = context.DebugTrace
                    ? CloneJsonCompatible(call.Arguments)
                    : SummarizeArguments(call.Arguments),
                Success = result.Success,
                ResultSummary = SummarizeResult(result),
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
