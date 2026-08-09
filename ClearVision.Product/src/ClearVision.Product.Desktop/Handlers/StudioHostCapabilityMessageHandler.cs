using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace ClearVision.Product.Desktop.Handlers;

internal sealed class StudioHostCapabilityMessageHandler :
    IDesktopWebMessageOwner,
    IHostMessageClient
{
    internal const string FilePickerMessageType = "PickFileCommand";

    private readonly ILogger<StudioHostCapabilityMessageHandler> _logger;
    private readonly FilePickerMessageHandler _filePickerHandler;
    private WebView2? _webViewControl;
    private CoreWebView2? _webView;
    private int _disposeState;
    private long _rejectedMessageCount;

    public StudioHostCapabilityMessageHandler(
        ILogger<StudioHostCapabilityMessageHandler> logger,
        ILoggerFactory loggerFactory)
    {
        _logger = logger;
        _filePickerHandler = new FilePickerMessageHandler(
            this,
            loggerFactory.CreateLogger<FilePickerMessageHandler>());
    }

    public string Surface => "studio-host-capabilities";

    public int ActiveSubscriptionCount => _webView is null ? 0 : 1;

    internal long RejectedMessageCount => Interlocked.Read(ref _rejectedMessageCount);

    internal static bool IsAllowedMessageType(string? messageType) =>
        string.Equals(messageType, FilePickerMessageType, StringComparison.Ordinal);

    public void Initialize(WebView2 webViewControl)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposeState) != 0, this);
        if (webViewControl?.CoreWebView2 is null)
        {
            throw new InvalidOperationException("WebView2 content is not initialized.");
        }

        if (_webView is not null)
        {
            throw new InvalidOperationException("Studio host capability channel is already initialized.");
        }

        _webViewControl = webViewControl;
        _webView = webViewControl.CoreWebView2;
        _webView.WebMessageReceived += OnWebMessageReceived;
    }

    private void OnWebMessageReceived(
        object? sender,
        CoreWebView2WebMessageReceivedEventArgs args)
    {
        _ = HandleWebMessageAsync(args);
    }

    private async Task HandleWebMessageAsync(CoreWebView2WebMessageReceivedEventArgs args)
    {
        try
        {
            using var document = JsonDocument.Parse(args.WebMessageAsJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty("messageType", out var messageTypeElement) ||
                messageTypeElement.ValueKind != JsonValueKind.String)
            {
                RejectMessage("missing-or-invalid-message-type");
                return;
            }

            var messageType = messageTypeElement.GetString();
            if (!IsAllowedMessageType(messageType))
            {
                RejectMessage(messageType ?? "missing-message-type");
                return;
            }

            await _filePickerHandler.HandleAsync(args.WebMessageAsJson);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "[StudioHostCapabilities] Rejected malformed WebMessage payload.");
            Interlocked.Increment(ref _rejectedMessageCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[StudioHostCapabilities] Host capability request failed.");
        }
    }

    private void RejectMessage(string messageType)
    {
        Interlocked.Increment(ref _rejectedMessageCount);
        _logger.LogWarning(
            "[StudioHostCapabilities] Rejected non-host WebMessage: {MessageType}",
            messageType);
    }

    public void SendEvent<T>(T eventData)
    {
        if (Volatile.Read(ref _disposeState) != 0)
        {
            return;
        }

        var json = JsonSerializer.Serialize(eventData, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
        PostWebMessageJson(json);
    }

    private void PostWebMessageJson(string json)
    {
        var webViewControl = _webViewControl;
        var webView = _webView;
        if (webViewControl is null || webView is null || webViewControl.IsDisposed)
        {
            return;
        }

        if (webViewControl.InvokeRequired)
        {
            _ = webViewControl.BeginInvoke(new Action(() =>
            {
                if (Volatile.Read(ref _disposeState) != 0 || webViewControl.IsDisposed)
                {
                    return;
                }

                try
                {
                    webView.PostWebMessageAsJson(json);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "[StudioHostCapabilities] WebMessage response was not delivered.");
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

        var webViewControl = _webViewControl;
        var webView = _webView;
        _webViewControl = null;
        _webView = null;

        if (webViewControl is null || webView is null || webViewControl.IsDisposed || webViewControl.InvokeRequired)
        {
            return;
        }

        try
        {
            webView.WebMessageReceived -= OnWebMessageReceived;
        }
        catch (Exception ex) when (
            ex is InvalidOperationException or InvalidCastException or COMException)
        {
            _logger.LogDebug(ex, "[StudioHostCapabilities] WebView2 was unavailable during detach.");
        }
    }
}
