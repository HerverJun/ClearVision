// OperatorFlowDto.cs
// 更新流程请求
// 作者：蘅芜君

namespace ClearVision.Product.Application.DTOs;

using ClearVision.Product.Core.Decisions;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.ValueObjects;

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

    public DecisionConfiguration? DecisionConfiguration { get; set; }

    /// <summary>
    /// 转换为实体对象
    /// </summary>
    public OperatorFlow ToEntity()
    {
        // 创建空流程
        var flow = new OperatorFlow(Name)
        {
            DecisionConfiguration = DecisionConfiguration
        };

        // 添加算子
        foreach (var op in Operators)
        {
            var canonicalType = OperatorTypeAliasResolver.Resolve(op.Type);
            // 使用 5 参数构造函数创建算子 (保留原始ID)
            var operatorEntity = new Operator(op.Id, op.Name, canonicalType, op.X, op.Y)
            {
                Metadata = op.Metadata == null
                    ? null
                    : new Dictionary<string, object?>(op.Metadata, StringComparer.OrdinalIgnoreCase)
            };
            if (!op.IsEnabled)
            {
                operatorEntity.Disable();
            }

            // 加载输入端口
            foreach (var port in op.InputPorts)
            {
                operatorEntity.LoadInputPort(port.Id, port.Name, port.DataType, port.IsRequired);
            }

            // 加载输出端口
            foreach (var port in op.OutputPorts)
            {
                operatorEntity.LoadOutputPort(port.Id, port.Name, port.DataType);
            }

            // 添加参数
            foreach (var p in op.Parameters)
            {
                var param = new Parameter(
                    p.Id,
                    p.Name,
                    p.DisplayName,
                    p.Description ?? "",
                    p.DataType,
                    p.DefaultValue,
                    p.MinValue,
                    p.MaxValue,
                    p.IsRequired,
                    p.Options
                );
                // 【关键】设置前端编辑过的值
                if (p.Value != null)
                {
                    param.SetValue(p.Value);
                }
                operatorEntity.AddParameter(param);
            }

            flow.AddOperator(operatorEntity);
        }

        // 添加连接
        foreach (var c in Connections)
        {
            var connection = new OperatorConnection(
                c.SourceOperatorId,
                c.SourcePortId,
                c.TargetOperatorId,
                c.TargetPortId
            );
            flow.AddConnection(connection);
        }

        return flow;
    }
}

/// <summary>
/// 更新流程请求
/// </summary>
public class UpdateFlowRequest
{
    public string? Name { get; set; }

    public long? ExpectedPersistenceRevision { get; set; }

    public DecisionConfiguration? DecisionConfiguration { get; set; }

    public List<OperatorDto> Operators { get; set; } = new();
    public List<OperatorConnectionDto> Connections { get; set; } = new();

}
