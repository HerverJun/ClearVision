using System.Text.Json;
using ClearVision.Product.Core.AI.Tools;
using ClearVision.Product.Core.DTOs;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Services;
using ClearVision.Product.Core.Streaming;

namespace ClearVision.Product.Infrastructure.AI.Tools;

public sealed class ReplayFlowWithFrameTool : VisionAgentToolBase
{
    private readonly IVisionAgentTemporaryFrameStore _frameStore;
    private readonly IOperatorFactory _operatorFactory;
    private readonly IFlowExecutionService _flowExecutionService;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new FlexibleStringDictionaryJsonConverter() }
    };

    public ReplayFlowWithFrameTool(
        IVisionAgentTemporaryFrameStore frameStore,
        IOperatorFactory operatorFactory,
        IFlowExecutionService flowExecutionService)
    {
        _frameStore = frameStore;
        _operatorFactory = operatorFactory;
        _flowExecutionService = flowExecutionService;
    }

    public override string Name => "replay_flow_with_frame";
    public override string DisplayName => "Replay flow with frame";
    public override string Description => "Consumes a temporaryFrameId from capture_test_frame and executes the flow with the cached real frame. This is distinct from structure-only dryrun_flow.";
    public override string Category => "flow";
    public override VisionAgentToolPermission Permission => VisionAgentToolPermission.RuntimePreview;
    public override JsonElement ParametersSchema { get; } = Schema("""
        {
          "type": "object",
          "required": ["temporaryFrameId", "flow"],
          "properties": {
            "temporaryFrameId": { "type": "string" },
            "flow": { "type": "object" },
            "entryOperatorTempId": { "type": "string" },
            "expectedOutputs": { "type": "object" }
          }
        }
        """);

    public override async Task<VisionAgentToolResult> ExecuteAsync(
        VisionAgentToolContext context,
        JsonElement arguments,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var temporaryFrameId = ReadString(arguments, "temporaryFrameId");
        if (string.IsNullOrWhiteSpace(temporaryFrameId))
        {
            return VisionAgentToolResult.Fail(
                "temporary_frame_required",
                "temporaryFrameId is required. Call capture_test_frame first.");
        }

        if (!_frameStore.TryGet(temporaryFrameId, out var frame))
        {
            return VisionAgentToolResult.Fail(
                "temporary_frame_not_found",
                "The temporary frame was not found or has expired. Capture a new test frame.");
        }

        var flowElement = ReadObjectOrSelf(arguments, "flow");
        AiGeneratedFlowJson? flow;
        try
        {
            flow = JsonSerializer.Deserialize<AiGeneratedFlowJson>(flowElement.GetRawText(), JsonOptions);
        }
        catch (JsonException ex)
        {
            return VisionAgentToolResult.Fail("invalid_flow_json", ex.Message, new
            {
                frame = BuildFrameSummary(frame)
            });
        }

        if (flow == null)
        {
            return VisionAgentToolResult.Fail("invalid_flow_json", "Flow payload is empty.", new
            {
                frame = BuildFrameSummary(frame)
            });
        }

        try
        {
            var selectedEntry = ResolveEntryOperator(flow, ReadString(arguments, "entryOperatorTempId"));
            if (!selectedEntry.Success)
            {
                return selectedEntry.Result!;
            }

            var entity = VisionAgentFlowConverter.ToEntity(flow, _operatorFactory, out var operatorIdByTempId);
            if (!operatorIdByTempId.TryGetValue(selectedEntry.EntryOperatorTempId!, out var entryOperatorId))
            {
                return VisionAgentToolResult.Fail(
                    "entry_operator_mapping_failed",
                    $"Unable to map entryOperatorTempId '{selectedEntry.EntryOperatorTempId}' to runtime operator id.",
                    new { frame = BuildFrameSummary(frame) });
            }

            var validation = _flowExecutionService.ValidateFlow(entity);
            if (!validation.IsValid)
            {
                return VisionAgentToolResult.Ok(new
                {
                    replayExecuted = false,
                    replayKind = "real_frame_runtime_execution",
                    valid = false,
                    isValid = false,
                    usedEntryOperatorTempId = selectedEntry.EntryOperatorTempId,
                    frame = BuildFrameSummary(frame),
                    blockingIssues = validation.Errors,
                    warnings = validation.Warnings
                });
            }

            var envelope = BuildFrameEnvelope(frame);
            var inputs = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
            {
                [$"ProvidedFrameEnvelope:{entryOperatorId}"] = envelope
            };
            var result = await _flowExecutionService.ExecuteFlowAsync(
                entity,
                inputs,
                enableParallel: false,
                cancellationToken);

            if (_frameStore.GetStats().RemoveAfterReplay)
            {
                _frameStore.Remove(frame.TemporaryFrameId);
            }

            return VisionAgentToolResult.Ok(new
            {
                replayExecuted = true,
                replayKind = "real_frame_runtime_execution",
                valid = true,
                isValid = true,
                usedEntryOperatorTempId = selectedEntry.EntryOperatorTempId,
                usedRuntimeOperatorId = entryOperatorId,
                result.IsSuccess,
                replaySucceeded = result.IsSuccess,
                durationMs = result.ExecutionTimeMs,
                result.ErrorMessage,
                frame = BuildFrameSummary(frame),
                outputSummary = SummarizeOutputs(result.OutputData),
                scoreSummary = SummarizeScores(result),
                operatorResults = result.OperatorResults.Select(item => new
                {
                    item.OperatorId,
                    item.OperatorName,
                    item.IsSuccess,
                    durationMs = item.ExecutionTimeMs,
                    item.ErrorMessage,
                    outputSummary = SummarizeOutputs(item.OutputData),
                    scores = ExtractScoreFields(item.OutputData)
                }).ToList(),
                warnings = result.IsSuccess || string.IsNullOrWhiteSpace(result.ErrorMessage)
                    ? Array.Empty<string>()
                    : new[] { result.ErrorMessage }
            });
        }
        catch (Exception ex)
        {
            return VisionAgentToolResult.Fail("replay_flow_failed", ex.Message, new
            {
                frame = BuildFrameSummary(frame)
            });
        }
    }

    private static object BuildFrameSummary(VisionAgentTemporaryFrame frame)
    {
        return new
        {
            frame.TemporaryFrameId,
            byteLength = frame.Bytes.Length,
            frame.Metadata.CameraBindingId,
            frame.Metadata.CameraId,
            frame.Metadata.CameraName,
            frame.Metadata.Width,
            frame.Metadata.Height,
            frame.Metadata.PixelFormat,
            frame.Metadata.CapturedAtUtc,
            frame.ExpiresAtUtc
        };
    }

    private static EntrySelection ResolveEntryOperator(AiGeneratedFlowJson flow, string? requestedTempId)
    {
        var acquisitionOperators = flow.Operators
            .Where(op => string.Equals(op.OperatorType, nameof(OperatorType.ImageAcquisition), StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (!string.IsNullOrWhiteSpace(requestedTempId))
        {
            var selected = acquisitionOperators.FirstOrDefault(op =>
                string.Equals(op.TempId, requestedTempId.Trim(), StringComparison.OrdinalIgnoreCase));
            if (selected == null)
            {
                return EntrySelection.Fail(VisionAgentToolResult.Fail(
                    "entry_operator_temp_id_not_found",
                    $"entryOperatorTempId '{requestedTempId}' does not reference an ImageAcquisition operator.",
                    new
                    {
                        requestedEntryOperatorTempId = requestedTempId,
                        imageAcquisitionTempIds = acquisitionOperators.Select(op => op.TempId).ToList()
                    }));
            }

            return EntrySelection.Ok(selected.TempId);
        }

        if (acquisitionOperators.Count == 1)
        {
            return EntrySelection.Ok(acquisitionOperators[0].TempId);
        }

        if (acquisitionOperators.Count == 0)
        {
            return EntrySelection.Fail(VisionAgentToolResult.Fail(
                "no_image_acquisition_operator",
                "replay_flow_with_frame requires an ImageAcquisition operator to receive the temporary frame."));
        }

        return EntrySelection.Fail(VisionAgentToolResult.Fail(
            "multiple_image_acquisition_requires_entry_operator_temp_id",
            "Flow contains multiple ImageAcquisition operators. Provide entryOperatorTempId to select the exact entry.",
            new
            {
                imageAcquisitionTempIds = acquisitionOperators.Select(op => op.TempId).ToList()
            }));
    }

    private static FrameEnvelope BuildFrameEnvelope(VisionAgentTemporaryFrame frame)
    {
        return new FrameEnvelope(
            string.IsNullOrWhiteSpace(frame.Metadata.CameraId) ? frame.Metadata.CameraBindingId : frame.Metadata.CameraId,
            Sequence: 0,
            HostReceiveTimestampUtc: frame.Metadata.CapturedAtUtc,
            Width: frame.Metadata.Width ?? 0,
            Height: frame.Metadata.Height ?? 0,
            PixelFormat: string.IsNullOrWhiteSpace(frame.Metadata.PixelFormat) ? "EncodedImage" : frame.Metadata.PixelFormat!,
            PayloadKind: FramePayloadKind.EncodedImage,
            Payload: frame.Bytes,
            Tags: new Dictionary<string, string>
            {
                ["TemporaryFrameId"] = frame.TemporaryFrameId,
                ["CameraBindingId"] = frame.Metadata.CameraBindingId
            });
    }

    private static Dictionary<string, object?> SummarizeOutputs(Dictionary<string, object>? outputData)
    {
        var summary = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        if (outputData == null || outputData.Count == 0)
        {
            return summary;
        }

        foreach (var (key, value) in outputData.Take(24))
        {
            summary[key] = SummarizeValue(value);
        }

        if (outputData.Count > summary.Count)
        {
            summary["__truncatedKeyCount"] = outputData.Count - summary.Count;
        }

        return summary;
    }

    private static Dictionary<string, object?> SummarizeScores(FlowExecutionResult result)
    {
        var scores = ExtractScoreFields(result.OutputData);
        foreach (var op in result.OperatorResults)
        {
            foreach (var pair in ExtractScoreFields(op.OutputData))
            {
                scores.TryAdd($"{op.OperatorName}.{pair.Key}", pair.Value);
            }
        }

        return scores;
    }

    private static Dictionary<string, object?> ExtractScoreFields(Dictionary<string, object>? outputData)
    {
        var summary = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        if (outputData == null)
        {
            return summary;
        }

        foreach (var key in new[] { "Score", "Confidence", "MatchScore" })
        {
            if (outputData.TryGetValue(key, out var value))
            {
                summary[key] = SummarizeValue(value);
            }
        }

        return summary;
    }

    private static object? SummarizeValue(object? value)
    {
        if (value == null)
        {
            return null;
        }

        if (value is string or bool or byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal)
        {
            return value;
        }

        if (value is byte[] bytes)
        {
            return new { type = "byte[]", byteLength = bytes.Length };
        }

        if (value is System.Collections.IDictionary dictionary)
        {
            return new
            {
                type = value.GetType().Name,
                count = dictionary.Count,
                keys = dictionary.Keys.Cast<object>().Select(key => key?.ToString()).Where(key => !string.IsNullOrWhiteSpace(key)).Take(12).ToList()
            };
        }

        if (value is System.Collections.ICollection collection)
        {
            return new
            {
                type = value.GetType().Name,
                count = collection.Count
            };
        }

        return new
        {
            type = value.GetType().Name
        };
    }

    private sealed record EntrySelection(
        bool Success,
        string? EntryOperatorTempId,
        VisionAgentToolResult? Result)
    {
        public static EntrySelection Ok(string tempId) => new(true, tempId, null);
        public static EntrySelection Fail(VisionAgentToolResult result) => new(false, null, result);
    }
}
