using System.Collections.Concurrent;
using ClearVision.Product.Application.DTOs;
using ClearVision.Product.Core.ProjectVariables;

namespace ClearVision.Product.Application.Services;

public sealed class ProjectVariableSessionRegistry
{
    private readonly ConcurrentDictionary<Guid, IProjectVariableSession> _sessions = new();
    private readonly ConcurrentDictionary<Guid, object> _projectGates = new();
    private readonly IProjectVariableStateStore? _stateStore;

    public ProjectVariableSessionRegistry()
        : this(null)
    {
    }

    public ProjectVariableSessionRegistry(IProjectVariableStateStore? stateStore)
    {
        _stateStore = stateStore;
    }

    public IProjectVariableSession GetOrCreate(ProjectDto project)
    {
        ArgumentNullException.ThrowIfNull(project);
        return GetOrCreate(project.Id, project.GlobalVariables);
    }

    public IProjectVariableSession GetOrCreate(Guid projectId, ProjectGlobalVariableSchema schema)
    {
        lock (GetProjectGate(projectId))
        {
            if (_sessions.TryGetValue(projectId, out var session))
            {
                return session;
            }

            session = CreateSession(projectId, schema);
            _sessions[projectId] = session;
            return session;
        }
    }

    public bool TryPublishSchemaAndPersist(
        Guid projectId,
        ProjectGlobalVariableSchema schema,
        out IProjectVariableSession session,
        out string? error)
    {
        ArgumentNullException.ThrowIfNull(schema);

        lock (GetProjectGate(projectId))
        {
            if (_sessions.TryGetValue(projectId, out var current))
            {
                var currentHash = ProjectGlobalVariableSchemaValidator.ComputeSchemaHash(current.Schema);
                var nextHash = ProjectGlobalVariableSchemaValidator.ComputeSchemaHash(schema);
                if (string.Equals(currentHash, nextHash, StringComparison.Ordinal))
                {
                    session = current;
                    error = null;
                    return true;
                }

                var migrated = new ProjectVariableSession(schema, current.GetSnapshots(), current.SchemaGeneration + 1);
                try
                {
                    Save(projectId, migrated);
                    var published = migrated.CreateSnapshotClone();
                    _sessions[projectId] = published;
                    session = published;
                    error = null;
                    return true;
                }
                catch (Exception ex)
                {
                    session = current;
                    error = $"GV030: project global variable state could not be persisted: {ex.Message}";
                    return false;
                }
            }

            var candidate = CreateSession(projectId, schema);
            try
            {
                Save(projectId, candidate);
                var published = candidate.CreateSnapshotClone();
                _sessions[projectId] = published;
                session = published;
                error = null;
                return true;
            }
            catch (Exception ex)
            {
                session = candidate;
                error = $"GV030: project global variable state could not be persisted: {ex.Message}";
                return false;
            }
        }
    }

    public bool TryMutateAndPersist(
        Guid projectId,
        ProjectGlobalVariableSchema schema,
        Action<IProjectVariableSession> mutate,
        out IProjectVariableSession session,
        out string? error)
    {
        return TryMutateAndPersist(projectId, schema, mutate, expectedVersions: null, out session, out error);
    }

    public bool TryMutateAndPersist(
        Guid projectId,
        ProjectGlobalVariableSchema schema,
        Action<IProjectVariableSession> mutate,
        IReadOnlyDictionary<Guid, long>? expectedVersions,
        out IProjectVariableSession session,
        out string? error)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(mutate);

