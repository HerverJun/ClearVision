using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ClearVision.Product.Core.Decisions;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.ProjectVariables;
using ClearVision.Product.Core.ValueObjects;

namespace ClearVision.Product.Core.Services;

/// <summary>
/// The declared authority of a flow used for one execution.  It is deliberately
/// explicit: callers cannot infer authority from a non-null inline flow.
/// </summary>
public enum ExecutionSnapshotSource
{
    PersistedProject = 0,
    Draft = 1,
    RuntimePackage = 2,
    ShadowBaseline = 3,
    ShadowCandidate = 4
}

/// <summary>Execution intent used by the single side-effect policy.</summary>
public enum ExecutionRunMode
{
    FormalPrimary = 0,
    Preview = 1,
    Debug = 2,
    ShadowCandidate = 3,
    StationRuntime = 4
}

public enum ShadowExecutionRole
{
    None = 0,
    Baseline = 1,
    Candidate = 2
}

[Flags]
public enum ExecutionSideEffect
{
    None = 0,
    DeviceRead = 1,
    FileRead = 2,
    FileWrite = 4,
    NetworkWrite = 8,
    DeviceWrite = 16,
    StateWrite = 32
}

public sealed record ExecutionSideEffectViolation(
    Guid OperatorId,
    string OperatorName,
    OperatorType OperatorType,
    ExecutionSideEffect Capability,
    string Code,
    string Message);

/// <summary>
/// Central capability catalog.  Admission and the flow executor both consume
/// this catalog, so an operator cannot regain I/O by choosing a different API.
/// </summary>
public static class ExecutionSideEffectCatalog
{
    private static readonly HashSet<OperatorType> FileReadOperators =
    [
        OperatorType.DeepLearning,
        OperatorType.SemanticSegmentation,
        OperatorType.AnomalyDetection,
        OperatorType.ShapeMatching,
        OperatorType.AkazeFeatureMatch,
        OperatorType.OrbFeatureMatch,
        OperatorType.GradientShapeMatch,
        OperatorType.PyramidShapeMatch,
        OperatorType.PlanarMatching,
        OperatorType.LocalDeformableMatching,
        OperatorType.CalibrationLoader,
        OperatorType.CameraCalibration,
        OperatorType.FisheyeCalibration,
        OperatorType.StereoCalibration
    ];

    private static readonly HashSet<OperatorType> NetworkWriteOperators =
    [
        OperatorType.HttpRequest,
        OperatorType.TcpCommunication,
        OperatorType.SerialCommunication,
        OperatorType.ModbusCommunication,
        OperatorType.ModbusRtuCommunication,
        OperatorType.SiemensS7Communication,
        OperatorType.MitsubishiMcCommunication,
        OperatorType.OmronFinsCommunication,
        OperatorType.MqttPublish,
        OperatorType.DatabaseWrite
    ];

    private static readonly HashSet<OperatorType> DeviceWriteOperators =
    [
        OperatorType.CameraCalibration,
        OperatorType.FisheyeCalibration,
        OperatorType.StereoCalibration,
        OperatorType.NPointCalibration,
        OperatorType.TranslationRotationCalibration,
        OperatorType.HandEyeCalibration,
        OperatorType.CalibrationLoader,
        OperatorType.TriggerModule
    ];

