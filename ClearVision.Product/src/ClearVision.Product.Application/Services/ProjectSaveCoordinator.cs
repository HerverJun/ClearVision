using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ClearVision.Product.Application.DTOs;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Exceptions;
using ClearVision.Product.Core.Interfaces;
using ClearVision.Product.Core.ProjectVariables;
using ClearVision.Product.Core.Services;

namespace ClearVision.Product.Application.Services;

public sealed class ProjectSaveCoordinator
{
    private const int ManifestSchemaVersion = 1;
    private const string ProjectCandidateFileName = "project-candidate.json";
    private const string FlowFileName = "flow.json";
    private const string VariableStateFileName = "variable-state.json";
    private const string ProjectAssetsFileName = "project-assets.json";
    private const string ManifestFileName = "manifest.json";
    private static readonly ConcurrentDictionary<Guid, ProjectGate> ProjectGates = new();
    private static readonly ConcurrentDictionary<Guid, string> RecoveryRequired = new();
    private static readonly ConcurrentDictionary<Guid, byte> HiddenCreates = new();
    private static readonly ConcurrentDictionary<Guid, ProjectAccessState> ProjectStates = new();
    private static readonly SemaphoreSlim CreateGate = new(1, 1);
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
    private readonly IProjectAssetStorage? _projectAssetStorage;
    private readonly string _transactionRoot;

