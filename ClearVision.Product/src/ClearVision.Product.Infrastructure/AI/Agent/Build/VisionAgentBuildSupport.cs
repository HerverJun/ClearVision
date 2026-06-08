using System.Text.Json;
using ClearVision.Product.Application.DTOs;
using ClearVision.Product.Core.DTOs;
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