    public static ExecutionSideEffect GetCapabilities(Operator @operator)
    {
        var capabilities = ExecutionSideEffect.None;
        if (FileReadOperators.Contains(@operator.Type) || RequiresOptionalFileRead(@operator))
        {
            capabilities |= ExecutionSideEffect.FileRead;
        }

        if (NetworkWriteOperators.Contains(@operator.Type))
        {
            capabilities |= ExecutionSideEffect.NetworkWrite;
        }

        if (DeviceWriteOperators.Contains(@operator.Type))
        {
            capabilities |= ExecutionSideEffect.DeviceWrite;
        }

        if (@operator.Type == OperatorType.ImageSave || @operator.Type == OperatorType.TextSave ||
            (@operator.Type == OperatorType.ResultOutput && ReadBool(@operator, "SaveToFile")) ||
            (@operator.Type == OperatorType.AnomalyDetection && HasValue(@operator, "SaveFeatureBankPath")))
        {
            capabilities |= ExecutionSideEffect.FileWrite;
        }

        if (@operator.Type == OperatorType.ImageAcquisition)
        {
            var sourceType = ReadString(@operator, "SourceType", "sourceType");
            if (string.Equals(NormalizeOption(sourceType), "Camera", StringComparison.OrdinalIgnoreCase))
            {
                capabilities |= ExecutionSideEffect.DeviceRead;
            }
            else if (!string.IsNullOrWhiteSpace(ReadString(@operator, "FilePath", "filePath")))
            {
                capabilities |= ExecutionSideEffect.FileRead;
            }
        }

        if (@operator.Type is OperatorType.VariableWrite or OperatorType.VariableIncrement)
        {
            capabilities |= ExecutionSideEffect.StateWrite;
        }

        return capabilities;
    }

    private static bool RequiresOptionalFileRead(Operator @operator)
    {
        if (@operator.Type == OperatorType.EdgeDetection)
        {
            return string.Equals(
                       NormalizeOption(ReadString(@operator, "Method")),
                       "OnnxEdge",
                       StringComparison.OrdinalIgnoreCase) ||
                   HasValue(@operator, "EdgeModelPath", "EdgeModelId", "ModelCatalogPath");
        }

        if (@operator.Type == OperatorType.OcrRecognition)
        {
            return HasValue(@operator, "ModelPath");
        }

        return false;
    }

    private static bool HasValue(Operator @operator, params string[] names) =>
        names.Any(name => !string.IsNullOrWhiteSpace(ReadString(@operator, name)));

    /// <summary>
    /// Returns the declaration field that caused a capability when one exists.
    /// This keeps admission diagnostics tied to the same catalog as execution
    /// policy instead of reintroducing per-entrypoint operator classification.
    /// </summary>
    public static string? GetCapabilityParameterName(Operator @operator, ExecutionSideEffect capabilities)
    {
        if (capabilities.HasFlag(ExecutionSideEffect.FileWrite) &&
            @operator.Type == OperatorType.ResultOutput)
        {
            return "SaveToFile";
        }

        if (capabilities.HasFlag(ExecutionSideEffect.FileRead) &&
            @operator.Type == OperatorType.ImageAcquisition)
        {
            return "FilePath";
        }

        return capabilities.HasFlag(ExecutionSideEffect.DeviceRead) &&
            @operator.Type == OperatorType.ImageAcquisition
            ? "SourceType"
            : null;
    }

    private static bool ReadBool(Operator @operator, string name)
    {
        var raw = @operator.Parameters.FirstOrDefault(parameter =>
            string.Equals(parameter.Name, name, StringComparison.OrdinalIgnoreCase))?.GetValue();
        return raw is bool value
            ? value
            : bool.TryParse(Convert.ToString(raw, System.Globalization.CultureInfo.InvariantCulture), out var parsed) && parsed;
    }

    private static string? ReadString(Operator @operator, params string[] names) =>
        @operator.Parameters.FirstOrDefault(parameter =>
            names.Any(name => string.Equals(parameter.Name, name, StringComparison.OrdinalIgnoreCase)))
            ?.GetValue()?.ToString();

    private static string NormalizeOption(string? value)
    {
        var normalized = value?.Trim() ?? string.Empty;
        var separator = normalized.IndexOf('|', StringComparison.Ordinal);
        return separator >= 0 ? normalized[..separator].Trim() : normalized;
    }
}

