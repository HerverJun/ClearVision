using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ClearVision.Product.Core.AI.Tools;
using ClearVision.Product.Core.Cameras;
using ClearVision.Product.Core.DTOs;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Interfaces;
using ClearVision.Product.Core.Services;

namespace ClearVision.Product.Infrastructure.AI.Tools;

public sealed class NoOpVisionAgentStationStatusReader : IVisionAgentStationStatusReader
{
    public static readonly NoOpVisionAgentStationStatusReader Instance = new();

    private NoOpVisionAgentStationStatusReader()
    {
    }

    public Task<IReadOnlyList<VisionAgentStationStatus>> GetStationsAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<VisionAgentStationStatus>>(Array.Empty<VisionAgentStationStatus>());
    }
}

public sealed class CheckStationStatusTool : VisionAgentToolBase
{
    private readonly IVisionAgentStationStatusReader _stationStatusReader;

    public CheckStationStatusTool(IVisionAgentStationStatusReader stationStatusReader)
    {
        _stationStatusReader = stationStatusReader;
    }

    public override string Name => "check_station_status";
    public override string DisplayName => "Check station status";
    public override string Description => "Reads registered Station status. It does not send commands or restart stations.";
    public override string Category => "deployment";
    public override VisionAgentToolPermission Permission => VisionAgentToolPermission.ReadOnly;
    public override JsonElement ParametersSchema { get; } = Schema("""
        {
          "type": "object",
          "properties": {
            "targetStationId": { "type": "string" }
          }
        }
        """);

    public override async Task<VisionAgentToolResult> ExecuteAsync(
        VisionAgentToolContext context,
        JsonElement arguments,
        CancellationToken cancellationToken)
    {
        var targetStationId = ReadString(arguments, "targetStationId");
        var stations = await _stationStatusReader.GetStationsAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(targetStationId))
        {
            stations = stations
                .Where(item => string.Equals(item.StationId, targetStationId, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        return VisionAgentToolResult.Ok(new
        {
            stations,
            count = stations.Count
        });
    }
}

public sealed class RuntimePackagePrecheckTool : VisionAgentToolBase
{
    private readonly IAiFlowValidator _validator;
    private readonly ICameraManager _cameraManager;
    private readonly IConfigurationService _configurationService;
    private readonly IVisionAgentStationStatusReader _stationStatusReader;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new FlexibleStringDictionaryJsonConverter() }
    };

    public RuntimePackagePrecheckTool(
        IAiFlowValidator validator,
        ICameraManager cameraManager,
        IConfigurationService configurationService,
        IVisionAgentStationStatusReader stationStatusReader)
    {
        _validator = validator;
        _cameraManager = cameraManager;
        _configurationService = configurationService;
        _stationStatusReader = stationStatusReader;
    }

    public override string Name => "runtime_package_precheck";
    public override string DisplayName => "Runtime package precheck";
    public override string Description => "Builds a deployment readiness checklist. It does not export packages or deploy to Station.";
    public override string Category => "deployment";
    public override VisionAgentToolPermission Permission => VisionAgentToolPermission.DeploymentPrepare;
    public override JsonElement ParametersSchema { get; } = Schema("""
        {
          "type": "object",
          "properties": {
            "flow": { "type": "object" },
            "targetStationId": { "type": "string" },
            "validationSummary": { "type": "object" },
            "dryRunSummary": { "type": "object" },
            "replaySummary": { "type": "object" },
            "requireReplayForCameraFlow": { "type": "boolean" }
          }
        }
        """);

