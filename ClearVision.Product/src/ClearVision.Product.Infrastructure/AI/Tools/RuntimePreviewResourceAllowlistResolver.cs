using System.Text.Json;
using ClearVision.Product.Core.AI.Tools;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Infrastructure.AI.Agent;

namespace ClearVision.Product.Infrastructure.AI.Tools;

public sealed class RuntimePreviewResourceAllowlistResolver
{
    public const string MetadataToolName = "runtime_preview_metadata";

    public RuntimePreviewResourceTrace Resolve(RuntimePreviewRequest request)
    {
        var config = request.PilotConfig;
        config.Normalize();

        if (!config.Enabled)
        {
            return Deny(
                "pilot",
                null,
                "runtime_preview_pilot_disabled",
                "RuntimePreview Pilot is disabled; use Offline adapter.");
        }

        if (!string.Equals(config.Mode, RuntimePreviewPilotConfig.ModeMetadataOnly, StringComparison.OrdinalIgnoreCase))
        {
            return Deny(
                "pilot",
                config.Mode,
                "runtime_preview_mode_denied",
                "RuntimePreview Pilot v0.8 only supports metadata_only mode.");
        }

        if (ContainsImageBytes(request.Arguments))
        {
            return Deny(
                "image_bytes",
                null,
                "runtime_preview_image_bytes_denied",
                "RuntimePreview Pilot never accepts encoded image payloads.");
        }

        if (ContainsDangerousField(request.Arguments, out var dangerousField, out var dangerousValue, out var reasonCode))
        {
            return Deny(
                dangerousField,
                dangerousValue,
                reasonCode,
                "RuntimePreview Pilot denied a dangerous resource request.");
        }

        if (string.Equals(request.ToolName, RuntimePreviewPermissionGate.CaptureToolName, StringComparison.OrdinalIgnoreCase))
        {
            return ResolveCapture(request, config);
        }

        if (string.Equals(request.ToolName, RuntimePreviewPermissionGate.ReplayToolName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(request.ToolName, MetadataToolName, StringComparison.OrdinalIgnoreCase))
        {
            return ResolveReplay(request, config);
        }

        return Deny(
            "tool",
            request.ToolName,
            "runtime_preview_tool_not_supported",
            "RuntimePreview Pilot supports metadata-only runtime_preview_metadata plus gated capture/replay tool names.");
    }

    private static RuntimePreviewResourceTrace ResolveCapture(
        RuntimePreviewRequest request,
        RuntimePreviewPilotConfig config)
    {
        var cameraBindingId =
            ReadArgumentString(request.Arguments, "cameraBindingId") ??
            ReadArgumentString(request.Arguments, "cameraId");
        if (string.IsNullOrWhiteSpace(cameraBindingId))
        {
            return Missing(
                "camera",
                "CameraBindingId",
                "runtime_preview_camera_binding_missing",
                "RuntimePreview capture requires an allowlisted CameraBindingId or CameraId.");
        }

        return RequireAllowlisted(
            "camera",
            cameraBindingId,
            config.AllowedCameraBindingIds,
            "runtime_preview_camera_allowlist_empty",
            "runtime_preview_camera_not_allowlisted");
    }

    private static RuntimePreviewResourceTrace ResolveReplay(
        RuntimePreviewRequest request,
        RuntimePreviewPilotConfig config)
    {
        var directFlowId = ReadArgumentString(request.Arguments, "flowId");
        if (!string.IsNullOrWhiteSpace(directFlowId))
        {
            var flowDecision = RequireAllowlisted(
                "flow",
                directFlowId,
                config.AllowedFlowIds,
                "runtime_preview_flow_allowlist_empty",
                "runtime_preview_flow_not_allowlisted");
            if (!flowDecision.Allowed)
            {
                return flowDecision;
            }
        }

        var directRoot =
            ReadArgumentString(request.Arguments, "resourceRootId") ??
            ReadArgumentString(request.Arguments, "resourceRoot");
        if (!string.IsNullOrWhiteSpace(directRoot))
        {
            var rootDecision = RequireAllowlisted(
                "resourceRoot",
                directRoot,
                config.AllowedResourceRoots,
                "runtime_preview_resource_root_allowlist_empty",
                "runtime_preview_resource_root_not_allowlisted");
            if (!rootDecision.Allowed)
            {
                return rootDecision;
            }
        }

        var normalized = VisionAgentFlowDraftNormalizer.Normalize(request.Arguments, request.Context);
        if (!normalized.Success)
        {
            return Deny(
                "flow",
                null,
                normalized.ErrorCode ?? "runtime_preview_flow_invalid",
                normalized.ErrorMessage ?? "RuntimePreview flow draft could not be normalized.");
        }

        var checks = new List<RuntimePreviewResourceTrace>();
        foreach (var op in normalized.Flow.Operators)
        {
            var dangerous = ResolveDangerousOperatorResources(op);
            if (!dangerous.Allowed)
            {
                return dangerous;
            }

            var rootDecision = ResolveOperatorResourceRoot(op, config);
            if (!rootDecision.Allowed)
            {
                return rootDecision;
            }

            var check = op.OperatorType switch
            {
                "ImageAcquisition" => ResolveImageAcquisition(op, config),
                "TemplateMatching" => ResolveTemplateMatching(op, config),
                "DeepLearning" => ResolveDeepLearning(op, config),
                "ResultOutput" => ResolveResultOutput(op),
                _ => RuntimePreviewResourceTrace.NotEvaluated() with
                {
                    ReasonCode = "runtime_preview_operator_no_external_resource",
                    ResourceType = op.OperatorType,
                    Trace =
                    [
                        TraceItem(op.OperatorType, op.TempId, "operator_no_external_resource", true)
                    ]
                }
            };

            if (!check.Allowed)
            {
                return check;
            }

            checks.Add(check);
        }

        if (checks.Count == 0 && string.IsNullOrWhiteSpace(directFlowId))
        {
            return Missing(
                "flow",
                "flow",
                "runtime_preview_flow_missing",
                "RuntimePreview replay requires a flow draft or allowlisted flowId.");
        }

        return new RuntimePreviewResourceTrace
        {
            Allowed = true,
            ReasonCode = "runtime_preview_resources_allowlisted",
            ResourceType = "workflow",
            ResourceId = directFlowId,
            NormalizedKey = RuntimePreviewPilotConfig.NormalizeResourceKey(directFlowId) ?? string.Empty,
            Trace = checks
                .SelectMany(item => item.Trace.Count > 0 ? item.Trace : [TraceItem(item.ResourceType, item.ResourceId, item.ReasonCode, true)])
                .ToList()
        };
    }

    private static RuntimePreviewResourceTrace ResolveOperatorResourceRoot(
        VisionAgentFlowOperator op,
        RuntimePreviewPilotConfig config)
    {
        var root =
            ReadParameter(op, "ResourceRootId") ??
            ReadParameter(op, "ResourceRoot");
        if (IsPending(root))
        {
            return RuntimePreviewResourceTrace.NotEvaluated();
        }

        var decision = RequireAllowlisted(
            "resourceRoot",
            root,
            config.AllowedResourceRoots,
            "runtime_preview_resource_root_allowlist_empty",
            "runtime_preview_resource_root_not_allowlisted");
        return decision with
        {
            Trace =
            [
                TraceItem("resourceRoot", op.TempId, decision.ReasonCode, decision.Allowed)
            ]
        };
    }

    private static RuntimePreviewResourceTrace ResolveImageAcquisition(
        VisionAgentFlowOperator op,
        RuntimePreviewPilotConfig config)
    {
        var sourceType = ReadParameter(op, "SourceType");
        if (string.Equals(sourceType, "File", StringComparison.OrdinalIgnoreCase))
        {
            return Deny(
                "file_path",
                ReadParameter(op, "FilePath"),
                "runtime_preview_file_source_denied",
                "RuntimePreview Pilot does not read real image files.");
        }

        var cameraBindingId =
            ReadParameter(op, "CameraBindingId") ??
            ReadParameter(op, "CameraId");
        if (IsPending(cameraBindingId))
        {
            return Missing(
                "camera",
                $"{op.TempId}.CameraBindingId",
                "runtime_preview_camera_binding_missing",
                "RuntimePreview needs an allowlisted camera binding before pilot metadata can run.");
        }

        var decision = RequireAllowlisted(
            "camera",
            cameraBindingId,
            config.AllowedCameraBindingIds,
            "runtime_preview_camera_allowlist_empty",
            "runtime_preview_camera_not_allowlisted");
        return decision with
        {
            Trace =
            [
                TraceItem("camera", op.TempId, decision.ReasonCode, decision.Allowed)
            ]
        };
    }

    private static RuntimePreviewResourceTrace ResolveTemplateMatching(
        VisionAgentFlowOperator op,
        RuntimePreviewPilotConfig config)
    {
        var templatePath = ReadParameter(op, "TemplatePath");
        if (!string.IsNullOrWhiteSpace(templatePath) && !IsPending(templatePath))
        {
            return Deny(
                "template_path",
                templatePath,
                "runtime_preview_template_path_denied",
                "RuntimePreview Pilot accepts TemplateId only; real template paths are denied.");
        }

        var templateId = ReadParameter(op, "TemplateId");
        if (IsPending(templateId))
        {
            return Missing(
                "template",
                $"{op.TempId}.TemplateId",
                "runtime_preview_template_missing",
                "RuntimePreview needs an allowlisted TemplateId before pilot metadata can run.");
        }

        var decision = RequireAllowlisted(
            "template",
            templateId,
            config.AllowedTemplateIds,
            "runtime_preview_template_allowlist_empty",
            "runtime_preview_template_not_allowlisted");
        return decision with
        {
            Trace =
            [
                TraceItem("template", op.TempId, decision.ReasonCode, decision.Allowed)
            ]
        };
    }

    private static RuntimePreviewResourceTrace ResolveDeepLearning(
        VisionAgentFlowOperator op,
        RuntimePreviewPilotConfig config)
    {
        var modelPath = ReadParameter(op, "ModelPath") ?? ReadParameter(op, "ModelCatalogPath");
        if (!string.IsNullOrWhiteSpace(modelPath) && !IsPending(modelPath))
        {
            return Deny(
                "model_path",
                modelPath,
                "runtime_preview_model_path_denied",
                "RuntimePreview Pilot accepts ModelId only; real model paths are denied.");
        }

        var modelId = ReadParameter(op, "ModelId");
        if (IsPending(modelId))
        {
            return Missing(
                "model",
                $"{op.TempId}.ModelId",
                "runtime_preview_model_missing",
                "RuntimePreview needs an allowlisted ModelId before pilot metadata can run.");
        }

        var decision = RequireAllowlisted(
            "model",
            modelId,
            config.AllowedModelIds,
            "runtime_preview_model_allowlist_empty",
            "runtime_preview_model_not_allowlisted");
        return decision with
        {
            Trace =
            [
                TraceItem("model", op.TempId, decision.ReasonCode, decision.Allowed)
            ]
        };
    }

    private static RuntimePreviewResourceTrace ResolveResultOutput(VisionAgentFlowOperator op)
    {
        var channel =
            ReadParameter(op, "Channel") ??
            ReadParameter(op, "OutputChannel") ??
            ReadParameter(op, "OutputChannelId");
        if (string.Equals(channel, "plc", StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrWhiteSpace(ReadParameter(op, "PlcAddress")) ||
            !string.IsNullOrWhiteSpace(ReadParameter(op, "PLCParameters")))
        {
            return Deny(
                "plc",
                channel,
                "runtime_preview_plc_denied",
                "RuntimePreview Pilot never writes or prepares PLC resources.");
        }

        if (string.Equals(channel, "file", StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrWhiteSpace(ReadParameter(op, "FilePath")) ||
            !string.IsNullOrWhiteSpace(ReadParameter(op, "OutputPath")))
        {
            return Deny(
                "file_path",
                ReadParameter(op, "FilePath") ?? ReadParameter(op, "OutputPath"),
                "runtime_preview_file_output_denied",
                "RuntimePreview Pilot never writes file outputs.");
        }

        return RuntimePreviewResourceTrace.NotEvaluated() with
        {
            ReasonCode = "runtime_preview_output_metadata_only",
            ResourceType = "output",
            Trace =
            [
                TraceItem("output", op.TempId, "runtime_preview_output_metadata_only", true)
            ]
        };
    }

    private static RuntimePreviewResourceTrace ResolveDangerousOperatorResources(VisionAgentFlowOperator op)
    {
        foreach (var parameter in op.Parameters)
        {
            var name = parameter.Key;
            var value = parameter.Value;
            if (string.IsNullOrWhiteSpace(value) || IsPending(value))
            {
                continue;
            }

            if (LooksStationField(name))
            {
                return Deny(
                    "station",
                    SafeResourceId(value),
                    "runtime_preview_station_denied",
                    "RuntimePreview Pilot never touches Station resources.");
            }

            if (LooksPlcField(name))
            {
                return Deny(
                    "plc",
                    SafeResourceId(value),
                    "runtime_preview_plc_denied",
                    "RuntimePreview Pilot never touches PLC resources.");
            }

            if (LooksPathField(name) || LooksPathValue(value))
            {
                return Deny(
                    "external_path",
                    SafeResourceId(value),
                    LooksTraversal(value) ? "runtime_preview_path_traversal_denied" : "runtime_preview_external_path_denied",
                    "RuntimePreview Pilot denies external paths.");
            }
        }

        return RuntimePreviewResourceTrace.NotEvaluated();
    }

    private static RuntimePreviewResourceTrace RequireAllowlisted(
        string resourceType,
        string? resourceId,
        IReadOnlyList<string> allowlist,
        string emptyCode,
        string missCode)
    {
        if (IsPending(resourceId))
        {
            return Missing(
                resourceType,
                resourceType,
                $"runtime_preview_{resourceType}_missing",
                $"RuntimePreview Pilot requires an allowlisted {resourceType} resource.");
        }

        var normalized = RuntimePreviewPilotConfig.NormalizeResourceKey(resourceId);
        if (normalized == null)
        {
            return Deny(
                resourceType,
                SafeResourceId(resourceId),
                "runtime_preview_resource_key_denied",
                "RuntimePreview Pilot denied an unsafe resource key.");
        }

        if (allowlist.Count == 0)
        {
            return Deny(
                resourceType,
                SafeResourceId(resourceId),
                emptyCode,
                $"RuntimePreview Pilot {resourceType} allowlist is empty.");
        }

        var allowed = allowlist.Contains(normalized, StringComparer.OrdinalIgnoreCase);
        return new RuntimePreviewResourceTrace
        {
            Allowed = allowed,
            ReasonCode = allowed ? "runtime_preview_resource_allowlisted" : missCode,
            ResourceType = resourceType,
            ResourceId = SafeResourceId(resourceId),
            NormalizedKey = normalized,
            MissingResources = allowed
                ? []
                :
                [
                    new
                    {
                        resourceType,
                        resourceKey = normalized,
                        reasonCode = missCode,
                        description = $"Resource '{normalized}' is not in RuntimePreview Pilot allowlist."
                    }
                ],
            Trace =
            [
                TraceItem(resourceType, SafeResourceId(resourceId), allowed ? "allowlist_hit" : "allowlist_miss", allowed)
            ]
        };
    }

    private static RuntimePreviewResourceTrace Missing(
        string resourceType,
        string resourceKey,
        string reasonCode,
        string description)
    {
        return new RuntimePreviewResourceTrace
        {
            Allowed = false,
            ReasonCode = reasonCode,
            ResourceType = resourceType,
            ResourceId = null,
            NormalizedKey = RuntimePreviewPilotConfig.NormalizeResourceKey(resourceKey) ?? resourceKey,
            MissingResources =
            [
                new
                {
                    resourceType,
                    resourceKey,
                    reasonCode,
                    description
                }
            ],
            Trace =
            [
                TraceItem(resourceType, resourceKey, reasonCode, false)
            ]
        };
    }

    private static RuntimePreviewResourceTrace Deny(
        string resourceType,
        string? resourceId,
        string reasonCode,
        string description)
    {
        return new RuntimePreviewResourceTrace
        {
            Allowed = false,
            ReasonCode = reasonCode,
            ResourceType = resourceType,
            ResourceId = SafeResourceId(resourceId),
            NormalizedKey = RuntimePreviewPilotConfig.NormalizeResourceKey(resourceId) ?? string.Empty,
            MissingResources =
            [
                new
                {
                    resourceType,
                    resourceKey = RuntimePreviewPilotConfig.NormalizeResourceKey(resourceId) ?? resourceType,
                    reasonCode,
                    description
                }
            ],
            Trace =
            [
                TraceItem(resourceType, SafeResourceId(resourceId), reasonCode, false)
            ]
        };
    }

    private static object TraceItem(string resourceType, string? resourceId, string reasonCode, bool allowed)
    {
        return new
        {
            resourceType,
            resourceId = SafeResourceId(resourceId),
            reasonCode,
            allowed
        };
    }

    private static bool ContainsDangerousField(
        JsonElement element,
        out string resourceType,
        out string? resourceId,
        out string reasonCode)
    {
        resourceType = string.Empty;
        resourceId = null;
        reasonCode = string.Empty;
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.Object ||
                    property.Value.ValueKind == JsonValueKind.Array)
                {
                    if (ContainsDangerousField(property.Value, out resourceType, out resourceId, out reasonCode))
                    {
                        return true;
                    }
                }

                if (property.Value.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                var value = property.Value.GetString();
                if (string.IsNullOrWhiteSpace(value) || IsPending(value))
                {
                    continue;
                }

                if (LooksStationField(property.Name))
                {
                    resourceType = "station";
                    resourceId = value;
                    reasonCode = "runtime_preview_station_denied";
                    return true;
                }

                if (LooksPlcField(property.Name))
                {
                    resourceType = "plc";
                    resourceId = value;
                    reasonCode = "runtime_preview_plc_denied";
                    return true;
                }

                if (LooksPathField(property.Name) || LooksPathValue(value))
                {
                    resourceType = "external_path";
                    resourceId = value;
                    reasonCode = LooksTraversal(value)
                        ? "runtime_preview_path_traversal_denied"
                        : "runtime_preview_external_path_denied";
                    return true;
                }
            }
        }

        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                if (ContainsDangerousField(item, out resourceType, out resourceId, out reasonCode))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool ContainsImageBytes(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.Name.Contains("base64", StringComparison.OrdinalIgnoreCase) ||
                    property.Name.Contains("imageBytes", StringComparison.OrdinalIgnoreCase) ||
                    property.Name.Equals("bytes", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                if (ContainsImageBytes(property.Value))
                {
                    return true;
                }
            }
        }

        if (element.ValueKind == JsonValueKind.Array)
        {
            return element.EnumerateArray().Any(ContainsImageBytes);
        }

        if (element.ValueKind == JsonValueKind.String)
        {
            var value = element.GetString();
            return value?.Contains("base64", StringComparison.OrdinalIgnoreCase) == true ||
                   value?.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase) == true;
        }

        return false;
    }

