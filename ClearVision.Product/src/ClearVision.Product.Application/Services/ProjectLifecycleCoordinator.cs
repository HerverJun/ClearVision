using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ClearVision.Product.Application.DTOs;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Exceptions;
using ClearVision.Product.Core.Interfaces;
using ClearVision.Product.Core.Services;
using Microsoft.Extensions.Logging;

namespace ClearVision.Product.Application.Services;

public sealed class ProjectLifecycleCoordinator
{
    public const int PayloadFingerprintVersion = 1;
    public static readonly TimeSpan CreateOperationRetention = TimeSpan.FromDays(7);
    public static readonly TimeSpan ImportOperationRetention = TimeSpan.FromDays(7);
    public static readonly TimeSpan DeleteOperationRetention = TimeSpan.FromDays(30);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly ConcurrentDictionary<OperationKey, Lazy<Task<ProjectLifecycleOperation>>> InFlight = new();

    private readonly IProjectLifecycleOperationRepository _operationRepository;
    private readonly IProjectRepository _projectRepository;
    private readonly ProjectService _projectService;
    private readonly IInspectionRuntimeCoordinator _runtimeCoordinator;
    private readonly ILogger<ProjectLifecycleCoordinator>? _logger;

    public ProjectLifecycleCoordinator(
        IProjectLifecycleOperationRepository operationRepository,
        IProjectRepository projectRepository,
        ProjectService projectService,
        IInspectionRuntimeCoordinator runtimeCoordinator,
        ILogger<ProjectLifecycleCoordinator>? logger = null)
    {
        _operationRepository = operationRepository ?? throw new ArgumentNullException(nameof(operationRepository));
        _projectRepository = projectRepository ?? throw new ArgumentNullException(nameof(projectRepository));
        _projectService = projectService ?? throw new ArgumentNullException(nameof(projectService));
        _runtimeCoordinator = runtimeCoordinator ?? throw new ArgumentNullException(nameof(runtimeCoordinator));
        _logger = logger;
    }

    public async Task<ProjectCreateCommandResult> CreateBlankAsync(
        string userId,
        CreateProjectRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var normalizedUserId = NormalizeUserId(userId);
        var clientOperationId = request.ClientOperationId
            ?? throw new ProjectLifecycleValidationException(
                "PROJECT_VALIDATION_OPERATION_ID_REQUIRED",
                "clientOperationId is required for authoritative blank project create.");
        if (clientOperationId == Guid.Empty)
        {
            throw new ProjectLifecycleValidationException(
                "PROJECT_VALIDATION_OPERATION_ID_REQUIRED",
                "clientOperationId cannot be empty.");
        }

        if (request.Flow != null || request.GlobalVariables != null || request.AdditionalProperties?.Count > 0)
        {
            throw new ProjectLifecycleValidationException(
                "PROJECT_VALIDATION_BLANK_CREATE_ONLY",
                "F04 project create only accepts clientOperationId, name, and description.");
        }

        var name = NormalizeName(request.Name);
        var description = NormalizeDescription(request.Description);
        var fingerprint = ComputeCreateFingerprint(name, description);
        var operation = await _operationRepository.GetAsync(
            normalizedUserId,
            ProjectLifecycleOperationKind.Create,
            clientOperationId,
            cancellationToken);
        EnsureNotExpired(operation, clientOperationId);
        var replayed = operation != null;
        if (operation == null)
        {
            operation = ProjectLifecycleOperation.ReserveCreate(
                normalizedUserId,
                clientOperationId,
                fingerprint,
                Guid.NewGuid(),
                name,
                description,
                DateTimeOffset.UtcNow);
            try
            {
                await _operationRepository.AddAsync(operation, cancellationToken);
            }
            catch
            {
                operation = await _operationRepository.GetAsync(
                    normalizedUserId,
                    ProjectLifecycleOperationKind.Create,
                    clientOperationId,
                    cancellationToken);
                if (operation == null)
                {
                    throw;
                }

                replayed = true;
            }
        }

        EnsureNotExpired(operation, clientOperationId);
        EnsureFingerprint(operation, fingerprint);
        operation = await ExecuteSingleFlightAsync(
            operation,
            () => ExecuteCreateAsync(operation.Id, cancellationToken));
        ThrowIfFailed(operation);

        var result = DeserializeResult(operation);
        var project = result.Project
            ?? throw new InvalidOperationException("Completed create operation has no Project result.");
        return new ProjectCreateCommandResult(
            project,
            replayed,
            await ToDtoAsync(operation, cancellationToken));
    }

