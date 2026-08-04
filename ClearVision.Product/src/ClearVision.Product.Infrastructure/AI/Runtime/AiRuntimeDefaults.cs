// AiRuntimeDefaults.cs
// AI 运行时默认实现
// 提供运行时适配器、默认连接器与基础策略
// 作者：蘅芜君
using ClearVision.Product.Contracts.Messages;

namespace ClearVision.Product.Infrastructure.AI.Runtime;

/// <summary>
/// Stage A connector adapter that routes requests to the existing AiApiClient.
/// This keeps behavior unchanged while switching the call site to the unified abstraction.
/// </summary>
public sealed class AiApiClientAdapterConnector : IAiConnector
{
    private readonly AiApiClient _apiClient;
    private readonly AiGenerationOptions _options;

    public AiApiClientAdapterConnector(AiApiClient apiClient, AiModelConfig modelConfig)
    {
        _apiClient = apiClient;
        _options = modelConfig.ToGenerationOptions();
    }

    public Task<AiCompletionResult> CompleteAsync(
        string systemPrompt,
        List<ChatMessage> messages,
        CancellationToken cancellationToken = default)
    {
        return _apiClient.CompleteAsync(systemPrompt, messages, _options, cancellationToken);
    }

    public Task<AiCompletionResult> StreamCompleteAsync(
        string systemPrompt,
        List<ChatMessage> messages,
        Action<AiStreamChunk> onChunk,
        CancellationToken cancellationToken = default)
    {
        return _apiClient.StreamCompleteAsync(systemPrompt, messages, onChunk, _options, cancellationToken);
    }
}

/// <summary>
/// Stage A default connector factory.
/// </summary>
public sealed class AiConnectorFactory : IAiConnectorFactory
{
    private readonly AiApiClient _apiClient;

    public AiConnectorFactory(AiApiClient apiClient)
    {
        _apiClient = apiClient;
    }

    public IAiConnector CreateConnector(AiModelConfig modelConfig)
    {
        return new AiApiClientAdapterConnector(_apiClient, modelConfig);
    }
}

/// <summary>
/// Stage A model registry adapter on top of AiConfigStore.
/// </summary>
public sealed class AiModelRegistry : IAiModelRegistry
{
    private readonly AiConfigStore _configStore;

    public AiModelRegistry(AiConfigStore configStore)
    {
        _configStore = configStore;
    }

    public AiModelConfig GetActiveModel()
    {
        var allModels = _configStore.GetAll();
        var active = allModels.FirstOrDefault(x => x.IsActive && x.IsEnabled) ??
                     allModels.FirstOrDefault(x => x.IsEnabled) ??
                     allModels.FirstOrDefault();
        if (active == null)
            throw new InvalidOperationException("No available AI model configuration.");

        return active;
    }

    public IReadOnlyList<AiModelConfig> GetAllModels()
    {
        return _configStore.GetAll();
    }
}

/// <summary>
/// Stage A selector: always use active profile regardless of role.
/// </summary>
public sealed class ActiveAiModelSelector : IAiModelSelector
{
    private readonly IAiModelRegistry _registry;

    public ActiveAiModelSelector(IAiModelRegistry registry)
    {
        _registry = registry;
    }

    public AiModelConfig SelectGenerationModel()
    {
        return _registry.GetActiveModel();
    }

    public AiModelConfig SelectModelForRole(string role)
    {
        return SelectGenerationModel();
    }

    public (AiModelConfig Model, string Reason) SelectModelForRoleWithReason(string role)
    {
        return (SelectGenerationModel(), "active");
    }
}

/// <summary>
/// Stage B+ selector: prefers the active model within a role, then uses Priority.
/// Falls back to the active model when no role-specific binding exists.
/// </summary>
public sealed class RoleAwareAiModelSelector : IAiModelSelector
{
    private readonly IAiModelRegistry _registry;

    public RoleAwareAiModelSelector(IAiModelRegistry registry)
    {
        _registry = registry;
    }

    public AiModelConfig SelectGenerationModel()
    {
        return SelectModelForRole("generation");
    }

    public AiModelConfig SelectModelForRole(string role)
    {
        return SelectModelForRoleWithReason(role).Model;
    }

    public (AiModelConfig Model, string Reason) SelectModelForRoleWithReason(string role)
    {
        var allModels = _registry.GetAllModels();
        var candidates = allModels
            .Where(m => m.IsEnabled
                && m.RoleBindings != null
                && m.RoleBindings.Contains(role, StringComparer.OrdinalIgnoreCase))
            .OrderByDescending(m => m.IsActive)
            .ThenBy(m => m.Priority ?? 100)
            .ToList();

        if (candidates.Count > 0)
            return (candidates[0], $"role_binding:{role}");

        // Fallback: return the active model
        return (_registry.GetActiveModel(), "active");
    }
}
