// IProjectFlowStorage.cs
// 加载流程 JSON 字符串
// 作者：蘅芜君

namespace ClearVision.Product.Core.Interfaces;

/// <summary>
/// 负责存储项目流程数据的接口
/// </summary>
public interface IProjectFlowStorage
{
    /// <summary>
    /// 保存流程 JSON 字符串
    /// </summary>
    Task SaveFlowJsonAsync(Guid projectId, string flowJson);

    Task SaveFlowJsonAsync(Guid projectId, string flowJson, long persistenceRevision)
    {
        return SaveFlowJsonAsync(projectId, flowJson);
    }

    /// <summary>
    /// 加载流程 JSON 字符串
    /// </summary>
    Task<string?> LoadFlowJsonAsync(Guid projectId);

    Task<ProjectFlowStorageMetadata?> LoadMetadataAsync(Guid projectId)
    {
        return Task.FromResult<ProjectFlowStorageMetadata?>(null);
    }

    Task DeleteFlowJsonAsync(Guid projectId)
    {
        return Task.CompletedTask;
    }
}

public sealed record ProjectFlowStorageMetadata(
    int SchemaVersion,
    Guid ProjectId,
    long PersistenceRevision,
    string FlowHash,
    DateTimeOffset SavedAtUtc);