    public override async Task<VisionAgentToolResult> ExecuteAsync(
        VisionAgentToolContext context,
        JsonElement arguments,
        CancellationToken cancellationToken)
    {
        var blocking = new List<string>();
        var warnings = new List<string>();
        var requiredUserActions = new List<string>();
        AiGeneratedFlowJson? flow = TryReadFlow(arguments);

        if (flow == null)
        {
            blocking.Add("Flow payload is missing or cannot be parsed.");
            requiredUserActions.Add("Provide a valid final_flow payload before deployment precheck.");
        }
        else
        {
            var validation = _validator.Validate(flow);
            if (!validation.IsValid)
            {
                blocking.AddRange(validation.Errors.Select(error => $"validate_flow: {error}"));
                requiredUserActions.Add("Repair validation errors before preparing runtime package.");
            }

            AddResourceIssues(flow, blocking, warnings, requiredUserActions);
        }

        var config = _configurationService.GetCurrent();
        var bindings = _cameraManager.GetBindings();
        if (bindings.Count == 0)
        {
            bindings = config.Cameras;
        }

        if (bindings.Count == 0)
        {
            warnings.Add("No camera bindings are configured.");
        }

        if (flow != null)
        {
            AddCameraBindingIssues(flow, bindings, blocking, warnings, requiredUserActions);
            AddReplayRequirementIssues(flow, arguments, blocking, warnings, requiredUserActions);
        }

        AddProvidedValidationStateIssues(arguments, blocking, warnings, requiredUserActions);

        var targetStationId = ReadString(arguments, "targetStationId");
        if (!string.IsNullOrWhiteSpace(targetStationId))
        {
            var stations = await _stationStatusReader.GetStationsAsync(cancellationToken);
            var station = stations.FirstOrDefault(item =>
                string.Equals(item.StationId, targetStationId, StringComparison.OrdinalIgnoreCase));
            if (station == null)
            {
                blocking.Add($"Target Station '{targetStationId}' is not registered.");
                requiredUserActions.Add("Register or select an existing target Station.");
            }
            else if (!station.Online)
            {
                blocking.Add($"Target Station '{targetStationId}' is offline.");
                requiredUserActions.Add("Bring the target Station online before deployment.");
            }
        }
        else
        {
            warnings.Add("Target Station was not specified.");
            requiredUserActions.Add("Select targetStationId before package export or deploy.");
        }

        var action = new VisionAgentPendingAction
        {
            ActionType = "runtimePackagePrecheck.review",
            Title = "Review runtime package precheck",
            Summary = blocking.Count == 0
                ? "Runtime package precheck has no blocking issues."
                : $"Runtime package precheck found {blocking.Count} blocking issue(s).",
            Payload = new { ready = blocking.Count == 0, blockingIssues = blocking, warnings, requiredUserActions },
            RequiresUserConfirmation = true
        };

        return VisionAgentToolResult.Ok(new
        {
            ready = blocking.Count == 0,
            blockingIssues = blocking,
            warnings,
            requiredUserActions
        }, requiresUserConfirmation: true, pendingActions: [action]);
    }

