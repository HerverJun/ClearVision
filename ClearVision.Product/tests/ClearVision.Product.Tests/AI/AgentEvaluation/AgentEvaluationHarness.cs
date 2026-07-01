using System.Text.Json;

namespace ClearVision.Product.Tests.AI.AgentEvaluation;

internal sealed class AgentEvaluationHarness
{
    public const string MockSource = "agent_evaluation_harness_mock";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public async Task<AgentEvaluationResult> RunAsync(
        AgentEngineeringEvaluationCase evaluationCase,
        CancellationToken cancellationToken = default)
    {
        var registry = new MockAgentToolRegistry(BuildMockTools(evaluationCase.MockToolResponses));
        var allowedPermissions = BuildAllowedPermissions(evaluationCase.AllowRuntimePreview);
        var context = new MockAgentToolContext
        {
            UserDescription = evaluationCase.UserRequest,
            AllowedPermissions = allowedPermissions
        };
        var state = new EvaluationRunState();
        var callResults = new List<AgentEvaluationToolCallResult>();
        var rawToolResults = new List<(string ToolName, AgentEvaluationToolPermission? Permission, AgentEvaluationToolResult Result)>();
        var pendingActions = new List<string>();
        var blockingIssues = new List<string>();

        foreach (var call in evaluationCase.ToolCalls)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var permission = registry.TryGet(call.ToolName, out var tool)
                ? tool.Permission
                : (AgentEvaluationToolPermission?)null;
            var executeCountBefore = tool?.ExecuteCount ?? 0;
            var arguments = BuildArguments(call, evaluationCase, state);
            var result = await registry.ExecuteAsync(
                call.ToolName,
                context,
                arguments,
                cancellationToken);
            var executedByMock = tool != null && tool.ExecuteCount > executeCountBefore;

            callResults.Add(new AgentEvaluationToolCallResult
            {
                ToolName = call.ToolName,
                Permission = permission?.ToString() ?? string.Empty,
                Success = result.Success,
                ErrorCode = result.ErrorCode,
                ErrorMessage = result.ErrorMessage,
                ExecutedByMock = executedByMock,
                MockSource = executedByMock ? MockSource : null
            });
            rawToolResults.Add((call.ToolName, permission, result));
            pendingActions.AddRange(result.PendingActions.Select(action => action.ActionType));
            blockingIssues.AddRange(ExtractBlockingIssues(call.ToolName, result));
            UpdateState(call.ToolName, result, state);
        }

        var actualFlowStructure = evaluationCase.Flow.ToStructure();
        var actualValidationPreview = BuildValidationPreview(rawToolResults);
        var actualPermissionDecision = BuildPermissionDecision(
            callResults,
            evaluationCase.AllowRuntimePreview);
        var distinctBlockingIssues = blockingIssues
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var failures = Compare(evaluationCase, callResults, actualFlowStructure, pendingActions,
            actualValidationPreview, actualPermissionDecision, distinctBlockingIssues);