/// <summary>
/// The one policy used for Preview, Debug, Formal Primary, Shadow Candidate and
/// Station Runtime.  Only explicitly admitted formal modes can execute writes
/// or device operations.
/// </summary>
public sealed class ExecutionSideEffectPolicy
{
    private ExecutionSideEffectPolicy(ExecutionRunMode runMode, ExecutionSideEffect allowedCapabilities)
    {
        RunMode = runMode;
        AllowedCapabilities = allowedCapabilities;
    }

    public ExecutionRunMode RunMode { get; }

    public ExecutionSideEffect AllowedCapabilities { get; }

    public static ExecutionSideEffectPolicy For(ExecutionRunMode runMode) => runMode switch
    {
        ExecutionRunMode.FormalPrimary or ExecutionRunMode.StationRuntime =>
            new ExecutionSideEffectPolicy(runMode, ExecutionSideEffect.DeviceRead | ExecutionSideEffect.FileRead |
                ExecutionSideEffect.FileWrite | ExecutionSideEffect.NetworkWrite | ExecutionSideEffect.DeviceWrite |
                ExecutionSideEffect.StateWrite),
        // Preview/debug consume only bounded request input and may mutate
        // variables inside an isolated preview session. They never acquire a
        // filesystem, network, database, or device resource from node params.
        ExecutionRunMode.Preview or ExecutionRunMode.Debug =>
            new ExecutionSideEffectPolicy(runMode, ExecutionSideEffect.StateWrite),
        // Shadow intentionally receives no shared state write capability.
        ExecutionRunMode.ShadowCandidate =>
            new ExecutionSideEffectPolicy(runMode, ExecutionSideEffect.None),
        _ => new ExecutionSideEffectPolicy(runMode, ExecutionSideEffect.None)
    };

    public IReadOnlyList<ExecutionSideEffectViolation> Validate(OperatorFlow? flow)
    {
        if (flow?.Operators == null)
        {
            return Array.Empty<ExecutionSideEffectViolation>();
        }

        return NestedExecutionFlowCatalog.EnumerateEnabledOperators(flow)
            .Select(@operator =>
            {
                var required = ExecutionSideEffectCatalog.GetCapabilities(@operator);
                var disallowed = required & ~AllowedCapabilities;
                return disallowed == ExecutionSideEffect.None
                    ? null
                    : new ExecutionSideEffectViolation(
                        @operator.Id,
                        @operator.Name,
                        @operator.Type,
                        disallowed,
                        "SIDE_EFFECT_POLICY_BLOCKED",
                        $"{@operator.Type} requires '{disallowed}', which is not allowed in {RunMode}.");
            })
            .Where(violation => violation != null)
            .Cast<ExecutionSideEffectViolation>()
            .ToArray();
    }

    public IReadOnlyList<ExecutionSideEffectViolation> Validate(Operator? @operator)
    {
        if (@operator == null || !@operator.IsEnabled)
        {
            return Array.Empty<ExecutionSideEffectViolation>();
        }

        var required = ExecutionSideEffectCatalog.GetCapabilities(@operator);
        var disallowed = required & ~AllowedCapabilities;
        return disallowed == ExecutionSideEffect.None
            ? Array.Empty<ExecutionSideEffectViolation>()
            :
            [
                new ExecutionSideEffectViolation(
                    @operator.Id,
                    @operator.Name,
                    @operator.Type,
                    disallowed,
                    "SIDE_EFFECT_POLICY_BLOCKED",
                    $"{@operator.Type} requires '{disallowed}', which is not allowed in {RunMode}.")
            ];
    }
}

/// <summary>
/// Immutable definition of one execution.  The constructor captures a private
/// deep clone and every execution receives another clone, preventing admission
/// and execution from observing different mutable flow objects.
/// </summary>
public sealed class ExecutionSnapshot
{
    private readonly OperatorFlow _flow;
    private readonly ProjectGlobalVariableSchema _globalVariables;

