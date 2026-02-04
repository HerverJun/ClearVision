using Acme.Product.Application.DTOs;
using Acme.Product.Core.Entities;
using Acme.Product.Core.Exceptions;
using Acme.Product.Core.Interfaces;
using Acme.Product.Core.ValueObjects;

namespace Acme.Product.Application.Services;

/// <summary>
/// 工程应用服务
/// </summary>
public class ProjectService
{
    private readonly IProjectRepository _projectRepository;

    public ProjectService(IProjectRepository projectRepository)
    {
        _projectRepository = projectRepository;
    }

    /// <summary>
    /// 创建工程
    /// </summary>
    public async Task<ProjectDto> CreateAsync(CreateProjectRequest request)
    {
        var project = new Project(request.Name, request.Description);
        await _projectRepository.AddAsync(project);
        return MapToDto(project);
    }

    /// <summary>
    /// 获取工程
    /// </summary>
    public async Task<ProjectDto?> GetByIdAsync(Guid id)
    {
        var project = await _projectRepository.GetByIdAsync(id);
        return project != null ? MapToDto(project) : null;
    }

    /// <summary>
    /// 获取所有工程
    /// </summary>
    public async Task<IEnumerable<ProjectDto>> GetAllAsync()
    {
        var projects = await _projectRepository.GetAllAsync();
        return projects.Select(MapToDto);
    }

    /// <summary>
    /// 更新工程
    /// </summary>
    public async Task<ProjectDto> UpdateAsync(Guid id, UpdateProjectRequest request)
    {
        var project = await _projectRepository.GetByIdAsync(id);
        if (project == null)
            throw new ProjectNotFoundException(id);

        project.UpdateInfo(request.Name, request.Description);
        
        // 【修复】如果有流程数据，更新流程
        if (request.Flow != null)
        {
            var flow = MapDtoToFlow(request.Flow);
            project.UpdateFlow(flow);
        }
        
        await _projectRepository.UpdateAsync(project);
        return MapToDto(project);
    }
    
    /// <summary>
    /// 将OperatorFlowDto转换为Core实体
    /// </summary>
    private OperatorFlow MapDtoToFlow(OperatorFlowDto dto)
    {
        var flow = new OperatorFlow(dto.Name);
        
        // 添加算子
        foreach (var opDto in dto.Operators)
        {
            var op = new Operator(
                opDto.Name,
                opDto.Type,
                opDto.X,
                opDto.Y
            );
            
            // 设置ID（如果提供了）
            if (opDto.Id != Guid.Empty)
            {
                // 使用反射设置ID，因为构造函数会生成新的ID
                typeof(Operator).GetProperty("Id")?.SetValue(op, opDto.Id);
            }
            
            // 添加参数
            foreach (var paramDto in opDto.Parameters)
            {
                var param = new Parameter(
                    paramDto.Id == Guid.Empty ? Guid.NewGuid() : paramDto.Id,
                    paramDto.Name,
                    paramDto.DisplayName,
                    paramDto.Description ?? string.Empty,
                    paramDto.DataType,
                    paramDto.DefaultValue,
                    paramDto.MinValue,
                    paramDto.MaxValue,
                    paramDto.IsRequired
                );
                
                if (paramDto.Value != null)
                {
                    param.SetValue(paramDto.Value);
                }
                
                op.AddParameter(param);
            }
            
            flow.AddOperator(op);
        }
        
        // 添加连接
        foreach (var connDto in dto.Connections)
        {
            var connection = new OperatorConnection(
                connDto.SourceOperatorId,
                connDto.TargetOperatorId,
                connDto.SourcePortId,
                connDto.TargetPortId
            );
            
            // 设置连接ID
            if (connDto.Id != Guid.Empty)
            {
                typeof(OperatorConnection).GetProperty("Id")?.SetValue(connection, connDto.Id);
            }
            
            flow.AddConnection(connection);
        }
        
        return flow;
    }

    /// <summary>
    /// 删除工程
    /// </summary>
    public async Task DeleteAsync(Guid id)
    {
        var project = await _projectRepository.GetByIdAsync(id);
        if (project == null)
            throw new ProjectNotFoundException(id);

        project.MarkAsDeleted();
        await _projectRepository.UpdateAsync(project);
    }

    /// <summary>
    /// 搜索工程
    /// </summary>
    public async Task<IEnumerable<ProjectDto>> SearchAsync(string keyword)
    {
        var projects = await _projectRepository.SearchAsync(keyword);
        return projects.Select(MapToDto);
    }

    /// <summary>
    /// 获取最近打开的工程
    /// </summary>
    public async Task<IEnumerable<ProjectDto>> GetRecentlyOpenedAsync(int count = 10)
    {
        var projects = await _projectRepository.GetRecentlyOpenedAsync(count);
        return projects.Select(MapToDto);
    }

    private ProjectDto MapToDto(Project project)
    {
        return new ProjectDto
        {
            Id = project.Id,
            Name = project.Name,
            Description = project.Description,
            Version = project.Version,
            CreatedAt = project.CreatedAt,
            ModifiedAt = project.ModifiedAt,
            LastOpenedAt = project.LastOpenedAt,
            GlobalSettings = project.GlobalSettings
        };
    }
}
