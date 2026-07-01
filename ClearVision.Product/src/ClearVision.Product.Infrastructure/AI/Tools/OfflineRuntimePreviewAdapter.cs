using System.Text.Json;
using ClearVision.Product.Core.AI.Tools;
using ClearVision.Product.Infrastructure.AI.Agent;

namespace ClearVision.Product.Infrastructure.AI.Tools;

public sealed class OfflineRuntimePreviewAdapter : IRuntimePreviewAdapter
{
    public const string AdapterName = "offline_runtime_preview";

    private readonly RuntimePreviewArtifactStore _artifactStore;

    public OfflineRuntimePreviewAdapter(RuntimePreviewArtifactStore artifactStore)
    {
        _artifactStore = artifactStore;
    }

    public string Name => AdapterName;

    public IReadOnlySet<string> SupportedToolNames { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            RuntimePreviewPermissionGate.CaptureToolName,
            RuntimePreviewPermissionGate.ReplayToolName
        };

    public Task<RuntimePreviewResult> ExecuteAsync(
        RuntimePreviewRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.Equals(request.ToolName, RuntimePreviewPermissionGate.CaptureToolName, StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(Capture(request));
        }

        if (string.Equals(request.ToolName, RuntimePreviewPermissionGate.ReplayToolName, StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(Replay(request));
        }

        return Task.FromResult(RuntimePreviewResult.Fail(
            Name,
            "runtime_preview_tool_not_supported",
            $"Offline RuntimePreview adapter does not support tool '{request.ToolName}'."));
    }

    private RuntimePreviewResult Capture(RuntimePreviewRequest request)
    {
        var cameraBindingId = ReadString(request.Arguments, "cameraBindingId") ?? "<pending-camera-binding>";
        var operatorTempId =
            ReadString(request.Arguments, "operatorTempId") ??
            ReadString(request.Arguments, "entryOperatorTempId") ??
            "op_cam";
        var frameId = ReadString(request.Arguments, "frameId") ??
                      $"offline-frame-{RuntimePreviewArtifactStore.StableSuffix(cameraBindingId, operatorTempId)}";
        var artifacts = new[]
        {
            _artifactStore.CreateFrameMetadata(
                RuntimePreviewPermissionGate.CaptureToolName,
                frameId,
                operatorTempId,
                cameraBindingId)
        };

        return new RuntimePreviewResult
        {
            Success = true,
            AdapterName = Name,
            PreviewMode = RuntimePreviewModes.OfflineFixture,
            PreviewReady = true,
            FrameSource = "offline_fixture_metadata",
            FrameId = frameId,
            PermissionDecision = OfflinePermissionDecision(request),
            ResourceTrace = OfflineResourceTrace("capture", cameraBindingId),
            Fallback = RuntimePreviewFallbackInfo.NotUsed(),
            Warnings = new object[]
            {
                new
                {
                    code = "offline_capture_metadata_only",
                    message = "Offline RuntimePreview returned deterministic frame metadata only."
                }
            },
            Artifacts = artifacts,
            BinaryIncluded = false,
            CapturedRealFrame = false,
            LoadedModelFiles = false,
            AccessedHardware = false,
            StationTouched = false
        };
    }

    private RuntimePreviewResult Replay(RuntimePreviewRequest request)
    {
        var frameId = ReadString(request.Arguments, "frameId") ?? "offline-frame";
        var normalized = VisionAgentFlowDraftNormalizer.Normalize(request.Arguments, request.Context);
        if (!normalized.Success)
        {
            return RuntimePreviewResult.Fail(
                Name,
                normalized.ErrorCode ?? "invalid_flow",
                normalized.ErrorMessage ?? "Flow draft could not be normalized.");
        }

        var validation = VisionAgentFlowDraftValidator.Validate(normalized.Flow);
        var executedOperators = new List<object>();
        var skippedOperators = new List<object>();
        var artifacts = new List<RuntimePreviewArtifactSummary>();

        if (validation.BlockingIssues.Count > 0)
        {
            skippedOperators.AddRange(validation.Flow.Operators.Select(op => new
            {
                tempId = op.TempId,
                operatorType = op.OperatorType,
                reason = "validation_blocked"
            }));
        }
        else
        {
            var executionOrder = BuildExecutionOrder(validation.Flow, out var skippedByOrder);
            var index = 0;
            foreach (var op in executionOrder)
            {
                var status = SimulatedStatus(op.OperatorType);
                executedOperators.Add(new
                {
                    tempId = op.TempId,
                    operatorType = op.OperatorType,
                    status,
                    previewMode = RuntimePreviewModes.OfflineFixture
                });
                artifacts.Add(_artifactStore.CreateOperatorResultMetadata(
                    RuntimePreviewPermissionGate.ReplayToolName,
                    frameId,
                    op.TempId,
                    op.OperatorType,
                    status,
                    index++));
            }

            skippedOperators.AddRange(skippedByOrder.Select(op => new
            {
                tempId = op.TempId,
                operatorType = op.OperatorType,
                reason = "not_reachable_or_cycle"
            }));
        }

        artifacts.Add(_artifactStore.CreateReplaySummaryMetadata(
            RuntimePreviewPermissionGate.ReplayToolName,
            frameId,
            executedOperators.Count,
            skippedOperators.Count,
            validation.BlockingIssues.Count));

        var blockingIssues = validation.BlockingIssues.Select(FlowValidationPayload.IssuePayload).ToList();
        var warnings = validation.Warnings.Select(FlowValidationPayload.IssuePayload).ToList();
        warnings.Add(new
        {
            code = "offline_replay_metadata_only",
            message = "Offline RuntimePreview replay used structural metadata only and did not execute vision algorithms.",
            tempId = (string?)null,
            operatorType = (string?)null
        });
        var missingResources = validation.MissingResources.Select(FlowValidationPayload.ResourcePayload).ToList();
        var previewReady = blockingIssues.Count == 0;

        return new RuntimePreviewResult
        {
            Success = previewReady,
            AdapterName = Name,
            PreviewMode = RuntimePreviewModes.OfflineFixture,
            PreviewReady = previewReady,
            WorkflowDraftAllowed = true,
            FrameSource = "offline_fixture_metadata",
            FrameId = frameId,
            PermissionDecision = OfflinePermissionDecision(request),
            ResourceTrace = OfflineResourceTrace("replay", frameId),
            Fallback = RuntimePreviewFallbackInfo.NotUsed(),
            ReplaySummary = new
            {
                replaySucceeded = previewReady,
                adapterName = Name,
                previewMode = RuntimePreviewModes.OfflineFixture,
                frameId,
                executedOperators,
                skippedOperators,
                generatedRealImages = false,
                loadedModelFiles = false,
                accessedHardware = false,
                stationTouched = false
            },
            Warnings = warnings,
            BlockingIssues = blockingIssues,
            MissingResources = missingResources,
            Artifacts = artifacts,
            BinaryIncluded = false,
            CapturedRealFrame = false,
            LoadedModelFiles = false,
            AccessedHardware = false,
            StationTouched = false,
            ErrorCode = previewReady ? null : "runtime_preview_replay_not_ready",
            ErrorMessage = previewReady ? null : "Offline replay stopped because structural validation produced blocking issues."
        };
    }

    private static RuntimePreviewPermissionDecision OfflinePermissionDecision(RuntimePreviewRequest request)
    {
        return new RuntimePreviewPermissionDecision
        {
            Allowed = true,
            ReasonCode = request.Context.RuntimePreviewPilot.Enabled
                ? "runtime_preview_pilot_fallback_offline"
                : "runtime_preview_offline_metadata_only",
            Reason = "Offline RuntimePreview returns deterministic metadata only.",
            RuntimePreviewConsent = request.Context.RuntimePreviewConsent,
            PilotEnabled = request.Context.RuntimePreviewPilot.Enabled,
            MetadataOnly = true,
            RequestedAdapterName = request.RequestedAdapterName ?? request.AdapterName,
            EffectiveAdapterName = AdapterName,
            AllowlistCounts = new
            {
                camera = request.Context.RuntimePreviewPilot.AllowedCameraBindingIds.Count,
                model = request.Context.RuntimePreviewPilot.AllowedModelIds.Count,
                template = request.Context.RuntimePreviewPilot.AllowedTemplateIds.Count,
                flow = request.Context.RuntimePreviewPilot.AllowedFlowIds.Count,
                resourceRoot = request.Context.RuntimePreviewPilot.AllowedResourceRoots.Count
            }
        };
    }

    private static RuntimePreviewResourceTrace OfflineResourceTrace(string operation, string? resourceId)
    {
        return new RuntimePreviewResourceTrace
        {
            Allowed = true,
            ReasonCode = "runtime_preview_offline_metadata_only",
            ResourceType = operation,
            ResourceId = RuntimePreviewArtifactStore.StableSuffix(resourceId ?? operation),
            NormalizedKey = "offline_metadata_only",
            Trace =
            [
                new
                {
                    resourceType = operation,
                    resourceId = "offline_metadata",
                    reasonCode = "runtime_preview_offline_metadata_only",
                    allowed = true
                }
            ]
        };
    }

    private static IReadOnlyList<VisionAgentFlowOperator> BuildExecutionOrder(
        VisionAgentFlowDraft flow,
        out IReadOnlyList<VisionAgentFlowOperator> skippedOperators)
    {
        var operators = flow.Operators
            .Where(op => !string.IsNullOrWhiteSpace(op.TempId))
            .GroupBy(op => op.TempId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
        var operatorsById = operators.ToDictionary(op => op.TempId, StringComparer.OrdinalIgnoreCase);
        var connections = flow.Connections
            .Where(connection =>
                operatorsById.ContainsKey(connection.SourceTempId) &&
                operatorsById.ContainsKey(connection.TargetTempId))
            .ToList();
        var outgoing = operators.ToDictionary(
            op => op.TempId,
            _ => new List<string>(),
            StringComparer.OrdinalIgnoreCase);
        var indegree = operators.ToDictionary(
            op => op.TempId,
            _ => 0,
            StringComparer.OrdinalIgnoreCase);

        foreach (var connection in connections)
        {
            outgoing[connection.SourceTempId].Add(connection.TargetTempId);
            indegree[connection.TargetTempId]++;
        }

        var ready = new Queue<string>(InitialReadyOperators(flow, operators, indegree));
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ordered = new List<VisionAgentFlowOperator>();

        while (ready.Count > 0)
        {
            var tempId = ready.Dequeue();
            if (!visited.Add(tempId))
            {
                continue;
            }

            ordered.Add(operatorsById[tempId]);
            foreach (var targetTempId in outgoing[tempId])
            {
                indegree[targetTempId]--;
                if (indegree[targetTempId] == 0)
                {
                    ready.Enqueue(targetTempId);
                }
            }
        }

        skippedOperators = operators
            .Where(op => !visited.Contains(op.TempId))
            .ToList();
        return ordered;
    }

    private static IEnumerable<string> InitialReadyOperators(
        VisionAgentFlowDraft flow,
        IReadOnlyList<VisionAgentFlowOperator> operators,
        IReadOnlyDictionary<string, int> indegree)
    {
        if (!string.IsNullOrWhiteSpace(flow.EntryOperatorTempId) &&
            indegree.ContainsKey(flow.EntryOperatorTempId))
        {
            yield return flow.EntryOperatorTempId;
            yield break;
        }

        foreach (var op in operators.Where(op => indegree[op.TempId] == 0))
        {
            yield return op.TempId;
        }
    }

    private static string SimulatedStatus(string operatorType)
    {
        return operatorType switch
        {
            "ImageAcquisition" => "simulated_offline_frame",
            "TemplateMatching" => "simulated_offline_template_match",
            "DeepLearning" => "simulated_offline_model_inference",
            "CircleMeasurement" => "simulated_offline_measurement",
            "MeasureDistance" => "simulated_offline_measurement",
            "ResultOutput" => "simulated_offline_output",
            _ => "simulated_offline_operator"
        };
    }

    private static string? ReadString(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase) &&
                property.Value.ValueKind == JsonValueKind.String)
            {
                return property.Value.GetString();
            }
        }

        return null;
    }
}
