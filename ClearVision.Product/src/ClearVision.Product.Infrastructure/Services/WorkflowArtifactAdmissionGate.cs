using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using ClearVision.Product.Application.DTOs;
using ClearVision.Product.Application.Services;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.DTOs;
using ClearVision.Product.Core.Services;
using ClearVision.Product.Core.ValueObjects;
using ClearVision.Product.Infrastructure.AI.Agent;
using Microsoft.Extensions.Logging;

namespace ClearVision.Product.Infrastructure.Services;

public sealed class WorkflowLegacyScanner
{
    private static readonly IReadOnlyDictionary<string, string> UnambiguousAliases =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["MeasureDistance"] = nameof(OperatorType.Measurement),
            ["TemplateMatch"] = nameof(OperatorType.TemplateMatching)
        };

    private readonly IOperatorFactory _operatorFactory;

    public WorkflowLegacyScanner(IOperatorFactory operatorFactory)
    {
        _operatorFactory = operatorFactory;
    }

    public ScanResult Scan(
        OperatorFlowDto flow,
        string? originalSnapshot,
        bool allowHistoricalDisabledOperators = false)
    {
        var diagnostics = new List<WorkflowArtifactDiagnostic>();
        var repairs = new List<WorkflowArtifactRepair>();
        var operators = flow.Operators ?? [];
        var operatorIds = new HashSet<Guid>();
        var portIds = new HashSet<Guid>();
        var parameterIds = new HashSet<Guid>();

        if (flow.Id == Guid.Empty)
        {
            repairs.Add(new("missing_flow_id", "Assigned a new stable workflow ID."));
        }

        if (string.IsNullOrWhiteSpace(flow.Name))
        {
            repairs.Add(new("missing_flow_name", "Assigned a stable default workflow name."));
        }

        if (operators.Count == 0)
        {
            diagnostics.Add(new("missing_operators", "Workflow must contain at least one operator."));
        }

        foreach (var op in operators)
        {
            if (op.Id == Guid.Empty)
            {
                repairs.Add(new(
                    "missing_operator_id",
                    "Assigned a new stable operator ID because no connection references an empty endpoint.",
                    op.Id.ToString("D"),
                    string.Empty,
                    Guid.NewGuid().ToString("D")));
            }
            else if (!operatorIds.Add(op.Id))
            {
                diagnostics.Add(new(
                    "duplicate_or_empty_operator_id",
                    "Operator IDs must be non-empty and unique.",
                    op.Id.ToString("D"),
                    op.Type.ToString()));
            }

            if (!Enum.IsDefined(op.Type))
            {
                diagnostics.Add(new(
                    "unknown_operator",
                    $"Operator type '{op.Type}' is not in the governed exposure catalog.",
                    op.Id.ToString("D"),
                    op.Type.ToString()));
                continue;
            }

            var canonicalType = OperatorTypeAliasResolver.Resolve(op.Type);
            if (canonicalType != op.Type)
            {
                repairs.Add(new(
                    "operator_type_alias",
                    "Applied an unambiguous operator type alias.",
                    op.Id.ToString("D"),
                    op.Type.ToString(),
                    canonicalType.ToString()));
            }

            if (OperatorExposureCatalog.IsDisabled(canonicalType) && !allowHistoricalDisabledOperators)
            {
                diagnostics.Add(new(
                    "disabled_operator",
                    $"Operator type '{canonicalType}' is disabled and cannot be created, imported or admitted.",
                    op.Id.ToString("D"),
                    canonicalType.ToString()));
                continue;
            }

            var metadata = _operatorFactory.GetMetadata(canonicalType);
            if (metadata == null)
            {
                diagnostics.Add(new(
                    "unknown_operator",
                    $"Operator type '{op.Type}' is not in the current catalog.",
                    op.Id.ToString("D"),
                    canonicalType.ToString()));
                continue;
            }

            ScanOperatorIdentity(op, diagnostics);
            ScanPorts(op, metadata, diagnostics, repairs, portIds);
            ScanParameters(op, metadata, diagnostics, repairs, parameterIds);
        }

        var operatorById = operators
            .Where(op => op.Id != Guid.Empty)
            .GroupBy(op => op.Id)
            .ToDictionary(group => group.Key, group => group.First());
        foreach (var connection in flow.Connections ?? [])
        {
            if (connection.Id == Guid.Empty)
            {
                repairs.Add(new(
                    "missing_connection_id",
                    "Assigned a new stable connection ID."));
            }

            var hasEmptyEndpoint = false;
            if (connection.SourceOperatorId == Guid.Empty || connection.TargetOperatorId == Guid.Empty)
            {
                diagnostics.Add(new(
                    "connection_references_empty_operator_id",
                    "A connection cannot reference an operator with an empty identity."));
                hasEmptyEndpoint = true;
            }

            if (connection.SourcePortId == Guid.Empty || connection.TargetPortId == Guid.Empty)
            {
                diagnostics.Add(new(
                    "connection_references_empty_port_id",
                    "A connection cannot reference a port with an empty identity."));
                hasEmptyEndpoint = true;
            }

            if (hasEmptyEndpoint)
            {
                continue;
            }

            if (!operatorById.TryGetValue(connection.SourceOperatorId, out var source) ||
                !operatorById.TryGetValue(connection.TargetOperatorId, out var target))
            {
                diagnostics.Add(new(
                    "unknown_connection_endpoint",
                    "Connection references an unknown operator endpoint."));
                continue;
            }

            var sourcePort = source.OutputPorts.FirstOrDefault(port => port.Id == connection.SourcePortId);
            var targetPort = target.InputPorts.FirstOrDefault(port => port.Id == connection.TargetPortId);
            if (sourcePort == null || targetPort == null)
            {
                diagnostics.Add(new(
                    "unknown_connection_port",
                    "Connection references an unknown port endpoint.",
                    source.Id.ToString("D"),
                    source.Type.ToString(),
                    sourcePort?.Name ?? targetPort?.Name ?? string.Empty));
                continue;
            }

            if (!PortDataTypeCompatibility.AreCompatible(sourcePort.DataType, targetPort.DataType))
            {
                diagnostics.Add(new(
                    "route_port_type_mismatch",
                    $"Connection data types are incompatible: {sourcePort.DataType} -> {targetPort.DataType}.",
                    source.Id.ToString("D"),
                    source.Type.ToString(),
                    targetPort.Name));
            }
        }

        ScanRawAliases(originalSnapshot, repairs, diagnostics);
        return new ScanResult(diagnostics, repairs);
    }

    public RawScanResult ScanRawJson(
        string originalSnapshot,
        bool allowHistoricalDisabledOperators = false)
    {
        if (string.IsNullOrWhiteSpace(originalSnapshot))
        {
            return new RawScanResult(null, [new("empty_artifact", "Workflow artifact JSON is empty.")], []);
        }

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(originalSnapshot);
        }
        catch (JsonException ex)
        {
            return new RawScanResult(null, [new("invalid_artifact_json", $"Workflow artifact JSON is invalid: {ex.Message}")], []);
        }

        if (root is not JsonObject objectRoot)
        {
            return new RawScanResult(null, [new("invalid_artifact_shape", "Workflow artifact JSON must be an object.")], []);
        }

        var diagnostics = new List<WorkflowArtifactDiagnostic>();
        var repairs = new List<WorkflowArtifactRepair>();
        if (objectRoot["operators"] is not JsonArray operators)
        {
            diagnostics.Add(new("missing_operators", "Workflow artifact JSON does not contain operators."));
        }
        else
        {
            foreach (var node in operators.OfType<JsonObject>())
            {
                var typeNode = ReadProperty(node, "type");
                var type = typeNode?.GetValue<string>()?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(type))
                {
                    diagnostics.Add(new("missing_operator_type", "Operator type is missing."));
                    continue;
                }

                if (UnambiguousAliases.TryGetValue(type, out var canonical))
                {
                    repairs.Add(new("operator_type_alias", "Applied an unambiguous versioned operator alias.", ToId(node), type, canonical));
                    node[FindPropertyName(node, "type") ?? "type"] = canonical;
                    continue;
                }

                if (!Enum.TryParse<OperatorType>(type, true, out var parsedType) ||
                    !Enum.IsDefined(parsedType))
                {
                    diagnostics.Add(new(
                        "ambiguous_or_unknown_operator_alias",
                        $"Operator type '{type}' is not an exact catalog type or an unambiguous alias.",
                        ToId(node),
                        type));
                }
                else
                {
                    var canonicalType = OperatorTypeAliasResolver.Resolve(parsedType);
                    if (OperatorExposureCatalog.IsDisabled(canonicalType) && !allowHistoricalDisabledOperators)
                    {
                        diagnostics.Add(new(
                            "disabled_operator",
                            $"Operator type '{canonicalType}' is disabled and cannot be created, imported or admitted.",
                            ToId(node),
                            canonicalType.ToString()));
                    }
                }
            }
        }

        return new RawScanResult(objectRoot, diagnostics, repairs);
    }

    private void ScanOperatorIdentity(
        OperatorDto op,
        List<WorkflowArtifactDiagnostic> diagnostics)
    {
        if (op.Metadata == null ||
            !TryReadMetadataString(op.Metadata, "agentRequestedOperatorType", out var requestedType) ||
            string.IsNullOrWhiteSpace(requestedType))
        {
            return;
        }

        var requested = NormalizeAlias(requestedType);
        if (!string.Equals(requested, op.Type.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            diagnostics.Add(new(
                "operator_name_type_mismatch",
                $"Agent requested operator type '{requestedType}' but artifact stores '{op.Type}'.",
                op.Id.ToString("D"),
                op.Type.ToString()));
        }
    }

    private void ScanPorts(
        OperatorDto op,
        OperatorMetadata metadata,
        List<WorkflowArtifactDiagnostic> diagnostics,
        List<WorkflowArtifactRepair> repairs,
        HashSet<Guid> portIds)
    {
        var inputDefinitions = metadata.InputPorts.ToDictionary(item => item.Name, StringComparer.OrdinalIgnoreCase);
        var outputDefinitions = metadata.OutputPorts.ToDictionary(item => item.Name, StringComparer.OrdinalIgnoreCase);
        var inputNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var outputNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var port in op.InputPorts ?? [])
        {
            if (port.Id == Guid.Empty)
            {
                repairs.Add(new(
                    "missing_port_id",
                    "Assigned a new stable input port ID.",
                    op.Id.ToString("D"),
                    string.Empty,
                    port.Name));
            }
            else if (!portIds.Add(port.Id))
            {
                diagnostics.Add(new(
                    "duplicate_port_id",
                    "Port IDs must be unique.",
                    op.Id.ToString("D"),
                    op.Type.ToString(),
                    port.Name));
            }

            if (!inputNames.Add(port.Name))
            {
                diagnostics.Add(new("duplicate_input_port", $"Input port '{port.Name}' is duplicated.", op.Id.ToString("D"), op.Type.ToString(), port.Name));
                continue;
            }

            if (!inputDefinitions.TryGetValue(port.Name, out var definition))
            {
                diagnostics.Add(new("unknown_input_port", $"Input port '{port.Name}' is not in the operator contract.", op.Id.ToString("D"), op.Type.ToString(), port.Name));
                continue;
            }

            if (port.Direction != PortDirection.Input)
            {
                diagnostics.Add(new("input_port_direction_mismatch", $"Input port '{port.Name}' has the wrong direction.", op.Id.ToString("D"), op.Type.ToString(), port.Name));
            }

            if (port.DataType != definition.DataType || !port.IsRequired.Equals(definition.IsRequired))
            {
                diagnostics.Add(new("input_port_contract_mismatch", $"Input port '{port.Name}' does not match the operator contract.", op.Id.ToString("D"), op.Type.ToString(), port.Name));
            }
        }

        foreach (var definition in metadata.InputPorts.Where(item => item.IsRequired && !inputNames.Contains(item.Name)))
        {
            diagnostics.Add(new("missing_required_input_port", $"Required input port '{definition.Name}' is missing from the artifact.", op.Id.ToString("D"), op.Type.ToString(), definition.Name));
        }

        foreach (var port in op.OutputPorts ?? [])
        {
            if (port.Id == Guid.Empty)
            {
                repairs.Add(new(
                    "missing_port_id",
                    "Assigned a new stable output port ID.",
                    op.Id.ToString("D"),
                    string.Empty,
                    port.Name));
            }
            else if (!portIds.Add(port.Id))
            {
                diagnostics.Add(new(
                    "duplicate_port_id",
                    "Port IDs must be unique.",
                    op.Id.ToString("D"),
                    op.Type.ToString(),
                    port.Name));
            }

            if (!outputNames.Add(port.Name))
            {
                diagnostics.Add(new("duplicate_output_port", $"Output port '{port.Name}' is duplicated.", op.Id.ToString("D"), op.Type.ToString(), port.Name));
                continue;
            }

            if (!outputDefinitions.TryGetValue(port.Name, out var definition))
            {
                diagnostics.Add(new("unknown_output_port", $"Output port '{port.Name}' is not in the operator contract.", op.Id.ToString("D"), op.Type.ToString(), port.Name));
                continue;
            }

            if (port.Direction != PortDirection.Output)
            {
                diagnostics.Add(new("output_port_direction_mismatch", $"Output port '{port.Name}' has the wrong direction.", op.Id.ToString("D"), op.Type.ToString(), port.Name));
            }

            if (port.DataType != definition.DataType)
            {
                diagnostics.Add(new("output_port_contract_mismatch", $"Output port '{port.Name}' does not match the operator contract.", op.Id.ToString("D"), op.Type.ToString(), port.Name));
            }
        }

    }

    private static void ScanParameters(
        OperatorDto op,
        OperatorMetadata metadata,
        List<WorkflowArtifactDiagnostic> diagnostics,
        List<WorkflowArtifactRepair> repairs,
        HashSet<Guid> parameterIds)
    {
        var definitions = metadata.Parameters.ToDictionary(item => item.Name, StringComparer.OrdinalIgnoreCase);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var parameter in op.Parameters ?? [])
        {
            if (parameter.Id == Guid.Empty)
            {
                repairs.Add(new(
                    "missing_parameter_id",
                    "Assigned a new stable parameter ID.",
                    op.Id.ToString("D"),
                    string.Empty,
                    parameter.Name));
            }
            else if (!parameterIds.Add(parameter.Id))
            {
                diagnostics.Add(new(
                    "duplicate_parameter_id",
                    "Parameter IDs must be unique.",
                    op.Id.ToString("D"),
                    op.Type.ToString(),
                    ParameterName: parameter.Name));
            }

            if (!names.Add(parameter.Name))
            {
                diagnostics.Add(new("duplicate_parameter", $"Parameter '{parameter.Name}' is duplicated.", op.Id.ToString("D"), op.Type.ToString(), ParameterName: parameter.Name));
                continue;
            }

            if (!definitions.TryGetValue(parameter.Name, out var definition))
            {
                diagnostics.Add(new("unknown_parameter", $"Parameter '{parameter.Name}' is not in the operator contract.", op.Id.ToString("D"), op.Type.ToString(), ParameterName: parameter.Name));
                continue;
            }

            if (!string.Equals(parameter.DataType, definition.DataType, StringComparison.OrdinalIgnoreCase) ||
                parameter.IsRequired != definition.IsRequired)
            {
                diagnostics.Add(new(
                    "parameter_contract_mismatch",
                    $"Parameter '{parameter.Name}' does not match the operator contract.",
                    op.Id.ToString("D"),
                    op.Type.ToString(),
                    ParameterName: parameter.Name));
            }
        }

        foreach (var definition in metadata.Parameters.Where(item => item.IsRequired && !names.Contains(item.Name)))
        {
            diagnostics.Add(new("missing_required_parameter", $"Required parameter '{definition.Name}' is missing from the artifact.", op.Id.ToString("D"), op.Type.ToString(), ParameterName: definition.Name));
        }
    }

    private static void ScanRawAliases(
        string? originalSnapshot,
        List<WorkflowArtifactRepair> repairs,
        List<WorkflowArtifactDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(originalSnapshot))
        {
            return;
        }

        try
        {
            using var document = JsonDocument.Parse(originalSnapshot);
            if (!document.RootElement.TryGetProperty("operators", out var operators) ||
                operators.ValueKind != JsonValueKind.Array)
            {
                return;
            }

            foreach (var op in operators.EnumerateArray())
            {
                if (!op.TryGetProperty("type", out var typeElement) || typeElement.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                var type = typeElement.GetString() ?? string.Empty;
                if (UnambiguousAliases.TryGetValue(type, out var canonical) &&
                    repairs.All(item => !string.Equals(item.FromValue, type, StringComparison.OrdinalIgnoreCase)))
                {
                    repairs.Add(new("operator_type_alias", "Applied an unambiguous versioned operator alias.", string.Empty, type, canonical));
                }
                else if (!string.IsNullOrWhiteSpace(type) &&
                         !UnambiguousAliases.ContainsKey(type) &&
                         !Enum.TryParse<OperatorType>(type, true, out _))
                {
                    diagnostics.Add(new("unknown_operator_alias", $"Operator type '{type}' is not a known catalog type."));
                }
            }
        }
        catch (JsonException)
        {
            // Raw JSON shape is reported by ScanRawJson. DTO admission still has a typed value.
        }
    }

    private static string NormalizeAlias(string value) =>
        UnambiguousAliases.TryGetValue(value.Trim(), out var canonical)
            ? canonical
            : value.Trim();

    private static bool TryReadMetadataString(
        IReadOnlyDictionary<string, object?> metadata,
        string key,
        out string value)
    {
        value = string.Empty;
        var pair = metadata.FirstOrDefault(item => string.Equals(item.Key, key, StringComparison.OrdinalIgnoreCase));
        if (string.IsNullOrWhiteSpace(pair.Key) || pair.Value == null)
        {
            return false;
        }

        value = pair.Value switch
        {
            string text => text,
            JsonElement element when element.ValueKind == JsonValueKind.String => element.GetString() ?? string.Empty,
            _ => pair.Value.ToString() ?? string.Empty
        };
        return !string.IsNullOrWhiteSpace(value);
    }

    private static JsonNode? ReadProperty(JsonObject node, string name) =>
        node.FirstOrDefault(item => string.Equals(item.Key, name, StringComparison.OrdinalIgnoreCase)).Value;

    private static string? FindPropertyName(JsonObject node, string name) =>
        node.FirstOrDefault(item => string.Equals(item.Key, name, StringComparison.OrdinalIgnoreCase)).Key;

    private static string ToId(JsonObject node)
    {
        var id = ReadProperty(node, "id")?.GetValue<string>();
        return id ?? string.Empty;
    }

    public sealed record ScanResult(
        IReadOnlyList<WorkflowArtifactDiagnostic> Diagnostics,
        IReadOnlyList<WorkflowArtifactRepair> Repairs);

    public sealed record RawScanResult(
        JsonObject? Root,
        IReadOnlyList<WorkflowArtifactDiagnostic> Diagnostics,
        IReadOnlyList<WorkflowArtifactRepair> Repairs);
}

