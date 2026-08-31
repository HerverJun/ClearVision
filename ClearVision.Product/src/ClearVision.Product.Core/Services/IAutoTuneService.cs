// IAutoTuneService.cs
// 自动调参服务接口
// 【Phase 4】LLM 闭环验证 - 自动调参
// 作者：架构修复方案 v2

using ClearVision.Product.Core.Entities;

namespace ClearVision.Product.Core.Services;

/// <summary>
/// 自动调参服务
/// 职责：根据执行反馈自动调整参数，迭代优化直到达到目标
/// </summary>
public interface IAutoTuneService
{
    /// <summary>
    /// 场景级自动调参
    /// </summary>
    /// <param name="scenarioKey">场景键</param>
    /// <param name="flow">完整流程</param>
    /// <param name="inputImage">输入图像</param>
    /// <param name="goal">调参目标</param>
    /// <param name="maxIterations">最大迭代次数</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>场景级调参结果</returns>
    Task<ScenarioAutoTuneResult> AutoTuneScenarioAsync(
        string scenarioKey,
        OperatorFlow flow,
        byte[] inputImage,
        AutoTuneGoal goal,
        Guid projectId,
        long persistenceRevision,
        ExecutionRequestAuthority authority,
        int maxIterations = 5,
        CancellationToken ct = default);
}
