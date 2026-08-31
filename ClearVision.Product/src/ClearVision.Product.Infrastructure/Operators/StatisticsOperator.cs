// StatisticsOperator.cs
// 统计算子
// 对输入数据执行统计聚合与指标输出
// 作者：蘅芜君
using System.Collections.Concurrent;
using ClearVision.Product.Core.Attributes;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Operators;
using ClearVision.Product.Core.Services;
using Microsoft.Extensions.Logging;

namespace ClearVision.Product.Infrastructure.Operators;

[OperatorMeta(
    DisplayName = "统计分析",
    Description = "基于滚动历史计算均值、标准差和 Cpk 统计结果。",
    CategoryId = OperatorCategoryId.DataProcessing,
    IconName = "stats"
)]
[InputPort("Value", "Input Value", PortDataType.Float, IsRequired = true)]
[OutputPort("Mean", "Mean", PortDataType.Float)]
[OutputPort("StdDev", "StdDev", PortDataType.Float)]
[OutputPort("Count", "Count", PortDataType.Integer)]
[OutputPort("Min", "Min", PortDataType.Float)]
[OutputPort("Max", "Max", PortDataType.Float)]
[OutputPort("Cpk", "Cpk", PortDataType.Float)]
[OutputPort("IsCapable", "Is Capable", PortDataType.Boolean)]
[OperatorParam("USL", "Upper Specification Limit", "double", DefaultValue = "", Description = "Optional. Cpk is calculated when both USL and LSL are provided.")]
[OperatorParam("LSL", "Lower Specification Limit", "double", DefaultValue = "", Description = "Optional. Cpk is calculated when both USL and LSL are provided.")]
[OperatorParam("WindowSize", "Window Size", "int", DefaultValue = 1000, Min = 2, Max = 50000)]
[OperatorParam("StateTtlMinutes", "State TTL Minutes", "int", DefaultValue = 120, Min = 1, Max = 10080)]
[OperatorParam("Reset", "Reset History", "bool", DefaultValue = false)]
public class StatisticsOperator : OperatorBase
{
    private static readonly ConcurrentDictionary<ExecutionStateKey, RollingHistoryState> HistoryByScope = new();
    private static readonly object CleanupSync = new();
    private static DateTime _lastCleanupUtc = DateTime.MinValue;
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromMinutes(5);

    public override OperatorType OperatorType => OperatorType.Statistics;

    public StatisticsOperator(ILogger<StatisticsOperator> logger) : base(logger)
    {
    }

    protected override Task<OperatorExecutionOutput> ExecuteCoreAsync(
        Operator @operator,
        Dictionary<string, object>? inputs,
        CancellationToken cancellationToken)
    {
        if (!TryGetInputValue<double>(inputs, "Value", out var value))
        {
            return Task.FromResult(OperatorExecutionOutput.Failure("Input Value is required."));
        }

        var usl = GetOptionalDoubleParam(@operator, "USL");
        var lsl = GetOptionalDoubleParam(@operator, "LSL");
        var windowSize = GetIntParam(@operator, "WindowSize", 1000, min: 2, max: 50_000);
        var stateTtlMinutes = GetIntParam(@operator, "StateTtlMinutes", 120, min: 1, max: 10_080);
        var reset = GetBoolParam(@operator, "Reset", false);
        var nowUtc = DateTime.UtcNow;

        var stateKey = ExecutionStateKey.ForOperator(@operator.Id);
        var state = HistoryByScope.GetOrAdd(stateKey, _ => new RollingHistoryState());

        RollingStatisticsSnapshot snapshot;
        lock (state.SyncRoot)
        {
            if (reset)
            {
                state.Clear();
            }

            state.Add(value, windowSize);
            state.LastTouchedUtc = nowUtc;
            state.Ttl = TimeSpan.FromMinutes(stateTtlMinutes);
            snapshot = state.Snapshot();
        }

        TryCleanupStaleStates(nowUtc);

        var count = snapshot.Count;
        var mean = snapshot.Mean;
        var min = snapshot.Min;
        var max = snapshot.Max;
        var stdDev = snapshot.StdDev;

        var output = new Dictionary<string, object>
        {
            { "Mean", mean },
            { "StdDev", stdDev },
            { "Count", count },
            { "Min", min },
            { "Max", max },
            { "Range", max - min },
            { "WindowSize", windowSize },
            { "StateTtlMinutes", stateTtlMinutes }
        };

        if (usl.HasValue && lsl.HasValue && count >= 2 && stdDev > 0)
        {
            var cp = (usl.Value - lsl.Value) / (6.0 * stdDev);
            var cpu = (usl.Value - mean) / (3.0 * stdDev);
            var cpl = (mean - lsl.Value) / (3.0 * stdDev);
            var cpk = Math.Min(cpu, cpl);

            output["Cp"] = Math.Round(cp, 4);
            output["Cpk"] = Math.Round(cpk, 4);
            output["CPU"] = Math.Round(cpu, 4);
            output["CPL"] = Math.Round(cpl, 4);
            output["USL"] = usl.Value;
            output["LSL"] = lsl.Value;
            output["IsCapable"] = cpk >= 1.33;
        }

        Logger.LogDebug(
            "[Statistics] Operator={OperatorId}, Count={Count}, Mean={Mean:F4}, StdDev={StdDev:F4}",
            @operator.Id,
            count,
            mean,
            stdDev);

        return Task.FromResult(OperatorExecutionOutput.Success(output));
    }

