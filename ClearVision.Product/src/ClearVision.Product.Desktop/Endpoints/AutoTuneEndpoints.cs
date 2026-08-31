// AutoTuneEndpoints.cs
// 自动调参 API 端点
// 【Phase 4】LLM 闭环验证 - 自动调参端点
// 作者：架构修复方案 v2

using System.Security.Claims;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Interfaces;
using ClearVision.Product.Core.Services;
using ClearVision.Product.Core.ValueObjects;
using ClearVision.Product.Infrastructure.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace ClearVision.Product.Desktop.Endpoints;

/// <summary>
/// 自动调参相关 API 端点
/// </summary>
public static class AutoTuneEndpoints
{
    // StateWrite is permitted only through the isolated preview execution context.
    // Every external capability, including future catalog additions, fails closed.
    private const ExecutionSideEffect AutoTuneAllowedCapabilities = ExecutionSideEffect.StateWrite;

    /// <summary>
    /// 注册自动调参端点
    /// </summary>
    public static IEndpointRouteBuilder MapAutoTuneEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/autotune")
            .WithTags("AutoTune");

        // POST /api/autotune/flow-node/preview - 线序场景专用预览与分析
        group.MapPost("/flow-node/preview", async (
            FlowNodePreviewRequest request,
            HttpContext context,
            IFlowNodePreviewService previewService,
            IExecutionAdmissionService executionAdmissionService,
            IProjectRepository projectRepository,
            AutoTuneExecutionGate executionGate,
            ILogger<AutoTuneService> logger,
            CancellationToken _) =>
        {
            CancellationTokenSource? executionCancellation = null;
            try
            {
                logger.LogInformation(
                    "[AutoTuneAPI] 请求线序预览分析: FlowId={FlowId}, NodeId={NodeId}",
                    request.FlowId, request.TargetNodeId);

                var flow = FlowEntityMapper.ToPreviewEntity(request.FlowData, request.TargetNodeId);
                var admission = ValidateAutoTuneFlow(executionAdmissionService, flow);
                if (!admission.IsAllowed)
                {
                    return ToAdmissionFailure(admission);
                }

                var authorityAdmission = await ValidateAutoTuneAuthorityAsync(
                    request,
                    context,
                    flow,
                    executionAdmissionService,
                    projectRepository,
                    context.RequestAborted);
                if (authorityAdmission.Failure != null)
                {
                    return authorityAdmission.Failure;
                }

                var authority = authorityAdmission.Authority!;

                byte[]? inputImage = null;
                if (!string.IsNullOrWhiteSpace(request.InputImageBase64) &&
                    !ImagePayloadDecoder.TryDecodeBytes(request.InputImageBase64, "InputImageBase64", out inputImage, out var decodeError, out var statusCode))
                {
                    return ImagePayloadDecoder.ToErrorResult(decodeError, statusCode);
                }

                using var executionLease = executionGate.TryAcquire(authority.Principal.SubjectId);
                if (executionLease == null)
                {
                    return ToFailure(StatusCodes.Status429TooManyRequests, "AUTOTUNE_CONCURRENCY_LIMIT_EXCEEDED", "AutoTune execution concurrency limit exceeded.");
                }

                executionCancellation = executionGate.CreateDeadlineSource(context.RequestAborted);
                var result = await previewService.PreviewWithMetricsAsync(
                    flow,
                    request.TargetNodeId,
                    inputImage,
                    request.ProjectId,
                    request.ExpectedProjectRevision!.Value,
                    authority,
                    projectVariables: null,
                    ct: executionCancellation.Token);

                return Results.Ok(MapFlowNodePreviewResponse(result));
            }
            catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
            {
                return Results.StatusCode(499);
            }
            catch (OperationCanceledException) when (executionCancellation?.IsCancellationRequested == true)
            {
                return ToFailure(StatusCodes.Status408RequestTimeout, "AUTOTUNE_DEADLINE_EXCEEDED", "AutoTune execution exceeded its server deadline.");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[AutoTuneAPI] 线序预览分析失败");
                return Results.Problem(ex.Message);
            }
            finally
            {
                executionCancellation?.Dispose();
            }
        })
        .WithName("PreviewFlowNodeWithMetrics")
        .WithDescription("返回线序节点预览图、结构化指标、诊断码、建议和缺失资源")
        .RequireClearVisionPermission(ClearVisionPermissionPolicies.CanEditProject);

