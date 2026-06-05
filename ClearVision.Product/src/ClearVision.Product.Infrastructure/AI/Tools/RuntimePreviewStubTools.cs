using System.Text.Json;
using ClearVision.Product.Core.AI.Tools;
using ClearVision.Product.Infrastructure.AI.Agent;

namespace ClearVision.Product.Infrastructure.AI.Tools;

public sealed class RuntimePreviewCaptureStubTool : VisionAgentToolBase
{
    public override string Name => RuntimePreviewPermissionGate.CaptureToolName;
    public override string DisplayName => "Capture test frame stub";
    public override string Description => "Returns structure-only test-frame metadata without touching cameras or image files.";
    public override string Category => "runtime_preview";
    public override VisionAgentToolPermission Permission => VisionAgentToolPermission.RuntimePreview;
    public override JsonElement ParametersSchema { get; } = Schema("""
        {
          "type": "object",
          "properties": {
            "cameraBindingId": { "type": "string" },
            "operatorTempId": { "type": "string" },
            "reason": { "type": "string" }
          }
        }
        """);

    public override Task<VisionAgentToolResult> ExecuteAsync(
        VisionAgentToolContext context,
        JsonElement arguments,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var cameraBindingId = ReadString(arguments, "cameraBindingId") ?? "<pending-camera-binding>";
        var operatorTempId = ReadString(arguments, "operatorTempId") ?? ReadString(arguments, "entryOperatorTempId") ?? "op_cam";
        var frameId = $"stub-frame-{StableSuffix(cameraBindingId, operatorTempId)}";
        var artifacts = new[]
        {
            new RuntimePreviewArtifactSummary
            {
                ArtifactId = frameId,
                ArtifactType = "frame_metadata",
                SourceTool = Name,
                MetadataOnly = true,
                BinaryIncluded = false,
                ByteLength = 0,
                Metadata = new
                {
                    cameraBindingId,
                    operatorTempId,
                    frameSource = "runtime_preview_stub"
                }
            }
        };
        var data = new
        {
            source = "runtime_preview_static_stub",
            previewReady = true,
            frameSource = "stub",
            frameId,
            cameraBindingId,
            operatorTempId,
            warnings = new[]
            {
                new
                {
                    code = "stub_capture_only",
                    message = "RuntimePreview capture returned metadata only; no camera or image file was accessed."
                }
            },
            blockingIssues = Array.Empty<object>(),
            artifacts,
            capturedRealFrame = false,
            binaryIncluded = false
        };

        return Task.FromResult(VisionAgentToolResult.Ok(data));
    }

    private static string StableSuffix(params string[] values)
    {
        var hash = string.Join("|", values).GetHashCode(StringComparison.OrdinalIgnoreCase);
        return Math.Abs(hash).ToString("x");
    }
}

public sealed class RuntimePreviewReplayStubTool : VisionAgentToolBase
{
    public override string Name => RuntimePreviewPermissionGate.ReplayToolName;
    public override string DisplayName => "Replay flow with frame stub";
    public override string Description => "Returns structure-only replay metadata without executing vision logic or loading models.";
    public override string Category => "runtime_preview";
    public override VisionAgentToolPermission Permission => VisionAgentToolPermission.RuntimePreview;
    public override JsonElement ParametersSchema { get; } = Schema("""
        {
          "type": "object",
          "properties": {
            "frameId": { "type": "string" },
            "flow": { "type": ["object", "string"] },
            "flowJson": { "type": "string" },
            "entryOperatorTempId": { "type": "string" }
          }
        }
        """);

    public override Task<VisionAgentToolResult> ExecuteAsync(
        VisionAgentToolContext context,
        JsonElement arguments,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var frameId = ReadString(arguments, "frameId") ?? "stub-frame";
        var normalized = VisionAgentFlowDraftNormalizer.Normalize(arguments, context);
        var executedOperators = normalized.Success
            ? normalized.Flow.Operators.Select(op => new
            {
                tempId = op.TempId,
                operatorType = op.OperatorType,
                status = "stub_replayed"
            }).ToList<object>()
            : [];
        var blockingIssues = normalized.Success
            ? Array.Empty<object>()
            : new object[]
            {
                new
                {
                    code = normalized.ErrorCode ?? "invalid_flow",
                    message = normalized.ErrorMessage ?? "Flow draft could not be normalized."
                }
            };
        var replaySucceeded = normalized.Success;
        var artifacts = new[]
        {
            new RuntimePreviewArtifactSummary
            {
                ArtifactId = $"stub-replay-{StableSuffix(frameId)}",
                ArtifactType = "replay_metadata",
                SourceTool = Name,
                MetadataOnly = true,
                BinaryIncluded = false,
                ByteLength = 0,
                Metadata = new
                {
                    frameId,
                    executedOperatorCount = executedOperators.Count
                }
            }
        };
        var data = new
        {
            source = "runtime_preview_static_stub",
            previewReady = replaySucceeded,
            frameSource = "stub",
            replaySummary = new
            {
                replaySucceeded,
                frameId,
                executedOperators,
                skippedOperators = Array.Empty<object>(),
                generatedRealImages = false,
                loadedModelFiles = false,
                accessedHardware = false,
                stationTouched = false
            },
            warnings = new[]
            {
                new
                {
                    code = "stub_replay_only",
                    message = "RuntimePreview replay used structural metadata only; no real image, model, camera, or Station was accessed."
                }
            },
            blockingIssues,
            artifacts,
            binaryIncluded = false
        };

        return Task.FromResult(replaySucceeded
            ? VisionAgentToolResult.Ok(data)
            : VisionAgentToolResult.Fail("runtime_preview_replay_stub_failed", "Replay stub could not normalize the flow.", data));
    }

    private static string StableSuffix(string value)
    {
        return Math.Abs((value ?? string.Empty).GetHashCode(StringComparison.OrdinalIgnoreCase)).ToString("x");
    }
}