    public override ValidationResult ValidateParameters(Operator @operator)
    {
        var usl = GetOptionalDoubleParam(@operator, "USL");
        var lsl = GetOptionalDoubleParam(@operator, "LSL");

        if (usl.HasValue && lsl.HasValue && usl.Value <= lsl.Value)
        {
            return ValidationResult.Invalid("USL must be greater than LSL.");
        }

        var windowSize = GetIntParam(@operator, "WindowSize", 1000);
        if (windowSize < 2 || windowSize > 50_000)
        {
            return ValidationResult.Invalid("WindowSize must be between 2 and 50000.");
        }

        var stateTtlMinutes = GetIntParam(@operator, "StateTtlMinutes", 120);
        if (stateTtlMinutes < 1 || stateTtlMinutes > 10_080)
        {
            return ValidationResult.Invalid("StateTtlMinutes must be between 1 and 10080.");
        }

        return ValidationResult.Valid();
    }

    private static void TryCleanupStaleStates(DateTime nowUtc)
    {
        if ((nowUtc - _lastCleanupUtc) < CleanupInterval)
        {
            return;
        }

        lock (CleanupSync)
        {
            if ((nowUtc - _lastCleanupUtc) < CleanupInterval)
            {
                return;
            }

            foreach (var entry in HistoryByScope)
            {
                var shouldRemove = false;
                var state = entry.Value;
                lock (state.SyncRoot)
                {
                    shouldRemove = state.LastTouchedUtc < nowUtc - state.Ttl;
                }

                if (shouldRemove)
                {
                    HistoryByScope.TryRemove(entry.Key, out _);
                }
            }

            _lastCleanupUtc = nowUtc;
        }
    }

    private static double? GetOptionalDoubleParam(Operator @operator, string name)
    {
        var param = @operator.Parameters.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (param?.Value == null)
        {
            return null;
        }

        return double.TryParse(param.Value.ToString(), out var value) ? value : null;
    }

    private readonly record struct RollingStatisticsSnapshot(
        int Count,
        double Mean,
        double StdDev,
        double Min,
        double Max);

    private sealed class RollingHistoryState
    {
        public object SyncRoot { get; } = new();

        private readonly Queue<double> _values = new();
        private readonly LinkedList<double> _minCandidates = new();
        private readonly LinkedList<double> _maxCandidates = new();
        private double _sum;
        private double _sumSquares;

        public DateTime LastTouchedUtc { get; set; } = DateTime.UtcNow;

        public TimeSpan Ttl { get; set; } = TimeSpan.FromMinutes(120);

        public void Add(double value, int windowSize)
        {
            _values.Enqueue(value);
            _sum += value;
            _sumSquares += value * value;

            while (_minCandidates.Last != null && _minCandidates.Last.Value > value)
            {
                _minCandidates.RemoveLast();
            }

            _minCandidates.AddLast(value);

            while (_maxCandidates.Last != null && _maxCandidates.Last.Value < value)
            {
                _maxCandidates.RemoveLast();
            }

            _maxCandidates.AddLast(value);

            while (_values.Count > windowSize)
            {
                RemoveOldest();
            }
        }

        public void Clear()
        {
            _values.Clear();
            _minCandidates.Clear();
            _maxCandidates.Clear();
            _sum = 0.0;
            _sumSquares = 0.0;
        }

        public RollingStatisticsSnapshot Snapshot()
        {
            var count = _values.Count;
            if (count == 0)
            {
                return new RollingStatisticsSnapshot(0, 0.0, 0.0, 0.0, 0.0);
            }

            var mean = _sum / count;
            var variance = count > 1
                ? (_sumSquares - (_sum * _sum / count)) / (count - 1)
                : 0.0;
            return new RollingStatisticsSnapshot(
                count,
                mean,
                Math.Sqrt(Math.Max(0.0, variance)),
                _minCandidates.First!.Value,
                _maxCandidates.First!.Value);
        }

        private void RemoveOldest()
        {
            var removed = _values.Dequeue();
            _sum -= removed;
            _sumSquares -= removed * removed;

            if (_minCandidates.First != null && _minCandidates.First.Value.Equals(removed))
            {
                _minCandidates.RemoveFirst();
            }

            if (_maxCandidates.First != null && _maxCandidates.First.Value.Equals(removed))
            {
                _maxCandidates.RemoveFirst();
            }
        }
    }
}
