using System.Diagnostics;
using ClearVision.Product.Core.AI.Tools;
using ClearVision.Product.Core.DTOs;
using ClearVision.Product.Infrastructure.AI.AgentRun;

namespace ClearVision.Product.Infrastructure.AI.Agent;

public sealed class BuildToolRunner
{
    private readonly IVisionAgentToolRegistry _toolRegistry;
    private readonly IAgentRunEventSink? _eventSink;
    private readonly AgentRunEventRedactor _redactor;

    public BuildToolRunner(
        IVisionAgentToolRegistry toolRegistry,
        AgentRunEventRedactor redactor,
        IAgentRunEventSink? eventSink = null)
    {
        _toolRegistry = toolRegistry;
        _redactor = redactor;
        _eventSink = eventSink;
    }

    internal async Task<BuildStepResult<T>> ExecuteEvidenceStepAsync<T>(
        string? runId,
        List<VisionAgentToolEvidence> evidence,
        string stage,
        string toolName,
        string inputSummary,
        Func<CancellationToken, Task<BuildStepResult<T>>> action,
        CancellationToken cancellationToken,
        string? completionEventType = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var evidenceId = $"ev_{Guid.NewGuid():N}";
        _eventSink?.StageStarted(runId, stage, VisionAgentBuildSupport.StageTitle(stage), inputSummary, new
        {
            evidenceId,
            toolName,
            inputSummary = _redactor.RedactText(inputSummary),
            metadataOnly = true,
            redactionPass = true
        });
        _eventSink?.ToolStarted(runId, stage, toolName, new
        {
            evidenceId,
            toolName,
            inputSummary = _redactor.RedactText(inputSummary),
            metadataOnly = true,
            redactionPass = true
        });

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var result = await action(cancellationToken);
            stopwatch.Stop();
            var status = VisionAgentBuildSupport.NormalizeStatus(result.Status);
            var item = new VisionAgentToolEvidence
            {
                Stage = stage,
                ToolName = toolName,
                InputSummary = _redactor.RedactText(inputSummary),
                OutputSummary = _redactor.RedactText(result.OutputSummary),
                Status = status,
                DurationMs = stopwatch.ElapsedMilliseconds,
                EvidenceId = evidenceId,
                RepairAction = _redactor.RedactText(result.RepairAction),
                WarningCode = _redactor.RedactText(result.WarningCode),
                ApplyImpact = _redactor.RedactText(result.ApplyImpact),
                DeploymentImpact = _redactor.RedactText(result.DeploymentImpact),
                MetadataOnly = true,
                RedactionPass = true
            };
            evidence.Add(item);
            var payload = EvidencePayload(item, result.PayloadDetails);
            if (status == AgentRunEventStatuses.Failed)
            {
                _eventSink?.ToolFailed(runId, stage, toolName, item.DurationMs, item.OutputSummary, payload);
            }
            else
            {
                _eventSink?.ToolCompleted(runId, stage, toolName, item.DurationMs, payload);
            }

            if (!string.IsNullOrWhiteSpace(completionEventType))
            {
                _eventSink?.Append(runId, new AgentRunEventDraft
                {
                    EventType = completionEventType,
                    Stage = stage,
                    Title = VisionAgentBuildSupport.StageTitle(stage),
                    Summary = item.OutputSummary,
                    Status = status,
                    Payload = payload
                });
            }

            _eventSink?.StageCompleted(runId, stage, VisionAgentBuildSupport.StageTitle(stage), item.OutputSummary, payload);
            return result;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            var item = new VisionAgentToolEvidence
            {
                Stage = stage,
                ToolName = toolName,
                InputSummary = _redactor.RedactText(inputSummary),
                OutputSummary = _redactor.RedactText(ex.Message),
                Status = AgentRunEventStatuses.Failed,
                DurationMs = stopwatch.ElapsedMilliseconds,
                EvidenceId = evidenceId,
                WarningCode = "tool_exception",
                ApplyImpact = "blocked",
                DeploymentImpact = "blocked",
                MetadataOnly = true,
                RedactionPass = true
            };
            evidence.Add(item);
            _eventSink?.ToolFailed(runId, stage, toolName, item.DurationMs, item.OutputSummary, EvidencePayload(item, null));
            throw;
        }
    }

    internal async Task<BuildStepResult<VisionAgentToolResult>> RunRegisteredToolAsync(
        string? runId,
        List<VisionAgentToolEvidence> evidence,
        VisionAgentToolContext context,
        string stage,
        string toolName,
        string inputSummary,
        object arguments,
        CancellationToken cancellationToken,
        string completionEventType)
    {
        return await ExecuteEvidenceStepAsync(
            runId,
            evidence,
            stage,
            toolName,
            inputSummary,
            async ct =>
            {
                var result = await _toolRegistry.ExecuteAsync(
                    toolName,
                    context,
                    VisionAgentBuildSupport.ToJsonElement(arguments),
                    ct);
                var data = VisionAgentBuildSupport.ToJsonElementOrNull(result.Data);
                var hasBlocking = ToolHasBlockingIssues(new VisionAgentToolResult
                {
                    Success = result.Success,
                    Data = result.Data,
                    ErrorCode = result.ErrorCode,
                    ErrorMessage = result.ErrorMessage,
                    PendingActions = result.PendingActions
                });
                return VisionAgentBuildSupport.StepResult(
                    result,
                    result.Success
                        ? ToolSummary(toolName, data, hasBlocking)
                        : $"{DisplayToolName(toolName)} 执行失败：{result.ErrorCode}",
                    result.Success && !hasBlocking ? AgentRunEventStatuses.Completed :
                    result.Success ? AgentRunEventStatuses.Blocked : AgentRunEventStatuses.Failed,
                    new
                    {
                        toolName,
                        success = result.Success,
                        errorCode = result.ErrorCode,
                        data = result.Data,
                        pendingActionCount = result.PendingActions.Count,
                        blocking = hasBlocking,
                        metadataOnly = true
                    },
                    warningCode: hasBlocking ? $"{toolName}_blocked" : string.Empty,
                    applyImpact: hasBlocking && toolName == "validate_flow" ? "blocked" : "editable_draft_allowed",
                    deploymentImpact: hasBlocking ? "deployment_blocked" : "no_deployment_blocker");
            },
            cancellationToken,
            completionEventType);
    }

    private object? EvidencePayload(VisionAgentToolEvidence evidence, object? details)
    {
        var payload = _redactor.RedactObject(new
        {
            evidence.Stage,
            evidence.ToolName,
            evidence.InputSummary,
            evidence.OutputSummary,
            evidence.Status,
            evidence.DurationMs,
            evidence.EvidenceId,
            evidence.RepairAction,
            evidence.WarningCode,
            evidence.ApplyImpact,
            evidence.DeploymentImpact,
            evidence.MetadataOnly,
            evidence.RedactionPass,
            details
        });
        if (_redactor.IsRedactionSafe(payload))
        {
            return payload;
        }

        return new
        {
            evidence.Stage,
            evidence.ToolName,
            evidence.InputSummary,
            OutputSummary = "发布该工具证据前，已移除不安全元数据。",
            Status = AgentRunEventStatuses.Completed,
            evidence.DurationMs,
            evidence.EvidenceId,
            evidence.RepairAction,
            WarningCode = "unsafe_metadata_redacted",
            evidence.ApplyImpact,
            DeploymentImpact = "review_public_diagnostics",
            MetadataOnly = true,
            RedactionPass = true,
            DetailsRedacted = true
        };
    }

    internal static bool ToolHasBlockingIssues(VisionAgentToolResult result)
    {
        if (!result.Success)
        {
            return true;
        }

        return VisionAgentBuildSupport.ReadCount(result.Data, "blockingIssues") > 0 ||
               VisionAgentBuildSupport.ReadBool(result.Data, "dryRunSucceeded") == false;
    }

    private static string ToolSummary(string toolName, System.Text.Json.JsonElement? data, bool blocking)
    {
        var displayName = DisplayToolName(toolName);
        if (data == null)
        {
            return $"{displayName} 已完成，但没有可公开的数据负载。";
        }

        if (toolName == "validate_flow")
        {
            return blocking
                ? "结构校验发现阻断问题。"
                : "结构校验已通过，公开元数据可用。";
        }

        if (toolName == "dryrun_flow")
        {
            return VisionAgentBuildSupport.ReadBool(data.Value, "dryRunSucceeded") == false
                ? "元数据预演报告草稿存在阻断。"
                : "元数据预演已成功完成。";
        }

        if (toolName == "runtime_package_precheck")
        {
            return VisionAgentBuildSupport.ReadBool(data.Value, "readyForDeployment") == true
                ? "运行包就绪检查通过。"
                : "运行包就绪检查会阻断部署，但不阻断画布应用。";
        }

        return $"{displayName} 已完成。";
    }

    private static string DisplayToolName(string toolName)
    {
        return toolName switch
        {
            "match_flow_template" => "模板匹配工具",
            "get_flow_template_skeleton" => "模板骨架工具",
            "validate_flow" => "流程结构校验工具",
            "dryrun_flow" => "元数据预演工具",
            "runtime_package_precheck" => "运行包就绪检查工具",
            _ => toolName
        };
    }
}
