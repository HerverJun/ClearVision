using System.Text.Json;
using ClearVision.Product.Application.DTOs;
using ClearVision.Product.Core.AI.Tools;
using ClearVision.Product.Core.DTOs;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Interfaces;
using ClearVision.Product.Infrastructure.AI.Tools;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ClearVision.Product.Infrastructure.AI.Agent;

public interface IVisionAgentGenerateFlowService
{
    Task<AiFlowGenerationResult> GenerateFlowAsync(
        AiFlowGenerationRequest request,
        CancellationToken cancellationToken);
}

public sealed class VisionAgentGenerateFlowService : IVisionAgentGenerateFlowService
{
    private readonly VisionAgentLoop _loop;
    private readonly Microsoft.Extensions.Logging.ILogger<VisionAgentGenerateFlowService> _logger;
    private readonly VisionAgentLoopOptions _loopOptions;
    private readonly AgentGenerateFlowOptions _agentOptions;
    private readonly IVisionAgentPlannerService? _plannerService;
    private readonly VisionAgentProtocolParser _protocolParser;
    private readonly AgentWorkflowDraftEditor _draftEditor;
    private readonly IConfigurationService? _configurationService;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public VisionAgentGenerateFlowService(
        VisionAgentLoop loop,
        IOptions<VisionAgentLoopOptions> loopOptions,
        Microsoft.Extensions.Logging.ILogger<VisionAgentGenerateFlowService> logger,
        IOptions<AgentGenerateFlowOptions>? agentOptions = null,
        IVisionAgentPlannerService? plannerService = null,
        VisionAgentProtocolParser? protocolParser = null,
        AgentWorkflowDraftEditor? draftEditor = null,
        IConfigurationService? configurationService = null)
    {
        _loop = loop;
        _logger = logger;
        _loopOptions = loopOptions.Value;
        _loopOptions.Normalize();
        _agentOptions = agentOptions?.Value ?? new AgentGenerateFlowOptions();
        _agentOptions.Mode = AiAgentGenerateFlowModes.Normalize(_agentOptions.Mode);
        _plannerService = plannerService;
        _protocolParser = protocolParser ?? new VisionAgentProtocolParser();
        _draftEditor = draftEditor ?? new AgentWorkflowDraftEditor();
        _configurationService = configurationService;
    }

    public async Task<AiFlowGenerationResult> GenerateFlowAsync(
        AiFlowGenerationRequest request,
        CancellationToken cancellationToken)
    {
        if (ShouldUsePlanner(request))
        {
            var plannerResult = await RunPlannerAsync(request, cancellationToken);
            if (plannerResult.Success || !_agentOptions.FallbackToScriptedOnPlannerFailure)
            {
                return plannerResult;
            }

            _logger.LogWarning(
                "Vision Agent planner failed and will fall back to scripted mode. Error={Error}",
                plannerResult.ErrorMessage);
            return await RunScriptedAsync(
                request,
                "agent_planner_scripted_fallback",
                "Vision Agent planner failed; scripted safe fallback generated a workflow draft.",
                cancellationToken);
        }

        return await RunScriptedAsync(
            request,
            "agent_controlled_scripted",
            "Controlled Vision Agent generated a workflow draft from static engineering tools.",
            cancellationToken);
    }

    private Task<AiFlowGenerationResult> RunScriptedAsync(
        AiFlowGenerationRequest request,
        string generationMode,
        string explanation,
        CancellationToken cancellationToken)
    {
        var capture = new AgentToolResultCapture();
        var completion = new AgentGenerateFlowScript(request, capture);
        return RunWithCompletionAsync(
            request,
            capture,
            completion.CompleteAsync,
            generationMode,
            explanation,
            cancellationToken);
    }

    private Task<AiFlowGenerationResult> RunPlannerAsync(
        AiFlowGenerationRequest request,
        CancellationToken cancellationToken)
    {
        if (_plannerService == null)
        {
            return Task.FromResult(ControlledFailure(
                "Vision Agent planner service is not registered.",
                []));
        }

        var capture = new AgentToolResultCapture();
        if (!string.IsNullOrWhiteSpace(request.ExistingFlowJson))
        {
            using var doc = JsonDocument.Parse(request.ExistingFlowJson);
            capture.FlowDraft = doc.RootElement.Clone();
        }

        var completion = new AgentGenerateFlowPlanner(
            request,
            capture,
            _plannerService,
            _protocolParser,
            _draftEditor);
        return RunWithCompletionAsync(
            request,
            capture,
            completion.CompleteAsync,
            "agent_planner",
            "Vision Agent planner generated or edited a workflow draft with static engineering tools.",
            cancellationToken);
    }

