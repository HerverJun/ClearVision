// ProjectService.cs
// 灏?OperatorConnection 鍊煎璞℃槧灏勪负 DTO
// 浣滆€咃細铇呰姕鍚?

using System.Text.Json;
using ClearVision.Product.Application.DTOs;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Exceptions;
using ClearVision.Product.Core.Interfaces;
using ClearVision.Product.Core.ProjectVariables;
using ClearVision.Product.Core.Services;
using ClearVision.Product.Core.ValueObjects;
using Microsoft.Extensions.Logging;

namespace ClearVision.Product.Application.Services;

/// <summary>
/// 宸ョ▼搴旂敤鏈嶅姟
/// </summary>
public class ProjectService
{
    private readonly IProjectRepository _projectRepository;
    private readonly IProjectFlowStorage _flowStorage;
    private readonly IOperatorFactory _operatorFactory;
    private readonly ILogger<ProjectService>? _logger;
    private readonly ProjectVariableSessionRegistry? _projectVariableSessions;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    public ProjectService(IProjectRepository projectRepository, IProjectFlowStorage flowStorage, IOperatorFactory operatorFactory)
        : this(projectRepository, flowStorage, operatorFactory, null)
    {
    }

    public ProjectService(
        IProjectRepository projectRepository,
        IProjectFlowStorage flowStorage,
        IOperatorFactory operatorFactory,
        ILogger<ProjectService>? logger)
        : this(projectRepository, flowStorage, operatorFactory, logger, null)
    {
    }

    public ProjectService(
        IProjectRepository projectRepository,
        IProjectFlowStorage flowStorage,
        IOperatorFactory operatorFactory,
        ILogger<ProjectService>? logger,
        ProjectVariableSessionRegistry? projectVariableSessions)
    {
        _projectRepository = projectRepository;
        _flowStorage = flowStorage;
        _operatorFactory = operatorFactory;
        _logger = logger;
        _projectVariableSessions = projectVariableSessions;
    }

    /// <summary>
    /// 鍒涘缓宸ョ▼
    /// </summary>
    public async Task<ProjectDto> CreateAsync(CreateProjectRequest request)
    {
        var project = new Project(request.Name, request.Description);
        var globalVariables = request.GlobalVariables ?? new ProjectGlobalVariableSchema();
        ProjectGlobalVariableSchemaValidator.ThrowIfInvalid(globalVariables, request.Flow?.ToEntity());
        project.UpdateGlobalVariables(globalVariables);
        await _projectRepository.AddAsync(project);

        // 濡傛灉鍒涘缓鏃跺甫鏈夋祦绋嬶紙閫氬父鏄┖鐨勶紝浣嗕负浜嗗畬鏁存€э級
        if (request.Flow != null)
        {
            var json = JsonSerializer.Serialize(request.Flow);
            await _flowStorage.SaveFlowJsonAsync(project.Id, json);
        }

        return MapToDto(project);
    }

