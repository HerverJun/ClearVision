// WebView2Host.cs
// 异步释放资源。
// 作者：蘅芜君

using System.Net;
using System.Reflection;
using System.Text.Json;
using ClearVision.Product.Contracts.Messages;
using ClearVision.Product.Desktop.Configuration;
using ClearVision.Product.Desktop.Handlers;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace ClearVision.Product.Desktop;

/// <summary>
/// WebView2 宿主类，负责 WebView2 控件的异步初始化和配置。
/// 基于《代码实践指导》中的 WebView2Host 设计模式。
/// </summary>
public sealed class WebView2Host : IAsyncDisposable
{
    private static readonly JsonSerializerOptions StartupScriptJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly WebView2 _webView;
    private readonly StudioOptions _studioOptions;
    private CoreWebView2Environment? _environment;
    private bool _isInitialized;
    private bool _isDisposed;
    private readonly WebMessageHandler? _messageHandler;

    internal static Uri CreateInitialPageUri(int webPort)
    {
        if (webPort < 1 || webPort > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(webPort), "Web port must be between 1 and 65535.");
        }

        return new Uri($"http://localhost:{webPort}/index.html");
    }

    internal static Uri CreateInitialPageUri(int webPort, bool workspaceV2Enabled)
    {
        var decision = new StudioStartupPageDecision(
            workspaceV2Enabled ? StudioStartupPageKind.FrontendV2 : StudioStartupPageKind.Legacy,
            workspaceV2Enabled,
            workspaceV2Enabled ? StudioStartupPageResolver.FrontendV2PagePath : StudioStartupPageResolver.LegacyPagePath,
            RequiredFilePath: null,
            DiagnosticMessage: null);

        return StudioStartupPageResolver.CreateInitialPageUri(webPort, decision);
    }

    internal static string BuildStartupInjectionScript(
        bool workspaceV2Enabled,
        string apiBaseUrl,
        string cssVersion,
        bool nodePreviewInspectorEnabled = false,
        bool propertyPanelCapabilityEnabled = false,
        bool previewPanelCapabilityEnabled = false,
        bool globalVariablesCapabilityEnabled = false,
        bool settingsCapabilityEnabled = false,
        bool projectPageCapabilityEnabled = false,
        bool inspectionCapabilityEnabled = false,
        bool resultsReviewCapabilityEnabled = false,
        bool aiPanelCapabilityEnabled = false,
        bool circleSearchV2ToolEnabled = true,
        bool nPointCalibrationWorkbenchEnabled = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiBaseUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(cssVersion);

        var startup = new
        {
            workspaceV2Enabled,
            nodePreviewInspectorEnabled,
            apiBaseUrl,
            hostKind = "desktop-webview2",
            frontendV2BasePath = StudioStartupPageResolver.FrontendV2BasePath,
            featureFlags = new Dictionary<string, bool>
            {
                ["Studio:NodePreviewInspectorEnabled"] = nodePreviewInspectorEnabled,
                ["Studio2.PropertyPanel"] = propertyPanelCapabilityEnabled,
                ["Studio2.PreviewPanel"] = previewPanelCapabilityEnabled,
                ["Studio2.GlobalVariables"] = globalVariablesCapabilityEnabled,
                ["Studio2.Settings"] = settingsCapabilityEnabled,
                ["Studio2.ProjectPage"] = projectPageCapabilityEnabled,
                ["Studio2.Inspection"] = inspectionCapabilityEnabled,
                ["Studio2.ResultsReview"] = resultsReviewCapabilityEnabled,
                ["Studio2.AiPanel"] = aiPanelCapabilityEnabled,
                ["Studio:CircleSearchV2ToolEnabled"] = circleSearchV2ToolEnabled,
                ["Studio:NPointCalibrationWorkbenchEnabled"] = nPointCalibrationWorkbenchEnabled
            }
        };

        var startupJson = JsonSerializer.Serialize(startup, StartupScriptJsonOptions);
        var apiBaseUrlJson = JsonSerializer.Serialize(apiBaseUrl, StartupScriptJsonOptions);
        var cssVersionJson = JsonSerializer.Serialize(cssVersion, StartupScriptJsonOptions);

        return $$"""
            (() => {
                const startup = {{startupJson}};
                const featureFlags = Object.freeze({
                    ...(startup.featureFlags && typeof startup.featureFlags === 'object'
                        ? startup.featureFlags
                        : {})
                });
                Object.defineProperty(startup, 'featureFlags', {
                    value: featureFlags,
                    writable: false,
                    configurable: false,
                    enumerable: true
                });
                Object.freeze(startup);
                Object.defineProperty(window, '__CLEARVISION_STARTUP__', {
                    value: startup,
                    writable: false,
                    configurable: false,
                    enumerable: true
                });
                window.__API_BASE_URL__ = {{apiBaseUrlJson}};
                window.__CSS_VERSION__ = {{cssVersionJson}};
                console.log('[Desktop] API Base URL:', window.__API_BASE_URL__);
                console.log('[Desktop] CSS Version:', window.__CSS_VERSION__);
                console.log('[Desktop] Startup:', window.__CLEARVISION_STARTUP__);
            })();
        """;
    }

    /// <summary>
    /// WebView2 初始化完成事件。
    /// </summary>
    public event EventHandler? Initialized;

    /// <summary>
    /// 收到 Web 消息事件。
    /// </summary>
    public event EventHandler<WebMessage>? MessageReceived;

    /// <summary>
    /// 获取是否已初始化。
    /// </summary>
    public bool IsInitialized => _isInitialized;

    /// <summary>
    /// 获取 CoreWebView2 实例（初始化后可用）。
    /// </summary>
    public CoreWebView2? CoreWebView2 => _webView.CoreWebView2;

    /// <summary>
    /// 创建 WebView2 宿主实例。
    /// </summary>
    /// <param name="webView">WebView2 控件实例</param>
    /// <param name="messageHandler">Web 消息处理器</param>
    public WebView2Host(
        WebView2 webView,
        WebMessageHandler? messageHandler = null,
        StudioOptions? studioOptions = null)
    {
        _webView = webView ?? throw new ArgumentNullException(nameof(webView));
        _messageHandler = messageHandler;
        _studioOptions = studioOptions ?? new StudioOptions();
    }

    /// <summary>
    /// 异步初始化 WebView2 环境和控件。
    /// </summary>
    /// <param name="userDataFolder">用户数据文件夹路径（可选）</param>
    /// <param name="language">语言设置（可选，默认 zh-CN）</param>
    public async Task InitializeAsync(
        string? userDataFolder = null,
        string language = "zh-CN")
    {
        if (_isInitialized)
        {
            return;
        }

        ObjectDisposedException.ThrowIf(_isDisposed, this);

        try
        {
            // 配置用户数据文件夹
            var dataFolder = userDataFolder ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ClearVision.Product",
                "WebView2");

            // 确保目录存在
            Directory.CreateDirectory(dataFolder);

            // 创建自定义环境
            _environment = await CoreWebView2Environment.CreateAsync(
                browserExecutableFolder: null,
                userDataFolder: dataFolder,
                options: new CoreWebView2EnvironmentOptions
                {
                    AdditionalBrowserArguments = $"--lang={language}",
                    AllowSingleSignOnUsingOSPrimaryAccount = true
                });

            // 确保 CoreWebView2 初始化完成
            await _webView.EnsureCoreWebView2Async(_environment);

            // 配置 WebView2
            await ConfigureWebView2Async();

            // 注册消息处理器
            RegisterMessageHandlers();

            // 加载初始页面
            LoadInitialPage();

            _isInitialized = true;

            // 触发初始化完成事件
            Initialized?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"WebView2 初始化失败: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// 配置 WebView2 设置。
    /// </summary>
    private async Task ConfigureWebView2Async()
    {
        var core = _webView.CoreWebView2
            ?? throw new InvalidOperationException("WebView2 Core 尚未初始化");
        var settings = core.Settings;

        // 开发工具配置（发布时应禁用）
#if DEBUG
        settings.AreDevToolsEnabled = true;
        settings.AreDefaultContextMenusEnabled = true;
#else
        settings.AreDevToolsEnabled = false;
        settings.AreDefaultContextMenusEnabled = false;
#endif

        // 禁用不需要的功能
        settings.IsStatusBarEnabled = false;
        settings.IsZoomControlEnabled = false;

        // 启用本地文件访问（允许ES6模块加载）
        core.SetVirtualHostNameToFolderMapping(
            "app.local",
            DesktopWebRootResolver.Resolve(),
            CoreWebView2HostResourceAccessKind.Allow);

#if DEBUG
        await ClearDiskCacheAsync(core);
        System.Diagnostics.Debug.WriteLine("[WebView2Host] DEBUG模式：已清除 WebView2 缓存");
#endif

        // 注入 API 配置脚本和动态版本号（在每个文档创建时执行）
        var apiPort = Program.GetWebPort();
        var apiBaseUrl = $"http://localhost:{apiPort}/api";
        var cssVersion = GenerateCssVersion();
        var initScript = BuildStartupInjectionScript(
            _studioOptions.WorkspaceV2Enabled,
            apiBaseUrl,
            cssVersion,
            _studioOptions.NodePreviewInspectorEnabled,
            _studioOptions.PropertyPanelCapabilityEnabled,
            _studioOptions.PreviewPanelCapabilityEnabled,
            _studioOptions.GlobalVariablesCapabilityEnabled,
            _studioOptions.SettingsCapabilityEnabled,
            _studioOptions.ProjectPageCapabilityEnabled,
            _studioOptions.InspectionCapabilityEnabled,
            _studioOptions.ResultsReviewCapabilityEnabled,
            _studioOptions.AiPanelCapabilityEnabled,
            _studioOptions.CircleSearchV2ToolEnabled,
            _studioOptions.NPointCalibrationWorkbenchEnabled);
        await core.AddScriptToExecuteOnDocumentCreatedAsync(initScript);
        System.Diagnostics.Debug.WriteLine($"[WebView2Host] 已注入 API 配置脚本: {apiBaseUrl}");
        System.Diagnostics.Debug.WriteLine($"[WebView2Host] CSS版本号: {cssVersion}");

        // 新窗口处理：强制在当前窗口打开
        core.NewWindowRequested += (sender, e) =>
        {
            e.Handled = true;
            core.Navigate(e.Uri);
        };

        // 导航完成处理
        core.NavigationCompleted += (sender, e) =>
        {
            if (!e.IsSuccess)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"导航失败: {e.WebErrorStatus}");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine(
                    $"导航完成: {core.Source}");
            }
        };
    }

    /// <summary>
    /// 注册消息处理器。
    /// </summary>
    private void RegisterMessageHandlers()
    {
        // 【修复】移除重复的 WebMessageReceived 订阅
        // WebMessageHandler.Initialize() 已注册此事件，并能灵活匹配
        // 前端发送的 messageType/type/Type 等不同字段名。
        // WebView2Host 的 OnWebMessageReceived 将 JSON 反序列化为 WebMessage（期望 Type 属性），
        // 但前端实际使用 messageType 字段名，导致 Type 为空、消息匹配失败。
        // 保留此方法以便未来需要时重新启用。
        // _webView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;
    }

    /// <summary>
    /// 处理收到的 Web 消息。
    /// </summary>
    private void OnWebMessageReceived(
        object? sender,
        CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            var message = JsonSerializer.Deserialize<WebMessage>(
                e.WebMessageAsJson,
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

            if (message is not null)
            {
                // 触发消息接收事件
                MessageReceived?.Invoke(this, message);

                // 处理消息并发送响应
                _ = HandleMessageAsync(message);
            }
        }
        catch (JsonException ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"无法解析 Web 消息: {ex.Message}");
        }
    }

    /// <summary>
    /// 异步处理消息。
    /// </summary>
    private async Task HandleMessageAsync(WebMessage message)
    {
        try
        {
            if (_messageHandler != null)
            {
                // 委托给 WebMessageHandler 处理
                var result = await _messageHandler.HandleAsync(message);
                await SendMessageAsync(result);
            }
            else
            {
                // 如果没有处理器，返回错误
                await SendMessageAsync(new WebMessageResponse
                {
                    RequestId = message.Id,
                    Success = false,
                    Error = "消息处理器未初始化"
                });
            }
        }
        catch (Exception ex)
        {
            var errorResponse = new WebMessageResponse
            {
                RequestId = message.Id,
                Success = false,
                Error = ex.Message
            };

            await SendMessageAsync(errorResponse);
        }
    }

    /// <summary>
    /// 执行 JavaScript 脚本。
    /// </summary>
    /// <typeparam name="T">消息类型</typeparam>
    /// <param name="message">消息对象</param>
    public Task SendMessageAsync<T>(T message) where T : class
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        if (!_isInitialized || _webView.CoreWebView2 is null)
        {
            throw new InvalidOperationException("WebView2 尚未初始化");
        }

        var json = JsonSerializer.Serialize(message, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        // 在 UI 线程上执行
        if (_webView.InvokeRequired)
        {
            _webView.Invoke(() => _webView.CoreWebView2.PostWebMessageAsJson(json));
        }
        else
        {
            _webView.CoreWebView2.PostWebMessageAsJson(json);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// 加载初始页面。
    /// </summary>
    private void LoadInitialPage()
    {
        var decision = StudioStartupPageResolver.Resolve(_studioOptions);
        if (decision.IsNavigable)
        {
            // Keep the WebView2 document and API calls on the same localhost
            // origin. In packaged installs, app.local -> localhost can be
            // blocked or misreported as a missing backend.
            _webView.Source = StudioStartupPageResolver.CreateInitialPageUri(
                Program.GetWebPort(),
                decision);
        }
        else
        {
            _webView.CoreWebView2.NavigateToString(BuildStartupDiagnosticHtml(decision));
        }
    }

    private static string BuildStartupDiagnosticHtml(StudioStartupPageDecision decision)
    {
        var title = decision.WorkspaceV2Enabled
            ? "Studio 2.0 V2 启动失败"
            : "ClearVision Product";
        var message = decision.DiagnosticMessage ?? "WebView2 已成功初始化，但未找到前端入口文件。";

        return $$"""
            <!DOCTYPE html>
            <html lang="zh-CN">
            <head>
                <meta charset="UTF-8">
                <title>{{WebUtility.HtmlEncode(title)}}</title>
                <style>
                    body {
                        font-family: 'Segoe UI', 'Microsoft YaHei', sans-serif;
                        display: flex;
                        justify-content: center;
                        align-items: center;
                        min-height: 100vh;
                        margin: 0;
                        background: #111827;
                        color: #f9fafb;
                    }
                    main {
                        width: min(720px, calc(100vw - 48px));
                        border: 1px solid #374151;
                        border-radius: 8px;
                        padding: 24px;
                        background: #1f2937;
                    }
                    h1 { font-size: 24px; margin: 0 0 12px; }
                    p { font-size: 14px; line-height: 1.7; margin: 0; color: #d1d5db; }
                </style>
            </head>
            <body>
                <main>
                    <h1>{{WebUtility.HtmlEncode(title)}}</h1>
                    <p>{{WebUtility.HtmlEncode(message)}}</p>
                </main>
            </body>
            </html>
            """;
    }

    /// <summary>
    /// 导航到指定 URL。
    /// </summary>
    /// <param name="url">目标 URL</param>
    public void Navigate(string url)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        if (_webView.CoreWebView2 is null)
        {
            throw new InvalidOperationException("WebView2 尚未初始化");
        }

        _webView.CoreWebView2.Navigate(url);
    }

    /// <summary>
    /// 执行 JavaScript 脚本。
    /// </summary>
    /// <param name="script">JavaScript 代码</param>
    /// <returns>脚本执行结果</returns>
    public async Task<string> ExecuteScriptAsync(string script)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        if (!_isInitialized || _webView.CoreWebView2 is null)
        {
            throw new InvalidOperationException("WebView2 尚未初始化");
        }

        return await _webView.CoreWebView2.ExecuteScriptAsync(script);
    }

    /// <summary>
    /// 发送共享缓冲区数据（用于高性能图像传输）
    /// </summary>
    public void SendSharedBuffer(byte[] data, int width, int height)
    {
        if (!_isInitialized || _webView.CoreWebView2 is null || _environment is null)
            return;

        try
        {
            // 创建共享缓冲区
            // 注意：SharedBuffer 需要手动释放，但在 PostSharedBufferToScript 后，
            // WebView2 会在脚本接收并处理后管理其生命周期，或者我们需要在适当时候释放？
            // 根据文档，PostSharedBufferToScript 共享了缓冲区的所有权。
            // 但为了安全，通常应该在前端处理完发送回执后再释放，或者依赖 WebView2 的生命周期管理。
            // 简单起见，这里创建并发送。如果频繁调用，可能会有资源压力，但在 Demo 中可行。
            // 更好的做法是重用 SharedBuffer (RingBuffer 模式)。

            using var sharedBuffer = _environment.CreateSharedBuffer((ulong)data.Length);

            using (var stream = sharedBuffer.OpenStream())
            {
                stream.Write(data, 0, data.Length);
            }

            var additionalData = JsonSerializer.Serialize(new { width, height });

            _webView.CoreWebView2.PostSharedBufferToScript(
                sharedBuffer,
                CoreWebView2SharedBufferAccess.ReadOnly,
                additionalData);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"发送共享缓冲区失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 【科学方案三】清除WebView2缓存
    /// </summary>
    public async Task ClearCacheAsync()
    {
        if (!_isInitialized || _webView.CoreWebView2 is null)
        {
            throw new InvalidOperationException("WebView2 尚未初始化");
        }

        await ClearDiskCacheAsync(_webView.CoreWebView2);
    }

    private static async Task ClearDiskCacheAsync(CoreWebView2 core)
    {
        try
        {
            await core.Profile.ClearBrowsingDataAsync(CoreWebView2BrowsingDataKinds.DiskCache);

            System.Diagnostics.Debug.WriteLine("[WebView2Host] 缓存已清除");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[WebView2Host] 清除缓存失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 【科学方案三】强制刷新（清除缓存并重新加载）
    /// </summary>
    public async Task ForceReloadAsync()
    {
        if (!_isInitialized || _webView.CoreWebView2 is null)
        {
            throw new InvalidOperationException("WebView2 尚未初始化");
        }

        // 1. 清除缓存
        await ClearCacheAsync();

        // 2. 重新加载当前页面（不使用缓存）
        _webView.CoreWebView2.Reload();

        System.Diagnostics.Debug.WriteLine("[WebView2Host] 强制刷新完成");
    }

    /// <summary>
    /// 【科学方案二】生成CSS版本号
    /// </summary>
    private string GenerateCssVersion()
    {
#if DEBUG
        // DEBUG模式：使用时间戳，确保每次启动都不同
        return DateTime.Now.Ticks.ToString();
#else
        // RELEASE模式：使用程序集版本号
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        return version?.ToString() ?? "1.0.0";
#endif
    }

    /// <summary>
    /// 异步释放资源。
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;

        // 取消事件订阅
        if (_webView.CoreWebView2 is not null)
        {
            _webView.CoreWebView2.WebMessageReceived -= OnWebMessageReceived;
        }

        // 释放 WebView2 控件
        _webView.Dispose();

        await Task.CompletedTask;
    }
}
