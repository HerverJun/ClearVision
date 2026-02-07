using Acme.Product.Application.DTOs;
using Acme.Product.Core.Entities;
using Acme.Product.Core.Enums;
using Acme.Product.Core.Exceptions;
using Acme.Product.Core.Interfaces;
using Acme.Product.Core.Operators;
using Acme.Product.Core.Services;
using Acme.Product.Core.ValueObjects;

namespace Acme.Product.Application.Services;

/// <summary>
/// 算子服务实现
/// Sprint 4: S4-004 实现
/// </summary>
public class OperatorService : IOperatorService
{
    private readonly IOperatorRepository _operatorRepository;
    private readonly IOperatorFactory _operatorFactory;
    private static readonly Dictionary<OperatorType, OperatorMetadataDto> OperatorMetadataCache = new();

    public OperatorService(
        IOperatorRepository operatorRepository,
        IOperatorFactory operatorFactory)
    {
        _operatorRepository = operatorRepository;
        _operatorFactory = operatorFactory;
        InitializeMetadataCache();
    }

    /// <summary>
    /// 初始化算子元数据缓存
    /// </summary>
    private void InitializeMetadataCache()
    {
        if (OperatorMetadataCache.Count > 0)
            return;

        var metadata = new List<OperatorMetadataDto>
        {
            new()
            {
                Id = Guid.NewGuid(),
                Type = "ImageAcquisition",
                DisplayName = "图像采集",
                Category = "输入",
                Icon = "📷",
                Description = "从相机或文件获取图像",
                Inputs = new List<PortDefinitionDto>(),
                Outputs = new List<PortDefinitionDto>
                {
                    new() { Name = "image", DisplayName = "图像", DataType = PortDataType.Image, IsRequired = true }
                },
                Parameters = new List<ParameterDefinitionDto>
                {
                    new() { Name = "source", DisplayName = "数据源", DataType = "enum", DefaultValue = "file", IsRequired = true },
                    new() { Name = "path", DisplayName = "文件路径", DataType = "string", DefaultValue = "", IsRequired = false }
                }
            },
            new()
            {
                Id = Guid.NewGuid(),
                Type = "Filtering",
                DisplayName = "滤波",
                Category = "预处理",
                Icon = "🔍",
                Description = "图像滤波降噪处理",
                Inputs = new List<PortDefinitionDto>
                {
                    new() { Name = "image", DisplayName = "输入图像", DataType = PortDataType.Image, IsRequired = true }
                },
                Outputs = new List<PortDefinitionDto>
                {
                    new() { Name = "image", DisplayName = "输出图像", DataType = PortDataType.Image, IsRequired = true }
                },
                Parameters = new List<ParameterDefinitionDto>
                {
                    new() { Name = "method", DisplayName = "滤波方法", DataType = "enum", DefaultValue = "gaussian", IsRequired = true },
                    new() { Name = "kernelSize", DisplayName = "核大小", DataType = "int", DefaultValue = 5, MinValue = 3, MaxValue = 31, IsRequired = true },
                    new() { Name = "sigma", DisplayName = "Sigma", DataType = "double", DefaultValue = 1.0, MinValue = 0.1, MaxValue = 10.0, IsRequired = true }
                }
            },
            new()
            {
                Id = Guid.NewGuid(),
                Type = "EdgeDetection",
                DisplayName = "边缘检测",
                Category = "特征提取",
                Icon = "〰️",
                Description = "检测图像边缘特征",
                Inputs = new List<PortDefinitionDto>
                {
                    new() { Name = "image", DisplayName = "输入图像", DataType = PortDataType.Image, IsRequired = true }
                },
                Outputs = new List<PortDefinitionDto>
                {
                    new() { Name = "image", DisplayName = "边缘图像", DataType = PortDataType.Image, IsRequired = true }
                },
                Parameters = new List<ParameterDefinitionDto>
                {
                    new() { Name = "method", DisplayName = "检测方法", DataType = "enum", DefaultValue = "canny", IsRequired = true },
                    new() { Name = "threshold1", DisplayName = "低阈值", DataType = "double", DefaultValue = 50.0, MinValue = 0, MaxValue = 255, IsRequired = true },
                    new() { Name = "threshold2", DisplayName = "高阈值", DataType = "double", DefaultValue = 150.0, MinValue = 0, MaxValue = 255, IsRequired = true }
                }
            },
            new()
            {
                Id = Guid.NewGuid(),
                Type = "Thresholding",
                DisplayName = "二值化",
                Category = "预处理",
                Icon = "⚫",
                Description = "图像阈值分割",
                Inputs = new List<PortDefinitionDto>
                {
                    new() { Name = "image", DisplayName = "输入图像", DataType = PortDataType.Image, IsRequired = true }
                },
                Outputs = new List<PortDefinitionDto>
                {
                    new() { Name = "image", DisplayName = "二值图像", DataType = PortDataType.Image, IsRequired = true }
                },
                Parameters = new List<ParameterDefinitionDto>
                {
                    new() { Name = "method", DisplayName = "阈值方法", DataType = "enum", DefaultValue = "otsu", IsRequired = true },
                    new() { Name = "threshold", DisplayName = "阈值", DataType = "int", DefaultValue = 127, MinValue = 0, MaxValue = 255, IsRequired = false }
                }
            },
            new()
            {
                Id = Guid.NewGuid(),
                Type = "Morphology",
                DisplayName = "形态学",
                Category = "预处理",
                Icon = "🔄",
                Description = "腐蚀、膨胀、开闭运算",
                Inputs = new List<PortDefinitionDto>
                {
                    new() { Name = "image", DisplayName = "输入图像", DataType = PortDataType.Image, IsRequired = true }
                },
                Outputs = new List<PortDefinitionDto>
                {
                    new() { Name = "image", DisplayName = "输出图像", DataType = PortDataType.Image, IsRequired = true }
                },
                Parameters = new List<ParameterDefinitionDto>
                {
                    new() { Name = "operation", DisplayName = "操作", DataType = "enum", DefaultValue = "open", IsRequired = true },
                    new() { Name = "kernelSize", DisplayName = "核大小", DataType = "int", DefaultValue = 3, MinValue = 1, MaxValue = 21, IsRequired = true },
                    new() { Name = "iterations", DisplayName = "迭代次数", DataType = "int", DefaultValue = 1, MinValue = 1, MaxValue = 10, IsRequired = true }
                }
            },
            new()
            {
                Id = Guid.NewGuid(),
                Type = "BlobAnalysis",
                DisplayName = "Blob分析",
                Category = "特征提取",
                Icon = "🔵",
                Description = "连通区域分析",
                Inputs = new List<PortDefinitionDto>
                {
                    new() { Name = "image", DisplayName = "输入图像", DataType = PortDataType.Image, IsRequired = true }
                },
                Outputs = new List<PortDefinitionDto>
                {
                    new() { Name = "image", DisplayName = "标记图像", DataType = PortDataType.Image, IsRequired = true },
                    new() { Name = "blobs", DisplayName = "Blob数据", DataType = PortDataType.Contour, IsRequired = true }
                },
                Parameters = new List<ParameterDefinitionDto>
                {
                    new() { Name = "minArea", DisplayName = "最小面积", DataType = "int", DefaultValue = 100, MinValue = 0, MaxValue = 10000, IsRequired = true },
                    new() { Name = "maxArea", DisplayName = "最大面积", DataType = "int", DefaultValue = 100000, MinValue = 100, MaxValue = 1000000, IsRequired = true }
                }
            },
            new()
            {
                Id = Guid.NewGuid(),
                Type = "TemplateMatching",
                DisplayName = "模板匹配",
                Category = "检测",
                Icon = "🎯",
                Description = "图像模板匹配定位",
                Inputs = new List<PortDefinitionDto>
                {
                    new() { Name = "image", DisplayName = "输入图像", DataType = PortDataType.Image, IsRequired = true },
                    new() { Name = "template", DisplayName = "模板图像", DataType = PortDataType.Image, IsRequired = true }
                },
                Outputs = new List<PortDefinitionDto>
                {
                    new() { Name = "image", DisplayName = "结果图像", DataType = PortDataType.Image, IsRequired = true },
                    new() { Name = "position", DisplayName = "位置", DataType = PortDataType.Point, IsRequired = true }
                },
                Parameters = new List<ParameterDefinitionDto>
                {
                    new() { Name = "threshold", DisplayName = "匹配阈值", DataType = "double", DefaultValue = 0.8, MinValue = 0.0, MaxValue = 1.0, IsRequired = true },
                    new() { Name = "method", DisplayName = "匹配方法", DataType = "enum", DefaultValue = "ncc", IsRequired = true }
                }
            },
            new()
            {
                Id = Guid.NewGuid(),
                Type = "Measurement",
                DisplayName = "测量",
                Category = "检测",
                Icon = "📏",
                Description = "几何尺寸测量",
                Inputs = new List<PortDefinitionDto>
                {
                    new() { Name = "image", DisplayName = "输入图像", DataType = PortDataType.Image, IsRequired = true }
                },
                Outputs = new List<PortDefinitionDto>
                {
                    new() { Name = "image", DisplayName = "结果图像", DataType = PortDataType.Image, IsRequired = true },
                    new() { Name = "distance", DisplayName = "距离", DataType = PortDataType.Float, IsRequired = true }
                },
                Parameters = new List<ParameterDefinitionDto>
                {
                    new() { Name = "x1", DisplayName = "起点X", DataType = "int", DefaultValue = 0, IsRequired = true },
                    new() { Name = "y1", DisplayName = "起点Y", DataType = "int", DefaultValue = 0, IsRequired = true },
                    new() { Name = "x2", DisplayName = "终点X", DataType = "int", DefaultValue = 100, IsRequired = true },
                    new() { Name = "y2", DisplayName = "终点Y", DataType = "int", DefaultValue = 100, IsRequired = true },
                    new() { Name = "measureType", DisplayName = "测量类型", DataType = "enum", DefaultValue = "PointToPoint", IsRequired = true }
                }
            },
            new()
            {
                Id = Guid.NewGuid(),
                Type = "ContourDetection",
                DisplayName = "轮廓检测",
                Category = "特征提取",
                Icon = "💠",
                Description = "查找并提取图像中的轮廓",
                Inputs = new List<PortDefinitionDto>
                {
                    new() { Name = "image", DisplayName = "输入图像", DataType = PortDataType.Image, IsRequired = true }
                },
                Outputs = new List<PortDefinitionDto>
                {
                    new() { Name = "image", DisplayName = "结果图像", DataType = PortDataType.Image, IsRequired = true }
                },
                Parameters = new List<ParameterDefinitionDto>
                {
                    new() { Name = "minArea", DisplayName = "最小面积", DataType = "int", DefaultValue = 100, MinValue = 0, IsRequired = true },
                    new() { Name = "maxArea", DisplayName = "最大面积", DataType = "int", DefaultValue = 100000, MinValue = 0, IsRequired = true }
                }
            },
            new()
            {
                Id = Guid.NewGuid(),
                Type = "DeepLearning",
                DisplayName = "深度学习",
                Category = "AI检测",
                Icon = "🧠",
                Description = "AI缺陷检测",
                Inputs = new List<PortDefinitionDto>
                {
                    new() { Name = "image", DisplayName = "输入图像", DataType = PortDataType.Image, IsRequired = true }
                },
                Outputs = new List<PortDefinitionDto>
                {
                    new() { Name = "image", DisplayName = "结果图像", DataType = PortDataType.Image, IsRequired = true },
                    new() { Name = "defects", DisplayName = "缺陷列表", DataType = PortDataType.Contour, IsRequired = true }
                },
                Parameters = new List<ParameterDefinitionDto>
                {
                    new() { Name = "model", DisplayName = "模型路径", DataType = "string", DefaultValue = "", IsRequired = true },
                    new() { Name = "confidence", DisplayName = "置信度阈值", DataType = "double", DefaultValue = 0.5, MinValue = 0.0, MaxValue = 1.0, IsRequired = true }
                }
            },
            new()
            {
                Id = Guid.NewGuid(),
                Type = "ResultOutput",
                DisplayName = "结果输出",
                Category = "输出",
                Icon = "📤",
                Description = "输出检测结果",
                Inputs = new List<PortDefinitionDto>
                {
                    new() { Name = "image", DisplayName = "输入图像", DataType = PortDataType.Image, IsRequired = false },
                    new() { Name = "data", DisplayName = "输入数据", DataType = PortDataType.Any, IsRequired = false }
                },
                Outputs = new List<PortDefinitionDto>(),
                Parameters = new List<ParameterDefinitionDto>
                {
                    new() { Name = "format", DisplayName = "输出格式", DataType = "enum", DefaultValue = "json", IsRequired = true },
                    new() { Name = "saveImage", DisplayName = "保存图像", DataType = "bool", DefaultValue = true, IsRequired = true }
                }
            }
        };

        foreach (var meta in metadata)
        {
            if (Enum.TryParse<OperatorType>(meta.Type, out var type))
            {
                OperatorMetadataCache[type] = meta;
            }
        }
    }

