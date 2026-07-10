using System.Text.Json;
using ClearVision.Product.Core.AI.Tools;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Services;

namespace ClearVision.Product.Infrastructure.AI.Tools;

public sealed class RuntimePackagePrecheckTool : VisionAgentToolBase
{
    private readonly IVisionAgentStationStatusReader _stationStatusReader;

    public RuntimePackagePrecheckTool()
        : this(new NoOpVisionAgentStationStatusReader())
    {
    }

    public RuntimePackagePrecheckTool(IVisionAgentStationStatusReader stationStatusReader)
    {
        _stationStatusReader = stationStatusReader;
    }

    public override string Name => "runtime_package_precheck";
    public override string DisplayName => "运行包预检";
    public override string Description => "仅用静态草稿检查部署就绪度，不打包、不加载、不触碰运行资源。";
    public override string Category => "deployment";
    public override VisionAgentToolPermission Permission => VisionAgentToolPermission.DeploymentPrepare;
    public override JsonElement ParametersSchema { get; } = Schema("""
        {
          "type": "object",
          "properties": {
            "flow": { "type": ["object", "string"] },
            "flowJson": { "type": "string" },
            "validationSummary": { "type": "object" },
            "dryRunSummary": { "type": "object" },
            "targetStationId": { "type": "string" },
            "requireReplay": { "type": "boolean" },
            "replaySummary": { "type": "object" },
            "manualResourceConfirmations": { "type": "array" }
          }
        }
        """);

    public override async Task<VisionAgentToolResult> ExecuteAsync(
        VisionAgentToolContext context,
        JsonElement arguments,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var normalized = VisionAgentFlowDraftNormalizer.Normalize(arguments, context);
        if (!normalized.Success)
        {
            return VisionAgentToolResult.Fail(
                normalized.ErrorCode ?? "invalid_flow",
                normalized.ErrorMessage ?? "Flow draft could not be normalized.");
        }

        var contractValidation = VisionAgentFlowDraftValidator.Validate(normalized.Flow);
        var flow = contractValidation.Flow;
        var blockingIssues = new List<PrecheckIssue>();
        var warnings = new List<PrecheckIssue>();
        var missingResources = new List<PrecheckMissingResource>();

        MergeValidationSummary(arguments, contractValidation, blockingIssues, warnings, missingResources);
        AddDeploymentResourceChecks(flow, warnings, missingResources);
        AddDeploymentConstraintChecks(flow, blockingIssues);
        var manualConfirmationCount = AddManualConfirmationChecks(flow, arguments, warnings, missingResources);
        CheckDryRun(arguments, blockingIssues, warnings);
        CheckReplay(arguments, blockingIssues);
        await CheckTargetStationAsync(arguments, blockingIssues, warnings, cancellationToken);

        var pendingActions = BuildPendingActions(missingResources, blockingIssues);
        var readyForDeployment = blockingIssues.Count == 0 && missingResources.Count == 0;
        var data = new
        {
            source = "deployment_prepare_static_precheck",
            readyForDeployment,
            workflowDraftAllowed = true,
            deploymentBlocked = !readyForDeployment,
            blockingIssues = blockingIssues.Select(IssuePayload).ToList(),
            warnings = warnings.Select(IssuePayload).ToList(),
            missingResources = missingResources.Select(ResourcePayload).ToList(),
            pendingActions = pendingActions.Select(PendingActionPayload).ToList(),
            precheckSummary = new
            {
                summary = readyForDeployment
                    ? "Draft flow passed deployment precheck with static inputs only."
                    : "Draft flow can remain as workflow draft, but deployment is blocked until issues are resolved.",
                blockingIssueCount = blockingIssues.Count,
                warningCount = warnings.Count,
                missingResourceCount = missingResources.Count,
                manualConfirmationCount
            },
            manualConfirmationRequired = true,
            manualConfirmationCount,
            metadataOnly = true,
            deployed = false,
            packageCreated = false,
            stationTouched = false
        };

        return VisionAgentToolResult.Ok(data, pendingActions: pendingActions);
    }

