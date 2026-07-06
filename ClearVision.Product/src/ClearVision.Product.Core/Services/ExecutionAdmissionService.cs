using System.Text.Json;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Interfaces;

namespace ClearVision.Product.Core.Services;

public enum ExecutionAdmissionSurface
{
    StoredProjectExecution = 0,
    InlineOfficialExecution = 1,
    RealtimeInlineExecution = 2,
    NodePreview = 3,
    OperatorPreview = 4,
    LegacyWebMessageExecution = 5
}

public sealed record ExecutionAdmissionViolation(
    Guid OperatorId,
    string OperatorName,
    OperatorType OperatorType,
    string Reason,
    string? ParameterName = null);

public sealed class ExecutionAdmissionResult
{
    private ExecutionAdmissionResult(
        bool isAllowed,
        string code,
        string message,
        IReadOnlyList<ExecutionAdmissionViolation> violations)
    {
        IsAllowed = isAllowed;
        Code = code;
        Message = message;
        Violations = violations;
    }

    public bool IsAllowed { get; }

    public string Code { get; }

    public string Message { get; }

    public IReadOnlyList<ExecutionAdmissionViolation> Violations { get; }

    public static ExecutionAdmissionResult Allow() =>
        new(true, "ADMISSION_ALLOWED", string.Empty, Array.Empty<ExecutionAdmissionViolation>());

    public static ExecutionAdmissionResult Reject(
        string code,
        string message,
        IReadOnlyList<ExecutionAdmissionViolation>? violations = null) =>
        new(false, code, message, violations ?? Array.Empty<ExecutionAdmissionViolation>());
}

public interface IExecutionAdmissionService
{
    Task<ExecutionAdmissionResult> ValidateProjectAsync(
        Guid projectId,
        ExecutionAdmissionSurface surface,
        CancellationToken cancellationToken = default);

    Task<ExecutionAdmissionResult> ValidateFlowAsync(
        Guid projectId,
        OperatorFlow? flow,
        ExecutionAdmissionSurface surface,
        CancellationToken cancellationToken = default);

    ExecutionAdmissionResult ValidateOperator(
        Operator @operator,
        ExecutionAdmissionSurface surface);

    ExecutionAdmissionResult ValidateLegacyWebMessage(string messageType);
}

public sealed class ExecutionAdmissionService : IExecutionAdmissionService
{
    private static readonly HashSet<OperatorType> AlwaysBlockedSideEffectTypes =
    [
        OperatorType.HttpRequest,
        OperatorType.TextSave,
        OperatorType.ImageSave,
        OperatorType.DatabaseWrite,
        OperatorType.TcpCommunication,
        OperatorType.SerialCommunication,
        OperatorType.ModbusCommunication,
        OperatorType.ModbusRtuCommunication,
        OperatorType.SiemensS7Communication,
        OperatorType.MitsubishiMcCommunication,
        OperatorType.OmronFinsCommunication,
        OperatorType.MqttPublish,
        OperatorType.CameraCalibration,
        OperatorType.FisheyeCalibration,
        OperatorType.StereoCalibration,
        OperatorType.NPointCalibration,
        OperatorType.TranslationRotationCalibration,
        OperatorType.HandEyeCalibration,
        OperatorType.CalibrationLoader
    ];

    private readonly IProjectRepository _projectRepository;

    public ExecutionAdmissionService(IProjectRepository projectRepository)
    {
        _projectRepository = projectRepository;
    }

    public async Task<ExecutionAdmissionResult> ValidateProjectAsync(
        Guid projectId,
        ExecutionAdmissionSurface surface,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (surface == ExecutionAdmissionSurface.LegacyWebMessageExecution)
        {
            return RejectLegacyWebMessage();
        }

        if (projectId == Guid.Empty)
        {
            return ExecutionAdmissionResult.Reject(
                "ADMISSION_PROJECT_REQUIRED",
                "An active projectId is required for execution.");
        }

        var project = await _projectRepository.GetByIdFreshAsync(projectId);
        cancellationToken.ThrowIfCancellationRequested();

        return project == null
            ? ExecutionAdmissionResult.Reject(
                "ADMISSION_PROJECT_NOT_ACTIVE",
                $"Project '{projectId}' does not exist or has been deleted.")
            : ExecutionAdmissionResult.Allow();
    }