public sealed class WorkflowLegacyRepairService
{
    public WorkflowLegacyRepairService(IOperatorFactory _)
    {
    }

    public OperatorFlowDto Repair(OperatorFlowDto source)
    {
        var result = new OperatorFlowDto
        {
            Id = source.Id == Guid.Empty ? Guid.NewGuid() : source.Id,
            Name = string.IsNullOrWhiteSpace(source.Name) ? "Workflow" : source.Name,
            DecisionConfiguration = source.DecisionConfiguration,
            Operators = source.Operators.Select(CloneOperator).ToList(),
            Connections = source.Connections.Select(CloneConnection).ToList()
        };

        return result;
    }

    private static OperatorDto CloneOperator(OperatorDto source)
    {
        return new OperatorDto
        {
            Id = source.Id == Guid.Empty ? Guid.NewGuid() : source.Id,
            Name = source.Name,
            Type = OperatorTypeAliasResolver.Resolve(source.Type),
            Metadata = source.Metadata == null
                ? null
                : new Dictionary<string, object?>(source.Metadata, StringComparer.OrdinalIgnoreCase),
            X = source.X,
            Y = source.Y,
            IsEnabled = source.IsEnabled,
            ExecutionStatus = source.ExecutionStatus,
            ExecutionTimeMs = source.ExecutionTimeMs,
            ErrorMessage = source.ErrorMessage,
            InputPorts = source.InputPorts.Select(port => new PortDto
            {
                Id = port.Id == Guid.Empty ? Guid.NewGuid() : port.Id,
                Name = port.Name,
                Direction = port.Direction,
                DataType = port.DataType,
                IsRequired = port.IsRequired
            }).ToList(),
            OutputPorts = source.OutputPorts.Select(port => new PortDto
            {
                Id = port.Id == Guid.Empty ? Guid.NewGuid() : port.Id,
                Name = port.Name,
                Direction = port.Direction,
                DataType = port.DataType,
                IsRequired = port.IsRequired
            }).ToList(),
            Parameters = source.Parameters.Select(parameter => new ParameterDto
            {
                Id = parameter.Id == Guid.Empty ? Guid.NewGuid() : parameter.Id,
                Name = parameter.Name,
                DisplayName = parameter.DisplayName,
                Description = parameter.Description,
                DataType = parameter.DataType,
                Value = parameter.Value,
                DefaultValue = parameter.DefaultValue,
                MinValue = parameter.MinValue,
                MaxValue = parameter.MaxValue,
                IsRequired = parameter.IsRequired,
                Options = parameter.Options?.Select(option => new ParameterOption
                {
                    Label = option.Label,
                    Value = option.Value
                }).ToList()
            }).ToList()
        };
    }