        // POST /api/autotune/scenario - 线序场景级自动调参
        group.MapPost("/scenario", async (
            ScenarioAutoTuneRequest request,
            HttpContext context,
            IAutoTuneService autoTuneService,
            IExecutionAdmissionService executionAdmissionService,
            IProjectRepository projectRepository,
            AutoTuneExecutionGate executionGate,
            ILogger<AutoTuneService> logger,
            CancellationToken _) =>
        {
            CancellationTokenSource? executionCancellation = null;
            try
            {
                logger.LogInformation(
                    "[AutoTuneAPI] 请求场景级自动调参: ScenarioKey={ScenarioKey}",
                    request.ScenarioKey);

                if (!AutoTuneExecutionGate.IsIterationCountAllowed(request.MaxIterations))
                {
                    return ToFailure(
                        StatusCodes.Status400BadRequest,
                        "AUTOTUNE_ITERATION_LIMIT_EXCEEDED",
                        $"MaxIterations must be between {AutoTuneExecutionGate.MinimumIterations} and {AutoTuneExecutionGate.MaximumIterations}.");
                }

                var flow = FlowEntityMapper.ToEntity(request.FlowData);
                var admission = ValidateAutoTuneFlow(executionAdmissionService, flow);
                if (!admission.IsAllowed)
                {
                    return ToAdmissionFailure(admission);
                }

                var authorityAdmission = await ValidateAutoTuneAuthorityAsync(
                    request,
                    context,
                    flow,
                    executionAdmissionService,
                    projectRepository,
                    context.RequestAborted);
                if (authorityAdmission.Failure != null)
                {
                    return authorityAdmission.Failure;
                }

                var authority = authorityAdmission.Authority!;

                if (string.IsNullOrWhiteSpace(request.InputImageBase64))
                {
                    return Results.BadRequest(new ScenarioAutoTuneResponse
                    {
                        Success = false,
                        ScenarioKey = request.ScenarioKey,
                        ErrorMessage = "缺少输入图像，无法执行线序场景自动调参。"
                    });
                }

                if (!ImagePayloadDecoder.TryDecodeBytes(request.InputImageBase64, "InputImageBase64", out var inputImage, out var decodeError, out var statusCode))
                {
                    var errorResponse = new ScenarioAutoTuneResponse
                    {
                        Success = false,
                        ScenarioKey = request.ScenarioKey,
                        ErrorMessage = decodeError
                    };
                    return statusCode == StatusCodes.Status413PayloadTooLarge
                        ? Results.Json(errorResponse, statusCode: StatusCodes.Status413PayloadTooLarge)
                        : Results.BadRequest(errorResponse);
                }

                using var executionLease = executionGate.TryAcquire(authority.Principal.SubjectId);
                if (executionLease == null)
                {
                    return ToFailure(StatusCodes.Status429TooManyRequests, "AUTOTUNE_CONCURRENCY_LIMIT_EXCEEDED", "AutoTune execution concurrency limit exceeded.");
                }

                executionCancellation = executionGate.CreateDeadlineSource(context.RequestAborted);
                var result = await autoTuneService.AutoTuneScenarioAsync(
                    request.ScenarioKey,
                    flow,
                    inputImage,
                    request.Goal ?? new AutoTuneGoal(),
                    request.ProjectId,
                    request.ExpectedProjectRevision!.Value,
                    authority,
                    request.MaxIterations,
                    executionCancellation.Token);

                var response = MapScenarioAutoTuneResponse(result);
                return result.Success || result.MissingResources.Count > 0
                    ? Results.Ok(response)
                    : Results.BadRequest(response);
            }
            catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
            {
                return Results.StatusCode(499);
            }
            catch (OperationCanceledException) when (executionCancellation?.IsCancellationRequested == true)
            {
                return ToFailure(StatusCodes.Status408RequestTimeout, "AUTOTUNE_DEADLINE_EXCEEDED", "AutoTune execution exceeded its server deadline.");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[AutoTuneAPI] 场景级自动调参失败");
                return Results.Problem(ex.Message);
            }
            finally
            {
                executionCancellation?.Dispose();
            }
        })
        .WithName("AutoTuneScenario")
        .WithDescription("仅对 wire-sequence-terminal 场景执行白名单参数自动调参")
        .RequireClearVisionPermission(ClearVisionPermissionPolicies.CanEditProject);