    /// <summary>
    /// 鑾峰彇宸ョ▼
    /// </summary>
    public async Task<ProjectDto?> GetByIdAsync(Guid id)
    {
        var project = await _projectRepository.GetByIdAsync(id);
        if (project == null)
            return null;

        var dto = MapToDto(project);

        // 浠庢枃浠跺姞杞芥祦绋嬫暟鎹鐩?DB 鏁版嵁 (濡傛灉鏈?
        var flowJson = await _flowStorage.LoadFlowJsonAsync(id);
        if (!string.IsNullOrEmpty(flowJson))
        {
            try
            {
                var flowDto = JsonSerializer.Deserialize<OperatorFlowDto>(flowJson, _jsonOptions);
                if (flowDto != null)
                {
                    dto.Flow = flowDto;
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to deserialize flow JSON for project {ProjectId}; falling back to database flow.", id);
                // 蹇界暐鍙嶅簭鍒楀寲閿欒锛屽洖閫€鍒?DB 鏁版嵁
            }
        }

        // 銆愮粺涓€淇銆戞棤璁烘暟鎹潵鑷?DB 杩樻槸 JSON锛岄兘灏濊瘯鍥炲～缂哄け鐨?Options
        if (dto.Flow != null)
        {
            var migrated = MigrateFlowDto(dto.Flow);
            EnrichFlowDtoWithMetadata(dto.Flow);

            if (migrated)
            {
                var json = JsonSerializer.Serialize(dto.Flow, _jsonOptions);
                await _flowStorage.SaveFlowJsonAsync(id, json);
            }
        }

        return dto;
    }

    private void EnrichFlowDtoWithMetadata(OperatorFlowDto flowDto)
    {
        foreach (var opDto in flowDto.Operators)
        {
            var metadata = _operatorFactory.GetMetadata(opDto.Type);
            if (metadata == null)
                continue;

            foreach (var paramDto in opDto.Parameters)
            {
                // 濡傛灉 Options 涓虹┖涓?DataType 鏄?enum锛屽皾璇曚粠鍏冩暟鎹仮澶?
                if ((paramDto.Options == null || paramDto.Options.Count == 0) &&
                    (paramDto.DataType.Equals("enum", StringComparison.OrdinalIgnoreCase) ||
                     paramDto.DataType.Equals("select", StringComparison.OrdinalIgnoreCase)))
                {
                    var paramDef = metadata.Parameters.FirstOrDefault(p => p.Name == paramDto.Name);
                    if (paramDef != null && paramDef.Options != null)
                    {
                        paramDto.Options = paramDef.Options;
                    }
                }
            }
        }
    }

    /// <summary>
    /// 鑾峰彇鎵€鏈夊伐绋?
    /// </summary>
    public async Task<IEnumerable<ProjectDto>> GetAllAsync()
    {
        var projects = await _projectRepository.GetAllAsync();
        // GetAll 閫氬父涓嶈繑鍥炶缁嗙殑 Flow 鍐呭浠ヤ紭鍖栨€ц兘锛屾垨鑰呮垜浠彲浠ラ€夋嫨鍔犺浇
        // 杩欓噷鏆傛椂淇濇寔鍘熸牱锛屼粎杩斿洖杞婚噺绾у垪琛?
        return projects.Select(MapToDto);
    }

    /// <summary>
    /// 鏇存柊宸ョ▼
    /// </summary>
    public async Task<ProjectDto> UpdateAsync(Guid id, UpdateProjectRequest request)
    {
        var project = await _projectRepository.GetByIdAsync(id);
        if (project == null)
            throw new ProjectNotFoundException(id);

        var previousName = project.Name;
        var previousDescription = project.Description;
        var previousGlobalVariables = CloneSchema(project.GlobalVariables);
        var previousFlowJson = await _flowStorage.LoadFlowJsonAsync(id);
        var nextFlow = request.Flow ?? await LoadStoredFlowDtoAsync(id);
        var nextSchema = request.GlobalVariables ?? project.GlobalVariables;
        var flowChanged = request.Flow != null;
        if (nextFlow != null)
        {
            flowChanged |= MigrateFlowDto(nextFlow);
            EnrichFlowDtoWithMetadata(nextFlow);
            flowChanged |= NormalizeProjectVariableOperatorNames(nextFlow, nextSchema);
        }

        ProjectGlobalVariableSchemaValidator.ThrowIfInvalid(nextSchema, nextFlow?.ToEntity());
        var nextFlowJson = flowChanged && nextFlow != null ? JsonSerializer.Serialize(nextFlow, _jsonOptions) : null;
        var repositoryUpdated = false;

        // 濡傛灉鏈夋祦绋嬫暟鎹紝鏇存柊鍒版枃浠?
        try
        {
            if (nextFlowJson != null)
            {
                await _flowStorage.SaveFlowJsonAsync(id, nextFlowJson);
            }

            project.UpdateInfo(request.Name, request.Description);
            if (request.GlobalVariables != null)
            {
                project.UpdateGlobalVariables(nextSchema);
            }

            await _projectRepository.UpdateAsync(project);
            repositoryUpdated = true;
            if (request.GlobalVariables != null)
            {
                if (_projectVariableSessions != null &&
                    !_projectVariableSessions.TryPublishSchemaAndPersist(id, project.GlobalVariables, out _, out var publishError))
                {
                    throw new InvalidOperationException(publishError);
                }
            }
        }
        catch
        {
            project.UpdateInfo(previousName, previousDescription);
            project.UpdateGlobalVariables(previousGlobalVariables);
            if (repositoryUpdated)
            {
                await _projectRepository.UpdateAsync(project);
            }

            if (nextFlowJson != null && previousFlowJson != null)
            {
                await _flowStorage.SaveFlowJsonAsync(id, previousFlowJson);
            }
            else if (nextFlowJson != null)
            {
                await _flowStorage.DeleteFlowJsonAsync(id);
            }

            throw;
        }

        var dto = MapToDto(project);
        dto.Flow = nextFlow;
        return dto;
    }

    /// <summary>
    /// 鏇存柊宸ョ▼娴佺▼
    /// </summary>
    public async Task UpdateFlowAsync(Guid id, UpdateFlowRequest request)
    {
        // 1. 楠岃瘉宸ョ▼瀛樺湪
        var project = await _projectRepository.GetByIdAsync(id);
        if (project == null)
            throw new ProjectNotFoundException(id);

        // 2. 鏋勯€犳祦绋婦TO
        var flowDto = new OperatorFlowDto
        {
            Name = "MainFlow", // 淇濇寔榛樿鍚嶇О鎴栦粠鏌愬鑾峰彇
            Operators = request.Operators,
            Connections = request.Connections
        };
        await UpdateAsync(id, new UpdateProjectRequest
        {
            Name = project.Name,
            Description = project.Description,
            Flow = flowDto
        });

        // 4. 鏇存柊宸ョ▼淇敼鏃堕棿 (鍙€夛紝浣嗘帹鑽?
        // project.LastModified = DateTime.UtcNow; // 濡傛灉 Project 鏈夎繖涓瓧娈?
        // await _projectRepository.UpdateAsync(project);
    }

    /// <summary>
    /// 灏哋peratorFlowDto杞崲涓篊ore瀹炰綋
    /// </summary>
    private OperatorFlow MapDtoToFlow(OperatorFlowDto dto, Guid? flowId = null)
    {
        var flow = new OperatorFlow(dto.Name);

        // 銆愬叧閿慨澶嶃€戝鏋滄寚瀹氫簡 flowId (閫氬父鏄?Project.Id)锛屽己鍒惰缃畠
        // EF Core Table Splitting 瑕佹眰 Project.Id == Flow.Id
        if (flowId.HasValue)
        {
            // Flow缁ф壙鑷狤ntity锛孖d瀹氫箟鍦‥ntity涓?
            typeof(ClearVision.Product.Core.Entities.Base.Entity)
                .GetProperty("Id")?
                .SetValue(flow, flowId.Value);
        }

        // 娣诲姞绠楀瓙
        foreach (var opDto in dto.Operators)
        {
            var canonicalType = OperatorTypeAliasResolver.Resolve(opDto.Type);
            var op = new Operator(
                opDto.Name,
                canonicalType,
                opDto.X,
                opDto.Y
            );

            // 璁剧疆ID锛堝鏋滄彁渚涗簡锛?
            if (opDto.Id != Guid.Empty)
            {
                // 浣跨敤鍙嶅皠璁剧疆ID锛屽洜涓烘瀯閫犲嚱鏁颁細鐢熸垚鏂扮殑ID
                typeof(Operator).GetProperty("Id")?.SetValue(op, opDto.Id);
            }

            // 鎭㈠杈撳叆绔彛锛堜繚鐣橧D浠ョ淮鎸佽繛绾匡級
            foreach (var portDto in opDto.InputPorts)
            {
                op.LoadInputPort(portDto.Id, portDto.Name, portDto.DataType, portDto.IsRequired);
            }

            // 鎭㈠杈撳嚭绔彛锛堜繚鐣橧D浠ョ淮鎸佽繛绾匡級
            foreach (var portDto in opDto.OutputPorts)
            {
                op.LoadOutputPort(portDto.Id, portDto.Name, portDto.DataType);
            }

            // 娣诲姞鍙傛暟
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
                    paramDto.IsRequired,
                    paramDto.Options
                );

                if (paramDto.Value != null)
                {
                    param.SetValue(paramDto.Value);
                }

                op.AddParameter(param);
            }

            flow.AddOperator(op);
        }

