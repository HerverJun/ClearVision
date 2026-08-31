// WebMessageHandler.cs
// 发送事件到前端
// 作者：蘅芜君

using System.Collections.Concurrent;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using ClearVision.Product.Application.Analysis;
using ClearVision.Product.Application.DTOs;
using ClearVision.Product.Application.Services;
using ClearVision.Product.Contracts.Messages;
using ClearVision.Product.Core.DTOs;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Events;
using ClearVision.Product.Core.Interfaces;
using ClearVision.Product.Core.Services;
using ClearVision.Product.Desktop.Extensions;
using ClearVision.Product.Desktop.Inspection;
using ClearVision.Product.Desktop.PreviewArtifacts;
using ClearVision.Product.Infrastructure.AI.AgentRun;
using ClearVision.Product.Infrastructure.Calibration;
using ClearVision.Product.Infrastructure.Data;
using ClearVision.Product.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using ConversationSessionAccessException = ClearVision.Product.Infrastructure.AI.ConversationSessionAccessException;

namespace ClearVision.Product.Desktop.Handlers;

internal readonly record struct GenerateFlowRequestIdentity(
    string OwnerHash,
    string SessionId,
    string RequestId)
{
    public static GenerateFlowRequestIdentity CreateForRegistration(
        string ownerHash,
        string? sessionId,
        string requestId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerHash);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestId);

        return new GenerateFlowRequestIdentity(
            ownerHash.Trim(),
            sessionId.Trim(),
            requestId.Trim());
    }

    public static bool TryCreateExact(
        string ownerHash,
        string? sessionId,
        string? requestId,
        out GenerateFlowRequestIdentity identity)
    {
        if (string.IsNullOrWhiteSpace(ownerHash) ||
            string.IsNullOrWhiteSpace(sessionId) ||
            string.IsNullOrWhiteSpace(requestId))
        {
            identity = default;
            return false;
        }

        identity = CreateForRegistration(ownerHash, sessionId, requestId);
        return true;
    }
}

internal sealed class GenerateFlowRequestRegistry<TRequest>
    where TRequest : class
{
    private readonly ConcurrentDictionary<GenerateFlowRequestIdentity, TRequest> _requests = new();

    public int Count => _requests.Count;

    public bool TryRegister(GenerateFlowRequestIdentity identity, TRequest request) =>
        _requests.TryAdd(identity, request);

    public bool TryResolveExact(
        string ownerHash,
        string? sessionId,
        string? requestId,
        out TRequest request)
    {
        if (GenerateFlowRequestIdentity.TryCreateExact(
                ownerHash,
                sessionId,
                requestId,
                out var identity) &&
            _requests.TryGetValue(identity, out request!))
        {
            return true;
        }

        request = null!;
        return false;
    }

    public bool CompareAndRemove(GenerateFlowRequestIdentity identity, TRequest expectedRequest) =>
        ((ICollection<KeyValuePair<GenerateFlowRequestIdentity, TRequest>>)_requests)
        .Remove(new KeyValuePair<GenerateFlowRequestIdentity, TRequest>(identity, expectedRequest));

    public IReadOnlyList<TRequest> Snapshot() => _requests.Values.ToArray();

    public void Clear() => _requests.Clear();
}

/// <summary>
/// WebView2 消息处理器
/// </summary>
public class WebMessageHandler : IWebMessageClient, IDisposable
{
    private const int MaxPendingWebMessages = 512;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IInspectionEventBus _eventBus;
    private readonly ILogger<WebMessageHandler> _logger;
    private readonly FilePickerMessageHandler _filePickerHandler;
    private readonly AiSessionMessageHandler _aiSessionHandler;
    private readonly WebMessageAdmissionService _admissionService;
    private WebView2? _webViewControl;
    private CoreWebView2? _webView;
    private int _disposeState;
    private readonly ConcurrentQueue<PendingWebMessage> _pendingWebMessages = new();
    private int _pendingWebMessageCount;
    private int _webMessageDrainScheduled;
    private long _droppedWebMessageCount;
    private readonly GenerateFlowRequestRegistry<ActiveGenerateFlowRequest> _activeGenerateFlowRequests = new();
    private readonly object _bindingGate = new();
    private WebMessageDeliveryBinding? _currentDeliveryBinding;
    private long _deliveryEpoch;