        // POST /api/autotune/suggest - 获取参数建议（快速建议，不调参）
        group.MapPost("/suggest", (
            ParameterSuggestionRequest request,
            IPreviewMetricsAnalyzer metricsAnalyzer,
            ILogger<AutoTuneService> logger,
            CancellationToken ct) =>
        {
            try
            {
                logger.LogInformation("[AutoTuneAPI] 请求参数建议: Type={Type}", request.OperatorType);

                // 分析当前指标
                var metrics = metricsAnalyzer.Analyze(
                    request.CurrentOutputData.TryGetValue("Image", out var img) && img is OpenCvSharp.Mat mat ? mat : new OpenCvSharp.Mat(),
                    request.CurrentOutputData,
                    request.Goal);

                var suggestions = metrics.Suggestions.Select(s => new ParameterSuggestionDto
                {
                    ParameterName = s.ParameterName,
                    CurrentValue = s.CurrentValue,
                    SuggestedValue = s.SuggestedValue,
                    Reason = s.Reason,
                    ExpectedImprovement = s.ExpectedImprovement
                }).ToList();

                return Results.Ok(new ParameterSuggestionResponse
                {
                    Success = true,
                    Diagnostics = metrics.Diagnostics,
                    OverallScore = metrics.OverallScore,
                    Suggestions = suggestions,
                    Goals = new OptimizationGoalsDto
                    {
                        CurrentBlobCount = metrics.Goals.CurrentBlobCount,
                        TargetBlobCount = metrics.Goals.TargetBlobCount,
                        CountError = metrics.Goals.CountError,
                        NoisePenalty = metrics.Goals.NoisePenalty,
                        FragmentPenalty = metrics.Goals.FragmentPenalty,
                        AreaDistributionScore = metrics.Goals.AreaDistributionScore,
                        ShapeRegularityScore = metrics.Goals.ShapeRegularityScore
                    }
                });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[AutoTuneAPI] 获取参数建议失败");
                return Results.Problem(ex.Message);
            }
        })
        .WithName("GetParameterSuggestions")
        .WithDescription("基于当前执行结果获取参数调整建议");

        // GET /api/autotune/strategies - 获取支持的调参策略
        group.MapGet("/strategies", () =>
        {
            var strategies = new[]
            {
                new StrategyInfoDto
                {
                    Name = "BinarySearch",
                    DisplayName = "二分搜索",
                    Description = "适用于阈值类参数，快速收敛",
                    SupportedOperators = new[] { "Threshold", "CannyEdge", "HoughLines" }
                },
                new StrategyInfoDto
                {
                    Name = "GradientDescent",
                    DisplayName = "梯度下降",
                    Description = "适用于连续参数，平滑优化",
                    SupportedOperators = new[] { "GaussianBlur", "BilateralFilter" }
                },
                new StrategyInfoDto
                {
                    Name = "Heuristic",
                    DisplayName = "启发式搜索",
                    Description = "基于规则的智能调整，适用性广",
                    SupportedOperators = new[] { "FilterContours", "Morphology", "FindContours", "BlobDetection" }
                },
                new StrategyInfoDto
                {
                    Name = "GridSearch",
                    DisplayName = "网格搜索",
                    Description = "适用于多参数组合优化",
                    SupportedOperators = new[] { "*" }
                }
            };

            return Results.Ok(strategies);
        })
        .WithName("GetTuningStrategies")
        .WithDescription("获取支持的自动调参策略");

