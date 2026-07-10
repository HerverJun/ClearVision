using System.Text.Json;
using ClearVision.Product.Core.AI.Tools;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Services;
using ClearVision.Product.Infrastructure.AI;

namespace ClearVision.Product.Infrastructure.AI.Tools;

public sealed class RuntimePreviewPilotResourceCatalog
{
    public RuntimePreviewPilotCatalog Build(
        RuntimePreviewPilotConfig config,
        AppConfig? appConfig,
        AiConfigStore? aiConfigStore,
        JsonElement? workflowDraft = null)
    {
        var normalizedConfig = config.CloneNormalized();
        var items = new List<RuntimePreviewPilotCatalogItem>();
        AddCameraItems(items, appConfig);
        AddAiModelItems(items, aiConfigStore);
        AddWorkflowDraftItems(items, workflowDraft);

        var sourceCounts = items
            .GroupBy(item => item.Source, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);

        if (items.Count == 0)
        {
            items.AddRange(FixtureFallbackItems());
            sourceCounts["fixture"] = items.Count;
        }

        return new RuntimePreviewPilotCatalog
        {
            GeneratedAtUtc = DateTimeOffset.UtcNow,
            Items = items
                .GroupBy(item => $"{item.ResourceType}:{item.Id}:{item.Source}", StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderBy(item => item.ResourceType, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            SourceSummary = new
            {
                appConfig = sourceCounts.GetValueOrDefault("app_config"),
                aiConfigStore = sourceCounts.GetValueOrDefault("ai_config_store"),
                workflowDraft = sourceCounts.GetValueOrDefault("workflow_draft"),
                fixture = sourceCounts.GetValueOrDefault("fixture"),
                metadataOnly = true,
                realResourcesTouched = false
            },
            AllowlistCounts = AllowlistCounts(normalizedConfig)
        };
    }

    public static object AllowlistCounts(RuntimePreviewPilotConfig config)
    {
        return new
        {
            camera = config.AllowedCameraBindingIds.Count,
            model = config.AllowedModelIds.Count,
            template = config.AllowedTemplateIds.Count,
            flow = config.AllowedFlowIds.Count,
            resourceRoot = config.AllowedResourceRoots.Count
        };
    }

    private static void AddCameraItems(ICollection<RuntimePreviewPilotCatalogItem> items, AppConfig? appConfig)
    {
        foreach (var binding in appConfig?.Cameras ?? [])
        {
            var id = SafeId(binding.Id);
            var safe = RuntimePreviewPilotConfig.IsAllowedToken(binding.Id);
            items.Add(new RuntimePreviewPilotCatalogItem
            {
                Id = id,
                DisplayName = SafeDisplayName(binding.DisplayName, "Camera binding"),
                ResourceType = "camera",
                Source = "app_config",
                SafeForPilot = safe,
                ReasonCode = safe ? "runtime_preview_catalog_safe_metadata" : "runtime_preview_catalog_unsafe_id_redacted",
                Redacted = id == "<redacted>",
                Metadata = new
                {
                    enabled = binding.IsEnabled,
                    manufacturer = SafeDisplayName(binding.Manufacturer, string.Empty),
                    modelName = SafeDisplayName(binding.ModelName, string.Empty),
                    interfaceType = SafeDisplayName(binding.InterfaceType, string.Empty)
                }
            });
        }
    }

    private static void AddAiModelItems(ICollection<RuntimePreviewPilotCatalogItem> items, AiConfigStore? aiConfigStore)
    {
        if (aiConfigStore == null)
        {
            return;
        }

        foreach (var model in aiConfigStore.GetAll())
        {
            var id = SafeId(model.Id);
            var safe = RuntimePreviewPilotConfig.IsAllowedToken(model.Id);
            var roles = AiModelConfig.NormalizeRoleBindings(model.RoleBindings, model.ModelRole);
            items.Add(new RuntimePreviewPilotCatalogItem
            {
                Id = id,
                DisplayName = SafeDisplayName(model.DisplayName ?? model.Name, "AI model"),
                ResourceType = "model",
                Source = "ai_config_store",
                SafeForPilot = safe,
                ReasonCode = safe ? "runtime_preview_catalog_safe_metadata" : "runtime_preview_catalog_unsafe_id_redacted",
                Redacted = id == "<redacted>",
                Metadata = new
                {
                    provider = SafeDisplayName(model.Provider, "OpenAI Compatible"),
                    roleBindings = roles,
                    enabled = model.IsEnabled,
                    active = model.IsActive,
                    hasApiKey = !string.IsNullOrWhiteSpace(model.ApiKey),
                    baseUrl = "<redacted>"
                }
            });
        }
    }

    private static void AddWorkflowDraftItems(ICollection<RuntimePreviewPilotCatalogItem> items, JsonElement? workflowDraft)
    {
        if (workflowDraft is not { ValueKind: JsonValueKind.Object } draft)
        {
            return;
        }

        if (!draft.TryGetProperty("operators", out var operators) || operators.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var op in operators.EnumerateArray())
        {
            if (!op.TryGetProperty("operatorType", out var typeElement) ||
                typeElement.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var operatorType = typeElement.GetString() ?? string.Empty;
            var tempId = ReadString(op, "tempId") ?? operatorType;
            if (!op.TryGetProperty("parameters", out var parameters) || parameters.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            AddWorkflowResource(items, parameters, "CameraBindingId", "camera", tempId);
            AddWorkflowResource(items, parameters, "CameraId", "camera", tempId);
            AddWorkflowResource(items, parameters, "ModelId", "model", tempId);
            AddWorkflowResource(items, parameters, "TemplateId", "template", tempId);
            AddWorkflowResource(items, parameters, "FlowId", "flow", tempId);
            AddWorkflowResource(items, parameters, "ResourceRootId", "resourceRoot", tempId);
            AddWorkflowResource(items, parameters, "ResourceRoot", "resourceRoot", tempId);
        }
    }

    private static void AddWorkflowResource(
        ICollection<RuntimePreviewPilotCatalogItem> items,
        JsonElement parameters,
        string parameterName,
        string resourceType,
        string tempId)
    {
        var raw = ReadString(parameters, parameterName);
        if (OperatorParameterValueSemantics.IsMissing(raw))
        {
            return;
        }

        var id = SafeId(raw);
        var safe = RuntimePreviewPilotConfig.IsAllowedToken(raw);
        items.Add(new RuntimePreviewPilotCatalogItem
        {
            Id = id,
            DisplayName = safe ? $"{resourceType}:{id}" : "<redacted>",
            ResourceType = resourceType,
            Source = "workflow_draft",
            SafeForPilot = safe,
            ReasonCode = safe ? "runtime_preview_catalog_workflow_metadata" : "runtime_preview_catalog_unsafe_workflow_value_redacted",
            Redacted = id == "<redacted>",
            Metadata = new
            {
                operatorTempId = SafeDisplayName(tempId, "operator"),
                parameterName
            }
        });
    }

    private static IReadOnlyList<RuntimePreviewPilotCatalogItem> FixtureFallbackItems()
    {
        return
        [
            new RuntimePreviewPilotCatalogItem
            {
                Id = "fixture-camera",
                DisplayName = "Fixture camera metadata",
                ResourceType = "camera",
                Source = "fixture",
                SafeForPilot = false,
                ReasonCode = "runtime_preview_catalog_fixture_not_authoritative",
                Redacted = false
            },
            new RuntimePreviewPilotCatalogItem
            {
                Id = "fixture-template",
                DisplayName = "Fixture template metadata",
                ResourceType = "template",
                Source = "fixture",
                SafeForPilot = false,
                ReasonCode = "runtime_preview_catalog_fixture_not_authoritative",
                Redacted = false
            },
            new RuntimePreviewPilotCatalogItem
            {
                Id = "fixture-flow",
                DisplayName = "Fixture flow metadata",
                ResourceType = "flow",
                Source = "fixture",
                SafeForPilot = false,
                ReasonCode = "runtime_preview_catalog_fixture_not_authoritative",
                Redacted = false
            }
        ];
    }

    private static string SafeId(string? value)
    {
        var normalized = RuntimePreviewPilotConfig.NormalizeResourceKey(value);
        return normalized ?? "<redacted>";
    }

    private static string SafeDisplayName(string? value, string fallback)
    {
        var redacted = AiSecretSanitizer.Redact(value);
        if (string.IsNullOrWhiteSpace(redacted))
        {
            return fallback;
        }

        if (RuntimePreviewPilotConfig.LooksUnsafeResourceKey(redacted))
        {
            return "<redacted>";
        }

        return redacted.Trim();
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
