using System.Text.Json;
using ClearVision.Product.Application.DTOs;
using ClearVision.Product.Core.Cameras;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace ClearVision.Product.Desktop.Services;

/// <summary>
/// Serializes camera save/reset operations. Lock order is always camera operation gate,
/// then the AppConfig mutation gate owned by IConfigurationService.
/// </summary>
public sealed class CameraConfigurationCoordinator
{
    public const string ErrorRuntimeConflict = "CAMERA_RUNTIME_CONFLICT";
    public const string ErrorValidation = "CAMERA_BINDINGS_VALIDATION_FAILED";

    private static readonly JsonSerializerOptions CloneOptions = new(JsonSerializerDefaults.Web);
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private readonly IConfigurationService _configurationService;
    private readonly ICameraManager _cameraManager;
    private readonly ICameraFrameStreamCoordinator _streamCoordinator;
    private readonly ISerialPhotoelectricTriggerInputService _serialTriggerService;
    private readonly ILogger<CameraConfigurationCoordinator> _logger;

    public CameraConfigurationCoordinator(
        IConfigurationService configurationService,
        ICameraManager cameraManager,
        ICameraFrameStreamCoordinator streamCoordinator,
        ISerialPhotoelectricTriggerInputService serialTriggerService,
        ILogger<CameraConfigurationCoordinator> logger)
    {
        _configurationService = configurationService;
        _cameraManager = cameraManager;
        _streamCoordinator = streamCoordinator;
        _serialTriggerService = serialTriggerService;
        _logger = logger;
    }

    public Task<CameraConfigurationResult> SaveAsync(
        UpdateCameraBindingsRequest request,
        CancellationToken cancellationToken = default) =>
        SaveCoreAsync(request, resetAllAppConfig: false, cancellationToken);

    private async Task<CameraConfigurationResult> SaveCoreAsync(
        UpdateCameraBindingsRequest request,
        bool resetAllAppConfig,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!request.ExpectedRevision.HasValue)
        {
            return CameraConfigurationResult.Validation(
                "APP_CONFIG_EXPECTED_REVISION_REQUIRED",
                "expectedRevision is required.",
                [new AppConfigValidationError("expectedRevision", "expectedRevision is required.")]);
        }

        var normalizedBindings = CloneAndNormalize(request.Bindings);
        var normalizedActiveCameraId = NormalizeActiveCameraId(normalizedBindings, request.ActiveCameraId);
        var requestErrors = ValidateBindings(normalizedBindings, request.ActiveCameraId);
        if (requestErrors.Count > 0)
        {
            return CameraConfigurationResult.Validation(ErrorValidation, "Camera binding validation failed.", requestErrors);
        }

