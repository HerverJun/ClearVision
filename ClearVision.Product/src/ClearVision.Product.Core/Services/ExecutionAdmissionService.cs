using ClearVision.Product.Core.Decisions;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Interfaces;

namespace ClearVision.Product.Core.Services;

/// <summary>
/// 执行准入 surface：明确区分“正式运行”（允许流程声明的真实 I/O）与
/// “预览/调参/AI dry-run”（除已实现的 safe dry-run 外，禁止真实外部 I/O）。
/// </summary>
/// <remarks>
/// 正式运行 surface（<see cref="StoredProjectExecution"/> / <see cref="StudioInspectionRun"/> /
/// <see cref="StationRuntimeExecution"/>）只做“项目存在、项目激活”等非 I/O 安全校验，
/// 不再拦截 ImageAcquisition/ImageSave/TextSave/ResultOutput/TCP/PLC/HTTP/MQTT/DatabaseWrite/标定等副作用算子。
/// 预览 surface（<see cref="NodePreview"/> / <see cref="OperatorPreview"/> / <see cref="AutoTunePreview"/>）
/// 继续阻断真实外部 I/O；节点预览层已单独实现 ImageSave/TextSave/ResultOutput 的 safe dry-run。
/// <see cref="LegacyWebMessageExecution"/> 恒定禁用。
/// </remarks>
public enum ExecutionAdmissionSurface
{
    /// <summary>已授权的正式项目执行：使用项目已存储的流程运行，允许流程声明的真实 I/O。</summary>
    StoredProjectExecution = 0,

    /// <summary>Studio 检测页单次/连续“运行流程”，使用画布内联流程运行，允许流程声明的真实 I/O。</summary>
    StudioInspectionRun = 1,

    /// <summary>Station 现场正式运行（含内联流程），允许流程声明的真实 I/O。</summary>
    StationRuntimeExecution = 2,

    /// <summary>流程编辑器节点预览：禁止真实外部 I/O（写盘算子走 safe dry-run；File 图源允许读图）。</summary>
    NodePreview = 3,

    /// <summary>单算子预览/调参：禁止真实外部 I/O。</summary>
    OperatorPreview = 4,

    /// <summary>旧 WebMessage 执行命令：恒定禁用。</summary>
    LegacyWebMessageExecution = 5,

    /// <summary>AutoTune 线序预览与指标分析：禁止真实外部 I/O（File 图源允许读图，与节点预览一致）。</summary>
    AutoTunePreview = 6,

    ShadowCandidate = 7,

    Debug = 8
}

public sealed record ExecutionAdmissionViolation(
    Guid OperatorId,
    string OperatorName,
    OperatorType OperatorType,
    string Reason,
    string? ParameterName = null,
    string? Code = null);

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

    ExecutionAdmissionResult ValidateFlowDefinition(
        OperatorFlow? flow,
        ExecutionAdmissionSurface surface);

    ExecutionAdmissionResult ValidateFlowSideEffects(
        OperatorFlow? flow,
        ExecutionAdmissionSurface surface);

    ExecutionAdmissionResult ValidateOperator(
        Operator @operator,
        ExecutionAdmissionSurface surface);

    ExecutionAdmissionResult ValidateSnapshot(
        ExecutionSnapshot snapshot,
        ExecutionAdmissionSurface surface);

    ExecutionAdmissionResult ValidateLegacyWebMessage(string messageType);
}

public sealed class ExecutionAdmissionService : IExecutionAdmissionService
{
    private readonly IProjectRepository _projectRepository;
    private readonly IInspectionRuntimeCoordinator? _runtimeCoordinator;
    private readonly IFlowDefinitionValidator? _flowDefinitionValidator;

    public ExecutionAdmissionService(
        IProjectRepository projectRepository,
        IInspectionRuntimeCoordinator? runtimeCoordinator = null,
        IFlowDefinitionValidator? flowDefinitionValidator = null)
    {
        _projectRepository = projectRepository;
        _runtimeCoordinator = runtimeCoordinator;
        _flowDefinitionValidator = flowDefinitionValidator;
    }

    /// <summary>
    /// 正式运行 surface：允许流程声明的真实 I/O，仅保留非 I/O 安全校验（项目存在/激活等）。
    /// </summary>
    public static bool IsOfficialExecutionSurface(ExecutionAdmissionSurface surface) =>
        surface is ExecutionAdmissionSurface.StoredProjectExecution
            or ExecutionAdmissionSurface.StudioInspectionRun
            or ExecutionAdmissionSurface.StationRuntimeExecution;

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
        if (!projectAdmission.IsAllowed)
        {
            // 正式运行 surface：项目校验通过即放行，允许流程声明的真实 I/O。
            return projectAdmission;
        }