        return app;
    }

    private static ExecutionAdmissionResult ValidateAutoTuneFlow(
        IExecutionAdmissionService executionAdmissionService,
        OperatorFlow flow)
    {
        var admission = executionAdmissionService.ValidateFlowDefinition(
            flow,
            ExecutionAdmissionSurface.AutoTunePreview);
        if (!admission.IsAllowed)
        {
            return admission;
        }

        var violations = flow.Operators
            .Where(@operator => @operator.IsEnabled)
            .Select(@operator =>
            {
                var capabilities = ExecutionSideEffectCatalog.GetCapabilities(@operator) & ~AutoTuneAllowedCapabilities;
                return capabilities == ExecutionSideEffect.None
                    ? null
                    : new ExecutionAdmissionViolation(
                        @operator.Id,
                        @operator.Name,
                        @operator.Type,
                        $"{@operator.Type} requires external capability '{capabilities}', which is forbidden for AutoTune Draft execution.",
                        ExecutionSideEffectCatalog.GetCapabilityParameterName(@operator, capabilities));
            })
            .Where(violation => violation != null)
            .Cast<ExecutionAdmissionViolation>()
            .ToList();

        return violations.Count == 0
            ? ExecutionAdmissionResult.Allow()
            : ExecutionAdmissionResult.Reject(
                "ADMISSION_AUTOTUNE_PREVIEW_SIDE_EFFECT_BLOCKED",
                "AutoTune Draft execution cannot access files, networks, databases, PLCs, cameras, serial ports, or other devices.",
                violations);
    }

    private static async Task<(ExecutionRequestAuthority? Authority, IResult? Failure)> ValidateAutoTuneAuthorityAsync(
        AutoTuneDraftAuthorityRequest request,
        HttpContext context,
        OperatorFlow flow,
        IExecutionAdmissionService executionAdmissionService,
        IProjectRepository projectRepository,
        CancellationToken cancellationToken)
    {
        var principal = ResolvePrincipal(context);
        if (principal == null)
        {
            return (null, ToFailure(
                StatusCodes.Status403Forbidden,
                "ADMISSION_PRINCIPAL_REQUIRED",
                "AutoTune Draft execution requires an authenticated principal."));
        }

        if (!principal.IsEngineerOrAdmin || principal.IsSystem)
        {
            return (null, ToFailure(
                StatusCodes.Status403Forbidden,
                "ADMISSION_DRAFT_ROLE_FORBIDDEN",
                "AutoTune Draft execution requires an Engineer or Admin principal."));
        }

        if (request.ProjectId == Guid.Empty ||
            request.ExpectedProjectRevision is not { } expectedRevision || expectedRevision < 0)
        {
            return (null, ToFailure(
                StatusCodes.Status400BadRequest,
                "ADMISSION_DRAFT_REVISION_REQUIRED",
                "AutoTune Draft execution requires a projectId and expectedProjectRevision."));
        }

        if (!TryValidateAuthorityIds(request.ConfirmationId, request.AuditId, out var confirmationId, out var auditId))
        {
            return (null, ToFailure(
                StatusCodes.Status400BadRequest,
                "ADMISSION_DRAFT_CONFIRMATION_REQUIRED",
                "AutoTune Draft execution requires distinct confirmationId and auditId UUIDs."));
        }

        if (request.DeclaredCapabilities is not { } declaredCapabilities)
        {
            return (null, ToFailure(
                StatusCodes.Status400BadRequest,
                "ADMISSION_DRAFT_CAPABILITY_CONFIRMATION_REQUIRED",
                "AutoTune Draft execution requires an explicit declaredCapabilities value."));
        }

        var projectAdmission = await executionAdmissionService.ValidateProjectAsync(
            request.ProjectId,
            ExecutionAdmissionSurface.AutoTunePreview,
            cancellationToken);
        if (!projectAdmission.IsAllowed)
        {
            return (null, ToAdmissionFailure(projectAdmission));
        }

        var project = await projectRepository.GetByIdFreshAsync(request.ProjectId);
        cancellationToken.ThrowIfCancellationRequested();
        if (project == null || project.IsDeleted)
        {
            return (null, ToFailure(
                StatusCodes.Status400BadRequest,
                "ADMISSION_PROJECT_NOT_ACTIVE",
                "The bound AutoTune project does not exist or has been deleted."));
        }

        if (project.PersistenceRevision != expectedRevision)
        {
            return (null, ToFailure(
                StatusCodes.Status409Conflict,
                "ADMISSION_DRAFT_REVISION_STALE",
                $"AutoTune Draft expected project revision {expectedRevision}, but the current revision is {project.PersistenceRevision}."));
        }

        var requiredCapabilities = ExecutionCapabilityManifest.Derive(flow).Capabilities;
        if (declaredCapabilities != requiredCapabilities)
        {
            return (null, ToFailure(
                StatusCodes.Status400BadRequest,
                "ADMISSION_CAPABILITY_MANIFEST_MISMATCH",
                "The declared AutoTune capability manifest does not match the immutable flow."));
        }

        var flowHash = ExecutionFlowIdentity.ComputeFlowHash(flow);
        var authority = new ExecutionRequestAuthority(
            principal,
            expectedRevision,
            new ExecutionCapabilityManifest(declaredCapabilities, isExplicit: true),
            confirmationId,
            auditId,
            new Dictionary<string, string>
            {
                ["ProjectRevision"] = project.PersistenceRevision.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["FlowHash"] = flowHash
            });
        return (authority, null);
    }

    private static ExecutionPrincipal? ResolvePrincipal(HttpContext context)
    {
        var subjectId = context.User.FindFirstValue(ClaimTypes.NameIdentifier)?.Trim();
        var role = EndpointPermissionGuards.GetCurrentRole(context)?.Trim();
        var name = context.User.Identity?.Name?.Trim();
        if (context.User.Identity?.IsAuthenticated != true ||
            string.IsNullOrWhiteSpace(subjectId) ||
            string.IsNullOrWhiteSpace(role))
        {
            return null;
        }

        return new ExecutionPrincipal(
            subjectId,
            string.IsNullOrWhiteSpace(name) ? subjectId : name,
            role,
            IsAuthenticated: true);
    }

    private static bool TryValidateAuthorityIds(
        string? rawConfirmationId,
        string? rawAuditId,
        out string confirmationId,
        out string auditId)
    {
        confirmationId = rawConfirmationId?.Trim() ?? string.Empty;
        auditId = rawAuditId?.Trim() ?? string.Empty;
        return Guid.TryParse(confirmationId, out var parsedConfirmation) && parsedConfirmation != Guid.Empty &&
            Guid.TryParse(auditId, out var parsedAudit) && parsedAudit != Guid.Empty &&
            parsedConfirmation != parsedAudit;
    }

    private static IResult ToAdmissionFailure(ExecutionAdmissionResult admission) =>
        Results.BadRequest(new
        {
            Code = admission.Code,
            Error = admission.Message,
            Violations = admission.Violations
        });

    private static IResult ToFailure(int statusCode, string code, string message) =>
        Results.Json(new { Code = code, Error = message }, statusCode: statusCode);

    #region DTO 映射

    private static PreviewMetricsDto MapMetricsToDto(PreviewMetrics metrics)
    {
        return new PreviewMetricsDto
        {
            MeanIntensity = metrics.ImageStats.MeanIntensity,
            StdDev = metrics.ImageStats.StdDev,
            LaplacianVariance = metrics.ImageStats.LaplacianVariance,
            Histogram = metrics.ImageStats.Histogram,
            BlobCount = metrics.BlobStats.Count,
            Diagnostics = metrics.Diagnostics,
            OverallScore = metrics.OverallScore,
            Continuous = new ContinuousPreviewMetricsDto
            {
                Enabled = metrics.Continuous.Enabled,
                Fps = metrics.Continuous.Fps,
                DroppedFrames = metrics.Continuous.DroppedFrames,
                LatencyMs = metrics.Continuous.LatencyMs,
                QueueDepth = metrics.Continuous.QueueDepth,
                BufferCapacity = metrics.Continuous.BufferCapacity,
                BufferCount = metrics.Continuous.BufferCount,
                BufferOverwrittenCount = metrics.Continuous.BufferOverwrittenCount
            },
            Goals = new OptimizationGoalsDto
            {
                CurrentBlobCount = metrics.Goals.CurrentBlobCount,
                TargetBlobCount = metrics.Goals.TargetBlobCount,
                CountError = metrics.Goals.CountError,
                NoisePenalty = metrics.Goals.NoisePenalty,
                FragmentPenalty = metrics.Goals.FragmentPenalty,
                AreaDistributionScore = metrics.Goals.AreaDistributionScore,
                ShapeRegularityScore = metrics.Goals.ShapeRegularityScore
            }
        };
    }

    private static FlowNodePreviewResponse MapFlowNodePreviewResponse(FlowNodePreviewWithMetricsResult result)
    {
        return new FlowNodePreviewResponse
        {
            Success = result.Success,
            TargetNodeId = result.TargetNodeId,
            InputImageBase64 = EncodeImage(result.InputImage),
            PreviewImageBase64 = EncodeImage(result.PreviewImage),
            Outputs = result.Outputs,
            Metrics = result.Metrics != null ? MapMetricsToDto(result.Metrics) : null,
            DiagnosticCodes = result.DiagnosticCodes,
            Suggestions = result.Suggestions
                .Select(MapParameterSuggestionToDto)
                .ToList(),
            MissingResources = result.MissingResources
                .Select(MapMissingResourceToDto)
                .ToList(),
            ErrorMessage = result.ErrorMessage,
            FailedOperatorId = result.FailedOperatorId,
            FailedOperatorName = result.FailedOperatorName
        };
    }

    private static ScenarioAutoTuneResponse MapScenarioAutoTuneResponse(ScenarioAutoTuneResult result)
    {
        return new ScenarioAutoTuneResponse
        {
            Success = result.Success,
            ScenarioKey = result.ScenarioKey,
            FinalParameters = result.FinalParameters,
            TotalIterations = result.TotalIterations,
            TotalExecutionTimeMs = result.TotalExecutionTimeMs,
            IsGoalAchieved = result.IsGoalAchieved,
            ErrorMessage = result.ErrorMessage,
            Iterations = result.Iterations.Select(item => new AutoTuneIterationDto
            {
                Iteration = item.Iteration,
                Parameters = item.Parameters,
                Score = item.Score,
                ExecutionTimeMs = item.ExecutionTimeMs,
                Metrics = item.Metrics != null ? MapMetricsToDto(item.Metrics) : null
            }).ToList(),
            DiagnosticCodes = result.DiagnosticCodes,
            MissingResources = result.MissingResources
                .Select(MapMissingResourceToDto)
                .ToList(),
            FinalPreview = result.FinalPreview != null
                ? MapFlowNodePreviewResponse(result.FinalPreview)
                : null
        };
    }

    private static ParameterSuggestionDto MapParameterSuggestionToDto(ParameterSuggestion suggestion)
    {
        return new ParameterSuggestionDto
        {
            ParameterName = suggestion.ParameterName,
            CurrentValue = suggestion.CurrentValue,
            SuggestedValue = suggestion.SuggestedValue,
            Reason = suggestion.Reason,
            ExpectedImprovement = suggestion.ExpectedImprovement
        };
    }

    private static PreviewMissingResourceDto MapMissingResourceToDto(PreviewMissingResource resource)
    {
        return new PreviewMissingResourceDto
        {
            ResourceType = resource.ResourceType,
            ResourceKey = resource.ResourceKey,
            Description = resource.Description,
            DiagnosticCode = resource.DiagnosticCode
        };
    }

    private static string? EncodeImage(byte[]? bytes)
    {
        return bytes == null || bytes.Length == 0
            ? null
            : Convert.ToBase64String(bytes);
    }

    #endregion
}

