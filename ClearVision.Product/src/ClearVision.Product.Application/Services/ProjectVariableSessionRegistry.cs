using System.Collections.Concurrent;
using ClearVision.Product.Application.DTOs;
using ClearVision.Product.Core.ProjectVariables;

namespace ClearVision.Product.Application.Services;

public sealed class ProjectVariableSessionRegistry
{
    private readonly ConcurrentDictionary<Guid, IProjectVariableSession> _sessions = new();
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
        return _sessions.GetOrAdd(projectId, _ => CreateSession(projectId, schema));
    }

    public IProjectVariableSession Replace(Guid projectId, ProjectGlobalVariableSchema schema)
    {
        var session = new ProjectVariableSession(schema);
        _sessions.AddOrUpdate(projectId, session, (_, _) => session);
        Save(projectId, session);
        return session;
    }

    public void Save(Guid projectId)
    {
        if (_sessions.TryGetValue(projectId, out var session))
        {
            Save(projectId, session);
        }
    }

    public void Save(Guid projectId, IProjectVariableSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        _stateStore?.Save(ToProjectScopeId(projectId), session.Schema, session.GetSnapshots());
    }

    public bool TryRemove(Guid projectId)
    {
        return _sessions.TryRemove(projectId, out _);
    }

    private IProjectVariableSession CreateSession(Guid projectId, ProjectGlobalVariableSchema schema)
    {
        var snapshots = _stateStore?.Load(ToProjectScopeId(projectId), schema) ?? [];
        return new ProjectVariableSession(schema, snapshots);
    }

    public static string ToProjectScopeId(Guid projectId) => $"project:{projectId:D}";
}