    public async Task<ProjectDeleteCommandResult> DeleteAsync(
        string userId,
        Guid projectId,
        DeleteProjectRequest request,
        bool waitForCleanup,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var normalizedUserId = NormalizeUserId(userId);
        if (projectId == Guid.Empty || request.ClientOperationId == Guid.Empty)
        {
            throw new ProjectLifecycleValidationException(
                "PROJECT_VALIDATION_OPERATION_ID_REQUIRED",
                "Project id and clientOperationId are required.");
        }

        if (request.ExpectedPersistenceRevision < 0)
        {
            throw new ProjectLifecycleValidationException(
                "PROJECT_VALIDATION_REVISION_INVALID",
                "expectedPersistenceRevision cannot be negative.");
        }

        var fingerprint = ComputeDeleteFingerprint(projectId, request.ExpectedPersistenceRevision);
        var operation = await _operationRepository.GetAsync(
            normalizedUserId,
            ProjectLifecycleOperationKind.Delete,
            request.ClientOperationId,
            cancellationToken);
        EnsureNotExpired(operation, request.ClientOperationId);
        var replayed = operation != null;
        if (operation == null)
        {
            operation = ProjectLifecycleOperation.ReserveDelete(
                normalizedUserId,
                request.ClientOperationId,
                fingerprint,
                projectId,
                request.ExpectedPersistenceRevision,
                DateTimeOffset.UtcNow);
            try
            {
                await _operationRepository.AddAsync(operation, cancellationToken);
            }
            catch
            {
                operation = await _operationRepository.GetAsync(
                    normalizedUserId,
                    ProjectLifecycleOperationKind.Delete,
                    request.ClientOperationId,
                    cancellationToken);
                if (operation == null)
                {
                    throw;
                }

                replayed = true;
            }
        }

        EnsureNotExpired(operation, request.ClientOperationId);
        EnsureFingerprint(operation, fingerprint);
        operation = await ExecuteSingleFlightAsync(
            operation,
            () => ExecuteDeleteAsync(operation.Id, cancellationToken));
        ThrowIfFailed(operation);
        if (waitForCleanup && operation.CleanupStatus != ProjectLifecycleCleanupStatus.CleanupCompleted)
        {
            operation = await ProcessCleanupAsync(operation.Id, cancellationToken);
        }

        return new ProjectDeleteCommandResult(
            projectId,
            replayed,
            await ToDtoAsync(operation, cancellationToken));
    }

