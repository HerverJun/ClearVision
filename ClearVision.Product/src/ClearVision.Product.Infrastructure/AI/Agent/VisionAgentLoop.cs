using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ClearVision.Product.Core.AI.Tools;
using ClearVision.Product.Core.DTOs;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Infrastructure.AI;
using ClearVision.Product.Infrastructure.AI.Connectors;
using ClearVision.Product.Infrastructure.AI.Runtime;
using ClearVision.Product.Contracts.Messages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ClearVision.Product.Infrastructure.AI.Agent;

public sealed class VisionAgentLoopResult
{
    public bool Success { get; set; }
    public AiGeneratedFlowJson? Flow { get; set; }
    public List<VisionAgentToolTrace>? ToolTrace { get; set; }
    public string? Explanation { get; set; }
    public string? Reasoning { get; set; }
    public int RetryCount { get; set; }
    public string? ErrorMessage { get; set; }
}

public sealed class VisionAgentLoop
{
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly AiGenerationOrchestrator _aiOrchestrator;
    private readonly VisionAgentProtocolParser _protocolParser;
    private readonly Microsoft.Extensions.Logging.ILogger<VisionAgentLoop> _logger;

    public VisionAgentLoop(
        IServiceScopeFactory serviceScopeFactory,
        AiGenerationOrchestrator aiOrchestrator,
        VisionAgentProtocolParser protocolParser,
        Microsoft.Extensions.Logging.ILogger<VisionAgentLoop> logger)
    {
        _serviceScopeFactory = serviceScopeFactory;
        _aiOrchestrator = aiOrchestrator;
        _protocolParser = protocolParser;
        _logger = logger;
    }

