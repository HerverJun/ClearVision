// IAiModelSelector.cs
// AI 模型选择器接口
// 定义按请求上下文选择目标模型的策略契约
// 作者：蘅芜君
namespace ClearVision.Product.Infrastructure.AI.Runtime;

/// <summary>
/// Selects model profiles for different runtime intents.
/// </summary>
public interface IAiModelSelector
{
    AiModelConfig SelectGenerationModel();

    /// <summary>
    /// Selects a model bound to the specified role.
    /// Falls back to the active model when no role-specific binding exists.
    /// </summary>
    AiModelConfig SelectModelForRole(string role);

    /// <summary>
    /// Selects a model for the specified role and returns the selection reason.
    /// </summary>
    (AiModelConfig Model, string Reason) SelectModelForRoleWithReason(string role);
}