        await _operationGate.WaitAsync(cancellationToken);
        try
        {
            var read = await _configurationService.ReadAsync(cancellationToken);
            if (!read.IsHealthy || read.Config == null)
            {
                return CameraConfigurationResult.FromReadFailure(read);
            }

            var previousBindings = CloneAndNormalize(read.Config.Cameras);
            var affectedBindingIds = GetAffectedBindingIds(previousBindings, normalizedBindings);
            var conflicts = FindRuntimeConflicts(previousBindings, affectedBindingIds);
            if (conflicts.Count > 0)
            {
                return CameraConfigurationResult.RuntimeConflict(conflicts, read.Config.Revision);
            }

            var mutation = await _configurationService.MutateAndApplyAsync(
                request.ExpectedRevision.Value,
                candidate =>
                {
                    if (resetAllAppConfig)
                    {
                        CopyDefaults(candidate);
                    }
                    else
                    {
                        candidate.Cameras = CloneAndNormalize(normalizedBindings);
                        candidate.ActiveCameraId = normalizedActiveCameraId;
                    }
                },
                candidate => ValidateBindings(candidate.Cameras, candidate.ActiveCameraId),
                async (candidate, ct) =>
                {
                    await ReleaseRetiredStreamsAsync(affectedBindingIds);
                    await _cameraManager.ApplyBindingsAsync(CloneAndNormalize(candidate.Cameras), candidate.ActiveCameraId);
                    _serialTriggerService.ConfigureBindings(candidate.Cameras);
                },
                async (previous, ct) =>
                {
                    await ReleaseRetiredStreamsAsync(affectedBindingIds);
                    await _cameraManager.ApplyBindingsAsync(CloneAndNormalize(previous.Cameras), previous.ActiveCameraId);
                    _serialTriggerService.ConfigureBindings(previous.Cameras);
                },
                cancellationToken);

            if (!mutation.IsSuccess)
            {
                return CameraConfigurationResult.FromMutation(mutation);
            }

            var committed = mutation.Config!;
            _logger.LogInformation(
                "Camera configuration committed. Revision={Revision}, BindingCount={BindingCount}, NoOp={NoOp}",
                committed.Revision,
                committed.Cameras.Count,
                mutation.IsNoOp);
            return CameraConfigurationResult.Success(mutation);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public Task<CameraConfigurationResult> ResetAsync(long? expectedRevision, CancellationToken cancellationToken = default)
    {
        return SaveCoreAsync(
            new UpdateCameraBindingsRequest
            {
                ExpectedRevision = expectedRevision,
                Bindings = new List<CameraBindingConfig>(),
                ActiveCameraId = string.Empty
            },
            resetAllAppConfig: true,
            cancellationToken);
    }

    private IReadOnlyList<CameraRuntimeConflict> FindRuntimeConflicts(
        IReadOnlyList<CameraBindingConfig> previousBindings,
        IReadOnlySet<string> affectedBindingIds)
    {
        var conflicts = new List<CameraRuntimeConflict>();
        foreach (var binding in previousBindings.Where(binding => affectedBindingIds.Contains(binding.Id)))
        {
            var usage = _streamCoordinator.SnapshotStreamUsage(binding.Id);
            var directAcquisition = !string.IsNullOrWhiteSpace(binding.SerialNumber) &&
                _cameraManager.GetCamera(binding.SerialNumber)?.IsAcquiring == true;
            if (usage?.IsRunning != true && !directAcquisition)
            {
                continue;
            }

            conflicts.Add(new CameraRuntimeConflict(
                binding.Id,
                binding.DisplayName,
                usage?.LeaseCount ?? 0,
                usage?.PreviewSessionCount ?? 0,
                usage?.PendingFrameWaiters ?? 0,
                directAcquisition));
        }

        return conflicts;
    }

    private async Task ReleaseRetiredStreamsAsync(IEnumerable<string> bindingIds)
    {
        foreach (var bindingId in bindingIds)
        {
            await _streamCoordinator.ReleaseIdleStreamAsync(bindingId);
        }
    }

    private static HashSet<string> GetAffectedBindingIds(
        IReadOnlyList<CameraBindingConfig> previous,
        IReadOnlyList<CameraBindingConfig> next)
    {
        var nextById = next.ToDictionary(binding => binding.Id, StringComparer.OrdinalIgnoreCase);
        return previous
            .Where(binding =>
                !nextById.TryGetValue(binding.Id, out var replacement) ||
                HasRuntimeSettingsChanged(binding, replacement))
            .Select(binding => binding.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static bool HasRuntimeSettingsChanged(CameraBindingConfig previous, CameraBindingConfig next)
    {
        var previousJson = JsonSerializer.Serialize(previous, CloneOptions);
        var nextJson = JsonSerializer.Serialize(next, CloneOptions);
        return !string.Equals(previousJson, nextJson, StringComparison.Ordinal);
    }

    private static IReadOnlyList<AppConfigValidationError> ValidateBindings(
        IReadOnlyList<CameraBindingConfig> bindings,
        string? requestedActiveCameraId)
    {
        var errors = new List<AppConfigValidationError>();
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < bindings.Count; index++)
        {
            var binding = bindings[index];
            if (!ids.Add(binding.Id))
            {
                errors.Add(new AppConfigValidationError($"cameras[{index}].id", "Camera binding Ids must be unique."));
            }

            if (binding.ExposureTimeUs <= 0)
            {
                errors.Add(new AppConfigValidationError($"cameras[{index}].exposureTimeUs", "Exposure time must be greater than zero."));
            }

            if (binding.GainDb < 0)
            {
                errors.Add(new AppConfigValidationError($"cameras[{index}].gainDb", "Gain cannot be negative."));
            }

            if (binding.UsesSerialPhotoelectricTrigger() &&
                !System.Text.RegularExpressions.Regex.IsMatch(binding.SerialPhotoelectricPortName, "^COM[0-9]+$", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            {
                errors.Add(new AppConfigValidationError(
                    $"cameras[{index}].serialPhotoelectricPortName",
                    "Serial photoelectric trigger requires a COM port such as COM3."));
            }
        }

        if (!string.IsNullOrWhiteSpace(requestedActiveCameraId) &&
            !bindings.Any(binding => binding.Id.Equals(requestedActiveCameraId.Trim(), StringComparison.OrdinalIgnoreCase)))
        {
            errors.Add(new AppConfigValidationError("activeCameraId", "Active camera Id must reference an existing binding."));
        }

        return errors;
    }

    private static string NormalizeActiveCameraId(IReadOnlyList<CameraBindingConfig> bindings, string? activeCameraId)
    {
        var normalized = activeCameraId?.Trim() ?? string.Empty;
        return bindings.Any(binding => binding.Id.Equals(normalized, StringComparison.OrdinalIgnoreCase))
            ? bindings.First(binding => binding.Id.Equals(normalized, StringComparison.OrdinalIgnoreCase)).Id
            : bindings.FirstOrDefault()?.Id ?? string.Empty;
    }

    private static List<CameraBindingConfig> CloneAndNormalize(IEnumerable<CameraBindingConfig>? bindings)
    {
        var json = JsonSerializer.Serialize(bindings ?? Enumerable.Empty<CameraBindingConfig>(), CloneOptions);
        var clone = JsonSerializer.Deserialize<List<CameraBindingConfig>>(json, CloneOptions) ?? new List<CameraBindingConfig>();
        foreach (var binding in clone)
        {
            binding.Normalize();
        }

        return clone;
    }

    private static void CopyDefaults(AppConfig candidate)
    {
        var json = JsonSerializer.Serialize(new AppConfig(), CloneOptions);
        var defaults = JsonSerializer.Deserialize<AppConfig>(json, CloneOptions) ?? new AppConfig();
        defaults.Normalize();
        candidate.General = defaults.General;
        candidate.Communication = defaults.Communication;
        candidate.TcpCommunication = defaults.TcpCommunication;
        candidate.Storage = defaults.Storage;
        candidate.Runtime = defaults.Runtime;
        candidate.Features = defaults.Features;
        candidate.Cameras = defaults.Cameras;
        candidate.Security = defaults.Security;
        candidate.ActiveCameraId = defaults.ActiveCameraId;
    }
}

public sealed record CameraRuntimeConflict(
    string CameraBindingId,
    string DisplayName,
    int LeaseCount,
    int PreviewSessionCount,
    int PendingFrameWaiters,
    bool DirectAcquisition);

public sealed record CameraConfigurationResult(
    bool IsSuccess,
    string? ErrorCode,
    string? Message,
    AppConfigMutationResult? Mutation,
    IReadOnlyList<AppConfigValidationError> ValidationErrors,
    IReadOnlyList<CameraRuntimeConflict> RuntimeConflicts,
    long? Revision,
    AppConfigReadStatus? ReadStatus = null,
    bool HasLastGood = false)
{
    public static CameraConfigurationResult Success(AppConfigMutationResult mutation) =>
        new(true, null, null, mutation, Array.Empty<AppConfigValidationError>(), Array.Empty<CameraRuntimeConflict>(), mutation.ActualRevision);

    public static CameraConfigurationResult Validation(
        string errorCode,
        string message,
        IReadOnlyList<AppConfigValidationError> errors) =>
        new(false, errorCode, message, null, errors, Array.Empty<CameraRuntimeConflict>(), null);

    public static CameraConfigurationResult RuntimeConflict(IReadOnlyList<CameraRuntimeConflict> conflicts, long revision) =>
        new(false, CameraConfigurationCoordinator.ErrorRuntimeConflict,
            "Camera preview or acquisition is active. Stop it before changing the affected bindings.",
            null, Array.Empty<AppConfigValidationError>(), conflicts, revision);

    public static CameraConfigurationResult FromReadFailure(AppConfigReadResult read) =>
        new(false, read.ErrorCode, read.Message, null, Array.Empty<AppConfigValidationError>(), Array.Empty<CameraRuntimeConflict>(),
            read.Config?.Revision, read.Status, read.HasLastGood);

    public static CameraConfigurationResult FromMutation(AppConfigMutationResult mutation) =>
        new(false, mutation.ErrorCode, mutation.Message, mutation,
            mutation.ValidationErrors ?? Array.Empty<AppConfigValidationError>(),
            Array.Empty<CameraRuntimeConflict>(), mutation.ActualRevision);
}
