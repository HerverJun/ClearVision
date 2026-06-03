using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ClearVision.Product.Core.AI.Tools;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Services;

namespace ClearVision.Product.Infrastructure.AI.Tools;

public sealed class OperatorSchemaTool : IVisionAgentTool
{
    private readonly IOperatorFactory _operatorFactory;

    public OperatorSchemaTool(IOperatorFactory operatorFactory)
    {
        _operatorFactory = operatorFactory;
    }

    public string Name => "get_operator_schema";
    public string DisplayName => "获取算子参数规约";
    public string Description => "获取指定算子类型的输入/输出端口定义和参数列表（包括参数名、类型、默认值、必填项和选项）。";
    public string Category => "Operators";
    public VisionAgentToolPermission Permission => VisionAgentToolPermission.ReadOnly;

    public JsonElement ParametersSchema { get; } = JsonDocument.Parse(@"{
        ""type"": ""object"",
        ""properties"": {
            ""operatorType"": { ""type"": ""string"", ""description"": ""要查询的算子类型名，必须存在于算子目录中（例如 ImageAcquisition）"" }
        },
        ""required"": [""operatorType""]
    }").RootElement;

    public Task<VisionAgentToolResult> ExecuteAsync(
        VisionAgentToolContext context,
        JsonElement arguments,
        CancellationToken cancellationToken)
    {
        if (arguments.ValueKind != JsonValueKind.Object || 
            !arguments.TryGetProperty("operatorType", out var opTypeProp) || 
            opTypeProp.ValueKind != JsonValueKind.String)
        {
            return Task.FromResult(VisionAgentToolResult.CreateFailure("Missing or invalid 'operatorType' parameter."));
        }

        var operatorTypeName = opTypeProp.GetString()?.Trim();
        if (string.IsNullOrEmpty(operatorTypeName) || 
            !Enum.TryParse<OperatorType>(operatorTypeName, ignoreCase: true, out var opType))
        {
            return Task.FromResult(VisionAgentToolResult.CreateFailure($"Unknown operatorType: '{operatorTypeName}'"));
        }

        var metadata = _operatorFactory.GetMetadata(opType);
        if (metadata == null)
        {
            return Task.FromResult(VisionAgentToolResult.CreateFailure($"Metadata not found for operator type '{operatorTypeName}'."));
        }

        var schema = new
        {
            operatorType = metadata.Type.ToString(),
            displayName = metadata.DisplayName,
            category = metadata.Category,
            description = metadata.Description,
            inputs = metadata.InputPorts.Select(p => new
            {
                portName = p.Name,
                displayName = p.DisplayName,
                dataType = p.DataType.ToString(),
                required = p.IsRequired,
                description = p.Description ?? string.Empty
            }).ToList(),
            outputs = metadata.OutputPorts.Select(p => new
            {
                portName = p.Name,
                displayName = p.DisplayName,
                dataType = p.DataType.ToString(),
                description = p.Description ?? string.Empty
            }).ToList(),
            parameters = metadata.Parameters.Select(p => new
            {
                paramName = p.Name,
                displayName = p.DisplayName,
                type = p.DataType,
                defaultValue = p.DefaultValue?.ToString(),
                required = p.IsRequired,
                description = p.Description ?? string.Empty,
                minValue = p.MinValue?.ToString(),
                maxValue = p.MaxValue?.ToString(),
                options = p.Options?.Select(o => o.Value?.ToString()).ToList()
            }).ToList()
        };

        var summary = $"Retrieved schema for {operatorTypeName}";
        return Task.FromResult(VisionAgentToolResult.CreateSuccess(schema, summary));
    }
}
