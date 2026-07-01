using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ClearVision.Product.Core.ProjectVariables;

public interface IProjectVariableSession : IDisposable
{
    ProjectGlobalVariableSchema Schema { get; }

    long SchemaGeneration { get; }

    IReadOnlyList<ProjectVariableValueSnapshot> GetSnapshots();

    IProjectVariableSession CreateSnapshotClone();

    bool TryCommitFrom(
        IProjectVariableSession source,
        IReadOnlyDictionary<Guid, long> expectedVersions,
        out string? error);

    bool TryGetValue(Guid variableId, out JsonElement value);

    bool TryGetSnapshot(Guid variableId, out ProjectVariableValueSnapshot snapshot);

    bool TryGetDefinition(Guid variableId, out ProjectGlobalVariableDefinition definition);

    bool TryGetDefinitionByName(string variableName, out ProjectGlobalVariableDefinition definition);

    ProjectVariableValueSnapshot SetValue(
        Guid variableId,
        object? value,
        ProjectVariableUpdatedBy updatedBy,
        Guid? runId = null,
        Guid? operatorId = null);

    ProjectVariableValueSnapshot Increment(
        Guid variableId,
        long delta,
        ProjectVariableUpdatedBy updatedBy,
        Guid? runId = null,
        Guid? operatorId = null);

    ProjectVariableIncrementResult IncrementAtomic(
        Guid variableId,
        long delta,
        ProjectVariableUpdatedBy updatedBy,
        Guid? runId = null,
        Guid? operatorId = null,
        string resetCondition = "None",
        long resetThreshold = 0,
        long resetValue = 0);

    ProjectVariableValueSnapshot Reset(Guid variableId, ProjectVariableUpdatedBy updatedBy);

    void ResetAll(ProjectVariableUpdatedBy updatedBy);
}

public interface IProjectVariableStateStore
{
    IReadOnlyList<ProjectVariableValueSnapshot> Load(string scopeId, ProjectGlobalVariableSchema schema);

    void Save(string scopeId, ProjectGlobalVariableSchema schema, IReadOnlyList<ProjectVariableValueSnapshot> snapshots);

    void Save(
        string scopeId,
        ProjectGlobalVariableSchema schema,
        IReadOnlyList<ProjectVariableValueSnapshot> snapshots,
        long persistenceRevision,
        Guid? saveId)
    {
        Save(scopeId, schema, snapshots);
    }

    ProjectVariableStateMetadata? LoadMetadata(string scopeId)
    {
        return null;
    }

    void Delete(string scopeId);
}

public sealed record ProjectVariableStateMetadata(
    int SchemaVersion,
    string ScopeId,
    long PersistenceRevision,
    string SchemaHash,
    string StateHash,
    DateTimeOffset SavedAtUtc,
    Guid? SaveId);

public static class ProjectVariableStateHash
{
    private static readonly JsonSerializerOptions HashJsonOptions = new(JsonSerializerDefaults.Web);

    public static string Compute(
        ProjectGlobalVariableSchema schema,
        IReadOnlyList<ProjectVariableValueSnapshot> snapshots)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(snapshots);
        var definitionsById = schema.Variables
            .Where(variable => variable.Id != Guid.Empty)
            .GroupBy(variable => variable.Id)
            .ToDictionary(group => group.Key, group => group.First());

