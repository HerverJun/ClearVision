// PreviewNodeEndpoints.cs
// 预览工作流中指定节点的输出
// 【Phase 3】复用调试缓存机制，执行上游子图到目标节点
// 作者：架构修复方案 v2

using System.Collections;
using System.Text.Json;
using ClearVision.Product.Application.DTOs;
using ClearVision.Product.Application.Services;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Interfaces;
using ClearVision.Product.Core.ProjectVariables;
using ClearVision.Product.Core.Services;
using ClearVision.Product.Core.ValueObjects;
using ClearVision.Product.Infrastructure.Operators;
using ClearVision.Product.Infrastructure.Services;
using ClearVision.Product.Desktop.Configuration;
using ClearVision.Product.Desktop.Observation;
using ClearVision.Product.Desktop.PreviewArtifacts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenCvSharp;

namespace ClearVision.Product.Desktop.Endpoints;

/// <summary>
/// 节点预览端点
/// </summary>
public static class PreviewNodeEndpoints
{
    private const int DefaultPreviewTimeoutMs = 30_000;
    private const int MinPreviewTimeoutMs = 1_000;
    private const int MaxPreviewTimeoutMs = 120_000;
    private const int MaxPreviewImageBytes = 8 * 1024 * 1024;
    private const int MaxMetricsItems = 64;
    private const int MaxMetricsStringChars = 1024;

    private static readonly string[] MetricsCountKeys =
    [
        "BlobCount", "blobCount", "DefectCount", "defectCount", "DetectionCount", "detectionCount", "ObjectCount", "objectCount"
    ];

    private static readonly string[] MetricsCollectionKeys =
    [
        "SortedDetections", "sortedDetections", "DetectionList", "detectionList", "Detections", "detections",
        "Objects", "objects", "Defects", "defects", "Blobs", "blobs"
    ];

    private static readonly string[] MetricsStringListKeys =
    [
        "ExpectedLabels", "ExpectedOrder", "ActualOrder", "SortedLabels", "MissingLabels", "DuplicateLabels"
    ];

    private static readonly string[] MetricsNumericConfigKeys =
    [
        "ConfiguredMinConfidence", "RequiredMinConfidence", "MinRequiredConfidence", "ExpectedCount", "TargetCount"
    ];