    public async Task<ExecutionAdmissionResult> ValidateFlowAsync(
        Guid projectId,
        OperatorFlow? flow,
        ExecutionAdmissionSurface surface,
        CancellationToken cancellationToken = default)
    {
        if (surface == ExecutionAdmissionSurface.LegacyWebMessageExecution)
        {
            return RejectLegacyWebMessage();
        }

        var projectAdmission = await ValidateProjectAsync(projectId, surface, cancellationToken);
        if (!projectAdmission.IsAllowed || surface == ExecutionAdmissionSurface.StoredProjectExecution)
        {
            return projectAdmission;
        }

        if (!HasExecutableFlow(flow))
        {
            return ExecutionAdmissionResult.Allow();
        }

        var violations = flow!.Operators
            .Where(op => op.IsEnabled)
            .Select(op => TryCreateViolation(op, surface))
            .Where(violation => violation != null)
            .Cast<ExecutionAdmissionViolation>()
            .ToList();

        return violations.Count == 0
            ? ExecutionAdmissionResult.Allow()
            : ExecutionAdmissionResult.Reject(
                ResolveBlockedCode(surface),
                BuildBlockedMessage(surface, violations),
                violations);
    }

    public ExecutionAdmissionResult ValidateOperator(
        Operator @operator,
        ExecutionAdmissionSurface surface)
    {
        if (surface == ExecutionAdmissionSurface.LegacyWebMessageExecution)
        {
            return RejectLegacyWebMessage();
        }

        if (surface == ExecutionAdmissionSurface.StoredProjectExecution)
        {
            return ExecutionAdmissionResult.Allow();
        }

        var violation = TryCreateViolation(@operator, surface);
        return violation == null
            ? ExecutionAdmissionResult.Allow()
            : ExecutionAdmissionResult.Reject(
                ResolveBlockedCode(surface),
                BuildBlockedMessage(surface, [violation]),
                [violation]);
    }

    public ExecutionAdmissionResult ValidateLegacyWebMessage(string messageType)
    {
        return ExecutionAdmissionResult.Reject(
            "ADMISSION_LEGACY_WEBMESSAGE_DISABLED",
            $"Legacy WebMessage '{messageType}' is disabled. Use the authenticated HTTP API instead.");
    }

    private static ExecutionAdmissionResult RejectLegacyWebMessage() =>
        ExecutionAdmissionResult.Reject(
            "ADMISSION_LEGACY_WEBMESSAGE_DISABLED",
            "Legacy WebMessage execution commands are disabled. Use the authenticated HTTP API instead.");

    private static bool HasExecutableFlow(OperatorFlow? flow) =>
        flow?.Operators?.Count > 0;

    private static ExecutionAdmissionViolation? TryCreateViolation(
        Operator @operator,
        ExecutionAdmissionSurface surface)
    {
        if (surface == ExecutionAdmissionSurface.StoredProjectExecution)
        {
            return null;
        }

        if (AlwaysBlockedSideEffectTypes.Contains(@operator.Type))
        {
            return CreateViolation(
                @operator,
                $"{@operator.Type} can perform external I/O or persistent side effects.");
        }

        if (@operator.Type == OperatorType.ResultOutput &&
            TryReadBoolParameter(@operator, "SaveToFile", out var saveToFile) &&
            saveToFile)
        {
            return CreateViolation(
                @operator,
                "ResultOutput with SaveToFile=true writes local files.",
                "SaveToFile");
        }

        if (@operator.Type == OperatorType.ImageAcquisition)
        {
            var sourceType = NormalizeOptionValue(TryReadStringParameter(@operator, "SourceType", "sourceType"));
            var filePath = TryReadStringParameter(@operator, "FilePath", "filePath");
            if (sourceType.Equals("Camera", StringComparison.OrdinalIgnoreCase))
            {
                return CreateViolation(
                    @operator,
                    "ImageAcquisition with SourceType=Camera can access local camera hardware.",
                    "SourceType");
            }

            if (!string.IsNullOrWhiteSpace(filePath))
            {
                return CreateViolation(
                    @operator,
                    "ImageAcquisition with FilePath can read local files.",
                    "FilePath");
            }
        }

        return null;
    }

