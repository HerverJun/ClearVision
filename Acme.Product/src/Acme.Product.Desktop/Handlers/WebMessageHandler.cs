// WebMessageHandler.cs
// 发送事件到前端
// 作者：蘅芜君

using System.Collections.Concurrent;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Acme.Product.Application.Analysis;
using Acme.Product.Application.DTOs;
using Acme.Product.Application.Services;
using Acme.Product.Contracts.Messages;
using Acme.Product.Core.DTOs;
using Acme.Product.Core.Entities;
using Acme.Product.Core.Enums;
using Acme.Product.Core.Events;
using Acme.Product.Core.Interfaces;
using Acme.Product.Core.Services;
using Acme.Product.Desktop.Extensions;
using Acme.Product.Desktop.Inspection;
using Acme.Product.Infrastructure.Data;
using Acme.Product.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace Acme.Product.Desktop.Handlers;

/// <summary>
/// WebView2 消息处理器
/// </summary>
public class WebMessageHandler : IWebMessageClient, IDisposable
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOperatorFactory _operatorFactory;
    private readonly IInspectionEventBus _eventBus;
    private readonly ILogger<WebMessageHandler> _logger;
    private readonly OperatorExecutionMessageHandler _operatorExecutionHandler;
    private readonly ProjectFlowMessageHandler _projectFlowHandler;
    private readonly InspectionMessageHandler _inspectionHandler;
    private readonly FilePickerMessageHandler _filePickerHandler;
    private readonly AiSessionMessageHandler _aiSessionHandler;
    private WebView2? _webViewControl;
    private CoreWebView2? _webView;
    private int _disposeState;
    private readonly ConcurrentDictionary<string, ActiveGenerateFlowRequest> _activeGenerateFlowRequests = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, string> _activeGenerateFlowRequestIdsBySessionId = new(StringComparer.OrdinalIgnoreCase);
    private string? _latestGenerateFlowRequestId;

    // 事件订阅句柄
    private readonly List<IDisposable> _subscriptions = new();
    private bool _isSubscribed = false;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter() }
    };

    public WebMessageHandler(
        IServiceScopeFactory scopeFactory,
        IOperatorFactory operatorFactory,
        IInspectionEventBus eventBus,
        ILogger<WebMessageHandler> logger,
        ILoggerFactory loggerFactory)
    {
        _scopeFactory = scopeFactory;
        _operatorFactory = operatorFactory;
        _eventBus = eventBus;
        _logger = logger;
        _operatorExecutionHandler = new OperatorExecutionMessageHandler(
            scopeFactory,
            this,
            loggerFactory.CreateLogger<OperatorExecutionMessageHandler>());
        _projectFlowHandler = new ProjectFlowMessageHandler(
            scopeFactory,
            operatorFactory,
            loggerFactory.CreateLogger<ProjectFlowMessageHandler>());
        _inspectionHandler = new InspectionMessageHandler(
            scopeFactory,
            this,
            loggerFactory.CreateLogger<InspectionMessageHandler>());
        _filePickerHandler = new FilePickerMessageHandler(
            this,
            loggerFactory.CreateLogger<FilePickerMessageHandler>());
        _aiSessionHandler = new AiSessionMessageHandler(
            scopeFactory,
            this,
            loggerFactory.CreateLogger<AiSessionMessageHandler>());
    }

    /// <summary>
    /// 处理消息（供 WebView2Host 调用）
    /// </summary>
    public async Task<WebMessageResponse> HandleAsync(WebMessage message)
    {
        try
        {
            _logger.LogInformation("[WebMessageHandler] 处理消息: {MessageType}", message.Type);

            var messageJson = ExtractCommandJson(message);

            switch (message.Type)
            {
                case nameof(ExecuteOperatorCommand):
                    await _operatorExecutionHandler.HandleAsync(messageJson);
                    break;
                case nameof(UpdateFlowCommand):
                    await _projectFlowHandler.HandleUpdateFlowAsync(messageJson);
                    break;
                case nameof(StartInspectionCommand):
                    await _inspectionHandler.HandleStartAsync(messageJson);
                    break;
                case nameof(StopInspectionCommand):
                    await _inspectionHandler.HandleStopAsync();
                    break;
                case nameof(PickFileCommand):
                    await _filePickerHandler.HandleAsync(messageJson);
                    break;
                default:
                    return new WebMessageResponse { RequestId = message.Id, Success = false, Error = "未知消息类型" };
            }

            return new WebMessageResponse { RequestId = message.Id, Success = true };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[WebMessageHandler] 处理消息失败");
            return new WebMessageResponse { RequestId = message.Id, Success = false, Error = ex.Message };
        }
    }

    /// <summary>
    /// 初始化 WebView
    /// </summary>
    public void Initialize(WebView2 webViewControl)
    {
        if (webViewControl?.CoreWebView2 == null)
            throw new InvalidOperationException("WebView2 content is not initialized.");

        _webViewControl = webViewControl;
        _webView = webViewControl.CoreWebView2;
        _webView.WebMessageReceived += OnWebMessageReceived;

        // 【架构修复 v2】订阅事件总线
        InitializeEventSubscriptions();
    }

    /// <summary>
    /// 【架构修复 v2】订阅事件总线
    /// </summary>
    private void InitializeEventSubscriptions()
    {
        if (_isSubscribed)
            return;

        try
        {
            _logger.LogInformation("[WebMessageHandler] 初始化事件订阅");

            // 订阅状态变更事件
            _subscriptions.Add(_eventBus.Subscribe<InspectionStateChangedEvent>(async (evt, ct) =>
            {
                PublishRealtimeMessages(evt);
                await Task.CompletedTask;
            }));

            // 订阅结果事件
            _subscriptions.Add(_eventBus.Subscribe<InspectionResultEvent>(async (evt, ct) =>
            {
                PublishRealtimeMessages(evt);
                await Task.CompletedTask;
            }));

            // 订阅进度事件
            _subscriptions.Add(_eventBus.Subscribe<InspectionProgressEvent>(async (evt, ct) =>
            {
                PublishRealtimeMessages(evt);
                await Task.CompletedTask;
            }));

            _isSubscribed = true;
            _logger.LogInformation("[WebMessageHandler] 事件订阅完成，共 {Count} 个订阅", _subscriptions.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[WebMessageHandler] 事件订阅失败");
        }
    }

    /// <summary>
    /// 处理收到的消息（同步事件处理器）
    /// </summary>
    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        // 使用SafeFireAndForget确保异步异常被捕获，避免async void问题
        HandleWebMessageAsync(e).SafeFireAndForget(_logger);
    }

    /// <summary>
    /// 异步处理Web消息
    /// </summary>
    private async Task HandleWebMessageAsync(CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            var messageJson = e.WebMessageAsJson;
            string messageType = string.Empty;

            try
            {
                using (var doc = JsonDocument.Parse(messageJson))
                {
                    // 【修复】按优先级检查可能的消息类型字段
                    // 前端修复后使用 messageType，但保留对旧格式 type 的兼容
                    if (doc.RootElement.TryGetProperty("messageType", out var typeProp) ||
                        doc.RootElement.TryGetProperty("MessageType", out typeProp) ||
                        doc.RootElement.TryGetProperty("type", out typeProp) ||
                        doc.RootElement.TryGetProperty("Type", out typeProp))
                    {
                        messageType = typeProp.GetString() ?? string.Empty;
                    }
                }
            }
            catch (JsonException)
            {
                // 忽略非JSON消息
                return;
            }

            if (string.IsNullOrEmpty(messageType))
                return;

            _logger.LogInformation("[WebMessageHandler] 收到消息: {MessageType}", messageType);

            switch (messageType)
            {
                case nameof(ExecuteOperatorCommand):
                    await _operatorExecutionHandler.HandleAsync(messageJson);
                    break;

                case nameof(UpdateFlowCommand):
                    await _projectFlowHandler.HandleUpdateFlowAsync(messageJson);
                    break;

                case nameof(StartInspectionCommand):
                    await _inspectionHandler.HandleStartAsync(messageJson);
                    break;

                case nameof(StopInspectionCommand):
                    await _inspectionHandler.HandleStopAsync();
                    break;

                case nameof(PickFileCommand):
                    await _filePickerHandler.HandleAsync(messageJson);
                    break;

                case "ListAiSessions":
                    await _aiSessionHandler.HandleListAsync();
                    break;

                case "GetAiSession":
                    await _aiSessionHandler.HandleGetAsync(messageJson);
                    break;

                case "DeleteAiSession":
                    await _aiSessionHandler.HandleDeleteAsync(messageJson);
                    break;

                case "GenerateFlow":
                    await HandleGenerateFlowCommand(messageJson);
                    break;

                case "CancelGenerateFlow":
                    HandleCancelGenerateFlowCommand(messageJson);
                    break;

                case "planar2d:solve":
                    await HandlePlanarScaleOffsetSolveCommand(messageJson);
                    break;

                case "planar2d:save":
                    await HandlePlanarScaleOffsetSaveCommand(messageJson);
                    break;

                default:
                    _logger.LogWarning("[WebMessageHandler] 未知消息类型: {MessageType}", messageType);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[WebMessageHandler] 处理消息失败");
        }
    }

    private static string ExtractCommandJson(WebMessage message)
    {
        if (!string.IsNullOrWhiteSpace(message.Payload))
        {
            return message.Payload;
        }

        return JsonSerializer.Serialize(message, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
    }

    /// <summary>
    /// 处理 AI 生成工作流请求
    /// </summary>
    private async Task HandleGenerateFlowCommand(string messageJson)
    {
        ActiveGenerateFlowRequest? activeRequest = null;
        try
        {
            using var doc = JsonDocument.Parse(messageJson);
            var payload = doc.RootElement.GetProperty("payload");
            var description = payload.GetProperty("description").GetString() ?? "";
            var hint = payload.TryGetProperty("hint", out var hintElement)
                ? hintElement.GetString()
                : null;
            var sessionId = payload.TryGetProperty("sessionId", out var sessionElement)
                ? sessionElement.GetString()
                : null;
            var mode = GenerateFlowModeExtensions.ParseOrAuto(TryGetMessageString(payload, "mode"));
            var debugPrompt = TryGetBoolean(payload, "debugPrompt") ??
                              TryGetBoolean(doc.RootElement, "debugPrompt") ??
                              false;
            var requestId = TryGetMessageString(payload, "requestId")
                ?? TryGetMessageString(doc.RootElement, "requestId")
                ?? Guid.NewGuid().ToString("N");
            var requirementMode = TryGetMessageString(payload, "requirementMode")
                ?? TryGetMessageString(doc.RootElement, "requirementMode");
            var templateSelection = TryGetTemplateSelection(payload);
            var existingFlowJson = TryGetExistingFlowJson(payload);
            var attachments = payload.TryGetProperty("attachments", out var attachmentElement) &&
                              attachmentElement.ValueKind == JsonValueKind.Array
                ? attachmentElement.EnumerateArray()
                    .Where(item => item.ValueKind == JsonValueKind.String)
                    .Select(item => item.GetString())
                    .Where(path => !string.IsNullOrWhiteSpace(path))
                    .Cast<string>()
                    .ToList()
                : null;

            using var scope = _scopeFactory.CreateScope();
            var handler = scope.ServiceProvider.GetRequiredService<Acme.Product.Infrastructure.AI.GenerateFlowMessageHandler>();
            activeRequest = RegisterGenerateFlowRequest(requestId, sessionId);

            // 在后台线程执行 AI 生成，避免 WebView2/UI 线程被流式解析和回调压满。
            var resultJson = await Task.Run(() => handler.HandleAsync(
                description,
                sessionId,
                existingFlowJson,
                hint,
                mode,
                debugPrompt,
                requestId,
                attachments,
                requirementMode,
                templateSelection,
                onMessage: (type, payload) =>
                {
                    // payload 已是 JSON 字符串，直接拼接外层 envelope，避免反序列化再序列化的额外开销。
                    var progressJson = $"{{\"messageType\":{JsonSerializer.Serialize(type)},\"payload\":{payload}}}";
                    PostWebMessageJson(progressJson);
                },
                cancellationToken: activeRequest.CancellationTokenSource.Token));

            // 发回前端（原始 JSON）
            PostWebMessageJson(resultJson);
        }
        catch (HttpRequestException ex) when (ex.InnerException is System.Net.Sockets.SocketException)
        {
            _logger.LogWarning(ex, "AI 服务连接被拦截");
            SendProgressMessage("AiFirewallBlocked", new
            {
                message = "检测到网络连接被阻断（可能被防火墙拦截）",
                detail = ex.Message,
                timestamp = DateTime.Now
            });
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogWarning(ex, "AI 服务请求超时");
            SendProgressMessage("AiFirewallBlocked", new
            {
                message = "AI 服务连接超时（可能被防火墙拦截）",
                detail = "请检查网络环境或代理设置",
                timestamp = DateTime.Now
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "处理 AI 生成请求失败");

            var errorResponse = new GenerateFlowResponse
            {
                Success = false,
                ErrorMessage = $"系统错误：{ex.Message}"
            };

            var json = JsonSerializer.Serialize(errorResponse, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            PostWebMessageJson(json);
        }
        finally
        {
            if (activeRequest != null)
            {
                UnregisterGenerateFlowRequest(activeRequest);
            }
        }
    }

    private static AiTemplateSelectionInfo? TryGetTemplateSelection(JsonElement payload)
    {
        if (!payload.TryGetProperty("templateSelection", out var selection) &&
            !payload.TryGetProperty("TemplateSelection", out selection))
        {
            return null;
        }

        if (selection.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return null;

        if (selection.ValueKind != JsonValueKind.Object)
            return null;

        var mode = TryGetMessageString(selection, "mode");
        var templateId = TryGetMessageString(selection, "templateId");
        var scenarioKey = TryGetMessageString(selection, "scenarioKey");
        if (string.IsNullOrWhiteSpace(mode) &&
            string.IsNullOrWhiteSpace(templateId) &&
            string.IsNullOrWhiteSpace(scenarioKey))
        {
            return null;
        }

        return new AiTemplateSelectionInfo
        {
            Mode = mode ?? string.Empty,
            TemplateId = templateId,
            ScenarioKey = scenarioKey
        };
    }

    private static string? TryGetExistingFlowJson(JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object)
            return null;

        if (!payload.TryGetProperty("existingFlowJson", out var flowElement) &&
            !payload.TryGetProperty("ExistingFlowJson", out flowElement))
        {
            return null;
        }

        return flowElement.ValueKind switch
        {
            JsonValueKind.String => string.IsNullOrWhiteSpace(flowElement.GetString()) ? null : flowElement.GetString(),
            JsonValueKind.Object or JsonValueKind.Array => flowElement.GetRawText(),
            _ => null
        };
    }

    /// <summary>
    /// 处理二维平面比例偏移标定解算请求
    /// </summary>
    private async Task HandlePlanarScaleOffsetSolveCommand(string messageJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(messageJson);
            var payload = doc.RootElement.GetProperty("payload");

            var points = new List<PlanarScaleOffsetCalibrationPoint>();
            foreach (var pointElement in payload.EnumerateArray())
            {
                points.Add(new PlanarScaleOffsetCalibrationPoint
                {
                    PixelX = pointElement.GetProperty("pixelX").GetDouble(),
                    PixelY = pointElement.GetProperty("pixelY").GetDouble(),
                    PhysicalX = pointElement.GetProperty("physicalX").GetDouble(),
                    PhysicalY = pointElement.GetProperty("physicalY").GetDouble()
                });
            }

            using var scope = _scopeFactory.CreateScope();
            var calibService = scope.ServiceProvider.GetRequiredService<IPlanarScaleOffsetCalibrationService>();
            var result = await calibService.SolveAsync(points);

            // 发送结果回前端
            SendProgressMessage("planar2d:solve:result", result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "处理二维平面比例偏移标定解算失败");
            SendProgressMessage("planar2d:solve:result", new { success = false, message = ex.Message });
        }
    }

    /// <summary>
    /// 处理二维平面比例偏移标定保存请求
    /// </summary>
    private async Task HandlePlanarScaleOffsetSaveCommand(string messageJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(messageJson);
            var payload = doc.RootElement.GetProperty("payload");
            var resultElement = payload.GetProperty("result");
            var fileName = payload.GetProperty("fileName").GetString() ?? "planar_scale_offset_calib.json";

            var result = new PlanarScaleOffsetCalibrationResult
            {
                Success = resultElement.GetProperty("success").GetBoolean(),
                Accepted = resultElement.TryGetProperty("accepted", out var acceptedElement) && acceptedElement.GetBoolean(),
                Message = resultElement.TryGetProperty("message", out var messageElement) ? messageElement.GetString() ?? string.Empty : string.Empty,
                OriginX = resultElement.GetProperty("originX").GetDouble(),
                OriginY = resultElement.GetProperty("originY").GetDouble(),
                ScaleX = resultElement.GetProperty("scaleX").GetDouble(),
                ScaleY = resultElement.GetProperty("scaleY").GetDouble(),
                MeanErrorX = resultElement.TryGetProperty("meanErrorX", out var meanErrorXElement) ? meanErrorXElement.GetDouble() : 0.0,
                MeanErrorY = resultElement.TryGetProperty("meanErrorY", out var meanErrorYElement) ? meanErrorYElement.GetDouble() : 0.0,
                Rmse = resultElement.TryGetProperty("rmse", out var rmseElement) ? rmseElement.GetDouble() : 0.0,
                PointCount = resultElement.TryGetProperty("pointCount", out var pointCountElement) ? pointCountElement.GetInt32() : 0
            };

            using var scope = _scopeFactory.CreateScope();
            var calibService = scope.ServiceProvider.GetRequiredService<IPlanarScaleOffsetCalibrationService>();
            var isSaved = await calibService.SaveCalibrationAsync(result, fileName);

            // 发送结果回前端
            SendProgressMessage("planar2d:save:result", new { success = isSaved });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "保存二维平面比例偏移标定文件失败");
            SendProgressMessage("planar2d:save:result", new { success = false, message = ex.Message });
        }
    }

    private void HandleCancelGenerateFlowCommand(string messageJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(messageJson);
            var payload = doc.RootElement.TryGetProperty("payload", out var payloadElement) &&
                          payloadElement.ValueKind == JsonValueKind.Object
                ? payloadElement
                : doc.RootElement;

            var requestId = TryGetMessageString(payload, "requestId") ??
                            TryGetMessageString(doc.RootElement, "requestId");
            var sessionId = TryGetMessageString(payload, "sessionId") ??
                            TryGetMessageString(doc.RootElement, "sessionId");

            if (!TryResolveGenerateFlowRequest(requestId, sessionId, out var activeRequest))
            {
                _logger.LogInformation(
                    "收到取消 AI 生成请求，但未找到活动任务。RequestId={RequestId}, SessionId={SessionId}",
                    requestId,
                    sessionId);
                PostWebMessageJson(JsonSerializer.Serialize(new CancelGenerateFlowResponse
                {
                    Success = false,
                    Status = "not_found",
                    SessionId = sessionId,
                    RequestId = requestId,
                    ErrorMessage = "未找到可取消的生成任务。"
                }, _jsonOptions));
                return;
            }

            if (!activeRequest.CancellationTokenSource.IsCancellationRequested)
            {
                activeRequest.CancellationTokenSource.Cancel();
            }

            _logger.LogInformation(
                "已取消 AI 生成请求。RequestId={RequestId}, SessionId={SessionId}",
                activeRequest.RequestId,
                activeRequest.SessionId);

            PostWebMessageJson(JsonSerializer.Serialize(new CancelGenerateFlowResponse
            {
                Success = true,
                Status = "cancelled",
                SessionId = activeRequest.SessionId,
                RequestId = activeRequest.RequestId,
                Message = "已发送取消请求。"
            }, _jsonOptions));

            SendProgressMessage("GenerateFlowProgress", new
            {
                message = "正在取消本次生成...",
                phase = "cancelling",
                requestId = activeRequest.RequestId,
                sessionId = activeRequest.SessionId
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "处理取消 AI 生成请求失败");
            PostWebMessageJson(JsonSerializer.Serialize(new CancelGenerateFlowResponse
            {
                Success = false,
                Status = "failed",
                SessionId = null,
                RequestId = null,
                ErrorMessage = ex.Message
            }, _jsonOptions));
        }
    }

    private ActiveGenerateFlowRequest RegisterGenerateFlowRequest(string requestId, string? sessionId)
    {
        var activeRequest = new ActiveGenerateFlowRequest(requestId, sessionId, new CancellationTokenSource());
        _activeGenerateFlowRequests[requestId] = activeRequest;
        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            _activeGenerateFlowRequestIdsBySessionId[sessionId] = requestId;
        }

        _latestGenerateFlowRequestId = requestId;
        return activeRequest;
    }

    private void UnregisterGenerateFlowRequest(ActiveGenerateFlowRequest activeRequest)
    {
        _activeGenerateFlowRequests.TryRemove(activeRequest.RequestId, out _);
        if (!string.IsNullOrWhiteSpace(activeRequest.SessionId))
        {
            _activeGenerateFlowRequestIdsBySessionId.TryRemove(activeRequest.SessionId, out _);
        }

        if (string.Equals(_latestGenerateFlowRequestId, activeRequest.RequestId, StringComparison.OrdinalIgnoreCase))
        {
            _latestGenerateFlowRequestId = _activeGenerateFlowRequests.Keys.LastOrDefault();
        }

        activeRequest.Dispose();
    }

    private bool TryResolveGenerateFlowRequest(
        string? requestId,
        string? sessionId,
        out ActiveGenerateFlowRequest activeRequest)
    {
        if (!string.IsNullOrWhiteSpace(requestId) &&
            _activeGenerateFlowRequests.TryGetValue(requestId, out activeRequest!))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(sessionId) &&
            _activeGenerateFlowRequestIdsBySessionId.TryGetValue(sessionId, out var mappedRequestId) &&
            _activeGenerateFlowRequests.TryGetValue(mappedRequestId, out activeRequest!))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(_latestGenerateFlowRequestId) &&
            _activeGenerateFlowRequests.TryGetValue(_latestGenerateFlowRequestId, out activeRequest!))
        {
            return true;
        }

        activeRequest = null!;
        return false;
    }

    private static string? TryGetMessageString(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return null;

        if (element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String)
            return property.GetString();

        var pascalCase = char.ToUpperInvariant(propertyName[0]) + propertyName[1..];
        if (element.TryGetProperty(pascalCase, out property) && property.ValueKind == JsonValueKind.String)
            return property.GetString();

        return null;
    }

    private static bool? TryGetBoolean(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return null;

        if (!element.TryGetProperty(propertyName, out var property))
        {
            var pascalCase = char.ToUpperInvariant(propertyName[0]) + propertyName[1..];
            if (!element.TryGetProperty(pascalCase, out property))
                return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String when bool.TryParse(property.GetString(), out var parsed) => parsed,
            _ => null
        };
    }

    private void PublishRealtimeMessages(IInspectionEvent evt)
    {
        foreach (var message in InspectionRealtimeEventMapper.Map(evt))
        {
            SendProgressMessage(message.EventType, message.Payload);
        }
    }

    public void NotifyInspectionResult(InspectionResult result, Guid projectId)
    {
        try
        {
            Dictionary<string, object>? outputData = null;
            Dictionary<string, object>? analysisData = null;

            try
            {
                outputData = AnalysisPayloadSerialization.DeserializeJsonDictionary(result.OutputDataJson);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "[WebMessageHandler] 解析 OutputDataJson 失败: ResultId={ResultId}", result.Id);
            }

            try
            {
                analysisData = AnalysisPayloadSerialization.DeserializeJsonDictionary(result.AnalysisDataJson);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "[WebMessageHandler] 解析 AnalysisDataJson 失败: ResultId={ResultId}", result.Id);
            }

            var message = CreateInspectionCompletedMessage(result, projectId, outputData, analysisData);

            PostWebMessageJson(JsonSerializer.Serialize(message, _jsonOptions));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[WebMessageHandler] 推送检测结果失败: ResultId={ResultId}", result.Id);
        }
    }

    internal static object CreateInspectionCompletedMessage(
        InspectionResult result,
        Guid projectId,
        Dictionary<string, object>? outputData,
        Dictionary<string, object>? analysisData)
    {
        return new
        {
            type = "inspectionCompleted",
            messageType = "inspectionCompleted",
            resultId = result.Id,
            projectId,
            status = result.Status.ToString(),
            defects = result.Defects.Select(d => new
            {
                type = d.Type.ToString(),
                x = d.X,
                y = d.Y,
                width = d.Width,
                height = d.Height,
                confidence = d.ConfidenceScore,
                description = d.Description ?? string.Empty
            }).ToList(),
            processingTimeMs = result.ProcessingTimeMs,
            outputImage = result.OutputImage != null ? Convert.ToBase64String(result.OutputImage) : null,
            outputData,
            analysisData
        };
    }

    /// <summary>
    /// 发送事件到前端
    /// </summary>
    public void SendEvent<T>(T eventData)
    {
        try
        {
            var json = JsonSerializer.Serialize(eventData, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            PostWebMessageJson(json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[WebMessageHandler] 发送事件失败");
        }
    }

    public void SendProgressMessage(string type, object payload)
    {
        var json = JsonSerializer.Serialize(new
        {
            messageType = type,
            payload
        }, _jsonOptions);

        PostWebMessageJson(json);
    }

    private void PostWebMessageJson(string json)
    {
        var webViewControl = _webViewControl;
        var webView = _webView;
        if (webViewControl == null || webView == null || webViewControl.IsDisposed)
            return;

        if (webViewControl.InvokeRequired)
        {
            _ = webViewControl.BeginInvoke(new Action(() =>
            {
                if (webViewControl.IsDisposed)
                    return;

                try
                {
                    webView.PostWebMessageAsJson(json);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "[WebMessageHandler] 异步推送 WebMessage 失败");
                }
            }));
            return;
        }

        webView.PostWebMessageAsJson(json);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposeState, 1) != 0)
        {
            return;
        }
        _logger.LogInformation("[WebMessageHandler] 正在释放资源");

        foreach (var activeRequest in _activeGenerateFlowRequests.Values)
        {
            try
            {
                if (!activeRequest.CancellationTokenSource.IsCancellationRequested)
                {
                    activeRequest.CancellationTokenSource.Cancel();
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "[WebMessageHandler] 取消活动 AI 生成请求失败");
            }
        }

        foreach (var activeRequest in _activeGenerateFlowRequests.Values)
        {
            activeRequest.Dispose();
        }

        _activeGenerateFlowRequests.Clear();
        _activeGenerateFlowRequestIdsBySessionId.Clear();

        foreach (var subscription in _subscriptions)
        {
            try
            {
                subscription.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[WebMessageHandler] 取消订阅时异常");
            }
        }
        _subscriptions.Clear();
        _isSubscribed = false;

        DetachWebView();

        _logger.LogInformation("[WebMessageHandler] 资源已释放");
    }

    private void DetachWebView()
    {
        var webViewControl = _webViewControl;
        var webView = _webView;

        _webViewControl = null;
        _webView = null;

        if (webViewControl == null || webView == null || webViewControl.IsDisposed)
        {
            return;
        }

        if (webViewControl.InvokeRequired)
        {
            _logger.LogDebug("[WebMessageHandler] Skipping WebView2 detach because shutdown is not on the UI thread.");
            return;
        }

        try
        {
            webView.WebMessageReceived -= OnWebMessageReceived;
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogDebug(ex, "[WebMessageHandler] WebView2 is no longer available during shutdown.");
        }
        catch (InvalidCastException ex)
        {
            _logger.LogDebug(ex, "[WebMessageHandler] WebView2 COM interface is already unavailable during shutdown.");
        }
        catch (COMException ex)
        {
            _logger.LogDebug(ex, "[WebMessageHandler] WebView2 COM cleanup is already in progress.");
        }
    }

    private sealed class ActiveGenerateFlowRequest : IDisposable
    {
        public ActiveGenerateFlowRequest(
            string requestId,
            string? sessionId,
            CancellationTokenSource cancellationTokenSource)
        {
            RequestId = requestId;
            SessionId = sessionId;
            CancellationTokenSource = cancellationTokenSource;
        }

        public string RequestId { get; }

        public string? SessionId { get; }

        public CancellationTokenSource CancellationTokenSource { get; }

        public void Dispose()
        {
            CancellationTokenSource.Dispose();
        }
    }
}