    private static OperatorConnectionDto CloneConnection(OperatorConnectionDto source) => new()
    {
        Id = source.Id == Guid.Empty ? Guid.NewGuid() : source.Id,
        SourceOperatorId = source.SourceOperatorId,
        SourcePortId = source.SourcePortId,
        TargetOperatorId = source.TargetOperatorId,
        TargetPortId = source.TargetPortId
    };
}

public sealed class WorkflowArtifactQuarantineStore : IWorkflowArtifactQuarantineStore
{
    private readonly string _rootDirectory;
    private readonly ILogger<WorkflowArtifactQuarantineStore> _logger;

    public WorkflowArtifactQuarantineStore(ILogger<WorkflowArtifactQuarantineStore> logger)
    {
        _logger = logger;
        _rootDirectory = Path.Combine(
            AppContext.BaseDirectory,
            "App_Data",
            "WorkflowQuarantine");
    }

    public void Preserve(WorkflowArtifactQuarantineRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        Directory.CreateDirectory(_rootDirectory);
        var fileName = string.IsNullOrWhiteSpace(record.RecordId)
            ? $"quarantine_{Guid.NewGuid():N}.json"
            : $"{Sanitize(record.RecordId)}.json";
        var path = Path.Combine(_rootDirectory, fileName);
        var tempPath = path + ".tmp";
        var json = JsonSerializer.Serialize(record, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(tempPath, json, new UTF8Encoding(false));
        File.Move(tempPath, path, overwrite: true);
        _logger.LogWarning(
            "Preserved workflow artifact admission record {RecordId} with disposition {Disposition}.",
            record.RecordId,
            record.Report.Disposition);
    }

    private static string Sanitize(string value)
    {
        var sanitized = new string(value
            .Where(ch => char.IsLetterOrDigit(ch) || ch is '_' or '-')
            .ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? $"quarantine_{Guid.NewGuid():N}" : sanitized;
    }
}

public sealed class WorkflowArtifactAdmissionGate : IWorkflowArtifactAdmissionGate
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly WorkflowLegacyScanner _scanner;
    private readonly WorkflowLegacyRepairService _repairService;
    private readonly IWorkflowArtifactQuarantineStore _quarantineStore;
    private readonly VisionTaskRouteContractRegistry _routeContracts = new();

    public WorkflowArtifactAdmissionGate(
        WorkflowLegacyScanner scanner,
        WorkflowLegacyRepairService repairService,
        IWorkflowArtifactQuarantineStore quarantineStore)
    {
        _scanner = scanner;
        _repairService = repairService;
        _quarantineStore = quarantineStore ?? throw new ArgumentNullException(nameof(quarantineStore));
    }

    public WorkflowArtifactAdmissionResult Inspect(
        OperatorFlowDto flow,
        string source,
        string? originalSnapshot = null,
        WorkflowArtifactAdmissionContext? context = null)
    {
        ArgumentNullException.ThrowIfNull(flow);
        var original = string.IsNullOrWhiteSpace(originalSnapshot)
            ? JsonSerializer.Serialize(flow, JsonOptions)
            : originalSnapshot;
        var originalHash = ComputeHash(original);
        var scan = _scanner.Scan(
            flow,
            original,
            context?.AllowHistoricalDisabledOperators == true);
        if (scan.Diagnostics.Count > 0)
        {
            return Quarantine(source, original, originalHash, scan.Diagnostics, scan.Repairs);
        }

        OperatorFlowDto admitted;
        try
        {
            admitted = _repairService.Repair(flow);
        }
        catch (Exception ex)
        {
            return Quarantine(
                source,
                original,
                originalHash,
                [new("repair_failed", ex.Message)],
                scan.Repairs);
        }

        var routeDiagnostics = AssessRoute(admitted, context);
        if (routeDiagnostics.Count > 0)
        {
            var previewOnly = IsSafeScaffold(admitted, routeDiagnostics);
            return Quarantine(
                source,
                original,
                originalHash,
                routeDiagnostics,
                scan.Repairs,
                previewOnly ? admitted : null,
                previewOnly);
        }

        var admittedJson = JsonSerializer.Serialize(admitted, JsonOptions);
        var changed = !string.Equals(originalHash, ComputeHash(admittedJson), StringComparison.OrdinalIgnoreCase);
        var disposition = changed || scan.Repairs.Count > 0
            ? WorkflowArtifactAdmissionDisposition.RepairableLegacy
            : WorkflowArtifactAdmissionDisposition.Canonical;
        var report = BuildReport(
            source,
            disposition,
            originalHash,
            ComputeHash(admittedJson),
            originalPreserved: disposition == WorkflowArtifactAdmissionDisposition.RepairableLegacy,
            canRun: true,
            canExport: true,
            canSyncStation: true,
            diagnostics: [],
            repairs: scan.Repairs);
        PreserveIfNeeded(source, original, report);
        return new WorkflowArtifactAdmissionResult
        {
            Disposition = disposition,
            Flow = admitted,
            Report = report
        };
    }

    public WorkflowArtifactAdmissionResult Inspect(
        OperatorFlow flow,
        string source,
        string? originalSnapshot = null,
        WorkflowArtifactAdmissionContext? context = null)
    {
        ArgumentNullException.ThrowIfNull(flow);
        var dto = ToDto(flow);
        var result = Inspect(dto, source, originalSnapshot, context);
        return result with
        {
            Entity = result.Flow?.ToEntity()
        };
    }

    public WorkflowArtifactAdmissionResult InspectJson(string originalSnapshot, string source)
    {
        return InspectJson(originalSnapshot, source, null);
    }

    public WorkflowArtifactAdmissionResult InspectJson(
        string originalSnapshot,
        string source,
        WorkflowArtifactAdmissionContext? context)
    {
        var raw = _scanner.ScanRawJson(
            originalSnapshot,
            context?.AllowHistoricalDisabledOperators == true);
        if (raw.Diagnostics.Count > 0)
        {
            return Quarantine(source, originalSnapshot, ComputeHash(originalSnapshot), raw.Diagnostics, raw.Repairs);
        }

        var normalizedJson = raw.Root?.ToJsonString(JsonOptions) ?? originalSnapshot;
        OperatorFlowDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<OperatorFlowDto>(normalizedJson, JsonOptions);
        }
        catch (JsonException ex)
        {
            return Quarantine(
                source,
                originalSnapshot,
                ComputeHash(originalSnapshot),
                [new("invalid_artifact_json", ex.Message)],
                raw.Repairs);
        }

        return dto == null
            ? Quarantine(
                source,
                originalSnapshot,
                ComputeHash(originalSnapshot),
                [new("invalid_artifact_shape", "Workflow artifact JSON could not be materialized.")],
                raw.Repairs)
            : Inspect(dto, source, originalSnapshot, context);
    }