    public ExecutionSnapshot(
        Guid projectId,
        OperatorFlow flow,
        long persistenceRevision,
        ExecutionSnapshotSource source,
        ExecutionRunMode runMode,
        IReadOnlyDictionary<string, string>? resourceBindings = null,
        string? runtimePackageId = null,
        ShadowExecutionRole shadowRole = ShadowExecutionRole.None,
        Guid? snapshotId = null,
        ProjectGlobalVariableSchema? globalVariables = null,
        OperatorFlow? executionIdentityFlow = null,
        ExecutionPrincipal? principal = null,
        ExecutionCapabilityManifest? capabilityManifest = null,
        long? expectedProjectRevision = null,
        string? confirmationId = null,
        string? auditId = null,
        Guid? sessionId = null,
        Guid? runId = null,
        ExecutionSideEffect externalCapabilities = ExecutionSideEffect.None)
    {
        if (projectId == Guid.Empty)
        {
            throw new ArgumentException("An execution snapshot requires a project id.", nameof(projectId));
        }

        ArgumentNullException.ThrowIfNull(flow);
        if (executionIdentityFlow != null &&
            (source != ExecutionSnapshotSource.RuntimePackage ||
             resourceBindings == null ||
             !resourceBindings.ContainsKey("PackageRoot")))
        {
            throw new ArgumentException(
                "A distinct execution identity flow is only valid for a runtime package with an explicit PackageRoot deployment binding.",
                nameof(executionIdentityFlow));
        }

        ProjectId = projectId;
        PersistenceRevision = persistenceRevision;
        Source = source;
        RunMode = runMode;
        ShadowRole = shadowRole;
        SnapshotId = snapshotId is { } requestedSnapshotId && requestedSnapshotId != Guid.Empty
            ? requestedSnapshotId
            : Guid.NewGuid();
        RuntimePackageId = NormalizeOptional(runtimePackageId);
        _flow = ExecutionFlowIdentity.CloneFlow(flow);
        _globalVariables = CloneGlobalVariables(globalVariables);
        var identityFlow = executionIdentityFlow == null
            ? _flow
            : ExecutionFlowIdentity.CloneFlow(executionIdentityFlow);
        FlowHash = ExecutionFlowIdentity.ComputeFlowHash(identityFlow);
        DecisionConfigurationHash = ExecutionFlowIdentity.ComputeDecisionConfigurationHash(identityFlow.DecisionConfiguration);
        ResourceBindings = new ReadOnlyDictionary<string, string>(
            (resourceBindings ?? new Dictionary<string, string>())
                .Where(pair => !string.IsNullOrWhiteSpace(pair.Key))
                .ToDictionary(pair => pair.Key.Trim(), pair => pair.Value?.Trim() ?? string.Empty, StringComparer.Ordinal));
        Principal = principal ?? ExecutionPrincipal.System();
        CapabilityManifest = capabilityManifest ?? new ExecutionCapabilityManifest(
            ExecutionCapabilityManifest.Derive(_flow).Capabilities | externalCapabilities,
            isExplicit: false);
        ExpectedProjectRevision = expectedProjectRevision;
        ConfirmationId = NormalizeOptional(confirmationId);
        AuditId = NormalizeOptional(auditId);
        if ((externalCapabilities & ~ExecutionSideEffect.DeviceRead) != ExecutionSideEffect.None)
        {
            throw new ArgumentOutOfRangeException(
                nameof(externalCapabilities),
                "Only server-bound external DeviceRead authority is supported.");
        }

        ExternalCapabilities = externalCapabilities;
        SessionId = sessionId is { } requestedSessionId && requestedSessionId != Guid.Empty
            ? requestedSessionId
            : SnapshotId;
        RunId = runId is { } requestedRunId && requestedRunId != Guid.Empty
            ? requestedRunId
            : Guid.NewGuid();
        FlowId = _flow.Id == Guid.Empty ? SnapshotId : _flow.Id;
    }

