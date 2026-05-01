// TimerStatisticsOperator.cs
// 计时统计算子
// 统计流程或算子的耗时并输出指标
// 作者：蘅芜君
using System.Diagnostics;
using System.Collections.Concurrent;
using Acme.Product.Core.Entities;
using Acme.Product.Core.Enums;
using Acme.Product.Core.Operators;
using Microsoft.Extensions.Logging;

using Acme.Product.Core.Attributes;
namespace Acme.Product.Infrastructure.Operators;

[OperatorMeta(
    DisplayName = "计时统计",
    Description = "Measures elapsed and cycle time statistics.",
    Category = "逻辑工具",
    IconName = "timer",
    Keywords = new[] { "timer", "elapsed", "cycle time", "ct", "statistics" },
    Version = "1.0.1"
)]
[InputPort("Trigger", "Trigger", PortDataType.Any, IsRequired = false)]
[OutputPort("ElapsedMs", "Elapsed (ms)", PortDataType.Float)]
[OutputPort("TotalMs", "Total (ms)", PortDataType.Float)]
[OutputPort("AverageMs", "Average (ms)", PortDataType.Float)]
[OutputPort("Count", "Count", PortDataType.Integer)]
[OperatorParam("Mode", "Mode", "enum", DefaultValue = "SingleShot", Options = new[] { "SingleShot|SingleShot", "Cumulative|Cumulative" })]
[OperatorParam("ResetInterval", "Reset Interval", "int", DefaultValue = 0, Min = 0, Max = 1000000)]
[OperatorParam("StateTtlMinutes", "State TTL Minutes", "int", DefaultValue = 120, Min = 0, Max = 10080)]
[OperatorParam("Reset", "Reset History", "bool", DefaultValue = false)]
public class TimerStatisticsOperator : OperatorBase
{
    private readonly ConcurrentDictionary<Guid, TimerState> _states = new();

    public override OperatorType OperatorType => OperatorType.TimerStatistics;

    public TimerStatisticsOperator(ILogger<TimerStatisticsOperator> logger) : base(logger)
    {
    }

    protected override Task<OperatorExecutionOutput> ExecuteCoreAsync(
        Operator @operator,
        Dictionary<string, object>? inputs,
        CancellationToken cancellationToken)
    {
        var mode = GetStringParam(@operator, "Mode", "SingleShot");
        if (!IsValidMode(mode))
        {
            return Task.FromResult(OperatorExecutionOutput.Failure("Mode must be SingleShot or Cumulative"));
        }

        var resetInterval = GetIntParam(@operator, "ResetInterval", 0);
        if (resetInterval < 0)
        {
            return Task.FromResult(OperatorExecutionOutput.Failure("ResetInterval must be >= 0"));
        }

        if (resetInterval > 1_000_000)
        {
            return Task.FromResult(OperatorExecutionOutput.Failure("ResetInterval must be <= 1000000"));
        }

        var stateTtlMinutes = GetIntParam(@operator, "StateTtlMinutes", 120, min: 0, max: 10_080);
        var reset = GetBoolParam(@operator, "Reset", false);
        var nowUtc = DateTime.UtcNow;
        CleanupStaleStates(nowUtc, @operator.Id);

        if (reset)
        {
            _states.TryRemove(@operator.Id, out _);
            return Task.FromResult(OperatorExecutionOutput.Success(CreateOutput(
                elapsedMs: 0,
                totalMs: 0,
                averageMs: 0,
                count: 0,
                @operator.Id,
                stateTtlMinutes,
                resetApplied: true,
                inputs)));
        }

        double elapsedMs;
        double totalMs;
        double averageMs;
        int count;
        var state = _states.GetOrAdd(@operator.Id, static _ => new TimerState());

        lock (state.SyncRoot)
        {
            if (!state.Started)
            {
                state.IntervalStopwatch.Start();
                state.Started = true;
                elapsedMs = 0;
            }
            else
            {
                elapsedMs = state.IntervalStopwatch.Elapsed.TotalMilliseconds;
                state.IntervalStopwatch.Restart();
            }

            if (mode.Equals("Cumulative", StringComparison.OrdinalIgnoreCase))
            {
                state.Count++;
                state.TotalMs += elapsedMs;

                totalMs = state.TotalMs;
                count = state.Count;
                averageMs = state.Count > 0 ? state.TotalMs / state.Count : 0;

                if (resetInterval > 0 && state.Count >= resetInterval)
                {
                    state.Count = 0;
                    state.TotalMs = 0;
                    state.IntervalStopwatch.Restart();
                }
            }
            else
            {
                totalMs = elapsedMs;
                averageMs = elapsedMs;
                count = 1;
            }

            state.LastTouchedUtc = nowUtc;
            state.Ttl = TimeSpan.FromMinutes(stateTtlMinutes);
        }

        return Task.FromResult(OperatorExecutionOutput.Success(CreateOutput(
            elapsedMs,
            totalMs,
            averageMs,
            count,
            @operator.Id,
            stateTtlMinutes,
            resetApplied: false,
            inputs)));
    }