    public async Task<VisionAgentLoopResult> RunAsync(
        string systemPrompt,
        List<ChatMessage> initialMessages,
        string sessionId,
        string? existingFlowJson,
        string? targetStationId,
        AiGenerationOptions options,
        AiModelConfig activeModel,
        Action<AiStreamChunk>? onStreamChunk,
        CancellationToken cancellationToken)
    {
        var toolTraceList = new List<VisionAgentToolTrace>();
        var messages = new List<ChatMessage>(initialMessages);

        int maxRounds = options.MaxRetries > 0 ? options.MaxRetries : 3; // Keep aligned with retry options or default to 3
        int hardLimit = 5;
        int currentRound = 0;
        int parseErrors = 0;

        AiGeneratedFlowJson? finalFlow = null;
        string? reasoningContent = null;

        while (currentRound < hardLimit)
        {
            currentRound++;

            if (currentRound > maxRounds)
            {
                _logger.LogWarning("VisionAgentLoop exceeded maximum rounds limit ({MaxRounds})", maxRounds);
                return new VisionAgentLoopResult
                {
                    Success = false,
                    ToolTrace = toolTraceList,
                    ErrorMessage = "达到工具调用多轮 Loop 上限，未生成最终流程图。请重试或修改您的需求。"
                };
            }

            _logger.LogInformation("VisionAgentLoop round {Round}/{MaxRounds} calling LLM", currentRound, maxRounds);

            var completionResult = await _aiOrchestrator.StreamCompleteAsync(
                systemPrompt,
                messages,
                chunk => onStreamChunk?.Invoke(chunk),
                activeModel,
                cancellationToken);

            if (!string.IsNullOrEmpty(completionResult.Reasoning))
            {
                reasoningContent = completionResult.Reasoning;
            }

            var content = completionResult.Content ?? string.Empty;

            // 1. Check if model calls tools
            if (_protocolParser.TryParseToolCalls(content, out var toolCallRequest) && toolCallRequest != null)
            {
                _logger.LogInformation("VisionAgentLoop detected {Count} tool calls in LLM response", toolCallRequest.ToolCalls.Count);
                
                messages.Add(new ChatMessage("assistant", content));

                var results = new List<(string Id, VisionAgentToolResult Result, long DurationMs, IVisionAgentTool Tool)>();
                var readOnlyCalls = new List<VisionAgentToolCallItem>();
                var stateCalls = new List<VisionAgentToolCallItem>();

                using (var scope = _serviceScopeFactory.CreateScope())
                {
                    var registry = scope.ServiceProvider.GetRequiredService<IVisionAgentToolRegistry>();
                    foreach (var call in toolCallRequest.ToolCalls)
                    {
                        if (registry.TryGet(call.Name, out var tool))
                        {
                            if (tool.Permission == VisionAgentToolPermission.ReadOnly)
                                readOnlyCalls.Add(call);
                            else
                                stateCalls.Add(call);
                        }
                        else
                        {
                            stateCalls.Add(call); // unknown tool, process sequentially to return error
                        }
                    }
                }

                // Execute ReadOnly tools in parallel
                if (readOnlyCalls.Count > 0)
                {
                    var tasks = readOnlyCalls.Select(async call =>
                    {
                        var sw = Stopwatch.StartNew();
                        using var scope = _serviceScopeFactory.CreateScope();
                        var registry = scope.ServiceProvider.GetRequiredService<IVisionAgentToolRegistry>();

                        var context = new VisionAgentToolContext
                        {
                            SessionId = sessionId,
                            ExistingFlowJson = existingFlowJson,
                            TargetStationId = targetStationId
                        };

                        registry.TryGet(call.Name, out var tool);
                        var res = await registry.ExecuteAsync(call.Name, context, call.Arguments, cancellationToken);
                        return (call.Id, Result: res, DurationMs: sw.ElapsedMilliseconds, Tool: tool!);
                    });

                    var resolved = await Task.WhenAll(tasks);
                    results.AddRange(resolved);
                }

                // Execute status modifying/simulation tools sequentially
                foreach (var call in stateCalls)
                {
                    var sw = Stopwatch.StartNew();
                    using var scope = _serviceScopeFactory.CreateScope();
                    var registry = scope.ServiceProvider.GetRequiredService<IVisionAgentToolRegistry>();

                    var context = new VisionAgentToolContext
                    {
                        SessionId = sessionId,
                        ExistingFlowJson = existingFlowJson,
                        TargetStationId = targetStationId
                    };

                    IVisionAgentTool? tool = null;
                    VisionAgentToolResult res;
                    if (registry.TryGet(call.Name, out tool))
                    {
                        res = await registry.ExecuteAsync(call.Name, context, call.Arguments, cancellationToken);
                    }
                    else
                    {
                        res = VisionAgentToolResult.CreateFailure($"Unknown tool '{call.Name}'.");
                    }
                    
                    results.Add((call.Id, res, sw.ElapsedMilliseconds, tool!));
                }

                // Format results as user message response to LLM
                var toolResultItems = results.Select(r => new
                {
                    id = r.Id,
                    status = r.Result.Success ? "success" : "failed",
                    result = r.Result.Success ? r.Result.Data : null,
                    errorMessage = r.Result.ErrorMessage
                }).ToList();

                var toolResponseJson = JsonSerializer.Serialize(new { toolResults = toolResultItems });
                messages.Add(new ChatMessage("user", toolResponseJson));

                // Save traces
                foreach (var r in results)
                {
                    toolTraceList.Add(new VisionAgentToolTrace
                    {
                        ToolName = r.Tool?.Name ?? "unknown",
                        Arguments = null, // Avoid bloating trace with arguments
                        Success = r.Result.Success,
                        ResultSummary = r.Result.Summary,
                        ErrorMessage = r.Result.ErrorMessage,
                        DurationMs = r.DurationMs,
                        Permission = r.Tool?.Permission.ToString() ?? "None"
                    });
                }
            }
            // 2. Check if model outputs final flow
            else if (_protocolParser.TryParseFinalFlow(content, out var flowJson) && flowJson != null)
            {
                _logger.LogInformation("VisionAgentLoop successfully matched final flow.");
                finalFlow = flowJson;
                break;
            }
            else
            {
                // Format parsing retry
                parseErrors++;
                if (parseErrors >= 2)
                {
                    return new VisionAgentLoopResult
                    {
                        Success = false,
                        ToolTrace = toolTraceList,
                        ErrorMessage = "解析最终流程格式连续失败，未生成有效流程结构。"
                    };
                }

                messages.Add(new ChatMessage("assistant", content));
                messages.Add(new ChatMessage("user", "【格式错误】您返回的数据不符合工具调用或 final_flow JSON 协议。请确认以正确的 JSON 格式（包含 { 和 }）输出结果。"));
            }
        }

        if (finalFlow == null)
        {
            return new VisionAgentLoopResult
            {
                Success = false,
                ToolTrace = toolTraceList,
                ErrorMessage = "Loop terminated without generating a valid final flow."
            };
        }

        return new VisionAgentLoopResult
        {
            Success = true,
            Flow = finalFlow,
            ToolTrace = toolTraceList,
            Explanation = finalFlow.Explanation,
            Reasoning = reasoningContent,
            RetryCount = currentRound - 1
        };
    }
}