#region 请求/响应 DTOs

public abstract class AutoTuneDraftAuthorityRequest
{
    public Guid ProjectId { get; set; }
    public long? ExpectedProjectRevision { get; set; }
    public ExecutionSideEffect? DeclaredCapabilities { get; set; }
    public string? ConfirmationId { get; set; }
    public string? AuditId { get; set; }
}

/// <summary>
/// 线序节点预览请求
/// </summary>
public class FlowNodePreviewRequest : AutoTuneDraftAuthorityRequest
{
    public Guid FlowId { get; set; }
    public Guid TargetNodeId { get; set; }
    public FlowDataDto FlowData { get; set; } = new();
    public string? InputImageBase64 { get; set; }
    public AutoTuneGoal? Goal { get; set; }
}

/// <summary>
/// 线序场景自动调参请求
/// </summary>
public class ScenarioAutoTuneRequest : AutoTuneDraftAuthorityRequest
{
    public string ScenarioKey { get; set; } = string.Empty;
    public FlowDataDto FlowData { get; set; } = new();
    public string? InputImageBase64 { get; set; }
    public AutoTuneGoal? Goal { get; set; }
    public int MaxIterations { get; set; } = 5;
}

/// <summary>
/// 参数建议请求
/// </summary>
public class ParameterSuggestionRequest
{
    /// <summary>
    /// 算子类型
    /// </summary>
    public OperatorType OperatorType { get; set; }

