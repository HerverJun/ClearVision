// DryRunService.cs
// 仿真执行服务 - Sprint 4 Task 4.2
// 支持双向仿真，可注入 Stub 响应
// 作者：蘅芜君

using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.ProjectVariables;
using ClearVision.Product.Core.Services;

namespace ClearVision.Product.Infrastructure.AI.DryRun;

/// <summary>
/// 仿真执行服务
/// 在 DryRun 模式下执行流程，使用 StubRegistry 模拟外部设备响应
/// </summary>
public class DryRunService
{
    internal const int MaxInputCount = 64;
    internal const int MaxBinaryInputBytes = 16 * 1024 * 1024;
    internal const int MaxStringInputCharacters = 1024 * 1024;
    internal const int MaxTotalInputBytes = 16 * 1024 * 1024;

    private readonly IFlowExecutionService _flowExecutionService;

    public DryRunService(IFlowExecutionService flowExecutionService)
    {
        _flowExecutionService = flowExecutionService;
    }

    /// <summary>
    /// 执行仿真运行
    /// </summary>
    /// <param name="flow">要仿真的流程</param>
    /// <param name="testInputs">测试输入数据（图像等）</param>
    /// <param name="stubRegistry">数据挡板注册表</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>仿真结果，包含分支覆盖率信息</returns>
    public async Task<DryRunResult> RunAsync(
        OperatorFlow flow,
        Dictionary<string, object> testInputs,
        DryRunStubRegistry stubRegistry,
        CancellationToken cancellationToken = default)
    {
        return await RunAsync(flow, testInputs, stubRegistry, null, cancellationToken);
    }

    public async Task<DryRunResult> RunAsync(
        OperatorFlow flow,
        Dictionary<string, object> testInputs,
        DryRunStubRegistry stubRegistry,
        ProjectVariableExecutionContext? projectVariables,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(flow);
        ArgumentNullException.ThrowIfNull(testInputs);
        ArgumentNullException.ThrowIfNull(stubRegistry);
        cancellationToken.ThrowIfCancellationRequested();

        var result = new DryRunResult
        {
            StartTime = DateTime.UtcNow,
            FlowId = flow.Id,
            FlowName = flow.Name
        };

        Dictionary<string, object> boundedInputs;
        try
        {
            boundedInputs = new Dictionary<string, object>(testInputs, StringComparer.Ordinal);
        }
        catch
        {
            return CompleteRejected(
                result,
                "ADMISSION_DRY_RUN_INPUT_INVALID",
                "Dry-run inputs could not be captured safely.");
        }

        var inputViolation = ValidateInputs(boundedInputs);
        if (inputViolation != null)
        {
            return CompleteRejected(result, "ADMISSION_DRY_RUN_INPUT_BOUNDS", inputViolation);
        }

        // This is an internal, offline-only preview authority. Every run gets
        // an isolated synthetic project/session/run identity, an explicit
        // derived capability manifest, and distinct confirmation/audit ids.
        // Preview policy remains authoritative and rejects every external I/O
        // capability before an operator handler can be selected.
        var snapshot = CreateSandboxSnapshot(flow);
        var authority = ExecutionAuthorityMatrix.Validate(snapshot);
        if (!authority.Allowed)
        {
            return CompleteRejected(result, authority.Code, authority.Message);
        }

        FlowValidationResult? validation;
        try
        {
            validation = _flowExecutionService.ValidateSnapshot(snapshot);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return CompleteRejected(
                result,
                "ADMISSION_DRY_RUN_VALIDATION_UNAVAILABLE",
                "Dry-run flow validation is unavailable.");
        }

        if (validation is not { IsValid: true })
        {
            var message = validation?.Errors.Count > 0
                ? string.Join("; ", validation.Errors)
                : "Dry-run flow validation failed closed.";
            return CompleteRejected(result, "ADMISSION_DRY_RUN_FLOW_INVALID", message);
        }

        var previousContext = DryRunContext.Current;
        var runContext = new DryRunContext
        {
            IsDryRun = true,
            StubRegistry = stubRegistry,
            BranchExecutionCounts = new Dictionary<string, int>(),
            SnapshotId = snapshot.SnapshotId,
            SessionId = snapshot.SessionId,
            RunId = snapshot.RunId
        };
        DryRunContext.Current = runContext;

        try
        {
            // 执行流程
            var flowResult = projectVariables == null
                ? await _flowExecutionService.ExecuteWithSnapshotAsync(
                    snapshot,
                    boundedInputs,
                    enableParallel: false,
                    cancellationToken)
                : await _flowExecutionService.ExecuteWithSnapshotAsync(
                    snapshot,
                    boundedInputs,
                    new ProjectVariableExecutionContext(
                        projectVariables.Session,
                        projectVariables.BindingIndex,
                        projectVariables.RunId,
                        isPreview: true),
                    enableParallel: false,
                    cancellationToken);

            result.FlowResult = flowResult;
            result.IsSuccess = flowResult.IsSuccess;

            // 收集分支覆盖信息
            result.BranchExecutionCounts = runContext.BranchExecutionCounts;
            result.TotalBranches = result.BranchExecutionCounts.Count;
            result.CoveredBranches = result.BranchExecutionCounts.Count(x => x.Value > 0);
            result.CoveragePercentage = result.TotalBranches > 0
                ? (double)result.CoveredBranches / result.TotalBranches * 100
                : 100;
        }
        finally
        {
            DryRunContext.Current = previousContext;
        }

        result.EndTime = DateTime.UtcNow;
        result.DurationMs = (result.EndTime - result.StartTime).TotalMilliseconds;

        return result;
    }