    public Guid SnapshotId { get; }
    public Guid ProjectId { get; }
    public long PersistenceRevision { get; }
    public string FlowHash { get; }
    public string DecisionConfigurationHash { get; }
    public ExecutionSnapshotSource Source { get; }
    public ExecutionRunMode RunMode { get; }
    public ShadowExecutionRole ShadowRole { get; }
    public string? RuntimePackageId { get; }
    public IReadOnlyDictionary<string, string> ResourceBindings { get; }
    public ExecutionPrincipal Principal { get; }
    public ExecutionCapabilityManifest CapabilityManifest { get; }
    public long? ExpectedProjectRevision { get; }
    public string? ConfirmationId { get; }
    public string? AuditId { get; }
    public ExecutionSideEffect ExternalCapabilities { get; }
    public Guid SessionId { get; }
    public Guid FlowId { get; }
    public Guid RunId { get; }
    public ExecutionSideEffectPolicy SideEffectPolicy => ExecutionSideEffectPolicy.For(RunMode);

    public OperatorFlow CreateExecutionFlow() => ExecutionFlowIdentity.CloneFlow(_flow);

    public ProjectGlobalVariableSchema CreateGlobalVariables() => CloneGlobalVariables(_globalVariables);

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static ProjectGlobalVariableSchema CloneGlobalVariables(ProjectGlobalVariableSchema? schema)
    {
        if (schema == null)
        {
            return new ProjectGlobalVariableSchema();
        }

        var json = JsonSerializer.Serialize(schema);
        return JsonSerializer.Deserialize<ProjectGlobalVariableSchema>(json)
            ?? new ProjectGlobalVariableSchema();
    }
}

