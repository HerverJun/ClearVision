using System.Text;
using System.Text.Json;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace ClearVision.Product.Infrastructure.Services;

/// <summary>
/// The single mutation authority for the process-wide AppConfig document.
/// The async gate covers authoritative reload, revision CAS, patching, validation,
/// durable replacement, and (when requested) dependent runtime application.
/// </summary>
public sealed class JsonConfigurationService : IConfigurationService
{
    public const string ErrorMalformed = "APP_CONFIG_MALFORMED";
    public const string ErrorEmpty = "APP_CONFIG_EMPTY";
    public const string ErrorAccessDenied = "APP_CONFIG_ACCESS_DENIED";
    public const string ErrorLocked = "APP_CONFIG_LOCKED";
    public const string ErrorIo = "APP_CONFIG_IO_ERROR";
    public const string ErrorUnavailable = "APP_CONFIG_UNAVAILABLE";
    public const string ErrorRevisionConflict = "APP_CONFIG_REVISION_CONFLICT";
    public const string ErrorValidation = "APP_CONFIG_VALIDATION_FAILED";
    public const string ErrorPersist = "APP_CONFIG_PERSIST_FAILED";
    public const string ErrorApply = "APP_CONFIG_RUNTIME_APPLY_FAILED";
    public const string ErrorFenced = "APP_CONFIG_FENCED";