    private static string? ReadArgumentString(JsonElement element, string propertyName)
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

    private static string? ReadParameter(VisionAgentFlowOperator op, string name)
    {
        return op.Parameters.TryGetValue(name, out var value) ? value : null;
    }

    private static bool LooksStationField(string name)
    {
        return name.Contains("station", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksPlcField(string name)
    {
        return name.Contains("plc", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("PLC", StringComparison.Ordinal);
    }

    private static bool LooksPathField(string name)
    {
        return name.Contains("path", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("file", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("image", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksPathValue(string value)
    {
        return value.Contains(":\\", StringComparison.Ordinal) ||
               value.Contains(":/", StringComparison.Ordinal) ||
               value.Contains("\\", StringComparison.Ordinal) ||
               value.Contains("/", StringComparison.Ordinal) ||
               LooksTraversal(value);
    }

    private static bool LooksTraversal(string value)
    {
        return value.Contains("..", StringComparison.Ordinal);
    }

    private static bool IsPending(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ||
               value.Trim().StartsWith("<pending", StringComparison.OrdinalIgnoreCase);
    }

    private static string? SafeResourceId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return LooksPathValue(trimmed) || trimmed.Contains("base64", StringComparison.OrdinalIgnoreCase)
            ? "<redacted>"
            : trimmed;
    }
}