    public async Task<ProjectImportCommandResult> ImportAsync(
        string userId,
        ProjectImportRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var normalizedUserId = NormalizeUserId(userId);
        if (request.ClientOperationId == Guid.Empty)
        {
            throw new ProjectLifecycleValidationException(
                "PROJECT_VALIDATION_OPERATION_ID_REQUIRED",
                "clientOperationId is required for project import.");
        }

        var mode = NormalizeImportMode(request.Mode);
        var expectedRevision = request.ExpectedPersistenceRevision;
        if (expectedRevision is < 0)
        {
            throw new ProjectLifecycleValidationException(
                "PROJECT_VALIDATION_REVISION_INVALID",
                "expectedPersistenceRevision cannot be negative.");
        }

        if (mode == ProjectImportMode.CreateNew && expectedRevision.HasValue)
        {
            throw new ProjectLifecycleValidationException(
                "PROJECT_IMPORT_CREATE_REVISION_FORBIDDEN",
                "CREATE_NEW import cannot provide an expectedPersistenceRevision.");
        }

        var targetProjectId = request.TargetProjectId;
        if (mode == ProjectImportMode.OverwriteExisting && !targetProjectId.HasValue)
        {
            throw new ProjectLifecycleValidationException(
                "PROJECT_IMPORT_TARGET_REQUIRED",
                "OVERWRITE_EXISTING import requires targetProjectId.");
        }
        else if (mode == ProjectImportMode.OverwriteExisting && targetProjectId == Guid.Empty)
        {
            throw new ProjectLifecycleValidationException(
                "PROJECT_IMPORT_TARGET_REQUIRED",
                "targetProjectId cannot be empty.");
        }

        if (mode == ProjectImportMode.OverwriteExisting && !expectedRevision.HasValue)
        {
            throw new ProjectLifecycleValidationException(
                "PROJECT_IMPORT_REVISION_REQUIRED",
                "OVERWRITE_EXISTING import requires expectedPersistenceRevision.");
        }

        var document = request.Document
            ?? throw new ProjectLifecycleValidationException(
                "PROJECT_IMPORT_DOCUMENT_REQUIRED",
                "Project import document is required.");
        var documentJson = ProjectJsonContract.Serialize(document);
        var existingOperation = await _operationRepository.GetAsync(
            normalizedUserId,
            ProjectLifecycleOperationKind.Import,
            request.ClientOperationId,
            cancellationToken);
        EnsureNotExpired(existingOperation, request.ClientOperationId);

        var projectId = existingOperation?.ProjectId ??
            (mode == ProjectImportMode.OverwriteExisting
                ? targetProjectId!.Value
                : Guid.NewGuid());
        var fingerprint = ComputeImportFingerprint(
            mode,
            mode == ProjectImportMode.OverwriteExisting ? projectId : null,
            expectedRevision,
            documentJson);
        var operation = existingOperation;
        var replayed = operation != null;
        if (operation == null)
        {
            var payload = new ProjectImportExecutionPayload
            {
                Mode = mode,
                ExpectedPersistenceRevision = mode == ProjectImportMode.CreateNew ? 0 : expectedRevision,
                Document = document
            };
            operation = ProjectLifecycleOperation.ReserveImport(
                normalizedUserId,
                request.ClientOperationId,
                fingerprint,
                projectId,
                payload.ExpectedPersistenceRevision,
                JsonSerializer.Serialize(payload, JsonOptions),
                DateTimeOffset.UtcNow);
            try
            {
                await _operationRepository.AddAsync(operation, cancellationToken);
            }
            catch
            {
                operation = await _operationRepository.GetAsync(
                    normalizedUserId,
                    ProjectLifecycleOperationKind.Import,
                    request.ClientOperationId,
                    cancellationToken);
                if (operation == null)
                {
                    throw;
                }

                replayed = true;
            }
        }

        EnsureNotExpired(operation, request.ClientOperationId);
        EnsureFingerprint(operation, fingerprint);
        operation = await ExecuteSingleFlightAsync(
            operation,
            () => ExecuteImportAsync(operation.Id, cancellationToken));
        ThrowIfFailed(operation);
        var result = DeserializeResult(operation).Project
            ?? throw new InvalidOperationException("Completed import operation has no Project result.");
        return new ProjectImportCommandResult(
            operation.ProjectId,
            result,
            replayed,
            await ToDtoAsync(operation, cancellationToken));
    }

    public async Task DeleteLegacyAsync(
        string userId,
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        var project = await _projectRepository.GetByIdIncludingDeletedAsync(projectId);
        if (project == null)
        {
            return;
        }

        await DeleteAsync(
            userId,
            projectId,
            new DeleteProjectRequest
            {
                ClientOperationId = Guid.NewGuid(),
                ExpectedPersistenceRevision = project.PersistenceRevision
            },
            waitForCleanup: true,
            cancellationToken);
    }

    public async Task<ProjectLifecycleOperationDto> GetOperationAsync(
        string userId,
        Guid clientOperationId,
        ProjectLifecycleOperationKind kind,
        CancellationToken cancellationToken = default)
    {
        var operation = await _operationRepository.GetAsync(
            NormalizeUserId(userId),
            kind,
            clientOperationId,
            cancellationToken);
        if (operation == null || operation.ExpiresAtUtc <= DateTimeOffset.UtcNow)
        {
            throw new ProjectOperationNotFoundException(clientOperationId);
        }

        return await ToDtoAsync(operation, cancellationToken);
    }

    public async Task RunRecoveryAndRetentionAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var recoverable = await _operationRepository.GetRecoverableAsync(now, 100, cancellationToken);
        foreach (var operation in recoverable)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (operation.Status is ProjectLifecycleOperationStatus.Pending or ProjectLifecycleOperationStatus.FailedRetryable)
                {
                    await ExecuteSingleFlightAsync(
                        operation,
                        operation.Kind switch
                        {
                            ProjectLifecycleOperationKind.Create =>
                                () => ExecuteCreateAsync(operation.Id, cancellationToken),
                            ProjectLifecycleOperationKind.Import =>
                                () => ExecuteImportAsync(operation.Id, cancellationToken),
                            _ => () => ExecuteDeleteAsync(operation.Id, cancellationToken)
                        });
                }