    /// <summary>
    /// 当前输出数据（用于分析）
    /// </summary>
    public Dictionary<string, object> CurrentOutputData { get; set; } = new();

    /// <summary>
    /// 调参目标
    /// </summary>
    public AutoTuneGoal Goal { get; set; } = new();
}

/// <summary>
/// 线序节点预览响应
/// </summary>
public class FlowNodePreviewResponse
{
    public bool Success { get; set; }
    public Guid TargetNodeId { get; set; }
    public string? InputImageBase64 { get; set; }
    public string? PreviewImageBase64 { get; set; }
    public Dictionary<string, object> Outputs { get; set; } = new();
    public PreviewMetricsDto? Metrics { get; set; }
    public List<string> DiagnosticCodes { get; set; } = new();
    public List<ParameterSuggestionDto> Suggestions { get; set; } = new();
    public List<PreviewMissingResourceDto> MissingResources { get; set; } = new();
    public string? ErrorMessage { get; set; }
    public Guid? FailedOperatorId { get; set; }
    public string? FailedOperatorName { get; set; }
}

/// <summary>
/// 线序场景自动调参响应
/// </summary>
public class ScenarioAutoTuneResponse
{
    public bool Success { get; set; }
    public string ScenarioKey { get; set; } = string.Empty;
    public Dictionary<string, object> FinalParameters { get; set; } = new();
    public int TotalIterations { get; set; }
    public long TotalExecutionTimeMs { get; set; }
    public bool IsGoalAchieved { get; set; }
    public string? ErrorMessage { get; set; }
    public List<AutoTuneIterationDto> Iterations { get; set; } = new();
    public List<string> DiagnosticCodes { get; set; } = new();
    public List<PreviewMissingResourceDto> MissingResources { get; set; } = new();
    public FlowNodePreviewResponse? FinalPreview { get; set; }
}

