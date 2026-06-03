using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ClearVision.Product.Core.AI.Tools;
using ClearVision.Product.Core.DTOs;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Services;
using ClearVision.Product.Infrastructure.AI.DryRun;
using ClearVision.Product.Application.DTOs;

namespace ClearVision.Product.Infrastructure.AI.Tools;

public sealed class DryRunFlowTool : IVisionAgentTool
{
    private readonly DryRunService _dryRunService;
    private readonly IOperatorFactory _operatorFactory;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new FlexibleStringDictionaryJsonConverter() }
    };

    public DryRunFlowTool(DryRunService dryRunService, IOperatorFactory operatorFactory)
    {
        _dryRunService = dryRunService;
        _operatorFactory = operatorFactory;
    }

    public string Name => "dryrun_flow";
    public string DisplayName => "DryRun预演仿真";
    public string Description => "在虚拟沙箱中对生成的工作流进行结构级跑分仿真，预演数据链路并统计分支覆盖率。不需要真实硬件，只验证数据流逻辑。";
    public string Category => "Validation";
    public VisionAgentToolPermission Permission => VisionAgentToolPermission.Simulation;

    public JsonElement ParametersSchema { get; } = JsonDocument.Parse(@"{
        ""type"": ""object"",
        ""properties"": {
            ""flow"": {
                ""type"": ""object"",
                ""description"": ""工作流 JSON 对象，包含 operators 和 connections""
            },
            ""testInputsMode"": { ""type"": ""string"", ""description"": ""可选，测试输入模式，默认为 'empty_stub'"" }
        },
        ""required"": [""flow""]
    }").RootElement;

    public async Task<VisionAgentToolResult> ExecuteAsync(
        VisionAgentToolContext context,
        JsonElement arguments,
        CancellationToken cancellationToken)
    {
        if (arguments.ValueKind != JsonValueKind.Object ||
            !arguments.TryGetProperty("flow", out var flowProp))
        {
            return VisionAgentToolResult.CreateFailure("Missing or invalid 'flow' parameter.");
        }

        AiGeneratedFlowJson? flowJson;
        try
        {
            var flowRaw = flowProp.GetRawText();
            flowJson = JsonSerializer.Deserialize<AiGeneratedFlowJson>(flowRaw, _jsonOptions);
        }
        catch (Exception ex)
        {
            return VisionAgentToolResult.CreateFailure($"Failed to parse 'flow' argument: {ex.Message}");
        }

        if (flowJson == null)
        {
            return VisionAgentToolResult.CreateFailure("Flow argument deserialized to null.");
        }

        var stopwatch = Stopwatch.StartNew();

        try
        {
            // 1. Convert to DTO
            var (dto, _) = ConvertToFlowDto(flowJson);

            // 2. Convert DTO to Entity
            var flowEntity = ConvertDtoToEntity(dto);

            // 3. Execute DryRun
            var drResult = await _dryRunService.RunAsync(
                flowEntity,
                new Dictionary<string, object>(), // 空输入
                new DryRunStubRegistry(),
                cancellationToken);

            var responseData = new
            {
                isSuccess = drResult.IsSuccess,
                durationMs = stopwatch.ElapsedMilliseconds,
                coveragePercentage = drResult.CoveragePercentage,
                coveredBranches = drResult.CoveredBranches,
                totalBranches = drResult.TotalBranches,
                warnings = Array.Empty<string>()
            };

            var summary = drResult.IsSuccess
                ? $"DryRun passed. Coverage: {drResult.CoveragePercentage:F1}% ({drResult.CoveredBranches}/{drResult.TotalBranches} branches)"
                : "DryRun failed.";

            return VisionAgentToolResult.CreateSuccess(responseData, summary);
        }
        catch (Exception ex)
        {
            var errorData = new
            {
                isSuccess = false,
                durationMs = stopwatch.ElapsedMilliseconds,
                coveragePercentage = 0.0,
                coveredBranches = 0,
                totalBranches = 0,
                warnings = new[] { $"DryRun failed during conversion or simulation: {ex.Message}" }
            };

            return VisionAgentToolResult.CreateSuccess(errorData, $"DryRun failed: {ex.Message}");
        }
    }

    private (OperatorFlowDto Flow, Dictionary<string, string> ActualOperatorIdMap) ConvertToFlowDto(
        AiGeneratedFlowJson generated)
    {
        var opInfoMapping = new Dictionary<string, (Guid Id, OperatorMetadata Meta)>();
        var portMapping = new Dictionary<string, (Dictionary<string, Guid> Inputs, Dictionary<string, Guid> Outputs)>();

        foreach (var op in generated.Operators)
        {
            var type = Enum.Parse<OperatorType>(op.OperatorType);
            var metadata = _operatorFactory.GetMetadata(type) ?? throw new InvalidOperationException($"Operator {type} is not registered.");
            var operatorId = Guid.NewGuid();
            opInfoMapping[op.TempId] = (operatorId, metadata);

            var inputPorts = new Dictionary<string, Guid>();
            foreach (var p in metadata.InputPorts)
                inputPorts[p.Name] = Guid.NewGuid();

            var outputPorts = new Dictionary<string, Guid>();
            foreach (var p in metadata.OutputPorts)
                outputPorts[p.Name] = Guid.NewGuid();

            portMapping[op.TempId] = (inputPorts, outputPorts);
        }

        var operators = generated.Operators.Select(op =>
        {
            var (operatorId, metadata) = opInfoMapping[op.TempId];
            var (inputs, outputs) = portMapping[op.TempId];

            return new OperatorDto
            {
                Id = operatorId,
                Name = op.DisplayName,
                Type = metadata.Type,
                X = 0,
                Y = 0,
                IsEnabled = true,
                InputPorts = metadata.InputPorts.Select(p => new PortDto
                {
                    Id = inputs[p.Name],
                    Name = p.Name,
                    Direction = PortDirection.Input,
                    DataType = p.DataType,
                    IsRequired = p.IsRequired
                }).ToList(),
                OutputPorts = metadata.OutputPorts.Select(p => new PortDto
                {
                    Id = outputs[p.Name],
                    Name = p.Name,
                    Direction = PortDirection.Output,
                    DataType = p.DataType
                }).ToList(),
                Parameters = metadata.Parameters.Select(p => new ParameterDto
                {
                    Id = Guid.NewGuid(),
                    Name = p.Name,
                    DisplayName = p.DisplayName,
                    Description = p.Description,
                    DataType = p.DataType,
                    DefaultValue = p.DefaultValue,
                    IsRequired = p.IsRequired,
                    Options = p.Options?.Select(opt => new ClearVision.Product.Core.ValueObjects.ParameterOption
                    {
                        Label = opt.Label,
                        Value = opt.Value
                    }).ToList(),
                    Value = op.Parameters.TryGetValue(p.Name, out var val) ? val : null
                }).ToList()
            };
        }).ToList();

        var connections = generated.Connections?.Select(conn =>
        {
            var outputs = portMapping[conn.SourceTempId].Outputs;
            if (!outputs.TryGetValue(conn.SourcePortName, out var srcPortId))
            {
                throw new InvalidOperationException($"Source operator {conn.SourceTempId} does not have output port '{conn.SourcePortName}'");
            }

            var inputs = portMapping[conn.TargetTempId].Inputs;
            if (!inputs.TryGetValue(conn.TargetPortName, out var tgtPortId))
            {
                throw new InvalidOperationException($"Target operator {conn.TargetTempId} does not have input port '{conn.TargetPortName}'");
            }

            return new OperatorConnectionDto
            {
                Id = Guid.NewGuid(),
                SourceOperatorId = opInfoMapping[conn.SourceTempId].Id,
                SourcePortId = srcPortId,
                TargetOperatorId = opInfoMapping[conn.TargetTempId].Id,
                TargetPortId = tgtPortId
            };
        }).ToList() ?? new List<OperatorConnectionDto>();

        return (
            new OperatorFlowDto
            {
                Id = Guid.NewGuid(),
                Name = "DryRun Flow",
                Operators = operators,
                Connections = connections
            },
            opInfoMapping.ToDictionary(
                item => item.Key,
                item => item.Value.Id.ToString(),
                StringComparer.OrdinalIgnoreCase));
    }

    private OperatorFlow ConvertDtoToEntity(OperatorFlowDto dto)
    {
        var flow = new OperatorFlow(dto.Name);
        typeof(OperatorFlow).GetProperty("Id")?.SetValue(flow, dto.Id);

        flow.Operators = dto.Operators.Select(o =>
        {
            var op = _operatorFactory.CreateOperator(o.Type, o.Name, o.X, o.Y);
            typeof(Operator).GetProperty("Id")?.SetValue(op, o.Id);

            foreach (var pDto in o.Parameters)
            {
                var targetParam = op.Parameters.FirstOrDefault(p => p.Name == pDto.Name);
                if (targetParam != null && pDto.Value != null)
                    targetParam.SetValue(pDto.Value);
            }
            return op;
        }).ToList();

        flow.Connections = dto.Connections.Select(c =>
        {
            var conn = new ClearVision.Product.Core.ValueObjects.OperatorConnection(c.SourceOperatorId, c.SourcePortId, c.TargetOperatorId, c.TargetPortId);
            typeof(ClearVision.Product.Core.ValueObjects.OperatorConnection).GetProperty("Id")?.SetValue(conn, c.Id);
            return conn;
        }).ToList();

        return flow;
    }
}
