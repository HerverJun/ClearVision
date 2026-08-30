// ProjectDto.cs
// 算子流程（可选）
// 作者：蘅芜君

namespace ClearVision.Product.Application.DTOs;

using ClearVision.Product.Core.ProjectVariables;
using System.Text.Json.Serialization;

/// <summary>
/// 工程数据传输对象
/// </summary>
public class ProjectDto
{
    /// <summary>
    /// 工程ID
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// 工程名称
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 工程描述
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// 工程版本
    /// </summary>
    public string Version { get; set; } = "1.0.0";

    public long PersistenceRevision { get; set; }

    /// <summary>
    /// 算子流程
    /// </summary>
    public OperatorFlowDto? Flow { get; set; }

    /// <summary>
    /// 全局配置参数
    /// </summary>
    public Dictionary<string, string> GlobalSettings { get; set; } = new();

    public ProjectGlobalVariableSchema GlobalVariables { get; set; } = new();

    public ProjectAssetsDto Assets { get; set; } = new();

    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// 最后修改时间
    /// </summary>
    public DateTime? ModifiedAt { get; set; }

    /// <summary>
    /// 最后打开时间
    /// </summary>
    public DateTime? LastOpenedAt { get; set; }
}

/// <summary>
/// 创建工程请求
/// </summary>
public class CreateProjectRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>
    /// 算子流程（可选）
    /// </summary>
    public OperatorFlowDto? Flow { get; set; }

    public ProjectGlobalVariableSchema? GlobalVariables { get; set; }
}

/// <summary>
/// 更新工程请求
/// </summary>
public class UpdateProjectRequest
{
    private string? _name;
    private string? _description;
    private OperatorFlowDto? _flow;
    private ProjectGlobalVariableSchema? _globalVariables;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Name
    {
        get => _name;
        set
        {
            _name = value;
            HasName = true;
        }
    }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description
    {
        get => _description;
        set
        {
            _description = value;
            HasDescription = true;
        }
    }

    public long? ExpectedPersistenceRevision { get; set; }

    /// <summary>
    /// 算子流程（可选）
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public OperatorFlowDto? Flow
    {
        get => _flow;
        set
        {
            _flow = value;
            HasFlow = true;
        }
    }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ProjectGlobalVariableSchema? GlobalVariables
    {
        get => _globalVariables;
        set
        {
            _globalVariables = value;
            HasGlobalVariables = true;
        }
    }

    [JsonIgnore]
    public bool HasName { get; private set; }

    [JsonIgnore]
    public bool HasDescription { get; private set; }

    [JsonIgnore]
    public bool HasFlow { get; private set; }

    [JsonIgnore]
    public bool HasGlobalVariables { get; private set; }
}

/// <summary>
/// Dedicated revisioned patch for the project global-variable schema.
/// </summary>
public sealed class UpdateProjectGlobalVariablesRequest
{
    public long? ExpectedPersistenceRevision { get; set; }

    public ProjectGlobalVariableSchema? Schema { get; set; }
}

public sealed class UpdateProjectGlobalVariablesResponse
{
    public Guid ProjectId { get; set; }

    public long PersistenceRevision { get; set; }

    public ProjectGlobalVariableSchema GlobalVariables { get; set; } = new();
}
