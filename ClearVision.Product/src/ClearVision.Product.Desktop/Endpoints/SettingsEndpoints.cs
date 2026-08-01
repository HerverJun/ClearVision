// SettingsEndpoints.cs
// 设置功能 API 端点
// 作者：蘅芜君

using System;
using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Ports;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using ClearVision.Product.Application.Services;
using ClearVision.Product.Core.AI.Tools;
using ClearVision.Product.Core.Cameras;
using ClearVision.Product.Core.Continuous;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Interfaces;
using ClearVision.Product.Desktop.Data;
using ClearVision.Product.Desktop.Triggers;
using ClearVision.Product.Infrastructure.AI;
using ClearVision.Product.Infrastructure.AI.Tools;
using ClearVision.Product.Infrastructure.Cameras;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Win32;

namespace ClearVision.Product.Desktop.Endpoints;

/// <summary>
/// 设置功能 API 端点
/// </summary>
public static class SettingsEndpoints
{
    private static readonly JsonSerializerOptions SettingsJsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public static IEndpointRouteBuilder MapSettingsEndpoints(this IEndpointRouteBuilder app)
    {
        // 获取当前配置
        app.MapGet("/api/settings", async (IConfigurationService configService, HttpContext context) =>
        {
            var config = await configService.LoadAsync();
            if (IsAdmin(context))
            {
                return Results.Ok(config);
            }

            return Results.Ok(ToSafeSettingsResponse(config));
        })
        .RequireClearVisionPermission(ClearVisionPermissionPolicies.RequireAuthenticated);

        // 更新配置
        app.MapPut("/api/settings", async (JsonElement request, IConfigurationService configService, HttpContext context) =>
        {
            if (!IsAdmin(context))
            {
                return Results.Forbid();
            }

            try
            {
                var currentConfig = await configService.LoadAsync();
                var config = MergeSettingsUpdate(currentConfig, request);

                await configService.SaveAsync(config);
                return Results.Ok(new { Message = "设置已保存", Config = config });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { Error = ex.Message });
            }
        })
        .RequireClearVisionPermission(ClearVisionPermissionPolicies.RequireAdmin);