    public override ValidationResult ValidateParameters(Operator @operator)
    {
        var mode = GetStringParam(@operator, "Mode", "SingleShot");
        if (!IsValidMode(mode))
        {
            return ValidationResult.Invalid("Mode must be SingleShot or Cumulative");
        }

        var resetInterval = GetIntParam(@operator, "ResetInterval", 0);
        if (resetInterval < 0)
        {
            return ValidationResult.Invalid("ResetInterval must be >= 0");
        }

        if (resetInterval > 1_000_000)
        {
            return ValidationResult.Invalid("ResetInterval must be <= 1000000");
        }

        var stateTtlMinutes = GetIntParam(@operator, "StateTtlMinutes", 120);
        if (stateTtlMinutes < 0 || stateTtlMinutes > 10_080)
        {
            return ValidationResult.Invalid("StateTtlMinutes must be between 0 and 10080.");
        }

        return ValidationResult.Valid();
    }

    private static bool IsValidMode(string mode)
    {
        return mode.Equals("SingleShot", StringComparison.OrdinalIgnoreCase)
            || mode.Equals("Cumulative", StringComparison.OrdinalIgnoreCase);
    }

    private Dictionary<string, object> CreateOutput(
        double elapsedMs,
        double totalMs,
        double averageMs,
        int count,
        Guid operatorId,
        int stateTtlMinutes,
        bool resetApplied,
        Dictionary<string, object>? inputs)
    {
        var output = new Dictionary<string, object>
        {
            { "ElapsedMs", elapsedMs },
            { "TotalMs", totalMs },
            { "AverageMs", averageMs },
            { "Count", count },
            { "StateScope", "OperatorInstance" },
            { "StateKey", operatorId },
            { "StateTtlMinutes", stateTtlMinutes },
            { "ResetApplied", resetApplied },
            { "Diagnostics", new Dictionary<string, object>
                {
                    { "StateScope", "OperatorInstance" },
                    { "StateStorage", "InMemoryByOperatorId" },
                    { "StateTtlMinutes", stateTtlMinutes },
                    { "ResetApplied", resetApplied }
                }
            }
        };

        if (inputs != null && inputs.TryGetValue("Trigger", out var trigger))
        {
            output["Trigger"] = trigger;
        }

        return output;
    }

    private void CleanupStaleStates(DateTime nowUtc, Guid currentOperatorId)
    {
        foreach (var entry in _states)
        {
            if (entry.Key == currentOperatorId)
            {
                continue;
            }

            var shouldRemove = false;
            lock (entry.Value.SyncRoot)
            {
                shouldRemove = entry.Value.LastTouchedUtc <= nowUtc - entry.Value.Ttl;
            }

            if (shouldRemove)
            {
                _states.TryRemove(entry.Key, out _);
            }
        }
    }

    private sealed class TimerState
    {
        public object SyncRoot { get; } = new();
        public Stopwatch IntervalStopwatch { get; } = new();
        public bool Started { get; set; }
        public double TotalMs { get; set; }
        public int Count { get; set; }
        public DateTime LastTouchedUtc { get; set; } = DateTime.UtcNow;
        public TimeSpan Ttl { get; set; } = TimeSpan.FromMinutes(120);
    }
}