        return Compute(
            ProjectGlobalVariableSchemaValidator.ComputeSchemaHash(schema),
            snapshots.Select(snapshot => snapshot with
            {
                Value = NormalizeSnapshotValue(snapshot, definitionsById)
            }).ToList());
    }

    public static string Compute(
        string schemaHash,
        IReadOnlyList<ProjectVariableValueSnapshot> snapshots)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(schemaHash);
        ArgumentNullException.ThrowIfNull(snapshots);
        var payload = new
        {
            schemaHash,
            variables = snapshots
                .OrderBy(snapshot => snapshot.VariableId)
                .Select(snapshot => new
                {
                    variableId = snapshot.VariableId,
                    value = snapshot.Value,
                    version = snapshot.Version,
                    updatedAtUtc = snapshot.UpdatedAtUtc,
                    updatedBy = snapshot.UpdatedBy,
                    runId = snapshot.RunId,
                    operatorId = snapshot.OperatorId
                })
                .ToList()
        };
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, HashJsonOptions);
        var hash = SHA256.HashData(bytes);
        return "sha256:" + Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static JsonElement NormalizeSnapshotValue(
        ProjectVariableValueSnapshot snapshot,
        IReadOnlyDictionary<Guid, ProjectGlobalVariableDefinition> definitionsById)
    {
        if (definitionsById.TryGetValue(snapshot.VariableId, out var definition) &&
            definition.ValueType == ProjectGlobalVariableValueType.Int64)
        {
            return snapshot.Value.ValueKind == JsonValueKind.String
                ? JsonSerializer.SerializeToElement(snapshot.Value.GetString() ?? string.Empty)
                : JsonSerializer.SerializeToElement(snapshot.Value.GetRawText());
        }

        return snapshot.Value.Clone();
    }
}

public sealed class ProjectVariableSession : IProjectVariableSession
{
    private readonly ConcurrentDictionary<Guid, ProjectVariableState> _states = new();
    private readonly Dictionary<Guid, ProjectGlobalVariableDefinition> _definitionsById;
    private readonly Dictionary<string, ProjectGlobalVariableDefinition> _definitionsByName;
    private readonly object _stateGate = new();
    private bool _disposed;

    public ProjectVariableSession(ProjectGlobalVariableSchema? schema)
        : this(schema, snapshots: null, schemaGeneration: 0)
    {
    }

    public ProjectVariableSession(
        ProjectGlobalVariableSchema? schema,
        IEnumerable<ProjectVariableValueSnapshot>? snapshots)
        : this(schema, snapshots, schemaGeneration: 0)
    {
    }

