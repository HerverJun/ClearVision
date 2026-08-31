using System.Text.Json;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.ProjectVariables;
using ClearVision.Product.Core.Services;
using ClearVision.Product.Core.ValueObjects;
using ClearVision.Product.Infrastructure.AI.Runtime;
using ClearVision.Product.Infrastructure.Operators;
using Microsoft.Extensions.Logging;
using OpenCvSharp;

namespace ClearVision.Product.Infrastructure.Services;

public sealed class FlowNodePreviewService : IFlowNodePreviewService
{
    private readonly ILogger<FlowNodePreviewService> _logger;
    private readonly IFlowExecutionService _flowExecution;
    private readonly IPreviewMetricsAnalyzer _metricsAnalyzer;
    private readonly IOperatorFactory _operatorFactory;

    public FlowNodePreviewService(
        ILogger<FlowNodePreviewService> logger,
        IFlowExecutionService flowExecution,
        IPreviewMetricsAnalyzer metricsAnalyzer,
        IOperatorFactory operatorFactory)
    {
        _logger = logger;
        _flowExecution = flowExecution;
        _metricsAnalyzer = metricsAnalyzer;
        _operatorFactory = operatorFactory;
    }

    public async Task<FlowNodePreviewWithMetricsResult> PreviewWithMetricsAsync(
        OperatorFlow flow,
        Guid targetNodeId,
        byte[]? inputImage,
        Guid projectId,
        long persistenceRevision,
        ExecutionRequestAuthority authority,
        ProjectVariableExecutionContext? projectVariables = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(authority);
        return await PreviewWithMetricsCoreAsync(
            flow,
            targetNodeId,
            inputImage,
            projectVariables,
            projectId,
            persistenceRevision,
            authority,
            ct);
    }

