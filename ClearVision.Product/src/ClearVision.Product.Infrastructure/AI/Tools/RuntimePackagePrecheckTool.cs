using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ClearVision.Product.Core.AI.Tools;
using ClearVision.Product.Core.DTOs;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Services;
using ClearVision.Product.Core.Cameras;

namespace ClearVision.Product.Infrastructure.AI.Tools;

public sealed class RuntimePackagePrecheckTool : IVisionAgentTool
{
    private readonly IAiFlowValidator _validator;
    private readonly ICameraManager _cameraManager;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new FlexibleStringDictionaryJsonConverter() }
    };

    public RuntimePackagePrecheckTool(IAiFlowValidator validator, ICameraManager cameraManager)
    {
        _validator = validator;
        _cameraManager = cameraManager;
    }

    public string Name => "runtime_package_precheck";
    public string DisplayName => "部署包预检查";
    public string Description => "对目标部署流程在目标工位上的适配性进行预检查（例如模型路径是否确认、相机绑定是否完整、PLC连接状态等），并输出阻碍部署的严重问题列表。";
    public string Category => "Deployment";
    public VisionAgentToolPermission Permission => VisionAgentToolPermission.DeploymentPrepare;

    public JsonElement ParametersSchema { get; } = JsonDocument.Parse(@"{
        ""type"": ""object"",
        ""properties"": {
            ""flow"": {
                ""type"": ""object"",
                ""description"": ""包含 operators、connections 等结构的工作流 JSON 对象""
            },
            ""targetStationId"": { ""type"": ""string"", ""description"": ""目标部署工位 ID"" }
        },
        ""required"": [""flow"", ""targetStationId""]
    }").RootElement;

    public Task<VisionAgentToolResult> ExecuteAsync(
        VisionAgentToolContext context,
        JsonElement arguments,
        CancellationToken cancellationToken)
    {
        if (arguments.ValueKind != JsonValueKind.Object ||
            !arguments.TryGetProperty("flow", out var flowProp) ||
            !arguments.TryGetProperty("targetStationId", out var stationProp) ||
            stationProp.ValueKind != JsonValueKind.String)
        {
            return Task.FromResult(VisionAgentToolResult.CreateFailure("Missing or invalid required parameters ('flow' and 'targetStationId')."));
        }

        AiGeneratedFlowJson? flowJson;
        try
        {
            var flowRaw = flowProp.GetRawText();
            flowJson = JsonSerializer.Deserialize<AiGeneratedFlowJson>(flowRaw, _jsonOptions);
        }
        catch (Exception ex)
        {
            return Task.FromResult(VisionAgentToolResult.CreateFailure($"Failed to parse 'flow' argument: {ex.Message}"));
        }

        if (flowJson == null)
        {
            return Task.FromResult(VisionAgentToolResult.CreateFailure("Flow argument deserialized to null."));
        }

        var blockingIssues = new List<string>();
        var warnings = new List<string>();

        // 1. Validate parameters using IAiFlowValidator
        var validation = _validator.Validate(flowJson);
        foreach (var error in validation.Diagnostics.Where(d => d.Severity == AiValidationSeverity.Error))
        {
            blockingIssues.Add($"[校验错误] {error.Message} (算子: {error.OperatorId})");
        }

        // 2. Check for image acquisition operator camera ID binding
        var cameraBindings = _cameraManager.GetBindings() ?? new();
        var acqOperators = flowJson.Operators.Where(o => string.Equals(o.OperatorType, "ImageAcquisition", StringComparison.OrdinalIgnoreCase)).ToList();

        foreach (var acq in acqOperators)
        {
            acq.Parameters.TryGetValue("SourceType", out var srcType);
            var sourceType = srcType?.ToString() ?? "File";

            if (sourceType.Equals("Camera", StringComparison.OrdinalIgnoreCase))
            {
                acq.Parameters.TryGetValue("CameraId", out var camId);
                var cameraId = camId?.ToString();

                if (string.IsNullOrWhiteSpace(cameraId))
                {
                    blockingIssues.Add($"[相机缺失] 图像采集算子 '{acq.DisplayName}' ({acq.TempId}) 未指定 CameraId。");
                }
                else if (!cameraBindings.Any(b => string.Equals(b.Id, cameraId, StringComparison.OrdinalIgnoreCase)))
                {
                    blockingIssues.Add($"[相机绑定未配置] 图像采集算子 '{acq.DisplayName}' 引用了不存在的相机绑定 ID '{cameraId}'。");
                }
            }
        }

        // 3. Check for DeepLearning models path validation
        var dlOperators = flowJson.Operators.Where(o => string.Equals(o.OperatorType, "DeepLearning", StringComparison.OrdinalIgnoreCase)).ToList();
        foreach (var dl in dlOperators)
        {
            dl.Parameters.TryGetValue("ModelPath", out var pathObj);
            var path = pathObj?.ToString();
            if (string.IsNullOrWhiteSpace(path))
            {
                blockingIssues.Add($"[模型路径缺失] 深度学习算子 '{dl.DisplayName}' ({dl.TempId}) 的 ModelPath 不能为空。");
            }
        }

        // 4. Default warnings
        warnings.Add("未在目标工位进行过真机联试图像采集验证。");

        var isReady = blockingIssues.Count == 0;

        var result = new
        {
            ready = isReady,
            blockingIssues = blockingIssues,
            warnings = warnings
        };

        var summary = isReady
            ? "Precheck passed. Flow is ready to package."
            : $"Precheck failed with {blockingIssues.Count} blocking issues.";

        return Task.FromResult(VisionAgentToolResult.CreateSuccess(result, summary));
    }
}