    private static ExecutionAdmissionViolation CreateViolation(
        Operator @operator,
        string reason,
        string? parameterName = null) =>
        new(@operator.Id, @operator.Name, @operator.Type, reason, parameterName);

    private static string ResolveBlockedCode(ExecutionAdmissionSurface surface) =>
        surface switch
        {
            ExecutionAdmissionSurface.OperatorPreview => "ADMISSION_OPERATOR_PREVIEW_SIDE_EFFECT_BLOCKED",
            ExecutionAdmissionSurface.NodePreview => "ADMISSION_NODE_PREVIEW_SIDE_EFFECT_BLOCKED",
            ExecutionAdmissionSurface.RealtimeInlineExecution => "ADMISSION_REALTIME_INLINE_SIDE_EFFECT_BLOCKED",
            _ => "ADMISSION_INLINE_SIDE_EFFECT_BLOCKED"
        };

    private static string BuildBlockedMessage(
        ExecutionAdmissionSurface surface,
        IReadOnlyList<ExecutionAdmissionViolation> violations)
    {
        var first = violations[0];
        var surfaceName = surface switch
        {
            ExecutionAdmissionSurface.OperatorPreview => "operator preview",
            ExecutionAdmissionSurface.NodePreview => "node preview",
            ExecutionAdmissionSurface.RealtimeInlineExecution => "realtime inline execution",
            _ => "inline execution"
        };

        return $"{surfaceName} blocked side-effect operator '{first.OperatorName}' ({first.OperatorType}): {first.Reason}";
    }

    private static bool TryReadBoolParameter(Operator @operator, string name, out bool value)
    {
        value = false;
        var raw = TryReadParameter(@operator, name);
        switch (raw)
        {
            case bool boolValue:
                value = boolValue;
                return true;
            case string text when bool.TryParse(text, out var parsed):
                value = parsed;
                return true;
            case JsonElement element:
                if (element.ValueKind == JsonValueKind.True || element.ValueKind == JsonValueKind.False)
                {
                    value = element.GetBoolean();
                    return true;
                }

                if (element.ValueKind == JsonValueKind.String &&
                    bool.TryParse(element.GetString(), out var jsonParsed))
                {
                    value = jsonParsed;
                    return true;
                }

                break;
        }

        return false;
    }

    private static string TryReadStringParameter(Operator @operator, params string[] names)
    {
        foreach (var name in names)
        {
            var raw = TryReadParameter(@operator, name);
            var value = raw switch
            {
                null => null,
                string text => text,
                JsonElement element when element.ValueKind == JsonValueKind.String => element.GetString(),
                JsonElement element when element.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined => element.ToString(),
                _ => raw.ToString()
            };

            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return string.Empty;
    }

    private static object? TryReadParameter(Operator @operator, string name)
    {
        return @operator.Parameters
            .FirstOrDefault(parameter => parameter.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            ?.GetValue();
    }

    private static string NormalizeOptionValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value.Trim();
        var separatorIndex = normalized.IndexOf('|', StringComparison.Ordinal);
        return separatorIndex >= 0
            ? normalized[..separatorIndex].Trim()
            : normalized;
    }
}
