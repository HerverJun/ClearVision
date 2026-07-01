using System.Threading;

namespace ClearVision.Product.Core.ProjectVariables;

public sealed class ProjectVariableExecutionContext
{
    public ProjectVariableExecutionContext(
        IProjectVariableSession session,
        ProjectVariableBindingIndex bindingIndex,
        Guid runId,
        bool isPreview = false,
        ProjectVariableCommitHandler? commitHandler = null)
    {
        Session = session ?? throw new ArgumentNullException(nameof(session));
        BindingIndex = bindingIndex ?? throw new ArgumentNullException(nameof(bindingIndex));
        RunId = runId;
        IsPreview = isPreview;
        CommitHandler = commitHandler;
    }

    public IProjectVariableSession Session { get; }

    public ProjectVariableBindingIndex BindingIndex { get; }

    public Guid RunId { get; }

    public bool IsPreview { get; }

    public ProjectVariableCommitHandler? CommitHandler { get; }
}

public delegate ProjectVariableCommitResult ProjectVariableCommitHandler(
    IProjectVariableSession workingSession,
    IReadOnlyDictionary<Guid, long> expectedVersions);

public sealed record ProjectVariableCommitResult(bool Succeeded, string? Error)
{
    public static ProjectVariableCommitResult Success() => new(true, null);

    public static ProjectVariableCommitResult Failure(string? error) => new(false, error);
}

public interface IProjectVariableExecutionContextAccessor
{
    ProjectVariableExecutionContext? Current { get; }

    IDisposable BeginScope(ProjectVariableExecutionContext context);
}

public sealed class ProjectVariableExecutionContextAccessor : IProjectVariableExecutionContextAccessor
{
    private readonly AsyncLocal<ProjectVariableExecutionContext?> _current = new();

    public ProjectVariableExecutionContext? Current => _current.Value;

    public IDisposable BeginScope(ProjectVariableExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var previous = _current.Value;
        _current.Value = context;
        return new Scope(this, previous);
    }

    private sealed class Scope : IDisposable
    {
        private readonly ProjectVariableExecutionContextAccessor _owner;
        private readonly ProjectVariableExecutionContext? _previous;
        private bool _disposed;

        public Scope(ProjectVariableExecutionContextAccessor owner, ProjectVariableExecutionContext? previous)
        {
            _owner = owner;
            _previous = previous;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _owner._current.Value = _previous;
            _disposed = true;
        }
    }
}
