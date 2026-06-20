// FlowExecutionService.cs
// 流程执行服务实现
// Encoding cleanup: previous comment text was unreadable.

using System.Collections;
using System.Collections.Concurrent;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Operators;
using ClearVision.Product.Core.ProjectVariables;
using ClearVision.Product.Core.Services;
using ClearVision.Product.Core.ValueObjects;
using ClearVision.Product.Infrastructure.Logging;
using ClearVision.Product.Infrastructure.Operators;
using Microsoft.Extensions.Logging;
using OpenCvSharp;

namespace ClearVision.Product.Infrastructure.Services;

/// <summary>
/// 流程执行服务实现
/// </summary>
public class FlowExecutionService : IFlowExecutionService, IDisposable
{
    private static readonly TimeSpan DebugCleanupInterval = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan DebugSessionTtl = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan ExecutionStatusTtl = TimeSpan.FromSeconds(30);
    private const string OperatorCanceledErrorMessage = "Operator execution was canceled.";
    private const int DefaultDebugCacheMaxEntries = 256;
    private const long DefaultDebugCacheMaxBytes = 128L * 1024 * 1024;
    private const long DefaultDebugCacheMaxEntryBytes = 32L * 1024 * 1024;
    private static readonly ConditionalWeakTable<OperatorFlow, FlowExecutionPlanCache> FlowExecutionPlanCaches = new();
    private static readonly ConditionalWeakTable<Operator, ParameterFingerprintOrderCache> FingerprintParameterOrderCaches = new();
    private static readonly HashSet<OperatorType> AutoParallelBlockedOperatorTypes =
    [
        OperatorType.ImageAcquisition,
        OperatorType.ResultOutput,
        OperatorType.ModbusCommunication,
        OperatorType.ModbusRtuCommunication,
        OperatorType.TcpCommunication,
        OperatorType.SerialCommunication,
        OperatorType.SiemensS7Communication,
        OperatorType.MitsubishiMcCommunication,
        OperatorType.OmronFinsCommunication,
        OperatorType.DatabaseWrite,
        OperatorType.HttpRequest,
        OperatorType.MqttPublish,
        OperatorType.ImageSave,
        OperatorType.TextSave,
        OperatorType.VariableWrite,
        OperatorType.VariableIncrement,
        OperatorType.CycleCounter,
        OperatorType.TimerStatistics,
        OperatorType.ForEach,
        OperatorType.ScriptOperator,
        OperatorType.TriggerModule,
        OperatorType.FrameChangeTrigger,
        OperatorType.FrameAveraging,
        OperatorType.DeepLearning,
        OperatorType.OnnxInference,
        OperatorType.SemanticSegmentation,
        OperatorType.AnomalyDetection,
        OperatorType.CalibrationLoader,
        OperatorType.CameraCalibration,
        OperatorType.TranslationRotationCalibration,
        OperatorType.StereoCalibration,
        OperatorType.HandEyeCalibration
    ];
    private readonly ConcurrentDictionary<Guid, FlowExecutionStatus> _executionStatuses = new();
    private readonly Dictionary<OperatorType, IOperatorExecutor> _executors;
    private readonly ILogger<FlowExecutionService> _logger;
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _executionCancellations = new();
    private readonly IVariableContext _variableContext;
    private readonly IProjectVariableExecutionContextAccessor _projectVariableContextAccessor;

    // Encoding cleanup: previous comment text was unreadable.
    private readonly ConcurrentDictionary<(Guid DebugSessionId, Guid OperatorId), Dictionary<string, object>> _debugCache = new();
    private readonly ConcurrentDictionary<(Guid DebugSessionId, Guid OperatorId), string> _debugCacheFingerprints = new();
    private readonly ConcurrentDictionary<(Guid DebugSessionId, Guid OperatorId), long> _debugCacheEntrySizes = new();
    private readonly ConcurrentDictionary<Guid, DebugOptions> _debugOptions = new();
    private readonly ConcurrentDictionary<Guid, DateTime> _debugSessionLastAccess = new();
    private readonly object _debugCacheEvictionGate = new();
    private readonly int _debugCacheMaxEntries;
    private readonly long _debugCacheMaxBytes;
    private readonly long _debugCacheMaxEntryBytes;
    private long _debugCacheBytes;
    private readonly Timer _debugCacheCleanupTimer;
    private bool _disposed;

    private readonly record struct FlowExecutionPlanStamp(
        DateTime? FlowModifiedAt,
        int OperatorCount,
        int ConnectionCount,
        int TopologyHash);

    private sealed class FlowExecutionPlan
    {
        private FlowExecutionPlan(
            OperatorFlow flow,
            FlowExecutionPlanStamp stamp,
            FlowTopologyIndex topology,
            List<Operator> executionOrder,
            List<List<Operator>> executionLayers,
            Dictionary<(Guid OperatorId, Guid PortId), int> fanOutDegrees)
        {
            Flow = flow;
            Stamp = stamp;
            Topology = topology;
            ExecutionOrder = executionOrder;
            ExecutionLayers = executionLayers;
            FanOutDegrees = fanOutDegrees;
            ResultOutputCandidates = BuildReverseCandidates(executionOrder, op => op.Type == OperatorType.ResultOutput);
            ResultJudgmentCandidates = BuildReverseCandidates(executionOrder, op => op.Type == OperatorType.ResultJudgment);
            ReverseExecutionOrder = BuildReverseCandidates(executionOrder, _ => true);
        }

        public OperatorFlow Flow { get; }
        public FlowExecutionPlanStamp Stamp { get; }
        public FlowTopologyIndex Topology { get; }
        public List<Operator> ExecutionOrder { get; }
        public List<List<Operator>> ExecutionLayers { get; }
        public Dictionary<(Guid OperatorId, Guid PortId), int> FanOutDegrees { get; }
        public List<Operator> ResultOutputCandidates { get; }
        public List<Operator> ResultJudgmentCandidates { get; }
        public List<Operator> ReverseExecutionOrder { get; }

        public static FlowExecutionPlan Build(OperatorFlow flow, FlowExecutionPlanStamp stamp)
        {
            var topology = FlowTopologyIndex.Build(flow);
            var executionOrder = topology.BuildExecutionOrder(flow.Operators);
            var executionLayers = BuildExecutionLayers(executionOrder, topology);
            var fanOutDegrees = AnalyzeFanOutDegrees(flow.Connections);
            return new FlowExecutionPlan(flow, stamp, topology, executionOrder, executionLayers, fanOutDegrees);
        }

        public FlowInputPreparationIndex CreateInputPreparationIndex() => new(Topology);

        private static List<Operator> BuildReverseCandidates(
            IReadOnlyList<Operator> executionOrder,
            Func<Operator, bool> predicate)
        {
            var candidates = new List<Operator>();
            for (var index = executionOrder.Count - 1; index >= 0; index--)
            {
                var op = executionOrder[index];
                if (predicate(op))
                {
                    candidates.Add(op);
                }
            }

            return candidates;
        }
    }

    private sealed class FlowExecutionPlanCache
    {
        private readonly object _gate = new();
        private FlowExecutionPlan? _plan;

        public FlowExecutionPlan GetOrBuild(OperatorFlow flow, FlowExecutionPlanStamp stamp)
        {
            var cached = _plan;
            if (cached != null && cached.Stamp.Equals(stamp))
            {
                return cached;
            }

            lock (_gate)
            {
                cached = _plan;
                if (cached != null && cached.Stamp.Equals(stamp))
                {
                    return cached;
                }

                var rebuilt = FlowExecutionPlan.Build(flow, stamp);
                _plan = rebuilt;
                return rebuilt;
            }
        }
    }

    private sealed class FlowTopologyIndex
    {
        private readonly Dictionary<Guid, Operator> _operatorsById;
        private readonly Dictionary<Guid, List<OperatorConnection>> _incomingConnectionsByTargetId;
        private readonly Dictionary<Guid, List<OperatorConnection>> _outgoingConnectionsBySourceId;
        private readonly Dictionary<(Guid OperatorId, Guid PortId), Port> _outputPortsByOperatorPortId;
        private readonly Dictionary<(Guid OperatorId, Guid PortId), Port> _inputPortsByOperatorPortId;
        private readonly Dictionary<(Guid OperatorId, string PortName), Port> _outputPortsByOperatorName;

        private FlowTopologyIndex(
            Dictionary<Guid, Operator> operatorsById,
            Dictionary<Guid, List<OperatorConnection>> incomingConnectionsByTargetId,
            Dictionary<Guid, List<OperatorConnection>> outgoingConnectionsBySourceId,
            Dictionary<(Guid OperatorId, Guid PortId), Port> outputPortsByOperatorPortId,
            Dictionary<(Guid OperatorId, Guid PortId), Port> inputPortsByOperatorPortId,
            Dictionary<(Guid OperatorId, string PortName), Port> outputPortsByOperatorName)
        {
            _operatorsById = operatorsById;
            _incomingConnectionsByTargetId = incomingConnectionsByTargetId;
            _outgoingConnectionsBySourceId = outgoingConnectionsBySourceId;
            _outputPortsByOperatorPortId = outputPortsByOperatorPortId;
            _inputPortsByOperatorPortId = inputPortsByOperatorPortId;
            _outputPortsByOperatorName = outputPortsByOperatorName;
        }

        public static FlowTopologyIndex Build(OperatorFlow flow)
        {
            var operatorsById = new Dictionary<Guid, Operator>(flow.Operators.Count);
            var incomingConnectionsByTargetId = new Dictionary<Guid, List<OperatorConnection>>();
            var outgoingConnectionsBySourceId = new Dictionary<Guid, List<OperatorConnection>>();
            var outputPortsByOperatorPortId = new Dictionary<(Guid OperatorId, Guid PortId), Port>();
            var inputPortsByOperatorPortId = new Dictionary<(Guid OperatorId, Guid PortId), Port>();
            var outputPortsByOperatorName = new Dictionary<(Guid OperatorId, string PortName), Port>();

            foreach (var op in flow.Operators)
            {
                operatorsById[op.Id] = op;

                foreach (var outputPort in op.OutputPorts)
                {
                    outputPortsByOperatorPortId[(op.Id, outputPort.Id)] = outputPort;
                    outputPortsByOperatorName.TryAdd((op.Id, outputPort.Name), outputPort);
                }

                foreach (var inputPort in op.InputPorts)
                {
                    inputPortsByOperatorPortId[(op.Id, inputPort.Id)] = inputPort;
                }
            }

            foreach (var connection in flow.Connections)
            {
                if (!incomingConnectionsByTargetId.TryGetValue(connection.TargetOperatorId, out var list))
                {
                    list = new List<OperatorConnection>();
                    incomingConnectionsByTargetId[connection.TargetOperatorId] = list;
                }

                // Keep original flow connection order to preserve merge/override semantics.
                list.Add(connection);

                if (!outgoingConnectionsBySourceId.TryGetValue(connection.SourceOperatorId, out var outgoingList))
                {
                    outgoingList = new List<OperatorConnection>();
                    outgoingConnectionsBySourceId[connection.SourceOperatorId] = outgoingList;
                }

                outgoingList.Add(connection);
            }

            return new FlowTopologyIndex(
                operatorsById,
                incomingConnectionsByTargetId,
                outgoingConnectionsBySourceId,
                outputPortsByOperatorPortId,
                inputPortsByOperatorPortId,
                outputPortsByOperatorName);
        }

        public List<Operator> BuildExecutionOrder(IReadOnlyList<Operator> operators)
        {
            var visited = new HashSet<Guid>();
            var result = new List<Operator>(operators.Count);

            foreach (var op in operators)
            {
                VisitOperator(op, visited, result);
            }

            return result;
        }

