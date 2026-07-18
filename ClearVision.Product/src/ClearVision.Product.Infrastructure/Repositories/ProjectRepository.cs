// ProjectRepository.cs
// 更新工程流程 - 显式处理 EF Core 变更追踪
// 作者：蘅芜君

using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace ClearVision.Product.Infrastructure.Repositories;

/// <summary>
/// 工程仓储实现
/// </summary>
public class ProjectRepository : RepositoryBase<Project>, IProjectRepository
{
    public ProjectRepository(Data.VisionDbContext context) : base(context)
    {
    }

    /// <summary>
    /// 获取所有未删除的工程
    /// </summary>
    public override async Task<IEnumerable<Project>> GetAllAsync()
    {
        return await _dbSet
            .Where(p => !p.IsDeleted)
            .OrderByDescending(p => p.ModifiedAt)
            .ToListAsync();
    }

    public override async Task<Project?> GetByIdAsync(Guid id)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Project id cannot be empty.", nameof(id));
        }

        var project = await _dbSet
            .FirstOrDefaultAsync(p => p.Id == id && !p.IsDeleted);
        return project is { IsDeleted: false } ? project : null;
    }

    public override async Task<bool> ExistsAsync(Guid id)
    {
        if (id == Guid.Empty)
        {
            return false;
        }

        return await _dbSet.AnyAsync(p => p.Id == id && !p.IsDeleted);
    }

    public async Task<Project?> GetByIdFreshAsync(Guid id)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Project id cannot be empty.", nameof(id));
        }

        return await _dbSet
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == id && !p.IsDeleted);
    }

    public async Task<Project?> GetByIdForUpdateAsync(Guid id)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Project id cannot be empty.", nameof(id));
        }

        var trackedEntry = _context.ChangeTracker.Entries<Project>()
            .FirstOrDefault(entry => entry.Entity.Id == id);
        if (trackedEntry != null)
        {
            await trackedEntry.ReloadAsync();
            return trackedEntry.State == EntityState.Detached || trackedEntry.Entity.IsDeleted
                ? null
                : trackedEntry.Entity;
        }

        return await _dbSet.FirstOrDefaultAsync(p => p.Id == id && !p.IsDeleted);
    }

    public async Task<Project?> GetByIdIncludingDeletedAsync(Guid id)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Project id cannot be empty.", nameof(id));
        }

        return await _dbSet
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == id);
    }

    public async Task AddWithLifecycleOperationAsync(
        Project project,
        ProjectLifecycleOperation operation)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(operation);

        await _dbSet.AddAsync(project);
        _context.Set<ProjectLifecycleOperation>().Update(operation);
        try
        {
            await _context.SaveChangesAsync();
        }
        catch
        {
            _context.Entry(project).State = EntityState.Detached;
            await _context.Entry(operation).ReloadAsync();
            throw;
        }
    }

    public async Task TombstoneWithLifecycleOperationAsync(
        Project project,
        ProjectLifecycleOperation operation)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(operation);

        _dbSet.Update(project);
        _context.Set<ProjectLifecycleOperation>().Update(operation);
        try
        {
            await _context.SaveChangesAsync();
        }
        catch
        {
            await _context.Entry(project).ReloadAsync();
            await _context.Entry(operation).ReloadAsync();
            throw;
        }
    }

    public async Task<DateTime?> RecordOpenAsync(Guid id, DateTime openedAtUtc)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Project id cannot be empty.", nameof(id));
        }

        if (openedAtUtc.Kind == DateTimeKind.Local)
        {
            openedAtUtc = openedAtUtc.ToUniversalTime();
        }
        else if (openedAtUtc.Kind == DateTimeKind.Unspecified)
        {
            openedAtUtc = DateTime.SpecifyKind(openedAtUtc, DateTimeKind.Utc);
        }

        var affected = await _dbSet
            .Where(project => project.Id == id && !project.IsDeleted)
            .ExecuteUpdateAsync(setters => setters.SetProperty(
                project => project.LastOpenedAt,
                project => project.LastOpenedAt == null || project.LastOpenedAt < openedAtUtc
                    ? openedAtUtc
                    : project.LastOpenedAt));
        if (affected == 0)
        {
            return null;
        }

        return await _dbSet
            .AsNoTracking()
            .Where(project => project.Id == id && !project.IsDeleted)
            .Select(project => project.LastOpenedAt)
            .SingleAsync();
    }

    public async Task<Project?> GetByNameAsync(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        return await _dbSet
            .FirstOrDefaultAsync(p => p.Name == name && !p.IsDeleted);
    }

    public async Task<IEnumerable<Project>> GetRecentlyOpenedAsync(int count = 10)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(count, 0);

        return await _dbSet
            .Where(p => !p.IsDeleted && p.LastOpenedAt != null)
            .OrderByDescending(p => p.LastOpenedAt)
            .Take(count)
            .ToListAsync();
    }

    public async Task<IEnumerable<Project>> SearchAsync(string keyword)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyword);

        return await _dbSet
            .Where(p => !p.IsDeleted &&
                (p.Name.Contains(keyword) ||
                 (p.Description != null && p.Description.Contains(keyword))))
            .ToListAsync();
    }

    public async Task<Project?> GetWithFlowAsync(Guid id)
    {
        ArgumentException.ThrowIfNullOrEmpty(nameof(id));

        return await _dbSet
            .Include(p => p.Flow)
                .ThenInclude(f => f.Operators)
                    .ThenInclude(o => o.InputPorts)
            .Include(p => p.Flow)
                .ThenInclude(f => f.Operators)
                    .ThenInclude(o => o.OutputPorts)
            .Include(p => p.Flow)
                .ThenInclude(f => f.Operators)
                    .ThenInclude(o => o.Parameters)
            .Include(p => p.Flow)
                .ThenInclude(f => f.Connections)
            .FirstOrDefaultAsync(p => p.Id == id && !p.IsDeleted);
    }

    /// <summary>
    /// 更新工程流程 - 显式处理 EF Core 变更追踪
    /// </summary>
    public async Task UpdateFlowAsync(Project project)
    {
        // 【修复】遍历流程中的所有算子，将新添加的算子添加到 Operators DbSet
        // 对于 owned entities (Port, Parameter)，EF Core 会自动处理
        foreach (var op in project.Flow.Operators)
        {
            var entry = _context.Entry(op);
            // 如果实体状态是 Detached，说明是新创建的
            if (entry.State == Microsoft.EntityFrameworkCore.EntityState.Detached)
            {
                // 设置外键以关联到 Project/Flow
                typeof(Operator).GetProperty("ProjectId")?.SetValue(op, project.Id);
                // 添加到 DbSet，EF Core 会自动处理 owned entities
                _context.Set<Operator>().Add(op);
            }
        }



        await _context.SaveChangesAsync();
    }
}