/// <summary>Canonical execution identity and snapshot clone implementation.</summary>
public static class ExecutionFlowIdentity
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static string ComputeFlowHash(OperatorFlow flow)
    {
        ArgumentNullException.ThrowIfNull(flow);
        var canonical = new
        {
            operators = flow.Operators
                .OrderBy(@operator => @operator.Id)
                .Select(@operator => new
                {
                    id = @operator.Id,
                    type = OperatorTypeAliasResolver.Resolve(@operator.Type).ToString(),
                    enabled = @operator.IsEnabled,
                    inputs = @operator.InputPorts.OrderBy(port => port.Id).Select(port => new { port.Id, port.Name, type = port.DataType.ToString(), port.IsRequired }),
                    outputs = @operator.OutputPorts.OrderBy(port => port.Id).Select(port => new { port.Id, port.Name, type = port.DataType.ToString() }),
                    parameters = @operator.Parameters.OrderBy(parameter => parameter.Id).Select(parameter => new
                    {
                        parameter.Id,
                        parameter.Name,
                        parameter.DataType,
                        parameter.IsRequired,
                        defaultValue = CanonicalizeJson(parameter.DefaultValueJson),
                        value = CanonicalizeJson(parameter.ValueJson),
                        minimum = CanonicalizeJson(parameter.MinValueJson),
                        maximum = CanonicalizeJson(parameter.MaxValueJson),
                        options = CanonicalizeJson(parameter.OptionsJson)
                    })
                }),
            connections = flow.Connections
                .OrderBy(connection => connection.SourceOperatorId)
                .ThenBy(connection => connection.SourcePortId)
                .ThenBy(connection => connection.TargetOperatorId)
                .ThenBy(connection => connection.TargetPortId)
                .Select(connection => new { connection.SourceOperatorId, connection.SourcePortId, connection.TargetOperatorId, connection.TargetPortId }),
            decision = CanonicalizeJson(JsonSerializer.Serialize(flow.DecisionConfiguration, JsonOptions))
        };

        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(canonical, JsonOptions));
        return Convert.ToHexString(SHA256.HashData(bytes));
    }

    public static string ComputeDecisionConfigurationHash(DecisionConfiguration? configuration)
    {
        var canonical = CanonicalizeJson(JsonSerializer.Serialize(configuration, JsonOptions));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    public static OperatorFlow CloneFlow(OperatorFlow source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var clone = new OperatorFlow(source.Id, source.Name)
        {
            DecisionConfiguration = CloneDecisionConfiguration(source.DecisionConfiguration)
        };

        foreach (var sourceOperator in source.Operators)
        {
            var cloneOperator = new Operator(
                sourceOperator.Id,
                sourceOperator.Name,
                OperatorTypeAliasResolver.Resolve(sourceOperator.Type),
                sourceOperator.Position.X,
                sourceOperator.Position.Y);
            if (!sourceOperator.IsEnabled)
            {
                cloneOperator.Disable();
            }

            foreach (var port in sourceOperator.InputPorts)
            {
                cloneOperator.LoadInputPort(port.Id, port.Name, port.DataType, port.IsRequired);
            }

            foreach (var port in sourceOperator.OutputPorts)
            {
                cloneOperator.LoadOutputPort(port.Id, port.Name, port.DataType);
            }

            foreach (var parameter in sourceOperator.Parameters)
            {
                var cloneParameter = new Parameter(
                    parameter.Id,
                    parameter.Name,
                    parameter.DisplayName,
                    parameter.Description,
                    parameter.DataType,
                    CloneParameterValue(parameter.DefaultValue),
                    CloneParameterValue(parameter.MinValue),
                    CloneParameterValue(parameter.MaxValue),
                    parameter.IsRequired,
                    parameter.Options?.Select(option => new ParameterOption { Label = option.Label, Value = option.Value }).ToList());
                cloneParameter.SetValue(CloneParameterValue(parameter.Value));
                cloneOperator.AddParameter(cloneParameter);
            }

            clone.AddOperator(cloneOperator);
        }

        foreach (var connection in source.Connections)
        {
            // Preserve the captured graph exactly, including legacy/imported graphs whose
            // connections predate port metadata. Admission remains responsible for deciding
            // whether such a graph is executable; snapshot construction must not mutate or
            // reject the object before the caller reaches that boundary.
            clone.Connections.Add(new OperatorConnection(
                connection.SourceOperatorId,
                connection.SourcePortId,
                connection.TargetOperatorId,
                connection.TargetPortId));
        }

        return clone;
    }

    private static object? CloneParameterValue(object? value)
    {
        if (value == null)
        {
            return null;
        }

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(value, JsonOptions));
        return CloneElement(document.RootElement);
    }

    private static object? CloneElement(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Null or JsonValueKind.Undefined => null,
        JsonValueKind.String => element.GetString(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Number when element.TryGetInt64(out var integer) => integer,
        JsonValueKind.Number => element.GetDouble(),
        JsonValueKind.Array => element.EnumerateArray().Select(CloneElement).ToArray(),
        JsonValueKind.Object => element.EnumerateObject().ToDictionary(property => property.Name, property => CloneElement(property.Value)),
        _ => element.GetRawText()
    };

    private static DecisionConfiguration? CloneDecisionConfiguration(DecisionConfiguration? configuration)
    {
        if (configuration == null)
        {
            return null;
        }

        var json = JsonSerializer.Serialize(configuration, JsonOptions);
        return JsonSerializer.Deserialize<DecisionConfiguration>(json, JsonOptions);
    }

    private static string CanonicalizeJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return "null";
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream))
            {
                WriteCanonicalElement(writer, document.RootElement);
            }

            return Encoding.UTF8.GetString(stream.ToArray());
        }
        catch (JsonException)
        {
            // Old data can contain non-JSON scalar values. Preserve it with a
            // stable JSON representation rather than failing identity creation.
            return JsonSerializer.Serialize(json, JsonOptions);
        }
    }

    private static void WriteCanonicalElement(Utf8JsonWriter writer, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject().OrderBy(property => property.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonicalElement(writer, property.Value);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                {
                    WriteCanonicalElement(writer, item);
                }
                writer.WriteEndArray();
                break;
            default:
                element.WriteTo(writer);
                break;
        }
    }
}