    private readonly string _configPath;
    private readonly ILogger<JsonConfigurationService> _logger;
    private readonly IAppConfigFileStore _fileStore;
    private readonly SemaphoreSlim _mutationGate = new(1, 1);
    private readonly object _cacheSync = new();
    private AppConfig? _lastGoodConfig;
    private bool _fenced;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public JsonConfigurationService(ILogger<JsonConfigurationService> logger)
        : this(logger, Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json"))
    {
    }

    public JsonConfigurationService(
        ILogger<JsonConfigurationService> logger,
        string configPath,
        IAppConfigFileStore? fileStore = null)
    {
        if (string.IsNullOrWhiteSpace(configPath))
        {
            throw new ArgumentException("Configuration path cannot be empty.", nameof(configPath));
        }

        _logger = logger;
        _configPath = configPath;
        _fileStore = fileStore ?? PhysicalAppConfigFileStore.Instance;
    }

    public async Task<AppConfigReadResult> ReadAsync(CancellationToken cancellationToken = default)
    {
        await _mutationGate.WaitAsync(cancellationToken);
        try
        {
            return await ReadAuthoritativeLockedAsync(cancellationToken);
        }
        finally
        {
            _mutationGate.Release();
        }
    }

    public async Task<AppConfig> LoadAsync()
    {
        var result = await ReadAsync();
        if (!result.IsHealthy || result.Config == null)
        {
            throw new AppConfigUnavailableException(result);
        }

        return Clone(result.Config);
    }

    public Task<AppConfigMutationResult> MutateAsync(
        long expectedRevision,
        Action<AppConfig> patch,
        Func<AppConfig, IReadOnlyList<AppConfigValidationError>>? validate = null,
        CancellationToken cancellationToken = default)
    {
        return MutateCoreAsync(
            expectedRevision,
            patch,
            validate,
            apply: null,
            rollbackApply: null,
            cancellationToken);
    }

    public Task<AppConfigMutationResult> MutateAndApplyAsync(
        long expectedRevision,
        Action<AppConfig> patch,
        Func<AppConfig, IReadOnlyList<AppConfigValidationError>>? validate,
        Func<AppConfig, CancellationToken, Task> apply,
        Func<AppConfig, CancellationToken, Task>? rollbackApply = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(apply);
        return MutateCoreAsync(
            expectedRevision,
            patch,
            validate,
            apply,
            rollbackApply,
            cancellationToken);
    }

    [Obsolete("Production writers must use revisioned MutateAsync.")]
    public async Task SaveAsync(AppConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        var read = await ReadAsync();
        if (!read.IsHealthy || read.Config == null)
        {
            throw new AppConfigUnavailableException(read);
        }

        var replacement = Clone(config);
        var result = await MutateAsync(read.Config.Revision, candidate => CopyConfig(replacement, candidate));
        if (!result.IsSuccess)
        {
            throw new IOException(result.Message ?? "Failed to save application configuration.");
        }
    }

    public AppConfig GetCurrent()
    {
        lock (_cacheSync)
        {
            if (_lastGoodConfig == null)
            {
                throw new AppConfigUnavailableException(new AppConfigReadResult(
                    AppConfigReadStatus.Unavailable,
                    null,
                    ErrorUnavailable,
                    "No last-good application configuration is available."));
            }

            return Clone(_lastGoodConfig);
        }
    }

    private async Task<AppConfigMutationResult> MutateCoreAsync(
        long expectedRevision,
        Action<AppConfig> patch,
        Func<AppConfig, IReadOnlyList<AppConfigValidationError>>? validate,
        Func<AppConfig, CancellationToken, Task>? apply,
        Func<AppConfig, CancellationToken, Task>? rollbackApply,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(patch);
        await _mutationGate.WaitAsync(cancellationToken);
        try
        {
            if (_fenced)
            {
                return Failure(
                    AppConfigMutationStatus.Fenced,
                    expectedRevision,
                    GetLastGoodOrNull(),
                    ErrorFenced,
                    "AppConfig mutations are fenced because a previous runtime apply could not be durably rolled back.");
            }

            var read = await ReadAuthoritativeLockedAsync(cancellationToken);
            if (!read.IsHealthy || read.Config == null)
            {
                return Failure(
                    AppConfigMutationStatus.StorageFailure,
                    expectedRevision,
                    read.Config,
                    read.ErrorCode ?? ErrorUnavailable,
                    read.Message ?? "The authoritative AppConfig could not be loaded.");
            }

            var previous = Clone(read.Config);
            if (expectedRevision != previous.Revision)
            {
                return Failure(
                    AppConfigMutationStatus.RevisionConflict,
                    expectedRevision,
                    previous,
                    ErrorRevisionConflict,
                    $"Expected AppConfig revision {expectedRevision}, but the authoritative revision is {previous.Revision}.");
            }

            var candidate = Clone(previous);
            patch(candidate);
            candidate = Normalize(candidate);
            candidate.Revision = previous.Revision;

            var validationErrors = validate?.Invoke(Clone(candidate)) ?? Array.Empty<AppConfigValidationError>();
            if (validationErrors.Count > 0)
            {
                return new AppConfigMutationResult(
                    AppConfigMutationStatus.ValidationFailed,
                    previous,
                    expectedRevision,
                    previous.Revision,
                    ErrorValidation,
                    "The AppConfig candidate failed validation.",
                    validationErrors);
            }

            if (ConfigEquals(previous, candidate))
            {
                if (apply != null)
                {
                    try
                    {
                        await apply(Clone(previous), cancellationToken);
                    }
                    catch (Exception applyException)
                    {
                        _logger.LogError(
                            applyException,
                            "Runtime reconciliation failed for unchanged AppConfig revision {Revision}.",
                            previous.Revision);

                        if (rollbackApply != null)
                        {
                            try
                            {
                                await rollbackApply(Clone(previous), CancellationToken.None);
                            }
                            catch (Exception rollbackRuntimeException)
                            {
                                _fenced = true;
                                _logger.LogCritical(
                                    rollbackRuntimeException,
                                    "Runtime rollback failed after unchanged AppConfig reconciliation. Mutations are fenced.");
                                return Failure(
                                    AppConfigMutationStatus.Fenced,
                                    expectedRevision,
                                    previous,
                                    ErrorFenced,
                                    "Runtime reconciliation and rollback failed; AppConfig mutations are fenced.");
                            }
                        }

                        return Failure(
                            AppConfigMutationStatus.ApplyFailed,
                            expectedRevision,
                            previous,
                            ErrorApply,
                            "Dependent runtime reconciliation failed for the unchanged AppConfig.");
                    }
                }

                return new AppConfigMutationResult(
                    AppConfigMutationStatus.NoChange,
                    previous,
                    expectedRevision,
                    previous.Revision);
            }

            try
            {
                candidate.Revision = checked(previous.Revision + 1);
                await PersistExactLockedAsync(candidate, cancellationToken);
            }
            catch (Exception ex) when (IsStorageException(ex))
            {
                _logger.LogError(ex, "Failed to persist AppConfig candidate. Revision remains {Revision}.", previous.Revision);
                return Failure(
                    AppConfigMutationStatus.StorageFailure,
                    expectedRevision,
                    previous,
                    ErrorPersist,
                    "The AppConfig candidate could not be durably persisted.");
            }

            SetLastGood(candidate);

            if (apply != null)
            {
                try
                {
                    await apply(Clone(candidate), cancellationToken);
                }
                catch (Exception applyException)
                {
                    _logger.LogError(
                        applyException,
                        "Runtime apply failed after AppConfig revision {Revision} was persisted; restoring revision {PreviousRevision}.",
                        candidate.Revision,
                        previous.Revision);

                    try
                    {
                        await PersistExactLockedAsync(previous, CancellationToken.None);
                        SetLastGood(previous);
                    }
                    catch (Exception rollbackPersistException) when (IsStorageException(rollbackPersistException))
                    {
                        _fenced = true;
                        _logger.LogCritical(
                            rollbackPersistException,
                            "AppConfig rollback failed after runtime apply failure. Mutations are fenced.");
                        return Failure(
                            AppConfigMutationStatus.Fenced,
                            expectedRevision,
                            previous,
                            ErrorFenced,
                            "Runtime apply failed and the previous AppConfig could not be durably restored; mutations are fenced.");
                    }

                    if (rollbackApply != null)
                    {
                        try
                        {
                            await rollbackApply(Clone(previous), CancellationToken.None);
                        }
                        catch (Exception rollbackRuntimeException)
                        {
                            _fenced = true;
                            _logger.LogCritical(
                                rollbackRuntimeException,
                                "Runtime rollback failed after the previous AppConfig was restored. Mutations are fenced.");
                            return Failure(
                                AppConfigMutationStatus.Fenced,
                                expectedRevision,
                                previous,
                                ErrorFenced,
                                "The previous AppConfig was restored, but runtime rollback failed; mutations are fenced.");
                        }
                    }

                    return Failure(
                        AppConfigMutationStatus.ApplyFailed,
                        expectedRevision,
                        previous,
                        ErrorApply,
                        "The AppConfig candidate was rolled back because dependent runtime apply failed.");
                }
            }

            return new AppConfigMutationResult(
                AppConfigMutationStatus.Applied,
                Clone(candidate),
                expectedRevision,
                candidate.Revision);
        }
        finally
        {
            _mutationGate.Release();
        }
    }

    private async Task<AppConfigReadResult> ReadAuthoritativeLockedAsync(CancellationToken cancellationToken)
    {
        if (_fenced)
        {
            return Degraded(
                ErrorFenced,
                "AppConfig is fenced after an incomplete runtime rollback.");
        }

        try
        {
            if (!_fileStore.Exists(_configPath))
            {
                var defaults = Normalize(new AppConfig());
                defaults.Revision = 0;
                await PersistExactLockedAsync(defaults, cancellationToken);
                SetLastGood(defaults);
                _logger.LogWarning("Configuration file was absent and initialized at {Path}.", _configPath);
                return new AppConfigReadResult(AppConfigReadStatus.Initialized, Clone(defaults));
            }

            var bytes = await _fileStore.ReadAllBytesAsync(_configPath, cancellationToken);
            if (bytes.Length == 0 || Encoding.UTF8.GetString(bytes).All(char.IsWhiteSpace))
            {
                return Degraded(ErrorEmpty, "The AppConfig file is empty.");
            }

            var loaded = JsonSerializer.Deserialize<AppConfig>(bytes, JsonOptions)
                ?? throw new JsonException("The AppConfig document contains JSON null.");
            loaded = Normalize(loaded);
            SetLastGood(loaded);
            _logger.LogInformation("Configuration loaded from {Path}. Revision={Revision}", _configPath, loaded.Revision);
            return new AppConfigReadResult(AppConfigReadStatus.Healthy, Clone(loaded));
        }
        catch (Exception ex) when (IsReadException(ex))
        {
            var (code, message) = ClassifyReadFailure(ex);
            _logger.LogError(ex, "Failed to reload authoritative AppConfig from {Path}. ErrorCode={ErrorCode}", _configPath, code);
            return Degraded(code, message);
        }
    }

    private async Task PersistExactLockedAsync(AppConfig config, CancellationToken cancellationToken)
    {
        var snapshot = Normalize(Clone(config));
        snapshot.Revision = config.Revision;
        var bytes = JsonSerializer.SerializeToUtf8Bytes(snapshot, JsonOptions);
        var candidatePath = $"{_configPath}.{Guid.NewGuid():N}.candidate";
        var directory = Path.GetDirectoryName(_configPath);

        try
        {
            if (!string.IsNullOrWhiteSpace(directory))
            {
                _fileStore.CreateDirectory(directory);
            }

            await _fileStore.WriteAllBytesAsync(candidatePath, bytes, cancellationToken);
            _fileStore.Replace(candidatePath, _configPath);
        }
        finally
        {
            try
            {
                _fileStore.DeleteIfExists(candidatePath);
            }
            catch (Exception cleanupException)
            {
                _logger.LogWarning(cleanupException, "Failed to clean AppConfig candidate {CandidatePath}.", candidatePath);
            }
        }
    }

    private AppConfigReadResult Degraded(string errorCode, string message)
    {
        var lastGood = GetLastGoodOrNull();
        return lastGood == null
            ? new AppConfigReadResult(AppConfigReadStatus.Unavailable, null, errorCode, message)
            : new AppConfigReadResult(AppConfigReadStatus.DegradedLastGood, lastGood, errorCode, message);
    }

    private static AppConfigMutationResult Failure(
        AppConfigMutationStatus status,
        long expectedRevision,
        AppConfig? config,
        string errorCode,
        string message)
    {
        return new AppConfigMutationResult(
            status,
            config == null ? null : Clone(config),
            expectedRevision,
            config?.Revision,
            errorCode,
            message);
    }

    private AppConfig? GetLastGoodOrNull()
    {
        lock (_cacheSync)
        {
            return _lastGoodConfig == null ? null : Clone(_lastGoodConfig);
        }
    }

    private void SetLastGood(AppConfig config)
    {
        lock (_cacheSync)
        {
            _lastGoodConfig = Clone(config);
        }
    }

    private static (string Code, string Message) ClassifyReadFailure(Exception exception)
    {
        return exception switch
        {
            JsonException => (ErrorMalformed, "The AppConfig file contains malformed JSON."),
            UnauthorizedAccessException => (ErrorAccessDenied, "Access to the AppConfig file was denied."),
            IOException ioException when IsSharingViolation(ioException) =>
                (ErrorLocked, "The AppConfig file is locked by another process."),
            IOException => (ErrorIo, "The AppConfig file could not be read."),
            _ => (ErrorUnavailable, "The AppConfig file is unavailable.")
        };
    }

    private static bool IsSharingViolation(IOException exception)
    {
        var code = exception.HResult & 0xFFFF;
        return code is 32 or 33;
    }

    private static bool IsReadException(Exception exception) =>
        exception is JsonException or IOException or UnauthorizedAccessException or NotSupportedException;

    private static bool IsStorageException(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or NotSupportedException;

    private static bool ConfigEquals(AppConfig left, AppConfig right)
    {
        return JsonSerializer.Serialize(left, JsonOptions) == JsonSerializer.Serialize(right, JsonOptions);
    }

    private static AppConfig Normalize(AppConfig config)
    {
        config.Normalize();
        return config;
    }

    private static AppConfig Clone(AppConfig config)
    {
        var json = JsonSerializer.Serialize(config, JsonOptions);
        return Normalize(JsonSerializer.Deserialize<AppConfig>(json, JsonOptions) ?? new AppConfig());
    }

    private static void CopyConfig(AppConfig source, AppConfig destination)
    {
        var snapshot = Clone(source);
        destination.General = snapshot.General;
        destination.Communication = snapshot.Communication;
        destination.TcpCommunication = snapshot.TcpCommunication;
        destination.Storage = snapshot.Storage;
        destination.Runtime = snapshot.Runtime;
        destination.Features = snapshot.Features;
        destination.Cameras = snapshot.Cameras;
        destination.Security = snapshot.Security;
        destination.ActiveCameraId = snapshot.ActiveCameraId;
    }
}

public interface IAppConfigFileStore
{
    bool Exists(string path);
    Task<byte[]> ReadAllBytesAsync(string path, CancellationToken cancellationToken);
    Task WriteAllBytesAsync(string path, byte[] bytes, CancellationToken cancellationToken);
    void CreateDirectory(string path);
    void Replace(string candidatePath, string activePath);
    void DeleteIfExists(string path);
}

public sealed class PhysicalAppConfigFileStore : IAppConfigFileStore
{
    public static PhysicalAppConfigFileStore Instance { get; } = new();

    private PhysicalAppConfigFileStore()
    {
    }

    public bool Exists(string path)
    {
        try
        {
            _ = File.GetAttributes(path);
            return true;
        }
        catch (FileNotFoundException)
        {
            return false;
        }
        catch (DirectoryNotFoundException)
        {
            return false;
        }
    }

    public Task<byte[]> ReadAllBytesAsync(string path, CancellationToken cancellationToken) =>
        File.ReadAllBytesAsync(path, cancellationToken);

    public Task WriteAllBytesAsync(string path, byte[] bytes, CancellationToken cancellationToken) =>
        File.WriteAllBytesAsync(path, bytes, cancellationToken);

    public void CreateDirectory(string path) => Directory.CreateDirectory(path);

    public void Replace(string candidatePath, string activePath) => File.Move(candidatePath, activePath, overwrite: true);

    public void DeleteIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