    private async Task<FlowNodePreviewWithMetricsResult> PreviewWithMetricsCoreAsync(
        OperatorFlow flow,
        Guid targetNodeId,
        byte[]? inputImage,
        ProjectVariableExecutionContext? projectVariables,
        Guid projectId,
        long persistenceRevision,
        ExecutionRequestAuthority authority,
        CancellationToken ct)
    {
        var targetOperator = flow.Operators.FirstOrDefault(item => item.Id == targetNodeId);
        if (targetOperator == null)
        {
            return new FlowNodePreviewWithMetricsResult
            {
                Success = false,
                TargetNodeId = targetNodeId,
                ErrorMessage = $"未找到目标节点: {targetNodeId}"
            };
        }

        var snapshot = CreatePreviewSnapshot(flow, projectId, persistenceRevision, authority);
        var validation = _flowExecution.ValidateSnapshot(snapshot);
        if (validation == null || !validation.IsValid)
        {
            return new FlowNodePreviewWithMetricsResult
            {
                Success = false,
                TargetNodeId = targetNodeId,
                ErrorMessage = validation == null
                    ? "ADMISSION_FLOW_VALIDATION_UNAVAILABLE: Preview flow validation is unavailable."
                    : $"ADMISSION_FLOW_INVALID: {string.Join("; ", validation.Errors)}",
                DiagnosticCodes = ["admission_rejected"]
            };
        }

        var missingResources = CollectMissingResources(flow, targetNodeId, inputImage);
        if (missingResources.Count > 0)
        {
            return new FlowNodePreviewWithMetricsResult
            {
                Success = false,
                TargetNodeId = targetNodeId,
                ErrorMessage = "预览缺少必要资源或参数配置：" +
                               string.Join("；", missingResources.Select(item => item.Description)),
                MissingResources = missingResources,
                DiagnosticCodes = missingResources
                    .Select(item => item.DiagnosticCode)
                    .Where(item => !string.IsNullOrWhiteSpace(item))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList()
            };
        }

        var debugSessionId = Guid.NewGuid();
        var debugOptions = new DebugOptions
        {
            DebugSessionId = debugSessionId,
            EnableIntermediateCache = true,
            BreakAtOperatorId = targetNodeId,
            ImageFormat = ".png"
        };

        var externalInputImage = ShouldUseExternalInputImage(flow, targetNodeId)
            ? inputImage
            : null;
        var inputData = BuildInputData(externalInputImage);
        var result = await _flowExecution.ExecuteDebugWithSnapshotAsync(
            snapshot,
            debugOptions,
            inputData,
            projectVariables,
            ct);

        if (!result.IntermediateResults.TryGetValue(targetNodeId, out var nodeOutput))
        {
            nodeOutput = _flowExecution.GetDebugIntermediateResult(debugSessionId, targetNodeId);
        }

        if (nodeOutput == null)
        {
            return BuildFailureResult(flow, targetNodeId, result, missingResources);
        }

        var targetDebugResult = result.DebugOperatorResults.FirstOrDefault(item => item.OperatorId == targetNodeId);
        var previewImageBytes = TryGetOutputImageBytes(nodeOutput)
            ?? TryGetImageBytesFromSnapshot(targetDebugResult?.InputSnapshot);
        var inputSnapshotImage = ResolveInputImageBytes(flow, targetNodeId, result, targetDebugResult, externalInputImage);
        var sanitizedOutputs = BuildResponseOutputData(nodeOutput);
        var metrics = AnalyzePreviewMetrics(previewImageBytes, sanitizedOutputs);
        var diagnosticCodes = BuildDiagnosticCodes(metrics, missingResources);

        return new FlowNodePreviewWithMetricsResult
        {
            Success = result.IsSuccess,
            TargetNodeId = targetNodeId,
            InputImage = inputSnapshotImage,
            PreviewImage = previewImageBytes,
            Outputs = sanitizedOutputs,
            Metrics = metrics,
            Suggestions = metrics?.Suggestions?.ToList() ?? new List<ParameterSuggestion>(),
            MissingResources = missingResources,
            DiagnosticCodes = diagnosticCodes,
            ErrorMessage = result.ErrorMessage,
            ExecutedOperators = result.DebugOperatorResults
                .Select(item => new ExecutedOperatorTrace
                {
                    OperatorId = item.OperatorId,
                    OperatorName = item.OperatorName,
                    ExecutionOrder = item.ExecutionOrder,
                    ExecutionTimeMs = item.ExecutionTimeMs,
                    IsSuccess = item.IsSuccess
                })
                .ToList()
        };
    }

    private static ExecutionSnapshot CreatePreviewSnapshot(
        OperatorFlow flow,
        Guid projectId,
        long persistenceRevision,
        ExecutionRequestAuthority authority)
    {
        if (projectId == Guid.Empty || persistenceRevision < 0)
        {
            throw new InvalidOperationException("ADMISSION_DRAFT_PROJECT_BINDING_REQUIRED: AutoTune preview requires a valid project revision binding.");
        }

        var resourceBindings = authority.ResourceBindings.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        resourceBindings["ProjectRevision"] = persistenceRevision.ToString(System.Globalization.CultureInfo.InvariantCulture);
        resourceBindings["FlowHash"] = ExecutionFlowIdentity.ComputeFlowHash(flow);

        return new ExecutionSnapshot(
            projectId,
            flow,
            persistenceRevision,
            ExecutionSnapshotSource.Draft,
            ExecutionRunMode.Preview,
            resourceBindings: resourceBindings,
            principal: authority.Principal,
            capabilityManifest: authority.CapabilityManifest,
            expectedProjectRevision: authority.ExpectedProjectRevision,
            confirmationId: authority.ConfirmationId,
            auditId: authority.AuditId);
    }

    private PreviewMetrics? AnalyzePreviewMetrics(byte[]? previewImageBytes, Dictionary<string, object> outputData)
    {
        if (previewImageBytes == null || previewImageBytes.Length == 0)
        {
            return null;
        }

        try
        {
            using var image = Cv2.ImDecode(previewImageBytes, ImreadModes.Unchanged);
            if (image.Empty())
            {
                return null;
            }

            return _metricsAnalyzer.Analyze(image, outputData);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[FlowNodePreview] 无法分析预览指标");
            return null;
        }
    }

