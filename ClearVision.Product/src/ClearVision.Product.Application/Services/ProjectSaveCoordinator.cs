using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ClearVision.Product.Application.DTOs;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Interfaces;
using ClearVision.Product.Core.ProjectVariables;

namespace ClearVision.Product.Application.Services;

public sealed class ProjectSaveCoordinator
{
    private const int ManifestSchemaVersion = 1;
    private const string ProjectCandidateFileName = "project-candidate.json";
    private const string FlowFileName = "flow.json";
    private const string VariableStateFileName = "variable-state.json";
    private const string ManifestFileName = "manifest.json";
    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> ProjectGates = new();
    private static readonly ConcurrentDictionary<Guid, string> RecoveryRequired = new();
    private static readonly ConcurrentDictionary<Guid, ProjectAccessState> ProjectStates = new();
    private static readonly object StartupRecoveryGate = new();
    private static TaskCompletionSource StartupRecoveryReady = CreateCompletedStartupBarrier();
    private static bool StartupRecoveryCompleted = true;
    private static bool StartupRecoveryHasRun;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    private readonly IProjectRepository _projectRepository;
    private readonly IProjectFlowStorage _flowStorage;
    private readonly ProjectVariableSessionRegistry? _projectVariableSessions;
    private readonly IProjectSaveFailureInjector? _failureInjector;
    private readonly string _transactionRoot;