    // 事件订阅句柄
    private readonly List<IDisposable> _subscriptions = new();
    private bool _isSubscribed = false;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
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
        ArgumentNullException.ThrowIfNull(operatorFactory);
        _eventBus = eventBus;
        _logger = logger;
        _filePickerHandler = new FilePickerMessageHandler(
            this,
            loggerFactory.CreateLogger<FilePickerMessageHandler>());
        _aiSessionHandler = new AiSessionMessageHandler(
            scopeFactory,
            this,
            loggerFactory.CreateLogger<AiSessionMessageHandler>());
        _admissionService = new WebMessageAdmissionService(scopeFactory);
    }

    /// <summary>
    /// 处理消息（供 WebView2Host 调用）
    /// </summary>
    public async Task<WebMessageResponse> HandleAsync(WebMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var payload = ParsePayloadElement(message.Payload);
        var json = JsonSerializer.Serialize(new
        {
            messageType = message.Type,
            requestId = message.Id,
            payload
        }, _jsonOptions);

        return await DispatchWebMessageAsync(json, source: null);
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
        _webView.NavigationStarting += OnNavigationStarting;

        // 【架构修复 v2】订阅事件总线
        InitializeEventSubscriptions();
        if (Volatile.Read(ref _pendingWebMessageCount) > 0)
        {
            SchedulePendingWebMessageDrain(webViewControl);
        }
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
        await DispatchWebMessageAsync(e.WebMessageAsJson, e.Source);
    }

    internal async Task<WebMessageResponse> DispatchWebMessageAsync(
        string messageJson,
        string? source,
        CancellationToken cancellationToken = default)
    {
        var admission = await _admissionService.AdmitAsync(messageJson, source, cancellationToken);
        if (!admission.Allowed)
        {
            if (string.Equals(
                    admission.MessageType,
                    WebMessageAdmissionService.BindingChangedMessageType,
                    StringComparison.Ordinal) &&
                WebMessageAdmissionService.IsTrustedOrigin(source))
            {
                InvalidateDeliveryBinding("client-auth-cleared");
            }

            SendProgressMessage("WebMessageRejected", new
            {
                success = false,
                requestId = admission.RequestId,
                messageType = admission.MessageType,
                code = admission.ErrorCode,
                errorMessage = admission.PublicMessage
            });

            return new WebMessageResponse
            {
                RequestId = admission.RequestId,
                Success = false,
                Data = JsonSerializer.Serialize(new { code = admission.ErrorCode }, _jsonOptions),
                Error = admission.PublicMessage
            };
        }

        var principal = admission.Principal!;
        var binding = BindDeliveryPrincipal(
            principal,
            admission.ClientBindingId,
            admission.NavigationEpoch);
        var commandJson = JsonSerializer.Serialize(new
        {
            messageType = admission.MessageType,
            requestId = admission.RequestId,
            payload = admission.Payload
        }, _jsonOptions);

        try
        {
            _logger.LogInformation(
                "[WebMessageHandler] Admitted message: {MessageType}",
                admission.MessageType);

            switch (admission.MessageType)
            {
                case WebMessageAdmissionService.BindingChangedMessageType:
                    SendBoundProgressMessage("BridgeBindingChangedResult", new
                    {
                        success = true,
                        requestId = admission.RequestId
                    }, binding);
                    break;
                case nameof(PickFileCommand):
                    await _filePickerHandler.HandleAsync(commandJson, binding);
                    break;
                case "ListAiSessions":
                    await _aiSessionHandler.HandleListAsync(principal, binding);
                    break;
                case "GetAiSession":
                    await _aiSessionHandler.HandleGetAsync(commandJson, principal, binding);
                    break;
                case "DeleteAiSession":
                    await _aiSessionHandler.HandleDeleteAsync(commandJson, principal, binding);
                    break;
                case "GenerateFlow":
                    await HandleGenerateFlowCommand(commandJson, principal, binding);
                    break;
                case "CancelGenerateFlow":
                    HandleCancelGenerateFlowCommand(commandJson, principal, binding);
                    break;
                case "planar2d:solve":
                    await HandlePlanarScaleOffsetSolveCommand(commandJson, principal, binding);
                    break;
                case "planar2d:save":
                    await HandlePlanarScaleOffsetSaveCommand(commandJson, principal, binding);
                    break;
                default:
                    throw new InvalidOperationException("Default-deny WebMessage dispatcher invariant failed.");
            }

            return new WebMessageResponse
            {
                RequestId = admission.RequestId,
                Success = true
            };
        }
        catch (ConversationSessionAccessException)
        {
            SendBoundProgressMessage("WebMessageCommandFailed", new
            {
                success = false,
                requestId = admission.RequestId,
                messageType = admission.MessageType,
                code = WebMessageErrorCodes.NotFound,
                errorMessage = "Resource not found."
            }, binding);
            return new WebMessageResponse
            {
                RequestId = admission.RequestId,
                Success = false,
                Data = JsonSerializer.Serialize(new { code = WebMessageErrorCodes.NotFound }, _jsonOptions),
                Error = "Resource not found."
            };
        }
        catch (JsonException)
        {
            return CommandFailure(
                admission,
                binding,
                WebMessageErrorCodes.Validation,
                "WebMessage payload is invalid.");
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "[WebMessageHandler] Command failed. MessageType={MessageType}",
                admission.MessageType);
            return CommandFailure(
                admission,
                binding,
                WebMessageErrorCodes.Conflict,
                "WebMessage command could not be completed.");
        }
    }

    private static JsonElement ParsePayloadElement(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return JsonSerializer.SerializeToElement(new { });
        }

        try
        {
            return JsonSerializer.Deserialize<JsonElement>(payload);
        }
        catch (JsonException)
        {
            return JsonSerializer.SerializeToElement(payload);
        }
    }

    internal static bool IsLegacyExecutionMessage(string? messageType)
    {
        return string.Equals(messageType, nameof(ExecuteOperatorCommand), StringComparison.Ordinal) ||
            string.Equals(messageType, nameof(UpdateFlowCommand), StringComparison.Ordinal) ||
            string.Equals(messageType, nameof(StartInspectionCommand), StringComparison.Ordinal) ||
            string.Equals(messageType, nameof(StopInspectionCommand), StringComparison.Ordinal);
    }

    private WebMessageResponse CommandFailure(
        WebMessageAdmissionResult admission,
        WebMessageDeliveryBinding binding,
        string code,
        string publicMessage)
    {
        SendBoundProgressMessage("WebMessageCommandFailed", new
        {
            success = false,
            requestId = admission.RequestId,
            messageType = admission.MessageType,
            code,
            errorMessage = publicMessage
        }, binding);

        return new WebMessageResponse
        {
            RequestId = admission.RequestId,
            Success = false,
            Data = JsonSerializer.Serialize(new { code }, _jsonOptions),
            Error = publicMessage
        };
    }

    private WebMessageDeliveryBinding BindDeliveryPrincipal(
        AuthenticatedWebMessagePrincipal principal,
        string clientBindingId,
        long navigationEpoch)
    {
        List<ActiveGenerateFlowRequest>? requestsToCancel = null;
        WebMessageDeliveryBinding binding;
        lock (_bindingGate)
        {
            var current = _currentDeliveryBinding;
            if (current != null &&
                string.Equals(current.OwnerHash, principal.OwnerHash, StringComparison.Ordinal) &&
                string.Equals(current.ClientBindingId, clientBindingId, StringComparison.Ordinal) &&
                current.NavigationEpoch == navigationEpoch)
            {
                return current;
            }

            if (current != null)
            {
                requestsToCancel = _activeGenerateFlowRequests.Snapshot().ToList();
            }

            binding = new WebMessageDeliveryBinding(
                Interlocked.Increment(ref _deliveryEpoch),
                principal.OwnerHash,
                clientBindingId,
                navigationEpoch);
            _currentDeliveryBinding = binding;
        }

        CancelAndRemoveRequests(requestsToCancel);
        if (requestsToCancel != null)
        {
            ClearPendingBoundWebMessages();
        }
        return binding;
    }

    private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        InvalidateDeliveryBinding("navigation-starting");
    }

    private void InvalidateDeliveryBinding(string reason)
    {
        List<ActiveGenerateFlowRequest> requests;
        lock (_bindingGate)
        {
            _currentDeliveryBinding = null;
            Interlocked.Increment(ref _deliveryEpoch);
            requests = _activeGenerateFlowRequests.Snapshot().ToList();
        }

        CancelAndRemoveRequests(requests);
        ClearPendingBoundWebMessages();
        _logger.LogInformation(
            "[WebMessageHandler] Delivery binding invalidated. Reason={Reason}",
            reason);
    }

    private void CancelAndRemoveRequests(IEnumerable<ActiveGenerateFlowRequest>? requests)
    {
        if (requests == null)
        {
            return;
        }

        foreach (var request in requests)
        {
            if (TryRemoveActiveGenerateFlowRequest(request))
            {
                try
                {
                    request.CancellationTokenSource.Cancel();
                    CancelAgentRunForGenerateFlow(request);
                }
                catch (ObjectDisposedException)
                {
                }
            }
        }
    }

    private bool MatchesCurrentBinding(WebMessageDeliveryBinding binding)
    {
        lock (_bindingGate)
        {
            return _currentDeliveryBinding == binding;
        }
    }

    /// <summary>
    /// 处理 AI 生成工作流请求
    /// </summary>
    private async Task HandleGenerateFlowCommand(
        string messageJson,
        AuthenticatedWebMessagePrincipal principal,
        WebMessageDeliveryBinding binding)
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
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                var response = new GenerateFlowResponse
                {
                    Success = false,
                    Status = AiFlowGenerationResult.CompletionStatusFailed,
                    Code = WebMessageErrorCodes.Validation,
                    ErrorMessage = "GenerateFlow requires an explicit sessionId.",
                    FailureSummary = "generate_flow_session_required: GenerateFlow requires an explicit sessionId.",
                    SessionId = null,
                    RequestId = requestId,
                    ClarificationRequired = false,
                    CompletionStatus = AiFlowGenerationResult.CompletionStatusFailed,
                    InteractionState = AiInteractionStates.Failed,
                    FirstFixRecommendation = "请重新打开 AI 会话后再发起生成。"
                };
                PostWebMessageJson(SerializeGenerateFlowResponse(
                    response,
                    AiFlowGenerationResult.FailureTypeSystemError), binding);
                return;
            }

            sessionId = sessionId.Trim();
            var requirementMode = TryGetMessageString(payload, "requirementMode")
                ?? TryGetMessageString(doc.RootElement, "requirementMode");
            var templateSelection = TryGetTemplateSelection(payload);
            VisionAgentBuildFromPlanRequest? buildFromPlan;
            try
            {
                buildFromPlan = TryGetBuildFromPlan(payload);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Invalid BuildFromPlan payload received from WebView2.");
                var response = new GenerateFlowResponse
                {
                    Success = false,
                    Status = AiFlowGenerationResult.CompletionStatusFailed,
                    Code = WebMessageErrorCodes.Validation,
                    ErrorMessage = "BuildFromPlan payload is invalid.",
                    FailureSummary = "build_from_plan_payload_invalid: BuildFromPlan payload is invalid.",
                    SessionId = sessionId,
                    RequestId = requestId,
                    ClarificationRequired = false,
                    CompletionStatus = AiFlowGenerationResult.CompletionStatusFailed,
                    InteractionState = AiInteractionStates.Failed,
                    FirstFixRecommendation = "请回到左侧 Plan 工作台重新确认计划后再开始构建。"
                };
                PostWebMessageJson(SerializeGenerateFlowResponse(
                    response,
                    AiFlowGenerationResult.FailureTypeSystemError),
                    binding);
                return;
            }
            var useVisionAgentGenerateFlow = TryGetBoolean(payload, "useVisionAgentGenerateFlow") ?? false;
            var agentGenerateFlowMode = TryGetMessageString(payload, "agentGenerateFlowMode")
                ?? TryGetMessageString(doc.RootElement, "agentGenerateFlowMode");
            var runtimePreviewConsent = TryGetBoolean(payload, "runtimePreviewConsent") ?? false;
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
            var handler = scope.ServiceProvider.GetRequiredService<ClearVision.Product.Infrastructure.AI.GenerateFlowMessageHandler>();
            if (!TryRegisterGenerateFlowRequest(
                    requestId,
                    sessionId,
                    principal.OwnerHash,
                    binding,
                    out var registeredRequest))
            {
                var conflictResponse = new GenerateFlowResponse
                {
                    Success = false,
                    Status = AiFlowGenerationResult.CompletionStatusFailed,
                    Code = WebMessageErrorCodes.Conflict,
                    ErrorMessage = "An active generation already uses this request or session identifier.",
                    FailureSummary = "An active generation already uses this request or session identifier.",
                    SessionId = sessionId,
                    RequestId = requestId,
                    CompletionStatus = AiFlowGenerationResult.CompletionStatusFailed,
                    InteractionState = AiInteractionStates.Failed
                };
                PostWebMessageJson(
                    SerializeGenerateFlowResponse(
                        conflictResponse,
                        AiFlowGenerationResult.FailureTypeSystemError),
                    binding);
                return;
            }
            activeRequest = registeredRequest;

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
                buildFromPlan,
                useVisionAgentGenerateFlow,
                agentGenerateFlowMode,
                runtimePreviewConsent,
                ownerHash: principal.OwnerHash,
                onMessage: (type, payload) =>
                {
                    // payload 已是 JSON 字符串，直接拼接外层 envelope，避免反序列化再序列化的额外开销。
                    var progressJson = $"{{\"messageType\":{JsonSerializer.Serialize(type)},\"payload\":{payload}}}";
                    PostWebMessageJson(progressJson, binding);
                },
                cancellationToken: registeredRequest.CancellationTokenSource.Token,
                onAgentRunCreated: runId =>
                {
                    registeredRequest.AgentRunId = runId;
                }));

            // 发回前端（原始 JSON）
            PostWebMessageJson(resultJson, binding);
        }
        catch (HttpRequestException ex) when (ex.InnerException is System.Net.Sockets.SocketException)
        {
            _logger.LogWarning(ex, "AI 服务连接被拦截");
            SendBoundProgressMessage("AiFirewallBlocked", new
            {
                message = "检测到网络连接被阻断（可能被防火墙拦截）",
                detail = ex.Message,
                timestamp = DateTime.Now
            }, binding);
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogWarning(ex, "AI 服务请求超时");
            SendBoundProgressMessage("AiFirewallBlocked", new
            {
                message = "AI 服务连接超时（可能被防火墙拦截）",
                detail = "请检查网络环境或代理设置",
                timestamp = DateTime.Now
            }, binding);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "处理 AI 生成请求失败");

            var errorResponse = new GenerateFlowResponse
            {
                Success = false,
                Status = AiFlowGenerationResult.CompletionStatusFailed,
                Code = WebMessageErrorCodes.Conflict,
                ErrorMessage = "系统错误，请查看后端日志。",
                FailureSummary = "系统错误，请查看后端日志。",
                CompletionStatus = AiFlowGenerationResult.CompletionStatusFailed
            };

            var json = JsonSerializer.Serialize(errorResponse, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            PostWebMessageJson(json, binding);
        }
        finally
        {
            if (activeRequest != null)
            {
                UnregisterGenerateFlowRequest(activeRequest);
            }
        }
    }

    private static string SerializeGenerateFlowResponse(
        GenerateFlowResponse response,
        string? failureType)
    {
        return JsonSerializer.Serialize(new
        {
            response.Type,
            response.Success,
            response.Status,
            response.Code,
            response.ErrorMessage,
            response.FailureSummary,
            response.ClarificationRequired,
            response.SessionId,
            response.RequestId,
            response.FirstFixRecommendation,
            response.CompletionStatus,
            response.InteractionState,
            FailureType = failureType
        }, _jsonOptions);
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

    private static T? TryGetPayloadObject<T>(JsonElement payload, string propertyName)
        where T : class
    {
        if (payload.ValueKind != JsonValueKind.Object)
            return null;

        if (!payload.TryGetProperty(propertyName, out var property))
        {
            var pascalCase = char.ToUpperInvariant(propertyName[0]) + propertyName[1..];
            if (!payload.TryGetProperty(pascalCase, out property))
                return null;
        }

        if (property.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return null;

        return property.Deserialize<T>(_jsonOptions);
    }

    private static VisionAgentBuildFromPlanRequest? TryGetBuildFromPlan(JsonElement payload)
    {
        return TryGetPayloadObject<VisionAgentBuildFromPlanRequest>(payload, "buildFromPlan");
    }

    /// <summary>
    /// 处理二维平面比例偏移标定解算请求
    /// </summary>
    private async Task HandlePlanarScaleOffsetSolveCommand(
        string messageJson,
        AuthenticatedWebMessagePrincipal principal,
        WebMessageDeliveryBinding binding)
    {
        try
        {
            using var doc = JsonDocument.Parse(messageJson);
            var payload = doc.RootElement.GetProperty("payload");
            var pointSource = payload.ValueKind == JsonValueKind.Object &&
                              payload.TryGetProperty("points", out var configuredPoints)
                ? configuredPoints
                : payload;
            var projectId = payload.ValueKind == JsonValueKind.Object
                ? TryGetGuid(payload, "projectId")
                : null;
            var sourceSessionId = payload.ValueKind == JsonValueKind.Object
                ? TryGetMessageString(payload, "sessionId")
                : null;
            var assetId = payload.ValueKind == JsonValueKind.Object
                ? TryGetMessageString(payload, "assetId")
                : null;
            var cameraBindingId = payload.ValueKind == JsonValueKind.Object
                ? TryGetMessageString(payload, "cameraBindingId")
                : null;

            var points = new List<PlanarScaleOffsetCalibrationPoint>();
            foreach (var pointElement in pointSource.EnumerateArray())
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
            PreviewArtifactReferenceV1? solveArtifact = null;
            if (result.MeetsAcceptanceCriteria() && projectId.HasValue && projectId.Value != Guid.Empty)
            {
                var artifactStore = scope.ServiceProvider.GetRequiredService<PreviewArtifactStore>();
                var bundle = PlanarScaleOffsetCalibrationService.CreateCalibrationBundle(result);
                var provenance = new PlanarScaleOffsetSolveArtifactV1
                {
                    ProjectId = projectId.Value,
                    SessionId = sourceSessionId ?? string.Empty,
                    AssetId = assetId ?? string.Empty,
                    CameraBindingId = cameraBindingId ?? string.Empty,
                    Bundle = bundle
                };
                using var batch = artifactStore.CreateBatch(new PreviewArtifactOwnerScope(
                    projectId.Value,
                    Guid.Empty,
                    Guid.NewGuid(),
                    null,
                    null,
                    principal.UserId));
                solveArtifact = batch.Add(
                    "planar2dSolveBundle",
                    "calibration-solve-provenance.v1",
                    "$.Planar2D.SolveProvenance",
                    "application/json",
                    JsonSerializer.SerializeToUtf8Bytes(provenance, _jsonOptions));
                batch.Commit();
            }

            SendBoundProgressMessage("planar2d:solve:result", new
            {
                result.Success,
                result.Accepted,
                result.Message,
                result.OriginX,
                result.OriginY,
                result.ScaleX,
                result.ScaleY,
                result.MeanErrorX,
                result.MeanErrorY,
                result.Rmse,
                result.PointCount,
                solveArtifact,
                canFormalSave = solveArtifact != null
            }, binding);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "处理二维平面比例偏移标定解算失败");
            SendBoundProgressMessage("planar2d:solve:result", new
            {
                success = false,
                code = WebMessageErrorCodes.Validation,
                message = "Calibration solve request is invalid."
            }, binding);
        }
    }

    /// <summary>
    /// 处理二维平面比例偏移标定保存请求
    /// </summary>
    private async Task HandlePlanarScaleOffsetSaveCommand(
        string messageJson,
        AuthenticatedWebMessagePrincipal principal,
        WebMessageDeliveryBinding binding)
    {
        try
        {
            using var doc = JsonDocument.Parse(messageJson);
            var payload = doc.RootElement.GetProperty("payload");
            var request = payload.Deserialize<PlanarScaleOffsetFormalSaveRequest>(_jsonOptions);
            if (request == null || request.ProjectId == Guid.Empty)
            {
                SendPlanarSaveFailure(binding, WebMessageErrorCodes.Validation, "Project context is required.");
                return;
            }

            if (request.ExpectedPersistenceRevision is null)
            {
                SendPlanarSaveFailure(
                    binding,
                    WebMessageErrorCodes.Validation,
                    "expectedPersistenceRevision is required.");
                return;
            }

            using var scope = _scopeFactory.CreateScope();
            var artifactStore = scope.ServiceProvider.GetRequiredService<PreviewArtifactStore>();
            if (!artifactStore.TryReadScoped(
                    request.SolveArtifactId,
                    principal.UserId,
                    request.ProjectId,
                    "planar2dSolveBundle",
                    out var artifact) ||
                artifact == null)
            {
                SendPlanarSaveFailure(binding, WebMessageErrorCodes.NotFound, "Solve artifact not found.");
                return;
            }

            PlanarScaleOffsetSolveArtifactV1? provenance;
            try
            {
                provenance = JsonSerializer.Deserialize<PlanarScaleOffsetSolveArtifactV1>(
                    artifact.Bytes,
                    _jsonOptions);
            }
            catch (JsonException)
            {
                SendPlanarSaveFailure(binding, WebMessageErrorCodes.Validation, "Solve artifact is invalid.");
                return;
            }

            if (provenance?.Bundle == null ||
                !string.Equals(
                    artifact.Reference.Role,
                    "calibration-solve-provenance.v1",
                    StringComparison.Ordinal) ||
                !string.Equals(provenance.SchemaVersion, "calibration-solve-provenance.v1", StringComparison.Ordinal) ||
                !string.Equals(provenance.SolveKind, "planar2d", StringComparison.Ordinal) ||
                provenance.ProjectId != request.ProjectId ||
                !string.Equals(request.SessionId?.Trim() ?? string.Empty, provenance.SessionId, StringComparison.Ordinal) ||
                !string.Equals(request.AssetId?.Trim() ?? string.Empty, provenance.AssetId, StringComparison.Ordinal) ||
                !string.Equals(request.CameraBindingId?.Trim() ?? string.Empty, provenance.CameraBindingId, StringComparison.Ordinal))
            {
                SendPlanarSaveFailure(binding, WebMessageErrorCodes.NotFound, "Solve artifact not found.");
                return;
            }

            if (!CalibrationBundleV2Json.TryRequireAccepted(provenance.Bundle, out _))
            {
                SendPlanarSaveFailure(
                    binding,
                    WebMessageErrorCodes.Validation,
                    "Calibration solve artifact did not pass validation.");
                return;
            }

            var runtimeCoordinator = scope.ServiceProvider.GetRequiredService<IInspectionRuntimeCoordinator>();
            await using var mutationLease = await runtimeCoordinator.TryAcquireMutationLeaseAsync(
                request.ProjectId,
                "calibration-asset-save",
                CancellationToken.None);
            if (mutationLease == null)
            {
                SendPlanarSaveFailure(binding, WebMessageErrorCodes.Conflict, "Project is currently running.");
                return;
            }

            var projectService = scope.ServiceProvider.GetRequiredService<ProjectService>();
            var bundlePayload = JsonSerializer.SerializeToElement(
                provenance.Bundle,
                CalibrationBundleV2Json.DefaultOptions);
            var response = await projectService.SaveCalibrationAssetAsync(
                request.ProjectId,
                new ProjectCalibrationAssetSaveRequest
                {
                    ExpectedPersistenceRevision = request.ExpectedPersistenceRevision,
                    AssetId = string.IsNullOrWhiteSpace(request.AssetId)
                        ? provenance.AssetId
                        : request.AssetId,
                    Version = provenance.Bundle.CalibrationVersion,
                    Producer = nameof(PlanarScaleOffsetCalibrationService),
                    SourceDraftSessionId = provenance.SessionId,
                    ImageIdentity = provenance.CameraBindingId,
                    ExpectedContentHash = ProjectAssetJson.ComputePayloadHash(bundlePayload),
                    Payload = bundlePayload
                });

            SendBoundProgressMessage("planar2d:save:result", new
            {
                success = true,
                message = "Calibration project asset saved.",
                projectId = response.ProjectId,
                persistenceRevision = response.PersistenceRevision,
                asset = response.Asset
            }, binding);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "保存二维平面比例偏移标定 project asset 失败");
            var code = ex.Message.StartsWith("PSV011:", StringComparison.Ordinal)
                ? WebMessageErrorCodes.Conflict
                : WebMessageErrorCodes.Validation;
            SendPlanarSaveFailure(binding, code, code == WebMessageErrorCodes.Conflict
                ? "Project revision conflict."
                : "Calibration asset validation failed.");
        }
    }

    private void SendPlanarSaveFailure(
        WebMessageDeliveryBinding binding,
        string code,
        string message)
    {
        SendBoundProgressMessage("planar2d:save:result", new
        {
            success = false,
            code,
            message
        }, binding);
    }

    private void HandleCancelGenerateFlowCommand(
        string messageJson,
        AuthenticatedWebMessagePrincipal principal,
        WebMessageDeliveryBinding binding)
    {
        try
        {
            using var doc = JsonDocument.Parse(messageJson);
            var payload = doc.RootElement.TryGetProperty("payload", out var payloadElement) &&
                          payloadElement.ValueKind == JsonValueKind.Object
                ? payloadElement
                : default;

            // The envelope requestId identifies this WebMessage. Cancellation authority
            // comes only from the explicit owner + sessionId + generation requestId tuple.
            var requestId = TryGetMessageString(payload, "requestId");
            var sessionId = TryGetMessageString(payload, "sessionId");

            if (!TryResolveGenerateFlowRequest(
                    principal.OwnerHash,
                    requestId,
                    sessionId,
                    out var activeRequest))
            {
                _logger.LogInformation(
                    "收到取消 AI 生成请求，但未找到活动任务。RequestId={RequestId}, SessionId={SessionId}",
                    requestId,
                    sessionId);
                PostWebMessageJson(JsonSerializer.Serialize(new CancelGenerateFlowResponse
                {
                    Success = false,
                    Status = "not_found",
                    Code = WebMessageErrorCodes.NotFound,
                    SessionId = sessionId,
                    RequestId = requestId,
                    ErrorMessage = "未找到可取消的生成任务。"
                }, _jsonOptions), binding);
                return;
            }

            if (!activeRequest.CancellationTokenSource.IsCancellationRequested)
            {
                activeRequest.CancellationTokenSource.Cancel();
            }

            CancelAgentRunForGenerateFlow(activeRequest);

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
            }, _jsonOptions), binding);

            SendBoundProgressMessage("GenerateFlowProgress", new
            {
                message = "正在取消本次生成...",
                phase = "cancelling",
                requestId = activeRequest.RequestId,
                sessionId = activeRequest.SessionId
            }, binding);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "处理取消 AI 生成请求失败");
            PostWebMessageJson(JsonSerializer.Serialize(new CancelGenerateFlowResponse
            {
                Success = false,
                Status = "failed",
                Code = WebMessageErrorCodes.Conflict,
                SessionId = null,
                RequestId = null,
                ErrorMessage = "Cancellation could not be completed."
            }, _jsonOptions), binding);
        }
    }

    private void CancelAgentRunForGenerateFlow(ActiveGenerateFlowRequest activeRequest)
    {
        if (string.IsNullOrWhiteSpace(activeRequest.AgentRunId))
        {
            return;
        }

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var streamService = scope.ServiceProvider.GetService<IAgentRunEventStreamService>();
            if (streamService?.IsRunOwner(activeRequest.AgentRunId, activeRequest.OwnerHash) == true)
            {
                streamService.Cancel(activeRequest.AgentRunId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to cancel AgentRun for GenerateFlow WebMessage request. RunId={RunId}, RequestId={RequestId}",
                activeRequest.AgentRunId,
                activeRequest.RequestId);
        }
    }

    private bool TryRegisterGenerateFlowRequest(
        string requestId,
        string? sessionId,
        string ownerHash,
        WebMessageDeliveryBinding binding,
        out ActiveGenerateFlowRequest activeRequest)
    {
        var identity = GenerateFlowRequestIdentity.CreateForRegistration(
            ownerHash,
            sessionId,
            requestId);
        var candidate = new ActiveGenerateFlowRequest(
            identity,
            binding,
            new CancellationTokenSource());

        lock (_bindingGate)
        {
            if (_currentDeliveryBinding != binding)
            {
                candidate.Dispose();
                activeRequest = null!;
                return false;
            }

            if (!_activeGenerateFlowRequests.TryRegister(identity, candidate))
            {
                candidate.Dispose();
                activeRequest = null!;
                return false;
            }
        }

        activeRequest = candidate;
        return true;
    }

    private void UnregisterGenerateFlowRequest(ActiveGenerateFlowRequest activeRequest)
    {
        TryRemoveActiveGenerateFlowRequest(activeRequest);
        activeRequest.Dispose();
    }

    private bool TryRemoveActiveGenerateFlowRequest(ActiveGenerateFlowRequest activeRequest)
    {
        return _activeGenerateFlowRequests.CompareAndRemove(
            activeRequest.Identity,
            activeRequest);
    }

    private bool TryResolveGenerateFlowRequest(
        string ownerHash,
        string? requestId,
        string? sessionId,
        out ActiveGenerateFlowRequest activeRequest)
    {
        return _activeGenerateFlowRequests.TryResolveExact(
            ownerHash,
            sessionId,
            requestId,
            out activeRequest);
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

    private static Guid? TryGetGuid(JsonElement element, string propertyName)
    {
        var value = TryGetMessageString(element, propertyName);
        return Guid.TryParse(value, out var parsed) ? parsed : null;
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
            imageId = result.ImageId,
            imageReference = InspectionResultImageReferenceBuilder.Build(projectId, result.Id, result.ImageId),
            outputImage = result.ImageId.HasValue
                ? null
                : (result.OutputImage != null ? Convert.ToBase64String(result.OutputImage) : null),
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

    internal void SendBoundEvent<T>(T eventData, WebMessageDeliveryBinding binding)
    {
        var json = JsonSerializer.Serialize(eventData, _jsonOptions);
        PostWebMessageJson(json, binding);
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

    internal void SendBoundProgressMessage(
        string type,
        object payload,
        WebMessageDeliveryBinding binding)
    {
        var json = JsonSerializer.Serialize(new
        {
            messageType = type,
            payload
        }, _jsonOptions);

        PostWebMessageJson(json, binding);
    }

    void IWebMessageClient.SendBoundEvent<T>(T eventData, WebMessageDeliveryBinding binding) =>
        SendBoundEvent(eventData, binding);

    void IWebMessageClient.SendBoundProgressMessage(
        string type,
        object payload,
        WebMessageDeliveryBinding binding) =>
        SendBoundProgressMessage(type, payload, binding);

    private void PostWebMessageJson(string json, WebMessageDeliveryBinding? binding = null)
    {
        if (binding != null && !MatchesCurrentBinding(binding))
        {
            return;
        }

        var webViewControl = _webViewControl;
        var webView = _webView;
        if (webViewControl == null || webView == null)
        {
            TryEnqueuePendingWebMessage(json, binding);
            return;
        }

        if (webViewControl.IsDisposed)
        {
            return;
        }

        if (webViewControl.InvokeRequired)
        {
            if (TryEnqueuePendingWebMessage(json, binding))
            {
                SchedulePendingWebMessageDrain(webViewControl);
                return;
            }

            _ = webViewControl.BeginInvoke(new Action(() =>
            {
                if (webViewControl.IsDisposed)
                    return;

                try
                {
                    if (binding == null || MatchesCurrentBinding(binding))
                    {
                        webView.PostWebMessageAsJson(json);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "[WebMessageHandler] 异步推送 WebMessage 失败");
                }
            }));
            return;
        }

        if (binding == null || MatchesCurrentBinding(binding))
        {
            webView.PostWebMessageAsJson(json);
        }
    }

    private bool TryEnqueuePendingWebMessage(
        string json,
        WebMessageDeliveryBinding? binding = null)
    {
        while (Volatile.Read(ref _pendingWebMessageCount) >= MaxPendingWebMessages)
        {
            if (!_pendingWebMessages.TryDequeue(out _))
            {
                break;
            }

            Interlocked.Decrement(ref _pendingWebMessageCount);
            Interlocked.Increment(ref _droppedWebMessageCount);
        }

        _pendingWebMessages.Enqueue(new PendingWebMessage(json, binding));
        Interlocked.Increment(ref _pendingWebMessageCount);
        return true;
    }

    private void SchedulePendingWebMessageDrain(WebView2 webViewControl)
    {
        if (Interlocked.CompareExchange(ref _webMessageDrainScheduled, 1, 0) != 0)
        {
            return;
        }

        try
        {
            _ = webViewControl.BeginInvoke(new Action(DrainPendingWebMessages));
        }
        catch (Exception ex)
        {
            Interlocked.Exchange(ref _webMessageDrainScheduled, 0);
            ClearPendingWebMessages();
            _logger.LogDebug(ex, "[WebMessageHandler] Failed to schedule WebMessage drain.");
        }
    }

    private void DrainPendingWebMessages()
    {
        try
        {
            var webViewControl = _webViewControl;
            var webView = _webView;
            if (webViewControl == null || webView == null || webViewControl.IsDisposed)
            {
                ClearPendingWebMessages();
                return;
            }

            while (_pendingWebMessages.TryDequeue(out var pending))
            {
                Interlocked.Decrement(ref _pendingWebMessageCount);
                if (webViewControl.IsDisposed)
                {
                    ClearPendingWebMessages();
                    return;
                }

                try
                {
                    if (pending.Binding == null || MatchesCurrentBinding(pending.Binding))
                    {
                        webView.PostWebMessageAsJson(pending.Json);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "[WebMessageHandler] Failed to post queued WebMessage.");
                }
            }
        }
        finally
        {
            Interlocked.Exchange(ref _webMessageDrainScheduled, 0);
            if (Volatile.Read(ref _pendingWebMessageCount) > 0)
            {
                var webViewControl = _webViewControl;
                if (webViewControl != null && !webViewControl.IsDisposed)
                {
                    SchedulePendingWebMessageDrain(webViewControl);
                }
                else
                {
                    ClearPendingWebMessages();
                }
            }
        }
    }

    private void ClearPendingWebMessages()
    {
        while (_pendingWebMessages.TryDequeue(out _))
        {
        }

        Interlocked.Exchange(ref _pendingWebMessageCount, 0);
    }

    private void ClearPendingBoundWebMessages()
    {
        var unbound = new List<PendingWebMessage>();
        while (_pendingWebMessages.TryDequeue(out var pending))
        {
            Interlocked.Decrement(ref _pendingWebMessageCount);
            if (pending.Binding == null)
            {
                unbound.Add(pending);
            }
        }

        foreach (var pending in unbound)
        {
            _pendingWebMessages.Enqueue(pending);
            Interlocked.Increment(ref _pendingWebMessageCount);
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposeState, 1) != 0)
        {
            return;
        }
        _logger.LogInformation("[WebMessageHandler] 正在释放资源");

        ClearPendingWebMessages();

        var activeGenerateFlowRequests = _activeGenerateFlowRequests.Snapshot();
        foreach (var activeRequest in activeGenerateFlowRequests)
        {
            try
            {
                if (!activeRequest.CancellationTokenSource.IsCancellationRequested)
                {
                    activeRequest.CancellationTokenSource.Cancel();
                }

                CancelAgentRunForGenerateFlow(activeRequest);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "[WebMessageHandler] 取消活动 AI 生成请求失败");
            }
        }

        foreach (var activeRequest in activeGenerateFlowRequests)
        {
            activeRequest.Dispose();
        }

        _activeGenerateFlowRequests.Clear();

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
            webView.NavigationStarting -= OnNavigationStarting;
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
            GenerateFlowRequestIdentity identity,
            WebMessageDeliveryBinding binding,
            CancellationTokenSource cancellationTokenSource)
        {
            Identity = identity;
            Binding = binding;
            CancellationTokenSource = cancellationTokenSource;
        }

        public GenerateFlowRequestIdentity Identity { get; }

        public string RequestId => Identity.RequestId;

        public string? SessionId => string.IsNullOrEmpty(Identity.SessionId)
            ? null
            : Identity.SessionId;

        public string OwnerHash => Identity.OwnerHash;

        public WebMessageDeliveryBinding Binding { get; }

        public string? AgentRunId { get; set; }

        public CancellationTokenSource CancellationTokenSource { get; }

        public void Dispose()
        {
            CancellationTokenSource.Dispose();
        }
    }

    private sealed class PlanarScaleOffsetSolveArtifactV1
    {
        public string SchemaVersion { get; init; } = "calibration-solve-provenance.v1";
        public string SolveKind { get; init; } = "planar2d";
        public Guid ProjectId { get; init; }
        public string SessionId { get; init; } = string.Empty;
        public string AssetId { get; init; } = string.Empty;
        public string CameraBindingId { get; init; } = string.Empty;
        public CalibrationBundleV2? Bundle { get; init; }
    }

    private sealed class PlanarScaleOffsetFormalSaveRequest
    {
        public string? SolveArtifactId { get; init; }
        public Guid ProjectId { get; init; }
        public long? ExpectedPersistenceRevision { get; init; }
        public string? AssetId { get; init; }
        public string? SessionId { get; init; }
        public string? CameraBindingId { get; init; }
    }
}
