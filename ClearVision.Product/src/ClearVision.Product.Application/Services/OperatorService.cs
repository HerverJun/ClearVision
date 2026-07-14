// OperatorService.cs
// 初始化算子元数据缓存
// 作者：蘅芜君

using ClearVision.Product.Application.DTOs;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Exceptions;
using ClearVision.Product.Core.Interfaces;
using ClearVision.Product.Core.Operators;
using ClearVision.Product.Core.Services;
using ClearVision.Product.Core.ValueObjects;
using Microsoft.Extensions.Logging;

namespace ClearVision.Product.Application.Services;

/// <summary>
/// 算子服务实现
/// Sprint 4: S4-004 实现
/// </summary>
public class OperatorService : IOperatorService
{
    private readonly IOperatorRepository _operatorRepository;
    private readonly IOperatorFactory _operatorFactory;
    private readonly ILogger<OperatorService>? _logger;
    private readonly Dictionary<OperatorType, OperatorMetadataDto> _operatorMetadataCache = new();

    public OperatorService(
        IOperatorRepository operatorRepository,
        IOperatorFactory operatorFactory)
        : this(operatorRepository, operatorFactory, null)
    {
    }

    public OperatorService(
        IOperatorRepository operatorRepository,
        IOperatorFactory operatorFactory,
        ILogger<OperatorService>? logger)
    {
        _operatorRepository = operatorRepository;
        _operatorFactory = operatorFactory;
        _logger = logger;
        InitializeMetadataCache();
    }

    /// <summary>
    /// 初始化算子元数据缓存
    /// </summary>
    private void InitializeMetadataCache()
    {
        _operatorMetadataCache.Clear();
        foreach (var metadata in _operatorFactory.GetAllMetadata())
        {
            _operatorMetadataCache[metadata.Type] = MapFactoryMetadata(metadata);
        }
    }

    private static OperatorMetadataDto MapFactoryMetadata(OperatorMetadata metadata)
    {
        return new OperatorMetadataDto
        {
            Id = Guid.NewGuid(),
            Type = metadata.Type.ToString(),
            DisplayName = metadata.DisplayName,
            CategoryId = metadata.CategoryId.ToString(),
            Category = metadata.Category,
            CategoryOrder = OperatorCategoryCatalog.GetOrder(metadata.CategoryId),
            Lifecycle = metadata.Lifecycle.ToString(),
            LifecycleNote = metadata.LifecycleNote,
            DefaultHidden = metadata.DefaultHidden,
            Icon = metadata.IconName ?? string.Empty,
            Description = metadata.Description,
            Keywords = metadata.Keywords?.ToArray() ?? Array.Empty<string>(),
            Inputs = metadata.InputPorts.Select(MapPortDefinition).ToList(),
            Outputs = metadata.OutputPorts.Select(MapPortDefinition).ToList(),
            Parameters = metadata.Parameters.Select(MapParameterDefinition).ToList(),
            ParameterConstraints = metadata.ParameterConstraints.ToList(),
            OutputAvailabilityRules = metadata.OutputAvailabilityRules.ToList()
        };
    }

    private static PortDefinitionDto MapPortDefinition(PortDefinition definition)
    {
        return new PortDefinitionDto
        {
            Name = definition.Name,
            DisplayName = definition.DisplayName,
            DataType = definition.DataType,
            IsRequired = definition.IsRequired,
            Description = definition.Description ?? string.Empty
        };
    }

    private static ParameterDefinitionDto MapParameterDefinition(ParameterDefinition definition)
    {
        return new ParameterDefinitionDto
        {
            Name = definition.Name,
            DisplayName = definition.DisplayName,
            Description = definition.Description ?? string.Empty,
            DataType = definition.DataType,
            DefaultValue = definition.DefaultValue,
            MinValue = definition.MinValue,
            MaxValue = definition.MaxValue,
            IsRequired = definition.IsRequired,
            Options = definition.Options?.Select(option => new ParameterOptionDto
            {
                Label = option.Label,
                Value = option.Value
            }).ToList()
        };
    }

    public Task<IEnumerable<OperatorMetadataDto>> GetLibraryAsync()
    {
        return Task.FromResult(_operatorMetadataCache.Values.AsEnumerable());
    }

    public Task<OperatorDto?> GetByIdAsync(Guid id)
    {
        // 从元数据缓存中查找
        var meta = _operatorMetadataCache.Values.FirstOrDefault(m => m.Id == id);
        if (meta == null)
            return Task.FromResult<OperatorDto?>(null);

        var dto = MapToDto(meta);
        return Task.FromResult<OperatorDto?>(dto);
    }

    public Task<OperatorDto?> GetByTypeAsync(OperatorType type)
    {
        if (!_operatorMetadataCache.TryGetValue(type, out var meta))
            return Task.FromResult<OperatorDto?>(null);

        var dto = MapToDto(meta);
        return Task.FromResult<OperatorDto?>(dto);
    }