        public IReadOnlyList<OperatorConnection> GetIncomingConnections(Guid targetOperatorId) =>
            _incomingConnectionsByTargetId.TryGetValue(targetOperatorId, out var list)
                ? list
                : [];

        public IReadOnlyList<OperatorConnection> GetOutgoingConnections(Guid sourceOperatorId) =>
            _outgoingConnectionsBySourceId.TryGetValue(sourceOperatorId, out var list)
                ? list
                : [];

        public Operator? GetOperator(Guid operatorId)
        {
            _operatorsById.TryGetValue(operatorId, out var op);
            return op;
        }

        public Port? GetSourcePort(Guid sourceOperatorId, Guid sourcePortId)
        {
            _outputPortsByOperatorPortId.TryGetValue((sourceOperatorId, sourcePortId), out var sourcePort);
            return sourcePort;
        }

        public Port? GetTargetPort(Guid targetOperatorId, Guid targetPortId)
        {
            _inputPortsByOperatorPortId.TryGetValue((targetOperatorId, targetPortId), out var targetPort);
            return targetPort;
        }

        public Port? GetOutputPortByName(Guid operatorId, string portName)
        {
            _outputPortsByOperatorName.TryGetValue((operatorId, portName), out var port);
            return port;
        }

        private void VisitOperator(Operator op, HashSet<Guid> visited, List<Operator> result)
        {
            if (!visited.Add(op.Id))
            {
                return;
            }

            if (_incomingConnectionsByTargetId.TryGetValue(op.Id, out var dependencies))
            {
                foreach (var connection in dependencies)
                {
                    if (_operatorsById.TryGetValue(connection.SourceOperatorId, out var dependency))
                    {
                        VisitOperator(dependency, visited, result);
                    }
                }
            }

            result.Add(op);
        }
    }

    private sealed class FlowInputPreparationIndex
    {
        private readonly FlowTopologyIndex _topology;

        public int IncomingConnectionLookupCount { get; private set; }
        public int SourceOperatorLookupCount { get; private set; }
        public int SourcePortLookupCount { get; private set; }
        public int TargetPortLookupCount { get; private set; }

        public FlowInputPreparationIndex(OperatorFlow flow)
            : this(FlowTopologyIndex.Build(flow))
        {
        }

        public FlowInputPreparationIndex(FlowTopologyIndex topology)
        {
            _topology = topology;
        }

        public IReadOnlyList<OperatorConnection> GetIncomingConnections(Guid targetOperatorId)
        {
            IncomingConnectionLookupCount++;
            return _topology.GetIncomingConnections(targetOperatorId);
        }

        public Operator? GetSourceOperator(Guid sourceOperatorId)
        {
            SourceOperatorLookupCount++;
            return _topology.GetOperator(sourceOperatorId);
        }

        public Port? GetSourcePort(Guid sourceOperatorId, Guid sourcePortId)
        {
            SourcePortLookupCount++;
            return _topology.GetSourcePort(sourceOperatorId, sourcePortId);
        }

        public Port? GetTargetPort(Guid targetOperatorId, Guid targetPortId)
        {
            TargetPortLookupCount++;
            return _topology.GetTargetPort(targetOperatorId, targetPortId);
        }
    }

    private sealed record ParameterFingerprintOrderCache(
        DateTime? ModifiedAt,
        int ParameterCount,
        Parameter[] Parameters);

    private static FlowInputPreparationIndex BuildFlowInputPreparationIndex(OperatorFlow flow)
    {
        return GetFlowExecutionPlan(flow).CreateInputPreparationIndex();
    }

    private static FlowExecutionPlan GetFlowExecutionPlan(OperatorFlow flow)
    {
        var stamp = CreateFlowExecutionPlanStamp(flow);
        return FlowExecutionPlanCaches
            .GetValue(flow, _ => new FlowExecutionPlanCache())
            .GetOrBuild(flow, stamp);
    }

    private static FlowExecutionPlanStamp CreateFlowExecutionPlanStamp(OperatorFlow flow)
    {
        var hash = new HashCode();

        foreach (var op in flow.Operators)
        {
            hash.Add(op.Id);
            hash.Add(op.Type);
            hash.Add(op.IsEnabled);
            hash.Add(op.InputPorts.Count);
            foreach (var inputPort in op.InputPorts)
            {
                hash.Add(inputPort.Id);
                hash.Add(inputPort.Name, StringComparer.Ordinal);
                hash.Add(inputPort.DataType);
            }

            hash.Add(op.OutputPorts.Count);
            foreach (var outputPort in op.OutputPorts)
            {
                hash.Add(outputPort.Id);
                hash.Add(outputPort.Name, StringComparer.Ordinal);
                hash.Add(outputPort.DataType);
            }
        }

        foreach (var connection in flow.Connections)
        {
            hash.Add(connection.SourceOperatorId);
            hash.Add(connection.SourcePortId);
            hash.Add(connection.TargetOperatorId);
            hash.Add(connection.TargetPortId);
        }

        return new FlowExecutionPlanStamp(
            flow.ModifiedAt,
            flow.Operators.Count,
            flow.Connections.Count,
            hash.ToHashCode());
    }

    private static bool IsFlowSafeForAutoParallelization(OperatorFlow flow)
    {
        foreach (var op in flow.Operators)
        {
            if (AutoParallelBlockedOperatorTypes.Contains(op.Type))
            {
                return false;
            }
        }

        return true;
    }

    public FlowExecutionService(
        IEnumerable<IOperatorExecutor> executors,
        ILogger<FlowExecutionService> logger,
        IVariableContext variableContext,
        IProjectVariableExecutionContextAccessor? projectVariableContextAccessor = null,
        long? debugCacheMaxBytes = null,
        int? debugCacheMaxEntries = null,
        long? debugCacheMaxEntryBytes = null)
    {
        _executors = executors.ToDictionary(e => e.OperatorType);
        _logger = logger;
        _variableContext = variableContext;
        _projectVariableContextAccessor = projectVariableContextAccessor ?? new ProjectVariableExecutionContextAccessor();
        _debugCacheMaxBytes = Math.Max(0, debugCacheMaxBytes ?? DefaultDebugCacheMaxBytes);
        _debugCacheMaxEntries = Math.Max(0, debugCacheMaxEntries ?? DefaultDebugCacheMaxEntries);
        _debugCacheMaxEntryBytes = Math.Min(
            Math.Max(0, debugCacheMaxEntryBytes ?? DefaultDebugCacheMaxEntryBytes),
            _debugCacheMaxBytes);
        _debugCacheCleanupTimer = new Timer(CleanupStaleDebugSessions, null, DebugCleanupInterval, DebugCleanupInterval);
    }

    public Task<FlowExecutionResult> ExecuteFlowAsync(
        OperatorFlow flow,
        Dictionary<string, object>? inputData,
        FlowExecutionMode executionMode,
        CancellationToken cancellationToken = default)
    {
        var enableParallel = executionMode == FlowExecutionMode.AutoSafeParallel &&
            IsFlowSafeForAutoParallelization(flow);

        if (executionMode == FlowExecutionMode.AutoSafeParallel && !enableParallel)
        {
            _logger.LogDebug(
                "[FlowExecution] AutoSafeParallel requested for flow {FlowId}, but the flow contains stateful or side-effect operators. Falling back to sequential execution.",
                flow.Id);
        }

        return ExecuteFlowAsync(flow, inputData, enableParallel, cancellationToken);
    }

    public async Task<FlowExecutionResult> ExecuteFlowAsync(
        OperatorFlow flow,
        Dictionary<string, object>? inputData = null,
        bool enableParallel = false,
        CancellationToken cancellationToken = default)
    {
        var projectVariableContext = _projectVariableContextAccessor.Current;
        if (projectVariableContext != null && enableParallel)
        {
            enableParallel = false;
            _logger.LogDebug(
                "[FlowExecution] Project global variables are active for flow {FlowId}; falling back to sequential execution.",
                flow.Id);
        }

        using var variableScope = _variableContext.BeginScope(new VariableContextScope(
            flow.Id,
            projectVariableContext?.RunId ?? Guid.NewGuid(),
            enableParallel ? "parallel-flow-run" : "sequential-flow-run"));

        // Encoding cleanup: previous comment text was unreadable.
        _variableContext.IncrementCycleCount();
        _logger.LogDebug("[FlowExecution] 寰幆璁℃暟: {CycleCount}", _variableContext.CycleCount);

        // Each ExecuteFlowAsync call owns its own FlowExecutionResult instance.
        var result = new FlowExecutionResult();
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        ConcurrentDictionary<Guid, Dictionary<string, object>>? operatorOutputs = null;

        // 鍒涘缓閾炬帴鐨?CancellationTokenSource
        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _executionCancellations[flow.Id] = cts;

        try
        {
            // Encoding cleanup: previous comment text was unreadable.
            var plan = GetFlowExecutionPlan(flow);

            // 获取执行顺序（拓扑排序）
            var executionOrder = CreateProjectVariableExecutionOrder(plan, _projectVariableContextAccessor.Current);
            var inputPreparationIndex = plan.CreateInputPreparationIndex();

            // 鍒濆鍖栨墽琛岀姸鎬?
            var status = new FlowExecutionStatus
            {
                FlowId = flow.Id,
                IsExecuting = true,
                StartTime = DateTime.UtcNow,
                ProgressPercentage = 0
            };
            _executionStatuses[flow.Id] = status;

            // 瀛樺偍姣忎釜绠楀瓙鐨勮緭鍑?- 浣跨敤 ConcurrentDictionary 鏀寔骞惰鎵ц
            operatorOutputs = new ConcurrentDictionary<Guid, Dictionary<string, object>>();

            // 璁剧疆鍒濆杈撳叆鏁版嵁
            if (inputData != null)
            {
                ApplyInitialInputRefCounts(inputData, executionOrder, inputPreparationIndex);
                operatorOutputs[Guid.Empty] = inputData;
            }

            if (enableParallel && executionOrder.Count > 1)
            {
                // 并行执行模式
                await ExecuteFlowParallelAsync(plan, operatorOutputs, result, status, cts.Token, inputPreparationIndex);
            }
            else
            {
                // 顺序执行模式
                await ExecuteFlowSequentialAsync(flow, plan, operatorOutputs, result, status, cts.Token, inputPreparationIndex);
            }

            stopwatch.Stop();
            result.ExecutionTimeMs = stopwatch.ElapsedMilliseconds;

            // Check whether execution was canceled.
            if (cts.Token.IsCancellationRequested)
            {
                result.IsSuccess = false;
                result.ErrorMessage = "Flow was canceled.";
            }
            else
            {
                result.IsSuccess = result.OperatorResults.All(r => r.IsSuccess);
            }

            result.WasShortCircuited = result.OperatorResults.Any(r => r.ShortCircuitedFlow);

            // 记录流程执行完成日志
            _logger.LogFlowExecution(flow.Id, executionOrder.Count, stopwatch.ElapsedMilliseconds, result.IsSuccess);

            if (result.IsSuccess)
            {
                var flowOutputOperator = ResolveFlowOutputOperator(plan, operatorOutputs);
                if (flowOutputOperator != null)
                {
                    result.OutputData = ConvertImageWrappersToBytes(operatorOutputs[flowOutputOperator.Id]);
                }
            }

            status.IsExecuting = false;
            status.ProgressPercentage = 100;
            status.CompletedAt = DateTime.UtcNow;

            return result;
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            result.IsSuccess = false;
            result.ErrorMessage = "Flow was canceled.";
            result.ExecutionTimeMs = stopwatch.ElapsedMilliseconds;
            return result;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            result.IsSuccess = false;
            result.ErrorMessage = $"流程执行异常: {ex.Message}";
            result.ExecutionTimeMs = stopwatch.ElapsedMilliseconds;
            _logger.LogError(ex, "流程执行异常: {FlowId}", flow.Id);
            return result;
        }
        finally
        {
            // 娓呯悊 CancellationTokenSource
            if (_executionCancellations.TryRemove(flow.Id, out var removedCts))
            {
                removedCts.Dispose();
            }

            if (_executionStatuses.TryGetValue(flow.Id, out var finalStatus))
            {
                finalStatus.IsExecuting = false;
                finalStatus.ProgressPercentage = 100;
                finalStatus.CompletedAt ??= DateTime.UtcNow;
            }

            if (operatorOutputs != null)
            {
                ReleaseRemainingImageWrappers(operatorOutputs);
            }
        }
    }

