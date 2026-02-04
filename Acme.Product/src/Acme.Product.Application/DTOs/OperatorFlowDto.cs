namespace Acme.Product.Application.DTOs;

/// <summary>
/// 算子流程数据传输对象
/// </summary>
public class OperatorFlowDto
{
    /// <summary>
    /// 流程ID
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// 流程名称
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 算子列表
    /// </summary>
    public List<OperatorDto> Operators { get; set; } = new();

    /// <summary>
    /// 连接关系列表
    /// </summary>
    public List<OperatorConnectionDto> Connections { get; set; } = new();
}

/// <summary>
/// 更新流程请求
/// </summary>
public class UpdateFlowRequest
{
    public List<OperatorDto> Operators { get; set; } = new();
    public List<OperatorConnectionDto> Connections { get; set; } = new();
}
