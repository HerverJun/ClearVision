using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ClearVision.Product.Core.AI.Tools;
using ClearVision.Product.Core.DTOs;

namespace ClearVision.Product.Infrastructure.AI.Tools;

public sealed class RuntimePackageManifestDraftTool : IVisionAgentTool
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new FlexibleStringDictionaryJsonConverter() }
    };

    public string Name => "draft_runtime_package_manifest";
    public string DisplayName => "生成部署包清单草稿";
    public string Description => "基于当前的流程图，整理并输出部署所需资源包的清单草稿（包含所需深度学习模型、相机ID绑定、PLC连线元信息）。只读。";
    public string Category => "Deployment";
    public VisionAgentToolPermission Permission => VisionAgentToolPermission.ConfigDraft;

    public JsonElement ParametersSchema { get; } = JsonDocument.Parse(@"{
        ""type"": ""object"",
        ""properties"": {
            ""flow"": {
                ""type"": ""object"",
                ""description"": ""包含 operators、connections 等结构的工作流 JSON 对象""
            },
            ""packageName"": { ""type"": ""string"", ""description"": ""可选，建议的运行包名称，如 'wire-sequence-line1-v1'"" }
        },
        ""required"": [""flow""]
    }").RootElement;

    public Task<VisionAgentToolResult> ExecuteAsync(
        VisionAgentToolContext context,
        JsonElement arguments,
        CancellationToken cancellationToken)
    {
        if (arguments.ValueKind != JsonValueKind.Object ||
            !arguments.TryGetProperty("flow", out var flowProp))
        {
            return Task.FromResult(VisionAgentToolResult.CreateFailure("Missing or invalid 'flow' parameter."));
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

        string packageName = "wire-sequence-deploy-package";
        if (arguments.TryGetProperty("packageName", out var nameProp) && nameProp.ValueKind == JsonValueKind.String)
        {
            packageName = nameProp.GetString() ?? packageName;
        }

        var requiredModels = new List<string>();
        var requiredCameraBindings = new List<string>();
        var requiredPlcConnections = new List<string>();
        var pendingApprovals = new List<string> { "需要确认相机物理接线", "需要最终人工跑通DryRun" };

        foreach (var op in flowJson.Operators)
        {
            if (string.Equals(op.OperatorType, "DeepLearning", StringComparison.OrdinalIgnoreCase))
            {
                op.Parameters.TryGetValue("ModelPath", out var pathObj);
                var path = pathObj?.ToString();
                if (!string.IsNullOrWhiteSpace(path))
                {
                    requiredModels.Add(path);
                }
            }
            else if (string.Equals(op.OperatorType, "ImageAcquisition", StringComparison.OrdinalIgnoreCase))
            {
                op.Parameters.TryGetValue("CameraId", out var camObj);
                var cameraId = camObj?.ToString();
                if (!string.IsNullOrWhiteSpace(cameraId))
                {
                    requiredCameraBindings.Add(cameraId);
                }
            }
            else if (string.Equals(op.OperatorType, "PlcOutput", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(op.OperatorType, "PlcInput", StringComparison.OrdinalIgnoreCase))
            {
                op.Parameters.TryGetValue("Address", out var addrObj);
                var address = addrObj?.ToString();
                if (!string.IsNullOrWhiteSpace(address))
                {
                    requiredPlcConnections.Add(address);
                }
            }
        }

        // Generate simple hash from flowJson as a fingerprint
        string flowHash = "";
        try
        {
            using var sha = SHA256.Create();
            var rawBytes = Encoding.UTF8.GetBytes(flowProp.GetRawText());
            var hashBytes = sha.ComputeHash(rawBytes);
            flowHash = Convert.ToHexString(hashBytes).ToLowerInvariant();
        }
        catch
        {
            flowHash = Guid.NewGuid().ToString("N");
        }

        var result = new
        {
            packageName = packageName,
            flowHash = flowHash,
            requiredModels = requiredModels.Distinct().ToList(),
            requiredCameraBindings = requiredCameraBindings.Distinct().ToList(),
            requiredPlcConnections = requiredPlcConnections.Distinct().ToList(),
            pendingApprovals = pendingApprovals
        };

        var summary = $"Draft deployment manifest created for package '{packageName}'.";
        return Task.FromResult(VisionAgentToolResult.CreateSuccess(result, summary));
    }
}
