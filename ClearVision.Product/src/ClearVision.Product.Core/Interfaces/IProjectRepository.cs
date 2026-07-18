// IProjectRepository.cs
// 更新工程流程
// 作者：蘅芜君

using ClearVision.Product.Core.Entities;

namespace ClearVision.Product.Core.Interfaces;

/// <summary>
/// 工程仓储接口
/// </summary>
public interface IProjectRepository : IRepository<Project>
{
    /// <summary>
    /// Re-read a project from the database without returning an already tracked instance.
    /// </summary>
    Task<Project?> GetByIdFreshAsync(Guid id);

    /// <summary>
    /// Re-read a database-current tracked project instance for save-time updates.
    /// </summary>
    Task<Project?> GetByIdForUpdateAsync(Guid id);

    /// <summary>
    /// Reads a project regardless of its tombstone state. Lifecycle coordinators use
    /// this only to reconcile durable command outcomes; product reads remain filtered.
    /// </summary>
    Task<Project?> GetByIdIncludingDeletedAsync(Guid id);

    /// <summary>
    /// Atomically persists a reserved Project and its completed create operation.
    /// </summary>
    Task AddWithLifecycleOperationAsync(Project project, ProjectLifecycleOperation operation);

    /// <summary>
    /// Atomically persists the Project tombstone and completed delete operation.
    /// </summary>
    Task TombstoneWithLifecycleOperationAsync(Project project, ProjectLifecycleOperation operation);

    /// <summary>
    /// Updates only LastOpenedAt using server UTC authority. PersistenceRevision,
    /// ModifiedAt, Flow, variables and assets are not changed.
    /// </summary>
    Task<DateTime?> RecordOpenAsync(Guid id, DateTime openedAtUtc);

    /// <summary>
    /// 根据名称查找工程
    /// </summary>
    Task<Project?> GetByNameAsync(string name);

    /// <summary>
    /// 获取最近打开的工程列表
    /// </summary>
    Task<IEnumerable<Project>> GetRecentlyOpenedAsync(int count = 10);

    /// <summary>
    /// 搜索工程
    /// </summary>
    Task<IEnumerable<Project>> SearchAsync(string keyword);

    /// <summary>
    /// 获取工程及其流程
    /// </summary>
    Task<Project?> GetWithFlowAsync(Guid id);

    /// <summary>
    /// 更新工程流程
    /// </summary>
    Task UpdateFlowAsync(Project project);
}