    public Task<OperatorDto> CreateAsync(CreateOperatorRequest request)
    {
        // 使用工厂创建算子实例，确保端口和参数正确初始化
        var operatorEntity = _operatorFactory.CreateOperator(
            request.Type,
            request.Name,
            100, 100
        );

        // 如果请求中提供了参数，覆盖默认值
        if (request.Parameters != null)
        {
            foreach (var param in request.Parameters)
            {
                if (!string.IsNullOrEmpty(param.Name) && param.Value != null)
                {
                    try
                    {
                        operatorEntity.UpdateParameter(param.Name, param.Value);
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogWarning(ex, "Ignored invalid operator parameter while creating {OperatorType}: {ParameterName}", request.Type, param.Name);
                    }
                }
            }
        }

        var dto = MapEntityToDto(operatorEntity);
        return Task.FromResult(dto);
    }

    public async Task<OperatorDto> UpdateAsync(Guid id, UpdateOperatorRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        // 从仓储获取算子实体
        var entity = await _operatorRepository.GetByIdAsync(id);
        if (entity == null)
        {
            throw new OperatorNotFoundException(id);
        }

        // 更新名称
        if (!string.IsNullOrWhiteSpace(request.Name))
        {
            entity.UpdateName(request.Name);
        }

        // 更新参数
        if (request.Parameters != null)
        {
            foreach (var param in request.Parameters)
            {
                if (!string.IsNullOrWhiteSpace(param.Name) && param.Value != null)
                {
                    try
                    {
                        entity.UpdateParameter(param.Name, param.Value);
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogWarning(ex, "Ignored invalid operator parameter while updating {OperatorId}: {ParameterName}", id, param.Name);
                    }
                }
            }
        }

        // 保存到仓储
        await _operatorRepository.UpdateAsync(entity);

        // 返回更新后的DTO
        return MapEntityToDto(entity);
    }

    public async Task DeleteAsync(Guid id)
    {
        // 从仓储获取算子实体
        var entity = await _operatorRepository.GetByIdAsync(id);
        if (entity == null)
        {
            throw new OperatorNotFoundException(id);
        }

        // 从仓储删除
        await _operatorRepository.DeleteAsync(entity);
    }

    public Task<ValidationResultDto> ValidateParametersAsync(Guid operatorId, Dictionary<string, object> parameters)
    {
        var result = new ValidationResultDto { IsValid = true };

        var meta = _operatorMetadataCache.Values.FirstOrDefault(m => m.Id == operatorId);
        if (meta == null)
        {
            result.IsValid = false;
            result.Errors.Add("算子不存在");
            return Task.FromResult(result);
        }

        // 验证必填参数
        foreach (var param in meta.Parameters.Where(p => p.IsRequired))
        {
            if (!parameters.ContainsKey(param.Name) || parameters[param.Name] == null)
            {
                result.IsValid = false;
                result.Errors.Add($"必填参数 '{param.DisplayName}' 未提供");
            }
        }

        return Task.FromResult(result);
    }

    public Task<IEnumerable<OperatorTypeInfoDto>> GetOperatorTypesAsync()
    {
        var types = _operatorMetadataCache.Values.Select(m => new OperatorTypeInfoDto
        {
            Type = m.Type,
            DisplayName = m.DisplayName,
            CategoryId = m.CategoryId,
            Category = m.Category,
            CategoryOrder = m.CategoryOrder,
            Lifecycle = m.Lifecycle,
            LifecycleNote = m.LifecycleNote,
            DefaultHidden = m.DefaultHidden,
            Icon = m.Icon
        });

        return Task.FromResult(types);
    }

    public Task<OperatorMetadataDto?> GetMetadataAsync(OperatorType type)
    {
        if (!_operatorMetadataCache.TryGetValue(type, out var meta))
            return Task.FromResult<OperatorMetadataDto?>(null);

        return Task.FromResult<OperatorMetadataDto?>(meta);
    }

    private OperatorDto MapToDto(OperatorMetadataDto meta)
    {
        return new OperatorDto
        {
            Id = meta.Id,
            Name = meta.DisplayName,
            Type = Enum.Parse<OperatorType>(meta.Type),
            X = 0,
            Y = 0,
            Parameters = meta.Parameters.Select(p => new ParameterDto
            {
                Name = p.Name,
                DisplayName = p.DisplayName,
                DataType = p.DataType,
                Value = p.DefaultValue
            }).ToList(),
            InputPorts = meta.Inputs.Select(i => new PortDto
            {
                Id = Guid.NewGuid(),
                Name = i.Name,
                DataType = i.DataType,
                Direction = PortDirection.Input
            }).ToList(),
            OutputPorts = meta.Outputs.Select(o => new PortDto
            {
                Id = Guid.NewGuid(),
                Name = o.Name,
                DataType = o.DataType,
                Direction = PortDirection.Output
            }).ToList()
        };
    }

    private OperatorDto MapEntityToDto(Operator entity)
    {
        return new OperatorDto
        {
            Id = entity.Id,
            Name = entity.Name,
            Type = entity.Type,
            X = entity.Position.X,
            Y = entity.Position.Y,
            Parameters = entity.Parameters.Select(p => new ParameterDto
            {
                Name = p.Name,
                DisplayName = p.DisplayName,
                DataType = p.DataType,
                Value = p.GetValue()
            }).ToList()
        };
    }
}
