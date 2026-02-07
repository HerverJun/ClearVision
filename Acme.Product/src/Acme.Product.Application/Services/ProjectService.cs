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
            // 传入 Project.Id 以确保 Table Splitting ID 一致
            var flow = MapDtoToFlow(request.Flow, project.Id);
            project.UpdateFlow(flow);
        }

        await _projectRepository.UpdateAsync(project);
        return MapToDto(project);
    }

    /// <summary>
    /// 更新工程流程
    /// </summary>
    public async Task UpdateFlowAsync(Guid id, UpdateFlowRequest request)
    {
        // 使用 GetWithFlowAsync 确保加载现有关联数据
        var project = await _projectRepository.GetWithFlowAsync(id);
        if (project == null)
            throw new ProjectNotFoundException(id);

        // 构造流程DTO
        var flowDto = new OperatorFlowDto
        {
            Name = project.Flow.Name, // 保持原有名称
            Operators = request.Operators,
            Connections = request.Connections
        };

        // 使用已修复的 MapDtoToFlow 逻辑 (包含端口恢复)
        // 传入 Project.Id 以确保 Table Splitting ID 一致
        var flow = MapDtoToFlow(flowDto, project.Id);

        // 更新到实体
        project.UpdateFlow(flow);

        await _projectRepository.UpdateAsync(project);
    }

    /// <summary>
    /// 将OperatorFlowDto转换为Core实体
    /// </summary>
    private OperatorFlow MapDtoToFlow(OperatorFlowDto dto, Guid? flowId = null)
    {
        var flow = new OperatorFlow(dto.Name);

        // 【关键修复】如果指定了 flowId (通常是 Project.Id)，强制设置它
        // EF Core Table Splitting 要求 Project.Id == Flow.Id
        if (flowId.HasValue)
        {
            // Flow继承自Entity，Id定义在Entity中
            typeof(Acme.Product.Core.Entities.Base.Entity)
                .GetProperty("Id")?
                .SetValue(flow, flowId.Value);
        }

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

            // 恢复输入端口（保留ID以维持连线）
            foreach (var portDto in opDto.InputPorts)
            {
                op.LoadInputPort(portDto.Id, portDto.Name, portDto.DataType, portDto.IsRequired);
            }

            // 恢复输出端口（保留ID以维持连线）
            foreach (var portDto in opDto.OutputPorts)
            {
                op.LoadOutputPort(portDto.Id, portDto.Name, portDto.DataType);
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
