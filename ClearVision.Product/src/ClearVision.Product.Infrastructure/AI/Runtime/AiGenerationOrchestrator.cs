// AiGenerationOrchestrator.cs
// AI 生成编排器
// 统一编排模型选择、调用链路与结果落地
// 作者：蘅芜君
using ClearVision.Product.Contracts.Messages;

namespace ClearVision.Product.Infrastructure.AI.Runtime;

/// <summary>
/// Unified runtime orchestrator used by generation services.
/// </summary>
public sealed class AiGenerationOrchestrator
{
    private readonly IAiModelSelector _modelSelector;
    private readonly IAiConnectorFactory _connectorFactory;

    public AiGenerationOrchestrator(
        IAiModelSelector modelSelector,
        IAiConnectorFactory connectorFactory)
    {
        _modelSelector = modelSelector;
        _connectorFactory = connectorFactory;
    }

    public AiModelConfig ResolveGenerationModel()
    {
        return _modelSelector.SelectGenerationModel();
    }

    /// <summary>
    /// Returns the selection reason for the generation model.
    /// </summary>
    public string ResolveSelectionReason()
    {
        return _modelSelector.SelectModelForRoleWithReason("generation").Reason;
    }

    /// <summary>
    /// Resolves the model for a specific runtime role (generation, reasoning, fallback, validation).
    /// </summary>
    public AiModelConfig ResolveModelForRole(string role)
    {
        return _modelSelector.SelectModelForRole(role);
    }

    /// <summary>
    /// Resolves a fallback model: tries "fallback" role first, then the active model.
    /// Returns the fallback model only if it differs from the primary (i.e., a real fallback binding exists).
    /// </summary>
    public AiModelConfig ResolveFallbackModel()
    {
        var fallback = _modelSelector.SelectModelForRole("fallback");
        var primary = _modelSelector.SelectModelForRole("generation");
        return fallback.Id != primary.Id ? fallback : primary;
    }

    public AiModelCapabilities ResolveCapabilities(AiModelConfig? modelConfig = null)
    {
        var model = modelConfig ?? ResolveGenerationModel();
        return model.GetEffectiveCapabilities();
    }

    public bool SupportsVisionInput(AiModelConfig? modelConfig = null)
    {
        return ResolveCapabilities(modelConfig).SupportsVisionInput;
    }

    public Task<AiCompletionResult> CompleteAsync(
        string systemPrompt,
        List<ChatMessage> messages,
        AiModelConfig? modelConfig = null,
        CancellationToken cancellationToken = default)
    {
        var model = modelConfig ?? ResolveGenerationModel();
        var connector = _connectorFactory.CreateConnector(model);
        return connector.CompleteAsync(systemPrompt, messages, cancellationToken);
    }

    public Task<AiCompletionResult> CompleteWithToolsAsync(
        string systemPrompt,
        List<ChatMessage> messages,
        IReadOnlyList<AiNativeToolDefinition> tools,
        AiModelConfig? modelConfig = null,
        CancellationToken cancellationToken = default)
    {
        var model = modelConfig ?? ResolveGenerationModel();
        var connector = _connectorFactory.CreateConnector(model);
        if (connector is IAiToolCallingConnector toolCallingConnector)
        {
            return toolCallingConnector.CompleteWithToolsAsync(systemPrompt, messages, tools, cancellationToken);
        }

        return connector.CompleteAsync(systemPrompt, messages, cancellationToken);
    }

    public Task<AiCompletionResult> StreamCompleteAsync(
        string systemPrompt,
        List<ChatMessage> messages,
        Action<AiStreamChunk> onChunk,
        AiModelConfig? modelConfig = null,
        CancellationToken cancellationToken = default)
    {
        var model = modelConfig ?? ResolveGenerationModel();
        var connector = _connectorFactory.CreateConnector(model);
        return connector.StreamCompleteAsync(systemPrompt, messages, onChunk, cancellationToken);
    }
}
