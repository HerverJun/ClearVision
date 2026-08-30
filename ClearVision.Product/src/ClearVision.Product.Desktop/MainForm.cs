// MainForm.cs
// 初始化菜单栏
// 作者：蘅芜君

using System.Diagnostics;
using System.Text.Json;
using System.Windows.Forms;
using ClearVision.Product.Desktop.Configuration;
using ClearVision.Product.Desktop.Triggers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Web.WebView2.WinForms;

namespace ClearVision.Product.Desktop;

/// <summary>
/// 主窗体，集成 WebView2 控件。
/// </summary>
public partial class MainForm : Form
{
    private readonly WebView2 _webView;
    private readonly WebView2Host _webView2Host;
    private readonly Handlers.WebMessageHandler? _messageHandler;
    private readonly EnterPhotoelectricTriggerInputService? _triggerInputService;
    private bool _allowClose;
    private bool _closingInProgress;
    private bool _disposedWebViewHost;

    public MainForm()
    {
        InitializeComponent();

        // 创建 WebView2 控件
        _webView = new WebView2
        {
            Dock = DockStyle.Fill
        };
        Controls.Add(_webView);

        // 创建 WebView2 宿主
        _messageHandler = Program.ServiceProvider?.GetService<Handlers.WebMessageHandler>();
        _triggerInputService = Program.ServiceProvider?.GetService<EnterPhotoelectricTriggerInputService>();
        var studioOptions = Program.ServiceProvider?.GetService<IOptions<StudioOptions>>()?.Value
            ?? new StudioOptions();
        _webView2Host = new WebView2Host(_webView, studioOptions);

        // 窗体加载时初始化 WebView2
        Load += MainForm_Load;
        FormClosing += MainForm_FormClosing;
    }