    private static ExecutionSnapshot CreateSandboxSnapshot(OperatorFlow flow)
    {
        const long revision = 0;
        var sessionId = Guid.NewGuid();
        var runId = Guid.NewGuid();
        var flowHash = ExecutionFlowIdentity.ComputeFlowHash(flow);
        return new ExecutionSnapshot(
            Guid.NewGuid(),
            flow,
            revision,
            ExecutionSnapshotSource.Draft,
            ExecutionRunMode.Preview,
            new Dictionary<string, string>
            {
                ["ProjectRevision"] = revision.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["FlowHash"] = flowHash,
                ["ExecutionScope"] = "OfflineDryRun"
            },
            principal: new ExecutionPrincipal(
                $"dry-run:{sessionId:N}",
                "AI Dry-Run Sandbox",
                "Engineer",
                IsAuthenticated: true),
            capabilityManifest: ExecutionCapabilityManifest.Derive(flow, isExplicit: true),
            expectedProjectRevision: revision,
            confirmationId: Guid.NewGuid().ToString("D"),
            auditId: Guid.NewGuid().ToString("D"),
            sessionId: sessionId,
            runId: runId);
    }

    private static string? ValidateInputs(IReadOnlyDictionary<string, object> inputs)
    {
        if (inputs.Count > MaxInputCount)
        {
            return $"Dry-run input count exceeds the hard limit of {MaxInputCount}.";
        }

        long totalInputBytes = 0;
        foreach (var (name, value) in inputs)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return "Dry-run input names must be non-empty.";
            }

            switch (value)
            {
                case byte[] bytes:
                    if (bytes.Length > MaxBinaryInputBytes)
                    {
                        return $"Dry-run binary input '{name}' exceeds the hard size limit.";
                    }
                    totalInputBytes += bytes.Length;
                    break;
                case string text:
                    if (text.Length > MaxStringInputCharacters)
                    {
                        return $"Dry-run text input '{name}' exceeds the hard size limit.";
                    }
                    totalInputBytes += (long)text.Length * sizeof(char);
                    break;
                case Stream:
                    return $"Dry-run input '{name}' cannot carry a live stream or file handle.";
            }