/// <summary>
/// 自动调参迭代 DTO
/// </summary>
public class AutoTuneIterationDto
{
    /// <summary>
    /// 迭代序号
    /// </summary>
    public int Iteration { get; set; }

    /// <summary>
    /// 本轮参数
    /// </summary>
    public Dictionary<string, object> Parameters { get; set; } = new();

    /// <summary>
    /// 本轮评分
    /// </summary>
    public double Score { get; set; }

    /// <summary>
    /// 执行时间（毫秒）
    /// </summary>
    public long ExecutionTimeMs { get; set; }

    /// <summary>
    /// 本轮指标（可选）
    /// </summary>
    public PreviewMetricsDto? Metrics { get; set; }
}

/// <summary>
/// 预览指标 DTO
/// </summary>
public class PreviewMetricsDto
{
    /// <summary>
    /// 平均亮度
    /// </summary>
    public double MeanIntensity { get; set; }

    /// <summary>
    /// 标准差
    /// </summary>
    public double StdDev { get; set; }

    /// <summary>
    /// 拉普拉斯方差
    /// </summary>
    public double LaplacianVariance { get; set; }

    /// <summary>
    /// 直方图
    /// </summary>
    public int[] Histogram { get; set; } = Array.Empty<int>();

    /// <summary>
    /// Blob 数量
    /// </summary>
    public int BlobCount { get; set; }

    /// <summary>
    /// 诊断标签
    /// </summary>
    public List<string> Diagnostics { get; set; } = new();

