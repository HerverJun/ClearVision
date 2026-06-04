using System.Text.Json;
using ClearVision.Product.Core.AI.Tools;

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
    public override string DisplayName => "Runtime package precheck";
    public override string Description => "Checks whether a draft flow is ready for deployment without packaging, loading, or touching runtime resources.";
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
            "replaySummary": { "type": "object" }
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

        var blockingIssues = new List<PrecheckIssue>();
        var warnings = new List<PrecheckIssue>();
        var missingResources = new List<PrecheckMissingResource>();

        MergeValidationSummary(arguments, normalized.Flow, blockingIssues, warnings, missingResources);
        AddDeploymentResourceChecks(normalized.Flow, warnings, missingResources);
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
                missingResourceCount = missingResources.Count
            },
            deployed = false,
            packageCreated = false,
            stationTouched = false
        };

        return VisionAgentToolResult.Ok(data, pendingActions: pendingActions);
    }

    private static void MergeValidationSummary(
        JsonElement arguments,
        VisionAgentFlowDraft flow,
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

        var validation = VisionAgentFlowDraftValidator.Validate(flow);
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
        foreach (var op in flow.Operators)
        {
            if (string.Equals(op.OperatorType, "ResultOutput", StringComparison.OrdinalIgnoreCase) &&
                IsMissingParameter(op.Parameters, "Channel"))
            {
                AddMissingResource(
                    warnings,
                    missingResources,
                    "output_channel",
                    "Channel",
                    op.TempId,
                    op.OperatorType,
                    "ResultOutput.Channel is not configured.");
            }

            if (op.OperatorType.Contains("Plc", StringComparison.OrdinalIgnoreCase) &&
                HasMissingPlcParameters(op.Parameters))
            {
                AddMissingResource(
                    warnings,
                    missingResources,
                    "plc_parameters",
                    "PLCParameters",
                    op.TempId,
                    op.OperatorType,
                    $"{op.OperatorType} PLC parameters are missing or pending.");
            }
        }
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

    private static bool HasMissingPlcParameters(IReadOnlyDictionary<string, string?> parameters)
    {
        return parameters.Count == 0 ||
               parameters.Any(parameter =>
                   string.IsNullOrWhiteSpace(parameter.Value) ||
                   parameter.Value.StartsWith("<pending", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsMissingParameter(
        IReadOnlyDictionary<string, string?> parameters,
        string parameterName)
    {
        return !parameters.TryGetValue(parameterName, out var value) ||
               string.IsNullOrWhiteSpace(value) ||
               value.StartsWith("<pending", StringComparison.OrdinalIgnoreCase);
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
}