    public async Task<FlowExecutionResult> ExecuteFlowAsync(
        OperatorFlow flow,
        Dictionary<string, object>? inputData,
        ProjectVariableExecutionContext projectVariables,
        bool enableParallel = false,
        CancellationToken cancellationToken = default)
    {
        using var previewSession = projectVariables.IsPreview
            ? projectVariables.Session.CreateSnapshotClone()
            : null;
        var effectiveContext = previewSession == null
            ? projectVariables
            : new ProjectVariableExecutionContext(
                previewSession,
                projectVariables.BindingIndex,
                projectVariables.RunId,
                isPreview: true);

        using var scope = _projectVariableContextAccessor.BeginScope(effectiveContext);
        return await ExecuteFlowAsync(flow, inputData, enableParallel, cancellationToken);
    }

    /// <summary>
    /// 顺序执行流程
    /// </summary>
    private async Task ExecuteFlowSequentialAsync(
        OperatorFlow flow,
        FlowExecutionPlan plan,
        ConcurrentDictionary<Guid, Dictionary<string, object>> operatorOutputs,
        FlowExecutionResult result,
        FlowExecutionStatus status,
        CancellationToken cancellationToken,
        FlowInputPreparationIndex inputPreparationIndex)
    {
        var executionOrder = CreateProjectVariableExecutionOrder(plan, _projectVariableContextAccessor.Current);
        int completedCount = 0;
        foreach (var op in executionOrder)
        {
            // Check cancellation before the next operator.
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            status.CurrentOperatorId = op.Id;
            status.ProgressPercentage = (double)completedCount / executionOrder.Count * 100;

            if (!op.IsEnabled)
            {
                result.OperatorResults.Add(CreateSkippedOperatorResult(op));
                completedCount++;
                continue;
            }

            if (!TryResolveExecutor(op.Type, out var executor))
            {
                result.OperatorResults.Add(new OperatorExecutionResult
                {
                    OperatorId = op.Id,
                    OperatorName = op.Name,
                    IsSuccess = false,
                    ErrorMessage = $"未找到类型为 {op.Type} 的算子执行器"
                });
                continue;
            }

            // 鏇存柊褰撳墠鎵ц鐘舵€?
            status.CurrentOperatorId = op.Id;
            status.ProgressPercentage = (double)completedCount / executionOrder.Count * 100;

            // 鍑嗗杈撳叆鏁版嵁
            var inputs = PrepareOperatorInputs(flow, op, operatorOutputs, inputPreparationIndex);
            if (!TryApplyProjectVariableTargetBindings(op, inputs, out var targetBindingError))
            {
                result.OperatorResults.Add(new OperatorExecutionResult
                {
                    OperatorId = op.Id,
                    OperatorName = op.Name,
                    IsSuccess = false,
                    ErrorMessage = targetBindingError
                });
                result.IsSuccess = false;
                result.ErrorMessage = $"绠楀瓙 '{op.Name}' 鎵ц澶辫触: {targetBindingError}";
                break;
            }

            // 执行算子
            var opResult = await ExecuteOperatorInternalAsync(op, executor, inputs, cancellationToken);
            result.OperatorResults.Add(opResult);

            if (!opResult.IsSuccess)
            {
                result.IsSuccess = false;
                result.ErrorMessage = $"算子 '{op.Name}' 执行失败: {opResult.ErrorMessage}";
                break;
            }

            var outputs = opResult.OutputData ?? new Dictionary<string, object>();
            operatorOutputs[op.Id] = outputs;

            // Sprint 1 Task 1.1: 应用扇出引用计数
            ApplyFanOutRefCounts(op, outputs, plan.FanOutDegrees, plan.Topology);
            if (!TryCommitProjectVariableSourceBindings(op, outputs, out var sourceBindingError))
            {
                result.OperatorResults.Add(new OperatorExecutionResult
                {
                    OperatorId = op.Id,
                    OperatorName = op.Name,
                    IsSuccess = false,
                    ErrorMessage = sourceBindingError
                });
                result.IsSuccess = false;
                result.ErrorMessage = $"绠楀瓙 '{op.Name}' 鎵ц澶辫触: {sourceBindingError}";
                break;
            }

            completedCount++;

            if (opResult.ShortCircuitedFlow)
            {
                _logger.LogDebug(
                    "[FlowExecution] Operator '{OperatorName}' short-circuited this flow cycle.",
                    op.Name);
                break;
            }
        }
    }

    private static List<Operator> CreateProjectVariableExecutionOrder(
        FlowExecutionPlan plan,
        ProjectVariableExecutionContext? projectVariables)
    {
        var baseOrder = plan.ExecutionOrder;
        if (projectVariables == null || !projectVariables.BindingIndex.HasBindings)
        {
            return baseOrder.ToList();
        }

        var schema = projectVariables.Session.Schema;
        var orderByOperatorId = baseOrder
            .Select((op, index) => (op.Id, Index: index))
            .ToDictionary(item => item.Id, item => item.Index);
        var operatorsById = baseOrder.ToDictionary(op => op.Id);
        var outgoing = baseOrder.ToDictionary(op => op.Id, _ => new HashSet<Guid>());
        var indegree = baseOrder.ToDictionary(op => op.Id, _ => 0);

        foreach (var op in baseOrder)
        {
            foreach (var connection in plan.Topology.GetOutgoingConnections(op.Id))
            {
                if (operatorsById.ContainsKey(connection.TargetOperatorId) &&
                    outgoing[op.Id].Add(connection.TargetOperatorId))
                {
                    indegree[connection.TargetOperatorId]++;
                }
            }
        }

        foreach (var edge in projectVariables.BindingIndex.GetImplicitEdges(schema))
        {
            if (!operatorsById.ContainsKey(edge.SourceOperatorId) ||
                !operatorsById.ContainsKey(edge.TargetOperatorId))
            {
                continue;
            }

            if (outgoing[edge.SourceOperatorId].Add(edge.TargetOperatorId))
            {
                indegree[edge.TargetOperatorId]++;
            }
        }

        var ready = new SortedSet<Guid>(Comparer<Guid>.Create((left, right) =>
        {
            var byIndex = orderByOperatorId[left].CompareTo(orderByOperatorId[right]);
            return byIndex != 0 ? byIndex : left.CompareTo(right);
        }));

        foreach (var (operatorId, count) in indegree)
        {
            if (count == 0)
            {
                ready.Add(operatorId);
            }
        }

        var ordered = new List<Operator>(baseOrder.Count);
        while (ready.Count > 0)
        {
            var operatorId = ready.Min;
            ready.Remove(operatorId);
            ordered.Add(operatorsById[operatorId]);

            foreach (var next in outgoing[operatorId])
            {
                indegree[next]--;
                if (indegree[next] == 0)
                {
                    ready.Add(next);
                }
            }
        }

        if (ordered.Count != baseOrder.Count)
        {
            throw new InvalidOperationException("Project global variable bindings create an implicit execution cycle.");
        }

        return ordered;
    }

    private bool TryApplyProjectVariableTargetBindings(
        Operator op,
        Dictionary<string, object> inputs,
        out string? error)
    {
        error = null;
        var context = _projectVariableContextAccessor.Current;
        if (context == null)
        {
            return true;
        }

        foreach (var binding in context.BindingIndex.GetTargets(op.Id))
        {
            if (!context.Session.TryGetDefinition(binding.VariableId, out var definition))
            {
                error = $"GV008: project global variable '{binding.VariableId}' does not exist.";
                return false;
            }

            if (!context.Session.TryGetValue(binding.VariableId, out var value))
            {
                error = $"GV021: project global variable '{definition.Name}' has no current value.";
                return false;
            }

            var parameter = op.Parameters.FirstOrDefault(item => item.Id == binding.ParameterId);
            if (parameter == null)
            {
                error = $"GV011: target parameter '{binding.ParameterId}' does not exist on operator '{op.Name}'.";
                return false;
            }

            if (!ProjectVariableValueConverter.TryConvertForParameter(value, definition.ValueType, parameter.DataType, out var converted, out var convertError))
            {
                error = $"GV022: project global variable '{definition.Name}' cannot be applied to parameter '{parameter.Name}' ({parameter.DataType}): {convertError}";
                return false;
            }

            inputs[parameter.Name] = converted!;
        }

        return true;
    }