        return new AgentEvaluationResult
        {
            CaseId = evaluationCase.CaseId,
            Passed = failures.Count == 0,
            ActualToolCalls = callResults,
            ActualFlowStructure = actualFlowStructure,
            ActualPendingActions = pendingActions,
            ActualValidationPreview = actualValidationPreview,
            ActualPermissionDecision = actualPermissionDecision,
            ActualBlockingIssues = distinctBlockingIssues,
            FailReason = failures.Count == 0 ? null : string.Join("; ", failures),
            PassReason = failures.Count == 0 ? evaluationCase.ExpectedPassFailReason : string.Empty
        };
    }

    private static IReadOnlyList<ScriptedMockAgentTool> BuildMockTools(IReadOnlyList<MockToolResponse> responses)
    {
        return responses
            .GroupBy(response => response.ToolName, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var permissions = group
                    .Select(item => item.Permission)
                    .Distinct()
                    .ToList();
                if (permissions.Count != 1)
                {
                    throw new InvalidOperationException(
                        $"Mock tool '{group.Key}' has inconsistent permissions.");
                }

                return new ScriptedMockAgentTool(group.Key, permissions[0], group.ToList());
            })
            .ToList();
    }

    private static HashSet<AgentEvaluationToolPermission> BuildAllowedPermissions(bool allowRuntimePreview)
    {
        var permissions = new HashSet<AgentEvaluationToolPermission>
        {
            AgentEvaluationToolPermission.ReadOnly,
            AgentEvaluationToolPermission.Simulation,
            AgentEvaluationToolPermission.ConfigDraft,
            AgentEvaluationToolPermission.DeploymentPrepare
        };

        if (allowRuntimePreview)
        {
            permissions.Add(AgentEvaluationToolPermission.RuntimePreview);
        }

        return permissions;
    }

    private static JsonElement BuildArguments(
        EvaluationToolCall call,
        AgentEngineeringEvaluationCase evaluationCase,
        EvaluationRunState state)
    {
        var arguments = call.Arguments.ToDictionary(
            item => item.Key,
            item => item.Value,
            StringComparer.OrdinalIgnoreCase);

        if (call.IncludeFlow)
        {
            arguments["flow"] = evaluationCase.Flow;
        }

        if (call.UseCapturedFrame)
        {
            arguments["temporaryFrameId"] = state.TemporaryFrameId ?? "missing_mock_temporary_frame";
        }

        if (call.IncludeDryRunSummary && state.DryRunSummary.HasValue)
        {
            arguments["dryRunSummary"] = state.DryRunSummary.Value;
        }

        if (call.IncludeReplaySummary && state.ReplaySummary.HasValue)
        {
            arguments["replaySummary"] = state.ReplaySummary.Value;
        }

        return ToJsonElement(arguments);
    }

    private static void UpdateState(
        string toolName,
        AgentEvaluationToolResult result,
        EvaluationRunState state)
    {
        var data = ToNullableJsonElement(result.Data);
        if (!data.HasValue)
        {
            return;
        }

        if (string.Equals(toolName, "capture_test_frame", StringComparison.OrdinalIgnoreCase) &&
            TryReadString(data.Value, "temporaryFrameId", out var temporaryFrameId))
        {
            state.TemporaryFrameId = temporaryFrameId;
        }
        else if (string.Equals(toolName, "dryrun_flow", StringComparison.OrdinalIgnoreCase))
        {
            state.DryRunSummary = data.Value.Clone();
        }
        else if (string.Equals(toolName, "replay_flow_with_frame", StringComparison.OrdinalIgnoreCase))
        {
            state.ReplaySummary = data.Value.Clone();
        }
        else if (string.Equals(toolName, "runtime_package_precheck", StringComparison.OrdinalIgnoreCase))
        {
            state.RuntimePackagePrecheck = data.Value.Clone();
        }
    }

    private static AgentEvaluationValidationPreview BuildValidationPreview(
        IReadOnlyList<(string ToolName, AgentEvaluationToolPermission? Permission, AgentEvaluationToolResult Result)> toolResults)
    {
        return new AgentEvaluationValidationPreview
        {
            StructuralDryRunStatus = ResolveDryRunStatus(toolResults),
            FrameReplayStatus = ResolveReplayStatus(toolResults),
            RuntimePackagePrecheckStatus = ResolvePrecheckStatus(toolResults),
            ToolDryRunTrace = toolResults
                .Where(item => IsValidationPreviewTool(item.ToolName))
                .Select(item => item.ToolName)
                .ToList()
        };
    }

    private static string ResolveDryRunStatus(
        IReadOnlyList<(string ToolName, AgentEvaluationToolPermission? Permission, AgentEvaluationToolResult Result)> toolResults)
    {
        var result = LastToolResult(toolResults, "dryrun_flow");
        if (result == null)
        {
            return "not_run";
        }

        if (!result.Success)
        {
            return "blocked";
        }

        var data = ToNullableJsonElement(result.Data);
        if (data.HasValue &&
            (ReadBool(data.Value, "valid") == false ||
             ReadBool(data.Value, "isValid") == false ||
             ReadStringArray(data.Value, "blockingIssues").Count > 0))
        {
            return "blocked";
        }

        return "ok";
    }

    private static string ResolveReplayStatus(
        IReadOnlyList<(string ToolName, AgentEvaluationToolPermission? Permission, AgentEvaluationToolResult Result)> toolResults)
    {
        var result = LastToolResult(toolResults, "replay_flow_with_frame");
        if (result == null)
        {
            return "not_run";
        }

        if (string.Equals(result.ErrorCode, "tool_permission_denied", StringComparison.OrdinalIgnoreCase))
        {
            return "permission_denied";
        }

        if (!result.Success)
        {
            return "blocked";
        }

        var data = ToNullableJsonElement(result.Data);
        if (data.HasValue &&
            (ReadBool(data.Value, "replayExecuted") == true ||
             ReadBool(data.Value, "replaySucceeded") == true ||
             ReadBool(data.Value, "isSuccess") == true))
        {
            return "ok";
        }

        return "blocked";
    }

    private static string ResolvePrecheckStatus(
        IReadOnlyList<(string ToolName, AgentEvaluationToolPermission? Permission, AgentEvaluationToolResult Result)> toolResults)
    {
        var result = LastToolResult(toolResults, "runtime_package_precheck");
        if (result == null)
        {
            return "not_run";
        }

        if (!result.Success)
        {
            return "blocked";
        }

        var data = ToNullableJsonElement(result.Data);
        if (data.HasValue && ReadBool(data.Value, "ready") == true)
        {
            return ReadStringArray(data.Value, "warnings").Count > 0
                ? "ready_with_warnings"
                : "ready";
        }

        return "blocked";
    }

    private static AgentEvaluationPermissionDecision BuildPermissionDecision(
        IReadOnlyList<AgentEvaluationToolCallResult> callResults,
        bool runtimePreviewAllowed)
    {
        return new AgentEvaluationPermissionDecision
        {
            RuntimePreviewAllowed = runtimePreviewAllowed,
            DeniedToolNames = callResults
                .Where(item => string.Equals(item.ErrorCode, "tool_permission_denied", StringComparison.OrdinalIgnoreCase))
                .Select(item => item.ToolName)
                .ToList(),
            RuntimePreviewExecutedToolNames = callResults
                .Where(item => item.ExecutedByMock &&
                               string.Equals(item.Permission, AgentEvaluationToolPermission.RuntimePreview.ToString(),
                                   StringComparison.OrdinalIgnoreCase))
                .Select(item => item.ToolName)
                .ToList(),
            DeploymentPrepareExecutedToolNames = callResults
                .Where(item => item.ExecutedByMock &&
                               string.Equals(item.Permission, AgentEvaluationToolPermission.DeploymentPrepare.ToString(),
                                   StringComparison.OrdinalIgnoreCase))
                .Select(item => item.ToolName)
                .ToList()
        };
    }

    private static IReadOnlyList<string> ExtractBlockingIssues(string toolName, AgentEvaluationToolResult result)
    {
        var issues = new List<string>();
        var data = ToNullableJsonElement(result.Data);
        if (data.HasValue)
        {
            issues.AddRange(ReadStringArray(data.Value, "blockingIssues"));
            issues.AddRange(ReadStringArray(data.Value, "errors"));
        }

        if (!result.Success &&
            !string.Equals(result.ErrorCode, "tool_permission_denied", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(result.ErrorMessage))
        {
            issues.Add($"{toolName}: {result.ErrorMessage}");
        }

        return issues;
    }

    private static IReadOnlyList<string> Compare(
        AgentEngineeringEvaluationCase evaluationCase,
        IReadOnlyList<AgentEvaluationToolCallResult> callResults,
        EvaluationFlowStructure flowStructure,
        IReadOnlyList<string> pendingActions,
        AgentEvaluationValidationPreview validationPreview,
        AgentEvaluationPermissionDecision permissionDecision,
        IReadOnlyList<string> blockingIssues)
    {
        var failures = new List<string>();
        AddSequenceFailure(failures, "tool calls",
            evaluationCase.ExpectedToolCalls,
            callResults.Select(item => item.ToolName).ToList());
        AddJsonFailure(failures, "flow structure",
            evaluationCase.ExpectedFlowStructure,
            flowStructure);
        AddSequenceFailure(failures, "pending actions",
            evaluationCase.ExpectedPendingActions,
            pendingActions);
        AddJsonFailure(failures, "validation preview",
            evaluationCase.ExpectedValidationPreview,
            validationPreview);
        AddJsonFailure(failures, "permission behavior",
            evaluationCase.ExpectedPermissionBehavior,
            permissionDecision);
        AddSequenceFailure(failures, "blocking issues",
            evaluationCase.ExpectedBlockingIssues,
            blockingIssues);

        return failures;
    }

    private static void AddSequenceFailure(
        List<string> failures,
        string label,
        IReadOnlyList<string> expected,
        IReadOnlyList<string> actual)
    {
        if (!expected.SequenceEqual(actual, StringComparer.Ordinal))
        {
            failures.Add($"{label} expected [{string.Join(", ", expected)}] actual [{string.Join(", ", actual)}]");
        }
    }

    private static void AddJsonFailure(List<string> failures, string label, object expected, object actual)
    {
        var expectedJson = JsonSerializer.Serialize(expected, JsonOptions);
        var actualJson = JsonSerializer.Serialize(actual, JsonOptions);
        if (!string.Equals(expectedJson, actualJson, StringComparison.Ordinal))
        {
            failures.Add($"{label} expected {expectedJson} actual {actualJson}");
        }
    }

    private static AgentEvaluationToolResult? LastToolResult(
        IReadOnlyList<(string ToolName, AgentEvaluationToolPermission? Permission, AgentEvaluationToolResult Result)> toolResults,
        string toolName)
    {
        return toolResults
            .LastOrDefault(item => string.Equals(item.ToolName, toolName, StringComparison.OrdinalIgnoreCase))
            .Result;
    }

    private static bool IsValidationPreviewTool(string toolName)
    {
        return string.Equals(toolName, "dryrun_flow", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(toolName, "replay_flow_with_frame", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(toolName, "runtime_package_precheck", StringComparison.OrdinalIgnoreCase);
    }

    private static JsonElement ToJsonElement(object value)
    {
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(value, JsonOptions));
        return doc.RootElement.Clone();
    }

    private static JsonElement? ToNullableJsonElement(object? value)
    {
        if (value == null)
        {
            return null;
        }

        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(value, JsonOptions));
        return doc.RootElement.Clone();
    }

    private static bool TryReadString(JsonElement element, string propertyName, out string value)
    {
        value = string.Empty;
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty(propertyName, out var property) ||
            property.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(property.GetString()))
        {
            return false;
        }

        value = property.GetString()!;
        return true;
    }

    private static bool? ReadBool(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty(propertyName, out var property))
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

    private static IReadOnlyList<string> ReadStringArray(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty(propertyName, out var property) ||
            property.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return property.EnumerateArray()
            .Select(item => item.ValueKind == JsonValueKind.String ? item.GetString() : item.GetRawText())
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item!)
            .ToList();
    }

    private sealed class EvaluationRunState
    {
        public string? TemporaryFrameId { get; set; }
        public JsonElement? DryRunSummary { get; set; }
        public JsonElement? ReplaySummary { get; set; }
        public JsonElement? RuntimePackagePrecheck { get; set; }
    }

    private sealed record MockAgentToolContext
    {
        public string UserDescription { get; init; } = string.Empty;
        public IReadOnlySet<AgentEvaluationToolPermission> AllowedPermissions { get; init; } =
            new HashSet<AgentEvaluationToolPermission>();
    }

    private sealed class MockAgentToolRegistry
    {
        private readonly IReadOnlyDictionary<string, ScriptedMockAgentTool> _tools;

        public MockAgentToolRegistry(IReadOnlyList<ScriptedMockAgentTool> tools)
        {
            _tools = tools.ToDictionary(tool => tool.Name, StringComparer.OrdinalIgnoreCase);
        }

        public bool TryGet(string name, out ScriptedMockAgentTool tool)
        {
            return _tools.TryGetValue(name.Trim(), out tool!);
        }

        public Task<AgentEvaluationToolResult> ExecuteAsync(
            string name,
            MockAgentToolContext context,
            JsonElement arguments,
            CancellationToken cancellationToken)
        {
            if (!TryGet(name, out var tool))
            {
                return Task.FromResult(AgentEvaluationToolResult.Fail(
                    "unknown_tool",
                    $"Mock agent tool '{name}' is not registered."));
            }

            if (tool.Permission == AgentEvaluationToolPermission.ConfigWrite)
            {
                return Task.FromResult(AgentEvaluationToolResult.Fail(
                    "tool_permission_denied",
                    $"Tool '{tool.Name}' requires ConfigWrite, which is not allowed in Evaluation Harness v0.1."));
            }

            if (tool.Permission == AgentEvaluationToolPermission.DeploymentPrepare &&
                !string.Equals(tool.Name, "runtime_package_precheck", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(AgentEvaluationToolResult.Fail(
                    "tool_permission_denied",
                    $"Tool '{tool.Name}' requests DeploymentPrepare, but only runtime_package_precheck is allowed."));
            }

            if (!context.AllowedPermissions.Contains(tool.Permission))
            {
                return Task.FromResult(AgentEvaluationToolResult.Fail(
                    "tool_permission_denied",
                    $"Tool '{tool.Name}' requires permission '{tool.Permission}', which is not allowed in this mock session."));
            }

            return tool.ExecuteAsync(context, arguments, cancellationToken);
        }
    }

    private sealed class ScriptedMockAgentTool
    {
        private readonly Queue<MockToolResponse> _responses;

        public ScriptedMockAgentTool(
            string name,
            AgentEvaluationToolPermission permission,
            IReadOnlyList<MockToolResponse> responses)
        {
            Name = name;
            Permission = permission;
            _responses = new Queue<MockToolResponse>(responses);
        }

        public string Name { get; }
        public AgentEvaluationToolPermission Permission { get; }
        public int ExecuteCount { get; private set; }

        public Task<AgentEvaluationToolResult> ExecuteAsync(
            MockAgentToolContext context,
            JsonElement arguments,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ExecuteCount++;
            if (_responses.Count == 0)
            {
                return Task.FromResult(AgentEvaluationToolResult.Fail(
                    "mock_response_missing",
                    $"No scripted mock response remains for tool '{Name}'."));
            }

            var response = _responses.Dequeue();
            return Task.FromResult(new AgentEvaluationToolResult
            {
                Success = response.Success,
                Data = response.Data,
                ErrorCode = response.ErrorCode,
                ErrorMessage = response.ErrorMessage,
                RequiresUserConfirmation = response.RequiresUserConfirmation,
                PendingActions = response.PendingActions.ToList()
            });
        }
    }
}