    /// <summary>
    /// 综合评分
    /// </summary>
    public double OverallScore { get; set; }

    /// <summary>
    /// 优化目标
    /// </summary>
    public OptimizationGoalsDto Goals { get; set; } = new();

    public ContinuousPreviewMetricsDto Continuous { get; set; } = new();
}

public class ContinuousPreviewMetricsDto
{
    public bool Enabled { get; set; }
    public double? Fps { get; set; }
    public long? DroppedFrames { get; set; }
    public double? LatencyMs { get; set; }
    public int? QueueDepth { get; set; }
    public int? BufferCapacity { get; set; }
    public int? BufferCount { get; set; }
    public long? BufferOverwrittenCount { get; set; }
}

/// <summary>
/// 优化目标 DTO
/// </summary>
public class OptimizationGoalsDto
{
    public int? TargetBlobCount { get; set; }
    public int CurrentBlobCount { get; set; }
    public double CountError { get; set; }
    public int NoisePenalty { get; set; }
    public int FragmentPenalty { get; set; }
    public double AreaDistributionScore { get; set; }
    public double ShapeRegularityScore { get; set; }
}

/// <summary>
/// 参数建议响应
/// </summary>
public class ParameterSuggestionResponse
{
    /// <summary>
    /// 是否成功
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// 诊断标签
    /// </summary>
    public List<string> Diagnostics { get; set; } = new();

    /// <summary>
    /// 综合评分
    /// </summary>
    public double OverallScore { get; set; }

    /// <summary>
    /// 参数建议列表
    /// </summary>
    public List<ParameterSuggestionDto> Suggestions { get; set; } = new();

    /// <summary>
    /// 优化目标状态
    /// </summary>
    public OptimizationGoalsDto Goals { get; set; } = new();
}

/// <summary>
/// 参数建议 DTO
/// </summary>
public class ParameterSuggestionDto
{
    /// <summary>
    /// 参数名称
    /// </summary>
    public string ParameterName { get; set; } = string.Empty;

    /// <summary>
    /// 当前值
    /// </summary>
    public object? CurrentValue { get; set; }

    /// <summary>
    /// 建议值
    /// </summary>
    public object? SuggestedValue { get; set; }

    /// <summary>
    /// 调整原因
    /// </summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>
    /// 预期改进
    /// </summary>
    public string ExpectedImprovement { get; set; } = string.Empty;
}

public class PreviewMissingResourceDto
{
    public string ResourceType { get; set; } = string.Empty;
    public string ResourceKey { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string DiagnosticCode { get; set; } = string.Empty;
}

/// <summary>
/// 调参策略信息 DTO
/// </summary>
public class StrategyInfoDto
{
    /// <summary>
    /// 策略名称
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 显示名称
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// 描述
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// 支持的算子列表
    /// </summary>
    public string[] SupportedOperators { get; set; } = Array.Empty<string>();
}

/// <summary>
/// 流程数据 DTO（简化版）
/// </summary>
public class FlowDataDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "AutoTuneFlow";
    public List<CanvasOperatorDataDto> Operators { get; set; } = new();

    /// <summary>
    /// 节点列表
    /// </summary>
    public List<FlowNodeDto> Nodes { get; set; } = new();

    /// <summary>
    /// 连接列表
    /// </summary>
    public List<FlowConnectionDto> Connections { get; set; } = new();

    /// <summary>
    /// 转换为实体
    /// </summary>
    public OperatorFlow ToEntity()
    {
        return FlowEntityMapper.ToEntity(this);
    }
}

public class FlowNodeDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public OperatorType Type { get; set; }
    public Dictionary<string, object> Parameters { get; set; } = new();
    public PositionDto Position { get; set; } = new();
}

public class PositionDto
{
    public double X { get; set; }
    public double Y { get; set; }
}

public class FlowConnectionDto
{
    public Guid Id { get; set; }
    public Guid SourceId { get; set; }
    public Guid TargetId { get; set; }
    public Guid SourceOperatorId { get; set; }
    public Guid SourcePortId { get; set; }
    public Guid TargetOperatorId { get; set; }
    public Guid TargetPortId { get; set; }
}

#endregion