    private bool TryCommitProjectVariableSourceBindings(
        Operator op,
        IReadOnlyDictionary<string, object> outputs,
        out string? error)
    {
        error = null;
        var context = _projectVariableContextAccessor.Current;
        if (context == null)
        {
            return true;
        }

        foreach (var binding in context.BindingIndex.GetSources(op.Id))
        {
            if (!context.Session.TryGetDefinition(binding.VariableId, out var definition))
            {
                error = $"GV008: project global variable '{binding.VariableId}' does not exist.";
                return false;
            }

            var port = op.OutputPorts.FirstOrDefault(item => item.Id == binding.OutputPortId);
            if (port == null)
            {
                error = $"GV010: source output port '{binding.OutputPortId}' does not exist on operator '{op.Name}'.";
                return false;
            }

            if (!outputs.TryGetValue(port.Name, out var value) &&
                !string.IsNullOrWhiteSpace(binding.OutputPortName))
            {
                outputs.TryGetValue(binding.OutputPortName, out value);
            }

            if (value == null)
            {
                error = $"GV023: source output '{port.Name}' did not produce a value for project global variable '{definition.Name}'.";
                return false;
            }

            try
            {
                context.Session.SetValue(
                    binding.VariableId,
                    value,
                    ProjectVariableUpdatedBy.OperatorOutput,
                    context.RunId,
                    op.Id);
            }
            catch (Exception ex)
            {
                error = $"GV024: source output '{port.Name}' cannot update project global variable '{definition.Name}': {ex.Message}";
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// 骞惰鎵ц娴佺▼ - 鎸夊眰绾у苟琛屾墽琛屾棤渚濊禆鐨勭畻瀛?
    /// </summary>
    private async Task ExecuteFlowParallelAsync(
        FlowExecutionPlan plan,
        ConcurrentDictionary<Guid, Dictionary<string, object>> operatorOutputs,
        FlowExecutionResult result,
        FlowExecutionStatus status,
        CancellationToken cancellationToken,
        FlowInputPreparationIndex inputPreparationIndex)
    {
        // 构建执行层级（哪些算子可以并行执行）
        var executionOrder = plan.ExecutionOrder;
        var executionLayers = plan.ExecutionLayers;
        var completedOperators = new HashSet<Guid>();
        var failed = false;

        foreach (var layer in executionLayers)
        {
            if (failed || cancellationToken.IsCancellationRequested)
                break;

            // 鏇存柊鐘舵€?
            status.CurrentOperatorId = layer.First().Id;
            status.ProgressPercentage = (double)completedOperators.Count / executionOrder.Count * 100;

            using var layerCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            OperatorExecutionResult? primaryLayerFailure = null;

            // Encoding cleanup: previous comment text was unreadable.
            var layerTasks = layer.Select(op => ExecuteParallelLayerOperatorAsync(
                plan,
                op,
                operatorOutputs,
                inputPreparationIndex,
                cancellationToken,
                layerCts,
                failedResult => Interlocked.CompareExchange(ref primaryLayerFailure, failedResult, null) is null)).ToList();

            // Wait for the layer to drain before mutating the method-local result accumulator.
            var layerResults = await Task.WhenAll(layerTasks);
            result.OperatorResults.AddRange(layerResults);

            // Stop parallel execution when any operator failed.
            if (layerResults.Any(r => !r.IsSuccess))
            {
                failed = true;
                var failedOp = primaryLayerFailure
                    ?? layerResults
                    .FirstOrDefault(r => !r.IsSuccess && !IsCanceledOperatorResult(r))
                    ?? layerResults.First(r => !r.IsSuccess);
                result.IsSuccess = false;
                result.ErrorMessage = cancellationToken.IsCancellationRequested
                    ? "Flow was canceled."
                    : $"算子 '{failedOp.OperatorName}' 执行失败: {failedOp.ErrorMessage}";
            }
            else if (layerResults.Any(r => r.ShortCircuitedFlow))
            {
                break;
            }

            foreach (var op in layer)
            {
                completedOperators.Add(op.Id);
            }
        }
    }

    private async Task<OperatorExecutionResult> ExecuteParallelLayerOperatorAsync(
        FlowExecutionPlan plan,
        Operator op,
        ConcurrentDictionary<Guid, Dictionary<string, object>> operatorOutputs,
        FlowInputPreparationIndex inputPreparationIndex,
        CancellationToken cancellationToken,
        CancellationTokenSource layerCts,
        Func<OperatorExecutionResult, bool> signalLayerFailure)
    {
        if (layerCts.Token.IsCancellationRequested)
        {
            return CreateCanceledOperatorResult(op);
        }

        if (!op.IsEnabled)
        {
            return CreateSkippedOperatorResult(op);
        }

        if (!TryResolveExecutor(op.Type, out var executor))
        {
            var missingExecutorResult = new OperatorExecutionResult
            {
                OperatorId = op.Id,
                OperatorName = op.Name,
                IsSuccess = false,
                ErrorMessage = $"未找到类型为 {op.Type} 的算子执行器"
            };

            if (!cancellationToken.IsCancellationRequested && signalLayerFailure(missingExecutorResult))
            {
                await CancelLayerAsync(layerCts);
            }

            return missingExecutorResult;
        }

        var inputs = PrepareOperatorInputs(plan.Flow, op, operatorOutputs, inputPreparationIndex);
        var opResult = await ExecuteOperatorInternalAsync(op, executor, inputs, layerCts.Token);

        if (opResult.IsSuccess)
        {
            var outputs = opResult.OutputData ?? new Dictionary<string, object>();
            operatorOutputs[op.Id] = outputs;

            ApplyFanOutRefCounts(op, outputs, plan.FanOutDegrees, plan.Topology);

            if (opResult.ShortCircuitedFlow)
            {
                _logger.LogDebug(
                    "[FlowExecution] Operator '{OperatorName}' short-circuited the current parallel layer.",
                    op.Name);
            }

            return opResult;
        }

        if (!cancellationToken.IsCancellationRequested && signalLayerFailure(opResult))
        {
            await CancelLayerAsync(layerCts);
        }

        if (!cancellationToken.IsCancellationRequested &&
            layerCts.IsCancellationRequested &&
            string.IsNullOrWhiteSpace(opResult.ErrorMessage))
        {
            return CreateCanceledOperatorResult(op, opResult.ExecutionTimeMs);
        }

        return opResult;
    }

    /// <summary>
    /// 构建执行层级 - 将算子分组，同一层的算子可以并行执行
    /// </summary>
    private static List<List<Operator>> BuildExecutionLayers(
        IReadOnlyList<Operator> executionOrder,
        FlowTopologyIndex topology)
    {
        var layers = new List<List<Operator>>();
        var inDegreeByOperatorId = new Dictionary<Guid, int>(executionOrder.Count);
        var scheduled = new HashSet<Guid>();

        foreach (var op in executionOrder)
        {
            inDegreeByOperatorId[op.Id] = topology.GetIncomingConnections(op.Id).Count;
        }

        var currentLayer = executionOrder
            .Where(op => inDegreeByOperatorId[op.Id] == 0)
            .ToList();
        if (currentLayer.Count == 0)
        {
            currentLayer = executionOrder.ToList();
        }

        while (currentLayer.Count > 0)
        {
            layers.Add(currentLayer);

            foreach (var op in currentLayer)
            {
                scheduled.Add(op.Id);
            }

            var nextLayer = new List<Operator>();
            foreach (var op in currentLayer)
            {
                foreach (var connection in topology.GetOutgoingConnections(op.Id))
                {
                    if (!inDegreeByOperatorId.TryGetValue(connection.TargetOperatorId, out var inDegree))
                    {
                        continue;
                    }

                    inDegree--;
                    inDegreeByOperatorId[connection.TargetOperatorId] = inDegree;
                    if (inDegree == 0 &&
                        !scheduled.Contains(connection.TargetOperatorId) &&
                        topology.GetOperator(connection.TargetOperatorId) is { } nextOperator)
                    {
                        nextLayer.Add(nextOperator);
                    }
                }
            }

            if (nextLayer.Count == 0)
            {
                var remaining = executionOrder
                    .Where(op => !scheduled.Contains(op.Id))
                    .ToList();
                if (remaining.Count == 0)
                {
                    break;
                }

                nextLayer = remaining;
            }

            currentLayer = nextLayer;
        }

        return layers;
    }

    // Encoding cleanup: previous comment text was unreadable.
    private const int DefaultOperatorTimeoutMs = 30000;

    /// <summary>
    // Encoding cleanup: previous comment text was unreadable.
    /// </summary>
    private async Task<OperatorExecutionResult> ExecuteOperatorInternalAsync(
        Operator op,
        IOperatorExecutor executor,
        Dictionary<string, object> inputs,
        CancellationToken cancellationToken = default)
    {
        op.MarkExecutionStarted();
        var opStopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            // 涓虹畻瀛愭墽琛屾坊鍔犲叏灞€瓒呮椂淇濇姢
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromMilliseconds(DefaultOperatorTimeoutMs));

            // Encoding cleanup: previous comment text was unreadable.
            var opResult = await executor.ExecuteAsync(op, inputs, timeoutCts.Token);
            opStopwatch.Stop();

            if (cancellationToken.IsCancellationRequested)
            {
                return new OperatorExecutionResult
                {
                    OperatorId = op.Id,
                    OperatorName = op.Name,
                    IsSuccess = false,
                    ExecutionTimeMs = opStopwatch.ElapsedMilliseconds,
                    ErrorMessage = "Operator execution was canceled."
                };
            }

            if (opResult.IsSuccess)
            {
                op.MarkExecutionCompleted(opStopwatch.ElapsedMilliseconds);
                _logger.LogOperatorExecution(op.Id, op.Name, opStopwatch.ElapsedMilliseconds, true);

                return new OperatorExecutionResult
                {
                    OperatorId = op.Id,
                    OperatorName = op.Name,
                    IsSuccess = true,
                    ExecutionTimeMs = opStopwatch.ElapsedMilliseconds,
                    OutputData = opResult.OutputData,
                    ShortCircuitedFlow = opResult.ShouldShortCircuitFlow
                };
            }
            else
            {
                op.MarkExecutionFailed(opResult.ErrorMessage ?? "未知错误");
                _logger.LogOperatorExecution(op.Id, op.Name, opStopwatch.ElapsedMilliseconds, false);
                _logger.LogError("算子执行失败: {OperatorName} ({OperatorId}), 错误: {ErrorMessage}",
                    op.Name, op.Id, opResult.ErrorMessage);

                return new OperatorExecutionResult
                {
                    OperatorId = op.Id,
                    OperatorName = op.Name,
                    IsSuccess = false,
                    ExecutionTimeMs = opStopwatch.ElapsedMilliseconds,
                    ErrorMessage = opResult.ErrorMessage
                };
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            opStopwatch.Stop();
            op.MarkExecutionFailed($"Operator timed out ({DefaultOperatorTimeoutMs / 1000}s)");
            _logger.LogError("算子执行超时: {OperatorName} ({OperatorId})", op.Name, op.Id);

            return new OperatorExecutionResult
            {
                OperatorId = op.Id,
                OperatorName = op.Name,
                IsSuccess = false,
                ExecutionTimeMs = opStopwatch.ElapsedMilliseconds,
                ErrorMessage = $"Operator '{op.Name}' timed out ({DefaultOperatorTimeoutMs / 1000}s)"
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            opStopwatch.Stop();
            op.MarkExecutionFailed("Operator execution was canceled.");
            _logger.LogWarning("算子执行被取消: {OperatorName} ({OperatorId})", op.Name, op.Id);

            return CreateCanceledOperatorResult(op, opStopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            opStopwatch.Stop();
            op.MarkExecutionFailed(ex.Message);
            _logger.LogError(ex, "算子执行异常: {OperatorName} ({OperatorId})", op.Name, op.Id);

            return new OperatorExecutionResult
            {
                OperatorId = op.Id,
                OperatorName = op.Name,
                IsSuccess = false,
                ExecutionTimeMs = opStopwatch.ElapsedMilliseconds,
                ErrorMessage = ex.Message
            };
        }
    }

    public async Task<OperatorExecutionResult> ExecuteOperatorAsync(Operator @operator, Dictionary<string, object>? inputs = null)
    {
        if (!TryResolveExecutor(@operator.Type, out var executor))
        {
            return new OperatorExecutionResult
            {
                OperatorId = @operator.Id,
                OperatorName = @operator.Name,
                IsSuccess = false,
                ErrorMessage = $"未找到类型为 {@operator.Type} 的算子执行器"
            };
        }

        return await ExecuteOperatorInternalAsync(
            @operator,
            executor,
            inputs ?? new Dictionary<string, object>(),
            CancellationToken.None);
    }

    public FlowValidationResult ValidateFlow(OperatorFlow flow)
    {
        var result = new FlowValidationResult();

        // Validate that the flow contains operators.
        if (flow.Operators.Count == 0)
        {
            result.Errors.Add("Flow does not contain any operators.");
            return result;
        }

        var hasInputOperator = false;
        var hasOutputOperator = false;

        // 楠岃瘉姣忎釜绠楀瓙鐨勫弬鏁?
        foreach (var op in flow.Operators)
        {
            hasInputOperator |= op.Type == OperatorType.ImageAcquisition;
            hasOutputOperator |= op.Type == OperatorType.ResultOutput;

            if (TryResolveExecutor(op.Type, out var executor))
            {
                var validation = executor.ValidateParameters(op);
                if (!validation.IsValid)
                {
                    foreach (var error in validation.Errors)
                    {
                        result.Errors.Add($"算子 '{op.Name}': {error}");
                    }
                }
            }
        }

        if (!hasInputOperator)
        {
            result.Warnings.Add("流程缺少图像采集算子作为输入");
        }

        if (!hasOutputOperator)
        {
            result.Warnings.Add("流程缺少结果输出算子");
        }

        result.IsValid = result.Errors.Count == 0;
        return result;
    }

    public FlowExecutionStatus? GetExecutionStatus(Guid flowId)
    {
        return _executionStatuses.TryGetValue(flowId, out var status) ? status : null;
    }

    private bool TryResolveExecutor(OperatorType operatorType, out IOperatorExecutor executor)
    {
        if (_executors.TryGetValue(operatorType, out executor!))
        {
            return true;
        }

        var resolvedType = OperatorTypeAliasResolver.Resolve(operatorType);
        if (resolvedType != operatorType && _executors.TryGetValue(resolvedType, out executor!))
        {
            return true;
        }

        executor = null!;
        return false;
    }

    public Task CancelExecutionAsync(Guid flowId)
    {
        if (_executionCancellations.TryGetValue(flowId, out var cts))
        {
            try
            {
                cts.Cancel();
                _logger.LogInformation("Cancellation requested for flow: {FlowId}", flowId);
            }
            catch (ObjectDisposedException)
            {
                // Encoding cleanup: previous comment text was unreadable.
            }
        }

        if (_executionStatuses.TryGetValue(flowId, out var status))
        {
            status.IsExecuting = false;
            status.CompletedAt = DateTime.UtcNow;
        }

        return Task.CompletedTask;
    }

    /// <summary>
    // Encoding cleanup: previous comment text was unreadable.
    // Encoding cleanup: previous comment text was unreadable.
    /// </summary>
    private Dictionary<string, object> ConvertImageWrappersToBytes(Dictionary<string, object>? outputData)
    {
        if (outputData == null)
            return new Dictionary<string, object>();

        var result = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        foreach (var kvp in outputData)
        {
            if (TryNormalizeOutputValue(kvp.Value, out var normalized))
            {
                result[kvp.Key] = normalized!;
            }
        }
        return result;
    }

    private static Operator? ResolveFlowOutputOperator(
        FlowExecutionPlan plan,
        ConcurrentDictionary<Guid, Dictionary<string, object>> operatorOutputs)
    {
        foreach (var op in plan.ResultOutputCandidates)
        {
            if (operatorOutputs.ContainsKey(op.Id))
            {
                return op;
            }
        }

        foreach (var op in plan.ResultJudgmentCandidates)
        {
            if (operatorOutputs.ContainsKey(op.Id))
            {
                return op;
            }
        }

        foreach (var op in plan.ReverseExecutionOrder)
        {
            if (operatorOutputs.ContainsKey(op.Id))
            {
                return op;
            }
        }

        return null;
    }

    private static bool TryNormalizeOutputValue(object? value, out object? normalized, int depth = 0)
    {
        const int maxDepth = 8;
        if (depth > maxDepth)
        {
            normalized = value?.ToString();
            return normalized != null;
        }

        switch (value)
        {
            case null:
                normalized = null;
                return true;
            case ImageWrapper wrapper:
                normalized = wrapper.GetBytes();
                return true;
            case Mat mat:
                normalized = mat.ToBytes(".png");
                return true;
            case byte[] bytes:
                normalized = bytes;
                return true;
            case string or bool or char or sbyte or byte or short or ushort or int or uint or long or ulong or float or double or decimal or DateTime or DateTimeOffset or TimeSpan or Guid:
                normalized = value;
                return true;
        }

        var type = value.GetType();
        if (type.IsEnum)
        {
            normalized = value.ToString() ?? string.Empty;
            return true;
        }

        if (value is IDictionary<string, object> typedDict)
        {
            var dictResult = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (var (key, dictValue) in typedDict)
            {
                if (TryNormalizeOutputValue(dictValue, out var child, depth + 1))
                {
                    dictResult[key] = child;
                }
            }

            normalized = dictResult;
            return true;
        }

        if (value is IDictionary dictionary)
        {
            var dictResult = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (DictionaryEntry entry in dictionary)
            {
                var key = entry.Key?.ToString();
                if (string.IsNullOrWhiteSpace(key))
                    continue;

                if (TryNormalizeOutputValue(entry.Value, out var child, depth + 1))
                {
                    dictResult[key] = child;
                }
            }

            normalized = dictResult;
            return true;
        }

        if (value is IEnumerable enumerable)
        {
            var list = new List<object?>();
            foreach (var item in enumerable)
            {
                if (TryNormalizeOutputValue(item, out var child, depth + 1))
                {
                    list.Add(child);
                }
            }

            normalized = list;
            return true;
        }

        if (TrySerializeJsonValue(value, out var jsonElement))
        {
            normalized = jsonElement;
            return true;
        }

        normalized = value.ToString();
        return normalized != null;
    }

    private static bool TrySerializeJsonValue(object value, out JsonElement jsonElement)
    {
        try
        {
            jsonElement = JsonSerializer.SerializeToElement(value, value.GetType());
            return true;
        }
        catch
        {
            jsonElement = default;
            return false;
        }
    }

    private static void ReleaseRemainingImageWrappers(
        IEnumerable<KeyValuePair<Guid, Dictionary<string, object>>> operatorOutputs)
    {
        var wrappers = new HashSet<ImageWrapper>(ReferenceEqualityComparer.Instance);

        foreach (var (operatorId, outputData) in operatorOutputs)
        {
            foreach (var value in outputData.Values)
            {
                CollectImageWrappers(value, wrappers);
            }
        }

        foreach (var wrapper in wrappers)
        {
            while (wrapper.RefCount > 0)
            {
                try
                {
                    wrapper.Release();
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (InvalidOperationException)
                {
                    break;
                }
            }
        }
    }

    private static void CollectImageWrappers(object? value, HashSet<ImageWrapper> wrappers, int depth = 0)
    {
        const int maxDepth = 8;
        if (value == null || depth > maxDepth)
        {
            return;
        }

        if (value is ImageWrapper wrapper)
        {
            wrappers.Add(wrapper);
            return;
        }

        if (value is string or byte[] or Mat)
        {
            return;
        }

        if (value is IDictionary<string, object> typedDict)
        {
            foreach (var child in typedDict.Values)
            {
                CollectImageWrappers(child, wrappers, depth + 1);
            }
            return;
        }

        if (value is IDictionary dictionary)
        {
            foreach (DictionaryEntry entry in dictionary)
            {
                CollectImageWrappers(entry.Value, wrappers, depth + 1);
            }
            return;
        }

        if (value is IEnumerable enumerable)
        {
            foreach (var item in enumerable)
            {
                CollectImageWrappers(item, wrappers, depth + 1);
            }
        }
    }

    #region Sprint 1 Task 1.1: 扇出预分析与引用计数管理

    /// <summary>
    // Encoding cleanup: previous comment text was unreadable.
    /// 鐢ㄤ簬鍐冲畾 ImageWrapper 鐨勫紩鐢ㄨ鏁板垵濮嬪€笺€?
    /// </summary>
    private static Dictionary<(Guid OperatorId, Guid PortId), int> AnalyzeFanOutDegrees(
        IReadOnlyCollection<OperatorConnection> connections)
    {
        var targetsBySourcePort = new Dictionary<(Guid OperatorId, Guid PortId), HashSet<Guid>>();
        foreach (var conn in connections)
        {
            var key = (conn.SourceOperatorId, conn.SourcePortId);
            if (!targetsBySourcePort.TryGetValue(key, out var targets))
            {
                targets = new HashSet<Guid>();
                targetsBySourcePort[key] = targets;
            }

            // One downstream operator executes once even if multiple input ports are wired
            // to the same source output port, so fan-out should be counted by unique consumers.
            targets.Add(conn.TargetOperatorId);
        }

        return targetsBySourcePort.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Count);
    }

    /// <summary>
    // Encoding cleanup: previous comment text was unreadable.
    // Encoding cleanup: previous comment text was unreadable.
    /// </summary>
    private void ApplyFanOutRefCounts(
        Operator op,
        Dictionary<string, object> outputs,
        IReadOnlyDictionary<(Guid OperatorId, Guid PortId), int> fanOutDegrees,
        FlowTopologyIndex topology)
    {
        foreach (var (portName, value) in outputs)
        {
            if (value is not ImageWrapper img)
                continue;

            // 尝试通过名称查找端口 ID，以匹配扇出度分析使用的 Key
            var port = topology.GetOutputPortByName(op.Id, portName);
            if (port == null && op.OutputPorts.Count == 1 && IsStandardImageOutputKey(portName))
            {
                port = op.OutputPorts[0];
            }

            int fanOut = port != null
                ? fanOutDegrees.GetValueOrDefault((op.Id, port.Id), 1)
                : 1;

            // Encoding cleanup: previous comment text was unreadable.
            for (int i = 1; i < fanOut; i++)
            {
                img.AddRef();
            }

            _logger.LogDebug("[FlowExecution] Set ref count: Operator={OperatorName}, Port={PortName}, FanOut={FanOut}, RefCount={RefCount}",
                op.Name, portName, fanOut, img.RefCount);
        }
    }

    private void ApplyInitialInputRefCounts(
        Dictionary<string, object> inputData,
        IReadOnlyCollection<Operator> executionOrder,
        FlowInputPreparationIndex inputPreparationIndex)
    {
        var consumerCount = CountInitialInputConsumers(executionOrder, inputPreparationIndex);
        if (consumerCount <= 1)
        {
            return;
        }

        var wrappers = new HashSet<ImageWrapper>(ReferenceEqualityComparer.Instance);
        foreach (var value in inputData.Values)
        {
            if (value is ImageWrapper wrapper)
            {
                wrappers.Add(wrapper);
            }
        }

        foreach (var wrapper in wrappers)
        {
            for (var i = 1; i < consumerCount; i++)
            {
                wrapper.AddRef();
            }
        }
    }

    private int CountInitialInputConsumers(
        IReadOnlyCollection<Operator> executionOrder,
        FlowInputPreparationIndex inputPreparationIndex)
    {
        var count = 0;
        foreach (var op in executionOrder)
        {
            if (!op.IsEnabled)
            {
                continue;
            }

            if (inputPreparationIndex.GetIncomingConnections(op.Id).Count != 0)
            {
                continue;
            }

            if (TryResolveExecutor(op.Type, out var executor) && executor is OperatorBase)
            {
                count++;
            }
        }

        return count;
    }

    #endregion

    private Dictionary<string, object> PrepareOperatorInputs(
        OperatorFlow flow,
        Operator op,
        IDictionary<Guid, Dictionary<string, object>> operatorOutputs,
        FlowInputPreparationIndex? inputPreparationIndex = null)
    {
        var inputs = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        inputPreparationIndex ??= BuildFlowInputPreparationIndex(flow);

        // Encoding cleanup: previous comment text was unreadable.
        // Encoding cleanup: previous comment text was unreadable.
        foreach (var param in op.Parameters)
        {
            if (param.Value != null)
            {
                inputs[param.Name] = param.Value;
            }
        }

        // Encoding cleanup: previous comment text was unreadable.
        var incomingConnections = inputPreparationIndex.GetIncomingConnections(op.Id);

        // 如果没有输入连接，尝试从初始输入数据获取 (Guid.Empty)
        if (incomingConnections.Count == 0)
        {
            if (operatorOutputs.TryGetValue(Guid.Empty, out var initialInputs))
            {
                foreach (var kvp in initialInputs)
                {
                    // Use case-insensitive key matching to avoid "image" vs "Image" mismatches.
                    if (!inputs.ContainsKey(kvp.Key))
                    {
                        inputs[kvp.Key] = kvp.Value;
                    }
                }
            }
        }
        else
        {
            foreach (var connection in incomingConnections)
            {
                if (operatorOutputs.TryGetValue(connection.SourceOperatorId, out var sourceOutputs))
                {
                    // Encoding cleanup: previous comment text was unreadable.
                    var sourceOperator = inputPreparationIndex.GetSourceOperator(connection.SourceOperatorId);

                    if (sourceOperator?.Type == OperatorType.ConditionalBranch)
                    {
                        // Encoding cleanup: previous comment text was unreadable.
                        // Encoding cleanup: previous comment text was unreadable.
                        var sourcePort = inputPreparationIndex.GetSourcePort(connection.SourceOperatorId, connection.SourcePortId);
                        if (sourcePort != null)
                        {
                            var portName = sourcePort.Name;
                            var targetPort = inputPreparationIndex.GetTargetPort(op.Id, connection.TargetPortId);
                            // Forward source output data only when the source port produced a non-null value.
                            if (sourceOutputs.TryGetValue(portName, out var portData) && portData != null)
                            {
                                if (targetPort != null)
                                {
                                    inputs[targetPort.Name] = portData;
                                }

                                if (!inputs.ContainsKey(portName))
                                {
                                    inputs[portName] = portData;
                                }
                                // Encoding cleanup: previous comment text was unreadable.
                                if (sourceOutputs.TryGetValue("Result", out var result))
                                    inputs["ConditionResult"] = result;
                                if (sourceOutputs.TryGetValue("Condition", out var condition))
                                    inputs["Condition"] = condition;
                                if (sourceOutputs.TryGetValue("ActualValue", out var actualValue))
                                    inputs["ActualValue"] = actualValue;
                            }
                            // Encoding cleanup: previous comment text was unreadable.
                        }
                    }
                    else
                    {
                        // Encoding cleanup: previous comment text was unreadable.

                        // Encoding cleanup: previous comment text was unreadable.
                        // Encoding cleanup: previous comment text was unreadable.
                        Port? sourcePort = null;
                        Port? targetPort = null;
                        if (sourceOperator != null)
                        {
                            sourcePort = inputPreparationIndex.GetSourcePort(connection.SourceOperatorId, connection.SourcePortId);
                            targetPort = inputPreparationIndex.GetTargetPort(op.Id, connection.TargetPortId);

                            // 【Bug 4 修复】基于端口名称的精确映射
                            if (sourcePort != null && targetPort != null)
                            {
                                // Encoding cleanup: previous comment text was unreadable.
                                if (sourceOutputs.TryGetValue(sourcePort.Name, out var data))
                                {
                                    // Encoding cleanup: previous comment text was unreadable.
                                    // 例如：源输出 "Image" -> 目标输入 "Background"
                                    inputs[targetPort.Name] = data;
                                }
                            }
                        }

                        // Encoding cleanup: previous comment text was unreadable.
                        // Encoding cleanup: previous comment text was unreadable.
                        // Encoding cleanup: previous comment text was unreadable.
                        // 我们依然执行全量合并，但跳过已存在的键（避免覆盖精确映射的结果）
                        foreach (var kvp in sourceOutputs)
                        {
                            if (!inputs.ContainsKey(kvp.Key))
                            {
                                // Never implicitly propagate reference-counted image payloads across
                                // unrelated ports. This avoids hidden ImageWrapper consumers that are
                                // invisible to fan-out analysis and can cause premature disposal.
                                if (ShouldSkipImplicitFallbackValue(kvp.Key, kvp.Value, sourceOperator, sourcePort))
                                {
                                    _logger.LogDebug(
                                        "[FlowExecution] Skip implicit fallback key '{Key}' from {SourceOperator} to {TargetOperator} to avoid hidden ImageWrapper propagation.",
                                        kvp.Key,
                                        sourceOperator?.Name ?? connection.SourceOperatorId.ToString(),
                                        op.Name);
                                    continue;
                                }

                                inputs[kvp.Key] = kvp.Value;
                            }
                        }
                    }
                }
            }
        }

        return inputs;
    }

    private static bool ShouldSkipImplicitFallbackValue(
        string key,
        object? value,
        Operator? sourceOperator,
        Port? connectedSourcePort)
    {
        if (!ContainsImageWrapperReference(value))
            return false;

        if (sourceOperator == null)
            return true;

        if (connectedSourcePort == null)
            return true;

        // Exact match: port name == key name (original logic)
        if (string.Equals(connectedSourcePort.Name, key, StringComparison.OrdinalIgnoreCase))
            return false;

        // Supplemental match: if this operator has only one output port and the key is a
        // well-known image output key produced by OperatorBase.CreateImageOutput, treat it
        // as the port's actual output rather than an implicit propagation.
        // This handles the common mismatch where port is named "Output" but the data key is "Image".
        if (sourceOperator.OutputPorts.Count == 1 && IsStandardImageOutputKey(key))
            return false;

        return true;
    }

    /// <summary>
    /// Returns true for well-known output keys produced by OperatorBase.CreateImageOutput
    /// and other standard operator output methods.
    /// </summary>
    private static bool IsStandardImageOutputKey(string key)
    {
        return string.Equals(key, "Image", StringComparison.OrdinalIgnoreCase)
            || string.Equals(key, "Edges", StringComparison.OrdinalIgnoreCase)
            || string.Equals(key, "Mask", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsImageWrapperReference(object? value, int depth = 0)
    {
        const int maxDepth = 6;
        if (value == null || depth > maxDepth)
            return false;

        if (value is ImageWrapper)
            return true;

        if (value is string or byte[] or Mat)
            return false;

        if (value is IDictionary<string, object> typedDict)
        {
            foreach (var child in typedDict.Values)
            {
                if (ContainsImageWrapperReference(child, depth + 1))
                    return true;
            }

            return false;
        }

        if (value is IDictionary dict)
        {
            foreach (DictionaryEntry entry in dict)
            {
                if (ContainsImageWrapperReference(entry.Value, depth + 1))
                    return true;
            }
            return false;
        }

        if (value is IEnumerable enumerable)
        {
            foreach (var item in enumerable)
            {
                if (ContainsImageWrapperReference(item, depth + 1))
                    return true;
            }
        }

        return false;
    }

    #region 调试功能实现

    /// <summary>
    // Encoding cleanup: previous comment text was unreadable.
    /// </summary>
    public async Task<FlowDebugExecutionResult> ExecuteFlowDebugAsync(
        OperatorFlow flow,
        DebugOptions options,
        Dictionary<string, object>? inputData,
        ProjectVariableExecutionContext projectVariables,
        CancellationToken cancellationToken = default)
    {
        using var previewSession = projectVariables.IsPreview
            ? projectVariables.Session.CreateSnapshotClone()
            : null;
        var effectiveContext = previewSession == null
            ? projectVariables
            : new ProjectVariableExecutionContext(
                previewSession,
                projectVariables.BindingIndex,
                projectVariables.RunId,
                isPreview: true);

        using var scope = _projectVariableContextAccessor.BeginScope(effectiveContext);
        return await ExecuteFlowDebugAsync(flow, options, inputData, cancellationToken);
    }

    public async Task<FlowDebugExecutionResult> ExecuteFlowDebugAsync(
        OperatorFlow flow,
        DebugOptions options,
        Dictionary<string, object>? inputData = null,
        CancellationToken cancellationToken = default)
    {
        var result = new FlowDebugExecutionResult
        {
            DebugSessionId = options.DebugSessionId
        };

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        ConcurrentDictionary<Guid, Dictionary<string, object>>? operatorOutputs = null;

        // 保存调试选项
        _debugOptions[options.DebugSessionId] = options;
        TouchDebugSession(options.DebugSessionId);

        // 鍒涘缓閾炬帴鐨?CancellationTokenSource
        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _executionCancellations[flow.Id] = cts;

        try
        {
            // 获取执行顺序（拓扑排序）
            var plan = GetFlowExecutionPlan(flow);
            var executionOrder = CreateProjectVariableExecutionOrder(plan, _projectVariableContextAccessor.Current);
            var inputPreparationIndex = plan.CreateInputPreparationIndex();

            // 鍒濆鍖栨墽琛岀姸鎬?
            var status = new FlowExecutionStatus
            {
                FlowId = flow.Id,
                IsExecuting = true,
                StartTime = DateTime.UtcNow,
                ProgressPercentage = 0
            };
            _executionStatuses[flow.Id] = status;

            // 瀛樺偍姣忎釜绠楀瓙鐨勮緭鍑?
            operatorOutputs = new ConcurrentDictionary<Guid, Dictionary<string, object>>();

            // 璁剧疆鍒濆杈撳叆鏁版嵁
            if (inputData != null)
            {
                ApplyInitialInputRefCounts(inputData, executionOrder, inputPreparationIndex);
                operatorOutputs[Guid.Empty] = inputData;
            }

            // Encoding cleanup: previous comment text was unreadable.
            int completedCount = 0;
            Guid? pausedOperatorId = null;

            foreach (var op in executionOrder)
            {
                // Check cancellation before the next operator.
                if (cts.Token.IsCancellationRequested)
                {
                    break;
                }

                // Check whether this operator is a breakpoint.
                if (options.Breakpoints.Contains(op.Id))
                {
                    pausedOperatorId = op.Id;
                    result.BreakpointHit = true;
                    result.PausedOperatorId = pausedOperatorId;
                    _logger.LogInformation("[调试] 命中断点: {OperatorName} ({OperatorId})", op.Name, op.Id);

                    if (options.StepMode)
                    {
                        // Encoding cleanup: previous comment text was unreadable.
                        break;
                    }
                }

                status.CurrentOperatorId = op.Id;
                status.ProgressPercentage = (double)completedCount / executionOrder.Count * 100;

                if (!op.IsEnabled)
                {
                    var skippedDebugResult = CreateSkippedDebugOperatorResult(op, completedCount, options.Breakpoints.Contains(op.Id));
                    result.DebugOperatorResults.Add(skippedDebugResult);
                    result.OperatorResults.Add(skippedDebugResult);
                    result.IntermediateResults[op.Id] = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                    completedCount++;

                    if (options.BreakAtOperatorId.HasValue && op.Id == options.BreakAtOperatorId.Value)
                    {
                        pausedOperatorId = op.Id;
                        result.PausedOperatorId = pausedOperatorId;
                        break;
                    }

                    continue;
                }

                if (!TryResolveExecutor(op.Type, out var executor))
                {
                    var debugResult = new OperatorDebugResult
                    {
                        OperatorId = op.Id,
                        OperatorName = op.Name,
                        IsSuccess = false,
                        ErrorMessage = $"未找到类型为 {op.Type} 的算子执行器",
                        ExecutionOrder = completedCount,
                        IsBreakpoint = options.Breakpoints.Contains(op.Id)
                    };
                    result.DebugOperatorResults.Add(debugResult);
                    result.OperatorResults.Add(debugResult);
                    continue;
                }

                // 鏇存柊褰撳墠鎵ц鐘舵€?
                status.CurrentOperatorId = op.Id;
                status.ProgressPercentage = (double)completedCount / executionOrder.Count * 100;

                // 鍑嗗杈撳叆鏁版嵁
                var inputs = PrepareOperatorInputs(flow, op, operatorOutputs, inputPreparationIndex);
                if (!TryApplyProjectVariableTargetBindings(op, inputs, out var targetBindingError))
                {
                    var debugResult = new OperatorDebugResult
                    {
                        OperatorId = op.Id,
                        OperatorName = op.Name,
                        IsSuccess = false,
                        ErrorMessage = targetBindingError,
                        ExecutionOrder = completedCount,
                        IsBreakpoint = options.Breakpoints.Contains(op.Id),
                        InputSnapshot = CloneNormalizedDictionary(ConvertImageWrappersToBytes(inputs))
                    };
                    result.DebugOperatorResults.Add(debugResult);
                    result.OperatorResults.Add(debugResult);
                    result.IsSuccess = false;
                    result.ErrorMessage = $"Operator '{op.Name}' project variable target binding failed: {targetBindingError}";
                    break;
                }

                var normalizedInputSnapshot = ConvertImageWrappersToBytes(inputs);
                var cacheKey = (options.DebugSessionId, op.Id);
                string? cacheFingerprint = null;
                if (options.EnableIntermediateCache)
                {
                    cacheFingerprint = CreateDebugCacheFingerprint(op, normalizedInputSnapshot);
                }

                if (options.EnableIntermediateCache &&
                    _debugCache.TryGetValue(cacheKey, out var cachedOutputs) &&
                    _debugCacheFingerprints.TryGetValue(cacheKey, out var cachedFingerprint) &&
                    string.Equals(cachedFingerprint, cacheFingerprint, StringComparison.Ordinal))
                {
                    var cachedCopy = CloneNormalizedDictionary(cachedOutputs);
                    operatorOutputs[op.Id] = cachedCopy;

                    var cachedDebugResult = new OperatorDebugResult
                    {
                        OperatorId = op.Id,
                        OperatorName = op.Name,
                        IsSuccess = true,
                        ExecutionTimeMs = 0,
                        ExecutionOrder = completedCount,
                        StartTime = DateTime.UtcNow,
                        EndTime = DateTime.UtcNow,
                        IsBreakpoint = options.Breakpoints.Contains(op.Id),
                        InputSnapshot = CloneNormalizedDictionary(normalizedInputSnapshot),
                        OutputData = CloneNormalizedDictionary(cachedCopy),
                        OutputSnapshot = CloneNormalizedDictionary(cachedCopy)
                    };

                    result.DebugOperatorResults.Add(cachedDebugResult);
                    result.OperatorResults.Add(cachedDebugResult);
                    result.IntermediateResults[op.Id] = CloneNormalizedDictionary(cachedCopy);
                    TouchDebugSession(options.DebugSessionId);
                    if (!TryCommitProjectVariableSourceBindings(op, cachedCopy, out var cachedSourceBindingError))
                    {
                        cachedDebugResult.IsSuccess = false;
                        cachedDebugResult.ErrorMessage = cachedSourceBindingError;
                        result.IsSuccess = false;
                        result.ErrorMessage = $"Operator '{op.Name}' project variable source binding failed: {cachedSourceBindingError}";
                        break;
                    }

                    completedCount++;

                    if (options.BreakAtOperatorId.HasValue && op.Id == options.BreakAtOperatorId.Value)
                    {
                        pausedOperatorId = op.Id;
                        result.PausedOperatorId = pausedOperatorId;
                        _logger.LogInformation("[Debug] Reused cached output and paused at breakpoint operator {OperatorName} ({OperatorId})", op.Name, op.Id);
                        break;
                    }

                    continue;
                }

                // 执行算子
                var opResult = await ExecuteOperatorInternalAsync(op, executor, inputs, cts.Token);
                var normalizedOutputData = ConvertImageWrappersToBytes(opResult.OutputData);

                // 创建调试结果
                var debugOpResult = new OperatorDebugResult
                {
                    OperatorId = op.Id,
                    OperatorName = op.Name,
                    IsSuccess = opResult.IsSuccess,
                    ExecutionTimeMs = opResult.ExecutionTimeMs,
                    ErrorMessage = opResult.ErrorMessage,
                    ShortCircuitedFlow = opResult.ShortCircuitedFlow,
                    OutputData = CloneNormalizedDictionary(normalizedOutputData),
                    ExecutionOrder = completedCount,
                    StartTime = DateTime.UtcNow.AddMilliseconds(-opResult.ExecutionTimeMs),
                    EndTime = DateTime.UtcNow,
                    IsBreakpoint = options.Breakpoints.Contains(op.Id),
                    InputSnapshot = CloneNormalizedDictionary(normalizedInputSnapshot),
                    OutputSnapshot = CloneNormalizedDictionary(normalizedOutputData)
                };

                result.DebugOperatorResults.Add(debugOpResult);
                result.OperatorResults.Add(debugOpResult);

                if (!opResult.IsSuccess)
                {
                    result.IsSuccess = false;
                    result.ErrorMessage = $"算子 '{op.Name}' 执行失败: {opResult.ErrorMessage}";
                    break;
                }

                // 淇濆瓨杈撳嚭
                var outputs = opResult.OutputData ?? new Dictionary<string, object>();
                operatorOutputs[op.Id] = outputs;
                ApplyFanOutRefCounts(op, outputs, plan.FanOutDegrees, plan.Topology);
                if (!TryCommitProjectVariableSourceBindings(op, outputs, out var sourceBindingError))
                {
                    debugOpResult.IsSuccess = false;
                    debugOpResult.ErrorMessage = sourceBindingError;
                    result.IsSuccess = false;
                    result.ErrorMessage = $"Operator '{op.Name}' project variable source binding failed: {sourceBindingError}";
                    break;
                }

                // Encoding cleanup: previous comment text was unreadable.
                if (options.EnableIntermediateCache && normalizedOutputData.Count > 0)
                {
                    SetDebugCacheEntry(cacheKey, normalizedOutputData, cacheFingerprint!);
                    result.IntermediateResults[op.Id] = CloneNormalizedDictionary(normalizedOutputData);
                    TouchDebugSession(options.DebugSessionId);
                }

                TouchDebugSession(options.DebugSessionId);
                completedCount++;

                if (opResult.ShortCircuitedFlow)
                {
                    break;
                }

                // 【Phase 3】检查是否到达指定的断点算子
                if (options.BreakAtOperatorId.HasValue && op.Id == options.BreakAtOperatorId.Value)
                {
                    pausedOperatorId = op.Id;
                    result.PausedOperatorId = pausedOperatorId;
                    _logger.LogInformation("[Debug] Reached breakpoint operator: {OperatorName} ({OperatorId}), stopping execution.", op.Name, op.Id);
                    break;
                }
            }

            stopwatch.Stop();
            result.ExecutionTimeMs = stopwatch.ElapsedMilliseconds;

            // Check whether execution was canceled.
            if (cts.Token.IsCancellationRequested)
            {
                result.IsSuccess = false;
                result.ErrorMessage = "Flow was canceled.";
            }
            else
            {
                result.IsSuccess = result.OperatorResults.All(r => r.IsSuccess);
            }

            result.WasShortCircuited = result.OperatorResults.Any(r => r.ShortCircuitedFlow);

            if (result.IsSuccess)
            {
                var flowOutputOperator = ResolveFlowOutputOperator(plan, operatorOutputs);
                if (flowOutputOperator != null)
                {
                    result.OutputData = ConvertImageWrappersToBytes(operatorOutputs[flowOutputOperator.Id]);
                }
            }

            status.IsExecuting = false;
            status.ProgressPercentage = 100;
            status.CompletedAt = DateTime.UtcNow;

            return result;
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            result.IsSuccess = false;
            result.ErrorMessage = "Flow was canceled.";
            result.ExecutionTimeMs = stopwatch.ElapsedMilliseconds;
            return result;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            result.IsSuccess = false;
            result.ErrorMessage = $"流程执行异常: {ex.Message}";
            result.ExecutionTimeMs = stopwatch.ElapsedMilliseconds;
            _logger.LogError(ex, "调试流程执行异常: {FlowId}", flow.Id);
            return result;
        }
        finally
        {
            if (_executionCancellations.TryRemove(flow.Id, out var removedCts))
            {
                removedCts.Dispose();
            }

            if (_executionStatuses.TryGetValue(flow.Id, out var finalStatus))
            {
                finalStatus.IsExecuting = false;
                finalStatus.ProgressPercentage = 100;
                finalStatus.CompletedAt ??= DateTime.UtcNow;
            }

            if (operatorOutputs != null)
            {
                ReleaseRemainingImageWrappers(operatorOutputs);
            }
        }
    }

    /// <summary>
    /// 获取调试中间结果
    /// </summary>
    public Dictionary<string, object>? GetDebugIntermediateResult(Guid debugSessionId, Guid operatorId)
    {
        if (_debugCache.TryGetValue((debugSessionId, operatorId), out var result))
        {
            TouchDebugSession(debugSessionId);
            return CloneNormalizedDictionary(result);
        }
        return null;
    }

    /// <summary>
    /// 清除调试缓存
    /// </summary>
    public Task ClearDebugCacheAsync(Guid debugSessionId)
    {
        // Encoding cleanup: previous comment text was unreadable.
        lock (_debugCacheEvictionGate)
        {
            var keysToRemove = _debugCache.Keys.Where(k => k.DebugSessionId == debugSessionId).ToList();
            foreach (var key in keysToRemove)
            {
                RemoveDebugCacheEntryUnderLock(key);
            }
        }

        _debugOptions.TryRemove(debugSessionId, out _);
        _debugSessionLastAccess.TryRemove(debugSessionId, out _);

        _logger.LogInformation("[Debug] Cleared debug cache: {DebugSessionId}", debugSessionId);
        return Task.CompletedTask;
    }

    private void SetDebugCacheEntry(
        (Guid DebugSessionId, Guid OperatorId) cacheKey,
        Dictionary<string, object> normalizedOutputData,
        string fingerprint)
    {
        if (_debugCacheMaxEntries <= 0 || _debugCacheMaxBytes <= 0 || _debugCacheMaxEntryBytes <= 0)
        {
            lock (_debugCacheEvictionGate)
            {
                RemoveDebugCacheEntryUnderLock(cacheKey);
            }

            return;
        }

        var entryBytes = EstimateDebugCacheSize(normalizedOutputData);
        if (entryBytes > _debugCacheMaxEntryBytes)
        {
            lock (_debugCacheEvictionGate)
            {
                RemoveDebugCacheEntryUnderLock(cacheKey);
            }

            _logger.LogDebug(
                "[Debug] Skipped intermediate cache entry {DebugSessionId}/{OperatorId}: {EntryBytes} bytes exceeds per-entry limit {MaxEntryBytes} bytes.",
                cacheKey.DebugSessionId,
                cacheKey.OperatorId,
                entryBytes,
                _debugCacheMaxEntryBytes);
            return;
        }

        lock (_debugCacheEvictionGate)
        {
            if (_debugCacheEntrySizes.TryGetValue(cacheKey, out var previousBytes))
            {
                _debugCacheBytes -= previousBytes;
            }

            _debugCache[cacheKey] = normalizedOutputData;
            _debugCacheFingerprints[cacheKey] = fingerprint;
            _debugCacheEntrySizes[cacheKey] = entryBytes;
            _debugCacheBytes += entryBytes;

            TrimDebugCacheUnderLock(cacheKey);
        }
    }

    private void TrimDebugCacheUnderLock((Guid DebugSessionId, Guid OperatorId) protectedKey)
    {
        if (_debugCacheEntrySizes.Count <= _debugCacheMaxEntries && _debugCacheBytes <= _debugCacheMaxBytes)
        {
            return;
        }

        var candidates = _debugCacheEntrySizes.Keys
            .Where(key => !key.Equals(protectedKey))
            .OrderBy(key => _debugSessionLastAccess.TryGetValue(key.DebugSessionId, out var lastAccess)
                ? lastAccess
                : DateTime.MinValue)
            .ThenBy(key => key.DebugSessionId)
            .ThenBy(key => key.OperatorId)
            .ToList();

        foreach (var key in candidates)
        {
            if (_debugCacheEntrySizes.Count <= _debugCacheMaxEntries && _debugCacheBytes <= _debugCacheMaxBytes)
            {
                break;
            }

            RemoveDebugCacheEntryUnderLock(key);
        }
    }

    private void RemoveDebugCacheEntryUnderLock((Guid DebugSessionId, Guid OperatorId) cacheKey)
    {
        _debugCache.TryRemove(cacheKey, out _);
        _debugCacheFingerprints.TryRemove(cacheKey, out _);
        if (_debugCacheEntrySizes.TryRemove(cacheKey, out var entryBytes))
        {
            _debugCacheBytes = Math.Max(0, _debugCacheBytes - entryBytes);
        }
    }

    private static long EstimateDebugCacheSize(object? value, int depth = 0)
    {
        const int maxDepth = 8;
        if (value == null)
        {
            return 0;
        }

        if (depth > maxDepth)
        {
            return 64;
        }

        return value switch
        {
            byte[] bytes => bytes.LongLength,
            string text => (long)text.Length * sizeof(char),
            Dictionary<string, object> dictionary => EstimateStringObjectDictionarySize(dictionary, depth),
            IDictionary<string, object> dictionary => EstimateStringObjectDictionarySize(dictionary, depth),
            IDictionary dictionary => EstimateUntypedDictionarySize(dictionary, depth),
            IEnumerable enumerable when value is not string => EstimateEnumerableSize(enumerable, depth),
            _ => 64
        };
    }

    private static long EstimateStringObjectDictionarySize(IDictionary<string, object> dictionary, int depth)
    {
        var total = 0L;
        foreach (var (key, child) in dictionary)
        {
            total = AddClamped(total, (long)key.Length * sizeof(char));
            total = AddClamped(total, EstimateDebugCacheSize(child, depth + 1));
        }

        return total;
    }

    private static long EstimateUntypedDictionarySize(IDictionary dictionary, int depth)
    {
        var total = 0L;
        foreach (DictionaryEntry entry in dictionary)
        {
            total = AddClamped(total, EstimateDebugCacheSize(entry.Key, depth + 1));
            total = AddClamped(total, EstimateDebugCacheSize(entry.Value, depth + 1));
        }

        return total;
    }

    private static long EstimateEnumerableSize(IEnumerable enumerable, int depth)
    {
        var total = 0L;
        foreach (var child in enumerable)
        {
            total = AddClamped(total, EstimateDebugCacheSize(child, depth + 1));
        }

        return total;
    }

    private static long AddClamped(long current, long addition)
    {
        return addition > long.MaxValue - current ? long.MaxValue : current + addition;
    }

    private static Dictionary<string, object> CloneNormalizedDictionary(Dictionary<string, object> source)
    {
        var clone = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in source)
        {
            clone[key] = CloneNormalizedValue(value)!;
        }

        return clone;
    }

    private static object? CloneNormalizedValue(object? value)
    {
        return value switch
        {
            null => null,
            byte[] bytes => bytes.ToArray(),
            Dictionary<string, object> dictionary => CloneNormalizedDictionary(dictionary),
            IDictionary<string, object> typedDictionary => CloneNormalizedDictionary(
                typedDictionary.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase)),
            IEnumerable enumerable when value is not string => enumerable.Cast<object?>().Select(CloneNormalizedValue).ToList(),
            _ => value
        };
    }

    private static string CreateDebugCacheFingerprint(Operator op, Dictionary<string, object> inputs)
    {
        using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        AppendFingerprintString(hasher, op.Id.ToString("D"));
        AppendFingerprintString(hasher, op.Type.ToString());

        foreach (var parameter in GetFingerprintParameters(op))
        {
            AppendFingerprintString(hasher, parameter.Name);
            AppendFingerprintValue(hasher, parameter.GetValue());
        }

        foreach (var (key, value) in inputs.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            AppendFingerprintString(hasher, key);
            AppendFingerprintValue(hasher, value);
        }

        return Convert.ToHexString(hasher.GetHashAndReset());
    }

    private static IReadOnlyList<Parameter> GetFingerprintParameters(Operator op)
    {
        if (FingerprintParameterOrderCaches.TryGetValue(op, out var cache) &&
            cache.ModifiedAt == op.ModifiedAt &&
            cache.ParameterCount == op.Parameters.Count)
        {
            return cache.Parameters;
        }

        var parameters = op.Parameters
            .OrderBy(parameter => parameter.Name, StringComparer.Ordinal)
            .ToArray();

        var newCache = new ParameterFingerprintOrderCache(op.ModifiedAt, op.Parameters.Count, parameters);
        lock (FingerprintParameterOrderCaches)
        {
            FingerprintParameterOrderCaches.Remove(op);
            FingerprintParameterOrderCaches.Add(op, newCache);
        }

        return parameters;
    }

    private static void AppendFingerprintValue(IncrementalHash hasher, object? value)
    {
        if (!TryNormalizeOutputValue(value, out var normalized))
        {
            AppendFingerprintString(hasher, "<unsupported>");
            return;
        }

        switch (normalized)
        {
            case null:
                AppendFingerprintString(hasher, "<null>");
                return;
            case byte[] bytes:
                hasher.AppendData(BitConverter.GetBytes(bytes.Length));
                hasher.AppendData(bytes);
                return;
            case IDictionary<string, object?> dictionary:
                AppendFingerprintString(hasher, "<dict>");
                foreach (var (key, nestedValue) in dictionary.OrderBy(pair => pair.Key, StringComparer.Ordinal))
                {
                    AppendFingerprintString(hasher, key);
                    AppendFingerprintValue(hasher, nestedValue);
                }
                AppendFingerprintString(hasher, "</dict>");
                return;
            case IEnumerable enumerable when normalized is not string:
                AppendFingerprintString(hasher, "<list>");
                foreach (var item in enumerable)
                {
                    AppendFingerprintValue(hasher, item);
                }
                AppendFingerprintString(hasher, "</list>");
                return;
            default:
                AppendTypedFingerprintValue(hasher, normalized);
                return;
        }
    }

    private static void AppendTypedFingerprintValue(IncrementalHash hasher, object value)
    {
        switch (value)
        {
            case string text:
                AppendFingerprintString(hasher, "<string>");
                AppendFingerprintString(hasher, text);
                return;
            case bool boolValue:
                AppendFingerprintString(hasher, "<bool>");
                AppendFingerprintString(hasher, boolValue ? "1" : "0");
                return;
            case char charValue:
                AppendFingerprintString(hasher, "<char>");
                AppendFingerprintString(hasher, charValue.ToString());
                return;
            case sbyte sbyteValue:
                AppendFingerprintString(hasher, "<sbyte>");
                AppendFingerprintString(hasher, sbyteValue.ToString(CultureInfo.InvariantCulture));
                return;
            case byte byteValue:
                AppendFingerprintString(hasher, "<byte>");
                AppendFingerprintString(hasher, byteValue.ToString(CultureInfo.InvariantCulture));
                return;
            case short shortValue:
                AppendFingerprintString(hasher, "<short>");
                AppendFingerprintString(hasher, shortValue.ToString(CultureInfo.InvariantCulture));
                return;
            case ushort ushortValue:
                AppendFingerprintString(hasher, "<ushort>");
                AppendFingerprintString(hasher, ushortValue.ToString(CultureInfo.InvariantCulture));
                return;
            case int intValue:
                AppendFingerprintString(hasher, "<int>");
                AppendFingerprintString(hasher, intValue.ToString(CultureInfo.InvariantCulture));
                return;
            case uint uintValue:
                AppendFingerprintString(hasher, "<uint>");
                AppendFingerprintString(hasher, uintValue.ToString(CultureInfo.InvariantCulture));
                return;
            case long longValue:
                AppendFingerprintString(hasher, "<long>");
                AppendFingerprintString(hasher, longValue.ToString(CultureInfo.InvariantCulture));
                return;
            case ulong ulongValue:
                AppendFingerprintString(hasher, "<ulong>");
                AppendFingerprintString(hasher, ulongValue.ToString(CultureInfo.InvariantCulture));
                return;
            case float floatValue:
                AppendFingerprintString(hasher, "<float>");
                AppendFingerprintString(hasher, floatValue.ToString("R", CultureInfo.InvariantCulture));
                return;
            case double doubleValue:
                AppendFingerprintString(hasher, "<double>");
                AppendFingerprintString(hasher, doubleValue.ToString("R", CultureInfo.InvariantCulture));
                return;
            case decimal decimalValue:
                AppendFingerprintString(hasher, "<decimal>");
                AppendFingerprintString(hasher, decimalValue.ToString(CultureInfo.InvariantCulture));
                return;
            case DateTime dateTimeValue:
                AppendFingerprintString(hasher, "<datetime>");
                AppendFingerprintString(hasher, dateTimeValue.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
                return;
            case DateTimeOffset dateTimeOffsetValue:
                AppendFingerprintString(hasher, "<datetimeoffset>");
                AppendFingerprintString(hasher, dateTimeOffsetValue.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
                return;
            case TimeSpan timeSpanValue:
                AppendFingerprintString(hasher, "<timespan>");
                AppendFingerprintString(hasher, timeSpanValue.ToString("c", CultureInfo.InvariantCulture));
                return;
            case Guid guidValue:
                AppendFingerprintString(hasher, "<guid>");
                AppendFingerprintString(hasher, guidValue.ToString("D"));
                return;
            case IFormattable formattable:
                AppendFingerprintString(hasher, $"<{value.GetType().FullName}>");
                AppendFingerprintString(hasher, formattable.ToString(null, CultureInfo.InvariantCulture) ?? string.Empty);
                return;
            default:
                AppendFingerprintString(hasher, $"<{value.GetType().FullName}>");
                AppendFingerprintString(hasher, value.ToString() ?? string.Empty);
                return;
        }
    }

    private static void AppendFingerprintString(IncrementalHash hasher, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        hasher.AppendData(BitConverter.GetBytes(bytes.Length));
        hasher.AppendData(bytes);
    }

    private void TouchDebugSession(Guid debugSessionId)
    {
        _debugSessionLastAccess[debugSessionId] = DateTime.UtcNow;
    }

    private void CleanupStaleDebugSessions(object? state)
    {
        try
        {
            var staleBefore = DateTime.UtcNow - DebugSessionTtl;
            foreach (var entry in _debugSessionLastAccess)
            {
                if (entry.Value < staleBefore)
                {
                    _ = ClearDebugCacheAsync(entry.Key);
                }
            }

            CleanupStaleExecutionStatuses(DateTime.UtcNow - ExecutionStatusTtl);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "[Debug] Failed to cleanup stale debug sessions.");
        }
    }

    private void CleanupStaleExecutionStatuses(DateTime staleBefore)
    {
        foreach (var entry in _executionStatuses)
        {
            if (entry.Value.IsExecuting)
            {
                continue;
            }

            if (entry.Value.CompletedAt is DateTime completedAt && completedAt < staleBefore)
            {
                _executionStatuses.TryRemove(entry.Key, out _);
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        // This service does not have a finalizer. Per-execution CancellationTokenSources are disposed
        // in the finally blocks of ExecuteFlowAsync / ExecuteFlowDebugAsync; Dispose only tears down
        // the service-level cleanup timer.
        _debugCacheCleanupTimer.Dispose();
        _disposed = true;
    }

    private static OperatorExecutionResult CreateCanceledOperatorResult(Operator op, long executionTimeMs = 0)
    {
        return new OperatorExecutionResult
        {
            OperatorId = op.Id,
            OperatorName = op.Name,
            IsSuccess = false,
            ExecutionTimeMs = executionTimeMs,
            ErrorMessage = OperatorCanceledErrorMessage
        };
    }

    private static OperatorExecutionResult CreateSkippedOperatorResult(Operator op)
    {
        return new OperatorExecutionResult
        {
            OperatorId = op.Id,
            OperatorName = op.Name,
            IsSuccess = true,
            ExecutionTimeMs = 0,
            OutputData = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
        };
    }

    private static OperatorDebugResult CreateSkippedDebugOperatorResult(Operator op, int executionOrder, bool isBreakpoint)
    {
        var now = DateTime.UtcNow;
        return new OperatorDebugResult
        {
            OperatorId = op.Id,
            OperatorName = op.Name,
            IsSuccess = true,
            ExecutionTimeMs = 0,
            ExecutionOrder = executionOrder,
            StartTime = now,
            EndTime = now,
            IsBreakpoint = isBreakpoint,
            InputSnapshot = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase),
            OutputData = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase),
            OutputSnapshot = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
        };
    }

    private static bool IsCanceledOperatorResult(OperatorExecutionResult result)
    {
        return string.Equals(result.ErrorMessage, OperatorCanceledErrorMessage, StringComparison.Ordinal);
    }

    private static async Task CancelLayerAsync(CancellationTokenSource layerCts)
    {
        try
        {
            await layerCts.CancelAsync();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    #endregion
}