    private WorkflowArtifactAdmissionResult Quarantine(
        string source,
        string original,
        string originalHash,
        IReadOnlyList<WorkflowArtifactDiagnostic> diagnostics,
        IReadOnlyList<WorkflowArtifactRepair> repairs,
        OperatorFlowDto? previewFlow = null,
        bool previewOnly = false)
    {
        var report = BuildReport(
            source,
            WorkflowArtifactAdmissionDisposition.Quarantined,
            originalHash,
            string.Empty,
            originalPreserved: true,
            canRun: false,
            canExport: false,
            canSyncStation: false,
            diagnostics,
            repairs,
            previewOnly);
        PreserveIfNeeded(source, original, report);
        return new WorkflowArtifactAdmissionResult
        {
            Disposition = WorkflowArtifactAdmissionDisposition.Quarantined,
            Flow = previewOnly ? previewFlow : null,
            Report = report
        };
    }

    private static bool IsSafeScaffold(
        OperatorFlowDto flow,
        IReadOnlyList<WorkflowArtifactDiagnostic> diagnostics)
    {
        return diagnostics.Any(item =>
                   item.Code.Equals("minimum_scaffold_task_incomplete", StringComparison.OrdinalIgnoreCase)) &&
               diagnostics.All(item =>
                   item.Code.StartsWith("route_", StringComparison.OrdinalIgnoreCase) ||
                   item.Code.Equals("minimum_scaffold_task_incomplete", StringComparison.OrdinalIgnoreCase)) &&
               flow.Operators.Count > 0 &&
               flow.Operators.All(operatorDto => operatorDto.Type is
                   OperatorType.ImageAcquisition or
                   OperatorType.ResultJudgment or
                   OperatorType.ResultOutput);
    }