            if (totalInputBytes > MaxTotalInputBytes)
            {
                return $"Dry-run inputs exceed the total hard budget of {MaxTotalInputBytes} bytes.";
            }
        }

        return null;
    }

    private static DryRunResult CompleteRejected(DryRunResult result, string code, string message)
    {
        result.IsSuccess = false;
        result.FlowResult = new FlowExecutionResult
        {
            IsSuccess = false,
            ErrorMessage = $"{code}: {message}"
        };
        result.EndTime = DateTime.UtcNow;
        result.DurationMs = (result.EndTime - result.StartTime).TotalMilliseconds;
        return result;
    }

    /// <summary>
    /// 批量执行多组测试用例
    /// </summary>
    public async Task<DryRunBatchResult> RunBatchAsync(
        OperatorFlow flow,
        List<Dictionary<string, object>> testCases,
        DryRunStubRegistry stubRegistry,
        CancellationToken cancellationToken = default)
    {
        var results = new List<DryRunResult>();
        var allBranches = new HashSet<string>();

        foreach (var testCase in testCases)
        {
            var result = await RunAsync(flow, testCase, stubRegistry, cancellationToken);
            results.Add(result);

            foreach (var branch in result.BranchExecutionCounts.Keys)
            {
                allBranches.Add(branch);
            }
        }

        // 汇总覆盖率
        var totalExecutions = new Dictionary<string, int>();
        foreach (var branch in allBranches)
        {
            totalExecutions[branch] = results.Sum(r => r.BranchExecutionCounts.GetValueOrDefault(branch, 0));
        }

        return new DryRunBatchResult
        {
            TotalTestCases = testCases.Count,
            Results = results,
            CombinedBranchExecutionCounts = totalExecutions,
            TotalBranches = allBranches.Count,
            CoveredBranches = totalExecutions.Count(x => x.Value > 0),
            CoveragePercentage = allBranches.Count > 0
                ? (double)totalExecutions.Count(x => x.Value > 0) / allBranches.Count * 100
                : 100
        };
    }
}

/// <summary>
/// DryRun async-flow-local 上下文
/// </summary>
public class DryRunContext
{
    private static readonly AsyncLocal<DryRunContext?> CurrentContext = new();

    public static DryRunContext? Current
    {
        get => CurrentContext.Value;
        set => CurrentContext.Value = value;
    }

    public bool IsDryRun { get; set; }
    public DryRunStubRegistry? StubRegistry { get; set; }
    public Dictionary<string, int> BranchExecutionCounts { get; set; } = new();
    public Guid SnapshotId { get; set; }
    public Guid SessionId { get; set; }
    public Guid RunId { get; set; }

    /// <summary>
    /// 记录分支执行
    /// </summary>
    public void RecordBranchExecution(string branchId)
    {
        if (BranchExecutionCounts.ContainsKey(branchId))
            BranchExecutionCounts[branchId]++;
        else
            BranchExecutionCounts[branchId] = 1;
    }
}

/// <summary>
/// 单次仿真结果
/// </summary>
public class DryRunResult
{
    public Guid FlowId { get; set; }
    public string FlowName { get; set; } = string.Empty;
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public double DurationMs { get; set; }
    public bool IsSuccess { get; set; }
    public FlowExecutionResult? FlowResult { get; set; }
    public Dictionary<string, int> BranchExecutionCounts { get; set; } = new();
    public int TotalBranches { get; set; }
    public int CoveredBranches { get; set; }
    public double CoveragePercentage { get; set; }
}

/// <summary>
/// 批量仿真结果
/// </summary>
public class DryRunBatchResult
{
    public int TotalTestCases { get; set; }
    public List<DryRunResult> Results { get; set; } = new();
    public Dictionary<string, int> CombinedBranchExecutionCounts { get; set; } = new();
    public int TotalBranches { get; set; }
    public int CoveredBranches { get; set; }
    public double CoveragePercentage { get; set; }

    /// <summary>
    /// 生成覆盖率报告
    /// </summary>
    public string GenerateReport()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"=== DryRun 批量仿真报告 ===");
        sb.AppendLine($"测试用例数: {TotalTestCases}");
        sb.AppendLine($"分支覆盖率: {CoveragePercentage:F1}% ({CoveredBranches}/{TotalBranches})");
        sb.AppendLine();
        sb.AppendLine("分支执行情况:");
        foreach (var (branch, count) in CombinedBranchExecutionCounts.OrderBy(x => x.Key))
        {
            var status = count > 0 ? "✅" : "❌";
            sb.AppendLine($"  {status} {branch}: {count} 次");
        }
        return sb.ToString();
    }
}