    private async Task<AiFlowGenerationResult> RunWithCompletionAsync(
        AiFlowGenerationRequest request,
        AgentToolResultCapture capture,
        Func<IReadOnlyList<VisionAgentLoopMessage>, CancellationToken, Task<string>> completeAsync,
        string generationMode,
        string explanation,
        CancellationToken cancellationToken)
    {
        VisionAgentLoopResult loopResult;
        try
        {
            var allowedPermissions = new HashSet<VisionAgentToolPermission>
            {
                VisionAgentToolPermission.ReadOnly,
                VisionAgentToolPermission.Simulation,
                VisionAgentToolPermission.DeploymentPrepare
            };
            if (RuntimePreviewPermissionGate.HasConsent(request))
            {
                allowedPermissions.Add(VisionAgentToolPermission.RuntimePreview);
            }

            loopResult = await _loop.RunAsync(new VisionAgentLoopRequest
            {
                UserPrompt = request.Description,
                ToolContext = new VisionAgentToolContext
                {
                    UserDescription = request.Description,
                    AdditionalContext = request.AdditionalContext,
                    ExistingFlowJson = request.ExistingFlowJson,
                    MaxToolResultChars = _loopOptions.MaxToolResultChars,
                    RuntimePreviewConsent = request.RuntimePreviewConsent,
                    RuntimePreviewPilot = ResolveRuntimePreviewPilotConfig(),
                    AllowedPermissions = allowedPermissions
                },
                CompleteAsync = completeAsync
            }, cancellationToken);
        }
        catch (AgentToolCallPolicyViolationException ex)
        {
            return ControlledFailure(
                $"Vision Agent planner tool policy denied the request: {ex.Message}",
                []);
        }
        catch (Exception ex)
        {
            return ControlledFailure(
                $"Vision Agent GenerateFlow failed: {ex.Message}",
                []);
        }

        if (!loopResult.Success)
        {
            return ControlledFailure(
                loopResult.ErrorMessage ?? "Vision agent loop failed.",
                loopResult.ToolTrace);
        }

        if (capture.FlowDraft.ValueKind != JsonValueKind.Object)
        {
            return ControlledFailure(
                "Vision agent did not produce a workflow draft.",
                loopResult.ToolTrace);
        }

        try
        {
            var flow = BuildFlowDto(capture.FlowDraft, request.Description, out var operatorIdMap);
            var validationPreview = new
            {
                structuralValidation = CloneJsonCompatible(capture.ValidationSummary),
                dryRun = CloneJsonCompatible(capture.DryRunSummary),
                deploymentPrecheck = CloneJsonCompatible(capture.DeploymentPrecheck),
                runtimePreview = CloneJsonCompatible(capture.RuntimePreviewSummary)
            };
            var missingResources = BuildMissingResources(capture.DeploymentPrecheck);
            var pendingParameters = BuildPendingParameters(capture.DeploymentPrecheck, operatorIdMap);
            var pendingActions = loopResult.PendingActions
                .Select(ClonePendingAction)
                .Concat(ReadArray(capture.DeploymentPrecheck, "pendingActions")
                .Select(CloneJsonCompatible)
                .Where(item => item != null)
                .Cast<object>())
                .ToList();

            return new AiFlowGenerationResult
            {
                Success = true,
                CompletionStatus = AiFlowGenerationResult.CompletionStatusCompleted,
                Flow = flow,
                AiExplanation = explanation,
                ParametersNeedingReview = BuildParametersNeedingReview(pendingParameters),
                RetryCount = 0,
                SessionId = request.SessionId,
                DetectedIntent = AiTurnIntents.NewFlow,
                DryRunResult = CloneJsonCompatible(capture.DryRunSummary),
                GenerationMode = generationMode,
                TemplateLockLevel = generationMode.Contains("planner", StringComparison.OrdinalIgnoreCase)
                    ? "agent_planner_draft"
                    : "agent_template_skeleton",
                PendingParameters = pendingParameters,
                MissingResources = missingResources,
                PendingActions = pendingActions,
                ValidationPreview = validationPreview,
                ToolTrace = loopResult.ToolTrace.Select(MapTrace).ToList(),
                StageTimeline =
                [
                    new AiGenerationStageDiagnostic
                    {
                        Stage = generationMode,
                        Status = "completed",
                        Summary = $"toolCalls={loopResult.ToolTrace.Count}, rounds={loopResult.ToolRounds}",
                        DurationMs = 0,
                        Metadata = new Dictionary<string, string>
                        {
                            ["agentMode"] = generationMode,
                            ["toolCalls"] = loopResult.ToolTrace.Count.ToString()
                        }
                    }
                ],
                TurnIntent = AiTurnIntents.NewFlow,
                InteractionState = AiInteractionStates.Completed,
                RouterConfidence = AiRouterConfidence.High
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Controlled Vision Agent GenerateFlow mapping failed.");
            return ControlledFailure(
                $"Controlled Vision Agent GenerateFlow mapping failed: {ex.Message}",
                loopResult.ToolTrace);
        }
    }

    private bool ShouldUsePlanner(AiFlowGenerationRequest request)
    {
        if (!request.UseVisionAgentGenerateFlow)
        {
            return false;
        }

        var requestMode = AiAgentGenerateFlowModes.Normalize(request.AgentGenerateFlowMode);
        return string.Equals(requestMode, AiAgentGenerateFlowModes.Planner, StringComparison.OrdinalIgnoreCase);
    }

    private ClearVision.Product.Core.Entities.RuntimePreviewPilotConfig ResolveRuntimePreviewPilotConfig()
    {
        try
        {
            var config = _configurationService?.GetCurrent().Runtime.RuntimePreviewPilot.CloneNormalized()
                ?? new ClearVision.Product.Core.Entities.RuntimePreviewPilotConfig();
            config.Normalize();
            return config;
        }
        catch
        {
            var fallback = new ClearVision.Product.Core.Entities.RuntimePreviewPilotConfig();
            fallback.Normalize();
            return fallback;
        }
    }

    private static AiFlowGenerationResult ControlledFailure(
        string errorMessage,
        IReadOnlyList<VisionAgentToolTrace> traces)
    {
        return new AiFlowGenerationResult
        {
            Success = false,
            CompletionStatus = AiFlowGenerationResult.CompletionStatusFailed,
            FailureType = AiFlowGenerationResult.FailureTypeSystemError,
            ErrorMessage = errorMessage,
            FailureSummary = new AiFailureSummary
            {
                Category = "vision_agent",
                Code = "controlled_agent_generate_flow_failed",
                Message = errorMessage,
                RepairTarget = "Retry with legacy GenerateFlow or inspect agent tool trace."
            },
            ToolTrace = traces.Select(MapTrace).ToList(),
            InteractionState = AiInteractionStates.Failed,
            TurnIntent = AiTurnIntents.NewFlow,
            RouterConfidence = AiRouterConfidence.Low
        };
    }

    private static OperatorFlowDto BuildFlowDto(
        JsonElement flowDraft,
        string description,
        out Dictionary<string, string> operatorIdMap)
    {
        var operators = ReadArray(flowDraft, "operators")
            .Select(ReadDraftOperator)
            .ToList();
        var connections = ReadArray(flowDraft, "connections")
            .Select(ReadDraftConnection)
            .ToList();
        var flow = new OperatorFlowDto
        {
            Id = Guid.NewGuid(),
            Name = $"Vision Agent Draft - {description}",
            Operators = new List<OperatorDto>(),
            Connections = new List<OperatorConnectionDto>()
        };
        var operatorIds = operators.ToDictionary(
            op => op.TempId,
            _ => Guid.NewGuid(),
            StringComparer.OrdinalIgnoreCase);
        var inputPorts = new Dictionary<string, Dictionary<string, Guid>>(StringComparer.OrdinalIgnoreCase);
        var outputPorts = new Dictionary<string, Dictionary<string, Guid>>(StringComparer.OrdinalIgnoreCase);

        var index = 0;
        foreach (var op in operators)
        {
            var schema = VisionAgentReadOnlyCatalog.Schemas.TryGetValue(op.OperatorType, out var foundSchema)
                ? foundSchema
                : null;
            var inputs = schema?.InputPorts.ToList() ?? new List<string>();
            var outputs = schema?.OutputPorts.ToList() ?? new List<string>();
            inputPorts[op.TempId] = inputs.ToDictionary(name => name, _ => Guid.NewGuid(), StringComparer.OrdinalIgnoreCase);
            outputPorts[op.TempId] = outputs.ToDictionary(name => name, _ => Guid.NewGuid(), StringComparer.OrdinalIgnoreCase);

            flow.Operators.Add(new OperatorDto
            {
                Id = operatorIds[op.TempId],
                Name = string.IsNullOrWhiteSpace(op.DisplayName) ? op.OperatorType : op.DisplayName,
                Type = ResolveOperatorType(op.OperatorType),
                X = 160 * index,
                Y = 0,
                IsEnabled = true,
                InputPorts = inputs.Select(name => new PortDto
                {
                    Id = inputPorts[op.TempId][name],
                    Name = name,
                    Direction = PortDirection.Input,
                    DataType = InferPortDataType(name),
                    IsRequired = true
                }).ToList(),
                OutputPorts = outputs.Select(name => new PortDto
                {
                    Id = outputPorts[op.TempId][name],
                    Name = name,
                    Direction = PortDirection.Output,
                    DataType = InferPortDataType(name)
                }).ToList(),
                Parameters = BuildParameters(op, schema)
            });
            index++;
        }

        foreach (var connection in connections)
        {
            if (!operatorIds.ContainsKey(connection.SourceTempId) ||
                !operatorIds.ContainsKey(connection.TargetTempId))
            {
                continue;
            }

            var sourcePortId = EnsurePort(
                outputPorts,
                flow,
                operatorIds,
                connection.SourceTempId,
                connection.SourcePortName,
                PortDirection.Output);
            var targetPortId = EnsurePort(
                inputPorts,
                flow,
                operatorIds,
                connection.TargetTempId,
                connection.TargetPortName,
                PortDirection.Input);
            flow.Connections.Add(new OperatorConnectionDto
            {
                Id = Guid.NewGuid(),
                SourceOperatorId = operatorIds[connection.SourceTempId],
                SourcePortId = sourcePortId,
                TargetOperatorId = operatorIds[connection.TargetTempId],
                TargetPortId = targetPortId
            });
        }

        operatorIdMap = operatorIds.ToDictionary(
            item => item.Key,
            item => item.Value.ToString(),
            StringComparer.OrdinalIgnoreCase);
        return flow;
    }

    private static List<ParameterDto> BuildParameters(
        DraftOperator op,
        OperatorSchemaItem? schema)
    {
        var parameters = new Dictionary<string, ParameterDto>(StringComparer.OrdinalIgnoreCase);
        if (schema != null)
        {
            foreach (var parameter in schema.Parameters)
            {
                parameters[parameter.Name] = new ParameterDto
                {
                    Id = Guid.NewGuid(),
                    Name = parameter.Name,
                    DisplayName = parameter.Name,
                    Description = parameter.Summary,
                    DataType = parameter.DataType,
                    IsRequired = parameter.Required,
                    Value = op.Parameters.TryGetValue(parameter.Name, out var value) ? value : null
                };
            }
        }

        foreach (var parameter in op.Parameters)
        {
            if (!parameters.ContainsKey(parameter.Key))
            {
                parameters[parameter.Key] = new ParameterDto
                {
                    Id = Guid.NewGuid(),
                    Name = parameter.Key,
                    DisplayName = parameter.Key,
                    DataType = "string",
                    Value = parameter.Value
                };
            }
            else
            {
                parameters[parameter.Key].Value = parameter.Value;
            }
        }

        return parameters.Values.ToList();
    }

    private static Guid EnsurePort(
        Dictionary<string, Dictionary<string, Guid>> portMap,
        OperatorFlowDto flow,
        IReadOnlyDictionary<string, Guid> operatorIds,
        string tempId,
        string portName,
        PortDirection direction)
    {
        if (!portMap.TryGetValue(tempId, out var ports))
        {
            ports = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
            portMap[tempId] = ports;
        }

        var normalizedPortName = string.IsNullOrWhiteSpace(portName) ? "Value" : portName;
        if (ports.TryGetValue(normalizedPortName, out var existing))
        {
            return existing;
        }

        var portId = Guid.NewGuid();
        ports[normalizedPortName] = portId;
        if (!operatorIds.TryGetValue(tempId, out var operatorId))
        {
            return portId;
        }

        var operatorDto = flow.Operators.FirstOrDefault(item => item.Id == operatorId);
        if (operatorDto == null)
        {
            return portId;
        }

        var port = new PortDto
        {
            Id = portId,
            Name = normalizedPortName,
            Direction = direction,
            DataType = InferPortDataType(normalizedPortName),
            IsRequired = direction == PortDirection.Input
        };
        if (direction == PortDirection.Input)
        {
            operatorDto.InputPorts.Add(port);
        }
        else
        {
            operatorDto.OutputPorts.Add(port);
        }

        return portId;
    }

    private static OperatorType ResolveOperatorType(string operatorType)
    {
        var mapped = operatorType switch
        {
            "MeasureDistance" => "Measurement",
            _ => operatorType
        };

        return Enum.TryParse<OperatorType>(mapped, ignoreCase: true, out var parsed)
            ? OperatorTypeAliasResolver.Resolve(parsed)
            : OperatorType.ResultJudgment;
    }

    private static PortDataType InferPortDataType(string portName)
    {
        if (portName.Contains("image", StringComparison.OrdinalIgnoreCase))
        {
            return PortDataType.Image;
        }

        if (portName.Contains("point", StringComparison.OrdinalIgnoreCase) ||
            portName.Contains("center", StringComparison.OrdinalIgnoreCase))
        {
            return PortDataType.Point;
        }

        if (portName.Contains("detection", StringComparison.OrdinalIgnoreCase))
        {
            return PortDataType.DetectionList;
        }

        if (portName.Contains("score", StringComparison.OrdinalIgnoreCase) ||
            portName.Contains("distance", StringComparison.OrdinalIgnoreCase) ||
            portName.Contains("radius", StringComparison.OrdinalIgnoreCase) ||
            portName.Contains("diameter", StringComparison.OrdinalIgnoreCase))
        {
            return PortDataType.Float;
        }

        return PortDataType.Any;
    }

    private static List<AiMissingResourceInfo> BuildMissingResources(JsonElement deploymentPrecheck)
    {
        return ReadArray(deploymentPrecheck, "missingResources")
            .Select(resource => new AiMissingResourceInfo
            {
                ResourceType = ReadString(resource, "resourceKind") ?? "resource",
                ResourceKey = $"{ReadString(resource, "tempId")}.{ReadString(resource, "parameterName")}",
                Description = ReadString(resource, "message") ?? resource.GetRawText()
            })
            .ToList();
    }

    private static List<AiPendingParameterInfo> BuildPendingParameters(
        JsonElement deploymentPrecheck,
        IReadOnlyDictionary<string, string> operatorIdMap)
    {
        return ReadArray(deploymentPrecheck, "missingResources")
            .GroupBy(resource => ReadString(resource, "tempId") ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .Where(group => !string.IsNullOrWhiteSpace(group.Key))
            .Select(group => new AiPendingParameterInfo
            {
                OperatorId = group.Key,
                ActualOperatorId = operatorIdMap.TryGetValue(group.Key, out var actualId) ? actualId : string.Empty,
                ParameterNames = group
                    .Select(resource => ReadString(resource, "parameterName") ?? string.Empty)
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList()
            })
            .ToList();
    }

    private static Dictionary<string, List<string>> BuildParametersNeedingReview(
        IEnumerable<AiPendingParameterInfo> pendingParameters)
    {
        return pendingParameters.ToDictionary(
            parameter => parameter.OperatorId,
            parameter => parameter.ParameterNames,
            StringComparer.OrdinalIgnoreCase);
    }

    private static object ClonePendingAction(VisionAgentPendingAction action)
    {
        return new
        {
            actionType = action.ActionType,
            title = action.Title,
            summary = action.Summary,
            payload = action.Payload,
            requiresUserConfirmation = action.RequiresUserConfirmation
        };
    }

    private static object MapTrace(VisionAgentToolTrace trace)
    {
        return new
        {
            toolName = trace.ToolName,
            success = trace.Success,
            errorCode = trace.ErrorCode,
            durationMs = trace.DurationMs,
            permission = trace.Permission,
            adapterName = trace.AdapterName,
            permissionDecision = trace.PermissionDecision,
            arguments = trace.Arguments,
            resultSummary = trace.ResultSummary
        };
    }

    private static DraftOperator ReadDraftOperator(JsonElement element)
    {
        var parameters = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        if (TryGetProperty(element, "parameters", out var parameterElement) &&
            parameterElement.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in parameterElement.EnumerateObject())
            {
                parameters[property.Name] = ReadScalar(property.Value);
            }
        }

        return new DraftOperator(
            ReadString(element, "tempId") ?? Guid.NewGuid().ToString("N"),
            ReadString(element, "operatorType") ?? "ResultJudgment",
            ReadString(element, "displayName") ?? ReadString(element, "operatorType") ?? "Operator",
            parameters);
    }

    private static DraftConnection ReadDraftConnection(JsonElement element)
    {
        return new DraftConnection(
            ReadString(element, "sourceTempId") ?? string.Empty,
            ReadString(element, "sourcePortName") ?? string.Empty,
            ReadString(element, "targetTempId") ?? string.Empty,
            ReadString(element, "targetPortName") ?? string.Empty);
    }

    private static IEnumerable<JsonElement> ReadArray(JsonElement root, string propertyName)
    {
        return root.ValueKind == JsonValueKind.Object &&
               TryGetProperty(root, propertyName, out var value) &&
               value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray()
            : [];
    }

    private static object? CloneJsonCompatible(JsonElement value)
    {
        return value.ValueKind == JsonValueKind.Undefined
            ? null
            : JsonSerializer.Deserialize<object>(value.GetRawText(), JsonOptions);
    }

    private static string? ReadString(JsonElement element, string propertyName)
    {
        return element.ValueKind == JsonValueKind.Object &&
               TryGetProperty(element, propertyName, out var value) &&
               value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static bool TryGetProperty(
        JsonElement element,
        string propertyName,
        out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }

    private static string? ReadScalar(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => value.GetRawText()
        };
    }

    private sealed record DraftOperator(
        string TempId,
        string OperatorType,
        string DisplayName,
        IReadOnlyDictionary<string, string?> Parameters);

    private sealed record DraftConnection(
        string SourceTempId,
        string SourcePortName,
        string TargetTempId,
        string TargetPortName);

    private sealed class AgentToolResultCapture
    {
        public JsonElement FlowDraft { get; set; }
        public JsonElement ValidationSummary { get; set; }
        public JsonElement DryRunSummary { get; set; }
        public JsonElement DeploymentPrecheck { get; set; }
        public JsonElement RuntimePreviewSummary { get; set; }
    }

    private sealed class AgentGenerateFlowPlanner
    {
        private readonly AiFlowGenerationRequest _request;
        private readonly AgentToolResultCapture _capture;
        private readonly IVisionAgentPlannerService _plannerService;
        private readonly VisionAgentProtocolParser _protocolParser;
        private readonly AgentWorkflowDraftEditor _draftEditor;

        public AgentGenerateFlowPlanner(
            AiFlowGenerationRequest request,
            AgentToolResultCapture capture,
            IVisionAgentPlannerService plannerService,
            VisionAgentProtocolParser protocolParser,
            AgentWorkflowDraftEditor draftEditor)
        {
            _request = request;
            _capture = capture;
            _plannerService = plannerService;
            _protocolParser = protocolParser;
            _draftEditor = draftEditor;
        }

        public async Task<string> CompleteAsync(
            IReadOnlyList<VisionAgentLoopMessage> messages,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CaptureLatestToolResults(messages);
            var completion = await _plannerService.CompleteAsync(new AgentPlannerCompletionRequest
            {
                GenerationRequest = _request,
                Messages = messages,
                FlowDraft = _capture.FlowDraft,
                ValidationSummary = _capture.ValidationSummary,
                DryRunSummary = _capture.DryRunSummary,
                DeploymentPrecheck = _capture.DeploymentPrecheck
            }, cancellationToken);

            CaptureOutgoingFlowDraft(completion);
            CaptureFinalDraft(completion);
            return completion;
        }

        private void CaptureLatestToolResults(IReadOnlyList<VisionAgentLoopMessage> messages)
        {
            var latest = messages.LastOrDefault(message =>
                string.Equals(message.Role, "user", StringComparison.OrdinalIgnoreCase) &&
                message.Content.Contains("\"tool_result\"", StringComparison.OrdinalIgnoreCase));
            if (latest == null)
            {
                return;
            }

            using var doc = JsonDocument.Parse(latest.Content);
            if (!TryGetProperty(doc.RootElement, "toolResults", out var results) ||
                results.ValueKind != JsonValueKind.Array)
            {
                return;
            }

            foreach (var result in results.EnumerateArray())
            {
                var name = ReadString(result, "name");
                if (string.IsNullOrWhiteSpace(name) ||
                    !TryGetProperty(result, "data", out var data))
                {
                    continue;
                }

                switch (name)
                {
                    case "get_flow_template_skeleton":
                        CaptureFlowDraft(data);
                        break;
                    case "validate_flow":
                        _capture.ValidationSummary = data.Clone();
                        break;
                    case "dryrun_flow":
                        _capture.DryRunSummary = data.Clone();
                        break;
                    case "runtime_package_precheck":
                        _capture.DeploymentPrecheck = data.Clone();
                        break;
                    case var preview when RuntimePreviewPermissionGate.IsRuntimePreviewTool(preview):
                        _capture.RuntimePreviewSummary = data.Clone();
                        break;
                }
            }
        }

        private void CaptureOutgoingFlowDraft(string completion)
        {
            var parsed = _protocolParser.Parse(completion);
            if (!parsed.IsToolCall)
            {
                return;
            }

            foreach (var call in parsed.ToolCalls)
            {
                if (!string.Equals(call.Name, "validate_flow", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(call.Name, "dryrun_flow", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(call.Name, "runtime_package_precheck", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (TryReadFlowArgument(call.Arguments, out var flow))
                {
                    _capture.FlowDraft = flow;
                }
            }
        }

        private void CaptureFinalDraft(string completion)
        {
            if (_draftEditor.TryApplyFinalContent(completion, _capture.FlowDraft, out var editedDraft))
            {
                _capture.FlowDraft = editedDraft;
            }
        }

        private void CaptureFlowDraft(JsonElement data)
        {
            if (data.ValueKind == JsonValueKind.Object &&
                TryGetProperty(data, "truncated", out var truncated) &&
                truncated.ValueKind == JsonValueKind.True)
            {
                return;
            }

            if (data.ValueKind == JsonValueKind.Object)
            {
                _capture.FlowDraft = data.Clone();
            }
        }

        private static bool TryReadFlowArgument(JsonElement arguments, out JsonElement flow)
        {
            flow = default;
            if (TryGetProperty(arguments, "flow", out var flowElement))
            {
                if (flowElement.ValueKind == JsonValueKind.Object)
                {
                    flow = flowElement.Clone();
                    return true;
                }

                if (flowElement.ValueKind == JsonValueKind.String &&
                    TryParseFlowJson(flowElement.GetString(), out flow))
                {
                    return true;
                }
            }

            if (TryGetProperty(arguments, "flowJson", out var flowJson) &&
                flowJson.ValueKind == JsonValueKind.String &&
                TryParseFlowJson(flowJson.GetString(), out flow))
            {
                return true;
            }

            return false;
        }

        private static bool TryParseFlowJson(string? value, out JsonElement flow)
        {
            flow = default;
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            try
            {
                using var doc = JsonDocument.Parse(value);
                if (doc.RootElement.ValueKind != JsonValueKind.Object)
                {
                    return false;
                }

                flow = doc.RootElement.Clone();
                return true;
            }
            catch (JsonException)
            {
                return false;
            }
        }
    }

    private sealed class AgentGenerateFlowScript
    {
        private readonly AiFlowGenerationRequest _request;
        private readonly AgentToolResultCapture _capture;
        private readonly bool _useExistingFlow;
        private int _step;
        private string _templateId = "template_matching_alignment";

        public AgentGenerateFlowScript(
            AiFlowGenerationRequest request,
            AgentToolResultCapture capture)
        {
            _request = request;
            _capture = capture;
            if (!string.IsNullOrWhiteSpace(request.ExistingFlowJson))
            {
                using var doc = JsonDocument.Parse(request.ExistingFlowJson);
                _capture.FlowDraft = doc.RootElement.Clone();
                _useExistingFlow = true;
            }
        }

        public Task<string> CompleteAsync(
            IReadOnlyList<VisionAgentLoopMessage> messages,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CaptureLatestToolResults(messages);
            var response = _useExistingFlow
                ? NextExistingFlowStep()
                : NextTemplateStep();

            return Task.FromResult(response);
        }

        private string NextExistingFlowStep()
        {
            return _step++ switch
            {
                0 => ReadOnlyDiscoveryCalls(),
                1 => ToolCall("validate_flow", new { flow = CloneJsonCompatible(_capture.FlowDraft) }),
                2 => ToolCall("dryrun_flow", new { flow = CloneJsonCompatible(_capture.FlowDraft) }),
                3 => PrecheckCall(),
                _ => "Controlled Vision Agent GenerateFlow completed."
            };
        }

        private string NextTemplateStep()
        {
            return _step++ switch
            {
                0 => ReadOnlyDiscoveryCalls(),
                1 => ToolCall("get_flow_template_skeleton", new { templateId = _templateId }),
                2 => ToolCall("validate_flow", new { flow = CloneJsonCompatible(_capture.FlowDraft) }),
                3 => ToolCall("dryrun_flow", new { flow = CloneJsonCompatible(_capture.FlowDraft) }),
                4 => PrecheckCall(),
                _ => "Controlled Vision Agent GenerateFlow completed."
            };
        }

        private string ReadOnlyDiscoveryCalls()
        {
            return ToolCalls(
                ("list_operator_catalog", new { keyword = _request.Description }),
                ("get_operator_schema", new { operatorType = "ImageAcquisition" }),
                ("match_flow_template", new { request = _request.Description }),
                ("inspect_current_flow", new { existingFlowJson = _request.ExistingFlowJson }));
        }

        private string PrecheckCall()
        {
            return ToolCall("runtime_package_precheck", new
            {
                flow = CloneJsonCompatible(_capture.FlowDraft),
                validationSummary = CloneJsonCompatible(_capture.ValidationSummary),
                dryRunSummary = CloneJsonCompatible(_capture.DryRunSummary)
            });
        }

        private void CaptureLatestToolResults(IReadOnlyList<VisionAgentLoopMessage> messages)
        {
            var latest = messages.LastOrDefault(message =>
                string.Equals(message.Role, "user", StringComparison.OrdinalIgnoreCase) &&
                message.Content.Contains("\"tool_result\"", StringComparison.OrdinalIgnoreCase));
            if (latest == null)
            {
                return;
            }

            using var doc = JsonDocument.Parse(latest.Content);
            if (!TryGetProperty(doc.RootElement, "toolResults", out var results) ||
                results.ValueKind != JsonValueKind.Array)
            {
                return;
            }

            foreach (var result in results.EnumerateArray())
            {
                var name = ReadString(result, "name");
                if (string.IsNullOrWhiteSpace(name) ||
                    !TryGetProperty(result, "data", out var data))
                {
                    continue;
                }

                switch (name)
                {
                    case "match_flow_template":
                        _templateId = ReadFirstTemplateId(data) ?? _templateId;
                        break;
                    case "get_flow_template_skeleton":
                        _capture.FlowDraft = data.Clone();
                        break;
                    case "validate_flow":
                        _capture.ValidationSummary = data.Clone();
                        break;
                    case "dryrun_flow":
                        _capture.DryRunSummary = data.Clone();
                        break;
                    case "runtime_package_precheck":
                        _capture.DeploymentPrecheck = data.Clone();
                        break;
                }
            }
        }

        private static string? ReadFirstTemplateId(JsonElement data)
        {
            var candidates = ReadArray(data, "candidates").ToList();
            return candidates.Count == 0
                ? null
                : ReadString(candidates[0], "templateId");
        }

        private static string ToolCall(string name, object? arguments = null)
        {
            return ToolCalls((name, arguments ?? new { }));
        }

        private static string ToolCalls(params (string Name, object Arguments)[] calls)
        {
            return JsonSerializer.Serialize(new
            {
                kind = "tool_call",
                toolCalls = calls.Select((call, index) => new
                {
                    id = $"call_{index + 1}",
                    name = call.Name,
                    arguments = call.Arguments
                })
            }, JsonOptions);
        }
    }
}