        // 娣诲姞杩炴帴
        foreach (var connDto in dto.Connections)
        {
            // 銆愪慨澶嶃€戜慨姝ｅ弬鏁伴『搴忥細sourceOperatorId, sourcePortId, targetOperatorId, targetPortId
            var connection = new OperatorConnection(
                connDto.SourceOperatorId,
                connDto.SourcePortId,        // 鉁?淇锛氱2涓弬鏁板簲璇ユ槸 SourcePortId
                connDto.TargetOperatorId,    // 鉁?淇锛氱3涓弬鏁板簲璇ユ槸 TargetOperatorId
                connDto.TargetPortId
            );

            // 璁剧疆杩炴帴ID
            if (connDto.Id != Guid.Empty)
            {
                typeof(OperatorConnection).GetProperty("Id")?.SetValue(connection, connDto.Id);
            }

            flow.AddConnection(connection);
        }

        return flow;
    }

    /// <summary>
    /// 鍒犻櫎宸ョ▼
    /// </summary>
    public async Task DeleteAsync(Guid id)
    {
        var project = await _projectRepository.GetByIdAsync(id);
        if (project == null)
            throw new ProjectNotFoundException(id);

        project.MarkAsDeleted();
        await _projectRepository.UpdateAsync(project);
        _projectVariableSessions?.Delete(id);
    }

    /// <summary>
    /// 鎼滅储宸ョ▼
    /// </summary>
    public async Task<IEnumerable<ProjectDto>> SearchAsync(string keyword)
    {
        var projects = await _projectRepository.SearchAsync(keyword);
        return projects.Select(MapToDto);
    }

    /// <summary>
    /// 鑾峰彇鏈€杩戞墦寮€鐨勫伐绋?
    /// </summary>
    public async Task<IEnumerable<ProjectDto>> GetRecentlyOpenedAsync(int count = 10)
    {
        var projects = await _projectRepository.GetRecentlyOpenedAsync(count);
        return projects.Select(MapToDto);
    }

    public async Task<ProjectGlobalVariableSchema> UpdateGlobalVariablesAsync(Guid id, ProjectGlobalVariableSchema schema)
    {
        var project = await _projectRepository.GetByIdAsync(id);
        if (project == null)
            throw new ProjectNotFoundException(id);

        var updated = await UpdateAsync(id, new UpdateProjectRequest
        {
            Name = project.Name,
            Description = project.Description,
            Flow = await LoadStoredFlowDtoAsync(id),
            GlobalVariables = schema
        });
        return updated.GlobalVariables;
    }

    private static ProjectGlobalVariableSchema CloneSchema(ProjectGlobalVariableSchema schema)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(schema, _jsonOptions);
        return JsonSerializer.Deserialize<ProjectGlobalVariableSchema>(bytes, _jsonOptions) ?? new ProjectGlobalVariableSchema();
    }

    private async Task<OperatorFlowDto?> LoadStoredFlowDtoAsync(Guid projectId)
    {
        var flowJson = await _flowStorage.LoadFlowJsonAsync(projectId);
        if (string.IsNullOrWhiteSpace(flowJson))
        {
            return null;
        }

        return JsonSerializer.Deserialize<OperatorFlowDto>(flowJson, _jsonOptions);
    }

    private async Task<OperatorFlow?> LoadStoredFlowEntityAsync(Guid projectId)
    {
        var flowJson = await _flowStorage.LoadFlowJsonAsync(projectId);
        if (string.IsNullOrWhiteSpace(flowJson))
        {
            return null;
        }

        var flowDto = JsonSerializer.Deserialize<OperatorFlowDto>(flowJson, _jsonOptions);
        return flowDto?.ToEntity();
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
            GlobalSettings = project.GlobalSettings,
            GlobalVariables = project.GlobalVariables,
            // 淇锛氭坊鍔?Flow 瀛楁鏄犲皠
            Flow = project.Flow != null ? MapFlowToDto(project.Flow) : null
        };
    }

    /// <summary>
    /// 灏?OperatorFlow 瀹炰綋鏄犲皠涓?DTO
    /// </summary>
    private OperatorFlowDto MapFlowToDto(OperatorFlow flow)
    {
        return new OperatorFlowDto
        {
            Id = flow.Id,
            Name = flow.Name,
            Operators = flow.Operators.Select(MapOperatorToDto).ToList(),
            Connections = flow.Connections.Select(MapConnectionToDto).ToList()
        };
    }

    /// <summary>
    /// 灏?Operator 瀹炰綋鏄犲皠涓?DTO
    /// </summary>
    private OperatorDto MapOperatorToDto(Operator op)
    {
        return new OperatorDto
        {
            Id = op.Id,
            Name = op.Name,
            Type = OperatorTypeAliasResolver.Resolve(op.Type),
            X = op.Position.X,
            Y = op.Position.Y,
            InputPorts = op.InputPorts.Select(MapPortToDto).ToList(),
            OutputPorts = op.OutputPorts.Select(MapPortToDto).ToList(),
            Parameters = op.Parameters.Select(MapParameterToDto).ToList(),
            IsEnabled = op.IsEnabled,
            ExecutionStatus = op.ExecutionStatus,
            ExecutionTimeMs = op.ExecutionTimeMs,
            ErrorMessage = op.ErrorMessage
        };
    }

    /// <summary>
    /// 灏?Port 鍊煎璞℃槧灏勪负 DTO
    /// </summary>
    private PortDto MapPortToDto(Port port)
    {
        return new PortDto
        {
            Id = port.Id,
            Name = port.Name,
            Direction = port.Direction,
            DataType = port.DataType,
            IsRequired = port.IsRequired
        };
    }

    /// <summary>
    /// 灏?Parameter 鍊煎璞℃槧灏勪负 DTO
    /// </summary>
    private ParameterDto MapParameterToDto(Parameter param)
    {
        return new ParameterDto
        {
            Id = param.Id,
            Name = param.Name,
            DisplayName = param.DisplayName,
            Description = param.Description,
            DataType = param.DataType,
            Value = param.GetValue(),
            DefaultValue = param.DefaultValue,
            MinValue = param.MinValue,
            MaxValue = param.MaxValue,
            IsRequired = param.IsRequired,
            Options = param.Options
        };
    }

    /// <summary>
    /// 灏?OperatorConnection 鍊煎璞℃槧灏勪负 DTO
    /// </summary>
    private OperatorConnectionDto MapConnectionToDto(OperatorConnection conn)
    {
        return new OperatorConnectionDto
        {
            Id = conn.Id,
            SourceOperatorId = conn.SourceOperatorId,
            SourcePortId = conn.SourcePortId,
            TargetOperatorId = conn.TargetOperatorId,
            TargetPortId = conn.TargetPortId
        };
    }

    private static readonly HashSet<string> LegacyPortNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "input",
        "output",
        "in",
        "out"
    };

    private bool MigrateFlowDto(OperatorFlowDto flowDto)
    {
        var changed = false;
        foreach (var opDto in flowDto.Operators)
        {
            var canonicalType = OperatorTypeAliasResolver.Resolve(opDto.Type);
            if (canonicalType != opDto.Type)
            {
                opDto.Type = canonicalType;
                changed = true;
            }

            var metadata = _operatorFactory.GetMetadata(opDto.Type);
            if (metadata == null)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(opDto.Name))
            {
                opDto.Name = metadata.DisplayName;
                changed = true;
            }

            changed |= NormalizePorts(opDto.InputPorts, metadata.InputPorts, PortDirection.Input);
            changed |= NormalizePorts(opDto.OutputPorts, metadata.OutputPorts, PortDirection.Output);
            changed |= NormalizeParameters(opDto.Parameters, metadata.Parameters);
        }

        return changed;
    }

    private static bool NormalizeProjectVariableOperatorNames(
        OperatorFlowDto flowDto,
        ProjectGlobalVariableSchema schema)
    {
        var variablesById = schema.Variables
            .Where(variable => variable.Id != Guid.Empty)
            .GroupBy(variable => variable.Id)
            .ToDictionary(group => group.Key, group => group.First());
        var variablesByName = schema.Variables
            .Where(variable => !string.IsNullOrWhiteSpace(variable.Name))
            .GroupBy(variable => variable.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        if (variablesById.Count == 0)
        {
            return false;
        }

        var changed = false;
        foreach (var opDto in flowDto.Operators)
        {
            if (opDto.Type is not (OperatorType.VariableRead or OperatorType.VariableWrite or OperatorType.VariableIncrement))
            {
                continue;
            }

            if (!string.Equals(GetParameterString(opDto, "Scope"), "Project", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var variableIdParameter = GetParameter(opDto, "VariableId");
            var variableNameParameter = GetParameter(opDto, "VariableName");
            ProjectGlobalVariableDefinition? definition = null;
            var variableIdText = variableIdParameter?.Value?.ToString();
            var hasParsedVariableId = Guid.TryParse(variableIdText, out var variableId);
            if (hasParsedVariableId)
            {
                variablesById.TryGetValue(variableId, out definition);
            }

            var variableNameText = variableNameParameter?.Value?.ToString();
            ProjectGlobalVariableDefinition? definitionByName = null;
            if (!string.IsNullOrWhiteSpace(variableNameText))
            {
                variablesByName.TryGetValue(variableNameText, out definitionByName);
            }

            if (definition != null &&
                definitionByName != null &&
                definition.Id != definitionByName.Id)
            {
                continue;
            }

            if (definition == null && !hasParsedVariableId)
            {
                if (definitionByName == null)
                {
                    continue;
                }

                definition = definitionByName;
            }

            if (definition == null)
            {
                continue;
            }

            variableIdParameter ??= AddParameter(opDto, "VariableId");
            var currentId = variableIdParameter.Value?.ToString();
            var nextId = definition.Id.ToString("D");
            if (!string.Equals(currentId, nextId, StringComparison.OrdinalIgnoreCase))
            {
                variableIdParameter.Value = nextId;
                changed = true;
            }

            variableNameParameter ??= AddParameter(opDto, "VariableName");
            var currentName = variableNameParameter.Value?.ToString();
            if (string.Equals(currentName, definition.Name, StringComparison.Ordinal))
            {
                continue;
            }

            variableNameParameter.Value = definition.Name;
            changed = true;
        }

        return changed;
    }

    private static ParameterDto AddParameter(OperatorDto opDto, string name)
    {
        var parameter = new ParameterDto
        {
            Id = Guid.NewGuid(),
            Name = name,
            DisplayName = name,
            DataType = "string"
        };
        opDto.Parameters.Add(parameter);
        return parameter;
    }

    private static ParameterDto? GetParameter(OperatorDto opDto, string name)
    {
        return opDto.Parameters.FirstOrDefault(parameter =>
            string.Equals(parameter.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    private static string? GetParameterString(OperatorDto opDto, string name)
    {
        return GetParameter(opDto, name)?.Value?.ToString();
    }

    private static bool NormalizePorts(List<PortDto> ports, List<PortDefinition> metadataPorts, PortDirection direction)
    {
        if (metadataPorts.Count == 0)
        {
            return false;
        }

        var changed = false;
        var shouldRebuild = ports.Count == 0 ||
            (ports.Count == metadataPorts.Count &&
             ports.All(port => LegacyPortNames.Contains(port.Name) || port.Id == Guid.Empty));

        if (shouldRebuild)
        {
            ports.Clear();
            foreach (var definition in metadataPorts)
            {
                ports.Add(new PortDto
                {
                    Id = Guid.NewGuid(),
                    Name = definition.Name,
                    Direction = direction,
                    DataType = definition.DataType,
                    IsRequired = direction == PortDirection.Input && definition.IsRequired
                });
            }

            return true;
        }

        var count = Math.Min(ports.Count, metadataPorts.Count);
        for (var index = 0; index < count; index += 1)
        {
            var port = ports[index];
            var definition = metadataPorts[index];

            if (port.Id == Guid.Empty)
            {
                port.Id = Guid.NewGuid();
                changed = true;
            }

            if (LegacyPortNames.Contains(port.Name))
            {
                port.Name = definition.Name;
                changed = true;
            }

            if (port.DataType != definition.DataType)
            {
                port.DataType = definition.DataType;
                changed = true;
            }

            if (port.Direction != direction)
            {
                port.Direction = direction;
                changed = true;
            }

            if (direction == PortDirection.Input && port.IsRequired != definition.IsRequired)
            {
                port.IsRequired = definition.IsRequired;
                changed = true;
            }
        }

        return changed;
    }

    private static bool NormalizeParameters(List<ParameterDto> parameters, List<ParameterDefinition> metadataParameters)
    {
        var changed = false;

        foreach (var definition in metadataParameters)
        {
            var parameter = parameters.FirstOrDefault(item =>
                string.Equals(item.Name, definition.Name, StringComparison.OrdinalIgnoreCase));

            if (parameter == null)
            {
                parameters.Add(new ParameterDto
                {
                    Id = Guid.NewGuid(),
                    Name = definition.Name,
                    DisplayName = definition.DisplayName,
                    Description = definition.Description,
                    DataType = definition.DataType,
                    Value = definition.DefaultValue,
                    DefaultValue = definition.DefaultValue,
                    MinValue = definition.MinValue,
                    MaxValue = definition.MaxValue,
                    IsRequired = definition.IsRequired,
                    Options = definition.Options
                });
                changed = true;
                continue;
            }

            if (parameter.Id == Guid.Empty)
            {
                parameter.Id = Guid.NewGuid();
                changed = true;
            }

            if (!string.Equals(parameter.Name, definition.Name, StringComparison.Ordinal))
            {
                parameter.Name = definition.Name;
                changed = true;
            }

            if (!string.Equals(parameter.DisplayName, definition.DisplayName, StringComparison.Ordinal))
            {
                parameter.DisplayName = definition.DisplayName;
                changed = true;
            }

            if (parameter.Description != definition.Description)
            {
                parameter.Description = definition.Description;
                changed = true;
            }

            if (!string.Equals(parameter.DataType, definition.DataType, StringComparison.OrdinalIgnoreCase))
            {
                parameter.DataType = definition.DataType;
                changed = true;
            }

            if (!Equals(parameter.DefaultValue, definition.DefaultValue))
            {
                parameter.DefaultValue = definition.DefaultValue;
                changed = true;
            }

            if (!Equals(parameter.MinValue, definition.MinValue))
            {
                parameter.MinValue = definition.MinValue;
                changed = true;
            }

            if (!Equals(parameter.MaxValue, definition.MaxValue))
            {
                parameter.MaxValue = definition.MaxValue;
                changed = true;
            }

            if (parameter.IsRequired != definition.IsRequired)
            {
                parameter.IsRequired = definition.IsRequired;
                changed = true;
            }

            if ((parameter.Options == null || parameter.Options.Count == 0) && definition.Options != null)
            {
                parameter.Options = definition.Options;
                changed = true;
            }
        }

        return changed;
    }
}