    public ProjectSaveCoordinator(
        IProjectRepository projectRepository,
        IProjectFlowStorage flowStorage,
        ProjectVariableSessionRegistry? projectVariableSessions = null,
        string? transactionRoot = null,
        IProjectSaveFailureInjector? failureInjector = null)
    {
        _projectRepository = projectRepository ?? throw new ArgumentNullException(nameof(projectRepository));
        _flowStorage = flowStorage ?? throw new ArgumentNullException(nameof(flowStorage));
        _projectVariableSessions = projectVariableSessions;
        _failureInjector = failureInjector;
        _transactionRoot = string.IsNullOrWhiteSpace(transactionRoot)
            ? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "App_Data", "ProjectSaveTransactions")
            : transactionRoot;
    }

    public void EnsureProjectAvailable(Guid projectId)
    {
        EnsureStartupRecoveryReady();
        if (RecoveryRequired.TryGetValue(projectId, out var reason))
        {
            throw new InvalidOperationException($"PSV001: project '{projectId}' requires save recovery before access: {reason}");
        }
    }

    public async Task<ProjectAccessLease> AcquireProjectAccessAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        await WaitForStartupRecoveryAsync(cancellationToken);
        var gate = ProjectGates.GetOrAdd(projectId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (RecoveryRequired.TryGetValue(projectId, out var reason))
            {
                throw new InvalidOperationException($"PSV001: project '{projectId}' requires save recovery before access: {reason}");
            }

            return new ProjectAccessLease(gate);
        }
        catch
        {
            gate.Release();
            throw;
        }
    }

    public async Task<ProjectSaveResult> SaveExistingProjectAsync(ProjectSaveRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        await WaitForStartupRecoveryAsync();

        var gate = ProjectGates.GetOrAdd(request.Project.Id, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        try
        {
            EnsureProjectAvailable(request.Project.Id);
            ProjectStates[request.Project.Id] = ProjectAccessState.Saving;
            return await SaveExistingProjectUnderGateAsync(request);
        }
        finally
        {
            if (RecoveryRequired.ContainsKey(request.Project.Id))
            {
                ProjectStates[request.Project.Id] = ProjectAccessState.RecoveryRequired;
            }
            else
            {
                ProjectStates[request.Project.Id] = ProjectAccessState.Idle;
            }

            gate.Release();
        }
    }

    public async Task RunStartupRecoveryAsync(CancellationToken cancellationToken = default)
    {
        if (!BeginStartupRecovery())
        {
            await WaitForStartupRecoveryAsync(cancellationToken);
            return;
        }

        try
        {
            await RecoverAllAsync();
            CompleteStartupRecovery(null);
        }
        catch (Exception ex)
        {
            CompleteStartupRecovery(ex);
            throw;
        }
    }

    public async Task RecoverAllAsync()
    {
        if (!Directory.Exists(_transactionRoot))
        {
            return;
        }

        foreach (var projectDirectory in Directory.EnumerateDirectories(_transactionRoot))
        {
            foreach (var saveDirectory in Directory.EnumerateDirectories(projectDirectory))
            {
                await RecoverDirectoryAsync(saveDirectory);
            }
        }
    }

    private async Task<ProjectSaveResult> SaveExistingProjectUnderGateAsync(ProjectSaveRequest request)
    {
        var project = await _projectRepository.GetByIdAsync(request.Project.Id)
            ?? throw new InvalidOperationException($"PSV002: project '{request.Project.Id}' does not exist.");
        if (project.PersistenceRevision != request.ExpectedRevision)
        {
            throw new InvalidOperationException(
                $"PSV011: stale project save request. Expected={request.ExpectedRevision}, Current={project.PersistenceRevision}.");
        }

        var previousFlowJson = await _flowStorage.LoadFlowJsonAsync(project.Id);
        var previousSchema = project.GlobalVariables;
        var previousSchemaHash = ProjectGlobalVariableSchemaValidator.ComputeSchemaHash(previousSchema);
        var nextSchemaHash = ProjectGlobalVariableSchemaValidator.ComputeSchemaHash(request.NextSchema);
        var schemaChanged = !string.Equals(previousSchemaHash, nextSchemaHash, StringComparison.Ordinal);
        var metadataChanged =
            !string.Equals(project.Name, request.Name, StringComparison.Ordinal) ||
            !string.Equals(project.Description, request.Description, StringComparison.Ordinal);
        var flowChanged = request.NextFlowJson != null &&
            !string.Equals(previousFlowJson, request.NextFlowJson, StringComparison.Ordinal);
        var variableCandidate = schemaChanged && _projectVariableSessions != null && SchemaHasVariableState(previousSchema, request.NextSchema)
            ? _projectVariableSessions.BuildSchemaMigrationCandidate(project.Id, previousSchema, request.NextSchema)
            : null;

        if (!metadataChanged && !schemaChanged && !flowChanged)
        {
            return new ProjectSaveResult(project, request.NextFlow, Changed: false);
        }

        var fromRevision = project.PersistenceRevision;
        var toRevision = fromRevision + 1;
        var saveId = Guid.NewGuid();
        var saveDirectory = GetSaveDirectory(project.Id, saveId);
        Directory.CreateDirectory(saveDirectory);
        var participants = BuildParticipants(flowChanged, variableCandidate != null);

        var projectCandidate = ProjectCandidate.From(project, request, fromRevision, toRevision);
        var projectBytes = SerializeToBytes(projectCandidate);
        WriteArtifact(saveDirectory, ProjectCandidateFileName, projectBytes);

        byte[]? flowBytes = null;
        if (flowChanged && request.NextFlowJson != null)
        {
            flowBytes = new UTF8Encoding(false).GetBytes(request.NextFlowJson);
            WriteArtifact(saveDirectory, FlowFileName, flowBytes);
        }

        byte[]? variableBytes = null;
        if (variableCandidate != null)
        {
            variableBytes = SerializeToBytes(VariableStateCandidate.From(variableCandidate));
            WriteArtifact(saveDirectory, VariableStateFileName, variableBytes);
        }

        var artifacts = new List<ProjectSaveArtifact>
        {
            ProjectSaveArtifact.From(ProjectCandidateFileName, fromRevision, toRevision, projectBytes)
        };
        if (flowBytes != null)
        {
            artifacts.Add(ProjectSaveArtifact.From(FlowFileName, fromRevision, toRevision, flowBytes));
        }

        if (variableBytes != null)
        {
            artifacts.Add(ProjectSaveArtifact.From(VariableStateFileName, fromRevision, toRevision, variableBytes));
        }

        var manifest = ProjectSaveManifest.Create(
            saveId,
            project.Id,
            fromRevision,
            toRevision,
            participants,
            ProjectSavePhase.Prepared,
            artifacts);

        try
        {
            WriteManifestAtomic(saveDirectory, manifest);
            await InjectFailureAsync(ProjectSaveFailurePoint.AfterPrepared, manifest);
            ValidateStagedArtifacts(saveDirectory, manifest);
        }
        catch
        {
            TryDeleteSaveDirectory(saveDirectory);
            throw;
        }

        manifest = manifest with { Phase = ProjectSavePhase.CommitIntended };
        WriteManifestAtomic(saveDirectory, manifest);

        Exception? originalException = null;
        try
        {
            await InjectFailureAsync(ProjectSaveFailurePoint.AfterCommitIntent, manifest);
            await ApplyCommittedIntentAsync(saveDirectory, manifest);
        }
        catch (Exception ex)
        {
            originalException = ex;
            try
            {
                ValidateStagedArtifacts(saveDirectory, manifest);
                await ApplyCommittedIntentAsync(saveDirectory, manifest);
                RecoveryRequired.TryRemove(project.Id, out _);
            }
            catch (Exception recoveryEx)
            {
                RecoveryRequired[project.Id] = recoveryEx.Message;
                throw new InvalidOperationException(
                    $"PSV012: post-intent recovery failed. Original={originalException.Message}; Recovery={recoveryEx.Message}",
                    recoveryEx);
            }

            if (RecoveryRequired.ContainsKey(project.Id))
            {
                throw;
            }
        }

        project.SetPersistenceRevision(toRevision);
        return new ProjectSaveResult(project, request.NextFlow, Changed: true);
    }

    private async Task RecoverDirectoryAsync(string saveDirectory)
    {
        var manifestPath = Path.Combine(saveDirectory, ManifestFileName);
        if (!File.Exists(manifestPath))
        {
            return;
        }

        var manifest = DeserializeFile<ProjectSaveManifest>(manifestPath);
        var gate = ProjectGates.GetOrAdd(manifest.ProjectId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        try
        {
            ProjectStates[manifest.ProjectId] = ProjectAccessState.Recovering;
            if (manifest.Phase == ProjectSavePhase.Prepared)
            {
                Directory.Delete(saveDirectory, recursive: true);
                return;
            }

            if (manifest.Phase == ProjectSavePhase.Completed)
            {
                Directory.Delete(saveDirectory, recursive: true);
                RecoveryRequired.TryRemove(manifest.ProjectId, out _);
                return;
            }

            ValidateStagedArtifacts(saveDirectory, manifest);
            await ApplyCommittedIntentAsync(saveDirectory, manifest);
            RecoveryRequired.TryRemove(manifest.ProjectId, out _);
        }
        catch (Exception ex)
        {
            RecoveryRequired[manifest.ProjectId] = ex.Message;
            throw;
        }
        finally
        {
            if (RecoveryRequired.ContainsKey(manifest.ProjectId))
            {
                ProjectStates[manifest.ProjectId] = ProjectAccessState.RecoveryRequired;
            }
            else
            {
                ProjectStates[manifest.ProjectId] = ProjectAccessState.Idle;
            }

            gate.Release();
        }
    }

    private async Task ApplyCommittedIntentAsync(string saveDirectory, ProjectSaveManifest manifest)
    {
        var projectCandidate = DeserializeFile<ProjectCandidate>(Path.Combine(saveDirectory, ProjectCandidateFileName));
        var project = await _projectRepository.GetByIdAsync(manifest.ProjectId)
            ?? throw new InvalidOperationException($"PSV002: project '{manifest.ProjectId}' does not exist.");

        await InjectFailureAsync(ProjectSaveFailurePoint.BeforeProjectApply, manifest);
        if (project.PersistenceRevision == manifest.FromRevision)
        {
            var originalName = project.Name;
            var originalDescription = project.Description;
            var originalGlobalVariables = project.GlobalVariables;
            var originalRevision = project.PersistenceRevision;
            try
            {
                project.UpdateInfo(projectCandidate.Name, projectCandidate.Description);
                project.UpdateGlobalVariables(projectCandidate.GlobalVariables);
                project.SetPersistenceRevision(manifest.ToRevision);
                await _projectRepository.UpdateAsync(project);
            }
            catch
            {
                project.UpdateInfo(originalName, originalDescription);
                project.UpdateGlobalVariables(originalGlobalVariables);
                project.SetPersistenceRevision(originalRevision);
                throw;
            }
        }
        else if (project.PersistenceRevision == manifest.ToRevision)
        {
            VerifyProjectAtCandidate(project, projectCandidate);
        }
        else
        {
            throw new InvalidOperationException(
                $"PSV003: project revision conflict. Current={project.PersistenceRevision}, From={manifest.FromRevision}, To={manifest.ToRevision}.");
        }

        await InjectFailureAsync(ProjectSaveFailurePoint.AfterProjectApply, manifest);
        if (manifest.Participants.Contains(ProjectSaveParticipant.Flow))
        {
            var flowArtifact = manifest.Artifacts.Single(item => item.Name == FlowFileName);
            var flowJson = await File.ReadAllTextAsync(Path.Combine(saveDirectory, FlowFileName), Encoding.UTF8);
            var metadata = await _flowStorage.LoadMetadataAsync(manifest.ProjectId);
            if (metadata?.PersistenceRevision == manifest.ToRevision)
            {
                if (!string.Equals(metadata.FlowHash, flowArtifact.Sha256, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("PSV004: flow artifact hash mismatch at target revision.");
                }
            }
            else if (metadata == null ||
                metadata.PersistenceRevision == manifest.FromRevision ||
                (metadata.PersistenceRevision == 0 && manifest.FromRevision == 0))
            {
                await _flowStorage.SaveFlowJsonAsync(manifest.ProjectId, flowJson, manifest.ToRevision);
                metadata = await _flowStorage.LoadMetadataAsync(manifest.ProjectId);
                if (metadata?.PersistenceRevision != manifest.ToRevision ||
                    !string.Equals(metadata.FlowHash, flowArtifact.Sha256, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("PSV005: flow apply verification failed.");
                }
            }
            else
            {
                throw new InvalidOperationException(
                    $"PSV006: flow revision conflict. Current={metadata.PersistenceRevision}, From={manifest.FromRevision}, To={manifest.ToRevision}.");
            }

            await InjectFailureAsync(ProjectSaveFailurePoint.AfterFlowApply, manifest);
        }

        if (manifest.Participants.Contains(ProjectSaveParticipant.VariableState))
        {
            var variableState = DeserializeFile<VariableStateCandidate>(Path.Combine(saveDirectory, VariableStateFileName));
            var migrationCandidate = variableState.ToMigrationCandidate();
            string? persistError = null;
            if (_projectVariableSessions == null)
            {
                throw new InvalidOperationException("PSV007: project variable state persistence is unavailable.");
            }

            var metadata = _projectVariableSessions.LoadStateMetadata(manifest.ProjectId);
            if (metadata?.PersistenceRevision == manifest.ToRevision)
            {
                if (!string.Equals(metadata.StateHash, variableState.StateHash, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("PSV013: variable state hash mismatch at target revision.");
                }
            }
            else if (metadata == null ||
                metadata.PersistenceRevision == manifest.FromRevision ||
                (metadata.PersistenceRevision == 0 && manifest.FromRevision == 0))
            {
                if (!_projectVariableSessions.TryPersistMigrationCandidate(
                        manifest.ProjectId,
                        migrationCandidate,
                        manifest.ToRevision,
                        manifest.SaveId,
                        variableState.StateHash,
                        out _,
                        out persistError))
                {
                    throw new InvalidOperationException(persistError ?? "PSV007: project variable state persistence is unavailable.");
                }
            }
            else
            {
                throw new InvalidOperationException(
                    $"PSV014: variable state revision conflict. Current={metadata.PersistenceRevision}, From={manifest.FromRevision}, To={manifest.ToRevision}.");
            }

            await InjectFailureAsync(ProjectSaveFailurePoint.AfterVariableStateApply, manifest);
        }

        var completed = manifest with { Phase = ProjectSavePhase.Completed };
        await InjectFailureAsync(ProjectSaveFailurePoint.BeforeComplete, manifest);
        WriteManifestAtomic(saveDirectory, completed);
        Directory.Delete(saveDirectory, recursive: true);
    }

    private Task InjectFailureAsync(ProjectSaveFailurePoint point, ProjectSaveManifest manifest) =>
        _failureInjector?.OnPointAsync(point, manifest) ?? Task.CompletedTask;

    private static void TryDeleteSaveDirectory(string saveDirectory)
    {
        try
        {
            if (Directory.Exists(saveDirectory))
            {
                Directory.Delete(saveDirectory, recursive: true);
            }
        }
        catch
        {
            // Preserve the original pre-commit failure; prepared-only journals are still discardable on recovery.
        }
    }

    private static async Task WaitForStartupRecoveryAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await StartupRecoveryReady.Task.WaitAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new InvalidOperationException($"PSV015: project save startup recovery did not complete: {ex.Message}", ex);
        }
    }

    private static void EnsureStartupRecoveryReady()
    {
        if (StartupRecoveryReady.Task.IsCompletedSuccessfully)
        {
            return;
        }

        if (StartupRecoveryReady.Task.IsFaulted)
        {
            throw new InvalidOperationException(
                $"PSV015: project save startup recovery did not complete: {StartupRecoveryReady.Task.Exception?.GetBaseException().Message}");
        }

        StartupRecoveryReady.Task.GetAwaiter().GetResult();
    }

    private static bool BeginStartupRecovery()
    {
        lock (StartupRecoveryGate)
        {
            if (!StartupRecoveryCompleted)
            {
                return false;
            }

            if (StartupRecoveryHasRun)
            {
                return false;
            }

            StartupRecoveryCompleted = false;
            StartupRecoveryReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            return true;
        }
    }

    private static void CompleteStartupRecovery(Exception? exception)
    {
        lock (StartupRecoveryGate)
        {
            StartupRecoveryCompleted = true;
            StartupRecoveryHasRun = true;
            if (exception == null)
            {
                StartupRecoveryReady.TrySetResult();
            }
            else
            {
                StartupRecoveryReady.TrySetException(exception);
            }
        }
    }

    private static TaskCompletionSource CreateCompletedStartupBarrier()
    {
        var source = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        source.SetResult();
        return source;
    }

    internal static void ResetStaticStateForTests()
    {
        ProjectGates.Clear();
        RecoveryRequired.Clear();
        ProjectStates.Clear();
        lock (StartupRecoveryGate)
        {
            StartupRecoveryReady = CreateCompletedStartupBarrier();
            StartupRecoveryCompleted = true;
            StartupRecoveryHasRun = false;
        }
    }

    private static void VerifyProjectAtCandidate(Project project, ProjectCandidate candidate)
    {
        var currentHash = ProjectGlobalVariableSchemaValidator.ComputeSchemaHash(project.GlobalVariables);
        var candidateHash = ProjectGlobalVariableSchemaValidator.ComputeSchemaHash(candidate.GlobalVariables);
        if (!string.Equals(project.Name, candidate.Name, StringComparison.Ordinal) ||
            !string.Equals(project.Description, candidate.Description, StringComparison.Ordinal) ||
            !string.Equals(currentHash, candidateHash, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("PSV008: project candidate does not match target revision.");
        }
    }

    private static bool SchemaHasVariableState(ProjectGlobalVariableSchema previousSchema, ProjectGlobalVariableSchema nextSchema) =>
        previousSchema.Variables.Count > 0 || nextSchema.Variables.Count > 0;

    private static IReadOnlyList<ProjectSaveParticipant> BuildParticipants(bool flowChanged, bool variableStateChanged)
    {
        var participants = new List<ProjectSaveParticipant> { ProjectSaveParticipant.Project };
        if (flowChanged)
        {
            participants.Add(ProjectSaveParticipant.Flow);
        }

        if (variableStateChanged)
        {
            participants.Add(ProjectSaveParticipant.VariableState);
        }

        return participants;
    }

    private string GetSaveDirectory(Guid projectId, Guid saveId) =>
        Path.Combine(_transactionRoot, projectId.ToString("D"), saveId.ToString("D"));

    private static void ValidateStagedArtifacts(string saveDirectory, ProjectSaveManifest manifest)
    {
        foreach (var artifact in manifest.Artifacts)
        {
            var path = Path.Combine(saveDirectory, artifact.Name);
            if (!File.Exists(path))
            {
                throw new InvalidOperationException($"PSV009: staged artifact '{artifact.Name}' is missing.");
            }

            var bytes = File.ReadAllBytes(path);
            if (bytes.LongLength != artifact.Length ||
                !string.Equals(ComputeSha256(bytes), artifact.Sha256, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"PSV010: staged artifact '{artifact.Name}' failed integrity validation.");
            }
        }
    }

    private static void WriteArtifact(string saveDirectory, string name, byte[] bytes)
    {
        var path = Path.Combine(saveDirectory, name);
        File.WriteAllBytes(path, bytes);
        var readBack = File.ReadAllBytes(path);
        if (!readBack.SequenceEqual(bytes))
        {
            throw new IOException($"Staged artifact '{name}' did not round-trip.");
        }
    }

    private static void WriteManifestAtomic(string saveDirectory, ProjectSaveManifest manifest)
    {
        var path = Path.Combine(saveDirectory, ManifestFileName);
        var tempPath = path + ".tmp";
        File.WriteAllBytes(tempPath, SerializeToBytes(manifest));
        File.Move(tempPath, path, overwrite: true);
    }

    private static byte[] SerializeToBytes<T>(T value) => JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions);

    private static T DeserializeFile<T>(string path) =>
        JsonSerializer.Deserialize<T>(File.ReadAllText(path, Encoding.UTF8), JsonOptions)
        ?? throw new InvalidOperationException($"Unable to deserialize '{path}'.");

    private static string ComputeSha256(byte[] bytes)
    {
        var hash = SHA256.HashData(bytes);
        return "sha256:" + Convert.ToHexString(hash).ToLowerInvariant();
    }
}

public sealed record ProjectSaveRequest(
    Project Project,
    long ExpectedRevision,
    string Name,
    string? Description,
    ProjectGlobalVariableSchema PreviousSchema,
    ProjectGlobalVariableSchema NextSchema,
    string? PreviousFlowJson,
    OperatorFlowDto? NextFlow,
    string? NextFlowJson);

public sealed record ProjectSaveResult(Project Project, OperatorFlowDto? Flow, bool Changed);

public sealed record ProjectSaveManifest(
    int SchemaVersion,
    Guid SaveId,
    Guid ProjectId,
    long FromRevision,
    long ToRevision,
    IReadOnlyList<ProjectSaveParticipant> Participants,
    IReadOnlyList<ProjectSaveArtifact> Artifacts,
    ProjectSavePhase Phase,
    DateTimeOffset CreatedAtUtc)
{
    public static ProjectSaveManifest Create(
        Guid saveId,
        Guid projectId,
        long fromRevision,
        long toRevision,
        IReadOnlyList<ProjectSaveParticipant> participants,
        ProjectSavePhase phase,
        IReadOnlyList<ProjectSaveArtifact> artifacts)
    {
        return new ProjectSaveManifest(
            1,
            saveId,
            projectId,
            fromRevision,
            toRevision,
            participants,
            artifacts,
            phase,
            DateTimeOffset.UtcNow);
    }
}

public sealed record ProjectSaveArtifact(
    string Name,
    long FromRevision,
    long ToRevision,
    long Length,
    string Sha256)
{
    public static ProjectSaveArtifact From(string name, long fromRevision, long toRevision, byte[] bytes) =>
        new(name, fromRevision, toRevision, bytes.LongLength, ComputeSha256(bytes));

    private static string ComputeSha256(byte[] bytes)
    {
        var hash = SHA256.HashData(bytes);
        return "sha256:" + Convert.ToHexString(hash).ToLowerInvariant();
    }
}

public enum ProjectSaveParticipant
{
    Project = 0,
    Flow = 1,
    VariableState = 2
}

public enum ProjectSavePhase
{
    Prepared = 0,
    CommitIntended = 1,
    Completed = 2
}

public enum ProjectAccessState
{
    Idle = 0,
    Saving = 1,
    Recovering = 2,
    RecoveryRequired = 3
}

public sealed class ProjectAccessLease : IAsyncDisposable, IDisposable
{
    private readonly SemaphoreSlim _gate;
    private bool _disposed;

    internal ProjectAccessLease(SemaphoreSlim gate)
    {
        _gate = gate;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _gate.Release();
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}

public enum ProjectSaveFailurePoint
{
    AfterPrepared = 0,
    AfterCommitIntent = 1,
    BeforeProjectApply = 2,
    AfterProjectApply = 3,
    AfterFlowApply = 4,
    AfterVariableStateApply = 5,
    BeforeComplete = 6
}

public interface IProjectSaveFailureInjector
{
    Task OnPointAsync(ProjectSaveFailurePoint point, ProjectSaveManifest manifest);
}

public sealed record ProjectCandidate(
    Guid ProjectId,
    string Name,
    string? Description,
    string Version,
    Dictionary<string, string> GlobalSettings,
    ProjectGlobalVariableSchema GlobalVariables,
    long FromRevision,
    long ToRevision)
{
    public static ProjectCandidate From(Project project, ProjectSaveRequest request, long fromRevision, long toRevision) =>
        new(
            project.Id,
            request.Name,
            request.Description,
            project.Version,
            new Dictionary<string, string>(project.GlobalSettings),
            request.NextSchema,
            fromRevision,
            toRevision);
}

public sealed record VariableStateCandidate(
    ProjectGlobalVariableSchema Schema,
    IReadOnlyList<ProjectVariableValueSnapshot> Snapshots,
    string SchemaHash,
    string StateHash,
    long SchemaGeneration,
    bool SessionWasLoaded)
{
    public static VariableStateCandidate From(ProjectVariableSessionMigrationCandidate candidate) =>
        new(
            candidate.Schema,
            candidate.Snapshots,
            ProjectGlobalVariableSchemaValidator.ComputeSchemaHash(candidate.Schema),
            ProjectVariableStateHash.Compute(candidate.Schema, candidate.Snapshots),
            candidate.SchemaGeneration,
            candidate.SessionWasLoaded);

    public ProjectVariableSessionMigrationCandidate ToMigrationCandidate() =>
        new(Schema, Snapshots, SchemaGeneration, SessionWasLoaded);
}
