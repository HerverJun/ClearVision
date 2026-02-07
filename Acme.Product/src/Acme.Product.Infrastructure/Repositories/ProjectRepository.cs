using Acme.Product.Core.Entities;
using Acme.Product.Core.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Acme.Product.Infrastructure.Repositories;

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
            .FirstOrDefaultAsync(p => p.Id == id && !p.IsDeleted);
    }
}
