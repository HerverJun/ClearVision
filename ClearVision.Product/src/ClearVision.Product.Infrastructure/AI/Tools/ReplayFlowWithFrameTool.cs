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
using ClearVision.Product.Infrastructure.AI.Runtime;
using ClearVision.Product.Application.DTOs;

namespace ClearVision.Product.Infrastructure.AI.Tools;

public sealed class ReplayFlowWithFrameTool : IVisionAgentTool
{
    private readonly DryRunService _dryRunService;
    private readonly IOperatorFactory _operatorFactory;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new FlexibleStringDictionaryJsonConverter() }
    };

    public ReplayFlowWithFrameTool(DryRunService dryRunService, IOperatorFactory operatorFactory)
    {
        _dryRunService = dryRunService;
        _operatorFactory = operatorFactory;
    }

    public string Name => "replay_flow_with_frame";
    public string DisplayName => "真实图片回放校验";
    public string Description => "将 capture_test_frame 获取的临时测试帧，作为测试输入喂给流程执行器，仿真评估流程中后续算子（如模板匹配、深度学习）在真实图片上的表现。";
    public string Category => "Validation";
    public VisionAgentToolPermission Permission => VisionAgentToolPermission.Simulation;

    public JsonElement ParametersSchema { get; } = JsonDocument.Parse(@"{
        ""type"": ""object"",
        ""properties"": {
            ""flow"": {
                ""type"": ""object"",
                ""description"": ""包含 operators、connections 等结构的工作流 JSON 对象""
            },
            ""temporaryFrameId"": {
                ""type"": ""string"",
                ""description"": ""通过 capture_test_frame 获取的临时测试帧 ID""
            },
            ""entryOperatorTempId"": {
                ""type"": ""string"",
                ""description"": ""可选，图像输入算子的临时 ID，如果不填默认为第一个采集算子""
            }
        },
        ""required"": [""flow"", ""temporaryFrameId""]
    }").RootElement;

    public async Task<VisionAgentToolResult> ExecuteAsync(
        VisionAgentToolContext context,
        JsonElement arguments,
        CancellationToken cancellationToken)
    {
        if (arguments.ValueKind != JsonValueKind.Object ||
            !arguments.TryGetProperty("flow", out var flowProp) ||
            !arguments.TryGetProperty("temporaryFrameId", out var frameProp) ||
            frameProp.ValueKind != JsonValueKind.String)
        {
            return VisionAgentToolResult.CreateFailure("Missing or invalid required parameters ('flow' and 'temporaryFrameId').");
        }

        var temporaryFrameId = frameProp.GetString() ?? string.Empty;
        
        // 1. Get image from TemporaryFrameCache
        if (!TemporaryFrameCache.TryGet(temporaryFrameId, out var frameBytes, out var width, out var height, out var format))
        {
            return VisionAgentToolResult.CreateFailure($"Temporary frame '{temporaryFrameId}' has expired or is invalid.");
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
            // 2. Convert to DTO and keep actual operator mappings
            var (dto, opIdMapping) = ConvertToFlowDto(flowJson);
            var idToTempId = opIdMapping.ToDictionary(kv => kv.Value, kv => kv.Key, StringComparer.OrdinalIgnoreCase);

            // 3. Convert to OperatorFlow Entity
            var flowEntity = ConvertDtoToEntity(dto);

            // 4. Inject test frame into inputs
            var testInputs = new Dictionary<string, object>
            {
                ["Image"] = frameBytes
            };

            // 5. Run DryRun
            var drResult = await _dryRunService.RunAsync(
                flowEntity,
                testInputs,
                new DryRunStubRegistry(),
                cancellationToken);

            // 6. Map operator results back to temporary IDs
            var operatorResults = new List<object>();
            if (drResult.FlowResult?.OperatorResults != null)
            {
                foreach (var opResult in drResult.FlowResult.OperatorResults)
                {
                    var guidStr = opResult.OperatorId.ToString();
                    idToTempId.TryGetValue(guidStr, out var tempId);
                    tempId ??= guidStr;

                    double? score = null;
                    if (opResult.OutputData != null)
                    {
                        if (opResult.OutputData.TryGetValue("Score", out var sVal) && sVal is double sd) score = sd;
                        else if (opResult.OutputData.TryGetValue("score", out var sVal2) && sVal2 is double sd2) score = sd2;
                        else if (opResult.OutputData.TryGetValue("Confidence", out var cVal) && cVal is double cd) score = cd;
                        else if (opResult.OutputData.TryGetValue("MatchScore", out var msVal) && msVal is double msd) score = msd;
                    }

                    operatorResults.Add(new
                    {
                        operatorTempId = tempId,
                        operatorType = opResult.OperatorName,
                        score = score,
                        status = opResult.IsSuccess ? "passed" : "failed",
                        errorMessage = opResult.ErrorMessage
                    });
                }
            }

            var responseData = new
            {
                isSuccess = drResult.IsSuccess,
                durationMs = stopwatch.ElapsedMilliseconds,
                frameWidth = width,
                frameHeight = height,
                operatorResults = operatorResults,
                warnings = Array.Empty<string>()
            };

            var summary = drResult.IsSuccess
                ? $"Replay validation succeeded with frame {width}x{height}."
                : "Replay validation failed.";

            return VisionAgentToolResult.CreateSuccess(responseData, summary);
        }
        catch (Exception ex)
        {
            var errorData = new
            {
                isSuccess = false,
                durationMs = stopwatch.ElapsedMilliseconds,
                frameWidth = width,
                frameHeight = height,
                operatorResults = Array.Empty<object>(),
                warnings = new[] { $"Replay failed during conversion or simulation: {ex.Message}" }
            };

            return VisionAgentToolResult.CreateSuccess(errorData, $"Replay execution error: {ex.Message}");
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
                Name = "Replay Flow",
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
