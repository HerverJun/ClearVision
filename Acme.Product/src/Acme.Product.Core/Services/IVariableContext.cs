using System.Collections.Concurrent;
using System.Threading;

namespace Acme.Product.Core.Services;

public interface IVariableContext
{
    VariableContextScope CurrentScope { get; }

    IDisposable BeginScope(VariableContextScope scope);

    T? GetValue<T>(string variableName, T? defaultValue = default);

    void SetValue<T>(string variableName, T value);

    long Increment(string variableName, long delta = 1);

    bool Remove(string variableName);

    bool Contains(string variableName);

    IEnumerable<string> GetVariableNames();

    void Clear();

    long CycleCount { get; }

    void IncrementCycleCount();

    void ResetCycleCount();
}

public sealed record VariableContextScope(
    Guid FlowId,
    Guid RunId,
    string ExecutionKind,
    Guid? ProjectId = null,
    Guid? SessionId = null)
{
    public static VariableContextScope Global { get; } =
        new(Guid.Empty, Guid.Empty, "global");
}

public class VariableContext : IVariableContext
{
    private readonly VariableState _globalState = new(VariableContextScope.Global);
    private readonly AsyncLocal<VariableState?> _currentState = new();

    public VariableContextScope CurrentScope => Current.Scope;

    public long CycleCount => Interlocked.Read(ref Current.CycleCount);

    public IDisposable BeginScope(VariableContextScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        var previous = _currentState.Value;
        _currentState.Value = new VariableState(scope);
        return new ScopeHandle(_currentState, previous);
    }

    public T? GetValue<T>(string variableName, T? defaultValue = default)
    {
        if (Current.Variables.TryGetValue(variableName, out var value))
        {
            try
            {
                if (value is T typed)
                {
                    return typed;
                }

                return (T?)Convert.ChangeType(value, typeof(T));
            }
            catch
            {
                return defaultValue;
            }
        }

        return defaultValue;
    }

    public void SetValue<T>(string variableName, T value)
    {
        Current.Variables[variableName] = value!;
    }

    public long Increment(string variableName, long delta = 1)
    {
        return Current.Variables.AddOrUpdate(variableName, delta, (_, existingValue) =>
        {
            var current = existingValue switch
            {
                long value => value,
                int value => value,
                double value => (long)value,
                _ => 0L
            };
            return current + delta;
        }) switch
        {
            long value => value,
            int value => value,
            _ => 0L
        };
    }

    public bool Remove(string variableName)
    {
        return Current.Variables.TryRemove(variableName, out _);
    }

    public bool Contains(string variableName)
    {
        return Current.Variables.ContainsKey(variableName);
    }

    public IEnumerable<string> GetVariableNames()
    {
        return Current.Variables.Keys.ToList();
    }

    public void Clear()
    {
        Current.Variables.Clear();
        Interlocked.Exchange(ref Current.CycleCount, 0);
    }

    public void IncrementCycleCount()
    {
        Interlocked.Increment(ref Current.CycleCount);
    }

    public void ResetCycleCount()
    {
        Interlocked.Exchange(ref Current.CycleCount, 0);
    }

    private VariableState Current => _currentState.Value ?? _globalState;

    private sealed class VariableState
    {
        public VariableState(VariableContextScope scope)
        {
            Scope = scope;
        }

        public VariableContextScope Scope { get; }

        public ConcurrentDictionary<string, object> Variables { get; } = new();

        public long CycleCount;
    }

    private sealed class ScopeHandle : IDisposable
    {
        private readonly AsyncLocal<VariableState?> _slot;
        private readonly VariableState? _previous;
        private bool _disposed;

        public ScopeHandle(AsyncLocal<VariableState?> slot, VariableState? previous)
        {
            _slot = slot;
            _previous = previous;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _slot.Value = _previous;
            _disposed = true;
        }
    }
}
