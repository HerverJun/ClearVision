// SettingsEndpoints.cs
// 设置功能 API 端点
// 作者：蘅芜君

using System;
using System.Buffers.Binary;
using System.IO.Ports;
using System.Linq;
using System.Text.Json;
using Acme.Product.Application.Services;
using Acme.Product.Core.Cameras;
using Acme.Product.Core.Continuous;
using Acme.Product.Core.Entities;
using Acme.Product.Core.Enums;
using Acme.Product.Core.Interfaces;
using Acme.Product.Desktop.Triggers;
using Acme.Product.Infrastructure.AI;
using Acme.Product.Infrastructure.Cameras;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Win32;

namespace Acme.Product.Desktop.Endpoints;

/// <summary>
/// 设置功能 API 端点
/// </summary>
public static class SettingsEndpoints
{
    public static IEndpointRouteBuilder MapSettingsEndpoints(this IEndpointRouteBuilder app)
    {
        // 获取当前配置
        app.MapGet("/api/settings", async (IConfigurationService configService) =>
        {
            var config = await configService.LoadAsync();
            return Results.Ok(config);
        });

        // 更新配置
        app.MapPut("/api/settings", async (AppConfig config, IConfigurationService configService, HttpContext context) =>
        {
            if (!IsAdmin(context))
            {
                return Results.Forbid();
            }

            try
            {
                var currentConfig = await configService.LoadAsync();
                config.Cameras = NormalizeBindings(CloneBindings(currentConfig.Cameras));
                config.ActiveCameraId = currentConfig.ActiveCameraId ?? string.Empty;

                await configService.SaveAsync(config);
                return Results.Ok(new { Message = "设置已保存" });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { Error = ex.Message });
            }
        });

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
        });

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
        });

        // 获取磁盘容量信息（用于设置页存储卡片）
        app.MapGet("/api/settings/disk-usage", (string? path, IConfigurationService configService) =>
        {
            var configuredPath = string.IsNullOrWhiteSpace(path)
                ? configService.GetCurrent().Storage.ImageSavePath
                : path;

            if (!TryBuildDiskUsage(configuredPath, out var usage, out var error))
            {
                return Results.BadRequest(new { Error = error });
            }

            return Results.Ok(usage);
        });

        // ==================== AI 多模型管理 API ====================

        // 获取所有模型（不含 ApiKey）
        app.MapGet("/api/ai/models", (AiConfigStore configStore) =>
        {
            var models = configStore.GetAll();
            var result = models.Select(m => new
            {
                m.Id,
                m.Name,
                m.Provider,
                hasApiKey = !string.IsNullOrWhiteSpace(m.ApiKey), // 前端用此判断是否已配置密钥
                m.Model,
                baseUrl = m.BaseUrl ?? "",
                m.TimeoutMs,
                m.IsActive,
                m.Protocol,
                m.AuthMode,
                m.AuthHeaderName,
                m.ExtraHeaders,
                m.ExtraQuery,
                m.ExtraBody,
                m.RoleBindings,
                m.Priority,
                m.Capabilities,
                m.Reasoning,
                ReasoningSupport = m.GetReasoningSupport()
            });
            return Results.Ok(result);
        });

        app.MapPost("/api/ai/reasoning-support", (AiReasoningSupportRequest request) =>
        {
            var support = AiReasoningModelFamilyCatalog.Resolve(
                request.Provider,
                request.Model,
                request.BaseUrl,
                request.Protocol);
            return Results.Ok(support);
        });

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
                    Provider = request.Provider ?? AiModelConfig.GetLegacyProviderByProtocol(request.Protocol),
                    ApiKey = request.ApiKey ?? "",
                    Model = request.Model ?? string.Empty,
                    BaseUrl = string.IsNullOrWhiteSpace(request.BaseUrl) ? null : request.BaseUrl,
                    TimeoutMs = request.TimeoutMs > 0 ? request.TimeoutMs : 120000,
                    Protocol = request.Protocol,
                    AuthMode = request.AuthMode,
                    AuthHeaderName = request.AuthHeaderName,
                    ExtraHeaders = CloneStringMap(request.ExtraHeaders),
                    ExtraQuery = CloneStringMap(request.ExtraQuery),
                    ExtraBody = CloneJsonMap(request.ExtraBody),
                    RoleBindings = CloneStringList(request.RoleBindings),
                    Priority = request.Priority,
                    IsActive = false,
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
        });

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
                    Provider = request.Provider!,
                    ApiKey = request.ApiKey ?? "", // 空字符串 → 保留原值（由 AiConfigStore.Update 处理）
                    Model = request.Model ?? string.Empty,
                    BaseUrl = request.BaseUrl,
                    TimeoutMs = request.TimeoutMs,
                    Protocol = request.Protocol,
                    AuthMode = request.AuthMode,
                    AuthHeaderName = request.AuthHeaderName,
                    ExtraHeaders = CloneStringMap(request.ExtraHeaders),
                    ExtraQuery = CloneStringMap(request.ExtraQuery),
                    ExtraBody = CloneJsonMap(request.ExtraBody),
                    RoleBindings = CloneStringList(request.RoleBindings),
                    Priority = request.Priority,
                    Capabilities = request.Capabilities?.Clone(),
                    Reasoning = request.Reasoning?.Clone()
                };
                var result = configStore.Update(id, updated);
                if (result == null)
                    return Results.NotFound(new { Error = $"模型 {id} 不存在" });

                return Results.Ok(new { Message = "模型已更新" });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { Error = ex.Message });
            }
        });

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
        });

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
        });

        // 测试指定模型的连接（使用该模型的真实 Key，不影响全局 active 状态）
        app.MapPost("/api/ai/models/{id}/test", async (string id, AiConfigStore configStore, AiApiClient apiClient, HttpContext context) =>
        {
            if (!IsAdmin(context))
            {
                return Results.Forbid();
            }

            try
            {
                var model = configStore.GetById(id);
                if (model == null)
                    return Results.NotFound(new { Success = false, Message = $"模型 {id} 不存在" });

                var authMode = AiModelConfig.NormalizeAuthMode(model.AuthMode, model.Protocol ?? model.Provider);
                if (authMode != AiModelConfig.AuthModeNone && string.IsNullOrEmpty(model.ApiKey))
                    return Results.Ok(new { Success = false, Message = "连接失败: 未配置 API Key" });

                var options = model.ToGenerationOptions();
                var response = await apiClient.StreamCompleteAsync(
                    "You are a connection health-check assistant. Respond only with valid JSON.",
                    new List<ChatMessage> { new("user", "Reply with a JSON object exactly: {\"ok\": true}") },
                    _ => { },
                    options,
                    CancellationToken.None);

                if (!IsSuccessfulAiHealthCheck(response.Content))
                    return Results.Ok(new { Success = false, Message = "连接失败: AI 返回内容不是预期的 JSON health-check 响应" });

                return Results.Ok(new { Success = true, Message = "连接成功" });
            }
            catch (Exception ex)
            {
                return Results.Ok(new { Success = false, Message = $"连接失败: {ex.Message}" });
            }
        });

        // ==================== 相机管理 API ====================

        // 搜索在线相机设备
        app.MapGet("/api/cameras/discover", async (Acme.Product.Core.Cameras.ICameraManager cameraManager) =>
        {
            var devices = await cameraManager.EnumerateCamerasAsync();
            return Results.Ok(devices);
        });

        // 仅通过华睿 SDK 搜索在线相机
        app.MapGet("/api/cameras/discover/huaray", (Acme.Product.Core.Cameras.ICameraManager cameraManager) =>
        {
            var devices = CameraProviderFactory.DiscoverHuarayOnly();
            var mapped = MapDiscoveredDevices(devices, cameraManager).ToList();
            var diagnostics = BuildHuarayDiagnostics(mapped.Count);
            return Results.Ok(new { devices = mapped, diagnostics });
        });

        // 仅通过海康 SDK 搜索在线相机
        app.MapGet("/api/cameras/discover/hikvision", (Acme.Product.Core.Cameras.ICameraManager cameraManager) =>
        {
            var devices = CameraProviderFactory.DiscoverHikvisionOnly();
            return Results.Ok(MapDiscoveredDevices(devices, cameraManager));
        });

        // 获取已配置的相机绑定列表
        app.MapGet("/api/cameras/bindings", async (Acme.Product.Core.Cameras.ICameraManager cameraManager) =>
        {
            var bindings = cameraManager.GetBindings();
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
                    binding.ExposureTimeUs,
                    binding.GainDb,
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
        });

        // 更新相机绑定配置
        app.MapPut("/api/cameras/bindings", async (
            Acme.Product.Application.DTOs.UpdateCameraBindingsRequest request,
            Acme.Product.Core.Cameras.ICameraManager cameraManager,
            [FromServices] ICameraFrameStreamCoordinator streamCoordinator,
            [FromServices] ISerialPhotoelectricTriggerInputService serialPhotoelectricTriggerInputService,
            IConfigurationService configService) =>
        {
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
        });

        // 使用绑定参数执行手动软触发抓图（仅用于预览）
        app.MapPost("/api/cameras/soft-trigger-capture", async (
            CameraSoftTriggerCaptureRequest request,
            HttpContext context,
            [FromServices]
            Acme.Product.Core.Cameras.ICameraManager cameraManager,
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
                // （StartGrabbing → TriggerMode=On → TriggerSource=Software → ExecuteSoftwareTrigger → GetFrame）

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
        });

        app.MapGet("/api/trigger-input/diagnostics", ([FromServices] ITriggerInputService triggerInputService) =>
        {
            return Results.Ok(triggerInputService.GetDiagnostics());
        });

        app.MapGet("/api/trigger-input/serial-photoelectric-ports", () =>
        {
            return Results.Ok(BuildSerialPhotoelectricPortList());
        });

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
        });

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
        });

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
        });

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
        });

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
        });

        return app;
    }

    private static bool IsAdmin(HttpContext context)
    {
        return context.Items.TryGetValue("CurrentUser", out var userObj) &&
               userObj is UserSession user &&
               string.Equals(user.Role, UserRole.Admin.ToString(), StringComparison.Ordinal);
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

    private static object ToAiModelResponse(AiModelConfig m) => new
    {
        m.Id,
        m.Name,
        m.Provider,
        hasApiKey = !string.IsNullOrWhiteSpace(m.ApiKey),
        m.Model,
        baseUrl = m.BaseUrl ?? "",
        m.TimeoutMs,
        m.IsActive,
        m.Protocol,
        m.AuthMode,
        m.AuthHeaderName,
        m.ExtraHeaders,
        m.ExtraQuery,
        m.ExtraBody,
        m.RoleBindings,
        m.Priority,
        m.Capabilities,
        m.Reasoning,
        ReasoningSupport = m.GetReasoningSupport()
    };

    private static IEnumerable<CameraInfo> MapDiscoveredDevices(
        IEnumerable<CameraDeviceInfo> devices,
        Acme.Product.Core.Cameras.ICameraManager cameraManager)
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

    private static bool TryBuildDiskUsage(string? targetPath, out object usage, out string error)
    {
        usage = default!;
        error = string.Empty;

        try
        {
            var fullPath = string.IsNullOrWhiteSpace(targetPath)
                ? AppContext.BaseDirectory
                : Path.GetFullPath(targetPath);

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

            usage = new
            {
                driveName = drive.Name,
                sourcePath = fullPath,
                totalBytes,
                usedBytes,
                freeBytes,
                totalGb = Math.Round(totalBytes / 1024d / 1024d / 1024d, 2),
                usedGb = Math.Round(usedBytes / 1024d / 1024d / 1024d, 2),
                freeGb = Math.Round(freeBytes / 1024d / 1024d / 1024d, 2),
                usedPercent = Math.Round(usedPercent, 2)
            };
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
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
}

/// <summary>创建模型请求</summary>
public class AiModelCreateRequest
{
    public string? Name { get; set; }
    public string? Provider { get; set; }
    public string? ApiKey { get; set; }
    public string? Model { get; set; }
    public string? BaseUrl { get; set; }
    public int TimeoutMs { get; set; }
    public string? Protocol { get; set; }
    public string? AuthMode { get; set; }
    public string? AuthHeaderName { get; set; }
    public Dictionary<string, string>? ExtraHeaders { get; set; }
    public Dictionary<string, string>? ExtraQuery { get; set; }
    public Dictionary<string, JsonElement>? ExtraBody { get; set; }
    public AiReasoningSettings? Reasoning { get; set; }
    public List<string>? RoleBindings { get; set; }
    public int? Priority { get; set; }
    public AiModelCapabilities? Capabilities { get; set; }
}

/// <summary>更新模型请求</summary>
public class AiModelUpdateRequest
{
    public string? Name { get; set; }
    public string? Provider { get; set; }
    public string? ApiKey { get; set; }
    public string? Model { get; set; }
    public string? BaseUrl { get; set; }
    public int TimeoutMs { get; set; }
    public string? Protocol { get; set; }
    public string? AuthMode { get; set; }
    public string? AuthHeaderName { get; set; }
    public Dictionary<string, string>? ExtraHeaders { get; set; }
    public Dictionary<string, string>? ExtraQuery { get; set; }
    public Dictionary<string, JsonElement>? ExtraBody { get; set; }
    public AiReasoningSettings? Reasoning { get; set; }
    public List<string>? RoleBindings { get; set; }
    public int? Priority { get; set; }
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