    private static void MergeValidationSummary(
        JsonElement arguments,
        VisionAgentFlowValidation validation,
        List<PrecheckIssue> blockingIssues,
        List<PrecheckIssue> warnings,
        List<PrecheckMissingResource> missingResources)
    {
        if (TryGetProperty(arguments, "validationSummary", out var validationSummary) &&
            validationSummary.ValueKind == JsonValueKind.Object)
        {
            blockingIssues.AddRange(ReadIssues(validationSummary, "blockingIssues"));
            warnings.AddRange(ReadIssues(validationSummary, "warnings"));
            missingResources.AddRange(ReadMissingResources(validationSummary, "missingResources"));
            return;
        }

        blockingIssues.AddRange(validation.BlockingIssues.Select(issue => new PrecheckIssue(
            issue.Code,
            issue.Message,
            issue.TempId,
            issue.OperatorType)));
        warnings.AddRange(validation.Warnings.Select(issue => new PrecheckIssue(
            issue.Code,
            issue.Message,
            issue.TempId,
            issue.OperatorType)));
        missingResources.AddRange(validation.MissingResources.Select(resource => new PrecheckMissingResource(
            resource.ResourceKind,
            resource.ParameterName,
            resource.TempId,
            resource.OperatorType,
            resource.Message)));
    }

    private static void AddDeploymentResourceChecks(
        VisionAgentFlowDraft flow,
        List<PrecheckIssue> warnings,
        List<PrecheckMissingResource> missingResources)
    {
        foreach (var resource in VisionAgentParameterRuleCenter.CollectMissingResources(
                     flow,
                     VisionAgentParameterRuleScope.DeploymentPrecheck))
        {
            AddMissingResource(
                warnings,
                missingResources,
                resource.ResourceKind,
                resource.ParameterName,
                resource.TempId,
                resource.OperatorType,
                resource.Message);
        }
    }