                var current = await _operationRepository.GetByIdAsync(operation.Id, cancellationToken);
                if (current is
                    {
                        Kind: ProjectLifecycleOperationKind.Delete,
                        Status: ProjectLifecycleOperationStatus.Completed,
                        CleanupStatus: ProjectLifecycleCleanupStatus.CleanupPending or
                            ProjectLifecycleCleanupStatus.CleanupFailedRetryable
                    })
                {
                    await ProcessCleanupAsync(current.Id, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(
                    ex,
                    "Project lifecycle recovery deferred operation {OperationId} ({Kind}).",
                    operation.Id,
                    operation.Kind);
            }
        }

        await _operationRepository.DeleteExpiredAsync(now, cancellationToken);
    }

    internal Task<ProjectLifecycleOperation> RetryCleanupAsync(
        Guid operationId,
        CancellationToken cancellationToken = default) =>
        ProcessCleanupAsync(operationId, cancellationToken);

    internal static void ResetInFlightForTests() => InFlight.Clear();

    private async Task<ProjectLifecycleOperation> ExecuteCreateAsync(
        Guid operationId,
        CancellationToken cancellationToken)
    {
        var operation = await RequireOperationAsync(operationId, cancellationToken);
        if (operation.Status == ProjectLifecycleOperationStatus.Completed)
        {
            return operation;
        }

        if (operation.Status == ProjectLifecycleOperationStatus.FailedTerminal)
        {
            return operation;
        }

        try
        {
            var existingProject = await _projectRepository.GetByIdIncludingDeletedAsync(operation.ProjectId);
            if (existingProject != null)
            {
                var existingDto = await _projectService.GetByIdAsync(existingProject.Id);
                var result = new ProjectLifecycleOperationResultDto
                {
                    Project = existingDto ?? DeserializeResult(operation).Project,
                    ProjectDeleted = existingProject.IsDeleted
                };
                operation.CompleteCreate(
                    JsonSerializer.Serialize(result, JsonOptions),
                    DateTimeOffset.UtcNow,
                    DateTimeOffset.UtcNow.Add(CreateOperationRetention));
                await _operationRepository.UpdateAsync(operation, cancellationToken);
                return operation;
            }

            await _projectService.CreateBlankFromOperationAsync(
                operation,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow.Add(CreateOperationRetention));
            return operation;
        }
        catch (ProjectLifecycleValidationException ex)
        {
            await MarkTerminalAsync(operation, ex.ErrorCode, CreateOperationRetention, cancellationToken);
            throw;
        }
        catch (Exception ex) when (ex is not ProjectOperationRetryableException)
        {
            await MarkRetryableAsync(operation, "PROJECT_OPERATION_RETRYABLE", cancellationToken);
            throw new ProjectOperationRetryableException(
                operation.ClientOperationId,
                "PROJECT_OPERATION_RETRYABLE",
                ex);
        }
    }

    private async Task<ProjectLifecycleOperation> ExecuteDeleteAsync(
        Guid operationId,
        CancellationToken cancellationToken)
    {
        var operation = await RequireOperationAsync(operationId, cancellationToken);
        if (operation.Status == ProjectLifecycleOperationStatus.Completed ||
            operation.Status == ProjectLifecycleOperationStatus.FailedTerminal)
        {
            return operation;
        }

        await using var mutationLease = await _runtimeCoordinator.TryAcquireMutationLeaseAsync(
            operation.ProjectId,
            "project-lifecycle-delete",
            cancellationToken);
        if (mutationLease == null)
        {
            await MarkRetryableAsync(operation, "PROJECT_MUTATION_CONFLICT", cancellationToken);
            throw new ProjectMutationConflictException(operation.ProjectId);
        }

        try
        {
            var existing = await _projectRepository.GetByIdIncludingDeletedAsync(operation.ProjectId);
            if (existing == null)
            {
                await MarkTerminalAsync(operation, "PROJECT_NOT_FOUND", DeleteOperationRetention, cancellationToken);
                throw new ProjectNotFoundException(operation.ProjectId);
            }

            if (existing.IsDeleted)
            {
                var authority = await _operationRepository.GetDeleteAuthorityAsync(
                    operation.ProjectId,
                    cancellationToken);
                var result = new ProjectLifecycleOperationResultDto
                {
                    Deleted = true,
                    AlreadyDeleted = true,
                    CleanupStatus = ToCleanupStatus(
                        authority?.CleanupStatus ?? ProjectLifecycleCleanupStatus.CleanupPending)
                };
                if (authority != null)
                {
                    operation.CompleteDeleteReplay(
                        JsonSerializer.Serialize(result, JsonOptions),
                        authority.Id,
                        authority.CleanupStatus,
                        DateTimeOffset.UtcNow,
                        DateTimeOffset.UtcNow.Add(DeleteOperationRetention));
                }
                else
                {
                    operation.CompleteDelete(
                        JsonSerializer.Serialize(result, JsonOptions),
                        DateTimeOffset.UtcNow,
                        DateTimeOffset.UtcNow.Add(DeleteOperationRetention));
                }

                await _operationRepository.UpdateAsync(operation, cancellationToken);
                return operation;
            }

            await _projectService.TombstoneFromOperationAsync(
                operation,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow.Add(DeleteOperationRetention));
            return operation;
        }
        catch (ProjectMutationConflictException)
        {
            await MarkRetryableAsync(operation, "PROJECT_MUTATION_CONFLICT", cancellationToken);
            throw;
        }
        catch (ProjectRevisionConflictException ex)
        {
            await MarkTerminalAsync(operation, ex.ErrorCode, DeleteOperationRetention, cancellationToken);
            throw;
        }
        catch (ProjectNotFoundException ex)
        {
            await MarkTerminalAsync(operation, ex.ErrorCode, DeleteOperationRetention, cancellationToken);
            throw;
        }
        catch (Exception ex) when (ex is not ProjectOperationRetryableException)
        {
            await MarkRetryableAsync(operation, "PROJECT_OPERATION_RETRYABLE", cancellationToken);
            throw new ProjectOperationRetryableException(
                operation.ClientOperationId,
                "PROJECT_OPERATION_RETRYABLE",
                ex);
        }
    }

    private async Task<ProjectLifecycleOperation> ExecuteImportAsync(
        Guid operationId,
        CancellationToken cancellationToken)
    {
        var operation = await RequireOperationAsync(operationId, cancellationToken);
        if (operation.Status is ProjectLifecycleOperationStatus.Completed or ProjectLifecycleOperationStatus.FailedTerminal)
        {
            return operation;
        }

        try
        {
            var payload = DeserializeImportPayload(operation);
            var existing = await _projectRepository.GetByIdIncludingDeletedAsync(operation.ProjectId);
            var expectedRevision = operation.ExpectedPersistenceRevision ?? 0;
            if (existing != null &&
                !existing.IsDeleted &&
                existing.PersistenceRevision == expectedRevision + 1 &&
                await _projectService.IsImportAppliedAsync(operation.ProjectId, payload.Document, expectedRevision))
            {
                var current = await _projectService.GetByIdAsync(operation.ProjectId)
                    ?? throw new ProjectNotFoundException(operation.ProjectId);
                CompleteImportOperation(operation, current);
                await _operationRepository.UpdateAsync(operation, cancellationToken);
                return operation;
            }

            await using var mutationLease = await _runtimeCoordinator.TryAcquireMutationLeaseAsync(
                operation.ProjectId,
                "project-lifecycle-import",
                cancellationToken);
            if (mutationLease == null)
            {
                await MarkRetryableAsync(operation, "PROJECT_MUTATION_CONFLICT", cancellationToken);
                throw new ProjectMutationConflictException(operation.ProjectId);
            }

            ProjectDto project;
            if (payload.Mode == ProjectImportMode.CreateNew)
            {
                project = await _projectService.CreateImportedFromOperationAsync(operation, payload.Document);
            }
            else
            {
                if (existing == null || existing.IsDeleted)
                {
                    throw new ProjectNotFoundException(operation.ProjectId);
                }

                project = await _projectService.ApplyImportAsync(
                    operation.ProjectId,
                    payload.Document,
                    expectedRevision);
            }

            CompleteImportOperation(operation, project);
            await _operationRepository.UpdateAsync(operation, cancellationToken);
            return operation;
        }
        catch (ProjectMutationConflictException)
        {
            await MarkRetryableAsync(operation, "PROJECT_MUTATION_CONFLICT", cancellationToken);
            throw;
        }
        catch (ProjectSaveRevisionConflictException ex)
        {
            await MarkTerminalAsync(operation, "PROJECT_REVISION_CONFLICT", ImportOperationRetention, cancellationToken);
            throw new ProjectRevisionConflictException(
                operation.ProjectId,
                operation.ExpectedPersistenceRevision ?? -1,
                ex.ActualRevision);
        }
        catch (ProjectRevisionConflictException ex)
        {
            await MarkTerminalAsync(operation, ex.ErrorCode, ImportOperationRetention, cancellationToken);
            throw;
        }
        catch (ProjectNotFoundException ex)
        {
            await MarkTerminalAsync(operation, ex.ErrorCode, ImportOperationRetention, cancellationToken);
            throw;
        }
        catch (ProjectLifecycleValidationException ex)
        {
            await MarkTerminalAsync(operation, ex.ErrorCode, ImportOperationRetention, cancellationToken);
            throw;
        }
        catch (Exception ex) when (ex is not ProjectOperationRetryableException)
        {
            await MarkRetryableAsync(operation, "PROJECT_OPERATION_RETRYABLE", cancellationToken);
            throw new ProjectOperationRetryableException(
                operation.ClientOperationId,
                "PROJECT_OPERATION_RETRYABLE",
                ex);
        }
    }

    private static void CompleteImportOperation(ProjectLifecycleOperation operation, ProjectDto project)
    {
        operation.CompleteImport(
            JsonSerializer.Serialize(new ProjectLifecycleOperationResultDto { Project = project }, JsonOptions),
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.Add(ImportOperationRetention));
    }

    private static ProjectImportExecutionPayload DeserializeImportPayload(ProjectLifecycleOperation operation)
    {
        if (string.IsNullOrWhiteSpace(operation.CommandPayloadJson))
        {
            throw new ProjectLifecycleValidationException(
                "PROJECT_IMPORT_PAYLOAD_MISSING",
                "Project import operation payload is missing.");
        }

        return JsonSerializer.Deserialize<ProjectImportExecutionPayload>(operation.CommandPayloadJson, JsonOptions)
            ?? throw new ProjectLifecycleValidationException(
                "PROJECT_IMPORT_PAYLOAD_INVALID",
                "Project import operation payload is invalid.");
    }

    private async Task<ProjectLifecycleOperation> ProcessCleanupAsync(
        Guid operationId,
        CancellationToken cancellationToken)
    {
        var operation = await RequireOperationAsync(operationId, cancellationToken);
        if (operation.CleanupAuthorityOperationId != null)
        {
            return operation;
        }
        if (operation.CleanupStatus == ProjectLifecycleCleanupStatus.CleanupCompleted)
        {
            return operation;
        }

        try
        {
            await _projectService.CleanupDeletedProjectAsync(operation.ProjectId);
            operation.MarkCleanupCompleted(
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow.Add(DeleteOperationRetention));
            await _operationRepository.UpdateAsync(operation, cancellationToken);
        }
        catch (Exception ex)
        {
            var delaySeconds = Math.Min(300, Math.Max(5, 5 * (operation.CleanupAttemptCount + 1)));
            operation.MarkCleanupFailedRetryable(
                "PROJECT_CLEANUP_RETRYABLE",
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow.AddSeconds(delaySeconds));
            await _operationRepository.UpdateAsync(operation, cancellationToken);
            _logger?.LogWarning(
                ex,
                "Project cleanup will retry for project {ProjectId}, operation {OperationId}.",
                operation.ProjectId,
                operation.ClientOperationId);
        }

        return operation;
    }

    private async Task<ProjectLifecycleOperationDto> ToDtoAsync(
        ProjectLifecycleOperation operation,
        CancellationToken cancellationToken)
    {
        var result = operation.ResultJson == null ? null : DeserializeResult(operation);
        if (operation.Kind == ProjectLifecycleOperationKind.Create &&
            operation.Status == ProjectLifecycleOperationStatus.Completed)
        {
            var project = await _projectRepository.GetByIdIncludingDeletedAsync(operation.ProjectId);
            if (result != null)
            {
                result.ProjectDeleted = project?.IsDeleted ?? true;
            }
        }

        if (operation.Kind == ProjectLifecycleOperationKind.Delete && result != null)
        {
            var cleanupAuthority = operation.CleanupAuthorityOperationId is { } authorityId
                ? await _operationRepository.GetByIdAsync(authorityId, cancellationToken)
                : operation;
            result.CleanupStatus = ToCleanupStatus(cleanupAuthority?.CleanupStatus ?? operation.CleanupStatus);
        }

        return new ProjectLifecycleOperationDto
        {
            ClientOperationId = operation.ClientOperationId,
            Kind = operation.Kind switch
            {
                ProjectLifecycleOperationKind.Create => "create",
                ProjectLifecycleOperationKind.Delete => "delete",
                ProjectLifecycleOperationKind.Import => "import",
                _ => throw new ArgumentOutOfRangeException(nameof(operation.Kind), operation.Kind, null)
            },
            Status = ToOperationStatus(operation.Status),
            ProjectId = operation.ProjectId,
            Result = result,
            ErrorCode = operation.ErrorCode,
            CreatedAtUtc = operation.CreatedAtUtc,
            UpdatedAtUtc = operation.UpdatedAtUtc,
            ExpiresAtUtc = operation.ExpiresAtUtc
        };
    }

    private static ProjectLifecycleOperationResultDto DeserializeResult(ProjectLifecycleOperation operation)
    {
        if (string.IsNullOrWhiteSpace(operation.ResultJson))
        {
            return new ProjectLifecycleOperationResultDto();
        }

        return JsonSerializer.Deserialize<ProjectLifecycleOperationResultDto>(operation.ResultJson, JsonOptions)
            ?? new ProjectLifecycleOperationResultDto();
    }

    private async Task<ProjectLifecycleOperation> RequireOperationAsync(
        Guid operationId,
        CancellationToken cancellationToken)
    {
        return await _operationRepository.GetByIdAsync(operationId, cancellationToken)
            ?? throw new InvalidOperationException($"Project lifecycle operation '{operationId}' disappeared.");
    }

    private async Task MarkRetryableAsync(
        ProjectLifecycleOperation operation,
        string errorCode,
        CancellationToken cancellationToken)
    {
        operation.MarkFailedRetryable(
            errorCode,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.AddSeconds(5));
        await _operationRepository.UpdateAsync(operation, cancellationToken);
    }

    private async Task MarkTerminalAsync(
        ProjectLifecycleOperation operation,
        string errorCode,
        TimeSpan retention,
        CancellationToken cancellationToken)
    {
        operation.MarkFailedTerminal(
            errorCode,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.Add(retention));
        await _operationRepository.UpdateAsync(operation, cancellationToken);
    }

    private static void EnsureFingerprint(ProjectLifecycleOperation operation, string fingerprint)
    {
        if (!operation.MatchesFingerprint(fingerprint))
        {
            throw new ProjectOperationPayloadMismatchException(operation.ClientOperationId);
        }
    }

    private static void EnsureNotExpired(ProjectLifecycleOperation? operation, Guid clientOperationId)
    {
        if (operation?.ExpiresAtUtc <= DateTimeOffset.UtcNow)
        {
            throw new ProjectOperationNotFoundException(clientOperationId);
        }
    }

    private static void ThrowIfFailed(ProjectLifecycleOperation operation)
    {
        if (operation.Status == ProjectLifecycleOperationStatus.FailedRetryable)
        {
            if (operation.ErrorCode == "PROJECT_MUTATION_CONFLICT")
            {
                throw new ProjectMutationConflictException(operation.ProjectId);
            }

            throw new ProjectOperationRetryableException(
                operation.ClientOperationId,
                operation.ErrorCode ?? "PROJECT_OPERATION_RETRYABLE");
        }

        if (operation.Status != ProjectLifecycleOperationStatus.FailedTerminal)
        {
            return;
        }

        throw operation.ErrorCode switch
        {
            "PROJECT_NOT_FOUND" => new ProjectNotFoundException(operation.ProjectId),
            "PROJECT_REVISION_CONFLICT" => new ProjectRevisionConflictException(
                operation.ProjectId,
                operation.ExpectedPersistenceRevision ?? -1,
                operation.ExpectedPersistenceRevision ?? -1),
            _ => new ProjectLifecycleValidationException(
                operation.ErrorCode ?? "PROJECT_OPERATION_FAILED",
                "Project lifecycle operation failed terminally.")
        };
    }

    private static async Task<ProjectLifecycleOperation> ExecuteSingleFlightAsync(
        ProjectLifecycleOperation operation,
        Func<Task<ProjectLifecycleOperation>> execute)
    {
        if (operation.Status is ProjectLifecycleOperationStatus.Completed or ProjectLifecycleOperationStatus.FailedTerminal)
        {
            return operation;
        }

        var key = new OperationKey(operation.UserId, operation.Kind, operation.ClientOperationId);
        var candidate = new Lazy<Task<ProjectLifecycleOperation>>(execute, LazyThreadSafetyMode.ExecutionAndPublication);
        var current = InFlight.GetOrAdd(key, candidate);
        try
        {
            return await current.Value;
        }
        finally
        {
            if (current.IsValueCreated && current.Value.IsCompleted)
            {
                ((ICollection<KeyValuePair<OperationKey, Lazy<Task<ProjectLifecycleOperation>>>>)InFlight)
                    .Remove(new KeyValuePair<OperationKey, Lazy<Task<ProjectLifecycleOperation>>>(key, current));
            }
        }
    }

    private static string NormalizeUserId(string userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ProjectLifecycleValidationException(
                "PROJECT_VALIDATION_USER_REQUIRED",
                "Authenticated user identity is required.");
        }

        return userId.Trim();
    }

    private static string NormalizeName(string name)
    {
        var normalized = name?.Trim() ?? string.Empty;
        if (normalized.Length == 0)
        {
            throw new ProjectLifecycleValidationException(
                "PROJECT_VALIDATION_NAME_REQUIRED",
                "Project name is required.");
        }

        if (normalized.Length > 200)
        {
            throw new ProjectLifecycleValidationException(
                "PROJECT_VALIDATION_NAME_TOO_LONG",
                "Project name cannot exceed 200 characters.");
        }

        return normalized;
    }

    private static string? NormalizeDescription(string? description)
    {
        var normalized = description?.Trim();
        if (string.IsNullOrEmpty(normalized))
        {
            return null;
        }

        if (normalized.Length > 1000)
        {
            throw new ProjectLifecycleValidationException(
                "PROJECT_VALIDATION_DESCRIPTION_TOO_LONG",
                "Project description cannot exceed 1000 characters.");
        }

        return normalized;
    }

    private static ProjectImportMode NormalizeImportMode(string? mode)
    {
        return mode?.Trim().ToUpperInvariant() switch
        {
            "CREATE_NEW" => ProjectImportMode.CreateNew,
            "OVERWRITE_EXISTING" => ProjectImportMode.OverwriteExisting,
            _ => throw new ProjectLifecycleValidationException(
                "PROJECT_IMPORT_MODE_INVALID",
                "Project import mode must be CREATE_NEW or OVERWRITE_EXISTING.")
        };
    }

    private static string ComputeCreateFingerprint(string name, string? description) =>
        ComputeFingerprint(JsonSerializer.Serialize(new
        {
            version = PayloadFingerprintVersion,
            name,
            description
        }, JsonOptions));

    private static string ComputeDeleteFingerprint(Guid projectId, long expectedPersistenceRevision) =>
        ComputeFingerprint(JsonSerializer.Serialize(new
        {
            version = PayloadFingerprintVersion,
            projectId = projectId.ToString("D").ToLowerInvariant(),
            expectedPersistenceRevision
        }, JsonOptions));

    private static string ComputeImportFingerprint(
        ProjectImportMode mode,
        Guid? targetProjectId,
        long? expectedPersistenceRevision,
        string documentJson) =>
        ComputeFingerprint(JsonSerializer.Serialize(new
        {
            version = PayloadFingerprintVersion,
            mode = mode.ToString(),
            targetProjectId = targetProjectId?.ToString("D").ToLowerInvariant(),
            expectedPersistenceRevision,
            documentJson
        }, JsonOptions));

    private static string ComputeFingerprint(string canonicalJson)
    {
        var bytes = Encoding.UTF8.GetBytes(canonicalJson);
        return "sha256:" + Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    private static string ToOperationStatus(ProjectLifecycleOperationStatus status) => status switch
    {
        ProjectLifecycleOperationStatus.Pending => "pending",
        ProjectLifecycleOperationStatus.Completed => "completed",
        ProjectLifecycleOperationStatus.FailedRetryable => "failed-retryable",
        ProjectLifecycleOperationStatus.FailedTerminal => "failed-terminal",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null)
    };

    private static string ToCleanupStatus(ProjectLifecycleCleanupStatus status) => status switch
    {
        ProjectLifecycleCleanupStatus.NotApplicable => "not-applicable",
        ProjectLifecycleCleanupStatus.CleanupPending => "cleanup-pending",
        ProjectLifecycleCleanupStatus.CleanupCompleted => "cleanup-completed",
        ProjectLifecycleCleanupStatus.CleanupFailedRetryable => "cleanup-failed-retryable",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null)
    };

    private readonly record struct OperationKey(
        string UserId,
        ProjectLifecycleOperationKind Kind,
        Guid ClientOperationId);
}
