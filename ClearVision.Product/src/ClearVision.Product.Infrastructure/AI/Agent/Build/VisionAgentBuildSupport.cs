using System.Text.Json;
using ClearVision.Product.Application.DTOs;
using ClearVision.Product.Core.AI.Tools;
using ClearVision.Product.Core.DTOs;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Infrastructure.AI.AgentRun;

namespace ClearVision.Product.Infrastructure.AI.Agent;

internal static class VisionAgentBuildSupport
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public static BuildStepResult<T> StepResult<T>(
        T payload,
        string outputSummary,
        string status,
        object? details,
        string warningCode = "",
        string repairAction = "",
        string applyImpact = "",
        string deploymentImpact = "")
    {
        return new BuildStepResult<T>(
            payload,
            outputSummary,
            status,
            details,
            warningCode,
            repairAction,
            applyImpact,
            deploymentImpact);
    }

    public static VisionAgentToolContext BuildToolContext(
        AiFlowGenerationRequest request,
        VisionAgentBuildFromPlanRequest? build,
        string? currentFlowSnapshot)
    {
        return new VisionAgentToolContext
        {
            UserDescription = FirstNonEmpty(build?.OriginalUserPrompt, request.Description),
            AdditionalContext = request.AdditionalContext,
            SessionId = request.SessionId,
            AgentRunId = request.AgentRunId,
            ExistingFlowJson = currentFlowSnapshot,
            DebugTrace = false,
            RuntimePreviewConsent = false,
            AllowedPermissions = new HashSet<VisionAgentToolPermission>
            {
                VisionAgentToolPermission.ReadOnly,
                VisionAgentToolPermission.Simulation,
                VisionAgentToolPermission.DeploymentPrepare
            }
        };
    }

    public static TemplateCandidate? FirstTemplateCandidate(object? data)
    {
        var root = ToJsonElementOrNull(data);
        if (root == null ||
            !TryGetProperty(root.Value, "candidates", out var candidates) ||
            candidates.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var item in candidates.EnumerateArray())
        {
            return new TemplateCandidate(
                ReadString(item, "templateId") ?? string.Empty,
                ReadString(item, "scenarioKey") ?? string.Empty,
                ReadDouble(item, "score"));
        }

        return null;
    }

    public static IEnumerable<string> ReadOperatorTypes(object? templateSkeleton)
    {
        var root = ToJsonElementOrNull(templateSkeleton);
        if (root == null ||
            !TryGetProperty(root.Value, "operators", out var operators) ||
            operators.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (var op in operators.EnumerateArray())
        {
            var type = ReadString(op, "operatorType") ?? ReadString(op, "type");
            if (!string.IsNullOrWhiteSpace(type))
            {
                yield return type;
            }
        }
    }

    public static bool ToolHasBlockingIssues(VisionAgentToolResult result)
    {
        if (!result.Success)
        {
            return true;
        }

        return ReadCount(result.Data, "blockingIssues") > 0 ||
               ReadBool(result.Data, "dryRunSucceeded") == false;
    }

    public static string ToolSummary(string toolName, JsonElement? data, bool blocking)
    {
        if (data == null)
        {
            return $"{toolName} completed with no public data payload.";
        }

        if (toolName == "validate_flow")
        {
            return blocking
                ? "Schema validation found blocking issues."
                : "Schema validation passed with public metadata.";
        }

        if (toolName == "dryrun_flow")
        {
            return ReadBool(data.Value, "dryRunSucceeded") == false
                ? "Metadata dry-run reported a blocked draft."
                : "Metadata dry-run completed successfully.";
        }

        if (toolName == "runtime_package_precheck")
        {
            return ReadBool(data.Value, "readyForDeployment") == true
                ? "Runtime package readiness passed."
                : "Runtime package readiness blocks deployment but not canvas Apply.";
        }

        return $"{toolName} completed.";
    }

    public static List<AiPendingParameterInfo> MergePendingParameters(
        IEnumerable<AiPendingParameterInfo> mapped,
        AiFlowGenerationRequest request)
    {
        return DeduplicatePending(mapped
            .Concat(request.BuildFromPlan?.PlanSnapshot?.RecommendedDefaults
                .Where(item => item.Value.Contains("pending", StringComparison.OrdinalIgnoreCase))
                .Select(item => new AiPendingParameterInfo
                {
                    OperatorId = "plan_default",
                    ActualOperatorId = "plan_default",
                    ParameterNames = [item.Id]
                }) ?? []));
    }

    public static List<AiMissingResourceInfo> MergeMissingResources(
        IEnumerable<AiMissingResourceInfo> mapped,
        VisionAgentToolResult validation,
        VisionAgentToolResult packageReadiness)
    {
        var resources = mapped.ToList();
        resources.AddRange(ReadMissingResources(validation.Data));
        resources.AddRange(ReadMissingResources(packageReadiness.Data));
        return DeduplicateMissing(resources);
    }

    public static List<AiPendingParameterInfo> DeduplicatePending(IEnumerable<AiPendingParameterInfo> items)
    {
        return items
            .GroupBy(item => $"{item.OperatorId}|{item.ActualOperatorId}", StringComparer.OrdinalIgnoreCase)
            .Select(group => new AiPendingParameterInfo
            {
                OperatorId = group.First().OperatorId,
                ActualOperatorId = group.First().ActualOperatorId,
                ParameterNames = group.SelectMany(item => item.ParameterNames)
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList()
            })
            .Where(item => item.ParameterNames.Count > 0)
            .ToList();
    }

    public static List<AiMissingResourceInfo> DeduplicateMissing(IEnumerable<AiMissingResourceInfo> items)
    {
        return items
            .Where(item => !string.IsNullOrWhiteSpace(item.ResourceType) ||
                           !string.IsNullOrWhiteSpace(item.ResourceKey))
            .GroupBy(item => $"{item.ResourceType}|{item.ResourceKey}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
    }

    public static IEnumerable<AiMissingResourceInfo> ReadMissingResources(object? data)
    {
        var root = ToJsonElementOrNull(data);
        if (root == null ||
            !TryGetProperty(root.Value, "missingResources", out var resources) ||
            resources.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (var item in resources.EnumerateArray())
        {
            var kind = ReadString(item, "resourceType") ??
                       ReadString(item, "resourceKind") ??
                       "missing_resource";
            var key = ReadString(item, "resourceKey") ??
                      $"{ReadString(item, "tempId")}.{ReadString(item, "parameterName")}";
            yield return new AiMissingResourceInfo
            {
                ResourceType = kind,
                ResourceKey = key,
                Description = FirstNonEmpty(
                    ReadString(item, "description"),
                    ReadString(item, "message"),
                    "Missing resource metadata.")
            };
        }
    }

    public static string FirstFixRecommendation(
        VisionAgentApplyGate gate,
        IReadOnlyList<AiMissingResourceInfo> missingResources,
        IReadOnlyList<AiPendingParameterInfo> pendingParameters)
    {
        if (gate.Blocked)
        {
            return "Fix workflow structure blockers before applying the draft to the canvas.";
        }

        var firstMissing = missingResources.FirstOrDefault();
        if (firstMissing != null)
        {
            return $"Bind missing {firstMissing.ResourceType} metadata for {firstMissing.ResourceKey} before deployment.";
        }

        var firstPending = pendingParameters.FirstOrDefault();
        if (firstPending != null)
        {
            return $"Confirm pending parameter metadata on {firstPending.OperatorId} before release.";
        }

        return gate.DeploymentReady
            ? "Review the draft on canvas, then proceed to runtime packaging when ready."
            : "Review readiness gates and resolve deployment blockers before Station deployment.";
    }

    public static object? ToJsonCompatible(object? value)
    {
        return value == null
            ? null
            : JsonSerializer.Deserialize<object>(JsonSerializer.Serialize(value, JsonOptions), JsonOptions);
    }

    public static JsonElement ToJsonElement(object value)
    {
        return JsonSerializer.SerializeToElement(value, JsonOptions);
    }

    public static JsonElement? ToJsonElementOrNull(object? value)
    {
        if (value == null)
        {
            return null;
        }

        return JsonSerializer.SerializeToElement(value, JsonOptions);
    }

    public static bool TryGetProperty(JsonElement element, string propertyName, out JsonElement value)
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

    public static string? ReadString(JsonElement element, string propertyName)
    {
        return TryGetProperty(element, propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    public static double ReadDouble(JsonElement element, string propertyName)
    {
        return TryGetProperty(element, propertyName, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetDouble()
            : 0;
    }

    public static bool? ReadBool(object? data, string propertyName)
    {
        var root = ToJsonElementOrNull(data);
        return root == null ? null : ReadBool(root.Value, propertyName);
    }

    public static bool? ReadBool(JsonElement element, string propertyName)
    {
        if (!TryGetProperty(element, propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null
        };
    }

    public static int ReadCount(object? data, string propertyName)
    {
        var root = ToJsonElementOrNull(data);
        if (root == null ||
            !TryGetProperty(root.Value, propertyName, out var value))
        {
            return 0;
        }

        return value.ValueKind == JsonValueKind.Array
            ? value.GetArrayLength()
            : 0;
    }

    public static List<string> ReadIssueCodes(object? data, string propertyName)
    {
        var root = ToJsonElementOrNull(data);
        if (root == null ||
            !TryGetProperty(root.Value, propertyName, out var value) ||
            value.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return value.EnumerateArray()
            .Select(item => FirstNonEmpty(ReadString(item, "code"), ReadString(item, "message")))
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static IReadOnlyList<string> ReadExistingNodeIds(string? currentFlowSnapshot)
    {
        if (string.IsNullOrWhiteSpace(currentFlowSnapshot))
        {
            return [];
        }

        try
        {
            using var doc = JsonDocument.Parse(currentFlowSnapshot);
            if (!TryGetProperty(doc.RootElement, "operators", out var operators) ||
                operators.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            return operators.EnumerateArray()
                .Select(item => FirstNonEmpty(
                    ReadString(item, "tempId"),
                    ReadString(item, "id"),
                    ReadString(item, "name")))
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Take(32)
                .ToList();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    public static OperatorFlowDto? TryReadExistingCanvasFlow(string? currentFlowSnapshot)
    {
        if (string.IsNullOrWhiteSpace(currentFlowSnapshot))
        {
            return null;
        }

        try
        {
            var flow = JsonSerializer.Deserialize<OperatorFlowDto>(currentFlowSnapshot, JsonOptions);
            return flow?.Operators.Count > 0 ? flow : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static string DefaultParameterValue(string operatorType, string parameterName)
    {
        if (parameterName.Contains("camera", StringComparison.OrdinalIgnoreCase))
        {
            return "<pending-camera-binding>";
        }

        if (parameterName.Contains("model", StringComparison.OrdinalIgnoreCase))
        {
            return "<pending-model-resource>";
        }

        if (parameterName.Contains("template", StringComparison.OrdinalIgnoreCase))
        {
            return "<pending-template-artifact>";
        }

        if (parameterName.Contains("tolerance", StringComparison.OrdinalIgnoreCase))
        {
            return "<pending-tolerance>";
        }

        if (parameterName.Contains("channel", StringComparison.OrdinalIgnoreCase))
        {
            return "<pending-output-channel>";
        }

        return operatorType switch
        {
            "ResultJudgment" when parameterName.Equals("Rule", StringComparison.OrdinalIgnoreCase) => "OK when inspection score satisfies configured threshold.",
            "Thresholding" when parameterName.Equals("Mode", StringComparison.OrdinalIgnoreCase) => "adaptive_review",
            "TemplateMatching" when parameterName.Equals("MinScore", StringComparison.OrdinalIgnoreCase) => "0.8",
            "TemplateMatching" when parameterName.Equals("MaxMatches", StringComparison.OrdinalIgnoreCase) => "1",
            "DeepLearning" when parameterName.Equals("ConfidenceThreshold", StringComparison.OrdinalIgnoreCase) => "0.6",
            "SurfaceDefectDetection" when parameterName.Equals("ModelKind", StringComparison.OrdinalIgnoreCase) => "surface_defect",
            "SemanticSegmentation" when parameterName.Equals("ModelKind", StringComparison.OrdinalIgnoreCase) => "segmentation",
            "BlobAnalysis" when parameterName.Equals("MinArea", StringComparison.OrdinalIgnoreCase) => "20",
            "BlobAnalysis" when parameterName.Equals("MaxArea", StringComparison.OrdinalIgnoreCase) => "<pending-max-area>",
            "RoiManager" when parameterName.Equals("RoiName", StringComparison.OrdinalIgnoreCase) => "inspection_roi",
            _ => "<pending-parameter>"
        };
    }

    public static string MissingResourceKind(string operatorType, string parameterName, bool pending)
    {
        if (!pending)
        {
            return string.Empty;
        }

        if (parameterName.Contains("camera", StringComparison.OrdinalIgnoreCase))
        {
            return "camera_binding";
        }

        if (parameterName.Contains("model", StringComparison.OrdinalIgnoreCase))
        {
            return "model_resource";
        }

        if (parameterName.Contains("template", StringComparison.OrdinalIgnoreCase))
        {
            return "template_artifact";
        }

        if (parameterName.Contains("channel", StringComparison.OrdinalIgnoreCase))
        {
            return "output_channel";
        }

        if (operatorType.Contains("Measure", StringComparison.OrdinalIgnoreCase) ||
            parameterName.Contains("tolerance", StringComparison.OrdinalIgnoreCase))
        {
            return "measurement_parameter";
        }

        return string.Empty;
    }

    public static OperatorType ToOperatorType(string operatorType)
    {
        if (Enum.TryParse<OperatorType>(operatorType, ignoreCase: true, out var parsed))
        {
            return parsed;
        }

        return operatorType switch
        {
            "MeasureDistance" => OperatorType.Measurement,
            "SemanticSegmentation" => OperatorType.DeepLearning,
            "ImageCompose" => OperatorType.ImageAdd,
            _ => OperatorType.DeepLearning
        };
    }

    public static GenerateFlowMode ToGenerateFlowMode(string buildIntent)
    {
        return buildIntent switch
        {
            "modify" or "refactor" => GenerateFlowMode.Modify,
            "explain" => GenerateFlowMode.Explain,
            "review_pending_parameters" => GenerateFlowMode.ReviewPendingParameters,
            _ => GenerateFlowMode.New
        };
    }

    public static string ToTurnIntent(string buildIntent)
    {
        return buildIntent switch
        {
            "modify" or "refactor" => AiTurnIntents.ModifyFlow,
            "explain" => AiTurnIntents.ExplainFlow,
            "review_pending_parameters" => AiTurnIntents.ReviewPendingParameters,
            _ => AiTurnIntents.NewFlow
        };
    }

    public static int FlowOperatorCount(object? flow)
    {
        if (flow is OperatorFlowDto dto)
        {
            return dto.Operators.Count;
        }

        var root = ToJsonElementOrNull(flow);
        return root != null &&
               TryGetProperty(root.Value, "operators", out var operators) &&
               operators.ValueKind == JsonValueKind.Array
            ? operators.GetArrayLength()
            : 0;
    }

    public static string StageTitle(string stage)
    {
        return stage switch
        {
            "plan_generation" => "Load Plan",
            "resolve_build_intent" => "Resolve Build intent",
            "template_strategy" => "Resolve template strategy",
            "operator_pipeline" => "Select operator pipeline",
            "parameter_mapping" => "Map parameters",
            "workflow_draft" => "Draft workflow",
            "validate_schema" => "Validate schema",
            "metadata_dry_run" => "Metadata dry-run",
            "package_readiness" => "Package readiness",
            "station_compatibility" => "Station compatibility",
            "operator_contract" => "Operator contract",
            "release_review" => "Release review",
            "repair_loop" => "Repair loop",
            "workflow_diff" => "Workflow diff",
            "apply_gate" => "Apply gate",
            _ => stage
        };
    }

    public static string NormalizeStatus(string? status)
    {
        return status is AgentRunEventStatuses.Completed or
            AgentRunEventStatuses.Failed or
            AgentRunEventStatuses.Blocked or
            AgentRunEventStatuses.Cancelled or
            AgentRunEventStatuses.Running
            ? status
            : AgentRunEventStatuses.Completed;
    }

    public static string TempIdFor(string operatorType, int ordinal)
    {
        return operatorType switch
        {
            "ImageAcquisition" => "op_cam",
            "RoiManager" => "op_roi",
            "SurfaceDefectDetection" => "op_surface_defect",
            "DeepLearning" => "op_detect",
            "SemanticSegmentation" => "op_segment",
            "TemplateMatching" => "op_match",
            "BlobAnalysis" => "op_blob",
            "Thresholding" => "op_threshold",
            "CircleMeasurement" => ordinal <= 2 ? "op_circle_a" : "op_circle_b",
            "MeasureDistance" => "op_distance",
            "ResultJudgment" => "op_judge",
            "ResultOutput" => "op_out",
            _ => $"op_{new string(operatorType.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray())}_{ordinal}"
        };
    }

    public static string Clean(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
    }

    public static string CleanValue(string? value)
    {
        var text = Clean(value);
        return string.IsNullOrWhiteSpace(text) ? "<pending-parameter>" : text;
    }

    public static string FirstNonEmpty(params string?[] values)
    {
        return values.Select(Clean).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
    }
}