    private static AiGeneratedFlowJson? TryReadFlow(JsonElement arguments)
    {
        try
        {
            var flowElement = ReadObjectOrSelf(arguments, "flow");
            return JsonSerializer.Deserialize<AiGeneratedFlowJson>(flowElement.GetRawText(), JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static void AddResourceIssues(
        AiGeneratedFlowJson flow,
        List<string> blocking,
        List<string> warnings,
        List<string> requiredUserActions)
    {
        foreach (var op in flow.Operators)
        {
            foreach (var parameter in op.Parameters)
            {
                var key = parameter.Key;
                var value = parameter.Value;
                if (key.Contains("ModelPath", StringComparison.OrdinalIgnoreCase) &&
                    string.IsNullOrWhiteSpace(value))
                {
                    blocking.Add($"{op.TempId}.{key} is missing.");
                    requiredUserActions.Add($"Provide model path for {op.TempId}.{key}.");
                }

                if ((key.Contains("Path", StringComparison.OrdinalIgnoreCase) ||
                     key.Contains("Calibration", StringComparison.OrdinalIgnoreCase)) &&
                    string.IsNullOrWhiteSpace(value))
                {
                    warnings.Add($"{op.TempId}.{key} is empty and needs engineer review.");
                }
            }
        }
    }

    private static void AddCameraBindingIssues(
        AiGeneratedFlowJson flow,
        IReadOnlyList<CameraBindingConfig> bindings,
        List<string> blocking,
        List<string> warnings,
        List<string> requiredUserActions)
    {
        foreach (var acquisition in flow.Operators.Where(op =>
                     string.Equals(op.OperatorType, "ImageAcquisition", StringComparison.OrdinalIgnoreCase)))
        {
            if ((!acquisition.Parameters.TryGetValue("CameraBindingId", out var bindingId) &&
                 !acquisition.Parameters.TryGetValue("CameraId", out bindingId)) ||
                string.IsNullOrWhiteSpace(bindingId))
            {
                blocking.Add($"{acquisition.TempId} ImageAcquisition is missing CameraBindingId.");
                requiredUserActions.Add($"Bind a camera for ImageAcquisition {acquisition.TempId}.");
                continue;
            }

            if (!bindings.Any(binding =>
                    string.Equals(binding.Id, bindingId, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(binding.SerialNumber, bindingId, StringComparison.OrdinalIgnoreCase)))
            {
                blocking.Add($"{acquisition.TempId} CameraBindingId '{bindingId}' is not configured.");
                requiredUserActions.Add($"Create or select camera binding '{bindingId}'.");
            }
        }

        if (!flow.Operators.Any(op => string.Equals(op.OperatorType, "ImageAcquisition", StringComparison.OrdinalIgnoreCase)))
        {
            warnings.Add("Flow has no ImageAcquisition operator.");
        }
    }

    private static void AddReplayRequirementIssues(
        AiGeneratedFlowJson flow,
        JsonElement arguments,
        List<string> blocking,
        List<string> warnings,
        List<string> requiredUserActions)
    {
        if (!flow.Operators.Any(op => string.Equals(op.OperatorType, "ImageAcquisition", StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        var requireReplay = ReadBool(arguments, "requireReplayForCameraFlow");
        var replaySummary = TryGetObject(arguments, "replaySummary");
        var replaySuccess = replaySummary.HasValue &&
            (ReadBool(replaySummary.Value, "replaySucceeded") ||
             ReadBool(replaySummary.Value, "isSuccess") ||
             ReadBool(replaySummary.Value, "IsSuccess"));

        if (replaySuccess)
        {
            return;
        }

        if (requireReplay)
        {
            blocking.Add("Camera flow requires a successful replay_flow_with_frame result before deployment precheck can pass.");
            requiredUserActions.Add("Run capture_test_frame and replay_flow_with_frame successfully for the selected camera entry.");
        }
        else
        {
            warnings.Add("Camera flow has no successful replay_flow_with_frame result.");
        }
    }

    private static void AddProvidedValidationStateIssues(
        JsonElement arguments,
        List<string> blocking,
        List<string> warnings,
        List<string> requiredUserActions)
    {
        AddSummaryIssues(arguments, "validationSummary", "validation", blocking, warnings, requiredUserActions);
        AddSummaryIssues(arguments, "dryRunSummary", "dryrun", blocking, warnings, requiredUserActions);
        AddSummaryIssues(arguments, "replaySummary", "replay", blocking, warnings, requiredUserActions);
    }

    private static void AddSummaryIssues(
        JsonElement arguments,
        string propertyName,
        string label,
        List<string> blocking,
        List<string> warnings,
        List<string> requiredUserActions)
    {
        var summary = TryGetObject(arguments, propertyName);
        if (!summary.HasValue)
        {
            return;
        }

        var valid = !TryReadBool(summary.Value, "isValid", out var isValid) &&
                    !TryReadBool(summary.Value, "valid", out isValid)
            ? (bool?)null
            : isValid;
        var success = !TryReadBool(summary.Value, "isSuccess", out var isSuccess) &&
                      !TryReadBool(summary.Value, "dryRunSucceeded", out isSuccess) &&
                      !TryReadBool(summary.Value, "replaySucceeded", out isSuccess)
            ? (bool?)null
            : isSuccess;

        if (valid == false || success == false)
        {
            blocking.Add($"{label} summary reports failure.");
            requiredUserActions.Add($"Resolve {label} failures before runtime package preparation.");
        }

        foreach (var issue in ReadStringArray(summary.Value, "errors").Concat(ReadStringArray(summary.Value, "blockingIssues")))
        {
            blocking.Add($"{label}: {issue}");
        }

        foreach (var warning in ReadStringArray(summary.Value, "warnings"))
        {
            warnings.Add($"{label}: {warning}");
        }
    }

    private static JsonElement? TryGetObject(JsonElement arguments, string propertyName)
    {
        if (arguments.ValueKind == JsonValueKind.Object &&
            arguments.TryGetProperty(propertyName, out var value) &&
            value.ValueKind == JsonValueKind.Object)
        {
            return value;
        }

        return null;
    }

    private static bool TryReadBool(JsonElement arguments, string propertyName, out bool value)
    {
        value = false;
        if (arguments.ValueKind != JsonValueKind.Object ||
            !arguments.TryGetProperty(propertyName, out var element))
        {
            return false;
        }

        if (element.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            value = element.GetBoolean();
            return true;
        }

        if (element.ValueKind == JsonValueKind.String &&
            bool.TryParse(element.GetString(), out var parsed))
        {
            value = parsed;
            return true;
        }

        return false;
    }

    private static IReadOnlyList<string> ReadStringArray(JsonElement arguments, string propertyName)
    {
        if (arguments.ValueKind != JsonValueKind.Object ||
            !arguments.TryGetProperty(propertyName, out var value) ||
            value.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<string>();
        }

        return value.EnumerateArray()
            .Select(item => item.ValueKind == JsonValueKind.String ? item.GetString() : item.GetRawText())
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item!)
            .ToList();
    }
}

public sealed class RuntimePackageManifestDraftTool : VisionAgentToolBase
{
    public override string Name => "draft_runtime_package_manifest";
    public override string DisplayName => "Draft runtime package manifest";
    public override string Description => "Creates a runtime package manifest draft only. It does not write to package directories or deploy.";
    public override string Category => "deployment";
    public override VisionAgentToolPermission Permission => VisionAgentToolPermission.ConfigDraft;
    public override JsonElement ParametersSchema { get; } = Schema("""
        {
          "type": "object",
          "properties": {
            "packageName": { "type": "string" },
            "flow": { "type": "object" },
            "targetStationId": { "type": "string" }
          }
        }
        """);

    public override Task<VisionAgentToolResult> ExecuteAsync(
        VisionAgentToolContext context,
        JsonElement arguments,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var flowRaw = arguments.ValueKind == JsonValueKind.Object &&
                      arguments.TryGetProperty("flow", out var flowElement)
            ? flowElement.GetRawText()
            : "{}";
        var packageName = ReadString(arguments, "packageName") ?? BuildPackageName(context.UserDescription);
        var manifest = new
        {
            packageName,
            flowHash = ComputeHash(flowRaw),
            targetStationId = ReadString(arguments, "targetStationId"),
            requiredModels = ExtractRequiredValues(flowRaw, "ModelPath"),
            requiredCameraBindings = ExtractRequiredValues(flowRaw, "CameraBindingId"),
            requiredPlcConnections = ExtractPlcOperators(flowRaw),
            pendingApprovals = new[]
            {
                "Engineer must review all pending parameters.",
                "Engineer must confirm camera/PLC bindings.",
                "Engineer must run runtime_package_precheck before export/deploy."
            },
            draftOnly = true
        };

        var action = new VisionAgentPendingAction
        {
            ActionType = "runtimePackageManifestDraft.review",
            Title = "Review runtime package manifest draft",
            Summary = $"Draft manifest '{packageName}' is ready for review.",
            Payload = manifest,
            RequiresUserConfirmation = true
        };

        return Task.FromResult(VisionAgentToolResult.Ok(
            new { manifest, requiresUserConfirmation = true },
            requiresUserConfirmation: true,
            pendingActions: [action]));
    }

    private static string BuildPackageName(string description)
    {
        var normalized = new string((description ?? string.Empty)
            .ToLowerInvariant()
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : '-')
            .ToArray());
        normalized = string.Join("-", normalized.Split('-', StringSplitOptions.RemoveEmptyEntries).Take(6));
        return string.IsNullOrWhiteSpace(normalized)
            ? $"vision-agent-package-{DateTime.UtcNow:yyyyMMddHHmmss}"
            : normalized;
    }

    private static string ComputeHash(string text)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(text ?? string.Empty));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static List<string> ExtractRequiredValues(string flowRaw, string parameterName)
    {
        try
        {
            using var doc = JsonDocument.Parse(flowRaw);
            return doc.RootElement.GetRawText().Contains(parameterName, StringComparison.OrdinalIgnoreCase)
                ? FindStringValues(doc.RootElement, parameterName).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
                : new List<string>();
        }
        catch (JsonException)
        {
            return new List<string>();
        }
    }

    private static List<string> ExtractPlcOperators(string flowRaw)
    {
        try
        {
            using var doc = JsonDocument.Parse(flowRaw);
            return FindStringValues(doc.RootElement, "operatorType")
                .Where(value => value.Contains("Communication", StringComparison.OrdinalIgnoreCase) ||
                                value.Contains("Plc", StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (JsonException)
        {
            return new List<string>();
        }
    }

    private static IEnumerable<string> FindStringValues(JsonElement element, string propertyName)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase) &&
                    property.Value.ValueKind == JsonValueKind.String &&
                    !string.IsNullOrWhiteSpace(property.Value.GetString()))
                {
                    yield return property.Value.GetString()!;
                }

                foreach (var nested in FindStringValues(property.Value, propertyName))
                {
                    yield return nested;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                foreach (var nested in FindStringValues(item, propertyName))
                {
                    yield return nested;
                }
            }
        }
    }
}