    public ProjectVariableSession(
        ProjectGlobalVariableSchema? schema,
        IEnumerable<ProjectVariableValueSnapshot>? snapshots,
        long schemaGeneration)
    {
        Schema = schema ?? new ProjectGlobalVariableSchema();
        SchemaGeneration = Math.Max(0, schemaGeneration);
        _definitionsById = Schema.Variables
            .Where(variable => variable.Id != Guid.Empty)
            .GroupBy(variable => variable.Id)
            .ToDictionary(group => group.Key, group => group.First());
        _definitionsByName = Schema.Variables
            .Where(variable => !string.IsNullOrWhiteSpace(variable.Name))
            .GroupBy(variable => variable.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        ResetAll(ProjectVariableUpdatedBy.Initial);
        if (snapshots != null)
        {
            ApplySnapshots(snapshots);
        }
    }

    private void ApplySnapshots(IEnumerable<ProjectVariableValueSnapshot> snapshots)
    {
        lock (_stateGate)
        {
            foreach (var snapshot in snapshots)
            {
                if (!_definitionsById.TryGetValue(snapshot.VariableId, out var definition))
                {
                    continue;
                }

                if (!ProjectVariableValueConverter.TryConvertToVariableValue(
                        snapshot.Value,
                        definition.ValueType,
                        out var converted,
                        out _))
                {
                    ResetIncompatibleSnapshot(definition, snapshot.Version);
                    continue;
                }

                try
                {
                    ValidateRange(definition, converted);
                }
                catch (InvalidOperationException)
                {
                    ResetIncompatibleSnapshot(definition, snapshot.Version);
                    continue;
                }

                _states[snapshot.VariableId] = new ProjectVariableState(
                    snapshot.VariableId,
                    converted.Clone(),
                    Math.Max(0, snapshot.Version),
                    snapshot.UpdatedAtUtc == default ? DateTimeOffset.UtcNow : snapshot.UpdatedAtUtc,
                    snapshot.UpdatedBy,
                    snapshot.RunId,
                    snapshot.OperatorId);
            }
        }
    }

    private void ResetIncompatibleSnapshot(ProjectGlobalVariableDefinition definition, long previousVersion)
    {
        if (!ProjectVariableValueConverter.TryConvertToVariableValue(definition.InitialValue, definition.ValueType, out var converted, out var error))
        {
            throw new InvalidOperationException($"Project global variable '{definition.Name}' has invalid initial value: {error}");
        }

        ValidateRange(definition, converted);
        var nextVersion = previousVersion >= long.MaxValue
            ? long.MaxValue
            : Math.Max(0, previousVersion) + 1;
        _states[definition.Id] = new ProjectVariableState(
            definition.Id,
            converted,
            nextVersion,
            DateTimeOffset.UtcNow,
            ProjectVariableUpdatedBy.Reset,
            null,
            null);
    }

    public ProjectGlobalVariableSchema Schema { get; }

    public long SchemaGeneration { get; }

    public IReadOnlyList<ProjectVariableValueSnapshot> GetSnapshots()
    {
        lock (_stateGate)
        {
            return _states.Values
                .Select(state => state.ToSnapshot())
                .OrderBy(snapshot => _definitionsById.TryGetValue(snapshot.VariableId, out var definition) ? definition.Order : int.MaxValue)
                .ToList();
        }
    }

    public IProjectVariableSession CreateSnapshotClone()
    {
        ThrowIfDisposed();
        return new ProjectVariableSession(Schema, GetSnapshots(), SchemaGeneration);
    }

    public bool TryGetValue(Guid variableId, out JsonElement value)
    {
        lock (_stateGate)
        {
            if (_states.TryGetValue(variableId, out var state))
            {
                value = state.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    public bool TryGetSnapshot(Guid variableId, out ProjectVariableValueSnapshot snapshot)
    {
        lock (_stateGate)
        {
            if (_states.TryGetValue(variableId, out var state))
            {
                snapshot = state.ToSnapshot();
                return true;
            }
        }

        snapshot = default!;
        return false;
    }

    public bool TryCommitFrom(
        IProjectVariableSession source,
        IReadOnlyDictionary<Guid, long> expectedVersions,
        out string? error)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(expectedVersions);

        var sourceSnapshots = source.GetSnapshots();
        lock (_stateGate)
        {
            foreach (var (variableId, expectedVersion) in expectedVersions)
            {
                if (!_states.TryGetValue(variableId, out var current))
                {
                    error = $"GV025: project global variable '{variableId}' no longer exists in the authoritative session.";
                    return false;
                }

                if (current.Version != expectedVersion)
                {
                    error = $"GV025: project global variable '{variableId}' changed from version {expectedVersion} to {current.Version} before this run could commit.";
                    return false;
                }
            }

            foreach (var snapshot in sourceSnapshots)
            {
                _states[snapshot.VariableId] = new ProjectVariableState(
                    snapshot.VariableId,
                    snapshot.Value.Clone(),
                    snapshot.Version,
                    snapshot.UpdatedAtUtc,
                    snapshot.UpdatedBy,
                    snapshot.RunId,
                    snapshot.OperatorId);
            }
        }

        error = null;
        return true;
    }

    public bool TryGetDefinition(Guid variableId, out ProjectGlobalVariableDefinition definition)
    {
        return _definitionsById.TryGetValue(variableId, out definition!);
    }

    public bool TryGetDefinitionByName(string variableName, out ProjectGlobalVariableDefinition definition)
    {
        return _definitionsByName.TryGetValue(variableName, out definition!);
    }

    public ProjectVariableValueSnapshot SetValue(
        Guid variableId,
        object? value,
        ProjectVariableUpdatedBy updatedBy,
        Guid? runId = null,
        Guid? operatorId = null)
    {
        ThrowIfDisposed();
        if (!_definitionsById.TryGetValue(variableId, out var definition))
        {
            throw new InvalidOperationException($"Project global variable '{variableId}' does not exist.");
        }

        if (!ProjectVariableValueConverter.TryConvertToVariableValue(value, definition.ValueType, out var converted, out var error))
        {
            throw new InvalidOperationException($"Project global variable '{definition.Name}' rejected value: {error}");
        }

        ValidateRange(definition, converted);

        ProjectVariableState state;
        lock (_stateGate)
        {
            state = _states.TryGetValue(variableId, out var current)
                ? current.Next(converted, updatedBy, runId, operatorId)
                : new ProjectVariableState(variableId, converted, 1, DateTimeOffset.UtcNow, updatedBy, runId, operatorId);
            _states[variableId] = state;
        }

        return state.ToSnapshot();
    }

    public ProjectVariableValueSnapshot Increment(
        Guid variableId,
        long delta,
        ProjectVariableUpdatedBy updatedBy,
        Guid? runId = null,
        Guid? operatorId = null)
    {
        return IncrementAtomic(variableId, delta, updatedBy, runId, operatorId).Snapshot;
    }

    public ProjectVariableIncrementResult IncrementAtomic(
        Guid variableId,
        long delta,
        ProjectVariableUpdatedBy updatedBy,
        Guid? runId = null,
        Guid? operatorId = null,
        string resetCondition = "None",
        long resetThreshold = 0,
        long resetValue = 0)
    {
        ThrowIfDisposed();
        if (!_definitionsById.TryGetValue(variableId, out var definition))
        {
            throw new InvalidOperationException($"Project global variable '{variableId}' does not exist.");
        }

        if (definition.ValueType != ProjectGlobalVariableValueType.Int64)
        {
            throw new InvalidOperationException($"Project global variable '{definition.Name}' is {definition.ValueType}; only Int64 can be incremented.");
        }

        lock (_stateGate)
        {
            if (!_states.TryGetValue(variableId, out var current))
            {
                var previous = 0L;
                var wasReset = ShouldReset(previous, resetCondition, resetThreshold);
                var next = checked((wasReset ? resetValue : previous) + delta);
                var converted = JsonSerializer.SerializeToElement(next);
                ValidateRange(definition, converted);
                var created = new ProjectVariableState(variableId, converted, 1, DateTimeOffset.UtcNow, updatedBy, runId, operatorId);

                _states[variableId] = created;
                return new ProjectVariableIncrementResult(created.ToSnapshot(), previous, next, wasReset);
            }

            var currentValue = Convert.ToInt64(ProjectVariableValueConverter.ToObject(current.Value));
            var shouldReset = ShouldReset(currentValue, resetCondition, resetThreshold);
            var nextValue = checked((shouldReset ? resetValue : currentValue) + delta);
            var nextElement = JsonSerializer.SerializeToElement(nextValue);
            ValidateRange(definition, nextElement);
            var updated = current.Next(nextElement, updatedBy, runId, operatorId);

            _states[variableId] = updated;
            return new ProjectVariableIncrementResult(updated.ToSnapshot(), currentValue, nextValue, shouldReset);
        }
    }

    public ProjectVariableValueSnapshot Reset(Guid variableId, ProjectVariableUpdatedBy updatedBy)
    {
        ThrowIfDisposed();
        if (!_definitionsById.TryGetValue(variableId, out var definition))
        {
            throw new InvalidOperationException($"Project global variable '{variableId}' does not exist.");
        }

        return SetValue(variableId, definition.InitialValue, updatedBy);
    }

    public void ResetAll(ProjectVariableUpdatedBy updatedBy)
    {
        ThrowIfDisposed();
        lock (_stateGate)
        {
            foreach (var definition in Schema.Variables)
            {
                if (!ProjectVariableValueConverter.TryConvertToVariableValue(definition.InitialValue, definition.ValueType, out var converted, out var error))
                {
                    throw new InvalidOperationException($"Project global variable '{definition.Name}' has invalid initial value: {error}");
                }

                ValidateRange(definition, converted);
                _states[definition.Id] = _states.TryGetValue(definition.Id, out var current) &&
                    updatedBy != ProjectVariableUpdatedBy.Initial
                        ? current.Next(converted, updatedBy, null, null)
                        : new ProjectVariableState(
                            definition.Id,
                            converted,
                            updatedBy == ProjectVariableUpdatedBy.Initial ? 0 : 1,
                            DateTimeOffset.UtcNow,
                            updatedBy,
                            null,
                            null);
            }
        }
    }

    public void Dispose()
    {
        _disposed = true;
        lock (_stateGate)
        {
            _states.Clear();
        }
    }

    private static void ValidateRange(ProjectGlobalVariableDefinition definition, JsonElement value)
    {
        if (definition.ValueType is not (ProjectGlobalVariableValueType.Int64 or ProjectGlobalVariableValueType.Double))
        {
            return;
        }

        if (definition.ValueType == ProjectGlobalVariableValueType.Int64)
        {
            var numeric = value.GetInt64();
            if (definition.MinBound.HasValue && definition.MinBound.Value.TryGetInt64(out var min) && numeric < min)
            {
                throw new InvalidOperationException($"Project global variable '{definition.Name}' value is below Min={definition.MinBound.Value.Text}.");
            }

            if (definition.MaxBound.HasValue && definition.MaxBound.Value.TryGetInt64(out var max) && numeric > max)
            {
                throw new InvalidOperationException($"Project global variable '{definition.Name}' value is above Max={definition.MaxBound.Value.Text}.");
            }

            return;
        }

        var doubleValue = value.GetDouble();
        if (definition.MinBound.HasValue && definition.MinBound.Value.TryGetDouble(out var doubleMin) && doubleValue < doubleMin)
        {
            throw new InvalidOperationException($"Project global variable '{definition.Name}' value is below Min={definition.MinBound.Value.Text}.");
        }

        if (definition.MaxBound.HasValue && definition.MaxBound.Value.TryGetDouble(out var doubleMax) && doubleValue > doubleMax)
        {
            throw new InvalidOperationException($"Project global variable '{definition.Name}' value is above Max={definition.MaxBound.Value.Text}.");
        }
    }

    private static bool ShouldReset(long currentValue, string resetCondition, long resetThreshold)
    {
        return resetCondition.ToLowerInvariant() switch
        {
            "greaterthan" => currentValue > resetThreshold,
            "lessthan" => currentValue < resetThreshold,
            "equal" => currentValue == resetThreshold,
            _ => false
        };
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(ProjectVariableSession));
        }
    }

    private sealed record ProjectVariableState(
        Guid VariableId,
        JsonElement Value,
        long Version,
        DateTimeOffset UpdatedAtUtc,
        ProjectVariableUpdatedBy UpdatedBy,
        Guid? RunId,
        Guid? OperatorId)
    {
        public ProjectVariableState Next(JsonElement value, ProjectVariableUpdatedBy updatedBy, Guid? runId, Guid? operatorId)
        {
            return this with
            {
                Value = value,
                Version = Version + 1,
                UpdatedAtUtc = DateTimeOffset.UtcNow,
                UpdatedBy = updatedBy,
                RunId = runId,
                OperatorId = operatorId
            };
        }

        public ProjectVariableValueSnapshot ToSnapshot()
        {
            return new ProjectVariableValueSnapshot(VariableId, Value, Version, UpdatedAtUtc, UpdatedBy, RunId, OperatorId);
        }
    }
}

public sealed record ProjectVariableIncrementResult(
    ProjectVariableValueSnapshot Snapshot,
    long PreviousValue,
    long NewValue,
    bool WasReset);
