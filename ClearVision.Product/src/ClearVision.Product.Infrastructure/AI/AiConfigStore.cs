// AiConfigStore.cs
// AI 配置存储
// 负责 AI 配置的读取、写入与默认值管理
// 作者：蘅芜君
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ClearVision.Product.Infrastructure.AI;

internal sealed class AiModelGenerationDocument
{
    public int SchemaVersion { get; set; } = 2;

    public string GenerationId { get; set; } = string.Empty;

    public List<AiModelConfig> Models { get; set; } = new();

    public List<string> SecretModelIds { get; set; } = new();
}

internal sealed record AiModelLoadResult(List<AiModelConfig> Models, string GenerationId);

/// <summary>
/// Runtime store for AI model profiles.
/// Persists models to ai_models.json and migrates old ai_config.json on first load.
/// </summary>
public class AiConfigStore
{
    private readonly Microsoft.Extensions.Logging.ILogger<AiConfigStore> _logger;
    private readonly object _lock;
    private List<AiModelConfig> _models = new();
    private readonly AiGenerationOptions _initialOptions;
    private readonly string _modelsFilePath;
    private readonly string _modelsBackupFilePath;
    private readonly string _legacyConfigFilePath;
    private readonly string _secretsRoot;
    private readonly IAiPersistenceFaultInjector _faultInjector;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private static string SanitizeLogValue(string? value)
    {
        return string.IsNullOrEmpty(value)
            ? string.Empty
            : value.Replace("\r", "\\r", StringComparison.Ordinal)
                .Replace("\n", "\\n", StringComparison.Ordinal);
    }

    public AiConfigStore(IOptions<AiGenerationOptions> initialOptions, Microsoft.Extensions.Logging.ILogger<AiConfigStore> logger)
        : this(initialOptions, logger, AppContext.BaseDirectory)
    {
    }

    public AiConfigStore(
        IOptions<AiGenerationOptions> initialOptions,
        Microsoft.Extensions.Logging.ILogger<AiConfigStore> logger,
        string storageDirectory)
        : this(initialOptions, logger, storageDirectory, null)
    {
    }

    internal AiConfigStore(
        IOptions<AiGenerationOptions> initialOptions,
        Microsoft.Extensions.Logging.ILogger<AiConfigStore> logger,
        string storageDirectory,
        IAiPersistenceFaultInjector? faultInjector)
    {
        _logger = logger;
        _initialOptions = CloneOptions(initialOptions.Value);
        if (string.IsNullOrWhiteSpace(storageDirectory))
            throw new ArgumentException("Storage directory must not be empty.", nameof(storageDirectory));

        Directory.CreateDirectory(storageDirectory);
        _modelsFilePath = Path.Combine(storageDirectory, "ai_models.json");
        _modelsBackupFilePath = _modelsFilePath + ".previous";
        _lock = AiPersistenceFileOperations.GetMutationGate(_modelsFilePath);
        _legacyConfigFilePath = Path.Combine(storageDirectory, "ai_config.json");
        _secretsRoot = Path.Combine(storageDirectory, "ai_model_secrets");
        _faultInjector = faultInjector ?? NoOpAiPersistenceFaultInjector.Instance;
        Directory.CreateDirectory(_secretsRoot);

        lock (_lock)
        {
            var loaded = LoadOrMigrate(_initialOptions);
            _models = loaded.Models;
        }
    }

    public List<AiModelConfig> GetAll()
    {
        lock (_lock)
        {
            return _models.Select(CloneModel).ToList();
        }
    }

    public AiModelConfig? GetById(string id)
    {
        lock (_lock)
        {
            var model = _models.FirstOrDefault(x => x.Id == id);
            return model == null ? null : CloneModel(model);
        }
    }

    /// <summary>
    /// Legacy compatibility helper. Returns active model as generation options.
    /// </summary>
    public AiGenerationOptions Get()
    {
        lock (_lock)
        {
            var active = _models.FirstOrDefault(x => x.IsActive && x.IsEnabled) ??
                         _models.FirstOrDefault(x => x.IsEnabled) ??
                         _models.FirstOrDefault();
            if (active == null)
                throw new InvalidOperationException("没有可用的 AI 模型配置");

            return active.ToGenerationOptions();
        }
    }