        // 更新主题配置（避免回写整份配置造成并发覆盖）
        app.MapPut("/api/settings/theme", async (ThemeUpdateRequest request, IConfigurationService configService) =>
        {
            try
            {
                var config = await configService.LoadAsync();
                config.General ??= new GeneralConfig();
                config.General.Theme = GeneralConfig.NormalizeTheme(request.Theme);

                await configService.SaveAsync(config);

                return Results.Ok(new
                {
                    Message = "主题已保存",
                    theme = config.General.Theme
                });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { Error = ex.Message });
            }
        })
        .RequireClearVisionPermission(ClearVisionPermissionPolicies.RequireAdmin);

        // 重置配置为默认值
        app.MapPost("/api/settings/reset", async (IConfigurationService configService, AiConfigStore aiConfigStore, HttpContext context) =>
        {
            if (!IsAdmin(context))
            {
                return Results.Forbid();
            }

            var defaultConfig = new AppConfig();
            await configService.SaveAsync(defaultConfig);
            var defaultModels = aiConfigStore.ResetToDefaults();
            return Results.Ok(new
            {
                message = "系统配置和 AI 模型配置已恢复默认值",
                config = defaultConfig,
                aiModels = defaultModels.Select(ToAiModelResponse),
                resetScope = new[] { "appConfig", "aiModels" }
            });
        })
        .RequireClearVisionPermission(ClearVisionPermissionPolicies.RequireAdmin);

        // 获取磁盘容量信息（用于设置页存储卡片）
        app.MapGet("/api/settings/disk-usage", (string? path, IConfigurationService configService) =>
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                var errors = new List<string>();
                DiskUsageInfo? firstUsage = null;
                var configuredPath = configService.GetCurrent().Storage?.ImageSavePath;
                foreach (var rootPath in InspectionImagePersistencePaths.ResolveImageSaveRoots(configuredPath))
                {
                    if (TryBuildDiskUsage(rootPath, out var resolvedUsage, out var resolvedError))
                    {
                        if (resolvedUsage.CanWrite)
                        {
                            return Results.Ok(resolvedUsage);
                        }

                        firstUsage ??= resolvedUsage;
                        continue;
                    }

                    if (!string.IsNullOrWhiteSpace(resolvedError))
                    {
                        errors.Add(resolvedError);
                    }
                }

                if (firstUsage != null)
                {
                    return Results.Ok(firstUsage);
                }

                return Results.BadRequest(new
                {
                    Error = errors.FirstOrDefault() ?? "Unable to resolve disk usage path."
                });
            }

            if (!TryBuildDiskUsage(path, out var usage, out var error))
            {
                return Results.BadRequest(new { Error = error });
            }

            return Results.Ok(usage);
        })
        .RequireClearVisionPermission(ClearVisionPermissionPolicies.RequireAdmin);

        app.MapGet("/api/settings/database/status", async (
            [FromServices] VisionDatabaseMaintenanceService databaseMaintenance,
            CancellationToken cancellationToken) =>
        {
            return Results.Ok(await databaseMaintenance.GetStatusAsync(cancellationToken));
        })
        .RequireClearVisionPermission(ClearVisionPermissionPolicies.RequireAdmin);

        app.MapPost("/api/settings/database/repair", async (
            [FromServices] VisionDatabaseMaintenanceService databaseMaintenance,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (!IsAdmin(context))
            {
                return Results.Forbid();
            }

            try
            {
                return Results.Ok(await databaseMaintenance.RepairAsync(cancellationToken));
            }
            catch (Exception ex) when (IsDatabaseMaintenanceClientError(ex))
            {
                return BuildDatabaseMaintenanceError(ex);
            }
        })
        .RequireClearVisionPermission(ClearVisionPermissionPolicies.RequireAdmin);

        app.MapPost("/api/settings/database/backup", async (
            [FromServices] VisionDatabaseMaintenanceService databaseMaintenance,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (!IsAdmin(context))
            {
                return Results.Forbid();
            }

            try
            {
                return Results.Ok(await databaseMaintenance.CreateBackupAsync("manual", cancellationToken));
            }
            catch (Exception ex) when (IsDatabaseMaintenanceClientError(ex))
            {
                return BuildDatabaseMaintenanceError(ex);
            }
        })
        .RequireClearVisionPermission(ClearVisionPermissionPolicies.RequireAdmin);

        app.MapPost("/api/settings/database/restore", async (
            [FromBody] VisionDatabaseRestoreRequest request,
            [FromServices] VisionDatabaseMaintenanceService databaseMaintenance,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (!IsAdmin(context))
            {
                return Results.Forbid();
            }

            if (string.IsNullOrWhiteSpace(request.BackupPath))
            {
                return Results.BadRequest(new { Error = "BackupPath is required." });
            }

            try
            {
                return Results.Ok(await databaseMaintenance.RestoreBackupAsync(request.BackupPath, cancellationToken));
            }
            catch (Exception ex) when (IsDatabaseMaintenanceClientError(ex))
            {
                return BuildDatabaseMaintenanceError(ex);
            }
        })
        .RequireClearVisionPermission(ClearVisionPermissionPolicies.RequireAdmin);

        app.MapPost("/api/settings/database/cleanup", async (
            [FromBody] VisionDatabaseCleanupRequest request,
            [FromServices] VisionDatabaseMaintenanceService databaseMaintenance,
            [FromServices] IConfigurationService configService,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (!IsAdmin(context))
            {
                return Results.Forbid();
            }

            var retentionDays = request.RetentionDays
                ?? configService.GetCurrent().Storage?.RetentionDays
                ?? 30;
            try
            {
                return Results.Ok(await databaseMaintenance.CleanupHistoryAsync(retentionDays, cancellationToken));
            }
            catch (Exception ex) when (IsDatabaseMaintenanceClientError(ex))
            {
                return BuildDatabaseMaintenanceError(ex);
            }
        })
        .RequireClearVisionPermission(ClearVisionPermissionPolicies.RequireAdmin);

        // ==================== AI 多模型管理 API ====================

        // 获取所有模型（不含 ApiKey）
        app.MapGet("/api/ai/models", (AiConfigStore configStore, HttpContext context) =>
        {
            var models = configStore.GetAll();
            var result = IsAdmin(context)
                ? models.Select(ToAiModelResponse)
                : models.Select(ToSafeAiModelResponse);
            return Results.Ok(result);
        })
        .RequireClearVisionPermission(ClearVisionPermissionPolicies.RequireAuthenticated);

        app.MapPost("/api/ai/reasoning-support", (AiReasoningSupportRequest request) =>
        {
            var support = AiReasoningModelFamilyCatalog.Resolve(
                request.Provider,
                request.Model,
                request.BaseUrl,
                request.Protocol);
            return Results.Ok(support);
        })
        .RequireClearVisionPermission(ClearVisionPermissionPolicies.RequireAuthenticated);

        // 创建新模型
        app.MapPost("/api/ai/models", (AiModelCreateRequest request, AiConfigStore configStore, HttpContext context) =>
        {
            if (!IsAdmin(context))
            {
                return Results.Forbid();
            }

            try
            {
                var model = new AiModelConfig
                {
                    Id = $"model_{Guid.NewGuid():N}",
                    Name = request.Name ?? "新建模型",
                    DisplayName = request.DisplayName,
                    Provider = request.Provider ?? AiModelConfig.GetLegacyProviderByProtocol(request.Protocol),
                    ApiKey = request.ApiKey ?? "",
                    Model = request.Model ?? string.Empty,
                    BaseUrl = string.IsNullOrWhiteSpace(request.BaseUrl) ? null : request.BaseUrl,
                    TimeoutMs = request.TimeoutMs > 0 ? request.TimeoutMs : 120000,
                    Protocol = request.Protocol,
                    WireApi = request.WireApi,
                    AuthMode = request.AuthMode,
                    AuthHeaderName = request.AuthHeaderName,
                    ExtraHeaders = CloneStringMap(request.ExtraHeaders),
                    ExtraQuery = CloneStringMap(request.ExtraQuery),
                    ExtraBody = CloneJsonMap(request.ExtraBody),
                    RoleBindings = CloneStringList(request.RoleBindings),
                    ModelRole = request.ModelRole,
                    Priority = request.Priority,
                    Remark = request.Remark,
                    IsActive = false,
                    IsEnabled = request.IsEnabled ?? true,
                    Capabilities = request.Capabilities?.Clone(),
                    Reasoning = request.Reasoning?.Clone()
                };
                configStore.Add(model);
                return Results.Ok(new { Message = "模型已创建", model.Id });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { Error = ex.Message });
            }
        })
        .RequireClearVisionPermission(ClearVisionPermissionPolicies.RequireAdmin);

        // 更新指定模型
        app.MapPut("/api/ai/models/{id}", (string id, AiModelUpdateRequest request, AiConfigStore configStore, HttpContext context) =>
        {
            if (!IsAdmin(context))
            {
                return Results.Forbid();
            }

            try
            {
                var updated = new AiModelConfig
                {
                    Name = request.Name!,
                    DisplayName = request.DisplayName,
                    Provider = request.Provider!,
                    ApiKey = request.ApiKey ?? "", // 空字符串 → 保留原值（由 AiConfigStore.Update 处理）
                    Model = request.Model ?? string.Empty,
                    BaseUrl = request.BaseUrl,
                    TimeoutMs = request.TimeoutMs,
                    Protocol = request.Protocol,
                    WireApi = request.WireApi,
                    AuthMode = request.AuthMode,
                    AuthHeaderName = request.AuthHeaderName,
                    ExtraHeaders = CloneStringMap(request.ExtraHeaders),
                    ExtraQuery = CloneStringMap(request.ExtraQuery),
                    ExtraBody = CloneJsonMap(request.ExtraBody),
                    RoleBindings = CloneStringList(request.RoleBindings),
                    ModelRole = request.ModelRole,
                    Priority = request.Priority,
                    Remark = request.Remark,
                    IsEnabled = request.IsEnabled ?? true,
                    Capabilities = request.Capabilities?.Clone(),
                    Reasoning = request.Reasoning?.Clone()
                };
                var result = configStore.Update(id, updated, ResolveApiKeyUpdateMode(request.ApiKeyOperation, request.ApiKey));
                if (result == null)
                    return Results.NotFound(new { Error = $"模型 {id} 不存在" });

                return Results.Ok(new { Message = "模型已更新" });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { Error = ex.Message });
            }
        })
        .RequireClearVisionPermission(ClearVisionPermissionPolicies.RequireAdmin);

        // 删除指定模型
        app.MapDelete("/api/ai/models/{id}", (string id, AiConfigStore configStore, HttpContext context) =>
        {
            if (!IsAdmin(context))
            {
                return Results.Forbid();
            }

            try
            {
                var ok = configStore.Delete(id);
                return ok
                    ? Results.Ok(new { Message = "模型已删除" })
                    : Results.NotFound(new { Error = $"模型 {id} 不存在" });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { Error = ex.Message });
            }
        })
        .RequireClearVisionPermission(ClearVisionPermissionPolicies.RequireAdmin);

        // 设为激活模型
        app.MapPost("/api/ai/models/{id}/activate", (string id, AiConfigStore configStore, HttpContext context) =>
        {
            if (!IsAdmin(context))
            {
                return Results.Forbid();
            }

            var ok = configStore.SetActive(id);
            return ok
                ? Results.Ok(new { Message = "已切换激活模型" })
                : Results.NotFound(new { Error = $"模型 {id} 不存在" });
        })
        .RequireClearVisionPermission(ClearVisionPermissionPolicies.RequireAdmin);

        // 测试指定模型的连接（使用该模型的真实 Key，不影响全局 active 状态）
        app.MapPost("/api/ai/models/{id}/default-planner", (string id, AiConfigStore configStore, HttpContext context) =>
        {
            if (!IsAdmin(context))
            {
                return Results.Forbid();
            }

            var ok = configStore.SetDefaultForRole(id, AiModelConfig.RolePlanner);
            return ok
                ? Results.Ok(new { Message = "Default planner model updated.", role = AiModelConfig.RolePlanner })
                : Results.NotFound(new { Error = $"Model {id} not found." });
        })
        .RequireClearVisionPermission(ClearVisionPermissionPolicies.RequireAdmin);

        app.MapPost("/api/ai/models/{id}/default-shadow-eval", (string id, AiConfigStore configStore, HttpContext context) =>
        {
            if (!IsAdmin(context))
            {
                return Results.Forbid();
            }

            var ok = configStore.SetDefaultForRole(id, AiModelConfig.RoleShadowEval);
            return ok
                ? Results.Ok(new { Message = "Default shadow eval model updated.", role = AiModelConfig.RoleShadowEval })
                : Results.NotFound(new { Error = $"Model {id} not found." });
        })
        .RequireClearVisionPermission(ClearVisionPermissionPolicies.RequireAdmin);

        app.MapPost("/api/ai/models/{id}/test", async (string id, AiConfigStore configStore, AiApiClient apiClient, HttpContext context) =>
        {
            if (!IsAdmin(context))
            {
                return Results.Forbid();
            }

            var model = configStore.GetById(id);
            if (model == null)
            {
                return Results.NotFound(BuildAiModelConnectionResult(
                    connectionOk: false,
                    statusCode: null,
                    errorCode: "model_config_not_found",
                    latencyMs: 0,
                    sanitizedMessage: $"Model config {id} not found.",
                    provider: string.Empty,
                    modelName: string.Empty,
                    protocol: string.Empty,
                    wireApi: string.Empty));
            }

            var testResult = await TestAiModelConnectionAsync(model, apiClient, context.RequestAborted);
            configStore.UpdateTestStatus(
                id,
                testResult.ConnectionOk ? "ok" : "failed",
                DateTimeOffset.UtcNow,
                testResult.LatencyMs);
            return Results.Ok(testResult);
        })
        .RequireClearVisionPermission(ClearVisionPermissionPolicies.RequireAdmin);

        // AI model connection tests only call the configured LLM endpoint and do not touch RuntimePreview, Station, camera, PLC, or deployment.
        // ==================== 相机管理 API ====================

        // 搜索在线相机设备
        app.MapGet("/api/settings/runtime-preview-pilot/config", async (IConfigurationService configService) =>
        {
            var config = await configService.LoadAsync();
            var pilot = config.Runtime.RuntimePreviewPilot.CloneNormalized();
            return Results.Ok(new
            {
                config = pilot,
                validation = RuntimePreviewPilotConfigValidator.Validate(pilot),
                metadataOnly = true,
                realResourcesTouched = false
            });
        });

        app.MapPut("/api/settings/runtime-preview-pilot/config", async (
            RuntimePreviewPilotConfig request,
            IConfigurationService configService,
            HttpContext context) =>
        {
            if (!IsAdmin(context))
            {
                return Results.Forbid();
            }

            var failures = RuntimePreviewPilotConfigValidator.Validate(request);
            if (failures.Count > 0)
            {
                return Results.BadRequest(new
                {
                    error = "RuntimePreview Pilot config validation failed.",
                    failures
                });
            }

            var normalized = request.CloneNormalized();
            var config = await configService.LoadAsync();
            config.Runtime ??= new RuntimeConfig();
            config.Runtime.RuntimePreviewPilot = normalized;
            await configService.SaveAsync(config);
            return Results.Ok(new
            {
                message = "RuntimePreview Pilot config saved.",
                config = normalized,
                validation = Array.Empty<string>(),
                metadataOnly = true,
                realResourcesTouched = false
            });
        })
        .RequireClearVisionPermission(ClearVisionPermissionPolicies.RequireAdmin);

        app.MapGet("/api/settings/runtime-preview-pilot/catalog", async (
            [FromServices] IConfigurationService configService,
            [FromServices] AiConfigStore aiConfigStore,
            [FromServices] RuntimePreviewPilotResourceCatalog catalogBuilder) =>
        {
            var config = await configService.LoadAsync();
            var pilot = config.Runtime.RuntimePreviewPilot.CloneNormalized();
            var catalog = catalogBuilder.Build(pilot, config, aiConfigStore);
            return Results.Ok(catalog);
        });

        app.MapPost("/api/settings/runtime-preview-pilot/readiness", async (
            RuntimePreviewPilotReadinessEndpointRequest request,
            [FromServices] IConfigurationService configService,
            [FromServices] AiConfigStore aiConfigStore,
            [FromServices] RuntimePreviewPilotResourceCatalog catalogBuilder,
            [FromServices] RuntimePreviewPilotReadinessGate readinessGate,
            [FromServices] RuntimePreviewPermissionBroker permissionBroker,
            HttpContext httpContext) =>
        {
            var endpointDecision = permissionBroker.EvaluateEndpointAccess(
                "runtime-preview-pilot-readiness",
                IsAdmin(httpContext),
                IsDeveloperUiRequested(httpContext));
            if (!endpointDecision.Allowed)
            {
                return Results.Json(new
                {
                    error = endpointDecision.ReasonCode,
                    permissionDecision = endpointDecision,
                    metadataOnly = true,
                    realResourcesTouched = false
                }, statusCode: StatusCodes.Status403Forbidden);
            }

            var appConfig = await configService.LoadAsync();
            var pilot = request.Config ?? appConfig.Runtime.RuntimePreviewPilot.CloneNormalized();
            var failures = RuntimePreviewPilotConfigValidator.Validate(pilot);
            if (failures.Count > 0)
            {
                return Results.BadRequest(new
                {
                    error = "RuntimePreview Pilot config validation failed.",
                    failures
                });
            }

            pilot.Normalize();
            var toolName = string.IsNullOrWhiteSpace(request.ToolName)
                ? ClearVision.Product.Infrastructure.AI.Agent.RuntimePreviewPermissionGate.ReplayToolName
                : request.ToolName.Trim();
            var workflowDraft = ExtractRuntimePreviewWorkflowDraft(request);
            var arguments = BuildRuntimePreviewReadinessArguments(request, workflowDraft);
            var catalog = catalogBuilder.Build(pilot, appConfig, aiConfigStore, workflowDraft);
            var context = new ClearVision.Product.Core.AI.Tools.VisionAgentToolContext
            {
                RuntimePreviewConsent = true,
                RuntimePreviewPilot = pilot,
                AllowedPermissions = new HashSet<ClearVision.Product.Core.AI.Tools.VisionAgentToolPermission>
                {
                    ClearVision.Product.Core.AI.Tools.VisionAgentToolPermission.ReadOnly,
                    ClearVision.Product.Core.AI.Tools.VisionAgentToolPermission.Simulation,
                    ClearVision.Product.Core.AI.Tools.VisionAgentToolPermission.RuntimePreview
                }
            };
            var result = readinessGate.Evaluate(pilot, catalog, toolName, arguments, context);
            return Results.Ok(new
            {
                readiness = result,
                catalog,
                metadataOnly = true,
                realResourcesTouched = false
            });
        });

        app.MapGet("/api/settings/runtime-preview-pilot/sessions", (
            [FromServices] RuntimePreviewSessionStore sessionStore,
            [FromServices] RuntimePreviewPermissionBroker permissionBroker,
            HttpContext httpContext) =>
        {
            var endpointDecision = permissionBroker.EvaluateEndpointAccess(
                "runtime-preview-pilot-sessions",
                IsAdmin(httpContext),
                IsDeveloperUiRequested(httpContext));
            if (!endpointDecision.Allowed)
            {
                return Results.Json(new
                {
                    error = endpointDecision.ReasonCode,
                    permissionDecision = endpointDecision,
                    metadataOnly = true,
                    realResourcesTouched = false
                }, statusCode: StatusCodes.Status403Forbidden);
            }

            return Results.Ok(new
            {
                sessions = sessionStore.List(),
                permissionDecision = endpointDecision,
                metadataOnly = true,
                realResourcesTouched = false
            });
        });

        app.MapPost("/api/settings/runtime-preview-pilot/sessions", async (
            RuntimePreviewSessionCreateRequest request,
            [FromServices] IConfigurationService configService,
            [FromServices] AiConfigStore aiConfigStore,
            [FromServices] RuntimePreviewSimulatedExecutionHarness harness,
            [FromServices] RuntimePreviewPermissionBroker permissionBroker,
            HttpContext httpContext) =>
        {
            var endpointDecision = permissionBroker.EvaluateEndpointAccess(
                "runtime-preview-pilot-session-create",
                IsAdmin(httpContext),
                IsDeveloperUiRequested(httpContext));
            if (!endpointDecision.Allowed)
            {
                return Results.Json(new
                {
                    error = endpointDecision.ReasonCode,
                    permissionDecision = endpointDecision,
                    metadataOnly = true,
                    realResourcesTouched = false
                }, statusCode: StatusCodes.Status403Forbidden);
            }

            var appConfig = await configService.LoadAsync();
            var session = harness.CreateMetadataSession(request, appConfig, aiConfigStore);
            return Results.Ok(new
            {
                session,
                permissionDecision = endpointDecision,
                metadataOnly = true,
                realResourcesTouched = false
            });
        });

        app.MapPost("/api/settings/runtime-preview-pilot/sessions/simulate", async (
            RuntimePreviewSessionCreateRequest request,
            [FromServices] IConfigurationService configService,
            [FromServices] AiConfigStore aiConfigStore,
            [FromServices] RuntimePreviewSimulatedExecutionHarness harness,
            [FromServices] RuntimePreviewPermissionBroker permissionBroker,
            HttpContext httpContext) =>
        {
            var endpointDecision = permissionBroker.EvaluateEndpointAccess(
                "runtime-preview-pilot-sessions-simulate",
                IsAdmin(httpContext),
                IsDeveloperUiRequested(httpContext));
            if (!endpointDecision.Allowed)
            {
                return Results.Json(new
                {
                    error = endpointDecision.ReasonCode,
                    permissionDecision = endpointDecision,
                    metadataOnly = true,
                    realResourcesTouched = false
                }, statusCode: StatusCodes.Status403Forbidden);
            }

            var appConfig = await configService.LoadAsync();
            var report = harness.RunEndToEnd(
                request,
                appConfig,
                aiConfigStore,
                isAdmin: IsAdmin(httpContext),
                developerUiRequested: IsDeveloperUiRequested(httpContext));
            return Results.Ok(new
            {
                session = report.Session,
                report,
                auditEvents = report.AuditEvents,
                permissionDecision = endpointDecision,
                metadataOnly = true,
                realResourcesTouched = false
            });
        });

        app.MapGet("/api/settings/runtime-preview-pilot/sessions/{sessionId}/report", (
            string sessionId,
            [FromServices] RuntimePreviewReportArchive reportArchive,
            [FromServices] RuntimePreviewPermissionBroker permissionBroker,
            HttpContext httpContext) =>
        {
            var endpointDecision = permissionBroker.EvaluateEndpointAccess(
                "runtime-preview-pilot-session-report",
                IsAdmin(httpContext),
                IsDeveloperUiRequested(httpContext));
            if (!endpointDecision.Allowed)
            {
                return Results.Json(new
                {
                    error = endpointDecision.ReasonCode,
                    permissionDecision = endpointDecision,
                    metadataOnly = true,
                    realResourcesTouched = false
                }, statusCode: StatusCodes.Status403Forbidden);
            }

            var report = reportArchive.GetBySessionId(sessionId);
            return report == null
                ? Results.NotFound(new { error = "RuntimePreview session report was not found." })
                : Results.Ok(new
                {
                    report,
                    permissionDecision = endpointDecision,
                    metadataOnly = true,
                    realResourcesTouched = false
                });
        });

        app.MapPost("/api/settings/runtime-preview-pilot/sessions/{sessionId}/cancel", (
            string sessionId,
            [FromServices] RuntimePreviewSimulatedExecutionHarness harness,
            [FromServices] RuntimePreviewPermissionBroker permissionBroker,
            HttpContext httpContext) =>
        {
            var endpointDecision = permissionBroker.EvaluateEndpointAccess(
                "runtime-preview-pilot-session-cancel",
                IsAdmin(httpContext),
                IsDeveloperUiRequested(httpContext));
            if (!endpointDecision.Allowed)
            {
                return Results.Json(new
                {
                    error = endpointDecision.ReasonCode,
                    permissionDecision = endpointDecision,
                    metadataOnly = true,
                    realResourcesTouched = false
                }, statusCode: StatusCodes.Status403Forbidden);
            }

            var session = harness.Cancel(sessionId);
            return session == null
                ? Results.NotFound(new { error = "RuntimePreview session was not found." })
                : Results.Ok(new
                {
                    session,
                    permissionDecision = endpointDecision,
                    metadataOnly = true,
                    realResourcesTouched = false
                });
        });

        app.MapGet("/api/settings/runtime-preview-pilot/sessions/{sessionId}/replay", (
            string sessionId,
            [FromServices] RuntimePreviewSimulatedExecutionHarness harness,
            [FromServices] RuntimePreviewPermissionBroker permissionBroker,
            HttpContext httpContext) =>
        {
            var endpointDecision = permissionBroker.EvaluateEndpointAccess(
                "runtime-preview-pilot-session-replay",
                IsAdmin(httpContext),
                IsDeveloperUiRequested(httpContext));
            if (!endpointDecision.Allowed)
            {
                return Results.Json(new
                {
                    error = endpointDecision.ReasonCode,
                    permissionDecision = endpointDecision,
                    metadataOnly = true,
                    realResourcesTouched = false
                }, statusCode: StatusCodes.Status403Forbidden);
            }

            var replay = harness.Replay(sessionId);
            return replay == null
                ? Results.NotFound(new { error = "RuntimePreview session replay was not found." })
                : Results.Ok(new
                {
                    replay,
                    permissionDecision = endpointDecision,
                    metadataOnly = true,
                    realResourcesTouched = false
                });
        });

        app.MapGet("/api/settings/runtime-preview-pilot/sessions/{sessionId}/report/export", (
            string sessionId,
            [FromServices] RuntimePreviewReportArchive reportArchive,
            [FromServices] RuntimePreviewPermissionBroker permissionBroker,
            HttpContext httpContext) =>
        {
            var endpointDecision = permissionBroker.EvaluateEndpointAccess(
                "runtime-preview-pilot-session-report-export",
                IsAdmin(httpContext),
                IsDeveloperUiRequested(httpContext));
            if (!endpointDecision.Allowed)
            {
                return Results.Json(new
                {
                    error = endpointDecision.ReasonCode,
                    permissionDecision = endpointDecision,
                    metadataOnly = true,
                    realResourcesTouched = false
                }, statusCode: StatusCodes.Status403Forbidden);
            }

            var report = reportArchive.GetBySessionId(sessionId);
            return report == null
                ? Results.NotFound(new { error = "RuntimePreview session report export was not found." })
                : Results.Ok(new
                {
                    export = new
                    {
                        fileName = $"{report.ReportId}.metadata-only.json",
                        exportedAtUtc = DateTimeOffset.UtcNow,
                        report,
                        metadataOnly = true,
                        realResourcesTouched = false
                    },
                    permissionDecision = endpointDecision,
                    metadataOnly = true,
                    realResourcesTouched = false
                });
        });

        app.MapPost("/api/settings/runtime-preview-pilot/sessions/deploy-readiness", async (
            RuntimePreviewDeployReadinessRequest request,
            [FromServices] IConfigurationService configService,
            [FromServices] AiConfigStore aiConfigStore,
            [FromServices] RuntimePreviewDeployReadinessService deployReadinessService,
            [FromServices] RuntimePreviewPermissionBroker permissionBroker,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var endpointDecision = permissionBroker.EvaluateEndpointAccess(
                "runtime-preview-pilot-deploy-readiness",
                IsAdmin(httpContext),
                IsDeveloperUiRequested(httpContext));
            if (!endpointDecision.Allowed)
            {
                return Results.Json(new
                {
                    error = endpointDecision.ReasonCode,
                    permissionDecision = endpointDecision,
                    metadataOnly = true,
                    realResourcesTouched = false
                }, statusCode: StatusCodes.Status403Forbidden);
            }

            var appConfig = await configService.LoadAsync();
            var report = await deployReadinessService.GenerateAsync(
                request,
                appConfig,
                aiConfigStore,
                isAdmin: IsAdmin(httpContext),
                developerUiRequested: IsDeveloperUiRequested(httpContext),
                cancellationToken);
            return Results.Ok(new
            {
                deployReadinessReport = report,
                session = report.SimulationReport?.Session,
                permissionDecision = endpointDecision,
                metadataOnly = true,
                realResourcesTouched = false
            });
        });

        app.MapPost("/api/settings/runtime-preview-pilot/sessions/package-readiness", async (
            RuntimePreviewPackageReadinessRequest request,
            [FromServices] IConfigurationService configService,
            [FromServices] AiConfigStore aiConfigStore,
            [FromServices] RuntimePreviewPackageReadinessBridge packageReadinessBridge,
            [FromServices] RuntimePreviewReportArchive reportArchive,
            [FromServices] RuntimePreviewPermissionBroker permissionBroker,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var endpointDecision = permissionBroker.EvaluateEndpointAccess(
                "runtime-preview-pilot-package-readiness",
                IsAdmin(httpContext),
                IsDeveloperUiRequested(httpContext));
            if (!endpointDecision.Allowed)
            {
                return Results.Json(new
                {
                    error = endpointDecision.ReasonCode,
                    permissionDecision = endpointDecision,
                    metadataOnly = true,
                    realResourcesTouched = false
                }, statusCode: StatusCodes.Status403Forbidden);
            }

            var appConfig = await configService.LoadAsync();
            var report = await packageReadinessBridge.GenerateAsync(
                request,
                appConfig,
                aiConfigStore,
                isAdmin: IsAdmin(httpContext),
                developerUiRequested: IsDeveloperUiRequested(httpContext),
                cancellationToken);
            return Results.Ok(new
            {
                packageReadinessReport = report,
                manifestDryRunReport = string.IsNullOrWhiteSpace(report.ManifestDryRunReportId)
                    ? null
                    : reportArchive.GetManifestDryRunReport(report.ManifestDryRunReportId),
                permissionDecision = endpointDecision,
                metadataOnly = true,
                realResourcesTouched = false
            });
        });

        app.MapPost("/api/settings/runtime-preview-pilot/sessions/manifest-dry-run", async (
            RuntimePackageManifestDryRunRequest request,
            [FromServices] IConfigurationService configService,
            [FromServices] AiConfigStore aiConfigStore,
            [FromServices] RuntimePreviewPackageReadinessBridge packageReadinessBridge,
            [FromServices] RuntimePreviewReportArchive reportArchive,
            [FromServices] RuntimePreviewPermissionBroker permissionBroker,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var endpointDecision = permissionBroker.EvaluateEndpointAccess(
                "runtime-preview-pilot-manifest-dry-run",
                IsAdmin(httpContext),
                IsDeveloperUiRequested(httpContext));
            if (!endpointDecision.Allowed)
            {
                return Results.Json(new
                {
                    error = endpointDecision.ReasonCode,
                    permissionDecision = endpointDecision,
                    metadataOnly = true,
                    realResourcesTouched = false
                }, statusCode: StatusCodes.Status403Forbidden);
            }

            var appConfig = await configService.LoadAsync();
            var packageReport = await packageReadinessBridge.GenerateAsync(
                new RuntimePreviewPackageReadinessRequest
                {
                    Config = request.Config,
                    ToolName = request.ToolName,
                    Arguments = request.Arguments,
                    WorkflowDraft = request.WorkflowDraft,
                    RuntimePreviewConsent = request.RuntimePreviewConsent,
                    RequireReplay = request.RequireReplay
                },
                appConfig,
                aiConfigStore,
                isAdmin: IsAdmin(httpContext),
                developerUiRequested: IsDeveloperUiRequested(httpContext),
                cancellationToken);
            var manifestReport = string.IsNullOrWhiteSpace(packageReport.ManifestDryRunReportId)
                ? null
                : reportArchive.GetManifestDryRunReport(packageReport.ManifestDryRunReportId);
            return Results.Ok(new
            {
                packageReadinessReport = packageReport,
                manifestDryRunReportId = packageReport.ManifestDryRunReportId,
                manifestDryRunReport = manifestReport,
                permissionDecision = endpointDecision,
                metadataOnly = true,
                packageCreated = false,
                deploymentExecuted = false,
                realResourcesTouched = false
            });
        });

        app.MapPost("/api/settings/runtime-preview-pilot/sessions/pre-release-review", async (
            RuntimePreviewPreReleaseReviewRequest request,
            [FromServices] IConfigurationService configService,
            [FromServices] AiConfigStore aiConfigStore,
            [FromServices] RuntimePreviewPreReleaseReviewService preReleaseReviewService,
            [FromServices] RuntimePreviewReportArchive reportArchive,
            [FromServices] RuntimePreviewPermissionBroker permissionBroker,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var endpointDecision = permissionBroker.EvaluateEndpointAccess(
                "runtime-preview-pilot-pre-release-review",
                IsAdmin(httpContext),
                IsDeveloperUiRequested(httpContext));
            if (!endpointDecision.Allowed)
            {
                return Results.Json(new
                {
                    error = endpointDecision.ReasonCode,
                    permissionDecision = endpointDecision,
                    metadataOnly = true,
                    realResourcesTouched = false
                }, statusCode: StatusCodes.Status403Forbidden);
            }

            var appConfig = await configService.LoadAsync();
            var report = await preReleaseReviewService.GenerateAsync(
                request,
                appConfig,
                aiConfigStore,
                isAdmin: IsAdmin(httpContext),
                developerUiRequested: IsDeveloperUiRequested(httpContext),
                cancellationToken);
            return Results.Ok(new
            {
                preReleaseReviewReport = report,
                packageReadinessReport = string.IsNullOrWhiteSpace(report.PackageReadinessReportId)
                    ? null
                    : reportArchive.GetPackageReadinessReport(report.PackageReadinessReportId),
                manifestDryRunReport = string.IsNullOrWhiteSpace(report.ManifestId)
                    ? null
                    : reportArchive.GetManifestDryRunReport(report.ManifestId),
                stationCompatibilityReport = string.IsNullOrWhiteSpace(report.StationCompatibilityReportId)
                    ? null
                    : reportArchive.GetStationCompatibilityReport(report.StationCompatibilityReportId),
                operatorContractValidationReport = string.IsNullOrWhiteSpace(report.OperatorContractValidationReportId)
                    ? null
                    : reportArchive.GetOperatorContractValidationReport(report.OperatorContractValidationReportId),
                permissionDecision = endpointDecision,
                metadataOnly = true,
                packageCreated = false,
                deploymentExecuted = false,
                realResourcesTouched = false
            });
        });

        app.MapPost("/api/settings/runtime-preview-pilot/retention/cleanup", (
            RuntimePreviewRetentionCleanupEndpointRequest request,
            [FromServices] RuntimePreviewGovernanceMaintenanceService maintenanceService,
            [FromServices] RuntimePreviewPermissionBroker permissionBroker,
            HttpContext httpContext) =>
        {
            var endpointDecision = permissionBroker.EvaluateEndpointAccess(
                "runtime-preview-pilot-retention-cleanup",
                IsAdmin(httpContext),
                IsDeveloperUiRequested(httpContext));
            if (!endpointDecision.Allowed)
            {
                return Results.Json(new
                {
                    error = endpointDecision.ReasonCode,
                    permissionDecision = endpointDecision,
                    metadataOnly = true,
                    realResourcesTouched = false
                }, statusCode: StatusCodes.Status403Forbidden);
            }

            var cleanup = maintenanceService.Cleanup(request.RetentionDays, request.MaxSessions);
            return Results.Ok(new
            {
                cleanup,
                permissionDecision = endpointDecision,
                metadataOnly = true,
                realResourcesTouched = false
            });
        });

        app.MapGet("/api/settings/runtime-preview-pilot/station-profiles", (
            [FromServices] RuntimePreviewStationProfileCatalog stationProfileCatalog,
            [FromServices] RuntimePreviewPermissionBroker permissionBroker,
            HttpContext httpContext) =>
        {
            var endpointDecision = permissionBroker.EvaluateEndpointAccess(
                "runtime-preview-pilot-station-profiles",
                IsAdmin(httpContext),
                IsDeveloperUiRequested(httpContext));
            if (!endpointDecision.Allowed)
            {
                return Results.Json(new
                {
                    error = endpointDecision.ReasonCode,
                    permissionDecision = endpointDecision,
                    metadataOnly = true,
                    realResourcesTouched = false
                }, statusCode: StatusCodes.Status403Forbidden);
            }

            return Results.Ok(new
            {
                stationProfiles = stationProfileCatalog.BuildProfiles(),
                permissionDecision = endpointDecision,
                metadataOnly = true,
                realResourcesTouched = false
            });
        });

        app.MapGet("/api/settings/runtime-preview-pilot/operator-contract-registry", (
            [FromServices] RuntimePreviewOperatorContractRegistry operatorContractRegistry,
            [FromServices] RuntimePreviewPermissionBroker permissionBroker,
            HttpContext httpContext) =>
        {
            var endpointDecision = permissionBroker.EvaluateEndpointAccess(
                "runtime-preview-pilot-operator-contract-registry",
                IsAdmin(httpContext),
                IsDeveloperUiRequested(httpContext));
            if (!endpointDecision.Allowed)
            {
                return Results.Json(new
                {
                    error = endpointDecision.ReasonCode,
                    permissionDecision = endpointDecision,
                    metadataOnly = true,
                    realResourcesTouched = false
                }, statusCode: StatusCodes.Status403Forbidden);
            }

            return Results.Ok(new
            {
                operatorContractRegistry = operatorContractRegistry.BuildRegistry(),
                operatorContractCoverageReport = operatorContractRegistry.BuildCoverageReport(),
                permissionDecision = endpointDecision,
                metadataOnly = true,
                realResourcesTouched = false
            });
        });

        app.MapGet("/api/settings/runtime-preview-pilot/scenario-evidence", async (
            [FromServices] IConfigurationService configService,
            [FromServices] AiConfigStore aiConfigStore,
            [FromServices] RuntimePreviewScenarioEvidenceService scenarioEvidenceService,
            [FromServices] RuntimePreviewPermissionBroker permissionBroker,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var endpointDecision = permissionBroker.EvaluateEndpointAccess(
                "runtime-preview-pilot-scenario-evidence",
                IsAdmin(httpContext),
                IsDeveloperUiRequested(httpContext));
            if (!endpointDecision.Allowed)
            {
                return Results.Json(new
                {
                    error = endpointDecision.ReasonCode,
                    permissionDecision = endpointDecision,
                    metadataOnly = true,
                    realResourcesTouched = false
                }, statusCode: StatusCodes.Status403Forbidden);
            }

            var appConfig = await configService.LoadAsync();
            var evidence = await scenarioEvidenceService.RunAsync(appConfig, aiConfigStore, cancellationToken);
            return Results.Ok(new
            {
                evidence,
                permissionDecision = endpointDecision,
                metadataOnly = true,
                realResourcesTouched = false
            });
        });

        app.MapGet("/api/settings/runtime-preview-pilot/scenario-corpus", (
            [FromServices] RuntimePreviewScenarioCorpusService scenarioCorpusService,
            [FromServices] RuntimePreviewPermissionBroker permissionBroker,
            HttpContext httpContext) =>
        {
            var endpointDecision = permissionBroker.EvaluateEndpointAccess(
                "runtime-preview-pilot-scenario-corpus",
                IsAdmin(httpContext),
                IsDeveloperUiRequested(httpContext));
            if (!endpointDecision.Allowed)
            {
                return Results.Json(new
                {
                    error = endpointDecision.ReasonCode,
                    permissionDecision = endpointDecision,
                    metadataOnly = true,
                    realResourcesTouched = false
                }, statusCode: StatusCodes.Status403Forbidden);
            }

            var corpus = scenarioCorpusService.BuildCorpus();
            return Results.Ok(new
            {
                corpus,
                permissionDecision = endpointDecision,
                metadataOnly = true,
                realResourcesTouched = false
            });
        });

        app.MapGet("/api/settings/runtime-preview-pilot/redacted-flow-corpus", (
            [FromServices] RuntimePreviewRedactedFlowCorpusService redactedFlowCorpusService,
            [FromServices] RuntimePreviewPermissionBroker permissionBroker,
            HttpContext httpContext) =>
        {
            var endpointDecision = permissionBroker.EvaluateEndpointAccess(
                "runtime-preview-pilot-redacted-flow-corpus",
                IsAdmin(httpContext),
                IsDeveloperUiRequested(httpContext));
            if (!endpointDecision.Allowed)
            {
                return Results.Json(new
                {
                    error = endpointDecision.ReasonCode,
                    permissionDecision = endpointDecision,
                    metadataOnly = true,
                    realResourcesTouched = false
                }, statusCode: StatusCodes.Status403Forbidden);
            }

            var corpus = redactedFlowCorpusService.BuildCorpus();
            return Results.Ok(new
            {
                corpus,
                permissionDecision = endpointDecision,
                metadataOnly = true,
                realResourcesTouched = false
            });
        });

        app.MapGet("/api/settings/runtime-preview-pilot/agent-explanation-benchmark", (
            [FromServices] RuntimePreviewAgentExplanationService explanationService,
            [FromServices] RuntimePreviewPermissionBroker permissionBroker,
            HttpContext httpContext) =>
        {
            var endpointDecision = permissionBroker.EvaluateEndpointAccess(
                "runtime-preview-pilot-agent-explanation-benchmark",
                IsAdmin(httpContext),
                IsDeveloperUiRequested(httpContext));
            if (!endpointDecision.Allowed)
            {
                return Results.Json(new
                {
                    error = endpointDecision.ReasonCode,
                    permissionDecision = endpointDecision,
                    metadataOnly = true,
                    realResourcesTouched = false
                }, statusCode: StatusCodes.Status403Forbidden);
            }

            var benchmark = explanationService.Run();
            return Results.Ok(new
            {
                benchmark,
                permissionDecision = endpointDecision,
                metadataOnly = true,
                realResourcesTouched = false
            });
        });

        app.MapGet("/api/settings/runtime-preview-pilot/governance/index", (
            [FromServices] RuntimePreviewGovernanceStore governanceStore,
            [FromServices] RuntimePreviewPermissionBroker permissionBroker,
            HttpContext httpContext) =>
        {
            var endpointDecision = permissionBroker.EvaluateEndpointAccess(
                "runtime-preview-pilot-governance-index",
                IsAdmin(httpContext),
                IsDeveloperUiRequested(httpContext));
            if (!endpointDecision.Allowed)
            {
                return Results.Json(new
                {
                    error = endpointDecision.ReasonCode,
                    permissionDecision = endpointDecision,
                    metadataOnly = true,
                    realResourcesTouched = false
                }, statusCode: StatusCodes.Status403Forbidden);
            }

            return Results.Ok(new
            {
                index = governanceStore.BuildIndexSummary(),
                permissionDecision = endpointDecision,
                metadataOnly = true,
                realResourcesTouched = false
            });
        });

        app.MapGet("/api/settings/runtime-preview-pilot/governance/export", (
            [FromServices] RuntimePreviewGovernanceStore governanceStore,
            [FromServices] RuntimePreviewPermissionBroker permissionBroker,
            HttpContext httpContext) =>
        {
            var endpointDecision = permissionBroker.EvaluateEndpointAccess(
                "runtime-preview-pilot-governance-export",
                IsAdmin(httpContext),
                IsDeveloperUiRequested(httpContext));
            if (!endpointDecision.Allowed)
            {
                return Results.Json(new
                {
                    error = endpointDecision.ReasonCode,
                    permissionDecision = endpointDecision,
                    metadataOnly = true,
                    realResourcesTouched = false
                }, statusCode: StatusCodes.Status403Forbidden);
            }

            return Results.Ok(new
            {
                export = governanceStore.ExportManifest(),
                permissionDecision = endpointDecision,
                metadataOnly = true,
                realResourcesTouched = false
            });
        });

        app.MapGet("/api/settings/runtime-preview-pilot/governance/lookup", (
            string? sessionId,
            string? reportId,
            string? caseId,
            string? manifestId,
            string? reviewId,
            string? stationProfileId,
            string? operatorType,
            [FromServices] RuntimePreviewSessionStore sessionStore,
            [FromServices] RuntimePreviewReportArchive reportArchive,
            [FromServices] RuntimePreviewScenarioCorpusService scenarioCorpusService,
            [FromServices] RuntimePreviewRedactedFlowCorpusService redactedFlowCorpusService,
            [FromServices] RuntimePreviewStationProfileCatalog stationProfileCatalog,
            [FromServices] RuntimePreviewOperatorContractRegistry operatorContractRegistry,
            [FromServices] RuntimePreviewPermissionBroker permissionBroker,
            HttpContext httpContext) =>
        {
            var endpointDecision = permissionBroker.EvaluateEndpointAccess(
                "runtime-preview-pilot-governance-lookup",
                IsAdmin(httpContext),
                IsDeveloperUiRequested(httpContext));
            if (!endpointDecision.Allowed)
            {
                return Results.Json(new
                {
                    error = endpointDecision.ReasonCode,
                    permissionDecision = endpointDecision,
                    metadataOnly = true,
                    realResourcesTouched = false
                }, statusCode: StatusCodes.Status403Forbidden);
            }

            var corpus = scenarioCorpusService.BuildCorpus();
            var normalizedSessionId = string.IsNullOrWhiteSpace(sessionId) ? string.Empty : sessionId.Trim();
            var normalizedReportId = string.IsNullOrWhiteSpace(reportId) ? string.Empty : reportId.Trim();
            var normalizedCaseId = string.IsNullOrWhiteSpace(caseId) ? string.Empty : caseId.Trim();
            var normalizedManifestId = string.IsNullOrWhiteSpace(manifestId) ? string.Empty : manifestId.Trim();
            var normalizedReviewId = string.IsNullOrWhiteSpace(reviewId) ? string.Empty : reviewId.Trim();
            var normalizedStationProfileId = string.IsNullOrWhiteSpace(stationProfileId) ? string.Empty : stationProfileId.Trim();
            var normalizedOperatorType = string.IsNullOrWhiteSpace(operatorType) ? string.Empty : operatorType.Trim();
            var redactedCorpus = redactedFlowCorpusService.BuildCorpus();
            var stationProfiles = stationProfileCatalog.BuildProfiles();
            var registry = operatorContractRegistry.BuildRegistry();
            var coverage = operatorContractRegistry.BuildCoverageReport();
            var result = new
            {
                session = string.IsNullOrWhiteSpace(normalizedSessionId) ? null : sessionStore.Get(normalizedSessionId),
                sessionReport = string.IsNullOrWhiteSpace(normalizedSessionId) ? null : reportArchive.GetBySessionId(normalizedSessionId),
                deployReadinessReport = string.IsNullOrWhiteSpace(normalizedSessionId) ? null : reportArchive.GetDeployReadinessReportBySessionId(normalizedSessionId),
                packageReadinessReport = string.IsNullOrWhiteSpace(normalizedSessionId) ? null : reportArchive.GetPackageReadinessReportBySessionId(normalizedSessionId),
                manifestDryRunReport = string.IsNullOrWhiteSpace(normalizedSessionId) ? null : reportArchive.GetManifestDryRunReportBySessionId(normalizedSessionId),
                stationCompatibilityReport = string.IsNullOrWhiteSpace(normalizedSessionId) ? null : reportArchive.GetStationCompatibilityReportBySessionId(normalizedSessionId),
                operatorContractValidationReport = string.IsNullOrWhiteSpace(normalizedSessionId) ? null : reportArchive.GetOperatorContractValidationReportBySessionId(normalizedSessionId),
                preReleaseReviewReport = string.IsNullOrWhiteSpace(normalizedSessionId) ? null : reportArchive.GetPreReleaseReviewReportBySessionId(normalizedSessionId),
                report = string.IsNullOrWhiteSpace(normalizedReportId) ? null : reportArchive.Get(normalizedReportId),
                deployReport = string.IsNullOrWhiteSpace(normalizedReportId) ? null : reportArchive.GetDeployReadinessReport(normalizedReportId),
                packageReport = string.IsNullOrWhiteSpace(normalizedReportId) ? null : reportArchive.GetPackageReadinessReport(normalizedReportId),
                manifestReport = string.IsNullOrWhiteSpace(normalizedReportId) ? null : reportArchive.GetManifestDryRunReportByReportId(normalizedReportId),
                manifest = string.IsNullOrWhiteSpace(normalizedManifestId) ? null : reportArchive.GetManifestDryRunReport(normalizedManifestId),
                preReleaseReview = string.IsNullOrWhiteSpace(normalizedReviewId) ? null : reportArchive.GetPreReleaseReviewReport(normalizedReviewId),
                releaseReviewDecision = string.IsNullOrWhiteSpace(normalizedReviewId) ? null : reportArchive.GetReleaseReviewDecision(normalizedReviewId),
                preReleaseReviewByManifest = string.IsNullOrWhiteSpace(normalizedManifestId) ? null : reportArchive.GetPreReleaseReviewReportByManifestId(normalizedManifestId),
                releaseReviewDecisionByReport = string.IsNullOrWhiteSpace(normalizedReportId) ? null : reportArchive.GetReleaseReviewDecision(normalizedReportId),
                stationProfile = string.IsNullOrWhiteSpace(normalizedStationProfileId)
                    ? null
                    : stationProfiles.Profiles.FirstOrDefault(item => string.Equals(item.StationProfileId, normalizedStationProfileId, StringComparison.OrdinalIgnoreCase)),
                stationProfileReports = string.IsNullOrWhiteSpace(normalizedStationProfileId)
                    ? Array.Empty<RuntimePreviewStationCompatibilityReport>()
                    : reportArchive.GetStationCompatibilityReportsByStationProfileId(normalizedStationProfileId),
                operatorContract = string.IsNullOrWhiteSpace(normalizedOperatorType)
                    ? null
                    : registry.Contracts.FirstOrDefault(item => string.Equals(item.OperatorType, normalizedOperatorType, StringComparison.OrdinalIgnoreCase)),
                operatorContractCoverageReport = coverage,
                operatorContractValidationReportsByOperator = string.IsNullOrWhiteSpace(normalizedOperatorType)
                    ? Array.Empty<RuntimePreviewOperatorContractValidationReport>()
                    : reportArchive.ListOperatorContractValidationReports()
                        .Where(item => item.ContractResults.Any(resultItem => string.Equals(resultItem.OperatorType, normalizedOperatorType, StringComparison.OrdinalIgnoreCase)))
                        .ToArray(),
                corpusCase = string.IsNullOrWhiteSpace(normalizedCaseId)
                    ? null
                    : corpus.Cases.FirstOrDefault(item => string.Equals(item.CaseId, normalizedCaseId, StringComparison.OrdinalIgnoreCase)),
                redactedFlowCase = string.IsNullOrWhiteSpace(normalizedCaseId)
                    ? null
                    : redactedCorpus.Cases.FirstOrDefault(item => string.Equals(item.CaseId, normalizedCaseId, StringComparison.OrdinalIgnoreCase)),
                preReleaseReviewsByCase = string.IsNullOrWhiteSpace(normalizedCaseId)
                    ? Array.Empty<RuntimePreviewPreReleaseReviewReport>()
                    : reportArchive.GetPreReleaseReviewReportsByCaseId(normalizedCaseId)
            };
            return Results.Ok(new
            {
                lookup = result,
                permissionDecision = endpointDecision,
                metadataOnly = true,
                realResourcesTouched = false
            });
        });

        app.MapGet("/api/cameras/discover", async (ClearVision.Product.Core.Cameras.ICameraManager cameraManager) =>
        {
            var devices = await cameraManager.EnumerateCamerasAsync();
            return Results.Ok(devices);
        })
        .RequireClearVisionPermission(ClearVisionPermissionPolicies.CanOperateHardware);

        // 仅通过华睿 SDK 搜索在线相机
        app.MapGet("/api/cameras/discover/huaray", (ClearVision.Product.Core.Cameras.ICameraManager cameraManager) =>
        {
            var devices = CameraProviderFactory.DiscoverHuarayOnly();
            var mapped = MapDiscoveredDevices(devices, cameraManager).ToList();
            var diagnostics = BuildHuarayDiagnostics(mapped.Count);
            return Results.Ok(new { devices = mapped, diagnostics });
        })
        .RequireClearVisionPermission(ClearVisionPermissionPolicies.CanOperateHardware);

        // 仅通过海康 SDK 搜索在线相机
        app.MapGet("/api/cameras/discover/hikvision", (ClearVision.Product.Core.Cameras.ICameraManager cameraManager) =>
        {
            var devices = CameraProviderFactory.DiscoverHikvisionOnly();
            return Results.Ok(MapDiscoveredDevices(devices, cameraManager));
        })
        .RequireClearVisionPermission(ClearVisionPermissionPolicies.CanOperateHardware);

        // 获取已配置的相机绑定列表
        app.MapGet("/api/cameras/bindings", async (
            ClearVision.Product.Core.Cameras.ICameraManager cameraManager,
            IConfigurationService configService) =>
        {
            var bindings = cameraManager.GetBindings();
            var config = await configService.LoadAsync();
            config.Normalize();
            var discoveredDevices = await cameraManager.EnumerateCamerasAsync();
            var discoveredLookup = discoveredDevices.ToDictionary(
                device => device.CameraId,
                device => device,
                StringComparer.OrdinalIgnoreCase);

            var payload = bindings.Select(binding =>
            {
                binding.Normalize();
                var serialNumber = binding.SerialNumber?.Trim() ?? string.Empty;
                var runtimeCamera = string.IsNullOrWhiteSpace(serialNumber)
                    ? null
                    : cameraManager.GetCamera(serialNumber);
                var isDiscovered = !string.IsNullOrWhiteSpace(serialNumber)
                    && discoveredLookup.TryGetValue(serialNumber, out _);

                var connectionStatus = ResolveBindingConnectionStatus(binding, runtimeCamera, isDiscovered);

                return new
                {
                    binding.Id,
                    binding.DisplayName,
                    DeviceId = serialNumber,
                    binding.SerialNumber,
                    binding.IpAddress,
                    binding.Manufacturer,
                    binding.ModelName,
                    binding.InterfaceType,
                    binding.IsEnabled,
                    IsActive = string.Equals(binding.Id, config.ActiveCameraId, StringComparison.OrdinalIgnoreCase),
                    binding.ExposureTimeUs,
                    binding.GainDb,
                    binding.PixelFormat,
                    binding.TriggerMode,
                    binding.HardwareTriggerSource,
                    binding.SoftwareTriggerSource,
                    binding.EnterPhotoelectricDebounceMs,
                    binding.EnterPhotoelectricTimeoutMs,
                    binding.IgnoreEnterTriggerWhileBusy,
                    binding.EnterPhotoelectricDeviceId,
                    binding.SerialPhotoelectricPortName,
                    binding.SerialPhotoelectricBaudRate,
                    binding.SerialPhotoelectricDebounceMs,
                    binding.SerialPhotoelectricTimeoutMs,
                    binding.IgnoreSerialPhotoelectricTriggerWhileBusy,
                    binding.TargetFrameRateFps,
                    ConnectionStatus = connectionStatus
                };
            });

            return Results.Ok(payload);
        })
        .RequireClearVisionPermission(ClearVisionPermissionPolicies.CanOperateHardware);

        // 更新相机绑定配置
        app.MapPut("/api/cameras/bindings", async (
            ClearVision.Product.Application.DTOs.UpdateCameraBindingsRequest request,
            ClearVision.Product.Core.Cameras.ICameraManager cameraManager,
            [FromServices] ICameraFrameStreamCoordinator streamCoordinator,
            [FromServices] ISerialPhotoelectricTriggerInputService serialPhotoelectricTriggerInputService,
            IConfigurationService configService,
            HttpContext context) =>
        {
            if (!ClearVisionPermissionPolicies.IsEngineerOrAdmin(context))
            {
                return Results.Json(new { error = "HardwareOperationPermissionRequired" }, statusCode: StatusCodes.Status403Forbidden);
            }

            try
            {
                // 1. 更新 CameraManager 内存状态
                var existingBindings = NormalizeBindings(CloneBindings(cameraManager.GetBindings()));
                var normalizedBindings = NormalizeBindings(request.Bindings);
                var changedActiveConflicts = normalizedBindings
                    .Select(binding =>
                    {
                        var existing = existingBindings.FirstOrDefault(item =>
                            item.Id.Equals(binding.Id, StringComparison.OrdinalIgnoreCase));
                        var usage = SnapshotStreamUsageOrDefault(streamCoordinator, binding.Id);
                        return new { binding, existing, usage };
                    })
                    .Where(item =>
                        item.existing != null &&
                        item.usage.IsRunning &&
                        HasRuntimeCameraSettingsChanged(item.existing, item.binding))
                    .Select(item => new
                    {
                        CameraBindingId = item.binding.Id,
                        item.binding.DisplayName,
                        item.usage.LeaseCount,
                        item.usage.PreviewSessionCount,
                        item.usage.PendingFrameWaiters,
                        TriggerMode = item.usage.TriggerMode.ToConfigValue()
                    })
                    .ToList();

                var removedActiveConflicts = existingBindings
                    .Where(existing => !normalizedBindings.Any(binding =>
                        binding.Id.Equals(existing.Id, StringComparison.OrdinalIgnoreCase)))
                    .Select(existing => new
                    {
                        binding = existing,
                        usage = SnapshotStreamUsageOrDefault(streamCoordinator, existing.Id)
                    })
                    .Where(item => item.usage.IsRunning)
                    .Select(item => new
                    {
                        CameraBindingId = item.binding.Id,
                        item.binding.DisplayName,
                        item.usage.LeaseCount,
                        item.usage.PreviewSessionCount,
                        item.usage.PendingFrameWaiters,
                        TriggerMode = item.usage.TriggerMode.ToConfigValue()
                    })
                    .ToList();

                var activeConflicts = changedActiveConflicts
                    .Concat(removedActiveConflicts)
                    .ToList();

                if (activeConflicts.Count > 0)
                {
                    return Results.Conflict(new
                    {
                        Error = "相机流正在运行，不能直接保存会影响采集的相机参数。请先停止预览或检测，再保存。",
                        ActiveStreams = activeConflicts
                    });
                }

                cameraManager.UpdateBindings(normalizedBindings, request.ActiveCameraId);

                // 2. 持久化到 AppConfig
                var config = await configService.LoadAsync();
                config.Cameras = normalizedBindings;
                config.ActiveCameraId = request.ActiveCameraId;
                await configService.SaveAsync(config);
                if (serialPhotoelectricTriggerInputService is SerialPhotoelectricTriggerInputService serialPhotoelectricTriggerInput)
                {
                    serialPhotoelectricTriggerInput.ConfigureBindings(normalizedBindings);
                }

                return Results.Ok(new { Message = "相机配置已保存" });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { Error = ex.Message });
            }
        })
        .RequireClearVisionPermission(ClearVisionPermissionPolicies.CanOperateHardware);

        // 使用绑定参数执行手动软触发抓图（仅用于预览）
        app.MapPost("/api/cameras/soft-trigger-capture", async (
            CameraSoftTriggerCaptureRequest request,
            HttpContext context,
            [FromServices]
            ClearVision.Product.Core.Cameras.ICameraManager cameraManager,
            [FromServices]
            ITriggerInputService triggerInputService,
            [FromServices]
            ISerialPhotoelectricTriggerInputService serialPhotoelectricTriggerInputService) =>
        {
            if (string.IsNullOrWhiteSpace(request.CameraBindingId))
            {
                return Results.BadRequest(new { Error = "CameraBindingId is required." });
            }

            try
            {
                var binding = cameraManager.GetBindings()
                    .FirstOrDefault(b => b.Id.Equals(request.CameraBindingId, StringComparison.OrdinalIgnoreCase));
                if (binding == null)
                {
                    return Results.NotFound(new { Error = $"Camera binding not found: {request.CameraBindingId}" });
                }

                binding.Normalize();
                if (CameraTriggerModeExtensions.Normalize(binding.TriggerMode) != CameraTriggerMode.Software)
                {
                    return Results.BadRequest(new
                    {
                        Error = "当前相机绑定不是 Software 触发模式，请使用共享帧流预览/取图接口。"
                    });
                }

                var camera = await cameraManager.GetOrCreateByBindingAsync(request.CameraBindingId);

                await camera.SetExposureTimeAsync(binding.ExposureTimeUs);
                await camera.SetGainAsync(binding.GainDb);
                if (camera is IIndustrialCamera industrialCamera)
                {
                    await industrialCamera.SetPixelFormatAsync(CameraPixelFormatExtensions.Normalize(binding.PixelFormat));
                }

                if (binding.UsesEnterPhotoelectricTrigger())
                {
                    var triggerOptions = binding.ToEnterPhotoelectricTriggerOptions() with
                    {
                        AcceptPendingSignalsAfterUtc = NormalizeUtc(request.AcceptPendingEnterSignalAfterUtc)
                    };

                    await triggerInputService.WaitForEnterPhotoelectricAsync(
                        triggerOptions,
                        context.RequestAborted);
                }
                else if (binding.UsesSerialPhotoelectricTrigger())
                {
                    var triggerOptions = binding.ToSerialPhotoelectricTriggerOptions() with
                    {
                        AcceptPendingSignalsAfterUtc = NormalizeUtc(request.AcceptPendingEnterSignalAfterUtc)
                    };

                    await serialPhotoelectricTriggerInputService.WaitForSerialPhotoelectricAsync(
                        triggerOptions,
                        context.RequestAborted);
                }

                // 软触发采图序列已内聚在 AcquireSingleFrameAsync 中
                // （切到软件触发 → 复用或启动采集流 → ExecuteSoftwareTrigger → GetFrame；仅在 SDK 不允许热切模式时兜底停启）

                var frameBytes = await camera.AcquireSingleFrameAsync();
                if (!TryReadPngDimensions(frameBytes, out var width, out var height))
                {
                    return Results.BadRequest(new { Error = "Camera frame metadata parse failed." });
                }

                context.Response.Headers["X-Image-Width"] = width.ToString();
                context.Response.Headers["X-Image-Height"] = height.ToString();
                context.Response.Headers["X-Camera-Id"] = request.CameraBindingId;
                context.Response.Headers["X-Trigger-Mode"] = "Software";
                context.Response.Headers["X-Trigger-Source"] = binding.SoftwareTriggerSource;

                return Results.File(
                    frameBytes,
                    contentType: "image/png",
                    fileDownloadName: null);
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { Error = ex.Message });
            }
        })
        .RequireClearVisionPermission(ClearVisionPermissionPolicies.CanOperateHardware);

        app.MapGet("/api/trigger-input/diagnostics", ([FromServices] ITriggerInputService triggerInputService) =>
        {
            return Results.Ok(triggerInputService.GetDiagnostics());
        })
        .RequireClearVisionPermission(ClearVisionPermissionPolicies.CanOperateHardware);

        app.MapGet("/api/trigger-input/serial-photoelectric-ports", () =>
        {
            return Results.Ok(BuildSerialPhotoelectricPortList());
        })
        .RequireClearVisionPermission(ClearVisionPermissionPolicies.CanOperateHardware);

        app.MapPost("/api/trigger-input/test-serial-photoelectric", async (
            SerialPhotoelectricTestRequest request,
            [FromServices]
            ISerialPhotoelectricTriggerInputService serialPhotoelectricTriggerInputService,
            HttpContext context) =>
        {
            var timeoutMs = Math.Clamp(request.TimeoutMs <= 0 ? 10000 : request.TimeoutMs, 1000, 60000);
            var debounceMs = CameraSoftwareTriggerSourceExtensions.NormalizeSerialPhotoelectricDebounceMs(request.DebounceMs);
            var baudRate = CameraSoftwareTriggerSourceExtensions.NormalizeSerialPhotoelectricBaudRate(request.BaudRate);
            var portName = (request.PortName ?? string.Empty).Trim().ToUpperInvariant();

            try
            {
                var result = await serialPhotoelectricTriggerInputService.WaitForSerialPhotoelectricAsync(
                    new SerialPhotoelectricTriggerOptions(
                        "settings-serial-photoelectric-test",
                        "Settings Serial Photoelectric Test",
                        portName,
                        baudRate,
                        debounceMs,
                        timeoutMs,
                        IgnoreWhileBusy: false)
                    {
                        AcceptPendingSignalsAfterUtc = DateTime.UtcNow
                    },
                    context.RequestAborted);

                return Results.Ok(new
                {
                    Message = "串口光电测试成功",
                    result.Source,
                    PortName = result.DeviceId,
                    result.TimestampUtc
                });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { Error = ex.Message });
            }
        })
        .RequireClearVisionPermission(ClearVisionPermissionPolicies.CanOperateHardware);

        app.MapPost("/api/trigger-input/learn-enter-device", async (
            TriggerDeviceLearnRequest request,
            [FromServices]
            ITriggerInputService triggerInputService,
            CancellationToken cancellationToken) =>
        {
            var timeoutMs = Math.Clamp(request.TimeoutMs <= 0 ? 10000 : request.TimeoutMs, 1000, 60000);
            try
            {
                var result = await triggerInputService.LearnEnterPhotoelectricDeviceAsync(
                    TimeSpan.FromMilliseconds(timeoutMs),
                    cancellationToken);
                return Results.Ok(result);
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { Error = ex.Message });
            }
        })
        .RequireClearVisionPermission(ClearVisionPermissionPolicies.CanOperateHardware);

        app.MapPost("/api/cameras/continuous-preview/start", async (
            CameraContinuousPreviewStartRequest request,
            [FromServices]
            ICameraFrameStreamCoordinator streamCoordinator) =>
        {
            if (string.IsNullOrWhiteSpace(request.CameraBindingId))
            {
                return Results.BadRequest(new { Error = "CameraBindingId is required." });
            }

            try
            {
                var session = await streamCoordinator.StartPreviewSessionAsync(request.CameraBindingId);
                return Results.Ok(new
                {
                    session.SessionId,
                    session.CameraBindingId,
                    TriggerMode = session.TriggerMode.ToConfigValue(),
                    session.TargetFrameRateFps
                });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { Error = ex.Message });
            }
        })
        .RequireClearVisionPermission(ClearVisionPermissionPolicies.CanOperateHardware);

        app.MapGet("/api/cameras/continuous-preview/frame/{sessionId}", async (
            string sessionId,
            HttpContext context,
            [FromServices]
            ICameraFrameStreamCoordinator streamCoordinator,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                return Results.BadRequest(new { Error = "SessionId is required." });
            }

            try
            {
                var frame = await streamCoordinator.WaitForPreviewFrameAsync(sessionId, cancellationToken);
                context.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
                context.Response.Headers.Pragma = "no-cache";
                context.Response.Headers.Expires = "0";
                context.Response.Headers["X-Image-Width"] = frame.Width.ToString();
                context.Response.Headers["X-Image-Height"] = frame.Height.ToString();
                context.Response.Headers["X-Camera-Id"] = frame.CameraBindingId;
                context.Response.Headers["X-Frame-Sequence"] = frame.Sequence.ToString();
                return Results.File(frame.ImageData, frame.ContentType, fileDownloadName: null);
            }
            catch (KeyNotFoundException ex)
            {
                return Results.NotFound(new { Error = ex.Message });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { Error = ex.Message });
            }
        })
        .RequireClearVisionPermission(ClearVisionPermissionPolicies.CanOperateHardware);

        app.MapPost("/api/cameras/continuous-preview/stop", async (
            CameraContinuousPreviewStopRequest request,
            [FromServices]
            ICameraFrameStreamCoordinator streamCoordinator) =>
        {
            if (string.IsNullOrWhiteSpace(request.SessionId))
            {
                return Results.BadRequest(new { Error = "SessionId is required." });
            }

            await streamCoordinator.StopPreviewSessionAsync(request.SessionId);
            return Results.Ok(new { Message = "Continuous preview session stopped." });
        })
        .RequireClearVisionPermission(ClearVisionPermissionPolicies.CanOperateHardware);

        return app;
    }

    private static AppConfig MergeSettingsUpdate(AppConfig currentConfig, JsonElement request)
    {
        if (request.ValueKind == JsonValueKind.Object && TryGetJsonProperty(request, "saveScope", out _))
        {
            ValidateScopedSettingsUpdate(request);
        }
        else if (request.ValueKind == JsonValueKind.Object)
        {
            // Legacy/full settings payloads keep their permissive shape, but known
            // AppConfig values must still be rejected before the save authority runs.
            ValidateLegacySettingsValues(request);
        }

        currentConfig ??= new AppConfig();
        currentConfig.Normalize();
        if (request.ValueKind != JsonValueKind.Object)
        {
            return currentConfig;
        }

        var incoming = JsonSerializer.Deserialize<AppConfig>(request.GetRawText(), SettingsJsonOptions) ?? new AppConfig();
        incoming.Normalize();
        var scope = TryGetJsonProperty(request, "saveScope", out var scopeElement)
            ? scopeElement.GetString()?.Trim()
            : null;

        if (ShouldMergeSection(request, scope, "general") &&
            TryGetJsonProperty(request, "general", out var generalElement) &&
            generalElement.ValueKind == JsonValueKind.Object)
        {
            MergeGeneralConfig(currentConfig.General, incoming.General, generalElement);
        }

        if (ShouldMergeSection(request, scope, "storage") &&
            TryGetJsonProperty(request, "storage", out var storageElement) &&
            storageElement.ValueKind == JsonValueKind.Object)
        {
            MergeStorageConfig(currentConfig.Storage, incoming.Storage, storageElement);
        }

        if (ShouldMergeSection(request, scope, "runtime") &&
            TryGetJsonProperty(request, "runtime", out var runtimeElement) &&
            runtimeElement.ValueKind == JsonValueKind.Object)
        {
            MergeRuntimeConfig(currentConfig.Runtime, incoming.Runtime, runtimeElement);
        }

        if (ShouldMergeSection(request, scope, "security", "users") &&
            TryGetJsonProperty(request, "security", out var securityElement) &&
            securityElement.ValueKind == JsonValueKind.Object)
        {
            MergeSecurityConfig(currentConfig.Security, incoming.Security, securityElement);
        }

        if (ShouldMergeSection(request, scope, "communication", "plc") &&
            TryGetJsonProperty(request, "communication", out _))
        {
            currentConfig.Communication = incoming.Communication;
        }

        if (ShouldMergeSection(request, scope, "tcpCommunication", "tcp") &&
            TryGetJsonProperty(request, "tcpCommunication", out _))
        {
            currentConfig.TcpCommunication = incoming.TcpCommunication;
        }

        if (ShouldMergeSection(request, scope, "features") &&
            TryGetJsonProperty(request, "features", out _))
        {
            currentConfig.Features = incoming.Features;
        }

        // Camera bindings are owned by /api/cameras/bindings; keep the existing protection for full settings PUT.
        currentConfig.Cameras = NormalizeBindings(CloneBindings(currentConfig.Cameras));
        currentConfig.ActiveCameraId = currentConfig.ActiveCameraId ?? string.Empty;
        currentConfig.Normalize();
        return currentConfig;
    }

    private static void ValidateScopedSettingsUpdate(JsonElement request)
    {
        if (!TryGetJsonProperty(request, "saveScope", out var scopeElement) ||
            scopeElement.ValueKind != JsonValueKind.String)
        {
            throw new ArgumentException("A scoped settings request must include a string saveScope.");
        }

        var scope = scopeElement.GetString()?.Trim().ToLowerInvariant();
        // Legacy Settings uses "users" for the security policy tab. Keep this
        // narrow alias while rejecting every other non-Next scope.
        var sectionName = scope switch
        {
            "general" => "general",
            "storage" => "storage",
            "runtime" => "runtime",
            "security" => "security",
            "users" => "security",
            _ => throw new ArgumentException("saveScope must be one of: general, storage, runtime, security.")
        };

        var duplicateTopLevel = request.EnumerateObject()
            .GroupBy(property => property.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateTopLevel != null)
        {
            throw new ArgumentException($"Scoped settings request contains duplicate top-level field '{duplicateTopLevel.Key}'.");
        }

        var sectionProperty = request.EnumerateObject()
            .FirstOrDefault(property => property.Name.Equals(sectionName, StringComparison.OrdinalIgnoreCase));
        if (sectionProperty.Value.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException($"Scoped settings request must include an object section named '{sectionName}'.");
        }

        var topLevelNames = request.EnumerateObject()
            .Select(property => property.Name)
            .ToArray();
        if (topLevelNames.Any(name =>
                !name.Equals("saveScope", StringComparison.OrdinalIgnoreCase) &&
                !name.Equals(sectionName, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException($"Scoped settings request for '{sectionName}' contains fields from another section.");
        }

        var section = sectionProperty.Value;
        var duplicateField = section.EnumerateObject()
            .GroupBy(property => property.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateField != null)
        {
            throw new ArgumentException($"Scoped settings section '{sectionName}' contains duplicate field '{duplicateField.Key}'.");
        }
        if (!section.EnumerateObject().Any())
        {
            throw new ArgumentException($"Scoped settings section '{sectionName}' must contain at least one field.");
        }

        foreach (var property in section.EnumerateObject())
        {
            var field = property.Name.ToLowerInvariant();
            switch (sectionName)
            {
                case "general":
                    ValidateGeneralScopedField(field, property.Value);
                    break;
                case "storage":
                    ValidateStorageScopedField(field, property.Value);
                    break;
                case "runtime":
                    ValidateRuntimeScopedField(field, property.Value);
                    break;
                case "security":
                    ValidateSecurityScopedField(field, property.Value);
                    break;
            }
        }
    }

    private static void ValidateLegacySettingsValues(JsonElement request)
    {
        if (TryGetJsonProperty(request, "general", out var general) && general.ValueKind == JsonValueKind.Object)
        {
            if (TryGetJsonProperty(general, "softwareTitle", out var title)) RequireNonEmptyString(title, "general.softwareTitle");
            if (TryGetJsonProperty(general, "theme", out var theme))
            {
                var normalized = RequireString(theme, "general.theme").Trim().ToLowerInvariant();
                if (normalized is not (GeneralConfig.ThemeDark or GeneralConfig.ThemeLight))
                    throw new ArgumentException("general.theme must be 'dark' or 'light'.");
            }
        }

        if (TryGetJsonProperty(request, "storage", out var storage) && storage.ValueKind == JsonValueKind.Object)
        {
            if (TryGetJsonProperty(storage, "imageSavePath", out var path)) RequireNonEmptyString(path, "storage.imageSavePath");
            if (TryGetJsonProperty(storage, "savePolicy", out var policy))
            {
                var normalized = RequireString(policy, "storage.savePolicy").Trim();
                if (!normalized.Equals("None", StringComparison.OrdinalIgnoreCase) &&
                    !normalized.Equals("NgOnly", StringComparison.OrdinalIgnoreCase) &&
                    !normalized.Equals("All", StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException("storage.savePolicy must be None, NgOnly or All.");
            }
            if (TryGetJsonProperty(storage, "retentionDays", out var retention)) RequireNonNegativeInteger(retention, "storage.retentionDays");
            if (TryGetJsonProperty(storage, "minFreeSpaceGb", out var freeSpace)) RequireNonNegativeInteger(freeSpace, "storage.minFreeSpaceGb");
        }

        if (TryGetJsonProperty(request, "runtime", out var runtime) && runtime.ValueKind == JsonValueKind.Object)
        {
            if (TryGetJsonProperty(runtime, "stopOnConsecutiveNg", out var stopOnNg)) RequireNonNegativeInteger(stopOnNg, "runtime.stopOnConsecutiveNg");
            if (TryGetJsonProperty(runtime, "missingMaterialTimeoutSeconds", out var timeout)) RequireNonNegativeInteger(timeout, "runtime.missingMaterialTimeoutSeconds");
        }

        if (TryGetJsonProperty(request, "security", out var security) && security.ValueKind == JsonValueKind.Object)
        {
            if (TryGetJsonProperty(security, "passwordMinLength", out var passwordMinLength) &&
                RequireInteger(passwordMinLength, "security.passwordMinLength") < 6)
                throw new ArgumentException("security.passwordMinLength cannot be less than 6.");
            if (TryGetJsonProperty(security, "loginFailureLockoutCount", out var lockoutCount) &&
                RequireInteger(lockoutCount, "security.loginFailureLockoutCount") < 1)
                throw new ArgumentException("security.loginFailureLockoutCount must be at least 1.");
        }
    }

    private static void ValidateGeneralScopedField(string field, JsonElement value)
    {
        switch (field)
        {
            case "softwaretitle":
                RequireNonEmptyString(value, "general.softwareTitle");
                return;
            case "theme":
                var theme = RequireString(value, "general.theme").Trim().ToLowerInvariant();
                if (theme is not (GeneralConfig.ThemeDark or GeneralConfig.ThemeLight))
                {
                    throw new ArgumentException("general.theme must be 'dark' or 'light'.");
                }
                return;
            case "autostart":
                RequireBoolean(value, "general.autoStart");
                return;
            default:
                throw new ArgumentException($"Unknown field '{field}' in scoped general settings.");
        }
    }

    private static void ValidateStorageScopedField(string field, JsonElement value)
    {
        switch (field)
        {
            case "imagesavepath":
                RequireNonEmptyString(value, "storage.imageSavePath");
                return;
            case "savepolicy":
                var policy = RequireString(value, "storage.savePolicy").Trim();
                if (policy is not ("None" or "NgOnly" or "All") &&
                    !policy.Equals("None", StringComparison.OrdinalIgnoreCase) &&
                    !policy.Equals("NgOnly", StringComparison.OrdinalIgnoreCase) &&
                    !policy.Equals("All", StringComparison.OrdinalIgnoreCase))
                {
                    throw new ArgumentException("storage.savePolicy must be None, NgOnly or All.");
                }
                return;
            case "retentiondays":
                RequireNonNegativeInteger(value, "storage.retentionDays");
                return;
            case "minfreespacegb":
                RequireNonNegativeInteger(value, "storage.minFreeSpaceGb");
                return;
            default:
                throw new ArgumentException($"Unknown field '{field}' in scoped storage settings.");
        }
    }

    private static void ValidateRuntimeScopedField(string field, JsonElement value)
    {
        switch (field)
        {
            case "autorun":
                RequireBoolean(value, "runtime.autoRun");
                return;
            case "stoponconsecutiveng":
                RequireNonNegativeInteger(value, "runtime.stopOnConsecutiveNg");
                return;
            case "missingmaterialtimeoutseconds":
                RequireNonNegativeInteger(value, "runtime.missingMaterialTimeoutSeconds");
                return;
            case "applyprotectionrules":
                RequireBoolean(value, "runtime.applyProtectionRules");
                return;
            case "runtimepreviewpilot":
                throw new ArgumentException("runtime.runtimePreviewPilot is developer-only and cannot be changed through generic settings.");
            default:
                throw new ArgumentException($"Unknown field '{field}' in scoped runtime settings.");
        }
    }

    private static void ValidateSecurityScopedField(string field, JsonElement value)
    {
        switch (field)
        {
            case "passwordminlength":
                var passwordMinLength = RequireInteger(value, "security.passwordMinLength");
                if (passwordMinLength < 6)
                {
                    throw new ArgumentException("security.passwordMinLength cannot be less than 6.");
                }
                return;
            case "sessiontimeoutminutes":
                throw new ArgumentException("security.sessionTimeoutMinutes is a historical read-only field and cannot be changed.");
            case "loginfailurelockoutcount":
                var lockoutCount = RequireInteger(value, "security.loginFailureLockoutCount");
                if (lockoutCount < 1)
                {
                    throw new ArgumentException("security.loginFailureLockoutCount must be at least 1.");
                }
                return;
            default:
                throw new ArgumentException($"Unknown field '{field}' in scoped security settings.");
        }
    }

    private static string RequireString(JsonElement value, string path)
    {
        if (value.ValueKind != JsonValueKind.String)
        {
            throw new ArgumentException($"{path} must be a string.");
        }

        return value.GetString() ?? string.Empty;
    }

    private static void RequireNonEmptyString(JsonElement value, string path)
    {
        if (string.IsNullOrWhiteSpace(RequireString(value, path)))
        {
            throw new ArgumentException($"{path} cannot be empty.");
        }
    }

    private static void RequireBoolean(JsonElement value, string path)
    {
        if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            throw new ArgumentException($"{path} must be a boolean.");
        }
    }

    private static int RequireInteger(JsonElement value, string path)
    {
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var result))
        {
            throw new ArgumentException($"{path} must be an integer.");
        }

        return result;
    }

    private static void RequireNonNegativeInteger(JsonElement value, string path)
    {
        if (RequireInteger(value, path) < 0)
        {
            throw new ArgumentException($"{path} cannot be negative.");
        }
    }

    private static bool ShouldMergeSection(JsonElement request, string? scope, string propertyName, params string[] scopeAliases)
    {
        if (!string.IsNullOrWhiteSpace(scope))
        {
            return string.Equals(scope, propertyName, StringComparison.OrdinalIgnoreCase) ||
                   scopeAliases.Any(alias => string.Equals(scope, alias, StringComparison.OrdinalIgnoreCase));
        }

        return TryGetJsonProperty(request, propertyName, out _);
    }

    private static void MergeGeneralConfig(GeneralConfig target, GeneralConfig source, JsonElement section)
    {
        if (TryGetJsonProperty(section, "softwareTitle", out _))
        {
            target.SoftwareTitle = source.SoftwareTitle;
        }

        if (TryGetJsonProperty(section, "theme", out _))
        {
            target.Theme = source.Theme;
        }

        if (TryGetJsonProperty(section, "autoStart", out _))
        {
            target.AutoStart = source.AutoStart;
        }
    }

    private static void MergeStorageConfig(StorageConfig target, StorageConfig source, JsonElement section)
    {
        if (TryGetJsonProperty(section, "imageSavePath", out _))
        {
            target.ImageSavePath = source.ImageSavePath;
        }

        if (TryGetJsonProperty(section, "savePolicy", out _))
        {
            target.SavePolicy = source.SavePolicy;
        }

        if (TryGetJsonProperty(section, "retentionDays", out _))
        {
            target.RetentionDays = source.RetentionDays;
        }

        if (TryGetJsonProperty(section, "minFreeSpaceGb", out _))
        {
            target.MinFreeSpaceGb = source.MinFreeSpaceGb;
        }
    }

    private static void MergeRuntimeConfig(RuntimeConfig target, RuntimeConfig source, JsonElement section)
    {
        if (TryGetJsonProperty(section, "autoRun", out _))
        {
            target.AutoRun = source.AutoRun;
        }

        if (TryGetJsonProperty(section, "stopOnConsecutiveNg", out _))
        {
            target.StopOnConsecutiveNg = source.StopOnConsecutiveNg;
        }

        if (TryGetJsonProperty(section, "missingMaterialTimeoutSeconds", out _))
        {
            target.MissingMaterialTimeoutSeconds = source.MissingMaterialTimeoutSeconds;
        }

        if (TryGetJsonProperty(section, "applyProtectionRules", out _))
        {
            target.ApplyProtectionRules = source.ApplyProtectionRules;
        }

        if (TryGetJsonProperty(section, "runtimePreviewPilot", out _))
        {
            target.RuntimePreviewPilot = source.RuntimePreviewPilot;
        }
    }

    private static void MergeSecurityConfig(SecurityConfig target, SecurityConfig source, JsonElement section)
    {
        if (TryGetJsonProperty(section, "passwordMinLength", out _))
        {
            target.PasswordMinLength = source.PasswordMinLength;
        }

        if (TryGetJsonProperty(section, "loginFailureLockoutCount", out _))
        {
            target.LoginFailureLockoutCount = source.LoginFailureLockoutCount;
        }
    }

    private static bool TryGetJsonProperty(JsonElement element, string propertyName, out JsonElement value)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            value = default;
            return false;
        }

        if (element.TryGetProperty(propertyName, out value))
        {
            return true;
        }

        foreach (var property in element.EnumerateObject())
        {
            if (property.NameEquals(propertyName) ||
                property.Name.Equals(propertyName, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static bool IsAdmin(HttpContext context) => ClearVisionPermissionPolicies.IsAdmin(context);

    private static object ToSafeSettingsResponse(AppConfig config)
    {
        config.Normalize();
        return new
        {
            safeSubset = true,
            config.Revision,
            general = new
            {
                config.General.SoftwareTitle,
                config.General.Theme
            }
        };
    }

    private static bool IsDatabaseMaintenanceClientError(Exception ex)
    {
        return ex is ArgumentException
            or InvalidOperationException
            or FileNotFoundException
            or DirectoryNotFoundException
            or InvalidDataException;
    }

    private static IResult BuildDatabaseMaintenanceError(Exception ex)
    {
        if (ex is FileNotFoundException fileNotFound)
        {
            return Results.NotFound(new
            {
                Error = fileNotFound.Message,
                FileName = fileNotFound.FileName
            });
        }

        return Results.BadRequest(new { Error = ex.Message });
    }

    private static bool IsDeveloperUiRequested(HttpContext context)
    {
        return context.Request.Headers.TryGetValue("X-CV-Developer-UI", out var value) &&
               value.Any(item => string.Equals(item, "true", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsSuccessfulAiHealthCheck(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return false;

        try
        {
            using var document = JsonDocument.Parse(content);
            var root = document.RootElement;
            return root.ValueKind == JsonValueKind.Object &&
                root.TryGetProperty("ok", out var ok) &&
                ok.ValueKind == JsonValueKind.True;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static async Task<AiModelTestConnectionResult> TestAiModelConnectionAsync(
        AiModelConfig model,
        AiApiClient apiClient,
        CancellationToken cancellationToken)
    {
        var protocol = AiModelConfig.NormalizeProtocol(model.Protocol, model.Provider);
        var wireApi = AiModelConfig.NormalizeWireApi(model.WireApi);
        var authMode = AiModelConfig.NormalizeAuthMode(model.AuthMode, protocol);
        var timeoutMs = Math.Clamp(model.TimeoutMs <= 0 ? 120_000 : model.TimeoutMs, 1_000, 300_000);

        if (authMode != AiModelConfig.AuthModeNone && string.IsNullOrWhiteSpace(model.ApiKey))
        {
            return BuildAiModelConnectionResult(
                false,
                null,
                "missing_api_key",
                0,
                "API key is required for this auth mode.",
                model.Provider,
                model.Model,
                protocol,
                wireApi);
        }

        if (!string.IsNullOrWhiteSpace(model.BaseUrl) &&
            !Uri.TryCreate(model.BaseUrl, UriKind.Absolute, out _))
        {
            return BuildAiModelConnectionResult(
                false,
                null,
                "base_url_error",
                0,
                "BaseUrl is not a valid absolute URL.",
                model.Provider,
                model.Model,
                protocol,
                wireApi);
        }

        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromMilliseconds(timeoutMs));
            var options = model.ToGenerationOptions();
            options.MaxRetries = 0;
            options.MaxTokens = 32;
            options.Temperature = 0;

            var response = await apiClient.StreamCompleteAsync(
                "You are a connection health-check assistant. Respond only with valid JSON.",
                new List<ChatMessage> { new("user", "Reply with a JSON object exactly: {\"ok\": true}") },
                _ => { },
                options,
                timeoutCts.Token);

            stopwatch.Stop();
            if (!IsSuccessfulAiHealthCheck(response.Content))
            {
                return BuildAiModelConnectionResult(
                    false,
                    null,
                    "bad_response",
                    Elapsed(stopwatch),
                    "AI response did not match the expected JSON health-check shape; 不是预期的 JSON health-check 响应.",
                    model.Provider,
                    model.Model,
                    protocol,
                    wireApi);
            }

            return BuildAiModelConnectionResult(
                true,
                200,
                "ok",
                Elapsed(stopwatch),
                "Connection succeeded.",
                model.Provider,
                model.Model,
                protocol,
                wireApi);
        }
        catch (HttpRequestException ex)
        {
            stopwatch.Stop();
            var statusCode = ex.StatusCode.HasValue ? (int?)ex.StatusCode.Value : null;
            return BuildAiModelConnectionResult(
                false,
                statusCode,
                ClassifyHttpConnectionError(statusCode),
                Elapsed(stopwatch),
                AiSecretSanitizer.RedactException(ex),
                model.Provider,
                model.Model,
                protocol,
                wireApi);
        }
        catch (Exception ex) when (ex is TaskCanceledException or OperationCanceledException)
        {
            stopwatch.Stop();
            return BuildAiModelConnectionResult(
                false,
                null,
                "timeout",
                Elapsed(stopwatch),
                "Connection test timed out.",
                model.Provider,
                model.Model,
                protocol,
                wireApi);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return BuildAiModelConnectionResult(
                false,
                null,
                ClassifyNonHttpConnectionError(ex),
                Elapsed(stopwatch),
                AiSecretSanitizer.RedactException(ex),
                model.Provider,
                model.Model,
                protocol,
                wireApi);
        }
    }

    private static AiModelTestConnectionResult BuildAiModelConnectionResult(
        bool connectionOk,
        int? statusCode,
        string errorCode,
        int latencyMs,
        string sanitizedMessage,
        string provider,
        string modelName,
        string protocol,
        string wireApi)
    {
        var safeMessage = AiSecretSanitizer.Redact(sanitizedMessage);
        return new AiModelTestConnectionResult
        {
            ConnectionOk = connectionOk,
            Success = connectionOk,
            StatusCode = statusCode,
            ErrorCode = errorCode,
            LatencyMs = latencyMs,
            SanitizedMessage = safeMessage,
            Message = safeMessage,
            Provider = provider,
            ModelName = modelName,
            Protocol = protocol,
            WireApi = wireApi
        };
    }

    private static string ClassifyHttpConnectionError(int? statusCode)
    {
        return statusCode switch
        {
            401 or 403 => "auth_failed",
            404 => "model_not_found",
            >= 500 => "provider_error",
            _ => "http_error"
        };
    }

    private static string ClassifyNonHttpConnectionError(Exception exception)
    {
        var message = exception.Message;
        if (exception is UriFormatException ||
            message.Contains("url", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("uri", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("baseurl", StringComparison.OrdinalIgnoreCase))
        {
            return "base_url_error";
        }

        return "response_format_error";
    }

    private static int Elapsed(Stopwatch stopwatch)
    {
        return (int)Math.Clamp(stopwatch.ElapsedMilliseconds, 0, int.MaxValue);
    }

    private static AiApiKeyUpdateMode ResolveApiKeyUpdateMode(string? apiKeyOperation, string? apiKey)
    {
        var normalized = string.IsNullOrWhiteSpace(apiKeyOperation)
            ? (string.IsNullOrWhiteSpace(apiKey) ? "keep" : "replace")
            : apiKeyOperation.Trim().ToLowerInvariant().Replace("_", "-");

        return normalized switch
        {
            "clear" => AiApiKeyUpdateMode.Clear,
            "replace" => AiApiKeyUpdateMode.Replace,
            "new" => AiApiKeyUpdateMode.Replace,
            "keep" => AiApiKeyUpdateMode.Keep,
            _ => string.IsNullOrWhiteSpace(apiKey) ? AiApiKeyUpdateMode.Keep : AiApiKeyUpdateMode.Replace
        };
    }

    private static object ToSafeAiModelResponse(AiModelConfig m) => new
    {
        m.Id,
        displayName = string.IsNullOrWhiteSpace(m.DisplayName) ? m.Name : m.DisplayName,
        m.Provider,
        m.Model,
        m.ModelRole,
        m.IsEnabled,
        m.IsActive,
        m.Capabilities
    };

    private static object ToAiModelResponse(AiModelConfig m) => new
    {
        m.Id,
        m.Name,
        m.DisplayName,
        m.Provider,
        hasApiKey = !string.IsNullOrWhiteSpace(m.ApiKey),
        apiKeyMasked = AiSecretSanitizer.MaskApiKey(!string.IsNullOrWhiteSpace(m.ApiKey)),
        m.Model,
        baseUrl = AiSecretSanitizer.Redact(m.BaseUrl ?? ""),
        m.TimeoutMs,
        m.IsActive,
        m.IsEnabled,
        m.Protocol,
        m.WireApi,
        m.AuthMode,
        m.AuthHeaderName,
        ExtraHeaders = MaskSensitiveStringMap(m.ExtraHeaders),
        ExtraQuery = MaskSensitiveStringMap(m.ExtraQuery),
        ExtraBody = MaskSensitiveJsonMap(m.ExtraBody),
        m.RoleBindings,
        m.ModelRole,
        m.Priority,
        m.Remark,
        m.CreatedAt,
        m.UpdatedAt,
        m.LastTestStatus,
        m.LastTestAt,
        m.LastTestLatencyMs,
        m.Capabilities,
        m.Reasoning,
        ReasoningSupport = m.GetReasoningSupport()
    };

    private static Dictionary<string, string>? MaskSensitiveStringMap(Dictionary<string, string>? values)
    {
        if (values == null)
        {
            return null;
        }

        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in values)
        {
            result[item.Key] = IsSensitiveAiConfigKey(item.Key)
                ? "<redacted>"
                : AiSecretSanitizer.Redact(item.Value);
        }

        return result;
    }

    private static Dictionary<string, JsonElement>? MaskSensitiveJsonMap(Dictionary<string, JsonElement>? values)
    {
        if (values == null)
        {
            return null;
        }

        var result = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in values)
        {
            result[item.Key] = IsSensitiveAiConfigKey(item.Key)
                ? JsonSerializer.SerializeToElement("<redacted>")
                : RedactJsonElement(item.Value);
        }

        return result;
    }

    private static JsonElement RedactJsonElement(JsonElement value)
    {
        var redactedRaw = AiSecretSanitizer.Redact(value.GetRawText());
        try
        {
            return JsonSerializer.Deserialize<JsonElement>(redactedRaw).Clone();
        }
        catch (JsonException)
        {
            return JsonSerializer.SerializeToElement(AiSecretSanitizer.Redact(value.ToString()));
        }
    }

    private static bool IsSensitiveAiConfigKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        var normalized = key.Trim()
            .Replace("_", "-", StringComparison.Ordinal)
            .ToLowerInvariant();
        return normalized is "authorization"
            or "x-api-key"
            or "api-key"
            or "apikey"
            or "api-key-id"
            or "token"
            or "access-token"
            or "refresh-token"
            or "secret"
            or "client-secret"
            or "password"
            or "signature"
            or "sig" ||
            normalized.Contains("token", StringComparison.Ordinal) ||
            normalized.Contains("secret", StringComparison.Ordinal) ||
            normalized.Contains("password", StringComparison.Ordinal) ||
            normalized.Contains("api-key", StringComparison.Ordinal) ||
            normalized.Contains("apikey", StringComparison.Ordinal);
    }

    private static IEnumerable<CameraInfo> MapDiscoveredDevices(
        IEnumerable<CameraDeviceInfo> devices,
        ClearVision.Product.Core.Cameras.ICameraManager cameraManager)
    {
        return devices.Select(d => new CameraInfo
        {
            CameraId = d.SerialNumber,
            Name = string.IsNullOrEmpty(d.UserDefinedName) ? d.Model : d.UserDefinedName,
            IpAddress = d.IpAddress,
            Manufacturer = d.Manufacturer,
            Model = d.Model,
            ConnectionType = d.InterfaceType,
            IsConnected = cameraManager.GetCamera(d.SerialNumber) != null
        });
    }

    private static string ResolveBindingConnectionStatus(
        CameraBindingConfig binding,
        ICamera? runtimeCamera,
        bool isDiscovered)
    {
        if (!binding.IsEnabled)
        {
            return "Disabled";
        }

        if (string.IsNullOrWhiteSpace(binding.SerialNumber))
        {
            return "Unbound";
        }

        if (runtimeCamera?.IsConnected == true)
        {
            return "Connected";
        }

        return isDiscovered ? "Online" : "Offline";
    }

    private static List<CameraBindingConfig> NormalizeBindings(IEnumerable<CameraBindingConfig>? bindings)
    {
        return (bindings ?? Enumerable.Empty<CameraBindingConfig>())
            .Select(binding =>
            {
                binding.Normalize();
                return binding;
            })
            .ToList();
    }

    private static IEnumerable<CameraBindingConfig> CloneBindings(IEnumerable<CameraBindingConfig>? bindings)
    {
        return (bindings ?? Enumerable.Empty<CameraBindingConfig>()).Select(binding => new CameraBindingConfig
        {
            Id = binding.Id,
            DisplayName = binding.DisplayName,
            SerialNumber = binding.SerialNumber,
            IpAddress = binding.IpAddress,
            Manufacturer = binding.Manufacturer,
            ModelName = binding.ModelName,
            InterfaceType = binding.InterfaceType,
            IsEnabled = binding.IsEnabled,
            ExposureTimeUs = binding.ExposureTimeUs,
            GainDb = binding.GainDb,
            PixelFormat = binding.PixelFormat,
            TriggerMode = binding.TriggerMode,
            HardwareTriggerSource = binding.HardwareTriggerSource,
            SoftwareTriggerSource = binding.SoftwareTriggerSource,
            EnterPhotoelectricDebounceMs = binding.EnterPhotoelectricDebounceMs,
            EnterPhotoelectricTimeoutMs = binding.EnterPhotoelectricTimeoutMs,
            IgnoreEnterTriggerWhileBusy = binding.IgnoreEnterTriggerWhileBusy,
            EnterPhotoelectricDeviceId = binding.EnterPhotoelectricDeviceId,
            SerialPhotoelectricPortName = binding.SerialPhotoelectricPortName,
            SerialPhotoelectricBaudRate = binding.SerialPhotoelectricBaudRate,
            SerialPhotoelectricDebounceMs = binding.SerialPhotoelectricDebounceMs,
            SerialPhotoelectricTimeoutMs = binding.SerialPhotoelectricTimeoutMs,
            IgnoreSerialPhotoelectricTriggerWhileBusy = binding.IgnoreSerialPhotoelectricTriggerWhileBusy,
            TargetFrameRateFps = binding.TargetFrameRateFps,
            ContinuousInspection = CloneContinuousInspection(binding.ContinuousInspection)
        });
    }

    private static CameraStreamUsageSnapshot SnapshotStreamUsageOrDefault(
        ICameraFrameStreamCoordinator streamCoordinator,
        string cameraBindingId)
    {
        return streamCoordinator.SnapshotStreamUsage(cameraBindingId)
            ?? new CameraStreamUsageSnapshot(
                cameraBindingId,
                false,
                0,
                0,
                0,
                CameraTriggerMode.Software,
                CameraTriggerModeExtensions.DefaultTargetFrameRateFps);
    }

    private static ContinuousInspectionConfig CloneContinuousInspection(ContinuousInspectionConfig? config)
    {
        config ??= new ContinuousInspectionConfig();
        return new ContinuousInspectionConfig
        {
            Mode = config.Mode,
            HardwareProfile = config.HardwareProfile,
            TargetFps = config.TargetFps,
            BufferCapacity = config.BufferCapacity,
            DetectEveryNFrames = config.DetectEveryNFrames,
            PreEventFrames = config.PreEventFrames,
            PostEventFrames = config.PostEventFrames,
            MinConsensusFrames = config.MinConsensusFrames,
            ConsensusThreshold = config.ConsensusThreshold,
            SchedulerQueueLength = config.SchedulerQueueLength,
            MaxLatencyMs = config.MaxLatencyMs,
            SaveReplayOnNgOnly = config.SaveReplayOnNgOnly,
            ShadowOutputDisabled = config.ShadowOutputDisabled
        };
    }

    private static bool HasRuntimeCameraSettingsChanged(CameraBindingConfig previous, CameraBindingConfig next)
    {
        previous.Normalize();
        next.Normalize();
        var previousMode = CameraTriggerModeExtensions.Normalize(previous.TriggerMode);
        var nextMode = CameraTriggerModeExtensions.Normalize(next.TriggerMode);

        return !string.Equals(previous.SerialNumber, next.SerialNumber, StringComparison.OrdinalIgnoreCase)
            || Math.Abs(previous.ExposureTimeUs - next.ExposureTimeUs) > 0.001
            || Math.Abs(previous.GainDb - next.GainDb) > 0.001
            || !string.Equals(previous.PixelFormat, next.PixelFormat, StringComparison.OrdinalIgnoreCase)
            || previousMode != nextMode
            || !string.Equals(previous.SoftwareTriggerSource, next.SoftwareTriggerSource, StringComparison.OrdinalIgnoreCase)
            || previous.EnterPhotoelectricDebounceMs != next.EnterPhotoelectricDebounceMs
            || previous.EnterPhotoelectricTimeoutMs != next.EnterPhotoelectricTimeoutMs
            || previous.IgnoreEnterTriggerWhileBusy != next.IgnoreEnterTriggerWhileBusy
            || !string.Equals(previous.EnterPhotoelectricDeviceId, next.EnterPhotoelectricDeviceId, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(previous.SerialPhotoelectricPortName, next.SerialPhotoelectricPortName, StringComparison.OrdinalIgnoreCase)
            || previous.SerialPhotoelectricBaudRate != next.SerialPhotoelectricBaudRate
            || previous.SerialPhotoelectricDebounceMs != next.SerialPhotoelectricDebounceMs
            || previous.SerialPhotoelectricTimeoutMs != next.SerialPhotoelectricTimeoutMs
            || previous.IgnoreSerialPhotoelectricTriggerWhileBusy != next.IgnoreSerialPhotoelectricTriggerWhileBusy
            || ((previousMode == CameraTriggerMode.External || nextMode == CameraTriggerMode.External) &&
                !string.Equals(
                    CameraHardwareTriggerSourceExtensions.Normalize(previous.HardwareTriggerSource),
                    CameraHardwareTriggerSourceExtensions.Normalize(next.HardwareTriggerSource),
                    StringComparison.OrdinalIgnoreCase))
            || CameraTriggerModeExtensions.NormalizeTargetFrameRate(previous.TargetFrameRateFps)
                != CameraTriggerModeExtensions.NormalizeTargetFrameRate(next.TargetFrameRateFps)
            || HasContinuousStreamBufferSettingsChanged(previous.ContinuousInspection, next.ContinuousInspection);
    }

    private static bool HasContinuousStreamBufferSettingsChanged(
        ContinuousInspectionConfig? previous,
        ContinuousInspectionConfig? next)
    {
        previous ??= new ContinuousInspectionConfig();
        next ??= new ContinuousInspectionConfig();
        previous.Normalize();
        next.Normalize();
        return previous.BufferCapacity != next.BufferCapacity
            || previous.PreEventFrames != next.PreEventFrames
            || previous.PostEventFrames != next.PostEventFrames;
    }

    private static object BuildHuarayDiagnostics(int deviceCount)
    {
        var sdkLoaded = MindVisionCamera.IsSdkLoaded;
        var sdkPath = MindVisionCamera.SdkAssemblyLocation;
        var sdkLoadError = MindVisionCamera.LastSdkLoadError;
        var enumerateDetail = MindVisionCamera.LastEnumerateError;

        string message;
        if (deviceCount > 0)
        {
            message = $"华睿搜索成功，发现 {deviceCount} 台设备。";
        }
        else if (!sdkLoaded)
        {
            message = string.IsNullOrWhiteSpace(sdkLoadError)
                ? "华睿 SDK 未加载成功，请检查 MVSDK_Net.dll。"
                : $"华睿 SDK 未加载成功：{sdkLoadError}";
        }
        else if (!string.IsNullOrWhiteSpace(enumerateDetail))
        {
            message = $"华睿 SDK 已加载，但枚举结果为空：{enumerateDetail}";
        }
        else
        {
            message = "华睿 SDK 已加载，但未发现设备。请检查相机供电、网线与网卡网段。";
        }

        return new
        {
            sdkLoaded,
            sdkPath,
            sdkLoadError,
            enumerateDetail,
            message
        };
    }

    private static bool TryReadPngDimensions(byte[] pngBytes, out int width, out int height)
    {
        width = 0;
        height = 0;

        if (pngBytes.Length < 24)
        {
            return false;
        }

        ReadOnlySpan<byte> pngSignature = stackalloc byte[] { 137, 80, 78, 71, 13, 10, 26, 10 };
        ReadOnlySpan<byte> ihdrChunkType = stackalloc byte[] { 73, 72, 68, 82 };

        if (!pngBytes.AsSpan(0, 8).SequenceEqual(pngSignature))
        {
            return false;
        }

        if (!pngBytes.AsSpan(12, 4).SequenceEqual(ihdrChunkType))
        {
            return false;
        }

        width = BinaryPrimitives.ReadInt32BigEndian(pngBytes.AsSpan(16, 4));
        height = BinaryPrimitives.ReadInt32BigEndian(pngBytes.AsSpan(20, 4));
        return width > 0 && height > 0;
    }

    private static DateTime? NormalizeUtc(DateTime? value)
    {
        if (!value.HasValue)
        {
            return null;
        }

        return value.Value.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(value.Value, DateTimeKind.Utc)
            : value.Value.ToUniversalTime();
    }

    private static bool TryBuildDiskUsage(string? targetPath, out DiskUsageInfo usage, out string error)
    {
        usage = default!;
        error = string.Empty;

        try
        {
            var fullPath = string.IsNullOrWhiteSpace(targetPath)
                ? AppContext.BaseDirectory
                : Path.GetFullPath(targetPath.Trim());

            var rootPath = Path.GetPathRoot(fullPath);
            if (string.IsNullOrWhiteSpace(rootPath))
            {
                rootPath = Path.GetPathRoot(AppContext.BaseDirectory);
            }

            if (string.IsNullOrWhiteSpace(rootPath))
            {
                error = "无法解析磁盘根路径。";
                return false;
            }

            var drive = new DriveInfo(rootPath);
            if (!drive.IsReady)
            {
                error = $"磁盘不可用: {drive.Name}";
                return false;
            }

            var totalBytes = drive.TotalSize;
            var freeBytes = drive.AvailableFreeSpace;
            var usedBytes = totalBytes - freeBytes;
            var usedPercent = totalBytes > 0 ? usedBytes * 100.0 / totalBytes : 0;
            var isAccessible = Directory.Exists(fullPath);
            var canWrite = isAccessible
                ? CanWriteToDirectory(fullPath)
                : CanCreateDirectoryAt(fullPath);

            usage = new DiskUsageInfo(
                DriveName: drive.Name,
                SourcePath: fullPath,
                IsAccessible: isAccessible,
                CanWrite: canWrite,
                TotalBytes: totalBytes,
                UsedBytes: usedBytes,
                FreeBytes: freeBytes,
                TotalGb: Math.Round(totalBytes / 1024d / 1024d / 1024d, 2),
                UsedGb: Math.Round(usedBytes / 1024d / 1024d / 1024d, 2),
                FreeGb: Math.Round(freeBytes / 1024d / 1024d / 1024d, 2),
                UsedPercent: Math.Round(usedPercent, 2));
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private sealed record DiskUsageInfo(
        string DriveName,
        string SourcePath,
        bool IsAccessible,
        bool CanWrite,
        long TotalBytes,
        long UsedBytes,
        long FreeBytes,
        double TotalGb,
        double UsedGb,
        double FreeGb,
        double UsedPercent);

    private static bool CanWriteToDirectory(string directoryPath)
    {
        string? probePath = null;
        try
        {
            probePath = Path.Combine(directoryPath, $".cv-write-probe-{Guid.NewGuid():N}.tmp");
            using var stream = new FileStream(
                probePath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 1,
                FileOptions.DeleteOnClose);
            stream.WriteByte(0);
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(probePath))
            {
                try
                {
                    if (File.Exists(probePath))
                    {
                        File.Delete(probePath);
                    }
                }
                catch
                {
                    // Best-effort cleanup; the file is also opened with DeleteOnClose.
                }
            }
        }
    }

    private static bool CanCreateDirectoryAt(string directoryPath)
    {
        var nearestExistingDirectory = directoryPath;
        while (!string.IsNullOrWhiteSpace(nearestExistingDirectory) &&
               !Directory.Exists(nearestExistingDirectory))
        {
            if (File.Exists(nearestExistingDirectory))
            {
                return false;
            }

            nearestExistingDirectory = Path.GetDirectoryName(nearestExistingDirectory);
        }

        if (string.IsNullOrWhiteSpace(nearestExistingDirectory))
        {
            return false;
        }

        string? probePath = null;
        try
        {
            probePath = Path.Combine(nearestExistingDirectory, $".cv-dir-probe-{Guid.NewGuid():N}");
            Directory.CreateDirectory(probePath);
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(probePath))
            {
                try
                {
                    if (Directory.Exists(probePath))
                    {
                        Directory.Delete(probePath, recursive: true);
                    }
                }
                catch
                {
                    // Best-effort cleanup for the temporary directory probe.
                }
            }
        }
    }

    private static IReadOnlyList<SerialPhotoelectricPortInfo> BuildSerialPhotoelectricPortList()
    {
        var friendlyNames = ReadSerialPortFriendlyNamesFromRegistry();
        var portNames = SerialPort.GetPortNames()
            .Select(port => port.Trim().ToUpperInvariant())
            .Where(IsComPortName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(port => ExtractComPortNumber(port))
            .ThenBy(port => port, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var candidates = portNames
            .Select(port =>
            {
                var displayName = friendlyNames.TryGetValue(port, out var friendlyName)
                    ? friendlyName
                    : port;
                return new SerialPhotoelectricPortCandidate(
                    port,
                    displayName,
                    ScoreSerialPhotoelectricPort(displayName));
            })
            .ToArray();

        var recommended = candidates
            .Where(candidate => candidate.Score > 0)
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => ExtractComPortNumber(candidate.PortName))
            .FirstOrDefault();

        recommended ??= candidates.Length == 1
            ? candidates[0]
            : candidates
                .Where(candidate => !LooksLikeBluetoothSerialPort(candidate.DisplayName))
                .OrderBy(candidate => ExtractComPortNumber(candidate.PortName))
                .FirstOrDefault();

        var recommendedPortName = recommended?.PortName;
        return candidates
            .Select(candidate => new SerialPhotoelectricPortInfo(
                candidate.PortName,
                candidate.DisplayName,
                string.Equals(candidate.PortName, recommendedPortName, StringComparison.OrdinalIgnoreCase)))
            .ToArray();
    }

    private static Dictionary<string, string> ReadSerialPortFriendlyNamesFromRegistry()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var roots = new[]
        {
            @"SYSTEM\CurrentControlSet\Enum\USB",
            @"SYSTEM\CurrentControlSet\Enum\BTHENUM",
            @"SYSTEM\CurrentControlSet\Enum\FTDIBUS",
            @"SYSTEM\CurrentControlSet\Enum\SERENUM",
            @"SYSTEM\CurrentControlSet\Enum\ROOT"
        };

        foreach (var rootPath in roots)
        {
            try
            {
                using var root = Registry.LocalMachine.OpenSubKey(rootPath);
                if (root != null)
                {
                    ReadSerialPortFriendlyNamesFromRegistry(root, result, depth: 0);
                }
            }
            catch
            {
                // Registry access can fail for some device classes; other roots still provide useful matches.
            }
        }

        return result;
    }

    private static void ReadSerialPortFriendlyNamesFromRegistry(
        RegistryKey key,
        Dictionary<string, string> result,
        int depth)
    {
        if (depth > 8)
        {
            return;
        }

        try
        {
            if (key.GetValue("FriendlyName") is string friendlyName &&
                TryExtractComPortName(friendlyName, out var portName))
            {
                result[portName] = friendlyName;
            }

            foreach (var subKeyName in key.GetSubKeyNames())
            {
                try
                {
                    using var subKey = key.OpenSubKey(subKeyName);
                    if (subKey != null)
                    {
                        ReadSerialPortFriendlyNamesFromRegistry(subKey, result, depth + 1);
                    }
                }
                catch
                {
                    // Ignore individual device keys that cannot be opened.
                }
            }
        }
        catch
        {
            // Ignore registry branches that cannot be enumerated.
        }
    }

    private static bool TryExtractComPortName(string friendlyName, out string portName)
    {
        portName = string.Empty;
        var start = friendlyName.LastIndexOf("(COM", StringComparison.OrdinalIgnoreCase);
        if (start < 0)
        {
            return false;
        }

        var end = friendlyName.IndexOf(')', start);
        if (end <= start + 1)
        {
            return false;
        }

        var candidate = friendlyName.Substring(start + 1, end - start - 1).Trim().ToUpperInvariant();
        if (!IsComPortName(candidate))
        {
            return false;
        }

        portName = candidate;
        return true;
    }

    private static bool IsComPortName(string value)
    {
        if (value.Length <= 3 || !value.StartsWith("COM", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        for (var index = 3; index < value.Length; index++)
        {
            if (!char.IsDigit(value[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static int ExtractComPortNumber(string portName)
    {
        return int.TryParse(portName.AsSpan(3), out var number) ? number : int.MaxValue;
    }

    private static int ScoreSerialPhotoelectricPort(string displayName)
    {
        var normalized = displayName.ToLowerInvariant();
        if (LooksLikeBluetoothSerialPort(normalized))
        {
            return -100;
        }

        var score = 0;
        if (normalized.Contains("ch340", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("ch341", StringComparison.OrdinalIgnoreCase))
        {
            score += 120;
        }

        if (normalized.Contains("usb-serial", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("usb serial", StringComparison.OrdinalIgnoreCase))
        {
            score += 100;
        }

        if (normalized.Contains("cp210", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("ftdi", StringComparison.OrdinalIgnoreCase))
        {
            score += 90;
        }

        if (normalized.Contains("usb", StringComparison.OrdinalIgnoreCase))
        {
            score += 70;
        }

        if (normalized.Contains("serial", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("串行", StringComparison.OrdinalIgnoreCase))
        {
            score += 10;
        }

        return score;
    }

    private static bool LooksLikeBluetoothSerialPort(string displayName)
    {
        return displayName.Contains("bluetooth", StringComparison.OrdinalIgnoreCase) ||
               displayName.Contains("蓝牙", StringComparison.OrdinalIgnoreCase) ||
               displayName.Contains("bthenum", StringComparison.OrdinalIgnoreCase);
    }

    private static Dictionary<string, string>? CloneStringMap(Dictionary<string, string>? source)
    {
        if (source == null)
            return null;

        return source.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);
    }

    private static Dictionary<string, JsonElement>? CloneJsonMap(Dictionary<string, JsonElement>? source)
    {
        if (source == null)
            return null;

        var result = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in source)
        {
            result[kv.Key] = kv.Value.Clone();
        }

        return result;
    }

    private static List<string>? CloneStringList(List<string>? source)
    {
        if (source == null)
            return null;

        return new List<string>(source);
    }

    private static JsonElement? ExtractRuntimePreviewWorkflowDraft(RuntimePreviewPilotReadinessEndpointRequest request)
    {
        if (request.WorkflowDraft is { ValueKind: JsonValueKind.Object } workflowDraft)
        {
            return workflowDraft.Clone();
        }

        if (request.Arguments is { ValueKind: JsonValueKind.Object } arguments)
        {
            foreach (var propertyName in new[] { "flow", "workflowDraft", "existingFlowJson" })
            {
                if (arguments.TryGetProperty(propertyName, out var value) &&
                    value.ValueKind == JsonValueKind.Object)
                {
                    return value.Clone();
                }
            }
        }

        return null;
    }

    private static JsonElement BuildRuntimePreviewReadinessArguments(
        RuntimePreviewPilotReadinessEndpointRequest request,
        JsonElement? workflowDraft)
    {
        if (request.Arguments is { ValueKind: JsonValueKind.Object } arguments)
        {
            return arguments.Clone();
        }

        if (workflowDraft is { ValueKind: JsonValueKind.Object } flow)
        {
            return JsonSerializer.SerializeToElement(new
            {
                flow
            });
        }

        return JsonSerializer.SerializeToElement(new { });
    }

    private sealed class AiModelTestConnectionResult
    {
        public bool ConnectionOk { get; init; }
        public bool Success { get; init; }
        public int? StatusCode { get; init; }
        public string ErrorCode { get; init; } = string.Empty;
        public int LatencyMs { get; init; }
        public string SanitizedMessage { get; init; } = string.Empty;
        public string Message { get; init; } = string.Empty;
        public string Provider { get; init; } = string.Empty;
        public string ModelName { get; init; } = string.Empty;
        public string Protocol { get; init; } = string.Empty;
        public string WireApi { get; init; } = string.Empty;
    }
}

/// <summary>创建模型请求</summary>
public class AiModelCreateRequest
{
    public string? Name { get; set; }
    public string? DisplayName { get; set; }
    public string? Provider { get; set; }
    public string? ApiKey { get; set; }
    public string? ApiKeyOperation { get; set; }
    public string? Model { get; set; }
    public string? BaseUrl { get; set; }
    public int TimeoutMs { get; set; }
    public string? Protocol { get; set; }
    public string? WireApi { get; set; }
    public string? AuthMode { get; set; }
    public string? AuthHeaderName { get; set; }
    public Dictionary<string, string>? ExtraHeaders { get; set; }
    public Dictionary<string, string>? ExtraQuery { get; set; }
    public Dictionary<string, JsonElement>? ExtraBody { get; set; }
    public AiReasoningSettings? Reasoning { get; set; }
    public List<string>? RoleBindings { get; set; }
    public string? ModelRole { get; set; }
    public int? Priority { get; set; }
    public bool? IsEnabled { get; set; }
    public string? Remark { get; set; }
    public AiModelCapabilities? Capabilities { get; set; }
}

/// <summary>更新模型请求</summary>
public class AiModelUpdateRequest
{
    public string? Name { get; set; }
    public string? DisplayName { get; set; }
    public string? Provider { get; set; }
    public string? ApiKey { get; set; }
    public string? ApiKeyOperation { get; set; }
    public string? Model { get; set; }
    public string? BaseUrl { get; set; }
    public int TimeoutMs { get; set; }
    public string? Protocol { get; set; }
    public string? WireApi { get; set; }
    public string? AuthMode { get; set; }
    public string? AuthHeaderName { get; set; }
    public Dictionary<string, string>? ExtraHeaders { get; set; }
    public Dictionary<string, string>? ExtraQuery { get; set; }
    public Dictionary<string, JsonElement>? ExtraBody { get; set; }
    public AiReasoningSettings? Reasoning { get; set; }
    public List<string>? RoleBindings { get; set; }
    public string? ModelRole { get; set; }
    public int? Priority { get; set; }
    public bool? IsEnabled { get; set; }
    public string? Remark { get; set; }
    public AiModelCapabilities? Capabilities { get; set; }
}

public class AiReasoningSupportRequest
{
    public string? Provider { get; set; }
    public string? Model { get; set; }
    public string? BaseUrl { get; set; }
    public string? Protocol { get; set; }
}

/// <summary>软触发抓图请求</summary>
public class RuntimePreviewPilotReadinessEndpointRequest
{
    public RuntimePreviewPilotConfig? Config { get; set; }
    public string? ToolName { get; set; }
    public JsonElement? Arguments { get; set; }
    public JsonElement? WorkflowDraft { get; set; }
}

public class RuntimePreviewRetentionCleanupEndpointRequest
{
    public int RetentionDays { get; set; } = 30;

    public int MaxSessions { get; set; } = 200;
}

public class CameraSoftTriggerCaptureRequest
{
    public string CameraBindingId { get; set; } = string.Empty;

    public DateTime? AcceptPendingEnterSignalAfterUtc { get; set; }
}

public class CameraContinuousPreviewStartRequest
{
    public string CameraBindingId { get; set; } = string.Empty;
}

public class CameraContinuousPreviewStopRequest
{
    public string SessionId { get; set; } = string.Empty;
}

public class TriggerDeviceLearnRequest
{
    public int TimeoutMs { get; set; } = 10000;
}

public class SerialPhotoelectricTestRequest
{
    public string? PortName { get; set; }

    public int BaudRate { get; set; } = 9600;

    public int DebounceMs { get; set; }

    public int TimeoutMs { get; set; } = 10000;
}

public sealed record SerialPhotoelectricPortInfo(
    string PortName,
    string DisplayName,
    bool IsRecommended);

internal sealed record SerialPhotoelectricPortCandidate(
    string PortName,
    string DisplayName,
    int Score);

public sealed class ThemeUpdateRequest
{
    public string? Theme { get; set; }
}
