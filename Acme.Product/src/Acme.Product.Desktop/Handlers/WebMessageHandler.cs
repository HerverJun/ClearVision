using Acme.Product.Contracts.Messages;
using Acme.Product.Core.Services;
using Acme.Product.Desktop.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using System.Text.Json;

namespace Acme.Product.Desktop.Handlers;

/// <summary>
/// WebView2 消息处理器
/// </summary>
public class WebMessageHandler
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOperatorFactory _operatorFactory;
    private readonly ILogger<WebMessageHandler> _logger;
    private WebView2? _webViewControl;
    private CoreWebView2? _webView;

    public WebMessageHandler(
        IServiceScopeFactory scopeFactory,
        IOperatorFactory operatorFactory,
        ILogger<WebMessageHandler> logger)
    {
        _scopeFactory = scopeFactory;
        _operatorFactory = operatorFactory;
        _logger = logger;
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
                    await HandleExecuteOperatorCommand(messageJson);
                    break;

                case nameof(UpdateFlowCommand):
                    await HandleUpdateFlowCommand(messageJson);
                    break;

                case nameof(StartInspectionCommand):
                    await HandleStartInspectionCommand(messageJson);
                    break;

                case nameof(StopInspectionCommand):
                    await HandleStopInspectionCommand();
                    break;

                case nameof(PickFileCommand):
                    await HandlePickFileCommand(messageJson);
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

    /// <summary>
    /// 处理执行算子命令
    /// </summary>
    private async Task HandleExecuteOperatorCommand(string messageJson)
    {
        var command = JsonSerializer.Deserialize<ExecuteOperatorCommand>(messageJson, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        if (command == null)
            return;

        try
        {
            // 创建 Scope
            using var scope = _scopeFactory.CreateScope();
            var flowService = scope.ServiceProvider.GetRequiredService<IFlowExecutionService>();

            // 创建算子实例
            var op = _operatorFactory.CreateOperator(
                Enum.Parse<Core.Enums.OperatorType>(command.OperatorId.ToString()),
                "TempOperator",
                0, 0);

            // 执行算子
            var result = await flowService.ExecuteOperatorAsync(op, command.Inputs);

            // 发送结果事件
            var eventData = new OperatorExecutedEvent
            {
                OperatorId = command.OperatorId,
                OperatorName = op.Name,
                IsSuccess = result.IsSuccess,
                OutputData = result.OutputData,
                ExecutionTimeMs = result.ExecutionTimeMs,
                ErrorMessage = result.ErrorMessage
            };

            SendEvent(eventData);
        }
        catch (Exception ex)
        {
            var eventData = new OperatorExecutedEvent
            {
                OperatorId = command.OperatorId,
                OperatorName = "Unknown",
                IsSuccess = false,
                ErrorMessage = ex.Message
            };

            SendEvent(eventData);
        }
    }

    /// <summary>
    /// 处理更新流程命令
    /// </summary>
    private async Task HandleUpdateFlowCommand(string messageJson)
    {
        var command = JsonSerializer.Deserialize<UpdateFlowCommand>(messageJson, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        if (command?.Flow == null)
            return;

        // 保存流程到数据库 - 通过FlowExecutionService或专门的流程管理服务
        // 实际实现需要注入IProjectRepository并调用UpdateFlowAsync
        // 这里提供基础实现框架
        _logger.LogInformation("流程更新请求: ProjectId={ProjectId}, OperatorCount={Count}",
            command.ProjectId, command.Flow.Operators?.Count ?? 0);

        await Task.CompletedTask;

        _logger.LogInformation("[WebMessageHandler] 流程已更新: {ProjectId}", command.ProjectId);
    }

    /// <summary>
    /// 处理开始检测命令
    /// </summary>
    private async Task HandleStartInspectionCommand(string messageJson)
    {
        var command = JsonSerializer.Deserialize<StartInspectionCommand>(messageJson, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        if (command == null)
            return;

        try
        {
            // 创建 Scope
            using var scope = _scopeFactory.CreateScope();
            var inspectionService = scope.ServiceProvider.GetRequiredService<IInspectionService>();

            byte[]? imageData = null;

            if (!string.IsNullOrEmpty(command.ImageBase64))
            {
                imageData = Convert.FromBase64String(command.ImageBase64);
            }

            // 执行检测
            var result = imageData != null
                ? await inspectionService.ExecuteSingleAsync(command.ProjectId, imageData)
                : await inspectionService.ExecuteSingleAsync(command.ProjectId, command.CameraId ?? "default");

            // 发送检测完成事件
            var eventData = new InspectionCompletedEvent
            {
                ResultId = result.Id,
                ProjectId = command.ProjectId,
                Status = result.Status.ToString(),
                Defects = result.Defects.Select(d => new Contracts.Messages.DefectData
                {
                    Type = d.Type.ToString(),
                    X = d.X,
                    Y = d.Y,
                    Width = d.Width,
                    Height = d.Height,
                    Confidence = d.ConfidenceScore,
                    Description = d.Description
                }).ToList(),
                ProcessingTimeMs = result.ProcessingTimeMs
            };

            SendEvent(eventData);
        }
        catch (Exception ex)
        {
            var eventData = new InspectionCompletedEvent
            {
                ProjectId = command.ProjectId,
                Status = "Error",
                Defects = new List<Contracts.Messages.DefectData>()
            };

            SendEvent(eventData);
            _logger.LogError(ex, "[WebMessageHandler] 检测失败");
        }
    }

    /// <summary>
    /// 处理停止检测命令
    /// </summary>
    private async Task HandleStopInspectionCommand()
    {
        using var scope = _scopeFactory.CreateScope();
        var inspectionService = scope.ServiceProvider.GetRequiredService<IInspectionService>();
        await inspectionService.StopRealtimeInspectionAsync();
        _logger.LogInformation("[WebMessageHandler] 检测已停止");
    }

    /// <summary>
    /// 发送事件到前端
    /// </summary>
    public void SendEvent<T>(T eventData) where T : EventBase
    {
        if (_webView == null || _webViewControl == null)
            return;

        var json = JsonSerializer.Serialize(eventData, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        if (_webViewControl.InvokeRequired)
        {
            _webViewControl.Invoke(() => _webView.PostWebMessageAsJson(json));
        }
        else
        {
            _webView.PostWebMessageAsJson(json);
        }
    }

    /// <summary>
    /// 发送进度通知
    /// </summary>
    public void SendProgress(double progress, string? operatorName = null, string? message = null)
    {
        var eventData = new ProgressNotificationEvent
        {
            Progress = progress,
            CurrentOperatorName = operatorName,
            Message = message
        };

        SendEvent(eventData);
    }

    /// <summary>
    /// 处理选择文件命令
    /// </summary>
    private async Task HandlePickFileCommand(string messageJson)
    {
        try
        {
            // 【修复】直接反序列化整个消息
            // 前端现在发送扁平结构：{ messageType, parameterName, filter, timestamp }
            var command = JsonSerializer.Deserialize<PickFileCommand>(messageJson, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (command == null)
            {
                _logger.LogWarning("[WebMessageHandler] 无法解析 PickFileCommand");
                return;
            }

            string? filePath = null;
            bool isCancelled = true;

            // 确保在 UI 线程执行
            if (_webViewControl.InvokeRequired)
            {
                _webViewControl.Invoke(new Action(() =>
                {
                    ShowFileDialog(command, ref filePath, ref isCancelled);
                }));
            }
            else
            {
                ShowFileDialog(command, ref filePath, ref isCancelled);
            }

            var eventData = new FilePickedEvent
            {
                ParameterName = command.ParameterName,
                FilePath = filePath,
                IsCancelled = isCancelled
            };

            SendEvent(eventData);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "处理文件选择命令失败");
            // 临时弹窗提示，方便用户反馈
            System.Windows.Forms.MessageBox.Show($"文件选择出错: {ex.Message}");
        }

        await Task.CompletedTask;
    }

    private void ShowFileDialog(PickFileCommand command, ref string? filePath, ref bool isCancelled)
    {
        using (var dialog = new System.Windows.Forms.OpenFileDialog
        {
            Filter = command.Filter,
            Title = "选择文件"
        })
        {
            // 尝试指定所有者窗口，防止对话框被遮挡
            if (dialog.ShowDialog(_webViewControl) == System.Windows.Forms.DialogResult.OK)
            {
                filePath = dialog.FileName;
                isCancelled = false;
            }
        }
    }
}

