using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ClearVision.Product.Core.AI.Tools;
using ClearVision.Product.Core.Cameras;
using ClearVision.Product.Core.DTOs;
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
            "targetStationId": { "type": "string" }
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
        AiGeneratedFlowJson? flow = TryReadFlow(arguments);

        if (flow == null)
        {
            blocking.Add("Flow payload is missing or cannot be parsed.");
        }
        else
        {
            var validation = _validator.Validate(flow);
            if (!validation.IsValid)
            {
                blocking.AddRange(validation.Errors.Select(error => $"validate_flow: {error}"));
            }

            AddResourceIssues(flow, blocking, warnings);
            AddCameraBindingIssues(flow, blocking, warnings);
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

        var targetStationId = ReadString(arguments, "targetStationId");
        if (!string.IsNullOrWhiteSpace(targetStationId))
        {
            var stations = await _stationStatusReader.GetStationsAsync(cancellationToken);
            var station = stations.FirstOrDefault(item =>
                string.Equals(item.StationId, targetStationId, StringComparison.OrdinalIgnoreCase));
            if (station == null)
            {
                blocking.Add($"Target Station '{targetStationId}' is not registered.");
            }
            else if (!station.Online)
            {
                blocking.Add($"Target Station '{targetStationId}' is offline.");
            }
        }
        else
        {
            warnings.Add("Target Station was not specified.");
        }

        var action = new VisionAgentPendingAction
        {
            ActionType = "runtimePackagePrecheck.review",
            Title = "Review runtime package precheck",
            Summary = blocking.Count == 0
                ? "Runtime package precheck has no blocking issues."
                : $"Runtime package precheck found {blocking.Count} blocking issue(s).",
            Payload = new { ready = blocking.Count == 0, blockingIssues = blocking, warnings },
            RequiresUserConfirmation = true
        };

        return VisionAgentToolResult.Ok(new
        {
            ready = blocking.Count == 0,
            blockingIssues = blocking,
            warnings
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

    private static void AddResourceIssues(AiGeneratedFlowJson flow, List<string> blocking, List<string> warnings)
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

    private static void AddCameraBindingIssues(AiGeneratedFlowJson flow, List<string> blocking, List<string> warnings)
    {
        foreach (var acquisition in flow.Operators.Where(op =>
                     string.Equals(op.OperatorType, "ImageAcquisition", StringComparison.OrdinalIgnoreCase)))
        {
            if (!acquisition.Parameters.TryGetValue("CameraBindingId", out var bindingId) ||
                string.IsNullOrWhiteSpace(bindingId))
            {
                blocking.Add($"{acquisition.TempId} ImageAcquisition is missing CameraBindingId.");
            }
        }

        if (!flow.Operators.Any(op => string.Equals(op.OperatorType, "ImageAcquisition", StringComparison.OrdinalIgnoreCase)))
        {
            warnings.Add("Flow has no ImageAcquisition operator.");
        }
    }
}

public sealed class RuntimePackageManifestDraftTool : VisionAgentToolBase
{
    public override string Name => "draft_runtime_package_manifest";
    public override string DisplayName => "Draft runtime package manifest";
    public override string Description => "Creates a runtime package manifest draft only. It does not write to package directories or deploy.";
    public override string Category => "deployment";
    public override VisionAgentToolPermission Permission => VisionAgentToolPermission.DeploymentPrepare;
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