    public ProjectSaveCoordinator(
        IProjectRepository projectRepository,
        IProjectFlowStorage flowStorage,
        ProjectVariableSessionRegistry? projectVariableSessions = null,
        string? transactionRoot = null,
        IProjectSaveFailureInjector? failureInjector = null,
        IProjectAssetStorage? projectAssetStorage = null)
    {
        _projectRepository = projectRepository ?? throw new ArgumentNullException(nameof(projectRepository));
        _flowStorage = flowStorage ?? throw new ArgumentNullException(nameof(flowStorage));
        _projectVariableSessions = projectVariableSessions;
        _failureInjector = failureInjector;
        _projectAssetStorage = projectAssetStorage;
        _transactionRoot = string.IsNullOrWhiteSpace(transactionRoot)
            ? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "App_Data", "ProjectSaveTransactions")
            : transactionRoot;
    }

    public void EnsureProjectAvailable(Guid projectId)
    {
        EnsureStartupRecoveryReady();
        if (HiddenCreates.ContainsKey(projectId))
        {
            throw new ProjectNotFoundException(projectId);
        }

        if (RecoveryRequired.TryGetValue(projectId, out var reason))
        {
            throw new InvalidOperationException($"PSV001: project '{projectId}' requires save recovery before access: {reason}");
        }
    }

    public async Task<ProjectAccessLease> AcquireProjectAccessAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        await WaitForStartupRecoveryAsync(cancellationToken);
        var gate = await RentProjectGateAsync(projectId, cancellationToken);
        var acquired = false;
        try
        {
            await gate.WaitAsync(cancellationToken);
            acquired = true;
            if (HiddenCreates.ContainsKey(projectId))
            {
                throw new ProjectNotFoundException(projectId);
            }

            if (RecoveryRequired.TryGetValue(projectId, out var reason))
            {
                throw new InvalidOperationException($"PSV001: project '{projectId}' requires save recovery before access: {reason}");
            }

            return new ProjectAccessLease(projectId, () => ReleaseAcquiredProjectGate(projectId, gate));
        }
        catch
        {
            if (acquired)
            {
                gate.Release();
            }

            ReleaseProjectGateReference(projectId, gate);
            throw;
        }
    }

    public void ClearProjectState(Guid projectId)
    {
        if (ProjectStates.TryGetValue(projectId, out var state) &&
            (state == ProjectAccessState.Saving || state == ProjectAccessState.Recovering))
        {
            return;
        }

        RecoveryRequired.TryRemove(projectId, out _);
        ProjectStates.TryRemove(projectId, out _);
        RequestProjectGateCleanup(projectId);
    }

    public async Task<ProjectSaveResult> SaveExistingProjectAsync(ProjectSaveRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        await using var access = await AcquireProjectAccessAsync(request.Project.Id);
        return await SaveExistingProjectUnderProjectAccessAsync(access, request);
    }

    public async Task<ProjectSaveResult> SaveExistingProjectUnderProjectAccessAsync(
        ProjectAccessLease projectAccess,
        ProjectSaveRequest request)
    {
        ArgumentNullException.ThrowIfNull(projectAccess);
        ArgumentNullException.ThrowIfNull(request);
        projectAccess.EnsureActiveFor(request.Project.Id);
        EnsureProjectAvailable(request.Project.Id);
        ProjectStates[request.Project.Id] = ProjectAccessState.Saving;
        try
        {
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
        }
    }

    public async Task<ProjectSaveResult> CreateProjectAsync(
        ProjectCreateSaveRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        await WaitForStartupRecoveryAsync(cancellationToken);
        await CreateGate.WaitAsync(cancellationToken);
        try
        {
            var duplicate = await _projectRepository.GetByNameAsync(request.Project.Name);
            if (duplicate != null)
            {
                throw new InvalidOperationException("PSV027: a project with the same name already exists.");
            }

            var gate = await RentProjectGateAsync(request.Project.Id, cancellationToken);
            await gate.WaitAsync(cancellationToken);
            try
            {
                EnsureProjectAvailable(request.Project.Id);
                ProjectStates[request.Project.Id] = ProjectAccessState.Saving;
                return await CreateProjectUnderGateAsync(request);
            }
            finally
            {
                ProjectStates[request.Project.Id] = RecoveryRequired.ContainsKey(request.Project.Id)
                    ? ProjectAccessState.RecoveryRequired
                    : ProjectAccessState.Idle;
                gate.Release();
                ReleaseProjectGateReference(request.Project.Id, gate);
            }
        }
        finally
        {
            CreateGate.Release();
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

    public async Task<ProjectSaveRecoverySummary> RecoverAllAsync()
    {
        var recoveredCount = 0;
        var discardedPreparedCount = 0;
        var recoveryRequiredProjectIds = new HashSet<Guid>();
        var failures = new List<ProjectSaveRecoveryFailure>();
        if (!Directory.Exists(_transactionRoot))
        {
            return new ProjectSaveRecoverySummary(
                recoveredCount,
                discardedPreparedCount,
                recoveryRequiredProjectIds.ToList(),
                failures,
                SystemFailure: null);
        }

        IReadOnlyList<string> projectDirectories;
        try
        {
            projectDirectories = Directory.EnumerateDirectories(_transactionRoot).ToList();
        }
        catch (Exception ex)
        {
            var summary = new ProjectSaveRecoverySummary(
                recoveredCount,
                discardedPreparedCount,
                recoveryRequiredProjectIds.ToList(),
                failures,
                SystemFailure: ex.Message);
            throw new ProjectSaveRecoverySystemException("PSV015: project save transaction root could not be enumerated.", summary, ex);
        }

        foreach (var projectDirectory in projectDirectories)
        {
            IReadOnlyList<string> saveDirectories;
            try
            {
                saveDirectories = Directory.EnumerateDirectories(projectDirectory).ToList();
            }
            catch (Exception ex)
            {
                var summary = new ProjectSaveRecoverySummary(
                    recoveredCount,
                    discardedPreparedCount,
                    recoveryRequiredProjectIds.ToList(),
                    failures,
                    SystemFailure: ex.Message);
                throw new ProjectSaveRecoverySystemException("PSV015: project save transaction directory could not be enumerated.", summary, ex);
            }

            var projectFailed = false;
            foreach (var saveDirectory in saveDirectories)
            {
                if (projectFailed)
                {
                    break;
                }

                try
                {
                    var result = await RecoverDirectoryAsync(saveDirectory);
                    recoveredCount += result.RecoveredCount;
                    discardedPreparedCount += result.DiscardedPreparedCount;
                }
                catch (ProjectSaveRecoveryProjectException ex)
                {
                    recoveryRequiredProjectIds.Add(ex.ProjectId);
                    failures.Add(new ProjectSaveRecoveryFailure(
                        ex.ProjectId,
                        ex.SaveId,
                        ex.Phase,
                        ex.InnerException?.Message ?? ex.Message));
                    projectFailed = true;
                }
            }
        }

        return new ProjectSaveRecoverySummary(
            recoveredCount,
            discardedPreparedCount,
            recoveryRequiredProjectIds.ToList(),
            failures,
            SystemFailure: null);
    }

    private async Task<ProjectSaveResult> CreateProjectUnderGateAsync(ProjectCreateSaveRequest request)
    {
        if (await _projectRepository.GetByIdForUpdateAsync(request.Project.Id) != null)
        {
            throw new InvalidOperationException($"PSV027: project '{request.Project.Id}' already exists.");
        }

        const long fromRevision = -1;
        const long toRevision = 0;
        var saveId = Guid.NewGuid();
        var saveDirectory = GetSaveDirectory(request.Project.Id, saveId);
        Directory.CreateDirectory(saveDirectory);

        var variableCandidate = request.Project.GlobalVariables.Variables.Count > 0 && _projectVariableSessions != null
            ? _projectVariableSessions.BuildSchemaMigrationCandidate(
                request.Project.Id,
                new ProjectGlobalVariableSchema(),
                request.Project.GlobalVariables)
            : null;
        var participants = BuildCreateParticipants(
            request.FlowJson != null,
            variableCandidate != null,
            request.Assets != null);
        var projectCandidate = ProjectCandidate.FromNew(request.Project);
        var artifacts = new List<ProjectSaveArtifact>();
        ProjectSaveManifest manifest;

        try
        {
            var projectBytes = SerializeToBytes(projectCandidate);
            WriteArtifact(saveDirectory, ProjectCandidateFileName, projectBytes);
            artifacts.Add(ProjectSaveArtifact.From(ProjectCandidateFileName, fromRevision, toRevision, projectBytes));

            if (request.FlowJson != null)
            {
                var flowBytes = new UTF8Encoding(false).GetBytes(request.FlowJson);
                WriteArtifact(saveDirectory, FlowFileName, flowBytes);
                artifacts.Add(ProjectSaveArtifact.From(FlowFileName, fromRevision, toRevision, flowBytes));
            }

            if (variableCandidate != null)
            {
                var variableBytes = SerializeToBytes(VariableStateCandidate.From(variableCandidate));
                WriteArtifact(saveDirectory, VariableStateFileName, variableBytes);
                artifacts.Add(ProjectSaveArtifact.From(VariableStateFileName, fromRevision, toRevision, variableBytes));
            }

            if (request.Assets != null)
            {
                var normalizedAssets = ProjectAssetJson.WithProjectRevision(request.Assets, toRevision);
                var assetBytes = ProjectAssetJson.SerializeToBytes(normalizedAssets);
                WriteArtifact(saveDirectory, ProjectAssetsFileName, assetBytes);
                artifacts.Add(ProjectSaveArtifact.From(ProjectAssetsFileName, fromRevision, toRevision, assetBytes));
            }

            manifest = ProjectSaveManifest.Create(
                saveId,
                request.Project.Id,
                fromRevision,
                toRevision,
                participants,
                ProjectSavePhase.Prepared,
                artifacts,
                ProjectSaveOperation.Create);
            WriteManifestAtomic(saveDirectory, manifest);
            await InjectFailureAsync(ProjectSaveFailurePoint.AfterPrepared, manifest);
            ValidateStagedArtifacts(saveDirectory, manifest);
            await InjectFailureAsync(ProjectSaveFailurePoint.BeforeCommitIntent, manifest);
            manifest = manifest with { Phase = ProjectSavePhase.CommitIntended };
            WriteManifestAtomic(saveDirectory, manifest);
        }
        catch
        {
            TryDeleteSaveDirectory(saveDirectory);
            TryDeleteProjectDirectoryIfEmpty(request.Project.Id);
            throw;
        }

        HiddenCreates[request.Project.Id] = 0;
        try
        {
            await InjectFailureAsync(ProjectSaveFailurePoint.AfterCommitIntent, manifest);
            var created = await ApplyCreateCommittedIntentAsync(saveDirectory, manifest, request.Project);
            HiddenCreates.TryRemove(request.Project.Id, out _);
            RecoveryRequired.TryRemove(request.Project.Id, out _);
            return new ProjectSaveResult(created, request.Flow, Changed: true);
        }
        catch (Exception originalException)
        {
            try
            {
                await RollbackCreateCommittedIntentAsync(saveDirectory, manifest);
                HiddenCreates.TryRemove(request.Project.Id, out _);
                RecoveryRequired.TryRemove(request.Project.Id, out _);
            }
            catch (Exception rollbackException)
            {
                HiddenCreates[request.Project.Id] = 0;
                RecoveryRequired[request.Project.Id] = rollbackException.Message;
                throw new InvalidOperationException(
                    $"PSV028: project create rollback failed. Original={originalException.Message}; Rollback={rollbackException.Message}",
                    rollbackException);
            }

            throw;
        }
    }

    private async Task<ProjectSaveResult> SaveExistingProjectUnderGateAsync(ProjectSaveRequest request)
    {
        var project = await _projectRepository.GetByIdForUpdateAsync(request.Project.Id)
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
        ProjectAssetSaveCandidate? assetCandidate = request.ProjectAssetCandidate;
        ProjectAssetsDto? carryForwardAssets = null;
        var projectContentChanged = metadataChanged || schemaChanged || flowChanged;
        if (assetCandidate != null && _projectAssetStorage == null)
        {
            throw new InvalidOperationException("PSV017: project asset persistence is unavailable.");
        }

        if (assetCandidate == null && projectContentChanged && _projectAssetStorage != null)
        {
            var existingAssets = await _projectAssetStorage.LoadAssetsAsync(project.Id);
            if (ProjectAssetJson.HasAssets(existingAssets))
            {
                carryForwardAssets = existingAssets;
            }
        }

        if (!projectContentChanged && assetCandidate == null && carryForwardAssets == null)
        {
            return new ProjectSaveResult(project, request.NextFlow, Changed: false);
        }

        var fromRevision = project.PersistenceRevision;
        var toRevision = fromRevision + 1;
        assetCandidate ??= carryForwardAssets == null
            ? null
            : ProjectAssetSaveCandidate.Create(
                ProjectAssetJson.WithProjectRevision(carryForwardAssets, toRevision),
                fromRevision,
                toRevision,
                "carry-forward");
        if (assetCandidate != null)
        {
            ValidateProjectAssetCandidate(assetCandidate, fromRevision, toRevision);
            await EnsureProjectAssetStorageAtRevisionAsync(project.Id, fromRevision);
        }

        var saveId = Guid.NewGuid();
        var saveDirectory = GetSaveDirectory(project.Id, saveId);
        Directory.CreateDirectory(saveDirectory);
        var participants = BuildParticipants(flowChanged, variableCandidate != null, assetCandidate != null);

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

        byte[]? projectAssetsBytes = null;
        if (assetCandidate != null)
        {
            projectAssetsBytes = ProjectAssetJson.SerializeToBytes(assetCandidate.Assets);
            var computedAssetsHash = ProjectAssetJson.ComputeSha256(projectAssetsBytes);
            if (!string.Equals(computedAssetsHash, assetCandidate.AssetsHash, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("PSV018: project asset candidate hash does not match staged manifest.");
            }

            WriteArtifact(saveDirectory, ProjectAssetsFileName, projectAssetsBytes);
        }

        var artifacts = new List<ProjectSaveArtifact>
        {
            ProjectSaveArtifact.From(ProjectCandidateFileName, fromRevision, toRevision, projectBytes)
        };
        if (flowBytes != null)
        {
            var previousFlowBytes = previousFlowJson == null
                ? null
                : new UTF8Encoding(false).GetBytes(previousFlowJson);
            artifacts.Add(ProjectSaveArtifact.From(
                FlowFileName,
                fromRevision,
                toRevision,
                flowBytes,
                previousFlowBytes));
        }

        if (variableBytes != null)
        {
            artifacts.Add(ProjectSaveArtifact.From(VariableStateFileName, fromRevision, toRevision, variableBytes));
        }

        if (projectAssetsBytes != null)
        {
            artifacts.Add(ProjectSaveArtifact.From(ProjectAssetsFileName, fromRevision, toRevision, projectAssetsBytes));
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

    private async Task<ProjectSaveRecoveryDirectoryResult> RecoverDirectoryAsync(string saveDirectory)
    {
        var manifestPath = Path.Combine(saveDirectory, ManifestFileName);
        if (!File.Exists(manifestPath))
        {
            Directory.Delete(saveDirectory, recursive: true);
            TryDeleteDirectoryIfEmpty(Path.GetDirectoryName(saveDirectory));
            return new ProjectSaveRecoveryDirectoryResult(0, 1);
        }

        var manifest = DeserializeFile<ProjectSaveManifest>(manifestPath);
        if (manifest.Operation == ProjectSaveOperation.Create && manifest.Phase != ProjectSavePhase.Completed)
        {
            HiddenCreates[manifest.ProjectId] = 0;
        }
        var gate = await RentProjectGateAsync(manifest.ProjectId);
        await gate.WaitAsync();
        try
        {
            ProjectStates[manifest.ProjectId] = ProjectAccessState.Recovering;
            if (manifest.Phase == ProjectSavePhase.Prepared)
            {
                Directory.Delete(saveDirectory, recursive: true);
                HiddenCreates.TryRemove(manifest.ProjectId, out _);
                TryDeleteProjectDirectoryIfEmpty(manifest.ProjectId);
                return new ProjectSaveRecoveryDirectoryResult(0, 1);
            }

            if (manifest.Phase == ProjectSavePhase.Completed)
            {
                Directory.Delete(saveDirectory, recursive: true);
                HiddenCreates.TryRemove(manifest.ProjectId, out _);
                RecoveryRequired.TryRemove(manifest.ProjectId, out _);
                TryDeleteProjectDirectoryIfEmpty(manifest.ProjectId);
                return new ProjectSaveRecoveryDirectoryResult(1, 0);
            }

            ValidateStagedArtifacts(saveDirectory, manifest);
            if (manifest.Operation == ProjectSaveOperation.Create)
            {
                await RollbackCreateCommittedIntentAsync(saveDirectory, manifest);
                HiddenCreates.TryRemove(manifest.ProjectId, out _);
            }
            else
            {
                await ApplyCommittedIntentAsync(saveDirectory, manifest);
            }
            RecoveryRequired.TryRemove(manifest.ProjectId, out _);
            return new ProjectSaveRecoveryDirectoryResult(1, 0);
        }
        catch (Exception ex)
        {
            RecoveryRequired[manifest.ProjectId] = ex.Message;
            throw new ProjectSaveRecoveryProjectException(manifest.ProjectId, manifest.SaveId, manifest.Phase, ex);
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
            ReleaseProjectGateReference(manifest.ProjectId, gate);
        }
    }

    private async Task<Project> ApplyCreateCommittedIntentAsync(
        string saveDirectory,
        ProjectSaveManifest manifest,
        Project createProject)
    {
        ArgumentNullException.ThrowIfNull(createProject);
        var projectCandidate = DeserializeFile<ProjectCandidate>(Path.Combine(saveDirectory, ProjectCandidateFileName));
        await InjectFailureAsync(ProjectSaveFailurePoint.BeforeProjectApply, manifest);
        var project = await LoadCreateProjectForVerificationAsync(manifest.ProjectId);
        if (project == null)
        {
            if (createProject.Id != manifest.ProjectId ||
                createProject.PersistenceRevision != manifest.ToRevision)
            {
                throw new InvalidOperationException("PSV003: in-memory create project does not match the staged target revision.");
            }

            VerifyProjectAtCandidate(createProject, projectCandidate);
            project = await _projectRepository.AddAsync(createProject)
                ?? throw new InvalidOperationException("PSV003: project repository returned no created aggregate.");
            if (project.Id != manifest.ProjectId || project.PersistenceRevision != manifest.ToRevision)
            {
                throw new InvalidOperationException("PSV003: created project does not match the staged target revision.");
            }

            VerifyProjectAtCandidate(project, projectCandidate);
        }
        else
        {
            if (project.PersistenceRevision != manifest.ToRevision)
            {
                throw new InvalidOperationException(
                    $"PSV003: create project revision conflict. Current={project.PersistenceRevision}, To={manifest.ToRevision}.");
            }

            VerifyProjectAtCandidate(project, projectCandidate);
        }

        await InjectFailureAsync(ProjectSaveFailurePoint.AfterProjectApply, manifest);
        if (manifest.Participants.Contains(ProjectSaveParticipant.ProjectAssets))
        {
            if (_projectAssetStorage == null)
            {
                throw new InvalidOperationException("PSV017: project asset persistence is unavailable.");
            }

            var assetArtifact = manifest.Artifacts.Single(item => item.Name == ProjectAssetsFileName);
            var assets = DeserializeFile<ProjectAssetsDto>(Path.Combine(saveDirectory, ProjectAssetsFileName));
            await InjectFailureAsync(ProjectSaveFailurePoint.BeforeProjectAssetsApply, manifest);
            var metadata = await _projectAssetStorage.LoadMetadataAsync(manifest.ProjectId);
            if (metadata == null)
            {
                await _projectAssetStorage.SaveAssetsAsync(
                    manifest.ProjectId,
                    assets,
                    manifest.ToRevision,
                    manifest.SaveId,
                    assetArtifact.Sha256);
            }
            else if (metadata.PersistenceRevision != manifest.ToRevision ||
                !string.Equals(metadata.AssetsHash, assetArtifact.Sha256, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("PSV020: project asset hash mismatch at create target revision.");
            }

            await InjectFailureAsync(ProjectSaveFailurePoint.AfterProjectAssetsApply, manifest);
        }

        if (manifest.Participants.Contains(ProjectSaveParticipant.Flow))
        {
            var flowArtifact = manifest.Artifacts.Single(item => item.Name == FlowFileName);
            var flowJson = await File.ReadAllTextAsync(Path.Combine(saveDirectory, FlowFileName), Encoding.UTF8);
            var metadata = await _flowStorage.LoadMetadataAsync(manifest.ProjectId);
            if (metadata == null)
            {
                await _flowStorage.SaveFlowJsonAsync(manifest.ProjectId, flowJson, manifest.ToRevision);
            }
            else if (metadata.PersistenceRevision != manifest.ToRevision ||
                !string.Equals(metadata.FlowHash, flowArtifact.Sha256, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("PSV004: flow artifact hash mismatch at create target revision.");
            }

            await InjectFailureAsync(ProjectSaveFailurePoint.AfterFlowApply, manifest);
        }

        if (manifest.Participants.Contains(ProjectSaveParticipant.VariableState))
        {
            if (_projectVariableSessions == null)
            {
                throw new InvalidOperationException("PSV007: project variable state persistence is unavailable.");
            }

            var variableState = DeserializeFile<VariableStateCandidate>(Path.Combine(saveDirectory, VariableStateFileName));
            var metadata = _projectVariableSessions.LoadStateMetadata(manifest.ProjectId);
            if (metadata == null)
            {
                if (!_projectVariableSessions.TryPersistMigrationCandidate(
                        manifest.ProjectId,
                        variableState.ToMigrationCandidate(),
                        manifest.ToRevision,
                        manifest.SaveId,
                        variableState.StateHash,
                        out _,
                        out var persistError))
                {
                    throw new InvalidOperationException(persistError ?? "PSV007: project variable state persistence is unavailable.");
                }
            }
            else if (metadata.PersistenceRevision != manifest.ToRevision ||
                !string.Equals(metadata.StateHash, variableState.StateHash, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("PSV013: variable state hash mismatch at create target revision.");
            }

            await InjectFailureAsync(ProjectSaveFailurePoint.AfterVariableStateApply, manifest);
        }

        await InjectFailureAsync(ProjectSaveFailurePoint.BeforeComplete, manifest);
        WriteManifestAtomic(saveDirectory, manifest with { Phase = ProjectSavePhase.Completed });
        TryDeleteSaveDirectory(saveDirectory);
        TryDeleteProjectDirectoryIfEmpty(manifest.ProjectId);
        return project;
    }

    private async Task RollbackCreateCommittedIntentAsync(
        string saveDirectory,
        ProjectSaveManifest manifest)
    {
        var projectCandidate = DeserializeFile<ProjectCandidate>(Path.Combine(saveDirectory, ProjectCandidateFileName));
        await InjectFailureAsync(ProjectSaveFailurePoint.BeforeCreateRollback, manifest);
        var project = await LoadCreateProjectForVerificationAsync(manifest.ProjectId);
        if (project != null)
        {
            if (project.PersistenceRevision != manifest.ToRevision)
            {
                throw new InvalidOperationException(
                    $"PSV003: create rollback revision conflict. Current={project.PersistenceRevision}, To={manifest.ToRevision}.");
            }

            VerifyProjectAtCandidate(project, projectCandidate);
            await _projectRepository.DeleteAsync(project);
        }

        await _flowStorage.DeleteFlowJsonAsync(manifest.ProjectId);
        if (_projectAssetStorage != null)
        {
            await _projectAssetStorage.DeleteAssetsAsync(manifest.ProjectId);
        }

        _projectVariableSessions?.Delete(manifest.ProjectId);
        await InjectFailureAsync(ProjectSaveFailurePoint.AfterCreateRollback, manifest);
        TryDeleteSaveDirectory(saveDirectory);
        TryDeleteProjectDirectoryIfEmpty(manifest.ProjectId);
    }

    private async Task ApplyCommittedIntentAsync(string saveDirectory, ProjectSaveManifest manifest)
    {
        var projectCandidate = DeserializeFile<ProjectCandidate>(Path.Combine(saveDirectory, ProjectCandidateFileName));
        var project = await _projectRepository.GetByIdForUpdateAsync(manifest.ProjectId)
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
        if (manifest.Participants.Contains(ProjectSaveParticipant.ProjectAssets))
        {
            if (_projectAssetStorage == null)
            {
                throw new InvalidOperationException("PSV017: project asset persistence is unavailable.");
            }

            var assetArtifact = manifest.Artifacts.Single(item => item.Name == ProjectAssetsFileName);
            var assets = DeserializeFile<ProjectAssetsDto>(Path.Combine(saveDirectory, ProjectAssetsFileName));
            await InjectFailureAsync(ProjectSaveFailurePoint.BeforeProjectAssetsApply, manifest);

            var metadata = await _projectAssetStorage.LoadMetadataAsync(manifest.ProjectId);
            if (metadata?.PersistenceRevision == manifest.ToRevision)
            {
                if (!string.Equals(metadata.AssetsHash, assetArtifact.Sha256, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("PSV020: project asset hash mismatch at target revision.");
                }

                var storedAssets = await _projectAssetStorage.LoadAssetsAsync(manifest.ProjectId);
                if (!string.Equals(ProjectAssetJson.ComputeAssetsHash(storedAssets), assetArtifact.Sha256, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("PSV020: project asset hash mismatch at target revision.");
                }
            }
            else if (metadata == null || metadata.PersistenceRevision == manifest.FromRevision)
            {
                await _projectAssetStorage.SaveAssetsAsync(
                    manifest.ProjectId,
                    assets,
                    manifest.ToRevision,
                    manifest.SaveId,
                    assetArtifact.Sha256);
                metadata = await _projectAssetStorage.LoadMetadataAsync(manifest.ProjectId);
                var storedAssets = await _projectAssetStorage.LoadAssetsAsync(manifest.ProjectId);
                if (metadata?.PersistenceRevision != manifest.ToRevision ||
                    !string.Equals(metadata.AssetsHash, assetArtifact.Sha256, StringComparison.Ordinal) ||
                    !string.Equals(ProjectAssetJson.ComputeAssetsHash(storedAssets), assetArtifact.Sha256, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("PSV021: project asset apply verification failed.");
                }
            }
            else
            {
                throw new InvalidOperationException(
                    $"PSV023: project asset revision conflict. Current={metadata.PersistenceRevision}, From={manifest.FromRevision}, To={manifest.ToRevision}.");
            }

            await InjectFailureAsync(ProjectSaveFailurePoint.AfterProjectAssetsApply, manifest);
        }

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
            else if (metadata == null || metadata.PersistenceRevision <= manifest.FromRevision)
            {
                if (!string.IsNullOrWhiteSpace(flowArtifact.PreviousSha256))
                {
                    var currentFlowJson = await _flowStorage.LoadFlowJsonAsync(manifest.ProjectId);
                    var currentFlowHash = currentFlowJson == null
                        ? null
                        : ComputeSha256(new UTF8Encoding(false).GetBytes(currentFlowJson));
                    if (!string.Equals(currentFlowHash, flowArtifact.PreviousSha256, StringComparison.Ordinal) ||
                        (metadata != null &&
                         !string.Equals(metadata.FlowHash, flowArtifact.PreviousSha256, StringComparison.Ordinal)))
                    {
                        throw new InvalidOperationException("PSV006: authoritative flow changed outside the project revision CAS.");
                    }
                }

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
            else if (CanApplyVariableStateCandidate(metadata, variableState, manifest.FromRevision))
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
                    $"PSV014: variable state revision conflict. Current={metadata?.PersistenceRevision.ToString() ?? "missing"}, From={manifest.FromRevision}, To={manifest.ToRevision}.");
            }

            await InjectFailureAsync(ProjectSaveFailurePoint.AfterVariableStateApply, manifest);
        }

        var completed = manifest with { Phase = ProjectSavePhase.Completed };
        await InjectFailureAsync(ProjectSaveFailurePoint.BeforeComplete, manifest);
        WriteManifestAtomic(saveDirectory, completed);
        Directory.Delete(saveDirectory, recursive: true);
        TryDeleteProjectDirectoryIfEmpty(manifest.ProjectId);
    }

    private Task InjectFailureAsync(ProjectSaveFailurePoint point, ProjectSaveManifest manifest) =>
        _failureInjector?.OnPointAsync(point, manifest) ?? Task.CompletedTask;

    private async Task<Project?> LoadCreateProjectForVerificationAsync(Guid projectId) =>
        await _projectRepository.GetWithFlowAsync(projectId) ??
        await _projectRepository.GetByIdForUpdateAsync(projectId);

    private static bool CanApplyVariableStateCandidate(
        ProjectVariableStateMetadata? metadata,
        VariableStateCandidate candidate,
        long fromRevision)
    {
        if (metadata == null)
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(candidate.SourceStateHash))
        {
            return metadata.PersistenceRevision <= fromRevision &&
                candidate.SourcePersistenceRevision == metadata.PersistenceRevision &&
                string.Equals(candidate.SourceSchemaHash, metadata.SchemaHash, StringComparison.Ordinal) &&
                string.Equals(candidate.SourceStateHash, metadata.StateHash, StringComparison.Ordinal);
        }

        // Compatibility for manifests staged before source-state fencing was added.
        return metadata.PersistenceRevision == fromRevision ||
            (metadata.PersistenceRevision == 0 && fromRevision == 0);
    }

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

    private void TryDeleteProjectDirectoryIfEmpty(Guid projectId) =>
        TryDeleteDirectoryIfEmpty(Path.Combine(_transactionRoot, projectId.ToString("D")));

    private static void TryDeleteDirectoryIfEmpty(string? directory)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(directory) &&
                Directory.Exists(directory) &&
                !Directory.EnumerateFileSystemEntries(directory).Any())
            {
                Directory.Delete(directory);
            }
        }
        catch
        {
            // Empty transaction directories are non-authoritative cleanup residue.
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

    private static async ValueTask<ProjectGate> RentProjectGateAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        while (true)
        {
            var gate = ProjectGates.GetOrAdd(projectId, static _ => new ProjectGate());
            if (gate.TryAddReference())
            {
                if (ProjectGates.TryGetValue(projectId, out var current) && ReferenceEquals(current, gate))
                {
                    return gate;
                }

                ReleaseProjectGateReference(projectId, gate);
                continue;
            }

            if (gate.IsClosedAndIdle)
            {
                RemoveProjectGateIfCurrent(projectId, gate);
            }

            await gate.WaitUntilClosedAsync(cancellationToken);
        }
    }

    private static void ReleaseAcquiredProjectGate(Guid projectId, ProjectGate gate)
    {
        gate.Release();
        ReleaseProjectGateReference(projectId, gate);
    }

    private static void ReleaseProjectGateReference(Guid projectId, ProjectGate gate)
    {
        if (gate.ReleaseReference())
        {
            RemoveProjectGateIfCurrent(projectId, gate);
        }
    }

    private static void RequestProjectGateCleanup(Guid projectId)
    {
        if (!ProjectGates.TryGetValue(projectId, out var gate))
        {
            return;
        }

        if (gate.RequestClose())
        {
            RemoveProjectGateIfCurrent(projectId, gate);
        }
    }

    private static void RemoveProjectGateIfCurrent(Guid projectId, ProjectGate gate)
    {
        ((ICollection<KeyValuePair<Guid, ProjectGate>>)ProjectGates)
            .Remove(new KeyValuePair<Guid, ProjectGate>(projectId, gate));
    }

    internal static void ResetStaticStateForTests()
    {
        ProjectGates.Clear();
        RecoveryRequired.Clear();
        HiddenCreates.Clear();
        ProjectStates.Clear();
        lock (StartupRecoveryGate)
        {
            StartupRecoveryReady = CreateCompletedStartupBarrier();
            StartupRecoveryCompleted = true;
            StartupRecoveryHasRun = false;
        }
    }

    internal static bool HasProjectStateForTests(Guid projectId) => ProjectStates.ContainsKey(projectId);

    internal static bool HasRecoveryRequiredForTests(Guid projectId) => RecoveryRequired.ContainsKey(projectId);

    internal static bool HasProjectGateForTests(Guid projectId) => ProjectGates.ContainsKey(projectId);

    public static bool IsProjectHidden(Guid projectId) => HiddenCreates.ContainsKey(projectId);

    private static void VerifyProjectAtCandidate(Project project, ProjectCandidate candidate)
    {
        var currentHash = ProjectGlobalVariableSchemaValidator.ComputeSchemaHash(project.GlobalVariables);
        var candidateHash = ProjectGlobalVariableSchemaValidator.ComputeSchemaHash(candidate.GlobalVariables);
        var settingsMatch = project.GlobalSettings.Count == candidate.GlobalSettings.Count &&
            project.GlobalSettings.All(item =>
                candidate.GlobalSettings.TryGetValue(item.Key, out var value) &&
                string.Equals(item.Value, value, StringComparison.Ordinal));
        var flowMatches = string.IsNullOrWhiteSpace(candidate.DatabaseFlowHash) ||
            (string.Equals(
                 ExecutionFlowIdentity.ComputeFlowHash(project.Flow),
                 candidate.DatabaseFlowHash,
                 StringComparison.Ordinal) &&
             (!candidate.DatabaseFlowId.HasValue || project.Flow.Id == candidate.DatabaseFlowId.Value) &&
             (candidate.DatabaseFlowName == null ||
              string.Equals(project.Flow.Name, candidate.DatabaseFlowName, StringComparison.Ordinal)));
        if (!string.Equals(project.Name, candidate.Name, StringComparison.Ordinal) ||
            !string.Equals(project.Description, candidate.Description, StringComparison.Ordinal) ||
            !string.Equals(project.Version, candidate.Version, StringComparison.Ordinal) ||
            !settingsMatch ||
            !string.Equals(currentHash, candidateHash, StringComparison.Ordinal) ||
            !flowMatches)
        {
            throw new InvalidOperationException("PSV008: project candidate does not match target revision.");
        }
    }

    private static bool SchemaHasVariableState(ProjectGlobalVariableSchema previousSchema, ProjectGlobalVariableSchema nextSchema) =>
        previousSchema.Variables.Count > 0 || nextSchema.Variables.Count > 0;

    private static IReadOnlyList<ProjectSaveParticipant> BuildParticipants(
        bool flowChanged,
        bool variableStateChanged,
        bool projectAssetsChanged)
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

        if (projectAssetsChanged)
        {
            participants.Add(ProjectSaveParticipant.ProjectAssets);
        }

        return participants;
    }

    private static IReadOnlyList<ProjectSaveParticipant> BuildCreateParticipants(
        bool hasFlow,
        bool hasVariableState,
        bool hasProjectAssets)
    {
        var participants = new List<ProjectSaveParticipant> { ProjectSaveParticipant.Project };
        if (hasFlow)
        {
            participants.Add(ProjectSaveParticipant.Flow);
        }

        if (hasVariableState)
        {
            participants.Add(ProjectSaveParticipant.VariableState);
        }

        if (hasProjectAssets)
        {
            participants.Add(ProjectSaveParticipant.ProjectAssets);
        }

        return participants;
    }

    private static void ValidateProjectAssetCandidate(
        ProjectAssetSaveCandidate candidate,
        long fromRevision,
        long toRevision)
    {
        if (candidate.FromRevision != fromRevision || candidate.ToRevision != toRevision)
        {
            throw new InvalidOperationException(
                $"PSV024: project asset candidate revision mismatch. CandidateFrom={candidate.FromRevision}, CandidateTo={candidate.ToRevision}, From={fromRevision}, To={toRevision}.");
        }

        var computedHash = ProjectAssetJson.ComputeAssetsHash(candidate.Assets);
        if (!string.Equals(computedHash, candidate.AssetsHash, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("PSV018: project asset candidate hash does not match staged manifest.");
        }
    }

    private async Task EnsureProjectAssetStorageAtRevisionAsync(Guid projectId, long fromRevision)
    {
        if (_projectAssetStorage == null)
        {
            throw new InvalidOperationException("PSV017: project asset persistence is unavailable.");
        }

        var metadata = await _projectAssetStorage.LoadMetadataAsync(projectId);
        if (metadata != null && metadata.PersistenceRevision != fromRevision)
        {
            throw new InvalidOperationException(
                $"PSV023: project asset revision conflict. Current={metadata.PersistenceRevision}, From={fromRevision}.");
        }
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
    string? NextFlowJson,
    ProjectAssetSaveCandidate? ProjectAssetCandidate = null);

public sealed record ProjectCreateSaveRequest(
    Project Project,
    OperatorFlowDto? Flow,
    string? FlowJson,
    ProjectAssetsDto? Assets = null);

public sealed record ProjectSaveResult(Project Project, OperatorFlowDto? Flow, bool Changed);

public sealed record ProjectSaveRecoverySummary(
    int RecoveredCount,
    int DiscardedPreparedCount,
    IReadOnlyList<Guid> RecoveryRequiredProjectIds,
    IReadOnlyList<ProjectSaveRecoveryFailure> Failures,
    string? SystemFailure);

public sealed record ProjectSaveRecoveryFailure(
    Guid ProjectId,
    Guid SaveId,
    ProjectSavePhase Phase,
    string Error);

internal sealed record ProjectSaveRecoveryDirectoryResult(int RecoveredCount, int DiscardedPreparedCount);

public sealed class ProjectSaveRecoverySystemException : InvalidOperationException
{
    public ProjectSaveRecoverySystemException(
        string message,
        ProjectSaveRecoverySummary summary,
        Exception innerException)
        : base(message, innerException)
    {
        Summary = summary;
    }

    public ProjectSaveRecoverySummary Summary { get; }
}

public sealed class ProjectSaveRecoveryProjectException : InvalidOperationException
{
    public ProjectSaveRecoveryProjectException(
        Guid projectId,
        Guid saveId,
        ProjectSavePhase phase,
        Exception innerException)
        : base($"PSV016: project '{projectId}' recovery failed for save '{saveId}' in phase '{phase}': {innerException.Message}", innerException)
    {
        ProjectId = projectId;
        SaveId = saveId;
        Phase = phase;
    }

    public Guid ProjectId { get; }

    public Guid SaveId { get; }

    public ProjectSavePhase Phase { get; }
}

public sealed record ProjectSaveManifest(
    int SchemaVersion,
    Guid SaveId,
    Guid ProjectId,
    long FromRevision,
    long ToRevision,
    IReadOnlyList<ProjectSaveParticipant> Participants,
    IReadOnlyList<ProjectSaveArtifact> Artifacts,
    ProjectSavePhase Phase,
    DateTimeOffset CreatedAtUtc,
    ProjectSaveOperation Operation = ProjectSaveOperation.ExistingMutation)
{
    public static ProjectSaveManifest Create(
        Guid saveId,
        Guid projectId,
        long fromRevision,
        long toRevision,
        IReadOnlyList<ProjectSaveParticipant> participants,
        ProjectSavePhase phase,
        IReadOnlyList<ProjectSaveArtifact> artifacts,
        ProjectSaveOperation operation = ProjectSaveOperation.ExistingMutation)
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
            DateTimeOffset.UtcNow,
            operation);
    }
}

public sealed record ProjectSaveArtifact(
    string Name,
    long FromRevision,
    long ToRevision,
    long Length,
    string Sha256,
    string? PreviousSha256 = null)
{
    public static ProjectSaveArtifact From(
        string name,
        long fromRevision,
        long toRevision,
        byte[] bytes,
        byte[]? previousBytes = null) =>
        new(
            name,
            fromRevision,
            toRevision,
            bytes.LongLength,
            ComputeSha256(bytes),
            previousBytes == null ? null : ComputeSha256(previousBytes));

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
    VariableState = 2,
    ProjectAssets = 3
}

public enum ProjectSavePhase
{
    Prepared = 0,
    CommitIntended = 1,
    Completed = 2
}

public enum ProjectSaveOperation
{
    ExistingMutation = 0,
    Create = 1
}

public enum ProjectAccessState
{
    Idle = 0,
    Saving = 1,
    Recovering = 2,
    RecoveryRequired = 3
}

internal sealed class ProjectGate
{
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private readonly object _sync = new();
    private readonly TaskCompletionSource _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _referenceCount;
    private bool _closing;

    public bool IsClosedAndIdle
    {
        get
        {
            lock (_sync)
            {
                return _closing && _referenceCount == 0;
            }
        }
    }

    public Task WaitAsync(CancellationToken cancellationToken = default) =>
        _semaphore.WaitAsync(cancellationToken);

    public void Release() => _semaphore.Release();

    public bool TryAddReference()
    {
        lock (_sync)
        {
            if (_closing)
            {
                return false;
            }

            _referenceCount++;
            return true;
        }
    }

    public bool ReleaseReference()
    {
        var shouldRemove = false;
        lock (_sync)
        {
            if (_referenceCount <= 0)
            {
                throw new InvalidOperationException("Project gate reference count cannot be negative.");
            }

            _referenceCount--;
            shouldRemove = _closing && _referenceCount == 0;
        }

        if (shouldRemove)
        {
            _closed.TrySetResult();
        }

        return shouldRemove;
    }

    public bool RequestClose()
    {
        var shouldRemove = false;
        lock (_sync)
        {
            _closing = true;
            shouldRemove = _referenceCount == 0;
        }

        if (shouldRemove)
        {
            _closed.TrySetResult();
        }

        return shouldRemove;
    }

    public Task WaitUntilClosedAsync(CancellationToken cancellationToken) =>
        _closed.Task.WaitAsync(cancellationToken);
}

public sealed class ProjectAccessLease : IAsyncDisposable, IDisposable
{
    private readonly Guid _projectId;
    private readonly Action _release;
    private bool _disposed;

    internal ProjectAccessLease(Guid projectId, Action release)
    {
        _projectId = projectId;
        _release = release;
    }

    internal void EnsureActiveFor(Guid projectId)
    {
        if (_disposed || projectId != _projectId)
        {
            throw new InvalidOperationException("PSV026: project access lease is missing, disposed, or belongs to another project.");
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _release();
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
    BeforeProjectAssetsApply = 4,
    AfterProjectAssetsApply = 5,
    AfterFlowApply = 6,
    AfterVariableStateApply = 7,
    BeforeComplete = 8,
    BeforeCommitIntent = 9,
    BeforeCreateRollback = 10,
    AfterCreateRollback = 11
}

public interface IProjectSaveFailureInjector
{
    Task OnPointAsync(ProjectSaveFailurePoint point, ProjectSaveManifest manifest);
}

public sealed record ProjectAssetSaveCandidate(
    ProjectAssetsDto Assets,
    string AssetsHash,
    long FromRevision,
    long ToRevision,
    string Reason)
{
    public static ProjectAssetSaveCandidate Create(
        ProjectAssetsDto assets,
        long fromRevision,
        long toRevision,
        string reason)
    {
        var normalized = ProjectAssetJson.WithProjectRevision(assets, toRevision);
        return new ProjectAssetSaveCandidate(
            normalized,
            ProjectAssetJson.ComputeAssetsHash(normalized),
            fromRevision,
            toRevision,
            reason);
    }
}

public sealed record ProjectCandidate(
    Guid ProjectId,
    string Name,
    string? Description,
    string Version,
    Dictionary<string, string> GlobalSettings,
    ProjectGlobalVariableSchema GlobalVariables,
    long FromRevision,
    long ToRevision,
    DateTime CreatedAt = default,
    DateTime? ModifiedAt = null,
    string? DatabaseFlowHash = null,
    Guid? DatabaseFlowId = null,
    string? DatabaseFlowName = null)
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
            toRevision,
            project.CreatedAt,
            project.ModifiedAt);

    public static ProjectCandidate FromNew(Project project) =>
        new(
            project.Id,
            project.Name,
            project.Description,
            project.Version,
            new Dictionary<string, string>(project.GlobalSettings),
            project.GlobalVariables,
            -1,
            project.PersistenceRevision,
            project.CreatedAt,
            project.ModifiedAt,
            ExecutionFlowIdentity.ComputeFlowHash(project.Flow),
            project.Flow.Id,
            project.Flow.Name);
}

public sealed record VariableStateCandidate(
    ProjectGlobalVariableSchema Schema,
    IReadOnlyList<ProjectVariableValueSnapshot> Snapshots,
    string SchemaHash,
    string StateHash,
    long SchemaGeneration,
    bool SessionWasLoaded,
    long? SourcePersistenceRevision = null,
    string? SourceSchemaHash = null,
    string? SourceStateHash = null)
{
    public static VariableStateCandidate From(ProjectVariableSessionMigrationCandidate candidate) =>
        new(
            candidate.Schema,
            candidate.Snapshots,
            ProjectGlobalVariableSchemaValidator.ComputeSchemaHash(candidate.Schema),
            ProjectVariableStateHash.Compute(candidate.Schema, candidate.Snapshots),
            candidate.SchemaGeneration,
            candidate.SessionWasLoaded,
            candidate.SourceMetadata?.PersistenceRevision,
            candidate.SourceMetadata?.SchemaHash,
            candidate.SourceMetadata?.StateHash);

    public ProjectVariableSessionMigrationCandidate ToMigrationCandidate() =>
        new(Schema, Snapshots, SchemaGeneration, SessionWasLoaded);
}