    private async void MainForm_Load(object? sender, EventArgs e)
    {
        try
        {
            // 初始化菜单栏（在WebView2初始化之前）
            InitializeMenu();

            await _webView2Host.InitializeAsync();

            // S4-006: 初始化 WebMessage 处理器，挂载到 WebView2
            if (_webView.CoreWebView2 != null)
            {
                _messageHandler?.Initialize(_webView);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"WebView2 初始化失败: {ex.Message}",
                "错误",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        _triggerInputService?.AttachWindow(Handle);
    }

    protected override void WndProc(ref Message m)
    {
        _triggerInputService?.HandleWindowMessage(m.Msg, m.LParam);
        base.WndProc(ref m);
    }

    /// <summary>
    /// 初始化菜单栏
    /// </summary>
    private void InitializeMenu()
    {
        var menuStrip = new MenuStrip();

        // 视图菜单
        var viewMenu = new ToolStripMenuItem("视图");

        // 【科学方案三】强制刷新菜单项
        var refreshMenuItem = new ToolStripMenuItem("强制刷新 (清除缓存)", null, async (s, e) =>
        {
            try
            {
                await _webView2Host.ForceReloadAsync();
                MessageBox.Show("缓存已清除，页面已刷新", "完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"刷新失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        });
        refreshMenuItem.ShortcutKeys = Keys.F5 | Keys.Control;
        refreshMenuItem.ToolTipText = "Ctrl+F5 强制刷新";

        viewMenu.DropDownItems.Add(refreshMenuItem);

        // 开发工具菜单项（仅DEBUG模式）
#if DEBUG
        viewMenu.DropDownItems.Add(new ToolStripSeparator());

        var devToolsMenuItem = new ToolStripMenuItem("开发者工具", null, (s, e) =>
        {
            _webView.CoreWebView2?.OpenDevToolsWindow();
        });
        devToolsMenuItem.ShortcutKeys = Keys.F12;
        viewMenu.DropDownItems.Add(devToolsMenuItem);

        // 清除缓存菜单项
        var clearCacheMenuItem = new ToolStripMenuItem("清除缓存", null, async (s, e) =>
        {
            try
            {
                await _webView2Host.ClearCacheAsync();
                MessageBox.Show("缓存已清除", "完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"清除缓存失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        });
        viewMenu.DropDownItems.Add(clearCacheMenuItem);
#endif

        menuStrip.Items.Add(viewMenu);

        // 将菜单栏添加到窗体
        Controls.Add(menuStrip);
        MainMenuStrip = menuStrip;
    }

    private async void MainForm_FormClosing(object? sender, FormClosingEventArgs e)
    {
        if (_allowClose)
        {
            await DisposeWebViewHostAsync();
            return;
        }

        if (_closingInProgress)
        {
            e.Cancel = true;
            return;
        }

        e.Cancel = true;
        _closingInProgress = true;

        try
        {
            var timeout = e.CloseReason == CloseReason.WindowsShutDown
                ? TimeSpan.FromSeconds(2)
                : TimeSpan.FromSeconds(5);
            var flushed = await FlushWorkspaceBeforeCloseAsync(timeout);
            if (!flushed && e.CloseReason != CloseReason.WindowsShutDown)
            {
                var choice = MessageBox.Show(
                    "Plan 修改尚未保存，仍要退出吗？",
                    "ClearVision",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning,
                    MessageBoxDefaultButton.Button2);
                if (choice != DialogResult.Yes)
                {
                    _closingInProgress = false;
                    return;
                }
            }

            _allowClose = true;
            BeginInvoke(new Action(Close));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MainForm] Shutdown coordination failed: {ex}");
            var choice = MessageBox.Show(
                "Plan 修改尚未保存，仍要退出吗？",
                "ClearVision",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);
            if (choice == DialogResult.Yes)
            {
                _allowClose = true;
                BeginInvoke(new Action(Close));
                return;
            }

            _closingInProgress = false;
        }
    }

    private async Task<bool> FlushWorkspaceBeforeCloseAsync(TimeSpan timeout)
    {
        try
        {
            var stopwatch = Stopwatch.StartNew();
            var operationId = Guid.NewGuid().ToString("N");
            var startResult = await ExecuteScriptWithTimeoutAsync(
                BuildAiWorkspaceFlushStartScript(operationId, "host_close"),
                timeout);
            if (!ParseBooleanScriptResult(startResult))
            {
                return false;
            }

            while (stopwatch.Elapsed < timeout)
            {
                var remaining = timeout - stopwatch.Elapsed;
                var rawStatus = await ExecuteScriptWithTimeoutAsync(
                    BuildAiWorkspaceFlushStatusScript(operationId),
                    remaining);
                var status = ParseAiWorkspaceFlushStatus(rawStatus);
                if (status.IsTerminal)
                {
                    return status.Succeeded;
                }

                var delay = TimeSpan.FromMilliseconds(Math.Min(
                    50,
                    Math.Max(1, (timeout - stopwatch.Elapsed).TotalMilliseconds)));
                await Task.Delay(delay);
            }

            return false;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MainForm] AI workspace flush before close failed: {ex}");
            return false;
        }
    }

    private async Task<string?> ExecuteScriptWithTimeoutAsync(string script, TimeSpan timeout)
    {
        if (timeout <= TimeSpan.Zero)
        {
            return null;
        }

        var scriptTask = _webView2Host.ExecuteScriptAsync(script);
        var completed = await Task.WhenAny(scriptTask, Task.Delay(timeout));
        return completed == scriptTask
            ? await scriptTask
            : null;
    }

    internal static string BuildAiWorkspaceFlushStartScript(string operationId, string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationId);
        var operationIdJson = JsonSerializer.Serialize(operationId);
        var reasonJson = JsonSerializer.Serialize(string.IsNullOrWhiteSpace(reason) ? "host_close" : reason);
        return $$"""
            (() => {
                const operationId = {{operationIdJson}};
                const reason = {{reasonJson}};
                const storeName = '__clearVisionAiWorkspaceFlushResults';
                const store = window[storeName] || (window[storeName] = {});
                store[operationId] = { status: 'pending', value: false, error: '', startedAt: Date.now() };
                try {
                    const flush = window.__clearVisionFlushAiPanelWorkspace;
                    if (typeof flush !== 'function') {
                        store[operationId] = {
                            status: 'completed',
                            value: true,
                            error: '',
                            startedAt: Date.now()
                        };
                        return true;
                    }

                    Promise.resolve(flush(reason))
                        .then(value => {
                            store[operationId] = {
                                status: 'completed',
                                value: value === true,
                                error: '',
                                startedAt: Date.now()
                            };
                        })
                        .catch(error => {
                            store[operationId] = {
                                status: 'failed',
                                value: false,
                                error: String(error && (error.message || error) || 'unknown'),
                                startedAt: Date.now()
                            };
                        });
                    return true;
                } catch (error) {
                    store[operationId] = {
                        status: 'failed',
                        value: false,
                        error: String(error && (error.message || error) || 'unknown'),
                        startedAt: Date.now()
                    };
                    return false;
                }
            })()
            """;
    }

    internal static string BuildAiWorkspaceFlushStatusScript(string operationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationId);
        var operationIdJson = JsonSerializer.Serialize(operationId);
        return $$"""
            (() => {
                const operationId = {{operationIdJson}};
                const store = window.__clearVisionAiWorkspaceFlushResults || {};
                const state = store[operationId] || { status: 'missing', value: false, error: '' };
                if (state.status === 'completed' || state.status === 'failed' || state.status === 'missing') {
                    delete store[operationId];
                }
                return state;
            })()
            """;
    }

    internal static bool ParseBooleanScriptResult(string? rawResult)
    {
        if (string.IsNullOrWhiteSpace(rawResult))
        {
            return false;
        }

        try
        {
            return JsonSerializer.Deserialize<bool>(rawResult);
        }
        catch (JsonException)
        {
            return string.Equals(rawResult.Trim(), "true", StringComparison.OrdinalIgnoreCase);
        }
    }

    internal static AiWorkspaceFlushStatus ParseAiWorkspaceFlushStatus(string? rawResult)
    {
        if (string.IsNullOrWhiteSpace(rawResult))
        {
            return AiWorkspaceFlushStatus.Failed;
        }

        try
        {
            using var document = JsonDocument.Parse(rawResult);
            var root = document.RootElement;
            if (root.ValueKind == JsonValueKind.True)
            {
                return AiWorkspaceFlushStatus.Success;
            }

            if (root.ValueKind == JsonValueKind.False)
            {
                return AiWorkspaceFlushStatus.Failed;
            }

            if (root.ValueKind != JsonValueKind.Object)
            {
                return AiWorkspaceFlushStatus.Failed;
            }

            var status = root.TryGetProperty("status", out var statusElement)
                ? statusElement.GetString()
                : string.Empty;
            status = status?.Trim().ToLowerInvariant();
            if (status == "pending")
            {
                return AiWorkspaceFlushStatus.Pending;
            }

            if (status == "completed")
            {
                var succeeded = root.TryGetProperty("value", out var valueElement) &&
                    valueElement.ValueKind == JsonValueKind.True;
                return succeeded
                    ? AiWorkspaceFlushStatus.Success
                    : AiWorkspaceFlushStatus.Failed;
            }

            return AiWorkspaceFlushStatus.Failed;
        }
        catch (JsonException)
        {
            return AiWorkspaceFlushStatus.Failed;
        }
    }

    internal readonly record struct AiWorkspaceFlushStatus(bool IsTerminal, bool Succeeded)
    {
        public static AiWorkspaceFlushStatus Pending { get; } = new(false, false);
        public static AiWorkspaceFlushStatus Success { get; } = new(true, true);
        public static AiWorkspaceFlushStatus Failed { get; } = new(true, false);
    }

    private async Task DisposeWebViewHostAsync()
    {
        if (_disposedWebViewHost)
        {
            return;
        }

        _disposedWebViewHost = true;
        try
        {
            _messageHandler?.Dispose();
            await _webView2Host.DisposeAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MainForm] Shutdown cleanup failed: {ex}");
        }
    }

    private void InitializeComponent()
    {
        SuspendLayout();
        //
        // MainForm
        //
        AutoScaleDimensions = new SizeF(7F, 17F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(1280, 720);
        Name = "MainForm";
        StartPosition = FormStartPosition.CenterScreen;
        Text = "ClearVision Product";
        ResumeLayout(false);
    }
}
