using System.Collections.Concurrent;
using ClearVision.Product.Application.DTOs;
using ClearVision.Product.Core.ProjectVariables;

namespace ClearVision.Product.Application.Services;

public sealed class ProjectVariableSessionRegistry
{
    private readonly ConcurrentDictionary<Guid, IProjectVariableSession> _sessions = new();

    public IProjectVariableSession GetOrCreate(ProjectDto project)
    {
        ArgumentNullException.ThrowIfNull(project);
        return _sessions.GetOrAdd(project.Id, _ => new ProjectVariableSession(project.GlobalVariables));
    }

    public IProjectVariableSession GetOrCreate(Guid projectId, ProjectGlobalVariableSchema schema)
    {
        return _sessions.GetOrAdd(projectId, _ => new ProjectVariableSession(schema));
    }

    public IProjectVariableSession Replace(Guid projectId, ProjectGlobalVariableSchema schema)
    {
        var session = new ProjectVariableSession(schema);
        _sessions.AddOrUpdate(projectId, session, (_, _) => session);
        return session;
    }

    public bool TryRemove(Guid projectId)
    {
        return _sessions.TryRemove(projectId, out _);
    }
}
