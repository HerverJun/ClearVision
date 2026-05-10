// TriggerModuleOperator.cs
// 触发模块算子
// 管理软件触发、定时触发与外部触发流程
// 作者：蘅芜君
using Acme.Product.Core.Attributes;
using Acme.Product.Core.Entities;
using Acme.Product.Core.Enums;
using Acme.Product.Core.Operators;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace Acme.Product.Infrastructure.Operators;

[OperatorMeta(
    DisplayName = "触发模块",
    Description = "Generates software, timer, or external triggers.",
    Category = "逻辑工具",
    IconName = "trigger",
    Keywords = new[] { "trigger", "start", "timer", "external signal" }
)]
[InputPort("Signal", "Signal", PortDataType.Boolean, IsRequired = false)]
[OutputPort("Triggered", "Triggered", PortDataType.Boolean)]
[OutputPort("Timestamp", "Timestamp", PortDataType.String)]
[OutputPort("TriggerCount", "Trigger Count", PortDataType.Integer)]
[OperatorParam("TriggerMode", "Trigger Mode", "enum", DefaultValue = "Software", Options = new[] { "Software|Software", "Timer|Timer", "ExternalSignal|ExternalSignal" })]
[OperatorParam("Interval", "Interval (ms)", "int", DefaultValue = 1000, Min = 1, Max = 3600000)]
[OperatorParam("AutoRepeat", "Auto Repeat", "bool", DefaultValue = true)]
public class TriggerModuleOperator : OperatorBase
{
    private static readonly TimeSpan StateTtl = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromMinutes(5);

    private readonly ConcurrentDictionary<Guid, TriggerModuleState> _states = new();
    private readonly object _cleanupSync = new();
    private DateTime _lastCleanupUtc = DateTime.MinValue;

    public override OperatorType OperatorType => OperatorType.TriggerModule;

    public TriggerModuleOperator(ILogger<TriggerModuleOperator> logger) : base(logger)
    {
    }

    protected override Task<OperatorExecutionOutput> ExecuteCoreAsync(
        Operator @operator,
        Dictionary<string, object>? inputs,
        CancellationToken cancellationToken)
    {
        var mode = GetStringParam(@operator, "TriggerMode", "Software");
        var intervalMs = GetIntParam(@operator, "Interval", 1000, 1, 3_600_000);
        var autoRepeat = GetBoolParam(@operator, "AutoRepeat", true);

        var now = DateTime.UtcNow;
        if (mode.Equals("ExternalSignal", StringComparison.OrdinalIgnoreCase) &&
            !TryGetSignalInput(inputs, out _))
        {
            return Task.FromResult(OperatorExecutionOutput.Failure("Signal input is required in ExternalSignal mode."));
        }

        var triggered = false;
        var count = 0;
        var state = _states.GetOrAdd(@operator.Id, static _ => new TriggerModuleState());

        lock (state.SyncRoot)
        {
            if (mode.Equals("Software", StringComparison.OrdinalIgnoreCase))
            {
                triggered = true;
            }
            else if (mode.Equals("Timer", StringComparison.OrdinalIgnoreCase))
            {
                triggered = ShouldTriggerByTimer(state, now, intervalMs, autoRepeat);
            }
            else
            {
                triggered = TryGetSignalInput(inputs, out var signal) && signal;
            }

            if (triggered)
            {
                state.LastTriggerUtc = now;
                state.TriggerCount++;
            }

            count = state.TriggerCount;
            state.LastTouchedUtc = now;
        }

        TryCleanupStaleStates(now);

        var output = new Dictionary<string, object>
        {
            { "Triggered", triggered },
            { "Timestamp", now.ToString("O") },
            { "TriggerCount", count },
            { "StateScope", "OperatorInstance" },
            { "StateKey", @operator.Id }
        };

        return Task.FromResult(OperatorExecutionOutput.Success(output));
    }

    public override ValidationResult ValidateParameters(Operator @operator)
    {
        var mode = GetStringParam(@operator, "TriggerMode", "Software");
        var validModes = new[] { "Software", "Timer", "ExternalSignal" };
        if (!validModes.Contains(mode, StringComparer.OrdinalIgnoreCase))
        {
            return ValidationResult.Invalid("TriggerMode must be Software, Timer or ExternalSignal");
        }

        if (mode.Equals("Timer", StringComparison.OrdinalIgnoreCase))
        {
            var interval = GetIntParam(@operator, "Interval", 1000);
            if (interval <= 0)
            {
                return ValidationResult.Invalid("Interval must be greater than 0 in Timer mode");
            }
        }

        return ValidationResult.Valid();
    }

    private bool ShouldTriggerByTimer(TriggerModuleState state, DateTime now, int intervalMs, bool autoRepeat)
    {
        if (state.TriggerCount == 0)
        {
            return true;
        }

        if (!autoRepeat)
        {
            return false;
        }

        if (state.LastTriggerUtc == DateTime.MinValue)
        {
            return true;
        }

        return (now - state.LastTriggerUtc).TotalMilliseconds >= intervalMs;
    }

    private void TryCleanupStaleStates(DateTime nowUtc)
    {
        if ((nowUtc - _lastCleanupUtc) < CleanupInterval)
        {
            return;
        }

        lock (_cleanupSync)
        {
            if ((nowUtc - _lastCleanupUtc) < CleanupInterval)
            {
                return;
            }

            var staleBefore = nowUtc - StateTtl;
            foreach (var entry in _states)
            {
                var state = entry.Value;
                var shouldRemove = false;
                lock (state.SyncRoot)
                {
                    shouldRemove = state.LastTouchedUtc < staleBefore;
                }

                if (shouldRemove)
                {
                    _states.TryRemove(entry.Key, out _);
                }
            }

            _lastCleanupUtc = nowUtc;
        }
    }

    private static bool TryGetSignalInput(Dictionary<string, object>? inputs, out bool signal)
    {
        signal = false;

        if (inputs == null)
        {
            return false;
        }

        if (inputs.TryGetValue("Signal", out var signalObj) && TryConvertToBool(signalObj, out signal))
        {
            return true;
        }

        return false;
    }

    private static bool TryConvertToBool(object? raw, out bool value)
    {
        value = false;

        if (raw is null)
        {
            return false;
        }

        return raw switch
        {
            bool b => (value = b) == b,
            int i => (value = i != 0) || i == 0,
            long l => (value = l != 0) || l == 0,
            double d => (value = Math.Abs(d) > double.Epsilon) || Math.Abs(d) <= double.Epsilon,
            _ => bool.TryParse(raw.ToString(), out value)
        };
    }

    private sealed class TriggerModuleState
    {
        public object SyncRoot { get; } = new();
        public DateTime LastTriggerUtc { get; set; } = DateTime.MinValue;
        public DateTime LastTouchedUtc { get; set; } = DateTime.UtcNow;
        public int TriggerCount { get; set; }
    }
}