    public Task<IEnumerable<OperatorMetadataDto>> GetLibraryAsync()
    {
        return Task.FromResult(OperatorMetadataCache.Values.AsEnumerable());
    }

    public Task<OperatorDto?> GetByIdAsync(Guid id)
    {
        // 从元数据缓存中查找
        var meta = OperatorMetadataCache.Values.FirstOrDefault(m => m.Id == id);
        if (meta == null)
            return Task.FromResult<OperatorDto?>(null);

        var dto = MapToDto(meta);
        return Task.FromResult<OperatorDto?>(dto);
    }

    public Task<OperatorDto?> GetByTypeAsync(OperatorType type)
    {
        if (!OperatorMetadataCache.TryGetValue(type, out var meta))
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
                    catch (Exception)
                    {
                        // 记录日志或忽略无效参数
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
                    catch (InvalidOperationException)
                    {
                        // 参数不存在，跳过或记录日志
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

        var meta = OperatorMetadataCache.Values.FirstOrDefault(m => m.Id == operatorId);
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
        var types = OperatorMetadataCache.Values.Select(m => new OperatorTypeInfoDto
        {
            Type = m.Type,
            DisplayName = m.DisplayName,
            Category = m.Category,
            Icon = m.Icon
        });

        return Task.FromResult(types);
    }

    public Task<OperatorMetadataDto?> GetMetadataAsync(OperatorType type)
    {
        if (!OperatorMetadataCache.TryGetValue(type, out var meta))
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