    public AiModelConfig Add(AiModelConfig model)
    {
        AiModelConfig committed;
        lock (_lock)
        {
            ReloadAuthoritativeModelsLocked();
            var candidateModels = _models.Select(CloneModel).ToList();
            var candidateModel = CloneModel(model);
            if (string.IsNullOrWhiteSpace(candidateModel.Id))
            {
                candidateModel.Id = $"model_{Guid.NewGuid():N}";
            }

            if (candidateModels.Any(x => string.Equals(x.Id, candidateModel.Id, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException($"AI model id already exists: {candidateModel.Id}");
            }

            StampNewModel(candidateModel);
            candidateModel.ValidateReasoningConfiguration();
            candidateModel.Capabilities = (candidateModel.Capabilities?.Clone() ?? AiModelCapabilities.Infer(candidateModel.Provider, candidateModel.Model)).Normalize();

            if (candidateModels.Count == 0)
                candidateModel.IsActive = true;

            candidateModels.Add(candidateModel);
            CommitAndActivateCandidateLocked(candidateModels, "add");
            committed = CloneModel(candidateModel);
        }

        model.Id = committed.Id;
        _logger.LogInformation("[AiConfigStore] 新增模型: {Name} ({Id})", SanitizeLogValue(committed.Name), SanitizeLogValue(committed.Id));
        return committed;
    }

    /// <summary>
    /// Update model by id. Empty/null ApiKey preserves existing key.
    /// </summary>
    public AiModelConfig? Update(string id, AiModelConfig updated)
    {
        return Update(id, updated, AiApiKeyUpdateMode.Keep);
    }

    public AiModelConfig? Update(string id, AiModelConfig updated, AiApiKeyUpdateMode apiKeyUpdateMode)
    {
        AiModelConfig? committed;
        lock (_lock)
        {
            ReloadAuthoritativeModelsLocked();
            var candidateModels = _models.Select(CloneModel).ToList();
            var index = candidateModels.FindIndex(x => x.Id == id);
            if (index < 0)
                return null;

            var candidate = CloneModel(candidateModels[index]);
            ApplyUpdatedValues(candidate, updated, apiKeyUpdateMode);
            candidate.UpdatedAt = DateTimeOffset.UtcNow;
            candidate.ValidateReasoningConfiguration();
            candidate.NormalizeAdvancedFields();
            candidateModels[index] = candidate;
            CommitAndActivateCandidateLocked(candidateModels, "update");
            committed = CloneModel(candidate);
        }

        _logger.LogInformation("[AiConfigStore] 更新模型: {Name} ({Id})", SanitizeLogValue(updated.Name), SanitizeLogValue(id));
        return committed;
    }

    public bool Delete(string id)
    {
        lock (_lock)
        {
            ReloadAuthoritativeModelsLocked();
            var candidateModels = _models.Select(CloneModel).ToList();
            if (candidateModels.Count <= 1)
                throw new InvalidOperationException("至少需保留一个模型配置");

            var removed = candidateModels.RemoveAll(x => x.Id == id);
            if (removed == 0)
                return false;

            if (!candidateModels.Any(x => x.IsActive) && candidateModels.Count > 0)
            {
                candidateModels[0].IsActive = true;
            }

            CommitAndActivateCandidateLocked(candidateModels, "delete");
        }

        _logger.LogInformation("[AiConfigStore] 删除模型: {Id}", SanitizeLogValue(id));
        return true;
    }

    public bool SetActive(string id)
    {
        lock (_lock)
        {
            ReloadAuthoritativeModelsLocked();
            var candidateModels = _models.Select(CloneModel).ToList();
            var target = candidateModels.FirstOrDefault(x => x.Id == id);
            if (target == null)
                return false;

            foreach (var model in candidateModels)
                model.IsActive = model.Id == id;
            target.IsEnabled = true;
            target.UpdatedAt = DateTimeOffset.UtcNow;

            CommitAndActivateCandidateLocked(candidateModels, "activate");
        }

        _logger.LogInformation("[AiConfigStore] 激活模型切换为: {Id}", SanitizeLogValue(id));
        return true;
    }

    public bool SetDefaultForRole(string id, string role)
    {
        var normalizedRole = AiModelConfig.NormalizeRoleName(role);
        lock (_lock)
        {
            ReloadAuthoritativeModelsLocked();
            var candidateModels = _models.Select(CloneModel).ToList();
            var target = candidateModels.FirstOrDefault(x => x.Id == id);
            if (target == null)
                return false;

            foreach (var model in candidateModels)
            {
                model.RoleBindings = AiModelConfig
                    .NormalizeRoleBindings(model.RoleBindings)
                    .Where(item => !string.Equals(item, normalizedRole, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                if (model.RoleBindings.Count == 0)
                {
                    model.RoleBindings.Add(AiModelConfig.RoleGeneration);
                }

                model.ModelRole = model.RoleBindings.FirstOrDefault() ?? AiModelConfig.RoleGeneration;
                model.UpdatedAt = DateTimeOffset.UtcNow;
            }

            target.RoleBindings = AiModelConfig.NormalizeRoleBindings(target.RoleBindings);
            if (!target.RoleBindings.Contains(normalizedRole, StringComparer.OrdinalIgnoreCase))
            {
                target.RoleBindings.Insert(0, normalizedRole);
            }

            target.ModelRole = normalizedRole;
            target.Priority = Math.Min(target.Priority ?? 100, 1);
            target.IsEnabled = true;
            target.UpdatedAt = DateTimeOffset.UtcNow;
            target.NormalizeAdvancedFields();

            CommitAndActivateCandidateLocked(candidateModels, "set_default_role");
        }

        _logger.LogInformation("[AiConfigStore] Set default model role {Role}: {Id}", SanitizeLogValue(normalizedRole), SanitizeLogValue(id));
        return true;
    }

    public AiModelConfig? UpdateTestStatus(
        string id,
        string status,
        DateTimeOffset testedAt,
        int? latencyMs)
    {
        AiModelConfig? committed;
        lock (_lock)
        {
            ReloadAuthoritativeModelsLocked();
            var candidateModels = _models.Select(CloneModel).ToList();
            var model = candidateModels.FirstOrDefault(x => x.Id == id);
            if (model == null)
                return null;

            model.LastTestStatus = string.IsNullOrWhiteSpace(status) ? "untested" : status.Trim().ToLowerInvariant();
            model.LastTestAt = testedAt;
            model.LastTestLatencyMs = latencyMs;
            model.UpdatedAt = DateTimeOffset.UtcNow;
            model.NormalizeAdvancedFields();
            CommitAndActivateCandidateLocked(candidateModels, "update_test_status");
            committed = CloneModel(model);
        }

        return committed;
    }

    public List<AiModelConfig> ResetToDefaults()
    {
        List<AiModelConfig> resetModels;
        lock (_lock)
        {
            var candidateModels = CreateDefaultModels(_initialOptions);
            CommitAndActivateCandidateLocked(candidateModels, "reset");
            resetModels = candidateModels.Select(CloneModel).ToList();
        }

        try
        {
            if (File.Exists(_legacyConfigFilePath))
            {
                File.Delete(_legacyConfigFilePath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[AiConfigStore] 删除旧版 ai_config.json 失败: {Message}", ex.Message);
        }

        _logger.LogInformation("[AiConfigStore] AI 模型配置已重置为默认值");
        return resetModels;
    }

    private void TryDeleteLegacyConfigFile()
    {
        try
        {
            if (File.Exists(_legacyConfigFilePath))
            {
                File.Delete(_legacyConfigFilePath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[AiConfigStore] 删除旧版 ai_config.json 失败: {Message}", ex.Message);
        }
    }

    private AiModelLoadResult LoadOrMigrate(AiGenerationOptions fallback)
    {
        CleanupCandidateResidue();

        if (File.Exists(_modelsFilePath))
        {
            if (TryLoadGenerationDocument(_modelsFilePath, out var active, out var activeError))
            {
                CleanupSecretGenerationResidue(active.GenerationId);
                _logger.LogInformation(
                    "[AiConfigStore] Loaded {Count} AI models from generation {GenerationId}",
                    active.Models.Count,
                    active.GenerationId);
                return active;
            }

            if (TryLoadLegacyModelList(_modelsFilePath, out var legacyModels, out var legacyError))
            {
                var generationId = PersistGeneration(legacyModels, "migrate_legacy_model_list");
                CleanupSecretGenerationResidue(generationId);
                _logger.LogInformation(
                    "[AiConfigStore] Migrated {Count} legacy AI models into generation {GenerationId}",
                    legacyModels.Count,
                    generationId);
                return new AiModelLoadResult(legacyModels, generationId);
            }

            Exception? backupGenerationError = null;
            if (File.Exists(_modelsBackupFilePath) &&
                TryLoadGenerationDocument(_modelsBackupFilePath, out var recovered, out backupGenerationError))
            {
                RestoreBackupDocument();
                CleanupSecretGenerationResidue(recovered.GenerationId);
                _logger.LogWarning(
                    "[AiConfigStore] Recovered AI model generation {GenerationId} from the previous durable document.",
                    recovered.GenerationId);
                return recovered;
            }

            Exception? legacyBackupError = null;
            if (File.Exists(_modelsBackupFilePath) &&
                TryLoadLegacyModelList(_modelsBackupFilePath, out var legacyBackupModels, out legacyBackupError))
            {
                // Restore the complete legacy generation first. If migration is interrupted,
                // restart can still read this active legacy document and retry safely.
                RestoreBackupDocument();
                var generationId = PersistGeneration(legacyBackupModels, "recover_legacy_model_backup");
                CleanupSecretGenerationResidue(generationId);
                _logger.LogWarning(
                    "[AiConfigStore] Recovered {Count} AI models from the previous legacy generation into {GenerationId}.",
                    legacyBackupModels.Count,
                    generationId);
                return new AiModelLoadResult(legacyBackupModels, generationId);
            }

            throw new AiConfigPersistenceException(
                "AI_MODEL_RECOVERY_FAILED",
                "load",
                new AggregateException(
                    activeError ?? new InvalidDataException("Active AI model document is invalid."),
                    legacyError ?? new InvalidDataException("Legacy AI model document is invalid."),
                    backupGenerationError ?? new InvalidDataException("Previous AI model generation is invalid."),
                    legacyBackupError ?? new InvalidDataException("Previous legacy AI model document is invalid.")));
        }

        if (File.Exists(_legacyConfigFilePath))
        {
            try
            {
                var json = File.ReadAllText(_legacyConfigFilePath);
                var legacy = JsonSerializer.Deserialize<AiGenerationOptions>(json, JsonOptions)
                    ?? throw new JsonException("Legacy AI configuration deserialized to null.");
                _logger.LogInformation("[AiConfigStore] Migrating legacy ai_config.json");
                var migrated = new AiModelConfig
                {
                    Id = "model_migrated",
                    Name = "系统默认模型",
                    DisplayName = "Legacy default model",
                    Provider = legacy.Provider,
                    Protocol = AiModelConfig.NormalizeProtocol(null, legacy.Provider),
                    WireApi = AiModelConfig.NormalizeWireApi(legacy.WireApi),
                    AuthMode = AiModelConfig.NormalizeAuthMode(null, AiModelConfig.NormalizeProtocol(null, legacy.Provider)),
                    ApiKey = legacy.ApiKey,
                    Model = legacy.Model,
                    BaseUrl = legacy.BaseUrl,
                    TimeoutMs = legacy.TimeoutSeconds * 1000,
                    RoleBindings = new List<string> { AiModelConfig.RoleGeneration, AiModelConfig.RolePlanner },
                    ModelRole = AiModelConfig.RoleGeneration,
                    Priority = 100,
                    Capabilities = AiModelCapabilities.Infer(legacy.Provider, legacy.Model),
                    Reasoning = new AiReasoningSettings(),
                    IsActive = true,
                    IsEnabled = true
                };
                migrated.NormalizeAdvancedFields();

                var models = new List<AiModelConfig> { migrated };
                var generationId = PersistGeneration(models, "migrate_legacy_config");
                TryDeleteLegacyConfigFile();
                CleanupSecretGenerationResidue(generationId);
                return new AiModelLoadResult(models, generationId);
            }
            catch (AiConfigPersistenceException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new AiConfigPersistenceException("AI_MODEL_MIGRATION_FAILED", "migration", ex);
            }
        }

        _logger.LogInformation("[AiConfigStore] Initializing AI models from appsettings defaults");
        var result = CreateDefaultModels(fallback);
        var initialGenerationId = PersistGeneration(result, "initialize");
        CleanupSecretGenerationResidue(initialGenerationId);
        return new AiModelLoadResult(result, initialGenerationId);
    }

    private static void EnsureOneActive(List<AiModelConfig> models)
    {
        if (!models.Any(x => x.IsActive) && models.Count > 0)
            models[0].IsActive = true;
    }

    private static void EnsureUniqueModelIds(List<AiModelConfig> models)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var model in models)
        {
            if (string.IsNullOrWhiteSpace(model.Id) || seen.Contains(model.Id))
            {
                model.Id = $"model_{Guid.NewGuid():N}";
            }

            seen.Add(model.Id);
        }
    }

    private static void EnsureCapabilities(List<AiModelConfig> models)
    {
        foreach (var model in models)
        {
            model.Capabilities = (model.Capabilities?.Clone() ?? AiModelCapabilities.Infer(model.Provider, model.Model)).Normalize();
            model.Reasoning = (model.Reasoning?.Clone() ?? new AiReasoningSettings()).Normalize();
        }
    }

    private static void EnsureAdvancedFields(List<AiModelConfig> models)
    {
        foreach (var model in models)
        {
            model.NormalizeAdvancedFields();
        }
    }

    private static void StampNewModel(AiModelConfig model)
    {
        var now = DateTimeOffset.UtcNow;
        model.CreatedAt ??= now;
        model.UpdatedAt = now;
        model.LastTestStatus = string.IsNullOrWhiteSpace(model.LastTestStatus)
            ? "untested"
            : model.LastTestStatus.Trim().ToLowerInvariant();
        model.NormalizeAdvancedFields();
    }

    private void CommitAndActivateCandidateLocked(List<AiModelConfig> candidateModels, string operation)
    {
        var durableGenerationId = PersistGeneration(candidateModels, operation);
        _models = candidateModels.Select(CloneModel).ToList();
        CleanupSecretGenerationResidue(durableGenerationId);
    }

    private void ReloadAuthoritativeModelsLocked()
    {
        var loaded = LoadOrMigrate(_initialOptions);
        _models = loaded.Models;
    }

    private string PersistGeneration(IReadOnlyList<AiModelConfig> models, string operation)
    {
        var generationId = Guid.NewGuid().ToString("N");
        var candidateSecretDirectory = Path.Combine(_secretsRoot, $".candidate-{generationId}");
        var finalSecretDirectory = Path.Combine(_secretsRoot, generationId);
        var candidateDocumentPath = $"{_modelsFilePath}.{generationId}.candidate";
        var stage = "candidate_start";
        var committed = false;
        var interrupted = false;

        try
        {
            Directory.CreateDirectory(candidateSecretDirectory);
            _faultInjector.OnStage(
                AiPersistenceStage.ModelCandidateStarted,
                "ai_models",
                candidateSecretDirectory);

            stage = "candidate_secrets";
            var candidateSecretStore = new DpapiFileAiApiKeySecretStore(candidateSecretDirectory);
            var secretModelIds = new List<string>();
            foreach (var model in models.Where(item => !string.IsNullOrWhiteSpace(item.ApiKey)))
            {
                _faultInjector.OnStage(
                    AiPersistenceStage.ModelSecretCandidateWrite,
                    "ai_models",
                    candidateSecretDirectory);
                candidateSecretStore.Save(model.Id, model.ApiKey);
                secretModelIds.Add(model.Id);
            }

            _faultInjector.OnStage(
                AiPersistenceStage.ModelSecretsPrepared,
                "ai_models",
                candidateSecretDirectory);

            stage = "candidate_document";
            var document = new AiModelGenerationDocument
            {
                SchemaVersion = 2,
                GenerationId = generationId,
                Models = models.Select(CloneModelForPersistence).ToList(),
                SecretModelIds = secretModelIds
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
                    .ToList()
            };
            var json = JsonSerializer.Serialize(document, JsonOptions);
            AiPersistenceFileOperations.WriteAllTextDurable(candidateDocumentPath, json);
            _faultInjector.OnStage(
                AiPersistenceStage.ModelDocumentPrepared,
                "ai_models",
                candidateDocumentPath);

            Directory.Move(candidateSecretDirectory, finalSecretDirectory);

            stage = "commit";
            _faultInjector.OnStage(
                AiPersistenceStage.ModelCommitStarted,
                "ai_models",
                _modelsFilePath);
            AiPersistenceFileOperations.CommitCandidate(
                candidateDocumentPath,
                _modelsFilePath,
                _modelsBackupFilePath);
            committed = true;
            _faultInjector.OnStage(
                AiPersistenceStage.ModelCommitCompleted,
                "ai_models",
                _modelsFilePath);

            return generationId;
        }
        catch (AiPersistenceInterruptionException)
        {
            interrupted = true;
            throw;
        }
        catch (Exception ex)
        {
            if (committed)
            {
                _logger.LogWarning(
                    ex,
                    "[AiConfigStore] Post-commit observation failed for generation {GenerationId}; the durable commit remains authoritative.",
                    generationId);
                return generationId;
            }

            var errorCode = stage == "candidate_secrets"
                ? "AI_MODEL_SECRET_PERSISTENCE_FAILED"
                : stage == "commit"
                    ? "AI_MODEL_COMMIT_FAILED"
                    : "AI_MODEL_CANDIDATE_PERSISTENCE_FAILED";
            _logger.LogError(
                ex,
                "[AiConfigStore] Durable model mutation failed at stage {Stage} during {Operation}.",
                stage,
                operation);
            throw new AiConfigPersistenceException(errorCode, stage, ex);
        }
        finally
        {
            if (!committed && !interrupted)
            {
                AiPersistenceFileOperations.TryDeleteFile(candidateDocumentPath);
                AiPersistenceFileOperations.TryDeleteDirectory(candidateSecretDirectory);
                AiPersistenceFileOperations.TryDeleteDirectory(finalSecretDirectory);
            }
        }
    }

    private bool TryLoadGenerationDocument(
        string documentPath,
        out AiModelLoadResult result,
        out Exception? error)
    {
        try
        {
            var json = File.ReadAllText(documentPath);
            var document = JsonSerializer.Deserialize<AiModelGenerationDocument>(json, JsonOptions)
                ?? throw new JsonException("AI model generation document deserialized to null.");
            if (document.SchemaVersion != 2 ||
                document.Models == null ||
                document.Models.Count == 0 ||
                document.SecretModelIds == null)
            {
                throw new InvalidDataException("AI model generation document is incomplete.");
            }

            var generationId = NormalizeGenerationId(document.GenerationId);
            var secretDirectory = ResolveSecretGenerationDirectory(generationId);
            if (!Directory.Exists(secretDirectory))
            {
                throw new DirectoryNotFoundException("The referenced AI secret generation is unavailable.");
            }

            if ((File.GetAttributes(secretDirectory) & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidDataException("The referenced AI secret generation directory is not authoritative.");
            }

            EnsureUniqueModelIds(document.Models);
            EnsureOneActive(document.Models);
            EnsureCapabilities(document.Models);
            EnsureAdvancedFields(document.Models);

            var modelIds = document.Models
                .Select(model => model.Id)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var secretIds = document.SecretModelIds
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (!secretIds.IsSubsetOf(modelIds))
            {
                throw new InvalidDataException("The AI secret generation references an unknown model.");
            }

            var secretStore = new DpapiFileAiApiKeySecretStore(secretDirectory);
            foreach (var model in document.Models)
            {
                model.ApiKey = string.Empty;
                if (!secretIds.Contains(model.Id))
                {
                    continue;
                }

                if (!secretStore.TryRead(model.Id, out var apiKey))
                {
                    throw new InvalidDataException("The referenced AI secret generation is incomplete.");
                }

                model.ApiKey = apiKey;
            }

            result = new AiModelLoadResult(document.Models, generationId);
            error = null;
            return true;
        }
        catch (Exception ex)
        {
            result = new AiModelLoadResult(new List<AiModelConfig>(), string.Empty);
            error = ex;
            return false;
        }
    }

    private static string NormalizeGenerationId(string? generationId)
    {
        if (string.IsNullOrWhiteSpace(generationId) ||
            !Guid.TryParseExact(generationId.Trim(), "N", out var parsed))
        {
            throw new InvalidDataException("AI model generation identifier is invalid.");
        }

        return parsed.ToString("N");
    }

    private string ResolveSecretGenerationDirectory(string generationId)
    {
        var canonicalRoot = Path.GetFullPath(_secretsRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var candidate = Path.GetFullPath(Path.Combine(canonicalRoot, generationId));
        var requiredPrefix = canonicalRoot + Path.DirectorySeparatorChar;
        if (!candidate.StartsWith(requiredPrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("AI model secret generation path escaped its authority root.");
        }

        return candidate;
    }

    private bool TryLoadLegacyModelList(
        string documentPath,
        out List<AiModelConfig> models,
        out Exception? error)
    {
        try
        {
            var json = File.ReadAllText(documentPath);
            models = JsonSerializer.Deserialize<List<AiModelConfig>>(json, JsonOptions)
                ?? throw new JsonException("Legacy AI model list deserialized to null.");
            if (models.Count == 0)
            {
                throw new InvalidDataException("Legacy AI model list is empty.");
            }

            EnsureUniqueModelIds(models);
            EnsureOneActive(models);
            EnsureCapabilities(models);
            EnsureAdvancedFields(models);

            var legacySecretStore = new DpapiFileAiApiKeySecretStore(_secretsRoot);
            foreach (var model in models.Where(model => string.IsNullOrWhiteSpace(model.ApiKey)))
            {
                if (legacySecretStore.TryRead(model.Id, out var apiKey))
                {
                    model.ApiKey = apiKey;
                }
            }

            error = null;
            return true;
        }
        catch (Exception ex)
        {
            models = new List<AiModelConfig>();
            error = ex;
            return false;
        }
    }

    private void RestoreBackupDocument()
    {
        var candidatePath = $"{_modelsFilePath}.{Guid.NewGuid():N}.recovery.candidate";
        try
        {
            AiPersistenceFileOperations.WriteAllTextDurable(
                candidatePath,
                File.ReadAllText(_modelsBackupFilePath));
            File.Move(candidatePath, _modelsFilePath, overwrite: true);
        }
        catch (Exception ex)
        {
            throw new AiConfigPersistenceException("AI_MODEL_RECOVERY_COMMIT_FAILED", "recovery", ex);
        }
        finally
        {
            AiPersistenceFileOperations.TryDeleteFile(candidatePath);
        }
    }

    private void CleanupCandidateResidue()
    {
        try
        {
            var storageDirectory = Path.GetDirectoryName(_modelsFilePath)!;
            _faultInjector.OnStage(
                AiPersistenceStage.ModelCandidateCleanupStarted,
                "ai_models",
                storageDirectory);
            foreach (var path in Directory.EnumerateFiles(storageDirectory, "ai_models.json.*.candidate"))
            {
                AiPersistenceFileOperations.TryDeleteFile(path);
            }

            if (!Directory.Exists(_secretsRoot))
            {
                return;
            }

            foreach (var path in Directory.EnumerateDirectories(_secretsRoot, ".candidate-*"))
            {
                AiPersistenceFileOperations.TryDeleteDirectory(path);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "[AiConfigStore] Non-authoritative AI model candidate cleanup will be retried on a later mutation or restart.");
        }
    }

    private void CleanupSecretGenerationResidue(string activeGenerationId)
    {
        try
        {
            _faultInjector.OnStage(AiPersistenceStage.CleanupStarted, "ai_models", _secretsRoot);
            string? backupGenerationId = null;
            if (File.Exists(_modelsBackupFilePath))
            {
                if (TryLoadGenerationDocument(
                    _modelsBackupFilePath,
                    out var backup,
                    out var backupGenerationError))
                {
                    backupGenerationId = backup.GenerationId;
                }
                else if (!TryLoadLegacyModelList(
                    _modelsBackupFilePath,
                    out _,
                    out var legacyBackupError))
                {
                    // A transient sharing/permission/DPAPI failure must not turn an otherwise
                    // recoverable previous document into a permanently mixed generation by
                    // deleting the secrets it may still reference. Defer all generation pruning
                    // until the previous document can be classified and validated again.
                    _logger.LogWarning(
                        new AggregateException(
                            backupGenerationError ?? new InvalidDataException("Previous AI model generation is not readable."),
                            legacyBackupError ?? new InvalidDataException("Previous legacy AI model document is not readable.")),
                        "[AiConfigStore] Deferred secret generation cleanup because the previous AI model document could not be verified.");
                    return;
                }
            }

            foreach (var path in Directory.EnumerateDirectories(_secretsRoot))
            {
                var name = Path.GetFileName(path);
                if (name.Equals(activeGenerationId, StringComparison.OrdinalIgnoreCase) ||
                    (!string.IsNullOrWhiteSpace(backupGenerationId) &&
                     name.Equals(backupGenerationId, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                AiPersistenceFileOperations.TryDeleteDirectory(path);
            }

            if (!string.IsNullOrWhiteSpace(backupGenerationId))
            {
                foreach (var legacySecretPath in Directory.EnumerateFiles(_secretsRoot, "*.dpapi"))
                {
                    AiPersistenceFileOperations.TryDeleteFile(legacySecretPath);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "[AiConfigStore] Non-authoritative AI model generation cleanup will be retried on a later mutation or restart.");
        }
    }

    private static List<AiModelConfig> CreateDefaultModels(AiGenerationOptions fallback)
    {
        var defaultModel = new AiModelConfig
        {
            Id = "model_default",
            Name = "系统默认模型",
            DisplayName = "System default model",
            Provider = fallback.Provider,
            Protocol = AiModelConfig.NormalizeProtocol(null, fallback.Provider),
            WireApi = AiModelConfig.NormalizeWireApi(fallback.WireApi),
            AuthMode = AiModelConfig.NormalizeAuthMode(null, AiModelConfig.NormalizeProtocol(null, fallback.Provider)),
            ApiKey = fallback.ApiKey,
            Model = fallback.Model,
            BaseUrl = fallback.BaseUrl,
            TimeoutMs = Math.Max(1, fallback.TimeoutSeconds) * 1000,
            RoleBindings = new List<string> { AiModelConfig.RoleGeneration, AiModelConfig.RolePlanner },
            ModelRole = AiModelConfig.RoleGeneration,
            Priority = 100,
            Capabilities = AiModelCapabilities.Infer(fallback.Provider, fallback.Model),
            Reasoning = new AiReasoningSettings(),
            IsActive = true,
            IsEnabled = true
        };
        defaultModel.NormalizeAdvancedFields();

        return new List<AiModelConfig> { defaultModel };
    }

    private static AiModelConfig CloneModelForPersistence(AiModelConfig model)
    {
        var clone = CloneModel(model);
        clone.ApiKey = string.Empty;
        return clone;
    }

    private static AiModelConfig CloneModel(AiModelConfig model) => new()
    {
        Id = model.Id,
        Name = model.Name,
        DisplayName = model.DisplayName,
        Provider = model.Provider,
        ApiKey = model.ApiKey,
        Model = model.Model,
        BaseUrl = model.BaseUrl,
        TimeoutMs = model.TimeoutMs,
        Protocol = model.Protocol,
        WireApi = model.WireApi,
        AuthMode = model.AuthMode,
        AuthHeaderName = model.AuthHeaderName,
        ExtraHeaders = CloneStringDictionary(model.ExtraHeaders),
        ExtraQuery = CloneStringDictionary(model.ExtraQuery),
        ExtraBody = CloneJsonDictionary(model.ExtraBody),
        RoleBindings = model.RoleBindings == null ? null : new List<string>(model.RoleBindings),
        ModelRole = model.ModelRole,
        Priority = model.Priority,
        Remark = model.Remark,
        CreatedAt = model.CreatedAt,
        UpdatedAt = model.UpdatedAt,
        LastTestStatus = model.LastTestStatus,
        LastTestAt = model.LastTestAt,
        LastTestLatencyMs = model.LastTestLatencyMs,
        Capabilities = model.Capabilities?.Clone(),
        Reasoning = model.Reasoning?.Clone(),
        IsActive = model.IsActive,
        IsEnabled = model.IsEnabled
    };

    private static void ApplyUpdatedValues(
        AiModelConfig candidate,
        AiModelConfig updated,
        AiApiKeyUpdateMode apiKeyUpdateMode)
    {
        var providerChanged = !string.IsNullOrWhiteSpace(updated.Provider) &&
            !string.Equals(updated.Provider, candidate.Provider, StringComparison.Ordinal);

        candidate.Name = updated.Name ?? candidate.Name;
        candidate.DisplayName = updated.DisplayName ?? candidate.DisplayName;
        candidate.Provider = updated.Provider ?? candidate.Provider;
        candidate.Model = updated.Model ?? candidate.Model;
        candidate.BaseUrl = updated.BaseUrl;
        candidate.TimeoutMs = updated.TimeoutMs > 0 ? updated.TimeoutMs : candidate.TimeoutMs;
        candidate.Protocol = updated.Protocol ?? (providerChanged ? null : candidate.Protocol);
        candidate.WireApi = updated.WireApi ?? candidate.WireApi;
        candidate.AuthMode = updated.AuthMode ?? (providerChanged || updated.Protocol != null ? null : candidate.AuthMode);
        candidate.AuthHeaderName = updated.AuthHeaderName ??
            (providerChanged || updated.Protocol != null || updated.AuthMode != null ? null : candidate.AuthHeaderName);
        candidate.Priority = updated.Priority ?? candidate.Priority;
        candidate.ModelRole = updated.ModelRole ?? candidate.ModelRole;
        candidate.Remark = updated.Remark;
        candidate.IsEnabled = updated.IsEnabled;

        if (updated.ExtraHeaders != null)
            candidate.ExtraHeaders = CloneStringDictionary(updated.ExtraHeaders);

        if (updated.ExtraQuery != null)
            candidate.ExtraQuery = CloneStringDictionary(updated.ExtraQuery);

        if (updated.ExtraBody != null)
            candidate.ExtraBody = CloneJsonDictionary(updated.ExtraBody);

        if (updated.RoleBindings != null)
            candidate.RoleBindings = new List<string>(updated.RoleBindings);

        if (updated.Capabilities != null)
        {
            candidate.Capabilities = updated.Capabilities.Clone().Normalize();
        }

        if (updated.Reasoning != null)
        {
            candidate.Reasoning = updated.Reasoning.Clone().Normalize();
        }

        switch (apiKeyUpdateMode)
        {
            case AiApiKeyUpdateMode.Replace:
                candidate.ApiKey = updated.ApiKey ?? string.Empty;
                break;
            case AiApiKeyUpdateMode.Clear:
                candidate.ApiKey = string.Empty;
                break;
            case AiApiKeyUpdateMode.Keep:
            default:
                if (!string.IsNullOrEmpty(updated.ApiKey))
                {
                    candidate.ApiKey = updated.ApiKey;
                }

                break;
        }
    }

    private static Dictionary<string, string>? CloneStringDictionary(Dictionary<string, string>? source)
    {
        if (source == null || source.Count == 0)
            return null;

        return source.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);
    }

    private static Dictionary<string, JsonElement>? CloneJsonDictionary(Dictionary<string, JsonElement>? source)
    {
        if (source == null || source.Count == 0)
            return null;

        var cloned = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in source)
        {
            cloned[kv.Key] = kv.Value.Clone();
        }

        return cloned;
    }

    private static AiGenerationOptions CloneOptions(AiGenerationOptions options) => new()
    {
        Provider = options.Provider,
        ApiKey = options.ApiKey,
        Model = options.Model,
        MaxRetries = options.MaxRetries,
        TimeoutSeconds = options.TimeoutSeconds,
        MaxTokens = options.MaxTokens,
        Temperature = options.Temperature,
        BaseUrl = options.BaseUrl,
        Protocol = options.Protocol,
        WireApi = options.WireApi,
        AuthMode = options.AuthMode,
        AuthHeaderName = options.AuthHeaderName,
        ExtraHeaders = CloneStringDictionary(options.ExtraHeaders),
        ExtraQuery = CloneStringDictionary(options.ExtraQuery),
        ExtraBody = CloneJsonDictionary(options.ExtraBody),
        Capabilities = options.Capabilities?.Clone(),
        ReasoningMode = options.ReasoningMode,
        ReasoningEffort = options.ReasoningEffort
    };
}