        lock (GetProjectGate(projectId))
        {
            var authoritative = _sessions.GetOrAdd(projectId, _ => CreateSession(projectId, schema));
            var authoritativeHash = ProjectGlobalVariableSchemaValidator.ComputeSchemaHash(authoritative.Schema);
            var requestedHash = ProjectGlobalVariableSchemaValidator.ComputeSchemaHash(schema);
            if (!string.Equals(authoritativeHash, requestedHash, StringComparison.Ordinal))
            {
                session = authoritative;
                error = "GV025: project global variable schema changed before this mutation could commit.";
                return false;
            }

            if (expectedVersions != null &&
                !ValidateExpectedVersions(authoritative, expectedVersions, out error))
            {
                session = authoritative;
                return false;
            }

            using var candidate = authoritative.CreateSnapshotClone();
            try
            {
                mutate(candidate);
            }
            catch (Exception ex)
            {
                session = authoritative;
                error = ex.Message;
                return false;
            }

            try
            {
                Save(projectId, candidate);
                var published = candidate.CreateSnapshotClone();
                _sessions[projectId] = published;
                session = published;
                error = null;
                return true;
            }
            catch (Exception ex)
            {
                session = authoritative;
                error = $"GV030: project global variable state could not be persisted: {ex.Message}";
                return false;
            }
        }
    }

    private static bool ValidateExpectedVersions(
        IProjectVariableSession authoritative,
        IReadOnlyDictionary<Guid, long> expectedVersions,
        out string? error)
    {
        foreach (var (variableId, expectedVersion) in expectedVersions)
        {
            if (!authoritative.TryGetSnapshot(variableId, out var current))
            {
                error = $"GV025: project global variable '{variableId}' no longer exists in the authoritative session.";
                return false;
            }

            if (current.Version != expectedVersion)
            {
                error = $"GV025: project global variable '{variableId}' changed from version {expectedVersion} to {current.Version} before this mutation could commit.";
                return false;
            }
        }

        error = null;
        return true;
    }

    public bool TryCommitAndPersist(
        Guid projectId,
        IProjectVariableSession workingSession,
        IReadOnlyDictionary<Guid, long> expectedVersions,
        out IProjectVariableSession session,
        out string? error)
    {
        ArgumentNullException.ThrowIfNull(workingSession);
        ArgumentNullException.ThrowIfNull(expectedVersions);

        lock (GetProjectGate(projectId))
        {
            if (!_sessions.TryGetValue(projectId, out var authoritative))
            {
                session = workingSession;
                error = $"GV025: project global variable session for project '{projectId}' no longer exists.";
                return false;
            }

            var authoritativeHash = ProjectGlobalVariableSchemaValidator.ComputeSchemaHash(authoritative.Schema);
            var workingHash = ProjectGlobalVariableSchemaValidator.ComputeSchemaHash(workingSession.Schema);
            if (authoritative.SchemaGeneration != workingSession.SchemaGeneration)
            {
                session = authoritative;
                error = "GV025: project global variable schema generation changed before this run could commit.";
                return false;
            }

            if (!string.Equals(authoritativeHash, workingHash, StringComparison.Ordinal))
            {
                session = authoritative;
                error = "GV025: project global variable schema changed before this run could commit.";
                return false;
            }

            using var candidate = authoritative.CreateSnapshotClone();
            if (!candidate.TryCommitFrom(workingSession, expectedVersions, out var commitError))
            {
                session = authoritative;
                error = commitError;
                return false;
            }

            try
            {
                Save(projectId, candidate);
                var published = candidate.CreateSnapshotClone();
                _sessions[projectId] = published;
                session = published;
                error = null;
                return true;
            }
            catch (Exception ex)
            {
                session = authoritative;
                error = $"GV030: project global variable state could not be persisted: {ex.Message}";
                return false;
            }
        }
    }

    private void Save(Guid projectId, IProjectVariableSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        _stateStore?.Save(ToProjectScopeId(projectId), session.Schema, session.GetSnapshots());
    }

    public bool TryRemove(Guid projectId)
    {
        lock (GetProjectGate(projectId))
        {
            return _sessions.TryRemove(projectId, out _);
        }
    }

    public void Delete(Guid projectId)
    {
        lock (GetProjectGate(projectId))
        {
            _sessions.TryRemove(projectId, out _);
            _stateStore?.Delete(ToProjectScopeId(projectId));
        }
    }

    private object GetProjectGate(Guid projectId) => _projectGates.GetOrAdd(projectId, _ => new object());

    private IProjectVariableSession CreateSession(Guid projectId, ProjectGlobalVariableSchema schema)
    {
        var snapshots = _stateStore?.Load(ToProjectScopeId(projectId), schema) ?? [];
        return new ProjectVariableSession(schema, snapshots);
    }

    public static string ToProjectScopeId(Guid projectId) => $"project:{projectId:D}";
}