    private static Dictionary<string, object>? BuildInputData(byte[]? inputImage)
    {
        if (inputImage == null || inputImage.Length == 0)
        {
            return null;
        }

        return new Dictionary<string, object>
        {
            ["Image"] = inputImage
        };
    }

    private List<PreviewMissingResource> CollectMissingResources(
        OperatorFlow flow,
        Guid targetNodeId,
        byte[]? externalInputImage)
    {
        var relevantOperators = CollectRelevantOperators(flow, targetNodeId);
        var missing = new List<PreviewMissingResource>();
        var externalImageCanSatisfyAcquisition = externalInputImage is { Length: > 0 } &&
                                                 ShouldUseExternalInputImage(flow, targetNodeId);

        foreach (var op in relevantOperators)
        {
            var metadata = _operatorFactory.GetMetadata(op.Type);
            if (metadata == null)
            {
                continue;
            }

            var values = op.Parameters
                .GroupBy(parameter => parameter.Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group.First().GetValue(),
                    StringComparer.OrdinalIgnoreCase);
            var explicitNames = values.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var canonicalization = OperatorParameterConstraintEvaluator.Canonicalize(metadata, values, explicitNames);
            var states = OperatorParameterConstraintEvaluator.ResolveStates(metadata, values, explicitNames);
            IReadOnlySet<string>? satisfiedInputPorts = externalImageCanSatisfyAcquisition
                ? new HashSet<string>(["Image"], StringComparer.OrdinalIgnoreCase)
                : null;

            foreach (var violation in OperatorParameterConstraintEvaluator.Validate(
                         metadata,
                         values,
                         explicitNames,
                         satisfiedInputPorts: satisfiedInputPorts))
            {
                missing.Add(CreateConstraintIssue(op, violation));
            }

            foreach (var state in states.Where(state =>
                         !state.EffectiveDisabled &&
                         !state.EffectiveIgnored &&
                         !string.IsNullOrWhiteSpace(state.Constraint.ResourceKind)))
            {
                if (OperatorParameterConstraintEvaluator.IsSatisfiedByInputPort(
                        state.Constraint,
                        satisfiedInputPorts))
                {
                    continue;
                }

                ValidateConfiguredResource(
                    op,
                    metadata,
                    state,
                    canonicalization.EffectiveValues,
                    missing);
            }
        }

        return missing
            .GroupBy(
                item => $"{item.ResourceKey}|{item.DiagnosticCode}",
                StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
    }

    private static PreviewMissingResource CreateConstraintIssue(
        Operator op,
        OperatorParameterConstraintViolation violation)
    {
        IReadOnlyList<string> parameterNames = violation.ParameterNames.Count == 0
            ? ["configuration"]
            : violation.ParameterNames;
        var firstParameter = parameterNames[0];
        var description = violation.Code switch
        {
            "at-least-one" => $"至少需要配置以下一项：{string.Join("、", parameterNames)}",
            "mutually-exclusive" => $"以下参数不能同时配置：{string.Join("、", parameterNames)}",
            _ => $"缺少当前模式必需的参数：{firstParameter}"
        };

        return new PreviewMissingResource
        {
            ResourceType = ResolveResourceType(violation.ResourceKind),
            ResourceKey = $"{op.Type}.{firstParameter}",
            Description = description,
            DiagnosticCode = violation.Code == "mutually-exclusive"
                ? "conflicting_resource_configuration"
                : ResolveDiagnosticCode(violation.ResourceKind)
        };
    }

    private static void ValidateConfiguredResource(
        Operator op,
        OperatorMetadata metadata,
        OperatorParameterConstraintState state,
        IReadOnlyDictionary<string, object?> effectiveValues,
        List<PreviewMissingResource> missing)
    {
        var parameterName = state.Constraint.Parameter;
        effectiveValues.TryGetValue(parameterName, out var rawValue);
        var value = rawValue?.ToString()?.Trim() ?? string.Empty;
        var resourceKind = state.Constraint.ResourceKind;

        if (string.Equals(resourceKind, "model_labels", StringComparison.OrdinalIgnoreCase))
        {
            ValidateModelLabels(op, parameterName, effectiveValues, missing);
            return;
        }

        if (string.IsNullOrWhiteSpace(value) || OperatorParameterValueSemantics.IsPendingSentinel(value))
        {
            return;
        }

        if (string.Equals(resourceKind, "image_file", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(resourceKind, "model_catalog", StringComparison.OrdinalIgnoreCase))
        {
            if (!FileExists(value))
            {
                var description = string.Equals(resourceKind, "image_file", StringComparison.OrdinalIgnoreCase)
                    ? $"图像文件不存在：{value}"
                    : $"模型目录文件不存在：{value}";
                missing.Add(CreateMissingResource(op, parameterName, resourceKind, description));
            }

            return;
        }

        if (string.Equals(resourceKind, "model_resource", StringComparison.OrdinalIgnoreCase))
        {
            ValidateCatalogBackedFile(op, parameterName, value, effectiveValues, resourceKind, missing);
            return;
        }

        if (string.Equals(resourceKind, "feature_bank", StringComparison.OrdinalIgnoreCase))
        {
            if (parameterName.StartsWith("Save", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            ValidateCatalogBackedFile(op, parameterName, value, effectiveValues, resourceKind, missing);
        }
    }

    private static void ValidateCatalogBackedFile(
        Operator op,
        string parameterName,
        string value,
        IReadOnlyDictionary<string, object?> effectiveValues,
        string? resourceKind,
        List<PreviewMissingResource> missing)
    {
        if (parameterName.EndsWith("Id", StringComparison.OrdinalIgnoreCase))
        {
            var catalogPath = GetEffectiveString(effectiveValues, "ModelCatalogPath");
            if (!ModelCatalog.TryResolve(value, catalogPath, null, out var resolved, out var error) ||
                resolved == null ||
                !FileExists(resolved.ArtifactPath))
            {
                missing.Add(CreateMissingResource(
                    op,
                    parameterName,
                    resourceKind,
                    error ?? $"目录资源不存在：{value}"));
            }

            return;
        }

        if (!FileExists(value))
        {
            missing.Add(CreateMissingResource(op, parameterName, resourceKind, $"文件不存在：{value}"));
        }
    }

    private static void ValidateModelLabels(
        Operator op,
        string parameterName,
        IReadOnlyDictionary<string, object?> effectiveValues,
        List<PreviewMissingResource> missing)
    {
        var taskType = GetEffectiveString(effectiveValues, "TaskType");
        if (!DeepLearningTaskResolver.TryParse(taskType, out var requestedTask))
        {
            return;
        }

        var modelPath = ResolveModelPath(effectiveValues, out var catalogEntry);
        var requiresLabels = requestedTask == DeepLearningTaskType.ObjectDetection ||
                             (requestedTask == DeepLearningTaskType.Auto &&
                              DeepLearningTaskResolver.TryResolveCatalogType(catalogEntry?.Type, out var catalogTask) &&
                              catalogTask == DeepLearningTaskType.ObjectDetection);
        if (!requiresLabels)
        {
            return;
        }

        var labelsPath = GetEffectiveString(effectiveValues, parameterName);
        var targetClasses = GetEffectiveString(effectiveValues, "TargetClasses");
        if (DeepLearningLabelResolver.AreLabelsResolvable(labelsPath, modelPath, targetClasses, out _))
        {
            return;
        }

        missing.Add(CreateMissingResource(
            op,
            parameterName,
            "model_labels",
            string.IsNullOrWhiteSpace(labelsPath)
                ? "缺少可用的标签文件，且模型或内置资源未提供可解析标签。"
                : $"标签文件不可用，且模型或内置资源未提供可解析标签：{labelsPath}"));
    }

    private static string ResolveModelPath(
        IReadOnlyDictionary<string, object?> effectiveValues,
        out ModelCatalogEntry? catalogEntry)
    {
        catalogEntry = null;
        var explicitPath = GetEffectiveString(effectiveValues, "ModelPath");
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            return explicitPath;
        }

        var modelId = GetEffectiveString(effectiveValues, "ModelId");
        if (string.IsNullOrWhiteSpace(modelId))
        {
            return string.Empty;
        }

        try
        {
            return ModelCatalog.ResolveExplicitOrCatalogPath(
                null,
                modelId,
                GetEffectiveString(effectiveValues, "ModelCatalogPath"),
                null,
                out catalogEntry);
        }
        catch
        {
            return string.Empty;
        }
    }

    private static PreviewMissingResource CreateMissingResource(
        Operator op,
        string parameterName,
        string? resourceKind,
        string description)
    {
        return new PreviewMissingResource
        {
            ResourceType = ResolveResourceType(resourceKind),
            ResourceKey = $"{op.Type}.{parameterName}",
            Description = description,
            DiagnosticCode = ResolveDiagnosticCode(resourceKind)
        };
    }

    private static string ResolveResourceType(string? resourceKind) => resourceKind switch
    {
        "image_file" => "ImageFile",
        "camera_binding" => "Camera",
        "template_resource" => "Template",
        "model_resource" => "Model",
        "model_catalog" => "ModelCatalog",
        "model_labels" => "Label",
        "feature_bank" => "FeatureBank",
        "output_file" => "OutputFile",
        "plc_endpoint" => "PlcEndpoint",
        "plc_address" => "PlcAddress",
        "tcp_profile" => "TcpProfile",
        "network_endpoint" => "NetworkEndpoint",
        _ => "Parameter"
    };

    private static string ResolveDiagnosticCode(string? resourceKind) => resourceKind switch
    {
        "image_file" => "missing_image_file",
        "camera_binding" => "missing_camera_binding",
        "template_resource" => "missing_template",
        "model_resource" => "missing_model",
        "model_catalog" => "missing_model_catalog",
        "model_labels" => "missing_labels",
        "feature_bank" => "missing_feature_bank",
        "output_file" => "missing_output_path",
        "plc_endpoint" => "missing_plc_endpoint",
        "plc_address" => "missing_plc_address",
        "tcp_profile" => "missing_tcp_profile",
        "network_endpoint" => "missing_network_endpoint",
        _ => "missing_parameter"
    };

    private static string GetEffectiveString(
        IReadOnlyDictionary<string, object?> effectiveValues,
        string parameterName)
    {
        return effectiveValues.TryGetValue(parameterName, out var value)
            ? value?.ToString()?.Trim() ?? string.Empty
            : string.Empty;
    }

    private static bool FileExists(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            return File.Exists(Path.GetFullPath(path));
        }
        catch
        {
            return false;
        }
    }

    private static FlowNodePreviewWithMetricsResult BuildFailureResult(
        OperatorFlow flow,
        Guid targetNodeId,
        FlowDebugExecutionResult result,
        List<PreviewMissingResource> missingResources)
    {
        var targetResult = result.DebugOperatorResults.FirstOrDefault(item => item.OperatorId == targetNodeId);
        var failedOperator = targetResult?.IsSuccess == false
            ? targetResult
            : result.DebugOperatorResults.FirstOrDefault(item => !item.IsSuccess);
        var inputImage = TryGetImageBytesFromSnapshot(targetResult?.InputSnapshot)
            ?? TryGetImageBytesFromSnapshot(failedOperator?.InputSnapshot)
            ?? ResolveInputImageBytes(flow, targetNodeId, result, targetResult, null);
        var outputs = targetResult?.OutputSnapshot ?? failedOperator?.OutputSnapshot ?? new Dictionary<string, object>();

        return new FlowNodePreviewWithMetricsResult
        {
            Success = false,
            TargetNodeId = targetNodeId,
            InputImage = inputImage,
            Outputs = BuildResponseOutputData(outputs),
            MissingResources = missingResources,
            DiagnosticCodes = missingResources
                .Select(item => item.DiagnosticCode)
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            ErrorMessage = BuildMissingNodeOutputDetail(result, targetNodeId),
            FailedOperatorId = failedOperator?.OperatorId,
            FailedOperatorName = failedOperator?.OperatorName,
            ExecutedOperators = result.DebugOperatorResults
                .Select(item => new ExecutedOperatorTrace
                {
                    OperatorId = item.OperatorId,
                    OperatorName = item.OperatorName,
                    ExecutionOrder = item.ExecutionOrder,
                    ExecutionTimeMs = item.ExecutionTimeMs,
                    IsSuccess = item.IsSuccess
                })
                .ToList()
        };
    }

    private static List<string> BuildDiagnosticCodes(
        PreviewMetrics? metrics,
        IReadOnlyCollection<PreviewMissingResource> missingResources)
    {
        var codes = new List<string>();

        if (metrics != null)
        {
            foreach (var diagnostic in metrics.Diagnostics)
            {
                var mapped = diagnostic switch
                {
                    PreviewDiagnosticTags.MissingExpectedClass => "missing_expected_class",
                    PreviewDiagnosticTags.DuplicateDetectedClass => "duplicate_detected_class",
                    PreviewDiagnosticTags.DetectionCountMismatch => "detection_count_mismatch",
                    PreviewDiagnosticTags.LowDetectionConfidence => "low_detection_confidence",
                    PreviewDiagnosticTags.OrderMismatch => "order_mismatch",
                    _ => null
                };

                if (!string.IsNullOrWhiteSpace(mapped))
                {
                    codes.Add(mapped);
                }
            }
        }

        codes.AddRange(missingResources
            .Select(item => item.DiagnosticCode)
            .Where(item => !string.IsNullOrWhiteSpace(item)));

        return codes
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static HashSet<Guid> CollectRelevantOperatorIds(OperatorFlow flow, Guid targetNodeId)
    {
        var visited = new HashSet<Guid>();
        var stack = new Stack<Guid>();
        stack.Push(targetNodeId);

        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (!visited.Add(current))
            {
                continue;
            }

            foreach (var connection in flow.Connections.Where(item => item.TargetOperatorId == current))
            {
                stack.Push(connection.SourceOperatorId);
            }
        }

        return visited;
    }

    private static List<Operator> CollectRelevantOperators(OperatorFlow flow, Guid targetNodeId)
    {
        var relevantIds = CollectRelevantOperatorIds(flow, targetNodeId);
        return flow.Operators
            .Where(item => relevantIds.Contains(item.Id))
            .ToList();
    }

    private static bool ShouldUseExternalInputImage(OperatorFlow flow, Guid targetNodeId)
    {
        return ImageAcquisitionFlowAnalyzer.ShouldPassExternalInputImageToPreview(flow, targetNodeId);
    }

    private static string GetStringParam(Operator op, params string[] names)
    {
        foreach (var name in names)
        {
            var parameter = op.Parameters.FirstOrDefault(item => item.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            var raw = parameter?.GetValue()?.ToString();
            if (!string.IsNullOrWhiteSpace(raw))
            {
                return raw.Trim();
            }
        }

        return string.Empty;
    }

    private static Dictionary<string, object> BuildResponseOutputData(Dictionary<string, object> nodeOutput)
    {
        var response = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in nodeOutput)
        {
            if (IsInternalPreviewImageKey(key))
            {
                continue;
            }

            response[key] = value;
        }

        return response;
    }

    private static bool IsInternalPreviewImageKey(string key)
    {
        return string.Equals(key, "Image", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(key, "OriginalImage", StringComparison.OrdinalIgnoreCase);
    }

    private static byte[]? TryGetOutputImageBytes(Dictionary<string, object> nodeOutput)
    {
        if (!nodeOutput.TryGetValue("Image", out var imageValue) || imageValue == null)
        {
            return null;
        }

        return imageValue switch
        {
            ImageWrapper wrapper => wrapper.GetBytes(),
            Mat mat when !mat.Empty() => mat.ToBytes(".png"),
            byte[] bytes => bytes,
            string base64 when !string.IsNullOrWhiteSpace(base64) => TryDecodeBase64(base64),
            JsonElement element when element.ValueKind == JsonValueKind.String => TryDecodeBase64(element.GetString()),
            _ => null
        };
    }

    private static byte[]? TryGetImageBytesFromSnapshot(Dictionary<string, object>? snapshot)
    {
        if (snapshot == null)
        {
            return null;
        }

        return TryGetOutputImageBytes(snapshot);
    }

    private static byte[]? TryDecodeBase64(string? base64)
    {
        if (string.IsNullOrWhiteSpace(base64))
        {
            return null;
        }

        try
        {
            return Convert.FromBase64String(base64);
        }
        catch
        {
            return null;
        }
    }

    private static string BuildMissingNodeOutputDetail(FlowDebugExecutionResult result, Guid targetNodeId)
    {
        var targetResult = result.DebugOperatorResults.FirstOrDefault(item => item.OperatorId == targetNodeId);
        if (targetResult != null && !targetResult.IsSuccess)
        {
            return $"目标节点 '{targetResult.OperatorName}' 执行失败: {targetResult.ErrorMessage ?? "未知错误"}";
        }

        var failedOperator = result.DebugOperatorResults.FirstOrDefault(item => !item.IsSuccess);
        if (failedOperator != null)
        {
            return $"上游或目标节点 '{failedOperator.OperatorName}' 执行失败: {failedOperator.ErrorMessage ?? "未知错误"}";
        }

        return !string.IsNullOrWhiteSpace(result.ErrorMessage)
            ? result.ErrorMessage!
            : $"无法获取节点 {targetNodeId} 的输出，可能节点执行失败或未被执行";
    }
    private static byte[]? ResolveInputImageBytes(
        OperatorFlow flow,
        Guid targetNodeId,
        FlowDebugExecutionResult result,
        OperatorDebugResult? targetDebugResult,
        byte[]? externalInputImage)
    {
        return TryGetImageBytesFromSnapshot(targetDebugResult?.InputSnapshot)
            ?? TryGetNearestImageAcquisitionOutputBytes(flow, targetNodeId, result)
            ?? externalInputImage;
    }

    private static byte[]? TryGetNearestImageAcquisitionOutputBytes(
        OperatorFlow flow,
        Guid targetNodeId,
        FlowDebugExecutionResult result)
    {
        var relevantIds = CollectRelevantOperatorIds(flow, targetNodeId);
        var imageAcquisitionIds = flow.Operators
            .Where(item => relevantIds.Contains(item.Id) && item.Type == OperatorType.ImageAcquisition)
            .Select(item => item.Id)
            .ToHashSet();

        if (imageAcquisitionIds.Count == 0)
        {
            return null;
        }

        foreach (var debugResult in result.DebugOperatorResults
                     .Where(item => imageAcquisitionIds.Contains(item.OperatorId))
                     .OrderByDescending(item => item.ExecutionOrder))
        {
            var bytes = TryGetImageBytesFromSnapshot(debugResult.OutputSnapshot)
                ?? TryGetImageBytesFromSnapshot(debugResult.InputSnapshot);
            if (bytes != null && bytes.Length > 0)
            {
                return bytes;
            }
        }

        foreach (var operatorId in imageAcquisitionIds)
        {
            if (result.IntermediateResults.TryGetValue(operatorId, out var outputData))
            {
                var bytes = TryGetOutputImageBytes(outputData);
                if (bytes != null && bytes.Length > 0)
                {
                    return bytes;
                }
            }
        }

        return null;
    }
}