    private void PreserveIfNeeded(string source, string original, WorkflowQuarantineReport report)
    {
        if (report.Disposition == WorkflowArtifactAdmissionDisposition.Canonical)
        {
            return;
        }

        _quarantineStore.Preserve(new WorkflowArtifactQuarantineRecord
        {
            RecordId = report.ReportId,
            RecordedAtUtc = DateTimeOffset.UtcNow,
            Source = source,
            Report = report,
            OriginalSnapshot = original
        });
    }

    private IReadOnlyList<WorkflowArtifactDiagnostic> AssessRoute(
        OperatorFlowDto flow,
        WorkflowArtifactAdmissionContext? context)
    {
        var taskType = FirstNonEmpty(
            context?.TaskType,
            flow.Operators
                .Select(op => ReadMetadataString(op.Metadata, "agentTaskType", "taskType"))
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)));
        taskType = VisionTaskRouteContractRegistry.NormalizeTaskType(taskType);
        var hasArtifactEvidence = !string.IsNullOrWhiteSpace(context?.TaskType) ||
                                  !string.IsNullOrWhiteSpace(context?.ArtifactFingerprint) ||
                                  context?.RouteSemanticsSatisfied.HasValue == true ||
                                  !string.IsNullOrWhiteSpace(taskType) ||
                                  flow.Operators.Any(op =>
                                      !string.IsNullOrWhiteSpace(ReadMetadataString(
                                          op.Metadata,
                                          "agentArtifactFingerprint",
                                          "artifactFingerprint")));
        if (!hasArtifactEvidence)
        {
            return [];
        }

        var diagnostics = new List<WorkflowArtifactDiagnostic>();
        var metadataFingerprints = flow.Operators
            .Select(op => ReadMetadataString(op.Metadata, "agentArtifactFingerprint", "artifactFingerprint"))
            .ToList();
        var knownFingerprints = metadataFingerprints
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var metadataFingerprint = knownFingerprints.FirstOrDefault() ?? string.Empty;
        if ((context != null && string.IsNullOrWhiteSpace(context.ArtifactFingerprint)) ||
            (flow.Operators.Count > 0 && metadataFingerprints.Any(string.IsNullOrWhiteSpace)))
        {
            diagnostics.Add(new(
                "artifact_fingerprint_missing",
                "AI workflow admission requires the compiled artifact fingerprint."));
        }
        if (knownFingerprints.Count > 1)
        {
            diagnostics.Add(new(
                "artifact_fingerprint_mismatch",
                "AI workflow operators carry conflicting compiled artifact fingerprints."));
        }

        if (context != null &&
            !string.IsNullOrWhiteSpace(context.ArtifactFingerprint) &&
            !string.IsNullOrWhiteSpace(metadataFingerprint) &&
            !string.Equals(context.ArtifactFingerprint, metadataFingerprint, StringComparison.OrdinalIgnoreCase))
        {
            diagnostics.Add(new(
                "artifact_fingerprint_mismatch",
                "The supplied compiled artifact fingerprint does not match the persisted workflow metadata."));
        }

        if (!string.IsNullOrWhiteSpace(metadataFingerprint))
        {
            var planHash = ReadConsistentMetadataValue(flow, "agentPlanHash", "planHash");
            var catalogVersion = ReadConsistentMetadataValue(flow, "agentCatalogVersion", "catalogVersion");
            var buildIntent = ReadConsistentMetadataValue(flow, "agentBuildIntent", "buildIntent");
            if (string.IsNullOrWhiteSpace(planHash) ||
                string.IsNullOrWhiteSpace(catalogVersion) ||
                string.IsNullOrWhiteSpace(buildIntent))
            {
                diagnostics.Add(new(
                    "artifact_fingerprint_unverifiable",
                    "AI workflow artifact fingerprint is missing the canonical Plan/Catalog/Intent identity."));
            }
            else
            {
                var computedFingerprint = WorkflowArtifactFingerprint.Compute(
                    planHash,
                    catalogVersion,
                    buildIntent,
                    BuildRouteGraph(flow));
                if (!string.Equals(computedFingerprint, metadataFingerprint, StringComparison.OrdinalIgnoreCase))
                {
                    diagnostics.Add(new(
                        "artifact_fingerprint_mismatch",
                        "The persisted workflow graph does not match its compiled artifact fingerprint."));
                }
            }
        }

        var routeSatisfied = context?.RouteSemanticsSatisfied ??
            flow.Operators
                .Select(op => ReadMetadataString(op.Metadata, "agentRouteSemanticsSatisfied"))
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => bool.TryParse(value, out var parsed) && parsed)
                .Cast<bool?>()
                .FirstOrDefault();
        if (routeSatisfied == false)
        {
            diagnostics.Add(new(
                "route_semantics_not_satisfied",
                "The AI workflow route contract did not pass before artifact admission."));
        }

        if (context != null &&
            routeSatisfied.HasValue &&
            context.RouteSemanticsSatisfied.HasValue &&
            routeSatisfied.Value != context.RouteSemanticsSatisfied.Value)
        {
            diagnostics.Add(new(
                "route_semantics_evidence_mismatch",
                "The route result supplied by BuildApplication does not match workflow metadata."));
        }

        if (string.IsNullOrWhiteSpace(taskType))
        {
            diagnostics.Add(new(
                "unsupported_task_route_contract",
                "AI workflow artifact does not carry a verifiable task route contract."));
            return diagnostics;
        }

        var graph = BuildRouteGraph(flow);
        var assessment = _routeContracts.Assess(taskType, graph);
        if (!assessment.Supported || !assessment.Satisfied)
        {
            foreach (var reason in assessment.BlockingReasons.DefaultIfEmpty("route_semantics_blocked"))
            {
                diagnostics.Add(new(
                    reason,
                    $"Task route '{assessment.TaskType}' failed semantic admission."));
            }
        }

        return diagnostics;
    }

    private static CanonicalWorkflowGraph BuildRouteGraph(OperatorFlowDto flow)
    {
        var nodes = flow.Operators
            .Select(op => new CanonicalWorkflowNode(
                FirstNonEmpty(
                    ReadMetadataString(op.Metadata, "agentTempId", "AgentTempId"),
                    op.Id.ToString("D")),
                OperatorTypeAliasResolver.Resolve(op.Type).ToString(),
                op.Name,
                (op.Parameters ?? [])
                    .GroupBy(parameter => parameter.Name, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(
                        group => group.Key,
                        group => (string?)Convert.ToString(group.First().Value ?? group.First().DefaultValue),
                        StringComparer.OrdinalIgnoreCase),
                (op.InputPorts ?? [])
                    .Select(port => new VisionAgentPortFingerprint
                    {
                        Name = port.Name,
                        DataType = port.DataType.ToString(),
                        Required = port.IsRequired
                    })
                    .ToList(),
                (op.OutputPorts ?? [])
                    .Select(port => new VisionAgentPortFingerprint
                    {
                        Name = port.Name,
                        DataType = port.DataType.ToString(),
                        Required = port.IsRequired
                    })
                    .ToList()))
            .ToList();
        var idByGuid = flow.Operators.ToDictionary(
            op => op.Id,
            op => FirstNonEmpty(
                ReadMetadataString(op.Metadata, "agentTempId", "AgentTempId"),
                op.Id.ToString("D")));
        var connections = (flow.Connections ?? [])
            .Where(connection => idByGuid.ContainsKey(connection.SourceOperatorId) &&
                                 idByGuid.ContainsKey(connection.TargetOperatorId))
            .Select(connection => new CanonicalWorkflowConnection(
                idByGuid[connection.SourceOperatorId],
                flow.Operators
                    .First(op => op.Id == connection.SourceOperatorId)
                    .OutputPorts
                    .FirstOrDefault(port => port.Id == connection.SourcePortId)?.Name ?? string.Empty,
                idByGuid[connection.TargetOperatorId],
                flow.Operators
                    .First(op => op.Id == connection.TargetOperatorId)
                    .InputPorts
                    .FirstOrDefault(port => port.Id == connection.TargetPortId)?.Name ?? string.Empty))
            .ToList();
        return new CanonicalWorkflowGraph(
            nodes,
            connections,
            nodes.FirstOrDefault()?.TempId ?? string.Empty);
    }

    private static string ReadConsistentMetadataValue(
        OperatorFlowDto flow,
        params string[] keys)
    {
        var values = flow.Operators
            .Select(op => ReadMetadataString(op.Metadata, keys))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return values.Count == 1 ? values[0] : string.Empty;
    }

    private static string ReadMetadataString(
        IReadOnlyDictionary<string, object?>? metadata,
        params string[] keys)
    {
        if (metadata == null)
        {
            return string.Empty;
        }

        foreach (var key in keys)
        {
            var pair = metadata.FirstOrDefault(item => string.Equals(item.Key, key, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(pair.Key) && pair.Value != null)
            {
                return pair.Value switch
                {
                    string text => text.Trim(),
                    JsonElement element when element.ValueKind == JsonValueKind.String => (element.GetString() ?? string.Empty).Trim(),
                    _ => pair.Value.ToString()?.Trim() ?? string.Empty
                };
            }
        }

        return string.Empty;
    }

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;

    private static WorkflowQuarantineReport BuildReport(
        string source,
        WorkflowArtifactAdmissionDisposition disposition,
        string originalHash,
        string admittedHash,
        bool originalPreserved,
        bool canRun,
        bool canExport,
        bool canSyncStation,
        IReadOnlyList<WorkflowArtifactDiagnostic> diagnostics,
        IReadOnlyList<WorkflowArtifactRepair> repairs,
        bool previewOnly = false) =>
        new()
        {
            ReportId = $"artifact_{Guid.NewGuid():N}",
            Source = source ?? string.Empty,
            Disposition = disposition,
            OriginalArtifactHash = originalHash,
            AdmittedArtifactHash = admittedHash,
            OriginalArtifactPreserved = originalPreserved,
            CanRun = canRun,
            CanExport = canExport,
            CanSyncStation = canSyncStation,
            PreviewOnly = previewOnly,
            Diagnostics = diagnostics.ToList(),
            Repairs = repairs.ToList()
        };

    private static string ComputeHash(string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value ?? string.Empty));
        return "sha256:" + Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static OperatorFlowDto ToDto(OperatorFlow flow) => new()
    {
        Id = flow.Id,
        Name = flow.Name,
        DecisionConfiguration = flow.DecisionConfiguration,
        Operators = flow.Operators.Select(op => new OperatorDto
        {
            Id = op.Id,
            Name = op.Name,
            Type = op.Type,
            Metadata = op.Metadata == null
                ? null
                : new Dictionary<string, object?>(op.Metadata, StringComparer.OrdinalIgnoreCase),
            X = op.Position.X,
            Y = op.Position.Y,
            IsEnabled = op.IsEnabled,
            InputPorts = op.InputPorts.Select(port => new PortDto
            {
                Id = port.Id,
                Name = port.Name,
                Direction = port.Direction,
                DataType = port.DataType,
                IsRequired = port.IsRequired
            }).ToList(),
            OutputPorts = op.OutputPorts.Select(port => new PortDto
            {
                Id = port.Id,
                Name = port.Name,
                Direction = port.Direction,
                DataType = port.DataType,
                IsRequired = port.IsRequired
            }).ToList(),
            Parameters = op.Parameters.Select(parameter => new ParameterDto
            {
                Id = parameter.Id,
                Name = parameter.Name,
                DisplayName = parameter.DisplayName,
                Description = parameter.Description,
                DataType = parameter.DataType,
                Value = parameter.Value,
                DefaultValue = parameter.DefaultValue,
                MinValue = parameter.MinValue,
                MaxValue = parameter.MaxValue,
                IsRequired = parameter.IsRequired,
                Options = parameter.Options
            }).ToList()
        }).ToList(),
        Connections = flow.Connections.Select(connection => new OperatorConnectionDto
        {
            Id = connection.Id,
            SourceOperatorId = connection.SourceOperatorId,
            SourcePortId = connection.SourcePortId,
            TargetOperatorId = connection.TargetOperatorId,
            TargetPortId = connection.TargetPortId
        }).ToList()
    };
}