        var runtimeState = _runtimeCoordinator?.GetState(projectId);
        if (IsOfficialExecutionSurface(surface) &&
            runtimeState?.Status is RuntimeStatus.Starting or RuntimeStatus.Running or RuntimeStatus.Stopping)
        {
            return ExecutionAdmissionResult.Reject(
                "ADMISSION_RUNTIME_ALREADY_ACTIVE",
                $"Project '{projectId}' already has an active runtime session.");
        }

        return ValidateFlowDefinition(flow, surface);
    }

    public ExecutionAdmissionResult ValidateFlowDefinition(
        OperatorFlow? flow,
        ExecutionAdmissionSurface surface)
    {
        var admission = ValidateStandaloneFlow(flow, surface);
        if (!admission.IsAllowed || flow == null || _flowDefinitionValidator == null)
        {
            return admission;
        }

        var validation = _flowDefinitionValidator.ValidateFlow(flow);
        if (validation == null)
        {
            return admission;
        }
        return validation.IsValid
            ? admission
            : ExecutionAdmissionResult.Reject(
                "ADMISSION_FLOW_INVALID",
                validation.Errors.Count > 0
                    ? string.Join("; ", validation.Errors)
                    : "Flow validation failed.");
    }

    public static ExecutionAdmissionResult ValidateStandaloneFlow(
        OperatorFlow? flow,
        ExecutionAdmissionSurface surface)
    {
        if (!HasExecutableFlow(flow))
        {
            return ExecutionAdmissionResult.Reject(
                "ADMISSION_FLOW_REQUIRED",
                "An executable flow is required.");
        }

        var structure = ValidateStructureAndRequiredParameters(flow!);
        if (!structure.IsAllowed)
        {
            return structure;
        }

        if (IsOfficialExecutionSurface(surface))
        {
            var decisionIssues = FinalDecisionResolver.Validate(flow);
            if (decisionIssues.Count > 0)
            {
                var violations = decisionIssues.Select(issue =>
                {
                    var op = issue.OperatorId.HasValue
                        ? flow!.Operators.FirstOrDefault(candidate => candidate.Id == issue.OperatorId.Value)
                        : null;
                    return new ExecutionAdmissionViolation(
                        issue.OperatorId ?? Guid.Empty,
                        op?.Name ?? string.Empty,
                        op?.Type ?? default,
                        issue.Message,
                        issue.OutputName,
                        issue.Code);
                }).ToList();
                return ExecutionAdmissionResult.Reject(
                    $"ADMISSION_{decisionIssues[0].Code}",
                    decisionIssues[0].Message,
                    violations);
            }

            return ExecutionAdmissionResult.Allow();
        }

        return ValidateFlowSideEffectsCore(flow, surface);
    }

    public sealed class ExecutionAdmissionRejectedException : InvalidOperationException
    {
        public ExecutionAdmissionRejectedException(ExecutionAdmissionResult admission)
            : base($"{admission.Code}: {admission.Message}")
        {
            Admission = admission;
        }

        public ExecutionAdmissionResult Admission { get; }
    }

    public ExecutionAdmissionResult ValidateFlowSideEffects(
        OperatorFlow? flow,
        ExecutionAdmissionSurface surface)
    {
        return ValidateFlowSideEffectsCore(flow, surface);
    }

    private static ExecutionAdmissionResult ValidateFlowSideEffectsCore(
        OperatorFlow? flow,
        ExecutionAdmissionSurface surface)
    {
        if (surface == ExecutionAdmissionSurface.LegacyWebMessageExecution)
        {
            return RejectLegacyWebMessage();
        }

        if (IsOfficialExecutionSurface(surface) ||
            !HasExecutableFlow(flow))
        {
            // 正式运行 surface 不拦截副作用算子；空流程无副作用可查。
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

        if (IsOfficialExecutionSurface(surface))
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

    public ExecutionAdmissionResult ValidateSnapshot(
        ExecutionSnapshot snapshot,
        ExecutionAdmissionSurface surface)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var runtimeState = _runtimeCoordinator?.GetState(snapshot.ProjectId);
        if (IsOfficialExecutionSurface(surface) &&
            runtimeState?.Status is RuntimeStatus.Starting or RuntimeStatus.Running or RuntimeStatus.Stopping)
        {
            return ExecutionAdmissionResult.Reject(
                "ADMISSION_RUNTIME_ALREADY_ACTIVE",
                $"Project '{snapshot.ProjectId}' already has an active runtime session.");
        }

        return ValidateFlowDefinition(snapshot.CreateExecutionFlow(), surface);
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

    private static ExecutionAdmissionResult ValidateStructureAndRequiredParameters(OperatorFlow flow)
    {
        var violations = new List<ExecutionAdmissionViolation>();
        var duplicateOperators = flow.Operators
            .GroupBy(op => op.Id)
            .Where(group => group.Key == Guid.Empty || group.Count() > 1)
            .SelectMany(group => group);
        foreach (var op in duplicateOperators)
        {
            violations.Add(new(op.Id, op.Name, op.Type, "Operator IDs must be non-empty and unique.", Code: "FLOW_OPERATOR_ID_INVALID"));
        }

        var operatorById = flow.Operators
            .GroupBy(op => op.Id)
            .ToDictionary(group => group.Key, group => group.First());
        foreach (var connection in flow.Connections)
        {
            if (!operatorById.TryGetValue(connection.SourceOperatorId, out var source) ||
                !operatorById.TryGetValue(connection.TargetOperatorId, out var target) ||
                source.OutputPorts.All(port => port.Id != connection.SourcePortId) ||
                target.InputPorts.All(port => port.Id != connection.TargetPortId))
            {
                violations.Add(new(
                    connection.SourceOperatorId,
                    source?.Name ?? string.Empty,
                    source?.Type ?? default,
                    "Connection references a missing operator or port.",
                    Code: "FLOW_CONNECTION_INVALID"));
            }
        }

        foreach (var op in flow.Operators.Where(op => op.IsEnabled))
        {
            foreach (var parameter in op.Parameters.Where(parameter => parameter.IsRequired))
            {
                var value = parameter.GetValue();
                if (value == null)
                {
                    violations.Add(new(
                        op.Id,
                        op.Name,
                        op.Type,
                        $"Required parameter '{parameter.Name}' is missing.",
                        parameter.Name,
                        "FLOW_REQUIRED_PARAMETER_MISSING"));
                }
            }
        }

        return violations.Count == 0
            ? ExecutionAdmissionResult.Allow()
            : ExecutionAdmissionResult.Reject(
                $"ADMISSION_{violations[0].Code}",
                violations[0].Reason,
                violations);
    }

    private static ExecutionAdmissionViolation? TryCreateViolation(
        Operator @operator,
        ExecutionAdmissionSurface surface)
    {
        if (IsOfficialExecutionSurface(surface))
        {
            // 正式运行 surface：允许流程声明的真实 I/O，不产生副作用违规。
            return null;
        }

        var policy = ExecutionSideEffectPolicy.For(surface switch
        {
            ExecutionAdmissionSurface.ShadowCandidate => ExecutionRunMode.ShadowCandidate,
            ExecutionAdmissionSurface.Debug => ExecutionRunMode.Debug,
            _ => ExecutionRunMode.Preview
        });
        var allowedCapabilities = policy.AllowedCapabilities;
        if (surface == ExecutionAdmissionSurface.OperatorPreview)
        {
            // Single-operator preview has no bounded upstream sample context;
            // retain its stricter no-file-read contract.
            allowedCapabilities &= ~ExecutionSideEffect.FileRead;
        }

        var blockedCapabilities = ExecutionSideEffectCatalog.GetCapabilities(@operator) & ~allowedCapabilities;
        return blockedCapabilities == ExecutionSideEffect.None
            ? null
            : CreateViolation(
                @operator,
                $"{@operator.Type} requires '{blockedCapabilities}', which is not allowed in {policy.RunMode}.",
                ExecutionSideEffectCatalog.GetCapabilityParameterName(@operator, blockedCapabilities));
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
            ExecutionAdmissionSurface.AutoTunePreview => "ADMISSION_AUTOTUNE_PREVIEW_SIDE_EFFECT_BLOCKED",
            _ => "ADMISSION_PREVIEW_SIDE_EFFECT_BLOCKED"
        };

    private static string BuildBlockedMessage(
        ExecutionAdmissionSurface surface,
        IReadOnlyList<ExecutionAdmissionViolation> violations)
    {
        // 仅预览 surface 会走到这里；正式运行 surface 已在上游放行，不会套用“预览”文案。
        var first = violations[0];
        var surfaceName = surface switch
        {
            ExecutionAdmissionSurface.OperatorPreview => "算子预览",
            ExecutionAdmissionSurface.NodePreview => "节点预览",
            ExecutionAdmissionSurface.AutoTunePreview => "线序预览",
            _ => "预览"
        };

        return $"{surfaceName}已安全拦截副作用算子“{first.OperatorName}”（{first.OperatorType}）：{first.Reason}预览不会访问外部设备、网络服务或执行文件系统写入，正式运行流程时才会执行。";
    }

}