    private static readonly HashSet<string> MetricsDetectionFieldKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "Label", "ClassName", "Confidence", "Score", "X", "Left", "Y", "Top", "Width", "Height", "Area"
    };

    private static readonly JsonSerializerOptions FlowJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    /// <summary>
    /// 映射节点预览端点
    /// </summary>
    public static IEndpointRouteBuilder MapPreviewNodeEndpoints(this IEndpointRouteBuilder app)
    {
        // 【Phase 3】预览工作流中指定节点的输出
        app.MapPost("/api/flows/preview-node", async (
            PreviewNodeRequest request,
            HttpContext context,
            IFlowExecutionService flowService,
            IProjectRepository projectRepository,
            IProjectFlowStorage flowStorage,
            ProjectVariableSessionRegistry projectVariableSessions,
            IServiceProvider serviceProvider,
            PreviewArtifactMaterializer artifactMaterializer,
            IOptions<StudioOptions> studioOptions,
            ILogger<object> logger) =>
        {
            ProjectAccessLease? projectAccess = null;
            try
            {
                logger.LogInformation(
                    "[PreviewNode] 请求预览节点: Project={ProjectId}, Node={NodeId}, Session={DebugSessionId}",
                    request.ProjectId, request.TargetNodeId, request.DebugSessionId);

                var identityValidationProblem = ValidateObservationIdentity(request);
                if (identityValidationProblem != null)
                {
                    return identityValidationProblem;
                }

                var useArtifactReferences = UsesPreviewArtifactReferences(request);
                var artifactOwner = CreateArtifactOwner(request);

                if (request.ProjectId != Guid.Empty)
                {
                    var projectSaveCoordinator = serviceProvider.GetService<ProjectSaveCoordinator>();
                    if (projectSaveCoordinator != null)
                    {
                        projectAccess = await projectSaveCoordinator.AcquireProjectAccessAsync(
                            request.ProjectId,
                            context.RequestAborted);
                    }
                }

                // 从数据库加载流程，或直接使用前端传来的流程数据
                ClearVision.Product.Core.Entities.OperatorFlow? flow;

                if (request.FlowData?.Operators?.Count > 0)
                {
                    // 使用前端传来的流程数据
                    flow = FlowEntityMapper.ToPreviewEntity(request.FlowData, request.TargetNodeId, "PreviewFlow");
                }
                else
                {
                    // 从数据库加载
                    flow = await ResolveStoredProjectFlowAsync(request.ProjectId, projectRepository, flowStorage);
                }

                if (flow == null)
                {
                    return Results.Problem(
                        detail: "无法获取流程数据",
                        statusCode: 400);
                }

                // 应用参数覆盖（如果有）
                if (request.Parameters != null && request.Parameters.Count > 0)
                {
                    var targetOp = flow.Operators.FirstOrDefault(o => o.Id == request.TargetNodeId);
                    if (targetOp != null)
                    {
                        foreach (var param in request.Parameters)
                        {
                            var existingParam = targetOp.Parameters.FirstOrDefault(p => p.Name == param.Key);
                            if (existingParam != null)
                            {
                                existingParam.SetValue(param.Value);
                            }
                        }
                    }
                }

                // 构建调试选项
                var debugOptions = new DebugOptions
                {
                    DebugSessionId = request.DebugSessionId,
                    EnableIntermediateCache = true,
                    BreakAtOperatorId = request.TargetNodeId,  // 【Phase 3】执行到目标节点后停止
                    ImageFormat = request.ImageFormat ?? ".png"
                };

                // 准备输入数据
                Dictionary<string, object>? inputData = null;
                var externalInputImageBase64 = ShouldUseExternalInputImage(flow, request.TargetNodeId)
                    ? request.InputImageBase64
                    : null;
                if (!string.IsNullOrWhiteSpace(externalInputImageBase64))
                {
                    if (!ImagePayloadDecoder.TryDecodeBytes(externalInputImageBase64, "InputImageBase64", out var imageData, out var decodeError, out var statusCode))
                    {
                        return ImagePayloadDecoder.ToErrorResult(decodeError, statusCode);
                    }

                    inputData = new Dictionary<string, object>
                    {
                        ["Image"] = imageData
                    };
                }

                var timeoutMs = NormalizePreviewTimeoutMs(request.TimeoutMs);
                using var previewCancellation = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
                previewCancellation.CancelAfter(TimeSpan.FromMilliseconds(timeoutMs));
                var projectVariables = await CreatePreviewProjectVariableContextAsync(
                    request.ProjectId,
                    flow,
                    projectRepository,
                    projectVariableSessions);
                if (projectAccess != null)
                {
                    await projectAccess.DisposeAsync();
                    projectAccess = null;
                }

                // 执行调试流程（自动执行上游子图到目标节点）
                var result = projectVariables == null
                    ? await flowService.ExecuteFlowDebugAsync(
                        flow,
                        debugOptions,
                        inputData,
                        previewCancellation.Token)
                    : await flowService.ExecuteFlowDebugAsync(
                        flow,
                        debugOptions,
                        inputData,
                        projectVariables,
                        previewCancellation.Token);

                // 获取目标节点的输出
                if (!result.IntermediateResults.TryGetValue(request.TargetNodeId, out var nodeOutput))
                {
                    // 如果中间结果中没有，尝试从缓存获取
                    nodeOutput = flowService.GetDebugIntermediateResult(request.DebugSessionId, request.TargetNodeId);
                }

                if (nodeOutput == null)
                {
                    return Results.Ok(BuildFailureResponse(
                        request,
                        flow,
                        result,
                        request.TargetNodeId,
                        externalInputImageBase64,
                        artifactMaterializer,
                        artifactOwner,
                        useArtifactReferences,
                        studioOptions.Value,
                        previewCancellation.Token,
                        logger));
                }

                // 提取输出图像
                var rawOutputImageBytes = TryGetOutputImageBytes(nodeOutput);
                var sanitizedOutputData = BuildResponseOutputData(nodeOutput);
                byte[]? outputImageBytes;
                string? outputImageBase64;
                string? inputImageBase64;
                List<PreviewArtifactReferenceV1>? artifacts = null;
                Dictionary<string, object> observationOutputData = nodeOutput;
                PreviewArtifactMaterializationResult? materialization = null;

                if (useArtifactReferences)
                {
                    outputImageBytes = rawOutputImageBytes;
                    outputImageBase64 = null;
                }
                else
                {
                    outputImageBytes = LimitPreviewImageBytes(
                        rawOutputImageBytes,
                        sanitizedOutputData,
                        "输出图像",
                        logger);
                    outputImageBase64 = outputImageBytes != null
                        ? Convert.ToBase64String(outputImageBytes)
                        : null;
                }
                var metricsInput = BuildMetricsOutputData(nodeOutput);
                var metrics = BuildPreviewMetrics(metricsInput.OutputData, outputImageBytes, result.ErrorMessage, metricsInput.Diagnostics);

                logger.LogInformation(
                    "[PreviewNode] 预览完成: Project={ProjectId}, Node={NodeId}, Success={Success}",
                    request.ProjectId, request.TargetNodeId, result.IsSuccess);

                var targetDebugResult = result.DebugOperatorResults.FirstOrDefault(r => r.OperatorId == request.TargetNodeId);
                var rawInputImageBytes = ResolveInputImageBytes(flow, request.TargetNodeId, result, targetDebugResult, externalInputImageBase64);
                try
                {
                if (useArtifactReferences)
                {
                    materialization = artifactMaterializer.MaterializePreview(
                        artifactOwner,
                        nodeOutput,
                        rawInputImageBytes,
                        rawOutputImageBytes,
                        previewCancellation.Token);
                    artifacts = materialization.Artifacts.Count > 0 ? materialization.Artifacts : null;
                    observationOutputData = materialization.OutputData;
                    sanitizedOutputData = BuildResponseOutputData(materialization.OutputData);
                    AppendPreviewArtifactDiagnostics(sanitizedOutputData, materialization.Diagnostics);
                    inputImageBase64 = null;
                }
                else
                {
                    var inputImageBytes = LimitPreviewImageBytes(
                        rawInputImageBytes,
                        sanitizedOutputData,
                        "输入图像",
                        logger);
                    inputImageBase64 = inputImageBytes != null ? Convert.ToBase64String(inputImageBytes) : null;
                }

                var failedOperator = FindFailedOperator(result, request.TargetNodeId);
                var observation = BuildPreviewObservation(
                    request,
                    result,
                    observationOutputData,
                    flow,
                    failedOperator,
                    studioOptions.Value);

                var response = new PreviewNodeResponse
                {
                    Success = result.IsSuccess,
                    ProjectId = request.ProjectId,
                    TargetNodeId = request.TargetNodeId,
                    DebugSessionId = request.DebugSessionId,
                    InputImageBase64 = inputImageBase64,
                    OutputData = sanitizedOutputData,
                    OutputImageBase64 = outputImageBase64,
                    ExecutionTimeMs = result.ExecutionTimeMs,
                    ErrorMessage = result.ErrorMessage,
                    FailedOperatorId = failedOperator?.OperatorId,
                    FailedOperatorName = failedOperator?.OperatorName,
                    FailedOperatorType = ResolveOperatorTypeName(flow, failedOperator?.OperatorId),
                    Metrics = metrics,
                    Artifacts = artifacts,
                    ExecutedOperators = result.DebugOperatorResults.Select(r => new ExecutedOperatorInfo
                    {
                        OperatorId = r.OperatorId,
                        OperatorName = r.OperatorName,
                        ExecutionOrder = r.ExecutionOrder,
                        ExecutionTimeMs = r.ExecutionTimeMs,
                        IsSuccess = r.IsSuccess
                    }).ToList(),
                    Observation = observation
                };
                previewCancellation.Token.ThrowIfCancellationRequested();
                materialization?.Commit();
                return Results.Ok(response);
                }
                finally
                {
                    materialization?.Dispose();
                }
            }
            catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException) when (!context.RequestAborted.IsCancellationRequested)
            {
                logger.LogWarning(
                    "[PreviewNode] 预览超时: Project={ProjectId}, Node={NodeId}, TimeoutMs={TimeoutMs}",
                    request.ProjectId, request.TargetNodeId, request.TimeoutMs);
                return Results.Problem(
                    detail: "节点预览超时，已取消本次调试执行。",
                    statusCode: StatusCodes.Status504GatewayTimeout);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[PreviewNode] 预览节点失败: {ProjectId}, {NodeId}",
                    request.ProjectId, request.TargetNodeId);
                return Results.Problem(
                    detail: $"预览节点失败: {ex.Message}",
                    statusCode: 500);
            }
            finally
            {
                if (projectAccess != null)
                {
                    await projectAccess.DisposeAsync();
                }
            }
        });

        return app;
    }

    private static async Task<ClearVision.Product.Core.Entities.OperatorFlow?> ResolveStoredProjectFlowAsync(
        Guid projectId,
        IProjectRepository projectRepository,
        IProjectFlowStorage flowStorage)
    {
        var project = await projectRepository.GetWithFlowAsync(projectId);
        if (project == null)
        {
            return null;
        }

        var storedFlow = await LoadFlowFromStorageAsync(projectId, flowStorage);
        if (HasExecutableFlow(storedFlow))
        {
            return storedFlow;
        }

        return HasExecutableFlow(project.Flow) ? project.Flow : null;
    }

    private static async Task<ProjectVariableExecutionContext?> CreatePreviewProjectVariableContextAsync(
        Guid projectId,
        ClearVision.Product.Core.Entities.OperatorFlow flow,
        IProjectRepository projectRepository,
        ProjectVariableSessionRegistry projectVariableSessions)
    {
        if (projectId == Guid.Empty)
        {
            return null;
        }

        var project = await projectRepository.GetByIdAsync(projectId);
        var schema = project?.GlobalVariables;
        if (schema == null ||
            (schema.Variables.Count == 0 && schema.SourceBindings.Count == 0 && schema.TargetBindings.Count == 0))
        {
            return null;
        }

        var previewSchema = CreatePreviewExecutionSchema(schema, flow);
        ProjectGlobalVariableSchemaValidator.ThrowIfInvalid(previewSchema, flow);
        var session = projectVariableSessions.GetOrCreate(projectId, schema);
        return new ProjectVariableExecutionContext(
            session,
            ProjectVariableBindingIndex.Build(previewSchema),
            Guid.NewGuid(),
            isPreview: true);
    }

    private static ProjectGlobalVariableSchema CreatePreviewExecutionSchema(
        ProjectGlobalVariableSchema schema,
        ClearVision.Product.Core.Entities.OperatorFlow previewFlow)
    {
        var previewOperatorIds = previewFlow.Operators
            .Select(@operator => @operator.Id)
            .ToHashSet();

        return new ProjectGlobalVariableSchema
        {
            SchemaVersion = schema.SchemaVersion,
            Variables = schema.Variables,
            SourceBindings = schema.SourceBindings
                .Where(binding => previewOperatorIds.Contains(binding.OperatorId))
                .ToList(),
            TargetBindings = schema.TargetBindings
                .Where(binding => previewOperatorIds.Contains(binding.OperatorId))
                .ToList()
        };
    }

    private static async Task<ClearVision.Product.Core.Entities.OperatorFlow?> LoadFlowFromStorageAsync(
        Guid projectId,
        IProjectFlowStorage flowStorage)
    {
        try
        {
            var flowJson = await flowStorage.LoadFlowJsonAsync(projectId);
            if (string.IsNullOrWhiteSpace(flowJson))
            {
                return null;
            }

            var flowDto = JsonSerializer.Deserialize<OperatorFlowDto>(flowJson, FlowJsonOptions);
            if (flowDto?.Operators?.Count > 0)
            {
                return flowDto.ToEntity();
            }
        }
        catch
        {
            // Fall back to the database snapshot if the stored flow JSON is missing or invalid.
        }

        return null;
    }

    private static bool HasExecutableFlow(ClearVision.Product.Core.Entities.OperatorFlow? flow)
    {
        return flow?.Operators?.Count > 0;
    }

    private static int NormalizePreviewTimeoutMs(int? requestedTimeoutMs)
    {
        var timeoutMs = requestedTimeoutMs ?? DefaultPreviewTimeoutMs;
        return Math.Clamp(timeoutMs, MinPreviewTimeoutMs, MaxPreviewTimeoutMs);
    }

    private static bool UsesPreviewArtifactReferences(PreviewNodeRequest request) =>
        string.Equals(request.ArtifactMode, "references", StringComparison.OrdinalIgnoreCase);

    private static PreviewArtifactOwnerScope CreateArtifactOwner(PreviewNodeRequest request) =>
        new(
            request.ProjectId,
            request.TargetNodeId,
            request.DebugSessionId,
            request.ClientRequestSequence,
            request.FlowRevision);

    private static void AppendPreviewArtifactDiagnostics(
        Dictionary<string, object> outputData,
        IReadOnlyList<string> diagnostics)
    {
        if (diagnostics.Count == 0)
        {
            return;
        }

        outputData["_previewArtifactDiagnostics"] = diagnostics
            .Distinct(StringComparer.Ordinal)
            .Take(MaxMetricsItems)
            .ToList();
    }

    private static byte[]? LimitPreviewImageBytes(
        byte[]? imageBytes,
        Dictionary<string, object> outputData,
        string label,
        ILogger logger)
    {
        if (imageBytes == null || imageBytes.Length <= MaxPreviewImageBytes)
        {
            return imageBytes;
        }

        logger.LogWarning(
            "[PreviewNode] {Label} exceeds preview payload limit. Bytes={Bytes}, Limit={Limit}",
            label,
            imageBytes.Length,
            MaxPreviewImageBytes);
        outputData["_previewWarning"] = $"{label}过大，已省略图像载荷，仅保留结构化摘要。";
        return null;
    }

    private static OperatorDebugResult? FindFailedOperator(
        FlowDebugExecutionResult result,
        Guid targetNodeId)
    {
        var targetResult = result.DebugOperatorResults
            .FirstOrDefault(item => item.OperatorId == targetNodeId);
        return targetResult?.IsSuccess == false
            ? targetResult
            : result.DebugOperatorResults.FirstOrDefault(item => !item.IsSuccess);
    }

    private static string? ResolveOperatorTypeName(
        ClearVision.Product.Core.Entities.OperatorFlow flow,
        Guid? operatorId)
    {
        if (!operatorId.HasValue)
        {
            return null;
        }

        return flow.Operators.FirstOrDefault(item => item.Id == operatorId.Value)?.Type.ToString();
    }

    private static Dictionary<string, object> BuildResponseOutputData(Dictionary<string, object> nodeOutput)
    {
        return ExecutionObservationProjector.BuildLegacyOutputData(nodeOutput, IsInternalPreviewImageKey);
    }

    private static BoundedMetricsInput BuildMetricsOutputData(Dictionary<string, object> nodeOutput)
    {
        var response = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        var diagnostics = new List<string>();

        foreach (var key in MetricsCountKeys.Concat(MetricsNumericConfigKeys))
        {
            string? diagnostic = null;
            if (nodeOutput.TryGetValue(key, out var value) &&
                TryBuildMetricsScalar(value, out var boundedValue, out diagnostic))
            {
                response[key] = boundedValue!;
            }
            else if (diagnostic != null)
            {
                diagnostics.Add(diagnostic);
            }
        }

        foreach (var key in MetricsStringListKeys)
        {
            string? diagnostic = null;
            if (nodeOutput.TryGetValue(key, out var value) &&
                TryBuildMetricsStringList(value, out var boundedValue, out diagnostic))
            {
                response[key] = boundedValue;
            }
            else if (diagnostic != null)
            {
                diagnostics.Add(diagnostic);
            }
        }

        foreach (var key in MetricsCollectionKeys)
        {
            string? diagnostic = null;
            if (nodeOutput.TryGetValue(key, out var value) &&
                TryBuildMetricsCollection(value, out var boundedValue, out diagnostic))
            {
                response[key] = boundedValue!;
            }
            else if (diagnostic != null)
            {
                diagnostics.Add(diagnostic);
            }
        }

        return new BoundedMetricsInput(response, diagnostics.Distinct(StringComparer.Ordinal).Take(MaxMetricsItems).ToList());
    }

    private static bool TryBuildMetricsScalar(object? value, out object? boundedValue, out string? diagnostic)
    {
        diagnostic = null;
        boundedValue = value switch
        {
            null => null,
            int or long or decimal => value,
            float floatValue when float.IsFinite(floatValue) => floatValue,
            double doubleValue when double.IsFinite(doubleValue) => doubleValue,
            string text => ClipMetricsString(text),
            JsonElement element when element.ValueKind is JsonValueKind.Number or JsonValueKind.String => element.Clone(),
            _ => null
        };

        if (boundedValue != null || value == null)
        {
            return true;
        }

        diagnostic = "PreviewMetricsUnsupportedScalar";
        return false;
    }

    private static bool TryBuildMetricsStringList(object? value, out object boundedValue, out string? diagnostic)
    {
        diagnostic = null;
        boundedValue = new List<string>();
        switch (value)
        {
            case null:
                return true;
            case string text:
                boundedValue = ClipMetricsString(text);
                return true;
            case string[] array:
                boundedValue = array
                    .Where(item => !string.IsNullOrWhiteSpace(item))
                    .Take(MaxMetricsItems)
                    .Select(ClipMetricsString)
                    .ToList();
                return true;
            case List<string> list:
                boundedValue = list
                    .Where(item => !string.IsNullOrWhiteSpace(item))
                    .Take(MaxMetricsItems)
                    .Select(ClipMetricsString)
                    .ToList();
                return true;
            case JsonElement element when element.ValueKind == JsonValueKind.Array:
                boundedValue = element
                    .EnumerateArray()
                    .Take(MaxMetricsItems)
                    .Select(item => item.ValueKind == JsonValueKind.String ? item.GetString() : item.GetRawText())
                    .Where(item => !string.IsNullOrWhiteSpace(item))
                    .Select(item => ClipMetricsString(item!))
                    .ToList();
                return true;
            case JsonElement element when element.ValueKind == JsonValueKind.String:
                boundedValue = ClipMetricsString(element.GetString() ?? string.Empty);
                return true;
            default:
                diagnostic = "PreviewMetricsUnsupportedStringList";
                return false;
        }
    }

    private static bool TryBuildMetricsCollection(object? value, out object? boundedValue, out string? diagnostic)
    {
        diagnostic = null;
        boundedValue = null;
        switch (value)
        {
            case null:
                boundedValue = new List<object?>();
                return true;
            case DetectionList detectionList:
                boundedValue = new DetectionList((detectionList.Detections ?? new List<ClearVision.Product.Core.ValueObjects.DetectionResult>())
                    .Take(MaxMetricsItems)
                    .Select(CloneDetection));
                if ((detectionList.Detections?.Count ?? 0) > MaxMetricsItems)
                {
                    diagnostic = "PreviewMetricsCollectionTruncated";
                }
                return true;
            case ClearVision.Product.Core.ValueObjects.DetectionResult[] detections:
                boundedValue = detections.Take(MaxMetricsItems).Select(CloneDetection).ToList();
                if (detections.Length > MaxMetricsItems)
                {
                    diagnostic = "PreviewMetricsCollectionTruncated";
                }
                return true;
            case JsonElement element when element.ValueKind == JsonValueKind.Array:
                boundedValue = BuildMetricsItemsFromJsonArray(element, out diagnostic);
                return true;
            case IDictionary dictionary:
                if (TryBuildMetricsDictionary(dictionary, out var item))
                {
                    boundedValue = new List<object?> { item };
                    return true;
                }

                diagnostic = "PreviewMetricsUnsupportedDictionary";
                return false;
            default:
                if (TryBuildMetricsKnownIndexedCollection(value, out boundedValue, out diagnostic))
                {
                    return true;
                }

                if (value is IEnumerable and not string)
                {
                    diagnostic = "PreviewMetricsUnsupportedEnumerable";
                    return false;
                }

                diagnostic = "PreviewMetricsUnsupportedCollection";
                return false;
        }
    }

    private static bool TryBuildMetricsKnownIndexedCollection(
        object value,
        out object boundedValue,
        out string? diagnostic)
    {
        diagnostic = null;
        boundedValue = new List<object?>();
        int total;
        Func<int, object?> readItem;
        if (value is Array array && array.Rank == 1)
        {
            total = array.Length;
            readItem = index => array.GetValue(index);
        }
        else if (IsKnownGenericList(value.GetType()) && value is IList list)
        {
            total = list.Count;
            readItem = index => list[index];
        }
        else
        {
            return false;
        }

        var result = new List<object?>();
        for (var index = 0; index < Math.Min(total, MaxMetricsItems); index++)
        {
            if (TryBuildMetricsItem(readItem(index), out var item))
            {
                result.Add(item);
            }
        }

        if (total > MaxMetricsItems)
        {
            diagnostic = "PreviewMetricsCollectionTruncated";
        }

        boundedValue = result;
        return true;
    }

    private static List<object?> BuildMetricsItemsFromJsonArray(JsonElement element, out string? diagnostic)
    {
        var result = new List<object?>();
        var count = element.GetArrayLength();
        foreach (var item in element.EnumerateArray().Take(MaxMetricsItems))
        {
            if (TryBuildMetricsItem(item, out var boundedItem))
            {
                result.Add(boundedItem);
            }
        }

        diagnostic = count > MaxMetricsItems ? "PreviewMetricsCollectionTruncated" : null;
        return result;
    }

    private static bool TryBuildMetricsItem(object? value, out object? item)
    {
        switch (value)
        {
            case ClearVision.Product.Core.ValueObjects.DetectionResult detection:
                item = CloneDetection(detection);
                return true;
            case IDictionary dictionary:
                return TryBuildMetricsDictionary(dictionary, out item);
            case JsonElement element when element.ValueKind == JsonValueKind.Object:
                return TryBuildMetricsDictionary(element, out item);
            default:
                item = null;
                return false;
        }
    }

    private static bool TryBuildMetricsDictionary(IDictionary dictionary, out object? item)
    {
        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (DictionaryEntry entry in dictionary)
        {
            if (entry.Key is not string key || !MetricsDetectionFieldKeys.Contains(key))
            {
                continue;
            }

            if (TryBuildMetricsFieldValue(entry.Value, out var value))
            {
                result[key] = value;
            }
        }

        item = result;
        return result.Count > 0;
    }

    private static bool TryBuildMetricsDictionary(JsonElement element, out object? item)
    {
        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in element.EnumerateObject())
        {
            if (!MetricsDetectionFieldKeys.Contains(property.Name))
            {
                continue;
            }

            if (TryBuildMetricsFieldValue(property.Value, out var value))
            {
                result[property.Name] = value;
            }
        }

        item = result;
        return result.Count > 0;
    }

    private static bool TryBuildMetricsFieldValue(object? value, out object? boundedValue)
    {
        switch (value)
        {
            case null:
                boundedValue = null;
                return true;
            case string text:
                boundedValue = ClipMetricsString(text);
                return true;
            case int or long or decimal:
                boundedValue = value;
                return true;
            case float floatValue when float.IsFinite(floatValue):
                boundedValue = floatValue;
                return true;
            case double doubleValue when double.IsFinite(doubleValue):
                boundedValue = doubleValue;
                return true;
            case JsonElement element when element.ValueKind == JsonValueKind.Number:
                boundedValue = element.Clone();
                return true;
            case JsonElement element when element.ValueKind == JsonValueKind.String:
                boundedValue = ClipMetricsString(element.GetString() ?? string.Empty);
                return true;
            default:
                boundedValue = null;
                return false;
        }
    }

    private static ClearVision.Product.Core.ValueObjects.DetectionResult CloneDetection(
        ClearVision.Product.Core.ValueObjects.DetectionResult detection)
    {
        return new ClearVision.Product.Core.ValueObjects.DetectionResult(
            detection.Label,
            detection.Confidence,
            detection.X,
            detection.Y,
            detection.Width,
            detection.Height);
    }

    private static bool IsKnownGenericList(Type type) =>
        type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>);

    private static string ClipMetricsString(string value) =>
        value.Length <= MaxMetricsStringChars ? value : value[..MaxMetricsStringChars] + "...";

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
        return ImagePayloadDecoder.TryDecodeBytes(base64, "ImageBase64", out var imageData, out _, out _)
            ? imageData
            : null;
    }

    private static bool HasUpstreamOperatorType(
        ClearVision.Product.Core.Entities.OperatorFlow flow,
        Guid targetNodeId,
        OperatorType type)
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
                var sourceOperator = flow.Operators.FirstOrDefault(item => item.Id == connection.SourceOperatorId);
                if (sourceOperator == null)
                {
                    continue;
                }

                if (sourceOperator.Type == type)
                {
                    return true;
                }

                stack.Push(sourceOperator.Id);
            }
        }

        return false;
    }

    private static bool IsTargetOperatorType(
        ClearVision.Product.Core.Entities.OperatorFlow flow,
        Guid targetNodeId,
        OperatorType type)
    {
        return flow.Operators.Any(item => item.Id == targetNodeId && item.Type == type);
    }

    private static bool ShouldUseExternalInputImage(
        ClearVision.Product.Core.Entities.OperatorFlow flow,
        Guid targetNodeId)
    {
        return !IsTargetOperatorType(flow, targetNodeId, OperatorType.ImageAcquisition)
            && !HasUpstreamOperatorType(flow, targetNodeId, OperatorType.ImageAcquisition);
    }

    private static string BuildMissingNodeOutputDetail(
        FlowDebugExecutionResult result,
        Guid targetNodeId)
    {
        var targetResult = result.DebugOperatorResults
            .FirstOrDefault(item => item.OperatorId == targetNodeId);

        if (targetResult != null && !targetResult.IsSuccess)
        {
            return $"目标节点 '{targetResult.OperatorName}' 执行失败: {targetResult.ErrorMessage ?? "未知错误"}";
        }

        var failedOperator = result.DebugOperatorResults
            .FirstOrDefault(item => !item.IsSuccess);

        if (failedOperator != null)
        {
            return $"上游或目标节点 '{failedOperator.OperatorName}' 执行失败: {failedOperator.ErrorMessage ?? "未知错误"}";
        }

        if (!string.IsNullOrWhiteSpace(result.ErrorMessage))
        {
            return result.ErrorMessage!;
        }

        return $"无法获取节点 {targetNodeId} 的输出，可能节点执行失败或未被执行";
    }

    private static PreviewNodeResponse BuildFailureResponse(
        PreviewNodeRequest request,
        ClearVision.Product.Core.Entities.OperatorFlow flow,
        FlowDebugExecutionResult result,
        Guid targetNodeId,
        string? externalInputImageBase64,
        PreviewArtifactMaterializer artifactMaterializer,
        PreviewArtifactOwnerScope artifactOwner,
        bool useArtifactReferences,
        StudioOptions studioOptions,
        CancellationToken cancellationToken,
        ILogger logger)
    {
        var targetResult = result.DebugOperatorResults
            .FirstOrDefault(item => item.OperatorId == targetNodeId);
        var failedOperator = targetResult?.IsSuccess == false
            ? targetResult
            : result.DebugOperatorResults.FirstOrDefault(item => !item.IsSuccess);

        var failureMessage = BuildMissingNodeOutputDetail(result, targetNodeId);
        var failureOutputData = targetResult?.OutputSnapshot ?? failedOperator?.OutputSnapshot;
        var outputDataForObservation = failureOutputData;
        var sanitizedOutputData = failureOutputData != null
            ? BuildResponseOutputData(failureOutputData)
            : new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        var rawInputImageBytes = TryGetImageBytesFromSnapshot(targetResult?.InputSnapshot)
            ?? TryGetImageBytesFromSnapshot(failedOperator?.InputSnapshot)
            ?? ResolveInputImageBytes(flow, targetNodeId, result, targetResult, externalInputImageBase64);
        string? inputImageBase64;
        List<PreviewArtifactReferenceV1>? artifacts = null;
        PreviewArtifactMaterializationResult? materialization = null;
        try
        {
        if (useArtifactReferences)
        {
            materialization = artifactMaterializer.MaterializePreview(
                artifactOwner,
                failureOutputData,
                rawInputImageBytes,
                TryGetImageBytesFromSnapshot(failureOutputData),
                cancellationToken);
            artifacts = materialization.Artifacts.Count > 0 ? materialization.Artifacts : null;
            outputDataForObservation = materialization.OutputData;
            sanitizedOutputData = BuildResponseOutputData(materialization.OutputData);
            AppendPreviewArtifactDiagnostics(sanitizedOutputData, materialization.Diagnostics);
            inputImageBase64 = null;
        }
        else
        {
            var inputImageBytes = LimitPreviewImageBytes(
                rawInputImageBytes,
                sanitizedOutputData,
                "输入图像",
                logger);
            inputImageBase64 = inputImageBytes != null ? Convert.ToBase64String(inputImageBytes) : null;
        }

        var response = new PreviewNodeResponse
        {
            Success = false,
            ProjectId = request.ProjectId,
            TargetNodeId = request.TargetNodeId,
            DebugSessionId = request.DebugSessionId,
            InputImageBase64 = inputImageBase64,
            OutputData = sanitizedOutputData.Count > 0 ? sanitizedOutputData : null,
            OutputImageBase64 = null,
            ExecutionTimeMs = result.ExecutionTimeMs,
            ErrorMessage = failureMessage,
            FailedOperatorId = failedOperator?.OperatorId,
            FailedOperatorName = failedOperator?.OperatorName,
            FailedOperatorType = ResolveOperatorTypeName(flow, failedOperator?.OperatorId),
            Metrics = null,
            Artifacts = artifacts,
            ExecutedOperators = result.DebugOperatorResults.Select(r => new ExecutedOperatorInfo
            {
                OperatorId = r.OperatorId,
                OperatorName = r.OperatorName,
                ExecutionOrder = r.ExecutionOrder,
                ExecutionTimeMs = r.ExecutionTimeMs,
                IsSuccess = r.IsSuccess
            }).ToList(),
            Observation = BuildPreviewObservation(
                request,
                result,
                outputDataForObservation,
                flow,
                failedOperator,
                studioOptions,
                successOverride: false,
                errorMessageOverride: failureMessage)
        };
        cancellationToken.ThrowIfCancellationRequested();
        materialization?.Commit();
        return response;
        }
        finally
        {
            materialization?.Dispose();
        }
    }

    private static IResult? ValidateObservationIdentity(PreviewNodeRequest request)
    {
        if (!ExecutionObservationProjector.IsIdentityValueInSafeRange(request.ClientRequestSequence))
        {
            return Results.Problem(
                detail: "clientRequestSequence must be a non-negative JavaScript-safe integer.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        if (!ExecutionObservationProjector.IsIdentityValueInSafeRange(request.FlowRevision))
        {
            return Results.Problem(
                detail: "flowRevision must be a non-negative JavaScript-safe integer.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        return null;
    }

    private static ExecutionObservationEnvelopeV1 BuildPreviewObservation(
        PreviewNodeRequest request,
        FlowDebugExecutionResult result,
        IReadOnlyDictionary<string, object>? outputData,
        ClearVision.Product.Core.Entities.OperatorFlow flow,
        OperatorDebugResult? failedOperator,
        StudioOptions studioOptions,
        bool? successOverride = null,
        string? errorMessageOverride = null)
    {
        var targetOperator = flow.Operators.FirstOrDefault(item => item.Id == request.TargetNodeId);
        return ExecutionObservationProjector.CreatePreviewObservation(new ExecutionObservationPreviewInput
        {
            ProjectId = request.ProjectId,
            TargetNodeId = request.TargetNodeId,
            DebugSessionId = request.DebugSessionId,
            ClientRequestSequence = request.ClientRequestSequence,
            FlowRevision = request.FlowRevision,
            Success = successOverride ?? result.IsSuccess,
            ExecutionTimeMs = result.ExecutionTimeMs,
            ErrorMessage = errorMessageOverride ?? result.ErrorMessage,
            FailedOperatorId = failedOperator?.OperatorId,
            FailedOperatorName = failedOperator?.OperatorName,
            FailedOperatorType = ResolveOperatorTypeName(flow, failedOperator?.OperatorId),
            ExecutedOperatorCount = result.DebugOperatorResults.Count,
            OutputData = outputData,
            OutputPorts = targetOperator?
                .OutputPorts
                .Select(port => new ExecutionObservationOutputPortV1
                {
                    Id = port.Id,
                    Name = port.Name
                })
                .ToList() ?? [],
            TargetOperator = targetOperator,
            FeatureFlags = BuildObservationFeatureFlags(studioOptions)
        });
    }

    private static IReadOnlyDictionary<string, bool> BuildObservationFeatureFlags(StudioOptions studioOptions) =>
        new Dictionary<string, bool>(StringComparer.Ordinal)
        {
            ["Studio:CircleSearchV2ToolEnabled"] = studioOptions.CircleSearchV2ToolEnabled,
            ["Studio:NPointCalibrationWorkbenchEnabled"] = studioOptions.NPointCalibrationWorkbenchEnabled
        };

    private static byte[]? ResolveInputImageBytes(
        ClearVision.Product.Core.Entities.OperatorFlow flow,
        Guid targetNodeId,
        FlowDebugExecutionResult result,
        OperatorDebugResult? targetDebugResult,
        string? requestInputImageBase64)
    {
        return TryGetImageBytesFromSnapshot(targetDebugResult?.InputSnapshot)
            ?? TryGetNearestImageAcquisitionOutputBytes(flow, targetNodeId, result)
            ?? TryDecodeBase64(requestInputImageBase64);
    }

    private static byte[]? TryGetNearestImageAcquisitionOutputBytes(
        ClearVision.Product.Core.Entities.OperatorFlow flow,
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

    private static HashSet<Guid> CollectRelevantOperatorIds(
        ClearVision.Product.Core.Entities.OperatorFlow flow,
        Guid targetNodeId)
    {
        var visited = new HashSet<Guid> { targetNodeId };
        var stack = new Stack<Guid>();
        stack.Push(targetNodeId);

        while (stack.Count > 0)
        {
            var current = stack.Pop();
            foreach (var connection in flow.Connections.Where(item => item.TargetOperatorId == current))
            {
                if (visited.Add(connection.SourceOperatorId))
                {
                    stack.Push(connection.SourceOperatorId);
                }
            }
        }

        return visited;
    }

    private static PreviewFeedbackMetrics BuildPreviewMetrics(
        Dictionary<string, object> outputData,
        byte[]? outputImageBytes,
        string? errorMessage,
        IReadOnlyList<string> inputDiagnostics)
    {
        try
        {
            var detectionSummary = DetectionOutputInspector.Inspect(outputData);
            var areas = ExtractBlobAreas(outputData);
            if (areas.Count == 0 && detectionSummary.Detections.Count > 0)
            {
                areas = detectionSummary.Detections
                    .Select(detection => (double)detection.Area)
                    .ToList();
            }

            return new PreviewFeedbackMetrics
            {
                BlobCount = ResolveBlobCount(outputData, areas.Count, detectionSummary),
                AreaStats = areas.Count == 0
                    ? null
                    : new PreviewAreaStats
                    {
                        Min = areas.Min(),
                        Max = areas.Max(),
                        Mean = areas.Average()
                    },
                DetectionCount = detectionSummary.HasDetectionSemantics ? detectionSummary.DetectionCount : null,
                ObjectCount = detectionSummary.DeclaredCount ?? (detectionSummary.HasDetectionSemantics ? detectionSummary.DetectionCount : null),
                PerClassCount = detectionSummary.PerClassCount.Count > 0 ? detectionSummary.PerClassCount : null,
                SortedLabels = detectionSummary.ActualOrder.Count > 0 ? detectionSummary.ActualOrder : null,
                MinConfidence = detectionSummary.MinConfidence,
                MissingLabels = detectionSummary.MissingLabels.Count > 0 ? detectionSummary.MissingLabels : null,
                DuplicateLabels = detectionSummary.DuplicateLabels.Count > 0 ? detectionSummary.DuplicateLabels : null,
                Diagnostics = CreateDetectionDiagnostics(detectionSummary, inputDiagnostics),
                BinaryRatio = ComputeBinaryRatio(outputImageBytes),
                ErrorMessage = errorMessage
            };
        }
        catch (Exception ex)
        {
            return new PreviewFeedbackMetrics
            {
                BlobCount = 0,
                BinaryRatio = ComputeBinaryRatio(outputImageBytes),
                ErrorMessage = errorMessage,
                Diagnostics = inputDiagnostics
                    .Concat(new[] { $"PreviewMetricsUnavailable: {ex.GetBaseException().Message}" })
                    .Take(MaxMetricsItems)
                    .ToList()
            };
        }
    }

    private static int ResolveBlobCount(
        Dictionary<string, object> outputData,
        int fallbackCount,
        DetectionOutputSummary detectionSummary)
    {
        foreach (var key in new[] { "BlobCount", "blobCount", "DefectCount", "defectCount", "DetectionCount", "detectionCount", "ObjectCount", "objectCount" })
        {
            if (outputData.TryGetValue(key, out var value) && TryReadInt(value, out var count))
            {
                return count;
            }
        }

        foreach (var key in new[] { "DetectionList", "detectionList", "Objects", "objects", "Defects", "defects", "Blobs", "blobs" })
        {
            if (outputData.TryGetValue(key, out var value) && TryGetCollectionCount(value, out var count))
            {
                return count;
            }
        }

        if (detectionSummary.HasDetectionSemantics)
        {
            return detectionSummary.DetectionCount;
        }

        return fallbackCount;
    }

    private static List<double> ExtractBlobAreas(Dictionary<string, object> outputData)
    {
        foreach (var key in new[] { "Defects", "defects", "Blobs", "blobs" })
        {
            if (!outputData.TryGetValue(key, out var value) || value == null)
            {
                continue;
            }

            var areas = new List<double>();
            foreach (var item in EnumerateItems(value))
            {
                if (TryReadArea(item, out var area))
                {
                    areas.Add(area);
                }
            }

            if (areas.Count > 0)
            {
                return areas;
            }
        }

        return new List<double>();
    }

    private static IEnumerable<object?> EnumerateItems(object value)
    {
        if (value is JsonElement element && element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                yield return item;
            }

            yield break;
        }

        if (value is Array array && array.Rank == 1)
        {
            for (var index = 0; index < Math.Min(array.Length, MaxMetricsItems); index++)
            {
                yield return array.GetValue(index);
            }

            yield break;
        }

        if (IsKnownGenericList(value.GetType()) && value is IList list)
        {
            for (var index = 0; index < Math.Min(list.Count, MaxMetricsItems); index++)
            {
                yield return list[index];
            }
        }
    }

    private static bool TryReadArea(object? item, out double area)
    {
        if (TryReadDoubleField(item, "Area", out area) || TryReadDoubleField(item, "area", out area))
        {
            return true;
        }

        if ((TryReadDoubleField(item, "Width", out var width) || TryReadDoubleField(item, "width", out width)) &&
            (TryReadDoubleField(item, "Height", out var height) || TryReadDoubleField(item, "height", out height)))
        {
            area = width * height;
            return true;
        }

        area = 0;
        return false;
    }

    private static bool TryReadDoubleField(object? item, string fieldName, out double value)
    {
        if (item is IDictionary<string, object> typedDictionary &&
            typedDictionary.TryGetValue(fieldName, out var dictionaryValue))
        {
            return TryReadDouble(dictionaryValue, out value);
        }

        if (item is IDictionary dictionary && dictionary.Contains(fieldName))
        {
            return TryReadDouble(dictionary[fieldName], out value);
        }

        if (item is JsonElement element &&
            element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty(fieldName, out var property))
        {
            return TryReadDouble(property, out value);
        }

        value = 0;
        return false;
    }

    private static bool TryReadInt(object? value, out int number)
    {
        switch (value)
        {
            case int intValue:
                number = intValue;
                return true;
            case long longValue:
                number = (int)longValue;
                return true;
            case double doubleValue:
                number = (int)doubleValue;
                return true;
            case JsonElement element when element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out var jsonInt):
                number = jsonInt;
                return true;
            default:
                number = 0;
                return false;
        }
    }

    private static bool TryReadDouble(object? value, out double number)
    {
        switch (value)
        {
            case double doubleValue:
                number = doubleValue;
                return true;
            case float floatValue:
                number = floatValue;
                return true;
            case int intValue:
                number = intValue;
                return true;
            case long longValue:
                number = longValue;
                return true;
            case decimal decimalValue:
                number = (double)decimalValue;
                return true;
            case JsonElement element when element.ValueKind == JsonValueKind.Number && element.TryGetDouble(out var jsonDouble):
                number = jsonDouble;
                return true;
            default:
                number = 0;
                return false;
        }
    }

    private static bool TryGetCollectionCount(object? value, out int count)
    {
        if (value == null)
        {
            count = 0;
            return false;
        }

        if (value is DetectionList detectionList)
        {
            count = detectionList.Count;
            return true;
        }

        if (value is JsonElement element && element.ValueKind == JsonValueKind.Array)
        {
            count = element.GetArrayLength();
            return true;
        }

        if (value is Array array && array.Rank == 1)
        {
            count = Math.Min(array.Length, MaxMetricsItems);
            return true;
        }

        if (IsKnownGenericList(value.GetType()) && value is IList list)
        {
            count = Math.Min(list.Count, MaxMetricsItems);
            return true;
        }

        count = 0;
        return false;
    }

    private static List<string>? CreateDetectionDiagnostics(
        DetectionOutputSummary detectionSummary,
        IReadOnlyList<string> inputDiagnostics)
    {
        var diagnostics = new List<string>();
        diagnostics.AddRange(inputDiagnostics);
        if (!detectionSummary.HasDetectionSemantics)
        {
            return diagnostics.Count > 0 ? diagnostics : null;
        }

        var expectedCount = detectionSummary.ExpectedCount ?? detectionSummary.ExpectedLabels.Count;

        if (detectionSummary.MissingLabels.Count > 0)
        {
            diagnostics.Add(PreviewDiagnosticTags.MissingExpectedClass);
        }

        if (detectionSummary.DuplicateLabels.Count > 0)
        {
            diagnostics.Add(PreviewDiagnosticTags.DuplicateDetectedClass);
        }

        if (expectedCount > 0 && detectionSummary.DetectionCount != expectedCount)
        {
            diagnostics.Add(PreviewDiagnosticTags.DetectionCountMismatch);
        }

        if (detectionSummary.MinConfidence.HasValue &&
            detectionSummary.MinConfidence.Value < detectionSummary.RequiredMinConfidence)
        {
            diagnostics.Add(PreviewDiagnosticTags.LowDetectionConfidence);
        }

        if (detectionSummary.ExpectedLabels.Count > 0 &&
            !detectionSummary.ExpectedLabels.SequenceEqual(detectionSummary.ActualOrder, StringComparer.OrdinalIgnoreCase))
        {
            diagnostics.Add(PreviewDiagnosticTags.OrderMismatch);
        }

        return diagnostics.Count > 0
            ? diagnostics.Distinct(StringComparer.Ordinal).Take(MaxMetricsItems).ToList()
            : null;
    }

    private sealed record BoundedMetricsInput(
        Dictionary<string, object> OutputData,
        List<string> Diagnostics);

    private static double ComputeBinaryRatio(byte[]? outputImageBytes)
    {
        if (outputImageBytes == null || outputImageBytes.Length == 0)
        {
            return 0;
        }

        try
        {
            using var decoded = Cv2.ImDecode(outputImageBytes, ImreadModes.Unchanged);
            if (decoded.Empty())
            {
                return 0;
            }

            using var grayscale = decoded.Channels() == 1
                ? decoded.Clone()
                : decoded.CvtColor(ColorConversionCodes.BGR2GRAY);

            var nonZero = Cv2.CountNonZero(grayscale);
            return Math.Round(nonZero / (double)(grayscale.Rows * grayscale.Cols), 4);
        }
        catch
        {
            return 0;
        }
    }
}

public class PreviewFeedbackMetrics
{
    public int BlobCount { get; set; }
    public PreviewAreaStats? AreaStats { get; set; }
    public int? DetectionCount { get; set; }
    public int? ObjectCount { get; set; }
    public Dictionary<string, int>? PerClassCount { get; set; }
    public List<string>? SortedLabels { get; set; }
    public double? MinConfidence { get; set; }
    public List<string>? MissingLabels { get; set; }
    public List<string>? DuplicateLabels { get; set; }
    public List<string>? Diagnostics { get; set; }
    public double BinaryRatio { get; set; }
    public string? ErrorMessage { get; set; }
}

public class PreviewAreaStats
{
    public double Min { get; set; }
    public double Max { get; set; }
    public double Mean { get; set; }
}

/// <summary>
/// 预览节点请求
/// </summary>
public class PreviewNodeRequest
{
    /// <summary>
    /// 项目ID
    /// </summary>
    public Guid ProjectId { get; set; }

    /// <summary>
    /// 目标节点ID（要预览的算子）
    /// </summary>
    public Guid TargetNodeId { get; set; }

    /// <summary>
    /// 调试会话ID（用于缓存复用）
    /// </summary>
    public Guid DebugSessionId { get; set; } = Guid.NewGuid();

    /// <summary>
    /// 客户端本地请求序列；服务端仅校验并在 Observation identity 中回显。
    /// </summary>
    public long? ClientRequestSequence { get; set; }

    /// <summary>
    /// 客户端本地 flow revision；不是后端执行版本，服务端仅校验并回显。
    /// </summary>
    public long? FlowRevision { get; set; }

    /// <summary>
    /// 流程数据（包含所有算子和连接）
    /// </summary>
    public UpdateFlowRequest? FlowData { get; set; }

    /// <summary>
    /// 输入图像（Base64），可选
    /// </summary>
    public string? InputImageBase64 { get; set; }

    /// <summary>
    /// 目标节点的新参数（覆盖原参数）
    /// </summary>
    public Dictionary<string, object>? Parameters { get; set; }

    /// <summary>
    /// 输出图像格式，默认 .png
    /// </summary>
    public string? ImageFormat { get; set; }

    /// <summary>
    /// 预览超时（毫秒）
    /// </summary>
    public int? TimeoutMs { get; set; }

    /// <summary>
    /// Artifact transport mode. Null preserves legacy Base64 compatibility; references returns artifact refs.
    /// </summary>
    public string? ArtifactMode { get; set; }
}

/// <summary>
/// 预览节点响应
/// </summary>
public class PreviewNodeResponse
{
    /// <summary>
    /// 是否成功
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// 项目ID
    /// </summary>
    public Guid ProjectId { get; set; }

    /// <summary>
    /// 目标节点ID
    /// </summary>
    public Guid TargetNodeId { get; set; }

    /// <summary>
    /// 调试会话ID
    /// </summary>
    public Guid DebugSessionId { get; set; }

    /// <summary>
    /// 输入图像（Base64），用于失败态下回显已到达目标节点的输入
    /// </summary>
    public string? InputImageBase64 { get; set; }

    /// <summary>
    /// 节点输出数据
    /// </summary>
    public Dictionary<string, object>? OutputData { get; set; }

    /// <summary>
    /// 输出图像（Base64）
    /// </summary>
    public string? OutputImageBase64 { get; set; }

    /// <summary>
    /// 执行时间（毫秒）
    /// </summary>
    public long ExecutionTimeMs { get; set; }

    /// <summary>
    /// 错误信息
    /// </summary>
    public string? ErrorMessage { get; set; }
    public Guid? FailedOperatorId { get; set; }
    public string? FailedOperatorName { get; set; }
    public string? FailedOperatorType { get; set; }
    public PreviewFeedbackMetrics? Metrics { get; set; }

    /// <summary>
    /// Bounded preview artifacts returned when ArtifactMode is references.
    /// </summary>
    public List<PreviewArtifactReferenceV1>? Artifacts { get; set; }

    /// <summary>
    /// 执行的算子列表（上游子图）
    /// </summary>
    public List<ExecutedOperatorInfo>? ExecutedOperators { get; set; }

    /// <summary>
    /// G05A 无持久化、只读、可丢弃的执行观察投影。
    /// </summary>
    public ExecutionObservationEnvelopeV1? Observation { get; set; }
}

/// <summary>
/// 执行的算子信息
/// </summary>
public class ExecutedOperatorInfo
{
    public Guid OperatorId { get; set; }
    public string OperatorName { get; set; } = string.Empty;
    public int ExecutionOrder { get; set; }
    public long ExecutionTimeMs { get; set; }
    public bool IsSuccess { get; set; }
}

/// <summary>
/// 流程数据传输对象（用于前端序列化）
/// </summary>