    private static void AddDeploymentConstraintChecks(
        VisionAgentFlowDraft flow,
        List<PrecheckIssue> blockingIssues)
    {
        foreach (var issue in VisionAgentParameterRuleCenter.CollectConstraintViolations(flow))
        {
            var violation = issue.Violation;
            if ((violation.Code is "required" or "at-least-one") &&
                !string.IsNullOrWhiteSpace(violation.ResourceKind))
            {
                continue;
            }

            var code = violation.Code switch
            {
                "at-least-one" => "missing_conditional_parameter_group",
                "mutually-exclusive" => "mutually_exclusive_parameters",
                _ => "missing_conditional_parameter"
            };
            if (blockingIssues.Any(existing =>
                    string.Equals(existing.Code, code, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(existing.TempId, issue.TempId, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var parameterNames = string.Join(", ", violation.ParameterNames);
            var message = violation.Code switch
            {
                "at-least-one" => $"{issue.OperatorType} requires at least one of {parameterNames} before deployment.",
                "mutually-exclusive" => $"{issue.OperatorType} parameters {parameterNames} cannot be configured together.",
                _ => $"{issue.OperatorType}.{parameterNames} is required for the active parameter mode."
            };
            blockingIssues.Add(new PrecheckIssue(code, message, issue.TempId, issue.OperatorType));
        }
    }

    private static int AddManualConfirmationChecks(
        VisionAgentFlowDraft flow,
        JsonElement arguments,
        List<PrecheckIssue> warnings,
        List<PrecheckMissingResource> missingResources)
    {
        var confirmations = ReadManualConfirmations(arguments);
        foreach (var resource in CollectConfiguredDeploymentResources(flow))
        {
            if (missingResources.Any(item =>
                    string.Equals(item.TempId, resource.TempId, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(item.ParameterName, resource.ParameterName, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            if (HasMetadataOnlyManualConfirmation(confirmations, resource))
            {
                continue;
            }

            AddMissingResource(
                warnings,
                missingResources,
                resource.ResourceKind,
                resource.ParameterName,
                resource.TempId,
                resource.OperatorType,
                $"{resource.OperatorType}.{resource.ParameterName} requires metadata-only manual confirmation before deployment.");
        }

        return confirmations.Count;
    }

    private static IReadOnlyList<DeploymentResourceRequirement> CollectConfiguredDeploymentResources(
        VisionAgentFlowDraft flow)
    {
        var resources = new List<DeploymentResourceRequirement>();
        var contractCatalog = new VisionAgentOperatorContractCatalog();
        foreach (var op in flow.Operators)
        {
            if (contractCatalog.TryGet(op.OperatorType, out var contract) &&
                contract.ParameterConstraints is { Count: > 0 } constraints)
            {
                var metadata = new OperatorMetadata
                {
                    Parameters = contract.Parameters.Select(parameter => new ParameterDefinition
                    {
                        Name = parameter.Name,
                        IsRequired = parameter.IsRequired,
                        DefaultValue = parameter.DefaultValue
                    }).ToList(),
                    ParameterConstraints = constraints.ToList()
                };
                var values = op.Parameters.ToDictionary(
                    pair => pair.Key,
                    pair => (object?)pair.Value,
                    StringComparer.Ordinal);
                var canonicalization = OperatorParameterConstraintEvaluator.Canonicalize(metadata, values);
                foreach (var state in OperatorParameterConstraintEvaluator.ResolveStates(metadata, values)
                             .Where(item =>
                                 !item.EffectiveDisabled &&
                                 string.IsNullOrWhiteSpace(item.Constraint.AliasFor) &&
                                 RequiresManualConfirmation(item.Constraint.ResourceKind)))
                {
                    if (!canonicalization.ExplicitValues.TryGetValue(state.Constraint.Parameter, out var value) ||
                        OperatorParameterValueSemantics.IsMissing(value) ||
                        IsInactiveResourceSwitch(value))
                    {
                        continue;
                    }

                    resources.Add(new DeploymentResourceRequirement(
                        state.Constraint.ResourceKind!,
                        state.Constraint.Parameter,
                        op.TempId,
                        op.OperatorType));
                }

                continue;
            }

            if (IsOperatorType(op, "OnnxInference") ||
                IsOperatorType(op, "SemanticSegmentation") ||
                IsOperatorType(op, "AnomalyDetection"))
            {
                AddIfConfigured(resources, op, "model_resource", ["ModelPath", "ModelId", "ModelCatalogPath"]);
            }

            if (IsOperatorType(op, "TemplateMatching"))
            {
                AddIfConfigured(resources, op, "template_artifact", ["TemplatePath", "TemplateId", "Template"]);
            }

            if (IsOperatorType(op, "UnitConvert"))
            {
                AddIfConfigured(resources, op, "measurement_parameter", ["Scale", "PixelScale", "CalibrationScale"]);
            }

            if (op.OperatorType.Contains("Plc", StringComparison.OrdinalIgnoreCase))
            {
                AddIfConfigured(resources, op, "plc_address", ["PlcAddress", "PLCParameters"]);
            }
        }

        return resources
            .GroupBy(item => $"{item.ResourceKind}|{item.TempId}|{item.ParameterName}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
    }

    private static bool RequiresManualConfirmation(string? resourceKind)
    {
        return resourceKind is
            "camera_binding" or
            "model_resource" or
            "output_file" or
            "plc_endpoint" or
            "plc_address" or
            "tcp_profile" or
            "network_endpoint";
    }

    private static bool IsInactiveResourceSwitch(object? value)
    {
        return value is bool boolean
            ? !boolean
            : bool.TryParse(value?.ToString(), out var parsed) && !parsed;
    }

    private static void AddIfConfigured(
        List<DeploymentResourceRequirement> resources,
        VisionAgentFlowOperator op,
        string resourceKind,
        IReadOnlyList<string> parameterNames)
    {
        foreach (var parameterName in parameterNames)
        {
            var value = ReadParameter(op, parameterName);
            if (IsMissingParameterValue(value))
            {
                continue;
            }

            resources.Add(new DeploymentResourceRequirement(
                resourceKind,
                parameterName,
                op.TempId,
                op.OperatorType));
            return;
        }
    }

    private static IReadOnlyList<ManualResourceConfirmation> ReadManualConfirmations(JsonElement arguments)
    {
        var confirmations = new List<ManualResourceConfirmation>();
        ReadConfirmationArray(arguments, confirmations);

        if (TryGetProperty(arguments, "flow", out var flow))
        {
            if (flow.ValueKind == JsonValueKind.Object)
            {
                ReadConfirmationArray(flow, confirmations);
            }
            else if (flow.ValueKind == JsonValueKind.String &&
                     !string.IsNullOrWhiteSpace(flow.GetString()))
            {
                try
                {
                    using var doc = JsonDocument.Parse(flow.GetString()!);
                    ReadConfirmationArray(doc.RootElement, confirmations);
                }
                catch (JsonException)
                {
                    // Flow parse errors are handled by the normalizer; confirmation parsing stays best-effort.
                }
            }
        }

        return confirmations;
    }

    private static void ReadConfirmationArray(
        JsonElement root,
        List<ManualResourceConfirmation> confirmations)
    {
        foreach (var propertyName in new[]
                 {
                     "manualResourceConfirmations",
                     "manualConfirmations",
                     "resourceConfirmations"
                 })
        {
            if (!TryGetProperty(root, propertyName, out var value) ||
                value.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            confirmations.AddRange(value.EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.Object)
                .Select(item => new ManualResourceConfirmation(
                    ReadStringProperty(item, "resourceType") ??
                    ReadStringProperty(item, "resourceKind") ??
                    string.Empty,
                    ReadStringProperty(item, "operatorId") ??
                    ReadStringProperty(item, "actualOperatorId") ??
                    ReadStringProperty(item, "tempId") ??
                    string.Empty,
                    ReadStringProperty(item, "parameterName") ?? string.Empty,
                    ReadStringProperty(item, "resourceKey") ??
                    ReadStringProperty(item, "resourceRef") ??
                    string.Empty,
                    ReadBool(item, "metadataOnly") == true)));
        }
    }

    private static bool HasMetadataOnlyManualConfirmation(
        IReadOnlyList<ManualResourceConfirmation> confirmations,
        DeploymentResourceRequirement resource)
    {
        var resourceKey = $"{resource.TempId}.{resource.ParameterName}";
        return confirmations.Any(confirmation =>
            confirmation.MetadataOnly &&
            (string.IsNullOrWhiteSpace(confirmation.ResourceType) ||
             string.Equals(confirmation.ResourceType, resource.ResourceKind, StringComparison.OrdinalIgnoreCase)) &&
            (ResourceKeysMatch(confirmation.ResourceKey, resource, resourceKey) ||
             (string.Equals(confirmation.OperatorId, resource.TempId, StringComparison.OrdinalIgnoreCase) &&
              ParameterNamesMatch(
                  resource.OperatorType,
                  confirmation.ParameterName,
                  resource.ParameterName))));
    }

    private static bool ResourceKeysMatch(
        string confirmationKey,
        DeploymentResourceRequirement resource,
        string canonicalResourceKey)
    {
        if (string.Equals(confirmationKey, canonicalResourceKey, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var separatorIndex = confirmationKey.LastIndexOf('.');
        if (separatorIndex <= 0 || separatorIndex >= confirmationKey.Length - 1)
        {
            return false;
        }

        return string.Equals(
                   confirmationKey[..separatorIndex],
                   resource.TempId,
                   StringComparison.OrdinalIgnoreCase) &&
               ParameterNamesMatch(
                   resource.OperatorType,
                   confirmationKey[(separatorIndex + 1)..],
                   resource.ParameterName);
    }

    private static bool ParameterNamesMatch(string operatorType, string left, string right)
    {
        return string.Equals(
            CanonicalizeParameterName(operatorType, left),
            CanonicalizeParameterName(operatorType, right),
            StringComparison.OrdinalIgnoreCase);
    }

    private static string CanonicalizeParameterName(string operatorType, string parameterName)
    {
        if (!Enum.TryParse<OperatorType>(operatorType, ignoreCase: true, out var parsedType))
        {
            return parameterName;
        }

        var constraint = OperatorParameterConstraintProvider.Instance
            .GetConstraints(parsedType)
            .FirstOrDefault(item =>
                item.Parameter.Equals(parameterName, StringComparison.Ordinal) &&
                !string.IsNullOrWhiteSpace(item.AliasFor));
        return constraint?.AliasFor ?? parameterName;
    }

    private static string? ReadParameter(
        VisionAgentFlowOperator op,
        string parameterName)
    {
        return op.Parameters.TryGetValue(parameterName, out var value) ? value : null;
    }

    private static bool IsOperatorType(VisionAgentFlowOperator op, string operatorType)
    {
        return string.Equals(op.OperatorType, operatorType, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsFileSource(string? sourceType)
    {
        return string.Equals(sourceType, "file", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(sourceType, "image", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(sourceType, "path", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsMissingParameterValue(string? value)
    {
        return OperatorParameterValueSemantics.IsMissing(value);
    }

    private static void CheckDryRun(
        JsonElement arguments,
        List<PrecheckIssue> blockingIssues,
        List<PrecheckIssue> warnings)
    {
        if (!TryGetProperty(arguments, "dryRunSummary", out var dryRunSummary) ||
            dryRunSummary.ValueKind != JsonValueKind.Object)
        {
            blockingIssues.Add(new PrecheckIssue(
                "dryrun_missing",
                "Deployment precheck requires a structure-only dryrun summary."));
            return;
        }

        if (ReadBool(dryRunSummary, "dryRunSucceeded") == false)
        {
            blockingIssues.Add(new PrecheckIssue(
                "dryrun_failed",
                "Dryrun summary reports failure."));
        }

        warnings.AddRange(ReadIssues(dryRunSummary, "warnings"));
        blockingIssues.AddRange(ReadIssues(dryRunSummary, "blockingIssues"));
    }

    private static void CheckReplay(
        JsonElement arguments,
        List<PrecheckIssue> blockingIssues)
    {
        if (ReadBool(arguments, "requireReplay") != true)
        {
            return;
        }

        if (!TryGetProperty(arguments, "replaySummary", out var replaySummary) ||
            replaySummary.ValueKind != JsonValueKind.Object)
        {
            blockingIssues.Add(new PrecheckIssue(
                "replay_required",
                "Replay is required before deployment, but replaySummary is missing."));
            return;
        }

        if (ReadBool(replaySummary, "success") == false ||
            ReadBool(replaySummary, "replaySucceeded") == false)
        {
            blockingIssues.Add(new PrecheckIssue(
                "replay_failed",
                "Replay summary reports failure."));
        }
    }

    private async Task CheckTargetStationAsync(
        JsonElement arguments,
        List<PrecheckIssue> blockingIssues,
        List<PrecheckIssue> warnings,
        CancellationToken cancellationToken)
    {
        var targetStationId = ReadStringProperty(arguments, "targetStationId");
        if (string.IsNullOrWhiteSpace(targetStationId))
        {
            warnings.Add(new PrecheckIssue(
                "target_station_missing",
                "targetStationId is not set; workflow draft is still allowed."));
            return;
        }

        var status = await _stationStatusReader.TryReadAsync(targetStationId, cancellationToken);
        if (status == null)
        {
            blockingIssues.Add(new PrecheckIssue(
                "station_status_unavailable",
                $"No status is available for targetStationId '{targetStationId}'.",
                targetStationId));
            return;
        }

        if (!status.IsOnline)
        {
            blockingIssues.Add(new PrecheckIssue(
                "station_offline",
                $"Target station '{targetStationId}' is not online.",
                targetStationId));
        }
    }

    private static IReadOnlyList<VisionAgentPendingAction> BuildPendingActions(
        IReadOnlyList<PrecheckMissingResource> missingResources,
        IReadOnlyList<PrecheckIssue> blockingIssues)
    {
        var actions = missingResources.Select(resource => new VisionAgentPendingAction
        {
            ActionType = "provide_missing_resource",
            Title = $"Provide {resource.ParameterName}",
            Summary = resource.Message,
            Payload = ResourcePayload(resource),
            RequiresUserConfirmation = true
        }).ToList();

        actions.AddRange(blockingIssues
            .Where(issue => string.Equals(issue.Code, "dryrun_missing", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(issue.Code, "replay_required", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(issue.Code, "station_status_unavailable", StringComparison.OrdinalIgnoreCase))
            .Select(issue => new VisionAgentPendingAction
            {
                ActionType = "resolve_deployment_blocker",
                Title = issue.Code,
                Summary = issue.Message,
                Payload = IssuePayload(issue),
                RequiresUserConfirmation = true
            }));

        return actions;
    }

    private static void AddMissingResource(
        List<PrecheckIssue> warnings,
        List<PrecheckMissingResource> missingResources,
        string resourceKind,
        string parameterName,
        string tempId,
        string operatorType,
        string message)
    {
        if (missingResources.Any(resource =>
                string.Equals(resource.TempId, tempId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(resource.ParameterName, parameterName, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        missingResources.Add(new PrecheckMissingResource(
            resourceKind,
            parameterName,
            tempId,
            operatorType,
            message));
        warnings.Add(new PrecheckIssue(
            "missing_resource",
            message,
            tempId,
            operatorType));
    }

    private static IEnumerable<PrecheckIssue> ReadIssues(JsonElement root, string propertyName)
    {
        if (!TryGetProperty(root, propertyName, out var issues) ||
            issues.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return issues.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.Object)
            .Select(item => new PrecheckIssue(
                ReadStringProperty(item, "code") ?? "issue",
                ReadStringProperty(item, "message") ?? item.GetRawText(),
                ReadStringProperty(item, "tempId"),
                ReadStringProperty(item, "operatorType")))
            .ToList();
    }

    private static IEnumerable<PrecheckMissingResource> ReadMissingResources(JsonElement root, string propertyName)
    {
        if (!TryGetProperty(root, propertyName, out var resources) ||
            resources.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return resources.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.Object)
            .Select(item => new PrecheckMissingResource(
                ReadStringProperty(item, "resourceKind") ?? "resource",
                ReadStringProperty(item, "parameterName") ?? "parameter",
                ReadStringProperty(item, "tempId") ?? string.Empty,
                ReadStringProperty(item, "operatorType") ?? string.Empty,
                ReadStringProperty(item, "message") ?? item.GetRawText()))
            .ToList();
    }

    private static object IssuePayload(PrecheckIssue issue)
    {
        return new
        {
            code = issue.Code,
            message = issue.Message,
            tempId = issue.TempId,
            operatorType = issue.OperatorType
        };
    }

    private static object ResourcePayload(PrecheckMissingResource resource)
    {
        return new
        {
            resourceKind = resource.ResourceKind,
            parameterName = resource.ParameterName,
            tempId = resource.TempId,
            operatorType = resource.OperatorType,
            message = resource.Message
        };
    }

    private static object PendingActionPayload(VisionAgentPendingAction action)
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

    private static string? ReadStringProperty(JsonElement element, string propertyName)
    {
        return element.ValueKind == JsonValueKind.Object &&
               TryGetProperty(element, propertyName, out var value) &&
               value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static bool? ReadBool(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            !TryGetProperty(element, propertyName, out var value))
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

    private sealed record PrecheckIssue(
        string Code,
        string Message,
        string? TempId = null,
        string? OperatorType = null);

    private sealed record PrecheckMissingResource(
        string ResourceKind,
        string ParameterName,
        string TempId,
        string OperatorType,
        string Message);

    private sealed record DeploymentResourceRequirement(
        string ResourceKind,
        string ParameterName,
        string TempId,
        string OperatorType);

    private sealed record ManualResourceConfirmation(
        string ResourceType,
        string OperatorId,
        string ParameterName,
        string ResourceKey,
        bool MetadataOnly);
}
